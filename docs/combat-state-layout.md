# Battle 状态布局

[返回 Sts2Solver 入口](README.md)

文中的“现有引擎”指导出冻结快照时 CombatSolver 的原引擎（`a62f1850c8586e5951e222eecb0dd34231e76d8a`）；这些语义已冻结在 `tests/Golden/data`，以后以快照和实机证据为准。

`Battle` 是一场战斗在某个稳定边界上的全部分支可变状态。它是 `unmanaged` 结构体：没有对象引用，普通的按值赋值仍是深拷贝；搜索池只保存“头部 + 前 `CardCount` 张牌”的打包前缀，这份前缀才是状态身份。下标 ≥ `CardCount` 的牌格内容无定义，任何代码都不得读取；其它内联数组的未用槽位仍必须为零。

## 1. 归属原则

- **进入 `Battle`**：会随分支变化、并影响后续合法动作或结算的值。范围以 AGENTS.md 第 5 节的状态清单和旧引擎状态键（`BuildStateKey`、`SimulatedCombatState.AppendFingerprint`）为下限；旧状态键漏掉但影响未来结算的值（例如回合数、Power 的 `AmountOnTurnStart`）同样进入。
- **进入 `Rules`**：根导入时冻结、整场搜索不变的值。包括卡牌各升级等级的基础数值与费用、怪物行动的静态参数与编译后的状态机、可生成怪物的闭包条目、遭遇槽名、根监听前缀顺序、最大能量基础值、手牌上限、药水槽数、根药水槽（用于区分生成药水与根药水）、随机药水池（战斗内与战斗外）、卡牌洗牌排序键、角色基础球槽数（`BaseOrbSlotCount`）。`Rules` 是不可变引用类型，所有分支共享。
- **都不进入**：纯 UI、日志、显示名、派生启发式值。历史日志只保留会影响结算的计数器。

## 2. 结构

容量是编译期常量，取值如下，以测量结果调整。所有数组都是 `[InlineArray]`。

字段顺序为 `Round … OrbArray Orbs` 沿用旧布局，之后是 `HistoryCourseCard`，最后是 `Cards`；`HeaderSize = 3860` 是 `Cards` 的偏移。完整字段列表见 `Battle.cs`。下表按职责分组，不严格等于内存顺序。

| 区块 | 内容 | 容量 |
| --- | --- | --- |
| Header | 回合数、轮次、当前方、玩家阶段、终局结果、`ShufflesCrossed`、下一个 CombatId、标志位（玩家 Hook 激活、回合结束请求、正在结束）、小数卡牌变量表（4 个 `decimal`，`DecimalVarCount` 为已用项数） | — |
| Player | `Creature`（HP、MaxHP、格挡、标志）、能量、星能、金币、手牌上限 | 1 |
| Osty | `Creature` 与存在标志 | 1 |
| Enemies | `Enemy`：`Creature`、怪物 Id、规则条目下标、CombatId、遭遇槽下标、死亡阶段、当前状态机节点、最近行动日志、私有 AI 寄存器、跳过行动/强制行动标志 | 12 |
| Powers | `Power`：Power Id、持有者、施加者、数量、`AmountOnTurnStart`、两个辅助计数、标志（跳过下次衰减、刚由敌人施加等） | 64 |
| Relics | `Relic`：遗物 Id、计数、辅助值、标志（已熔化、本场已触发等） | 32 |
| Potions | 各槽药水 Id 与标志 | 5 |
| Orbs | `Orb`：球 Id、`Aux`（Dark 的 `_evokeVal` / Glass 的 `_passiveVal`）；`OrbCount`、`OrbCapacity` | 10 |
| HistoryCourseCard | 上一次攻击的完整 `Card` 值；`HistoryCourseCardPresent` 标志 | 1 |
| Cards | `Card`，五个牌堆连续存放：手牌、抽牌堆、弃牌堆、消耗堆、打出区；`Cards` 是最后一个字段，下标 ≥ `CardCount` 的内容无定义 | 255 |
| Counters | 按 `Counter` 编号的玩家整数计数器：本回合计数（打出、开始打出、攻击、技能、格挡牌、消耗、弃牌、攻击次数、花费能量；玩家回合开始的 `BeginSideTurn` 清零，敌方回合保持；`StarsGainedThisTurn` 是本回合获得的正星能，供 Radiate 的 CalculatedHits 读取，同样在玩家回合开始清零）与本场累计失血、回复量、玩家未格挡受伤事件数（TearAsunder）。充能球另有两个战斗长计数器：`LightningChannels`（Voltaic 的闪电充能数，导入原版历史）与 `OrbChannels`（预测历史中的充能次数，对应 1000 次安全上限）。亡灵契约师的 `OstyAttacksThisTurn`（Rattle；仅当根捕获时原生 `Player.Osty` 存在才在玩家回合开始清零）、`DoomAppliedThisTurn`（DeathsDoor；玩家回合开始清零）、`EtherealPlays`（BansheesCry/PullFromBelow；战斗长、导入根历史）与 `CardsGeneratedThisCombat`（Supermassive 的生成卡乘数；导入 `CountGeneratedCards`，每次 `CardGenerated` 加一，包含战斗结束时被拒绝加入的生成卡）。另有两个苦难归一化计数：`VitalSparkNormalizedAmount`（`_lastNormalizedVitalSparkAmount`，`int.MinValue` 表示 null）与 `SkillPlayedThisSideTurn`（`_skillsPlayedThisTurn`，本侧回合末清零）。生物的本回合失血标记在 `CreatureFlags.LostHpThisTurn`，换边或换轮清除；`DampenCasterMask` 是 MagiKnight 对玩家施加 DampenPower 的施法者位集合（按敌人 owner 下标一位一个）。药水用量与免费槽另有三个计数：`GeneratedPotionUseCount` 统计本场生成后又被使用的药水（生成 = 使用时的槽位药水不等于根药水；评分加入 `PotionUseCount`），`GeneratedPotionStrategicCost` 低 16 位打包显式用药的战略成本、高 16 位打包自动用药（Fairy in a Bottle）的成本，`EntropicFreePotionMask` 是 5 个药水槽的免费位（第 i 位对应槽 i；使用该槽时清除，免费药水成本为 0）。偷窃账本另有二个：`OutstandingStolenGold`（`_outstandingStolenGold`：`ThieveryPower` 的 Gold 账本加所有 `HeistPower` 层数）与 `OutstandingStolenCards`（`_outstandingStolenCards`：每个持有被偷卡的 `SwipePower` 一行）。成长计数有五个，每个打包两个 16 位来源（`GrowthRewards`），十个原版成长来源共用，恰好占满 48 槽而不改变布局 | 48 |
| Rng | 九条战斗 RNG：计数器加四个 64 位状态字 | 9 |

