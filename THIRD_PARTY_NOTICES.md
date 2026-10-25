# 第三方声明 / Third-Party Notices

## CombatSolver

- 项目 / Project：CombatSolver
- 仓库 / Repository：https://github.com/Torch1230/CombatSolver
- 许可证 / License：MIT（原文见下）

BlitzSolver 与 CombatSolver 的来源关系：

- **翻译编写**：`src/Core/Engine`、`src/Core/Content` 与 `src/Core/Search`（`Ported/` 以外）最初由 rank-Yu 在 CombatSolver 的分支里作为可选搜索后端 Flash 编写，提交历史随本仓库保留。引擎与内容按 CombatSolver 模拟引擎、镜像与预测代码的方法划分和结算顺序翻译成定长结构体实现；搜索的状态评估按 CombatSolver 搜索的评估逐字段移植。它们在 CombatSolver 中与原引擎逐步差分校验。
- **直接移植**：`src/Core/Search/Ported/` 的文件从 CombatSolver（源提交 `a62f1850`）移植，只带过来 BlitzSolver 实际读取的成员；每个文件开头注明源文件路径与源提交。`tests/OfflineHost/` 从 CombatSolver 的 `tools/search/OfflineSearchHarness/`、`src/Testing/Host/`、`src/Testing/Support/` 与 `src/Testing/Contracts/Search/` 移植离线建局、注入与原生状态转储所需的子集；同样在文件头注明来源，未复制 `src/Engine`、`src/Prediction` 或搜索/回放/Showcase 代码。
- **导入层**：`src/Import/` 的 `BattleImporter`、`BattleProjections`、`UnsupportedContent` 与私有状态认领规则移植自 CombatSolver `src/Flash/Bridge`（`FlashStateImporter`、`FlashStateProjections`、`FlashUnsupportedContent`）；`RulesCompiler` 移植自 `src/Flash/Bridge/FlashRulesCompiler.cs`，其中卡牌克隆/升级、怪物状态机静态快照与召唤创建路径按原生模型和游戏 API 独立实现，语义参照 `src/Engine/Common/PredictionUtils`、`src/Prediction/BranchMonsterAi`、`MonsterValueReader` 与 `MonsterSpawnSupport`，未复制 `src/Engine`。`RulesCompiler.PostPrefix` 按同一编译器的 request-root/content-root 分工，从前缀前原生状态与回放后 `Battle` 重建规则表。`PrefixReplay` 按 CombatSolver `SearchReplayCore`/`PlanAction` 的固定前缀语义在 Blitz 内核上逐步执行动作与选择；`PrefixPlan` 按 `FlashDifferentialProbe` 的解析规则翻译 `flashPrefixActions`/`flashPrefixPlan`。`TurnSetupRootCapture`/`TurnSetupCardSelector` 参照 `tools/search/OfflineSearchHarness/TurnSetupRootCapture.cs`，在原生 `SetupPlayerTurn` 入口记录 Start 卡牌集合并按原生选项重建准备分支。`MirrorRiskTables` 是从 `CardOnPlayMirrors`/`AfterDeathMirrors` 一次性导出的静态数据，`RulesDiff` 为新增诊断。`NativeHistory` 独立实现，对照 `src/Search/RootCombatHistorySnapshot` 与 `SimulatedCombatState` 的只读 getter。`src/Core/Engine/BattleDump.cs` 从 `src/Flash/Bridge/FlashBattleDump.cs` 移植并增加偏移与字段差异。`ScoreCompiler`/`BoundCompiler` 移植 `src/Flash/Bridge/FlashScoreBridge.cs`、`FlashBoundBridge.cs` 的评分表与下界语义；`SearchPolicy`/`SearchPolicyCapture` 的字段与派生规则参照 `src/Search/SearchPolicySnapshot.cs`、`src/Runtime/SolverController.cs` 的 `CaptureSearchPolicy`，成长目标参照 `src/Search/GrowthOpportunityPolicy.cs`，评分/下界分类参照 `src/Search/StrategicEffectModel.cs`、`ActEndingBossPolicy.cs`、`PotionValuationRegistry.cs` 与 `StrategicHpRecoveryBound*`；`HealingBoundTables` 与 `ComponentInitialCardTypes` 是从 D1 `config.br`/`a62f1850` 一次性导出的静态分类结果。`tests/OfflineHost` 的 `ImportCompare`/`ImportNegative`/`RulesCompare`/`RulesTableCheck`/`ConfigCompare` 与对应模式为新增。
- **冻结快照**：`tests/Golden/data` 由 CombatSolver 中的导出工具生成，来源提交记录在 `manifest.json` 的 `provenance`。

How BlitzSolver relates to CombatSolver:

