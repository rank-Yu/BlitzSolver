# Blitz 内核语义：角色机制

各角色专属的内核机制，从[内核语义](combat-semantics.md)拆出；通用的执行上下文、Hook 派发、出牌、伤害与回合流程仍以那份文档为准。

文中的“现有引擎”指导出冻结快照时 CombatSolver 的原引擎（`a62f1850c8586e5951e222eecb0dd34231e76d8a`）；这些语义已冻结在 `tests/Golden/data`，以后以快照和实机证据为准。

## 1. 静默猎手：弃牌与 Sly

来源：`S/CombatPredictionSimulator.Card.cs` 的 `Discard`、`S/CombatPredictionSimulator.DiscardContinuation.cs`、`Q/SimulatedCombatState.ActionChoices.cs`、`Q/CardChoiceResolution.cs`。

- `Kernel.Discard` 对应 `CardCmd.Discard(choiceContext, CardModel)`：强制结束/已死时返回；卡移到弃牌堆底，`CardsDiscardedThisTurn + 1`，派发 `AfterCardDiscarded`，然后若该牌 `IsSlyThisTurn`（关键字 Sly 或单回合标记）立即自动打出。
- `Kernel.DiscardAndDraw` 对应多张 `CardCmd.Discard` / `DiscardAndDraw`：按选择顺序逐张弃置并派发钩子，然后抽牌，最后按弃置顺序自动打出“弃置时为 Sly”的牌。弃置后可能洗牌，旧引擎持有 `CardModel` 对象并从新牌堆打出；Blitz 在弃置时为每张 Sly 卡写一个一次性身份字节（`Card.TransientMark` 的 64..255，`Exec.TransientMarkBase` 给嵌套批次分配互不重叠的区间），随牌值穿过抽牌/洗牌后再按标记定位。字节相同的两张 Sly 牌**不能**互换：选另一张会改变手牌与弃牌堆的分配。标记被 hook 清除或卡已离开所有牌堆时抛 `UnsupportedException`，区间耗尽抛 `CapacityException`（K0 合同）。`DecisionsDecisions` 的重复自动打出和 Mayhem 的自动预打出队列复用同一 64..255 动态区间（`TransientMarkBase` 嵌套分配），按标记从当前牌堆打出。`BeatDown` 使用 1..63，`ClearTransientMarks` 只清自己的区间，因此不会清除 Sly/Decisions/Mayhem 批次的标记；若 BeatDown 恰好选中同一张卡，其标记会被覆盖，后续 Sly 自动打出按显式未支持回落。
- 回合末手牌清理（`CorePowerSupport.FlushPlayerHandAtTurnEnd`）直接用 `AddToPile` 移入弃牌堆，**不**派发 `AfterCardDiscarded`、也不触发 Sly；Blitz 保持相同行为。
- 卡牌自身选择的空选项：`BuildRequiredEmptyChoice` 列出的牌（HiddenDaggers、Brand）在手牌为空时，手动打出会提出 0..0 的空选择；自动打出中的嵌套选择由 `ResolveNestedCardChoice` **直接结算**同一个空选择、不提问。Blitz 用 `Exec.CurrentPlayIsAutoPlay` 区分这两种情形，随后照常执行 `ApplyPostChoiceEffects`。
- 手牌触发（`AfterCardDrawn` 中的 `CorrosiveWavePower`、`SpeedsterPower` 等）与弃牌触发都在上面的位置派发；多张弃置的 `AfterCardDiscarded` 之间可以出现待处理选择，Blitz 丢弃半成品状态、从父状态带选择重执行。

### 1.1 剩余卡牌与遗物

