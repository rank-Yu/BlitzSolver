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

全量回归（`.local/tool-tasks/flash-search/regress.sh`，或按根分片并行的 `regress-par.sh`；SK、PT、DF、RG、NB、LR、MB、LS、ID、RQ、CX、TW、PF、PM、KB、SP、DX3、A3B、K4C、ST、PA、PC、DX4、PR、CP、TS、EC、SP2、DX5、SR、B1、XP、HS、LH、HS2、B2 合入后）：FlashKernelChecks 通过；162 个自然遭遇根（铁甲战士初始牌组覆盖三幕已实现遭遇的 FLASHM1/FLASHM2，另有其他四个角色的初始牌组各 6 条，以及三个第三幕首领在 `--act-index 2` 下开启首领策略的 6 条）、253 个固定根（`coverage/fixtures/cards/flash-*.json`）与 64 个生成牌组（含 29 个非铁甲战士牌组、11 个第三幕牌组）（`coverage/corpora/flash-decks/`）每根 32 条随机游走，K1 单步、K2 游走、K4 评估与 K5 下界输入全部零差异，没有准入拒绝；`unsupported` 全部为 0。

### 2.1 当前状态（LH 合入后）

- 覆盖：五个角色的卡池、296 个遗物、65 种药水、三幕全部遭遇与第三幕首领策略、事件卡牌与事件遭遇。不支持的内容在准入时给出类别：多人专用、Mock、废弃内容，以及现有引擎同样不支持的 BYRDPIP。
- 默认语料的协调器对照（Medium、宽度组合、120 秒，Off 为固定基线）：
  - 四角色 46 个牌组：On 相对 Off 12 好 / 29 同 / 5 差，战略失血差合计 −85（同为 120 秒预算）；按关闭模式不受时间截断的口径为 −71。差别全部来自 SILENT-C-13：现有引擎修正 The Hunt 致命判定后，关闭模式约需 129 秒才自然结束（240 秒预算下胜利、失血 56），120 秒预算下被截断、未取胜、失血 70；修正前的旧代码在空闲机器上 112 秒、较忙时 124 秒结束，能否在 120 秒内完成取决于机器负载。开启模式 22 秒取胜、失血 46。
  - 铁甲战士 33 个牌组：10 好 / 19 同 / 4 差，−40。
  - 79 根全部 `FLASH_ADOPTED`。开启模式仍调用原搜索的只有 4 次 `SmartPotionGradient` 的 `flash-potion-policy-no-route`，来自 IRONCLAD-27 和 IRONCLAD-53；现有求解器在这两根上同样没有满足策略的路线。
- 48 根基准（10 秒）：Flash 相对现有主搜索 14 好 / 28 同 / 6 差，失血合计 −34。
- 其余仍由原搜索执行的请求形态与原因码见 [搜索与接入](search.md) 3.2 节。

### 2.2 批次记录

截至 SP2 的已完成批次，逐批证据归档在 [覆盖补齐与协调器接管批次测试记录](../archive/flash-search/testing-batches-2026-10-20.md)。新批次在本节追加。

开启模式剩余的内核回落（DX5，2026-10-20）：SILENT-C-20 的路线核对差 9 点，根因是 Pen Nib 第十次攻击的加倍锁存泄漏到 Dagger Throw 弃牌后嵌套自动打出的 Sly 牌 Flick-Flack；锁存改按 `FlashExec.PlayIdentity` 记账，与现有引擎按卡牌对象比较一致（`flash-dx5-pen-nib-sly` 撤掉修正时 K1 差 9）。战损假人时限的最后一层补上 `FlashOutcome.EventDefeat`：跳过后续阶段、威胁投影按现有引擎、终局键追加 `PolicyBoundaryRank`；三个战损假人遭遇的自然根全部走到事件失败，协调器开启模式 `FLASH_ADOPTED:verified` 且与关闭模式同分。HISTORY_COURSE 用新增的跨回合卡值槽 `FlashBattle.HistoryCourseCard`（12,600 → 12,696 字节），自动预打出阶段克隆上一玩家回合最后一张攻击并自动打出，复制品移出战斗。6 个 `flash-dx5-*` 固定根零差异；默认 79 根与第三幕 150 根的协调器开启模式中核对不一致、内核不支持与内核边界均为 0；默认 79 根战略结果逐根不变，48 根基准质量不变、耗时 +0.2%。

