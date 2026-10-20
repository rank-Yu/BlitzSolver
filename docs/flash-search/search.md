# Flash 搜索与接入

[返回 Flash 入口](README.md)

## 1. FlashBeamSolver

FlashBeam 在 `FlashBattle` 上做按回合分层的 Beam 搜索，产出候选路线，不直接产出 `SolverResult`。

- **容器复用**：每层是跨层复用的 `FlashBattle` 分块存储；子状态先复制父状态到当前块的第一个空槽（`Pool.Reserve`），在槽位上原地执行动作，按需提交（去重）、复制到回合结束层或作为终局丢弃；求解器与评估类型都跳过局部变量清零（`SkipLocalsInit`）。分数、父指针、动作等元数据也按类型分块。状态块固定 64 槽（64 × 12,600 B = 806,400 B）；其余平行数组按 `Unsafe.SizeOf<T>()` 选 ≥ 85,000 B 的 2 的幂块（例如 `RetentionFacts` 512 项、`int`/`double`/`ulong` 16K–32K 项），因此每块直接进 LOH，不在 Gen0/Gen1 反复晋升，也没有小于 LOH 阈值的状态数组。块只追加、不复制，增长不再重分配；`_order`/`_rankOrder`/`_rankPosition` 这类必须连续排序的 scratch 按容量上界一次分配。`Reserve` 在块边界或容量边界使用单个 scratch 槽，重复状态与终局状态不占新槽、不触发新块。保留层与回合前沿固定为 Beam 宽度。两个可增长池 `next`/`ended` 各自使用同一个按字节计的容量预算：状态上限为 `PoolStateBudgetBytes / sizeof(FlashBattle)`（`PoolStateBudgetBytes = 200 MiB`，12,600 B 状态约 16,644 个）向下取整到 64 槽块，即 16,640 个状态（209,664,000 B ≈ 199.95 MiB）；实际容量取它与原 `width × (MaxLegalActions + 1)` 的较小值。子状态层（`next`）每层清空，正常每层至多写入 `width × MaxLegalActions` 个唯一子状态；选择展开（每个请求至多 32 个分支）与首层承诺展开会把一个父动作放大成多个子状态，达到预算上限时装不下的唯一子状态逐个丢弃并计数（`FlashBeam.Pool`），下一层清空后恢复。回合结束层（`ended`）在一个回合内按动作深度累积终止子状态，REGENT-V-06 实测单回合可到 434 层、约 10.4 万个终止子状态；达到预算上限时不再整次回落，而是按回合边界 `CopyTopTo` 的同一保留规则压缩到 `width × 8` 后继续累积（见下文“容量边界”）。曾试过按进程缓存 64 MiB 状态块：块在 Medium 上确实命中（SILENT-C-13 每次求解约 160–300 块），但保留 64 MiB 后碎片 180 MB（分块无池 30–36 MB、基线 0.6 MB）、峰值 419 MB（分块无池 330–355 MB），故未保留，当前每次 `Solve` 新建池。
- **路线不进状态**：每个节点只记父节点下标与本步 `FlashAction`，找到候选后沿父指针倒推。
- **去重**：每层用开放寻址表按指纹分桶，命中后比较完整字节；同一状态只保留排序值最高的代表。旧引擎转置表的六维 Pareto 保留（药水数、药水战略成本、未来卖血、累计失血、动作数、分数）尚未移植。
- **内核派发**：`FlashHooks.CollectListeners` 在没有任何卡牌、苦难或附魔定义实现当前 Hook 时跳过整个卡区扫描；该判定来自不可变内容注册表的类型并集（`FlashRegistry.CardHooks`/`CardHooksExt`），逐项等价于对每张牌执行 `Has` 后必然不命中，因此不改变监听顺序或成员。费用、伤害与 HP 损失投影这几类 Hook 没有卡区监听者，直接受益。
- **层内保留**：先按 `CompareBeamRankOrder`（排序值、动作数、进攻进度）取前 Beam 宽度个状态；请求 profile 的软时间预算不超过 30 秒（10 秒自然根基准与短预算）时其中 `OffensiveShare`（25%）的席位留给敌人 HP 最低的状态，较长的协调器预算不保留这条 Flash 特有的进攻泳道。再照 `BeamRetentionPolicy.RankBest` 的必保置换，把现有搜索的必保代表取代排名末位的普通席位：每个药水分组与全局的防守、进攻、准备（`SetupLaneValue` 与 Scaling+Resource+Control 组合，资源道含星能与可达手牌价值）、牌堆整理（AttackDensity / LiveDeckClutter）、最压缩牌堆、力量与虚弱控制、战术铺垫（`ZeroCostPlayableCount`/`ReachableHandValue`）领先者；八条泳道（Scaling、Resource、Control、RevivalWindow、ReactiveDamage、EndTurnDeckCompression、LongTermResource、HpInvestment）领先者；持续铺垫特征分组与焦点目标分组的防守/准备领先者（分组键用 `PersistentSetupTraits`；现有搜索用 `PersistentSetupTraits | LatentSetupTraits` 的 `StrategicSetupTraits`，Flash 的 4× Beam 会把逐卡潜在标签扩成远多于现有的组，故只用持续项，K4 仍逐字段核对两者）；回合边界的 `FindBestTurnBoundaryHand` 领先者；以及按首次根动作分组的谱系代表（多个回合准备根时按现有 `BuildRootActionLineageSignature` 的首步签名（Kind、CardId、PotionId、目标与回放分配器开局的首张牌）跨根合并；单根时每个首步子节点各成一组，这是与现有搜索的已知差异：在 Flash 现有的保留结构下改用签名分组会改变保留代表，SILENT-C-13 从 46 失血变为死亡；`clamp(W/8, 4, 16)` 组，每组取排序、防守、进攻、准备、准备组合、战术、整理、压缩与目标压力代表）。必保成员只换席位，不增加 Beam 宽度；排序、分组与比较器读取的字段都由 K4 逐字段核对。`FindBestLane` 在现有搜索里按节点累计的 `SearchRouteTraits` 过滤，Flash 尚未建模该标签，泳道领先者直接在整组上比较（取到极值的状态通常正带该标签）。`CopyPower` 计划成员再叠加 `AdmitPlanCommitmentRepresentatives` 的计划代表席（见“计划承诺”）；力量承诺泳道再叠加 `AdmitPowerCommitmentSeats`（见“力量承诺与 `AggressivePowerCommitment`”）。旧引擎的选项路由配额 `max(8, 2W/5)` 与 option-leader 轮转、药水使用配额与药水谱系（`RankBest` 的 `PotionUseLineageKey` 组）、stand-pat 探测与 `FindBestFreshResourceStandPat`/`FindBestDamagingContinuation` 回放、Pareto、循环/跨回合/有序变动组合、回合边界整理代表（`FindBestCuratedTurnBoundaryHand`）、完整胜利分组代表（Flash 终局不进保留池）与偷窃资源领先者尚未移植。
- **次段成员**：`SolverSearchProfile.SecondRankBand` 的成员照 `BeamWidthPortfolio.MoveLeadingBandToTail`，在全局剪枝取普通席之前把分数序前 W 位挪到队尾，留下的普通席位是第 W+1 至 2W 位（候选不足 2W 时按原顺序回填）；必保代表、力量承诺席、`CopyPower` 计划席与药水配额不变。次段成员保留 Flash 的 ×4 宽度与节点补偿，但次段边界用请求 profile 的 W：把前 W 位挪到队尾后仍取 4W 个普通席位，于是 W+1..2W 的次段不会整体被推到 4W+1..8W，成员在保留 Flash 池宽的同时仍执行原搜索的次段语义。`BaseScoreOnly`、连续致命意图与上下文排序仍照原排序值组合。
- **首层承诺展开**：策略开启时，FlashBeam 的第一个回合层照现有引擎 `CombatBeamSolver.Phases.cs` 的 `searchedTurnLayers == 0` 分支，对 `HasPlayableFetchedPower` 等价状态（该动作的主选择效果为 `MoveToHand`、取回的能力牌已在手且付得起）先执行其全部合法后继再参与本层排序；中间选择状态不提交，后继照常走节点计数、容量、去重与终局提交。条件按 `CardChoiceSupport` 的主选择来源（8 张取回牌）与 Droplet of Precognition 药水逐项对照，策略关闭或其选择未取回可支付 Power 时不生效。现有引擎的回合层局部墙钟切片在 FlashBeam 中本来就不存在（Flash 每层固定按 Beam 宽度截断，只有全局节点/时间预算），因此策略开启时无需额外关闭切片。
- **计划承诺**：`ContinuationPurpose.PlanCommitment` 的固定前缀仍由现有引擎 `ApplyFixedPrefix` 回放，Flash 从前缀末状态导入搜索根。FlashBeam 照 `PlanCommitment.CountRealizedPayoffs` 在节点链上累计三类收益证据：`CardPlayed` 在 `turn >= Payoff.EarliestTurn` 时按收益卡计数；`FreePotionUsed` 按收益药水身份与用药前后战略成本不变计数；`RegisteredPowerBenefit` 读路线承诺元数据的 `RealizedEvidence`，按 128 位已打能力集合判断收益卡是否在承诺内，取整条链上的最大值。`InitialPlanPayoffCount` 用现有引擎从请求根回放固定前缀并调用同一个 `CountRealizedPayoffs` 得到；前缀节点没有承诺元数据，因此 `RegisteredPowerBenefit` 在两侧都只统计 Flash 搜索阶段新建的承诺。`CopyPower` 计划在保留时照 `AdmitPlanCommitmentRepresentatives` 取已兑现进度最高的三个进度带，每带保留 `BeamRankScore` 最高的代表，且只替换兑现进度严格更低的非必保席位（`MaxBy` 使用不含药水成本罚分的纯排序值）。物化后 `CardPlayed`/`FreePotionUsed` 按同一规则重数整条 `PlanAction` 链并要求与 Flash 的计数相等；`RegisteredPowerBenefit` 的旧承诺链只存在于现有搜索的展开节点，物化固定前缀不重建它，因此该证据由 Flash 的承诺元数据计数并在物化阶段保持同一数值。现有 `_planCommitment` 的另一处读取 `PlanHorizonPolicy.ShouldExtend` 只放宽现有搜索的跨回合无进展剪枝；FlashBeam 本来就没有这条剪枝（只按回合层/Beam/全局预算截断），计划视野在 Flash 上是等价宽松，不需要单独的视野字段。分发点不再因三类收益证据中的任何一类保留原搜索。
- **力量承诺与 `AggressivePowerCommitment`**：Bridge 把 `PowerCardValuationModels.Registry` 的逐卡承诺描述编译进 `FlashBeamOptions.PowerCommitments`（机制族、优先级、准入政策、总楼层），`FlashBeamSolver` 在每个路线节点旁保存紧凑承诺元数据（机制族/优先级/打出的能力集合/轮次转换/最后证据轮/投资/未兑现潜力/进展与兑现证据/开启动作数）。这是路线节点元数据，不进入 `FlashBattle`、指纹或状态键；相同战斗状态仍按原字节去重，并保留排序值更高代表的承诺历史，与旧 `SearchNode.PowerCommitment` 的去重口径一致。保留时照 `PowerCommitmentSeatPolicy.SeatQuota` 取席（aggressive 为一半 Beam，否则 `clamp(W/12, 2, 12)`），照 `PowerCommitmentRetention.RankRepresentatives` 先按（机制族、药水数、回合、能力集合）取每组代表，再按兑现证据/优先级/进展/净未兑现/开启动作数/轮次/生命/分数排序，只替换最后一批非必保、非代表席位并标为必保；`PowerCommitmentLifecycle.Advance` 的寿命按 profile 开关取 3（aggressive）或 2 回合转换。逐卡证据在 `FlashPowerEvidence` 中移植：`*PowerHasTriggerEvidence` 的全部卡牌分支、`*PowerTriggerProjectionFloor`、`*PowerOpeningProjectionPotential`（含 `PowerTurnFrontier` 的手牌子集 DP 与 `PowerGrowthFrontierPotential` 参数）以及 Silent 的 `SilentPowerProgressEvidence`/`SilentPowerRealizedEvidence`（Master Planner 的 Sly 价值差、After Image/Footwork/Well Laid Plans/Speedster/Tools of the Trade）。输入来自 `FlashBattle` 的字节状态、K4 逐字段核对的特征（`PlayerBlock`、`ProjectedPlayerHp`、`ReachableHandValue`、`Turn`、`AliveEnemyCount` 等）和根冻结的逐卡评分表（卡牌类型/动态变量/关键词/费用/Sly/攻击段数）；能量单位、抽牌数、剩余回合与最大能量由同一份态与评分表重算，不读取旧模拟器。上述读取的状态与特征在 K1/K2 指纹与 K4 中逐字节/逐字段核对，因此不会出现仅 Flash 可见的分支事实。
- **计划请求的早期侦察**：`EarlyTurnScoutDepth` 成为 FlashBeam 的回合层上限；每完成一层照 `SelectEarlyTurnFrontier` 过滤该回合的终止子状态、按策略键（开手动作、持续铺垫特征、药水数、持续增益档、可达手牌档、焦点目标）每组取分数前二，再按开手动作分组轮转报告最多 24 个候选。候选的排序值、准备特征、药水数与焦点目标都来自 K4 逐字段核对的 Flash 特征；`SearchRouteTraits` 尚未建模，策略键省去该维（只会合并分组，不会新增候选）。Flash 在物化与逐步核对全部成功后才把候选翻译成 `PlanAction`、读取旧引擎 `StateKey` 并调用观察者；失败回落时只由现有搜索的观察者发布。分发点只放开计划 pass 自己的深度 1、空前缀侦察；其他早期回合探索请求仍按边界回落。
- **评分**：由 `FlashScoreBridge.Create` 在根上编译逐内容数值表（卡牌估值与动态变量增量、计算伤害乘数、潜在布置值与类别、Power 的战略效果分类/参数、药水战略成本），冻结评估读取的 `Initial*` 与 policy 值，返回 `FlashScorer`。`FlashScorer.Features(in FlashBattle, actionCount)` 逐项移植旧引擎 `CombatBeamSolver.Snapshot`（`src/Search/CombatBeamSolver.StateEvaluation.cs`）：同样的项、同样的运算顺序与 `checked`/取整位置，特征直接从 `FlashBattle` 计算，不构造 `SimulationSnapshot`。节点分数与终局比较用 `Features(...).Score`；层内保留排序用 `FlashScorer.BeamRankScore`（旧引擎 `BeamRetentionPolicy.BeamRankScore`），连续威胁与上下文排序修正也在同一个排序值上。K4 在同一状态逐字段核对每个特征（成长/遗物字段与 `Turn` 含在内），`Score` 按 double 逐位比较；差分探针在开启这两个 profile 开关时另按 double 比较同一状态的 `BeamRankScore`（`rankChecked`/`rankFailed`）。
- **成长与遗物计数目标**：`SearchPolicySnapshot.EffectiveGrowthBudgets` 与 `RelicTargets` 冻结进 `FlashScoreRules`，不再在准入时拒绝。内核用五个打包计数器承载 `SimulatedCombatState._growthRewards` 的十个原版来源（`HandOfGreed`/`TheHunt`/`Feed` 的致命击杀、`Royalties`/`Alchemize`/`GeneticAlgorithm`/`TheScythe`/`Goopy` 的成功结算与牌组副本门控；ForbiddenGrimoire/MadScience 没有 Flash 定义，其累计值只在根导入历史时保留），根导入从同一字段读取历史；十个来源的 16 位计数两两打包进现有计数器数组，`FlashBattle` 布局与 `ExpectedSize` 不变。遗物目标按 `RelicCounterCatalog` 的周期与范围编译到对应 `FlashRelic.Counter`（Meat on the Bone 用当前 HP 阈值重算），`FlashScorer` 复刻 `EvaluateRelicCounters` 的 `SatisfiedMask`/`TargetMask`/`HpCredit`/`Distance`/`SatisfiedPriority` 与胜利后的 `HealingHpCredit`。评分项、`StrategicHpCredit`、`StrategyGoalHpCredit`、`StrategyGoalCount` 与终局排序键（战略失血之后依次比较成长信用与收益数）与现有快照逐项一致；有界成长目标与遗物目标也进入 HP 目标早停条件。第三方 `GrowthSourceMirrors` 额度/目标仍按 `UnsupportedRequest` 拒绝。`Profile.ContinuousThreatRanking` 与 `Profile.ContextualRanking` 也在此层移植：前者只在 EndTurn、存活、投影 HP ≤ 0 且仍有可支付手牌时把死亡罚分换成连续 HP 缺口；后者把同一组 K4 特征映射进 `ContextualRankingModel.Input`，逐状态得到与现有 `BeamRankScore` 相同的修正，终局/非存活上下文仍绕过。`DevelopmentStrategy` 仍拒绝（见下文）。

