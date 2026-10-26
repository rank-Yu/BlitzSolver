# 离线宿主

本工程在普通 .NET 9 控制台进程里初始化《杀戮尖塔 2》的游戏静态状态，用游戏自己的
`RunManager`、`ModelDb` 与 `CombatState` 建出一场战斗，推进到玩家可操作，再把原生战斗状态
写成确定性转储。它不启动 Godot 可执行文件，也不需要场景树。

用途是 M2a/M2b 的离线验证：先证明不启动游戏也能建出与冻结快照导出时相同的原生状态，再用
`src/Import` 把状态导入 `Battle` 并与 GOLD 逐字节对照；也用于对照 Mod 在无头游戏里写出的
实机捕获结果。建局、注入与转储代码与 Mod 共用 `src/TestHost/`。本工程不在
`BlitzSolver.sln` 中；CI 没有游戏文件，不会构建或运行它。

## 需要游戏文件

需要本机的游戏安装目录，至少包含：

- `sts2.dll`
- `GodotSharp.dll`
- `0Harmony.dll`

路径通过本目录下被 git 忽略的 `local.props` 配置。把 `local.props.example` 复制为
`local.props`，通常只需要把 `SteamRoot` 指向 Steam 库根目录；`Sts2Dir` 与 `Sts2DataDir`
会按操作系统派生。`local.props` 不要提交。

RitsuLib 不引用：建局、注入与转储路径只调用游戏原生 API，不需要 RitsuLib 的类型或补丁。

## 构建

从仓库根目录：

```bash
dotnet build tests/OfflineHost/OfflineHost.csproj -c Release
```

产物在 `artifacts/bin/OfflineHost/release/OfflineHost.dll`。构建不读取游戏文件到仓库，
`bin/` 与 `obj/` 已被忽略。

## 建一个根

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --root kg2-b-stomp \
  --out .local/offlinehost/out
```

输出为 `.local/offlinehost/out/<root>.native.txt`。`--root` 使用
`tests/Golden/data/d1/<root>` 的目录名。

## 建全部 410 个根

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --all --jobs 8 --out .local/offlinehost/out
```

每个根一个子进程，避免游戏静态状态跨根污染；`--jobs` 取值 1 到 8。

## 导入对照

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --root kg2-b-stomp --import-compare
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --all --jobs 8 --import-compare
```

每个根建原生状态、调 `BattleImporter.Import`，再与 `d1/<root>/golden.br` 的期望 `BATL`
逐字节比较。结果只有 `same`、`diff`（打印 `BattleDump` 的前 24 个差异字段及偏移）与
`reject`（打印稳定原因码）。`--all` 覆盖全部 410 个输入/418 个根：前缀 fixture 先导入前缀前
状态，再用 `PrefixReplay` 在 Core 内核上执行冻结动作；回合准备 fixture 用
`SetupPlayerTurn` Harmony 前缀记录 Start 卡牌集合并按原生选项逐个重建分支。实现见
[`docs/import.md`](../../docs/import.md)。

`--compiled-rules` 改用 `RulesCompiler.Compile` 的输出来导入：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --all --jobs 8 --import-compare --compiled-rules
```

## 整局状态导入对照

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --run-import-compare --run-kind all --run-seeds 20 --run-jobs 8
```

自然开局（5 角色 × A0/A10 × 20 种子）与全部 410 个 D1 输入各起一个子进程：导入
`RunState`，与独立原生转储逐字段比较；每条随机流抽 64 次；比较当前幕地图；D1 还检查
9 条战斗流、HP、牌组、遗物与药水。只跑一类用 `--run-kind natural|d1`。负例：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll --run-import-negative
```

字段与命令细节见[整局状态布局](../../docs/run-state-layout.md)与
[测试与验证](../../docs/testing.md)第 6.10 节。

## 规则编译对照

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --root kg2-b-stomp --rules-compare
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --all --jobs 8 --rules-compare
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll --rules-table-check
```

`--rules-compare` 对每个根编译 `Rules`、用 `RulesCodec.Encode` 编码，再与该根冻结规则对象
逐字节比较；`diff` 打印 `RulesDiff` 的第一处表/项。`--rules-table-check` 校验
`src/Import/Combat/MirrorRiskTables.cs` 与 `ModelDb.All`/Blitz Registry 的 id 集合完全一致。

## 评估配置对照

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --root kg2-b-stomp --config-compare
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --all --jobs 8 --config-compare
```

