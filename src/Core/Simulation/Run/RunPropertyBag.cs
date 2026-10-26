using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Canonical, fixed-capacity encoding of a native <c>SavedProperties</c> bag.
/// <para>
/// Layout: one length byte followed by self-delimiting entries sorted by property-name hash.
/// Each entry is <c>ulong nameHash, byte kind, payload</c>. Supported payloads are int, bool,
/// UTF-8 string, int[], model reference, one card and a card array. Cards recurse into their own
/// property bags. The importer and the independent native dump use the same writer, so equal native
/// bags always have the same bytes; names are hashed rather than stored to keep the state reference
/// free. R2 maps its known property names to the same hashes.
/// </para>
/// </summary>
public static class RunProperties
{
    public const byte KindInt = 0;
    public const byte KindBool = 1;
    public const byte KindString = 2;
    public const byte KindIntArray = 3;
    public const byte KindModel = 4;
    public const byte KindCard = 5;
    public const byte KindCardArray = 6;

    /// <summary>Order-independent hash of a saved-property name (xxHash64 over UTF-8).</summary>
    public static ulong NameHash(string name)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        return RunHash.XxHash64(bytes.AsSpan());
    }

    public static void Init(ref RunPropertyBag bag) => bag.Length = 0;

    public static void AddInt(ref RunPropertyBag bag, ulong nameHash, int value,
        int capacity = RunLimits.CardPropertyBytes)
    {
        CheckCapacity(capacity);
        RunPropertyBag local = bag;
        Span<byte> bytes = Storage(ref local);
        int position = Begin(bytes, local.Length, capacity, nameHash, KindInt);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[position..], value);
        position += 4;
        End(ref bag, ref local, position);
    }

    public static void AddBool(ref RunPropertyBag bag, ulong nameHash, bool value,
        int capacity = RunLimits.CardPropertyBytes)
    {
        CheckCapacity(capacity);
        RunPropertyBag local = bag;
        Span<byte> bytes = Storage(ref local);
        int position = Begin(bytes, local.Length, capacity, nameHash, KindBool);
        bytes[position++] = value ? (byte)1 : (byte)0;
        End(ref bag, ref local, position);
    }

    public static void AddString(ref RunPropertyBag bag, ulong nameHash, scoped ReadOnlySpan<byte> value,
        int capacity = RunLimits.CardPropertyBytes)
    {
        if (value.Length > RunLimits.PropertyStringBytes)
            throw new CapacityException("RunProperties.String", RunLimits.PropertyStringBytes);
        CheckCapacity(capacity);
        RunPropertyBag local = bag;
        Span<byte> bytes = Storage(ref local);
        int position = Begin(bytes, local.Length, capacity, nameHash, KindString);
        Ensure(position - 1, 1 + value.Length, capacity);
        bytes[position++] = (byte)value.Length;
        value.CopyTo(bytes[position..]);
        position += value.Length;
        End(ref bag, ref local, position);
    }

    public static void AddIntArray(ref RunPropertyBag bag, ulong nameHash, scoped ReadOnlySpan<int> value,
        int capacity = RunLimits.CardPropertyBytes)
    {
        if (value.Length > RunLimits.PropertyInts)
            throw new CapacityException("RunProperties.IntArray", RunLimits.PropertyInts);
        CheckCapacity(capacity);
        RunPropertyBag local = bag;
        Span<byte> bytes = Storage(ref local);
        int position = Begin(bytes, local.Length, capacity, nameHash, KindIntArray);
        Ensure(position - 1, 1 + value.Length * 4, capacity);
        bytes[position++] = (byte)value.Length;
        foreach (int item in value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes[position..], item);
            position += 4;
        }
        End(ref bag, ref local, position);
    }

    public static void AddModel(ref RunPropertyBag bag, ulong nameHash, RunRef value,
        int capacity = RunLimits.CardPropertyBytes)
    {
        CheckCapacity(capacity);
        RunPropertyBag local = bag;
        Span<byte> bytes = Storage(ref local);
        int position = Begin(bytes, local.Length, capacity, nameHash, KindModel);
        Ensure(position - 1, 3, capacity);
        bytes[position++] = (byte)value.Kind;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[position..], value.Id);
        position += 2;
        End(ref bag, ref local, position);
    }

    public static void AddCard(ref RunPropertyBag bag, ulong nameHash, in RunCard value,
        int capacity = RunLimits.CardPropertyBytes)
    {
        CheckCapacity(capacity);
        RunPropertyBag local = bag;
        Span<byte> bytes = Storage(ref local);
        int position = Begin(bytes, local.Length, capacity, nameHash, KindCard);
        position = WriteCard(bytes, position, capacity, value);
        End(ref bag, ref local, position);
    }

    public static void AddCardArray(ref RunPropertyBag bag, ulong nameHash,
        scoped ReadOnlySpan<RunCard> value, int capacity = RunLimits.CardPropertyBytes)
    {
        if (value.Length > RunLimits.PropertyCards)
            throw new CapacityException("RunProperties.CardArray", RunLimits.PropertyCards);
        CheckCapacity(capacity);
        RunPropertyBag local = bag;
        Span<byte> bytes = Storage(ref local);
        int position = Begin(bytes, local.Length, capacity, nameHash, KindCardArray);
        Ensure(position - 1, 1, capacity);
        bytes[position++] = (byte)value.Length;
        foreach (RunCard card in value)
            position = WriteCard(bytes, position, capacity, card);
        End(ref bag, ref local, position);
    }

    /// <summary>Appends an already encoded entry, preserving the target's capacity.</summary>
    public static void Append(ref RunPropertyBag target, in RunPropertyBag entry,
        int capacity = RunLimits.CardPropertyBytes)
    {
        CheckCapacity(capacity);
        if (target.Length + entry.Length > capacity)
            throw new CapacityException("RunPropertyBag", capacity);
        RunPropertyBag local = target;
        Span<byte> bytes = Storage(ref local);
        entry.Bytes[..entry.Length].CopyTo(bytes[(1 + local.Length)..]);
        local.Length = (byte)(local.Length + entry.Length);
        target = local;
    }

    /// <summary>Encodes one card value into an existing property-bag payload.</summary>
    public static int WriteCard(Span<byte> bytes, int position, int capacity, in RunCard card)
    {
        int size = 2 + 1 + 1 + 2 + 2 + 4 + 1 + card.Properties.Length
            + 1 + card.EnchantmentProperties.Length;
        Ensure(position - 1, size, capacity);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[position..], card.Id);
        position += 2;
        bytes[position++] = card.Upgrade;
        bytes[position++] = card.HasEnchantment;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[position..], card.EnchantmentId);
        position += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[position..], unchecked((ushort)card.EnchantmentAmount));
        position += 2;
        BinaryPrimitives.WriteInt32LittleEndian(bytes[position..], card.FloorAddedToDeck);
        position += 4;
        position = WriteBag(bytes, position, card.Properties);
        return WriteBag(bytes, position, card.EnchantmentProperties);
    }

    private static int WriteBag(Span<byte> bytes, int position, in RunPropertyBag bag)
    {
        if (bag.Length > RunLimits.RelicPropertyBytes)
            throw new CapacityException("RunPropertyBag", RunLimits.RelicPropertyBytes);
        bytes[position++] = bag.Length;
        bag.Bytes[..bag.Length].CopyTo(bytes[position..]);
        return position + bag.Length;
    }

    private static Span<byte> Storage(ref RunPropertyBag bag)
        => MemoryMarshal.CreateSpan(ref Unsafe.As<RunPropertyBag, byte>(ref bag),
            Unsafe.SizeOf<RunPropertyBag>());

    private static void CheckCapacity(int capacity)
    {
        if ((uint)capacity > (uint)RunLimits.RelicPropertyBytes)
            throw new ArgumentOutOfRangeException(nameof(capacity));
    }

    private static int Begin(Span<byte> bytes, int used, int capacity, ulong nameHash, byte kind)
    {
        int position = 1 + used;
        Ensure(used, 9, capacity);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[position..], nameHash);
        position += 8;
        bytes[position++] = kind;
        return position;
    }

    private static void End(ref RunPropertyBag target, ref RunPropertyBag local, int position)
    {
        local.Length = (byte)(position - 1);
        target = local;
    }

    private static void Ensure(int used, int count, int capacity)
    {
        if (used + count > capacity)
            throw new CapacityException("RunPropertyBag", capacity);
    }

    /// <summary>Structural validation used by <see cref="RunCodec"/> after decode.</summary>
    public static void Validate(in RunPropertyBag bag, int capacity, string what)
    {
        if (bag.Length > capacity)
            throw new InvalidDataException($"{what} 属性包长度 {bag.Length} 超出 {capacity}。");
        Reader reader = new(bag);
        int count = 0;
        while (reader.TryNext(out _, out byte kind))
        {
            if (++count > RunLimits.PropertyCount)
                throw new InvalidDataException($"{what} 属性条目超过 {RunLimits.PropertyCount}。");
            switch (kind)
            {
                case KindInt:
                    _ = reader.ReadInt();
                    break;
                case KindBool:
                    _ = reader.ReadBool();
                    break;
                case KindString:
                    _ = reader.ReadString();
                    break;
                case KindIntArray:
                {
                    int length = reader.ReadByte();
                    if (length > RunLimits.PropertyInts)
                        throw new InvalidDataException($"{what} int[] 长度 {length} 超出。");
                    for (int index = 0; index < length; index++)
                        _ = reader.ReadInt();
                    break;
                }
                case KindModel:
                    _ = reader.ReadModel();
                    break;
                case KindCard:
                    _ = reader.ReadCard();
                    break;
                case KindCardArray:
                {
                    int length = reader.ReadByte();
                    if (length > RunLimits.PropertyCards)
                        throw new InvalidDataException($"{what} 卡数组长度 {length} 超出。");
                    for (int index = 0; index < length; index++)
                        _ = reader.ReadCard();
                    break;
                }
                default:
                    throw new InvalidDataException($"{what} 属性种类 {kind} 未知。");
            }
        }
        if (reader.Remaining != 0)
            throw new InvalidDataException($"{what} 属性包尾部有 {reader.Remaining} 个未消费字节。");
    }

    /// <summary>Reader for validation and for R2 content that knows the property names.</summary>
    public ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _buffer;
        private int _position;

        public Reader(in RunPropertyBag bag)
        {
            if (bag.Length > RunLimits.RelicPropertyBytes)
                throw new InvalidDataException($"属性包长度 {bag.Length} 超出上限。");
            _buffer = bag.Bytes[..bag.Length];
            _position = 0;
        }

        public readonly int Remaining => _buffer.Length - _position;

        public bool TryNext(out ulong nameHash, out byte kind)
        {
            if (Remaining == 0)
            {
                nameHash = 0;
                kind = 0;
                return false;
            }
            if (Remaining < 9)
                throw new InvalidDataException("属性条目头部被截断。");
            nameHash = BinaryPrimitives.ReadUInt64LittleEndian(_buffer[_position..]);
            _position += 8;
            kind = _buffer[_position++];
            return true;
        }

        public int ReadInt() => ReadInt32();
        public bool ReadBool() => ReadByte() != 0;
        public byte ReadByte() => _position < _buffer.Length
            ? _buffer[_position++]
            : throw new InvalidDataException("属性值被截断。");
        public int ReadInt32()
        {
            if (_position + 4 > _buffer.Length)
                throw new InvalidDataException("属性 int 被截断。");
            int value = BinaryPrimitives.ReadInt32LittleEndian(_buffer[_position..]);
            _position += 4;
            return value;
        }
        public ushort ReadUInt16()
        {
            if (_position + 2 > _buffer.Length)
                throw new InvalidDataException("属性 ushort 被截断。");
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer[_position..]);
            _position += 2;
            return value;
        }
        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (_position + count > _buffer.Length)
                throw new InvalidDataException("属性字节被截断。");
            ReadOnlySpan<byte> value = _buffer.Slice(_position, count);
            _position += count;
            return value;
        }
        public RunRef ReadModel() => new((RunRefKind)ReadByte(), ReadUInt16());
        public string ReadString()
        {
            int length = ReadByte();
            return Encoding.UTF8.GetString(ReadBytes(length));
        }
        public RunCard ReadCard() => ReadCard(1);
        public RunCard ReadCard(int depth)
        {
            if (depth > 2)
                throw new InvalidDataException("属性卡嵌套超过两层。");
            RunCard card = default;
            card.Id = ReadUInt16();
            card.Upgrade = ReadByte();
            card.HasEnchantment = ReadByte();
            card.EnchantmentId = ReadUInt16();
            card.EnchantmentAmount = unchecked((short)ReadUInt16());
            card.FloorAddedToDeck = ReadInt32();
            card.Properties = ReadBag();
            card.EnchantmentProperties = ReadBag();
            return card;
        }
        private RunPropertyBag ReadBag()
        {
            int length = ReadByte();
            RunPropertyBag bag = default;
            if (length > RunLimits.RelicPropertyBytes)
                throw new InvalidDataException($"嵌套属性包长度 {length} 超出上限。");
            ReadBytes(length).CopyTo(bag.Bytes);
            bag.Length = (byte)length;
            return bag;
        }
    }
}

/// <summary>Fixed-size saved-property bytes; <see cref="Length"/> is the used prefix.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunPropertyBag
{
    public byte Length;
    public RunPropertyBytes Bytes;
}

[InlineArray(RunLimits.RelicPropertyBytes)]
public struct RunPropertyBytes
{
    private byte _first;
}
