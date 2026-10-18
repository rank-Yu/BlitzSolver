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

## 2. 当前证据（2026-10-18，Linux）

F1 纵切与内容批次 P1–P12 的逐批证据、审查修正与跳过清单已归档到 [内容批次测试记录](../archive/flash-search/testing-content-batches-2026-10-18.md)；下面的全量回归覆盖其中全部根。

全量回归（`.local/tool-tasks/flash-search/regress.sh`，KS 合入后）：FlashKernelChecks 通过；29 个自然遭遇根（铁甲战士初始牌组）与 66 个固定根（`coverage/fixtures/cards/flash-*.json`）每根 32 条随机游走，K1 单步、K2 游走与 K4 评估全部零差异，没有准入拒绝。

### 2.1 强制行动与眩晕（KS，2026-10-18）

内核新增 `ForceMonsterMove` 与 `ForceStunnedMove`。规则编译给每个怪物追加一个合成 STUNNED 节点（无攻击、必须执行一次），其动态后继存于 `FlashEnemy.ForcedMove`，布局不变。`PrepareMonsterMovesForNextRound` 按现有引擎处理“眩晕且跳过下次行动时不推进”与“本轮已行动后被强制时不推进”。必须先执行一次的行动不再在编译时拒绝；导入映射进行中的 STUNNED 及其后继。新增遭遇乐加维林族母（首领）、噬尸蛞蝓（普通/弱）、骇鳗（精英），Power 沉睡、贪食、尖啸、活力；固定根 `coverage/fixtures/cards/flash-ks-*.json`。FlashKernelChecks 新增强制行动合同：缺省后继、不写行动日志、执行后推进、已行动后不推进、稳定边界清除标志。

审查修正：外部 agent 用反射清空导入私有 Fork 中乐加维林的 `_isAwake` 条目，现改为由 `DescribeFlashUnrepresentableState` 只放过这一个已证明惰性的条目，不修改模拟器。依据：现有引擎只在状态指纹与续接文本中读它，行动选择读的是沉睡层数。活力的私有状态原先按类型名认领，并依赖 Fork 时的断言；现改为通过 `VigorPowerMirrors` 的类型化访问认领，投影时显式要求状态空闲。

证据：四个遭遇的 FLASHM1/FLASHM2 自然根与四个固定根（每根 48 条游走）零差异。临时计数显示游走中眩晕确实触发并推进：乐加维林两个自然根的 74 次唤醒全部进入后继；噬尸蛞蝓普通的 280 次触发中 259 次推进，其余发生在本轮已行动之后，按规则不推进。

强制行动、眩晕与必须先执行一次的行动已有内核支持。仍跳过的 8 个第一幕遭遇及缺失能力如下；仪式兽、瀑布巨兽、异蛙寄生虫各自的强制行动来源随遭遇一起实现：

| 能力 | 遭遇 |
| --- | --- |
| 召唤/生成敌人 | 雾菇、双尾鼠（只缺这一项）；异蛙寄生虫、穿着一件大衣的两只地精、邪恶气体 |
| 苦难/附魔与改费用 Hook | 仪式兽、藤蔓蹒跚者、邪恶气体 |
| 阻止战斗结束、死亡后不离场 | 异蛙寄生虫、瀑布巨兽、穿着一件大衣的两只地精 |
| 偷窃与评估侧 | 穿着一件大衣的两只地精（被偷资源未建模）；瀑布巨兽（动态攻击伤害与评估特例） |

### 2.2 FlashBeam 端到端（v1，离线宿主 `OFFLINE_HARNESS_FLASH_SOLVE=1`）

同一根先用现有 `CombatBeamSolver` 求解，再走 Flash：准入 → FlashBeam → 逐步旧引擎回放核对 → 固定前缀物化。两个根的 Flash 路线逐步核对全部一致，物化成功。宿主默认档位（Beam 24、2000 节点），种子 FLASH1：

