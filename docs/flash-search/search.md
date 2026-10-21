# Flash 搜索与接入

[返回 Flash 入口](README.md)

## 1. FlashBeamSolver

FlashBeam 在 `FlashBattle` 上做按回合分层的 Beam 搜索，产出候选路线，不直接产出 `SolverResult`。

- **容器复用**：每层是跨层复用的 `FlashBattle` 分块存储；子状态先复制父状态到当前块的第一个空槽（`Pool.Reserve`），在槽位上原地执行动作，按需提交（去重）、复制到回合结束层或作为终局丢弃；求解器与评估类型都跳过局部变量清零（`SkipLocalsInit`）。分数、父指针、动作等元数据也按类型分块。状态块固定 64 槽（64 × 12,696 B = 812,544 B）；其余平行数组按 `Unsafe.SizeOf<T>()` 选 ≥ 85,000 B 的 2 的幂块（例如 `RetentionFacts` 512 项、`int`/`double`/`ulong` 16K–32K 项），因此每块直接进 LOH，不在 Gen0/Gen1 反复晋升，也没有小于 LOH 阈值的状态数组。块只追加、不复制，增长不再重分配；`_order`/`_rankOrder`/`_rankPosition` 这类必须连续排序的 scratch 按容量上界一次分配。`Reserve` 在块边界或容量边界使用单个 scratch 槽，重复状态与终局状态不占新槽、不触发新块。保留层与回合前沿固定为 Beam 宽度。两个可增长池 `next`/`ended` 各自使用同一个按字节计的容量预算：状态上限为 `PoolStateBudgetBytes / sizeof(FlashBattle)`（`PoolStateBudgetBytes = 200 MiB`，12,696 B 状态约 16,518 个）向下取整到 64 槽块，即 16,512 个状态（209,636,352 B ≈ 199.93 MiB）；实际容量取它与原 `width × (MaxLegalActions + 1)` 的较小值。子状态层（`next`）每层清空，正常每层至多写入 `width × MaxLegalActions` 个唯一子状态；选择展开（每个请求至多 32 个分支）与首层承诺展开会把一个父动作放大成多个子状态，达到预算上限时装不下的唯一子状态逐个丢弃并计数（`FlashBeam.Pool`），下一层清空后恢复。回合结束层（`ended`）在一个回合内按动作深度累积终止子状态；达到预算上限时不再让整次搜索失败，而是按回合边界 `CopyTopTo` 的同一保留规则压缩到 `width × 8` 后继续累积（见下文“容量边界”）。当前每次 `Solve` 新建池。
- **路线不进状态**：每个节点只记父节点下标与本步 `FlashAction`，找到候选后沿父指针倒推。
- **去重**：每层用开放寻址表按指纹分桶，命中后比较完整字节。六维 Pareto 标签（药水数、药水战略成本、未来卖血、累计失血、动作数、分数）已按现有 `TranspositionFrontier` 移植：被任一已接受标签支配的候选拒绝，非支配标签保留（不设标签数上限），代表按现有 `IsBetterSearchNode`（分数降序、动作数升序）选择，而不是 Flash 的排序值。`FutureSoldHp` 在 Flash 中没有路线级值，当前恒 0，是这一维的已知残差。
- **内核派发**：`FlashHooks.CollectListeners` 在没有任何卡牌、苦难或附魔定义实现当前 Hook 时跳过整个卡区扫描；该判定来自不可变内容注册表的类型并集（`FlashRegistry.CardHooks`/`CardHooksExt`），逐项等价于对每张牌执行 `Has` 后必然不命中，因此不改变监听顺序或成员。费用、伤害与 HP 损失投影这几类 Hook 没有卡区监听者，直接受益。
- **层内保留**：先按 `CompareBeamRankOrder`（排序值、动作数、进攻进度）取前 Beam 宽度个状态；请求 profile 的软时间预算不超过 30 秒（10 秒自然根基准与短预算）时其中 `OffensiveShare`（25%）的席位留给敌人 HP 最低的状态，较长的协调器预算不保留这条 Flash 特有的进攻泳道。再照 `BeamRetentionPolicy.RankBest` 的必保置换，把现有搜索的必保代表取代排名末位的普通席位：每个药水分组与全局的防守、进攻、准备（`SetupLaneValue` 与 Scaling+Resource+Control 组合，资源道含星能与可达手牌价值）、牌堆整理（AttackDensity / LiveDeckClutter）、最压缩牌堆、力量与虚弱控制、战术铺垫（`ZeroCostPlayableCount`/`ReachableHandValue`）领先者；八条泳道（Scaling、Resource、Control、RevivalWindow、ReactiveDamage、EndTurnDeckCompression、LongTermResource、HpInvestment）领先者；持续铺垫特征分组与焦点目标分组的防守/准备领先者；回合边界的 `FindBestTurnBoundaryHand` 领先者；以及按首次根动作分组的谱系代表（多个回合准备根时按现有 `BuildRootActionLineageSignature` 的首步签名（Kind、CardId、PotionId、目标与回放分配器开局的首张牌）跨根合并；单根时每个首步子节点各成一组，这是与现有搜索的已知差异：在 Flash 现有的保留结构下改用签名分组会改变保留代表，SILENT-C-13 从 46 失血变为死亡；`clamp(W/8, 4, 16)` 组，每组取排序、防守、进攻、准备、准备组合、战术、整理、压缩与目标压力代表）。必保成员只换席位，不增加 Beam 宽度；排序、分组与比较器读取的字段都由 K4 逐字段核对。下列保留结构已移植到 FlashBeam，全部按 profile 的 W（`SecondRankBandWidth`，不是 Flash 的 ×4 池宽）计算席位：

  - **路线标签**：`SearchRouteTraits` 是路线节点元数据，按动作在父节点上累计，不进入 `FlashBattle`、指纹或状态键。分类照 `ClassifyCardTraits`/`ClassifyPotionTraits`/`ClassifyRoundTransitionTraits` 的逐项特征比较；`FindBestLane` 只在该标签命中的节点中取领先者（`FindBestLane` 的八条泳道与 `bestUtilityDefensive` 都恢复原过滤），profile W ≥ 18 才收集八条泳道。现有搜索的 `pure` 来自模拟器历史条目类型，Flash 路线没有历史流，生产以“非 Attack 牌视为非 pure”近似；K4 探针把原搜索按历史算出的 `pure` 同时喂给两侧，逐转移核对完整 `SearchRouteTraits` 增量（`traitChecked`/`traitFailed`），并用 `pocketwatchChecked`/`declinedChecked` 对照原快照的 Pocketwatch 与 PaelsEye 输入，生产近似本身是已知残差。

  - **持续铺垫与焦点目标分组**：分组上限按每组药水数分别取 `max(4, W/6)` 与 `clamp(W/10, 2, 4)`，组序按原比较器（组内最高 BeamRank / 最高焦点压力，稳定回退首次出现）。分组键仍用 `PersistentSetupTraits`：Flash 的 ×4 宽度会把逐卡 `LatentSetupTraits` 扩成远多于现有搜索的组，改用 `StrategicSetupTraits` 会改变保留代表（已知差异），故生产保留持续项；K4 仍逐字段核对 `PersistentSetupTraits` 与 `StrategicSetupTraits`。

  - **药水使用配额与谱系**：`FeasiblePotionUseQuotas` 按 W 取 `used = W < 4 ? 1 : max(2, W/3)`、`unused = W - used`；`ReservePotionQuotaLeaders` 先按分数序保护两组领先者，`EnforcePotionUseQuota` 从最后一批未被 required/领先者保护的异组席位替换。谱系按药水使用次数分组，每组取 `clamp(W/6, 2, 6)` 个不同的显式药水 id 多重集（排序后 ordinal 键），再用 `FindBestPotionLineage`（胜负、投影 HP、敌人 HP、分数）取代表。配额只在有效政策不是 `Disabled` 时执行。

  - **选项路由配额与 option-leader 轮转**：节点保存最近一次选择的路由签名（回合、SourceId、效果、来源牌堆、选项牌 id/升级/卡值哈希/选项序号/张数、ContextId、三类分布指纹）。SourceId 用“卡/药水种类 + 内容 id”的前缀值；Flash 选择请求没有旧引擎的上下文字符串，`ContextId` 恒 0（Seeker Strike 的采样上下文会合并，残差）。敌人 combat/control 分布指纹按旧公式逐值重建并 K4 逐字段核对；`UnorderedPileKey` 用 Flash 卡字节哈希保持同一等价类，数值不可与旧指纹比较。family 取目标压力、整理与准备领先者；option 组按 actions-since-choice 取代表：0 取父排名最高的上下文里排序值最高者，1 取 `FindBestSetup`，更久取 `FindBestTargetPressure` 的易伤目标变体。不同候选按 `RoutingChoiceLimit=96` 去重，前 `max(8, 2W/5)` 个成为 required；上下文 Pareto、当前回合 direct extremes 与 Art of War 的 8 回合窗口已移植。

  - **stand-pat 探测**：每个药水组照 `FindBestStandPat` 对 Scaling/Resource/Control 三条泳道各取最多 8 个探测点（投影 HP、LaneValue、排序值），Control 另有延迟击杀探测点；`FindBestFreshResourceStandPat` 取前 64 个新鲜资源点，`FindBestDamagingContinuation` 取破盾后可继续攻击的点。探测在 Flash 内核里对节点状态执行一次 EndTurn，读结束态的胜负、延迟伤害、投影 HP 与资源公式；遇到回合开始选择时按 `CardChoiceSupport.BuildAutomaticPolicyChoice` 的规则自动作答（手牌升序、其他来源降序、同分按卡 id/升级/卡字节哈希），同一 EndTurn 动作追加选择位后从父状态重放，无法作答时该探测不可用。回合开始的选择作答使用 `taken` 位图记录已选下标；该 `stackalloc bool` 缓冲区必须先清零，否则残留的 `true` 字节会跳过选项，重放时与同一 `EntropyPower` 请求的 1–1 约束不一致；生产代码在每次作答前 `taken.Clear()`。探测点与比较器读取的特征全部由 K4 逐字段核对（新增 `PlayableAttackCount`）。
  - **Pareto**：`MultiObjectiveDominates` 的全部维度已在 `RetentionFacts` 中，按敌人 combat 分布、敌人 control 分布与无序牌堆三类指纹组成等价 cohort（与旧 `MultiObjectiveDominates` 的 cohort 门一致）；每个 cohort 按原比较器保留最多 3 个非支配状态（排序值降序、动作数升序）。路由上下文 Pareto 已按旧 `BuildContextPareto` 移植，候选包含 safe discard continuation、短/长 actions-since-choice 领先者、context 前 16 个 rank 成员与分数/进攻/防守/有序牌堆领先者。
  - **有序牌堆与回合边界整理**：`ProjectedShuffleOrderValue/Key` 按旧 `BuildProjectedShuffleOrder` 的稳定洗牌投影重建并进入 K4；`FindBestCuratedTurnBoundaryHand`、谱系整理/战术/目标/整理/洗牌序代表与 pocketwatch 节奏桶/族、tactical key、每 cohort 的有序牌堆变体已移植。Flash 不扩大 Beam 宽度（旧 `effectiveLimit` 的加宽未移植），代表只替换普通席位。药水次数按实际 `PotionCount` 分组（含超过 `FlashLimits.PotionSlots` 的生成药水使用）。
  - 仍未移植：`BeamRetentionPolicy.OrderedMutation*` 的碰撞谱系/租约/观察桥/原子双序准入（Flash 不生成该 lineage 元数据）、结束回合压缩谱系代表（`EndTurnDeckCompressionLineage*`）、完整胜利分组代表（Flash 终局不进保留池，保留池恒为空）、路由签名的 `ContextId`（恒 0）与选择完成后的牌堆/敌人分布键（Flash 取选择前的父状态）。整次求解范围的两张转置表试过未采用：120 根质量不变，打满 100 万条上限时单次求解约 100 MB 托管内存。偷窃资源领先者与 `FindBestTurnBoundaryHand` 已移植；`FindBestLane` 的路线标签过滤已恢复。
