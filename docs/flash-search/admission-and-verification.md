# Flash 准入与校验

[返回 Flash 入口](README.md)

Flash 的正确性由三道关口保证：搜索前的**准入**、开发期的**差分测试**、部署前的**旧引擎回放核对**。任何一关失败都回落到现有后端，不部分提交。

## 1. 准入

准入在每次 `Solve()` 分派时执行，输入是根快照分叉出的旧模拟器。结果是"准入"或带稳定原因码的"拒绝"。

拒绝条件（任一成立即拒绝）：

| 原因码 | 条件 |
| --- | --- |
| `MultiPlayer` | 玩家数不是 1 |
| `RootPhase` | 根不在玩家出牌阶段（F1–F3 不支持 `IncludeTurnSetup` 的回合准备根） |
| `PendingChoice` | 根存在待处理选择或执行续接 |
| `ThirdParty` | 存在第三方 gameplay 订阅者、BaseLib 卡牌修饰、未登记模型状态 |
| `UnsupportedCard` 等 | 任一卡牌、附魔、苦难、Power、遗物、药水、球、怪物没有 Flash 定义 |
| `UnsupportedClosure` | 可达内容闭包中有不受支持的项（见下） |
| `UnrepresentableState` | 旧模拟器里存在 Flash 无法表示的状态（未建模的 StateStore 条目、非空的未支持侧表等） |
| `Capacity` | 估计的牌数、Power 数、敌人数超出容量 |
| `UnsupportedRequest` | 本次 `Solve()` 参数含 Flash 尚未支持的选项（见 [搜索与接入](search.md#3-请求参数)） |

**可达内容闭包**：根里的内容加上它们可能生成的内容，递归求闭包。每个定义声明自己可能生成什么（固定列表或"某角色某稀有度的卡池"这类池引用）；池在根导入时按原版数据展开。怪物的召唤表（`FlashMonsterDef.SpawnedMonsterIds`）、给玩家塞入的状态牌也在闭包内。闭包里任何一项没有定义就拒绝，避免搜索中途遇到未知内容。

怪物的可生成类型在 `FlashRulesCompiler` 中编译：每个类型在根模拟器的私有 Fork 上执行现有引擎的创建路径（`MonsterSpawnSupport.Create` + `AddPredictedMonster` + `PreparePredictedMonster`），状态机、静态值与初始私有整数都从这次创建结果读取，不手抄。规则表容量是 `FlashLimits.MonsterRules`，超出按 `Capacity` 拒绝。根持有 `Murderous` 修饰而闭包非空时拒绝（`UnsupportedClosure`，新敌人的入场力量未移植）；贤者之石、毛皮大衣没有 Flash 遗物定义，持有它们的根在导入阶段按 `UnsupportedRelic` 拒绝。

准入只做判断，不修改任何状态。拒绝原因写入诊断，用于覆盖率统计。

## 2. 根导入与投影

`FlashStateImporter` 把旧模拟器的状态投影成 `FlashBattle`，同时编译 `FlashRules`。同一个投影函数用于两处：

- 运行时：投影搜索根。
- 测试时：投影旧引擎每一步的结果，与 Flash 的结果比较。

投影必须完整：旧模拟器里每一项会影响后续结算的值都要么映射到 `FlashBattle`，要么在准入中被拒绝。读取旧引擎内部状态（包括原版克隆模型的私有字段）只发生在 Bridge 中。

选择不需要投影：`FlashChoiceTranslator` 用旧引擎逐次回放带选择的动作，差分探针同时比较每个选择请求的效果、来源、时机、数量与同序选项（卡牌 Id 与升级），K1 对每个动作展开全部选择组合（每个动作至多 64 个），K2 随机挑合法选择。

模型私有状态（`FlashStateProjections`）：`PredictionStateStore` 的每个条目都必须被某个投影认领（按 Power/遗物写入 `FlashPower.Aux*` 或遗物计数，值取条目，没有条目时取条目构造器会读取的模型数据），或被证明无效；其余条目一律拒绝。无效条目只有两类：等于当前 Power 数量的 `PowerAmountPredictionState`（同步时只会被删除）；敌方生物离开战斗后其 Power 的任何条目（现有引擎只经由仍在战斗中的生物身上的 Power 读取条目，离场的敌人不会回来）。新增投影与对应 Flash 定义一起提交，差分探针逐步比较投影值。

怪物私有整数（`_monsterIntStates`）走同一条认领规则：`FlashStateProjections.ProjectMonster` 按已知敌人的 `FlashMonsterDef.AiRegisterNames` 把值写入 `FlashEnemy.Ai`，并按 `InertMonsterIntNames` 认领只被状态指纹/续接文本读取的条目（目前只有乐加维林族母的 `_isAwake`）。寄存器取值调用现有 `GetMonsterInt`，即优先捕获条目、没有条目时读模型字段，所以惰性条目与缺失条目的投影相同。其余条目一律按 `UnrepresentableState` 拒绝；`DescribeFlashUnrepresentableState` 不再整体拒绝 `_monsterIntStates`。

投影只读不写：旧引擎的本回合计数读取器首次读取时会从根历史填充缓存，因此 `FlashStateImporter` 先 Fork 一份私有模拟器再读取，调用方持有的状态（可能正被其他工作线程 Fork）保持不变。

## 3. 差分测试

**等价定义**：对任意准入状态 `s` 和合法动作 `a`，

```
Project(Old.Step(s, a)) == Flash.Step(Project(s), a)     （逐字节）
```

这里 `Old.Step` 走现有搜索使用的同一条动作管线（`CombatBeamSolver.Replay` 的出牌、药水、结束回合路径）。

测试层次（对应 AGENTS.md 第 8 节）：

| 层 | 内容 | 位置 |
| --- | --- | --- |
| K0 纯内核 | RNG 对照录制向量、布局尺寸断言、规范化、容量失败、选择协议 | `tools/testing/checks/FlashKernelChecks/`，源码包含内核文件，不依赖游戏 |
| K1 单步穷举 | 从根出发，枚举全部合法动作（含选择分支）各执行一步并比较 | 离线搜索宿主的差分探针（`OFFLINE_HARNESS_FLASH_DIFF=1`，`tools/search/OfflineSearchHarness/FlashDifferentialProbe.cs`） |
| K2 随机游走 | 固定种子，多回合随机合法动作序列，每步比较；覆盖洗牌、敌人回合、死亡 | 同上 |
| K3 内容定向 | 每个新内容至少一个 fixture，让该内容在前几步内被触发 | `coverage/fixtures/` 下按主题存放，K1/K2 消费 |
| K4 评估一致 | K1/K2 中投影一致的每个状态，`FlashScorer` 的评估值必须等于现有引擎同一状态的 `SimulationSnapshot` 对应成员；`Score` 按 double 精确比较 | 同一探针；字段表与比较在 `FlashScoreFeatures`、`FlashScoreProjection`。现有引擎停在搜索边界的状态（`BoundaryReason` 非 None）不比较 |
| K5 下界一致 | K1/K2 中投影一致的每个状态，`FlashBoundFeatures` 的 `FutureHealPotential`、`HasRisk`、`RemainingHealingPotential`、`FutureHealUsed`、`CumulativePlayerHpLost`、`RecoveredPlayerHp`、`PlayerHp`、`PlayerMaxHp`、`DeathSaveHpRestored`、`ExplicitPotionStrategicCost` 与 `StrategicHpLowerBound` 必须等于现有主搜索成员同一状态的取值。下界无法精确移植的根整体禁用剪枝，汇总行写 `bound=disabled(<原因>)` | 同一探针；比较在 `FlashBoundFeatures.Diff`。现有侧只读访问器在 `CombatBeamSolver.Flash.cs`（`PrimaryIncumbentRemainingHealingPotential`、`PrimaryIncumbentFutureHealPotential`、`PrimaryIncumbentHpLowerBound`） |

复跑命令与当前证据见 [测试记录](testing.md)。

比较失败时输出第一处差异的字段路径（例如 `Enemies[1].Powers[VULNERABLE].Amount: old=2 flash=3`）、动作序列与种子，便于直接复现。

K4 让 FlashBeam 的排序依据与现有搜索相同：同一状态得到同一分数，路线质量的差别只来自搜索结构本身。K5 让 incumbent 下界剪枝的输入与现有主搜索成员相同：同一状态得到同一 `StrategicHpLowerBound` 与 `costClosed`/`pruneEqualHp` 依据；汇总行的 `bound=checked boundChecked=N boundFailed=0` 表示已比较，`bound=disabled(<原因>)` 表示该根显式关闭剪枝。

差分测试只证明"与现有引擎一致"。现有引擎与原版是否一致由既有的 actual/simulated 严格差分保证，不在这里重复。

## 4. 运行时回放核对

Flash 选出的路线在采用前由旧引擎从同一根逐步回放（`FlashRouteMaterializer`，见 [搜索与接入](search.md#2-路线物化)）：每一步旧引擎状态的投影必须与 Flash 状态逐字节一致。

- 任何一步投影不一致、选择对不上，或旧引擎到达 Flash 无法表示的状态（导入拒绝），都抛 `FlashVerificationException`，消息以 `FLASH_VERIFY_MISMATCH step=` 开头并带首个分歧步序。整条 Flash 路线被丢弃：On 模式回落旧后端，Shadow 模式记为 `status=mismatch`。
- 核对通过的路线作为固定前缀交给旧求解器物化，结果形状与旧后端相同。

## 5. 失败处理约束

- 不新增 `catch (Exception)`。只捕获 `FlashUnsupportedException`、`FlashCapacityException` 与校验失败这三类已定义的业务失败，以及取消。
- 捕获后整个 Flash 求解作废并回落，不把半成品路线交给协调器。
- 回落本身计入诊断：原因、阶段、根摘要。
