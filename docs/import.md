# 原生战斗状态导入

[返回文档索引](README.md)

`src/Import` 把游戏原生的 `CombatState` 直接投影成 `Battle`，不建 CombatSolver 的
`SimulatedCombatState` 影子状态。它是只读的导入/规则编译层，集中读取游戏模型私有字段；
`Core` 不引用它，`Sts2Solver.sln` 也不包含它（CI 没有游戏文件）。建局、注入与原生状态转储
在独立的 `src/TestHost`，该层也会引用 `sts2.dll`/Harmony 并通过 publicizer 访问原生内部成员，
但只用于测试。

本页说明入口、字段来源、规则编译、评估配置编译、拒绝规则与离线对照方式。冻结快照格式见
[冻结快照](golden-data.md)，复跑命令见 [测试与验证](testing.md)。

本层只读原生状态与模型；建局、注入与转储会写游戏状态，放在独立的 `src/TestHost`
（`Sts2Solver.TestHost`），由战斗差分宿主与测试版 Mod 共用。

## 1. 入口与输入

```csharp
Rules rules = RulesCompiler.Compile(combatState, player, runState);
Battle battle = BattleImporter.Import(combatState, player, rules, options);
```

- `combatState`：当前原生战斗；导入与规则编译都只读，不改状态。
- `player`：本地单人玩家。普通根要求玩家出牌阶段；回合准备根用 `TurnSetupRoot` 标记，
  并由离线宿主传入 Start 时刻的卡牌对象集合（`RootCards`），不再用 `DeckVersion == null`
  近似“准备期间生成”。
- `runState`：玩家所在跑局。规则编译从它读取当前地图坐标、解锁状态与
  `CardMultiplayerConstraint`，并把它作为召唤物 scratch 创建路径的 RunRng；必须与
  `combatState.RunState` 是同一个实例。
- `rules`：`RulesCompiler` 编译出的该根规则表。`BattleImporter` 不再要求冻结对象；普通根的
  离线 `--import-compare` 默认仍解码 `objects/rules/<sha>.br`，加 `--compiled-rules` 时改用
  编译结果。前缀根与回合准备根必须用编译结果：前者需要先有前缀前规则表才能回放，后者需要
  一张共享规则表导入同 fixture 的多个准备根。
- `options`：在根上没有原生对应物的少量事实（`ShufflesCrossed`、`HasRisk`、事件失败边界、
  回合准备根标记与 Start 卡牌对象集合 `RootCards`）。默认值描述普通出牌阶段根。
  自动战斗的实时导入还会设置 `KnownEnemies`/`AllowDeadEnemies`/`RootEnemyCount`
  （稳定敌人顺序、死亡墓碑）、`RevivingEnemies`（预测为复活的死亡行）与 `Reconciliation`
  （从原生 History 重建本回合/累计计数）。这些字段只由 `src/Import/Combat/LiveCombatTracker.cs` 使用，根捕获保持
  旧行为，冻结快照的 410/410 不受影响。

输出先 `default(Battle)`，再逐字段写；不复用缓冲，因此 padding 始终为零。卡牌小数变量最后
按“detached 卡 → 牌堆存储顺序”规范化。

## 2. 字段组与来源

| 字段组 | 原生来源 | 移植/独立实现 |
| --- | --- | --- |
| `Round`,`PlayerTurn`,`Energy`,`Stars`,`Gold`,`Side`,`Phase` | `CombatState` / `PlayerCombatState` / `Player` | 直接读 |
| `NextCombatId` | `CombatState._nextCreatureId` | 直接读 |
| `ShuffleEventCount` | 模拟器创建时为 0 | 根事实 |
| `ShufflesCrossed` | 旧 `SimulationSnapshot` | 根事实（`ImportOptions`） |
| `Outcome` | 只有 `EventDefeat` 边界需要外部事实 | 根事实 |
| `Flags` | `CombatManager.IsAboutToLose`、`Player.IsActiveForHooks`、`Player.Osty`、旧快照风险 | 直接读 + 根事实 |
| `Player`,`Osty` | `Creature.CurrentHp/MaxHp/Block`；本回合未格挡伤害来自原生 History | 直接读 + History |
| `Rng`（9 条） | `RunState.Rng` 的 9 条流 | 直接读 |
| `Enemies` | `CombatState.Enemies`、`MonsterModel.NextMove/StateLog`、`MoveStateMachine`、`Rules.MonsterRule` | 直接读；AI 节点映射由规则编译器冻结 |
| `Powers` | `Creature.Powers` 的原生顺序，按 owner 分组 | `FlashStateProjections` 的 case 表移植；私有字段由 `NativeAccess` 集中读取 |
| `Relics` | `Player.Relics` 顺序 | `ProjectRelic` 移植；`StatefulRelicState` 从原生字段或原生 History 读取 |
| `Cards` | 五堆原生顺序、`CardModel`、`Rules.Card(id, upgrade)` | `FlashStateImporter.Card` 移植 |
| `DetachedCards` | `NightmarePower.selectedCard`、`SwipePower.StolenCard` | 直接读 |
| `DecimalVars` | `DynamicVars` 与规则基值的差 | `DecimalVars.TryAllocate/TryCanonicalize` |
| `Counters` | `CombatManager.History` 的 13 类条目 + 根上为 0 的账本 | 独立实现，对照 `RootCombatHistorySnapshot`/`SimulatedCombatState` 的 getter |
| `HistoryCourseCard` | 根上旧引擎的 `_lastAttackThisTurn` | 按旧桥根物化；前缀/准备起点当前为空，回放后由内核维护 |

