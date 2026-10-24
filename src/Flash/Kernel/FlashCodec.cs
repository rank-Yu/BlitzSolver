using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CombatSolver.Flash.Kernel;

/// <summary>
/// Deterministic little-endian primitives shared by the Flash golden codecs. The writer never emits
/// timestamps, unordered dictionary enumeration or runtime-dependent values; the reader rejects
/// truncated input and unknown versions explicitly.
/// </summary>
internal static class FlashCodec
{
    internal const ushort FormatVersion = 1;

    internal static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static void WriteString(BinaryWriter writer, string? value)
    {
        if (value is null)
        {
            writer.Write(-1);
            return;
        }
        byte[] bytes = Utf8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    internal static string? ReadString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0)
            return null;
        if (length > 1 << 20)
            throw new InvalidDataException($"字符串长度 {length} 超出上限。");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new InvalidDataException("字符串数据被截断。");
        return Utf8.GetString(bytes);
    }

    internal static void WriteDecimal(BinaryWriter writer, decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        for (int index = 0; index < bits.Length; index++)
            writer.Write(bits[index]);
    }

    internal static decimal ReadDecimal(BinaryReader reader)
    {
        int a = reader.ReadInt32();
        int b = reader.ReadInt32();
        int c = reader.ReadInt32();
        int flags = reader.ReadInt32();
        return new decimal([a, b, c, flags]);
    }

    internal static void WriteDecimals(BinaryWriter writer, decimal[] values)
    {
        writer.Write(values.Length);
        for (int index = 0; index < values.Length; index++)
            WriteDecimal(writer, values[index]);
    }

    internal static decimal[] ReadDecimals(BinaryReader reader)
    {
        int count = ReadCount(reader, 1 << 16, "decimal 数组");
        decimal[] values = new decimal[count];
        for (int index = 0; index < count; index++)
            values[index] = ReadDecimal(reader);
        return values;
    }

    internal static void WriteIntArray(BinaryWriter writer, int[] values)
    {
        writer.Write(values.Length);
        for (int index = 0; index < values.Length; index++)
            writer.Write(values[index]);
    }

    internal static int[] ReadIntArray(BinaryReader reader, int max, string what)
    {
        int count = ReadCount(reader, max, what);
        int[] values = new int[count];
        for (int index = 0; index < count; index++)
            values[index] = reader.ReadInt32();
        return values;
    }

    internal static void WriteUShortArray(BinaryWriter writer, ushort[] values)
    {
        writer.Write(values.Length);
        for (int index = 0; index < values.Length; index++)
            writer.Write(values[index]);
    }

    internal static ushort[] ReadUShortArray(BinaryReader reader, int max, string what)
    {
        int count = ReadCount(reader, max, what);
        ushort[] values = new ushort[count];
        for (int index = 0; index < count; index++)
            values[index] = reader.ReadUInt16();
        return values;
    }

    internal static int ReadCount(BinaryReader reader, int max, string what)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > max)
            throw new InvalidDataException($"{what}元素个数 {count} 不在 0..{max} 内。");
        return count;
    }

    internal static bool ReadBool(BinaryReader reader)
    {
        byte value = reader.ReadByte();
        if (value > 1)
            throw new InvalidDataException($"布尔值 {value} 非法。");
        return value != 0;
    }

    /// <summary>Rejects bytes after the declared payload; callers use it to catch trailing corruption.</summary>
    internal static void ExpectEnd(BinaryReader reader, Stream stream, string what)
    {
        if (stream.Position != stream.Length)
            throw new InvalidDataException($"{what} 尾部有 {stream.Length - stream.Position} 个未消费字节。");
    }

    internal static byte[] ToBytes(Action<BinaryWriter> write)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Utf8, leaveOpen: true))
            write(writer);
        return stream.ToArray();
    }

    internal static T Read<T>(byte[] bytes, string what, Func<BinaryReader, T> read)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using BinaryReader reader = new(stream, Utf8, leaveOpen: true);
        T value = read(reader);
        ExpectEnd(reader, stream, what);
        return value;
    }

    internal static byte[] Sha256(ReadOnlySpan<byte> bytes) => SHA256.HashData(bytes);

    internal static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(Sha256(bytes)).ToLowerInvariant();

    /// <summary>The truncated SHA-256 of the complete <see cref="FlashBattle"/> byte image (64 bits).</summary>
    internal static ulong BattleHash(in FlashBattle battle)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(FlashFingerprint.Bytes(battle), hash);
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }

    internal static ulong Sha256U64(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }
}

