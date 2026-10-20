# Flash 测试记录

[返回 Flash 入口](README.md)

本文记录 Flash 的可复跑验证入口与当前证据。方法定义见 [准入与校验](admission-and-verification.md)。

## 1. 复跑入口

纯内核合同（K0），不依赖游戏：

```bash
DOTNET_TieredCompilation=0 dotnet run --project tools/testing/checks/FlashKernelChecks -c Release
```

差分探针（K1 单步穷举、K2 随机游走），需要游戏程序集与已构建的模组：

```bash
dotnet build CombatSolver.csproj -c Release -p:CopyModOnBuild=false
dotnet build tools/search/OfflineSearchHarness/OfflineSearchHarness.csproj -c Release
OFFLINE_HARNESS_FLASH_DIFF=1 OFFLINE_HARNESS_FLASH_DIFF_WALKS=32 \
  dotnet .local/tool-build/OfflineSearchHarness/bin/Release/net9.0/OfflineSearchHarness.dll \
  --character IRONCLAD --encounter NIBBITS_NORMAL --seed FLASH1 --milestone M1 --out <输出目录>
```

PowerShell 用 `$env:OFFLINE_HARNESS_FLASH_DIFF = '1'` 设置同名变量后运行相同 `dotnet` 命令。探针把结果写入输出目录的 `flash-diff.txt`（首行为汇总），并在 `harness-result.json` 的 `flashDiff` 字段记录同样的计数。`OFFLINE_HARNESS_FLASH_DIFF_STEPS` 控制每条随机游走的最大步数（默认 60）。

主搜索分发（`BeamSolveDispatch`）在协调器模式下复跑，日志中的 `[CombatSolver/Flash]` 行记录对照或采用结果：

```bash
OFFLINE_HARNESS_FLASH_MODE=Shadow \
  dotnet .local/tool-build/OfflineSearchHarness/bin/Release/net9.0/OfflineSearchHarness.dll \
  --character IRONCLAD --encounter NIBBITS_NORMAL --seed FLASH1 --milestone M2 \
  --search-mode Coordinator --budget-ms 10000 --out <输出目录>
```

无人测试中的实机部署用 `--flash-search-mode-for-test Off|Shadow|On`（PowerShell `-FlashSearchModeForTest`）临时覆盖设置；`--export-bug-report-after-combat` 导出的问题包在 `diagnostics/logs/combat/*.jsonl` 中保留 `[CombatSolver/Flash]` 行：

```bash
./tools/testing/run-unattended-test.sh --scenario-id FLASH-ON-NIBBITS --character-id IRONCLAD --encounter-id NIBBITS_NORMAL \
  --enemy-current-hp 1000 --headless-fast-mode-for-test Instant --deployment-fast-mode-for-test Instant \
  --deployment-inter-action-delay-seconds-for-test 0 --expected-unexpected-replans-at-most 0 \
  --flash-search-mode-for-test On --timeout-seconds 120 --cleanup-instance-on-exit
```

## 2. 当前证据（2026-10-20，Linux）

F1 纵切与内容批次 P1–P12 的逐批证据已归档到 [内容批次测试记录](../archive/flash-search/testing-content-batches-2026-10-18.md)，强制行动/眩晕（KS）、敌人生成（KM）与早期端到端测试归档到 [KS、KM 与早期端到端测试记录](../archive/flash-search/testing-batches-2026-10-19.md)，第二幕 P13 的逐根证据归档到 [第二幕 P13 测试记录](../archive/flash-search/testing-act2-p13-2026-10-19.md)，截至 SP2 的其余批次归档到 [覆盖补齐与协调器接管批次测试记录](../archive/flash-search/testing-batches-2026-10-20.md)；下面的全量回归覆盖其中全部根。

