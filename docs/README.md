# BlitzSolver 文档

BlitzSolver 是一个只依赖 BCL 的战斗搜索与结算内核。它把一场战斗保存在固定大小、不含对象引用的结构体里，分叉只需复制内存，在受支持的战斗上以较低开销展开 Beam 搜索，并用冻结快照校验搜索与结算结果。

本目录是 BlitzSolver 的设计与验证文档。CombatSolver 时期的宿主接入、Bridge 与离线回放等材料收在 [`archive/`](archive/)。

## 1. 设计目标

仍然成立的部分：

- **定长结构体**：`Battle` 是 `unmanaged` 结构体，没有对象引用；容量是编译期常量，溢出显式失败，不截断、不逐出。
- **整块复制分叉**：复制一份 `Battle` 就是分叉；没有写时复制、引用计数或分层状态。
- **热路径零托管分配**：动作执行、状态复制与指纹计算不分配托管对象；搜索容器按块预分配，块只追加。
- **显式失败**：不支持的语义抛 `UnsupportedException`，容量不足抛 `CapacityException`，不会静默近似或返回默认值。
- **确定性**：同一局面只有一种字节表示；同一次搜索在同一请求上产生同一路线与同一结果字节。

不再属于当前仓库的目标：

- 不再维护 CombatSolver 的宿主接入、Bridge 根导入与旧引擎回放物化；这些只作为历史参考留在 `archive/`。
- 不再以「与 CombatSolver 原引擎逐步一致」作为运行期承诺。一致性冻结在 `tests/Golden/data`，后续以快照和实机证据为准。

## 2. 代码布局

| 目录 | 内容 |
| --- | --- |
| `src/Core/Engine/` | `BlitzSolver.Engine`：状态、RNG、牌堆、出牌与回合流程、伤害/格挡/Power、Hook、选择协议、指纹 |
| `src/Core/Content/` | `BlitzSolver.Content.*`：卡牌、Power、遗物、药水、怪物、球、附魔、苦难定义 |
| `src/Core/Search/` | `BlitzSolver.Search`：`BeamSolver`、搜索容器、评分与下界 |
| `src/Core/Search/Scoring/` | `BlitzSolver.Search.Scoring`：状态评估 |
| `src/Import/` | `BlitzSolver.Import`：原生 `CombatState` → `Battle` 导入；唯一允许读游戏模型与私有字段的层 |
| `src/Core/Search/Ported/` | `BlitzSolver.Search`：从 CombatSolver `a62f1850` 移植的纯值依赖 |
| `tests/KernelChecks/` | K0 纯内核合同（程序集 `BlitzSolver.KernelChecks`） |
| `tests/Golden/` | 冻结快照校验器（程序集 `BlitzSolver.Golden`）与 `data/` |
| [`tests/OfflineHost/`](../tests/OfflineHost/README.md) | 离线宿主：不启动 Godot，建出原生 `CombatState` 并转储；需要游戏文件，不在 CI 中运行 |
| `docs/archive/` | CombatSolver 时期的历史材料，不描述当前运行路径 |

依赖方向：`Content → Engine`、`Search → Engine`、`Import → Core`。Engine 不引用 Content 或 Search，Search 也不引用 Content（内容通过注册表挂到引擎上）。Core 不引用游戏、Godot、Harmony、RitsuLib 或任何 NuGet 包；`Import` 是唯一允许引用游戏程序集并读取模型私有字段的层，且不加入 `BlitzSolver.sln`。

## 3. 文档索引

| 文档 | 内容 |
| --- | --- |
| [状态布局](state-layout.md) | `Battle` 字段、容量、规范化与指纹 |
| [内核语义](kernel-semantics.md) | 结算管线、Hook 派发顺序、内容定义、选择协议、怪物 AI |
| [角色机制](kernel-characters.md) | 弃牌与 Sly、充能球、星能与锻造、Osty 与 Doom |
| [药水与遗物](kernel-potions-relics.md) | 额外回合管线、药水与遗物机制 |
| [搜索](search.md) | `BeamSolver` 的容器复用、保留、评估、剪枝与容量边界 |
| [原生状态导入](import.md) | `CombatState` → `Battle` 的字段来源、拒绝码与离线对照 |
| [冻结快照](golden-data.md) | `tests/Golden/data` 的格式、来源、哈希与更新条件 |
| [测试与验证](testing.md) | 构建、K0、D1/D2、manifest、每层证明与未验证项 |

历史材料：

| 文档 | 内容 |
| --- | --- |
| [CombatSolver 接入方式](archive/combatsolver-integration.md) | CombatSolver 的接入分派与旧引擎回放物化，仅作历史参考 |
| [CombatSolver 准入与校验](archive/admission-and-verification.md) | CombatSolver 里对接原引擎的 Bridge 准入与运行时核对 |
| [CombatSolver 时期测试记录](archive/combatsolver-flash-testing.md) | 旧回归工具、批次数值与实机整场记录 |

## 4. 当前状态

- Core 独立编译，只有 BCL 依赖；构建 0 警告。
- K0 纯内核合同通过；`Golden` 的 D1 重放与 D2 全量求解通过。
- 冻结快照 `tests/Golden/data` 由 `manifest.json` 逐文件校验；校验入口见 [测试与验证](testing.md)。
- 离线宿主新增 `--import-compare`：397 个在范围根全部逐字节相同；`--import-negative` 验证 3 类显式拒绝。
- **未验证**：实机运行、前缀与额外回合准备根、部署、界面与第三方 Mod 内容。

内核语义以 [状态布局](state-layout.md)、[内核语义](kernel-semantics.md)、[角色机制](kernel-characters.md) 与 [药水与遗物](kernel-potions-relics.md) 为准；搜索行为以 [搜索](search.md) 为准；快照与证据以 [冻结快照](golden-data.md) 和 [测试与验证](testing.md) 为准。
