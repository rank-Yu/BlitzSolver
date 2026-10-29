namespace Sts2Solver.Simulation.Combat;

/// <summary>
/// Per-card private rider codes shared by content definitions and evaluation. They live in the
/// simulation layer so <c>Ai.Combat</c> can read them without referencing <c>Content</c>.
/// </summary>
public static class CardPrivateRiders
{
    /// <summary>Native <c>TinkerTime</c> rider code for Curious.</summary>
    public const int MadScienceCurious = 8;
}
