// TestMode differences cross-checked against sts2 v0.111.0 MerchantPotionEntry.CalcCost
// and MerchantRoom.Exit. Both methods are synchronous; no await may run under this scope.
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.TestSupport;

namespace BlitzSolver.OfflineHost.Runtime;

internal static class MerchantRuntime
{
    internal static void Install(Harmony harmony)
    {
        foreach (MethodInfo method in new[] {
            AccessTools.Method(typeof(MerchantPotionEntry), nameof(MerchantPotionEntry.CalcCost)),
            AccessTools.Method(typeof(MerchantRoom), nameof(MerchantRoom.Exit)) })
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(MerchantRuntime), nameof(Begin)),
                finalizer: new HarmonyMethod(typeof(MerchantRuntime), nameof(End)));
    }
    private static void Begin(out bool __state)
    {
        __state = TestMode.IsOn;
        TestMode.IsOn = false;
    }
    private static void End(bool __state) => TestMode.IsOn = __state;
}
