# 局外决策协议与地图进点

[返回文档索引](README.md)

本文记录局外决策的类型、地图合法点、未知点掷骰，以及从地图选点走到"房间已建好、历史已追加、
尚未进入房间"的原版步骤。2026-10-27 起局外由原版推进，决策请求由导入层读出（R3b）；本文
描述协议、原版决策导入首批与 `RunMapEntry` 边界。战斗房间的建局见[战斗边界](run-combat-boundary.md)，
状态字段与可见性见[整局状态布局](run-state-layout.md)。

## 1. 请求、回答与共享负载

Core 提供有界、无托管引用的可见协议；`RunDecisionImporter` 已支持首批原版地图、事件、
休息、选牌、奖励列表/选牌奖励、药水整理、商店及宝箱展示阶段待选请求。协议能表达一种界面不代表已有该界面的执行桥接。`RunState`/`RunCodec v3` 的 49912 字节
布局不变，`RunDecision` 的既有数字不变。

| 类型 | 内容与预算 |
| --- | --- |
| `RunDecisionToken` | 非零 `ulong SessionId` 与非零 `ulong Revision`，16 字节 |
| `RunDecisionRequest` | Token、可见 `Source`、`PageKey`、Kind、独立数量/取消/确认约束、选牌用途、预览知识及共享池，15960 字节 |
| `RunDecisionOption` | 非零请求内 `OptionId`、Kind、明确 `PayloadKind`、可见锁定/禁用 flags、NativeKey、typed 代价/效果及 bundle 切片，44 字节 |
| `RunDecisionFact` | 正数 `Amount`、FactKind；只有 AddCard 带完整可见 CardView，16 字节 |
| `RunDecisionSlice` | `ushort Start/Count`，4 字节；空切片必须为 default |
| `RunAction` | 完整 Token、`Select/Cancel`、有序 OptionId 列表，536 字节 |
| `RunView.Request` | 当前请求；`WithRequest` 先校验再返回同步 Request/Decision/DecisionOptionCount 的新投影，投影共 20912 字节 |

请求保留 129 个选项，Cancel 独立于选项。`RunDecisionLimits` 设定共享 Facts 256 条、bundle
Cards 256 张、机器键 UTF-8 字节池 4096 字节。129 沿用现有工程预算；新共享池也是明确的工程
上限，不是原版上限或全内容覆盖承诺。原版无限增长的候选/组合/文本超过容量时显式拒绝。
各选项只携带切片，不各自内嵌大数组；共享切片可复用，但必须完全落在已填充池内。

`OptionId` 对应请求中的可选实例，不能用数组下标、模型 id 或机器键替代。两张同模型牌可有
不同 id、升级和附魔；旧快照的 id 始终指捕获的原实例；候选重排使快照过期，新导入必须换 Token。bundle 内允许重复模型，选择一次取得
整个组合，不把组内牌数计入 SelectCount。原版实例映射封装在 Import 快照中，由宿主持有快照，Core 不保存游戏引用。

`PayloadKind` 是 None/Map/Card/Model/Reward/CardBundle 的互斥联合；没有激活的字段必须为空。
Map 带坐标与点类型；Card 带 CardView；Model 带 RunRef；Reward 带奖励种类和可见模型，
卡牌奖励容器不冒充选牌子请求；CardBundle 带共享牌池切片。Source 可为 None/Event/Relic，
只表达玩家可见的请求来源；EventOption 的 Model 允许 Event/Relic（含 Neow 的可见遗物选项）。

Costs 与 Effects 分别标注 `Unknown/Partial/Complete`。Unknown 只能有零条目，Partial 必须有
已知条目，Complete 可为零以明确表示已知为空。不能把默认的零条目理解为确定无代价/效果。
当前最小事实集只支持 Costs 的 GoldLoss/HpLoss，以及 Effects 的 GoldGain/RemoveCards/AddCard；
每条 Amount 必须为正数，AddCard 还包含可见升级/附魔。事实是当前可见说明或数量，不是实际
结算预测；Complete 只承诺这类可见描述完整，不保证原版 hooks/条件结算后的净损益仍等于这些
数值。未映射的附魔、随机遗物、条件结算等
保持未知或部分已知，不放入预测结果、隐藏随机值或策略评分。Facts 不是执行指令，原版负责结算。

