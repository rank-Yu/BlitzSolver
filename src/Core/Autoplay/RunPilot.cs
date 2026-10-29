using Sts2Solver.Simulation.Run;
using Sts2Solver.Ai.Run;

namespace Sts2Solver.Autoplay;

public enum RunBoundaryKind { Decision, Combat, Terminal, Paused }
public enum RunStopReason { Victory, Death, Budget, Cancelled, Paused, Failed }
public sealed record RunPilotBoundary(RunBoundaryKind Kind, RunView View = default,
    RunStopReason Terminal = RunStopReason.Failed, string? Detail = null);
public sealed record RunCombatResult(RunStopReason? Stop = null, string? Detail = null);
public sealed record RunPilotResult(RunStopReason Reason, int Decisions, int Steps,
    string? Detail, RunDecisionToken[] Trace);

public delegate ValueTask<RunPilotAnswer> RunDecisionCallback(RunView view, CancellationToken token);

public interface IRunPilotHost
{
    /// <summary>Wait asynchronously for a stable boundary. Terminal boundaries never require an import.</summary>
    ValueTask<RunPilotBoundary> WaitForBoundaryAsync(CancellationToken token);
    /// <summary>Native child callbacks await the supplied shared decision path while this parent awaits native completion.</summary>
    ValueTask ExecuteAsync(RunPilotAnswer answer, RunDecisionCallback decideChild, CancellationToken token);
    /// <summary>The runtime adapter uses the existing CombatPilot; a boundary-only adapter explicitly returns Paused.</summary>
    ValueTask<RunCombatResult> RunCombatAsync(CancellationToken token);
    /// <summary>Invalidate pending requests when the pilot stops, including budget, errors and cancellation.</summary>
    void InvalidatePending();
}

/// <summary>One policy path and one bounded ledger, including native child choices.</summary>
public static class RunPilot
{
    public static async Task<RunPilotResult> RunAsync(IRunPilotHost host, IRunPolicy policy,
        int maximumDecisions = 256, int maximumSteps = 512, CancellationToken token = default)
    {
        if (maximumDecisions < 1 || maximumSteps < 1) throw new ArgumentOutOfRangeException(nameof(maximumDecisions));
        int decisions = 0, steps = 0;
        List<RunDecisionToken> trace = [];
        async ValueTask<RunPilotAnswer> Decide(RunView view, CancellationToken childToken)
        {
            token.ThrowIfCancellationRequested(); childToken.ThrowIfCancellationRequested();
            if (decisions >= maximumDecisions) throw new RunBudgetException();
            RunDecisionProtocol.ValidateRequest(view.Request);
            decisions++; trace.Add(view.Request.Token);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, childToken);
            RunPilotAnswer answer = await policy.DecideAsync(view, linked.Token).AsTask().WaitAsync(linked.Token);
            token.ThrowIfCancellationRequested(); childToken.ThrowIfCancellationRequested();
            RunDecisionProtocol.ValidateAction(view.Request, answer.Action);
            if (answer.Action.Kind == RunActionKind.Select && view.Request.RequireConfirmation != 0 && !answer.Confirmed)
                throw new InvalidDataException("run-confirmation-required");
            return answer;
        }
        RunPilotResult Result(RunStopReason reason, string? detail = null)
            => new(reason, decisions, steps, detail, [.. trace]);
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                RunPilotBoundary boundary = await host.WaitForBoundaryAsync(token);
                if (boundary.Kind == RunBoundaryKind.Terminal)
                {
                    if (boundary.Terminal is not (RunStopReason.Victory or RunStopReason.Death or RunStopReason.Failed))
                        throw new InvalidDataException("run-invalid-terminal");
                    return Result(boundary.Terminal, boundary.Detail);
                }
                if (boundary.Kind == RunBoundaryKind.Paused) return Result(RunStopReason.Paused, boundary.Detail);
                if (steps >= maximumSteps) return Result(RunStopReason.Budget, "step-budget");
                steps++;
                switch (boundary.Kind)
                {
                    case RunBoundaryKind.Decision:
                        RunPilotAnswer answer = await Decide(boundary.View, token);
                        await host.ExecuteAsync(answer, Decide, token);
                        break;
                    case RunBoundaryKind.Combat:
                        RunCombatResult combat = await host.RunCombatAsync(token);
                        if (combat.Stop is { } stop)
                        {
                            if (!Enum.IsDefined(stop) || stop == RunStopReason.Victory)
                                throw new InvalidDataException("run-invalid-combat-stop");
                            return Result(stop, combat.Detail);
                        }
                        break;
                    default: throw new InvalidDataException("run-unknown-boundary");
                }
            }
        }
        catch (RunBudgetException) { return Result(RunStopReason.Budget, "decision-budget"); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return Result(RunStopReason.Cancelled, "external-cancellation"); }
        finally { host.InvalidatePending(); }
    }
    private sealed class RunBudgetException : Exception;
}

/// <summary>Production combat adapters share the existing whole-combat loop and preserve pauses.</summary>
internal static class RunCombatHandoff
{
    internal static async ValueTask<RunCombatResult> RunAsync(ICombatPilotHost host, string root, CancellationToken token)
    {
        CombatPilotResult result = await CombatPilot.RunAsync(host, root, token);
        if (token.IsCancellationRequested) return new(RunStopReason.Cancelled, "combat-cancelled");
        if (result.Status == "cancelled") return new(RunStopReason.Cancelled, "combat-cancelled");
        if (result.Status == "failed") return new(RunStopReason.Failed, result.PauseReason ?? "combat-failed");
        if (result.Status == "paused" || result.PauseReason != null)
            return new(RunStopReason.Paused, result.PauseReason ?? "combat-paused");
        if (result.Outcome == "victory") return new();
        if (result.Outcome == "defeat") return new(RunStopReason.Death, "combat-death");
        return new(RunStopReason.Paused, result.PauseReason ?? result.Status);
    }
}
