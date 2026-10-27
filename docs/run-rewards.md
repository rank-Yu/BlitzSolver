# 战后奖励：生成、领取与回到地图

[返回文档索引](README.md)

本文记录 R2c-1 的战后奖励边界：写回之后如何按原版顺序生成奖励、每条随机数流消耗几次、
奖励界面有哪些决策与动作、领取各奖励的状态效果，以及离开奖励界面回到地图的条件。奖励的
状态负载在 [整局状态布局](run-state-layout.md)；决策协议见[局外决策协议](run-decisions.md)；
内容表新增数据见[内容表与 RulesBuilder](content-table.md)；对照方法与数字见
[测试与验证](testing.md) 与 `.local/tool-tasks/blitz/R2c1-report.md`。

## 1. 入口与状态

战斗结束后先调用 `RunCombatWriteback.Apply`。它在原版 `CombatRoom.OfferRoomEndRewards`
之前停下，并完成两件与奖励有关的事：

- 写入当前 `RunRoom.GoldProportion`（普通遭遇是 `1 - 逃跑敌人数 / 已知敌人数`；
  GremlinMerc 的私有金币状态不在本批范围，生成时显式拒绝）；
- 把战斗中产生的额外奖励放进 `RunState.Rewards`，`Generated = 0`：偷牌归还的
  `SpecialCardReward`、`RoyaltiesPower` 的金币、`TheHunt` 的额外卡牌奖励、
  `ForbiddenGrimoire` 的删牌奖励。`HeistPower` 金币需要在敌人死亡时取回，终局 `Battle` 没有
  保留该信息；GremlinMerc 这类依赖私有金币比例的遭遇在生成时显式拒绝。

之后调用：

```csharp
RunRewardGenerator.GenerateForCombat(ref state, contentTable);
```

它对应原版 `RewardsCmd.GenerateForRoomEnd` 的 `new RewardsSet(player).WithRewardsFromRoom(room)`
加 `GenerateWithoutOffering()`。`RunState.Rewards.Generated` 置 1，`Stage` 进入 `List`；
`ShouldGiveRewards == false` 的遭遇不打开奖励界面，`Stage` 保持 `None`。

奖励列表是 `RunState.Rewards.Items`，容量 16；卡牌选项放在同一结构的 `Choices`，容量 16。
两者都参与 `RunCodec`（格式版本 2）。奖励界面打开时 `RunDecisionRequest.Capture` 先看
`Rewards.Stage`，不会把已完成战斗房误判成地图选点。

## 2. 原版生成顺序与随机数

`WithRewardsFromRoom` **只建列表、不抽取**；`GenerateWithoutOffering` 再按列表顺序
`Populate`。因此金币的 `NextInt` 发生在药水掉落判定之后，顺序不能交换。

| # | 步骤 | 写入 | 随机数 |
| ---: | --- | --- | --- |
| 1 | 最后一幕 Boss：空奖励集合 | — | — |
| 2 | 尝试首次跑局 Ironclad 教学奖励 | 金币/药水/遗物/固定卡牌 | 仅未固定的金币与随机遗物 |
| 3 | 否则按房间类型加默认奖励 | Monster：金币、药水 Roll、卡牌；Elite：金币、药水 Roll、卡牌、遗物；Boss：金币、药水 Roll、卡牌 | 药水 Roll 在此消耗一次 `Rewards.NextFloat` |
| 4 | 追加战斗 `ExtraRewards` | 偷牌、TheHunt、ForbiddenGrimoire 等 | — |
| 5 | 按列表顺序 `Populate` | 金币金额、药水 id、卡牌三/四张、遗物 id | 见下 |
| 6 | `Hook.ModifyRewards` / `TryModifyRewardsLate` | 追加奖励；Driftwood 设置 reroll | 追加的卡牌/遗物在下一步 Populate |
| 7 | 新追加奖励 `Populate` | — | 与第 5 步同一顺序 |
| 8 | `Hook.AfterModifyingRewards` | Wongos 的 `GaveRelic` 等 | — |
| 9 | 按 `RewardsSetIndex` 稳定排序 | 金币 1、药水 2、遗物 3、`SpecialCard` 4、卡牌 5、删牌 7 | — |
| 10 | `Hook.BeforeCombatRewardOffered` | LastingCandy 的 `CombatRewardsSeen` | — |

各类 `Populate` 的抽取：

