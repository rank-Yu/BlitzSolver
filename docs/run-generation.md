# 新局生成与地图

[返回文档索引](README.md)

本文记录 R2b-1 的局外生成：从角色、进阶、种子和解锁状态出发，在 Core 里重放原版新局
（`RunState.CreateForNewRun` → `RunManager.InitializeNewRun` → `GenerateRooms` →
`SetActInternal(0)`），得到玩家做第一个选择之前的 `RunState`；以及每幕的 `StandardActMap`
地图生成。状态字段、随机数派生和导入见[整局状态布局](run-state-layout.md)，内容表其余部分见
[内容表与 RulesBuilder](content-table.md)，对照命令见[测试与验证](testing.md)第 6.15 节。

本批不做决策协议、先古之民事件与地图选点；这些在 R2b-2 的
[局外决策协议与地图进点](run-decisions.md) 里落地，本文只保留新局入口与地图生成。

## 1. 原版新局流程

下表按调用顺序列出写入的状态与消耗的随机数。所有新局随机数都来自 `UpFront`，地图来自
`new Rng(seed, "act_N_map")`；其余 14 条流在新局阶段不消耗。

| # | 原版步骤 | 写入状态 | 随机数 |
| ---: | --- | --- | --- |
| 1 | `RunState.CreateForNewRun`：`new RunRngSet(seed)` | `Rng.Seed = XxHash64(UTF8(seed))`（`old` 前缀用旧 32 位哈希），15 条命名流计数 0 | 无 |
| 2 | `new RunOddsSet` / `PlayerOddsSet` | 未知点 `Monster=0.1, Elite=-1, Treasure=0.02, Shop=0.03`；卡稀有度 `-0.05`；药水 `0.4` | 无 |
| 3 | `CreateShared` | 起始牌组进入原生 `_allCards`；Core 按顺序写 `RunState.Deck`（`FloorAdded=1`） | 无 |
| 4 | `Player.InitializeSeed(seed)` | `Rng.PlayerSeed = hash(seed) + slot`；玩家流 Rewards/Shops/Transformations 计数 0 | 无 |
| 5 | 起始牌 `AfterCreated()` | 只有 `SpoilsMap` 覆写，起始牌组没有 | 无 |
| 6 | `InitializeNewRun`：共享遗物袋 `Populate(GetUnlockedRelics, UpFront)` | `SharedBag`：共享池按解锁过滤后按稀有度分桶（第一次出现顺序），每桶 `UnstableShuffle` | 每次抽取 1 次；Σ(桶大小-1) |
| 7 | 玩家遗物袋 `Populate(player, UpFront)` | `PlayerBag`：共享池 + 角色池，解锁过滤后只保留 Common/Uncommon/Rare/Shop，同样分桶洗牌 | Σ(桶大小-1) |
| 8 | `SetStartedWithNeowFlag` | `Extra.StartedWithNeow = IsEpochRevealed<NeowEpoch>` | 无 |
| 9 | `ApplyAscensionEffects` | A4：药水槽 3→2；A5：牌组末尾加 `AscendersBane`（`FloorAdded=1`） | 无 |
| 10 | `FinalizeStartingRelics` | 五个起始遗物的 `AfterObtained` 都不改状态 | 无 |
| 11 | `GenerateRooms`：共享先古 `UnstableShuffle(UpFront)` | 无（只用于每幕子集） | Σ(n-1) |
| 12 | 对第 2、3 幕：`NextInt(remaining+1)` 取前缀 | 每幕 `_sharedAncientSubset` | 每幕 1 次 |
| 13 | 每幕 `ActModel.GenerateRooms`：本幕事件 + 共享事件，按 Event1/2/3 epoch 过滤 | `Act.Events` | Σ(事件数-1) |
| 14 | 弱遭遇：`NumberOfWeakEncounters` 个（Overgrowth/Underdocks 3，Hive/Glory 2） | `Act.Normal` | 每抽 1 次起；同 tag 重试再抽 |
| 15 | 常规遭遇：补到 `BaseNumberOfRooms` | `Act.Normal` | 同上 |
| 16 | 精英：固定 15 个 | `Act.Elite` | 同上 |
| 17 | Boss：`NextItem(AllBossEncounters)` | `Act.BossId` | 1 次 |
| 18 | 先古：`NextItem(GetUnlockedAncients + 共享子集)` | `Act.HasAncient/AncientId`；空集合不抽 | 1 次或 0 |
| 19 | `ShouldApplyTutorialModifications()` 时 `ApplyDiscoveryOrderModifications` | Boss 换成发现顺序里第一个未见的；`ApplyActDiscoveryOrderModifications` 的固定交换 | 无 |
| 20 | 最后一幕且 A10：`NextItem(AllBossEncounters 去掉 Boss)` | `Act.HasSecondBoss/SecondBossId` | 1 次 |
| 21 | `SetActInternal(0)`：切换幕、清 visited、`ResetToBase` | `ActIndex=0`、`ActFloor=0`、`Visited`、`Odds` | 无 |
| 22 | `GenerateMap` → `StandardActMap` | `Map` | `act_1_map`，见第 3 节 |
| 23 | `!StartedWithNeow && 幕 0` | 起点 `PointType = Monster` | 无 |
| 24 | `EnterAct` 的房间入口 | `Rooms`、`Visited`、历史、事件 RNG | 见[局外决策协议与地图进点](run-decisions.md) |

