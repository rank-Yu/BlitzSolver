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

- `tests/KernelChecks`：`"contracts": "Passed"`。覆盖 `BlitzBattle`/`BlitzCard`/`BlitzEnemy`/`BlitzPower`/`BlitzRngState` 等布局尺寸、RNG 参考向量、牌堆与容量失败、选择协议、内容注册表与编码合同。
- `tests/Golden k0`：`K0 Passed fields=30 battleBytes=12696 formatVersion=2 magics=BATL,RULE,GOLD,SREQ,SRES`。检查 `BlitzRulesCodecContract` 的 30 个字段都进入编码、`decode(encode(x))` 字节稳定、`Unsafe.SizeOf<BlitzBattle>() == 12696`，`BATL`/`RULE`/`GOLD`/`SREQ`/`SRES` 五个魔数按小端字节解出的 ASCII 与格式标签一致，以及 `BATL` 的旧魔数/version 1 被显式拒绝。

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

`--jobs N` 控制并行度，默认处理器数；`--shard i/n` 按排好序的文件做 `index % n == i` 分片。D1 按根目录分片，D2 按请求文件分片。

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
3. 运行 `BlitzBeamSolver.Solve`；
4. 与 `d2/expected/*.result.br` 逐字段比较路线、TopRoutes、分数 double 位、展开/转移/去重、容量丢弃、终态压缩、药水审计、侦察层、BestRootIndex、PotionPolicySatisfied 与 Takeover。

D2 证明：冻结的 1,983 个请求在去软时间预算后可复现完整搜索结果。
D2 不证明：请求集合之外的根、实机运行、界面与部署行为。

完整 `verify` 的最后一行是 `GOLDEN_VERIFY ... mismatched=0`。解码失败或字段不一致输出 `GOLDEN_INVALID <原因>`，非预期异常输出 `GOLDEN_FAILED <类型>: <消息>`，两者都以非零退出。

### 4.3 计时

本机 24 逻辑处理器，`--jobs 8`：

| 运行 | 墙钟 | 备注 |
| --- | ---: | --- |
| Release 重建 | 约 3.8s | 0 警告 |
| D1 | 12.0s | 410 文件 / 482,227 状态 / 474,832 特征 |
| D2 | 283.9s | 1,983/1,983，展开 15,739,416 |
| D1 + D2 | 296.7s | 不含构建 |
| manifest | 小于 1s | 5,592 文件 / 24,338,737 B |

M1b 的 D2 为 331.3s；本轮 v2 迁移后的 D2 为 283.9s。差异在测量波动范围内。

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

## 6. 每层证明什么

| 层 | 证明 | 不证明 |
| --- | --- | --- |
| 构建 | 源码可编译、无警告、只有 BCL 依赖 | 运行时正确性 |
| K0 | 布局、RNG、编码、容量与注册表合同 | 搜索与冻结输入 |
| D1 | 410 个冻结输入的逐步重放与特征哈希 | 未捕获状态、实机导入 |
| D2 | 1,983 个冻结请求的完整搜索结果 | 未捕获请求、实机部署 |
| manifest | 快照完整性与来源声明 | 语义正确性 |

这四层合起来证明：当前源码对 `tests/Golden/data` 的冻结证据是可复现的。它们不证明：BlitzSolver 能在真实游戏中导入状态、在真实战斗中部署、在界面上显示，或支持任意第三方 Mod。

## 7. 未验证项

- **实机运行**：尚未在游戏内启动 BlitzSolver。
- **实机状态导入**：尚未从游戏运行时把 `CombatState`/`RunState` 投影成 `BlitzBattle`。
- **部署**：尚未把搜索结果翻译回游戏动作并执行。
- **界面**：没有设置页、进度、路线预览或失败提示。
- **第三方 Mod**：没有实机 Mod 根；当前只验证 BCL 内的固定内容与冻结请求。
- **Windows/Steam**：当前计时与验证在 Linux；Windows 未单独验证。

这些项目在接入层完成前不应被文档或提交信息描述为已验证。冻结快照的更新条件见 [冻结快照](golden-data.md) 第 11 节。
