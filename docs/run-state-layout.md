# 整局状态布局

[返回文档索引](README.md)

本文记录 R1b 的整局状态表示：原版 `RunState`/`Player` 的字段盘点与取舍、`RunState` 的布局与容量、局外随机数的派生、字节编码、玩家可见投影 `RunView`，以及从原版导入时的拒绝码。状态与决策协议见[整局设计](full-run-design.md)第 3、5、11 节；战斗状态见[战斗状态布局](combat-state-layout.md)。

2026-10-27 起局外由原版推进，本节只描述导入后的状态表示。`RunKernel.Step` 已删除；地图、
房间、奖励与商店的当前界面状态仍随 `RunState` 导入，用于状态比较与 R3b 的请求投影。

## 1. 范围与事实来源

唯一事实来源是 `~/ss/CombatSolver/.local/decompiled/sts2-v0.111.0/` 的原版代码：

- `MegaCrit.Sts2.Core.Runs.RunState`、`RunManager`；
- `MegaCrit.Sts2.Core.Entities.Players.Player`；
- `MegaCrit.Sts2.Core.Models.ActModel`、`MegaCrit.Sts2.Core.Map.*`；
- `RunRngSet`/`PlayerRngSet`、`RunOddsSet`/`PlayerOddsSet`、`RelicGrabBag`、`UnlockState`；
- `SavedProperties` 与 `Serializable*` 存档模型。

R1b 只覆盖单人、标准模式、原版内容。多人状态、第三方内容、非标准模式（Daily/Custom）与 Modifier 显式拒绝；见第 7 节。

## 2. 字段盘点

分类：

1. **进入 `RunState`**：会影响以后的随机结果或游戏结果；
2. **可推导**：由其他字段、内容表或原版公式导出；列出推导方式；
3. **只影响界面/统计/成就**：不进入；
4. **多人专用或不在单人范围**：不进入；非默认时导入拒绝。

### 2.1 `RunState`

| 原版字段 | 分类 | `RunState` 字段 / 排除理由 |
| --- | --- | --- |
| `_players`（单人一个） | 1 | `Player`、`Deck`、`Relics`、`SharedBag`/`PlayerBag`；多人拒绝 |
| `CurrentActIndex` | 1 | `ActIndex` |
| `Acts` | 1 | `Acts`：每幕 `Id`、预生成事件/普通/精英序列、Boss/第二 Boss/先古、访问计数 |
| `Act` | 2 | `Acts[ActIndex]` |
| `Map`（当前幕） | 1 | `Map`：网格尺寸、节点、连线、起始/Boss/第二 Boss、起始点集合、`CanBeModified` |
| `_visitedMapCoords` | 1 | `Visited.Coords` |
| `CurrentMapCoord`/`CurrentMapPoint` | 2 | `Visited.Coords` 末项 + `Map` |
| `ActFloor` | 1 | `ActFloor`（界面层数与部分事件条件） |
| `TotalFloor` | 2 | `History.Count`（原版为 `MapPointHistory.Sum(c => c.Count)`） |
| `_mapPointHistory` | 1 | `History`：幕、地图点类型、每个房间的类型/模型/怪物/回合数；玩家统计子表排除 |
| `CurrentMapPointHistoryEntry` | 2 | `History` 最后一项 |
| `_currentRooms` | 1 | `Rooms`：房间类型、模型、父事件、`IsPreFinished` |
| `_visitedEventIds` | 1 | `Visited.Events` 位图（影响事件去重） |
| `_allCards` | 2 | 由牌组、遗物/卡牌私有属性与当前房间状态重建；浮动卡属 R2 |
| `IsGameOver` | 2 | `Player.CurrentHp`/`Decision` |
| `GameMode` | 1 | `GameMode`；仅 `Standard` 可导入 |
| `AscensionLevel` | 1 | `Ascension` |
| `Rng`（12 条） | 1 | `Rng.Streams` 中 12 条 + 玩家 3 条 |
| `Odds` | 1 | `Odds` 四个浮点 |
| `SharedRelicGrabBag` | 1 | `SharedBag`（各稀有度剩余顺序 + `RefreshAllowed`） |
| `UnlockState` | 1 | `Unlock`：已解锁 epoch 位图、已见遭遇位图、`NumberOfRuns` |
| `Modifiers` | 4 | 非空拒绝；Daily/Custom 不在范围 |
| `BadgeModels` | 3 | 成就/界面 |
| `ExtraFields` | 1/3 | `StartedWithNeow`、`TestSubjectKills` 进入；`FreedRepy` 只影响背景 VFX，不进入 |
| `MultiplayerScalingModel` | 4 | 多人专用 |
| `NextRoomId` | 3 | 当前地图点内的界面/存档房间编号，可推导；不进入 |
| `RunLocation`/`MapLocation` | 2 | 由 `ActIndex`、`Visited`、`Rooms` 推导 |
| `PreFinishedRoom` | 1 | 已并入 `Rooms.IsPreFinished` |
| `MapDrawings`、`DailyTime`、`RunTime`、`WinTime`、`SaveTime`、`NumReloads`、`PlatformType` | 3 | 存档元数据/界面 |
| `PlayerMapPointHistoryEntry`（金币、卡牌增减、选择记录等） | 3 | 统计/界面；`FakeMerchant` 的当前事件历史属 R2 事件状态 |