| 奖励 | 抽取顺序 | 次数 |
| --- | --- | --- |
| 金币（区间） | `Rewards.NextInt(min, max + 1)` | 1 |
| 金币（固定金额） | 无 | 0 |
| 药水 | `Rewards.NextFloat` 定稀有度，`NextInt` 从该稀有度候选中取 | 2 |
| 卡牌 | 每张：`NextFloat` 定稀有度，`NextInt` 从稀有度候选取卡，`NextFloat` 定升级 | 3/张 |
| 遗物 | `NextFloat` 定稀有度；从玩家遗物袋前端取 | 1 |

稀有度保底写入 `RunState.PlayerOdds.CardRarity`；药水掉落保底写入
`RunState.PlayerOdds.PotionReward`。遗物抽取会先从玩家袋中删掉 `IsAllowed` 为假的遗物；
该删除本身不消耗随机数。普通遭遇金币用 `GoldProportion` 缩放，Elite/Boss 不缩放。

### 2.1 卡牌奖励

`CardCreationOptions.ForRoom` 决定卡池（角色卡池）、来源 `Encounter` 与稀有度来源：
Monster→Regular、Elite→Elite、Boss→Boss。生成每张牌时：

1. `ModifyCardRewardCreationOptions`（DingyRug + 无色池、PrismaticGem + 已解锁角色池）；
2. 按纪元解锁、单人限制、已选黑名单过滤；按卡池实际存在的稀有度做 `GetNextAllowedRarity`；
3. `Roll`（Encounter 来源）更新稀有度保底；Boss 的 offset 固定为 0，仍消耗一次浮点；
4. 升级概率 = `actIndex × (0.25 或 Scarcity 的 0.125)`，仍然先抽一次 `NextFloat`；
5. 全部三张后 `TryModifyCardRewardOptions`：LastingCandy 追加一张不重复的 Power；
   Late pass 的蛋与 LavaLamp 升级；不支持的附魔类 Hook 显式拒绝。

取卡加入牌组时设置 `FloorAddedToDeck = History.Count`，并执行
`ModifyCardBeingAddedToDeck`（蛋）与 `AfterCardChangedPiles`（BookOfFiveRings、LuckyFysh、
DarkstonePeriapt；BingBong 显式拒绝）。卡牌属性包的默认值来自内容表
`ContentCard.DefaultProperties`，例如 Genetic Algorithm 的成长计数。

### 2.2 药水与遗物

- 药水池是角色池 + 共享池，按解锁过滤；稀有度阈值与概率成长来自内容表。
- 药水满槽时先进入 `PotionDiscard`：列出每个已占槽位与“放弃”；选择槽位会先丢该瓶再拿
  新药水。Sozu 使 `ShouldProcurePotion` 为假，药水奖励直接视为已取但不入槽。
- 遗物奖励从 `RunState.PlayerBag` 前端拉取，随后从共享袋删除；袋耗尽时使用 Circlet。
- 遗物拿到时先检查 `ContentRelicRewardFacts.Obtain`。已实现：Strawberry/Mango/Pear 等加
  最大生命、LeesWaffle 加最大生命并回满、OldCoin/GoldenPearl 加金币、PotionBelt 加药水槽、
  WarPaint/Whetstone 用 Niche 稳定洗牌后升级。其余有 `AfterObtained` 的遗物返回
  `relic-obtain-effect`，不改状态。
- 新遗物的默认私有属性包来自 `ContentRelicRewardFacts.DefaultProperties`，例如 LizardTail 的
  `WasUsed=false`。

### 2.3 金币

`GainGold` 按遗物顺序应用 `BowlerHat` 的 `GoldIncrease` 与 `Ectoplasm` 的归零，再按
`DragonFruit` 的 `MaxHp` 加最大生命。金额为 0 时不触发 `AfterGoldGained`。

## 3. 奖励决策

奖励界面共三个阶段，全部用 `RunDecisionRequest` 表示：

| 阶段 | `RunDecision` | 选项 | 动作 |
| --- | --- | --- | --- |
| 列表 | `CombatReward` | 每条剩余奖励 `Kind=Reward`（`RewardType`/`Model`/`Amount`）；最后 `Kind=Skip` 表示离开 | `TakeReward`（`Option` 为请求下标）、`LeaveRewards` |
| 卡牌 | `CardReward` | 每张 `Kind=Card`（id、升级、附魔）；`Kind=Skip`；Driftwood 时 `Kind=Reroll` | `ChooseCard`、`SkipReward`、`RerollCardReward` |
| 药水满槽 | `PotionDiscard` | 每个已占槽 `Kind=Potion`；`Kind=Skip` | `ChoosePotion`、`SkipReward` |

