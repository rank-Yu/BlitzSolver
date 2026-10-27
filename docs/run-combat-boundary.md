# 战斗边界：进点与开战

[返回文档索引](README.md)

本文记录战斗边界的两半：R2a-2 的建局从停在地图选点的 `RunState` 进入战斗地图点，生成
遭遇，建立第一个玩家出牌阶段的 `Battle`；R2a-3 的写回在战斗结束 Hook 之后、奖励生成之前
把终局 `Battle` 写回 `RunState`。战后奖励、事件与地图选择不在本文范围。

## 1. 原版路径

以下顺序对应 sts2 `v0.111.0` 的单人标准模式。行号与类名只用于对照，不是 Blitz 的依赖。

| # | 步骤 | 读取 | 写入 | 随机数 |
| ---: | --- | --- | --- | --- |
| 1 | `RunManager.EnterMapCoord` | 地图点、已访问坐标 | `VisitedMapCoords`、`NextRoomId=0` | — |
| 2 | `EnterMapPointInternal` | `CurrentActIndex`、地图点类型 | `ActFloor = coord.row + 1` | — |
| 3 | `ExitCurrentRooms` | `_currentRooms` | 逐个 `PopCurrentRoom`，执行 `Room.Exit` | — |
| 4 | `RollRoomTypeFor` | 地图点类型、未知点黑名单、`Odds.UnknownMapPoint` | — | 未知点：`UnknownMapPoint` 流；战斗点固定映射，不掷骰 |
| 5 | `State.Act.PullNextEncounter` | 本幕遭遇序列与四个访问计数 | — | — |
| 6 | `AppendToMapPointHistory` | `CurrentActIndex`、点类型、房间类型、遭遇 id | 新历史条目；`TotalFloor = History.Count` | — |
| 7 | `CreateRoom` / `CombatRoom` 构造 | 遭遇模型、`RunState` | `CombatState`：`RoundNumber=1`、`CurrentSide=Player`；玩家 Creature 先加入，CombatId 0 | — |
| 8 | `CombatManager.SetUpCombat` | `RunState.Rng.Shuffle`、玩家牌组 | 重置玩家战斗状态；克隆牌组并按 `Shuffle` 稳定洗牌；每个 Creature `SetUpForCombat`（重建行动状态机） | `Shuffle` |
| 9 | `Hook.AfterRoomEntered` | 遗物、房间类型 | 战斗开始前的能力、格挡、星能、遗物状态（Vajra、BronzeScales、DataDisk、Gorget、OddlySmoothStone、DivineRight、EmberTea、RedSkull、SlingOfCourage、PhilosophersStone 等） | 少数效果（如 DelicateFrond）用局外流 |
| 10 | `AfterCreatureAdded`（玩家后敌人） | 怪物、遭遇槽位 | `AfterAddedToRoom` 的起始 Power / HP 调整；敌人的第一次 `RollMove` | `Niche`（HP）、`MonsterAi`（初始行动） |
| 11 | `Hook.BeforeCombatStart` | 遗物、Power | Anchor 格挡、BeltBuckle 敏捷、Snecko 混乱、BoundPhylactery/PhylacteryUnbound 召 Osty、FurCoat 敌人 HP=1 等 | 少数生成效果用 `CombatCardGeneration` 等 |
| 12 | `Hook.BeforeCombatStartLate` | 同上 | PetrifiedToad 药水等 | `CombatPotionGeneration` |
| 13 | `StartTurn` | `CombatState`、遗物、Power | `BeforeTurnStart` 快照 Power 数值；`BeforeSideTurnStart` 的 BagOfMarbles/RedMask/TwistedFunnel/CrackedCore/Plating 等；能量重置；`BeforeHandDraw`；手牌上限与固有牌；抽牌；`AfterPlayerTurnStart`；`AfterSideTurnStart`；球队列；AutoPrePlay | 洗牌生成的卡用 `Shuffle`；抽牌本身不耗随机数 |
| 14 | `EnterRoomInternal` 返回 | 房间栈 | `State.Act.MarkRoomVisited(roomType)`（普通/精英/Boss 对应计数 +1） | — |
| 15 | 可出牌 | — | 导入层读取 `Battle`/`Rules`/`RunState` | — |

关键点：

