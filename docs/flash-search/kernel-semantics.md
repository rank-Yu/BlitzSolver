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
- 修正类 Hook（`ModifyDamage*`、`ModifyHpLost*`、`ShouldClearBlock` 等）不得改写状态：预报与评估会在只读语义下调用它们。需要随攻击捕获的值在 `BeforeAttack` 中记录（例如活力，对应 `VigorPowerMirrors.BeforeAttack`）。
- 战斗正在结束时，战斗监听者的 Hook 返回空，`AfterCardPlayed`、`AfterBlockBroken`、`AfterAttack` 除外；伤害和死亡类的跑局监听者不受此限（`M/HookMirrors.cs:1351`）。
- 只有原版监听者时，旧引擎对回合阶段 Hook、Power 反应、最大能量与抽牌数、`BeforeHandDraw` 使用"先全部 Power、再全部遗物"的批量顺序，不按逐监听者顺序。Flash 在相同位置用相同的批量顺序。这一偏差以旧引擎为准。

### 3.3 Hook 目录

内核为下列 Hook 提供派发点，名称与原版一致；内容需要新的 Hook 时在内核增加派发点并补差分测试：

- 出牌：`ModifyEnergyCost`（含 Late）、`ModifyStarCost`、`ShouldPlay`、`BeforeCardPlayed`、`ModifyCardPlayCount`、`AfterModifyingCardPlayCount`、`ModifyCardPlayResultLocation`、`AfterCardPlayed`（含 Late）、`AfterEnergySpent`、`AfterStarsSpent`、`AfterCardExhausted`、`AfterHandEmptied`。
- 攻击与伤害：`BeforeAttack`、`ModifyAttackHitCount`、`AfterAttack`、`ModifyDamageAdditive`、`ModifyDamageMultiplicative`、`ModifyDamageCap`、`BeforeDamageReceived`、`ModifyHpLostBeforeOsty`（含 Late）、`ModifyHpLostAfterOsty`（含 Late）、`AfterModifyingHpLostAfterOsty`、`AfterBlockBroken`、`AfterCurrentHpChanged`、`AfterDamageGiven`、`AfterDamageReceived`（含 Late）。
- 格挡：`BeforeBlockGained`、`ModifyBlockAdditive`、`ModifyBlockMultiplicative`、`AfterModifyingBlockAmount`、`AfterBlockGained`、`ShouldClearBlock`、`AfterBlockCleared`。
- 死亡：`BeforeDeath`、`ShouldDie`（含 Late）、`AfterDeath`、`AfterPreventingDeath`。
- 已有派发点（其余见本节列表）：`BeforeDamageReceived`（`TryDamageTarget` 中修正后、格挡前，不受战斗结束闸门限制）、`BeforeSideTurnEnd` 三轮（玩家回合结束第一阶段，受闸门限制，参与者以位集合传入）、`ShouldClearBlock`、`ModifyMaxEnergy`、`ModifyHandDraw`（含 Late）与第 7 节的 Power/遗物阶段方法。
- 回合：`BeforeSideTurnStart`、`AfterSideTurnStart`、`AfterEnergyReset`（含 Late）、`ModifyMaxEnergy`、`BeforeHandDraw`、`ModifyHandDraw`（含 Late）、`AfterPlayerTurnStart`、`AfterAutoPostPlayPhaseEntered`（阶段切到 AutoPostPlay 后、切到 End 之前，卡牌与 Power 监听者；HowlFromBeyond、StampedePower）、`BeforeSideTurnEnd`（VeryEarly、Early、普通）、`OnTurnEndInHand`、`AfterSideTurnEnd`（含 Late）、`ShouldTakeExtraTurn`。
- 抽牌与牌堆：`ShouldDraw`、`AfterCardDrawn`（含 Early，以及被抽卡自身的 Hook；HellraiserPower 在 Early 自动打出刚抽到的 Strike）、`ModifyShuffleOrder`、`AfterShuffle`、`AfterCardChangedPiles`。
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

药水（`Q/CombatBeamSolver.Expansion.Replay.cs` 的 `UsePotion` 分支与 `P/PotionExecutionSupport.cs`）：清空槽位（使用记录只供搜索评分）→ `BeforePotionUsed` → 药水效果（`FlashPotionDef.OnUse`，对应 `PotionOnUseSupport.Use` 的分支；自身药水作用于玩家）→ 药水选择 → 玩家存活时 `AfterPotionUsed` → 敌人死亡效果；之后与出牌相同：洗牌计数、胜负判定、处理被请求的回合结束。药水的目标类型与变量（含伤害、格挡变量的 ValueProp）从规范药水编译进规则表；合法目标按 `TargetsForPotion`（任一敌人逐个可命中敌人；自身/任一玩家在玩家存活时无目标；全体敌人与无生物目标无目标）。

