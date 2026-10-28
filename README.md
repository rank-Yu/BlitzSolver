# BlitzSolver 极速求解器

《杀戮尖塔 2》单人自动游玩 Mod，开发中。战斗求解已经可用；目标是整局自动游玩，架构见 [整局设计](docs/full-run-design.md)。

整场战斗状态是一个 12,696 字节的定长结构体 `Battle`，不含对象引用，分支只需整块复制内存。搜索在它上面做 Beam，热路径零托管分配。

## 当前状态

- 搜索核心（引擎、内容、搜索）已经独立成只依赖 .NET 基础库的 `BlitzSolver.Core`，覆盖五个角色卡池、296 个遗物、65 种药水与三幕全部遭遇。
- 冻结快照回归（`tests/Golden`）不需要游戏文件，就能证明内核与搜索的每一步都和导出时的原引擎结果一致。
- Mod 已能在无头游戏中加载，并在可出牌时把 20 个普通根捕获、导入；`Battle`、`Rules`、
  评估配置与原生状态转储都与冻结快照/差分宿主逐字节相同。同样的根能在游戏里对捕获状态做
  一次单 Beam 搜索，确定性 `SREQ`/`SRES` 与离线组装/重解逐字节相同。
- 这 20 个固定根已在无头游戏里自动打完整场（16 胜 4 负）：路线部署到原版出牌/药水/结束
  回合/选牌入口，逐动作核对与每回合对账记录 0 偏差、0 部署失败。普通玩家用 `F8` 开关
  “本场全自动”；界面留给后续批次。

## 目录

| 目录 | 内容 |
| --- | --- |
| `src/Core/Simulation/Combat` | 状态布局、回合与出牌管线、结算、规则表 |
| `src/Core/Simulation/Run` | 局外状态、投影、编码、内容表与开战选择预测；局外由原版推进 |
| `src/Core/Content` | 卡牌、Power、遗物、药水、球、附魔与怪物定义 |
| `src/Core/Ai/Combat` | `BeamSolver`、状态评估与按功能归位的路线质量、资源策略、新颖度搜索 |
| `src/Core/Ai/Run` | 局外决策（R1a 只有目录说明） |
| `src/Core/Autoplay` | `CombatPilot` 整场战斗决策循环；`RunPilot` 随 R3b |
| `src/Import` | 原生导入；战斗导入在 `Combat/`，整局与决策请求在 `Run/` |
| `src/TestHost` | 测试共享建局与转储；战斗在 `Combat/`，整局在 `Run/` |
| `src/OfflineHost` | 离线宿主骨架（R3b 落地）；R3-0 只有 README |
| `src/Mod` | RitsuLib Mod 入口、战斗与测试代码；不加入 `BlitzSolver.sln` |
| `tests/KernelChecks` | 内核合同（K0）；战斗合同在 `Combat/` |
| `tests/Golden` | 冻结快照校验器；数据在 `tests/Golden/data`，战斗校验器在 `Combat/` |
| `tests/Architecture` | 编译后的 Core 层依赖检查 |
| `tests/Differential` | 进程内差分；战斗对照与离线整场在 `Combat/`，导入/建局/地图进点在 `Run/` |
| `tools/testing` | Linux 无头实例启动与请求/结果轮询 |
| `tools/run-eval` | 整局评估器骨架（R5）；R3-0 只有 README |
| `tests/corpora/quality` | 120 根质量语料（五个角色 × 三幕） |
| `docs` | 规格文档，从 [docs/README.md](docs/README.md) 进入 |

## 构建与验证

需要 .NET 9 SDK。

```bash
dotnet build BlitzSolver.sln -c Release
dotnet run --project tests/KernelChecks -c Release --no-build
dotnet run --project tests/Architecture -c Release --no-build
dotnet run --project tests/Golden -c Release --no-build -- k0
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data
```

`verify` 重放 418 个根、48 万个游走状态（约 12 秒），并对 1,983 个搜索请求重新求解（8 并行约 5 分钟）。`--only d1|d2`、`--shard i/n`、`--jobs N` 可以拆开运行。

## 来源与许可

本仓库参考了 [CombatSolver](https://github.com/Torch1230/CombatSolver) 与 [Random Foreseer](https://github.com/hotwords123/StS2.RandomForeseer)，部分代码移植自 CombatSolver（文件头注明出处）。

本仓库以 [MIT 许可证](LICENSE) 发布。两个上游项目也都是 MIT 许可，许可文本与本仓库的 `LICENSE` 相同，按其要求保留版权声明：

- CombatSolver：Copyright (c) 2026 Torch1230 and contributors
- Random Foreseer：Copyright (c) 2026 hotwords123
