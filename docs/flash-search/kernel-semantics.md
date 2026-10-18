# Flash 内核语义

[返回 Flash 入口](README.md)

本文规定 Flash 内核的执行模型。**规范来源是现有引擎的实际行为**：下文的步骤顺序摘自现有引擎源码，引用路径相对仓库根。两者冲突时以现有引擎和差分测试结果为准，并回头修正本文。

路径缩写：`S/` = `src/Engine/InCombat/Simulation/`，`M/` = `src/Engine/InCombat/Mirrors/`，`Q/` = `src/Search/`，`P/` = `src/Prediction/`。

## 1. 执行上下文

内核入口是静态方法，签名形如 `FlashStepResult Step(ref FlashBattle battle, in FlashAction action, FlashRules rules)`。执行期间的临时信息放在栈上的 `FlashExec`（`ref struct`，持有 `ref FlashBattle`、`FlashRules`、选择游标、当前出牌上下文），不进入状态。

- 动作执行到稳定边界才返回，返回值是 `Completed`、`NeedsChoice`（附选项规格）或 `Terminal`。
- 执行中途不允许分配托管对象；内容代码只读写 `FlashBattle` 和栈上值。
- 未知内容 Id、未实现的 Hook 组合抛 `FlashUnsupportedException`；容量溢出抛 `FlashCapacityException`。两者都使本次 Flash 求解整体作废。

## 2. 内容定义

每类内容一个抽象基类：`FlashCardDef`、`FlashPowerDef`、`FlashRelicDef`、`FlashPotionDef`、`FlashMonsterDef`、`FlashOrbDef`、`FlashEnchantmentDef`、`FlashAfflictionDef`。具体内容是 `sealed` 子类，覆写需要的 Hook 虚方法。

- 每个定义用特性声明原版 Id，例如 `[FlashContent("BASH")]`。注册表在首次使用时扫描定义，分配连续的 `ushort` 内部 Id，并按"覆写了哪些 Hook"建立每个 Hook 的监听位图，派发时跳过非监听者。
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
- 战斗正在结束时，战斗监听者的 Hook 返回空，`AfterCardPlayed`、`AfterBlockBroken`、`AfterAttack` 除外；伤害和死亡类的跑局监听者不受此限（`M/HookMirrors.cs:1351`）。
- 只有原版监听者时，旧引擎对回合阶段 Hook、Power 反应、最大能量与抽牌数、`BeforeHandDraw` 使用"先全部 Power、再全部遗物"的批量顺序，不按逐监听者顺序。Flash 在相同位置用相同的批量顺序。这一偏差以旧引擎为准。

### 3.3 Hook 目录

内核为下列 Hook 提供派发点，名称与原版一致；内容需要新的 Hook 时在内核增加派发点并补差分测试：

- 出牌：`ModifyEnergyCost`（含 Late）、`ModifyStarCost`、`ShouldPlay`、`BeforeCardPlayed`、`ModifyCardPlayCount`、`AfterModifyingCardPlayCount`、`ModifyCardPlayResultLocation`、`AfterCardPlayed`（含 Late）、`AfterEnergySpent`、`AfterStarsSpent`、`AfterCardExhausted`、`AfterHandEmptied`。
- 攻击与伤害：`BeforeAttack`、`ModifyAttackHitCount`、`AfterAttack`、`ModifyDamageAdditive`、`ModifyDamageMultiplicative`、`ModifyDamageCap`、`BeforeDamageReceived`、`ModifyHpLostBeforeOsty`（含 Late）、`ModifyHpLostAfterOsty`（含 Late）、`AfterModifyingHpLostAfterOsty`、`AfterBlockBroken`、`AfterCurrentHpChanged`、`AfterDamageGiven`、`AfterDamageReceived`（含 Late）。
- 格挡：`BeforeBlockGained`、`ModifyBlockAdditive`、`ModifyBlockMultiplicative`、`AfterModifyingBlockAmount`、`AfterBlockGained`、`ShouldClearBlock`、`AfterBlockCleared`。
- 死亡：`BeforeDeath`、`ShouldDie`（含 Late）、`AfterDeath`、`AfterPreventingDeath`。
- 回合：`BeforeSideTurnStart`、`AfterSideTurnStart`、`AfterEnergyReset`（含 Late）、`ModifyMaxEnergy`、`BeforeHandDraw`、`ModifyHandDraw`（含 Late）、`AfterPlayerTurnStart`、`AfterAutoPostPlayPhaseEntered`、`BeforeSideTurnEnd`（VeryEarly、Early、普通）、`OnTurnEndInHand`、`AfterSideTurnEnd`（含 Late）、`ShouldTakeExtraTurn`。
- 抽牌与牌堆：`ShouldDraw`、`AfterCardDrawn`（含 Early，以及被抽卡自身的 Hook）、`ModifyShuffleOrder`、`AfterShuffle`、`AfterCardChangedPiles`。
- Power：`ModifyPowerAmount`（遗物：UnsettlingLamp、SneckoSkull、RuinedHelmet）、`AfterPowerAmountChanged`（Shroud、SleightOfFlesh、Vicious）。
- 球：被动与激发；`OrbQueue.BeforeTurnEnd`。

