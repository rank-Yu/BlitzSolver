# Flash 搜索与接入

[返回 Flash 入口](README.md)

## 1. FlashBeamSolver

FlashBeam 在 `FlashBattle` 上做按回合分层的 Beam 搜索，产出候选路线，不直接产出 `SolverResult`。

- **容器复用**：每层是跨层复用的 `FlashBattle[]`；子状态先复制父状态到子状态层的首个空槽（`Pool.Reserve`），在槽位上原地执行动作，按需提交（去重）、复制到回合结束层或作为终局丢弃；求解器与评估类型都跳过局部变量清零（`SkipLocalsInit`）。分数、父指针、动作等元数据放在平行数组。保留层与回合前沿固定为 Beam 宽度；子状态层与回合结束层从宽度 × 8 起按需倍增，上限为每个状态展开全部合法动作的最坏情况（宽度 × `MaxLegalActions + 1`），超过上限按容量拒绝。
- **路线不进状态**：每个节点只记父节点下标与本步 `FlashAction`，找到候选后沿父指针倒推。
- **去重**：每层用开放寻址表按指纹分桶，命中后比较完整字节；同一状态只保留排序值最高的代表。旧引擎转置表的六维 Pareto 保留（药水数、药水战略成本、未来卖血、累计失血、动作数、分数）尚未移植。
- **层内保留**：按排序值取前 Beam 宽度个状态，其中 `OffensiveShare`（默认 25%）的席位留给敌人 HP 最低的状态。旧引擎的多样性泳道、选择保路与药水配额尚未移植。
- **评分**：由 `FlashScoreBridge.Create` 在根上编译逐内容数值表（卡牌估值与动态变量增量、计算伤害乘数、潜在布置值与类别、Power 的战略效果分类/参数、药水战略成本），冻结评估读取的 `Initial*` 与 policy 值，返回 `FlashScorer`。`FlashScorer.Features(in FlashBattle, actionCount)` 逐项移植旧引擎 `CombatBeamSolver.Snapshot`（`src/Search/CombatBeamSolver.StateEvaluation.cs`）：同样的项、同样的运算顺序与 `checked`/取整位置，特征直接从 `FlashBattle` 计算，不构造 `SimulationSnapshot`。节点分数与终局比较用 `Features(...).Score`；层内保留排序用 `FlashScorer.BeamRankScore`（旧引擎 `BeamRetentionPolicy.BeamRankScore`）。K4 在同一状态逐字段核对每个特征，`Score` 按 double 逐位比较。
- **评分表**：每张已准入的卡牌按（内容 Id, 升级等级）有明确条目；每个 Power 有战略效果分类与参数。旧引擎判定不贡献的项显式记为 0（例如非玩家侧 Power、临时 Power、`StrategicEffectMirrors.Contributes` 为 false 的项）。未移植的逐内容乘数或请求设置在根上以 `FlashRejection.UnsupportedCard` / `UnsupportedRequest` 拒绝，不默认记 0。
- **选择**：遇到 `NeedsChoice` 时用 `FlashChoiceEnumerator` 按选项规格枚举（选择数量从小到大、同数量按下标字典序），每个请求至多探索 32 个分支（v1）；每个被执行的分支都是精确的，上限只限制探索面。旧引擎按估值排序挑分支（`MaxPile/HandChoiceBranchesPerAction`），v1 尚未移植这套排序。
- **药水**：Bridge 在根上取现有搜索的药水约束（`CapturePotionPolicyForFlash`：每个槽位是否允许显式使用、战略 HP 成本、最早回合、最多使用次数），换算成 Flash 玩家回合后放进 `FlashPotionPolicy`；未设置时不使用药水。FlashBeam 只对仍是根上原药水的槽位生成药水动作。旧引擎的 `Score` 不含药水成本，因此 FlashBeam 在**排序值**上额外减去已用药水的战略成本（`SolverWeights.Hp` 计价）；这是 Flash 特有的项，节点分数与终局比较仍用 `Features(...).Score`。v1 尚未移植现有搜索的无药基线、强制使用与保留策略。
- **早停**：照抄现有搜索的两种早停：HP 目标（请求可在 HP 目标处停止、且不要求显式药水时，一次无死亡豁免、显式药水不超过额度、本场已失血加路线失血不超过可接受值的完整胜利，在下一个父节点之前结束搜索）与零伤害（胜利且未失血、未用显式药水、满血且最大生命未减少时，当前回合结束后停止）。
- **incumbent 下界剪枝**：移植现有主搜索成员的局部 HP 下界（`CombatBeamSolver.Retention.cs` 的 `CanUseLocalPrimaryHpBound`、`IsLocalPrimaryHpBucket`、`ApplyPrimaryIncumbentBound`/`ApplyPrimaryIncumbentBoundCore`、`ShouldPruneByPrimaryIncumbent`、`TryTightenPrimarySearchIncumbent`、`TightenPrimarySearchIncumbentAtTurnLayer`）。`FlashBoundFeatures` 在根上冻结 `FutureHealPotential`、`HasRisk`、剩余治疗额度（Component/KnownNative 证书）、战略首领减伤与初始 incumbent；求值只读 `FlashBattle` 和冻结表。终局完整胜利在提交时收紧 incumbent（与现有搜索的同一层 Prune 对齐）；现有搜索的回合结束收紧作用于同一批已提交的 `completed` 终局，Flash 在提交时已经收紧，因此不重复。每个动作层保留之后与回合边界保留之后各剪一次；被剪掉的席位不补。参数沿用现有语义：`allowTurnTieBound` 由 `costClosed` 门控，`pruneEqualHp` 只对非终局、无风险且药水成本闭合的节点生效，`costClosed` 读 `ExplicitPotionStrategicCost`。某个下界输入在 Flash 上无法精确给出时该根整体禁用剪枝，汇总行写 `bound=disabled(<原因>)`，不使用近似值。共享的 `PrimaryIncumbents` 表不接入，避免对照模式的隔离泄漏；初始 `PrimaryIncumbent` 由请求传入并按 `FlashBattle.PlayerTurn` 相对根的偏移换算。
- **预算**：取同一 `SolverSearchProfile`：Beam 宽度与全局节点上限乘以 `FlashRouteMaterializer.ProfileWidthScale`（4），软时间不变。FlashBeam 只有排序泳道与进攻泳道，需要更宽的 Beam 才能达到现有搜索的路线质量（[测试记录](testing.md) 2.5）。旧引擎按回合层分配节点预算的规则尚未移植。
- **产出**：按旧引擎终局比较器（`FinalPlanOrdering.Select` 的排序键）选出一条路线：完整胜利、存活、死亡保护次数、战略失血（`ActEndingBossPolicy.StrategicHpDeficit`，含战后遗物回复）、结束回合、生命资源消耗、长期资源、愤怒复制数、可选药水数、敌人 HP、分数、动作数。没有胜利路线时，预算耗尽时的回合起点状态与终局状态一起参与比较。成长与遗物计数额度为 0（相关设置在准入时拒绝）；偷窃、卖血与边界原因这几个键尚未移植。

