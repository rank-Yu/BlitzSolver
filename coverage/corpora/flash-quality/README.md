# 四角色质量语料

本目录保存 Flash 协调器质量对照使用的 46 个四角色生成牌组。每个子目录是一份
`spec.json` + `request.json`，`request.json` 的 `generatedScenarioPath` 指向本目录内的规格。

- **来源**：`.local/tool-tasks/flash-search/inputs/rq-set/` 的 46 个根，目录名沿用来源选择时的
  `<角色>-C-<NN>` / `<角色>-V-<NN>`。底层种子是 `FLASH-DECK-<角色>-<NN>`；46 个根对应 44 个
  唯一规格，`NECROBINDER-C-00`/`NECROBINDER-V-00` 与 `REGENT-C-07`/`REGENT-V-07` 是同一规格的
  两份副本，仍作为独立根保留以复现协调器计数。
- **生成口径**：与 `deck-survey.sh` 相同。固定进阶 10，
  `includeStartingDeck`/`includeStartingRelics`/`includeAscendersBane` 为真；
  `characterCards.count = 6 + (i % 7)`、`colorlessCards.count = i % 3`、
  `relics.count = 2 + i % 4`、`potions.count = i % 3`；按 `i % 2` 取第一/二幕，按 `i % 3`
  取普通/精英/首领。五个角色中本目录只收静默猎手、故障机器人、储君与亡灵契约师；铁甲战士的
  33 个第一/二幕牌组在 [flash-decks](../flash-decks/README.md)。
- **用途**：`tools/search/FlashRegression/flash_regression.py coordinator` 默认读取本目录，
  加上 `flash-decks` 的 33 个铁甲战士根，共 79 根；每根在 Medium、宽度组合、120 秒预算下跑
  关闭/开启模式对照。它不进入 493 根差分回归；差分入口仍以 `flash-decks` 与固定根为准。
- **复跑**：

  ```bash
  python3 -B tools/search/FlashRegression/flash_regression.py coordinator --label coord
  python3 -B tools/search/FlashRegression/flash_regression.py coordinator \
    --label coord-one --ids SILENT-C-13 --budget-ms 120000
  ```

  关闭模式基线可用 `--off-baseline <旧结果目录>` 复用，只重跑开启模式。
- **记录**：结果写入 `.local/flash-regression/`；本目录只保留输入。替换牌组时同步
  `request.json` 内的仓库相对路径。

当前质量数字与逐根结论见 [Flash 测试记录](../../../docs/flash-search/testing.md) 2.1；本目录不维护
流水账。
