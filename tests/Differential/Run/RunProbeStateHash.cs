// Probe-only semantic hash. Raw RunCodec bytes/hash remain intact and are reported separately.
// R3a evidence: RunRef's byte at offset 1 is implicit CLR alignment, not a model field.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BlitzSolver.Simulation.Run;

namespace Differential;

internal static class RunProbeStateHash
{
    internal static readonly int[] RunRefPaddingOffsets = Collect();

    internal static string Semantic(byte[] encoded)
    {
        byte[] copy = (byte[])encoded.Clone();
        foreach (int offset in RunRefPaddingOffsets) copy[RunCodec.HeaderSize + offset] = 0;
        return Convert.ToHexString(SHA256.HashData(copy));
    }

    private static int[] Collect()
    {
        List<int> result = [];
        Visit(typeof(BlitzSolver.Simulation.Run.RunState), 0);
        return [.. result];

        void Visit(Type type, int start)
        {
            if (type.IsPrimitive || type.IsEnum) return;
            if (!type.IsValueType) throw new InvalidDataException($"RunState出现引用字段：{type}");
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (type == typeof(RunRef))
            {
                // Assert the exact measured layout; never drop an arbitrary byte after a change.
                if (Size(type) != 4 || fields.Length != 2
                    || fields.Count(f => f.FieldType == typeof(RunRefKind) && Offset(type, f) == 0) != 1
                    || fields.Count(f => f.FieldType == typeof(ushort) && Offset(type, f) == 2) != 1)
                    throw new InvalidDataException("RunRef padding布局改变");
                result.Add(start + 1);
                return;
            }
            if (type.GetCustomAttribute<InlineArrayAttribute>() is { } array)
            {
                FieldInfo element = fields.Single();
                int stride = Size(element.FieldType);
                for (int i = 0; i < array.Length; i++) Visit(element.FieldType, start + i * stride);
                return;
            }
            foreach (FieldInfo field in fields) Visit(field.FieldType, start + Offset(type, field));
        }
    }

    private static int Offset(Type type, FieldInfo field) => checked((int)Marshal.OffsetOf(type, field.Name));
    private static int Size(Type type) => (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!
        .MakeGenericMethod(type).Invoke(null, null)!;
}
