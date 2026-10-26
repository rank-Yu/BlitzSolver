# Mod 入口、无头捕获与实机搜索

[返回文档索引](README.md)

本页说明 `src/Mod` 的职责、打包方式、RitsuLib 入口、无头测试协议、
`tools/testing/run-headless.sh` 的用法与实机对照边界。原生状态如何变成
`Battle`/`Rules`/评估配置见 [原生状态导入](import.md)；离线宿主的对照模式见
[测试与验证](testing.md) 第 6 节。

M2b-0 完成了“游戏加载 Mod、在可出牌时捕获并导入”；M2b-1 在捕获之后对根调用一次
`BeamSolver` 得到路线；M2b-2 把路线翻译成原版入口调用，逐动作核对，按回合对账，并在路线
仍然有效时续用剩余动作。普通玩家用 `F8` 开关“本场全自动”，没有界面，状态只写日志。

## 1. 目录与依赖

| 路径 | 职责 |
| --- | --- |
| `src/Mod/BlitzSolver.csproj` | `Godot.NET.Sdk/4.5.1` Mod 工程；不加入 `BlitzSolver.sln` |
| `src/Mod/Entry.cs` | 游戏 `ModInitializer` 入口；检测 CombatSolver；注册补丁与生命周期 |
| `src/Core/Search/FullCombatDriver.cs` | Mod 与离线工具共用的整场决策循环：搜索时机、路线所有权、用尽/失败约定与回合记账 |
| `src/Core/Engine/RouteReplay.cs` | 把 `PlayerAction[]` 回放成每个动作的 `Before`/`After` 与选择计划 |
| `src/Import/LiveCombatTracker.cs` | 实时导入：稳定敌人顺序、死亡/复活行与不可观测搜索账本 |
| `src/Mod/AutoCombatSession.cs` | 全自动会话生命周期、取消、主线程设置与结果转换；循环本身在 `FullCombatDriver` |
| `src/Mod/FullCombatHost.cs` | 共享驱动的 Mod 宿主：等待可出牌、实时导入、游戏内搜索、部署与边界投影 |
| `src/Mod/SearchSession.cs` | 主线程捕获→后台 `BeamSolver.Solve`→主线程收结果；取消、让帧与内存采样 |
| `src/Mod/ActionDeployer.cs` | 主线程动作部署；出牌/药水/结束回合；动作队列捕获与边界等待 |
| `src/Mod/PlannedNativeSelector.cs` | 原版 `ICardSelector` 驱动；选项身份与所在牌堆核对 |
| `src/Mod/BattleReconciler.cs` | `Battle` 编码的严格比较与墓碑/搜索账本归一化 |
| `src/Mod/SearchHotkeyNode.cs` | 普通玩家的 `F8` 全自动开关 |
| `src/Mod/TestSession.cs` | 无头测试循环：请求、建局、捕获、单次求解或整场自动战斗、写结果、返回主菜单 |
| `src/Mod/GodotCombatWaiter.cs` | 主线程建局与 Play 阶段等待 |
| `src/Mod/Patches/` | 无头隔离、教学发现顺序、FTUE、回合准备 Start 卡牌捕获 |
| `src/TestHost/` | Mod 与 `tests/OfflineHost` 共用的建局、注入与原生状态转储 |
| `tools/testing/run-headless.sh` | Linux 隔离实例、请求、启动、轮询与清理 |

依赖方向：`Mod` 编入 `TestHost`、`Import` 与 `Core`；`TestHost → Import → Core`。
`src/TestHost` 是独立的测试宿主层（会写游戏状态），是两侧唯一的一份建局与注入实现；`Mod`
不复制 `ScenarioBuilder`/`OfflineRequest`，只提供游戏侧的 `ICombatHost` 实现
（`NGame.StartNewSingleplayerRun` 与帧等待）。

整场循环的决策部分在 `src/Core/Search/FullCombatDriver.cs`，只依赖 `Engine`，因此离线工具
与 Mod 跑同一份搜索时机、路线使用和失败约定。`src/Mod/FullCombatHost.cs` 提供原生 I/O：
等待 Play 阶段、`LiveCombatTracker.ImportRoot`、`SearchSession`、`ActionDeployer` 与
`BattleReconciler`。`src/Import/LiveCombatTracker.cs` 放在 Import，是因为它只做实时根导入；
`src/Core/Engine/RouteReplay.cs` 放在 Engine，是因为它只回放内核动作，不读搜索配置。

Core 仍然只依赖 BCL。`Mod` 引用游戏程序集、Godot、Harmony 与 RitsuLib；只读导入/规则编译
的模型私有字段读取集中在 `src/Import`，`src/TestHost` 仅测试使用 publicizer 做建局、注入与
转储。Mod 不加入 `BlitzSolver.sln`，CI 不构建它。