- **第三幕首领策略**：`SearchPolicySnapshot.Act3BossStrategy` 冻结进 `FlashScoreRules`，不再在准入时拒绝。评分表按该开关编译 `StrategicEffectModel.Requirements`，`Lethality`、`Pagestorm`、`DanseMacabre`、`Demesne`、`PrepTime` 的 `Act3BossInteractions` 估值分支逐项照搬；`FirstAttackDamage` 与 `CaptureAct3BossInteractionPotential` 的 `EtherealDrawTriggers`、`PagestormBonusDrawCapacity`、`HighEnergyPlays`、`DemesneEnergyGain`、`DemesneDrawGain` 从 `FlashBattle` 重算，策略关闭时不进入这些读取。六个新特征连同 `Act3BossInteractions` 标记进入 K4 逐字段核对（参考侧由 `SimulationSnapshot` 保存同一状态的上下文值）。
- **评估的保留专用值**：`ReachableHandValue` 与 `ZeroCostPlayableCount` 只被保留比较器读取，不进入 `Score`、`BeamRankScore` 或终局 `RouteKey`。`Score` 调用 `Features(..., includeRetentionOnly: false)` 跳过它们；状态真正进入保留池时 `Pool.Insert` 才计算一次，重复状态（字节相同的代表）沿用已存的值，终局状态完全不计算。K4 仍用默认的 `Features(...)`（`includeRetentionOnly: true`）逐字段核对这两项。可达手牌估值直接复用 `FlashKernel.CanPlayCard(..., out energyCost, out starCost)` 已经算出的费用，不再对同一张牌、同一牌堆重复执行能量/星能费用 Hook。
- **评分表**：每张已准入的卡牌按（内容 Id, 升级等级）有明确条目；每个 Power 有战略效果分类与参数。旧引擎判定不贡献的项显式记为 0（例如非玩家侧 Power、临时 Power、`StrategicEffectMirrors.Contributes` 为 false 的项）。未移植的逐内容乘数或请求设置在根上以 `FlashRejection.UnsupportedCard` / `UnsupportedRequest` 拒绝，不默认记 0。
- **选择**：遇到 `NeedsChoice` 时用 `FlashChoiceEnumerator` 按选项规格枚举（选择数量从小到大、同数量按下标字典序），每个请求至多探索 32 个分支（v1）；每个被执行的分支都是精确的，上限只限制探索面。旧引擎按估值排序挑分支（`MaxPile/HandChoiceBranchesPerAction`），v1 尚未移植这套排序。
- **药水**：Bridge 在根上取现有搜索的药水约束（`CapturePotionPolicyForFlash`：最早回合、最多使用次数），并把 `PotionStrategySnapshot`、有效政策与 `Disabled` 覆盖一起冻结进 `FlashPotionPolicy`；未设置时不使用药水。FlashBeam 对槽位中当前任何可搜索药水生成动作，按现有 `AllowsPotionUse` 的（槽位，药水身份）门 `PotionStrategySnapshot.AllowsExplicitUse` 判断，因此搜索中生成的药水与根药水使用同一规则，而不是只对仍是根上原药水的槽位生成。旧引擎的 `Score` 不含药水成本，因此非审计请求在**排序值**上额外减去已用药水的战略成本（`SolverWeights.Hp` 计价）；这是 Flash 特有的项，节点分数与终局比较仍用 `Features(...).Score`。审计请求（`AuditPotionPolicy` 且精确层 `MinimumPotionUses > 1`）的排序值直接使用现有 `BeamRankScore`：该层必须使用多瓶药，Flash 特有的成本罚项会把必需路线排到尚未用药的状态之后；成本仍由终局 `FinalPlanOrdering` 计入。单药审计层保留该罚项，避免改变既有的 1 药路线。固定前缀已经用掉的药水不在这段槽位差里：Bridge 把搜索根快照的累计 `PotionUseCount`/`PotionStrategicCost`/`ExplicitPotionStrategicCost` 与自动用药次数冻结进 `FlashScoreRules` 和 `FlashBoundFeatures`，评估与下界再加回这些前缀计数；`ExplicitPotionUseCount` 是新增的 K4 字段。`ExplicitPotionUseCount` 同时计入根槽位消耗与搜索中生成的用药（`GeneratedPotionUseCount`）；节点记录每次用药的实际药水身份，强制指令与 Ambergris 计数按身份匹配，生成的同身份药水也计入。导入搜索根时 `FlashStateImporter.ImportPotions` 按搜索根的累计 `PotionUseCount` 跳过前缀已消费的用药记录，只把搜索根之后的用药计入 `GeneratedPotionUseCount`/`GeneratedPotionStrategicCost`；否则前缀用药会同时出现在 `Initial*` 与 generated 计数里，精确用药层会看到多一次。`DropletOfPrecognition` 的抽牌堆选牌（现有引擎 `PotionChoiceSupport` 的 `MoveToHand`/`Draw` 精确选择）已在内核里请求选择并按 `AddToPile(Bottom)` 移动；Bridge 的 `TryBuildPotionChoice` 对所有 `PotionChoiceSupport.RequiresChoice` 药水从探测回放构造 `PlanAction.Choice`。
- **精确用药层（Smart 审计）**：`SmartPotionGradient` 与 `SmartOpeningPotionPosterior` 请求由 FlashBeam 搜索时，`FlashBeamOptions.AuditPotionPolicy` 打开现有 `FinalPlanOrdering.Select` 的移植：按 `minimumPotionUses`/`MaximumPotionUses` 过滤，`RequireAtLeastOne` 的 `PotionUsePolicy.IsEligible`、`PotionFreePolicyBaseline`、死亡保护、Ambergris 限制，并按同一次序（含 `PolicyHpDeficit`）选路线。精确层的显式用药计数包含搜索中生成的用药，槽位动作按当前药水身份判断，不再要求等于根药水。找不到满足策略的路线时返回 `PotionPolicySatisfied=false` 并回落现有求解器，不替它抛 `PotionPolicyUnsatisfiedException`。`_smartPotionEligibilityHpCeiling` 由现有构造函数的共享静态入口计算，Flash 在每次保路后按已由 K5 核对的 `FlashBoundValues` 逐状态剪枝（组件证书未知的分支保留）。药水前缀会先由现有引擎 `ApplyFixedPrefix` 回放，因此前缀可以以药水开头。其余药水策略（强制使用、最早药水回合等）仍在分发点按边界回落；计划承诺按下一节的计划门处理。
- **早停**：照抄现有搜索的两种早停：HP 目标（请求可在 HP 目标处停止、且不要求显式药水时，一次无死亡豁免、显式药水不超过额度、本场已失血加路线失血不超过可接受值的完整胜利，在下一个父节点之前结束搜索）与零伤害（胜利且未失血、未用显式药水、满血且最大生命未减少时，当前回合结束后停止）。
- **incumbent 下界剪枝**：移植现有主搜索成员的局部 HP 下界（`CombatBeamSolver.Retention.cs` 的 `CanUseLocalPrimaryHpBound`、`IsLocalPrimaryHpBucket`、`ApplyPrimaryIncumbentBound`/`ApplyPrimaryIncumbentBoundCore`、`ShouldPruneByPrimaryIncumbent`、`TryTightenPrimarySearchIncumbent`、`TightenPrimarySearchIncumbentAtTurnLayer`）。`FlashBoundFeatures` 在根上冻结 `FutureHealPotential`、`HasRisk`、剩余治疗额度（Component/KnownNative 证书）、战略首领减伤与初始 incumbent；求值只读 `FlashBattle` 和冻结表。终局完整胜利在提交时收紧 incumbent（与现有搜索的同一层 Prune 对齐）；现有搜索的回合结束收紧作用于同一批已提交的 `completed` 终局，Flash 在提交时已经收紧，因此不重复。每个动作层保留之后与回合边界保留之后各剪一次；被剪掉的席位不补。参数沿用现有语义：`allowTurnTieBound` 由 `costClosed` 门控，`pruneEqualHp` 只对非终局、无风险且药水成本闭合的节点生效，`costClosed` 读 `ExplicitPotionStrategicCost`。某个下界输入在 Flash 上无法精确给出时该根整体禁用剪枝，汇总行写 `bound=disabled(<原因>)`，不使用近似值。共享的 `PrimaryIncumbents` 表不接入，避免对照模式的隔离泄漏；初始 `PrimaryIncumbent` 由请求传入并按 `FlashBattle.PlayerTurn` 相对根的偏移换算。
- **容量边界（分支级）**：搜索展开中的容量溢出按“一个子状态装不下”处理，只丢弃该子状态并继续：内核执行动作时的状态级写入（卡牌、Power、已知敌人、遗物、充能球、`DetachedCards`、小数卡牌变量、费用修饰、选择选项、动作选择链、`FlashExec` 的暂存队列等）、动作执行后的评分，以及提交到非累积池，全部在 `ExpandAction` 的同一个边界内捕获 `FlashCapacityException`；一次选择请求的某个选项分支溢出只丢弃该分支，同请求的其余选项照常展开。诊断按容量区块分别计数（`FlashBeamResult.CapacityDroppedChildren`，日志 `flash_capacity_dropped=Cards:1,Powers:2`，合计仍在 `flash_capacity_pruned`）。这与节点预算同类，是搜索边界而不是模拟错误；采用的路线仍逐步核对。**根状态本身**装不下仍是导入拒绝：`FlashStateImporter`/根评分在展开循环之前抛出的容量溢出与 `FlashUnsupportedException` 一样让整次 Flash 尝试回落。
- **回合结束层压缩**：`ended` 只在累计子状态达到容量上限（`min(width × (MaxLegalActions + 1), 向下取整到 64 槽的 200 MiB / sizeof(FlashBattle) = 16,640)`）时触发压缩：把当前全部 `ended` 子状态按与回合边界 `CopyTopTo` **完全相同的选择函数**（按 Beam rank 的前列席位、进攻席位，以及全部必保代表——每个药水分组的各泳道/整理/回合边界手牌领先者、持续铺垫特征与焦点目标分组、根动作谱系、偷窃根的资源保全代表等）重选到 `clamp(width × 8, width, 容量)` 个席位，原地前移并重建去重表后继续累积。压缩目标取 8 倍 Beam 宽度的依据：回合边界最终只消费 `width` 个席位，8 倍宽度保留了 8 组完整的排序席位并容纳全部必保代表（宽度 4–512 时目标为 32–4096 个状态，约 0.4–51 MB）；它与容量上限之间留有足够余量（宽度 ≥ 92 时上限是 16,640 个状态；更小宽度取原 `width × (MaxLegalActions + 1)`），正常回合不会触发压缩。触发条件严格是达到上限，因此 `ended` 未达到上限的根不会进入压缩，路线、转移数、展开数与去重数逐位不变；只有原本会整次回落的根才观察到压缩后的保留集合，且采用路线仍逐步由现有引擎回放核对。压缩次数与被压缩掉的子状态数分别记入 `FlashBeamResult.EndedCompactions`/`EndedCompressedStates`，日志为 `flash_ended_compactions`/`flash_ended_dropped`；压缩不释放已分配的块，后续累积复用同一批状态块。
- **预算**：取同一 `SolverSearchProfile`：Beam 宽度与全局节点上限乘以 `FlashRouteMaterializer.ProfileWidthScale`（4），软时间不变。FlashBeam 只有排序泳道与进攻泳道，需要更宽的 Beam 才能达到现有搜索的路线质量（[搜索演进测试记录](../archive/flash-search/testing-search-2026-10-19.md) 2.3）。旧引擎按回合层分配节点预算的规则尚未移植。
- **产出**：按旧引擎终局比较器（`FinalPlanOrdering.Select` 的排序键）选出一条路线：完整胜利、存活、死亡保护次数、战略失血（`ActEndingBossPolicy.StrategicHpDeficit`，含成长/遗物信用与战后遗物回复）、成长信用、成长收益数、结束回合、生命资源消耗、长期资源、愤怒复制数、可选药水数、敌人 HP、分数、动作数。没有胜利路线时，预算耗尽时的回合起点状态与终局状态一起参与比较。偷窃、卖血与边界原因这几个键尚未移植。

