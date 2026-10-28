# 局外决策协议与地图进点

[返回文档索引](README.md)

本文记录局外决策的类型、地图合法点、未知点掷骰，以及从地图选点走到"房间已建好、历史已追加、
尚未进入房间"的原版步骤。2026-10-27 起局外由原版推进，决策请求由导入层读出（R3b）；本文只
描述仍保留的协议与 `RunMapEntry` 边界。战斗房间的建局见[战斗边界](run-combat-boundary.md)，
状态字段与可见性见[整局状态布局](run-state-layout.md)。

## 1. 保留的类型

`RunKernel.Step` 已删除；请求不再由 Core 生成。保留：

| 类型 | 内容 |
| --- | --- |
| `RunDecisionRequest` | 当前决策种类 `Kind`、选项数 `OptionCount`、固定容量 129 的 `RunDecisionOptionArray Options` |
| `RunDecisionOption` | `Kind`（`MapPoint` / `EventOption` / `Reward` / `Skip` / `Card` / `Potion` / `Relic` / `ShopEntry` / `CardRemoval` / `RestSiteOption` / `BossRelic` / `Reroll`）、`Index`、`Coord`、点类型、`RewardType`、卡牌的 `Upgrade`/附魔字段、`Model`（`RunRef`）、`Amount` |
| `RunAction` | `Kind`（与决策种类对应）、`Option`（请求里的下标）、`Coord`（地图点） |
| `RunActionKind` | `SelectMapPoint`、事件/奖励/商店/休息处/选卡/选遗物等动作 |
| `RunView.Request` | 投影中的当前请求；R3b 的 `RunDecisionImporter` 填充 |

`RunDecisionImporter` 放在 `src/Import/Run/Decisions/`，从原版当前界面/模型读出完整选项。
`RunView.From` 暂时只填 `Decision` 与 `DecisionOptionCount`，`Request` 留空，等 R3b 接入；
在此之前，导入后的地图选项由 `RunTravel.Collect` 独立枚举，用于差分。

`RunStepStatus`/`RunStepResult`/`RunStepReject` 是旧局外内核的结果类型，已随 `RunKernel` 删除。
仍保留的拒绝原因是 `RunStepReason` 的四个字符串，供 `RunMapEntry`/`RunUnknownPoint` 抛
`ContentUnavailableException` 时使用：

| 码 | 场景 |
| --- | --- |
| `room-stack-active` | 房间栈里有未完成的非地图房间 |
| `unknown-point-hook` | 未知点掷骰遇到改写候选/成长的模型 |
| `event-hook` | 事件拉取遇到 `ModifyNextEvent` |
| `event-allowed-check` | 候选事件的 `IsAllowed` 依赖跑局状态 |

## 2. 地图合法点

`RunTravel.Collect` 按原版 `NMapScreen.RecalculateTravelability` 与
`MapTravel.GetTravelablePointsFrom` 的顺序：

| 当前状态 | 合法点 |
| --- | --- |
| 没有已走坐标 | 起点 `Map.Starting` |
| 已走坐标最后一项是 Boss，且本幕有第二 Boss | 第二 Boss |
| 已走坐标最后一行的行号是 `Map.Height - 1` | Boss |
| 其他 | 最后一点的 `Children`，按坐标排序 |

原版在 `Hook.ShouldAllowFreeTravel` 为真时返回下一行的所有点；0.111.0 只有 `WingedBoots`
（未用尽）和 `Flight` 修正会打开它。R3-0 不再在 Core 建模自由移动，`RunTravel` 只给出普通
子点；原版执行器与 R3b 的一致性检查以原版实际合法点为准。

`RunTravel.CanTravel` 还要求房间栈里没有未完成的非地图房间；战斗结束回地图时原版会保留一个
`IsPreFinished` 的战斗房间，这不算阻塞。

## 3. 一次地图进点的原版顺序

下表对应 `RunManager.EnterMapCoord` / `EnterMapCoordInternal` / `EnterMapPointInternal`
（sts2 `v0.111.0`）。"状态"列只列 `RunState` 会保存的字段；UI、存档、同步与
`PlayerMapPointHistoryEntry` 的玩家统计不在状态里。