## 2. 打包结论：一个 DLL

**结论：游戏的 Mod 加载器不能从 Mod 目录加载多个 DLL；本仓库发布单个 `BlitzSolver.dll`。**

依据：

1. 游戏反编译 `MegaCrit/Sts2/Core/Modding/ModManager.cs` 的 `TryLoadMod` 只计算
   `Path.Combine(mod.path, mod.manifest.id + ".dll")`，然后调用
   `AssemblyLoadContext.LoadFromAssemblyPath`。它没有枚举目录内的其它 DLL，也没有为 Mod
   目录注册依赖解析器。
2. `.NET` 的 `AssemblyLoadContext.Default.LoadFromAssemblyPath` 不会探测主 DLL 所在目录来
   解析依赖。用同目录两个 DLL 做的探针得到
   `FileNotFoundException: Could not load file or assembly 'Dep'`；把依赖放到宿主应用目录才
   能解析。
3. RitsuLib 自己的 `ModuleAssemblyResolver` 能从 `compat/`、`shared/` 加载它的多个程序集，
   那是 RitsuLib 内部的解析器，不改变游戏加载器的上述合同。BlitzSolver 不能依赖每个用户
   都安装同一套目录结构来解析 Core/Import。

因此 `src/Mod/BlitzSolver.csproj` 把 `src/Core/**/*.cs`、`src/Import/**/*.cs` 与
`src/TestHost/**/*.cs` 作为 `Compile` 源码一起编进 `BlitzSolver.dll`。`ProjectReference` 只建立
构建依赖顺序，`ReferenceOutputAssembly="false"`，不会复制 `BlitzSolver.Core.dll`、
`BlitzSolver.Import.dll` 或 `BlitzSolver.TestHost.dll`。打包目录只包含：

```text
BlitzSolver/
  BlitzSolver.json
  BlitzSolver.dll
  LICENSE
  README.md
```

PE 引用表只包含 BCL、`GodotSharp`、`sts2`、RitsuLib 与 `0Harmony`，没有
`BlitzSolver.Core`/`BlitzSolver.Import`/`BlitzSolver.TestHost` 程序集引用。RitsuLib 作为依赖由
用户安装，不随本仓库分发。

## 3. 清单与标识

`src/Mod/BlitzSolver.json`：

```json
{
  "id": "BlitzSolver",
  "name": "极速战斗求解器",
  "author": "rank-Yu",
  "version": "0.1.0",
  "has_dll": true,
  "min_game_version": "0.111.0",
  "dependencies": [{"id": "STS2-RitsuLib", "min_version": "0.6.0"}]
}
```

中文名「极速战斗求解器」，英文名 "Blitz Solver"（初始化日志带 `name=... english=...`）。
`affects_gameplay` 为 `true`：玩家按 `F8` 开启全自动后，Mod 通过原版动作队列写战斗动作；
不按 `F8` 时只加载入口与快捷键节点，不建局、不搜索、不部署。

构建：

```bash
dotnet build src/Mod/BlitzSolver.csproj -c Release
```

游戏程序集与 RitsuLib 路径来自被 git 忽略的 `src/Mod/local.props`；模板是
`local.props.example`。构建只读取游戏/RitsuLib 文件，绝不写入 `~/games/sts2/mods`；
打包产物在 `artifacts/mod/BlitzSolver/`。

## 4. 入口、触发与生命周期

入口使用游戏 `[ModInitializer]`，初始化时：

1. 检查 `ModManager.Mods` 中是否存在 `CombatSolver`（`state` 为 `None` 或 `Loaded`）。
   存在时只写一行
   `[BlitzSolver] DISABLED reason=CombatSolver present (coexistence policy: no patches, no events)`
   后返回；不注册补丁、不订阅生命周期。
2. 不存在时用 RitsuLib 的 `CreatePatcher`/`ApplyRequiredPatcher` 注册补丁；注册
   `CombatStartingEvent`/`CombatEndedEvent` 生命周期，并在 `NGame` 上挂 `SearchHotkeyNode`。
3. 只有设置环境变量 `BLITZ_TEST_DIR` 时才进入测试模式，并在 `GameReadyEvent` 后启动测试
   循环。普通玩家不设置该变量时仍会加载入口与快捷键节点，但不会自动建局或自动搜索。

搜索内存上限由 Mod 宿主按机器给出：默认取
`GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` 的 15%，下限 1 GiB，不设上限；容器与任务
对象限制已由该 API 计入。环境变量 `BLITZ_SEARCH_MEMORY_MB`（MiB）可覆盖默认值，非法值写
`SEARCH_MEMORY_IGNORED` 后回退默认。这个数字只经 `SearchRequestCompiler` 传给
`BeamOptions.SearchMemoryBudgetBytes`，不进入 `SREQ`/`SRES`；搜索核心不读取 GC 或系统
内存。超预算时搜索按节点上限同样的边界停止并交出当前最好路线，日志与 `full-combat.json`
的 `memoryBudgetExceeded`/`memoryExceededAtBytes` 记录触发点。