### 2.2 `Player`

| 原版字段 | 分类 | `RunState` 字段 / 排除理由 |
| --- | --- | --- |
| `Character` | 1 | `Player.Character`（`RunCatalog` 角色 id） |
| `Creature.CurrentHp/MaxHp` | 1 | `Player.CurrentHp/MaxHp` |
| `NetId` | 4 | 单人固定；多人拒绝 |
| `PlayerRng`（3 条） | 1 | `Rng.Streams[Rewards/Shops/Transformations]` + `Rng.PlayerSeed` |
| `PlayerOdds` | 1 | `PlayerOdds.CardRarity/PotionReward` |
| `RelicGrabBag` | 1 | `PlayerBag` |
| `UnlockState` | 1 | 单人等于 `RunState.Unlock`；多人拒绝 |
| `IsActiveForHooks` | 2 | `Creature.IsAlive` |
| `PlayerCombatState` | 3 | 战斗状态，由 `Battle` 导入/导出 |
| `ExtraFields.CardShopRemovalsUsed` | 1 | `ExtraPlayer.CardShopRemovalsUsed`（删牌涨价） |
| `ExtraFields.WongoPoints` | 1 | `ExtraPlayer.WongoPoints`（事件货币） |
| `ExtraFields.CccomboBadgeUnlocked`、`DamageDealt`、`DebuffsApplied` | 3 | 成就/统计 |
| `Relics` | 1 | `Relics`：顺序、id、状态、Wax/Melted、StackCount、DisplayAmount、FloorAdded、私有 `SavedProperties` |
| `PotionSlots` | 1 | `Player.Potions`（槽数 + 每槽 id/空） |
| `Gold` | 1 | `Player.Gold` |
| `MaxAscensionWhenRunStarted` | 3 | 界面提示 |
| `CanUseOrRemovePotions` | 3 | 瞬时界面锁 |
| `Deck` | 1 | `Deck`：顺序、id、升级、附魔（id/层数/属性）、FloorAdded、私有 `SavedProperties` |
| `MaxEnergy` | 1 | `Player.MaxEnergy` |
| `BaseOrbSlotCount` | 1 | `Player.BaseOrbSlots` |
| `DiscoveredCards/Relics/Potions/Enemies/Epochs` | 3 | 图鉴/进度统计 |
| `Piles`、`Osty`、`HasEventPet` | 2/3 | 由牌组/战斗状态/遗物推导 |
| `MaxPotionCount` | 2 | `Player.Potions.Count` |

### 2.3 `ActModel`、地图与房间

| 原版字段 | 分类 | `RunState` 字段 / 排除理由 |
| --- | --- | --- |
| `ActModel.Id` | 1 | `RunAct.Id` |
| `_rooms.events` / `eventsVisited` | 1 | `RunAct.Events`/`EventsVisited` |
| `_rooms.normalEncounters` / `normalEncountersVisited` | 1 | `RunAct.Normal`/`NormalVisited` |
| `_rooms.eliteEncounters` / `eliteEncountersVisited` | 1 | `RunAct.Elite`/`EliteVisited` |
| `_rooms.Boss` / `SecondBoss` / `BossEncountersVisited` | 1 | `BossId`/`SecondBossId`/`BossVisited` |
| `_rooms.Ancient` | 1 | `HasAncient`/`AncientId`；无先古是合法状态（新存档没有 Neow 解锁时） |
| `_sharedAncientSubset` | 2 | 只在 `GenerateRooms` 时使用；已选先古在 `Ancient` 中 |
| `AllEncounters` 等缓存、颜色/贴图/音乐/标题 | 3 | 内容元数据/界面 |
| `ActMap.Grid` | 1 | `Map.Nodes` |
| `ActMap.startMapPoints` | 1 | `Map.Starts` |
| `BossMapPoint`/`StartingMapPoint`/`SecondBossMapPoint` | 1 | `Map.Boss`/`Starting`/`SecondBoss`（也在 `Nodes` 中） |
| `MapPoint.coord/PointType/CanBeModified/Children` | 1 | `RunMapPoint` 对应字段 |
| `MapPoint.parents` | 2 | 由 `Children` 反向推导 |
| `MapPoint.Quests` | 2 | 由 `FurCoat` 遗物与 `SpoilsMap` 卡牌的私有属性重建；R2 |
| `ShouldReplaceTreasureWithElites`、`_pointTypeCounts` | 2/3 | 地图生成参数；生成后的地图已保存 |
| `AbstractRoom.RoomType/ModelId/ParentEventId/IsPreFinished` | 1 | `Rooms` |
| `AbstractRoom.Id` | 3 | 地图点内房间编号，可推导；不进入 |
| 房间内的奖励列表/商店货架/事件页面/选牌页面/宝箱内容 | 未完成 | R2；本批只表示当前房间身份 |