R2b-1 的对照边界是第 22 步之后、第 24 步之前：第一幕地图已经生成，还没有进入任何房间。
第 24 步之后的进点边界由 R2b-2 实现并对照。
R1b 的 `RunTestHost.StartNewRunAsync` 包含第 24 步的 `EnterAct(0)`，因此它要求
`VisitedMapCoords == 0`，只适用于没有 Neow 的存档；有 Neow 时第 24 步会进入先古事件。新入口
`RunTestHost.StartNewRunBeforeChoicesAsync` 在 `Launch` 后直接调用 `GenerateMap`，与
`SetActInternal(0)` 里地图生成那一段等价，从而让 Neow 开和关的存档都停在同一边界。

## 2. 实现

### 2.1 入口

`src/Core/Simulation/Run/RunGenerator.cs`：

```csharp
RunState state = RunGenerator.CreateNewRun(contentTable, new NewRunRequest
{
    Character = characterId,          // RunCatalog 角色 id
    Ascension = ascension,            // 0..10
    Seed = "原版种子字符串",
    Unlock = runUnlockState,          // RunUnlockState：epoch 位图 + 已见遭遇位图 + 跑局数
    TutorialModifications = tutorial, // ShouldApplyTutorialModifications()
}, out RngState actMapRng);

RunGenerator.SetAct(contentTable, ref state, actIndex, out RngState actMapRng);
```

`CreateNewRun` 按第 1 节顺序写状态；`SetAct` 是 `SetActInternal` 去掉房间入口的部分：切换幕、
清 visited、`Odds` 回到初值、从 `Rng.Map[actIndex]` 生成该幕地图并置 `MapConsumed[actIndex]=1`。
换幕只影响地图与 `ActIndex/ActFloor/Visited/Odds`；三幕的房间序列在 `CreateNewRun` 里一次生成完。

不支持的情况显式失败：未知角色/幕/遭遇/事件、进阶越界、容量溢出、非 `Standard` 输入（入口本身
不含模式参数），以及地图 Hook 内容（见 2.4）。

### 2.2 随机数

`UpFront` 的消耗完全由第 6–20 步决定。Core 的 `RunRandom` 提供三个原版语义的抽取：

- `NextGaussianInt(ref RngState, mean, stdDev, min, max)`：每次尝试两次 `NextDouble`，
  `sqrt(-2 ln(1-d1)) * sin(2π(1-d2))`，`Math.Round`（银行家舍入），落在闭区间外则重试；
- `UnstableShuffle`：`while (n>1) { n--; swap(n, NextInt(n+1)); }`；
- `RunGrabBag`：权重累计（新局权重全为 1）、谓词不匹配时不抽、谓词没有候选项时直接返回 null。

遭遇选择先试 `!e.SharesTagsWith(last) && e != last`，谓词无候选项时不再消耗随机数，退化为
无谓词抽取。连续相同 tag 的重试次数因此与原版一致。

地图流：`Rng.Map[actIndex]` 保存 `new Rng(seed, "act_N_map")` 的初始状态，
`RunMapGenerator.Generate` 从它开始并返回消耗后的 `RngState`；`MapConsumed` 只表示“该幕地图
已生成”，不存消耗后的状态（原版每幕都是临时 `Rng`）。

### 2.3 地图

