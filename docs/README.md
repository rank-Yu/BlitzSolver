# Sts2Solver 文档

Sts2Solver 是一个只依赖 BCL 的战斗搜索与结算内核。它把一场战斗保存在固定大小、不含对象引用的结构体里，分叉只需复制内存，在受支持的战斗上以较低开销展开 Beam 搜索，并用冻结快照校验搜索与结算结果。项目的目标是整局自动游玩：局外由原版 `sts2.dll` 推进，Sts2Solver 负责战斗与局外策略，架构见 [整局设计](full-run-design.md)。

本目录是 Sts2Solver 的设计与验证文档。CombatSolver 时期的宿主接入、Bridge 与离线回放等材料收在 [`archive/`](archive/)。

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
- 不再自己写局外模拟器；局外由原版推进，最后一个包含完整局外模拟器的提交是 `683bc03`。

## 2. 代码布局

| 目录 | 内容 |
| --- | --- |
| `src/Core/Simulation/Combat/` | `Sts2Solver.Simulation.Combat`：状态、RNG、牌堆、出牌与回合流程、伤害/格挡/Power、Hook、选择协议、指纹 |
| `src/Core/Simulation/Run/` | 局外状态 `RunState`、编码与投影、决策请求类型、内容表与 `RulesBuilder`、开战选择预测 `RunCombatSetup` |
| `src/Core/Content/` | `Sts2Solver.Content.*`：卡牌、Power、遗物、药水、怪物、球、附魔、苦难定义 |
| `src/Core/Ai/Combat/` | `Sts2Solver.Ai.Combat`：`BeamSolver`、搜索容器、根级路线标志与指纹、搜索让帧 |
| `src/Core/Ai/Combat/Quality/` | `Sts2Solver.Ai.Combat.Quality`：路线质量比较与 incumbent 剪枝 |
| `src/Core/Ai/Combat/Policy/` | `Sts2Solver.Ai.Combat.Policy`：药水、偷窃、成长、遗物计数与首领结算策略 |
| `src/Core/Ai/Combat/Scoring/` | `Sts2Solver.Ai.Combat.Scoring`：状态评估、评分权重与能力牌估值 |
| `src/Core/Ai/Combat/Novelty/` | `Sts2Solver.Ai.Combat.Novelty`：Bfws 新颖度容器、选项与遥测 |
| `src/Core/Ai/Run/` | 局外决策：只读 `RunView` 与决策请求 |
| `src/Core/Autoplay/` | `Sts2Solver.Autoplay`：`CombatPilot` 与 `RunPilot`（R3b） |
| `src/Import/` | `Sts2Solver.Import`：原生 `CombatState` → `Battle`、原生 `RunState` → `RunState`、决策请求导入；唯一允许读游戏模型与私有字段的层，只读；子目录 `Combat/`、`Run/`、`Run/Decisions/`（R3b） |
| `src/OfflineHost/` | 离线宿主（R3b）：普通 .NET 进程运行原版有界 Act1 多房间与商店；子目录 `Runtime/`、`Combat/`、`Run/` |
| `src/TestHost/` | `Sts2Solver.TestHost`：测试用建局、注入与原生状态转储；战斗部分在 `Combat/`，整局在 `Run/` |
| `src/Mod/` | `Sts2Solver`：RitsuLib Mod 入口、战斗生命周期、无头捕获测试模式；子目录 `Combat/`、`Run/`（R3c）、`UI/`、`Testing/`、`Patches/`；不加入 `Sts2Solver.sln` |
| `tests/KernelChecks/` | K0 纯内核合同（程序集 `Sts2Solver.KernelChecks`）；战斗合同在 `Combat/`，整局合同在 `Run/` |
| `tests/Golden/` | 冻结快照校验器（程序集 `Sts2Solver.Golden`）与 `data/`；战斗校验器在 `Combat/` |
| `tests/Architecture/` | 编译后的 `Sts2Solver.Core` 层依赖检查（程序集 `Sts2Solver.Architecture`），加入 `Sts2Solver.sln` |
| [`tests/Differential/`](../tests/Differential/README.md) | 进程内差分：不启动 Godot，建出原生 `CombatState`/`RunState`；战斗对照、进程内原版动作执行与离线整场在 `Combat/`，整局导入、建局、地图选点、内容表与 `RulesBuilder` 在 `Run/`；需要游戏文件，不在 CI 中运行 |
| `tools/run-eval/` | 独立烟测 CLI：首战、下一房间与有界多房间；批量整局评估仍待 R5 |
| `docs/archive/` | CombatSolver 时期的历史材料，不描述当前运行路径 |