### 2.4 随机数、概率、遗物袋与解锁

| 原版字段 | 分类 | `RunState` 字段 / 排除理由 |
| --- | --- | --- |
| `RunRngSet.StringSeed` | 3 | 界面显示；实际种子 `Seed` 进入 |
| `RunRngSet.Seed` | 1 | `Rng.Seed` |
| `RunRngSet._rngs`（12 条） | 1 | `Rng.Streams` 对应 12 条 |
| `PlayerRngSet.Seed` | 1 | `Rng.PlayerSeed` |
| `PlayerRngSet._rngs`（3 条） | 1 | `Rng.Streams[Rewards/Shops/Transformations]` |
| `RunOddsSet.UnknownMapPoint` 4 值 | 1 | `Odds` |
| `PlayerOddsSet.CardRarity/PotionReward.CurrentValue` | 1 | `PlayerOdds` |
| `RelicGrabBag._deques` | 1 | `RunRelicBag.Segments`（8 个稀有度，按原顺序） |
| `RelicGrabBag._mpFallbackDequeue` | 4 | 非空拒绝；多人专用 |
| `RelicGrabBag._refreshAllowed` | 1 | `RunRelicBag.RefreshAllowed`（共享袋 true、玩家袋 false） |
| `RelicGrabBag._originalRelics` | 2 | 由解锁状态 + 遗物池重建；R2 |
| `UnlockState._unlockedEpochIds` | 1 | `Unlock.Epochs` 位图 |
| `UnlockState._encountersSeen` | 1 | `Unlock.EncountersSeen` 位图 |
| `UnlockState.NumberOfRuns` | 1 | `Unlock.NumberOfRuns`（未知地图点保底） |
| `UnlockState` 的派生池 | 2 | 由位图 + 内容定义重建 |
| 每个事件的 RNG（`EventModel.Rng`） | 1 | `Rng.Events`：当前事件链的 `EventId + RngState` |
| 每幕地图 RNG（`act_N_map`） | 1 | `Rng.Map`：派生初始状态 + `MapConsumed` |
| 遭遇 RNG（`seed + TotalFloor + hash(encounter)`） | 2 | 由 `Rng.Seed` + `History.Count` + 遭遇 id 推导 |

### 2.5 存档对象与私有属性

| 原版类型 | 分类 | 处理 |
| --- | --- | --- |
| `SerializableCard`（Id/Upgrade/Enchantment/Props/Floor） | 1 | `RunCard` |
| `SerializableRelic`（Id/Props/Floor） | 1 | `RunRelic` |
| `SerializablePotion`（Id/SlotIndex） | 1 | `RunPotion` |
| `SavedProperties` 的 int/bool/string/int[]/ModelId/Card/Card[] | 1 | `RunPropertyBag` 通用编码 |
| `SerializablePlayer` 的发现字段、`SerializableRun` 的时间/绘制/平台字段 | 3 | 不进入 |
| `SerializableRoom` 的 `ExtraRewards`/`GoldProportion` | 1 | `RunState.Rewards`（`Generated=0`）与 `RunRoom.GoldProportion`；`EncounterState` 仍是原版私有状态 |
| `SerializableModifier` | 4 | 非空拒绝 |

## 3. `RunState` 布局

`RunState` 是 `[StructLayout(LayoutKind.Sequential)]` 的无引用结构体，`Unsafe.SizeOf<RunState>() == 49912`。子结构：

