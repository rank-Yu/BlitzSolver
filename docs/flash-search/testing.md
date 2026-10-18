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

## 2. 当前证据（F1 纵切，2026-10-18，Linux）

内容范围：铁甲战士初始牌组（打击、防御、痛击）、燃烧之血、力量、易伤、绒毛蠕虫爬行者与尼比特。

| 层 | 结果 |
| --- | --- |
| K0 | 布局尺寸（`FlashBattle` 6240 字节、`FlashCard` 32、`FlashEnemy` 64、`FlashPower` 24、RNG 40）、RNG 对原版算法参考副本 6000 次取值、introsort 与 `List.Sort` 在 4000 组随机数组上的置换一致、容量溢出抛出、牌堆段操作：全部通过 |
| K1/K2 | `FUZZY_WURM_CRAWLER_WEAK`、`NIBBITS_WEAK`、`NIBBITS_NORMAL` 各 6 个种子（FLASH1–FLASH6），每根 32 条随机游走：单步 135 次、游走约 12,500 次转移全部字节一致，所有游走走到终局 |

K0 同时输出一个 Beam 形工作负载的单步耗时（复制父状态到预分配槽、执行、哈希），在 vps4 上约 0.40 µs/转移，测量循环零托管分配（40 字节为一次性开销）。现有引擎同类工作约 30–45 µs/转移（`compact-real-combat` 实验与生产搜索统计），两者口径不同，只作量级参考。

### 2.1 第一批内容（P1，由外部 coding agent 按任务卡实现，2026-10-18）

新增双重打击、铁斩波、剑柄打击、耸肩无视、闪电霹雳、上勾拳，以及虚弱、脆弱、敏捷。固定根 fixture：`coverage/fixtures/cards/flash-p1-fuzzy-wurm-weak.json`、`flash-p1-nibbits-normal.json`、`flash-p1-nibbits-weak.json`（全部卡牌含升级版，三种 Power 分别挂在玩家和敌人身上）。每根 48 条随机游走：单步 25 次、游走 1,807 次转移全部一致，包含 3 次玩家死亡结局。

### 2.2 第二批内容（P2，2026-10-18）

新增铁甲战士卡牌 20 张：重锤、全身撞击、突破、御血术、血墙、完美打击、挑衅、破击、战栗、旋风斩、飞剑回旋镖、劫掠、欺凌、拆卸、灰烬打击、余烬、熔融之拳、与我一战！、焚烧、巨像；Power 巨像。完美打击按规则表中的原版卡牌标签统计打击牌。固定根：`coverage/fixtures/cards/flash-p2-a-basic-attacks.json`、`flash-p2-b-calculated.json`、`flash-p2-c-powers.json`、`flash-p2-d-x-random.json`。每根 48 条随机游走，单步 62 次、游走 1,900 次转移全部一致，包含 6 次玩家死亡结局。

外部 agent 跳过并说明原因的卡：凌虐与预备打击（临时力量的生命周期）、无情猛攻（改费用 Hook）、怨恨、扯碎、邪眼（尚未维护的回合与历史计数）、痛殴（跨卡的计算伤害）、倾泻（自动打出）、添柴（消耗后生成卡）。

### 2.3 内核扩展与第三批内容（P3，2026-10-18）

内核新增回合阶段骨架（Power 与遗物的阶段方法、`ShouldClearBlock`、`ModifyMaxEnergy`、`ModifyHandDraw`）与玩家本回合计数、生物本回合失血标记；导入改为在私有 Fork 上读取。以上两项各自在 3 个纵切根与当时全部覆盖 fixture 上（每根 32 条随机游走）零差异。

P3 新增铁甲战士卡牌 13 张：燃烧、无惧疼痛、势不可当、残酷、岿然不动、狂宴、祭品、放血、重振精神、跃跃欲试、愤怒、契约终结、主宰；Power：无惧疼痛、势不可当、残酷（易伤倍率加成）。固定根：`coverage/fixtures/cards/flash-p3-a-power-cards.json`、`flash-p3-b-block-exhaust.json`、`flash-p3-c-self-damage.json`、`flash-p3-d-feed-fatal.json`。外部 agent 交付时每根 48 条游走零差异；审查后改为通过 `CloneCard` 生成愤怒的复制品（清除牌组链接，与原版 `AfterCloned` 一致），重振精神只消耗起始快照内的牌；合入主线后连同 P1/P2 与纵切共 14 根、每根 32 条游走零差异。

