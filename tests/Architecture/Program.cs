using System.Reflection;
using Sts2Solver.Architecture;
using Sts2Solver.Simulation.Combat;

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
    + $"rules=6 visibleRunTypes={ArchitectureRules.VisibleRunTypes.Count}");

Assembly self = typeof(DependencyChecker).Assembly;
IReadOnlyList<Violation> negative = DependencyChecker.Check(self);
const string sourceType = "Sts2Solver.Ai.Run.UnregisteredRunReferenceFixture";
const string targetType = "Sts2Solver.Simulation.Run.RunProjectionFixture";
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

IReadOnlyList<Violation> visible = DependencyChecker.Check(self);
const string visibleSource = "Sts2Solver.Ai.Run.VisibleRunReferenceFixture";
const string visibleTarget = "Sts2Solver.Simulation.Run.RunView";
if (visible.Any(violation => violation.Source == visibleSource && violation.Target == visibleTarget))
{
    Console.WriteLine("ARCHITECTURE_VISIBLE_FAILED RunView 已登记但仍被拒绝");
    return 1;
}
Console.WriteLine("ARCHITECTURE_VISIBLE_OK RunView 对 Ai.Run 可见");
HashSet<string> recursiveTypes = new(StringComparer.Ordinal)
{
    "Sts2Solver.Simulation.Run.VisibleNestedFixture",
    "Sts2Solver.Simulation.Run.VisibleInlineFixture",
    "Sts2Solver.Simulation.Run.ManagedVisibleFixture",
    "Sts2Solver.Simulation.Run.IndirectVisibleFixture",
};
IReadOnlyList<Violation> recursive = DependencyChecker.Check(self, recursiveTypes);
if (!recursive.Any(v => v.Source == "Sts2Solver.Simulation.Run.VisibleNestedFixture"
        && v.Target == "Sts2Solver.Simulation.Run.HiddenNestedFixture")
    || !recursive.Any(v => v.Source == "Sts2Solver.Simulation.Run.ManagedVisibleFixture"
        && v.Target == "System.String")
    || !recursive.Any(v => v.Source == "Sts2Solver.Simulation.Run.IndirectVisibleFixture"
        && v.Target == "System.IntPtr")
    || !recursive.Any(v => v.Source == "Sts2Solver.Simulation.Run.IndirectVisibleFixture"
        && v.Target == "System.UIntPtr"))
{
    Console.WriteLine("ARCHITECTURE_RECURSIVE_FAILED 未拒绝嵌套隐藏负载、托管引用或地址");
    return 1;
}
Console.WriteLine("ARCHITECTURE_RECURSIVE_OK 内联数组隐藏负载、托管引用与native-sized地址均被拒绝");
return 0;
