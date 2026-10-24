# FlashGolden：纯核心 Flash 期望值校验器

`FlashGolden` 在**只编译** `src/Flash/Kernel/**` 与 `src/Flash/Content/**` 的进程里校验
`.local/blitz-m0/golden` 的 D1 数据，不需要 `CombatSolver.csproj`、游戏 DLL、Godot、Harmony
或 RitsuLib。它是新仓库 BlitzSolver 回归入口的前身：证明冻结的 Flash 根、K1 动作与 K2 游走
只靠 Flash 源码就能逐字节复现。

```bash
dotnet run --project tools/search/FlashGolden -c Release -- k0
dotnet run --project tools/search/FlashGolden -c Release -- verify .local/blitz-m0/golden
```

- `k0`：运行 `FlashRulesCodecContract`，检查 `FlashRules` 每个构造字段都进入编码字节，并打印
  字段数与 `Unsafe.SizeOf<FlashBattle>()`。
- `verify`：读取 `golden/d1/*` 与 `objects/rules/*`，重放每条 K1 补全与每条 K2 游走；逐步比较
  状态哈希、合法动作集合、动作选择、选择请求（选项、顺序、上下限、效果、时机）与结束标志。
  不一致时以非零退出，并打印第一处差异（根、游走、步、期望与实际）；通过时输出
  `FLASH_GOLDEN_VERIFY ... mismatched=0`。

数据格式、计数和 Search 外部类型清单见任务报告 `.local/tool-tasks/flash-search/M0-report.md`。
