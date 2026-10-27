using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>The opening battle, its rules and the run state after entering the map point.</summary>
public sealed class CombatSetupResult
{
    public required RunState Run { get; init; }

    public required Battle Battle { get; init; }

    public required Rules Rules { get; init; }

    public required RulesRootFacts Facts { get; init; }

    public required ushort EncounterId { get; init; }
}

/// <summary>
/// The run-side half of the combat boundary: enter a combat map point, advance the run state,
/// generate the encounter and build the opening <see cref="Battle"/>. The battle-start semantics
/// live in <see cref="CombatStart"/> and the content definitions; this class only maps the run state
/// (deck, relics, potions, RNG and act sequences) into the combat structures.
/// </summary>
public static class RunCombatSetup
{
    private const int EncounterSpawnCapacity = 16;

    public static CombatSetupResult EnterMapCoord(in RunState run, ContentTable table, RunCoord coord)
    {
        RunMapEntryResult entry = RunMapEntry.Enter(in run, table, coord);
        return entry.Combat ?? throw new ContentUnavailableException(ContentReason.UnsupportedRoom,
            $"坐标 {coord} 生成了 {entry.RoomType} 房间，不是战斗房间。");
    }

    /// <summary>
    /// Native <c>EnterMapPointInternal</c> without a room-stack base, addressed by point type.
    /// Kept for the R2a-2 callers; the shared implementation is <see cref="RunMapEntry"/>.
    /// </summary>
    public static CombatSetupResult EnterMapPoint(in RunState run, ContentTable table, int actFloor,
        RunMapPointType pointType)
    {
        RunMapEntryResult entry = RunMapEntry.Enter(in run, table,
            FindPointCoord(in run, pointType), actFloor);
        return entry.Combat ?? throw new ContentUnavailableException(ContentReason.UnsupportedRoom,
            $"{pointType} 生成了 {entry.RoomType} 房间，不是战斗房间。");
    }