`Counters` 的按项划分沿用 M2-0 报告 A.1.2：45 项是历史/账本，2 项原生私有状态，1 项引擎缓存。
新根上成长、死亡、偷窃与累计 HP 账本都从零开始；本回合计数从原生 History 现算。
`VitalSparkNormalizedAmount` 在普通根是 `int.MinValue`，回合准备根是 0（旧桥在准备阶段
至少做过一次规范化）。

实时导入（`Reconciliation=true`）与根捕获的区别：

- `CardsPlayedThisTurn`/`SkillCardsPlayedThisTurn` 按“已完成的出牌”计数，和内核的
  `RecordCardPlayed` 一致；根捕获保留旧桥的 first-in-series 口径。
- `CumulativeHpLost`、`PlayerUnblockedDamageEvents`、`OrbChannels` 从整场 History 现算；
  根捕获仍写 0/本回合值，保持冻结快照。
- `SkillPlayedThisSideTurn` 由本回合已完成技能推导；根捕获写 0。
- 原生会在敌人死亡后把它从 `CombatState.Enemies` 移除并重置行动状态机。实时导入用会话开始时
  冻结的已知敌人顺序保留墓碑行；预测为 `Reviving` 的行按复活行导入，行动/AI 仍从原生
  `MonsterModel` 读取。永久死亡行不读已经重置的行动状态机。
- `Roster` 按 `CombatState.Enemies` 的当前顺序导入（原生 `AddCreature` 后按 encounter slot
  稳定排序）。冻结的已知顺序只决定敌人行下标；之后出现的召唤物按 `CombatId` 升序追加，恢复
  内核的创建顺序，而不是原生槽位顺序。
- 卡牌 `Keywords` 只读 `CardModel.GetKeywordsWithSources(KeywordSources.Local)`。
  `CardModel.Keywords` 还会运行 `TryModifyKeywordsInCombat`（HexPower 给 Hexed 卡加
  Ethereal）；该授予在内核里是惰性查询，写进卡行会在 Hex 移除后残留并被重复计数。
- `Reconciliation` 导入的 `SOVEREIGN_BLADE` 把 `Private` 写为当前 `SWORD_SAGE_POWER` 层数：
  原生 `BaseReplayCount` 已包含该牌见过的每次 SwordSage 加成。根捕获仍写
  `Rules.RootSwordSageAmount`，保持冻结基线。
- 合成 `STUNNED`/`REVIVE_MOVE` 只存在于 `MonsterModel.NextMove`，不在 `StateLog` 或
  `MoveStateMachine.States`；后继统一从 `NextMove.FollowUpState?.Id ?? FollowUpStateId` 读取。
  `STUNNED` 且原生持有 `ImbalancedPower` 时同时置 `EnemyFlags.SkipNextMove`：该 Power 的
  `AfterDamageGiven` 只会在完全格挡的攻击后调用 `CreatureCmd.Stun`（或置 BowlbugRock
  `IsOffBalance`），这是原生唯一的失衡眩晕来源。

## 3. 规则编译

`RulesCompiler` 输入原生战斗、玩家与 `RunState`，输出 `Rules`。语义以 CombatSolver
`src/Flash/Bridge/FlashRulesCompiler.cs`（`a62f1850`）为准；旧引擎的模拟状态访问改成原生模型
读取，不复制 `src/Engine` 的状态定义。

### 3.1 30 个构造参数

