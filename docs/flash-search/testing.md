# Flash 测试记录

[返回 Flash 入口](README.md)

本文记录 Flash 的可复跑验证入口与当前证据。方法定义见 [准入与校验](admission-and-verification.md)。

## 1. 复跑入口

纯内核合同（K0），不依赖游戏：

```bash
DOTNET_TieredCompilation=0 dotnet run --project tools/testing/checks/FlashKernelChecks -c Release
```

差分探针（K1 单步穷举、K2 随机游走），需要游戏程序集与已构建的模组：

```bash
dotnet build CombatSolver.csproj -c Release -p:CopyModOnBuild=false
dotnet build tools/search/OfflineSearchHarness/OfflineSearchHarness.csproj -c Release
OFFLINE_HARNESS_FLASH_DIFF=1 OFFLINE_HARNESS_FLASH_DIFF_WALKS=32 \
  dotnet .local/tool-build/OfflineSearchHarness/bin/Release/net9.0/OfflineSearchHarness.dll \
  --character IRONCLAD --encounter NIBBITS_NORMAL --seed FLASH1 --milestone M1 --out <输出目录>
```

PowerShell 用 `$env:OFFLINE_HARNESS_FLASH_DIFF = '1'` 设置同名变量后运行相同 `dotnet` 命令。探针把结果写入输出目录的 `flash-diff.txt`（首行为汇总），并在 `harness-result.json` 的 `flashDiff` 字段记录同样的计数。`OFFLINE_HARNESS_FLASH_DIFF_STEPS` 控制每条随机游走的最大步数（默认 60）。

主搜索分发（`BeamSolveDispatch`）在协调器模式下复跑，日志中的 `[CombatSolver/Flash]` 行记录对照或采用结果：

```bash
OFFLINE_HARNESS_FLASH_MODE=Shadow \
  dotnet .local/tool-build/OfflineSearchHarness/bin/Release/net9.0/OfflineSearchHarness.dll \
  --character IRONCLAD --encounter NIBBITS_NORMAL --seed FLASH1 --milestone M2 \
  --search-mode Coordinator --budget-ms 10000 --out <输出目录>
```

## 2. 当前证据（2026-10-19，Linux）

F1 纵切与内容批次 P1–P12 的逐批证据已归档到 [内容批次测试记录](../archive/flash-search/testing-content-batches-2026-10-18.md)，强制行动/眩晕（KS）、敌人生成（KM）与早期端到端测试归档到 [KS、KM 与早期端到端测试记录](../archive/flash-search/testing-batches-2026-10-19.md)，第二幕 P13 的逐根证据归档到 [第二幕 P13 测试记录](../archive/flash-search/testing-act2-p13-2026-10-19.md)；下面的全量回归覆盖其中全部根。

全量回归（`.local/tool-tasks/flash-search/regress.sh`，P13 合入后）：FlashKernelChecks 通过；51 个自然遭遇根（铁甲战士初始牌组，含第二幕 9 个遭遇的 FLASHM1/FLASHM2）与 77 个固定根（`coverage/fixtures/cards/flash-*.json`）每根 32 条随机游走，K1 单步、K2 游走与 K4 评估全部零差异，没有准入拒绝。

### 2.1 覆盖缺口

第一幕：

强制行动、眩晕与必须先执行一次的行动已有内核支持，敌人生成也已支持。仍跳过的 6 个第一幕遭遇及缺失能力如下；仪式兽、瀑布巨兽、异蛙寄生虫各自的强制行动来源随遭遇一起实现：

| 能力 | 遭遇 |
| --- | --- |
| 其他召唤物（内核已支持生成，召唤物定义未实现） | 异蛙寄生虫、穿着一件大衣的两只地精、邪恶气体 |
| 苦难/附魔与改费用 Hook | 仪式兽、藤蔓蹒跚者、邪恶气体 |
| 阻止战斗结束、死亡后不离场 | 异蛙寄生虫、瀑布巨兽、穿着一件大衣的两只地精 |
| 偷窃与评估侧 | 穿着一件大衣的两只地精（被偷资源未建模）；瀑布巨兽（动态攻击伤害与评估特例） |