## 2. 路线物化

`FlashRouteMaterializer` 把候选路线交给旧引擎：

1. 从根分叉旧模拟器，逐步回放 Flash 动作。每一步按手牌位置取牌，按旧引擎的规则生成 `PlanAction`（`CardStateKey`、occurrence、`TargetCombatId`、选择令牌），并要求旧引擎这一步的投影与 Flash 逐字节一致。
2. 把整条已核对路线作为 `fixedPrefixActions` 交给现有 `CombatBeamSolver`，用极小预算求解。现有求解器自己回放并校验前缀（`ApplyFixedPrefix`），按既有终局排序与物化逻辑生成 `SolverResult`。协调器的续用补搜已经使用同一入口，所以不需要提取 `MaterializeSelectedRoute`。

固定前缀续接在导入前先用同一 `ApplyFixedPrefix` 规则回放请求前缀（`CombatBeamSolver.ReplayFixedPrefixForFlash`），把前缀末尾状态作为 Flash 搜索根；步骤 1 从该状态开始核对，步骤 2 的 `fixedPrefixActions` 是“请求前缀 + Flash 路线”，仍由现有求解器从请求根完整回放与物化。

**回合准备根（`IncludeTurnSetup`）**：玩家回合开始的原生选择页在根捕获时还没有作答，捕获的根是 `Player/Start`。Bridge 不把该 Start 状态导入内核，而是先调用现有求解器的 `CaptureTurnSetupRootsForFlash`（内部就是 `BuildTurnSetupRoots`），按与现有搜索**完全相同的选择预算**把每个准备选择分支回放到稳定 `Play` 状态，再逐个导入 `FlashBattle`。`FlashBeamSolver.Solve` 接收这组根（现有搜索的 frontier 同样从多个准备根出发），返回选中路线的 `BestRootIndex`；路线本身从 `Play` 状态开始，准备选择不占动作数。物化时把选中的 `PlanCardChoice` 向量作为 `rootTurnSetupChoices` 交给现有求解器，它在同一 `BuildTurnSetupRoots` 结果里只保留该向量并回放 `fixedPrefixActions`，结果的 `TurnSetupChoices`/`TurnSetupPlayState` 与现有语义同形；物化后逐项核对选择向量，不一致按 `FlashVerificationException` 整条丢弃。

