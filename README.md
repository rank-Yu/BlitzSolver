# STS2 Solver · 尖塔求解器

面向《杀戮尖塔 2》SL 模式的战斗与整局求解器，开发中。基于同一种子的确定性状态搜索，覆盖战斗搜索与局外决策；Mod 当前提供战斗自动执行，离线宿主已接通第一幕流程。架构见 [整局设计](docs/full-run-design.md)。

整场战斗状态是一个 12,696 字节的定长结构体 `Battle`，不含对象引用，分支只需整块复制内存。搜索在它上面做 Beam，热路径零托管分配。

## 当前状态

- 搜索核心（引擎、内容、搜索）已经独立成只依赖 .NET 基础库的 `Sts2Solver.Core`，覆盖五个角色卡池、296 个遗物、65 种药水与三幕全部遭遇。
- 冻结快照回归（`tests/Golden`）不需要游戏文件，就能证明内核与搜索的每一步都和导出时的原引擎结果一致。
- Mod 支持状态捕获、战斗搜索、路线部署及逐动作核对，使用原版出牌、药水、结束回合和选牌入口。普通玩家用 `F8` 开关“本场全自动”；设置界面尚未提供。
- 战斗使用组合搜索，依次搜索无新增用药、最多一次和按请求策略放开的成员组合；架构和约束见 [战斗搜索](docs/combat-ai.md)。
- 实验记录、跑分和验证报告保存在本地 `.local/`，不提交到仓库。

## 目录

| 目录 | 内容 |
| --- | --- |
| `src/Core/Simulation/Combat` | 状态布局、回合与出牌管线、结算、规则表 |
| `src/Core/Simulation/Run` | 局外状态、投影、编码、内容表与开战选择预测；局外由原版推进 |
| `src/Core/Content` | 卡牌、Power、遗物、药水、球、附魔与怪物定义 |
| `src/Core/Ai/Combat` | `BeamSolver`、状态评估与按功能归位的路线质量、资源策略、新颖度搜索 |
| `src/Core/Ai/Run` | 局外可见决策接口与策略 |
| `src/Core/Autoplay` | `CombatPilot` 整场战斗决策循环；`RunPilot` 整局边界与决策循环 |
| `src/Import` | 原生导入；战斗导入在 `Combat/`，整局与决策请求在 `Run/` |
| `src/TestHost` | 测试共享建局与转储；战斗在 `Combat/`，整局在 `Run/` |
| `src/OfflineHost` | 原版离线宿主：首战、下一房间、有界 Act1 多房间，事件选牌/商店/宝箱及 Boss 领奖后的换幕前停止 |
| `src/Mod` | RitsuLib Mod 入口、战斗与测试代码；不加入 `Sts2Solver.sln` |
| `tests/KernelChecks` | 内核合同（K0）；战斗合同在 `Combat/` |
| `tests/Golden` | 冻结快照校验器；数据在 `tests/Golden/data`，战斗校验器在 `Combat/` |
| `tests/Architecture` | 编译后的 Core 层依赖检查 |
| `tests/Differential` | 进程内差分；战斗对照与离线整场在 `Combat/`，导入/建局/地图进点在 `Run/` |
| `tools/testing` | Linux 无头实例启动与请求/结果轮询 |
| `tools/run-eval` | 独立首战/下一房间/有界多房间/第一幕烟测 CLI；批量整局评估待 R5 |
| `tests/corpora/quality` | 122 根质量语料（原 120 根＋两场固定女王回归） |
| `docs` | 架构、接口、使用与测试方法，从 [docs/README.md](docs/README.md) 进入 |

## 构建与验证

需要 .NET 9 SDK。

```bash
dotnet build Sts2Solver.sln -c Release
dotnet run --project tests/KernelChecks -c Release --no-build
dotnet run --project tests/Architecture -c Release --no-build
dotnet run --project tests/Golden -c Release --no-build -- k0
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data
```

`verify` 重放 418 个根、48 万个游走状态，并对 1,983 个搜索请求重新求解。`--only d1|d2`、`--shard i/n`、`--jobs N` 可以拆开运行。

## 来源与许可

本仓库参考了 [CombatSolver](https://github.com/Torch1230/CombatSolver) 与 [Random Foreseer](https://github.com/hotwords123/StS2.RandomForeseer)，部分代码移植自 CombatSolver（文件头注明出处）。

本仓库以 [MIT 许可证](LICENSE) 发布。两个上游项目也都是 MIT 许可，许可文本与本仓库的 `LICENSE` 相同，按其要求保留版权声明：

- CombatSolver：Copyright (c) 2026 Torch1230 and contributors
- Random Foreseer：Copyright (c) 2026 hotwords123
