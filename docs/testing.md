# 测试与验证

[返回文档索引](README.md)

本文说明 Sts2Solver 当前仓库能验证什么、不能验证什么，以及如何复跑。冻结快照的格式、来源和更新条件见 [冻结快照](golden-data.md)。

## 1. 环境

- .NET SDK 9.0.300 或更高补丁版本；SDK 版本以 `global.json` 为准。
- Linux 或 Windows 的普通 .NET 控制台环境；K0、D1、D2 都不需要游戏、Godot、Harmony、RitsuLib 或第三方 Mod。
- 所有命令在仓库根目录执行，`dotnet` 需要在 `PATH` 上。

## 2. 构建

Linux/Bash 本地完整验收统一使用：

```bash
tools/testing/verify-core.sh --jobs 8
```

脚本先构建整个 `Sts2Solver.sln`，构建成功后依次运行 KernelChecks、Architecture、Golden
K0 与完整 模拟回归/搜索回归；任一步失败立即非零退出。它打印当前提交、工作区是否有未提交改动、SDK、
并行度与数据目录绝对路径；`dirty` 时提交号不代表全部受测源码。默认并行度为 8，不能通过
参数跳过构建。审核候选快照时可显式使用
`tools/testing/verify-core.sh --jobs 8 --data-dir .local/<candidate>`，日志会打印实际数据目录；
默认始终验证 `tests/Golden/data`。

`dotnet run --no-build` 会使用该测试项目产物目录里的 `Sts2Solver.Core.dll` 副本。只构建
`tests/Differential` 不会刷新 Golden 或 KernelChecks 的副本，改 Core 后必须重新构建整个
solution，再运行下文的单项命令；不能把旧产物的通过结果当作当前源码的验证。

单独构建：

```bash
dotnet build Sts2Solver.sln -c Release -t:Rebuild
```

构建产物在 `artifacts/bin/`：

- `Sts2Solver.Core.dll`
- `Sts2Solver.KernelChecks.dll`
- `Sts2Solver.Golden.dll`
- `Sts2Solver.Architecture.dll`

### 2.1 层依赖规则

```bash
dotnet run --project tests/Architecture -c Release --no-build
```

`tests/Architecture` 检查编译后的 Core 分层引用：Simulation 不依赖上层，Content/Ai 只依赖 Simulation，Autoplay 依赖 Simulation/Ai。正反例确保允许的引用通过、禁止的引用被报告；同时检查 Core 不含局外模块。依赖方向见 [架构说明](README.md)。

构建 Mod 后还可以检查正式包与测试包的类型和程序集引用边界，命令见 [Mod 构建](mod.md)。

## 3. K0：纯内核合同

```bash
dotnet run --project tests/KernelChecks -c Release --no-build
dotnet run --project tests/Golden -c Release --no-build -- k0
```

两条命令分别检查：

- `tests/KernelChecks`：`"contracts": "Passed"`。覆盖 `Battle`/`Card`/`Enemy`/`Power`/`RngState` 等布局尺寸、RNG 参考向量、牌堆与容量失败、选择协议、内容注册表与编码合同；另含 `Exec` 字段覆盖合同（反射枚举全部实例字段，见 4.3）。
- `tests/Golden k0`：`K0 Passed fields=30 battleBytes=27320 formatVersion=3 magics=BATL,RULE,GOLD,SREQ,SRES`。检查 `RulesCodecContract` 的 30 个字段都进入编码、`decode(encode(x))` 字节稳定、`Unsafe.SizeOf<Battle>() == 27320`，`BATL`/`RULE`/`GOLD`/`SREQ`/`SRES` 五个魔数按小端字节解出的 ASCII 与格式标签一致，以及 `BATL` 的旧魔数/version 1 被显式拒绝。

K0 证明：内核的布局、编码与纯函数合同在 BCL 环境下成立。
K0 不证明：模拟回归/搜索回归 的搜索语义、冻结输入的求值路径、实机状态。

## 4. 模拟回归与搜索回归

`Golden` 表示保存好的标准答案，不表示最优路线。旧编号 D1 对应模拟回归，D2 对应搜索回归；旧 `--only d1/d2` 仍可使用，磁盘目录与 manifest 字段继续沿用原格式。