实现在 `src/Search/FlashBeam/Bridge/FlashRouteMaterializer.cs` 与 `src/Search/CombatBeamSolver.TurnSetup.cs`。

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
| `Policy.IncludeTurnSetup` | 支持：根必须是 `Player/Start`；Bridge 按现有 `BuildTurnSetupRoots` 的选择预算枚举准备根，FlashBeam 从全部准备根搜索，物化时固定选中的选择向量 |

`CombatBeamSolver` 的其余构造参数（`maximumPotionUses`、`minimumPotionUses`、`earliestPotionTurn`、`fixedPrefixActions`、`potionFreePolicyBaseline`、`earlyTurnScout*`、`planCommitment`）在主搜索成员中不使用。Smart 梯度审计通过 `BeamSolveRequest.PotionFreePolicyBaseline`、`MaximumPotionUses`、`MinimumPotionUses` 携带精确层参数；区间上下界、基线、最早用药回合、强制指令与 `RequireAtLeastOne` 均已移植（见 3.1）。固定前缀续接通过第二个分发入口 `BeamSolveDispatch.SolveContinuation` 走同一 `FlashMode`；药水相关的续接请求不再被“干净请求”门挡住，只有下列语义边界仍直接调用现有求解器：

- `Commitment != null`：只接管下面的 `PlanCommitment` 成员，其余计划请求保留原搜索（`can-take-over:commitment`）。
  - `PlanCommitment` 成员：没有早期侦察、`PotionFreePolicyBaseline == null`、`EarliestPotionTurn == null`、药水上下界非负且不矛盾，且收益证据是 `CardPlayed`。药水政策无药或带上下界/`RequireAtLeastOne` 都可以；带药水前缀的 `CopyPower`/`CopyCard` 计划与 `PowerCycle` 共用 PA 的审计终局过滤，物化后再核对计划收益计数与药水约束。
