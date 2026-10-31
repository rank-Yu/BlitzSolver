using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using STS2RitsuLib.Patching.Models;

namespace Sts2Solver;

/// <summary>
/// 无头测试不创建无窗口战斗基础教学界面。
/// Adapted from CombatSolver/src/Testing/Host/UnattendedHeadlessFtuePatch.cs at a62f1850
/// (MIT, Copyright (c) 2026 Torch1230 and contributors).
/// </summary>
internal sealed class UnattendedHeadlessFtuePatch : IPatchMethod
{
    public static string PatchId => "sts2solver_unattended_headless_ftue";

    public static string Description => "无头测试不创建无窗口战斗基础教学界面";

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(NCombatRulesFtue),
            nameof(NCombatRulesFtue.Create),
            Type.EmptyTypes),
    ];

    [HarmonyPriority(Priority.First)]
    public static bool Prefix(ref NCombatRulesFtue? __result)
    {
        if (!TestSession.Active)
            return true;
        __result = null;
        ModLog.Info("[Sts2Solver] FTUE_SKIPPED id=combat_rules_ftue reason=headless_ui");
        return false;
    }
}