第二幕（虫巢）：实现 9 个遭遇（实现说明与逐根证据见 [第二幕 P13 测试记录](../archive/flash-search/testing-act2-p13-2026-10-19.md)），跳过 11 个，缺失能力与现存机制位置：

| 遭遇 | 缺失能力 | 机制位置 |
| --- | --- | --- |
| `BOWLBUGS_NORMAL`、`BOWLBUGS_WEAK` | 其他（遭遇槽位）；另需 ImbalancedPower 的失衡眩晕 | `EncounterModel.Slots` 为空但 `GenerateMonsters` 使用 `first`/`middle`/`last`、`odd`/`even`；`SimulatedCombatState.cs:278` 只取 `Encounter.Slots`；`FlashStateImporter.cs:263-273 SlotForFlash` 拒绝未知槽位。`P/MonsterMoveSemantics.cs:84-90` 的失衡路径已存在，但内容未落。 |
| `SLUMBERING_BEETLE_NORMAL` | 其他（遭遇槽位）；另需 SlumberPower（Plating 已有） | 同上；`SlumberingBeetle` 的 `first`/`second`/`third` 槽位不在 `Encounter.Slots`。 |
| `DECIMILLIPEDE_ELITE` | 其他（Reattach 复活/不可命中/致死抑制） | `P/DeathPowerSupport.cs:120 BeginReattach`、`S/SimulatedCombatState.DeathLifecycle.cs:120`、原版 `ReattachPower.DoReattach/ShouldAllowHitting/ShouldOwnerDeathTriggerFatal`；Flash 只有 `PreventsRemovalAfterDeath` 与幻象式复活（`FlashKernel.Monsters.cs:207`），没有 Reattach 节点、复活回血与致死抑制。 |
| `HUNTER_KILLER_NORMAL` | 状态无法用整数槽表达（`_tenderCardsPlayed`） | `S/SimulatedCombatState.cs:970 RecordTenderCardPlayed`、`P/EndTurnPowerSupport.cs:96,220 RestoreTender`、`P/AfterCardPlayedMirrors.cs:799`；`S/SimulatedCombatState.FlashProjection.cs:116 DescribeFlashUnrepresentableState` 对非零 `_tenderCardsPlayed` 仍直接拒绝，允许修改的投影文件无法认领。 |
| `KAISER_CRAB_BOSS` | 其他（Surrounded 状态在玩家死亡清空 Power 后残留） | `P/BeforeCardPlayedMirrors.cs:273`/`P/ModifyDamageMirrors.cs:178` 维护 `SurroundedPredictionState`；玩家死亡清空 Power 后 `StateStore` 条目仍在，`FlashStateImporter.ImportPowers` 跳过 0 层 Power，`FlashStateProjections.RequireAllClaimed` 只对 Vigor 退役状态放行。自然根首个差异：`UnrepresentableState StateStore: SurroundedPredictionState@SURROUNDED_POWER`。 |
| `INFESTED_PRISMS_ELITE` | 苦难/附魔；AfterPowerAmountChanged / AfterApplied | 原版 `VitalSparkPower` 在 `BeforeCombatStart`/`AfterCardEnteredCombat` 施加 `Tainted`、`AfterPowerAmountChanged` 同步层数；`P/MonsterMoveEffects.cs` 的 PULSATE/RADIATE。Flash 没有 Affliction 内容与这些 Hook。 |
| `KNOWLEDGE_DEMON_BOSS` | 其他（怪物选择协议）；状态无法用整数槽表达（`_knowledgeDemonCurseCounters`） | `S/SimulatedCombatState.KnowledgeDemon.cs`、`P/KnowledgeDemonChoiceSupport.cs`、`P/BranchMonsterAi.cs:160-170`、`P/MonsterMoveEffects.cs:341`；Flash 怪物行动不能产生待处理选择。 |
| `LOUSE_PROGENITOR_NORMAL` | 状态无法用整数槽表达（CurlUpPredictionState 退役） | `E/Mirrors/Hooks/Card/AfterCardPlayedMirrors.cs:564`、`AfterDamageReceivedMirrors.cs:139` 维护 `CurlUpPredictionState`；Power 归零移除后条目仍在，`ImportPowers` 不再调用 `ProjectPower`，`RequireAllClaimed` 只处理 Vigor。 |
| `THE_INSATIABLE_BOSS` | 其他（SandpitPower 的 AfterSideTurnStartLate/目标/击杀）；评估侧缺口 | `P/MonsterMoveEffects.cs:938`、`P/TurnStartPowerSupport.cs:449`、`P/CardOnPlaySupport.Batch042.cs:64`、`S/SimulatedCombatState.IncrementSandpitTargeting`；Flash 没有 `AfterSideTurnStartLate` 与目标化 Power。`FlashScorer.cs:415` 把 `SandpitRemaining` 固定为 0，而 `FlashScoreProjection` 比较该字段。 |
| `THIEVING_HOPPER_WEAK` | 偷窃；评估侧缺口 | `P/MonsterMoveEffects.cs:170-220` 的 THIEVERY/ESCAPE、`S/SimulatedCombatState.RecordThievery`；`FlashScoreBridge.cs:103 TheftApplicable` 拒绝，`FlashScorer.cs:227-230` 把 `OutstandingStolenResource` 固定为 0。 |