`TakeReward` 在药水满槽时切到 `PotionDiscard`，在卡牌奖励上切到 `CardReward`；其余奖励
立即结算并从列表删除。取完最后一条后 `Stage` 仍是 `List`、选项只剩“离开”，对应原版终态
奖励界面的 proceed；`LeaveRewards` 清空列表并回到 `Stage=None`。此时若地图有合法点，
`Capture` 返回 `MapPoint`，房间栈里的战斗房仍是 `IsPreFinished=1`，与原版一致。

`LeaveRewards` 对应 `SkipLocalRewardsSet`：未取奖励的 `OnSkipped` 只写地图点历史，不改
`RunState`。`SkipReward` 只用于卡牌/药水页，等价于原版的单项跳过。

## 4. 拒绝码

| 码 | 场景 |
| --- | --- |
| `rewards-not-generated` | 写回后尚未调用奖励生成入口 |
| `reward-open` | 奖励界面未离开却尝试地图选点 |
| `not-reward-decision` | 当前阶段不接受该奖励动作 |
| `reward-index` / `card-option` / `potion-slot` | 选项下标越界或类型不符 |
| `reward-hook-listener` | 当前监听者重写了未实现的奖励 Hook；生成/领取前扫描，状态不变 |
| `relic-allowed-hook` | 遗物重写了 `IsAllowed`，但导出映射没有它 |
| `reward-hook-relic` | 保留给单件遗物获得效果/卡牌选项 Hook 的显式拒绝 |
| `alternative-hidden` | OfflineHost 的 alternatives 前缀发现监听者重写了 `TryModifyCardRewardAlternatives`，拒绝隐藏 |
| `relic-obtain-effect` | 未实现的遗物 `AfterObtained` |
| `card-reward-alternative` | 非法 reroll |
| `gold-proportion-encounter` | GremlinMerc 这类私有金币比例 |
| `capacity` | 奖励或卡牌选项容量溢出 |

## 5. 奖励流程调用的 Hook 与监听者

奖励层不靠模型 id 白名单猜测“有没有效果”。导出器对每个卡牌、附魔、遗物、药水模型用反射检查
<code>AbstractModel</code> 的下列虚方法；内容表为每类模型保存 <code>Overridden</code> 与
<code>Implemented</code> 两个位集。Core 在生成或领取奖励前扫描
<code>RunState.IterateHookListeners(null)</code> 对应的监听者（牌组卡、卡上附魔、未熔遗物、持有
药水）；任一 <code>Unsupported = Overridden &amp; ~Implemented</code> 非零就抛
<code>reward-hook-listener</code>，状态不变。名单里有、实际没重写的模型在导出时直接抛
<code>InvalidDataException</code>，防止名单过期。