| 根 | 现有求解 | Flash（宽度 ×1，进攻席位 25%） | Flash（宽度 ×4） |
| --- | --- | --- | --- |
| `FUZZY_WURM_CRAWLER_WEAK` | 失血 4，第 3 回合结束，约 360 ms | 失血 1，第 4 回合，冷启动约 120 ms，热运行 40–56 ms | 失血 1，第 4 回合，热运行约 110 ms |
| `NIBBITS_NORMAL` | 失血 28，第 5 回合，约 600 ms | 失血 42，第 5 回合，冷启动约 120 ms，热运行 60–80 ms | 失血 37，第 5 回合，热运行约 170 ms |

结论：管线与语义一致性已打通；FlashBeam v1 的排序只移植了现有评分的核心项，在多敌人根上路线质量明显低于现有搜索，尚不能开放。搜索内单步约 5–8 µs/转移，远高于内核基准 0.4 µs，开销主要在搜索层（每个子节点的威胁投影、比较器分配、父链回溯），属于待优化项。

### 2.3 主搜索分发（Off/Shadow/On，协调器模式，2026-10-18）

离线宿主 `--search-mode Coordinator --budget-ms 10000`，宿主默认档位：

| 根 | 模式 | 结果 |
| --- | --- | --- |
| `NIBBITS_NORMAL` / FLASH1 | Off | 总扩展 1627、总转移 4896、最终失血 26 |
| 同上 | Shadow | 与 Off 完全相同（扩展、转移、分数、失血、求解器诊断行数）；对照行：旧主搜索失血 28、约 520 ms，Flash 失血 42、约 140 ms（搜索 94 ms），逐步核对通过 |
| 同上 | On | 采用 Flash 主搜索结果（失血 42），后续精炼与审计仍用旧后端，最终失血 28 |
| `flash-c1-choice-cards` | On | 采用 Flash 结果，零失血，协调器在战损目标处停止；总耗时 0.56 s |
| `flash-k3-basic-potions` | Shadow / On | 旧主搜索失血 27，Flash 失血 45（含药水动作），核对通过；两种模式最终失血都是 10 |
| 静默猎手初始牌组 / `NIBBITS_WEAK` | On | 准入拒绝 `UnsupportedCard STRIKE_SILENT`，回落旧后端 |

结论：分发、隔离、回落与药水覆盖均按设计工作。FlashBeam 主搜索的路线质量仍明显低于旧主搜索；当前只替换主搜索成员，整次请求的大部分耗时在旧后端的精炼与审计里，所以 On 的总耗时收益有限。

### 2.4 评估移植与 K4（S1，2026-10-18）

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

### 2.5 质量基准（48 根，2026-10-18）

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
| ×4，状态池按需增长（当前默认） | 13 / 28 / 7 | −9 | 24.3 s → 12.6 s |

结论：终局比较器修正了“同失血但多拖回合”的选择；进攻席位不可去掉，加大到 40% 也没有收益；同宽度下 FlashBeam 的保留结构仍弱于现有搜索。宽度与节点 ×4 是第一个总失血优于现有搜索的档位，端到端耗时约为其 0.5，因此作为 `FlashRouteMaterializer.ProfileWidthScale` 的默认值（软时间预算不变）。

状态池原按最坏情况预分配（每层宽度 × 122 个 `FlashBattle`，宽度 96 时四个池约 290 MB），现改为按需增长：48 根的路线与改动前逐根相同，每次求解的托管分配平均 19.8 MB、最大 33.6 MB。两次运行的总耗时 11.4 s 与 12.6 s 之差在单根 ±300 ms 的波动之内，未单独确认。游戏内档位（宽度 45/90/135）未做基准。

## 3. 已知限制与未验证项

- 只覆盖归档批次与 2.1 所列内容；其他内容在准入阶段拒绝，尚未有差分证据。
- 仍在战斗中的生物身上已移除 Power 的残留条目不能投影：根导入时拒绝，路线核对中遇到时按核对失败丢弃 Flash 路线；敌方离场后的残留按惰性处理（见归档卷 2.8.1）。
- 选择只覆盖卡牌自身的单选（消耗、移到抽牌堆顶、升级）；多选、回合开始选择、嵌套选择与自动打出中的选择尚无差分证据。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 未做可见游戏、无人测试或实机部署验证；分发只在离线宿主协调器模式下验证过 2.3 所列根。设置面板的新选项未在游戏中查看。