- `AppendToMapPointHistory` 在遭遇生成之前，所以遭遇随机数的 `TotalFloor` 已经包含本次地图点。
- `Niche` 的 HP 掷骰发生在洗牌之前；两者是不同流，顺序不影响结果，但记录时必须分开。
- `GainBlock` 在 `CombatManager.IsInProgress` 为 false 时是 no-op；CubexConstruct 的
  `AfterAddedToRoom` 格挡不会进入开局根。Plating 的“首回合给敌人格挡”发生在
  `BeforeSideTurnStart`，不是 `AfterAddedToRoom`。
- `AfterAddedToRoom` 的初始行动会先执行 `Niche` HP，再执行 `MonsterAi` 掷骰；`SetUpForCombat`
  只重建状态机，不改 HP。
- `BeforeCombatStart` 之前 `IsInProgress` 已置 true，Anchor、BeltBuckle 等格挡/状态效果生效。
- 第一回合不清理格挡，也不重置 `StarsGainedThisTurn`；DivineRight 在房间进入时获得的 3 星保留到根。

## 2. Blitz 入口

`src/Core/Simulation/Run/RunCombatSetup.cs`：

```csharp
CombatSetupResult result = RunCombatSetup.EnterMapCoord(in runState, table, coord);
// 或测试路径：RunCombatSetup.EnterMapPoint(in runState, table, actFloor, pointType)
```

`CombatSetupResult` 包含：

- `Run`：进点后的 `RunState`（已加已走坐标、楼层、历史、房间栈、访问计数，并更新 9 条战斗流）；
- `Battle`：第一个玩家出牌阶段的状态；
- `Rules`：由 `RulesBuilder` 用建局过程产生的 `RulesRootFacts` 编出；
- `Facts`：初始节点、条件分支目标、节点攻击、根 Power 基线、`InitialMoveLogged`；
- `EncounterId`：本次遭遇的 `RunCatalog` id。

入口只接受 `Monster` / `Elite` / `Boss` 点。未知点、商店、宝箱、休息处、事件与先古之民
显式抛 `ContentUnavailableException(ContentReason.UnsupportedRoom, ...)`，留给 R2b。

## 3. 遭遇生成

`src/Core/Content/Encounters/Encounters.cs` 为每个 `RunCatalog` 遭遇提供一个
`EncounterDef`，按原版 `GenerateMonsters` 的选择顺序写出 Blitz 值类型：

- `EncounterDef.Slots` 是原版 `EncounterModel.Slots`（导入层用它把 Creature 的槽位名映射为
  `Enemy.Slot`；不在列表中的名字映射为 `NoSlot`）。
- `EncounterDef.Generate(ref EncounterRng, Span<EncounterSpawn>, out int)` 只写固定容量的
  `EncounterSpawn`，不分配、不复制原版模型。
- `EncounterSpawn` 带 `MonsterId`、`Slot`、`Variant`、`HpReduction`。`Variant` 表示遭遇级
  私有标志（Chomper 的 ScreamFirst、TwoTailedRat 的 StarterMoveIndex、PunchOff 的 HP 扣减等）。
- 遭遇随机数按 R1b 的 `RunRng.Encounter(seed, TotalFloor, encounterId)` 派生；生成结束后该
  Rng 丢弃，不写回 `RunState`。
- 怪物 HP 用 `RunState` 的 `Niche` 流；初始行动用 `MonsterAi` 流；卡牌生成的随机位置用
  `Shuffle`，升级选择用 `CombatCardSelection`，与内核现有流序号一致。
- 依赖槽位的分支（Exoskeleton、Wriggler、Myte、PhantasmalGardener 等）由
  `MonsterDef.ResolveConditional` 在真实 `Enemy.Slot` 上解析；Nibbit/Toadpole 的前后排由
  `MonsterDef.CombatStartConditional` 按 `Variant` 解析。

## 4. 开战语义

开战和第一回合才有的效果与现有内核共用实现，不另写一份：

- `src/Core/Simulation/Combat/CombatStart.cs`：初始洗牌、`AfterRoomEntered`、
  `BeforeCombatStart(Late)`、第一回合入口。
- `src/Core/Simulation/Combat/Kernel.BattleStart.cs`：第一回合的能量、`BeforeHandDraw`、固有牌
  置顶、抽牌、`AfterPlayerTurnStart`、首个 `AfterSideTurnStart`、Plating 首回合格挡与
  AutoPrePlay。后续回合仍走 `Kernel.AdvanceRound`。
