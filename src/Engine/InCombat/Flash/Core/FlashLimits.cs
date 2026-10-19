namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Fixed capacities of <see cref="FlashBattle"/>. Exceeding any of them throws
/// <see cref="FlashCapacityException"/>; nothing is truncated.
/// </summary>
public static class FlashLimits
{
    public const int Cards = 96;
    public const int Enemies = 10;
    public const int Powers = 64;
    public const int Relics = 32;
    public const int PotionSlots = 5;
    public const int Orbs = 10;
    public const int Counters = 48;
    public const int RngStreams = 9;
    public const int EnemyAiRegisters = 4;
    public const int EnemyRecentMoves = 8;

    /// <summary>
    /// Total monster rule entries: the root's known enemies first, then one entry per spawnable
    /// monster type in the closure (<c>FlashRulesBuilder.SetMonster</c>/<c>AddSpawnableMonster</c>).
    /// </summary>
    public const int MonsterRules = 32;

    /// <summary>
    /// Local energy-cost modifiers of one card. Stomp adds one <c>EndOfTurn</c> modifier per Attack
    /// played this turn (<c>Stomp.BeforeCardPlayed</c>) on top of the modifiers other effects write,
    /// so the native unbounded list needs room for a full turn's plays. Overflow still throws
    /// <see cref="FlashCapacityException"/> and falls back instead of truncating.
    /// </summary>
    public const int CardEnergyCostModifiers = 16;
    // One action may carry a primary choice plus nested auto-play choices. Decisions, Decisions
    // replays the chosen card up to its Repeat variable (3), and a chosen Decisions, Decisions can
    // nest again for each skill in hand; the existing engine keeps those frames unbounded. Every
    // beam node stores a whole FlashAction, so a deeper chain is a FlashCapacityException boundary
    // (the branch is dropped and the existing solver keeps it) rather than a larger inline array:
    // 32 slots cost about 10% more managed allocation on the starter-deck bench for no reached route.
    public const int MaxChoices = 8;

    /// <summary>Options of one choice request; selections are bit sets over them.</summary>
    public const int MaxChoiceOptions = 64;

    /// <summary>
    /// Power amount changes queued between <c>ResolvePowerAmountChanges</c> calls. The existing engine
    /// keeps an unbounded list; a single card's applications are far below this bound, so overflow
    /// throws <see cref="FlashCapacityException"/> rather than truncating.
    /// </summary>
    public const int PendingPowerAmountChanges = 64;

    /// <summary>Options of a generated-card choice (not in any pile), carried by value.</summary>
    public const int MaxGeneratedChoiceOptions = 8;
    public const int MaxHandSize = 10;

    /// <summary>
    /// Power amounts snapshotted at <c>BeforeCardPlayed</c> for the <c>AfterCardPlayed</c> consumer
    /// (the existing engine's <c>CardPlayPairPredictionState</c>): one entry per listener and play
    /// index. Nested auto-plays push their own entries above the outer play's, so the stack bounds
    /// the nesting depth of one action; overflow throws <see cref="FlashCapacityException"/>.
    /// </summary>
    public const int CardPlayPairs = 32;

    /// <summary>
    /// Selected-card values carried by instanced powers that clone them later (NightmarePower). The
    /// existing engine keeps one detached <c>CardModel</c> clone per instance; Flash stores its full
    /// value in a compact table. Exceeding the capacity throws <see cref="FlashCapacityException"/>
    /// instead of evicting a live selection.
    /// </summary>
    public const int DetachedCards = 3;

    /// <summary>
    /// Exact decimal card-variable deltas (Thrash's <c>DynamicVars.Damage.BaseValue</c> is changed by
    /// a decimal <c>ModifyDamage</c> result). The value table lives in the header; a card references
    /// a slot through <see cref="FlashCard.Private"/>. Slots are canonicalized in card order at every
    /// stable boundary, so the byte image does not depend on allocation history. The existing engine
    /// keeps one decimal per card object; four simultaneous live values cover the Thrash copies in the admitted decks,
    /// and a fifth distinct value falls back through <see cref="FlashCapacityException"/>.
    /// </summary>
    public const int DecimalCardVars = 4;

    /// <summary>Sentinel of the <see cref="FlashCard.Private"/> identity ledger: no applied entry yet.</summary>
    public const short UninitializedLedger = short.MinValue;

    /// <summary>
    /// Per-creature temporary Power ids doubled by UnsettlingLamp within one card's power
    /// application scope (<c>_unsettlingLampInternalPowerTypes</c>). A card applies at most a few
    /// temporary stats; overflow throws <see cref="FlashCapacityException"/>.
    /// </summary>
    public const int LampInternalPowerTypes = 4;
    public const int MonologuePending = 8;

    /// <summary>Owner index of the player in <see cref="FlashPower.Owner"/> and creature references.</summary>
    public const byte PlayerOwner = 0;

    /// <summary>Owner index of Osty; enemies start at <see cref="FirstEnemyOwner"/>.</summary>
    public const byte OstyOwner = 1;

    public const byte FirstEnemyOwner = 2;

    /// <summary>Marker for "no creature" in applier/target fields.</summary>
    public const byte NoOwner = byte.MaxValue;

    /// <summary>Clamp used by the existing engine for HP, block, energy, stars and power amounts.</summary>
    public const int ValueClamp = 999_999_999;
}