依赖方向：`Content → Simulation.Combat`（整局层还可用 `Simulation.Run`）、`Ai.Combat → Simulation.Combat`、`Ai.Run → Simulation.Run`（只限登记为可见投影/决策请求的类型）、`Autoplay → Simulation.*/Ai.*`、`Mod → Import → Core`。`Simulation.Combat` 不引用其他层，`Ai.Combat` 不引用 `Content`（内容通过注册表挂到模拟上）。Core 不引用游戏、Godot、Harmony、RitsuLib 或任何 NuGet 包；`Import` 是唯一允许读取模型私有字段的层；`OfflineHost` 引用 Core、Import、游戏程序集与 Godot/Harmony，不引用 `TestHost`；`Mod` 不引用 `OfflineHost`。`Import`、`Mod`、`OfflineHost`、`tools/run-eval` 与 `tests/Differential` 都不加入 `Sts2Solver.sln`，依赖规则由 `tests/Architecture` 检查。

## 3. 文档索引

| 文档 | 内容 |
| --- | --- |
| [整局设计](full-run-design.md) | 整局自动游玩的目标架构：随机数分工、局外状态、导入与决策、战斗边界、托管、目录与依赖规则、验证与阶段 |
| [战斗状态布局](combat-state-layout.md) | `Battle` 字段、容量、规范化与指纹 |
| [整局状态布局](run-state-layout.md) | `RunState` 字段盘点、容量、局外随机数、编码、`RunView` 可见性与导入拒绝码 |
| [内容表与 RulesBuilder](content-table.md) | 进程内内容表的范围与导出、`Rules` 30 项来源分类、根事实缺口与逐字节对照方法 |
| [局外决策协议与地图进点](run-decisions.md) | 请求/动作类型、地图合法点、未知点掷骰、非战斗房间边界、拒绝码与 R3b 导入位置 |
| [战斗边界](run-combat-boundary.md) | 从地图选点进入战斗点、遭遇生成、开战 Hook、第一回合、`RulesRootFacts` 与建局对照 |
| [离线宿主](offline-host.md) | 离线宿主的启动、目录、引用规则与 TestMode 差别清单 |
| [自动游玩](autoplay.md) | `RunPilot`、`IRunPilotHost`、两个宿主的执行方式与三种模式 |
| [战斗内核语义](combat-semantics.md) | 结算管线、Hook 派发顺序、内容定义、选择协议、怪物 AI |
| [战斗角色机制](combat-characters.md) | 弃牌与 Sly、充能球、星能与锻造、Osty 与 Doom |
| [战斗药水与遗物](combat-potions-relics.md) | 额外回合管线、药水与遗物机制 |
| [战斗搜索](combat-ai.md) | `BeamSolver` 的容器复用、保留、评估、剪枝与容量边界 |
| [原生状态导入](import.md) | `CombatState` → `Battle`/`Rules`/评估配置的编译、拒绝码与离线对照 |
| [冻结快照](golden-data.md) | `tests/Golden/data` 的格式、来源、哈希与更新条件 |
| [Mod 入口与无头捕获](mod.md) | 打包、RitsuLib 入口、共存、部署与对账、测试协议、启动脚本与实机对照 |
| [测试与验证](testing.md) | 构建、K0、D1/D2、manifest、每层证明与未验证项 |

