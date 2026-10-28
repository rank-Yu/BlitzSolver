# 战斗边界：进点与开战

[返回文档索引](README.md)

本文记录战斗边界的建局：从停在地图选点的 `RunState` 进入战斗地图点，生成遭遇，建立第一个
玩家出牌阶段的 `Battle`。R3-0 起写回已删除，战斗结束后由原版推进，下一步重新导入
`RunState`。`RunMapEntry` 把"进点、掷房间类型、建房间、追加历史"抽成共享入口，战斗房间从
这里接入建局，非战斗房间停在"已建好、尚未进入"边界；决策协议与地图选点见
[局外决策协议与地图进点](run-decisions.md)。

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

`src/Core/Simulation/Run/RunMapEntry.cs`：

```csharp
RunMapEntryResult entry = RunMapEntry.Enter(in runState, contentTable, coord);
// entry.Combat 是 CombatSetupResult；entry.CombatChoice 是未给全的开战选择
```

R2a-2 的兼容入口仍在 `src/Core/Simulation/Run/RunCombatSetup.cs`：

```csharp
CombatSetupResult result = RunCombatSetup.EnterMapCoord(in runState, table, coord);
// 或测试路径：RunCombatSetup.EnterMapPoint(in runState, table, actFloor, pointType)
```

`RunMapEntry.Enter` 先完成原版 `EnterMapCoord`/`EnterMapPointInternal` 的前半段：清房间栈、
`ActFloor`、已走坐标、`RollRoomTypeFor`、取遭遇/事件、追加历史。战斗房间随后把房间压栈并
调用 `RunCombatSetup.BuildCombat`；非战斗房间在压栈与 `EnterRoomInternal` 之前返回，状态是
"房间已建好、历史已追加、尚未进入房间"。

`CombatSetupResult` 包含：

- `Run`：进点后的 `RunState`（已加已走坐标、楼层、历史、房间栈、访问计数，并更新 9 条战斗流）；
- `Battle`：第一个玩家出牌阶段的状态；
- `Rules`：由 `RulesBuilder` 用建局过程产生的 `RulesRootFacts` 编出；
- `Facts`：初始节点、条件分支目标、节点攻击、根 Power 基线、`InitialMoveLogged`；
- `EncounterId`：本次遭遇的 `RunCatalog` id。

### 2.1 开战选择

开战的前三段（`AfterRoomEntered`、`BeforeCombatStart(Late)`）和第一回合准备都可能需要玩家选择。
Blitz 用战斗内核已有的 `ChoiceRequest` + 选择序列表示，不另建局外选择类型：

```csharp
CombatSetupAttempt attempt = RunCombatSetup.TryEnterMapCoord(in run, table, coord, choices);
// attempt.Pending 非空：choice 序列还不够，RunState 不变
// attempt.Result  非空：完整建局结果
```

`choices` 是 `ReadOnlySpan<ulong>`，每个元素是一次 `ChoiceRequest` 的位掩码；建局每次都从同一个
进点前 `RunState` 从头重放，不保存中途快照。序列上限是 `RunLimits.CombatSetupChoices`（8），
超出抛 `CapacityException`。当前 21 种遗物在一场战斗开局最多产生 4 个选择点：Toolbox、
GamblingChip、ToastyMittens、ChoicesParadox 各一个；第 2 回合以后的选择仍走 `Kernel.Step` 的
普通动作选择机制。

`ChoiceKind.SelectOption` 现在也支持范围选择（Toolbox 的 0..1、ChoicesParadox 的 1..1），
选项卡值放在 `ChoiceRequest.GeneratedOptions`；手牌选择仍是 `SelectCards` + 牌堆位置位掩码。
GamblingChip 用 `MinCount=0`、`MaxCount=手牌数`，一次位掩码表示任意子集。

