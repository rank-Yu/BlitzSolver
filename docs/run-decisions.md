# 局外决策协议与地图进点

[返回文档索引](README.md)

本文记录 R2b-2 落地的局外内核协议与地图选点边界：`RunKernel.Step` 的请求、动作与结果类型，
地图合法点的枚举规则，未知点掷骰，以及从地图选点走到"房间已建好、历史已追加、尚未进入房间"
的原版步骤。战斗房间的战斗建局见[战斗边界](run-combat-boundary.md)；新局与地图生成见
[新局生成与地图](run-generation.md)；状态字段与可见性见[整局状态布局](run-state-layout.md)。

## 1. 入口与类型

```csharp
RunKernel kernel = new(contentTable);
RunState next = state;
RunStepResult result = kernel.Step(ref next, RunAction.SelectMapPoint(coord));
```

`RunKernel` 持有进程内的 `ContentTable`，本身无可变状态；一次 `Step` 只读写传入的
`RunState`，不做搜索、不读全局设置。

| 类型 | 内容 |
| --- | --- |
| `RunDecisionRequest` | 当前决策种类 `Kind`、选项数 `OptionCount`、固定容量 129 的 `RunDecisionOptionArray Options` |
| `RunDecisionOption` | `Kind`（`MapPoint` / `EventOption` / `Reward` / `Skip` / `Card` / `Potion` / `Relic` / `ShopEntry` / `CardRemoval` / `RestSiteOption` / `BossRelic` / `Reroll`）、`Index`、`Coord`、点类型、`RewardType`、卡牌的 `Upgrade`/附魔字段、`Model`（`RunRef`）、`Amount` |
| `RunAction` | `Kind`（与决策种类对应）、`Option`（请求里的下标）、`Coord`（地图点）；商店用 `BuyShopEntry` / `RemoveCard` / `LeaveShop` / `CancelCardSelection` |
| `RunStepResult` | `Status`（`Decision` / `Combat` / `Ended` / `UnsupportedRoom` / `Rejected`）、`Reject`、稳定原因码 `Reason`、`Detail`、当前请求、房间与点类型、`CombatSetupResult` |

`RunDecisionRequest.Capture(in RunState)` 是唯一的请求构造入口，`RunView.Request` 直接使用它。
选项按坐标排序：原版地图点的子点是 `HashSet<MapPoint>`，枚举顺序不是语义；协议只承诺集合，
不承诺原版哈希顺序。

地图决策的判定不只看 `RunState.Decision`。该字段是导入层按"当前房间"给出的提示；战斗结束后
地图界面打开时，原版房间栈里仍可能是已完成（`IsPreFinished`）的战斗房间，因此
`Capture` 的规则是：

1. HP 为 0：`GameOver`；
2. `Shop.Stage=CardRemoval`：`CardSelect`（删牌选卡）；
3. `Shop.Stage=Shop`：`Shop`；
4. 奖励阶段（`Rewards.Stage`）非空：奖励请求；
5. 房间栈里没有未完成的非地图房间，且 `RunTravel.Collect` 至少有一个点：`MapPoint`；
6. 否则沿用 `RunState.Decision`（例如事件页；本批这些请求还没有选项）。

`RunDecisionRequest` 只含玩家界面上能看到的东西：地图坐标、点类型、模型 id 与数量。没有随机数
状态、未生成序列、遗物袋顺序或解锁位图；`tests/Architecture` 把请求类型与 `RunView` 一起登记为
`Ai.Run` 可见类型。

## 2. 地图合法点

`RunTravel.Collect` 按原版 `NMapScreen.RecalculateTravelability` 与
`MapTravel.GetTravelablePointsFrom` 的顺序：

| 当前状态 | 合法点 |
| --- | --- |
| 没有已走坐标 | 起点 `Map.Starting` |
| 已走坐标最后一项是 Boss，且本幕有第二 Boss | 第二 Boss |
| 已走坐标最后一行的行号是 `Map.Height - 1` | Boss |
| 其他 | 最后一点的 `Children`，按坐标排序 |

`MapTravel.GetTravelablePointsFrom` 在 `Hook.ShouldAllowFreeTravel` 为真时返回下一行的所有点。
0.111.0 只有 `WingedBoots`（未用尽）和 `Flight` 修正会打开它；R2b-2 不实现自由移动，持有
`ContentTable.FreeTravelRelics` 里的遗物时 `Step` 返回 `Rejected`（`free-travel-hook`），
不会悄悄按普通子点处理。`RunView.Request` 不建模这条规则，持有该遗物时仍列出普通子点；
宿主必须以 `Step` 的结果为准。

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