- **KnifeTrap**：按消耗堆顺序取出 Shiv（按“值 + 同值第几张”定位），升级版先把该张 Shiv 升一级，再 `Kernel.AutoPlay` 到同一目标；Shiv 带消耗关键字，打出后回到消耗堆。位置假设不成立（抽牌钩子移动了牌）时抛 `UnsupportedException`。对照 `CardOnPlaySupport.Batch043.ApplyKnifeTrap`。
- **EchoingSlash**：`Kernel.CardAttackRepeating` 在一个 `BeforeAttack`/`AfterAttack` 上下文内反复对全体敌人造成伤害；每波后按当前名单结算死亡，每击杀一名敌人追加一波。对照 `CardOnPlaySupport.Batch042.ApplyEchoingSlash`。
- **Burst**：`BurstPower.ModifyCardPlayCount` 给技能牌 +1 次重放，`AfterModifyingCardPlayCount` 减一层，`EndTurnRegular` 移除；对照 `ModifyCardPlayCountMirrors` 与 `CorePowerSupport` 的 Burst case。
- **Shadowmeld**：`ShadowmeldPower.ModifyBlockMultiplicative` 按 2^层数放大其主人的格挡（≥96 层取 `decimal.MaxValue`，与现有引擎的溢出阀一致），侧回合末移除；对照 `ModifyBlockMultiplicativeMirrors.HandleShadowmeldPower`。
- **ToolsOfTheTrade**：`ModifyHandDraw` 加层数；玩家回合开始按层数弃牌（`TurnStartPowerSupport` 的 ToolsOfTheTrade case，`PlanChoiceEffect.Discard`）。
- **Mirage**：`CalculationBase + CalculationExtra × 所有存活敌人的毒层数`，以 `Move` 卡牌格挡结算；对照 `CalculatedVarSpecRegistry` 的 Mirage。
- **TheHunt**：攻击前捕获目标是否允许致命击杀，击杀后施加 `TheHuntPower` 并记 30 长线资源；战斗内额外卡牌奖励只在 `CombatRoom` 上，两个模拟状态都不表示它，评估按现有 `missedTheHuntRewards` 扣分。对照 `CorePowerSupport` 的 TheHunt case。
- **遗物**：Tingsha 在玩家侧弃牌后随机打 3 点 Unpowered；HelicalDart 在 Shiv 打出后施加 1 层临时敏捷（`HelicalDartPower`）；SneckoSkull 通过新的 `ModifyPowerAmountGiven` 查询给玩家施加的毒 +1；PaperKrane 在 `WeakPower` 的乘数里对玩家目标减 0.15；BeltBuckle 战前若没有药水已在根快照里给 2 敏捷，`AfterPotionUsed` 在最后一瓶药用掉且本场尚未补过时再给 2 敏捷并置遗物计数（对应 `TriggerRelicsAfterPotionUsed` 的 `StatefulRelicState.Current` 门控）。PenNib 的第十次攻击锁存按 `Exec.PlayIdentity` 记账：弃牌选择里嵌套自动打出的 Sly 牌是另一次 CardPlay，不会继承加倍；对照 `BeforeCardPlayedMirrors.HandlePenNib`/`ModifyDamageMirrors.HandlePenNib`/`AfterCardPlayedMirrors.HandlePenNib` 的 `AttackToDouble == CardSource.Original`。对照 `AfterCardDiscardedMirrors`、`AfterCardPlayedMirrors.HandleHelicalDart`、`PersistentRelicSupport.ModifyPowerAmountGiven`、`ModifyDamageMirrors.HandleWeakPower`。

## 2. 故障机器人：充能球

来源：`S/CombatPredictionSimulator.Orb.cs`、`S/SimOrbQueue.cs`、`M/Orbs/**`、`M/Hooks/Orb/**`、`P/OrbLifecycleSupport.cs`、`P/EnchantmentLifecycleSupport.TriggerAfterTurnStartOrbs` 与回合阶段中的球调用位置。

**状态与规则。** 球按 `SimOrbQueue.Orbs` 的顺序存在 `Battle.Orbs`，`OrbCount`/`OrbCapacity` 与旧引擎同义。`Orb.Aux` 保存原版球唯一的私有十进制值：Dark 的 `_evokeVal`（初始 6）与 Glass 的 `_passiveVal`（初始 4）；其他球不使用。导入时容量超过 10、球数超过容量、无 Blitz 定义的球、或私有值不是 `int` 能表示的整数，都按 `ImportException` 拒绝。`BaseOrbSlotCount`（角色基础槽数）进入 `Rules`；`LightningChannels` 与 `OrbChannels` 是两个追加计数器，前者导入 `CalculatedVarSpecRegistry.CountLightningChannels`（原版历史 + 预测历史），后者只统计预测历史中的充能事件，对应旧引擎的 1000 次充能安全上限。

**生成（channel）。** `Kernel.ChannelOrb` 对应 `CombatPredictionSimulator.OrbChannel`：结束中/有待处理选择时不做；基础槽数为 0 且容量为 0 时先加 1 槽（`OrbCmd.AddSlots`）；容量已满时先 `EvokeNext` 最左侧球，再入队。若激发副作用同步充能又把槽填满，旧引擎记录 `MethodMirrorIncomplete` 风险并放弃；Blitz 抛 `UnsupportedException`（K0 合同覆盖满槽先激发），不自行追加激发。容量仍为 0 时返回 false；成功入队后记录 `OrbChannels`，闪电球另记 `LightningChannels`，再派发 `AfterOrbChanneled`。预测历史达到 1000 次充能时显式拒绝。`AddOrbSlots` 按 `min(10 - capacity, amount)` 增长；`RemoveOrbSlots` 对应 `SimOrbQueue.RemoveCapacity`，从队尾删球且不触发任何球 Hook。

**激发（evoke）。** `EvokeNext` 对应 `OrbEvokeNext`/`OrbCmd.EvokeNext`：取最左侧球，按 `repeat` 重复，只有最后一次出队（Dualcast、MultiCast、Shatter 的 `dequeue:false` 语义）；每次激发后执行 `ApplyEnemyDeathPowers`。`EvokeOrb` 对应 `OrbEvoke`：先按需要出队，再调用球的 `Evoke`，把返回值写入目标 owner 列表后派发 `AfterOrbEvoked`；结束中或有待处理选择时不执行。`MultiCast` 等 X 次激发由卡牌读取 `ResolveEnergyXValue` 后传入。`Darkness`/`TeslaCoil`/`Loop` 的额外被动走 `OrbPassiveAt`（不查询触发次数 Hook）。

