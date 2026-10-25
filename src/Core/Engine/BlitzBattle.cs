using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlitzSolver.Engine;

public enum BlitzSide : byte
{
    None = 0,
    Player = 1,
    Enemy = 2,
}

/// <summary>Native <c>PlayerTurnPhase</c>; values match the native enum.</summary>
public enum BlitzPlayerPhase : byte
{
    None = 0,
    Start = 1,
    AutoPrePlay = 2,
    Play = 3,
    AutoPostPlay = 4,
    End = 5,
}

public enum BlitzOutcome : byte
{
    None = 0,
    Victory = 1,
    Defeat = 2,

    /// <summary>
    /// The encounter's event-defeat ending (<c>SearchBoundaryReason.EventDefeat</c>): the Battleworn
    /// Dummy's time limit expired and the existing engine stopped at
    /// <c>SimulatedCombatState.BattlewornDummyTimedOut</c>. Like the existing search's <c>won</c>, this
    /// is neither a victory nor a normal defeat.
    /// </summary>
    EventDefeat = 3,
}

[Flags]
public enum BlitzBattleFlags : byte
{
    None = 0,
    PlayerHooksActive = 1 << 0,
    PlayerTurnEndRequested = 1 << 1,
    OstyPresent = 1 << 2,

    /// <summary>Native <c>CombatTurnState.PendingLoss</c> (<c>IsAboutToLose</c>).</summary>
    PendingLoss = 1 << 3,

    /// <summary>
    /// Native <c>CombatPredictionSimulator.HasRisk</c> (the snapshot's uncompensated risk). Imported
    /// from the root; generated-card choices are compensated, so the kernel never raises it itself.
    /// Monotonic for the rest of the combat.
    /// </summary>
    HasRisk = 1 << 4,

    /// <summary>
    /// Osty existed in the root's native <c>PlayerCombatState</c> (<c>Player.Osty != null</c>). The existing
    /// engine's <c>BeginSideTurn(player)</c> resets Osty's per-turn counters only through that native pet
    /// field, so an Osty created during the search never resets them. Constant for the solve; imported.
    /// </summary>
    OstyWasRootPet = 1 << 5,

    /// <summary>
    /// <see cref="BlitzBattle.HistoryCourseCard"/> holds the last attack the player finished this turn
    /// (<c>SimulatedCombatState._lastAttackThisTurn</c>). Cleared when the turn-start auto-play consumes
    /// it and when an imported stable boundary has no live value.
    /// </summary>
    HistoryCourseCardPresent = 1 << 6,

}

/// <summary>Combat pile segments, in their storage order inside <see cref="BlitzBattle.Cards"/>.</summary>
public enum BlitzPile : byte
{
    Hand = 0,
    Draw = 1,
    Discard = 2,
    Exhaust = 3,
    Play = 4,
}

