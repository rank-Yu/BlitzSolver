# Ai.Run

[整局设计](../../../../docs/full-run-design.md)第 4 节规定的局外决策层。

职责：只读 `RunView` 与 `RunDecisionRequest`，产出 `RunAction`/选项选择；不引用局外状态本体
或随机数类型。需要推演评估时通过 `Autoplay` 注入的接口调用。本目录在 R1a 只建立目录与说明，
规则与估值随 R4 落地；R2b-2 已把地图决策请求与动作类型登记为可见类型。