**被动（passive）。** `TriggerOrbPassive` 对应 `OrbModel.TriggerPassive`：先以纯查询 `ModifyOrbPassiveTriggerCounts` 计算次数（`GoldPlatedCables` 对首个球 +1；`AfterModifyingOrbPassiveTriggerCount` 只有展示监听者，Blitz 不派发），再逐次 `Passive`；每次被动后执行 `ApplyEnemyDeathPowers`。`OrbPassiveAt` 对应 `OrbCmd.Passive`，跳过触发次数 Hook。两者都在结束中/待选择时不执行。`ModifyOrbValue` 是纯查询，按战斗监听顺序调用（`FocusPower` 增加集中并钳制 ≥ 0；集中只修正需要修正的球值），修正类 Hook 不写状态。

**回合位置。** 玩家回合末：`BeforeSideTurnEnd` 与胜负判定之后、虚无/回合末在手牌效果之前，按队列快照逐个球调用 `BeforeTurnEndOrbTrigger`（Plasma 的该触发为空）。每次触发与被动后的敌人死亡效果立即结算；战斗结束中后续触发无效果。玩家回合开始：在敌人死亡效果与苦难归一化之后、自动预打出之前，按队列快照调用 `AfterTurnStartOrbTrigger`；只有 Plasma 重写它并触发一次被动（仍经 `TriggerOrbPassive`，所以金缆的触发次数修正生效）。玩家死亡清空球队列时容量一并归零（`OrbQueue.Clear`）。

**球值。** Lightning 被动 3、激发 8；Frost 被动 2 并给所有者格挡、激发 5；Dark 被动把 `Modify(6)` 累加进 `Aux`，激发对当前生命最低的可命中敌人造成 **原始 `Aux`**（原版 `EvokeVal` 不再过 `ModifyOrbValue`，集中只影响累积）；Glass 被动取 `Modify(Aux)`，大于 0 时造成伤害并把 `Aux` 减 1，激发为 `Modify(Aux) * 2`，伤害所有可命中敌人；Plasma 被动 +1 能量、激发 +2 能量。Lightning 与 Glass 的随机目标取 `CombatTargets.NextItem` 的一次 `NextInt(0, count)`，候选为空时不消耗；Lightning 的显式目标（TeslaCoil）不消耗该流。Chaos 与 TrashToTreasure 的随机球取 `CombatOrbGeneration`，候选顺序是原版 `_validOrbs`：Lightning、Frost、Dark、Plasma、Glass。

**遗物。** CrackedCore 在第一回合玩家侧 `BeforeSideTurnStart` 充能闪电；RunicCapacitor 与 SymbioticVirus 在第一回合玩家侧 `AfterSideTurnStart` 增加 3 槽 / 充能暗球；GoldPlatedCables 实现触发次数 Hook；PowerCell 与旧引擎的 `RegisterIgnored<PowerCell>` 一致，是准入所需的空定义。ReptileTrinket 在 `AfterPotionUsed` 中按遗物变量施加临时力量（`ReptileTrinketPower`），对应 `TriggerRelicsAfterPotionUsed`。UnsettlingLamp 使用实例身份：`BeforePowerAmountChanged` 记录触发卡的来源序号与临时 Power 内部类型，`ModifyPowerAmountGivenMultiplicative` 对同一来源、同一种临时 Power 内部类型的首次减益翻倍，完成的锁存写入遗物 `Counter`。

### 2.1 剩余卡牌与药水

- **Leap**：通用格挡；**BoostAway**：格挡后生成一张 Dazed 到弃牌堆（`CardEffectSpecRegistry` 的 BoostAway case）；**MeteorStrike**：单体攻击后充能 3 个等离子球（`OrbCardMirrors.MeteorStrikeOnPlay`）；**MomentumStrike**：攻击后给当前打出的牌加一条本场绝对 0 费修饰（`CardEffectSpecRegistry` 的 MomentumStrike case，`CurrentPlayCard` 取当前打出堆槽位）；**Quadcast**：`EvokeNext(Repeat)`，只有最后一激发退队（`OrbCardMirrors.QuadcastOnPlay`）。
- **EssenceOfDarkness**：按当前球槽容量逐次充能暗球（`OrbPotionMirrors.EssenceOfDarknessOnUse`）。
- 多人限定卡（EnergySurge、Ignition、ImitationLearning、Hibernate、OneForAll）在单人 `FilterForCombatAndPlayerCount` 中被排除，按原版不定义。

## 3. 储君：星能与锻造

来源：`S/CombatPredictionSimulator.Energy.cs`、`S/CombatPredictionSimulator.Card.cs`、`E/CombatPredictedCardExtensions.cs`、`P/PowerLifecycleSupport.cs`、`M/Hooks/Resources/AfterStarsGainedMirrors.cs`、`M/Hooks/Card/ModifyStarCostMirrors.cs`、`P/PersistentPowerSupport.Forge`、`Q/SimulatedCombatState.CardLifecycle.cs`、`Q/SimulatedCombatState.AutoPlay.cs`。

**状态。** 星能是 `Battle.Stars`（`SimPlayerCombatState.Stars`），导入直接取 `playerState.Stars`。`BaseStarCost` 来自原版同名字段（无星费为 -1），临时星费取 `_temporaryStarCosts` 最后一条（多条目不一致按容量拒绝）。`LastStarsSpent` 是出牌期瞬时值，存栈上的 `Exec.StarValue`，不进入稳定状态。