普通玩家的触发方式是 **`F8` 开关**：当前战斗处于本地玩家 `Play` 阶段时，`SearchHotkeyNode`
调用 `AutoCombatSession.Toggle`。第一次按下开始“本场全自动”，会话在每回合自动搜索并部署；
再按一次、战斗结束、预测偏差达到暂停上限或战斗退出时恢复手动。没有界面，全部状态写
`[BlitzSolver] AUTO_*`/`DEPLOY_*` 日志。测试模式下 `TestSession.Active=true`，`F8` 被显式
拒绝，避免与测试循环并发。

取消入口：

| 事件 | 行为 |
| --- | --- |
| `CombatEndedEvent` | `SearchSession.OnCombatEnded()` 取消搜索；`AutoCombatSession.OnCombatEnded()` 记录停止原因 |
| `CombatStartingEvent` | 取消上一场残留搜索（`combat_started`） |
| `NGame.TreeExiting` | `AutoCombatSession.Cancel("tree_exiting")` + `SearchSession.Cancel(...)`，覆盖退出/切场景 |
| 测试模式请求清理 | `AutoCombatSession.CancelAndWaitAsync` 与 `SearchSession.CancelAndWaitAsync` 都等待任务退出后才返回主菜单 |
| 测试请求 `searchCancelAfterMillisecondsForTest` | 单次搜索到点取消；整场测试时到点取消全自动会话 |

## 5. 线程与取消模型

一次实机搜索分成三段：

1. **主线程（捕获/组装）**：`RulesCompiler.Compile` → `BattleImporter.Import` →
   `SearchPolicyCapture.Capture`/`SearchPolicyOverrides.ApplyOverrides` →
   `SearchRequestCompiler.CompileRequest`。这一步只读原生状态，产出的
   `SearchCase` 只含定长 `Battle`、`Rules`、`Scorer`/`BoundFeatures` 的冻结数组与
   `BeamOptions`。
2. **后台 worker**：`Task.Run(() => new BeamSolver(...).Solve(...))`。worker 不持有
   `CombatState`/`Player`/`CardModel` 等 live 对象，也不读取任何游戏全局状态。中档正常搜索
   附加 `SearchWorkPacer`，主线程每帧用 `SearchFramePressureSignal.ObserveFrame` 喂帧间隔，
   pacer 按 `ScoreWeights` 的工作片与帧压力让出；确定性测试请求不附加 pacer，以便与离线
   重放逐字节一致。
3. **主线程（收结果）**：worker 完成后，`RunAsync` 的 Godot 同步上下文继续在主线程执行；
   代码显式断言 `NGame.IsMainThread()`。此时才编码 `SRES`、写 `search.json` 与日志。路线
   摘要只写日志（`SEARCH_DONE`/`SEARCH_ROUTE`），不修改战斗。

取消通过 `CancellationTokenSource`：`BeamSolver` 在每个父状态/动作/回合层检查
`ThrowIfCancellationRequested`，不轮询游戏状态。全自动会话的取消令牌只在主线程动作之间
检查；已经入队的原版动作会走完并等待稳定边界，不会把半执行动作留在游戏里。取消会同时取消
当前 worker 并让会话在下一个等待点停止；之后 `CancelAndWaitAsync` 等待两段任务都退出。
取消后日志带 `worker_exited=true/false`；测试模式会在离开当前根前等待 worker 退出。取消的
搜索仍能报告已展开节点数（`BeamSolver.ExpandedNodes`/`TransitionCount`）。

内存采样在主线程帧循环里做：`GC.GetTotalMemory(false)` 与 `Environment.WorkingSet` 的峰值，
同时记录 `GC.GetTotalAllocatedBytes` 的增量。采样只读进程/GC 计数，不影响搜索路线。

## 6. 无头测试协议

协议是文件式的，一侧是启动脚本，一侧是游戏内 Mod：

```text
$BLITZ_TEST_DIR/
  requests/<root>.json              # OfflineRequest，文件名即结果根名
  results/<root>/
    BATL                            # BattleCodec.Encode
    RULE                            # RulesCodec.Encode
    SREQ.capture                    # 冻结评估配置请求（M2b-0 的 SREQ）
    SREQ                            # 完整搜索请求（主 Beam，确定性时写）
    SRES                            # SearchCodec.EncodeResult（确定性求解成功时写）
    native.txt                      # NativeStateDump
    search.json                     # 单次搜索状态、耗时、节点/转移、内存峰值
    full-combat.json                # 整场测试：胜负、回合、HP、搜索/沿用/偏差/失败与逐次日志
    status.json                     # ok/failed、stage、搜索/整场状态与指标、准备选择
  results/done.json                 # 请求数、成功/失败数、总耗时、逐根结果
  mod.log                           # Mod 自己的日志
```