```bash
# 完整校验（先 manifest，再 模拟回归 + 搜索回归）
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data

# 只跑一层
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data --only simulation --jobs 8
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data --only search --jobs 8

# 分片与并行度
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data --only search --jobs 8 --shard 0/4
```

`--jobs N` 控制并行度，默认处理器数。`--shard i/n` 只跑第 i 片：

- D1 分片方式不变，按排好序的根目录做 `index % n == i`。
- D2 以 `d2/expected/<sha>.result.br` 记录的 `ExpandedNodes` 为权重。所有请求按权重降序（权重相同按请求哈希的序数顺序）依次分给当前总权重最小的分片（总和相同取编号小的），结果与文件系统枚举顺序无关；每片内也按权重降序提交，最重的请求最先开始。
- 未指定 `--shard` 等价于 `0/1`：全部请求，同样按权重降序处理。

每次 D2 运行会先打印一行 `SEARCH_SHARD`，列出当前片的 `requests`/`weight` 与旧分法的 `oldRequests`/`oldWeight`，便于对照。D2 的权重来自期望结果，不参与求解或比较逻辑。

### 4.1 模拟回归

输出 `SIMULATION_OK` 汇总文件、根、游走、状态、特征与差异计数。校验失败时返回非零退出码。

D1 对每个根重放：

- `d1/<root>/golden.br` 的根状态、K1 单步动作与完整选择序列；
- K2 固定种子游走的每步状态、合法动作顺序、选择请求与实际选择；
- 每个记录状态的 `scoreHash`、`boundStatus`/`boundHash`；
- 每个转移的 `traitPresent`/`traitHash`；
- 规则对象 `objects/rules/*.br` 的编码一致性。

D1 证明：冻结的 410 个 D1 输入在 Sts2Solver 上可逐字节重放，评分/下界/路线标签特征与录制时一致。其中 23 个根在 M2c 按原版证据修正语义后由 Sts2Solver 重录（`provenance.d1Rerecord`），其余 387 个根仍是 M0 宿主的录制。
D1 不证明：未捕获的状态、实机导入、任意第三方内容。

### 4.1.1 D1 重录与保真检查

语义修正后旧 D1 录制本身会变成旧行为，需要 Sts2Solver 自己重录。命令：

```bash
# 1. 在未改语义的代码上证明工具可复现旧录制：全部 410 个根逐字节相同
dotnet run --project tests/Golden -c Release --no-build -- \
  rerecord tests/Golden/data .local/tool-tasks/solver/rerecord-fidelity \
  --roots .local/tool-tasks/solver/rerecord-roots-all.txt --reason "fidelity check" --jobs 6
diff -r tests/Golden/data/d1 .local/tool-tasks/solver/rerecord-fidelity/d1

# 2. 语义修正后只重录失配可归因的根
dotnet run --project tests/Golden -c Release --no-build -- \
  rerecord tests/Golden/data .local/tool-tasks/solver/rerecord-d1 \
  --roots .local/tool-tasks/solver/rerecord-roots.txt --reason "<批次与语义修正>" --jobs 6
dotnet run --project tests/Golden -c Release --no-build -- \
  verify .local/tool-tasks/solver/rerecord-d1 --only simulation --jobs 6
```

第一项是阶段 C 的门槛：`rerecord` 解出旧记录，保留根状态、`config.br` 与 `meta.json` 的
输入映射/规则哈希，用 M0 的种子与游走策略重算 K1/K2 与三个派生哈希，再以
`CompressionLevel.SmallestSize` 写回。未改语义时 410 个根的 `golden.br` 必须逐字节相同；
计数不变时 `meta.json` 不重写，因此也逐字节相同。若字节不同但解码后字段相同，报告必须
说明压缩层原因。该检查不过，不做语义修正后的重录。

第二项只对 `verify` 报出的失配根重录，其他根保持原样。每个根输出一行变化摘要：第一个
失配位置（K1 下标或 seed/状态下标）、该步动作与卡牌/药水原生 id、变化的 K1 数、游走状态
数与游走长度。摘要与归因表写进任务报告。重录后的清单写入 `provenance.d1Rerecord`。

#### 重新捕获根

