#!/usr/bin/env python3
"""启动器退出合同；使用假游戏，不需要游戏程序集或 dotnet。"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest


LAUNCHER = Path(__file__).resolve().with_name("run-headless.sh")
FAKE_GAME = r'''#!/usr/bin/env python3
import json, os, sys, time
from pathlib import Path
root = Path(os.environ["STS2_SOLVER_TEST_DIR"])
case = json.loads(next((root / "requests").glob("*.json")).read_text())
log = Path(sys.argv[sys.argv.index("--log-file") + 1])
log.write_text("[Sts2Solver] INIT id=Sts2Solver\n")
if not case.get("missing_done"):
    (root / "results" / "done.json").write_text(json.dumps({
        "schemaVersion": 1, "ok": int(not case.get("failed")),
        "failed": int(bool(case.get("failed"))), "results": [], "totalMs": 1
    }))
time.sleep(case.get("delay", 1))
(root / "normal-exit.marker").write_text("finished cleanup")
sys.exit(case.get("exit_code", 0))
'''


class HeadlessExitTests(unittest.TestCase):
    def run_case(self, case):
        with tempfile.TemporaryDirectory(prefix="headless-exit-") as directory:
            root = Path(directory)
            for name in ["game", "ritsu", "mod", "seed/SlayTheSpire2/default/1"]:
                (root / name).mkdir(parents=True)
            game = root / "game/SlayTheSpire2"
            game.write_text(FAKE_GAME)
            game.chmod(0o755)
            for name in ["ritsu/STS2-RitsuLib.dll", "ritsu/mod_manifest.json",
                         "mod/Sts2Solver.dll", "mod/Sts2Solver.json"]:
                (root / name).write_text("{}")
            (root / "seed/SlayTheSpire2/default/1/settings.save").write_text("{}")
            request = root / "case.json"
            request.write_text(json.dumps(case))
            results = root / "results"
            command = ["bash", str(LAUNCHER), "--instance", "test",
                       "--instance-root", str(root / "instances"),
                       "--game-root", str(root / "game"),
                       "--ritsu-root", str(root / "ritsu"),
                       "--mod-dir", str(root / "mod"),
                       "--seed-data", str(root / "seed"),
                       "--request", str(request), "--results", str(results),
                       "--timeout-seconds", "3", "--shutdown-timeout-seconds", "2"]
            result = subprocess.run(command, capture_output=True, text=True, timeout=15)
            marker = (root / "instances/test/run/normal-exit.marker").exists()
            copied = (results / "done.json").exists()
            return result.returncode, result.stdout + result.stderr, marker, copied

    def test_waits_for_clean_exit(self):
        code, log, marker, copied = self.run_case({})
        self.assertEqual(code, 0, log)
        self.assertTrue(marker, log)
        self.assertTrue(copied, log)
        self.assertIn("HEADLESS_EXIT code=0", log)

    def test_already_exited_success_is_accepted(self):
        code, log, marker, copied = self.run_case({"delay": 0})
        self.assertEqual(code, 0, log)
        self.assertTrue(marker, log)
        self.assertTrue(copied, log)
        self.assertIn("HEADLESS_EXIT code=0", log)

    def test_completed_results_do_not_hide_process_failure(self):
        code, log, marker, copied = self.run_case({"exit_code": 139})
        self.assertNotEqual(code, 0, log)
        self.assertTrue(marker, log)
        self.assertTrue(copied, log)
        self.assertIn("HEADLESS_EXIT code=139", log)

    def test_failed_tests_remain_failed(self):
        for exit_code in [0, 1]:
            with self.subTest(exit_code=exit_code):
                code, log, marker, copied = self.run_case({"failed": True, "exit_code": exit_code})
                self.assertNotEqual(code, 0, log)
                self.assertTrue(marker, log)
                self.assertTrue(copied, log)

    def test_shutdown_timeout_is_failure(self):
        code, log, marker, copied = self.run_case({"delay": 30})
        self.assertNotEqual(code, 0, log)
        self.assertFalse(marker, log)
        self.assertTrue(copied, log)
        self.assertIn("仍未退出", log)

    def test_exit_without_results_is_failure(self):
        code, log, marker, copied = self.run_case({"missing_done": True, "delay": 0})
        self.assertNotEqual(code, 0, log)
        self.assertTrue(marker, log)
        self.assertFalse(copied, log)


if __name__ == "__main__":
    unittest.main()
