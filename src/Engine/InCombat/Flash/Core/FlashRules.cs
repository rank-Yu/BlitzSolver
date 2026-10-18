namespace CombatSolver.Engine.InCombat.Flash;

public enum FlashCardType : byte
{
    None = 0,
    Attack = 1,
    Skill = 2,
    Power = 3,
    Status = 4,
    Curse = 5,
    Quest = 6,
}

public enum FlashTargetType : byte
{
    None = 0,
    Self = 1,
    AnyEnemy = 2,
    AllEnemies = 3,
    RandomEnemy = 4,
    AnyPlayer = 5,
    AnyAlly = 6,
    AllAllies = 7,
    TargetedNoCreature = 8,
    Osty = 9,
}

public enum FlashCardRarity : byte
{
    None = 0,
    Basic = 1,
    Common = 2,
    Uncommon = 3,
    Rare = 4,
    Ancient = 5,
    Event = 6,
    Token = 7,
    Status = 8,
    Curse = 9,
    Quest = 10,
}

/// <summary>Native <c>CardTag</c> set of a card, one bit per tag.</summary>
[Flags]
public enum FlashCardTag : byte
{
    None = 0,
    Strike = 1 << 0,
    Defend = 1 << 1,
    Minion = 1 << 2,
    OstyAttack = 1 << 3,
    Shiv = 1 << 4,
}

/// <summary>Root-frozen data of one card id at one upgrade level.</summary>
public sealed class FlashCardStats
{
    public required FlashCardType Type { get; init; }
    public required FlashTargetType Target { get; init; }
    public required FlashCardRarity Rarity { get; init; }
    public required int EnergyCost { get; init; }
    public required bool CostsX { get; init; }
    public required int StarCost { get; init; }
    public required bool StarCostsX { get; init; }
    public required FlashKeywords Keywords { get; init; }

    /// <summary>Native <c>MaxUpgradeLevel</c>; the card is upgradable while its level is below it.</summary>
    public required int MaxUpgradeLevel { get; init; }

    /// <summary>Canonical native tags (Strike, Defend, Minion, Osty attack, Shiv).</summary>
    public FlashCardTag Tags { get; init; }

    /// <summary>Base values of the native dynamic variables named by the card definition, in order.</summary>
    public required decimal[] Vars { get; init; }
}

public enum FlashMonsterNodeKind : byte
{
    Move = 0,
    RandomBranch = 1,
    ConditionalBranch = 2,
}

/// <summary>Native <c>MoveRepeatType</c>.</summary>
public enum FlashMoveRepeatType : byte
{
    CanRepeatForever = 0,
    CanRepeatXTimes = 1,
    CannotRepeat = 2,
    UseOnlyOnce = 3,
}

public readonly record struct FlashMonsterAttack(int BaseDamage, int Repeats);

public readonly record struct FlashRandomBranchOption(
    int Target,
    float BaseWeight,
    FlashMoveRepeatType RepeatType,
    int MaxTimes,
    int Cooldown);

/// <summary>One node of a compiled native monster move state machine.</summary>
public sealed class FlashMonsterNode
{
    public required string NativeId { get; init; }
    public required FlashMonsterNodeKind Kind { get; init; }

    /// <summary>Follow-up node of a move node.</summary>
    public int FollowUp { get; init; } = -1;

    /// <summary>
    /// Native <c>MoveState.MustPerformOnceBeforeTransitioning</c>: the machine does not advance while
    /// the round ended without this move being performed (<c>SimulatedCombatState.MonsterAi.cs:122</c>).
    /// The synthetic STUNNED node carries it too; its follow-up lives in <see cref="FlashEnemy.ForcedMove"/>.
    /// </summary>
    public bool MustPerformOnceBeforeTransitioning { get; init; }

    public FlashMonsterAttack[] Attacks { get; init; } = [];
    public FlashRandomBranchOption[] Options { get; init; } = [];

    /// <summary>Root-resolved target of a conditional branch, or -1 when unresolved.</summary>
    public int ConditionalTarget { get; init; } = -1;
}