`rerecord` 把根状态当作输入，只重算 K1/K2；当语义修正改的是“从原版状态导入/回放出的根”
本身时，旧冻结根仍是旧行为，必须先用 `Differential --recapture-root` 重新捕获根和
`config.br`。适用条件是：`--config-compare` 或 `--import-compare` 的差异落在根状态，
且差异能归因到一次已确认的原版语义修正；不要用它掩盖 K1/K2 或搜索结果的偏差。

```bash
# 1. 差分宿主从原生状态重新导入/回放，写出载荷（roots.gold + config.br）
dotnet artifacts/bin/Differential/release/Differential.dll \
  --root qf-nightmare-detached --recapture-root \
  --data tests/Golden/data --corpora tests/corpora --out .local/recapture

# 2. 保真检查：全部 410 个根重新捕获，除已归因的根外必须 rootBytes=same configBytes=same
dotnet artifacts/bin/Differential/release/Differential.dll \
  --all --jobs 4 --recapture-root \
  --data tests/Golden/data --corpora tests/corpora --out .local/recapture-all

# 3. Golden 用载荷替换根与 config.br，再按 M0 策略重算 K1/K2 与派生哈希
dotnet run --project tests/Golden -c Release --no-build -- \
  rerecord tests/Golden/data .local/tool-tasks/solver/rerecord-root \
  --roots .local/tool-tasks/solver/rerecord-root.txt \
  --recapture-root .local/recapture --reason "<批次与语义修正>" --jobs 4
```

`--recapture-root` 必须同时给 `--roots` 和 `--reason`；载荷目录按根名分目录，`roots.gold` 是
只含根段的 `GOLD`，`config.br` 是重新编译的 `SREQ`。Golden 不引用游戏程序集：它只读取
差分宿主产出的载荷，替换 `golden.br` 的根段与 `config.br`，其余输入映射、`rulesSha256`
和 K1/K2 规则保持不变。重新捕获只应对已定位的根执行，并在清单的
`provenance.d1Rerecord` 写入 `recaptureRoots=<根名>`；`tests/Golden/data` 仍由协调者决定是否替换。

### 4.2 搜索回归

`tests/Golden/data` 的 D2 期望结果已在 M3b 合并时按正式默认 `all-fill`（完全等价出牌 + 类优先补位选牌）用 `refreeze` 重算，清单带 `provenance.refreeze`；M3b 时 D1 没有变。之后再有会改变搜索结果的改动，先按 4.3 运行 `compare`，经用户同意后在副本上 `refreeze`，再替换仓库数据；不要手改数据。M2c 合并时按 4.1.1 重录了 23 个 D1 根，再在重录副本上 `refreeze`，D2 结果不变。

D2 对 `d2/requests/*.request.br` 的 1,983 个唯一请求：

1. 解码 `SREQ` 请求（规则、战斗、搜索选项）；
2. 把软时间预算改为 `TimeSpan.MaxValue`，清空 pacer、进度、接管与 trace；
3. 运行 `BeamSolver.Solve`；
4. 与 `d2/expected/*.result.br` 逐字段比较路线、TopRoutes、分数 double 位、展开/转移/去重、容量丢弃、终态压缩、药水审计、侦察层、BestRootIndex、PotionPolicySatisfied 与 Takeover。

分片权重取第 4 步所比较的同一个 `ExpandedNodes` 字段。它是确定性计数，与机器无关；全量运行和分片运行都不依赖目录枚举顺序。

`SEARCH_OK` 还输出 `playDeduped`、`playVerifiedExact/Approx`、`choiceByClassRequests`、`choiceVerifiedExact/Approx` 与未决计数；关闭校验开关时 `*Verified*` 项为 0，自检方式见 4.4。

D2 证明：1,983 个请求在去软时间预算后可复现完整搜索结果。
D2 不证明：请求集合之外的根、实机运行、界面与部署行为。

完整 `verify` 的最后一行是 `GOLDEN_VERIFY ... mismatched=0`。解码失败或字段不一致输出 `GOLDEN_INVALID <原因>`，非预期异常输出 `GOLDEN_FAILED <类型>: <消息>`，两者都以非零退出。

### 4.3 对照、校验与重冻

#### `compare`：只报告、不判失败