NativeKey 与可选 PageKey 保存原版 `TextKey`、休息选项 `OptionId` 等机器键的原样 UTF-8，
空键表示未提供。构造与校验都严格拒绝坏 UTF-8、分裂码点和越界，不截断。它们帮助未来策略
区分选项，但不作为执行身份，不解析文本推断效果。协议不保存完整本地化文案或动态参数；
首批导入器读取机器键及两个已映射事件的 typed 事实；完整本地化参数仍未接入。`SelectionPurpose` 仅用于 CardSelect，
可为 None/Remove/Upgrade/Transform/Enchant；None 表示用途未描述，不能猜测。
SelectionPreviewKnowledge 目前必须为 Unknown：它不承诺变牌/附魔后的卡牌预览，不能把当前 CardView 当作预测结果。
Treasure=11 与 Open=15 追加到既有请求/选项枚举；GameOver=10 仍不是可执行请求。
封箱 Treasure 请求只含无负载 Open，真实开箱之后才用 RelicSelect/Skip 展示原实例；完成后的 Treasure/Leave 才允许离开。

`RunView.From` 目前只填状态里的决策提示与选项数，Request 保持 default，不能作为可执行
请求。地图合法点仍由 `RunTravel.Collect` 独立枚举；`RunMapEntry.Enter` 仍接受坐标，不是第二个
请求执行器。正式 OfflineRunHost/RunExecutor 已执行 Map/Event/Rest/Shop/CardSelect/Treasure，策略估值与全内容导入仍未落地；测试桥接见第 7 节。

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
| 5 | `ExitCurrentRooms` | 逐个弹出 `Rooms` 并执行 `Exit`；此边界在进点后为空栈 | — |
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

## 6. Kind 兼容与严格回答校验

| 请求 Kind | 允许的选项 Kind（括号为负载） |
| --- | --- |
| MapPoint | MapPoint(Map) |
| Event | EventOption(None 或 Event/Relic Model)、Leave(None) |
| CombatReward | Reward(Reward)、Leave(None) |
| CardReward | Card(Card) 或 CardBundle(CardBundle)、Skip(None)、Reroll(None)；同一请求不能混 Card 和 CardBundle |
| Shop | ShopEntry(Card 或 Potion/Relic Model)、CardRemoval(None)、Leave(None) |
| RestSite | RestSiteOption(None)、Leave(None) |
| CardSelect | 只有 Card(Card)，不能混 Leave/Skip/Cancel/Reroll |
| RelicSelect | Relic/BossRelic(Relic Model)、Skip(None) |
| PotionDiscard | Potion(Potion Model)、Skip(None) |

控制行只在原版确实展示时导入；表格表示合同可表达的范围，不授权虚构 Skip/Leave/Reroll。
强制奖励没有 Skip/Leave。None、GameOver 和未知 Kind 不是可执行选择请求。所有非 CardSelect
请求必须 MinSelect=MaxSelect=1；CardSelect 支持 0..129 的精确/区间多选，且可选数量至少满足 Min。

回答只能是 Select 或 Cancel。Select 保留 OptionId 顺序、禁止重复；空列表仅在 Min=0 时有效，
不代表取消。Cancel 只在 Cancelable=1 且列表为空时有效。Cancelable、Min/Max 与
RequireConfirmation 彼此独立；确认是宿主/UI 的执行约束，Core 校验成功不代表玩家已确认。

`RunDecisionProtocol.ValidateRequest` 与 `ValidateAction(request, action)` 是统一入口，后者首先
完整校验请求，再检查 Token、回答种类、选择数量、重复/不存在 id 与禁用/锁定行。请求校验在
任何计数或切片索引之前检查容量，拒绝重复 id、非法枚举/flags、错误 payload、Kind 不匹配、
越界切片与启用项不足 Min；所有失败不修改输入、不执行部分回答。`Select/Cancel` 构造辅助
使用同一入口，`ResolveOptionIndex` 在校验后按 id 查找。`RunDecisionBuilder` 只负责有界填充池，
最终请求仍须完整校验；容量失败在写入前拒绝。

格式错误抛 `InvalidDataException`，消息冒号前是稳定原因码：`request-token/request-flags`、
`selection-purpose/selection-bounds`、`source-kind/model-ref`、`option-id/option-flags/option-kind`、
`payload-kind/payload-unused/payload-model/payload-slice/kind-payload`、`knowledge/cost-kind/effect-kind`、
`fact-kind/fact-amount/fact-card`、`card-flags/card-enchantment`、`map-point-type/reward-type/reward-model`、
`bundle-empty/reward-entity/enabled-count/key-utf8`、`action-token/action-kind/action-count/action-cancel`、
`action-duplicate/action-option/action-disabled`。容量不足抛 `CapacityException`；未知内容或不支持的
请求种类抛 `UnsupportedException`，不截断或猜测。

