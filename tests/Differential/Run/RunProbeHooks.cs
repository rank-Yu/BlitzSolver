// Probe-only runtime observation and UI bypasses, following sts2 v0.111.0 public model entries.
using System.Reflection;
using Sts2Solver.Import;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace Differential;

internal static class RunProbeHooks
{
    internal static RunProbe? Active;
    internal static readonly List<Task> Pending = [];

    internal static void Install()
    {
        Patch(AccessTools.PropertyGetter(typeof(MegaCrit.Sts2.Core.Models.EnchantmentModel), "HoverTips"), prefix: nameof(NoHoverTips));
        Patch(AccessTools.PropertyGetter(typeof(MegaCrit.Sts2.Core.Models.RelicModel), "HoverTipsExcludingRelic"), prefix: nameof(NoHoverTips));

        foreach (MethodInfo method in typeof(UserDataPathProvider).GetMethods().Where(m => m.Name is "GetProfileDir" or "GetProfileScopedBasePath"))
            Patch(method, prefix: nameof(ProfilePath));
        Patch(AccessTools.Method(typeof(RunHistorySaveManager), nameof(RunHistorySaveManager.GetHistoryPath)), prefix: nameof(HistoryPath));
        Patch(AccessTools.Method(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseABundleScreen)), prefix: nameof(Bundle));
        GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Models.Events.Trial), "Accept"),
            transpiler: new HarmonyMethod(typeof(RunProbeHooks), nameof(TrialUi)));
        Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Models.Events.Trial), "AddVfxAnchoredToPortrait"), prefix: nameof(SkipUi));
        Patch(AccessTools.Method(typeof(RelicSelectCmd), nameof(RelicSelectCmd.FromChooseARelicScreen)), prefix: nameof(RejectRelic));
        Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Models.EventModel), "BeginEvent"), postfix: nameof(CaptureTask));
        foreach (string name in new[] { "LoadRoomEventAssets", "LoadRoomRestSite", "LoadRoomTreasureAssets", "LoadRoomMerchantAssets" })
            foreach (MethodInfo method in typeof(PreloadManager).GetMethods().Where(m => m.Name == name))
                Patch(method, prefix: nameof(Completed));
        Patch(AccessTools.Method(typeof(RewardsSet), nameof(RewardsSet.Offer)), postfix: nameof(CaptureTask));
        Patch(AccessTools.Method(typeof(RunManager), nameof(RunManager.OnEnded)), postfix: nameof(Ended));
        Patch(AccessTools.Method(typeof(EventOption), nameof(EventOption.Chosen)), postfix: nameof(CaptureTask));
        Patch(AccessTools.Method(typeof(MerchantPotionEntry), nameof(MerchantPotionEntry.CalcCost)),
            prefix: nameof(PriceBegin), finalizer: nameof(PriceEnd));
        Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Runs.RunManager), "ShouldApplyTutorialModifications"),
            prefix: nameof(NoTutorial));
        foreach (MethodInfo method in typeof(CardSelectCmd).GetMethods().Where(m =>
                     m.GetParameters().Any(p => p.ParameterType == typeof(CardSelectorPrefs))))
            Patch(method, prefix: nameof(ObservePrefs));
        MethodInfo[] imports = [.. typeof(LiveCombatTracker).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.Name is "ImportRoot" or "ImportForComparison")];
        if (imports.Length != 2) throw new InvalidDataException($"战斗导入计时入口改变：{imports.Length}");
        foreach (MethodInfo method in imports)
            Patch(method, prefix: nameof(ImportBegin), finalizer: nameof(ImportEnd));
    }

    private static void Patch(MethodBase method, string? prefix = null, string? postfix = null, string? finalizer = null)
    {
        HarmonyMethod? Hook(string? name) => name == null ? null : new(typeof(RunProbeHooks), name);
        GameBootstrap.Harmony.Patch(method, Hook(prefix), Hook(postfix), finalizer: Hook(finalizer));
    }

    // Trial.Accept has two unguarded UI blocks under IsMe, followed by native RNG/page logic.
    // Only those UI conditions are replaced. No event outcome is reimplemented.
    private static IEnumerable<CodeInstruction> TrialUi(IEnumerable<CodeInstruction> instructions)
    {
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(MegaCrit.Sts2.Core.Context.LocalContext)
                && method.Name == "IsMe" && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(MegaCrit.Sts2.Core.Entities.Players.Player))
            {
                instruction.operand = AccessTools.Method(typeof(RunProbeHooks), nameof(NoLocalUi));
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 2) throw new InvalidDataException($"Trial.Accept UI pattern changed: {replaced}");
    }
    private static bool NoLocalUi(MegaCrit.Sts2.Core.Entities.Players.Player player) => false;
    private static bool SkipUi() => false;
    private static bool ProfilePath(int profileId, ref string __result) { __result = $"profile{profileId}"; return false; }
    private static bool HistoryPath(int profileId, ref string __result) { __result = $"profile{profileId}/saves/history"; return false; }
    private static void Bundle(IReadOnlyList<IReadOnlyList<MegaCrit.Sts2.Core.Models.CardModel>> bundles) => Active?.ObserveBundle(bundles);
    private static void RejectRelic() => throw new UnsupportedException("RelicSelectCmd尚无离线UI选择器，禁止进入Godot屏幕");
    private static bool NoHoverTips(ref IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> __result) { __result = []; return false; }

    private static bool Completed(ref Task __result) { __result = Task.CompletedTask; return false; }
    private static bool NoTutorial(ref bool __result) { __result = false; return false; }
    private static void Ended(bool isVictory) => Active?.ObserveEnded(isVictory);
    private static void CaptureTask(Task __result) => Pending.Add(__result);
    private static void PriceBegin(out bool __state) { __state = TestMode.IsOn; TestMode.IsOn = false; }
    private static void PriceEnd(bool __state) => TestMode.IsOn = __state;
    private static void ImportBegin(out IDisposable? __state) => __state = Active?.Telemetry.Measure("import_check");
    private static void ImportEnd(IDisposable? __state) => __state?.Dispose();
    private static void ObservePrefs(object[] __args)
    {
        foreach (object arg in __args)
            if (arg is CardSelectorPrefs prefs) Active?.ObservePrefs(prefs);
    }
}
