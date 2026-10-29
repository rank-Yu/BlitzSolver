using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Simulation.Run;

/// <summary>
/// Out-of-combat and carried combat streams, in the order stored by <see cref="RunState"/>.
/// The first six are the run/player streams that only exist outside combat; the last nine are the
/// combat streams carried across the battle boundary without re-derivation.
/// </summary>
public enum RunRngStream : byte
{
    UpFront,
    UnknownMapPoint,
    TreasureRoomRelics,
    Rewards,
    Shops,
    Transformations,
    Shuffle,
    CombatCardGeneration,
    CombatPotionGeneration,
    CombatCardSelection,
    CombatEnergyCosts,
    CombatTargets,
    CombatOrbGeneration,
    MonsterAi,
    Niche,
}

/// <summary>Native <c>MapPointType</c>; values match the game enum.</summary>
public enum RunMapPointType : byte
{
    Unassigned = 0,
    Unknown = 1,
    Shop = 2,
    Treasure = 3,
    RestSite = 4,
    Monster = 5,
    Elite = 6,
    Boss = 7,
    Ancient = 8,
}

/// <summary>Native <c>RoomType</c>; values match the game enum.</summary>
public enum RunRoomType : byte
{
    Unassigned = 0,
    Monster = 1,
    Elite = 2,
    Boss = 3,
    Treasure = 4,
    Shop = 5,
    Event = 6,
    RestSite = 7,
    Map = 8,
}

/// <summary>Native <c>GameMode</c>; only <see cref="Standard"/> is imported in R1b.</summary>
public enum RunGameMode : byte
{
    None = 0,
    Standard = 1,
    Daily = 2,
    Custom = 3,
}

/// <summary>What the player is currently being asked to decide, if anything.</summary>
public enum RunDecision : byte
{
    None = 0,
    MapPoint = 1,
    Event = 2,
    CombatReward = 3,
    CardReward = 4,
    Shop = 5,
    RestSite = 6,
    CardSelect = 7,
    RelicSelect = 8,
    PotionDiscard = 9,
    GameOver = 10,
    Treasure = 11,
}

/// <summary>Native <c>RelicRarity</c>; values match the game enum.</summary>
public enum RunRelicRarity : byte
{
    None = 0,
    Starter = 1,
    Common = 2,
    Uncommon = 3,
    Rare = 4,
    Shop = 5,
    Event = 6,
    Ancient = 7,
}

/// <summary>Native <c>RelicStatus</c>.</summary>
public enum RunRelicStatus : byte
{
    Normal = 0,
    Active = 1,
    Disabled = 2,
}

[Flags]
public enum RunRelicFlags : byte
{
    None = 0,
    Wax = 1 << 0,
    Melted = 1 << 1,
}

/// <summary>One map or history coordinate.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunCoord : IEquatable<RunCoord>
{
    public byte Col;
    public byte Row;

    public RunCoord(byte col, byte row)
    {
        Col = col;
        Row = row;
    }

    public readonly bool Equals(RunCoord other) => Col == other.Col && Row == other.Row;
    public readonly override bool Equals(object? obj) => obj is RunCoord other && Equals(other);
    public readonly override int GetHashCode() => (Col << 8) | Row;
    public readonly override string ToString() => $"{Col},{Row}";
}

/// <summary>Deck-card facts the native mutable card exposes but <c>SerializableCard</c> does not.</summary>
[Flags]
public enum RunCardFlags : byte
{
    None = 0,
    /// <summary>Native <c>IsRemovable</c> (not Eternal); the merchant removal filter.</summary>
    Removable = 1 << 0,
    /// <summary>Native <c>Type == CardType.Curse</c>; removal selection puts curses first.</summary>
    Curse = 1 << 1,
}

/// <summary>
/// A card held by the run (deck, or a card carried by a relic/event saved property). Only the
/// fields persisted by <c>SerializableCard</c> are stored. Saved properties whose value is a model
/// are encoded as a <see cref="RunRef"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunCard
{
    public ushort Id;
    public byte Upgrade;
    public byte HasEnchantment;
    public RunCardFlags Flags;
    public ushort EnchantmentId;
    public short EnchantmentAmount;
    public int FloorAddedToDeck;
    public RunPropertyBag Properties;
    public RunPropertyBag EnchantmentProperties;
}