```bash
dotnet run --project tests/Golden -c Release --no-build -- \
  compare tests/Golden/data --jobs 6 --config all --shard 0/4 \
  --out .local/compare-all-0.csv
```

- `--config none|play|choice|all|choice-fill|all-fill`：`none` 关闭两项去重，`play` 只开完全等价的出牌去重，`choice` 只开旧版选牌等价类（每个类向量一个代表），`all` 是 `play + choice`，`choice-fill` 是类优先并补位置变体，`all-fill` 是 `play + choice-fill`（正式默认）。
- `--shard i/n`：与 `verify` 相同的 D2 权重分片；全量对照按 4 片运行。
- `--out`：逐请求 CSV，含质量分类、路线是否逐动作相同、展开/转移、分数、胜负、`ms` 与等价计数。
- `--verify`：在对照时打开 `STS2_SOLVER_VERIFY_SEARCH_EQUIVALENCE` 的全部检查；精确等价断言失败仍会以 `GOLDEN_FAILED` 退出，近似项只统计。
- 质量用 `BeamSolver.TryCompareResultQuality`：把冻结路线与当前路线分别从同一根重放，按求解器自己的终局比较键（胜负、存活、死亡保护、战略失血、成长、回合、资源、可选药水、敌人 HP、分数、动作数）分类为更好/相同/更差。无法重放时计 `unknown`。
- 本命令不修改 `tests/Golden/data`，也不更新 manifest。

#### `refreeze`：在副本上重算并重写清单

```bash
dotnet run --project tests/Golden -c Release --no-build -- \
  refreeze tests/Golden/data .local/tool-tasks/solver/refreeze-data-fill --jobs 6
dotnet run --project tests/Golden -c Release --no-build -- \
  verify .local/tool-tasks/solver/refreeze-data-fill --jobs 6
```

`refreeze` 先拒绝源/目标互相包含或目标非空，再复制整棵数据目录，用当前代码重解全部 1,983 个 D2 请求，写回 `d2/expected`；旧的 `replay-report-*.json` 合并为一份新报告，最后调用 `ManifestBuilder` 重写清单，并在 `provenance.refreeze` 写入新结果集的 SHA-256。源目录只读。普通 `manifest` 现在保留已有的 `provenance.refreeze` 与 `provenance.d1Rerecord`；在重冻或重录后的数据上重写清单应逐字节不变。

输出 `REFREEZE_OK` 记录源、目标、请求数和结果哈希；`MANIFEST_WRITTEN` 汇总文件大小与来源。逐次输出保存在本地。

### 4.4 搜索等价自检（`Exec` 覆盖合同 + 原地投影）

`tests/KernelChecks` 的 `ExecCoverage` 合同用反射枚举 `Exec` 的全部实例字段（含私有字段），要求每个字段恰好归入一类：进入 `ExecContinuation`、捕获时断言为空/为零、`AdvanceRoundPlayerStart` 入口重置、或单独处理（`B`/`R`/选择游标）。分类表在 `tests/KernelChecks/Combat/ExecCoverage.cs` 一处；同时反向校验快照字段集合与 `ExecContinuation` 完全一致。出现未分类、重复归类或快照表不一致时，消息写出字段名。临时给 `Exec` 加字段的本地反向检查会失败（`ReverseCoverageProbe` 未分类）。

`BeamSolver` 默认使用共享的 `EndTurn` 前缀；设置 `STS2_SOLVER_VERIFY_SEARCH_EQUIVALENCE=1` 后同时开启：

- 每个复用前缀的选择叶子同时执行完整 `Kernel.Step` 与前缀续跑，先逐字节比较 `Battle`（不一致时用 `BattleDump.Diff` 输出字段差异），并比较 `StepResult`、`ChoiceRequest`（选项、来源、时机与生成卡字节）和 `HistoryRisk`；
- 每次 `Scorer.ProjectHpAfterThreat` 原地投影前保存 `Battle`，`finally` 恢复后逐字节比较前后镜像；不一致时用 `BattleDump.Diff` 写出字段差异；
- 每个被跳过的完全等价出牌从父状态执行代表与被跳过的动作，按同一确定策略续完后续选择，逐字节比较 `Battle`；完全等价必须相同，否则抛 `同名出牌完全等价校验失败`；
- 每个选牌输出（类代表或补位物理选择）取一个同类但不同位置的物理选择（各类最后一次出现）与它比较；判定为完全等价时必须相同，判定为近似时只统计实际相同的比例。每请求最多抽样 8 个输出，容量/选择深度失败的比较计未决。