宿主必须生成不透明 SessionId、维护单调 Revision、持有当前待选请求、重新核对原版候选实例并
在执行/取消/换页后消费或失效 Token。不得仅由种子、内容 hash 或隐藏 RNG 推导身份。
Core 只比较回答与给定请求的完整 Token，不保存已消费历史；再次传入旧请求与旧回答仍可能
通过纯校验，宿主必须承担防重放及生命周期。推荐模式同样使用这些规则。

原版 FromSimpleGrid/FromDeckGeneric 在候选不多于 Min 且不需手动确认时可能自动全选，
根本不产生 pending 请求。导入器区分此路径与真实待选请求，依据真实可选数量形成
有效约束，不能照抄 prefs 后制造无法满足 Min 的请求。宿主保留原版自动行为。

## 7. 原版首批导入与绑定

`src/Import/Run/Decisions/RunDecisionImporter*.cs` 只读原版当前模型。各入口返回
`NativeDecisionSnapshot<T>`；`Request` 按值返回纯 Core 数据，私有绑定数组不向策略暴露。
`Resolve` 先完整 `ValidateAction`，再按回答顺序返回只读原实例列表；不执行游戏、不消费 Token。
候选实例不可重复引用，同模型不同实例保留各自升级、附魔与不同 OptionId。
`ValidateCurrent` 重读可见请求并逐项比较，再按 `ReferenceEquals` 检查实例与顺序；不能用
`RestSiteOption.Equals`，原版该方法按 OptionId 与 Owner 比较。

| 入口 | 原版依据与限制 |
| --- | --- |
| `Map(run, token, isTravelEnabled)` | `isTravelEnabled` 是宿主当前已到地图待选阶段的实时只读回调，不由 Import 猜测；同时检查 `Hook.ShouldProceedToNextMapPoint`。合法点按原版起点、第二 Boss、末行 Boss 与 `MapTravel.GetTravelablePointsFrom` 枚举，保留免费移动 Hook。重核 room、已走坐标与候选身份。 |
| `Event(model, token)` | 必须是当前 `EventRoom.LocalMutableEvent`，已初始化 Description、CurrentOptions 与 DynamicVars。不启动事件、不生成页面。保留原序、锁定、IsProceed、TextKey、可见 Source/Relic；仅 `WasChosen && DisableOnChosen` 禁用，允许再次选择的按钮保留。 |
| `Rest(room, player, token)` | 只读当前 `RestSiteRoom.Options`，保留原实例、IsEnabled 与 OptionId 机器键；不调用会耗 RNG 的 Generate，不重写休息/升级数值。 |
| `CardSelect(player, options, prefs, kind, token, purpose)` | 由原版实际 pending 回调提供真实候选、完整 prefs 与原命令种类，不能只由 ICardSelector 的 min/max 推导取消。支持 DeckGeneric、DeckUpgrade 与 SimpleGrid；升级预览首批要求 Min=Max，其他范围显式拒绝。用途默认 None，仅来源明确时指定。 |

ThisOrThat 的 PLAIN 从当前 DynamicVars.HpLoss/Gold 导入 HpLoss 与 GoldGain；
FieldOfManSizedHoles 的 RESIST 从当前 DynamicVars.Cards 和原版 Normality 模型导入
RemoveCards 与 AddCard。两者 Complete 仅描述当前可见语义，不预测 Hooks 后净收益；其余
事件保持 Unknown，不扫描未来随机结果或解析本地化文本。

已完成事件没有模型内待选行时显式拒绝：`NEventRoom.SetOptions` 另外生成的 UI PROCEED
不在 CurrentOptions 内，不能伪造为模型选项。已有模型内 IsProceed 行可映射 Leave。
原版自动全选、零候选提前返回不产生请求；手动确认且 Min 大于实际候选数时显式拒绝，
不缩小 Min。Max 只按实际候选数形成可选上界。Cancel 与 Min=0 的空 Select 在协议内分开。
原版 deck UI 的 CloseSelection 把 CardsSelected 完成为空列表；取消适配仅在宿主回调边界
返回该原版结果。RequireConfirmation 要求 Select 已确认，Cancel 沿原版关闭行为无需确认。