/// <summary>A held relic, in native relic-bar order.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunRelic
{
    public ushort Id;
    public RunRelicStatus Status;
    public RunRelicFlags Flags;
    public short StackCount;
    public int DisplayAmount;
    public int FloorAddedToDeck;
    public RunPropertyBag Properties;
}

/// <summary>One potion slot; <see cref="Present"/> distinguishes an empty slot from potion id 0.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunPotion
{
    public ushort Id;
    public byte Present;
    private byte _padding;
}

/// <summary>Run-level reward/unknown-map odds (native <c>RunOddsSet</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunOdds
{
    public float Monster;
    public float Elite;
    public float Treasure;
    public float Shop;
}

/// <summary>Per-player card/potion odds (native <c>PlayerOddsSet</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunPlayerOdds
{
    public float CardRarity;
    public float PotionReward;
}

/// <summary>Native <c>ExtraRunFields</c>; combat/achievement-only counters live in the dump.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunExtraFields
{
    public byte StartedWithNeow;
    private byte _padding0;
    private ushort _padding1;
    public int TestSubjectKills;
}

/// <summary>Native <c>ExtraPlayerFields</c> fields that change future results.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunExtraPlayerFields
{
    public int CardShopRemovalsUsed;
    public int WongoPoints;
}

/// <summary>One RNG slot that belongs to an active event instance.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunEventRng
{
    public ushort EventId;
    public byte Active;
    private byte _padding;
    public RngState State;
}

/// <summary>One act's pre-generated room set.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunAct
{
    public ushort Id;
    public byte HasAncient;
    public ushort AncientId;
    public ushort BossId;
    public byte HasSecondBoss;
    public ushort SecondBossId;
    public ushort EventCount;
    public ushort NormalCount;
    public ushort EliteCount;
    public int EventsVisited;
    public int NormalVisited;
    public int EliteVisited;
    public int BossVisited;
    public RunActEvents Events;
    public RunActNormal Normal;
    public RunActElite Elite;
}

[InlineArray(RunLimits.ActEvents)]
public struct RunActEvents
{
    private ushort _first;
}

[InlineArray(RunLimits.ActNormalEncounters)]
public struct RunActNormal
{
    private ushort _first;
}

[InlineArray(RunLimits.ActEliteEncounters)]
public struct RunActElite
{
    private ushort _first;
}

[InlineArray(RunLimits.Acts)]
public struct RunActArray
{
    private RunAct _first;
}

[StructLayout(LayoutKind.Sequential)]
public struct RunActs
{
    public byte Count;
    private byte _padding;
    public RunActArray Items;
}

/// <summary>One map node, including the starting, boss and second-boss points.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunMapPoint
{
    public RunCoord Coord;
    public RunMapPointType Type;
    public byte CanBeModified;
    public byte ChildCount;
    public RunMapChildren Children;
}

[InlineArray(RunLimits.MapChildren)]
public struct RunMapChildren
{
    private RunCoord _first;
}

[InlineArray(RunLimits.MapPoints)]
public struct RunMapPointArray
{
    private RunMapPoint _first;
}

[InlineArray(RunLimits.MapStarts)]
public struct RunMapStartArray
{
    private RunCoord _first;
}

/// <summary>The current act's complete map, in native column-major enumeration order.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunMap
{
    public byte Width;
    public byte Height;
    public ushort NodeCount;
    public RunCoord Starting;
    public RunCoord Boss;
    public byte HasSecondBoss;
    public RunCoord SecondBoss;
    public byte StartCount;
    public RunMapStartArray Starts;
    public RunMapPointArray Nodes;
}

/// <summary>One room in a map-point history entry.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunHistoryRoom
{
    public RunRoomType RoomType;
    public RunRef Model;
    public byte MonsterCount;
    public RunHistoryMonsters Monsters;
    public int TurnsTaken;
}

