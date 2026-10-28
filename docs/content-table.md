# 内容表与 RulesBuilder

[返回文档索引](README.md)

本文说明 R2a-1 的进程内内容表、导出器与 Core `RulesBuilder`：它们把 `RulesCompiler` 从原版活对象逐场读取的内容，改成一次导出、之后由整局状态与战斗根重建。`Rules` 的编码与字段不变，验收标准是同一根上两条路径经 `RulesCodec` 编码后逐字节相同。

## 1. 为什么要拆成“内容 + 根状态”

`RulesCompiler` 在开战时直接读原生 `CombatState` 与模型，输入混合了三类东西：

- 只由原版规范模型决定的内容；
- 由当前整局状态决定的池、解锁与遗物顺序；
- 由当前战斗根决定的牌堆、Power、敌人、药水、历史标记等。

整局模拟器在 Core 里从 `RunState` 建战斗时没有对应的原版战斗对象，因此需要把第一类固化成内容表，第二、三类从 `RunState` 与 `Battle` 推出。原版仍然是唯一事实来源：内容表在进程启动时从当前加载的这份游戏导出，不写盘、不缓存、不检查版本。

## 2. 内容表

类型：`src/Core/Simulation/Run/ContentTable.cs` 的 `ContentTable`。
导出器：`src/Import/Run/ContentTableExporter.cs` 的 `ContentTableExporter.Export()`。
表只由 `ModelDb` 的规范模型与 Blitz `Registry`/`RunCatalog` 生成，只在内存里存在一次；`RulesBuilder` 不读任何原版对象。

| 区块 | 内容 | 覆盖数量（0.111.0） |
| --- | --- | --- |
| `Cards` | 每个 `Registry` 卡牌 id 的 0..`MaxUpgradeLevel` 全部升级等级；`CardStats` 与 `Rules` 完全同型 | 558 张 / 1078 个等级 |
| `PowerVars` | 每个有变量名的 Power 的 `DynamicVars[name].BaseValue` | 12 个有变量（`Registry.PowerCount` 242） |
| `PowerDeathHistoryRisk` | `MirrorRiskTables` 的每 Power 死亡镜像风险 | 242 项 |
| `Potions` | 每个 `Registry` 药水的目标/稀有度/战略成本/变量与变量属性 | 65 个 |
| `PotionGeneratableInCombat` | `PotionModel.CanBeGeneratedInCombat` | 65 项 |
| `RelicVars` | 每个有变量名的遗物变量（`Rules` 使用） | 100 个有变量（`Registry.RelicCount` 297） |
| `RelicStartVars` | 每个遗物全部 `DynamicVars` 的 `name -> BaseValue`（建局使用，不进 `Rules`） | 297 个遗物 |
| `Enchantments` | 每个 `Registry` 附魔的 native id 与 `HooksExt`（规则编译器只读 shuffle/before-flush 两位） | 22 个 |
| `SpawnMonsters` | 每个怪物 id 的召唤变体 `MonsterRules`，按进阶 0..10 各一份 | 107 个可召唤变体 / 108 个怪物 |
| `Encounters` | 每个 `RunCatalog` 遭遇的槽位、按（怪物，槽位，进阶）组织的怪物规则，以及从原版 `AfterAddedToRoom` 捕获的开战记录 | 89 个遭遇 |
| `Characters` | 每个角色的卡池（`ContentPoolEntry`：id、类型、稀有度、战斗生成、多人限制、所需 epoch）、角色药水池、角色解锁 epoch | 5 个角色 |
| `CharacterOrder` | 原生 `ModelDb.AllCharacters` 顺序，用于 Splash 的“其他角色攻击”拼接 | 5 |
| `ColorlessPool`/`StatusPool`/`CursePool` | 共享卡池的候选顺序与所需 epoch | 合计 549 个卡池候选 |
| `SharedPotionPool` | 共享药水池顺序 | 65 个药水中属于共享池的部分 |

