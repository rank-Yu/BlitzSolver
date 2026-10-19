# FlashBattle 状态布局

[返回 Flash 入口](README.md)

`FlashBattle` 是一场战斗在某个稳定边界上的全部分支可变状态。它是 `unmanaged` 结构体：没有对象引用，复制即深拷贝，整块字节就是状态身份。

## 1. 归属原则

- **进入 `FlashBattle`**：会随分支变化、并影响后续合法动作或结算的值。范围以 AGENTS.md 第 5 节的状态清单和旧引擎状态键（`BuildStateKey`、`SimulatedCombatState.AppendFingerprint`）为下限；旧状态键漏掉但影响未来结算的值（例如回合数、Power 的 `AmountOnTurnStart`）同样进入。
- **进入 `FlashRules`**：根导入时冻结、整场搜索不变的值。包括卡牌各升级等级的基础数值与费用、怪物行动的静态参数与编译后的状态机、可生成怪物的闭包条目、遭遇槽名、根监听前缀顺序、最大能量基础值、手牌上限、药水槽数、根药水槽（用于区分生成药水与根药水）、随机药水池（战斗内与战斗外）、卡牌洗牌排序键、角色基础球槽数（`BaseOrbSlotCount`）。`FlashRules` 是不可变引用类型，所有分支共享。
- **都不进入**：纯 UI、日志、显示名、派生启发式值。历史日志只保留会影响结算的计数器。

## 2. 结构

容量是编译期常量，首版取值如下，以测量结果调整。所有数组都是 `[InlineArray]`。

| 区块 | 内容 | 首版容量 |
| --- | --- | --- |
| Header | 回合数、轮次、当前方、玩家阶段、终局结果、`ShufflesCrossed`、下一个 CombatId、标志位（玩家 Hook 激活、回合结束请求、正在结束） | — |
| Player | `FlashCreature`（HP、MaxHP、格挡、标志）、能量、星能、金币、手牌上限 | 1 |
| Osty | `FlashCreature` 与存在标志 | 1 |
| Enemies | `FlashEnemy`：`FlashCreature`、怪物 Id、规则条目下标、CombatId、遭遇槽下标、死亡阶段、当前状态机节点、最近行动日志、私有 AI 寄存器、跳过行动/强制行动标志 | 8 |
| Powers | `FlashPower`：Power Id、持有者、施加者、数量、`AmountOnTurnStart`、两个辅助计数、标志（跳过下次衰减、刚由敌人施加等） | 64 |
| Cards | `FlashCard`，五个牌堆连续存放：手牌、抽牌堆、弃牌堆、消耗堆、打出区 | 128 |
| Relics | `FlashRelic`：遗物 Id、计数、辅助值、标志（已熔化、本场已触发等） | 32 |
| Potions | 各槽药水 Id 与标志 | 5 |
| Orbs | `FlashOrb`：球 Id、`Aux`（Dark 的 `_evokeVal` / Glass 的 `_passiveVal`）；`OrbCount`、`OrbCapacity` | 10 |
| Counters | 按 `FlashCounter` 编号的玩家整数计数器：本回合计数（打出、开始打出、攻击、技能、格挡牌、消耗、弃牌、攻击次数、花费能量；玩家回合开始的 `BeginSideTurn` 清零，敌方回合保持；`StarsGainedThisTurn` 是本回合获得的正星能，供 Radiate 的 CalculatedHits 读取，同样在玩家回合开始清零）与本场累计失血、回复量、玩家未格挡受伤事件数（TearAsunder）。充能球另有两个战斗长计数器：`LightningChannels`（Voltaic 的闪电充能数，导入原版历史）与 `OrbChannels`（预测历史中的充能次数，对应 1000 次安全上限）。亡灵契约师的 `OstyAttacksThisTurn`（Rattle；仅当根捕获时原生 `Player.Osty` 存在才在玩家回合开始清零）、`DoomAppliedThisTurn`（DeathsDoor；玩家回合开始清零）、`EtherealPlays`（BansheesCry/PullFromBelow；战斗长、导入根历史）与 `CardsGeneratedThisCombat`（Supermassive 的生成卡乘数；导入 `CountGeneratedCards`，每次 `CardGenerated` 加一，包含战斗结束时被拒绝加入的生成卡）。另有两个苦难归一化计数：`VitalSparkNormalizedAmount`（`_lastNormalizedVitalSparkAmount`，`int.MinValue` 表示 null）与 `SkillPlayedThisSideTurn`（`_skillsPlayedThisTurn`，本侧回合末清零）。生物的本回合失血标记在 `FlashCreatureFlags.LostHpThisTurn`，换边或换轮清除；`DampenCasterMask` 是 MagiKnight 对玩家施加 DampenPower 的施法者位集合（按敌人 owner 下标一位一个）。MB 追加三个：`GeneratedPotionUseCount` 统计本场生成后又被使用的药水（生成 = 使用时的槽位药水不等于根药水；评分加入 `PotionUseCount`），`GeneratedPotionStrategicCost` 低 16 位打包显式用药的战略成本、高 16 位打包自动用药（Fairy in a Bottle）的成本，`EntropicFreePotionMask` 是 5 个药水槽的免费位（第 i 位对应槽 i；使用该槽时清除，免费药水成本为 0） | 48 |
| Rng | 九条战斗 RNG：计数器加四个 64 位状态字 | 9 |

