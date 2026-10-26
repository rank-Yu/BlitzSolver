# BlitzSolver 文档

BlitzSolver 是一个只依赖 BCL 的战斗搜索与结算内核。它把一场战斗保存在固定大小、不含对象引用的结构体里，分叉只需复制内存，在受支持的战斗上以较低开销展开 Beam 搜索，并用冻结快照校验搜索与结算结果。项目的目标是整局自动游玩，局外模拟器与决策层的架构见 [整局设计](full-run-design.md)。

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
| `src/Core/Simulation/Combat/` | `BlitzSolver.Simulation.Combat`：状态、RNG、牌堆、出牌与回合流程、伤害/格挡/Power、Hook、选择协议、指纹 |
| `src/Core/Simulation/Run/` | 整局模拟：`RunState`、`RunView`、局外随机数、编码与状态校验（R1b）；进程内内容表与 `RulesBuilder`（R2a-1） |
| `src/Core/Content/` | `BlitzSolver.Content.*`：卡牌、Power、遗物、药水、怪物、球、附魔、苦难定义 |
| `src/Core/Ai/Combat/` | `BlitzSolver.Ai.Combat`：`BeamSolver`、搜索容器、根级路线标志与指纹、搜索让帧 |
| `src/Core/Ai/Combat/Quality/` | `BlitzSolver.Ai.Combat.Quality`：路线质量比较与 incumbent 剪枝 |
| `src/Core/Ai/Combat/Policy/` | `BlitzSolver.Ai.Combat.Policy`：药水、偷窃、成长、遗物计数与首领结算策略 |
| `src/Core/Ai/Combat/Scoring/` | `BlitzSolver.Ai.Combat.Scoring`：状态评估、评分权重与能力牌估值 |
| `src/Core/Ai/Combat/Novelty/` | `BlitzSolver.Ai.Combat.Novelty`：Bfws 新颖度容器、选项与遥测 |
| `src/Core/Ai/Run/` | 局外决策：只读 `RunView` 与决策请求；R1a 只有目录说明 |
| `src/Core/Autoplay/` | `BlitzSolver.Autoplay`：`CombatPilot` 整场战斗决策循环，Mod 与离线宿主共用 |
| `src/Import/` | `BlitzSolver.Import`：原生 `CombatState` → `Battle`、原生 `RunState` → `RunState` 导入；唯一允许读游戏模型与私有字段的层，只读；战斗导入在 `Combat/`，整局在 `Run/` |
| `src/TestHost/` | `BlitzSolver.TestHost`：无头与离线共用的建局、注入与原生状态转储；只在测试中使用；战斗部分在 `Combat/`，整局在 `Run/` |
| `src/Mod/` | `BlitzSolver`：RitsuLib Mod 入口、战斗生命周期、无头捕获测试模式；子目录 `Combat/`、`Testing/`、`UI/`、`Patches/`；不加入 `BlitzSolver.sln` |
| `tests/KernelChecks/` | K0 纯内核合同（程序集 `BlitzSolver.KernelChecks`）；战斗合同在 `Combat/` |
| `tests/Golden/` | 冻结快照校验器（程序集 `BlitzSolver.Golden`）与 `data/`；战斗校验器在 `Combat/` |
| `tests/Architecture/` | 编译后的 `BlitzSolver.Core` 层依赖检查（程序集 `BlitzSolver.Architecture`），加入 `BlitzSolver.sln` |
| [`tests/OfflineHost/`](../tests/OfflineHost/README.md) | 离线宿主：不启动 Godot，建出原生 `CombatState`/`RunState` 并转储；战斗对照与离线整场在 `Combat/`，整局导入对照在 `Run/`；需要游戏文件，不在 CI 中运行 |
| `docs/archive/` | CombatSolver 时期的历史材料，不描述当前运行路径 |

