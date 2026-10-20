# FlashBeam 目录规则

本目录是 Flash 搜索与衔接层：准入、规则编译、根导入、动作翻译、路线物化和 `FlashBeamSolver`。规格见 `docs/flash-search/`。以下规则补充仓库根 AGENTS.md：

- 只有 `Bridge/` 可以读取旧引擎内部状态和原版模型；`FlashBeamSolver` 只操作 `FlashBattle` 与 `FlashRules`。
- 本目录不引用 `BeamSolveDispatch`、`CombatSearchCoordinator`、`SolverSettings.Current`、UI 或 Runtime 单例；外部能力通过 `SearchPolicySnapshot` 与请求参数注入。
- 准入只判断不修改；拒绝必须给出稳定原因码。
- 只捕获 `FlashUnsupportedException`、`FlashCapacityException`、校验失败与取消。子状态级的 `FlashCapacityException` 在 `FlashBeamSolver.ExpandAction` 内作为搜索边界丢弃该分支并计数，回合结束层按容量压缩继续，不交出半成品路线；根导入/根评分抛出的容量、`FlashUnsupportedException`、校验失败与取消仍让整个 Flash 求解作废并回落。
- 交给协调器的结果必须由旧引擎回放物化，不能由 Flash 状态直接拼装 `SolverResult`。
