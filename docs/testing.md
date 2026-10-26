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
- `BlitzSolver.Architecture.dll`

### 2.1 层依赖规则

```bash
dotnet run --project tests/Architecture -c Release --no-build
```

`tests/Architecture` 读取编译后的 `BlitzSolver.Core`，检查 `Simulation.Combat`、`Simulation.Run`、
`Content`、`Ai.Combat`、`Ai.Run` 与 `Autoplay` 之间的类型引用；BCL 引用一律允许。违反时逐条输出
`源类型 -> 目标类型 via ...` 并以非零退出。`Ai.Run` 目前只允许 `Simulation.Run` 中显式登记为可见
投影 / 决策请求的类型（当前登记为空）；测试程序集里的本地反例同时验证未登记引用会报错、登记后
会被允许。规则表见 [整局设计](full-run-design.md) 第 4 节。

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

D1 证明：冻结的 410 个 D1 输入在 BlitzSolver 上可逐字节重放，评分/下界/路线标签特征与录制时一致。其中 23 个根在 M2c 按原版证据修正语义后由 Blitz 重录（`provenance.d1Rerecord`），其余 387 个根仍是 M0 宿主的录制。
D1 不证明：未捕获的状态、实机导入、任意第三方内容。

### 4.1.1 D1 重录与保真检查

语义修正后旧 D1 录制本身会变成旧行为，需要 Blitz 自己重录。命令：

```bash
# 1. 在未改语义的代码上证明工具可复现旧录制：全部 410 个根逐字节相同
dotnet run --project tests/Golden -c Release --no-build -- \
  rerecord tests/Golden/data .local/tool-tasks/blitz/rerecord-fidelity \
  --roots .local/tool-tasks/blitz/rerecord-roots-all.txt --jobs 6
diff -r tests/Golden/data/d1 .local/tool-tasks/blitz/rerecord-fidelity/d1

# 2. 语义修正后只重录失配可归因的根
dotnet run --project tests/Golden -c Release --no-build -- \
  rerecord tests/Golden/data .local/tool-tasks/blitz/rerecord-d1 \
  --roots .local/tool-tasks/blitz/rerecord-roots.txt --jobs 6
dotnet run --project tests/Golden -c Release --no-build -- \
  verify .local/tool-tasks/blitz/rerecord-d1 --only d1 --jobs 6
```

第一项是阶段 C 的门槛：`rerecord` 解出旧记录，保留根状态、`config.br` 与 `meta.json` 的
输入映射/规则哈希，用 M0 的种子与游走策略重算 K1/K2 与三个派生哈希，再以
`CompressionLevel.SmallestSize` 写回。未改语义时 410 个根的 `golden.br` 必须逐字节相同；
计数不变时 `meta.json` 不重写，因此也逐字节相同。若字节不同但解码后字段相同，报告必须
说明压缩层原因。该检查不过，不做语义修正后的重录。

第二项只对 `verify` 报出的失配根重录，其他根保持原样。每个根输出一行变化摘要：第一个
失配位置（K1 下标或 seed/状态下标）、该步动作与卡牌/药水原生 id、变化的 K1 数、游走状态
数与游走长度。摘要与归因表写进任务报告。重录后的清单写入 `provenance.d1Rerecord`。

### 4.2 D2

`tests/Golden/data` 的 D2 期望结果已在 M3b 合并时按正式默认 `all-fill`（完全等价出牌 + 类优先补位选牌）用 `refreeze` 重算，清单带 `provenance.refreeze`；M3b 时 D1 没有变。之后再有会改变搜索结果的改动，先按 4.3 运行 `compare`，经用户同意后在副本上 `refreeze`，再替换仓库数据；不要手改数据。M2c 合并时按 4.1.1 重录了 23 个 D1 根，再在重录副本上 `refreeze`，D2 结果不变。

D2 对 `d2/requests/*.request.br` 的 1,983 个唯一请求：

1. 解码 `SREQ` 请求（规则、战斗、搜索选项）；
2. 把软时间预算改为 `TimeSpan.MaxValue`，清空 pacer、进度、接管与 trace；
3. 运行 `BeamSolver.Solve`；
4. 与 `d2/expected/*.result.br` 逐字段比较路线、TopRoutes、分数 double 位、展开/转移/去重、容量丢弃、终态压缩、药水审计、侦察层、BestRootIndex、PotionPolicySatisfied 与 Takeover。