### 2.1 Card

`Card` 是卡牌的完整值，当前 92 字节；按对象账本复用已有的 `Private` 字段，不增加卡牌尺寸。字段集以旧引擎卡牌状态键（`CaptureCardStateFingerprintForTesting`、`CardChoiceSupport.ChoiceCardKey`）为准：

- 卡牌 Id、升级等级、本地关键字位图；
- 费用：基础费用（含 X 费标志）、费用修饰栈（本场、本回合、直到打出、临时；容量 16，Stomp 每打出一张攻击追加一条 EndOfTurn 修饰）、星能费用；修饰数量使用独立字节，不再压进 `CostState` 的两位；临时星能费用保留当前条目与最多两个同成本旧条目（`TemporaryStarCostStack`：低 2 位旧条目数，之后每个旧条目 2 位清除标志），`CurrentStarCost` 读当前条目，回合末/打出后清理按清除标志逐条移除；不同成本的旧条目仍按容量拒绝。
- 状态标志：下次打出时消耗、本回合狡诈、本回合保留、是否牌组卡、已移出战斗；
- `BaseReplayCount`；
- 语义动态变量相对规则表基础值的增量（两格），以及少数卡牌的私有计数（Claw、Rampage、GeneticAlgorithm、Maul、MadScience、TheScythe 这类）；
- 附魔 Id、数量、状态，以及 Glam、Momentum 这类附魔私有字段；
- 苦难 Id 与数量；
- `IsClone`（原版 `CardModel.IsClone`）：占用 `CostState` 的 bit 3（bit 0–2 是当前临时星能费用标志，bit 4 是 `ReturnToHandNextTurn`，bit 5–7 是附魔状态）；旧临时星能条目在 `TemporaryStarCostStack`，不影响这几位；克隆卡跳过 `AfterCardEnteredCombat` 的卡牌专属部分（Stomp 等），由 `CloneCard` 置位，导入时从原版卡读取。
- `ReturnToHandNextTurn`（Bolas/ThrummingHatchet，`CostState` bit 4）：对应 `SimulatedCombatState._returnToHandNextTurn` 的成员关系；导入时按原牌对象投影，`BeforeHandDraw` 的返回手牌阶段清位。
- `DampenOriginalUpgrade`：卡牌被 MagiKnight 的 DampenPower 降级前的升级等级加一（0 表示未降级）。`DowngradeCard` 在把卡牌重置到 0 级动态变量、基础费用与本地关键字时写入；`RestoreDampenedCards` 在最后一个施法者死亡时按该值逐级恢复。

`CardFlags.PowerAfflictionChecked`（bit 7）对应 `PredictedCard.HasCheckedPowerAfflictionEntry`：根导入时置位，根之后生成的卡清零，第一次归一化后置位；`Keywords.CreatedAfterRoot`（bit 7）不是原生关键字，对应 `_liveCardsAtSnapshot` 之外的卡，只用于一次性苦难进场检查。卡牌尺寸为 92 字节。`TransientMark` 是一次动作内的对象身份标记，占用原有尾部填充字节：`BeatDown` 使用 1..63；64..255 是共享的动态分配区间，由栈上的 `Exec.TransientMarkBase` 分配互不重叠的嵌套区间（Sly/Stoke、`DecisionsDecisions` 的重复自动打出、Mayhem 的自动预打出队列）。嵌套批次丢弃同一张已带外层标记的卡时，内层在自动打出前把外层标记写回，再让卡值随自动打出移动；0 表示未标记，动作结束前清除，稳定边界恒为 0，克隆与生成卡从 0 开始。