卡牌（铁甲战士剩余 24 张，P14）：实现 Aggression、StoneArmor、Unmovable、Rupture 与 AggressionPower、UnmovablePower、RupturePower（StoneArmor 复用 PlatingPower）；4 个 `coverage/fixtures/cards/flash-p14-*.json` 每根 48 条游走零差异。RupturePower 的 Aux0..2 只在出牌期间存放待结算力量与伤害来源标记，稳定边界为 0，投影只认领空的 `RupturePredictionState`。跳过 20 张：自动打出（Cascade、Havoc、Hellraiser、HowlFromBeyond、Stampede）、卡池随机生成（InfernalBlade、Stoke）、变化（PrimalForce）、改费用（Corruption、Unrelenting、Stomp，后者另需扩充 `CardEnergyCostModifiers` 容量）、出牌次数（OneTwoPunch）、AfterPowerAmountChanged（Vicious）、战斗全程未格挡受伤计数（TearAsunder）、跨卡计算伤害（Thrash）、多人专用（Blaze、DemonicShield、Midnight、Outrage、Tank）。

卡牌（无色卡池，P15）：新增 28 张无色卡（含 Finesse、Impatience、Production、Salvo、SecretTechnique、DarkShackles 等）与 5 个 Power（DarkShackles、Fasten、NoBlock、PrepTime、TheGambit），8 个 `coverage/fixtures/cards/flash-p15-*.json` 每根 48 条游走 K1/K2/K4/K5 零差异；MindBlast 与 Rend 的计算伤害乘数、TheGambit 的死亡预测按现有评估补进 FlashScorer/FlashScoreBridge。跳过 37 张：多人专用 12、自动打出 3（BeatDown、Catastrophe、Mayhem）、卡池随机生成/变化 7（Alchemize、Calamity、Discovery、Entropy、JackOfAllTrades、Jackpot、Splash）、随机抽样与回手历史 5、伤害/击杀历史 4、按实例区分的 Power 4（Automation、Panache、RollingBoulder、TheBomb）、尚未派发的 Hook 2（Nostalgia、Stratagem）。