- **次段成员**：`SolverSearchProfile.SecondRankBand` 的成员照 `BeamWidthPortfolio.MoveLeadingBandToTail`，在全局剪枝取普通席之前把分数序前 W 位挪到队尾，留下的普通席位是第 W+1 至 2W 位（候选不足 2W 时按原顺序回填）；必保代表、力量承诺席、`CopyPower` 计划席与药水配额不变。次段成员保留 Flash 的 ×4 宽度与节点补偿，但次段边界用请求 profile 的 W：把前 W 位挪到队尾后仍取 4W 个普通席位，于是 W+1..2W 的次段不会整体被推到 4W+1..8W，成员在保留 Flash 池宽的同时仍执行原搜索的次段语义。`BaseScoreOnly`、连续致命意图与上下文排序仍照原排序值组合。
- **首层承诺展开**：策略开启时，FlashBeam 的第一个回合层照现有引擎 `CombatBeamSolver.Phases.cs` 的 `searchedTurnLayers == 0` 分支，对 `HasPlayableFetchedPower` 等价状态（该动作的主选择效果为 `MoveToHand`、取回的能力牌已在手且付得起）先执行其全部合法后继再参与本层排序；中间选择状态不提交，后继照常走节点计数、容量、去重与终局提交。条件按 `CardChoiceSupport` 的主选择来源（8 张取回牌）与 Droplet of Precognition 药水逐项对照，策略关闭或其选择未取回可支付 Power 时不生效。现有引擎的回合层局部墙钟切片在 FlashBeam 中本来就不存在（Flash 每层固定按 Beam 宽度截断，只有全局节点/时间预算），因此策略开启时无需额外关闭切片。
- **计划承诺**：`ContinuationPurpose.PlanCommitment` 的固定前缀仍由现有引擎 `ApplyFixedPrefix` 回放，Flash 从前缀末状态导入搜索根。FlashBeam 照 `PlanCommitment.CountRealizedPayoffs` 在节点链上累计三类收益证据：`CardPlayed` 在 `turn >= Payoff.EarliestTurn` 时按收益卡计数；`FreePotionUsed` 按收益药水身份与用药前后战略成本不变计数；`RegisteredPowerBenefit` 读路线承诺元数据的 `RealizedEvidence`，按 128 位已打能力集合判断收益卡是否在承诺内，取整条链上的最大值。`InitialPlanPayoffCount` 用现有引擎从请求根回放固定前缀并调用同一个 `CountRealizedPayoffs` 得到；前缀节点没有承诺元数据，因此 `RegisteredPowerBenefit` 在两侧都只统计 Flash 搜索阶段新建的承诺。`CopyPower` 计划在保留时照 `AdmitPlanCommitmentRepresentatives` 取已兑现进度最高的三个进度带，每带保留 `BeamRankScore` 最高的代表，且只替换兑现进度严格更低的非必保席位（`MaxBy` 使用不含药水成本罚分的纯排序值）。物化后 `CardPlayed`/`FreePotionUsed` 按同一规则重数整条 `PlanAction` 链并要求与 Flash 的计数相等；`RegisteredPowerBenefit` 的旧承诺链只存在于现有搜索的展开节点，物化固定前缀不重建它，因此该证据由 Flash 的承诺元数据计数并在物化阶段保持同一数值。现有 `_planCommitment` 的另一处读取 `PlanHorizonPolicy.ShouldExtend` 只放宽现有搜索的跨回合无进展剪枝；FlashBeam 本来就没有这条剪枝（只按回合层/Beam/全局预算截断），计划视野在 Flash 上是等价宽松，不需要单独的视野字段。分发点不再因三类收益证据中的任何一类保留原搜索。
- **力量承诺与 `AggressivePowerCommitment`**：Bridge 把 `PowerCardValuationModels.Registry` 的逐卡承诺描述编译进 `FlashBeamOptions.PowerCommitments`（机制族、优先级、准入政策、总楼层），`FlashBeamSolver` 在每个路线节点旁保存紧凑承诺元数据（机制族/优先级/打出的能力集合/轮次转换/最后证据轮/投资/未兑现潜力/进展与兑现证据/开启动作数）。这是路线节点元数据，不进入 `FlashBattle`、指纹或状态键；相同战斗状态仍按原字节去重，并保留排序值更高代表的承诺历史，与旧 `SearchNode.PowerCommitment` 的去重口径一致。保留时照 `PowerCommitmentSeatPolicy.SeatQuota` 取席（aggressive 为一半 Beam，否则 `clamp(W/12, 2, 12)`），照 `PowerCommitmentRetention.RankRepresentatives` 先按（机制族、药水数、回合、能力集合）取每组代表，再按兑现证据/优先级/进展/净未兑现/开启动作数/轮次/生命/分数排序，只替换最后一批非必保、非代表席位并标为必保；`PowerCommitmentLifecycle.Advance` 的寿命按 profile 开关取 3（aggressive）或 2 回合转换。逐卡证据在 `FlashPowerEvidence` 中移植：`*PowerHasTriggerEvidence` 的全部卡牌分支、`*PowerTriggerProjectionFloor`、`*PowerOpeningProjectionPotential`（含 `PowerTurnFrontier` 的手牌子集 DP 与 `PowerGrowthFrontierPotential` 参数）以及 Silent 的 `SilentPowerProgressEvidence`/`SilentPowerRealizedEvidence`（Master Planner 的 Sly 价值差、After Image/Footwork/Well Laid Plans/Speedster/Tools of the Trade）。输入来自 `FlashBattle` 的字节状态、K4 逐字段核对的特征（`PlayerBlock`、`ProjectedPlayerHp`、`ReachableHandValue`、`Turn`、`AliveEnemyCount` 等）和根冻结的逐卡评分表（卡牌类型/动态变量/关键词/费用/Sly/攻击段数）；能量单位、抽牌数、剩余回合与最大能量由同一份态与评分表重算，不读取旧模拟器。上述读取的状态与特征在 K1/K2 指纹与 K4 中逐字节/逐字段核对，因此不会出现仅 Flash 可见的分支事实。
- **新颖度搜索**：`SearchPolicySnapshot.NoveltySearch`（`NoveltySearchOptions`）由 `FlashBeamOptions.Novelty` 交给 FlashBeam，执行与现有 `CombatBeamSolver.RunNoveltyOpen` 同一套 Bfws 调度：有界 `MaxOpen` 优先队列按（新颖宽度、`Score`、入队序号）取最小，`MaxActions`/`MaxTurns` 的父节点记 `HorizonLeaves`，熟悉子状态共享最近新颖祖先的 `FamiliarAllowance` 预算，`MaxNoveltyEntries` 达到上限后置 `novelty_limit`。新颖事实由 `FlashNoveltyFacts` 从 K4 逐字段核对的特征、四个牌堆的（卡牌 Id、升级、数量档）、抽牌堆前三张与有效 Power 生成；键、档案、已生成元组与 open 队列都是路线/搜索元数据，不进入 `FlashBattle`、指纹或状态键。终局候选沿用 Flash 的 `CompareRoutes`（现有 `FinalPlanOrdering.Select` 的同口径键）与移植的审计终局过滤，`NoveltySearchTelemetry` 逐字段写入 `SolverResult.NoveltySearch`；`NoveltyBudget` 与探索/精炼的展开数、墙钟余量继续由协调器按实测工作量扣减（Flash 展开计入请求账本）。新颖度搜索在每个父节点前响应接管、每 200 ms 发布一次进度与当前最佳路线预览，不再只在搜索结束时发一个检查点。
- **早期回合侦察**：`EarlyTurnScoutDepth` 是 FlashBeam 的回合层上限；每完成一层照 `SelectEarlyTurnFrontier` 过滤该回合的终止子状态、按策略键（开手动作、持续铺垫特征、药水数、持续增益档、可达手牌档、焦点目标）每组取分数前二，再按开手动作分组轮转报告最多 24 个候选。候选的排序值、准备特征、药水数与焦点目标都来自 K4 逐字段核对的 Flash 特征；`SearchRouteTraits` 尚未建模，策略键省去该维（只会合并分组，不会新增候选）。Flash 在物化与逐步核对全部成功后才把候选翻译成 `PlanAction`、读取旧引擎 `StateKey` 并调用观察者；失败时不发布任何候选（On 停止部署，Shadow 只发布现有搜索的候选）。计划 pass 自己的侦察按任意正深度接管；前缀、计划承诺与药水约束（基线、最早回合、上下界、`RequireAtLeastOne`、强制指令）都交给同一套 Flash 搜索与物化后核对。
- **评分**：由 `FlashScoreBridge.Create` 在根上编译逐内容数值表（卡牌估值与动态变量增量、计算伤害乘数、潜在布置值与类别、Power 的战略效果分类/参数、药水战略成本），冻结评估读取的 `Initial*` 与 policy 值，返回 `FlashScorer`。`FlashScorer.Features(in FlashBattle, actionCount)` 逐项移植旧引擎 `CombatBeamSolver.Snapshot`（`src/Search/CombatBeamSolver.StateEvaluation.cs`）：同样的项、同样的运算顺序与 `checked`/取整位置，特征直接从 `FlashBattle` 计算，不构造 `SimulationSnapshot`。节点分数与终局比较用 `Features(...).Score`；层内保留排序用 `FlashScorer.BeamRankScore`（旧引擎 `BeamRetentionPolicy.BeamRankScore`），连续威胁与上下文排序修正也在同一个排序值上。K4 在同一状态逐字段核对每个特征（成长/遗物字段与 `Turn` 含在内），`Score` 按 double 逐位比较；差分探针在开启这两个 profile 开关时另按 double 比较同一状态的 `BeamRankScore`（`rankChecked`/`rankFailed`）。
- **成长与遗物计数目标**：`SearchPolicySnapshot.EffectiveGrowthBudgets` 与 `RelicTargets` 冻结进 `FlashScoreRules`，不再在准入时拒绝。内核用五个打包计数器承载 `SimulatedCombatState._growthRewards` 的十个原版来源（`HandOfGreed`/`TheHunt`/`Feed` 的致命击杀、`Royalties`/`Alchemize`/`GeneticAlgorithm`/`TheScythe`/`Goopy` 的成功结算与牌组副本门控；ForbiddenGrimoire/MadScience 没有 Flash 定义，其累计值只在根导入历史时保留），根导入从同一字段读取历史；十个来源的 16 位计数两两打包进现有计数器数组，`FlashBattle` 布局与 `ExpectedSize` 不变。遗物目标按 `RelicCounterCatalog` 的周期与范围编译到对应 `FlashRelic.Counter`（Meat on the Bone 用当前 HP 阈值重算），`FlashScorer` 复刻 `EvaluateRelicCounters` 的 `SatisfiedMask`/`TargetMask`/`HpCredit`/`Distance`/`SatisfiedPriority` 与胜利后的 `HealingHpCredit`。评分项、`StrategicHpCredit`、`StrategyGoalHpCredit`、`StrategyGoalCount` 与终局排序键（战略失血之后依次比较成长信用与收益数）与现有快照逐项一致；有界成长目标与遗物目标也进入 HP 目标早停条件。第三方 `GrowthSourceMirrors` 额度/目标仍按 `UnsupportedRequest` 拒绝。`Profile.ContinuousThreatRanking` 与 `Profile.ContextualRanking` 也在此层移植：前者只在 EndTurn、存活、投影 HP ≤ 0 且仍有可支付手牌时把死亡罚分换成连续 HP 缺口；后者把同一组 K4 特征映射进 `ContextualRankingModel.Input`，逐状态得到与现有 `BeamRankScore` 相同的修正，终局/非存活上下文仍绕过。`DevelopmentStrategy` 仍拒绝（见下文）。

