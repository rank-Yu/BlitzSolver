# run-eval 有界烟测 CLI

普通 .NET 9 独立进程，不加入 solution，提供首战、下一房间和有界多房间三种停止模式。
默认首战完成后、领奖之前停止；显式 --stop-after next-room 继续领取奖励并进入下一房间后暂停。
--stop-after rooms 可在普通 Act1 连续处理多个房间；这些里程碑都不代表整局胜利。后续批量整局评估仍未实现。

## 运行

在 tests/Differential/local.props 配好编译所用游戏目录：

    dotnet build tools/run-eval -c Release
    dotnet artifacts/bin/run-eval/release/run-eval.dll --game-data /path/to/data_sts2_linuxbsd_x86_64

完整参数：

    --game-data PATH
    --seed R3B2-DECISION
    --character Ironclad
    --ascension 0
    --unlocks neow-epoch-v1
    --tier Low
    --timeout-seconds 300
    --stop-after first-combat|next-room|rooms
    --max-rooms 4
    --max-decisions 256
    --max-steps 512

--stop-after 接受 first-combat（默认）、next-room 或 rooms。next-room 使用 next-room-smoke-v1 可见策略，
按可见顺序领奖、选择首张合法卡、必要时管理满药水后显式再领取、Leave，然后选首个合法地图点。
rooms 使用 rooms-smoke-v1：Neow 优先可见 LAVA_ROCK，地图按当前合法点坐标 col/row 排序；事件优先可见 Proceed/Leave/GiveUp，否则取末项；休息取首个合法项；商店最多一次可负担购买后 Leave。
--max-rooms 的 1..100 预算只对 rooms 停止边界生效，默认 4，计完成的地图节点房间，不计 Neow；第 N 房达到可合法离开的边界即停止，不额外进入下一房。决策和步骤预算覆盖所有模式及异步子选择。
--game-data 必填。默认角色 Ironclad、A0、seed R3B2-DECISION。
角色接受 Ironclad/Silent/Regent/Necrobinder/Defect，但这不是所有角色/内容已验证的声明。
tier 接受 Low/Medium/High/ExtraHigh；使用既有确定性 widening 配置和 2576980377 bytes 内存预算。
唯一解锁 profile neow-epoch-v1 与旧 run-host-compare 的自然 Neow 样例相同：
unlockedEpochIds=[NeowEpoch.Id]、encountersSeen=[]、numberOfRuns=1。
角色由 CLI 显式指定；Silent 是使用该低解锁内容配置的角色夹具，
不代表已解锁存档的自然角色菜单流程。参数和所选档位写入 JSON。
--help 不加载游戏；Ctrl+C 取消整个会话。

每进程只跑一局。若调用公开 FirstCombat.Run，也必须提供有效配置，每个进程只允许一次调用。
每个进程独立初始化游戏静态状态；不能在取消/失败后直接复用原版单例启动下一局。
可执行输出随附根 LICENSE/README，保留上游版权声明。

## 输出与退出码

stdout 只有一行紧凑 JSON；原版初始化诊断在 stderr。默认不生成动作轨迹或磁盘报告。
JSON 包含完整配置、版本化 first-combat-smoke-v1 可见选择、起始/结算后 RunState SHA-256 和
HP/金币/牌组/遗物/药水摘要、开战/原版清理前终局 Battle SHA-256、原版结束路径、
动作数/搜索数/展开量以及部署/边界偏差数。原版终局 HP 与结算后 HP 分开报告，
因此可以观察 BurningBlood 等正常战后 hook。next-room 另报 afterRewards 和 nextRoom
（房间类型/模型、地图坐标、状态摘要）。原有首战输出字段与行为保留。rooms 另报 rooms（每个可旅行完成点）、combats（每战独立终局证据）、finalState；首战旧字段仍只指首战。finalState 仅在可确认的导入边界提供，dispatch 中的失败可为 null。

| exit | 含义 |
| --- | --- |
| 0 | Paused，milestone=first-combat-complete、next-room-entered 或 room-budget-reached，原版推进与所需终局比较成功 |
| 1 | Failed、Death、预算耗尽、范围停止或其他未达所选里程碑的结果 |
| 2 | 参数无效、游戏路径/程序集缺失 |
| 4 | 总体超时 |
| 130 | 外部取消 |

缺捕获/终局差异不产生 milestone。first-combat-complete 不是整局 Victory。
已验证 Ironclad A0 的 R3B2-DECISION、R3B5-SECOND，以及 Silent A0 的 R3B2-DECISION，所有部署/终局比较通过。
主 seed 经两次独立 CLI 进程与独立旧 Differential 原版参考对照通过。
next-room 主 seed 两次独立进程结果一致，并与独立旧 runtime 原版参考逐段一致。
主 seed rooms4 两个独立新进程完全一致，并与独立原版 oracle 四个完成房间和三场战斗逐字段一致。
初始化/迁移差异、生命周期与当前边界详见 src/OfflineHost/README.md。
