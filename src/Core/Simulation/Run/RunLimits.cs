namespace Sts2Solver.Simulation.Run;

/// <summary>
/// Fixed capacities of <see cref="RunState"/>. Exceeding any of them throws
/// <see cref="CapacityException"/>; nothing is truncated. Sizes are the native upper bounds or,
/// where native is unbounded, the largest value seen in the captured single-player corpus
/// (see <c>docs/run-state-layout.md</c>).
/// </summary>
public static class RunLimits
{
    /// <summary>Native act list for a standard run is three; four leaves room for one variant act.</summary>
    public const int Acts = 4;

    /// <summary><c>RoomSet.events</c> for one act (largest observed act has 31; 64 is headroom).</summary>
    public const int ActEvents = 64;

    /// <summary><c>RoomSet.normalEncounters</c>; native generates at most 15 rooms per act.</summary>
    public const int ActNormalEncounters = 24;

    /// <summary><c>RoomSet.eliteEncounters</c>; native always generates 15.</summary>
    public const int ActEliteEncounters = 16;

    /// <summary>Deck cards. Native has no hard cap; 128 is above the largest captured deck.</summary>
    public const int DeckCards = 128;

    /// <summary>Held relics. Native has no hard cap; 64 is above the largest captured relic bar.</summary>
    public const int Relics = 64;

    /// <summary>Potion slots grow through events; 8 covers the largest observed belt.</summary>
    public const int PotionSlots = 8;

    /// <summary>
    /// Entries per rarity in one grab bag. The shared bag contains every eligible relic once
    /// (about 103 at 0.111.0), so 160 covers the pool plus refreshes.
    /// </summary>
    public const int RelicBagPerRarity = 160;

    /// <summary>Relic rarities in native <c>RelicRarity</c> order.</summary>
    public const int RelicRarities = 8;

    /// <summary>Map points including the starting, boss and second-boss points.</summary>
    public const int MapPoints = 160;

    /// <summary>A map point's children. Seven paths can merge into one point.</summary>
    public const int MapChildren = 7;

    /// <summary>Starting row points; the native generator creates seven paths.</summary>
    public const int MapStarts = 7;

    /// <summary>Visited coordinates in the current act; the map is at most 7 x 24.</summary>
    public const int VisitedCoords = 64;

    /// <summary>Map-point history entries across all acts (3 acts x 16 floors, with headroom).</summary>
    public const int MapHistory = 96;

    /// <summary>Rooms recorded in one map-point history entry (event -&gt; combat chains).</summary>
    public const int HistoryRooms = 4;

    /// <summary>Monster ids recorded for one history room.</summary>
    public const int HistoryMonsters = 8;

    /// <summary>Current room stack depth (an event can push a combat room).</summary>
    public const int RoomStack = 4;

    /// <summary>Active event RNGs; only the current event chain needs state.</summary>
    public const int EventRngs = 4;

    /// <summary>Out-of-combat RNG streams plus the nine carried combat streams.</summary>
    public const int RngStreams = 15;

    /// <summary>Per-act map RNGs (one per possible act).</summary>
    public const int ActMapRngs = Acts;

    /// <summary>Unlock epochs at 0.111.0: 57. Two words leave headroom.</summary>
    public const int UnlockEpochWords = 2;

    /// <summary>Seen encounters at 0.111.0: 89. Two words leave headroom.</summary>
    public const int UnlockEncounterWords = 2;

    /// <summary>Visited event ids at 0.111.0: 66. Two words leave headroom.</summary>
    public const int VisitedEventWords = 2;

    /// <summary>
    /// Choices one opening build may ask. Toolbox, GamblingChip, ToastyMittens and ChoicesParadox
    /// each ask at most once (4); the bound is the kernel's per-action choice capacity.
    /// </summary>
    public const int CombatSetupChoices = 8;

    /// <summary>Per-relic serialized private-property bag.</summary>
    public const int RelicPropertyBytes = 64;

    /// <summary>Per-card (and per-enchantment) serialized private-property bag.</summary>
    public const int CardPropertyBytes = 32;

    /// <summary>Maximum properties stored in one bag (saved-property lists are tiny).</summary>
    public const int PropertyCount = 16;

    /// <summary>Maximum string bytes in one saved property.</summary>
    public const int PropertyStringBytes = 32;

    /// <summary>Maximum ints in one <c>int[]</c> saved property (FurCoat marks at most 4).</summary>
    public const int PropertyInts = 12;

    /// <summary>Maximum cards in one <c>List&lt;SerializableCard&gt;</c> saved property.</summary>
    public const int PropertyCards = 4;

    /// <summary>
    /// Options in one decision request. The largest native screen is deck card selection, which
    /// lists every removable deck card. Keep the existing 129-row engineering budget; cancel is
    /// an independent answer and does not occupy an option slot.
    /// </summary>
    public const int DecisionOptions = DeckCards + 1;

    /// <summary>Character cards offered by the normal merchant (<c>_coloredCardTypes.Length</c>).</summary>
    public const int ShopCharacterCards = 5;

    /// <summary>Colorless cards offered by the normal merchant (<c>_colorlessCardRarities.Length</c>).</summary>
    public const int ShopColorlessCards = 2;

    /// <summary>Relic slots offered by the normal merchant.</summary>
    public const int ShopRelics = 3;

    /// <summary>Potion slots offered by the normal merchant.</summary>
    public const int ShopPotions = 3;

    /// <summary>
    /// Rewards in one post-combat set. Native has no hard cap (The Hunt can append one card reward
    /// per successful kill); 16 is the explicit overflow guard for this boundary.
    /// </summary>
    public const int Rewards = 16;

    /// <summary>
    /// Card options stored for all pending card/special-card rewards. Two combat card rewards with
    /// Lasting Candy's fourth option, plus The Hunt extras, fit well below 16.
    /// </summary>
    public const int RewardCardOptions = 16;
}
