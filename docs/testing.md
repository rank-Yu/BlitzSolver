# 测试与验证

[返回文档索引](README.md)

本文说明 BlitzSolver 当前仓库能验证什么、不能验证什么，以及如何复跑。冻结快照的格式、来源和更新条件见 [冻结快照](golden-data.md)。

## 1. 环境

- .NET SDK 9.0.300 或更高补丁版本；当前 `global.json` 固定 9.0.300，实测 9.0.318。
- Linux 或 Windows 的普通 .NET 控制台环境；K0、D1、D2 都不需要游戏、Godot、Harmony、RitsuLib 或第三方 Mod。
- 所有命令在仓库根目录执行，`dotnet` 需要在 `PATH` 上。

## 2. 构建

```bash
dotnet build BlitzSolver.sln -c Release -t:Rebuild
```

当前结果：`0 个警告，0 个错误`。产物在 `artifacts/bin/`：

- `BlitzSolver.Core.dll`
- `BlitzSolver.KernelChecks.dll`
- `BlitzSolver.Golden.dll`

## 3. K0：纯内核合同

```bash
dotnet run --project tests/KernelChecks -c Release --no-build
dotnet run --project tests/Golden -c Release --no-build -- k0
```

两条命令分别检查：

- `tests/KernelChecks`：`"contracts": "Passed"`。覆盖 `Battle`/`Card`/`Enemy`/`Power`/`RngState` 等布局尺寸、RNG 参考向量、牌堆与容量失败、选择协议、内容注册表与编码合同；另含 `Exec` 字段覆盖合同（反射枚举全部实例字段，见 4.3）。
- `tests/Golden k0`：`K0 Passed fields=30 battleBytes=12696 formatVersion=2 magics=BATL,RULE,GOLD,SREQ,SRES`。检查 `RulesCodecContract` 的 30 个字段都进入编码、`decode(encode(x))` 字节稳定、`Unsafe.SizeOf<Battle>() == 12696`，`BATL`/`RULE`/`GOLD`/`SREQ`/`SRES` 五个魔数按小端字节解出的 ASCII 与格式标签一致，以及 `BATL` 的旧魔数/version 1 被显式拒绝。

K0 证明：内核的布局、编码与纯函数合同在 BCL 环境下成立。
K0 不证明：D1/D2 的搜索语义、冻结输入的求值路径、实机状态。

## 4. D1/D2：冻结快照重放

```bash
# 完整校验（先 manifest，再 D1 + D2）
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data

# 只跑一层
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data --only d1 --jobs 8
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data --only d2 --jobs 8

# 分片与并行度
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data --only d2 --jobs 8 --shard 0/4
```

`--jobs N` 控制并行度，默认处理器数。`--shard i/n` 只跑第 i 片：

- D1 分片方式不变，按排好序的根目录做 `index % n == i`。
- D2 以 `d2/expected/<sha>.result.br` 记录的 `ExpandedNodes` 为权重。所有请求按权重降序（权重相同按请求哈希的序数顺序）依次分给当前总权重最小的分片（总和相同取编号小的），结果与文件系统枚举顺序无关；每片内也按权重降序提交，最重的请求最先开始。
- 未指定 `--shard` 等价于 `0/1`：全部请求，同样按权重降序处理。

每次 D2 运行会先打印一行 `D2_SHARD`，列出当前片的 `requests`/`weight` 与旧分法的 `oldRequests`/`oldWeight`，便于对照。D2 的权重来自期望结果，不参与求解或比较逻辑。

### 4.1 D1

当前结果：

```text
D1_OK files=410 roots=418 k1=5062 walks=15296 states=482227 transitions=469352
  features=474832 traits=474414 rules=388 mismatched=0
```

D1 对每个根重放：

- `d1/<root>/golden.br` 的根状态、K1 单步动作与完整选择序列；
- K2 固定种子游走的每步状态、合法动作顺序、选择请求与实际选择；
- 每个记录状态的 `scoreHash`、`boundStatus`/`boundHash`；
- 每个转移的 `traitPresent`/`traitHash`；
- 规则对象 `objects/rules/*.br` 的编码一致性。