| # | 参数 | 实现 |
| ---: | --- | --- |
| 1 | `Cards` | 原生 `CardModel.ToMutable()` + `UpgradeInternal/FinalizeUpgradeInternal` 克隆规范卡；根卡/生成卡/卡池卡按升级等级建表 |
| 2 | `PowerVars` | canonical Power 的 `DynamicVars[name].BaseValue`，名字来自 Sts2Solver `PowerDef.VarNames` |
| 3 | `Potions` | canonical 药水的目标/稀有度/变量；战略成本查固定政策表（Token=0、High=18、Elevated=14、其余=9） |
| 4 | `RelicVars` | canonical 遗物的 `DynamicVars[name].BaseValue` |
| 5 | `Monsters` | 原生 `MonsterMoveStateMachine`、静态权重、条件分支、攻击意图与怪物私有整数；已知敌人按根上模型编译 |
| 6 | `SpawnRules` | 从已知敌人的 `MonsterDef.SpawnedMonsterIds` 做 BFS，首次出现顺序分配下标 |
| 7 | `CardPools` | 玩家真实解锁状态 + `CardFactory.FilterForCombat` + 单人/多人约束 |
| 8 | `PotionPool` | `PotionFactory.GetPotionOptions` + `CanBeGeneratedInCombat` |
| 9 | `OutOfCombatPotionPool` | 同一药水选项，不带战斗内过滤 |
| 10 | `RootPotions` | 原生药水槽的 `id+1` |
| 11 | `PowerDeathHistoryRisk` | 查 `MirrorRiskTables` 静态表 |
| 12 | `HasEncounter` | 原生 `Encounter != null` |
| 13 | `EncounterSlots` | 原生 `Encounter.Slots` 顺序 |
| 14 | `RootEnemyCount` | 原生 `CombatState.Enemies.Count` |
| 15 | `BaseMaxEnergy` | `Player.MaxEnergy`（未经过 Hook 的基础值） |
| 16 | `PlayerSlotIndex` | 单人恒 0 |
| 17 | `BaseOrbSlotCount` | `Player.Character.BaseOrbSlotCount` |
| 18 | `RootSwordSageAmount` | 玩家 `SwordSagePower` 的当前量 |
| 19 | `UnsettlingLampRelicId` | 遗物顺序中第一个 `UnsettlingLamp` 的规则 id |
| 20 | `HasIdentityContent` | 角色类型 + SwordSage + 根卡/Power/遗物的 Sts2Solver 身份标记 |
| 21 | `HasShuffleOrderEnchantment` | 根卡附魔的 `HookMaskExt.ModifyShuffleOrder` |
| 22 | `HasBeforeFlushEnchantment` | 根卡附魔的 `HookMaskExt.BeforeFlush` |
| 23 | `HasTheftContent` | 两个盗贼遭遇、盗贼怪物或根上的盗贼 Power |
| 24 | `HasDecimalCardVars` | 已编译卡牌中任一 `UsesDecimalDamageVar` |
| 25 | `HasInfiniteHpDisplayMonster` | 已知敌人或召唤闭包中任一 `HasInfiniteHpDisplay` |
| 26 | `HasPaelsLegion` | 未融化遗物中的 `PaelsLegion` |
| 27 | `FurCoatSpawnHpOne` | 未融化 `FurCoat` 的标记坐标包含当前地图坐标 |
| 28 | `RootLostHpMask` | 原生 History 的本回合未格挡伤害，玩家/Osty/每个敌人一位 |
| 29 | `HasHistoryCourse` | 未融化遗物中的 `HistoryCourse` |
| 30 | `PenNibRelicIndex` | 遗物行中 `PenNib` 的下标（融化遗物也计数） |

卡牌规范值不读根卡：根卡可能带 Dampen、附魔或临时修饰。编译器取 `CanonicalInstance`，
按目标升级等级逐级克隆并升级；升级后的 `CurrentUpgradeLevel` 必须等于目标等级，否则以
`unsupported-card` 拒绝。卡池与药水池都使用玩家 `UnlockState` 的同一份解锁状态。

已知敌人的静态参数与初始 AI 寄存器由 `NativeAccess.MonsterInt` 读取；召唤物在私有 scratch
`CombatState` 中通过 `ToMutable` + `SetUpForCombat` 创建，因此其状态机、初始节点和静态值
不依赖根上的同类型敌人。召唤闭包保持原生 BFS 顺序。

### 3.2 镜像覆盖率静态表

`Card.PlaysRecordRisk` 与 `Rules.PowerDeathHistoryRisk` 不是游戏数据，而是旧引擎
`CardOnPlayMirrors.ResolveDispatchKind` / `AfterDeathMirrors.ResolveDispatchKind` 的镜像覆盖率。
本仓库不重写这套推断逻辑，而是把结果冻结为 `src/Import/Combat/MirrorRiskTables.cs`：

