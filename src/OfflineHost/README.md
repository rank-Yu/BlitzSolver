# OfflineHost 原版有界整局宿主

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
输出地图坐标和点类型。next-room-smoke-v1 还选择可见奖励、首张合法卡及合法 Leave；满药水时丢弃首个合法槽，
回到奖励列表后重新选择领取。这些策略都只读可见 DTO。first/next 的选择顺序兼容；rooms-smoke-v1 见下节。未知原生命令和未授权奖励入口显式失败。

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
尚未领取奖励。显式 StopAfter="next-room" 则继续处理奖励并进入下一房间，
返回通用 Paused / next-room-entered；死亡为 Death，部署偏差为 Failed，取消和超时有独立状态。没有整局通关声明。

## 战后领奖与下一房间

新模式在原版首战终局核对、战后队列稳定后，由原版 CombatRoom.OfferRoomEndRewards →
RewardsCmd.GenerateForRoomEnd → BeforeCombatRewardOffered → RewardsSet.Offer 生成并展示一次。
host 持有单一 owner/player/room/set 租约，受限 testSelector 替代 TestMode 默认自动全领；
保留原始 generation/Offer Task，观察原版 TaskHelper.RunSafely 会吞掉的异常。
未授权嵌套或其他奖励 set 在 Offer 进入生成前失败。

每次奖励列表都是新的 snapshot/token。CardReward.OnSelect 保持原样，原版 ShowScreen 返回
只包含托管身份的 UI surrogate，OptionSelected 异步等待同一 RunPilot 的 child policy。
实例绑定、确认、stale、replay、取消及迟到回答使用共享宿主合同；原版继续负责同步器、
发牌、history、hook 和 Reroll 循环。RefreshOptions/GetCardHolder 仅处理展示，不执行 Godot。
surrogate 不调用 Godot 构造或 Dispose，创建后立即抑制终结器；弱身份表保证已关闭 surrogate
的 OptionSelected/RefreshOptions/GetCardHolder 迟到调用也不能进入 Godot。只替换 alternatives 构造中的 hotkey 字符串读取，
不抑制 Generate 或奖励 hook；Reroll 后按原版最初 alternatives 实例解释返回索引，
新请求隐藏已用 Reroll。

满药水的原版领取返回 false 后，host 提供显式 PotionDiscard child。丢弃经过原版
DiscardPotionGameAction 的队列与完成等待，返回保留库存；两者都回奖励列表，必须新操作领取。
terminal Leave 调用原版 ProceedFromTerminalRewardsScreen 打开地图。原版并不在此跳过未领奖励，
所以 host 继续持有 OfferTask；实际 EnterMapCoord 的 BeforeLeavingRoom 才写入 OnSkipped/history
并完成同步器 stack。新房间稳定后等待原始 generation/Offer/Start 完成，释放 selector、订阅和 owner，再清空 host 引用；next-room 返回通用 Paused，rooms 可以新建下一场奖励租约。
新增 AfterRewards/NextRoom 摘要与 next-room-smoke-v1 可见选择；默认 first-combat 模式兼容。

取消不能回滚原版已发生效果。原版 CardReward.OnSelect 在 await 被取消时可能保留未完成的
screen 引用及模型自己的 RelicObtained 订阅，host 不写它的私有 gameplay 状态来伪装完成；
host 的 selector/事件订阅/租约全部释放，已知 surrogate 的 OptionSelected/RefreshOptions/GetCardHolder 迟到调用显式拒绝。
该局不可恢复，独立 CLI 一进程一局提供最终隔离。

## 有界多房间与商店

显式 StopAfter="rooms" 使用同一个 RunPilot，MaxRooms 默认 4，计数是达到本房原版可旅行边界的
地图节点数，排除 Neow（虽然其坐标是第 0 行）。第 N 房处理完成后直接 Paused / room-budget-reached，
不会进入第 N+1 房。事件走 EventSynchronizer；IsProceed 保留原始回调。休息走 RestSiteSynchronizer，
取消 Smith 后仍继续选择，MiniatureTent 等保留选项时要等剩余选项真正完成。普通 Heal 的原版空
OfferCustom 保留生成/hook/同步器，仅在 Heal dispatch 作用域允许一次无房间、无可选奖励的 set；
若生成 hook 加入奖励，在 BeginRewardsSet 之前显式 Unsupported，未知嵌套不会被自动领取。