## 2. 路线物化

`FlashRouteMaterializer` 把候选路线交给旧引擎：

1. 从根分叉旧模拟器，逐步回放 Flash 动作。每一步按手牌位置取牌，按旧引擎的规则生成 `PlanAction`（`CardStateKey`、occurrence、`TargetCombatId`、选择令牌），并要求旧引擎这一步的投影与 Flash 逐字节一致。
2. 把整条已核对路线作为 `fixedPrefixActions` 交给现有 `CombatBeamSolver`，用极小预算求解。现有求解器自己回放并校验前缀（`ApplyFixedPrefix`），按既有终局排序与物化逻辑生成 `SolverResult`。协调器的续用补搜已经使用同一入口，所以不需要提取 `MaterializeSelectedRoute`。

实现在 `src/Search/FlashBeam/Bridge/FlashRouteMaterializer.cs`。

回放中任何一步与 Flash 投影不一致都按 [运行时回放核对](admission-and-verification.md#4-运行时回放核对) 处理。

固定前缀节点按"未比较的回合结果"标注（`CreateUncomparedTurnOutcome`），与协调器续用路线的口径相同；因此 Flash 物化结果的"卖血"统计与完整旧搜索不同，比较路线质量时以战损、胜负与结束回合为准。

## 3. 请求参数

当前只有协调器主搜索成员（`DirectSearchPurpose.PrimaryBeam`）经过 Flash。`BeamSolveRequest` 各字段在 Flash 中的处理：

| 字段 | 处理 |
| --- | --- |
| `Root`、`DisplayNames`、`BattleDamage`、`Policy`、`CancellationToken`、`Profile` | 支持；FlashBeam 的宽度与节点为 `Profile` 的 4 倍，时间取自 `Profile` |
| `PotionPolicyOverride` | 支持：回放求解器与物化求解器带同一覆盖，`CapturePotionPolicyForFlash` 据此取槽位约束 |
| `ProgressCallback` | 只在物化阶段报告；FlashBeam 搜索期间不报告进度 |
| `PrimaryIncumbent` | 支持：作为 FlashBeam 的初始 incumbent，`CombatEndedTurn` 从 `node.Turn` 换算到 Flash 玩家回合；共享的 `PrimaryIncumbents` 表不接入 |
| `Purpose` | 决定是否查看模式，并作为物化求解器的工作量归因 |

`CombatBeamSolver` 的其余构造参数（`maximumPotionUses`、`minimumPotionUses`、`earliestPotionTurn`、`fixedPrefixActions`、`potionFreePolicyBaseline`、`earlyTurnScout*`、`planCommitment`）在主搜索成员中不使用；用到它们的调用点不经过分发，仍用旧后端。

回放与物化求解器照常向 `policy.RequestWorkTotals` 记录工作量，FlashBeam 自身的节点与转移不计入。接管请求只在物化求解器中响应，FlashBeam 搜索期间不响应。

## 4. BeamSolveDispatch 与模式

`src/Search/BeamSolveDispatch.cs` 是唯一分发点。协调器 `RunSearchPass` 的 `SolveMember` 把参数打包为 `BeamSolveRequest` 交给 `BeamSolveDispatch.Solve`。只有 `PrimaryBeam` 用途查看模式；精炼成员、补充审计、计划搜索与侦察仍直接使用 `CombatBeamSolver`，因为它们的 profile 开关在 Flash 中没有对应实现。

模式由 `SearchPolicySnapshot.FlashMode` 冻结，主线程从设置 `SolverSettingsData.FlashSearchMode` 读取（设置 > 性能 > 搜索预算 >「Flash 快速搜索（实验）」）。离线宿主可用 `OFFLINE_HARNESS_FLASH_MODE=Off|Shadow|On` 覆盖。

| 模式 | 行为 |
| --- | --- |
| Off（默认） | 只用旧后端 |
| Shadow | 先跑旧后端，再在隔离策略下跑 Flash，返回旧结果并记录 `FLASH_SHADOW` |
| On | 先跑 Flash；已准入且逐步核对通过则采用并记录 `FLASH_ADOPTED`，否则记录 `FLASH_FALLBACK` 并改跑旧后端 |

Shadow 的隔离策略清空 `RequestWorkTotals`、`PrimaryIncumbents` 与 `Interaction`，求解器诊断写入空接收器，并且不报告进度，因此返回结果、工作量与日志都与 Off 相同，只多一行对照。请求的初始 `PrimaryIncumbent` 是只读输入，Shadow/On 都按同一请求值传给 FlashBeam；两边都不写共享 `PrimaryIncumbents` 表，因此 Flash 的收紧只影响本次 Flash 搜索。

日志行前缀为 `[CombatSolver/Flash]`。Flash 段的 `status` 取 `verified`、`rejected`（带准入拒绝原因）或 `mismatch`（带首个不一致步）；`verified` 段记录耗时、胜负、HP、`hp_lost`、`end_turn`、各阶段耗时以及 FlashBeam 节点与转移数。Shadow 行另有旧后端的同名字段与 `same_route`。

根上的准入拒绝（`FlashImportException`）以结果形式返回；核对不一致（`FlashVerificationException`，包括路线中途旧引擎状态无法投影）丢弃整条 Flash 路线。其他异常视为 Flash 缺陷，向上抛出并中止本次搜索，Shadow 也一样。

`SolvedRouteCache` 的键记录 `FlashRoutes = (FlashMode == On)`；Shadow 返回旧结果，与 Off 共用缓存。

设置项的玩家文案同时维护中文与英文（`ui-localization`）。