全量回归（`.local/tool-tasks/flash-search/regress.sh`，或按根分片并行的 `regress-par.sh`；SK、PT、DF、RG、NB、LR、MB、LS、ID、RQ、CX、TW、PF、PM、KB、SP、DX3、A3B、K4C、ST、PA、PC、DX4、PR、CP、TS、EC、SP2、DX5、SR、B1、XP、HS、LH、HS2、B2、DT、RS、DX6、TT、CT、RS3、DX7、UX、SW 合入后）：FlashKernelChecks 通过；162 个自然遭遇根（铁甲战士初始牌组覆盖三幕已实现遭遇的 FLASHM1/FLASHM2，另有其他四个角色的初始牌组各 6 条，以及三个第三幕首领在 `--act-index 2` 下开启首领策略的 6 条）、261 个固定根（`coverage/fixtures/cards/flash-*.json`）与 64 个生成牌组（含 29 个非铁甲战士牌组、11 个第三幕牌组）（`coverage/corpora/flash-decks/`）每根 32 条随机游走，K1 单步、K2 游走、K4 评估与 K5 下界输入全部零差异，没有准入拒绝；`unsupported` 全部为 0。

### 2.1 当前状态（SW 合入后）

- 覆盖：五个角色的卡池、296 个遗物、65 种药水、三幕全部遭遇与第三幕首领策略、事件卡牌与事件遭遇。不支持的内容在准入时给出类别：多人专用、Mock、废弃内容，以及现有引擎同样不支持的 BYRDPIP。
- 默认语料的协调器对照（Medium、宽度组合、120 秒，Off 为固定基线）。口径：两种模式同一预算、同一档位，每根单次运行，各按实际结果计入，预算内未完成的根同样照实计入；同一代码重复测量时个别根有数点波动。
  - 四角色 46 个牌组：On 相对 Off 11 好 / 30 同 / 5 差，战略失血差合计 −90。
  - 铁甲战士 33 个牌组：8 好 / 20 同 / 5 差，−16。
  - 79 根全部 `FLASH_ADOPTED`，没有 `FLASH_SEARCH_FAILED`，开启模式不调用原搜索。IRONCLAD-27 与 IRONCLAD-53 各 2 次 `SmartPotionGradient` 由 Flash 判定无路线（`FLASH_NO_ROUTE`），现有求解器在这两根上同样没有满足策略的路线（`acceptable=False`）。
- 48 根基准（10 秒）：Flash 相对现有主搜索 14 好 / 29 同 / 5 差，失血合计 −34。
- 开启模式的无路线结论与失败类别见 [搜索与接入](search.md) 3.2 节。

### 2.2 批次记录

截至 SP2 的已完成批次，逐批证据归档在 [覆盖补齐与协调器接管批次测试记录](../archive/flash-search/testing-batches-2026-10-20.md)。新批次在本节追加。

开启模式剩余的内核回落（DX5，2026-10-20）：SILENT-C-20 的路线核对差 9 点，根因是 Pen Nib 第十次攻击的加倍锁存泄漏到 Dagger Throw 弃牌后嵌套自动打出的 Sly 牌 Flick-Flack；锁存改按 `FlashExec.PlayIdentity` 记账，与现有引擎按卡牌对象比较一致（`flash-dx5-pen-nib-sly` 撤掉修正时 K1 差 9）。战损假人时限的最后一层补上 `FlashOutcome.EventDefeat`：跳过后续阶段、威胁投影按现有引擎、终局键追加 `PolicyBoundaryRank`；三个战损假人遭遇的自然根全部走到事件失败，协调器开启模式 `FLASH_ADOPTED:verified` 且与关闭模式同分。HISTORY_COURSE 用新增的跨回合卡值槽 `FlashBattle.HistoryCourseCard`（12,600 → 12,696 字节），自动预打出阶段克隆上一玩家回合最后一张攻击并自动打出，复制品移出战斗。6 个 `flash-dx5-*` 固定根零差异；默认 79 根与第三幕 150 根的协调器开启模式中核对不一致、内核不支持与内核边界均为 0；默认 79 根战略结果逐根不变，48 根基准质量不变、耗时 +0.2%。