保路开关与能力承诺（SR，2026-10-20）：`SecondRankBand` 与 `AggressivePowerCommitment` 改由 FlashBeam 执行，分发点删除这两个拒绝。次段剪枝在全局剪枝后执行（必保、承诺与计划席位不变）；力量承诺的席位配额、2/3 回合寿命与代表置换按原语义移植，承诺是路线节点元数据，不进 `FlashBattle` 与状态键；逐卡触发证据、开局投影与静默猎手的专用进展/兑现证据按现有引擎移植（`FlashPowerEvidence`），`RegisteredPowerBenefit` 计划收益放开；多个回合准备根的首步谱系按现有签名跨根合并（单根保持按节点分组，见 search.md）。默认 79 根相对 SR 之前 6 好 / 71 同 / 2 差、失血合计 +14：IRONCLAD-17 17 → 56 与关闭模式相同（此前 Flash 忽略承诺泳道），SILENT-C-13 +2；IRONCLAD-50 56 → 36。药水矩阵不再有 `second-rank-band` 回落，但 SILENT-C-08 强制用药 16 → 20、DEFECT-C-05 至少一瓶 23 → 30 变差，与 IRONCLAD-17 一并归因于 Flash 缺少原搜索与承诺席位平衡的保留泳道，留给后续保留结构批次。48 根基准路线逐根不变。

开局探针抽离原搜索类（B1，2026-10-20）：协调器约 40 处 `Build*` 开局/前缀探针与四个续接来源改持 `src/Search/Probes/OpeningProbe`，不再实例化 `CombatBeamSolver`；评分、指纹与威胁投影抽成原搜索与探针共用的 `SearchEvaluator`，回合推进、动作枚举、候选分类、固定前缀与选牌规格各只保留一份实现（`SearchRoundExecution`、`SearchExpansionShared`、`SearchProbeHelpers`），结构门禁禁止 `src/Search/Probes/**` 引用 `CombatBeamSolver`，并禁止这些方法在 `CombatBeamSolver*.cs` 中再有方法体。`Replay`/`ReplayAction`/`ReplayTurnSetup` 当时仍是两份算法体，B2 统一。合入后默认 79 根关闭与开启模式均与改动前逐根相同（路线与质量），48 根基准路线逐根相同，8 个探针相关无人测试合同 Passed；`CARD-CONTINUATION-CONTRACT` 在改动前的基线上同样失败（找不到 `DAGGER_THROW` 弃牌堆实例），属于既有问题。

统一回放核心（B2，2026-10-20）：`Replay`/`ReplayAction`/`ReplayTurnSetup`/`ResolveTurnSetupChoices` 与准备根枚举收敛到 `src/Search/Probes/SearchReplayCore.cs`，原搜索与探针共用；检查点、执行续接帧、回放证据、性能计量与增量校验按显式上下文注入，准备根的选择预算只有一份。Flash 的桥接与物化改用 `SearchReplayCore`、`OpeningProbe` 与共享的 `SearchRouteMaterializer`，`src/Search/FlashBeam/**` 不再引用 `CombatBeamSolver`（结构门禁强制），开启模式只在分发点的回落处构造原求解器。合入后默认 79 根关闭与开启模式均与改动前逐根相同，回合准备根与 48 根基准路线逐根相同，回放续接相关无人测试合同 Passed；`CARD-CONTINUATION-CONTRACT` 的既有失败查明为夹具在出牌收尾前检查弃牌堆。

