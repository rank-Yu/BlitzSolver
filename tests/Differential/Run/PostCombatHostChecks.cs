// Directed production-host fixtures. Rooms enter through the original map, then native
// EndCombatInternal supplies a settled combat fixture; these do not claim combat simulation coverage.
// All reward effects, alternatives, synchronization and potion actions use the original game methods.
using System.Reflection;
using BlitzSolver.Ai.Run;
using BlitzSolver.Autoplay;
using BlitzSolver.Import;
using BlitzSolver.OfflineHost.Run;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;
using Bootstrap = BlitzSolver.OfflineHost.Runtime.GameBootstrap;

namespace Differential;

internal static class PostCombatHostChecks
{
    private static MainLoopContext _loop = null!;
    private static Func<Player, CombatRoom, RewardsSet>? _factory;
    private static bool _failOffer;
    private static bool _nested;
    private static MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen? _lastScreen;
    private static int _checks, _cases;
    private static RewardsSet? _set;
    internal static int Run()
    {
        _loop = new(); SynchronizationContext.SetSynchronizationContext(_loop);
        Bootstrap.ApplyGodotBypasses(); Bootstrap.SkipGodotNodeStaticConstructors(); Bootstrap.InitializeStaticState();
        Harmony harmony = new("BlitzSolver.Differential.PostCombat");
        harmony.Patch(AccessTools.Method(typeof(RewardsCmd), nameof(RewardsCmd.GenerateForRoomEnd)),
            prefix: new HarmonyMethod(typeof(PostCombatHostChecks), nameof(Generate)));
        harmony.Patch(AccessTools.Method(typeof(RewardsSet), nameof(RewardsSet.GenerateWithoutOffering)),
            prefix: new HarmonyMethod(typeof(PostCombatHostChecks), nameof(FailOffer)));
        harmony.Patch(AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Add), [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
            prefix: new HarmonyMethod(typeof(PostCombatHostChecks), nameof(Nested)));
        harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen), "ShowScreen"),
            postfix: new HarmonyMethod(typeof(PostCombatHostChecks), nameof(CaptureScreen)));
        Case("async-card-instance-replay-and-surrogate-gc", AsyncCard);
        Case("reroll-fresh-token-and-skip-remains", RerollSkip);
        Case("terminal-leave-defers-native-skip-to-map-entry", Leave);
        Case("empty-rewards-explicit-leave", Empty);
        Case("full-potion-return-discard-and-explicit-retry", Potions);
        Case("child-cancel-dispose-and-late-answer", () => Cancellation(false));
        Case("dispose-pending-child-and-late-answer", () => Cancellation(true));
        Case("child-stale-rejected", Stale);
        Case("unrelated-offer-and-native-raw-failure", Failures);
        Case("two-leases-room-exit-and-old-token", TwoLeases);
        Console.WriteLine($"POSTCOMBAT status=pass cases={_cases} checks={_checks}");
        return 0;
    }
    private static void CaptureScreen(MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen? __result) => _lastScreen = __result;
    private static bool Generate(Player player, CombatRoom room, ref Task<RewardsSet> __result)
    {
        if (_factory == null) return true;
        _set = _factory(player, room);
        __result = Task.FromResult(_set); return false;
    }
    private static void FailOffer() { if (_failOffer) throw new InvalidDataException("native-offer-failure"); }
    private static void Nested()
    {
        if (_nested) new RewardsSet(_set!.Player).Offer().GetAwaiter().GetResult();
    }
    private static void Case(string name, Action action)
    {
        Console.WriteLine($"POSTCOMBAT case={name} status=start");
        try { action(); _cases++; Console.WriteLine($"POSTCOMBAT case={name} status=pass"); }
        finally { _factory = null; _failOffer = false; _nested = false; _set = null; _lastScreen = null; if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false); }
    }
    private static string NativeState(NativeRun run, Player player)
        => NativeRunDump.Capture(run, player) + "\nhistory=" + System.Text.Json.JsonSerializer.Serialize(
            run.MapPointHistory, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    private static void Check(bool condition, string reason) { _checks++; if (!condition) throw new InvalidDataException("postcombat-check:" + reason); }
    private static void Wait(Task task) => _loop.RunUntilCompleted(task, TimeSpan.FromSeconds(60), "postcombat-fixture");
    private static T Get<T>(ValueTask<T> task) { Task<T> t = task.AsTask(); Wait(t); return t.GetAwaiter().GetResult(); }
    private static RunPilotAnswer Pick(RunView view, int index = 0)
        => new(RunDecisionProtocol.Select(view.Request, [view.Request.Options[index].OptionId]), true);
    private static RunPilotAnswer Pick(RunView view, RunDecisionOptionKind kind)
        => Pick(view, Enumerable.Range(0, view.Request.OptionCount).First(i => view.Request.Options[i].Kind == kind));
    private static void Reject(Func<Task> callback, string fragment)
    {
        bool rejected = false;
        try { Wait(callback()); }
        catch (InvalidDataException e) when (e.Message.Contains(fragment, StringComparison.Ordinal)) { rejected = true; }
        catch (BlitzSolver.Simulation.Combat.UnsupportedException e) when (e.Message.Contains(fragment, StringComparison.Ordinal)) { rejected = true; }
        Check(rejected, "expected " + fragment);
    }
    private static CardReward Cards(Player player, bool reroll = false)
        => new([player.RunState.CreateCard<Anger>(player), player.RunState.CreateCard<Anger>(player)],
            CardCreationSource.Encounter, player, CardCreationOptions.ForRoom(player, RoomType.Monster)) { CanReroll = reroll };
    private static RewardsSet Set(Player player, CombatRoom room, params Reward[] rewards)
        => new RewardsSet(player).EmptyForRoom(room).WithCustomRewards(rewards.ToList());
    private static (NativeRun Run, Player Player, OfflineRunHost Host) Prepare(Func<Player, CombatRoom, RewardsSet> factory, bool start = true, bool multi = false)
    {
        var create = RunTestHost.StartNewRunWithUnlocksAsync(ModelDb.AllCharacters.Single(c => c.GetType().Name == "Ironclad"),
            0, "R3B2-DECISION", new UnlockState([new NeowEpoch().Id], [], 1));
        Wait(create); (NativeRun run, Player player) = create.Result;
        Wait(((EventRoom)run.CurrentRoom!).LocalMutableEvent.CurrentOptions.Single(o => o.Relic is LavaRock).Chosen());
        Wait(RunManager.Instance.EnterMapCoord(RunTestHost.GetTravelablePoints(run)[0].coord));
        var playable = new OfflineCombatWaiter(_loop).WaitForPlayableCombatAsync(); Wait(playable);
        Wait(CombatManager.Instance.EndCombatInternal());
        CombatRoom room = (CombatRoom)run.CurrentRoom!;
        _factory = factory;
        bool first = true;
        OfflineRunHost? host = null;
        async ValueTask<OfflineRunBoundary> Boundary(CancellationToken token)
        {
            if (multi && !ReferenceEquals(run.CurrentRoom, room))
            {
                await host!.ConfirmRewardsRoomExitAsync(token);
                Check(RewardsSet.testSelector == null, "settled previous lease releases global selector before reuse");
                room = run.CurrentRoom as CombatRoom ?? throw new InvalidDataException("fixture-next-combat");
                first = true;
            }
            return first ? new(OfflineBoundaryKind.Combat)
                : host!.RewardsProceedConfirmed ? new(OfflineBoundaryKind.Map, TravelEnabled: () => host!.RewardsTravelEnabled)
                : new(OfflineBoundaryKind.Rewards);
        }
        host = new(run, player, Boundary,
            async token => { first = false; await host!.OfferCombatRewardsAsync(room, token); return new(); });
        Get(host.WaitForBoundaryAsync(default));
        if (start) Get(host.RunCombatAsync(default));
        return (run, player, host);
    }
    private static RunDecisionCallback NoChild => (_, _) => throw new InvalidDataException("unexpected-child");
    private static void End(OfflineRunHost host)
    {
        try
        {
            RunView view = Get(host.WaitForBoundaryAsync(default)).View;
            Wait(host.ExecuteAsync(Pick(view, RunDecisionOptionKind.Leave), NoChild, default).AsTask());
        }
        finally { Wait(host.DisposeAsync().AsTask()); }
    }
    private static void AsyncCard()
    {
        var (run, player, host) = Prepare((p, r) => Set(p, r, Cards(p)));
        CardModel expected = ((CardReward)_set!.Rewards.Single()).Cards.Last();
        RunView root = Get(host.WaitForBoundaryAsync(default)).View;
        TaskCompletionSource<RunPilotAnswer> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RunView child = default;
        int count = player.Deck.Cards.Count;
        Task selection = host.ExecuteAsync(Pick(root), (view, _) => { child = view; return new(answer.Task); }, default).AsTask();
        Check(!selection.IsCompleted && child.Request.Kind == RunDecision.CardReward && player.Deck.Cards.Count == count,
            "native parent genuinely suspended on asynchronous child");
        Check(child.Request.Options[0].Card.Id == child.Request.Options[1].Card.Id, "duplicate model candidates");
        Reject(() => host.ExecuteAsync(Pick(root), NoChild, default).AsTask(), "host-busy");
        answer.SetResult(Pick(child, 1)); Wait(selection);
        Check(player.Deck.Cards.Any(c => ReferenceEquals(c, expected)), "exact second native instance added");
        Reject(() => host.ExecuteAsync(Pick(root), NoChild, default).AsTask(), "host-no-pending");
        End(host);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(CardSelectCmd.Selector == null && RewardsSet.testSelector == null, "selectors released and surrogate finalizer suppressed");
    }
    private static void RerollSkip()
    {
        var (_, player, host) = Prepare((p, r) => Set(p, r, Cards(p, true)));
        RunView root = Get(host.WaitForBoundaryAsync(default)).View;
        List<RunView> children = [];
        Wait(host.ExecuteAsync(Pick(root), (view, _) =>
        {
            children.Add(view);
            return ValueTask.FromResult(Pick(view, children.Count == 1 ? RunDecisionOptionKind.Reroll : RunDecisionOptionKind.Skip));
        }, default).AsTask());
        Check(children.Count == 2 && children[1].Request.Token.Revision > children[0].Request.Token.Revision, "reroll child token advances");
        Check(Enumerable.Range(0, children[1].Request.OptionCount).All(i => children[1].Request.Options[i].Kind != RunDecisionOptionKind.Reroll),
            "spent reroll hidden");
        Check(!_set!.Rewards.Single().SuccessfullySelected && Get(host.WaitForBoundaryAsync(default)).View.Request.OptionCount == 2,
            "skip preserves reward and returns list");
        // Invalidate this inspected fresh list before the common Leave helper imports again.
        host.InvalidatePending();
        End(host);
    }
    private static void Leave()
    {
        var (run, player, host) = Prepare((p, r) => Set(p, r, Cards(p), new GoldReward(17, 17, p)));
        RunView root = Get(host.WaitForBoundaryAsync(default)).View;
        string before = NativeState(run, player);
        Wait(host.ExecuteAsync(Pick(root, RunDecisionOptionKind.Leave), NoChild, default).AsTask());
        Check(before == NativeState(run, player) && !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(_set!),
            "terminal proceed preserves untaken history and stack");
        Reject(() => new RewardsSet(player).Offer(), "host-unsupported-native-entry");
        Reject(() => host.ConfirmRewardsRoomExitAsync(default).AsTask(), "host-reward-exit-unconfirmed");
        RunView map = Get(host.WaitForBoundaryAsync(default)).View;
        Wait(host.ExecuteAsync(Pick(map), NoChild, default).AsTask());
        Wait(host.ConfirmRewardsRoomExitAsync(default).AsTask());
        Check(RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(_set!) && !ReferenceEquals(_set!.Room, run.CurrentRoom) && before != NativeState(run, player)
            && run.MapPointHistory.SelectMany(a => a).SelectMany(h => h.PlayerStats).Sum(s => s.CardChoices.Count) == 2,
            "real map entry skips stack and completes tracked offer");
        Wait(host.DisposeAsync().AsTask());
    }
    private static void TwoLeases()
    {
        var (run, player, host) = Prepare((p, r) => Set(p, r, Cards(p)), multi: true);
        RunView first = Get(host.WaitForBoundaryAsync(default)).View, firstChild = default;
        Wait(host.ExecuteAsync(Pick(first), (v, _) => { firstChild = v; return ValueTask.FromResult(Pick(v)); }, default).AsTask());
        RunView leave = Get(host.WaitForBoundaryAsync(default)).View;
        Wait(host.ExecuteAsync(Pick(leave, RunDecisionOptionKind.Leave), NoChild, default).AsTask());
        RunView map = Get(host.WaitForBoundaryAsync(default)).View;
        int next = Enumerable.Range(0, map.Request.OptionCount).First(i => map.Request.Options[i].PointType == RunMapPointType.Monster);
        Wait(host.ExecuteAsync(Pick(map, next), NoChild, default).AsTask());
        Wait(new OfflineCombatWaiter(_loop).WaitForPlayableCombatAsync());
        Wait(CombatManager.Instance.EndCombatInternal());
        RunPilotBoundary combat = Get(host.WaitForBoundaryAsync(default));
        Check(combat.Kind == RunBoundaryKind.Combat, "second native combat boundary after real room exit");
        Get(host.RunCombatAsync(default));
        RunView second = Get(host.WaitForBoundaryAsync(default)).View;
        Check(second.Request.Token.SessionId == first.Request.Token.SessionId
            && second.Request.Token.Revision > firstChild.Request.Token.Revision, "lease reuse preserves monotonic session revision");
        Reject(() => host.ExecuteAsync(Pick(firstChild), NoChild, default).AsTask(), "action-token");
        Wait(host.ExecuteAsync(Pick(second), (v, _) => ValueTask.FromResult(Pick(v)), default).AsTask());
        End(host);
        Check(RewardsSet.testSelector == null && CardSelectCmd.Selector == null, "two completed leases leave no global owner");
    }
    private static void Empty()
    {
        var (_, _, host) = Prepare((p, r) => Set(p, r));
        RunView root = Get(host.WaitForBoundaryAsync(default)).View;
        Check(root.Request.OptionCount == 1 && root.Request.Options[0].Kind == RunDecisionOptionKind.Leave, "empty terminal rewards still require proceed");
        Wait(host.ExecuteAsync(Pick(root), NoChild, default).AsTask()); Wait(host.DisposeAsync().AsTask());
    }
    private static void Potions()
    {
        var (_, player, host) = Prepare((p, r) =>
        {
            for (int i = 0; i < p.MaxPotionCount; i++) Wait(PotionCmd.TryToProcure(ModelDb.Potion<BlockPotion>().ToMutable(), p));
            return Set(p, r, new PotionReward(ModelDb.Potion<FirePotion>().ToMutable(), p));
        });
        RunView root = Get(host.WaitForBoundaryAsync(default)).View;
        string[] initial = player.PotionSlots.Select(p => p!.Id.Entry).ToArray();
        Wait(host.ExecuteAsync(Pick(root), (view, _) =>
        {
            Check(view.Request.Kind == RunDecision.PotionDiscard, "full native potion attempt opens management");
            return ValueTask.FromResult(Pick(view, RunDecisionOptionKind.Skip));
        }, default).AsTask());
        Check(player.PotionSlots.Select(p => p!.Id.Entry).SequenceEqual(initial) && !_set!.Rewards.Single().SuccessfullySelected, "back changes no inventory or reward");
        root = Get(host.WaitForBoundaryAsync(default)).View;
        Wait(host.ExecuteAsync(Pick(root), (view, _) => ValueTask.FromResult(Pick(view, RunDecisionOptionKind.Potion)), default).AsTask());
        Check(player.PotionSlots[0] == null && !_set!.Rewards.Single().SuccessfullySelected, "native discard returns without automatic retry");
        root = Get(host.WaitForBoundaryAsync(default)).View;
        Wait(host.ExecuteAsync(Pick(root), NoChild, default).AsTask());
        Check(player.PotionSlots[0] is FirePotion && _set!.Rewards.Single().SuccessfullySelected, "fresh explicit action claims potion");
        End(host);
    }
    private static void Cancellation(bool disposeFirst)
    {
        var (_, player, host) = Prepare((p, r) => Set(p, r, Cards(p)));
        RunView root = Get(host.WaitForBoundaryAsync(default)).View, child = default;
        TaskCompletionSource<RunPilotAnswer> late = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = player.Deck.Cards.Count;
        using CancellationTokenSource cancel = new();
        Task execute = host.ExecuteAsync(Pick(root), (view, _) => { child = view; return new(late.Task); }, cancel.Token).AsTask();
        if (disposeFirst) Wait(host.DisposeAsync().AsTask()); else cancel.Cancel();
        bool cancelled = false; try { Wait(execute); } catch (OperationCanceledException) { cancelled = true; }
        Wait(host.DisposeAsync().AsTask());
        var expired = _lastScreen ?? throw new InvalidDataException("missing-surrogate");
        Reject(() => expired.OptionSelected(), "host-unrelated-reward-ui");
        Reject(() => { expired.RefreshOptions([], []); return Task.CompletedTask; }, "host-unrelated-reward-ui");
        Reject(() => { expired.GetCardHolder(null!); return Task.CompletedTask; }, "host-unrelated-reward-ui");
        late.SetResult(Pick(child)); _loop.Pump(TimeSpan.FromMilliseconds(10));
        Check(cancelled && player.Deck.Cards.Count == count && RewardsSet.testSelector == null && CardSelectCmd.Selector == null,
            "cancelled child and late noncooperative policy cannot award");
        GC.Collect(); GC.WaitForPendingFinalizers();
    }
    private static void Stale()
    {
        var (_, player, host) = Prepare((p, r) => Set(p, r, Cards(p)));
        RunView root = Get(host.WaitForBoundaryAsync(default)).View;
        TaskCompletionSource<RunPilotAnswer> late = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RunView child = default;
        Task execute = host.ExecuteAsync(Pick(root), (view, _) => { child = view; return new(late.Task); }, default).AsTask();
        ((CardReward)_set!.Rewards.Single()).CanReroll = true;
        late.SetResult(Pick(child));
        Reject(() => execute, "stale");
        Wait(host.DisposeAsync().AsTask());
    }
    private static void Failures()
    {
        var (_, player, host) = Prepare((p, r) => Set(p, r, Cards(p)), start: false);
        _failOffer = true;
        Reject(() => host.RunCombatAsync(default).AsTask(), "native-offer-failure");
        _failOffer = false;
        Reject(() => host.DisposeAsync().AsTask(), "native-offer-failure");
        Check(RewardsSet.testSelector == null, "faulted raw offer still releases selector");
        if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false);
        (_, player, host) = Prepare((p, r) => Set(p, r, Cards(p)));
        RunView root = Get(host.WaitForBoundaryAsync(default)).View;
        _nested = true;
        Reject(() => host.ExecuteAsync(Pick(root), (view, _) => ValueTask.FromResult(Pick(view)), default).AsTask(), "host-unsupported-native-entry");
        _nested = false;
        Wait(host.DisposeAsync().AsTask());
    }
}