在副本上全量 D2 打开开关：

```bash
STS2_SOLVER_VERIFY_SEARCH_EQUIVALENCE=1 \
  dotnet run --project tests/Golden -c Release --no-build -- \
  verify .local/golden-copy --only search --jobs 6
```

完全等价检查失败会直接报错；近似项和未决项只统计。用输出的计数判断实际覆盖范围，逐次结果保存在本地实验记录中。

## 5. manifest

```bash
dotnet run --project tests/Golden -c Release --no-build -- manifest tests/Golden/data
```

`manifest` 按路径 ordinal 排序，逐文件写 `path`/`bytes`/`sha256`，并写 `formatVersion`、`battleSize`、`provenance`、`compression`、`counts`、`totalBytes`、`targetBytes`。当前：

```text
MANIFEST_WRITTEN files=5589 totalBytes=23684198 d1Files=410 d1Roots=418 d1K1=5062
  d1Walks=15296 d1WalkStates=482209 d1Transitions=469334 d1Configs=410
  d2Requests=1983 d2Expected=1983
```

manifest 是确定性的：同一份 `data/` 重写后应逐字节相同。验证时：

```bash
git diff --exit-code -- tests/Golden/data/manifest.json
```

manifest 证明：文件集合、大小、SHA-256、计数与 provenance 自洽。
manifest 不证明：语义正确性；它只保护冻结快照不被静默改动。

## 6. 差分宿主（需要游戏文件，不在 CI 中运行）

`tests/Differential` 是不启动 Godot 的控制台工程：它初始化游戏静态状态、用游戏自己的
`RunManager`/`ModelDb` 建局、推进到玩家可操作，写出原生 `CombatState` 的确定性转储，并可用
`--import-compare` 调 `src/Import` 导入 `Battle`、与 `golden.br` 的 `BATL` 逐字节对照，用
`--rules-compare` 编译 `Rules` 后与冻结规则对象逐字节对照，或用 `--config-compare` 编译
`ScoreRules`/`BoundFeatures.Config` 后与 `config.br` 逐字节对照；`--search-compare` 还能
重放游戏写出的确定性 `SREQ`/`SRES` 并核对离线组装的请求。工程**不加入**
`Sts2Solver.sln`；CI 没有游戏文件，只保证 `dotnet build Sts2Solver.sln` 不受影响。

需要本机游戏目录（`sts2.dll`、`GodotSharp.dll`、`0Harmony.dll`）。路径通过
`tests/Differential/local.props` 配置；该文件被 git 忽略，模板是 `local.props.example`。
RitsuLib 不需要。建局、注入与转储代码与 Mod 共用 `src/TestHost/`，不在两边复制。
完整说明见 [`tests/Differential/README.md`](../tests/Differential/README.md)。

```bash
dotnet build tests/Differential/Differential.csproj -c Release
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 8 --out .local/offlinehost/out
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
dotnet artifacts/bin/Differential/release/Differential.dll   --root kg2-b-stomp --import-compare

# 全部 410 个输入（前缀根回放，回合准备 fixture 枚举全部分支）
dotnet artifacts/bin/Differential/release/Differential.dll   --all --jobs 8 --import-compare

# 改用规则编译器输出，而不是解码冻结规则对象
dotnet artifacts/bin/Differential/release/Differential.dll   --all --jobs 8 --import-compare --compiled-rules
```

每根输出 `same`、`diff` 或 `reject`。`diff` 打印 `BattleDump.Diff` 的前 24 行，`reject` 打印稳定原因码与 detail；`--all` 汇总计数并按首个差异字段、拒绝原因分组。前缀根通过“导入前缀前状态 + 内核回放”比较，回合准备 fixture 按原生准备选项逐个重建。字段来源见 [原生状态导入](import.md)。

### 6.2 规则编译对照

