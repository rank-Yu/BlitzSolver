using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Stable reason codes shared by the combat rules compiler and the run-layer rules builder. The
/// strings are the <c>ImportReason</c> codes; keeping the ones the builder can raise here makes the
/// Core rejection independent of the native import layer while staying byte-for-byte the same code.
/// </summary>
public static class ContentReason
{
    public const string UnsupportedCard = "unsupported-card";
    public const string UnsupportedPower = "unsupported-power";
    public const string UnsupportedRelic = "unsupported-relic";
    public const string UnsupportedPotion = "unsupported-potion";
    public const string UnsupportedMonster = "unsupported-monster";
    public const string UnsupportedEnchantment = "unsupported-enchantment";
    public const string UnsupportedEncounter = "unsupported-encounter";
    public const string UnsupportedEvent = "unsupported-event";
    public const string UnsupportedRoom = "unsupported-room";
    public const string UnsupportedClosure = "unsupported-closure";
    public const string Capacity = "capacity";
}

/// <summary>One table entry the exporter could not build, with the stable reason and detail.</summary>
public sealed class ContentUnsupported
{
    public required string Reason { get; init; }

    public required string Detail { get; init; }
}

/// <summary>
/// Content the table marked unsupported, or a root reference the table does not describe. The
/// reason uses the same stable code the native rules compiler would have rejected with.
/// </summary>
public sealed class ContentUnavailableException(string reason, string detail)
    : UnsupportedException($"内容表拒绝 reason={reason}：{detail}")
{
    public string Reason { get; } = reason;

    public string Detail { get; } = detail;
}

/// <summary>
/// One canonical card id with the stats of every upgrade level. <see cref="Levels"/> is indexed by
/// upgrade level and always starts at level 0.
/// </summary>
public sealed class ContentCard
{
    public required CardStats[] Levels { get; init; }

    /// <summary>
    /// Saved properties of a freshly created mutable card at each upgrade level (native defaults
    /// such as Genetic Algorithm's growth counters).
    /// </summary>
    public required RunPropertyBag[] DefaultProperties { get; init; }
}

/// <summary>
/// One ordered candidate of a card pool, with the filter facts and the epochs that must be
/// revealed. <see cref="CardId"/> is <see cref="NoCard"/> when the native card has no Blitz
/// definition; the builder rejects with <c>unsupported-card</c> only when that candidate survives
/// the unlock and pool filters.
/// </summary>
public sealed class ContentPoolEntry
{
    public const ushort NoCard = ushort.MaxValue;

    public required ushort CardId { get; init; }

    public required string NativeId { get; init; }

    /// <summary>Native <c>CardModel.Type</c>.</summary>
    public required CardType Type { get; init; }

    /// <summary>Native <c>CardModel.Rarity</c>.</summary>
    public required CardRarity Rarity { get; init; }

    public required bool CanBeGeneratedInCombat { get; init; }

    public required bool MultiplayerOnly { get; init; }

    /// <summary>Native <c>CardEnergyCost.Canonical</c>.</summary>
    public required int EnergyCost { get; init; }

    public required bool CostsX { get; init; }

    public required bool Ethereal { get; init; }

    public required ulong RequiredEpochs { get; init; }
}

/// <summary>One canonical enchantment id: its native id and the hook bits the rules compiler reads.</summary>
public sealed class ContentEnchantment
{
    public required string NativeId { get; init; }

    public required HookMaskExt HooksExt { get; init; }
}

/// <summary>One ordered candidate of a potion pool.</summary>
public sealed class ContentPotionEntry
{
    public const ushort NoPotion = ushort.MaxValue;

    public required ushort PotionId { get; init; }

    public required string NativeId { get; init; }

    public required bool CanBeGeneratedInCombat { get; init; }

    public required ulong RequiredEpochs { get; init; }
}

/// <summary>Ordered card-pool candidates before the per-run unlock and constraint filters.</summary>
public sealed class ContentCardPool
{
    public required ContentPoolEntry[] Cards { get; init; }
}

/// <summary>One playable character's card and potion pools, plus its new-run inventory.</summary>
public sealed class ContentCharacter
{
    /// <summary>Dense id from <see cref="RunCatalog"/>.</summary>
    public required ushort Id { get; init; }

    public required string NativeId { get; init; }