| 结构 | 内容 |
| --- | --- |
| `RunPlayer` | 角色 id、HP/最大 HP、MaxEnergy、金币、基础球槽、药水槽 |
| `RunDeck` | 牌组顺序 + `RunCard` 数组 |
| `RunCard` | 卡牌 id、升级、附魔 id/层数、`RunCardFlags`（可删/诅咒）、FloorAdded、卡牌/附魔属性包（各 32 字节） |
| `RunRelics` / `RunRelic` | 遗物顺序、id、状态、Wax/Melted、StackCount、DisplayAmount、FloorAdded、属性包（64 字节） |
| `RunRelicBag` | 8 个稀有度的剩余顺序（每段 160）+ `RefreshAllowed` |
| `RunActs` / `RunAct` | 每幕预生成序列与访问计数 |
| `RunMap` / `RunMapPoint` | 网格、节点、连线、起始/Boss/第二 Boss、起始点集合 |
| `RunHistory` | 地图点历史（类型 + 房间 + 怪物 + 回合数） |
| `RunRooms` | 当前房间栈（类型、模型、父事件、`IsPreFinished`、`GoldProportion`） |
| `RunRewards` | 战后奖励列表（类型、模型、金额、选项位置）与当前奖励阶段；卡牌选项在同一结构的 `Choices` |
| `RunShop` | 商店阶段、删牌服务状态，以及角色卡 5、无色卡 2、遗物 3、药水 3 的货架；每格保存基础价 `Cost`、Hook 后价格 `Price`、库存/打折标记 |
| `RunVisited` | 已走坐标、已访问事件位图 |
| `RunRngState` | 15 条流、4 条地图流、当前事件流、种子 |
| `RunUnlockState` | 2+2 个 64 位字 + 跑局数 |
| `RunOdds`/`RunPlayerOdds` | 概率当前值 |
| `RunExtraFields`/`RunExtraPlayerFields` | `StartedWithNeow`、`TestSubjectKills`、删牌次数、Wongo 点数 |

`RunPropertyBag` 的布局是 `byte Length + byte[64]`；`RunCard` 148 字节、`RunRelic` 84 字节。`RunView` 是独立投影，`Unsafe.SizeOf<RunView>() == 20912`；R3b-1 的请求为 15960 字节、选项 44 字节、事实 16 字节、回答 536 字节。请求共享 129 个选项、256 个事实、256 张 bundle 牌与 4096 字节严格 UTF-8 机器键；这些是工程预算，超限显式拒绝。请求不进入 RunState/RunCodec，后者布局与版本不变。

### 3.1 容量

所有容量是编译期常量，溢出抛 `CapacityException`，不截断。依据是原版上限或 410 个 D1 根 + 200 个自然开局的实测最大值，再留余量。

| 容量 | 值 | 依据 |
| --- | ---: | --- |
| `Acts` | 4 | 原版标准三幕；变体幕留一 |
| `ActEvents` | 64 | 实测最大单幕 31；事件全集 66 |
| `ActNormalEncounters` | 24 | 原版单幕最多 15 个房间 |
| `ActEliteEncounters` | 16 | 原版固定生成 15 个精英 |
| `DeckCards` | 128 | 大于实测牌组；`Battle` 上限 96 是战斗边界 |
| `Relics` | 64 | 大于实测遗物栏；`Battle` 上限 32 是战斗边界 |
| `PotionSlots` | 8 | 实测最多 3–5；事件可增加 |
| `RelicBagPerRarity` | 160 | 0.111.0 单稀有度最大约 100 |
| `RelicRarities` | 8 | 原版 `RelicRarity` 全部值 |
| `MapPoints` | 160 | 7 × 最大 24 行 + 3 个特殊点 |
| `MapChildren` | 7 | 7 条路径可汇入同一点 |
| `MapStarts` | 7 | 原版固定 7 条起始路径 |
| `VisitedCoords` | 64 | 单幕最多 7 × 24 |
| `MapHistory` | 96 | 三幕 × 16 层 + 余量 |
| `HistoryRooms` | 4 | 事件→战斗等链 |
| `HistoryMonsters` | 8 | 单遭遇怪物数 |
| `RoomStack` | 4 | 原版通常 1，事件嵌套战斗最多 2 |
| `EventRngs` | 4 | 当前事件链 |
| `RngStreams` | 15 | 6 条局外 + 9 条战斗 |
| `ActMapRngs` | 4 | 每幕一条 |
| `UnlockEpochWords` | 2 | 0.111.0 有 57 个 epoch |
| `UnlockEncounterWords` | 2 | 0.111.0 有 89 个遭遇 |
| `VisitedEventWords` | 2 | 0.111.0 有 66 个事件 |
| `PropertyCount` | 16 | 单模型 `SavedProperty` 远少于 16 |
| `PropertyStringBytes` | 32 | 原版字符串属性（皮肤名） |
| `PropertyInts` | 12 | `FurCoat` 坐标数组 |
| `PropertyCards` | 4 | 卡牌数组属性（`PaelsTooth`） |
| `DecisionOptions` | 129 | 单个决策请求的选项上限；牌组选卡最多 128 张可删卡加取消 |
| `ShopCharacterCards` | 5 | 原版 `_coloredCardTypes.Length` |
| `ShopColorlessCards` | 2 | 原版 `_colorlessCardRarities.Length` |
| `ShopRelics` | 3 | 原版 `PopulateRelicEntries` 固定 3 格 |
| `ShopPotions` | 3 | 原版 `CreateRandomPotionsOutOfCombat(..., 3, ...)` |
| `Rewards` | 16 | 单个战后奖励集合的项数上限；原版无硬上限，本批超限显式 `CapacityException` |
| `RewardCardOptions` | 16 | 所有待处理卡牌/特殊卡奖励的选项总数；单个卡牌奖励通常 3，LastingCandy 4 |