```bash
# 单个根：编译 Rules → RulesCodec.Encode → 与冻结规则对象逐字节比较
dotnet artifacts/bin/Differential/release/Differential.dll   --root kg2-b-stomp --rules-compare

# 全部 410 个输入
dotnet artifacts/bin/Differential/release/Differential.dll   --all --jobs 8 --rules-compare

# 静态镜像覆盖率表合同：没有遗漏/多余 id，且覆盖全部 Sts2Solver Registry 内容
dotnet artifacts/bin/Differential/release/Differential.dll   --rules-table-check
```

每根输出 `same`、`diff` 或 `reject`。`diff` 打印 `RulesDiff` 的第一处表/项及前 16 行差异；`reject` 打印稳定原因码与 detail。`--all` 汇总不同冻结规则对象数，`--rules-table-check` 报告规则覆盖与缺项。

### 6.3 评估配置对照

```bash
# 单个根：编译 ScoreRules/Bound → 重组 SREQ → 与 config.br 逐字节比较
dotnet artifacts/bin/Differential/release/Differential.dll   --root kg2-b-stomp --config-compare

# 全部 410 个输入
dotnet artifacts/bin/Differential/release/Differential.dll   --all --jobs 8 --config-compare
```

比较整份请求及回合准备 fixture 的全部准备根；依次核对规则、根、评分表、下界与其余选项，并打印第一处差异。同时用 `GoldenHash` 检查 `scoreHash`/`boundHash`。

### 6.4 实机捕获结果对照

Mod 在无头游戏里把 `BATL`/`RULE`/`SREQ.capture`/`native.txt` 写进结果目录后，离线宿主可以对照：

```bash
dotnet artifacts/bin/Differential/release/Differential.dll \
  --live-compare .local/m2b1-20-results
```

每根比较：

- `BATL` 与 `d1/<root>/golden.br` 的期望根（回合准备 fixture 按 `setupSelections` 选根）；
- `RULE` 与冻结规则对象；
- `SREQ.capture` 与 `config.br`（回合准备 fixture 为 `scope=subparts`，只比评分/下界子部件；
  旧结果没有 `SREQ.capture` 时回退到 `SREQ`）；
- `native.txt` 与离线宿主用同一请求重建的 `NativeStateDump`。

输出逐根 `live ... status=same ... native=same`，最后汇总 `live roots=N same=... diff=... reject=... native=...`。回合准备 fixture 的请求比较范围为 `scope=subparts`。启动脚本、协议与测试会话的幕结构处理见 [Mod 入口与无头捕获](mod.md)。

生成实机请求：

```bash
dotnet artifacts/bin/Differential/release/Differential.dll \
  --emit-live-requests .local/live-requests --roots-file .local/m2b0-20-roots.txt
dotnet artifacts/bin/Differential/release/Differential.dll \
  --emit-live-requests .local/live-requests-all --all
```

`--all` 跳过 13 个前缀输入，输出 397 个普通请求；回合准备 fixture 也在其中。
`--emit-live-requests` 还接受 `--search-tier Low|Medium|High|ExtraHigh`（默认 `Medium`）、
`--search-normal`（关掉确定性软时间）与
`--search-cancel-after-ms N`（取消测试）。

### 6.5 实机搜索请求/结果对照

单次捕获测试默认用中档确定性旧格式选项求解一次（软时间关闭、宽度 300、节点上限 512,000），写
`SREQ`/`SRES`/`search.json`。离线宿主：

```bash
dotnet artifacts/bin/Differential/release/Differential.dll \
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

### 6.6 实机整场自动战斗

`OfflineRequest` 增加 `fullCombatForTest`；`--emit-live-requests` 的 `--full-combat` 把它置位
并关闭确定性。测试模式捕获根后立刻进入 `AutoCombatSession`，由共享的 `CombatPilot`
每回合搜索/沿用路线、逐动作核对、回合对账，直到胜利、失败、暂停或取消。结果写 `full-combat.json`，并在 `status.json`
增加 `fullCombatStatus/outcome/turns/playerHp/playerMaxHp/searches/reusedTurns/deviations/
deploymentFailures`。每个搜索成员按自身宽度计算独立时限；`src/Mod` 还会把
`headlessFastModeForTest` 覆盖为 `Instant`（默认）并在结束后恢复。

命令：

```bash
tools/testing/run-headless.sh --instance m2b2-full-1 \
  --roots-file .local/full-1.txt --full-combat \
  --results .local/m2b2-full-1-results \
  --cleanup-instance-on-exit --timeout-seconds 480