D1 证明：冻结的 410 个 D1 输入在 BlitzSolver 上可逐字节重放，评分/下界/路线标签特征与捕获时一致。
D1 不证明：未捕获的状态、实机导入、任意第三方内容。

### 4.2 D2

当前结果：

```text
D2_OK selected=1983 equal=1983 mismatch=0 expanded=15739416 prefixChecks=0 threatChecks=0
  prefixShared=15484187 prefixUnshared=229786 prefixReuse=10798057
  prefixChoice=229786 prefixExtraTurn=0 prefixNotInProgress=0
```

D2 对 `d2/requests/*.request.br` 的 1,983 个唯一请求：

1. 解码 `SREQ` 请求（规则、战斗、搜索选项）；
2. 把软时间预算改为 `TimeSpan.MaxValue`，清空 pacer、进度、接管与 trace；
3. 运行 `BeamSolver.Solve`；
4. 与 `d2/expected/*.result.br` 逐字段比较路线、TopRoutes、分数 double 位、展开/转移/去重、容量丢弃、终态压缩、药水审计、侦察层、BestRootIndex、PotionPolicySatisfied 与 Takeover。

分片权重取第 4 步所比较的同一个 `ExpandedNodes` 字段。它是确定性计数，与机器无关；全量运行和分片运行都不依赖目录枚举顺序。

D2 证明：冻结的 1,983 个请求在去软时间预算后可复现完整搜索结果。
D2 不证明：请求集合之外的根、实机运行、界面与部署行为。

完整 `verify` 的最后一行是 `GOLDEN_VERIFY ... mismatched=0`。解码失败或字段不一致输出 `GOLDEN_INVALID <原因>`，非预期异常输出 `GOLDEN_FAILED <类型>: <消息>`，两者都以非零退出。

### 4.3 搜索等价自检（`Exec` 覆盖合同 + 原地投影）

`tests/KernelChecks` 的 `ExecCoverage` 合同用反射枚举 `Exec` 的全部实例字段（含私有字段），要求每个字段恰好归入一类：进入 `ExecContinuation`、捕获时断言为空/为零、`AdvanceRoundPlayerStart` 入口重置、或单独处理（`B`/`R`/选择游标）。分类表在 `tests/KernelChecks/ExecCoverage.cs` 一处；同时反向校验快照字段集合与 `ExecContinuation` 完全一致。出现未分类、重复归类或快照表不一致时，消息写出字段名。临时给 `Exec` 加字段的本地反向检查会失败（`ReverseCoverageProbe` 未分类）。

`BeamSolver` 默认使用共享的 `EndTurn` 前缀；设置 `BLITZ_VERIFY_SEARCH_EQUIVALENCE=1` 后同时开启两项等价检查：

- 每个复用前缀的选择叶子同时执行完整 `Kernel.Step` 与前缀续跑，先逐字节比较 `Battle`（不一致时用 `BattleDump.Diff` 输出字段差异），并比较 `StepResult`、`ChoiceRequest`（选项、来源、时机与生成卡字节）和 `HistoryRisk`；
- 每次 `Scorer.ProjectHpAfterThreat` 原地投影前保存 `Battle`，`finally` 恢复后逐字节比较前后镜像；不一致时用 `BattleDump.Diff` 写出字段差异。

不一致时分别抛出 `EndTurn 前缀等价校验失败` 或 `威胁投影原地自检失败`。开关默认关闭，不进入请求格式。D2 输出行的 `prefixChecks` 与 `threatChecks` 分别是两项检查实际比较的次数：

```bash
BLITZ_VERIFY_SEARCH_EQUIVALENCE=1 \
  dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data --only d2 --jobs 6
```

M3a 跟进的实测：全量 D2 1983/1983，`prefixChecks=10,764,918`，`threatChecks=86,463,608`，wall 358.2s（jobs 6）。关闭时全量 `verify`：D1 12.2s、D2 192.9s，1983/1983 相等；正常请求 `6a4d89…` 关闭开关的 3 次中位数 4.993s，与加入检查前的 4.988s 相差 +0.09%，在 1% 以内。

