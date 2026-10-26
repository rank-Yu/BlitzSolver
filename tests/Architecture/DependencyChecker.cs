using System.Reflection;
using System.Reflection.Emit;
using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Architecture;

/// <summary>
/// Reads the compiled <c>BlitzSolver.Core</c> assembly and checks the layer dependency rules from
/// <c>docs/full-run-design.md</c> section 4. Type references are discovered from signatures, base
/// types, interfaces, locals and IL operands; BCL types are always allowed and are ignored by the
/// layer classifier.
/// </summary>
internal static class DependencyChecker
{
    internal static IReadOnlyList<Violation> Check(Assembly assembly,
        IReadOnlySet<string>? visibleRunTypes = null)
    {
        visibleRunTypes ??= ArchitectureRules.VisibleRunTypes;
        List<Violation> violations = [];
        HashSet<(string Source, string Target)> seen = [];
        foreach (Type sourceType in assembly.GetTypes())
        {
            Layer source = ArchitectureRules.LayerOf(sourceType.Namespace);
            if (source == Layer.None)
                continue;
            foreach ((Type targetType, string via) in TypeReferences.Of(sourceType))
            {
                Layer target = ArchitectureRules.LayerOf(targetType.Namespace);
                if (target == Layer.None || target == source)
                    continue;
                if (ArchitectureRules.Allows(source, target, targetType, visibleRunTypes))
                    continue;
                string sourceName = ArchitectureRules.TypeName(sourceType);
                string targetName = ArchitectureRules.TypeName(targetType);
                if (seen.Add((sourceName, targetName)))
                    violations.Add(new Violation(sourceName, targetName, via));
            }
        }
        return violations;
    }
}

internal sealed record Violation(string Source, string Target, string Via)
{
    public override string ToString() => $"{Source} -> {Target} via {Via}";
}

internal enum Layer
{
    None,
    SimulationCombat,
    SimulationRun,
    Content,
    AiCombat,
    AiRun,
    Autoplay,
}

internal static class ArchitectureRules
{
    /// <summary>
    /// <c>Simulation.Run</c> types that <c>Ai.Run</c> may see: only visible projections and
    /// decision requests, registered explicitly. R1a has no such type yet.
    /// </summary>
    internal static readonly IReadOnlySet<string> VisibleRunTypes = new HashSet<string>(
        StringComparer.Ordinal)
    {
        "BlitzSolver.Simulation.Run.RunView",
        "BlitzSolver.Simulation.Run.RunView+PlayerView",
        "BlitzSolver.Simulation.Run.RunView+PotionView",
        "BlitzSolver.Simulation.Run.RunView+PotionViewArray",
        "BlitzSolver.Simulation.Run.RunView+CardView",
        "BlitzSolver.Simulation.Run.RunView+CardViewArray",
        "BlitzSolver.Simulation.Run.RunView+DeckView",
        "BlitzSolver.Simulation.Run.RunView+RelicView",
        "BlitzSolver.Simulation.Run.RunView+RelicViewArray",
        "BlitzSolver.Simulation.Run.RunView+RelicViewSet",
        "BlitzSolver.Simulation.Run.RunView+MapPointView",
        "BlitzSolver.Simulation.Run.RunView+MapPointViewArray",
        "BlitzSolver.Simulation.Run.RunView+MapView",
        "BlitzSolver.Simulation.Run.RunView+RoomView",
        "BlitzSolver.Simulation.Run.RunView+RoomViewArray",
        "BlitzSolver.Simulation.Run.RunView+RoomViewSet",
        "BlitzSolver.Simulation.Run.RunCoord",
        "BlitzSolver.Simulation.Run.RunRef",
        "BlitzSolver.Simulation.Run.RunRefKind",
        "BlitzSolver.Simulation.Run.RunDecision",
        "BlitzSolver.Simulation.Run.RunMapPointType",
        "BlitzSolver.Simulation.Run.RunRoomType",
        "BlitzSolver.Simulation.Run.RunRelicStatus",
        "BlitzSolver.Simulation.Run.RunRelicFlags",
        "BlitzSolver.Simulation.Run.RunLimits",
        "BlitzSolver.Simulation.Run.RunOdds",
        "BlitzSolver.Simulation.Run.RunPlayerOdds",
        "BlitzSolver.Simulation.Run.RunMapStartArray",
        "BlitzSolver.Simulation.Run.RunMapChildren",
    };

