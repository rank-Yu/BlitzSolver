using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sts2Solver.Ai.Combat;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.KernelChecks;

/// <summary>Packed card-storage contracts: prefix identity, arena reservation/compaction and &gt;96 cards.</summary>
internal static class PackedCardsChecks
{
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidDataException("packed-cards: " + message); }

    internal static void Check(Rules rules)
    {
        Battle state = default;
        state.Round = 7;
        state.HandCount = 3;
        state.Cards[0] = new Card { Id = 0, Private = 1 };
        state.Cards[1] = new Card { Id = 0, Private = 2 };
        state.Cards[2] = new Card { Id = 0, Private = 3 };
        state.Flags = BattleFlags.HistoryCourseCardPresent;
        state.HistoryCourseCard = new Card { Id = 0, Private = 99 };

        // A packed copy carries the defined prefix; the suffix is undefined and ignored.
        Battle copy = default;
        Battle.CopyPacked(in state, ref copy);
        int packed = Battle.PackedSize(in state);
        Span<byte> suffix = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref copy, 1))[packed..];
        suffix.Fill(0xA5);
        Assert(Fingerprint.Hash(in copy) == Fingerprint.Hash(in state), "hash ignores suffix");
        Assert(LegacyHash(in state) == Fingerprint.Hash(in state), "hash matches the pre-packed value");
        Assert(Fingerprint.Equal(in copy, in state), "equality ignores suffix");
        Assert(BattleCodec.Encode(copy).AsSpan().SequenceEqual(BattleCodec.Encode(state)),
            "encoding ignores suffix");
        Assert(Battle.PackedSize(in copy) == packed, "packed size follows CardCount");

        Battle decoded = BattleCodec.Decode(BattleCodec.Encode(state));
        Assert(Fingerprint.Equal(in decoded, in state), "codec round trip");
        Assert(Battle.PackedSize(in decoded) == packed, "codec round trip size");

        // Arena: full 200-card records cross the 512 KiB block boundary, compact in place, and
        // reclaim the released blocks.
        SearchStorage storage = new();
        storage.BeginSolve(0);
        PackedStateArena arena = new(64, storage);
        Battle[] originals = new Battle[40];
        for (int index = 0; index < originals.Length; index++)
        {
            Battle child = default;
            child.Round = index + 1;
            for (int card = 0; card < 200; card++)
                Piles.Append(ref child, Pile.Draw, new Card { Id = 0, Private = checked((short)card) });
            originals[index] = child;
            arena.Reserve();
            Battle.CopyPacked(in child, ref arena.ReservedState);
            arena.CommitReserved();
        }
        Assert(arena.Count == originals.Length, "arena count");
        Assert(arena.UsedBytes > PackedStateArena.PayloadBytes, "records cross a block boundary");
        for (int index = 0; index < originals.Length; index++)
            Assert(Fingerprint.Equal(in arena[index], in originals[index]), $"arena state {index}");

        long beforeCompact = arena.UsedBytes;
        int write = 0;
        for (int read = 0; read < arena.Count; read += 2)
            arena.Move(read, write++);
        arena.Truncate(write);
        Assert(arena.UsedBytes <= beforeCompact, "compaction reclaims bytes");
        for (int index = 0; index < write; index++)
            Assert(Fingerprint.Equal(in arena[index], in originals[index * 2]),
                $"compacted state {index}");
        Assert(Fingerprint.Hash(in arena[0]) == Fingerprint.Hash(in originals[0]),
            "compaction preserves the packed hash");

        arena.Clear();
        Assert(arena.Count == 0 && arena.UsedBytes == 0, "clear keeps reusable blocks");

        CheckCompactionInvariant();
        CheckUnorderedCompaction();

        // A 200-card state is a normal Battle: it can be stepped and inserted (deduplicated by the
        // pool's hash/equality contract).
        Battle large = default;
        large.Player = new Creature { CurrentHp = 50, MaxHp = 50, Flags = CreatureFlags.Present };
        large.MaxHandSize = 10;
        large.Energy = 3;
        for (int card = 0; card < 200; card++)
            Piles.Append(ref large, Pile.Draw, new Card { Id = 0, Private = checked((short)card) });
        Assert(large.CardCount == 200, "200-card state");
        StepResult result = Kernel.Step(ref large, PlayerAction.EndTurn(), rules,
            out ChoiceRequest request, out bool historyRisk);
        Assert(result is StepResult.Completed or StepResult.NeedsChoice,
            $"200-card step result {result}");
        _ = request;
        _ = historyRisk;
        Assert(large.CardCount <= Limits.Cards, "step stays within capacity");

        arena.Reserve();
        Battle.CopyPacked(in large, ref arena.ReservedState);
        arena.CommitReserved();
        arena.Reserve();
        Battle.CopyPacked(in large, ref arena.ReservedState);
        ulong hash = Fingerprint.Hash(in arena[arena.Count - 1]);
        Assert(hash == Fingerprint.Hash(in large), "dedupe hash for a 200-card state");
        Assert(Fingerprint.Equal(in arena[arena.Count - 1], in large), "dedupe equality for a 200-card state");
        Console.WriteLine($"PACKED_CARDS states=200 arenaBytes={PackedStateArena.BlockBytes} pass");
    }

    private static void CheckCompactionInvariant()
    {
        SearchStorage storage = new();
        storage.BeginSolve(0);
        PackedStateArena arena = new(64, storage);
        int[] counts = [255, 240, 220, 200, 180, 160, 140, 120, 100, 80, 60, 40, 20, 5, 255, 200, 150, 100, 50, 10];
        Battle[] originals = new Battle[counts.Length];
        for (int index = 0; index < counts.Length; index++)
        {
            Battle state = BuildWithCards(counts[index], index + 1);
            originals[index] = state;
            arena.Reserve();
            Battle.CopyPacked(in state, ref arena.ReservedState);
            arena.CommitReserved();
        }
        long before = arena.UsedBytes;
        arena.Compact();
        Assert(arena.Count == counts.Length, "compaction keeps count");
        Assert(arena.UsedBytes <= before, "compaction does not grow the tail");
        for (int index = 0; index < counts.Length; index++)
        {
            long offset = arena.OffsetOf(index);
            int inBlock = PackedStateArena.InBlockOffset(offset);
            Assert(inBlock + Battle.ExpectedSize <= PackedStateArena.BlockBytes,
                $"compacted record {index} leaves a full Battle tail");
            Assert(inBlock <= PackedStateArena.PayloadBytes,
                $"compacted record {index} starts in the payload");
            Battle compacted = arena[index];
            Assert(Fingerprint.Bytes(in compacted).SequenceEqual(Fingerprint.Bytes(in originals[index])),
                $"compacted record {index} bytes unchanged");
        }
    }

    private static void CheckUnorderedCompaction()
    {
        SearchStorage storage = new();
        storage.BeginSolve(0);
        PackedStateArena arena = new(8, storage);
        foreach (int cards in new[] { 5, 20, 200 })
        {
            Battle state = BuildWithCards(cards, cards);
            arena.Reserve();
            Battle.CopyPacked(in state, ref arena.ReservedState);
            arena.CommitReserved();
        }
        arena.Move(2, 0);
        bool threw = false;
        try
        {
            arena.Compact();
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert(threw, "unordered compaction is rejected");
    }

    private static Battle BuildWithCards(int count, int round)
    {
        Battle state = default;
        state.Round = round;
        for (int card = 0; card < count; card++)
            Piles.Append(ref state, Pile.Draw, new Card { Id = 0, Private = checked((short)card) });
        return state;
    }

    /// <summary>The pre-v3 fingerprint: same segments over the canonical 12,696-byte image.</summary>
    private static ulong LegacyHash(in Battle battle)
    {
        const int legacyHeader = 3768;
        const int legacyCards = 3768;
        const int legacyHistory = 12600;
        const int legacySize = 12696;
        Span<byte> image = stackalloc byte[legacySize];
        ReadOnlySpan<byte> source = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(in battle, 1));
        source[..legacyHeader].CopyTo(image);
        source.Slice(Battle.HeaderSize, battle.CardCount * 92).CopyTo(image[legacyCards..]);
        source.Slice((int)Marshal.OffsetOf<Battle>(nameof(Battle.HistoryCourseCard)), 92)
            .CopyTo(image[legacyHistory..]);

        const int decimalVarsOffset = 328;
        const int decimalVarsEndOffset = 392;
        const int enemiesOffset = 1000;
        const int powersOffset = 1768;
        const int relicsOffset = 3304;
        const int orbsOffset = 3688;
        ulong hash = Prime3;
        hash = Mix(hash, image[..decimalVarsOffset]);
        int decimalBytes = battle.DecimalVarCount * Unsafe.SizeOf<decimal>();
        if (decimalBytes > 0)
            hash = Mix(hash, image.Slice(decimalVarsOffset, decimalBytes));
        hash = Mix(hash, image[decimalVarsEndOffset..enemiesOffset]);
        hash = Mix(hash, image.Slice(enemiesOffset, battle.EnemyCount * Unsafe.SizeOf<Enemy>()));
        hash = Mix(hash, image.Slice(powersOffset, battle.PowerCount * Unsafe.SizeOf<Power>()));
        hash = Mix(hash, image.Slice(relicsOffset, battle.RelicCount * Unsafe.SizeOf<Relic>()));
        hash = Mix(hash, image.Slice(orbsOffset, battle.OrbCount * Unsafe.SizeOf<Orb>()));
        hash = Mix(hash, image.Slice(legacyCards, battle.CardCount * Unsafe.SizeOf<Card>()));
        if ((battle.Flags & BattleFlags.HistoryCourseCardPresent) != 0)
            hash = Mix(hash, image.Slice(legacyHistory, Unsafe.SizeOf<Card>()));
        hash ^= hash >> 33;
        hash *= Prime2;
        hash ^= hash >> 29;
        return hash;
    }

    private const ulong Prime1 = 0x9E3779B185EBCA87UL;
    private const ulong Prime2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong Prime3 = 0x165667B19E3779F9UL;

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

    private static ulong Round(ulong accumulator, ulong word)
        => BitOperations.RotateLeft(accumulator + word * Prime2, 31) * Prime1;
}