### 4.4 计时

本机 24 逻辑处理器，`--jobs 8`（D2 分片模拟另标 `--jobs 4`）：

| 运行 | 墙钟 | 备注 |
| --- | ---: | --- |
| Release 重建 | 约 4.6s | 0 警告 |
| D1 | 12.0s | 410 文件 / 482,227 状态 / 474,832 特征 |
| D2 全量 | 267.2s | 1,983/1,983，展开 15,739,416 |
| D1 + D2（分项相加） | 约 279s | 不含构建 |
| manifest | 小于 1s | 5,592 文件 / 24,338,737 B |

D1 计时沿用本轮之前的测量；D1 分片与执行路径未改。D2 全量按新的权重顺序运行。

D2 的 4 片 CI 模拟（依次运行，`--jobs 4`）：

| 分片 | 请求 | 权重和 | equal | 墙钟 |
| ---: | ---: | ---: | ---: | ---: |
| 0/4 | 494 | 3,934,854 | 494 | 260.8s |
| 1/4 | 495 | 3,934,854 | 495 | 50.1s |
| 2/4 | 496 | 3,934,854 | 496 | 50.7s |
| 3/4 | 498 | 3,934,854 | 498 | 48.4s |
| 合计 | 1,983 | 15,739,416 | 1,983 | 410.1s |

按权重分配后 4 片的权重和完全相同，但墙钟仍取决于最重的单请求。分片 0 包含权重 283,539 的请求 `40e330b6…`；它单独求解需要 249.2s，已经超过 4 片平均墙钟 102.5s。这是单请求下限，增加分片数不能把这一片的墙钟压到 249.2s 以下。

M1b 的 D2 为 331.3s；v2 迁移后为 283.9s；改为按权重调度后为 267.2s。全量总展开数不变，差异来自调度与测量波动。

M3a（`EndTurn` 前缀共享 + 评分/内核聚合）在同一台机器上以 `--jobs 6` 测得：基线 `main@f4909de` 全量 D2 267.2s，本批最终一次 185.1s（多次运行 179.0–195.6s）；跟进的关闭检查全量 192.9s。慢请求 `40e330b6…` 的中位数从 259.1s 降到 170.2s（283,539 个展开节点，0.914→0.600 ms/节点）。开启 `BLITZ_VERIFY_SEARCH_EQUIVALENCE=1` 的全量 D2 为 1983/1983，`prefixChecks=10,764,918`、`threatChecks=86,463,608`，358.2s。

## 5. manifest

```bash
dotnet run --project tests/Golden -c Release --no-build -- manifest tests/Golden/data
```

`manifest` 按路径 ordinal 排序，逐文件写 `path`/`bytes`/`sha256`，并写 `formatVersion`、`battleSize`、`provenance`、`compression`、`counts`、`totalBytes`、`targetBytes`。当前：

```text
MANIFEST_WRITTEN files=5596 totalBytes=24340033 d1Files=410 d1Roots=418 d1K1=5062
  d1Walks=15296 d1WalkStates=482227 d1Transitions=469352 d1Configs=410
  d2Requests=1983 d2Expected=1983
```

manifest 是确定性的：同一份 `data/` 重写后应逐字节相同。验证时：

```bash
git diff --exit-code -- tests/Golden/data/manifest.json
```

manifest 证明：文件集合、大小、SHA-256、计数与 provenance 自洽。
manifest 不证明：语义正确性；它只保护冻结快照不被静默改动。

## 6. 离线宿主（需要游戏文件，不在 CI 中运行）

