# 离线宿主

离线宿主的目标是在普通 .NET 9 控制台进程里加载原版 `sts2.dll`，由原版 `RunManager` 推进整局。
它不启动 Godot 可执行文件，也不需要场景树；战斗通过 `CombatPilot` 交给 Sts2Solver，局外每一步都从
原版重新导入 `RunState`。现已有独立 `OfflineHost` 库、共享 `RunPilot` 与真实 Map/Event/Rest/Shop/CardSelect/Treasure
执行。Runtime/NewRunFactory/Combat 已迁移，tools/run-eval 通过纯 DTO 接口从独立进程正常启动，
默认完成首场真实战斗并在领奖前以 Paused / first-combat-complete 停止；显式
--stop-after next-room 支持原版战后奖励/异步选卡/满药水管理及合法进入下一房间，
以通用 Paused / next-room-entered 停止。显式 --stop-after rooms --max-rooms N 在普通 Act1 处理多房间、
多场独立战斗奖励与正式商店，在第 N 个地图节点达到安全可旅行边界后停止，不计 Neow、不进入下一房。
独立入口按事件完成、休息同步器完成、商店 Leave 或已实际 Proceed 的战后奖励展示报告地图边界；
原版终局清理前捕获、逐步比较和结算后队列稳定均是完成的前提。
--stop-after act1 使用相同的可见规则，真实 Boss 胜利且原版奖励全部完成后停在换幕按钮之前，
Boss 奖励请求不包含本范围无法执行的换幕 Leave；不调用 ActChangeSynchronizer、不进入第二幕；这是 Paused / act1-complete，绝不是整局 Victory。
宝箱先由共享 RunPilot 选择 Open，执行原版正常/额外奖励后才显示遗物领取/跳过，最后 Leave。
事件 ChooseCard、变牌、附魔和移除保留原版过滤/自动选择/实际作用，仅托管真实异步选择回调。
当前 API、无界面设施差异与验证范围见
[src/OfflineHost](../src/OfflineHost/README.md) 和 [CLI](../tools/run-eval/README.md)。

## 1. 职责与目录

| 目录 | 职责 |
| --- | --- |
| `Runtime/` | 程序集解析、消息泵、内存存档、本地化、Godot UI 设施、NewRunFactory 与有界多房间会话 |
| `Combat/` | `NativePilotHost`、`NativeActionExecutor`、原版选择器与终局捕获/比较 |
| `Run/` | `OfflineRunHost`、`RunExecutor` 与真实局外选择执行 |

离线宿主只依赖 `Core`、`Import`、`sts2.dll`、`GodotSharp`、`0Harmony`；不引用 `TestHost`，
也不被 `Mod` 引用。原版整局推进，Sts2Solver 只拿到导入后的 `RunState`/`Battle` 与决策请求。

## 2. 完整宿主的目标启动顺序

1. 解析并加载游戏程序集（`AssemblyBootstrap`）；
2. 初始化原版静态状态与 Godot 旁路（`GameBootstrap`）；
3. 建立消息泵（`MainLoopContext`）与存档桩（`MemorySaveStore`）；
4. 由 `NewRunFactory` 建新局，或导入现有存档；
5. 循环：导入 `RunState` → `RunDecisionImporter` 生成请求 → 策略选择 → `RunExecutor` 执行 →
   进入战斗时交给 `OfflineCombatHost`/`CombatExecutor`。

每一步都从原版重新导入，不在离线宿主内维护第二份局外状态。

## 3. TestMode

离线宿主在初始化时设 `TestMode.IsOn = true`。实机不在全局打开 TestMode（会走 NRun/淡入淡出
的测试分支并异常退出），因此两边存在下列差别；差分只在这些差别不影响被比较的字段时才成立。

