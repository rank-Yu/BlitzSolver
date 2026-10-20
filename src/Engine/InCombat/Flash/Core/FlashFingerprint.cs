using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Byte-image identity of Flash values. The hash only buckets and covers the used part of each
/// region (unused slots are zero in canonical form); equality always compares every byte, so two
/// states are merged only when they are identical.
/// </summary>
public static class FlashFingerprint
{
    private const ulong Prime1 = 0x9E3779B185EBCA87UL;
    private const ulong Prime2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong Prime3 = 0x165667B19E3779F9UL;

    private static readonly int EnemiesOffset = Offset(static (ref FlashBattle b) => ref Unsafe.As<FlashEnemy, byte>(ref b.Enemies[0]));
    private static readonly int PowersOffset = Offset(static (ref FlashBattle b) => ref Unsafe.As<FlashPower, byte>(ref b.Powers[0]));
    private static readonly int RelicsOffset = Offset(static (ref FlashBattle b) => ref Unsafe.As<FlashRelic, byte>(ref b.Relics[0]));
    private static readonly int OrbsOffset = Offset(static (ref FlashBattle b) => ref Unsafe.As<FlashOrb, byte>(ref b.Orbs[0]));
    private static readonly int CardsOffset = Offset(static (ref FlashBattle b) => ref Unsafe.As<FlashCard, byte>(ref b.Cards[0]));
    private static readonly int DecimalVarsOffset = Offset(static (ref FlashBattle b) => ref Unsafe.As<decimal, byte>(ref b.DecimalVars[0]));
    private static readonly int DecimalVarsEndOffset = DecimalVarsOffset + FlashLimits.DecimalCardVars * Unsafe.SizeOf<decimal>();

    private delegate ref byte FieldSelector(ref FlashBattle battle);

    public static ReadOnlySpan<byte> Bytes(in FlashBattle battle)
        => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in battle, 1));

    public static ulong Hash(in FlashBattle battle)
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
        hash = Mix(hash, all.Slice(EnemiesOffset, battle.EnemyCount * Unsafe.SizeOf<FlashEnemy>()));
        hash = Mix(hash, all.Slice(PowersOffset, battle.PowerCount * Unsafe.SizeOf<FlashPower>()));
        hash = Mix(hash, all.Slice(RelicsOffset, battle.RelicCount * Unsafe.SizeOf<FlashRelic>()));
        hash = Mix(hash, all.Slice(OrbsOffset, battle.OrbCount * Unsafe.SizeOf<FlashOrb>()));
        hash = Mix(hash, all.Slice(CardsOffset, battle.CardCount * Unsafe.SizeOf<FlashCard>()));
        // The HistoryCourse cross-turn slot sits after the card array. Skip it when absent (the
        // canonical bytes are zero) so roots without the relic do not pay 92 extra hashed bytes.
        if ((battle.Flags & FlashBattleFlags.HistoryCourseCardPresent) != 0)
        {
            int historyCourseOffset = CardsOffset + FlashLimits.Cards * Unsafe.SizeOf<FlashCard>();
            hash = Mix(hash, all.Slice(historyCourseOffset, Unsafe.SizeOf<FlashCard>()));
        }
        hash ^= hash >> 33;
        hash *= Prime2;
        hash ^= hash >> 29;
        return hash;
    }

    public static bool Equal(in FlashBattle left, in FlashBattle right) => Bytes(left).SequenceEqual(Bytes(right));

    public static bool Equal(in FlashCard left, in FlashCard right)
        => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in left, 1))
            .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in right, 1)));

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
        FlashBattle probe = default;
        return (int)Unsafe.ByteOffset(ref Unsafe.As<FlashBattle, byte>(ref probe), ref selector(ref probe));
    }
}