卡池条目还带 `EnergyCost`、`CostsX`、`Ethereal`，因为原版 `CardPools` 的 `zero-cost` 与 `ethereal` 过滤发生在上锁之后、战斗过滤之前。导出器用“把所有 epoch 去掉一个再求一次解锁集合”的办法得到每个条目的 `RequiredEpochs`，由 `RunState.Unlock` 的 epoch 位图在运行时过滤。

怪物规则按进阶 0..10 各导出一份。原版 `MinInitialHp`、攻击意图和部分静态整数通过 `AscensionHelper` 读取当前跑局进阶；导出器在无跑局状态下临时给 `RunManager` 一个只用于“非空”判断的占位状态与对应 `AscensionManager`，导出完立即恢复。这是导出器唯一的临时全局上下文，不写进度、不碰存档；若导出时已有进行中的跑局会显式失败。

原版有但 Blitz 不支持的内容不会默认成功：`ContentTable` 为卡牌、Power、药水、遗物、附魔、
怪物分别保留 `CardUnsupported`/`PowerUnsupported`/`PotionUnsupported`/`RelicUnsupported`/
`EnchantmentUnsupported`/`MonsterUnsupported`，遭遇保留 `UnsupportedReason`/`UnsupportedDetail`；
每项带稳定原因码与细节。`RulesBuilder` 用与 `RulesCompiler` 相同的稳定码（`unsupported-card`、
`unsupported-power`、`unsupported-relic`、`unsupported-potion`、`unsupported-monster`、
`unsupported-enchantment`、`unsupported-encounter`、`unsupported-closure`、`capacity`）抛出
`ContentUnavailableException`。`--content-table-report` 会把所有 `CONTENT-UNSUPPORTED` 条目按
类型列出。当前 0.111.0 只有 `EXOSKELETON` 没有独立召唤变体（它的 `INIT_MOVE` 条件分支在
无槽位 scratch 中无法解析），遭遇变体仍然完整；另有 37 个原版卡池候选卡没有 Blitz 定义。

导出耗时与保留内存（`--content-table-report`，Release，单次）：约 **1.5 秒**、**19.5 MB**（`GC.GetTotalMemory(true)` 前后差；R2c-1 新增每个卡牌等级与遗物的默认属性包、奖励事实与教学表，保留量因此上升）。只测量，不做缓存或优化。

## 3. `RulesBuilder`

`src/Core/Simulation/Run/RulesBuilder.cs`：

```csharp
Rules rules = RulesBuilder.Build(contentTable, runState, battle, encounterId, rootFacts);
```

- `contentTable`：进程内唯一内容表；
- `runState`：整局状态；读取角色、进阶、`MaxEnergy`、基础球槽、解锁位图、遗物顺序与属性包、当前地图坐标；
- `battle`：战斗根；读取牌堆、Power、遗物、药水、敌人、AI 寄存器与本回合掉血标记；
- `encounterId`：当前遭遇，`ContentTable.NoEncounter` 表示没有遭遇；
- `rootFacts`：可选，见第 5 节。

`RulesBuilder` 按 `RulesCompiler` 的同一顺序生成 30 个构造参数：

1. 先用 `RulesDraft`（原 `RulesBuilder` 收集器改名，避免与新的构建器重名）建立各表；
2. 卡牌按 `Battle.Cards` 的原生堆顺序加入，再沿 `Registry` 的生成卡/卡池/Power/药水闭包补齐；
3. 卡池按 `RunState` 的角色、解锁 epoch、单人约束重建，药水池同理；
4. 已知敌人先用遭遇 setup 的变体，找不到时用召唤变体；再叠加 `Battle` 的 AI 寄存器、根 Power 数值，以及 `rootFacts` 的初始节点/条件选择/攻击值；
5. 最后 `RulesDraft.Build()` 返回 `Rules`。