保路开关与能力承诺（SR，2026-10-20）：`SecondRankBand` 与 `AggressivePowerCommitment` 改由 FlashBeam 执行，分发点删除这两个拒绝。次段剪枝在全局剪枝后执行（必保、承诺与计划席位不变）；力量承诺的席位配额、2/3 回合寿命与代表置换按原语义移植，承诺是路线节点元数据，不进 `FlashBattle` 与状态键；逐卡触发证据、开局投影与静默猎手的专用进展/兑现证据按现有引擎移植（`FlashPowerEvidence`），`RegisteredPowerBenefit` 计划收益放开；多个回合准备根的首步谱系按现有签名跨根合并（单根保持按节点分组，见 search.md）。默认 79 根相对 SR 之前 6 好 / 71 同 / 2 差、失血合计 +14：IRONCLAD-17 17 → 56 与关闭模式相同（此前 Flash 忽略承诺泳道），SILENT-C-13 +2；IRONCLAD-50 56 → 36。药水矩阵不再有 `second-rank-band` 回落，但 SILENT-C-08 强制用药 16 → 20、DEFECT-C-05 至少一瓶 23 → 30 变差，与 IRONCLAD-17 一并归因于 Flash 缺少原搜索与承诺席位平衡的保留泳道，留给后续保留结构批次。48 根基准路线逐根不变。

开局探针抽离原搜索类（B1，2026-10-20）：协调器约 40 处 `Build*` 开局/前缀探针与四个续接来源改持 `src/Search/Probes/OpeningProbe`，不再实例化 `CombatBeamSolver`；评分、指纹与威胁投影抽成原搜索与探针共用的 `SearchEvaluator`，回合推进、动作枚举、候选分类、固定前缀与选牌规格各只保留一份实现（`SearchRoundExecution`、`SearchExpansionShared`、`SearchProbeHelpers`），结构门禁禁止 `src/Search/Probes/**` 引用 `CombatBeamSolver`，并禁止这些方法在 `CombatBeamSolver*.cs` 中再有方法体。`Replay`/`ReplayAction`/`ReplayTurnSetup` 当时仍是两份算法体，B2 统一。合入后默认 79 根关闭与开启模式均与改动前逐根相同（路线与质量），48 根基准路线逐根相同，8 个探针相关无人测试合同 Passed；`CARD-CONTINUATION-CONTRACT` 在改动前的基线上同样失败（找不到 `DAGGER_THROW` 弃牌堆实例），属于既有问题。

统一回放核心（B2，2026-10-20）：`Replay`/`ReplayAction`/`ReplayTurnSetup`/`ResolveTurnSetupChoices` 与准备根枚举收敛到 `src/Search/Probes/SearchReplayCore.cs`，原搜索与探针共用；检查点、执行续接帧、回放证据、性能计量与增量校验按显式上下文注入，准备根的选择预算只有一份。Flash 的桥接与物化改用 `SearchReplayCore`、`OpeningProbe` 与共享的 `SearchRouteMaterializer`，`src/Search/FlashBeam/**` 不再引用 `CombatBeamSolver`（结构门禁强制），开启模式只在分发点的回落处构造原求解器。合入后默认 79 根关闭与开启模式均与改动前逐根相同，回合准备根与 48 根基准路线逐根相同，回放续接相关无人测试合同 Passed；`CARD-CONTINUATION-CONTRACT` 的既有失败查明为夹具在出牌收尾前检查弃牌堆。

保留结构（RS，2026-10-20）：FlashBeam 补上 `SearchRouteTraits` 路线标签（八条泳道按标签过滤）、按 profile 宽度的药水使用配额与 `PotionUseLineageKey` 谱系、选项路由配额与 option leader、stand-pat（三条泳道、fresh resource 与 damaging continuation，用 Flash 内核重放结束回合）与全局 Pareto；路线标签与谱系是路线节点元数据，K4 逐转移核对路线标签增量（约 45 万次零失败）。持续铺垫分组仍用 `PersistentSetupTraits`：改用含潜在标签的 `StrategicSetupTraits` 会在 Flash 的 4 倍宽度下扩出远多于现有搜索的分组，DEFECT-C-05 至少一瓶 30 → 40。默认 79 根相对合入前 10 好 / 65 同 / 4 差、战略失血 −6，IRONCLAD-17 与 NECROBINDER-V-01 维持 56 与 30；REGENT-C-07/V-07 46 → 54（stand-pat 探测在回合开始选择处不能像原搜索那样自动作答），IRONCLAD-53 +2，SILENT-V-03 +1。单独移植选项路由配额时（RS1）IRONCLAD-17 曾变为 63，补上 stand-pat 后恢复。48 根基准质量 14/29/5、耗时持平。尚未移植：循环/跨回合/有序变动组合、回合边界整理代表、路由上下文 Pareto/direct extremes/Art of War 窗口与分布指纹。

