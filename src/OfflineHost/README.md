# OfflineHost 局外执行库

`BlitzSolver.OfflineHost.csproj` 是普通 .NET 9 库，不加入 solution。它引用 Core、Import 与原版游戏、
GodotSharp、Harmony；不引用 TestHost，也不链接测试源文件。游戏路径沿用
`tests/Differential/GameReferences.props` 与被忽略的 `local.props`。

## 已实现

- `Run/OfflineRunHost` 实现 Core 的 `IRunPilotHost`：原版边界导入、会话/单调版本、完整回答和确认校验、
  snapshot 新鲜度及实例绑定、dispatch 前消费、取消与异步释放。
- `Run/RunExecutor` 执行原版 Map/Event/Rest。CardSelect 只接入真实 native 回调，并捕获完整 prefs；
  父动作等待 child 时，由同一个 RunPilot policy 回调异步回答，没有第二个策略循环。
- `Runtime/NativeCallbacks` 只迁移选牌 prefs 捕获与事件原始任务观察；补丁只安装一次，没有活动调用时不驱动选择。
  AsyncLocal 执行上下文和一次性 prefs 凭据限制 child 来源，公开 Execute 永远不能绕过 busy。
- 每次 root/child 请求都重新导入原版 RunState，再生成 RunView.WithRequest；策略只有可见投影。
  非法回答无 native 效果；失效的请求不会复活，合法 Cancel 在 adapter 边界变成 native 空选择。

全进程只允许一个 host。选牌 selector 只在局外 native 执行范围内持有，finally 恢复；战斗可使用
既有 selector。Dispose 取消 child 并等待原版父链结束后才释放 host 租约。不合作策略迟到的回答不能执行。
取消和异常不能撤销原版已经发生的效果；若嵌入 runtime 的其他原版异步任务不响应取消，Dispose 会继续等待，
不会先释放全局状态让新局与旧任务并发。

## 嵌入边界与未实现内容

调用方先初始化游戏静态状态/消息泵并建立真实 run/player，再向构造器传入异步边界提供器。
提供器只报告或等待信号，不推进原版流程；取消后不能遗留会推进原版的后台任务。
它必须明确报告地图可旅行信号、当前事件/休息室、战斗与终局事实；库尚未自主发现 UI readiness、
奖励、商店、战斗结束/胜利，也不从 HP 猜测整局胜利。等待中的提供器应遵循取消。

战斗 delegate 由 runtime adapter 接入既有 CombatPilot；Core 的内部 RunCombatHandoff 统一结果转换。
未注入战斗执行时明确返回 Paused / combat-boundary。首战边界不是终局胜利。

本库没有正式 CLI、NewRunFactory、完整 Runtime/Combat 搬迁、三幕通关、Mod UI 或推荐模式；
自动策略估值留待 R4。同步 CardReward selector 无法支持任意异步策略，显式 Unsupported，
不阻塞等答案。未接入的 CardSelect From*、RelicSelect 与 RewardsSet.Offer 在活动执行 scope 中也会显式拒绝，避免原版 TestMode 自动选择/领取。Rewards/Shop 正式边界与执行尚未接入，不能借用测试驱动器冒充支持。

## 验证

```bash
dotnet build src/OfflineHost -c Release
dotnet run --project tests/Differential -c Release -- --run-host-compare
```

新模式使用独立测试 runtime 初始化，实际调用本库执行器；自然 EnterAct→Neow→地图→首战第一回合
与同 seed 的独立原版调用比较 native dump、导入状态和 RNG。Smith 使用真实原版异步父子链，并用
未完成策略 Task 验证 success/Cancel、失效/外部取消/Dispose、迟到回答、重放、stale 与原始异常。
这些 directed fixtures 和 fake host 终局合同不是整局通关证据。

架构及宿主目标见[离线宿主](../../docs/offline-host.md)，共享协议见[自动游玩](../../docs/autoplay.md)。
