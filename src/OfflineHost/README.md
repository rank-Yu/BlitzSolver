# OfflineHost

离线宿主：在普通 .NET 9 进程里加载原版 `sts2.dll`，由原版 `RunManager` 推进整局；战斗交给
Blitz 的 `CombatPilot`，每一步局外状态都从原版重新导入 `RunState`。它不启动 Godot，也不
自己实现局外模拟器。

R3-0 只建立目录与职责，**不建项目文件**；R3b 落地。

## 目录

| 目录 | 职责 | R3b 来源 |
| --- | --- | --- |
| `Runtime/` | 进程启动、程序集解析、消息泵、存档桩、本地化、Godot 旁路 | `tests/Differential/` 的 `GameBootstrap`、`AssemblyBootstrap`、`MainLoopContext`、`MemorySaveStore`、`OfflineLocalization` |
| `Combat/` | 战斗宿主与执行：`OfflineCombatHost`、`CombatExecutor`、选择器、战斗等待 | `NativePilotHost` → `OfflineCombatHost`；`NativeActionExecutor` → `CombatExecutor`；`NativeChoiceSelector`、`OfflineCombatWaiter` |
| `Run/` | 整局宿主与执行：`OfflineRunHost`、`RunExecutor`、`NewRunFactory`；接收 `RunDecisionImporter` 的请求 | R3b 新增 |

类名固定如下，Mod 与离线宿主共用同一套协议：

| 职责 | Mod | 离线宿主 |
| --- | --- | --- |
| 战斗宿主 | `CombatPilotHost` | `OfflineCombatHost` |
| 战斗执行 | `ActionDeployer` | `CombatExecutor` |
| 整局宿主 | `RunPilotHost` | `OfflineRunHost` |
| 局外执行 | `RunDeployer`（经界面） | `RunExecutor`（经模型层） |
| 开新局 | — | `NewRunFactory` |

决策请求导入为 `RunDecisionImporter`，放在 `src/Import/Run/Decisions/`；离线宿主的类不加
`Native` 前缀。

## 引用规则

- `OfflineHost` 引用 `Core`、`Import`、`sts2.dll`、`GodotSharp`、`0Harmony`。
- `OfflineHost` 不引用 `TestHost`；驱动原版状态的测试注入留在 `tests/Differential`。
- `Mod` 不引用 `OfflineHost`。
- `tests/Differential` 引用 `OfflineHost` 与 `TestHost`，保留进程内差分。
- `tools/run-eval` 引用 `OfflineHost`。
- 这三个项目都不加入 `BlitzSolver.sln`。

设计、TestMode 差别与验证方式见 [离线宿主](../../docs/offline-host.md)；整局自动游玩的协议见
[自动游玩](../../docs/autoplay.md)。
