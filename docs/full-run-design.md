# 整局自动游玩：架构设计

[返回文档索引](README.md)

本文是 BlitzSolver 从"整场战斗求解"扩展到"整局自动游玩"的目标架构。它只记录长期方向、边界与分工；每个阶段开工时另写任务卡，进度与数字记在各批次报告里，不写进本文。

## 1. 目标与范围

**产品目标：** 在原版《杀戮尖塔 2》单人模式中，能把一整局游戏交给 Mod，包括地图、先古之民、事件、战斗、奖励、商店、休息处、宝箱和幕切换。最终支持所有角色、所有进阶等级、所有幕与变体。

三种模式都保留，玩家自选：

| 模式 | 行为 |
| --- | --- |
| 只托管战斗 | 现有的 `F8` 全自动战斗；局外由玩家决定 |
| 只给推荐 | 局外每个决策界面显示推荐及理由，由玩家决定；战斗可以单独托管 |
| 全自动 | 整局交给 Mod |

**工具目标：** 离线宿主运行原版整局，战斗交给 Blitz，用来评估局外策略。两个宿主（Mod 与离线）都由原版 `sts2.dll` 推进局外；Blitz 不再自己实现局外模拟器。

**不在范围内：**

- 多人模式；
- 第三方 Mod 内容：遇到时显式拒绝；
- 局外决策预知未来，见第 7 节；
- 自己维护第二套局外推进与逐步写回。

**发布门槛：** 尚未确定，以后按实测基线再定。

## 2. 沿用的设计原则

战斗内核的原则同样适用于局外：

- **定长结构体：** `RunState` 是不含对象引用的定长结构体，分叉就是复制内存。容量是编译期常量，溢出显式失败。
- **确定性：** 同一状态只有一种字节表示；同一种子、同一输入、同一策略，结果逐字节相同。
- **显式失败：** 未知内容抛 `UnsupportedException`，容量不足抛 `CapacityException`。不近似、不跳过、不返回默认值。
- **原版是唯一的事实来源：** 局外状态与决策请求都从原版读出；Blitz 只保留开战选择预测。
- **只有一份实现：** Mod 与离线宿主共用战斗托管与决策逻辑，离线测到的就是游戏里跑的。
- **热路径零托管分配**，只要它会进入推演或评估的内循环。

## 3. 随机数分工

原版有 15 条按用途命名的流，每条都由"种子 + 名字"单独派生，互不影响。另外还有每个事件和每幕地图各自的随机数。

| 归属 | 流 | 说明 |
| --- | --- | --- |
| `Battle`（已有） | CombatCardGeneration、CombatPotionGeneration、CombatCardSelection、CombatEnergyCosts、CombatTargets、CombatOrbs、MonsterAi | 只在局内使用，状态跨战斗延续 |
| `Battle`（已有），并跨边界传递 | Shuffle、Niche | Shuffle 主要在局内用，个别局外内容也用；Niche 局内局外都用：怪物 HP、十几件遗物拿到时的效果 |
| 原版局外（R3 起） | UpFront、UnknownMapPoint、TreasureRoomRelics、Rewards、Shops、Transformations、每个事件的随机数、每幕地图的 `act_N_map` | 由原版 `RunManager` 推进；Blitz 只从导入的 `RunState` 读当前状态 |

`RunState` 始终保存全部 15 条流与事件/地图随机数的当前状态，用于导入、编码和状态比较；Blitz 不在局外抽取它们。Niche 和 Shuffle 两边都会消耗，战斗边界必须原样传递，不能重新派生。

## 4. 分层与目录

Core 按两个维度交叉组织：**模拟 / 决策 / 托管** × **战斗 / 整局**。"整局"（Run）沿用原版的叫法：原版一整局是 `RunState`、`RunManager`，一场战斗是 `CombatState`。

目标目录树：

