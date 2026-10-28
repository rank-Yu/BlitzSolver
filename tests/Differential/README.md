# Differential

进程内差分宿主：在普通 .NET 9 控制台进程里初始化原版《杀戮尖塔 2》的静态状态，用原版
`RunManager`、`ModelDb` 与 `CombatState` 建局，再把原生状态导入 `Battle`/`RunState` 与
Blitz 的结果逐字节比较。它不启动 Godot 可执行文件，也不需要场景树。

R3-0 起局外由原版推进；本工程保留导入、建局、地图进点、战斗边界、规则/配置与战斗 pilot
的对照。局外整局运行在 `src/OfflineHost`（R3b）。

本工程不在 `BlitzSolver.sln` 中；CI 没有游戏文件，不会构建或运行它。

## 需要游戏文件

需要本机游戏安装目录，至少包含 `sts2.dll`、`GodotSharp.dll`、`0Harmony.dll`。路径通过本目录
下被 git 忽略的 `local.props` 配置；模板是 `local.props.example`。`local.props` 不要提交。

## 构建

```bash
dotnet build tests/Differential/Differential.csproj -c Release
```

产物在 `artifacts/bin/Differential/release/Differential.dll`。构建不读取游戏文件到仓库。

## 建一个根 / 全部根

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --root kg2-b-stomp --out .local/differential/out
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 8 --out .local/differential/out
```

每个根一个子进程，避免游戏静态状态跨根污染；`--jobs` 取值 1 到 8。

## 战斗导入与规则

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --root kg2-b-stomp --import-compare
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 8 --import-compare
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 8 --import-compare --compiled-rules
dotnet artifacts/bin/Differential/release/Differential.dll --root kg2-b-stomp --rules-compare
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 8 --rules-compare
dotnet artifacts/bin/Differential/release/Differential.dll --rules-table-check
dotnet artifacts/bin/Differential/release/Differential.dll --root kg2-b-stomp --config-compare
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 8 --config-compare
```

`--import-compare` 建原生状态、导入 `Battle`，与 `d1/<root>/golden.br` 的期望 `BATL` 逐字节
比较；`--compiled-rules` 改用 `RulesCompiler.Compile` 的输出。`--rules-compare` 比较
`RulesCodec.Encode`；`--config-compare` 比较 `config.br` 的完整请求与特征哈希。
`--import-negative` 验证私有字段/未认领状态/第三方内容三类拒绝。

## 内容表与 RulesBuilder

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --content-table-report
dotnet artifacts/bin/Differential/release/Differential.dll --rules-build-compare --build-kind d1 --build-jobs 8
dotnet artifacts/bin/Differential/release/Differential.dll --rules-build-compare --build-kind natural --build-seeds 20 --build-jobs 8
dotnet artifacts/bin/Differential/release/Differential.dll --rules-build-compare --build-kind encounter --build-jobs 8
```

`--content-table-report` 导出一次进程内内容表并打印覆盖、耗时与保留内存。
`--rules-build-compare` 对每个根同时走原生 `RulesCompiler` 与导入 `RunState`/`Battle` 后的
Core `RulesBuilder`，逐字节比较两份 `RulesCodec`。加 `--build-content` 汇总用到的卡牌等级与
怪物 id。

## 整局状态导入

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --run-import-compare --run-kind all --run-seeds 20 --run-jobs 8
dotnet artifacts/bin/Differential/release/Differential.dll --run-import-compare --run-kind natural --run-seeds 20 --run-jobs 8
dotnet artifacts/bin/Differential/release/Differential.dll --run-import-compare --run-kind d1 --run-jobs 8
dotnet artifacts/bin/Differential/release/Differential.dll --run-import-negative
```

自然开局（5 角色 × A0/A10 × 20 种子）与全部 410 个 D1 输入各起一个子进程：导入
`RunState`，与独立原生转储逐字段比较；每条随机流抽 64 次；比较当前幕地图；D1 还检查
9 条战斗流、HP、牌组、遗物与药水。

## 战斗边界建局

```bash
dotnet artifacts/bin/Differential/release/Differential.dll \
  --combat-setup-compare --setup-kind natural --setup-seeds 100 --setup-jobs 8
dotnet artifacts/bin/Differential/release/Differential.dll \
  --combat-setup-compare --setup-kind encounter --setup-seeds 20 --setup-jobs 8 --setup-shard 0 2
dotnet artifacts/bin/Differential/release/Differential.dll \
  --combat-setup-compare --setup-kind d1 --setup-seeds 4 --setup-jobs 8 --setup-shard 0 2
dotnet artifacts/bin/Differential/release/Differential.dll \
  --combat-setup-compare --setup-relic-matrix --setup-matrix-seeds 20 --setup-jobs 8
```

