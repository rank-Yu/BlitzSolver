# Golden 冻结快照

[返回文档索引](README.md)

`tests/Golden/data` 是 BlitzSolver 的冻结证据。它由 CombatSolver 时期的 M0 宿主在固定输入上导出，当前仓库只读取、校验和重放，不依赖游戏程序集。本文说明目录、格式、来源与更新条件；如何复跑见 [测试与验证](testing.md)。

数据里的魔数、JSON 键（例如 `flashBattleSize`）和根名（例如 `flash-dx5-pen-nib-sly`）保持捕获时拼写；类型名在文中按当前代码写作 `BlitzBattle`、`BlitzRules`、`BlitzScoreRules` 等。

## 1. 目录结构

```text
tests/Golden/data/
  manifest.json
  objects/rules/<sha256>.br          # FRUL 规则对象，按内容 SHA-256 去重
  d1/<root>/config.br               # 该根捕获时的 FRS1 配置 sidecar
  d1/<root>/golden.br               # FGD1：根/K1/K2
  d1/<root>/meta.json               # 输入映射、计数、格式版本、BlitzBattle 大小
  d2/requests/<sha256>.request.br   # FRS1 请求，按完整请求字节去重
  d2/expected/<sha256>.result.br    # FRR1 期望结果
  d2/raw/<root>/<sha>[.pid.seq].capture.br
  d2/replay-report-<shard>-of-<n>.json
  d2/rules-index-<shard>-of-<n>.json
  d2/replay-<shard>.log
  unsupported.log
```

当前规模：

| 项 | 数量 |
| --- | ---: |
| 文件总数 | 5,596 |
| 总字节 | 24,345,755 |
| D1 文件/根目录 | 410（每个目录一份 `config.br`/`golden.br`/`meta.json`） |
| D1 记录根（`counts.d1.roots`） | 418（与 410 个文件目录不是同一计数） |
| D1 K1 动作 | 5,062 |
| D1 游走 / 状态 / 转移 | 15,296 / 482,227 / 469,352 |
| D1 特征 / trait | 474,832 / 474,414 |
| D1 规则对象 | 388 |
| D2 请求 / 期望 | 1,983 / 1,983 |

## 2. 压缩与哈希

- 所有 `.br` 文件用 `System.IO.Compression.BrotliStream` + `CompressionLevel.SmallestSize` 压缩。
- 文件内容不含时间戳；同数据两次编码字节相同。
- `manifest.json` 的 `sha256` 是对压缩后文件字节计的；校验器逐文件比较大小与 SHA-256。
- 状态哈希是整块 `BlitzBattle` 字节的 SHA-256 截断到 64 位（`BlitzCodec.BattleHash`）。
- `FRS1` 请求按完整请求字节的 SHA-256 去重；`FRUL` 按完整规则字节的 SHA-256 去重。
- 搜索指纹只用于分桶；去重在指纹命中后仍比较完整字节，不允许仅凭哈希合并。

## 3. `FBAT`：BlitzBattle

```text
u32 magic="FBAT" | u16 version=1 | i32 Unsafe.SizeOf<BlitzBattle>() | raw bytes
```

解码要求 magic、version、声明大小与当前 `Unsafe.SizeOf<BlitzBattle>()`（12,696）一致，且数据长度正好等于 `HeaderSize + size`；否则抛 `InvalidDataException`。解码后校验各计数上限（卡牌、敌人、Power、遗物、球、药水槽、decimal 变量）、卡牌 Id 与敌人规则下标。

`FBAT` 是整块状态的容器；字段布局、容量与规范化见 [状态布局](state-layout.md)。

## 4. `FRUL`：BlitzRules

```text
u32 magic="FRUL" | u16 version=1 | i32 fieldCount=30
cards[id][upgrade] | powerVars[id] | potions[id] | relicVars[id] | monsters[id]
spawnRules（按 ushort id 排序） | cardPools（按 key ordinal 排序）
potionPool | outOfCombatPotionPool | rootPotions | powerDeathHistoryRisk
hasEncounter | encounterSlots | rootEnemyCount | baseMaxEnergy | playerSlotIndex
baseOrbSlotCount | rootSwordSageAmount | unsettlingLampRelicId
hasIdentityContent | hasShuffleOrderEnchantment | hasBeforeFlushEnchantment
hasTheftContent | hasDecimalCardVars | hasInfiniteHpDisplayMonster | hasPaelsLegion
furCoatSpawnHpOne | rootLostHpMask | hasHistoryCourse | penNibRelicIndex
```

