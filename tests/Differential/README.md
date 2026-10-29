# Differential

进程内差分宿主：在普通 .NET 9 控制台进程里初始化原版《杀戮尖塔 2》的静态状态，用原版
`RunManager`、`ModelDb` 与 `CombatState` 建局，再把原生状态导入 `Battle`/`RunState` 与
Blitz 的结果逐字节比较。它不启动 Godot 可执行文件，也不需要场景树。

R3-0 起局外由原版推进；本工程保留导入、建局、地图进点、战斗边界、规则/配置与战斗 pilot
的对照。局外整局运行在 `src/OfflineHost`（R3b）。

本工程不在 `BlitzSolver.sln` 中；CI 没有游戏文件，不会构建或运行它。

## 需要游戏文件

需要本机游戏安装目录，至少包含 `sts2.dll`、`GodotSharp.dll`、`0Harmony.dll`。路径通过本目录
下被 git 忽略的 `local.props` 配置；模板是 `local.props.example`。`local.props` 不要提交。

## 构建

```bash
dotnet build tests/Differential/Differential.csproj -c Release
```

产物在 `artifacts/bin/Differential/release/Differential.dll`。构建不读取游戏文件到仓库。

## 建一个根 / 全部根

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --root kg2-b-stomp --out .local/differential/out
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 8 --out .local/differential/out
```

每个根一个子进程，避免游戏静态状态跨根污染；`--jobs` 取值 1 到 8。

## 战斗导入与规则

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --root kg2-b-stomp --import-compare
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 8 --import-compare
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 8 --import-compare --compiled-rules
dotnet artifacts/bin/Differential/release/Differential.dll --root kg2-b-stomp --rules-compare
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 8 --rules-compare
dotnet artifacts/bin/Differential/release/Differential.dll --rules-table-check
dotnet artifacts/bin/Differential/release/Differential.dll --root kg2-b-stomp --config-compare
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 8 --config-compare
```

`--import-compare` 建原生状态、导入 `Battle`，与 `d1/<root>/golden.br` 的期望 `BATL` 逐字节
比较；`--compiled-rules` 改用 `RulesCompiler.Compile` 的输出。`--rules-compare` 比较
`RulesCodec.Encode`；`--config-compare` 比较 `config.br` 的完整请求与特征哈希。
`--import-negative` 验证私有字段/未认领状态/第三方内容三类拒绝。

## 内容表与 RulesBuilder

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --content-table-report
dotnet artifacts/bin/Differential/release/Differential.dll --rules-build-compare --build-kind d1 --build-jobs 8
dotnet artifacts/bin/Differential/release/Differential.dll --rules-build-compare --build-kind natural --build-seeds 20 --build-jobs 8
dotnet artifacts/bin/Differential/release/Differential.dll --rules-build-compare --build-kind encounter --build-jobs 8
```

`--content-table-report` 导出一次进程内内容表并打印覆盖、耗时与保留内存。
`--rules-build-compare` 对每个根同时走原生 `RulesCompiler` 与导入 `RunState`/`Battle` 后的
Core `RulesBuilder`，逐字节比较两份 `RulesCodec`。加 `--build-content` 汇总用到的卡牌等级与
怪物 id。

## 整局状态导入

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --run-import-compare --run-kind all --run-seeds 20 --run-jobs 8
dotnet artifacts/bin/Differential/release/Differential.dll --run-import-compare --run-kind natural --run-seeds 20 --run-jobs 8
dotnet artifacts/bin/Differential/release/Differential.dll --run-import-compare --run-kind d1 --run-jobs 8
dotnet artifacts/bin/Differential/release/Differential.dll --run-import-negative
```

自然开局（5 角色 × A0/A10 × 20 种子）与全部 410 个 D1 输入各起一个子进程：导入
`RunState`，与独立原生转储逐字段比较；每条随机流抽 64 次；比较当前幕地图；D1 还检查
9 条战斗流、HP、牌组、遗物与药水。

## 原版决策导入首批

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --run-decision-compare
```

独立模式读取真实原版模型，经过 `RunDecisionImporter`、Core 回答校验、请求内原实例解析，
再调用原版地图/事件/休息入口并重新导入。输出分开标记自然 Neow→地图与定向原版 fixture；
fixture 通过原版 EnterRoomDebug 进入事件/休息，候选替换、禁用与容量负例会显式修改测试状态。
关键效果与同输入独立直接原版调用比较，包含实际事件换页、Field 多选移除与加诅咒、休息和
Smith 嵌套升级/取消。负例检查非法回答、旧 Token、重放、候选替换、牌组/可见负载变化和缺少
确认均无原版副作用；原版派发 Task 异常必须向调用者传播。导入前后比较独立原生转储与 RNG。

该模式自己的测试 session 管理 session/revision、消费/失效及嵌套 pending，原版选牌实际
callback 结合三个受支持命令的完整 prefs 作用域导入；不会由 min/max 猜 Cancelable，也不为
自动全选或零候选建立请求。只绕过无 Godot 进程的资源/路径/hover UI 调用，原版效果保留。
测试串行且没有外部并发动作；不代表生产 OfflineHost、RunPilot 或 Mod UI 已实现，也不覆盖
bundle 与通用 RelicSelect。

## 奖励与商店原版决策闭环

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --run-reward-shop-compare
```

