# 原生战斗状态导入

[返回文档索引](README.md)

`src/Import` 把游戏原生的 `CombatState` 直接投影成定长 `Battle`，不建 CombatSolver 的
`SimulatedCombatState` 影子状态。它是仓库中唯一引用 `sts2.dll`、Godot/Harmony 类型并读取游戏
模型私有字段的层；`Core` 不引用它，`BlitzSolver.sln` 也不包含它（CI 没有游戏文件）。

本页说明入口、字段来源、规则编译、评估配置编译、拒绝规则与离线对照方式。冻结快照格式见
[冻结快照](golden-data.md)，复跑命令见 [测试与验证](testing.md)。

## 1. 入口与输入

```csharp
Rules rules = RulesCompiler.Compile(combatState, player, runState);
Battle battle = BattleImporter.Import(combatState, player, rules, options);
```

- `combatState`：当前原生战斗；导入与规则编译都只读，不改状态。
- `player`：本地单人玩家；根要求玩家出牌阶段（`RequirePlayPhase`）。
- `runState`：玩家所在跑局。规则编译从它读取当前地图坐标、解锁状态与
  `CardMultiplayerConstraint`，并把它作为召唤物 scratch 创建路径的 RunRng；必须与
  `combatState.RunState` 是同一个实例。
- `rules`：`RulesCompiler` 编译出的该根规则表。`BattleImporter` 不再要求冻结对象；离线
  `--import-compare` 默认仍解码 `objects/rules/<sha>.br`，加 `--compiled-rules` 时改用编译
  结果。
- `options`：在根上没有原生对应物的少量事实（`ShufflesCrossed`、`HasRisk`、事件失败边界、
  回合准备普通根标记）。默认值描述普通出牌阶段根。

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
| `HistoryCourseCard` | 根上旧引擎的 `_lastAttackThisTurn` 为空 | 不填；跨回合槽由 M2a-5 处理 |

`Counters` 的按项划分沿用 M2-0 报告 A.1.2：45 项是历史/账本，2 项原生私有状态，1 项引擎缓存。
新根上成长、死亡、偷窃与累计 HP 账本都从零开始；本回合计数从原生 History 现算。
`VitalSparkNormalizedAmount` 在普通根是 `int.MinValue`，回合准备普通根是 0（旧桥在准备阶段
至少做过一次规范化）。

## 3. 规则编译

`RulesCompiler` 输入原生战斗、玩家与 `RunState`，输出 `Rules`。语义以 CombatSolver
`src/Flash/Bridge/FlashRulesCompiler.cs`（`a62f1850`）为准；旧引擎的模拟状态访问改成原生模型
读取，不复制 `src/Engine` 的状态定义。

### 3.1 30 个构造参数

| # | 参数 | 实现 |
| ---: | --- | --- |
| 1 | `Cards` | 原生 `CardModel.ToMutable()` + `UpgradeInternal/FinalizeUpgradeInternal` 克隆规范卡；根卡/生成卡/卡池卡按升级等级建表 |
| 2 | `PowerVars` | canonical Power 的 `DynamicVars[name].BaseValue`，名字来自 Blitz `PowerDef.VarNames` |
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
| 20 | `HasIdentityContent` | 角色类型 + SwordSage + 根卡/Power/遗物的 Blitz 身份标记 |
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
本仓库不重写这套推断逻辑，而是把结果冻结为 `src/Import/MirrorRiskTables.cs`：

- 导出源：CombatSolver `src/Flash/Bridge/FlashRulesCompiler.cs` 的同一查询，源提交 `a62f1850`；
- 覆盖 603 个原版卡牌 id、283 个原版 Power id（`ModelDb.All` 的全量 canonical 内容）；
- 表里没有的 id 以 `missing-mirror-risk` 显式拒绝，不默认为 false；
- `--rules-table-check` 对照 `ModelDb.All` 检查遗漏/多余 id，并检查 Blitz Registry 的每个
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
| `Cards` | 对 Blitz Registry 的每个 card id、每个 `0..MaxUpgradeLevel` 用 canonical 卡克隆并升级；读 `Damage/Block/Cards` 基值、类型、动态类型、变量下标、攻击段数、飞刀来源、动态变量关键字、回合结束效果、Osty/Shiv 标签、`LatentValue`/`LatentTrait` 与 `CalculatedKind` |
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
`Scorer`，在导入根上算 `ScoreFeatures`，再把四个 feature 值填回最终表。这样与原引擎
`CombatBeamSolver`/`SimulationSnapshot` 的冻结值逐字段一致，不复制原引擎计算。回合准备根按
`FlashScoreBridge.cs:74-80` 强制四项为 0。

### 4.3 `BoundFeatures.Config`

