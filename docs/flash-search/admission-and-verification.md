# Flash 准入与校验

[返回 Flash 入口](README.md)

Flash 的正确性由三道关口保证：搜索前的**准入**、开发期的**差分测试**、部署前的**旧引擎回放核对**。任何一关失败都回落到现有后端，不部分提交。

## 1. 准入

准入在每次 `Solve()` 分派时执行，输入是根快照分叉出的旧模拟器。结果是"准入"或带稳定原因码的"拒绝"。

拒绝条件（任一成立即拒绝）：

| 原因码 | 条件 |
| --- | --- |
| `MultiPlayer` | 玩家数不是 1 |
| `RootPhase` | 根不在玩家出牌阶段（F1–F3 不支持 `IncludeTurnSetup` 的回合准备根） |
| `PendingChoice` | 根存在待处理选择或执行续接 |
| `ThirdParty` | 存在第三方 gameplay 订阅者、BaseLib 卡牌修饰、未登记模型状态 |
| `UnsupportedCard` 等 | 任一卡牌、附魔、苦难、Power、遗物、药水、球、怪物没有 Flash 定义 |
| `UnsupportedClosure` | 可达内容闭包中有不受支持的项（见下） |
| `UnrepresentableState` | 旧模拟器里存在 Flash 无法表示的状态（未建模的 StateStore 条目、非空的未支持侧表等） |
| `Capacity` | 估计的牌数、Power 数、敌人数超出容量 |
| `UnsupportedRequest` | 本次 `Solve()` 参数含 Flash 尚未支持的选项（见 [搜索与接入](search.md#3-请求参数)） |

**可达内容闭包**：根里的内容加上它们可能生成的内容，递归求闭包。每个定义声明自己可能生成什么：固定卡表（`GeneratedCards`）、候选卡池（`GeneratedCardPools`，键见 `FlashCardPools`）、可施加的 Power（`AppliedPowers`，用于跟随 CalamityPower 这类会继续生成卡的 Power）以及随机药水池（`GeneratesRandomPotion`）。闭包从根的战斗牌堆、Power、遗物、药水、已知敌人与召唤闭包中的怪物定义出发，而不是从全部注册定义出发；这样不含生成效果的根不会被无关池拒绝。

池在根导入时用现有引擎的同一套候选枚举冻结进 `FlashRules`（角色/无色卡池、`character.powers`（WhiteNoise/CreativeAiPower 的 Power 卡池）、战斗过滤、多人约束、类型/费用/排除谓词，保持原顺序）；内核只按 RNG 取样。闭包里每个候选卡都必须有 Flash 定义且其规范数值进入规则表，每个随机药水候选都必须有 Flash 药水定义；否则该根按 `UnsupportedClosure` 拒绝，诊断里带上第一个缺失项与池键。召唤表的怪物定义也参与同一闭包，它们生成的卡同样需要定义。

怪物的可生成类型在 `FlashRulesCompiler` 中编译：每个类型在根模拟器的私有 Fork 上执行现有引擎的创建路径（`MonsterSpawnSupport.Create` + `AddPredictedMonster` + `PreparePredictedMonster`），状态机、静态值与初始私有整数都从这次创建结果读取，不手抄。规则表容量是 `FlashLimits.MonsterRules`，超出按 `Capacity` 拒绝。根持有 `Murderous` 修饰而闭包非空时拒绝（`UnsupportedClosure`，新敌人的入场力量未移植）；贤者之石、毛皮大衣没有 Flash 遗物定义，持有它们的根在导入阶段按 `UnsupportedRelic` 拒绝。

准入只做判断，不修改任何状态。拒绝原因写入诊断，用于覆盖率统计。

## 2. 根导入与投影

`FlashStateImporter` 把旧模拟器的状态投影成 `FlashBattle`，同时编译 `FlashRules`。同一个投影函数用于两处：

- 运行时：投影搜索根。
- 测试时：投影旧引擎每一步的结果，与 Flash 的结果比较。

投影必须完整：旧模拟器里每一项会影响后续结算的值都要么映射到 `FlashBattle`，要么在准入中被拒绝。读取旧引擎内部状态（包括原版克隆模型的私有字段）只发生在 Bridge 中。

选择不需要投影：`FlashChoiceTranslator` 用旧引擎逐次回放带选择的动作，差分探针同时比较每个选择请求的效果、来源、时机、数量与同序选项（卡牌 Id 与升级），K1 对每个动作展开全部选择组合（每个动作至多 64 个），K2 随机挑合法选择。

模型私有状态（`FlashStateProjections`）：`PredictionStateStore` 的每个条目都必须被某个投影认领（按 Power/遗物写入 `FlashPower.Aux*` 或遗物计数，值取条目，没有条目时取条目构造器会读取的模型数据），或被证明无效；其余条目一律拒绝。无效条目有下列几类：等于当前 Power 数量的 `PowerAmountPredictionState`（同步时只会被删除）；敌方生物离开战斗后其 Power 的任何条目（现有引擎只经由仍在战斗中的生物身上的 Power 读取条目，离场的敌人不会回来）；以及**退役条目**——Power 归零移除后残留、且其值已不可能再被任何结算读取的条目。新增投影与对应 Flash 定义一起提交，差分探针逐步比较投影值。

P16 新增的投影：实例型 Power 每个原生实例导入为一行，`AutomationPower.Aux0` 取 `AutomationPredictionState.CardsLeft`（无条目时取 `DisplayAmount`），`PanachePower.Aux0/Aux1` 取 `PanachePredictionState.CardsLeft/AlreadyApplied`（无条目时由模型构造同类型状态读取），`TheBombPower.Aux0` 取 `DynamicVars.Damage`；`RollingBoulderPower` 只有 Amount。`_addedPowerInstances`/`_rootMultiInstancePowerClones` 不再整体拒绝，因为 `EffectivePowers()` 已给出每个实例，导入按监听顺序写行。`_returnToHandNextTurn` 按原牌对象投影到 `FlashCard.ReturnToHandNextTurn`。`GigantificationPower` 的私有 `State.CommandToModify` 在稳定边界必须为空，活着的实例由 `ProjectPower` 认领；最后 1 层结算后模型归零、条目仍空闲残留，按惰性处理（读取点只遍历正层 `EffectivePowers`，重新获得是新的原生实例）。

DF 新增的球与卡牌私有状态投影：

- `SimOrbQueue.Orbs` 按顺序导入 `FlashBattle.Orbs`，容量写入 `OrbCapacity`；每个球的原版 Id 必须有 Flash 定义，否则 `UnsupportedOrb`。DarkOrb 的 `_evokeVal` 与 GlassOrb 的 `_passiveVal` **没有** `PredictionStateStore` 条目：模拟器克隆球模型并在克隆上改值，导入经 `OrbMirrors.GetStoredValueForFlash` 读取模型字段（Bridge 内唯一的读取点）并写入 `FlashOrb.Aux`。非整数/越界值拒绝为 `UnrepresentableState`，不四舍五入。容量超 10 或球数超容量按 `Capacity` 拒绝。
- `LightningChannels` 取 `CalculatedVarSpecRegistry.CountLightningChannelsForFlash`（原版历史 + 预测历史）；`OrbChannels` 取预测历史中 `CombatPredictionOrbChanneledEntry` 的数量。二者都只增不减。
- Claw 与 GeneticAlgorithm 的每卡累积值（`ExtraDamageFromClawPlays` / `IncreasedBlock`）在原版模型里是私有字段，但等于导入卡牌相对规则表基值的整数变量差。`FlashCardDef.ProjectPrivate` 在 Bridge 计算 `Var0/Var1` 之后把该差写进 `FlashCard.Private`；对应定义在 `AfterDowngraded` 中把它加回 `Var0`，与 `DowngradeCard` 重置变量、原版 `AfterDowngraded` 重新加成一致。
- 满槽充能先激发最左侧球；若激发副作用同步充能又把槽填满，现有引擎记录 `MethodMirrorIncomplete` 风险并放弃本次预测。Flash 明确抛 `FlashUnsupportedException`，不追加激发。预测历史充能达到 1000 次同样按未支持边界拒绝。
- `ReptileTrinket` 的 `AfterPotionUsed` 在药水效果之后、Power 数量结算与敌人死亡结算之前派发：玩家存活时按遗物变量施加 `ReptileTrinketPower`（临时力量 + 力量）。`UnsettlingLamp` 仍无定义：它依赖卡牌实例身份、`BeforePowerAmountChanged` 与 `ModifyPowerAmountGivenMultiplicative` 的逐卡翻倍账本。

退役条目的认领规则与现有实现：

- `SurroundedPower`（KAISER_CRAB_BOSS）：玩家死亡时 `HandlePlayerDeath` 清空玩家 Power，`SurroundedPredictionState` 仍按退役的 Power 实例留在 store。`Facing` 只被 `BeforeCardPlayed` 与 `ModifyDamageMultiplicative` 读取，而这两个入口都只在玩家存活、Power 仍为正层时遍历 `EffectivePowers`；退役实例不可能再被遍历，因此按惰性处理（`RequireAllClaimed` 放过 `SurroundedPower { Amount: 0 }` 的 `SurroundedPredictionState`）。存活时其值由 `ProjectPower` 认领并写入 `FlashPower.Aux0`。
- `CurlUpPower`（LOUSE_PROGENITOR_NORMAL）：触发后 Power 自身归零移除，store 条目保留 `Consumed`/已清空的 `PlayedCard`。它的两个 Hook（`AfterDamageReceived`、`AfterCardPlayed`）只访问正层的有效 Power，重新获得的 Power 是新的原生实例，旧条目不会再被读；因此 `CurlUpPower { Amount: 0 }` 的 `CurlUpPredictionState` 按惰性处理。
- 玩家死亡后的退役 Power 状态：`HandlePlayerDeath` → `RemovePowersAfterDeath` 把玩家 Power 数量归零并移除，但 `DarkEmbracePredictionState`、`JugglingPredictionState`、`HellraiserPredictionState`、`RupturePredictionState` 与 `CardPlayPairPredictionState` 仍留在 store。它们的读取点都是该 Power 自己的 Hook，只遍历正层的 `EffectivePowers`；`CardPlayPair` 的全局清理只删除进行中的 `CardPlay` 键。玩家已死亡，后续不会再有出牌、抽牌或回合阶段，因此这些条目按惰性处理。
- 临时星能费用：`CardModel._temporaryStarCosts` 可以有多条（Bullet Time 对同一张手牌重复生效）。`CurrentStarCost` 只读最后一条，清理按过期类别整类移除；全部条目（费用与两个清除标志）相同时投影成 `FlashCard` 的单条字段，不同时按 `Capacity` 拒绝（移除一条会暴露更早的值，Flash 无法表示）。
- `AfterimagePower` / `StranglePower` 在 `BeforeCardPlayed` 把能力层数写进 `CardPlayPairPredictionState`（以 `CardPlay` 为键），由 `AfterCardPlayed` 删除。稳定边界为空：`ProjectPower` 认领空条目，非空条目按 `UnrepresentableState` 拒绝（结算进行中）。Power 归零退役后残留的空条目按惰性处理——它的两个读取点都只遍历正层 `EffectivePowers`，重新获得该 Power 是新的原生实例与新的 state；退役条目的非空表仍拒绝。
- `_tenderCardsPlayed`（HUNTER_KILLER_NORMAL）：不是 store 条目，而是 `SimulatedCombatState` 的分支字典。`TenderPower` 正层时由 `ProjectPower` 通过 `GetTenderCardsPlayed` 写入 `FlashPower.Aux0`（有字典条目取条目，否则取条目构造器会读的原生字段）。玩家死亡清空 Power 后字典可能留下非零值，但 `GetTenderCardsPlayed` 只在正层 `TenderPower` 的 Hook/指纹读取，退役值不可能再被读，故 `DescribeFlashUnrepresentableState` 不再整体拒绝该表。

`FlashKernel.ApplyEnemyDeathPowers` 在清空死亡 Power 之前按 `FlashPowerDef.DeathTrigger` 执行 `DeathPowerSupport.Trigger` 的死亡分支（Reattach、Infested、Surrounded 的朝向更新）；Reattach 段的导入允许 `Reviving` 阶段配原生 `DEAD_MOVE`/`REATTACH_MOVE`，只有幻象型怪物要求 `Reviving` 必须处于合成 `REVIVE_MOVE`。

怪物私有整数（`_monsterIntStates`）走同一条认领规则：`FlashStateProjections.ProjectMonster` 按已知敌人的 `FlashMonsterDef.AiRegisterNames` 把值写入 `FlashEnemy.Ai`，并按 `InertMonsterIntNames` 认领只被状态指纹/续接文本读取的条目（目前是乐加维林族母与沉睡甲虫的 `_isAwake`）。寄存器取值调用现有 `GetMonsterInt`，即优先捕获条目、没有条目时读模型字段，所以惰性条目与缺失条目的投影相同。其余条目一律按 `UnrepresentableState` 拒绝；`DescribeFlashUnrepresentableState` 不再整体拒绝 `_monsterIntStates`。A3 的 `AiRegisterNames` 另外覆盖 Fabricator 的 `fabricator_last_spawn`（走 `GetFabricatorLastSpawn` 的模型字段映射）、TestSubject 的 `_respawns`/`_extraMultiClawCount`/`SecondFormHp`/`ThirdFormHp`，以及 Aeonglass 的 `AdditionalStrength`/`WitherUpgradeCount`（`GetMonsterIntForFlash` 对 Aeonglass 走 `ReadAeonglassCounter`：有分支字典条目取条目，否则读模型字段）。

A3 的分支字典投影与退役规则：

- **Aeonglass 计数器**：`_aeonglassAdditionalStrength`/`_aeonglassWitherUpgradeCount` 的每个键必须是已知的 Aeonglass，否则拒绝；值经 AI 寄存器投影。`NormalizeAeonglassWithers` 不读字典之外的状态，Wither 的伪升级只用卡牌 Damage 变量偏移表示（每级 +3，Wither 无其他变量来源）。
- **Dampen**：`_dampenCasters` 投影为 `FlashCounter.DampenCasterMask`（每个 (玩家, 施法者) 一位），`_dampenOriginalUpgrades` 投影为每张卡的 `DampenOriginalUpgrade = 原等级 + 1`。每个施法者/卡牌条目都必须能在已知敌人/当前牌堆中找到，否则拒绝；最后施法者死亡时由卡牌字段恢复等级。
- **Possess**：`_stolenStrength`/`_stolenDexterity` 的键必须是已知敌人与玩家，值投影到对应 Possess Power 的 Aux0；没有条目即构造器的 0。Power 死亡时按该值返还，条目随现有引擎的返还删除。
- **Nemesis**：`_nemesisShouldApplyIntangible` 按生物投影到 `NEMESIS_POWER.Aux0`；键必须是已知敌人。Power 归零后条目仍按生物保留，重新获得同一 Power 时读同一值，因此不是丢弃状态。


**退役状态的玩家死亡前提**：上面的惰性规则要求 `model.Amount == 0`、`model.Owner.Player` 存在且该玩家生物在模拟状态里已经死亡；存活玩家的退役 Power 状态仍被拒绝，不把“暂时归零但仍可能被读”的情况放宽。

**卡牌苦难与附魔投影（KA）**：`FlashStateImporter.Card` 读取 `Preview.Affliction`（Id、Amount）与 `Preview.Enchantment`（Id、Amount、Status、私有值），只接受 `FlashRegistry` 中已登记的苦难/附魔，其余按 `UnsupportedAffliction`/`UnsupportedEnchantment` 拒绝；Id 以 `+1` 编码，0 表示无。附魔私有值复用 `EnchantmentStateSupport.TryGetPrivateValue`（`Glam._usedThisCombat`、`Momentum._extraDamage`，与状态指纹同一读取口径），桥接层不新增反射。根卡牌的 `PowerAfflictionChecked` 置位，表示它们已参与现有引擎的一次性进场检查。`SimulatedCombatState.LastNormalizedVitalSparkAmountForFlash` 是只读访问器，导入 `VitalSparkNormalizedAmount`（`int.MinValue` 表示 null）；`SkillPlayedThisSideTurn` 从 `HasPlayedSkillThisTurn` 导入。已实现 18 个附魔：Sharp、Instinct、Corrupted、Momentum、Nimble、Goopy、TezcatarasEmber、Vigorous、Adroit、Inky、Sown、Swift、Glam、Spiral、Clone、Steady、RoyallyApproved、SoulsPower；Imbued、Slither、SlumberingEssence、PerfectFit 仍按未支持附魔拒绝（分别需要自动预打出、抽牌随机费用、BeforeFlush、按实例的洗牌顺序）。

投影只读不写：旧引擎的本回合计数读取器首次读取时会从根历史填充缓存，因此 `FlashStateImporter` 先 Fork 一份私有模拟器再读取，调用方持有的状态（可能正被其他工作线程 Fork）保持不变。

## 3. 差分测试

**等价定义**：对任意准入状态 `s` 和合法动作 `a`，

```
Project(Old.Step(s, a)) == Flash.Step(Project(s), a)     （逐字节）
```

这里 `Old.Step` 走现有搜索使用的同一条动作管线（`CombatBeamSolver.Replay` 的出牌、药水、结束回合路径）。

测试层次（对应 AGENTS.md 第 8 节）：

| 层 | 内容 | 位置 |
| --- | --- | --- |
| K0 纯内核 | RNG 对照录制向量、布局尺寸断言、规范化、容量失败、选择协议 | `tools/testing/checks/FlashKernelChecks/`，源码包含内核文件，不依赖游戏 |
| K1 单步穷举 | 从根出发，枚举全部合法动作（含选择分支）各执行一步并比较 | 离线搜索宿主的差分探针（`OFFLINE_HARNESS_FLASH_DIFF=1`，`tools/search/OfflineSearchHarness/FlashDifferentialProbe.cs`） |
| K2 随机游走 | 固定种子，多回合随机合法动作序列，每步比较；覆盖洗牌、敌人回合、死亡 | 同上 |
| K3 内容定向 | 每个新内容至少一个 fixture，让该内容在前几步内被触发 | `coverage/fixtures/` 下按主题存放，K1/K2 消费 |
| K4 评估一致 | K1/K2 中投影一致的每个状态，`FlashScorer` 的评估值必须等于现有引擎同一状态的 `SimulationSnapshot` 对应成员；`Score` 按 double 精确比较。药水计数除 `PotionUseCount`/`PotionStrategicCost`/`AutomaticPotionUseCount`/`ExplicitPotionStrategicCost` 外还逐字段比较 `ExplicitPotionUseCount` | 同一探针；字段表与比较在 `FlashScoreFeatures`、`FlashScoreProjection`。现有引擎停在搜索边界的状态（`BoundaryReason` 非 None）不比较 |
| K5 下界一致 | K1/K2 中投影一致的每个状态，`FlashBoundFeatures` 的 `FutureHealPotential`、`HasRisk`、`RemainingHealingPotential`、`FutureHealUsed`、`CumulativePlayerHpLost`、`RecoveredPlayerHp`、`PlayerHp`、`PlayerMaxHp`、`DeathSaveHpRestored`、`ExplicitPotionStrategicCost` 与 `StrategicHpLowerBound` 必须等于现有主搜索成员同一状态的取值。下界无法精确移植的根整体禁用剪枝，汇总行写 `bound=disabled(<原因>)` | 同一探针；比较在 `FlashBoundFeatures.Diff`。现有侧只读访问器在 `CombatBeamSolver.Flash.cs`（`PrimaryIncumbentRemainingHealingPotential`、`PrimaryIncumbentFutureHealPotential`、`PrimaryIncumbentHpLowerBound`） |

复跑命令与当前证据见 [测试记录](testing.md)。

比较失败时输出第一处差异的字段路径（例如 `Enemies[1].Powers[VULNERABLE].Amount: old=2 flash=3`）、动作序列与种子，便于直接复现。

K4 让 FlashBeam 的排序依据与现有搜索相同：同一状态得到同一分数，路线质量的差别只来自搜索结构本身。威胁投影的 `ProjectedPlayerHp` 在玩家已死亡时仍按现有引擎的 run-listener 表读取遗物/卡牌修正（例如 Tungsten Rod 的每击减伤）；Flash 的评分探针在只读副本上打开 `PlayerHooksActive`，不改变被评估状态。K5 让 incumbent 下界剪枝的输入与现有主搜索成员相同：同一状态得到同一 `StrategicHpLowerBound` 与 `costClosed`/`pruneEqualHp` 依据；汇总行的 `bound=checked boundChecked=N boundFailed=0` 表示已比较，`bound=disabled(<原因>)` 表示该根显式关闭剪枝。

差分测试只证明"与现有引擎一致"。现有引擎与原版是否一致由既有的 actual/simulated 严格差分保证，不在这里重复。

## 4. 运行时回放核对

Flash 选出的路线在采用前由旧引擎从同一根逐步回放（`FlashRouteMaterializer`，见 [搜索与接入](search.md#2-路线物化)）：每一步旧引擎状态的投影必须与 Flash 状态逐字节一致。

- 任何一步投影不一致、选择对不上，或旧引擎到达 Flash 无法表示的状态（导入拒绝），都抛 `FlashVerificationException`，消息以 `FLASH_VERIFY_MISMATCH step=` 开头并带首个分歧步序。整条 Flash 路线被丢弃：On 模式回落旧后端，Shadow 模式记为 `status=mismatch`。
- Flash 搜索自身到达内核不建模的状态（`FlashUnsupportedException`）或容量上限（`FlashCapacityException`）时同样回落旧后端，记 `status=boundary`。差分探针把这类情况计入汇总行末的 `unsupported=N` 并在报告中写出首条消息，不当作分歧。当前明确边界之一是 Thrash：现有引擎把 `ModifyDamage` 的小数结果写进卡牌 `DynamicVars.Damage`，而 `FlashCard` 的语义变量是整数；没有可以精确复现的整数化/取整规则，因此该路径抛 `FlashUnsupportedException`，不截断、不近似。
- 核对通过的路线作为固定前缀交给旧求解器物化，结果形状与旧后端相同。
- Smart 药水审计（`SmartPotionGradient` 与 `SmartOpeningPotionPosterior`）在物化时把同一 `PotionFreePolicyBaseline`、`minimum/maximumPotionUses` 与 `RequireAtLeastOne` 交回现有 `CombatBeamSolver`，由现有 `FinalPlanOrdering.Select` 再做一次筛选。物化后 `FlashPotionBridge.VerifyRoute` 核对路线的显式用药次数：动作里的 `UsePotion` 数必须等于 `PotionUses` 的非自动计数，且满足最小/最大次数与 `RequireAtLeastOne` 至少一次；任一不满足都返回 `ConstraintFailure` 回落，不把 Flash 的“没找到”写成现有求解器的 `PotionPolicyUnsatisfiedException`。现有终局筛选自己抛出的 `PotionPolicyUnsatisfiedException` 也在物化点捕获并回落。
- 固定前缀可以由药水动作开头：前缀先经现有 `ApplyFixedPrefix` 回放，Flash 只从回放后的搜索根展开；搜索根快照的累计药水计数冻结进评分与下界，因此前缀用药不丢失。

## 5. 失败处理约束

- 不新增 `catch (Exception)`。只捕获 `FlashUnsupportedException`、`FlashCapacityException`、校验失败、取消，以及物化阶段现有求解器抛出的 `PotionPolicyUnsatisfiedException`。
- 捕获后整个 Flash 求解作废并回落，不把半成品路线交给协调器。
- 药水审计没有满足策略的路线（`PotionPolicySatisfied=false`）、物化路线次数不符、或精确层下界输入不可用（`smart-potion-bound unavailable`）时同样回落，诊断行写 `status=boundary` 与稳定原因；不把 Flash 搜索面较小解释为现有搜索无解。
- 回落本身计入诊断：原因、阶段、根摘要。