卡牌 `SHIV`/`SOVEREIGN_BLADE` 的目标类型在原版里由玩家是否持有 `FAN_OF_KNIVES_POWER`/`SEEKING_EDGE_POWER` 决定；这两个 Power 在 `Battle` 里，`RulesBuilder` 据此覆盖目标。`FoulPotion` 的目标在战斗内是 `AllEnemies`、战斗外是 `TargetedNoCreature`；规则编译器只在战斗内运行，导出器因此固定存战斗内值。

## 4. 30 项来源分类

| # | 参数 | 类 | 来源/处理 |
| ---: | --- | --- | --- |
| 1 | `Cards` | 1/3 | 内容表提供各升级等级；根上实际用到哪些由 `Battle.Cards` 与生成闭包决定；`SHIV`/`SOVEREIGN_BLADE` 目标由 `Battle.Powers` 覆盖 |
| 2 | `PowerVars` | 1 | 内容表全部 Power 变量 |
| 3 | `Potions` | 1 | 内容表全部药水；`FoulPotion` 存战斗内目标 |
| 4 | `RelicVars` | 1 | 内容表全部遗物变量 |
| 5 | `Monsters` | 1/3/4 | 内容表的召唤/遭遇变体；根上 AI 寄存器来自 `Battle`，根 Power 数值来自 `Battle`（前缀根由 `rootFacts` 提供请求根基线）；初始节点、条件选择、攻击基值见第 5 节 |
| 6 | `SpawnRules` | 1/3 | 生成闭包由 `Battle` 敌人与 `MonsterDef.SpawnedMonsterIds` 做 BFS；每个类型的规则是内容表召唤变体 |
| 7 | `CardPools` | 2/1 | 角色、解锁 epoch、单人约束来自 `RunState`；候选顺序与过滤事实来自内容表 |
| 8 | `PotionPool` | 2/1 | 角色药水池、解锁 epoch 来自 `RunState`；候选来自内容表 |
| 9 | `OutOfCombatPotionPool` | 2/1 | 同上，但不过滤 `CanBeGeneratedInCombat` |
| 10 | `RootPotions` | 3 | `Battle.Potions` 的 id+1 |
| 11 | `PowerDeathHistoryRisk` | 1 | `MirrorRiskTables` 冻结表 |
| 12 | `HasEncounter` | 2 | `RunState` 当前房间模型或显式遭遇 |
| 13 | `EncounterSlots` | 1 | 内容表遭遇槽位 |
| 14 | `RootEnemyCount` | 3 | `Battle.EnemyCount` |
| 15 | `BaseMaxEnergy` | 2 | `RunState.Player.MaxEnergy` |
| 16 | `PlayerSlotIndex` | 3 | 单人恒 0 |
| 17 | `BaseOrbSlotCount` | 2 | `RunState.Player.BaseOrbSlots` |
| 18 | `RootSwordSageAmount` | 3 | `Battle` 玩家 `SWORD_SAGE_POWER` |
| 19 | `UnsettlingLampRelicId` | 2 | `RunState.Relics` 顺序中第一个 `UnsettlingLamp` |
| 20 | `HasIdentityContent` | 2/3 | 角色 + `RunState`/`Battle` 的卡/Power/遗物定义标记 |
| 21 | `HasShuffleOrderEnchantment` | 3/1 | `Battle.Cards` 的附魔；hook 位来自内容表 `Enchantments` |
| 22 | `HasBeforeFlushEnchantment` | 3/1 | `Battle.Cards` 的附魔；hook 位来自内容表 `Enchantments` |
| 23 | `HasTheftContent` | 2/3 | 遭遇 id + `Battle` 敌人/Power |
| 24 | `HasDecimalCardVars` | 1/3 | 闭包中每张卡的 `UsesDecimalDamageVar` |
| 25 | `HasInfiniteHpDisplayMonster` | 3/1 | `Battle` 敌人 + 内容表生成闭包中的 `HasInfiniteHpDisplay` |
| 26 | `HasPaelsLegion` | 2 | `RunState.Relics` 未融化遗物 |
| 27 | `FurCoatSpawnHpOne` | 2 | `RunState` 的 FurCoat 遗物属性包（`FurCoatCoordCols/Rows/CoordsSet`）+ 最后一个已访问坐标 |
| 28 | `RootLostHpMask` | 3 | `Battle` 玩家/Osty/敌人的 `LostHpThisTurn` |
| 29 | `HasHistoryCourse` | 2 | `RunState.Relics` 未融化 `HISTORY_COURSE` |
| 30 | `PenNibRelicIndex` | 2 | `RunState.Relics` 行下标（融化也计数） |