- **第三幕首领策略**：`SearchPolicySnapshot.Act3BossStrategy` 冻结进 `FlashScoreRules`，不再在准入时拒绝。评分表按该开关编译 `StrategicEffectModel.Requirements`，`Lethality`、`Pagestorm`、`DanseMacabre`、`Demesne`、`PrepTime` 的 `Act3BossInteractions` 估值分支逐项照搬；`FirstAttackDamage` 与 `CaptureAct3BossInteractionPotential` 的 `EtherealDrawTriggers`、`PagestormBonusDrawCapacity`、`HighEnergyPlays`、`DemesneEnergyGain`、`DemesneDrawGain` 从 `FlashBattle` 重算，策略关闭时不进入这些读取。六个新特征连同 `Act3BossInteractions` 标记进入 K4 逐字段核对（参考侧由 `SimulationSnapshot` 保存同一状态的上下文值）。
- **评估的保留专用值**：`ReachableHandValue` 与 `ZeroCostPlayableCount` 只被保留比较器读取，不进入 `Score`、`BeamRankScore` 或终局 `RouteKey`。`Score` 调用 `Features(..., includeRetentionOnly: false)` 跳过它们；状态真正进入保留池时 `Pool.Insert` 才计算一次，重复状态（字节相同的代表）沿用已存的值，终局状态完全不计算。K4 仍用默认的 `Features(...)`（`includeRetentionOnly: true`）逐字段核对这两项。可达手牌估值直接复用 `FlashKernel.CanPlayCard(..., out energyCost, out starCost)` 已经算出的费用，不再对同一张牌、同一牌堆重复执行能量/星能费用 Hook。
- **评分表**：每张已准入的卡牌按（内容 Id, 升级等级）有明确条目；每个 Power 有战略效果分类与参数。旧引擎判定不贡献的项显式记为 0（例如非玩家侧 Power、临时 Power、`StrategicEffectMirrors.Contributes` 为 false 的项）。未移植的逐内容乘数或请求设置在根上以 `FlashRejection.UnsupportedCard` / `UnsupportedRequest` 拒绝，不默认记 0。
- **选择**：遇到 `NeedsChoice` 时用 `FlashChoiceEnumerator` 按选项规格枚举（选择数量从小到大、同数量按下标字典序），每个请求至多探索 32 个分支（v1）；每个被执行的分支都是精确的，上限只限制探索面。旧引擎按估值排序挑分支（`MaxPile/HandChoiceBranchesPerAction`），v1 尚未移植这套排序。
- **药水**：Bridge 在根上取现有搜索的药水约束（`CapturePotionPolicyForFlash`：最早回合、最多使用次数），并把 `PotionStrategySnapshot`、有效政策与 `Disabled` 覆盖一起冻结进 `FlashPotionPolicy`；未设置时不使用药水。FlashBeam 对槽位中当前任何可搜索药水生成动作，按现有 `AllowsPotionUse` 的（槽位，药水身份）门 `PotionStrategySnapshot.AllowsExplicitUse` 判断，因此搜索中生成的药水与根药水使用同一规则，而不是只对仍是根上原药水的槽位生成。旧引擎的 `Score` 不含药水成本，因此非审计请求在**排序值**上额外减去已用药水的战略成本（`SolverWeights.Hp` 计价）；这是 Flash 特有的项，节点分数与终局比较仍用 `Features(...).Score`。审计请求（`AuditPotionPolicy` 且精确层 `MinimumPotionUses > 1`）的排序值直接使用现有 `BeamRankScore`：该层必须使用多瓶药，Flash 特有的成本罚项会把必需路线排到尚未用药的状态之后；成本仍由终局 `FinalPlanOrdering` 计入。单药审计层保留该罚项，避免改变既有的 1 药路线。固定前缀已经用掉的药水不在这段槽位差里：Bridge 把搜索根快照的累计 `PotionUseCount`/`PotionStrategicCost`/`ExplicitPotionStrategicCost` 与自动用药次数冻结进 `FlashScoreRules` 和 `FlashBoundFeatures`，评估与下界再加回这些前缀计数；`ExplicitPotionUseCount` 是新增的 K4 字段。`ExplicitPotionUseCount` 同时计入根槽位消耗与搜索中生成的用药（`GeneratedPotionUseCount`）；节点记录每次用药的实际药水身份，强制指令与 Ambergris 计数按身份匹配，生成的同身份药水也计入。导入搜索根时 `FlashStateImporter.ImportPotions` 按搜索根的累计 `PotionUseCount` 跳过前缀已消费的用药记录，只把搜索根之后的用药计入 `GeneratedPotionUseCount`/`GeneratedPotionStrategicCost`；否则前缀用药会同时出现在 `Initial*` 与 generated 计数里，精确用药层会看到多一次。`DropletOfPrecognition` 的抽牌堆选牌（现有引擎 `PotionChoiceSupport` 的 `MoveToHand`/`Draw` 精确选择）已在内核里请求选择并按 `AddToPile(Bottom)` 移动；`LiquidMemories`（弃牌堆 → 本回合免费回手）、`Ashwater`（手牌 0..N 消耗）、`GamblersBrew`（手牌 0..N 弃置再抽）与 `TouchOfInsanity`（手牌中耗能/耗星卡本场免费）按 `PotionChoiceSupport.GetSpec/Apply` 的选项、来源牌堆、数量与效果实现，空选项也发一个零选选择请求；Bridge 的 `TryBuildPotionChoice` 对所有 `PotionChoiceSupport.RequiresChoice` 药水从探测回放构造 `PlanAction.Choice`。
- **精确用药层（Smart 审计）**：`SmartPotionGradient` 与 `SmartOpeningPotionPosterior` 请求由 FlashBeam 搜索时，`FlashBeamOptions.AuditPotionPolicy` 打开现有 `FinalPlanOrdering.Select` 的移植：按 `minimumPotionUses`/`MaximumPotionUses` 过滤，`RequireAtLeastOne` 的 `PotionUsePolicy.IsEligible`、`PotionFreePolicyBaseline`、死亡保护、Ambergris 限制，并按同一次序（含 `PolicyHpDeficit`）选路线。精确层的显式用药计数包含搜索中生成的用药，槽位动作按当前药水身份判断，不再要求等于根药水。找不到满足策略的路线时返回 `PotionPolicySatisfied=false`，由分发点转成与现有搜索同形的 `PotionPolicyUnsatisfiedException`（规则 1）；这是无路线结论，不回落、也不算失败事件。`_smartPotionEligibilityHpCeiling` 由现有构造函数的共享静态入口计算，Flash 在每次保路后按已由 K5 核对的 `FlashBoundValues` 逐状态剪枝（组件证书未知的分支保留）。药水前缀会先由现有引擎 `ApplyFixedPrefix` 回放，因此前缀可以以药水开头。强制使用、最早药水回合、`RequireAtLeastOne` 与计划承诺也由 Flash 执行（见 3.1 与分发点）。
- **早停**：照抄现有搜索的两种早停：HP 目标（请求可在 HP 目标处停止、且不要求显式药水时，一次无死亡豁免、显式药水不超过额度、本场已失血加路线失血不超过可接受值的完整胜利，在下一个父节点之前结束搜索）与零伤害（胜利且未失血、未用显式药水、满血且最大生命未减少时，当前回合结束后停止）。
- **incumbent 下界剪枝**：移植现有主搜索成员的局部 HP 下界（`CombatBeamSolver.Retention.cs` 的 `CanUseLocalPrimaryHpBound`、`IsLocalPrimaryHpBucket`、`ApplyPrimaryIncumbentBound`/`ApplyPrimaryIncumbentBoundCore`、`ShouldPruneByPrimaryIncumbent`、`TryTightenPrimarySearchIncumbent`、`TightenPrimarySearchIncumbentAtTurnLayer`）。`FlashBoundFeatures` 在根上冻结 `FutureHealPotential`、`HasRisk`、剩余治疗额度（Component/KnownNative 证书）、战略首领减伤与初始 incumbent；求值只读 `FlashBattle` 和冻结表。终局完整胜利在提交时收紧 incumbent（与现有搜索的同一层 Prune 对齐）；现有搜索的回合结束收紧作用于同一批已提交的 `completed` 终局，Flash 在提交时已经收紧，因此不重复。每个动作层保留之后与回合边界保留之后各剪一次；被剪掉的席位不补。参数沿用现有语义：`allowTurnTieBound` 由 `costClosed` 门控，`pruneEqualHp` 只对非终局、无风险且药水成本闭合的节点生效，`costClosed` 读 `ExplicitPotionStrategicCost`。某个下界输入在 Flash 上无法精确给出时该根整体禁用剪枝，汇总行写 `bound=disabled(<原因>)`，不使用近似值。共享的 `PrimaryIncumbents` 表不接入，避免对照模式的隔离泄漏；初始 `PrimaryIncumbent` 由请求传入并按 `FlashBattle.PlayerTurn` 相对根的偏移换算。
- **容量边界（分支级）**：搜索展开中的容量溢出按“一个子状态装不下”处理，只丢弃该子状态并继续：内核执行动作时的状态级写入（卡牌、Power、已知敌人、遗物、充能球、`DetachedCards`、小数卡牌变量、费用修饰、选择选项、动作选择链、`FlashExec` 的暂存队列等）、动作执行后的评分，以及提交到非累积池，全部在 `ExpandAction` 的同一个边界内捕获 `FlashCapacityException`；一次选择请求的某个选项分支溢出只丢弃该分支，同请求的其余选项照常展开。诊断按容量区块分别计数（`FlashBeamResult.CapacityDroppedChildren`，日志 `flash_capacity_dropped=Cards:1,Powers:2`，合计仍在 `flash_capacity_pruned`）。这与节点预算同类，是搜索边界而不是模拟错误；采用的路线仍逐步核对。**根状态本身**装不下仍是导入拒绝：`FlashStateImporter`/根评分在展开循环之前抛出的容量溢出与 `FlashUnsupportedException` 一样让整次 Flash 尝试失败，开启模式写 `FLASH_SEARCH_FAILED reason=kernel-capacity` 并停止部署，不回落。
- **回合结束层压缩**：`ended` 只在累计子状态达到容量上限（`min(width × (MaxLegalActions + 1), 向下取整到 64 槽的 200 MiB / sizeof(FlashBattle) = 16,512)`）时触发压缩：把当前全部 `ended` 子状态按与回合边界 `CopyTopTo` **完全相同的选择函数**（按 Beam rank 的前列席位、进攻席位，以及全部必保代表——每个药水分组的各泳道/整理/回合边界手牌领先者、持续铺垫特征与焦点目标分组、根动作谱系、偷窃根的资源保全代表等）重选到 `clamp(width × 8, width, 容量)` 个席位，原地前移并重建去重表后继续累积。压缩目标取 8 倍 Beam 宽度的依据：回合边界最终只消费 `width` 个席位，8 倍宽度保留了 8 组完整的排序席位并容纳全部必保代表（宽度 4–512 时目标为 32–4096 个状态，约 0.4–51 MB）；它与容量上限之间留有足够余量（宽度 ≥ 92 时上限是 16,512 个状态；更小宽度取原 `width × (MaxLegalActions + 1)`），正常回合不会触发压缩。触发条件严格是达到上限，因此 `ended` 未达到上限的根不会进入压缩，路线、转移数、展开数与去重数逐位不变；只有原本会超出容量上界的根才观察到压缩后的保留集合，且采用路线仍逐步由现有引擎回放核对。压缩次数与被压缩掉的子状态数分别记入 `FlashBeamResult.EndedCompactions`/`EndedCompressedStates`，日志为 `flash_ended_compactions`/`flash_ended_dropped`；压缩不释放已分配的块，后续累积复用同一批状态块。
- **预算**：取同一 `SolverSearchProfile`：Beam 宽度与全局节点上限乘以 `FlashRouteMaterializer.ProfileWidthScale`（4），软时间不变。FlashBeam 只有排序泳道与进攻泳道，需要更宽的 Beam 才能达到现有搜索的路线质量（[搜索演进测试记录](../archive/flash-search/testing-search-2026-10-19.md) 2.3）。旧引擎按回合层分配节点预算的规则尚未移植。
- **产出**：按旧引擎终局比较器（`FinalPlanOrdering.Select` 的排序键）选出一条路线：完整胜利、存活、死亡保护次数、战略失血（`ActEndingBossPolicy.StrategicHpDeficit`，含成长/遗物信用与战后遗物回复）、成长信用、成长收益数、结束回合、生命资源消耗、长期资源、愤怒复制数、可选药水数、敌人 HP、分数、动作数。没有胜利路线时，预算耗尽时的回合起点状态与终局状态一起参与比较。偷窃、卖血与边界原因这几个键尚未移植。