### 4.1 自动打出

来源：`S/CombatPredictionSimulator.AutoPlay.cs:21-73`、`S/CombatPredictionSimulator.AutoPlayContinuation.cs`。

`FlashKernel.AutoPlay` 与 `ManualPlay` 共用 `PlayFromPlay`（结果牌堆、每次打出、`BeforeCardPlayed`、效果、`OwnChoice`、`AfterCardPlayed` 普通与 Late、收尾），只替换资源与目标两步：

1. 战斗结束/正在结束或拥有者死亡时不打出。卡牌不在任何牌堆时先加入打出区（触发 `AfterEnteredCombat`）。
2. 不可打出关键字、`ShouldPlay` 拒绝或无法解析目标时，`MoveToResultPileWithoutPlaying`：先加入打出区，再按 `ExhaustOnNextPlay`/消耗关键字进入消耗堆，否则进入弃牌堆；不触发任何出牌 Hook、计数或费用清理。
3. 不支付能量/星能；`SpendResources(isAutoPlay: true, skipXCapture)` 只在 `!skipXCapture` 时把当前费用写入本次 X 值（X 费卡取当前能量），并记录星能值。`ManualCardsPlayedThisTurn` 不计自动打出。
4. 目标：`AnyEnemy` 用 `CombatTargets` 流从 `HittableEnemies`（名单顺序、排除复活/永久死亡）取随机目标，候选为空时不消耗流；单人没有其他队友，`AnyAlly` 只有调用方已给出目标才成立；其他目标类型直接通过。
5. `AutoPlayFromDrawPile` 先按现有引擎把最多 `count` 张抽牌堆顶牌移到打出区，再按顺序逐张自动打出；每张打出前写入 `ExhaustOnNextPlay = forceExhaust`（false 也写入）。打出的卡结果去向为消耗堆时清除该标志（`CardModel.GetResultLocationForCardPlay`），未打出而直接移入去向时保留（`MoveToResultPileWithoutPlaying`）。
6. 自动打出中的卡牌自身选择走现有嵌套选择协议：`OwnChoice` 请求选择、动作未携带时返回 `NeedsChoice`，调用方从父状态带选择重执行。生成选项（Discovery/Splash）的选择协议见 4.2。

### 4.2 卡池随机生成与变化

来源：`E/CombatCardGenerationExtensions.cs`、`S/CombatPredictionSimulator.AutoPlayContinuation.cs`、`P/CardOnPlaySupport.Batch042.cs`、`Q/CardChoiceResolution.cs`。

- **候选池在 Bridge 冻结。** 定义用 `FlashCardPools` 的键声明可能生成的池；`FlashRulesCompiler` 用现有引擎的同一枚举（`player.GetUnlocked...` + `FilterForCombatAndPlayerCount`，再按调用点的类型/费用/排除谓词过滤）把候选按原顺序写进 `FlashRules`。内核只按 RNG 取样，不重新过滤。
- **取样。** 去重（`TakeRandom`）先对整个候选列表做一次 Fisher-Yates（`n-1` 次 `Shuffle` 流 `NextInt(n+1)`），再取前 `count` 张；有放回（`NextItem`）每张消耗一次 `CombatCardGeneration` 流 `NextInt(0, n)`。候选为空时不消耗 RNG。生成的卡按 `CreateCard` 创建为 0 级规范卡，`UpgradeIf` 再升级一次。
- **加入战斗。** `AddGeneratedCard` 对应 `AddGeneratedCardsToCombat` 的单卡路径：手牌已满改投弃牌堆，按 `Bottom`/`Top`/`Random` 插入，调用卡的 `AfterEnteredCombat`。`SetToFreeThisTurn` 写入绝对 0 能量修饰（EndOfTurn|WhenPlayed）和 0 临时星能费用（回合末清除）。
- **生成选项选择。** Discovery/Splash 先记录生成的选项并写入风险（旧引擎的 `History.CardGenerationOptions` + `RecordRisk(UnresolvedPlayerChoice)`），随后 `GetSpec` 的 `GenerateToHand` 分支在 `OwnChoice` 提问。Flash 把选项值直接放进 `FlashChoiceRequest.GeneratedOptions`（不在任何牌堆），选择后再 `CloneCard`、`SetToFreeThisTurn`、加入手牌；空选项也提一个空范围选择，与 `RangeSpec` 一致。
- **变化。** `ReplaceCard` 对应 `CardCmd.Transform` 的现有引擎结算：从原牌堆移除原卡（`RemoveFromCombat`），再把替换卡插回 `min(原下标, 当前长度)` 并调用 `AfterEnteredCombat`；不触发 `AfterCardChangedPiles`。Flash 没有实例身份，按位置定位；PrimalForce 对 snapshot 的手牌攻击按升序原位替换为 GiantRock（PrimalForce 升级时 GiantRock 升 1 级）。
- **随机药水。** 一次 `CombatPotionGeneration` `NextFloat` 选稀有度（Rare ≤ 0.1、Uncommon ≤ 0.35、否则 Common），再一次 `NextItem` 在冻结的同稀有度池中选；`TryProcurePotion` 放入第一个空槽，成功时记录 `LongTermResourceValue + 20`。
- **风险。** `FlashBattleFlags.HasRisk` 只表示旧快照的**未补偿**风险（`snapshot.HasRisk`）；Discovery/Splash 记录 `UnresolvedPlayerChoice` 后置位，之后整场保持。根导入从快照读取该位；内核不尝试对其它风险原因做补偿分类。

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