这组内容没有新增计数器，Hook 位沿用 `HookMaskExt` 的 `1UL << 16`，`Card`/`Battle` 尺寸不变。`AmountOnTurnStart` 只对三个会被结算读取的 Power 保存（`DrawCardsNextTurnPower`、`SummonNextTurnPower`、`HelloWorldPower`），其余 Power 在导入与回合开始快照时保持 0；这是把原生模型里“镜像扣层尚未同步”的滞后值归一化，避免 Sts2Solver 立即扣层后与旧投影差异（依据见 [准入](https://github.com/rank-Yu/sts2-solver/blob/dev/full-run/docs/archive/admission-and-verification.md)）。

卡牌没有实例身份。牌堆里两张完全相同的打击字节相同，可以互换。路线和选择都按位置记录。

### 2.2 牌堆

五个牌堆共用一个 `Cards` 数组，按 `手牌 | 抽牌堆 | 弃牌堆 | 消耗堆 | 打出区` 顺序连续排列，各自记录数量。抽牌堆顶是抽牌堆段的第一个元素，与旧引擎 `SimCardPile` 的"索引 0 为顶"一致，所以抽一张顶牌入手只需移动两个计数。其余跨堆移动在 `[0, CardCount]` 内用段内平移完成。

`Cards` 的容量是 255（每个牌堆计数仍是 `byte`）。`Battle.HeaderSize = 3860` 是 `Cards` 的偏移，`HistoryCourseCard` 紧挨在它前面；`PackedSize = HeaderSize + CardCount * 92`，满牌时为 27,320 字节。搜索池按实际打包大小计费并按区块存放；普通结构赋值只用于根、回放、导入和测试等非热路径。

### 2.3 Power 顺序

`Powers` 按持有者分组，组的顺序就是 Hook 监听顺序里各生物的顺序；组内按获得顺序排列。新 Power 追加到持有者组末尾，对应旧引擎"插在持有者最后一个 Power 之后"的规则。数量归零的 Power 立即移除并压实，之后重新获得时排在组末，对应旧引擎退休根槽位的语义。实例型 Power 每个原生实例一行，同一 `(owner, id)` 的多行按获得顺序排列；Hook 与阶段遍历保存 occurrence 以定位到确切实例，而不是只按 `(owner, id)` 取第一行。

### 2.4 编码约定

- 药水槽、附魔、苦难存"内部 Id + 1"，0 表示空。卡牌、Power、遗物、怪物直接存内部 Id。
- `Card.CostState` 的 bit 3 是 `IsClone`、bit 4 是 `ReturnToHandNextTurn`；设置/清除临时星能费用和附魔状态时必须保留这两位。
- 敌人是否为主要敌人不存标志，与旧引擎一样由其持有的 Power 推出（`OwnerIsSecondaryEnemy`）。
- Osty 只在 `Osty` 区块与 `OstyPresent` 标志中表示（死亡后对象仍在，`Osty.CurrentHp <= 0` 即死亡）；`OstyWasRootPet`（`BattleFlags` bit 5）表示根捕获时原生 `Player.Osty` 非空，供 `BeginSideTurn(player)` 的宠物计数清零语义使用，整场不变。`Osty` 的 Power 与其他生物一样占 `Powers` 行，`Owner = Limits.OstyOwner`。
- `OblivionPower` 的“本张牌打出时的层数”只存在栈上的 `Exec.CardPlayPairs`（`CardPlayPairKind.OblivionDoom`）；稳定边界为空，不进入布局。
- `SandpitPower` 的原生实例型目标存在 `Power.Target`（`byte` owner，`NoOwner` 表示无目标）；每个原生 `ApplyTargeted` 实例一行。`Kernel.SetPowerAmountAt` 按行精确减层，避免同 (owner, id) 的实例互相覆盖。
- Knowledge Demon 的固定选项选择（两张诅咒卡）不进入牌堆：候选值只存在栈上的 `ChoiceRequest.GeneratedOptions`，选择后从父状态重执行。选择种类为 `ChoiceKind.SelectOption`，`ChoiceEffect.ApplyKnowledgeCurse`。
- 逃跑（`CreatureEscaped`）会从名单中移除该敌人；Sts2Solver 同步左移名单并递减后续 owner 的 `Power.Owner`/`Applier`/`Target` 下标，并移除该敌人的全部 Power。已知敌人数组在左移后把该怪物作为 `Escaped | PermanentlyDead` 墓碑追加到末尾（保留 `MonsterId`/`CombatId`/`Slot`/`Rules`），这样写回历史时仍能补上原生 `CreatureCmd.Add` 已经加入的怪物 Id；墓碑不进入名单，也没有 Power 引用它。被偷金币/卡牌计数不因逃跑变化。
- `Enemy.Rules` 是 `Rules` 怪物规则表的下标。根已知敌人按 `KnownEnemies` 下标占据表的前 `RootEnemyCount` 项，之后每个可生成怪物类型一项；已知敌人数组只追加，所以新敌人的规则编号由怪物 Id 查可生成表（`Rules.SpawnRulesId`）。
- `Enemy.Slot` 是遭遇槽下标（`Rules.EncounterSlots` 的顺序），没有槽名时为 255。名单（`Roster`）按槽下标稳定升序，255 视作 -1 排在有名字的槽位之前；名单顺序就是 Hook 监听与敌方行动顺序。
- `EnemyFlags.PerformedMoveThisRound`、`ForcedMustPerformAfterMove` 与 `ActingThisRound` 只在敌方回合内部使用，任何稳定边界上都为 0。
- `Enemy.ForcedMove` 是合成 STUNNED 或 REVIVE_MOVE 节点的动态后继（原版 `MoveState("STUNNED"/"REVIVE_MOVE").FollowUpStateId`），当前节点不是这两个合成节点时恒为 255。布局不因此改变。
- `Enemy.SteamPhase` 是 Waterfall Giant 的 `_steamEruptionPhases` 条目（0 none、1 about-to-blow、2 explode），占用原 `_padding1` 的第一个字节；其余怪物恒为 0。`_pressureGunBonus`/`_steamEruptionDamage` 作为该怪物的两个 AI 寄存器（`PressureGunBonus`/`SteamEruptionDamage`）投影。
- `Orb.Aux` 是球唯一的私有整数：DarkOrb 的 `_evokeVal`（初始 6，被动累加）或 GlassOrb 的 `_passiveVal`（初始 4，被动后递减）；Lightning/Frost/Plasma 恒为 0。导入时要求它是可表示的整数；未用槽位清零。球的顺序就是 `OrbQueue.Orbs` 的顺序，容量来自 `SimOrbQueue.Capacity`。
- `Battle.NextCombatId` 是旧引擎的 `_nextCreatureId`；生成敌人时先取当前值再递增。
- 怪物行动日志存"曾经用过的行动位集"加最近 8 条；原版重复规则只读这两部分。冷却或连续次数超出 8 的状态机在运行时抛 `UnsupportedException`。
- `Enemy.PoweredAttackHitsThisTurn` 只在玩家回合内累计、玩家回合开始清零，稳定边界上是普通计数；它不改变布局。
- 生成候选池、随机药水池与按原卡展开的变化池只存在 `Rules`（根冻结、非分支状态）。生成选项（Discovery/Splash）只存在栈上的 `ChoiceRequest.GeneratedOptions`，不进入 `Battle`；需要选择时整个动作从父状态重执行。
- 出牌期间的金额快照（Afterimage/Strangle，现有引擎的 `CardPlayPairPredictionState`）只存在栈上的 `Exec.CardPlayPairs`（容量 `Limits.CardPlayPairs`）；稳定边界为空，不改变布局。弃牌与 Sly 自动打出只移动既有牌值、使用既有 `CardsDiscardedThisTurn` 计数器，也不改变布局。

### 2.5 卡牌实例身份与实例型 Power

**小数卡牌变量。** 现有引擎的 `Thrash.OnPlay` 把 `ModifyDamage` 的 `decimal` 结果同时加进 `DynamicVars.Damage.BaseValue` 与私有 `ExtraDamage`；`AfterDowngraded` 再把 `ExtraDamage` 加回，克隆按模型字段复制。Sts2Solver 在头部 `DecimalVars` 保存“当前 BaseValue 减规则表规范值”的精确 `decimal`，卡上的 `Private` 存槽位加一（0 表示无偏移）。`DecimalVars.Assign` 只在值变化时移动槽位；值相同的卡共享槽位，更新一张不会改写另一张。稳定边界按“`DetachedCards` 顺序、再卡牌存储顺序”重建槽位并对相同位模式去重，所以字节表示与分配历史无关，导入投影与内核走同一函数。槽位不足抛 `CapacityException`；未声明 `UsesDecimalDamageVar` 的卡收到小数偏移仍是显式 `UnsupportedException`。Thrash 的伤害读取走同一个 `decimal`（`CardEffects.Var`），`Scorer` 也先按 `decimal` 相加再转 `double`，与现有 `CardChoiceSupport.CardValue` 的取整点一致。

现有引擎用 `CardModel`/`PredictedCard` 对象做键的账本和同一 `(owner, id)` 的多个 Power 实例，Sts2Solver 的表示如下。设计原则仍是“按值随卡移动，不维护全局引用表”：卡牌在 `Battle.Cards` 中搬动时整块值一起移动；新对象（生成、复制、变化）从零开始，对应现有引擎“新对象不在字典里”的语义。

**A 类：按卡牌对象记账的值。**

| 现有引擎 | Sts2Solver 表示 | 稳定边界语义 |
| --- | --- | --- |
| `MonologuePower.amountsForPlayedCards`（`PowerModel` 内部 `Dictionary<CardModel,int>`） | 不进入状态。`Exec` 的 `MonologuePending` 栈在每次出牌开始时快照当前所有正层 `(owner, occurrence, Strength)`，效果结束后逐条结算 | `BeforeCardPlayed` 登记、`AfterCardPlayed` 删除，只在一次出牌内部非空；现有引擎的 `CapturePendingMonologues` 也只在 `CardOnPlaySupport` 期间读取 |
| `_swordSageReplayBonuses`（`Dictionary<PredictedCard,int>`） | 每张 `Card.Private`：该牌已应用的 SwordSage 重放加成（`short.MinValue` = 尚未初始化） | 随卡值移动；`NormalizeSwordSageReplays` 按“当前 SwordSage 层数 − 已应用”调整 `ReplayCount` 并写回 |
| `SummonForth` 的 `MoveSovereignBladesToHand`（按 `AllCards` 对象取非手牌君王之刃） | 先按卡牌 Id 快照抽牌/弃牌/消耗/打出段里的非手牌君王之刃，再逐张 `MoveCard` 到手牌底部（满手改送弃牌堆） | 只依赖牌值，不需要身份；快照避免把刚送入弃牌堆的牌再次捡回 |
| `VambracePredictionState.TriggeringCard` | 不进入状态；`Exec` 保存触发卡所在出牌的 power-source serial 与相对位置 | `BlockGainedThisCombat` 持久化（`Relic.Counter`）；`TriggeringCard` 只在一次出牌内被读 |
| `_unsettlingLampTriggeringCards` / `_unsettlingLampInternalPowerTypes` | 不进入状态；`Exec` 保存触发卡的 source serial 与本次已翻倍的临时 Power 内部 Id 集合 | `CompleteCardPowerApplication` 时清除；锁存位（`RelicStatus`/`_isFinishedTriggering` 的引擎状态）持久化为 `Relic.Counter` |
| Fetch 的 `_fetchCardsPlayedThisTurn` | `Card.Private` 的 bit 0 | 随卡值移动；`RecordCardPlayed` 置位，玩家回合开始清零；生成的卡/克隆不清位复制 |
| `_nightmareSelections`（`Dictionary<NightmarePower,PredictedCard>`） | `Battle.DetachedCards` 的小数组：`Power.Aux0 = 槽位 + 1`，槽位存选定卡的完整 `Card` 值 | Power 移除时清空槽位；每个 `NightmarePower` 实例一行，按获得顺序 |
| `SwipePower.StolenCard`（被 THIEVERY 移除的卡） | `Battle.DetachedCards` 的同一小数组：`Power.Aux0 = 槽位 + 1`，槽位存被偷卡的完整 `Card` 值（含 `DeckCard`/`RemovedFromState`） | THIEVERY 从抽牌/弃牌堆移出时分配；击杀飞贼时保留槽位并清除 `RemovedFromState`（写回把牌组卡移到末尾）；逃跑时保留槽位和 `RemovedFromState`（写回按丢失移除）；仍被 `SwipePower` 引用的槽位是仍被扣住的卡。 |

**瞬态值不进入指纹。** `Exec.FirstRoomIsBoss`、`Exec.CombatStart` 与 `Exec.SetupContent` 只在
建局的开战 Hook/首回合内使用，分别让 Pantograph/BagOfPreparation 等只执行一次、并提供内容表
的遗物 `DynamicVars`；搜索的稳定边界不读它们。`Exec` 的
Monologue/Vambrace/UnsettlingLamp 数据在每次动作结束前排空，稳定边界的字节里没有它们。`Exec.TransientMarkBase` 同样只在动作内存在：`DiscardAndDraw`、`DecisionsDecisions` 的重复自动打出与 Mayhem 的自动预打出队列都用它从 64..255 分配身份字节；标记随卡值穿过抽牌、洗牌与牌堆移动，动作结束前清零（同一标记出现两次或标记丢失时抛显式未支持；确认已离开战斗牌堆时走 `AutoPlay` 的 no-pile 分支）；区间耗尽抛 `CapacityException`。`BeatDown` 的清理只覆盖 1..63，不会清除 Sly 批次；若 BeatDown 选中同一张卡，覆盖后的标记按显式未支持回落。`NightmarePower` 的选定卡是真正的跨回合值：它在下一次 `BeforeHandDraw` 要被克隆 `Amount` 次，因此进入 `DetachedCards`。

**移动、复制、生成、消耗、离场。**

- 移动（牌堆间、回手、洗牌）：整块 `Card` 复制，`Private` 随行。
- 复制/克隆（`CardModel.CreateClone`、`Clone`、`Duplicate`）：`Kernel.CloneCard` 把 `Private` 重置为未初始化并清 bit 0（Fetch 的“本回合这张牌打出过”不随克隆继承），对应现有引擎新对象不在字典里；`SwordSage` 的首次归一化对克隆使用当前层数，所以克隆不会重复加层。
- 生成（`CreateCard`）：`Private` 未初始化、Fetch 位为 0。首次 SwordSage 归一化对非克隆使用根层数（`Rules.RootSwordSageAmount`），与 `NormalizeSwordSageReplays` 的 `rootAmount` 基线一致。
- 消耗/离场（`Exhaust`、`RemoveFromCombat`、战斗结束）：卡值从 `Cards` 移除，相关账本随卡消失；`_swordSageReplayBonuses`/`_nightmareSelections` 中指向已离场对象的残留条目不会被任何结算读取，按惰性放过。Nightmare Power 自身被移除时清空其 detached 槽位，避免下一次获得读到旧卡。
- 变化（`Transform`、`ReplaceCard`）：新卡按生成语义创建，不继承旧卡账本。
- 复制整场战斗（Fork）：`Battle` 按值复制，`DetachedCards` 一并复制；`Exec` 的瞬态栈不跨越 Fork（稳定边界为空）。

**B 类：实例型 Power。** Power 表按 `(owner, id)` 允许重复行，每行一个原生实例，按获得顺序排列（见 [2.3](#23-power-顺序)）。实例私有值放 `Power` 的 `Aux0/Aux1`：

- `MonologuePower`：`Amount=1`，`Aux0=Strength`（应用时从卡变量写入），`Aux1=StrengthApplied`。
- `NightmarePower`：`Amount=3`，`Aux0=DetachedCards` 槽位 + 1。
- `ThieveryPower`：`Aux0=DynamicVars.Gold`（被偷金币账本，`SurprisePower` 死亡转移时读取）。
- `SwipePower`：`Amount=1`，`Aux0=DetachedCards` 槽位 + 1（被偷卡的完整值）。
- `HeistPower`：被偷金币就是 `Amount`，无需私有槽。
- `OrbitPower`：`Amount=每次触发的能量`，`Aux0=energySpent % 4`（现有引擎的 `_orbitEnergyRemainders`，`DisplayAmount = 4 - remainder`）。
- `AutomationPower`/`PanachePower`/`TheBombPower`：沿用 `Aux0/Aux1` 投影；`RollingBoulderPower` 只有 `Amount`。
- `PhantomBladesPower`/`PaleBlueDotPower` 不是实例型；`PaleBlueDotPower.Aux0` 保存每回合锁存位（0/1），每次玩家侧回合末清 0。

**导入与投影。** `StateImporter` 逐卡写入：

- SwordSage：`TargetLedger = _swordSageReplayBonuses[card]`（没有条目时按根层数/克隆当前层数推导；有正层 Power 却没有条目仍拒绝）。
- Fetch：`_fetchCardsPlayedThisTurn` 成员置位；位只对当前牌堆里的卡有意义，离场成员不投影。
- Orbit/PaleBlueDot/Nightmare：`ProjectPower` 读 `_orbitEnergyRemainders`、`_paleBlueDotActivated`、`_nightmareSelections`；条目缺失时对正层实例拒绝（现有引擎读取会抛异常）。退役实例（`Amount == 0`）的残留条目按惰性放过，依据与现有 `PowerAmountPredictionState` 相同：读取点只遍历正层有效 Power，重新获得是新的原生实例。
- Vambrace/UnsettlingLamp：`VambracePredictionState.BlockGainedThisCombat` 与 `StatefulRelicState` 的 lamp 锁存位投影为 `Relic.Counter`；`TriggeringCard`（serial+位置）/临时类型集合在稳定边界必须为空。
- Monologue：内部字典必须为空，否则按 `UnrepresentableState` 拒绝（与 `CardPlayPairPredictionState` 相同的稳定边界前提）。

**指纹与去重。** Sts2Solver 按打包前缀比较，指纹只分桶：

- 现有状态键用 `CardChoiceSupport.ChoiceCardKey` 和 `PredictedCard` 身份；SwordSage 的 per-card 已应用值不在键里，但“相同 `ReplayCount` + 不同已应用值”会对后续层数变化产生不同结果，Sts2Solver 把它放进卡值，因此会区分现有键会合并的这类状态；这是有意的正确性修正，不是扩大区分。
- Fetch 的现有键把已打出的 Fetch 按 `_registeredCombatCards` 下标规范化；Sts2Solver 用卡上的“该实例本回合打出过”位，等价于按实例区分而不按全局注册下标。两张未打出的同值 Fetch 仍字节相同可合并；打出其中一张后两张卡值不同，与“哪张实例打出过”的语义一致。
- Orbit/PaleBlueDot 的现有键是无序集合（owner + 剩余/锁存）；Sts2Solver 的 Power 行顺序是获得顺序，同一 `(owner,id)` 多实例的交换在 Sts2Solver 中会少合并。Nightmare 选定卡在现有键里按 `ChoiceCardKey` 归一化，Sts2Solver 用完整卡值（更保守）。这些都只会减少合并、不会把不同局面误合并。
- Monologue/Vambrace/UnsettlingLamp 的瞬态数据不进入字节，稳定边界与现有键一致；`AmountOnTurnStart` 等既有归一化规则不变。

**容量与回落。** `DetachedCards` 容量 3（Nightmare 的两个实例与被偷卡共用；同一场战斗里 ThievingHopper 只有一个且 THIEVERY 只发生一次，归还/丢失的卡值保留到写回，槽位不再在移除 `SwipePower` 时立即释放）；第 4 个并存卡片按 `CapacityException` 丢弃该子状态（不覆盖、不逐出），由搜索边界计数。`Exec.MonologuePending` 容量 8；更多同时等候结算的 Monologue 实例按同一异常回落。`Rules.HasIdentityContent` 为假（根牌组/Power/遗物与可生成内容都不涉及身份）时，整批身份扫描跳过；它只是优化前提，为真时仍按上述语义逐项结算。`Private` 是 `short`：SwordSage 已应用层数或 `ReplayCount` 增量超出 `short` 范围时按 `CapacityException`；`ReplayCount` 本身仍按 `byte` 容量（现有 `Card` 字段）。Fetch 只用 bit 0，其余位保持 0。所有退役条目只按惰性放行，不写回、不清理活实例。

### 2.6 瞬态标志与规则字段

这组值没有改变 `Battle`/`Card` 的尺寸，也没有新增 `Counter`：

- `PowerFlags.PendingAmountState`（bit 3）是**瞬态**标志：某 Power 的 native `PowerAmountPredictionState` 在本动作内被读取（目前是 GigantificationPower 的 `BeforeAttack`）。玩家死亡清理时带此标志的行保留，`Kernel.Step` 在稳定边界统一清除；稳定边界的字节里没有它。
- `HellraiserPower.Aux0` 保存 `infiniteAutoPlaysThisTurn`（状态条目优先，否则模型内部数据）；`RampartPass` 直接按 Power 行顺序派发，不随 owner 是否在名单内过滤。
- `Card.DampenOriginalUpgrade` 仍是卡牌被 Dampen 前的升级等级加一：克隆、变化和替换（`CloneCard`/`TransformCards`/`ReplaceCard`）清零；Rampage/TheScythe 的 `PreservesVar0OnDowngrade` 在降级后恢复 `Var0`，其余累计值仍按既有 `AfterDowngraded`/`Private` 表示。
- `MonsterRules.InitialMoveLogged` 是规则表字段（非战斗状态）：状态机构造器已把初始 `MoveState` 记入 `StateLog` 时，敌方回合内的召唤物在生成时立即记录该行动。

### 2.7 事件内容追加的卡牌值与动态类型

这组内容没有改变 `Battle`/`Card` 的尺寸（`Card` 92 字节、`Battle` 27,320 字节），也没有新增 `Counter`：

- **`Card.Private` 的新用途。** `MAD_SCIENCE` 把原版两个保存属性打进 `Private`：低字节是 `CardModel.Type`（`TinkerTimeType`，1/2/3），高字节是 `TinkerTime.RiderEffect`（0–9）。原版 `CreateClone` 复制这两个保存属性，所以克隆也复制 `Private`；生成路径不会产生该卡。`MAUL` 的 `_extraDamageFromMaulPlays` 同时写进 `Var0`（Damage 变量偏移）与 `Private`，`AfterDowngraded` 用 `Private` 恢复 `Var0`；克隆复制该值，与原版复制卡模型字段一致。两处都只在 `Card` 已有字段内保存，`Private` 的 `short` 溢出按 `CapacityException` 拒绝。
- **动态卡牌类型。** `MAD_SCIENCE` 的 `CardModel.Type` 是 `TinkerTimeType` 的覆盖；规则表仍按原生卡牌 Id 保存规范类型（生成池与指纹用它），结算与评估读取 `CardDef.ResolveCardType(battle, rules, card)`。`CardStats.HasDynamicType` 是该调用的根级开关：没有动态类型卡的根只多一次布尔判断。`TargetType` 同样按卡值覆盖（Attack 为 `AnyEnemy`，其余 `Self`）。
- **实例型 ToricToughnessPower。** 每回合数一行，`Aux0` 是该实例在格挡清零时提供的格挡量（原版 `SetBlock` 的 `DynamicVars.Block`，投影要求非负整数，否则 `UnrepresentableState`）。同一 `(owner,id)` 的两个实例可以在同一次 `AfterBlockCleared` 中先后移除，牌表下标会移动，所以卡与 Power 定义都声明 `IsIdentityContent`，让派发走按 occurrence 跟踪移除的路径；稳定边界仍只有 `Aux0`/`Amount`。
- **只存在于战斗外的 Power 与成长收益。** `FORBIDDEN_GRIMOIRE_POWER` 与 `IMPROVEMENT_POWER` 的原版唯一钩子是 `AfterCombatEnd`，现有引擎的战斗模拟不派发它；Sts2Solver 只保留计数行，卡牌效果另行记录 `LongTermResourceValue`（与原版 `RecordLongTermResource(50)` 一致）。两张卡同时按现有引擎的时机写 ST 的成长计数器：`FORBIDDEN_GRIMOIRE` 在施加 Power 后 `GrowthRewards.Add(ForbiddenGrimoire)`；`MAD_SCIENCE` 的 Improvement 骑手只在 `ImprovementPower` 数量实际增加时 `GrowthRewards.Add(MadScience)`。两个 Power 条目本身不会被任何战斗结算读取，因此无需投影私有状态。

### 2.8 结局与跨回合卡值槽

跨回合状态只增加了一个卡值槽，没有新增 `Counter`，`Card` 尺寸不变：

- **`Outcome.EventDefeat`（追加枚举值 3）。** 战损假人的 `BATTLEWORN_DUMMY_TIME_LIMIT_POWER` 最后一层（Amount 1）不递减，直接置 `Outcome = EventDefeat`；`RunEnemyTurn` 在 `TriggerEnemySideTurnEndEffects` 之后立即返回，跳过敌人死亡、毒、苦难归一化与下一轮准备，与 `CombatBeamSolver.Expansion.Replay` 的 `SearchBoundaryReason.EventDefeat` 早退一致。Amount 2 以上仍只递减。Sts2Solver 的评分在 EventDefeat 上不做威胁投影（`ThreatProjection(currentHp, 0, 0)`，与现有评估的 boundary 分支一致），终局键追加 `PolicyBoundaryRank(EventDefeat)=4` 的末位比较项。现有引擎不执行 `CreatureCmd.Escape`（在返回前就停了），Sts2Solver 也不移除假人。
- **`Battle.HistoryCourseCard` + `BattleFlags.HistoryCourseCardPresent`（bit 6）。** 一个完整 `Card` 值，放在 `Cards` 之前（头部末尾）；`Battle` 当前为 27,320 字节。它表示 `SimulatedCombatState._lastAttackThisTurn`：每次完成一张**非 dupe** 攻击牌时，从出牌堆的实时值写入；`AutoPrePlay` 开始时先捕获并清空它（现有引擎的两本字典在此时分离），Mayhem 之后按每个未熔化 `HISTORY_COURSE` 复制一份（`CloneCard` + 去掉 Exhaust 关键字 + 清 `ReturnToHandNextTurn`，`IsClone=true`）并自动打出。dupe 通过栈上的 `Exec.CurrentPlayIsHistoryCourseDupe` 传播：结果去向是移出战斗（`PileType.None`）、不写回本回合最后攻击，且 Feral/Rebound/ChainsOfBinding 等按 `IsDupe` 跳过的 Hook 在 Sts2Solver 里同样跳过。跨回合槽的投影只取这一本字典，不取退役的上一回合字典——所有受支持根都在 `Play` 阶段，AutoPrePlay 已经消费过它。`Rules.HasHistoryCourse` 为假时记录与 AutoPrePlay 都跳过。
- **Pen Nib 的锁存身份。** `Relic.Aux` 保存第十次攻击的 `Exec.PlayIdentity`（不是布尔 1），`ModifyDamageMultiplicative` 只在当前 `PlayIdentity` 与它相等时加倍；`AfterCardPlayed` 只在 `LastPlayIdentity` 相等时清除。这样弃牌选择中嵌套自动打出的 Sly 牌（另一次 CardPlay）不会继承加倍。`Aux` 是瞬态；`Rules.PenNibRelicIndex` 让 `Step` 在每个稳定边界用一次下标写入清掉因玩家死亡等未完成出牌留下的值，与投影的 0 一致。
- **小数变量规范化顺序**变为“`DetachedCards` → HistoryCourse 跨回合槽 → 卡牌存储顺序”；跨回合槽只在 `HistoryCourseCardPresent` 时参与。

### 2.9 药水与遗物使用的既有槽位

这组内容没有改变 `Battle`（27,320 字节）或 `Card`（92 字节）的尺寸，也没有新增 `Counter`。新增内容使用既有槽位：

- `Exec` 的瞬态 `PlayIdentity`/`LastPlayIdentity`/`PlayIdentityCounter`/`MusicBoxPlayIdentity`：每次原生 `CardPlay` 一个单调编号，供 `PAELS_LEGION` 把 `AfterModifyingBlockAmount` 与同一次 `AfterCardPlayed` 配对、`MUSIC_BOX` 把 `BeforeCardPlayed` 与 `AfterCardPlayed` 配对。嵌套自动打出保存/恢复，稳定边界恒为 0/初值。
- `Relic.Counter`/`Aux` 的新用途：`MUSIC_BOX.Counter` 本回合已用；`THROWING_AXE.Counter` 本场已用；`METRONOME.Counter` 已充能次数；`PAELS_LEGION.Counter` 冷却、`Aux` 本次格挡所属 `PlayIdentity`、`RelicFlags.UsedThisCombat` 为原版的显示锁存；`EMOTION_CHIP`/`PAELS_EYE` 的 `StatefulRelicState`（Current/Previous）。
- `Rules` 三个根级字段（非战斗状态）：`HasPaelsLegion`（`GainBlock` 是否需要收集 changed-listener 列表；Vambrace 之外的新增格挡修正）、`FurCoatSpawnHpOne`（命中的生成敌人当前生命 1）、`RootLostHpMask`（根捕获时每个 owner 的 `HasLostHpThisTurn`，额外回合恢复用）。三者都不改变 `Battle` 大小。
- `PAELS_EYE` 的额外回合：`Round` 不变、`PlayerTurn` 加一；普通回合换轮时清零失血标记，额外回合按 `RootLostHpMask` 恢复根历史答案。

### 2.10 回合准备根

回合准备根不改变 `Battle`/`Card` 布局，也不新增 `Counter`。根捕获时是 `Player/Start`：原生 `SetupPlayerTurn` 尚未作答；Bridge 用现有引擎的 `BuildTurnSetupRoots` 按同一选择预算把每个准备分支回放到稳定 `Play` 状态，只把该 `Play` 状态导入为 `Battle`。准备选择向量（`PlanCardChoice` 列表、来源/效果/牌堆/时机/卡牌令牌）与根动作数（固定前缀长度）只存在于 `TurnSetupRootCandidate`/`BeamOptions.InitialActionCount`；它们不进入状态字节、指纹或续用文本。搜索根之间的差异全部已经在导入后的 `Battle` 里（手牌、弃牌堆、Power、能量、HP 等）。

建局侧的开战选择复用同一套 `ChoiceRequest`：`ChoiceKind.SelectOption` 携带 `GeneratedOptions`（生成卡按值），`SelectCards` 携带牌堆绝对位置；Tab 里的 `MinCount`/`MaxCount` 支持范围（Toolbox 0..1、GamblingChip 0..手牌数、ChoicesParadox/ToastyMittens 1..1）。待定请求只活在 `CombatSetupChoice`/`RunStepResult.CombatChoice` 里，不进入 `Battle`；建局从进点前 `RunState` 重放，不保存部分 `Battle`。

`Exec.FirstRoomIsElite`/`FirstRoomIsBoss` 只用于建局或 `RunCombatSetup` 的瞬时上下文，不进入 `Battle`（BoomingConch/Pantograph）。所有模拟回合按全局自动选择器的不暂停顺序建模：`SetupPlayerTurn` 的 `AfterPlayerTurnStart` 完整跑完后，才跑 `AfterSideTurnStart` 与回合开始球；手动界面/`localOnly` 选择器会暂停并先跑侧回合开始，这是已知差异。停在选择上的冻结/导入根按根状态延续，不重跑侧回合。`Exec` 没有延迟字段，搜索的稳定边界不读这些瞬时值。

物化时选中的选择向量作为 `rootTurnSetupChoices` 固定回现有求解器；现有求解器从同一根重新枚举准备根，只保留该向量再回放固定前缀，结果的 `TurnSetupChoices`/`TurnSetupPlayState` 与关闭 Sts2Solver 时完全同形。若某个准备选择把状态推出 Sts2Solver 容量或内容闭包，按准入/边界原因整次作废，On 模式显式失败，不部分提交。

### 2.11 出牌期瞬态与回手

这些是 0.111 现有引擎用 `CardPlay`/`CombatPredictionHistory` 表达的按次出牌历史，Sts2Solver 只在 `Exec` 上保留当次动作的瞬时值，稳定边界不进入 `Battle`：

- **伤害合计。** `Damage` 在结果 Hook 之前把本次每个结果的 `BlockedDamage + UnblockedDamage + OverkillDamage` 累加到 `CardDamageTotal`（该次出牌的全部命中），并记录最近一次 `Damage` 的 `LastDamageTotal = BlockedDamage + UnblockedDamage` 与 `LastDamageOverkill`。对应现有引擎 `CombatPredictionDamageReceivedEntry.Result.TotalDamage + OverkillDamage`。每次出牌开始时清零。
- **Fisticuffs**：镜像攻击后先结算敌人死亡，再按 `CardDamageTotal` 获得格挡（`CorePowerSupport` 的 Fisticuffs case）。
- **Omnislice**：第一击后结算死亡，再把 `LastDamageTotal + LastDamageOverkill` 以 `Unpowered | Move` 打给目标以外的可命中敌人（`CardOnPlaySupport.Batch042.ApplyOmnislice`）。
- **EchoingSlash 与多波攻击。** `Kernel.CardAttackRepeating` 对应现有引擎在一个 `AttackContext` 内的循环：每波对当前全部存活敌人造成一次伤害，随后用**当前名单**跑死亡结算，`WasTargetKilled` 的个数追加为下一波；`BeforeAttack`/`AfterAttack` 各只派发一次。Omnislice 与 EchoingSlash 的死亡结算都只遍历当前名单（`combat.Enemies`），因此击杀后离场的 Infested 尸体不会在复制伤害前生成 Wriggler。每波开始前从 `Exec.PlayPosition` 重新同步当前打出的牌并读取伤害变量（现有引擎每波读 `base.DynamicVars.Damage`）：Dampen 的最后一位施法者在本波死亡时恢复卡牌升级等级，后续波次必须按恢复后的伤害结算，不能沿用出牌开始时的快照。
- **HandOfGreed**：镜像攻击后检查本次出牌是否击杀目标且目标所有 Power 都允许致命（`ShouldOwnerDeathTriggerFatal`），成功则加金币与 `LongTermResourceValue`。金币修正遗物（BowlerHat/Ectoplasm/DragonFruit）只影响战斗外金币，Sts2Solver 没有金币状态；三者已有空/能量定义，不改变战斗内结算。
- **GoldAxe**：`FinishedCardPlays` 计数器导入根历史的 `GetFinishedPlaysBeforePrediction` 并在每次完成出牌时 +1，作为 `CalculatedVarSpecRegistry` 的乘数。
- **回手。** Bolas/ThrummingHatchet 打出后把 `Card.ReturnToHandNextTurn`（`CostState` bit 4）置位；玩家下回合 `BeforeHandDraw` 的返回手牌阶段按当前牌堆顺序把这些牌移回手牌并清位，对应 `SimulatedCombatState._returnToHandNextTurn` 与 `PrepareBeforeHandDraw`。根导入把 `_returnToHandNextTurn` 的成员投影到对应卡值。
- **出牌期金额快照。** Afterimage 与 Strangle 在 `BeforeCardPlayed` 记下能力层数、在 `AfterCardPlayed` 消费（现有引擎 `CardPlayPairPredictionState`，以 `CardPlay` 对象为键）。Sts2Solver 把它放在栈上的 `Exec.CardPlayPairs`（容量 `Limits.CardPlayPairs`），嵌套自动打出的条目压在外层之上并先被消费；稳定边界为空，不进入 `Battle`。

## 3. 规范化

同一局面必须只有一种字节表示，否则去重失效；不同局面绝不能字节相同。

- `Enemies`、`Powers`、`Relics`、`Orbs`、`DetachedCards`、`DecimalVars` 的未使用槽位与填充字节全部为零；牌堆移除元素时只在前 `CardCount` 张内前移，不要求清空旧尾格。`Cards` 下标 ≥ `CardCount` 的内容无定义，不参与规范化、比较、编码或任何读取。
- 牌堆保持旧引擎里的真实顺序，不排序。旧引擎在部分牌堆上忽略顺序（`PileOrderInvariantMask`），Sts2Solver 首版不做这种合并：只会少合并，不会错误合并。
- 布尔值只用 0 和 1。
- `BattleFlags.HasRisk` 是旧引擎快照的未补偿风险（`SimulationSnapshot.HasRisk`），只在根导入时
  置位并保持到战斗结束；Discovery/Splash 的 `UnresolvedPlayerChoice` 被补偿目录标为已补偿，内核不置位。
  它不改变结构体大小。
- 结构体布局用 `StructLayout(LayoutKind.Sequential)`，并有一个检查断言 `Unsafe.SizeOf<Battle>()` 与预期一致，防止字段改动悄悄引入填充。

## 4. 指纹与相等

- 指纹是 64 位哈希，只覆盖打包前缀中各区块已使用的部分（头部、前 `EnemyCount` 个敌人、前 `PowerCount` 个 Power、前 `CardCount` 张牌等），四路并行混合。`HistoryCourseCard` 虽然移到了头部，仍在最后混入，因此同一状态的指纹值与改动前完全相同。
- 相等比较打包前缀（`Fingerprint.Bytes`），`Fingerprint.Equal` 是唯一入口。搜索去重在指纹命中后必须再比较打包前缀，不允许仅凭哈希合并；后缀垃圾字节不参与比较。
- Sts2Solver 的去重只在 Sts2Solver 内部使用，不写入旧引擎的 `StateFingerprint` 或 `ContinuationStamp`。

## 5. 容量与失败

- 任何写入超过容量都抛 `CapacityException`，携带区块名与容量。搜索展开中的这类异常是**子状态边界**：`BeamSolver` 只丢弃抛出异常的那一个子状态（选择请求的每个选项分支各自独立），按区块名计数进 `BeamResult.CapacityDroppedChildren`，其余子状态照常提交与去重；不截断、不覆盖、不逐出。内核不支持的语义（`UnsupportedException`）仍让整次 Sts2Solver 求解作废；On 模式写 `SEARCH_FAILED` 并停止部署。
- 两个可增长池（`next`/`ended`）仍按旧状态几何规划座位数，`min(width × (MaxLegalActions + 1), 16,512)` 是各侧数组的上限；实际存储改为按区块字节计费的打包状态池，默认 `PoolStateBudgetBytes = 200 MiB` 是单池的真实内存账本上限。座位上限不随牌数变化，所以牌数 ≤96 的搜索边界与旧版逐项相同；牌多的状态占更多字节，真正先撞到的是内存预算。两者分别对应现有的 `CapacityException("Beam.Pool")` 与 `SearchMemoryBudgetException` 路径。回合结束层（`ended`）达到座位上限时仍按与回合边界相同的保留规则压缩；内存预算不足则按 `memory` 停止并保留已找到的路线。语义见 [搜索](combat-ai.md) 的“回合结束层压缩”。
- 根状态本身超过容量（根导入或根评分在展开循环之前抛出）仍是导入拒绝：整次 Sts2Solver 求解作废，On 模式写 `SEARCH_FAILED reason=kernel-capacity` 并停止部署，不部分提交。
- 准入阶段按牌组规模和可生成内容估计上限，明显超出容量的根直接不准入，避免搜索中途失败。
- 数值钳制与旧引擎一致（HP、格挡、能量、Power 数量钳制在 ±999,999,999），由内核统一实现。

## 6. 尺寸预算

`Enemy` 为 64 字节：`Rules` 占用原 `_padding` 的第一个字节，`PoweredAttackHitsThisTurn`（BeatIntoShape 的每目标本回合强化攻击命中数，0..255，溢出显式拒绝）占用第二个字节，`SteamPhase` 占用第三个字节（原 `_padding1` 的第一个字节），最后一个字节继续填充。`Orb` 为 8 字节（`Id` + `Aux`）。`Card` 为 92 字节（费用修饰容量 16，`DampenOriginalUpgrade`，`TransientMark` 复用尾部填充；实例身份复用 `Private` 账本）。`Battle` 为 27,320 字节，其中头部 3,860 字节、满牌 255 × 92 = 23,460 字节；最大打包大小与结构体大小相同。搜索池只复制打包前缀，典型 23 张牌的状态约 5.9 KiB。`docs/combat-ai.md` 记录池存储与账本。

Power 数量变化队列不进入 `Battle`：它只存在于栈上的 `Exec.PendingPowerChanges`（容量 64），并在每个稳定边界前由 `ResolvePowerAmountChanges` 排空；排空先把当前批次复制到局部 `drained` 再清空队列，嵌套排空不会消费外层批次。旧引擎同样要求 Fork 前 `_pendingPowerAmountChanges` 为空。伤害来源直接放在栈上的 `DamageResult`。搜索容器不属于状态布局。`BeamSolver` 的四个池使用 512 KiB 有效载荷 + 一个完整 `Battle` 尾部余量的字节区块（551,608 B/块），按需租用并计入内存账本；元数据块按类型 ≥ 85 KB。块大小取舍：小于 85 KB 的数组会在 Gen0/Gen1 反复晋升，单块过大又难以在 LOH 中安置。当前每次 `Solve` 新建池。