## 2. 路线物化

`FlashRouteMaterializer` 把候选路线交给共享回放核心：

1. 从根分叉旧模拟器，逐步回放 Flash 动作。每一步按手牌位置取牌，按旧引擎的规则生成 `PlanAction`（`CardStateKey`、occurrence、`TargetCombatId`、选择令牌），并要求旧引擎这一步的投影与 Flash 逐字节一致。回放走 `SearchReplayCore`，与现有搜索和开局探针同一实现。
2. 把整条已核对路线作为固定前缀，在共享回放核心上重放成父链（`ApplyFixedPrefix`），再用共享 `SearchRouteMaterializer` 按既有终局排序、标注和物化步骤生成 `SolverResult`。原搜索的最终路线也走同一个物化器。

固定前缀续接在导入前先用同一 `ApplyFixedPrefix` 规则回放请求前缀（`OpeningProbe.ReplayFixedPrefixForFlash` / `SearchReplayCore`），把前缀末尾状态作为 Flash 搜索根；步骤 1 从该状态开始核对，步骤 2 的前缀是“请求前缀 + Flash 路线”，由共享物化器从请求根完整回放。

**回合准备根（`IncludeTurnSetup`）**：玩家回合开始的原生选择页在根捕获时还没有作答，捕获的根是 `Player/Start`。Bridge 不把该 Start 状态导入内核，而是先调用共享 `SearchReplayCore.CaptureTurnSetupRoots`（`ChoiceSearchBudget`/`ChoiceOccurrenceCollector` 的唯一实现），按与现有搜索**完全相同的选择预算**把每个准备选择分支回放到稳定 `Play` 状态，再逐个导入 `FlashBattle`。`FlashBeamSolver.Solve` 接收这组根（现有搜索的 frontier 同样从多个准备根出发），返回选中路线的 `BestRootIndex`；路线本身从 `Play` 状态开始，准备选择不占动作数。物化时把选中的 `PlanCardChoice` 向量作为根节点元数据交给共享 `SearchRouteMaterializer`，由它从准备选择后的稳定 `Play` 根回放固定前缀；结果的 `TurnSetupChoices`/`TurnSetupPlayState` 与现有语义同形。物化后逐项核对选择向量，不一致按 `FlashVerificationException` 整条丢弃。准备根在回合开始的生成效果之前捕获，在这些时机生成卡牌的定义（如 `BigHat`、`VexingPuzzlebox`）必须声明 `GeneratedCardPools`，闭包才含其候选卡。回合准备搜索的 Flash 失败与协调器搜索同一约定：写 `FLASH_SEARCH_FAILED`、停止部署、关闭全自动并弹出 Flash 提示，原生选择留给玩家作答。

