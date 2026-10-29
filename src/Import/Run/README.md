# Import.Run

整局导入与内容导出。

- `RunStateImporter.cs`：原生 `RunState`/`Player` → Core `RunState`，只读；拒绝未知卡片、遗物、
  药水、事件与私有状态。R3b 起每一步都从原版重新导入。
- `ContentTableExporter.cs`：从本机游戏模型导出进程内 `ContentTable`，只保留 `RunCombatSetup`
  与 `RulesBuilder` 用到的内容：卡牌/等级、Power 变量、药水、遗物变量与开战变量、附魔、
  怪物的（槽位、进阶）规则组合、遭遇、角色卡池/药水池、未知点概率与开战 setup Hook 掩码。
  奖励池、商店价格、新局静态数据与对应 Hook 掩码已删除。
- `Decisions/`：`RunDecisionImporter` 从原版真实待选模型导入 MapPoint、Event、RestSite、
  CardSelect。`NativeDecisionSnapshot<T>` 按值提供纯 Request，封装只读原实例绑定；
  `Resolve` 完整校验回答后返回有序原实例，`ValidateCurrent` 重核来源、当前候选身份与可见负载。
  Import 不执行游戏、不生成页面/选项、不管理 Token 生命周期，不引用 TestHost。
  地图要求宿主实时提供实际待选入口是否启用；选牌要求真实 pending 回调的完整 prefs 和
  原命令种类，自动选择不制造请求。事件 typed 事实首批仅 ThisOrThat.PLAIN 与
  FieldOfManSizedHoles.RESIST；未知效果保持 Unknown。奖励、商店与其他选择类型尚未接入。
  正式宿主执行仍待落地，Differential 中的生命周期与回调桥接仅用于测试。
  接口、取消/确认、过期与覆盖限制见[局外决策](../../../docs/run-decisions.md#7-原版首批导入与绑定)。

内容表的范围与对照方法见 [内容表与 RulesBuilder](../../../docs/content-table.md)。