`src/Core/Simulation/Run/RunMapGenerator.cs` 按 `StandardActMap` 的结构实现：

1. `_mapLength = BaseNumberOfRooms + 1`；7 列网格；起点 `(3,0)`、Boss `(3,mapLength)`、
   A10 最后一幕的第二 Boss `(3,mapLength+1)`；
2. `GetMapPointTypes`：本幕两次抽取（休息点、未知点），参数来自内容表；
3. `GenerateMap`：7 条路径，每条起点列 `NextInt(0,7)`（第二条重复则重抽），逐行
   `StableShuffle([-1,0,1])`（先排序再 Fisher-Yates）并避免交叉；
4. `AssignPointTypes`：最后一行休息、倒数第 7 行宝箱、第 1 行怪物；把休息/商店/精英/未知
   塞进队列，三次“未分配点排序后洗牌 + 逐个取第一个合法类型”；
5. `MapPathPruning.PruneAndRepair`：最多 3 轮，删除重复段（每轮最多 50 次迭代）并按原顺序
   补回商店/精英/休息/未知的数量；
6. `MapPostProcessing`：`CenterGrid`、`SpreadAdjacentMapPoints`、`StraightenPaths`；
7. 导出 `RunMap`：先网格点（列优先），再起点/Boss/第二 Boss；起点类型按 `StartedWithNeow`
   在第 23 步覆盖。

每个参与判定的点类型集合（父子、子、兄弟、下方、上方）与网格宽度、起始路径数、精英上限、
每进阶精英数、商店数都来自 `ContentMapRules`；这些集合由导出器从 `StandardActMap` 的私有静态
集合读取。

### 2.4 地图 Hook 与显式失败

新局只有起始遗物，五件起始遗物都不覆写地图 Hook；因此第一幕地图不经过
`ModifyGeneratedMap`/`ModifyGeneratedMapLate`/`AfterMapGenerated`。后续幕如果 `RunState` 的
遗物或牌组里有 `FurCoat`、`GoldenCompass`、`SpoilsMap`，原版会改写地图（加 quest、换成
GoldenPath/Spoils 地图）；R2b-1 不实现这些变体地图，`RunGenerator.SetAct` 在检测到它们时抛出
`UnsupportedException`（消息含会改写地图的模型）。`StandardActMap` 的
`shouldReplaceTreasureWithElites`（Warden 进阶）在 `RunManager.GenerateMap` 里恒为 false，因此
不导出。

## 3. 池子与解锁

内容表新增的部分（类型在 `src/Core/Simulation/Run/RunContent.cs`）：

| 内容 | 说明 | 0.111.0 数量 |
| --- | --- | --- |
| `Characters[].Starting` | 起始牌组（顺序）、起始遗物（含默认 `DisplayAmount`）、起始药水、HP、金币、MaxEnergy、球槽、药水槽 | 5 个角色 |
| `Characters[].RelicPool` / `SharedRelicPool` | 遗物池原始枚举顺序；每项的稀有度与所需 epoch 掩码 | 共享 118；角色各 8 |
| `SharedEvents` / `EventRequiredEpochs` | `ModelDb.AllSharedEvents` 顺序；每个事件需要的 Event1/2/3 epoch | 18 个共享事件 / 66 个事件 |
| `SharedAncients` | 共享先古（Darv）顺序与所需 epoch | 1 |
| `Acts[]` | 每幕 `BaseRooms`、弱遭遇数、事件顺序、遭遇顺序（弱/常规/精英/Boss 由 `ContentEncounter.RoomType/IsWeak` 分类）、先古池、Boss 发现顺序、教学重排交换、两次地图点数量抽取 | 4 幕 |
| `Encounters[].RoomType/IsWeak/TagMask` | 遭遇的房间类型、弱标记、tag 位图（`EncounterTag`） | 89 |
| `MapRules` | 宽度、起始路径数、精英上限、宝箱行/上下限制行、点类型分配与修剪轮数、五组限制集合、每进阶精英数、商店数 | — |
| `BaseOdds` / `AscensionRules` / `NeowEpoch` | 概率初值；SwarmingElites/TightBelt/AscendersBane/DoubleBoss 的等级、进阶之灾卡 id、最高进阶；Neow epoch | — |

池子顺序全部是原版枚举顺序，不排序：洗牌结果依赖它。解锁过滤按 `RequiredEpochs` 位图做，
不读存档。

