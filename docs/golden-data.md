# Golden 冻结快照

[返回文档索引](README.md)

`tests/Golden/data` 是 Sts2Solver 的冻结证据。它由 CombatSolver 时期的 M0 宿主在固定输入上导出，当前仓库只读取、校验和重放，不依赖游戏程序集。本文说明目录、格式、来源与更新条件；如何复跑见 [测试与验证](testing.md)。

数据格式本身不带产品名：魔数、版本、JSON 键和根名从 v2 起都不含 `Flash` 或 `Sts2Solver`。类型名在文中按当前代码写作 `Battle`、`Rules`、`ScoreRules` 等。`meta.json` 的 `input` 仍保存 CombatSolver 时期的 fixture/语料路径（例如 `coverage/fixtures/cards/flash-dx5-pen-nib-sly.json`），那是出处，不是当前格式的一部分。

## 1. 目录结构

```text
tests/Golden/data/
  manifest.json
  objects/rules/<sha256>.br          # RULE 规则对象，按内容 SHA-256 去重
  d1/<root>/config.br               # 该根的 SREQ 配置 sidecar
  d1/<root>/golden.br               # GOLD：根/K1/K2
  d1/<root>/meta.json               # 输入映射、计数、格式版本、Battle 大小
  d2/requests/<sha256>.request.br   # SREQ 请求，按完整请求字节去重
  d2/expected/<sha256>.result.br    # SRES 期望结果
  d2/replay-report-<shard>-of-<n>.json
  d2/rules-index-<shard>-of-<n>.json
```

`d1/<root>` 的根名不带产品前缀：v1 的 290 个 `flash-<name>` 在 v2 中改为 `<name>`，其余 120 个根名原样保留，去前缀后没有冲突。`meta.json` 的 `root` 与目录名一致。`input` 里记录的 CombatSolver 路径保持原样。

当前规模：

| 项 | 数量 |
| --- | ---: |
| 文件总数（不含 `manifest.json`） | 5,589 |
| 总字节 | 23,684,198 |
| D1 文件/根目录 | 410（每个目录一份 `config.br`/`golden.br`/`meta.json`） |
| D1 记录根（`counts.d1.roots`） | 418（与 410 个文件目录不是同一计数） |
| D1 K1 动作 | 5,062 |
| D1 游走 / 状态 / 转移 | 15,296 / 482,209 / 469,334 |
| D1 特征 / trait | 474,814 / 474,396 |
| D1 规则对象 | 388 |
| D2 请求 / 期望 | 1,983 / 1,983 |

## 2. 压缩与哈希

- 所有 `.br` 文件用 `System.IO.Compression.BrotliStream` + `CompressionLevel.SmallestSize` 压缩。
- 文件内容不含时间戳；同数据两次编码字节相同。
- `manifest.json` 的 `sha256` 是对压缩后文件字节计的；校验器逐文件比较大小与 SHA-256。
- 状态哈希是打包前缀（`Fingerprint.Bytes`）的 SHA-256 截断到 64 位（`Codec.BattleHash`）。v3 迁移时，D1 的每个 `stateHash`/`childHash` 都先用旧定长镜像算法重算并比对存储值，确认一致后才改写为新的打包前缀哈希。
- `SREQ` 请求按完整请求字节的 SHA-256 去重；`RULE` 按完整规则字节的 SHA-256 去重。规则对象与 D2 请求的文件名是内容 SHA-256，期望结果沿用对应请求的哈希作为文件名；格式版本变化会改变这些名字。
- 搜索指纹只用于分桶；去重在指纹命中后仍比较打包前缀，不允许仅凭哈希合并。

## 3. `BATL`：Battle

```text
u32 magic="BATL" | u16 version=3 | i32 PackedSize | packed bytes
```

魔数常量为 `0x4C544142`；按小端字节解出的 ASCII 是 `BATL`。v3 只写打包前缀：头部（`Battle.HeaderSize = 3,860` 字节）加 `CardCount × 92` 张牌，满牌 27,320 字节。解码要求 magic、version、声明长度在 `HeaderSize..Unsafe.SizeOf<Battle>()` 内，数据长度正好等于 `HeaderSize + declared`，且解码后的计数推出的 `Battle.PackedSize` 与声明一致；否则抛 `InvalidDataException`。解码先把工作区清零再写入前缀，后缀保持零。v1 的 `FBAT`/version 1 与 v2 的全量镜像都会被显式拒绝。解码后校验各计数上限（卡牌、敌人、Power、遗物、球、药水槽、decimal 变量）、卡牌 Id 与敌人规则下标。

