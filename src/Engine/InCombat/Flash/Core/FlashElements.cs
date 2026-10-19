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

    /// <summary>
    /// The existing engine's <c>PredictedCard.HasCheckedPowerAfflictionEntry</c>: the one-time
    /// <c>VitalSparkPower</c>/<c>SmoggyPower</c>/<c>GalvanicPower</c> card-entry check has run. Root
    /// cards are marked at import (they were present at the snapshot); cards created after the root
    /// start unmarked, so the next normalization afflicts them once.
    /// </summary>
    PowerAfflictionChecked = 1 << 7,
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

    /// <summary>
    /// Not a native keyword: this card entered combat after the root snapshot. The existing engine
    /// distinguishes such cards from root cards with <c>SimulatedCombatState._liveCardsAtSnapshot</c>;
    /// the one-time Vital Spark/Smog/Galvanic entry check applies only to these cards.
    /// </summary>
    CreatedAfterRoot = 1 << 7,
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

    /// <summary><c>LocalCostModifierExpiration.EndOfTurn | WhenPlayed</c> (the native "this turn or until played").</summary>
    EndOfTurnOrWhenPlayed = EndOfTurn | WhenPlayed,
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
/// cards with equal bytes are interchangeable. Size is asserted to be 90 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FlashCard
{
    private const byte TemporaryStarPresentBit = 0x01;
    private const byte TemporaryStarClearsAtTurnEndBit = 0x02;
    private const byte TemporaryStarClearsWhenPlayedBit = 0x04;
    private const byte CloneBit = 0x08;
    private const byte ReturnToHandNextTurnBit = 0x10;
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
    /// Bit 0: temporary star cost present; bit 1: it clears at turn end; bit 2: it clears when
    /// played; bit 3: <see cref="IsClone"/>; bit 4: <see cref="ReturnToHandNextTurn"/>; bits 5-7:
    /// native enchantment status. The energy modifier count has its own byte so the capacity is not
    /// limited to the two bits the packed state used to have.
    /// </summary>
    public byte CostState;

    /// <summary>Number of used entries in <see cref="EnergyModifiers"/> (0..<see cref="FlashLimits.CardEnergyCostModifiers"/>).</summary>
    public byte ModifierCount;

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

    /// <summary>
    /// While MagiKnight's DampenPower has the card downgraded, the upgrade level before the downgrade
    /// plus one (0 = not dampened). Restoring the caster's death re-applies that many upgrades.
    /// </summary>
    public byte DampenOriginalUpgrade;

    /// <summary>
    /// One-action identity mark used by BeatDown to follow a card object after an earlier auto-play
    /// moves it out of the discard pile. 0 means unmarked. The field occupies existing tail padding;
    /// card size stays 92 bytes. BeatDown clears every mark before its OnPlay returns, so a stable
    /// boundary never contains a nonzero value. Gameplay clones and generated cards start at 0.
    /// </summary>
    public byte TransientMark;

    public int EnergyModifierCount
    {
        readonly get => ModifierCount;
        set
        {
            if ((uint)value > FlashLimits.CardEnergyCostModifiers)
                throw new FlashCapacityException("Card.EnergyModifiers", FlashLimits.CardEnergyCostModifiers);
            ModifierCount = (byte)value;
        }
    }

    /// <summary>
    /// Native <c>CardModel.IsClone</c>. Clones skip the card-specific part of
    /// <c>AfterCardEnteredCombat</c> (Stomp's cost reduction and similar); the bit lives in the spare
    /// bit 3 of <c>CostState</c>, which the temporary-star and enchantment setters preserve.
    /// </summary>
    public bool IsClone
    {
        readonly get => (CostState & CloneBit) != 0;
        set => CostState = (byte)((CostState & ~CloneBit) | (value ? CloneBit : 0));
    }

    /// <summary>
    /// Native <c>SimulatedCombatState._returnToHandNextTurn</c> membership (Bolas and ThrummingHatchet):
    /// the card goes back to hand at the next player turn's BeforeHandDraw. The existing engine keeps
    /// the played card object in a set; Flash carries the same membership on the card value.
    /// </summary>
    public bool ReturnToHandNextTurn
    {
        readonly get => (CostState & ReturnToHandNextTurnBit) != 0;
        set => CostState = (byte)((CostState & ~ReturnToHandNextTurnBit) | (value ? ReturnToHandNextTurnBit : 0));
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

/// <summary>
/// Waterfall Giant's <c>SimulatedCombatState._steamEruptionPhases</c> entry. Zero means no forced
/// Steam Eruption action; the two values are the forced <c>ABOUT_TO_BLOW_MOVE</c> and
/// <c>EXPLODE_MOVE</c> phases (<c>SimulatedCombatState.TryConsumeForcedMonsterMove</c>).
/// </summary>
public enum FlashSteamPhase : byte
{
    None = 0,
    AboutToBlow = 1,
    Explode = 2,
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

    /// <summary>
    /// Powered attack hits dealt to this enemy by the player this turn
    /// (<c>SimulatedCombatState.GetPoweredAttackHitsThisTurn(player, enemy)</c>): BeatIntoShape's
    /// calculated-forge multiplier. Imported for the root turn and reset for every enemy in
    /// <c>BeginSideTurn(player)</c>; a hit that does not fit the byte is an explicit boundary.
    /// </summary>
    public byte PoweredAttackHitsThisTurn;

    /// <summary>
    /// Waterfall Giant's forced Steam Eruption phase (<c>SimulatedCombatState._steamEruptionPhases</c>).
    /// Stored in the former padding byte; zero for every other monster and while no eruption is armed.
    /// </summary>
    public FlashSteamPhase SteamPhase;

    private byte _padding1;

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

/// <summary>Selected-card values of instanced powers (NightmarePower); a zeroed slot is free.</summary>
[InlineArray(FlashLimits.DetachedCards)]
public struct FlashDetachedCardArray
{
    private FlashCard _first;
}

/// <summary>
/// Exact decimal card-variable deltas referenced by <see cref="FlashCard.Private"/> (slot + 1).
/// Canonicalized in detached-card then card storage order at every stable boundary.
/// </summary>
[InlineArray(FlashLimits.DecimalCardVars)]
public struct FlashDecimalVarArray
{
    private decimal _first;
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