**遗物袋**：共享袋用共享池、保留全部 8 个稀有度（含 Event/Ancient）；玩家袋用共享池 + 角色池、
只保留 4 个可抽取稀有度。两者都先按稀有度第一次出现的顺序分桶，再逐桶 `UnstableShuffle`；
共享袋 `RefreshAllowed=true`、玩家袋 false。

**教室重排**：`ShouldApplyTutorialModifications()` 为 true 时，每幕先按 `BossDiscoveryOrder`
把 Boss 换成第一个未见的遭遇，再执行该幕的固定交换（`RoomSet.SwapToOrCreateAtIndex` 语义：
找到就与目标下标交换，找不到就放到目标下标）。0.111.0 只有 Overgrowth 有固定交换（7 个常规、
2 个事件、2 个精英），且只在 `NumberOfRuns == 0` 时执行。

## 4. 内容表导出与推断

`src/Import/Run/NewRunContentExporter.cs` 从本机游戏模型导出上面所有数据。两部分无法直接用
getter 读到，改为“探测原版方法”：

- **地图点数量抽取**：给 `ActModel.GetMapPointTypes(Rng)` 喂可控的 `Rng` 状态（2400 个随机状态
  加 48 个按首次正态偏差分桶的远尾状态），记录返回值、`Counter` 增量和原始 `NextDouble` 序列；
  再在候选空间（均匀 `NextInt(min,max)` 与高斯 `NextGaussianInt(mean,stdDev,min,max)`，`mean`
  0..32、`stdDev` 1..3、上下界 0..33）里重放。`round(mean+z)+offset` 与
  `round((mean+offset)+z)`、上下界同移完全等价，因此候选取 `offset=0` 的规范代表。远尾探针
  让拒绝区间可见，唯一候选对才导出；有歧义时导出失败而不是猜。
- **教学重排**：构造 `RoomSet`（三个列表填幕外模型），调用
  `ActModel.ApplyDiscoveryOrderModifications`；全填充列表只走“创建”分支，从而得到
  `(列表, 目标下标, 模型)` 集合；再对每个列表枚举交换顺序（≤ 8 个交换），在 48 个随机排列上
  与原生结果比对，唯一顺序（或代数等价的一组）才导出。`NumberOfRuns=1/2` 再探测一次得到
  “只在首次跑局生效”标记。

导出时如果遇到无法代表的模型（幕外交换、非 `Enum` 稀有度、目录外事件）直接抛
`InvalidDataException`。

## 5. 对照方法

`tests/OfflineHost` 的新模式 `--new-run-compare`：每个用例在同一子进程里先走原版
（`RunTestHost.StartNewRunBeforeChoicesAsync`），再用 Core 生成，导入原版后与 `RunDump` 逐字段
比较（包括 15 条流的状态与计数、`MapConsumed`、遗物袋、三幕房间序列、地图）。随后对第 2、3 幕
调用 `RunManager.SetActInternal`，Core 调用 `RunGenerator.SetAct`，比较地图与 `act_N_map` 的
消耗后状态（`Counter` 与四个状态字）。

对照覆盖：5 角色 × 进阶 0..10 × 6 种解锁状态 × 教学重排开/关 × 每组 30 个种子 = **19,800** 个
新局，每个新局都对照第 1、2、3 幕。解锁状态包括 R1b 的空存档、全部解锁、三种实机跑局的
`encountersSeen`/`NumberOfRuns` 部分解锁，以及两种按游戏真实解锁顺序截断的部分 epoch 状态。
失败时打印第一处差异的字段路径与两边的值。结果见 R2b1 报告。

## 6. 保留的字面量

只保留原版方法体里的算法常量，不是每幕内容；全部列在 R2b1 报告第 4 节：

- `StandardActMap`：宝箱行 `GetRowCount()-7`、下方限制行 `row<6`、上方限制 `row>=length-3`、
  点类型分配 3 轮、`PruneAndRepair` 3 轮、`PruneDuplicateSegments` 50 次上限；
- `Player.CreateForNewRun` 的药水槽初值 3；
- `Rng.NextGaussianInt` 的两次 `NextDouble` 与银行家舍入（算法，不是数据）。

每幕内容（房间数、弱遭遇数、事件/遭遇/先古池及顺序、tag、稀有度、epoch、地图点数量参数、
进阶修改）都来自内容表。
