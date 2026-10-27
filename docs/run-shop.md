# 商店：货架生成、价格、购买与删牌

[返回文档索引](README.md)

本文记录 R2c-2 的商店内容边界：原版从进入商店点到回到地图的步骤、货架生成的随机数顺序、
价格与 Hook、购买/删牌/离开的状态变化，以及商店流程会调用的 `AbstractModel` Hook 清单。
决策协议见[局外决策协议](run-decisions.md)，状态字段与可见性见[整局状态布局](run-state-layout.md)。

## 1. 原版流程

从 `RunManager.EnterMapCoord` 选到商店点开始：

| # | 原版步骤 | 读取 | 写入 `RunState` | 随机数 |
| ---: | --- | --- | --- | --- |
| 1 | `EnterMapCoord` / `EnterMapPointInternal`：追加已走坐标、`ActFloor`、退出旧房间、掷房间类型、追加历史 | 地图点、旧房间栈 | `Visited.Coords`、`ActFloor`、`History`；商店无模型 | 未知点：`UnknownMapPoint` 一次（R2b-2） |
| 2 | `EnterRoomInternal`：`PushRoom(MerchantRoom)` | 房间 | `Rooms` 追加 Shop | — |
| 3 | `Hook.BeforeRoomEntered` | 监听者 | 见第 6 节 | 可能由 Hook 消耗 |
| 4 | `MerchantRoom.EnterInternal` → `MerchantInventory.CreateForNormalMerchant` | 角色/解锁、玩家遗物袋/共享袋、卡池、药水池、概率当前值 | 货架（5 角色卡 + 2 无色卡 + 3 遗物 + 3 药水 + 删牌服务）、袋 | 见第 2 节 |
| 5 | `Hook.AfterRoomEntered` | 监听者 | 少量遗物效果（金币/治疗/状态/计数） | 金币 Hook 不抽随机数 |
| 6 | `Act.MarkRoomVisited(Shop)` | — | 无（商店不增加访问计数） | — |
| 7 | 玩家购买 | 条目、金币、牌组/遗物/药水 | 金币、牌组/遗物/药水、补货/售罄、`AfterItemPurchased`/`UpdateEntries` | 补货时按条目类型抽取 |
| 8 | 玩家删牌 | 金币、牌组 | 扣金币、删牌、`CardShopRemovalsUsed++`、服务标记已用 | — |
| 9 | 离开：`ExitCurrentRoom` → `MerchantRoom.Exit` | 未买条目 | `RunState` 不变；玩家地图历史写 `CardChoices`/`RelicChoices`/`PotionChoices` 的 `wasPicked=false`（TestMode 跳过） | — |
| 10 | 回到地图：下一次 `EnterMapCoord` | — | `Rooms` 为空，`Capture` 返回 `MapPoint` | — |

`MerchantRoom.Exit` 写的是 `PlayerMapPointHistoryEntry` 的玩家统计，不在 `RunState` 里；R1b 的
历史布局只保存房间/怪物/回合数。R2c-2 不把这几条选择历史加入整局状态，报告第 8 节列为未验证项。

## 2. 货架生成与随机数顺序

`CreateForNormalMerchant` 对单人依次执行：

1. `PlayerRng.Shops.NextInt(5)`：选出打折槽（`_coloredCardTypes.Length`）；
2. **角色卡 5 张**：类型固定 `[Attack, Attack, Skill, Skill, Power]`。每张：
   - 卡池 = 角色卡池的 `GetUnlockedCards`（epoch + 单人/多人过滤），排除 Basic 与货架已有卡；
   - `PlayerOdds.CardRarity.RollWithoutChangingFutureOdds(Shop)`：在 `Rewards` 流抽一次
     `NextFloat`；稀有度按 `ShopRareOdds + offset`、`0.37 + ShopRareOdds + offset` 判定，
     `offset` 是 `PlayerOdds.CardRarity` 的当前值；**不改保底值**；
   - 找不到该稀有度/类型的卡时按原版 `GetNextHighestRarityWithWrapping` 升档；
   - `Shops.NextInt(items.Count)` 选具体卡；
   - `RollForUpgrade(..., -999999999m)`：在 `Rewards` 流抽一次 `NextFloat`，基准概率为负，
     没有模型重写 `ModifyCardRewardUpgradeOdds`，所以永不升级；
   - `CalcCost`：在 `Shops` 流抽一次 `NextFloat(0.95,1.05)`，四舍五入到整数；
   - 若该槽是打折槽：`SetOnSale` 再调一次 `CalcCost`（第二次 `Shops` 抽取），再把整数价除以 2；
