# 原版战斗差分宿主

在普通 .NET 进程中初始化原版战斗，核对导入、规则、搜索配置与动作结果。需要游戏文件；不包含局外地图、奖励或商店执行。

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

默认质量集合由 `tests/corpora/quality/default-roots.txt` 指定，共108场（105个生成牌组、2场女王、1场沙漏）。用 `--list-quality-roots` 可以只列出名单。

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --offline-combat --corpora tests/corpora --jobs 4 \
  --out .local/offline-combat/default
dotnet artifacts/bin/Differential/release/Differential.dll --offline-combat --root quality-DEFECT-000 \
  --search-tier Medium --out .local/offline-combat/one
```

不启动 Godot，用与 Mod 相同的 `CombatPilot` 在内核里打完一个根。每个根写
`<out>/<root>/full-combat.json`，父进程写 `<out>/summary.json`；`--search-tier` 默认
`Medium`，`--memory-budget-mb` 覆盖搜索内存预算。

离线实验可选 `--potion-schedule one-allowed|one-two-three|one-two-allowed`；不指定时沿用默认计划。各组的结果、原请求约束与测量记录写入 `--out` 指定的本地目录，不提交实验报告。

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

## 原版逐动作与终局对照

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --combat-pilot-compare --pilot-root quality-IRONCLAD-001
```

使用原版创建战斗，低档组合搜索后通过原版动作执行，核对每个稳定边界与终局合同。与 `--offline-combat` 的内核回放评测不同；细节见 [测试方法](../../docs/testing.md)。