**空槽**：`FlashKernel.NextFreeSlot`/`LastFreeSlot` 与 `SimulatedCombatState` 同义：战斗没有遭遇时返回 `null`；有空槽时返回遭遇槽名；`NextFreeSlot` 在无空槽时返回 `""`，`LastFreeSlot` 在无空槽时返回 `null`。

**幻象与爪牙**：`EyeWithTeeth` 入场时先获得 IllusionPower 1（applier 为自己）再获得 MinionPower 1（无 applier），与 `ApplyNativeEntrancePowers` 一致。IllusionPower 的 `PreventsRemovalAfterDeath` 让尸体留在名单；其 `AfterDeath` 对应 `BeginIllusionRevive`：死亡阶段改为 `Reviving`，强制合成 `REVIVE_MOVE` 节点，动态后继取最近一条非复活行动日志。规则编译在 `IllusionRevive` 为真的怪物上追加该合成节点（无攻击、必须先执行一次），节点下标存在 `FlashMonsterRules.ReviveNode`。`REVIVE_MOVE` 执行时把生命回满并清回复活阶段，随后按动态后继推进。尸体的 Power 清理按 `RemovePowersAfterDeath`：有 IllusionPower 时保留增益与临时减益、移除其余减益；已离开名单的敌人清空全部 Power。MinionPower 的 `OwnerIsSecondaryEnemy` 让主要敌人死亡时连带杀死剩余爪牙；`MinionPower.ShouldPowerBeRemovedAfterOwnerDeath` 为假。

**TwoTailedRat**：AI 寄存器 0 是 `_turnsUntilSummonable`（初始 2），1 是 `_callForBackupCount`（初始 0）。`SCRATCH_MOVE`/`DISEASE_BITE_MOVE`/`SCREECH_MOVE` 递减前者；`CALL_FOR_BACKUP_MOVE` 在 `LastFreeSlot` 生成一只新鼠，再把名单中所有鼠的后一个寄存器同步为（最大值 + 1）。随机分支权重按 `BranchMonsterAi.GetBranchWeight`：可召唤时 CALL_FOR_BACKUP 为 0.75、其余三者各 1/12；不可召唤时 CALL_FOR_BACKUP 为 0、其余为 1。可召唤条件按 `CanTwoTailedRatSummon`：回合计数不大于 0、计数小于 3、`NextFreeSlot` 非空，且其他鼠当前行动不是 CALL_FOR_BACKUP。`SCREECH_MOVE` 还给玩家 1 层 Frail。

**导入认领**：`_monsterIntStates` 的每个条目必须被某个已知敌人的 `AiRegisterNames` 或 `InertMonsterIntNames` 认领，否则按 `UnrepresentableState` 拒绝；声明为寄存器的名字取 `GetMonsterInt` 的值（有捕获条目用条目，没有时读模型字段），因此与现有引擎的无条目语义一致。乐加维林族母的 `_isAwake` 由 `InertMonsterIntNames` 声明。