## 4. 未知点掷骰

`RunUnknownPoint.Roll` 对应 `UnknownMapPointOdds.Roll`：

1. **首次跑局保底**：`Unlock.NumberOfRuns == 0` 时统计历史里 `MapPointType.Unknown` 的条目数；
   `< 2` 返回 `Event`，`== 2` 返回 `Monster`。这两条都**不抽随机数、不改概率**。
2. **候选集**：`{Monster, Elite, Treasure, Shop, Event}` 去掉黑名单。标准模式黑名单只有
   `Shop`（见上）。
3. **Hook**：`ModifyUnknownMapPointRoomTypes` / `ModifyOddsIncreaseForUnrolledRoomType` 会改
   候选集或概率增长。R2b-2 不实现这些 Hook：持有 `ContentTable.UnknownPointHookRelics`
   或 `UnknownPointHookCards` 里的模型时返回 `Rejected`（`unknown-point-hook`）。
4. **浮点抽取**：`UnknownMapPoint` 流一次 `NextFloat()`（`0..1`，`float`）。按原版字典插入
   顺序 `Monster, Elite, Treasure, Shop` 累加**当前**概率，负数项跳过；`roll <= cumulative`
   命中；没有命中任何非事件项则 `Event`。
5. **概率成长**：再按同一顺序，命中项重置为 `BaseOdds` 里该类型的值，其余仍在候选集里的项
   加上**基础值**（不是当前值）。Elite 的基础值是负数，所以每次未命中都会继续变小，标准模式
   下永远掷不出 Elite。
6. `ResetToBase` 在换幕的 `SetActInternal` 里完成（`RunGenerator.SetAct`）；`Rng.Odds`
   当前值保存在 `RunState.Odds`。

首次跑局的教学分支 `TryGetRoomTypeForTutorial` 在 `RollRoomTypeFor` 之前：仅当
`pointType == Unassigned`、非测试模式、单人、`NumberOfRuns == 0` 且历史里没有
`Unassigned` 条目时把房间定为 `Event`。标准地图的起点是 `Ancient`（无 Neow 的幕 0 被改成
`Monster`），其余点都有类型，所以这个分支在标准新局里不触发；它仍按原版条件实现，供
调试/未来入口复用。

## 5. 非战斗房间的边界

`Step` 对非战斗点的结果是 `UnsupportedRoom`，状态推进到原版第 9 步之后、第 10 步之前：

- `ActFloor` 已更新；
- `Visited.Coords` 已追加目标坐标；
- `Rooms` 为空（旧房间已退出，新房间还没压入）；
- `History` 已追加一条，`MapPointType` 是目标点类型，首个房间是掷出的房间类型与模型；
- `Visited.Events` 已标记（事件点）；
- `Rng.Events` 已有一条事件 RNG，初态是 `seed + slot(0) + hash(event id)`。原版在
  `EventModel.BeginEvent`（进入事件房间时）才创建它；本批在 PullNextEvent 之后预先创建，
  这样 R2e 拿到边界状态就能直接生成事件页。对照时该字段单独验证，不参与逐行 RunState 比较；
- `Act` 的 `EventsVisited` 已按 `EnsureNextEventIsValid` 前进，但 `Act.MarkRoomVisited`
  尚未执行；
- `Odds` 已按未知点掷骰更新；
- `Decision` 与 `DecisionOptionCount` 清零，等待房间内容阶段设置新请求。

宝箱遗物、休息处选项、事件页与先古之民选项都不在本批：结果里的 `Reason` 分别是
`room-content-treasure`、`room-content-rest-site`、`room-content-event`、
`room-content-ancient`。商店已改为返回 `Decision`（见 5.2），不再返回 `room-content-shop`。
这不是异常，也不是默认值；调用方必须显式处理。
`UnsupportedRoom` 是终态：该状态不是"等待决策"，调用方在内容阶段落地前不应再调用 `Step`，
也不应使用 `RunView.Request`（它按地图规则仍会列出后续点，因为房间栈为空且历史已追加）。

事件拉取的原版细节：

- `RoomSet.EnsureNextEventIsValid` 从 `Events[eventsVisited % Count]` 开始，跳过已访问的事件，
  每跳过一次 `eventsVisited++`；全部耗尽后允许重复。
