# Flash 内核语义：角色机制

各角色专属的内核机制，从[内核语义](kernel-semantics.md)拆出；通用的执行上下文、Hook 派发、出牌、伤害与回合流程仍以那份文档为准。

## 1. 静默猎手：弃牌与 Sly

来源：`S/CombatPredictionSimulator.Card.cs` 的 `Discard`、`S/CombatPredictionSimulator.DiscardContinuation.cs`、`Q/SimulatedCombatState.ActionChoices.cs`、`Q/CardChoiceResolution.cs`。

- `FlashKernel.Discard` 对应 `CardCmd.Discard(choiceContext, CardModel)`：强制结束/已死时返回；卡移到弃牌堆底，`CardsDiscardedThisTurn + 1`，派发 `AfterCardDiscarded`，然后若该牌 `IsSlyThisTurn`（关键字 Sly 或单回合标记）立即自动打出。
- `FlashKernel.DiscardAndDraw` 对应多张 `CardCmd.Discard` / `DiscardAndDraw`：按选择顺序逐张弃置并派发钩子，然后抽牌，最后按弃置顺序自动打出“弃置时为 Sly”的牌。弃置后可能洗牌，所以自动打出按“值 + 同值第几张”在**整个战斗牌堆**里重新定位；同值序号在全部弃置完成后按弃置顺序分配，先行自动打出移走同值低序号后逐个下调。找不到时抛 `FlashUnsupportedException`（K0 合同）。
- 回合末手牌清理（`CorePowerSupport.FlushPlayerHandAtTurnEnd`）直接用 `AddToPile` 移入弃牌堆，**不**派发 `AfterCardDiscarded`、也不触发 Sly；Flash 保持相同行为。
- 卡牌自身选择的空选项：`BuildRequiredEmptyChoice` 列出的牌（HiddenDaggers、Brand）在手牌为空时，手动打出会提出 0..0 的空选择；自动打出中的嵌套选择由 `ResolveNestedCardChoice` **直接结算**同一个空选择、不提问。Flash 用 `FlashExec.CurrentPlayIsAutoPlay` 区分这两种情形，随后照常执行 `ApplyPostChoiceEffects`。
- 手牌触发（`AfterCardDrawn` 中的 `CorrosiveWavePower`、`SpeedsterPower` 等）与弃牌触发都在上面的位置派发；多张弃置的 `AfterCardDiscarded` 之间可以出现待处理选择，Flash 丢弃半成品状态、从父状态带选择重执行。

## 2. 故障机器人：充能球

来源：`S/CombatPredictionSimulator.Orb.cs`、`S/SimOrbQueue.cs`、`M/Orbs/**`、`M/Hooks/Orb/**`、`P/OrbLifecycleSupport.cs`、`P/EnchantmentLifecycleSupport.TriggerAfterTurnStartOrbs` 与回合阶段中的球调用位置。

**状态与规则。** 球按 `SimOrbQueue.Orbs` 的顺序存在 `FlashBattle.Orbs`，`OrbCount`/`OrbCapacity` 与旧引擎同义。`FlashOrb.Aux` 保存原版球唯一的私有十进制值：Dark 的 `_evokeVal`（初始 6）与 Glass 的 `_passiveVal`（初始 4）；其他球不使用。导入时容量超过 10、球数超过容量、无 Flash 定义的球、或私有值不是 `int` 能表示的整数，都按 `FlashImportException` 拒绝。`BaseOrbSlotCount`（角色基础槽数）进入 `FlashRules`；`LightningChannels` 与 `OrbChannels` 是两个追加计数器，前者导入 `CalculatedVarSpecRegistry.CountLightningChannels`（原版历史 + 预测历史），后者只统计预测历史中的充能事件，对应旧引擎的 1000 次充能安全上限。

**生成（channel）。** `FlashKernel.ChannelOrb` 对应 `CombatPredictionSimulator.OrbChannel`：结束中/有待处理选择时不做；基础槽数为 0 且容量为 0 时先加 1 槽（`OrbCmd.AddSlots`）；容量已满时先 `EvokeNext` 最左侧球，再入队。若激发副作用同步充能又把槽填满，旧引擎记录 `MethodMirrorIncomplete` 风险并放弃；Flash 抛 `FlashUnsupportedException`（K0 合同覆盖满槽先激发），不自行追加激发。容量仍为 0 时返回 false；成功入队后记录 `OrbChannels`，闪电球另记 `LightningChannels`，再派发 `AfterOrbChanneled`。预测历史达到 1000 次充能时显式拒绝。`AddOrbSlots` 按 `min(10 - capacity, amount)` 增长；`RemoveOrbSlots` 对应 `SimOrbQueue.RemoveCapacity`，从队尾删球且不触发任何球 Hook。

