# BlitzSolver 仓库工作指令

本文件约束所有在本仓库中工作的 coding agent。开始处理任务前完整阅读。

## 1. 项目边界

BlitzSolver（极速求解器）是《杀戮尖塔 2》的单人自动游玩 Mod，面向原版游戏内容。战斗求解已经完成；目标是整局自动游玩，保留只托管战斗、只给推荐、全自动三种模式。目标目录结构（`Simulation` / `Ai` / `Autoplay` × `Combat` / `Run`）、依赖规则和验证方式见 [整局设计](docs/full-run-design.md) 第 4 节；当前目录与职责如下表，依赖规则由 `tests/Architecture` 检查。

| 目录 | 命名空间 | 职责 |
| --- | --- | --- |
| `src/Core/Simulation/Combat` | `BlitzSolver.Simulation.Combat` | 结构体状态 `Battle`、回合与出牌管线、结算与规则表 `Rules` |
| `src/Core/Simulation/Run` | `BlitzSolver.Simulation.Run` | 整局模拟：定长整局状态 `RunState`、局外随机数、编码 `RunCodec` 与玩家可见投影 `RunView`（R1b）；局外模拟器随 R2 落地 |
| `src/Core/Content` | `BlitzSolver.Content.*` | 卡牌、Power、遗物、药水、球、附魔与怪物定义 |
| `src/Core/Ai/Combat` | `BlitzSolver.Ai.Combat`（含 `.Quality`、`.Policy`、`.Scoring`、`.Novelty`） | `BeamSolver`、状态评估、路线质量、资源策略与新颖度搜索 |
| `src/Core/Ai/Run` | `BlitzSolver.Ai.Run` | 局外决策；R1a 只有目录说明，规则与估值随 R4 落地 |
| `src/Core/Autoplay` | `BlitzSolver.Autoplay` | `CombatPilot` 整场战斗决策循环；Mod 与离线宿主共用 |
| `src/Import` | `BlitzSolver.Import` | 原生 `CombatState`/`CombatManager.History`/模型私有字段 → `Battle`；唯一允许读游戏模型与私有字段的层，只读，不改动游戏；子目录 `Combat/`（战斗导入）、`Run/`（整局导入 `RunStateImporter`） |
| `src/TestHost` | `BlitzSolver.TestHost` | 无头与离线共用的建局、注入与原生状态转储；只在测试中使用，会写游戏状态；子目录 `Combat/`、`Run/`（新局入口与独立原生整局转储） |
| `src/Mod` | `BlitzSolver` | RitsuLib `ModInitializer` 入口、战斗生命周期、无头捕获测试模式；子目录 `Combat/`、`Testing/`、`UI/`、`Patches/`；不加入 `BlitzSolver.sln` |
| `tests/KernelChecks` | | 内核合同（K0）；战斗合同在 `Combat/`，整局合同在 `Run/` |
| `tests/Golden` | | 冻结快照校验器与数据；战斗校验器在 `Combat/` |
| `tests/Architecture` | | 编译后的 `BlitzSolver.Core` 层依赖检查；加入 `BlitzSolver.sln` |
| [`tests/OfflineHost`](tests/OfflineHost/README.md) | | 离线宿主；战斗对照与离线整场在 `Combat/`，整局导入对照在 `Run/`，入口与引导留根目录 |

依赖方向：`Content → Simulation.Combat`（整局层落地后还可用 `Simulation.Run`）、`Ai.Combat → Simulation.Combat`、`Ai.Run → Simulation.Run`（只限登记为可见投影/决策请求的类型）、`Autoplay → Simulation.*/Ai.*`、`Mod → Import → Core`。`Simulation.Combat` 不引用其他层；`Ai.Combat` 不引用 `Content`；内容通过 `Registry` 挂到模拟上。`Import` 是唯一允许读取模型私有字段的层；`Mod` 引用游戏程序集、Godot/Harmony 与 RitsuLib，负责入口与补丁，但不直接读私有字段。`Import` 与 `Mod` 都不加入 `BlitzSolver.sln`，依赖规则由 `tests/Architecture` 检查。

## 2. 硬约束