新颖度探索与早期回合侦察（XP，2026-10-20）：新颖度探索、自适应新颖度精炼与早期回合侦察三个协调器成员改走 `BeamSolveDispatch`，开启模式由 FlashBeam 执行（`FlashBeamSolver.Novelty.cs`）：BFWS 开放队列按（新颖宽度、−分数、序号）排序，新颖事实与分区照现有 `CaptureNoveltyFacts`（`FlashNoveltyFacts`，新增 6 个 K4 字段逐事实核对），熟悉子状态共享祖先的逃逸预算，停止条件与诊断字段与原搜索同口径；早期回合侦察按原策略键每层取 24 个候选，物化并核对后才回报观察者；计划侦察的深度限制放开。这三个成员默认关闭，默认 79 根逐根不变；另选 DEFECT-C-05、IRONCLAD-04、SILENT-C-14 三根在两种配置下稳定触发并全部 `FLASH_ADOPTED`（相对关闭模式 1 好 / 2 差、失血合计 +4，变差来自既有保留结构差异），新颖度控制合同与早期回合无头合同开启/关闭模式均 Passed。尚未移植：新颖度终局的 `RankFinal` 预筛、搜索中途的接管响应（Flash 只在搜索结束时响应）、早期回合续搜的工作量记账。

致命击杀判定改读分支状态（LH，2026-10-20）：现有引擎的 The Hunt、Feed、HandOfGreed 与 BookRepairKnife 致命判定原经原版 `ReattachPower.ShouldOwnerDeathTriggerFatal`/`AreAllOtherSegmentsDead` 读取根上 live 生物的生命，分支内其他千足虫节段已死时仍判为不致命；改为 `SimulatedCombatState.ShouldOwnerDeathTriggerFatal` 按分支 Power 与分支死亡状态计算，同一修正把 Surrounded/BackAttack 与 FrostOrb/Hibernate 的 Power 存在性读取改为分支查询。The Hunt 与 BookRepairKnife 的 actual/simulated 严格差分修正前分别差 `TheHuntPower` 1 层与治疗 3 点，修正后完整快照一致；HS 发现的 SILENT-C-13 第 56 步核对不一致消失，默认 79 根开启模式核对不一致为 0、逐根不变；关闭模式 79 根只有 SILENT-C-13 改变（The Hunt 奖励从 0 变为 1，120 秒预算内不再取胜），基线已更新。Doom 击杀的致命计数仍在击杀前进行（原版在全部击杀后），另行修正。

### 2.3 无人测试实机部署（2026-10-19）

Linux 无头游戏（Medium 档位、宽度组合、`Instant / 0 秒`，铁甲战士初始牌组，`NIBBITS_NORMAL` 原生满血）：On 与 Off 均 Passed、第 9 回合胜利、计划外重算 0；On 最终 59 HP，Off 54 HP，整场耗时 7.1 s 与 6.9 s（含建局）。On 的战后问题包有 6 行 `FLASH_ADOPTED`（全部 `status=verified`，单次 180–467 ms），`diagnostics/settings.json` 与 15 份检查点政策都记录 `"flashSearchMode": "On"`。`CHECKPOINT-PROFILE-CONTRACT` 增加 Flash 模式的记录值优先与旧包回退检查，Passed。该机器首次运行游戏前没有交互档案，用一次 `--force-steam=off` 无头启动在仓库 `.local/headless-seed/` 生成种子档案，并以 `XDG_DATA_HOME` 指向它运行脚本。

其他四个角色（TW 合入后，同一设置，`NIBBITS_NORMAL` 原生满血，同一无头实例依次运行，最后一项带 `--cleanup-instance-on-exit` 并由启动器删除实例目录）：静默猎手、故障机器人、储君、亡灵契约师的初始牌组 On 全部 Passed，分别在第 9、8、9、10 回合胜利，计划外重算 0。静默猎手、故障机器人、储君的战后问题包各有 6、6、7 行 `FLASH_ADOPTED`，全部 `status=verified`，没有回落；亡灵契约师是最后一项，问题包随实例目录一起清理，只保留无人测试结果。