所有模拟出来的回合开始都按**自动选择器、不暂停**的顺序建模：`SetupPlayerTurn` 先完整跑完
`AfterPlayerTurnStart`（包括三趟和其中的选择），再跑 `Hook.AfterSideTurnStart`、死亡结算和
回合开始球效果。第一回合和后续回合走同一套顺序。原先冻结的 4 个 D1 根
（`quality-REGENT-001`、`qf-turn-start-choice-forge`、`qf2-gradient-failure`、`ts-a-toasty`）
已用原版全局选择器逐动作 same 的证据重录，见 [Golden 数据](golden-data.md) 8.1。原版
`CardSelectCmd.FromHand`/`FromChooseACardScreen`/`FromSimpleGrid` 只有在
`CardSelectCmd.Selector == null` 时才 `SignalPlayerChoiceBegun`；全局
`PushSelector(selector)`（Mod 部署的 `PlannedNativeSelector.Activate` 用法）会让
`Selector != null`，因此不暂停。`Exec` 不再有 `DeferSideTurnStartForChoice` 等延迟字段。

手动界面或 `localOnly: true` 的 `LocalSelector` 走 `Selector == null` 分支，会暂停
`SetupPlayerTurn` 先跑侧回合开始；持有 ToastyMittens 这类回合开始选择、又有 Crossbow 等
`AfterSideTurnStart` 加牌效果时，界面上看到的选项可能和自动/Mod 路径不同。这是已知差异，
不影响搜索；R3 的 Mod 部署回答开战选择时必须使用不带 `localOnly` 的全局选择器。

ToastyMittens 在手牌只有 1 张时按原生
`FromHand` 的 `!RequireManualConfirmation && list.Count <= MinSelect` 自动选唯一一张，
不产生 `ChoiceRequest`；ChoicesParadox 只有 1 张生成卡时同理。

`RunCombatSetup.EnterMapCoord` 只接受战斗房间；未知点掷出的商店/宝箱/休息处/事件与先古点
由 `RunMapEntry` 停在"已建好、尚未进入"边界，内容由原版执行器处理。未知点掷骰、事件拉取和
非战斗边界的细节见[局外决策协议与地图进点](run-decisions.md)。

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
不变。`Exec.FirstRoomIsBoss` / `FirstRoomIsElite` 是只在这一段有效的瞬时标志（Pantograph、
BoomingConch），不进入 `Battle` 布局。

`RulesRootFacts` 由建局过程产生：每个敌人的构造初始节点、条件分支实际目标、节点攻击表、
`InitialMoveLogged`。`RulesBuilder` 用它覆盖内容表里的通用行；这样 `Rules` 对照覆盖了
初始节点、条件分支和攻击基值，而不是从内容表默认值猜。

### 4.1 数值来自内容表

建局数值不进入 `Rules`（否则会改变搜索字节），而是走内容表的只读建局上下文：

- **遗物**：导出器把规范 `RelicModel.DynamicVars` 全量放进 `ContentTable.RelicStartVars`。
  建局时遗物按变量名读取，例如 Anchor 的 `Block`、BronzeScales 的 `ThornsPower`、
  DivineRight 的 `Stars`。`RelicDef.VarNames` 没有为建局效果增加，`Rules` 字节不变。
- **只在建局期使用的闭包**：Toolbox/ChoicesParadox/VexingPuzzlebox 的生成池、BigHat 的
  Ethereal 池、OrangeDough 的无色池、TeaOfDiscourtesy 的 Dazed、NinjaScroll 的 Shiv、
  DelicateFrond 的局外药水池，用 `ContentDef.SetupOnlyCardPools` /
  `SetupOnlyGeneratedCards` / `SetupOnlyOutOfCombatPotionPool` 声明。它们只冻结进
  `RunCombatSetup` 的 provisional `Rules`，不进入 `CombatSetupResult.Rules`，因此不会改变
  冻结规则表；真正留下的生成卡/药水仍按 `Battle.Cards`/`Battle.Potions` 进入最终 `Rules`。
  BigHat 的 `GeneratedCardPools` 保留旧的 `CharacterAll` 闭包（与冻结规则一致），
  `SetupOnlyCardPools` 才提供原版的 `CharacterEthereal` 采样池。
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
| `unsupported-room` | 非战斗点、地图缺坐标 |
| `room-stack-active` | 房间栈里有未完成的非地图房间 |
| `unknown-point-hook` / `event-hook` / `event-allowed-check` | 未知点掷骰或事件拉取遇到未实现的 Hook（见[局外决策协议](run-decisions.md)） |
| `unsupported-encounter` | 没有遭遇生成定义、遭遇槽位不一致 |
| `unsupported-monster` | 遭遇生成或怪物规则缺内容 |
| `unsupported-relic` | 内容表缺遗物定义或遗物变量；21 种开战/第一回合遗物已实现，不再在建局开头整批拒绝 |
| `unsupported-card` | 牌组卡牌的升级等级超出内容表 |
| `unsupported-potion` | 药水 id 超出内容表 |
| `unsupported-enchantment` | 附魔 id 超出内容表 |
| `capacity` | 容量溢出 |