```

取消检查可使用 `--full-combat --search-cancel-after-ms 5000`，检查返回状态、工作任务退出和主菜单恢复。

### 6.7 离线整场自动战斗

离线工具从真实语料导入根，使用与 Mod 相同的组合协调器和请求级时间、内存预算；每根一个子进程，`--jobs` 控制并发。它在内核中执行搜索路线，用于质量评估；原版逐动作及终局核对另用 `--combat-pilot-compare`。

```bash
dotnet artifacts/bin/Differential/release/Differential.dll \
  --offline-combat --corpora tests/corpora --jobs 4 --search-tier Medium \
  --out .local/offline-combat/medium

dotnet artifacts/bin/Differential/release/Differential.dll \
  --offline-combat --root quality-REGENT-010 --search-tier Medium \
  --out .local/offline-combat/one

dotnet artifacts/bin/Differential/release/Differential.dll \
  --combat-pilot-compare --pilot-root quality-REGENT-010
```

每根的 `full-combat.json` 记录胜负、重搜、执行失败与每次搜索的完整成员明细；父进程的 `summary.json` 汇总档位、四种宽度、时间和内存预算。成员明细包含执行/跳过、停止原因、展开数、路线质量和内存峰值。墙钟控制的成员可能在不同机器截断于不同位置，不能当确定性 Golden 比较。`--memory-budget-mb` 可调整总内存，旧加宽与节点上限参数已移除。

### 6.8 搜索内存与计时

搜索账本统计状态、路由、辅助数组和标签等逻辑存储；进程托管堆与工作集峰值另行记录。极小预算合同检查分配拒绝、可回放的回退路线和复用；分批压缩合同单独对照启用与关闭。账本不是游戏进程 RSS 限制。搜索实现和档位见 [战斗搜索](combat-ai.md)；测量结果保存在本地 `.local/`。

### 6.9 负例

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --import-negative
```

默认在 `p8-c-rage-juggling-dark-embrace` 上验证三条路径：把 Power 的内部数据置空得到
`missing-private-field`；向认领账本注入未认领条目得到 `unclaimed-private-state`；把
`Differential` 程序集里的测试 Power 挂到玩家身上得到 `third-party-content`。三条都必须拒绝，
输出 `negative failures=0`。

离线宿主证明：不启动 Godot 时，410 个冻结输入能建出与参考导出相同的原生战斗状态；410 个
输入（418 个根）能导入并与 GOLD 的 `BATL` 逐字节相同，也能编译出与冻结规则对象逐字节相同
的 `Rules`，还能编译出与 `config.br` 相同的评分表与下界；3 类私有状态/第三方内容负例显式
拒绝。它不证明：实机部署、界面、准备阶段的多段选择，以及未捕获的第三方 Mod 内容。实机
状态导入与单 Beam 搜索已由 `src/Mod` 的 20 根捕获、`--live-compare` 与 `--search-compare`
覆盖。

### 6.10 原版战斗动作与终局对照

```bash
# 当前质量语料，CombatPilot 低档；默认并行 8 个子进程
dotnet artifacts/bin/Differential/release/Differential.dll --combat-pilot-compare

# 单根；终局 diff 返回 1
dotnet artifacts/bin/Differential/release/Differential.dll \
  --combat-pilot-compare --pilot-root quality-IRONCLAD-001
```

每个用例在独立子进程里：通过与战斗捕获共用的 `ScenarioBuilder` 建立原版战斗；`CombatPilot`
低档生成路线，`NativePilotHost` 逐动作交给 `NativeActionExecutor` 用原版公开入口执行。
每个**可出牌**动作边界仍用 `LiveCombatTracker.ImportForComparison` 读回 `Battle`，与
`action.After` 逐字节比较。

