namespace Sts2Solver.Simulation.Run
{
    /// <summary>Local negative/positive fixture: an unregistered Run-layer type.</summary>
    internal sealed class RunProjectionFixture
    {
    }

    /// <summary>Local positive fixture: Simulation.Run may reference Simulation.Combat.</summary>
    internal sealed class RunRulesFixture
    {
        internal static Sts2Solver.Simulation.Combat.Battle Make() => default;
    }
}

namespace Sts2Solver.Ai.Run
{
    /// <summary>
    /// Local counterexample: Ai.Run references a Simulation.Run type that is not registered as a
    /// visible projection or decision request. The checker must report it.
    /// </summary>
    internal sealed class UnregisteredRunReferenceFixture
    {
        internal static Sts2Solver.Simulation.Run.RunProjectionFixture Make() => new();
    }

    /// <summary>Positive fixture: the registered RunView projection is visible to Ai.Run.</summary>
    internal sealed class VisibleRunReferenceFixture
    {
        internal static Sts2Solver.Simulation.Run.RunView Make() => default;
    }
}

namespace Sts2Solver.Simulation.Run
{
    internal readonly record struct VisibleNestedFixture(VisibleInlineFixture Payload);

    [System.Runtime.CompilerServices.InlineArray(2)]
    internal struct VisibleInlineFixture { private HiddenNestedFixture _first; }

    internal readonly record struct HiddenNestedFixture(RunState State);

    internal readonly record struct ManagedVisibleFixture(string Text);

    internal readonly record struct IndirectVisibleFixture(nint SignedAddress, nuint UnsignedAddress);
}