/// <summary>
/// Indexes into <see cref="BlitzBattle.Counters"/>. Only counters that affect future resolution
/// belong here; presentation-only history stays out of the state.
/// </summary>
public enum BlitzCounter : byte
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

    // Route counters read by the score port (BlitzScorer). Appended only, so every existing value
    // keeps its index.
    LongTermResourceValue,
    AngerCopiesGenerated,
    DeathSaveRelicHpRestored,
    DeathSavePotionHpRestored,
    DeathSaveUseCount,
    AutomaticPotionUseCount,

    /// <summary>
    /// <c>SimulatedCombatState._lastNormalizedVitalSparkAmount</c> (<see cref="int.MinValue"/> = null).
    /// <see cref="BlitzKernel.NormalizeCardAfflictions"/> rewrites Tainted amounts only when this
    /// differs from the current total Vital Spark amount.
    /// </summary>
    VitalSparkNormalizedAmount,

    /// <summary>
    /// <c>SimulatedCombatState._skillsPlayedThisTurn</c> membership for the player: 1 after a Skill
    /// card finished this side turn, reset at that side's turn end. Distinct from
    /// <see cref="SkillCardsPlayedThisTurn"/>, which the evaluation and Letter Opener consume.
    /// </summary>
    SkillPlayedThisSideTurn,

    /// <summary>
    /// Combat-long finished card plays (<c>SimulatedCombatState.GetFinishedPlaysBeforePrediction</c>
    /// plus every completed play in this search): the GoldAxe calculated-damage multiplier
    /// (<c>CalculatedVarSpecRegistry.CountFinishedCardPlays</c>).
    /// </summary>
    FinishedCardPlays,
    /// Bitmask of enemy owners whose MagiKnight applied DampenPower to the player
    /// (<c>SimulatedCombatState._dampenCasters</c>), one bit per enemy owner index.
    /// </summary>
    DampenCasterMask,

    /// <summary>
    /// Combat-long cards drawn by the player, including the root history
    /// (<c>SimulatedCombatState.GetCardsDrawnBeforePrediction</c>): the Murder calculated-damage
    /// multiplier (<c>CalculatedVarSpecRegistry.CountDrawnCards</c>).
    /// </summary>
    CardsDrawn,

    /// <summary>
    /// Lightning orbs channelled this combat (<c>CalculatedVarSpecRegistry.CountLightningChannels</c>):
    /// Voltaic's calculated channel count.
    /// </summary>
    LightningChannels,

    /// <summary>
    /// Every orb channelled this combat: the existing engine's 1000-channel safety cap
    /// (<c>CombatPredictionSimulator.MaxSimulatedChanneledOrbs</c>).
    /// </summary>
    OrbChannels,

    /// <summary>
    /// Stars gained this player turn (<c>SimulatedCombatState.GetStarsGainedThisTurn</c>, positive
    /// gains only): Radiate's CalculatedHits multiplier. Reset in the player side-turn begin.
    /// </summary>
    StarsGainedThisTurn,

    /// <summary>
    /// Creature attacks by Osty this turn (<c>SimulatedCombatState._creatureAttacksThisTurn</c> for the
    /// Osty pet): the Rattle calculated-hit multiplier. Reset in <c>BeginSideTurn(player)</c> only when
    /// <see cref="BlitzBattleFlags.OstyWasRootPet"/> is set, matching the existing engine's
    /// <c>ownerPlayer.Osty</c> lookup. Root history is imported.
    /// </summary>
    OstyAttacksThisTurn,

    /// <summary>
    /// The player applied DoomPower this turn (<c>SimulatedCombatState._doomAppliersThisTurn</c> for the
    /// player): the DeathsDoor block-repeat condition. Reset at the player's turn start.
    /// </summary>
    DoomAppliedThisTurn,

    /// <summary>
    /// Combat-long Ethereal card plays by the player (<c>CountEtherealPlays</c>: root history plus each
    /// <c>CardPlayFinishedEntry.WasEthereal</c>): the PullFromBelow multiplier. Only grows.
    /// </summary>
    EtherealPlays,

    /// <summary>
    /// Combat-long cards generated by the player (<c>CalculatedVarSpecRegistry.CountGeneratedCards</c>:
    /// <c>SimulatedCombatState.GetCardsGeneratedBeforePrediction</c> plus every
    /// <c>CombatPredictionCardGeneratedEntry</c> whose creator is the player): Supermassive's
    /// calculated-damage multiplier. Imported from the root history and incremented on every generation
    /// path that records <c>CombatPredictionHistory.CardGenerated</c>.
    /// </summary>
    CardsGeneratedThisCombat,

    /// <summary>
    /// Potions generated during this combat and then used (they never occupied a root slot). The
    /// existing snapshot's <c>PotionUseCount</c> counts every <c>PredictedPotionUse</c>; the scorer
    /// adds this counter because the root-slot diff cannot see generated uses.
    /// </summary>
    GeneratedPotionUseCount,

    /// <summary>
    /// Packed strategic cost of generated potion uses: low 16 bits explicit uses, high 16 bits
    /// automatic uses. <c>generated = the slot's potion is not the root potion for that slot</c>;
    /// <c>freeFromEntropic</c> potions cost zero. The split keeps automatic uses out of
    /// <c>ExplicitPotionStrategicCost</c> exactly as the root-slot diff does.
    /// </summary>
    GeneratedPotionStrategicCost,

    /// <summary>
    /// Bit i set when potion slot i holds a potion procured with <c>freeFromEntropic: true</c>
    /// (<c>SimulatedCombatState._freeEntropicPotions</c>). Consuming the slot clears the bit; a used
    /// free potion contributes zero <c>PotionUsePolicy.StrategicHpCost</c>.
    /// </summary>
    EntropicFreePotionMask,

    /// <summary>
    /// Stolen gold still held by a living thief (<c>SimulatedCombatState._outstandingStolenGold</c>):
    /// <c>ThieveryPower.DynamicVars.Gold</c> plus every <c>HeistPower</c> amount. Incremented when
    /// <c>RecordThievery</c> steals gold, decremented when a <c>HeistPower</c> owner dies
    /// (<c>RecoverStolenResources</c>); escaping leaves it outstanding.
    /// </summary>
    OutstandingStolenGold,

    /// <summary>
    /// Stolen cards still held by a living thief (<c>SimulatedCombatState._outstandingStolenCards</c>):
    /// one per <c>SwipePower</c> with a stolen card. Incremented by THIEVERY, decremented by
    /// <c>RecoverStolenResources</c> when the thief dies; escaping leaves it outstanding.
    /// </summary>
    OutstandingStolenCards,

    // Per-source growth reward counts (SimulatedCombatState._growthRewards). Two 16-bit source
    // values per counter; see BlitzGrowthRewards. The fixed counter array has room for all of them,
    // so the BlitzBattle layout is unchanged.
    GrowthRewardsHandOfGreedTheHunt,
    GrowthRewardsFeedRoyalties,
    GrowthRewardsAlchemizeGeneticAlgorithm,
    GrowthRewardsTheScytheGoopy,
    GrowthRewardsForbiddenGrimoireMadScience,
}