[InlineArray(RunLimits.HistoryMonsters)]
public struct RunHistoryMonsters
{
    private ushort _first;
}

[InlineArray(RunLimits.HistoryRooms)]
public struct RunHistoryRoomArray
{
    private RunHistoryRoom _first;
}

[StructLayout(LayoutKind.Sequential)]
public struct RunHistoryEntry
{
    public byte ActIndex;
    public RunMapPointType MapPointType;
    public byte RoomCount;
    public RunHistoryRoomArray Rooms;
}

[InlineArray(RunLimits.MapHistory)]
public struct RunHistoryArray
{
    private RunHistoryEntry _first;
}

[StructLayout(LayoutKind.Sequential)]
public struct RunHistory
{
    public ushort Count;
    public RunHistoryArray Entries;
}

/// <summary>One room in the live room stack (the UI's current room and its parents).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunRoom
{
    public RunRoomType RoomType;
    public RunRef Model;
    public RunRef ParentEvent;
    public byte IsPreFinished;
    private byte _padding0;
    private ushort _padding1;
    /// <summary>
    /// Native <c>CombatRoom.GoldProportion</c> after <c>OnCombatEnded</c>; 1 outside combat rooms.
    /// Post-combat gold rewards scale by it.
    /// </summary>
    public float GoldProportion;
}

[InlineArray(RunLimits.RoomStack)]
public struct RunRoomArray
{
    private RunRoom _first;
}

[StructLayout(LayoutKind.Sequential)]
public struct RunRooms
{
    public byte Count;
    public RunRoomArray Items;
}

[StructLayout(LayoutKind.Sequential)]
public struct RunPotionArray
{
    public byte Count;
    private byte _padding;
    public RunPotionSlots Items;
}

[InlineArray(RunLimits.PotionSlots)]
public struct RunPotionSlots
{
    private RunPotion _first;
}

[StructLayout(LayoutKind.Sequential)]
public struct RunDeck
{
    public ushort Count;
    public RunCardArray Cards;
}

[InlineArray(RunLimits.DeckCards)]
public struct RunCardArray
{
    private RunCard _first;
}

[StructLayout(LayoutKind.Sequential)]
public struct RunRelics
{
    public byte Count;
    public RunRelicArray Items;
}

[InlineArray(RunLimits.Relics)]
public struct RunRelicArray
{
    private RunRelic _first;
}

[StructLayout(LayoutKind.Sequential)]
public struct RunRelicBagSegment
{
    public ushort Count;
    public RunRelicBagIds Ids;
}

[InlineArray(RunLimits.RelicBagPerRarity)]
public struct RunRelicBagIds
{
    private ushort _first;
}

[InlineArray(RunLimits.RelicRarities)]
public struct RunRelicBagSegments
{
    private RunRelicBagSegment _first;
}

/// <summary>
/// One native <c>RelicGrabBag</c>: per-rarity remaining order. The single-player game currently
/// populates all eight rarity buckets, including Event and Ancient; every bucket is preserved.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunRelicBag
{
    /// <summary>Native <c>RelicGrabBag._refreshAllowed</c> (shared bag true, player bag false).</summary>
    public byte RefreshAllowed;
    private byte _padding;
    public RunRelicBagSegments Segments;
}

/// <summary>Run-level RNG: named streams, per-act map streams and active event streams.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunRngState
{
    public ulong Seed;
    public ulong PlayerSeed;
    public RunRngStreams Streams;
    public RunMapRngs Map;
    public RunMapRngConsumed MapConsumed;
    public byte EventCount;
    public RunEventRngs Events;
}

[InlineArray(RunLimits.RngStreams)]
public struct RunRngStreams
{
    private RngState _first;
}

[InlineArray(RunLimits.ActMapRngs)]
public struct RunMapRngs
{
    private RngState _first;
}

[InlineArray(RunLimits.ActMapRngs)]
public struct RunMapRngConsumed
{
    private byte _first;
}

[InlineArray(RunLimits.EventRngs)]
public struct RunEventRngs
{
    private RunEventRng _first;
}