分片权重取第 4 步所比较的同一个 `ExpandedNodes` 字段。它是确定性计数，与机器无关；全量运行和分片运行都不依赖目录枚举顺序。

`D2_OK` 还输出 `playDeduped`、`playVerifiedExact/Approx`、`choiceByClassRequests`、`choiceVerifiedExact/Approx` 与未决计数；关闭校验开关时 `*Verified*` 项为 0，打开后的实测见 4.4。

D2 证明：1,983 个请求在去软时间预算后可复现完整搜索结果。
D2 不证明：请求集合之外的根、实机运行、界面与部署行为。

完整 `verify` 的最后一行是 `GOLDEN_VERIFY ... mismatched=0`。解码失败或字段不一致输出 `GOLDEN_INVALID <原因>`，非预期异常输出 `GOLDEN_FAILED <类型>: <消息>`，两者都以非零退出。

### 4.3 M3b 对照、校验与重冻

#### `compare`：只报告、不判失败

```bash
dotnet run --project tests/Golden -c Release --no-build -- \
  compare tests/Golden/data --jobs 6 --config all --shard 0/4 \
  --out .local/compare-all-0.csv
```

- `--config none|play|choice|all|choice-fill|all-fill`：`none` 关闭两项去重，`play` 只开完全等价的出牌去重，`choice` 只开旧版选牌等价类（每个类向量一个代表），`all` 是 `play + choice`，`choice-fill` 是类优先并补位置变体，`all-fill` 是 `play + choice-fill`（正式默认）。
- `--shard i/n`：与 `verify` 相同的 D2 权重分片；全量对照按 4 片运行。
- `--out`：逐请求 CSV，含质量分类、路线是否逐动作相同、展开/转移、分数、胜负、`ms` 与等价计数。
- `--verify`：在对照时打开 `BLITZ_VERIFY_SEARCH_EQUIVALENCE` 的全部检查；精确等价断言失败仍会以 `GOLDEN_FAILED` 退出，近似项只统计。
- 质量用 `BeamSolver.TryCompareResultQuality`：把冻结路线与当前路线分别从同一根重放，按求解器自己的终局比较键（胜负、存活、死亡保护、战略失血、成长、回合、资源、可选药水、敌人 HP、分数、动作数）分类为更好/相同/更差。无法重放时计 `unknown`。
- 本命令不修改 `tests/Golden/data`，也不更新 manifest。

M3b 在 1,983 个 D2 请求上的 `--jobs 6` 四片合计（`solveMsSum` 是逐请求求解时间之和，墙钟是四片顺序运行的合计；基线 `none` 的第一片同时含慢请求 `40e330…`）：

| 配置 | 更好/相同/更差 | 胜利 +/− | 展开 | 转移 | 求解 ms 合计 | 四片墙钟 | 最慢片 |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| `none`（冻结基线） | 0 / 1,983 / 0 | 0 / 0 | 15,739,416 | 89,023,995 | 1,013,005 | 295.1s | 177.4s |
| `play`（完全等价出牌） | 0 / 1,983 / 0 | 0 / 0 | 15,739,416 | 84,595,399（−4.98%） | 1,012,406 | 292.6s | 176.2s |
| `choice`（旧版选牌等价类） | 97 / 1,816 / 70 | +11 / −5 | 15,627,412（−0.71%） | 80,329,530（−9.77%） | 912,795 | 196.8s | 82.7s |
| `all`（`play + choice`） | 97 / 1,816 / 70 | +11 / −5 | 15,627,412（−0.71%） | 75,891,681（−14.75%） | 888,978 | 193.7s | 82.3s |
| `choice-fill` | 29 / 1,941 / 13 | +5 / −4 | 15,568,469（−1.09%） | 77,836,748（−12.57%） | 859,367 | 157.6s | 39.9s |
| `all-fill`（正式默认） | 29 / 1,941 / 13 | +5 / −4 | 15,568,469（−1.09%） | 73,395,937（−17.56%） | 843,219 | 154.8s | 39.2s |

`all-fill` 与 `all` 的质量差异集中在“位置变体是否被保留”：

| 配置 | 更差（截断请求/未截断） | 更好（截断/未截断） | 胜利变失败（截断/未截断） | 失败变胜利（截断/未截断） |
| --- | --- | --- | --- | --- |
| `all` | 70（15 / 55） | 97（47 / 50） | 5（2 / 3） | 11（5 / 6） |
| `all-fill` | 13（13 / 0） | 29（29 / 0） | 4（4 / 0） | 5（5 / 0） |

