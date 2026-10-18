# Flash 搜索后端

Flash 是 CombatSolver 的可选搜索后端。它把整场战斗保存在一块固定大小、不含对象引用的结构体里，分叉只需复制内存，在受支持的战斗上以远低于现有模拟器的开销展开 Beam。

现有模拟引擎（`CombatPredictionSimulator` 及其镜像、`SimulatedCombatState` 与 Prediction 补偿）仍是唯一的语义权威：Flash 的每一步都必须与它逐字节一致，Flash 搜出的路线在部署前由它从同一根完整回放核对。

本目录是设计规格。代码落地后，当前源码职责以 [架构地图](../ARCHITECTURE.md) 为准；本目录只保留设计理由与合同。

| 文档 | 内容 |
| --- | --- |
| [状态布局](state-layout.md) | `FlashBattle` 字段、容量、规范化与指纹 |
| [内核语义](kernel-semantics.md) | 结算管线、Hook 派发顺序、内容定义方式、选择协议、怪物 AI |
| [准入与校验](admission-and-verification.md) | 按场准入、根导入、差分测试、运行时回放核对 |
| [搜索与接入](search.md) | FlashBeam、路线物化、`BeamSolveDispatch` 与 Off/Shadow/On 模式 |

## 1. 目标与非目标

目标：

- 受支持战斗上，一次状态转移（复制、执行、指纹）在微秒量级，搜索热路径零托管分配。
- 与现有引擎严格等价：同一状态执行同一动作，结果投影成 `FlashBattle` 后字节相同。
- 可选、可回退：不支持的内容、容量溢出、校验失败都显式回落到现有后端，不部分提交。

非目标：

- 不替代现有模拟器，不改变现有搜索政策的默认行为；默认模式为 Off。
- 不支持第三方 Mod 内容（AGENTS.md 第 1 节）。根中存在第三方 gameplay 订阅者、BaseLib 卡牌修饰或未登记模型时整场不准入。
- 不比现有引擎"更正确"。发现现有引擎与原版不一致时，先按 `combat-semantic-change` 修正现有引擎并补差分证据，Flash 再跟随。

## 2. 总体结构

```
主线程                        后台（每次 Solve 调用）
CombatRootSnapshot ──► BeamSolveDispatch ─┬─ Off / 不准入：CombatBeamSolver.Solve()
                                          └─ Flash：
                                              FlashAdmission      根内容闭包是否全部受支持
                                              FlashRootImporter   旧模拟器根 → FlashBattle + FlashRules
                                              FlashBeamSolver     在 FlashBattle 上搜索，产出候选路线
                                              FlashRouteMaterializer
                                                                  旧引擎回放候选 → PlanAction / SolverResult
```

目录与依赖：

- `src/Engine/InCombat/Flash/`：纯内核，命名空间 `CombatSolver.Engine.InCombat.Flash`。只依赖 BCL，不引用 MegaCrit、Godot、HarmonyLib 或 CombatSolver 其他命名空间。
  - `Core/`：状态、RNG、牌堆、出牌与回合流程、伤害/格挡/Power 管线、Hook 派发、选择协议、指纹。
  - `Content/`：内容定义，按 `Cards/<角色>`、`Powers`、`Relics`、`Potions`、`Monsters`、`Orbs`、`Enchantments`、`Afflictions` 分目录。
- `src/Search/FlashBeam/`：Flash 搜索与衔接，可以引用 Search、Prediction、Engine 和游戏程序集。
  - `Bridge/`：准入、规则编译、根导入、动作翻译、路线物化。
  - 根目录：`FlashBeamSolver`、评分与搜索容器。
- `src/Search/BeamSolveDispatch.cs`：协调器调用 `Solve()` 的唯一分派点。

依赖方向固定为 `Flash 内核 ← FlashBeam ← BeamSolveDispatch ← CombatSearchCoordinator`。内核与 FlashBeam 不反向依赖分派点或协调器；这条边界写入两端结构门禁。

## 3. 关键决定

1. **语义权威是现有引擎，不是原版。** 路线由现有引擎回放核对，Flash 与它不一致就无法部署；它与原版的差异属于现有引擎缺陷，在那里修。
2. **状态是单个 unmanaged 结构体，固定容量。** 溢出抛 `FlashCapacityException` 并回落，不截断、不丢弃。
3. **内容是不可变定义对象，数值来自每根编译的规则表。** 卡牌、Power、怪物的数值在根导入时从原版模型读出写入 `FlashRules`；内容代码不硬编码原版数据里已有的数值。
4. **选择用"带选择向量重执行"。** 动作携带本次执行需要的全部选择；效果需要选择而动作未提供时返回"需要选择"，搜索枚举选项后从父状态重新执行。内核没有续接帧。
5. **卡牌在牌堆里按值存放，没有实例身份。** 相同副本字节相同，去重天然生效；路线按手牌位置记录，物化时在旧引擎的同一位置取牌，生成 `CardStateKey` 与 occurrence。
6. **怪物 AI 编译自原版行动状态机图。** 行动、随机分支、条件分支按旧引擎 `BranchMonsterAi` 的同一方式解释；旧引擎里的特例逐个登记，不做通用猜测。
7. **伤害与格挡修正沿用旧引擎的 `decimal` 计算位置和取整时点。** 修正系数如 1.1 无法用二进制浮点精确表示，不用 `double` 近似。
8. **Flash 只产出候选路线。** `SolverResult` 由旧引擎回放物化，终局取舍复用旧的 `FinalPlanOrdering`，协调器看到的结果与现有后端同形。

## 4. 阶段

| 阶段 | 内容 | 完成判据 |
| --- | --- | --- |
| F0 | 规格、规则、skill | 本目录、`.agents/skills/flash-search/`、AGENTS.md 修订合入分支 |
| F1 | 内核核心、根导入、差分探针；纵切内容：铁甲战士初始牌组、燃烧之血、第一幕弱怪 | 纵切 fixture 的一步穷举与多回合随机游走差分全部一致 |
| F2 | 内容扩展：角色牌池、通用 Power、怪物、遗物、药水、球、附魔 | 每项内容附差分证据；覆盖率统计显示准入战斗比例 |
| F3 | FlashBeam、路线物化、`BeamSolveDispatch`、Shadow 模式 | 离线宿主 Shadow 对照零校验失败，记录速度与质量 |
| F4 | On 模式、哨兵验收、PR | 同根、同政策、同预算路线质量不退化，搜索耗时下降 |

F2 的批量内容实现可以交给外部 coding agent，按 [flash-search skill](../../.agents/skills/flash-search/SKILL.md) 的任务卡合同交付并附差分证据。
