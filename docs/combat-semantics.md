# Blitz 内核语义

[返回 Blitz 入口](README.md)

本文规定 Blitz 内核的执行模型。文中的“现有引擎”指导出冻结快照时 CombatSolver 的原引擎（`a62f1850c8586e5951e222eecb0dd34231e76d8a`）；这些语义已冻结在 `tests/Golden/data`，以后以快照和实机证据为准。

下文的步骤顺序摘自该引擎源码；路径缩写均指该提交的 CombatSolver 源码，不指向本仓库。两者冲突时以 `tests/Golden/data` 的快照和差分测试结果为准，并回头修正本文。

路径缩写（CombatSolver `a62f1850`）：`S/` = `src/Engine/InCombat/Simulation/`，`M/` = `src/Engine/InCombat/Mirrors/`，`E/` = `src/Engine/InCombat/Extensions/`，`Q/` = `src/Search/`，`P/` = `src/Prediction/`。

## 1. 执行上下文

内核入口是静态方法，签名形如 `StepResult Step(ref Battle battle, in PlayerAction action, Rules rules)`。执行期间的临时信息放在栈上的 `Exec`（`ref struct`，持有 `ref Battle`、`Rules`、选择游标、当前出牌上下文），不进入状态。

- 动作执行到稳定边界才返回，返回值是 `Completed`、`NeedsChoice`（附选项规格）或 `Terminal`。
- 执行中途不允许分配托管对象；内容代码只读写 `Battle` 和栈上值。
- 未知内容 Id、未实现的 Hook 组合抛 `UnsupportedException`；容量溢出抛 `CapacityException`。两者都使本次 Blitz 求解整体作废。
- `EndTurn` 动作可拆成“玩家回合结束 + 敌方回合”和“玩家回合开始”两段。`Kernel.StepEndTurnSharingPrefix` 在完整执行一次 `EndTurn` 时顺带返回停在 `AdvanceRoundPlayerStart` 之前的 `EndTurnPrefix`；`Kernel.StepEndTurnFromPrefix` 从该不可变快照继续执行后半段。前缀包含当时的 `Battle` 和后半段可能读取的全部 `Exec` 瞬态（力量变化队列、伤害/击杀/溢出暂存、出牌身份与来源序号、手牌/生成选项、Monologue、Vambrace、Lamp 等）；选择游标、效果深度与嵌套 Sly 标记范围在该点必须为空，捕获时逐项断言。额外回合或前半段已请求选择时明确返回不可共享（`EndTurnPrefixStatus`），调用方仍走完整的 `Step`，不静默近似；前半段已在玩家回合开始前结束时仍会捕获快照，续跑跳过玩家开始半段、只执行 action 收尾（与完整 `Step` 相同）。`tests/KernelChecks` 的 `ExecCoverage` 合同用反射把 `Exec` 的每个实例字段归入“快照 / 捕获断言为空 / 入口重置 / 单独处理”四类之一，并校验快照字段集合与 `ExecContinuation` 完全一致；新增字段未归类会直接失败。

## 2. 内容定义

每类内容一个抽象基类：`CardDef`、`PowerDef`、`RelicDef`、`PotionDef`、`MonsterDef`、`OrbDef`、`EnchantmentDef`、`AfflictionDef`。具体内容是 `sealed` 子类，覆写需要的 Hook 虚方法。

- 每个定义用特性声明原版 Id，例如 `[ContentId("BASH")]`。注册表在首次使用时扫描定义，分配连续的 `ushort` 内部 Id，并按"覆写了哪些 Hook"建立每个 Hook 的监听位图，派发时跳过非监听者。
- 动作 Hook 按快照派发，但每个监听者在调用前重新检查玩家是否仍可触发 Hook：`BattleFlags.PlayerHooksActive` 清零后，玩家拥有的 Power/Relic/Potion/Card/Affliction/Enchantment/Orb 监听被跳过；死亡阻止/复活期间标志保持，`BeforeDeath`/`AfterDeath` 不受影响。这与原版 `CombatState.Contains` 的 `Owner.IsActiveForHooks` 过滤一致。各角色专属的内核机制（弃牌与 Sly、充能球、星能与锻造、Osty 与 Doom）见 [角色机制](combat-characters.md)。`HookMask` 的 64 位已用满，此后新增的 Hook 放在 `HookMaskExt`，两者用同一反射计算（`ContentDef.Hooks`/`HooksExt`），`HookDispatch.CollectListeners` 对两种掩码各有一个重载，收集顺序相同。
- 定义对象不可变、无字段状态；所有可变值存在 `Battle` 里。
- 数值从 `Rules` 读取：`rules.Card(id, upgrade).Damage`、`rules.Monster(slot).Move(moveId).Hits` 等。只有原版数据里不存在、写在原版代码中的常量才允许出现在定义里，并注释原版出处。
- 原版 Id 到内部 Id 的映射在 Bridge 建立；内核只把原版 Id 当元数据字符串，不引用原版类型。

## 3. Hook 派发

### 3.1 监听顺序

监听列表按以下顺序遍历（来源 `Q/SimulatedCombatState.cs:1903-2064`）：

1. 根前缀：根捕获时原生 `IterateHookListeners` 的顺序，去掉卡牌、苦难、附魔和球；只保留仍在场的生物与药水。玩家侧新生物插在敌人块之前；敌人块（先 Power，后怪物本身）按当前槽位重新分组；新药水追加在前缀末尾。根前缀的分组顺序存在 `Rules`。
2. 球：每个玩家的球，按队列顺序。
3. 卡牌：手牌、抽牌堆、弃牌堆、消耗堆、打出区，按堆内顺序；每张卡之后紧跟它的苦难，再跟它的附魔。