- 请求格式与 `tests/OfflineHost` 的 `OfflineRequest` 相同；`GeneratedScenarioPath` 必须是
  绝对路径。`run-headless.sh --root/--roots-file` 调用 `OfflineHost --emit-live-requests`
  生成请求，质量语料的 spec 路径会自动变成绝对路径。
- 一个游戏进程按文件名顺序处理多个请求；每根结束后调用 `NGame.ReturnToMainMenu`，等
  `RunManager` 空闲、主菜单出现、`CombatManager` 无状态后，再建下一局。
- 建局、注入、等待 Play 与离线宿主共用 `src/TestHost/ScenarioBuilder.cs`；Mod 只替换
  `ICombatHost.StartRunAsync` 为 `NGame.StartNewSingleplayerRun`，以及把离线消息泵换成 Godot
  帧等待。
- 捕获在主线程完成：`RulesCompiler.Compile` → `BattleImporter.Import` →
  `EvaluationConfigCompiler.CompileRequest`（写 `SREQ.capture`）→
  `SearchRequestCompiler.CompileRequest`（写 `SREQ`）。
- 整场测试由 `fullCombatForTest=true` 开启：捕获根后立刻进入全自动，用请求的
  `searchTier`（默认中档）和软时间做正常搜索，直到胜利、失败、暂停或取消。`status.json` 的
  `fullCombat*` 字段与 `full-combat.json` 记录胜负、结束回合、剩余 HP、药水使用、搜索次数、
  沿用回合数、偏差次数与字段差异、部署失败次数与原因、总耗时。
- `headlessFastModeForTest` 在整场测试中覆盖战斗动画速度（默认 `Instant`，跑完恢复原值）；
  其余模式不变。
- 单次搜索的选项由请求控制：
  - `searchTier`：`Low`/`Medium`/`High`/`VeryHigh`，默认 `Medium`。
  - `deterministicSearchForTest`：默认 `true`。去掉软时间（`TimeSpan.MaxValue`），只保留
    节点上限，求解后写 `SREQ`/`SRES`，供离线逐字节重放。
  - `false` 时使用该档位的正常软时间，带 pacer，只写 `search.json` 的耗时与内存，不写
    `SREQ`/`SRES`；用于“中”档重根测量。
  - `searchCancelAfterMillisecondsForTest`：单次搜索到点取消；整场测试时作为全自动取消
    时点。测试把 `searchStatus=cancelled`/`fullCombatStatus=cancelled` 与任务已退出视为预期
    的取消验证。
- 回合准备 fixture 的 `SREQ.capture` 仍只做评分/下界子部件比较；本批不对准备根做单 Beam
  搜索，`searchStatus=skipped`。前缀根在实机测试模式显式拒绝。
- 捕获前等待 1.5s，让回合开始的可视/计时协程（例如 `FakeHappyFlower` 的 1 秒激活）结束。
  这些协程会临时改变原生公开状态；离线宿主在消息泵里会立即走完，实机若不等，会把瞬态的
  `DisplayAmount` 写进 `NativeStateDump`，与离线结果不一致。
- 无头隔离补丁：测试中不写战斗胜利进度；无窗口时不创建战斗基础教学界面。两者都在
  `TestSession.Active` 为 false 时直接放行。

### 6.1 建局幕结构与离线对齐（M2b-0b）

实机与离线曾出现 5 个第三幕 A10 Boss 根的 `BossHpRelief` 差异。根因不在导入器，而在
`RunManager.GenerateRooms` 的教学发现顺序分支：

- 离线宿主 `GameBootstrap` 在初始化时设 `TestMode.IsOn = true`，实机测试模式没有。
  `RunManager.ShouldApplyTutorialModifications()` 因此离线返回 false、实机返回 true。
- 实机为 true 时，`RunManager.GenerateRooms` 调用
  `ActModel.ApplyDiscoveryOrderModifications(UnlockState)`；`Glory.BossDiscoveryOrder` 为
  `QUEEN_BOSS`、`TEST_SUBJECT_BOSS`、`AEONGLASS_BOSS`，空进度下 `ActModel._rooms.Boss`
  被覆盖为 `QUEEN_BOSS`。
- 紧接着 `RunManager.GenerateRooms` 用
  `AllBossEncounters.Where(e => e.Id != act.BossEncounter.Id)` 选 `SecondBossEncounter`；
  第一个 Boss 被覆盖后，排除集合随之改变，`Act.SecondBossEncounter` 与离线不同。
  `ScoreCompiler.ResolveNativeHpRelief` 只是如实读取 `Encounter.RoomType`、
  `RunState.CurrentActIndex`/`Acts.Count` 与 `Act.SecondBossEncounter`。
