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

### 2.4 FlashBeam 端到端（v1，离线宿主 `OFFLINE_HARNESS_FLASH_SOLVE=1`）

同一根先用现有 `CombatBeamSolver` 求解，再走 Flash：准入 → FlashBeam → 逐步旧引擎回放核对 → 固定前缀物化。两个根的 Flash 路线逐步核对全部一致，物化成功。宿主默认档位（Beam 24、2000 节点），种子 FLASH1：

| 根 | 现有求解 | Flash（宽度 ×1，进攻席位 25%） | Flash（宽度 ×4） |
| --- | --- | --- | --- |
| `FUZZY_WURM_CRAWLER_WEAK` | 失血 4，第 3 回合结束，约 360 ms | 失血 1，第 4 回合，冷启动约 120 ms，热运行 40–56 ms | 失血 1，第 4 回合，热运行约 110 ms |
| `NIBBITS_NORMAL` | 失血 28，第 5 回合，约 600 ms | 失血 42，第 5 回合，冷启动约 120 ms，热运行 60–80 ms | 失血 37，第 5 回合，热运行约 170 ms |

结论：管线与语义一致性已打通；FlashBeam v1 的排序只移植了现有评分的核心项，在多敌人根上路线质量明显低于现有搜索，尚不能开放。搜索内单步约 5–8 µs/转移，远高于内核基准 0.4 µs，开销主要在搜索层（每个子节点的威胁投影、比较器分配、父链回溯），属于待优化项。

## 3. 已知限制与未验证项

- 只覆盖上述纵切内容；其他内容在准入阶段拒绝，尚未有差分证据。
- 选择分支（`NeedsChoice`）在当前内容中不会出现，探针遇到时跳过并计数。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 未做可见游戏或实机部署验证；FlashBeam 只在离线宿主上与现有求解对照过两个根，未接入协调器。