    /// <summary>Epoch mask that must be revealed for <see cref="CardPool"/> to be available.</summary>
    public required ulong UnlockEpochs { get; init; }

    public required ContentCardPool CardPool { get; init; }

    /// <summary>Native character potion pool order.</summary>
    public required ContentPotionEntry[] PotionPool { get; init; }

    /// <summary>Epoch mask that must be revealed for <see cref="PotionPool"/> to be available.</summary>
    public required ulong PotionUnlockEpochs { get; init; }

    /// <summary>Starting deck, relics, potions and base stats of <c>Player.CreateForNewRun</c>.</summary>
    public required ContentStartingInventory Starting { get; init; }

    /// <summary>Native character relic pool order (without the shared pool).</summary>
    public required ContentRelicPoolEntry[] RelicPool { get; init; }
}

/// <summary>One monster generated by an encounter, with the slot native setup placed it in.</summary>
public sealed class ContentEncounterMonster
{
    public required ushort MonsterId { get; init; }

    /// <summary>Native slot name, or null when the encounter did not name a slot.</summary>
    public required string? Slot { get; init; }

    /// <summary>
    /// Rules of the mutable model the encounter generates at each ascension level, before any
    /// root-specific power amount, AI register or conditional selection is overlaid.
    /// </summary>
    public required MonsterRules[] RulesByAscension { get; init; }

    /// <summary>Opening <c>AfterAddedToRoom</c> effects recorded from the native mutable model.</summary>
    public required CombatStartRecord?[] CombatStartByAscension { get; init; }

    public MonsterRules Rules(int ascension)
        => RulesByAscension[Math.Clamp(ascension, 0, ContentTable.AscensionCount - 1)];

    public CombatStartRecord? CombatStart(int ascension)
        => CombatStartByAscension[Math.Clamp(ascension, 0, ContentTable.AscensionCount - 1)];
}

/// <summary>
/// One native encounter: its ordered slot names and the monsters its setup generated. The setup is
/// computed with a deterministic scratch run so a per-encounter random starting move is
/// reproducible; <see cref="RulesBuilder"/> overrides the per-root starting node from the battle
/// root facts.
/// </summary>
public sealed class ContentEncounter
{
    public required string NativeId { get; init; }

    public required string[] Slots { get; init; }

    public required ContentEncounterMonster[] Monsters { get; init; }

    /// <summary>Native <c>EncounterModel.RoomType</c>.</summary>
    public required RunRoomType RoomType { get; init; }

    /// <summary>Native <c>EncounterModel.IsWeak</c>.</summary>
    public required bool IsWeak { get; init; }

    /// <summary>Native <c>EncounterModel.Tags</c> as a bit per <c>EncounterTag</c> value.</summary>
    public required ulong TagMask { get; init; }

    /// <summary>Native <c>EncounterModel.ShouldGiveRewards</c>.</summary>
    public required bool ShouldGiveRewards { get; init; }

    /// <summary>Native <c>MinGoldReward</c> at each ascension level.</summary>
    public required int[] MinGoldByAscension { get; init; }

    /// <summary>Native <c>MaxGoldReward</c> at each ascension level.</summary>
    public required int[] MaxGoldByAscension { get; init; }

    /// <summary>
    /// True when the encounter overrides <c>CalculateGoldProportion</c> with state the terminal
    /// battle does not expose (GremlinMerc). Post-combat reward generation rejects these.
    /// </summary>
    public required bool CustomGoldProportion { get; init; }

    public int MinGold(int ascension)
        => MinGoldByAscension[Math.Clamp(ascension, 0, ContentTable.AscensionCount - 1)];

    public int MaxGold(int ascension)
        => MaxGoldByAscension[Math.Clamp(ascension, 0, ContentTable.AscensionCount - 1)];

    /// <summary>Stable reason when the encounter cannot be represented, otherwise null.</summary>
    public string? UnsupportedReason { get; init; }

    public string? UnsupportedDetail { get; init; }
}