### 2.1 FlashCard

`FlashCard` 是卡牌的完整值，当前 92 字节。字段集以旧引擎卡牌状态键（`CaptureCardStateFingerprintForTesting`、`CardChoiceSupport.ChoiceCardKey`）为准：

- 卡牌 Id、升级等级、本地关键字位图；
- 费用：基础费用（含 X 费标志）、费用修饰栈（本场、本回合、直到打出、临时；容量 16，Stomp 每打出一张攻击追加一条 EndOfTurn 修饰）、星能费用；修饰数量使用独立字节，不再压进 `CostState` 的两位；
- 状态标志：下次打出时消耗、本回合狡诈、本回合保留、是否牌组卡、已移出战斗；
- `BaseReplayCount`；
- 语义动态变量相对规则表基础值的增量（两格），以及少数卡牌的私有计数（Claw、Rampage、GeneticAlgorithm、Maul、MadScience、TheScythe 这类）；
- 附魔 Id、数量、状态，以及 Glam、Momentum 这类附魔私有字段；
- 苦难 Id 与数量；
- `IsClone`（原版 `CardModel.IsClone`）：占用 `CostState` 的 bit 3（bit 0–2 是临时星能费用标志，bit 4 是 `ReturnToHandNextTurn`，bit 5–7 是附魔状态）；克隆卡跳过 `AfterCardEnteredCombat` 的卡牌专属部分（Stomp 等），由 `CloneCard` 置位，导入时从原版卡读取。
- `ReturnToHandNextTurn`（Bolas/ThrummingHatchet，`CostState` bit 4）：对应 `SimulatedCombatState._returnToHandNextTurn` 的成员关系；导入时按原牌对象投影，`BeforeHandDraw` 的返回手牌阶段清位。
- `IsClone`（原版 `CardModel.IsClone`）：占用 `CostState` 的 bit 3（bit 0–2 是临时星能费用标志，bit 5–7 是附魔状态）；克隆卡跳过 `AfterCardEnteredCombat` 的卡牌专属部分（Stomp 等），由 `CloneCard` 置位，导入时从原版卡读取。
- `DampenOriginalUpgrade`：卡牌被 MagiKnight 的 DampenPower 降级前的升级等级加一（0 表示未降级）。`DowngradeCard` 在把卡牌重置到 0 级动态变量、基础费用与本地关键字时写入；`RestoreDampenedCards` 在最后一个施法者死亡时按该值逐级恢复。

`FlashCardFlags.PowerAfflictionChecked`（bit 7）对应 `PredictedCard.HasCheckedPowerAfflictionEntry`：根导入时置位，根之后生成的卡清零，第一次归一化后置位；`FlashKeywords.CreatedAfterRoot`（bit 7）不是原生关键字，对应 `_liveCardsAtSnapshot` 之外的卡，只用于一次性苦难进场检查。卡牌尺寸仍是 90 字节。

卡牌没有实例身份。牌堆里两张完全相同的打击字节相同，可以互换。路线和选择都按位置记录。

### 2.2 牌堆

五个牌堆共用一个 `Cards` 数组，按 `手牌 | 抽牌堆 | 弃牌堆 | 消耗堆 | 打出区` 顺序连续排列，各自记录数量。抽牌堆顶是抽牌堆段的第一个元素，与旧引擎 `SimCardPile` 的"索引 0 为顶"一致，所以抽一张顶牌入手只需移动两个计数。其余跨堆移动用段内平移完成。

### 2.3 Power 顺序

