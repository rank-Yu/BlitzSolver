# Flash 搜索后端

Flash 是 CombatSolver 的可选搜索后端。它把整场战斗保存在一块固定大小、不含对象引用的结构体里，分叉只需复制内存，在受支持的战斗上以远低于现有模拟器的开销展开 Beam。

现有模拟引擎（`CombatPredictionSimulator` 及其镜像、`SimulatedCombatState` 与 Prediction 补偿）仍是唯一的语义权威：Flash 的每一步都必须与它逐字节一致，Flash 搜出的路线在部署前由它从同一根完整回放核对。

本目录是设计规格。当前源码职责以 [架构地图](../ARCHITECTURE.md) 为准；本目录只保留设计理由与合同。

| 文档 | 内容 |
| --- | --- |
| [状态布局](state-layout.md) | `FlashBattle` 字段、容量、规范化与指纹 |
| [内核语义](kernel-semantics.md) | 结算管线、Hook 派发顺序、内容定义方式、选择协议、怪物 AI；出牌期瞬态与回手见状态布局 |
| [角色机制](kernel-characters.md) | 各角色专属的内核机制：弃牌与 Sly、充能球、星能与锻造、Osty 与 Doom |
| [药水与遗物](kernel-potions-relics.md) | 额外回合管线、9 瓶药水与新增遗物机制 |
| [准入与校验](admission-and-verification.md) | 按场准入、根导入、差分测试、运行时回放核对 |
| [搜索与接入](search.md) | FlashBeam、路线物化、`BeamSolveDispatch` 与 Off/Shadow/On 模式 |
| [测试记录](testing.md) | 复跑入口、当前证据与未验证项 |

## 1. 目标与非目标

目标：

- 受支持战斗上，一次状态转移（复制、执行、指纹）在微秒量级，搜索热路径零托管分配。
- 与现有引擎严格等价：同一状态执行同一动作，结果投影成 `FlashBattle` 后字节相同。
- 可选且失败可见：开启模式只由 Flash 搜索，不回落现有后端。只有 Flash 搜索自己判定 `flash-potion-policy-no-route` 时返回现有搜索同形的无路线结果（`PotionPolicyUnsatisfiedException`）；精确层下界不可用只关闭剪枝并继续搜索。物化后两侧对同一药水约束判定不一致（`route-*`/`materialize-potion-policy`）以及不支持的内容、根级容量溢出、内核边界、核对不一致、开发策略与其它根阶段都停止本次搜索/部署，写一个稳定的 `FLASH_SEARCH_FAILED` 事件并进入现有问题包流程。搜索展开中单个子状态的容量溢出只丢弃该分支，回合结束层的容量上界通过压缩继续。

非目标：

- 不替代现有模拟器，不改变现有搜索政策的默认行为；默认模式为 Off。
- 不支持第三方 Mod 内容（AGENTS.md 第 1 节）。根中存在第三方 gameplay 订阅者、BaseLib 卡牌修饰或未登记模型时整场不准入。
- 不比现有引擎"更正确"。发现现有引擎与原版不一致时，先按 `combat-semantic-change` 修正现有引擎并补差分证据，Flash 再跟随。

## 2. 总体结构

```
主线程                        后台（每次 Solve 调用）
CombatRootSnapshot ──► BeamSolveDispatch ─┬─ Off / Shadow 的现有侧：CombatBeamSolver.Solve()
                                          └─ On / Shadow 的 Flash 侧：
                                              FlashAdmission      根内容闭包是否全部受支持
                                              FlashRootImporter   旧模拟器根 → FlashBattle + FlashRules
                                              FlashBeamSolver     在 FlashBattle 上搜索，产出候选路线
                                              FlashRouteMaterializer
                                                                  旧引擎回放候选 → PlanAction / SolverResult
                                              On：拒绝 → FLASH_SEARCH_FAILED，无路线 → 同形结果
```

目录与依赖：