快照检查当前 RunManager 的同一个 active run、当前来源、候选和已投影可见约束；Deck
入口还检查当前完整 Deck 实例顺序、候选 Owner/成员与升级资格。DeckGeneric 的任意 filter、
sortingOrder 不由 Import 重新运行；正式宿主在其他游戏状态改变时仍须主动失效并重新获取
真实 pending。快照不保存 replay 历史，不能替代宿主的 session 唯一性、revision 单调性、
执行前消费、过期失效、busy 与嵌套生命周期。

`tests/Differential/Run/NativeDecisionTestSession.cs` 是独立测试桥接，执行原版地图、事件与
休息同步器并回答原版选牌回调；父请求消费后，嵌套选牌使用新 revision，异常后旧请求不能
复用。普通事件走 EventSynchronizer，模型内 IsProceed 走原版 EventOption.Chosen；捕获
实际 Chosen Task 传播异常。选牌测试在三个当前支持命令的同步进入 callback 路径上捕获
完整 prefs，作用域在调用返回/异常后恢复；这不是通用异步上下文桥接。测试串行且没有外部
并发游戏动作，不代表 Mod UI 或生产离线宿主已经接入。

奖励与商店入口使用 `NativeDecisionSnapshot<object>` 混合绑定真实 Reward/RewardsSet、CardModel/
CardRewardAlternative、PotionModel/当前奖励、MerchantEntry/MerchantInventory，不新建身份包装对象。

| 入口 | 原版依据与限制 |
| --- | --- |
| `Rewards(set, visible, active, canLeave, token)` | 只读检查原版 `_isGenerated`、同步器当前栈顶集合及其 Id；不调用 Generate/Begin。`visible` 必须实时读取 UI 按钮或实际 callback 中仍显示的集合，`active` 与 `canLeave` 来自当前画面。Gold 导入当前 Amount 为 GoldGain；遗物/药水携带模型，Card 仅容器，不暴露内部候选。强制奖励还有可见行时没有 Leave；已完成集合只允许当前 CombatRoom 终端画面的空列表 Proceed，其余由宿主收尾。 |
| `CardReward(reward, cards, alternatives, active, token)` | 从原版 OnSelect 的实际回调集合读取，校验牌集合就是原 `_cards`。只支持实际标准 Skip 与 REROLL，校验 AfterSelected；不自行调用 Alternative.Generate，不用 default 冒充 Skip。原 while 在重抽后再次回调，每次需要新 Token。 |
| `PotionDiscard(reward, active, token)` | 原版 TooFull 不会自动弹出丢弃屏幕；这是宿主明确进入的整理步骤。只接受当前 pending 满槽且允许领取的 PotionReward；各槽保留 Potion 原实例，已入队药水禁用、死亡玩家拒绝。Skip 仅返回奖励列表，选药水仅丢弃，不自动重试领取。 |
| `Shop(room, player, active, canLeave, token)` | 只读当前 MerchantRoom 的本地库存；售罄普通物品按原 UI 隐藏，已用删牌牌匾保留且 Disabled。买不起、满药水或当前药水 Hook 禁止等保留禁用行；删牌同时核对 Hook 与可移除牌。价格只取 Entry.Cost（已含折扣），GoldLoss 使用正数；0 价为 Complete 空 Costs，负价拒绝。Leave 只关闭库存，不 PopRoom/跳地图。 |

原版 NRewardButton.GetReward 在 SelectLocalReward 返回 false 时重新启用按钮并发 RewardSkipped，
NRewardsScreen 只据此更新脉冲状态，不移除按钮。因此选牌 Skip 与满槽失败后奖励仍可见、可重试；
PotionReward 在 NotAllowed 时反而返回 true 并消费奖励，不能与 TooFull 混为一谈。

原版 CardReward.OnSelect 在 TestMode 的同步 ICardSelector 回调中复用首次生成的 alternatives；
Reroll 后原 UI 的 RefreshOptions 会重建按钮并去掉 REROLL。导入器据 CanReroll=false 剔除回调列表内
过期的 REROLL，其他未知替代效果仍显式拒绝，包含 PaelsWing 的 SACRIFICE。当前不支持 bundle 与
通用 RelicSelect。完整本地化仍未实现。

快照额外捕获集合 Id、父奖励集合原实例与 Id、商店 Entry 对应 model 原实例；同一个 Entry 补货成
同模型新对象也必须失效。每次父/子请求与返回列表由宿主生成新 revision，非法回答与旧 Token 不派发，
失效的子请求必须使原等待链失败并释放 busy。Import 不依赖 TestHost，不生成/执行原版动作。