每个用例在独立子进程里：原版 `RunManager.EnterMapCoord` 到可出牌，导入 `Battle`、编译
`Rules`、转储进点后 `RunState`；Blitz 侧导入同一进点前 `RunState`，调用
`RunCombatSetup.EnterMapCoord`，用 `RulesBuilder` 建 `Rules`、`RunDump` 转储。比较
`Battle`/`RulesCodec` 逐字节与 `RunState` 逐字段。`--setup-cover` 只统计覆盖，
`--setup-choices` 打印开战选择请求树，`--setup-matrix-list` 打印遗物矩阵名单。

## 地图选点与进房间

```bash
dotnet artifacts/bin/Differential/release/Differential.dll \
  --map-step-compare --map-step-case "Ironclad|0|empty|0|R2B2-PROBE-1" --map-step-acts 3
dotnet artifacts/bin/Differential/release/Differential.dll \
  --map-step-compare --map-step-seeds 8 --map-step-jobs 4 --map-step-acts 3
```

原版从新局自己推进到每个地图选点；每到一个选点导出一次 `SerializableRun` 快照，对每个合法
选项恢复同一快照并分别执行 `RunManager.EnterMapCoord` 与 `RunMapEntry.Enter`。战斗比较
`Battle`/`Rules`/`RunState`；非战斗房间用测试补丁截在 `RunManager.EnterRoomInternal` 之前，
比较"房间已建好、历史已追加、尚未进入"的 `RunState`（事件 RNG 单独比较）。战斗用杀敌捷径，
奖励与房间内容跳过；捷径在每次导入之前，不进入比较。

## 战斗 pilot

```bash
# 120 个质量根，默认 8 个子进程
dotnet artifacts/bin/Differential/release/Differential.dll --combat-pilot-compare
dotnet artifacts/bin/Differential/release/Differential.dll --combat-pilot-compare --pilot-root quality-IRONCLAD-001
dotnet artifacts/bin/Differential/release/Differential.dll --combat-pilot-compare --pilot-jobs 4 --pilot-shard 0 2
```

低档 `CombatPilot` 生成路线，`NativeActionExecutor` 用原版公开入口逐动作执行；每个可出牌动作
边界导入 `Battle` 与预测逐字节比较。终局在 `EndCombatInternal`/`ProcessPendingLoss` 的 prefix
捕获原版 `Battle`，比较胜负、HP/MaxHP、金币、药水、遗物状态与 9 条战斗 RNG；手牌/牌堆/回合
内计数等清理字段不在合同内。捕获失败计 reject，不计 same。

## 根重新捕获

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --root qf-nightmare-detached --recapture-root --out .local/recapture
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 4 --recapture-root --out .local/recapture-all
```

每个根按与 `--config-compare` 相同的导入/前缀回放/回合准备枚举路径重建根和 `config.br`，写出
`<out>/<root>/roots.gold` 与 `<out>/<root>/config.br`。

## 实机请求/结果与搜索对照

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --emit-live-requests .local/live-requests --all
dotnet artifacts/bin/Differential/release/Differential.dll --live-compare .local/m2b0-20-results
dotnet artifacts/bin/Differential/release/Differential.dll --search-compare .local/m2b1-20-results
```

`--emit-live-requests` 为 Mod 无头测试生成请求；`--live-compare` 比较游戏写出的
`BATL`/`RULE`/`SREQ.capture`/`native.txt`；`--search-compare` 重解游戏 `SREQ` 并比较 `SRES`。
启动脚本见 [`tools/testing/run-headless.sh`](../../tools/testing/run-headless.sh)。

## 离线整场自动战斗

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --offline-combat --corpora tests/corpora --jobs 4 \
  --out .local/offline-combat/default
dotnet artifacts/bin/Differential/release/Differential.dll --offline-combat --root quality-DEFECT-000 \
  --beam-width 256 --max-width 256 --out .local/offline-combat/one
```

不启动 Godot，用与 Mod 相同的 `CombatPilot` 在内核里打完一个根。每个根写
`<out>/<root>/full-combat.json`，父进程写 `<out>/summary.json`；`--search-tier` 默认
`Medium`，`--memory-budget-mb` 覆盖搜索内存预算。

## 与参考转储对照

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --compare-only .local/differential/out .local/m0ref/dumps
```

## 输入

- fixture：`tests/corpora/fixtures/<root>.json`；
- 质量语料：`tests/corpora/quality/<name>/spec.json` 与 `request.json`，根名为
  `quality-<name>`。

请求 DTO 覆盖当前 410 个输入、生成场景 spec 与评估政策实际用到的字段；未知字段、非空
`checkpointArchivePath`/`replayPolicyOverridePath` 以及不支持的搜索/部署值会显式失败。