/// <summary>
/// Process-local content exported once from the canonical native models. It holds exactly the data
/// <c>RulesCompiler</c> reads from content: card stats for every upgrade level, power/relic/potion
/// variables, enchantment hook bits, canonical and encounter-generated monster rules, encounter
/// slots, and the unlocked candidate pools of every character. Root state (cards, relics, powers, AI registers, conditional
/// selections, potions) is not part of this table.
/// <para>
/// The table is built from the game files loaded by this process; it is never written to disk and
/// carries no version check. Unknown content is represented by a null entry or an explicit
/// unsupported reason and is rejected by <see cref="RulesBuilder"/> with the native rejection code.
/// </para>
/// </summary>
public sealed class ContentTable : ICombatStartContent
{
    public const ushort NoEncounter = ushort.MaxValue;

    /// <summary>Native ascension levels 0..10; monster content varies by level.</summary>
    public const int AscensionCount = 11;

    public required ContentCard?[] Cards { get; init; }

    public required ContentUnsupported?[] CardUnsupported { get; init; }

    public required decimal[]?[] PowerVars { get; init; }

    public required ContentUnsupported?[] PowerUnsupported { get; init; }

    public required bool[] PowerDeathHistoryRisk { get; init; }

    public required PotionStats?[] Potions { get; init; }

    public required ContentUnsupported?[] PotionUnsupported { get; init; }

    /// <summary>Canonical enchantments; the rules compiler reads the two hook bits used by shuffles.</summary>
    public required ContentEnchantment?[] Enchantments { get; init; }

    public required ContentUnsupported?[] EnchantmentUnsupported { get; init; }

    /// <summary>Native <c>PotionModel.CanBeGeneratedInCombat</c>, indexed by potion id.</summary>
    public required bool[] PotionGeneratableInCombat { get; init; }

    public required decimal[]?[] RelicVars { get; init; }

    /// <summary>All canonical <c>DynamicVars</c> of each relic, by native variable name.</summary>
    public required IReadOnlyDictionary<string, decimal>?[] RelicStartVars { get; init; }

    public required ContentUnsupported?[] RelicUnsupported { get; init; }

    /// <summary>
    /// Canonical spawn variant of every monster id at each ascension level, as the spawn closure
    /// compiles it; the outer index is the ascension level.
    /// </summary>
    public required MonsterRules?[][] SpawnMonsters { get; init; }

    /// <summary>Reason a spawn variant is absent, or null when it is present.</summary>
    public required ContentUnsupported?[] MonsterUnsupported { get; init; }

    /// <summary>Per-encounter generated monsters, indexed by <see cref="RunCatalog"/> encounter id.</summary>
    public required ContentEncounter?[] Encounters { get; init; }

    public required ContentCharacter?[] Characters { get; init; }

    /// <summary>Character ids in native <c>ModelDb.AllCharacters</c> order (used by Splash).</summary>
    public required ushort[] CharacterOrder { get; init; }

    public required ContentCardPool ColorlessPool { get; init; }

    public required ContentCardPool StatusPool { get; init; }

    public required ContentCardPool CursePool { get; init; }

    public required ContentPotionEntry[] SharedPotionPool { get; init; }

    /// <summary>Native <c>SharedRelicPool.AllRelics</c> order; the shared bag's only source.</summary>
    public required ContentRelicPoolEntry[] SharedRelicPool { get; init; }

    /// <summary>Native <c>ModelDb.AllSharedEvents</c> order.</summary>
    public required ushort[] SharedEvents { get; init; }

    /// <summary>Epochs that must be revealed for an event, indexed by event id.</summary>
    public required ulong[] EventRequiredEpochs { get; init; }

    /// <summary>Native <c>UnlockState.SharedAncients</c> base order (Darv).</summary>
    public required ContentAncientEntry[] SharedAncients { get; init; }

    /// <summary>Acts indexed by <see cref="RunCatalog"/> act id.</summary>
    public required ContentAct?[] Acts { get; init; }

    /// <summary>Native <c>ActModel.GetDefaultList()</c> order.</summary>
    public required ushort[] DefaultActs { get; init; }

    public required ContentMapRules MapRules { get; init; }

    public required ContentOdds BaseOdds { get; init; }

    public required ContentAscensionRules AscensionRules { get; init; }

    public required ContentRewardRules RewardRules { get; init; }

    public required ContentShopRules ShopRules { get; init; }

    public required ContentTutorialRewards TutorialRewards { get; init; }

    /// <summary>Reward-layer facts indexed by relic id.</summary>
    public required ContentRelicRewardFacts?[] RelicRewardFacts { get; init; }

