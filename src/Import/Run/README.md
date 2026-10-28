# Import.Run

整局导入与内容导出。

- `RunStateImporter.cs`：原生 `RunState`/`Player` → Core `RunState`，只读；拒绝未知卡片、遗物、
  药水、事件与私有状态。R3b 起每一步都从原版重新导入。
- `ContentTableExporter.cs`：从本机游戏模型导出进程内 `ContentTable`，只保留 `RunCombatSetup`
  与 `RulesBuilder` 用到的内容：卡牌/等级、Power 变量、药水、遗物变量与开战变量、附魔、
  怪物的（槽位、进阶）规则组合、遭遇、角色卡池/药水池、未知点概率与开战 setup Hook 掩码。
  奖励池、商店价格、新局静态数据与对应 Hook 掩码已删除。
- `Decisions/`：R3b 新增 `RunDecisionImporter`，从原版当前界面读出 `RunDecisionRequest`。

内容表的范围与对照方法见 [内容表与 RulesBuilder](../../../docs/content-table.md)。