遗物（R2）：新增 122 个遗物定义（34 个有战斗行为，88 个只在战斗外或根之前生效、逐个注明原版 Hook）与 SelfFormingClayPower；PaperPhrog 的效果由易伤倍率承载。7 个 `coverage/fixtures/cards/flash-r2-*.json` 每根 48 条游走零差异。跳过的遗物：TheAbacus（AfterShuffle 未派发）、MysticLighter（附魔伤害修正）、IceCream（能量重置）、ChemicalX（ModifyXValue）、RuinedHelmet（Power 数量修正 Hook）、GhostSeed（遗物的 AfterCardEnteredCombat）。目标 8 个药水全部跳过（随机生成与自动打出待 KG1，Duplicator 需出牌次数修改，Gigantification 需对象身份）。

内核能力（KG1，自动打出、卡池随机生成、变化）：`FlashKernel.AutoPlay`/`AutoPlayFromDrawPile` 复用打出管线，不可打出、无目标或 `ShouldPlay` 拒绝时移入结果牌堆，随机目标走 CombatTargets 流；新增 `AfterAutoPostPlayPhaseEntered` 派发点。生成池由 Bridge 按现有枚举冻结，内核按现有取样（去重或有放回）抽取，候选缺定义时按 `UnsupportedClosure` 拒绝；PrimalForce 用 `ReplaceCard` 原位替换为 GiantRock。实现 Havoc、Cascade、Hellraiser、HowlFromBeyond、Stampede、BeatDown、Catastrophe、PrimalForce、Jackpot；InfernalBlade、Stoke、Discovery、JackOfAllTrades、Splash、Calamity、Alchemize 已有定义，但生成闭包仍含未定义的卡或药水，根被拒绝；Entropy 跳过。3 个 `coverage/fixtures/cards/flash-kg1-*.json` 每根 48 条游走零差异，临时计数确认自动打出、生成与变化在游走中发生。

药水（R3）：DistilledChaos 用 `AutoPlayFromDrawPile` 实现，3 个 `coverage/fixtures/cards/flash-r3-*.json` 每根 48 条游走零差异；OrobicAcid 按现有卡池生成语义实现，但生成闭包仍缺 OneTwoPunch、Stomp、Thrash、Unrelenting、Vicious 等卡，根被拒绝。AttackPotion、SkillPotion、PowerPotion、ColorlessPotion 暂不定义：它们的 0..1 生成选择由 `PotionChoiceSupport` 结算，Flash 的回放翻译尚未覆盖。EntropicBrew（战斗外药水池与免费状态）、Duplicator、GigantificationPotion、Ambergris 与角色专属药水跳过。

内核 Hook（KG2）：新增改费用（普通与 Late 两遍）、出牌去向、出牌次数、`AfterPowerAmountChanged`（按现有引擎的延迟队列在同样的位置排空）、`AfterShuffle`、`ModifyXValue`、玩家能量重置判定、接收侧 Power 数量修正、遗物 `AfterCardEnteredCombat` 的派发，`AfterDamageReceived` 带卡牌来源。实现 Corruption、Unrelenting、Stomp、OneTwoPunch、Vicious、TearAsunder、Thrash、Duplicator、TheAbacus、ChemicalX、IceCream、RuinedHelmet、GhostSeed；RupturePower 改为直接读取伤害来源。为容纳 Stomp 逐次追加的费用修饰，单卡修饰容量 2 → 16，`FlashCard` 90 字节、`FlashBattle` 11808 字节：48 根基准路线不变，FlashBeam 搜索耗时 +3.5%，每次求解托管分配平均 16 → 27 MB，留作之后压缩卡牌布局的候选。4 个 `coverage/fixtures/cards/flash-kg2-*.json` 每根 48 条游走零差异，临时计数确认每个新派发点在游走中触发。UnsettlingLamp（卡牌实例身份）与 SneckoSkull（无 Poison 定义）跳过。

### 2.2 搜索演进摘要

完整表格与分析见 [搜索演进测试记录](../archive/flash-search/testing-search-2026-10-19.md)。

