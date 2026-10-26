# Simulation.Run

[整局设计](../../../../docs/full-run-design.md)第 4 节规定的整局模拟层。

职责：局外状态、地图、房间、奖励、商店、事件流程与局外随机数；进入战斗时建局交给
[`Simulation.Combat`](../Combat/)，打完写回。本目录在 R1a 只建立目录与说明，代码随
`RunState` 与局外模拟器的后续批次落地。