未验证：fixture 注入的卡都不是牌组卡，愤怒复制品清除牌组链接这一点只有源码对照，没有差分覆盖。外部 agent 跳过的卡（原因均为当时内核缺少的阶段或 Hook）：狂怒、火焰屏障、撕裂（旧引擎写入 `RupturePredictionState`，导入拒绝）、凶恶（`AfterPowerAmountChanged`）、岩石铠甲、坚定不移、薪火之源、惊逃、地狱狂徒、杂耍、连环拳、绯红披风。

### 2.4 选择（2026-10-18）

内核新增卡牌选择（`OwnChoice`、`ChooseCards`/`ChooseCardsInRange`、消耗/移动/升级选中卡、卡牌升级）与 Bridge 选择翻译；新增武装、头槌、燃烧契约、坚毅、烙印。固定根 `coverage/fixtures/cards/flash-c1-choice-cards.json`（五张卡各含升级版，弃牌堆有相同的卡，无惧疼痛在场让选择中的消耗触发 Hook）：64 条随机游走中单步 64 次、游走 1,916 次转移零差异，其中 634 次带选择的动作，旧引擎每个选择请求的效果、来源、时机、数量与同序选项都与 Flash 一致。调试中发现并修正：`Spec` 无选项时不提问、`BuildRequiredEmptyChoice` 的空选择、相同卡按出现序号定位。合入后 15 根回归（每根 32 条游走）零差异。

端到端（`OFFLINE_HARNESS_FLASH_SOLVE=1`，同一 fixture，Beam 24、2000 节点）：现有求解失血 1、约 2.5 s；Flash 失血 0、约 223 ms（热运行约 150 ms），路线含 6 次选择，逐步核对与固定前缀物化全部通过。

### 2.5 第四、五批内容（P4、P5，2026-10-18）

P4 新增铁甲战士卡牌 13 张：火焰屏障、薪火之源、恶魔形态、壁垒、凌虐、预备打击、怨恨、暴走、恶魔之焰、邪眼、被遗忘的仪式、战鼓、时候未到；Power：火焰屏障、薪火之源、恶魔形态、壁垒、凌虐与预备打击的临时力量。固定根 `coverage/fixtures/cards/flash-p4-*.json` 四个。审查修正：外部 agent 用"先加力量、未生效再撤回"模拟临时力量，在有人工制品时与现有引擎不同；内核新增 `ApplyTemporaryStrength`（人工制品先判临时 Power，首次施加走 `BeforeApplied`，叠加走数量比较）与 `RetireTemporaryStrength`，并加固定根 `flash-k2-temporary-strength-artifact.json`（对方柱构装体，开局带人工制品）。

P5 新增第一幕（密林）怪物 16 种：缩小甲虫、树叶史莱姆（小/中）、树枝史莱姆（小/中）、飞蝇菌子、蛮兽、闪光贾克斯果、劫掠者斧手/刺客/暴徒/弩手/追踪手、方柱构装体、蛇行扼杀者、多尼斯异鸟；Power：缩小、紧缠、领地意识、人工制品；状态牌：黏液。审查修正：外部 agent 因内核没有死亡阶段 Hook，用攻击后与出牌后的 Hook 模拟"施加者死亡时清除缩小与紧缠"；内核改为在 `ApplyEnemyDeathPowers` 的对应位置新增 `ApplierDied` 阶段（紧缠、恶咒、缩小按现有代码顺序，再到魔法炸弹），两种 Power 改用它。

证据（合入并修正后，每根 48 条游走，铁甲战士初始牌组的自然遭遇根，不用 fixture——fixture 加载器会把敌人生命改成 1）：遭遇缩小甲虫、一群史莱姆、一大群史莱姆、真菌与史莱姆、蛮兽、红宝石劫掠者、密林植物、方柱构装体、扼杀者与伙伴、密林爬虫、多尼斯异鸟（精英）与三个纵切根共 14 根，单步 115 次、游走 15,154 次转移零差异；固定根 31 根（每根 32 条游走）零差异。