## 4. 局外随机数

原版 15 条按用途命名的流：`UpFront`、`UnknownMapPoint`、`TreasureRoomRelics`、`Rewards`、`Shops`、`Transformations`、`Shuffle`、`CombatCardGeneration`、`CombatPotionGeneration`、`CombatCardSelection`、`CombatEnergyCosts`、`CombatTargets`、`CombatOrbs`、`MonsterAi`、`Niche`。`RunState.Rng.Streams` 的顺序与 `RunRngStream` 一致：

| 下标 | 流 | 原版归属 | 种子 |
| ---: | --- | --- | --- |
| 0 | `up_front` | `RunRngSet` | `Rng.Seed` |
| 1 | `unknown_map_point` | `RunRngSet` | `Rng.Seed` |
| 2 | `treasure_room_relics` | `RunRngSet` | `Rng.Seed` |
| 3 | `rewards` | `PlayerRngSet` | `Rng.PlayerSeed` |
| 4 | `shops` | `PlayerRngSet` | `Rng.PlayerSeed` |
| 5 | `transformations` | `PlayerRngSet` | `Rng.PlayerSeed` |
| 6 | `shuffle` | `RunRngSet` | `Rng.Seed` |
| 7 | `combat_card_generation` | `RunRngSet` | `Rng.Seed` |
| 8 | `combat_potion_generation` | `RunRngSet` | `Rng.Seed` |
| 9 | `combat_card_selection` | `RunRngSet` | `Rng.Seed` |
| 10 | `combat_energy_costs` | `RunRngSet` | `Rng.Seed` |
| 11 | `combat_targets` | `RunRngSet` | `Rng.Seed` |
| 12 | `combat_orbs` | `RunRngSet` | `Rng.Seed` |
| 13 | `monster_ai` | `RunRngSet` | `Rng.Seed` |
| 14 | `niche` | `RunRngSet` | `Rng.Seed` |

派生规则与原版逐字节一致：

- 命名流：`MegaRandom(seed + XxHash64(UTF8(name)))`；名字是原版 `RunRngType`/`PlayerRngType` 的 `SnakeCase`。
- `MegaRandom` 初始化用 SplitMix64 连续四次；每次 `Next*` 恰好递增一次 `Counter`。
- 玩家种子：`Player.InitializeSeed(seed)` 为 `Hash(seed string) + slot`；单人 slot 0。`RunState.Rng.PlayerSeed` 直接保存这个值，不在 Sts2Solver 侧重算字符串种子。
- 事件 RNG：`seed + (shared ? 0 : slot) + Hash(event id)`；单人 slot 0。`Rng.Events` 保存当前事件链的已消费状态。
- 遭遇 RNG：`seed + TotalFloor + Hash(encounter id)`；`TotalFloor` 由 `History.Count` 得到。
- 每幕地图 RNG：`seed + Hash("act_N_map")`；`Rng.Map` 保存初始状态，`MapConsumed` 标记该幕地图是否已在原版生成。当前幕地图已导入，R2 不应再用该初始状态重生成；未来幕从 `MapConsumed=0` 开始。
- `RunRng.NewRun(runSeed, playerSeed)` 生成 15 条初始状态，是本批离线建局与导入的唯一构造入口。

`tests/KernelChecks/Run/RunChecks.cs` 用从原版导出的固定种子前 8 次抽取锁死所有 15 条命名流、事件、遭遇与地图派生；`--run-import-compare` 对每个自然开局/战斗根再从原版与 Sts2Solver 各抽 64 次（`NextInt(1000)`/`NextFloat`/`NextDouble`/`NextInt(-5,100)`/`NextBool` 轮换）。

## 5. 编码

`RunCodec` 是整块字节格式，风格与 `BattleCodec` 一致：

```text
magic   uint32  0x534E5552 ("RUNS")
version uint16  3
size    int32   Unsafe.SizeOf<RunState>() = 49912
payload byte[]  RunState 的规范字节镜像
```