旧版 `choice` 在未截断请求上虽然集合相同（32 个名额足够），但把类代表顺序放在前面，会改变池的插入/同分顺序；`all-fill` 对未截断请求直接使用原 `Enumerate`，把所有这类变化清零。截断请求里 `choice-fill` 先保留不同内容选法，再补位置变体，13 个变差和 29 个变好全部落在这一子集。

慢请求 `40e330…`（REGENT-013）：

| 配置 | 展开 | 转移 | 求解时间 | 路线/质量 |
| --- | ---: | ---: | ---: | --- |
| `none` | 283,539 | 12,430,403 | 177.1s | 冻结 |
| `choice` | 143,643 | 6,105,755 | 82.4s | 逐动作相同，相同 |
| `all` | 143,643 | 6,072,056 | 82.0s | 逐动作相同，相同 |
| `all-fill` | 132,270 | 1,573,784 | 23.0s | 逐动作相同，相同 |

`play` 完全等价出牌只改变转移/去重计数，路线、展开与质量全部相同；`all-fill` 与 `choice-fill` 的质量完全相同，说明完全等价出牌没有叠入质量变化。`all-fill` 的 29 个更好对 13 个更差、净多 1 次胜利（+5/−4）；旧 `all` 虽然净多 6 次胜利（+11/−5），但其中 55 个变差、3 个胜利变失败发生在未截断请求，纯粹来自类代表顺序/位置变体的丢失。按“未截断请求零变化”的标准，正式默认选 `all-fill`。近似出牌去重评估后已撤掉，不在当前 `compare` 配置中。

按 D1 根匹配（`Fingerprint.Hash` + 完整字节比较，1,315/1,983 个请求命中 D1 根；其余 668 个无法定位根），`all-fill`：

| 角色 | 请求 | 更好 | 相同 | 更差 | 胜利 +/− | 展开 | 转移 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| REGENT | 255 | 11 | 237 | 7 | +5 / −4 | −9.89% | −59.16% |
| DEFECT | 276 | 2 | 274 | 0 | 0 / 0 | −0.28% | −4.66% |
| NECROBINDER | 254 | 7 | 247 | 0 | 0 / 0 | +0.03% | −3.78% |
| SILENT | 252 | 0 | 252 | 0 | 0 / 0 | +0.01% | −12.11% |
| IRONCLAD | 278 | 0 | 278 | 0 | 0 / 0 | −0.00% | −5.13% |
| 未匹配 | 668 | 9 | 653 | 6 | 0 / 0 | −0.23% | −5.66% |

D2/D1 数据没有幕字段；`quality-*` 根名只带角色，`a3-*` 根可确定为第三幕。上表只按角色汇总，不按幕拆分。变差集中在未匹配根（6）、`quality-REGENT-015`（4）、`quality-REGENT-008`（2）与 `quality-REGENT-002`（1）；4 个胜利变失败全部在截断请求里，分别是 `22850c40`/`e690d0fa`（REGENT-015）、`a7e0e220`（REGENT-002）、`0e87f3a0`（REGENT-008）。

#### `refreeze`：在副本上重算并重写清单

```bash
dotnet run --project tests/Golden -c Release --no-build -- \
  refreeze tests/Golden/data .local/tool-tasks/blitz/refreeze-data-fill --jobs 6
dotnet run --project tests/Golden -c Release --no-build -- \
  verify .local/tool-tasks/blitz/refreeze-data-fill --jobs 6
```

`refreeze` 先拒绝源/目标互相包含或目标非空，再复制整棵数据目录，用当前代码重解全部 1,983 个 D2 请求，写回 `d2/expected`；旧的 `replay-report-*.json` 合并为一份新报告，最后调用 `ManifestBuilder` 重写清单，并在 `provenance.refreeze` 写入新结果集的 SHA-256。源目录只读。普通 `manifest` 现在保留已有的 `provenance.refreeze` 与 `provenance.d1Rerecord`；在重冻或重录后的数据上重写清单应逐字节不变。

本次副本演示输出：

```text
REFREEZE_OK source=.../tests/Golden/data target=.../.local/tool-tasks/blitz/refreeze-data-fill
  requests=1983 captured=2802 jobs=6
  refreeze=M3b current-code results sha256=682e89f0e6855fcd303a4f7270986d6aa2dcafbd5c637ae094d78713ecfde8f6
MANIFEST_WRITTEN files=5589 totalBytes=23692454 d1Files=410 d1Roots=418 ...
```

