# Mod 入口、无头捕获与实机搜索

[返回文档索引](README.md)

本页说明 `src/Mod` 的职责、打包方式、RitsuLib 入口、无头测试协议、
`tools/testing/run-headless.sh` 的用法与实机对照边界。原生状态如何变成
`Battle`/`Rules`/评估配置见 [原生状态导入](import.md)；离线宿主的对照模式见
[测试与验证](testing.md) 第 6 节。

M2b-0 完成了“游戏加载 Mod、在可出牌时捕获并导入”；M2b-1 在捕获之后对根调用一次
`BeamSolver` 得到路线。**本批不部署**：不在游戏里执行路线动作，也没有界面。普通玩家可以用
`F8` 对当前可出牌战斗触发一次搜索，结果只写日志。

## 1. 目录与依赖

| 路径 | 职责 |
| --- | --- |
| `src/Mod/BlitzSolver.csproj` | `Godot.NET.Sdk/4.5.1` Mod 工程；不加入 `BlitzSolver.sln` |
| `src/Mod/Entry.cs` | 游戏 `ModInitializer` 入口；检测 CombatSolver；注册补丁与生命周期 |
| `src/Mod/SearchSession.cs` | 主线程捕获→后台 `BeamSolver.Solve`→主线程收结果；取消、让帧与内存采样 |
| `src/Mod/SearchHotkeyNode.cs` | 普通玩家的 `F8` 单次搜索触发器 |
| `src/Mod/TestSession.cs` | 无头测试循环：请求、建局、捕获、确定性/测量求解、写结果、返回主菜单 |
| `src/Mod/GodotCombatWaiter.cs` | 主线程建局与 Play 阶段等待 |
| `src/Mod/Patches/` | 无头隔离、FTUE、回合准备 Start 卡牌捕获 |
| `src/TestHost/` | Mod 与 `tests/OfflineHost` 共用的建局、注入与原生状态转储 |
| `tools/testing/run-headless.sh` | Linux 隔离实例、请求、启动、轮询与清理 |

依赖方向：`Mod` 编入 `TestHost`、`Import` 与 `Core`；`TestHost → Import → Core`。
`src/TestHost` 是独立的测试宿主层（会写游戏状态），是两侧唯一的一份建局与注入实现；`Mod`
不复制 `ScenarioBuilder`/`OfflineRequest`，只提供游戏侧的 `ICombatHost` 实现
（`NGame.StartNewSingleplayerRun` 与帧等待）。

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
`affects_gameplay` 为 `false`：本版在正常游戏中不改变任何游戏状态。

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

普通玩家的触发方式是 **`F8`**：当前战斗处于本地玩家 `Play` 阶段时，`SearchHotkeyNode`
调用 `SearchSession.TryStartManual`，捕获一次、导入、组装中档请求并在后台求解一次。不会
每回合自动触发，也不会部署路线。测试模式下 `TestSession.Active=true`，`F8` 被显式拒绝，避免
与测试循环并发。

取消入口：

| 事件 | 行为 |
| --- | --- |
| `CombatEndedEvent` | `SearchSession.OnCombatEnded()` 立即 `Cancel("combat_ended")` |
| `CombatStartingEvent` | 取消上一场残留搜索（`combat_started`） |
| `NGame.TreeExiting` | 取消搜索（`tree_exiting`），覆盖退出/切场景 |
| 测试模式请求清理 | `CancelAndWaitAsync` 取消并等待 worker 退出后才返回主菜单 |
| 测试请求 `searchCancelAfterMillisecondsForTest` | 到点主动取消，用于验证取消不残留 |

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
`ThrowIfCancellationRequested`，不轮询游戏状态。取消只停止 worker，不触碰 live 战斗。
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
    search.json                     # 搜索状态、耗时、节点/转移、内存峰值
    status.json                     # ok/failed、stage、搜索状态与指标、准备选择
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
- 测试模式的搜索选项由请求控制：
  - `searchTier`：`Low`/`Medium`/`High`/`VeryHigh`，默认 `Medium`。
  - `deterministicSearchForTest`：默认 `true`。去掉软时间（`TimeSpan.MaxValue`），只保留
    节点上限，求解后写 `SREQ`/`SRES`，供离线逐字节重放。
  - `false` 时使用该档位的正常软时间，带 pacer，只写 `search.json` 的耗时与内存，不写
    `SREQ`/`SRES`；用于“中”档重根测量。
  - `searchCancelAfterMillisecondsForTest`：到点取消；测试把
    `searchStatus=cancelled, workerExited=true` 视为预期的取消验证。
- 回合准备 fixture 的 `SREQ.capture` 仍只做评分/下界子部件比较；本批不对准备根做单 Beam
  搜索，`searchStatus=skipped`。前缀根在实机测试模式显式拒绝。
- 捕获前等待 1.5s，让回合开始的可视/计时协程（例如 `FakeHappyFlower` 的 1 秒激活）结束。
  这些协程会临时改变原生公开状态；离线宿主在消息泵里会立即走完，实机若不等，会把瞬态的
  `DisplayAmount` 写进 `NativeStateDump`，与离线结果不一致。
- 无头隔离补丁：测试中不写战斗胜利进度；无窗口时不创建战斗基础教学界面。两者都在
  `TestSession.Active` 为 false 时直接放行。

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
`--search-cancel-after-ms` 只用于测试。

## 9. 实机对照

### 9.1 捕获/冻结快照对照

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

### 9.2 搜索请求/结果对照

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

## 10. 当前边界

- 不部署：不在游戏里执行路线动作，不做跨回合续用对账，没有全自动。
- 没有界面；普通玩家只有 `F8` 单次触发，测试模式才有自动逐根求解。
- 只做单根 `BeamSolver.Solve`；前缀根不在实机测试模式执行，回合准备根只捕获不搜索。
- 搜索选项不覆盖 novelty、计划承诺、早期侦察、能力承诺与 incumbent；这些成员保持默认。
- `PowerCommitments`、`HpTargetStop`、药水审计等高级成员未接入，路线可能与本仓库外的
  CombatSolver 主搜索成员不同；本批的对照只要求游戏与离线宿主使用同一个请求编译器。
- 未做 Windows `.ps1` 启动脚本、Steam Workshop 打包、Mod 设置页、第三方 Mod 兼容层。
- 实机捕获与搜索依赖同一游戏版本与同一 RitsuLib 版本；版本不符时导入层会以稳定原因码
  拒绝，而不是返回默认值。