- 早期侦察：只接管计划阶段自己的深度 1 侦察，其他早期侦察保留原搜索（`can-take-over:early-turn-scout`）。
  - 计划侦察：`Purpose == PlanCommitment`、`Commitment == null`、空前缀、`EarlyTurnScoutDepth == 1`、观察者非空、药水为无药。
- 根阶段不是受支持的两形：普通 `Player/Play`，或 `IncludeTurnSetup` 的 `Player/Start`。其余 side/phase 保留显式拒绝并写 `FLASH_LEGACY ... reason=root-phase:<side>:<phase>`（Flash 内核只表示稳定出牌状态，不能把敌方回合或其它准备阶段当作出牌根搜索）。
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

`SecondRankBand`、`AggressivePowerCommitment`、`BaseScoreOnly` 都由 FlashBeam 执行：次段在全局剪枝取次段席位，力量承诺泳道按上面的席位/寿命移植；分发点不再因这些 profile 开关保留原求解器，也不会静默忽略它们。此前已经是“干净请求”的续接保持原行为。

`SmartOpeningPotionPosterior` 续接由独立的 `PotionAuditBoundary` 判断：只保留“非 Smart 政策”和“根无可搜索药水”两个语义边界（前者表示该审计本身不适用，后者表示无药可审计）。计划承诺与早期回合侦察仍按上面的语义边界回落。

