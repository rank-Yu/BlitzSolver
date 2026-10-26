# Import.Run

[整局设计](../../../docs/full-run-design.md)第 4 节规定的整局导入目录。R1b 已落地
`RunStateImporter`。

职责：把原版 `RunState`、地图、房间与奖励投影成 Blitz 的局外状态；`Import` 仍是唯一允许读取
原版私有字段的层，只读，不改动游戏。

- `RunStateImporter.Import(nativeRunState, localPlayer)` 读取单人标准模式整局状态；未知/第三方/
  多人/非标准内容返回稳定 `ImportReason`，容量溢出抛 `CapacityException`。
- 字段盘点、拒绝码与对照结果见[整局状态布局](../../../docs/run-state-layout.md)。
- 独立原生转储在 `src/TestHost/Run/NativeRunDump.cs`；离线对照入口在
  `tests/OfflineHost/Run/`。
