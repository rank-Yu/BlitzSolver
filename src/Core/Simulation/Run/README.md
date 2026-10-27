# Simulation.Run

[整局设计](../../../../docs/full-run-design.md)第 4 节规定的整局模拟层。R1b 已落地整局状态与
局外随机数；R2a-1 已落地进程内内容表与 `RulesBuilder`；R2a-2 已落地地图进点到首个出牌阶段的
战斗边界；R2b-2 已落地决策请求、`RunKernel` 与地图选点到非战斗房间的边界。奖励、商店、事件等
决策随 R2c–R2f 继续落地。

职责：局外状态、地图、房间、奖励、商店、事件流程与局外随机数；进入战斗时建局交给
[`Simulation.Combat`](../Combat/)，打完写回。

| 文件 | 内容 |
| --- | --- |
| `RunState.cs` | 定长、无引用的整局状态与容量边界 |
| `RunTypes.cs` / `RunLimits.cs` | 子结构、枚举与容量常量 |
| `RunCatalog.cs` | 角色/幕/遭遇/事件/epoch 的稳定 id 目录 |
| `RunRng.cs` | 与原版逐字节一致的 15 条流、事件/遭遇/地图派生 |
| `RunPropertyBag.cs` | 模型私有 `SavedProperties` 的规范编码 |
| `RunCodec.cs` | `RUNS` 魔数、版本与规范字节镜像；结构校验 |
| `RunView.cs` | 只读玩家可见投影；`Ai.Run` 唯一可见的整局类型组 |
| `ContentTable.cs` | 原生内容快照的进程内表，含遭遇的（怪物、槽位、进阶）规则组合 |
| `RulesBuilder.cs` | 从 `RunState`、`Battle` 与 `RulesRootFacts` 建 `Rules` |
| `RunDecisions.cs` | `RunDecisionRequest`/`RunDecisionOption`/`RunAction`/`RunStepResult` 与稳定拒绝码 |
| `RunTravel.cs` | 合法地图点：起点、普通子点、Boss、第二 Boss |
| `RunUnknownPoint.cs` | 未知点首次跑局保底、黑名单、浮点掷骰与概率成长；教学未知点 |
| `RunMapEntry.cs` | `RunKernel` 与 R2a-2 共用的进点前半段：房间类型、遭遇/事件、历史；非战斗房间的进入前边界 |
| `RunKernel.cs` | `Step(ref RunState, in RunAction)`：地图选点、拒绝、战斗/非战斗/结束结果 |
| `RunCombatSetup.cs` | `BuildCombat`：遭遇生成、开战与第一回合；兼容入口 `EnterMapCoord`/`EnterMapPoint` |

字段、容量、编码与可见性见[整局状态布局](../../../../docs/run-state-layout.md)；决策协议与进点见
[局外决策协议与地图进点](../../../../docs/run-decisions.md)；战斗边界见
[战斗边界](../../../../docs/run-combat-boundary.md)。