- `Content`：`RelicDef.AfterRoomEntered` / `BeforeCombatStart` / `BeforeCombatStartLate` /
  `BeforeHandDraw` / `RoomEnteredStatus`，`PowerDef.BeforeCombatStart(Late)`，
  `MonsterDef.CombatStartMove`、`CombatStartConditional`；开战数值不再写在 Core 的怪物
  定义里，而是由内容表的 `CombatStartRecord` 按（怪物、槽位、进阶）提供。
- `Kernel.Monsters`：`AddMonsterForCombatStart` 负责 HP/CombatId/roster；`PrepareMonsterForCombatStart`
  负责 `AfterAddedToRoom` 与第一次行动掷骰。

新加的 Hook 都直接由建局入口调用，不占用 `HookMask` / `HookMaskExt` 的位；现有 64 位 Hook 表
不变。`Exec.FirstRoomIsBoss` 是只在这一段有效的瞬时标志（Pantograph），不进入 `Battle` 布局。

`RulesRootFacts` 由建局过程产生：每个敌人的构造初始节点、条件分支实际目标、节点攻击表、
`InitialMoveLogged`。`RulesBuilder` 用它覆盖内容表里的通用行；这样 `Rules` 对照覆盖了
初始节点、条件分支和攻击基值，而不是从内容表默认值猜。

### 4.1 数值来自内容表

建局数值不进入 `Rules`（否则会改变搜索字节），而是走内容表的只读建局上下文：

- **遗物**：导出器把规范 `RelicModel.DynamicVars` 全量放进 `ContentTable.RelicStartVars`。
  建局时遗物按变量名读取，例如 Anchor 的 `Block`、BronzeScales 的 `ThornsPower`、
  DivineRight 的 `Stars`。`RelicDef.VarNames` 没有增加，`Rules` 字节不变。
- **怪物**：导出器对每个遭遇的每个进阶真正执行一次原版 `MonsterModel.AfterAddedToRoom`，
  按（怪物、槽位、进阶）记录：施加的 Power（目标、Applier、层数、`Power.Target`、
  WitheringPresence 的 `CardsLeft`）以及 HP 模式。建局由 `Kernel.PrepareMonsterForCombatStart`
  按记录调用 `ApplyPower`/`ApplyPowerTargeted`，仍经过内核 Hook。
- **暂存的 fallback 常量**（内容表里带原版成员名）：
  - `SneckoEye`/`FakeSneckoEye` 的 `ConfusedPower=1`：原生方法体字面量，不是 DynamicVar；
  - `FurCoat` 的 `SpawnHp=1`：原生 `BeforeCombatStart` 的 `SetCurrentHp(..., 1m)`；
  - `DecimillipedeSegment` 的唯一 HP 步长 `2`：原生 `AfterAddedToRoom` 的算法常量。
  这三项在 [内容表](content-table.md) 与本批报告第 0 节列出，游戏版本变更时需人工核对。
- **WaterfallGiant**：导出器在 `ScratchAscension` 下反射读取原版
  `BasePressureGunDamage` 并写 `_currentPressureGunDamage`；字段/属性缺失直接抛异常。
- **结构常数**：遭遇生成里的 `rng.NextInt(3)`、`NextInt(2, 10)` 等是原版调用参数，仍留在
  遭遇移植代码并注释来源；它们不是可独立导出的内容数值。

## 5. 拒绝码

建局遇到未实现内容时显式失败，复用内容表的稳定原因码：

| 原因码 | 场景 |
| --- | --- |
| `unsupported-room` | 非战斗点、房间栈含非地图房间、地图缺坐标 |
| `unsupported-encounter` | 没有遭遇生成定义、遭遇槽位不一致 |
| `unsupported-monster` | 遭遇生成或怪物规则缺内容 |
| `unsupported-relic` | 遗物的开战/第一回合效果未实现（Toolbox、DelicateFrond、MysticLighter、BigHat 等） |
| `unsupported-card` | 牌组卡牌的升级等级超出内容表 |
| `unsupported-potion` | 药水 id 超出内容表 |
| `unsupported-enchantment` | 附魔 id 超出内容表 |
| `capacity` | 容量溢出 |

