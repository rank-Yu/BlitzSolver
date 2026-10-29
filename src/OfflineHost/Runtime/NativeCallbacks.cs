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
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace BlitzSolver.OfflineHost.Run;

/// <summary>Installed once. Inactive callbacks only carry local context and never choose native options.</summary>
internal static class NativeCallbacks
{
    private static readonly object Gate = new();
    private static bool _installed;
    private static readonly AsyncLocal<Selection?> SelectionContext = new();
    private static readonly AsyncLocal<List<Task>?> EventTasks = new();
    internal sealed record Selection(Player? Player, CardSelectorPrefs Prefs, NativeCardSelectionKind Kind,
        int CallbackMin, int CallbackMax, EnchantmentModel? Enchantment = null, int Amount = 0,
        Func<CardModel, bool>? Filter = null)
    { internal int Consumed; }
    private static readonly AsyncLocal<Action<GameAction>?> EnqueueObserver = new();
    private static void Enqueued(GameAction action) => EnqueueObserver.Value?.Invoke(action);
    internal static PickRelicAction PickTreasure(int? index)
    {
        PickRelicAction? captured = null;
        if (EnqueueObserver.Value != null) throw new InvalidDataException("host-treasure-queue-reentrancy");
        EnqueueObserver.Value = action =>
        {
            if (action is not PickRelicAction pick || captured != null)
                throw new InvalidDataException("host-treasure-unrelated-action");
            captured = pick;
        };
        try
        {
            if (index.HasValue) RunManager.Instance.TreasureRoomRelicSynchronizer.PickRelicLocally(index);
            else RunManager.Instance.TreasureRoomRelicSynchronizer.SkipRelicLocally();
        }
        finally { EnqueueObserver.Value = null; }
        return captured ?? throw new InvalidDataException("host-treasure-pick-not-enqueued");
    }
    internal static Selection? CurrentSelection => SelectionContext.Value;
    internal static void Install()
    {
        lock (Gate)
        {
            if (_installed) return;
            Harmony harmony = new("BlitzSolver.OfflineHost.NativeCallbacks");
            string[] supported = [nameof(CardSelectCmd.FromDeckGeneric), nameof(CardSelectCmd.FromDeckForUpgrade),
                nameof(CardSelectCmd.FromSimpleGrid), nameof(CardSelectCmd.FromChooseACardScreen),
                nameof(CardSelectCmd.FromDeckForTransformation), nameof(CardSelectCmd.FromDeckForEnchantment),
                nameof(CardSelectCmd.FromDeckForRemoval)];
            foreach (MethodInfo method in typeof(CardSelectCmd).GetMethods().Where(m => supported.Contains(m.Name)))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeCallbacks), nameof(SelectionPrefix)),
                    finalizer: new HarmonyMethod(typeof(NativeCallbacks), nameof(SelectionFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue)),
                prefix: new HarmonyMethod(typeof(NativeCallbacks), nameof(Enqueued)));
            harmony.Patch(AccessTools.Method(typeof(EventOption), nameof(EventOption.Chosen)),
                postfix: new HarmonyMethod(typeof(NativeCallbacks), nameof(ChosenPostfix)));
            foreach (MethodInfo method in typeof(CardSelectCmd).GetMethods().Where(m => m.Name.StartsWith("From", StringComparison.Ordinal)
                && !supported.Contains(m.Name)))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeCallbacks), nameof(RejectUnsupported)));
            harmony.Patch(AccessTools.Method(typeof(RelicSelectCmd), nameof(RelicSelectCmd.FromChooseARelicScreen)),
                prefix: new HarmonyMethod(typeof(NativeCallbacks), nameof(RejectUnsupported)));
            harmony.Patch(AccessTools.Method(typeof(RewardsSet), nameof(RewardsSet.Offer)),
                prefix: new HarmonyMethod(typeof(NativeCallbacks), nameof(RewardOfferPrefix)),
                postfix: new HarmonyMethod(typeof(NativeCallbacks), nameof(RewardOfferPostfix)));
            harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.RewardsSetSynchronizer), "BeginRewardsSet"),
                prefix: new HarmonyMethod(typeof(NativeCallbacks), nameof(GeneratedRewardsPrefix)));
            RewardCardScreen.Install(harmony);
            _installed = true;
        }
    }
    private static void GeneratedRewardsPrefix(RewardsSet set) => RewardPresentation.CheckGenerated(set);
    private static void RewardOfferPrefix(RewardsSet __instance) => RewardPresentation.CheckOffer(__instance);
    private static void RewardOfferPostfix(RewardsSet __instance, Task __result) => RewardPresentation.TrackOffer(__instance, __result);
    private static void RejectUnsupported(MethodBase __originalMethod)
    {
        if (OfflineRunHost.IsShopRemoval && __originalMethod.DeclaringType == typeof(CardSelectCmd)
            && __originalMethod.Name == nameof(CardSelectCmd.FromDeckForRemoval)) return;
        if (OfflineRunHost.HasExecutionContext)
            throw new BlitzSolver.Simulation.Combat.UnsupportedException("host-unsupported-native-entry:" + __originalMethod.DeclaringType!.Name + "." + __originalMethod.Name);
    }
    private static void SelectionPrefix(object[] __args, MethodBase __originalMethod, out Selection? __state)
    {
        __state = SelectionContext.Value;
        if (OfflineRunHost.HasExecutionContext && !OfflineRunHost.IsExecuting)
            throw new InvalidDataException("host-card-execution-closed");
        ParameterInfo[] parameters = __originalMethod.GetParameters();
        object? Arg(string name)
        {
            int index = Array.FindIndex(parameters, p => p.Name == name);
            return index < 0 ? null : __args[index];
        }
        Player? player = Arg("player") as Player;
        CardSelectorPrefs prefs = Arg("prefs") is CardSelectorPrefs given ? given
            : new CardSelectorPrefs(new LocString("offline", ""), Arg("canSkip") is true ? 0 : 1, 1) { RequireManualConfirmation = false };
        NativeCardSelectionKind kind = __originalMethod.Name switch
        {
            nameof(CardSelectCmd.FromDeckGeneric) => __state?.Kind == NativeCardSelectionKind.DeckRemove
                ? NativeCardSelectionKind.DeckRemove : NativeCardSelectionKind.DeckGeneric,
            nameof(CardSelectCmd.FromDeckForUpgrade) => NativeCardSelectionKind.DeckUpgrade,
            nameof(CardSelectCmd.FromDeckForTransformation) => NativeCardSelectionKind.DeckTransform,
            nameof(CardSelectCmd.FromDeckForEnchantment) => NativeCardSelectionKind.DeckEnchant,
            nameof(CardSelectCmd.FromDeckForRemoval) => NativeCardSelectionKind.DeckRemove,
            nameof(CardSelectCmd.FromChooseACardScreen) => NativeCardSelectionKind.ChooseCard,
            _ => NativeCardSelectionKind.SimpleGrid,
        };
        if (kind == NativeCardSelectionKind.DeckTransform && Arg("cardToTransformation") != null && OfflineRunHost.IsExecuting)
            throw new BlitzSolver.Simulation.Combat.UnsupportedException("host-custom-transformation-preview");
        if (player == null && Arg("cards") is IReadOnlyList<CardModel> cards && cards.Count > 0) player = cards[0].Owner;
        Func<CardModel, bool>? filter = Arg("additionalFilter") as Func<CardModel, bool> ?? Arg("filter") as Func<CardModel, bool>;
        SelectionContext.Value = new(player, prefs, kind, kind == NativeCardSelectionKind.ChooseCard ? 0 : prefs.MinSelect,
            kind == NativeCardSelectionKind.ChooseCard ? 1 : prefs.MaxSelect,
            Arg("enchantment") as EnchantmentModel, Arg("amount") is int amount ? amount : 0, filter ?? (kind == NativeCardSelectionKind.DeckEnchant && __state?.Kind == kind ? __state.Filter : null));
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
