#!/usr/bin/env python3
"""Compare an offline whole-combat result directory with the CombatSolver q120 baseline.

The output format is the same table as the coordinator's q120-table.py: totals plus one row per
character, act and room type, then the roots whose win/loss differs. It is a repository entry point
so the same report can be regenerated after any search change.

Usage:
  tools/testing/offline-combat-table.py --results .local/offline-combat/default \
      --baseline ~/ss/solver/.local/q120/baseline.tsv

The baseline is a tab-separated file with columns:
  root  win|death  turns  playerHp  playerMaxHp  searches
"""
import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", required=True, type=Path,
                        help="offline --out directory (one <root>/full-combat.json per root)")
    parser.add_argument("--baseline", required=True, type=Path,
                        help="CombatSolver baseline.tsv")
    parser.add_argument("--corpora", default=Path("tests/corpora"), type=Path,
                        help="corpus root containing quality/<id>/spec.json")
    parser.add_argument("--keys", action="store_true",
                        help="only print the full-combat.json keys and exit")
    parser.add_argument("--label", default="offline",
                        help="label used in the heading")
    return parser.parse_args()


def result_path(results: Path, rid: str) -> Path:
    for name in (f"quality-{rid}", rid):
        candidate = results / name / "full-combat.json"
        if candidate.exists():
            return candidate
    return results / f"quality-{rid}" / "full-combat.json"


def won(result: dict | None) -> bool:
    return result is not None and result.get("outcome") == "victory"


def main() -> int:
    args = parse_args()
    baseline: dict[str, dict] = {}
    for line in args.baseline.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        fields = line.split("\t")
        if len(fields) != 6:
            print(f"invalid baseline line: {line}", file=sys.stderr)
            return 2
        rid, outcome, turn, hp, maxhp, searches = fields
        baseline[rid] = dict(win=outcome == "win", turn=int(turn), hp=int(hp),
                             maxhp=int(maxhp), searches=int(searches))

    rows = []
    for rid, expected in sorted(baseline.items()):
        spec_path = args.corpora / "quality" / rid / "spec.json"
        spec = json.loads(spec_path.read_text())
        path = result_path(args.results, rid)
        result = json.loads(path.read_text()) if path.exists() else None
        rows.append((rid, spec, expected, result))

    missing = [rid for rid, _, _, result in rows if result is None]
    print(f"{args.label}: results {len(rows) - len(missing)} missing {len(missing)}")
    if missing:
        print(f"  missing: {missing[:10]}")
    if args.keys:
        keys = sorted({key for *_, result in rows if result for key in result})
        print(f"full-combat.json keys: {keys}")
        return 0

    groups: dict[str, list] = defaultdict(list)
    for rid, spec, expected, result in rows:
        character = rid.split("-")[0]
        groups["合计"].append((rid, expected, result))
        groups[character].append((rid, expected, result))
        groups[f"第{spec['actIndex'] + 1}幕"].append((rid, expected, result))
        groups[spec["encounterKind"]].append((rid, expected, result))

    print("| 分组 | 场数 | Sts2Solver 胜 | CombatSolver 胜 | 两边都胜时 HP 差合计 | 搜索次数 Sts2Solver / CS |")
    print("| --- | ---: | ---: | ---: | ---: | ---: |")
    order = ["合计", "IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER",
             "第1幕", "第2幕", "第3幕", "Monster", "Elite", "Boss"]
    for group in order + [key for key in groups if key not in order]:
        items = groups.get(group)
        if not items:
            continue
        solver_wins = sum(won(result) for _, _, result in items)
        combat_wins = sum(expected["win"] for _, expected, _ in items)
        hp_delta = sum(result["playerHp"] - expected["hp"]
                       for _, expected, result in items if won(result) and expected["win"])
        solver_searches = sum(result.get("searches", 0) for _, _, result in items if result)
        combat_searches = sum(expected["searches"] for _, expected, _ in items)
        print(f"| {group} | {len(items)} | {solver_wins} | {combat_wins} | "
              f"{hp_delta:+d} | {solver_searches} / {combat_searches} |")

    print()
    print("胜负不同的根：")
    for rid, spec, expected, result in rows:
        if won(result) != expected["win"]:
            print(f"  {rid} act{spec['actIndex'] + 1} {spec['encounterKind']}: "
                  f"Sts2Solver {result and result.get('outcome')} "
                  f"turn={result and result.get('turns')} hp={result and result.get('playerHp')} | "
                  f"CS {'win' if expected['win'] else 'death'} "
                  f"turn={expected['turn']} hp={expected['hp']}")

    print()
    print("异常（非胜负结果、偏差或部署失败）：")
    for rid, _, _, result in rows:
        if (result is None or result.get("outcome") not in ("victory", "defeat")
                or result.get("deviations") or result.get("deploymentFailures")):
            keys = ("status", "outcome", "deviations", "deploymentFailures", "error")
            print(f"  {rid}: {result and {key: result.get(key) for key in keys}}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