/// <summary>Root-frozen data of one known enemy: its compiled state machine and static values.</summary>
public sealed class FlashMonsterRules
{
    public required FlashMonsterNode[] Nodes { get; init; }
    public required int InitialNode { get; init; }

    /// <summary>
    /// The synthetic STUNNED node appended after every native state (and the unregistered initial
    /// branch). It has no attacks or effects, is must-perform-once, and its follow-up is the dynamic
    /// <see cref="FlashEnemy.ForcedMove"/> (<c>SimulatedCombatState.MonsterAi.cs:76-86,102-128</c>).
    /// </summary>
    public required int StunnedNode { get; init; }

    /// <summary>Static integers named by the monster definition, in order.</summary>
    public required int[] StaticInts { get; init; }

    public int NodeIndex(string nativeId)
    {
        for (int index = 0; index < Nodes.Length; index++)
        {
            if (Nodes[index].NativeId == nativeId)
                return index;
        }
        throw new FlashUnsupportedException($"怪物状态机没有节点 {nativeId}。");
    }
}

/// <summary>
/// Immutable per-root rule tables shared by every branch of one Flash solve. Values come from
/// native models at root import; content definitions read numbers from here instead of
/// hard-coding them.
/// </summary>
/// <summary>Canonical values of a potion: its target type and the dynamic variables its definition names.</summary>
public sealed class FlashPotionStats
{
    public required FlashTargetType Target { get; init; }

    public required decimal[] Vars { get; init; }

    /// <summary>Value props of each named variable (damage and block variables carry them; others are None).</summary>
    public required FlashValueProps[] VarProps { get; init; }
}

public sealed class FlashRules
{
    private readonly FlashCardStats?[][] _cards;
    private readonly decimal[][] _powerVars;
    private readonly FlashPotionStats?[] _potions;
    private readonly decimal[][] _relicVars;
    private readonly FlashMonsterRules?[] _monsters;

    internal FlashRules(
        FlashCardStats?[][] cards,
        decimal[][] powerVars,
        FlashPotionStats?[] potions,
        decimal[][] relicVars,
        FlashMonsterRules?[] monsters,
        int baseMaxEnergy,
        int playerSlotIndex)
    {
        _cards = cards;
        _powerVars = powerVars;
        _potions = potions;
        _relicVars = relicVars;
        _monsters = monsters;
        BaseMaxEnergy = baseMaxEnergy;
        PlayerSlotIndex = playerSlotIndex;
    }

    public int BaseMaxEnergy { get; }

    public int PlayerSlotIndex { get; }

    /// <summary>
    /// Shuffle sort rank. Card ids are assigned in ordinal order of native ids, and all cards
    /// share one model category, so the id itself is the native <c>ModelId</c> order.
    /// </summary>
    public int CardSortRank(ushort id) => id;

    public FlashCardStats Card(ushort id, int upgrade)
    {
        FlashCardStats?[]? levels = id < _cards.Length ? _cards[id] : null;
        if (levels == null || (uint)upgrade >= (uint)levels.Length || levels[upgrade] is not { } stats)
            throw new FlashUnsupportedException($"规则表缺少卡牌 {FlashRegistry.Card(id).NativeId}+{upgrade}。");
        return stats;
    }

    public decimal PowerVar(ushort powerId, int index)
    {
        decimal[] vars = powerId < _powerVars.Length ? _powerVars[powerId] : [];
        if ((uint)index >= (uint)vars.Length)
            throw new FlashUnsupportedException($"规则表缺少 Power {FlashRegistry.Power(powerId).NativeId} 的第 {index} 个变量。");
        return vars[index];
    }

    public FlashPotionStats Potion(ushort potionId)
        => potionId < _potions.Length && _potions[potionId] is { } stats
            ? stats
            : throw new FlashUnsupportedException($"规则表缺少药水 {FlashRegistry.Potion(potionId).NativeId}。");

    /// <summary>A canonical dynamic variable of a relic, in the order its definition names them.</summary>
    public decimal RelicVar(ushort relicId, int index)
    {
        decimal[] vars = relicId < _relicVars.Length ? _relicVars[relicId] : [];
        if ((uint)index >= (uint)vars.Length)
            throw new FlashUnsupportedException($"规则表缺少遗物 {FlashRegistry.Relic(relicId).NativeId} 的第 {index} 个变量。");
        return vars[index];
    }

