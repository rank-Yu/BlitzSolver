# 历史逐轮加宽搜索验证

本文保存旧逐轮加宽搜索的验证方法与记录，供历史对照；旧命令参数和节点预算不适用于当前组合搜索。当前用法见 [测试与验证](../testing.md) 与 [战斗搜索](../combat-ai.md)。

### 6.7 离线整场自动战斗

`--offline-combat` 用与 Mod 完全相同的整场决策循环（`src/Core/Autoplay/CombatPilot.cs`）
在 Sts2Solver 内核里打完一个根，不启动 Godot，也不模拟原生动画。默认跑质量语料的 120 根：

```bash
# 默认中档、确定性搜索；每个根一个子进程，--jobs 控制同时运行的重进程数
dotnet artifacts/bin/Differential/release/Differential.dll \
  --offline-combat --corpora tests/corpora --jobs 4 \
  --out .local/offline-combat/default

# 指定根列表（每行一个根名，# 注释）；根名必须是 d1 根
dotnet artifacts/bin/Differential/release/Differential.dll \
  --offline-combat --roots-file .local/roots120.txt --jobs 4 \
  --out .local/offline-combat/default

# 只跑一个根，便于定位（同进程，不再起子进程）
dotnet artifacts/bin/Differential/release/Differential.dll \
  --offline-combat --root quality-DEFECT-000 --out .local/offline-combat/one

# 四档评估：每档 120 根，--jobs 4。低/中/高各约 3/5/7 分钟；
# ExtraHigh 按 60 根拆两次，单条命令保持在 8 分钟内
for tier in Low Medium High; do
  dotnet artifacts/bin/Differential/release/Differential.dll \
    --offline-combat --roots-file .local/roots120.txt --jobs 4 \
    --search-tier "$tier" --out ".local/offline-combat/$tier"
done
for part in a b; do
  dotnet artifacts/bin/Differential/release/Differential.dll \
    --offline-combat --roots-file ".local/roots-xhigh-$part.txt" --jobs 4 \
    --search-tier ExtraHigh --out ".local/offline-combat/xhigh-$part"
done

# 单宽度探针：起始=最多，便于只测某一宽度；每轮节点上限仍可覆盖
dotnet artifacts/bin/Differential/release/Differential.dll \
  --offline-combat --corpora tests/corpora --jobs 4 \
  --beam-width 512 --max-width 512 --out .local/offline-combat/w512

# 同宽度、旧局面池上限（200 MiB），用于放大前后对照
dotnet artifacts/bin/Differential/release/Differential.dll \
  --offline-combat --corpora tests/corpora --jobs 4 \
  --beam-width 512 --max-width 512 --pool-state-budget-mb 200 \
  --out .local/offline-combat/w512-pool200

# 极小预算强制部分路线，走一遍重搜路径（不是等价用例）
dotnet artifacts/bin/Differential/release/Differential.dll \
  --offline-combat --root quality-REGENT-013 --memory-budget-mb 64 \
  --out .local/offline-combat/reroute
```

开局根与无头整场的实际搜索根一致：`ScenarioBuilder` 建出原生 `CombatState`，
`RulesCompiler.Compile` 冻结规则，`LiveCombatTracker.ImportRoot` 导入开局 `Battle`
（`RootCards`、`Reconciliation` 等选项与 Mod 首根相同）。注意 `--emit-live-requests` 后
`TestSession` 写出的 `BATL` 捕获根用的是普通 `ImportOptions.Default`，不是整场搜索根；在
120 个质量根上 96 根逐字节相同，24 个 DEFECT 根只差 `Counter.OrbChannels`（捕获根 0，
整场根按原生 History 重建为 1 或 2）。离线工具使用整场根，因此与实机整场搜索结果对齐。
搜索请求用同一个 `SearchRequestCompiler` 组装第一轮，性能档默认 `Medium`（起始宽度
256、最多 512、每轮节点上限 宽度 × 2,000、决策总时间 120 秒）；离线模式把每轮软时间设为
`TimeSpan.MaxValue`，只受节点上限和全局内存预算约束。多轮翻倍、每轮节点上限、终局比较、
局面池放大与时间预估都在 Core 的 `WideningSearch` 里，Mod 与离线整场共用该入口。

