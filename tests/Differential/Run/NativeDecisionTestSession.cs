// TEST ONLY: native dispatch and lifecycle exercise. This is not an OfflineHost or a production pilot.
// Selection callback prefs are captured at the actual CardSelectCmd entry; ICardSelector alone loses them.
using System.Reflection;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Run;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;

namespace Differential;

internal sealed partial class NativeDecisionTestSession
{
    private static long _nextSession;
    private readonly ulong _session = checked((ulong)Interlocked.Increment(ref _nextSession));
    private ulong _revision;
    private Pending? _current;
    private int _executions;
    public int NativeCalls { get; private set; }
    public RunDecisionRequest Request => _current?.Request ?? throw new InvalidDataException("host-no-pending");
    public bool HasPending => _current != null;
    public RunDecisionToken LastToken { get; private set; }

    private sealed record Pending(RunDecisionRequest Request, Action ValidateCurrent,
        Func<RunAction, Task> Dispatch, Action<RunAction> ValidateBindings, Action<Exception>? Invalidate);

    private void Begin<T>(Func<RunDecisionToken, NativeDecisionSnapshot<T>> import,
        Func<IReadOnlyList<T>, RunAction, Task> dispatch, bool nested = false, Action<Exception>? invalidate = null) where T : class
    {
        if (_current != null || _executions != 0 && !nested) throw new InvalidDataException("host-busy");
        RunDecisionToken token = new(_session, checked(++_revision));
        NativeDecisionSnapshot<T> snapshot = import(token);
        LastToken = token;
        _current = new(snapshot.Request, snapshot.ValidateCurrent,
            action => dispatch(snapshot.Resolve(action), action), action => snapshot.Resolve(action), invalidate);
    }

    public void Map(NativeRun run, Func<bool> enabled)
        => Begin(token => RunDecisionImporter.Map(run, token, enabled),
            (points, _) => RunManager.Instance.EnterMapCoord(points[0].coord));

    public void Event(EventModel model)
        => Begin(token => RunDecisionImporter.Event(model, token),
            (options, _) => NativeDecisionTestHooks.ChooseEvent(model, options[0]));

    public void Rest(RestSiteRoom room, Player player)
        => Begin(token => RunDecisionImporter.Rest(room, player, token), (options, _) =>
        {
            int index = -1;
            for (int i = 0; i < room.Options.Count; i++)
                if (ReferenceEquals(room.Options[i], options[0])) index = i;
            if (index < 0) throw new InvalidDataException("host-rest-instance");
            return RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(index);
        });

    public Task<IEnumerable<CardModel>> Cards(Player player, IReadOnlyList<CardModel> cards,
        CardSelectorPrefs prefs, NativeCardSelectionKind kind, bool nested = false)
    {
        TaskCompletionSource<IEnumerable<CardModel>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Begin(token => RunDecisionImporter.CardSelect(player, cards, prefs, kind, token,
            kind == NativeCardSelectionKind.DeckUpgrade ? RunSelectionPurpose.Upgrade :
                InShopRemoval ? RunSelectionPurpose.Remove : RunSelectionPurpose.None),
            (selected, action) =>
            {
                // Native deck UI cancellation resolves CardsSelected to Array.Empty<CardModel>().
                // Keep the protocol distinction until this adapter boundary (Select may also be empty).
                completion.SetResult(action.Kind == RunActionKind.Cancel ? Array.Empty<CardModel>() : selected);
                return Task.CompletedTask;
            }, nested, error => completion.TrySetException(error));
        return completion.Task;
    }

    private void Invalidate(Pending pending, Exception error)
    {
        _current = null;
        pending.Invalidate?.Invoke(error); // Fail any original waiting callback; never impersonate Cancel.
    }

    public async Task Submit(RunAction action, bool confirmed = false)
    {
        Pending pending = _current ?? throw new InvalidDataException("host-no-pending");
        RunDecisionProtocol.ValidateAction(pending.Request, action);
        if (action.Kind == RunActionKind.Select && pending.Request.RequireConfirmation != 0 && !confirmed)
            throw new InvalidDataException("host-confirmation-required");
        pending.ValidateBindings(action);
        try { pending.ValidateCurrent(); }
        catch (InvalidDataException error) { Invalidate(pending, error); throw; }
        catch (BlitzSolver.Simulation.Combat.UnsupportedException error) { Invalidate(pending, error); throw; }
        catch (BlitzSolver.Simulation.Combat.CapacityException error) { Invalidate(pending, error); throw; }
        catch (UnsupportedImportException error) { Invalidate(pending, error); throw; }
        _current = null; // Consume before calling native code. Exceptions and nested choices never revive it.
        _executions++;
        NativeCalls++;
        try { await pending.Dispatch(action); }
        finally { _executions--; }
    }
}

