# Flash 搜索与接入

[返回 Flash 入口](README.md)

## 1. FlashBeamSolver

FlashBeam 在 `FlashBattle` 上做按回合分层的 Beam 搜索，产出候选路线，不直接产出 `SolverResult`。

- **容器全部预分配**：每层是复用的 `FlashBattle[]`；子状态先复制父状态到空槽，再在槽位上原地执行动作。分数、父指针、动作等元数据放在平行数组。
- **路线不进状态**：每个节点只记父节点下标与本步 `FlashAction`，找到候选后沿父指针倒推。
- **去重**：每层用开放寻址表按指纹分桶，命中后比较完整字节；同一状态保留 Pareto 意义上不被支配的代表，维度沿用旧引擎转置表的六维（药水数、药水战略成本、未来卖血、累计失血、动作数、分数）。
- **评分**：移植旧引擎 `SimulationSnapshot` 的主要分项（预测 HP、累计失血、敌人 HP、胜利奖励、死亡惩罚、风险等，见 `Q/CombatBeamSolver.StateEvaluation.cs:162-505`）与 `BeamRankScore` 的中间项。特征从 `FlashBattle` 直接计算，不构造 `SimulationSnapshot`。
- **选择**：遇到 `NeedsChoice` 时用 `FlashChoiceEnumerator` 按选项规格枚举（选择数量从小到大、同数量按下标字典序），每个请求至多探索 32 个分支（v1）；每个被执行的分支都是精确的，上限只限制探索面。旧引擎按估值排序挑分支（`MaxPile/HandChoiceBranchesPerAction`），v1 尚未移植这套排序。
- **药水**：Bridge 在根上取现有搜索的药水约束（`CapturePotionPolicyForFlash`：每个槽位是否允许显式使用、战略 HP 成本、最早回合、最多使用次数），换算成 Flash 玩家回合后放进 `FlashPotionPolicy`；未设置时不使用药水。FlashBeam 只对仍是根上原药水的槽位生成药水动作，评分减去已用药水的战略成本（按 HP 权重）。v1 尚未移植现有搜索的无药基线、强制使用与保留策略。
- **预算**：节点数、时间、Beam 宽度取自同一 `SolverSearchProfile`；按回合层分配预算的规则与旧引擎 `SolveCore` 相同。
- **产出**：胜利路线与在预算边界停下的路线中，按旧引擎终局比较器的主要键排序后取前 K 条（首版 K = 16）。

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
| `Root`、`DisplayNames`、`BattleDamage`、`Policy`、`CancellationToken`、`Profile` | 支持；FlashBeam 的宽度、节点与时间取自 `Profile` |
| `PotionPolicyOverride` | 支持：回放求解器与物化求解器带同一覆盖，`CapturePotionPolicyForFlash` 据此取槽位约束 |
| `ProgressCallback` | 只在物化阶段报告；FlashBeam 搜索期间不报告进度 |
| `PrimaryIncumbent` | 不参与 Flash 搜索；协调器照常比较各成员结果 |
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

Shadow 的隔离策略清空 `RequestWorkTotals`、`PrimaryIncumbents` 与 `Interaction`，求解器诊断写入空接收器，并且不报告进度，因此返回结果、工作量与日志都与 Off 相同，只多一行对照。

日志行前缀为 `[CombatSolver/Flash]`。Flash 段的 `status` 取 `verified`、`rejected`（带准入拒绝原因）或 `mismatch`（带首个不一致步）；`verified` 段记录耗时、胜负、HP、`hp_lost`、`end_turn`、各阶段耗时以及 FlashBeam 节点与转移数。Shadow 行另有旧后端的同名字段与 `same_route`。

准入拒绝（`FlashImportException`）以结果形式返回；核对不一致（`FlashVerificationException`）丢弃整条 Flash 路线。其他异常视为 Flash 缺陷，向上抛出并中止本次搜索，Shadow 也一样。

`SolvedRouteCache` 的键记录 `FlashRoutes = (FlashMode == On)`；Shadow 返回旧结果，与 Off 共用缓存。

设置项的玩家文案同时维护中文与英文（`ui-localization`）。