**激发（evoke）。** `EvokeNext` 对应 `OrbEvokeNext`/`OrbCmd.EvokeNext`：取最左侧球，按 `repeat` 重复，只有最后一次出队（Dualcast、MultiCast、Shatter 的 `dequeue:false` 语义）；每次激发后执行 `ApplyEnemyDeathPowers`。`EvokeOrb` 对应 `OrbEvoke`：先按需要出队，再调用球的 `Evoke`，把返回值写入目标 owner 列表后派发 `AfterOrbEvoked`；结束中或有待处理选择时不执行。`MultiCast` 等 X 次激发由卡牌读取 `ResolveEnergyXValue` 后传入。`Darkness`/`TeslaCoil`/`Loop` 的额外被动走 `OrbPassiveAt`（不查询触发次数 Hook）。

**被动（passive）。** `TriggerOrbPassive` 对应 `OrbModel.TriggerPassive`：先以纯查询 `ModifyOrbPassiveTriggerCounts` 计算次数（`GoldPlatedCables` 对首个球 +1；`AfterModifyingOrbPassiveTriggerCount` 只有展示监听者，Flash 不派发），再逐次 `Passive`；每次被动后执行 `ApplyEnemyDeathPowers`。`OrbPassiveAt` 对应 `OrbCmd.Passive`，跳过触发次数 Hook。两者都在结束中/待选择时不执行。`ModifyOrbValue` 是纯查询，按战斗监听顺序调用（`FocusPower` 增加集中并钳制 ≥ 0；集中只修正需要修正的球值），修正类 Hook 不写状态。

**回合位置。** 玩家回合末：`BeforeSideTurnEnd` 与胜负判定之后、虚无/回合末在手牌效果之前，按队列快照逐个球调用 `BeforeTurnEndOrbTrigger`（Plasma 的该触发为空）。每次触发与被动后的敌人死亡效果立即结算；战斗结束中后续触发无效果。玩家回合开始：在敌人死亡效果与苦难归一化之后、自动预打出之前，按队列快照调用 `AfterTurnStartOrbTrigger`；只有 Plasma 重写它并触发一次被动（仍经 `TriggerOrbPassive`，所以金缆的触发次数修正生效）。玩家死亡清空球队列时容量一并归零（`OrbQueue.Clear`）。

**球值。** Lightning 被动 3、激发 8；Frost 被动 2 并给所有者格挡、激发 5；Dark 被动把 `Modify(6)` 累加进 `Aux`，激发对当前生命最低的可命中敌人造成 **原始 `Aux`**（原版 `EvokeVal` 不再过 `ModifyOrbValue`，集中只影响累积）；Glass 被动取 `Modify(Aux)`，大于 0 时造成伤害并把 `Aux` 减 1，激发为 `Modify(Aux) * 2`，伤害所有可命中敌人；Plasma 被动 +1 能量、激发 +2 能量。Lightning 与 Glass 的随机目标取 `CombatTargets.NextItem` 的一次 `NextInt(0, count)`，候选为空时不消耗；Lightning 的显式目标（TeslaCoil）不消耗该流。Chaos 与 TrashToTreasure 的随机球取 `CombatOrbGeneration`，候选顺序是原版 `_validOrbs`：Lightning、Frost、Dark、Plasma、Glass。

**遗物。** CrackedCore 在第一回合玩家侧 `BeforeSideTurnStart` 充能闪电；RunicCapacitor 与 SymbioticVirus 在第一回合玩家侧 `AfterSideTurnStart` 增加 3 槽 / 充能暗球；GoldPlatedCables 实现触发次数 Hook；PowerCell 与旧引擎的 `RegisterIgnored<PowerCell>` 一致，是准入所需的空定义。ReptileTrinket 在 `AfterPotionUsed` 中按遗物变量施加临时力量（`ReptileTrinketPower`），对应 `TriggerRelicsAfterPotionUsed`。UnsettlingLamp 仍无定义：它需要卡牌实例身份、`BeforePowerAmountChanged` 与 `ModifyPowerAmountGivenMultiplicative` 的逐卡翻倍账本。

## 3. 储君：星能与锻造