奖励与商店 Differential 通过原版 RewardsSet.Offer(testSelector) 与 MerchantRoom.Enter 验证。
其 CardReward 测试接口同步返回，所以测试在真实 callback 中提交回答，由原版 while 负责重入；
它只在新 CLI 内撤掉旧 bootstrap 对 alternatives 的整体屏蔽，并严格匹配替换构造器三处 Godot 热键
读取，保留原版 Generate/Hook/OnSelect/AfterSelected。药水丢弃通过 PotionCmd.Discard 的等价模型入口，
未覆盖 NPotionPopup 的 DiscardPotionGameAction 入队/UI 链；删牌成功后只补原 UI 的 SetUsed 职责，
取消不扣费/不标 Used。奖励/商店的正式 OfflineHost 执行及 Mod 整局执行仍未接入；共享 RunPilot 与 Map/Event/Rest/CardSelect 正式执行见第 10 节。

## 8. 验证

`tests/Differential` 的 `--map-step-compare` 从新局出发，用原版推进到每个地图选点；对每个
合法选项先从同一份 `SerializableRun` 快照恢复，再分别执行原版 `EnterMapCoord` 与
`RunMapEntry.Enter`。战斗比较 `Battle`/`Rules`/`RunState`，非战斗比较进点后的 `RunState`
（事件 RNG 单独验证）。这是"保留的对照必须与 `683bc03` 相同"的一部分；结果见
[测试与验证](testing.md)。

## 9. 原版证据与后续桥接边界

第 1、6 节协议已在 Core 落地；第 7 节实现首批真实模型导入与测试执行闭环，第 10 节接入正式 Map/Event/Rest/CardSelect 执行；奖励/商店的正式宿主执行仍待接入。R3a 的 `--run-probe` 与 `RunProbe` JSON
记录只用于收集证据，不是 `RunDecisionImporter`、`RunPilot` 或正式策略接口。

### 已观察的入口

原版 `v0.111.0` 的 `RunManager.EnterAct(0)` 完成正常新局入口与 `AfterActEntered`；事件由
`EventSynchronizer.ChooseLocalOption` 推进，等待实际 `EventOption.Chosen` 返回的 Task。
`AwaitPendingOptionTasks` 自身会吞异常，单独等待它不足以证明选项执行成功。

战斗胜利的 `CombatManager.CombatWon` 通常由 `NCombatUi` 订阅，然后调用
`CombatRoom.OfferRoomEndRewards`。离线探针用同一公开调用替代这段界面回调，奖励生成一次；
`RewardsSet.testSelector` 收到的是已经 `GenerateWithoutOffering` 和 `BeginRewardsSet` 的原集合。
`NativeRewardSession.AttachAlreadyBegun` 因此不能再次 Begin 或重新生成奖励。先古/事件里嵌套的
奖励也经过该原版钩子。休息用 `RestSiteSynchronizer.ChooseLocalOption`，宝箱用
`DoNormalRewards`、`PickRelicLocally`、`DoExtraRewardsIfNeeded`。战后继续首先调用
`ProceedFromTerminalRewardsScreen` 恢复父事件；普通 Boss 后的幕切换仍由 `EnterNextAct` 完成。

### 证据如何进入协议

未来统一入口为 `Decide(可见投影, Request) -> Action`；当前公共头、互斥负载与 typed 事实
见前文。下表追溯原版依据，完整本地化导入和正式宿主执行桥接仍是后续工作。