分类代码：1 内容级，2 由整局状态决定，3 由战斗根决定，4 只能从原版活对象读到。

## 5. 第 4 类缺口与 `RulesRootFacts`

`Battle` 是战斗的定长公开状态，但它不保存三类只影响规则表、不影响内核结算的原版事实：

1. **已知敌人的初始状态机节点与条件分支选择**。遭遇 setup 会按遭遇随机数、槽位和私有标志（例如 Nibbit/Toadpole 的前后排、BowlbugRock 的失衡标志）解析一次；`Battle` 只保存当前节点。
2. **已知敌人的攻击基值**。`MonsterRules.Attacks` 是规则编译时从原生 `DamageCalc` 捕获的；Waterfall Giant 的 `CurrentPressureGunDamage` 和 The Forgotten 的 `DreadDamage`（加自身 Dexterity）会随私有数值变化，`Battle` 没有这些私有值。
3. **前缀根的根 Power 基线**。`CompileAfterPrefix` 按 `CombatId` 从请求根（前缀前）读取 `RootPowerAmounts`；前缀召唤的敌人没有基线，按 0 处理，而前缀后的 `Battle` 上可能有 Power。

这三类由 `RulesRootFacts` 以根事实传入：

```csharp
public sealed class RulesRootFacts
{
    public required byte[] EnemyInitialNodes;                 // 255 = 未知
    public required int[][] EnemyConditionalTargets;          // int.MinValue = 未知
    public required MonsterAttack[]?[][] EnemyNodeAttacks;    // null = 用内容表
    public required int[]?[] EnemyRootPowerAmounts;           // null = 从 Battle 推
    public bool[] EnemyInitialMoveLogged;                     // 空 = 用内容表
}
```

`RulesBuilder` 在事实缺失时从内容表/`Battle` 推导；条件分支还会调用 Core 内容定义已有的
`MonsterDef.ResolveConditional`。对照程序从原生根规则填这些事实；R2a-2 建局时会在自己的遭遇
setup 里得到同样的值，并把它们和 `Battle` 一起交给构建器。这不是默认值：缺失的初始化节点/
条件/攻击不会被猜测，`RulesBuilder` 会在没有内容变体时显式拒绝。

遭遇表现在按 `EncounterModel.AllPossibleMonsters` 与 `Slots` 导出：每个可能出现的
（怪物，槽位）组合有一行按进阶的 `MonsterRules`，规则由该槽位下的 scratch 怪物编译；实际
生成时再按 `EncounterDef` 的语义选择怪物、槽位和变体。这样不再只保存
`GenerateMonstersWithSlots(NullRunState)` 的一种随机组合。槽位相关的条件分支（Exoskeleton、
Wriggler、Myte、PhantasmalGardener 等）在导出时按真实槽位解析；遭遇变体的私有标志和 HP 扣减
由 `EncounterSpawn.Variant`/`HpReduction` 在 Core 建局时应用。

`docs/import.md` 的 30 项表仍描述原生编译器；本文件描述整局层的新来源。

## 6. 开战值

`Rules` 只服务搜索，不携带建局数值；`ContentTable` 另存两份只读建局数据：

- `RelicStartVars`：每个遗物规范 `DynamicVars` 的全量 `name -> BaseValue`。建局时遗物 Hook 通过
  `ICombatStartContent.RelicVar` 读取，不把常量写进 `RelicDef`、不改变 `Rules` 字节。
