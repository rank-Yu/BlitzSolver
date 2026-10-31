namespace Sts2Solver.Content
{
    internal sealed class ContentFixture;
    internal static class ValidContentFixture
    {
        internal static Sts2Solver.Simulation.Combat.Battle Make() => default;
    }
}

namespace Sts2Solver.Ai.Combat
{
    internal static class InvalidAiFixture
    {
        internal static Sts2Solver.Content.ContentFixture Make() => new();
    }
    internal static class ValidAiFixture
    {
        internal static Sts2Solver.Simulation.Combat.Battle Make() => default;
    }
}

namespace Sts2Solver.Simulation.Combat
{
    internal static class InvalidSimulationFixture
    {
        internal static Sts2Solver.Content.ContentFixture Make() => new();
    }
}
