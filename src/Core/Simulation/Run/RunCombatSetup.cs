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
/// One unanswered opening choice. <see cref="Request"/> is the kernel's request against the partial
/// battle (which is discarded); <see cref="Index"/> is the number of choices already consumed, so the
/// caller appends the next bit set and replays from the pre-entry state.
/// </summary>
public sealed class CombatSetupChoice
{
    public required ChoiceRequest Request { get; init; }

    public required int Index { get; init; }

    public required ushort EncounterId { get; init; }
}

/// <summary>Either the complete opening build or the next choice it needs.</summary>
public sealed class CombatSetupAttempt
{
    public CombatSetupResult? Result { get; init; }

    public CombatSetupChoice? Pending { get; init; }

    public bool IsPending => Result == null;
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
        => EnterMapCoord(in run, table, coord, default);

    /// <summary>
    /// Replays the opening build from the pre-entry run state with a complete choice sequence. A
    /// pending request throws instead of returning a half-built battle; callers that enumerate
    /// choices use <see cref="TryEnterMapCoord"/>.
    /// </summary>
    public static CombatSetupResult EnterMapCoord(in RunState run, ContentTable table, RunCoord coord,
        scoped ReadOnlySpan<ulong> choices)
    {
        CombatSetupAttempt attempt = TryEnterMapCoord(in run, table, coord, choices);
        if (attempt.Pending is { } pending)
        {
            throw new UnsupportedException(
                $"坐标 {coord} 的开战选择 {pending.Index} 尚未回答（{pending.Request.Kind}，" +
                $"{pending.Request.MinCount}..{pending.Request.MaxCount} 个选项）。");
        }
        return attempt.Result!;
    }

    /// <summary>One replay attempt; <see cref="CombatSetupAttempt.Pending"/> carries the next request.</summary>
    private static void RequireRunSetupHooks(in RunState run, ContentTable table)
    {
        for (int index = 0; index < run.Relics.Count; index++)
        {
            RunRelic relic = run.Relics.Items[index];
            uint unsupported = table.RelicSetupHooks[relic.Id].Unsupported;
            if (unsupported != 0)
                throw SetupHookFailure("遗物", relic.Id, unsupported);
        }
        for (int index = 0; index < run.Deck.Count; index++)
        {
            RunCard card = run.Deck.Cards[index];
            uint unsupported = table.CardSetupHooks[card.Id].Unsupported;
            if (card.HasEnchantment != 0)
                unsupported |= table.EnchantmentSetupHooks[card.EnchantmentId].Unsupported;
            if (unsupported != 0)
                throw SetupHookFailure("卡牌", card.Id, unsupported);
        }
    }

    private static void RequirePowerSetupHooks(in Battle battle, ContentTable table)
    {
        for (int index = 0; index < battle.PowerCount; index++)
        {
            uint unsupported = table.PowerSetupHooks[battle.Powers[index].Id].Unsupported;
            if (unsupported != 0)
                throw SetupHookFailure("Power", battle.Powers[index].Id, unsupported);
        }
    }

    private static ContentUnavailableException SetupHookFailure(string kind, ushort id,
        uint unsupported)
    {
        string name = kind switch
        {
            "遗物" => id < Registry.RelicCount ? Registry.Relic(id).NativeId : id.ToString(),
            "卡牌" => id < Registry.CardCount ? Registry.Card(id).NativeId : id.ToString(),
            "Power" => id < Registry.PowerCount ? Registry.Power(id).NativeId : id.ToString(),
            _ => id.ToString(),
        };
        string hooks = string.Join(",", Enum.GetValues<ContentSetupHook>()
            .Where(hook => hook != ContentSetupHook.None && (unsupported & (uint)hook) != 0));
        return new ContentUnavailableException(ContentReason.SetupHookListener,
            $"{kind} {name} 重写了未实现的建局 Hook：{hooks}。");
    }

    public static CombatSetupAttempt TryEnterMapCoord(in RunState run, ContentTable table,
        RunCoord coord, scoped ReadOnlySpan<ulong> choices)
    {
        RunMapEntryResult entry = RunMapEntry.Enter(in run, table, coord, null, choices);
        if (entry.CombatChoice is { } pending)
            return new CombatSetupAttempt { Pending = pending };
        return new CombatSetupAttempt
        {
            Result = entry.Combat ?? throw new ContentUnavailableException(
                ContentReason.UnsupportedRoom,
                $"坐标 {coord} 生成了 {entry.RoomType} 房间，不是战斗房间。"),
        };
    }

    /// <summary>
    /// Native <c>EnterMapPointInternal</c> without a room-stack base, addressed by point type.
    /// Kept for the R2a-2 callers; the shared implementation is <see cref="RunMapEntry"/>.
    /// </summary>
    public static CombatSetupResult EnterMapPoint(in RunState run, ContentTable table, int actFloor,
        RunMapPointType pointType)
        => EnterMapPoint(in run, table, actFloor, pointType, default);

