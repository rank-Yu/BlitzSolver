# 离线宿主

本工程在普通 .NET 9 控制台进程里初始化《杀戮尖塔 2》的游戏静态状态，用游戏自己的
`RunManager`、`ModelDb` 与 `CombatState` 建出一场战斗，推进到玩家可操作，再把原生战斗状态
写成确定性转储。它不启动 Godot 可执行文件，也不需要场景树。

用途是 M2a 的离线验证：先证明不启动游戏也能建出与冻结快照导出时相同的原生状态，再用
`src/Import` 把状态导入 `Battle` 并与 GOLD 逐字节对照。本工程不在 `BlitzSolver.sln` 中；
CI 没有游戏文件，不会构建或运行它。

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
`reject`（打印稳定原因码）。`--all` 只跑 397 个在范围根，跳过 13 个前缀 fixture；额外 8 个
回合准备根与 3 个普通根的处理见 [`docs/import.md`](../../docs/import.md)。

`--compiled-rules` 改用 `RulesCompiler.Compile` 的输出来导入：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --all --jobs 8 --import-compare --compiled-rules
```

## 规则编译对照

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --root kg2-b-stomp --rules-compare
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll   --all --jobs 8 --rules-compare
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll --rules-table-check
```

`--rules-compare` 对每个根编译 `Rules`、用 `RulesCodec.Encode` 编码，再与该根冻结规则对象
逐字节比较；`diff` 打印 `RulesDiff` 的第一处表/项。`--rules-table-check` 校验
`src/Import/MirrorRiskTables.cs` 与 `ModelDb.All`/Blitz Registry 的 id 集合完全一致。

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

3 个回合准备 fixture 的 `config.br` 含全部准备根，本批只导入普通根，因此只比较
`ScoreRules`/`Bound` 子结构；完整请求与额外准备根留给 M2a-5。当前 397 个在范围根
`config same=397 diff=0 reject=0`。

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
`checkpointArchivePath`/`replayPolicyOverridePath` 以及本批不支持的搜索/部署值会显式失败。
`flashPrefixActions`/`flashPrefixPlan` 被识别但不执行；额外回合准备根不在本批范围。

## 转储

转储是行式规范文本，格式由 `NativeStateDump.cs` 定义：头部、玩家/Osty/敌人属性、
原生顺序的牌堆与 Power、遗物可见计数、药水槽、球、9 条 RNG 与 `NextCombatId`。
它只读原生状态：不执行动作、不推进回合、不跑搜索。
