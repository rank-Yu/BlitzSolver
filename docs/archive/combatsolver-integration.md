# CombatSolver 接入方式（历史参考）

这是 CombatSolver 中的接入方式，仅作历史参考。新仓库没有 `BeamSolveDispatch`、`CombatSearchCoordinator`、Off/On 模式、Bridge 或旧引擎回放物化；这些内容不在 Sts2Solver 的运行路径中。

## 2. 路线物化

`FlashRouteMaterializer` 把候选路线交给共享回放核心：

1. 从根分叉旧模拟器，逐步回放 Flash 动作。每一步按手牌位置取牌，按旧引擎的规则生成 `PlanAction`（`CardStateKey`、occurrence、`TargetCombatId`、选择令牌），并要求旧引擎这一步的投影与 Flash 逐字节一致。回放走 `SearchReplayCore`，与现有搜索和开局探针同一实现。
2. 把整条已核对路线作为固定前缀，在共享回放核心上重放成父链（`ApplyFixedPrefix`），再用共享 `SearchRouteMaterializer` 按既有终局排序、标注和物化步骤生成 `SolverResult`。原搜索的最终路线也走同一个物化器。

固定前缀续接在导入前先用同一 `ApplyFixedPrefix` 规则回放请求前缀（`OpeningProbe.ReplayFixedPrefixForFlash` / `SearchReplayCore`），把前缀末尾状态作为 Flash 搜索根；步骤 1 从该状态开始核对，步骤 2 的前缀是“请求前缀 + Flash 路线”，由共享物化器从请求根完整回放。

**回合准备根（`IncludeTurnSetup`）**：玩家回合开始的原生选择页在根捕获时还没有作答，捕获的根是 `Player/Start`。Bridge 不把该 Start 状态导入内核，而是先调用共享 `SearchReplayCore.CaptureTurnSetupRoots`（`ChoiceSearchBudget`/`ChoiceOccurrenceCollector` 的唯一实现），按与现有搜索**完全相同的选择预算**把每个准备选择分支回放到稳定 `Play` 状态，再逐个导入 `FlashBattle`。`FlashBeamSolver.Solve` 接收这组根（现有搜索的 frontier 同样从多个准备根出发），返回选中路线的 `BestRootIndex`；路线本身从 `Play` 状态开始，准备选择不占动作数。物化时把选中的 `PlanCardChoice` 向量作为根节点元数据交给共享 `SearchRouteMaterializer`，由它从准备选择后的稳定 `Play` 根回放固定前缀；结果的 `TurnSetupChoices`/`TurnSetupPlayState` 与现有语义同形。物化后逐项核对选择向量，不一致按 `FlashVerificationException` 整条丢弃。准备根在回合开始的生成效果之前捕获，在这些时机生成卡牌的定义（如 `BigHat`、`VexingPuzzlebox`）必须声明 `GeneratedCardPools`，闭包才含其候选卡。回合准备搜索的 Flash 回调约定与协调器搜索相同：两类准入拒绝写 `FLASH_FALLBACK` 后改用常规搜索，其余失败写 `FLASH_SEARCH_FAILED`、停止部署、关闭全自动并弹出 Flash 提示，原生选择留给玩家作答。

实现在 `src/Flash/Bridge/FlashRouteMaterializer.cs`、`src/Search/Probes/SearchReplayCore.cs` 与 `src/Search/Probes/SearchRouteMaterializer.cs`。