第三幕首领（A3B 与 K4C 合入后，铁甲战士初始牌组，`--act-index-for-test 2` 使首领策略开启）：QUEEN_BOSS 与 AEONGLASS_BOSS 在首领生命 150、玩家生命 999 下 On 胜利（第 26、15 回合），TEST_SUBJECT_BOSS 会分阶段以原生命复活，按 `--expected-player-death` 在原生命下运行至玩家第 5 回合死亡；三场均 Passed、计划外重算 0。问题包各有 6、4、8 行 `FLASH_ADOPTED`，全部 `status=verified`，没有回落，`diagnostics` 记录 `"act3BossStrategy": true`。最后一项为带 `--cleanup-instance-on-exit` 的短战斗，启动器删除了实例目录。

多样化牌组的开启模式整场（HS，2026-10-20，`fb042139` 之后）：从 `rq-set`、`coverage/corpora/flash-decks` 与第三幕调查根中选 40 个场景（五个角色、三幕、普通/精英/首领、带药水与回合开始选择遗物），Linux 无头游戏 `Instant / 0 秒`、玩家 200 生命、敌人原生满血、开启模式整场部署：38 场 Passed，2 场为第三幕 QUEEN_BOSS 下玩家战死（关闭模式同样战死），全部计划外重算 0；问题包中 453 行 `FLASH_ADOPTED` 全部 `status=verified`。实机暴露两处核对不一致：ChainsOfBinding 本回合被缚卡计数在回合末被 Flash 提前清零（已按现有引擎改为下一玩家回合开始清零，`flash-hs-chains-turn-reset`）；SILENT-C-13 上 The Hunt 的致命判定由现有引擎经原版 `ReattachPower.ShouldOwnerDeathTriggerFatal` 读取了 live 生物生命（违反后台搜索不读 live 值的约束），Flash 读分支状态，交由现有引擎修正。10 对开启/关闭对照胜负全部相同，剩余生命开启 6 好 / 1 差 / 2 同。无人测试请求默认向手牌注入一张 `STRIKE_IRONCLAD`，用显式牌组建局时需传 `--cards-json []`。

第二轮（HS2，2026-10-20，`66c8c3f0` 之后）：另选 60 个未跑过的场景（第三幕精英与三个最终首领 16 场、事件遭遇 5 场、24 场带 2 瓶药水、回合开始选择遗物 10 场），开启模式整场 59 场 Passed（8 场按预期战死）、1 场因 1 次部署漂移重算判 Failed；问题包 567 行 `FLASH_ADOPTED` 全部核对通过。修正 SwordSagePower 下锻造生成的君王之刃重放基线（生成的刀从 0 补当前层数，`flash-hs2-sword-sage-forge`）。另报告并交后续批次：`--flash-search-mode-for-test` 晚于回合开始选择根的设置捕获；TurnBoundaryRescue 采用路线缺一个回合的续接戳导致一次重算；IRONCLAD-50 计划中 STOMP 的费用修正列表与实机差一项导致部署漂移；act3-SILENT-19 两个敌人同时差 3 点伤害的核对不一致；部分战死局问题包缺 `replay/recent/session.json`。

## 3. 已知限制与未验证项

- 只覆盖归档批次与 2.1 所列内容；其他内容在准入阶段拒绝，尚未有差分证据。
- 仍在战斗中的生物身上已移除 Power 的残留条目不能投影：根导入时拒绝，路线核对中遇到时按核对失败丢弃 Flash 路线；敌方离场后的残留按惰性处理（见内容批次归档卷 2.8.1）。
- 选择覆盖卡牌自身的单选、手牌/抽牌堆/弃牌堆选择、回合开始选择、知识恶魔的固定选项与自动打出中的嵌套选择（LR、MB 固定根）；一个动作最多携带 8 个选择，更深的链按容量边界拒绝。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 实机部署只在 Linux 无头游戏中验证过 2.7 的五个角色初始牌组对 `NIBBITS_NORMAL` 与铁甲战士对三个第三幕首领；未做可见游戏、Windows 或 Steam 会话验证，设置面板的新选项未在游戏中查看。