第二轮实机问题（DX6，2026-10-20）：Flash 的 Echoing Slash 每波沿用出牌时的伤害值，第一波击杀最后一个 Dampen 施法者、牌的升级等级恢复后，第二波仍按旧值结算（act3-SILENT-19 两敌各差 3 点）；改为每波从实时打出的牌读取伤害（`flash-dx6-dampen-echoing`）。现有引擎的“本回合已完成攻击/Shiv 数”原在 `AfterCardPlayed` 之后才递增，而原版在 `AfterCardPlayed` 之前记录 `CardPlayFinished`，Calamity 在攻击的 `AfterCardPlayed` 中生成的 Stomp 因此少算一次减费（IRONCLAD-50 部署漂移）；现有引擎与 Flash 都改为在 `AfterCardPlayed` 之前递增，无头重跑计划外重算为 0。测试侧：`--*-for-test` 的设置覆盖在第一次搜索根捕获前生效；战斗结束后导出的问题包先完成取证会话再归档。默认 79 根中只有 IRONCLAD-50 改变（关闭与开启模式均变好），基线已更新。

测试工具与诊断（TT，2026-10-20）：`--theft-policy-for-test` 改在 `BeginCombat` 消费预登记策略，回合开始选择的根也读到测试策略；`CARD-CONTINUATION-CONTRACT` 的既有失败查明为夹具沿用了默认 1 生命的敌人，Dagger Throw 击杀后战斗进入收尾、牌按生产语义不能移入弃牌堆，夹具改为把敌人生命抬到 5000 后恢复 Passed；续接戳本身已含有序费用修正层，对账诊断改为先单独列出 `cost-state` 子差异（原先被 180 字符截断），`DEPLOY_REPLAN card_missing` 增加计划状态键，匹配规则不变（新增 `CONTINUATION-COST-STATE-DIAGNOSTIC`）；无头实例的进程标记与租约不是原子发布，启动器对已验证身份的活标记重建缺失的租约（`HEADLESS_LEASE_RECREATED`），连续 10 个请求复用同一实例通过。

剩余请求形态（CT，2026-10-20）：分发点删除 `can-take-over:*` 与 `potion-audit-boundary:*`，计划成员（带无药基线、最早用药回合、药水上下界、强制指令或早期侦察）、计划侦察（带承诺、前缀、药水约束）、带侦察的普通续接、以及审计不适用时的普通请求都由 Flash 执行；非法药水上下界在开启模式下抛出与原搜索同类型、同消息的参数异常。`root-phase` 保留为显式边界：生产只捕获 `Player/Play` 与回合开始选择的 `Player/Start` 两种根。强制结束回合的卡（如 Void Form）翻译为计划动作时补上回合结束标记，修正 TurnBoundaryRescue 路线缺一个回合续接戳的问题（REGENT-V-28 续接戳 `[2..9]`）。探针构造的 15 种形态全部 `FLASH_ADOPTED:verified`；第三幕 150 根 `can-take-over:commitment` 10 → 0、逐根质量不变；默认 79 根与 48 根基准逐根不变。

新颖度探索与早期回合侦察（XP，2026-10-20）：新颖度探索、自适应新颖度精炼与早期回合侦察三个协调器成员改走 `BeamSolveDispatch`，开启模式由 FlashBeam 执行（`FlashBeamSolver.Novelty.cs`）：BFWS 开放队列按（新颖宽度、−分数、序号）排序，新颖事实与分区照现有 `CaptureNoveltyFacts`（`FlashNoveltyFacts`，新增 6 个 K4 字段逐事实核对），熟悉子状态共享祖先的逃逸预算，停止条件与诊断字段与原搜索同口径；早期回合侦察按原策略键每层取 24 个候选，物化并核对后才回报观察者；计划侦察的深度限制放开。这三个成员默认关闭，默认 79 根逐根不变；另选 DEFECT-C-05、IRONCLAD-04、SILENT-C-14 三根在两种配置下稳定触发并全部 `FLASH_ADOPTED`（相对关闭模式 1 好 / 2 差、失血合计 +4，变差来自既有保留结构差异），新颖度控制合同与早期回合无头合同开启/关闭模式均 Passed。尚未移植：新颖度终局的 `RankFinal` 预筛、搜索中途的接管响应（Flash 只在搜索结束时响应）、早期回合续搜的工作量记账。

