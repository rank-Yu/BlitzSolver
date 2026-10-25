namespace BlitzSolver.Import;

/// <summary>
/// Root-local values the old search bridge carried on its snapshot rather than in the native
/// combat state. Defaults describe a normal play-phase root captured by the offline host.
/// </summary>
public sealed record ImportOptions
{
    /// <summary><c>SimulationSnapshot.ShufflesCrossed</c>; zero for a fresh root.</summary>
    public int ShufflesCrossed { get; init; }

    /// <summary><c>SimulationSnapshot.HasRisk</c>; false unless the caller carries a recorded risk.</summary>
    public bool HasRisk { get; init; }

    /// <summary>
    /// Potion uses already spent by a fixed prefix (<c>OpeningProbe</c>). M2a roots have none; kept
    /// so the generated-potion counters have the same meaning as the old bridge.
    /// </summary>
    public int SkippedPotionUses { get; init; }

    /// <summary>The root was captured at a search boundary where the battleworn dummy timed out.</summary>
    public bool EventDefeatBoundary { get; init; }

    /// <summary>Require the player play phase; true for every search root.</summary>
    public bool RequirePlayPhase { get; init; } = true;

    /// <summary>
    /// The root is the ordinary root of a turn-setup fixture (captured before the test host's initial
    /// injection). The old bridge's setup capture normalized Vital Spark at least once.
    /// </summary>
    public bool TurnSetupRoot { get; init; }

    public static ImportOptions Default { get; } = new();
}
