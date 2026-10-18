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

### 2.2 评估移植与 K4（S1，2026-10-18）

`FlashScorer` 移植现有搜索的状态评估（`CombatBeamSolver.Snapshot`）与中途排序（`BeamRankScore`），`FlashScoreBridge` 在根上编译逐内容数值表并冻结请求设置；FlashBeam 改用它们排序。新增路径计数 `LongTermResourceValue`、`AngerCopiesGenerated`、`DeathSaveRelicHpRestored`、`DeathSavePotionHpRestored`、`DeathSaveUseCount`、`AutomaticPotionUseCount`。

K4 证据：22 个自然遭遇根与 57 个固定根（每根 32 条游走）中投影一致的全部状态，评估逐字段相等，`Score` 逐位相等；没有根因设置被拒绝。

评估暂不移植、因而在根上拒绝的设置：非零成长额度、非空遗物计数目标（遗物策略默认开启，持有计数类遗物的局会被拒绝并回落）、第三幕首领策略、开发策略、第三方战略效果与移除估值登记、`Profile` 的仅基础分/连续威胁/权重扰动/上下文排序、偷窃遭遇、瀑布巨人与遗忘者的伤害特例。

端到端对照（FLASHM1，预算 10 秒，同宽度同节点）：

| 根 | 现有搜索失血 / 结束回合 / 耗时 | Flash 失血 / 结束回合 / 端到端耗时（其中搜索） |
| --- | --- | --- |
| `NIBBITS_NORMAL` | 29 / 8 / 587 ms | 37 / 8 / 218 ms（87 ms） |
| `SLIMES_NORMAL` | 19 / 8 / 773 ms | 20 / 12 / 304 ms（157 ms） |
| `RUBY_RAIDERS_NORMAL` | 35 / 5 / 544 ms | 37 / 5 / 280 ms（151 ms） |

