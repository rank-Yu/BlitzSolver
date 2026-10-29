using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sts2Solver.Simulation.Run;

/// <summary>
/// Implicit CLR alignment bytes only. Declared fields (including explicit reserved fields) are
/// always retained. Build once at the codec boundary; no reflection occurs during later encodes.
/// </summary>
internal static class RunCodecPadding
{
    private static readonly (int Offset, int Length)[] Ranges = Collect();

    internal static void Clear(Span<byte> payload)
    {
        foreach ((int offset, int length) in Ranges)
            payload.Slice(offset, length).Clear();
    }

    private static (int Offset, int Length)[] Collect()
    {
        Dictionary<Type, int[]> cache = [];
        int[] padding = Visit(typeof(RunState));
        List<(int Offset, int Length)> ranges = [];
        for (int index = 0; index < padding.Length;)
        {
            int start = padding[index++];
            int end = start + 1;
            while (index < padding.Length && padding[index] == end)
            {
                index++;
                end++;
            }
            ranges.Add((start, end - start));
        }
        return [.. ranges];

        int[] Visit(Type type)
        {
            if (cache.TryGetValue(type, out int[]? existing))
                return existing;
            // Marshal.OffsetOf is valid for this managed image only when every leaf is blittable.
            // In particular bool/char, platform-sized integers and pointers must fail explicitly.
            if (type.IsEnum)
                return Visit(Enum.GetUnderlyingType(type));
            if (type == typeof(byte) || type == typeof(sbyte)
                || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint)
                || type == typeof(long) || type == typeof(ulong)
                || type == typeof(float) || type == typeof(double))
                return cache[type] = [];
            if (type.IsPrimitive || !type.IsValueType || !type.IsLayoutSequential)
                throw new InvalidOperationException($"RunCodec 不支持布局类型 {type}。");

            int size = Size(type);
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic);
            if (fields.Any(field => field.IsDefined(typeof(MarshalAsAttribute), inherit: false)
                || field.IsDefined(typeof(FixedBufferAttribute), inherit: false)))
                throw new InvalidOperationException($"RunCodec 不支持字段 MarshalAs/FixedBuffer：{type}。");
            List<int> result = [];
            if (type.GetCustomAttribute<InlineArrayAttribute>() is { } array)
            {
                if (fields.Length != 1 || Offset(type, fields[0]) != 0 || array.Length <= 0)
                    throw new InvalidOperationException($"RunCodec InlineArray 布局非法：{type}。");
                Type element = fields[0].FieldType;
                int[] elementPadding = Visit(element);
                int stride = Size(element);
                if (size != checked(stride * array.Length))
                    throw new InvalidOperationException($"RunCodec InlineArray 步长不符：{type}。");
                for (int index = 0; index < array.Length; index++)
                    foreach (int offset in elementPadding)
                        result.Add(index * stride + offset);
            }
            else
            {
                bool[] covered = new bool[size];
                foreach (FieldInfo field in fields)
                {
                    int[] nested = Visit(field.FieldType);
                    int start = Offset(type, field);
                    int end = checked(start + Size(field.FieldType));
                    if (start < 0 || end > size)
                        throw new InvalidOperationException($"RunCodec 字段越界：{type}.{field.Name}。");
                    for (int offset = start; offset < end; offset++)
                    {
                        if (covered[offset])
                            throw new InvalidOperationException($"RunCodec 字段重叠：{type}.{field.Name}。");
                        covered[offset] = true;
                    }
                    foreach (int offset in nested)
                        result.Add(start + offset);
                }
                for (int offset = 0; offset < size; offset++)
                    if (!covered[offset]) result.Add(offset);
            }
            result.Sort();
            return cache[type] = [.. result];
        }
    }

    private static int Offset(Type type, FieldInfo field)
        => checked((int)Marshal.OffsetOf(type, field.Name));

    private static int Size(Type type) => (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!
        .MakeGenericMethod(type).Invoke(null, null)!;
}