- 卡牌数组按内部 Id 保存每级 `BlitzCardStats`；Power/遗物变量按内部 Id 保存 `decimal` 数组；药水/怪物以存在位 + 完整值保存。
- 生成规则与卡池按键排序；`fieldCount` 必须同时等于 `BlitzRules.SchemaFieldCount` 与 `BlitzRulesCodec.EncodedFieldCount`。
- `decimal` 写 4 个 `int`（`decimal.GetBits`），保留 scale 与精确值；字符串用 UTF-8 字节长度前缀。
- K0 对 30 个字段逐项变异，任何一个字段没写进字节都会失败；`decode→encode` 必须字节相同。

## 5. `FGD1`：D1 文件

```text
u32 magic="FGD1" | u16 version=1
string inputJson | string rulesSha256
rootCount | 每个根：i32 FBAT 长度 + FBAT 字节 + string choices
          + u64 scoreHash + u8 boundStatus + u64 boundHash
k1Count | 每个 K1：i32 rootIndex + 动作（无选择基动作）+ 选择数组
        + 选择请求数组 + u64 childHash + bool ended
        + u64 scoreHash + u8 boundStatus + u64 boundHash
        + bool traitPresent + u64 traitHash
walkCount | 每条游走：i32 rootIndex + i32 seed + stateCount
          每个状态：u64 stateHash + 合法动作数组 + 每个动作的 immediateStatus
                   + 并行 immediateRequests + bool hasStep
                 若有 step：基动作 + 选择数组 + stepRequests + u64 childHash
                            + bool ended + scoreHash/boundStatus/boundHash
                            + traitPresent/traitHash
```

- 动作编码：`Kind, HandIndex, Target, PotionSlot, ChoiceCount, ChoiceCount × u64 选择位集`（不写结构体 padding）。
- 选择请求编码：`Kind, Effect, Timing, Source, SourceIsNone, OptionsAreGenerated, OptionCount, MinCount, MaxCount, OptionCount × 选项字节`；生成卡选项再写 `OptionCount × BlitzCard` 原始 92 字节。
- `immediateStatus`：0 = 动作不需要选择；1 = 需要且并行请求有效；2 = 内核边界（`BlitzUnsupportedException`/`BlitzCapacityException`）。
- K1 对每个合法动作记录完整补全的请求序列；K2 每个状态记录枚举顺序的合法动作、第一次选择请求与随机采用动作的完整请求序列。
- K4 `scoreHash` 是 `BlitzScoreFeatures` 全部字段的确定性 SHA-256 截断；`boundHash` 是 `BlitzBoundValues`；`traitHash` 是 `SearchRouteTraits + pure + declined + pocketwatch threshold/played`。D1 校验器只验证内核可复现部分（K1/K2）。
- `meta.json` 记录 `formatVersion`、`battleSize`、`root`、`input`、`rulesSha256` 与 roots/K1/walks/walkStates/transitions 计数。

## 6. `FRS1` / `FRR1`：D2 请求与结果

### 6.1 `FRS1`

```text
magic | version | rules 长度 + FRUL | battles 数量 + 每个 FBAT | options
```

`options` 按固定顺序写全部确定值字段：

- 主构造：BeamWidth、MaxExpandedNodes、MaxTurns、SoftTimeBudget.Ticks、InitialPlayerMaxHp；
- CandidateScanCount、BlitzPotionPolicy（Earliest/Maximum/Strategy/EffectivePolicy/ForceDisabled）；
- Scorer、Ordering、HpTargetStop、Bound、Incumbent、EffectivePotionPolicy、TheftPolicy；
- ForcedPotionDirectives、EnforceForcedPotionDirectives、MinimumPotionUses、InitialExplicitPotionUses、InitialAutomaticPotionUses；
- PotionFreeBaseline、PotionReplacementHpCredit、SmartPotionEligibilityHpCeiling、AmbergrisPotionId、InitialAmbergrisUses、AuditPotionPolicy；
- InitialActionCount、Act3BossStrategy、PlanCommitment、InitialPlanPayoffCount、EarlyTurnScoutDepth、Novelty；
- InitialPlayerTurn、InitialSearchTurn、SecondRankBand、SecondRankBandWidth、AggressivePowerCommitment、PreserveReplayAllocatorOpening、PowerCommitments。

`BlitzScoreRules` 完整写入：每张卡每级的 `BlitzCardScore`、每个 Power 的 `BlitzPowerScore`、药水战略代价、成长额度/目标、遗物计数器规则、战后治疗、Boss HP 减免、ContinuousThreatRanking、ContextualRanking（仅允许 null）、初始值、根计数、根药水、所有命名 Power/Card/Relic/Potion Id。

`BlitzBoundFeatures.Config` 完整写入所有开关、证书、数组与根药水。

宿主回调 `Pacer`、`Progress`、`PollTakeover`、`Trace` 只写 presence 位；解码后为 null，不影响路线选择。`ContextualRankingModel` 非 null 或 `NoveltySearchTelemetry` 非 null 时捕获显式抛 `NotSupportedException`，不会写半份数据。

