# Blitz 质量语料

本目录保存 Blitz 差分回归与协调器 Off/On 质量对照共用的 120 个生成牌组。每个子目录是一份 `spec.json` + `request.json`，`request.json` 的 `generatedScenarioPath` 指向本目录内的规格。

- **组成**：五个角色各 24 个牌组（`<角色>-000` … `<角色>-023`）。序号 000–007 为第一幕，008–015 为第二幕，016–023 为第三幕；遭遇种类按序号 `i % 3` 取普通、精英、首领，合计各 40。
- **生成口径**：`flash_regression.py survey --character <角色>`（固定口径，见[工具说明](../../../tools/search/BlitzRegression/README.md#survey)），种子为 `FLASH-QUALITY-<角色>-<NNN>`。固定进阶 10，`includeStartingDeck`/`includeStartingRelics`/`includeAscendersBane` 为真；额外角色卡 `6 + i % 7` 张（升级 1 级），遗物 `2 + i % 4` 个；无色牌 `(i // 3 + 1) % 3` 张、药水 `(i // 3) % 3` 瓶，这两项不随遭遇种类同步变化。生成多少收多少，不按 Blitz 准入筛选，没有重复规格；五个角色各跑一次 `survey --out coverage/corpora/flash-quality` 即逐字节重建。
- **用途**：`flash_regression.py diff` 默认包含本目录（每根 48 条游走），`coordinator` 默认读取本目录（Medium、宽度组合、120 秒预算，关闭与开启模式各跑一次，各按实际结果计入，8 个并行进程）。
- **复跑**：

  ```bash
  python3 -B tools/search/BlitzRegression/flash_regression.py diff --label quality-diff --only quality --jobs 8
  python3 -B tools/search/BlitzRegression/flash_regression.py coordinator --label quality --jobs 8
  python3 -B tools/search/BlitzRegression/flash_regression.py coordinator --label quality-one --ids SILENT-007
  ```

- **记录**：结果写入 `.local/flash-regression/`；本目录只保留输入。此前的 100 根（两位编号种子）、79 根对照以及 `flash-decks`、`flash-act3` 两份早期语料的数字保留在测试记录与归档卷中。