回放中任何一步与 Flash 投影不一致都按 [运行时回放核对](admission-and-verification.md#4-运行时回放核对) 处理。

固定前缀节点按"未比较的回合结果"标注（`CreateUncomparedTurnOutcome`），与协调器续用路线的口径相同；因此 Flash 物化结果的"卖血"统计与完整旧搜索不同，比较路线质量时以战损、胜负与结束回合为准。

## 3. 请求参数

协调器宽度组合的成员（`DirectSearchPurpose.PrimaryBeam` 与 `RefinementBeam`）、Smart 药水梯度的直接审计（`DirectSearchPurpose.SmartPotionGradient`，经同一 `BeamSolveDispatch.Solve` 入口）、协调器直接药水审计点以及带药水约束的固定前缀续接请求经过 Flash。中途排序的 profile 变体按现有 `BeamRankScore` 移植：仅基础分成员只用基础分，权重扰动对非终局状态把指定项乘以系数；连续致命意图定价与上下文排序已移植，不再因这两个 profile 开关拒绝。`DevelopmentStrategy` 的 `Rank` 只读 `StrategyNodeFeatures`，其中每个值都能由 K4 核对过的 Flash 特征给出；但 `Prioritize` 依赖现有 `ActionCandidate`（含每个候选的伤害、格挡、消耗、归一化值与 `MaxCardBranchesPerNode` 截断），`Retain` 依赖 `RankBest` 的 `preserveDefensiveRoute`/`finalQualityFirst` 必保通道，Flash 没有这些结构与模式标志。只搬 `Rank` 会静默丢掉另外两个钩子的排序调整。当前只在关闭模式支持开发策略：开启模式在分发点直接写 `FLASH_SEARCH_FAILED reason=development-strategy` 并停止部署，不猜测、不回落；将来若定义只含 Flash 特征的输入/接口，再由协调者决定放开。

`BeamSolveRequest` 各字段在 Flash 中的处理：

| 字段 | 处理 |
| --- | --- |
| `Root`、`DisplayNames`、`BattleDamage`、`Policy`、`CancellationToken`、`Profile` | 支持；FlashBeam 的宽度、节点上限与软时间直接取自 `Profile`。开启模式下该 profile 的宽度与节点上限已是 Flash 档位值（见 1 节“预算”），关闭模式为常规档位值 |
| `PotionPolicyOverride` | 支持：回放求解器与物化求解器带同一覆盖，`CapturePotionPolicyForFlash` 据此取槽位约束 |
| `ProgressCallback` | 支持：按现有搜索的 100 ms 进度节拍与 200 ms 路线预览节拍报告；预览路线用共享 `SearchReplayCore` 从请求根回放（固定前缀包含在内），路线采纳种子由共享 `SearchRouteMaterializer` 惰性物化 |
| `PrimaryIncumbent` | 支持：作为 FlashBeam 的初始 incumbent，`CombatEndedTurn` 从 `node.Turn` 换算到 Flash 玩家回合；共享的 `PrimaryIncumbents` 表不接入 |
| `Purpose` | 决定是否查看模式，并作为 Flash 搜索与物化求解器的工作量归因 |
| `Policy.IncludeTurnSetup` | 支持：根必须是 `Player/Start`；Bridge 按现有 `BuildTurnSetupRoots` 的选择预算枚举准备根，FlashBeam 从全部准备根搜索，物化时固定选中的选择向量 |

`CombatBeamSolver` 的其余构造参数（`maximumPotionUses`、`minimumPotionUses`、`earliestPotionTurn`、`fixedPrefixActions`、`potionFreePolicyBaseline`、`earlyTurnScout*`、`planCommitment`）在主搜索成员中不使用。Smart 梯度审计通过 `BeamSolveRequest.PotionFreePolicyBaseline`、`MaximumPotionUses`、`MinimumPotionUses` 携带精确层参数；区间上下界、基线、最早用药回合、强制指令与 `RequireAtLeastOne` 均已移植（见 3.1）。固定前缀续接通过第二个分发入口 `BeamSolveDispatch.SolveContinuation` 走同一 `FlashMode`。除不支持的根阶段外，所有续接形态都交给 Flash：任意 `ContinuationPurpose` 上的计划承诺（含收益证据、计划前缀与 `CopyPower` 代表席）、计划阶段自己的任意正深度侦察（空前缀或非空前缀、带不带承诺均可）、带任意药水约束（基线、最早回合、上下界、`RequireAtLeastOne`、强制指令）的续接，以及其它带侦察的续接。分类门与 `can-take-over:*` 原因码不再产生。

- 根阶段不是受支持的两形：普通 `Player/Play`，或 `IncludeTurnSetup` 的 `Player/Start`。其余 side/phase 在开启模式写 `FLASH_SEARCH_FAILED reason=root-phase detail=root-phase:<side>:<phase>` 并停止部署（Flash 内核只表示稳定出牌状态，不能把敌方回合或其它准备阶段当作出牌根搜索）。
- 药水上下界为负或 `MinimumPotionUses > MaximumPotionUses`：按现有求解器 `SolveCore` 的同一参数校验抛出 `ArgumentOutOfRangeException`，不执行搜索，也不写失败事件或遗留日志。

`PolicyOverride` 作为求解政策使用；`PrimaryIncumbent` 按 SB 的口径传入。`ProgressCallbackOverride`（没有时用请求的进度回调）现在同时交给 Flash 搜索，续接搜索按同一节拍发布进度、预览与路线采纳种子。`ResetFixedPrefixSchedulingBaseline` 对 Smart 药水后验请求转交给共享回放核心，使固定前缀后的调度基线与现有搜索相同；其余续接请求仍不读取它。profile 中 Flash 尚未建模的开关仍由准入按 `UnsupportedRequest` 拒绝；开启模式把它转成 `FLASH_SEARCH_FAILED reason=admission-unsupported-request`，不回落。

### 3.1 放开的药水续接与精确层

续接请求的 `MaximumPotionUses`/`MinimumPotionUses`、`PotionFreePolicyBaseline`、`EarliestPotionTurn`、`RequireAtLeastOne`（政策或覆盖）、`Smart` 政策无 `Disabled` 覆盖以及强制用药指令都交给 Flash。带任意一项药水约束、或续接的有效政策为 `Smart`（无 `Disabled` 覆盖）的请求打开 `FlashBeamOptions.AuditPotionPolicy`：Flash 按显式用药次数收集每档最佳终局候选，再用移植的 `FinalPlanOrdering.Select` 过滤与排序：

- **区间**：`ExplicitPotionUseCount >= MinimumPotionUses`，扩展期由 `MaximumUses` 限制用药动作；两者不再要求相等。计数都包含固定前缀。
- **基线**：`PotionFreePolicyBaseline` 作为审计后的无药基线；无覆盖时取本次搜索的最佳无药候选。
- **排序键**：完整移植 `FinalPlanOrdering.Select` 已由 K4 核对的特征键（胜利、存活、死亡保护、偷窃恢复、战略失血、成长信用与收益、结束回合、政策失血、生命资源消耗、长期资源、愤怒复制、政策边界原因、可选药水数、敌人 HP、分数、动作数）。偷窃恢复与政策边界原因键已移植，顺序与常规相同；卖血键不参与比较，原因见上文“产出”。
- **强制指令**：按 `(槽位, 药水身份)` 的集合逐条核对，等价于 `PotionStrategySnapshot.EvaluateForcedUses`；前缀已用的强制药水从固定前缀动作折入。未满足的路线不能通过终局过滤，同次数下不会遮蔽满足的路线。强制使用次数与战略成本从可选用药中扣除后再套用 `RequireAtLeastOne`/`Smart` 资格。
- **最早用药回合**：`EarliestPotionTurn` 按请求根回合换算后限制 Flash 的用药动作；物化后再核对没有更早的用药动作。

物化后的 `FlashPotionBridge.VerifyRoute` 复核动作与显式用药计数、最小/最大次数、最早回合、`RequireAtLeastOne` 与强制指令身份；任一不满足按 `ConstraintFailure` 交给分发点。共享 `SearchRouteMaterializer` 使用的 `FinalPlanOrdering` 在物化阶段拒绝不合规路线时记 `materialize-potion-policy`。FlashBeam 的终局审计已按同一口径过滤（含强制指令、显式次数上下界、Smart/RequireAtLeastOne、Ambergris 与偷窃恢复；其中软策略的偷窃恢复要求确实用过药水，Ambergris 例外按恢复后的 loot 键判断）；只有 Flash 搜索自己判定 `flash-potion-policy-no-route` 才是无路线，`route-*`/`materialize-potion-policy` 是两侧判定不一致，开启模式按 `potion-policy-mismatch` 显式失败。

`SecondRankBand`、`AggressivePowerCommitment`、`BaseScoreOnly` 都由 FlashBeam 执行：次段在全局剪枝取次段席位，力量承诺泳道按上面的席位/寿命移植；分发点不再因这些 profile 开关保留原求解器，也不会静默忽略它们。此前已经是“干净请求”的续接保持原行为。

`SmartOpeningPotionPosterior` 续接也交给 Flash。原先把“非 Smart 政策”和“根无可搜索药水”当作 `potion-audit-boundary:*` 的语义边界已删除：这两种情况下现有求解器执行的就是带同一上下界/基线的普通请求，Flash 执行同一请求；无药可审计时 Flash 与现有求解器同为无路线，后者按既有 `PotionPolicyUnsatisfiedException` 结束。

### 3.2 开启模式的改用常规搜索、无路线与失败处理

开启模式（`FlashMode == On`）先运行 Flash；`FlashMode == Off` 只跑现有求解器，`FLASH_LEGACY` 行与类已删除。

分发点按以下三类处理未采用的结果：

1. **改用常规搜索（整次协调器搜索重跑）**：只有两类准入拒绝进入本路径——`admission-unsupported-content`（卡牌、Power、遗物、药水、球、怪物、附魔、苦难或生成闭包没有 Flash 定义）与 `admission-unrepresentable-state`（第三方订阅者、未登记模型等状态无法投影）。`BeamSolveDispatch` 不自己构造常规求解器，而是抛出 Search 层的 `FlashFallbackRequiredException`（携带 `FlashFallbackNotice`）；`CombatSearchCoordinator.Solve` 只捕获这一个异常类型。捕获后先确认这次 Flash 尝试还没有向调用方发布可采用路线、路线采纳种子或路线预览，再写一行 `FLASH_FALLBACK`，并用 `FlashMode = Off`、`Profile = SearchPolicySnapshot.RegularProfile` 把整次协调器搜索从头跑一遍；新尝试新建自己的请求时钟与预算账本，使用同一个 `cancellationToken`。不停止部署、不关闭全自动、不导出失败问题包。`RegularProfile` 由 `SolverSettings.ApplyFlashPerformance` 在换档时保存（自定义档位两者相同）；开启模式缺少该字段时显式失败，不按 Flash 档位或比例缩放兜底。Runtime 主搜索与回合准备搜索都从同一个 `CombatSearchCoordinator.Solve` 入口进入。

   如果被放弃的 Flash 尝试已经发布了可采用路线、采纳种子或路线预览，则不重跑：按原失败约定写 `FLASH_SEARCH_FAILED reason=<同一类别>` 并抛 `FlashSearchFailedException`，避免两次尝试的结果混在一起。准入只取决于根内容（含生成闭包），实际回落都发生在第一个 Flash 请求、发布任何结果之前；协调器仍保留这条发布检查，防止以后有请求形态在发布后才触发准入。

   每次协调器搜索最多写一行：

```
[CombatSolver/Flash] FLASH_FALLBACK reason=<类别> detail=<第一个不支持的内容或状态> purpose=<用途>
```

   `detail` 取准入异常的原始 detail（内容 ID 或状态描述）。同一个 `SearchDiagnosticsSink` 同时向 Runtime 发布 `FlashFallbackNotice`：每场战斗最多提示一次“本场有 Flash 尚未支持的内容（<游戏内名称>），改用常规搜索”，名称由 Runtime 在 UI 层按内容 ID 取游戏内本地化名、取不到时用内容 ID；提示走现有消息横幅，不打断或阻塞部署。每次搜索的改用另记入问题包的 `FlashFallback` 分类（类别与内容），但不触发上传建议、不新建问题包。普通提示失败不影响已经采用的常规搜索结果。

2. **无路线（接受 Flash 结论）**：只有 `ConstraintFailure=flash-potion-policy-no-route` 时，不搜索、不回落，抛出现有调用方已经处理的 `PotionPolicyUnsatisfiedException`（同一异常类型），并写一行 `FLASH_NO_ROUTE reason=flash-rejected:flash-potion-policy-no-route detail=<原因>`。它不算失败事件。精确层下界输入不可用（`smart-potion-bound unavailable`）只关闭下界剪枝，搜索照常继续，不是无路线。
3. **显式失败（停止部署）**：写一行稳定事件并抛出 `FlashSearchFailedException`：

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
| `admission-unsupported-request` | 固定前缀/准备根无法回放，或其它 Flash 未实现的请求形态 |
| `admission-capacity` / `admission-pending-choice` / `admission-other` | 其余准入类别 |

`admission-unsupported-content` 与 `admission-unrepresentable-state` 不在上表：它们是第 1 类的改用常规搜索，不是失败事件。

Search 只通过注入的 `SearchDiagnosticsSink` 写事件、发布改用记录并抛异常，不引用 Runtime/UI/logger。Runtime 对改用只刷新提示与问题包分类；对显式失败沿用现有失败搜索路径：停止本回合部署、取消部署会话、清理搜索会话与待用基线、关闭全自动、记录问题包分类（`FlashSearchFailed` + 原 `SearchFailure`）、弹出中英文提示并通知无人测试 `Failed`。无人测试结果在 `failureCategory` 字段带同一 `reason` token。

原 `potion-audit-boundary:*` 与 `can-take-over:*` 原因码已删除：这些请求现在由 Flash 执行同一请求，或（非法药水上下界）按参数校验抛出同一异常。仅有的无路线结论仍是药水策略/审计没有可接受路线；仅有的显式拒绝是上表。


固定前缀续接的搜索口径：

1. 现有引擎从请求根按 `ApplyFixedPrefix` 的同一规则回放 `ContinuationSearchRequest.Prefix`，得到前缀末尾的 `SimulationSnapshot`（`OpeningProbe.ReplayFixedPrefixForFlash`）。前缀无法回放按 `UnsupportedRequest` 拒绝；开启模式写 `FLASH_SEARCH_FAILED reason=admission-unsupported-request` 并停止部署，不回落。
2. 前缀末尾状态导入为 FlashBeam 的搜索根，规则表也从该状态编译；评估与下界的根冻结表仍从请求根建立，对应现有求解器的 `_run.Initial*` 来自请求根。
3. FlashBeam 的节点动作数从 `Prefix.Length` 起算，使 `Score`、`RouteKey`、HP 目标早停中的完整胜利判定与下界剪枝看到与现有求解器相同的动作数。incumbent 和药水最早回合仍按请求根回合换算，不因前缀推进。
4. 最终路线 = 前缀 + Flash 路线。Flash 路线每一步照常由现有引擎从搜索根回放并与 Flash 投影逐字节核对；物化时由共享 `SearchRouteMaterializer` 从前缀 + 路线重放父链并构建结果，形状与现有后端的续接结果相同。

每次 Flash 求解向 `policy.RequestWorkTotals` 记录一次工作贡献：`ExpandedNodes`/`TransitionCount` 是 Flash 的实际展开与转移（转移还包含准入、回合准备、前缀、核对与物化的旧引擎回放），耗时、分配与 GC 计数取整次求解的实测值。共享节点预算按同一实际展开数扣减（`BudgetExpandedNodes` 取默认值，即 `ExpandedNodes`），不按宽度倍数折算。实际数字仍写在 `FLASH_ADOPTED` 的 `flash_expanded`/`flash_transitions` 与 `FLASH_PROGRESS_STATS` 中。接管在层循环、出牌深度与每个新颖度父节点前按现有搜索的检查点读取：`AdoptRoute` 停止搜索并物化玩家请求的那个种子，`ApplyCurrentTurn` 只在存在当前回合边界候选时停止并以 `CurrentTurnAdoption` 物化；采用的路线仍逐步由现有引擎回放核对。

### 3.3 Flash 对原搜索类的剩余依赖

Flash 桥接、固定前缀回放、回合准备根与最终物化都已改用 `src/Search/Probes/` 的共享回放核心、`OpeningProbe`、`SearchEvaluator` 和 `SearchRouteMaterializer`；`src/Flash/Search/**` 与 `src/Flash/Bridge/**` 不引用 `CombatBeamSolver`，结构门禁同时检查这一点。

仍构造原求解器的位置只有 Off 模式，以及 On 模式改用常规搜索后的整次协调器重跑：

| 位置 | 依赖 | 归属 |
| --- | --- | --- |
| `BeamSolveDispatch.cs` | Off 跑现有求解器；On 遇到 `admission-unsupported-content` / `admission-unrepresentable-state` 抛 `FlashFallbackRequiredException`，不构造原求解器 | 原搜索代码保留给关闭模式，后续删除原搜索的 PR 再处理 |
| `CombatSearchCoordinator.FlashFallback.cs` | 只捕获 `FlashFallbackRequiredException`，写一次 `FLASH_FALLBACK` 后用 `FlashMode = Off` 与 `RegularProfile` 重新进入 `SolveOnce`；发布过结果时不重跑，改抛 `FlashSearchFailedException` | 重跑点与发布判定留在协调器 |

开启模式没有自己的 `FLASH_LEGACY` 行；改用常规搜索时重跑出来的常规求解器写它原有的日志与结果。

`CombatBeamSolver.Flash.cs` 中的参考入口只服务离线差分探针（测试工具），不参与 Flash 生产路径；`OpeningProbe`、共享回放核心、三个开局续接来源与协调器探针调用点不在此表。

## 4. BeamSolveDispatch 与模式

`src/Search/BeamSolveDispatch.cs` 是唯一分发点。协调器 `RunSearchPass` 的 `SolveMember` 把参数打包为 `BeamSolveRequest` 交给 `BeamSolveDispatch.Solve`；`FrontierContinuationScheduler` 的 `Dispatch`/`DispatchOptional` 把 `ContinuationSearchRequest` 交给 `BeamSolveDispatch.SolveContinuation`。`PrimaryBeam`、`RefinementBeam`、`SmartPotionGradient`、协调器直接审计点 `PotionFreeAudit`、`RequiredPotionAudit`、`NarrowOpeningIncumbent`、`TurnBoundaryDiscovery`、新颖度探索 `NoveltyExploration`、自适应新颖度精炼 `AdaptiveNoveltyRefinement` 与早期回合侦察 `EarlyTurnScout` 都查看 `FlashMode`；Off 直接使用 `CombatBeamSolver`，On 在两类准入拒绝时只抛 `FlashFallbackRequiredException`，由 `CombatSearchCoordinator.Solve` 把整次搜索改为 Off + `RegularProfile` 重跑。回合准备根（`Player/Start` + `IncludeTurnSetup`）由 Flash 接管；其它根阶段在 On 模式是 `root-phase` 显式失败。`SecondRankBand`、`AggressivePowerCommitment` 与 `BaseScoreOnly` 已由 FlashBeam 执行，分发点不再因它们保留原搜索；带药水上下界与 `RequireAtLeastOne` 的计划成员同样交给 Flash。`RunForcedPotionOpeningRescue` 的三个剖面变体都经 `BeamSolveDispatch.Solve`。`NarrowOpeningIncumbent`、`EarlyTurnScout` 与两个新颖度成员也直接使用请求 profile 的宽度、节点上限与时限：窄开局使用协调器构造的 1/16 宽度 pilot，侦察与新颖度成员使用协调器已缩小的 profile。次段成员使用 profile W 作次段边界；多个回合准备根作为同一首层的并列根送入 `Solve(IReadOnlyList<FlashBattle>)`。计划成员与计划阶段的任意正深度侦察同样查看 `FlashMode`。

模式由 `SearchPolicySnapshot.FlashMode` 冻结，主线程从设置 `SolverSettingsData.FlashSearchMode` 读取（设置 > 性能 > 搜索预算 >「Flash 快速搜索（实验）」）。离线宿主可用 `OFFLINE_HARNESS_FLASH_MODE=Off|On` 覆盖。旧设置文件、检查点与问题包中记录的 `Shadow`（字符串或任何数值形式）在读取时归一化为 Off，`On` 的序列化值仍是字符串 `On`。

| 模式 | 行为 |
| --- | --- |
| Off（默认） | 只用旧后端 |
| On | 先跑 Flash；成功记 `FLASH_ADOPTED`；`admission-unsupported-content` / `admission-unrepresentable-state` 记 `FLASH_FALLBACK` 后把同一请求改走常规搜索；药水策略无路线按现有 `PotionPolicyUnsatisfiedException` 形状返回；其它拒绝记 `FLASH_SEARCH_FAILED` 并停止部署 |

请求的初始 `PrimaryIncumbent` 是只读输入，On 按同一请求值传给 FlashBeam；Flash 不写共享 `PrimaryIncumbents` 表，因此收紧只影响本次 Flash 搜索。

日志行前缀为 `[CombatSolver/Flash]`。主搜索成员行写 `purpose=<DirectSearchPurpose>`；续接的采用行写 `purpose=<ContinuationPurpose> prefix=<前缀动作数>`。每次协调器搜索改用常规搜索时写一行 `FLASH_FALLBACK reason=<类别> detail=<第一个不支持的内容或状态> purpose=<用途>`，随后由重跑出来的常规搜索写自己的日志（该次搜索没有 `FLASH_ADOPTED`）。行内 `incumbent=<战略失血差>@<结束回合>` 是请求传入的初始 incumbent（`-` 表示没有）。Flash 段的 `status` 取 `verified`、`rejected`（带准入拒绝原因）、`mismatch`（带首个不一致步）或 `boundary`（Flash 搜索中途到达内核不建模的状态，或根级容量装不下）；`verified` 段记录耗时、胜负、HP、`hp_lost`、`end_turn`、各阶段耗时以及 FlashBeam 节点与转移数，并记录 `flash_capacity_dropped`（按容量区块的子状态丢弃）、`flash_ended_compactions`/`flash_ended_dropped`（回合结束层压缩次数与被压缩掉的子状态）以及 `flash_yields`/`flash_frame_waits`/`flash_frame_wait_ms`/`flash_max_gc_pause_ms`（pacer 让出与帧恢复）。有进度回调的 Flash 求解结束时另写 `FLASH_PROGRESS_STATS events=<进度数> previews=<预览回放次数> skips=<跳过的预览数> mismatches=<翻译不一致的预览数> preview_ms=<物化总耗时>`；一次求解中第一次翻译不一致另写 `FLASH_PREVIEW_MISMATCH step=<步> detail=<消息> route=<至该步的动作> oldHand=<旧引擎手牌> flashHandIndex=<Flash 下标>`，之后只计数；路线核对失败时写 `FLASH_VERIFY_STEP` 与 `FLASH_VERIFY_BEFORE`（分歧步的动作、计划与核对前状态）；路线变化时（最多每 2 秒）写 `FLASH_PROGRESS candidate=<版本> turn=<回合> ...`，接管检查点写 `FLASH_TAKEOVER kind=<种类> seed=<版本> stop_after_result=<bool>` 与 `FLASH_TAKEOVER_MATERIALIZED kind=<种类> scope=<结果范围> actions=<动作数>`。

根上的准入拒绝（`FlashImportException`）以结果形式返回；核对不一致（`FlashVerificationException`，包括路线中途旧引擎状态无法投影）丢弃整条 Flash 路线。Flash 搜索中途抛出的 `FlashUnsupportedException`（例如卡牌变量出现内核无法表示的小数偏移）是派生内核的边界。在 On 模式，只有 `admission-unsupported-content` 与 `admission-unrepresentable-state` 抛 `FlashFallbackRequiredException`，由第 3.2 节的协调器重跑用常规档位从头搜索；其余准入拒绝、核对不一致与内核边界都转换为第 3.2 节的一行 `FLASH_SEARCH_FAILED` 加 `FlashSearchFailedException`，停止搜索与部署。子状态级的 `FlashCapacityException` 已在 `FlashBeamSolver` 内按分支丢弃，不产生 `boundary`；只有在展开循环之前（根导入或根评分）抛出的容量溢出是 `kernel-capacity` 显式失败。其他异常视为 Flash 缺陷，向上抛出并中止本次搜索。

`SolvedRouteCache` 的键记录 `FlashRoutes = (FlashMode == On)`；Off 共用不含 Flash 路线的缓存。On 模式改用常规搜索得到的结果按请求政策计入 On 缓存，但结果本身没有 Flash 工作字段。

设置项的玩家文案同时维护中文与英文（`ui-localization`）。
