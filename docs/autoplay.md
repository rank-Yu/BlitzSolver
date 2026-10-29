# 自动游玩

整局自动游玩由两个宿主驱动，共用同一套整局协议；局外都由原版推进，Blitz 只负责决策与战斗。
共享 `RunPilot`、`IRunPilotHost` 与正式 `OfflineRunHost`/`RunExecutor` 的局外执行基础已经实现。
真实自然链路验证到首个战斗边界，完整离线整局尚未实现。Mod 的整局宿主与推荐界面仍未接入。

## 1. 协议

- `RunPilot`：共享决策循环。每一步从宿主取得可见 `RunView` 与
  `RunDecisionRequest`，选择 `RunAction`，交给宿主执行；遇到战斗时调用 `CombatPilot`。
- `IRunPilotHost`：异步返回 Decision/Combat/Terminal 边界，执行局外动作并移交战斗；未知异常原样传播。
- `Ai.Run.IRunPolicy`：只接收 RunView，返回 RunPilotAnswer(Action, Confirmed)。需确认的 Select 必须明确授权；Cancel 不需要确认。
- 父动作中的 native 异步子请求调用同一个 Decide 回调，共用决策预算、trace、取消与策略；父 loop 无需等待自身处理子请求。
- `RunPilotResult` 区分 Victory/Death/Budget/Cancelled/Paused/Failed；步数限制重复战斗，child 同样计入决策预算。
  Terminal 的明确胜利事实优先于 HP，首战边界暂停始终是 Paused。
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

本批完成共享循环与局外执行库；Mod 界面执行器计划在 R3c，策略在 R4，评估闭环在 R5。