    public FlashMonsterRules Monster(byte owner)
    {
        int enemy = owner - FlashLimits.FirstEnemyOwner;
        if ((uint)enemy >= (uint)_monsters.Length || _monsters[enemy] is not { } rules)
            throw new FlashUnsupportedException($"规则表缺少敌人 {owner} 的状态机。");
        return rules;
    }
}

/// <summary>Collects root data from the bridge and freezes it into <see cref="FlashRules"/>.</summary>
public sealed class FlashRulesBuilder
{
    private readonly List<FlashCardStats?>[] _cards = new List<FlashCardStats?>[FlashRegistry.CardCount];
    private readonly decimal[]?[] _powerVars = new decimal[]?[FlashRegistry.PowerCount];
    private readonly FlashPotionStats?[] _potions = new FlashPotionStats?[FlashRegistry.PotionCount];
    private readonly decimal[]?[] _relicVars = new decimal[]?[FlashRegistry.RelicCount];
    private readonly FlashMonsterRules?[] _monsters = new FlashMonsterRules?[FlashLimits.Enemies];

    public int BaseMaxEnergy { get; set; }

    public int PlayerSlotIndex { get; set; }

    public void SetCard(ushort id, int upgrade, FlashCardStats stats)
    {
        List<FlashCardStats?> levels = _cards[id] ??= [];
        while (levels.Count <= upgrade)
            levels.Add(null);
        if (levels[upgrade] is { } existing && !SameStats(existing, stats))
            throw new InvalidOperationException($"卡牌 {FlashRegistry.Card(id).NativeId}+{upgrade} 的规则数据前后不一致。");
        levels[upgrade] = stats;
    }

    public bool HasCard(ushort id, int upgrade)
        => _cards[id] is { } levels && upgrade < levels.Count && levels[upgrade] != null;

    public void SetPowerVars(ushort id, decimal[] vars)
    {
        if (_powerVars[id] is { } existing && !existing.AsSpan().SequenceEqual(vars))
            throw new InvalidOperationException($"Power {FlashRegistry.Power(id).NativeId} 的规则数据前后不一致。");
        _powerVars[id] = vars;
    }

    public bool HasPowerVars(ushort id) => _powerVars[id] != null;

    public void SetPotion(ushort id, FlashPotionStats stats) => _potions[id] = stats;

    public void SetRelicVars(ushort id, decimal[] vars) => _relicVars[id] = vars;

    public void SetMonster(int enemyIndex, FlashMonsterRules rules)
    {
        if ((uint)enemyIndex >= FlashLimits.Enemies)
            throw new FlashCapacityException("Enemies", FlashLimits.Enemies);
        _monsters[enemyIndex] = rules;
    }

    public FlashRules Build()
    {
        FlashCardStats?[][] cards = new FlashCardStats?[_cards.Length][];
        for (int id = 0; id < _cards.Length; id++)
            cards[id] = _cards[id]?.ToArray() ?? [];
        decimal[][] powerVars = new decimal[_powerVars.Length][];
        for (int id = 0; id < _powerVars.Length; id++)
            powerVars[id] = _powerVars[id] ?? [];
        decimal[][] relicVars = new decimal[_relicVars.Length][];
        for (int id = 0; id < _relicVars.Length; id++)
            relicVars[id] = _relicVars[id] ?? [];
        return new FlashRules(cards, powerVars, (FlashPotionStats?[])_potions.Clone(), relicVars,
            (FlashMonsterRules?[])_monsters.Clone(), BaseMaxEnergy, PlayerSlotIndex);
    }

    private static bool SameStats(FlashCardStats left, FlashCardStats right)
        => left.Type == right.Type
            && left.Target == right.Target
            && left.Rarity == right.Rarity
            && left.EnergyCost == right.EnergyCost
            && left.CostsX == right.CostsX
            && left.StarCost == right.StarCost
            && left.StarCostsX == right.StarCostsX
            && left.Keywords == right.Keywords
            && left.Tags == right.Tags
            && left.Vars.AsSpan().SequenceEqual(right.Vars);
}