3. **无色卡 2 张**：固定稀有度 `[Uncommon, Rare]`。每张按同样流程，但没有稀有度抽取；
   `Shops.NextInt` 选卡、`Rewards.NextFloat` 升级判定、`Shops.NextFloat(0.95,1.05)` 定价；
4. **遗物 3 格**：前两格各在 `Rewards` 流抽一次 `NextFloat` 判稀有度（`<0.5` Common、
   `<0.83` Uncommon、否则 Rare），第三格固定 Shop。每格：
   - 从玩家袋**后端**拉取满足 `IsAllowedInShops` 与黑名单的遗物；目标段为空时按
     Shop→Common→Uncommon→Rare→None 升档；拉到后从玩家袋移除，并同步从共享袋移除；
   - `Shops.NextFloat(0.85,1.15)` 乘以该遗物的 `MerchantCost` 并四舍五入；
5. **药水 3 个**：先对三个槽各在 `Shops` 流抽一次稀有度（`<=0.1` Rare、`<=0.35` Uncommon、
   否则 Common）和一次 `NextInt` 选药水（选中的从候选列表移除，避免同一货架重复）；随后每个
   药水构造时定价：基础价按稀有度（50/75/100），测试模式关闭时再在 `Shops` 流抽一次
   `NextFloat(0.95,1.05)` 并四舍五入；离线 TestMode 打开时不抽、不乘；
6. **删牌服务**：`BaseCost(A) + PriceIncrease(A) × CardShopRemovalsUsed`；A0–5 是 75/25，
   A6+（`Inflation`）是 100/50。

所有价格随后按第 3 节应用 `ModifyMerchantPrice`。货架的固定容量是 5/2/3/3，删牌一项；
`RunState.Shop` 保存每格的基础价 `Cost`、Hook 后价格 `Price`、`Stocked`/`OnSale` 标记与
删牌服务的 `Used`。

## 3. 价格

| 条目 | 基础价 |
| --- | --- |
| 角色卡 Common/Uncommon/Rare | 50/75/150，乘 `Shops.NextFloat(0.95,1.05)` |
| 无色卡 | 同稀有度角色卡价 ×1.15 后四舍五入，再乘 0.95–1.05 |
| 遗物 | `RelicModel.MerchantCost`（Common 175、Uncommon 225、Rare 275、Shop 200；假遗物有自己的 50）乘 0.85–1.15 |
| 药水 | 50/75/100，乘 0.95–1.05（仅 TestMode 关闭） |
| 删牌 | 75/100 + 25/50 × 已删次数 |

这些数值全部从内容表读：卡/药水价由反射调用原版私有 `GetCost` 得到每个稀有度的整数，
`MerchantCost` 从 canonical 遗物属性读，删牌基数/递增从私有静态属性读，倍率从方法体
`ldc.r4` 常量读。`RunShop` 同时保存基础价与最终价。

`ModifyMerchantPrice` 在监听者顺序（牌组卡/附魔、遗物、药水）上依次应用，价格用 `decimal`
连乘、最后截断为整数：

- `MembershipCard`：`price × Discount/100`（50%）；
- `TheCourier`：`price × (1 - Discount/100)`（80%）。

两者同时持有时按遗物栏顺序连乘。改价 Hook 只在商店房间内生效；R2c-2 在生成货架后、每次购买
/删牌后重算 `Price`。

## 4. 购买

`MerchantEntry.OnTryPurchaseWrapper` 的顺序：

1. 未售罄、金币足够；
2. 条目自己的 `OnTryPurchase`：
   - **卡牌**：`CardPileCmd.Add(card, Deck)`（`ShouldAddToDeck`、蛋的
     `TryModifyCardBeingAddedToDeck`、`AfterCardChangedPiles`），成功后扣金币；
   - **遗物**：扣金币 → `RelicCmd.Obtain`（入遗物栏、非可叠加时从两个袋移除、
     `FloorAddedToDeck=TotalFloor`、`AfterObtained`）；
   - **药水**：`PotionCmd.TryToProcure`（`ShouldProcurePotion`、找第一个空槽、
     `AfterPotionProcured`），成功后扣金币；满槽或 `Sozu` 时原版购买失败、不扣金币；Blitz 不把这种必失败项列为合法选项；
   - **删牌**：进入选卡界面（第 5 节），此时不扣金币；