SP2 复核了 DX4 在第三幕五个牌组上看到的 16 个 `flash-potion-policy-no-route` 梯度回落点：修正后只剩 `NECROBINDER-07` 的 2 个与 `SILENT-28` 的 2 个，都是现有求解器在同一请求上 `SMART_POTION_GRADIENT ... route_missing=true` 的双方无路线；其余 12 个回落点由 Flash 找到路线。`NECROBINDER-22` 的最终 61 胜来自同一生成药水搜索空间参与后的 `TurnBoundaryRescue`，`REGENT-23` 的最终 63 胜来自梯度第 4 层（修正前该根 66 胜/12 回合，修正后 63 胜/9 回合）。

### 3.2 `FLASH_LEGACY` 可观测性

开启模式（`FlashMode == On`）下任何一次真正执行的原搜索都写一行稳定日志：

```
[CombatSolver/Flash] FLASH_LEGACY purpose=<用途> reason=<原因> ms=<整数毫秒>
```

关闭（Off）与对照（Shadow）模式不写。分发点覆盖 `SolveExisting`/`SolveExistingContinuation`；协调器里仍由原搜索直接执行的实验/救援位置（`NoveltyExploration`、`AdaptiveNoveltyRefinement`、`EarlyTurnScout`、`TurnBoundaryDiscovery` 的三个剖面变体中 Flash 未建模的两个）也写同一行。原因码使用 FI 盘点第 2 节的稳定前缀：