历史材料：

| 文档 | 内容 |
| --- | --- |
| [CombatSolver 接入方式](archive/combatsolver-integration.md) | CombatSolver 的接入分派与旧引擎回放物化，仅作历史参考 |
| [CombatSolver 准入与校验](archive/admission-and-verification.md) | CombatSolver 里对接原引擎的 Bridge 准入与运行时核对 |
| [CombatSolver 时期测试记录](archive/combatsolver-flash-testing.md) | 旧回归工具、批次数值与实机整场记录 |

## 4. 当前状态

- Core 独立编译，只有 BCL 依赖；构建 0 警告；`tests/Architecture` 的层依赖检查 0 违反。
- K0 纯内核合同通过；`Golden` 的 D1 重放与 D2 全量求解通过。D2 期望结果已按 M3b 的默认搜索重冻（`provenance.refreeze`）。
- 冻结快照 `tests/Golden/data` 由 `manifest.json` 逐文件校验；校验入口见 [测试与验证](testing.md)。
- 离线宿主 `--rules-compare`：410 个输入编译出的 `Rules` 与 388 个冻结规则对象全部逐字节相同；`--import-compare`（含 `--compiled-rules`）对照 410 个输入/418 个根全部相同；`--config-compare` 的 410 份整请求全部相同，418 个根的特征哈希一致。
- Mod 已在无头游戏中加载；20 个普通根（五角色、三幕、普通/精英/Boss、私有状态）的实机 `BATL`/`RULE`/`SREQ.capture` 与 `NativeStateDump` 均与冻结快照/离线宿主逐字节相同。
- R1b 新增：`RunState`（49,912 字节）与 `RunView`；15 条随机流的派生与 64 次抽取和原版一致；自然开局 200/200、D1 410/410（含 120 个质量根）逐字段相同；7 类局外导入负例显式拒绝。见 [整局状态布局](run-state-layout.md)。
- R2a-1 新增：进程内内容表与 `RulesBuilder`。D1 全部 410 个输入、自然第一场战斗 200、全部 89 个遭遇加 Ruby Raiders 覆盖种子，`RulesBuilder` 与 `RulesCompiler` 的 `RulesCodec` 编码全部逐字节相同；内容覆盖 1078/1078 个卡牌等级、108/108 个怪物。见 [内容表与 RulesBuilder](content-table.md)。
- R2a-2 新增：地图进点到首个出牌阶段的战斗边界 `RunCombatSetup`；R2a-3 至 R2c-2 的局外模拟、奖励与商店已按 2026-10-27 的决定删除，最后包含它们的提交是 `683bc03`。
- R3-0：删除局外模拟器、奖励/商店对照、写回与新局生成；原离线宿主工程改名为 `tests/Differential`；保留的导入/建局/地图进点/战斗对照与 `683bc03` 相同。终局独立检查发现 `TheGambitPower` 在 `AfterDamageReceived` 中杀死玩家后，原版按 `IsActiveForHooks` 逐项过滤后续监听，而 Sts2Solver 仍调用后续遗物；已按此最小修正 `HookDispatch.TryResolve`，KernelChecks 新增定向合同，D1/D2 不变。中档离线 120 根因此有 1 根（`quality-SILENT-022`）搜索展开数变化，其余 119 根与基线逐字段相同。见 [离线宿主](offline-host.md) 与 [测试与验证](testing.md) 6.13。
- **未验证**：界面、离线原版整局、两个宿主的一致性、语料外的多段准备阶段与未捕获前缀、第三方 Mod 内容、Windows 启动脚本。

内核语义以 [战斗状态布局](combat-state-layout.md)、[战斗内核语义](combat-semantics.md)、[战斗角色机制](combat-characters.md) 与 [战斗药水与遗物](combat-potions-relics.md) 为准；搜索行为以 [战斗搜索](combat-ai.md) 为准；快照与证据以 [冻结快照](golden-data.md) 和 [测试与验证](testing.md) 为准。