| # | 原版步骤 | 写入 `RunState` | 随机数 |
| ---: | --- | --- | --- |
| 1 | `EnterMapCoord`：`State.AddVisitedMapCoord(coord)` | 已走坐标追加；重复坐标直接返回 | — |
| 2 | `EnterMapCoordInternal` | — | — |
| 3 | `UpdatePlayerStatsInMapPointHistory`（历史非空时） | 只写历史里的玩家金币/HP，不在 `RunState` | — |
| 4 | `State.ActFloor = coord.row + 1` | `ActFloor` | — |
| 5 | `ExitCurrentRooms` | 逐个弹出 `Rooms` 并执行 `Exit`；本批在进点后为空栈 | — |
| 6 | `BuildRoomTypeBlacklist(上一历史项, CurrentMapPoint.Children)` | — | — |
| 7 | `RollRoomTypeFor(pointType, blacklist)` | 未知点更新 `Odds` | 未知点：`UnknownMapPoint` 流一次 `NextFloat` |
| 8 | `CreateRoom(roomType, pointType)` | 事件：`PullNextEvent` 标记已访问事件；战斗：取下一遭遇 | 事件 RNG 在进入事件时创建 |
| 9 | `AppendToMapPointHistory(pointType, roomType, modelId)` | 新历史条目 | — |
| 10 | `EnterRoom` → `EnterRoomInternal` → `room.Enter` | 压入 `Rooms`；`Act.MarkRoomVisited` 增加访问计数 | 战斗见[战斗边界](run-combat-boundary.md) |

关键点：

- `EnterMapCoord` 先追加已走坐标，再进入 `EnterMapPointInternal`。因此第 6 步的
  `CurrentMapPoint` 是**新点**，黑名单用的是新点的子点，不是上一点。
- 黑名单只有一种：上一历史房间含商店，或新点的所有子点都是商店时，禁止把未知点掷成商店。
- 历史在 `CreateRoom` 之后、`EnterRoom` 之前追加；`CreateRoom` 已经消耗了遭遇/事件序列。
- `EnterRoomInternal` 在 `room.Enter` 之后才 `Act.MarkRoomVisited`；非战斗房间在"进入之前"
  停下时，事件/战斗的访问计数都还没有增加（事件游标可能在第 7/8 步被
  `EnsureNextEventIsValid` 推进）。
- 普通地图点没有"事件点"类型；事件房间只来自 `Unknown` 与 `Ancient`。

`RunMapEntry.Enter(in RunState, ContentTable, RunCoord)` 实现第 1–9 步：

- 战斗房间继续 `RunCombatSetup.BuildCombat`，返回 `CombatSetupResult`，或在开战选择未给全时
  返回 `CombatChoice`（`RunMapEntryResult.CombatChoice`）；
- 非战斗房间返回 `RunMapEntryResult.Run`，状态停在原版第 9 步之后、第 10 步之前；
- 任何内容层拒绝抛 `ContentUnavailableException`，状态不变；调用方决定停止还是交给原版执行。

## 4. 未知点掷骰

`RunUnknownPoint.Roll` 对应 `UnknownMapPointOdds.Roll`：

1. **首次跑局保底**：`Unlock.NumberOfRuns == 0` 时统计历史里 `MapPointType.Unknown` 的条目数；
   `< 2` 返回 `Event`，`== 2` 返回 `Monster`。这两条都**不抽随机数、不改概率**。
2. **候选集**：`{Monster, Elite, Treasure, Shop, Event}` 去掉黑名单。标准模式黑名单只有
   `Shop`（见上）。
3. **Hook**：`ModifyUnknownMapPointRoomTypes` / `ModifyOddsIncreaseForUnrolledRoomType` 会改
   候选集或概率增长。R3-0 不实现这些 Hook：持有 `ContentTable.UnknownPointHookRelics`
   或 `UnknownPointHookCards` 里的模型时抛 `ContentUnavailableException`（`unknown-point-hook`）。
4. **浮点抽取**：`UnknownMapPoint` 流一次 `NextFloat()`（`0..1`，`float`）。按原版字典插入
   顺序 `Monster, Elite, Treasure, Shop` 累加**当前**概率，负数项跳过；`roll <= cumulative`
   命中；没有命中任何非事件项则 `Event`。
5. **概率成长**：再按同一顺序，命中项重置为 `BaseOdds` 里该类型的值，其余仍在候选集里的项
   加上**基础值**（不是当前值）。Elite 的基础值是负数，所以每次未命中都会继续变小，标准模式
   下永远掷不出 Elite。
6. `ResetToBase` 在换幕的 `SetActInternal` 里由原版完成；`RunState.Odds` 保存当前值。

