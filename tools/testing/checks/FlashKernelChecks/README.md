# Flash 纯内核合同

直接编译 `src/Engine/InCombat/Flash/` 全部源码（含内容定义），不依赖游戏程序集或 CombatSolver 其他代码。

```bash
DOTNET_TieredCompilation=0 dotnet run --project tools/testing/checks/FlashKernelChecks -c Release
```

PowerShell 先设置 `$env:DOTNET_TieredCompilation = '0'`，再运行相同 dotnet 命令。

检查项：

- `FlashBattle`、`FlashCard`、`FlashEnemy`、`FlashPower`、`FlashRngState` 的字节尺寸，防止字段改动引入隐式填充。
- RNG：对照原版 `MegaRandom`/`Rng` 的参考副本，逐次比较 `NextInt`、`NextFloat`、`NextInt(min, max)` 与计数器。
- 洗牌排序：`FlashCardSort` 与 `List<T>.Sort()` 在相同比较键下产生同一置换（含相等键的元素）。
- 容量溢出抛 `FlashCapacityException`；牌堆段的抽顶、移除与复制语义。

输出 JSON 中的 `benchmark` 是纵切内容上的 Beam 形工作负载耗时（复制、执行、哈希分别计时）和测量循环的托管分配量，只作为内核性能的回归参考，不代表完整搜索耗时。

差分正确性不在本项目里检查，见 [Flash 测试记录](../../../../docs/flash-search/testing.md)。