| 原因 | 含义 |
| --- | --- |
| `potion-audit-boundary:<码>` | Smart 梯度/后验的语义边界（`potion-policy`、`no-searchable-potion` 等） |
| `can-take-over:<码>` | 续接门保留的语义边界（`commitment`、`early-turn-scout`、`potion-bounds`） |
| `direct:<用途>` | 协调器直接执行的原搜索（`direct:novelty-exploration`、`direct:early-turn-scout`、`direct:adaptive-novelty-refinement` 等） |
| `flash-rejected:<码>` | Flash 有结果前拒绝或精确层/物化后核对失败（准入原因、`flash-potion-policy-no-route`、`materialize-potion-policy`、`route-*`） |
| `flash-boundary` | Flash 内核边界（`FlashUnsupportedException`/`FlashCapacityException`） |
| `flash-mismatch` | 逐步回放核对不一致 |
| `root-phase:<side>:<phase>` | 根不在受支持的两形（`Player/Play` 或 `IncludeTurnSetup` 的 `Player/Start`）；Flash 不搜索其它阶段 |


固定前缀续接的搜索口径：

1. 现有引擎从请求根按 `CombatBeamSolver.ApplyFixedPrefix` 的同一规则回放 `ContinuationSearchRequest.Prefix`，得到前缀末尾的 `SimulationSnapshot`（`CombatBeamSolver.ReplayFixedPrefixForFlash`）。前缀无法回放按 `UnsupportedRequest` 拒绝并回落。
2. 前缀末尾状态导入为 FlashBeam 的搜索根，规则表也从该状态编译；评估与下界的根冻结表仍从请求根建立，对应现有求解器的 `_run.Initial*` 来自请求根。
3. FlashBeam 的节点动作数从 `Prefix.Length` 起算，使 `Score`、`RouteKey`、HP 目标早停中的完整胜利判定与下界剪枝看到与现有求解器相同的动作数。incumbent 和药水最早回合仍按请求根回合换算，不因前缀推进。
4. 最终路线 = 前缀 + Flash 路线。Flash 路线每一步照常由现有引擎从搜索根回放并与 Flash 投影逐字节核对；物化时把前缀 + 路线整条作为 `fixedPrefixActions` 交给现有求解器，结果形状与现有后端的续接结果相同。

回放与物化求解器照常向 `policy.RequestWorkTotals` 记录工作量，FlashBeam 自身的节点与转移不计入。接管请求只在物化求解器中响应，FlashBeam 搜索期间不响应。

### 3.3 Flash 对原搜索类的剩余依赖

Flash 路径自身的探针已迁到 `src/Search/Probes/`，不再经过 `CombatBeamSolver`。其余对原搜索类的直接依赖按批次归属如下（下一批处理 Bridge 与物化）：

| 位置 | 依赖 | 归属 |
| --- | --- | --- |
| `FlashBeam/Bridge/FlashRouteMaterializer.cs` | 构造原求解器做前缀/整条路线物化与校验 | SR 合入后的下一批 |
| `FlashBeam/Bridge/FlashChoiceTranslator.cs` | 用原求解器回放选择分支 | 下一批 |
| `FlashBeam/Bridge/FlashScoreBridge.cs` | `CombatBeamSolver.CapturePotionUsageForFlash` 静态根捕获 | 下一批 |
| `FlashBeam/FlashBeamSolver.cs` | `TryTightenPrimarySearchIncumbent` / `ShouldPruneByPrimaryIncumbent` 静态下界入口 | 下一批（或随原搜索删除） |
| `BeamSolveDispatch.cs` | 关闭/回落模式构造 `CombatBeamSolver` 求解 | 保留原搜索期间 |
| `CombatSearchCoordinator.NoveltyPortfolio.cs` / `.EarlyTurnExploration.cs` / `.PostSearch.cs` | 本批范围外的直接求解成员 | 保留到对应批次 |

