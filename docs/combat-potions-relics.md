# Sts2Solver 内核语义：额外回合与药水/遗物

[返回 Sts2Solver 入口](README.md)

文中的“现有引擎”指导出冻结快照时 CombatSolver 的原引擎（`a62f1850c8586e5951e222eecb0dd34231e76d8a`）；这些语义已冻结在 `tests/Golden/data`，以后以快照和实机证据为准。

本文件承接 [内核语义](combat-semantics.md) 第 14 节末尾的指针：额外回合管线、9 瓶药水与 34 个遗物定义，以及 HISTORY_COURSE 与 Pen Nib 的锁存身份。通用执行上下文、Hook 派发、出牌、伤害与回合流程仍以那份文档为准。

## 1. 额外回合与药水/遗物

来源：`Search/CombatBeamSolver.Expansion.Replay.cs:1038,1220-1232`、`Search/CombatBeamSolver.RoundTransition.cs:23-28`、`Engine/InCombat/Mirrors/HookMirrors.ExtraTurn.cs`、`Search/SimulatedCombatState.Relics.cs` 与 `Search/SimulatedCombatState.ReactiveRelics.cs`。

**额外回合。** 玩家回合末的阶段二与敌人死亡结算之后、敌人回合之前，`Kernel.ShouldTakeExtraTurnPass` 按监听顺序询问 `ShouldTakeExtraTurn`，第一个返回真者生效（`AmbergrisPower`、`PaelsEye`）。生效时：

1. 先跑 `AfterRelicRoundPass`（`RecordRelicRoundDamage` 的位置；EmotionChip 在此把“本回合失血”翻到“上一回合”）；
2. 再在固定监听快照上跑 `AfterTakingExtraTurnPass`（Ambergris 自减一层，PaelsEye 置已用）；
3. **跳过整个敌人回合**与 `PrepareMonsterMovesForNextRound`，`AdvanceRoundPlayerStart(takingExtraTurn: true)`：`RoundNumber` 不加一，玩家回合数加一，`TriggerSideTurnStart` 的 Rampart 只在非额外回合执行。
4. `AdvancePlayerTurn` 只清空 `_unblockedDamageThisTurn` 缓存，旧引擎随后从不可变根历史重算 `HasLostHpThisTurn`；Sts2Solver 在额外回合用 `Rules.RootLostHpMask`（根捕获时每个 owner 的答案）恢复这些标记，普通回合换轮时全部清零。根历史之外的预测期伤害不再计入，与旧引擎一致。

**药水（9 瓶）。** `AMBERGRIS`（`MaxHp * HealPercent / 100` 治疗 + `AmbergrisPower` 1）、`BONE_BREW`（`SummonOsty(Summon)`）、`COSMIC_CONCOCTION`（去重无色牌、升级后逐一入手）、`CUNNING_POTION`（升级 Shiv）、`FOCUS_POTION`、`POISON_POTION`、`POTION_OF_CAPACITY`（`AddOrbSlots(Repeat)`）、`POTION_OF_DOOM`、`POT_OF_GHOULS`（Soul 入手）。逐条对应 `Prediction/PotionOnUseSupport.Use` 与 `Mirrors/Potions/OnUse/CardGenerationPotionMirrors.Generate`。