Power 顺序见 [状态布局](combat-state-layout.md#23-power-顺序)。

### 3.2 派发规则

- 按列表顺序逐个调用；遇到待处理选择时停止（与 `M/HookMirrors.cs:1439` 一致）。
- 多阶段 Hook 依次跑 Early、普通、Late。`AfterCardPlayed` 的 Late 阶段重新读取监听列表。
- 修正类 Hook（`ModifyDamage*`、`ModifyHpLost*`、`ShouldClearBlock` 等）不得改写状态：预报与评估会在只读语义下调用它们。需要随攻击捕获的值在 `BeforeAttack` 中记录（例如活力，对应 `VigorPowerMirrors.BeforeAttack`）。
- 战斗正在结束时，战斗监听者的 Hook 返回空，`AfterCardPlayed`、`AfterBlockBroken`、`AfterAttack` 除外；伤害和死亡类的跑局监听者不受此限（`M/HookMirrors.cs:1351`）。`TryModifyEnergyCostInCombat`（普通与 Late）、`ModifyCardPlayResultLocation`、`ModifyCardPlayCount`、`ModifyXValue` 也通过 `IterateCombatHookListeners` 遍历，因此 Blitz 在 `IsOverOrEnding` 时跳过它们；费用查询和评估在结束状态读到的是未修正费用。
- 费用 Hook 是纯查询：普通阶段按战斗监听顺序跑一遍，再按同一顺序跑 Late 阶段；调用方最后 `(int)` 截断并钳制到 ≥ 0（`M/HookMirrors.cs:486-530`）。`AreCardsFree` 分支在 Blitz 中没有内容。
- `AfterPowerAmountChanged` 不在施加 Power 时立即派发：`ApplyWithBeforeApplied` 只把 `(PowerId, Delta, Applier)` 放进栈上的待处理队列，之后在现有引擎的同一位置调用 `ResolvePowerAmountChanges` 排空（卡牌补偿结束、`CompleteCardPlayEffects`、回合开始 `BeforeHandDraw` 后、`TriggerSideTurnStart` 末、玩家/敌方回合结束 `TriggerRegular` 后、敌人行动结束、敌人死亡效果末、药水使用后、Dominate 的 Vulnerable 与 Strength 之间）。排空时先把当前批次复制到栈上的 `drained` 并清空队列，再逐条派发；嵌套排空（Vicious 抽到 Strike 后 Hellraiser 自动打出）因此只能看到处理器新追加的变化，与现有引擎 `DrainPowerAmountChanges` 的先整批取走一致。派发只遍历当前 Power（现有引擎 `EffectivePowers()`），按监听顺序对每条变化调用；Vicious 在“Delta > 0 且变化的是 Vulnerable 且施加者是持有者”时抽牌。旧引擎**没有**通用 `AfterApplied`；只有 PhantomBlades/Juggling/Feral 等个别初始化，Blitz 在对应定义里另行处理。
- 现有引擎的 `ModifyPowerAmountForRelics` 在 Artifact 检查之前依次跑 `BeforePowerAmountChanged`、`ModifyPowerAmountGiven`（SneckoSkull）与 `TryModifyPowerAmountReceived`（RuinedHelmet）。Blitz 用 `HookMaskExt.ModifyPowerAmountGiven` 实现给予侧的纯查询，再实现接收侧的 `TryModifyPowerAmountReceived` 与 `AfterModifyingPowerAmountReceived`；修正阶段不得改写状态，RuinedHelmet 的“本场已用”标记在 After 阶段写入。
- 卡牌从战斗外进入战斗时，`SimulatedCombatState.CardEventHistory.AfterCardEnteredCombat` 先遍历卡牌拥有者的遗物（GhostSeed），再执行卡牌自身的 `AfterEnteredCombat`（Stomp、Pinpoint 等费用反应）。Blitz 的 `Kernel.Generation` 的进入战斗路径按同一顺序派发。本回合攻击完成计数在 `AfterCardPlayed` 派发之前写入（对应现有引擎在 `AfterCardPlayed` 前记录 `CardPlayFinished`），因此 Calamity 在攻击的 `AfterCardPlayed` 中生成的 Stomp 会把该次攻击计入 `AfterCardEnteredCombat` 的初始减费；Finisher 等在自己 `OnPlay` 内读取计数的牌仍不会把本次出牌算进去。
- `AmountOnTurnStart` 只有 `DrawCardsNextTurnPower`、`SummonNextTurnPower` 与 `HelloWorldPower` 会被结算读取（`PowerLifecycleSupport.SemanticallyRelevantAmountOnTurnStart`）。其余 Power 的该字段只反映原生模型数量，而镜像扣层（`PowerAmountPredictionState`）要到动作边界才同步到模型；Blitz 立即扣层，因此这些 Power 的 `AmountOnTurnStart` 在导入与回合开始快照时都归零，不复制一个只存在于原生模型里的滞后值。
- 只有原版监听者时，旧引擎对回合阶段 Hook、Power 反应、最大能量与抽牌数、`BeforeHandDraw` 使用"先全部 Power、再全部遗物"的批量顺序，不按逐监听者顺序。Blitz 在相同位置用相同的批量顺序。这一偏差以旧引擎为准。

### 3.3 Hook 目录

内核为下列 Hook 提供派发点，名称与原版一致；内容需要新的 Hook 时在内核增加派发点并补差分测试：

- 出牌：`TryModifyEnergyCostInCombat`（含 Late；Corruption、FreeAttackPower、FreeSkillPower、FreePowerPower）、`TryModifyStarCost`（BrilliantScarf、VoidFormPower；纯查询，只在原费用非负且战斗未结束时派发）、`AfterStarsSpent`（先遗物后 Power 的固定顺序）、`AfterStarsGained`、`ShouldPlay`、`BeforeCardPlayed`、`ModifyCardPlayCount` / `AfterModifyingCardPlayCount`（OneTwoPunchPower、DuplicationPower）、`ModifyCardPlayResultLocation` / `AfterModifyingCardPlayResultLocation`（Corruption；Nostalgia 还返回 `Draw` 的 `Top` 位置）、`AfterCardPlayed`（含 Late）、`AfterEnergySpent`、`AfterStarsSpent`、`AfterCardExhausted`、`AfterCardDiscarded`、`AfterHandEmptied`。
- 卡牌目标：`CardDef.ResolveTargetType` 覆写原生 `CardModel.TargetType`。规则表冻结规范目标；`Shiv` 在持有 `FanOfKnivesPower` 时返回 `AllEnemies`，合法动作枚举、自动打出目标解析与卡牌攻击都读这个动态值。
- 攻击与伤害：`BeforeAttack`、`ModifyAttackHitCount`、`AfterAttack`、`ModifyDamageAdditive`、`ModifyDamageMultiplicative`、`ModifyDamageCap`、`BeforeDamageReceived`、`ModifyHpLostBeforeOsty`（含 Late）、`ModifyHpLostAfterOsty`（含 Late）、`AfterModifyingHpLostAfterOsty`、`AfterBlockBroken`、`AfterCurrentHpChanged`、`AfterDamageGiven`、`AfterDamageReceived`（含 Late）。
- 格挡：`BeforeBlockGained`、`ModifyBlockAdditive`、`ModifyBlockMultiplicative`、`AfterModifyingBlockAmount`、`AfterBlockGained`、`ShouldClearBlock`、`AfterBlockCleared`。
- 死亡：`BeforeDeath`、`ShouldDie`（含 Late）、`AfterDeath`、`AfterPreventingDeath`。
- 已有派发点（其余见本节列表）：`BeforeDamageReceived`（`TryDamageTarget` 中修正后、格挡前，不受战斗结束闸门限制）、`BeforeSideTurnEnd` 三轮（玩家回合结束第一阶段，受闸门限制，参与者以位集合传入）、`ShouldClearBlock`、`ModifyMaxEnergy`、`ModifyHandDraw`（含 Late）与第 7 节的 Power/遗物阶段方法。
- 回合：`BeforeSideTurnStart`、`AfterSideTurnStart`、`AfterEnergyReset`（含 Late）、`ModifyMaxEnergy`、`BeforeHandDraw`、`ModifyHandDraw`（含 Late）、`AfterPlayerTurnStart`、`AfterAutoPostPlayPhaseEntered`（阶段切到 AutoPostPlay 后、切到 End 之前，卡牌与 Power 监听者；HowlFromBeyond、StampedePower）、`BeforeSideTurnEnd`（VeryEarly、Early、普通）、`OnTurnEndInHand`、`AfterSideTurnEnd`（含 Late）、`AfterSideTurnEndLate`（`HookMaskExt`；刷新监听快照后派发，DisintegrationPower）、`ShouldTakeExtraTurn`。
- 抽牌与牌堆：`ShouldDraw`、`AfterCardDrawn`（含 Early，以及被抽卡自身的 Hook；HellraiserPower 在 Early 自动打出刚抽到的 Strike）、`ModifyShuffleOrder`（稳定洗牌后、写回抽牌堆前；Slither 的抽牌随机费用走 `AfterCardDrawn`）、`AfterShuffle`（TheAbacus）、`AfterCardChangedPiles`。`AfterShuffle` 在洗牌计数 +1 后、战斗未结束时派发，监听者为全部战斗监听者。
- Power：`TryModifyPowerAmountReceived` / `AfterModifyingPowerAmountReceived`（RuinedHelmet；UnsettlingLamp 按触发卡的出牌来源逐卡翻倍，触发卡与已翻倍的临时 Power 类型只存在 `Exec`，见 [状态布局](combat-state-layout.md) 2.5）、`AfterPowerAmountChanged`（Shroud、SleightOfFlesh、Vicious；Blitz 目前只有 Vicious、PossessStrength/PossessSpeed 定义；派发携带被修改 Power 的目标 owner，因为原版读取的是 `power.Owner`）。**实例型 Power**（`PowerInstanceType.Instanced`）每个原生实例占 Power 表一行，`(owner, id)` 相同的行按出现序区分；阶段遍历与 Hook 快照都保存 occurrence，`FindPowerOccurrence` 精确回指该行（Automation、Panache、RollingBoulder、TheBomb、Gigantification、Monologue、Orbit、Nightmare）。
- 数值与遗物：`ModifyXValue`（ChemicalX，位置与 `CardModel.ResolveEnergyXValue` 一致）、`ShouldPlayerResetEnergy`（IceCream）、遗物 `AfterCardEnteredCombat`（GhostSeed）。
- 生成卡与额外结算时机：`AfterCardGeneratedForCombat` 在 `AddGeneratedCard` 插入牌堆并派发 `AfterCardEnteredCombat` 之后按监听顺序派发，参数是**卡牌数组的绝对下标**（牌堆内下标只用于 `AfterCardEnteredCombat`）与生成者 owner（玩家或 `NoOwner`，对应现有引擎的 `Player? Creator`；怪物及其 Power 生成的牌为 `NoOwner`）；Aeonglass 用它把新生成的 Wither 直接提升到当前 `WitherUpgradeCount`。`Rampart` 只在玩家侧非额外回合的回合开始、`OwnerAfterSideTurnStart` 之后执行（`PersistentPowerSupport.TriggerRampart`）。`HasKeyword` 在稳定边界上查询本地关键字后，还会为持有 `HexPower` 的玩家的 Hexed 卡补上 Ethereal（原版 `TryModifyKeywordsInCombat` 是惰性查询，不写入卡牌状态）。
- 球：`AfterOrbChanneled`、`AfterOrbEvoked`、`ModifyOrbPassiveTriggerCounts`、`ModifyOrbValue`；被动与激发、回合末/回合开始触发见[角色机制](combat-characters.md)第 2 节。

### 3.4 苦难与附魔

卡牌上的苦难与附魔是 `Card` 的值（`AfflictionId/Amount`、`EnchantmentId/Amount/Status/Private`），与现有引擎的 `PredictedCard.Preview.Affliction/Enchantment` 同形；导入时只接受已登记的定义，其余按 `UnsupportedAffliction`/`UnsupportedEnchantment` 拒绝。定义类是 `AfflictionDef`（`IsStackable`、`CanAfflictCardType`、`CanAfflictUnplayableCards`、`OnPlay`）与 `EnchantmentDef`（`EnchantDamageAdditive/Multiplicative`、`EnchantBlockAdditive/Multiplicative`、`EnchantPlayCount`、`OnPlay`，以及通用的 `AfterCardPlayed` 等监听 Hook）。

- **修正类 Hook。** 现有引擎在 `HookMirrors.ModifyDamage`/`ModifyBlock` 中先调用来源卡的附魔修正，再跑普通监听者；Blitz 在 `HookDispatch.ModifyDamage/ModifyBlock` 的同一位次调用附魔定义。`ModifyDamage` 的附魔修正只用卡值，是纯查询。`ModifyBlock` 的溢出口径与普通修正相同。
- **出牌次数。** `CardModel.GetEnchantedReplayCount` 先应用附魔的 `EnchantPlayCount`，再进入 `ModifyCardPlayCount`；Blitz 的 `EnchantedReplayCount` 同时供出牌管线、评估的 `ReplayPotentialValue` 与 `ActionTranslator` 使用。
- **卡牌尾部。** 每次打出在卡牌效果与 `OwnChoice` 之后、`AfterCardPlayed` 之前依次执行附魔 `OnPlay`、苦难 `OnPlay`；持有者死亡或出现待处理选择时中止，与 `CardTailContinuation` 的 Enchantment → Affliction → Finish 顺序一致。
- **苦难归一化。** 现有引擎不直接逐 Hook 施加/清除苦难，而是在 `SimulatedCombatState.NormalizeCardAfflictions` 里按当前 Power 归一化：Hex/Tangled/Ringing 先按 Power 数量施加或清除；随后 `NormalizePowerAfflictions` 处理 Vital Spark 的 Tainted（数量变化才回写、Power 归零才清除）、Smoggy 的 Smog（本侧回合已打出技能时给新进场技能）与 Galvanic 的 Galvanized（新进场 Power 牌）。Blitz 的 `Kernel.NormalizeCardAfflictions` 逐条照抄，并在现有引擎的同一位置调用：`AfterCardEnteredCombat` 末、卡牌补偿效果末、每个动作结算、玩家回合末阶段一/阶段二、玩家回合开始末、每个敌人行动末。`PowerAfflictionChecked` 对应 `PredictedCard.HasCheckedPowerAfflictionEntry`：根导入的卡视为已检查，根之后生成的卡在下一次归一化时检查一次。`SkillPlayedThisSideTurn` 对应 `_skillsPlayedThisTurn`，在技能牌完成后置位、在该侧回合末清零（与持久化的 `SkillCardsPlayedThisTurn` 不同）。
- **施加/移除路径。** `ApplyAffliction` 对应 `CardCmd.Afflict` 的 `CanAfflict` 与同类型叠加；`ClearAffliction(s)` 对应 `ClearAffliction`。Vital Spark 的 Tainted 层数同步使用导入的 `LastNormalizedVitalSparkAmount` 计数器（`int.MinValue` 表示 null），首次导入不回写，保留实机战斗开始时可能叠高的层数。
- **四个附魔的派发位置。** `Slither` 在 `AfterCardDrawn` 中按 `CombatEnergyCosts.NextInt(4)` 给刚抽到的手牌追加绝对本场费用（`AfterCardDrawnMirrors.HandleSlither`）；`SlumberingEssence` 在玩家回合末清手牌前的 `BeforeFlush` 给每张自身手牌追加 `WhenPlayed` −1（`EnchantmentLifecycleSupport.BeforeFlush`）；`PerfectFit` 在非初始洗牌的 `ModifyShuffleOrder` 中把自己的牌移到合并列表最前（`ModifyShuffleOrderMirrors.HandlePerfectFit`）；`Imbued` 在玩家第 1 回合的自动预打出阶段按 `AllCards` 顺序自动打出（`EnchantmentLifecycleSupport.TriggerAutoPrePlay`，在 Mayhem 之后）。`Imbued` 的快照与显式边界见[准入](archive/admission-and-verification.md)。
- **本节涉及的 Power 与怪物。** 当前实现 Plow、Ringing、Tangled、Smoggy、Vital Spark、Tainted、Hex 七个 Power，以及 Ceremonial Beast、Vine Shambler、Infested Prism、Living Fog、Gas Bomb 的移动效果。Living Fog 的 BLOAT 在攻击前生成 Gas Bomb（`MonsterDef.BeforeMove`），Gas Bomb 的 EXPLODE 攻击后自毁。MysticLighter 是遗物定义：带附魔卡牌的 powered attack 加 `Damage`。Hexed 的 Ethereal 由上面的 `HasKeyword` 惰性提供；HexPower 被移除或施加者死亡后不再提供，卡牌本地关键字不变。


## 4. 出牌管线

来源：`Q/SimulatedCombatState.cs:1552`、`S/CombatPredictionSimulator.Card.cs:122-330`、`S/CombatPredictionSimulator.CardExecutionContinuation.cs:29-127`、`S/CombatPredictionSimulator.CardTailContinuation.cs:16-125`、`P/CorePowerSupport.cs:21-407`。

1. **可否打出**：Enthralled 锁 → Unplayable 关键字 → 能量费用 → 星能费用 → `ShouldPlay` → `CardIsPlayable` 镜像 → Smog 苦难。能量费用 = 基础费用 → 本地修饰 → `TryModifyEnergyCostInCombat` 普通阶段 → Late 阶段（战斗结束时两阶段都跳过）；钳制 ≥ 0。费用 Hook 接收卡牌当前牌堆，供 FreeAttackPower 判断手牌/打出区；评估侧读取本地修饰（`CostModifiers.Local`/`All`）而不读取费用 Hook。X 费等于当前全部能量。`ShouldPayExcessEnergyCostWithStars` 为真时超额能量按 2:1 用星能支付。
2. **支付**：重算两种费用和超额换算；记录 X 值与本次星能消耗；扣能量 → `AfterEnergySpent`；扣星能 → `AfterStarsSpent`。
3. **进入打出区**：卡从手牌移到打出区（不触发 `AfterCardChangedPiles`）；确定基础去向（复制品或能力牌 → 无；下次打出时消耗或带消耗关键字 → 消耗堆；其余 → 弃牌堆；个别卡牌覆写）；`ModifyCardPlayResultLocation`，随后只对改变去向的监听者派发 `AfterModifyingCardPlayResultLocation`（Corruption 把 Skill 改成消耗；改变“移出战斗”的基础去向显式拒绝）；打出次数 = 附魔重放 + 1 → `ModifyCardPlayCount` → `AfterModifyingCardPlayCount`（只对改变次数的监听者；OneTwoPunch/Duplication 在此递减）；持有者已死则结束。
4. **每次打出**：战斗已结束则停止；`BeforeCardPlayed`；历史计数；执行卡牌效果；结算卡牌自身选择。
5. **卡牌效果顺序**：先执行旧引擎镜像部分（推断配方或专门镜像，即攻击、格挡、抽牌），再执行补偿部分（`CardEffectSpecRegistry` 与 `CorePowerSupport.ApplyCardPowers` 里的 Power、资源、生成效果）。卡牌可以覆写 `ResultLocation` 来复刻 `CardResultLocationMirrors` 的 `GetResultLocationForCardPlay`（ShiningStrike 回抽牌堆顶、ParticleWall 回手牌）；基础去向仍由规则表类型与关键字决定。**补偿效果总在镜像效果之后**，这是旧引擎的实际顺序。现有引擎在 `CorePowerSupport.ApplyCardPowers` 里先执行 `CardOnPlaySupport.Apply`，随后立即 `ApplyEnemyDeathPowers`，最后才进入该卡的专属补偿 `switch`；因此“镜像攻击击杀会生成新敌人的怪，再把 Power 施加到当前敌人”的卡（Thunderclap）必须在专属补偿前结算死亡。Blitz 的对应定义在镜像攻击与专属补偿之间调用 `ApplyEnemyDeathPowers`，`PlayFromPlay` 还在整张卡的 `OnPlay` 之后、`OwnChoice` 之前补一次死亡结算。
6. **收尾**：持有者已死则中止；附魔 OnPlay → 苦难 OnPlay；`AfterCardPlayed` 普通阶段 → 重新读取监听列表后跑 Late 阶段（战斗正在结束也照常触发）；`ResolvePowerAmountChanges`（`CompleteCardPlayEffects`）；生命周期计数。
7. **去向**：仍在打出区的卡按去向处理（无 → 移出战斗；消耗 → `AfterCardExhausted`；否则加入目标牌堆）；不在嵌套效果中时触发 `AfterHandEmptied`；清理费用与目标。
8. **动作边界**：结算敌人死亡效果（`ApplyEnemyDeathPowers`）→ 胜负判定 → 处理被请求的回合结束。

药水（`Q/CombatBeamSolver.Expansion.Replay.cs` 的 `UsePotion` 分支与 `P/PotionExecutionSupport.cs`）：清空槽位（使用记录只供搜索评分）→ `BeforePotionUsed` → 药水效果（`PotionDef.OnUse`，对应 `PotionOnUseSupport.Use` 的分支；自身药水作用于玩家）→ 药水选择 → 玩家存活时 `AfterPotionUsed` → 敌人死亡效果；自动药水 `FairyInABottle` 的复活在 `AfterPreventingDeath` 里同样先 `AfterPotionUsed`（`FairyInABottleMirrors`），再走死亡效果；之后与出牌相同：洗牌计数、胜负判定、处理被请求的回合结束。药水的目标类型与变量（含伤害、格挡变量的 ValueProp）从规范药水编译进规则表；合法目标按 `TargetsForPotion`（任一敌人逐个可命中敌人；自身/任一玩家在玩家存活时无目标；全体敌人与无生物目标无目标）。

### 4.1 自动打出

来源：`S/CombatPredictionSimulator.AutoPlay.cs:21-73`、`S/CombatPredictionSimulator.AutoPlayContinuation.cs`。

自动预打出顺序是 `Bombardment` → Mayhem 的计划自动打出 → HistoryCourse 的上一回合最后攻击复制品 → 第 1 回合的 Imbued 附魔卡。WhisperingEarring 是计划来源，但没有战斗内效果，只保留最大能量。

`Kernel.AutoPlay` 与 `ManualPlay` 共用 `PlayFromPlay`（结果牌堆、每次打出、`BeforeCardPlayed`、效果、`OwnChoice`、`AfterCardPlayed` 普通与 Late、收尾），只替换资源与目标两步：

1. 战斗结束/正在结束或拥有者死亡时不打出。卡牌不在任何牌堆时先加入打出区（触发 `AfterEnteredCombat`）。
2. 不可打出关键字、`ShouldPlay` 拒绝或无法解析目标时，`MoveToResultPileWithoutPlaying`：先加入打出区，再按 `ExhaustOnNextPlay`/消耗关键字进入消耗堆，否则进入弃牌堆；不触发任何出牌 Hook、计数或费用清理。
3. 不支付能量/星能；`SpendResources(isAutoPlay: true, skipXCapture)` 只在 `!skipXCapture` 时把当前费用写入本次 X 值（X 费卡取当前能量），并记录星能值。`ManualCardsPlayedThisTurn` 不计自动打出。
4. 目标：`AnyEnemy` 用 `CombatTargets` 流从 `HittableEnemies`（名单顺序、排除复活/永久死亡）取随机目标，候选为空时不消耗流；单人没有其他队友，`AnyAlly` 只有调用方已给出目标才成立；其他目标类型直接通过。
5. `AutoPlayFromDrawPile` 先按现有引擎把最多 `count` 张抽牌堆顶牌移到打出区，再按顺序逐张自动打出；每张打出前写入 `ExhaustOnNextPlay = forceExhaust`（false 也写入）。打出的卡结果去向为消耗堆时清除该标志（`CardModel.GetResultLocationForCardPlay`），未打出而直接移入去向时保留（`MoveToResultPileWithoutPlaying`）。自动打出中战斗结束/正在结束时，现有引擎仍对**剩余队列的每张卡**写入该标志，然后 `AutoPlay` 立即返回；Blitz 在队列仍连续位于 `start` 的前提下复刻这一写入并停止。`count` 超过 `Limits.Cards` 时抛 `CapacityException`。
6. 自动打出中的卡牌自身选择走现有嵌套选择协议：`OwnChoice` 请求选择、动作未携带时返回 `NeedsChoice`，调用方从父状态带选择重执行。生成选项（Discovery/Splash）的选择协议见 4.2。
7. **BeatDown 的对象身份。** 现有引擎先收集选中的 `CardModel` 引用再逐张 `AutoPlay`；前一张把后续选中卡移入手牌、抽牌堆或消耗堆后，仍按对象引用从新牌堆打出。Blitz 在一次动作内给每张选中卡写 `TransientMark`（序号 + 1），每轮按该字节在 `Cards` 中重新定位后 `AutoPlay`，`finally` 清零；完全移出战斗牌堆或被转化的卡没有无牌堆执行表示，按显式 `UnsupportedException` 回落，不按值猜牌。`DecisionsDecisions` 的重复自动打出与 Mayhem 队列使用同一动态 64..255 区间（`TransientMarkBase`），按标记跟随卡值移动；标记重复/丢失显式未支持，确认离场时走 no-pile 分支。

### 4.2 卡池随机生成与变化

来源：`E/CombatCardGenerationExtensions.cs`、`S/CombatPredictionSimulator.AutoPlayContinuation.cs`、`P/CardOnPlaySupport.Batch042.cs`、`Q/CardChoiceResolution.cs`。

- **候选池在 Bridge 冻结。** 定义用 `CardPools` 的键声明可能生成的池；`CharacterAll` 的 `FilterForCombatAndPlayerCount` 已排除 Basic/Ancient/Event 与 `CanBeGeneratedInCombat=false`，所以 CallOfTheVoid 的“角色池非 Basic/Ancient”直接复用它，`CharacterEthereal`（BigHat 的候选）按原版本地 Ethereal 关键字过滤。`RulesCompiler` 用现有引擎的同一枚举（`player.GetUnlocked...` + `FilterForCombatAndPlayerCount`，再按调用点的类型/费用/排除谓词过滤）把候选按原顺序写进 `Rules`。内核只按 RNG 取样，不重新过滤。
- **取样。** 去重（`TakeRandom`）先对整个候选列表做一次 Fisher-Yates（`n-1` 次 `Shuffle` 流 `NextInt(n+1)`），再取前 `count` 张；有放回（`NextItem`）每张消耗一次 `CombatCardGeneration` 流 `NextInt(0, n)`。候选为空时不消耗 RNG。生成的卡按 `CreateCard` 创建为 0 级规范卡，`UpgradeIf` 再升级一次。
- **加入战斗。** `AddGeneratedCard` 对应 `AddGeneratedCardsToCombat` 的单卡路径：手牌已满改投弃牌堆，按 `Bottom`/`Top`/`Random` 插入，调用卡的 `AfterEnteredCombat`。`SetToFreeThisTurn` 写入绝对 0 能量修饰（EndOfTurn|WhenPlayed）和 0 临时星能费用（回合末清除）。
- **生成选项选择。** Discovery/Splash 先记录生成的选项并写入风险（旧引擎的 `History.CardGenerationOptions` + `RecordRisk(UnresolvedPlayerChoice)`），随后 `GetSpec` 的 `GenerateToHand` 分支在 `OwnChoice` 提问。Blitz 把选项值直接放进 `ChoiceRequest.GeneratedOptions`（不在任何牌堆），选择后按 `PredictedCard.Clone()` 的语义复制卡值（不是 `CreateClone`，`IsClone` 保持为假）、`SetToFreeThisTurn`、加入手牌；空选项也提一个空范围选择，与 `RangeSpec` 一致。
- **回合开始的生成。** SentryMode 在 `BeforeHandDraw` 生成 `SweepingGaze`；CallOfTheVoid 在同一时机对 `CharacterAll` 逐张执行 `TakeRandom(1)`（每张一次完整洗牌，等价现有引擎的 `GetDistinctForCombat(1)` 循环），生成后补 Ethereal 关键字再入手；Pagestorm 在 `AfterCardDrawn` 只按本地 Ethereal 关键字追加抽牌。BigHat 的生成发生在玩家第 1 回合开始，早于根捕获，故不在内核路径里。
- **变化的候选池。** `CardFactory.GetDefaultTransformationOptions` 对 Quest/Event/Ancient/Token 用无色池，其余用卡牌自己的原生池（角色、无色、Status、Curse）。Blitz 在规则表为每张卡编译 `TransformPool`，Entropy 的变化按它选择冻结池，而不是按“当前是否在角色池里”猜测；Status/Curse 池分别对应 `CardPools.StatusAll`/`CurseAll`。`ReplaceCard` 对应 `CardCmd.Transform` 的现有引擎结算：从原牌堆移除原卡（`RemoveFromCombat`），再把替换卡插回 `min(原下标, 当前长度)` 并调用 `AfterEnteredCombat`；不触发 `AfterCardChangedPiles`。Blitz 没有实例身份，按位置定位；PrimalForce 对 snapshot 的手牌攻击按升序原位替换为 GiantRock（PrimalForce 升级时 GiantRock 升 1 级）。Entropy 的回合开始变化按 `TurnStartChoiceSupport.Resolve(Transform)` 选择手牌，再对每张走 `CombatPredictionSimulator.CreateRandomCardForTransform`：候选是原卡 `Pool`（Quest/Event/Ancient/Token 取无色池）中 `CanBeGeneratedInCombat`、非 Basic/Ancient/Event/Epoch 排除的卡，排除原卡 Id，用 `CombatCardSelection` 流 `NextItem` 抽一张 0 级规范卡；候选为空按 `UnsupportedException` 拒绝。
- **随机药水。** 一次 `CombatPotionGeneration` `NextFloat` 选稀有度（Rare ≤ 0.1、Uncommon ≤ 0.35、否则 Common），再一次 `NextItem` 在冻结的同稀有度池中选；`TryProcurePotion` 放入第一个空槽，成功时记录 `LongTermResourceValue + 20`。
- **药水生成选择。** Attack/Skill/Power/ColorlessPotion 在 `OnUse` 里生成 3 张去重候选，再走 `PotionChoiceSupport.GetSpec` 的 `GenerateToHand` 0..1 选择。该选择不走 `PendingTurnStartChoice`：现有引擎把候选写进 `History.CardGenerationOptions`，`PotionExecutionSupport.Complete` 从 `PlanAction.Choice` 直接结算 `PotionChoiceSupport.Apply`。Blitz 的翻译层先无选择回放一次读取候选，构造同序 `PlanCardChoice` 挂到动作上，再正式回放；选定后按 `PredictedCard.Clone()` 语义复制（`IsClone` 保持假）、`SetToFreeThisTurn`、加入手牌。
- **风险。** `BattleFlags.HasRisk` 表示旧快照的**未补偿**风险（`snapshot.HasRisk`）；根导入从快照读取该位。Discovery/Splash 虽记录 `UnresolvedPlayerChoice`，但 `CardOnPlayCompensationCatalog` 把它们的 `OnPlay` 风险标为已补偿，内核不为它们置位。SeekerStrike 的 `BuildSeekerSpec` 不在补偿目录里，会记录风险，Blitz 在该选择请求前同样置位（单调保持到战斗结束）。

## 5. 伤害管线

来源：`S/CombatPredictionSimulator.Attack.cs:83-165`、`S/CombatPredictionSimulator.Damage.cs:55-467`、`M/Hooks/Damage/ModifyDamageMirrors.cs`。

1. **攻击**：结束中或攻击者已死则返回；`BeforeAttack`；`ModifyAttackHitCount`；逐次命中：攻击者死亡或无存活目标则停止，随机目标用 `CombatTargets` 流；最后记录历史并触发 `AfterAttack`。EchoingSlash 走 `CardAttackRepeating`：同一上下文内多波伤害，每波结束后结算当前名单的死亡，击杀数追加波数。
2. **伤害**：先对全部目标计算并扣除，再统一处理结果。
3. **单目标**：目标已死则跳过；`ModifyDamage` = 附魔加成与倍率 → 按监听顺序的加法阶段（力量等）→ 乘法阶段（易伤、虚弱等）→ 取所有 `ModifyDamageCap` 的最小值 → 钳制到 ≥ 0。全程 `decimal`，不取整。卡牌动态变量同样按现有引擎的 `decimal` 读取：Thrash 的 `Damage` 偏移存在 `Battle.DecimalVars`，`CardEffects.Var` 返回同一个 `decimal`；只有 HP 损失按现有引擎的位置 `(int)` 截断，评估先做 `decimal` 加法再转 `double`。`BeforeDamageReceived`；格挡吸收（目标是 Osty 时扣其主人的格挡；不可格挡伤害跳过）；`ModifyHpLost` BeforeOsty 与其 Late → Osty 重定向 → AfterOsty 与其 Late → `AfterModifyingHpLostAfterOsty`；失血时 `(int)` 截断；失血 ≥ 当前 HP 视为击杀。
4. **结果处理**：`AfterBlockBroken` → `AfterCurrentHpChanged` → `AfterDamageGiven` → 未被击杀时 `AfterDamageReceived`（含 Late）；之后执行击杀，或所有玩家死亡时判负。每次结果同时按 `RecordDamageReceived` 记录玩家对敌人的 powered attack 命中（完全格挡也计数，非强化伤害不计），供 BeatIntoShape 的按目标锻造乘数读取；该计数在玩家回合开始清零，超过 255 按 `UnsupportedException` 拒绝。
5. **击杀**：扣光剩余 HP 并触发 `AfterCurrentHpChanged` → `BeforeDeath` → `ShouldDie`（第一个阻止者生效）。死亡：`AfterDeath` → 移出阵容 → 主敌人死亡且只剩附属时附属一并死亡。被阻止：`AfterDeath(prevented)` 与 `AfterPreventingDeath`，最多递归 10 次。
6. **敌人死亡效果延后**：在动作边界由 `ApplyEnemyDeathPowers` 统一结算（`P/CorePowerSupport.cs:597-674`）：SteamEruption → 死亡 Power（Ravenous、Surrounded、召唤）→ 清除死者施加的 Constrict、Hex、Shrink、MagicBomb。

## 6. 格挡与 Power

格挡（`S/CombatPredictionSimulator.Block.cs:49-93`）：结束中或已死返回 0 → `BeforeBlockGained` → `ModifyBlock`（附魔、加法阶段含敏捷、乘法阶段含脆弱、溢出钳制、≥ 0）→ `AfterModifyingBlockAmount` → 大于 0 时按出牌记录并加到格挡（`int` 截断，上限 999,999,999）→ `AfterBlockGained`。己方回合开始清格挡：`ShouldClearBlock` 或保留者，然后 `AfterBlockCleared`。

Power 施加（`Q/SimulatedCombatState.cs:648-711`）：数量为 0 或目标不能接受 Power 时返回 → 遗物修正（先 `ModifyPowerAmountGiven` 给 SneckoSkull 等加层，再 `TryModifyPowerAmountReceived` 普通阶段记录被修改的监听者，随后在 Artifact 检查前对它们派发 `AfterModifyingPowerAmountReceived`；RuinedHelmet 在此翻倍并标记本场已用）→ 目标有人工制品且是减益时抵消一层并返回 → 实例型 Power 新建实例，其余按（持有者、类型）叠加并钳制 → 玩家侧新获得的虚弱、易伤、脆弱标记跳过下次衰减 → 更新监听顺序 → 把非零变化放进待处理队列。旧引擎**没有**通用的 `AfterApplied`，只有少数 Power 初始化。原版 `PowerCmd.Apply` 不写 `PowerModel.Target`，因此 `Kernel.ApplyPower` 默认把 `Power.Target` 写成 `NoOwner`；只有原版自身写 `Target` 的五处（Swipe/Thievery/Sandpit/Heist/WitheringPresence）才走 `ApplyPowerTargeted` 或由导入层保留原生目标；`Misery` 是另一种情况，它克隆源 `PowerModel`（`ClonePreservingMutability`）后用非泛型 `PowerCmd.Apply` 施加，`Target` 由克隆值原样保留，因此走 `ApplyPowerPreservingTarget`，不算新的写入点。其余卡牌、遗物、药水与怪物都不能传 `powerTarget`。

衰减：虚弱、易伤、脆弱、无实体、NoBlock 在**敌方回合结束时对所有生物**衰减并尊重跳过标记；Blur 在己方回合开始衰减且该回合开始不清格挡；DoubleDamage 在每方回合结束衰减；瞬时 Power 在己方回合结束清除；临时力量、敏捷、集中在 `TriggerRegular` 中按监听顺序退回。`PowerDef.PreventsCombatEnding` 对应原版 `ShouldStopCombatFromEnding`：战斗结束判定会遍历仍在阵容中的生物的正层 Power，AdaptablePower、StockPower 等返回真时 `IsEnding` 保持假（TestSubject 的复活窗口）。`SandpitPower` 是实例型、带目标的计数 Power：根导入按原生实例逐行写入 `Power.Target`；敌方侧回合开始的固定序列（`TurnStartPowerSupport.TriggerAfterSideTurnStart` 的 SANDPIT case）按行把层数减一，归零时对记录的目标执行 `Kill(force: true)`（玩家死亡时由 `HandlePlayerDeath` 顺带清理 Osty），与 `SimulatedCombatState.IncrementSandpitTargeting`/`FranticEscape` 的加层路径一致。

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
4. `BeforeHandDraw`：Power → 遗物 → 返回手牌的卡。返回手牌阶段处理 `Card.ReturnToHandNextTurn`（Bolas/ThrummingHatchet），牌已在手牌时只清位。
5. 抽牌数 = 5 + 下回合抽牌 + DrawCardsNextTurn → `ModifyHandDraw` → Late；抽牌。`ConsumeModifiedHandDraw` 的“尚未快照”项（`DrawCardsNextTurnPower` 的 `AmountOnTurnStart == 0`）在监听者之前直接加到基础抽牌数；已快照的层数由该 Power 的 `ModifyHandDraw` 加。`DrawCardsNextTurnPower` 在 `TriggerBaseSideTurnStart` 清零。
6. `AfterPlayerTurnStart`（原版：先 Power，再遗物）。
7. 玩家己方回合开始效果在抽牌**之后**执行；若回合开始有选择，则在该选择之前执行。
8. 敌人死亡效果 → 附魔与球 Hook → 自动预打出 → 胜负判定。

**实现形状**（`Kernel.TurnPhases.cs`）

现有引擎的回合阶段有两种写法，Blitz 分别对应：

- 按监听器顺序遍历 Power（或遗物）再按类型 `switch`：每个 `case` 是定义上的同名阶段方法（`PowerDef.EndTurnRegular`、`BeforeHandDraw`、`AfterPlayerTurnStart` 等；遗物为 `RelicDef` 的批次方法）。每一轮遍历开始时按监听器顺序（玩家、Osty、名单中的敌人，各自的 Power 按表内顺序）取快照，访问时按 (owner, id, occurrence) 重新定位（实例型 Power 每个实例一行），层数必须仍为正。
- 固定类型序列（例如 `TriggerOwnerAfterSideTurnStart` 的 BiasedCognition → … → Clarity、`TriggerBaseSideTurnStart` 的 Blur → DrawCardsNextTurn → Plating → Slow）：内核保存从现有代码抄录的原版 Id 列表，只调用已登记且在场、层数为正的类型。

持有者范围与现有遍历一致：`AfterEnergyReset` 与 `AfterPlayerTurnStart` 只看玩家生物，`BeforeHandDraw` 包含 Osty（`power.Owner.Player == player`）。敌方回合的每个阶段都重新读取当前敌人名单，对应现有代码直接传入 `simulatedCombat.Enemies`。`ShouldClearBlock` 是原版 Hook，战斗结束中不枚举监听者；第一个拒绝清除的监听者通过 `BlockAfterPreventingClear` 结算保留量。

`EndTurn` 的前缀共享只复用“玩家回合结束 + 敌方回合”这段计算结果，不改变转移序列：第一个选择叶子执行一次完整 `EndTurn` 并捕获快照，后续叶子各执行一次 `StepEndTurnFromPrefix`，仍逐一经过原来的池、去重与计数路径。每方回合开始的 `AmountOnTurnStart` 快照按所有者集合一次扫描（只写各 Power 自己的字段，与逐所有者调用结果相同）；敌方回合末的持续时间衰减先把各生物的可衰减 Power 按“生物顺序 + Power 表内顺序”分桶，再按原顺序逐项衰减，前一个生物只移除自己的 Power，因此后续生物的 `(owner, id)` 序列与逐生物重扫一致。

## 8. 抽牌与洗牌

来源：`S/CombatPredictionSimulator.CardPile.cs`。

- 小数抽牌数向上取整；`ShouldDraw` 失败则整次不抽。Pagestorm 在每张本地 Ethereal 牌抽到后追加一次抽牌（`AfterCardDrawn`）。
- 逐张：结束中或手牌达到上限则停止；抽牌堆空且弃牌堆非空时洗牌；取抽牌堆顶入手；历史 → `AfterCardDrawn` Early → 普通 → 被抽卡自身 Hook。`TryDrawOne` 在抽牌前保存被抽卡的值；Pillage 用它判断“刚抽到的牌是否攻击”，即使 `AfterCardDrawn` 中的 Hellraiser 已把该牌自动打出并从手牌移除。
- 洗牌：列表 = 弃牌堆（原顺序）+ 抽牌堆；按卡牌排序键稳定排序后用 `Shuffle` 流做不稳定洗牌（与 `StableShuffleProjection` 相同）；`ModifyShuffleOrder`；全部放到抽牌堆底；`ShuffleEventCount + 1`；战斗未结束时 `AfterShuffle`（TheAbacus 获得其 Unpowered 格挡）。排序键来自原版 `CardModel.CompareTo`，在根导入时编译进 `Rules`。
- 随机插入牌堆位置用 `Shuffle` 流的 `NextInt(n + 1)`。手牌满时加入手牌的卡改进弃牌堆。

## 9. 怪物 AI

旧引擎不运行原版状态机，而是解释它的图（`P/BranchMonsterAi.cs`）。Blitz 在根导入时把每个敌人的状态机编译成表：

- 节点类型：行动节点（攻击列表：基础伤害与次数）、随机分支（各后继的基础权重）、条件分支（根捕获时的选择结果）。
- 推进：从当前行动的后继出发，随机分支用 `MonsterAi` 流按 `MonsterRandomBranchResolver.Pick` 的规则选择（依赖最近行动日志），条件分支取根选择，直到落到行动节点；最多 32 步。
- 特例：旧引擎在 `ResolveConditional`、`GetBranchWeight` 与 `Advance` 中按怪物类型写的特殊逻辑（KnowledgeDemon、FrogKnight、Fabricator、TestSubject、Ovicopter、LagavulinMatriarch、SlumberingBeetle、Queen、LivingShield、BowlbugRock、TwoTailedRat）由对应 `MonsterDef` 覆写同名方法实现。KnowledgeDemon 的 `_curseOfKnowledgeCounter` 是一个 AI 寄存器：根导入取 `_knowledgeDemonCurseCounters` 条目，没有条目时取模型字段；CURSE_OF_KNOWLEDGE 的选项集合、选择后的加一与 PONDER 后的 `CurseOfKnowledgeBranch` 都读这个寄存器。怪物动态伤害与连击数另用 `ModifyAttackDamage`/`ModifyAttackRepeats` 对应 `AdjustMonsterMoveDamage` 与 `BranchMonsterAi.CurrentMove` 的动态伤害/命中数（TheForgotten 的 DREAD、TestSubject 的 MULTI_CLAW）；根冻结的 Power 数量用 `MonsterDef.RootPowerAmountNames` 编译进 `MonsterRules.RootPowerAmounts`。该基线对应现有引擎在**请求根**捕获、随 fork 共享的 `SimulatedCombatState._rootPowerAmounts`：有固定前缀时规则编译必须把请求根作为 `baselineSimulator`，不能取前缀之后的搜索根（否则 MIASMA 偷取 2 敏捷后，DREAD 会少 2 点伤害）。没有前缀时基线就是当前根。
- 行动执行：攻击按编译表结算；攻击前的效果由 `MonsterDef.BeforeMove` 实现（`P/MonsterMoveEffects.ApplyBeforeAttack`，例如 Living Fog 的 BLOAT 先生成 Gas Bomb）；非攻击效果由 `MonsterDef.ApplyMoveEffects` 实现，规范来源是 `P/MonsterMoveEffects.cs` 与 `P/MonsterMoveSemantics.cs`。
- 遭遇槽位：`Enemy.Slot` 来自 `SimulatedCombatState.GetSlotIndex`，名字不在 `_encounterSlots` 时就是原版的 -1（`NoSlot`）。`EncounterModel.Slots` 为空但仍用 `first`/`middle`/`last`、`odd`/`even`、`first`/`second`/`third` 的遭遇（碗虫、沉睡甲虫）因此不排序、也不报错；`NextFreeSlot` 对空槽表返回 `""`、`LastFreeSlot` 返回 `null`，与现有引擎一致，不编造槽位顺序。
- 生成初始状态：`MonsterDef.SpawnedStunned` 让 Bridge 用 `StartStunned = true` 编译该类型的生成规则（Wriggler 的初始节点是 SPAWNED_MOVE）；`SpawnSlotName` 给这类按槽名选择 INIT_MOVE 分支的怪物一个固定合法槽名，避免私有编译遇到无匹配条件。
- 私有字段：旧引擎通过 `_monsterIntStates` 读写的怪物私有整数，在 Blitz 中映射到 `Enemy` 的 AI 寄存器，编号由各怪物定义的 `AiRegisterNames` 按声明顺序决定；定义另外用 `InertMonsterIntNames` 声明只被状态指纹/续接文本读取、不影响结算的条目。导入时由定义认领，未认领的条目拒绝（见第 13 节）。

## 10. 选择协议

- `PlayerAction` 携带至多 32 个选择（`Limits.MaxChoices`；`DecisionsDecisions` 的嵌套自动打出可以在一张牌里产生二十余个请求），每个选择是"选项列表上的位集合"。选项列表由需要选择的效果按现有引擎 `CardChoiceSupport.GetSpec` 的顺序生成（来源牌堆中满足条件的卡的位置）。
- 效果请求第 k 个选择而动作只提供了不到 k 个时，内核停止执行，返回 `NeedsChoice` 与选项规格（效果、时机、来源牌堆、选项的绝对位置、最少与最多数量；数量已按选项数截断，与 `BuildChoices` 一致）。调用方丢弃这个半成品状态，枚举选项后从父状态重新执行同一动作。
- 卡牌自己的选择在 `CardDef.OwnChoice` 中，位于 `OnPlay` 之后、出牌收尾之前，对应现有引擎的 `ResolveManualCardChoice` 阶段（`GetSpec` → `Apply` → `ApplyPostChoiceEffects`）。`Kernel.ChooseCards` 对应 `CardChoiceSupport.Spec`：没有选项时不建规格、不提问，选择后效果照常执行；`ChooseCardsInRange` 对应 `RangeSpec`：没有选项也提问。`BuildRequiredEmptyChoice` 列出的卡（烙印等）在没有选项时提一个空选择。
- 选中的卡按选择顺序结算（与令牌顺序一致）。字节相同的卡在不同位置并不等价（拿走哪一张会改变中间卡的顺序），所以内核按"值 + 第几张相同的卡"定位选中的卡，对应旧引擎令牌的 `SourceOccurrence`；结算过程中有 Hook 介入时每张卡重新定位。`Kernel.TransformSelectedTo` 对应 `CardChoiceResolution.TransformSelectedCards`（Charge/Guards/Begone 的固定 Minion 替换），同样按值与同值序号重定位；`Duplicate`（HeirloomHammer 的 `CreateClone` 复制）与 `AutoPlayRepeated`（DecisionsDecisions）在卡定义内按同一位置规则结算。
- 时机标签按现有引擎 `SetActionChoiceTiming` 的位置切换：出牌为 Action，回合结束开始时为 PlayerTurnEnd，敌方回合为 EnemyTurn，玩家回合开始为 PlayerTurnStart。回合开始的选择由 `EndTurn` 动作携带。
- 翻译与校验：Bridge 用旧引擎逐次回放动作。旧引擎停在待定选择时，取 Blitz 的下一个位集合，按旧引擎规格的同序选项生成 `PlanCardChoice` 令牌（`CardChoiceSupport.BuildSelectedChoiceForBlitz`），再按现有搜索放置已解析分支的同一规则（`CombatBeamSolver.AppendPendingChoice`：回合阶段的进 `TurnStartChoices`，出牌第一个无来源请求为主选择，其余为嵌套选择）挂到动作上重放，直到旧引擎不再提问。选择次数不一致即校验失败。
- **固定选项选择。** Knowledge Demon 的 CURSE_OF_KNOWLEDGE 没有牌堆来源：选项是两张诅咒卡的值（`ChoiceKind.SelectOption`、`ChoiceEffect.ApplyKnowledgeCurse`、`OptionsAreGenerated`，`Source=None`，1..1）。`Kernel.ChooseOption` 与卡牌选择共用 `TryTakeChoice`，因此动作必须恰好选中一项；选择后按所选卡施加对应诅咒 Power 并把恶魔的计数器加一。Bridge 在旧引擎停在 `PendingKnowledgeDemonChoice` 时用 `KnowledgeDemonChoiceSupport.BuildChoices` 的同序分支直接生成 `PlanCardChoice` 并挂到 `TurnStartChoices`，不经过 `CardChoiceSpec`；差分探针比较选项卡 Id、时机与数量。

## 11. RNG

- 九条流与旧引擎一致：Shuffle、CombatCardGeneration、CombatPotionGeneration、CombatCardSelection、CombatEnergyCosts、CombatTargets、CombatOrbGeneration、MonsterAi、Niche。
- 每条流保存计数器与四个状态字，算法逐位复刻原版 `Rng`/`MegaRandom`，用纯内核检查对照原版类录制的输出向量。
- 每次取值计数器 +1，与原版一致；状态字和计数器都进入状态字节。

## 12. 强制行动与眩晕

来源：`Q/SimulatedCombatState.MonsterAi.cs:58-128`、`Q/SimulatedCombatState.cs:1174-1181`、`Q/CombatBeamSolver.Expansion.Replay.cs:1105-1180`、`P/BranchMonsterAi.cs:139-166,245-255`。

**规则编译**（`Bridge/RulesCompiler.CompileMonster/CompileMove`）：`MoveState.MustPerformOnceBeforeTransitioning` 编译成 `MonsterNode` 标志，不再拒绝。为每个怪物在已登记状态（以及未登记进状态表的初始分支）之后追加一个合成节点：Id 为 `STUNNED`、`Kind = Move`、无攻击、无效果、`MustPerformOnceBeforeTransitioning = true`、静态后继为空。已有节点下标保持不变；节点的后继动态存放在 `Enemy.ForcedMove`（255 表示当前不是 STUNNED）。节点总数因此加一，超过 64 个节点的状态机在导入时按容量拒绝。

**失衡眩晕**：一次怪物行动的全部命中若至少有一次 `DamageResult.WasFullyBlocked`，且该怪物仍有 `ImbalancedPower`，`MonsterMoveSemantics.ApplyForecastMove` 在攻击收尾后、行动附加效果前强制 `STUNNED` 并 `StunNextMove`。Blitz 的 `Damage` 返回"是否有完全格挡结果"，在 `ExecuteMonsterMoveCore` 的同一位置执行；BowlbugRock 的合成后继是 `HEADBUTT_MOVE`，其余怪物用最近一条行动日志。沉睡甲虫的 `SlumberPower` 在受击掉到 0 时强制 `STUNNED`（后继 `ROLL_OUT_MOVE`），在侧回合末掉到 0 时清 Plating 并由 `SNORE_NEXT` 条件分支走到 `ROLL_OUT_MOVE`。

**强制行动 API**：

- `Kernel.ForceMonsterMove(ref Exec, byte owner, int node)`：把当前节点换成 `node`，清除 `NeedsInitialRoll` 与 `ForcedMove`。不写 `LoggedMoves`/`RecentMoves`（`BranchMonsterAi` 只在 `Advance`/`RollInitial` 落到行动节点时写日志）。
- `Kernel.ForceStunnedMove(ref Exec, byte owner, int nextNode)`：`nextNode` 小于 0 时取现有引擎的缺省后继 `StateLog.LastOrDefault() ?? Current.Id`，即最近一条日志（`RecentMoves` 的末项），日志为空时取当前节点；随后写入 `ForcedMove = nextNode`、`Move = StunnedNode`、清除 `NeedsInitialRoll`。同样不写日志：现有引擎的 `ForceStunnedMove` 只替换 `Current`。
- 现有引擎的 `performedMoves` 是行动开始时的 `MoveState` 对象；强制行动在本轮已经行动之后替换当前节点时，这个对象与新的 `Current` 不同。Blitz 用 `EnemyFlags.ForcedMustPerformAfterMove` 记录这一条件（`ForceStunnedMove` 总是新建对象，在已行动之后调用时记录；`ForceMonsterMove` 只在目标不同且目标 must-perform-once 时记录）。`ActingThisRound` 覆盖现有引擎在动作返回后才写入 performed 标记的时序。两个标志都只在敌方回合内部使用，`PrepareMonsterMovesForNextRound` 消费并清零，稳定边界上恒为 0。

**推进**（`PrepareMonsterMovesForNextRound`，逐行对应 `MonsterAi.cs:102-128`）：不在场或不能行动则跳过；`NeedsInitialRoll` 走 `RollInitial` 对应的分支；本轮没有执行任何行动则跳过；当前是 STUNNED 且 `SkipNextMove` 为真则不推进；被强制替换且 must-perform-once 未满足则不推进；否则从 `ForcedMove`（STUNNED）或节点的静态后继推进，`ResolveMonsterState` 落到行动节点时写入日志并输出强制行动。被 `SkipNextMove` 跳过的敌人仍记入 `PerformedMoveThisRound`（`Replay.cs:1120-1125`）。

**执行**：STUNNED 没有攻击，跳过 `BeforeAttack`/`AfterAttack`；`MonsterDef.ApplyMoveEffects` 仍以 `STUNNED` 调用，怪物定义可据此实现 `MonsterMoveEffects.Apply` 的 STUNNED 分支（例如乐加维林族母清空格挡）。STUNNED 本身不写行动日志。

**导入**：旧引擎当前行动 Id 为 `STUNNED` 时映射到合成节点，后继（`FollowUpState?.Id ?? FollowUpStateId`）映射到 `ForcedMove`；后继仍是 `STUNNED` 时显式拒绝。合成 `STUNNED`/`REVIVE_MOVE` 只安装在 `MonsterModel.NextMove`，不会登记进 `StateLog` 或 `MoveStateMachine.States`，因此不能按注册表查后继。当前行动 `STUNNED` 且原生持有 `ImbalancedPower` 时同时置 `EnemyFlags.SkipNextMove`：`ImbalancedPower.AfterDamageGiven` 只在完全格挡的攻击后调用 `CreatureCmd.Stun`（BowlbugRock 走 `IsOffBalance`），这是该眩晕的唯一原生来源。进行中的 must-perform-once 行动由节点标志表示，`PerformedMoveThisRound` 在稳定边界上为 0，因此导入后不会误推进。

**乐加维林族母的 `_isAwake`**：现有引擎在 AsleepPower/STUNNED 中写入、在指纹与续接文本中读取，但行动选择读 AsleepPower 数量（`BranchMonsterAi.ResolveConditional`），该布尔值不影响后续结算。`DescribeBlitzUnrepresentableState` 只放过 `(LagavulinMatriarch, "_isAwake")` 这个已证明惰性的 `_monsterIntStates` 条目（不修改模拟器状态）；其他怪物私有整数仍被拒绝。

## 13. 敌人生成与召唤

来源：`P/MonsterSpawnSupport.cs`、`Q/SimulatedCombatState.cs:2800-2875,2977-3000`、`P/MonsterMoveEffects.cs` 的 `Fogmog`/`TwoTailedRat` 分支、`P/DeathPowerSupport.cs` 与 `Q/SimulatedCombatState.DeathLifecycle.cs:100-119,190-220`。

**规则编号与闭包**：每个 `Enemy` 用 `Rules` 记录怪物规则表下标；根已知敌人按 `KnownEnemies` 下标占用表的前 `RootEnemyCount` 个条目，之后每个可生成怪物类型一个条目。`MonsterDef.SpawnedMonsterIds` 声明该怪物能生成的原版 Id，`RulesCompiler` 从根敌人出发递归求闭包。可生成类型的状态机、静态值与初始私有整数由编译器在根模拟器的**私有 Fork** 上执行现有引擎的创建路径得到：`MonsterSpawnSupport.Create(canonical)` → `AddPredictedMonster`（`SetUpForCombat` + `AddCreature` + 排序）→ `PreparePredictedMonster`，再用同一个 `CompileMonster` 编译；不手抄静态值。规则表总容量 `Limits.MonsterRules`，溢出按 `Capacity` 拒绝。根持有 `Murderous` 修饰且闭包非空时拒绝（`UnsupportedClosure`）；贤者之石与毛皮大衣本就因缺少遗物定义在导入时拒绝。

**`SpawnMonster`**（`Kernel.SpawnMonster`，严格对应 `MonsterSpawnSupport.AddCreated` 的顺序）：

1. `CombatId = NextCombatId++`；生命在 `[MinInitialHp, MaxInitialHp]` 中排除名单敌人的当前 `MaxHp` 后取值。可选值按现有 `HashSet<int>` 的枚举顺序（等于升序插入顺序）由 Niche 流 `NextInt(0, count)` 取第 k 个；没有可选值时用 `NextInt(min, max + 1)`。K0 断言该枚举顺序。
2. 追加到已知敌人数组（`EnemyCount++`），再按遭遇槽下标稳定插入名单，对应 `AddCreature` + `SortEnemiesBySlotName`；同槽位保持先后顺序，无槽名（255）排在有名字的槽位之前。
3. 写入编译器捕获的 `SetUpForCombat` 初始值：`PendingInitialMoveNode`、初始 AI 寄存器。
4. 调用定义上的 `ApplyEntrancePowers`（对应 `ApplyNativeEntrancePowers`，只为已登记的内容实现）。
5. 玩家回合内立即从 `InitialNode` 掷出初始行动并写日志；敌人回合内保留 `NeedsInitialRoll`，由本轮结束的 `PrepareMonsterMovesForNextRound` 掷出。
6. `minion` 为真且没有 MinionPower 时补 1 层 Minion，applier 是召唤者。

**Fabricator 与 Axebot 生成**：Fabricator 按 `MonsterSpawnSupport.SpawnFabricatorBot` 的同一随机选择（`MonsterAi` 流 `NextItem`，排除 `fabricator_last_spawn`）生成 Guardbot/Noisebot/Zapbot/Stabbot，槽位取下一个空槽并带 Minion；Zapbot 的入场 HighVoltagePower 2 由 `ApplyEntrancePowers` 补上。Axebot 被击杀且仍有 StockPower 时在死亡钩内以同槽位生成替代体（`StockPower.AfterDeath`）：生成规则按 StockAmount 1 编译（BOOT_UP 初始节点与 +10 生命范围），入场再按来源 StockPower 补足剩余的 +10 与新的 StockPower 层数。

**空槽**：`Kernel.NextFreeSlot`/`LastFreeSlot` 与 `SimulatedCombatState` 同义：战斗没有遭遇时返回 `null`；有空槽时返回遭遇槽名；`NextFreeSlot` 在无空槽时返回 `""`，`LastFreeSlot` 在无空槽时返回 `null`。

**幻象与爪牙**：`EyeWithTeeth` 入场时先获得 IllusionPower 1（applier 为自己）再获得 MinionPower 1（无 applier），与 `ApplyNativeEntrancePowers` 一致。IllusionPower 的 `PreventsRemovalAfterDeath` 让尸体留在名单；其 `AfterDeath` 对应 `BeginIllusionRevive`：死亡阶段改为 `Reviving`，强制合成 `REVIVE_MOVE` 节点，动态后继取最近一条非复活行动日志。规则编译在 `IllusionRevive` 为真的怪物上追加该合成节点（无攻击、必须先执行一次），节点下标存在 `MonsterRules.ReviveNode`。`REVIVE_MOVE` 执行时把生命回满并清回复活阶段，随后按动态后继推进。尸体的 Power 清理按 `RemovePowersAfterDeath`：有 IllusionPower 时保留增益与临时减益、移除其余减益；已离开名单的敌人清空全部 Power。MinionPower 的 `OwnerIsSecondaryEnemy` 让主要敌人死亡时连带杀死剩余爪牙；`MinionPower.ShouldPowerBeRemovedAfterOwnerDeath` 为假。

**TwoTailedRat**：AI 寄存器 0 是 `_turnsUntilSummonable`（初始 2），1 是 `_callForBackupCount`（初始 0）。`SCRATCH_MOVE`/`DISEASE_BITE_MOVE`/`SCREECH_MOVE` 递减前者；`CALL_FOR_BACKUP_MOVE` 在 `LastFreeSlot` 生成一只新鼠，再把名单中所有鼠的后一个寄存器同步为（最大值 + 1）。随机分支权重按 `BranchMonsterAi.GetBranchWeight`：可召唤时 CALL_FOR_BACKUP 为 0.75、其余三者各 1/12；不可召唤时 CALL_FOR_BACKUP 为 0、其余为 1。可召唤条件按 `CanTwoTailedRatSummon`：回合计数不大于 0、计数小于 3、`NextFreeSlot` 非空，且其他鼠当前行动不是 CALL_FOR_BACKUP。`SCREECH_MOVE` 还给玩家 1 层 Frail。

**寄生死亡与召唤**：`InfestedPower` 在死亡效果清算阶段（`ApplyEnemyDeathPowers` 的 `DeathPowerSupport.Trigger` 位置）生成四只 `Wriggler` 到 `wriggler1..4` 槽位，创建时 `StartStunned = true`。它们是主要敌人；在死亡效果尚未清算、尸体已离开名单的窗口里，`HasUnresolvedSpawningDeath`（`SpawnsPrimaryEnemyOnDeath`）让 `IsEnding` 继续为假，与现有引擎一致（`Hook.ShouldStopCombatFromEnding` 的活动监听表此时看不到已移除尸体的 Power）。

**重接复活**：`ReattachPower` 不阻止击杀命中，但阻止尸体离场（`PreventsRemovalAfterDeath`），`ShouldPowerBeRemovedAfterOwnerDeath` 为假。死亡效果清算时，若还有另一段能行动（存活或 `Reviving`）则死亡阶段改为 `Reviving` 并强制 `DEAD_MOVE`，否则所有段改为 `PermanentlyDead`。`REATTACH_MOVE` 由怪物的 `ResolveReviveMove` 处理：仍有一段能行动时按 `ReattachPower` 数量回血并清除 `Reviving`。Reviving 段的 `CurrentHp` 为 0，`IsHittable` 自然为假；评估的 `EffectiveEnemyHp` 在有其他未永久死亡段时取 `ReattachPower` 数量，否则为 0。`ShouldOwnerDeathTriggerFatal` 同样按分支名单的 Power 与 HP/死亡状态计算；Blitz 使用 `Battle` 段状态，现有引擎使用 `SimulatedCombatState.ShouldOwnerDeathTriggerFatal`，双方都不能调用原版 live `ReattachPower.AreAllOtherSegmentsDead`。

**导入认领**：`_monsterIntStates` 的每个条目必须被某个已知敌人的 `AiRegisterNames` 或 `InertMonsterIntNames` 认领，否则按 `UnrepresentableState` 拒绝；声明为寄存器的名字取 `GetMonsterInt` 的值（有捕获条目用条目，没有时读模型字段），因此与现有引擎的无条目语义一致。乐加维林族母与沉睡甲虫的 `_isAwake` 由 `InertMonsterIntNames` 声明。Fabricator 的 `fabricator_last_spawn`（经 `GetFabricatorLastSpawn`）、TestSubject 的 `_respawns`/`_extraMultiClawCount`/`SecondFormHp`/`ThirdFormHp` 与 Aeonglass 的两个字典计数同样投影为 AI 寄存器。

## 14. 偷窃与瀑布巨兽

来源：`P/MonsterMoveEffects.cs` 的 THIEVERY/ESCAPE/GIMME 分支、`S/SimulatedCombatState.Theft.cs`、`S/CorePowerSupport.ApplyEnemyDeathPowers`、`S/SimulatedCombatState.cs:1363-1460`。

- **偷窃。** THIEVERY 在攻击前按 `ThievingHopper._stealPriorities`（Uncommon → Common/Rare/Event → Basic/Quest → Ancient/Imbued；前三类排除 Imbued）从抽牌堆+弃牌堆的牌组卡中选一张，用 `CombatCardGeneration` 流取走并从战斗中移除，完整 `Card` 存 `DetachedCards`、`SwipePower.Aux0` 指向槽位。GIMME/DOUBLE_SMASH/HEHE 在攻击后 `RecordThievery`：每个 `ThieveryPower` 按 `min(层数, 当前金币)` 偷金币，写入 `Gold` 余额、Power 的 `Aux0` 账本和 `OutstandingStolenGold`。`SurprisePower` 的死亡效果先创建 fat（CombatId、Niche HP 抽取先于 sneaky），moved 到 `SURPRISE_POWER.DeathTrigger`：转移每份 `ThieveryPower.Aux0` 到 fat 上的 `HeistPower`（Target 保留），再生成 sneaky 并加入 fat。死亡清算末尾 `RecoverStolenResources` 按 `SwipePower` 回收卡数、按 `HeistPower.Amount` 回收金币。`SwipePower.BeforeDeath` 在飞贼死亡时把 `StolenCard.DeckVersion` 追加到 `runState` 牌组末尾；Blitz 在死亡移除 `SwipePower` 时保留 detached 卡值并清除 `RemovedFromState`，写回再把对应牌组卡移到末尾。逃跑时 `CreatureEscaped` 先移除全部 Power；Blitz 保留 detached 卡值并保留 `RemovedFromState`，写回按“丢失”从牌组移除。仍被 `SwipePower.Aux0` 引用的 detached 行是仍被扣住的卡，写回直接移除。`OutstandingStolenCards`/`OutstandingStolenGold` 按原版在死亡回收、逃跑不回收。
- **逃跑。** `CreatureEscaped` 把生物移出 `CombatState.Enemies`，Blitz 在左移 owner 下标和名单后，把该怪物作为 `EnemyFlags.Escaped | DeathPhase.PermanentlyDead` 墓碑追加到已知敌人表末尾（不进入名单），保留 `MonsterId`/`CombatId`/`Slot`/`Rules` 供历史写回补上该怪物。`LiveCombatTracker` 用原版 `CreaturesChanged` 事件记录 `AddCreature` 的**加入顺序**（不是 CombatId 或槽位顺序），并把已逃跑生物按逃跑顺序追加到已知敌人末尾；逃跑生物的 loot 在 `CreatureCmd.Escape` 移除 Power 前已不可见，tracker 把逃跑前的 `ThieveryPower`/`HeistPower`/`SwipePower` 数量计入不可追回的 outstanding 计数。
- **Possession。** `PossessStrengthPower`/`PossessSpeedPower` 的 `_internalData` 是 `Dictionary<Creature, decimal>`，`AfterPowerAmountChanged` 记录带符号的负值；导入按玩家目标读取当前账本值，不再在非根边界写 0。根账本为空时仍投影为 0。
- **瀑布巨兽。** `PRESSURIZE/STOMP/RAM/SIPHON/PRESSURE_GUN/PRESSURE_UP` 在攻击后施加 `SteamEruptionPower`（数值见 `MonsterMoveEffects`），`PRESSURE_GUN_MOVE` 另把 `PressureGunIncrease` 加入原版 `_currentPressureGunDamage`（`AdjustMonsterMoveDamage` 的 `damage + bonus` 效果）；`SIPHON_MOVE` 治疗 `SiphonHeal`。原版没有旧引擎的 `_pressureGunBonus` 字段：AI 寄存器 `PressureGunBonus` 由 `_currentPressureGunDamage - BasePressureGunDamage` 投影，`SteamEruptionDamage` 由 `_steamEruptionDamage` 投影；两者在根编译时都为 0。死亡清算先于其它死亡 Power 调用 `TryTriggerSteamEruptionDeath`：HP/最大 HP 设为钳制值、强制 `ABOUT_TO_BLOW_MOVE`、记录 `AboutToBlow` 阶段并清理其它 Power（`SteamEruptionPower.ShouldPowerBeRemovedAfterOwnerDeath=false` 保留自身）。敌方回合在普通行动前消费强制阶段：AboutToBlow 记录当前层数为 `SteamEruptionDamage`、移除 Power、切到 Explode；Explode 用记录值经完整伤害管线打玩家一次，再 `Kill(owner, force:true)` 并结算死亡效果。爆发阶段 `EffectiveEnemyHp` 为 0，威胁投影只计 Explode 的记录伤害；`SteamEruptionPower` 的 `PreventsCombatEnding`/`PreventsRemovalAfterDeath` 与 `Kernel.IsEnding`、`ShouldRemoveAfterDeath` 一致。
- **评估。** `OutstandingStolenResource` 是金币+卡数计数，`PreserveResources` 策略下乘 `OutstandingStolenResourcePenalty` 进评分并作为最终路线的追回键；`MinimumOutstandingStolenResource` 只把已不可追回的部分留给 incumbent 下界（当前偷窃根的下界因 `theft_policy`/`swipe_power` 显式禁用）。

额外回合、9 瓶药水与新增遗物的逐条机制见 [药水与遗物](combat-potions-relics.md)。
