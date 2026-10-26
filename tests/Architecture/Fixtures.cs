namespace BlitzSolver.Simulation.Run
{
    /// <summary>Local negative/positive fixture: an unregistered Run-layer type.</summary>
    internal sealed class RunProjectionFixture
    {
    }

    /// <summary>Local positive fixture: Simulation.Run may reference Simulation.Combat.</summary>
    internal sealed class RunRulesFixture
    {
        internal static BlitzSolver.Simulation.Combat.Battle Make() => default;
    }
}

namespace BlitzSolver.Ai.Run
{
    /// <summary>
    /// Local counterexample: Ai.Run references a Simulation.Run type that is not registered as a
    /// visible projection or decision request. The checker must report it.
    /// </summary>
    internal sealed class UnregisteredRunReferenceFixture
    {
        internal static BlitzSolver.Simulation.Run.RunProjectionFixture Make() => new();
    }

    /// <summary>Positive fixture: the registered RunView projection is visible to Ai.Run.</summary>
    internal sealed class VisibleRunReferenceFixture
    {
        internal static BlitzSolver.Simulation.Run.RunView Make() => default;
    }
}
