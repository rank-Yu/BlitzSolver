using System.Runtime.InteropServices;
using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// One candidate of a native relic pool in native enumeration order. <see cref="RequiredEpochs"/>
/// mirrors how the pool's <c>GetUnlockedRelics</c> removes entries: a bit is set when removing that
/// epoch from the unlock state removes this relic.
/// </summary>
public sealed class ContentRelicPoolEntry
{
    public required ushort RelicId { get; init; }

    public required string NativeId { get; init; }

    public required RunRelicRarity Rarity { get; init; }

    public required ulong RequiredEpochs { get; init; }
}

/// <summary>One act-local ancient candidate and the epochs that must be revealed for it.</summary>
public sealed class ContentAncientEntry
{
    public required ushort EventId { get; init; }

    public required string NativeId { get; init; }

    public required ulong RequiredEpochs { get; init; }
}

/// <summary>One starting relic of a character, with the native default display amount.</summary>
public sealed class ContentStartingRelic
{
    public required ushort Id { get; init; }

    public required string NativeId { get; init; }

    public required int DisplayAmount { get; init; }
}

/// <summary>The inventory <c>Player.CreateForNewRun</c> builds from a character's canonical models.</summary>
public sealed class ContentStartingInventory
{
    public required ushort[] Deck { get; init; }

    public required ContentStartingRelic[] Relics { get; init; }

    public required ushort[] Potions { get; init; }

    public required int Hp { get; init; }

    public required int Gold { get; init; }

    public required int MaxEnergy { get; init; }

    public required int OrbSlots { get; init; }

    /// <summary>Native <c>Player.CreateForNewRun</c> passes the literal 3 potion slots.</summary>
    public required int PotionSlots { get; init; }
}

/// <summary>Kind of one draw in an act's map-point-count recipe.</summary>
public enum ContentDrawKind : byte
{
    /// <summary>Native <c>Rng.NextInt(minInclusive, maxExclusive)</c>.</summary>
    Uniform = 0,

    /// <summary>Native <c>Rng.NextGaussianInt(mean, stdDev, min, max)</c>; bounds inclusive.</summary>
    Gaussian = 1,
}

/// <summary>
/// One draw of an act's native <c>GetMapPointTypes</c>, exported from the game by probing that
/// method. <see cref="Offset"/> is added after the draw, matching acts that subtract one.
/// </summary>
public struct ContentCountDraw
{
    public ContentDrawKind Kind;
    public int Mean;
    public int StdDev;

    /// <summary>Uniform: inclusive minimum. Gaussian: inclusive minimum.</summary>
    public int Min;

    /// <summary>Uniform: exclusive maximum. Gaussian: inclusive maximum.</summary>
    public int Max;

    public int Offset;

    public int Draw(ref RngState rng) => Kind switch
    {
        ContentDrawKind.Uniform => rng.NextInt(Min, Max) + Offset,
        ContentDrawKind.Gaussian => RunRandom.NextGaussianInt(ref rng, Mean, StdDev, Min, Max) + Offset,
        _ => throw new UnsupportedException($"未知的幕地图点数量抽取类型 {(byte)Kind}。"),
    };
}

/// <summary>Which pre-generated room list of one act a tutorial swap targets.</summary>
public enum ContentRoomList : byte
{
    Events = 0,
    Normal = 1,
    Elite = 2,
}

/// <summary>
/// One native <c>RoomSet.SwapToOrCreateAtIndex</c> call of
/// <c>ActModel.ApplyActDiscoveryOrderModifications</c>: find the model in the list and swap it with
/// <paramref name="Index"/>, or place it there when absent. The order of the calls within a list is
/// preserved because swap chains are order-dependent.
/// </summary>
public struct ContentRoomSwap
{
    public ContentRoomList List;
    public byte Index;
    public ushort ModelId;
}

/// <summary>One act's pre-generated pool and map parameters, in native order.</summary>
public sealed class ContentAct
{
    public required ushort Id { get; init; }

    public required string NativeId { get; init; }

    /// <summary>Native <c>ActModel.Index</c> (the act slot this act can appear in).</summary>
    public required int Index { get; init; }

    public required bool IsDefault { get; init; }

    /// <summary>Epochs required by native <c>ActModel.IsUnlocked</c>.</summary>
    public required ulong UnlockEpochs { get; init; }

    /// <summary>Native <c>ActModel.BaseNumberOfRooms</c>.</summary>
    public required int BaseRooms { get; init; }

