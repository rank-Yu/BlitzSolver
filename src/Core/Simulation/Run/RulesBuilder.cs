using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Root facts that the opening battle root knows but <see cref="Battle"/> does not carry. The
/// encounter setup chooses each monster's initial state machine node and resolves conditional
/// branches for the opening root; later roots can also differ because a monster-private flag (for
/// example BowlbugRock's off-balance flag) is not in <see cref="Battle"/>. R2a-2 will build these
/// facts while instantiating the encounter; the import comparison reads them from the live root.
/// A fact value of <see cref="UnknownInitialNode"/> / <see cref="UnknownConditionalTarget"/> means
/// "derive it from the content table and the Core conditional resolver".
/// </summary>
public sealed class RulesRootFacts
{
    public const byte UnknownInitialNode = byte.MaxValue;
    public const int UnknownConditionalTarget = int.MinValue;

    public static RulesRootFacts Empty { get; } = new()
    {
        EnemyInitialNodes = [],
        EnemyConditionalTargets = [],
        EnemyNodeAttacks = [],
        EnemyRootPowerAmounts = [],
        EnemyInitialMoveLogged = [],
    };

    /// <summary>Initial node of each known enemy, indexed like <see cref="Battle.Enemies"/>.</summary>
    public required byte[] EnemyInitialNodes { get; init; }

    /// <summary>
    /// Conditional node targets of each known enemy, indexed like <see cref="Battle.Enemies"/>;
    /// each inner array is indexed by compiled node and uses <see cref="UnknownConditionalTarget"/>
    /// for non-conditional or unknown entries.
    /// </summary>
    public required int[][] EnemyConditionalTargets { get; init; }

    /// <summary>
    /// Root-captured attack values of each known enemy, indexed by enemy and compiled node; a null
    /// entry means the content table value is used. Attacks are captured at root because a
    /// monster-private number can change them (Waterfall Giant's pressure gun and The Forgotten's
    /// dexterity are the two examples); <see cref="Battle"/> does not carry those numbers.
    /// </summary>
    public required MonsterAttack[]?[][] EnemyNodeAttacks { get; init; }

    /// <summary>
    /// Root power baselines of each known enemy, or null to derive them from <see cref="Battle"/>.
    /// A post-prefix root reads these from the pre-prefix request root by combat id, so an enemy
    /// summoned by the prefix contributes an all-zero row even though the post-prefix battle has
    /// powers on it.
    /// </summary>
    public required int[]?[] EnemyRootPowerAmounts { get; init; }

    /// <summary>Native <c>InitialMoveLogged</c> per known enemy, or empty to use the content row.</summary>
    public bool[] EnemyInitialMoveLogged { get; init; } = [];

    /// <summary>Cards generated before the root that are not carried by <see cref="Battle.Cards"/>.</summary>
    public (string NativeId, int Upgrade)[] ExtraCards { get; init; } = [];

    public int[]? RootPowerAmounts(int enemy)
        => (uint)enemy < (uint)EnemyRootPowerAmounts.Length
            ? EnemyRootPowerAmounts[enemy]
            : null;

    public MonsterAttack[]? NodeAttacks(int enemy, int node)
        => (uint)enemy < (uint)EnemyNodeAttacks.Length
            && (uint)node < (uint)EnemyNodeAttacks[enemy].Length
            ? EnemyNodeAttacks[enemy][node]
            : null;

    public bool? InitialMoveLogged(int enemy)
        => (uint)enemy < (uint)EnemyInitialMoveLogged.Length ? EnemyInitialMoveLogged[enemy] : null;

    public byte InitialNode(int enemy)
        => (uint)enemy < (uint)EnemyInitialNodes.Length
            ? EnemyInitialNodes[enemy]
            : UnknownInitialNode;

    public int ConditionalTarget(int enemy, int node)
        => (uint)enemy < (uint)EnemyConditionalTargets.Length
            && (uint)node < (uint)EnemyConditionalTargets[enemy].Length
            ? EnemyConditionalTargets[enemy][node]
            : UnknownConditionalTarget;
}