副本上严格 `verify` 通过：D1 0 不一致；D2 `selected=1983 equal=1983 mismatch=0 expanded=15568469`，wall 122.3s（jobs 6）。用户同意后，M3b 合并到 main 时在合并后的代码上对 `tests/Golden/data` 重跑同一命令，结果集 SHA-256 与副本相同（`682e89f0…`），随后替换仓库数据；替换后严格 `verify` 为 D1 0 不一致、D2 1983/1983，等价开关下的计数与 4.4 相同。

### 4.4 搜索等价自检（`Exec` 覆盖合同 + 原地投影）

`tests/KernelChecks` 的 `ExecCoverage` 合同用反射枚举 `Exec` 的全部实例字段（含私有字段），要求每个字段恰好归入一类：进入 `ExecContinuation`、捕获时断言为空/为零、`AdvanceRoundPlayerStart` 入口重置、或单独处理（`B`/`R`/选择游标）。分类表在 `tests/KernelChecks/Combat/ExecCoverage.cs` 一处；同时反向校验快照字段集合与 `ExecContinuation` 完全一致。出现未分类、重复归类或快照表不一致时，消息写出字段名。临时给 `Exec` 加字段的本地反向检查会失败（`ReverseCoverageProbe` 未分类）。

`BeamSolver` 默认使用共享的 `EndTurn` 前缀；设置 `BLITZ_VERIFY_SEARCH_EQUIVALENCE=1` 后同时开启：

- 每个复用前缀的选择叶子同时执行完整 `Kernel.Step` 与前缀续跑，先逐字节比较 `Battle`（不一致时用 `BattleDump.Diff` 输出字段差异），并比较 `StepResult`、`ChoiceRequest`（选项、来源、时机与生成卡字节）和 `HistoryRisk`；
- 每次 `Scorer.ProjectHpAfterThreat` 原地投影前保存 `Battle`，`finally` 恢复后逐字节比较前后镜像；不一致时用 `BattleDump.Diff` 写出字段差异；
- 每个被跳过的完全等价出牌从父状态执行代表与被跳过的动作，按同一确定策略续完后续选择，逐字节比较 `Battle`；完全等价必须相同，否则抛 `同名出牌完全等价校验失败`；
- 每个选牌输出（类代表或补位物理选择）取一个同类但不同位置的物理选择（各类最后一次出现）与它比较；判定为完全等价时必须相同，判定为近似时只统计实际相同的比例。每请求最多抽样 8 个输出，容量/选择深度失败的比较计未决。

在副本上全量 D2 打开开关的命令与结果：

```bash
BLITZ_VERIFY_SEARCH_EQUIVALENCE=1 \
  dotnet run --project tests/Golden -c Release --no-build -- \
  verify .local/tool-tasks/blitz/refreeze-data-fill --only d2 --jobs 6
```

```text
D2_OK selected=1983 equal=1983 mismatch=0 expanded=15568469
  prefixChecks=1881251 threatChecks=73033078
  playDeduped=4404006 playVerifiedExact=4403604 playVerifiedApprox=0
  playVerifiedApproxEqual=0 playVerifiedUnresolved=402
  choiceByClassRequests=2046332 choiceVerifiedExact=500305
  choiceVerifiedApprox=1992813 choiceVerifiedApproxEqual=175350
  choiceVerifiedUnresolved=6221 wall=132.7s jobs=6
```

完全等价的断言（出牌 4,403,604 次、选牌 500,305 次）全部成立；近似选牌比较 1,992,813 次，其中实际完全相同 175,350 次（8.80%），未决 6,221 次。`playVerifiedApprox=0` 是因为正式代码只保留完全等价的出牌去重，近似出牌去重已撤。

### 4.5 计时

本机 24 逻辑处理器，M3b 以 `--jobs 6` 测量：

| 运行 | 墙钟 | 备注 |
| --- | ---: | --- |
| Release 重建 | 约 5.8s | 0 警告 |
| D1 | 10.6–11.2s | 410 文件 / 482,227 状态 / 474,832 特征 |
| D2（副本，严格 verify，全量一次运行） | 122.3s | 1,983/1,983，展开 15,568,469 |
| D2（副本，等价开关，全量一次运行） | 132.7s | 1,983/1,983，prefix/threat 与等价检查 |
| 副本 refreeze | 127.4s | 复制 + 重解 + 清单 |
| manifest | 小于 1s | 5,589 文件 / 23,692,497 B |

