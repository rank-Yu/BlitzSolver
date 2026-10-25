# 原生战斗状态导入

[返回文档索引](README.md)

`src/Import` 把游戏原生的 `CombatState` 直接投影成定长 `Battle`，不建 CombatSolver 的
`SimulatedCombatState` 影子状态。它是仓库中唯一引用 `sts2.dll`、Godot/Harmony 类型并读取游戏
模型私有字段的层；`Core` 不引用它，`BlitzSolver.sln` 也不包含它（CI 没有游戏文件）。

本页说明入口、字段来源、拒绝规则与离线对照方式。冻结快照格式见 [冻结快照](golden-data.md)，
复跑命令见 [测试与验证](testing.md)。

## 1. 入口与输入

```csharp
Battle battle = BattleImporter.Import(combatState, player, rules, options);
```

- `combatState`：当前原生战斗；导入只读，不改状态。
- `player`：本地单人玩家；根要求玩家出牌阶段（`RequirePlayPhase`）。
- `rules`：该根的冻结 `Rules` 对象，来自 `meta.json` 的 `rulesSha256` 对应
  `objects/rules/<sha>.br`，由 `RulesCodec.Decode` 解码。规则编译是 M2a-3，本批不重编。
- `options`：在根上没有原生对应物的少量事实（`ShufflesCrossed`、`HasRisk`、事件失败边界、
  回合准备普通根标记）。默认值描述普通出牌阶段根。

输出先 `default(Battle)`，再逐字段写；不复用缓冲，因此 padding 始终为零。卡牌小数变量最后
按“detached 卡 → 牌堆存储顺序”规范化。

## 2. 字段组与来源

| 字段组 | 原生来源 | 移植/独立实现 |
| --- | --- | --- |
| `Round`,`PlayerTurn`,`Energy`,`Stars`,`Gold`,`Side`,`Phase` | `CombatState` / `PlayerCombatState` / `Player` | 直接读 |
| `NextCombatId` | `CombatState._nextCreatureId` | 直接读 |
| `ShuffleEventCount` | 模拟器创建时为 0 | 根事实 |
| `ShufflesCrossed` | 旧 `SimulationSnapshot` | 根事实（`ImportOptions`） |
| `Outcome` | 只有 `EventDefeat` 边界需要外部事实 | 根事实 |
| `Flags` | `CombatManager.IsAboutToLose`、`Player.IsActiveForHooks`、`Player.Osty`、旧快照风险 | 直接读 + 根事实 |
| `Player`,`Osty` | `Creature.CurrentHp/MaxHp/Block`；本回合未格挡伤害来自原生 History | 直接读 + History |
| `Rng`（9 条） | `RunState.Rng` 的 9 条流 | 直接读 |
| `Enemies` | `CombatState.Enemies`、`MonsterModel.NextMove/StateLog`、`MoveStateMachine`、`Rules.MonsterRule` | 直接读；AI 节点映射用冻结规则，不重编 |
| `Powers` | `Creature.Powers` 的原生顺序，按 owner 分组 | `FlashStateProjections` 的 case 表移植；私有字段由 `NativeAccess` 集中读取 |
| `Relics` | `Player.Relics` 顺序 | `ProjectRelic` 移植；`StatefulRelicState` 从原生字段或原生 History 读取 |
| `Cards` | 五堆原生顺序、`CardModel`、`Rules.Card(id, upgrade)` | `FlashStateImporter.Card` 移植 |
| `DetachedCards` | `NightmarePower.selectedCard`、`SwipePower.StolenCard` | 直接读 |
| `DecimalVars` | `DynamicVars` 与规则基值的差 | `DecimalVars.TryAllocate/TryCanonicalize` |
| `Counters` | `CombatManager.History` 的 13 类条目 + 根上为 0 的账本 | 独立实现，对照 `RootCombatHistorySnapshot`/`SimulatedCombatState` 的 getter |
| `HistoryCourseCard` | 根上旧引擎的 `_lastAttackThisTurn` 为空 | 不填；跨回合槽由 M2a-5 处理 |

`Counters` 的按项划分沿用 M2-0 报告 A.1.2：45 项是历史/账本，2 项原生私有状态，1 项引擎缓存。
新根上成长、死亡、偷窃与累计 HP 账本都从零开始；本回合计数从原生 History 现算。
`VitalSparkNormalizedAmount` 在普通根是 `int.MinValue`，回合准备普通根是 0（旧桥在准备阶段
至少做过一次规范化）。

## 3. 拒绝规则

所有拒绝抛 `UnsupportedImportException`，`Reason` 是稳定字符串。绝不返回带默认值的半份
`Battle`。

