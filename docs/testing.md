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

- `tests/KernelChecks`：`"contracts": "Passed"`。覆盖 `Battle`/`Card`/`Enemy`/`Power`/`RngState` 等布局尺寸、RNG 参考向量、牌堆与容量失败、选择协议、内容注册表与编码合同。
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
D2_OK selected=1983 equal=1983 mismatch=0 expanded=15739416
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

### 4.3 计时

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

M1b 的 D2 为 331.3s；v2 迁移后为 283.9s；本轮改为按权重调度后全量 267.2s。全量总展开数不变，差异来自调度与测量波动。

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
`--import-compare` 调 `src/Import` 导入 `Battle`、与 `golden.br` 的 `BATL` 逐字节对照，或用
`--rules-compare` 编译 `Rules` 后与冻结规则对象逐字节对照。它不跑搜索。工程**不加入**
`BlitzSolver.sln`；CI 没有游戏文件，只保证 `dotnet build BlitzSolver.sln` 不受影响。

需要本机游戏目录（`sts2.dll`、`GodotSharp.dll`、`0Harmony.dll`）。路径通过
`tests/OfflineHost/local.props` 配置；该文件被 git 忽略，模板是 `local.props.example`。
RitsuLib 不需要。完整说明见 [`tests/OfflineHost/README.md`](../tests/OfflineHost/README.md)。

```bash
dotnet build tests/OfflineHost/OfflineHost.csproj -c Release
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll --all --jobs 8 --out .local/offlinehost/out
```

`--root <name>` 建单个根；`--all` 为每个根起一个子进程；`--compare-only <new> <ref>`
逐根比较转储并打印第一处差异。当前 410 个输入与参考转储逐字节相同；参考转储由 CombatSolver `flash/m0` 的 `fe428fca` 生成（该提交给 M0 离线宿主加了同一份 `NativeStateDump`）。

### 6.1 导入对照

```bash
# 单个根：建原生状态 → 导入 → 与 golden.br 的第一个/普通根 BATL 比较
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --root kg2-b-stomp --import-compare

# 397 个在范围根；13 个前缀 fixture 自动跳过
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --all --jobs 8 --import-compare

# 改用规则编译器输出，而不是解码冻结规则对象
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --all --jobs 8 --import-compare --compiled-rules
```

每根输出 `same`、`diff` 或 `reject`。`diff` 随后打印 `BattleDump.Diff` 的前 24 行，每行带
`+偏移 字段: golden=[...] import=[...]`；`reject` 打印稳定原因码与 detail。`--all` 汇总三类
计数，并按首个差异字段、拒绝原因分组。当前：

```text
import same=397 diff=0 reject=0
```

`qf2-gradient-failure`、`ts-b-toolbox`、`ts-a-toasty` 的普通根分别是 GOLD 的下标 1/0/1；
额外 8 个回合准备根不在本批。对照模式用原生第一个准备选项匹配对应的 golden 根，原因与字段
来源见 [原生状态导入](import.md)。

### 6.2 规则编译对照

```bash
# 单个根：编译 Rules → RulesCodec.Encode → 与冻结规则对象逐字节比较
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --root kg2-b-stomp --rules-compare

# 397 个在范围根；13 个前缀 fixture 自动跳过
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --all --jobs 8 --rules-compare

# 静态镜像覆盖率表合同：没有遗漏/多余 id，且覆盖全部 Blitz Registry 内容
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --rules-table-check
```

每根输出 `same`、`diff` 或 `reject`。`diff` 随后打印 `RulesDiff` 的第一处表/项及前 16 行差异；
`reject` 打印稳定原因码与 detail。`--all` 同时打印 397 个根覆盖的不同冻结规则对象数。当前：

```text
rules-objects distinct=376 roots=397
rules same=397 diff=0 reject=0
RULES-TABLES cards=603 powers=283 native_cards=603 native_powers=283 \
  missing_cards=0 extra_cards=0 missing_powers=0 extra_powers=0 registry_missing=0
```

### 6.3 负例

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll --import-negative
```

默认在 `p8-c-rage-juggling-dark-embrace` 上验证三条路径：把 Power 的内部数据置空得到
`missing-private-field`；向认领账本注入未认领条目得到 `unclaimed-private-state`；把
`OfflineHost` 程序集里的测试 Power 挂到玩家身上得到 `third-party-content`。三条都必须拒绝，
输出 `negative failures=0`。

离线宿主证明：不启动 Godot 时，410 个冻结输入能建出与参考导出相同的原生战斗状态；397 个在
范围根能直接导入并与 GOLD 的 `BATL` 逐字节相同，也能编译出与冻结规则对象逐字节相同的
`Rules`；3 类私有状态/第三方内容负例显式拒绝。它不证明：13 个前缀根、8 个额外回合准备根、
实机运行，以及未捕获的第三方 Mod 内容。

## 7. 每层证明什么

| 层 | 证明 | 不证明 |
| --- | --- | --- |
| 构建 | 源码可编译、无警告、只有 BCL 依赖 | 运行时正确性 |
| K0 | 布局、RNG、编码、容量与注册表合同 | 搜索与冻结输入 |
| D1 | 410 个冻结输入的逐步重放与特征哈希 | 未捕获状态、实机导入 |
| D2 | 1,983 个冻结请求的完整搜索结果 | 未捕获请求、实机部署 |
| manifest | 快照完整性与来源声明 | 语义正确性 |
| 离线宿主（手动） | 410 个原生 `CombatState` 与参考转储逐字节相同；397 个在范围根导入 `Battle` 与 GOLD 逐字节相同，编译 `Rules` 与冻结规则对象逐字节相同 | 前缀/额外回合准备根、实机运行、任意 Mod |

这些层合起来证明：当前源码对 `tests/Golden/data` 的冻结证据是可复现的。它们不证明：BlitzSolver 能在真实游戏中导入状态、在真实战斗中部署、在界面上显示，或支持任意第三方 Mod。

## 8. 未验证项

- **实机运行**：尚未在游戏内启动 BlitzSolver。
- **状态导入**：离线宿主的 `--import-compare` 已覆盖 397 个在范围根的 `Battle`；`--rules-compare` 已覆盖规则编译，评估配置留给 M2a-4。
- **实机状态导入**：尚未从游戏运行时把 `CombatState`/`RunState` 投影成 `Battle`。
- **部署**：尚未把搜索结果翻译回游戏动作并执行。
- **界面**：没有设置页、进度、路线预览或失败提示。
- **第三方 Mod**：没有实机 Mod 根；导入层只显式拒绝非 `sts2` 程序集的模型，未做兼容层。
- **Windows/Steam**：当前计时与验证在 Linux；Windows 未单独验证。

这些项目在接入层完成前不应被文档或提交信息描述为已验证。冻结快照的更新条件见 [冻结快照](golden-data.md) 第 11 节。