独立定向 fixture 模式通过原版 RewardsSet.Offer 的 testSelector 与真实 MerchantRoom.Enter 入口，
复用测试 session 的 Token、消费/失效与嵌套回调；不声称自然通关。覆盖已 Begin 集合只读、强制奖励、
重复模型不同实例、实际 Skip/Reroll 及重抽后的再次 callback、真实 PaelsWing 未知替代项拒绝、容量、
奖励父/子/返回列表、满槽失败/整理返回/另行领取与 NotAllowed 消费、商店禁用/售罄/折扣/补货身份、
卡牌/遗物/药水购买、真实删牌子请求成功/取消与失效等待链。代表性动作同种子分别从新局 fixture
独立直接调用原版，对比 NativeRunDump、公开奖励/库存字段与 RNG；导入及非法回答无副作用。

仅本模式撤销旧 bootstrap 对 CardRewardAlternative.Generate 的屏蔽，保留真实 Generate/Hook，
构造器只严格替换三处 Godot 热键展示读取。原同步 TestMode callback 在自身 while 中多次进入，
每次新 Token；不伪称异步 UI 等待接口。PotionDiscard 是宿主显式整理视图，Skip 返回原奖励列表、
Discard 只调用 PotionCmd.Discard，领取需新列表动作，未覆盖 NPotionPopup 的动作入队/UI 链。
商店 Leave 只关闭库存，不退出房间；删牌成功后的 SetUsed 仅补原 UI 职责，不重写扣费/移牌。
MerchantPotionEntry.CalcCost 周围用局部 prefix/finalizer 还原真实随机价路径并检查异常恢复；
旧 probe/测试默认模式不安装这些新增桥接。正式 OfflineHost/Mod 与通用 bundle/RelicSelect 仍未落地。

## 战斗边界建局

```bash
dotnet artifacts/bin/Differential/release/Differential.dll \
  --combat-setup-compare --setup-kind natural --setup-seeds 100 --setup-jobs 8
dotnet artifacts/bin/Differential/release/Differential.dll \
  --combat-setup-compare --setup-kind encounter --setup-seeds 20 --setup-jobs 8 --setup-shard 0 2
dotnet artifacts/bin/Differential/release/Differential.dll \
  --combat-setup-compare --setup-kind d1 --setup-seeds 4 --setup-jobs 8 --setup-shard 0 2
dotnet artifacts/bin/Differential/release/Differential.dll \
  --combat-setup-compare --setup-relic-matrix --setup-matrix-seeds 20 --setup-jobs 8
```

每个用例在独立子进程里：原版 `RunManager.EnterMapCoord` 到可出牌，导入 `Battle`、编译
`Rules`、转储进点后 `RunState`；Blitz 侧导入同一进点前 `RunState`，调用
`RunCombatSetup.EnterMapCoord`，用 `RulesBuilder` 建 `Rules`、`RunDump` 转储。比较
`Battle`/`RulesCodec` 逐字节与 `RunState` 逐字段。`--setup-cover` 只统计覆盖，
`--setup-choices` 打印开战选择请求树，`--setup-matrix-list` 打印遗物矩阵名单。

## 地图选点与进房间

```bash
dotnet artifacts/bin/Differential/release/Differential.dll \
  --map-step-compare --map-step-case "Ironclad|0|empty|0|R2B2-PROBE-1" --map-step-acts 3
dotnet artifacts/bin/Differential/release/Differential.dll \
  --map-step-compare --map-step-seeds 8 --map-step-jobs 4 --map-step-acts 3
```

原版从新局自己推进到每个地图选点；每到一个选点导出一次 `SerializableRun` 快照，对每个合法
选项恢复同一快照并分别执行 `RunManager.EnterMapCoord` 与 `RunMapEntry.Enter`。战斗比较
`Battle`/`Rules`/`RunState`；非战斗房间用测试补丁截在 `RunManager.EnterRoomInternal` 之前，
比较"房间已建好、历史已追加、尚未进入"的 `RunState`（事件 RNG 单独比较）。战斗用杀敌捷径，
奖励与房间内容跳过；捷径在每次导入之前，不进入比较。

## 战斗 pilot

