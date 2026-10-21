# Flash 测试记录

[返回 Flash 入口](README.md)

本文记录 Flash 的可复跑验证入口与当前证据。方法定义见 [准入与校验](admission-and-verification.md)。

## 1. 复跑入口

已提交的入口是 [Flash 回归工具](../../tools/search/FlashRegression/README.md)：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py <子命令> [参数]
```

工具负责 Release 构建与 `OfflineSearchHarness` 构建，`diff`/`scan` 默认再运行 K0；它调用现有宿主并把结果写到
`.local/flash-regression/<label>/`。旧的 `.local/tool-tasks/flash-search/*.sh` 不再作为复跑入口。

纯内核合同（K0）与两次构建：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py build
```

差分探针（K1 单步穷举、K2 随机游走）的全量根集合是 493 根（162 个自然遭遇与第三幕首领根、
267 个 `coverage/fixtures/cards/flash-*.json` 固定根、64 个 `coverage/corpora/flash-decks/`
生成牌组），默认每根 32 条游走、4 个并发进程：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py diff --label flash-diff --walks 32
```

只复跑一个固定根或一个牌组：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py diff \
  --label one --only fixtures --root flash-dx5-pen-nib-sly
python3 -B tools/search/FlashRegression/flash_regression.py diff \
  --label one --only decks --root deck-SILENT-13
```

`--include-act3`（或 `--only act3`）加入 `coverage/corpora/flash-act3/` 的 150 个第三幕牌组，
默认每根 48 条游走。`--shards N` / `--shard k` 按旧脚本的根下标分片；`--baseline <旧 SUMMARY.txt>`
按旧回归脚本的逐行比较口径，输出 `BASELINE.txt`。结果目录的 `SUMMARY.txt` 每行是
`<root> exit=<code> <flash-diff.txt 首行>`，末行 `DONE`；差分探针把汇总写在 `flash-diff.txt` 首行，
并在 `harness-result.json` 的 `flashDiff` 字段记录同样的计数。`OFFLINE_HARNESS_FLASH_DIFF_STEPS`
控制每条随机游走的最大步数（默认 60）。

最终候选路线扫描（每根 Flash 求解后把排序前 N 条候选交现有引擎逐条核对）：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py scan --label flash-scan --candidates 8
```

48 根质量基准与 79 根协调器对照：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py bench --label flash-bench --budget-ms 10000
python3 -B tools/search/FlashRegression/flash_regression.py coordinator --label flash-coord
```

协调器的关闭模式基线可用 `--off-baseline <旧结果目录>` 复用；只复跑一根时用
`--ids <目录名>`，例如 `--ids SILENT-C-13`。

需要直接调试宿主时，底层单根命令仍是：

```bash
OFFLINE_HARNESS_FLASH_DIFF=1 OFFLINE_HARNESS_FLASH_DIFF_WALKS=32 \
  dotnet .local/tool-build/OfflineSearchHarness/bin/Release/net9.0/OfflineSearchHarness.dll \
  --character IRONCLAD --encounter NIBBITS_NORMAL --seed FLASH1 --milestone M1 --out <输出目录>
```

PowerShell 用 `$env:OFFLINE_HARNESS_FLASH_DIFF = '1'` 与 `$env:OFFLINE_HARNESS_FLASH_DIFF_WALKS = '32'`
设置同名变量后运行相同 `dotnet` 命令。
主搜索分发（`BeamSolveDispatch`）在协调器模式下复跑，日志中的 `[CombatSolver/Flash]` 行记录
对照或采用结果：

```bash
OFFLINE_HARNESS_FLASH_MODE=Shadow \
  dotnet .local/tool-build/OfflineSearchHarness/bin/Release/net9.0/OfflineSearchHarness.dll \
  --character IRONCLAD --encounter NIBBITS_NORMAL --seed FLASH1 --milestone M2 \
  --search-mode Coordinator --budget-ms 10000 --out <输出目录>
```

无人测试中的实机部署用 `--flash-search-mode-for-test Off|Shadow|On`（PowerShell
`-FlashSearchModeForTest`）临时覆盖设置；`--export-bug-report-after-combat` 在战斗结束和 On
搜索失败停止部署时都会在写结果前导出问题包，失败包带 `FlashSearchFailed`/`SearchFailure`
分类。包内 `diagnostics/logs/combat/*.jsonl` 保留 `[CombatSolver/Flash]` 行：

```bash
./tools/testing/run-unattended-test.sh --scenario-id FLASH-ON-NIBBITS --character-id IRONCLAD --encounter-id NIBBITS_NORMAL \
  --enemy-current-hp 1000 --headless-fast-mode-for-test Instant --deployment-fast-mode-for-test Instant \
  --deployment-inter-action-delay-seconds-for-test 0 --expected-unexpected-replans-at-most 0 \
  --flash-search-mode-for-test On --timeout-seconds 120 --cleanup-instance-on-exit
```

## 2. 当前证据（2026-10-21，Linux）

F1 纵切与内容批次 P1–P12 的逐批证据已归档到 [内容批次测试记录](../archive/flash-search/testing-content-batches-2026-10-18.md)，强制行动/眩晕（KS）、敌人生成（KM）与早期端到端测试归档到 [KS、KM 与早期端到端测试记录](../archive/flash-search/testing-batches-2026-10-19.md)，第二幕 P13 的逐根证据归档到 [第二幕 P13 测试记录](../archive/flash-search/testing-act2-p13-2026-10-19.md)，截至 SP2 的其余批次归档到 [覆盖补齐与协调器接管批次测试记录](../archive/flash-search/testing-batches-2026-10-20.md)，DX5 至 HS3 的批次与无头整场归档到 [开启模式接管与实机整场测试记录](../archive/flash-search/testing-batches-2026-10-21.md)；下面的全量回归覆盖其中全部根。

全量回归（`flash_regression.py diff`；SK、PT、DF、RG、NB、LR、MB、LS、ID、RQ、CX、TW、PF、PM、KB、SP、DX3、A3B、K4C、ST、PA、PC、DX4、PR、CP、TS、EC、SP2、DX5、SR、B1、XP、HS、LH、HS2、B2、DT、RS、DX6、TT、CT、RS3、DX7、UX、SW、HS3、HS4 合入后）：FlashKernelChecks 通过；162 个自然遭遇根（铁甲战士初始牌组覆盖三幕已实现遭遇的 FLASHM1/FLASHM2，另有其他四个角色的初始牌组各 6 条，以及三个第三幕首领在 `--act-index 2` 下开启首领策略的 6 条）、267 个固定根（`coverage/fixtures/cards/flash-*.json`）与 64 个生成牌组（含 29 个非铁甲战士牌组、11 个第三幕牌组）（`coverage/corpora/flash-decks/`）每根 32 条随机游走，K1 单步、K2 游走、K4 评估与 K5 下界输入全部零差异，没有准入拒绝；`unsupported` 全部为 0。

### 2.1 当前状态（HS4 合入后）

- 覆盖：五个角色的卡池、296 个遗物、65 种药水、三幕全部遭遇与第三幕首领策略、事件卡牌与事件遭遇。不支持的内容在准入时给出类别：多人专用、Mock、废弃内容，以及现有引擎同样不支持的 BYRDPIP。
- 默认语料的协调器对照（Medium、宽度组合、120 秒，Off 为固定基线）。口径：两种模式同一预算、同一档位，每根单次运行，各按实际结果计入，预算内未完成的根同样照实计入；同一代码重复测量时个别根有数点波动。
  - 四角色 46 个牌组：On 相对 Off 11 好 / 30 同 / 5 差，战略失血差合计 −90。
  - 铁甲战士 33 个牌组：8 好 / 20 同 / 5 差，−16。
  - 79 根全部 `FLASH_ADOPTED`，没有 `FLASH_SEARCH_FAILED`，开启模式不调用原搜索。IRONCLAD-27 与 IRONCLAD-53 各 2 次 `SmartPotionGradient` 由 Flash 判定无路线（`FLASH_NO_ROUTE`），现有求解器在这两根上同样没有满足策略的路线（`acceptable=False`）。
- 48 根基准（10 秒）：Flash 相对现有主搜索 14 好 / 29 同 / 5 差，失血合计 −34。
- 开启模式的无路线结论与失败类别见 [搜索与接入](search.md) 3.2 节。

### 2.2 批次记录

截至 SP2 的已完成批次，逐批证据归档在 [覆盖补齐与协调器接管批次测试记录](../archive/flash-search/testing-batches-2026-10-20.md)；DX5 至 SW 的批次归档在 [开启模式接管与实机整场测试记录](../archive/flash-search/testing-batches-2026-10-21.md)。新批次在本节追加。

开启模式回合准备失败与 HS3 超时（HS4，2026-10-21）：HS3 中 5 场开启模式整场没有结果，都是 Flash 回合准备搜索失败后，无人测试宿主一直等待无人作答的原生选择；Spine 异常是启动器结束进程后的清理噪声。三类原因：回合准备根在回合开始的生成效果之前捕获，`BigHat` 与 `VexingPuzzlebox` 没有声明生成牌池，导入生成牌时报内核不支持（NECROBINDER-22、REGENT-19、rq-REGENT-V-19），两者改为声明角色卡池；进度预览把推测路线翻译到旧引擎时手牌下标越界，`ArgumentOutOfRangeException` 打断整次搜索（NECROBINDER-19）；TestSubject MULTI_CLAW 的连击数编译时取了根上已含额外次数的意图值，内核 `ModifyAttackRepeats` 又加一次（NECROBINDER-08 第 5 回合核对差 11 点，Flash 多打一下），改为编译取 `BaseMultiClawCount`。`flash-hs4-testsubject-multiclaw` 用新增的 `initialEnemyIntegerMembers` 把 `_extraMultiClawCount` 设为 1，修正前 16 条游走失败、修正后 0。翻译器在手牌下标越界，或旧引擎按动态目标规则（`GetTargetType`，含 Fan of Knives 下的 Shiv 与 Seeking Edge 下的君王之刃）需要敌方目标而 Flash 动作没有目标时，抛 `FlashChoiceMismatchException`：核对与物化路径转为 `verify-mismatch`，预览只跳过，每次求解第一次出现时写 `FLASH_PREVIEW_MISMATCH`，`FLASH_PROGRESS_STATS` 增加 `mismatches=`。回合准备搜索的 Flash 失败按 SW 约定处理：写 `FLASH_SEARCH_FAILED`、停止部署、关闭全自动、弹出 Flash 提示，问题包带 `SearchFailure` 与 `FlashSearchFailed`，原生选择留给玩家；无人测试宿主遇到回合准备或搜索失败时直接判 Failed，不再等到启动器时限。动态意图审计：原版 218 个攻击意图中只有 TestSubject MULTI_CLAW、TheForgotten DREAD 与 WaterfallGiant PRESSURE_GUN 由运行时值计算，后两者在根上不会重复计入（`flash-hs4-dread-root-dexterity` 根上带 5 层敏捷，游走零差异）。离线宿主新增最终候选路线扫描（`OFFLINE_HARNESS_FLASH_CANDIDATE_SCAN=N`）：全量回归 493 根共 2901 条候选逐条交现有引擎核对，没有不一致（54 根没有可扫描的候选）。HS3 中亡灵契约师与储君的 40 场开启模式整场重跑：全部有结果，29 胜 11 战死，735 行 `FLASH_ADOPTED` 全部核对通过，`FLASH_SEARCH_FAILED`、核对不一致与预览不一致均为 0，计划外重算 0；HS3 的 5 场超时都跑完（NECROBINDER-08 第 9 回合战死，关闭模式第 10 回合战死；其余 4 场胜利），其余场次的胜负与结束回合与 HS3 相同（有记录生命的场次生命也相同）。回合准备固定根与合同 16 项（`TURN-SETUP-*`、`toasty-qol-*`、`route-row-reuse` 等）开启与关闭模式均 Passed。合入后全量回归 493 根零差异，第三幕 150 根 48 条游走零差异；默认 79 根开启模式与 HS3 合入后逐根相同（46 个牌组 11 好 / 30 同 / 5 差、−90，33 个牌组 8 / 20 / 5、−16，没有 `FLASH_SEARCH_FAILED`，`FLASH_NO_ROUTE` 仍只在 IRONCLAD-27/53），48 根基准 14 / 29 / 5、失血合计 −34，与合入前相同。

### 2.3 无人测试实机部署

Linux 无头整场部署的历次记录（2026-10-19 的初始牌组与第三幕首领，HS、HS2、HS3 的多样化牌组整场）归档在 [开启模式接管与实机整场测试记录](../archive/flash-search/testing-batches-2026-10-21.md) 第 2 节，最近一轮见 2.2 的 HS4。HS3 与 HS4 的 40 场无头整场使用本地生成的四角色牌组清单；该清单保存在 `.local`，**没有随仓库提交**，因此审阅者无法从仓库逐场复跑同一 40 场，只能按本节方法重建代表场景；仓库内保留的是汇总结果。无人测试请求默认向手牌注入一张 `STRIKE_IRONCLAD`，用显式牌组建局时需传 `--cards-json []`。玩家战死的整场不导出问题包，`[CombatSolver/Flash]` 行从结果目录的 `runtime-events.json` 读取。多个实例并行时共用同一个 `COMBATSOLVER_HEADLESS_HOST_ROOT`：各用一个时，准入会把另一实例的游戏进程当作未登记进程，排队到时限后无结果。

## 3. 已知限制与未验证项

- 只覆盖归档批次与 2.1 所列内容；其他内容在准入阶段拒绝，尚未有差分证据。
- 精确层下界不可用（遗物目标、成长目标等）时 Flash 整次搜索不做下界剪枝，原搜索只对不满足条件的节点放开；路线不受影响，搜索面更大。除遗物目标外的禁用原因没有逐个构造实机根。
- `FLASH_SEARCH_FAILED` 的提示与问题包（包括回合准备搜索失败时关闭全自动、把原生选择留给玩家）只在无头合同与整场中验证，未在可见游戏中查看。
- 最终候选扫描只覆盖排序前 N 条最终候选，进度预览用过的推测路线没有导出；HS4 前 NECROBINDER-19 一次预览手牌下标差异（第 7 回合第 24 步）此后没有再现。扫描中 `flash-sk-a-discard-sly` 的一条搜索分支到达内核边界（TACTICIAN+1 弃牌后在自动打出前失去瞬态标记），开启模式在该状态按 `kernel-unsupported` 失败。
- 仍在战斗中的生物身上已移除 Power 的残留条目不能投影：根导入时拒绝，路线核对中遇到时按核对失败丢弃 Flash 路线；敌方离场后的残留按惰性处理（见内容批次归档卷 2.8.1）。
- 选择覆盖卡牌自身的单选、手牌/抽牌堆/弃牌堆选择、回合开始选择、知识恶魔的固定选项与自动打出中的嵌套选择（LR、MB 固定根）；一个动作最多携带 8 个选择，更深的链按容量边界拒绝。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 实机部署只在 Linux 无头游戏中验证（五个角色初始牌组、第三幕首领与 HS/HS2/HS3/HS4 的多样化牌组整场，见 2.3 与归档卷）；未做可见游戏、Windows 或 Steam 会话验证，设置面板的新选项未在游戏中查看。
