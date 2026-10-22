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

设置持久化兼容合同（旧设置/检查点/问题包中的 `Shadow` 与任何数值形式读成 Off，`On` 保持字符串序列化）：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py compat --label flash-compat
```

纯内核合同（K0）与两次构建：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py build
```

差分探针（K1 单步穷举、K2 随机游走）的全量根集合是 410 根：290 个 `coverage/fixtures/cards/flash-*.json` 固定根（默认每根 32 条游走）与 120 个 `coverage/corpora/flash-quality/` 质量牌组（默认每根 48 条游走）：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py diff --label flash-diff --jobs 8
python3 -B tools/search/FlashRegression/flash_regression.py diff \
  --label one --only fixtures --root flash-dx5-pen-nib-sly
```

`--only fixtures`、`--only quality` 只跑其中一组。

最终候选路线扫描（同一根集合，每根只跑一次 Flash；排序前 N 条最终候选交现有引擎逐条核对，输出含 `admitted=`/`rejection=`，准入拒绝即失败）：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py scan --label flash-scan --candidates 8 --jobs 8
```

120 根协调器质量对照（[质量语料](../../coverage/corpora/flash-quality/README.md)，8 个并行进程）：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py coordinator --label flash-coord --jobs 8
python3 -B tools/search/FlashRegression/flash_regression.py table .local/flash-regression/flash-coord --corpus coverage/corpora/flash-quality
```

`coordinator` 双模式结束时自动写出同一张表（`TABLE-GROUPS.txt`）；单模式（`--off-baseline` 默认只跑 `On`，或 `--modes Off`）写 `TABLE.txt` 的逐根汇总。有失败根（`FLASH_SEARCH_FAILED`）或缺失结果根时命令以非 0 退出。`table` 只读已有结果，用于复算或换语料。

协调器的关闭模式基线可用 `--off-baseline <旧结果目录>` 复用；只复跑一根时用
`--ids <目录名>`，例如 `--ids SILENT-007`。

需要直接调试宿主时，底层单根命令仍是：

```bash
OFFLINE_HARNESS_FLASH_DIFF=1 OFFLINE_HARNESS_FLASH_DIFF_WALKS=32 \
  dotnet .local/tool-build/OfflineSearchHarness/bin/Release/net9.0/OfflineSearchHarness.dll \
  --character IRONCLAD --encounter NIBBITS_NORMAL --seed FLASH1 --milestone M1 --out <输出目录>
```

PowerShell 用 `$env:OFFLINE_HARNESS_FLASH_DIFF = '1'` 与 `$env:OFFLINE_HARNESS_FLASH_DIFF_WALKS = '32'`
设置同名变量后运行相同 `dotnet` 命令。`diff --steps <N>` 与 `diff --debug` 分别映射
`OFFLINE_HARNESS_FLASH_DIFF_STEPS`（每条游走的步数上限，默认 60）与 `OFFLINE_HARNESS_FLASH_DEBUG`
（K4 参考差异时打印非 pure 历史条目）。
主搜索分发（`BeamSolveDispatch`）在协调器模式下复跑，日志中的 `[CombatSolver/Flash]` 行记录
采用结果：

```bash
OFFLINE_HARNESS_FLASH_MODE=On \
  dotnet .local/tool-build/OfflineSearchHarness/bin/Release/net9.0/OfflineSearchHarness.dll \
  --character IRONCLAD --encounter NIBBITS_NORMAL --seed FLASH1 --milestone M2 \
  --search-mode Coordinator --budget-ms 10000 --out <输出目录>