- 导出源：CombatSolver `src/Flash/Bridge/FlashRulesCompiler.cs` 的同一查询，源提交 `a62f1850`；
- 覆盖 603 个原版卡牌 id、283 个原版 Power id（`ModelDb.All` 的全量 canonical 内容）；
- 表里没有的 id 以 `missing-mirror-risk` 显式拒绝，不默认为 false；
- `--rules-table-check` 对照 `ModelDb.All` 检查遗漏/多余 id，并检查 Sts2Solver Registry 的每个
  card/power id 都有表项。

导出钩子提交在 CombatSolver `flash/m0` 的 `91641ff0`（环境变量 `M2A3_RULES_TABLES_DUMP`），读取 `a62f1850` 的原引擎；游戏或 CombatSolver 更新后用它重新导出，再用 `--rules-table-check` 复核。

## 4. 评估配置编译

每个 D1 根还冻结一份 `d1/<root>/config.br`：CombatSolver 的 `FlashGoldenConfigDump`
（`66a43838`）写出的 `SREQ` 请求，包含规则、根列表与
`new BeamOptions(1, 1, 1, TimeSpan.Zero, roots[0].Player.MaxHp)` 再加 `Scorer`、`Bound` 和
`InitialActionCount`。`src/Import` 现在从原生模型重新编译 `ScoreRules` 与
`BoundFeatures.Config`，不读 `config.br`。

```csharp
SearchPolicy policy = SearchPolicyCapture.Capture(combatState, player);
EvaluationRootFacts facts = EvaluationRootFacts.Default with { InitialActionCount = ... };
Scorer scorer = EvaluationConfigCompiler.CompileScorer(combatState, player, rules, battle, policy, facts);
BoundFeatures bound = EvaluationConfigCompiler.CompileBound(combatState, player, battle, policy, facts);
SearchCase request = EvaluationConfigCompiler.CompileRequest(
    combatState, player, rules, battle, policy, facts);
```

`ScoreCompiler` 与 `BoundCompiler` 的语义以 CombatSolver `a62f1850` 的
`src/Flash/Bridge/FlashScoreBridge.cs`、`FlashBoundBridge.cs` 为准；原引擎的模拟快照访问改成
原生模型 + 导入后的 `Battle`，不复制 `src/Engine`。

### 4.1 `SearchPolicy`

`SearchPolicy` 是不可变值对象，只包含两个 Bridge 实际读取的政策字段。对照用的值不从
`config.br` 读取，而是按 M0 离线宿主的固定设置、`SolverSettings` 默认值、
`SolverController.CaptureSearchPolicy` 的派生规则和请求覆盖重建：

| 字段 | 默认值来源 | 说明 |
| --- | --- | --- |
| `GrowthBudgets` | `ModRuntime.ApplyFixedBudgetSettings`：资源设置为空时 `default` | 十项内置成长额度；`IgnoreLongTermRewards` 打开时 `EffectiveGrowthBudgets` 归零 |
| `RequiredGrowthRewards` | 根派生：`GrowthOpportunityPolicy.Capture` | 手牌/牌堆里的成长来源目标；不只看根快照 |
| `HasUnboundedGrowthTargets` | 根派生 | 动态复制/重放来源使目标不可证 |
| `RelicTargets` | `SolverSettings.RelicStrategyEnabled=true` 且规则为空 | 按拥有遗物、范围与 id 排序过滤 |
| `Act3BossStrategy` | `SearchPolicySnapshot.IsAct3BossEncounter` | 第三幕 Boss 的三个 encounter id |
| `ActTransitionBossHpStrategy` | `SolverSettingsData` 默认 `ProgressionFirst` | `ActClearHeal` 是否保留 |
| `FinalBossHpStrategy` | 同上 | `RunEnding` 是否保留 |
| `IgnoreLongTermRewards` | 默认 `false` | 同时影响成长额度与目标 |
| `TheftPolicy` | `TheftEncounterStrategy.IsApplicable` | 盗贼遭遇/怪物时 `PreserveResources`，否则 `null` |
| `BaseScoreOnly` | harness `ordering=baseline` | 请求覆盖可打开 |
| `ContinuousThreatRanking` | harness 默认 `false` | 请求覆盖可打开 |
| `BeamWeightPerturbation` | harness 默认 `null` | 请求覆盖项与 scale 同时给出 |
| `HasDevelopmentStrategy` / 镜像登记标志 / 第三方成长标志 | 默认全空 | 非空且生效时以 `unsupported-policy` 拒绝 |