对照程序另把 D1 请求中给遗物注入 `IntegerMembers`/`BooleanMembers` 的根标为
`unsupported-relic-state`：这些值不进入 `RunState`，建局无法从导入状态恢复。D1 的
`--combat-setup-compare` 不做开战后的测试注入；带 `ClearPlayerHand`/`ClearPlayerPiles` 等
请求的根只比较“进点前 RunState 能唯一决定的自然开局”，注入本身不参与建局。

## 6. 对照方法

`tests/Differential` 的模式 `--combat-setup-compare` 在同一进程里跑两条路径：

1. 原版：导入进点前 `RunState`，调用 `RunManager.EnterMapCoord`（测试调整后也可等价地准备
   act 序列），等待可出牌；用 `LiveCombatTracker.ImportRoot` 的选项导入 `Battle`，
   `RulesCompiler` 编 `Rules`，`NativeRunDump` 转储进点后 `RunState`。
2. Blitz：导入同一进点前 `RunState`，调用 `RunCombatSetup.EnterMapCoord`；用
   `RulesBuilder` 建 `Rules`，`RunDump` 转储。

比较 `Battle` 逐字节、`RulesCodec` 编码逐字节、`RunState` 逐字段。失败时打印第一处差异的
字段路径和两边值；`--setup-cover` 只统计覆盖，不跑原版路径。

带开战选择时，对照按完整选择序列枚举：每遇到待定 `ChoiceRequest`，就按 `GenerateToHand`
（Toolbox 0..1、ChoicesParadox 1..1）、`Exhaust`（ToastyMittens 1..1）或
`DiscardAndDraw`（GamblingChip 0..手牌数）枚举可行位掩码；GamblingChip 在手牌超过 7 张时取
空集、每个单张、全选和 16 个确定性随机子集，其余选择全枚举。每个序列都从同一进点前
`RunState` 重放 Blitz，并让原版用同一序列重新进点；比较请求（种类、生成卡 id/升级或手牌
下标、min/max）、`Battle`、`Rules` 和进点后 `RunState`。`--setup-choices --setup-root <root>`
只打印 Blitz 的请求树，不跑原版。

三类用例：

- `natural`：5 角色 × 进阶 0/10 × 100 种子，从 R1b 新局入口进入第一排战斗点；
- `encounter`：89 个遭遇 × 进阶 0/10 × 20 种子，进点前调整本幕遭遇序列使目标遭遇成为下一个；
- `d1`：D1 的 410 个输入 × 4 个种子，用 `ScenarioBuilder.PrepareRunAsync` 建到进房间之前，
  再走真实地图点路径，不做开战后的测试注入。

结果与复跑命令见 [测试与验证](testing.md) 第 6.12 节；本批的数量与拒绝分布见
`R2a2-report.md`。

## 7. 写回已删除

R3-0 删除了 `RunCombatWriteback`：战斗结束后不再把终局 `Battle` 写回 `RunState`，也不由
Blitz 生成战后奖励。原版在 `EndCombatInternal` 之后继续推进，下一步从原版重新导入
`RunState`。开战时的 MawBank 金币、TeaOfDiscourtesy/BoneTea 的 `CombatsLeft` 等仍由
`RunCombatSetup` 处理，因为它们在第一个出牌阶段的 `Battle` 里可见；战斗结束后的遗物/牌组
长期变化由原版负责。