D2 四片 `compare --config all`（依次运行，`--jobs 6`）：

| 分片 | 请求 | equal/质量 | 墙钟 |
| ---: | ---: | --- | ---: |
| 0/4 | 494 | 11 更好 / 482 相同 / 1 更差 | 38.5s |
| 1/4 | 495 | 6 / 483 / 6 | 38.7s |
| 2/4 | 496 | 4 / 488 / 4 | 39.2s |
| 3/4 | 498 | 8 / 488 / 2 | 38.4s |
| 合计 | 1,983 | 29 / 1,941 / 13 | 154.8s |

慢请求 `40e330…` 从冻结基线的 177.1s 降到 23.0s（−87.0%），展开 283,539→132,270（−53.3%），转移 12,430,403→1,573,784（−87.3%），路线逐动作相同；单进程 `--jobs 1` 实测 17.2s。M3a 的计时（前缀共享）不再与 M3b 直接相加：本批的收益来自选择分支数，而 D1 与内核路径未改。

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
`--emit-live-requests` 还接受 `--search-tier Low|Medium|High|ExtraHigh`（默认 `Medium`）、
`--search-normal`（关掉确定性软时间）与
`--search-cancel-after-ms N`（取消测试）。

### 6.5 实机搜索请求/结果对照

测试模式在捕获后默认用中档确定性选项求解一次（软时间关闭，起始宽度 256、第一轮节点上限
512,000），写
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
并关闭确定性。测试模式捕获根后立刻进入 `AutoCombatSession`，由共享的 `CombatPilot`
每回合搜索/沿用路线、逐动作核对、回合对账，直到胜利、失败、暂停或取消。结果写 `full-combat.json`，并在 `status.json`
增加 `fullCombatStatus/outcome/turns/playerHp/playerMaxHp/searches/reusedTurns/deviations/
deploymentFailures`。整场搜索的每一轮使用该次决策剩余的正常软时间（见 6.7 的多轮规则）；`src/Mod` 还会把
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

### 6.7 离线整场自动战斗

`--offline-combat` 用与 Mod 完全相同的整场决策循环（`src/Core/Autoplay/CombatPilot.cs`）
在 Blitz 内核里打完一个根，不启动 Godot，也不模拟原生动画。默认跑质量语料的 120 根：

```bash
# 默认中档、确定性搜索；每个根一个子进程，--jobs 控制同时运行的重进程数
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --offline-combat --corpora tests/corpora --jobs 4 \
  --out .local/offline-combat/default

# 指定根列表（每行一个根名，# 注释）；根名必须是 d1 根
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --offline-combat --roots-file .local/roots120.txt --jobs 4 \
  --out .local/offline-combat/default

# 只跑一个根，便于定位（同进程，不再起子进程）
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --offline-combat --root quality-DEFECT-000 --out .local/offline-combat/one

# 四档评估：每档 120 根，--jobs 4。低/中/高各约 3/5/7 分钟；
# ExtraHigh 按 60 根拆两次，单条命令保持在 8 分钟内
for tier in Low Medium High; do
  dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
    --offline-combat --roots-file .local/roots120.txt --jobs 4 \
    --search-tier "$tier" --out ".local/offline-combat/$tier"
done
for part in a b; do
  dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
    --offline-combat --roots-file ".local/roots-xhigh-$part.txt" --jobs 4 \
    --search-tier ExtraHigh --out ".local/offline-combat/xhigh-$part"
done

# 单宽度探针：起始=最多，便于只测某一宽度；每轮节点上限仍可覆盖
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --offline-combat --corpora tests/corpora --jobs 4 \
  --beam-width 512 --max-width 512 --out .local/offline-combat/w512

# 同宽度、旧局面池上限（200 MiB），用于放大前后对照
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --offline-combat --corpora tests/corpora --jobs 4 \
  --beam-width 512 --max-width 512 --pool-state-budget-mb 200 \
  --out .local/offline-combat/w512-pool200

# 极小预算强制部分路线，走一遍重搜路径（不是等价用例）
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --offline-combat --root quality-REGENT-013 --memory-budget-mb 64 \
  --out .local/offline-combat/reroute
```