致命击杀判定改读分支状态（LH，2026-10-20）：现有引擎的 The Hunt、Feed、HandOfGreed 与 BookRepairKnife 致命判定原经原版 `ReattachPower.ShouldOwnerDeathTriggerFatal`/`AreAllOtherSegmentsDead` 读取根上 live 生物的生命，分支内其他千足虫节段已死时仍判为不致命；改为 `SimulatedCombatState.ShouldOwnerDeathTriggerFatal` 按分支 Power 与分支死亡状态计算，同一修正把 Surrounded/BackAttack 与 FrostOrb/Hibernate 的 Power 存在性读取改为分支查询。The Hunt 与 BookRepairKnife 的 actual/simulated 严格差分修正前分别差 `TheHuntPower` 1 层与治疗 3 点，修正后完整快照一致；HS 发现的 SILENT-C-13 第 56 步核对不一致消失，默认 79 根开启模式核对不一致为 0、逐根不变；关闭模式 79 根只有 SILENT-C-13 改变（The Hunt 奖励从 0 变为 1，120 秒预算内不再取胜），基线已更新。Doom 击杀的致命计数时机由 DT 修正。

Doom 击杀的致命计数时机（DT，2026-10-20）：原版 `DoomPower.DoomKill` 逐个击杀（每次击杀内完成死亡 Hook、移出阵容与 Power 移除），全部击杀后才调用 `AfterDiedToDoom`；现有引擎与 Flash 原先在击杀前统计致命数。两边都改为逐个击杀并补跑该次死亡效果、全部击杀后再按分支状态统计并治疗（`AfterDiedToDoom` 只有书本修复刀订阅）。新增无人测试合同 `DOOM-FATAL-TIMING-V0111`（三个千足虫节段同批 Doom 击杀，修正前模拟治疗 0、实机 9）与 `flash-dt-doom-fatal-timing`；默认 79 根关闭与开启模式逐根不变。

保留结构收尾（RS3，2026-10-20）：FlashBeam 移植回合边界整理代表、有序牌堆评估与代表（`ProjectedShuffleOrderValue`）、路由上下文 Pareto 与 direct extremes、Art of War 8 回合窗口、路由签名的来源与敌人分布指纹、药水次数超过槽位的分组和 `pure` 分派输入；stand-pat 探测遇到回合开始选择时按自动政策作答（手牌来源升序、其余降序）。新增 K4 字段（有序牌堆值、Art of War 触发、两类敌人分布指纹）全量回归零失败；原始 history-pure 与生产 pure 的差单列为 `pureRefMismatch` 诊断（0.20% 的出牌，来自补偿路径的状态相关分派）。默认 79 根相对合入前 4 好 / 2 差：REGENT-C-07/V-07 54 → 46 与关闭模式同分（路由上下文 Pareto 与 direct extremes 贡献，单结构消融关掉即回到 54），DEFECT-C-05 49 → 48，NECROBINDER-V-07 20 → 19；SILENT-C-14 38 → 40、IRONCLAD-17 56 → 59（同一组路由结构，签名维度之间有交互，未能隔离到单一结构）。药水矩阵 DEFECT-C-05 至少一瓶 30 → 23 与原搜索同分，SILENT-C-08 强制用药仍为 20（原搜索 16）。关闭模式 79 根逐根不变；48 根基准路线逐根不变、耗时 −0.3%。尚未移植：有序变动组合（`OrderedMutation*`）、结束回合压缩谱系与完整胜利分组代表；路由签名的 `ContextId` 恒为 0，`UnorderedPileKey` 用 Flash 卡字节哈希，只保持等价类。