    public static CombatSetupResult EnterMapPoint(in RunState run, ContentTable table, int actFloor,
        RunMapPointType pointType, scoped ReadOnlySpan<ulong> choices)
    {
        RunMapEntryResult entry = RunMapEntry.Enter(in run, table,
            FindPointCoord(in run, pointType), actFloor, choices);
        if (entry.CombatChoice is { } pending)
        {
            throw new UnsupportedException(
                $"{pointType} 的开战选择 {pending.Index} 尚未回答（{pending.Request.Kind}）。");
        }
        return entry.Combat ?? throw new ContentUnavailableException(ContentReason.UnsupportedRoom,
            $"{pointType} 生成了 {entry.RoomType} 房间，不是战斗房间。");
    }

    /// <summary>
    /// Battle half of the combat boundary. The caller (<see cref="RunMapEntry"/>) has already
    /// appended the map-point history and pushed the combat room, matching the native order where
    /// the room is created and recorded before <c>EnterRoomInternal</c>.
    /// </summary>
    internal static CombatSetupAttempt BuildCombat(ref RunState next, ContentTable table,
        RunRoomType roomType, ushort encounterId, scoped ReadOnlySpan<ulong> choices)
    {
        if (choices.Length > RunLimits.CombatSetupChoices)
            throw new CapacityException("Run.CombatSetupChoices", RunLimits.CombatSetupChoices);
        PlayerAction startupAction = default;
        for (int index = 0; index < choices.Length; index++)
            startupAction = startupAction.WithChoice(choices[index]);
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
        RequireRunSetupHooks(in next, table);
        // Native Hook.AfterRoomEntered gains MawBank's gold for the base room before combat start;
        // ApplyRelicCounterState later copies battle.Gold back, so the opening Battle must carry it.
        GainMawBankGold(ref next, table);
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
        Rules provisional = RulesBuilder.Build(table, in next, in battle, encounterId,
            provisionalFacts, includeSetupContent: true);

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

        // One execution context spans the room-entry, combat-start and first-turn passes so the
        // choice sequence is consumed in native order; a pending choice discards the partial battle
        // and the caller replays from the pre-entry RunState with one more bit set appended.
        bool elite = roomType == RunRoomType.Elite;
        bool boss = roomType == RunRoomType.Boss;
        Exec startup = new(ref battle, provisional, in startupAction)
        {
            CombatStart = true,
            SetupContent = table,
            FirstRoomIsElite = elite,
            FirstRoomIsBoss = boss,
        };
        CombatStart.AfterRoomEntered(ref startup, elite, boss);
        if (startup.NeedsChoice)
            return Pending(in startup, encounterId);
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
        RequirePowerSetupHooks(in battle, table);
        for (int index = 0; index < next.Relics.Count; index++)
        {
            RunRelic relic = next.Relics.Items[index];
            sbyte status = Registry.Relic(relic.Id)
                .RoomEnteredStatus(in battle, relic.Id, table, elite, boss);
            if (status >= 0)
                next.Relics.Items[index].Status = (RunRelicStatus)status;
        }
        CombatStart.BeforeCombatStart(ref startup, boss);
        if (startup.NeedsChoice)
            return Pending(in startup, encounterId);
        CombatStart.BeforeCombatStartLate(ref startup, boss);
        if (startup.NeedsChoice)
            return Pending(in startup, encounterId);
        CombatStart.StartFirstTurn(ref startup);
        if (startup.NeedsChoice)
            return Pending(in startup, encounterId);
        if (!startup.AllChoicesConsumed)
            throw new InvalidOperationException("开战选择序列有未消费的选项。");
        if ((battle.Flags & BattleFlags.OstyPresent) != 0)
            battle.Flags |= BattleFlags.OstyWasRootPet;
        PruneSetupDeadExtras(ref battle, spawnCount);
        ApplyTurnStartRelicState(ref next, in battle, table);
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
        next.Player.Gold = battle.Gold;
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
        return new CombatSetupAttempt
        {
            Result = new CombatSetupResult
            {
                Run = next,
                Battle = battle,
                Rules = finalRules,
                Facts = finalFacts,
                EncounterId = encounterId,
            },
        };
    }

    private static CombatSetupAttempt Pending(in Exec x, ushort encounterId)
        => new()
        {
            Pending = new CombatSetupChoice
            {
                Request = x.Request,
                Index = x.ChoicesTaken,
                EncounterId = encounterId,
            },
        };

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

    /// <summary>
    /// Monsters spawned during BeforeCombatStart can die before the first play phase. Native
    /// CombatState.Enemies is the live roster and does not retain such a setup-spawned corpse;
    /// Blitz keeps known enemies for writeback history, so trim trailing dead extras before
    /// building the root rules/battle.
    /// </summary>
    private static void PruneSetupDeadExtras(ref Battle battle, int spawnCount)
    {
        while (battle.EnemyCount > spawnCount)
        {
            int last = battle.EnemyCount - 1;
            if (battle.Enemies[last].DeathPhase != DeathPhase.PermanentlyDead)
                break;
            Kernel.RemoveFromRoster(ref battle, checked((byte)(Limits.FirstEnemyOwner + last)));
            battle.EnemyCount--;
            battle.Enemies[last] = default;
        }
    }

