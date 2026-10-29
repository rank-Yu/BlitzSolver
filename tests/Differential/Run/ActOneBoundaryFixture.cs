// Explicit boundary fixture: start a real native Boss room with test-only monster HP=1.
// This is never a natural Act1 completion claim. Combat/pilot/rewards remain production code.
using System.Text.Json;
using BlitzSolver.OfflineHost;
using BlitzSolver.Autoplay;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;

namespace Differential;

internal static class ActOneBoundaryFixture
{
    private static int _floor, _ready, _votes, _rewardRequests;
    private static bool _requestContract = true;
    private static void Decision(RunView view)
    {
        if (view.Request.Kind != RunDecision.CombatReward) return;
        _rewardRequests++;
        bool legalReward = false;
        for (int i = 0; i < view.Request.OptionCount; i++)
        {
            RunDecisionOption option = view.Request.Options[i];
            if (option.Kind == RunDecisionOptionKind.Leave) _requestContract = false;
            if (option.Kind == RunDecisionOptionKind.Reward && option.Flags == RunDecisionOptionFlags.None) legalReward = true;
        }
        _requestContract &= legalReward;
    }
    private static BlitzSolver.OfflineHost.Runtime.MainLoopContext? _productionLoop;
    private static void Loop(BlitzSolver.OfflineHost.Runtime.MainLoopContext __instance) => _productionLoop = __instance;
    private static Task? _offer;
    private static void Ready() => _ready++;
    private static void Enqueue(GameAction action) { if (action is VoteToMoveToNextActAction) _votes++; }
    private static void Offer(Task __result) => _offer = __result;
    private static string Snapshot(NativeRun run, Player player) => NativeRunDump.Capture(run, player)
        + RunDump.Capture(RunStateImporter.Import(run, player))
        + JsonSerializer.Serialize(run.MapPointHistory, new JsonSerializerOptions { IncludeFields = true });
    internal static int Run(string mode)
    {
        if (mode is not ("act1" or "rooms-exact" or "rooms-more" or "budget"))
            throw new InvalidDataException("unknown act1 fixture mode");
        Harmony harmony = new("BlitzSolver.Differential.ActOneFixture");
        harmony.Patch(
            AccessTools.Method(typeof(BlitzSolver.OfflineHost.Runtime.NewRunFactory), "Create"),
            postfix: new HarmonyMethod(typeof(ActOneBoundaryFixture), nameof(Created)));
        harmony.Patch(AccessTools.Method(typeof(ActChangeSynchronizer), nameof(ActChangeSynchronizer.SetLocalPlayerReady)),
            prefix: new HarmonyMethod(typeof(ActOneBoundaryFixture), nameof(Ready)));
        harmony.Patch(AccessTools.Method(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue)),
            prefix: new HarmonyMethod(typeof(ActOneBoundaryFixture), nameof(Enqueue)));
        harmony.Patch(AccessTools.GetDeclaredConstructors(typeof(BlitzSolver.OfflineHost.Runtime.MainLoopContext)).Single(),
            postfix: new HarmonyMethod(typeof(ActOneBoundaryFixture), nameof(Loop)));
        Type policy = typeof(BlitzSolver.OfflineHost.Runtime.FirstCombatSession)
            .GetNestedType("SmokePolicy", System.Reflection.BindingFlags.NonPublic)!;
        harmony.Patch(AccessTools.Method(policy, "DecideAsync"),
            prefix: new HarmonyMethod(typeof(ActOneBoundaryFixture), nameof(Decision)));
        FirstCombatResult result = FirstCombat.Run(new(AssemblyBootstrap.Sts2DataDir,
            StopAfter: mode.StartsWith("rooms", StringComparison.Ordinal) ? "rooms" : "act1",
            MaxRooms: mode == "rooms-exact" ? 1 : 2, MaximumDecisions: mode == "budget" ? 1 : 256, TimeoutSeconds: 120));
        NativeRun run = RunManager.Instance.DebugOnlyGetState()!;
        bool completed = mode != "budget";
        bool correct = result.Status == (mode == "budget" ? "Budget" : "Paused")
            && result.Milestone == (mode == "act1" ? "act1-complete" : mode == "rooms-exact" ? "room-budget-reached" : null)
            && result.Detail == (mode == "act1" ? "act1-complete" : mode == "rooms-exact" ? "room-budget-reached"
                : mode == "rooms-more" ? "scope-act-end" : "decision-budget")
            && _requestContract && _rewardRequests > 0 && _ready == 0 && _votes == 0 && run.CurrentActIndex == 0 && run.ActFloor == _floor && run.CurrentRoom!.RoomType == RoomType.Boss
            && result.Combats is [{ Outcome: "victory", Deviations: 0, DeploymentFailures: 0 }]
            && (!completed || result.Rooms is [{ RoomType: "Boss" }]);
        string production = Snapshot(run, run.Players.Single());
        bool? oracleEqual = null;
        if (completed && correct)
        {
            oracleEqual = Direct(harmony) == production;
            correct &= oracleEqual.Value && _ready == 0 && _votes == 0;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { rewardRequestContract = _requestContract, rewardRequests = _rewardRequests, oracleEqual, readyCalls = _ready, voteActions = _votes, fixture = "boss-hp-one-not-natural", mode, correct, result,
            nativeActIndex = run.CurrentActIndex, nativeFloor = run.ActFloor, originalFloor = _floor },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return correct ? 0 : 1;
    }
    private static string Direct(Harmony harmony)
    {
        MainLoopContext loop = new(); SynchronizationContext.SetSynchronizationContext(loop);
        void Wait(Task task) => loop.RunUntilCompleted(task, TimeSpan.FromSeconds(120), "boss-direct-oracle");
        RunManager.Instance.CleanUp(false);
        if (CombatManager.Instance.DebugOnlyCurrentTurnLoopTask is { } oldLoop)
        {
            SynchronizationContext.SetSynchronizationContext(_productionLoop);
            try { _productionLoop!.RunUntilCompleted(oldLoop, TimeSpan.FromSeconds(5), "drain-fixture-turn-loop"); }
            catch (OperationCanceledException) when (oldLoop.IsCanceled) { } // Original teardown cancellation, observed before the next fixture run.
            SynchronizationContext.SetSynchronizationContext(loop);
        }
        var create = RunTestHost.StartNewRunWithUnlocksAsync(ModelDb.AllCharacters.Single(c => c.GetType().Name == "Ironclad"),
            0, "R3B2-DECISION", new UnlockState([new NeowEpoch().Id], [], 1));
        var prepared = Prepare(create); Wait(prepared); var (run, player) = prepared.Result;
        var ready = new OfflineCombatWaiter(loop).WaitForPlayableCombatAsync(); Wait(ready);
        var combat = ready.Result;
        Rules rules = RulesCompiler.Compile(combat, player, run);
        LiveCombatTracker tracker = new(combat); Battle opening = tracker.ImportRoot(combat, player, rules); tracker.Observe(opening);
        TerminalBattleCapture.Install();
        NativePilotHost native = new(loop, combat, player, rules, tracker, opening, SearchPolicyCapture.Capture(combat, player),
            SearchPerformanceProfile.For(SearchPerformanceTier.Low), OfflineCombatSettings.DefaultMemoryBudgetBytes, widening: true);
        var fight = CombatPilot.RunAsync(native, "boss-direct", default); Wait(fight);
        if (fight.Result.Outcome != "victory" || fight.Result.Deviations != 0 || fight.Result.DeploymentFailures != 0)
            throw new InvalidDataException("boss-direct-native-combat");
        Wait(RunManager.Instance.ActionExecutor.FinishedExecutingActions());
        harmony.Patch(AccessTools.Method(typeof(RewardsSet), nameof(RewardsSet.Offer)),
            postfix: new HarmonyMethod(typeof(ActOneBoundaryFixture), nameof(Offer)));
        RewardsSet.testSelector = async set =>
        {
            foreach (var reward in set.Rewards) await RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
        };
        try
        {
            using (CardSelectCmd.UseSelector(new DirectSelector()))
            {
                Wait(((CombatRoom)run.CurrentRoom!).OfferRoomEndRewards());
                Wait(_offer ?? throw new InvalidDataException("boss-direct-offer"));
            }
        }
        finally { RewardsSet.testSelector = null; }
        Wait(RunManager.Instance.ActionExecutor.FinishedExecutingActions());
        return Snapshot(run, player);
    }
    private sealed class DirectSelector : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards, int min, int max) => Task.FromResult(cards.Take(Math.Max(1, min)));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult> cards,
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives) => new() { card = cards[0].Card };
    }
    private static void Created(ref Task<(NativeRun Run, Player Player)> __result) => __result = Prepare(__result);
    private static async Task<(NativeRun Run, Player Player)> Prepare(Task<(NativeRun Run, Player Player)> created)
    {
        var pair = await created;
        await RunManager.Instance.EnterRoomDebug(RoomType.Boss, model: ModelDb.Encounter<CeremonialBeastBoss>().ToMutable(), showTransition: false);
        CombatState state = CombatManager.Instance.DebugOnlyGetState() ?? throw new InvalidDataException("fixture-boss-state");
        foreach (var enemy in state.Enemies) enemy.SetCurrentHpInternal(1);
        _floor = pair.Run.ActFloor;
        return pair;
    }
}