- `ActModel.PullNextEvent` 调 `Hook.ModifyNextEvent`，然后 `RunState.AddVisitedEvent`。
- 某些事件的 `IsAllowed` 依赖跑局状态（金币、牌组、遗物、HP、幕、药水等）。本批不实现这些
  规则：候选事件的 `ContentTable.EventAllowedOverride` 为真时返回 `Rejected`
  （`event-allowed-check`），不猜它是否允许。R2e 接管后由事件规则决定。
- `ModifyNextEvent` 的持有者（`LanternKey`）本批显式 `Rejected`（`event-hook`）。
- 先古点 `PullAncient` 只取本幕 `Act.AncientId`，不写 `Visited.Events`；事件 RNG 仍按同一
  公式预置。Neow 与先古事件页仍是 R2f。

### 5.1 战后奖励决策（R2c-1）

奖励界面不是地图选点：`RunState.Rewards.Stage` 是先于地图的判定。三个阶段的完整选项与
动作见[战后奖励](run-rewards.md)第 3 节。要点：

- `Rewards.Stage=List` 时 `Capture` 返回 `CombatReward`，选项是剩余奖励加“离开”；
  `TakeReward.Option` 是**请求下标**，内核再用 `RunDecisionOption.Index` 找奖励项。
- `TakeReward` 拿到卡牌奖励时切到 `CardReward`，拿到满槽药水时切到 `PotionDiscard`；
  这两个阶段的 `SkipReward` 只跳过当前一项。
- 所有奖励取完后仍停在 `List`，只剩“离开”；`LeaveRewards` 清空界面并回到地图。
- `RunDecisionRequest.Capture` 只看玩家可见的奖励列表、卡牌选项与药水槽；随机数、遗物袋
  顺序和未来序列不在请求里。
- 写回后必须先调用 `RunRewardGenerator.GenerateForCombat`；未生成时奖励动作返回
  `rewards-not-generated`，奖励界面未离开时地图选点返回 `reward-open`。

### 5.2 商店与删牌选卡（R2c-2）

商店在同一个 `Step` 里完成“地图点 → 压入 `MerchantRoom` → `BeforeRoomEntered` → 生成货架 →
`AfterRoomEntered` → 返回 `Decision`”。原版步骤与 Hook 见[商店](run-shop.md)。

**商店界面**：`RunDecisionRequest.Kind=Shop`，选项按固定顺序：

1. 角色卡 5 格、无色卡 2 格、遗物 3 格、药水 3 格中**有库存且买得起**的条目，`Kind=ShopEntry`，
   `Index` 是全局条目号（0–4 角色卡、5–6 无色卡、7–9 遗物、10–12 药水），`Model`/`Amount`
   是模型与 Hook 后价格；卡牌还带升级/附魔字段；
2. 删牌服务 `Kind=CardRemoval`，`Index=13`，`Amount=价格`，仅在未用过、买得起且牌组有可删卡时出现；
3. 离开 `Kind=Skip`。

药水在满槽、或 `Sozu` 禁止获得时不是合法选项；遗物/卡牌的获得效果或 Hook 不支持时，购买动作
`Rejected`（`relic-obtain-effect`、`shop-hook-listener` 等），状态不变。`BuyShopEntry.Option`
是**请求下标**；内核对 `RunDecisionOption.Index` 的条目执行购买、补货/售罄、`AfterItemPurchased`、
`UpdateEntries`，并重算价格。删牌服务购买后切到 `CardSelect`，此时不扣金币。

**从牌组选卡**：`RunDecisionRequest.Kind=CardSelect`，选项是满足条件的牌组卡（本批条件为
`RunCardFlags.Removable`，即非 Eternal），按原版 `FromDeckForRemoval` 的顺序：诅咒在前、
其余按牌组顺序；最后是取消 `Kind=Skip`。`RemoveCard.Option` 是请求下标，选中后扣金币、删牌、
`CardShopRemovalsUsed++`、标记删牌服务已用，再回到 `Shop`；`CancelCardSelection` 或选取消
不扣金币、不消耗服务，直接回到 `Shop`。同一 id/升级的重复牌按原版从牌组中取第一张。

**离开**：`LeaveShop` 清空货架、弹出商店房间并把 `Decision` 置回无；下一个请求是 `MapPoint`。
商店房间的 `Exit` 只写玩家历史里的未买条目（`wasPicked=false`），这些玩家统计不在 `RunState`；
离线 TestMode 下原版 `Exit` 直接返回。

## 6. 结果与拒绝码