结论：评估已与现有搜索一致，剩余质量差距来自搜索结构（去重保留、层内泳道、按层预算、终局比较器，见 [搜索与接入](search.md#1-flashbeamsolver)）。

### 2.3 质量基准（48 根，2026-10-18）

`.local/tool-tasks/flash-search/bench.sh`（一次性工具，任务结束随 `.local` 清理）：24 个已准入自然遭遇 × 种子 FLASHM1/FLASHM2，铁甲战士初始牌组，宿主默认档位（Beam 24、2000 节点、预算 10 秒），同一根先跑现有 `CombatBeamSolver`，再跑 Flash（准入 → FlashBeam → 逐步核对 → 物化，取热运行耗时）。按“胜利、失血、结束回合”比较：

| Flash 配置 | 更好 / 相同 / 更差 | 失血合计差 | 总耗时：现有 → Flash |
| --- | --- | --- | --- |
| 评估移植后（S1） | 7 / 20 / 21 | +78 | 24.3 s → 5.0 s |
| 加终局比较器 | 7 / 22 / 19 | +81 | 24.4 s → 5.2 s |
| 同上，进攻席位 0 | 3 / 12 / 33 | +342 | 24.9 s → 5.7 s |
| 同上，宽度与节点 ×2 | 8 / 27 / 13 | +41 | 25.2 s → 8.0 s |
| 同上，宽度与节点 ×3 | 11 / 27 / 10 | +24 | 24.6 s → 9.4 s |
| 同上，×3，进攻席位 40% | 10 / 26 / 12 | +27 | 25.1 s → 9.7 s |
| 同上，×4 | 13 / 28 / 7 | −9 | 25.3 s → 11.4 s |
| ×4，状态池按需增长 | 13 / 28 / 7 | −9 | 24.3 s → 12.6 s |
| ×4，子状态在池槽内原地执行（当前默认） | 13 / 28 / 7 | −9 | 24.5 s → 10.5 s |

结论：终局比较器修正了“同失血但多拖回合”的选择；进攻席位不可去掉，加大到 40% 也没有收益；同宽度下 FlashBeam 的保留结构仍弱于现有搜索。宽度与节点 ×4 是第一个总失血优于现有搜索的档位，端到端耗时约为其 0.5，因此作为 `FlashRouteMaterializer.ProfileWidthScale` 的默认值（软时间预算不变）。

状态池原按最坏情况预分配（每层宽度 × 122 个 `FlashBattle`，宽度 96 时四个池约 290 MB），现改为按需增长：48 根的路线与改动前逐根相同，每次求解的托管分配平均 19.8 MB、最大 33.6 MB。两次运行的总耗时 11.4 s 与 12.6 s 之差在单根 ±300 ms 的波动之内，未单独确认。游戏内档位（宽度 45/90/135）未做基准。

### 2.4 主搜索 incumbent 下界剪枝（SB，2026-10-19）

`FlashBeamSolver` 移植现有主搜索成员的局部 HP 下界剪枝。`FlashBoundFeatures` 在根上冻结现有引擎给出的 `FutureHealPotential`、剩余治疗证书（Component / KnownNative）、战略首领减伤与初始 incumbent；求值只读 `FlashBattle` 和冻结表。完整胜利在终局提交时收紧 incumbent，每个动作层保留之后与回合边界保留之后各剪一次；被剪掉的席位不补。共享 `PrimaryIncumbents` 表不接入，初始 `PrimaryIncumbent` 由 `BeamSolveDispatch.SolveFlash` 传入。

K5 证据（全量回归 `regress.sh sb-regression 32`）：33 个自然根与 68 个固定根，98 个根 `bound=checked`、3 个根显式禁用（`flash-p3-b-block-exhaust`、`flash-p3-d-feed-fatal` 为 `bound=disabled(growth_targets)`，`flash-p9-offensive-powers` 为 `bound=disabled(regen_potion_policy)`）；每根 `failed=0`、`walkFailed=0`、`scoreFailed=0`、`boundFailed=0`，无 REJECTED。代表首行：

```
SUMMARY oneStep=6 failed=0 walkTransitions=594 walkFailed=0 terminals=32 choiceActions=0 choiceCapped=0 choiceDeadEnds=0 endings=Victory/None:32 score=checked scoreChecked=601 scoreFailed=0 scoreSkippedBoundary=0 bound=checked boundChecked=601 boundFailed=0
```

质量下降的 6 个根另做 128 条游走定向 K5：`FUZZY_WURM_CRAWLER_WEAK-FLASHM2`、`NIBBITS_WEAK-FLASHM1`、`PUNCH_CONSTRUCT_NORMAL-FLASHM1`、`SEAPUNK_WEAK-FLASHM1`、`SLIMES_WEAK-FLASHM1`、`SLIMES_WEAK-FLASHM2`，合计 `boundChecked=17043`、`boundFailed=0`，下界输入在这些状态上仍逐字段一致。

质量与耗时（同一台机器；基线为改动前构建，加剪枝为改动后构建；每行的现有搜索与 Flash 来自同一次运行。24 个自然遭遇 × FLASHM1/FLASHM2，预算 10 秒，宽度与节点 ×4）：

| Flash 配置 | 更好 / 相同 / 更差 | 失血合计差 | 现有耗时 → Flash 热耗时 | 扩展数合计 | 转移数合计 |
| --- | --- | --- | --- | --- | --- |
| 无剪枝（改动前默认） | 13 / 28 / 7 | −9 | 24.6 s → 11.2 s | 243,294 | 1,038,405 |
| 加 incumbent 下界剪枝 | 14 / 24 / 10 | −16 | 23.8 s → 5.5 s | 64,058 | 283,787 |

`searchMs` 合计从 10,018.7 ms 降到 4,319.8 ms，转移数减少 72.7%。协调器 Off/On（`coord.sh sb-coord 10000 M2 FLASHM1 FLASHM2`，`WARMUPS=2`）：

| 配置 | On 更好 / 相同 / 更差 | 失血合计差 | Off 总耗时 | On 总耗时 | On 模式 `flash_transitions` 合计 |
| --- | --- | --- | --- | --- | --- |
| 改动前 | 13 / 28 / 7 | −2 | 18.5 s | 17.8 s | 875,377 |
| 加剪枝 | 12 / 27 / 9 | −2 | 18.6 s | 15.4 s | 283,787 |

逐根变差分析：

- 相对无剪枝基线变差 6 根：`FUZZY_WURM_CRAWLER_WEAK-FLASHM2`、`NIBBITS_WEAK-FLASHM1`、`PUNCH_CONSTRUCT_NORMAL-FLASHM1`、`SEAPUNK_WEAK-FLASHM1`、`SLIMES_WEAK-FLASHM1`、`SLIMES_WEAK-FLASHM2`。其中前两根的新结果与现有搜索完全相同（4/3、4/4），只是无剪枝 Flash 曾找到更好的路线；后四根新结果比现有搜索多 3–5 点失血。
- 这四根的路线在第二回合左右就分叉：新 Flash 先找到一个较差但更早的完整胜利，收紧后的下界把原先的防守线剪掉；现有搜索在同样的 `ShouldPruneByPrimaryIncumbent` 语义下没有剪掉该线，因为它先找到/保留了另一条胜利，或者该线仍在现有保留泳道（多样性/Pareto）里。K5（含上述 128 条游走）证明两边的下界输入与最终 `StrategicHpLowerBound` 相同，所以差异来自 incumbent 的发现顺序与 FlashBeam 更简单的保留结构，而不是剪枝语义或近似值。
- 没有通过加宽 Beam、提高节点上限或关闭剪枝来掩盖；这些根在禁用剪枝的配置下仍会回到无剪枝基线结果。

未移植与显式禁用：

- 共享 `PrimaryIncumbents` 表（对照/启用隔离）：不接入。
- `root.UsesComponentHealingCertificate` 且根药水含 `RegenPotion` 时，Component 上界依赖药水策略；该根禁用剪枝（`regen_potion_policy`）。
- `policy.EffectiveHasGrowthTargets` 非空的根在现有搜索中本来就不用局部下界；Flash 显式禁用（`growth_targets`）。`InitialGrowthRewards` 非零或可达 FEED 时也禁用（`growth_rewards` / `feed_growth`）。
- `RemainingHealingUpperBound`（Regent/Silent/Defect 的证书）与 `KnownNativeHealingPotential` 的 `SwipePower` 分支未移植；对应根禁用（`remaining_certificate` / `swipe_power`）。
- Flash 没有 risk 通道；K5 直接比较现有快照的 `HasRisk`，任何被覆盖状态记录 risk 都会使 K5 失败。当前全部根为零差异。

### 2.5 协调器宽度组合（2026-10-19）

分发点扩展到精炼成员，并新增内核 `BeforeAttack` Hook（活力的捕获从修正 Hook 移入，修正 Hook 不再改写状态）。全量回归 101 根 K1/K2/K4/K5 零差异。

`WARMUPS=2 COORD_EXTRA_ARGS=--use-portfolio coord.sh`（48 根，宿主默认档位，开启生产默认的宽度组合）：On 相对 Off 14 好 / 30 同 / 4 差、失血合计 −31，总耗时 27.0 s → 23.9 s。Flash 采用主搜索 144 次、精炼成员 288 次；精炼成员回落 249 次（仅基础分与带权重扰动的有界进攻成员在准入时拒绝）。

随后移植仅基础分与权重扰动两种中途排序变体（公式照搬 `CombatBeamSolver.BeamRankScore`，输入项均已由 K4 逐字段核对）。实机默认的 Medium 档位（宽度 60、120,000 节点、120 秒，开启宽度组合，24 个自然根 FLASHM1，每进程预热 1 次）：

| 配置 | On 更好 / 相同 / 更差 | 失血合计差 | Off → On 总耗时 | 精炼成员 |
| --- | --- | --- | --- | --- |
| 变体未移植 | 2 / 19 / 3 | +1 | 21.9 s → 17.6 s | 采用 96、回落 82 |
| 变体已移植 | 2 / 18 / 4 | +3 | 21.2 s → 15.0 s | 采用 144、回落 0 |

另试过把评估的威胁投影改为直接读取被评估状态、首次写入前才复制：路线与转移数不变，搜索耗时 4.32 s → 4.47 s，没有可测收益，未保留。

### 2.6 固定前缀续接分发（FC，2026-10-19）

`FrontierContinuationScheduler.Dispatch`/`DispatchOptional` 改走 `BeamSolveDispatch.SolveContinuation`，只把无药、无承诺、无侦察、无最早药水回合的干净请求交给 Flash；前缀由旧引擎按 `ApplyFixedPrefix` 同一规则回放，Flash 从前缀末尾搜索，动作计数从 `Prefix.Length` 起算，物化仍把前缀 + Flash 路线整条交给现有求解器。全量回归 `regress.sh fc-regression 32` 101 根 K1/K2/K4/K5 零差异（K0 通过），SUMMARY 见任务记录。

协调器对照（同一台机器，`WARMUPS=1 COORD_EXTRA_ARGS="--use-portfolio --profile Medium" coord.sh ... 120000 M2 FLASHM1`，24 个自然根）：

| 配置 | On 更好 / 相同 / 更差 | 失血合计差 | Off 总耗时 | On 总耗时 |
| --- | --- | --- | --- | --- |
| 改动前（fc-coord-baseline） | 2 / 18 / 4 | +3 | 22.1 s | 15.3 s |
| 固定前缀续接（fc-coord） | 3 / 17 / 4 | −1 | 23.0 s | 8.4 s |

Off 侧 24 根的 `search.solverMetrics`、`search.planActions`、`search.cachedContinuations` 与 `quality.json` 与改动前逐字段相同。相对改动前只有 `NIBBITS_NORMAL-FLASHM1` 变化：28 失血 / 9 回合 → 24 / 8。4 个变差根（`INKLETS_NORMAL`、`OVERGROWTH_CRAWLERS`、`RUBY_RAIDERS_NORMAL`、`SEAPUNK_NORMAL`）与改动前的 On 结果完全相同，属于既有主搜索/精炼成员的 Flash 保留结构差异；其中 `OVERGROWTH_CRAWLERS` 与 `SEAPUNK_NORMAL` 的续接请求被 Flash 采用，但最终选择仍与改动前 On 相同。

续接 Flash 行按用途统计（`fc-coord` 的 On 日志）：`OpeningTargetVariant` 采用 50 次（前缀 3 动作 ×10、4 动作 ×40）、`TurnBoundaryRescue` 采用 22 次（前缀 3×4、4×18），回落 0 次；`PrimaryBeam` 采用 72、`RefinementBeam` 采用 216。

Shadow 隔离（`NIBBITS_NORMAL`、`SLIMES_NORMAL`、`RUBY_RAIDERS_NORMAL`，FLASHM1，Medium，120 秒，宽度组合）：Off 与 Shadow 的 `search.solverMetrics` 全部字段、`quality.json`、`search.planActions` 完全相同；3 根合计 26 行 Shadow 对照（其中 11 行续接，`OpeningTargetVariant prefix=4`），全部 `same_route=false`（Flash 找到不同路线，Shadow 仍返回旧结果）。示例：`NIBBITS_NORMAL` 8 行、`SLIMES_NORMAL` 9 行、`RUBY_RAIDERS_NORMAL` 9 行。

## 3. 已知限制与未验证项

- 只覆盖归档批次与 2.1 所列内容；其他内容在准入阶段拒绝，尚未有差分证据。
- 仍在战斗中的生物身上已移除 Power 的残留条目不能投影：根导入时拒绝，路线核对中遇到时按核对失败丢弃 Flash 路线；敌方离场后的残留按惰性处理（见内容批次归档卷 2.8.1）。
- 选择只覆盖卡牌自身的单选（消耗、移到抽牌堆顶、升级）；多选、回合开始选择、嵌套选择与自动打出中的选择尚无差分证据。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 未做可见游戏、无人测试或实机部署验证；分发只在离线宿主协调器模式下验证过归档卷 2.3 所列根。设置面板的新选项未在游戏中查看。
