# FlashBattle 状态布局

[返回 Flash 入口](README.md)

`FlashBattle` 是一场战斗在某个稳定边界上的全部分支可变状态。它是 `unmanaged` 结构体：没有对象引用，复制即深拷贝，整块字节就是状态身份。

## 1. 归属原则

- **进入 `FlashBattle`**：会随分支变化、并影响后续合法动作或结算的值。范围以 AGENTS.md 第 5 节的状态清单和旧引擎状态键（`BuildStateKey`、`SimulatedCombatState.AppendFingerprint`）为下限；旧状态键漏掉但影响未来结算的值（例如回合数、Power 的 `AmountOnTurnStart`）同样进入。
- **进入 `FlashRules`**：根导入时冻结、整场搜索不变的值。包括卡牌各升级等级的基础数值与费用、怪物行动的静态参数与编译后的状态机、可生成怪物的闭包条目、遭遇槽名、根监听前缀顺序、最大能量基础值、手牌上限、药水槽数、卡牌洗牌排序键。`FlashRules` 是不可变引用类型，所有分支共享。
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
| Orbs | `FlashOrb`：球 Id、累积值；容量与数量 | 10 |
| Counters | 按 `FlashCounter` 编号的玩家整数计数器：本回合计数（打出、开始打出、攻击、技能、格挡牌、消耗、弃牌、攻击次数、花费能量；玩家回合开始的 `BeginSideTurn` 清零，敌方回合保持）与本场累计失血、回复量、玩家未格挡受伤事件数（TearAsunder）。另有两个苦难归一化计数：`VitalSparkNormalizedAmount`（`_lastNormalizedVitalSparkAmount`，`int.MinValue` 表示 null）与 `SkillPlayedThisSideTurn`（`_skillsPlayedThisTurn`，本侧回合末清零）。生物的本回合失血标记在 `FlashCreatureFlags.LostHpThisTurn`，换边或换轮清除 | 48 |
| Rng | 九条战斗 RNG：计数器加四个 64 位状态字 | 9 |

### 2.1 FlashCard

`FlashCard` 是卡牌的完整值，当前 90 字节。字段集以旧引擎卡牌状态键（`CaptureCardStateFingerprintForTesting`、`CardChoiceSupport.ChoiceCardKey`）为准：

- 卡牌 Id、升级等级、本地关键字位图；
- 费用：基础费用（含 X 费标志）、费用修饰栈（本场、本回合、直到打出、临时；容量 16，Stomp 每打出一张攻击追加一条 EndOfTurn 修饰）、星能费用；修饰数量使用独立字节，不再压进 `CostState` 的两位；
- 状态标志：下次打出时消耗、本回合狡诈、本回合保留、是否牌组卡、已移出战斗；
- `BaseReplayCount`；
- 语义动态变量相对规则表基础值的增量（两格），以及少数卡牌的私有计数（Claw、Rampage、GeneticAlgorithm、Maul、MadScience、TheScythe 这类）；
- 附魔 Id、数量、状态，以及 Glam、Momentum 这类附魔私有字段；
- 苦难 Id 与数量；
- `IsClone`（原版 `CardModel.IsClone`）：占用 `CostState` 的 bit 3（bit 0–2 是临时星能费用标志，bit 5–7 是附魔状态）；克隆卡跳过 `AfterCardEnteredCombat` 的卡牌专属部分（Stomp 等），由 `CloneCard` 置位，导入时从原版卡读取。

`FlashCardFlags.PowerAfflictionChecked`（bit 7）对应 `PredictedCard.HasCheckedPowerAfflictionEntry`：根导入时置位，根之后生成的卡清零，第一次归一化后置位；`FlashKeywords.CreatedAfterRoot`（bit 7）不是原生关键字，对应 `_liveCardsAtSnapshot` 之外的卡，只用于一次性苦难进场检查。卡牌尺寸仍是 90 字节。

卡牌没有实例身份。牌堆里两张完全相同的打击字节相同，可以互换。路线和选择都按位置记录。

### 2.2 牌堆