- `Encode` 先复制状态、`Canonicalize()`、`Validate()`；`Canonicalize` 清零所有超出计数的槽位，排序地图节点/子节点/起始点/事件 RNG，并清掉解锁/已访问位图中超出内容表的位。
- 完整结构体写入输出后，`RunCodecPadding` 清零所有隐式 CLR 对齐字节；只改输出，不依赖结构体复制或构造器是否保留 padding。元数据仅在首次编码时递归计算并合并为连续清零区间，后续编码不反射、不扫描字段；战斗搜索不使用这一步。
- 元数据只接受固定宽度 blittable 数值叶类型、枚举、顺序布局结构与步长经过 `Unsafe.SizeOf` 校验的 `InlineArray`；拒绝 bool、char、平台宽度整数、指针、引用、自动/显式布局、MarshalAs/FixedBuffer 字段和重叠字段。`Marshal.OffsetOf` 仅用于这些与 managed 布局一致的类型。每个声明字段都覆盖其完整尺寸，包括名称为 `_padding` 的显式保留字段，绝不按名称删字段。
- v3 / 49,912 字节布局共 2,452 个隐式 padding 字节，其中 `RunRef` 的 392 个实例各有 `+1` 对齐字节。其他局部间隙见下表；内联数组按实际元素步长展开。KernelChecks 独立覆盖标量字段并断言此盘点，新增字段占用旧间隙时必须更新合同并复审。

| 结构 | 局部隐式 padding 偏移 |
| --- | --- |
| `RunState` | 18–19、44–47、37558–37559 |
| `RunUnlockState` / `RunPlayer` | 36–39 / 2–3、58–59 |
| `RunCard` / `RunRelic` | 5、10–11、146–147 / 6–7、81–83 |
| `RunShopCard` / `RunShopModel` | 157–159 / 13–15 |
| `RunDeck` / `RunRelics` | 2–3 / 1–3 |
| `RunAct` / `RunActs` | 3、9、18–19 / 2–3 |
| `RunRef` / `RunHistoryRoom` | 1 / 1、7 |
| `RunHistoryEntry` / `RunHistory` | 3 / 2–3 |
| `RunRoom` / `RunRooms` | 1、14–15 / 1–3 |
| `RunVisited` / `RunEventRng` / `RunRngState` | 129–135 / 4–7 / 781–783 |

- 同一逻辑字段及显式保留字段只有一种规范字节表示：顺序无关集合被排序，未使用槽位和隐式 padding 为零。显式保留字段维持现有 v3 字段合同，不在本次修复中改变。
- `Decode` 校验魔数、版本、长度、结构计数、内容 id、属性包结构与 `ExpectedSize`；任何不一致抛 `InvalidDataException`。
- 旧的有效 v3 输入即使隐式 padding 非零也可解码；重新 `Encode` 得到规范字节。字段/枚举/容量验证保持严格，格式版本、目录 id 与布局不变。
- 内容目录顺序变化（新增原版内容）会改变 id，必须同时递增 `RunCodec.FormatVersion`；`RunState.ExpectedSize` 变化也必须递增版本。R2c-1 因 `RunRewards`/`RunRoom.GoldProportion` 升到 2；R2c-2 因 `RunShop`/`RunCard.Flags`/`DecisionOptions` 升到 3。
- 不写时间戳、非确定性字典遍历或运行时相关值。

## 6. 可见投影 `RunView`

`RunView` 是 `RunState` 的只读投影，`Simulation.Run` 中唯一登记给 `Ai.Run` 的类型组（`tests/Architecture` 的 `VisibleRunTypes`）。可见性准则是：界面上看不到、**并且**不能从玩家见过的历史推出来的信息不进 `RunView`。因此它不含随机数、未来序列、遗物袋顺序、解锁位图、属性包或玩家统计；未知点房间保底、卡牌稀有度保底与药水掉落的当前值可以由玩家见过的历史推导，按已定决策进入投影。