第三幕扫描的边界与核对不一致（DX7，2026-10-20）：第三幕 DEFECT-05、IRONCLAD-11 报“规则表缺少卡牌 ABRASIVE+0”不是生成卡缺失：ABRASIVE 是内部卡牌编号 0，FlashBeam 在选择分支循环里逐个递归展开选项，递归会复用并覆盖同一个预留槽（`Pool.Reserve`），之后的分支再从外层子状态读选项卡时读到空卡；改为进入循环前把选项卡值复制到栈上，路由签名与抓取能力判断只读这份快照。内核另修三处：卡牌监听者与自动打出队列的重定位身份改为（卡牌编号、是否复制品），升级、费用修正与附魔等可变值不再影响身份，修正两张 STOMP 在值变化后少派发一次减费（IRONCLAD-27 核对不一致）与自动打出期间升级变化被误判为不支持（REGENT-10）；EndOfDays 每次施加 Doom 后先结算数量变化与死亡，再统一 DoomKill，与现有引擎同序，修正 ShroudPower 格挡在敌人死亡后丢失（NECROBINDER-26 核对不一致）。新增 4 个 `flash-dx7-*` 固定根，其中 `flash-dx7-end-of-days-shroud` 在修正前 48 条游走有 20 条失败（格挡 10 对 0）、修正后为 0。第三幕 150 根、默认 79 根与 64 个牌组的协调器开启模式中内核不支持、核对不一致均为 0，剩余 `status=boundary` 全部是药水梯度层的 `flash-potion-policy-no-route`（5 根，各 2 次）。合入后默认 79 根开启模式与 RS3 合入后逐根相同，48 根基准质量逐根相同、耗时持平。

开启模式的进度、接管与工作量（UX，2026-10-20）：FlashBeam 在层开始、每个父节点、剪枝、层结束、复核与新颖度父节点处发布进度（100 ms 节流）；路线预览、当前回合预览与路线采纳种子由共享回放核心从请求根回放 Flash 路线构造（200 ms 节流），出现完整胜利路线时发布可晋升的当前最佳结果。接管在层、出牌深度与新颖度父节点处响应：`AdoptRoute` 物化玩家选中的种子，`ApplyCurrentTurn` 物化当前回合边界候选，两者都经现有引擎逐步核对。每次 Flash 求解（成功、拒绝、核对失败或取消）恰好记录一次工作量，取代此前只在新颖度与早期回合记 Flash 展开、其余请求只记前缀回放的做法：遥测、新颖度与早期回合读实际展开数，共享节点预算与宽度组合门槛读按实际宽度倍数折算的原搜索等价节点；原搜索贡献的两个值相同。取消每个动作检查，帧压力让出在父节点粒度。`GENERATED-NOVELTY-SEARCH` 控制合同开启/关闭模式均 Passed，新增 Flash 直连检查（采纳显示路线、应用当前回合、工作切片让出、帧压力等待、取消后工作量计入，取消延迟 8–17 ms）。合入后关闭模式 79 根重跑，与基线逐根相同（固定基线中 1 根按两次重跑一致的当前结果刷新，与本批无关）；开启模式相对合入前 3 根变化，均因宽度组合的精炼成员改按原搜索口径运行或跳过：SILENT-C-14 40 → 38，IRONCLAD-17 失血相同、结束回合 9 → 11，IRONCLAD-30 +1。48 根基准质量、路线与搜索指标逐根相同，耗时 −3.6%。未覆盖：路线预览逐回合的生命变化取相邻快照差值，不含卖血与遗物标注（预览标为未验证，部署仍走完整物化）；当前最佳结果的药水战略成本未扣 Ambergris 与强制用药的修正，只影响显示候选的晋升。