**费用。** `StarCostWithModifiers` 对应 `GetStarCostWithModifiers`：X 星费卡返回当前全部星能；否则基础/临时星费只在非负时交给 `TryModifyStarCost`（BrilliantScarf、VoidFormPower），最后 `(int)` 截断并钳制 ≥ 0；X 星费不派发费用 Hook。`ShouldPayExcessEnergyCostWithStars` 没有原版监听者，Blitz 不做能量超额折算。`ResolveStarXValue` 读 `StarValue` 再过 `ModifyXValue`（Stardust）。

**获得。** `Kernel.GainStars` 对应 `PlayerCmd.GainStars`/`CombatPredictionSimulator.GainStars`：`IsEnding` 时不获得；`ShouldGainStars` 没有原版监听者；状态钳制到 [0, 999999999]；正数获得同时累加 `StarsGainedThisTurn`（Radiate 的 CalculatedHits 乘数，玩家回合开始清零）；随后派发 `AfterStarsGained`（BlackHolePower）。卡牌在现有引擎的同一位置调用它（Venerate/HiddenCache/Convergence 在 `CardOnPlaySupport` 位置，SolarStrike/Glow/ShiningStrike 在 `CardEffectSpecRegistry` 位置，BigBang 按 Energy → Stars → Forge）。

**消耗。** `ManualPlay` 在卡进入打出区之前重算星费，写入 `StarValue`，扣星能后派发 `AfterStarsSpent`。现有引擎的顺序不是监听者顺序：先 `TriggerRelicsAfterStarsSpent`（GalacticDust、MiniRegent 按遗物表顺序），再按 `EffectivePowers()` 顺序跑 `ChildOfTheStarsPower`；Blitz 因此用「先遗物、后 Power」两遍派发。自动打出不支付星能，但同样在 `skipXCapture` 为假时捕获 `StarValue`。

**锻造。** `Kernel.Forge` 对应 `PersistentPowerSupport.Forge`：有待处理选择时不执行；手牌/抽牌堆/弃牌堆/打出区都没有未消耗的 `SOVEREIGN_BLADE` 时创建一张规范 0 级君王之刃到手中（走 `AddGeneratedCardToCombat` 的进战斗与生成卡 Hook）；然后给**所有**牌堆里的非复制君王之刃的伤害变量加 amount（`Var0`）。`CreatedThroughForge` 只驱动原版视觉，不进入 Blitz 状态。君王之刃的攻击次数来自 `Repeat` 变量（恒为 1，无原版效果调用 `SetRepeats`），`SeekingEdgePower` 把它改为全体目标，`ParryPower` 提供格挡。`SwordSagePower` 的重放加成按现有引擎 `NormalizeSwordSageReplays`：根快照内的刀带冻结根层数，根之后由锻造生成的非克隆刀从 0 起算、再由当前层数补差，克隆从当前层数起算；`Card.Private` 记录已应用加成。锻造卡与 `FurnacePower` 在 `GeneratedCards` 里声明君王之刃，规则闭包因此冻结它的两级数值。