    /// <summary>Native <c>ActModel.NumberOfWeakEncounters</c>.</summary>
    public required int WeakEncounters { get; init; }

    /// <summary>Native <c>ActModel.AllEvents</c> order (without shared events).</summary>
    public required ushort[] Events { get; init; }

    /// <summary>Native <c>ActModel.AllEncounters</c> order.</summary>
    public required ushort[] Encounters { get; init; }

    /// <summary>Native <c>ActModel.GetUnlockedAncients</c> base order.</summary>
    public required ContentAncientEntry[] Ancients { get; init; }

    /// <summary>Native <c>ActModel.BossDiscoveryOrder</c>.</summary>
    public required ushort[] BossDiscoveryOrder { get; init; }

    /// <summary>First native draw of <c>GetMapPointTypes</c> (rest sites).</summary>
    public required ContentCountDraw RestDraw { get; init; }

    /// <summary>Second native draw of <c>GetMapPointTypes</c> (unknown points).</summary>
    public required ContentCountDraw UnknownDraw { get; init; }

    /// <summary>Native <c>ApplyActDiscoveryOrderModifications</c> calls, in call order.</summary>
    public required ContentRoomSwap[] TutorialSwaps { get; init; }

    /// <summary>True when the swaps only run while <c>UnlockState.NumberOfRuns == 0</c>.</summary>
    public required bool TutorialSwapsFirstRunOnly { get; init; }

    public bool IsUnlocked(in RunUnlockState unlock)
    {
        for (int epoch = 0; epoch < RunCatalog.EpochCount; epoch++)
        {
            if ((UnlockEpochs & (1UL << epoch)) == 0)
                continue;
            if ((unlock.Epochs[epoch >> 6] & (1UL << (epoch & 63))) == 0)
                return false;
        }
        return true;
    }
}

/// <summary>
/// Native <c>StandardActMap</c> geometry and the restriction sets it uses while assigning point
/// types. Every field is read from the game; the point-type bit positions are
/// <see cref="RunMapPointType"/>.
/// </summary>
public sealed class ContentMapRules
{
    public required int Width { get; init; }

    public required int StartPaths { get; init; }

    public required int MaxElites { get; init; }

    /// <summary>Treasure row index counted from the end: <c>GetRowCount() - TreasureRowFromEnd</c>.</summary>
    public required int TreasureRowFromEnd { get; init; }

    /// <summary>Rows below this index reject <see cref="LowerRestrictions"/>.</summary>
    public required int LowerRestrictionRow { get; init; }

    /// <summary>Rows at or above <c>GetRowCount() - UpperRestrictionFromEnd</c> reject <see cref="UpperRestrictions"/>.</summary>
    public required int UpperRestrictionFromEnd { get; init; }

    /// <summary>Iterations of <c>AssignRemainingTypesToRandomPoints</c> (3 in the native method).</summary>
    public required int TypeAssignIterations { get; init; }

    /// <summary>Rounds of prune+repair (3 in the native <c>PruneAndRepair</c>).</summary>
    public required int PruneRepairIterations { get; init; }

    /// <summary>Guard on the native <c>PruneDuplicateSegments</c> loop (50).</summary>
    public required int PruneIterationLimit { get; init; }

    public required uint ParentRestrictions { get; init; }
    public required uint ChildRestrictions { get; init; }
    public required uint SiblingRestrictions { get; init; }
    public required uint LowerRestrictions { get; init; }
    public required uint UpperRestrictions { get; init; }

    /// <summary>Native <c>MapPointTypeCounts.NumOfElites</c> at ascension 0..10.</summary>
    public required int[] ElitesByAscension { get; init; }

    /// <summary>Native <c>MapPointTypeCounts.NumOfShops</c>.</summary>
    public required int Shops { get; init; }

    public bool Restricted(uint mask, RunMapPointType type) => (mask & (1U << (int)type)) != 0;
}

/// <summary>
/// The initial current values of the run/player odds sets, read from the native constructors.
/// </summary>
public sealed class ContentOdds
{
    public required float Monster { get; init; }
    public required float Elite { get; init; }
    public required float Treasure { get; init; }
    public required float Shop { get; init; }
    public required float CardRarity { get; init; }
    public required float PotionReward { get; init; }
}

/// <summary>
/// The ascension levels that change run creation, read from the native enum names and from
/// <c>AscensionManager.ApplyEffectsTo</c>.
/// </summary>
public sealed class ContentAscensionRules
{
    public required int MaxLevel { get; init; }

