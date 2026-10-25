# BlitzSolver 极速战斗求解器

《杀戮尖塔 2》单人整场战斗路线搜索 Mod，开发中。

整场战斗状态是一个 12,696 字节的定长结构体 `BlitzBattle`，不含对象引用，分支只需整块复制内存。搜索在它上面做 Beam，热路径零托管分配。

## 当前状态

- 搜索核心（引擎、内容、搜索）已经独立成只依赖 .NET 基础库的 `BlitzSolver.Core`，覆盖五个角色卡池、296 个遗物、65 种药水与三幕全部遭遇。
- 冻结快照回归（`tests/Golden`）不需要游戏文件，就能证明内核与搜索的每一步都和导出时的原引擎结果一致。
- **还不能在游戏里运行。** 实机状态导入、动作部署与界面正在开发。

## 目录

| 目录 | 内容 |
| --- | --- |
| `src/Core/Engine` | 状态布局、回合与出牌管线、结算、规则表 |
| `src/Core/Content` | 卡牌、Power、遗物、药水、球、附魔与怪物定义 |
| `src/Core/Search` | `BlitzBeamSolver` 与状态评估；`Ported/` 是移植自 CombatSolver 的依赖 |
| `tests/KernelChecks` | 内核合同（K0） |
| `tests/Golden` | 冻结快照校验器；数据在 `tests/Golden/data` |
| `tests/corpora/quality` | 120 根质量语料（五个角色 × 三幕） |
| `docs` | 规格文档，从 [docs/README.md](docs/README.md) 进入 |

## 构建与验证

需要 .NET 9 SDK。

```bash
dotnet build BlitzSolver.sln -c Release
dotnet run --project tests/KernelChecks -c Release --no-build
dotnet run --project tests/Golden -c Release --no-build -- k0
dotnet run --project tests/Golden -c Release --no-build -- verify tests/Golden/data
```

`verify` 重放 418 个根、48 万个游走状态（约 12 秒），并对 1,983 个搜索请求重新求解（8 并行约 5 分钟）。`--only d1|d2`、`--shard i/n`、`--jobs N` 可以拆开运行。

## 来源与许可

搜索核心最初由 rank-Yu 作为 [CombatSolver](https://github.com/Torch1230/CombatSolver) 的可选后端 Flash 开发，提交历史随本仓库保留。

本仓库以 [MIT 许可证](LICENSE) 发布。部分依赖移植自 CombatSolver，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