开局根与无头整场的实际搜索根一致：`ScenarioBuilder` 建出原生 `CombatState`，
`RulesCompiler.Compile` 冻结规则，`LiveCombatTracker.ImportRoot` 导入开局 `Battle`
（`RootCards`、`Reconciliation` 等选项与 Mod 首根相同）。注意 `--emit-live-requests` 后
`TestSession` 写出的 `BATL` 捕获根用的是普通 `ImportOptions.Default`，不是整场搜索根；在
120 个质量根上 96 根逐字节相同，24 个 DEFECT 根只差 `Counter.OrbChannels`（捕获根 0，
整场根按原生 History 重建为 1 或 2）。离线工具使用整场根，因此与实机整场搜索结果对齐。
搜索请求用同一个 `SearchRequestCompiler` 组装第一轮，性能档默认 `Medium`（起始宽度
256、最多 512、每轮节点上限 宽度 × 2,000、决策总时间 120 秒）；离线模式把每轮软时间设为
`TimeSpan.MaxValue`，只受节点上限和全局内存预算约束。多轮翻倍、每轮节点上限、终局比较、
局面池放大与时间预估都在 Core 的 `WideningSearch` 里，Mod 与离线整场共用该入口。

离线不按时间截断或跳过任何一轮；它用游戏内同一套“上一轮耗时的 2 倍”预估，把实机会跳过
的轮次标成 `wouldSkip`、会截断的标成 `wouldTruncate`，决策级 `softTimeRisk` 为任一标记。
`rounds[].remainingBeforeMilliseconds`/`estimatedMilliseconds` 是当时的预估输入，来自挂钟，
因此两次运行的这几个字段可能有小差异；`expandedNodes`、`routeActions`、胜负等确定性字段
不变。报告不能把 `wouldSkip`/`wouldTruncate` 的轮次当成实机一定会执行。

每根输出 `<out>/<root>/full-combat.json`，字段与无头整场的 `full-combat.json` 对齐：
`outcome`、`turns`、`playerHp`、`playerMaxHp`、`potionsUsed`、`searches`、
`reusedTurns`、`deviations`、`deploymentFailures`、`elapsedMs`、`pauseReason`、
`searchLog`、`deviationLog`、`failureLog`。`searchLog` 的每项包含
`turn`/`reason`/`status`/`elapsedMs`/`expandedNodes`/`routeActions`/`victory`/`error`，另加
`managedHeapPeakBytes`/`workingSetPeakBytes`、`bestRound`、`damageFreeVictory`、
`roundsRun`、`roundsSkippedByTime`、`softTimeRisk` 和 `rounds`。`rounds` 的每项记录
`width`/`maxExpandedNodes`/`poolStateBudgetBytes`/`elapsedMs`/`expandedNodes`/`routeActions`/
`victory`/`damageFreeVictory`/`wouldSkip`/`wouldTruncate`/`poolCapacityDropped`/
`searchMemoryPeakBytes` 等。父进程还写 `<out>/summary.json`（档位、起始/最多宽度、每宽节点
系数、总时间、逐根摘要与墙钟）。逐根摘要的 `exitCode` 是子进程退出码，`exceptionType` 是
从 stderr 的 `Unhandled exception.` 行解析出的异常类型；子进程没有写出 `full-combat.json`
时 `status=crashed`、`error` 为异常类型与消息（解析不到时保留退出码与缺失文件的说明），
不会再把 `[scenario] ...` 的最后一个阶段当成错误原因。

路线用完而战斗未结束时，离线工具从预测的最后一个 `Battle` 直接重搜；Mod 在同样的边界用
`LiveCombatTracker.ImportRoot` 从实机重新导入。两者逐字段的关系是：

- 原生可观测字段（HP、护甲、牌堆、卡值、9 条 RNG、Powers、Relics、Orbs、药水、回合/侧/
  阶段、结局）由同一 `BattleImporter` 投影；预测正确时离线根与实机根逐字节相同，逐动作
  `BattleReconciler` 已经在前一回合验证过这一点。
- `ShuffleEventCount`、`ShufflesCrossed`、`HasRisk` 与
  `LiveCombatTracker.CarriedCounters` 中的求解器账本在实机不可观测，`ImportRoot` 从最近预测
  边界携带；离线根本来就带着这些值，因此相同。