- `ContentEncounterMonster.CombatStartByAscension`：按（怪物、槽位、进阶）记录原版
  `MonsterModel.AfterAddedToRoom` 的结果。导出器在临时原版 `CombatState` 中先建出同一遭遇的
  全部怪物和玩家 Creature，再按顺序执行 `AfterAddedToRoom`，捕获施加到怪物/玩家的 Power
  （目标、Applier、层数、`Power.Target`、WitheringPresence 的 `CardsLeft`）及 HP 模式。
  逐组合补齐未出现在该次随机生成中的候选怪物；建局由内核按记录调用 `ApplyPower` 等入口。

三个暂存的原版方法体常量明确列在 [战斗边界](run-combat-boundary.md) 4.1：
`SneckoEye`/`FakeSneckoEye` 的 Confused 1、`FurCoat` 的 SpawnHp 1、Decimillipede 的 HP 步长 2。
其余开战数值均由本机游戏模型导出；WaterfallGiant 的 `BasePressureGunDamage` 走反射读取并在
字段/属性缺失时抛异常，不使用空条件跳过。

## 7. 对照方法

入口：`tests/OfflineHost` 的 `--rules-build-compare`。每个用例在独立子进程里：

1. 用原版路径建根并调用 `RulesCompiler.Compile`（前缀根调用 `CompileAfterPrefix`）；
2. 用 `RunStateImporter` 导入 `RunState`、`BattleImporter` 导入 `Battle`；
3. 用 `RulesBuilder.Build` 重建 `Rules`；
4. 两边 `RulesCodec.Encode` 后逐字节比较；不一致用 `RulesDiff.Compare` 打印第一处差异的表与项。

覆盖：

- D1 全部 410 个输入（含 120 个质量根、3 个回合准备 fixture 展开为 13 个准备根，共 420 个根；前缀根也走 `CompileAfterPrefix` 路径）；
- 自然开局第一场战斗：5 角色 × 进阶 0/10 × 20 种子 = 200，经 `RunTestHost.StartNewRunAsync` 后用 `RunManager.EnterMapCoord` 进入第一排战斗点；
- `RunCatalog` 的 89 个遭遇各一次，外加 Ruby Raiders 的第二个种子（它的 setup 从 5 个 raider 中随机取 3 个，默认种子只覆盖 3 个）。

当前结果：D1 `same=420 diff=0 reject=0 skip=0`，自然 `same=200`，遭遇 `same=90`；没有任何拒绝。内容覆盖：1078/1078 个卡牌升级等级、108/108 个 `Registry` 怪物（含 `EXOSKELETON` 的遭遇变体）、12 个有变量的 Power、100 个有变量的遗物、65 个药水；未用到项为 0。

复跑命令见 [测试与验证](testing.md) 第 6.11 节。
## 8. 新局相关区块（R2b-1）

R2b-1 在同一个 `ContentTable` 里加入新局与地图需要的静态数据，全部由
`src/Import/Run/NewRunContentExporter.cs` 从本机游戏导出：