来源：`S/CombatPredictionSimulator.Energy.cs`、`S/CombatPredictionSimulator.Card.cs`、`E/CombatPredictedCardExtensions.cs`、`P/PowerLifecycleSupport.cs`、`M/Hooks/Resources/AfterStarsGainedMirrors.cs`、`M/Hooks/Card/ModifyStarCostMirrors.cs`、`P/PersistentPowerSupport.Forge`、`Q/SimulatedCombatState.CardLifecycle.cs`、`Q/SimulatedCombatState.AutoPlay.cs`。

**状态。** 星能是 `FlashBattle.Stars`（`SimPlayerCombatState.Stars`），导入直接取 `playerState.Stars`。`BaseStarCost` 来自原版同名字段（无星费为 -1），临时星费取 `_temporaryStarCosts` 最后一条（多条目不一致按容量拒绝）。`LastStarsSpent` 是出牌期瞬时值，存栈上的 `FlashExec.StarValue`，不进入稳定状态。

**费用。** `StarCostWithModifiers` 对应 `GetStarCostWithModifiers`：X 星费卡返回当前全部星能；否则基础/临时星费只在非负时交给 `TryModifyStarCost`（BrilliantScarf、VoidFormPower），最后 `(int)` 截断并钳制 ≥ 0；X 星费不派发费用 Hook。`ShouldPayExcessEnergyCostWithStars` 没有原版监听者，Flash 不做能量超额折算。`ResolveStarXValue` 读 `StarValue` 再过 `ModifyXValue`（Stardust）。

**获得。** `FlashKernel.GainStars` 对应 `PlayerCmd.GainStars`/`CombatPredictionSimulator.GainStars`：`IsEnding` 时不获得；`ShouldGainStars` 没有原版监听者；状态钳制到 [0, 999999999]；正数获得同时累加 `StarsGainedThisTurn`（Radiate 的 CalculatedHits 乘数，玩家回合开始清零）；随后派发 `AfterStarsGained`（BlackHolePower）。卡牌在现有引擎的同一位置调用它（Venerate/HiddenCache/Convergence 在 `CardOnPlaySupport` 位置，SolarStrike/Glow/ShiningStrike 在 `CardEffectSpecRegistry` 位置，BigBang 按 Energy → Stars → Forge）。

**消耗。** `ManualPlay` 在卡进入打出区之前重算星费，写入 `StarValue`，扣星能后派发 `AfterStarsSpent`。现有引擎的顺序不是监听者顺序：先 `TriggerRelicsAfterStarsSpent`（GalacticDust、MiniRegent 按遗物表顺序），再按 `EffectivePowers()` 顺序跑 `ChildOfTheStarsPower`；Flash 因此用「先遗物、后 Power」两遍派发。自动打出不支付星能，但同样在 `skipXCapture` 为假时捕获 `StarValue`。

**锻造。** `FlashKernel.Forge` 对应 `PersistentPowerSupport.Forge`：有待处理选择时不执行；手牌/抽牌堆/弃牌堆/打出区都没有未消耗的 `SOVEREIGN_BLADE` 时创建一张规范 0 级君王之刃到手中（走 `AddGeneratedCardToCombat` 的进战斗与生成卡 Hook）；然后给**所有**牌堆里的非复制君王之刃的伤害变量加 amount（`Var0`）。`CreatedThroughForge` 只驱动原版视觉，不进入 Flash 状态。君王之刃的攻击次数来自 `Repeat` 变量（恒为 1，无原版效果调用 `SetRepeats`），`SeekingEdgePower` 把它改为全体目标，`ParryPower` 提供格挡。锻造卡与 `FurnacePower` 在 `GeneratedCards` 里声明君王之刃，规则闭包因此冻结它的两级数值。

**AutoPrePlay。** 玩家回合开始、`AfterTurnStartOrbs` 之后：`Phase = AutoPrePlay`；先按消耗堆顺序自动打出阶段开始时在消耗堆的 `BOMBARDMENT`（每次打出后回到消耗堆，捕获数量与当前数量必须一致，否则显式拒绝）；再按 `MayhemPower` 层数把抽牌堆顶牌移到打出区后依次自动打出（对应 `MoveCardsForAutoPlay` 与 `TriggerScheduledAutoPlays`）。Imbued、HistoryCourse、WhisperingEarring 没有 Flash 定义，持有它们的根在准入拒绝。完成后 `Phase = Play` 并做胜负判定。

