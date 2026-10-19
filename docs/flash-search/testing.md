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

无人测试中的实机部署用 `--flash-search-mode-for-test Off|Shadow|On`（PowerShell `-FlashSearchModeForTest`）临时覆盖设置；`--export-bug-report-after-combat` 导出的问题包在 `diagnostics/logs/combat/*.jsonl` 中保留 `[CombatSolver/Flash]` 行：

```bash
./tools/testing/run-unattended-test.sh --scenario-id FLASH-ON-NIBBITS --character-id IRONCLAD --encounter-id NIBBITS_NORMAL \
  --enemy-current-hp 1000 --headless-fast-mode-for-test Instant --deployment-fast-mode-for-test Instant \
  --deployment-inter-action-delay-seconds-for-test 0 --expected-unexpected-replans-at-most 0 \
  --flash-search-mode-for-test On --timeout-seconds 120 --cleanup-instance-on-exit
```

## 2. 当前证据（2026-10-19，Linux）

F1 纵切与内容批次 P1–P12 的逐批证据已归档到 [内容批次测试记录](../archive/flash-search/testing-content-batches-2026-10-18.md)，强制行动/眩晕（KS）、敌人生成（KM）与早期端到端测试归档到 [KS、KM 与早期端到端测试记录](../archive/flash-search/testing-batches-2026-10-19.md)，第二幕 P13 的逐根证据归档到 [第二幕 P13 测试记录](../archive/flash-search/testing-act2-p13-2026-10-19.md)；下面的全量回归覆盖其中全部根。

全量回归（`.local/tool-tasks/flash-search/regress.sh`，SK 合入后）：FlashKernelChecks 通过；116 个自然遭遇根（铁甲战士初始牌组覆盖三幕已实现遭遇的 FLASHM1/FLASHM2，另有 6 条静默猎手初始牌组根）、161 个固定根（`coverage/fixtures/cards/flash-*.json`）与 33 个生成牌组（`coverage/corpora/flash-decks/`）每根 32 条随机游走，K1 单步、K2 游走、K4 评估与 K5 下界输入全部零差异，没有准入拒绝；只有含 Thrash 的 `deck-IRONCLAD-02`/`18` 按小数卡牌变量边界记 `unsupported`。

### 2.1 覆盖缺口

怪物（KM2、KA，2026-10-19）：KM2 补上空 `EncounterModel.Slots` 的槽位回退（未知槽名与现有引擎同为 -1）、失衡眩晕、沉睡、千足虫重接、寄生死亡未结算时阻止战斗结束、Surrounded 与 CurlUp 退役状态的惰性规则、猎杀者的 Tender 计数投影，实现碗虫（普通/弱）、沉睡甲虫、千足虫、异蛙寄生虫、猎杀者、Kaiser Crab、Louse Progenitor；KA 补上卡牌苦难与附魔（导入、施加/清除、归一化、附魔伤害/格挡/重放、尾部 OnPlay，18 个附魔），实现仪式兽、藤蔓蹒跚者、邪恶气体（LIVING_FOG）、注入棱晶与 MysticLighter。`coverage/fixtures/cards/flash-km2-*.json` 8 个、`flash-ka-*.json` 5 个及 12 个新遭遇的 FLASHM1/FLASHM2 自然根，每根 48 条游走零差异、`unsupported=0`。邪恶气体会反复生成毒气弹，极长的战斗可能超出 8 个已知敌人的容量，此时按容量边界回落。

第三幕（A3，2026-10-19）：实现荣耀的全部 18 个遭遇（25 种新怪物，含 Queen 的强制行动、Aeonglass 的力量与凋零计数、MagiKnight 的 Dampen 降级与恢复、Turret Operator、Soul Nexus、Living Shield 等），19 个 `coverage/fixtures/cards/flash-a3-*.json` 与 18 个遭遇的 FLASHM1/FLASHM2 自然根每根 48 条游走零差异；`FlashCard` 增加 `DampenOriginalUpgrade`，`FlashBattle` 12000 字节。第一、二幕剩余（P16）：剩余 16 张无色卡全部实现，AttackPotion、SkillPotion、PowerPotion、ColorlessPotion 补齐到 `PotionChoiceSupport` 的选择回放翻译后实现，另实现 GigantificationPotion；7 个 `flash-p16-*.json` 零差异。

仍跳过：第一幕瀑布巨兽（动态攻击伤害、阻止战斗结束与评估特例）、穿着一件大衣的两只地精（偷窃）；EntropicBrew（战斗外药水池）；Thrash 的小数卡牌变量仍是显式边界；第二幕如下。未实现的附魔（Imbued、Slither、SlumberingEssence、PerfectFit）仍在准入拒绝。