**AutoPrePlay。** 玩家回合开始、`AfterTurnStartOrbs` 之后：`Phase = AutoPrePlay`；先按消耗堆顺序自动打出阶段开始时在消耗堆的 `BOMBARDMENT`（每次打出后回到消耗堆，捕获数量与当前数量必须一致，否则显式拒绝）；再按 `MayhemPower` 层数把抽牌堆顶牌移到打出区后依次自动打出（对应 `MoveCardsForAutoPlay` 与 `TriggerScheduledAutoPlays`），随后每个未熔化的 `HISTORY_COURSE` 复制上一玩家回合最后一张攻击牌并自动打出（详见[状态布局](combat-state-layout.md#28-结局与跨回合卡值槽)）；再执行 Imbued 的附魔自动打出。完成阶段后 `Phase = Play` 并做胜负判定。

**选择与回合开始选择。** `DecisionsDecisions` 在 `OwnChoice` 请求 `AutoPlayRepeated`（手牌中非不可打出的技能），随后按 `Repeat` 变量自动打出所选卡；第一张按请求选中的手牌位置定位，之后每次按上一次打出的结果牌堆位置（`MoveCard` 的插入下标）接力，值在打出中改变时取尾部钩子后的值，位置失效才退回按值定位。`HeirloomHammer` 请求 `Duplicate`（无色手牌），用 `CloneCard` 生成 `Repeat` 份到手中。`Charge`/`Guards`/`Begone` 请求 `Transform`，`Kernel.TransformSelectedTo` 按（值，同值第几张）在 `CardChoiceSupport.TransformSelectedCards` 的同一位置原位替换为 1 级或 0 级的 Minion 令牌。`CosmicIndifference`/`PhotonCut`/`Glimmer` 走 `MoveToDrawTop`。`TyrannyPower` 在玩家回合开始以 `PlayerTurnStart` 时机请求 `Exhaust`（`AfterPlayerTurnStart`），`ForegoneConclusionPower` 在 `BeforeHandDraw` 需要时先洗牌、再请求 `MoveToHand` 并移除自身；两者都由 `EndTurn` 动作携带选择。`PlayerAction` 最多携带 32 个选择（`Limits.MaxChoices`），足以覆盖 `DecisionsDecisions` 的嵌套自动打出；溢出按容量抛 `CapacityException`。

**历史计数与卡牌钩子。** `CardsGeneratedThisCombat` 在 `History.CardGenerated` 的同一位置加一：`AddGeneratedCard`（含战斗结束时被拒绝加入的生成卡）与 `TransformSelectedTo`/`ReplaceCard` 的插入；供 `Supermassive` 的 `CalculationBase + ExtraDamage × 生成数` 计算伤害。`Enemy.PoweredAttackHitsThisTurn` 在 `RecordDamageReceived` 里对“玩家 → 敌人、powered attack”加一（完全格挡也计数，非强化伤害不计），玩家回合开始清零，字节溢出抛 `UnsupportedException`；`BeatIntoShape` 用攻击前的命中数计算锻造量（攻击自身的命中在 `CalculatedForge - Results.Count × CalculationExtra` 中抵消）。`KinglyKick`/`KinglyPunch` 在 `AfterCardDrawn` 用 `TakeIntrinsicDraw` 认领自身，分别追加 `AddThisCombat(-1)` 与 `Damage/Private` 累加；`MakeItSo` 在 `AfterCardPlayedLate` 按本回合已完成技能数取模回手；`TheSealedThronePower` 在 `BeforeCardPlayed` 给星能；`CollisionCourse` 生成 `DEBRIS`，`MeteorShower` 依次对全体施加 Weak、Vulnerable。

**VoidForm。** `VoidForm` 先按 `PrepareVoidFormApplication` 把已有 `VoidFormPower` 的 `Aux0`（`VoidFormPredictionState.CardsPlayedThisTurn`）置为 999999999，再施加 `VoidFormPower`，最后 `RequestPlayerTurnEnd`。`VoidFormPower` 的普通能量 Late 钩子与星能钩子只在手牌/打出区且 `Aux0 < Amount` 时返回 0；`AfterCardPlayed` 只对非自动打出的最后一段加一；`BeforeSideTurnStart` 在玩家参与时清零。投影优先取 `VoidFormPredictionState`，否则读模型的内部计数。评估侧照 `VoidFormOpportunityValue` 计算剩余免费次数的手牌节省价值。

**遗物与 Power。** GalacticDust/MiniRegent 的计数取 `StatefulRelicState.Current`，Regalite 取 `RegalitePredictionState.UsedThisTurn`，BrilliantScarf 取 `CounterPredictionState`（非自动出牌递增、玩家侧回合开始清零，计数达到 `Cards-1` 时能量与星能费用都变 0）。ChildOfTheStarsPower 在 `AfterStarsSpent` 给格挡；GenesisPower/StarNextTurnPower 在 `AfterEnergyReset` 获得星能（后者随后移除）；ArsenalPower/PillarOfCreationPower 在 `AfterCardGeneratedForCombat` 给力量/格挡；FurnacePower 在 `OwnerAfterSideTurnStart` 锻造；ConquerorPower 让君王之刃对持有者造成双倍伤害并在侧回合末减层；ReflectPower 反弹被格挡的 powered attack 伤害并在侧回合开始减层；DyingStarPower 与 MonarchsGazeStrengthDownPower 是负向临时力量，在持有者侧回合末归还。

## 4. 亡灵契约师：Osty、Doom 与灵魂

来源：`S/CombatPredictionSimulator.Damage.cs`/`Attack.cs`、`Q/SimulatedCombatState.CardLifecycle.cs`、`Q/SimulatedCombatState.Doom.cs`、`P/MonsterMoveSemantics.cs`、`P/BeforeSideTurnEndMirrors`、`P/EndTurnPowerSupport`、`P/PowerLifecycleSupport` 与各卡牌的 mirror/补偿位置。

**状态**：`Battle.Osty` 是玩家侧宠物；`OstyPresent` 表示宠物存在（死亡后仍在），`OstyWasRootPet` 表示根捕获时原生 `Player.Osty` 非空。Osty 的生命/格挡用 `Creature`，Power 用 `Limits.OstyOwner`，监听顺序在玩家 Power/遗物/药水之后、敌人之前。

**召唤/治疗**：`SummonOsty` 对应 `OstyCmd.Summon` 的现有引擎实现：不存在时创建（消耗一个 `NextCombatId`、以 amount 初始化生命、施加 1 层 `DieForYouPower`，无 applier）；存活时提高最大生命并等量治疗；死亡时保留对象并按 amount 重置。`HealOsty` 只加当前生命且不超上限。Osty 死亡不触发玩家死亡、不移出战斗、不清 Power；玩家死亡时在清空球后以 `force` 击杀仍存活的 Osty。

**代受伤害**：格挡按宠物主人取（Osty 被打消耗玩家格挡），before-Osty 的 `ModifyHpLost` 后由 `ModifyUnblockedDamageTarget` 链式选接收者（`DieForYouPower` 在 Osty 存活、目标是主人、powered attack 时返回 Osty），再对接收者跑 after-Osty 修正。接收者改变时产生两个 `DamageResult`：Osty 先承受，溢出的 `OverkillDamage` 经 after-Osty 修正后打给原目标；两者格挡字段来自原目标。怪物行动的攻击在 `Damage` 前临时清零“已死 Osty 的 DieForYou”再恢复（等价 `MonsterMoveSemantics.DamagePlayer`）；非攻击伤害不重定向。

**Osty 攻击**：`CardTag.OstyAttack` 的卡以 `OstyOwner` 为 attacker 调 `CardAttack`，Strength/Weak/攻击历史归宠物；宠物死亡时直接返回。攻击结束给 `OstyAttacksThisTurn` 加一；该计数只在 `OstyWasRootPet` 时于玩家回合开始清零（现有引擎 `BeginSideTurn(player)` 走原生 `ownerPlayer.Osty`）。

**Doom**：敌方侧回合末对每个存活参与者求该侧 Doom 总和，首名满足 `CurrentHp <= Doom` 的监听者触发一次成批 `DoomKill`（`TriggerEnemySideTurnEndEffects` 开头、Regen/Plating 之前）；玩家侧回合末走 `EndTurnRegular` 的单监听者 `Kill`。原版 `DoomPower.DoomKill` 逐个 `CreatureCmd.Kill`（每个击杀立即完成 `AfterDeath` 与 Power 移除），全部击杀后才派发 `AfterDiedToDoom`；Blitz 与现有引擎都在每次 `Kill` 后结算该批的敌人死亡效果，最后才按 `ShouldOwnerDeathTriggerFatal` 统计每玩家 fatal 数（排除玩家），再按已定义的 `BookRepairKnife` 治疗（否则准入拒绝该根）。`DoomAppliedThisTurn` 记录玩家本回合施加过正层 Doom（DeathsDoor），玩家回合开始清零。

**其他计数**：`EtherealPlays` 战斗长，导入根历史后按每次打出完成时该卡本地 Ethereal 关键字加一（BansheesCry/PullFromBelow）。

**Melancholy**：`AfterDeath` 用 `State.FindCard(card)` 找战斗副本，对每个战斗副本按 `Energy` 变量追加本场 −费用；牌组（局外）副本取不到战斗副本时不产生效果。Blitz 只导入战斗副本，`DeckCopyOutsideCombatIsInert` 让这类牌组副本通过准入，依据与边界见[准入](archive/admission-and-verification.md)。

**Power 时机**：DieForYou/Calcify/Lethality/Haunt/Shroud/Countdown/Neurosurge/Oblivion/SicEm/NecroMastery/Debilitate/Veilpiercer/BorrowedTime/Friendship/EnfeeblingTouch/Demesne/SleightOfFlesh/SpiritOfAsh/DevourLife 逐条对应现有引擎 `HookMirrors`、`PowerLifecycleSupport`、`TurnStartPowerSupport`、`PersistentPowerSupport` 路径。Oblivion 在 `BeforeCardPlayed` 把当次层数放进栈上 `CardPlayPairs`（`OblivionDoom`），`AfterCardPlayed` 取出施加等量 Doom；稳定边界为空。Debilitate 的易伤/虚弱倍率并入 Vulnerable/Weak。SleightOfFlesh 在 `AfterPowerAmountChanged` 对“玩家施加给敌人的非临时减益”造成自身层数的 Unpowered 伤害。

**AfterHandEmptied**：现有引擎没有通用遗物 Hook，`TriggerRelicsAfterHandEmptied` 特判 `UnceasingTop`；Blitz 在卡牌结果堆移动后（最外层效果、允许的回合阶段内）做同样特判：手牌空且抽/弃牌堆非空时抽一张。

**卡牌修正**：Flatten 在任意 Osty 攻击后给战斗区内每张 Flatten 追加一条 `SetThisTurn(0)`（Absolute/EndOfTurn，按次数累积）；进入战斗的 Flatten 在该回合已有 Osty 攻击时同样追加。RightHandHand 的 `AfterCardPlayedLate` 不校验打出的副本：任意一次能量值达到其 Energy 变量的打出，都把弃牌堆中的每个副本收回手牌（`HandleRightHandHand` 的同一条件）。

### 4.1 剩余卡牌与遗物

- **SentryMode**：`SentryModePower.BeforeHandDraw` 按层数生成 `SweepingGaze` token 入手；SweepingGaze 是 0 费 Ethereal/Exhaust 的 Osty 随机目标攻击。对照 `TurnStartPowerSupport` 的 SentryMode case。
- **CallOfTheVoid**：`CallOfTheVoidPower.BeforeHandDraw` 每次从角色池（`CharacterAll`，其过滤已排除 Basic/Ancient/Event 与不可生成卡）做一次 `TakeRandom(1)`，给生成卡补 Ethereal 后入手。Fetch 与 Delay 均已有 Blitz 定义，闭包不再有该缺口；CombatSolver 的 `flash-id-g-callofthevoid.json` fixture 固定根覆盖它。
- **Misery**：锁定目标的每个减益（层数、applier、`Target`）快照，攻击后按可命中敌人顺序克隆到其他敌人，每次克隆后排空 Power 数量变化队列；目标带临时 Power 时显式 `UnsupportedException`（缺少实例内部属性合并）。对照 `BespokeCardMirrors.MiseryOnPlay`。
- **Pagestorm**：`AfterCardDrawn` 只检查卡牌本地 Ethereal 关键字，命中后抽层数张；对照 `AfterCardDrawnMirrors.HandlePagestormPower`。
- **DrainPower**：单体攻击后从弃牌堆的**可升级**卡中等概率取 `Cards` 张并升级（`CardSelectionCardMirrors.DrainPowerOnPlay` 的过滤 + 完整 Fisher-Yates + `CombatCardSelection` 流）。`CombatPredictionSimulator.Upgrade` 在战斗正在结束时 no-op，所以击杀的 DrainPower 仍消耗随机数、但不升级任何卡；Blitz 在选完目标后按同一门控跳过写入。
- **BigHat**：唯一战斗内触发是玩家第 1 回合开始生成 2 张 Ethereal 角色牌，发生在根捕获之前；现有引擎也没有对应镜像，定义为空。

## 5. 第三幕牌组暴露的语义

这些机制由第三幕随机牌组的 K1/K2 差分覆盖；现有引擎位置逐条注在条目后。

- **Globe Head / GalvanicPower**：`AfterCardPlayedMirrors.HandleGalvanicPower` 的伤害目标是**打出卡牌的所有者**（`PreviewCard.Owner`），不是 Power 的所有者；Globe Head 持 Power 而玩家出牌，因此反伤落在玩家。`PowerDef` 的 `AfterCardPlayed` 以 `Limits.PlayerOwner` 为目标（Blitz 内所有卡都属于玩家）。
- **MagiKnight / DampenPower**：升级牌降到 0 级后仍会被读取或打出，因此 `RulesCompiler` 为根上的升级牌补 0 级规则；`DowngradeCard` 在 `AfterDowngraded` 后按 `PreservesVar0OnDowngrade` 恢复 Rampage/TheScythe 的累计伤害（现有引擎的 `ExtraDamageFromPlays`/`IncreasedDamage`）。克隆与变化/替换产生的是新对象，`DampenOriginalUpgrade` 清零；离开战斗牌堆的 Dampen 记录按退役处理（见[准入](archive/admission-and-verification.md)）。卡牌在出牌中被降级后又被恢复时，`AddCurrentCardVar`/`SyncCurrentPlayCard` 以 `Exec.PlayPosition` 的实时牌值为准（`Rampage`/`TheScythe` 在攻击后同步，现有引擎读的是可变 `CardModel`）。
- **Queen / ChainsOfBindingPower**：`BeforeCardPlayedMirrors.HandleChainsOfBindingPower` 跳过的是 `CardModel.IsDupe`，不是 `IsClone`；复制品在准入时已拒绝，克隆（Swift、AdaptiveStrike）必须照常设置束缚标记。Aux1 是原版按玩家回合统计的 `CardAfflictedEntry` 数：回合末只清打出标记与 Bound 苦难，Aux1 保留到下一个玩家回合开始再清零，不能提前在 `BeforeSideTurnEnd` 重置，否则回合末状态与 `ChainsOfBindingPredictionState` 不一致。
- **不宁提灯（UnsettlingLamp）**：现有引擎的 `ApplyPowerFromSource(..., cardSource: null)` 会把“当前卡牌来源”压成 null，灯的观察/翻倍都不生效。Blitz 的 `ApplyPowerWithoutCardSource`/`ApplyTemporaryStrengthWithoutCardSource` 在应用期间关闭 `CardPowerSourceActive`，Envenom/Concoct 的毒与 ReaperForm/Underworld 的 Doom、MonarchsGaze 的临时力量降层都走这两个入口；否则卡牌作用域内的非卡牌来源减益会被翻倍。
- **Vambrace**：`AfterCardPlayed` 用“触发卡的作用域序号”匹配；`OwnChoice` 弃置 Sly 牌触发的嵌套自动打出会完成自己的作用域并改写 `LastCardPowerSourceSerial`，因此 `PlayFromPlay` 在嵌套打出返回后恢复外层的序号。
- **Turret Operator / RampartPower**：`PersistentPowerSupport.TriggerRampart` 遍历 `EffectivePowers()` 且只按数量过滤；Living Shield 已离开名单时其 Rampart 行仍会派发。`RampartPass` 直接按 Power 表行顺序遍历，不使用会跳过离场 owner 的 `CollectListeners`。
- **Axebot / StockPower**：补货时 `StockAmount = 原 Stock - 1` 会改变 `RespawnCount = 2 - StockAmount`，进而改变 `MinInitialHp/MaxInitialHp` 的整个区间。规则表按 StockAmount 1 编译，`AfterDeath` 传入 `hpRangeShift = (1 - newStock) * 10`，`RollInitialHp` 在平移后的区间上做“避开名单同生命”的抽取；入场 Power 不再补加生命。
- **召唤物的初始行动**：`RegisterPendingInitialMonsterAi` 在状态机构造器已把初始 `MoveState` 记入 `StateLog` 时直接采用它（`NeedsInitialRoll=false`）。`MonsterRules.InitialMoveLogged` 记录该条件；敌方回合内生成的召唤物也在 `PrepareSpawnedMonster` 立即记录初始行动，而不是等 `PrepareMonsterMovesForNextRound`（回合可能在玩家死亡时提前结束）。
- **HellraiserPower**：`InfiniteAutoPlaysThisTurn` 存 `Power.Aux0`（条目优先，缺条目取模型内部数据）。`HittableEnemies.All(HpDisplay.IsInfinite())` 在空集合时为真（测试体复活窗口），非空时只有 Waterfall Giant 的蒸汽爆发重写为真；计数达到 9 后不再自动打出但仍自增，有限分支清零，`EndTurnBatch048` 在 owner 参与时清零。
- **攻击后抽牌的死亡结算**：`CorePowerSupport.ApplyCardPowers` 在镜像攻击之后、卡牌专属补偿之前结算敌人死亡。PommelStrike、DaggerThrow、FlashOfSteel、MinionStrike、PhotonCut、RocketPunch、SweepingBeam、CompileDriver、Ftl 在攻击与抽牌之间调用 `ApplyEnemyDeathPowers`，使抽牌时的 `HittableEnemies`/Hellraiser 与现有引擎一致。
- **GigantificationPower**：现有引擎的 `PowerAmountPredictionState` 在动作边界把模型数量写回；玩家在同一次动作中死亡时，`BeforeAttack` 已登记的层数会把被死亡清理清零的 Power 恢复。Blitz 在 `BeforeAttack` 标 `PowerFlags.PendingAmountState`，`HandlePlayerDeath`/`RemovePowersAfterDeath` 保留这些行，`Kernel.Step` 在稳定边界清标记。

## 6. 事件内容的角色无关机制

事件卡不属于任何角色的初始/奖励池，但会进入牌组并参与战斗；它们的结算落在通用管线。布局与投影见[状态布局](combat-state-layout.md#27-事件内容追加的卡牌值与动态类型)，准入与缺失能力见[准入](archive/admission-and-verification.md)。

- **动态卡牌类型。** `MAD_SCIENCE` 的原生 `CardModel.Type` 是保存属性 `TinkerTimeType` 的覆盖（`TargetType`、结果去向和出牌计数都随之变化）。规则表仍按卡牌 Id 冻结规范数值；结算与 `Scorer` 用 `CardDef.ResolveCardType`/`CardStats.HasDynamicType` 读实时类型。`Private` 低/高字节保存类型与骑手，`StateImporter` 从原版两个保存属性投影，非法 `None` 按 `UnrepresentableState` 拒绝。评分中该卡的 `LatentCardSetupValue`/`LatentTrait` 按 Power 类型与 `Curious` 骑手照 `CombatBeamSolver.StateEvaluation` 取 10/5。
- **实例挡格。** `TORIC_TOUGHNESS_POWER` 每个实例一行，`Amount` 是剩余回合、`Aux0` 是挡清零时的格挡量；卡与 Power 都参与 `HasIdentityContent`，因为两个实例可在同一 `AfterBlockCleared` 中先后移除。挡格量在原版是 `decimal`，投影要求非负整数，否则显式拒绝。
- **结果去向与下回合资源。** `REBOUND_POWER` 用 `ModifyCardPlayResultLocation`/`AfterModifyingCardPlayResultLocation` 把下一张牌的 Discard 改为 Draw/Top 并扣层；`HELLO_WORLD_POWER` 在 `BeforeHandDraw` 用 `AmountOnTurnStart` 次 `TakeRandom` 取 Common 角色牌；`FEEDING_FRENZY_POWER` 是 `TemporaryStrengthPower`，在持有者侧回合末退役；`WRAITH_FORM_POWER` 在持有者侧回合开始扣敏捷。
- **战斗外 Power 与成长收益。** `FORBIDDEN_GRIMOIRE_POWER`/`IMPROVEMENT_POWER` 的原生唯一钩子是 `AfterCombatEnd`，战斗内只保留计数行；`FORBIDDEN_GRIMOIRE` 的 50 长期资源照现有引擎记入计数器，并在施加 Power 后记录 `ForbiddenGrimoire` 成长收益；`MAD_SCIENCE` 的 Improvement 骑手只在 `ImprovementPower` 实际增加时记录 `MadScience` 成长收益（ST 的打包计数器）。
- **任务/令牌卡。** 四张 Quest 卡由事件加入牌组、按普通牌洗入战斗牌堆，原生不可打出且只有战斗外钩子，因此定义为空效果；`LUMINESCE` 是 `RadiantPearl` 生成的 Token，只获得能量。
- **事件怪物。** `FAKE_MERCHANT_MONSTER`/`MYSTERIOUS_KNIGHT` 的招式效果照 `MonsterMoveEffects` 移植；`ARCHITECT`/`BATTLE_FRIEND_*` 只有 `NOTHING_MOVE`。战损假人的 `BATTLEWORN_DUMMY_TIME_LIMIT_POWER` 最后一层对应 `SearchBoundaryReason.EventDefeat`：现有引擎只设置 `_battlewornDummyTimedOut` 并在 `CreatureCmd.Escape` 之前返回，Blitz 用 `Outcome.EventDefeat` 表示同一结局、保留 Amount 1 并跳过后续阶段。