终局另做独立取证：`TerminalBattleCapture` 在 `CombatManager.EndCombatInternal`（胜利）与
`ProcessPendingLoss`（失败）的 **prefix** 捕获原版 `Battle`，再与预测终局比较。比较合同是
显式字段：`Outcome`（由捕获来源的胜利/失败路径判定，不由预测 Outcome 反推）、玩家
`CurrentHp`/`MaxHp`、`Gold`、药水槽、遗物 `Id/Flags/Counter/Aux` 与 9 条战斗 RNG。手牌/
抽牌堆/弃牌堆/消耗堆、回合内计数、敌人/Power 行等在原版清理中会变化，未纳入合同；因此不
宣称终局 `Battle` 逐字节全同。合同内字段均独立读取原版；常规导入器携带的搜索专用计数不参与终局比较，
也没有重实现战后 Hook 或奖励生成。捕获缺失和等待超时报告 `executor-diff`，原因分别为
`terminal_missing`、`boundary_timeout`；明确不支持的导入报告 `reject`，不计 same。
`EventDefeat` 当前以 `terminal_diff` 明确报告未覆盖，退出码为 1。

`status=same` 表示所有动作边界和终局合同相同；`status=executor-diff` 是内核预测与原版边界
不同；`status=reject` 带稳定原因码。`--pilot-root` 的 diff 退出码为 1；父进程先看 child exit，
非零 child 即使打印了 `same` 或 `reject` 也不会被计入正常结果（有明确 `executor-diff` 计 diff，
否则计 crash）。`actions` 是真实部署动作数，`turns` 是回合数。

定向合同覆盖 TheGambit 导致玩家死亡后不再调用后续玩家监听，以及 LizardTail 阻止死亡后仍允许监听执行的边界。
负例：`STS2_SOLVER_PILOT_TAMPER=hp`、`outcome`、`event` 篡改预测终局，检查分别报告
`player.hp`、`outcome`、`event-defeat is not covered`，退出码均为 1。

## 7. 各层校验范围

以下说明检查通过时的含义，具体批次是否通过以本地报告为准。

| 层 | 校验内容 | 不覆盖 |
| --- | --- | --- |
| 构建与架构检查 | 编译、警告及依赖边界 | 运行时正确性 |
| K0 | 布局、RNG、编码、容量与注册表合同 | 完整搜索与实机部署 |
| 模拟回归 | 冻结输入的逐步重放与特征哈希 | 未捕获状态、实机导入 |
| 搜索回归 | 冻结请求的完整确定性搜索结果 | 未捕获请求、生产时间预算下的搜索质量 |
| manifest | 快照完整性与来源声明 | 语义正确性 |
| 差分宿主 | 选定输入的原生状态、规则、导入、请求、随机流及边界执行对照 | 界面、任意第三方内容 |
| Mod 无头捕获 | 实机捕获与冻结快照、离线重建的比较 | 路线执行、界面 |
| 实机确定性搜索 | 游戏与离线宿主的请求和结果比较 | 生产组合调度的搜索质量 |
| 实机整场 | 自动执行、逐动作核对、回合对账、选牌与取消清理 | 语料外内容、界面、未捕获前缀与准备根 |

冻结回归、搜索质量评估和实机部署是不同检查；其中一项通过不能替代其它项。数据来源与更新条件见 [冻结快照](golden-data.md)。

## 8. 适用边界

- **覆盖范围**：每次运行只覆盖命令选定的根与配置，不能从局部样本推断全部普通根、全部质量语料或整局都已验证。
- **界面**：没有设置页、进度、路线预览或失败提示；普通玩家只有 `F8` 开关与日志。
- **非选牌模态**：选择驱动只覆盖 `CardSelectCmd` 的 `ICardSelector` 路径；未验证未来/第三方 Mod 的其它模态请求。
- **前缀根**：实机测试模式显式拒绝前缀动作；继续由离线宿主的 `PrefixReplay` 覆盖。
- **回合准备多根**：实机只捕获第一个准备选项；完整 `config.br` 的多根排序仍只在差分宿主验证。
- **第三方 Mod**：没有实机 Mod 根；导入层只显式拒绝非 `sts2` 程序集的模型，未做兼容层。
- **Windows/Steam**：当前计时与验证在 Linux；Windows `.ps1` 启动脚本留待后续。

未覆盖的内容不应被文档或提交信息描述为已验证。冻结快照的更新条件见 [冻结快照](golden-data.md) 第 11 节。