分数/下界 Bridge 不读取 `PotionPolicy`、`FixedBudget`、并行度、`IncludeTurnSetup` 等搜索
执行字段，因此它们不在 `SearchPolicy` 里。`OfflineRequest` 补了会影响政策的请求字段
（`growthBudgets`、`relicCounterRules`、`ignoreLongTermRewards`、两个 Boss 策略、
`theftPolicyForTest`、`act3BossStrategyForTest`、`baseScoreOnlyForTest`、
`continuousThreatRankingForTest`、`beamWeightTermForTest`/`beamWeightScaleForTest`）。
410 个冻结 fixture 都没有覆盖这些字段，全部使用固定默认值与根派生值；未知字段仍显式失败。

### 4.2 `ScoreRules`

| 字段 | 实现 |
| --- | --- |
| `Cards` | 对 Sts2Solver Registry 的每个 card id、每个 `0..MaxUpgradeLevel` 用 canonical 卡克隆并升级；读 `Damage/Block/Cards` 基值、类型、动态类型、变量下标、攻击段数、飞刀来源、动态变量关键字、回合结束效果、Osty/Shiv 标签、`LatentValue`/`LatentTrait` 与 `CalculatedKind` |
| `Powers` | 每个 Registry power 的 `StrategicKind`、`StrategicRequirements`、`PersistentSetupTraits`、`ITemporaryPower`、原生类型与 Danse Macabre 能量阈值 |
| `PotionStrategicCost` | canonical 药水 + `PotionValuationRegistry` 的 Token/High/Elevated/Standard 政策；持未融化 Petrified Toad 时 Potion Shaped Rock 为 0 |
| `GrowthBudgets` / `RequiredGrowthRewards` | 取 `SearchPolicy.Effective*`；第三方额度以 `unsupported-policy` 拒绝 |
| `RelicCounterRules` | 按政策目标顺序映射 canonical 遗物与 `MeatOnTheBone` 阈值 |
| `PostCombatRelicHeal` | 原生 `BurningBlood`/`BlackBlood` 的 `Heal` 与 `MeatOnTheBone` 的 `Heal`/`HpThreshold` |
| `BossHpRelief` | 原生 `Encounter.RoomType`、当前幕、`SecondBossEncounter`，再按两个 Boss 策略裁决 |
| `ContinuousThreatRanking` / `BaseScoreOnly` / `BeamWeightPerturbation` | 来自 `SearchPolicy` |
| `InitialPlayerMaxHp` / `IsActEndingBoss` / `InitialEnemyCount` | 导入 `Battle` 的玩家 MaxHp、Boss 减免、敌人数 |
| 根药水与药水计数 | 导入 `Battle` 的 `RootPotions`/`RootPotionSlotCount`；普通根初始计数为 0 |
| 33 个命名 id | `ModelDb.Power<T>()`/`Card<T>()`/`Relic<T>()`/`Potion<T>()` 的 native id 经 Registry 解析；未注册为 `ushort.MaxValue` |

四个 rank baseline（`InitialPersistentBuffValue`、`InitialEnemyStrengthSuppression`、
`InitialEnemyWeakTurns`、`InitialRetainedAttackValue`）先用四项为 0 的 `ScoreRules` 构造 Core
`Scorer`，在根上算 `ScoreFeatures`，再把四个 feature 值填回最终表。这样与原引擎
`CombatBeamSolver`/`SimulationSnapshot` 的冻结值逐字段一致，不复制原引擎计算。回合准备根按
`FlashScoreBridge.cs:74-80` 强制四项为 0；前缀根按同一 Bridge 的 request-root 语义，用前缀前
`Battle` 与前缀前规则表计算四项 baseline，而评分表本身仍取回放后的内容。

`BossHpRelief` 的幕结构诊断由 `NativeStateDump` 记录：`run.act.index/count/id`、
`run.act.boss`、`run.act.secondBoss`、`run.tutorial.discoveryOrder` 与 `run.unlock.*`。
这些字段只用于核对建局路径，不参与导入判定；`SecondBossEncounter` 缺失时写 `-`。

### 4.3 `BoundFeatures.Config`

下界的逐内容分类表冻结在 `src/Import/Combat/HealingBoundTables.cs`，来源是 D1 的 384 份 enabled
`config.br`：384 份的 `ComponentUnsafe*`、`SafeEnchantment`/`SafeAffliction`、
`KnownBlocking*`、`Regen*`/`Reattach*`/`Swipe*`、药水分类与数值逐字节相同，因此只保存一份，
生成时逐一比较过。这些数组是 CombatSolver `FlashBoundBridge` 分类器在导出时的输出，本仓库没有
重新推导分类判定；因此 `--config-compare` 在这一部分只证明表被完整、原样地取出并按根正确组装，
不是独立的编译证据。覆盖合同验证数组长度等于 `Registry.*Count`。游戏版本或分类规则变化时，
要在 CombatSolver 侧重新导出（用 `flash/m0` 的 `FLASH_CONFIG_OUT` 钩子生成新的 `config.br` 再提取），
不能只改本表。