| 区块 | 内容 | 覆盖数量（0.111.0） |
| --- | --- | --- |
| `Characters[].Starting` | 起始牌组顺序、起始遗物（含默认 `DisplayAmount`）、起始药水、HP、金币、MaxEnergy、球槽、药水槽 | 5 个角色 |
| `Characters[].RelicPool` / `SharedRelicPool` | 遗物池原始顺序；每项稀有度与所需 epoch 掩码 | 共享 118 / 角色各 8 |
| `SharedEvents` / `EventRequiredEpochs` | `ModelDb.AllSharedEvents` 顺序与每个事件的 Event1/2/3 epoch 掩码 | 18 / 66 |
| `SharedAncients` | 共享先古（Darv）顺序与 epoch | 1 |
| `Acts[]` | `BaseRooms`、弱遭遇数、事件顺序、遭遇顺序、先古池、Boss 发现顺序、教学重排交换、两次地图点数量抽取 | 4 幕 |
| `Encounters[].RoomType/IsWeak/TagMask` | 遭遇类型、弱标记、`EncounterTag` 位图 | 89 |
| `MapRules` | 网格宽度、起始路径数、精英上限、宝箱/限制行、轮数、五组点类型限制集合、每进阶精英数、商店数 | — |
| `BaseOdds` / `AscensionRules` / `NeowEpoch` | 概率初值；SwarmingElites/TightBelt/AscendersBane/DoubleBoss 等级、进阶之灾卡、最高进阶；Neow epoch | — |
| `MapHookRelics` / `MapHookCards` | 覆写地图 Hook 的模型（FurCoat/GoldenCompass、SpoilsMap），用于显式拒绝 | 2 / 1 |
| `FreeTravelRelics` | 覆写 `ShouldAllowFreeTravel` 的遗物（WingedBoots） | 1 |
| `UnknownPointHookRelics` / `UnknownPointHookCards` | 覆写未知点候选或概率成长的模型（GoldenCompass/JuzuBracelet、LanternKey） | 2 / 1 |
| `NextEventHookCards` | 覆写 `ModifyNextEvent` 的卡牌（LanternKey） | 1 |
| `EventAllowedOverride` | 按事件 id 标记覆写 `IsAllowed` 的事件，R2b-2 遇到时显式拒绝 | 36 / 66 |
| R2c-1 奖励区块 | 见第 9 节：遭遇金币/奖励开关、奖励概率、教学奖励、遗物/卡牌默认属性 | — |

池子按原版枚举顺序保存，不排序；解锁过滤按 epoch 掩码。两处原版方法只能“探测”得到：

- `ActModel.GetMapPointTypes` 的两次抽取：用受控 `Rng` 状态探测，记录返回值、`Counter` 增量与
  原始 `NextDouble` 序列，在均匀/高斯候选空间里重放；`round(mean+z)+offset` 与
  `round((mean+offset)+z)`、上下界同移等价，所以候选取 `offset=0` 的规范代表。远尾探针让
  拒绝区间可见，唯一候选对才导出。
- `ActModel.ApplyActDiscoveryOrderModifications`：在构造的 `RoomSet` 上调用原生方法得到交换
  集合，再枚举每个列表的交换顺序与 48 个随机排列比对；`NumberOfRuns=1/2` 探测得到
  “只在首次跑局生效”标记。

导出器对目录外事件、幕外交换模型、非法稀有度显式失败。单次导出（Release）约 1.3 秒，其中新局
推断约 0.45 秒；仍然只在进程内保留一次，不写盘、不缓存。详见[新局生成与地图](run-generation.md)。

## 9. 战后奖励区块（R2c-1）

R2c-1 在同一个 `ContentTable` 里加入战后奖励需要的静态数据；Core 的 `RunRewardGenerator`
只读这些字段，不写奖励数值字面量。

| 区块 | 内容 | 覆盖数量（0.111.0） |
| --- | --- | --- |
| `Encounters[].ShouldGiveRewards` | 遭遇是否产生战后奖励 | 89 |
| `Encounters[].MinGoldByAscension` / `MaxGoldByAscension` | 每个进阶等级的遭遇金币区间（含 Poverty） | 89 × 11 |
| `Encounters[].CustomGoldProportion` | `CalculateGoldProportion` 依赖战斗私有状态（GremlinMerc） | 1 |
| `RewardRules` | 药水基础概率/保底步长/精英加成、卡牌稀有度两套（Scarcity）阈值与成长、升级概率、遗物稀有度阈值、药水稀有度阈值 | — |
| `TutorialRewards` | 首次跑局 Ironclad 的怪物 7 组、精英 2 组、Boss 1 组固定卡牌/药水/遗物与金币常量 | 10 组 |
| `RelicRewardFacts[].Allowed` | 反射确认重写 `IsAllowed` 后按映射得到的五种变体（常真、Act3 宝箱前、单人、首次跑局 Ironclad 例外、永不允许）；未映射的重写为 `Unsupported`，映射里有但未重写导出失败 | 297 |
| `RelicRewardFacts[].Stackable` | 拿到后是否从袋中移除 | 297 |
| `RelicRewardFacts[].InitialDisplayAmount` | 新建可变遗物的 `DisplayAmount` | 297（非可抽稀有度记 0） |
| `RelicRewardFacts[].Obtain` | `SimpleAdd` / `Implemented` / `Unsupported` | 297 |
| `RelicRewardFacts[].RewardHooks` | 反射得到的奖励 Hook `Overridden`/`Implemented` 位集；任何 `Unsupported` 位在生成/领取前拒绝 | 297 |
| `ContentTable.CardRewardHooks` / `EnchantmentRewardHooks` / `PotionRewardHooks` | 按 id 索引的同类反射位集；卡牌 1 个重写（SovereignBlade），附魔/药水 0 | 558 / 22 / 65 |
| `RelicRewardFacts[].DefaultProperties` | 新建遗物的 `SavedProperties` 默认值 | 297 |
| `Cards[].DefaultProperties` | 每个升级等级的卡牌 `SavedProperties` 默认值（Genetic Algorithm 等） | 1078 个等级 |