| 遭遇 | 缺失能力 | 机制位置 |
| --- | --- | --- |
| `KNOWLEDGE_DEMON_BOSS` | 其他（怪物选择协议）；状态无法用整数槽表达（`_knowledgeDemonCurseCounters`） | `S/SimulatedCombatState.KnowledgeDemon.cs`、`P/KnowledgeDemonChoiceSupport.cs`、`P/BranchMonsterAi.cs:160-170`、`P/MonsterMoveEffects.cs:341`；Flash 怪物行动不能产生待处理选择。 |
| `THE_INSATIABLE_BOSS` | 其他（SandpitPower 的 AfterSideTurnStartLate/目标/击杀）；评估侧缺口 | `P/MonsterMoveEffects.cs:938`、`P/TurnStartPowerSupport.cs:449`、`P/CardOnPlaySupport.Batch042.cs:64`、`S/SimulatedCombatState.IncrementSandpitTargeting`；Flash 没有 `AfterSideTurnStartLate` 与目标化 Power。`FlashScorer.cs:415` 把 `SandpitRemaining` 固定为 0，而 `FlashScoreProjection` 比较该字段。 |
| `THIEVING_HOPPER_WEAK` | 偷窃；评估侧缺口 | `P/MonsterMoveEffects.cs:170-220` 的 THIEVERY/ESCAPE、`S/SimulatedCombatState.RecordThievery`；`FlashScoreBridge.cs:103 TheftApplicable` 拒绝，`FlashScorer.cs:227-230` 把 `OutstandingStolenResource` 固定为 0。 |

卡牌（铁甲战士剩余 24 张，P14）：实现 Aggression、StoneArmor、Unmovable、Rupture 与 AggressionPower、UnmovablePower、RupturePower（StoneArmor 复用 PlatingPower）；4 个 `coverage/fixtures/cards/flash-p14-*.json` 每根 48 条游走零差异。RupturePower 的 Aux0..2 只在出牌期间存放待结算力量与伤害来源标记，稳定边界为 0，投影只认领空的 `RupturePredictionState`。跳过 20 张：自动打出（Cascade、Havoc、Hellraiser、HowlFromBeyond、Stampede）、卡池随机生成（InfernalBlade、Stoke）、变化（PrimalForce）、改费用（Corruption、Unrelenting、Stomp，后者另需扩充 `CardEnergyCostModifiers` 容量）、出牌次数（OneTwoPunch）、AfterPowerAmountChanged（Vicious）、战斗全程未格挡受伤计数（TearAsunder）、跨卡计算伤害（Thrash）、多人专用（Blaze、DemonicShield、Midnight、Outrage、Tank）。

卡牌（无色卡池，P15）：新增 28 张无色卡（含 Finesse、Impatience、Production、Salvo、SecretTechnique、DarkShackles 等）与 5 个 Power（DarkShackles、Fasten、NoBlock、PrepTime、TheGambit），8 个 `coverage/fixtures/cards/flash-p15-*.json` 每根 48 条游走 K1/K2/K4/K5 零差异；MindBlast 与 Rend 的计算伤害乘数、TheGambit 的死亡预测按现有评估补进 FlashScorer/FlashScoreBridge。跳过 37 张：多人专用 12、自动打出 3（BeatDown、Catastrophe、Mayhem）、卡池随机生成/变化 7（Alchemize、Calamity、Discovery、Entropy、JackOfAllTrades、Jackpot、Splash）、随机抽样与回手历史 5、伤害/击杀历史 4、按实例区分的 Power 4（Automation、Panache、RollingBoulder、TheBomb）、尚未派发的 Hook 2（Nostalgia、Stratagem）。

角色（静默猎手，SK，2026-10-19）：内核补上弃牌管线与 `AfterCardDiscarded`（Sly 按弃置顺序自动打出）、中毒（`TriggerPoison` 与 Accelerant）、Shiv 生成与动态全体目标、下回合抽牌/能量、Blur、DoubleDamage、出牌期金额快照（Afterimage、Strangle）与多选卡牌选择的接线；实现初始牌组、RingOfTheSnake、73 张静默猎手卡牌（含 Shiv）与 21 个 Power，MementoMori、PreciseCut、Murder 的计算伤害照现有评估移植。7 个 `coverage/fixtures/cards/flash-sk-*.json` 与 NIBBITS/SLIMES/CULTISTS 的静默猎手初始牌组 FLASHM1/FLASHM2 自然根每根 48 条游走 K1/K2/K4/K5 零差异、`unsupported=0`；K0 增加弃牌/Sly/中毒/动态目标/快照/下回合资源合同，`FlashBattle` 仍为 12000 字节。跳过 15 张卡（TheHunt、KnifeTrap、EchoingSlash、Anointed、Burst、Mirage、ToolsOfTheTrade、Entropy、Nightmare、Shadowmeld、Nostalgia、Stratagem、Omnislice、PhantomBlades、RollingBoulder）与 9 个遗物（UnsettlingLamp、SneckoSkull、PaperKrane、UnceasingTop、ReptileTrinket、Vambrace、Tingsha、HelicalDart、BeltBuckle），缺失能力按项记录在 `.local` 任务报告中，主要是战斗内奖励、牌堆筛选后取样/自动打出、单次攻击多轮伤害、计算格挡、回合开始选择、实例型 Power 与 Power 给予数量修正 Hook。故障机器人、储君、亡灵契约师仍全部在准入阶段拒绝（各 30 个随机牌组准入 0）。

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