```
src/
├─ Core/ (Simulation/{Combat,Run}, Content/, Ai/{Combat,Run}, Autoplay/)
├─ Import/ (Combat/, Run/, Run/Decisions/ 由 R3b 新增)
├─ OfflineHost/  局外执行库（Run/、Runtime/必要回调；完整Runtime/Combat待迁移）
├─ Mod/ (Combat/, Run/ 由 R3c 落地, UI/, Testing/, Patches/)
└─ TestHost/ (Combat/, Run/)
tests/ (Differential/{Combat,Run}, KernelChecks/, Golden/, Architecture/, corpora/{quality,fixtures,runs})
tools/ (testing/, run-eval/ README)
```

`src/Core` 的职责：

| 目录 | 职责 |
| --- | --- |
| `Simulation/Combat` | `Battle`、回合与出牌管线、结算、战斗随机数、`Rules` |
| `Simulation/Run` | 局外状态 `RunState`、编码与投影、决策请求类型、内容表、`RulesBuilder`、开战选择预测 `RunCombatSetup` |
| `Content` | 卡牌、Power、遗物、药水、球、附魔、怪物与遭遇定义；不再规划 `Events/`、`Ancients/`、`Acts/`、`Pools/` |
| `Ai/Combat` | `BeamSolver`、评分、翻倍重搜 |
| `Ai/Run` | 局外决策：只读 `RunView` 与决策请求 |
| `Autoplay` | `CombatPilot`、`RunPilot`、宿主接口 |

命名空间与目录一致：`BlitzSolver.Simulation.Combat`、`BlitzSolver.Simulation.Run`、`BlitzSolver.Content.*`、`BlitzSolver.Ai.Combat`、`BlitzSolver.Ai.Run`、`BlitzSolver.Autoplay`。

Core 依赖规则，由 `tests/Architecture` 在编译后自动检查：

| 层 | 可以引用 |
| --- | --- |
| `Simulation/Combat` | 不引用其他层 |
| `Simulation/Run` | `Simulation/Combat` |
| `Content` | `Simulation/Combat`、`Simulation/Run` |
| `Ai/Combat` | `Simulation/Combat` |
| `Ai/Run` | 只能引用 `Simulation/Run` 里登记为可见投影 / 决策请求的类型，不能引用局外状态本体或随机数类型 |
| `Autoplay` | `Simulation.*`、`Ai.*` |

- `Ai/Run` 需要推演评估时，通过 `Autoplay` 注入的接口调用，不直接引用 `Simulation/Run` 的状态。
- 不拆程序集：内容定义要大量调用模拟的内部函数，战斗热路径也不宜跨程序集。依赖方向由 `tests/Architecture` 读取编译后的 `BlitzSolver.Core` 检查；`Ai/Run` 的未登记引用用本地反例验证会报错。

Core 以外的项目与引用：

| 项目 | 引用 | 说明 |
| --- | --- | --- |
| `src/Import` | Core、`sts2.dll` | 唯一读取原版模型与私有字段的层；战斗导入在 `Combat/`，整局导入在 `Run/`，决策请求导入在 `Run/Decisions/` |
| `src/OfflineHost` | Core、Import、`sts2.dll`、`GodotSharp`、`0Harmony` | 离线宿主；不引用 `TestHost` |
| `src/Mod` | Import、游戏程序集、Godot/Harmony、RitsuLib | 不引用 `OfflineHost`；子目录 `Combat/`、`Run/`（R3c）、`UI/`、`Testing/`、`Patches/` |
| `src/TestHost` | Core、Import、游戏程序集 | 测试用建局与注入；子目录 `Combat/`、`Run/` |
| `tests/Differential` | OfflineHost、TestHost | 进程内差分；子目录 `Combat/`、`Run/` |
| `tools/run-eval` | OfflineHost | 整局评估器 |
| `tests/KernelChecks` | Core | 战斗合同在 `Combat/`，整局合同在 `Run/` |
| `tests/Golden` | Core | 战斗校验器与冻结数据 |
| `tests/Architecture` | 编译后的 Core | 层依赖检查 |