    /// <summary>
    /// Battle half of the combat boundary. The caller (<see cref="RunMapEntry"/>) has already
    /// appended the map-point history and pushed the combat room, matching the native order where
    /// the room is created and recorded before <c>EnterRoomInternal</c>.
    /// </summary>
    internal static CombatSetupResult BuildCombat(ref RunState next, ContentTable table,
        RunRoomType roomType, ushort encounterId)
    {
        RejectUnsupportedRelics(in next);
        string nativeId = RunCatalog.EncounterName(encounterId);
        if (!Registry.TryEncounter(nativeId, out ushort defId))
            throw new ContentUnavailableException(ContentReason.UnsupportedEncounter, $"没有遭遇生成定义 {nativeId}。");
        EncounterDef def = Registry.Encounter(defId);

        EncounterRng encounterRng = new(
            RunRng.Encounter(next.Rng.Seed, next.History.Count, encounterId));
        Span<EncounterSpawn> spawns = stackalloc EncounterSpawn[EncounterSpawnCapacity];
        def.Generate(ref encounterRng, spawns, out int spawnCount);
        if (spawnCount == 0)
            throw new ContentUnavailableException(ContentReason.UnsupportedEncounter, $"遭遇 {nativeId} 没有生成怪物。");

        ContentEncounter encounter = table.Encounter(encounterId);
        if (encounter.UnsupportedReason != null)
            throw new ContentUnavailableException(encounter.UnsupportedReason,
                encounter.UnsupportedDetail ?? nativeId);
        if (encounter.Slots.Length != def.Slots.Count)
            throw new ContentUnavailableException(ContentReason.UnsupportedEncounter, $"遭遇 {nativeId} 的槽位定义与内容表不一致。");

        int ascension = Math.Clamp(next.Ascension, 0, ContentTable.AscensionCount - 1);
        Battle battle = CreateBattleShell(in next, table);
        for (int index = 0; index < spawnCount; index++)
        {
            EncounterSpawn spawn = spawns[index];
            string? slot = spawn.Slot == Kernel.NoSlot ? null : def.Slots[spawn.Slot];
            battle.Enemies[index] = new Enemy
            {
                MonsterId = spawn.MonsterId,
                Slot = spawn.Slot,
                Rules = checked((byte)index),
            };
            MonsterRules baseRules = RulesFor(table, encounter, spawn.MonsterId, slot, ascension);
            for (int register = 0; register < baseRules.InitialAiRegisters.Length
                 && register < Limits.EnemyAiRegisters; register++)
            {
                battle.Enemies[index].Ai[register] = baseRules.InitialAiRegisters[register];
            }
        }
        battle.EnemyCount = checked((byte)spawnCount);

        byte[] initialNodes = new byte[spawnCount];
        bool[] initialMoveLogged = new bool[spawnCount];
        int[][] provisionalConditional = new int[spawnCount][];
        for (int index = 0; index < spawnCount; index++)
        {
            initialNodes[index] = InitialNodeFor(table, encounter, spawns[index], ascension);
            initialMoveLogged[index] = InitialMoveLoggedFor(table, encounter, spawns[index], ascension);
            provisionalConditional[index] = ConditionalTargets(table, encounter, spawns[index], ascension);
        }
        RulesRootFacts provisionalFacts = new()
        {
            EnemyInitialNodes = initialNodes,
            EnemyConditionalTargets = provisionalConditional,
            EnemyNodeAttacks = [],
            EnemyRootPowerAmounts = [],
            EnemyInitialMoveLogged = initialMoveLogged,
            ExtraCards = SetupExtraCards(in next),
        };
        Rules provisional = RulesBuilder.Build(table, in next, in battle, encounterId, provisionalFacts);

        // The rules table rows are in enemy order; now create the real rows (HP roll + AI registers)
        // and give each the matching rule index.
        battle.EnemyCount = 0;
        battle.RosterCount = 0;
        battle.Enemies = default;
        PlayerAction noAction = default;
        Exec x = new(ref battle, provisional, in noAction)
        {
            CombatStart = true,
            SetupContent = table,
        };
        for (int index = 0; index < spawnCount; index++)
        {
            EncounterSpawn spawn = spawns[index];
            Kernel.AddMonsterForCombatStart(ref x, index, spawn.Slot);
            if (spawn.HpReduction > 0)
            {
                ref Creature creature = ref battle.Enemies[index].Creature;
                creature.CurrentHp = Math.Max(1, creature.CurrentHp - spawn.HpReduction);
            }
        }

        CombatStart.AfterRoomEntered(ref battle, provisional, table,
            roomType == RunRoomType.Elite, roomType == RunRoomType.Boss);
        for (int index = 0; index < spawnCount; index++)
        {
            string? slot = spawns[index].Slot == Kernel.NoSlot
                ? null : def.Slots[spawns[index].Slot];
            ContentEncounterMonster monsterEntry = EncounterMonster(encounter, spawns[index].MonsterId, slot);
            byte owner = checked((byte)(Limits.FirstEnemyOwner + index));
            Kernel.PrepareMonsterForCombatStart(ref x, owner,
                x.R.MonsterRule(x.B.Enemies[index].Rules),
                monsterEntry.CombatStart(ascension));
        }
        for (int index = 0; index < next.Relics.Count; index++)
        {
            RunRelic relic = next.Relics.Items[index];
            sbyte status = Registry.Relic(relic.Id)
                .RoomEnteredStatus(in battle, relic.Id, table,
                    roomType == RunRoomType.Elite, roomType == RunRoomType.Boss);
            if (status >= 0)
                next.Relics.Items[index].Status = (RunRelicStatus)status;
        }
        CombatStart.BeforeCombatStart(ref battle, provisional, table, roomType == RunRoomType.Boss);
        CombatStart.BeforeCombatStartLate(ref battle, provisional, table, roomType == RunRoomType.Boss);
        CombatStart.StartFirstTurn(ref battle, provisional, table);
        if ((battle.Flags & BattleFlags.OstyPresent) != 0)
            battle.Flags |= BattleFlags.OstyWasRootPet;
        ApplyTurnStartRelicState(ref next, in battle);
        bool hasVitalSpark = false;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            if (Registry.Power(battle.Powers[index].Id).NativeId == "VITAL_SPARK_POWER"
                && battle.Powers[index].Amount > 0)
            {
                hasVitalSpark = true;
                break;
            }
        }
        if (!hasVitalSpark)
            battle.SetCounter(Counter.VitalSparkNormalizedAmount, int.MinValue);
        for (int index = 0; index < battle.CardCount; index++)
            battle.Cards[index].Keywords &= ~Keywords.CreatedAfterRoot;
        // Opening heals (Pantograph, BloodVial) are not combat recovery: the native live importer
        // starts the search ledger at zero. Kernel.Heal would otherwise add them to RecoveredHp.
        battle.SetCounter(Counter.RecoveredHp, 0);
        // The post-entry RunState sees the same vitals the opening Battle has (first-turn heals and
        // any combat-start max-HP change included).
        next.Player.CurrentHp = battle.Player.CurrentHp;
        next.Player.MaxHp = battle.Player.MaxHp;
        for (int slot = 0; slot < battle.PotionSlotCount; slot++)
        {
            if (battle.Potions[slot] != 0)
                next.SetPotion(slot, checked((ushort)(battle.Potions[slot] - 1)));
        }

