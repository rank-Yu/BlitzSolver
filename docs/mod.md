# Mod 使用与构建

## 安装与使用

将构建或发布包里的 `Sts2Solver/` 目录放入游戏 `mods/` 目录。包包含 `Sts2Solver.dll`、`Sts2Solver.json`、`LICENSE` 与 `README.md`，不包含游戏文件或 RitsuLib；RitsuLib 由玩家单独安装。

当前适配游戏 v0.111.0，清单要求 RitsuLib 至少 0.6.0；使用兼容版本。当前没有设置界面，搜索固定采用中档：

- 自己的出牌阶段按 F8：开始本场全自动。
- 再按 F8：取消搜索与后续自动操作，已入队动作会完成。
- 战斗结束、退出场景或连续预测偏差后停止，下一场需重新开启。
- 状态和错误写入游戏日志的 `[Sts2Solver]` 条目。

检测到 CombatSolver 已加载或等待加载时，本 Mod 直接禁用，避免两个求解器同时控制战斗。第三方内容不在当前支持范围内；不支持的导入会明确拒绝。

## 两种构建

游戏与 RitsuLib 路径配置在忽略提交的 `src/Mod/local.props`，模板为 `local.props.example`。Import 和测试宿主的游戏路径来自 `tests/Differential/local.props`。

```bash
# 正式构建：只有战斗功能
dotnet build src/Mod -c Release

# 测试构建：额外包含场景注入、捕获与无头测试补丁
dotnet build src/Mod -c Release -p:EnableModTesting=true
```

| 项目 | 正式版 | 测试版 |
| --- | --- | --- |
| 打包目录 | `artifacts/mod/Sts2Solver` | `artifacts/mod-testing/Sts2Solver` |
| 中间文件与程序集 | `artifacts/obj`、`artifacts/bin` | `artifacts/testing/obj`、`artifacts/testing/bin` |
| Core、Import、战斗执行 | 相同源码 | 相同源码 |
| TestHost、测试入口与补丁 | 不编入 | 编入 |
| `STS2_SOLVER_TEST_DIR` | 不读取 | 设置后启动测试循环 |

两种构建使用相同 Mod ID，不同时安装。构建只写仓库的 artifacts，不写玩家游戏目录。

游戏加载器只加载与清单 ID 同名的 DLL，因此 Core 与 Import 的源码直接编入 `Sts2Solver.dll`，不附带其他求解器 DLL。测试版额外编入 `TestHost/Combat` 和 `TestHost/ModTesting`。测试版入口只在显式测试模式下注册测试补丁；正式版没有测试补丁目录或补丁注册逻辑。

验证包的编译边界，不需要加载游戏：

```bash
dotnet run --project tests/Architecture -c Release --no-build -- --mod artifacts/mod/Sts2Solver/Sts2Solver.dll
dotnet run --project tests/Architecture -c Release --no-build -- --testing-mod artifacts/mod-testing/Sts2Solver/Sts2Solver.dll
```

## 生命周期与线程

入口订阅 RitsuLib 的战斗开始/结束事件并挂接 F8 节点。搜索和部署分三段：

1. 主线程读取原版状态，由 Import 编译 Battle、Rules、评分和搜索配置。
2. 后台 worker 只访问冻结数据，运行组合调度里的搜索成员。普通模式会按游戏帧压力让出工作时间，确定性测试不附加让帧器。
3. 结果返回主线程，经内核重放验证路线后，由游戏动作入口执行并核对。

`CombatPilot` 管理整场搜索、部署、路线续用和失败暂停，详见 [战斗自动执行](autoplay.md)。每个搜索成员的时限按宽度计算，详见 [战斗搜索](combat-ai.md)。

搜索内存预算默认取可用内存的 15%，下限 1 GiB。环境变量 `STS2_SOLVER_SEARCH_MEMORY_MB` 可按 MiB 覆盖。它约束搜索自己的逻辑存储，不等于整个游戏进程的 RSS 上限；非法配置记录日志并回退默认值。

## 原版执行与核对

出牌、药水与结束回合通过原版动作队列执行，选牌通过原版 `ICardSelector` 接口回答。部署前核对回合、能量、牌身份、目标与可用性，选择数量和选项必须与实时请求匹配；不猜测选项。

每个动作完成后等待稳定边界，再通过 `LiveCombatTracker` 导入 Battle。比较覆盖 HP、护甲、能量、牌堆顺序、状态、遗物、药水、随机数、计数和结局。仅归一化原生不可观测或已不可达的字段，具体规则由 `BattleReconciler` 实现；不为消除偏差放宽容差。

一致则继续；不一致时清空剩余路线，从实际状态重新搜索。新回合与预测一致且有剩余动作时直接续用。同一回合连续三次部署失败或偏差后停止并交还手动。搜索失败、空路线或无效重放不会部署半成品。

F8、战斗结束和场景退出会取消后台搜索与自动执行；测试清理还等待它们完全退出才返回主菜单。

## 无头实机测试

先构建测试版 Mod 和 Differential。脚本默认使用 `artifacts/mod-testing/Sts2Solver`，复制到独立实例，源游戏目录不变。

```bash
dotnet build tests/Differential -c Release
dotnet build src/Mod -c Release -p:EnableModTesting=true
tools/testing/run-headless.sh --instance combat-smoke --root ROOT_NAME \
  --full-combat --results .local/combat-smoke --cleanup-instance-on-exit
```

测试模式读取请求 JSON，创建场景，捕获根，按请求执行单次搜索或整场战斗。测试专用补丁仅处理进度隔离、无头教程、Discovery 展示顺序和回合准备快照。请求失败会写明原因，清理后再处理下一请求。

确定性捕获写出 `BATL`、`RULE`、`SREQ`、`SRES` 等，供 Differential 的 `--live-compare` 和 `--search-compare` 核对。正常预算搜索是性能与执行测试，不保证与不同机器上的墙钟截断结果逐字节一致。

此版本尚未提供设置面板、路线展示、Steam Workshop 上传或第三方 Mod 兼容层。