    public required int SwarmingElitesLevel { get; init; }

    public required int TightBeltLevel { get; init; }

    public required int AscendersBaneLevel { get; init; }

    public required ushort AscendersBaneCardId { get; init; }

    public required int DoubleBossLevel { get; init; }

    public required int PovertyLevel { get; init; }

    public required int ScarcityLevel { get; init; }

    public bool AtLeast(in RunState state, int level) => state.Ascension >= level;
}

/// <summary>Native base rare/uncommon odds of one <c>CardRarityOddsType</c>.</summary>
public sealed class ContentCardRarityOdds
{
    public required float Rare { get; init; }

    public required float Uncommon { get; init; }
}

/// <summary>
/// Post-combat reward constants read from the native odds/factory code. The two ascension branches
/// are exported separately because <c>Scarcity</c> changes the card rarity and upgrade numbers.
/// </summary>
public sealed class ContentRewardRules
{
    public required float PotionBaseOdds { get; init; }

    public required float PotionPityStep { get; init; }

    public required float PotionEliteBonus { get; init; }

    public required float PotionEliteBonusScale { get; init; }

    public required float RarityOffsetBase { get; init; }

    public required float RarityOffsetMax { get; init; }

    public required ContentCardRarityOdds Regular { get; init; }

    public required ContentCardRarityOdds Elite { get; init; }

    public required ContentCardRarityOdds Boss { get; init; }

    public required ContentCardRarityOdds ScarcityRegular { get; init; }

    public required ContentCardRarityOdds ScarcityElite { get; init; }

    public required ContentCardRarityOdds ScarcityBoss { get; init; }

    public required float RarityGrowth { get; init; }

    public required float ScarcityRarityGrowth { get; init; }

    public required float UpgradeScaling { get; init; }

    public required float ScarcityUpgradeScaling { get; init; }

    /// <summary><c>RelicFactory.RollRarity</c>: roll &lt; this is Common.</summary>
    public required float RelicCommonThreshold { get; init; }

    /// <summary><c>RelicFactory.RollRarity</c>: roll &lt; this is Uncommon, otherwise Rare.</summary>
    public required float RelicUncommonThreshold { get; init; }

    /// <summary><c>PotionFactory</c>: roll &lt;= this is Rare.</summary>
    public required float PotionRareThreshold { get; init; }

    /// <summary><c>PotionFactory</c>: roll &lt;= this is Uncommon, otherwise Common.</summary>
    public required float PotionUncommonThreshold { get; init; }

    public ContentCardRarityOdds Odds(RoomRarityOdds kind, bool scarcity) => kind switch
    {
        RoomRarityOdds.Regular => scarcity ? ScarcityRegular : Regular,
        RoomRarityOdds.Elite => scarcity ? ScarcityElite : Elite,
        RoomRarityOdds.Boss => scarcity ? ScarcityBoss : Boss,
        _ => throw new UnsupportedException($"未知的卡牌稀有度来源 {(byte)kind}。"),
    };
}

/// <summary>Which native <c>CardRarityOddsType</c> a combat room uses.</summary>
public enum RoomRarityOdds : byte
{
    Regular = 0,
    Elite = 1,
    Boss = 2,
}

/// <summary>One tutorial monster/elite/boss card offer, with its fixed potion (if any).</summary>
public sealed class ContentTutorialCardOffer
{
    public const ushort NoPotion = ushort.MaxValue;

    public required ushort[] Cards { get; init; }

    public required ushort Potion { get; init; }
}

/// <summary>
/// First-run Ironclad tutorial rewards (<c>RewardsSet.TryGenerateTutorialRewards</c>). The fixed
/// gold range is the native <c>GoldReward</c> default; elite/boss gold uses the encounter values.
/// </summary>
public sealed class ContentTutorialRewards
{
    public required int MonsterGoldMin { get; init; }

    public required int MonsterGoldMax { get; init; }

    /// <summary>Monster-room offers indexed by the monster-room count minus one (0..6).</summary>
    public required ContentTutorialCardOffer[] Monsters { get; init; }

    public required ContentTutorialCardOffer EliteFirst { get; init; }

    public required ContentTutorialCardOffer EliteSecond { get; init; }

    public required ContentTutorialCardOffer BossFirst { get; init; }

    public required ushort EliteFirstRelic { get; init; }

    public required ushort EliteSecondRelic { get; init; }
}