开启模式不回落（SW，2026-10-20）：开启模式只运行 Flash，分发点删除全部原搜索构造点与 `FLASH_LEGACY`。只有 Flash 搜索自己判定 `flash-potion-policy-no-route` 时返回现有搜索同形的无路线结果（`FLASH_NO_ROUTE`，不算失败）；精确层下界不可用只关闭下界剪枝并照常搜索（`SMART_POTION_BOUND_UNAVAILABLE`），新增遗物目标固定根 `flash-sw2-smart-bound-relic` 的 On 与 Off 质量相同。其余情形写一行 `FLASH_SEARCH_FAILED reason=<类别>` 并停止本回合部署：准入拒绝各类别、内核不支持、根状态超容量、内核边界、核对不一致、开发策略、其它根阶段，以及 Flash 选出的路线经现有引擎复核后不满足药水约束（`potion-policy-mismatch`）；运行时关闭全自动、问题包加 `FlashSearchFailed` 与 `SearchFailure` 分类并弹出中英提示，无人测试结果带 `failureCategory`。终局过滤补齐原搜索软策略偷窃恢复的“确实用过药水”条件。离线失败合同逐类注入（85 项断言）与无头 `FLASH-SEARCH-FAILED-CONTRACT` Passed。RS3 起 stand-pat 探测在 EntropyPower 的回合开始变形选择上间歇抛出“选择 0 不符合选项规格”（第三幕 DEFECT-29、SILENT-28，64 牌组 SILENT-23）：`TryAutomaticTurnStartChoice` 的已选标记用 `stackalloc` 分配，所在类型带 `SkipLocalsInit`，残留字节让全部选项被跳过；改为显式清零，新增 `flash-sw2-entropy-transform`。四个 `SkipLocalsInit` 类型的 130 处 `stackalloc` 已逐一核对，没有其它先读后写；格挡修正的变化监听者缓冲区改为按加法与乘法两轮分配两倍容量（与伤害一致）。第三幕 150 根与 64 个牌组的开启模式失败事件与回落均为 0（SILENT-23 失血 70 → 19），20 场无头整场与其中 5 场的复测 Passed、计划外重算 0。合入后全量回归 487 根零差异；默认 79 根开启模式与 UX 合入后逐根相同（没有 `FLASH_SEARCH_FAILED`，`FLASH_NO_ROUTE` 只在 IRONCLAD-27/53），关闭模式与固定基线逐根相同；48 根基准路线逐根不变。

### 2.3 无人测试实机部署（2026-10-19）

Linux 无头游戏（Medium 档位、宽度组合、`Instant / 0 秒`，铁甲战士初始牌组，`NIBBITS_NORMAL` 原生满血）：On 与 Off 均 Passed、第 9 回合胜利、计划外重算 0；On 最终 59 HP，Off 54 HP，整场耗时 7.1 s 与 6.9 s（含建局）。On 的战后问题包有 6 行 `FLASH_ADOPTED`（全部 `status=verified`，单次 180–467 ms），`diagnostics/settings.json` 与 15 份检查点政策都记录 `"flashSearchMode": "On"`。`CHECKPOINT-PROFILE-CONTRACT` 增加 Flash 模式的记录值优先与旧包回退检查，Passed。该机器首次运行游戏前没有交互档案，用一次 `--force-steam=off` 无头启动在仓库 `.local/headless-seed/` 生成种子档案，并以 `XDG_DATA_HOME` 指向它运行脚本。

其他四个角色（TW 合入后，同一设置，`NIBBITS_NORMAL` 原生满血，同一无头实例依次运行，最后一项带 `--cleanup-instance-on-exit` 并由启动器删除实例目录）：静默猎手、故障机器人、储君、亡灵契约师的初始牌组 On 全部 Passed，分别在第 9、8、9、10 回合胜利，计划外重算 0。静默猎手、故障机器人、储君的战后问题包各有 6、6、7 行 `FLASH_ADOPTED`，全部 `status=verified`，没有回落；亡灵契约师是最后一项，问题包随实例目录一起清理，只保留无人测试结果。