`tests/OfflineHost` 是不启动 Godot 的控制台工程：它初始化游戏静态状态、用游戏自己的
`RunManager`/`ModelDb` 建局、推进到玩家可操作，写出原生 `CombatState` 的确定性转储，并可用
`--import-compare` 调 `src/Import` 导入 `Battle`、与 `golden.br` 的 `BATL` 逐字节对照，用
`--rules-compare` 编译 `Rules` 后与冻结规则对象逐字节对照，或用 `--config-compare` 编译
`ScoreRules`/`BoundFeatures.Config` 后与 `config.br` 逐字节对照；`--search-compare` 还能
重放游戏写出的确定性 `SREQ`/`SRES` 并核对离线组装的请求。工程**不加入**
`BlitzSolver.sln`；CI 没有游戏文件，只保证 `dotnet build BlitzSolver.sln` 不受影响。

需要本机游戏目录（`sts2.dll`、`GodotSharp.dll`、`0Harmony.dll`）。路径通过
`tests/OfflineHost/local.props` 配置；该文件被 git 忽略，模板是 `local.props.example`。
RitsuLib 不需要。建局、注入与转储代码与 Mod 共用 `src/TestHost/`，不在两边复制。
完整说明见 [`tests/OfflineHost/README.md`](../tests/OfflineHost/README.md)。

```bash
dotnet build tests/OfflineHost/OfflineHost.csproj -c Release
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll --all --jobs 8 --out .local/offlinehost/out
```

`--root <name>` 建单个根；`--all` 为每个根起一个子进程；`--compare-only <new> <ref>`
逐根比较转储并打印第一处差异。当前 410 个输入与参考转储逐字节相同；参考转储由 CombatSolver `flash/m0` 的 `fe428fca` 生成（该提交给 M0 离线宿主加了同一份 `NativeStateDump`）。

M2b-0b 起，`NativeStateDump` 格式版本为 2，额外记录跑局/幕信息：`run.ascension`、
`run.act.index`/`count`/`id`、`run.act.boss`/`secondBoss`、`run.room.type`/`id`、
`run.tutorial.discoveryOrder`、`run.unlock.*`，以及 `rng.UpFront` 的 counter/state。
这些字段用于直接对照两条建局路径的幕结构与解锁状态；`SecondBossEncounter` 缺失时写 `-`。

### 6.1 导入对照

```bash
# 单个根：建原生状态 → 导入 → 与 golden.br 的第一个/普通根 BATL 比较
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --root kg2-b-stomp --import-compare

# 全部 410 个输入（前缀根回放，回合准备 fixture 枚举全部分支）
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --all --jobs 8 --import-compare

# 改用规则编译器输出，而不是解码冻结规则对象
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --all --jobs 8 --import-compare --compiled-rules
```

每根输出 `same`、`diff` 或 `reject`。`diff` 随后打印 `BattleDump.Diff` 的前 24 行，每行带
`+偏移 字段: golden=[...] import=[...]`；`reject` 打印稳定原因码与 detail。`--all` 汇总三类
计数，并按首个差异字段、拒绝原因分组。当前：

```text
import same=410 diff=0 reject=0
```

`qf2-gradient-failure`、`ts-b-toolbox`、`ts-a-toasty` 共 11 个根全部对照。前缀根是
“导入前缀前状态 + 内核回放”，回合准备 fixture 是“SetupPlayerTurn 前缀捕获 Start 卡牌集合 +
逐个原生准备选项重建”。实现与字段来源见 [原生状态导入](import.md)。

### 6.2 规则编译对照

```bash
# 单个根：编译 Rules → RulesCodec.Encode → 与冻结规则对象逐字节比较
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --root kg2-b-stomp --rules-compare

# 全部 410 个输入
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --all --jobs 8 --rules-compare

# 静态镜像覆盖率表合同：没有遗漏/多余 id，且覆盖全部 Blitz Registry 内容
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --rules-table-check
```

每根输出 `same`、`diff` 或 `reject`。`diff` 随后打印 `RulesDiff` 的第一处表/项及前 16 行差异；
`reject` 打印稳定原因码与 detail。`--all` 同时打印 410 个输入覆盖的不同冻结规则对象数。当前：

