# Flash 搜索与接入

[返回 Flash 入口](README.md)

## 1. FlashBeamSolver

FlashBeam 在 `FlashBattle` 上做按回合分层的 Beam 搜索，产出候选路线，不直接产出 `SolverResult`。

- **容器复用**：每层是跨层复用的 `FlashBattle` 分块存储；子状态先复制父状态到当前块的第一个空槽（`Pool.Reserve`），在槽位上原地执行动作，按需提交（去重）、复制到回合结束层或作为终局丢弃；求解器与评估类型都跳过局部变量清零（`SkipLocalsInit`）。分数、父指针、动作等元数据也按类型分块。状态块固定 64 槽（64 × 12,408 B = 794,112 B）；其余平行数组按 `Unsafe.SizeOf<T>()` 选 ≥ 85,000 B 的 2 的幂块（例如 `RetentionFacts` 512 项、`int`/`double`/`ulong` 16K–32K 项），因此每块直接进 LOH，不在 Gen0/Gen1 反复晋升，也没有小于 LOH 阈值的状态数组。块只追加、不复制，增长不再重分配；`_order`/`_rankOrder`/`_rankPosition` 这类必须连续排序的 scratch 按容量上界一次分配。`Reserve` 在块边界或容量边界使用单个 scratch 槽，重复状态与终局状态不占新槽、不触发新块。保留层与回合前沿固定为 Beam 宽度；子状态层与回合结束层的显式容量边界仍是 `width × (MaxLegalActions + 1)`，超过时整次 Flash 尝试回落。这个边界只是拒绝边界，不是 `ended` 的真实上界：`ended` 在一个回合内按动作深度累积终止子状态，REGENT-V-06 实测单回合可到 434 层、约 10.4 万个终止子状态；把上界抬到该真实需求会把峰值抬到 3.4 GB，故保留显式边界并用整次回落保证与现有引擎的结果口径一致。曾试过按进程缓存 64 MiB 状态块：块在 Medium 上确实命中（SILENT-C-13 每次求解约 160–300 块），但保留 64 MiB 后碎片 180 MB（分块无池 30–36 MB、基线 0.6 MB）、峰值 419 MB（分块无池 330–355 MB），故未保留，当前每次 `Solve` 新建池。
- **路线不进状态**：每个节点只记父节点下标与本步 `FlashAction`，找到候选后沿父指针倒推。
- **去重**：每层用开放寻址表按指纹分桶，命中后比较完整字节；同一状态只保留排序值最高的代表。旧引擎转置表的六维 Pareto 保留（药水数、药水战略成本、未来卖血、累计失血、动作数、分数）尚未移植。
- **内核派发**：`FlashHooks.CollectListeners` 在没有任何卡牌、苦难或附魔定义实现当前 Hook 时跳过整个卡区扫描；该判定来自不可变内容注册表的类型并集（`FlashRegistry.CardHooks`/`CardHooksExt`），逐项等价于对每张牌执行 `Has` 后必然不命中，因此不改变监听顺序或成员。费用、伤害与 HP 损失投影这几类 Hook 没有卡区监听者，直接受益。
- **层内保留**：先按 `CompareBeamRankOrder`（排序值、动作数、进攻进度）取前 Beam 宽度个状态；请求 profile 的软时间预算不超过 30 秒（10 秒自然根基准与短预算）时其中 `OffensiveShare`（25%）的席位留给敌人 HP 最低的状态，较长的协调器预算不保留这条 Flash 特有的进攻泳道。再照 `BeamRetentionPolicy.RankBest` 的必保置换，把现有搜索的必保代表取代排名末位的普通席位：每个药水分组与全局的防守、进攻、准备（`SetupLaneValue` 与 Scaling+Resource+Control 组合，资源道含星能与可达手牌价值）、牌堆整理（AttackDensity / LiveDeckClutter）、最压缩牌堆、力量与虚弱控制、战术铺垫（`ZeroCostPlayableCount`/`ReachableHandValue`）领先者；八条泳道（Scaling、Resource、Control、RevivalWindow、ReactiveDamage、EndTurnDeckCompression、LongTermResource、HpInvestment）领先者；持续铺垫特征分组与焦点目标分组的防守/准备领先者（分组键用 `PersistentSetupTraits`；现有搜索用 `PersistentSetupTraits | LatentSetupTraits` 的 `StrategicSetupTraits`，Flash 的 4× Beam 会把逐卡潜在标签扩成远多于现有的组，故只用持续项，K4 仍逐字段核对两者）；回合边界的 `FindBestTurnBoundaryHand` 领先者；以及按首次根动作分组的谱系代表（`clamp(W/8, 4, 16)` 组，每组取排序、防守、进攻、准备、准备组合、战术、整理、压缩与目标压力代表）。必保成员只换席位，不增加 Beam 宽度；排序、分组与比较器读取的字段都由 K4 逐字段核对。`FindBestLane` 在现有搜索里按节点累计的 `SearchRouteTraits` 过滤，Flash 尚未建模该标签，泳道领先者直接在整组上比较（取到极值的状态通常正带该标签）。旧引擎的选项路由配额 `max(8, 2W/5)` 与 option-leader 轮转、药水使用配额与药水谱系（`RankBest` 的 `PotionUseLineageKey` 组）、stand-pat 探测与 `FindBestFreshResourceStandPat`/`FindBestDamagingContinuation` 回放、Pareto、循环/跨回合/有序变动组合、力量承诺、回合边界整理代表（`FindBestCuratedTurnBoundaryHand`）、完整胜利分组代表（Flash 终局不进保留池）与偷窃资源领先者尚未移植。
- **首层承诺展开**：策略开启时，FlashBeam 的第一个回合层照现有引擎 `CombatBeamSolver.Phases.cs` 的 `searchedTurnLayers == 0` 分支，对 `HasPlayableFetchedPower` 等价状态（该动作的主选择效果为 `MoveToHand`、取回的能力牌已在手且付得起）先执行其全部合法后继再参与本层排序；中间选择状态不提交，后继照常走节点计数、容量、去重与终局提交。条件按 `CardChoiceSupport` 的主选择来源（8 张取回牌）与 Droplet of Precognition 药水逐项对照，策略关闭或其选择未取回可支付 Power 时不生效。现有引擎的回合层局部墙钟切片在 FlashBeam 中本来就不存在（Flash 每层固定按 Beam 宽度截断，只有全局节点/时间预算），因此策略开启时无需额外关闭切片。
- **评分**：由 `FlashScoreBridge.Create` 在根上编译逐内容数值表（卡牌估值与动态变量增量、计算伤害乘数、潜在布置值与类别、Power 的战略效果分类/参数、药水战略成本），冻结评估读取的 `Initial*` 与 policy 值，返回 `FlashScorer`。`FlashScorer.Features(in FlashBattle, actionCount)` 逐项移植旧引擎 `CombatBeamSolver.Snapshot`（`src/Search/CombatBeamSolver.StateEvaluation.cs`）：同样的项、同样的运算顺序与 `checked`/取整位置，特征直接从 `FlashBattle` 计算，不构造 `SimulationSnapshot`。节点分数与终局比较用 `Features(...).Score`；层内保留排序用 `FlashScorer.BeamRankScore`（旧引擎 `BeamRetentionPolicy.BeamRankScore`），连续威胁与上下文排序修正也在同一个排序值上。K4 在同一状态逐字段核对每个特征（成长/遗物字段与 `Turn` 含在内），`Score` 按 double 逐位比较；差分探针在开启这两个 profile 开关时另按 double 比较同一状态的 `BeamRankScore`（`rankChecked`/`rankFailed`）。
- **成长与遗物计数目标**：`SearchPolicySnapshot.EffectiveGrowthBudgets` 与 `RelicTargets` 冻结进 `FlashScoreRules`，不再在准入时拒绝。内核用五个打包计数器承载 `SimulatedCombatState._growthRewards` 的十个原版来源（`HandOfGreed`/`TheHunt`/`Feed` 的致命击杀、`Royalties`/`Alchemize`/`GeneticAlgorithm`/`TheScythe`/`Goopy` 的成功结算与牌组副本门控；ForbiddenGrimoire/MadScience 没有 Flash 定义，其累计值只在根导入历史时保留），根导入从同一字段读取历史；十个来源的 16 位计数两两打包进现有计数器数组，`FlashBattle` 布局与 `ExpectedSize` 不变。遗物目标按 `RelicCounterCatalog` 的周期与范围编译到对应 `FlashRelic.Counter`（Meat on the Bone 用当前 HP 阈值重算），`FlashScorer` 复刻 `EvaluateRelicCounters` 的 `SatisfiedMask`/`TargetMask`/`HpCredit`/`Distance`/`SatisfiedPriority` 与胜利后的 `HealingHpCredit`。评分项、`StrategicHpCredit`、`StrategyGoalHpCredit`、`StrategyGoalCount` 与终局排序键（战略失血之后依次比较成长信用与收益数）与现有快照逐项一致；有界成长目标与遗物目标也进入 HP 目标早停条件。第三方 `GrowthSourceMirrors` 额度/目标仍按 `UnsupportedRequest` 拒绝。`Profile.ContinuousThreatRanking` 与 `Profile.ContextualRanking` 也在此层移植：前者只在 EndTurn、存活、投影 HP ≤ 0 且仍有可支付手牌时把死亡罚分换成连续 HP 缺口；后者把同一组 K4 特征映射进 `ContextualRankingModel.Input`，逐状态得到与现有 `BeamRankScore` 相同的修正，终局/非存活上下文仍绕过。`DevelopmentStrategy` 仍拒绝（见下文）。