| 原因码 | 含义 |
| --- | --- |
| `multi-player` | 玩家数不是 1，或多人专用内容 |
| `root-phase` | 根不在玩家出牌阶段 |
| `unsupported-card/power/relic/potion/orb/monster/enchantment/affliction` | 内容定义缺失 |
| `test-content` / `deprecated-content` | Mock/废弃内容 |
| `third-party-content` | 模型类型来自 `sts2` 以外的程序集 |
| `missing-private-field` | 读取层找不到字段、内部数据为 null 或类型不符 |
| `unclaimed-private-state` | 捕获了私有状态，但没有任何投影认领 |
| `unrepresentable-state` | 原生值无法用 `Battle` 表示（分数、越界、复制品等） |
| `capacity` | 固定容量不足 |
| `rules-mismatch` | 原生根与传入的 `Rules` 不一致 |
| `known-enemy-history` | 已逃离/已死亡敌人破坏了 `KnownEnemies` 顺序 |
| `run-deck-hook` | 牌组卡实现局外监听 Hook |

### 3.1 全有或全无

`PrivateStateLedger` 在导入前枚举旧桥会捕获的根私有状态：

- `PowerPredictionStateSupport.CaptureRootState` 的 13 种 Power；
- `RelicPredictionStateSupport.IsTracked` 的 25 种遗物；
- `SimulatedCombatState.IsStatefulRelic` 的 19 种遗物。

每个条目必须由 `ProjectPower`/`ProjectRelic` 认领。例如 `SoulboundPower`/`CacophonyPower`
虽然会被旧桥捕获，但本批没有投影实现，因此根会被 `unclaimed-private-state` 拒绝，而不是
丢掉状态。没有捕获条目的怪物私有整数也要么被 `MonsterDef.AiRegisterNames`/惰性名单认领，
要么拒绝。

### 3.2 第三方内容

所有读到的 `AbstractModel`（卡、Power、遗物、药水、球、怪物、附魔、苦难）先检查类型程序集
必须是 `sts2`；否则以 `third-party-content` 拒绝。`RejectRunListenerDeckCards` 对玩家牌组做
同样的检查，并拒绝带局外监听 Hook 的非惰性牌组卡。

## 4. 离线对照模式

`tests/OfflineHost` 的 `--import-compare` 为每个根建原生状态、导入，再与
`d1/<root>/golden.br` 的期望 `BATL` 逐字节比较：

```bash
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --root kg2-b-stomp --import-compare
dotnet artifacts/bin/OfflineHost/release/OfflineHost.dll \
  --all --jobs 8 --import-compare
```

每根只产生三种结果：

- `IMPORT status=same`；
- `IMPORT status=diff field=<字段>`：随后打印 `BattleDump.Diff` 的前 24 行，
  每行带 `+偏移 字段: golden=[...] import=[...]`；
- `IMPORT status=reject reason=<原因码>`，随后一行 detail。

`--all` 只对照 397 个根（410 个输入去掉 13 个带 `flashPrefixActions`/`flashPrefixPlan` 的
fixture），汇总 `same/diff/reject`，并分别按首个差异字段、拒绝原因分组计数。

### 4.1 回合准备的三个普通根

`qf2-gradient-failure`、`ts-b-toolbox`、`ts-a-toasty` 是回合准备 fixture。它们各自的
`golden.br` 有 4/3/4 个根：1 个普通根 + 8 个额外准备根（不在本批范围）。普通根是旧桥用
**第一个准备选项**回放得到的根；离线宿主用 `FirstChoiceCardSelector` 记录原生第一个选项，
对照模式据此选择 golden 中的对应根，而不是固定取 `Roots[0]`：

| 根 | 普通根下标 | 其余准备根 |
| --- | ---: | ---: |
| `qf2-gradient-failure` | 1 / 4 | 3 |
| `ts-b-toolbox` | 0 / 3 | 2 |
| `ts-a-toasty` | 1 / 4 | 3 |

这 3 个普通根的普通原生状态是在测试宿主注入初始状态**之前**捕获的；对照模式因此跳过
`InjectInitialStateAsync`。额外 8 个准备根与 13 个前缀根都留给 M2a-5。

## 5. 当前结果与限制

`--all --jobs 8 --import-compare` 当前：

```text
import same=397 diff=0 reject=0
```

3 类负例由 `--import-negative` 验证：

```text
NEGATIVE case=private-read status=rejected reason=missing-private-field
NEGATIVE case=unclaimed-private-state status=rejected reason=unclaimed-private-state
NEGATIVE case=third-party-content status=rejected reason=third-party-content
```

未完成/限制：

- 13 个前缀根与 8 个额外回合准备根不在本批；需要旧引擎的回放语义，留给 M2a-5。
- 回合准备普通根用 `CardModel.DeckVersion == null` 判断“准备期间生成的卡”，因为本批没有
  捕获 Start 时刻的卡对象集合；M2a-5 应改为传入真实的根卡集合。
- 手牌上限读游戏的 `CardPile.MaxCardsInHand`（当前为 10）。RitsuLib 等 Mod 另行修改手牌上限的方式不在支持范围。
- 私有字段读取绑定当前游戏版本；字段缺失、类型不符或内部数据为 null 都显式拒绝。
- 未接入实机；只在离线宿主上验证。