第三幕首领（A3B 与 K4C 合入后，铁甲战士初始牌组，`--act-index-for-test 2` 使首领策略开启）：QUEEN_BOSS 与 AEONGLASS_BOSS 在首领生命 150、玩家生命 999 下 On 胜利（第 26、15 回合），TEST_SUBJECT_BOSS 会分阶段以原生命复活，按 `--expected-player-death` 在原生命下运行至玩家第 5 回合死亡；三场均 Passed、计划外重算 0。问题包各有 6、4、8 行 `FLASH_ADOPTED`，全部 `status=verified`，没有回落，`diagnostics` 记录 `"act3BossStrategy": true`。最后一项为带 `--cleanup-instance-on-exit` 的短战斗，启动器删除了实例目录。

多样化牌组的开启模式整场（HS，2026-10-20，`fb042139` 之后）：从 `rq-set`、`coverage/corpora/flash-decks` 与第三幕调查根中选 40 个场景（五个角色、三幕、普通/精英/首领、带药水与回合开始选择遗物），Linux 无头游戏 `Instant / 0 秒`、玩家 200 生命、敌人原生满血、开启模式整场部署：38 场 Passed，2 场为第三幕 QUEEN_BOSS 下玩家战死（关闭模式同样战死），全部计划外重算 0；问题包中 453 行 `FLASH_ADOPTED` 全部 `status=verified`。实机暴露两处核对不一致：ChainsOfBinding 本回合被缚卡计数在回合末被 Flash 提前清零（已按现有引擎改为下一玩家回合开始清零，`flash-hs-chains-turn-reset`）；SILENT-C-13 上 The Hunt 的致命判定由现有引擎经原版 `ReattachPower.ShouldOwnerDeathTriggerFatal` 读取了 live 生物生命（违反后台搜索不读 live 值的约束），Flash 读分支状态，交由现有引擎修正。10 对开启/关闭对照胜负全部相同，剩余生命开启 6 好 / 1 差 / 2 同。无人测试请求默认向手牌注入一张 `STRIKE_IRONCLAD`，用显式牌组建局时需传 `--cards-json []`。

第二轮（HS2，2026-10-20，`66c8c3f0` 之后）：另选 60 个未跑过的场景（第三幕精英与三个最终首领 16 场、事件遭遇 5 场、24 场带 2 瓶药水、回合开始选择遗物 10 场），开启模式整场 59 场 Passed（8 场按预期战死）、1 场因 1 次部署漂移重算判 Failed；问题包 567 行 `FLASH_ADOPTED` 全部核对通过。修正 SwordSagePower 下锻造生成的君王之刃重放基线（生成的刀从 0 补当前层数，`flash-hs2-sword-sage-forge`）。另报告并交后续批次：`--flash-search-mode-for-test` 晚于回合开始选择根的设置捕获；TurnBoundaryRescue 采用路线缺一个回合的续接戳导致一次重算；IRONCLAD-50 计划中 STOMP 的费用修正列表与实机差一项导致部署漂移；act3-SILENT-19 两个敌人同时差 3 点伤害的核对不一致；部分战死局问题包缺 `replay/recent/session.json`。

## 3. 已知限制与未验证项

- 只覆盖归档批次与 2.1 所列内容；其他内容在准入阶段拒绝，尚未有差分证据。
- 精确层下界不可用（遗物目标、成长目标等）时 Flash 整次搜索不做下界剪枝，原搜索只对不满足条件的节点放开；路线不受影响，搜索面更大。除遗物目标外的禁用原因没有逐个构造实机根。
- `FLASH_SEARCH_FAILED` 的提示与问题包只在无头合同中验证，未在可见游戏中查看。
- 仍在战斗中的生物身上已移除 Power 的残留条目不能投影：根导入时拒绝，路线核对中遇到时按核对失败丢弃 Flash 路线；敌方离场后的残留按惰性处理（见内容批次归档卷 2.8.1）。
- 选择覆盖卡牌自身的单选、手牌/抽牌堆/弃牌堆选择、回合开始选择、知识恶魔的固定选项与自动打出中的嵌套选择（LR、MB 固定根）；一个动作最多携带 8 个选择，更深的链按容量边界拒绝。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 实机部署只在 Linux 无头游戏中验证过 2.7 的五个角色初始牌组对 `NIBBITS_NORMAL` 与铁甲战士对三个第三幕首领；未做可见游戏、Windows 或 Steam 会话验证，设置面板的新选项未在游戏中查看。
