# Flash 内核目录规则

本目录是 Flash 搜索后端的纯内核与内容定义，规格见 `docs/flash-search/`，工作流程见 `.agents/skills/flash-search/SKILL.md`。以下规则补充仓库根 AGENTS.md：

- 只依赖 BCL：不引用 MegaCrit、Godot、HarmonyLib，也不引用 `CombatSolver` 的其他命名空间。
- 语义以现有引擎为准；改动任何结算顺序或内容定义都要有与现有引擎的差分证据。
- 热路径不分配托管对象；内容定义没有字段状态，可变值只存在 `FlashBattle`。
- 原版数据中已有的数值从 `FlashRules` 读取，不硬编码。
- 溢出抛 `FlashCapacityException`，未知内容抛 `FlashUnsupportedException`；不截断、不跳过、不返回默认值，不写 `catch (Exception)`。
- 修改 `FlashBattle` 字段时同步 `docs/flash-search/state-layout.md` 与布局尺寸断言。
- `FlashHookMask` 的 64 位已用满：新 Hook 加在 `FlashHookMaskExt`，不复用、不重排已有位。