奖励监听者清单与三态规则见[战后奖励](run-rewards.md)第 5 节；`CONTENT-REWARD-HOOK` 行会打印每类
实际重写/未实现数量。

`RewardRules` 的几个内联常量（药水 0.1 保底、精英 0.5 系数、遗物 0.5/0.83 阈值、药水
0.1/0.35 阈值）通过读取原版方法体的 `ldc.r4` 导出；卡牌稀有度阈值通过
`CardRarityOdds.GetBaseOdds` 与 `CardRarityOdds.RarityGrowth` 在 A0/A10 两种
`ScratchAscension` 下读取；升级概率通过 `CardFactory.UpgradedCardOddScaling` 读取。
枚举与表结构见 `src/Core/Simulation/Run/RunContent.cs`。

## 10. 商店区块（R2c-2）

商店复用 `RewardRules` 的遗物/药水稀有度阈值，并增加以下静态数据：

| 区块 | 内容 | 覆盖数量（0.111.0） |
| --- | --- | --- |
| `ShopRules.CharacterCardTypes` | 反射 `MerchantInventory._coloredCardTypes` | 5 |
| `ShopRules.ColorlessCardRarities` | 反射 `MerchantInventory._colorlessCardRarities` | 2 |
| `ShopRules.CardCosts` / `ColorlessCardCosts` | 反射调用原版私有 `MerchantCardEntry.GetCost` 得到每个稀有度的整数价（无色已含 1.15 倍） | 各 11 槽 |
| `ShopRules.PotionCosts` | 反射调用 `MerchantPotionEntry.GetCost` | 6 槽 |
| `ShopRules.CardPriceMin/Max`、`RelicPriceMin/Max`、`PotionPriceMin/Max` | 方法体 `ldc.r4` 价格倍率 | 3 对 |
| `ShopRules.SaleDivisor` | `MerchantCardEntry.CalcCost` 的整数常量 | 1 |
| `ShopRules.PotionPriceVaries` | 导出进程的 `TestMode.IsOff`；live-prices 对照可强制 true | 1 |
| `ShopRules.WingedBootsRooms` | `WingedBoots` 的 `Rooms` 动态变量（最大次数/`DisplayAmount` 基准；自由移动时 `TimesUsed` 递增） | 1 |
| `ShopRules.RemovalBaseCost*` / `RemovalPriceIncrease*` / `InflationLevel` | 反射 `MerchantCardRemovalEntry.BaseCost`/`PriceIncrease` 在 A0 与 Inflation 的值 | 4 + 1 |
| `ShopRules.ShopRareOdds` / `ShopUncommonOdds` / `ScarcityShop*` | 反射 `CardRarityOdds.GetBaseOdds(Shop, …)` 在 A0/Scarcity 的值 | 4 |
| `RelicRewardFacts[].MerchantCost` | 反射 canonical 遗物的 `MerchantCost`（假遗物可覆盖） | 297 |
| `RelicRewardFacts[].AllowedInShops` | 反射 `IsAllowedInShops` | 297 |
| `RelicRewardFacts[].ShopHooks` | 商店 Flow 的 `Overridden`/`Implemented` 位集 | 297 |
| `ContentTable.CardShopHooks` / `EnchantmentShopHooks` / `PotionShopHooks` | 按 id 索引的同类位集 | 558 / 22 / 65 |