根上的判定按 `FlashBoundBridge`：

- 失效顺序：`growth_targets` → `relic_targets` → `theft_policy` → `growth_rewards` →
  `root_risk` → `pending_returning_cards` → `remaining_certificate`；失效时所有数组为空、
  `FutureHealPotential=int.MaxValue`、`Certificate=None`、`RootPotionSlotCount=0`。
- `HasOnlyPostCombatHealing`：角色/Ironclad 等封闭集合、原生敌人、无药水、遗物仅
  Burning Blood/Black Blood/Bound Phylactery/Ring of the Snake、玩家 Power 仅
  Neurosurge/Doom、卡牌仅白名单且无附魔/苦难。
- `Component` 证书：audited MVID、单人、无 Modifier、卡牌属 109 个 `ComponentInitialCard`
  类型且附件安全、根监听者分类安全、上界可计算；`Tainted` 等卡牌附件在根监听者枚举里与旧
  引擎一样被过滤。
- `KnownNative` 证书：单人、全原生模型与附件、无 Modifier/第三方监听；离线语料全部满足。
- `FutureHealPotential`/`PostCombatHeal`/`RequireCertifiedHealingProof`/`PruneEqualHp` 按
  Bridge 的根事实与 `policy.RelicTargets.Count` 设置。`ForceAllPotionsDisabled=false`、
  `MaximumPotionUses=null`：离线求解器没有 potion-policy override 或上限。
- 组件证书根上的 RegenPotion 以 `regen_potion_policy` 失效；根上有 SwipePower 以
  `swipe_power` 失效。

### 4.4 离线对照

`--config-compare` 用编译规则、导入根与编译出的 `Scorer`/`Bound` 重组 `SREQ`，与
`config.br` 逐字节比较；不一致时依次比较规则、根、`ScoreRules`、`Bound` 与其余选项并打印
第一处字段。3 个回合准备 fixture 的 `config.br` 含全部准备根，离线宿主按 GOLD 的选择顺序
把独立重建的准备根排入请求后做完整请求比较。前缀根的评分/下界根事实取前缀前原生状态
（含四个 rank baseline 与 `InitialEnemyCount`），评分表/规则取回放后的 `Battle`，药水计数取
`PrefixReplay` 的累计值。

```bash
dotnet artifacts/bin/Differential/release/Differential.dll \
  --root kg2-b-stomp --config-compare
dotnet artifacts/bin/Differential/release/Differential.dll \
  --all --jobs 8 --config-compare
```

同时还用编译出的 `ScoreRules`/`Bound` 在导入根上算 D1 的 `GoldenHash.Score`/`GoldenHash.Bound`
（与 `tests/Golden/GoldenHash.cs` 同一份代码），对照 `golden.br` 的 `scoreHash`/`boundHash`。
410 个输入做完整请求；418 个根逐一对照哈希。

### 4.5 搜索请求编译器

`SearchRequestCompiler` 只读原生根、已编译的规则和 `SearchPolicy`，冻结 `SearchCase` 所需的评分、下界、根生命、药水默认策略、失窃目标、遗物/成长目标与回放状态。能力承诺目录根据根中已登记的能力牌和真实 `RunState.TotalFloor` 编译；生成闭包内的登记能力也有描述符。普通界面未设置药水指令时使用 Smart。

`SearchPerformanceProfile` 提供基准、窄、宽、进攻四个最终宽度及每单位宽度的时限；档位值见 [战斗搜索](combat-ai.md)。Mod 与战斗差分宿主 再把宿主内存预算加入 `CombatSearchRequest`，交由 `CombatSearchCoordinator` 构造成员。成员宽度、评价、保留、用药限制与软时间在协调器中设置，全部受原请求保护/强制指令约束，生产没有展开节点上限。

`SCV2` 保存组合请求及总预算，旧 `SREQ` 仍表示冻结的确定性单成员请求。宿主回调与帧压力信号只在运行时传递；worker 不再读取 live 战斗或全局搜索设置。

## 5. 拒绝规则

所有拒绝抛 `UnsupportedImportException`，`Reason` 是稳定字符串。绝不返回带默认值的半份
`Battle` 或半份 `Rules`。