    internal static Layer LayerOf(string? ns)
    {
        if (ns is null)
            return Layer.None;
        if (ns == "BlitzSolver.Simulation.Combat" || ns.StartsWith("BlitzSolver.Simulation.Combat.", StringComparison.Ordinal))
            return Layer.SimulationCombat;
        if (ns == "BlitzSolver.Simulation.Run" || ns.StartsWith("BlitzSolver.Simulation.Run.", StringComparison.Ordinal))
            return Layer.SimulationRun;
        if (ns == "BlitzSolver.Content" || ns.StartsWith("BlitzSolver.Content.", StringComparison.Ordinal))
            return Layer.Content;
        if (ns == "BlitzSolver.Ai.Combat" || ns.StartsWith("BlitzSolver.Ai.Combat.", StringComparison.Ordinal))
            return Layer.AiCombat;
        if (ns == "BlitzSolver.Ai.Run" || ns.StartsWith("BlitzSolver.Ai.Run.", StringComparison.Ordinal))
            return Layer.AiRun;
        if (ns == "BlitzSolver.Autoplay" || ns.StartsWith("BlitzSolver.Autoplay.", StringComparison.Ordinal))
            return Layer.Autoplay;
        return Layer.None;
    }

    internal static bool Allows(Layer source, Layer target, Type targetType,
        IReadOnlySet<string> visibleRunTypes)
        => source switch
        {
            Layer.SimulationCombat => false,
            Layer.SimulationRun => target is Layer.SimulationCombat,
            Layer.Content => target is Layer.SimulationCombat or Layer.SimulationRun,
            Layer.AiCombat => target is Layer.SimulationCombat,
            Layer.AiRun => target is Layer.SimulationRun
                && visibleRunTypes.Contains(TypeName(targetType)),
            Layer.Autoplay => target is Layer.SimulationCombat or Layer.SimulationRun
                or Layer.AiCombat or Layer.AiRun,
            _ => true,
        };

    internal static string TypeName(Type type)
        => type.IsGenericType
            ? type.GetGenericTypeDefinition().FullName ?? type.Name
            : type.FullName ?? type.Name;
}