离线不按时间截断或跳过任何一轮；它用游戏内同一套“上一轮耗时的 2 倍”预估，把实机会跳过
的轮次标成 `wouldSkip`、会截断的标成 `wouldTruncate`，决策级 `softTimeRisk` 为任一标记。
`rounds[].remainingBeforeMilliseconds`/`estimatedMilliseconds` 是当时的预估输入，来自挂钟，
因此两次运行的这几个字段可能有小差异；`expandedNodes`、`routeActions`、胜负等确定性字段
不变。报告不能把 `wouldSkip`/`wouldTruncate` 的轮次当成实机一定会执行。

每根输出 `<out>/<root>/full-combat.json`，字段与无头整场的 `full-combat.json` 对齐：
`outcome`、`turns`、`playerHp`、`playerMaxHp`、`potionsUsed`、`searches`、
`reusedTurns`、`deviations`、`deploymentFailures`、`elapsedMs`、`pauseReason`、
`searchLog`、`deviationLog`、`failureLog`。`searchLog` 的每项包含
`turn`/`reason`/`status`/`elapsedMs`/`expandedNodes`/`routeActions`/`victory`/`error`，另加
`managedHeapPeakBytes`/`workingSetPeakBytes`、`bestRound`、`damageFreeVictory`、
`roundsRun`、`roundsSkippedByTime`、`softTimeRisk` 和 `rounds`。`rounds` 的每项记录
`width`/`maxExpandedNodes`/`poolStateBudgetBytes`/`elapsedMs`/`expandedNodes`/`routeActions`/
`victory`/`damageFreeVictory`/`wouldSkip`/`wouldTruncate`/`poolCapacityDropped`/
`searchMemoryPeakBytes` 等。父进程还写 `<out>/summary.json`（档位、起始/最多宽度、每宽节点
系数、总时间、逐根摘要与墙钟）。逐根摘要的 `exitCode` 是子进程退出码，`exceptionType` 是
从 stderr 的 `Unhandled exception.` 行解析出的异常类型；子进程没有写出 `full-combat.json`
时 `status=crashed`、`error` 为异常类型与消息（解析不到时保留退出码与缺失文件的说明），
不会再把 `[scenario] ...` 的最后一个阶段当成错误原因。

路线用完而战斗未结束时，离线工具从预测的最后一个 `Battle` 直接重搜；Mod 在同样的边界用
`LiveCombatTracker.ImportRoot` 从实机重新导入。两者逐字段的关系是：

- 原生可观测字段（HP、护甲、牌堆、卡值、9 条 RNG、Powers、Relics、Orbs、药水、回合/侧/
  阶段、结局）由同一 `BattleImporter` 投影；预测正确时离线根与实机根逐字节相同，逐动作
  `BattleReconciler`（`src/Import/Combat/BattleReconciler.cs`）已经在前一回合验证过这一点。
- `ShuffleEventCount`、`ShufflesCrossed`、`HasRisk` 与
  `LiveCombatTracker.CarriedCounters` 中的求解器账本在实机不可观测，`ImportRoot` 从最近预测
  边界携带；离线根本来就带着这些值，因此相同。
- `RevivingEnemies` 由预测边界中 `DeathPhase.Reviving` 的行决定；离线根的行相位已经是预测
  值，相同。
- 终局 `turns` 与 `playerHp` 按原生收尾投影：若最后一个动作到达敌方侧回合结束阶段
  （Doom 一类击杀），原生 `TurnNumber` 已前进一格，离线结果也加一；胜利后原生
  `AfterCombatVictory` 会结算 Burning Blood/Black Blood/Meat on the Bone，离线用同一个
  `PostCombatRelicHealProfile` 把等价治疗加到 `playerHp`。这两项都是结果投影，不回写搜索
  根或后续搜索输入。
- 实机比较视图会归一化永久死亡敌人的 AI 节点/日志、其名下 Power 行、detached 卡的一次性
  `PowerAfflictionChecked`、`NIGHTMARE_POWER.Target` 与不衰减 Power 的
  `SkipNextTickFromMonster`。这些字段在实机导入根与预测根之间可能不同，但都不参与后续
  搜索行为；离线重搜保留预测值。若后续在这些字段上加入搜索语义，需要先把它们从比较投影
  移回严格比较。