- 存档/解锁状态、`RunState.CreateForNewRun`、`SetUpNewSingleplayer`、RNG 消费顺序与
  `EnterRoomDebug` 都不是分叉点：修复前的 5 根原生转储显示两侧 `run.unlock.*` 与
  `rng.UpFront.*` 相同，只有 `run.act.boss`、`run.act.secondBoss` 与
  `run.tutorial.discoveryOrder` 不同。

修法是不在实机全局打开游戏 `TestMode`（实机会走 NRun/淡入淡出的测试分支并异常退出），
而是在测试会话中注册 `UnattendedNoDiscoveryOrderPatch`：仅当 `TestSession.Active` 时让
`RunManager.ShouldApplyTutorialModifications()` 返回 false，使实机与离线走同一条
`GenerateRooms` 分支，第三幕首个与第二个 Boss 都由 `UpFront` RNG 决定。
`NativeStateDump` 相应记录 `run.act.index/count/id`、`run.act.boss/secondBoss`、
`run.room.type/id`、`run.tutorial.discoveryOrder`、`run.unlock.*` 与 `rng.UpFront`，
让这类幕结构偏差在原生层直接可见。

## 7. 搜索请求与档位

`SearchRequestCompiler`（`src/Import`）从已经导入的根、`Rules`、`SearchPolicy`、根事实与
性能档位组装 `SearchCase`。它只读，不写游戏状态。字段来源与默认值：

| 字段 | 取值 |
| --- | --- |
| `BeamWidth` / `MaxExpandedNodes` / `SoftTimeBudget` | 档位表；确定性测试把软时间改成 `TimeSpan.MaxValue` |
| `MaxTurns` | 64（主搜索成员固定） |
| `InitialPlayerMaxHp` | 导入根的玩家最大 HP |
| `Scorer` / `Bound` | `EvaluationConfigCompiler.CompileScorer`/`CompileBound` |
| `Ordering` | 根当前 HP/最大 HP + `ScoreRules.BossHpRelief`/`PostCombatRelicHeal` |
| `Potions` / `EffectivePotionPolicy` | `Smart`，无指令、无最早回合/使用次数限制 |
| `InitialPlayerTurn` / `InitialSearchTurn` | 导入根的 `PlayerTurn` |
| `InitialActionCount` | `EvaluationRootFacts.InitialActionCount`（普通根 0） |
| `TheftPolicy` | `SearchPolicy.TheftPolicy`（盗贼遭遇时 `PreserveResources`） |
| `Act3BossStrategy` | `SearchPolicy.Act3BossStrategy` |
| `PreserveReplayAllocatorOpening` | 未融化的 `ThrowingAxe` 且本场未使用 |
| `AmbergrisPotionId` | `ScoreRules.AmbergrisPotionId` |
| `SecondRankBandWidth` | 档位 Beam 宽度；`SecondRankBand=false` |

档位：

| 档位 | Beam 宽度 | 节点上限 | 软时间 | 分支上限（卡/堆/手） |
| --- | ---: | ---: | ---: | --- |
| 低 | 180 | 240,000 | 60 秒 | 24 / 12 / 16 |
| 中 | 240 | 480,000 | 120 秒（`SolverSearchProfile.Default`） | 32 / 18 / 24 |
| 高 | 360 | 1,000,000 | 180 秒 | 48 / 28 / 36 |
| 极高 | 540 | 2,000,000 | 300 秒 | 72 / 42 / 54 |

Blitz 的 `BeamOptions` 没有分支上限字段；Flash 路径本身也固定使用 32 个选择分支，Blitz
`BeamSolver.MaxChoiceBranches=32` 与之相同，因此这三列只记录档位来源，不进入请求。其余
未显式设置的高级成员保持默认：`CandidateScanCount=0`、`Incumbent=null`、`HpTargetStop=null`、
`PotionFreeBaseline=null`、`SmartPotionEligibilityHpCeiling=null`、`AuditPotionPolicy=false`、
`ForcedPotionDirectives=None`、`MinimumPotionUses=0`、`PlanCommitment=null`、
`EarlyTurnScoutDepth=0`、`Novelty=null`、`PowerCommitments=null`、`SecondRankBand=false`、
`AggressivePowerCommitment=false`。完整来源表与理由见 M2b-1 报告
（`.local/tool-tasks/blitz/M2b1-report.md`）。

## 8. 启动脚本与命令