| 字段或负载及后续内容 | 实际请求/代码证据 | 含义与边界 |
| --- | --- | --- |
| `Selection.Min/Max`、回答中的选项标识列表 | `ICardSelector.GetSelectedCards(options,minSelect,maxSelect)`；`SmithRestSiteOption.OnSelect`；`PrecariousShears.AfterObtained` 要选多张 | 多选是同一个请求的一次回答，不能拆成若干无约束的单选 |
| 独立 `Cancelable`，必要时 `RequireConfirmation` | `CardSelectorPrefs` 明确分开这些字段；Smith 与商店删牌可取消 | `min=0` 不能推导为可取消；测试选择器只收到 min/max，R3b 必须从原版 prefs 入口导入取消事实 |
| 完整 `RunDecisionToken` 与请求内 `OptionId` | 事件每页的 `CurrentOptions`、`TextKey`、`WasChosen`；共享事件同步器用 pageIndex 拒绝旧页投票 | 动作须携带请求版本并重新校验。下标仅在这次请求有效，TextKey 也不能独自充当跨页动作令牌 |
| 事件负载的 `ModelId/PageKey/Options`，可见标题/描述及参数 | Neow、Trial 多页、SelfHelpBook 选牌等公开 `EventModel`/`EventOption` 属性 | 记录当前页、锁定和继续按钮；锁定的可见行不得成为合法动作。公开描述能保留，不能靠猜测描述重建结算 |
| `Costs[]` 与 `VisibleEffects[]`，由具体事件语义适配器填写 | 原版 `ThisOrThat.Plain` 同时付 HpLoss、得到 Gold；`FieldOfManSizedHoles.Resist` 移除 Cards 张并加 Curse；商店价格来自 `MerchantEntry.Cost` | 一个标量不够。这里只是确切代码证据，未声称自然矩阵已触发所有内容；不能把隐藏随机结果、策略评分或收益预测放进这些字段 |
| 卡牌选项的实例标识、模型、升级/附魔等可见差异 | 奖励卡牌与删牌/升级选项可能同模型、多实例；R3a 用原请求索引执行 | 只用 ModelId 无法区别同名牌，执行器必须回到该请求的原实例，拒绝过期回答 |
| `CardBundle`：一个选项包含若干牌 | `CardSelectCmd.FromChooseABundleScreen(player,bundles)`；TestMode 直接取 bundles[0] | 选一组牌与“从所有牌中多选”不同；测试分支也必须记录完整组合，不能因没走 ICardSelector 而漏记 |
| 奖励列表、奖励选牌、满槽药水的不同子请求 | `NativeRewardSession` 的 List/Card/PotionDiscard；选奖励后有后续请求，强制奖励有 `DisallowSkipping` | 子请求有自己的合法选项、数量与取消/跳过语义；容器奖励与拿到的牌不能混为同一次回答 |
| 商店负载、后续删牌负载 | `MerchantInventory` 与 `OnTryPurchaseWrapper`；删牌收到独立 prefs | 一个店铺可连续购买，每次原版推进后重新导入；价格/资格来自原版，不能在协议里复写折扣规则 |
| 遗物选择负载与显式不支持 | `RelicSelectCmd.FromChooseARelicScreen` 直接进入 Godot 选择屏幕，没有 TestMode selector | R3a 显式拒绝该入口；R3b 需 UI 桥接并保留 PlayerChoiceSynchronizer 同步，不能默认拿第一件并宣称原版已支持 |

请求只表达玩家可见事实和当前合法动作。事件消耗、奖励生成、商店补货、页切换、随机结果与
异步选择后的结算全部继续由原版执行。未知种类、第三方内容和没有正确桥接的界面应明确失败。

### 后续导入器与宿主仍需完成

- 公共头、共享池、严格合同与首批只读原实例快照已实现；跨 Mod/离线的 Token 生成、消费和失效仍由正式宿主落实。
- 事件可见说明参数如何保留，以及哪些事件需要机械语义适配才能给策略提供明确的多个代价/效果。
- 推荐模式的确认/取消、嵌套请求展示和原版选择同步桥接；不能将 TestMode 的自动选第一组当正式接口。
- `RunState` 导入在终局清理后的边界限制。R3a 单独记录原版 `OnEnded(isVictory)`、HP 和 IsGameOver；
  `WinRun` 后会杀死玩家，因此必须优先采用明确胜利事实，不能只按最终 HP 分类。

探针命令、计量定义与覆盖限制见 [Differential 使用说明](../tests/Differential/README.md)。
具体自然种子与夹具证据留在 R3a 归档报告，不把运行产物作为协议实现提交。

## 10. 正式执行与异步子选择

`OfflineRunHost` 持有原版快照，`RunExecutor` 通过公开同步器/命令执行 Map/Event/Rest；
原版 CardSelect 异步 callback 使用同一 RunPilot policy 回调，child 也计入预算。
每个请求先重新导入 RunState→RunView.WithRequest，回答完整校验、确认授权、ValidateCurrent 与实例绑定
通过后才消费并 dispatch。失效/取消/Dispose 使等待链结束，迟到回答不得派发，不转换成用户 Cancel。
Session 唯一、Revision 单调；已消费请求不能为重试而复活。

正式 Rewards/Shop 仍未接入。尤其同步 CardReward 的 TestMode selector 不能等待任意异步策略，
本库显式 Unsupported；前面的协议与测试 reward/shop 执行证明不代表正式库已经支持。
详细边界注入、资源生命周期与运行限制见 [OfflineHost](../src/OfflineHost/README.md)。