/// <summary>Native <c>RelicModel.IsAllowed</c> variants that affect the grab bag.</summary>
public enum ContentRelicAllowedKind : byte
{
    Always = 0,
    BeforeAct3TreasureChest = 1,
    SinglePlayerOnly = 2,
    NotFirstRunIroncladBeforeAct3 = 3,
    Never = 4,
    /// <summary>The relic overrides <c>IsAllowed</c> but is not in the exporter's mapping.</summary>
    Unsupported = 5,
}

/// <summary>Whether a relic can be added by a reward without running native <c>AfterObtained</c>.</summary>
public enum ContentRelicObtainKind : byte
{
    /// <summary>No <c>AfterObtained</c> override; the reward only adds the relic.</summary>
    SimpleAdd = 0,
    /// <summary>An <c>AfterObtained</c> override the run simulator implements explicitly.</summary>
    Implemented = 1,
    /// <summary>An <c>AfterObtained</c> override that is not represented; taking it is rejected.</summary>
    Unsupported = 2,
}

/// <summary>
/// Per-relic facts the reward layer needs. <see cref="InitialDisplayAmount"/> is the native
/// <c>DisplayAmount</c> of a freshly created mutable relic.
/// </summary>
public sealed class ContentRelicRewardFacts
{
    public required ContentRelicAllowedKind Allowed { get; init; }

    public required bool Stackable { get; init; }

    public required int InitialDisplayAmount { get; init; }

    public required ContentRelicObtainKind Obtain { get; init; }

    /// <summary>Reward-hook overrides of this relic type.</summary>
    public required ContentRewardHookMask RewardHooks { get; init; }

    /// <summary>
    /// Saved properties of a freshly created mutable relic (native default values, including
    /// <c>false</c>/<c>0</c> entries the serializer always writes).
    /// </summary>
    public required RunPropertyBag DefaultProperties { get; init; }
}

/// <summary>
/// Reward-flow <c>AbstractModel</c> hook bits. The content table records, per listener model,
/// which bits the canonical type overrides (<see cref="Overridden"/>) and which of those the Core
/// reward layer implements (<see cref="Implemented"/>). Any other override is unsupported and must
/// reject instead of silently skipping.
/// </summary>
[Flags]
public enum ContentRewardHook : uint
{
    None = 0,
    TryModifyRewards = 1u << 0,
    TryModifyRewardsLate = 1u << 1,
    AfterModifyingRewards = 1u << 2,
    BeforeCombatRewardOffered = 1u << 3,
    ModifyCardRewardCreationOptions = 1u << 4,
    ModifyCardRewardCreationOptionsLate = 1u << 5,
    TryModifyCardRewardOptions = 1u << 6,
    TryModifyCardRewardOptionsLate = 1u << 7,
    AfterModifyingCardRewardOptions = 1u << 8,
    ModifyCardRewardUpgradeOdds = 1u << 9,
    TryModifyCardRewardAlternatives = 1u << 10,
    ShouldAllowSelectingMoreCardRewards = 1u << 11,
    ShouldForcePotionReward = 1u << 12,
    ShouldProcurePotion = 1u << 13,
    AfterPotionProcured = 1u << 14,
    AfterPotionDiscarded = 1u << 15,
    AfterRewardTaken = 1u << 16,
    ModifyGoldGained = 1u << 17,
    AfterModifyingGoldGained = 1u << 18,
    AfterGoldGained = 1u << 19,
    ShouldAddToDeck = 1u << 20,
    TryModifyCardBeingAddedToDeck = 1u << 21,
    TryModifyCardBeingAddedToDeckLate = 1u << 22,
    AfterCardChangedPiles = 1u << 23,
    AfterCardChangedPilesLate = 1u << 24,
    AfterCurrentHpChanged = 1u << 25,
    ShouldProceedToNextMapPoint = 1u << 26,
}

/// <summary>Overridden and implemented reward-hook bits of one listener model.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ContentRewardHookMask
{
    public uint Overridden;
    public uint Implemented;

    public readonly uint Unsupported => Overridden & ~Implemented;

    public static ContentRewardHookMask From(
        IEnumerable<(ContentRewardHook Hook, bool Overridden, bool Implemented)> values)
    {
        ContentRewardHookMask mask = default;
        foreach ((ContentRewardHook hook, bool overridden, bool implemented) in values)
        {
            if (overridden)
                mask.Overridden |= (uint)hook;
            if (implemented)
                mask.Implemented |= (uint)hook;
        }
        return mask;
    }
}