P4 跳过（缺少的能力）：狂怒、杂耍、黑暗之拥（现有引擎为它们写入 `PredictionStateStore` 条目，导入拒绝）；绯红披风、狱火（Power 私有动态变量未投影）；岩石铠甲（覆甲的 `BeforeSideTurnEndEarly` 阶段）；坚定不移（单次出牌的格挡事件计数）；好勇斗狠（`UnstableShuffle`）；痛殴、扯碎、踩踏（跨卡伤害变量、历史命中次数、费用修饰容量）；添柴、地狱之刃、原始力量（卡池随机生成、变换）；彼岸咆哮（自动打出）；肉盾、群情激愤、午夜、恶魔护盾、炽焰为多人专用。P5 跳过的遭遇（按怪物）：雾菇（召唤）、墨宝（`ModifyHpLost` Hook）、藤蔓蹒跚者（苦难）、旧日雕像（缓慢的 `PredictionStateStore` 计数）、异蛙寄生虫（感染的回合末在手效果与召唤）。

### 2.6 药水与状态投影（2026-10-18）

内核新增药水动作（`UsePotionAction`、合法目标、规则表药水数值），Bridge 新增药水翻译与 `FlashStateProjections`（`PredictionStateStore` 条目逐个认领，未认领即拒绝；当前只认定等于 Power 数量的 `PowerAmountPredictionState` 为无效条目）；新增火焰药水、格挡药水、力量药水、能量药水。固定根 `coverage/fixtures/cards/flash-k3-basic-potions.json`（初始牌组 + 三瓶药水）每根 48 条游走：单步 16 次（含 4 个药水动作）、游走 1,092 次转移零差异。端到端：Flash 用了格挡与火焰两瓶药水、胜利时 73 HP、约 182 ms，逐步核对与物化通过；现有求解未用药水、51 HP、约 1.7 s——两者药水策略不同（现有搜索有无药基线等），不作质量对比。

### 2.7 第六、七批内容与内核补点（P6、P7，2026-10-18）

P6 新增第一幕（暗港）怪物 8 种：钙化邪教徒、潮湿邪教徒、幽灵船、拳击构装体、海洋混混、下水道蚌、淤泥旋螺、蟾蜍蝌蚪；Power：仪式、覆甲、荆棘；状态牌：晕眩。审查修正：外部 agent 在 `ModifyDamageAdditive` 里触发荆棘反伤（修正 Hook 带副作用，评分与预览也会调用它），内核改为在 `TryDamageTarget` 的现有位置新增 `BeforeDamageReceived` 派发；仪式原以 `AmountOnTurnStart` 近似"刚由敌人施加"，改为内核在施加时设置 `WasJustAppliedByEnemy`（`MarksJustAppliedByEnemy`）、导入投影 `_wasJustAppliedByEnemy`、定义按 `ConsumeRitualApplicationDelay` 消耗；覆甲在玩家身上的回合末格挡属于 `BeforeSideTurnEnd` 的 Early 阶段，内核新增玩家回合结束第一阶段的 `BeforeSideTurnEnd`（VeryEarly、Early、普通三轮）派发。新增固定根 `flash-k4-player-plating-thorns.json`（玩家带覆甲与荆棘）。

P7 新增遗物 85 个：战斗中无效果或只在根之前结算的 65 个写空定义（逐个注明依据），20 个按现有引擎的 Hook 或遗物阶段实现；需要遗物私有状态的 27 个与缺少能力的 13 个跳过。审查修正：斗篷扣、尖叫酒壶、历石、波纹水盆属于 `BeforeSideTurnEnd` Hook 的普通阶段而非回合结束开头的遗物批次，改挂到新派发点；遗物批次方法改名 `PrepareBeforeSideTurnEnd`（与 Hook 同名的重载曾让注册表的反射查找报二义性，`ComputeHooks` 改为按 `FlashListenerDef` 上的基方法精确匹配）。固定根 `coverage/fixtures/cards/flash-p7-*.json` 八个。