`OfflineHost`、`Mod`、`tools/run-eval` 都不加入 `BlitzSolver.sln`。`tests/Differential` 也不加入；`Import` 与 `Mod` 的依赖规则由 `tests/Architecture` 检查。

## 5. 局外状态 `RunState`

定长结构体，内容包括：

- 角色、进阶等级、幕与变体、楼层、当前房间与阶段；
- HP、最大生命、金币；
- 牌组：卡牌 ID、升级、附魔，以及跨战斗保留的卡牌状态；
- 遗物及其计数器，药水槽；
- 当前幕的地图（节点、连线、房间类型、已走路径）和本局开局生成的序列：敌人、事件、遗物的顺序；
- 第 3 节列出的全部随机数状态；
- 当前原版界面的请求投影：奖励列表、商店货架、事件当前页等。

容量按原版上限和实测定，溢出抛 `CapacityException`。字段变动同步状态布局文档。

**可见投影 `RunView`：** 决策层只能看到它。界面上看不到、**并且**不能从玩家见过的历史推出来的信息，不进 `RunView`；界面上看不到但能从历史推导的隐藏保底计数（未知点房间概率、卡牌稀有度与药水掉落）是例外，按原版规则进入投影。随机数状态、预生成序列、遗物袋顺序与解锁位图仍然不可见。可见与不可见的划分按原版界面逐项确定，并用测试锁定：决策层的类型里不能出现随机数或未来序列。

## 6. 决策请求与执行

决策请求由导入层从原版读出，R3b 落地为 `RunDecisionImporter`（`src/Import/Run/Decisions/`），不再由 Core 的局外内核生成。`RunKernel.Step` 已删除。

保留的协议类型：

- `RunDecisionRequest` 与 `RunDecisionOption*`：非零会话/修订 Token、可见来源、Min/Max、独立 Cancelable/RequireConfirmation、选牌用途，以及带稳定请求内 OptionId 的可见行；
- 共享有界 Facts/Cards/严格 UTF-8 机器键池：明确的 payload 与 typed 代价/效果，Unknown/Partial/Complete 区分未知和已知为空；bundle 一次选整组；
- `RunAction`/`RunActionKind`：携完整 Token 的 Select/Cancel 与有序唯一 OptionId 列表；空 Select 与取消不同；
- `RunDecisionProtocol.ValidateRequest/ValidateAction`：执行前完整校验数量、身份、Kind、flags、切片与候选资格，失败不修改输入；
- `RunView.Request`：From 暂留 default，WithRequest 校验后同步请求与提示字段；整个投影 20912 字节，不改变 RunState/Codec。

完整类型、容量、Kind 矩阵与拒绝码见[局外决策协议](run-decisions.md)。Core 只比较给定请求与回答的
Token；唯一身份、单调修订、原版实例映射与消费/失效必须由宿主维护，不能宣称纯校验提供防重放
存储。Ai.Run 可见白名单对嵌套字段递归检查，不含隐藏 RNG、属性包、预生成序列或游戏引用。
正式 RunDecisionImporter、共享 RunPilot 与 OfflineRunHost/RunExecutor 已接入 Map/Event/Rest/CardSelect；奖励/商店正式执行、完整 runtime 与整局通关尚未接入。事件效果仍由原版结算，不实现通用局外模拟。

请求来源是原版当前界面/模型。执行按宿主分开：

| 宿主 | 执行方式 |
| --- | --- |
| Mod | `RunDeployer` 经原版界面与公开入口执行 |
| 离线宿主 | `RunExecutor` 经原版模型层执行 |

不支持的界面或第三方内容显式拒绝，不近似、不返回默认值。地图进点仍由 `RunMapEntry` 表示"房间已建好、尚未进入"的边界；R3b 从原版重新导入后继续。

