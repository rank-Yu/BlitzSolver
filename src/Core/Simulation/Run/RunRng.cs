using System.Buffers.Binary;
using System.Numerics;
using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Deterministic 64-bit hash used for native <c>StringHelper.GetDeterministicHashCode</c>
/// (<c>XxHash64</c> with seed 0) and for saved-property names.
/// </summary>
public static class RunHash
{
    private const ulong Prime1 = 11400714785074694791UL;
    private const ulong Prime2 = 14029467366897019727UL;
    private const ulong Prime3 = 1609587929392839161UL;
    private const ulong Prime4 = 9650029242287828579UL;
    private const ulong Prime5 = 2870177450012600261UL;

    /// <summary>XXH64 of <paramref name="data"/> with the given seed.</summary>
    public static ulong XxHash64(ReadOnlySpan<byte> data, ulong seed = 0)
    {
        int offset = 0;
        ulong hash;
        if (data.Length >= 32)
        {
            ulong v1 = seed + Prime1 + Prime2;
            ulong v2 = seed + Prime2;
            ulong v3 = seed;
            ulong v4 = seed - Prime1;
            int limit = data.Length - 32;
            do
            {
                v1 = Round(v1, BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]));
                v2 = Round(v2, BinaryPrimitives.ReadUInt64LittleEndian(data[(offset + 8)..]));
                v3 = Round(v3, BinaryPrimitives.ReadUInt64LittleEndian(data[(offset + 16)..]));
                v4 = Round(v4, BinaryPrimitives.ReadUInt64LittleEndian(data[(offset + 24)..]));
                offset += 32;
            }
            while (offset <= limit);
            hash = BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7)
                + BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);
            hash = MergeRound(hash, v1);
            hash = MergeRound(hash, v2);
            hash = MergeRound(hash, v3);
            hash = MergeRound(hash, v4);
        }
        else
        {
            hash = seed + Prime5;
        }

        hash += (ulong)data.Length;
        while (offset + 8 <= data.Length)
        {
            ulong k1 = Round(0, BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]));
            hash ^= k1;
            hash = BitOperations.RotateLeft(hash, 27) * Prime1 + Prime4;
            offset += 8;
        }
        if (offset + 4 <= data.Length)
        {
            hash ^= (ulong)BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]) * Prime1;
            hash = BitOperations.RotateLeft(hash, 23) * Prime2 + Prime3;
            offset += 4;
        }
        while (offset < data.Length)
        {
            hash ^= data[offset] * Prime5;
            hash = BitOperations.RotateLeft(hash, 11) * Prime1;
            offset++;
        }

        hash ^= hash >> 33;
        hash *= Prime2;
        hash ^= hash >> 29;
        hash *= Prime3;
        hash ^= hash >> 32;
        return hash;
    }

    private static ulong Round(ulong accumulator, ulong input)
    {
        accumulator += input * Prime2;
        accumulator = BitOperations.RotateLeft(accumulator, 31);
        return accumulator * Prime1;
    }

    private static ulong MergeRound(ulong accumulator, ulong value)
    {
        accumulator ^= Round(0, value);
        return accumulator * Prime1 + Prime4;
    }
}

/// <summary>
/// Native RNG derivation. Every native run/player stream is <c>new Rng(seed, name)</c>, i.e.
/// <c>MegaRandom(seed + XxHash64(UTF8(name)))</c>; events and encounters add the hash without a
/// name. These functions are the only place the names live.
/// </summary>
public static class RunRng
{
    private static readonly string[] StreamNames =
    [
        "up_front",
        "unknown_map_point",
        "treasure_room_relics",
        "rewards",
        "shops",
        "transformations",
        "shuffle",
        "combat_card_generation",
        "combat_potion_generation",
        "combat_card_selection",
        "combat_energy_costs",
        "combat_targets",
        "combat_orbs",
        "monster_ai",
        "niche",
    ];

    public static string StreamName(RunRngStream stream) => StreamNames[(int)stream];

    /// <summary>Creates the native <c>MegaRandom</c> state for <paramref name="seed"/>.</summary>
    public static RngState Create(ulong seed)
    {
        ulong state = seed;
        ulong s0 = Splitmix64(ref state);
        ulong s1 = Splitmix64(ref state);
        ulong s2 = Splitmix64(ref state);
        ulong s3 = Splitmix64(ref state);
        return RngState.FromSerializable(0, s0, s1, s2, s3);
    }

    /// <summary>Native <c>new Rng(seed, name)</c> initial state.</summary>
    public static RngState Named(ulong seed, string name)
        => Create(seed + RunHash.XxHash64(System.Text.Encoding.UTF8.GetBytes(name)));

    /// <summary>Run or player stream initial state.</summary>
    public static RngState Stream(ulong seed, RunRngStream stream)
        => Named(seed, StreamNames[(int)stream]);

    /// <summary>Native event RNG: <c>seed + slot(0 for shared) + hash(event id)</c>.</summary>
    public static RngState Event(ulong seed, int playerSlot, ushort eventId)
        => Create(seed + (ulong)playerSlot + RunHash.XxHash64(
            System.Text.Encoding.UTF8.GetBytes(RunCatalog.EventName(eventId))));

    /// <summary>Native encounter RNG: <c>seed + TotalFloor + hash(encounter id)</c>.</summary>
    public static RngState Encounter(ulong seed, int totalFloor, ushort encounterId)
        => Create(seed + (ulong)totalFloor + RunHash.XxHash64(
            System.Text.Encoding.UTF8.GetBytes(RunCatalog.EncounterName(encounterId))));

    /// <summary>Native act map RNG name: <c>act_N_map</c> with the one-based act number.</summary>
    public static RngState ActMap(ulong seed, int actIndex)
        => Named(seed, $"act_{actIndex + 1}_map");

    public static ref RngState StreamRef(ref RunRngState rng, RunRngStream stream)
        => ref rng.Streams[(int)stream];

    public static ref RngState MapRef(ref RunRngState rng, int actIndex)
        => ref rng.Map[actIndex];

    public static ref RunEventRng EventRef(ref RunRngState rng, int index)
        => ref rng.Events[index];

    /// <summary>
    /// All 15 initial stream states for a new run with the given seeds. This is the canonical
    /// constructor used by import and by offline run creation.
    /// </summary>
    public static RunRngState NewRun(ulong runSeed, ulong playerSeed)
    {
        RunRngState rng = default;
        rng.Seed = runSeed;
        rng.PlayerSeed = playerSeed;
        // The native RunRngSet owns all run streams (including the nine combat streams) and uses
        // the run seed; the PlayerRngSet owns only Rewards/Shops/Transformations (indices 3..5)
        // and uses hash(seed string) + slot.
        for (int index = 0; index < RunLimits.RngStreams; index++)
        {
            bool playerStream = index is (int)RunRngStream.Rewards
                or (int)RunRngStream.Shops
                or (int)RunRngStream.Transformations;
            rng.Streams[index] = Stream(playerStream ? playerSeed : runSeed, (RunRngStream)index);
        }
        for (int act = 0; act < RunLimits.ActMapRngs; act++)
            rng.Map[act] = ActMap(runSeed, act);
        return rng;
    }

    private static ulong Splitmix64(ref ulong state)
    {
        ulong value = state += 11400714819323198485UL;
        value = (value ^ (value >> 30)) * 13787848793156543929UL;
        value = (value ^ (value >> 27)) * 10723151780598845931UL;
        return value ^ (value >> 31);
    }
}
