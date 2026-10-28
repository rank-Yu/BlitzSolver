# 离线宿主

离线宿主的目标是在普通 .NET 9 控制台进程里加载原版 `sts2.dll`，由原版 `RunManager` 推进整局。
它不启动 Godot 可执行文件，也不需要场景树；战斗通过 `CombatPilot` 交给 Blitz，局外每一步都从
原版重新导入 `RunState`。R3-0 只建立目录与 README，离线宿主、`RunPilot`、`RunExecutor` 与
`NewRunFactory` 都还没有实现；整局尚未跑通，下面的启动顺序是 R3b 的设计。代码骨架见
[`src/OfflineHost`](../src/OfflineHost/README.md)。

## 1. 职责与目录

| 目录 | 职责 |
| --- | --- |
| `Runtime/` | 程序集解析、消息泵、存档桩、本地化与 Godot 旁路 |
| `Combat/` | `OfflineCombatHost`、`CombatExecutor`、原版选择器与战斗等待 |
| `Run/` | `OfflineRunHost`、`RunExecutor`、`NewRunFactory` |

离线宿主只依赖 `Core`、`Import`、`sts2.dll`、`GodotSharp`、`0Harmony`；不引用 `TestHost`，
也不被 `Mod` 引用。原版整局推进，Blitz 只拿到导入后的 `RunState`/`Battle` 与决策请求。

## 2. 启动顺序

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
| `MerchantPotionEntry.CalcCost` | 不抽药水随机价，直接按稀有度基础价 | 在 `Shops` 流抽 `NextFloat(0.95,1.05)` | **不能只“调用原版”**：同一个方法在 `TestMode.IsOn=true` 下仍不消费 `Shops` RNG。R3b 必须在生成/补货的该调用周围局部设 `TestMode.IsOn=false`（prefix/postfix 或等价 try/finally），并在异常路径恢复 `true`；不能全局关闭 TestMode |
| `MerchantRoom.Exit` 的地图历史 | 跳过 `CardChoices`/`RelicChoices`/`PotionChoices` 的 `wasPicked=false` 写入 | 写入这些玩家统计 | `RunState` 不保存选择历史；R3b 以原版真实写入为准 |
| `RunManager.ClearScreens` 等 UI 调用 | 无界面，直接返回 | 关闭原生界面 | 离线宿主不读界面；选择通过模型层执行 |
| 教学发现顺序 | 默认 `ShouldApplyTutorialModifications()==false` | 实机测试会话用补丁同样返回 false | 两边都走 `UpFront` RNG 分支，第三幕 Boss 结构对齐 |
| 战斗内选择 | 测试 `ICardSelector`/`CardSelectCmd` 选择器直接给答案 | 原生界面 | 选择器只回答原版请求，不改变结算 |
| 进度保存 | `MemorySaveStore` 桩，保存失败只记录 | 写真实存档 | 不影响 `RunState` 与战斗状态 |

TestMode 是进程级全局设置；任何需要真实游戏分支的对照都必须局部切换并在结束后恢复，不能
整体关闭 TestMode，否则会进入需要 Godot/UI 的路径。R3b 的商店价格分支尤其要保证
`finally` 恢复，否则一次异常会让后续所有局外步骤继续跳过 `Shops` RNG。

## 4. 验证

- 进程内差分（[`tests/Differential`](../tests/Differential/README.md)）：只用于导入与建局，
  不用于局外逐步对账。
- 两个宿主之间的一致性：同一种子、同一策略，逐步比较导入的 `RunState`。
- 实机：Mod 在真实游戏里跑同一策略，比较战斗逐动作边界。

R3-0 的差分结果与 `683bc03` 相同；最后一次包含完整局外模拟器的提交是 `683bc03`。