    /// <summary>Reward-hook overrides of every card id.</summary>
    public required ContentRewardHookMask[] CardRewardHooks { get; init; }

    /// <summary>Reward-hook overrides of every enchantment id.</summary>
    public required ContentRewardHookMask[] EnchantmentRewardHooks { get; init; }

    /// <summary>Reward-hook overrides of every potion id.</summary>
    public required ContentRewardHookMask[] PotionRewardHooks { get; init; }

    /// <summary>Merchant-flow hook overrides of every card id.</summary>
    public required ContentShopHookMask[] CardShopHooks { get; init; }

    /// <summary>Merchant-flow hook overrides of every enchantment id.</summary>
    public required ContentShopHookMask[] EnchantmentShopHooks { get; init; }

    /// <summary>Merchant-flow hook overrides of every potion id.</summary>
    public required ContentShopHookMask[] PotionShopHooks { get; init; }

    /// <summary>Epoch whose reveal sets <c>ExtraRunFields.StartedWithNeow</c>.</summary>
    public required ushort NeowEpoch { get; init; }

    /// <summary>Relics that override a map-generation hook (FurCoat, GoldenCompass).</summary>
    public required ushort[] MapHookRelics { get; init; }

    /// <summary>Cards that override a map-generation hook (SpoilsMap).</summary>
    public required ushort[] MapHookCards { get; init; }

    /// <summary>Relics that override <c>ShouldAllowFreeTravel</c> (WingedBoots).</summary>
    public required ushort[] FreeTravelRelics { get; init; }

    /// <summary>Relics that override an unknown-map-point hook (GoldenCompass, JuzuBracelet).</summary>
    public required ushort[] UnknownPointHookRelics { get; init; }

    /// <summary>Cards that override an unknown-map-point hook (LanternKey).</summary>
    public required ushort[] UnknownPointHookCards { get; init; }

    /// <summary>Cards that override <c>ModifyNextEvent</c> (LanternKey).</summary>
    public required ushort[] NextEventHookCards { get; init; }

    /// <summary>Events that override <c>EventModel.IsAllowed</c>, indexed by event id.</summary>
    public required bool[] EventAllowedOverride { get; init; }

    /// <summary>The act's boss encounters in <c>AllBossEncounters</c> order.</summary>
    public ushort[] BossEncounters(ContentAct act)
    {
        ushort[] result = new ushort[act.Encounters.Length];
        int count = 0;
        for (int index = 0; index < act.Encounters.Length; index++)
        {
            ushort id = act.Encounters[index];
            if (Encounter(id).RoomType == RunRoomType.Boss)
                result[count++] = id;
        }
        Array.Resize(ref result, count);
        return result;
    }

    public ContentAct Act(ushort id)
    {
        if (id < Acts.Length && Acts[id] is { } act)
            return act;
        string name = id < RunCatalog.ActCount ? RunCatalog.ActName(id) : id.ToString();
        throw new ContentUnavailableException(ContentReason.UnsupportedClosure,
            $"内容表没有幕 {name}。");
    }