依赖方向：`Content → Simulation.Combat`（整局层落地后还可用 `Simulation.Run`）、`Ai.Combat → Simulation.Combat`、`Ai.Run → Simulation.Run`（只限登记为可见投影/决策请求的类型）、`Autoplay → Simulation.*/Ai.*`、`Mod → Import → Core`。`Simulation.Combat` 不引用其他层，`Ai.Combat` 不引用 `Content`（内容通过注册表挂到模拟上）。Core 不引用游戏、Godot、Harmony、RitsuLib 或任何 NuGet 包；`Import` 是唯一允许读取模型私有字段的层；`Mod` 引用游戏程序集、Godot/Harmony 与 RitsuLib，负责入口与补丁。`Import` 与 `Mod` 都不加入 `BlitzSolver.sln`，依赖规则由 `tests/Architecture` 检查。

## 3. 文档索引

| 文档 | 内容 |
| --- | --- |
| [整局设计](full-run-design.md) | 整局自动游玩的目标架构：随机数分工、局外状态与内核、战斗边界、决策层、托管、目录与依赖规则、验证与阶段 |
| [战斗状态布局](combat-state-layout.md) | `Battle` 字段、容量、规范化与指纹 |
| [整局状态布局](run-state-layout.md) | `RunState` 字段盘点、容量、局外随机数、编码、`RunView` 可见性与导入拒绝码 |
| [内容表与 RulesBuilder](content-table.md) | 进程内内容表的范围与导出、`Rules` 30 项来源分类、根事实缺口与逐字节对照方法 |
| [战斗边界](run-combat-boundary.md) | 从地图选点进入战斗点、遭遇生成、开战 Hook、第一回合、`RulesRootFacts` 与建局对照 |
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
- 离线宿主 `--rules-compare`：410 个输入编译出的 `Rules` 与 388 个冻结规则对象全部逐字节相同；`--import-compare`（含 `--compiled-rules`）对照 410 个输入/418 个根全部相同；`--config-compare` 的 410 份整请求全部相同，418 个根的特征哈希一致；`--import-negative` 验证 3 类显式拒绝。
- Mod 已在无头游戏中加载；20 个普通根（五角色、三幕、普通/精英/Boss、私有状态）的
  实机 `BATL`/`RULE`/`SREQ.capture` 与 `NativeStateDump` 均与冻结快照/离线宿主逐字节相同。实机与离线建局的第三幕幕结构已对齐（测试会话关闭首次跑局的教学发现顺序），5 个受影响根与 23 根复跑全部相同。
- 同样的 20 个根在游戏内用中档确定性 `BeamSolver` 求一次：游戏 `SREQ` 与离线组装逐字节
  相同，`SRES` 与离线重解逐字节相同。
- 20 个固定根已在无头游戏里自动打完整场（16 胜 4 负）：逐动作核对与回合对账记录 0 偏差、
  0 部署失败；选牌/抽牌堆/弃牌堆选择与药水目标都走原版入口。普通玩家用 `F8` 开关全自动，
  没有界面。打包、协议、部署与对账规则见 [Mod 入口与无头捕获](mod.md)。
- R1b 新增：`RunState`（45,512 字节）与 `RunView`；15 条随机流的派生与 64 次抽取和原版一致；
  自然开局 200/200、D1 410/410（含 120 个质量根）逐字段相同，战斗一致性 410/410 相同；
  7 类局外导入负例显式拒绝。见 [整局状态布局](run-state-layout.md)。
- R2a-1 新增：进程内内容表与 `RulesBuilder`。D1 全部 410 个输入（420 个根，含前缀与回合准备
  fixture）、自然第一场战斗 200、全部 89 个遭遇加 Ruby Raiders 覆盖种子，`RulesBuilder` 与
  `RulesCompiler` 的 `RulesCodec` 编码全部逐字节相同；内容覆盖 1078/1078 个卡牌等级、108/108 个
  怪物。见 [内容表与 RulesBuilder](content-table.md)。
- **未验证**：界面、397 根/120 根质量语料的整场全量、语料外的多段准备阶段与未捕获前缀、
  非选牌模态、第三方 Mod 内容、Windows 启动脚本。

内核语义以 [战斗状态布局](combat-state-layout.md)、[战斗内核语义](combat-semantics.md)、[战斗角色机制](combat-characters.md) 与 [战斗药水与遗物](combat-potions-relics.md) 为准；搜索行为以 [战斗搜索](combat-ai.md) 为准；快照与证据以 [冻结快照](golden-data.md) 和 [测试与验证](testing.md) 为准。
