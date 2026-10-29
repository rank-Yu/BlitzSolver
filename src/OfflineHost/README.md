# OfflineHost 原版首战宿主

普通 .NET 9 库，不加入 solution。依赖 Core、Import、原版游戏、GodotSharp 和 Harmony；
不引用 TestHost/Differential，不链接其源文件。公开 FirstCombat 接口只有 BCL DTO。
游戏编译路径沿用 tests/Differential/GameReferences.props 和被忽略的 local.props；运行时通过
FirstCombatOptions.GameDataDirectory 显式解析游戏程序集。

## 独立首战

FirstCombat.Run 每进程只运行一局。NewRunFactory 按原版顺序执行 CreateForNewRun →
SetUpNewSingleplayer(shouldSave:false) → FinalizeStartingRelics → Launch → EnterAct。
显式配置角色、seed、进阶和 neow-epoch-v1 解锁（unlockedEpochIds=[NeowEpoch.Id]、encountersSeen=[]、numberOfRuns=1）。
内存 ISaveStore 不读写真实存档。

Runtime 自建单线程 SynchronizationContext，驱动原版 async 续体。OfflineRunHost/RunPilot
处理可见 Neow、合法地图和真实异步选牌子请求。first-combat-smoke-v1 策略只接收 RunView：
只接受 Neow 事件、地图及其选牌请求；过滤 Disabled/Locked，优先可见 LAVA_ROCK，
否则选第一个合法选项。它只是烟测策略，没有 R4 局外估值，也不读取 seed、RNG 或未来内容。
输出地图坐标和点类型。未知事件、房间和未支持奖励入口显式失败。

Combat 接入既有 RulesCompiler、LiveCombatTracker、CombatPilot、SearchRequestCompiler、
BeamSolver 和 WideningSearch。默认 Low：256 起始/最大宽、每宽 2000 节点、原有 60000ms 档位，
默认内存预算 2576980377 bytes（16 GiB 的 15%）。与旧 RunProbe 一样使用确定性 widening：
搜索按既有节点上限运行，各宽度的时间保险只记录；整体 timeout/token 仍覆盖求解和原版等待。
算法及搜索预算语义没有改动。

每个动作通过原版入队/执行/选择器，并导入原版边界逐步比较。原版 EndCombatInternal /
ProcessPendingLoss 清理前捕获终局，按既有 TerminalBattleProjection 合同比较 HP、maxHP、
gold、potions、relics、全部战斗 RNG 和原版结束路径。缺捕获、捕获失败和差异均阻止完成；
预测 action.After 不能自行宣告原版胜利。终局验证成功、原版动作完成且队列空闲后，
另报结算后的真实 RunState 摘要。首战完成返回 Paused / first-combat-complete，随后停止，
尚未领取奖励；死亡为 Death，部署偏差为 Failed，取消和超时有独立状态。没有整局通关声明。

## 初始化与无界面设施

移植来源及 MIT 声明保留在各文件头。新 runtime 没有迁移旧测试宿主的奖励 alternatives 抑制、
SoulNexus/Rocket/Crusher gameplay hook bypass 或 TurnSetupRootCapture。无界面补丁仅处理：
Godot 节点静态构造/资源预载、文案/hover tips、日志、时间、平台玩家名/branch、版本、profile 路径、
内存存档初始化和 replay 输出。运行使用原版 TestMode 无场景树路径；不修改生命、库存或房间。

初始化设施的私有写入集中在 Runtime：ReflectionHelper._modTypes 置空防止扫描未加载 mod；
ModManager.State 标记模型发现初始化完成；NGame._mainThreadId 绑定消息泵线程；
OfflineLocalization 只构造空 LocManager 的表、语言、culture、比较器及 callbacks 并安装单例。
这些是替代 Godot/平台初始化的设施；不读取或写入游戏模型私有 gameplay 状态。
游戏模型的私有导入继续只在 Import。

## 嵌入执行库与生命周期

OfflineRunHost 保留会话/单调版本、fresh snapshot、完整回答/确认/实例绑定、消费后 dispatch、
异步子选择、取消和释放合同。未注入战斗 delegate 时返回 Paused / combat-boundary。
Runtime 只在明确已结束的事件且原版队列稳定时报告地图可旅行；不猜测未知房间可旅行。

全进程只允许一个 native host。动作订阅与 selector 均在 finally 释放；取消后的迟到回答不能执行。
整体取消贯穿消息泵、求解、动作入队/完成、边界和队列等待。取消不能回滚已发生的原版效果。
本独立入口一进程一局，即使失败/取消也禁止在同进程重启；CLI 进程退出承担原版不可取消后台工作
的最终隔离，不能把此入口当作跨局复用的 runtime。

## 验证范围

已验证 Ironclad A0：R3B2-DECISION 和 R3B5-SECOND；Silent A0：R3B2-DECISION，均为 neow-epoch-v1、Low。角色由 CLI 显式指定；profile 控制内容，Silent 夹具不声称来自已解锁存档的自然角色菜单流程。
主 seed 两个独立 CLI 进程结果完全一致；旧 Differential runtime + RunTestHost + NativePilotHost
独立原版参考的起始/开战/终局/结算后摘要、动作与搜索计数完全一致。
测试专用 Harmony 负向夹具验证实际 runner 遇 HP 差异/缺捕获不能给出 milestone，
并在原版首动作完成后取消，检查 selector/订阅释放及单进程单局限制。生产不包含测试环境变量后门。

不支持领奖、商店、后续房间/三幕通关、批量评估、策略质量或全内容覆盖。
使用方式见 tools/run-eval/README.md；共享执行协议见 docs/autoplay.md。
