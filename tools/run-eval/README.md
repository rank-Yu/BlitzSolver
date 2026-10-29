# run-eval 首战烟测 CLI

普通 .NET 9 独立进程，不加入 solution。当前只从原版新局推进到第一场真实战斗结束，
首战完成后、领奖之前停止；不代表整局胜利。后续批量整局评估仍未实现。

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
因此可以观察 BurningBlood 等正常战后 hook。

| exit | 含义 |
| --- | --- |
| 0 | Paused，milestone=first-combat-complete，原版终局比较成功 |
| 1 | Failed、Death 或其他未达首战完成的结果 |
| 2 | 参数无效、游戏路径/程序集缺失 |
| 4 | 总体超时 |
| 130 | 外部取消 |

缺捕获/终局差异不产生 milestone。first-combat-complete 不是整局 Victory。
已验证 Ironclad A0 的 R3B2-DECISION、R3B5-SECOND，以及 Silent A0 的 R3B2-DECISION，所有部署/终局比较通过。
主 seed 经两次独立 CLI 进程与独立旧 Differential 原版参考对照通过。
初始化/迁移差异、生命周期与当前边界详见 src/OfflineHost/README.md。