下界的逐内容分类表冻结在 `src/Import/HealingBoundTables.cs`，来源是 D1 的 384 份 enabled
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
第一处字段。3 个回合准备 fixture 的 `config.br` 含全部准备根，本批只导入普通根，因此只比较
`ScoreRules` 与 `Bound` 子结构；普通根仍做完整请求比较。

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --root kg2-b-stomp --config-compare
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --all --jobs 8 --config-compare
```

同时还用编译出的 `ScoreRules`/`Bound` 在导入根上算 D1 的 `GoldenHash.Score`/`GoldenHash.Bound`
（与 `tests/Golden/GoldenHash.cs` 同一份代码），对照 `golden.br` 的 `scoreHash`/`boundHash`。
394 个普通根做完整请求 + 哈希；3 个回合准备 fixture 的普通根也按选择向量选到对应
golden 根做哈希。

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
虽然会被旧桥捕获，但本批没有投影实现，因此根会被 `unclaimed-private-state` 拒绝，而不是
丢掉状态。没有捕获条目的怪物私有整数也要么被 `MonsterDef.AiRegisterNames`/惰性名单认领，
要么拒绝。

### 5.2 第三方内容

所有读到的 `AbstractModel`（卡、Power、遗物、药水、球、怪物、附魔、苦难）先检查类型程序集
必须是 `sts2`；否则以 `third-party-content` 拒绝。`RejectRunListenerDeckCards` 对玩家牌组做
同样的检查，并拒绝带局外监听 Hook 的非惰性牌组卡。

## 6. 离线对照模式

`tests/OfflineHost` 的 `--import-compare` 为每个根建原生状态、导入，再与
`d1/<root>/golden.br` 的期望 `BATL` 逐字节比较：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --root kg2-b-stomp --import-compare
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --all --jobs 8 --import-compare
```

`--rules-compare` 编译 `Rules`，用 `RulesCodec.Encode` 编码后与该根的冻结规则对象逐字节比较；
不一致时打印 `RulesDiff` 的第一处表/项及前 16 行差异：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --root kg2-b-stomp --rules-compare
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --all --jobs 8 --rules-compare
```

`--import-compare --compiled-rules` 改用 `RulesCompiler` 的结果导入，而不是解码冻结对象：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --all --jobs 8 --import-compare --compiled-rules
```

每根只产生三种结果：`same`、`diff`、`reject`。`--all` 只对照 397 个根（410 个输入去掉 13 个
带 `flashPrefixActions`/`flashPrefixPlan` 的 fixture），汇总 `same/diff/reject`，并分别按首个
差异字段/表、拒绝原因分组计数。`--rules-table-check` 在初始化游戏静态状态后校验静态表：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll --rules-table-check
```

### 6.1 回合准备的三个普通根

`qf2-gradient-failure`、`ts-b-toolbox`、`ts-a-toasty` 是回合准备 fixture。它们各自的
`golden.br` 有 4/3/4 个根：1 个普通根 + 8 个额外准备根（不在本批范围）。普通根是旧桥用
**第一个准备选项**回放得到的根；离线宿主用 `FirstChoiceCardSelector` 记录原生第一个选项，
对照模式据此选择 golden 中的对应根，而不是固定取 `Roots[0]`：

| 根 | 普通根下标 | 其余准备根 |
| --- | ---: | ---: |
| `qf2-gradient-failure` | 1 / 4 | 3 |
| `ts-b-toolbox` | 0 / 3 | 2 |
| `ts-a-toasty` | 1 / 4 | 3 |

这 3 个普通根的普通原生状态是在测试宿主注入初始状态**之前**捕获的；对照模式因此跳过
`InjectInitialStateAsync`。额外 8 个准备根与 13 个前缀根都留给 M2a-5。

## 7. 当前结果与限制

`--all --jobs 8 --rules-compare` 当前：

```text
rules-objects distinct=376 roots=397
rules same=397 diff=0 reject=0
```

397 个在范围根覆盖 376 个不同冻结规则对象；410 个输入合计 388 个对象，余下 12 个只被前缀根
使用。`--rules-table-check` 当前：

```text
RULES-TABLES cards=603 powers=283 native_cards=603 native_powers=283 \
  missing_cards=0 extra_cards=0 missing_powers=0 extra_powers=0 registry_missing=0
```

`--all --jobs 8 --import-compare --compiled-rules` 当前：

```text
import same=397 diff=0 reject=0
```

`--all --jobs 8 --config-compare` 当前：

```text
config same=397 diff=0 reject=0
```

394 个普通根整份请求逐字节相同；3 个回合准备 fixture 的普通根 `ScoreRules`/`Bound` 子结构
相同（完整请求留给 M2a-5）。397 个根的 `scoreHash`/`boundHash` 都与 D1 一致。

3 类负例由 `--import-negative` 验证：

```text
NEGATIVE case=private-read status=rejected reason=missing-private-field
NEGATIVE case=unclaimed-private-state status=rejected reason=unclaimed-private-state
NEGATIVE case=third-party-content status=rejected reason=third-party-content
```

未完成/限制：

- 13 个前缀根与 8 个额外回合准备根不在本批；需要旧引擎的回放语义，留给 M2a-5。
- 回合准备普通根用 `CardModel.DeckVersion == null` 判断“准备期间生成的卡”，因为本批没有
  捕获 Start 时刻的卡对象集合；M2a-5 应改为传入真实的根卡集合。
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
- 未接入实机；只在离线宿主上验证。
