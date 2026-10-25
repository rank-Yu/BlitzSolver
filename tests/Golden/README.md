# Golden：BlitzSolver 冻结快照校验器

`Golden` 通过 `ProjectReference` 引用 `src/Core/BlitzSolver.Core.csproj`，在只有 BCL 的进程里
校验冻结快照 `tests/Golden/data`。它不引用游戏 DLL、Godot、Harmony 或 RitsuLib。

```bash
dotnet run --project tests/Golden -c Release -- k0
dotnet run --project tests/Golden -c Release -- verify tests/Golden/data
dotnet run --project tests/Golden -c Release -- manifest tests/Golden/data
dotnet run --project tests/Golden -c Release -- verify tests/Golden/data --only d1 --jobs 8
dotnet run --project tests/Golden -c Release -- verify tests/Golden/data --only d2 --jobs 8 --shard 0/4
```

- `k0`：运行 `RulesCodecContract`，检查 `Rules` 每个构造字段都进入编码字节、五个魔数的 ASCII 标签、`formatVersion=2` 与 `BATL` 旧魔数/version 1 的显式拒绝，并打印
  字段数与 `Unsafe.SizeOf<Battle>()`。
- `verify`：先核对 `manifest.json`（格式版本、`Battle` 大小、`provenance`（含 `formatMigration`）、每个文件的
  字节数与 SHA-256、`totalBytes`，以及 D1/D2 计数），再分别校验 D1 与 D2。
- `manifest`：确定性地重写 `manifest.json`。文件按路径排序，逐文件记录大小与 SHA-256，
  沿用 D1/D2 计数字段，并认识 `d1/<root>/config.br`；未知文件、孤儿规则对象或缺失的
  D1 根文件都会显式失败。
- 成功时最后一行是 `GOLDEN_VERIFY ... mismatched=0`；解码或一致性失败输出 `GOLDEN_INVALID <原因>`，非预期异常输出 `GOLDEN_FAILED <类型>: <消息>`，并以非零退出。
- `--jobs N`：并行度，默认处理器数。
- `--only d1|d2`：只跑一侧。
- `--shard i/n`：D1 按排好序的根目录做 `index % n == i`；D2 以期望结果的 `ExpandedNodes` 为权重，按权重降序分给当前最轻的分片，并在每片内从重到轻执行。未指定时跑全部请求。

## D1

D1 重放 `golden/d1/*` 与 `objects/rules/*`：逐步比较状态哈希、合法动作与枚举顺序、立即选择请求、
完整选择序列、子状态哈希与结束标志；并逐字段比较每个已记录状态的 `scoreHash`、
`boundStatus/boundHash`、`traitPresent/traitHash`。哈希函数按 M0 宿主的字段顺序显式移植
（`GoldenHash.cs`），不依赖反射顺序。不一致时以非零退出并打印第一处差异。

GOLD 格式只冻结 `Rules`，没有冻结 `ScoreRules` 与 `BoundFeatures.Config`；这些
配置保存在同一个根目录的 `d1/<root>/config.br`。每个文件是一个 `SREQ` 请求的 Brotli 压缩，
包含该根运行时的完整 `Rules`、`ScoreRules`、`BoundFeatures.Config` 以及固定
前缀的 `InitialActionCount`。校验器只从配置取 `Scorer`/`Bound`，随后完全由 `Battle` +
Core 重算；配置缺失时显式失败。`config.br` 已纳入 `manifest.json` 的完整性与计数。

## D2

D2 读取 `d2/requests/*.request.br`（`SREQ`），把软时间预算改成 `TimeSpan.MaxValue` 后重新求解，再与
`d2/expected/*.result.br`（`SRES`）逐字段比较路线、TopRoutes、分数（double 位模式）、展开/转移/去重计数、
容量丢弃、终态压缩、药水审计、侦察层与接管动作。1,983 个请求全部相等。

数据格式、计数和 Search 外部类型清单见 `.local/tool-tasks/blitz/M0-report.md`。