## 4. 出牌管线

来源：`Q/SimulatedCombatState.cs:1552`、`S/CombatPredictionSimulator.Card.cs:122-330`、`S/CombatPredictionSimulator.CardExecutionContinuation.cs:29-127`、`S/CombatPredictionSimulator.CardTailContinuation.cs:16-125`、`P/CorePowerSupport.cs:21-407`。

1. **可否打出**：Enthralled 锁 → Unplayable 关键字 → 能量费用 → 星能费用 → `ShouldPlay` → `CardIsPlayable` 镜像 → Smog 苦难。能量费用 = 基础费用 → 本地修饰 → `ModifyEnergyCost` 普通阶段 → Late 阶段；`AreCardsFree` 置 0；钳制 ≥ 0。X 费等于当前全部能量。`ShouldPayExcessEnergyCostWithStars` 为真时超额能量按 2:1 用星能支付。
2. **支付**：重算两种费用和超额换算；记录 X 值与本次星能消耗；扣能量 → `AfterEnergySpent`；扣星能 → `AfterStarsSpent`。
3. **进入打出区**：卡从手牌移到打出区（不触发 `AfterCardChangedPiles`）；确定基础去向（复制品或能力牌 → 无；下次打出时消耗或带消耗关键字 → 消耗堆；其余 → 弃牌堆；个别卡牌覆写）；`ModifyCardPlayResultLocation`；打出次数 = 附魔重放 + 1 → `ModifyCardPlayCount` → `AfterModifyingCardPlayCount`；持有者已死则结束。
4. **每次打出**：战斗已结束则停止；`BeforeCardPlayed`；历史计数；执行卡牌效果；结算卡牌自身选择。
5. **卡牌效果顺序**：先执行旧引擎镜像部分（推断配方或专门镜像，即攻击、格挡、抽牌），再执行补偿部分（`CardEffectSpecRegistry` 与 `CorePowerSupport.ApplyCardPowers` 里的 Power、资源、生成效果）。**补偿效果总在镜像效果之后**，这是旧引擎的实际顺序，Flash 的卡牌定义分 `OnPlayMirrored` 与 `OnPlayCompensated` 两段实现。
6. **收尾**：持有者已死则中止；附魔 OnPlay → 苦难 OnPlay；`AfterCardPlayed` 普通阶段 → 重新读取监听列表后跑 Late 阶段（战斗正在结束也照常触发）；生命周期计数。
7. **去向**：仍在打出区的卡按去向处理（无 → 移出战斗；消耗 → `AfterCardExhausted`；否则加入目标牌堆）；不在嵌套效果中时触发 `AfterHandEmptied`；清理费用与目标。
8. **动作边界**：结算敌人死亡效果（`ApplyEnemyDeathPowers`）→ 胜负判定 → 处理被请求的回合结束。

自动打出（`S/CombatPredictionSimulator.AutoPlay.cs:21-73`）：不可打出、`ShouldPlay` 失败或无目标时直接移入去向；随机目标用 `CombatTargets` 流；不支付资源，X 值取当前能量。

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

Power 施加（`Q/SimulatedCombatState.cs:648-711`）：数量为 0 或目标不能接受 Power 时返回 → 遗物修正 → 目标有人工制品且是减益时抵消一层并返回 → 实例型 Power 新建实例，其余按（持有者、类型）叠加并钳制 → 玩家侧新获得的虚弱、易伤、脆弱标记跳过下次衰减 → 更新监听顺序 → 记录数量变化。旧引擎**没有**通用的 `AfterApplied`，只有少数 Power 初始化。

衰减：虚弱、易伤、脆弱、无实体、NoBlock 在**敌方回合结束时对所有生物**衰减并尊重跳过标记；Blur 在己方回合开始衰减；DoubleDamage 在每方回合结束衰减；瞬时 Power 在己方回合结束清除；临时力量、敏捷、集中在 `TriggerRegular` 中按监听顺序退回。

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
3. 能量：除非有保留能量效果，否则重置；加修正后的最大能量与下回合能量；`AfterEnergyReset`、遗物 `AfterEnergyReset`、`AfterEnergyResetLate`。
4. `BeforeHandDraw`：Power → 遗物 → 返回手牌的卡。
5. 抽牌数 = 5 + 下回合抽牌 + DrawCardsNextTurn → `ModifyHandDraw` → Late；抽牌。
6. `AfterPlayerTurnStart`（原版：先 Power，再遗物）。
7. 玩家己方回合开始效果在抽牌**之后**执行；若回合开始有选择，则在该选择之前执行。
8. 敌人死亡效果 → 附魔与球 Hook → 自动预打出 → 胜负判定。

**实现形状**（`FlashKernel.TurnPhases.cs`）

现有引擎的回合阶段有两种写法，Flash 分别对应：

