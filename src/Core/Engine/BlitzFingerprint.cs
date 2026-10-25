using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlitzSolver.Engine;

/// <summary>
/// Byte-image identity of Blitz values. The hash only buckets and covers the used part of each
/// region (unused slots are zero in canonical form); equality always compares every byte, so two
/// states are merged only when they are identical.
/// </summary>
public static class BlitzFingerprint
{
    private const ulong Prime1 = 0x9E3779B185EBCA87UL;
    private const ulong Prime2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong Prime3 = 0x165667B19E3779F9UL;

    private static readonly int EnemiesOffset = Offset(static (ref BlitzBattle b) => ref Unsafe.As<BlitzEnemy, byte>(ref b.Enemies[0]));
    private static readonly int PowersOffset = Offset(static (ref BlitzBattle b) => ref Unsafe.As<BlitzPower, byte>(ref b.Powers[0]));
    private static readonly int RelicsOffset = Offset(static (ref BlitzBattle b) => ref Unsafe.As<BlitzRelic, byte>(ref b.Relics[0]));
    private static readonly int OrbsOffset = Offset(static (ref BlitzBattle b) => ref Unsafe.As<BlitzOrb, byte>(ref b.Orbs[0]));
    private static readonly int CardsOffset = Offset(static (ref BlitzBattle b) => ref Unsafe.As<BlitzCard, byte>(ref b.Cards[0]));
    private static readonly int DecimalVarsOffset = Offset(static (ref BlitzBattle b) => ref Unsafe.As<decimal, byte>(ref b.DecimalVars[0]));
    private static readonly int DecimalVarsEndOffset = DecimalVarsOffset + BlitzLimits.DecimalCardVars * Unsafe.SizeOf<decimal>();

    private delegate ref byte FieldSelector(ref BlitzBattle battle);

    public static ReadOnlySpan<byte> Bytes(in BlitzBattle battle)
        => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in battle, 1));

    public static ulong Hash(in BlitzBattle battle)
    {
        ReadOnlySpan<byte> all = Bytes(battle);
        ulong hash = Prime3;
        // Roots without decimal card content keep the table zero. Hash the header in two pieces so
        // those roots do not pay for 4 * sizeof(decimal) bytes on every state hash; equality still
        // compares the complete byte image.
        hash = Mix(hash, all[..DecimalVarsOffset]);
        int decimalBytes = battle.DecimalVarCount * Unsafe.SizeOf<decimal>();
        if (decimalBytes > 0)
            hash = Mix(hash, all.Slice(DecimalVarsOffset, decimalBytes));
        hash = Mix(hash, all[DecimalVarsEndOffset..EnemiesOffset]);
        hash = Mix(hash, all.Slice(EnemiesOffset, battle.EnemyCount * Unsafe.SizeOf<BlitzEnemy>()));
        hash = Mix(hash, all.Slice(PowersOffset, battle.PowerCount * Unsafe.SizeOf<BlitzPower>()));
        hash = Mix(hash, all.Slice(RelicsOffset, battle.RelicCount * Unsafe.SizeOf<BlitzRelic>()));
        hash = Mix(hash, all.Slice(OrbsOffset, battle.OrbCount * Unsafe.SizeOf<BlitzOrb>()));
        hash = Mix(hash, all.Slice(CardsOffset, battle.CardCount * Unsafe.SizeOf<BlitzCard>()));
        // The HistoryCourse cross-turn slot sits after the card array. Skip it when absent (the
        // canonical bytes are zero) so roots without the relic do not pay 92 extra hashed bytes.
        if ((battle.Flags & BlitzBattleFlags.HistoryCourseCardPresent) != 0)
        {
            int historyCourseOffset = CardsOffset + BlitzLimits.Cards * Unsafe.SizeOf<BlitzCard>();
            hash = Mix(hash, all.Slice(historyCourseOffset, Unsafe.SizeOf<BlitzCard>()));
        }
        hash ^= hash >> 33;
        hash *= Prime2;
        hash ^= hash >> 29;
        return hash;
    }

    public static bool Equal(in BlitzBattle left, in BlitzBattle right) => Bytes(left).SequenceEqual(Bytes(right));

    public static bool Equal(in BlitzCard left, in BlitzCard right)
        => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in left, 1))
            .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in right, 1)));

    /// <summary>
    /// Stable card-state hash used by retention-only projection keys (the projected shuffle order and
    /// the unordered pile multiset). It hashes the complete card byte image, so byte-identical cards
    /// hash equally without reading model references.
    /// </summary>
    public static ulong HashCard(in BlitzCard card)
        => Mix(Prime3, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in card, 1)));

    /// <summary>Four independent lanes over 32-byte blocks, then single words, then the byte tail.</summary>
    private static ulong Mix(ulong seed, ReadOnlySpan<byte> data)
    {
        ulong v0 = seed + Prime1 + Prime2, v1 = seed + Prime2, v2 = seed, v3 = seed - Prime1;
        ReadOnlySpan<ulong> words = MemoryMarshal.Cast<byte, ulong>(data);
        int index = 0;
        for (; index + 4 <= words.Length; index += 4)
        {
            v0 = Round(v0, words[index]);
            v1 = Round(v1, words[index + 1]);
            v2 = Round(v2, words[index + 2]);
            v3 = Round(v3, words[index + 3]);
        }
        ulong hash = BitOperations.RotateLeft(v0, 1) + BitOperations.RotateLeft(v1, 7)
            + BitOperations.RotateLeft(v2, 12) + BitOperations.RotateLeft(v3, 18);
        for (; index < words.Length; index++)
            hash = BitOperations.RotateLeft(hash ^ Round(0, words[index]), 27) * Prime1 + Prime3;
        int tail = data.Length & 7;
        if (tail != 0)
        {
            ulong last = 0;
            data[^tail..].CopyTo(MemoryMarshal.AsBytes(new Span<ulong>(ref last)));
            hash = BitOperations.RotateLeft(hash ^ Round(0, last), 27) * Prime1 + Prime3;
        }
        return hash ^ (ulong)data.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Round(ulong accumulator, ulong word)
        => BitOperations.RotateLeft(accumulator + word * Prime2, 31) * Prime1;

    private static int Offset(FieldSelector selector)
    {
        BlitzBattle probe = default;
        return (int)Unsafe.ByteOffset(ref Unsafe.As<BlitzBattle, byte>(ref probe), ref selector(ref probe));
    }
}
