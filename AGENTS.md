# Sts2Solver 仓库工作指令

本文件约束所有在本仓库中工作的 coding agent。开始任务前完整阅读。

## 1. 项目边界

主分支是原版单人、同种子 SL 模式的战斗求解 Mod。局外代码保存在 `dev/full-run`，不属于主分支当前发布范围。目录与依赖详见 [架构说明](docs/README.md)。

- `Core/Simulation/Combat`：战斗状态、规则与结算；不引用其他层。
- `Core/Content` 与 `Core/Ai/Combat`：内容与搜索，各自只依赖 Simulation，Ai 不引用 Content。
- `Core/Autoplay`：CombatPilot，通过宿主接口驱动整场战斗；依赖 Simulation 与 Ai。
- `Import/Combat`：只读游戏模型、私有字段，生成 Battle、Rules 与搜索配置。
- `Mod`：主线程捕获/执行/核对，后台搜索；UI 暂为 F8 快捷键。
- `TestHost/Combat`：战斗测试场景、注入与转储；`TestHost/ModTesting`：测试版 Mod 专用入口与补丁。
- `tests/Differential`：战斗原版差分与离线评测；不包含局外执行。

正式 Mod 仅编入 Core、Import 与 Mod 源码。`-p:EnableModTesting=true` 才编入 TestHost；两种构建的中间文件和包目录隔离。生产代码不得依赖测试 DTO 或测试宿主。

## 2. 硬约束

- `src/Core` 只依赖 BCL：不引用游戏程序集、Godot、HarmonyLib 或任何 NuGet 包。
- 正确性优先于搜索质量与性能；不要用扩大宽度、节点或时间预算掩盖模拟偏差。
- 未知语义显式失败：未知内容抛 `UnsupportedException`，容量不足抛 `CapacityException`。不截断、不跳过、不返回默认值，不写 `catch (Exception)`；只捕获这两种异常、校验失败与取消。
- 搜索展开中单个子状态的 `CapacityException` 只丢弃该分支并计数；根导入或根评分失败让整次求解作废，不交出半成品路线。
- 热路径不分配托管对象；内容定义没有字段状态，可变值只存在 `Battle`。
- 原版数值从 `Rules` 读取，不硬编码。
- 修改 `Battle` 字段时同步 `docs/combat-state-layout.md` 与布局尺寸断言。`HookMask` 的 64 位已用满，新 Hook 加在 `HookMaskExt`，不复用、不重排已有位。
- 牌区下标 ≥ `CardCount` 的牌格内容无定义，任何代码不得读取；热路径复制状态用 `Battle.CopyPacked`，普通结构赋值只留在根、回放、导入和测试等非热路径。
- 搜索不读取全局可变设置，外部能力通过 `BeamOptions` 等请求参数注入。
- 同种子 SL 战斗搜索允许读取真实随机数状态。
- 不在每个节点的热路径上新增按根恒定的扫描；只在根含相关内容时才走的路径，用 `Rules` 的根级布尔跳过。

## 3. 冻结快照是回归基准

`tests/Golden/data` 是从 CombatSolver 导出时逐步与原引擎核对过的期望值，来源记录在 `manifest.json` 的 `provenance`。格式见 `docs/golden-data.md`。

- 改动 `src/Core` 的提交必须通过完整 `verify`（模拟回归与搜索回归）。
- 不手改数据；`manifest.json` 只由 `Golden manifest` 重写。
- 有意改变语义或搜索结果的改动会让快照失配。这类改动先取得实机证据并经用户同意，再更新快照，提交说明写清哪些根变了、为什么。

## 4. 移植代码

- 从 CombatSolver 移植的代码在文件头保留来源路径、源提交与 MIT 声明。
- 参照 CombatSolver 翻译编写的代码（例如对照原引擎的结算顺序、原版 Runtime 的部署流程）同样在文件头或文档里注明对照来源。不从 CombatSolver 的 `src/Engine` 复制状态定义或方法体。README 的“来源与许可”一节保留 CombatSolver 与 Random Foreseer 的版权声明，不要删。
- `LICENSE` 与 `README.md` 随每个二进制发布包一起提供（README 里有上游的版权声明）。
- 只移植 Sts2Solver 实际读取的成员，语义与注释原样保留；要改语义时另起提交，并按第 3 节处理。

## 5. 验证

```bash
dotnet build Sts2Solver.sln -c Release
dotnet run --project tests/KernelChecks -c Release --no-build
dotnet run --project tests/Golden -c Release --no-build -- k0
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data
```

- 模拟回归通常较快；搜索回归耗时取决于机器。可用 `--only simulation|search`、`--shard i/n`、`--jobs N` 拆开运行。
- 报告完成前运行与改动相称的验证，无法执行时明确写未验证。只改文档时不跑搜索回归；同一输入已经通过的验证不重复跑。

## 6. 提交与文档

- 提交信息首行写成 `<模块>: <中文说明>`，冒号用半角加空格，说明与正文用中文。模块用英文：只改一个目录时用目录或项目名（`Simulation`、`Content`、`Ai`、`Autoplay`、`Import`、`TestHost`、`Mod`、`Differential`、`Golden`、`KernelChecks`、`Architecture`、`corpora`）；跨 `src/Core` 多个部分用 `Core`；不对应单个目录时用 `docs`、`repo`、`ci`、`license`；只改顶层文件时用文件名（`README`、`AGENTS.md`）。
- 只提交本任务的文件；`artifacts/`、`bin/`、`obj/`、`.local/` 不进提交。
- 一次性脚本、探针与中间产物放 `.local/`，任务结束后删除。
- 文档用中文，每份文档只有一个职责，`docs/README.md` 是索引。
- `docs/` 只保存架构、接口、使用说明、设计参考与测试方法；实验记录、跑分表、验证流水、任务规格和批次报告放在本地 `.local/`，不提交到仓库。进度与运行流水也不写进本文件。
