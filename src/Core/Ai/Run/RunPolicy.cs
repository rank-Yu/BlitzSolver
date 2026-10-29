using Sts2Solver.Simulation.Run;

namespace Sts2Solver.Ai.Run;

public readonly record struct RunPilotAnswer(RunAction Action, bool Confirmed = false);
/// <summary>Only registered visible DTOs cross the strategy boundary; Architecture enforces this layer.</summary>
public interface IRunPolicy
{
    ValueTask<RunPilotAnswer> DecideAsync(RunView view, CancellationToken token);
}
