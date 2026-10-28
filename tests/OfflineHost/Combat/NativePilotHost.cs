// Low-tier CombatPilot host for the in-process boundary comparison. Search uses the same request
// compiler and BeamSolver as the offline whole-combat host; deployment goes through
// NativeActionExecutor so each route action is checked against the original game.
using BlitzSolver.Ai.Combat;
using BlitzSolver.Autoplay;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.TestHost;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;

namespace OfflineHost;

internal sealed class NativePilotHost(
    MainLoopContext loop,
    CombatState combat,
    Player player,
    Rules rules,
    LiveCombatTracker tracker,
    Battle opening,
    SearchPolicy policy,
    SearchPerformanceProfile profile,
    long memoryBudgetBytes) : ICombatPilotHost
{
    private readonly NativeActionExecutor _executor = new(loop, combat, player, rules, tracker);
    private readonly List<string> _potionsUsed = [];
    private Battle _current = opening;

    internal Battle Current => _current;

    /// <summary>Native run dump captured when the pilot observes the terminal outcome, before the
    /// run leaves the combat room (LavaLamp's AfterRoomEntered reset would otherwise run first).</summary>
    internal string? TerminalRun { get; private set; }

    internal bool? TerminalDead { get; private set; }

    internal CombatActionCoverage Coverage { get; } = new();

    public ValueTask<CombatPilotTurn?> WaitForTurnAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_current.Outcome != Outcome.None)
            return ValueTask.FromResult<CombatPilotTurn?>(null);
        return ValueTask.FromResult<CombatPilotTurn?>(new CombatPilotTurn(
            _current.PlayerTurn, _current.Player.CurrentHp, _current.Player.MaxHp));
    }

    public ValueTask<CombatPilotSearch> SearchAsync(int turn, string reason,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CombatState live = CombatManager.Instance.DebugOnlyGetState()
            ?? throw new OfflineDeploymentException("live_missing", "搜索时找不到原生战斗。");
        Player livePlayer = LocalContext.GetMe(live)
            ?? throw new OfflineDeploymentException("live_player_missing", "搜索时找不到本地玩家。");
        try
        {
            Battle liveRoot = tracker.ImportForComparison(live, livePlayer, rules, _current);
            if (!BattleReconciler.Same(_current, liveRoot))
            {
                string[] rootDiff = BattleReconciler.Diff(_current, liveRoot);
                return ValueTask.FromResult(new CombatPilotSearch(null, "diff", 0, 0, 0, false,
                    string.Join(" | ", rootDiff.Take(8)), "root_deviation",
                    string.Join('\n', rootDiff), 0, 0));
            }
            SearchCase searchCase = SearchRequestCompiler.CompileRequest(
                live, livePlayer, rules, _current, policy, EvaluationRootFacts.Default, profile,
                deterministic: true, memoryBudgetBytes);
            BeamResult beam = new BeamSolver(searchCase.Rules, searchCase.Options)
                .Solve(searchCase.Battles, token);
            ReplayedRoute route = ReplayedRoute.Build(_current, rules, beam.BestRoute,
                beam.BestIsVictory);
            return ValueTask.FromResult(new CombatPilotSearch(route, "ok", 0,
                beam.ExpandedNodes, beam.BestRoute.Length, beam.BestIsVictory, null, null, null,
                0, 0));
        }
        catch (UnsupportedImportException error)
        {
            string detail = $"reason={error.Reason} {error.Detail}";
            return ValueTask.FromResult(Failed("rejected", detail, "import_failed", detail));
        }
        catch (BlitzSolver.Simulation.Combat.UnsupportedException error)
        {
            return ValueTask.FromResult(Failed("rejected", error.Message, "import_unsupported",
                error.Message));
        }
        catch (CapacityException error)
        {
            string detail = $"region={error.Region} {error.Message}";
            return ValueTask.FromResult(Failed("rejected", detail, "import_capacity", detail));
        }
        catch (InvalidDataException error)
        {
            return ValueTask.FromResult(Failed("rejected", error.Message, "request_invalid",
                error.Message));
        }
    }

    public ValueTask<CombatPilotBoundary> CheckBoundaryAsync(ReplayedAction expected, int turn,
        int index, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CombatState live = CombatManager.Instance.DebugOnlyGetState()
            ?? throw new OfflineDeploymentException("live_missing", "边界检查时找不到原生战斗。");
        Player livePlayer = LocalContext.GetMe(live)
            ?? throw new OfflineDeploymentException("live_player_missing", "边界检查时找不到本地玩家。");
        Battle liveRoot = tracker.ImportForComparison(live, livePlayer, rules, expected.Before);
        bool same = BattleReconciler.Same(expected.Before, liveRoot);
        return ValueTask.FromResult(new CombatPilotBoundary(same,
            CombatPilotAction.Describe(expected),
            same ? [] : BattleReconciler.Diff(expected.Before, liveRoot)));
    }

    public void BeginDeployment(int turn)
    {
    }

    public ValueTask<CombatPilotDeployment> DeployAsync(ReplayedAction action, int turn,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Coverage.CountAction(action.Action.Kind);
        try
        {
            Battle live = _executor.Execute(action);
            if (live.Outcome != Outcome.None && TerminalRun == null)
            {
                CombatState? nativeLive = CombatManager.Instance.DebugOnlyGetState();
                Player? nativePlayer = nativeLive == null ? null : LocalContext.GetMe(nativeLive);
                if (nativeLive?.RunState is NativeRunState terminalRun && nativePlayer != null)
                {
                    TerminalRun = NativeRunDump.Capture(terminalRun, nativePlayer);
                    TerminalDead = live.Player.IsDead;
                }
            }
            if (!BattleReconciler.Same(action.After, live))
            {
                string[] diff = BattleReconciler.Diff(action.After, live);
                return ValueTask.FromResult(new CombatPilotDeployment(false, false,
                    "boundary_diff", string.Join('\n', diff),
                    CombatPilotAction.Describe(action), diff));
            }
            _current = action.After;
            return ValueTask.FromResult(new CombatPilotDeployment(true, true, null, null,
                CombatPilotAction.Describe(action), []));
        }
        catch (OfflineDeploymentException error)
        {
            return ValueTask.FromResult(new CombatPilotDeployment(false, false, error.Reason,
                error.Message, CombatPilotAction.Describe(action), []));
        }
    }

    public void OnActionDeployed(ReplayedAction action)
    {
        _current = action.After;
        if (action.Action.Kind == PlayerActionKind.UsePotion)
        {
            ushort stored = action.Before.Potions[action.Action.PotionSlot];
            if (stored != 0)
                _potionsUsed.Add(Registry.Potion((ushort)(stored - 1)).NativeId);
        }
    }

    public void OnRouteAccepted(ReplayedRoute route)
    {
    }

    public CombatPilotFinal Final(bool cancelled)
    {
        string outcome;
        string status;
        if (_current.Outcome == Outcome.Victory)
        {
            outcome = "victory";
            status = "victory";
        }
        else if (_current.Outcome == Outcome.Defeat)
        {
            outcome = "defeat";
            status = "defeat";
        }
        else if (_current.Outcome == Outcome.EventDefeat)
        {
            outcome = _current.Player.IsDead ? "defeat" : "ended";
            status = outcome == "ended" ? "failed" : "defeat";
        }
        else
        {
            outcome = "in_progress";
            status = cancelled ? "cancelled" : "paused";
        }
        return new CombatPilotFinal(outcome, status, _current.PlayerTurn,
            _current.Player.CurrentHp, _current.Player.MaxHp, [.. _potionsUsed]);
    }

    private static CombatPilotSearch Failed(string status, string error, string reason,
        string detail)
        => new(null, status, 0, 0, 0, false, error, reason, detail, 0, 0);

}