[InlineArray(RunLimits.UnlockEpochWords)]
public struct RunUnlockEpochWords
{
    private ulong _first;
}

[InlineArray(RunLimits.UnlockEncounterWords)]
public struct RunUnlockEncounterWords
{
    private ulong _first;
}

[StructLayout(LayoutKind.Sequential)]
public struct RunUnlockState
{
    public RunUnlockEpochWords Epochs;
    public RunUnlockEncounterWords EncountersSeen;
    public int NumberOfRuns;
}

[StructLayout(LayoutKind.Sequential)]
public struct RunVisited
{
    public byte CoordCount;
    public RunVisitedCoords Coords;
    public RunVisitedEventWords Events;
}

[InlineArray(RunLimits.VisitedCoords)]
public struct RunVisitedCoords
{
    private RunCoord _first;
}

[InlineArray(RunLimits.VisitedEventWords)]
public struct RunVisitedEventWords
{
    private ulong _first;
}

/// <summary>One native post-combat reward type.</summary>
public enum RunRewardType : byte
{
    None = 0,
    Gold = 1,
    Potion = 2,
    Card = 3,
    Relic = 4,
    SpecialCard = 5,
    CardRemoval = 6,
}

/// <summary>What the reward screen is currently asking for.</summary>
public enum RunRewardStage : byte
{
    /// <summary>No reward screen is open.</summary>
    None = 0,
    /// <summary>The reward list is shown; every remaining reward plus "leave" is legal.</summary>
    List = 1,
    /// <summary>A card reward's options are shown.</summary>
    Card = 2,
    /// <summary>A potion reward cannot be held because the belt is full; discard or skip it.</summary>
    PotionDiscard = 3,
}

[Flags]
public enum RunRewardFlags : byte
{
    None = 0,
    /// <summary>Native <c>GoldReward</c> constructed with <c>wasGoldStolenBack</c>.</summary>
    GoldStolenBack = 1 << 0,
    /// <summary>Native <c>CardReward.CanReroll</c> (Driftwood).</summary>
    CardCanReroll = 1 << 1,
    /// <summary>The reward has been populated (gold amount rolled, model/options chosen).</summary>
    Populated = 1 << 2,
    /// <summary>Card options were fixed by native code (tutorial) rather than rolled.</summary>
    ManualCards = 1 << 3,
    /// <summary>The card reward was created by <c>CardCreationOptions.ForRoom</c> for a combat
    /// room (native <c>IsFromCombat</c>); Lasting Candy only applies to these.</summary>
    CardFromCombat = 1 << 4,
}

/// <summary>
/// One pending reward. Card and special-card options live in <see cref="RunRewards.Choices"/> at
/// <see cref="OptionStart"/>..<see cref="OptionCount"/>; a potion/relic names its model in
/// <see cref="ModelId"/>; gold carries its amount.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunReward
{
    public RunRewardType Type;
    public byte OptionStart;
    public byte OptionCount;
    public RunRewardFlags Flags;
    public ushort ModelId;
    /// <summary>Options a card reward requests when it is generated (<c>CardReward.OptionCount</c>).</summary>
    public byte CardCount;
    /// <summary>Native <c>RoomRarityOdds</c> used by the card reward's creation options.</summary>
    public byte RaritySource;
    public int Amount;
    /// <summary>Unpopulated gold maximum; equal to <see cref="Amount"/> once populated.</summary>
    public int AmountMax;
}

[InlineArray(RunLimits.Rewards)]
public struct RunRewardArray
{
    private RunReward _first;
}

[InlineArray(RunLimits.RewardCardOptions)]
public struct RunRewardCardArray
{
    private RunCard _first;
}