离线搜索的评分/下界配置仍从开局原生状态捕获（`SearchPolicyCapture` 与
`EvaluationConfigCompiler` 的冻结表），因为离线进程没有后续回合的原生模型；这是离线重搜与实机
重搜唯一未逐字节对齐的输入。本批四档 120 根默认仍只需要开局搜索，因此不影响对应关系；
出现路线用尽后的重搜时，需要同时验证这部分的请求字节。

整场等价的口径是逐根比较 `outcome`、`turns`、`playerHp`、`searches`，以及每次搜索的
`expandedNodes` 与 `routeActions`；不设容差。汇总表用仓库入口
[`tools/testing/offline-combat-table.py`](../tools/testing/offline-combat-table.py) 生成，格式
与协调者的 q120 表一致：

```bash
tools/testing/offline-combat-table.py \
  --results .local/offline-combat/Medium \
  --baseline ~/ss/solver/.local/q120/baseline.tsv
```

它按角色、幕与房间类型对照 CombatSolver 基线，并列出胜负不同的根与异常（非胜负结果、偏差、
部署失败）。四档把 `--results` 换成对应的 `--out` 目录即可复用同一张表。

### 6.8 离线搜索内存与计时

`--offline-combat` 的每次决策和每一轮都写内存诊断：`searchMemoryUsedBytes`、
`searchMemoryPeakBytes`、`routeNodeStorageBytes`、`routeNodePeakCount`、
`memoryBudgetExceeded`、`memoryExceededAtBytes` 与 `poolCapacityDropped`。这些是搜索账本
的逻辑字节，不读 GC；`managedHeapPeakBytes` 才是进程托管堆采样，只用于报告。多轮搜索共用
一份 `SearchStorage`，轮次之间保留的块也计入预算；默认局面池从 200 MiB 随宽度放大到
256/512/1,024 MiB（宽度 256/512/1,024），宽度 2,048 的单池上限为 1,728 MiB；乘积先按
64 位计算再截断到 `int`，放大后的实际池块逐块记账。
`--pool-state-budget-mb N` 可把每轮池上限固定为 N MiB，用于放大前后对照，不进入 `SREQ`。

离线宿主的默认预算固定按 0.15 × 16 GiB = 2,576,980,377 B；`--memory-budget-mb N` 用 MiB
覆盖。预算触发后搜索停止展开并返回当时最好的路线（没有候选时取已展开状态的排序领先者），
因此整场仍能打完。两个小预算回归例子：

```bash
# 3 个根 × 64/128/256 MiB；同一预算跑两次，逐根比较确定性字段
for mb in 64 128 256; do
  dotnet artifacts/bin/Differential/release/Differential.dll \
    --offline-combat --root quality-REGENT-013 --memory-budget-mb $mb \
    --out .local/budget-test/b$mb
done
```

离线子进程默认先做一次 50,000 节点的热身求解（同一根、同宽度、不计入 `elapsedMs`），
让分层 JIT 在测得的搜索前达到游戏进程的热状态。环境变量
`STS2_SOLVER_OFFLINE_WARMUP_NODES` 可改节点数，0 关闭；测内存对照时应设为 0，避免热身堆残留
抬高 `managedHeapPeakBytes`。计时口径：同一个 120 根、默认 Medium、`--jobs 6`；对照
`~/ss/solver/.local/q120c` 的 3 lane 无头游戏结果，只取旧离线 `w240` 中
`elapsedMs >= 500` 的 117 次搜索，逐次算 `gameMs / offlineMs`，取中位数。

同一请求复用存储可单独验证：

```bash
STS2_SOLVER_STORAGE_CHECK=1 dotnet artifacts/bin/Differential/release/Differential.dll \
  --offline-combat --root quality-REGENT-013 --out .local/storage-check
# STORAGE_CHECK same=True firstBytes=... secondBytes=... secondDelta=...
#   regionsFirst=[chunk:...,array:...] regionsSecond=[chunk:...,array:...]
```

`chunk`/`array` 两类在第二次求解不再增长，说明大块已从 `SearchStorage` 复用；第二次仍会
为池选择结构、路由签名字典与标签列表分配小块。`--emit-live-requests` 与
`run-headless.sh --cleanup-instance-on-exit` 的 28 根核对见第 7 节，不随本批改变。