| 结果 | 含义 |
| --- | --- |
| `Decision` | 动作完成，`RunStepResult.Request` 是下一个请求 |
| `Combat` | 战斗已建好，`RunStepResult.Combat` 是 R2a-2 的 `CombatSetupResult` |
| `Ended` | 玩家 HP 为 0，或状态已是 `GameOver` |
| `UnsupportedRoom` | 房间已建、历史已追加、尚未进入；`Reason` 标明内容阶段 |
| `Rejected` | 动作非法或当前不支持；`RunState` 不变 |

拒绝码（`RunStepReject` / `Reason`）：

| 码 | 场景 |
| --- | --- |
| `wrong-action-kind` | 本批只接受 `SelectMapPoint` |
| `not-map-decision` | 当前不是地图请求 |
| `unknown-map-point` | 坐标不在当前地图 |
| `not-travelable` | 坐标不是当前合法点 |
| `free-travel-hook` | 持有改写自由移动的遗物 |
| `unknown-point-hook` | 未知点掷骰遇到改写候选/成长的模型 |
| `event-hook` | 事件拉取遇到 `ModifyNextEvent` |
| `event-allowed-check` | 候选事件的 `IsAllowed` 依赖跑局状态 |
| `room-stack-active` | 房间栈里有未完成的非地图房间 |
| `combat-unsupported` | 战斗建局的内容表拒绝（原因码见战斗边界） |
| `corrupt-map` | 已走坐标找不到对应地图点（`Collect` 返回空） |
| `not-reward-decision` / `reward-index` / `card-option` / `potion-slot` | 奖励阶段下标或动作不符 |
| `rewards-not-generated` / `reward-open` | 奖励尚未生成 / 奖励界面未离开 |
| `reward-hook-relic` / `relic-obtain-effect` / `gold-proportion-encounter` | 奖励 Hook、遗物获得效果或金币比例未实现 |
| `not-shop-decision` / `shop-option` / `shop-state` | 商店阶段动作或下标不符 |
| `shop-hook-listener` | 当前监听者重写了未实现的商店 Hook |
| `shop-removal-card` / `shop-potion` | 选中的卡不可删 / 药水不能获得 |

## 7. 后续决策的接法

本批只填 `MapPoint`，但请求、动作与结果已经按后续阶段设计：

| 阶段 | 决策 | 选项 | 动作 |
| --- | --- | --- | --- |
| R2c | 战后奖励、离开 | 每条奖励 `Kind=Reward/Skip`、模型与金币 | `TakeReward` / `LeaveRewards` |
| R2c | 选牌、药水满槽 | 牌/药水 `Kind=Card/Potion`，下标即原版界面下标；Driftwood 时 `Kind=Reroll` | `ChooseCard` / `ChoosePotion` / `SkipReward` / `RerollCardReward` |
| R2c-2 | 商店、删牌 | `Kind=ShopEntry/CardRemoval`，`Amount` 放价格；删牌选卡 `Kind=Card/Cancel` | `BuyShopEntry` / `RemoveCard` / `CancelCardSelection` / `LeaveShop` |
| R2d | 宝箱、遗物选择 | `Kind=Relic` | `ChooseRelic` |
| R2e | 事件页、多页 | `Kind=EventOption`，`Index` 是当前页选项下标 | `ChooseEventOption` |
| R2e | 休息处 | `Kind=RestSiteOption` | `RestSiteOption` |
| R2f | 先古之民、Boss 遗物 | `Kind=EventOption/BossRelic` | `ChooseEventOption` / `ChooseBossRelic` |

实现新决策时只在 `Capture` 里填请求、在 `Step` 里按 `RunActionKind` 分支；不新增请求类型，
也不把随机数或未来序列放进请求。原版界面自动完成的选择按原版规则在 `Step` 内自动完成，
再返回下一个 `Decision` 或 `Combat`。`UnsupportedRoom` 是过渡状态：内容阶段落地后，同一个
房间应返回 `Decision`（例如商店货架）而不是 `UnsupportedRoom`。

## 8. 验证

`tests/OfflineHost` 的 `--map-step-compare` 从新局出发，用原版推进到每个地图选点；对每个合法
选项先从同一份 `SerializableRun` 快照恢复，再分别执行原版与 `RunKernel.Step`。战斗比较
`Battle`/`Rules`/`RunState`，非战斗比较 `RunState`（事件 RNG 单独验证）。商店决策与购买由
`--shop-compare` 单独比较；`--map-step-compare` 只确认商店点返回 `Shop` 决策。规模与结果见
[测试与验证](testing.md) 6.18、R2b-2 与 R2c-2 报告。
