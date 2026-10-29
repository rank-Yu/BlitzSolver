// Original treasure commands and UI gameplay responsibility are an independent native oracle.
// Debug rooms and hook fault injection below are fixtures, never natural-run victories.
using System.Text.Json;
using Sts2Solver.Ai.Run;
using Sts2Solver.Autoplay;
using Sts2Solver.Import;
using Sts2Solver.OfflineHost.Run;
using Sts2Solver.Simulation.Run;
using Sts2Solver.TestHost;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;
using Bootstrap = Sts2Solver.OfflineHost.Runtime.GameBootstrap;

namespace Differential;

internal static class TreasureHostChecks
{
    private static MainLoopContext _loop = null!;
    private static int _checks, _cases;
    private static bool _extra;
    private static TaskCompletionSource? _goldDelay;
    private static ExecutionContext? _late;
    private static void Wait(Task task) => _loop.RunUntilCompleted(task, TimeSpan.FromSeconds(60), "treasure-host");
    private static T Get<T>(Task<T> task) { Wait(task); return task.GetAwaiter().GetResult(); }
    private static void Check(bool valid, string reason) { _checks++; if (!valid) throw new InvalidDataException("treasure-host:" + reason); }
    private static string State(NativeRun run, Player player) => NativeRunDump.Capture(run, player)
        + RunDump.Capture(RunStateImporter.Import(run, player))
        + JsonSerializer.Serialize(run.MapPointHistory, new JsonSerializerOptions { IncludeFields = true });
    private static RunPilotAnswer Pick(RunView view, int index = 0)
        => new(RunDecisionProtocol.Select(view.Request, [view.Request.Options[index].OptionId]), true);
    private static (NativeRun Run, Player Player, TreasureRoom Room) Start(bool empty = false)
    {
        if (RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(false);
        var created = RunTestHost.StartNewRunBeforeChoicesAsync(ModelDb.AllCharacters.Single(c => c.GetType().Name == "Ironclad"),
            0, "R3B8-TREASURE", UnlockState.none);
        var (run, player) = Get(created);
        if (empty) player.AddRelicInternal(ModelDb.Relic<SilverCrucible>().ToMutable());
        Wait(RunManager.Instance.EnterRoomDebug(RoomType.Treasure, showTransition: false));
        return (run, player, (TreasureRoom)run.CurrentRoom!);
    }
    internal static int Run()
    {
        _loop = new(); SynchronizationContext.SetSynchronizationContext(_loop);
        Bootstrap.ApplyGodotBypasses(); Bootstrap.SkipGodotNodeStaticConstructors(); Bootstrap.InitializeStaticState();
        foreach (string mode in new[] { "take", "skip", "empty", "second" }) Normal(mode);
        Duplicates();
        Faults();
        Console.WriteLine($"TREASUREHOST status=pass cases={_cases} checks={_checks}");
        return 0;
    }
    private static void Normal(string mode)
    {
        var (run, player, room) = Start(mode == "empty");
        OfflineRunHost host = null!;
        host = new OfflineRunHost(run, player, _ => ValueTask.FromResult(hostBoundary()));
        OfflineRunBoundary hostBoundary() => host!.TreasureProceedConfirmed(room)
            ? new(OfflineBoundaryKind.Paused, Detail: "treasure-complete") : new(OfflineBoundaryKind.Treasure, Treasure: room);
        int choices = 0, awardEvents = 0;
        void ObserveAward(List<RelicPickingResult> _) => awardEvents++;
        RunManager.Instance.TreasureRoomRelicSynchronizer.RelicsAwarded += ObserveAward;
        var result = Get(RunPilot.RunAsync(host, new Policy((view, _) =>
        {
            choices++;
            if (choices == 1) Check(view.Request.Kind == RunDecision.Treasure && view.Request.OptionCount == 1
                && view.Request.Options[0].Kind == RunDecisionOptionKind.Open
                && view.Request.Options[0].PayloadKind == RunDecisionPayloadKind.None, "closed chest never reveals generated relic");
            int index = mode == "skip" && view.Request.Kind == RunDecision.RelicSelect ? view.Request.OptionCount - 1 : 0;
            return ValueTask.FromResult(Pick(view, index));
        })));
        Check(result.Reason == RunStopReason.Paused && choices == (mode == "empty" ? 2 : 3), "real open/relic/leave stages " + mode);
        Check(mode != "skip" || RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics != null, "singleplayer skip retains native session until Exit");
        string actual = State(run, player);
        if (mode == "second")
        {
            Wait(RunManager.Instance.EnterRoomDebug(RoomType.Treasure, showTransition: false));
            room = (TreasureRoom)run.CurrentRoom!;
            result = Get(RunPilot.RunAsync(host, new Policy((view, _) => ValueTask.FromResult(Pick(view)))));
            Check(result.Reason == RunStopReason.Paused && host.TreasureProceedConfirmed(room), "second room gets a fresh chest lifecycle");
            actual = State(run, player);
        }
        Check(awardEvents == (mode == "skip" ? 0 : mode == "second" ? 2 : 1), "native award event count including empty chest " + mode);
        RunManager.Instance.TreasureRoomRelicSynchronizer.RelicsAwarded -= ObserveAward;
        Wait(host.DisposeAsync().AsTask());
        var (direct, dp, dr) = Start(mode == "empty");
        Direct(dp, dr, mode == "skip");
        if (mode == "second")
        {
            Wait(RunManager.Instance.EnterRoomDebug(RoomType.Treasure, showTransition: false));
            Direct(dp, (TreasureRoom)direct.CurrentRoom!, false);
        }
        Check(actual == State(direct, dp), "independent original treasure commands/awards/full dump/history/RNG " + mode);
        if (mode == "skip")
        {
            Wait(RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.ThisOrThat>(), showTransition: false));
            Check(RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics == null, "native Exit clears skipped chest");
        }
        _cases++;
    }
    private static void Direct(Player player, TreasureRoom room, bool skip)
    {
        Wait(room.DoNormalRewards());
        Wait(room.DoExtraRewardsIfNeeded());
        var sync = RunManager.Instance.TreasureRoomRelicSynchronizer;
        if (sync.CurrentRelics is not { Count: > 0 }) sync.CompleteWithNoRelics();
        else
        {
            List<RelicPickingResult>? results = null;
            void Award(List<RelicPickingResult> r) => results = r;
            sync.RelicsAwarded += Award;
            try
            {
                if (skip) sync.SkipRelicLocally(); else sync.PickRelicLocally(0);
                Wait(RunManager.Instance.ActionExecutor.FinishedExecutingActions());
            }
            finally { sync.RelicsAwarded -= Award; }
            if (!skip)
            {
                foreach (RelicPickingResult result in results ?? throw new InvalidDataException("oracle-award-missing"))
                {
                    RelicModel mutable = result.relic.ToMutable();
                    if (result.type != RelicPickingResultType.Skipped) Wait(RelicCmd.Obtain(mutable, result.player!));
                    if (!ReferenceEquals(player, result.player)) player.RelicGrabBag.MoveToFallback(result.relic);
                }
            }
        }
        Wait(RunManager.Instance.ProceedFromTerminalRewardsScreen());
    }
    private static void Duplicates()
    {
        var (run, player, room) = Start();
        var host = new OfflineRunHost(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Treasure, Treasure: room)));
        ValueTask<RunPilotAnswer> NoChild(RunView _, CancellationToken token) => throw new InvalidDataException("unexpected-treasure-child");
        var open = Get(host.WaitForBoundaryAsync(default).AsTask());
        Wait(host.ExecuteAsync(Pick(open.View), NoChild, default).AsTask());
        var reveal = Get(host.WaitForBoundaryAsync(default).AsTask());
        bool replay = false;
        try { Wait(host.ExecuteAsync(Pick(open.View), NoChild, default).AsTask()); }
        catch (InvalidDataException e) when (e.Message.StartsWith("action-token", StringComparison.Ordinal)) { replay = true; }
        Check(replay, "duplicate Open cannot spend native gold/RNG again");
        Wait(host.ExecuteAsync(Pick(reveal.View), NoChild, default).AsTask());
        bool duplicate = false;
        try { Wait(host.ExecuteAsync(Pick(reveal.View), NoChild, default).AsTask()); }
        catch (InvalidDataException e) when (e.Message == "host-no-pending") { duplicate = true; }
        Check(duplicate, "duplicate pick cannot obtain relic twice");
        Wait(host.DisposeAsync().AsTask()); _cases++;

        (run, player, room) = Start();
        host = new(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Treasure, Treasure: room)));
        open = Get(host.WaitForBoundaryAsync(default).AsTask());
        Wait(host.ExecuteAsync(Pick(open.View), NoChild, default).AsTask());
        reveal = Get(host.WaitForBoundaryAsync(default).AsTask());
        RunManager.Instance.TreasureRoomRelicSynchronizer.CompleteWithNoRelics();
        bool stale = false;
        try { Wait(host.ExecuteAsync(Pick(reveal.View), NoChild, default).AsTask()); }
        catch (InvalidDataException e) when (e.Message.StartsWith("native-stale", StringComparison.Ordinal)) { stale = true; }
        Check(stale && player.Relics.Count == 1, "stale revealed relic instance cannot be claimed");
        Wait(host.DisposeAsync().AsTask()); _cases++;
    }
    private static void Faults()
    {
        Harmony harmony = new("Sts2Solver.Differential.TreasureFaults");
        harmony.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.ModifyRewards)),
            prefix: new HarmonyMethod(typeof(TreasureHostChecks), nameof(ModifyRewards)));
        harmony.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.AfterGoldGained)),
            postfix: new HarmonyMethod(typeof(TreasureHostChecks), nameof(DelayGold)));
        // Stale hidden generation changes cannot make an old Open token execute a different chest.
        var (run, player, room) = Start();
        var host = new OfflineRunHost(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Treasure, Treasure: room)));
        var boundary = Get(host.WaitForBoundaryAsync(default).AsTask());
        RunManager.Instance.TreasureRoomRelicSynchronizer.CompleteWithNoRelics();
        bool rejected = false;
        try { Wait(host.ExecuteAsync(Pick(boundary.View), (_, _) => throw new InvalidDataException("unexpected-child"), default).AsTask()); }
        catch (InvalidDataException e) when (e.Message.Contains("native-stale", StringComparison.Ordinal)) { rejected = true; }
        Check(rejected, "stale closed chest rejected"); Wait(host.DisposeAsync().AsTask()); _cases++;
        // A real nonempty generated extra set fails explicitly before TestMode's automatic selection.
        (run, player, room) = Start();
        host = new(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Treasure, Treasure: room)));
        boundary = Get(host.WaitForBoundaryAsync(default).AsTask()); _extra = true; int gold = player.Gold;
        rejected = false;
        try { Wait(host.ExecuteAsync(Pick(boundary.View), (_, _) => throw new InvalidDataException("unexpected-child"), default).AsTask()); }
        catch (Sts2Solver.Simulation.Combat.UnsupportedException e) when (e.Message.Contains("treasure-extra", StringComparison.Ordinal)) { rejected = true; }
        finally { _extra = false; }
        Check(rejected && player.Gold > gold && player.Gold < gold + 60 && player.Relics.Count == 1, "normal native gold preserved; unknown extra reward never auto-selected");
        Wait(host.DisposeAsync().AsTask()); _cases++;
        // Cancellation drains original noncancelable hook before owner/selector/scope can be released.
        (run, player, room) = Start();
        host = new(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Treasure, Treasure: room)));
        boundary = Get(host.WaitForBoundaryAsync(default).AsTask()); _goldDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        Task executing = host.ExecuteAsync(Pick(boundary.View), (_, _) => throw new InvalidDataException("unexpected-child"), cancellation.Token).AsTask();
        Check(!executing.IsCompleted && _late != null, "native gold hook truly pending");
        cancellation.Cancel(); Task disposing = host.DisposeAsync().AsTask();
        Check(!executing.IsCompleted && !disposing.IsCompleted, "cancel/dispose retain owner until original parent settles");
        bool busy = false;
        try { _ = new OfflineRunHost(run, player, _ => ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Paused))); }
        catch (InvalidDataException e) when (e.Message == "host-native-session-busy") { busy = true; }
        Check(busy, "no new host can share unfinished native action");
        _goldDelay.SetResult(); _goldDelay = null;
        bool cancelled = false;
        try { Wait(executing); } catch (OperationCanceledException) { cancelled = true; }
        Wait(disposing); Check(cancelled, "cancel after original settle is cancellation");
        bool lateRejected = false;
        ExecutionContext.Run(_late!, _ =>
        {
            try { new RewardsSet(player).WithCustomRewards([new GoldReward(7, 7, player)]).Offer().GetAwaiter().GetResult(); }
            catch (Sts2Solver.Simulation.Combat.UnsupportedException) { lateRejected = true; }
        }, null);
        // Captured general execution context is closed; it may not fall through to automatic reward selection.
        Check(lateRejected, "closed native execution rejects a late reward offer"); _late = null; _cases++;
    }
    private static void ModifyRewards(Player player, List<Reward> rewards)
    { if (_extra) rewards.Add(new GoldReward(77, 77, player)); }
    private static void DelayGold(ref Task __result)
    {
        if (_goldDelay == null) return;
        _late = ExecutionContext.Capture(); __result = Task.WhenAll(__result, _goldDelay.Task);
    }
    private sealed class Policy(Func<RunView, CancellationToken, ValueTask<RunPilotAnswer>> callback) : IRunPolicy
    { public ValueTask<RunPilotAnswer> DecideAsync(RunView view, CancellationToken token) => callback(view, token); }
}