| Hook | 在哪一步调用 |
| --- | --- |
| `ShouldForcePotionReward` | 第 2 步药水掉落判定前；由 WhiteBeastStatue 实现 |
| `ModifyCardRewardCreationOptions` / `ModifyCardRewardCreationOptionsLate` | 第 5 步每张奖励卡建池前；DingyRug、PrismaticGem |
| `TryModifyRewards` / `TryModifyRewardsLate` | 第 6 步奖励列表修改；BlackStar、LavaRock、PrayerWheel、WhiteStar、Amethyst、Wongos、Driftwood |
| `AfterModifyingRewards` | 第 6 步末尾；Wongos 的 `GaveRelic` |
| `BeforeCombatRewardOffered` | 第 8 步排序后；LastingCandy 的 `CombatRewardsSeen`/显示值 |
| `ModifyCardRewardUpgradeOdds` | 第 5 步每张卡升级判定前；0.111.0 无重写 |
| `TryModifyCardRewardOptions` / `TryModifyCardRewardOptionsLate` | 第 5 步三张牌都生成后；LastingCandy、三种蛋、LavaLamp |
| `AfterModifyingCardRewardOptions` | 同上，修改 Hook 之后；0.111.0 只有 SilverCrucible/SilkenTress，未实现 |
| `TryModifyCardRewardAlternatives` | 卡牌页打开时；PaelsWing，未实现；OfflineHost 的 alternatives 前缀会显式抛 `alternative-hidden` |
| `ShouldAllowSelectingMoreCardRewards` | 卡牌页选择循环；0.111.0 无重写 |
| `AfterRewardTaken` | 第 8/9 步任一奖励领取完成后 |
| `ModifyGoldGained` / `AfterModifyingGoldGained` | 第 8 步领取金币、以及卡牌/遗物效果加金币时；BowlerHat、Ectoplasm |
| `AfterGoldGained` | 金币加完后；DragonFruit |
| `ShouldProcurePotion` | 第 9 步拿药水前；Sozu |
| `AfterPotionProcured` | 药水进槽后；BeltBuckle（仅状态刷新，奖励期无状态差） |
| `AfterPotionDiscarded` | 药水满槽丢瓶时；BeltBuckle |
| `ShouldAddToDeck` | 第 9 步卡牌加入牌组前；0.111.0 无重写 |
| `TryModifyCardBeingAddedToDeck` / `TryModifyCardBeingAddedToDeckLate` | 卡牌加入牌组时；三种蛋；FresnelLens 未实现 |
| `AfterCardChangedPiles` / `AfterCardChangedPilesLate` | 卡牌进入牌组/牌堆后；BookOfFiveRings、DarkstonePeriapt、LuckyFysh、SovereignBlade；BingBong 未实现 |
| `AfterCurrentHpChanged` | 遗物获得效果加最大生命/治疗时；MeatOnTheBone、RedSkull（奖励期无状态差） |
| `ShouldProceedToNextMapPoint` | 奖励页 proceed/回地图时；0.111.0 无重写 |

`RelicModel.IsAllowed` 单独按同样三态导出：重写且不在映射中为 `Unsupported`，拉遗物时显式抛
`relic-allowed-hook`；映射中有但实际没重写则导出失败。0.111.0 的 20 个重写全部有映射，没有
`Unsupported`。

监听者覆盖（`CONTENT-REWARD-HOOK`）：卡牌 558 个中 1 个重写（SovereignBlade 的
`AfterCardChangedPiles`，已实现）；附魔 22 个、药水 65 个均无奖励 Hook；遗物 32 个重写，
其中 25 个已实现、7 个 Unsupported（BingBong、FresnelLens、Glitter、PaelsWing、SilkenTress、
SilverCrucible、WingCharm）。这些数由 `--content-table-report` 的
`CONTENT-REWARD-HOOK`/`CONTENT-HOOK` 行输出，可重复核对。

## 6. 与原版对照

`--reward-compare` 在 R2a-3 的整场执行器终点做三组对照：写回后的 `RunState`、生成的奖励
列表、以及从同一 `SerializableRun` 快照恢复后对每个合法选项的单步结果；随后用随机合法序列
走到离开奖励界面，并比较回到地图后的请求。原版侧使用 `RewardsCmd.GenerateForRoomEnd`、
`RewardsSetSynchronizer`、测试 `ICardSelector` 与 `PotionCmd.Discard`，不读界面。
`--reward-kind run` 把 R2b-2 地图选点、战斗、写回、奖励串起来，遇到商店/事件等未实现房间
停止。结果与拒绝分布见 R2c-1 报告。

已知限制：

- `RelicModel.DisplayAmount` 是计算 getter，没有 setter；快照记录旧遗物的原值，恢复后逐件
  比较。对 `ORNAMENTAL_FAN`、`LETTER_OPENER`、`KUSARIGAMA`、`SHURIKEN`、`RAINBOW_RING`、
  `BEATING_REMNANT`、`MUSIC_BOX` 这些由**每回合临时计数**派生的显示值，恢复快照会丢计数；
  **只有同时满足“在 transient 名单内”和“实际不等”**才在 native dump 投影 pre-generation
  值。名单外遗物若不等，打印 `REWARD-DISPLAY-MISMATCH` 并记 diff，不覆盖。`RelicModel.Status`
  不进入存档，恢复后由测试侧写回 pre-snapshot 值。
- 偷牌归还的 `SpecialCardReward` 代码路径已实现，但本批自然/D1 语料的战斗策略没有触发
  “盗贼先偷后死”，因此没有原版对照证据；Heist 金币与 GremlinMerc 金币比例显式拒绝。
- 依赖界面选牌的遗物获得效果（GnarledHammer、Kifuda、PunchDagger、RoyalStamp、DollysMirror、
  Orrery、Cauldron 等）与删牌奖励属于 R2d。
