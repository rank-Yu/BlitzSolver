# Ai.Run

[整局设计](../../../../docs/full-run-design.md)第 4 节规定的局外决策层。

`IRunPolicy` 接收当前可见 `RunView`（含完整 RunDecisionRequest），异步返回
`RunPilotAnswer`（RunAction 与显式确认授权）。接口与答案都属于此层，Autoplay 调用策略，
策略无需反向依赖宿主层。Architecture 检查只允许登记的可见 Simulation.Run DTO，
不能读取 RunState、随机数状态或原版引用。规则、估值与正式策略仍待 R4。
