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

开启模式准入改用合同（同一根上 Off 路线、On 整次改用常规搜索后的路线/质量/工作量逐项相同；On 正常搜索日志恰好一行 `FLASH_FALLBACK`、不写 `FLASH_SEARCH_FAILED`；把常规求解器强制套上 Flash 档位会改变工作量；已发布路线/采纳种子时不再重跑；未登记搜索用途仍以 `admission-unsupported-request` 失败）：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py fallback --label flash-fallback --jobs 8
```

固定输入为 `coverage/fixtures/search/flash-fallback-soulbound-power.json`：玩家携带原版尚无 Flash 定义的 `SOULBOUND_POWER`，根展开量明显超过常规 Beam 宽度。合同在同一个离线宿主进程里通过 `CombatSearchCoordinator.Solve` 跑 Off、On、常规模式 + Flash profile 的敏感性对照，以及一条带预置路线采纳种子的已发布回落；`BeamSolveDispatch` 只用于未登记用途的失败断言。检查 JSON 为 `flash-fallback-checks.json`。

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

### 2.1 当前状态（CL2 合入后）

- 覆盖：五个角色的卡池、296 个遗物、65 种药水、三幕全部遭遇与第三幕首领策略、事件卡牌与事件遭遇。不支持的内容在准入时给出类别：多人专用、Mock、废弃内容，以及现有引擎同样不支持的 BYRDPIP。
- 质量对照（[质量语料](../../coverage/corpora/flash-quality/README.md) 120 根，协调器 Medium、宽度组合、120 秒，8 个并行进程）。开启模式在 Medium 档位使用 Flash 自己的 Beam 宽度 240 与节点上限 480,000，常规搜索保持 60 / 120,000；软时间、分支上限等其余档位字段相同。口径：两种模式同一预算、同一性能档位，每根单次运行，各按实际结果计入；“好/同/差”按求解器自己的比较键：胜负、存活、死亡豁免次数、战略失血 + 药水战略成本（`RouteQualityPolicy` 的药水口径）、结束回合，差值列只统计两种模式都获胜的根，是（战略失血 + 药水战略成本）之差的合计，输掉的战斗的代价与获胜的不可比，只有一方获胜的根看胜场列；表由 `flash_regression.py table` 生成；只比较战略失血会把用药换来的少失血算作更好（例如药水成本 45 的路线失血 17，按求解器口径是 62）；同一代码重复测量时个别根有数点波动。

  | 范围 | 根数 | 开启相对关闭（好 / 同 / 差） | 两边都胜：根数，失血 + 药水成本差 | 胜场 关闭 → 开启 | 总耗时 关闭 → 开启 |
  | --- | ---: | --- | ---: | --- | --- |
  | 合计 | 120 | 31 / 72 / 17 | 87，−28 | 87 → 91 | 1671 s → 723 s |
  | 铁甲战士 | 24 | 7 / 14 / 3 | 20，−3 | 20 → 20 | 324 s → 114 s |
  | 静默猎手 | 24 | 2 / 21 / 1 | 14，−2 | 14 → 14 | 377 s → 156 s |
  | 故障机器人 | 24 | 8 / 13 / 3 | 18，−28 | 18 → 19 | 247 s → 106 s |
  | 储君 | 24 | 6 / 12 / 6 | 18，+42 | 18 → 20 | 363 s → 213 s |
  | 亡灵契约师 | 24 | 8 / 12 / 4 | 17，−37 | 17 → 18 | 359 s → 134 s |
  | 第一幕 | 40 | 8 / 29 / 3 | 40，−32 | 40 → 40 | 431 s → 151 s |
  | 第二幕 | 40 | 12 / 18 / 10 | 29，+32 | 29 → 32 | 740 s → 372 s |
  | 第三幕 | 40 | 11 / 25 / 4 | 18，−28 | 18 → 19 | 501 s → 200 s |

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

Linux 无头整场部署的历次记录（2026-10-19 的初始牌组与第三幕首领，HS、HS2、HS3 的多样化牌组整场）归档在 [开启模式接管与实机整场测试记录](../archive/flash-search/testing-batches-2026-10-21.md) 第 2 节，HS4 见 2.2。HS3/HS4 的 40 场使用当时的本地清单，汇总保留在归档卷；从 HL 起，固定 40 根写进已提交的
[`flash_regression.py headless`](../../tools/search/FlashRegression/README.md#headless) 子命令，审阅者可以从仓库逐场复跑同一组场景。

固定清单是五个角色各 8 根：序号 `000 001 002`（第一幕普通/精英/首领）、`009 010 011`
（第二幕普通/精英/首领）与 `019 020`（第三幕精英/首领），即 `IRONCLAD-000` … `NECROBINDER-020`；
`--ids` 可覆盖。每场从 `coverage/corpora/flash-quality/<id>/spec.json` 派生 `mode=Deploy` 的
`scenario.json`，调用 `tools/testing/run-unattended-test.sh --generated-scenario-path ...`，并传
`--flash-search-mode-for-test On|Off`（`--mode`，默认 `On`）、`--cards-json []` 与
`--search-budget-ms 120000`（默认，与质量语料协调器同预算）。生成场景保留原生
战斗状态，敌人生命是自然值，手牌没有被注入额外的打击。玩家战死记为 `death` 结果，不作为运行失败；
它的 `result.json` 可能是 `Failed`、启动器退出码为 1，工具用原生结束状态、结束回合与实际最大生命
记录真实终局，不读取 `solverMetrics.finalHp`。回合准备根在模式覆盖生效前捕获，所以工具在启动前把
`flashSearchMode` 写入实例的持久设置。

并行与证据：`--lanes N`（默认 2）；所有通道共用同一个 `COMBATSOLVER_HEADLESS_HOST_ROOT`，每条通道
一个 `.local/headless-instances/<label>-<通道>` 实例，最后一场带 `--cleanup-instance-on-exit` 并检查
实例目录已删除。每场证据写到 `<out>/<id>/evidence/`（`generated-scenario.*`、`result.json`、
`runtime-events.json`、`report.zip`/`report/`、`instance-logs/` 等），`SUMMARY.txt` 逐场列出胜负、
结束回合、真实终局生命、`FLASH_ADOPTED` 与核对通过次数、`FLASH_SEARCH_FAILED`、核对/预览不一致与
计划外重算，并给出合计和退出码判定。完整参数、`--scenario-baseline` 与退出码见
[工具说明](../../tools/search/FlashRegression/README.md#headless)。工具只接入 Linux Bash 启动器，
未接入 Windows `.ps1`。

整轮 40 场（vps4 路径示例；其他机器替换游戏、RitsuLib 与种子档路径）：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py headless \
  --label hl --lanes 2 --timeout-seconds 600 --search-budget-ms 120000 \
  --sts2-game-root "$HOME/games/sts2" \
  --ritsu-workshop-root "$HOME/games/sts2/steamapps/workshop/content/2868840/3747602295" \
  --xdg-data-home .local/headless-seed/data
```