```text
rules-objects distinct=388 roots=410
rules same=410 diff=0 reject=0
RULES-TABLES cards=603 powers=283 native_cards=603 native_powers=283 \
  missing_cards=0 extra_cards=0 missing_powers=0 extra_powers=0 registry_missing=0
```

### 6.3 评估配置对照

```bash
# 单个根：编译 ScoreRules/Bound → 重组 SREQ → 与 config.br 逐字节比较
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --root kg2-b-stomp --config-compare

# 全部 410 个输入
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --all --jobs 8 --config-compare
```

410 个输入整份请求逐字节相同，包括 3 个回合准备 fixture 的全部准备根。`diff` 依次比较规则、
根、评分表、下界与其余选项，打印第一处字段。当前同时通过 `GoldenHash` 校验 418 个根的
`scoreHash`/`boundHash`：

```text
config same=410 diff=0 reject=0 full=410 subparts=0
```

### 6.4 实机捕获结果对照

Mod 在无头游戏里把 `BATL`/`RULE`/`SREQ.capture`/`native.txt` 写进结果目录后，离线宿主可以对照：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --live-compare .local/m2b1-20-results
```

每根比较：

- `BATL` 与 `d1/<root>/golden.br` 的期望根（回合准备 fixture 按 `setupSelections` 选根）；
- `RULE` 与冻结规则对象；
- `SREQ.capture` 与 `config.br`（回合准备 fixture 为 `scope=subparts`，只比评分/下界子部件；
  旧结果没有 `SREQ.capture` 时回退到 `SREQ`）；
- `native.txt` 与离线宿主用同一请求重建的 `NativeStateDump`。

输出逐根 `live ... status=same ... native=same`，最后汇总
`live roots=N same=... diff=... reject=... native=...`。M2b-0 的 20 个普通根在 M2b-1 复跑仍
全部相同：两部分各 10 根，`same=10 diff=0 reject=0 native=10`；3 个回合准备 fixture 另行
捕获，`sreq=subparts`、`native=same`；共存探针输出 `COEXISTENCE check=passed`。启动脚本、
协议与限制见 [Mod 入口与无头捕获](mod.md)。

M2b-0b 修复了实机与离线的第三幕幕结构偏差：实机测试模式没有打开游戏 `TestMode`，
`RunManager.ShouldApplyTutorialModifications()` 返回 true，`RunManager.GenerateRooms` 用
`ActModel.ApplyDiscoveryOrderModifications` 把第三幕首个 Boss 覆盖为 `QUEEN_BOSS`，随后
第二个 Boss 的排除集合也随之改变；离线宿主打开 `TestMode`，走 RNG 分支。修法是只对
`RunManager.ShouldApplyTutorialModifications()` 加测试会话补丁，不全局改游戏 `TestMode`。
5 个受影响根与 23 根复跑结果：

```text
# 5 个第三幕 A10 Boss 根
live roots=5 same=5 diff=0 reject=0 native=5
# 20 个固定普通根 + 3 个回合准备 fixture
live roots=23 same=23 diff=0 reject=0 native=23
```

`BATL`/`RULE`/`SREQ` 与冻结快照相同，`native.txt` 与离线重建相同。根因与证据见
[Mod 入口与无头捕获](mod.md) 第 5.1 节。

生成实机请求：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --emit-live-requests .local/live-requests --roots-file .local/m2b0-20-roots.txt
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --emit-live-requests .local/live-requests-all --all
```

`--all` 跳过 13 个前缀输入，输出 397 个普通请求；回合准备 fixture 也在其中。
`--emit-live-requests` 还接受 `--search-tier Low|Medium|High|VeryHigh`（默认 `Medium`）、
`--search-normal`（关掉确定性软时间）与 `--search-cancel-after-ms N`（取消测试）。

### 6.5 实机搜索请求/结果对照