3. 有 `ShouldRefillMerchantEntry`（`TheCourier`）时补货，否则售罄；
4. `Hook.AfterItemPurchased(goldSpent)`（`MawBank` 把 `HasItemBeenBought` 置真并禁用）；
5. `MerchantInventory.UpdateEntries`：对每个在售卡重跑 `ModifyMerchantCardCreationResults`
   （蛋升级对应类型）；
6. 重算所有价格。

补货细节：

- 卡牌：`IsOnSale=false` 后 `Populate`；购买的那张仍在 `CreationResult` 里，所以**排除刚买的卡**；
  重新抽稀有度、卡、升级判定和一次价格；
- 遗物：把当前在售遗物作为黑名单，`Rewards` 抽新稀有度，再从袋后端拉取并抽价格；
- 药水：原版 `FillSlot(Array.Empty<PotionModel>())` 忽略黑名单，可能补出与在售重复的药水；
  R2c-2 按原版不排除；
- 删牌服务：原版入口不调用补货逻辑。

## 5. 删牌

`CardSelectCmd.FromDeckForRemoval`：

- 候选 = 牌组中 `IsRemovable`（非 Eternal）的卡；`RunCard.Flags.Removable` 在导入/生成时写入；
- 排序 = 诅咒 `-999999999` 在前，其余按牌组顺序；R2c-2 的请求同样先诅咒后普通卡；
- `RequireManualConfirmation=true`，所以总是打开选卡；`Cancelable=true`，可以取消；
- 选中后：扣金币 → `CardPileCmd.RemoveFromDeck`（`BeforeCardRemoved`、从牌组移除）→
  `CardShopRemovalsUsed++`；UI 收到成功后调 `SetUsed`；
- 取消：不扣金币、不增加计数、服务仍可购买；
- 重复卡：原版牌组移除按“第一张同 id/升级/附魔的卡”删除，R2c-2 同样处理。

选卡请求是通用的“从牌组选卡”表示：`Kind=CardSelect`，选项是满足参数的卡 + 取消；
`RemoveCard.Option` 是请求下标。R2e 的休息处升级与事件变牌/删牌复用同一结构，只换过滤条件。

## 6. Hook 清单

R2c-1 的 `ContentRewardHookMask` 继续覆盖与奖励共享的 Hook；R2c-2 新增
`ContentShopHookMask`（每位 Overridden/Implemented 三态）覆盖商店独有 Hook。导出器用反射比对
canonical 类型；名单内有但未重写、或重写但不在名单内都会记录/拒绝。

| Hook | 范围 | 已实现 |
| --- | --- | --- |
| `BeforeRoomEntered`（商店位 `BeforeShopRoomEntered`） | 商店独有 | 无；`Dowsing` 以 `shop-hook-listener` 拒绝 |
| `AfterRoomEntered`（商店位 `AfterShopRoomEntered`） | 商店独有 | 32 个遗物：MawBank（金币）、MealTicket/Planisphere（治疗）、LavaLamp（重置）、Pantograph/RegalPillow（状态）、WingedBoots（次数）；其余重写者在商店无状态效果；`LordsParasol` 拒绝 |
| `ModifyMerchantPrice` | 商店独有 | MembershipCard、TheCourier |
| `ModifyMerchantCardPool` / `ModifyMerchantCardRarity` | 商店独有 | 无重写者；`CharacterCards` 等 modifier 不在标准跑局 |
| `ModifyMerchantCardCreationResults` | 商店独有 | 三种蛋；`FresnelLens` 拒绝 |
| `ShouldRefillMerchantEntry` | 商店独有 | TheCourier |
| `AfterItemPurchased` | 商店独有 | MawBank |
| `BeforeCardRemoved` | 商店独有 | 无；`SpoilsMap` 拒绝 |
| `ModifyCardRewardUpgradeOdds` | 共享（奖励掩码） | 无重写者；商店升级基准为负 |
| `ShouldProcurePotion` / `AfterPotionProcured` | 共享 | Sozu / BeltBuckle |
| `ShouldAddToDeck` / `TryModifyCardBeingAddedToDeck` / `AfterCardChangedPiles` | 共享 | 无 / 三种蛋 / BookOfFiveRings、DarkstonePeriapt、LuckyFysh、SovereignBlade |
| `ModifyGoldGained` / `AfterModifyingGoldGained` / `AfterGoldGained` | 共享 | BowlerHat、Ectoplasm / DragonFruit |
| `AfterCurrentHpChanged` | 共享 | MeatOnTheBone、RedSkull（战斗外无状态效果） |