```bash
# 单根：生成请求、建实例、启动 --headless --force-steam=off、轮询结果、删除实例
tools/testing/run-headless.sh --instance m2b1-one --root kg2-b-stomp \
    --results .local/m2b1-one-results --cleanup-instance-on-exit

# 一根进程依次跑多个根（默认中档确定性求解）
tools/testing/run-headless.sh --instance m2b1-20 \
    --roots-file .local/m2b1-20-roots.txt \
    --results .local/m2b1-20-results --cleanup-instance-on-exit --timeout-seconds 480

# 共存检查：实例里同时放 CombatSolver，只等初始化/禁用日志
tools/testing/run-headless.sh --instance m2b0-coexist --coexist-combat-solver \
    --probe-only --cleanup-instance-on-exit
```

脚本在 `.local/headless-instances/<实例>` 下复制整份游戏、RitsuLib、打包好的 BlitzSolver
与种子 `XDG_DATA_HOME`；默认不复制 CombatSolver。`--cleanup-instance-on-exit` 用 trap 在
正常/异常/信号退出时终止自己启动的进程并删除整个实例，随后打印
`INSTANCE_REMOVED path=...`。没有该参数时保留实例供排查。

`--requests DIR`/`--request FILE` 可直接提供已生成的请求；`--probe-only` 只验证 Mod 初始化
或共存禁用；`--no-launch` 只准备实例。Windows 的 `.ps1` 入口是后续项，本批不提供。

为测量/取消生成请求时使用 `--emit-live-requests` 的搜索选项：

```bash
# 正常中档（带 120s 软时间），用于耗时与内存
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --emit-live-requests .local/heavy --root quality-REGENT-013 \
  --search-tier Medium --search-normal

# 到 1s 取消，用于验证取消不残留
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --emit-live-requests .local/cancel --root quality-REGENT-013 \
  --search-tier Medium --search-normal --search-cancel-after-ms 1000
```

`--search-tier Low|Medium|High|VeryHigh` 默认 `Medium`；`--search-normal` 关闭确定性；
`--search-cancel-after-ms` 只用于测试。整场自动战斗请求加 `--full-combat`：

```bash
tools/testing/run-headless.sh --instance m2b2-full-1 \
  --roots-file .local/full-1.txt --full-combat \
  --results .local/m2b2-full-1-results \
  --cleanup-instance-on-exit --timeout-seconds 480
```

脚本把 `--roots-file/--root` 与 `--full-combat`/`--search-tier` 一起传给
`OfflineHost --emit-live-requests`；整场请求固定 `deterministicSearchForTest=false`，搜索使用
正常档软时间。

## 9. 部署、逐步核对与回合对账

整场循环的决策段由 `FullCombatDriver`（`src/Core/Search/FullCombatDriver.cs`）持有：它决定
何时搜索、沿用哪条路线、路线何时算用完，以及连续失败/回合上限的暂停。`Mod` 的
`FullCombatHost` 只在驱动要求时做原生 I/O；`OfflineHost` 的 `--offline-combat` 用同一个
驱动和同一个 `ReplayedRoute` 在内核里执行。新增搜索时机、加宽参数或重搜策略只改驱动与请求
编译入口，离线测到的就是实机跑的。

### 9.1 路线回放

`ReplayedRoute.Build`（`src/Core/Engine/RouteReplay.cs`）从搜索根开始，用 `Kernel.Step`
重放 `BeamResult.BestRoute`：

- 每个动作先用“0 个选择”的副本执行，拿到 `NeedsChoice` 的实时 `ChoiceRequest`；再逐个追加
  路线里的位集，直到动作完整完成。这样每个选择都保留请求时的选项卡值与所在牌堆。
- `Before[i]`/`After[i]` 是动作前后的预测 `Battle`；末尾结局必须与 `BestIsVictory` 一致。
- 动作携带的选择数、结局或位集不自洽时，整条路线作废并重新搜索，不部署半条路线。

### 9.2 动作翻译与选择

部署只在主线程、通过原版入口执行：

| 路线动作 | 原生入口 | 部署前核对 |
| --- | --- | --- |
| `PlayCard` | `CardModel.TryManualPlay(Creature?)` + `PlayCardAction` | 回合/轮次/能量/星能/手牌数；手牌下标处的卡 id+升级；目标 `CombatId` 存活；`CanPlayTargeting` |
| `UsePotion` | `PotionModel.EnqueueManualUse(Creature?)` + `UsePotionAction` | 槽位存在、药水 id 与预测一致、目标合法；`Self` 药水按原版规则用玩家自己补目标 |
| `EndTurn` | `CombatManager.OnEndedTurnLocally()` + `EndPlayerTurnAction` | 当前仍是预测的玩家回合 |

选择使用原版 `CardSelectCmd` 的 `ICardSelector` 路径（`PlannedNativeSelector`），无头与可见
游戏走同一条 `CardSelectCmd` 代码。每次 `GetSelectedCards`：