- 按监听器顺序遍历 Power（或遗物）再按类型 `switch`：每个 `case` 是定义上的同名阶段方法（`FlashPowerDef.EndTurnRegular`、`BeforeHandDraw`、`AfterPlayerTurnStart` 等；遗物为 `FlashRelicDef` 的批次方法）。每一轮遍历开始时按监听器顺序（玩家、Osty、名单中的敌人，各自的 Power 按表内顺序）取快照，访问时按 (owner, id) 重新定位，层数必须仍为正。
- 固定类型序列（例如 `TriggerOwnerAfterSideTurnStart` 的 BiasedCognition → … → Clarity、`TriggerBaseSideTurnStart` 的 Blur → DrawCardsNextTurn → Plating → Slow）：内核保存从现有代码抄录的原版 Id 列表，只调用已登记且在场、层数为正的类型。

持有者范围与现有遍历一致：`AfterEnergyReset` 与 `AfterPlayerTurnStart` 只看玩家生物，`BeforeHandDraw` 包含 Osty（`power.Owner.Player == player`）。敌方回合的每个阶段都重新读取当前敌人名单，对应现有代码直接传入 `simulatedCombat.Enemies`。`ShouldClearBlock` 是原版 Hook，战斗结束中不枚举监听者；第一个拒绝清除的监听者通过 `BlockAfterPreventingClear` 结算保留量。

## 8. 抽牌与洗牌

来源：`S/CombatPredictionSimulator.CardPile.cs`。

- 小数抽牌数向上取整；`ShouldDraw` 失败则整次不抽。
- 逐张：结束中或手牌达到上限则停止；抽牌堆空且弃牌堆非空时洗牌；取抽牌堆顶入手；历史 → `AfterCardDrawn` Early → 普通 → 被抽卡自身 Hook。
- 洗牌：列表 = 弃牌堆（原顺序）+ 抽牌堆；按卡牌排序键稳定排序后用 `Shuffle` 流做不稳定洗牌（与 `StableShuffleProjection` 相同）；`ModifyShuffleOrder`；全部放到抽牌堆底；`ShuffleEventCount + 1`；`AfterShuffle`。排序键来自原版 `CardModel.CompareTo`，在根导入时编译进 `FlashRules`。
- 随机插入牌堆位置用 `Shuffle` 流的 `NextInt(n + 1)`。手牌满时加入手牌的卡改进弃牌堆。

## 9. 怪物 AI

旧引擎不运行原版状态机，而是解释它的图（`P/BranchMonsterAi.cs`）。Flash 在根导入时把每个敌人的状态机编译成表：

- 节点类型：行动节点（攻击列表：基础伤害与次数）、随机分支（各后继的基础权重）、条件分支（根捕获时的选择结果）。
- 推进：从当前行动的后继出发，随机分支用 `MonsterAi` 流按 `MonsterRandomBranchResolver.Pick` 的规则选择（依赖最近行动日志），条件分支取根选择，直到落到行动节点；最多 32 步。
- 特例：旧引擎在 `ResolveConditional`、`GetBranchWeight` 与 `Advance` 中按怪物类型写的特殊逻辑（KnowledgeDemon、FrogKnight、Fabricator、TestSubject、Ovicopter、LagavulinMatriarch、SlumberingBeetle、Queen、LivingShield、BowlbugRock、TwoTailedRat）由对应 `FlashMonsterDef` 覆写同名方法实现。
- 行动执行：攻击按编译表结算；非攻击效果由 `FlashMonsterDef` 的行动方法实现，规范来源是 `P/MonsterMoveEffects.cs` 与 `P/MonsterMoveSemantics.cs`。
- 私有字段：旧引擎通过 `_monsterIntStates` 读写的怪物私有整数，在 Flash 中映射到 `FlashEnemy` 的 AI 寄存器，寄存器编号由各怪物定义声明。

## 10. 选择协议

- `FlashAction` 携带至多 4 个选择，每个选择是"选项列表上的位集合"。选项列表由需要选择的效果按确定顺序生成（通常是来源牌堆中满足条件的卡的位置）。
- 效果请求第 k 个选择而动作只提供了不到 k 个时，内核停止执行，返回 `NeedsChoice` 与选项规格（来源、可选位置、最少与最多数量）。调用方丢弃这个半成品状态，枚举选项后从父状态重新执行同一动作。
- 回合开始的选择由 `EndTurn` 动作携带，时序标签与旧引擎 `PlanChoiceTiming` 对应。
- 物化时，Bridge 在旧引擎同一时点按位置取卡，生成旧引擎的 `PlanCardChoice` 令牌（卡牌 Id、升级、状态键与 occurrence）。

## 11. RNG

- 九条流与旧引擎一致：Shuffle、CombatCardGeneration、CombatPotionGeneration、CombatCardSelection、CombatEnergyCosts、CombatTargets、CombatOrbGeneration、MonsterAi、Niche。
- 每条流保存计数器与四个状态字，算法逐位复刻原版 `Rng`/`MegaRandom`，用纯内核检查对照原版类录制的输出向量。
- 每次取值计数器 +1，与原版一致；状态字和计数器都进入状态字节。