五个牌堆共用一个 `Cards` 数组，按 `手牌 | 抽牌堆 | 弃牌堆 | 消耗堆 | 打出区` 顺序连续排列，各自记录数量。抽牌堆顶是抽牌堆段的第一个元素，与旧引擎 `SimCardPile` 的"索引 0 为顶"一致，所以抽一张顶牌入手只需移动两个计数。其余跨堆移动用段内平移完成。

### 2.3 Power 顺序

`Powers` 按持有者分组，组的顺序就是 Hook 监听顺序里各生物的顺序；组内按获得顺序排列。新 Power 追加到持有者组末尾，对应旧引擎"插在持有者最后一个 Power 之后"的规则。数量归零的 Power 立即移除并压实，之后重新获得时排在组末，对应旧引擎退休根槽位的语义。

### 2.4 编码约定

- 药水槽、附魔、苦难存"内部 Id + 1"，0 表示空。卡牌、Power、遗物、怪物直接存内部 Id。
- `FlashCard.CostState` 的 bit 3 是 `IsClone`；设置/清除临时星能费用和附魔状态时必须保留该位。
- 敌人是否为主要敌人不存标志，与旧引擎一样由其持有的 Power 推出（`OwnerIsSecondaryEnemy`）。
- `FlashEnemy.Rules` 是 `FlashRules` 怪物规则表的下标。根已知敌人按 `KnownEnemies` 下标占据表的前 `RootEnemyCount` 项，之后每个可生成怪物类型一项；已知敌人数组只追加，所以新敌人的规则编号由怪物 Id 查可生成表（`FlashRules.SpawnRulesId`）。
- `FlashEnemy.Slot` 是遭遇槽下标（`FlashRules.EncounterSlots` 的顺序），没有槽名时为 255。名单（`Roster`）按槽下标稳定升序，255 视作 -1 排在有名字的槽位之前；名单顺序就是 Hook 监听与敌方行动顺序。
- `FlashEnemyFlags.PerformedMoveThisRound`、`ForcedMustPerformAfterMove` 与 `ActingThisRound` 只在敌方回合内部使用，任何稳定边界上都为 0。
- `FlashEnemy.ForcedMove` 是合成 STUNNED 或 REVIVE_MOVE 节点的动态后继（原版 `MoveState("STUNNED"/"REVIVE_MOVE").FollowUpStateId`），当前节点不是这两个合成节点时恒为 255。布局不因此改变。
- `FlashBattle.NextCombatId` 是旧引擎的 `_nextCreatureId`；生成敌人时先取当前值再递增。
- 怪物行动日志存"曾经用过的行动位集"加最近 8 条；原版重复规则只读这两部分。冷却或连续次数超出 8 的状态机在运行时抛 `FlashUnsupportedException`。
- 生成候选池、随机药水池与按原卡展开的变化池只存在 `FlashRules`（根冻结、非分支状态）。生成选项（Discovery/Splash）只存在栈上的 `FlashChoiceRequest.GeneratedOptions`，不进入 `FlashBattle`；需要选择时整个动作从父状态重执行。

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

`FlashEnemy` 仍是 64 字节：`Rules` 占用原 `_padding` 的第一个字节，其余三个字节继续填充。`FlashCard` 为 90 字节（96 张，费用修饰容量 16），`FlashBattle` 实测 11808 字节。KA 的苦难/附魔只使用既有的卡牌字段、`FlashCardFlags` 的最后一个空闲位和追加的两个 `FlashCounter`，没有改变任何区块尺寸。K0 在同一台机器上测得 `copyOnly` 约 307 ns、`copyStep` 约 1.85 µs。若复制成本成为瓶颈，优先缩小 `FlashCard` 和卡牌容量，而不是引入写时复制或分层状态。

Power 数量变化队列不进入 `FlashBattle`：它只存在于栈上的 `FlashExec.PendingPowerChanges`（容量 64），并在每个稳定边界前由 `ResolvePowerAmountChanges` 排空；排空先把当前批次复制到局部 `drained` 再清空队列，嵌套排空不会消费外层批次。旧引擎同样要求 Fork 前 `_pendingPowerAmountChanges` 为空。RupturePower 的 `Aux2`（曾经暂存伤害来源标记）已不再使用，伤害来源直接放在栈上的 `FlashDamageResult`。
