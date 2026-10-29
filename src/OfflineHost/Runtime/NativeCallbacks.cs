// Callback semantics cross-checked against sts2 v0.111.0 CardSelectCmd and EventOption.
// Adapted from tests/Differential/Run/NativeDecisionTestSession.cs at 5890e705 (BlitzSolver, MIT).
using System.Reflection;
using BlitzSolver.Import;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rewards;

namespace BlitzSolver.OfflineHost.Run;

/// <summary>Installed once. Inactive callbacks only carry local context and never choose native options.</summary>
internal static class NativeCallbacks
{
    private static readonly object Gate = new();
    private static bool _installed;
    private static readonly AsyncLocal<Selection?> SelectionContext = new();
    private static readonly AsyncLocal<List<Task>?> EventTasks = new();
    internal sealed record Selection(Player Player, CardSelectorPrefs Prefs, NativeCardSelectionKind Kind)
    { internal int Consumed; }
    internal static Selection? CurrentSelection => SelectionContext.Value;
    internal static void Install()
    {
        lock (Gate)
        {
            if (_installed) return;
            Harmony harmony = new("BlitzSolver.OfflineHost.NativeCallbacks");
            foreach (string name in new[] { nameof(CardSelectCmd.FromDeckGeneric), nameof(CardSelectCmd.FromDeckForUpgrade), nameof(CardSelectCmd.FromSimpleGrid) })
                harmony.Patch(typeof(CardSelectCmd).GetMethods().Single(m => m.Name == name),
                    prefix: new HarmonyMethod(typeof(NativeCallbacks), nameof(SelectionPrefix)),
                    finalizer: new HarmonyMethod(typeof(NativeCallbacks), nameof(SelectionFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(EventOption), nameof(EventOption.Chosen)),
                postfix: new HarmonyMethod(typeof(NativeCallbacks), nameof(ChosenPostfix)));
            foreach (MethodInfo method in typeof(CardSelectCmd).GetMethods().Where(m => m.Name.StartsWith("From", StringComparison.Ordinal)
                && m.Name is not (nameof(CardSelectCmd.FromDeckGeneric) or nameof(CardSelectCmd.FromDeckForUpgrade) or nameof(CardSelectCmd.FromSimpleGrid))))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeCallbacks), nameof(RejectUnsupported)));
            harmony.Patch(AccessTools.Method(typeof(RelicSelectCmd), nameof(RelicSelectCmd.FromChooseARelicScreen)),
                prefix: new HarmonyMethod(typeof(NativeCallbacks), nameof(RejectUnsupported)));
            harmony.Patch(AccessTools.Method(typeof(RewardsSet), nameof(RewardsSet.Offer)),
                prefix: new HarmonyMethod(typeof(NativeCallbacks), nameof(RewardOfferPrefix)),
                postfix: new HarmonyMethod(typeof(NativeCallbacks), nameof(RewardOfferPostfix)));
            RewardCardScreen.Install(harmony);
            _installed = true;
        }
    }
    private static void RewardOfferPrefix(RewardsSet __instance) => RewardPresentation.CheckOffer(__instance);
    private static void RewardOfferPostfix(RewardsSet __instance, Task __result) => RewardPresentation.TrackOffer(__instance, __result);
    private static void RejectUnsupported(MethodBase __originalMethod)
    {
        if (OfflineRunHost.IsExecuting)
            throw new BlitzSolver.Simulation.Combat.UnsupportedException("host-unsupported-native-entry:" + __originalMethod.DeclaringType!.Name + "." + __originalMethod.Name);
    }
    private static void SelectionPrefix(Player player, CardSelectorPrefs prefs, MethodBase __originalMethod, out Selection? __state)
    {
        __state = SelectionContext.Value;
        SelectionContext.Value = new(player, prefs, __originalMethod.Name switch
        {
            nameof(CardSelectCmd.FromDeckGeneric) => NativeCardSelectionKind.DeckGeneric,
            nameof(CardSelectCmd.FromDeckForUpgrade) => NativeCardSelectionKind.DeckUpgrade,
            _ => NativeCardSelectionKind.SimpleGrid,
        });
    }
    private static void SelectionFinalizer(Selection? __state) => SelectionContext.Value = __state;
    private static void ChosenPostfix(Task __result) => EventTasks.Value?.Add(__result);
    internal static Task ChooseEvent(EventModel model, EventOption option)
    {
        if (option.IsProceed) return option.Chosen();
        if (EventTasks.Value != null) throw new InvalidDataException("host-event-reentrancy");
        int index = -1;
        for (int i = 0; i < model.CurrentOptions.Count; i++) if (ReferenceEquals(model.CurrentOptions[i], option)) index = i;
        if (index < 0) throw new InvalidDataException("host-event-instance");
        List<Task> tasks = []; EventTasks.Value = tasks;
        try { RunManager.Instance.EventSynchronizer.ChooseLocalOption(index); }
        finally { EventTasks.Value = null; }
        if (tasks.Count != 1) throw new InvalidDataException("host-event-task-count");
        return tasks[0]; // Raw task preserves native failure; synchronizer safe wrappers may swallow it.
    }
}