| 行为 | 离线 TestMode | 实机/原版 | 处理 |
| --- | --- | --- | --- |
| `TestMode.IsOn` | `true` | `false` | 只影响下列测试分支；不据此修改战斗语义 |
| `MerchantPotionEntry.CalcCost` | 不抽药水随机价，直接按稀有度基础价 | 在 `Shops` 流抽 `NextFloat(0.95,1.05)` | **不能只“调用原版”**：同一个方法在 `TestMode.IsOn=true` 下仍不消费 `Shops` RNG。MerchantRuntime 已在生成/补货的该同步调用周围局部设 `TestMode.IsOn=false`（prefix/finalizer），并在异常路径恢复 `true`；不能全局关闭 TestMode |
| `MerchantRoom.Exit` 的地图历史 | 跳过 `CardChoices`/`RelicChoices`/`PotionChoices` 的 `wasPicked=false` 写入 | 写入这些玩家统计 | `RunState` 不保存选择历史；MerchantRuntime 已在该同步方法周围以 prefix/finalizer 切换 TestMode，保留原版真实写入 |
| `RunManager.ClearScreens` 等 UI 调用 | 无界面，直接返回 | 关闭原生界面 | 离线宿主不读界面；选择通过模型层执行 |
| 教学发现顺序 | 默认 `ShouldApplyTutorialModifications()==false` | 实机测试会话用补丁同样返回 false | 两边都走 `UpFront` RNG 分支，第三幕 Boss 结构对齐 |
| 战后奖励与选卡 | testSelector 受会话租约控制展示；选卡使用原版 OnSelect 的 awaited UI 通道托管桥 | 原生界面 | 生成/同步器/发牌/hook/history/Reroll 保持原版；terminal Proceed 与 EnterMap 清理顺序一致 |
| 战斗内选择 | 测试 `ICardSelector`/`CardSelectCmd` 选择器直接给答案 | 原生界面 | 选择器只回答原版请求，不改变结算 |
| 进度保存 | `MemorySaveStore` 桩，保存失败只记录 | 写真实存档 | 不影响 `RunState` 与战斗状态 |

TestMode 是进程级全局设置；任何需要真实游戏分支的对照都必须局部切换并在结束后恢复，不能
整体关闭 TestMode，否则会进入需要 Godot/UI 的路径。R3b 的商店价格分支尤其要保证
`finally` 恢复，否则一次异常会让后续所有局外步骤继续跳过 `Shops` RNG。

## 4. 验证

- 进程内差分（[`tests/Differential`](../tests/Differential/README.md)）：`--run-host-compare` 使用正式库执行自然 Neow→地图→首战，以及真实 Smith 异步父子链，与独立原版调用比较。
- 两个宿主之间的一致性：同一种子、同一策略，逐步比较导入的 `RunState`。
- 实机：Mod 在真实游戏里跑同一策略，比较战斗逐动作边界。

R3-0 的差分结果与 `683bc03` 相同；最后一次包含完整局外模拟器的提交是 `683bc03`。

正式有界会话对照入口 --rooms-production 4 / --rooms-reference 4 在独立进程使用生产实现与旧 runtime、
旧 NativePilotHost/原版直接推进，逐房比较完整 RunDump、NativeRunDump（含 RNG）、完整 MapPointHistory
与全部战斗证据。--shop-host-checks、--multiroom-host-checks、--postcombat-host-checks 分别覆盖
商店、休息、跨房多次奖励租约的生产库生命周期。

## 5. R3a 可运行探针

普通 .NET 原版流程探针已放在 `tests/Differential/Run/RunProbe*`，使用说明见
[Differential](../tests/Differential/README.md#r3a-原版整局可行性探针)。它复用原版正常启动、公开房间
与选择入口、现有逐动作 CombatPilot 核对，逐边界导入RunState；每局独立子进程，未知/错误/预算
都非零。它仍是测试探针，不是正式 `src/OfflineHost` 实现。

R3a已经暴露两类额外边界：事件悬浮说明可能触发Godot原生层；Trial.Accept有无TestMode保护的
肖像UI调用。探针只绕过展示，保留原版页生成与结算。存档使用公开InitProfileId(1)配MemorySaveStore，
把原版路径拼接桥接成内存路径，保存逻辑继续运行。药水商店价格的真实分支由仅作用于CalcCost的
prefix/finalizer局部切换，独立夹具核查Shops消费及true/false入口正常/异常恢复。

TestMode仍无法覆盖所有选择屏幕：RelicSelectCmd直接打开Godot遗物选择界面，R3a显式拒绝；
CardSelectCmd的bundle测试分支自动选第一组，探针观察并记录完整候选。未知内容不跳过，具体已测
自然路径、定向夹具和阻塞必须分开统计；目前不能据此宣称全内容或三幕通关已验证。

R3a曾定位原始RunCodec跨进程字节差异为RunRef隐式对齐字节，当时保留原始hash，另用
probe-only语义hash诊断。后续独立Core修复已在编码输出边界规范化RunState全部隐式padding，
保留v3布局与旧输入兼容；见[整局状态布局](run-state-layout.md)第5节。探针的semanticStateHash
现仅为正式stateHash的兼容别名，复跑须严格比较完整原始hash轨迹。
