# Flash 搜索与接入

[返回 Flash 入口](README.md)

## 1. FlashBeamSolver

FlashBeam 在 `FlashBattle` 上做按回合分层的 Beam 搜索，产出候选路线，不直接产出 `SolverResult`。

- **容器全部预分配**：每层是复用的 `FlashBattle[]`；子状态先复制父状态到空槽，再在槽位上原地执行动作。分数、父指针、动作等元数据放在平行数组。
- **路线不进状态**：每个节点只记父节点下标与本步 `FlashAction`，找到候选后沿父指针倒推。
- **去重**：每层用开放寻址表按指纹分桶，命中后比较完整字节；同一状态保留 Pareto 意义上不被支配的代表，维度沿用旧引擎转置表的六维（药水数、药水战略成本、未来卖血、累计失血、动作数、分数）。
- **评分**：移植旧引擎 `SimulationSnapshot` 的主要分项（预测 HP、累计失血、敌人 HP、胜利奖励、死亡惩罚、风险等，见 `Q/CombatBeamSolver.StateEvaluation.cs:162-505`）与 `BeamRankScore` 的中间项。特征从 `FlashBattle` 直接计算，不构造 `SimulationSnapshot`。
- **选择**：遇到 `NeedsChoice` 时按选项规格枚举，分支数受 `MaxPile/HandChoiceBranchesPerAction` 约束，与旧引擎一致。
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

`CombatBeamSolver` 构造参数在 Flash 中的处理：

| 参数 | F3 首版 |
| --- | --- |
| `root`、`displayNames`、`battleDamage`、`policy`、`cancellationToken`、`searchProfile` | 支持 |
| `progressCallback` | 支持：每次 `Solve()` 从 0 开始报告节点数与耗时，节流间隔与旧引擎相同 |
| `potionPolicyOverride`、`maximumPotionUses`、`minimumPotionUses`、`earliestPotionTurn` | 支持 |
| `fixedPrefixActions` | 支持：先在旧引擎回放前缀，再投影成 Flash 根 |
| `primaryIncumbent`、`potionFreePolicyBaseline` | 支持：只用于剪枝与终局比较 |
| `earlyTurnScoutDepth`、`earlyTurnScoutObserver`、`planCommitment`、`NoveltySearch` | 不支持，准入拒绝（`UnsupportedRequest`），本次调用回落旧后端 |

`Solve()` 结束时同样向 `policy.RequestWorkTotals` 记录工作量，并正确响应接管请求与 `PotionPolicyUnsatisfiedException` 合同。

## 4. BeamSolveDispatch 与模式

协调器中所有 `new CombatBeamSolver(...).Solve()` 改为经过 `BeamSolveDispatch`，构造参数打包为 `BeamSolveRequest` 记录。`Build*Actions` 等辅助方法仍直接使用 `CombatBeamSolver`。

模式由 `SearchPolicySnapshot.FlashMode` 冻结，主线程从设置读取：

| 模式 | 行为 |
| --- | --- |
| Off（默认） | 只用旧后端 |
| Shadow | 两个后端都跑，部署旧结果；记录 Flash 准入、速度、质量与核对结果 |
| On | 准入则用 Flash 结果，核对失败或不准入时回落旧后端 |

设置项的玩家文案同时维护中文与英文（`ui-localization`）。
