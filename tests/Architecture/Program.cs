using System.Reflection;
using Sts2Solver.Architecture;
using Sts2Solver.Simulation.Combat;

if (args is ["--mod", var path])
    return ModBoundary.Check(path, testing: false);
if (args is ["--testing-mod", var testPath])
    return ModBoundary.Check(testPath, testing: true);
if (args.Length != 0)
{
    Console.Error.WriteLine("用法：Architecture [--mod DLL | --testing-mod DLL]");
    return 2;
}
Assembly core = typeof(Battle).Assembly;
IReadOnlyList<Violation> violations = DependencyChecker.Check(core);
foreach (Violation violation in violations)
    Console.WriteLine($"ARCHITECTURE_VIOLATION {violation}");
if (violations.Count != 0)
    return 1;
if (core.GetTypes().Any(t => t.Namespace is { } ns
    && (ns.StartsWith("Sts2Solver.Simulation.Run", StringComparison.Ordinal)
        || ns.StartsWith("Sts2Solver.Ai.Run", StringComparison.Ordinal)))
    || core.GetType("Sts2Solver.Autoplay.RunPilot") != null)
{
    Console.Error.WriteLine("ARCHITECTURE_FAILED 战斗核心中仍有局外模块");
    return 1;
}
Console.WriteLine($"ARCHITECTURE_OK assembly={core.GetName().Name} types={core.GetTypes().Length}");
IReadOnlyList<Violation> fixtures = DependencyChecker.Check(typeof(DependencyChecker).Assembly);
string[] expected = ["Sts2Solver.Ai.Combat.InvalidAiFixture", "Sts2Solver.Simulation.Combat.InvalidSimulationFixture"];
if (fixtures.Count != expected.Length || expected.Any(name => !fixtures.Any(v => v.Source == name)))
{
    Console.Error.WriteLine("ARCHITECTURE_FIXTURES_FAILED 应拒绝 Ai/Simulation -> Content，并允许 Content/Ai -> Simulation");
    return 1;
}
Console.WriteLine("ARCHITECTURE_FIXTURES_OK 战斗分层正反例通过");
return 0;
