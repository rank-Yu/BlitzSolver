# 冻结 fixture 输入

本目录保存 `tests/Golden/data/d1` 的 290 个 fixture 输入，供离线宿主
（`tests/Differential`）建出导出快照时的原生战斗状态。

## 来源

- 源仓库：CombatSolver
- 源提交：`a62f1850`
- 源路径：`coverage/fixtures/cards/flash-<root>.json`
- 复制目标：`tests/corpora/fixtures/<root>.json`
- 文件名去掉 `flash-` 前缀；`<root>` 与 `tests/Golden/data/d1/<root>` 一一对应。

290 个文件的内容**逐字节未改**。复制后已用 SHA-256 与源文件逐个比对；源路径由
`tests/Golden/data/d1/<root>/meta.json` 的 `input.fixture` 字段确认。

另外复制了 `coverage/fixtures/cards/specs/flash-qf-misery-roster.spec.json` 到
`specs/`。`qf-misery-roster` 是唯一一个带 `generatedScenarioPath` 的 fixture，源 JSON
按原字节指向该 spec；离线宿主按文件名把它解析到本目录。

## 本批不执行的部分

- 13 个 fixture 带 `flashPrefixActions` 或 `flashPrefixPlan`；本批只建前缀之前的原生状态，
  不执行前缀。清单见 `.local/tool-tasks/solver/M2a0-report.md`。
- `ts-a-toasty`、`ts-b-toolbox`、`qf2-gradient-failure` 会各产生额外的回合准备（turn-setup）
  根；本批只建每个 fixture 一个普通可操作根，额外 8 个根留给 M2a-5。
