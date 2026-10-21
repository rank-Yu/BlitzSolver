# Flash 质量语料

本目录保存 Flash 协调器 Off/On 质量对照使用的 100 个生成牌组。每个子目录是一份 `spec.json` + `request.json`，`request.json` 的 `generatedScenarioPath` 指向本目录内的规格。

- **组成**：五个角色各 20 个牌组（`<角色>-00` … `<角色>-19`）。序号 00–06 为第一幕，07–13 为第二幕，14–19 为第三幕；遭遇种类按序号 `i % 3` 取普通、精英、首领，合计普通 35、精英 35、首领 30。
- **生成口径**：`flash_regression.py survey --count 20 --act-blocks 7,7,6 --seed-prefix FLASH-QUALITY --vary-extras`，种子为 `FLASH-QUALITY-<角色>-<NN>`。固定进阶 10，`includeStartingDeck`/`includeStartingRelics`/`includeAscendersBane` 为真；额外角色卡 `6 + i % 7` 张（升级 1 级），遗物 `2 + i % 4` 个；无色牌 `(i // 3 + 1) % 3` 张、药水 `(i // 3) % 3` 瓶，这两项不随遭遇种类同步变化，每种遭遇都有 0、1、2 瓶药水的牌组。生成多少收多少，不按 Flash 准入筛选，没有重复规格。
- **用途**：`tools/search/FlashRegression/flash_regression.py coordinator` 默认读取本目录，每根在 Medium、宽度组合、120 秒预算下分别跑关闭与开启模式，各按实际结果计入。它不进入差分回归；差分输入是 `coverage/fixtures/cards/flash-*.json`、[flash-decks](../flash-decks/README.md) 与 [flash-act3](../flash-act3/README.md)。
- **复跑**：

  ```bash
  python3 -B tools/search/FlashRegression/flash_regression.py coordinator --label quality --jobs 8
  python3 -B tools/search/FlashRegression/flash_regression.py coordinator --label quality-one --ids SILENT-15
  ```

  当前口径是 8 个并行进程，并行对耗时的影响见[工具说明](../../../tools/search/FlashRegression/README.md#已知边界)。
- **记录**：结果写入 `.local/flash-regression/`；本目录只保留输入。替换牌组时同步 `request.json` 内的仓库相对路径。2026-10-21 之前的 79 根对照（四角色 46 个 RQ 牌组 + `flash-decks` 中 33 个铁甲战士牌组）的数字保留在测试记录与归档卷中。
