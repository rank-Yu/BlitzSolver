using System.Reflection;
using BlitzSolver.Architecture;
using BlitzSolver.Simulation.Combat;

Assembly core = typeof(Battle).Assembly;
IReadOnlyList<Violation> violations = DependencyChecker.Check(core);
foreach (Violation violation in violations)
    Console.WriteLine($"ARCHITECTURE_VIOLATION {violation}");
if (violations.Count != 0)
{
    Console.WriteLine($"ARCHITECTURE_FAILED assembly={core.GetName().Name} violations={violations.Count}");
    return 1;
}
Console.WriteLine($"ARCHITECTURE_OK assembly={core.GetName().Name} types={core.GetTypes().Length} "
    + "rules=6 visibleRunTypes=0");

Assembly self = typeof(DependencyChecker).Assembly;
IReadOnlyList<Violation> negative = DependencyChecker.Check(self);
const string sourceType = "BlitzSolver.Ai.Run.UnregisteredRunReferenceFixture";
const string targetType = "BlitzSolver.Simulation.Run.RunProjectionFixture";
Violation? expected = negative.FirstOrDefault(violation
    => violation.Source == sourceType && violation.Target == targetType);
if (expected is null)
{
    Console.WriteLine("ARCHITECTURE_NEGATIVE_FAILED 未报告未登记的 Ai.Run -> Simulation.Run 引用");
    return 1;
}
Console.WriteLine($"ARCHITECTURE_NEGATIVE_OK {expected}");

HashSet<string> visibleRunTypes = new(StringComparer.Ordinal) { targetType };
IReadOnlyList<Violation> permitted = DependencyChecker.Check(self, visibleRunTypes);
if (permitted.Any(violation => violation.Source == sourceType && violation.Target == targetType))
{
    Console.WriteLine("ARCHITECTURE_ALLOWLIST_FAILED 登记后的可见投影仍被拒绝");
    return 1;
}
Console.WriteLine("ARCHITECTURE_ALLOWLIST_OK 登记后的可见投影被允许");
return 0;