rooms-smoke-v1 优先可见 Neow LAVA_ROCK；合法地图点按当前可见 col、row 排序；自然事件优先
可见 Proceed/Leave/GiveUp，否则取末个合法项；休息选首个合法项；每个商店最多购买一项当前
可负担商品，再 Leave。这些偏好只读 RunView/NativeKey，不看 seed、RNG 或未来内容。
该保守策略不是胜率、估值或最优性声明。每场战斗都重新编译 rules、tracker/root/search policy；
进入战斗前清空旧终局证据，逐动作和终局继续与原版核对。原有 Combat/AfterCombat/AfterRewards
始终表示首战；新增 Rooms/Combats 汇总和 FinalState。FinalState 只在可确认导入的边界提供，
原生命令将要 dispatch 时清空，避免失败/取消后的旧摘要被当成最终状态。

商店展示已有 MerchantInventory 的 Cost 与原实体绑定；每次买/失败/补货后都重新导入 snapshot/token。
商品走 OnTryPurchaseWrapper；删牌使用 cancelable:true 的原版 wrapper，真正异步子选择带完整
prefs/确认/Cancel 与仅该 dispatch 生效的 Remove purpose。成功后补 NMerchantInventory 的
SetUsed 模型责任，取消不花金币、不标用。满药水商品按原版合法性禁用，不自动开启奖励丢弃流程。
Leave 只关闭展示；实际原版 Exit 才写未购卡/遗物/药水的 history，末房预算停止时不补写。

MerchantRuntime 仅包住同步 MerchantPotionEntry.CalcCost 与 MerchantRoom.Exit，将 TestMode
暂置 false 并在 Harmony finalizer 恢复。原版价格算法、hook 折扣/补货、Shops RNG 与未购 history
逻辑不变，不跨 await 关闭 TestMode，不重新计算已展示的 Cost。资源旁路只新增 merchant assets 预载。

主 seed Ironclad/A0/R3B2-DECISION/Low 的 rooms4 自然运行完成 SlimesWeak、NibbitsWeak、
TabletOfTruth、ShrinkerBeetleWeak，共三场战斗和各自奖励。独立旧 runtime/TestHost/旧
NativePilotHost 直接推进的逐房 RunDump、NativeRunDump、完整 MapPointHistory 与三份完整战斗摘要一致。
定向生产夹具另覆盖商店购买/补货/stale/删牌/满药水/unknown、正常与异常价格 scope、实际 Exit history、
休息取消/成功/保留选项/空奖励保护和跨两次奖励租约。夹具 setup 可以注入状态，不作为自然游玩证据。

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
新增主 seed next-room 模式由独立旧 Differential runtime/同步 selector 直接驱动原版领奖和进图，
对照起始、战后、领奖后、下一房间的 RunDump、独立 NativeRunDump（含 RNG）加独立序列化的完整 MapPointHistory（含 CardChoices）及可见候选。
定向生产宿主夹具覆盖异步挂起、Reroll/Skip、未领 Leave、空奖励、满药水、取消/Dispose/late、
stale、replay、嵌套拒绝及原始 Offer 异常。夹具中的 EndCombatInternal 只建立战后边界，
不作为战斗胜利或自然游玩证据。
测试专用 Harmony 负向夹具验证实际 runner 遇 HP 差异/缺捕获不能给出 milestone，
并在原版首动作完成后取消，检查 selector/订阅释放及单进程单局限制。生产不包含测试环境变量后门。

不支持三幕通关、宝箱/Boss/换幕/嵌套战斗、复杂事件与任意嵌套奖励、批量评估、策略质量或全内容覆盖。
使用方式见 tools/run-eval/README.md；共享执行协议见 docs/autoplay.md。