### 6.2 `FRR1`

```text
BestRoute | TopRoutes | BestIsVictory | BestScore(double bits)
ExpandedNodes | Transitions | Duplicates | TurnsSearched | PrunedIncumbents
CapacityPruned | CapacityDroppedChildren（按 key 排序） | EndedCompactions
EndedCompressedStates | SmartPotionPruned | BestPayoffCount | ScoutLayers
BestRootIndex | PotionPolicySatisfied | Takeover
```

不写 `Elapsed`、`WorkerYieldCount`、`FrameRecoveryWaitCount/Duration`、`MaxObservedGcPause` 与 novelty 遥测时间；这些字段不参与路线选择。`BestScore` 用 `BitConverter.DoubleToInt64Bits` 精确保存。

`CapacityDroppedChildren` 的键保持捕获时拼写；其中 `FlashBeam.Pool` 是数据格式键，不是当前类型名，源代码中必须保留。

## 7. `d1/<root>/config.br`

FGD1 只冻结 `BlitzRules`（`objects/rules` 的 SHA-256），没有冻结 `BlitzScoreRules` 与 `BlitzBoundFeatures.Config`。因此每个 D1 根另存一个 `config.br`：内容是 `FRS1` 请求的 Brotli 压缩，包含该根运行时的完整 `BlitzRules`、`BlitzScoreRules`、`BlitzBoundFeatures.Config` 与固定前缀的 `InitialActionCount`。

校验时只从 `config.br` 读 `Scorer`/`Bound`，随后完全由 `BlitzBattle` + Core 重算；配置缺失时显式失败，不静默跳过。`config.br` 已纳入 manifest 的完整性和计数。

## 8. manifest 与 provenance

`manifest.json` 字段：

| 字段 | 内容 |
| --- | --- |
| `formatVersion` | 当前为 1 |
| `flashBattleSize` | 12,696，捕获时的 `Unsafe.SizeOf<BlitzBattle>()` |
| `provenance.d1` / `.d2` | M0 宿主捕获提交 `75217006903195bc94654680423bb9ca580ec48a` |
| `provenance.d1Config` | D1 配置导出提交 `66a43838d89e7a03b05c0f04d5e4941ff555baec` |
| `compression` | `brotli` |
| `counts.d1` | roots/K1/walks/walkStates/transitions/files |
| `counts.d2` | capturedCalls/uniqueRequests/expectedResults/comparable/equal/mismatch/timeAffected |
| `totalBytes` / `targetBytes` | 总字节与目标预算 |
| `files` | 按 `path` ordinal 严格递增的 `{path,bytes,sha256}` 列表 |

D1/D2 的语义参照 CombatSolver `a62f1850`，但快照本身由 M0 宿主在 `752170…` 捕获；两者不是同一个提交。D1 的 410 个 `config.br` 与 `golden.br` 来自同一次确定性运行，M1a 已逐一核对 SHA-256。

## 9. 导出时核对过什么

- D1 的 410 个 `golden.br` 与 410 个 `config.br` 来自同一套根输入；每个根的 `meta.json` 记录输入映射、规则哈希与计数。
- D1 重放：状态、合法动作、选择、K1/K2、`scoreHash`/`boundHash`/`traitHash` 与导出值一致；M1a 实测 `mismatched=0`。
- D2 全量：1,983 个请求在去掉软时间预算后重新求解，路线、分数、计数、容量丢弃、审计与接管逐字段等于 `expected`；M1a 与当前仓库都实测 `1983/1983`。
- manifest：5,596 个文件的大小与 SHA-256、`totalBytes`、D1/D2 计数与 `provenance` 全部自洽；`manifest tests/Golden/data` 重写后逐字节相同。
- 没有把时间、GC、帧压力或运行期路径写进确定性结果；这些字段只存在于旧宿主的诊断里。

## 10. 什么情况下允许更新快照

默认冻结。只有以下条件同时满足时才允许更新 `tests/Golden/data`：

1. 有意改变内核语义或搜索行为，而不是为了让测试通过；
2. 有实机或明确的游戏证据支持这次改变；
3. 经用户同意，并在同一提交中更新数据文件与 `manifest.json`；
4. 在提交信息中写明变更原因、影响范围与新旧计数。

不允许：

- 手改 `.br` 内的字节或 `manifest.json` 的哈希来绕过校验；
- 只更新 `expected` 而不更新请求/规则/配置；
- 把未验证的实机行为写成已验证；
- 为了适配当前代码而改数据格式键、魔数或根名。

更新后应运行完整 `Golden verify`，确认 D1 `mismatched=0`、D2 全量相等，并重写 manifest 后确认 `git diff` 只包含有意变更。