```

无人测试中的实机部署用 `--flash-search-mode-for-test Off|On`（PowerShell
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

全量回归（`flash_regression.py diff`）：FlashKernelChecks 通过；当前集合是 290 个固定根（`coverage/fixtures/cards/flash-*.json`，每根 32 条随机游走）与 120 个质量牌组（`coverage/corpora/flash-quality/`，每根 48 条随机游走），共 410 根。最近一次全量差分（QF5 合入后的 410 根，内核与内容自此未变）K1 单步、K2 游走、K4 评估、K5 下界输入与动作集合核对全部零差异，没有准入拒绝，`unsupported` 全部为 0。此前的 162 个自然遭遇根、`flash-decks` 的 64 个生成牌组与 `flash-act3` 的 150 个第三幕牌组已从回归中移除，它们在移除前的最后一次运行也是零差异。

### 2.1 当前状态（FW 验收后）

- 覆盖：五个角色的卡池、296 个遗物、65 种药水、三幕全部遭遇与第三幕首领策略、事件卡牌与事件遭遇。不支持的内容在准入时给出类别：多人专用、Mock、废弃内容，以及现有引擎同样不支持的 BYRDPIP。
- 质量对照（[质量语料](../../coverage/corpora/flash-quality/README.md) 120 根，协调器 Medium、宽度组合、120 秒，8 个并行进程）。开启模式在 Medium 档位使用 Flash 自己的 Beam 宽度 240 与节点上限 480,000，常规搜索保持 60 / 120,000；软时间、分支上限等其余档位字段相同。口径：两种模式同一预算、同一性能档位，每根单次运行，各按实际结果计入；“好/同/差”按求解器自己的比较键：胜负、存活、死亡豁免次数、战略失血 + 药水战略成本（`RouteQualityPolicy` 的药水口径）、结束回合，差值列是两种模式（战略失血 + 药水战略成本）之差的合计，表由 `flash_regression.py table` 生成；只比较战略失血会把用药换来的少失血算作更好（例如药水成本 45 的路线失血 17，按求解器口径是 62）；同一代码重复测量时个别根有数点波动。

  | 范围 | 根数 | 开启相对关闭（好 / 同 / 差） | 失血 + 药水成本差 | 胜场 关闭 → 开启 | 总耗时 关闭 → 开启 |
  | --- | ---: | --- | ---: | --- | --- |
  | 合计 | 120 | 31 / 72 / 17 | +100 | 87 → 91 | 1671 s → 735 s |
  | 铁甲战士 | 24 | 7 / 14 / 3 | −3 | 20 → 20 | 324 s → 115 s |
  | 静默猎手 | 24 | 2 / 21 / 1 | −2 | 14 → 14 | 377 s → 160 s |
  | 故障机器人 | 24 | 8 / 13 / 3 | −24 | 18 → 19 | 247 s → 108 s |
  | 储君 | 24 | 6 / 12 / 6 | +102 | 18 → 20 | 363 s → 213 s |
  | 亡灵契约师 | 24 | 8 / 12 / 4 | +27 | 17 → 18 | 359 s → 139 s |
  | 第一幕 | 40 | 8 / 29 / 3 | −32 | 40 → 40 | 431 s → 155 s |
  | 第二幕 | 40 | 12 / 18 / 10 | +156 | 29 → 32 | 740 s → 378 s |
  | 第三幕 | 40 | 11 / 25 / 4 | −24 | 18 → 19 | 501 s → 203 s |

  17 根变差中 7 根只是结束回合更晚（DEFECT-005、DEFECT-013、NECROBINDER-007、NECROBINDER-009、NECROBINDER-012、REGENT-005、REGENT-012），没有开启模式未获胜的变差根；代价变多的 10 根是 DEFECT-012（14 → 31）、IRONCLAD-011（27 → 29）、IRONCLAD-014（117 → 133）、IRONCLAD-016（71 → 76）、NECROBINDER-022（52 → 55）、REGENT-008（118 → 160）、REGENT-014（111 → 113）、REGENT-015（23 → 26）、REGENT-021（24 → 31）与 SILENT-018（8 → 14）。开启模式多赢 4 根（DEFECT-022、NECROBINDER-014、REGENT-010、REGENT-011），没有把原本获胜的根搜成失败。旧 100 根语料同一批次为 29 好 / 60 同 / 11 差、失血 + 药水成本差 +241、胜场 70 → 74、总耗时 2030 s → 680 s。
- 120 根开启模式全部 `FLASH_ADOPTED`，没有 `FLASH_SEARCH_FAILED`，开启模式不调用原搜索。8 根共 16 次 `FLASH_NO_ROUTE`（`flash-potion-policy-no-route`），与现有搜索的无路线结果同形。
- 开启模式的无路线结论与失败类别见 [搜索与接入](search.md) 3.2 节。

### 2.2 精简与清理回归（CL1）

CL1 删除 Shadow 模式、K6 工具、25% 进攻席位、失败注入、死成员与空壳 token，并清理离线工具与文档；内核与内容无语义改动。最终源码上的证据：

- Release 构建 0 警告；K0 Passed；结构、文档、工具、覆盖四项门禁通过。
- 开启模式 120 根（`coordinator --off-baseline <主线 off120-base>`）：与 FW 基线 `.local/flash-regression/fw-on120` 的 120 根 `route.json`、`quality.json`、`SelectedExpanded`、`SelectedTransitions` 全部逐根相同。
- 关闭模式 120 根（`coordinator --modes Off`）：`quality.json` 120 根逐根相同；`SearchWorkAttributions` 合计展开 3,653,306、转移 17,168,117（基线 3,650,837 / 17,111,557），差异只在 IRONCLAD-013（+5 展开 / −17 转移）与 REGENT-013（+2,464 / +56,577）两个时间预算根，其余 118 根逐根相同。
- 候选扫描（每根只跑一次 Flash 的新跑法）410 根、2,008 条候选、0 不一致；410 根全部准入，75 根没有可扫描候选。作为新基线。
- 设置持久化兼容：`flash_regression.py compat` 输出 `FLASH_MODE_COMPAT status=Passed assertions=23`（旧 `Shadow`/数值形式读 Off，`On` 字符串往返）。

### 2.3 已完成批次

已完成批次的逐批证据冻结在 [Flash 搜索批次测试记录（2026-10-22）](../archive/flash-search/testing-batches-2026-10-22.md)（含 HS4、QF、QP、QH、QR、QW、FW 与 K6/R1–R3 的尝试记录）。更早的 P1–P12、KS/KM、第二幕 P13、SP2 等批次见 [Flash 搜索归档](../archive/flash-search/README.md)。新批次证据写入本节，不再追加到归档卷。

K6 逐层保留集合对比工具已从当前源码删除（`RetentionDiffProbe`、`FlashRetention`、`flash_regression.py retention`、`FlashBeamOptions.Retention` 与常规搜索的保留集合观察器），需要时从提交 `5365e89d` 找回；当前回归入口不再有 `retention` 子命令。路线追踪（`trace`）仍在使用的 `SearchPathObserver` 与 `SearchDiagnosticsSink.RetentionLabels` 保留。

### 2.4 无人测试实机部署

Linux 无头整场部署的历次记录（2026-10-19 的初始牌组与第三幕首领，HS、HS2、HS3 的多样化牌组整场）归档在 [开启模式接管与实机整场测试记录](../archive/flash-search/testing-batches-2026-10-21.md) 第 2 节，最近一轮见 2.2 的 HS4。HS3 与 HS4 的 40 场无头整场使用本地生成的四角色牌组清单；该清单保存在 `.local`，**没有随仓库提交**，因此审阅者无法从仓库逐场复跑同一 40 场，只能按本节方法重建代表场景；仓库内保留的是汇总结果。无人测试请求默认向手牌注入一张 `STRIKE_IRONCLAD`，用显式牌组建局时需传 `--cards-json []`。玩家战死的整场不导出问题包，`[CombatSolver/Flash]` 行从结果目录的 `runtime-events.json` 读取。多个实例并行时共用同一个 `COMBATSOLVER_HEADLESS_HOST_ROOT`：各用一个时，准入会把另一实例的游戏进程当作未登记进程，排队到时限后无结果。

## 3. 已知限制与未验证项

- 当前覆盖 290 个固定根与 120 个质量根；未覆盖的第三方内容按 [准入](admission-and-verification.md) 的 `admission-unsupported-content` / `admission-unrepresentable-state` 拒绝，尚无该类内容的差分证据。
- 精确层下界不可用（遗物目标、成长目标等）时 Flash 整次搜索不做下界剪枝，原搜索只对不满足条件的节点放开；路线不受影响，搜索面更大。除遗物目标外的禁用原因没有逐个构造实机根。
- `FLASH_SEARCH_FAILED` 的提示与问题包（包括回合准备搜索失败时关闭全自动、把原生选择留给玩家）只在无头合同与整场中验证，未在可见游戏中查看。
- 最终候选扫描只覆盖排序前 N 条最终候选，进度预览用过的推测路线没有导出；HS4 前 NECROBINDER-19 一次预览手牌下标差异（第 7 回合第 24 步）此后没有再现。扫描中 `flash-sk-a-discard-sly` 的一条搜索分支到达内核边界（TACTICIAN+1 弃牌后在自动打出前失去瞬态标记），开启模式在该状态按 `kernel-unsupported` 失败。
- 仍在战斗中的生物身上已移除 Power 的残留条目不能投影：根导入时拒绝，路线核对中遇到时按核对失败丢弃 Flash 路线；敌方离场后的残留按惰性处理（见内容批次归档卷 2.8.1）。
- 选择覆盖卡牌自身的单选、手牌/抽牌堆/弃牌堆选择、回合开始选择、知识恶魔的固定选项与自动打出中的嵌套选择（LR、MB 固定根）；一个动作最多携带 8 个选择，更深的链按容量边界拒绝。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 实机部署只在 Linux 无头游戏中验证（五个角色初始牌组、第三幕首领与 HS/HS2/HS3/HS4 的多样化牌组整场，见 2.4 与归档卷）；未做可见游戏、Windows 或 Steam 会话验证。设置面板的 Flash 档位现有两个选项（关闭/启用），未在可见游戏中查看。
