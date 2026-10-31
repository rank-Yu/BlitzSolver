# Sts2Solver 质量语料

本目录保存 Sts2Solver 差分回归与协调器 Off/On 质量对照共用的原有 120 个生成牌组，以及两份固定女王牌组和一份固定沙漏牌组，共 123 个输入。默认质量评测由 `default-roots.txt` 指定，共 **108 场**：105 个原生成牌组、2 场女王和1 场沙漏。原生成牌组中第三幕首领的15场（各角色017、020、023）不再参加默认质量跑分；输入继续供历史复跑及 Golden 一致性检查使用。原生成牌组每个子目录是一份 `spec.json` + `request.json`，`request.json` 的 `generatedScenarioPath` 指向本目录内的规格。

- **组成**：五个角色各 24 个牌组（`<角色>-000` … `<角色>-023`）。序号 000–007 为第一幕，008–015 为第二幕，016–023 为第三幕；遭遇种类按序号 `i % 3` 取普通、精英、首领，合计各 40。
- **生成口径**：生成器在 CombatSolver 仓库，固定提交的 survey 说明见 [FlashRegression README](https://github.com/Torch1230/CombatSolver/blob/a62f1850c8586e5951e222eecb0dd34231e76d8a/tools/search/FlashRegression/README.md#survey)。种子为 `FLASH-QUALITY-<角色>-<NNN>`；这个名字保留不改，因为它是生成牌组的随机种子，改了就是另一批牌组。固定进阶 10，`includeStartingDeck`/`includeStartingRelics`/`includeAscendersBane` 为真；额外角色卡 `6 + i % 7` 张（升级 1 级），遗物 `2 + i % 4` 个；无色牌 `(i // 3 + 1) % 3` 张、药水 `(i // 3) % 3` 瓶，这两项不随遭遇种类同步变化。生成多少收多少，不按当前 Sts2Solver 的准入规则筛选，没有重复规格；五个角色各跑一次 `survey --out coverage/corpora/flash-quality` 即逐字节重建。`coverage/corpora/flash-quality` 是 CombatSolver 仓库内的旧路径。
- **用途**：`flash_regression.py diff` 默认包含本目录（每根 48 条游走），`coordinator` 默认读取本目录（Medium、宽度组合、120 秒预算，关闭与开启模式各跑一次，各按实际结果计入，8 个并行进程）。这些命令属于 CombatSolver 的旧工具，只说明原有 120 根的复跑方式，不适用于追加的固定牌组。
- **复跑**：

  ```bash
  # 以下路径与命令都在 CombatSolver 仓库内，纯文本说明，不在本仓库执行。
  python3 -B tools/search/FlashRegression/flash_regression.py diff --label quality-diff --only quality --jobs 8
  python3 -B tools/search/FlashRegression/flash_regression.py coordinator --label quality --jobs 8
  python3 -B tools/search/FlashRegression/flash_regression.py coordinator --label quality-one --ids SILENT-007
  ```

- **记录**：结果写入 `.local/flash-regression/`（CombatSolver 的旧本地路径）；本目录只保留输入。此前的 100 根（两位编号种子）、79 根对照以及 `flash-decks`、`flash-act3` 两份早期语料的数字保留在测试记录与归档卷中；`flash-decks`、`flash-act3` 是 CombatSolver 时期的语料名。


## 本仓库整场回归

在仓库根目录运行：

    dotnet build tests/Differential/Differential.csproj -c Release
    dotnet artifacts/bin/Differential/release/Differential.dll --offline-combat --search-tier Medium --jobs 4 --out .local/quality

默认读取本目录的 `default-roots.txt`，共108根，不要求新根先有 Golden 元数据。可先用以下命令检查名单而不启动战斗：

    dotnet artifacts/bin/Differential/release/Differential.dll --list-quality-roots

也可用 `--root quality-SILENT-QUEEN-CG7UX7V5XZS9` 或 `--roots-file` 显式指定集合（包括已移出默认评测的历史根）。自定义语料目录没有名单时，按其 `request.json` 枚举。结果和实验记录保存在本地，不提交到本目录。

两份 SILENT-QUEEN-* 和 SILENT-AEONGLASS-CG7UX7V5XZS9 请求使用固定的已有牌组、遗物、附魔、生命与金币，走原版开战流程；它们是同配置重建开局，不是原存档的完整随机数状态。ownedLoadoutForTest 要求 clearRunDeck 和 preserveNativeCombatStateForTest，先恢复牌组再恢复无获取效果的遗物，避免对已拥有牌再次触发升级；生命和金币在进入战斗前设置。原有生成牌组的构建顺序不变。

固定回归的 expectations.json 指定档位、胜利要求和最低剩余生命；中档整场结果未达要求或出现部署偏差时返回失败，其他档位不套用中档阈值。默认统计应分别列出105个原生成牌组和3个固定牌组，并注明总数108；历史120/122场数据不能直接混用分母。沙漏使用种子CG7UX7V5XZS9、进阶10、75/75生命、35金币、无药、34张牌、壶铃举重3次、余像迅捷2；与保存的用户配置一致。

质量期望用于提示已知路线的退步，不代替维护者决定是否接受策略取舍。维护者确认接受的质量变化应同步更新期望，并把原版核对与前后对照记录保存在本地。
