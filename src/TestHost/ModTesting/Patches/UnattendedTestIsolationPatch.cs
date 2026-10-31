using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using STS2RitsuLib.Patching.Models;

namespace Sts2Solver;

/// <summary>
/// 无头测试不写入玩家的遭遇与击杀进度。
/// Adapted from CombatSolver/src/Testing/Host/UnattendedTestIsolationPatch.cs at a62f1850
/// (MIT, Copyright (c) 2026 Torch1230 and contributors).
/// </summary>
internal sealed class UnattendedTestIsolationPatch : IPatchMethod
{
    public static string PatchId => "sts2solver_unattended_test_isolation";

    public static string Description => "无头测试不写入玩家的遭遇与击杀进度";

    public static ModPatchTarget[] GetTargets() =>
    [
        new(
            typeof(SaveManager),
            nameof(SaveManager.UpdateProgressAfterCombatWon),
            [typeof(Player), typeof(CombatRoom)]),
    ];

    [HarmonyPriority(Priority.First)]
    public static bool Prefix()
    {
        if (!TestSession.Active)
            return true;
        ModLog.Info("[Sts2Solver] PROGRESS_WRITE_SKIPPED scope=combat_win_stats");
        return false;
    }
}