```bash
# 120 个质量根，默认 8 个子进程
dotnet artifacts/bin/Differential/release/Differential.dll --combat-pilot-compare
dotnet artifacts/bin/Differential/release/Differential.dll --combat-pilot-compare --pilot-root quality-IRONCLAD-001
dotnet artifacts/bin/Differential/release/Differential.dll --combat-pilot-compare --pilot-jobs 4 --pilot-shard 0 2
```

低档 `CombatPilot` 生成路线，`NativeActionExecutor` 用原版公开入口逐动作执行；每个可出牌动作
边界导入 `Battle` 与预测逐字节比较。终局在 `EndCombatInternal`/`ProcessPendingLoss` 的 prefix
捕获原版 `Battle`，比较胜负、HP/MaxHP、金币、药水、遗物状态与 9 条战斗 RNG；手牌/牌堆/回合
内计数等清理字段不在合同内。捕获失败计 reject，不计 same。

## 根重新捕获

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --root qf-nightmare-detached --recapture-root --out .local/recapture
dotnet artifacts/bin/Differential/release/Differential.dll --all --jobs 4 --recapture-root --out .local/recapture-all
```

每个根按与 `--config-compare` 相同的导入/前缀回放/回合准备枚举路径重建根和 `config.br`，写出
`<out>/<root>/roots.gold` 与 `<out>/<root>/config.br`。

## 实机请求/结果与搜索对照

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --emit-live-requests .local/live-requests --all
dotnet artifacts/bin/Differential/release/Differential.dll --live-compare .local/m2b0-20-results
dotnet artifacts/bin/Differential/release/Differential.dll --search-compare .local/m2b1-20-results
```

`--emit-live-requests` 为 Mod 无头测试生成请求；`--live-compare` 比较游戏写出的
`BATL`/`RULE`/`SREQ.capture`/`native.txt`；`--search-compare` 重解游戏 `SREQ` 并比较 `SRES`。
启动脚本见 [`tools/testing/run-headless.sh`](../../tools/testing/run-headless.sh)。

## 离线整场自动战斗

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --offline-combat --corpora tests/corpora --jobs 4 \
  --out .local/offline-combat/default
dotnet artifacts/bin/Differential/release/Differential.dll --offline-combat --root quality-DEFECT-000 \
  --beam-width 256 --max-width 256 --out .local/offline-combat/one
```

不启动 Godot，用与 Mod 相同的 `CombatPilot` 在内核里打完一个根。每个根写
`<out>/<root>/full-combat.json`，父进程写 `<out>/summary.json`；`--search-tier` 默认
`Medium`，`--memory-budget-mb` 覆盖搜索内存预算。

## 与参考转储对照

```bash
dotnet artifacts/bin/Differential/release/Differential.dll --compare-only .local/differential/out .local/m0ref/dumps
```

## 输入

- fixture：`tests/corpora/fixtures/<root>.json`；
- 质量语料：`tests/corpora/quality/<name>/spec.json` 与 `request.json`，根名为
  `quality-<name>`。

请求 DTO 覆盖当前 410 个输入、生成场景 spec 与评估政策实际用到的字段；未知字段、非空
`checkpointArchivePath`/`replayPolicyOverridePath` 以及不支持的搜索/部署值会显式失败。

## R3a 原版整局可行性探针

这是可复用的流程验证工具与流程基线，正式 OfflineHost/RunPilot/策略接口留待 R3b。探针使用正常原版
`EnterAct(0)` 新局入口、全解锁、A0、关闭教学修改；地图取第一个合法点，事件优先合法继续否则
第一项，奖励/卡牌取第一项，满药水槽跳过新药水，商店按原版合法选项顺序购买，休息在半血以下
优先治疗，否则升级。策略只读取当前公开选项；战斗读取真实 RNG 是既定战斗例外。
它不修改自然局的 HP、牌组、金币、敌人或未来序列，不代表有质量的局外策略。

```bash
# 仓库根目录；local.props 与 .NET 环境沿用上文配置。
dotnet build tests/Differential -c Release
dotnet artifacts/bin/Differential/release/Differential.dll --run-probe \
  --character Ironclad --seed R3A-0 --tier Medium --out .local/r3a/one
# 每局独立子进程；固定 R3A-0、R3A-1，五角色 A0，共十局。
dotnet artifacts/bin/Differential/release/Differential.dll --run-probe \
  --batch --seeds 2 --jobs 4 --max-seconds 90 --out .local/r3a/matrix
# 单独标记的定向夹具，不计入自然跑局成绩。
dotnet artifacts/bin/Differential/release/Differential.dll --run-probe \
  --fixture rooms --out .local/r3a/rooms