实现在 `src/Flash/Bridge/FlashRouteMaterializer.cs`、`src/Search/Probes/SearchReplayCore.cs` 与 `src/Search/Probes/SearchRouteMaterializer.cs`。

回放中任何一步与 Flash 投影不一致都按 [运行时回放核对](admission-and-verification.md#4-运行时回放核对) 处理。

固定前缀节点按"未比较的回合结果"标注（`CreateUncomparedTurnOutcome`），与协调器续用路线的口径相同；因此 Flash 物化结果的"卖血"统计与完整旧搜索不同，比较路线质量时以战损、胜负与结束回合为准。

## 3. 请求参数

协调器宽度组合的成员（`DirectSearchPurpose.PrimaryBeam` 与 `RefinementBeam`）、Smart 药水梯度的直接审计（`DirectSearchPurpose.SmartPotionGradient`，经同一 `BeamSolveDispatch.Solve` 入口）、协调器直接药水审计点以及带药水约束的固定前缀续接请求经过 Flash。中途排序的 profile 变体按现有 `BeamRankScore` 移植：仅基础分成员只用基础分，权重扰动对非终局状态把指定项乘以系数；连续致命意图定价与上下文排序已移植，不再因这两个 profile 开关拒绝。`DevelopmentStrategy` 的 `Rank` 只读 `StrategyNodeFeatures`，其中每个值都能由 K4 核对过的 Flash 特征给出；但 `Prioritize` 依赖现有 `ActionCandidate`（含每个候选的伤害、格挡、消耗、归一化值与 `MaxCardBranchesPerNode` 截断），`Retain` 依赖 `RankBest` 的 `preserveDefensiveRoute`/`finalQualityFirst` 必保通道，Flash 没有这些结构与模式标志。只搬 `Rank` 会静默丢掉另外两个钩子的排序调整。当前只在关闭模式支持开发策略：开启模式在分发点直接写 `FLASH_SEARCH_FAILED reason=development-strategy` 并停止部署，不猜测、不回落；将来若定义只含 Flash 特征的输入/接口，再由协调者决定放开。

`BeamSolveRequest` 各字段在 Flash 中的处理：

| 字段 | 处理 |
| --- | --- |
| `Root`、`DisplayNames`、`BattleDamage`、`Policy`、`CancellationToken`、`Profile` | 支持；FlashBeam 的宽度与节点为 `Profile` 的 4 倍，时间取自 `Profile` |
| `PotionPolicyOverride` | 支持：回放求解器与物化求解器带同一覆盖，`CapturePotionPolicyForFlash` 据此取槽位约束 |
| `ProgressCallback` | 支持：按现有搜索的 100 ms 进度节拍与 200 ms 路线预览节拍报告；预览路线用共享 `SearchReplayCore` 从请求根回放（固定前缀包含在内），路线采纳种子由共享 `SearchRouteMaterializer` 惰性物化 |
| `PrimaryIncumbent` | 支持：作为 FlashBeam 的初始 incumbent，`CombatEndedTurn` 从 `node.Turn` 换算到 Flash 玩家回合；共享的 `PrimaryIncumbents` 表不接入 |
| `Purpose` | 决定是否查看模式，并作为 Flash 搜索与物化求解器的工作量归因 |
| `Policy.IncludeTurnSetup` | 支持：根必须是 `Player/Start`；Bridge 按现有 `BuildTurnSetupRoots` 的选择预算枚举准备根，FlashBeam 从全部准备根搜索，物化时固定选中的选择向量 |

`CombatBeamSolver` 的其余构造参数（`maximumPotionUses`、`minimumPotionUses`、`earliestPotionTurn`、`fixedPrefixActions`、`potionFreePolicyBaseline`、`earlyTurnScout*`、`planCommitment`）在主搜索成员中不使用。Smart 梯度审计通过 `BeamSolveRequest.PotionFreePolicyBaseline`、`MaximumPotionUses`、`MinimumPotionUses` 携带精确层参数；区间上下界、基线、最早用药回合、强制指令与 `RequireAtLeastOne` 均已移植（见 3.1）。固定前缀续接通过第二个分发入口 `BeamSolveDispatch.SolveContinuation` 走同一 `FlashMode`。除不支持的根阶段外，所有续接形态都交给 Flash：任意 `ContinuationPurpose` 上的计划承诺（含收益证据、计划前缀与 `CopyPower` 代表席）、计划阶段自己的任意正深度侦察（空前缀或非空前缀、带不带承诺均可）、带任意药水约束（基线、最早回合、上下界、`RequireAtLeastOne`、强制指令）的续接，以及其它带侦察的续接。分类门与 `can-take-over:*` 原因码不再产生。

- 根阶段不是受支持的两形：普通 `Player/Play`，或 `IncludeTurnSetup` 的 `Player/Start`。其余 side/phase 在开启模式写 `FLASH_SEARCH_FAILED reason=root-phase detail=root-phase:<side>:<phase>` 并停止部署（Flash 内核只表示稳定出牌状态，不能把敌方回合或其它准备阶段当作出牌根搜索）。对照模式保持原行为：只跑现有搜索。
- 药水上下界为负或 `MinimumPotionUses > MaximumPotionUses`：按现有求解器 `SolveCore` 的同一参数校验抛出 `ArgumentOutOfRangeException`，不执行搜索，也不写失败事件或遗留日志。

`PolicyOverride` 作为求解政策使用；`PrimaryIncumbent` 按 SB 的口径传入。`ProgressCallbackOverride`（没有时用请求的进度回调）现在同时交给 Flash 搜索，续接搜索按同一节拍发布进度、预览与路线采纳种子。`ResetFixedPrefixSchedulingBaseline` 对 Smart 药水后验请求转交给共享回放核心，使固定前缀后的调度基线与现有搜索相同；其余续接请求仍不读取它。profile 中 Flash 尚未建模的开关仍由准入按 `UnsupportedRequest` 拒绝；开启模式把它转成 `FLASH_SEARCH_FAILED reason=admission-unsupported-request`，不回落。

### 3.1 放开的药水续接与精确层

续接请求的 `MaximumPotionUses`/`MinimumPotionUses`、`PotionFreePolicyBaseline`、`EarliestPotionTurn`、`RequireAtLeastOne`（政策或覆盖）、`Smart` 政策无 `Disabled` 覆盖以及强制用药指令都交给 Flash。带任意一项药水约束、或续接的有效政策为 `Smart`（无 `Disabled` 覆盖）的请求打开 `FlashBeamOptions.AuditPotionPolicy`：Flash 按显式用药次数收集每档最佳终局候选，再用移植的 `FinalPlanOrdering.Select` 过滤与排序：

- **区间**：`ExplicitPotionUseCount >= MinimumPotionUses`，扩展期由 `MaximumUses` 限制用药动作；两者不再要求相等。计数都包含固定前缀。
- **基线**：`PotionFreePolicyBaseline` 作为审计后的无药基线；无覆盖时取本次搜索的最佳无药候选。
- **排序键**：完整移植 `FinalPlanOrdering.Select` 已由 K4 核对的特征键（胜利、存活、死亡保护、偷窃恢复、战略失血、成长信用与收益、结束回合、政策失血、生命资源消耗、长期资源、愤怒复制、可选药水数、敌人 HP、分数、动作数）。卖血与边界原因两个键仍未移植（沿用原有缺口）。
- **强制指令**：按 `(槽位, 药水身份)` 的集合逐条核对，等价于 `PotionStrategySnapshot.EvaluateForcedUses`；前缀已用的强制药水从固定前缀动作折入。未满足的路线不能通过终局过滤，同次数下不会遮蔽满足的路线。强制使用次数与战略成本从可选用药中扣除后再套用 `RequireAtLeastOne`/`Smart` 资格。
- **最早用药回合**：`EarliestPotionTurn` 按请求根回合换算后限制 Flash 的用药动作；物化后再核对没有更早的用药动作。

物化后的 `FlashPotionBridge.VerifyRoute` 复核动作与显式用药计数、最小/最大次数、最早回合、`RequireAtLeastOne` 与强制指令身份；任一不满足按 `ConstraintFailure` 交给分发点。共享 `SearchRouteMaterializer` 使用的 `FinalPlanOrdering` 在物化阶段拒绝不合规路线时记 `materialize-potion-policy`。FlashBeam 的终局审计已按同一口径过滤（含强制指令、显式次数上下界、Smart/RequireAtLeastOne、Ambergris 与偷窃恢复；其中软策略的偷窃恢复要求确实用过药水，Ambergris 例外按恢复后的 loot 键判断）；只有 Flash 搜索自己判定 `flash-potion-policy-no-route` 才是无路线，`route-*`/`materialize-potion-policy` 是两侧判定不一致，开启模式按 `potion-policy-mismatch` 显式失败。

`SecondRankBand`、`AggressivePowerCommitment`、`BaseScoreOnly` 都由 FlashBeam 执行：次段在全局剪枝取次段席位，力量承诺泳道按上面的席位/寿命移植；分发点不再因这些 profile 开关保留原求解器，也不会静默忽略它们。此前已经是“干净请求”的续接保持原行为。

`SmartOpeningPotionPosterior` 续接也交给 Flash。原先把“非 Smart 政策”和“根无可搜索药水”当作 `potion-audit-boundary:*` 的语义边界已删除：这两种情况下现有求解器执行的就是带同一上下界/基线的普通请求，Flash 执行同一请求；无药可审计时 Flash 与现有求解器同为无路线，后者按既有 `PotionPolicyUnsatisfiedException` 结束。

### 3.2 开启模式的无路线与失败处理

开启模式（`FlashMode == On`）只运行 Flash，`SolveExisting`/`SolveExistingContinuation` 不会在该模式执行，因此不再有 `FLASH_LEGACY` 行；该类一并删除。`FlashMode == Off` 只跑现有求解器；`Shadow` 保持原状：现有求解器先执行并返回，Flash 只写一行 `FLASH_SHADOW` 对照。

分发点按以下两类处理未采用的结果：

1. **无路线（接受 Flash 结论）**：只有 `ConstraintFailure=flash-potion-policy-no-route` 时，不搜索、不回落，抛出现有调用方已经处理的 `PotionPolicyUnsatisfiedException`（同一异常类型），并写一行 `FLASH_NO_ROUTE reason=flash-rejected:flash-potion-policy-no-route detail=<原因>`。它不算失败事件。精确层下界输入不可用（`smart-potion-bound unavailable`）只关闭下界剪枝，搜索照常继续，不是无路线。
2. **显式失败（停止部署）**：写一行稳定事件并抛出 `FlashSearchFailedException`：

```
[CombatSolver/Flash] FLASH_SEARCH_FAILED reason=<类别> detail=<首个差异/拒绝原因>
```

| `reason` | 触发时机 |
| --- | --- |
| `verify-mismatch` | 旧引擎逐步回放与 Flash 投影不一致（`FLASH_VERIFY_MISMATCH`） |
| `root-phase` | 根不是 `Player/Play`，或 `IncludeTurnSetup` 的 `Player/Start`（`detail=root-phase:<side>:<phase>`） |
| `development-strategy` | 开启了仅开发者使用的 `DevelopmentStrategy`（当前只在关闭模式支持） |
| `kernel-unsupported` | 根导入或搜索中抛 `FlashUnsupportedException` |
| `kernel-capacity` | 根导入或搜索中抛 `FlashCapacityException`（子状态级容量丢弃不算） |
| `potion-policy-mismatch` | Flash 选出并物化的路线被 `FlashPotionBridge.VerifyRoute`（`route-*`）或现有 `FinalPlanOrdering`（`materialize-potion-policy`）判定不满足请求的药水约束；两侧过滤口径不一致 |
| `admission-multiplayer` | 准入拒绝多人专用内容 |
| `admission-test-content` / `admission-deprecated-content` | 准入拒绝 Mock 测试内容 / 废弃内容 |
| `admission-third-party` | 准入拒绝第三方 gameplay 订阅者、BaseLib 修饰或未登记模型 |
| `admission-unsupported-content` | 未定义的卡牌、Power、遗物、药水、球、怪物、附魔、苦难或生成闭包 |
| `admission-unsupported-request` | 固定前缀/准备根无法回放，或其它 Flash 未实现的请求形态 |
| `admission-capacity` / `admission-unrepresentable-state` / `admission-pending-choice` / `admission-other` | 其余准入类别 |

Search 只通过注入的 `SearchDiagnosticsSink` 写事件并抛异常，不引用 Runtime/UI/logger。Runtime 沿用现有失败搜索路径：停止本回合部署、取消部署会话、清理搜索会话与待用基线、关闭全自动、记录问题包分类（`FlashSearchFailed` + 原 `SearchFailure`）、弹出中英文提示并通知无人测试 `Failed`。无人测试结果在 `failureCategory` 字段带同一 `reason` token。

原 `potion-audit-boundary:*` 与 `can-take-over:*` 原因码已删除：这些请求现在由 Flash 执行同一请求，或（非法药水上下界）按参数校验抛出同一异常。仅有的无路线结论仍是药水策略/审计没有可接受路线；仅有的显式拒绝是上表。


固定前缀续接的搜索口径：

1. 现有引擎从请求根按 `ApplyFixedPrefix` 的同一规则回放 `ContinuationSearchRequest.Prefix`，得到前缀末尾的 `SimulationSnapshot`（`OpeningProbe.ReplayFixedPrefixForFlash`）。前缀无法回放按 `UnsupportedRequest` 拒绝；开启模式写 `FLASH_SEARCH_FAILED reason=admission-unsupported-request` 并停止部署，不回落。
2. 前缀末尾状态导入为 FlashBeam 的搜索根，规则表也从该状态编译；评估与下界的根冻结表仍从请求根建立，对应现有求解器的 `_run.Initial*` 来自请求根。
3. FlashBeam 的节点动作数从 `Prefix.Length` 起算，使 `Score`、`RouteKey`、HP 目标早停中的完整胜利判定与下界剪枝看到与现有求解器相同的动作数。incumbent 和药水最早回合仍按请求根回合换算，不因前缀推进。
4. 最终路线 = 前缀 + Flash 路线。Flash 路线每一步照常由现有引擎从搜索根回放并与 Flash 投影逐字节核对；物化时由共享 `SearchRouteMaterializer` 从前缀 + 路线重放父链并构建结果，形状与现有后端的续接结果相同。

每次 Flash 求解向 `policy.RequestWorkTotals` 记录一次工作贡献：`ExpandedNodes`/`TransitionCount` 是 Flash 的实际展开与转移（转移还包含准入、回合准备、前缀、核对与物化的旧引擎回放），耗时、分配与 GC 计数取整次求解的实测值。共享节点预算另走 `BudgetExpandedNodes`：按请求实际使用的 profile 宽度倍数折算（生产常规成员为 4，新颖度/侦察等 `profileScale=1` 成员不折算），使宽 Flash 成员不会按物理展开数成比例地更快耗尽共享节点额度。实际数字仍写在 `FLASH_ADOPTED` 的 `flash_expanded`/`flash_transitions` 与 `FLASH_PROGRESS_STATS` 中。接管在层循环、出牌深度与每个新颖度父节点前按现有搜索的检查点读取：`AdoptRoute` 停止搜索并物化玩家请求的那个种子，`ApplyCurrentTurn` 只在存在当前回合边界候选时停止并以 `CurrentTurnAdoption` 物化；采用的路线仍逐步由现有引擎回放核对。

### 3.3 Flash 对原搜索类的剩余依赖

Flash 桥接、固定前缀回放、回合准备根与最终物化都已改用 `src/Search/Probes/` 的共享回放核心、`OpeningProbe`、`SearchEvaluator` 和 `SearchRouteMaterializer`；`src/Flash/Search/**` 与 `src/Flash/Bridge/**` 不引用 `CombatBeamSolver`，结构门禁同时检查这一点。

仍构造原求解器的位置只有分发点的关闭模式与对照模式的现有侧：

| 位置 | 依赖 | 归属 |
| --- | --- | --- |
| `BeamSolveDispatch.cs` | Off 只跑现有求解器；Shadow 先跑现有求解器再跑隔离的 Flash 对照 | 原搜索代码保留给关闭/对照模式，后续删除原搜索的 PR 再处理 |

开启模式没有原搜索构造点，也没有 `FLASH_LEGACY`。

`CombatBeamSolver.Flash.cs` 中的参考入口只服务离线差分探针（测试工具），不参与 Flash 生产路径；`OpeningProbe`、共享回放核心、三个开局续接来源与协调器探针调用点不在此表。

## 4. BeamSolveDispatch 与模式

`src/Search/BeamSolveDispatch.cs` 是唯一分发点。协调器 `RunSearchPass` 的 `SolveMember` 把参数打包为 `BeamSolveRequest` 交给 `BeamSolveDispatch.Solve`；`FrontierContinuationScheduler` 的 `Dispatch`/`DispatchOptional` 把 `ContinuationSearchRequest` 交给 `BeamSolveDispatch.SolveContinuation`。`PrimaryBeam`、`RefinementBeam`、`SmartPotionGradient`、协调器直接审计点 `PotionFreeAudit`、`RequiredPotionAudit`、`NarrowOpeningIncumbent`、`TurnBoundaryDiscovery`、新颖度探索 `NoveltyExploration`、自适应新颖度精炼 `AdaptiveNoveltyRefinement` 与早期回合侦察 `EarlyTurnScout` 都查看 `FlashMode`；Off 直接使用 `CombatBeamSolver`，Shadow 只把它用作返回结果，On 不再构造它。回合准备根（`Player/Start` + `IncludeTurnSetup`）由 Flash 接管；其它根阶段在 On 模式是 `root-phase` 显式失败。`SecondRankBand`、`AggressivePowerCommitment` 与 `BaseScoreOnly` 已由 FlashBeam 执行，分发点不再因它们保留原搜索；带药水上下界与 `RequireAtLeastOne` 的计划成员同样交给 Flash。`RunForcedPotionOpeningRescue` 的三个剖面变体都经 `BeamSolveDispatch.Solve`。`NarrowOpeningIncumbent`、`EarlyTurnScout` 与两个新颖度成员经分发点后使用 profileScale=1：窄开局保持原宽度，侦察 profile 已由协调器放大，探索/精炼的节点与时限就是请求预算；新颖度成员不再套用 Flash 常规 ×4。次段成员保留 ×4 宽度，只用 profile W 作次段边界；多个回合准备根作为同一首层的并列根送入 `Solve(IReadOnlyList<FlashBattle>)`。计划成员与计划阶段的任意正深度侦察同样查看 `FlashMode`。

模式由 `SearchPolicySnapshot.FlashMode` 冻结，主线程从设置 `SolverSettingsData.FlashSearchMode` 读取（设置 > 性能 > 搜索预算 >「Flash 快速搜索（实验）」）。离线宿主可用 `OFFLINE_HARNESS_FLASH_MODE=Off|Shadow|On` 覆盖。

| 模式 | 行为 |
| --- | --- |
| Off（默认） | 只用旧后端 |
| Shadow | 先跑旧后端，再在隔离策略下跑 Flash，返回旧结果并记录 `FLASH_SHADOW`；根阶段边界仍只跑旧后端 |
| On | 只跑 Flash；成功记 `FLASH_ADOPTED`；药水策略无路线按现有 `PotionPolicyUnsatisfiedException` 形状返回；其它拒绝记 `FLASH_SEARCH_FAILED` 并停止部署，绝不改跑旧后端 |

Shadow 的隔离策略清空 `RequestWorkTotals`、`PrimaryIncumbents` 与 `Interaction`，求解器诊断写入空接收器，并且不报告进度，因此返回结果、工作量与日志都与 Off 相同，只多一行对照。续接请求的前缀回放与物化求解器也使用同一份隔离政策，不向共享账本记录工作量。请求的初始 `PrimaryIncumbent` 是只读输入，Shadow/On 都按同一请求值传给 FlashBeam；两边都不写共享 `PrimaryIncumbents` 表，因此 Flash 的收紧只影响本次 Flash 搜索。

日志行前缀为 `[CombatSolver/Flash]`。主搜索成员行写 `purpose=<DirectSearchPurpose>`；续接的对照与采用行写 `purpose=<ContinuationPurpose> prefix=<前缀动作数>`。行内 `incumbent=<战略失血差>@<结束回合>` 是请求传入的初始 incumbent（`-` 表示没有）。Flash 段的 `status` 取 `verified`、`rejected`（带准入拒绝原因）、`mismatch`（带首个不一致步）或 `boundary`（Flash 搜索中途到达内核不建模的状态，或根级容量装不下）；`verified` 段记录耗时、胜负、HP、`hp_lost`、`end_turn`、各阶段耗时以及 FlashBeam 节点与转移数，并记录 `flash_capacity_dropped`（按容量区块的子状态丢弃）、`flash_ended_compactions`/`flash_ended_dropped`（回合结束层压缩次数与被压缩掉的子状态）以及 `flash_yields`/`flash_frame_waits`/`flash_frame_wait_ms`/`flash_max_gc_pause_ms`（pacer 让出与帧恢复）。有进度回调的 Flash 求解结束时另写 `FLASH_PROGRESS_STATS events=<进度数> previews=<预览回放次数> skips=<跳过的预览数> mismatches=<翻译不一致的预览数> preview_ms=<物化总耗时>`；一次求解中第一次翻译不一致另写 `FLASH_PREVIEW_MISMATCH step=<步> detail=<消息> route=<至该步的动作> oldHand=<旧引擎手牌> flashHandIndex=<Flash 下标>`，之后只计数；路线核对失败时写 `FLASH_VERIFY_STEP` 与 `FLASH_VERIFY_BEFORE`（分歧步的动作、计划与核对前状态）；路线变化时（最多每 2 秒）写 `FLASH_PROGRESS candidate=<版本> turn=<回合> ...`，接管检查点写 `FLASH_TAKEOVER kind=<种类> seed=<版本> stop_after_result=<bool>` 与 `FLASH_TAKEOVER_MATERIALIZED kind=<种类> scope=<结果范围> actions=<动作数>`。Shadow 行另有旧后端的同名字段与 `same_route`。

根上的准入拒绝（`FlashImportException`）以结果形式返回；核对不一致（`FlashVerificationException`，包括路线中途旧引擎状态无法投影）丢弃整条 Flash 路线。Flash 搜索中途抛出的 `FlashUnsupportedException`（例如卡牌变量出现内核无法表示的小数偏移）是派生内核的边界。在 On 模式，准入拒绝、核对不一致与内核边界都转换为第 3.2 节的一行 `FLASH_SEARCH_FAILED` 加 `FlashSearchFailedException`，停止搜索与部署；Shadow 只把同样信息写进对照行，仍返回旧结果。子状态级的 `FlashCapacityException` 已在 `FlashBeamSolver` 内按分支丢弃，不产生 `boundary`；只有在展开循环之前（根导入或根评分）抛出的容量溢出是 `kernel-capacity` 显式失败。其他异常视为 Flash 缺陷，向上抛出并中止本次搜索，Shadow 也一样。

`SolvedRouteCache` 的键记录 `FlashRoutes = (FlashMode == On)`；Shadow 返回旧结果，与 Off 共用缓存。

设置项的玩家文案同时维护中文与英文（`ui-localization`）。