- **第三幕首领策略**：`SearchPolicySnapshot.Act3BossStrategy` 冻结进 `FlashScoreRules`，不再在准入时拒绝。评分表按该开关编译 `StrategicEffectModel.Requirements`，`Lethality`、`Pagestorm`、`DanseMacabre`、`Demesne`、`PrepTime` 的 `Act3BossInteractions` 估值分支逐项照搬；`FirstAttackDamage` 与 `CaptureAct3BossInteractionPotential` 的 `EtherealDrawTriggers`、`PagestormBonusDrawCapacity`、`HighEnergyPlays`、`DemesneEnergyGain`、`DemesneDrawGain` 从 `FlashBattle` 重算，策略关闭时不进入这些读取。六个新特征连同 `Act3BossInteractions` 标记进入 K4 逐字段核对（参考侧由 `SimulationSnapshot` 保存同一状态的上下文值）。
- **评估的保留专用值**：`ReachableHandValue` 与 `ZeroCostPlayableCount` 只被保留比较器读取，不进入 `Score`、`BeamRankScore` 或终局 `RouteKey`。`Score` 调用 `Features(..., includeRetentionOnly: false)` 跳过它们；状态真正进入保留池时 `Pool.Insert` 才计算一次，重复状态（字节相同的代表）沿用已存的值，终局状态完全不计算。K4 仍用默认的 `Features(...)`（`includeRetentionOnly: true`）逐字段核对这两项。可达手牌估值直接复用 `FlashKernel.CanPlayCard(..., out energyCost, out starCost)` 已经算出的费用，不再对同一张牌、同一牌堆重复执行能量/星能费用 Hook。
- **评分表**：每张已准入的卡牌按（内容 Id, 升级等级）有明确条目；每个 Power 有战略效果分类与参数。旧引擎判定不贡献的项显式记为 0（例如非玩家侧 Power、临时 Power、`StrategicEffectMirrors.Contributes` 为 false 的项）。未移植的逐内容乘数或请求设置在根上以 `FlashRejection.UnsupportedCard` / `UnsupportedRequest` 拒绝，不默认记 0。
- **选择**：遇到 `NeedsChoice` 时用 `FlashChoiceEnumerator` 按选项规格枚举（选择数量从小到大、同数量按下标字典序），每个请求至多探索 32 个分支（v1）；每个被执行的分支都是精确的，上限只限制探索面。旧引擎按估值排序挑分支（`MaxPile/HandChoiceBranchesPerAction`），v1 尚未移植这套排序。
- **药水**：Bridge 在根上取现有搜索的药水约束（`CapturePotionPolicyForFlash`：每个槽位是否允许显式使用、战略 HP 成本、最早回合、最多使用次数），换算成 Flash 玩家回合后放进 `FlashPotionPolicy`；未设置时不使用药水。FlashBeam 只对仍是根上原药水的槽位生成药水动作。旧引擎的 `Score` 不含药水成本，因此 FlashBeam 在**排序值**上额外减去已用药水的战略成本（`SolverWeights.Hp` 计价）；这是 Flash 特有的项，节点分数与终局比较仍用 `Features(...).Score`。固定前缀已经用掉的药水不在这段槽位差里：Bridge 把搜索根快照的累计 `PotionUseCount`/`PotionStrategicCost`/`ExplicitPotionStrategicCost` 与自动用药次数冻结进 `FlashScoreRules` 和 `FlashBoundFeatures`，评估与下界再加回这些前缀计数；`ExplicitPotionUseCount` 是新增的 K4 字段。导入搜索根时 `FlashStateImporter.ImportPotions` 按搜索根的累计 `PotionUseCount` 跳过前缀已消费的用药记录，只把搜索根之后的用药计入 `GeneratedPotionUseCount`/`GeneratedPotionStrategicCost`；否则前缀用药会同时出现在 `Initial*` 与 generated 计数里，精确用药层会看到多一次。`DropletOfPrecognition` 的抽牌堆选牌（现有引擎 `PotionChoiceSupport` 的 `MoveToHand`/`Draw` 精确选择）已在内核里请求选择并按 `AddToPile(Bottom)` 移动；Bridge 的 `TryBuildPotionChoice` 对所有 `PotionChoiceSupport.RequiresChoice` 药水从探测回放构造 `PlanAction.Choice`。
- **精确用药层（Smart 审计）**：`SmartPotionGradient` 与 `SmartOpeningPotionPosterior` 请求由 FlashBeam 搜索时，`FlashBeamOptions.AuditPotionPolicy` 打开现有 `FinalPlanOrdering.Select` 的移植：按 `minimumPotionUses`/`MaximumPotionUses` 过滤，`RequireAtLeastOne` 的 `PotionUsePolicy.IsEligible`、`PotionFreePolicyBaseline`、死亡保护、Ambergris 限制，并按同一次序（含 `PolicyHpDeficit`）选路线。找不到满足策略的路线时返回 `PotionPolicySatisfied=false` 并回落现有求解器，不替它抛 `PotionPolicyUnsatisfiedException`。`_smartPotionEligibilityHpCeiling` 由现有构造函数的共享静态入口计算，Flash 在每次保路后按已由 K5 核对的 `FlashBoundValues` 逐状态剪枝（组件证书未知的分支保留）。药水前缀会先由现有引擎 `ApplyFixedPrefix` 回放，因此前缀可以以药水开头。其余药水策略（强制使用、计划承诺、最早药水回合等）仍在分发点按边界回落。
- **早停**：照抄现有搜索的两种早停：HP 目标（请求可在 HP 目标处停止、且不要求显式药水时，一次无死亡豁免、显式药水不超过额度、本场已失血加路线失血不超过可接受值的完整胜利，在下一个父节点之前结束搜索）与零伤害（胜利且未失血、未用显式药水、满血且最大生命未减少时，当前回合结束后停止）。
- **incumbent 下界剪枝**：移植现有主搜索成员的局部 HP 下界（`CombatBeamSolver.Retention.cs` 的 `CanUseLocalPrimaryHpBound`、`IsLocalPrimaryHpBucket`、`ApplyPrimaryIncumbentBound`/`ApplyPrimaryIncumbentBoundCore`、`ShouldPruneByPrimaryIncumbent`、`TryTightenPrimarySearchIncumbent`、`TightenPrimarySearchIncumbentAtTurnLayer`）。`FlashBoundFeatures` 在根上冻结 `FutureHealPotential`、`HasRisk`、剩余治疗额度（Component/KnownNative 证书）、战略首领减伤与初始 incumbent；求值只读 `FlashBattle` 和冻结表。终局完整胜利在提交时收紧 incumbent（与现有搜索的同一层 Prune 对齐）；现有搜索的回合结束收紧作用于同一批已提交的 `completed` 终局，Flash 在提交时已经收紧，因此不重复。每个动作层保留之后与回合边界保留之后各剪一次；被剪掉的席位不补。参数沿用现有语义：`allowTurnTieBound` 由 `costClosed` 门控，`pruneEqualHp` 只对非终局、无风险且药水成本闭合的节点生效，`costClosed` 读 `ExplicitPotionStrategicCost`。某个下界输入在 Flash 上无法精确给出时该根整体禁用剪枝，汇总行写 `bound=disabled(<原因>)`，不使用近似值。共享的 `PrimaryIncumbents` 表不接入，避免对照模式的隔离泄漏；初始 `PrimaryIncumbent` 由请求传入并按 `FlashBattle.PlayerTurn` 相对根的偏移换算。
- **容量边界**：子状态在执行中超出 `FlashBattle` 的固定容量（卡牌、Power、敌人等，`FlashCapacityException`）时只丢弃该子状态，搜索继续；这与节点预算一样是搜索边界，采用的路线仍逐步核对。诊断行的 `flash_capacity_pruned` 记录丢弃数。搜索层面的容量（状态池上限）与内核不支持的语义（`FlashUnsupportedException`）仍让整次 Flash 尝试回落。
- **预算**：取同一 `SolverSearchProfile`：Beam 宽度与全局节点上限乘以 `FlashRouteMaterializer.ProfileWidthScale`（4），软时间不变。FlashBeam 只有排序泳道与进攻泳道，需要更宽的 Beam 才能达到现有搜索的路线质量（[搜索演进测试记录](../archive/flash-search/testing-search-2026-10-19.md) 2.3）。旧引擎按回合层分配节点预算的规则尚未移植。
- **产出**：按旧引擎终局比较器（`FinalPlanOrdering.Select` 的排序键）选出一条路线：完整胜利、存活、死亡保护次数、战略失血（`ActEndingBossPolicy.StrategicHpDeficit`，含成长/遗物信用与战后遗物回复）、成长信用、成长收益数、结束回合、生命资源消耗、长期资源、愤怒复制数、可选药水数、敌人 HP、分数、动作数。没有胜利路线时，预算耗尽时的回合起点状态与终局状态一起参与比较。偷窃、卖血与边界原因这几个键尚未移植。