    private static void ApplyTurnStartRelicState(ref RunState run, in Battle battle,
        ContentTable table)
    {
        for (int index = 0; index < run.Relics.Count; index++)
        {
            ref RunRelic relic = ref run.Relics.Items[index];
            string nativeId = Registry.Relic(relic.Id).NativeId;
            if (nativeId == "METRONOME")
            {
                // Native DisplayAmount is the total channels (_orbsChanneled), not the queue size.
                relic.DisplayAmount = battle.Counter(Counter.OrbChannels);
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
                case "FAKE_HAPPY_FLOWER":
                {
                    int value = battle.Relics[index].Counter;
                    relic.DisplayAmount = value;
                    relic.Properties = default;
                    RunProperties.Init(ref relic.Properties);
                    RunProperties.AddInt(ref relic.Properties,
                        RunProperties.NameHash("TurnsSeen"), value, RunLimits.RelicPropertyBytes);
                    relic.Status = value == (int)table.RelicVar(relic.Id, "Turns") - 1
                        ? RunRelicStatus.Active : RunRelicStatus.Normal;
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
                case "TEA_OF_DISCOURTESY":
                {
                    // BeforeCombatStart spent one combat and decremented Battle.Relics.Counter.
                    int value = Math.Max(0, battle.Relics[index].Counter);
                    relic.DisplayAmount = 0;
                    relic.Properties = default;
                    RunProperties.Init(ref relic.Properties);
                    RunProperties.AddInt(ref relic.Properties,
                        RunProperties.NameHash("CombatsLeft"), value, RunLimits.RelicPropertyBytes);
                    relic.Status = value > 0 ? RunRelicStatus.Normal : RunRelicStatus.Disabled;
                    break;
                }
                case "BONE_TEA":
                {
                    int value = Math.Max(0, battle.Relics[index].Counter);
                    relic.DisplayAmount = value;
                    relic.Properties = default;
                    RunProperties.Init(ref relic.Properties);
                    RunProperties.AddInt(ref relic.Properties,
                        RunProperties.NameHash("CombatsLeft"), value, RunLimits.RelicPropertyBytes);
                    relic.Status = value > 0 ? RunRelicStatus.Normal : RunRelicStatus.Disabled;
                    break;
                }
                case "PAELS_LEGION":
                    // AfterSideTurnStart decremented the cooldown; the native status follows it.
                    relic.Status = battle.Relics[index].Counter <= 0
                        ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                case "PAELS_EYE":
                    // Native AfterSideTurnStart marks the eye Active unless it was already spent.
                    relic.Status = battle.Relics[index].Counter == 0
                        ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
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
            if (Registry.Relic(relic.Id).NativeId == "PUMPKIN_CANDLE")
                battle.Relics[index].Counter = relic.DisplayAmount;
            if (Registry.Relic(relic.Id).NativeId is "TEA_OF_DISCOURTESY" or "BONE_TEA")
            {
                // The run-side CombatsLeft decides whether the first-turn effect fires and is
                // decremented there; the native battle relic has no counter of its own.
                battle.Relics[index].Counter = ReadRelicInt(in relic, "CombatsLeft");
            }
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

    /// <summary>Native <c>MawBank.AfterRoomEntered</c> at the first room of a map point.</summary>
    private static void GainMawBankGold(ref RunState state, ContentTable table)
    {
        if (state.Rooms.Count != 1)
            return;
        for (int index = 0; index < state.Relics.Count; index++)
        {
            ref RunRelic relic = ref state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            if (Registry.Relic(relic.Id).NativeId != "MAW_BANK"
                || ReadRelicBool(in relic, "HasItemBeenBought"))
            {
                continue;
            }
            GainGold(ref state, table, RelicIntVar(table, relic.Id, "Gold"));
        }
    }

    private static void GainGold(ref RunState state, ContentTable table, int amount)
    {
        decimal value = amount;
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            switch (Registry.Relic(relic.Id).NativeId)
            {
                case "BOWLER_HAT":
                    value *= table.RelicVar(relic.Id, "GoldIncrease");
                    break;
                case "ECTOPLASM":
                    value = 0m;
                    break;
            }
        }
        if (value <= 0m)
            return;
        state.Player.Gold += (int)value;
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            if (Registry.Relic(relic.Id).NativeId == "DRAGON_FRUIT")
                GainMaxHp(ref state, RelicIntVar(table, relic.Id, "MaxHp"));
        }
    }

    private static void GainMaxHp(ref RunState state, int amount)
    {
        if (amount <= 0)
            return;
        state.Player.MaxHp += amount;
        state.Player.CurrentHp = Math.Min(state.Player.MaxHp,
            state.Player.CurrentHp + amount);
    }

    private static int RelicIntVar(ContentTable table, ushort relicId, string name)
        => checked((int)table.RelicVar(relicId, name));

    private static int ReadRelicInt(in RunRelic relic, string name)
        => RunProperties.TryGetInt(relic.Properties, RunProperties.NameHash(name), out int value)
            ? value : 0;

    private static bool ReadRelicBool(in RunRelic relic, string name)
        => RunProperties.TryGetBool(relic.Properties, RunProperties.NameHash(name), out bool value)
            && value;

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