`OpeningProbe`、`SearchEvaluator`、三个开局续接来源与协调器探针调用点不在此表；结构门禁禁止 `src/Search/Probes/**` 引用 `CombatBeamSolver`。

## 4. BeamSolveDispatch 与模式

`src/Search/BeamSolveDispatch.cs` 是唯一分发点。协调器 `RunSearchPass` 的 `SolveMember` 把参数打包为 `BeamSolveRequest` 交给 `BeamSolveDispatch.Solve`；`FrontierContinuationScheduler` 的 `Dispatch`/`DispatchOptional` 把 `ContinuationSearchRequest` 交给 `BeamSolveDispatch.SolveContinuation`。`PrimaryBeam`、`RefinementBeam`、`SmartPotionGradient` 以及协调器直接审计点 `PotionFreeAudit`、`RequiredPotionAudit`、`NarrowOpeningIncumbent`、`TurnBoundaryDiscovery` 查看 `FlashMode`；保留的语义边界（计划阶段以外的早期侦察、非法药水上下界、不支持的根阶段）与实验路径仍直接使用 `CombatBeamSolver`，并在 On 模式写 `FLASH_LEGACY`。回合准备根（`Player/Start` + `IncludeTurnSetup`）由 Flash 接管。`SecondRankBand`、`AggressivePowerCommitment` 与 `BaseScoreOnly` 已由 FlashBeam 执行，分发点不再因它们保留原搜索；带药水上下界与 `RequireAtLeastOne` 的计划成员同样交给 Flash。`RunForcedPotionOpeningRescue` 的三个剖面变体都经 `BeamSolveDispatch.Solve`。`NarrowOpeningIncumbent` 经分发点后使用 profileScale=1，保持原窄搜索宽度，不套用 Flash 常规 ×4；次段成员保留 ×4 宽度，只用 profile W 作次段边界；多个回合准备根作为同一首层的并列根送入 `Solve(IReadOnlyList<FlashBattle>)`。计划成员与计划阶段的深度 1 侦察（PC）同样查看 `FlashMode`。

模式由 `SearchPolicySnapshot.FlashMode` 冻结，主线程从设置 `SolverSettingsData.FlashSearchMode` 读取（设置 > 性能 > 搜索预算 >「Flash 快速搜索（实验）」）。离线宿主可用 `OFFLINE_HARNESS_FLASH_MODE=Off|Shadow|On` 覆盖。

| 模式 | 行为 |
| --- | --- |
| Off（默认） | 只用旧后端 |
| Shadow | 先跑旧后端，再在隔离策略下跑 Flash，返回旧结果并记录 `FLASH_SHADOW` |
| On | 先跑 Flash；已准入且逐步核对通过则采用并记录 `FLASH_ADOPTED`，否则记录 `FLASH_FALLBACK` 并改跑旧后端 |

Shadow 的隔离策略清空 `RequestWorkTotals`、`PrimaryIncumbents` 与 `Interaction`，求解器诊断写入空接收器，并且不报告进度，因此返回结果、工作量与日志都与 Off 相同，只多一行对照。续接请求的前缀回放与物化求解器也使用同一份隔离政策，不向共享账本记录工作量。请求的初始 `PrimaryIncumbent` 是只读输入，Shadow/On 都按同一请求值传给 FlashBeam；两边都不写共享 `PrimaryIncumbents` 表，因此 Flash 的收紧只影响本次 Flash 搜索。

日志行前缀为 `[CombatSolver/Flash]`。主搜索成员行写 `purpose=<DirectSearchPurpose>`；续接的对照、采用与回落行写 `purpose=<ContinuationPurpose> prefix=<前缀动作数>`。行内 `incumbent=<战略失血差>@<结束回合>` 是请求传入的初始 incumbent（`-` 表示没有）。Flash 段的 `status` 取 `verified`、`rejected`（带准入拒绝原因）、`mismatch`（带首个不一致步）或 `boundary`（Flash 搜索中途到达内核不建模的状态，或根级容量装不下）；`verified` 段记录耗时、胜负、HP、`hp_lost`、`end_turn`、各阶段耗时以及 FlashBeam 节点与转移数，并记录 `flash_capacity_dropped`（按容量区块的子状态丢弃）、`flash_ended_compactions`/`flash_ended_dropped`（回合结束层压缩次数与被压缩掉的子状态）。Shadow 行另有旧后端的同名字段与 `same_route`。

根上的准入拒绝（`FlashImportException`）以结果形式返回；核对不一致（`FlashVerificationException`，包括路线中途旧引擎状态无法投影）丢弃整条 Flash 路线。Flash 搜索中途抛出的 `FlashUnsupportedException`（例如卡牌变量出现内核无法表示的小数偏移）是派生内核的边界，与准入拒绝同样回落现有求解器并记 `status=boundary`。子状态级的 `FlashCapacityException` 已在 `FlashBeamSolver` 内按分支丢弃，不产生 `boundary`；只有在展开循环之前（根导入或根评分）抛出的容量溢出仍让整次尝试回落。其他异常视为 Flash 缺陷，向上抛出并中止本次搜索，Shadow 也一样。

`SolvedRouteCache` 的键记录 `FlashRoutes = (FlashMode == On)`；Shadow 返回旧结果，与 Off 共用缓存。

设置项的玩家文案同时维护中文与英文（`ui-localization`）。