/// <summary>
/// The pending post-combat reward set and the current reward-screen stage. Before generation the
/// same item list holds the combat room's <c>ExtraRewards</c> (<see cref="Generated"/> 0); after
/// <c>RunRewards.GenerateForCombat</c> it holds the sorted default+extra set and
/// <see cref="Generated"/> is 1. The native reward screen lives in the UI, so its visible content
/// has to be part of the branchable run state for the decision protocol.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunRewards
{
    public RunRewardStage Stage;
    public byte Count;
    /// <summary>1 once default rewards have been generated; 0 while only combat extras exist.</summary>
    public byte Generated;
    /// <summary>Reward item index whose card options or potion slot choice is open.</summary>
    public byte ChoiceIndex;
    /// <summary>Card option count while <see cref="Stage"/> is <see cref="RunRewardStage.Card"/>.</summary>
    public byte ChoiceCount;
    private byte _padding0;
    private ushort _padding1;
    public RunRewardArray Items;
    public RunRewardCardArray Choices;

    public readonly RunReward Item(int index)
    {
        if ((uint)index >= Count)
            throw new InvalidDataException($"奖励下标 {index} 超出 {Count}。");
        return Items[index];
    }
}

/// <summary>What the merchant inventory is currently asking for.</summary>
public enum RunShopStage : byte
{
    /// <summary>No merchant inventory is open.</summary>
    None = 0,
    /// <summary>The shelf is shown; every stocked, affordable entry and "leave" are legal.</summary>
    Shop = 1,
    /// <summary>The card-removal service opened the removable-card selection.</summary>
    CardRemoval = 2,
}

[Flags]
public enum RunShopEntryFlags : byte
{
    None = 0,
    /// <summary>Native <c>MerchantEntry.IsStocked</c>.</summary>
    Stocked = 1 << 0,
    /// <summary>Native <c>MerchantCardEntry.IsOnSale</c>; the stored cost is already halved.</summary>
    OnSale = 1 << 1,
}

/// <summary>
/// One merchant card slot. <see cref="Cost"/> is the native private <c>_cost</c>
/// (already randomized, sale-halved); <see cref="Price"/> is <c>Cost</c> after the merchant-price
/// hooks, i.e. the number the shelf shows.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunShopCard
{
    public RunCard Card;
    public int Cost;
    public int Price;
    public RunShopEntryFlags Flags;
}

/// <summary>One merchant relic or potion slot; <see cref="Cost"/> and <see cref="Price"/> as in
/// <see cref="RunShopCard"/>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunShopModel
{
    public ushort ModelId;
    private ushort _padding;
    public int Cost;
    public int Price;
    public RunShopEntryFlags Flags;
}

/// <summary>Native <c>MerchantCardRemovalEntry</c>: cost and the one-shot used flag.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunShopRemoval
{
    public int Cost;
    public int Price;
    public byte Used;
    private byte _padding0;
    private ushort _padding1;
}

[InlineArray(RunLimits.ShopCharacterCards)]
public struct RunShopCharacterCardArray
{
    private RunShopCard _first;
}

[InlineArray(RunLimits.ShopColorlessCards)]
public struct RunShopColorlessCardArray
{
    private RunShopCard _first;
}

[InlineArray(RunLimits.ShopRelics)]
public struct RunShopRelicArray
{
    private RunShopModel _first;
}

[InlineArray(RunLimits.ShopPotions)]
public struct RunShopPotionArray
{
    private RunShopModel _first;
}

/// <summary>
/// The normal merchant's fixed shelf plus the open decision stage. The inventory exists only while
/// <see cref="Stage"/> is not <see cref="RunShopStage.None"/>; the native card/relic/potion lists
/// have fixed upper bounds (5 + 2 cards, 3 relics, 3 potions, one removal service).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunShop
{
    public RunShopStage Stage;
    private byte _padding0;
    private ushort _padding1;
    public RunShopRemoval Removal;
    public RunShopCharacterCardArray CharacterCards;
    public RunShopColorlessCardArray ColorlessCards;
    public RunShopRelicArray Relics;
    public RunShopPotionArray Potions;
}

/// <summary>The player's persistent run-scoped numbers.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunPlayer
{
    public ushort Character;
    public int CurrentHp;
    public int MaxHp;
    public int MaxEnergy;
    public int Gold;
    public int BaseOrbSlots;
    public RunPotionArray Potions;
}
