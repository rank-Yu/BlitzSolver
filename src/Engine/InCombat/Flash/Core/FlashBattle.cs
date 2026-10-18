using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CombatSolver.Engine.InCombat.Flash;

public enum FlashSide : byte
{
    None = 0,
    Player = 1,
    Enemy = 2,
}

/// <summary>Native <c>PlayerTurnPhase</c>; values match the native enum.</summary>
public enum FlashPlayerPhase : byte
{
    None = 0,
    Start = 1,
    AutoPrePlay = 2,
    Play = 3,
    AutoPostPlay = 4,
    End = 5,
}

public enum FlashOutcome : byte
{
    None = 0,
    Victory = 1,
    Defeat = 2,
}

[Flags]
public enum FlashBattleFlags : byte
{
    None = 0,
    PlayerHooksActive = 1 << 0,
    PlayerTurnEndRequested = 1 << 1,
    OstyPresent = 1 << 2,

    /// <summary>Native <c>CombatTurnState.PendingLoss</c> (<c>IsAboutToLose</c>).</summary>
    PendingLoss = 1 << 3,

    /// <summary>
    /// Native <c>CombatPredictionSimulator.HasRisk</c>: the route recorded an unresolved player
    /// choice (Discovery/Splash generated options). Monotonic for the rest of the combat.
    /// </summary>
    HasRisk = 1 << 4,
}

/// <summary>Combat pile segments, in their storage order inside <see cref="FlashBattle.Cards"/>.</summary>
public enum FlashPile : byte
{
    Hand = 0,
    Draw = 1,
    Discard = 2,
    Exhaust = 3,
    Play = 4,
}

/// <summary>
/// Indexes into <see cref="FlashBattle.Counters"/>. Only counters that affect future resolution
/// belong here; presentation-only history stays out of the state.
/// </summary>
public enum FlashCounter : byte
{
    // Player per-turn counters (SimulatedCombatState maps keyed by the player creature). They reset
    // in BeginSideTurn at the player turn start and persist through the enemy turn.
    CardsPlayedThisTurn,
    ManualCardsPlayedThisTurn,
    CardPlayStartsThisTurn,
    AttackPlayStartsThisTurn,
    AttackSkillStartsThisTurn,
    ZeroCostAttackStartsThisTurn,
    CardPlaySeriesStartedThisTurn,
    AttacksPlayedThisTurn,
    ShivsPlayedThisTurn,
    SkillCardsPlayedThisTurn,
    BlockCardsPlayedThisTurn,
    CardsExhaustedThisTurn,
    CardsDiscardedThisTurn,
    PlayerAttacksThisTurn,
    NonHandDrawsThisTurn,
    StatusCardsDrawnThisTurn,
    EnergySpentThisTurn,

    // Combat-long player counters.
    CumulativeHpLost,
    RecoveredHp,

    /// <summary>
    /// Player unblocked-damage events this combat (<c>History.Entries.OfType&lt;DamageReceivedEntry&gt;</c>
    /// with <c>Receiver == owner &amp;&amp; Result.UnblockedDamage &gt; 0</c>): the TearAsunder multiplier.
    /// Imported from the existing engine's pre-prediction history and incremented during the search.
    /// </summary>
    PlayerUnblockedDamageEvents,

    // Route counters read by the score port (FlashScorer). Appended only, so every existing value
    // keeps its index.
    LongTermResourceValue,
    AngerCopiesGenerated,
    DeathSaveRelicHpRestored,
    DeathSavePotionHpRestored,
    DeathSaveUseCount,
    AutomaticPotionUseCount,
}