本轮两根试跑用 `--ids IRONCLAD-000 SILENT-000 --lanes 2 --timeout-seconds 300
--search-budget-ms 120000` 与 `--scenario-baseline <主线离线证据根>`：两场均 Passed（第 4/5 回合），
建出的牌组、遗物、药水与遭遇和
离线宿主对同一 `spec.json` 的 `generated-scenario.resolved.json`/`loadout.json` 逐字段一致（比较时
忽略生成模式 `mode`），`SUMMARY.txt` 为 `RESULT PASS`，两个实例目录都已删除。当前机器上的证据在
`.local/flash-regression/hl-smoke/`。

HL 整轮（源码 `754ce17e`，上面的默认命令，开启模式、2 条通道、每次搜索 120 秒预算）：40 场
`RESULT PASS`，29 胜、11 战死，没有 `failed` 或 `no-result`；388 次 `FLASH_ADOPTED` 全部
`status=verified`，`FLASH_SEARCH_FAILED`、核对不一致、预览不一致与计划外重算都是 0，40 场场景检查
通过，两条通道的实例目录都已删除；墙钟约 8 分钟。战死的 11 根（IRONCLAD-020、SILENT-010/011/019/020、
DEFECT-020、REGENT-019/020、NECROBINDER-011/019/020）正是离线 120 根协调器对照中开启模式未获胜的
11 根，两边胜负集合一致。REGENT-019 在玩家自己回合结束时战死，写结果时游戏尚未离开战斗
（`combatEnded=false`、`stage=wait_combat_end`），首版工具把它归成 `no-result`；按执行器“原生战斗进入
结束状态后才报仍存在未死亡敌人”的语义修正分类后，用同一份证据重新汇总为 `death`，没有重跑游戏。
证据在 `.local/flash-regression/hl-on40/`（修正前的汇总保留为 `SUMMARY.before-deathfix.txt`）。