internal sealed class PendingNativeCardSelector(NativeDecisionTestSession session) : ICardSelector
{
    public List<CardModel>? LastOptions { get; private set; }
    public int CallCount { get; private set; }
    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        NativeDecisionTestHooks.SelectionScope scope = NativeDecisionTestHooks.SelectionScope.Current
            ?? throw new InvalidDataException("test-selector: complete native prefs were not captured");
        if (scope.Prefs.MinSelect != minSelect || scope.Prefs.MaxSelect != maxSelect)
            throw new InvalidDataException("test-selector: native bounds disagree");
        LastOptions = options as List<CardModel> ?? options.ToList();
        CallCount++;
        return session.Cards(scope.Player, LastOptions, scope.Prefs, scope.Kind, nested: true);
    }
    public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
        IReadOnlyList<CardRewardAlternative> alternatives)
        => throw new NotSupportedException("test-selector: rewards are outside this batch");
}

internal static class NativeDecisionTestHooks
{
    [ThreadStatic] private static List<Task>? _dispatchedOptions;
    private static bool NoHoverTips(ref IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> __result) { __result = []; return false; }
    private static bool Completed(ref Task __result) { __result = Task.CompletedTask; return false; }
    private static bool ProfilePath(int profileId, ref string __result) { __result = $"profile{profileId}"; return false; }
    public static void Install()
    {
        // UI-only bypasses also used by RunProbeHooks; do not install or alter its driving policy.
        foreach (Type type in new[] { typeof(EnchantmentModel), typeof(RelicModel) })
            GameBootstrap.Harmony.Patch(AccessTools.PropertyGetter(type, type == typeof(RelicModel) ? "HoverTipsExcludingRelic" : "HoverTips"),
                prefix: new HarmonyMethod(typeof(NativeDecisionTestHooks), nameof(NoHoverTips)));
        foreach (string name in new[] { "LoadRoomEventAssets", "LoadRoomRestSite" })
            GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Assets.PreloadManager), name),
                prefix: new HarmonyMethod(typeof(NativeDecisionTestHooks), nameof(Completed)));
        foreach (MethodInfo method in typeof(MegaCrit.Sts2.Core.Saves.UserDataPathProvider).GetMethods()
            .Where(m => m.Name is "GetProfileDir" or "GetProfileScopedBasePath"))
            GameBootstrap.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeDecisionTestHooks), nameof(ProfilePath)));
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitProfileId(1);
        foreach (string name in new[] { nameof(CardSelectCmd.FromDeckGeneric), nameof(CardSelectCmd.FromDeckForUpgrade), nameof(CardSelectCmd.FromSimpleGrid) })
        {
            MethodInfo method = typeof(CardSelectCmd).GetMethods().Single(m => m.Name == name);
            GameBootstrap.Harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(NativeDecisionTestHooks), nameof(SelectionPrefix)),
                finalizer: new HarmonyMethod(typeof(NativeDecisionTestHooks), nameof(SelectionFinalizer)));
        }
        GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(EventOption), nameof(EventOption.Chosen)),
            postfix: new HarmonyMethod(typeof(NativeDecisionTestHooks), nameof(ChosenPostfix)));
    }

    internal sealed class SelectionScope : IDisposable
    {
        [ThreadStatic] private static SelectionScope? _current;
        private readonly SelectionScope? _previous;
        public static SelectionScope? Current => _current;
        public Player Player { get; }
        public CardSelectorPrefs Prefs { get; }
        public NativeCardSelectionKind Kind { get; }
        public SelectionScope(Player player, CardSelectorPrefs prefs, NativeCardSelectionKind kind)
        {
            _previous = _current; _current = this;
            Player = player; Prefs = prefs; Kind = kind;
        }
        public void Dispose() => _current = _previous;
    }

    private static void SelectionPrefix(Player player, CardSelectorPrefs prefs, MethodBase __originalMethod, out SelectionScope __state)
        => __state = new(player, prefs, __originalMethod.Name switch
        {
            nameof(CardSelectCmd.FromDeckGeneric) => NativeCardSelectionKind.DeckGeneric,
            nameof(CardSelectCmd.FromDeckForUpgrade) => NativeCardSelectionKind.DeckUpgrade,
            _ => NativeCardSelectionKind.SimpleGrid,
        });
    private static void SelectionFinalizer(SelectionScope? __state) => __state?.Dispose();
    private static void ChosenPostfix(Task __result) => _dispatchedOptions?.Add(__result);

    public static Task ChooseEvent(EventModel model, EventOption option)
    {
        if (option.IsProceed) return option.Chosen();
        if (_dispatchedOptions != null) throw new InvalidDataException("test-event: synchronous dispatch reentrancy");
        List<Task> rawTasks = [];
        int index = -1;
        for (int i = 0; i < model.CurrentOptions.Count; i++)
            if (ReferenceEquals(model.CurrentOptions[i], option)) index = i;
        if (index < 0) throw new InvalidDataException("test-event: option instance missing");
        _dispatchedOptions = rawTasks;
        try { RunManager.Instance.EventSynchronizer.ChooseLocalOption(index); }
        finally { _dispatchedOptions = null; }
        if (rawTasks.Count != 1) throw new InvalidDataException("test-event: expected one original choice task");
        // AwaitPendingOptionTasks awaits TaskHelper.RunSafely wrappers and can swallow errors.
        // Await the original public Chosen task captured above so dispatch errors reach the caller.
        return rawTasks[0];
    }
}