internal static class TypeReferences
{
    internal static IEnumerable<(Type Type, string Via)> Of(Type source)
    {
        Collector collector = new();
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        collector.Add(source.BaseType, "base type");
        foreach (Type contract in source.GetInterfaces())
            collector.Add(contract, "interface");
        foreach (FieldInfo field in source.GetFields(declared))
            collector.Add(field.FieldType, $"field {field.Name}");
        foreach (PropertyInfo property in source.GetProperties(declared))
        {
            collector.Add(property.PropertyType, $"property {property.Name}");
            foreach (ParameterInfo parameter in property.GetIndexParameters())
                collector.Add(parameter.ParameterType, $"property {property.Name} index");
        }
        foreach (EventInfo @event in source.GetEvents(declared))
            collector.Add(@event.EventHandlerType, $"event {@event.Name}");
        foreach (Type nested in source.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            collector.Add(nested, "nested type");
        if (source.IsGenericType)
        {
            foreach (Type argument in source.GetGenericArguments())
            {
                collector.Add(argument, "type generic argument");
                foreach (Type constraint in argument.GetGenericParameterConstraints())
                    collector.Add(constraint, "type constraint");
            }
        }

        foreach (MethodBase method in source.GetMethods(declared).Cast<MethodBase>()
                     .Concat(source.GetConstructors(declared)))
        {
            collector.Add(method is MethodInfo info ? info.ReturnType : typeof(void),
                $"method {method.Name} return");
            foreach (ParameterInfo parameter in method.GetParameters())
                collector.Add(parameter.ParameterType, $"method {method.Name} parameter");
            if (method.IsGenericMethod)
            {
                foreach (Type argument in method.GetGenericArguments())
                {
                    collector.Add(argument, $"method {method.Name} generic argument");
                    foreach (Type constraint in argument.GetGenericParameterConstraints())
                        collector.Add(constraint, $"method {method.Name} constraint");
                }
            }
            collector.AddMethodBody(method);
        }
        return collector.Result;
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opcode => opcode.Value);

    private sealed class Collector
    {
        private readonly Dictionary<Type, string> _types = [];
        private readonly HashSet<Type> _visited = [];

        internal IEnumerable<(Type Type, string Via)> Result
            => _types.Select(pair => (pair.Key, pair.Value));

        internal void Add(Type? type, string via)
        {
            if (type is null || type.IsGenericParameter)
                return;
            if (type.HasElementType)
            {
                Add(type.GetElementType(), via);
                return;
            }
            if (type.IsGenericType)
            {
                Type definition = type.GetGenericTypeDefinition();
                if (definition != type)
                    Add(definition, via);
                foreach (Type argument in type.GetGenericArguments())
                    Add(argument, via);
            }
            if (type.IsFunctionPointer)
            {
                Add(type.GetFunctionPointerReturnType(), via);
                foreach (Type parameter in type.GetFunctionPointerParameterTypes())
                    Add(parameter, via);
            }
            if (_types.ContainsKey(type))
                return;
            _types.Add(type, via);
        }

        internal void AddMethodBody(MethodBase method)
        {
            if (method.GetMethodBody() is not { } body)
                return;
            foreach (LocalVariableInfo local in body.LocalVariables)
                Add(local.LocalType, $"local in {method.Name}");
            foreach (ExceptionHandlingClause clause in body.ExceptionHandlingClauses)
            {
                if (clause.Flags == ExceptionHandlingClauseOptions.Clause)
                    Add(clause.CatchType, $"catch in {method.Name}");
            }
            if (body.GetILAsByteArray() is not { } il)
                return;
            Module module = method.Module;
            Type[] typeArguments = method.DeclaringType is { IsGenericType: true } declaring
                ? declaring.GetGenericArguments()
                : Type.EmptyTypes;
            Type[] methodArguments = method.IsGenericMethod
                ? method.GetGenericArguments()
                : Type.EmptyTypes;
            int index = 0;
            while (index < il.Length)
            {
                short value = il[index++];
                if (value == 0xFE)
                    value = unchecked((short)(0xFE00 | il[index++]));
                if (!OpCodesByValue.TryGetValue(value, out OpCode opcode))
                    throw new InvalidOperationException(
                        $"Unknown IL opcode 0x{value:X4} in {method.DeclaringType?.FullName}.{method.Name}.");
                switch (opcode.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                    case OperandType.ShortInlineBrTarget:
                        index += 1;
                        break;
                    case OperandType.InlineVar:
                        index += 2;
                        break;
                    case OperandType.InlineI:
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineString:
                    case OperandType.InlineSig:
                    case OperandType.ShortInlineR:
                        index += 4;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        index += 8;
                        break;
                    case OperandType.InlineSwitch:
                    {
                        int count = BitConverter.ToInt32(il, index);
                        index += 4 + count * 4;
                        break;
                    }
                    case OperandType.InlineType:
                    {
                        int token = BitConverter.ToInt32(il, index);
                        index += 4;
                        Add(ResolveType(module, token, typeArguments, methodArguments),
                            $"IL type in {method.Name}");
                        break;
                    }
                    case OperandType.InlineMethod:
                    case OperandType.InlineField:
                    case OperandType.InlineTok:
                    {
                        int token = BitConverter.ToInt32(il, index);
                        index += 4;
                        AddMember(module, token, typeArguments, methodArguments, method.Name);
                        break;
                    }
                    default:
                        throw new InvalidOperationException(
                            $"Unhandled IL operand {opcode.OperandType} in "
                            + $"{method.DeclaringType?.FullName}.{method.Name}.");
                }
            }
        }

        private void AddMember(Module module, int token, Type[] typeArguments,
            Type[] methodArguments, string methodName)
        {
            MemberInfo? member;
            try
            {
                member = module.ResolveMember(token, typeArguments, methodArguments);
            }
            catch (ArgumentException)
            {
                return;
            }
            catch (BadImageFormatException)
            {
                return;
            }
            catch (TypeLoadException)
            {
                return;
            }
            catch (NotSupportedException)
            {
                return;
            }
            switch (member)
            {
                case Type type:
                    Add(type, $"IL type in {methodName}");
                    break;
                case MethodBase method:
                    Add(method.DeclaringType, $"IL method {method.Name} in {methodName}");
                    if (method is MethodInfo info)
                    {
                        Add(info.ReturnType, $"IL method {method.Name} return");
                        foreach (Type argument in info.GetGenericArguments())
                            Add(argument, $"IL method {method.Name} generic argument");
                    }
                    foreach (ParameterInfo parameter in method.GetParameters())
                        Add(parameter.ParameterType, $"IL method {method.Name} parameter");
                    break;
                case FieldInfo field:
                    Add(field.DeclaringType, $"IL field {field.Name} in {methodName}");
                    Add(field.FieldType, $"IL field {field.Name} type");
                    break;
                case PropertyInfo property:
                    Add(property.DeclaringType, $"IL property {property.Name} in {methodName}");
                    Add(property.PropertyType, $"IL property {property.Name} type");
                    break;
                case EventInfo @event:
                    Add(@event.DeclaringType, $"IL event {@event.Name} in {methodName}");
                    Add(@event.EventHandlerType, $"IL event {@event.Name} type");
                    break;
            }
        }

        private static Type? ResolveType(Module module, int token, Type[] typeArguments,
            Type[] methodArguments)
        {
            try
            {
                return module.ResolveType(token, typeArguments, methodArguments);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }
            catch (TypeLoadException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }
}
