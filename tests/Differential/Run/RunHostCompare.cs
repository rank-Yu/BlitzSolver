// Production OfflineHost integration. Debug rooms are lifecycle fixtures, never full-run claims.
using System.Reflection;
using BlitzSolver.Ai.Run;
using BlitzSolver.Autoplay;
using BlitzSolver.Import;
using BlitzSolver.OfflineHost.Run;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;

namespace Differential;

internal static class RunHostCompare
{
    private static MainLoopContext _loop = null!;
    private static int _checks, _cases, _choiceReservations;
    public static int Run(string[] args)
    {
        if (args.Length != 0) throw new InvalidDataException("--run-host-compare accepts no arguments");
        _loop = new(); SynchronizationContext.SetSynchronizationContext(_loop);
        GameBootstrap.ApplyGodotBypasses(); GameBootstrap.SkipGodotNodeStaticConstructors(); GameBootstrap.InitializeStaticState();
        InstallUiOnly(); // Does not install the old decision/probe callback hooks.
        Case("natural-neow-map-first-combat", Natural);
        Case("smith-asynchronous-success-and-cancel", Smith);
        Case("replay-confirmation-stale-and-native-failure", Lifecycle);
        Case("native-event-dispatch-exception", NativeFailure);
        Case("unsupported-native-entry-guards", UnsupportedEntries);
        Case("external-cancel-invalidate-dispose-late", Cancellation);
        Case("wait-and-combat-exclusion-and-run-binding", Concurrency);
        Console.WriteLine($"RUNHOST status=pass cases={_cases} checks={_checks} natural=first-combat-boundary-only");
        return 0;
    }
    private static void Case(string name, Action action)
    {
        Console.WriteLine($"RUNHOST case={name} status=start");
        try { action(); _cases++; Console.WriteLine($"RUNHOST case={name} status=pass checks={_checks}"); }
        finally { if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false); }
    }
    private static void Check(bool value, string reason)
    { _checks++; if (!value) throw new InvalidDataException("host-check: " + reason); }
    private static void Wait(Task task) => _loop.RunUntilCompleted(task, TimeSpan.FromSeconds(60), "production run host");
    private static T Get<T>(Task<T> task) { Wait(task); return task.GetAwaiter().GetResult(); }
    private static T Get<T>(ValueTask<T> task) => Get(task.AsTask());
    private static void Reject(Func<Task> action, string reason)
    {
        bool rejected = false;
        try { Wait(action()); }
        catch (InvalidDataException error) when (error.Message.Contains(reason, StringComparison.Ordinal)) { rejected = true; }
        catch (BlitzSolver.Simulation.Combat.UnsupportedException error) when (error.Message.Contains(reason, StringComparison.Ordinal)) { rejected = true; }
        Check(rejected, "expected rejection " + reason);
    }
    private static void Cancelled(Task task)
    { bool cancelled = false; try { Wait(task); } catch (OperationCanceledException) { cancelled = true; } Check(cancelled, "native chain cancelled"); }
    private static (NativeRun Run, Player Player) Start(bool natural = false)
    {
        if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false);
        CharacterModel character = ModelDb.AllCharacters.Single(c => c.GetType().Name == "Ironclad");
        UnlockState unlock = natural ? new UnlockState([new NeowEpoch().Id], [], 1) : UnlockState.none;
        return Get(natural ? RunTestHost.StartNewRunWithUnlocksAsync(character, 0, "R3B2-DECISION", unlock)
            : RunTestHost.StartNewRunBeforeChoicesAsync(character, 0, "R3B2-DECISION", unlock));
    }
    private static (NativeRun Run, Player Player, RestSiteRoom Room) Rest()
    {
        (NativeRun run, Player player) = Start();
        Wait(RunManager.Instance.EnterRoomDebug(RoomType.RestSite, showTransition: false));
        return (run, player, (RestSiteRoom)run.CurrentRoom!);
    }
    private static OfflineRunHost RestHost(NativeRun run, Player player, RestSiteRoom room)
        => new(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Rest, Rest: room)));
    private static RunPilotAnswer Select(RunView view, int index = 0, bool confirmed = true)
        => new(RunDecisionProtocol.Select(view.Request, [view.Request.Options[index].OptionId]), confirmed);
    private sealed class Policy(Func<RunView, CancellationToken, ValueTask<RunPilotAnswer>> callback) : IRunPolicy
    { public ValueTask<RunPilotAnswer> DecideAsync(RunView view, CancellationToken token) => callback(view, token); }
    private static string State(NativeRun run, Player player)
        => NativeRunDump.Capture(run, player) + "\nimport=" + RunDump.Capture(RunStateImporter.Import(run, player));
    private static void Natural()
    {
        (NativeRun run, Player player) = Start(natural: true);
        async ValueTask<OfflineRunBoundary> Boundary(CancellationToken token)
        {
            if (run.CurrentRoom is CombatRoom)
            { await new OfflineCombatWaiter(_loop).WaitForPlayableCombatAsync(); return new(OfflineBoundaryKind.Combat); }
            if (run.CurrentRoom is EventRoom er && !er.LocalMutableEvent.IsFinished)
                return new(OfflineBoundaryKind.Event, Event: er.LocalMutableEvent);
            return new(OfflineBoundaryKind.Map, TravelEnabled: () => true);
        }
        OfflineRunHost host = new(run, player, Boundary);
        int calls = 0;
        Policy policy = new((view, _) =>
        {
            calls++;
            int index = view.Request.Kind == RunDecision.Event
                ? Enumerable.Range(0, view.Request.OptionCount).Single(i => view.Request.Options[i].Model.Kind == RunRefKind.Relic
                    && Registry.Relic(view.Request.Options[i].Model.Id).NativeId == "LAVA_ROCK") : 0;
            return ValueTask.FromResult(Select(view, index));
        });
        RunPilotResult result;
        try { result = Get(RunPilot.RunAsync(host, policy)); }
        finally { Wait(host.DisposeAsync().AsTask()); }
        Check(result.Reason == RunStopReason.Paused && result.Detail == "combat-boundary" && result.Decisions == 2 && calls == 2,
            "real shared pilot stops explicitly at first combat");
        Check(run.CurrentRoom is CombatRoom && player.PlayerCombatState!.TurnNumber == 1, "first actual playable combat turn");
        string actual = State(run, player);
        (NativeRun direct, Player dp) = Start(natural: true);
        EventModel model = ((EventRoom)direct.CurrentRoom!).LocalMutableEvent;
        Wait(model.CurrentOptions.Single(o => o.Relic is LavaRock).Chosen());
        Wait(RunManager.Instance.EnterMapCoord(RunTestHost.GetTravelablePoints(direct)[0].coord));
        Wait(new OfflineCombatWaiter(_loop).WaitForPlayableCombatAsync());
        Check(actual == State(direct, dp), "independent native calls match imported state, native dump and RNG");
    }
    private static void Smith()
    {
        foreach (bool cancel in new[] { false, true })
        {
            (NativeRun run, Player player, RestSiteRoom room) = Rest();
            using CancellationTokenSource fixtureEnd = new();
            int boundaries = 0;
            OfflineRunHost host = new(run, player, _ =>
            {
                if (boundaries++ == 0) return ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Rest, Rest: room));
                fixtureEnd.Cancel(); throw new OperationCanceledException(fixtureEnd.Token);
            });
            int smith = Enumerable.Range(0, room.Options.Count).Single(i => room.Options[i] is SmithRestSiteOption);
            int upgrades = player.Deck.Cards.Sum(c => c.CurrentUpgradeLevel);
            TaskCompletionSource<RunPilotAnswer> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RunView child = default; int callbacks = 0;
            RunView parent = default;
            ValueTask<RunPilotAnswer> Decide(RunView view, CancellationToken token)
            {
                callbacks++;
                if (view.Request.Kind == RunDecision.RestSite) { parent = view; return ValueTask.FromResult(Select(view, smith)); }
                child = view; return new(answer.Task);
            }
            Task<RunPilotResult> effect = RunPilot.RunAsync(host, new Policy(Decide), token: fixtureEnd.Token);
            Check(callbacks == 2 && !effect.IsCompleted && child.Request.Kind == RunDecision.CardSelect,
                "native Smith parent awaits incomplete asynchronous child policy");
            Check(child.Request.Token.SessionId == parent.Request.Token.SessionId
                && child.Request.Token.Revision > parent.Request.Token.Revision && child.Player.CurrentHp == player.Creature.CurrentHp,
                "fresh child view and unique monotonic token");
            Reject(() => host.ExecuteAsync(Select(parent, smith), Decide, default).AsTask(), "host-busy");
            answer.SetResult(cancel ? new(RunDecisionProtocol.Cancel(child.Request)) : Select(child));
            RunPilotResult result = Get(effect);
            Check(result.Reason == RunStopReason.Cancelled && result.Decisions == 2, "same shared policy path; fixture explicitly stopped after parent settles");
            Check(player.Deck.Cards.Sum(c => c.CurrentUpgradeLevel) == upgrades + (cancel ? 0 : 1), "actual native upgrade or legal cancel");
            Reject(() => host.ExecuteAsync(Select(child), Decide, default).AsTask(), "host-no-pending");
            string actual = State(run, player); Wait(host.DisposeAsync().AsTask());
            (NativeRun direct, Player dp, RestSiteRoom dr) = Rest();
            using IDisposable selector = CardSelectCmd.UseSelector(new DirectSelector(cancel));
            Wait(RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(Enumerable.Range(0, dr.Options.Count).Single(i => dr.Options[i] is SmithRestSiteOption)));
            Check(actual == State(direct, dp), "independent native Smith comparison cancel=" + cancel);
        }
    }
    private static void Lifecycle()
    {
        (NativeRun run, Player player, RestSiteRoom room) = Rest(); OfflineRunHost host = RestHost(run, player, room);
        RunPilotBoundary boundary = Get(host.WaitForBoundaryAsync(default));
        RunPilotAnswer wrong = Select(boundary.View); RunAction wrongAction = wrong.Action; wrongAction.Token = new(9, 9); wrong = new(wrongAction);
        string before = State(run, player);
        Reject(() => host.ExecuteAsync(wrong, (_, _) => throw new InvalidOperationException(), default).AsTask(), "action-token");
        Check(before == State(run, player), "invalid answer has no effects");
        int smith = Enumerable.Range(0, room.Options.Count).Single(i => room.Options[i] is SmithRestSiteOption);
        TaskCompletionSource<RunPilotAnswer> answer = new(TaskCreationOptions.RunContinuationsAsynchronously); RunView child = default;
        Task effect = host.ExecuteAsync(Select(boundary.View, smith), (view, _) => { child = view; return new(answer.Task); }, default).AsTask();
        Wait(CardPileCmd.RemoveFromDeck(player.Deck.Cards.First(c => c.IsUpgradable)));
        answer.SetResult(Select(child)); Reject(() => effect, "native-stale");
        Check(effect.IsCompleted, "stale child releases original parent");
        RunPilotAnswer consumed = Select(boundary.View, smith);
        boundary = Get(host.WaitForBoundaryAsync(default));
        Reject(() => host.ExecuteAsync(consumed, (_, _) => throw new InvalidOperationException(), default).AsTask(), "action-token");
        Task failed = host.ExecuteAsync(Select(boundary.View, smith), (_, _) => throw new InvalidDataException("policy-native-chain-failure"), default).AsTask();
        Reject(() => failed, "policy-native-chain-failure");
        boundary = Get(host.WaitForBoundaryAsync(default)); Check(boundary.Kind == RunBoundaryKind.Decision, "failure releases host busy");
        host.InvalidatePending(); Wait(host.DisposeAsync().AsTask());
    }
    private static void NativeFailure()
    {
        (NativeRun run, Player player) = Start();
        Wait(RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<ThisOrThat>(), showTransition: false));
        EventModel model = ((EventRoom)run.CurrentRoom!).LocalMutableEvent;
        List<EventOption> options = (List<EventOption>)model.CurrentOptions; options.Clear();
        options.Add(new EventOption(model, () => Task.FromException(new InvalidOperationException("production-native-dispatch-fault")), "FIXTURE.THROW"));
        OfflineRunHost host = new(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Event, Event: model)));
        RunPilotBoundary boundary = Get(host.WaitForBoundaryAsync(default));
        bool propagated = false;
        try { Wait(host.ExecuteAsync(Select(boundary.View), (_, _) => throw new InvalidOperationException(), default).AsTask()); }
        catch (InvalidOperationException error) when (error.Message == "production-native-dispatch-fault") { propagated = true; }
        Check(propagated, "raw native EventOption task exception reaches caller");
        Reject(() => host.ExecuteAsync(Select(boundary.View), (_, _) => throw new InvalidOperationException(), default).AsTask(), "host-no-pending");
        options.Clear(); options.Add(new EventOption(model, () => Task.CompletedTask, "FIXTURE.AFTER_FAILURE"));
        Check(Get(host.WaitForBoundaryAsync(default)).Kind == RunBoundaryKind.Decision, "native failure releases busy without reviving consumed token");
        Wait(host.DisposeAsync().AsTask());
    }
    private static void UnsupportedEntries()
    {
        foreach (string entry in new[] { "bundle", "relic", "reward" })
        {
            (NativeRun run, Player player) = Start();
            Wait(RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<ThisOrThat>(), showTransition: false));
            EventModel model = ((EventRoom)run.CurrentRoom!).LocalMutableEvent;
            List<EventOption> options = (List<EventOption>)model.CurrentOptions; options.Clear();
            bool completed = false;
            IReadOnlyList<IReadOnlyList<CardModel>> bundles = new[] { new[] { player.Deck.Cards[0] }, new[] { player.Deck.Cards[1] } };
            RewardsSet rewards = new(player); rewards.Rewards.Add(new GoldReward(5, player));
            options.Add(new EventOption(model, async () =>
            {
                if (entry == "bundle") await CardSelectCmd.FromChooseABundleScreen(player, bundles);
                else if (entry == "relic") await RelicSelectCmd.FromChooseARelicScreen(player, [ModelDb.Relic<LavaRock>()]);
                else await rewards.Offer();
                completed = true;
            }, "FIXTURE.UNSUPPORTED"));
            OfflineRunHost host = new(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Event, Event: model)));
            RunPilotBoundary boundary = Get(host.WaitForBoundaryAsync(default));
            int choiceCount = _choiceReservations, gold = player.Gold;
            Reject(() => host.ExecuteAsync(Select(boundary.View), (_, _) => throw new InvalidOperationException(), default).AsTask(), "host-unsupported-native-entry");
            Check(!completed && choiceCount == _choiceReservations && player.Gold == gold && !rewards.Rewards[0].SuccessfullySelected,
                "unsupported " + entry + " fails before native choice reservation or automatic reward");
            Wait(host.DisposeAsync().AsTask());
            // The installed production guard is inactive outside the native host execution scope.
            if (entry == "bundle")
            {
                IEnumerable<CardModel> direct = Get(CardSelectCmd.FromChooseABundleScreen(player, bundles));
                Check(ReferenceEquals(direct.Single(), bundles[0][0]) && _choiceReservations == choiceCount + 1,
                    "inactive guard preserves original TestMode bundle behavior");
            }
        }
    }
    private static void Cancellation()
    {
        foreach (string mode in new[] { "external", "invalidate", "dispose" })
        {
            (NativeRun run, Player player, RestSiteRoom room) = Rest(); OfflineRunHost host = RestHost(run, player, room);
            RunPilotBoundary boundary = Get(host.WaitForBoundaryAsync(default));
            int smith = Enumerable.Range(0, room.Options.Count).Single(i => room.Options[i] is SmithRestSiteOption);
            using CancellationTokenSource cancellation = new();
            TaskCompletionSource<RunPilotAnswer> answer = new(TaskCreationOptions.RunContinuationsAsynchronously); RunView child = default;
            int upgrades = player.Deck.Cards.Sum(c => c.CurrentUpgradeLevel);
            Task effect = host.ExecuteAsync(Select(boundary.View, smith), (view, _) => { child = view; return new(answer.Task); }, cancellation.Token).AsTask();
            Check(!effect.IsCompleted, "child is genuinely pending before " + mode);
            Task? disposal = null;
            if (mode == "external") cancellation.Cancel(); else if (mode == "invalidate") host.InvalidatePending(); else disposal = host.DisposeAsync().AsTask();
            Cancelled(effect); if (disposal != null) Wait(disposal);
            answer.SetResult(Select(child)); Wait(Tick());
            Check(upgrades == player.Deck.Cards.Sum(c => c.CurrentUpgradeLevel), "late answer never upgrades after " + mode);
            Wait(host.DisposeAsync().AsTask());
            using IDisposable selector = CardSelectCmd.UseSelector(new DirectSelector()); // Scope was actually restored.
        }
    }
    private static void Concurrency()
    {
        (NativeRun run, Player player) = Start();
        TaskCompletionSource<OfflineRunBoundary> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        OfflineRunHost host = new(run, player, _ => new(signal.Task));
        Task<RunPilotBoundary> waiting = host.WaitForBoundaryAsync(default).AsTask();
        Reject(() => host.WaitForBoundaryAsync(default).AsTask(), "host-busy");
        Reject(() => host.RunCombatAsync(default).AsTask(), "host-busy");
        signal.SetResult(new(OfflineBoundaryKind.Combat)); Get(waiting);
        Check(Get(host.RunCombatAsync(default)).Stop == RunStopReason.Paused, "combat consumes boundary once");
        Reject(() => host.RunCombatAsync(default).AsTask(), "host-no-combat-boundary");
        Wait(host.DisposeAsync().AsTask());
        host = new(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Map, TravelEnabled: () => true)));
        Start(); Reject(() => host.WaitForBoundaryAsync(default).AsTask(), "host-run-changed"); Wait(host.DisposeAsync().AsTask());
        (run, player) = Start();
        host = new(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Map, TravelEnabled: () => true)));
        RunPilotBoundary oldBoundary = Get(host.WaitForBoundaryAsync(default)); Start();
        Reject(() => host.ExecuteAsync(Select(oldBoundary.View), (_, _) => throw new InvalidOperationException(), default).AsTask(), "host-run-changed");
        Reject(() => host.ExecuteAsync(Select(oldBoundary.View), (_, _) => throw new InvalidOperationException(), default).AsTask(), "host-no-pending");
        Wait(host.DisposeAsync().AsTask());
        (run, player) = Start();
        signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host = new(run, player, _ => new(signal.Task));
        waiting = host.WaitForBoundaryAsync(default).AsTask(); host.InvalidatePending(); Cancelled(waiting);
        signal.SetResult(new(OfflineBoundaryKind.Combat)); Wait(Tick());
        Reject(() => host.RunCombatAsync(default).AsTask(), "host-no-combat-boundary"); Wait(host.DisposeAsync().AsTask());
    }
    private static async Task Tick() => await Task.Yield();
    private static void InstallUiOnly()
    {
        foreach (Type type in new[] { typeof(EnchantmentModel), typeof(RelicModel) })
            GameBootstrap.Harmony.Patch(AccessTools.PropertyGetter(type, type == typeof(RelicModel) ? "HoverTipsExcludingRelic" : "HoverTips"), prefix: new HarmonyMethod(typeof(RunHostCompare), nameof(NoHoverTips)));
        foreach (string name in new[] { "LoadRoomEventAssets", "LoadRoomRestSite" })
            GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Assets.PreloadManager), name), prefix: new HarmonyMethod(typeof(RunHostCompare), nameof(Completed)));
        foreach (MethodInfo method in typeof(MegaCrit.Sts2.Core.Saves.UserDataPathProvider).GetMethods().Where(m => m.Name is "GetProfileDir" or "GetProfileScopedBasePath"))
            GameBootstrap.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(RunHostCompare), nameof(ProfilePath)));
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitProfileId(1);
        GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer), "ReserveChoiceId"),
            prefix: new HarmonyMethod(typeof(RunHostCompare), nameof(ChoiceReserved)));
    }
    private static void ChoiceReserved() => _choiceReservations++;
    private static bool NoHoverTips(ref IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> __result) { __result = []; return false; }
    private static bool Completed(ref Task __result) { __result = Task.CompletedTask; return false; }
    private static bool ProfilePath(int profileId, ref string __result) { __result = $"profile{profileId}"; return false; }
    private sealed class DirectSelector(bool cancel = false) : MegaCrit.Sts2.Core.TestSupport.ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
            => Task.FromResult(cancel ? Enumerable.Empty<CardModel>() : options.Take(minSelect));
        public MegaCrit.Sts2.Core.TestSupport.CardRewardSelection GetSelectedCardReward(
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult> options,
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives)
            => throw new NotSupportedException("reward outside batch");
    }
}