**按房间拆位规则**：`BeforeRoomEntered`/`AfterRoomEntered` 是原版一个通用方法，但 Core 只对
“商店房间”这一支有实现，所以掩码位按房间拆成 `BeforeShopRoomEntered`/`AfterShopRoomEntered`。
以后新增休息处、宝箱、事件等房间时，必须各加一对房间位，并在 Core 里为该房间单独判定/实现；
不能复用商店位，也不能因为某个监听者重写了通用方法就默认其他房间已实现。开战走
`CombatStart.AfterRoomEntered` 的独立战斗规则，不使用这组位。

`RequireSupportedListeners` 在进店前、每次购买/删牌前扫描牌组卡、附魔、遗物、药水；任一
`Unsupported` 位就抛 `ContentUnavailableException("shop-hook-listener")`，`Step` 返回
`Rejected`，状态不变。Badge/Achievement 不在 `RunState` 监听者里；标准跑局不导入 modifier。

**不在本批边界内的 Hook**：`RunState` 只保存货架，不保存 `MerchantRoom` 的 UI/商店交互状态；
商店位只覆盖已实现的 32 个 `AfterRoomEntered` 分支；`LordsParasol` 的自动购买、
FresnelLens 的附魔与 SpoilsMap 的地图任务显式拒绝，复杂效果属于 R2d。`WingedBoots` 的
`AfterRoomEntered` 分支本身没有在对照里命中：它在 R2b-2 的 `free-travel-hook` 边界就拒绝。

## 7. 保留的字面量

| 字面量 | 位置 | 来源/理由 |
| --- | --- | --- |
| 5 角色卡 / 2 无色卡 / 3 遗物 / 3 药水 / 1 删牌 | `RunLimits` | 原版固定数组/调用参数，容量上限 |
| `-999999999m`（不写进 Core） | 原版 `CardFactory.CreateForMerchant` | 升级基准为负且无 Hook 重写，Core 只消耗一次 `Rewards` 浮点、不升级 |
| 打折除数 | 内容表 `ShopRules.SaleDivisor` | 从 `CalcCost` IL 的整数常量读 |
| 价格倍率 0.95/1.05、0.85/1.15 | 内容表 `ShopRules` | 从 `CalcCost` IL 的 `ldc.r4` 读 |
| 卡/药水基础价 | 内容表 `CardCosts`/`ColorlessCardCosts`/`PotionCosts` | 反射调用原版私有 `GetCost` |
| 删牌 75/100、25/50 | 内容表 `RemovalBaseCost*`/`RemovalPriceIncrease*` | 反射读私有/公有静态属性 |
| 商店稀有度 0.09/0.045、0.37 | 内容表 `ShopRules` | 反射 `CardRarityOdds.GetBaseOdds(Shop, …)` |
| 遗物/药水稀有度阈值 | `ContentRewardRules` | R2c-1 已有的内容表字段 |
| 百分比换算 100 | `PriceWithHooks` | `Discount` 是百分数；单位换算，不是价格/概率/池子数值 |

## 8. 验证

`--shop-compare` 从自然新局走到商店点，或用 D1 牌组装直接进商店点；两边把金币设为同一值
（自然 500、D1 2000），逐字段比较货架、价格、合法选项，并在每个合法购买/删牌/取消/离开动作
后比较 `RunState` 与下一个请求。完整命令与结果见[测试与验证](testing.md) 6.18。

离线对照默认 `TestMode.IsOn=true`，因此原版 `MerchantPotionEntry.CalcCost` 不抽药水价的
`Shops.NextFloat`。`--shop-kind natural-live-prices` / `d1-live-prices` 用
`ContentTableExporter.Export(forcePotionPriceVaries: true)`，并在原生侧用 Harmony
prefix/postfix 只在 `MerchantPotionEntry.CalcCost` 内临时关闭 TestMode，覆盖货架生成和补货的
真实游戏分支；不整体关闭 TestMode，避免 `RunManager.ClearScreens`、`CardPileCmd.Add` 等
Godot/UI 代码。自然 60 局与 D1 103 局共命中 4,718 次 `CalcCost`，0 diff。

`natural-courier` 在自然开局后注入 `TheCourier`，配合贪心买空序列覆盖连续补货；非 Courier
商店买空后停止，Courier 商店在 16 次购买后停止。两边金币 5000，每一步都做状态/请求比较。