        // The encounter's MonsterIds history records the generated monsters in creation order.
        ref RunHistoryEntry entry = ref next.History.Entries[next.History.Count - 1];
        ref RunHistoryRoom room = ref entry.Rooms[0];
        for (int index = 0; index < spawnCount; index++)
        {
            if (room.MonsterCount >= RunLimits.HistoryMonsters)
                throw new CapacityException("RunHistory.Monsters", RunLimits.HistoryMonsters);
            room.Monsters[room.MonsterCount++] = battle.Enemies[index].MonsterId;
        }

        (Rules finalRules, RulesRootFacts finalFacts) = BuildFinalRules(table, in next, ref battle,
            encounterId, spawns, spawnCount, initialNodes, initialMoveLogged, ascension);
        for (int stream = 0; stream < Limits.RngStreams; stream++)
            next.Rng.Streams[(int)RunRngStream.Shuffle + stream] = battle.Rng[stream];
        return new CombatSetupResult
        {
            Run = next,
            Battle = battle,
            Rules = finalRules,
            Facts = finalFacts,
            EncounterId = encounterId,
        };
    }

    private static (string NativeId, int Upgrade)[] SetupExtraCards(in RunState run)
    {
        List<(string, int)> cards = [];
        for (int index = 0; index < run.Relics.Count; index++)
        {
            string nativeId = Registry.Relic(run.Relics.Items[index].Id).NativeId;
            if (nativeId == "FUNERARY_MASK")
                cards.Add(("SOUL", 0));
            else if (nativeId == "FENCING_MANUAL")
                cards.Add(("SOVEREIGN_BLADE", 0));
        }
        return [.. cards];
    }

    private static void RejectUnsupportedRelics(in RunState run)
    {
        for (int index = 0; index < run.Relics.Count; index++)
        {
            string nativeId = Registry.Relic(run.Relics.Items[index].Id).NativeId;
            if (nativeId is "TOOLBOX" or "DELICATE_FROND" or "VEXING_PUZZLEBOX"
                or "GAMBLING_CHIP" or "TINY_MAILBOX" or "GNARLED_HAMMER" or "MYSTIC_LIGHTER"
                or "BIG_HAT" or "MAW_BANK" or "ORANGE_DOUGH" or "INFUSED_CORE"
                or "TOY_BOX" or "TEA_OF_DISCOURTESY" or "FAKE_BLOOD_VIAL"
                or "JEWELED_MASK" or "ROYAL_POISON" or "BOOMING_CONCH"
                or "TOASTY_MITTENS" or "CHOICES_PARADOX" or "PAELS_LEGION" or "BIIIG_HUG")
            {
                throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
                    $"遗物 {nativeId} 的开战/第一回合效果尚未实现。");
            }
        }
    }

    private static void ApplyTurnStartRelicState(ref RunState run, in Battle battle)
    {
        for (int index = 0; index < run.Relics.Count; index++)
        {
            ref RunRelic relic = ref run.Relics.Items[index];
            string nativeId = Registry.Relic(relic.Id).NativeId;
            if (nativeId == "METRONOME")
            {
                relic.DisplayAmount = battle.OrbCount;
                relic.Status = RunRelicStatus.Normal;
                continue;
            }
            switch (nativeId)
            {
                case "HAPPY_FLOWER":
                {
                    int turns = (int)Registry.Relic(relic.Id).VarNames.Count;
                    _ = turns;
                    int value = battle.Relics[index].Counter;
                    relic.DisplayAmount = value;
                    relic.Properties = default;
                    RunProperties.Init(ref relic.Properties);
                    RunProperties.AddInt(ref relic.Properties,
                        RunProperties.NameHash("TurnsSeen"), value, RunLimits.RelicPropertyBytes);
                    relic.Status = value == 2 ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                }
                case "PENDULUM":
                {
                    int value = battle.Relics[index].Counter;
                    relic.DisplayAmount = value;
                    relic.Properties = default;
                    RunProperties.Init(ref relic.Properties);
                    RunProperties.AddInt(ref relic.Properties,
                        RunProperties.NameHash("TurnsSeen"), value, RunLimits.RelicPropertyBytes);
                    relic.Status = value == 2 ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                }
                case "ART_OF_WAR":
                    relic.Status = RunRelicStatus.Active;
                    break;
                case "POCKETWATCH":
                    relic.Status = RunRelicStatus.Active;
                    break;
                case "STONE_CALENDAR":
                    relic.DisplayAmount = battle.PlayerTurn;
                    relic.Status = RunRelicStatus.Normal;
                    break;
                case "RIPPLE_BASIN":
                    relic.Status = RunRelicStatus.Active;
                    break;
                case "EMBER_TEA":
                {
                    int value = Math.Max(0, relic.DisplayAmount - 1);
                    relic.DisplayAmount = value;
                    relic.Properties = default;
                    RunProperties.Init(ref relic.Properties);
                    RunProperties.AddInt(ref relic.Properties,
                        RunProperties.NameHash("CombatsLeft"), value, RunLimits.RelicPropertyBytes);
                    relic.Status = value > 0 ? RunRelicStatus.Normal : RunRelicStatus.Disabled;
                    break;
                }
            }
        }
    }

    private static Battle CreateBattleShell(in RunState run, ContentTable table)
    {
        Battle battle = default;
        battle.Round = 1;
        battle.PlayerTurn = 1;
        battle.Side = Side.Player;
        battle.Phase = PlayerPhase.Start;
        battle.Outcome = Outcome.None;
        battle.MaxHandSize = Limits.MaxHandSize;
        battle.Gold = run.Player.Gold;
        battle.NextCombatId = 1;
        battle.Player = new Creature
        {
            CurrentHp = run.Player.CurrentHp,
            MaxHp = run.Player.MaxHp,
            Flags = CreatureFlags.Present,
        };
        battle.Flags = BattleFlags.PlayerHooksActive;
        battle.SetCounter(Counter.VitalSparkNormalizedAmount, int.MinValue);
        battle.OrbCapacity = checked((byte)run.Player.BaseOrbSlots);
        battle.PotionSlotCount = run.Player.Potions.Count;
        for (int slot = 0; slot < run.Player.Potions.Count; slot++)
        {
            RunPotion potion = run.Player.Potions.Items[slot];
            if (potion.Present != 0)
            {
                if (potion.Id >= Registry.PotionCount)
                    throw new ContentUnavailableException(ContentReason.UnsupportedPotion,
                        $"药水 id {potion.Id} 超出内容表。");
                battle.Potions[slot] = checked((ushort)(potion.Id + 1));
            }
        }
        for (int index = 0; index < run.Relics.Count; index++)
        {
            RunRelic relic = run.Relics.Items[index];
            battle.Relics[index] = new Relic
            {
                Id = relic.Id,
                Flags = (relic.Flags & RunRelicFlags.Melted) != 0 ? RelicFlags.Melted : RelicFlags.None,
            };
        }
        battle.RelicCount = run.Relics.Count;
        if (run.Deck.Count > Limits.Cards)
            throw new CapacityException("Battle.Cards", Limits.Cards);
        for (int index = 0; index < run.Deck.Count; index++)
        {
            battle.Cards[index] = CreateCard(table, run.Deck.Cards[index]);
        }
        battle.DrawCount = checked((byte)run.Deck.Count);
        for (int stream = 0; stream < Limits.RngStreams; stream++)
            battle.Rng[stream] = run.Rng.Streams[(int)RunRngStream.Shuffle + stream];
        CombatStart.ShuffleInitialDrawPile(ref battle);
        return battle;
    }

    private static Card CreateCard(ContentTable table, in RunCard source)
    {
        ContentCard content = table.Card(source.Id);
        int upgrade = source.Upgrade;
        if (upgrade >= content.Levels.Length)
            throw new ContentUnavailableException(ContentReason.UnsupportedCard, $"卡牌 {source.Id} 的升级等级 {upgrade} 超出内容表。");
        CardStats stats = content.Levels[upgrade];
        Card card = new()
        {
            Id = source.Id,
            Upgrade = source.Upgrade,
            BaseCost = checked((sbyte)stats.EnergyCost),
            BaseStarCost = checked((sbyte)stats.StarCost),
            ReplayCount = 0,
            Keywords = stats.Keywords,
            Flags = CardFlags.DeckCard | CardFlags.PowerAfflictionChecked,
        };
        if (stats.CostsX)
            card.Flags |= CardFlags.CostsX;
        if (stats.StarCostsX)
            card.Flags |= CardFlags.StarCostsX;
        if (source.HasEnchantment != 0)
        {
            if (source.EnchantmentId >= Registry.EnchantmentCount)
                throw new ContentUnavailableException(ContentReason.UnsupportedEnchantment,
                    $"附魔 id {source.EnchantmentId} 超出内容表。");
            card.EnchantmentId = checked((byte)(source.EnchantmentId + 1));
            card.EnchantmentAmount = source.EnchantmentAmount;
        }
        if (Registry.Card(source.Id).UsesIdentityLedger)
            card.Private = Limits.UninitializedLedger;
        return card;
    }

    private static int[] ConditionalTargets(ContentTable table, ContentEncounter encounter,
        in EncounterSpawn spawn, int ascension)
    {
        string? slot = spawn.Slot == Kernel.NoSlot || spawn.Slot >= encounter.Slots.Length
            ? null
            : encounter.Slots[spawn.Slot];
        MonsterRules rules = RulesFor(table, encounter, spawn.MonsterId, slot, ascension);
        int[] targets = new int[rules.Nodes.Length];
        Array.Fill(targets, RulesRootFacts.UnknownConditionalTarget);
        MonsterDef def = Registry.Monster(spawn.MonsterId);
        for (int node = 0; node < rules.Nodes.Length; node++)
        {
            if (rules.Nodes[node].Kind != MonsterNodeKind.ConditionalBranch)
                continue;
            int target = def.CombatStartConditional(spawn.Variant, in rules, node);
            if (target >= 0)
                targets[node] = target;
            else
                targets[node] = rules.Nodes[node].ConditionalTarget;
        }
        return targets;
    }

    private static byte InitialNodeFor(ContentTable table, ContentEncounter encounter,
        in EncounterSpawn spawn, int ascension)
    {
        string? slot = spawn.Slot == Kernel.NoSlot || spawn.Slot >= encounter.Slots.Length
            ? null
            : encounter.Slots[spawn.Slot];
        MonsterRules rules = RulesFor(table, encounter, spawn.MonsterId, slot, ascension);
        string? move = Registry.Monster(spawn.MonsterId).CombatStartMove(spawn.Variant);
        if (move == null)
            return RulesRootFacts.UnknownInitialNode;
        return checked((byte)rules.NodeIndex(move));
    }

    private static bool InitialMoveLoggedFor(ContentTable table, ContentEncounter encounter,
        in EncounterSpawn spawn, int ascension)
    {
        string? slot = spawn.Slot == Kernel.NoSlot || spawn.Slot >= encounter.Slots.Length
            ? null
            : encounter.Slots[spawn.Slot];
        MonsterRules rules = RulesFor(table, encounter, spawn.MonsterId, slot, ascension);
        string? move = Registry.Monster(spawn.MonsterId).CombatStartMove(spawn.Variant);
        if (move != null)
            return rules.Nodes[rules.NodeIndex(move)].Kind == MonsterNodeKind.Move;
        return rules.InitialMoveLogged;
    }

    private static ContentEncounterMonster EncounterMonster(ContentEncounter encounter,
        ushort monsterId, string? slot)
    {
        foreach (ContentEncounterMonster entry in encounter.Monsters)
        {
            if (entry.MonsterId == monsterId
                && string.Equals(entry.Slot, slot, StringComparison.Ordinal))
                return entry;
        }
        throw new ContentUnavailableException(ContentReason.UnsupportedMonster,
            $"内容表没有遭遇怪物 {Registry.Monster(monsterId).NativeId} 的槽位 {slot ?? "<none>"}。");
    }

    private static MonsterRules RulesFor(ContentTable table, ContentEncounter encounter,
        ushort monsterId, string? slot, int ascension)
    {
        foreach (ContentEncounterMonster entry in encounter.Monsters)
        {
            if (entry.MonsterId == monsterId && string.Equals(entry.Slot, slot, StringComparison.Ordinal))
                return entry.Rules(ascension);
        }
        return table.SpawnMonster(monsterId, ascension);
    }

    private static (Rules Rules, RulesRootFacts Facts) BuildFinalRules(ContentTable table,
        in RunState run, ref Battle battle, ushort encounterId, Span<EncounterSpawn> spawns, int spawnCount,
        byte[] initialNodes, bool[] initialMoveLogged, int ascension)
    {
        ContentEncounter encounter = table.Encounter(encounterId);
        int[][] conditional = new int[spawnCount][];
        MonsterAttack[]?[][] attacks = new MonsterAttack[]?[spawnCount][];
        for (int index = 0; index < spawnCount; index++)
            conditional[index] = ConditionalTargets(table, encounter, spawns[index], ascension);
        RulesRootFacts baseFacts = new()
        {
            EnemyInitialNodes = initialNodes,
            EnemyConditionalTargets = conditional,
            EnemyNodeAttacks = [],
            EnemyRootPowerAmounts = [],
            EnemyInitialMoveLogged = initialMoveLogged,
            ExtraCards = SetupExtraCards(in run),
        };
        Rules baseRules = RulesBuilder.Build(table, in run, in battle, encounterId, baseFacts);
        PlayerAction noAction = default;
        Exec x = new(ref battle, baseRules, in noAction);
        for (int index = 0; index < spawnCount; index++)
        {
            byte owner = checked((byte)(Limits.FirstEnemyOwner + index));
            string? slot = spawns[index].Slot == Kernel.NoSlot
                ? null
                : encounter.Slots[spawns[index].Slot];
            MonsterRules enemyRules = RulesFor(table, encounter, spawns[index].MonsterId, slot, ascension);
            attacks[index] = new MonsterAttack[]?[enemyRules.Nodes.Length];
            for (int node = 0; node < enemyRules.Nodes.Length; node++)
            {
                attacks[index][node] = enemyRules.Nodes[node].Attacks;
                if (enemyRules.Nodes[node].Kind != MonsterNodeKind.ConditionalBranch)
                    continue;
                int target = Registry.Monster(spawns[index].MonsterId)
                    .CombatStartConditional(spawns[index].Variant, in enemyRules, node);
                if (target >= 0)
                {
                    conditional[index][node] = target;
                    continue;
                }
                if (enemyRules.Nodes[node].NativeId == "INIT_MOVE"
                    && enemyRules.Nodes[node].ConditionalTarget < 0)
                {
                    conditional[index][node] = -1;
                    continue;
                }
                int live = Registry.Monster(spawns[index].MonsterId)
                    .ResolveConditional(ref x, owner, node);
                if (live >= 0)
                    conditional[index][node] = live;
            }
        }
        RulesRootFacts facts = new()
        {
            EnemyInitialNodes = initialNodes,
            EnemyConditionalTargets = conditional,
            EnemyNodeAttacks = attacks,
            EnemyRootPowerAmounts = [],
            EnemyInitialMoveLogged = initialMoveLogged,
            ExtraCards = SetupExtraCards(in run),
        };
        Rules rules = RulesBuilder.Build(table, in run, in battle, encounterId, facts);
        return (rules, facts);
    }

    private static RunCoord FindPointCoord(in RunState run, RunMapPointType type)
    {
        for (int index = 0; index < run.Map.NodeCount; index++)
        {
            RunMapPoint point = run.Map.Nodes[index];
            if (point.Type == type && !ContainsCoord(in run, point.Coord))
                return point.Coord;
        }
        if (type == RunMapPointType.Boss)
            return run.Map.Boss;
        throw new ContentUnavailableException(ContentReason.UnsupportedRoom, $"地图没有未访问的 {type} 点。");
    }

    private static bool ContainsCoord(in RunState run, RunCoord coord)
    {
        for (int index = 0; index < run.Visited.CoordCount; index++)
        {
            if (run.Visited.Coords[index].Equals(coord))
                return true;
        }
        return false;
    }
}
