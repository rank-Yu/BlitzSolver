// Mod-side implementation of the shared whole-combat host. The decision loop lives in
// Core/Search/FullCombatDriver.cs; this file keeps only native I/O: waiting for the play phase,
// importing roots, running the in-game search worker, deploying actions and projecting the live
// boundary back to Battle.
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

/// <summary>Native host for <see cref="FullCombatDriver"/>.</summary>
internal sealed class FullCombatHost(
    NGame host,
    Rules rules,
    LiveCombatTracker tracker,
    OfflineRequest? request,
    SearchPerformanceTier tier,
    string root,
    CombatState combat,
    Player player) : IFullCombatHost
{
    private readonly string _root = root;
    private readonly List<string> _potionsUsed = [];
    private CombatState _combat = combat;
    private Player _player = player;
    private ActionDeployer? _deployer;
    private int _settledTurn = -1;
    private int _lastTurn = player.PlayerCombatState?.TurnNumber ?? 0;
    private int _lastHp = player.Creature.CurrentHp;
    private int _lastMaxHp = player.Creature.MaxHp;

    public async ValueTask<FullCombatTurn?> WaitForTurnAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            CombatState? liveState = CombatManager.Instance.DebugOnlyGetState();
            if (liveState == null || !CombatManager.Instance.IsInProgress)
                return null;
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
            // The old loop checked the turn cap before spending the new-turn settle wait.
            if (turn > FullCombatDriver.MaximumTurns)
            {
                _lastTurn = turn;
                _lastHp = livePlayer.Creature.CurrentHp;
                _lastMaxHp = livePlayer.Creature.MaxHp;
                return new FullCombatTurn(turn, _lastHp, _lastMaxHp);
            }
            if (_settledTurn != turn)
            {
                await GodotCombatWaiter.SettleAsync(host);
                _settledTurn = turn;
            }
            liveState = CombatManager.Instance.DebugOnlyGetState();
            if (liveState == null || !CombatManager.Instance.IsInProgress)
                return null;
            livePlayer = LocalContext.GetMe(liveState);
            if (livePlayer?.PlayerCombatState is not { } settledPlayerState)
                continue;
            _combat = liveState;
            _player = livePlayer;
            turn = settledPlayerState.TurnNumber;
            _lastTurn = turn;
            _lastHp = livePlayer.Creature.CurrentHp;
            _lastMaxHp = livePlayer.Creature.MaxHp;
            return new FullCombatTurn(turn, _lastHp, _lastMaxHp);
        }
    }

    public async ValueTask<FullCombatSearch> SearchAsync(int turn, string reason,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Battle searchRoot;
        SearchCase searchCase;
        try
        {
            tracker.Refresh(_combat);
            searchRoot = tracker.ImportRoot(_combat, _player, rules);
            searchCase = SearchSession.CompileCase(
                _combat, _player, rules, searchRoot, request, deterministic: false, tier);
        }
        catch (UnsupportedImportException error)
        {
            string detail = $"reason={error.Reason} {error.Detail}";
            ModLog.Error($"[BlitzSolver] AUTO_IMPORT_FAILED turn={turn} reason={reason} {detail}");
            return new FullCombatSearch(null, "rejected", 0, 0, 0, false, detail,
                "import_failed", detail, 0, 0);
        }
        catch (UnsupportedException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_IMPORT_FAILED turn={turn} reason={reason} {error.Message}");
            return new FullCombatSearch(null, "rejected", 0, 0, 0, false, error.Message,
                "import_unsupported", error.Message, 0, 0);
        }
        catch (CapacityException error)
        {
            string detail = $"region={error.Region} {error.Message}";
            ModLog.Error($"[BlitzSolver] AUTO_IMPORT_FAILED turn={turn} reason={reason} {detail}");
            return new FullCombatSearch(null, "rejected", 0, 0, 0, false, detail,
                "import_capacity", detail, 0, 0);
        }
        catch (InvalidDataException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_IMPORT_FAILED turn={turn} reason={reason} {error.Message}");
            return new FullCombatSearch(null, "rejected", 0, 0, 0, false, error.Message,
                "request_invalid", error.Message, 0, 0);
        }
        catch (InvalidOperationException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_IMPORT_FAILED turn={turn} reason={reason} {error.Message}");
            return new FullCombatSearch(null, "rejected", 0, 0, 0, false, error.Message,
                "request_invalid", error.Message, 0, 0);
        }
        SearchRunResult result = await SearchSession.RunAsync(
            host, _root, searchCase, deterministic: false, tier, resultDirectory: null,
            cancelAfterMilliseconds: null);
        if (result.Status != "ok" || result.Beam is null)
        {
            string detail = $"search {result.Status}: {result.Error}";
            ModLog.Error(
                $"[BlitzSolver] AUTO_SEARCH_FAILED turn={turn} reason={reason} " +
                $"status={result.Status} error={result.Error}");
            return new FullCombatSearch(null, result.Status, result.ElapsedMilliseconds,
                result.ExpandedNodes, 0, false, result.Error, "search_failed", detail,
                result.ManagedHeapPeakBytes, result.WorkingSetPeakBytes);
        }
        BeamResult beam = result.Beam;
        ReplayedRoute route;
        try
        {
            route = ReplayedRoute.Build(searchRoot, rules, beam.BestRoute, beam.BestIsVictory);
        }
        catch (InvalidDataException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_ROUTE_INVALID turn={turn} reason={reason} {error.Message}");
            return new FullCombatSearch(null, "invalid", result.ElapsedMilliseconds,
                result.ExpandedNodes, beam.BestRoute.Length, beam.BestIsVictory, error.Message,
                "route_invalid", error.Message, result.ManagedHeapPeakBytes,
                result.WorkingSetPeakBytes);
        }
        catch (InvalidOperationException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_ROUTE_INVALID turn={turn} reason={reason} {error.Message}");
            return new FullCombatSearch(null, "invalid", result.ElapsedMilliseconds,
                result.ExpandedNodes, beam.BestRoute.Length, beam.BestIsVictory, error.Message,
                "route_invalid", error.Message, result.ManagedHeapPeakBytes,
                result.WorkingSetPeakBytes);
        }
        catch (UnsupportedException error)
        {
            ModLog.Error($"[BlitzSolver] AUTO_ROUTE_INVALID turn={turn} reason={reason} {error.Message}");
            return new FullCombatSearch(null, "invalid", result.ElapsedMilliseconds,
                result.ExpandedNodes, beam.BestRoute.Length, beam.BestIsVictory, error.Message,
                "route_unsupported", error.Message, result.ManagedHeapPeakBytes,
                result.WorkingSetPeakBytes);
        }
        catch (CapacityException error)
        {
            string detail = $"region={error.Region} {error.Message}";
            ModLog.Error($"[BlitzSolver] AUTO_ROUTE_INVALID turn={turn} reason={reason} {detail}");
            return new FullCombatSearch(null, "invalid", result.ElapsedMilliseconds,
                result.ExpandedNodes, beam.BestRoute.Length, beam.BestIsVictory, detail,
                "route_capacity", detail, result.ManagedHeapPeakBytes, result.WorkingSetPeakBytes);
        }
        tracker.Observe(searchRoot);
        ModLog.Info(
            $"[BlitzSolver] AUTO_SEARCH turn={turn} reason={reason} ms={result.ElapsedMilliseconds} " +
            $"expanded={result.ExpandedNodes} route={beam.BestRoute.Length} victory={beam.BestIsVictory} " +
            $"score={beam.BestScore}");
        return new FullCombatSearch(route, "ok", result.ElapsedMilliseconds, result.ExpandedNodes,
            beam.BestRoute.Length, beam.BestIsVictory, null, null, null,
            result.ManagedHeapPeakBytes, result.WorkingSetPeakBytes);
    }

    public ValueTask<FullCombatBoundary> CheckBoundaryAsync(ReplayedAction expected, int turn,
        int index, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Battle live = tracker.ImportForComparison(_combat, _player, rules, expected.Before);
        if (BattleReconciler.Same(expected.Before, live))
        {
            ModLog.Info($"[BlitzSolver] TURN_REUSE turn={turn} index={index}");
            return ValueTask.FromResult(new FullCombatBoundary(true, FullCombatAction.Describe(expected), []));
        }
        string[] diff = BattleReconciler.Diff(expected.Before, live);
        ModLog.Error(
            $"[BlitzSolver] TURN_DEVIATION turn={turn} index={index} " +
            $"action={FullCombatAction.Describe(expected)} diff={string.Join(" | ", diff)}");
        return ValueTask.FromResult(new FullCombatBoundary(false, FullCombatAction.Describe(expected), diff));
    }

    public void BeginDeployment(int turn)
    {
        _deployer = new ActionDeployer(host, _combat, _player, rules, tracker);
    }

    public async ValueTask<FullCombatDeployment> DeployAsync(ReplayedAction action, int turn,
        CancellationToken token)
    {
        ActionDeployer deployer = _deployer
            ?? throw new InvalidOperationException("行动部署前没有建立部署器。");
        Battle actual;
        try
        {
            actual = await deployer.ExecuteAsync(action, token);
        }
        catch (DeploymentFailureException error)
        {
            ModLog.Error(
                $"[BlitzSolver] DEPLOY_FAILURE turn={turn} index={action.Index} " +
                $"action={FullCombatAction.Describe(action)} reason={error.Reason} detail={error.Message}");
            return new FullCombatDeployment(false, false, error.Reason, error.Message,
                FullCombatAction.Describe(action), []);
        }
        bool terminal = action.After.Outcome != Outcome.None;
        if (!terminal && !BattleReconciler.Same(action.After, actual))
        {
            string[] diff = BattleReconciler.Diff(action.After, actual);
            ModLog.Error(
                $"[BlitzSolver] ACTION_DEVIATION turn={turn} index={action.Index} " +
                $"action={FullCombatAction.Describe(action)} diff={string.Join(" | ", diff)}");
            return new FullCombatDeployment(true, false, null, null, FullCombatAction.Describe(action), diff);
        }
        return new FullCombatDeployment(true, true, null, null, FullCombatAction.Describe(action), []);
    }

    public void OnActionDeployed(ReplayedAction action)
    {
        tracker.Observe(action.After);
        if (action.Action.Kind == PlayerActionKind.UsePotion)
        {
            ushort stored = action.Before.Potions[action.Action.PotionSlot];
            if (stored != 0)
                _potionsUsed.Add(Registry.Potion((ushort)(stored - 1)).NativeId);
        }
        ModLog.Info(
            $"[BlitzSolver] DEPLOY_OK turn={action.Before.PlayerTurn} index={action.Index} " +
            $"action={FullCombatAction.Describe(action)} outcome={action.After.Outcome} " +
            $"next_turn={action.After.PlayerTurn}");
        if (action.After.Outcome != Outcome.None)
        {
            ModLog.Info(
                $"[BlitzSolver] DEPLOY_TERMINAL_CONFIRMED turn={action.Before.PlayerTurn} " +
                $"index={action.Index} action={FullCombatAction.Describe(action)} outcome={action.After.Outcome}");
        }
    }

    public void OnRouteAccepted(ReplayedRoute route)
    {
        tracker.Observe(route[0].Before);
    }

    public FullCombatFinal Final(bool cancelled)
    {
        bool inProgress = CombatManager.Instance.IsInProgress;
        CombatState? final = CombatManager.Instance.DebugOnlyGetState();
        Player? finalPlayer = final == null ? null : LocalContext.GetMe(final);
        if (finalPlayer != null)
        {
            _lastHp = finalPlayer.Creature.CurrentHp;
            _lastMaxHp = finalPlayer.Creature.MaxHp;
            _lastTurn = finalPlayer.PlayerCombatState?.TurnNumber ?? _lastTurn;
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
        else if (cancelled)
        {
            outcome = "in_progress";
            status = "cancelled";
        }
        else
        {
            outcome = "in_progress";
            status = "paused";
        }
        return new FullCombatFinal(outcome, status, _lastTurn, _lastHp, _lastMaxHp,
            [.. _potionsUsed]);
    }


}