- 评估移植（S1）：`FlashScorer` 逐项移植 `CombatBeamSolver.Snapshot` 与 `BeamRankScore`，K4 对投影一致的全部状态逐字段比较，`Score` 逐位相等。
- 宽度：48 根基准（宿主默认档位）中宽度与节点 ×4 是第一个总失血优于现有搜索的档位（13 好 / 28 同 / 7 差，失血 −9），设为 `ProfileWidthScale` 默认值；状态池按需增长、子状态在池槽内原地执行后路线不变、耗时下降。
- incumbent 下界剪枝（SB）：下界输入由 K5 逐状态核对；FlashBeam 转移数 −73%，相对现有搜索 14 好 / 24 同 / 10 差、失血 −16。变差根来自 incumbent 发现顺序与更简单的保留结构，不是剪枝语义。

### 2.3 协调器宽度组合（2026-10-19）

分发点扩展到精炼成员，并新增内核 `BeforeAttack` Hook（活力的捕获从修正 Hook 移入，修正 Hook 不再改写状态）。全量回归 101 根 K1/K2/K4/K5 零差异。

`WARMUPS=2 COORD_EXTRA_ARGS=--use-portfolio coord.sh`（48 根，宿主默认档位，开启生产默认的宽度组合）：On 相对 Off 14 好 / 30 同 / 4 差、失血合计 −31，总耗时 27.0 s → 23.9 s。Flash 采用主搜索 144 次、精炼成员 288 次；精炼成员回落 249 次（仅基础分与带权重扰动的有界进攻成员在准入时拒绝）。

随后移植仅基础分与权重扰动两种中途排序变体（公式照搬 `CombatBeamSolver.BeamRankScore`，输入项均已由 K4 逐字段核对）。实机默认的 Medium 档位（宽度 60、120,000 节点、120 秒，开启宽度组合，24 个自然根 FLASHM1，每进程预热 1 次）：

| 配置 | On 更好 / 相同 / 更差 | 失血合计差 | Off → On 总耗时 | 精炼成员 |
| --- | --- | --- | --- | --- |
| 变体未移植 | 2 / 19 / 3 | +1 | 21.9 s → 17.6 s | 采用 96、回落 82 |
| 变体已移植 | 2 / 18 / 4 | +3 | 21.2 s → 15.0 s | 采用 144、回落 0 |

另试过把评估的威胁投影改为直接读取被评估状态、首次写入前才复制：路线与转移数不变，搜索耗时 4.32 s → 4.47 s，没有可测收益，未保留。

### 2.4 固定前缀续接分发（FC，2026-10-19）

`FrontierContinuationScheduler.Dispatch`/`DispatchOptional` 改走 `BeamSolveDispatch.SolveContinuation`，只把无药、无承诺、无侦察、无最早药水回合的干净请求交给 Flash；前缀由旧引擎按 `ApplyFixedPrefix` 同一规则回放，Flash 从前缀末尾搜索，动作计数从 `Prefix.Length` 起算，物化仍把前缀 + Flash 路线整条交给现有求解器。全量回归 `regress.sh fc-regression 32` 101 根 K1/K2/K4/K5 零差异（K0 通过），SUMMARY 见任务记录。

协调器对照（同一台机器，`WARMUPS=1 COORD_EXTRA_ARGS="--use-portfolio --profile Medium" coord.sh ... 120000 M2 FLASHM1`，24 个自然根）：

| 配置 | On 更好 / 相同 / 更差 | 失血合计差 | Off 总耗时 | On 总耗时 |
| --- | --- | --- | --- | --- |
| 改动前（fc-coord-baseline） | 2 / 18 / 4 | +3 | 22.1 s | 15.3 s |
| 固定前缀续接（fc-coord） | 3 / 17 / 4 | −1 | 23.0 s | 8.4 s |

