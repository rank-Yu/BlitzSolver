// Whole-combat automation: search, deploy the current turn, compare every action, reconcile at each
// player turn start, and re-search on a mismatch. The turn loop and failure policy follow
// CombatSolver src/Runtime/SolverController.cs at a62f1850 (MIT, Copyright (c) 2026 Torch1230 and
// contributors), reduced to one search and one deployment at a time.
using System.Diagnostics;
using BlitzSolver.Engine;
using BlitzSolver.Import;
using BlitzSolver.Search;
using BlitzSolver.TestHost;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace BlitzSolver;

internal sealed record AutoSearchEntry(
    int Turn,
    string Reason,
    string Status,
    long ElapsedMs,
    int ExpandedNodes,
    int RouteActions,
    bool Victory,
    string? Error);

internal sealed record AutoDeviationEntry(
    int Turn,
    int ActionIndex,
    string Action,
    string[] Fields);

internal sealed record AutoFailureEntry(
    int Turn,
    string Reason,
    string Detail);

internal sealed record AutoCombatResult(
    string Root,
    string Status,
    string Outcome,
    int Turns,
    int PlayerHp,
    int PlayerMaxHp,
    string[] PotionsUsed,
    int Searches,
    int ReusedTurns,
    int Deviations,
    int DeploymentFailures,
    long ElapsedMs,
    string? PauseReason,
    AutoSearchEntry[] SearchLog,
    AutoDeviationEntry[] DeviationLog,
    AutoFailureEntry[] FailureLog);

/// <summary>
/// The single full-auto session. Ordinary F8 toggles it; the test protocol awaits it. All game
/// reads/writes happen on the main thread; the only background work is <see cref="SearchSession"/>'s
/// solver worker.
/// </summary>
internal static class AutoCombatSession
{
    private const int MaximumConsecutiveFailures = 3;
    private const int MaximumTurns = 200;

    private static readonly object Gate = new();
    private static CancellationTokenSource? _cancellation;
    private static string? _cancelReason;
    private static int _running;

    internal static bool IsRunning => Volatile.Read(ref _running) != 0;

    /// <summary>F8 entry: start full auto when idle, stop it when running.</summary>
    internal static void Toggle(NGame host)
    {
        if (!NGame.IsMainThread())
        {
            ModLog.Error("[BlitzSolver] AUTO_REJECTED reason=not_main_thread");
            return;
        }
        if (IsRunning)
        {
            Cancel("hotkey");
            ModLog.Info("[BlitzSolver] AUTO_STOP_REQUESTED reason=hotkey");
            return;
        }
        if (TestSession.Active)
        {
            ModLog.Info("[BlitzSolver] AUTO_REJECTED reason=test_session_active");
            return;
        }
        CombatState? combat = CombatManager.Instance.DebugOnlyGetState();
        if (combat == null || !CombatManager.Instance.IsInProgress)
        {
            ModLog.Info("[BlitzSolver] AUTO_REJECTED reason=no_active_combat");
            return;
        }
        Player? player = LocalContext.GetMe(combat);
        if (player?.PlayerCombatState is null
            || combat.CurrentSide != CombatSide.Player
            || player.PlayerCombatState.Phase != PlayerTurnPhase.Play)
        {
            ModLog.Info("[BlitzSolver] AUTO_REJECTED reason=not_playable");
            return;
        }
        _ = RunDetachedAsync(host, combat);
    }

    private static async Task RunDetachedAsync(NGame host, CombatState combat)
    {
        try
        {
            AutoCombatResult result = await RunAsync(host, combat, request: null, root: "manual",
                SearchPerformanceTier.Medium, CancellationToken.None);
            ModLog.Info(
                $"[BlitzSolver] AUTO_DONE root=manual status={result.Status} outcome={result.Outcome} " +
                $"turns={result.Turns} hp={result.PlayerHp}/{result.PlayerMaxHp} " +
                $"searches={result.Searches} reused={result.ReusedTurns} " +
                $"deviations={result.Deviations} failures={result.DeploymentFailures} " +
                $"elapsed_ms={result.ElapsedMs}");
        }
        catch (UnsupportedImportException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_FAILED root=manual reason={error.Reason} {error.Detail}");
        }
        catch (UnsupportedException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_FAILED root=manual unsupported {error.Message}");
        }
        catch (CapacityException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_FAILED root=manual capacity region={error.Region} {error.Message}");
        }
        catch (NotSupportedException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_FAILED root=manual unsupported {error.Message}");
        }
        catch (InvalidDataException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_FAILED root=manual invalid_data {error.Message}");
        }
        catch (InvalidOperationException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_FAILED root=manual invalid_operation {error.Message}");
        }
        catch (TimeoutException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_FAILED root=manual timeout {error.Message}");
        }
        catch (OperationCanceledException)
        {
            ModLog.Info($"[BlitzSolver] AUTO_CANCELLED root=manual reason={Reason() ?? "cancel"}");
        }
    }