测试模式在捕获后默认用中档确定性选项求解一次（软时间关闭，只留 480,000 节点上限），写
`SREQ`/`SRES`/`search.json`。离线宿主：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --search-compare .local/m2b1-20-results
```

逐根做两件事：

1. 解码游戏 `SREQ`，用同一 `BeamSolver` 离线重解，`SearchCodec.EncodeResult` 后与游戏
   `SRES` 逐字节比较；
2. 用离线宿主重建同一原生根、导入并编译规则/评估配置，再按 `status.searchTier` 与
   `status.deterministicSearch` 调用同一个 `SearchRequestCompiler`，与游戏 `SREQ` 逐字节
   比较。

输出逐根 `search=same sreq=same sres=same`，汇总
`search-compare roots=N same=... diff=... reject=... skipped=...`。非确定性测量结果计为
`skipped reason=measure-only`，回合准备根计为 `skipped reason=turn-setup-root`。

M2b-1 的 20 个普通根分两部分执行，20/20 得到 `search=same sreq=same sres=same`，
`diff=0 reject=0 skipped=0`。`quality-REGENT-013` 的“中”档正常搜索（120s 软时间）实测
63.3s、142,048 个展开节点、托管堆峰值约 1.96GB、工作集峰值约 3.21GB；另外两个 PF1 重根
`quality-IRONCLAD-013`、`quality-DEFECT-012` 在本请求下分别 2.8s / 2.1s。取消测试用
`quality-REGENT-013` 在 1s 时取消，输出 `searchStatus=cancelled, workerExited=true`，随后
返回主菜单，没有后台任务残留。完整数值与字段来源见 M2b-1 报告。

### 6.6 实机整场自动战斗

`OfflineRequest` 增加 `fullCombatForTest`；`--emit-live-requests` 的 `--full-combat` 把它置位
并关闭确定性。测试模式捕获根后立刻进入 `AutoCombatSession`，每回合搜索/沿用路线、逐动作
核对、回合对账，直到胜利、失败、暂停或取消。结果写 `full-combat.json`，并在 `status.json`
增加 `fullCombatStatus/outcome/turns/playerHp/playerMaxHp/searches/reusedTurns/deviations/
deploymentFailures`。整场搜索使用请求档位的正常软时间；`src/Mod` 还会把
`headlessFastModeForTest` 覆盖为 `Instant`（默认）并在结束后恢复。

命令：

```bash
tools/testing/run-headless.sh --instance m2b2-full-1 \
  --roots-file .local/full-1.txt --full-combat \
  --results .local/m2b2-full-1-results \
  --cleanup-instance-on-exit --timeout-seconds 480
```

M2b-2 的 20 个固定根分两条命令执行，全部结束：

```text
HEADLESS_DONE instance=m2b2-final-a ok=10 failed=0 total_ms=272809.2872
HEADLESS_DONE instance=m2b2-final-b ok=10 failed=0 total_ms=297313.871
```

16 胜 4 负，合计搜索 20 次、沿用回合 88 次、预测偏差 0、部署失败 0；逐场数据见
`.local/tool-tasks/blitz/M2b2-report.md`。取消验证用
`--full-combat --search-cancel-after-ms 5000`，得到 `fullCombatStatus=cancelled`、
`AutoCombatSession.IsRunning=false`，随后正常返回主菜单。

### 6.7 负例

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll --import-negative
```

默认在 `p8-c-rage-juggling-dark-embrace` 上验证三条路径：把 Power 的内部数据置空得到
`missing-private-field`；向认领账本注入未认领条目得到 `unclaimed-private-state`；把
`OfflineHost` 程序集里的测试 Power 挂到玩家身上得到 `third-party-content`。三条都必须拒绝，
输出 `negative failures=0`。

离线宿主证明：不启动 Godot 时，410 个冻结输入能建出与参考导出相同的原生战斗状态；410 个
输入（418 个根）能导入并与 GOLD 的 `BATL` 逐字节相同，也能编译出与冻结规则对象逐字节相同
的 `Rules`，还能编译出与 `config.br` 相同的评分表与下界；3 类私有状态/第三方内容负例显式
拒绝。它不证明：实机部署、界面、准备阶段的多段选择，以及未捕获的第三方 Mod 内容。实机
状态导入与单 Beam 搜索已由 `src/Mod` 的 20 根捕获、`--live-compare` 与 `--search-compare`
覆盖。