/// <summary>
/// Whole-block <see cref="FlashBattle"/> file: version, the exact struct size and the complete byte
/// image. The struct is unmanaged and reference-free, so the byte image is the complete state.
/// </summary>
public static class FlashBattleCodec
{
    private const uint Magic = 0x54414246; // "FBAT"

    /// <summary>Encoded header bytes: magic, version, size.</summary>
    public const int HeaderSize = 4 + 2 + 4;

    public static byte[] Encode(in FlashBattle battle)
    {
        int size = Unsafe.SizeOf<FlashBattle>();
        if (size != FlashBattle.ExpectedSize)
            throw new InvalidOperationException(
                $"FlashBattle 大小 {size} 与布局断言 {FlashBattle.ExpectedSize} 不符。");
        byte[] bytes = new byte[HeaderSize + size];
        Span<byte> header = bytes.AsSpan(0, HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], FlashCodec.FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[6..], size);
        FlashFingerprint.Bytes(battle).CopyTo(bytes.AsSpan(HeaderSize));
        return bytes;
    }

    public static FlashBattle Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
            throw new InvalidDataException($"FlashBattle 数据只有 {bytes.Length} 字节，短于头部 {HeaderSize}。");
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (magic != Magic)
            throw new InvalidDataException($"FlashBattle 魔数 {magic:X8} 不符。");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        if (version != FlashCodec.FormatVersion)
            throw new InvalidDataException($"FlashBattle 格式版本 {version} 不受支持（当前 {FlashCodec.FormatVersion}）。");
        int declared = BinaryPrimitives.ReadInt32LittleEndian(bytes[6..]);
        int current = Unsafe.SizeOf<FlashBattle>();
        if (declared != current || current != FlashBattle.ExpectedSize)
            throw new InvalidDataException(
                $"FlashBattle 大小 {declared} 与当前 {current}（布局断言 {FlashBattle.ExpectedSize}）不符。");
        if (bytes.Length != HeaderSize + declared)
            throw new InvalidDataException(
                $"FlashBattle 数据长度 {bytes.Length} 与声明 {HeaderSize + declared} 不符。");
        FlashBattle battle = default;
        bytes[HeaderSize..].CopyTo(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref battle, 1)));
        Validate(in battle);
        return battle;
    }

    /// <summary>Rejects counts that cannot describe a canonical state; later reads would be out of range.</summary>
    public static void Validate(in FlashBattle battle)
    {
        if (battle.HandCount + battle.DrawCount + battle.DiscardCount + battle.ExhaustCount + battle.PlayCount
            > FlashLimits.Cards)
            throw new InvalidDataException("FlashBattle 卡牌计数超出数组容量。");
        if (battle.EnemyCount > FlashLimits.Enemies || battle.RosterCount > FlashLimits.Enemies)
            throw new InvalidDataException("FlashBattle 敌人计数超出数组容量。");
        if (battle.PowerCount > FlashLimits.Powers || battle.RelicCount > FlashLimits.Relics
            || battle.OrbCount > FlashLimits.Orbs)
            throw new InvalidDataException("FlashBattle Power/遗物/球计数超出数组容量。");
        if (battle.PotionSlotCount > FlashLimits.PotionSlots)
            throw new InvalidDataException("FlashBattle 药水槽计数超出容量。");
        if (battle.DecimalVarCount > FlashLimits.DecimalCardVars)
            throw new InvalidDataException("FlashBattle decimal 变量计数超出容量。");
        if (battle.RosterCount > battle.EnemyCount)
            throw new InvalidDataException("FlashBattle roster 数量大于已知敌人数。");
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            if (battle.Enemies[index].Rules >= FlashLimits.MonsterRules)
                throw new InvalidDataException($"FlashBattle 敌人 {index} 的规则下标越界。");
        }
        for (int index = 0; index < battle.CardCount; index++)
        {
            if (battle.Cards[index].Id >= FlashRegistry.CardCount)
                throw new InvalidDataException($"FlashBattle 卡牌 {index} 的内容下标越界。");
        }
    }
}