| `RunView` 字段 | 原版界面是否可见 | 来源 |
| --- | --- | --- |
| 角色、进阶、幕、幕内层数、总层数 | 是 | `Player.Character`、`Ascension`、`ActIndex`、`ActFloor`、`History.Count` |
| 当前幕 id、本幕 Boss、第二 Boss、先古之民 | 是（地图界面） | `Acts[ActIndex]` 的 `Id/BossId/SecondBossId/AncientId` |
| HP/最大 HP、金币、MaxEnergy、基础球槽 | 是 | `Player` |
| 药水槽与药水 id/空 | 是 | `Player.Potions` |
| 牌组：卡牌 id、升级、附魔 id/层数 | 是（卡面） | `RunCard` 的可见子集 |
| 遗物：id、状态、Wax/Melted、显示计数 | 是 | `RunRelic` 的可见子集 |
| 地图：节点、连线、类型、已走、起始/Boss/第二 Boss | 是（未知点显示为未知） | `Map`、`Visited` |
| 当前房间类型/模型 | 是 | `Rooms` |
| 当前决策请求：Token、来源、数量/取消/确认约束、用途、选项实例及 typed 可见事实 | 是 | R3b 的 `RunDecisionImporter` 从原版界面读取；`RunView.From` 目前只填 `Decision` 与选项数 |
| 奖励界面阶段与剩余奖励数 | 是 | `RunRewards.Stage/Count` |
| 卡牌奖励的 id、升级、附魔 | 是（卡面） | `RunRewards.Choices` 投影；请求由 R3b 的 `RunDecisionImporter` 填充 |
| 商店货架：每格 id/升级/附魔、价格、打折、库存，删牌价格与是否已用 | 是 | `RunShop` 投影到 `ShopView`；请求由 R3b 的 `RunDecisionImporter` 填充 |
| 未知点房间概率、卡牌稀有度保底、药水掉落保底 | 界面隐藏，但可由玩家见过的历史推导 | `Odds`/`PlayerOdds` 的当前值 |
| 随机数状态、RNG 计数 | 否 | 不进入 |
| 预生成事件/遭遇序列、后续幕的 Boss/先古与未来内容 | 否（地图上不可见） | 不进入 |
| 遗物袋剩余顺序、`_originalRelics` | 否 | 不进入 |
| 解锁位图、已见遭遇位图、跑局数 | 否（界面只显示已解锁内容，不显示内部集合） | 不进入 |
| 模型私有 `SavedProperties` | 否（卡面只显示派生数字；内部属性不可见） | 不进入 |
| `FreedRepy`、成就、图鉴统计 | 部分可见但只影响界面 | 不进入 |
| 房间内部奖励/事件页/选牌页 | 是 | 共享有界请求 DTO；正式导入器与宿主后续接入 |

`RunView` 只带当前幕的 `Act/Boss/SecondBoss/Ancient`，不带后续幕的对应值；`CardView` 不含
`Private`/属性包；`RelicView` 不含属性包；`MapView` 不含 `CanBeModified`（内部规则）；`RunView` 不含任何 `RunRng*`、`RunRelicBag`、`RunAct`、`RunUnlockState`、`RunHistory`、`RunPropertyBag` 字段。
`RunDecisionRequest`、选项、事实、切片、Token、枚举、共享池、回答以及必要的构造/校验辅助均逐项
登记为 Ai.Run 可见类型，RunShopStage/RunShopEntryFlags/RunView+Shop* 也在白名单内。机器键只
保留公开的 TextKey/休息选项 key，不带完整本地化描述或内部属性；typed 事实只表达当前公开
的已知语义，Unknown/Partial/Complete 区分未知与已知为空。具体 Kind 表与宿主职责见
[决策协议](run-decisions.md)。`RunView.From` 的 Request 仍为 default；WithRequest 先完整校验，
成功才生成同步请求与提示字段的新投影。

`tests/Architecture` 递归检查所有登记可见 DTO 的实例字段，包含 private InlineArray 元素；嵌套
类型也必须在精确白名单内，拒绝托管引用及指针间接访问，不能用一个已登记外壳藏入 RunState、
RNG、属性包或未来序列。正例与嵌套数组/托管引用反例、KernelChecks 的无引用与尺寸合同共同
锁定这一边界，不把整个 Simulation.Run 命名空间放开。

## 7. 原版导入

`src/Import/Run/RunStateImporter.Import(RunState native, Player localPlayer)` 只读，返回 `Sts2Solver.Simulation.Run.RunState`。它先校验顶层范围，再逐组读取；任何未知/第三方/越界内容在写入前拒绝，不返回半成品。

### 7.1 拒绝码

| 码 | 条件 |
| --- | --- |
| `multi-player` | `Players.Count != 1`、`RelicGrabBag._mpFallbackDequeue` 非空 |
| `unsupported-game-mode` | 非 `GameMode.Standard` |
| `unsupported-modifier` | `Modifiers` 非空 |
| `third-party-content` | 任一模型来自非 `sts2` 程序集 |
| `unsupported-card` / `unsupported-relic` / `unsupported-potion` / `unsupported-monster` / `unsupported-enchantment` / `unsupported-affliction` / `unsupported-orb` | 不在 Sts2Solver `Registry` |
| `unsupported-character` / `unsupported-act` / `unsupported-encounter` / `unsupported-event` | 不在 `RunCatalog` |
| `unsupported-unlock` | 解锁 epoch 或已见遭遇不在目录 |
| `unsupported-map` | 地图不是 `StandardActMap`/`SavedActMap` 或坐标越界 |
| `unsupported-room` | 未知房间类型/模型 |
| `unsupported-property` | `SavedProperties` 含未知 `ModelId` 类别或非法属性 |
| `unsupported-relic-state` | 遗物状态/层数不可表示 |
| `unsupported-run` | 顶层结构不一致（幕索引等） |
| `capacity` | 任一容量溢出（`CapacityException.Region` 指出区块） |
| `missing-private-field` | 原版字段缺失（版本不匹配） |