首次跑局的教学分支 `TryGetRoomTypeForTutorial` 在 `RollRoomTypeFor` 之前：仅当
`pointType == Unassigned`、非测试模式、单人、`NumberOfRuns == 0` 且历史里没有
`Unassigned` 条目时把房间定为 `Event`。标准地图的起点是 `Ancient`（无 Neow 的幕 0 被改成
`Monster`），其余点都有类型，所以这个分支在标准新局里不触发；它仍按原版条件实现，供
调试/未来入口复用。

## 5. 非战斗房间的边界

`RunMapEntryResult` 对非战斗点停在原版第 9 步之后、第 10 步之前：

- `ActFloor` 已更新；
- `Visited.Coords` 已追加目标坐标；
- `Rooms` 为空（旧房间已退出，新房间还没压入）；
- `History` 已追加一条，`MapPointType` 是目标点类型，首个房间是掷出的房间类型与模型；
- `Visited.Events` 已标记（事件点）；
- `Rng.Events` 已有一条事件 RNG，初态是 `seed + slot(0) + hash(event id)`。原版在
  `EventModel.BeginEvent`（进入事件房间时）才创建它；R3-0 在 PullNextEvent 之后预先创建，
  对照时该字段单独验证，不参与逐行 `RunState` 比较；
- `Act` 的 `EventsVisited` 已按 `EnsureNextEventIsValid` 前进，但 `Act.MarkRoomVisited`
  尚未执行；
- `Odds` 已按未知点掷骰更新；
- `Decision` 与 `DecisionOptionCount` 清零；请求由 R3b 从原版读出。

宝箱遗物、休息处选项、事件页与先古之民选项由原版执行器处理；R3-0 的 `RunMapEntry` 只给出
"房间已建好、尚未进入"的状态，不在 Core 生成这些内容。

事件拉取的原版细节：

- `RoomSet.EnsureNextEventIsValid` 从 `Events[eventsVisited % Count]` 开始，跳过已访问的事件，
  每跳过一次 `eventsVisited++`；全部耗尽后允许重复。
- `ActModel.PullNextEvent` 调 `Hook.ModifyNextEvent`，然后 `RunState.AddVisitedEvent`。
- 某些事件的 `IsAllowed` 依赖跑局状态（金币、牌组、遗物、HP、幕、药水等）。R3-0 不实现这些
  规则：候选事件的 `ContentTable.EventAllowedOverride` 为真时抛
  `ContentUnavailableException`（`event-allowed-check`），不猜它是否允许。
- `ModifyNextEvent` 的持有者（`LanternKey`）显式拒绝（`event-hook`）。
- 先古点 `PullAncient` 只取本幕 `Act.AncientId`，不写 `Visited.Events`；事件 RNG 仍按同一
  公式预置。

## 6. 后续决策的接法

R3b 的 `RunDecisionImporter` 为每种界面填同一个 `RunDecisionRequest`：

| 阶段 | 决策 | 选项 |
| --- | --- | --- |
| R3b | 地图选点 | `Kind=MapPoint`，坐标与点类型 |
| R3b | 战后奖励、离开 | 每条奖励 `Kind=Reward/Skip`、模型与金币 |
| R3b | 选牌、药水满槽 | `Kind=Card/Potion`，下标即原版界面下标；可重抽时 `Kind=Reroll` |
| R3b | 商店、删牌 | `Kind=ShopEntry/CardRemoval`，`Amount` 放价格；删牌选卡 `Kind=Card/Cancel` |
| R3b | 宝箱、遗物选择 | `Kind=Relic` |
| R3b | 事件页、多页 | `Kind=EventOption`，`Index` 是当前页选项下标 |
| R3b | 休息处 | `Kind=RestSiteOption` |
| R3b | 先古之民、Boss 遗物 | `Kind=EventOption/BossRelic` |

导入层只读原版当前界面，不读随机数状态或未来生成序列；请求里也不放这些信息。

## 7. 验证

`tests/Differential` 的 `--map-step-compare` 从新局出发，用原版推进到每个地图选点；对每个
合法选项先从同一份 `SerializableRun` 快照恢复，再分别执行原版 `EnterMapCoord` 与
`RunMapEntry.Enter`。战斗比较 `Battle`/`Rules`/`RunState`，非战斗比较进点后的 `RunState`
（事件 RNG 单独验证）。这是"保留的对照必须与 `683bc03` 相同"的一部分；结果见
[测试与验证](testing.md)。