## 7. 每层证明什么

| 层 | 证明 | 不证明 |
| --- | --- | --- |
| 构建 | 源码可编译、无警告、只有 BCL 依赖 | 运行时正确性 |
| K0 | 布局、RNG、编码、容量与注册表合同 | 搜索与冻结输入 |
| D1 | 410 个冻结输入的逐步重放与特征哈希 | 未捕获状态、实机导入 |
| D2 | 1,983 个冻结请求的完整搜索结果 | 未捕获请求、实机部署 |
| manifest | 快照完整性与来源声明 | 语义正确性 |
| 离线宿主（手动） | 410 个原生 `CombatState` 与参考转储逐字节相同；410 个输入（418 个根）导入 `Battle` 与 GOLD 逐字节相同，编译 `Rules` 与冻结规则对象逐字节相同，编译 `ScoreRules`/`Bound` 与 `config.br` 相同且特征哈希一致 | 实机部署、任意 Mod、语料外前缀/准备阶段 |
| Mod 无头捕获（手动） | 20 个普通根（五角色、三幕、普通/精英/Boss、私有状态）的实机 `BATL`/`RULE`/`SREQ.capture` 与 `NativeStateDump` 均与冻结快照/离线宿主逐字节相同；M2b-0b 的 5 个第三幕 A10 Boss 根与 3 个回合准备 fixture 亦相同；共存检查通过 | 部署、界面、397 根全量、Windows 脚本、第三方 Mod |
| 实机单 Beam（手动） | 同样 20 根的确定性 `SREQ` 与离线组装逐字节相同，`SRES` 与离线重解逐字节相同；中档重根有耗时/内存；取消后 worker 退出 | 界面、语料外根、重根结果与 CombatSolver 主成员的对比 |
| 实机整场（手动） | 20 个固定根在无头游戏里自动打完（16 胜 4 负），逐动作核对与回合对账均为 0 偏差/0 部署失败；至少一个根实际走过选牌、抽牌堆/弃牌堆选择与药水目标；取消测试无残留 | 120 根全量、界面、未捕获前缀/准备根、第三方 Mod |

这些层合起来证明：当前源码对 `tests/Golden/data` 的冻结证据是可复现的，能在真实游戏中导入状态、搜索并在无头战斗里自动打完固定 20 根。它们不证明：全部 397 个普通根都能结束、界面可用，或支持任意第三方 Mod。

## 8. 未验证项

- **实机全量**：M2b-0 验证了 20 个普通根与 3 个回合准备 fixture，M2b-0b 追加 5 个第三幕 A10 Boss 根与 23 根复跑，M2b-1 验证了 20 个普通根的确定性搜索与 3 个 PF1 重根的中档测量，M2b-2 只验证了 20 个固定根的整场自动战斗。397 个普通根的捕获与确定性搜索、120 根质量语料的整场运行由协调者执行，不在本仓库计时的 8 分钟窗口内。
- **界面**：没有设置页、进度、路线预览或失败提示；普通玩家只有 `F8` 开关与日志。
- **非选牌模态**：选择驱动只覆盖 `CardSelectCmd` 的 `ICardSelector` 路径；未验证未来/第三方 Mod 的其它模态请求。
- **前缀根**：实机测试模式显式拒绝前缀动作；继续由离线宿主的 `PrefixReplay` 覆盖。
- **回合准备多根**：实机只捕获第一个准备选项；完整 `config.br` 的多根排序仍只在离线宿主验证。
- **第三方 Mod**：没有实机 Mod 根；导入层只显式拒绝非 `sts2` 程序集的模型，未做兼容层。
- **Windows/Steam**：当前计时与验证在 Linux；Windows `.ps1` 启动脚本留待后续。

这些项目在接入层完成前不应被文档或提交信息描述为已验证。冻结快照的更新条件见 [冻结快照](golden-data.md) 第 11 节。
