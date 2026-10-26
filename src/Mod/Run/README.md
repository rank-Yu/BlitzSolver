# Mod.Run

[整局设计](../../../docs/full-run-design.md)第 4 节规定的整局宿主目录。

职责：整局自动游玩的游戏侧宿主；`Screens/` 每个界面一个执行器，`Rooms/` 处理各类房间。宿主只做
输入输出，决策与托管复用 Core 的 `Autoplay`。本目录在 R1a 只建立目录与说明，代码随 R3 落地。