/// <summary>
/// All branch-mutable state of one combat at a stable boundary. Unmanaged and reference-free:
/// a copy is a deep fork, and the byte image is the state identity. See
/// <c>docs/state-layout.md</c>. Field order avoids implicit padding; the total size
/// is asserted by <c>BlitzSolver.KernelChecks</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BlitzBattle
{
    public const int ExpectedSize = 12696;

    public int Round;
    public int PlayerTurn;
    public int ShuffleEventCount;
    public int ShufflesCrossed;
    public uint NextCombatId;
    public int Energy;
    public int Stars;
    public int Gold;

    public BlitzSide Side;
    public BlitzPlayerPhase Phase;
    public BlitzOutcome Outcome;
    public BlitzBattleFlags Flags;

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

    /// <summary>
    /// Used entries of <see cref="DecimalCardVars"/> (canonical: contiguous 0..count-1). Reuses a
    /// former header padding byte so the count does not add to the layout.
    /// </summary>
    public byte DecimalVarCount;
    private byte _padding1;
    private byte _padding2;

    /// <summary>
    /// Selected-card values of instanced powers that clone them at a later turn (NightmarePower).
    /// Referenced by <see cref="BlitzPower.Aux0"/> (slot + 1); a zeroed slot is free. Part of the
    /// header byte image so both the fingerprint and equality cover it.
    /// </summary>
    public BlitzDetachedCardArray DetachedCards;

    /// <summary>
    /// Exact decimal card-variable deltas (Thrash). A card references a slot through
    /// <see cref="BlitzCard.Private"/> (slot + 1); values are canonicalized in detached-card then
    /// card storage order at every stable boundary. Part of the header byte image.
    /// </summary>
    public BlitzDecimalVarArray DecimalVars;

    /// <summary>Indexes into <see cref="Enemies"/>, in native roster order.</summary>
    public BlitzRoster Roster;

    public BlitzPotionSlots Potions;
    private short _padding3;

    public BlitzCreature Player;
    public BlitzCreature Osty;
    public BlitzCounterArray Counters;
    public BlitzRngStreams Rng;
    public BlitzEnemyArray Enemies;
    public BlitzPowerArray Powers;
    public BlitzRelicArray Relics;
    public BlitzOrbArray Orbs;
    public BlitzCardArray Cards;

    /// <summary>
    /// The last attack the player finished this turn, as a full card value
    /// (<c>SimulatedCombatState._lastAttackThisTurn</c>; HistoryCourse clones it at the next player turn's
    /// AutoPrePlay). One cross-turn slot; <see cref="BlitzBattleFlags.HistoryCourseCardPresent"/> says
    /// whether it is live. Sized as one <see cref="BlitzCard"/> at the end of the struct so the rest of
    /// the layout is unchanged. Imported from the existing engine's per-turn dictionary (never from the
    /// retired previous-turn dictionary: AutoPrePlay already ran before every search root).
    /// </summary>
    public BlitzCard HistoryCourseCard;

    public readonly int CardCount => HandCount + DrawCount + DiscardCount + ExhaustCount + PlayCount;

    public readonly bool IsOver => Outcome != BlitzOutcome.None;

    public readonly int Counter(BlitzCounter counter) => Counters[(int)counter];

    public void AddCounter(BlitzCounter counter, int amount) => Counters[(int)counter] += amount;

    public void SetCounter(BlitzCounter counter, int value) => Counters[(int)counter] = value;

    /// <summary>Creature behind an owner index: player, Osty or a known enemy.</summary>
    [UnscopedRef]
    public ref BlitzCreature Creature(byte owner)
    {
        if (owner == BlitzLimits.PlayerOwner)
            return ref Player;
        if (owner == BlitzLimits.OstyOwner)
            return ref Osty;
        int enemy = owner - BlitzLimits.FirstEnemyOwner;
        if ((uint)enemy >= EnemyCount)
            throw new ArgumentOutOfRangeException(nameof(owner), owner, "未知的生物下标。");
        return ref Enemies[enemy].Creature;
    }

    [UnscopedRef]
    public ref BlitzEnemy Enemy(byte owner)
    {
        int enemy = owner - BlitzLimits.FirstEnemyOwner;
        if ((uint)enemy >= EnemyCount)
            throw new ArgumentOutOfRangeException(nameof(owner), owner, "未知的敌人下标。");
        return ref Enemies[enemy];
    }

    [UnscopedRef]
    public ref BlitzRngState RngStream(BlitzRngStream stream) => ref Rng[(int)stream];
}