内核另外新增：`DoTurnEnd` 改为先对手牌一次分出回合末在手牌与虚无牌并按值与出现序号重新定位，支持 `OnTurnEndInHand`；遗物私有状态投影（`StatefulRelicState` 由投影认领，未认领即拒绝）与规则表遗物变量；不可搜索的药水不产生动作（`Searchable`）。

证据：合入并修正后，22 个自然遭遇根（密林 11、暗港 8、纵切 3，每根 32 条游走）与 27 个固定根零差异，FlashKernelChecks 通过。

### 2.8 死亡与出牌限制 Hook，第八至十批内容（P8–P10，2026-10-18）

内核新增：`BeforeDeath`、`ShouldDie`（含 Late、阻止者 `AfterPreventingDeath` 与重试）、`AfterDeath`、`AfterCurrentHpChanged`、`AfterBlockBroken`、移出名单判定（`PreventsRemovalAfterDeath`）；`ShouldPlay` 与着魔的出牌锁；`ShouldFlush`；玩家回合结束第一阶段末的 `CompletePlayerEarlySideTurnEnd`。修正 `AfterCardPlayedCleanup` 按位判断过期方式（原版 `HasFlag(WhenPlayed)`，原实现用相等比较，`EndOfTurn | WhenPlayed` 的修饰不会在出牌后清除）。死亡后判断主要敌人时与现有引擎一致只看名单内生物的 Power。

P8 新增带私有状态的遗物 20 个与 Power/卡 5 组（状态逐项投影到 `FlashRelic.Counter/Aux` 或 `FlashPower.Aux0`），共享遗物的数值改为规则表读取；审查把 Orichalcum 状态的反射读取改为 `OrichalcumMirrors.ShouldTriggerForFlash`。P9 新增药水 36 种与药水相关 Power 9 个；审查把再生的玩家侧递减改到 `CompletePlayerEarlySideTurnEnd`，保留手牌改用 `ShouldFlush`（原实现给手牌打本回合保留标记）。P10 新增状态牌 8 张、诅咒牌 14 张。固定根 `coverage/fixtures/cards/flash-p8-*.json`、`flash-p9-*.json`、`flash-p10-*.json`。

证据：合入并修正后 22 个自然遭遇根与 49 个固定根（每根 32 条游走）零差异，FlashKernelChecks 通过。仍跳过、现已有内核支持可补的：律动残余、蜥蜴尾巴、红头骨、罐装幽灵与幸运补剂、瓶中精灵、虚空、悔恨。

### 2.8.1 第十一批内容（P11，2026-10-18）

新增遗物律动残余、蜥蜴尾巴、红头骨；药水罐装幽灵、幸运补剂、瓶中精灵（自动药水，不提供显式使用）、异蛇之油；卡牌虚空、悔恨、执迷、凡庸、战斗专注；Power 缓冲、不可抽牌、滑溜、缓慢；遭遇墨宝（INKLETS_NORMAL）与旧日雕像（BYGONE_EFFIGY_ELITE）。私有状态投影：律动残余、蜥蜴尾巴各自的 PredictionState，红头骨的 StatefulRelicState，缓慢的 CounterPredictionState。固定根 `coverage/fixtures/cards/flash-p11-*.json`。

证据：7 个新固定根与两个新遭遇的 FLASHM1/FLASHM2 种子（每根 48 条游走）零差异；22 个自然遭遇根与 49 个既有固定根（每根 32 条游走）零差异，FlashKernelChecks 通过。

仍跳过：花园幽灵鳗（原版初始状态是未登记进状态机的条件分支，`FlashRulesCompiler` 尚不能解析）；鬼祟珊瑚群（定义在批内因残留条目问题被删除，待重做）。

合入后补充：敌方生物离开战斗（死亡移除或逃跑）后，其 Power 在 `PredictionStateStore` 中的残留条目按惰性处理（`FlashStateProjections.RequireAllClaimed`）。依据：现有引擎只经由仍在战斗中的生物身上的 Power 读取这些条目；原版召唤总是新建生物，只有玩家宠物会被重新加入，所以限定为敌方。新固定根 `flash-p11-h-bygone-effigy-kill.json`（旧日雕像 15 HP）48 条游走全部击杀雕像，零差异。

### 2.8.2 第一幕剩余遭遇（P12，2026-10-18）