每个根先用 `RulesCompiler` 编译规则、`BattleImporter` 导入普通根，再编译
`ScoreRules`/`BoundFeatures.Config`，按 M0 `FlashGoldenConfigDump` 的方式组装
`BeamOptions(1,1,1,TimeSpan.Zero, maxHp)` + `Scorer`/`Bound`/`InitialActionCount`，与
`d1/<root>/config.br` 逐字节比较。`diff` 依次比较规则、根、评分表、下界与其余选项并打印
第一处字段；同时用同一份 `tests/Golden/GoldenHash.cs` 校验 `scoreHash`/`boundHash`。

3 个回合准备 fixture 的 `config.br` 含全部准备根，离线宿主按 GOLD 的 `Choices` 顺序把独立
重建的根排入请求后做完整请求比较。当前 410 个输入：

```text
config same=410 diff=0 reject=0 full=410 subparts=0
```

请求 DTO 新增了会影响评估配置的政策覆盖：`growthBudgets`、`relicCounterRules`、
`ignoreLongTermRewards`、`actTransitionBossHpStrategy`、`finalBossHpStrategy`、
`theftPolicyForTest`、`act3BossStrategyForTest`、`baseScoreOnlyForTest`、
`continuousThreatRankingForTest`、`beamWeightTermForTest`/`beamWeightScaleForTest`。冻结
fixture 都没有使用它们；未知字段仍显式失败。

负例模式：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll --import-negative
```

它验证读不到私有字段、未认领私有状态、第三方程序集内容三类拒绝。

## 实机请求与结果对照

为 Mod 的无头测试生成请求（文件名就是结果根名）：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --emit-live-requests .local/live-requests --roots-file .local/m2b0-20-roots.txt
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --emit-live-requests .local/live-requests-all --all
```

`--all` 跳过 13 个前缀输入，生成 397 个普通请求。`generatedScenarioPath` 会写成绝对路径。

对照 Mod 写出的结果目录：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --live-compare .local/m2b0-20-results
```

逐根比较 `BATL`（与 `golden.br` 的期望根）、`RULE`（与冻结规则对象）、`SREQ.capture`
（与 `config.br`；回合准备 fixture 为 `scope=subparts`；旧结果没有该文件时回退到 `SREQ`）
和 `native.txt`（离线重建同一根后的 `NativeStateDump`）。输出
`live roots=N same=... diff=... reject=... native=...`。
启动脚本详见 [`docs/mod.md`](../../docs/mod.md)。

生成请求时可以指定搜索选项：

```bash
# 默认中档确定性（去软时间，起始宽度 256、第一轮节点上限 512,000）
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --emit-live-requests .local/live-requests --roots-file .local/m2b0-20-roots.txt

# 正常中档（带 120s 软时间），用于耗时/内存测量
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --emit-live-requests .local/heavy --root quality-REGENT-013 \
  --search-tier Medium --search-normal

# 到 1s 取消
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --emit-live-requests .local/cancel --root quality-REGENT-013 \
  --search-tier Medium --search-normal --search-cancel-after-ms 1000
```

对照游戏写出的确定性搜索请求与结果：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --search-compare .local/m2b1-20-results
```

对每个 `status.searchStatus=ok` 且 `deterministicSearch=true` 的结果目录：解码游戏 `SREQ`，
用同一 `BeamSolver` 离线重解并与游戏 `SRES` 逐字节比较；同时用离线宿主自己重建原生根、
导入并调用同一个 `SearchRequestCompiler`，与游戏 `SREQ` 逐字节比较。输出
`search-compare roots=N same=... diff=... reject=... skipped=...`。正常档测量结果计为
`skipped reason=measure-only`，回合准备根计为 `skipped reason=turn-setup-root`。

## 离线整场自动战斗

`--offline-combat` 不启动 Godot，用与 Mod 相同的 `CombatPilot` 在内核里打完一个根：

