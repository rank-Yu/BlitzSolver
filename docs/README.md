# 架构与文档索引

主分支发布战斗求解 Mod；局外开发保存在 `dev/full-run`，不参与主分支构建。

## 分层

```text
Mod（生命周期、UI、动作执行与核对）
  ├── Import.Combat（只读游戏 → Battle / Rules / 搜索配置）
  └── Autoplay.CombatPilot（整场决策循环）
        ├── Ai.Combat（搜索与评价）
        └── Simulation.Combat（状态与结算）
Content ──→ Simulation.Combat（经注册表提供内容行为）
```

- `Simulation.Combat` 不引用 Content、Ai、Autoplay 或游戏。
- `Content` 与 `Ai.Combat` 只向下引用 `Simulation.Combat`，Ai 不直接引用 Content。
- `Autoplay` 引用 Simulation 与 Ai，通过宿主接口请求搜索和执行。
- Core 只依赖 BCL；Import 负责读取原生模型及私有字段。Mod 在游戏主线程捕获与执行，后台 worker 仅处理冻结状态。
- `TestHost/Combat` 提供场景创建和注入，供 Differential 与测试版 Mod 使用；`TestHost/ModTesting` 仅编入测试版 Mod。
- 正式 Mod 不引用 TestHost，不注册测试补丁。无头测试仍使用与正式版相同的搜索、执行和核对代码。

`Sts2Solver.sln` 只包含无需游戏文件的 Core、KernelChecks、Golden 和 Architecture。Import、Mod、TestHost、Differential 单独构建。游戏加载器需要单一 Mod DLL，因此 Mod 将 Core 与 Import 源码编入 `Sts2Solver.dll`；测试版才额外编入 TestHost。

## 设计约束

`Battle` 是定长 unmanaged 状态，分支通过整块复制产生；热路径不分配托管对象。容量不足或不支持的语义显式失败，不截断、不返回默认值。固定预算请求的编码与重放用于确定性回归；正常搜索有墙钟截止，不保证不同机器得到同一路线。

药水成本、永久成长、遗物计数与战后回复仍属于战斗路线评价。读取原版幕数、楼层或随机数也仍是战斗导入的必要部分，不需要我们自己的局外状态模型。

## 文档

| 文档 | 内容 |
| --- | --- |
| [Mod 使用与构建](mod.md) | 安装、触发、取消、发布和测试构建 |
| [战斗自动执行](autoplay.md) | CombatPilot、宿主与路线续用 |
| [战斗搜索](combat-ai.md) | 多轮搜索、评分、保留与预算 |
| [战斗状态布局](combat-state-layout.md) | Battle、容量与编码 |
| [战斗内核语义](combat-semantics.md) | 结算、Hook、选择与怪物行为 |
| [角色机制](combat-characters.md) | 五角色机制 |
| [药水与遗物](combat-potions-relics.md) | 对应战斗机制 |
| [原生导入](import.md) | Battle、规则与搜索配置的捕获 |
| [冻结基准格式](golden-data.md) | 基准来源、磁盘格式与更新规则 |
| [测试与验证](testing.md) | 合同、模拟回归、搜索回归与实机对照 |

文档只保存架构、接口、使用和测试方法。实验、跑分与任务流水放本地 `.local/`。
