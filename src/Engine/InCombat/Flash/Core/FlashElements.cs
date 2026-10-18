using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>Per-card state flags. Values never encode presentation.</summary>
[Flags]
public enum FlashCardFlags : byte
{
    None = 0,
    ExhaustOnNextPlay = 1 << 0,
    SlyThisTurn = 1 << 1,
    RetainThisTurn = 1 << 2,
    DeckCard = 1 << 3,
    RemovedFromState = 1 << 4,
    CostsX = 1 << 5,
    StarCostsX = 1 << 6,
}

/// <summary>Current keyword set of a card, one bit per native <c>CardKeyword</c>.</summary>
[Flags]
public enum FlashKeywords : byte
{
    None = 0,
    Exhaust = 1 << 0,
    Ethereal = 1 << 1,
    Innate = 1 << 2,
    Unplayable = 1 << 3,
    Retain = 1 << 4,
    Sly = 1 << 5,
    Eternal = 1 << 6,
}

/// <summary>Native <c>LocalCostType</c>.</summary>
public enum FlashCostType : byte
{
    None = 0,
    Absolute = 1,
    Relative = 2,
}

/// <summary>Native <c>LocalCostModifierExpiration</c>; values match the native enum.</summary>
public enum FlashCostExpiration : byte
{
    EndOfCombat = 0,
    EndOfTurn = 2,
    WhenPlayed = 4,
}

/// <summary>One native <c>LocalCostModifier</c> packed into four bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct FlashCostModifier
{
    public short Amount;
    public FlashCostType Type;

    /// <summary>Low three bits: <see cref="FlashCostExpiration"/>; bit 7: reduce-only.</summary>
    public byte Packed;

    public readonly FlashCostExpiration Expiration => (FlashCostExpiration)(Packed & 0x07);

    public readonly bool IsReduceOnly => (Packed & 0x80) != 0;

    public static FlashCostModifier Create(int amount, FlashCostType type, FlashCostExpiration expiration, bool reduceOnly)
        => new()
        {
            Amount = checked((short)amount),
            Type = type,
            Packed = (byte)((byte)expiration | (reduceOnly ? 0x80 : 0)),
        };
}

[InlineArray(FlashLimits.CardEnergyCostModifiers)]
public struct FlashCostModifiers
{
    private FlashCostModifier _first;
}