- `RevivingEnemies` 由预测边界中 `DeathPhase.Reviving` 的行决定；离线根的行相位已经是预测
  值，相同。
- 终局 `turns` 与 `playerHp` 按原生收尾投影：若最后一个动作到达敌方侧回合结束阶段
  （Doom 一类击杀），原生 `TurnNumber` 已前进一格，离线结果也加一；胜利后原生
  `AfterCombatVictory` 会结算 Burning Blood/Black Blood/Meat on the Bone，离线用同一个
  `PostCombatRelicHealProfile` 把等价治疗加到 `playerHp`。这两项都是结果投影，不回写搜索
  根或后续搜索输入。
- 实机比较视图会归一化永久死亡敌人的 AI 节点/日志、其名下 Power 行、detached 卡的一次性
  `PowerAfflictionChecked`、`NIGHTMARE_POWER.Target` 与不衰减 Power 的
  `SkipNextTickFromMonster`。这些字段在实机导入根与预测根之间可能不同，但都不参与后续
  搜索行为；离线重搜保留预测值。若后续在这些字段上加入搜索语义，需要先把它们从比较投影
  移回严格比较。

离线搜索的评分/下界配置仍从开局原生状态捕获（`SearchPolicyCapture` 与
`EvaluationConfigCompiler` 的冻结表），因为离线进程没有后续回合的原生模型；这是离线重搜与实机
重搜唯一未逐字节对齐的输入。本批四档 120 根默认仍只需要开局搜索，因此不影响对应关系；
出现路线用尽后的重搜时，需要同时验证这部分的请求字节。

整场等价的口径是逐根比较 `outcome`、`turns`、`playerHp`、`searches`，以及每次搜索的
`expandedNodes` 与 `routeActions`；不设容差。汇总表用仓库入口
[`tools/testing/offline-combat-table.py`](../tools/testing/offline-combat-table.py) 生成，格式
与协调者的 q120 表一致：

```bash
tools/testing/offline-combat-table.py \
  --results .local/offline-combat/Medium \
  --baseline ~/sts2/BlitzSolver/.local/q120/baseline.tsv
```

它按角色、幕与房间类型对照 CombatSolver 基线，并列出胜负不同的根与异常（非胜负结果、偏差、
部署失败）。四档把 `--results` 换成对应的 `--out` 目录即可复用同一张表。

### 6.8 离线搜索内存与计时

`--offline-combat` 的每次决策和每一轮都写内存诊断：`searchMemoryUsedBytes`、
`searchMemoryPeakBytes`、`routeNodeStorageBytes`、`routeNodePeakCount`、
`memoryBudgetExceeded`、`memoryExceededAtBytes` 与 `poolCapacityDropped`。这些是搜索账本
的逻辑字节，不读 GC；`managedHeapPeakBytes` 才是进程托管堆采样，只用于报告。多轮搜索共用
一份 `SearchStorage`，轮次之间保留的块也计入预算；默认局面池从 200 MiB 随宽度放大到
256/512/1,024 MiB（宽度 256/512/1,024），宽度 2,048 的单池上限为 1,728 MiB；乘积先按
64 位计算再截断到 `int`，放大后的实际池块逐块记账。
`--pool-state-budget-mb N` 可把每轮池上限固定为 N MiB，用于放大前后对照，不进入 `SREQ`。

离线宿主的默认预算固定按 0.15 × 16 GiB = 2,576,980,377 B；`--memory-budget-mb N` 用 MiB
覆盖。预算触发后搜索停止展开并返回当时最好的路线（没有候选时取已展开状态的排序领先者），
因此整场仍能打完。两个小预算回归例子：

```bash
# 3 个根 × 64/128/256 MiB；同一预算跑两次，逐根比较确定性字段
for mb in 64 128 256; do
  dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
    --offline-combat --root quality-REGENT-013 --memory-budget-mb $mb \
    --out .local/budget-test/b$mb
done
```

离线子进程默认先做一次 50,000 节点的热身求解（同一根、同宽度、不计入 `elapsedMs`），
让分层 JIT 在测得的搜索前达到游戏进程的热状态。环境变量
`BLITZ_OFFLINE_WARMUP_NODES` 可改节点数，0 关闭；测内存对照时应设为 0，避免热身堆残留
抬高 `managedHeapPeakBytes`。计时口径：同一个 120 根、默认 Medium、`--jobs 6`；对照
`~/sts2/BlitzSolver/.local/q120c` 的 3 lane 无头游戏结果，只取旧离线 `w240` 中
`elapsedMs >= 500` 的 117 次搜索，逐次算 `gameMs / offlineMs`，取中位数。

