using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Patching.Models;

namespace BlitzSolver;

/// <summary>
/// 无头测试不应用首次跑局的教学发现顺序。离线宿主用 <c>TestMode.IsOn = true</c> 让
/// <c>RunManager.ShouldApplyTutorialModifications()</c> 返回 false；实机测试模式不能全局打开
/// 游戏 TestMode（会改变 NRun/淡入淡出的 UI 路径），因此只在这一个判定点上复现离线分支。
/// </summary>
internal sealed class UnattendedNoDiscoveryOrderPatch : IPatchMethod
{
    public static string PatchId => "blitzsolver_unattended_no_discovery_order";

    public static string Description => "无头测试不覆盖第三幕 Boss 的发现顺序";

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(RunManager), nameof(RunManager.ShouldApplyTutorialModifications)),
    ];

    [HarmonyPriority(Priority.First)]
    public static bool Prefix(ref bool __result)
    {
        if (!TestSession.Active)
            return true;
        __result = false;
        return false;
    }
}
