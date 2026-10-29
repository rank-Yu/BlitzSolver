using Sts2Solver.Ai.Run;
using Sts2Solver.Autoplay;
using Sts2Solver.Simulation.Run;

namespace Sts2Solver.KernelChecks;

internal static class RunPilotChecks
{
    private static int _checks;
    internal static void Check() => Run().GetAwaiter().GetResult();
    private static void Assert(bool value, string reason)
    { _checks++; if (!value) throw new InvalidDataException("run-pilot-check: " + reason); }
    private static RunView View(ulong revision = 1, bool confirm = false, bool cancel = false)
    {
        RunDecisionRequest request = new() { Kind = RunDecision.CardSelect, Token = new(717, revision),
            MinSelect = 1, MaxSelect = 1, Cancelable = cancel ? (byte)1 : (byte)0,
            RequireConfirmation = confirm ? (byte)1 : (byte)0 };
        RunDecisionBuilder.AddOption(ref request, new() { OptionId = 1, Kind = RunDecisionOptionKind.Card,
            PayloadKind = RunDecisionPayloadKind.Card });
        return default(RunView).WithRequest(request);
    }
    private static RunPilotAnswer Select(RunView view, bool confirmed = true)
        => new(RunDecisionProtocol.Select(view.Request, [1u]), confirmed);
    private sealed class Policy(Func<RunView, CancellationToken, ValueTask<RunPilotAnswer>> decide) : IRunPolicy
    {
        public int Calls;
        public ValueTask<RunPilotAnswer> DecideAsync(RunView view, CancellationToken token)
        { Calls++; return decide(view, token); }
    }
    private sealed class Host : IRunPilotHost
    {
        public readonly Queue<RunPilotBoundary> Boundaries = new();
        public Func<RunPilotAnswer, RunDecisionCallback, CancellationToken, ValueTask>? Execute;
        public RunCombatResult Combat = new();
        public int Reads, Executions, Combats, Invalidations;
        public bool Busy;
        public ValueTask<RunPilotBoundary> WaitForBoundaryAsync(CancellationToken token)
        { Reads++; return ValueTask.FromResult(Boundaries.Dequeue()); }
        public async ValueTask ExecuteAsync(RunPilotAnswer answer, RunDecisionCallback child, CancellationToken token)
        {
            Busy = true;
            try { Executions++; if (Execute != null) await Execute(answer, child, token); }
            finally { Busy = false; }
        }
        public ValueTask<RunCombatResult> RunCombatAsync(CancellationToken token)
        { Combats++; return ValueTask.FromResult(Combat); }
        public void InvalidatePending() { Invalidations++; }
    }
    private static Host NewHost()
    {
        Host host = new(); host.Boundaries.Enqueue(new(RunBoundaryKind.Decision, View()));
        host.Boundaries.Enqueue(new(RunBoundaryKind.Terminal, Terminal: RunStopReason.Victory)); return host;
    }
    private static async Task Run()
    {
        Policy policy = new((view, _) => ValueTask.FromResult(Select(view)));
        Host host = NewHost(); RunPilotResult result = await RunPilot.RunAsync(host, policy);
        Assert(result.Reason == RunStopReason.Victory && result.Decisions == 1 && host.Reads == 2
            && host.Executions == 1 && host.Invalidations == 1, "ordinary decision and terminal stops reads");
        host = NewHost(); TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<RunPilotAnswer> childAnswer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        policy = new((view, _) => view.Request.Token.Revision == 1 ? ValueTask.FromResult(Select(view)) : new(childAnswer.Task));
        host.Execute = async (_, child, token) => { entered.SetResult(); await child(View(2), token); };
        Task<RunPilotResult> run = RunPilot.RunAsync(host, policy); await entered.Task;
        Assert(!run.IsCompleted && policy.Calls == 2 && host.Busy, "true asynchronous child awaits same policy");
        childAnswer.SetResult(Select(View(2))); result = await run;
        Assert(result.Decisions == 2 && result.Trace.Select(t => t.Revision).SequenceEqual([1UL, 2UL]) && !host.Busy,
            "child shares trace and releases parent");
        host = NewHost(); host.Execute = async (_, child, token) => { await child(View(2), token); };
        result = await RunPilot.RunAsync(host, policy, maximumDecisions: 1);
        Assert(result.Reason == RunStopReason.Budget && result.Decisions == 1 && !host.Busy, "child budget settles parent");
        foreach (bool cancel in new[] { false, true })
        {
            host = NewHost(); host.Boundaries.Clear(); host.Boundaries.Enqueue(new(RunBoundaryKind.Decision, View(confirm: true, cancel: true)));
            host.Boundaries.Enqueue(new(RunBoundaryKind.Terminal, Terminal: RunStopReason.Death));
            policy = new((view, _) => ValueTask.FromResult(cancel
                ? new RunPilotAnswer(RunDecisionProtocol.Cancel(view.Request)) : Select(view, false)));
            if (cancel) { result = await RunPilot.RunAsync(host, policy); Assert(result.Reason == RunStopReason.Death, "Cancel needs no confirmation"); }
            else { bool rejected = false; try { await RunPilot.RunAsync(host, policy); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected && host.Executions == 0, "confirmation checked before dispatch"); }
        }
        foreach (bool child in new[] { false, true })
        {
            using CancellationTokenSource cancellation = new();
            host = NewHost(); TaskCompletionSource<RunPilotAnswer> late = new(TaskCreationOptions.RunContinuationsAsynchronously);
            policy = new((view, _) => !child || view.Request.Token.Revision == 2 ? new(late.Task) : ValueTask.FromResult(Select(view)));
            host.Execute = async (_, callback, _) => { await callback(View(2), CancellationToken.None); };
            run = RunPilot.RunAsync(host, policy, token: cancellation.Token); cancellation.Cancel(); result = await run;
            late.SetResult(Select(View(child ? 2UL : 1UL))); await Task.Yield();
            Assert(result.Reason == RunStopReason.Cancelled && host.Executions == (child ? 1 : 0) && !host.Busy,
                "root cancellation controls child None token and late answer cannot dispatch");
        }
        foreach (bool child in new[] { false, true })
        {
            host = NewHost(); policy = new((view, _) => child && view.Request.Token.Revision == 2
                ? throw new InvalidDataException("child-failure") : ValueTask.FromResult(Select(view)));
            host.Execute = async (_, callback, token) => { if (child) await callback(View(2), token); else throw new InvalidDataException("parent-failure"); };
            bool thrown = false; try { await RunPilot.RunAsync(host, policy); } catch (InvalidDataException) { thrown = true; }
            Assert(thrown && !host.Busy && host.Invalidations == 1, "parent or child failure releases lifecycle");
        }
        foreach (RunStopReason? stop in new RunStopReason?[] { null, RunStopReason.Death, RunStopReason.Paused, RunStopReason.Failed, RunStopReason.Cancelled })
        {
            host = new(); host.Combat = new(stop); host.Boundaries.Enqueue(new(RunBoundaryKind.Combat));
            host.Boundaries.Enqueue(new(RunBoundaryKind.Terminal, View(), RunStopReason.Victory));
            result = await RunPilot.RunAsync(host, policy);
            Assert(host.Combats == 1 && result.Reason == (stop ?? RunStopReason.Victory) && result.Decisions == 0,
                "combat handoff resumes only on success; explicit victory at HP zero");
        }
        foreach (RunStopReason invalid in new[] { RunStopReason.Victory, (RunStopReason)255 })
        {
            host = new(); host.Combat = new(invalid); host.Boundaries.Enqueue(new(RunBoundaryKind.Combat));
            bool rejected = false; try { await RunPilot.RunAsync(host, policy); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected && host.Invalidations == 1, "combat cannot claim whole-run victory or unknown stop");
        }
        host = new(); host.Boundaries.Enqueue(new(RunBoundaryKind.Paused, Detail: "next-room-entered"));
        result = await RunPilot.RunAsync(host, policy);
        Assert(result.Reason == RunStopReason.Paused && result.Detail == "next-room-entered"
            && result.Steps == 0 && result.Decisions == 0 && host.Combats == 0 && host.Invalidations == 1,
            "general pause neither dispatches combat nor claims terminal victory");
        host = new(); for (int i = 0; i < 3; i++) host.Boundaries.Enqueue(new(RunBoundaryKind.Combat));
        result = await RunPilot.RunAsync(host, policy, maximumSteps: 2);
        Assert(result.Reason == RunStopReason.Budget && host.Combats == 2, "repeated combat bounded by steps");
        foreach (string status in new[] { "victory", "defeat", "paused", "failed", "cancelled" })
        {
            CombatHost combatHost = new(status);
            RunCombatResult combat = await RunCombatHandoff.RunAsync(combatHost, "contract", CancellationToken.None);
            RunStopReason? expected = status switch { "victory" => null, "defeat" => RunStopReason.Death,
                "paused" => RunStopReason.Paused, "failed" => RunStopReason.Failed, _ => RunStopReason.Cancelled };
            Assert(combat.Stop == expected && combatHost.Finals == 1, "existing CombatPilot actually invoked " + status);
        }
        Console.WriteLine($"RUNPILOT contracts=pass checks={_checks}");
    }
    private sealed class CombatHost(string status) : ICombatPilotHost
    {
        public int Finals;
        public ValueTask<CombatPilotTurn?> WaitForTurnAsync(CancellationToken token) => ValueTask.FromResult<CombatPilotTurn?>(null);
        public CombatPilotFinal Final(bool cancelled) { Finals++; return new(status == "victory" ? "victory" : status == "defeat" ? "defeat" : "in_progress", status, 1, 0, 80, []); }
        public ValueTask<CombatPilotSearch> SearchAsync(int turn, string reason, CancellationToken token) => throw new InvalidOperationException();
        public ValueTask<CombatPilotBoundary> CheckBoundaryAsync(Sts2Solver.Simulation.Combat.ReplayedAction expected, int turn, int index, CancellationToken token) => throw new InvalidOperationException();
        public void BeginDeployment(int turn) => throw new InvalidOperationException();
        public ValueTask<CombatPilotDeployment> DeployAsync(Sts2Solver.Simulation.Combat.ReplayedAction action, int turn, CancellationToken token) => throw new InvalidOperationException();
        public void OnActionDeployed(Sts2Solver.Simulation.Combat.ReplayedAction action) => throw new InvalidOperationException();
        public void OnRouteAccepted(Sts2Solver.Simulation.Combat.ReplayedRoute route) => throw new InvalidOperationException();
    }
}