`Powers` 按持有者分组，组的顺序就是 Hook 监听顺序里各生物的顺序；组内按获得顺序排列。新 Power 追加到持有者组末尾，对应旧引擎"插在持有者最后一个 Power 之后"的规则。数量归零的 Power 立即移除并压实，之后重新获得时排在组末，对应旧引擎退休根槽位的语义。实例型 Power 每个原生实例一行，同一 `(owner, id)` 的多行按获得顺序排列；Hook 与阶段遍历保存 occurrence 以定位到确切实例，而不是只按 `(owner, id)` 取第一行。

### 2.4 编码约定

- 药水槽、附魔、苦难存"内部 Id + 1"，0 表示空。卡牌、Power、遗物、怪物直接存内部 Id。
- `FlashCard.CostState` 的 bit 3 是 `IsClone`、bit 4 是 `ReturnToHandNextTurn`；设置/清除临时星能费用和附魔状态时必须保留这两位。
- 敌人是否为主要敌人不存标志，与旧引擎一样由其持有的 Power 推出（`OwnerIsSecondaryEnemy`）。
- Osty 只在 `Osty` 区块与 `OstyPresent` 标志中表示（死亡后对象仍在，`Osty.CurrentHp <= 0` 即死亡）；`OstyWasRootPet`（`FlashBattleFlags` bit 5）表示根捕获时原生 `Player.Osty` 非空，供 `BeginSideTurn(player)` 的宠物计数清零语义使用，整场不变。`Osty` 的 Power 与其他生物一样占 `Powers` 行，`Owner = FlashLimits.OstyOwner`。
- `OblivionPower` 的“本张牌打出时的层数”只存在栈上的 `FlashExec.CardPlayPairs`（`FlashCardPlayPairKind.OblivionDoom`）；稳定边界为空，不进入布局。
- `SandpitPower` 的原生实例型目标存在 `FlashPower.Target`（`byte` owner，`NoOwner` 表示无目标）；每个原生 `ApplyTargeted` 实例一行。`FlashKernel.SetPowerAmountAt` 按行精确减层，避免同 (owner, id) 的实例互相覆盖。
- Knowledge Demon 的固定选项选择（两张诅咒卡）不进入牌堆：候选值只存在栈上的 `FlashChoiceRequest.GeneratedOptions`，选择后从父状态重执行。选择种类为 `FlashChoiceKind.SelectOption`，`FlashChoiceEffect.ApplyKnowledgeCurse`。
- `FlashEnemy.Rules` 是 `FlashRules` 怪物规则表的下标。根已知敌人按 `KnownEnemies` 下标占据表的前 `RootEnemyCount` 项，之后每个可生成怪物类型一项；已知敌人数组只追加，所以新敌人的规则编号由怪物 Id 查可生成表（`FlashRules.SpawnRulesId`）。
- `FlashEnemy.Slot` 是遭遇槽下标（`FlashRules.EncounterSlots` 的顺序），没有槽名时为 255。名单（`Roster`）按槽下标稳定升序，255 视作 -1 排在有名字的槽位之前；名单顺序就是 Hook 监听与敌方行动顺序。
- `FlashEnemyFlags.PerformedMoveThisRound`、`ForcedMustPerformAfterMove` 与 `ActingThisRound` 只在敌方回合内部使用，任何稳定边界上都为 0。
- `FlashEnemy.ForcedMove` 是合成 STUNNED 或 REVIVE_MOVE 节点的动态后继（原版 `MoveState("STUNNED"/"REVIVE_MOVE").FollowUpStateId`），当前节点不是这两个合成节点时恒为 255。布局不因此改变。
- `FlashOrb.Aux` 是球唯一的私有整数：DarkOrb 的 `_evokeVal`（初始 6，被动累加）或 GlassOrb 的 `_passiveVal`（初始 4，被动后递减）；Lightning/Frost/Plasma 恒为 0。导入时要求它是可表示的整数；未用槽位清零。球的顺序就是 `OrbQueue.Orbs` 的顺序，容量来自 `SimOrbQueue.Capacity`。
- `FlashBattle.NextCombatId` 是旧引擎的 `_nextCreatureId`；生成敌人时先取当前值再递增。
- 怪物行动日志存"曾经用过的行动位集"加最近 8 条；原版重复规则只读这两部分。冷却或连续次数超出 8 的状态机在运行时抛 `FlashUnsupportedException`。
- `FlashEnemy.PoweredAttackHitsThisTurn` 只在玩家回合内累计、玩家回合开始清零，稳定边界上是普通计数；它不改变布局。
- 生成候选池、随机药水池与按原卡展开的变化池只存在 `FlashRules`（根冻结、非分支状态）。生成选项（Discovery/Splash）只存在栈上的 `FlashChoiceRequest.GeneratedOptions`，不进入 `FlashBattle`；需要选择时整个动作从父状态重执行。
- 出牌期间的金额快照（Afterimage/Strangle，现有引擎的 `CardPlayPairPredictionState`）只存在栈上的 `FlashExec.CardPlayPairs`（容量 `FlashLimits.CardPlayPairs`）；稳定边界为空，不改变布局。弃牌与 Sly 自动打出只移动既有牌值、使用既有 `CardsDiscardedThisTurn` 计数器，也不改变布局。