- `src/Core` 只依赖 BCL：不引用游戏程序集、Godot、HarmonyLib 或任何 NuGet 包。
- 正确性优先于搜索质量与性能；不要用扩大宽度、节点或时间预算掩盖模拟偏差。
- 未知语义显式失败：未知内容抛 `UnsupportedException`，容量不足抛 `CapacityException`。不截断、不跳过、不返回默认值，不写 `catch (Exception)`；只捕获这两种异常、校验失败与取消。
- 搜索展开中单个子状态的 `CapacityException` 只丢弃该分支并计数；根导入或根评分失败让整次求解作废，不交出半成品路线。
- 热路径不分配托管对象；内容定义没有字段状态，可变值只存在 `Battle`。
- 原版数值从 `Rules` 读取，不硬编码。
- 修改 `Battle` 字段时同步 `docs/combat-state-layout.md` 与布局尺寸断言。`HookMask` 的 64 位已用满，新 Hook 加在 `HookMaskExt`，不复用、不重排已有位。
- 搜索不读取全局可变设置，外部能力通过 `BeamOptions` 等请求参数注入。
- 局外决策只读玩家可见信息，不读取随机数状态或未来的生成序列；战斗求解器读取真实随机数是既定例外。
- 不在每个节点的热路径上新增按根恒定的扫描；只在根含相关内容时才走的路径，用 `Rules` 的根级布尔跳过。

## 3. 冻结快照是回归基准

`tests/Golden/data` 是从 CombatSolver 导出时逐步与原引擎核对过的期望值，来源记录在 `manifest.json` 的 `provenance`。格式见 `docs/golden-data.md`。

- 改动 `src/Core` 的提交必须通过完整 `verify`（D1 与 D2）。
- 不手改数据；`manifest.json` 只由 `Golden manifest` 重写。
- 有意改变语义或搜索结果的改动会让快照失配。这类改动先取得实机证据并经用户同意，再更新快照，提交说明写清哪些根变了、为什么。

## 4. 移植代码

- 从 CombatSolver 移植的代码在文件头保留来源路径、源提交与 MIT 声明。
- 参照 CombatSolver 翻译编写的代码（例如对照原引擎的结算顺序、原版 Runtime 的部署流程）同样在文件头或文档里注明对照来源。不从 CombatSolver 的 `src/Engine` 复制状态定义或方法体。README 的“来源与许可”一节保留 CombatSolver 与 Random Foreseer 的版权声明，不要删。
- `LICENSE` 与 `README.md` 随每个二进制发布包一起提供（README 里有上游的版权声明）。
- 只移植 BlitzSolver 实际读取的成员，语义与注释原样保留；要改语义时另起提交，并按第 3 节处理。

## 5. 验证

```bash
dotnet build BlitzSolver.sln -c Release
dotnet run --project tests/KernelChecks -c Release --no-build
dotnet run --project tests/Golden -c Release --no-build -- k0
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data
```

- D1 约 10 秒；D2 在 8 并行下约 6 分钟。可用 `--only d1|d2`、`--shard i/n`、`--jobs N` 拆开运行。
- 报告完成前运行与改动相称的验证，无法执行时明确写未验证。只改文档时不跑 D2；同一输入已经通过的验证不重复跑。

## 6. 提交与文档

- 提交信息首行写成 `<模块>: <中文说明>`，冒号用半角加空格，说明与正文用中文。模块用英文：只改一个目录时用目录或项目名（`Simulation`、`Content`、`Ai`、`Autoplay`、`Import`、`TestHost`、`Mod`、`OfflineHost`、`Golden`、`KernelChecks`、`Architecture`、`corpora`）；跨 `src/Core` 多个部分用 `Core`；不对应单个目录时用 `docs`、`repo`、`ci`、`license`；只改顶层文件时用文件名（`README`、`AGENTS.md`）。
- 只提交本任务的文件；`artifacts/`、`bin/`、`obj/`、`.local/` 不进提交。
- 一次性脚本、探针与中间产物放 `.local/`，任务结束后删除。
- 文档用中文，每份文档只有一个职责，`docs/README.md` 是索引。进度、批次与运行流水不写进本文件。
