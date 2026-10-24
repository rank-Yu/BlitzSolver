# Flash 准入与校验

[返回 Flash 入口](README.md)

Flash 的正确性由三道关口保证：搜索前的**准入**、开发期的**差分测试**、部署前的**旧引擎回放核对**。开启模式不做部分提交：无路线按无路线约定返回现有搜索同形结果；`admission-unsupported-content` 与 `admission-unrepresentable-state` 两类准入让**整次协调器搜索**换用常规档位从头重跑；其余准入拒绝、核对不一致和内核边界写稳定失败事件并停止本次搜索/部署（离线与无人测试为失败），不回落现有后端。关闭模式不运行 Flash。

## 1. 准入

准入在每次 `Solve()` 分派时执行，输入是根快照分叉出的旧模拟器。结果是"准入"或带稳定原因码的"拒绝"。

拒绝条件（任一成立即拒绝）：

| 原因码 | 条件 |
| --- | --- |
| `MultiPlayer` | 玩家数不是 1 |
| `RootPhase` | 根不是受支持的两形：普通 `Player/Play`，或 `IncludeTurnSetup` 的 `Player/Start`（回合准备根由 Bridge 先回放成 `Play` 状态再导入；其它 side/phase 显式拒绝） |
| `PendingChoice` | 根存在待处理选择或执行续接 |
| `UnsupportedCard` 等 | 任一卡牌、附魔、苦难、Power、遗物、药水、球、怪物没有 Flash 定义 |
| `MultiPlayer` / `TestContent` / `DeprecatedContent` | 明确不在单人原版范围的内容类别（多人专用 37 张卡与 6 个 Power、`MockCardPool`/`MOCK_*` 测试内容、`DeprecatedCardPool` 废弃内容），见下节 |
| `UnsupportedClosure` | 可达内容闭包中有不受支持的项（见下） |
| `UnrepresentableState` | 旧模拟器里存在 Flash 无法表示的状态（未建模的 StateStore 条目、非空的未支持侧表等） |
| `Capacity` | 估计的牌数、Power 数、敌人数超出容量 |
| `UnsupportedRequest` | 本次 `Solve()` 参数含 Flash 尚未支持的选项（见 [搜索与接入](search.md#3-请求参数)） |

开启模式下，只有两类准入拒绝会让**整次协调器搜索**改用常规搜索：未定义内容（`admission-unsupported-content`）与不可表示状态（`admission-unrepresentable-state`）。`BeamSolveDispatch` 抛 `FlashFallbackRequiredException`，`CombatSearchCoordinator.Solve` 只捕获这一个异常类型，确认这次 Flash 尝试还没有发布可采用路线、路线采纳种子或路线预览后，写一行 `FLASH_FALLBACK reason=<类别> detail=<第一个不支持的内容或状态> purpose=<用途>`，并用 `FlashMode = Off`、`Profile = RegularProfile` 从头重跑整次搜索；新尝试新建自己的请求时钟与预算账本，使用同一个 `cancellationToken`。不停止部署、不关闭全自动、不导出失败问题包。`RegularProfile` 由 `SolverSettings.ApplyFlashPerformance` 保存，缺少时显式失败。准入只取决于根内容（含生成闭包），实际回落都发生在第一个 Flash 请求、发布任何结果之前；如果发布检查判定已经发布，则按原失败约定写 `FLASH_SEARCH_FAILED` 并抛 `FlashSearchFailedException`，不重跑。Runtime 每场战斗最多提示一次内容名称，并把类别与内容记入问题包；详见 [开启模式的改用常规搜索](search.md#32-开启模式的改用常规搜索无路线与失败处理)。

其余准入拒绝仍不是回落点：分发点按类别写一行 `FLASH_SEARCH_FAILED reason=admission-<类别> detail=<拒绝原因>`，抛出 `FlashSearchFailedException` 并停止部署。类别覆盖多人专用（`admission-multiplayer`）、Mock 测试内容（`admission-test-content`）、废弃内容（`admission-deprecated-content`）、未实现请求（`admission-unsupported-request`）、容量估计（`admission-capacity`）与待处理选择（`admission-pending-choice`）。第三方内容没有单独类别：未定义的卡牌、Power、遗物、药水、球、怪物、附魔、苦难与生成闭包按 `UnsupportedCard`/`UnsupportedPower`/`UnsupportedRelic`/`UnsupportedPotion`/`UnsupportedOrb`/`UnsupportedMonster`/`UnsupportedEnchantment`/`UnsupportedAffliction`/`UnsupportedClosure` 拒绝，统一写成 `admission-unsupported-content` 并走改用常规搜索；第三方 gameplay 订阅者或未登记模型留下无法投影的状态时按 `UnrepresentableState` 写成 `admission-unrepresentable-state`，同样改走常规搜索。原先的 `admission-third-party` token 已删除。

注意区分“准入的不可表示状态”和“搜索开始后旧引擎回放到达不可表示状态”：后者是 `FlashVerificationException`（`verify-mismatch`）或内核边界，仍按显式失败处理，不会改走常规搜索。

**可达内容闭包**：根里的内容加上它们可能生成的内容，递归求闭包。每个定义声明自己可能生成什么：固定卡表（`GeneratedCards`）、候选卡池（`GeneratedCardPools`，键见 `FlashCardPools`）、可施加的 Power（`AppliedPowers`，用于跟随 CalamityPower 这类会继续生成卡的 Power）以及随机药水池（`GeneratesRandomPotion`）。闭包从根的战斗牌堆、Power、遗物、药水、已知敌人与召唤闭包中的怪物定义出发，而不是从全部注册定义出发；这样不含生成效果的根不会被无关池拒绝。

池在根导入时用现有引擎的同一套候选枚举冻结进 `FlashRules`（角色/无色卡池、`character.powers`（WhiteNoise/CreativeAiPower 的 Power 卡池）、战斗过滤、多人约束、类型/费用/排除谓词，保持原顺序）；内核只按 RNG 取样。闭包里每个候选卡都必须有 Flash 定义且其规范数值进入规则表，每个随机药水候选都必须有 Flash 药水定义；否则该根按 `UnsupportedClosure` 拒绝，诊断里带上第一个缺失项与池键。召唤表的怪物定义也参与同一闭包，它们生成的卡同样需要定义。

怪物的可生成类型在 `FlashRulesCompiler` 中编译：每个类型在根模拟器的私有 Fork 上执行现有引擎的创建路径（`MonsterSpawnSupport.Create` + `AddPredictedMonster` + `PreparePredictedMonster`），状态机、静态值与初始私有整数都从这次创建结果读取，不手抄。行动的伤害与连击数取根上采集的静态值；原版意图用运行时值计算、内核钩子又会按实时状态调整的行动，编译时只取基础值，由钩子加一次实时部分：TestSubject MULTI_CLAW 的连击数取 `BaseMultiClawCount`，`_extraMultiClawCount` 只由 `ModifyAttackRepeats` 计入。原版全部攻击意图中，只有 TestSubject MULTI_CLAW、TheForgotten DREAD 与 WaterfallGiant PRESSURE_GUN 由运行时值计算；后两者在根上不会重复计入。规则表容量是 `FlashLimits.MonsterRules`，超出按 `Capacity` 拒绝。根持有 `Murderous` 修饰而闭包非空时拒绝（`UnsupportedClosure`，新敌人的入场力量未移植）；贤者之石（`PHILOSOPHERS_STONE`）与毛皮大衣（`FUR_COAT`）已有 Flash 遗物定义，不再按 `UnsupportedRelic` 拒绝。

准入只做判断，不修改任何状态。拒绝原因写入诊断，用于覆盖率统计。

`Player/Start` 的 `IncludeTurnSetup` 根不直接导入：分发点把它交给 Flash，Bridge 用现有求解器的 `BuildTurnSetupRoots` 按与现有搜索相同的选择预算枚举准备选择，并把每个分支回放到稳定 `Player/Play` 状态；`FlashStateImporter.Import(..., requirePlayPhase: true)` 只检查这些导入后的 `Play` 根。其余 side/phase 在开启模式写 `FLASH_SEARCH_FAILED reason=root-phase detail=root-phase:<side>:<phase>` 并停止部署，不搜索也不回落。根捕获与准备选择回放都在主线程现有引擎上完成，Flash 只消费 `Play` 状态。

## 2. 根导入与投影

`FlashStateImporter` 把旧模拟器的状态投影成 `FlashBattle`，同时编译 `FlashRules`。同一个投影函数用于两处：

- 运行时：投影搜索根。
- 测试时：投影旧引擎每一步的结果，与 Flash 的结果比较。

投影必须完整：旧模拟器里每一项会影响后续结算的值都要么映射到 `FlashBattle`，要么在准入中被拒绝。读取旧引擎内部状态（包括原版克隆模型的私有字段）只发生在 Bridge 中。

选择不需要投影：`FlashChoiceTranslator` 用旧引擎逐次回放带选择的动作，差分探针同时比较每个选择请求的效果、来源、时机、数量与同序选项（卡牌 Id 与升级），K1 对每个动作展开全部选择组合（每个动作至多 64 个），K2 随机挑合法选择。

模型私有状态（`FlashStateProjections`）：`PredictionStateStore` 的每个条目都必须被某个投影认领（按 Power/遗物写入 `FlashPower.Aux*` 或遗物计数，值取条目，没有条目时取条目构造器会读取的模型数据），或被证明无效；其余条目一律拒绝。无效条目有下列几类：等于当前 Power 数量的 `PowerAmountPredictionState`（同步时只会被删除）；敌方生物离开战斗后其 Power 的任何条目（现有引擎只经由仍在战斗中的生物身上的 Power 读取条目，离场的敌人不会回来）；以及**退役条目**——Power 归零移除后残留、且其值已不可能再被任何结算读取的条目。新增投影与对应 Flash 定义一起提交，差分探针逐步比较投影值。

实例型 Power 与相关卡值投影：实例型 Power 每个原生实例导入为一行，`AutomationPower.Aux0` 取 `AutomationPredictionState.CardsLeft`（无条目时取 `DisplayAmount`），`PanachePower.Aux0/Aux1` 取 `PanachePredictionState.CardsLeft/AlreadyApplied`（无条目时由模型构造同类型状态读取），`TheBombPower.Aux0` 取 `DynamicVars.Damage`；`RollingBoulderPower` 只有 Amount。`_addedPowerInstances`/`_rootMultiInstancePowerClones` 不再整体拒绝，因为 `EffectivePowers()` 已给出每个实例，导入按监听顺序写行。`_returnToHandNextTurn` 按原牌对象投影到 `FlashCard.ReturnToHandNextTurn`。`GigantificationPower` 的私有 `State.CommandToModify` 在稳定边界必须为空，活着的实例由 `ProjectPower` 认领；最后 1 层结算后模型归零、条目仍空闲残留，按惰性处理（读取点只遍历正层 `EffectivePowers`，重新获得是新的原生实例）。

球与卡牌私有状态投影：

- `SimOrbQueue.Orbs` 按顺序导入 `FlashBattle.Orbs`，容量写入 `OrbCapacity`；每个球的原版 Id 必须有 Flash 定义，否则 `UnsupportedOrb`。DarkOrb 的 `_evokeVal` 与 GlassOrb 的 `_passiveVal` **没有** `PredictionStateStore` 条目：模拟器克隆球模型并在克隆上改值，导入经 `OrbMirrors.GetStoredValueForFlash` 读取模型字段（Bridge 内唯一的读取点）并写入 `FlashOrb.Aux`。非整数/越界值拒绝为 `UnrepresentableState`，不四舍五入。容量超 10 或球数超容量按 `Capacity` 拒绝。
- `LightningChannels` 取 `CalculatedVarSpecRegistry.CountLightningChannelsForFlash`（原版历史 + 预测历史）；`OrbChannels` 取预测历史中 `CombatPredictionOrbChanneledEntry` 的数量。二者都只增不减。
- Claw 与 GeneticAlgorithm 的每卡累积值（`ExtraDamageFromClawPlays` / `IncreasedBlock`）在原版模型里是私有字段，但等于导入卡牌相对规则表基值的整数变量差。`FlashCardDef.ProjectPrivate` 在 Bridge 计算 `Var0/Var1` 之后把该差写进 `FlashCard.Private`；对应定义在 `AfterDowngraded` 中把它加回 `Var0`，与 `DowngradeCard` 重置变量、原版 `AfterDowngraded` 重新加成一致。
- 满槽充能先激发最左侧球；若激发副作用同步充能又把槽填满，现有引擎记录 `MethodMirrorIncomplete` 风险并放弃本次预测。Flash 明确抛 `FlashUnsupportedException`，不追加激发。预测历史充能达到 1000 次同样按未支持边界拒绝。
- `ReptileTrinket` 的 `AfterPotionUsed` 在药水效果之后、Power 数量结算与敌人死亡结算之前派发：玩家存活时按遗物变量施加 `ReptileTrinketPower`（临时力量 + 力量）。药水目标的 `SurroundedPower` 朝向由 `BeforePotionUsed` 的投影更新。`UnsettlingLamp`/`Vambrace` 按实例身份实现（见下）。

实例身份投影与惰性规则：

- **按卡记账**：`_swordSageReplayBonuses` 逐卡写入 `FlashCard.Private`（`_swordSageReplayBonuses` 缺失条目仍按 `UnrepresentableState` 拒绝）；`_fetchCardsPlayedThisTurn` 写入 `FlashCard.Private` 的 bit 0，只在当前牌堆中的卡上生效；`_nightmareSelections` 的选定卡完整值写入 `FlashBattle.DetachedCards`（每行 `NightmarePower.Aux0 = 槽位 + 1`），Power 移除时清空槽位；`_returnToHandNextTurn` 仍投影到卡上的位。离场卡或已移除 Power 的残留条目不再被任何结算读取，按惰性放过（读取点只遍历当前牌堆 / 正层 `EffectivePowers`）。Monologue/Vambrace/UnsettlingLamp 的触发卡只在一次出牌的 `FlashExec` 中存在，稳定边界必须为空，否则按 `UnrepresentableState` 拒绝。
- **实例型 Power**：`MonologuePower`（Strength/StrengthApplied → Aux0/Aux1）、`OrbitPower`（energySpent%4 → Aux0）、`NightmarePower`（detached 槽位 → Aux0）各导入为一行；`PaleBlueDotPower` 的每回合锁存位导入为 Aux0。`_orbitEnergyRemainders`/`_paleBlueDotActivated` 的正层实例缺条目时拒绝，零层实例的残留条目惰性放过。

储君投影与惰性规则：

- **星能**：`FlashBattle.Stars` 直接取 `SimPlayerCombatState.Stars`；无星费的卡 `BaseStarCost` 为 -1，临时星费按最后一条投影（多条目不一致按容量拒绝）；`LastStarsSpent` 只存在出牌期的 `FlashExec`，不投影。
- **星能遗物计数**：`GalacticDust.Counter` 与 `MiniRegent.Counter` 取 `StatefulRelicState.Current`（`SimulatedCombatState.StatefulRelicStateForFlash`，无条目时其构造器读的模型字段）；`Regalite.Counter` 取 `RegalitePredictionState.UsedThisTurn`（无条目时读 `_usedThisTurn`）；`BrilliantScarf.Counter` 取 `CounterPredictionState.Value`（无条目时读 `_cardsPlayedThisTurn`）。四者都只在对应遗物的 Hook 里读写。
- **SwordSagePower**：`NormalizeSwordSageReplays` 为每张君王之刃把「已应用的重放加成」写入 `FlashCard.Private`；根快照内的刀以 `FlashRules.RootSwordSageAmount` 冻结的根层数为基线，根之后由锻造生成的非克隆刀从 0 起算、再由当前层数补差（对应现有引擎 `_swordSageCardsInitialized` 之后的 `rootAmount=0`），克隆从当前层数起算。`ReplayCount` 的每次变化与 `Private` 同步。
- **自动预打出**：`BOMBARDMENT` 的消耗堆捕获、`MayhemPower` 的计划自动打出、HistoryCourse 的上一回合最后攻击复制品与 Imbued 的附魔自动打出在 `FlashKernel.AutoPrePlay` 里按现有引擎的阶段顺序复刻；WhisperingEarring 的触发没有战斗内状态，定义只保留最大能量。
- **历史计数投影**：`CardsGeneratedThisCombat` 取 `CalculatedVarSpecRegistry.CountGeneratedCardsForFlash`（根历史 + 预测历史）；`FlashEnemy.PoweredAttackHitsThisTurn` 取 `GetPoweredAttackHitsThisTurn(player, enemy)`（在导入的私有 Fork 上读取，调用方状态不变），超过 255 按 `UnrepresentableState` 拒绝。
- **`VoidFormPower.Aux0`**：取 `VoidFormPredictionState.CardsPlayedThisTurn`（`Claim` 认领）；没有条目时读模型内部数据的 `cardsPlayedThisTurn`。Power 归零退役后的残留条目按惰性处理：两个费用钩子与 `AfterCardPlayed` 只遍历正层 `EffectivePowers`，重新获得是新的原生实例。
- **生成闭包已覆盖**：`BundleOfJoy`、`Quasar`、`ManifestAuthority`、`SpectrumShift` 的候选池是 `ColorlessAll`，其中 Discovery/JackOfAllTrades/Splash 等又声明 `CharacterAll`；池内实例身份内容 Monologue、SwordSage、Orbit、PaleBlueDot、SummonForth 均已有 Flash 定义。闭包仍按根实际枚举的候选逐个要求定义，缺定义时按 `UnsupportedClosure` 拒绝，不再有整类已知缺口。

退役条目的认领规则与现有实现：

- `SurroundedPower`（KAISER_CRAB_BOSS）：玩家死亡时 `HandlePlayerDeath` 清空玩家 Power，`SurroundedPredictionState` 仍按退役的 Power 实例留在 store。`Facing` 只被 `BeforeCardPlayed` 与 `ModifyDamageMultiplicative` 读取，而这两个入口都只在玩家存活、Power 仍为正层时遍历 `EffectivePowers`；退役实例不可能再被遍历，因此按惰性处理（`RequireAllClaimed` 放过 `SurroundedPower { Amount: 0 }` 的 `SurroundedPredictionState`）。存活时其值由 `ProjectPower` 认领并写入 `FlashPower.Aux0`。
- `CurlUpPower`（LOUSE_PROGENITOR_NORMAL）：触发后 Power 自身归零移除，store 条目保留 `Consumed`/已清空的 `PlayedCard`。它的两个 Hook（`AfterDamageReceived`、`AfterCardPlayed`）只访问正层的有效 Power，重新获得的 Power 是新的原生实例，旧条目不会再被读；因此 `CurlUpPower { Amount: 0 }` 的 `CurlUpPredictionState` 按惰性处理。
- 玩家死亡后的退役 Power 状态：`HandlePlayerDeath` → `RemovePowersAfterDeath` 把玩家 Power 数量归零并移除，但 `DarkEmbracePredictionState`、`JugglingPredictionState`、`HellraiserPredictionState`、`RupturePredictionState` 与 `CardPlayPairPredictionState` 仍留在 store。它们的读取点都是该 Power 自己的 Hook，只遍历正层的 `EffectivePowers`；`CardPlayPair` 的全局清理只删除进行中的 `CardPlay` 键。玩家已死亡，后续不会再有出牌、抽牌或回合阶段，因此这些条目按惰性处理。
- 临时星能费用：`CardModel._temporaryStarCosts` 可以有多条（Bullet Time 对同一张手牌重复生效）。`CurrentStarCost` 只读最后一条，清理按过期类别整类移除；全部条目（费用与两个清除标志）相同时投影成 `FlashCard` 的单条字段，不同时按 `Capacity` 拒绝（移除一条会暴露更早的值，Flash 无法表示）。
- `AfterimagePower` / `StranglePower` / `SerpentFormPower` 在 `BeforeCardPlayed` 把能力层数写进 `CardPlayPairPredictionState`（以 `CardPlay` 为键），由 `AfterCardPlayed` 删除。稳定边界为空：`ProjectPower` 认领空条目，非空条目按 `UnrepresentableState` 拒绝（结算进行中）。Power 归零退役后残留的空条目按惰性处理——它的两个读取点都只遍历正层 `EffectivePowers`，重新获得该 Power 是新的原生实例与新的 state；退役条目的非空表仍拒绝。
- `AmountOnTurnStart` 惰性归一化：现有引擎的镜像扣层只写 `PowerAmountPredictionState`，模型数量要到动作边界 `SynchronizePowerAmountPredictionStates` 才同步，而 `SnapshotPowerAmountsAtTurnStart` 读的是尚未同步的模型数量。Flash 立即扣层，无法区分“当前有效层数”和“模型滞后层数”。只有 `DrawCardsNextTurnPower`、`SummonNextTurnPower`、`HelloWorldPower` 会被结算读取该字段（`PowerLifecycleSupport.SemanticallyRelevantAmountOnTurnStart` 与 `TurnStartPowerSupport`），其余 Power 的 `AmountOnTurnStart` 在导入和快照时都写成 0；这不会改变任何可观察结算，只消除投影差异。
- `BeltBuckle` 的 `StatefulRelicState`：`ProjectRelic` 把它投影到 `FlashRelic.Counter`（0/1），表示本场是否已经给过战后补充的 2 敏捷；`AfterPotionUsed` 在药水槽全空且计数为 0 时施加敏捷并置 1，与 `TriggerRelicsAfterPotionUsed` 的门控一致。战前应用已经在根快照里。
- **CallOfTheVoid 的生成闭包**：Power 声明 `CharacterAll` 池，闭包会要求 Fetch 与 Delay 的 Flash 定义；两者均已实现并登记生成闭包，`flash-id-g-callofthevoid.json` 固定根覆盖该路径。
- `_tenderCardsPlayed`（HUNTER_KILLER_NORMAL）：不是 store 条目，而是 `SimulatedCombatState` 的分支字典。`TenderPower` 正层时由 `ProjectPower` 通过 `GetTenderCardsPlayed` 写入 `FlashPower.Aux0`（有字典条目取条目，否则取条目构造器会读的原生字段）。玩家死亡清空 Power 后字典可能留下非零值，但 `GetTenderCardsPlayed` 只在正层 `TenderPower` 的 Hook/指纹读取，退役值不可能再被读，故 `DescribeFlashUnrepresentableState` 不再整体拒绝该表。

`FlashKernel.ApplyEnemyDeathPowers` 在清空死亡 Power 之前按 `FlashPowerDef.DeathTrigger` 执行 `DeathPowerSupport.Trigger` 的死亡分支（Reattach、Infested、Surrounded 的朝向更新）；Reattach 段的导入允许 `Reviving` 阶段配原生 `DEAD_MOVE`/`REATTACH_MOVE`，只有幻象型怪物要求 `Reviving` 必须处于合成 `REVIVE_MOVE`。

怪物私有整数（`_monsterIntStates`）走同一条认领规则：`FlashStateProjections.ProjectMonster` 按已知敌人的 `FlashMonsterDef.AiRegisterNames` 把值写入 `FlashEnemy.Ai`，并按 `InertMonsterIntNames` 认领只被状态指纹/续接文本读取的条目（目前是乐加维林族母与沉睡甲虫的 `_isAwake`）。寄存器取值调用现有 `GetMonsterInt`，即优先捕获条目、没有条目时读模型字段，所以惰性条目与缺失条目的投影相同。其余条目一律按 `UnrepresentableState` 拒绝；`DescribeFlashUnrepresentableState` 不再整体拒绝 `_monsterIntStates`。`AiRegisterNames` 还覆盖 Fabricator 的 `fabricator_last_spawn`（走 `GetFabricatorLastSpawn` 的模型字段映射）、TestSubject 的 `_respawns`/`_extraMultiClawCount`/`SecondFormHp`/`ThirdFormHp`，以及 Aeonglass 的 `AdditionalStrength`/`WitherUpgradeCount`（`GetMonsterIntForFlash` 对 Aeonglass 走 `ReadAeonglassCounter`：有分支字典条目取条目，否则读模型字段）。

分支字典投影与退役规则：

- **Aeonglass 计数器**：`_aeonglassAdditionalStrength`/`_aeonglassWitherUpgradeCount` 的每个键必须是已知的 Aeonglass，否则拒绝；值经 AI 寄存器投影。`NormalizeAeonglassWithers` 不读字典之外的状态，Wither 的伪升级只用卡牌 Damage 变量偏移表示（每级 +3，Wither 无其他变量来源）。
- **Dampen**：`_dampenCasters` 投影为 `FlashCounter.DampenCasterMask`（每个 (玩家, 施法者) 一位），`_dampenOriginalUpgrades` 投影为每张卡的 `DampenOriginalUpgrade = 原等级 + 1`。每个施法者/卡牌条目都必须能在已知敌人/当前牌堆中找到，否则拒绝；最后施法者死亡时由卡牌字段恢复等级。
- **Possess**：`_stolenStrength`/`_stolenDexterity` 的键必须是已知敌人与玩家，值投影到对应 Possess Power 的 Aux0；没有条目即构造器的 0。Power 死亡时按该值返还，条目随现有引擎的返还删除。
- **Nemesis**：`_nemesisShouldApplyIntangible` 按生物投影到 `NEMESIS_POWER.Aux0`；键必须是已知敌人。Power 归零后条目仍按生物保留，重新获得同一 Power 时读同一值，因此不是丢弃状态。


**退役状态的玩家死亡前提**：上面的惰性规则要求 `model.Amount == 0`、`model.Owner.Player` 存在且该玩家生物在模拟状态里已经死亡；存活玩家的退役 Power 状态仍被拒绝，不把“暂时归零但仍可能被读”的情况放宽。

Knowledge Demon、Automation、Sloth 与 EntropicBrew 的投影与惰性规则：

- **Knowledge Demon 的诅咒计数**：`SimulatedCombatState._knowledgeDemonCurseCounters` 的每个键必须是已知的 KnowledgeDemon；值由该怪物的 `FlashMonsterDef.AiRegisterNames`（`_curseOfKnowledgeCounter`）认领并写入 `FlashEnemy.Ai`。没有分支字典条目时取原版模型字段 `_curseOfKnowledgeCounter`（`FlashStateProjections.GetMonsterIntForFlash` 的 KnowledgeDemon 分支），与现有引擎 `GetKnowledgeDemonCurseCounter` 的“条目优先、否则模型字段”一致。`_monsterIntStates` 中同名条目同样优先。
- **AutomationPower 的退役计数**：`AutomationPredictionState` 的正层实例由 `ProjectPower` 认领到 `FlashPower.Aux0`；Power 归零移除后条目仍按退役模型留在 store。它的读取点（续发能量评估、指纹）只遍历正层 `EffectivePowers`，重新获得是新的原生实例，因此 `AutomationPower { Amount: 0 }` 的该条目按惰性处理。
- **SlothPower 的退役计数**：`CounterPredictionState` 的正层实例由 `ProjectPower` 认领到 `FlashPower.Aux0`（无条目时读原生 `_cardsPlayedThisTurn`）。Power 归零移除后条目仍按退役模型留在 store；它的读取点（`ShouldPlay`、`BeforeCardPlayed`、状态指纹）只遍历正层 `EffectivePowers`，重新获得是新的原生实例，因此 `SlothPower { Amount: 0 }` 的该条目按惰性处理。
- **EntropicBrew 的免费药水**：`_freeEntropicPotions` 的每个 `true` 条目投影为 `FlashCounter.EntropicFreePotionMask` 的对应槽位；使用该槽时清除。`PredictedPotionUse` 的每次使用按“该槽是否仍持有根药水”投影：不是根药水的使用计入 `GeneratedPotionUseCount`，其 `StrategicHpCost` 按自动/显式打包进 `GeneratedPotionStrategicCost`（免费 Entropic 药水的成本已经是 0），补上根药水槽 diff 看不到的 `PotionUseCount` 与成本。`false` 条目是使用后留下的退役标志，没有读取点。
- **随机药水闭包**：随机药水池（战斗内与 EntropicBrew 的战斗外池）的每个候选都要有 Flash 药水定义；候选药水自身的 `GeneratedCards`/`GeneratedCardPools`/`AppliedPowers` 也进入闭包，避免“随机到一瓶会生成卡牌的药水”时缺规则表。闭包冻结后再展开，直到没有新增内容。

小数卡牌变量、BeatDown 与对象身份的投影规则：

- **Thrash 的小数变量**：`FlashStateImporter.Card` 把 `DynamicVars[VarNames[0]].BaseValue − 规则表规范值` 的精确 `decimal` 写进 `FlashBattle.DecimalVars`，卡上 `Private` 存槽位加一；导入结束与每个稳定边界都按“`DetachedCards`、再卡牌存储顺序”规范化槽位并对相同位模式去重。它不经过 `PredictionStateStore`，也不改变其他卡牌的整数 `Var0/Var1`。克隆共享槽位，任一克隆更新时走值集合的写时复制，因此与现有引擎“克隆复制 `ExtraDamage`、之后互不影响”一致；降级保留该偏移，对应 `AfterDowngraded` 把 `ExtraDamage` 加回。K4 的卡牌估值先做 `decimal` 加法再转 `double`，与 `CardChoiceSupport.CardValue` 的取整点相同。
- **BeatDown 的选中卡对象**：现有引擎持有 `CardModel` 引用并对每个选中卡调用 `AutoPlay`；前一张自动打出的摸牌/洗牌/消耗把后续选中卡移到别的牌堆后，旧引擎仍按对象引用从新牌堆打出。Flash 在 `OnPlay` 开始时给每张选中卡写 `TransientMark = 序号 + 1`，每轮按该字节在整个 `Cards` 数组定位并调用 `AutoPlay`；`finally` 清除标记（以及 `DetachedCards` 里的残留），稳定边界字节恒为 0。克隆与生成卡从 0 开始，不能冒充被选中的原对象。`DecisionsDecisions` 的重复自动打出和 Mayhem 的自动预打出队列复用 64..255 的 `TransientMarkBase` 动态区间，按标记跟随卡值移动；标记重复或丢失时显式未支持，确认卡已离开全部战斗牌堆时走 `AutoPlay` 的 no-pile `AddToPile(Play)` 分支。
- **Stoke / RollingBoulder / Calamity 的对象身份**：`Stoke` 原版快照 `CardModel` 对象后逐个 `Exhaust`；Flash 在循环前给快照手牌写 64..255 的 `TransientMark`，按标记在整个 `Cards` 数组定位，`finally` 清除并恢复 `TransientMarkBase`。Dark Embrace 在消耗中抽到的新牌标记为 0，不能冒充快照牌；BeatDown 的 1..63 与 Sly 的 64..255 批次可嵌套。`RollingBoulderPower.AfterPlayerTurnStart` 在伤害前捕获实例的 `(owner,id,occurrence)`，伤害中敌人死亡删除 Power 行后重新定位再 +5，避免旧 `ref` 指向移位后的其它 Power。`CalamityPower` 用 `FlashCardPlayPairKind.CalamityAttacks` 按播放索引栈保存每次 Attack 的待生成量，嵌套自动打出（UPROAR 内 AutoPlay）先取内层、再取外层，不再被单个 `Aux0` 标志吞掉。

偷窃与瀑布巨兽的投影与惰性规则：

- **被偷卡牌**：`SwipePower.StolenCard` 的完整卡值导入 `FlashBattle.DetachedCards`，Power 行 `Aux0 = 槽位 + 1`；THIEVERY 在搜索中从抽牌/弃牌堆移除卡时现场分配槽位（`CardPileCmd.RemoveFromCombat` 的 `HasBeenRemovedFromState` 投影为卡值标志）。击杀飞贼时 `RecoverStolenResources` 回收卡数并随 Power 移除清槽；逃跑只移除 Power、不回收。
- **偷窃账本**：`SimulatedCombatState.UnrecoveredLootForFlash` 只读地取得 `_outstandingStolenGold`/`_outstandingStolenCards`（缓存未建立时扫描存活敌人的 `ThieveryPower.DynamicVars.Gold`、`HeistPower.Amount` 与 `SwipePower.StolenCard`，不写回缓存，避免投影修改调用方状态），写入两个 `FlashCounter`。`ThieveryPower.Aux0` 投影 `DynamicVars.Gold`；`HeistPower` 的金币就是 `Amount`；`RecordThievery`/`RecoverStolenResources` 在核心里维护计数。`_escapedCreatures` 不进入预测状态：它只被战斗后的金币比例读取，`CreatureEscaped` 同时移除了 `_knownEnemies` 与名单，因此非空列表按惰性放行。
- **瀑布巨兽的私有状态**：`_pressureGunBonus`/`_steamEruptionDamage` 的每个键必须是已知 WaterfallGiant，值投影到该敌人的 `PressureGunBonus`/`SteamEruptionDamage` AI 寄存器；`_steamEruptionPhases` 的键同样受限，值投影到 `FlashEnemy.SteamPhase`。死亡改写（HP/最大 HP 钳制、强制 `ABOUT_TO_BLOW_MOVE`、清 Power 并保留 SteamEruptionPower）与 `ABOUT_TO_BLOW`/`EXPLODE` 两个强制阶段由内核按 `TryTriggerSteamEruptionDeath`/`TryConsumeForcedMonsterMove` 的顺序结算；爆发阶段 `EffectiveEnemyHp` 为 0，`PreventsCombatEnding`/`PreventsRemovalAfterDeath` 与阻止战斗结束都按 Power 定义投影。
- **K4**：`FlashScoreFeatures.MinimumOutstandingStolenResource` 逐状态与 `SimulatedCombatState.MinimumOutstandingStolenResource` 比较；`OutstandingStolenResource` 同时进入评分与路线键（仅 `PreserveResources`）。`FlashBoundBridge` 的 `swipe_power` 禁用原因保留：下界的已知来源证书还没有覆盖被偷卡语义。

第三幕牌组投影与惰性规则：

- **Dampen 退役条目**：`_dampenOriginalUpgrades` 的键是 `PredictedCard` 对象。被变化/替换/移出战斗的卡不在任何牌堆中，但记录仍在；`RestoreDampenedCards` 只会升级该对象并删除记录，而 `HasBeenRemovedFromState` 阻止它再被加回牌堆，因此这类条目按惰性放行（无读取点能影响投影状态）。仍在牌堆中的条目必须能找到，否则 `UnrepresentableState`。升级牌的基础等级规则在根上补齐，降级后可读可打。
- **Hellraiser 计数**：`HellraiserPredictionState` 被认领并投影到 `FlashPower.Aux0`；没有条目时取条目构造器会读的模型内部数据 `HellraiserPower.Data.infiniteAutoPlaysThisTurn`。空的可击中敌人集合（测试体复活窗口）走无限分支。
- **Gigantification 的待同步条目**：现有引擎的 `PowerAmountPredictionState` 在动作边界把数量写回模型；玩家在同一动作中死亡时，这一写回会把死亡清理掉的 Power 恢复。Flash 用瞬态 `PendingAmountState` 保留对应行并在稳定边界清标记，不把它写进投影。
- **Axebot 补货规则**：规则表只编译 StockAmount 1 的生成条目；补货时 `hpRangeShift` 在抽取前平移整个生命区间（`RespawnCount * 10`），因此不需要第二份规则条目，也不改变 `FlashBattle`。
- **召唤物初始行动**：`InitialMoveLogged` 由规则表记录状态机构造器写入 `StateLog` 的初始 MoveState；敌方回合内生成时立即采用它，回合提前结束也不会留下 `NeedsInitialRoll`。
- **Rampart 的离场 owner**：`PersistentPowerSupport.TriggerRampart` 只按数量过滤 `EffectivePowers()`，离场敌人的 Rampart 行仍参与；`RampartPass` 与它一致，不使用按名单过滤的监听收集。

**卡牌苦难与附魔投影**：`FlashStateImporter.Card` 读取 `Preview.Affliction`（Id、Amount）与 `Preview.Enchantment`（Id、Amount、Status、私有值），只接受 `FlashRegistry` 中已登记的苦难/附魔，其余按 `UnsupportedAffliction`/`UnsupportedEnchantment` 拒绝；Id 以 `+1` 编码，0 表示无。附魔私有值复用 `EnchantmentStateSupport.TryGetPrivateValue`（`Glam._usedThisCombat`、`Momentum._extraDamage`，与状态指纹同一读取口径），桥接层不新增反射。根卡牌的 `PowerAfflictionChecked` 置位，表示它们已参与现有引擎的一次性进场检查。`SimulatedCombatState.LastNormalizedVitalSparkAmountForFlash` 是只读访问器，导入 `VitalSparkNormalizedAmount`（`int.MinValue` 表示 null）；`SkillPlayedThisSideTurn` 从 `HasPlayedSkillThisTurn` 导入。已实现 22 个附魔：Sharp、Instinct、Corrupted、Momentum、Nimble、Goopy、TezcatarasEmber、Vigorous、Adroit、Inky、Sown、Swift、Glam、Spiral、Clone、Steady、RoyallyApproved、SoulsPower、Imbued、Slither、SlumberingEssence、PerfectFit。

投影只读不写：旧引擎的本回合计数读取器首次读取时会从根历史填充缓存，因此 `FlashStateImporter` 先 Fork 一份私有模拟器再读取，调用方持有的状态（可能正被其他工作线程 Fork）保持不变。

**请求根冻结的规则基线**：`SimulatedCombatState._rootPowerAmounts` 在请求根捕获并随 Fork 复制，`AdjustMonsterMoveDamage` 的基线不随固定前缀移动。`FlashRulesCompiler.Compile(current, player, baseline)` 从 `baseline` 的 `EffectivePowers()` 读取 `FlashMonsterRules.RootPowerAmounts`；`FlashRouteMaterializer` 在固定前缀后传入请求根快照，没有前缀时两者相同。差分探针默认也把当前根当作基线。`FlashDifferentialProbe` 额外支持 fixture 字段 `flashPrefixActions`（`"EndTurn"`，或 `"PlayCard:<卡牌 Id>:<occurrence>[:<目标 CombatId>]"`，按数组顺序推进回合）以及完整计划前缀 `flashPrefixPlan`（`PlanAction` 形状的 JSON 数组）：先在旧引擎上回放该前缀，再从产生的搜索根开始 K1/K2，用来覆盖“前缀改变了冻结基线”或随机游走走不到的深根（`coverage/fixtures/cards/flash-dx4-dread-baseline.json`、`flash-dx5-pen-nib-sly.json`、`flash-hs-chains-turn-reset.json` 的 Skim 抽牌后 EndTurn 前缀）。K4 的相对分比较在根上使用前缀动作数，后续单步仍按一次动作计。

亡灵契约师（Osty、Doom 与灵魂）的投影与惰性规则：

- **Osty**：`FlashStateImporter` 以 `simulator.State.GetOsty(player)` 取宠物（模拟字典优先，否则原生 `player.Osty`），把它的 `CurrentHp`/`MaxHp`/`Block`/本回合失血标记写入 `FlashBattle.Osty` 并置 `OstyPresent`；原生 `player.Osty` 非空时再置 `OstyWasRootPet`（该布尔只影响玩家回合开始的 Osty 攻击计数清零，整场不变）。`_simulatedOsties` 的每个键必须是本场玩家、值必须是 `GetOsty` 当前返回的宠物；`_simulatedOstyMaxHp` 的每个值必须等于 `CombatPredictionState` 中该宠物的 `MaxHp`（`SummonOsty` 同时写两处），否则按 `UnrepresentableState` 拒绝。Osty 的 Power 按 `Owner` 映射到 `FlashLimits.OstyOwner`；死亡宠物保留的 Power 与其他正层 Power 一样投影。
- **历史计数**：`OstyAttacksThisTurn` 取 `SimulatedCombatState.GetCreatureAttacksThisTurn(osty)`（根历史 + 预测历史），`DoomAppliedThisTurn` 取 `WasDoomAppliedThisTurn(player.Creature)`，`EtherealPlays` 取 `GetEtherealPlaysBeforePrediction` 加预测历史中 `CombatCardPlayFinishedEntry.WasEthereal` 的条数。它们都是只增或按现有引擎清零时机重置的普通计数，没有额外条目。
- **Oblivion 的成对状态**：现有引擎把“打出时层数”存在 `CardPlayPairPredictionState`（键为当前 `CardPlay`），由 `AfterCardPlayed` 删除。稳定边界不可能落在一次卡牌效果内部（`_cardExecutionScopeDepth == 0`），因此该 map 在导入时必为空；`FlashStateProjections` 认领该条目并要求 `Amounts.Count == 0`，非空按 `UnrepresentableState` 拒绝。退役的 `OblivionPower` 残留空 map 与 Afterimage/Strangle 相同，按惰性放行（读取点只遍历正层 `EffectivePowers`，重新获得是新的原生实例）。
- **战斗结束**：Osty 死亡不阻止战斗结束；玩家死亡时先清玩家 Power/球，再以 `force` 击杀 Osty，最后关闭玩家侧 Hook，与现有引擎顺序一致。

### 2.1 明确分类的不支持内容与事件内容投影

**显式类别。** 单人原版范围外的卡牌与 Power 不再以笼统的“未定义”拒绝。`FlashUnsupportedContent.Card`/`Power`（`src/Flash/Bridge/FlashUnsupportedContent.cs`）在 `FlashRulesCompiler.CardId` 与 `FlashStateImporter` 的 Power 导入处分类：

- `MultiPlayer`：`CardMultiplayerConstraint.MultiplayerOnly` 的 37 张卡（`BEACON_OF_HOPE`、`BLADE_SYMPHONY`、…、`UNDERWORLD`）与 6 个多人专用 Power（`COORDINATE_POWER`、`FADE_POWER`、`HAMMER_TIME_POWER`、`LEADERSHIP_POWER`、`ONE_FOR_ALL_POWER`、`TANK_POWER`）。这些内容在单人流程不会进入牌组；手动注入的根以该类别拒绝。
- `TestContent`：`MockCardPool` 的卡与 `MOCK_*` Power；`DeprecatedContent`：`DeprecatedCardPool` 的卡。`ModelDb.AllCards` 由 `AllCardPools` 组成，测试池不在其中，因此这两类在正常单机跑局不可达；分类仍是准入路径上的显式类别，不是半成品定义。

**事件卡。** `Content/Cards/Event/` 实现 19 张 Event 稀有度卡、12 张 Basic/Ancient 事件奖励卡与 5 张任务/令牌卡；Event/Ancient 卡不在 `FilterForCombat` 的生成池中，只作为牌组牌/事件奖励出现，`GeneratedCardPools` 只声明它们自身会抽样的角色池（`Distraction`/`Metamorphosis`/`Abundance`/`MadScience` 的 Chaos）。任务卡（`BYRDONIS_EGG`、`DOWSING`、`LANTERN_KEY`、`SPOILS_MAP`）由事件加入牌组、按普通牌组牌洗进战斗牌堆，原生 `CardType.Quest`/不可打出，只有战斗外钩子；因此只保留卡值，没有战斗 Hook。`LUMINESCE` 是 RadiantPearl 在战斗内生成的 Token 卡，效果为获得能量。`MAD_SCIENCE` 的两个保存属性（`TinkerTimeType`/`TinkerTimeRider`）在 Bridge 里投影成 `FlashCard.Private`（低/高字节），不合法（`None`）的根按 `UnrepresentableState` 拒绝；规则表仍按原生 Id 冻结规范数值，结算/评估用 `FlashCardDef.CardType` 读实时类型，`HasDynamicType` 让没有该卡的根跳过这次动态派发。`MAUL` 的每张牌的伤害累加与其私有计数一起放在 `Var0`/`Private`，降级时恢复；`TORIC_TOUGHNESS` 的实例挡格值投影到 `Power.Aux0`（非整数拒绝），卡与 Power 都声明 `IsIdentityContent` 以启用按 occurrence 跟踪移除的 Power 派发。`FORBIDDEN_GRIMOIRE_POWER` 与 `IMPROVEMENT_POWER` 的原生唯一钩子是 `AfterCombatEnd`，没有战斗内读取点，只保留计数行；`FORBIDDEN_GRIMOIRE` 与 `MAD_SCIENCE` 的 Improvement 骑手按现有引擎时机写 `FlashGrowthRewards`（ST 的五个打包成长计数器），与 `Counter[GrowthRewardsForbiddenGrimoireMadScience]` 逐字段一致。

**事件遭遇。** `Content/Monsters/Event/` 实现 `FAKE_MERCHANT_MONSTER`、`MYSTERIOUS_KNIGHT`、`ARCHITECT` 与 `BATTLE_FRIEND_V1/V2/V3`。前两者的招式效果按现有引擎 `MonsterMoveEffects` 逐条移植（商人：SWIPE/SPEW_COINS/THROW_RELIC 的 Frail/ENRAGE 的力量；骑士：继承 FlailKnight 的 WAR_CHANT），起手力量/多层护甲来自根 Power 表。`ARCHITECT` 是 9999 HP 的 `NOTHING_MOVE` 循环，事件是对话场景、没有可搜索的自然战斗根；定义只保证合成根可模拟，不虚构效果。三个 `BATTLE_FRIEND` 变体同样只有 `NOTHING_MOVE`，`BATTLEWORN_DUMMY_TIME_LIMIT_POWER` 在敌方侧回合末递减。最后一层到时，现有引擎设置遭遇 `RanOutOfTime`，`MarkBattlewornDummyTimedOut` 让搜索停在 `SearchBoundaryReason.EventDefeat`；Flash 以追加的 `FlashOutcome.EventDefeat` 表示同一结局，保留 Amount 1（现有引擎在 `CreatureCmd.Escape` 之前就返回，Flash 不移除假人），`RunEnemyTurn` 在 `TriggerEnemySideTurnEndEffects` 后立即返回。`FlashScorer` 在 EventDefeat 上按现有评估的 boundary 分支使用当前 HP（不做威胁投影），终局键在资源键之后比较 `PolicyBoundaryRank(EventDefeat)=4`。三个战损假人遭遇的 FLASHM1/FLASHM2 自然根加入差分回归，固定 fixture `flash-dx5-battleworn-dummy-v1/v2/v3.json` 覆盖 K1 的 EndTurn→EventDefeat 转移。

### 2.2 HistoryCourse、Pen Nib 与 EventDefeat

- **HistoryCourse。** 现有引擎的 `_lastAttackThisTurn` 投影为 `FlashBattle.HistoryCourseCard` + `HistoryCourseCardPresent`；`_lastAttackPreviousTurn` **不投影**：所有受支持根都在 `Play` 阶段（普通根或 Bridge 回放后的准备根），AutoPrePlay 已经消费过上一回合字典，退役值不会再被任何结算读取；`CommitHistoryCourseTurn` 在回合末只把当回合字典搬到上一回合，Flash 的单槽在 AutoPrePlay 消费后由当回合的 `RecordCardPlayed` 重新填充，因此等价。投影从 `SimulatedCombatState.LastAttackThisTurnForFlash(player)`（只读访问器）读取卡值，走同一 `ImportCards` 的卡值投影（附魔、苦难、动态变量、费用修饰、`DampenOriginalUpgrade`、SwordSage/Fetch/returning 账本、小数变量槽），并把槽放在小数变量规范化的 `DetachedCards` 之后、卡牌存储之前。`HistoryCourseCardPresent` 为假时不参与规范化。dupe（`CreateDupeForPlayer`）由栈上的 `CurrentPlayIsHistoryCourseDupe` 表示，不进入状态字节；它的结果去向是移出战斗，不写回槽，并在 Feral/Rebound/ChainsOfBinding 等原生按 `IsDupe` 跳过的 Hook 处同样跳过。`FlashRules.HasHistoryCourse` 为假时记录与阶段都跳过。
- **Pen Nib。** 第十次攻击的锁存保存 `PlayIdentity`（不是布尔）；嵌套自动打出的 Sly 牌是另一次 CardPlay，不会继承加倍。`Aux` 只在一次出牌内非零；`FlashRules.PenNibRelicIndex` 让 `Step` 在稳定边界用一次下标写入清除未完成出牌留下的值（玩家死亡等），与投影的 0 一致。该字段与投影的 `PenNibPredictionState.AttacksPlayed` 一起逐状态比较。
- **EventDefeat。** `FlashOutcome.EventDefeat` 来自旧快照的 `BoundaryReason == SearchBoundaryReason.EventDefeat`（不是 `TerminalStamp`）；`SearchBoundaryReason` 为 EventDefeat 的状态在 K1/K2 逐字节比较，K4 与 K5 也照常比较（现有评估在该边界使用 `ThreatProjection(currentHp, 0, 0)`，Flash 同步）。其他非 None 边界仍按原规则跳过 K4/K5。

### 2.3 附魔与 MELANCHOLY 准入


- **局外牌组副本的准入例外**：现有引擎的 `AfterDeath` 等 Hook 走 `RunHookListeners`（不可变的根牌组前缀 + 战斗监听表），因此牌组里的卡也会被派发。`FlashCardDef.DeckCopyOutsideCombatIsInert` 只允许“该卡在战斗牌堆之外时每个局外监听 Hook 都证明无效果”的定义通过：`Melancholy` 的 `AfterDeathMirrors.HandleMelancholy` 用 `State.FindCard(card)` 取战斗副本，牌组副本取不到就直接返回，所以 Flash 只导入战斗副本、不为牌组副本造时机。其余带局外监听 Hook 的牌组卡仍按 `UnsupportedCard` 拒绝。
- **附魔**：`Imbued`、`Slither`、`SlumberingEssence`、`PerfectFit` 现在都有 Flash 定义，导入时不再按 `UnsupportedEnchantment` 拒绝。`Imbued` 的自动预打出按现有引擎的 `EnchantmentLifecycleSupport.TriggerAutoPrePlay` 在玩家第 1 回合、Mayhem 的计划自动打出之后执行；现有引擎持有 `PredictedCard` 对象快照，Flash 没有卡牌身份，改为“绝对位置 + 稳定键（Id/附魔/苦难/`IsClone`）”的快照并跟踪每次自动打出的净移动。位置或牌数不再匹配（卡被其他效果移走、生成/移除、转化）时显式抛 `FlashUnsupportedException`，不按值猜一张牌；K0 合同覆盖“牌被前一张自动打出移出战斗”的牌数不匹配分支。根快照固定在玩家出牌阶段，第 1 回合的自动预打出在根捕获前已经发生，所以这条路径只能由 K0 合同驱动，游走里不会出现。
- **无需新投影**：四个附魔的私有值都在既有 `FlashCard` 附魔字段里（`EnchantmentId/Amount/Status/Private`），没有新增 `PredictionStateStore` 条目、计数器或布局字段。

### 2.4 药水与遗物投影


- **遗物状态**：`MusicBoxPredictionState.WasUsedThisTurn` → `FlashRelic.Counter`（`CardBeingPlayed` 在稳定边界必须为空，否则 `UnrepresentableState`）；`ThrowingAxePredictionState.UsedThisCombat` → `Counter`；`MetronomePredictionState.OrbsChanneled` → `Counter`；`PaelsLegionPredictionState.Cooldown` → `Counter`，`TriggeredBlockLastTurn` → `FlashRelicFlags.UsedThisCombat`（只驱动动画），`AffectedCardPlay`/`HasCompletedAffectedCardPlay` 必须为空；`EmotionChip` 与 `PaelsEye` 的 `StatefulRelicState` → `Counter`/`Aux`。这些投影都取条目，没有条目时取构造器会读的模型字段（`_wasUsedThisTurn`、`_usedThisCombat`、`_orbsChanneled`、`_cooldown`、`_triggeredBlockLastTurn`）。
- **额外回合的失血标记**：`AdvancePlayerTurn` 只清空 `_unblockedDamageThisTurn` 缓存，旧引擎随后从不可变根历史重算 `HasLostHpThisTurn`，预测期伤害不再计入。Bridge 在根捕获时把每个 owner 的答案冻结为 `FlashRules.RootLostHpMask`；额外回合恢复这些标记，普通换轮清零。`FlashStateImporter` 的逐步投影仍按 `HasLostHpThisTurn` 读取，因此差分逐字节比较覆盖该恢复规则。
- **生成闭包**：`BIIIG_HUG` 声明 Soot；`CHOICES_PARADOX` 声明 `CharacterAll`、`CROSSBOW` 声明 `CharacterAttacks`；九瓶药水分别声明 Shiv/Soul 固定卡或 `ColorlessAll`。闭包仍按 `UnsupportedClosure` 拒绝缺定义的候选。
- **根之前的原生选择**：`CHOICES_PARADOX` 与 `TOASTY_MITTENS` 的原版钩子在玩家第 1 回合开始、根捕获之前执行；离线场景宿主为 `FLASH-PR-*` fixture 在开局窗口安装 `__FIRST__` 卡牌选择器，根状态因此是确定且可复现的。真实战斗里该窗口本来就要玩家作答，不属于 Flash 搜索。
- **仍不支持（原搜索做不到的不要求 Flash 做）**：`BYRDPIP` 的原版 `BeforeCombatStart`/`AddsPet` 召唤 Byrdpip 宠物，现有引擎只模拟 Osty，Flash 没有非 Osty 宠物的状态槽，按 `UnsupportedRelic` 拒绝；`DEPRECATED_RELIC` 的 `Rarity` 为 None、正常流程不可获得，确认后列为不支持。`HISTORY_COURSE` 已实现（见 2.2）。
- **FurCoat**：标记坐标在战斗外固定，`FlashRules.FurCoatSpawnHpOne` 冻结“当前坐标被标记”的结果；新生成敌人的当前生命在入场 Power 之后设为 1，最大生命保持编译值。

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
| K1 单步穷举 | 从根出发，枚举全部合法动作（含选择分支）各执行一步并比较；`IncludeTurnSetup` fixture 对每个准备根做 K4/K5 比较，再从每个 `Play` 根分别跑 K1 | 离线搜索宿主的差分探针（`OFFLINE_HARNESS_FLASH_DIFF=1`，`tools/search/OfflineSearchHarness/FlashDifferentialProbe.cs`） |
| K2 随机游走 | 固定种子，多回合随机合法动作序列，每步比较；覆盖洗牌、敌人回合、死亡 | 同上 |
| K3 内容定向 | 每个新内容至少一个 fixture，让该内容在前几步内被触发 | `coverage/fixtures/` 下按主题存放，K1/K2 消费 |
| K4 评估一致 | K1/K2 中投影一致的每个状态，`FlashScorer` 的评估值必须等于现有引擎同一状态的 `SimulationSnapshot` 对应成员；`Score` 按 double 精确比较。药水计数除 `PotionUseCount`/`PotionStrategicCost`/`AutomaticPotionUseCount`/`ExplicitPotionStrategicCost` 外还逐字段比较 `ExplicitPotionUseCount`。成长额度或遗物计数目标开启时，`GrowthRewards` 的十个来源、`GrowthHpCredit`、`GrowthRewardCount`、`StrategyGoalHpCredit`、`StrategicHpCredit`、`StrategyGoalCount` 与 `RelicCounters` 的 `SatisfiedMask`/`TargetMask`/`CounterValues`/`TargetMinimums`/`TargetMaximums`/`HpCredit`/`Distance`/`SatisfiedPriority`/`HealingHpCredit` 也逐字段比较；`Turn` 始终比较。第三幕首领策略开启时，`Act3BossInteractions`、`FirstAttackDamage`、`EtherealDrawTriggers`、`PagestormBonusDrawCapacity`、`HighEnergyPlays`、`DemesneEnergyGain`、`DemesneDrawGain` 也逐字段比较（参考值由 `SimulationSnapshot` 保存同一状态的战略上下文）。`ContinuousThreatRanking` 或 `ContextualRanking` 开启时，同一状态的 `BeamRankScore` 也按 double 逐位比较（汇总 `rankChecked`/`rankFailed`） | 同一探针；字段表与比较在 `FlashScoreFeatures`、`FlashScoreProjection`。现有引擎停在搜索边界的状态（`BoundaryReason` 非 None）不比较 |
| K5 下界一致 | K1/K2 中投影一致的每个状态，`FlashBoundFeatures` 的 `FutureHealPotential`、`HasRisk`、`RemainingHealingPotential`、`FutureHealUsed`、`CumulativePlayerHpLost`、`RecoveredPlayerHp`、`PlayerHp`、`PlayerMaxHp`、`DeathSaveHpRestored`、`ExplicitPotionStrategicCost` 与 `StrategicHpLowerBound` 必须等于现有主搜索成员同一状态的取值。下界无法精确移植的根整体禁用剪枝，汇总行写 `bound=disabled(<原因>)` | 同一探针；比较在 `FlashBoundFeatures.Diff`。现有侧只读访问器在 `CombatBeamSolver.Flash.cs`（`PrimaryIncumbentRemainingHealingPotential`、`PrimaryIncumbentFutureHealPotential`、`PrimaryIncumbentHpLowerBound`） |

复跑命令与当前证据见 [测试记录](testing.md)。

比较失败时输出第一处差异的字段路径（例如 `Enemies[1].Powers[VULNERABLE].Amount: old=2 flash=3`）、动作序列与种子，便于直接复现。

K4 让 FlashBeam 的排序依据与现有搜索相同：同一状态得到同一分数（含成长与遗物目标、连续威胁与上下文排序的中间排序值），路线质量的差别只来自搜索结构本身。威胁投影的 `ProjectedPlayerHp` 在玩家已死亡时仍按现有引擎的 run-listener 表读取遗物/卡牌修正（例如 Tungsten Rod 的每击减伤）；Flash 的评分探针在只读副本上打开 `PlayerHooksActive`，不改变被评估状态。K5 让 incumbent 下界剪枝的输入与现有主搜索成员相同：同一状态得到同一 `StrategicHpLowerBound` 与 `costClosed`/`pruneEqualHp` 依据；汇总行的 `bound=checked boundChecked=N boundFailed=0` 表示已比较，`bound=disabled(<原因>)` 表示该根显式关闭剪枝。

差分测试只证明"与现有引擎一致"。现有引擎与原版是否一致由既有的 actual/simulated 严格差分保证，不在这里重复。

## 4. 运行时回放核对

Flash 选出的路线在采用前由旧引擎从同一根逐步回放（`FlashRouteMaterializer`，见 [搜索与接入](search.md#2-路线物化)）：每一步旧引擎状态的投影必须与 Flash 状态逐字节一致。

- 任何一步投影不一致、选择对不上，或旧引擎到达 Flash 无法表示的状态（搜索开始后的导入拒绝），都抛 `FlashVerificationException`，消息以 `FLASH_VERIFY_MISMATCH step=` 开头并带首个分歧步序。整条 Flash 路线被丢弃：On 模式写 `FLASH_SEARCH_FAILED reason=verify-mismatch detail=<首个差异>`、抛出 `FlashSearchFailedException` 并停止部署。这里的导入拒绝不是[准入的两类改用](search.md#32-开启模式的改用常规搜索无路线与失败处理)。
- 离线宿主的 `harness-result.json`/`result.json` 在失败异常带完整核对证据时另写 `flashFailure`：`step`、`action`、完整 `plan`、全部 `differences`、步前状态、旧引擎与 Flash 的步后状态，以及 `directPurpose`/`continuationPurpose`/`prefix`。稳定日志行、320 字符 `detail` 与问题包写法不变。
- Flash 搜索自身到达内核不建模的状态（`FlashUnsupportedException`）或容量上限（`FlashCapacityException`）时，On 模式写 `reason=kernel-unsupported`/`reason=kernel-capacity` 并停止部署。差分探针把这类情况计入汇总行末的 `unsupported=N` 并在报告中写出首条消息，不当作分歧。Thrash 的小数卡牌变量不再是边界：现有引擎把 `ModifyDamage` 的 `decimal` 结果写进 `DynamicVars.Damage.BaseValue`，Flash 把同一偏移存进 `FlashBattle.DecimalVars`，伤害读取与 `FlashScorer` 都先按 `decimal` 运算、在现有引擎的同一位置转 `double`/整数；四种同时并存的精确值仍不足时抛 `FlashCapacityException`，不截断、不近似。BeatDown 选中卡牌的对象身份用一次动作内的 `FlashCard.TransientMark` 复刻：标记随卡值移动，前一张自动打出把牌移入手牌、抽牌堆或消耗堆后仍从新位置打出，动作结束前清除；选中的卡完全离开战斗牌堆（移出战斗、被转化）时 Flash 没有无牌堆执行表示，仍是显式 `FlashUnsupportedException`。
- 翻译器按旧引擎当前回放状态把 Flash 动作翻成计划动作：手牌下标超出旧引擎手牌，或旧引擎按动态目标规则（`CombatPredictionSimulator.GetTargetType`，含 Fan of Knives 下的 Shiv、Seeking Edge 下的君王之刃）需要敌方目标而 Flash 动作没有目标时，抛 `FlashChoiceMismatchException`。核对与物化路径把它转成 `FlashVerificationException`（On 模式 `verify-mismatch`）；进度预览只跳过该预览并记录 `FLASH_PREVIEW_MISMATCH`。
- 核对通过的路线作为固定前缀交给旧求解器物化，结果形状与旧后端相同。`IncludeTurnSetup` 路线还会把选中的准备选择向量作为 `rootTurnSetupChoices` 固定给旧求解器；旧求解器重新枚举准备根后只保留该向量，物化结果再逐项核对 `TurnSetupChoices`，不一致整条丢弃（On 模式即 `verify-mismatch` 失败）。
- Smart 药水审计（`SmartPotionGradient` 与 `SmartOpeningPotionPosterior`）在物化时把同一 `PotionFreePolicyBaseline`、`minimum/maximumPotionUses` 与 `RequireAtLeastOne` 交回现有 `CombatBeamSolver`，由现有 `FinalPlanOrdering.Select` 再做一次筛选。物化后 `FlashPotionBridge.VerifyRoute` 核对路线的显式用药次数：动作里的 `UsePotion` 数必须等于 `PotionUses` 的非自动计数，且满足最小/最大次数与 `RequireAtLeastOne` 至少一次；任一不满足都返回 `ConstraintFailure`。分发点把这些药水策略类 `ConstraintFailure` 统一转成现有搜索同形的 `PotionPolicyUnsatisfiedException`（`FLASH_NO_ROUTE`），由 Smart 梯度、后验与续接调度按原逻辑继续；它不是 `FLASH_SEARCH_FAILED`。
- 固定前缀可以由药水动作开头：前缀先经现有 `ApplyFixedPrefix` 回放，Flash 只从回放后的搜索根展开；搜索根快照的累计药水计数冻结进评分与下界，因此前缀用药不丢失。

## 5. 失败处理约束

- 不新增 `catch (Exception)`。只捕获 `FlashUnsupportedException`、`FlashCapacityException`、准入拒绝、校验失败与取消，以及物化阶段现有求解器抛出的 `PotionPolicyUnsatisfiedException`。
- 捕获后整个 Flash 求解作废，不把半成品路线交给协调器。On 模式只有 `admission-unsupported-content` / `admission-unrepresentable-state` 两类准入例外：协调器整次搜索改用 Off + `RegularProfile` 从头重跑，写一行 `FLASH_FALLBACK`，不算失败。已经发布路线、采纳种子或预览时不再重跑，按原失败约定写 `FLASH_SEARCH_FAILED`。其余情况随即停止搜索/部署并上报 `FlashSearchFailedException`；Off 保持原有返回路径。回合准备搜索走同一入口与分类约定：整次重跑，或其余失败（关闭全自动、Flash 提示、问题包带 `SearchFailure` 与 `FlashSearchFailed`，原生选择留给玩家）。
- 药水审计没有满足策略的路线（`PotionPolicySatisfied=false`，`flash-potion-policy-no-route`）时按无路线约定返回 `PotionPolicyUnsatisfiedException`；诊断行写 `FLASH_NO_ROUTE`，不写 `FLASH_SEARCH_FAILED`。精确层下界输入不可用（`smart-potion-bound unavailable`）不是无路线：开启模式只关闭下界剪枝并照常搜索，写 `SMART_POTION_BOUND_UNAVAILABLE` 诊断。物化后 `FlashPotionBridge.VerifyRoute`（`route-*`）或现有 `FinalPlanOrdering`（`materialize-potion-policy`）仍判定不满足请求药水约束时，是两侧判定不一致，按 `potion-policy-mismatch` 显式失败。
- 整次改用常规搜索、失败事件与无路线结论都计入诊断：类别、首个不支持的内容/差异/拒绝原因与阶段；On 模式不再写 `FLASH_LEGACY`。