新增遭遇墨影幻灵（VANTOM_BOSS）、同族小队（THE_KIN_BOSS）、化石追踪者、花园幽灵鳗、鬼祟珊瑚群、灵魂异鱼（SOUL_FYSH_BOSS），新增 Power 爪牙、胆小、吮吸、硬化外壳。私有状态投影：胆小与硬化外壳各自的 PredictionState。首领与精英各有固定根 `coverage/fixtures/cards/flash-p12-*.json`（玩家高血量、敌人降血或强制初始行动，让每个行动、循环与离场都在游走中出现）。

证据：6 个遭遇的 FLASHM1/FLASHM2 自然根与 5 个固定根（每根 48 条游走）K1/K2/K4 零差异；22 个自然遭遇根与 62 个固定根（每根 32 条游走）零差异，FlashKernelChecks 通过。评估侧无改动：新 Power 都是敌方 Power，不参与玩家侧战略估值。

仍跳过的 12 个第一幕遭遇及缺失的内核能力：

| 能力 | 遭遇 |
| --- | --- |
| 强制行动/眩晕 | 乐加维林族母、噬尸蛞蝓（普通/弱）、骇鳗（只缺这一项）；仪式兽、瀑布巨兽、异蛙寄生虫 |
| 召唤/生成敌人 | 雾菇、双尾鼠（只缺这一项）；异蛙寄生虫、穿着一件大衣的两只地精、邪恶气体 |
| 苦难/附魔与改费用 Hook | 仪式兽、藤蔓蹒跚者、邪恶气体 |
| 必须先执行一次的行动 | 仪式兽、瀑布巨兽 |
| 阻止战斗结束、死亡后不离场 | 异蛙寄生虫、瀑布巨兽、穿着一件大衣的两只地精 |
| 偷窃与评估侧 | 穿着一件大衣的两只地精（被偷资源未建模）；瀑布巨兽（动态攻击伤害与评估特例） |

### 2.9 FlashBeam 端到端（v1，离线宿主 `OFFLINE_HARNESS_FLASH_SOLVE=1`）

同一根先用现有 `CombatBeamSolver` 求解，再走 Flash：准入 → FlashBeam → 逐步旧引擎回放核对 → 固定前缀物化。两个根的 Flash 路线逐步核对全部一致，物化成功。宿主默认档位（Beam 24、2000 节点），种子 FLASH1：

| 根 | 现有求解 | Flash（宽度 ×1，进攻席位 25%） | Flash（宽度 ×4） |
| --- | --- | --- | --- |
| `FUZZY_WURM_CRAWLER_WEAK` | 失血 4，第 3 回合结束，约 360 ms | 失血 1，第 4 回合，冷启动约 120 ms，热运行 40–56 ms | 失血 1，第 4 回合，热运行约 110 ms |
| `NIBBITS_NORMAL` | 失血 28，第 5 回合，约 600 ms | 失血 42，第 5 回合，冷启动约 120 ms，热运行 60–80 ms | 失血 37，第 5 回合，热运行约 170 ms |

结论：管线与语义一致性已打通；FlashBeam v1 的排序只移植了现有评分的核心项，在多敌人根上路线质量明显低于现有搜索，尚不能开放。搜索内单步约 5–8 µs/转移，远高于内核基准 0.4 µs，开销主要在搜索层（每个子节点的威胁投影、比较器分配、父链回溯），属于待优化项。

### 2.10 主搜索分发（Off/Shadow/On，协调器模式，2026-10-18）

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

### 2.11 评估移植与 K4（S1，2026-10-18）

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

## 3. 已知限制与未验证项

- 只覆盖上述纵切内容；其他内容在准入阶段拒绝，尚未有差分证据。
- 仍在战斗中的生物身上已移除 Power 的残留条目不能投影：根导入时拒绝，路线核对中遇到时按核对失败丢弃 Flash 路线；敌方离场后的残留按惰性处理（2.8.1）。
- 选择只覆盖卡牌自身的单选（消耗、移到抽牌堆顶、升级）；多选、回合开始选择、嵌套选择与自动打出中的选择尚无差分证据。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 未做可见游戏、无人测试或实机部署验证；分发只在离线宿主协调器模式下验证过 2.10 所列根。设置面板的新选项未在游戏中查看。