```

`--max-rooms` 默认100，`--max-actions` 默认10000（局外请求、战斗搜索请求与部署动作合计），
`--max-seconds` 默认1800。房间数包括正常地图进入，不把夹具注入计入自然房间数。
`--jobs` 只能1..4，批次默认4。每局主进程有时长预算与战斗取消 token，父进程额外在预算+60秒
强制终止卡死子进程；Core 内部一次深层动作展开未必及时响应取消，因此必须保留父硬限。
非法参数退出2，真实不支持退出3，等待超时4，预算5；父批次任何子进程非零都返回1，崩溃不会
变成死亡/胜利。自然死亡是合法终局，死亡前没有到达的内容不算已覆盖。

探针默认复用完整 `WideningSearch`：Medium 宽256..512，每轮节点上限为宽×2000，固定内存预算
为16GiB×15%。和既有离线战斗相同，为可复现运行全部宽度，`EnforceTimeBudget=false`，不会按
机器速度跳过轮次；它不是实机120秒保险的墙钟复刻。Low只有256宽。每局记录档位、每次搜索轮次、
实际展开节点、所选轮次与重搜次数；不能把早期单轮调试结果当完整Medium性能。

每局目录包含 `trace.jsonl`（probe-only可见请求、选择、原版RunState的规范编码SHA256与摘要）、
`summary.json`（状态、互斥耗时、峰值内存）、`progress.json`（最近推进/搜索/部署阶段），以及
父进程保存的 stdout/stderr。每轮进入求解前覆盖保存 `search-request.bin` 与 `search-root.txt`，
便于定位搜索阻塞；差异时另存未经比较规范化的 predicted/native Battle 二进制和文本。
`batch.json` 记录并发度、父测逐进程墙钟、批次墙钟、退出码和吞吐量。

耗时互斥归类：initialization包含游戏初始化和正常开局；combat_search包含请求编译、完整翻倍
搜索及路线回放；native_combat是原版部署和边界等待；native_run是原版局外推进及异步消息泵等待；
import_check是RunState导入、战斗导入与逐边界核对；policy是有显式计时的选择规则；logging包括
JSON/进度/搜索证据文件；other包括剩余驱动与计时开销。嵌套进入新类别暂停旧类别，按活动scope
标识处理非LIFO异步完成，summary给出分段之和与未归类余量。policy中未单独包裹的常数时间选项
索引操作归other。进程CLR启动/JIT早于探针构造的开销由父测processWallMs覆盖，不混入分段。
不要用并行批次墙钟除各进程耗时推算局外占比。

可选夹具：`shop-price`验证真实Shops随机价消耗和TestMode正常/异常恢复；`telemetry`强制真实
RewardsSet选择器跨Task.Yield，验证父计时scope先退出时仍保持奖励类别；`contracts`验证永久离场
敌人的SkipNextMove比较规范化及严格负例；`rooms`覆盖Trial多页、SelfHelpBook选牌、休息升级、
商店购买/删牌、宝箱、奖励跳过/满槽药水、幕切换与原版WinRun终局。`unsupported`实际调用尚无
离线桥接的RelicSelectCmd入口，必须非零。夹具显式通过公开入口注入内容/物品，不能作为自然通关证据。

Godot旁路仅限预加载、悬浮说明展示与存档路径；Trial另对两段无TestMode保护的本地UI分支及
纯肖像VFX做定点旁路，原版事件RNG/选项/结算保留。原版奖励Offer只生成一次；商店药水
CalcCost临时关闭TestMode并由Harmony finalizer恢复，异常同样恢复。原版其余TestMode差别仍见
[离线宿主](../../docs/offline-host.md)，本探针不证明所有事件/界面或三幕通关。

R3a曾发现原始 `RunCodec` 字节哈希跨进程不总相同：`RunRef` 是4字节布局，Kind在0、Id在2，
中间1字节是CLR隐式padding。当时探针临时清理392处RunRef padding计算语义hash作为诊断，原始
hash差异保持公开。此后独立Core修复在正式编码输出边界清零所有2,452个隐式padding字节，布局、
v3格式与字段校验不变；覆盖合同位于KernelChecks。探针已删除临时掩码实现，`stateHash` 直接哈希
正式编码，保留 `semanticStateHash` 为相同值的日志兼容别名。跨进程复跑必须逐条严格比较完整
trace（包括两个hash字段）与summary全部非时间/内存facts。`BLITZ_PROBE_CAPTURE_RUNSTATE=0/12`
可额外保存幕0层12决策边界的正式RunState二进制和完整字段转储，供独立定位。

## 正式局外执行库回归

`dotnet run --project tests/Differential -c Release -- --run-host-compare` 调用正式
`OfflineRunHost`/`RunExecutor`，不与旧 decision/probe callback hooks 同时安装。
自然新局只验证 EnterAct→Neow→地图→首战第一回合，返回 Paused(combat-boundary)；
Smith 的成功/取消、stale、失效/外部取消/Dispose 与迟到答案使用真实异步 native 父子链。
独立原版调用用于对照 native dump、导入状态与 RNG；fake/定向 fixture 不作为完整通关证据。
