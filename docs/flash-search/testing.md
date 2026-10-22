# Flash 测试记录

[返回 Flash 入口](README.md)

本文记录 Flash 的可复跑验证入口与当前证据。方法定义见 [准入与校验](admission-and-verification.md)。

## 1. 复跑入口

已提交的入口是 [Flash 回归工具](../../tools/search/FlashRegression/README.md)：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py <子命令> [参数]
```

工具负责 Release 构建与 `OfflineSearchHarness` 构建，`diff`/`scan` 默认再运行 K0；它调用现有宿主并把结果写到
`.local/flash-regression/<label>/`。旧的 `.local/tool-tasks/flash-search/*.sh` 不再作为复跑入口。

纯内核合同（K0）与两次构建：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py build
```

差分探针（K1 单步穷举、K2 随机游走）的全量根集合是 410 根：290 个 `coverage/fixtures/cards/flash-*.json` 固定根（默认每根 32 条游走）与 120 个 `coverage/corpora/flash-quality/` 质量牌组（默认每根 48 条游走）：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py diff --label flash-diff --jobs 8
python3 -B tools/search/FlashRegression/flash_regression.py diff \
  --label one --only fixtures --root flash-dx5-pen-nib-sly
```

`--only fixtures`、`--only quality` 只跑其中一组。

最终候选路线扫描（同一根集合，Flash 求解后把排序前 N 条最终候选交现有引擎逐条核对）：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py scan --label flash-scan --candidates 8 --jobs 8
```

120 根协调器质量对照（[质量语料](../../coverage/corpora/flash-quality/README.md)，8 个并行进程）：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py coordinator --label flash-coord --jobs 8
python3 -B tools/search/FlashRegression/flash_regression.py table .local/flash-regression/flash-coord --corpus coverage/corpora/flash-quality
```

`coordinator` 结束时自动写出同一张表（`TABLE-GROUPS.txt`）；`table` 只读已有结果，用于复算或换语料。

协调器的关闭模式基线可用 `--off-baseline <旧结果目录>` 复用；只复跑一根时用
`--ids <目录名>`，例如 `--ids SILENT-007`。

需要直接调试宿主时，底层单根命令仍是：

```bash
OFFLINE_HARNESS_FLASH_DIFF=1 OFFLINE_HARNESS_FLASH_DIFF_WALKS=32 \
  dotnet .local/tool-build/OfflineSearchHarness/bin/Release/net9.0/OfflineSearchHarness.dll \
  --character IRONCLAD --encounter NIBBITS_NORMAL --seed FLASH1 --milestone M1 --out <输出目录>
```

PowerShell 用 `$env:OFFLINE_HARNESS_FLASH_DIFF = '1'` 与 `$env:OFFLINE_HARNESS_FLASH_DIFF_WALKS = '32'`
设置同名变量后运行相同 `dotnet` 命令。
主搜索分发（`BeamSolveDispatch`）在协调器模式下复跑，日志中的 `[CombatSolver/Flash]` 行记录
对照或采用结果：

```bash
OFFLINE_HARNESS_FLASH_MODE=Shadow \
  dotnet .local/tool-build/OfflineSearchHarness/bin/Release/net9.0/OfflineSearchHarness.dll \
  --character IRONCLAD --encounter NIBBITS_NORMAL --seed FLASH1 --milestone M2 \
  --search-mode Coordinator --budget-ms 10000 --out <输出目录>
```

无人测试中的实机部署用 `--flash-search-mode-for-test Off|Shadow|On`（PowerShell
`-FlashSearchModeForTest`）临时覆盖设置；`--export-bug-report-after-combat` 在战斗结束和 On
搜索失败停止部署时都会在写结果前导出问题包，失败包带 `FlashSearchFailed`/`SearchFailure`
分类。包内 `diagnostics/logs/combat/*.jsonl` 保留 `[CombatSolver/Flash]` 行：

```bash
./tools/testing/run-unattended-test.sh --scenario-id FLASH-ON-NIBBITS --character-id IRONCLAD --encounter-id NIBBITS_NORMAL \
  --enemy-current-hp 1000 --headless-fast-mode-for-test Instant --deployment-fast-mode-for-test Instant \
  --deployment-inter-action-delay-seconds-for-test 0 --expected-unexpected-replans-at-most 0 \
  --flash-search-mode-for-test On --timeout-seconds 120 --cleanup-instance-on-exit
```

## 2. 当前证据（2026-10-21，Linux）

F1 纵切与内容批次 P1–P12 的逐批证据已归档到 [内容批次测试记录](../archive/flash-search/testing-content-batches-2026-10-18.md)，强制行动/眩晕（KS）、敌人生成（KM）与早期端到端测试归档到 [KS、KM 与早期端到端测试记录](../archive/flash-search/testing-batches-2026-10-19.md)，第二幕 P13 的逐根证据归档到 [第二幕 P13 测试记录](../archive/flash-search/testing-act2-p13-2026-10-19.md)，截至 SP2 的其余批次归档到 [覆盖补齐与协调器接管批次测试记录](../archive/flash-search/testing-batches-2026-10-20.md)，DX5 至 HS3 的批次与无头整场归档到 [开启模式接管与实机整场测试记录](../archive/flash-search/testing-batches-2026-10-21.md)；下面的全量回归覆盖其中全部根。

全量回归（`flash_regression.py diff`，2026-10-21 起的根集合）：FlashKernelChecks 通过；289 个固定根（`coverage/fixtures/cards/flash-*.json`，每根 32 条随机游走）与 120 个质量牌组（`coverage/corpora/flash-quality/`，每根 48 条随机游走）共 409 根，K1 单步、K2 游走、K4 评估、K5 下界输入与动作集合核对全部零差异，没有准入拒绝，`unsupported` 全部为 0。此前的 162 个自然遭遇根、`flash-decks` 的 64 个生成牌组与 `flash-act3` 的 150 个第三幕牌组已从回归中移除，它们在移除前的最后一次运行也是零差异。QF5 新增一个固定根后当前集合为 290 个固定根 / 410 根，合入时按最终源码重跑。

### 2.1 当前状态（R2 合入后）

- 覆盖：五个角色的卡池、296 个遗物、65 种药水、三幕全部遭遇与第三幕首领策略、事件卡牌与事件遭遇。不支持的内容在准入时给出类别：多人专用、Mock、废弃内容，以及现有引擎同样不支持的 BYRDPIP。
- 质量对照（[质量语料](../../coverage/corpora/flash-quality/README.md) 120 根，协调器 Medium、宽度组合、120 秒，8 个并行进程）。Flash 与常规搜索使用同一档位的 Beam 宽度与节点上限。口径：两种模式同一预算、同一档位，每根单次运行，各按实际结果计入；“好/同/差”按求解器自己的比较键：胜负、存活、死亡豁免次数、战略失血 + 药水战略成本（`RouteQualityPolicy` 的药水口径）、结束回合，差值列是两种模式（战略失血 + 药水战略成本）之差的合计，表由 `flash_regression.py table` 生成；只比较战略失血会把用药换来的少失血算作更好（例如药水成本 45 的路线失血 17，按求解器口径是 62）；同一代码重复测量时个别根有数点波动。

  | 范围 | 根数 | 开启相对关闭（好 / 同 / 差） | 失血 + 药水成本差 | 胜场 关闭 → 开启 | 总耗时 关闭 → 开启 |
  | --- | ---: | --- | ---: | --- | --- |
  | 合计 | 120 | 8 / 90 / 22 | +13 | 87 → 85 | 1671 s → 335 s |
  | 铁甲战士 | 24 | 3 / 18 / 3 | +23 | 20 → 19 | 324 s → 57 s |
  | 静默猎手 | 24 | 1 / 20 / 3 | +5 | 14 → 14 | 377 s → 69 s |
  | 故障机器人 | 24 | 0 / 19 / 5 | +25 | 18 → 18 | 247 s → 45 s |
  | 储君 | 24 | 1 / 14 / 9 | −24 | 18 → 17 | 363 s → 98 s |
  | 亡灵契约师 | 24 | 3 / 19 / 2 | −16 | 17 → 17 | 359 s → 66 s |
  | 第一幕 | 40 | 4 / 29 / 7 | −18 | 40 → 40 | 431 s → 74 s |
  | 第二幕 | 40 | 2 / 28 / 10 | +11 | 29 → 28 | 740 s → 162 s |
  | 第三幕 | 40 | 2 / 33 / 5 | +20 | 18 → 17 | 501 s → 99 s |

  22 根变差中有 7 根只是结束回合更晚；2 根在开启模式未获胜（IRONCLAD-016、REGENT-008）；代价变多的 13 根是 DEFECT-000（17 → 20）、DEFECT-010（42 → 45）、DEFECT-012（14 → 33）、IRONCLAD-011（27 → 28）、IRONCLAD-014（117 → 135）、NECROBINDER-018（22 → 25）、NECROBINDER-022（52 → 55）、REGENT-002（55 → 56）、REGENT-014（111 → 117）、REGENT-015（23 → 30）、REGENT-021（24 → 32）、SILENT-013（19 → 23）与 SILENT-018（8 → 10）。复刻常规搜索的过程中，“同”由 78 增到 90，“好”由 19 降到 8：Flash 原先碰巧比常规更好的根随行为趋同而减少，目标是“差”降到 0。
- 120 根开启模式全部 `FLASH_ADOPTED`，没有 `FLASH_SEARCH_FAILED`，开启模式不调用原搜索。10 根共 20 次 `FLASH_NO_ROUTE`（`flash-potion-policy-no-route`），与现有搜索的无路线结果同形。
- 开启模式的无路线结论与失败类别见 [搜索与接入](search.md) 3.2 节。

### 2.2 批次记录

截至 SP2 的已完成批次，逐批证据归档在 [覆盖补齐与协调器接管批次测试记录](../archive/flash-search/testing-batches-2026-10-20.md)；DX5 至 SW 的批次归档在 [开启模式接管与实机整场测试记录](../archive/flash-search/testing-batches-2026-10-21.md)。新批次在本节追加。

开启模式回合准备失败与 HS3 超时（HS4，2026-10-21）：HS3 中 5 场开启模式整场没有结果，都是 Flash 回合准备搜索失败后，无人测试宿主一直等待无人作答的原生选择；Spine 异常是启动器结束进程后的清理噪声。三类原因：回合准备根在回合开始的生成效果之前捕获，`BigHat` 与 `VexingPuzzlebox` 没有声明生成牌池，导入生成牌时报内核不支持（NECROBINDER-22、REGENT-19、rq-REGENT-V-19），两者改为声明角色卡池；进度预览把推测路线翻译到旧引擎时手牌下标越界，`ArgumentOutOfRangeException` 打断整次搜索（NECROBINDER-19）；TestSubject MULTI_CLAW 的连击数编译时取了根上已含额外次数的意图值，内核 `ModifyAttackRepeats` 又加一次（NECROBINDER-08 第 5 回合核对差 11 点，Flash 多打一下），改为编译取 `BaseMultiClawCount`。`flash-hs4-testsubject-multiclaw` 用新增的 `initialEnemyIntegerMembers` 把 `_extraMultiClawCount` 设为 1，修正前 16 条游走失败、修正后 0。翻译器在手牌下标越界，或旧引擎按动态目标规则（`GetTargetType`，含 Fan of Knives 下的 Shiv 与 Seeking Edge 下的君王之刃）需要敌方目标而 Flash 动作没有目标时，抛 `FlashChoiceMismatchException`：核对与物化路径转为 `verify-mismatch`，预览只跳过，每次求解第一次出现时写 `FLASH_PREVIEW_MISMATCH`，`FLASH_PROGRESS_STATS` 增加 `mismatches=`。回合准备搜索的 Flash 失败按 SW 约定处理：写 `FLASH_SEARCH_FAILED`、停止部署、关闭全自动、弹出 Flash 提示，问题包带 `SearchFailure` 与 `FlashSearchFailed`，原生选择留给玩家；无人测试宿主遇到回合准备或搜索失败时直接判 Failed，不再等到启动器时限。动态意图审计：原版 218 个攻击意图中只有 TestSubject MULTI_CLAW、TheForgotten DREAD 与 WaterfallGiant PRESSURE_GUN 由运行时值计算，后两者在根上不会重复计入（`flash-hs4-dread-root-dexterity` 根上带 5 层敏捷，游走零差异）。离线宿主新增最终候选路线扫描（`OFFLINE_HARNESS_FLASH_CANDIDATE_SCAN=N`）：HS4 时全量回归 493 根共 2901 条候选逐条交现有引擎核对，没有不一致（54 根没有可扫描的候选）。HS3 中亡灵契约师与储君的 40 场开启模式整场重跑：全部有结果，29 胜 11 战死，735 行 `FLASH_ADOPTED` 全部核对通过，`FLASH_SEARCH_FAILED`、核对不一致与预览不一致均为 0，计划外重算 0；HS3 的 5 场超时都跑完（NECROBINDER-08 第 9 回合战死，关闭模式第 10 回合战死；其余 4 场胜利），其余场次的胜负与结束回合与 HS3 相同（有记录生命的场次生命也相同）。回合准备固定根与合同 16 项（`TURN-SETUP-*`、`toasty-qol-*`、`route-row-reuse` 等）开启与关闭模式均 Passed。HS4 合入后当时全量回归 493 根零差异，第三幕 150 根 48 条游走零差异；默认 79 根开启模式与 HS3 合入后逐根相同（46 个牌组 11 好 / 30 同 / 5 差、−90，33 个牌组 8 / 20 / 5、−16，没有 `FLASH_SEARCH_FAILED`，`FLASH_NO_ROUTE` 仍只在 IRONCLAD-27/53），48 根基准 14 / 29 / 5、失血合计 −34，与合入前相同。

质量语料暴露的内核与规则表问题（QF，2026-10-21）：100 根五角色三幕质量语料暴露 7 根问题，归为五个根因并各补一个固定根（`coverage/fixtures/cards/flash-qf-*.json`，修正后单独差分为 0）。规则闭包：`Seance` 变换生成 `Soul` 却没有声明 `GeneratedCards`（NECROBINDER-07/12，`flash-qf-seance-soul`）；`NightmarePower`/`SwipePower` 的分离卡在打出 Power 或被盗后不在任何战斗牌堆里，续接根导入时仍需要其规则（SILENT-15，`flash-qf-nightmare-detached`）。内核顺序：重放/Echo Form 的每次 CardPlay 都要重新开始 Fisticuffs 的伤害历史（NECROBINDER-04、DEFECT-03，`flash-qf-fisticuffs-replay`）；全体敌人 Power 应用要遍历 `HittableEnemies` 快照，避免首个目标死亡引起 roster 压缩后跳过后续敌人（NECROBINDER-15，`flash-qf-negative-pulse-roster`；Misery 的同类循环一并改为快照）；回合开始选择（Foregone Conclusion、Tyranny、Toasty Mittens、Tools of the Trade、Entropy）按 `TurnStartChoiceCursor.BeforeNextTake` 先启动玩家侧回合，再执行选择效果（REGENT-13，`flash-qf-turn-start-choice-forge`）。7 根各跑 48 条游走，全部 `failed=0 walkFailed=0 unsupported=0`；原开启模式失败的 6 根协调器开启模式均 `FLASH_ADOPTED` 且核对通过。NECROBINDER-18 开启模式战略失血 279 的组成是累计战损 66 + 最大生命差 24 + 死亡豁免溢价 189（21×9），不计 Osty 或召唤物；K4 评估与 48 条游走差分零差异，路线在第 5 回合因先前的出牌顺序进入不同手牌分支，属于保留结构/搜索宽度差异，不改。QF2 追加 `flash-qf-misery-roster`（Misery 的 live-roster 固定根）、EndOfDays/NoxiousFumes 的 owner 快照，以及智能药水梯度层的顶层失败合同；NECROBINDER-15 的 48 条游走与协调器开启模式（26 行 `FLASH_ADOPTED`，含 4 次 `SmartPotionGradient`）均无失败。默认差分回归已包含 100 根质量语料（600 根），`--only quality` 仍可单跑。 合入前在最终源码上：默认差分 600 根加第三幕 150 根共 750 根零差异；最终候选扫描 600 根共 3,589 条候选路线零不一致（`flash-sk-a-discard-sly` 仍是已知内核边界）；100 根质量对照见 2.1。

120 根质量语料暴露的内核问题（QF3，2026-10-21）：质量语料改为五个角色各 24 根、三幕各 8 根（种子 `FLASH-QUALITY-<角色>-<NNN>`，生成口径写死在 `survey`），回归只保留固定根与质量语料。新语料暴露 5 处内核分歧与 2 处内核边界，各补固定根（`flash-qf3-*`、`flash-qt2-*`，修正前失败、修正后为 0）：`SEEKING_EDGE` 先施加 Power 再锻造（REGENT-021）；`ReplaceCard` 不受战斗结束门约束（Hellraiser 抽牌击杀最后敌人后的 Entropy 变换，IRONCLAD-005）；玩家回合末最后敌人死亡后只跳过 late 监听派发，遗物的回合末清零照常执行（DEFECT-011 与随后发现的 KUSARIGAMA/ART_OF_WAR 计数回归）；Plow 按临时力量定义族移除（IRONCLAD-002）；Burrowed 的破格挡补偿延后到该效果的死亡处理之后（REGENT-009）。Decisions, Decisions 的重复自动打出与 Mayhem 的自动预打出队列改用一次动作内的瞬态标记跟踪牌身份，前一张牌移走后一张时从其当前牌堆打出，标记丢失或重复时显式失败（REGENT-007、REGENT-013 不再是内核边界）。同批按类别重组 `src/Flash/Content` 的文件（只移动类型，1,313 个类型的源码逐字相同），`UnsupportedCategories.cs` 移入 `Bridge`。合入前在最终源码上：默认差分 404 根零差异；最终候选扫描 404 根共 2,164 条候选零不一致（`flash-sk-a-discard-sly` 仍是已知内核边界）；120 根质量对照见 2.1；关闭模式与上游 `5c773caa` 在 120 根上逐根质量相同、总耗时 +0.5%。

路线追踪与搜索策略移植（QR、QP1–QP3、QF4，2026-10-21）：新增默认关闭的路线追踪（`flash_regression.py trace`，`OFFLINE_HARNESS_FLASH_TRACE_*`；关闭时 DEFECT-012 开启模式路线、总展开 242,219、总转移 1,621,887 逐位相同），对 QF3 时 8 个失血更多的根定位关闭模式路线在 Flash 中首次被丢弃的位置：IRONCLAD-014、REGENT-021、REGENT-013 在 Flash 中按排名截断，在现有搜索中由必保代表留下（来源标签分别为 stand-pat Control 组、根动作谱系签名组与路由选择）；DEFECT-012 当时记为同指纹去重只留一个代表（QH2 修正追踪后，真正的丢失在第 6 步的普通排名截断，见下一段）；REGENT-015 是开局目标变体不同；IRONCLAD-016 是 Liquid Memories 在 Flash 中为空效果；DEFECT-011、IRONCLAD-011 未定位。QP1 照 `PotionChoiceSupport.GetSpec` 实现 Liquid Memories、Ashwater、Gambler's Brew、Touch of Insanity 的选牌（各配 `flash-qp1-*` 固定根，修正前动作集合失败 47/47/46/45），差分新增两侧可选动作集合核对（`actionSetFailed`）。QP2 在池内同指纹去重时按现有转置表的六维标签做 Pareto 拒绝、代表改按 `IsBetterSearchNode`（120 根由 28/76/16、+34 变为 29/75/16、+10，IRONCLAD-016 由未胜变为 53）；单根首步谱系按签名分组再试一次，120 根变为 27/76/17、+45，撤回；stand-pat 诊断当时显示 IRONCLAD-014 的目标在进入 stand-pat 分组前已被去重代表顶替（这一结论来自去重后断开跟踪的旧追踪，QH2 之后未重查）。QP3 的整次求解转置表与选择完成后的路由键在 120 根上质量不变、未采用（见 [搜索与接入](search.md) 1 节）。合入时默认游走数的全量差分与候选扫描在 quality-NECROBINDER-022 上发现 Touch of Insanity 把 X 费牌列为选项（原版 `CostsEnergyOrStars` 的 global 分支排除 X 费），QF4 修正并补 `flash-qf4-touch-of-insanity-xcost`（修正前 17 条游走失败、修正后 0）。最终源码上：默认差分 409 根零差异；候选扫描 409 根共 2,194 条候选零不一致（`flash-sk-a-discard-sly` 仍是已知内核边界）；120 根质量见 2.1，与 QP2 时逐根相同（含展开数与转移数）；关闭模式基线与 QF3 时的关闭模式 120 根质量逐根相同（118 根展开数也相同，另 2 根受时间预算限制），原搜索只增加默认关闭的诊断，因此没有重跑与上游的对照。

档位宽度核对不一致（QF5，2026-10-22）：当时用已移除的 `OFFLINE_HARNESS_FLASH_PROFILE_SCALE=1` 得到与现行档位相同的宽度，120 根协调器开启模式只有 `quality-IRONCLAD-019` 失败（`verify-mismatch`，第 18 步 `PlayCard:ANGER#0->3`，复制牌旧引擎 `ANGER+1`、Flash `ANGER+0`）。该路线的固定成员是 `OpeningPowerRouteMember` 的 `JUGGLING` 前缀；根因是这次攻击击杀最后一个 `DampenPower` 施法者后，现有引擎在 Anger 复制补偿前执行死亡结算并把打出的 `CardModel` 升级回原等级，而 Flash 既漏了这次 `ApplyEnemyDeathPowers` 又复制了离开手牌时的 `FlashExec.Card` 副本。`Anger.OnPlay` 现先运行死亡结算、再用 `SyncCurrentPlayCard` 取实际出牌行后复制；旧引擎与 `.local/decompiled/sts2-v0.111.0` 的 `DampenPower.AfterRemoved` + `Anger.OnPlay.CreateClone` 一致，未改旧引擎。新增固定根 `flash-qf5-anger-dampen-clone`：修正前 32 条游走 `failed=1 walkFailed=18`，修正后 `failed=0 walkFailed=0`；`quality-IRONCLAD-019` 默认 48 条游走零差异；档位宽度单根协调器开启模式 30 行 `FLASH_ADOPTED`、没有 `FLASH_SEARCH_FAILED`。离线宿主新增 `flashFailure` 全量证据（完整路线、步前状态、两侧步后状态、全部差异与成员上下文），稳定事件行不变。默认游走、动作集合检查与旧批次更宽 Beam 未发现它的原因见 [开发笔记](../DEVELOPMENT_NOTES.md) 的 QF5 条；全量差分、候选扫描与 120 根质量对照留待合入时运行。

历史对照的变差根（QH、QH2，2026-10-21）：用当前代码只跑开启模式、关闭模式复用当时结果（原搜索自那时起只加默认关闭的诊断），旧 79 根按求解器口径 19/50/10、差值 −60，旧 100 根 30/60/10、+226（NECROBINDER-18 一根 +194）。11 个变差根各跑 48 条游走差分与候选扫描，全部零差异，两种模式导入的根逐字段相同。追踪改为同指纹去重后接到留下的代表节点继续（同一状态字节的另一条到达路线，状态仍在池里），此前归为“去重先到先得”的 5 根真正的丢失都在后面的层：REGENT-V-27（第 8 步，排名 508/960、宽度 240）、IRONCLAD-53（第 34 步）、DEFECT-012（第 6 步，Flash 排名 305/4643，现有搜索排名 17/60）是普通排名截断；REGENT-02（第 6 步，现有搜索由 `cohort.PrefixVariants[round]` 必保代表留下）与 SILENT-C-14（第 12 步，现有搜索由防御必保代表留下）是 Flash 没有对应的必保分组。NECROBINDER-C-08 的准备类领先者在 Flash 中存在并选中了准备值更高的节点，目标不是任何已移植分组的领先者。REGENT-V-03 关闭模式的 7 来自回合边界救援，该阶段只在主搜索结果足够好时进入，开启模式 w90 成员输掉后没有进入。IRONCLAD-28 开启模式原始失血更少（45 对 47），差 2 点来自成长奖励。NECROBINDER-18 两种模式各成员的原始失血都在 63–68，开启模式的路线族都要用死亡豁免，比较器按死亡豁免次数与溢价计入。RQ 时期 IRONCLAD-17 开启模式的失血 17 用了一瓶药（成本 45），按求解器口径是 62，SR 之后的 56 是改进而不是退化。

宽度与档位对齐（QW，2026-10-22）：Flash 的 Beam 宽度、节点上限与软时间直接使用档位值，与常规搜索相同，共享节点预算按 Flash 实际展开数扣减。默认 120 根开启模式与此前用旧测试开关取同样宽度的运行逐根相同（路线、质量、展开数与转移数），质量见 2.1。合入后在最终源码上：候选扫描 410 根共 2,031 条候选零不一致（求解探针的 Flash 宽度随之变窄，`flash-sk-a-discard-sly` 的内核边界在本次扫描中未被触及）；不改内核，未重跑差分（QF5 合入后的 410 根默认差分为零差异）。

逐层复刻常规搜索（K6、R1、R2，2026-10-22）：目标是同一档位下 Flash 在每一根上都不劣于常规搜索。K6 新增逐层保留集合一致检查（`OFFLINE_HARNESS_RETENTION_DIFF`，`flash_regression.py retention`，用法见[离线搜索宿主](../OFFLINE_SEARCH_HARNESS.md)）：对同一请求分别运行两种搜索，以 K2 规范状态投影的 128 位哈希为跨引擎状态键，逐个保留边界比较选中集合并给第一处分歧分类。120 根宽度组合成员共 653 次运行，637 次可比较（16 次在常规侧有不可投影的状态）；K6 时只有 1 次全程一致，第一处分歧几乎都在第 1 回合前三层，其中候选生成不同 48%、必保分组不同 30%、同分排名 14%、分数不同 8%。R1 照常规移植同名牌去重、选牌分支排序与档位上限、出牌准入与卖血惩罚：同名牌与分数两类第一处分歧原因归零，选牌枚举 574 → 242 次命中。R2 移植保留边界的同分顺序（不加下标决胜、全收早退、边界同分轮转、回合结束状态的分组顺序）与首步谱系签名分组，并修正 K6 探针打开常规侧必保来源标签（R1 的必保类数字因此只看到单方向）；同口径下同分原因 2,721 → 2,297、谱系原因 1,182 → 412，最终路线与常规相同的成员 20 → 24，120 根质量见 2.1。两批都没有改常规搜索的行为（R2 只在诊断字典上增加每边界清空）。合入后在最终源码上：R1 的默认差分 410 根零差异，候选扫描 R1 1,939 条、R2 1,922 条均零不一致；R2 不改内核，未重跑差分。剩余的第一处分歧主要是常规在排名之后的追加保留通道（长期资源、循环与跨回合组合）、路由代表、药水分组与焦点压力，以及少数选牌组合的枚举长尾。

### 2.3 无人测试实机部署

Linux 无头整场部署的历次记录（2026-10-19 的初始牌组与第三幕首领，HS、HS2、HS3 的多样化牌组整场）归档在 [开启模式接管与实机整场测试记录](../archive/flash-search/testing-batches-2026-10-21.md) 第 2 节，最近一轮见 2.2 的 HS4。HS3 与 HS4 的 40 场无头整场使用本地生成的四角色牌组清单；该清单保存在 `.local`，**没有随仓库提交**，因此审阅者无法从仓库逐场复跑同一 40 场，只能按本节方法重建代表场景；仓库内保留的是汇总结果。无人测试请求默认向手牌注入一张 `STRIKE_IRONCLAD`，用显式牌组建局时需传 `--cards-json []`。玩家战死的整场不导出问题包，`[CombatSolver/Flash]` 行从结果目录的 `runtime-events.json` 读取。多个实例并行时共用同一个 `COMBATSOLVER_HEADLESS_HOST_ROOT`：各用一个时，准入会把另一实例的游戏进程当作未登记进程，排队到时限后无结果。

## 3. 已知限制与未验证项

- 只覆盖归档批次与 2.1 所列内容；其他内容在准入阶段拒绝，尚未有差分证据。
- 精确层下界不可用（遗物目标、成长目标等）时 Flash 整次搜索不做下界剪枝，原搜索只对不满足条件的节点放开；路线不受影响，搜索面更大。除遗物目标外的禁用原因没有逐个构造实机根。
- `FLASH_SEARCH_FAILED` 的提示与问题包（包括回合准备搜索失败时关闭全自动、把原生选择留给玩家）只在无头合同与整场中验证，未在可见游戏中查看。
- 最终候选扫描只覆盖排序前 N 条最终候选，进度预览用过的推测路线没有导出；HS4 前 NECROBINDER-19 一次预览手牌下标差异（第 7 回合第 24 步）此后没有再现。扫描中 `flash-sk-a-discard-sly` 的一条搜索分支到达内核边界（TACTICIAN+1 弃牌后在自动打出前失去瞬态标记），开启模式在该状态按 `kernel-unsupported` 失败。
- 仍在战斗中的生物身上已移除 Power 的残留条目不能投影：根导入时拒绝，路线核对中遇到时按核对失败丢弃 Flash 路线；敌方离场后的残留按惰性处理（见内容批次归档卷 2.8.1）。
- 选择覆盖卡牌自身的单选、手牌/抽牌堆/弃牌堆选择、回合开始选择、知识恶魔的固定选项与自动打出中的嵌套选择（LR、MB 固定根）；一个动作最多携带 8 个选择，更深的链按容量边界拒绝。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 实机部署只在 Linux 无头游戏中验证（五个角色初始牌组、第三幕首领与 HS/HS2/HS3/HS4 的多样化牌组整场，见 2.3 与归档卷）；未做可见游戏、Windows 或 Steam 会话验证，设置面板的新选项未在游戏中查看。