Off 侧 24 根的 `search.solverMetrics`、`search.planActions`、`search.cachedContinuations` 与 `quality.json` 与改动前逐字段相同。相对改动前只有 `NIBBITS_NORMAL-FLASHM1` 变化：28 失血 / 9 回合 → 24 / 8。4 个变差根（`INKLETS_NORMAL`、`OVERGROWTH_CRAWLERS`、`RUBY_RAIDERS_NORMAL`、`SEAPUNK_NORMAL`）与改动前的 On 结果完全相同，属于既有主搜索/精炼成员的 Flash 保留结构差异；其中 `OVERGROWTH_CRAWLERS` 与 `SEAPUNK_NORMAL` 的续接请求被 Flash 采用，但最终选择仍与改动前 On 相同。

续接 Flash 行按用途统计（`fc-coord` 的 On 日志）：`OpeningTargetVariant` 采用 50 次（前缀 3 动作 ×10、4 动作 ×40）、`TurnBoundaryRescue` 采用 22 次（前缀 3×4、4×18），回落 0 次；`PrimaryBeam` 采用 72、`RefinementBeam` 采用 216。

Shadow 隔离（`NIBBITS_NORMAL`、`SLIMES_NORMAL`、`RUBY_RAIDERS_NORMAL`，FLASHM1，Medium，120 秒，宽度组合）：Off 与 Shadow 的 `search.solverMetrics` 全部字段、`quality.json`、`search.planActions` 完全相同；3 根合计 26 行 Shadow 对照（其中 11 行续接，`OpeningTargetVariant prefix=4`），全部 `same_route=false`（Flash 找到不同路线，Shadow 仍返回旧结果）。示例：`NIBBITS_NORMAL` 8 行、`SLIMES_NORMAL` 9 行、`RUBY_RAIDERS_NORMAL` 9 行。

### 2.5 生成牌组上的协调器对照（2026-10-19）

60 个随机生成的中期铁甲战士牌组（第一、二幕，普通/精英/首领，6–12 张角色卡、0–2 张无色卡、2–5 个遗物、0–2 瓶药水，进阶 10；`.local/tool-tasks/flash-search/deck-survey.sh`）中，KG2 合入后 15 个准入（此前 4 个）。剩余缺口以怪物为主（仪式兽、千足虫、异蛙寄生虫、知识恶魔、碗虫等），其次是 12 张无色卡、5 瓶带生成选择的药水与 MysticLighter。

对准入的牌组跑实机默认 Medium 档位、开启宽度组合的协调器 Off/On（每进程预热 1 次，`coord-deck.sh`）：14 根总耗时 92.6 s → 26.9 s，On 4 好 / 8 同 / 2 差、失血合计 −2；最重的两根 62.0 s → 12.0 s、17.2 s → 6.1 s。Flash 承担主搜索、精炼成员与 TurnBoundaryRescue、OpeningPowerRouteMember、OpeningTargetVariant、DeferredOpeningPower 四类续接。

第 15 根在 On 模式中途中止：Thrash 把 `ModifyDamage` 的小数结果加进卡牌变量，Flash 内核抛出通用参数异常并逃出分发点。修正后该路径抛 `FlashUnsupportedException`，分发点把 Flash 搜索中的 `FlashUnsupportedException`/`FlashCapacityException` 当作内核边界回落现有求解器（`status=boundary`）；该根 On 结果与 Off 相同（失血 74，32.2 s 对 29.4 s）。差分探针同样把这类情况计为 `unsupported`。

## 3. 已知限制与未验证项

- 只覆盖归档批次与 2.1 所列内容；其他内容在准入阶段拒绝，尚未有差分证据。
- 仍在战斗中的生物身上已移除 Power 的残留条目不能投影：根导入时拒绝，路线核对中遇到时按核对失败丢弃 Flash 路线；敌方离场后的残留按惰性处理（见内容批次归档卷 2.8.1）。
- 选择只覆盖卡牌自身的单选（消耗、移到抽牌堆顶、升级）；多选、回合开始选择、嵌套选择与自动打出中的选择尚无差分证据。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 未做可见游戏、无人测试或实机部署验证；分发只在离线宿主协调器模式下验证过归档卷 2.3 所列根。设置面板的新选项未在游戏中查看。