- `src/Flash/`：Flash 模块，依赖方向 `Bridge → Search → Kernel/Content`，目录规则见 `src/Flash/AGENTS.md`。
  - `Kernel/`（`CombatSolver.Flash.Kernel`）：状态、RNG、牌堆、出牌与回合流程、伤害/格挡/Power 管线、Hook 派发、选择协议、指纹。只依赖 BCL，不引用 MegaCrit、Godot、HarmonyLib 或 CombatSolver 其他命名空间。
  - `Content/`（`CombatSolver.Flash.Content.*`）：内容定义，按 `Cards/<角色>`、`Powers`、`Relics`、`Potions`、`Monsters`、`Orbs`、`Enchantments`、`Afflictions` 分目录；只依赖 BCL 与内核。文件名只描述内容：角色卡按 `StarterCards`、`AttackCards.<稀有度>`、`SkillCards.<稀有度>`、`PowerCards.<稀有度>`、`GeneratedCards`，Power/遗物/药水/怪物按来源、稀有度、幕或怪物族分文件，不使用批次代号。
  - `Search/`（`CombatSolver.Flash.Search`）：`FlashBeamSolver`、搜索容器，`Scoring/` 为状态评估。
  - `Bridge/`（`CombatSolver.Flash.Bridge`）：准入、规则编译、根导入、动作翻译、路线核对与物化、开启模式失败约定；可以引用 Search、Prediction、Engine 和游戏程序集。
- `src/Search/Probes/`：协调器与续接来源共享的开局/前缀探针与评估；不引用 `CombatBeamSolver`。
- `src/Search/BeamSolveDispatch.cs`：协调器调用 `Solve()` 的唯一分派点。

依赖方向固定为 `Flash 内核 ← FlashBeam ← BeamSolveDispatch ← CombatSearchCoordinator`。内核与 FlashBeam 不反向依赖分派点或协调器；这条边界写入两端结构门禁。

## 3. 关键决定

1. **语义权威是现有引擎，不是原版。** 路线由现有引擎回放核对，Flash 与它不一致就无法部署；它与原版的差异属于现有引擎缺陷，在那里修。
2. **状态是单个 unmanaged 结构体，固定容量。** 溢出抛 `FlashCapacityException`：搜索展开中的子状态溢出只丢弃该分支并计数，回合结束层达到上界时按保留规则压缩；根状态溢出在开启模式下是显式失败（`FLASH_SEARCH_FAILED reason=kernel-capacity`），不回落。不截断、不逐出。
3. **内容是不可变定义对象，数值来自每根编译的规则表。** 卡牌、Power、怪物的数值在根导入时从原版模型读出写入 `FlashRules`；内容代码不硬编码原版数据里已有的数值。
4. **选择用"带选择向量重执行"。** 动作携带本次执行需要的全部选择；效果需要选择而动作未提供时返回"需要选择"，搜索枚举选项后从父状态重新执行。内核没有续接帧。
5. **卡牌在牌堆里按值存放，没有全局引用表。** 相同副本字节相同，去重天然生效；现有引擎按卡牌对象记账的少数值随卡值或小数组移动（[状态布局](state-layout.md) 2.5）；路线按手牌位置记录，物化时在旧引擎的同一位置取牌，生成 `CardStateKey` 与 occurrence。
6. **怪物 AI 编译自原版行动状态机图。** 行动、随机分支、条件分支按旧引擎 `BranchMonsterAi` 的同一方式解释；旧引擎里的特例逐个登记，不做通用猜测。
7. **伤害与格挡修正沿用旧引擎的 `decimal` 计算位置和取整时点。** 修正系数如 1.1 无法用二进制浮点精确表示，不用 `double` 近似。
8. **Flash 只产出候选路线。** `SolverResult` 由旧引擎回放物化，终局取舍复用旧的 `FinalPlanOrdering`，协调器看到的结果与现有后端同形。

## 4. 当前状态

Flash 已接入主搜索分发（Off/Shadow/On），默认 Off。Flash 直接使用玩家所选档位的 Beam 宽度、节点上限与软时间，与常规搜索相同。On 只运行 Flash，不回落现有搜索；药水策略无路线时返回现有搜索同形的无路线结果，其它准入、容量、内核与核对失败写 `FLASH_SEARCH_FAILED` 并停止部署。模式、失败类别与请求参数见 [搜索与接入](search.md)；当前覆盖、差分证据、质量对照与未验证项见 [测试记录](testing.md)。

状态布局、容量与尺寸预算见 [状态布局](state-layout.md)；内容定义与结算顺序见 [内核语义](kernel-semantics.md)；角色专属机制与药水/遗物机制分别见 [角色机制](kernel-characters.md) 与 [药水与遗物](kernel-potions-relics.md)；准入与校验合同见 [准入与校验](admission-and-verification.md)。
