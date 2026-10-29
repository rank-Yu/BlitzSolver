using BlitzSolver.Simulation.Run;

namespace BlitzSolver.Ai.Run;

public readonly record struct RunPilotAnswer(RunAction Action, bool Confirmed = false);
/// <summary>Only registered visible DTOs cross the strategy boundary; Architecture enforces this layer.</summary>
public interface IRunPolicy
{
    ValueTask<RunPilotAnswer> DecideAsync(RunView view, CancellationToken token);
}