同一请求复用存储可单独验证：

```bash
BLITZ_STORAGE_CHECK=1 dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --offline-combat --root quality-REGENT-013 --out .local/storage-check
# STORAGE_CHECK same=True firstBytes=... secondBytes=... secondDelta=...
#   regionsFirst=[chunk:...,array:...] regionsSecond=[chunk:...,array:...]
```

`chunk`/`array` 两类在第二次求解不再增长，说明大块已从 `SearchStorage` 复用；第二次仍会
为池选择结构、路由签名字典与标签列表分配小块。`--emit-live-requests` 与
`run-headless.sh --cleanup-instance-on-exit` 的 28 根核对见第 7 节，不随本批改变。

### 6.9 负例

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

### 6.10 整局状态导入对照（R1b）

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --run-import-compare --run-kind all --run-seeds 20 --run-jobs 8
```

父进程按 8 并发起子进程：每个自然开局一个进程（5 角色 × A0/A10 × 20 种子 = 200），每个
D1 输入一个进程（410，含 120 个质量根）。子进程：

1. 自然开局用 `RunTestHost.StartNewRunAsync` 在 `EnterAct(0)` 后、玩家第一次选择前取原生
   `RunState`/`Player`；D1 用 `ScenarioBuilder.BuildAsync(skipInitialInjection: true)` 取
   战斗开始的整局状态。
2. `RunStateImporter.Import` 后，与独立实现的 `NativeRunDump`（`src/TestHost/Run/`，不调用
   导入层）逐字段比较；差异打印字段路径与两边值。私有属性由原生侧直接从 `SavedProperties`
   打印 `名字哈希:类型:值`，Blitz 侧从属性包解码出同一文本，属性编码器不参与对照。
3. 15 条流各从原版与 Blitz 抽 64 次（`NextInt(1000)`、`NextFloat`、`NextDouble`、
   `NextInt(-5,100)`、`NextBool` 轮换），并检查计数器；另抽当前事件流与 4 条 `act_N_map`。
4. 比较当前幕地图的节点、连线、类型、起始/Boss/第二 Boss、已走坐标。
5. D1 还导入同一战斗的 `Battle`，检查 9 条战斗流、HP/最大 HP、持久牌组身份、遗物
   顺序/id/Melted、药水槽。

只跑其中一类：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --run-import-compare --run-kind natural --run-seeds 20 --run-jobs 8
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --run-import-compare --run-kind d1 --run-jobs 8
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --run-import-compare --run-d1-root kg2-b-stomp
```

局外导入负例（多人、非标准模式、Modifier、未知事件、未知解锁、无地图、牌组容量）：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll --run-import-negative
```

2026-10 结果：自然开局 `same=200 diff=0 reject=0`；D1 `same=410 diff=0 reject=0`；战斗一致性
`same=410`；RNG `same=610`（每类都 15 条 × 64 次 + 地图/事件）；地图 `same=610`；负例
`failed=0`。逐字段比较包含地图、预生成序列、遗物袋顺序、解锁位图、RNG 五元组与模型私有
属性包。

## 7. 每层证明什么

| 层 | 证明 | 不证明 |
| --- | --- | --- |
| 构建 | 源码可编译、无警告、只有 BCL 依赖 | 运行时正确性 |
| K0 | 布局、RNG、编码、容量与注册表合同 | 搜索与冻结输入 |
| D1 | 410 个冻结输入的逐步重放与特征哈希 | 未捕获状态、实机导入 |
| D2 | 1,983 个冻结请求的完整搜索结果 | 未捕获请求、实机部署 |
| manifest | 快照完整性与来源声明 | 语义正确性 |
| 离线宿主（手动） | 410 个原生 `CombatState` 与参考转储逐字节相同；410 个输入（418 个根）导入 `Battle` 与 GOLD 逐字节相同，编译 `Rules` 与冻结规则对象逐字节相同，编译 `ScoreRules`/`Bound` 与 `config.br` 相同且特征哈希一致；R1b 的 200 个自然开局与 410 个战斗根的 `RunState` 逐字段相同，15 条流 × 64 抽取与地图相同 | 实机部署、任意 Mod、语料外前缀/准备阶段、奖励/商店/事件负载 |
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
