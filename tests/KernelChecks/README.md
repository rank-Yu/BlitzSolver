# Blitz 纯内核合同

通过 `ProjectReference` 引用编译后的 `BlitzSolver.Core`（源码为 `src/Core/Simulation/Combat/`、`src/Core/Ai/Combat/` 与 `src/Core/Content/`），不依赖游戏程序集或 BlitzSolver 其他代码。

```bash
DOTNET_TieredCompilation=0 dotnet run --project tests/KernelChecks -c Release
```

PowerShell 先设置 `$env:DOTNET_TieredCompilation = '0'`，再运行相同 dotnet 命令。

检查项：

- `Battle`、`Card`、`Enemy`、`Power`、`RngState` 的字节尺寸，防止字段改动引入隐式填充。
- RNG：对照原版 `MegaRandom`/`Rng` 的参考副本，逐次比较 `NextInt`、`NextFloat`、`NextInt(min, max)` 与计数器。
- 洗牌排序：`CardSort` 与 `List<T>.Sort()` 在相同比较键下产生同一置换（含相等键的元素）。
- 容量溢出抛 `CapacityException`；牌堆段的抽顶、移除与复制语义。

输出 JSON 中的 `benchmark` 是纵切内容上的 Beam 形工作负载耗时（复制、执行、哈希分别计时）和测量循环的托管分配量，只作为内核性能的回归参考，不代表完整搜索耗时。

差分正确性不在本项目里检查，见 [测试与验证](../../docs/testing.md)。
