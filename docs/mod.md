# Mod 入口、无头捕获与实机对照

[返回文档索引](README.md)

本页说明 `src/Mod` 的职责、打包方式、RitsuLib 入口、无头测试协议、`tools/testing/run-headless.sh`
的用法与实机对照边界。原生状态如何变成 `Battle`/`Rules`/评估配置见 [原生状态导入](import.md)；
离线宿主的对照模式见 [测试与验证](testing.md) 第 6 节。

本批只做 M2b-0：Mod 能被游戏加载、在无头游戏里进入 fixture 战斗、可出牌时捕获并导入。
**不搜索、不部署、没有界面。**玩家正常游戏时本版不执行任何游戏逻辑。

## 1. 目录与依赖

| 路径 | 职责 |
| --- | --- |
| `src/Mod/BlitzSolver.csproj` | `Godot.NET.Sdk/4.5.1` Mod 工程；不加入 `BlitzSolver.sln` |
| `src/Mod/Entry.cs` | 游戏 `ModInitializer` 入口；检测 CombatSolver；注册补丁与生命周期 |
| `src/Mod/TestSession.cs` | 无头测试循环：请求、建局、捕获、写结果、返回主菜单 |
| `src/Mod/GodotCombatWaiter.cs` | 主线程建局与 Play 阶段等待 |
| `src/Mod/Patches/` | 无头隔离、FTUE、回合准备 Start 卡牌捕获 |
| `src/TestHost/` | Mod 与 `tests/OfflineHost` 共用的建局、注入与原生状态转储 |
| `tools/testing/run-headless.sh` | Linux 隔离实例、请求、启动、轮询与清理 |

依赖方向：`Mod` 编入 `TestHost`、`Import` 与 `Core`；`TestHost → Import → Core`。`src/TestHost`
是独立的测试宿主层（会写游戏状态），是两侧唯一的一份建局与注入实现；`Mod` 不复制
`ScenarioBuilder`/`OfflineRequest`，只提供游戏侧的 `ICombatHost` 实现
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
`BlitzSolver.Import.dll` 或 `BlitzSolver.TestHost.dll`。
打包目录只包含：

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

## 4. 入口与共存

入口使用游戏 `[ModInitializer]`，初始化时：

1. 检查 `ModManager.Mods` 中是否存在 `CombatSolver`（`state` 为 `None` 或 `Loaded`）。
   存在时只写一行
   `[BlitzSolver] DISABLED reason=CombatSolver present (coexistence policy: no patches, no events)`
   后返回；不注册补丁、不订阅生命周期。
2. 不存在时用 RitsuLib 的 `CreatePatcher`/`ApplyRequiredPatcher` 注册补丁；注册
   `CombatStartingEvent`/`CombatEndedEvent` 生命周期。普通游戏时这两个回调是空操作。
3. 只有设置环境变量 `BLITZ_TEST_DIR` 时才进入测试模式，并在 `GameReadyEvent` 后启动测试
   循环。普通玩家不设置该变量，本版没有任何运行时行为。

## 5. 无头测试协议

协议是文件式的，一侧是启动脚本，一侧是游戏内 Mod：

```text
$BLITZ_TEST_DIR/
  requests/<root>.json              # OfflineRequest，文件名即结果根名
  results/<root>/
    BATL                            # BattleCodec.Encode
    RULE                            # RulesCodec.Encode
    SREQ                            # SearchCodec.EncodeRequest
    native.txt                      # NativeStateDump
    status.json                     # ok/failed、stage、耗时、准备选择
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
  `EvaluationConfigCompiler.CompileRequest`，写四个编码文件后再写 `status.json`。
- 捕获前等待 1.5s，让回合开始的可视/计时协程（例如 `FakeHappyFlower` 的 1 秒激活）结束。
  这些协程会临时改变原生公开状态；离线宿主在消息泵里会立即走完，实机若不等，会把瞬态的
  `DisplayAmount` 写进 `NativeStateDump`，与离线结果不一致。
- 回合准备 fixture：`TurnSetupRootCapture` 的 RitsuLib 补丁在 `SetupPlayerTurn` 入口记录
  Start 卡牌集合；捕获时 `skipInitialInjection: true`、`ImportOptions.TurnSetupRoot = true`、
  `RootCards = StartCards`，导入第一个原生准备选项对应的根。`SREQ` 只做
  `scope=subparts`（评分/下界子部件）比较：完整 `config.br` 需要枚举同一 fixture 的全部准备
  根并按 GOLD 顺序排列，这一批不重建该顺序。
- 前缀根（`flashPrefixActions`/`flashPrefixPlan`）在实机测试模式显式拒绝；它们继续由离线
  宿主的 `PrefixReplay` 覆盖。
- 无头隔离补丁：测试中不写战斗胜利进度；无窗口时不创建战斗基础教学界面。两者都在
  `TestSession.Active` 为 false 时直接放行。

## 6. 启动脚本

```bash
# 单根：生成请求、建实例、启动 --headless --force-steam=off、轮询结果、删除实例
tools/testing/run-headless.sh --instance m2b0-one --root kg2-b-stomp \
    --results .local/m2b0-one-results --cleanup-instance-on-exit

# 一根进程依次跑多个根
tools/testing/run-headless.sh --instance m2b0-20 \
    --roots-file .local/m2b0-20-roots.txt \
    --results .local/m2b0-20-results --cleanup-instance-on-exit

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

全量 397 个普通根（跳过 13 个前缀输入；约 40 分钟，默认 600s 不够，需显式放宽或分片）：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
    --emit-live-requests .local/m2b0-397-requests --all
tools/testing/run-headless.sh --instance m2b0-397 \
    --requests .local/m2b0-397-requests --results .local/m2b0-397-results \
    --cleanup-instance-on-exit --timeout-seconds 3600
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
    --live-compare .local/m2b0-397-results
```

## 7. 实机对照

离线宿主新增：

```bash
# 把 Mod 写出的结果与冻结快照对照；NativeStateDump 用离线宿主重建同一根后比较
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
    --live-compare .local/m2b0-20-results
```

`--live-compare` 对每个结果目录：

- `BATL`：解码后与 `d1/<root>/golden.br` 的期望根做 `BattleCodec` 字节比较（跳过公共头，
  与 `--import-compare` 相同）。回合准备 fixture 用 `status.setupSelections` 选对应 GOLD 根。
- `RULE`：与 `objects/rules/<sha>.br` 逐字节比较。
- `SREQ`：与 `d1/<root>/config.br` 逐字节比较；回合准备 fixture 只比较评分/下界子部件，
  输出 `sreq=subparts`。
- `native.txt`：离线宿主用同一请求重建原生根并调用同一份 `NativeStateDump.Capture`，逐字节
  比较。

汇总行是 `live roots=N same=... diff=... reject=... native=...`；任何 diff/reject 都非零退出。

## 8. 当前边界

- 不搜索、不部署、没有 UI；只捕获到可出牌的第一个玩家回合。
- 前缀根不在实机测试模式中执行；回合准备 fixture 只捕获第一个准备选项，`SREQ` 为子部件
  比较，不重建 GOLD 的多根排序。
- 未做 Windows `.ps1` 启动脚本、Steam Workshop 打包、Mod 设置页、第三方 Mod 兼容层。
- 实机捕获结果的 `RULE`/`SREQ` 仍依赖同一游戏版本与同一 RitsuLib 版本；版本不符时导入层会
  以稳定原因码拒绝，而不是返回默认值。
