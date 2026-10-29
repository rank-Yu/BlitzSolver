using System.Security.Cryptography;
using System.Text;
using BlitzSolver.Ai.Run;
using BlitzSolver.Autoplay;
using BlitzSolver.Import;
using BlitzSolver.OfflineHost.Combat;
using BlitzSolver.OfflineHost.Run;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;

namespace BlitzSolver.OfflineHost.Runtime;

internal static class FirstCombatSession
{
    internal static FirstCombatResult Run(FirstCombatOptions options, CancellationToken external)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(external, timeout.Token);
        CancellationToken token = lifetime.Token;
        MainLoopContext loop = new();
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(loop);
        StateSummary? starting = null, after = null;
        CombatSummary? summary = null;
        SmokePolicy policy = new();
        FirstCombatResult Result(string status, string? milestone, string? detail)
            => new(status, milestone, detail, options, "first-combat-smoke-v1", [.. policy.Choices], starting, after, summary);
        try
        {
            token.ThrowIfCancellationRequested();
            GameBootstrap.ApplyGodotBypasses();
            GameBootstrap.SkipGodotNodeStaticConstructors();
            GameBootstrap.InitializeStaticState();
            TerminalBattleCapture.Install();
            Task<(NativeRun Run, Player Player)> create = NewRunFactory.Create(options, token);
            loop.RunUntilCompleted(create, TimeSpan.FromSeconds(options.TimeoutSeconds), "new-run", token);
            (NativeRun run, Player player) = create.GetAwaiter().GetResult();
            starting = Summarize(run, player);
            ValueTask<OfflineRunBoundary> Boundary(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (run.CurrentRoom is CombatRoom)
                {
                    WaitPlayable(loop, run, player, ct);
                    return ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Combat));
                }
                if (run.CurrentRoom is EventRoom room && !room.LocalMutableEvent.IsFinished)
                    return ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Event, Event: room.LocalMutableEvent));
                if (run.CurrentRoom is EventRoom finished && finished.LocalMutableEvent.IsFinished)
                    return ValueTask.FromResult(new OfflineRunBoundary(OfflineBoundaryKind.Map,
                        TravelEnabled: () => finished.LocalMutableEvent.IsFinished
                            && !CombatManager.Instance.IsInProgress
                            && RunManager.Instance.ActionExecutor.FinishedExecutingActions().IsCompleted));
                throw new BlitzSolver.Simulation.Combat.UnsupportedException("unsupported-run-boundary:" + run.CurrentRoom?.GetType().Name);
            }
            async ValueTask<RunCombatResult> Combat(CancellationToken ct)
            {
                CombatState combat = WaitPlayable(loop, run, player, ct);
                Rules rules = RulesCompiler.Compile(combat, player, run);
                LiveCombatTracker tracker = new(combat);
                Battle opening = tracker.ImportRoot(combat, player, rules);
                tracker.Observe(opening);
                NativePilotHost host = new(loop, combat, player, rules, tracker, opening,
                    SearchPolicyCapture.Capture(combat, player),
                    SearchPerformanceProfile.For(SearchPerformanceProfile.ParseTier(options.SearchTier)), options.MemoryBudgetBytes,
                    widening: true);
                CombatPilotResult result = await CombatPilot.RunAsync(host, "first-combat", ct);
                Battle? terminal = TerminalBattleCapture.Value;
                summary = new(result.Status, result.Outcome, result.Turns, host.Actions, result.Searches,
                    result.SearchLog.Sum(s => (long)s.ExpandedNodes), result.Deviations, result.DeploymentFailures,
                    result.FailureLog.FirstOrDefault()?.Detail ?? result.PauseReason,
                    Hash(string.Join("\n", BattleDump.Lines(opening))),
                    terminal is { } t ? Hash(string.Join("\n", BattleDump.Lines(t))) : null,
                    TerminalBattleCapture.Path.ToString(), terminal?.Player.CurrentHp,
                    terminal?.Player.MaxHp, terminal?.Gold);
                if (result.DeploymentFailures != 0 || result.Deviations != 0 || result.Status == "failed")
                    return new(RunStopReason.Failed, summary.Failure ?? "combat-deviation");
                if (result.Status == "cancelled") return new(RunStopReason.Cancelled, "combat-cancelled");
                if (result.Outcome is "victory" or "defeat")
                {
                    if (terminal == null || CombatManager.Instance.IsInProgress)
                        return new(RunStopReason.Failed, "native-terminal-unconfirmed");
                    loop.RunUntilCompleted(RunManager.Instance.ActionExecutor.FinishedExecutingActions(),
                        TimeSpan.FromSeconds(options.TimeoutSeconds), "post-combat-queue", ct);
                    after = Summarize(run, player);
                    return result.Outcome == "victory"
                        ? new(RunStopReason.Paused, "first-combat-complete")
                        : new(RunStopReason.Death, "combat-death");
                }
                return new(RunStopReason.Failed, result.PauseReason ?? "combat-incomplete");
            }
            OfflineRunHost runHost = new(run, player, Boundary, Combat);
            try
            {
                Task<RunPilotResult> task = RunPilot.RunAsync(runHost, policy, token: token);
                loop.RunUntilCompleted(task, TimeSpan.FromSeconds(options.TimeoutSeconds), "run-pilot", token);
                RunPilotResult result = task.GetAwaiter().GetResult();
                if (timeout.IsCancellationRequested) return Result("Timeout", null, "overall-timeout");
                return Result(result.Reason.ToString(), result.Detail == "first-combat-complete" ? result.Detail : null, result.Detail);
            }
            finally
            {
                runHost.InvalidatePending();
                loop.RunUntilCompleted(runHost.DisposeAsync().AsTask(), TimeSpan.FromSeconds(5), "host-dispose");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return Result(timeout.IsCancellationRequested ? "Timeout" : "Cancelled", null, timeout.IsCancellationRequested ? "overall-timeout" : "external-cancellation"); }
        catch (TimeoutException error) { return Result("Timeout", null, error.Message); }
        catch (UnsupportedImportException error) { return Result("Failed", null, error.Message); }
        catch (BlitzSolver.Simulation.Combat.UnsupportedException error) { return Result("Failed", null, error.Message); }
        catch (CapacityException error) { return Result("Failed", null, error.Message); }
        catch (InvalidDataException error) { return Result("Failed", null, error.Message); }
        catch (OfflineDeploymentException error) { return Result("Failed", null, error.Message); }
        finally { TerminalBattleCapture.End(); SynchronizationContext.SetSynchronizationContext(previous); }
    }
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static StateSummary Summarize(NativeRun run, Player player)
        => new(Hash(RunDump.Capture(RunStateImporter.Import(run, player))), (int)player.Creature.CurrentHp,
            (int)player.Creature.MaxHp, player.Gold,
            player.Deck.Cards.Select(c => c.Id.Entry + "+" + c.CurrentUpgradeLevel).ToArray(),
            player.Relics.Select(r => r.Id.Entry).ToArray(),
            player.PotionSlots.Select(p => p?.Id.Entry ?? "").ToArray());
    private static CombatState WaitPlayable(MainLoopContext loop, NativeRun run, Player player, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), run))
                throw new InvalidDataException("playable-run-changed");
            CombatState? state = CombatManager.Instance.DebugOnlyGetState();
            if (state != null && CombatManager.Instance.IsInProgress && state.CurrentSide == CombatSide.Player
                && ReferenceEquals(MegaCrit.Sts2.Core.Context.LocalContext.GetMe(state), player)
                && player.PlayerCombatState?.Phase == PlayerTurnPhase.Play
                && RunManager.Instance.ActionExecutor.FinishedExecutingActions().IsCompleted)
                return state;
            loop.Pump(TimeSpan.FromMilliseconds(5));
        }
    }
    private sealed class SmokePolicy : IRunPolicy
    {
        internal List<VisibleChoice> Choices { get; } = [];
        public ValueTask<RunPilotAnswer> DecideAsync(RunView view, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            RunDecisionRequest request = view.Request;
            if (request.Kind == RunDecision.Event
                && (!RunCatalog.TryEvent("NEOW", out ushort neow) || request.Source != new RunRef(RunRefKind.Event, neow)))
                throw new BlitzSolver.Simulation.Combat.UnsupportedException("smoke-policy-event-outside-neow");
            string[] names = new string[request.OptionCount];
            List<int> legal = [];
            int chosen = -1;
            for (int i = 0; i < names.Length; i++)
            {
                RunDecisionOption option = request.Options[i];
                names[i] = option.Model.Kind == RunRefKind.Relic ? Registry.Relic(option.Model.Id).NativeId
                    : option.Kind == RunDecisionOptionKind.MapPoint
                        ? $"MapPoint:{option.Coord.Col},{option.Coord.Row}:{option.PointType}"
                        : option.Kind + ":" + option.OptionId;
                if ((option.Flags & (RunDecisionOptionFlags.Disabled | RunDecisionOptionFlags.Locked)) == 0)
                {
                    legal.Add(i);
                    if (chosen < 0 || names[i] == "LAVA_ROCK") chosen = i;
                }
            }
            if (request.Kind is not (RunDecision.Event or RunDecision.MapPoint or RunDecision.CardSelect))
                throw new InvalidDataException("smoke-policy-unsupported-decision:" + request.Kind);
            int count = Math.Max(1, (int)request.MinSelect);
            if (count > request.MaxSelect || count > legal.Count)
                throw new InvalidDataException("smoke-policy-no-legal-selection");
            uint[] ids = new uint[count];
            string[] selected = new string[count];
            legal.Remove(chosen);
            legal.Insert(0, chosen);
            for (int i = 0; i < count; i++)
            {
                int index = legal[i];
                ids[i] = request.Options[index].OptionId; selected[i] = names[index];
            }
            Choices.Add(new(request.Kind.ToString(), names, selected));
            return ValueTask.FromResult(new RunPilotAnswer(RunDecisionProtocol.Select(request, ids), true));
        }
    }
}