    public decimal RelicVar(ushort relicId, string name)
    {
        if (relicId < RelicStartVars.Length && RelicStartVars[relicId] is { } vars
            && vars.TryGetValue(name, out decimal value))
        {
            return value;
        }
        string relicName = relicId < Registry.RelicCount ? Registry.Relic(relicId).NativeId : relicId.ToString();
        throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
            $"内容表没有遗物 {relicName} 的开战变量 {name}。");
    }

    public ContentCharacter Character(ushort id)
    {
        if (id < Characters.Length && Characters[id] is { } character)
            return character;
        string name = id < RunCatalog.CharacterCount ? RunCatalog.CharacterName(id) : id.ToString();
        throw new ContentUnavailableException(ContentReason.UnsupportedClosure,
            $"内容表没有角色 {name}。");
    }

    public ContentCard Card(ushort id)
    {
        if (id < Cards.Length && Cards[id] is { } card)
            return card;
        ContentUnsupported? unsupported = id < CardUnsupported.Length ? CardUnsupported[id] : null;
        string name = id < Registry.CardCount ? Registry.Card(id).NativeId : id.ToString();
        throw new ContentUnavailableException(unsupported?.Reason ?? ContentReason.UnsupportedCard,
            unsupported?.Detail ?? $"内容表没有卡牌 {name}。");
    }

    public CardStats Card(ushort id, int upgrade)
    {
        ContentCard card = Card(id);
        if ((uint)upgrade >= (uint)card.Levels.Length || card.Levels[upgrade] is not { } stats)
        {
            ContentUnsupported? unsupported = id < CardUnsupported.Length ? CardUnsupported[id] : null;
            string name = id < Registry.CardCount ? Registry.Card(id).NativeId : id.ToString();
            throw new ContentUnavailableException(unsupported?.Reason ?? ContentReason.UnsupportedCard,
                unsupported?.Detail ?? $"内容表没有卡牌 {name}+{upgrade}。");
        }
        return stats;
    }

    public PotionStats Potion(ushort id)
    {
        if (id < Potions.Length && Potions[id] is { } potion)
            return potion;
        ContentUnsupported? unsupported = id < PotionUnsupported.Length ? PotionUnsupported[id] : null;
        string name = id < Registry.PotionCount ? Registry.Potion(id).NativeId : id.ToString();
        throw new ContentUnavailableException(unsupported?.Reason ?? ContentReason.UnsupportedPotion,
            unsupported?.Detail ?? $"内容表没有药水 {name}。");
    }

    public ContentEnchantment Enchantment(ushort id)
    {
        if (id < Enchantments.Length && Enchantments[id] is { } enchantment)
            return enchantment;
        ContentUnsupported? unsupported = id < EnchantmentUnsupported.Length
            ? EnchantmentUnsupported[id]
            : null;
        string name = id < Registry.EnchantmentCount
            ? Registry.Enchantment(id).NativeId
            : id.ToString();
        throw new ContentUnavailableException(
            unsupported?.Reason ?? ContentReason.UnsupportedEnchantment,
            unsupported?.Detail ?? $"内容表没有附魔 {name}。");
    }

    public decimal[] RelicVarsOf(ushort id)
    {
        if (id < RelicVars.Length && RelicVars[id] is { } vars)
            return vars;
        ContentUnsupported? unsupported = id < RelicUnsupported.Length ? RelicUnsupported[id] : null;
        string name = id < Registry.RelicCount ? Registry.Relic(id).NativeId : id.ToString();
        throw new ContentUnavailableException(unsupported?.Reason ?? ContentReason.UnsupportedRelic,
            unsupported?.Detail ?? $"内容表没有遗物变量 {name}。");
    }

    public decimal[] PowerVarsOf(ushort id)
    {
        if (id < PowerVars.Length && PowerVars[id] is { } vars)
            return vars;
        ContentUnsupported? unsupported = id < PowerUnsupported.Length ? PowerUnsupported[id] : null;
        string name = id < Registry.PowerCount ? Registry.Power(id).NativeId : id.ToString();
        throw new ContentUnavailableException(unsupported?.Reason ?? ContentReason.UnsupportedPower,
            unsupported?.Detail ?? $"内容表没有 Power 变量 {name}。");
    }

    public MonsterRules SpawnMonster(ushort id, int ascension)
    {
        int level = Math.Clamp(ascension, 0, AscensionCount - 1);
        if (id < SpawnMonsters[level].Length && SpawnMonsters[level][id] is { } rules)
            return rules;
        ContentUnsupported? unsupported = id < MonsterUnsupported.Length ? MonsterUnsupported[id] : null;
        string name = id < Registry.MonsterCount ? Registry.Monster(id).NativeId : id.ToString();
        throw new ContentUnavailableException(unsupported?.Reason ?? ContentReason.UnsupportedMonster,
            unsupported?.Detail ?? $"内容表没有怪物 {name}。");
    }

    public ContentEncounter Encounter(ushort id)
    {
        if (id >= Encounters.Length || Encounters[id] is not { } encounter)
        {
            string name = id < RunCatalog.EncounterCount
                ? RunCatalog.EncounterName(id)
                : id.ToString();
            throw new ContentUnavailableException(ContentReason.UnsupportedEncounter,
                $"内容表没有遭遇 {name}。");
        }
        if (encounter.UnsupportedReason is { } reason)
            throw new ContentUnavailableException(reason, encounter.UnsupportedDetail ?? encounter.NativeId);
        return encounter;
    }
}