`BATL` 是打包状态的容器；字段布局、容量与规范化见 [状态布局](combat-state-layout.md)。

## 4. `RULE`：Rules

```text
u32 magic="RULE" | u16 version=3 | i32 fieldCount=30
cards[id][upgrade] | powerVars[id] | potions[id] | relicVars[id] | monsters[id]
spawnRules（按 ushort id 排序） | cardPools（按 key ordinal 排序）
potionPool | outOfCombatPotionPool | rootPotions | powerDeathHistoryRisk
hasEncounter | encounterSlots | rootEnemyCount | baseMaxEnergy | playerSlotIndex
baseOrbSlotCount | rootSwordSageAmount | unsettlingLampRelicId
hasIdentityContent | hasShuffleOrderEnchantment | hasBeforeFlushEnchantment
hasTheftContent | hasDecimalCardVars | hasInfiniteHpDisplayMonster | hasPaelsLegion
furCoatSpawnHpOne | rootLostHpMask | hasHistoryCourse | penNibRelicIndex
```

魔数常量为 `0x454C5552`；按小端字节解出的 ASCII 是 `RULE`。v1 的 `FRUL` 或 version 1 会被显式拒绝。

- 卡牌数组按内部 Id 保存每级 `CardStats`；Power/遗物变量按内部 Id 保存 `decimal` 数组；药水/怪物以存在位 + 完整值保存。
- 生成规则与卡池按键排序；`fieldCount` 必须同时等于 `Rules.SchemaFieldCount` 与 `RulesCodec.EncodedFieldCount`。
- `decimal` 写 4 个 `int`（`decimal.GetBits`），保留 scale 与精确值；字符串用 UTF-8 字节长度前缀。
- K0 对 30 个字段逐项变异，任何一个字段没写进字节都会失败；`decode→encode` 必须字节相同。

## 5. `GOLD`：D1 文件