对照程序另把 D1 请求中给遗物注入 `IntegerMembers`/`BooleanMembers` 的根标为
`unsupported-relic-state`：这些值不进入 `RunState`，建局无法从导入状态恢复。

## 6. 对照方法

`tests/OfflineHost` 的模式 `--combat-setup-compare` 在同一进程里跑两条路径：

1. 原版：导入进点前 `RunState`，调用 `RunManager.EnterMapCoord`（测试调整后也可等价地准备
   act 序列），等待可出牌；用 `LiveCombatTracker.ImportRoot` 的选项导入 `Battle`，
   `RulesCompiler` 编 `Rules`，`NativeRunDump` 转储进点后 `RunState`。
2. Blitz：导入同一进点前 `RunState`，调用 `RunCombatSetup.EnterMapCoord`；用
   `RulesBuilder` 建 `Rules`，`RunDump` 转储。

比较 `Battle` 逐字节、`RulesCodec` 编码逐字节、`RunState` 逐字段。失败时打印第一处差异的
字段路径和两边值；`--setup-cover` 只统计覆盖，不跑原版路径。

三类用例：

- `natural`：5 角色 × 进阶 0/10 × 100 种子，从 R1b 新局入口进入第一排战斗点；
- `encounter`：89 个遭遇 × 进阶 0/10 × 20 种子，进点前调整本幕遭遇序列使目标遭遇成为下一个；
- `d1`：D1 的 410 个输入 × 4 个种子，用 `ScenarioBuilder.PrepareRunAsync` 建到进房间之前，
  再走真实地图点路径，不做开战后的测试注入。

结果与复跑命令见 [测试与验证](testing.md) 第 6.12 节；本批的数量与拒绝分布见
`R2a2-report.md`。

## 7. 战后写回

写回入口在 `src/Core/Simulation/Run/RunCombatWriteback.cs`：

```csharp
RunState next = RunCombatWriteback.Apply(in runAfterEntry, in terminalBattle, table);
```

终点与 R2c 的奖励生成对齐：原版已经跑完 `EndCombatInternal` 的
`Hook.AfterCombatEnd`、`CombatRoom.OnCombatEnded`、`Player.AfterCombatEnd`、
`Hook.AfterCombatVictory`、历史回合数、`MarkPreFinished`，但还没有调用
`RewardsCmd.GenerateForRoomEnd`。玩家死亡走原版的 `LoseCombat` 终点：不执行战斗结束
Hook，`RunState.IsGameOver` 由玩家死亡推导，`Decision = GameOver`。

写回顺序：

1. 复制 `Battle.Player` 的 HP/最大 HP、`Battle.Gold`、药水槽和 9 条战斗流；
2. 移除仍被 `SwipePower` 扣住的牌组牌（`Battle.DetachedCards` 中带 `DeckCard` 的行）；
3. 应用牌组长期变化：`GeneticAlgorithm`/`TheScythe` 的成长、`Goopy` 附魔层数；
4. 胜利/事件结束的结束 Hook：按原生监听顺序处理牌组牌（`Guilty`）、玩家 Power
   （`ImprovementPower`）、遗物 `AfterCombatEnd` 和两轮 `AfterCombatVictory`；
5. 同步最终遗物状态、显示计数和 `SavedProperties`；
6. 同步历史房间的怪物列表与回合数、当前房间 `IsPreFinished`、`Decision`。

遗物与 Power 数值全部来自内容表（`ContentTable.RelicVar`/`Card`）。目前覆盖的开战/首回合
效果与结束效果包括 Anchor、FakeAnchor、Pantograph、RedSkull、BeltBuckle、SneckoEye、Osty、
BagOfMarbles/RedMask/TwistedFunnel、BloodVial、Lantern、VeryHotCocoa、FurCoat、GhostSeed、
StoneCracker、Bellows、PetrifiedToad、FuneraryMask、FencingManual、Akabeko、FestivePopper、
CrackedCore、RingOfTheSnake，以及 FishingRod、ToyBox、PaelsTooth、PumpkinCandle、
WongosMysteryTicket、ChosenCheese、BurningBlood/BlackBlood、MeatOnTheBone、SwordOfStone、
WarHammer 等结束 Hook。

### 7.1 原版结束流程