- **Translated**: `src/Core/Engine`, `src/Core/Content` and `src/Core/Search` (outside `Ported/`) were first written by rank-Yu as Flash, an optional search backend in a CombatSolver branch; their commit history is preserved here. The engine and content translate the method structure and resolution order of CombatSolver's simulation engine, mirror and prediction code into a fixed-size struct implementation, and the search ports CombatSolver's state evaluation field by field. They were differentially validated step by step against that engine.
- **Ported**: files in `src/Core/Search/Ported/` are copied from CombatSolver (source commit `a62f1850`), carrying only the members BlitzSolver reads; each file names its source path and commit in its header. `tests/OfflineHost/` ports the subset of CombatSolver's `tools/search/OfflineSearchHarness/`, `src/Testing/Host/`, `src/Testing/Support/` and `src/Testing/Contracts/Search/` needed for offline run creation, injection and native-state dumping; it does not copy `src/Engine`, `src/Prediction`, or search/replay/Showcase code. The import layer (`src/Import/`) ports `FlashStateImporter`/`FlashStateProjections`/`FlashUnsupportedContent` from `src/Flash/Bridge`; `RulesCompiler` ports `FlashRulesCompiler`, with card cloning/upgrade, the monster static snapshot and the spawn creation path independently implemented against native models and game APIs (semantics reference `src/Engine/Common/PredictionUtils`, `src/Prediction/BranchMonsterAi`, `MonsterValueReader` and `MonsterSpawnSupport`; `src/Engine` is not copied). `RulesCompiler.PostPrefix` follows the same compiler's request-root/content-root split to rebuild a rule table from the pre-prefix native state and the replayed Battle. `PrefixReplay` follows the fixed-prefix semantics of `SearchReplayCore`/`PlanAction` on the Blitz kernel; `PrefixPlan` translates `flashPrefixActions`/`flashPrefixPlan` with `FlashDifferentialProbe`'s grammar. `TurnSetupRootCapture`/`TurnSetupCardSelector` reference `tools/search/OfflineSearchHarness/TurnSetupRootCapture.cs`, capturing the Start card set at native `SetupPlayerTurn` and rebuilding one setup branch per native option. `MirrorRiskTables` is frozen data exported once from `CardOnPlayMirrors`/`AfterDeathMirrors`; `RulesDiff` is a new diagnostic. `NativeHistory` independently implements the read-only history semantics of `RootCombatHistorySnapshot` and the `SimulatedCombatState` getters without copying `src/Engine`. `BattleDump` ports `FlashBattleDump` with offsets and keyed differences. `ScoreCompiler`/`BoundCompiler` port the score-table and bound semantics of `src/Flash/Bridge/FlashScoreBridge.cs` and `FlashBoundBridge.cs`; `SearchPolicy`/`SearchPolicyCapture` reference `src/Search/SearchPolicySnapshot.cs` and `SolverController.CaptureSearchPolicy`, with growth targets from `GrowthOpportunityPolicy`, and classifications from `StrategicEffectModel`, `ActEndingBossPolicy`, `PotionValuationRegistry` and `StrategicHpRecoveryBound*`. `HealingBoundTables` and `ComponentInitialCardTypes` are frozen classification results derived from the D1 `config.br` files and `a62f1850`. `ConfigCompare` is a new offline mode.
- **Frozen snapshots**: `tests/Golden/data` was produced by export tooling in CombatSolver; the source commits are recorded in the `provenance` of `manifest.json`.

CombatSolver 许可证原文 / CombatSolver license:

```text
MIT License

Copyright (c) 2026 Torch1230 and contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Random Foreseer

- 项目 / Project：Random Foreseer
- 作者与版权人 / Author and copyright holder：hotwords123
- GitHub：https://github.com/hotwords123/StS2.RandomForeseer
- 创意工坊 / Steam Workshop：https://steamcommunity.com/sharedfiles/filedetails/?id=3747531952
- 许可证 / License：MIT（原文见下）

CombatSolver 的内置战斗模拟核心使用并改造了 Random Foreseer 的部分实现。BlitzSolver 的引擎与内容是对该模拟核心的翻译（见上），因此可能包含源自 Random Foreseer 的部分，这些部分按 Random Foreseer 的 MIT 许可证使用。BlitzSolver 不加载、也不分发 Random Foreseer 的程序集。

CombatSolver's built-in combat simulation core uses and modifies portions of Random Foreseer. BlitzSolver's engine and content translate that core (see above), so they may contain portions derived from Random Foreseer; those portions are used under Random Foreseer's MIT License. BlitzSolver neither loads nor distributes the Random Foreseer assembly.

Random Foreseer 许可证原文 / Random Foreseer license:

```text
MIT License

Copyright (c) 2026 hotwords123

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 分发 / Distribution

本文件与 `LICENSE` 必须随源码与每个二进制发布包一起提供。

This file and `LICENSE` must accompany the source and every binary release.