## 3. 规范化

同一局面必须只有一种字节表示，否则去重失效；不同局面绝不能字节相同。

- 未使用的槽位、数组尾部和填充字节全部为零。移除元素时整体前移并清零末尾。
- 牌堆保持旧引擎里的真实顺序，不排序。旧引擎在部分牌堆上忽略顺序（`PileOrderInvariantMask`），Flash 首版不做这种合并：只会少合并，不会错误合并。
- 布尔值只用 0 和 1。
- `FlashBattleFlags.HasRisk` 是旧引擎快照的未补偿风险（`SimulationSnapshot.HasRisk`），只在根导入时
  置位并保持到战斗结束；Discovery/Splash 的 `UnresolvedPlayerChoice` 被补偿目录标为已补偿，内核不置位。
  它不改变结构体大小。
- 结构体布局用 `StructLayout(LayoutKind.Sequential)`，并有一个检查断言 `Unsafe.SizeOf<FlashBattle>()` 与预期一致，防止字段改动悄悄引入填充。

## 4. 指纹与相等

- 指纹是 64 位哈希，只覆盖各区块已使用的部分（头部、前 `EnemyCount` 个敌人、前 `PowerCount` 个 Power 等），四路并行混合；未用槽位按规范化恒为零，所以不影响区分能力。指纹只用于分桶。
- 相等只看字节，用 `SequenceEqual`。搜索去重在指纹命中后必须再比较完整字节，不允许仅凭哈希合并。
- Flash 的去重只在 Flash 内部使用，不写入旧引擎的 `StateFingerprint` 或 `ContinuationStamp`。

## 5. 容量与失败

- 任何写入超过容量都抛 `FlashCapacityException`，携带区块名与容量。搜索捕获后放弃本次 Flash 求解并回落旧后端；这是唯一允许的捕获点。
- 准入阶段按牌组规模和可生成内容估计上限，明显超出容量的根直接不准入，避免搜索中途失败。
- 数值钳制与旧引擎一致（HP、格挡、能量、Power 数量钳制在 ±999,999,999），由内核统一实现。

## 6. 尺寸预算

`FlashEnemy` 仍是 64 字节：`Rules` 占用原 `_padding` 的第一个字节，`PoweredAttackHitsThisTurn`（BeatIntoShape 的每目标本回合强化攻击命中数，0..255，溢出显式拒绝）占用原 `_padding` 的第二个字节，剩余两个字节继续填充。`FlashOrb` 为 8 字节（`Id` + `Aux`），DF 没有改变它的大小。`FlashCard` 为 92 字节（96 张，费用修饰容量 16，新增 `DampenOriginalUpgrade`），`FlashBattle` 实测 12000 字节。KA 的苦难/附魔只使用既有的卡牌字段、`FlashCardFlags` 的最后一个空闲位和追加的两个 `FlashCounter`，没有改变任何区块尺寸；A3 的 Dampen 记录追加一个卡牌字段（`DampenOriginalUpgrade`）与一个计数器（`DampenCasterMask`），因此卡牌与战斗各增大 2 字节和 128 字节。K0 在同一台机器上测得 `copyOnly` 约 307 ns、`copyStep` 约 1.85 µs。若复制成本成为瓶颈，优先缩小 `FlashCard` 和卡牌容量，而不是引入写时复制或分层状态。

Power 数量变化队列不进入 `FlashBattle`：它只存在于栈上的 `FlashExec.PendingPowerChanges`（容量 64），并在每个稳定边界前由 `ResolvePowerAmountChanges` 排空；排空先把当前批次复制到局部 `drained` 再清空队列，嵌套排空不会消费外层批次。旧引擎同样要求 Fork 前 `_pendingPowerAmountChanges` 为空。RupturePower 的 `Aux2`（曾经暂存伤害来源标记）已不再使用，伤害来源直接放在栈上的 `FlashDamageResult`。