## 2. 路线物化

`FlashRouteMaterializer` 把候选路线交给旧引擎：

1. 从根分叉旧模拟器，逐步回放 Flash 动作。每一步按手牌位置取牌，按旧引擎的规则生成 `PlanAction`（`CardStateKey`、occurrence、`TargetCombatId`、选择令牌），并要求旧引擎这一步的投影与 Flash 逐字节一致。
2. 把整条已核对路线作为 `fixedPrefixActions` 交给现有 `CombatBeamSolver`，用极小预算求解。现有求解器自己回放并校验前缀（`ApplyFixedPrefix`），按既有终局排序与物化逻辑生成 `SolverResult`。协调器的续用补搜已经使用同一入口，所以不需要提取 `MaterializeSelectedRoute`。

固定前缀续接在导入前先用同一 `ApplyFixedPrefix` 规则回放请求前缀（`CombatBeamSolver.ReplayFixedPrefixForFlash`），把前缀末尾状态作为 Flash 搜索根；步骤 1 从该状态开始核对，步骤 2 的 `fixedPrefixActions` 是“请求前缀 + Flash 路线”，仍由现有求解器从请求根完整回放与物化。

实现在 `src/Search/FlashBeam/Bridge/FlashRouteMaterializer.cs`。

回放中任何一步与 Flash 投影不一致都按 [运行时回放核对](admission-and-verification.md#4-运行时回放核对) 处理。

固定前缀节点按"未比较的回合结果"标注（`CreateUncomparedTurnOutcome`），与协调器续用路线的口径相同；因此 Flash 物化结果的"卖血"统计与完整旧搜索不同，比较路线质量时以战损、胜负与结束回合为准。

## 3. 请求参数

协调器宽度组合的成员（`DirectSearchPurpose.PrimaryBeam` 与 `RefinementBeam`）、Smart 药水梯度的直接审计（`DirectSearchPurpose.SmartPotionGradient`，经同一 `BeamSolveDispatch.Solve` 入口）、协调器直接药水审计点以及带药水约束的固定前缀续接请求经过 Flash。中途排序的 profile 变体按现有 `BeamRankScore` 移植：仅基础分成员只用基础分，权重扰动对非终局状态把指定项乘以系数；连续致命意图定价与上下文排序已移植，不再因这两个 profile 开关拒绝。`DevelopmentStrategy` 的 `Rank` 只读 `StrategyNodeFeatures`，其中每个值都能由 K4 核对过的 Flash 特征给出；但 `Prioritize` 依赖现有 `ActionCandidate`（含每个候选的伤害、格挡、消耗、归一化值与 `MaxCardBranchesPerNode` 截断），`Retain` 依赖 `RankBest` 的 `preserveDefensiveRoute`/`finalQualityFirst` 必保通道，Flash 没有这些结构与模式标志。只搬 `Rank` 会静默丢掉另外两个钩子的排序调整，因此本批仍拒绝；可选方案是为开发策略定义一份只含 Flash 特征的输入/接口（例如 `Rank` 与成员组织），由协调者决定。

`BeamSolveRequest` 各字段在 Flash 中的处理：

| 字段 | 处理 |
| --- | --- |
| `Root`、`DisplayNames`、`BattleDamage`、`Policy`、`CancellationToken`、`Profile` | 支持；FlashBeam 的宽度与节点为 `Profile` 的 4 倍，时间取自 `Profile` |
| `PotionPolicyOverride` | 支持：回放求解器与物化求解器带同一覆盖，`CapturePotionPolicyForFlash` 据此取槽位约束 |
| `ProgressCallback` | 只在物化阶段报告；FlashBeam 搜索期间不报告进度 |
| `PrimaryIncumbent` | 支持：作为 FlashBeam 的初始 incumbent，`CombatEndedTurn` 从 `node.Turn` 换算到 Flash 玩家回合；共享的 `PrimaryIncumbents` 表不接入 |
| `Purpose` | 决定是否查看模式，并作为物化求解器的工作量归因 |

`CombatBeamSolver` 的其余构造参数（`maximumPotionUses`、`minimumPotionUses`、`earliestPotionTurn`、`fixedPrefixActions`、`potionFreePolicyBaseline`、`earlyTurnScout*`、`planCommitment`）在主搜索成员中不使用。Smart 梯度审计通过 `BeamSolveRequest.PotionFreePolicyBaseline`、`MaximumPotionUses`、`MinimumPotionUses` 携带精确层参数；区间上下界、基线、最早用药回合、强制指令与 `RequireAtLeastOne` 均已移植（见 3.1）。固定前缀续接通过第二个分发入口 `BeamSolveDispatch.SolveContinuation` 走同一 `FlashMode`；药水相关的续接请求不再被“干净请求”门挡住，只有下列语义边界仍直接调用现有求解器：

- `Commitment != null`（跨回合计划视野、`CopyPower` 代表席；由 PC 批次处理）。
- `EarlyTurnScoutDepth != 0` 或 `EarlyTurnScoutObserver != null`（早期侦察前沿）。
- 根不在普通玩家出牌阶段（`IncludeTurnSetup` 或阶段不是 `Play`；回合准备根协议留给后续批次）。
- 药水上下界为负或 `MinimumPotionUses > MaximumPotionUses`（沿用现有求解器的参数校验）。

`PolicyOverride` 作为求解政策使用；`PrimaryIncumbent` 按 SB 的口径传入。`ProgressCallbackOverride` 只影响现有求解器的进度报告，Flash 搜索期间不报告进度。`ResetFixedPrefixSchedulingBaseline` 对 Smart 药水后验请求转交给物化求解器，使固定前缀后的调度基线与现有搜索相同；其余续接请求仍不读取它。profile 中 Flash 不建模的开关（连续致命意图、上下文排序等）仍由准入拒绝并回落。

### 3.1 放开的药水续接与精确层

续接请求的 `MaximumPotionUses`/`MinimumPotionUses`、`PotionFreePolicyBaseline`、`EarliestPotionTurn`、`RequireAtLeastOne`（政策或覆盖）、`Smart` 政策无 `Disabled` 覆盖以及强制用药指令都交给 Flash。带任意一项药水约束、或续接的有效政策为 `Smart`（无 `Disabled` 覆盖）的请求打开 `FlashBeamOptions.AuditPotionPolicy`：Flash 按显式用药次数收集每档最佳终局候选，再用移植的 `FinalPlanOrdering.Select` 过滤与排序：

- **区间**：`ExplicitPotionUseCount >= MinimumPotionUses`，扩展期由 `MaximumUses` 限制用药动作；两者不再要求相等。计数都包含固定前缀。
- **基线**：`PotionFreePolicyBaseline` 作为审计后的无药基线；无覆盖时取本次搜索的最佳无药候选。
- **排序键**：完整移植 `FinalPlanOrdering.Select` 已由 K4 核对的特征键（胜利、存活、死亡保护、偷窃恢复、战略失血、成长信用与收益、结束回合、政策失血、生命资源消耗、长期资源、愤怒复制、可选药水数、敌人 HP、分数、动作数）。卖血与边界原因两个键仍未移植（沿用原有缺口）。
- **强制指令**：按 `(槽位, 药水身份)` 的集合逐条核对，等价于 `PotionStrategySnapshot.EvaluateForcedUses`；前缀已用的强制药水从固定前缀动作折入。未满足的路线不能通过终局过滤，同次数下不会遮蔽满足的路线。强制使用次数与战略成本从可选用药中扣除后再套用 `RequireAtLeastOne`/`Smart` 资格。
- **最早用药回合**：`EarliestPotionTurn` 按请求根回合换算后限制 Flash 的用药动作；物化后再核对没有更早的用药动作。

物化后的 `FlashPotionBridge.VerifyRoute` 复核动作与显式用药计数、最小/最大次数、最早回合、`RequireAtLeastOne` 与强制指令身份；任一不满足按 `ConstraintFailure` 回落现有求解器。现有求解器的 `FinalPlanOrdering` 仍会在物化阶段拒绝不合规路线（`materialize-potion-policy`）。

本批新放开的请求如果带 Flash 尚未建模的保路开关（`SecondRankBand`、`AggressivePowerCommitment`），分发点保留原求解器并写 `FLASH_LEGACY reason=can-take-over:second-rank-band|aggressive-power-commitment`；不能让 Flash 静默忽略这些开关。`BaseScoreOnly` 已由评分桥移植，不触发该边界。此前已经是“干净请求”的续接保持原行为（其 profile 本来就会交给 Flash）。

`SmartOpeningPotionPosterior` 续接由独立的 `PotionAuditBoundary` 判断：只保留“非 Smart 政策”和“根无可搜索药水”两个语义边界（前者表示该审计本身不适用，后者表示无药可审计）。计划承诺与早期回合侦察仍按上面的语义边界回落。

### 3.2 `FLASH_LEGACY` 可观测性

开启模式（`FlashMode == On`）下任何一次真正执行的原搜索都写一行稳定日志：

```
[CombatSolver/Flash] FLASH_LEGACY purpose=<用途> reason=<原因> ms=<整数毫秒>
```

关闭（Off）与对照（Shadow）模式不写。分发点覆盖 `SolveExisting`/`SolveExistingContinuation`；协调器里仍由原搜索直接执行的实验/救援位置（`NoveltyExploration`、`AdaptiveNoveltyRefinement`、`EarlyTurnScout`、`TurnBoundaryDiscovery` 的三个剖面变体中 Flash 未建模的两个）也写同一行。原因码使用 FI 盘点第 2 节的稳定前缀：

| 原因 | 含义 |
| --- | --- |
| `potion-audit-boundary:<码>` | Smart 梯度/后验的语义边界（`potion-policy`、`no-searchable-potion` 等） |
| `can-take-over:<码>` | 续接门保留的语义边界（`commitment`、`early-turn-scout`、`turn-setup`、`potion-bounds`、`second-rank-band`、`aggressive-power-commitment`） |
| `direct:<用途>` | 协调器直接执行的原搜索（`direct:novelty-exploration`、`direct:early-turn-scout` 等） |
| `flash-rejected:<码>` | Flash 有结果前拒绝或精确层/物化后核对失败（准入原因、`flash-potion-policy-no-route`、`materialize-potion-policy`、`route-*`） |
| `flash-boundary` | Flash 内核边界（`FlashUnsupportedException`/`FlashCapacityException`） |
| `flash-mismatch` | 逐步回放核对不一致 |
| `turn-setup` | 回合准备根显式拒绝；完整支持留给后续批次 |


固定前缀续接的搜索口径：

1. 现有引擎从请求根按 `CombatBeamSolver.ApplyFixedPrefix` 的同一规则回放 `ContinuationSearchRequest.Prefix`，得到前缀末尾的 `SimulationSnapshot`（`CombatBeamSolver.ReplayFixedPrefixForFlash`）。前缀无法回放按 `UnsupportedRequest` 拒绝并回落。
2. 前缀末尾状态导入为 FlashBeam 的搜索根，规则表也从该状态编译；评估与下界的根冻结表仍从请求根建立，对应现有求解器的 `_run.Initial*` 来自请求根。
3. FlashBeam 的节点动作数从 `Prefix.Length` 起算，使 `Score`、`RouteKey`、HP 目标早停中的完整胜利判定与下界剪枝看到与现有求解器相同的动作数。incumbent 和药水最早回合仍按请求根回合换算，不因前缀推进。
4. 最终路线 = 前缀 + Flash 路线。Flash 路线每一步照常由现有引擎从搜索根回放并与 Flash 投影逐字节核对；物化时把前缀 + 路线整条作为 `fixedPrefixActions` 交给现有求解器，结果形状与现有后端的续接结果相同。

回放与物化求解器照常向 `policy.RequestWorkTotals` 记录工作量，FlashBeam 自身的节点与转移不计入。接管请求只在物化求解器中响应，FlashBeam 搜索期间不响应。

## 4. BeamSolveDispatch 与模式

`src/Search/BeamSolveDispatch.cs` 是唯一分发点。协调器 `RunSearchPass` 的 `SolveMember` 把参数打包为 `BeamSolveRequest` 交给 `BeamSolveDispatch.Solve`；`FrontierContinuationScheduler` 的 `Dispatch`/`DispatchOptional` 把 `ContinuationSearchRequest` 交给 `BeamSolveDispatch.SolveContinuation`。`PrimaryBeam`、`RefinementBeam`、`SmartPotionGradient` 以及协调器直接审计点 `PotionFreeAudit`、`RequiredPotionAudit`、`NarrowOpeningIncumbent`、`TurnBoundaryDiscovery` 查看 `FlashMode`；保留的语义边界（计划承诺、早期侦察、回合准备根）与实验路径仍直接使用 `CombatBeamSolver`，并在 On 模式写 `FLASH_LEGACY`。`SecondRankBand`、`AggressivePowerCommitment` 与分支上限只作用于现有搜索的保路泳道与分支截断，FlashBeam 没有这些结构，因此主搜索不读取它们（属于已记录的保留结构差异；新放开的药水请求按 3.1 的边界保留原搜索）；`RunForcedPotionOpeningRescue` 的三个剖面变体只有 `BaseScoreOnly` 由 Flash 执行，另外两个保留原搜索并记 `direct:turn-boundary-discovery:<开关>`。`NarrowOpeningIncumbent` 经分发点后使用 profileScale=1，保持原窄搜索宽度，不套用 Flash 常规 ×4。

模式由 `SearchPolicySnapshot.FlashMode` 冻结，主线程从设置 `SolverSettingsData.FlashSearchMode` 读取（设置 > 性能 > 搜索预算 >「Flash 快速搜索（实验）」）。离线宿主可用 `OFFLINE_HARNESS_FLASH_MODE=Off|Shadow|On` 覆盖。

| 模式 | 行为 |
| --- | --- |
| Off（默认） | 只用旧后端 |
| Shadow | 先跑旧后端，再在隔离策略下跑 Flash，返回旧结果并记录 `FLASH_SHADOW` |
| On | 先跑 Flash；已准入且逐步核对通过则采用并记录 `FLASH_ADOPTED`，否则记录 `FLASH_FALLBACK` 并改跑旧后端 |

Shadow 的隔离策略清空 `RequestWorkTotals`、`PrimaryIncumbents` 与 `Interaction`，求解器诊断写入空接收器，并且不报告进度，因此返回结果、工作量与日志都与 Off 相同，只多一行对照。续接请求的前缀回放与物化求解器也使用同一份隔离政策，不向共享账本记录工作量。请求的初始 `PrimaryIncumbent` 是只读输入，Shadow/On 都按同一请求值传给 FlashBeam；两边都不写共享 `PrimaryIncumbents` 表，因此 Flash 的收紧只影响本次 Flash 搜索。

日志行前缀为 `[CombatSolver/Flash]`。主搜索成员行写 `purpose=<DirectSearchPurpose>`；续接的对照、采用与回落行写 `purpose=<ContinuationPurpose> prefix=<前缀动作数>`。行内 `incumbent=<战略失血差>@<结束回合>` 是请求传入的初始 incumbent（`-` 表示没有）。Flash 段的 `status` 取 `verified`、`rejected`（带准入拒绝原因）、`mismatch`（带首个不一致步）或 `boundary`（Flash 搜索中途到达内核不建模的状态或容量上限）；`verified` 段记录耗时、胜负、HP、`hp_lost`、`end_turn`、各阶段耗时以及 FlashBeam 节点与转移数。Shadow 行另有旧后端的同名字段与 `same_route`。

根上的准入拒绝（`FlashImportException`）以结果形式返回；核对不一致（`FlashVerificationException`，包括路线中途旧引擎状态无法投影）丢弃整条 Flash 路线。Flash 搜索中途抛出的 `FlashUnsupportedException`（例如卡牌变量出现内核无法表示的小数偏移）与 `FlashCapacityException` 是派生内核的边界，与准入拒绝同样回落现有求解器并记 `status=boundary`。其他异常视为 Flash 缺陷，向上抛出并中止本次搜索，Shadow 也一样。

`SolvedRouteCache` 的键记录 `FlashRoutes = (FlashMode == On)`；Shadow 返回旧结果，与 Off 共用缓存。

设置项的玩家文案同时维护中文与英文（`ui-localization`）。