### 7.2 战斗一致性

对每个 D1 战斗根，`--run-import-compare` 在导入 `RunState` 后再用现有 `RulesCompiler`/`BattleImporter` 导入同一原生战斗，并检查：

- 9 条战斗流：`RunState.Rng.Streams[6..14]` 与 `Battle.Rng[0..8]` 的 `Counter/S0..S3` 完全相同；
- HP/最大 HP：`RunState.Player` 与 `Battle.Player`；
- 牌组：战斗牌堆中带 `DeckCard` 标记的持久牌与 `RunState.Deck` 的多重集完全相同；开局生成的额外卡不计入；
- 遗物：数量、顺序、id、Melted 标记；
- 药水：5 个战斗槽与 `RunState` 槽位逐一对应（`Battle` 用 id+1，0 为空）。

`RunStateImporter.Import` 的可选 `RewardsSet` 参数把当前奖励界面导入 `RunState.Rewards`；
不传时读取 `CombatRoom.ExtraRewards`，`Generated=0`。当前房间是 `MerchantRoom` 时，导入器
把 `MerchantInventory` 的货架、基础价、Hook 后价格与打折标记写入 `RunState.Shop`；
`shopStage=CardRemoval` 只用于把原生 UI 正在选卡这一事实投影到 `RunState`（原生状态本身没有
该阶段）。`NativeRunDump` 同时支持传入剩余奖励列表、阶段与商店阶段，供奖励/商店对照使用。

私有属性不参与“导入器对照导入器”：`NativeRunDump` 直接遍历原生 `SavedProperties`，按
`名字哈希:类型:值` 输出（数组逐项、字符串原样、按名字哈希排序）；`RunDump` 则从
`RunPropertyBag` 解码出同样文本。两边只有名字哈希函数 `RunHash.XxHash64` 共用，属性编码器
本身不参与对照；卡牌、附魔与遗物的属性包都走这条路径。

## 8. 验证结果

- `tests/KernelChecks`：`RunState` 尺寸、15 条流派生固定值、编码往返、全部容量溢出、`RunView` 可见性合同。
- 自然开局：5 角色 × A0/A10 × 20 个种子 = **200/200** 逐字段相同；每局 15 条流 × 64 次抽取 = 0 偏差；第一幕地图节点/连线/类型 = **200/200** 相同。
- 商店：自然 187 局 + D1 401 局逐字段与逐动作相同，0 diff；拒绝 9 例均有原因码（R2c-2 报告）。
- D1：**410/410** 输入逐字段相同（含 120 个质量根）；战斗一致性 **410/410** 相同；15 条流 × 64 次抽取 0 偏差；地图 **410/410** 相同。
- 负例：多人、非标准模式、Modifier、未知事件、未知解锁、无地图、牌组容量共 7 类均返回稳定拒绝码。
- 复跑命令见[测试与验证](testing.md)第 6.10 节；本批报告在 `.local/tool-tasks/solver/R1b-report.md`；商店对照在 `.local/tool-tasks/solver/R2c2-report.md`。

## 9. 未完成与未验证

- 事件当前页/选项、休息处选项、宝箱内容与通用选牌页面仍不在本批；奖励与商店货架已进入 `RunState`。
- 房间 `EncounterState` 与 Heist 金币来源未表示；依赖它们的奖励生成显式拒绝。
- 玩家地图点历史统计（金币、卡牌增减、选择记录）未表示；`FakeMerchant` 等事件需要的当前历史属 R2 事件状态。
- `MapPoint.Quests` 未显式存储，计划由 `FurCoat`/`SpoilsMap` 私有属性在 R2 重建；持有 `SpoilsMap` 时商人删牌/商店 Hook 会以 `shop-hook-listener` 拒绝。
- 商店房间的 `BeforeRoomEntered`/`AfterRoomEntered` 只有 32 个已验证的遗物效果实现；`LordsParasol`、`FRESNEL_LENS`、`BING_BONG` 等名单外监听者以 `shop-hook-listener` 拒绝。
- 未合成第三方模型做负例；第三方路径由 `NativeAccess.RequireOriginal` 统一覆盖，与战斗导入同一实现。
- 未跑无头游戏；本批不影响 Mod 行为。