## 7. 局外决策层

- **不预知未来（已定）：** 决策只读 `RunView`。战斗求解器仍然读取真实随机数，这是既定例外，和现在一致。
- **统一接口：** `Decide(RunView, 决策请求) → 选择`。推荐模式把同一个结果展示给玩家，附上理由；全自动模式直接执行。
- **实现按阶段替换：**
  1. 规则估值：卡牌和遗物估值、事件选项打分、商店和休息规则、地图路线规则；
  2. 大规模推演：**推迟**。以后确有需要时，从 Git 历史恢复 `683bc03` 的局外模拟器，作为评估工具，而不是当前运行路径；
  3. 必要时再引入学习模型，同样挂在这个接口后面。
- 评估一个策略改动以离线整局评估（第 10 节）为准，不凭单局印象。

## 8. 战斗边界

**写回已删除。** 战斗结束后不再把 `Battle` 写回 `RunState`，也不由 Blitz 生成战后奖励；这些由原版推进，下一步再从原版导入 `RunState`。

**建局保留，用于开战选择预测。** 从进点前的 `RunState` 出发，`RunMapEntry.Enter` 完成进点前半段，`RunCombatSetup` 按开战选择序列构造每个选项对应的 `Battle`、`Rules` 与开局 `RunState`。多根搜索拿到的是同一进点下所有合法开局的根，不需要真实推进原版。

- **验证：** 同一场战斗、同一选择序列，自建的 `Battle`/`Rules`/开局 `RunState` 必须与导入层从原版读出的状态逐字节相同。
- **战斗本身：** 交给现有的整场战斗托管和档位，不另写一套。

R2a-2 的入口、遭遇生成、开战与第一回合的逐步顺序、`RulesRootFacts` 和拒绝码见
[战斗边界](run-combat-boundary.md)。

## 9. 整局托管与宿主

整局托管只有一份，放在 `Autoplay`（`RunPilot`）。它负责：

- 从宿主取当前 `RunState`/`RunView` 与导入的决策请求；
- 交给决策层或玩家，执行选择；
- 遇到战斗时交给 `CombatPilot`；
- 记录每一步，处理失败与暂停。

两个宿主都由原版推进局外：

| | Mod 宿主 | 离线宿主 |
| --- | --- | --- |
| 局外推进 | 原版游戏 | 原版 `RunManager` 在普通 .NET 进程里 |
| 当前状态 | 每一步从原版导入 `RunState` | 每一步从原版导入 `RunState` |
| 执行选择 | `RunDeployer` 经界面 | `RunExecutor` 经模型层 |
| 对账 | 只保留战斗的逐动作核对；局外逐步对账取消 | 战斗逐动作核对；无局外逐步对账 |

类名固定：

| 职责 | Mod | 离线宿主 |
| --- | --- | --- |
| 战斗宿主 | `CombatPilotHost` | `OfflineCombatHost` |
| 战斗执行 | `ActionDeployer` | `CombatExecutor` |
| 整局宿主 | `RunPilotHost` | `OfflineRunHost` |
| 局外执行 | `RunDeployer` | `RunExecutor` |
| 开新局 | — | `NewRunFactory` |

## 10. 验证与评估

验证分三层：

1. **进程内差分（`tests/Differential`）：** 只用于导入和建局。在普通 .NET 进程里调用原版代码，和 Blitz 从同一输入算出的 `Battle`/`Rules`/`RunState` 逐字节比较。
2. **两个宿主之间的一致性：** 同一种子、同一策略，Mod 与离线宿主逐步比较导入的 `RunState`；战斗另做逐动作核对。
3. **实机：** 真实游戏里跑 Mod，确认界面执行器、事件/商店/奖励流程与最终结果。