/// <summary>
/// All branch-mutable state of one combat at a stable boundary. Unmanaged and reference-free:
/// a copy is a deep fork, and the byte image is the state identity. See
/// <c>docs/flash-search/state-layout.md</c>. Field order avoids implicit padding; the total size
/// is asserted by <c>FlashKernelChecks</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FlashBattle
{
    public const int ExpectedSize = 11808;

    public int Round;
    public int PlayerTurn;
    public int ShuffleEventCount;
    public int ShufflesCrossed;
    public uint NextCombatId;
    public int Energy;
    public int Stars;
    public int Gold;

    public FlashSide Side;
    public FlashPlayerPhase Phase;
    public FlashOutcome Outcome;
    public FlashBattleFlags Flags;

    public byte HandCount;
    public byte DrawCount;
    public byte DiscardCount;
    public byte ExhaustCount;
    public byte PlayCount;
    public byte MaxHandSize;

    /// <summary>Known enemies, including dead and escaped ones.</summary>
    public byte EnemyCount;

    /// <summary>Enemies currently in the roster, in native roster order.</summary>
    public byte RosterCount;

    public byte PowerCount;
    public byte RelicCount;
    public byte OrbCount;
    public byte OrbCapacity;
    public byte PotionSlotCount;
    private byte _padding0;
    private byte _padding1;
    private byte _padding2;

    /// <summary>Indexes into <see cref="Enemies"/>, in native roster order.</summary>
    public FlashRoster Roster;

    public FlashPotionSlots Potions;
    private short _padding3;

    public FlashCreature Player;
    public FlashCreature Osty;
    public FlashCounterArray Counters;
    public FlashRngStreams Rng;
    public FlashEnemyArray Enemies;
    public FlashPowerArray Powers;
    public FlashRelicArray Relics;
    public FlashOrbArray Orbs;
    public FlashCardArray Cards;

    public readonly int CardCount => HandCount + DrawCount + DiscardCount + ExhaustCount + PlayCount;

    public readonly bool IsOver => Outcome != FlashOutcome.None;

    public readonly int Counter(FlashCounter counter) => Counters[(int)counter];

    public void AddCounter(FlashCounter counter, int amount) => Counters[(int)counter] += amount;

    public void SetCounter(FlashCounter counter, int value) => Counters[(int)counter] = value;

    /// <summary>Creature behind an owner index: player, Osty or a known enemy.</summary>
    [UnscopedRef]
    public ref FlashCreature Creature(byte owner)
    {
        if (owner == FlashLimits.PlayerOwner)
            return ref Player;
        if (owner == FlashLimits.OstyOwner)
            return ref Osty;
        int enemy = owner - FlashLimits.FirstEnemyOwner;
        if ((uint)enemy >= EnemyCount)
            throw new ArgumentOutOfRangeException(nameof(owner), owner, "未知的生物下标。");
        return ref Enemies[enemy].Creature;
    }

    [UnscopedRef]
    public ref FlashEnemy Enemy(byte owner)
    {
        int enemy = owner - FlashLimits.FirstEnemyOwner;
        if ((uint)enemy >= EnemyCount)
            throw new ArgumentOutOfRangeException(nameof(owner), owner, "未知的敌人下标。");
        return ref Enemies[enemy];
    }

    [UnscopedRef]
    public ref FlashRngState RngStream(FlashRngStream stream) => ref Rng[(int)stream];
}

/// <summary>Pile segment operations over <see cref="FlashBattle.Cards"/>.</summary>
public static class FlashPiles
{
    public static int Count(in FlashBattle battle, FlashPile pile) => pile switch
    {
        FlashPile.Hand => battle.HandCount,
        FlashPile.Draw => battle.DrawCount,
        FlashPile.Discard => battle.DiscardCount,
        FlashPile.Exhaust => battle.ExhaustCount,
        FlashPile.Play => battle.PlayCount,
        _ => throw new ArgumentOutOfRangeException(nameof(pile), pile, null),
    };