/// <summary>
/// Builds a battle's <see cref="Rules"/> from the process-local <see cref="ContentTable"/>, the
/// run state and the battle root, without reading any native object. The tables are the same ones
/// <c>RulesCompiler</c> freezes: card stats per upgrade level, all power/relic/potion variables,
/// the monster state machines (including encounter-generated variants), encounter slots and the
/// per-character unlocked pools. Root-specific values come from <see cref="Battle"/>: piles,
/// powers, relics, potions, enemies, AI registers and lost-HP flags.
/// </summary>
public static class RulesBuilder
{
    public static Rules Build(ContentTable table, in RunState run, in Battle battle,
        ushort encounterId = ContentTable.NoEncounter, RulesRootFacts? facts = null,
        bool includeSetupContent = false)
    {
        ArgumentNullException.ThrowIfNull(table);
        facts ??= RulesRootFacts.Empty;
        ContentEncounter? encounter = encounterId == ContentTable.NoEncounter
            ? null
            : table.Encounter(encounterId);
        ContentCharacter character = table.Character(run.Player.Character);
        int ascension = Math.Clamp(run.Ascension, 0, ContentTable.AscensionCount - 1);

        RulesDraft draft = new()
        {
            BaseMaxEnergy = run.Player.MaxEnergy,
            BaseOrbSlotCount = run.Player.BaseOrbSlots,
            RootSwordSageAmount = PowerAmount(in battle, Limits.PlayerOwner, "SWORD_SAGE_POWER"),
            HasEncounter = encounter != null,
            EncounterSlots = encounter?.Slots ?? [],
        };
        bool hasIdentityContent = draft.RootSwordSageAmount != 0
            || character.NativeId is "REGENT" or "NECROBINDER" or "SILENT";

        bool hasShuffleOrderEnchantment = false;
        bool hasBeforeFlushEnchantment = false;
        for (int index = 0; index < battle.CardCount; index++)
        {
            ref readonly Card card = ref battle.Cards[index];
            AddCardStats(draft, table, in battle, card.Id, card.Upgrade);
            CardDef def = Registry.Card(card.Id);
            hasIdentityContent |= def.UsesIdentityLedger || def.TracksFetchPlay
                || def.MayGenerateIdentityContent || def.IsIdentityContent;
            if (card.EnchantmentId != 0)
            {
                ContentEnchantment enchantment = table.Enchantment((ushort)(card.EnchantmentId - 1));
                hasShuffleOrderEnchantment |= (enchantment.HooksExt & HookMaskExt.ModifyShuffleOrder) != 0;
                hasBeforeFlushEnchantment |= (enchantment.HooksExt & HookMaskExt.BeforeFlush) != 0;
            }
        }
        draft.HasShuffleOrderEnchantment = hasShuffleOrderEnchantment;
        draft.HasBeforeFlushEnchantment = hasBeforeFlushEnchantment;

        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly Power power = ref battle.Powers[index];
            if (power.Amount == 0)
                continue;
            hasIdentityContent |= Registry.Power(power.Id).IsIdentityContent;
        }

        ushort lampId = ushort.MaxValue;
        bool hasPaelsLegion = false;
        bool hasHistoryCourse = false;
        sbyte penNibRelicIndex = -1;
        for (int index = 0; index < battle.RelicCount; index++)
        {
            ref readonly Relic relic = ref battle.Relics[index];
            RelicDef def = Registry.Relic(relic.Id);
            hasIdentityContent |= def.MayGenerateIdentityContent || def.IsIdentityContent;
            if (lampId == ushort.MaxValue && def.IsUnsettlingLamp)
                lampId = relic.Id;
            if ((relic.Flags & RelicFlags.Melted) != 0)
                continue;
            if (def.NativeId == "HISTORY_COURSE")
                hasHistoryCourse = true;
            if (def.NativeId == "PEN_NIB")
                penNibRelicIndex = checked((sbyte)index);
            if (def.IsPaelsLegion)
                hasPaelsLegion = true;
        }
        draft.UnsettlingLampRelicId = lampId;
        draft.HasIdentityContent = hasIdentityContent;
        draft.HasPaelsLegion = hasPaelsLegion;
        draft.FurCoatSpawnHpOne = FurCoatSpawnHpOne(in run);
        draft.HasHistoryCourse = hasHistoryCourse;
        draft.PenNibRelicIndex = penNibRelicIndex;
        draft.HasTheftContent = HasTheftContent(in battle, encounter);

        for (ushort id = 0; id < Registry.PowerCount; id++)
        {
            if (Registry.Power(id).VarNames.Count > 0)
                draft.SetPowerVars(id, table.PowerVarsOf(id));
            draft.SetPowerDeathHistoryRisk(id, table.PowerDeathHistoryRisk[id]);
        }
        for (ushort id = 0; id < Registry.PotionCount; id++)
            draft.SetPotion(id, table.Potion(id));
        for (ushort id = 0; id < Registry.RelicCount; id++)
            draft.SetRelicVars(id, table.RelicVarsOf(id));

        if (battle.EnemyCount > Limits.Enemies)
            throw new CapacityException("Enemies", Limits.Enemies);
        draft.RootEnemyCount = battle.EnemyCount;
        draft.RootLostHpMask = RootLostHpMask(in battle);