/// <summary>
/// The complete value of one card in a combat pile. Cards have no instance identity: two
/// cards with equal bytes are interchangeable. Size is asserted to be 32 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FlashCard
{
    private const byte EnergyModifierCountMask = 0x03;
    private const byte TemporaryStarPresentBit = 0x04;
    private const byte TemporaryStarClearsAtTurnEndBit = 0x08;
    private const byte TemporaryStarClearsWhenPlayedBit = 0x10;
    private const int EnchantmentStatusShift = 5;

    public ushort Id;
    public byte Upgrade;
    public FlashCardFlags Flags;
    public FlashKeywords Keywords;
    public sbyte BaseCost;
    public sbyte BaseStarCost;
    public byte ReplayCount;
    public FlashCostModifiers EnergyModifiers;

    /// <summary>
    /// Bits 0-1: energy modifier count; bit 2: temporary star cost present; bit 3: it clears at
    /// turn end; bit 4: it clears when played; bits 5-7: native enchantment status.
    /// </summary>
    public byte CostState;

    public sbyte TemporaryStarCost;
    public byte EnchantmentId;
    public byte AfflictionId;
    public short EnchantmentAmount;
    public short EnchantmentPrivate;
    public short AfflictionAmount;

    /// <summary>Semantic dynamic-variable deltas relative to the rule table's base values.</summary>
    public short Var0;
    public short Var1;

    /// <summary>Card-private counter (Claw, Rampage, GeneticAlgorithm and similar).</summary>
    public short Private;

    public int EnergyModifierCount
    {
        readonly get => CostState & EnergyModifierCountMask;
        set
        {
            if ((uint)value > FlashLimits.CardEnergyCostModifiers)
                throw new FlashCapacityException("Card.EnergyModifiers", FlashLimits.CardEnergyCostModifiers);
            CostState = (byte)((CostState & ~EnergyModifierCountMask) | value);
        }
    }

    public readonly bool HasTemporaryStarCost => (CostState & TemporaryStarPresentBit) != 0;

    public readonly bool TemporaryStarCostClearsAtTurnEnd => (CostState & TemporaryStarClearsAtTurnEndBit) != 0;

    public readonly bool TemporaryStarCostClearsWhenPlayed => (CostState & TemporaryStarClearsWhenPlayedBit) != 0;

    public void SetTemporaryStarCost(int cost, bool clearsAtTurnEnd, bool clearsWhenPlayed)
    {
        TemporaryStarCost = checked((sbyte)cost);
        CostState = (byte)((CostState & ~(TemporaryStarPresentBit | TemporaryStarClearsAtTurnEndBit | TemporaryStarClearsWhenPlayedBit))
            | TemporaryStarPresentBit
            | (clearsAtTurnEnd ? TemporaryStarClearsAtTurnEndBit : 0)
            | (clearsWhenPlayed ? TemporaryStarClearsWhenPlayedBit : 0));
    }

    public void ClearTemporaryStarCost()
    {
        TemporaryStarCost = 0;
        CostState = (byte)(CostState & ~(TemporaryStarPresentBit | TemporaryStarClearsAtTurnEndBit | TemporaryStarClearsWhenPlayedBit));
    }

    public byte EnchantmentStatus
    {
        readonly get => (byte)(CostState >> EnchantmentStatusShift);
        set
        {
            if (value > 7)
                throw new FlashUnsupportedException($"附魔状态值 {value} 超出 3 位编码。");
            CostState = (byte)((CostState & 0x1F) | (value << EnchantmentStatusShift));
        }
    }

    public readonly bool HasKeyword(FlashKeywords keyword) => (Keywords & keyword) != 0;
}

/// <summary>Creature vitals shared by the player, Osty and enemies.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct FlashCreature
{
    public int CurrentHp;
    public int MaxHp;
    public int Block;
    public FlashCreatureFlags Flags;
    private byte _padding0;
    private short _padding1;

    public readonly bool IsAlive => CurrentHp > 0;

    public readonly bool IsDead => CurrentHp <= 0;
}

[Flags]
public enum FlashCreatureFlags : byte
{
    None = 0,
    Present = 1 << 0,

    /// <summary>Took unblocked damage in the current side turn (<c>SimulatedCombatState._unblockedDamageThisTurn</c>).</summary>
    LostHpThisTurn = 1 << 1,
}

/// <summary>Native <c>SimulatedCombatState</c> death phase of an enemy.</summary>
public enum FlashDeathPhase : byte
{
    None = 0,
    Reviving = 1,
    PermanentlyDead = 2,
}

[Flags]
public enum FlashEnemyFlags : byte
{
    None = 0,
    InCombat = 1 << 0,
    NeedsInitialRoll = 1 << 1,
    SkipNextMove = 1 << 2,
    Escaped = 1 << 3,
    PerformedMoveThisRound = 1 << 4,

    /// <summary>Its death was handled by the deferred enemy-death pass (solver <c>processedEnemyDeaths</c>).</summary>
    DeathProcessed = 1 << 5,

    /// <summary>
    /// A forced must-perform-once node replaced the current move after this enemy already performed this
    /// round (<c>SimulatedCombatState.MonsterAi.cs:122-126</c>: <c>performedMove</c> is the old move, so
    /// the machine must not advance). Consumed and cleared by
    /// <see cref="FlashKernel.PrepareMonsterMovesForNextRound"/>; zero at every stable boundary.
    /// </summary>
    ForcedMustPerformAfterMove = 1 << 6,

    /// <summary>
    /// <see cref="FlashKernel.ExecuteMonsterMove"/> is running. A forced must-perform node set while this
    /// is on replaced the move currently being performed; the external performed flag is only set after
    /// the action returns (<c>CombatBeamSolver.Expansion.Replay.cs:1164</c>). Zero at every stable boundary.
    /// </summary>
    ActingThisRound = 1 << 7,
}