| 原因码 | 含义 |
| --- | --- |
| `multi-player` | 玩家数不是 1，或多人专用内容 |
| `root-phase` | 根不在玩家出牌阶段 |
| `unsupported-card/power/relic/potion/orb/monster/enchantment/affliction` | 内容定义或变量缺失 |
| `unsupported-closure` | 生成卡/Power/卡池/药水池/召唤闭包缺定义 |
| `missing-mirror-risk` | 静态镜像覆盖率表没有该 card/power id |
| `test-content` / `deprecated-content` | Mock/废弃内容 |
| `third-party-content` | 模型类型来自 `sts2` 以外的程序集 |
| `missing-private-field` | 读取层找不到字段、内部数据为 null 或类型不符 |
| `unclaimed-private-state` | 捕获了私有状态，但没有任何投影认领 |
| `unrepresentable-state` | 原生值无法用 `Battle` 表示（分数、越界、复制品等） |
| `capacity` | 固定容量不足 |
| `rules-mismatch` | 原生根与传入的 `Rules` 不一致，或 `RunState` 不是同一实例 |
| `known-enemy-history` | 已逃离/已死亡敌人破坏了 `KnownEnemies` 顺序 |
| `prefix-plan` | 前缀动作或选择无法按 CombatSolver 的计划语义翻译/回放（缺手牌、目标、选项或选择次数不符） |
| `run-deck-hook` | 牌组卡实现局外监听 Hook |
| `unsupported-policy` | 评估配置政策含未移植的第三方登记/DevelopmentStrategy/非法覆盖 |
| `unsupported-healing-certificate` | 需要尚未移植的 environment 治疗证书特殊分支（保留原因码；当前语料未触发） |
| `invalid-golden` / `rules-build` | 离线对照的冻结对象损坏，或规则编译内部不变量失败 |

### 5.1 全有或全无

`PrivateStateLedger` 在导入前枚举旧桥会捕获的根私有状态：

- `PowerPredictionStateSupport.CaptureRootState` 的 13 种 Power；
- `RelicPredictionStateSupport.IsTracked` 的 25 种遗物；
- `SimulatedCombatState.IsStatefulRelic` 的 19 种遗物。

每个条目必须由 `ProjectPower`/`ProjectRelic` 认领。例如 `SoulboundPower`/`CacophonyPower`
虽然会被旧桥捕获，但当前没有投影实现，因此根会被 `unclaimed-private-state` 拒绝，而不是
丢掉状态。没有捕获条目的怪物私有整数也要么被 `MonsterDef.AiRegisterNames`/惰性名单认领，
要么拒绝。

### 5.2 第三方内容

所有读到的 `AbstractModel`（卡、Power、遗物、药水、球、怪物、附魔、苦难）先检查类型程序集
必须是 `sts2`；否则以 `third-party-content` 拒绝。`RejectRunListenerDeckCards` 对玩家牌组做
同样的检查，并拒绝带局外监听 Hook 的非惰性牌组卡。

## 6. 离线对照模式

`tests/Differential` 的 `--import-compare` 为每个根建原生状态、导入，再与
`d1/<root>/golden.br` 的期望 `BATL` 逐字节比较：

```bash
dotnet artifacts/bin/Differential/release/Differential.dll \
  --root kg2-b-stomp --import-compare
dotnet artifacts/bin/Differential/release/Differential.dll \
  --all --jobs 8 --import-compare
```

`--rules-compare` 编译 `Rules`，用 `RulesCodec.Encode` 编码后与该根的冻结规则对象逐字节比较；
不一致时打印 `RulesDiff` 的第一处表/项及前 16 行差异：

```bash
dotnet artifacts/bin/Differential/release/Differential.dll \
  --root kg2-b-stomp --rules-compare
dotnet artifacts/bin/Differential/release/Differential.dll \
  --all --jobs 8 --rules-compare
```

`--import-compare --compiled-rules` 改用 `RulesCompiler` 的结果导入，而不是解码冻结对象：

```bash
dotnet artifacts/bin/Differential/release/Differential.dll \
  --all --jobs 8 --import-compare --compiled-rules
```