/// <summary>Pile segment operations over <see cref="BlitzBattle.Cards"/>.</summary>
public static class BlitzPiles
{
    public static int Count(in BlitzBattle battle, BlitzPile pile) => pile switch
    {
        BlitzPile.Hand => battle.HandCount,
        BlitzPile.Draw => battle.DrawCount,
        BlitzPile.Discard => battle.DiscardCount,
        BlitzPile.Exhaust => battle.ExhaustCount,
        BlitzPile.Play => battle.PlayCount,
        _ => throw new ArgumentOutOfRangeException(nameof(pile), pile, null),
    };

    public static int Start(in BlitzBattle battle, BlitzPile pile) => pile switch
    {
        BlitzPile.Hand => 0,
        BlitzPile.Draw => battle.HandCount,
        BlitzPile.Discard => battle.HandCount + battle.DrawCount,
        BlitzPile.Exhaust => battle.HandCount + battle.DrawCount + battle.DiscardCount,
        BlitzPile.Play => battle.HandCount + battle.DrawCount + battle.DiscardCount + battle.ExhaustCount,
        _ => throw new ArgumentOutOfRangeException(nameof(pile), pile, null),
    };

    /// <summary>Pile and pile-local index of an absolute card position.</summary>
    public static BlitzPile PileOf(in BlitzBattle battle, int absolute, out int index)
    {
        if ((uint)absolute >= (uint)battle.CardCount)
            throw new ArgumentOutOfRangeException(nameof(absolute), absolute, "卡牌绝对位置越界。");
        BlitzPile pile = absolute switch
        {
            _ when absolute < battle.HandCount => BlitzPile.Hand,
            _ when absolute < battle.HandCount + battle.DrawCount => BlitzPile.Draw,
            _ when absolute < battle.HandCount + battle.DrawCount + battle.DiscardCount => BlitzPile.Discard,
            _ when absolute < battle.HandCount + battle.DrawCount + battle.DiscardCount + battle.ExhaustCount =>
                BlitzPile.Exhaust,
            _ => BlitzPile.Play,
        };
        index = absolute - Start(battle, pile);
        return pile;
    }

