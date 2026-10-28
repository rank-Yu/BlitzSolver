# Simulation.Run

[整局设计](../../../../docs/full-run-design.md)第 4 节规定的局外状态层。2026-10-27 起局外由原版
`sts2.dll` 推进，这里不再包含局外模拟器；只保留状态、投影、导入所需的表示，以及从进点前
`RunState` 预测开战选项的 `RunCombatSetup`。

职责：

- `RunState` 的定长表示、编码、校验与转储；
- 玩家可见投影 `RunView` 与决策请求/动作类型 `RunDecisionRequest`/`RunAction`；
- 从进点前状态预测每个开战选项对应的 `Battle`/`Rules`/开局 `RunState`；
- 进程内内容表 `ContentTable` 与 `RulesBuilder`，供 `RunCombatSetup`/`RulesBuilder` 使用。

| 文件 | 内容 |
| --- | --- |
| `RunState.cs` | 定长、无引用的整局状态与容量边界 |
| `RunTypes.cs` / `RunLimits.cs` | 子结构、枚举与容量常量 |
| `RunCatalog.cs` | 角色/幕/遭遇/事件/epoch 的稳定 id 目录 |
| `RunRng.cs` | 与原版逐字节一致的 15 条流、事件/遭遇/地图派生 |
| `RunPropertyBag.cs` | 模型私有 `SavedProperties` 的规范编码 |
| `RunCodec.cs` | `RUNS` 魔数、版本与规范字节镜像；结构校验 |
| `RunDump.cs` | 行式状态转储，供对照使用 |
| `RunView.cs` | 只读玩家可见投影；`RunView.Request` 留给 R3b 的 `RunDecisionImporter` 填充 |
| `RunDecisions.cs` | `RunDecisionRequest`/`RunDecisionOption`/`RunAction`/`RunActionKind` 与进点拒绝码 |
| `RunTravel.cs` | 合法地图点：起点、普通子点、Boss、第二 Boss |
| `RunUnknownPoint.cs` | 未知点首次跑局保底、黑名单、浮点掷骰与概率成长；教学未知点 |
| `RunMapEntry.cs` | 进点前半段：房间类型、遭遇/事件、历史；战斗房间继续建局，非战斗房间停在进入前边界 |
| `RunContent.cs` | 地图进点所需的初始未知点概率与开战 setup Hook 掩码 |
| `ContentTable.cs` | 原生内容快照的进程内表，含遭遇的（怪物、槽位、进阶）规则组合与开战池 |
| `RulesBuilder.cs` | 从 `RunState`、`Battle` 与 `RulesRootFacts` 建 `Rules` |
| `RunCombatSetup.cs` | `TryEnterMapCoord`/`EnterMapCoord`：遭遇生成、开战与第一回合；开战选择预测的唯一入口 |

已删除（2026-10-27，R3-0）：`RunGenerator`、`RunMapGenerator`、`RunRandom`、`RunRewards`、
`RunShop`、`RunKernel`、`RunCombatWriteback`。最后一个包含完整局外模拟器的提交是 `683bc03`；
以后需要时从 Git 历史恢复，不在当前设计里维护第二套局外推进。

字段、容量、编码与可见性见[整局状态布局](../../../../docs/run-state-layout.md)；决策协议与进点见
[局外决策协议与地图进点](../../../../docs/run-decisions.md)；战斗边界见
[战斗边界](../../../../docs/run-combat-boundary.md)；离线宿主见
[离线宿主](../../../../docs/offline-host.md)。