质量语料其余 80 根随后在同一源码与同一构建上补跑（`--skip-build`，`--ids` 传入默认清单以外的根，证据
`.local/flash-regression/hl-rest80/`），同样 `RESULT PASS`：62 胜、18 战死，969 次 `FLASH_ADOPTED`
全部核对通过，搜索失败、不一致与计划外重算都是 0，实例目录已删除，墙钟约 17 分钟。DEFECT-007 获胜后
启动器报告进程未能复用（退出码 1），下一场换新进程，不影响判定。

合计 120 根：

| 分组 | 场数 | 无头胜 | 无头战死 | 采用/核对通过 | 离线开启胜 | 离线关闭胜 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 合计 | 120 | 91 | 29 | 1,357 / 1,357 | 91 | 87 |
| 铁甲战士 | 24 | 20 | 4 | 282 / 282 | 20 | 20 |
| 静默猎手 | 24 | 14 | 10 | 283 / 283 | 14 | 14 |
| 故障机器人 | 24 | 19 | 5 | 267 / 267 | 19 | 18 |
| 储君 | 24 | 20 | 4 | 270 / 270 | 20 | 18 |
| 亡灵契约师 | 24 | 18 | 6 | 255 / 255 | 18 | 17 |
| 第一幕 | 40 | 40 | 0 | 354 / 354 | 40 | 40 |
| 第二幕 | 40 | 32 | 8 | 474 / 474 | 32 | 29 |
| 第三幕 | 40 | 19 | 21 | 529 / 529 | 19 | 18 |
| 普通 | 40 | 40 | 0 | 364 / 364 | 40 | 40 |
| 精英 | 40 | 33 | 7 | 454 / 454 | 33 | 31 |
| 首领 | 40 | 18 | 22 | 539 / 539 | 18 | 16 |

逐根胜负与离线 120 根协调器对照（`cl2-on120`）的开启模式完全一致，没有一根不同；“离线关闭胜”列只作
参照，无头整场没有跑关闭模式。全部 120 根可以一次复跑：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py headless \
  --label hl-120 --lanes 2 --timeout-seconds 600 --search-budget-ms 120000 \
  --ids $(ls coverage/corpora/flash-quality | grep -E '^[A-Z]+-[0-9]{3}$') \
  --sts2-game-root "$HOME/games/sts2" \
  --ritsu-workshop-root "$HOME/games/sts2/steamapps/workshop/content/2868840/3747602295" \
  --xdg-data-home .local/headless-seed/data
```

两条通道合计约 25 分钟（本轮 40 + 80 两段）。

### 2.5 FB：准入改用常规搜索（2026-10-22）

开启模式只对 `admission-unsupported-content` 与 `admission-unrepresentable-state` 两类准入让
**整次协调器搜索**改用常规档位重跑；其余失败类别不变。`BeamSolveDispatch` 只抛
`FlashFallbackRequiredException`，`CombatSearchCoordinator.Solve` 写一行 `FLASH_FALLBACK` 后用
`RegularProfile` 从头搜索；发布过结果时不重跑。固定输入
`coverage/fixtures/search/flash-fallback-soulbound-power.json` 在玩家侧注入原版尚无 Flash 定义的
`SOULBOUND_POWER`，根展开量超过常规 Beam 宽度，同一根上 Off/On 都走常规求解器。复跑：

```bash
python3 -B tools/search/FlashRegression/flash_regression.py fallback --label flash-fallback --jobs 8
python3 -B tools/search/FlashRegression/flash_regression.py coordinator \
  --label fb-on120 --off-baseline <主线 off120-base> --jobs 8