和界面绑得太紧、进程内无法调用的原版逻辑，逐个评估：加最小补丁让它能离线运行，或者只靠第 2、3 层验证，并在文档里列出。**"录下的整局作为局外回归基准"已删除**；局外不再有 Blitz 自己的推进实现需要回归。

**评估：**

- 离线整局评估器（`tools/run-eval`）：N 个种子 × 若干策略版本，统计通关率、到达楼层、各幕结束时的 HP、每次决策耗时；按角色、进阶等级、幕分组报告。
- 比较两个策略版本时用同一批种子配对比较。
- 定期用少量无头真实游戏，在同样的种子和策略下校验两个宿主没有偏差。

## 11. 阶段

| 阶段 | 内容 | 主要验证 |
| --- | --- | --- |
| R0 | 战斗搜索定型：翻倍重搜与新档位 | 离线 120 根、无头整场、D1/D2 |
| R1 | 结构准备、`RunState`、局外随机数、从原版导入局外状态 | D1/D2、离线 120 根、导入逐字段一致 |
| R2 | 原方向下的局外模拟器，做到 R2c-2 的商店；R2a-5 收尾开战选择与随机遗物 | 进程内差分，每块成千上万个种子 |
| R2a-5 | 开战选择与随机遗物；最后一个包含完整局外模拟器的提交是 `683bc03` | 建局对照、遗物矩阵、D1 |
| R3-0 | 清理与改名：删除局外模拟器、奖励/商店对照与写回；`tests/Differential`；新目录骨架与设计文档 | 保留的对照与 `683bc03` 完全相同 |
| R3a | 可行性验证：离线进程能否稳定运行原版整局、导入每一步 `RunState` | 小规模原版整局 |
| R3b | 离线宿主、决策请求导入 `RunDecisionImporter`、`RunPilot`、`NewRunFactory` | 离线整局跑通 |
| R3c | Mod 界面执行器、两个宿主的一致性检查 | 同种子同策略逐步 `RunState` 比较 |
| R4 | 局外规则策略；推荐与全自动的原版界面执行器 | 策略在评估器上开发与调参 |
| R5 | 评估闭环、界面、设置、Windows 实测、打包 | 发布门槛（待定） |

## 12. 已定决策与待定事项

已定（2026-10-27，用户确认）：

- 局外改由原版 `sts2.dll` 推进，不再自己写局外模拟器；R2d–R2f 取消；
- 两个宿主都由原版推进局外；离线宿主在普通 .NET 进程里运行原版，Mod 在真实游戏里运行；每一步都从原版重新导入 `RunState`；
- 决策请求由导入层从原版读出；
- Blitz 的局外部分只保留开战选择预测与状态/投影/导入；
- 用户原话："我们没有太大的分叉需求，只需要测试我们写的局外策略代码的好坏就行了"；
- 该条替换 2026-10-26 的"局外模拟器自己写，按 Blitz 的结构设计"；
- 最后一个包含完整局外模拟器的提交是 `683bc03`；以后需要从 Git 历史恢复。

已定（2026-10-26，仍然成立的部分）：

- 产品目标是整局自动游玩，三种模式都保留；
- 局外决策不预知未来；
- 最终覆盖所有角色、所有进阶等级、所有场景；
- 目录命名按第 4 节：`Simulation` / `Ai` / `Autoplay` × `Combat` / `Run`；
- 中文名改为"极速求解器"，英文名 BlitzSolver 不变。

待定：

- 发布门槛；
- 各决策类型在推荐模式下的界面形式；
- 进程内无法调用的原版局外逻辑清单与验证方式，R3 逐块确定。

已知风险：

- 离线运行原版的界面绑定：部分事件/商店/奖励需要弹窗或场景树，离线可能只能走模型层；
- TestMode 差别：离线打开 `TestMode.IsOn`，实机不打开，价格/教学/Tutorial 分支可能不同；
- 多进程：评估器每个工作进程跑一局，原版静态状态与内存占用需要隔离和观测。