Hook 清单与三态规则见[商店](run-shop.md)第 6 节。导出器对名单内有但未重写、重写但不在名单内
都显式失败/标记 `Unsupported`；`IntConstants` 读取整数常量，`FloatConstants` 读取浮点常量。

**房间 Hook 的位规则**：`BeforeRoomEntered`/`AfterRoomEntered` 在原版是通用方法，但
`ContentShopHookMask` 只记录 Core 已实现的那一支。当前只有商店位
`BeforeShopRoomEntered`/`AfterShopRoomEntered`；以后新增休息处、宝箱、事件等房间时，必须
为每种房间各加一对位，并在 Core 里单独实现/判定，不能复用商店位，也不能因为模型重写了
通用方法就默认其他房间已实现。战斗房间的 `AfterRoomEntered` 走
`CombatStart.AfterRoomEntered` 的独立战斗规则。

## 11. 建局专用闭包（R2a-5）

`ContentDef` 的三个虚属性只影响 `RunCombatSetup` 的 provisional `Rules`，不进入
`CombatSetupResult.Rules`，也不进入冻结规则表：

| 属性 | 用途 | 使用者 |
| --- | --- | --- |
| `SetupOnlyCardPools` | 第一回合从池里采样生成卡 | Toolbox（无色）、ChoicesParadox（角色）、VexingPuzzlebox（角色）、BigHat（角色 Ethereal）、OrangeDough（无色） |
| `SetupOnlyGeneratedCards` | 第一回合按 id 生成固定卡 | TeaOfDiscourtesy（Dazed）、NinjaScroll（Shiv） |
| `SetupOnlyOutOfCombatPotionPool` | `BeforeCombatStart` 用局外药水池补槽 | DelicateFrond |

`RulesBuilder.Build` 的 `includeSetupContent` 只在 provisional 调用时为 true；`RulesCompiler`
（原版侧规则对照）不读取这三个属性。BigHat 的 `GeneratedCardPools` 保留旧的
`CharacterAll` 闭包以保持冻结规则字节不变，`SetupOnlyCardPools` 才提供原版
`CharacterEthereal` 采样池。最终 `Rules` 只按 `Battle.Cards`/`Battle.Potions` 里实际留下的
生成内容补统计。

### 11.1 建局 Hook 掩码

`ContentTable` 另有 `CardSetupHooks`/`RelicSetupHooks`/`PowerSetupHooks`/`EnchantmentSetupHooks`
四个 `ContentSetupHookMask[]`。导出器对每个原生模型反射判断是否重写了建局调用链上的 Hook
（`AfterRoomEntered`、`BeforeCombatStart(Late)`、`BeforeSideTurnStart`、`AfterEnergyReset(Late)`、
`BeforeHandDraw(Late)`、`ModifyHandDraw(Late)`、`AfterPlayerTurnStart(Early/regular/Late)`、
`AfterSideTurnStart(Late)`、`ModifyMaxEnergy`、`ModifyOrbValue`、`ShouldProcurePotion`、
`AfterCardDrawn(Early)`、`AfterCardEnteredCombat`、`AfterCardGeneratedForCombat`、
`ModifyShuffleOrder`、`AfterAutoPrePlayPhaseEntered(Late)`）；`Implemented` 来自 Core 定义的同名
重写、显式别名或逐项 no-effect 名单，剩余位由 `RunCombatSetup` 以 `setup-hook-listener`
拒绝。遗物矩阵从 `Implemented` 名单自动生成，`--setup-matrix-list` 打印计数和名单。