**遗物。** 17 个有原引擎引用的遗物全部有定义；`PAELS_LEGION` 的冷却状态在遗物 `Counter`，触发中的 `CardPlay` 只在出牌期存在（`Exec.PlayIdentity`）；`MUSIC_BOX` 的“本回合已用”在遗物 `Counter`，被复制的攻击牌由同一 `PlayIdentity` 配对；`EMOTION_CHIP`/`PAELS_EYE` 的 `StatefulRelicState` 投影到 `Counter`/`Aux`；`BOOKMARK` 使用新的 `HookMaskExt.AfterFlush`（回合末弃牌后、清理前）；`BIIIG_HUG` 用 `AfterShuffle` 生成 Soot；`BONE_FLUTE` 在 Osty 攻击后给格挡；`METRONOME` 按充能次数造成 Unpowered 伤害；`THROWING_AXE` 每场第一次出牌重放一次；`SPIKED_GAUNTLETS` 加最大能量且能力牌费用 +1；`UNDYING_SIGIL` 对将死敌人打向玩家的强化攻击乘 `DamageDecrease`；`CROSSBOW`/`CHOICES_PARADOX` 在玩家侧回合开始用冻结卡池生成；`TOASTY_MITTENS` 用回合开始选择弃一张手牌再加力量。无原引擎镜像的 20 个：`BOOK_REPAIR_KNIFE`、`INFUSED_CORE`、`LUNAR_PASTRY`、`PHYLACTERY_UNBOUND`、`SEAL_OF_GOLD`、`SNECKO_EYE`、`WHISPERING_EARRING` 有战斗内行为（Doom 治疗、闪电球值、星能、Osty 召唤、金币换能量、抽牌数、最大能量），其余 10 个只在战斗外或根之前生效（`BING_BONG`、`DARKSTONE_PERIAPT`、`DATA_DISK`、`DELICATE_FROND`、`DIAMOND_DIADEM`、`DIVINE_DESTINY`、`FAKE_SNECKO_EYE`、`FUNERARY_MASK`、`NINJA_SCROLL`、`RADIANT_PEARL` 中的每个定义都注明原版 Hook；`TWISTED_FUNNEL` 属于前 17 个）。`BYRDPIP`（原版宠物，现有引擎不模拟非 Osty 宠物）与 `DEPRECATED_RELIC`（Rarity None，不可获得）仍无定义，准入按 `UnsupportedRelic` 拒绝；`HISTORY_COURSE` 已实现（见下）。

**HISTORY_COURSE 与 Pen Nib。** HISTORY_COURSE 只读取上一玩家回合最后一张攻击牌的完整卡值：`SimulatedCombatState._lastAttackThisTurn` 投影为 `Battle.HistoryCourseCard`（唯一的跨回合卡值槽，`Battle` 当前 12,696 字节），`AutoPrePlay` 在 Mayhem 之后复制并自动打出；dupe 的结果去向是移出战斗，不写回本回合最后攻击。Pen Nib 的锁存保存触发它的 `Exec.PlayIdentity`（`Relic.Aux`），嵌套自动打出的 Sly 牌不会继承加倍；`Rules.PenNibRelicIndex` 在稳定边界清除未完成出牌留下的锁存。两者的投影与 K4 规则见[准入](archive/admission-and-verification.md)与[状态布局](combat-state-layout.md#28-结局与跨回合卡值槽)。

**LavaLamp 与 Kusarigama。** LavaLamp 的 `AfterDamageReceived` 只记录本场是否受到可格挡的未格挡伤害（`TookDamageThisCombat`），Sts2Solver 用 `Relic.Counter` 携带并在写回时写回该布尔。Kusarigama 的 `AfterSideTurnEnd` 在原生战斗进入 ending（含 `PendingLoss`）时不会派发；Sts2Solver 在 `PlayerTurnEndPhaseTwo` 的 `RelicPass(AfterSideTurnEnd)` 前检查 `IsPendingLoss`，避免玩家在玩家回合结束阶段死亡时错误清零 `Relic.Counter`，终局写回再按计数设置 `DisplayAmount`/`Status`。

**FurCoat。** 命中地图坐标在战斗外固定，Bridge 把它冻结为 `Rules.FurCoatSpawnHpOne`；`Kernel.AddCreatedMonster` 在入场 Power 之后、`PreparePredictedMonster` 之前把新敌人的当前生命设为 1（`MonsterSpawnSupport.ApplyCreatureAddedRelics`）。

**ChoicesParadox / ToastyMittens 的开局选择。** 两者的原版钩子在玩家第 1 回合开始、根捕获之前；搜索根里的结果是已选中的牌与已弃的牌。Sts2Solver 在后续回合（`AfterPlayerTurnStart`）按同一 `TurnStartChoiceSupport` 协议发出 `GenerateToHand` / `Exhaust` 请求，由结束回合动作携带选择。