        List<ushort> spawnClosure = SpawnClosure(in battle);
        if (battle.EnemyCount + spawnClosure.Count > Limits.MonsterRules)
            throw new CapacityException("MonsterRules", Limits.MonsterRules);

        List<(ushort CardId, int Upgrade)> detached = [];
        for (int index = 0; index < Limits.DetachedCards; index++)
        {
            ref readonly Card card = ref battle.DetachedCards[index];
            if (card.Id != 0)
                detached.Add((card.Id, card.Upgrade));
        }
        AddGeneratedCardClosure(draft, table, in run, in battle, character,
            spawnClosure, detached, facts.ExtraCards, includeSetupContent);
        draft.HasInfiniteHpDisplayMonster = HasInfiniteHpDisplayMonster(in battle, spawnClosure);

        List<MonsterRules> rows = BuildMonsterRows(table, encounter, in battle, facts, ascension);
        for (int index = 0; index < rows.Count; index++)
            draft.SetMonster(index, rows[index]);
        foreach (ushort monsterId in spawnClosure)
            draft.AddSpawnableMonster(table.SpawnMonster(monsterId, ascension));

        ApplyConditionalResolver(draft, in battle, rows, facts);
        return draft.Build();
    }

    private static List<MonsterRules> BuildMonsterRows(ContentTable table,
        ContentEncounter? encounter, in Battle battle, RulesRootFacts facts, int ascension)
    {
        List<MonsterRules> rows = new(battle.EnemyCount);
        bool[] used = encounter == null ? [] : new bool[encounter.Monsters.Length];
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            ref readonly Enemy enemy = ref battle.Enemies[index];
            MonsterDef def = Registry.Monster(enemy.MonsterId);
            MonsterRules? baseRules = null;
            if (encounter != null)
            {
                string? slot = enemy.Slot == Kernel.NoSlot
                    ? null
                    : (uint)enemy.Slot < (uint)encounter.Slots.Length
                        ? encounter.Slots[enemy.Slot]
                        : null;
                for (int candidate = 0; candidate < encounter.Monsters.Length; candidate++)
                {
                    if (used[candidate])
                        continue;
                    ContentEncounterMonster entry = encounter.Monsters[candidate];
                    if (entry.MonsterId != enemy.MonsterId
                        || !string.Equals(entry.Slot, slot, StringComparison.Ordinal))
                        continue;
                    used[candidate] = true;
                    baseRules = entry.Rules(ascension);
                    break;
                }
            }
            baseRules ??= table.SpawnMonster(enemy.MonsterId, ascension);
            rows.Add(OverlayMonster(baseRules, def, in battle, index, facts));
        }
        return rows;
    }

    private static MonsterRules OverlayMonster(MonsterRules baseRules, MonsterDef def,
        in Battle battle, int index, RulesRootFacts facts)
    {
        ref readonly Enemy enemy = ref battle.Enemies[index];
        int[] rootPowers = facts.RootPowerAmounts(index)
            ?? new int[def.RootPowerAmountNames.Count];
        if (facts.RootPowerAmounts(index) == null)
        {
            for (int position = 0; position < rootPowers.Length; position++)
            {
                string nativeId = def.RootPowerAmountNames[position];
                for (int power = 0; power < battle.PowerCount; power++)
                {
                    ref readonly Power candidate = ref battle.Powers[power];
                    if (candidate.Owner != (byte)(Limits.FirstEnemyOwner + index)
                        || candidate.Amount == 0
                        || Registry.Power(candidate.Id).NativeId != nativeId)
                        continue;
                    rootPowers[position] = candidate.Amount;
                    break;
                }
            }
        }
        int[] ai = new int[def.AiRegisterNames.Count];
        for (int register = 0; register < ai.Length; register++)
            ai[register] = enemy.Ai[register];

        MonsterNode[] nodes = baseRules.Nodes;
        for (int node = 0; node < nodes.Length; node++)
        {
            int target = facts.ConditionalTarget(index, node);
            MonsterAttack[]? attacks = facts.NodeAttacks(index, node);
            bool targetChanged = nodes[node].Kind == MonsterNodeKind.ConditionalBranch
                && target != RulesRootFacts.UnknownConditionalTarget
                && target != nodes[node].ConditionalTarget;
            bool attacksChanged = attacks != null
                && !attacks.AsSpan().SequenceEqual(nodes[node].Attacks);
            if (!targetChanged && !attacksChanged)
                continue;
            if (nodes == baseRules.Nodes)
                nodes = [.. nodes];
            nodes[node] = new MonsterNode
            {
                NativeId = nodes[node].NativeId,
                Kind = nodes[node].Kind,
                FollowUp = nodes[node].FollowUp,
                MustPerformOnceBeforeTransitioning = nodes[node].MustPerformOnceBeforeTransitioning,
                Attacks = attacks ?? nodes[node].Attacks,
                Options = nodes[node].Options,
                ConditionalTarget = targetChanged ? target : nodes[node].ConditionalTarget,
            };
        }
        int initialNode = facts.InitialNode(index);
        if (initialNode == RulesRootFacts.UnknownInitialNode)
            initialNode = baseRules.InitialNode;
        return new MonsterRules
        {
            MonsterId = baseRules.MonsterId,
            Nodes = nodes,
            InitialNode = initialNode,
            PendingInitialMoveNode = baseRules.PendingInitialMoveNode,
            InitialMoveLogged = facts.InitialMoveLogged(index) ?? baseRules.InitialMoveLogged,
            StunnedNode = baseRules.StunnedNode,
            ReviveNode = baseRules.ReviveNode,
            InitialHpMin = baseRules.InitialHpMin,
            InitialHpMax = baseRules.InitialHpMax,
            StaticInts = baseRules.StaticInts,
            RootPowerAmounts = rootPowers,
            InitialAiRegisters = ai,
        };
    }

    /// <summary>
    /// Resolves conditional branches that the content table cannot decide and the root facts did
    /// not carry, using the same Core monster-specific resolver the kernel uses. The provisional
    /// rules only need the monster rows and the encounter slots.
    /// </summary>
    private static void ApplyConditionalResolver(RulesDraft draft,
        in Battle battle, List<MonsterRules> rows, RulesRootFacts facts)
    {
        bool needed = false;
        for (int index = 0; index < rows.Count && !needed; index++)
        {
            for (int node = 0; node < rows[index].Nodes.Length; node++)
            {
                if (rows[index].Nodes[node].Kind == MonsterNodeKind.ConditionalBranch
                    && facts.ConditionalTarget(index, node) == RulesRootFacts.UnknownConditionalTarget)
                {
                    needed = true;
                    break;
                }
            }
        }
        if (!needed)
            return;
        Battle scratch = battle;
        Rules provisional = draft.Build();
        for (int index = 0; index < rows.Count; index++)
        {
            MonsterRules row = rows[index];
            MonsterDef def = Registry.Monster(row.MonsterId);
            MonsterNode[]? nodes = null;
            for (int node = 0; node < row.Nodes.Length; node++)
            {
                if (row.Nodes[node].Kind != MonsterNodeKind.ConditionalBranch
                    || facts.ConditionalTarget(index, node) != RulesRootFacts.UnknownConditionalTarget)
                    continue;
                PlayerAction action = default;
                Exec exec = new(ref scratch, provisional, in action);
                int target = def.ResolveConditional(ref exec,
                    checked((byte)(Limits.FirstEnemyOwner + index)), node);
                if (target < 0 || target == row.Nodes[node].ConditionalTarget)
                    continue;
                nodes ??= [.. row.Nodes];
                nodes[node] = new MonsterNode
                {
                    NativeId = nodes[node].NativeId,
                    Kind = nodes[node].Kind,
                    FollowUp = nodes[node].FollowUp,
                    MustPerformOnceBeforeTransitioning = nodes[node].MustPerformOnceBeforeTransitioning,
                    Attacks = nodes[node].Attacks,
                    Options = nodes[node].Options,
                    ConditionalTarget = target,
                };
            }
            if (nodes != null)
            {
                row = new MonsterRules
                {
                    MonsterId = row.MonsterId,
                    Nodes = nodes,
                    InitialNode = row.InitialNode,
                    PendingInitialMoveNode = row.PendingInitialMoveNode,
                    InitialMoveLogged = row.InitialMoveLogged,
                    StunnedNode = row.StunnedNode,
                    ReviveNode = row.ReviveNode,
                    InitialHpMin = row.InitialHpMin,
                    InitialHpMax = row.InitialHpMax,
                    StaticInts = row.StaticInts,
                    RootPowerAmounts = row.RootPowerAmounts,
                    InitialAiRegisters = row.InitialAiRegisters,
                };
                rows[index] = row;
                draft.SetMonster(index, row);
            }
        }
    }

    private static void AddGeneratedCardClosure(RulesDraft draft, ContentTable table,
        in RunState run, in Battle battle, ContentCharacter character,
        IReadOnlyList<ushort> spawnClosure, IReadOnlyList<(ushort CardId, int Upgrade)> detached,
        IReadOnlyList<(string NativeId, int Upgrade)> extraCards, bool includeSetupContent)
    {
        Queue<(ushort CardId, int Upgrade)> pendingCards = new();
        Queue<string> pendingPools = new();
        Queue<string> pendingPowers = new();
        HashSet<(ushort, int)> seenCards = [];
        foreach ((string nativeId, int upgrade) in extraCards)
        {
            if (!Registry.TryCard(nativeId, out ushort extraId))
                throw new ContentUnavailableException(ContentReason.UnsupportedClosure,
                    $"开战生成卡 {nativeId} 没有 Blitz 定义");
            AddCardStats(draft, table, in battle, extraId, upgrade);
            if (seenCards.Add((extraId, upgrade)))
                pendingCards.Enqueue((extraId, upgrade));
        }
        foreach ((ushort cardId, int upgrade) in detached)
        {
            if (seenCards.Add((cardId, upgrade)))
                pendingCards.Enqueue((cardId, upgrade));
        }
        HashSet<string> seenPools = new(StringComparer.Ordinal);
        HashSet<string> seenPowerDefs = new(StringComparer.Ordinal);
        HashSet<ushort> seenPotionDefs = [];
        ushort[]? inCombatPotionPool = null;
        ushort[]? outOfCombatPotionPool = null;
        bool needsPotionPool = false;
        bool needsOutOfCombatPotionPool = false;
        void DeclareCard((string NativeId, int Upgrade) card)
        {
            if (!Registry.TryCard(card.NativeId, out ushort cardId))
                throw new ContentUnavailableException(ContentReason.UnsupportedClosure,
                    $"生成卡 {card.NativeId} 没有 Blitz 定义");
            if (seenCards.Add((cardId, card.Upgrade)))
                pendingCards.Enqueue((cardId, card.Upgrade));
        }

        void DeclarePool(string pool)
        {
            if (seenPools.Add(pool))
                pendingPools.Enqueue(pool);
        }

        void Declare(ContentDef def)
        {
            needsPotionPool |= def.GeneratesRandomPotion;
            needsOutOfCombatPotionPool |= def.GeneratesOutOfCombatPotion
                || (includeSetupContent && def.SetupOnlyOutOfCombatPotionPool);
            if (def is PotionDef && !seenPotionDefs.Add(def.Id))
                return;
            foreach ((string nativeId, int upgrade) in def.GeneratedCards)
                DeclareCard((nativeId, upgrade));
            foreach (string pool in def.GeneratedCardPools)
                DeclarePool(pool);
            if (includeSetupContent)
            {
                foreach ((string nativeId, int upgrade) in def.SetupOnlyGeneratedCards)
                    DeclareCard((nativeId, upgrade));
                foreach (string pool in def.SetupOnlyCardPools)
                    DeclarePool(pool);
            }
            foreach (string power in def.AppliedPowers)
                pendingPowers.Enqueue(power);
        }
        for (int index = 0; index < battle.CardCount; index++)
            Declare(Registry.Card(battle.Cards[index].Id));
        for (int index = 0; index < battle.PowerCount; index++)
        {
            if (battle.Powers[index].Amount != 0)
                Declare(Registry.Power(battle.Powers[index].Id));
        }
        for (int index = 0; index < battle.RelicCount; index++)
            Declare(Registry.Relic(battle.Relics[index].Id));
        for (int slot = 0; slot < battle.PotionSlotCount && slot < Limits.PotionSlots; slot++)
        {
            ushort stored = battle.Potions[slot];
            if (stored != 0)
                Declare(Registry.Potion((ushort)(stored - 1)));
        }
        for (int index = 0; index < battle.EnemyCount; index++)
            Declare(Registry.Monster(battle.Enemies[index].MonsterId));
        foreach (ushort monsterId in spawnClosure)
            Declare(Registry.Monster(monsterId));

        while (true)
        {
            while (pendingCards.Count > 0 || pendingPools.Count > 0 || pendingPowers.Count > 0)
            {
                while (pendingPowers.Count > 0)
                {
                    string nativeId = pendingPowers.Dequeue();
                    if (!seenPowerDefs.Add(nativeId))
                        continue;
                    if (!Registry.TryPower(nativeId, out ushort powerId))
                        throw new ContentUnavailableException(ContentReason.UnsupportedClosure,
                            $"生成 Power {nativeId} 没有 Blitz 定义");
                    Declare(Registry.Power(powerId));
                }
                while (pendingPools.Count > 0)
                {
                    string key = pendingPools.Dequeue();
                    ushort[] pool = FreezeCardPool(table, in run, character, key);
                    draft.SetCardPool(key, pool);
                    foreach (ushort id in pool)
                    {
                        Declare(Registry.Card(id));
                        AddCardStats(draft, table, in battle, id, 0);
                    }
                }
                if (pendingCards.Count == 0)
                    break;
                (ushort cardId, int upgrade) = pendingCards.Dequeue();
                AddCardStats(draft, table, in battle, cardId, upgrade);
                Declare(Registry.Card(cardId));
            }
            if (needsPotionPool && inCombatPotionPool == null)
            {
                inCombatPotionPool = FreezePotionPool(table, in run, character, inCombat: true);
                draft.SetPotionPool(inCombatPotionPool);
                foreach (ushort id in inCombatPotionPool)
                    Declare(Registry.Potion(id));
                continue;
            }
            if (needsOutOfCombatPotionPool && outOfCombatPotionPool == null)
            {
                outOfCombatPotionPool = FreezePotionPool(table, in run, character, inCombat: false);
                draft.SetOutOfCombatPotionPool(outOfCombatPotionPool);
                foreach (ushort id in outOfCombatPotionPool)
                    Declare(Registry.Potion(id));
                continue;
            }
            break;
        }

        for (int slot = 0; slot < battle.PotionSlotCount && slot < Limits.PotionSlots; slot++)
        {
            ushort stored = battle.Potions[slot];
            if (stored != 0)
                draft.SetRootPotion(slot, (ushort)(stored - 1));
        }
    }

    private static ushort[] FreezeCardPool(ContentTable table, in RunState run,
        ContentCharacter character, string key)
    {
        List<ContentPoolEntry> candidates = [];
        switch (key)
        {
            case CardPools.CharacterAll:
                candidates.AddRange(character.CardPool.Cards);
                break;
            case CardPools.CharacterAttacks:
                candidates.AddRange(character.CardPool.Cards.Where(
                    static entry => entry.Type == CardType.Attack));
                break;
            case CardPools.CharacterZeroCost:
                candidates.AddRange(character.CardPool.Cards.Where(
                    static entry => entry.EnergyCost == 0 && !entry.CostsX));
                break;
            case CardPools.ColorlessAll:
                candidates.AddRange(table.ColorlessPool.Cards);
                break;
            case CardPools.ColorlessExceptJackOfAllTrades:
                candidates.AddRange(table.ColorlessPool.Cards.Where(
                    static entry => entry.NativeId != "JACK_OF_ALL_TRADES"));
                break;
            case CardPools.OtherCharacterAttacks:
                {
                    int unlocked = 0;
                    for (int index = 0; index < table.CharacterOrder.Length; index++)
                    {
                        ContentCharacter other = table.Character(table.CharacterOrder[index]);
                        if (EpochsRevealed(in run, other.UnlockEpochs))
                            unlocked++;
                    }
                    for (int index = 0; index < table.CharacterOrder.Length; index++)
                    {
                        ushort otherId = table.CharacterOrder[index];
                        ContentCharacter other = table.Character(otherId);
                        if (!EpochsRevealed(in run, other.UnlockEpochs)
                            || (unlocked > 1 && otherId == character.Id))
                            continue;
                        candidates.AddRange(other.CardPool.Cards.Where(
                            static entry => entry.Type == CardType.Attack));
                    }
                    break;
                }
            case CardPools.CharacterPowers:
                candidates.AddRange(character.CardPool.Cards.Where(
                    static entry => entry.Type == CardType.Power));
                break;
            case CardPools.CharacterEthereal:
                candidates.AddRange(character.CardPool.Cards.Where(static entry => entry.Ethereal));
                break;
            case CardPools.StatusAll:
                candidates.AddRange(table.StatusPool.Cards);
                break;
            case CardPools.CurseAll:
                candidates.AddRange(table.CursePool.Cards);
                break;
            default:
                throw new ContentUnavailableException(ContentReason.UnsupportedClosure,
                    $"未知卡池 {key}");
        }

        List<ushort> ids = [];
        HashSet<ushort> seen = [];
        foreach (ContentPoolEntry entry in candidates)
        {
            if (!EpochsRevealed(in run, entry.RequiredEpochs) || entry.MultiplayerOnly)
                continue;
            if (!entry.CanBeGeneratedInCombat
                || entry.Rarity is CardRarity.Basic or CardRarity.Ancient or CardRarity.Event)
                continue;
            if (entry.CardId == ContentPoolEntry.NoCard)
                throw new ContentUnavailableException(ContentReason.UnsupportedCard,
                    $"卡池 {key} 的候选 {entry.NativeId} 没有 Blitz 定义");
            if (seen.Add(entry.CardId))
                ids.Add(entry.CardId);
        }
        return [.. ids];
    }

    private static ushort[] FreezePotionPool(ContentTable table, in RunState run,
        ContentCharacter character, bool inCombat)
    {
        List<ContentPotionEntry> candidates = [.. character.PotionPool, .. table.SharedPotionPool];
        List<ushort> ids = [];
        HashSet<ushort> seen = [];
        foreach (ContentPotionEntry entry in candidates)
        {
            if (!EpochsRevealed(in run, entry.RequiredEpochs))
                continue;
            if (inCombat && !entry.CanBeGeneratedInCombat)
                continue;
            if (entry.PotionId == ContentPotionEntry.NoPotion)
                throw new ContentUnavailableException(ContentReason.UnsupportedPotion,
                    $"药水池的候选 {entry.NativeId} 没有 Blitz 定义");
            if (seen.Add(entry.PotionId))
                ids.Add(entry.PotionId);
        }
        return [.. ids];
    }

    private static void AddCardStats(RulesDraft draft, ContentTable table, in Battle battle,
        ushort id, int upgrade)
    {
        CardStats stats = CardStatsFor(table, in battle, id, upgrade);
        draft.HasDecimalCardVars |= Registry.Card(id).UsesDecimalDamageVar;
        if (upgrade > 0 && !draft.HasCard(id, 0))
            draft.SetCard(id, 0, CardStatsFor(table, in battle, id, 0));
        if (!draft.HasCard(id, upgrade))
            draft.SetCard(id, upgrade, stats);
        if (upgrade < stats.MaxUpgradeLevel && !draft.HasCard(id, upgrade + 1))
            draft.SetCard(id, upgrade + 1, CardStatsFor(table, in battle, id, upgrade + 1));
    }

    /// <summary>
    /// The content table stores the canonical card values. Two cards change their target while the
    /// owner holds a specific power; both powers are in <see cref="Battle"/>, so the root value is
    /// derived here instead of being frozen into the table.
    /// </summary>
    private static CardStats CardStatsFor(ContentTable table, in Battle battle, ushort id,
        int upgrade)
    {
        CardStats stats = table.Card(id, upgrade);
        bool allEnemies = Registry.Card(id).NativeId switch
        {
            "SHIV" => PowerAmount(in battle, Limits.PlayerOwner, "FAN_OF_KNIVES_POWER") != 0,
            "SOVEREIGN_BLADE" => PowerAmount(in battle, Limits.PlayerOwner, "SEEKING_EDGE_POWER") != 0,
            _ => false,
        };
        if (!allEnemies || stats.Target == TargetType.AllEnemies)
            return stats;
        return new CardStats
        {
            Type = stats.Type,
            Target = TargetType.AllEnemies,
            Rarity = stats.Rarity,
            Pool = stats.Pool,
            EnergyCost = stats.EnergyCost,
            CostsX = stats.CostsX,
            StarCost = stats.StarCost,
            StarCostsX = stats.StarCostsX,
            Keywords = stats.Keywords,
            HasDynamicType = stats.HasDynamicType,
            MaxUpgradeLevel = stats.MaxUpgradeLevel,
            Tags = stats.Tags,
            IsSoul = stats.IsSoul,
            IsColorless = stats.IsColorless,
            PlaysRecordRisk = stats.PlaysRecordRisk,
            Vars = stats.Vars,
        };
    }

    private static ushort RootLostHpMask(in Battle battle)
    {
        ushort mask = 0;
        if ((battle.Player.Flags & CreatureFlags.LostHpThisTurn) != 0)
            mask |= 1 << Limits.PlayerOwner;
        if ((battle.Flags & BattleFlags.OstyPresent) != 0
            && (battle.Osty.Flags & CreatureFlags.LostHpThisTurn) != 0)
            mask |= 1 << Limits.OstyOwner;
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            if ((battle.Enemies[index].Creature.Flags & CreatureFlags.LostHpThisTurn) != 0)
                mask |= (ushort)(1 << (Limits.FirstEnemyOwner + index));
        }
        return mask;
    }

    private static bool HasTheftContent(in Battle battle, ContentEncounter? encounter)
    {
        if (encounter?.NativeId is "GREMLIN_MERC_NORMAL" or "THIEVING_HOPPER_WEAK")
            return true;
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            if (Registry.Monster(battle.Enemies[index].MonsterId).NativeId
                is "GREMLIN_MERC" or "THIEVING_HOPPER" or "FAT_GREMLIN")
                return true;
        }
        for (int index = 0; index < battle.PowerCount; index++)
        {
            if (battle.Powers[index].Amount == 0)
                continue;
            if (Registry.Power(battle.Powers[index].Id).NativeId
                is "THIEVERY_POWER" or "HEIST_POWER" or "SWIPE_POWER" or "SURPRISE_POWER")
                return true;
        }
        return false;
    }

    private static List<ushort> SpawnClosure(in Battle battle)
    {
        List<ushort> closure = [];
        HashSet<ushort> seen = [];
        Queue<string> pending = new();
        void Declare(MonsterDef def)
        {
            foreach (string nativeId in def.SpawnedMonsterIds)
                pending.Enqueue(nativeId);
        }
        for (int index = 0; index < battle.EnemyCount; index++)
            Declare(Registry.Monster(battle.Enemies[index].MonsterId));
        while (pending.Count > 0)
        {
            string nativeId = pending.Dequeue();
            if (!Registry.TryMonster(nativeId, out ushort id))
                throw new ContentUnavailableException(ContentReason.UnsupportedClosure,
                    $"生成怪物 {nativeId} 没有 Blitz 定义");
            if (!seen.Add(id))
                continue;
            closure.Add(id);
            Declare(Registry.Monster(id));
        }
        return closure;
    }

    private static bool HasInfiniteHpDisplayMonster(in Battle battle, IReadOnlyList<ushort> closure)
    {
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            if (Registry.Monster(battle.Enemies[index].MonsterId).HasInfiniteHpDisplay)
                return true;
        }
        foreach (ushort monsterId in closure)
        {
            if (Registry.Monster(monsterId).HasInfiniteHpDisplay)
                return true;
        }
        return false;
    }

    private static int PowerAmount(in Battle battle, byte owner, string nativeId)
    {
        if (!Registry.TryPower(nativeId, out ushort id))
            return 0;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly Power power = ref battle.Powers[index];
            if (power.Owner == owner && power.Id == id && power.Amount > 0)
                return power.Amount;
        }
        return 0;
    }

    private static bool EpochsRevealed(in RunState run, ulong mask)
    {
        for (int epoch = 0; epoch < RunCatalog.EpochCount; epoch++)
        {
            if ((mask & (1UL << epoch)) != 0 && !run.HasEpoch((ushort)epoch))
                return false;
        }
        return true;
    }

    private static bool FurCoatSpawnHpOne(in RunState run)
    {
        if (run.Visited.CoordCount == 0)
            return false;
        RunCoord current = run.Visited.Coords[run.Visited.CoordCount - 1];
        ulong colsHash = RunProperties.NameHash("FurCoatCoordCols");
        ulong rowsHash = RunProperties.NameHash("FurCoatCoordRows");
        ulong setHash = RunProperties.NameHash("FurCoatCoordsSet");
        for (int index = 0; index < run.Relics.Count; index++)
        {
            RunRelic relic = run.Relics.Items[index];
            if (Registry.Relic(relic.Id).NativeId != "FUR_COAT"
                || (relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            bool set = false;
            int[] cols = [];
            int[] rows = [];
            RunProperties.Reader reader = new(relic.Properties);
            while (reader.TryNext(out ulong name, out byte kind))
            {
                if (name == setHash && kind == RunProperties.KindBool)
                {
                    set = reader.ReadBool();
                }
                else if (name == colsHash && kind == RunProperties.KindIntArray)
                {
                    int count = reader.ReadByte();
                    cols = new int[count];
                    for (int item = 0; item < count; item++)
                        cols[item] = reader.ReadInt32();
                }
                else if (name == rowsHash && kind == RunProperties.KindIntArray)
                {
                    int count = reader.ReadByte();
                    rows = new int[count];
                    for (int item = 0; item < count; item++)
                        rows[item] = reader.ReadInt32();
                }
                else
                {
                    SkipProperty(ref reader, kind);
                }
            }
            if (!set || cols.Length != rows.Length)
                continue;
            for (int mark = 0; mark < cols.Length; mark++)
            {
                if (cols[mark] == current.Col && rows[mark] == current.Row)
                    return true;
            }
        }
        return false;
    }

    private static void SkipProperty(ref RunProperties.Reader reader, byte kind)
    {
        switch (kind)
        {
            case RunProperties.KindInt:
                _ = reader.ReadInt32();
                break;
            case RunProperties.KindBool:
                _ = reader.ReadBool();
                break;
            case RunProperties.KindString:
                _ = reader.ReadString();
                break;
            case RunProperties.KindModel:
                _ = reader.ReadModel();
                break;
            case RunProperties.KindCard:
                _ = reader.ReadCard();
                break;
            case RunProperties.KindCardArray:
                {
                    int count = reader.ReadByte();
                    for (int index = 0; index < count; index++)
                        _ = reader.ReadCard();
                    break;
                }
            case RunProperties.KindIntArray:
                {
                    int count = reader.ReadByte();
                    for (int index = 0; index < count; index++)
                        _ = reader.ReadInt32();
                    break;
                }
            default:
                throw new InvalidDataException($"未知遗物属性种类 {kind}。");
        }
    }
}