    internal static async Task<AutoCombatResult> RunAsync(NGame host, CombatState combat,
        OfflineRequest? request, string root, SearchPerformanceTier tier, CancellationToken external,
        int? cancelAfterMilliseconds = null)
    {
        if (!NGame.IsMainThread())
            throw new InvalidOperationException("全自动必须从游戏主线程启动。");
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("已有全自动会话在运行。");
        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(external);
        lock (Gate)
        {
            _cancellation = cancellation;
            _cancelReason = null;
        }
        Stopwatch total = Stopwatch.StartNew();
        List<AutoSearchEntry> searchLog = [];
        List<AutoDeviationEntry> deviationLog = [];
        List<AutoFailureEntry> failureLog = [];
        List<string> potionsUsed = [];
        int searches = 0;
        int reused = 0;
        int deviations = 0;
        int failures = 0;
        int deploymentFailures = 0;
        int consecutiveFailures = 0;
        int failureTurn = -1;
        int lastTurn = 0;
        int lastHp = 0;
        int lastMaxHp = 0;
        string? pauseReason = null;
        try
        {
            Player player = LocalContext.GetMe(combat)
                ?? throw new InvalidOperationException("找不到本地玩家。");
            if (combat.RunState is not RunState runState)
                throw new InvalidDataException("原生战斗的 RunState 不是具体 RunState。");
            Rules rules = RulesCompiler.Compile(combat, player, runState);
            LiveCombatTracker tracker = new(combat);
            Battle initial = tracker.ImportRoot(combat, player, rules);
            tracker.Observe(initial);
            lastTurn = player.PlayerCombatState?.TurnNumber ?? 0;
            lastHp = player.Creature.CurrentHp;
            lastMaxHp = player.Creature.MaxHp;
            ReplayedRoute? route = null;
            int index = 0;
            int? settledTurn = null;
            ModLog.Info(
                $"[BlitzSolver] AUTO_START root={root} turn={lastTurn} hp={lastHp}/{lastMaxHp} " +
                $"enemies={tracker.RootEnemyCount} tier={tier}");
            try
            {
            while (true)
            {
                if (cancelAfterMilliseconds is { } cancelLimit
                    && total.ElapsedMilliseconds >= cancelLimit)
                {
                    Cancel("test_cancel_after_ms");
                }
                cancellation.Token.ThrowIfCancellationRequested();
                CombatState? liveState = CombatManager.Instance.DebugOnlyGetState();
                if (liveState == null || !CombatManager.Instance.IsInProgress)
                    break;
                Player? livePlayer = LocalContext.GetMe(liveState);
                if (livePlayer?.PlayerCombatState is not { } playerState)
                {
                    await GodotCombatWaiter.NextFrameAsync(host);
                    continue;
                }
                if (liveState.CurrentSide != CombatSide.Player
                    || playerState.Phase != PlayerTurnPhase.Play
                    || !RunManager.Instance.ActionExecutor.FinishedExecutingActions().IsCompleted)
                {
                    await GodotCombatWaiter.NextFrameAsync(host);
                    continue;
                }
                int turn = playerState.TurnNumber;
                if (turn > MaximumTurns)
                {
                    pauseReason = $"回合数超过上限 {MaximumTurns}";
                    failures++;
                    deploymentFailures++;
                    failureLog.Add(new AutoFailureEntry(turn, "turn_limit", pauseReason));
                    break;
                }
                if (settledTurn != turn)
                {
                    await GodotCombatWaiter.SettleAsync(host);
                    settledTurn = turn;
                }
                liveState = CombatManager.Instance.DebugOnlyGetState();
                if (liveState == null || !CombatManager.Instance.IsInProgress)
                    break;
                livePlayer = LocalContext.GetMe(liveState);
                if (livePlayer?.PlayerCombatState is not { } settledPlayerState)
                    continue;
                player = livePlayer;
                playerState = settledPlayerState;
                turn = playerState.TurnNumber;
                lastTurn = turn;
                lastHp = player.Creature.CurrentHp;
                lastMaxHp = player.Creature.MaxHp;
                combat = liveState;
                if (failureTurn != turn)
                {
                    failureTurn = turn;
                    consecutiveFailures = 0;
                }
                if (route == null || index >= route.Count || route[index].Before.PlayerTurn != turn)
                {
                    (route, index) = await SearchAsync(
                        host, combat, player, rules, tracker, request, root, tier, turn,
                        route == null ? "initial" : "turn_start",
                        searchLog, failureLog, cancellation.Token);
                    searches++;
                    if (route == null)
                    {
                        failures++;
                        deploymentFailures++;
                        consecutiveFailures++;
                        if (consecutiveFailures >= MaximumConsecutiveFailures)
                        {
                            pauseReason = $"第 {turn} 回合连续 {consecutiveFailures} 次搜索/部署失败";
                            break;
                        }
                        continue;
                    }
                    if (route.Count == 0 || route[0].Before.PlayerTurn != turn)
                    {
                        failures++;
                        deploymentFailures++;
                        consecutiveFailures++;
                        failureLog.Add(new AutoFailureEntry(turn, "route_turn",
                            $"路线首动作回合 {(route.Count == 0 ? 0 : route[0].Before.PlayerTurn)} 与当前 {turn} 不符。"));
                        route = null;
                        if (consecutiveFailures >= MaximumConsecutiveFailures)
                        {
                            pauseReason = $"第 {turn} 回合连续 {consecutiveFailures} 次搜索/部署失败";
                            break;
                        }
                        continue;
                    }
                    if (index == 0)
                        tracker.Observe(route[0].Before);
                }
                else if (index > 0)
                {
                    Battle live = tracker.ImportForComparison(combat, player, rules, route[index].Before);
                    if (!BattleReconciler.Same(route[index].Before, live))
                    {
                        string[] diff = BattleReconciler.Diff(route[index].Before, live);
                        deviations++;
                        failures++;
                        consecutiveFailures++;
                        deviationLog.Add(new AutoDeviationEntry(turn, index,
                            Describe(route[index].Action), diff));
                        ModLog.Error(
                            $"[BlitzSolver] TURN_DEVIATION turn={turn} index={index} " +
                            $"action={Describe(route[index].Action)} diff={string.Join(" | ", diff)}");
                        route = null;
                        if (consecutiveFailures >= MaximumConsecutiveFailures)
                        {
                            pauseReason = $"第 {turn} 回合连续 {consecutiveFailures} 次预测偏差/部署失败";
                            break;
                        }
                        continue;
                    }
                    reused++;
                    ModLog.Info($"[BlitzSolver] TURN_REUSE turn={turn} index={index} " +
                        $"remaining={route.Count - index}");
                }

                bool turnEnded = false;
                bool finished = false;
                ActionDeployer deployer = new(host, combat, player, rules, tracker);
                while (index < route.Count && route[index].Before.PlayerTurn == turn)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    ReplayedAction action = route[index];
                    Battle actual;
                    try
                    {
                        actual = await deployer.ExecuteAsync(action, cancellation.Token);
                    }
                    catch (DeploymentFailureException error)
                    {
                        failures++;
                        deploymentFailures++;
                        consecutiveFailures++;
                        failureLog.Add(new AutoFailureEntry(turn, error.Reason, error.Message));
                        ModLog.Error(
                            $"[BlitzSolver] DEPLOY_FAILURE turn={turn} index={index} " +
                            $"action={Describe(action.Action)} reason={error.Reason} detail={error.Message}");
                        route = null;
                        break;
                    }
                    bool terminal = action.After.Outcome != Outcome.None;
                    if (!terminal && !BattleReconciler.Same(action.After, actual))
                    {
                        string[] diff = BattleReconciler.Diff(action.After, actual);
                        deviations++;
                        failures++;
                        consecutiveFailures++;
                        deviationLog.Add(new AutoDeviationEntry(turn, index,
                            Describe(action.Action), diff));
                        ModLog.Error(
                            $"[BlitzSolver] ACTION_DEVIATION turn={turn} index={index} " +
                            $"action={Describe(action.Action)} diff={string.Join(" | ", diff)}");
                        route = null;
                        break;
                    }
                    tracker.Observe(action.After);
                    if (terminal)
                        ModLog.Info(
                            $"[BlitzSolver] DEPLOY_TERMINAL_CONFIRMED turn={turn} index={index} " +
                            $"action={Describe(action.Action)} outcome={action.After.Outcome}");
                    if (action.Action.Kind == PlayerActionKind.UsePotion)
                    {
                        ushort stored = action.Before.Potions[action.Action.PotionSlot];
                        if (stored != 0)
                            potionsUsed.Add(Registry.Potion((ushort)(stored - 1)).NativeId);
                    }
                    if (action.After.PlayerTurn > turn)
                        turnEnded = true;
                    ModLog.Info(
                        $"[BlitzSolver] DEPLOY_OK turn={turn} index={index} " +
                        $"action={Describe(action.Action)} outcome={action.After.Outcome} " +
                        $"next_turn={action.After.PlayerTurn}");
                    index++;
                    if (terminal)
                    {
                        finished = true;
                        break;
                    }
                    if (index >= route.Count || route[index].Before.PlayerTurn != turn)
                        break;
                }
                if (finished)
                    break;
                if (route == null)
                {
                    if (consecutiveFailures >= MaximumConsecutiveFailures)
                    {
                        pauseReason = $"第 {turn} 回合连续 {consecutiveFailures} 次预测偏差/部署失败";
                        break;
                    }
                    continue;
                }
                if (turnEnded)
                    continue;
                failures++;
                deploymentFailures++;
                consecutiveFailures++;
                failureLog.Add(new AutoFailureEntry(turn, "turn_plan_exhausted",
                    $"第 {turn} 回合路线用完但没有结束回合动作。"));
                ModLog.Error(
                    $"[BlitzSolver] DEPLOY_FAILURE turn={turn} reason=turn_plan_exhausted " +
                    $"remaining_actions={route.Count - index}");
                route = null;
                if (consecutiveFailures >= MaximumConsecutiveFailures)
                {
                    pauseReason = $"第 {turn} 回合连续 {consecutiveFailures} 次预测偏差/部署失败";
                    break;
                }
            }
            }
            catch (OperationCanceledException)
            {
                ModLog.Info(
                    $"[BlitzSolver] AUTO_LOOP_CANCELLED root={root} reason={Reason() ?? "cancel"}");
            }
            total.Stop();
            string? cancelReason = Reason();
            bool inProgress = CombatManager.Instance.IsInProgress;
            CombatState? final = CombatManager.Instance.DebugOnlyGetState();
            Player? finalPlayer = final == null ? null : LocalContext.GetMe(final);
            if (finalPlayer != null)
            {
                lastHp = finalPlayer.Creature.CurrentHp;
                lastMaxHp = finalPlayer.Creature.MaxHp;
                lastTurn = finalPlayer.PlayerCombatState?.TurnNumber ?? lastTurn;
            }
            string outcome;
            string status;
            if (!inProgress)
            {
                bool playerAlive = finalPlayer?.Creature.IsAlive ?? false;
                bool enemiesAlive = final?.Enemies.Any(static enemy => enemy.IsAlive) ?? false;
                outcome = playerAlive && !enemiesAlive ? "victory" : playerAlive ? "ended" : "defeat";
                status = outcome == "ended" ? "failed" : outcome;
            }
            else if (cancellation.IsCancellationRequested)
            {
                outcome = "in_progress";
                status = "cancelled";
            }
            else
            {
                outcome = "in_progress";
                status = "paused";
            }
            AutoCombatResult result = new(
                root, status, outcome, lastTurn, lastHp, lastMaxHp, [.. potionsUsed],
                searches, reused, deviations, deploymentFailures, total.ElapsedMilliseconds, pauseReason,
                [.. searchLog], [.. deviationLog], [.. failureLog]);
            ModLog.Info(
                $"[BlitzSolver] AUTO_END root={root} status={status} outcome={outcome} turn={lastTurn} " +
                $"hp={lastHp}/{lastMaxHp} searches={searches} reused={reused} deviations={deviations} " +
                $"deployment_failures={deploymentFailures} elapsed_ms={result.ElapsedMs} " +
                $"reason={cancelReason ?? pauseReason ?? "-"}");
            return result;
        }
        finally
        {
            total.Stop();
            lock (Gate)
            {
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation = null;
                    _cancelReason = null;
                }
            }
            cancellation.Dispose();
            Volatile.Write(ref _running, 0);
        }
    }

    private static async Task<(ReplayedRoute? Route, int Index)> SearchAsync(
        NGame host, CombatState combat, Player player, Rules rules, LiveCombatTracker tracker,
        OfflineRequest? request, string root, SearchPerformanceTier tier, int turn, string reason,
        List<AutoSearchEntry> log, List<AutoFailureEntry> failures, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Battle searchRoot;
        SearchCase searchCase;
        try
        {
            tracker.Refresh(combat);
            searchRoot = tracker.ImportRoot(combat, player, rules);
            searchCase = SearchSession.CompileCase(
                combat, player, rules, searchRoot, request, deterministic: false, tier);
        }
        catch (UnsupportedImportException error)
        {
            string detail = $"reason={error.Reason} {error.Detail}";
            failures.Add(new AutoFailureEntry(turn, "import_failed", detail));
            log.Add(new AutoSearchEntry(turn, reason, "rejected", 0, 0, 0, false, detail));
            ModLog.Error($"[BlitzSolver] AUTO_IMPORT_FAILED turn={turn} reason={reason} {detail}");
            return (null, 0);
        }
        catch (UnsupportedException error)
        {
            failures.Add(new AutoFailureEntry(turn, "import_unsupported", error.Message));
            log.Add(new AutoSearchEntry(turn, reason, "rejected", 0, 0, 0, false, error.Message));
            ModLog.Error($"[BlitzSolver] AUTO_IMPORT_FAILED turn={turn} reason={reason} {error.Message}");
            return (null, 0);
        }
        catch (CapacityException error)
        {
            string detail = $"region={error.Region} {error.Message}";
            failures.Add(new AutoFailureEntry(turn, "import_capacity", detail));
            log.Add(new AutoSearchEntry(turn, reason, "rejected", 0, 0, 0, false, detail));
            ModLog.Error($"[BlitzSolver] AUTO_IMPORT_FAILED turn={turn} reason={reason} {detail}");
            return (null, 0);
        }
        catch (InvalidDataException error)
        {
            failures.Add(new AutoFailureEntry(turn, "request_invalid", error.Message));
            log.Add(new AutoSearchEntry(turn, reason, "rejected", 0, 0, 0, false, error.Message));
            ModLog.Error($"[BlitzSolver] AUTO_IMPORT_FAILED turn={turn} reason={reason} {error.Message}");
            return (null, 0);
        }
        catch (InvalidOperationException error)
        {
            failures.Add(new AutoFailureEntry(turn, "request_invalid", error.Message));
            log.Add(new AutoSearchEntry(turn, reason, "rejected", 0, 0, 0, false, error.Message));
            ModLog.Error($"[BlitzSolver] AUTO_IMPORT_FAILED turn={turn} reason={reason} {error.Message}");
            return (null, 0);
        }
        SearchRunResult result = await SearchSession.RunAsync(
            host, root, searchCase, deterministic: false, tier, resultDirectory: null,
            cancelAfterMilliseconds: null);
        if (result.Status != "ok" || result.Beam is null)
        {
            string detail = $"search {result.Status}: {result.Error}";
            failures.Add(new AutoFailureEntry(turn, "search_failed", detail));
            log.Add(new AutoSearchEntry(turn, reason, result.Status, result.ElapsedMilliseconds,
                result.ExpandedNodes, 0, false, result.Error));
            ModLog.Error(
                $"[BlitzSolver] AUTO_SEARCH_FAILED turn={turn} reason={reason} " +
                $"status={result.Status} error={result.Error}");
            return (null, 0);
        }
        BeamResult beam = result.Beam;
        ReplayedRoute route;
        try
        {
            route = ReplayedRoute.Build(searchRoot, rules, beam.BestRoute, beam.BestIsVictory);
        }
        catch (InvalidDataException error)
        {
            failures.Add(new AutoFailureEntry(turn, "route_invalid", error.Message));
            log.Add(new AutoSearchEntry(turn, reason, "invalid", result.ElapsedMilliseconds,
                result.ExpandedNodes, beam.BestRoute.Length, beam.BestIsVictory, error.Message));
            ModLog.Error($"[BlitzSolver] AUTO_ROUTE_INVALID turn={turn} reason={reason} {error.Message}");
            return (null, 0);
        }
        catch (InvalidOperationException error)
        {
            failures.Add(new AutoFailureEntry(turn, "route_invalid", error.Message));
            log.Add(new AutoSearchEntry(turn, reason, "invalid", result.ElapsedMilliseconds,
                result.ExpandedNodes, beam.BestRoute.Length, beam.BestIsVictory, error.Message));
            ModLog.Error($"[BlitzSolver] AUTO_ROUTE_INVALID turn={turn} reason={reason} {error.Message}");
            return (null, 0);
        }
        catch (UnsupportedException error)
        {
            failures.Add(new AutoFailureEntry(turn, "route_unsupported", error.Message));
            log.Add(new AutoSearchEntry(turn, reason, "invalid", result.ElapsedMilliseconds,
                result.ExpandedNodes, beam.BestRoute.Length, beam.BestIsVictory, error.Message));
            ModLog.Error($"[BlitzSolver] AUTO_ROUTE_INVALID turn={turn} reason={reason} {error.Message}");
            return (null, 0);
        }
        catch (CapacityException error)
        {
            string detail = $"region={error.Region} {error.Message}";
            failures.Add(new AutoFailureEntry(turn, "route_capacity", detail));
            log.Add(new AutoSearchEntry(turn, reason, "invalid", result.ElapsedMilliseconds,
                result.ExpandedNodes, beam.BestRoute.Length, beam.BestIsVictory, detail));
            ModLog.Error($"[BlitzSolver] AUTO_ROUTE_INVALID turn={turn} reason={reason} {detail}");
            return (null, 0);
        }
        tracker.Observe(searchRoot);
        log.Add(new AutoSearchEntry(turn, reason, "ok", result.ElapsedMilliseconds,
            result.ExpandedNodes, beam.BestRoute.Length, beam.BestIsVictory, null));
        ModLog.Info(
            $"[BlitzSolver] AUTO_SEARCH turn={turn} reason={reason} ms={result.ElapsedMilliseconds} " +
            $"expanded={result.ExpandedNodes} route={beam.BestRoute.Length} victory={beam.BestIsVictory} " +
            $"score={beam.BestScore}");
        return (route, 0);
    }

    internal static void OnCombatEnded()
    {
        // The loop observes IsInProgress itself; cancelling a running search is SearchSession's job.
        lock (Gate)
            _cancelReason ??= "combat_ended";
    }

    internal static void Cancel(string reason)
    {
        CancellationTokenSource? cancellation;
        lock (Gate)
        {
            cancellation = _cancellation;
            _cancelReason ??= reason;
        }
        if (cancellation != null && !cancellation.IsCancellationRequested)
        {
            ModLog.Info($"[BlitzSolver] AUTO_CANCEL_REQUESTED reason={reason}");
            cancellation.Cancel();
            SearchSession.Cancel(reason);
        }
    }

    internal static async Task<bool> CancelAndWaitAsync(NGame host, string reason, int timeoutMilliseconds)
    {
        Cancel(reason);
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (IsRunning)
        {
            if (Environment.TickCount64 >= deadline)
            {
                ModLog.Error($"[BlitzSolver] AUTO_CANCEL_TIMEOUT reason={reason}");
                return false;
            }
            await GodotCombatWaiter.NextFrameAsync(host);
        }
        return true;
    }

    private static string? Reason()
    {
        lock (Gate)
            return _cancelReason;
    }

    private static string Describe(PlayerAction action) => action.ToString();
}