```text
u32 magic="GOLD" | u16 version=3
string inputJson | string rulesSha256
rootCount | 每个根：i32 BATL 长度 + BATL 字节 + string choices
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

魔数常量为 `0x444C4F47`；按小端字节解出的 ASCII 是 `GOLD`。v1 的 `FGD1` 或 version 1 会被显式拒绝。

- 动作编码：`Kind, HandIndex, Target, PotionSlot, ChoiceCount, ChoiceCount × u64 选择位集`（不写结构体 padding）。
- 选择请求编码：`Kind, Effect, Timing, Source, SourceIsNone, OptionsAreGenerated, OptionCount, MinCount, MaxCount, OptionCount × 选项字节`；生成卡选项再写 `OptionCount × Card` 原始 92 字节。
- `immediateStatus`：0 = 动作不需要选择；1 = 需要且并行请求有效；2 = 内核边界（`UnsupportedException`/`CapacityException`）。
- K1 对每个合法动作记录完整补全的请求序列；K2 每个状态记录枚举顺序的合法动作、第一次选择请求与随机采用动作的完整请求序列。
- K4 `scoreHash` 是 `ScoreFeatures` 全部字段的确定性 SHA-256 截断；`boundHash` 是 `BoundValues`；`traitHash` 是 `SearchRouteTraits + pure + declined + pocketwatch threshold/played`。D1 校验器只验证内核可复现部分（K1/K2）。
- `meta.json` 记录 `formatVersion`（3）、`battleSize`（27,320）、`root`、`input`、`rulesSha256` 与 roots/K1/walks/walkStates/transitions 计数。

## 6. `SREQ` / `SRES`：D2 请求与结果

### 6.1 `SREQ`

```text
u32 magic="SREQ" | u16 version=3 | rules 长度 + RULE | battles 数量 + 每个 BATL | options
```

魔数常量为 `0x51455253`；按小端字节解出的 ASCII 是 `SREQ`。v1 的 `FRS1` 或 version 1 会被显式拒绝。

`options` 按固定顺序写全部确定值字段：

- 主构造：BeamWidth、MaxExpandedNodes、MaxTurns、SoftTimeBudget.Ticks、InitialPlayerMaxHp；
- CandidateScanCount、PotionPolicy（Earliest/Maximum/Strategy/EffectivePolicy/ForceDisabled）；
- Scorer、Ordering、HpTargetStop、Bound、Incumbent、EffectivePotionPolicy、TheftPolicy；
- ForcedPotionDirectives、EnforceForcedPotionDirectives、MinimumPotionUses、InitialExplicitPotionUses、InitialAutomaticPotionUses；
- PotionFreeBaseline、PotionReplacementHpCredit、SmartPotionEligibilityHpCeiling、AmbergrisPotionId、InitialAmbergrisUses、AuditPotionPolicy；
- InitialActionCount、Act3BossStrategy、PlanCommitment、InitialPlanPayoffCount、EarlyTurnScoutDepth、Novelty；
- InitialPlayerTurn、InitialSearchTurn、SecondRankBand、SecondRankBandWidth、AggressivePowerCommitment、PreserveReplayAllocatorOpening、PowerCommitments。

`ScoreRules` 完整写入：每张卡每级的 `CardScore`、每个 Power 的 `PowerScore`、药水战略代价、成长额度/目标、遗物计数器规则、战后治疗、Boss HP 减免、ContinuousThreatRanking、ContextualRanking（仅允许 null）、初始值、根计数、根药水、所有命名 Power/Card/Relic/Potion Id。

`BoundFeatures.Config` 完整写入所有开关、证书、数组与根药水。

宿主回调 `Pacer`、`Progress`、`PollTakeover`、`Trace` 只写 presence 位；解码后为 null，不影响路线选择。`ContextualRankingModel` 非 null 或 `NoveltySearchTelemetry` 非 null 时捕获显式抛 `NotSupportedException`，不会写半份数据。

### 6.2 `SRES`

```text
u32 magic="SRES" | u16 version=3
BestRoute | TopRoutes | BestIsVictory | BestScore(double bits)
ExpandedNodes | Transitions | Duplicates | TurnsSearched | PrunedIncumbents
CapacityPruned | CapacityDroppedChildren（按 key 排序） | EndedCompactions
EndedCompressedStates | SmartPotionPruned | BestPayoffCount | ScoutLayers
BestRootIndex | PotionPolicySatisfied | Takeover
```

魔数常量为 `0x53455253`；按小端字节解出的 ASCII 是 `SRES`。v1 的 `FRR1`（旧常量 `0x31535252` 按字节实际是 `RRS1`，旧注释写错了）或 version 1 会被显式拒绝。

不写 `Elapsed`、`WorkerYieldCount`、`FrameRecoveryWaitCount/Duration`、`MaxObservedGcPause` 与 novelty 遥测时间；这些字段不参与路线选择。`BestScore` 用 `BitConverter.DoubleToInt64Bits` 精确保存。

`CapacityDroppedChildren` 的键是数据格式的一部分。v2 中池容量区块的键是 `Beam.Pool`；v1 的 `FlashBeam.Pool` 在迁移时逐个映射，除此之外键与值不变。

## 7. `d1/<root>/config.br`

GOLD 只冻结 `Rules`（`objects/rules` 的 SHA-256），没有冻结 `ScoreRules` 与 `BoundFeatures.Config`。因此每个 D1 根另存一个 `config.br`：内容是 `SREQ` 请求的 Brotli 压缩，包含该根运行时的完整 `Rules`、`ScoreRules`、`BoundFeatures.Config` 与固定前缀的 `InitialActionCount`。

校验时只从 `config.br` 读 `Scorer`/`Bound`，随后完全由 `Battle` + Core 重算；配置缺失时显式失败，不静默跳过。`config.br` 已纳入 manifest 的完整性和计数。

## 8. manifest 与 provenance

`manifest.json` 字段：

| 字段 | 内容 |
| --- | --- |
| `formatVersion` | 当前为 3 |
| `battleSize` | 27,320，`Unsafe.SizeOf<Battle>()` |
| `provenance.d1` / `.d2` | M0 宿主捕获提交 `75217006903195bc94654680423bb9ca580ec48a` |
| `provenance.d1Config` | D1 配置导出提交 `66a43838d89e7a03b05c0f04d5e4941ff555baec` |
| `provenance.formatMigration` | v2→v3 迁移说明：`Battle` 改为打包牌区（头部 + 前 `CardCount` 张牌，容量 96→255）；D1 `stateHash`/`childHash` 逐状态核对旧镜像后改写为打包前缀哈希 |
| `provenance.refreeze` | 由 `Golden refreeze` 写入并更新：当前代码重算后的 D2 结果集 SHA-256。仓库数据自 M3b 重冻起带有该字段；普通 `manifest` 保留已有的值 |
| `provenance.d1Rerecord` | 由 `Golden rerecord` 写入：重录的 D1 根、原因与新记录的内容 SHA-256；普通 `manifest` 保留已有的值 |
| `compression` | `brotli` |
| `counts.d1` | roots/K1/walks/walkStates/transitions/files |
| `counts.d2` | capturedCalls/uniqueRequests/expectedResults/comparable/equal/mismatch/timeAffected |
| `totalBytes` / `targetBytes` | 总字节与目标预算 |
| `files` | 按 `path` ordinal 严格递增的 `{path,bytes,sha256}` 列表 |

D1/D2 的语义参照 CombatSolver `a62f1850`，但快照本身由 M0 宿主在 `752170…` 捕获；两者不是同一个提交。D1 的 410 个 `config.br` 与 `golden.br` 来自同一次确定性运行，M1a 已逐一核对 SHA-256。

### 8.1 D1 重录

原版语义修正后，旧 M0 录制会记录当时的内核行为；`Golden rerecord` 在副本上按当前 Sts2Solver
代码重录指定 D1 根：

```bash
dotnet run --project tests/Golden -c Release -- \
  rerecord tests/Golden/data /tmp/sts2-solver-rerecord --roots .local/rerecord-roots.txt --jobs 6