**选择与回合开始选择。** `DecisionsDecisions` 在 `OwnChoice` 请求 `AutoPlayRepeated`（手牌中非不可打出的技能），随后按 `Repeat` 变量自动打出所选卡；第一张按请求选中的手牌位置定位，之后每次按上一次打出的结果牌堆位置（`MoveCard` 的插入下标）接力，值在打出中改变时取尾部钩子后的值，位置失效才退回按值定位。`HeirloomHammer` 请求 `Duplicate`（无色手牌），用 `CloneCard` 生成 `Repeat` 份到手中。`Charge`/`Guards`/`Begone` 请求 `Transform`，`FlashKernel.TransformSelectedTo` 按（值，同值第几张）在 `CardChoiceSupport.TransformSelectedCards` 的同一位置原位替换为 1 级或 0 级的 Minion 令牌。`CosmicIndifference`/`PhotonCut`/`Glimmer` 走 `MoveToDrawTop`。`TyrannyPower` 在玩家回合开始以 `PlayerTurnStart` 时机请求 `Exhaust`（`AfterPlayerTurnStart`），`ForegoneConclusionPower` 在 `BeforeHandDraw` 需要时先洗牌、再请求 `MoveToHand` 并移除自身；两者都由 `EndTurn` 动作携带选择。`FlashAction` 最多携带 32 个选择（`FlashLimits.MaxChoices`），足以覆盖 `DecisionsDecisions` 的嵌套自动打出；溢出按容量抛 `FlashCapacityException`。

**历史计数与卡牌钩子。** `CardsGeneratedThisCombat` 在 `History.CardGenerated` 的同一位置加一：`AddGeneratedCard`（含战斗结束时被拒绝加入的生成卡）与 `TransformSelectedTo`/`ReplaceCard` 的插入；供 `Supermassive` 的 `CalculationBase + ExtraDamage × 生成数` 计算伤害。`FlashEnemy.PoweredAttackHitsThisTurn` 在 `RecordDamageReceived` 里对“玩家 → 敌人、powered attack”加一（完全格挡也计数，非强化伤害不计），玩家回合开始清零，字节溢出抛 `FlashUnsupportedException`；`BeatIntoShape` 用攻击前的命中数计算锻造量（攻击自身的命中在 `CalculatedForge - Results.Count × CalculationExtra` 中抵消）。`KinglyKick`/`KinglyPunch` 在 `AfterCardDrawn` 用 `TakeIntrinsicDraw` 认领自身，分别追加 `AddThisCombat(-1)` 与 `Damage/Private` 累加；`MakeItSo` 在 `AfterCardPlayedLate` 按本回合已完成技能数取模回手；`TheSealedThronePower` 在 `BeforeCardPlayed` 给星能；`CollisionCourse` 生成 `DEBRIS`，`MeteorShower` 依次对全体施加 Weak、Vulnerable。

**VoidForm。** `VoidForm` 先按 `PrepareVoidFormApplication` 把已有 `VoidFormPower` 的 `Aux0`（`VoidFormPredictionState.CardsPlayedThisTurn`）置为 999999999，再施加 `VoidFormPower`，最后 `RequestPlayerTurnEnd`。`VoidFormPower` 的普通能量 Late 钩子与星能钩子只在手牌/打出区且 `Aux0 < Amount` 时返回 0；`AfterCardPlayed` 只对非自动打出的最后一段加一；`BeforeSideTurnStart` 在玩家参与时清零。投影优先取 `VoidFormPredictionState`，否则读模型的内部计数。评估侧照 `VoidFormOpportunityValue` 计算剩余免费次数的手牌节省价值。

**遗物与 Power。** GalacticDust/MiniRegent 的计数取 `StatefulRelicState.Current`，Regalite 取 `RegalitePredictionState.UsedThisTurn`，BrilliantScarf 取 `CounterPredictionState`（非自动出牌递增、玩家侧回合开始清零，计数达到 `Cards-1` 时能量与星能费用都变 0）。ChildOfTheStarsPower 在 `AfterStarsSpent` 给格挡；GenesisPower/StarNextTurnPower 在 `AfterEnergyReset` 获得星能（后者随后移除）；ArsenalPower/PillarOfCreationPower 在 `AfterCardGeneratedForCombat` 给力量/格挡；FurnacePower 在 `OwnerAfterSideTurnStart` 锻造；ConquerorPower 让君王之刃对持有者造成双倍伤害并在侧回合末减层；ReflectPower 反弹被格挡的 powered attack 伤害并在侧回合开始减层；DyingStarPower 与 MonarchsGazeStrengthDownPower 是负向临时力量，在持有者侧回合末归还。

## 4. 亡灵契约师：Osty、Doom 与灵魂

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