1. 取计划里的下一个显式选择；选项数必须等于预测 `ChoiceRequest.OptionCount`。
2. 按卡牌 id+升级把预测选项逐项映射到原生 `CardModel`，并核对它在
   `ChoiceRequest.Source` 牌堆中。抽牌堆选牌在游戏里会按稀有度/id 排序，因此只按身份映射，
   不按下标猜。
3. 生成牌选择（Discovery、药水等）按生成卡 id+升级映射；`SelectOption` 同样按 id。
4. 选择数量必须落在原生界面给出的实时上下界内；对不上抛
   `ChoicePlanMismatchException`，会话暂停并清空路线，不用空选择或默认选择继续。

原生会在“选项数 ≤ 最小选择数且不要求手动确认”时自己选满、不调用 `ICardSelector`。路线
回放把这部分标成 implicit，不放进原生选择队列；其余选择都必须被游戏实际请求。若预测动作
本身是终局（例如 Snap 的 Osty 攻击击杀最后一个敌人），原生在 `IsOverOrEnding` 后直接返回空
选择、不请求选择器；此时未消费的计划选择不再算失败，因为战斗已结束且没有后续边界比较它。

### 9.3 逐步核对

每执行一个动作：

1. 等原版动作 `CompletionTask` 结束，再等动作队列空闲和预测的稳定边界（玩家 `Play` 阶段或
   战斗结束）；回合开始额外等 1.5s，让回合开始协程（例如 `FakeHappyFlower` 的计时器）结束。
2. `LiveCombatTracker` 从当前原生状态导入 `Battle`。稳定敌人顺序在会话开始时冻结；之后
   新召唤的敌人追加，死亡敌人保留墓碑行，预测为 `Reviving` 的敌人按复活行导入。
3. `BattleReconciler` 编码两侧 `Battle` 并比较。不同就记录 `ACTION_DEVIATION`/字段差异，
   丢弃本回合剩余部署，从当前实际状态重新搜索；相同才继续下一个动作。

严格比较前只做以下归一化，都是原生不可观测或搜索专用字段：

- `ShuffleEventCount`/`ShufflesCrossed`：旧搜索的打乱计数/计分派生值，原生没有对应字段；
  从最近预测边界携带，比较视图清零。
- `DetachedCards[*].Flags` 的 `PowerAfflictionChecked`：detached 卡是值快照，未进入战斗，
  原生没有这个一次性标记。
- 永久死亡敌人的 AI 节点/日志/寄存器，以及它们名下的 Power 行：原生在死亡移除后已重置或
  保留为不可达墓碑；两者都从比较视图移除，复活行不适用。
- `NIGHTMARE_POWER.Target`：原生为 null、内核/冻结导入投影为玩家；Power 本身不使用该字段。
- `Power.Flags.SkipNextTickFromMonster`：`ApplyFromMonster` 的求解器侧一次性跳过标记，原生
  `PowerModel` 没有该字段。只对不会进入 `TickDurations` 的 Power（如 ChainsOfBinding）清零：
  它们永远消费不到这个位；会衰减的 Power 仍严格比较。

TheBomb/Panache/ToricToughness 的 `Target` 与 SlimedBerserker Weak、BeltBuckle Dexterity 的
`Applier` 已在 M2c 按原版写成 `NoOwner`，不再经过比较投影。内核合并 Power 行只按
`(Owner, Id)` 查现有实例（`IsInstanced` 时新建），与原生 `FindExistingInstanceForStacking` 的
`PowerInstanceType.None`/`Instanced` 口径一致，不按 `Applier` 区分；这些行也不覆写
`ApplierDied` 或读取 `Applier`，因此 `NoOwner` 不改变合并与生命周期行为。

其余字段（HP、护甲、能量、星能、牌堆顺序、卡值、9 条 RNG、计数、Powers、Relics、Orbs、
药水、回合/侧/阶段、结局）都按 `BattleCodec` 字节比较，没有容差。

### 9.4 回合对账与续用

玩家新回合开始、状态稳定时，`FullCombatHost` 重新导入实际 `Battle`，由
`FullCombatDriver` 与路线里“上一动作之后”的预测边界比较：

- 相同且路线还有后续动作：直接沿用剩余路线，记一次 `TURN_REUSE`，不重新搜索；
- 不同：记一次 `TURN_DEVIATION`，字段差异写入 `full-combat.json` 的 `deviationLog`，从当前
  实际状态重新搜索；
- 路线已用完：重新搜索，直到战斗结束。

Blitz 精确模拟 9 条随机数流，所以预测正确时这些边界应当逐字节相同；不同就是模拟/导入/
部署问题的信号，不能靠放宽比较消掉。

### 9.5 失败约定

- 部署前校验失败、动作未入队、动作抛出/取消、选择对不上、边界超时、战斗提前结束：都不硬
  执行下一步；清空当前路线并从实际状态重新搜索。
