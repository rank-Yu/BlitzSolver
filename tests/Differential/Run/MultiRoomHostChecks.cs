// Production shared-policy rest lifecycle fixture. MiniatureTent preserves the remaining
// native options; cancel is not success, and the original synchronizer owns completion/history.
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rewards;
using BlitzSolver.Ai.Run;
using BlitzSolver.Autoplay;
using BlitzSolver.Import;
using BlitzSolver.OfflineHost.Run;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;
using Bootstrap = BlitzSolver.OfflineHost.Runtime.GameBootstrap;

namespace Differential;

internal static class MultiRoomHostChecks
{
    private static MainLoopContext _loop = null!;
    private static int _checks;
    private static string? _mode;
    private static CancellationTokenSource? _cancellation;
    private static int _begins;
    private static ExecutionContext? _capturedRest;
    private static void Wait(Task task) => _loop.RunUntilCompleted(task, TimeSpan.FromSeconds(60), "multiroom-rest");
    private static void Check(bool valid, string detail) { _checks++; if (!valid) throw new InvalidDataException("multiroom-rest:" + detail); }
    private static (NativeRun Run, Player Player, RestSiteRoom Room) Start(bool tent = true)
    {
        if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false);
        var create = RunTestHost.StartNewRunBeforeChoicesAsync(ModelDb.AllCharacters.Single(c => c.GetType().Name == "Ironclad"),
            0, "R3B7-REST", UnlockState.none);
        Wait(create); var (run, player) = create.Result;
        if (tent) player.AddRelicInternal(ModelDb.Relic<MiniatureTent>().ToMutable());
        Wait(RunManager.Instance.EnterRoomDebug(RoomType.RestSite, showTransition: false));
        return (run, player, (RestSiteRoom)run.CurrentRoom!);
    }
    private static string State(NativeRun run, Player player) => NativeRunDump.Capture(run, player)
        + RunDump.Capture(RunStateImporter.Import(run, player))
        + JsonSerializer.Serialize(run.MapPointHistory, new JsonSerializerOptions { IncludeFields = true });
    private static RunPilotAnswer Pick(RunView view, int index = 0)
        => new(RunDecisionProtocol.Select(view.Request, [view.Request.Options[index].OptionId]), true);
    internal static int Run()
    {
        _loop = new(); SynchronizationContext.SetSynchronizationContext(_loop);
        Bootstrap.ApplyGodotBypasses(); Bootstrap.SkipGodotNodeStaticConstructors(); Bootstrap.InitializeStaticState();
        var (run, player, room) = Start();
        OfflineRunHost host = new(run, player, _ => ValueTask.FromResult(room.Options.Count == 0
            ? new OfflineRunBoundary(OfflineBoundaryKind.Paused, Detail: "rest-native-complete")
            : new OfflineRunBoundary(OfflineBoundaryKind.Rest, Rest: room)));
        int parents = 0, children = 0, upgrades = player.Deck.Cards.Sum(c => c.CurrentUpgradeLevel);
        ValueTask<RunPilotAnswer> Decide(RunView view, CancellationToken token)
        {
            if (view.Request.Kind == RunDecision.CardSelect)
            {
                Check(view.Request.SelectionPurpose == RunSelectionPurpose.Upgrade, "shop remove purpose cannot leak into Smith");
                children++;
                return ValueTask.FromResult(children == 1 ? new RunPilotAnswer(RunDecisionProtocol.Cancel(view.Request)) : Pick(view));
            }
            parents++;
            if (parents == 2)
                Check(room.Options.Count == 2 && player.Deck.Cards.Sum(c => c.CurrentUpgradeLevel) == upgrades
                    && run.CurrentMapPointHistoryEntry!.GetEntry(player.NetId).RestSiteChoices.Count == 0, "cancel leaves room unfinished with all remaining options");
            if (parents == 3) Check(room.Options.Count == 1, "successful Smith retains remaining native option under MiniatureTent");
            int index = parents <= 2
                ? Enumerable.Range(0, room.Options.Count).Single(i => room.Options[i] is SmithRestSiteOption) : 0;
            return ValueTask.FromResult(Pick(view, index));
        }
        var pilot = RunPilot.RunAsync(host, new Policy(Decide)); Wait(pilot);
        Check(pilot.Result.Reason == RunStopReason.Paused && pilot.Result.Detail == "rest-native-complete"
            && pilot.Result.Decisions == 5 && children == 2, "single pilot counts parent/async child decisions");
        Check(room.Options.Count == 0 && RunManager.Instance.RestSiteSynchronizer.AfterAllRestSitesCompleted().IsCompleted
            && run.CurrentMapPointHistoryEntry!.GetEntry(player.NetId).RestSiteChoices.Count == 2, "real synchronizer completion/history after all options");
        string actual = State(run, player); Wait(host.DisposeAsync().AsTask());
        var (direct, dp, dr) = Start();
        using IDisposable scope = CardSelectCmd.UseSelector(new Selector());
        Wait(RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(Enumerable.Range(0, dr.Options.Count).Single(i => dr.Options[i] is SmithRestSiteOption)));
        Wait(RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(Enumerable.Range(0, dr.Options.Count).Single(i => dr.Options[i] is SmithRestSiteOption)));
        Wait(RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(0));
        Check(actual == State(direct, dp), "independent original rest choices/RunDump/native history/RNG");
        scope.Dispose(); Check(CardSelectCmd.Selector == null, "selectors restored");
        RunManager.Instance.CleanUp(false);
        Faults();
        Console.WriteLine($"MULTIROOMHOST status=pass cases=6 checks={_checks}"); return 0;
    }
    private static void Faults()
    {
        Harmony harmony = new("BlitzSolver.Differential.RestFaults");
        harmony.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.ModifyRewards)),
            prefix: new HarmonyMethod(typeof(MultiRoomHostChecks), nameof(ModifyRewards)));
        harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.RewardsSetSynchronizer), "BeginRewardsSet"),
            postfix: new HarmonyMethod(typeof(MultiRoomHostChecks), nameof(Begin)));
        foreach (string mode in new[] { "generated", "nested", "failure", "cancel", "normal" })
        {
            var (run, player, room) = Start(tent: false);
            var host = new OfflineRunHost(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Rest, Rest: room)));
            var pending = host.WaitForBoundaryAsync(default).AsTask(); Wait(pending);
            int heal = Enumerable.Range(0, room.Options.Count).Single(i => room.Options[i] is HealRestSiteOption);
            int gold = player.Gold;
            _mode = mode; _begins = 0; _cancellation = new CancellationTokenSource();
            bool rejected = false, cancelled = false;
            try
            {
                Wait(host.ExecuteAsync(Pick(pending.Result.View, heal), (_, _) => throw new InvalidDataException("unexpected-rest-child"),
                    _cancellation.Token).AsTask());
            }
            catch (BlitzSolver.Simulation.Combat.UnsupportedException e) when (mode is "generated" or "nested"
                && e.Message.Contains(mode == "generated" ? "host-unsupported-rest-rewards" : "host-unsupported-native-entry", StringComparison.Ordinal)) { rejected = true; }
            catch (InvalidDataException e) when (mode == "failure" && e.Message == "rest-hook-failure") { rejected = true; }
            catch (OperationCanceledException) when (mode == "cancel") { cancelled = true; }
            finally { _mode = null; _cancellation.Dispose(); _cancellation = null; }
            Check(mode is "generated" or "nested" or "failure" ? rejected && _begins == 0 : mode == "cancel" ? cancelled : _begins == 1,
                "native rest result and before-selection guard " + mode);
            Check(player.Gold == gold && !RewardPresentation.EmptyRestRewardsActive && CardSelectCmd.Selector == null,
                "empty reward scope/selector restored without automatic reward " + mode);
            if (mode == "normal") Check(room.Options.Count == 0 && RunManager.Instance.RestSiteSynchronizer.AfterAllRestSitesCompleted().IsCompleted,
                "ordinary Heal truly completes synchronizer");
            Wait(host.DisposeAsync().AsTask());
            if (mode == "normal")
            {
                bool lateRejected = false;
                ExecutionContext.Run(_capturedRest ?? throw new InvalidDataException("missing-captured-rest-context"), _ =>
                {
                    try { new RewardsSet(player).WithCustomRewards([new GoldReward(7, 7, player)]).Offer().GetAwaiter().GetResult(); }
                    catch (BlitzSolver.Simulation.Combat.UnsupportedException e) when (e.Message.Contains("host-unsupported-native-entry", StringComparison.Ordinal)) { lateRejected = true; }
                }, null);
                Check(lateRejected && player.Gold == gold, "closed captured rest context rejects late Offer before TestMode automatic reward");
                _capturedRest = null;
            }
            RunManager.Instance.CleanUp(false);
        }
    }
    private static void Begin() { if (_mode != null) _begins++; }
    private static void ModifyRewards(Player player, List<Reward> rewards)
    {
        if (_mode == "generated") rewards.Add(new GoldReward(7, 7, player));
        if (_mode == "nested") new RewardsSet(player).Offer().GetAwaiter().GetResult();
        if (_mode == "failure") throw new InvalidDataException("rest-hook-failure");
        if (_mode == "cancel") _cancellation!.Cancel();
        if (_mode == "normal") _capturedRest = ExecutionContext.Capture();
    }
    private sealed class Policy(Func<RunView, CancellationToken, ValueTask<RunPilotAnswer>> callback) : IRunPolicy
    { public ValueTask<RunPilotAnswer> DecideAsync(RunView view, CancellationToken token) => callback(view, token); }
    private sealed class Selector : ICardSelector
    {
        private int _calls;
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards, int min, int max)
            => Task.FromResult(++_calls == 1 ? Enumerable.Empty<CardModel>() : cards.Take(Math.Max(1, min)));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult> cards,
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives) => throw new InvalidDataException("unexpected-reward");
    }
}