```bash
# 全部质量根（默认 tests/corpora/quality）
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --offline-combat --corpora tests/corpora --jobs 4 \
  --out .local/offline-combat/default

# 指定根列表
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --offline-combat --roots-file .local/roots120.txt --jobs 4 \
  --out .local/offline-combat/default

# 单根；单宽度探针（起始=最多）或节点上限覆盖
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --offline-combat --root quality-DEFECT-000 \
  --beam-width 256 --max-width 256 --out .local/offline-combat/one

# 搜索内存预算覆盖（MiB）；默认是 0.15 × 16 GiB = 2,576,980,377 B
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --offline-combat --root quality-REGENT-013 \
  --memory-budget-mb 64 --out .local/offline-combat/b64
```

`--search-tier` 默认 `Medium`（宽度 256→512，每轮节点上限 宽度 × 2,000，决策总时间
120 秒）；`Low`/`High`/`ExtraHigh` 的最多宽度分别为 256/1,024/2,048，时间保险分别为
60/180/240 秒。`--beam-width W` 把起始与最多宽度都设成 W
（单宽度探针），`--max-width N` 可单独覆盖最多宽度，`--nodes-per-width N`/`--max-nodes N`
覆盖节点上限，`--pool-state-budget-mb N` 固定每轮局面池上限。多轮搜索的单池上限为
`max(200 MiB, 宽度 × 1 MiB)`，封顶 1,728 MiB；宽度 2,048 的乘积先按 64 位计算再截断。
每个根写 `<out>/<root>/full-combat.json`，父进程写 `<out>/summary.json`。搜索
使用确定性选项（去软时间，只受节点上限与全局预算约束）；每次决策和每一轮另记托管堆/工作集
峰值、逻辑内存账本、节点存储字节、预算触发、池容量丢弃与时间预估标记。子进程默认先做一次
50,000 节点热身（不计时；`BLITZ_OFFLINE_WARMUP_NODES` 可改，0 关闭），
`BLITZ_STORAGE_CHECK=1` 会在首个搜索上用同一 `SearchStorage` 连续解两次并打印逐字节比较与
分配差。ExtraHigh 的 120 根通常超过 8 分钟，按 60 根拆两次：

```bash
for part in a b; do
  dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
    --offline-combat --roots-file ".local/roots-xhigh-$part.txt" --jobs 4 \
    --search-tier ExtraHigh --out ".local/offline-combat/xhigh-$part"
done
```

`summary.json` 的逐根摘要总是带 `exitCode`。子进程在写出 `full-combat.json` 前崩溃时，
`status=crashed`，`exceptionType`/`error` 来自 stderr 的 `Unhandled exception.` 行；
解析不到异常时保留退出码与“没有写出 full-combat.json”的说明，不会把
`[scenario] scenario.validate` 之类的最后阶段误记为错误原因。逐场与实机比较的口径见
[`docs/testing.md`](../../docs/testing.md) 6.7 节，内存预算与计时见 6.8 节。

## 与参考转储对照

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --compare-only .local/offlinehost/out .local/m0ref/dumps
```

每个不一致的根打印第一处差异行；全部相同时输出
`compare roots=410 mismatches=0`。

## 输入

- fixture：`tests/corpora/fixtures/<root>.json`（来源见
  [`tests/corpora/fixtures/README.md`](../corpora/fixtures/README.md)）。
- 质量语料：`tests/corpora/quality/<name>/spec.json` 与 `request.json`，
  根名为 `quality-<name>`。

请求 DTO 覆盖当前 410 个输入、生成场景 spec 与评估政策实际用到的字段；未知字段、非空
`checkpointArchivePath`/`replayPolicyOverridePath` 以及不支持的搜索/部署值会显式失败。
`flashPrefixActions`/`flashPrefixPlan` 由 `PrefixPlan` 按 CombatSolver 的计划语义翻译；无法
翻译的字段抛 `prefix-plan`。`flashTurnSetupRootForTest` 的 Start 卡牌集合由
`TurnSetupRootCapture` 捕获。

## 转储

转储是行式规范文本，格式由 `NativeStateDump.cs` 定义：头部、跑局/幕（进阶、当前幕、
幕数、本幕首个/第二个 Boss、房间类型、教学发现顺序开关、解锁摘要）、玩家/Osty/敌人属性、
原生顺序的牌堆与 Power、遗物可见计数、药水槽、球、10 条 RNG（含 `UpFront`）与
`NextCombatId`。它只读原生状态：不执行动作、不推进回合、不跑搜索。