- 同一回合连续 3 次预测偏差/部署失败：`AUTO_STOP`，交还手动；已执行的动作保留，已入队的
  原版动作走完。
- 搜索结果为空、路线回放不自洽、搜索失败：不部署半成品路线。
- 战斗结束、玩家退出、`NGame.TreeExiting` 或测试清理：取消搜索与全自动会话，等待两段任务
  退出；不留下后台 worker 或原生选择器。

## 10. 实机对照

### 10.1 捕获/冻结快照对照

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --live-compare .local/m2b1-20-results
```

`--live-compare` 对每个结果目录：

- `BATL`：解码后与 `d1/<root>/golden.br` 的期望根做 `BattleCodec` 字节比较（跳过公共头，
  与 `--import-compare` 相同）。回合准备 fixture 用 `status.setupSelections` 选对应 GOLD 根。
- `RULE`：与 `objects/rules/<sha>.br` 逐字节比较。
- `SREQ.capture`（旧结果没有时回退到 `SREQ`）：与 `d1/<root>/config.br` 逐字节比较；回合
  准备 fixture 只比较评分/下界子部件。
- `native.txt`：离线宿主用同一请求重建原生根并调用同一份 `NativeStateDump.Capture`，逐字节
  比较。

汇总行是 `live roots=N same=... diff=... reject=... native=...`；任何 diff/reject 都非零退出。

M2b-0b 复跑（同一游戏版本、同一 RitsuLib）：

```text
# 5 个第三幕 A10 Boss 根
live roots=5 same=5 diff=0 reject=0 native=5

# 20 个固定普通根 + 3 个回合准备 fixture
live roots=23 same=23 diff=0 reject=0 native=23
```

`qf2-gradient-failure`、`ts-a-toasty`、`ts-b-toolbox` 的 `SREQ` 仍是 `sreq=subparts`，
其余 20 根为 `sreq=full`；`BATL`/`RULE`/`native` 全部相同。

397 个普通根全量（`--emit-live-requests --all`，4 条通道并行，构建与 M2b-0b 合并后的 `main` 相同）：

```text
live roots=397 same=397 diff=0 reject=0 native=397
```

### 10.2 搜索请求/结果对照

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --search-compare .local/m2b1-20-results
```

对每个 `deterministicSearch=true` 且 `searchStatus=ok` 的结果目录：

1. 解码游戏写出的 `SREQ`，用同一个 `BeamSolver` 离线重解，编码后与游戏 `SRES` 逐字节比较；
2. 用离线宿主自己重建同一原生根、导入、编译规则/评估配置，再按 `status.searchTier` 与
   `status.deterministicSearch` 调用同一个 `SearchRequestCompiler`，编码后与游戏 `SREQ`
   逐字节比较。

输出逐根 `search=same sreq=same sres=same`，汇总
`search-compare roots=N same=... diff=... reject=... skipped=...`。正常档测量结果（无
`SREQ`/`SRES`）计为 `skipped reason=measure-only`；回合准备根计为
`skipped reason=turn-setup-root`。

同一次 397 根运行里，每个根都做了确定性中档搜索：

```text
search-compare roots=397 same=393 diff=0 reject=1 skipped=3
```

`skipped` 是 3 个回合准备 fixture。`reject` 是 `sk-a-discard-sly`：搜索在根节点第一次展开时就以
内核不支持停止（`Kernel.Discard.cs`：被弃的 TACTICIAN+1 在自动打出前失去瞬态标记）。这是内核
语义缺口，不是捕获或请求编译的问题，留给后续语义批次。

## 11. 当前边界

- 已部署、逐步核对、回合对账并自动打完整场；没有界面，普通玩家只有 `F8` 开关与日志。
- 测试模式的整场请求使用正常档软时间；单次确定性搜索仍写 `SREQ`/`SRES` 供离线重放。
- 只做单根 `BeamSolver.Solve`；前缀根不在实机测试模式执行，回合准备根只捕获不搜索。
- 选择驱动覆盖原版 `CardSelectCmd` 的 `ICardSelector` 路径；不覆盖未来的非选牌模态请求，
  也不猜测未知选项。
- 搜索选项不覆盖 novelty、计划承诺、早期侦察、能力承诺与 incumbent；这些成员保持默认。
- `PowerCommitments`、`HpTargetStop`、药水审计等高级成员未接入，路线可能与本仓库外的
  CombatSolver 主搜索成员不同；本批的对照只要求游戏与离线宿主使用同一个请求编译器。
- 未做 Windows `.ps1` 启动脚本、Steam Workshop 打包、Mod 设置页、第三方 Mod 兼容层。
- 实机捕获与搜索依赖同一游戏版本与同一 RitsuLib 版本；版本不符时导入层会以稳定原因码
  拒绝，而不是返回默认值。