    public static int Start(in FlashBattle battle, FlashPile pile) => pile switch
    {
        FlashPile.Hand => 0,
        FlashPile.Draw => battle.HandCount,
        FlashPile.Discard => battle.HandCount + battle.DrawCount,
        FlashPile.Exhaust => battle.HandCount + battle.DrawCount + battle.DiscardCount,
        FlashPile.Play => battle.HandCount + battle.DrawCount + battle.DiscardCount + battle.ExhaustCount,
        _ => throw new ArgumentOutOfRangeException(nameof(pile), pile, null),
    };

    public static Span<FlashCard> Cards(ref FlashBattle battle, FlashPile pile)
        => MemoryMarshal.CreateSpan(ref battle.Cards[0], FlashLimits.Cards).Slice(Start(battle, pile), Count(battle, pile));

    public static ReadOnlySpan<FlashCard> ReadCards(in FlashBattle battle, FlashPile pile)
        => MemoryMarshal.CreateReadOnlySpan(in battle.Cards[0], FlashLimits.Cards).Slice(Start(battle, pile), Count(battle, pile));

    public static ref FlashCard At(ref FlashBattle battle, FlashPile pile, int index)
    {
        if ((uint)index >= (uint)Count(battle, pile))
            throw new ArgumentOutOfRangeException(nameof(index), index, $"{pile} 牌堆没有这个位置。");
        return ref battle.Cards[Start(battle, pile) + index];
    }

    /// <summary>Removes and returns the card at <paramref name="index"/>; later cards shift left.</summary>
    public static FlashCard RemoveAt(ref FlashBattle battle, FlashPile pile, int index)
    {
        int count = Count(battle, pile);
        if ((uint)index >= (uint)count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"{pile} 牌堆没有这个位置。");
        int total = battle.CardCount;
        int position = Start(battle, pile) + index;
        Span<FlashCard> all = MemoryMarshal.CreateSpan(ref battle.Cards[0], FlashLimits.Cards);
        FlashCard card = all[position];
        all.Slice(position + 1, total - position - 1).CopyTo(all.Slice(position));
        all[total - 1] = default;
        SetCount(ref battle, pile, count - 1);
        return card;
    }

    /// <summary>Inserts <paramref name="card"/> at <paramref name="index"/>; later cards shift right.</summary>
    public static void Insert(ref FlashBattle battle, FlashPile pile, int index, in FlashCard card)
    {
        int count = Count(battle, pile);
        if ((uint)index > (uint)count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"{pile} 牌堆插入位置越界。");
        int total = battle.CardCount;
        if (total >= FlashLimits.Cards)
            throw new FlashCapacityException("Cards", FlashLimits.Cards);
        int position = Start(battle, pile) + index;
        Span<FlashCard> all = MemoryMarshal.CreateSpan(ref battle.Cards[0], FlashLimits.Cards);
        all.Slice(position, total - position).CopyTo(all.Slice(position + 1));
        all[position] = card;
        SetCount(ref battle, pile, count + 1);
    }

    public static void Append(ref FlashBattle battle, FlashPile pile, in FlashCard card)
        => Insert(ref battle, pile, Count(battle, pile), card);

    /// <summary>
    /// Moves the top draw card to the end of the hand. The draw segment directly follows the hand,
    /// so this only moves the boundary.
    /// </summary>
    public static void DrawTopToHand(ref FlashBattle battle)
    {
        if (battle.DrawCount == 0)
            throw new InvalidOperationException("抽牌堆为空。");
        battle.HandCount++;
        battle.DrawCount--;
    }

    private static void SetCount(ref FlashBattle battle, FlashPile pile, int value)
    {
        byte count = checked((byte)value);
        switch (pile)
        {
            case FlashPile.Hand: battle.HandCount = count; break;
            case FlashPile.Draw: battle.DrawCount = count; break;
            case FlashPile.Discard: battle.DiscardCount = count; break;
            case FlashPile.Exhaust: battle.ExhaustCount = count; break;
            case FlashPile.Play: battle.PlayCount = count; break;
            default: throw new ArgumentOutOfRangeException(nameof(pile), pile, null);
        }
    }
}
