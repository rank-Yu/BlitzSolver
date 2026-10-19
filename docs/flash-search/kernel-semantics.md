# Flash 内核语义

[返回 Flash 入口](README.md)

本文规定 Flash 内核的执行模型。**规范来源是现有引擎的实际行为**：下文的步骤顺序摘自现有引擎源码，引用路径相对仓库根。两者冲突时以现有引擎和差分测试结果为准，并回头修正本文。

路径缩写：`S/` = `src/Engine/InCombat/Simulation/`，`M/` = `src/Engine/InCombat/Mirrors/`，`E/` = `src/Engine/InCombat/Extensions/`，`Q/` = `src/Search/`，`P/` = `src/Prediction/`。

## 1. 执行上下文

内核入口是静态方法，签名形如 `FlashStepResult Step(ref FlashBattle battle, in FlashAction action, FlashRules rules)`。执行期间的临时信息放在栈上的 `FlashExec`（`ref struct`，持有 `ref FlashBattle`、`FlashRules`、选择游标、当前出牌上下文），不进入状态。

- 动作执行到稳定边界才返回，返回值是 `Completed`、`NeedsChoice`（附选项规格）或 `Terminal`。
- 执行中途不允许分配托管对象；内容代码只读写 `FlashBattle` 和栈上值。
- 未知内容 Id、未实现的 Hook 组合抛 `FlashUnsupportedException`；容量溢出抛 `FlashCapacityException`。两者都使本次 Flash 求解整体作废。

## 2. 内容定义

每类内容一个抽象基类：`FlashCardDef`、`FlashPowerDef`、`FlashRelicDef`、`FlashPotionDef`、`FlashMonsterDef`、`FlashOrbDef`、`FlashEnchantmentDef`、`FlashAfflictionDef`。具体内容是 `sealed` 子类，覆写需要的 Hook 虚方法。

- 每个定义用特性声明原版 Id，例如 `[FlashContent("BASH")]`。注册表在首次使用时扫描定义，分配连续的 `ushort` 内部 Id，并按"覆写了哪些 Hook"建立每个 Hook 的监听位图，派发时跳过非监听者。`FlashHookMask` 的 64 位已用满，此后新增的 Hook 放在 `FlashHookMaskExt`，两者用同一反射计算（`FlashContentDef.Hooks`/`HooksExt`），`FlashHooks.CollectListeners` 对两种掩码各有一个重载，收集顺序相同。
- 定义对象不可变、无字段状态；所有可变值存在 `FlashBattle` 里。
- 数值从 `FlashRules` 读取：`rules.Card(id, upgrade).Damage`、`rules.Monster(slot).Move(moveId).Hits` 等。只有原版数据里不存在、写在原版代码中的常量才允许出现在定义里，并注释原版出处。
- 原版 Id 到内部 Id 的映射在 Bridge 建立；内核只把原版 Id 当元数据字符串，不引用原版类型。

## 3. Hook 派发

### 3.1 监听顺序

监听列表按以下顺序遍历（来源 `Q/SimulatedCombatState.cs:1903-2064`）：

1. 根前缀：根捕获时原生 `IterateHookListeners` 的顺序，去掉卡牌、苦难、附魔和球；只保留仍在场的生物与药水。玩家侧新生物插在敌人块之前；敌人块（先 Power，后怪物本身）按当前槽位重新分组；新药水追加在前缀末尾。根前缀的分组顺序存在 `FlashRules`。
2. 球：每个玩家的球，按队列顺序。
3. 卡牌：手牌、抽牌堆、弃牌堆、消耗堆、打出区，按堆内顺序；每张卡之后紧跟它的苦难，再跟它的附魔。

