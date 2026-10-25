# BlitzSolver 仓库工作指令

本文件约束所有在本仓库中工作的 coding agent。开始处理任务前完整阅读。

## 1. 项目边界

BlitzSolver（极速战斗求解器）是《杀戮尖塔 2》的单人整场战斗路线搜索 Mod，面向原版游戏内容。

| 目录 | 命名空间 | 职责 |
| --- | --- | --- |
| `src/Core/Engine` | `BlitzSolver.Engine` | 结构体状态 `Battle`、回合与出牌管线、结算与规则表 `Rules` |
| `src/Core/Content` | `BlitzSolver.Content.*` | 卡牌、Power、遗物、药水、球、附魔与怪物定义 |
| `src/Core/Search` | `BlitzSolver.Search`（含 `.Scoring`） | `BeamSolver` 与状态评估；`Ported/` 是移植自 CombatSolver 的依赖 |
| `tests/KernelChecks` | | 内核合同（K0） |
| `tests/Golden` | | 冻结快照校验器与数据 |

依赖方向：`Content → Engine`、`Search → Engine`。`Engine` 不引用 `Content` 或 `Search`，`Search` 也不引用 `Content`；内容通过 `Registry` 挂到引擎上。

## 2. 硬约束

- `src/Core` 只依赖 BCL：不引用游戏程序集、Godot、HarmonyLib 或任何 NuGet 包。
- 正确性优先于搜索质量与性能；不要用扩大宽度、节点或时间预算掩盖模拟偏差。
- 未知语义显式失败：未知内容抛 `UnsupportedException`，容量不足抛 `CapacityException`。不截断、不跳过、不返回默认值，不写 `catch (Exception)`；只捕获这两种异常、校验失败与取消。
- 搜索展开中单个子状态的 `CapacityException` 只丢弃该分支并计数；根导入或根评分失败让整次求解作废，不交出半成品路线。
- 热路径不分配托管对象；内容定义没有字段状态，可变值只存在 `Battle`。
- 原版数值从 `Rules` 读取，不硬编码。
- 修改 `Battle` 字段时同步 `docs/state-layout.md` 与布局尺寸断言。`HookMask` 的 64 位已用满，新 Hook 加在 `HookMaskExt`，不复用、不重排已有位。
- 搜索不读取全局可变设置，外部能力通过 `BeamOptions` 等请求参数注入。
- 不在每个节点的热路径上新增按根恒定的扫描；只在根含相关内容时才走的路径，用 `Rules` 的根级布尔跳过。

## 3. 冻结快照是回归基准

`tests/Golden/data` 是从 CombatSolver 导出时逐步与原引擎核对过的期望值，来源记录在 `manifest.json` 的 `provenance`。格式见 `docs/golden-data.md`。

- 改动 `src/Core` 的提交必须通过完整 `verify`（D1 与 D2）。
- 不手改数据；`manifest.json` 只由 `Golden manifest` 重写。
- 有意改变语义或搜索结果的改动会让快照失配。这类改动先取得实机证据并经用户同意，再更新快照，提交说明写清哪些根变了、为什么。

## 4. 移植代码

- 从 CombatSolver 移植的代码在文件头保留来源路径、源提交与 MIT 声明，并在 `THIRD_PARTY_NOTICES.md` 列出移植范围。
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

- 提交信息用中文。只提交本任务的文件；`artifacts/`、`bin/`、`obj/`、`.local/` 不进提交。
- 一次性脚本、探针与中间产物放 `.local/`，任务结束后删除。
- 文档用中文，每份文档只有一个职责，`docs/README.md` 是索引。进度、批次与运行流水不写进本文件。