原 R2a-3 写回覆盖的结束 Hook、结束流程与差分数字只保留在 `683bc03` 及 R2a-3/R2a-5 报告中，
不再作为当前实现或回归基准。

## 8. 进程内整场执行器

`tests/Differential/Combat/NativeActionExecutor.cs` 不开 Godot，用原版公开入口逐个执行内核
动作：

- `PlayCard`：`CardModel.TryManualPlay`；
- `UsePotion`：`PotionModel.EnqueueManualUse`；
- `EndTurn`：`CombatManager.OnEndedTurnLocally` + `EndPlayerTurnAction`。

动作入队/完成沿用 Mod 的 `ActionExecutor.BeforeActionExecuted` 捕获方式；选牌由
`tests/Differential/Combat/NativeChoiceSelector.cs` 按路线冻结的 `ChoiceRequest` 回答，未支持
或对不上的请求抛 `choice_mismatch`。每个动作完成后用
`LiveCombatTracker.ImportForComparison` 按整场托管的导入选项读回 `Battle`，与内核预测逐字节
比较。终局动作在战斗结束前没有下一个可出牌边界，但 `TerminalBattleCapture` 在
`CombatManager.EndCombatInternal`（胜利）与 `ProcessPendingLoss`（失败）的 prefix 捕获原版
`Battle`，按显式合同比较 `Outcome`（由捕获路径判定，不由预测反推）、玩家 HP/MaxHP、金币、
药水、遗物状态与 9 条战斗 RNG；手牌/牌堆/回合内计数等清理字段不比较，不能宣称终局
`Battle` 逐字节全同。捕获缺失报告 `executor-diff`（`terminal_missing`），不计 same；
`EventDefeat` 以 `terminal_diff` 报告未覆盖，退出码为 1。写回不再参与。动作等待超时为
`boundary_timeout`，未入队为
`action_not_enqueued`，原版取消/异常分别为 `action_canceled`/`action_failed`。

离线宿主为原版执行补充的 Godot 绕过都在 `tests/Differential/GameBootstrap.cs`：`Time.GetTicksMsec`、
`RunManager.WriteReplay`、`NHoverTipSet.Clear` 等。它们只影响离线进程，不改变 Mod 行为。

## 9. 遗物开战矩阵

`--combat-setup-compare --setup-relic-matrix` 对每个有开战/首回合或首回合结束状态的遗物
做定向建局：清空起始遗物后注入目标遗物，按触发条件选择普通/精英/Boss 与满血/受伤，
分别跑原版与 Blitz 两条路径。`--setup-matrix-seeds`（默认 20）控制有触发用例的种子数；自动发现的无效果/默认项用 1 个
smoke 种子；`--setup-matrix-list` 打印 Implemented 名单与计数。
`SETUP-RELQ` 按遗物汇总 `same/seeds` 与 `triggered`，`SETUP-RELQ-MISSING` 列出预期触发但
没有触发的遗物。矩阵使用 run-pilot 序列（GamblingChip 例外地取第一张单卡，保证触发可见）。

矩阵从 `ContentSetupHookMask` 的 Implemented 名单自动生成，当前 126 个遗物，全部各 20 个种子（无效果的也跑），共
2520 个 run。`SETUP-RELQ` 按遗物汇总 `same/seeds` 与 `triggered`，`SETUP-RELQ-MISSING`
列出预期触发但没有触发的遗物。矩阵使用 run-pilot 序列（GamblingChip 例外地取第一张单卡，
保证触发可见）。BiiigHug/ToyBox/GnarledHammer 的 `AfterObtained` 需要原版选牌/奖励界面，
矩阵不注入；BiiigHug/ToyBox 在 D1 覆盖，GnarledHammer 的获得路径仍显式拒绝。