每根只产生三种结果：`same`、`diff`、`reject`。`--all` 对照全部 410 个输入（418 个根），
汇总 `same/diff/reject`，并分别按首个差异字段/表、拒绝原因分组计数。前缀根先在原生前缀之前
导入 `Battle`，再用 `PrefixReplay` 在内核上执行冻结动作；回合准备 fixture 用原生
`SetupPlayerTurn` 前缀捕获 Start 卡牌集合，并逐个选项重建原生准备阶段，导入全部准备根。
`--rules-table-check` 在初始化游戏静态状态后校验静态表：

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --rules-table-check
```

### 6.1 回合准备的三个 fixture

`qf2-gradient-failure`、`ts-b-toolbox`、`ts-a-toasty` 是回合准备 fixture。它们各自的
`golden.br` 有 4/3/4 个根，共 11 个根。旧桥在 `CombatManager.SetupPlayerTurn` 之前捕获 Start
根，再在影子引擎上枚举准备选择；离线宿主在同一个原生入口用 Harmony 前缀记录 Start 时刻的
卡牌对象集合，然后按原生准备页的每个选项重建一次同一场战斗（每次都跳过测试宿主的初始注入），
把每个分支导入成 `Battle`。这样准备选择本身仍由原游戏执行，分支状态不需要在 Sts2Solver 内核里
重写 `SetupPlayerTurn`。

原生选项顺序与 M0 的搜索枚举顺序不同，且 M0 会按语义去重/限额：Toasty Mittens 的 5 个选项
在 GOLD 里留下 4 个根（其中两个根投影字节相同），Toolbox 的 3 个选项留下 3 个根。比较时按
`golden.br` 的根顺序（其 `Choices` 与之一致）把独立重建出的根逐一对齐；只借用 GOLD 的顺序
做请求根列表排序，根内容仍由原生重建。三个 fixture 的普通根同时是上述准备根之一（GOLD 下标 1/0/1）。

R2a-5 的建局开战选择不改变这个导入合同：Core 侧用 `ChoiceRequest` 位掩码从进点前
`RunState` 重放，原版侧对照工具按完整序列各自重新进点；3 个回合准备 fixture 仍按原生准备页
的每个选项重建整场战斗，用于冻结的导入/配置对照。

## 7. 检查入口与限制

使用 `--rules-compare`、`--rules-table-check`、`--import-compare`（可加 `--compiled-rules`）和 `--config-compare` 分别检查规则覆盖、状态导入及请求编译。各入口报告逐根差异与稳定拒绝码，使用方法见 [测试与验证](testing.md)。

3 类负例由 `--import-negative` 验证：

```text
NEGATIVE case=private-read status=rejected reason=missing-private-field
NEGATIVE case=unclaimed-private-state status=rejected reason=unclaimed-private-state
NEGATIVE case=third-party-content status=rejected reason=third-party-content
```

未完成/限制：

- 前缀回放的规则表由 `RulesCompiler.CompileAfterPrefix` 从“前缀前原生状态 + 回放后
  `Battle`”重建；前缀动作本身不在游戏里执行，因此如果后续前缀包含内核尚未支持的结算，会以
  `prefix-plan`/内核异常显式拒绝，而不是回退到游戏重放。
- 回合准备分支的**选取与排序**没有独立复现：M0 的枚举会按语义去重、限额并按自己的顺序排列（Toasty Mittens 5 个原生选项留下 4 个根），对照时借用 `golden.br` 的选择记录来对齐。各分支的状态内容是独立重建的。实机测试模式只捕获第一个准备选项；完整枚举与 GOLD 顺序仍需后续批次对照。
- 回合准备根目前按原生准备页的每个选项重建整场战斗；多段准备选择（一次准备阶段多次选牌）
  尚未在语料中出现，遇到时选择器的数量合同会失败而不是静默取第一个。
- 手牌上限读游戏的 `CardPile.MaxCardsInHand`（当前为 10）。RitsuLib 等 Mod 另行修改手牌上限的方式不在支持范围。
- 私有字段读取绑定当前游戏版本；字段缺失、类型不符或内部数据为 null 都显式拒绝。
- 镜像覆盖率表是由 `a62f1850` 一次性导出的数据，不是运行时推断；游戏或 CombatSolver 更新后
  必须重新导出并用 `--rules-table-check` 校验。
- `CanCertifyRemainingHealingEnvironment` 的 Regent/Silent 一般分支已移植；Defect 与
  Decimillipede 的特殊分支尚未移植。当前语料没有根触发这些特殊分支（所有非 Component 根的
  environment 证书都为 false）；M2b 接入语料外根时需要补判定。
- `Component`/`KnownNativeCertificate`/`HasOnlyPostCombatHealing` 的第三方 run/combat
  订阅者、BaseLib card modifier、AdaptedOnPlay 按离线宿主的固定“无第三方”口径处理；实机 Mod
  根需要另行拒绝或补证据。
- 实机测试支持捕获状态与确定性单成员搜索对照；回合准备 fixture 只比较评分/下界子部件并跳过搜索。前缀根与完整准备阶段多根排序仍由离线工具检查，测试会话的幕结构处理见 [Mod 入口与无头捕获](mod.md)。