```

- Release 构建 0 警告；K0 `contracts=Passed`；结构、文档、工具、覆盖四项门禁通过。
- `fallback` 合同：`status=Passed`；常规 profile 为 45 / 5,000，Flash profile 为 180 / 240,000。
  Off 与 On 的路线、质量、`ExpandedNodes`/`TransitionCount` 均为 1843 / 5855；同一常规求解器
  强制套上 Flash profile 为 6203 / 17954，敏感性成立。On 正常搜索日志恰好一行
  `[CombatSolver/Flash] FLASH_FALLBACK reason=admission-unsupported-content
  detail=SOULBOUND_POWER purpose=PrimaryBeam`，不写 `FLASH_SEARCH_FAILED`；预置路线采纳种子后
  回落不再重跑，改写 `FLASH_SEARCH_FAILED reason=admission-unsupported-content`；同样本上未登记
  `DirectSearchPurpose` 仍以 `FLASH_SEARCH_FAILED reason=admission-unsupported-request` 失败。
- 开启模式 120 根（`coordinator --off-baseline <主线 off120-base>`）：120 根全部准入，没有
  `FLASH_FALLBACK`/`FLASH_SEARCH_FAILED`；与主线 `.local/flash-regression/cl2-on120` 的
  `route.json`、`quality.json`、`SelectedExpanded`、`SelectedTransitions` 全部逐根相同。
  `pruneCounters` 的总展开/转移有 119 根相同；只有 REGENT-013（120 秒时间预算根，每次运行会变）
  为 253,260/6,598,600 对 253,712/6,796,322。逐根 wall 合计 744.4 秒（命令进度到最后一根 378.1 秒）。
- 关闭模式 120 根（`coordinator --modes Off`）：`quality.json` 120/120 与 `off120-base` 相同；
  `SelectedExpanded`/`SelectedTransitions` 118/120，总展开/转移同样 118/120，差异只在 IRONCLAD-013
  （177,209/991,173 对 178,345/996,696）与 REGENT-013（49,094/786,581 对 48,414/774,974）两个
  120 秒时间预算根，和既有关闭模式记录一致；日志没有 Flash 行。
- 未在可见游戏里查看改用提示的排版；每场战斗只提示一次的 Runtime 行为由代码路径与离线事件通道覆盖，
  尚无可见 Steam 截图。

## 3. 已知限制与未验证项

- 当前覆盖 290 个固定根与 120 个质量根；未覆盖的第三方内容按 [准入](admission-and-verification.md) 的 `admission-unsupported-content` / `admission-unrepresentable-state` 改用常规搜索，已有 `SOULBOUND_POWER` 根的改用合同；尚无第三方 Mod 实机根的差分证据。
- 精确层下界不可用（遗物目标、成长目标等）时 Flash 整次搜索不做下界剪枝，原搜索只对不满足条件的节点放开；路线不受影响，搜索面更大。除遗物目标外的禁用原因没有逐个构造实机根。
- `FLASH_SEARCH_FAILED` 的提示与问题包（包括回合准备搜索失败时关闭全自动、把原生选择留给玩家）只在无头合同与整场中验证，未在可见游戏中查看。
- 最终候选扫描只覆盖排序前 N 条最终候选，进度预览用过的推测路线没有导出；HS4 前 NECROBINDER-19 一次预览手牌下标差异（第 7 回合第 24 步）此后没有再现。扫描中 `flash-sk-a-discard-sly` 的一条搜索分支到达内核边界（TACTICIAN+1 弃牌后在自动打出前失去瞬态标记），开启模式在该状态按 `kernel-unsupported` 失败。
- 仍在战斗中的生物身上已移除 Power 的残留条目不能投影：根导入时拒绝，路线核对中遇到时按核对失败丢弃 Flash 路线；敌方离场后的残留按惰性处理（见内容批次归档卷 2.8.1）。
- 选择覆盖卡牌自身的单选、手牌/抽牌堆/弃牌堆选择、回合开始选择、知识恶魔的固定选项与自动打出中的嵌套选择（LR、MB 固定根）；一个动作最多携带 8 个选择，更深的链按容量边界拒绝。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 实机部署只在 Linux 无头游戏中验证（五个角色初始牌组、第三幕首领、HS/HS2/HS3/HS4 的多样化牌组整场与 HL 固定 40 根清单，见 2.4 与归档卷）；未做可见游戏、Windows 或 Steam 会话验证。设置面板的 Flash 档位现有两个选项（关闭/启用），未在可见游戏中查看。