Power 顺序见 [状态布局](state-layout.md#23-power-顺序)。

### 3.2 派发规则

- 按列表顺序逐个调用；遇到待处理选择时停止（与 `M/HookMirrors.cs:1439` 一致）。
- 多阶段 Hook 依次跑 Early、普通、Late。`AfterCardPlayed` 的 Late 阶段重新读取监听列表。
- 修正类 Hook（`ModifyDamage*`、`ModifyHpLost*`、`ShouldClearBlock` 等）不得改写状态：预报与评估会在只读语义下调用它们。需要随攻击捕获的值在 `BeforeAttack` 中记录（例如活力，对应 `VigorPowerMirrors.BeforeAttack`）。
- 战斗正在结束时，战斗监听者的 Hook 返回空，`AfterCardPlayed`、`AfterBlockBroken`、`AfterAttack` 除外；伤害和死亡类的跑局监听者不受此限（`M/HookMirrors.cs:1351`）。`TryModifyEnergyCostInCombat`（普通与 Late）、`ModifyCardPlayResultLocation`、`ModifyCardPlayCount`、`ModifyXValue` 也通过 `IterateCombatHookListeners` 遍历，因此 Flash 在 `IsOverOrEnding` 时跳过它们；费用查询和评估在结束状态读到的是未修正费用。
- 费用 Hook 是纯查询：普通阶段按战斗监听顺序跑一遍，再按同一顺序跑 Late 阶段；调用方最后 `(int)` 截断并钳制到 ≥ 0（`M/HookMirrors.cs:486-530`）。`AreCardsFree` 分支在 Flash 中没有内容。
- `AfterPowerAmountChanged` 不在施加 Power 时立即派发：`ApplyWithBeforeApplied` 只把 `(PowerId, Delta, Applier)` 放进栈上的待处理队列，之后在现有引擎的同一位置调用 `ResolvePowerAmountChanges` 排空（卡牌补偿结束、`CompleteCardPlayEffects`、回合开始 `BeforeHandDraw` 后、`TriggerSideTurnStart` 末、玩家/敌方回合结束 `TriggerRegular` 后、敌人行动结束、敌人死亡效果末、药水使用后、Dominate 的 Vulnerable 与 Strength 之间）。排空时先把当前批次复制到栈上的 `drained` 并清空队列，再逐条派发；嵌套排空（Vicious 抽到 Strike 后 Hellraiser 自动打出）因此只能看到处理器新追加的变化，与现有引擎 `DrainPowerAmountChanges` 的先整批取走一致。派发只遍历当前 Power（现有引擎 `EffectivePowers()`），按监听顺序对每条变化调用；Vicious 在“Delta > 0 且变化的是 Vulnerable 且施加者是持有者”时抽牌。旧引擎**没有**通用 `AfterApplied`；只有 PhantomBlades/Juggling/Feral 等个别初始化，Flash 在对应定义里另行处理。
- 现有引擎的 `ModifyPowerAmountForRelics` 在 Artifact 检查之前依次跑 `BeforePowerAmountChanged`、`ModifyPowerAmountGiven`（SneckoSkull）与 `TryModifyPowerAmountReceived`（RuinedHelmet）。Flash 实现接收侧的 `TryModifyPowerAmountReceived` 纯查询与 `AfterModifyingPowerAmountReceived`；修正阶段不得改写状态，RuinedHelmet 的“本场已用”标记在 After 阶段写入。
- 卡牌从战斗外进入战斗时，`SimulatedCombatState.CardEventHistory.AfterCardEnteredCombat` 先遍历卡牌拥有者的遗物（GhostSeed），再执行卡牌自身的 `AfterEnteredCombat`（Stomp、Pinpoint 等费用反应）。Flash 的 `FlashKernel.Generation` 的进入战斗路径按同一顺序派发。
- 只有原版监听者时，旧引擎对回合阶段 Hook、Power 反应、最大能量与抽牌数、`BeforeHandDraw` 使用"先全部 Power、再全部遗物"的批量顺序，不按逐监听者顺序。Flash 在相同位置用相同的批量顺序。这一偏差以旧引擎为准。

### 3.3 Hook 目录

内核为下列 Hook 提供派发点，名称与原版一致；内容需要新的 Hook 时在内核增加派发点并补差分测试：

- 出牌：`TryModifyEnergyCostInCombat`（含 Late；Corruption、FreeAttackPower、FreeSkillPower、FreePowerPower）、`TryModifyStarCost`（BrilliantScarf、VoidFormPower；纯查询，只在原费用非负且战斗未结束时派发）、`AfterStarsSpent`（先遗物后 Power 的固定顺序）、`AfterStarsGained`、`ShouldPlay`、`BeforeCardPlayed`、`ModifyCardPlayCount` / `AfterModifyingCardPlayCount`（OneTwoPunchPower、DuplicationPower）、`ModifyCardPlayResultLocation` / `AfterModifyingCardPlayResultLocation`（Corruption；Nostalgia 还返回 `Draw` 的 `Top` 位置）、`AfterCardPlayed`（含 Late）、`AfterEnergySpent`、`AfterStarsSpent`、`AfterCardExhausted`、`AfterCardDiscarded`、`AfterHandEmptied`。
- 卡牌目标：`FlashCardDef.TargetType` 覆写原生 `CardModel.TargetType`。规则表冻结规范目标；`Shiv` 在持有 `FanOfKnivesPower` 时返回 `AllEnemies`，合法动作枚举、自动打出目标解析与卡牌攻击都读这个动态值。
- 攻击与伤害：`BeforeAttack`、`ModifyAttackHitCount`、`AfterAttack`、`ModifyDamageAdditive`、`ModifyDamageMultiplicative`、`ModifyDamageCap`、`BeforeDamageReceived`、`ModifyHpLostBeforeOsty`（含 Late）、`ModifyHpLostAfterOsty`（含 Late）、`AfterModifyingHpLostAfterOsty`、`AfterBlockBroken`、`AfterCurrentHpChanged`、`AfterDamageGiven`、`AfterDamageReceived`（含 Late）。
- 格挡：`BeforeBlockGained`、`ModifyBlockAdditive`、`ModifyBlockMultiplicative`、`AfterModifyingBlockAmount`、`AfterBlockGained`、`ShouldClearBlock`、`AfterBlockCleared`。
- 死亡：`BeforeDeath`、`ShouldDie`（含 Late）、`AfterDeath`、`AfterPreventingDeath`。
- 已有派发点（其余见本节列表）：`BeforeDamageReceived`（`TryDamageTarget` 中修正后、格挡前，不受战斗结束闸门限制）、`BeforeSideTurnEnd` 三轮（玩家回合结束第一阶段，受闸门限制，参与者以位集合传入）、`ShouldClearBlock`、`ModifyMaxEnergy`、`ModifyHandDraw`（含 Late）与第 7 节的 Power/遗物阶段方法。
- 回合：`BeforeSideTurnStart`、`AfterSideTurnStart`、`AfterEnergyReset`（含 Late）、`ModifyMaxEnergy`、`BeforeHandDraw`、`ModifyHandDraw`（含 Late）、`AfterPlayerTurnStart`、`AfterAutoPostPlayPhaseEntered`（阶段切到 AutoPostPlay 后、切到 End 之前，卡牌与 Power 监听者；HowlFromBeyond、StampedePower）、`BeforeSideTurnEnd`（VeryEarly、Early、普通）、`OnTurnEndInHand`、`AfterSideTurnEnd`（含 Late）、`ShouldTakeExtraTurn`。
- 抽牌与牌堆：`ShouldDraw`、`AfterCardDrawn`（含 Early，以及被抽卡自身的 Hook；HellraiserPower 在 Early 自动打出刚抽到的 Strike）、`ModifyShuffleOrder`、`AfterShuffle`（TheAbacus）、`AfterCardChangedPiles`。`AfterShuffle` 在洗牌计数 +1 后、战斗未结束时派发，监听者为全部战斗监听者。
- Power：`TryModifyPowerAmountReceived` / `AfterModifyingPowerAmountReceived`（RuinedHelmet；UnsettlingLamp 与 SneckoSkull 仍因对象身份/临时 Power 类型表与 Poison 定义缺失而跳过）、`AfterPowerAmountChanged`（Shroud、SleightOfFlesh、Vicious；Flash 目前只有 Vicious、PossessStrength/PossessSpeed 定义；派发携带被修改 Power 的目标 owner，因为原版读取的是 `power.Owner`）。**实例型 Power**（`PowerInstanceType.Instanced`）每个原生实例占 Power 表一行，`(owner, id)` 相同的行按出现序区分；阶段遍历与 Hook 快照都保存 occurrence，`FindPowerOccurrence` 精确回指该行（Automation、Panache、RollingBoulder、TheBomb、Gigantification）。
- 数值与遗物：`ModifyXValue`（ChemicalX，位置与 `CardModel.ResolveEnergyXValue` 一致）、`ShouldPlayerResetEnergy`（IceCream）、遗物 `AfterCardEnteredCombat`（GhostSeed）。
- 生成卡与 A3 新增时机：`AfterCardGeneratedForCombat` 在 `AddGeneratedCard` 插入牌堆并派发 `AfterCardEnteredCombat` 之后按监听顺序派发，参数是**卡牌数组的绝对下标**（牌堆内下标只用于 `AfterCardEnteredCombat`）与生成者 owner（玩家或 `NoOwner`，对应现有引擎的 `Player? Creator`；怪物及其 Power 生成的牌为 `NoOwner`）；Aeonglass 用它把新生成的 Wither 直接提升到当前 `WitherUpgradeCount`。`Rampart` 只在玩家侧非额外回合的回合开始、`OwnerAfterSideTurnStart` 之后执行（`PersistentPowerSupport.TriggerRampart`）。`HasKeyword` 在稳定边界上查询本地关键字后，还会为持有 `HexPower` 的玩家的 Hexed 卡补上 Ethereal（原版 `TryModifyKeywordsInCombat` 是惰性查询，不写入卡牌状态）。
- 球：`AfterOrbChanneled`、`AfterOrbEvoked`、`ModifyOrbPassiveTriggerCounts`、`ModifyOrbValue`；被动与激发、回合末/回合开始触发见第 3.5 节。

### 3.4 苦难与附魔

卡牌上的苦难与附魔是 `FlashCard` 的值（`AfflictionId/Amount`、`EnchantmentId/Amount/Status/Private`），与现有引擎的 `PredictedCard.Preview.Affliction/Enchantment` 同形；导入时只接受已登记的定义，其余按 `UnsupportedAffliction`/`UnsupportedEnchantment` 拒绝。定义类是 `FlashAfflictionDef`（`IsStackable`、`CanAfflictCardType`、`CanAfflictUnplayableCards`、`OnPlay`）与 `FlashEnchantmentDef`（`EnchantDamageAdditive/Multiplicative`、`EnchantBlockAdditive/Multiplicative`、`EnchantPlayCount`、`OnPlay`，以及通用的 `AfterCardPlayed` 等监听 Hook）。

- **修正类 Hook。** 现有引擎在 `HookMirrors.ModifyDamage`/`ModifyBlock` 中先调用来源卡的附魔修正，再跑普通监听者；Flash 在 `FlashHooks.ModifyDamage/ModifyBlock` 的同一位次调用附魔定义。`ModifyDamage` 的附魔修正只用卡值，是纯查询。`ModifyBlock` 的溢出口径与普通修正相同。
- **出牌次数。** `CardModel.GetEnchantedReplayCount` 先应用附魔的 `EnchantPlayCount`，再进入 `ModifyCardPlayCount`；Flash 的 `EnchantedReplayCount` 同时供出牌管线、评估的 `ReplayPotentialValue` 与 `FlashActionTranslator` 使用。
- **卡牌尾部。** 每次打出在卡牌效果与 `OwnChoice` 之后、`AfterCardPlayed` 之前依次执行附魔 `OnPlay`、苦难 `OnPlay`；持有者死亡或出现待处理选择时中止，与 `CardTailContinuation` 的 Enchantment → Affliction → Finish 顺序一致。
- **苦难归一化。** 现有引擎不直接逐 Hook 施加/清除苦难，而是在 `SimulatedCombatState.NormalizeCardAfflictions` 里按当前 Power 归一化：Hex/Tangled/Ringing 先按 Power 数量施加或清除；随后 `NormalizePowerAfflictions` 处理 Vital Spark 的 Tainted（数量变化才回写、Power 归零才清除）、Smoggy 的 Smog（本侧回合已打出技能时给新进场技能）与 Galvanic 的 Galvanized（新进场 Power 牌）。Flash 的 `FlashKernel.NormalizeCardAfflictions` 逐条照抄，并在现有引擎的同一位置调用：`AfterCardEnteredCombat` 末、卡牌补偿效果末、每个动作结算、玩家回合末阶段一/阶段二、玩家回合开始末、每个敌人行动末。`PowerAfflictionChecked` 对应 `PredictedCard.HasCheckedPowerAfflictionEntry`：根导入的卡视为已检查，根之后生成的卡在下一次归一化时检查一次。`SkillPlayedThisSideTurn` 对应 `_skillsPlayedThisTurn`，在技能牌完成后置位、在该侧回合末清零（与持久化的 `SkillCardsPlayedThisTurn` 不同）。
- **施加/移除路径。** `ApplyAffliction` 对应 `CardCmd.Afflict` 的 `CanAfflict` 与同类型叠加；`ClearAffliction(s)` 对应 `ClearAffliction`。Vital Spark 的 Tainted 层数同步使用导入的 `LastNormalizedVitalSparkAmount` 计数器（`int.MinValue` 表示 null），首次导入不回写，保留实机战斗开始时可能叠高的层数。
- **Power 与怪物。** 本轮实现 Plow、Ringing、Tangled、Smoggy、Vital Spark、Tainted、Hex 七个 Power，以及 Ceremonial Beast、Vine Shambler、Infested Prism、Living Fog、Gas Bomb 的移动效果。Living Fog 的 BLOAT 在攻击前生成 Gas Bomb（`FlashMonsterDef.BeforeMove`），Gas Bomb 的 EXPLODE 攻击后自毁。MysticLighter 是遗物定义：带附魔卡牌的 powered attack 加 `Damage`。Hexed 的 Ethereal 由上面的 `HasKeyword` 惰性提供；HexPower 被移除或施加者死亡后不再提供，卡牌本地关键字不变。


### 3.5 充能球

来源：`S/CombatPredictionSimulator.Orb.cs`、`S/SimOrbQueue.cs`、`M/Orbs/**`、`M/Hooks/Orb/**`、`P/OrbLifecycleSupport.cs`、`P/EnchantmentLifecycleSupport.TriggerAfterTurnStartOrbs` 与回合阶段中的球调用位置。

**状态与规则。** 球按 `SimOrbQueue.Orbs` 的顺序存在 `FlashBattle.Orbs`，`OrbCount`/`OrbCapacity` 与旧引擎同义。`FlashOrb.Aux` 保存原版球唯一的私有十进制值：Dark 的 `_evokeVal`（初始 6）与 Glass 的 `_passiveVal`（初始 4）；其他球不使用。导入时容量超过 10、球数超过容量、无 Flash 定义的球、或私有值不是 `int` 能表示的整数，都按 `FlashImportException` 拒绝。`BaseOrbSlotCount`（角色基础槽数）进入 `FlashRules`；`LightningChannels` 与 `OrbChannels` 是两个追加计数器，前者导入 `CalculatedVarSpecRegistry.CountLightningChannels`（原版历史 + 预测历史），后者只统计预测历史中的充能事件，对应旧引擎的 1000 次充能安全上限。

**生成（channel）。** `FlashKernel.ChannelOrb` 对应 `CombatPredictionSimulator.OrbChannel`：结束中/有待处理选择时不做；基础槽数为 0 且容量为 0 时先加 1 槽（`OrbCmd.AddSlots`）；容量已满时先 `EvokeNext` 最左侧球，再入队。若激发副作用同步充能又把槽填满，旧引擎记录 `MethodMirrorIncomplete` 风险并放弃；Flash 抛 `FlashUnsupportedException`（K0 合同覆盖满槽先激发），不自行追加激发。容量仍为 0 时返回 false；成功入队后记录 `OrbChannels`，闪电球另记 `LightningChannels`，再派发 `AfterOrbChanneled`。预测历史达到 1000 次充能时显式拒绝。`AddOrbSlots` 按 `min(10 - capacity, amount)` 增长；`RemoveOrbSlots` 对应 `SimOrbQueue.RemoveCapacity`，从队尾删球且不触发任何球 Hook。

**激发（evoke）。** `EvokeNext` 对应 `OrbEvokeNext`/`OrbCmd.EvokeNext`：取最左侧球，按 `repeat` 重复，只有最后一次出队（Dualcast、MultiCast、Shatter 的 `dequeue:false` 语义）；每次激发后执行 `ApplyEnemyDeathPowers`。`EvokeOrb` 对应 `OrbEvoke`：先按需要出队，再调用球的 `Evoke`，把返回值写入目标 owner 列表后派发 `AfterOrbEvoked`；结束中或有待处理选择时不执行。`MultiCast` 等 X 次激发由卡牌读取 `ResolveEnergyXValue` 后传入。`Darkness`/`TeslaCoil`/`Loop` 的额外被动走 `OrbPassiveAt`（不查询触发次数 Hook）。

**被动（passive）。** `TriggerOrbPassive` 对应 `OrbModel.TriggerPassive`：先以纯查询 `ModifyOrbPassiveTriggerCounts` 计算次数（`GoldPlatedCables` 对首个球 +1；`AfterModifyingOrbPassiveTriggerCount` 只有展示监听者，Flash 不派发），再逐次 `Passive`；每次被动后执行 `ApplyEnemyDeathPowers`。`OrbPassiveAt` 对应 `OrbCmd.Passive`，跳过触发次数 Hook。两者都在结束中/待选择时不执行。`ModifyOrbValue` 是纯查询，按战斗监听顺序调用（`FocusPower` 增加集中并钳制 ≥ 0；集中只修正需要修正的球值），修正类 Hook 不写状态。

**回合位置。** 玩家回合末：`BeforeSideTurnEnd` 与胜负判定之后、虚无/回合末在手牌效果之前，按队列快照逐个球调用 `BeforeTurnEndOrbTrigger`（Plasma 的该触发为空）。每次触发与被动后的敌人死亡效果立即结算；战斗结束中后续触发无效果。玩家回合开始：在敌人死亡效果与苦难归一化之后、自动预打出之前，按队列快照调用 `AfterTurnStartOrbTrigger`；只有 Plasma 重写它并触发一次被动（仍经 `TriggerOrbPassive`，所以金缆的触发次数修正生效）。玩家死亡清空球队列时容量一并归零（`OrbQueue.Clear`）。

**球值。** Lightning 被动 3、激发 8；Frost 被动 2 并给所有者格挡、激发 5；Dark 被动把 `Modify(6)` 累加进 `Aux`，激发对当前生命最低的可命中敌人造成 **原始 `Aux`**（原版 `EvokeVal` 不再过 `ModifyOrbValue`，集中只影响累积）；Glass 被动取 `Modify(Aux)`，大于 0 时造成伤害并把 `Aux` 减 1，激发为 `Modify(Aux) * 2`，伤害所有可命中敌人；Plasma 被动 +1 能量、激发 +2 能量。Lightning 与 Glass 的随机目标取 `CombatTargets.NextItem` 的一次 `NextInt(0, count)`，候选为空时不消耗；Lightning 的显式目标（TeslaCoil）不消耗该流。Chaos 与 TrashToTreasure 的随机球取 `CombatOrbGeneration`，候选顺序是原版 `_validOrbs`：Lightning、Frost、Dark、Plasma、Glass。

**遗物。** CrackedCore 在第一回合玩家侧 `BeforeSideTurnStart` 充能闪电；RunicCapacitor 与 SymbioticVirus 在第一回合玩家侧 `AfterSideTurnStart` 增加 3 槽 / 充能暗球；GoldPlatedCables 实现触发次数 Hook；PowerCell 与旧引擎的 `RegisterIgnored<PowerCell>` 一致，是准入所需的空定义。ReptileTrinket 在 `AfterPotionUsed` 中按遗物变量施加临时力量（`ReptileTrinketPower`），对应 `TriggerRelicsAfterPotionUsed`。UnsettlingLamp 仍无定义：它需要卡牌实例身份、`BeforePowerAmountChanged` 与 `ModifyPowerAmountGivenMultiplicative` 的逐卡翻倍账本。

### 3.6 储君：星能与锻造

来源：`S/CombatPredictionSimulator.Energy.cs`、`S/CombatPredictionSimulator.Card.cs`、`E/CombatPredictedCardExtensions.cs`、`P/PowerLifecycleSupport.cs`、`M/Hooks/Resources/AfterStarsGainedMirrors.cs`、`M/Hooks/Card/ModifyStarCostMirrors.cs`、`P/PersistentPowerSupport.Forge`、`Q/SimulatedCombatState.CardLifecycle.cs`、`Q/SimulatedCombatState.AutoPlay.cs`。

**状态。** 星能是 `FlashBattle.Stars`（`SimPlayerCombatState.Stars`），导入直接取 `playerState.Stars`。`BaseStarCost` 来自原版同名字段（无星费为 -1），临时星费取 `_temporaryStarCosts` 最后一条（多条目不一致按容量拒绝）。`LastStarsSpent` 是出牌期瞬时值，存栈上的 `FlashExec.StarValue`，不进入稳定状态。

**费用。** `StarCostWithModifiers` 对应 `GetStarCostWithModifiers`：X 星费卡返回当前全部星能；否则基础/临时星费只在非负时交给 `TryModifyStarCost`（BrilliantScarf、VoidFormPower），最后 `(int)` 截断并钳制 ≥ 0；X 星费不派发费用 Hook。`ShouldPayExcessEnergyCostWithStars` 没有原版监听者，Flash 不做能量超额折算。`ResolveStarXValue` 读 `StarValue` 再过 `ModifyXValue`（Stardust）。

**获得。** `FlashKernel.GainStars` 对应 `PlayerCmd.GainStars`/`CombatPredictionSimulator.GainStars`：`IsEnding` 时不获得；`ShouldGainStars` 没有原版监听者；状态钳制到 [0, 999999999]；正数获得同时累加 `StarsGainedThisTurn`（Radiate 的 CalculatedHits 乘数，玩家回合开始清零）；随后派发 `AfterStarsGained`（BlackHolePower）。卡牌在现有引擎的同一位置调用它（Venerate/HiddenCache/Convergence 在 `CardOnPlaySupport` 位置，SolarStrike/Glow/ShiningStrike 在 `CardEffectSpecRegistry` 位置，BigBang 按 Energy → Stars → Forge）。

**消耗。** `ManualPlay` 在卡进入打出区之前重算星费，写入 `StarValue`，扣星能后派发 `AfterStarsSpent`。现有引擎的顺序不是监听者顺序：先 `TriggerRelicsAfterStarsSpent`（GalacticDust、MiniRegent 按遗物表顺序），再按 `EffectivePowers()` 顺序跑 `ChildOfTheStarsPower`；Flash 因此用「先遗物、后 Power」两遍派发。自动打出不支付星能，但同样在 `skipXCapture` 为假时捕获 `StarValue`。

**锻造。** `FlashKernel.Forge` 对应 `PersistentPowerSupport.Forge`：有待处理选择时不执行；手牌/抽牌堆/弃牌堆/打出区都没有未消耗的 `SOVEREIGN_BLADE` 时创建一张规范 0 级君王之刃到手中（走 `AddGeneratedCardToCombat` 的进战斗与生成卡 Hook）；然后给**所有**牌堆里的非复制君王之刃的伤害变量加 amount（`Var0`）。`CreatedThroughForge` 只驱动原版视觉，不进入 Flash 状态。君王之刃的攻击次数来自 `Repeat` 变量（恒为 1，无原版效果调用 `SetRepeats`），`SeekingEdgePower` 把它改为全体目标，`ParryPower` 提供格挡。锻造卡与 `FurnacePower` 在 `GeneratedCards` 里声明君王之刃，规则闭包因此冻结它的两级数值。

**AutoPrePlay。** 玩家回合开始、`AfterTurnStartOrbs` 之后：`Phase = AutoPrePlay`；先按消耗堆顺序自动打出阶段开始时在消耗堆的 `BOMBARDMENT`（每次打出后回到消耗堆，捕获数量与当前数量必须一致，否则显式拒绝）；再按 `MayhemPower` 层数把抽牌堆顶牌移到打出区后依次自动打出（对应 `MoveCardsForAutoPlay` 与 `TriggerScheduledAutoPlays`）。Imbued、HistoryCourse、WhisperingEarring 没有 Flash 定义，持有它们的根在准入拒绝。完成后 `Phase = Play` 并做胜负判定。

**遗物与 Power。** GalacticDust/MiniRegent 的计数取 `StatefulRelicState.Current`，Regalite 取 `RegalitePredictionState.UsedThisTurn`，BrilliantScarf 取 `CounterPredictionState`（非自动出牌递增、玩家侧回合开始清零，计数达到 `Cards-1` 时能量与星能费用都变 0）。ChildOfTheStarsPower 在 `AfterStarsSpent` 给格挡；GenesisPower/StarNextTurnPower 在 `AfterEnergyReset` 获得星能（后者随后移除）；ArsenalPower/PillarOfCreationPower 在 `AfterCardGeneratedForCombat` 给力量/格挡；FurnacePower 在 `OwnerAfterSideTurnStart` 锻造；ConquerorPower 让君王之刃对持有者造成双倍伤害并在侧回合末减层；ReflectPower 反弹被格挡的 powered attack 伤害并在侧回合开始减层；DyingStarPower 与 MonarchsGazeStrengthDownPower 是负向临时力量，在持有者侧回合末归还。

## 4. 出牌管线

来源：`Q/SimulatedCombatState.cs:1552`、`S/CombatPredictionSimulator.Card.cs:122-330`、`S/CombatPredictionSimulator.CardExecutionContinuation.cs:29-127`、`S/CombatPredictionSimulator.CardTailContinuation.cs:16-125`、`P/CorePowerSupport.cs:21-407`。

1. **可否打出**：Enthralled 锁 → Unplayable 关键字 → 能量费用 → 星能费用 → `ShouldPlay` → `CardIsPlayable` 镜像 → Smog 苦难。能量费用 = 基础费用 → 本地修饰 → `TryModifyEnergyCostInCombat` 普通阶段 → Late 阶段（战斗结束时两阶段都跳过）；钳制 ≥ 0。费用 Hook 接收卡牌当前牌堆，供 FreeAttackPower 判断手牌/打出区；评估侧读取本地修饰（`CostModifiers.Local`/`All`）而不读取费用 Hook。X 费等于当前全部能量。`ShouldPayExcessEnergyCostWithStars` 为真时超额能量按 2:1 用星能支付。
2. **支付**：重算两种费用和超额换算；记录 X 值与本次星能消耗；扣能量 → `AfterEnergySpent`；扣星能 → `AfterStarsSpent`。
3. **进入打出区**：卡从手牌移到打出区（不触发 `AfterCardChangedPiles`）；确定基础去向（复制品或能力牌 → 无；下次打出时消耗或带消耗关键字 → 消耗堆；其余 → 弃牌堆；个别卡牌覆写）；`ModifyCardPlayResultLocation`，随后只对改变去向的监听者派发 `AfterModifyingCardPlayResultLocation`（Corruption 把 Skill 改成消耗；改变“移出战斗”的基础去向显式拒绝）；打出次数 = 附魔重放 + 1 → `ModifyCardPlayCount` → `AfterModifyingCardPlayCount`（只对改变次数的监听者；OneTwoPunch/Duplication 在此递减）；持有者已死则结束。
4. **每次打出**：战斗已结束则停止；`BeforeCardPlayed`；历史计数；执行卡牌效果；结算卡牌自身选择。
5. **卡牌效果顺序**：先执行旧引擎镜像部分（推断配方或专门镜像，即攻击、格挡、抽牌），再执行补偿部分（`CardEffectSpecRegistry` 与 `CorePowerSupport.ApplyCardPowers` 里的 Power、资源、生成效果）。卡牌可以覆写 `ResultLocation` 来复刻 `CardResultLocationMirrors` 的 `GetResultLocationForCardPlay`（ShiningStrike 回抽牌堆顶、ParticleWall 回手牌）；基础去向仍由规则表类型与关键字决定。**补偿效果总在镜像效果之后**，这是旧引擎的实际顺序。现有引擎在 `CorePowerSupport.ApplyCardPowers` 里先执行 `CardOnPlaySupport.Apply`，随后立即 `ApplyEnemyDeathPowers`，最后才进入该卡的专属补偿 `switch`；因此“镜像攻击击杀会生成新敌人的怪，再把 Power 施加到当前敌人”的卡（Thunderclap）必须在专属补偿前结算死亡。Flash 的对应定义在镜像攻击与专属补偿之间调用 `ApplyEnemyDeathPowers`，`PlayFromPlay` 还在整张卡的 `OnPlay` 之后、`OwnChoice` 之前补一次死亡结算。
6. **收尾**：持有者已死则中止；附魔 OnPlay → 苦难 OnPlay；`AfterCardPlayed` 普通阶段 → 重新读取监听列表后跑 Late 阶段（战斗正在结束也照常触发）；`ResolvePowerAmountChanges`（`CompleteCardPlayEffects`）；生命周期计数。
7. **去向**：仍在打出区的卡按去向处理（无 → 移出战斗；消耗 → `AfterCardExhausted`；否则加入目标牌堆）；不在嵌套效果中时触发 `AfterHandEmptied`；清理费用与目标。
8. **动作边界**：结算敌人死亡效果（`ApplyEnemyDeathPowers`）→ 胜负判定 → 处理被请求的回合结束。

药水（`Q/CombatBeamSolver.Expansion.Replay.cs` 的 `UsePotion` 分支与 `P/PotionExecutionSupport.cs`）：清空槽位（使用记录只供搜索评分）→ `BeforePotionUsed` → 药水效果（`FlashPotionDef.OnUse`，对应 `PotionOnUseSupport.Use` 的分支；自身药水作用于玩家）→ 药水选择 → 玩家存活时 `AfterPotionUsed` → 敌人死亡效果；之后与出牌相同：洗牌计数、胜负判定、处理被请求的回合结束。药水的目标类型与变量（含伤害、格挡变量的 ValueProp）从规范药水编译进规则表；合法目标按 `TargetsForPotion`（任一敌人逐个可命中敌人；自身/任一玩家在玩家存活时无目标；全体敌人与无生物目标无目标）。

### 4.1 自动打出

来源：`S/CombatPredictionSimulator.AutoPlay.cs:21-73`、`S/CombatPredictionSimulator.AutoPlayContinuation.cs`。

`FlashKernel.AutoPlay` 与 `ManualPlay` 共用 `PlayFromPlay`（结果牌堆、每次打出、`BeforeCardPlayed`、效果、`OwnChoice`、`AfterCardPlayed` 普通与 Late、收尾），只替换资源与目标两步：

1. 战斗结束/正在结束或拥有者死亡时不打出。卡牌不在任何牌堆时先加入打出区（触发 `AfterEnteredCombat`）。
2. 不可打出关键字、`ShouldPlay` 拒绝或无法解析目标时，`MoveToResultPileWithoutPlaying`：先加入打出区，再按 `ExhaustOnNextPlay`/消耗关键字进入消耗堆，否则进入弃牌堆；不触发任何出牌 Hook、计数或费用清理。
3. 不支付能量/星能；`SpendResources(isAutoPlay: true, skipXCapture)` 只在 `!skipXCapture` 时把当前费用写入本次 X 值（X 费卡取当前能量），并记录星能值。`ManualCardsPlayedThisTurn` 不计自动打出。
4. 目标：`AnyEnemy` 用 `CombatTargets` 流从 `HittableEnemies`（名单顺序、排除复活/永久死亡）取随机目标，候选为空时不消耗流；单人没有其他队友，`AnyAlly` 只有调用方已给出目标才成立；其他目标类型直接通过。
5. `AutoPlayFromDrawPile` 先按现有引擎把最多 `count` 张抽牌堆顶牌移到打出区，再按顺序逐张自动打出；每张打出前写入 `ExhaustOnNextPlay = forceExhaust`（false 也写入）。打出的卡结果去向为消耗堆时清除该标志（`CardModel.GetResultLocationForCardPlay`），未打出而直接移入去向时保留（`MoveToResultPileWithoutPlaying`）。自动打出中战斗结束/正在结束时，现有引擎仍对**剩余队列的每张卡**写入该标志，然后 `AutoPlay` 立即返回；Flash 在队列仍连续位于 `start` 的前提下复刻这一写入并停止。`count` 超过 `FlashLimits.Cards` 时抛 `FlashCapacityException`。
6. 自动打出中的卡牌自身选择走现有嵌套选择协议：`OwnChoice` 请求选择、动作未携带时返回 `NeedsChoice`，调用方从父状态带选择重执行。生成选项（Discovery/Splash）的选择协议见 4.2。

### 4.2 卡池随机生成与变化

来源：`E/CombatCardGenerationExtensions.cs`、`S/CombatPredictionSimulator.AutoPlayContinuation.cs`、`P/CardOnPlaySupport.Batch042.cs`、`Q/CardChoiceResolution.cs`。

- **候选池在 Bridge 冻结。** 定义用 `FlashCardPools` 的键声明可能生成的池；`FlashRulesCompiler` 用现有引擎的同一枚举（`player.GetUnlocked...` + `FilterForCombatAndPlayerCount`，再按调用点的类型/费用/排除谓词过滤）把候选按原顺序写进 `FlashRules`。内核只按 RNG 取样，不重新过滤。
- **取样。** 去重（`TakeRandom`）先对整个候选列表做一次 Fisher-Yates（`n-1` 次 `Shuffle` 流 `NextInt(n+1)`），再取前 `count` 张；有放回（`NextItem`）每张消耗一次 `CombatCardGeneration` 流 `NextInt(0, n)`。候选为空时不消耗 RNG。生成的卡按 `CreateCard` 创建为 0 级规范卡，`UpgradeIf` 再升级一次。
- **加入战斗。** `AddGeneratedCard` 对应 `AddGeneratedCardsToCombat` 的单卡路径：手牌已满改投弃牌堆，按 `Bottom`/`Top`/`Random` 插入，调用卡的 `AfterEnteredCombat`。`SetToFreeThisTurn` 写入绝对 0 能量修饰（EndOfTurn|WhenPlayed）和 0 临时星能费用（回合末清除）。
- **生成选项选择。** Discovery/Splash 先记录生成的选项并写入风险（旧引擎的 `History.CardGenerationOptions` + `RecordRisk(UnresolvedPlayerChoice)`），随后 `GetSpec` 的 `GenerateToHand` 分支在 `OwnChoice` 提问。Flash 把选项值直接放进 `FlashChoiceRequest.GeneratedOptions`（不在任何牌堆），选择后按 `PredictedCard.Clone()` 的语义复制卡值（不是 `CreateClone`，`IsClone` 保持为假）、`SetToFreeThisTurn`、加入手牌；空选项也提一个空范围选择，与 `RangeSpec` 一致。
- **变化。** `ReplaceCard` 对应 `CardCmd.Transform` 的现有引擎结算：从原牌堆移除原卡（`RemoveFromCombat`），再把替换卡插回 `min(原下标, 当前长度)` 并调用 `AfterEnteredCombat`；不触发 `AfterCardChangedPiles`。Flash 没有实例身份，按位置定位；PrimalForce 对 snapshot 的手牌攻击按升序原位替换为 GiantRock（PrimalForce 升级时 GiantRock 升 1 级）。Entropy 的回合开始变化按 `TurnStartChoiceSupport.Resolve(Transform)` 选择手牌，再对每张走 `CombatPredictionSimulator.CreateRandomCardForTransform`：候选是原卡 `Pool`（Quest/Event/Ancient/Token 取无色池）中 `CanBeGeneratedInCombat`、非 Basic/Ancient/Event/Epoch 排除的卡，排除原卡 Id，用 `CombatCardSelection` 流 `NextItem` 抽一张 0 级规范卡；候选为空按 `FlashUnsupportedException` 拒绝。
- **随机药水。** 一次 `CombatPotionGeneration` `NextFloat` 选稀有度（Rare ≤ 0.1、Uncommon ≤ 0.35、否则 Common），再一次 `NextItem` 在冻结的同稀有度池中选；`TryProcurePotion` 放入第一个空槽，成功时记录 `LongTermResourceValue + 20`。
- **药水生成选择。** Attack/Skill/Power/ColorlessPotion 在 `OnUse` 里生成 3 张去重候选，再走 `PotionChoiceSupport.GetSpec` 的 `GenerateToHand` 0..1 选择。该选择不走 `PendingTurnStartChoice`：现有引擎把候选写进 `History.CardGenerationOptions`，`PotionExecutionSupport.Complete` 从 `PlanAction.Choice` 直接结算 `PotionChoiceSupport.Apply`。Flash 的翻译层先无选择回放一次读取候选，构造同序 `PlanCardChoice` 挂到动作上，再正式回放；选定后按 `PredictedCard.Clone()` 语义复制（`IsClone` 保持假）、`SetToFreeThisTurn`、加入手牌。
- **风险。** `FlashBattleFlags.HasRisk` 表示旧快照的**未补偿**风险（`snapshot.HasRisk`）；根导入从快照读取该位。Discovery/Splash 虽记录 `UnresolvedPlayerChoice`，但 `CardOnPlayCompensationCatalog` 把它们的 `OnPlay` 风险标为已补偿，内核不为它们置位。SeekerStrike 的 `BuildSeekerSpec` 不在补偿目录里，会记录风险，Flash 在该选择请求前同样置位（单调保持到战斗结束）。

### 4.3 出牌伤害历史与回手

这些是 0.111 现有引擎用 `CardPlay`/`CombatPredictionHistory` 表达的按次出牌历史，Flash 只在 `FlashExec` 上保留当次动作的瞬时值，稳定边界不进入 `FlashBattle`：

- **伤害合计。** `Damage` 在结果 Hook 之前把本次每个结果的 `BlockedDamage + UnblockedDamage + OverkillDamage` 累加到 `CardDamageTotal`（该次出牌的全部命中），并记录最近一次 `Damage` 的 `LastDamageTotal = BlockedDamage + UnblockedDamage` 与 `LastDamageOverkill`。对应现有引擎 `CombatPredictionDamageReceivedEntry.Result.TotalDamage + OverkillDamage`。每次出牌开始时清零。
- **Fisticuffs**：镜像攻击后先结算敌人死亡，再按 `CardDamageTotal` 获得格挡（`CorePowerSupport` 的 Fisticuffs case）。
- **Omnislice**：第一击后结算死亡，再把 `LastDamageTotal + LastDamageOverkill` 以 `Unpowered | Move` 打给目标以外的可命中敌人（`CardOnPlaySupport.Batch042.ApplyOmnislice`）。
- **HandOfGreed**：镜像攻击后检查本次出牌是否击杀目标且目标所有 Power 都允许致命（`ShouldOwnerDeathTriggerFatal`），成功则加金币与 `LongTermResourceValue`。金币修正遗物（BowlerHat/Ectoplasm/DragonFruit）尚未进入 Flash 定义，含有它们的根不在本批证据内。
- **GoldAxe**：`FinishedCardPlays` 计数器导入根历史的 `GetFinishedPlaysBeforePrediction` 并在每次完成出牌时 +1，作为 `CalculatedVarSpecRegistry` 的乘数。
- **回手。** Bolas/ThrummingHatchet 打出后把 `FlashCard.ReturnToHandNextTurn`（`CostState` bit 4）置位；玩家下回合 `BeforeHandDraw` 的返回手牌阶段按当前牌堆顺序把这些牌移回手牌并清位，对应 `SimulatedCombatState._returnToHandNextTurn` 与 `PrepareBeforeHandDraw`。根导入把 `_returnToHandNextTurn` 的成员投影到对应卡值。
- **出牌期金额快照。** Afterimage 与 Strangle 在 `BeforeCardPlayed` 记下能力层数、在 `AfterCardPlayed` 消费（现有引擎 `CardPlayPairPredictionState`，以 `CardPlay` 对象为键）。Flash 把它放在栈上的 `FlashExec.CardPlayPairs`（容量 `FlashLimits.CardPlayPairs`），嵌套自动打出的条目压在外层之上并先被消费；稳定边界为空，不进入 `FlashBattle`。

### 4.4 弃牌与 Sly

来源：`S/CombatPredictionSimulator.Card.cs` 的 `Discard`、`S/CombatPredictionSimulator.DiscardContinuation.cs`、`Q/SimulatedCombatState.ActionChoices.cs`、`Q/CardChoiceResolution.cs`。

- `FlashKernel.Discard` 对应 `CardCmd.Discard(choiceContext, CardModel)`：强制结束/已死时返回；卡移到弃牌堆底，`CardsDiscardedThisTurn + 1`，派发 `AfterCardDiscarded`，然后若该牌 `IsSlyThisTurn`（关键字 Sly 或单回合标记）立即自动打出。
- `FlashKernel.DiscardAndDraw` 对应多张 `CardCmd.Discard` / `DiscardAndDraw`：按选择顺序逐张弃置并派发钩子，然后抽牌，最后按弃置顺序自动打出“弃置时为 Sly”的牌。弃置后可能洗牌，所以自动打出按“值 + 同值第几张”在**整个战斗牌堆**里重新定位；同值序号在全部弃置完成后按弃置顺序分配，先行自动打出移走同值低序号后逐个下调。找不到时抛 `FlashUnsupportedException`（K0 合同）。
- 回合末手牌清理（`CorePowerSupport.FlushPlayerHandAtTurnEnd`）直接用 `AddToPile` 移入弃牌堆，**不**派发 `AfterCardDiscarded`、也不触发 Sly；Flash 保持相同行为。
- 卡牌自身选择的空选项：`BuildRequiredEmptyChoice` 列出的牌（HiddenDaggers、Brand）在手牌为空时，手动打出会提出 0..0 的空选择；自动打出中的嵌套选择由 `ResolveNestedCardChoice` **直接结算**同一个空选择、不提问。Flash 用 `FlashExec.CurrentPlayIsAutoPlay` 区分这两种情形，随后照常执行 `ApplyPostChoiceEffects`。
- 手牌触发（`AfterCardDrawn` 中的 `CorrosiveWavePower`、`SpeedsterPower` 等）与弃牌触发都在上面的位置派发；多张弃置的 `AfterCardDiscarded` 之间可以出现待处理选择，Flash 丢弃半成品状态、从父状态带选择重执行。

## 5. 伤害管线

来源：`S/CombatPredictionSimulator.Attack.cs:83-165`、`S/CombatPredictionSimulator.Damage.cs:55-467`、`M/Hooks/Damage/ModifyDamageMirrors.cs`。

1. **攻击**：结束中或攻击者已死则返回；`BeforeAttack`；`ModifyAttackHitCount`；逐次命中：攻击者死亡或无存活目标则停止，随机目标用 `CombatTargets` 流；最后记录历史并触发 `AfterAttack`。
2. **伤害**：先对全部目标计算并扣除，再统一处理结果。
3. **单目标**：目标已死则跳过；`ModifyDamage` = 附魔加成与倍率 → 按监听顺序的加法阶段（力量等）→ 乘法阶段（易伤、虚弱等）→ 取所有 `ModifyDamageCap` 的最小值 → 钳制到 ≥ 0。全程 `decimal`，不取整。`BeforeDamageReceived`；格挡吸收（目标是 Osty 时扣其主人的格挡；不可格挡伤害跳过）；`ModifyHpLost` BeforeOsty 与其 Late → Osty 重定向 → AfterOsty 与其 Late → `AfterModifyingHpLostAfterOsty`；失血时 `(int)` 截断；失血 ≥ 当前 HP 视为击杀。
4. **结果处理**：`AfterBlockBroken` → `AfterCurrentHpChanged` → `AfterDamageGiven` → 未被击杀时 `AfterDamageReceived`（含 Late）；之后执行击杀，或所有玩家死亡时判负。
5. **击杀**：扣光剩余 HP 并触发 `AfterCurrentHpChanged` → `BeforeDeath` → `ShouldDie`（第一个阻止者生效）。死亡：`AfterDeath` → 移出阵容 → 主敌人死亡且只剩附属时附属一并死亡。被阻止：`AfterDeath(prevented)` 与 `AfterPreventingDeath`，最多递归 10 次。
6. **敌人死亡效果延后**：在动作边界由 `ApplyEnemyDeathPowers` 统一结算（`P/CorePowerSupport.cs:597-674`）：SteamEruption → 死亡 Power（Ravenous、Surrounded、召唤）→ 清除死者施加的 Constrict、Hex、Shrink、MagicBomb。

## 6. 格挡与 Power

格挡（`S/CombatPredictionSimulator.Block.cs:49-93`）：结束中或已死返回 0 → `BeforeBlockGained` → `ModifyBlock`（附魔、加法阶段含敏捷、乘法阶段含脆弱、溢出钳制、≥ 0）→ `AfterModifyingBlockAmount` → 大于 0 时按出牌记录并加到格挡（`int` 截断，上限 999,999,999）→ `AfterBlockGained`。己方回合开始清格挡：`ShouldClearBlock` 或保留者，然后 `AfterBlockCleared`。

Power 施加（`Q/SimulatedCombatState.cs:648-711`）：数量为 0 或目标不能接受 Power 时返回 → 遗物修正（`TryModifyPowerAmountReceived` 普通阶段记录被修改的监听者，随后在 Artifact 检查前对它们派发 `AfterModifyingPowerAmountReceived`；RuinedHelmet 在此翻倍并标记本场已用）→ 目标有人工制品且是减益时抵消一层并返回 → 实例型 Power 新建实例，其余按（持有者、类型）叠加并钳制 → 玩家侧新获得的虚弱、易伤、脆弱标记跳过下次衰减 → 更新监听顺序 → 把非零变化放进待处理队列。旧引擎**没有**通用的 `AfterApplied`，只有少数 Power 初始化。

衰减：虚弱、易伤、脆弱、无实体、NoBlock 在**敌方回合结束时对所有生物**衰减并尊重跳过标记；Blur 在己方回合开始衰减且该回合开始不清格挡；DoubleDamage 在每方回合结束衰减；瞬时 Power 在己方回合结束清除；临时力量、敏捷、集中在 `TriggerRegular` 中按监听顺序退回。`FlashPowerDef.PreventsCombatEnding` 对应原版 `ShouldStopCombatFromEnding`：战斗结束判定会遍历仍在阵容中的生物的正层 Power，AdaptablePower、StockPower 等返回真时 `IsEnding` 保持假（TestSubject 的复活窗口）。

## 7. 回合流程

来源：`Q/CombatBeamSolver.Expansion.Replay.cs:965-1222`、`P/PlayerTurnEndLifecycle.cs`、`P/CorePowerSupport.cs`、`Q/CombatBeamSolver.RoundTransition.cs`、`Q/CombatBeamSolver.TurnExecutionContinuation.cs`。

**玩家回合结束**

1. 第一阶段：遗物 `BeforeSideTurnEnd`（含 VeryEarly）；`AfterAutoPostPlayPhaseEntered`；`BeforeSideTurnEnd` VeryEarly → Early → 普通；胜负判定；球的回合末被动；虚无卡先消耗，再处理回合末在手效果（移到打出区 → `OnTurnEndInHand` → 虚无则消耗，否则弃置）；胜负判定；玩家再生 −1。
2. 敌人死亡效果。
3. 未保留的手牌按手牌顺序弃置；回合末清理。
4. 第二阶段：缠绕伤害；清除 Tangled 与 Ringing；DoubleDamage 衰减；`TriggerRegular`；瞬时 Power；遗物 `AfterSideTurnEnd`；`AfterSideTurnEndLate`。
5. 敌人死亡效果；`ShouldTakeExtraTurn`。

**敌方回合**

1. 记录本回合行动的敌人快照；重置计数；`BeforeSideTurnStart`；清格挡与 `AfterBlockCleared`。
2. 己方回合开始效果：Blur、下回合抽牌清零、轮次大于 1 时 Plating 递减、Slow 重置、Power 与遗物的 `AfterSideTurnStart`。
3. 敌人中毒：触发 min(层数, 1 + 加速) 次，不可格挡且不受修正，每次 −1；敌人死亡效果；胜负判定。
4. 按快照顺序逐个敌人：不能行动则跳过 → 眩晕 → 强制行动 → 否则执行当前行动：`BeforeAttack` 补偿；逐次命中按基础伤害经完整伤害管线结算，命中后结算死亡效果，出现死亡则停止；失衡眩晕；行动附加效果（`P/MonsterMoveEffects.cs` 中按"怪物、行动"登记）；每个敌人后判定胜负。
5. 敌方回合结束效果：Doom 击杀、再生、Plating 格挡、`TriggerRegular`、持续时间衰减、`AfterSideTurnEndLate`。
6. 玩家中毒；清除 NoDraw；胜负判定。
7. 为下一轮准备怪物行动：只有本轮执行过行动的敌人推进状态机（见第 9 节）；例外为必须执行一次的行动、眩晕和新怪物的初始掷骰。

**玩家回合开始**

1. 轮次 +1（额外回合不加）；重置计数；记录 `AmountOnTurnStart`；`BeforeSideTurnStart`（原版：先遗物批次，再 Power）。
2. 清格挡与 `AfterBlockCleared`。
3. 能量：`ShouldPlayerResetEnergy`（IceCream 从第 2 回合起拒绝重置；否则清零）→ 加修正后的最大能量与 `ConsumeEnergyNextTurn`（`EnergyNextTurnPower` 的层数直接消费并清零）；`AfterEnergyReset`、遗物 `AfterEnergyReset`、`AfterEnergyResetLate`。
4. `BeforeHandDraw`：Power → 遗物 → 返回手牌的卡。返回手牌阶段处理 `FlashCard.ReturnToHandNextTurn`（Bolas/ThrummingHatchet），牌已在手牌时只清位。
5. 抽牌数 = 5 + 下回合抽牌 + DrawCardsNextTurn → `ModifyHandDraw` → Late；抽牌。`ConsumeModifiedHandDraw` 的“尚未快照”项（`DrawCardsNextTurnPower` 的 `AmountOnTurnStart == 0`）在监听者之前直接加到基础抽牌数；已快照的层数由该 Power 的 `ModifyHandDraw` 加。`DrawCardsNextTurnPower` 在 `TriggerBaseSideTurnStart` 清零。
6. `AfterPlayerTurnStart`（原版：先 Power，再遗物）。
7. 玩家己方回合开始效果在抽牌**之后**执行；若回合开始有选择，则在该选择之前执行。
8. 敌人死亡效果 → 附魔与球 Hook → 自动预打出 → 胜负判定。

**实现形状**（`FlashKernel.TurnPhases.cs`）

现有引擎的回合阶段有两种写法，Flash 分别对应：

- 按监听器顺序遍历 Power（或遗物）再按类型 `switch`：每个 `case` 是定义上的同名阶段方法（`FlashPowerDef.EndTurnRegular`、`BeforeHandDraw`、`AfterPlayerTurnStart` 等；遗物为 `FlashRelicDef` 的批次方法）。每一轮遍历开始时按监听器顺序（玩家、Osty、名单中的敌人，各自的 Power 按表内顺序）取快照，访问时按 (owner, id, occurrence) 重新定位（实例型 Power 每个实例一行），层数必须仍为正。
- 固定类型序列（例如 `TriggerOwnerAfterSideTurnStart` 的 BiasedCognition → … → Clarity、`TriggerBaseSideTurnStart` 的 Blur → DrawCardsNextTurn → Plating → Slow）：内核保存从现有代码抄录的原版 Id 列表，只调用已登记且在场、层数为正的类型。

持有者范围与现有遍历一致：`AfterEnergyReset` 与 `AfterPlayerTurnStart` 只看玩家生物，`BeforeHandDraw` 包含 Osty（`power.Owner.Player == player`）。敌方回合的每个阶段都重新读取当前敌人名单，对应现有代码直接传入 `simulatedCombat.Enemies`。`ShouldClearBlock` 是原版 Hook，战斗结束中不枚举监听者；第一个拒绝清除的监听者通过 `BlockAfterPreventingClear` 结算保留量。

## 8. 抽牌与洗牌

来源：`S/CombatPredictionSimulator.CardPile.cs`。

- 小数抽牌数向上取整；`ShouldDraw` 失败则整次不抽。
- 逐张：结束中或手牌达到上限则停止；抽牌堆空且弃牌堆非空时洗牌；取抽牌堆顶入手；历史 → `AfterCardDrawn` Early → 普通 → 被抽卡自身 Hook。`TryDrawOne` 在抽牌前保存被抽卡的值；Pillage 用它判断“刚抽到的牌是否攻击”，即使 `AfterCardDrawn` 中的 Hellraiser 已把该牌自动打出并从手牌移除。
- 洗牌：列表 = 弃牌堆（原顺序）+ 抽牌堆；按卡牌排序键稳定排序后用 `Shuffle` 流做不稳定洗牌（与 `StableShuffleProjection` 相同）；`ModifyShuffleOrder`；全部放到抽牌堆底；`ShuffleEventCount + 1`；战斗未结束时 `AfterShuffle`（TheAbacus 获得其 Unpowered 格挡）。排序键来自原版 `CardModel.CompareTo`，在根导入时编译进 `FlashRules`。
- 随机插入牌堆位置用 `Shuffle` 流的 `NextInt(n + 1)`。手牌满时加入手牌的卡改进弃牌堆。

## 9. 怪物 AI

旧引擎不运行原版状态机，而是解释它的图（`P/BranchMonsterAi.cs`）。Flash 在根导入时把每个敌人的状态机编译成表：

- 节点类型：行动节点（攻击列表：基础伤害与次数）、随机分支（各后继的基础权重）、条件分支（根捕获时的选择结果）。
- 推进：从当前行动的后继出发，随机分支用 `MonsterAi` 流按 `MonsterRandomBranchResolver.Pick` 的规则选择（依赖最近行动日志），条件分支取根选择，直到落到行动节点；最多 32 步。
- 特例：旧引擎在 `ResolveConditional`、`GetBranchWeight` 与 `Advance` 中按怪物类型写的特殊逻辑（KnowledgeDemon、FrogKnight、Fabricator、TestSubject、Ovicopter、LagavulinMatriarch、SlumberingBeetle、Queen、LivingShield、BowlbugRock、TwoTailedRat）由对应 `FlashMonsterDef` 覆写同名方法实现。A3 另用 `ModifyAttackDamage`/`ModifyAttackRepeats` 对应 `AdjustMonsterMoveDamage` 与 `BranchMonsterAi.CurrentMove` 的动态伤害/命中数（TheForgotten 的 DREAD、TestSubject 的 MULTI_CLAW）；根冻结的 Power 数量用 `FlashMonsterDef.RootPowerAmountNames` 编译进 `FlashMonsterRules.RootPowerAmounts`。
- 行动执行：攻击按编译表结算；攻击前的效果由 `FlashMonsterDef.BeforeMove` 实现（`P/MonsterMoveEffects.ApplyBeforeAttack`，例如 Living Fog 的 BLOAT 先生成 Gas Bomb）；非攻击效果由 `FlashMonsterDef.ApplyMoveEffects` 实现，规范来源是 `P/MonsterMoveEffects.cs` 与 `P/MonsterMoveSemantics.cs`。
- 遭遇槽位：`FlashEnemy.Slot` 来自 `SimulatedCombatState.GetSlotIndex`，名字不在 `_encounterSlots` 时就是原版的 -1（`NoSlot`）。`EncounterModel.Slots` 为空但仍用 `first`/`middle`/`last`、`odd`/`even`、`first`/`second`/`third` 的遭遇（碗虫、沉睡甲虫）因此不排序、也不报错；`NextFreeSlot` 对空槽表返回 `""`、`LastFreeSlot` 返回 `null`，与现有引擎一致，不编造槽位顺序。
- 生成初始状态：`FlashMonsterDef.SpawnedStunned` 让 Bridge 用 `StartStunned = true` 编译该类型的生成规则（Wriggler 的初始节点是 SPAWNED_MOVE）；`SpawnSlotName` 给这类按槽名选择 INIT_MOVE 分支的怪物一个固定合法槽名，避免私有编译遇到无匹配条件。
- 私有字段：旧引擎通过 `_monsterIntStates` 读写的怪物私有整数，在 Flash 中映射到 `FlashEnemy` 的 AI 寄存器，编号由各怪物定义的 `AiRegisterNames` 按声明顺序决定；定义另外用 `InertMonsterIntNames` 声明只被状态指纹/续接文本读取、不影响结算的条目。导入时由定义认领，未认领的条目拒绝（见第 13 节）。

## 10. 选择协议

- `FlashAction` 携带至多 4 个选择，每个选择是"选项列表上的位集合"。选项列表由需要选择的效果按现有引擎 `CardChoiceSupport.GetSpec` 的顺序生成（来源牌堆中满足条件的卡的位置）。
- 效果请求第 k 个选择而动作只提供了不到 k 个时，内核停止执行，返回 `NeedsChoice` 与选项规格（效果、时机、来源牌堆、选项的绝对位置、最少与最多数量；数量已按选项数截断，与 `BuildChoices` 一致）。调用方丢弃这个半成品状态，枚举选项后从父状态重新执行同一动作。
- 卡牌自己的选择在 `FlashCardDef.OwnChoice` 中，位于 `OnPlay` 之后、出牌收尾之前，对应现有引擎的 `ResolveManualCardChoice` 阶段（`GetSpec` → `Apply` → `ApplyPostChoiceEffects`）。`FlashKernel.ChooseCards` 对应 `CardChoiceSupport.Spec`：没有选项时不建规格、不提问，选择后效果照常执行；`ChooseCardsInRange` 对应 `RangeSpec`：没有选项也提问。`BuildRequiredEmptyChoice` 列出的卡（烙印等）在没有选项时提一个空选择。
- 选中的卡按选择顺序结算（与令牌顺序一致）。字节相同的卡在不同位置并不等价（拿走哪一张会改变中间卡的顺序），所以内核按"值 + 第几张相同的卡"定位选中的卡，对应旧引擎令牌的 `SourceOccurrence`；结算过程中有 Hook 介入时每张卡重新定位。
- 时机标签按现有引擎 `SetActionChoiceTiming` 的位置切换：出牌为 Action，回合结束开始时为 PlayerTurnEnd，敌方回合为 EnemyTurn，玩家回合开始为 PlayerTurnStart。回合开始的选择由 `EndTurn` 动作携带。
- 翻译与校验：Bridge 用旧引擎逐次回放动作。旧引擎停在待定选择时，取 Flash 的下一个位集合，按旧引擎规格的同序选项生成 `PlanCardChoice` 令牌（`CardChoiceSupport.BuildSelectedChoiceForFlash`），再按现有搜索放置已解析分支的同一规则（`CombatBeamSolver.AppendPendingChoice`：回合阶段的进 `TurnStartChoices`，出牌第一个无来源请求为主选择，其余为嵌套选择）挂到动作上重放，直到旧引擎不再提问。选择次数不一致即校验失败。

## 11. RNG

- 九条流与旧引擎一致：Shuffle、CombatCardGeneration、CombatPotionGeneration、CombatCardSelection、CombatEnergyCosts、CombatTargets、CombatOrbGeneration、MonsterAi、Niche。
- 每条流保存计数器与四个状态字，算法逐位复刻原版 `Rng`/`MegaRandom`，用纯内核检查对照原版类录制的输出向量。
- 每次取值计数器 +1，与原版一致；状态字和计数器都进入状态字节。

## 12. 强制行动与眩晕

来源：`Q/SimulatedCombatState.MonsterAi.cs:58-128`、`Q/SimulatedCombatState.cs:1174-1181`、`Q/CombatBeamSolver.Expansion.Replay.cs:1105-1180`、`P/BranchMonsterAi.cs:139-166,245-255`。

**规则编译**（`Bridge/FlashRulesCompiler.CompileMonster/CompileMove`）：`MoveState.MustPerformOnceBeforeTransitioning` 编译成 `FlashMonsterNode` 标志，不再拒绝。为每个怪物在已登记状态（以及未登记进状态表的初始分支）之后追加一个合成节点：Id 为 `STUNNED`、`Kind = Move`、无攻击、无效果、`MustPerformOnceBeforeTransitioning = true`、静态后继为空。已有节点下标保持不变；节点的后继动态存放在 `FlashEnemy.ForcedMove`（255 表示当前不是 STUNNED）。节点总数因此加一，超过 64 个节点的状态机在导入时按容量拒绝。

**失衡眩晕**：一次怪物行动的全部命中若至少有一次 `DamageResult.WasFullyBlocked`，且该怪物仍有 `ImbalancedPower`，`MonsterMoveSemantics.ApplyForecastMove` 在攻击收尾后、行动附加效果前强制 `STUNNED` 并 `StunNextMove`。Flash 的 `Damage` 返回"是否有完全格挡结果"，在 `ExecuteMonsterMoveCore` 的同一位置执行；BowlbugRock 的合成后继是 `HEADBUTT_MOVE`，其余怪物用最近一条行动日志。沉睡甲虫的 `SlumberPower` 在受击掉到 0 时强制 `STUNNED`（后继 `ROLL_OUT_MOVE`），在侧回合末掉到 0 时清 Plating 并由 `SNORE_NEXT` 条件分支走到 `ROLL_OUT_MOVE`。

**强制行动 API**：

- `FlashKernel.ForceMonsterMove(ref FlashExec, byte owner, int node)`：把当前节点换成 `node`，清除 `NeedsInitialRoll` 与 `ForcedMove`。不写 `LoggedMoves`/`RecentMoves`（`BranchMonsterAi` 只在 `Advance`/`RollInitial` 落到行动节点时写日志）。
- `FlashKernel.ForceStunnedMove(ref FlashExec, byte owner, int nextNode)`：`nextNode` 小于 0 时取现有引擎的缺省后继 `StateLog.LastOrDefault() ?? Current.Id`，即最近一条日志（`RecentMoves` 的末项），日志为空时取当前节点；随后写入 `ForcedMove = nextNode`、`Move = StunnedNode`、清除 `NeedsInitialRoll`。同样不写日志：现有引擎的 `ForceStunnedMove` 只替换 `Current`。
- 现有引擎的 `performedMoves` 是行动开始时的 `MoveState` 对象；强制行动在本轮已经行动之后替换当前节点时，这个对象与新的 `Current` 不同。Flash 用 `FlashEnemyFlags.ForcedMustPerformAfterMove` 记录这一条件（`ForceStunnedMove` 总是新建对象，在已行动之后调用时记录；`ForceMonsterMove` 只在目标不同且目标 must-perform-once 时记录）。`ActingThisRound` 覆盖现有引擎在动作返回后才写入 performed 标记的时序。两个标志都只在敌方回合内部使用，`PrepareMonsterMovesForNextRound` 消费并清零，稳定边界上恒为 0。

**推进**（`PrepareMonsterMovesForNextRound`，逐行对应 `MonsterAi.cs:102-128`）：不在场或不能行动则跳过；`NeedsInitialRoll` 走 `RollInitial` 对应的分支；本轮没有执行任何行动则跳过；当前是 STUNNED 且 `SkipNextMove` 为真则不推进；被强制替换且 must-perform-once 未满足则不推进；否则从 `ForcedMove`（STUNNED）或节点的静态后继推进，`ResolveMonsterState` 落到行动节点时写入日志并输出强制行动。被 `SkipNextMove` 跳过的敌人仍记入 `PerformedMoveThisRound`（`Replay.cs:1120-1125`）。

**执行**：STUNNED 没有攻击，跳过 `BeforeAttack`/`AfterAttack`；`FlashMonsterDef.ApplyMoveEffects` 仍以 `STUNNED` 调用，怪物定义可据此实现 `MonsterMoveEffects.Apply` 的 STUNNED 分支（例如乐加维林族母清空格挡）。STUNNED 本身不写行动日志。

**导入**：旧引擎当前行动 Id 为 `STUNNED` 时映射到合成节点，后继（`FollowUpState?.Id ?? FollowUpStateId`）映射到 `ForcedMove`；后继仍是 `STUNNED` 时显式拒绝。进行中的 must-perform-once 行动由节点标志表示，`PerformedMoveThisRound` 在稳定边界上为 0，因此导入后不会误推进。

**乐加维林族母的 `_isAwake`**：现有引擎在 AsleepPower/STUNNED 中写入、在指纹与续接文本中读取，但行动选择读 AsleepPower 数量（`BranchMonsterAi.ResolveConditional`），该布尔值不影响后续结算。`DescribeFlashUnrepresentableState` 只放过 `(LagavulinMatriarch, "_isAwake")` 这个已证明惰性的 `_monsterIntStates` 条目（不修改模拟器状态）；其他怪物私有整数仍被拒绝。

## 13. 敌人生成与召唤

来源：`P/MonsterSpawnSupport.cs`、`Q/SimulatedCombatState.cs:2800-2875,2977-3000`、`P/MonsterMoveEffects.cs` 的 `Fogmog`/`TwoTailedRat` 分支、`P/DeathPowerSupport.cs` 与 `Q/SimulatedCombatState.DeathLifecycle.cs:100-119,190-220`。

**规则编号与闭包**：每个 `FlashEnemy` 用 `Rules` 记录怪物规则表下标；根已知敌人按 `KnownEnemies` 下标占用表的前 `RootEnemyCount` 个条目，之后每个可生成怪物类型一个条目。`FlashMonsterDef.SpawnedMonsterIds` 声明该怪物能生成的原版 Id，`FlashRulesCompiler` 从根敌人出发递归求闭包。可生成类型的状态机、静态值与初始私有整数由编译器在根模拟器的**私有 Fork** 上执行现有引擎的创建路径得到：`MonsterSpawnSupport.Create(canonical)` → `AddPredictedMonster`（`SetUpForCombat` + `AddCreature` + 排序）→ `PreparePredictedMonster`，再用同一个 `CompileMonster` 编译；不手抄静态值。规则表总容量 `FlashLimits.MonsterRules`，溢出按 `Capacity` 拒绝。根持有 `Murderous` 修饰且闭包非空时拒绝（`UnsupportedClosure`）；贤者之石与毛皮大衣本就因缺少遗物定义在导入时拒绝。

**`SpawnMonster`**（`FlashKernel.SpawnMonster`，严格对应 `MonsterSpawnSupport.AddCreated` 的顺序）：

1. `CombatId = NextCombatId++`；生命在 `[MinInitialHp, MaxInitialHp]` 中排除名单敌人的当前 `MaxHp` 后取值。可选值按现有 `HashSet<int>` 的枚举顺序（等于升序插入顺序）由 Niche 流 `NextInt(0, count)` 取第 k 个；没有可选值时用 `NextInt(min, max + 1)`。K0 断言该枚举顺序。
2. 追加到已知敌人数组（`EnemyCount++`），再按遭遇槽下标稳定插入名单，对应 `AddCreature` + `SortEnemiesBySlotName`；同槽位保持先后顺序，无槽名（255）排在有名字的槽位之前。
3. 写入编译器捕获的 `SetUpForCombat` 初始值：`PendingInitialMoveNode`、初始 AI 寄存器。
4. 调用定义上的 `ApplyEntrancePowers`（对应 `ApplyNativeEntrancePowers`，只为本批内容实现）。
5. 玩家回合内立即从 `InitialNode` 掷出初始行动并写日志；敌人回合内保留 `NeedsInitialRoll`，由本轮结束的 `PrepareMonsterMovesForNextRound` 掷出。
6. `minion` 为真且没有 MinionPower 时补 1 层 Minion，applier 是召唤者。

**A3 生成**：Fabricator 按 `MonsterSpawnSupport.SpawnFabricatorBot` 的同一随机选择（`MonsterAi` 流 `NextItem`，排除 `fabricator_last_spawn`）生成 Guardbot/Noisebot/Zapbot/Stabbot，槽位取下一个空槽并带 Minion；Zapbot 的入场 HighVoltagePower 2 由 `ApplyEntrancePowers` 补上。Axebot 被击杀且仍有 StockPower 时在死亡钩内以同槽位生成替代体（`StockPower.AfterDeath`）：生成规则按 StockAmount 1 编译（BOOT_UP 初始节点与 +10 生命范围），入场再按来源 StockPower 补足剩余的 +10 与新的 StockPower 层数。

**空槽**：`FlashKernel.NextFreeSlot`/`LastFreeSlot` 与 `SimulatedCombatState` 同义：战斗没有遭遇时返回 `null`；有空槽时返回遭遇槽名；`NextFreeSlot` 在无空槽时返回 `""`，`LastFreeSlot` 在无空槽时返回 `null`。

**幻象与爪牙**：`EyeWithTeeth` 入场时先获得 IllusionPower 1（applier 为自己）再获得 MinionPower 1（无 applier），与 `ApplyNativeEntrancePowers` 一致。IllusionPower 的 `PreventsRemovalAfterDeath` 让尸体留在名单；其 `AfterDeath` 对应 `BeginIllusionRevive`：死亡阶段改为 `Reviving`，强制合成 `REVIVE_MOVE` 节点，动态后继取最近一条非复活行动日志。规则编译在 `IllusionRevive` 为真的怪物上追加该合成节点（无攻击、必须先执行一次），节点下标存在 `FlashMonsterRules.ReviveNode`。`REVIVE_MOVE` 执行时把生命回满并清回复活阶段，随后按动态后继推进。尸体的 Power 清理按 `RemovePowersAfterDeath`：有 IllusionPower 时保留增益与临时减益、移除其余减益；已离开名单的敌人清空全部 Power。MinionPower 的 `OwnerIsSecondaryEnemy` 让主要敌人死亡时连带杀死剩余爪牙；`MinionPower.ShouldPowerBeRemovedAfterOwnerDeath` 为假。

**TwoTailedRat**：AI 寄存器 0 是 `_turnsUntilSummonable`（初始 2），1 是 `_callForBackupCount`（初始 0）。`SCRATCH_MOVE`/`DISEASE_BITE_MOVE`/`SCREECH_MOVE` 递减前者；`CALL_FOR_BACKUP_MOVE` 在 `LastFreeSlot` 生成一只新鼠，再把名单中所有鼠的后一个寄存器同步为（最大值 + 1）。随机分支权重按 `BranchMonsterAi.GetBranchWeight`：可召唤时 CALL_FOR_BACKUP 为 0.75、其余三者各 1/12；不可召唤时 CALL_FOR_BACKUP 为 0、其余为 1。可召唤条件按 `CanTwoTailedRatSummon`：回合计数不大于 0、计数小于 3、`NextFreeSlot` 非空，且其他鼠当前行动不是 CALL_FOR_BACKUP。`SCREECH_MOVE` 还给玩家 1 层 Frail。

**寄生死亡与召唤**：`InfestedPower` 在死亡效果清算阶段（`ApplyEnemyDeathPowers` 的 `DeathPowerSupport.Trigger` 位置）生成四只 `Wriggler` 到 `wriggler1..4` 槽位，创建时 `StartStunned = true`。它们是主要敌人；在死亡效果尚未清算、尸体已离开名单的窗口里，`HasUnresolvedSpawningDeath`（`SpawnsPrimaryEnemyOnDeath`）让 `IsEnding` 继续为假，与现有引擎一致（`Hook.ShouldStopCombatFromEnding` 的活动监听表此时看不到已移除尸体的 Power）。

**重接复活**：`ReattachPower` 不阻止击杀命中，但阻止尸体离场（`PreventsRemovalAfterDeath`），`ShouldPowerBeRemovedAfterOwnerDeath` 为假。死亡效果清算时，若还有另一段能行动（存活或 `Reviving`）则死亡阶段改为 `Reviving` 并强制 `DEAD_MOVE`，否则所有段改为 `PermanentlyDead`。`REATTACH_MOVE` 由怪物的 `ResolveReviveMove` 处理：仍有一段能行动时按 `ReattachPower` 数量回血并清除 `Reviving`。Reviving 段的 `CurrentHp` 为 0，`IsHittable` 自然为假；评估的 `EffectiveEnemyHp` 在有其他未永久死亡段时取 `ReattachPower` 数量，否则为 0。

**导入认领**：`_monsterIntStates` 的每个条目必须被某个已知敌人的 `AiRegisterNames` 或 `InertMonsterIntNames` 认领，否则按 `UnrepresentableState` 拒绝；声明为寄存器的名字取 `GetMonsterInt` 的值（有捕获条目用条目，没有时读模型字段），因此与现有引擎的无条目语义一致。乐加维林族母与沉睡甲虫的 `_isAwake` 由 `InertMonsterIntNames` 声明。A3 另把 Fabricator 的 `fabricator_last_spawn`（经 `GetFabricatorLastSpawn`）、TestSubject 的 `_respawns`/`_extraMultiClawCount`/`SecondFormHp`/`ThirdFormHp` 与 Aeonglass 的两个字典计数投影为 AI 寄存器。

## 14. 亡灵契约师：Osty、Doom 与灵魂

来源：`S/CombatPredictionSimulator.Damage.cs`/`Attack.cs`、`Q/SimulatedCombatState.CardLifecycle.cs`、`Q/SimulatedCombatState.Doom.cs`、`P/MonsterMoveSemantics.cs`、`P/BeforeSideTurnEndMirrors`、`P/EndTurnPowerSupport`、`P/PowerLifecycleSupport` 与各卡牌的 mirror/补偿位置。

**状态**：`FlashBattle.Osty` 是玩家侧宠物；`OstyPresent` 表示宠物存在（死亡后仍在），`OstyWasRootPet` 表示根捕获时原生 `Player.Osty` 非空。Osty 的生命/格挡用 `FlashCreature`，Power 用 `FlashLimits.OstyOwner`，监听顺序在玩家 Power/遗物/药水之后、敌人之前。

**召唤/治疗**：`SummonOsty` 对应 `OstyCmd.Summon` 的现有引擎实现：不存在时创建（消耗一个 `NextCombatId`、以 amount 初始化生命、施加 1 层 `DieForYouPower`，无 applier）；存活时提高最大生命并等量治疗；死亡时保留对象并按 amount 重置。`HealOsty` 只加当前生命且不超上限。Osty 死亡不触发玩家死亡、不移出战斗、不清 Power；玩家死亡时在清空球后以 `force` 击杀仍存活的 Osty。

**代受伤害**：格挡按宠物主人取（Osty 被打消耗玩家格挡），before-Osty 的 `ModifyHpLost` 后由 `ModifyUnblockedDamageTarget` 链式选接收者（`DieForYouPower` 在 Osty 存活、目标是主人、powered attack 时返回 Osty），再对接收者跑 after-Osty 修正。接收者改变时产生两个 `DamageResult`：Osty 先承受，溢出的 `OverkillDamage` 经 after-Osty 修正后打给原目标；两者格挡字段来自原目标。怪物行动的攻击在 `Damage` 前临时清零“已死 Osty 的 DieForYou”再恢复（等价 `MonsterMoveSemantics.DamagePlayer`）；非攻击伤害不重定向。

**Osty 攻击**：`CardTag.OstyAttack` 的卡以 `OstyOwner` 为 attacker 调 `CardAttack`，Strength/Weak/攻击历史归宠物；宠物死亡时直接返回。攻击结束给 `OstyAttacksThisTurn` 加一；该计数只在 `OstyWasRootPet` 时于玩家回合开始清零（现有引擎 `BeginSideTurn(player)` 走原生 `ownerPlayer.Osty`）。

**Doom**：敌方侧回合末对每个存活参与者求该侧 Doom 总和，首名满足 `CurrentHp <= Doom` 的监听者触发一次成批 `DoomKill`（`TriggerEnemySideTurnEndEffects` 开头、Regen/Plating 之前）；玩家侧回合末走 `EndTurnRegular` 的单监听者 `Kill`。`DoomKill` 先统计每玩家 fatal 数（排除玩家、按 `ShouldOwnerDeathTriggerFatal`），逐个 `Kill`，再按已定义的 `BookRepairKnife` 治疗（否则准入拒绝该根）。`DoomAppliedThisTurn` 记录玩家本回合施加过正层 Doom（DeathsDoor），玩家回合开始清零。

**其他计数**：`EtherealPlays` 战斗长，导入根历史后按每次打出完成时该卡本地 Ethereal 关键字加一（BansheesCry/PullFromBelow）。

**Power 时机**：DieForYou/Calcify/Lethality/Haunt/Shroud/Countdown/Neurosurge/Oblivion/SicEm/NecroMastery/Debilitate/Veilpiercer/BorrowedTime/Friendship/EnfeeblingTouch/Demesne/SleightOfFlesh/SpiritOfAsh/DevourLife 逐条对应现有引擎 `HookMirrors`、`PowerLifecycleSupport`、`TurnStartPowerSupport`、`PersistentPowerSupport` 路径。Oblivion 在 `BeforeCardPlayed` 把当次层数放进栈上 `CardPlayPairs`（`OblivionDoom`），`AfterCardPlayed` 取出施加等量 Doom；稳定边界为空。Debilitate 的易伤/虚弱倍率并入 Vulnerable/Weak。SleightOfFlesh 在 `AfterPowerAmountChanged` 对“玩家施加给敌人的非临时减益”造成自身层数的 Unpowered 伤害。

**AfterHandEmptied**：现有引擎没有通用遗物 Hook，`TriggerRelicsAfterHandEmptied` 特判 `UnceasingTop`；Flash 在卡牌结果堆移动后（最外层效果、允许的回合阶段内）做同样特判：手牌空且抽/弃牌堆非空时抽一张。

**卡牌修正**：Flatten 在任意 Osty 攻击后给战斗区内每张 Flatten 追加一条 `SetThisTurn(0)`（Absolute/EndOfTurn，按次数累积）；进入战斗的 Flatten 在该回合已有 Osty 攻击时同样追加。RightHandHand 的 `AfterCardPlayedLate` 不校验打出的副本：任意一次能量值达到其 Energy 变量的打出，都把弃牌堆中的每个副本收回手牌（`HandleRightHandHand` 的同一条件）。
