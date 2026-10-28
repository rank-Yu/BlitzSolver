# 自动游玩

整局自动游玩由两个宿主驱动，共用同一套整局协议；局外都由原版推进，Blitz 只负责决策与战斗。
R3-0 只写协议与类名；`RunPilot`、`IRunPilotHost`、`OfflineRunHost`/`RunExecutor` 都尚未实现，
没有已跑通的离线整局。

## 1. 协议

- `RunPilot`（R3b 落地）：整局决策循环。每一步从宿主取得 `RunState`/`RunView` 与
  `RunDecisionRequest`，选择 `RunAction`，交给宿主执行；遇到战斗时调用 `CombatPilot`。
- `IRunPilotHost`：宿主接口。提供当前请求、执行局外动作、启动战斗、结束整局。
- `CombatPilot`：战斗决策循环，已有实现；Mod 与离线宿主共用。
- `RunDecisionImporter`（`src/Import/Run/Decisions/`）：从原版当前界面读出请求，是
  `RunState` 之外的唯一决策来源。

## 2. 两个宿主

| 职责 | Mod | 离线宿主 |
| --- | --- | --- |
| 战斗宿主 | `CombatPilotHost` | `OfflineCombatHost` |
| 战斗执行 | `ActionDeployer` | `CombatExecutor` |
| 整局宿主 | `RunPilotHost` | `OfflineRunHost` |
| 局外执行 | `RunDeployer`（经界面） | `RunExecutor`（经模型层） |
| 开新局 | — | `NewRunFactory` |

- **Mod**：`RunPilotHost` 在真实游戏里运行，`RunDeployer` 通过原版界面/公开入口执行局外动作；
  战斗由 `ActionDeployer` 逐动作部署并核对边界。
- **离线宿主**（R3b 落地）：设计为 `OfflineRunHost` 在普通 .NET 进程里运行，`RunExecutor` 直接
  调用原版模型层入口；战斗由 `CombatExecutor` 执行，边界导入原版 `Battle` 比较。

两边都不自己推演局外：每一步都从原版重新导入 `RunState`。离线宿主的细节见
[离线宿主](offline-host.md)，类名与目录见 [`src/OfflineHost`](../src/OfflineHost/README.md)。

## 3. 模式

保留三种模式：

| 模式 | 战斗 | 局外 | 用途 |
| --- | --- | --- | --- |
| 只托管战斗 | Blitz | 玩家 | 推荐/自动出牌 |
| 只给推荐 | Blitz 给建议 | 玩家 | 观察建议 |
| 全自动 | Blitz | `RunPilot` | 整局评估 |

R3-0 只写协议与类名；`RunPilot`/`IRunPilotHost` 在 R3b 落地，Mod 界面执行器在 R3c 落地，
策略在 R4，评估闭环在 R5。
