# STS2 Solver · 尖塔求解器

面向《杀戮尖塔 2》原版单人、同种子 SL 模式的战斗求解 Mod。读取当前战斗和真实随机数状态，搜索整场路线，通过原版入口自动出牌、用药并逐动作核对结果。

当前以战斗功能为发布范围，局外由玩家操作。局外开发代码保存在 `dev/full-run` 分支；完整版本的存档标签为 `archive/full-run-before-combat-release`。

## 使用

需要游戏 v0.111.0 及兼容的 RitsuLib（清单最低版本 0.6.0）。把发布包中的 `Sts2Solver/` 目录放入游戏的 `mods/` 目录，并单独安装 RitsuLib。

- 在自己的出牌阶段按 **F8**，开启本场战斗自动执行；再次按 F8 停止。
- 当前采用中档搜索，状态与错误写入游戏日志。设置界面尚未提供。
- 战斗结束自动停止，下一场需要重新开启。停止时已经入队的原版动作会完成。
- 检测到 CombatSolver 同时安装时，本 Mod 不启用，避免同时控制战斗。
- 预测与实际状态不符时会丢弃路线并重新搜索；连续失败后停止并交还手动。不支持的内容会明确拒绝。

安装、构建与测试版说明见 [Mod 使用与构建](docs/mod.md)。

## 架构

| 目录 | 职责 |
| --- | --- |
| `src/Core/Simulation/Combat` | 战斗状态（打包牌区）、规则、动作和回合结算 |
| `src/Core/Content` | 五角色卡牌、状态、怪物、遗物、药水、球与附魔效果 |
| `src/Core/Ai/Combat` | 搜索调度、评分、分支保留、去重与预算 |
| `src/Core/Autoplay` | `CombatPilot`：整场决策、路线续用与重搜 |
| `src/Import/Combat` | 只读导入游戏状态、编译规则与搜索配置 |
| `src/Mod` | 游戏入口、F8、后台搜索、动作执行与实际结果核对 |
| `src/TestHost/Combat` | 测试场景创建、注入、原生状态转储 |
| `src/TestHost/ModTesting` | 仅测试版 Mod 编入的实机测试入口与补丁 |
| `tests` | 合同、架构、冻结回归、原版差分与质量语料 |
| `tools/testing` | 本地验证与无头实机测试脚本 |

Core 只依赖 .NET 基础库。`Battle` 是不含对象引用的结构体；搜索池按“头部 + 实际牌数”的打包前缀保存状态，热路径只复制这份前缀，牌数不再固定为 96。正式发布包不包含局外模块、测试场景注入或测试补丁。完整依赖规则与文档索引见 [架构说明](docs/README.md)。

## 构建与验证

需要 .NET 9 SDK。无需游戏文件的完整验证：

```bash
tools/testing/verify-core.sh --jobs 8
```

也可分别执行：

```bash
dotnet build Sts2Solver.sln -c Release
dotnet run --project tests/KernelChecks -c Release --no-build
dotnet run --project tests/Architecture -c Release --no-build
dotnet run --project tests/Golden -c Release --no-build -- k0
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data --only simulation --jobs 8
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data --only search --jobs 8
```

模拟回归检查固定操作后的战斗状态；搜索回归检查固定请求的搜索结果。它们与基准一致不代表路线最优，搜索质量另由质量语料评估。实验记录与验证流水保存在本地 `.local/`，不提交。

## 来源与许可

本仓库参考了 [CombatSolver](https://github.com/Torch1230/CombatSolver) 与 [Random Foreseer](https://github.com/hotwords123/StS2.RandomForeseer)，部分代码移植自 CombatSolver（文件头注明出处）。

本仓库以 [MIT 许可证](LICENSE) 发布。两个上游项目也都是 MIT 许可，许可文本与本仓库的 `LICENSE` 相同，按其要求保留版权声明：

- CombatSolver：Copyright (c) 2026 Torch1230 and contributors
- Random Foreseer：Copyright (c) 2026 hotwords123