    public static Span<BlitzCard> Cards(ref BlitzBattle battle, BlitzPile pile)
        => MemoryMarshal.CreateSpan(ref battle.Cards[0], BlitzLimits.Cards).Slice(Start(battle, pile), Count(battle, pile));

    public static ReadOnlySpan<BlitzCard> ReadCards(in BlitzBattle battle, BlitzPile pile)
        => MemoryMarshal.CreateReadOnlySpan(in battle.Cards[0], BlitzLimits.Cards).Slice(Start(battle, pile), Count(battle, pile));

    public static ref BlitzCard At(ref BlitzBattle battle, BlitzPile pile, int index)
    {
        if ((uint)index >= (uint)Count(battle, pile))
            throw new ArgumentOutOfRangeException(nameof(index), index, $"{pile} 牌堆没有这个位置。");
        return ref battle.Cards[Start(battle, pile) + index];
    }

    /// <summary>Removes and returns the card at <paramref name="index"/>; later cards shift left.</summary>
    public static BlitzCard RemoveAt(ref BlitzBattle battle, BlitzPile pile, int index)
    {
        int count = Count(battle, pile);
        if ((uint)index >= (uint)count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"{pile} 牌堆没有这个位置。");
        int total = battle.CardCount;
        int position = Start(battle, pile) + index;
        Span<BlitzCard> all = MemoryMarshal.CreateSpan(ref battle.Cards[0], BlitzLimits.Cards);
        BlitzCard card = all[position];
        all.Slice(position + 1, total - position - 1).CopyTo(all.Slice(position));
        all[total - 1] = default;
        SetCount(ref battle, pile, count - 1);
        return card;
    }

    /// <summary>Inserts <paramref name="card"/> at <paramref name="index"/>; later cards shift right.</summary>
    public static void Insert(ref BlitzBattle battle, BlitzPile pile, int index, in BlitzCard card)
    {
        int count = Count(battle, pile);
        if ((uint)index > (uint)count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"{pile} 牌堆插入位置越界。");
        int total = battle.CardCount;
        if (total >= BlitzLimits.Cards)
            throw new BlitzCapacityException("Cards", BlitzLimits.Cards);
        int position = Start(battle, pile) + index;
        Span<BlitzCard> all = MemoryMarshal.CreateSpan(ref battle.Cards[0], BlitzLimits.Cards);
        all.Slice(position, total - position).CopyTo(all.Slice(position + 1));
        all[position] = card;
        SetCount(ref battle, pile, count + 1);
    }

    public static void Append(ref BlitzBattle battle, BlitzPile pile, in BlitzCard card)
        => Insert(ref battle, pile, Count(battle, pile), card);

    /// <summary>
    /// Moves the top draw card to the end of the hand. The draw segment directly follows the hand,
    /// so this only moves the boundary.
    /// </summary>
    public static void DrawTopToHand(ref BlitzBattle battle)
    {
        if (battle.DrawCount == 0)
            throw new InvalidOperationException("抽牌堆为空。");
        battle.HandCount++;
        battle.DrawCount--;
    }