```

- 源目录只读；目标目录必须为空，两者不能互相包含。工具先复制整棵数据目录。
- 未列出的根保持复制后的原样。列出的根保留根状态、`config.br`、`meta.json` 的 `input` 与
  `rulesSha256`，重算 K1 完成、K2 游走、`scoreHash`/`boundHash`/`traitHash`，并按新计数
  更新 `meta.json`。
- K2 只使用 GOLD 里已记录的种子列表，不自行发明种子。策略来自 M0 宿主
  `tools/search/OfflineSearchHarness/Flash/FlashDifferentialProbe.cs`（CombatSolver
  `75217006`）：每条游走 `new Random(seed)`，每步按“4 次里 3 次偏向手牌”的规则抽取合法
  动作，再用同一 `Random` 从 `ChoiceEnumerator.Enumerate` 的选择里抽；最多 60 步。K1 按
  M0 的深度优先补全顺序、每次动作最多 64 个补全。
- 未改语义时，410 个根的 `golden.br`、`config.br`、`meta.json` 全部逐字节相同；K1/K2 的
  状态哈希、合法动作顺序、选择请求与选择、三个派生哈希都由当前内核重算并复核。
- 重录后的清单写入 `provenance.d1Rerecord`，格式是
  `roots=<按 ordinal 排序的根名> reason=<原因> sha256=<每个 golden.br 解码后 GOLD 内容的
  SHA-256 汇总>`。普通 `manifest` 与 `refreeze` 都保留这一字段，不会把它丢掉。
- 仓库数据在 M2c 合并时重录了 23 个根（原因 `M2c semantic corrections`）：EchoingSlash/Omnislice 攻击计数 8 个、临时敏捷行顺序 3 个、Power 的 Target/Applier 12 个。名单见清单的 `provenance.d1Rerecord`；游走条数、状态数与转移数都不变。

## 9. 校验范围

- D1 的 `golden.br` 与 `config.br` 应来自同一套根输入；`meta.json` 记录输入映射、规则哈希与计数。
- D1 重放比较状态、合法动作、选择、K1/K2 与特征哈希。
- D2 对冻结请求重新求解，逐字段比较路线、分数、计数、容量丢弃、审计与接管结果。确定性重放不使用墙钟截止。
- manifest 核对逐文件大小与 SHA-256、总字节数、D1/D2 计数及来源；数据更新记录保留在 `provenance`。
- 时间、GC、帧压力或运行期路径仅用于诊断，不写入确定性结果。

## 10. 格式迁移

v2 只改数据格式和对外标识里的名字，不改任何字段值：

| 项 | v1 | v2 |
| --- | --- | --- |
| `Battle` 魔数 | `FBAT` | `BATL` |
| `Rules` 魔数 | `FRUL` | `RULE` |
| D1 文件魔数 | `FGD1` | `GOLD` |
| D2 请求魔数 | `FRS1` | `SREQ` |
| D2 结果魔数 | `FRR1`（常量按字节实为 `RRS1`） | `SRES` |
| 所有编解码版本 | 1 | 2 |
| 容量丢弃键 | `FlashBeam.Pool` | `Beam.Pool` |
| D1 根名 | `flash-<name>` | `<name>` |
| manifest 键 | `flashBattleSize` | `battleSize` |

v3 把 `Battle` 从 96 张定长镜像改为打包前缀，并把 `Codec.FormatVersion` 加到 3：

| 项 | v2 | v3 |
| --- | --- | --- |
| `BATL` 载荷 | 完整 12,696 B 结构体 | 头部 3,860 B + 前 `CardCount` 张牌，最大 27,320 B |
| `Battle` 容量 | 96 | 255 |
| `Cards` 位置 | 头部之后、`HistoryCourseCard` 之前 | 头部最末，`HistoryCourseCard` 紧挨其前 |
| 状态身份/编码 | 完整结构体字节 | `Fingerprint.Bytes` 打包前缀 |
| 所有编解码版本 | 2 | 3 |

v3 迁移只重排字节：旧头部、旧 `HistoryCourseCard`（旧偏移 = 3,768 + 96 × 92 = 12,600）、前 `CardCount` 张牌，按新布局写成打包前缀；`meta.json` 的 `formatVersion`/`battleSize`、D1 内嵌 `rulesSha256`、`objects/rules` 文件名、D2 请求/期望结果文件名与 `rules-index` 随格式字节同步更新。

D1 的 `stateHash`/`childHash` 必须随状态身份定义一起迁移。一次性工具用新内核重放每个根的 K1 与 K2 游走：对每个重放状态先按旧 12,696 B 镜像算法计算旧哈希，必须等于文件中的存储值；核对通过后才写入打包前缀哈希。全部 410 个根核对通过后一次性写回，核对数量为 482,209 个游走状态、469,334 个转移、5,062 个 K1 子状态；任何一处不符就停止且不写回。K1/K2 的动作、选择、分数与展开结果不重算。迁移工具一次性运行，运行后由 `Golden manifest` 重写清单。

格式迁移需分别解码新旧载荷并逐字段核对。规则对象与 D2 请求按内容 SHA-256 命名，格式字节变化后，应同步更新引用它们的索引和 manifest。当前 v3 读取器显式拒绝 v1/v2 文件。

必须保留的旧名字：

- 卡牌 `FlashOfSteel` 与内容 Id `FLASH_OF_STEEL`；
- 质量语料的随机种子 `FLASH-QUALITY-<角色>-<NNN>`（换种子会生成另一批牌组）；
- `meta.json` 的 `input`、移植文件来源头、提交哈希与固定 GitHub 链接（CombatSolver 出处）；
- `docs/archive/` 中的历史文档。

## 11. 什么情况下允许更新快照

默认冻结。只有以下条件同时满足时才允许更新 `tests/Golden/data`：

1. 有意改变内核语义或搜索行为，而不是为了让测试通过；
2. 有实机或明确的游戏证据支持这次改变；
3. 经用户同意，并在同一提交中更新数据文件与 `manifest.json`；
4. 在提交信息中写明变更原因、影响范围与新旧计数。

不允许：

- 手改 `.br` 内的字节或 `manifest.json` 的哈希来绕过校验；
- 只更新 `expected` 而不更新请求/规则/配置；
- 把未验证的实机行为写成已验证；
- 为了让当前代码通过而改数据格式键、魔数或根名。

更新后应运行完整 `Golden verify`，确认 D1 `mismatched=0`、D2 全量相等，并重写 manifest 后确认 `git diff` 只包含有意变更。

2026-10-28 R2a-5 重录：`quality-REGENT-001`（walk 21/state 11）、
`qf-turn-start-choice-forge`（K1 root 0）、`qf2-gradient-failure`（walk 3/state 29）与
`ts-a-toasty`（walk 2/state 26）受“回合开始选择统一按全局选择器、不暂停”和
`FromHand`/`FromSimpleGrid` 全回合短路径影响。原版证据用当时的
`--combat-writeback-compare --writeback-root <根>` 在全局
`FirstChoiceCombatSetupSelector`/`NativeChoiceSelector` 下逐动作比较，四根均为 `same`；
R3-0 删除了该命令，终局改在战斗层由 `--combat-pilot-compare` 比较；
重录命令为（分两批写入同一目标）：

```bash
dotnet run --project tests/Golden -c Release -- \
  rerecord tests/Golden/data .local/golden-rerecord \
  --roots .local/r2a5-rerecord-roots.txt \
  --reason "R2a-5 no-pause turn-start choices" --jobs 6
dotnet run --project tests/Golden -c Release -- \
  refreeze .local/golden-rerecord .local/golden-final --jobs 8
```

重录后 `d1WalkStates=482209`、`d1Transitions=469334`，D1 410 文件/418 根 `mismatched=0`；
重录后的数据已复制到 `tests/Golden/data`。
D2 `refreeze` 后 1983/1983；`compare --config all-fill` 记录 1 个请求更好
（`06d36716…`，newScore 高于 oldScore）、1982 个相同、0 个更差；实际改写 3 个
`d2/expected`，另两个（`a198281c…`、`7c4b346b…`）质量相同、展开/转移计数变化。`provenance.d1Rerecord`
与 `provenance.refreeze` 由工具写入，未手改。