| # | 原版步骤 | 对 `RunState`/`Player` 的写入 |
| ---: | --- | --- |
| 1 | `EndCombatInternal` | `TurnNumber` 先保存为 `turnsTaken`；`IsInProgress=false` |
| 2 | `Hook.AfterCombatEnd` | 牌组牌先于 Power、遗物：`Guilty` 计数/移除，`ImprovementPower` 升级，`FishingRod`/`ToyBox`/`PaelsTooth`/`PumpkinCandle`/`WongosMysteryTicket`/`ChosenCheese` 修改牌组、最大 HP、属性 |
| 3 | `room.OnCombatEnded` | 只写 `GoldProportion`（不在 `RunState`） |
| 4 | `RunManager.WriteReplay` | 离线宿主跳过；不影响 `RunState` |
| 5 | `player.AfterCombatEnd` | 清空战斗牌堆和 Power；不写持久字段 |
| 6 | `Hook.AfterCombatVictory` | 先 `AfterCombatVictoryEarly`（MeatOnTheBone 回血），再普通 pass（BurningBlood/BlackBlood 回血、SwordOfStone 替换、WarHammer 升级、BeltBuckle 状态） |
| 7 | 历史与房间 | `Rooms.Last().TurnsTaken`、`room.MarkPreFinished()` |
| 8 | 保存/成就/进度 | 存档与进度文件；不改 `RunState` 的可比较字段 |
| 9 | `CombatEnded` 事件 | `CombatRoom.OfferRoomEndRewards` 在 UI 侧触发；R2c 从生成奖励开始 |

战斗结束只把 `RecoveredHp` 等搜索台账保持为零：开战回血（Pantograph、BloodVial）属于
`RunState.Player.CurrentHp`，不是战斗内恢复。

## 8. 进程内整场执行器

`tests/OfflineHost/Combat/NativeActionExecutor.cs` 不开 Godot，用原版公开入口逐个执行内核
动作：

- `PlayCard`：`CardModel.TryManualPlay`；
- `UsePotion`：`PotionModel.EnqueueManualUse`；
- `EndTurn`：`CombatManager.OnEndedTurnLocally` + `EndPlayerTurnAction`。

动作入队/完成沿用 Mod 的 `ActionExecutor.BeforeActionExecuted` 捕获方式；选牌由
`tests/OfflineHost/Combat/NativeChoiceSelector.cs` 按路线冻结的 `ChoiceRequest` 回答，未支持
或对不上的请求抛 `choice_mismatch`。每个动作完成后用
`LiveCombatTracker.ImportForComparison` 按整场托管的导入选项读回 `Battle`，与内核预测逐字节
比较；终局动作不再导入，交给写回对照。动作等待超时为 `boundary_timeout`，未入队为
`action_not_enqueued`，原版取消/异常分别为 `action_canceled`/`action_failed`。

离线宿主为原版执行补充的 Godot 绕过都在 `tests/OfflineHost/GameBootstrap.cs`：`Time.GetTicksMsec`、
`RunManager.WriteReplay`、`NHoverTipSet.Clear` 等。它们只影响离线进程，不改变 Mod 行为。

## 9. 遗物开战矩阵

`--combat-setup-compare --setup-relic-matrix` 对每个有开战/首回合或首回合结束状态的遗物
做一次定向建局：清空起始遗物后注入目标遗物，按触发条件选择普通/精英/Boss 与满血/受伤，
分别跑原版与 Blitz 两条路径。`SETUP-RELQ` 输出每个遗物的房间、玩家状态、两边状态与触发
计数；`SETUP-RELQ-MISSING` 列出没有触发或没有对齐的项。

矩阵覆盖 50 个遗物，包括 Anchor/FakeAnchor、Pantograph、RedSkull、SlingOfCourage、BeltBuckle、
SneckoEye、Osty 两件套、BagOfMarbles/RedMask/TwistedFunnel、BloodVial、Lantern、VeryHotCocoa、
FurCoat、GhostSeed、StoneCracker、Bellows、PetrifiedToad、FuneraryMask、FencingManual、Akabeko、
FestivePopper、CrackedCore、RingOfTheSnake，以及首回合状态的 HappyFlower/Pendulum/ArtOfWar/
StoneCalendar/RippleBasin/Pocketwatch/Metronome/BurningSticks/ThrowingAxe/UnsettlingLamp/Vambrace/
MeatOnTheBone。