[InlineArray(FlashLimits.EnemyRecentMoves)]
public struct FlashRecentMoves
{
    private byte _first;
}

[InlineArray(FlashLimits.EnemyAiRegisters)]
public struct FlashAiRegisters
{
    private int _first;
}

/// <summary>
/// One known enemy. Enemies are never removed from the array; <see cref="FlashEnemyFlags.InCombat"/>
/// tracks roster membership so power owners stay stable.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FlashEnemy
{
    public FlashCreature Creature;
    public ushort MonsterId;
    public byte Slot;
    public FlashDeathPhase DeathPhase;
    public uint CombatId;

    /// <summary>Current move node in the compiled state machine of this enemy.</summary>
    public byte Move;

    public FlashEnemyFlags Flags;

    /// <summary>Total moves logged, saturating at 255.</summary>
    public byte LoggedMoves;

    /// <summary>
    /// Dynamic follow-up of the synthetic STUNNED node (the <c>FollowUpStateId</c> of the native
    /// <c>MoveState("STUNNED")</c> created by <c>SimulatedCombatState.ForceStunnedMove</c>), or 255 when
    /// <see cref="Move"/> is not the STUNNED node. Reusing this slot keeps the layout unchanged.
    /// </summary>
    public byte ForcedMove;

    /// <summary>Index into <see cref="FlashRules"/>'s monster table (<c>FlashRules.MonsterRule</c>).</summary>
    public byte Rules;

    private byte _padding0;
    private short _padding1;

    /// <summary>Bit i set when move node i has ever been logged (native "use only once" rule).</summary>
    public ulong EverUsedMoves;

    /// <summary>Most recent logged move nodes, newest last; see <see cref="LoggedMoves"/>.</summary>
    public FlashRecentMoves RecentMoves;

    /// <summary>Monster-private integers, numbered by each monster definition.</summary>
    public FlashAiRegisters Ai;
}

[Flags]
public enum FlashPowerFlags : byte
{
    None = 0,
    SkipNextDurationTick = 1 << 0,
    WasJustAppliedByEnemy = 1 << 1,

    /// <summary>Solver-side skip set by <c>ApplyFromMonster</c> for powers without the native duration skip.</summary>
    SkipNextTickFromMonster = 1 << 2,
}

/// <summary>
/// One power instance. The power table is kept grouped by <see cref="Owner"/> in ascending
/// owner order; within an owner, entries are in acquisition order.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FlashPower
{
    public ushort Id;
    public byte Owner;
    public byte Applier;
    public byte Target;
    public FlashPowerFlags Flags;
    public short Aux2;
    public int Amount;
    public int AmountOnTurnStart;
    public int Aux0;
    public int Aux1;
}

[Flags]
public enum FlashRelicFlags : byte
{
    None = 0,
    Melted = 1 << 0,
    UsedThisCombat = 1 << 1,
}

[StructLayout(LayoutKind.Sequential)]
public struct FlashRelic
{
    public ushort Id;
    public FlashRelicFlags Flags;
    private byte _padding;
    public int Counter;
    public int Aux;
}

[StructLayout(LayoutKind.Sequential)]
public struct FlashOrb
{
    public byte Id;
    private byte _padding0;
    private short _padding1;
    public int Aux;
}

[InlineArray(FlashLimits.Cards)]
public struct FlashCardArray
{
    private FlashCard _first;
}

[InlineArray(FlashLimits.Enemies)]
public struct FlashEnemyArray
{
    private FlashEnemy _first;
}

[InlineArray(FlashLimits.Enemies)]
public struct FlashRoster
{
    private byte _first;
}

[InlineArray(FlashLimits.Powers)]
public struct FlashPowerArray
{
    private FlashPower _first;
}

[InlineArray(FlashLimits.Relics)]
public struct FlashRelicArray
{
    private FlashRelic _first;
}

[InlineArray(FlashLimits.PotionSlots)]
public struct FlashPotionSlots
{
    private ushort _first;
}

[InlineArray(FlashLimits.Orbs)]
public struct FlashOrbArray
{
    private FlashOrb _first;
}

[InlineArray(FlashLimits.Counters)]
public struct FlashCounterArray
{
    private int _first;
}
