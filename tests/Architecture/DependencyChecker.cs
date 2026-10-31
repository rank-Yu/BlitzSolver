using System.Reflection;
using System.Reflection.Emit;

namespace Sts2Solver.Architecture;

/// <summary>检查编译后的类型签名、字段和 IL 引用，约束战斗核心的依赖方向。</summary>
internal static class DependencyChecker
{
    internal static IReadOnlyList<Violation> Check(Assembly assembly)
    {
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
                if (target == Layer.None || target == source || ArchitectureRules.Allows(source, target))
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

internal enum Layer { None, SimulationCombat, Content, AiCombat, Autoplay }

internal static class ArchitectureRules
{
    internal static Layer LayerOf(string? ns)
    {
        if (ns is null) return Layer.None;
        if (In(ns, "Sts2Solver.Simulation.Combat")) return Layer.SimulationCombat;
        if (In(ns, "Sts2Solver.Content")) return Layer.Content;
        if (In(ns, "Sts2Solver.Ai.Combat")) return Layer.AiCombat;
        if (In(ns, "Sts2Solver.Autoplay")) return Layer.Autoplay;
        return Layer.None;
    }

    private static bool In(string ns, string layer)
        => ns == layer || ns.StartsWith(layer + ".", StringComparison.Ordinal);

    internal static bool Allows(Layer source, Layer target) => source switch
    {
        Layer.SimulationCombat => false,
        Layer.Content or Layer.AiCombat => target == Layer.SimulationCombat,
        Layer.Autoplay => target is Layer.SimulationCombat or Layer.AiCombat,
        _ => false,
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