    private static void SetCount(ref BlitzBattle battle, BlitzPile pile, int value)
    {
        byte count = checked((byte)value);
        switch (pile)
        {
            case BlitzPile.Hand: battle.HandCount = count; break;
            case BlitzPile.Draw: battle.DrawCount = count; break;
            case BlitzPile.Discard: battle.DiscardCount = count; break;
            case BlitzPile.Exhaust: battle.ExhaustCount = count; break;
            case BlitzPile.Play: battle.PlayCount = count; break;
            default: throw new ArgumentOutOfRangeException(nameof(pile), pile, null);
        }
    }
}

/// <summary>
/// The ten built-in growth reward sources, in the order of the existing engine's
/// <c>GrowthSource</c> enum. The kernel cannot reference that enum; the bridge maps by index.
/// </summary>
public enum BlitzGrowthSource : byte
{
    HandOfGreed = 0,
    TheHunt,
    Feed,
    Royalties,
    Alchemize,
    GeneticAlgorithm,
    TheScythe,
    Goopy,
    ForbiddenGrimoire,
    MadScience,
}

/// <summary>
/// Accessors for <c>SimulatedCombatState._growthRewards</c> inside <see cref="BlitzBattle.Counters"/>.
/// Each counter packs two 16-bit source counts so the ten sources fit in five of the existing
/// counters; the counters array size (and therefore the state layout) does not change. The existing
/// engine records at most one reward per resolved card effect, orders of magnitude below the 16-bit
/// ceiling; a value that would overflow is an explicit capacity boundary, never truncated.
/// </summary>
public static class BlitzGrowthRewards
{
    public const int SourceCount = 10;
    private const int Mask = 0xFFFF;

    public static int Get(in BlitzBattle battle, BlitzGrowthSource source)
    {
        int packed = battle.Counter(Counter(source));
        return ((int)source & 1) == 0 ? packed & Mask : (int)((uint)packed >> 16) & Mask;
    }

    public static void Add(ref BlitzBattle battle, BlitzGrowthSource source)
    {
        int value = Get(battle, source);
        if (value == Mask)
            throw new BlitzCapacityException($"{nameof(BlitzGrowthRewards)}.{source}", Mask);
        Set(ref battle, source, value + 1);
    }

    public static void Set(ref BlitzBattle battle, BlitzGrowthSource source, int value)
    {
        if ((uint)value > Mask)
            throw new BlitzCapacityException($"{nameof(BlitzGrowthRewards)}.{source}", Mask);
        BlitzCounter counter = Counter(source);
        int packed = battle.Counter(counter);
        packed = ((int)source & 1) == 0
            ? (int)(((uint)packed & 0xFFFF0000u) | (uint)value)
            : (int)(((uint)packed & 0x0000FFFFu) | ((uint)value << 16));
        battle.SetCounter(counter, packed);
    }

    private static BlitzCounter Counter(BlitzGrowthSource source) => source switch
    {
        BlitzGrowthSource.HandOfGreed or BlitzGrowthSource.TheHunt
            => BlitzCounter.GrowthRewardsHandOfGreedTheHunt,
        BlitzGrowthSource.Feed or BlitzGrowthSource.Royalties
            => BlitzCounter.GrowthRewardsFeedRoyalties,
        BlitzGrowthSource.Alchemize or BlitzGrowthSource.GeneticAlgorithm
            => BlitzCounter.GrowthRewardsAlchemizeGeneticAlgorithm,
        BlitzGrowthSource.TheScythe or BlitzGrowthSource.Goopy
            => BlitzCounter.GrowthRewardsTheScytheGoopy,
        BlitzGrowthSource.ForbiddenGrimoire or BlitzGrowthSource.MadScience
            => BlitzCounter.GrowthRewardsForbiddenGrimoireMadScience,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}