生成牌组差分（DX，2026-10-19）：对准入的 32 个生成牌组跑 48 条游走，固定根回归没有覆盖到的内容组合暴露出 10 个牌组的分歧与 2 处崩溃。逐个按现有引擎修正：`PendingPowerChanges` 嵌套排空（Vicious 抽到 Strike 后 Hellraiser 自动打出，外层用过期计数越界，改为先整批取走再派发，对应 `DrainPowerAmountChanges`）；自动打出攻击中的敌人死亡要在后续自动打出选目标前结算；玩家死亡后 DarkEmbrace、Juggling、Hellraiser、Rupture、CardPlayPair 的退役预测状态只在玩家已死亡时按惰性处理；Discovery/Splash 不再置风险位（现有引擎把它们登记为已补偿）；Pillage 读取刚抽到的那张牌而不是手牌末尾；Razor Tooth 的升级照 `Upgrade` 在战斗结束中不生效；自动打出队列在战斗结束时照现有引擎只写消耗标志；Thunderclap 与注入死亡生成的结算顺序；SecondWind 与 BurningSticks 的消耗核对。牌组收进 `coverage/corpora/flash-decks/`（33 份 spec 与 request），回归脚本对其逐个跑差分。修复后全量回归 238 行零差异，只有含 Thrash 的两个牌组按小数卡牌变量边界记 `unsupported`。

牌组语料上的协调器对照（33 个牌组，Medium 档位、宽度组合、每进程预热 1 次）：Off 总耗时 273.0 s；On 在 DX 修正后为 195.7 s，其中一根因单个分支超出 96 张卡牌容量而整次回落、反比 Off 慢（54.2 s → 77.6 s）。子状态超出容量改为只丢弃该子状态后，On 为 128.1 s（约 2.1 倍），该根 10.7 s。质量按协调器自己的口径（胜负、战略失血差、结束回合）比较：4 好 / 20 同 / 9 差，战略失血差合计 +11（每场约 +0.33）；按原始失血比较会误判，例如 IRONCLAD-15 的 On 路线多失 8 HP，但战斗内回复与燃烧之血使两边战略失血差都是 0。剩余差距来自 FlashBeam 比现有搜索简单的保留结构。

保留结构移植（RL）：把现有搜索在对照路线中丢失点的必保机制按出现次数移植进 FlashBeam：每药水分组与全局的防守、进攻、准备（`SetupLaneValue` 与 Scaling+Resource+Control 组合）、牌堆整理、压缩、力量压制与虚弱控制领先者，以及根动作谱系代表（`clamp(width/8, 4, 16)` 组），按 `RankBest` 的必保置换写回，席位总数不变；比较器逐条对照 `BeamRetentionPolicy.Ranking.cs`，只读已由 K4 核对的评估字段（新增 `HandCount` 并纳入 K4）。33 个牌组：质量 6 好 / 22 同 / 5 差、战略失血差合计 −5（优于 Off），On 总耗时 126.6 s → 129.8 s；48 根初始牌组基准对现有搜索 18 好 / 24 同 / 6 差、失血 −46。未移植：选项路由配额、药水配额与谱系、stand-pat 探测、Pareto、力量承诺、回合边界手牌领先者等（仍变差的 IRONCLAD-17 需要力量承诺或 stand-pat 资源探测）。

### 2.6 无人测试实机部署（2026-10-19）

Linux 无头游戏（Medium 档位、宽度组合、`Instant / 0 秒`，铁甲战士初始牌组，`NIBBITS_NORMAL` 原生满血）：On 与 Off 均 Passed、第 9 回合胜利、计划外重算 0；On 最终 59 HP，Off 54 HP，整场耗时 7.1 s 与 6.9 s（含建局）。On 的战后问题包有 6 行 `FLASH_ADOPTED`（全部 `status=verified`，单次 180–467 ms），`diagnostics/settings.json` 与 15 份检查点政策都记录 `"flashSearchMode": "On"`。`CHECKPOINT-PROFILE-CONTRACT` 增加 Flash 模式的记录值优先与旧包回退检查，Passed。该机器首次运行游戏前没有交互档案，用一次 `--force-steam=off` 无头启动在仓库 `.local/headless-seed/` 生成种子档案，并以 `XDG_DATA_HOME` 指向它运行脚本。

## 3. 已知限制与未验证项

- 只覆盖归档批次与 2.1 所列内容；其他内容在准入阶段拒绝，尚未有差分证据。
- 仍在战斗中的生物身上已移除 Power 的残留条目不能投影：根导入时拒绝，路线核对中遇到时按核对失败丢弃 Flash 路线；敌方离场后的残留按惰性处理（见内容批次归档卷 2.8.1）。
- 选择只覆盖卡牌自身的单选（消耗、移到抽牌堆顶、升级）；多选、回合开始选择、嵌套选择与自动打出中的选择尚无差分证据。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 实机部署只在 Linux 无头游戏中验证过 2.6 的一个自然根；未做可见游戏、Windows 或 Steam 会话验证，设置面板的新选项未在游戏中查看。
