using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Post-combat write-back at the native endpoint: after every <c>AfterCombatEnd</c> /
/// <c>AfterCombatVictory</c> hook and before reward generation. The input is the run state captured
/// when the map point was entered plus the terminal battle; the result is the run state the next
/// reward decision would see.
/// <para>
/// The hook order mirrors <c>CombatManager.EndCombatInternal</c>: deck-card listeners first, then the
/// player's powers, then run relics in run order, then the two <c>AfterCombatVictory</c> passes. A
/// player death uses the native loss endpoint, which runs no post-combat hooks.
/// </para>
/// </summary>
public static class RunCombatWriteback
{
    private const int DetachedCardCapacity = Limits.DetachedCards;

    // Native method-body constants, not DynamicVars: Guilty.cs "CombatsSeen >= 5" and
    // PenNib.cs "AttacksPlayed % 10". Listed in the R2a-3 report for version review.
    private const int GuiltyCombatsBeforeRemoval = 5;
    private const int PenNibCycle = 10;

    public static RunState Apply(in RunState source, in Battle battle, ContentTable table)
    {
        if (battle.Outcome == Outcome.None)
            throw new InvalidDataException("战斗尚未结束，不能写回 RunState。");
        RunState run = source;
        RunRoomType roomType = CurrentRoomType(in run);
        bool victory = battle.Outcome == Outcome.Victory && !battle.Player.IsDead;
        int sourceGold = run.Player.Gold;

        run.Player.CurrentHp = battle.Player.CurrentHp;
        run.Player.MaxHp = battle.Player.MaxHp;
        run.Player.Gold = battle.Gold;
        for (int slot = 0; slot < run.Player.Potions.Count; slot++)
        {
            ushort stored = slot < battle.PotionSlotCount ? battle.Potions[slot] : (ushort)0;
            if (stored == 0)
                run.Player.Potions.Items[slot] = default;
            else
                run.Player.Potions.Items[slot] = new RunPotion { Id = checked((ushort)(stored - 1)), Present = 1 };
        }
        for (int stream = 0; stream < Limits.RngStreams; stream++)
            run.Rng.Streams[(int)RunRngStream.Shuffle + stream] = battle.Rng[stream];

        // Theft removed run-deck cards during combat; the detached values still carry DeckCard.
        // A card still referenced by SwipePower is still held; an unreferenced detached deck card
        // was returned by SwipePower.BeforeDeath and native appends it to the deck end.
        // Capture the returned-card reward while the deck card is still present; the theft
        // removal below represents native CardPileCmd.RemoveFromDeck. SwipePower.BeforeDeath
        // adds the card to the run card scope, not the player's deck, so the deck entry still
        // leaves now and the special-card reward re-adds it when claimed.
        ApplyCombatRewards(ref run, in battle, table, roomType, sourceGold);
        ApplyStolenDeckCards(ref run, in battle);
        ApplyPersistentGrowth(ref run, in battle, table);
        if (victory)
        {
            ApplyDeckAfterCombatEnd(ref run);
            ApplyPowerAfterCombatEnd(ref run, in battle, table);
            ApplyRelicAfterCombatEnd(ref run, in battle, table, roomType);
            ApplyRelicAfterCombatVictory(ref run, in battle, table, roomType);
        }
        bool finished = battle.Outcome is Outcome.Victory or Outcome.EventDefeat
            || !battle.Player.IsDead;
        for (int enemy = 0; enemy < battle.EnemyCount; enemy++)
        {
            if (Registry.Monster(battle.Enemies[enemy].MonsterId).NativeId != "TEST_SUBJECT")
                continue;
            // TestSubject.Kills is incremented on every death trigger; the AI register is the
            // revive count, and the final kill on a victory is one more death.
            int kills = battle.Enemies[enemy].Ai[0] + (battle.Outcome == Outcome.Victory ? 1 : 0);
            run.Extra.TestSubjectKills += kills;
        }
        ApplyRelicCounterState(ref run, in battle, table);
        if (finished)
            ApplyRelicFinalState(ref run, in battle, table);
        else
            ApplyDefeatRelicStatus(ref run, in battle, table);

        if (run.History.Count != 0)
        {
            ref RunHistoryEntry endEntry = ref run.History.Entries[run.History.Count - 1];
            if (endEntry.RoomCount != 0)
            {
                ref RunHistoryRoom endRoom = ref endEntry.Rooms[endEntry.RoomCount - 1];
                SyncHistoryMonsters(ref endRoom, in battle);
            }
        }
        if (finished)
        {
            if (run.Rooms.Count != 0)
            {
                RunRoom finishedRoom = run.Rooms.Items[run.Rooms.Count - 1];
                run.Rooms.Items[run.Rooms.Count - 1].GoldProportion =
                    battle.Outcome == Outcome.Victory
                        ? GoldProportion(in battle, table.Encounter(finishedRoom.Model.Id))
                        : 0f;
            }
            if (run.History.Count != 0)
            {
                ref RunHistoryEntry entry = ref run.History.Entries[run.History.Count - 1];
                if (entry.RoomCount != 0)
                    entry.Rooms[entry.RoomCount - 1].TurnsTaken = battle.PlayerTurn;
            }
            if (run.Rooms.Count != 0)
                run.Rooms.Items[run.Rooms.Count - 1].IsPreFinished = 1;
            run.Decision = RunDecision.None;
            run.DecisionOptionCount = 0;
        }
        else if (battle.Player.IsDead)
        {
            run.Player.CurrentHp = 0;
            run.Decision = RunDecision.GameOver;
            run.DecisionOptionCount = 0;
        }
        else
        {
            run.Decision = RunDecision.None;
            run.DecisionOptionCount = 0;
        }
        run.Canonicalize();
        return run;
    }

    private static void ApplyCombatRewards(ref RunState run, in Battle battle, ContentTable table,
        RunRoomType roomType, int sourceGold)
    {
        // SwipePower.BeforeDeath appends the returned card and one SpecialCardReward per return.
        for (int slot = 0; slot < DetachedCardCapacity; slot++)
        {
            Card detached = battle.DetachedCards[slot];
            if (detached.Id == 0 || (detached.Flags & CardFlags.DeckCard) == 0)
                continue;
            if (IsDetachedCardHeld(in battle, slot)
                || (detached.Flags & CardFlags.RemovedFromState) != 0)
            {
                continue;
            }
            int match = FindDeckCard(run, detached.Id, detached.Upgrade);
            if (match < 0)
                continue;
            RunCard card = run.Deck.Cards[match];
            RunReward reward = default;
            reward.Type = RunRewardType.SpecialCard;
            reward.OptionStart = run.ReserveRewardCards(1);
            reward.OptionCount = 1;
            reward.CardCount = 1;
            reward.Flags = RunRewardFlags.Populated;
            run.SetRewardCard(reward.OptionStart, card);
            run.AddReward(reward);
        }
        // HeistPower.BeforeDeath adds one GoldReward(amount, wasGoldStolenBack) on the fat
        // gremlin's death. Its owner is the enemy, so it is handled before the player-power pass.
        bool heistPower = false;
        if (battle.Outcome == Outcome.Victory)
        {
            for (int index = 0; index < battle.PowerCount; index++)
            {
                Power power = battle.Powers[index];
                if (Registry.Power(power.Id).NativeId != "HEIST_POWER")
                    continue;
                heistPower = true;
                RunReward reward = default;
                reward.Type = RunRewardType.Gold;
                reward.Amount = power.Amount;
                reward.AmountMax = power.Amount;
                reward.Flags = RunRewardFlags.Populated | RunRewardFlags.GoldStolenBack;
                run.AddReward(reward);
            }
        }
        if (!heistPower && battle.Outcome == Outcome.Victory
            && battle.Counter(Counter.OutstandingStolenGold) == 0
            && sourceGold > battle.Gold && run.Rooms.Count != 0)
        {
            RunRef model = run.Rooms.Items[run.Rooms.Count - 1].Model;
            if (model.Kind == RunRefKind.Encounter
                && table.Encounter(model.Id).NativeId.Contains("GREMLIN_MERC", StringComparison.Ordinal))
            {
                // HeistPower.BeforeDeath adds GoldReward(stolen, wasGoldStolenBack) when the fat
                // gremlin dies. The terminal Battle may already have dropped the power; the
                // source-vs-final gold delta is the stolen amount for this encounter.
                RunReward reward = default;
                reward.Type = RunRewardType.Gold;
                reward.Amount = sourceGold - battle.Gold;
                reward.AmountMax = reward.Amount;
                reward.Flags = RunRewardFlags.Populated | RunRewardFlags.GoldStolenBack;
                run.AddReward(reward);
            }
        }
        // Powers that append extra rewards in AfterCombatEnd / after a successful Hunt.
        for (int index = 0; index < battle.PowerCount; index++)
        {
            Power power = battle.Powers[index];
            if (power.Amount <= 0 || power.Owner != Limits.PlayerOwner)
                continue;
            string nativeId = Registry.Power(power.Id).NativeId;
            switch (nativeId)
            {
                case "ROYALTIES_POWER":
                {
                    RunReward reward = default;
                    reward.Type = RunRewardType.Gold;
                    reward.Amount = power.Amount;
                    reward.AmountMax = power.Amount;
                    reward.Flags = RunRewardFlags.Populated;
                    run.AddReward(reward);
                    break;
                }
                case "THE_HUNT_POWER":
                {
                    for (int count = 0; count < power.Amount; count++)
                    {
                        RunReward reward = default;
                        reward.Type = RunRewardType.Card;
                        reward.CardCount = 3;
                        reward.RaritySource = (byte)RoomRarity(roomType);
                        run.AddReward(reward);
                    }
                    break;
                }
                case "FORBIDDEN_GRIMOIRE_POWER":
                {
                    for (int count = 0; count < power.Amount; count++)
                    {
                        RunReward reward = default;
                        reward.Type = RunRewardType.CardRemoval;
                        reward.Flags = RunRewardFlags.Populated;
                        run.AddReward(reward);
                    }
                    break;
                }
            }
        }
    }

    private static RoomRarityOdds RoomRarity(RunRoomType roomType) => roomType switch
    {
        RunRoomType.Elite => RoomRarityOdds.Elite,
        RunRoomType.Boss => RoomRarityOdds.Boss,
        _ => RoomRarityOdds.Regular,
    };

    private static float GoldProportion(in Battle battle, ContentEncounter encounter)
    {
        if (!encounter.CustomGoldProportion)
        {
            int escaped = 0;
            for (int index = 0; index < battle.EnemyCount; index++)
            {
                if ((battle.Enemies[index].Flags & EnemyFlags.Escaped) != 0)
                    escaped++;
            }
            return battle.EnemyCount == 0 ? 1f : 1f - (float)escaped / battle.EnemyCount;
        }
        int escapedFat = 0;
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            if ((battle.Enemies[index].Flags & EnemyFlags.Escaped) != 0
                && Registry.Monster(battle.Enemies[index].MonsterId).NativeId == "FAT_GREMLIN")
            {
                escapedFat++;
            }
        }
        if (escapedFat == 0)
            return 1f;
        return battle.Counter(Counter.OutstandingStolenGold) > 0 ? 0f : 0.5f;
    }

    private static RunRoomType CurrentRoomType(in RunState run)
            => run.Rooms.Count == 0
                ? throw new InvalidDataException("写回时房间栈为空。")
                : run.Rooms.Items[run.Rooms.Count - 1].RoomType;

    /// <summary>
    /// Native <c>CreatureCmd.AddCreature</c> appends each enemy monster id to the history entry once.
    /// The initial spawns are already there; summons are appended from the known enemy table.
    /// </summary>
    private static void SyncHistoryMonsters(ref RunHistoryRoom room, in Battle battle)
    {
        for (int enemy = 0; enemy < battle.EnemyCount; enemy++)
        {
            ushort monster = battle.Enemies[enemy].MonsterId;
            bool present = false;
            for (int index = 0; index < room.MonsterCount; index++)
            {
                if (room.Monsters[index] == monster)
                {
                    present = true;
                    break;
                }
            }
            if (present)
                continue;
            if (room.MonsterCount >= RunLimits.HistoryMonsters)
                throw new CapacityException("RunHistory.Monsters", RunLimits.HistoryMonsters);
            room.Monsters[room.MonsterCount++] = monster;
        }
    }

    private static void ApplyStolenDeckCards(ref RunState run, in Battle battle)
    {
        for (int slot = 0; slot < DetachedCardCapacity; slot++)
        {
            Card detached = battle.DetachedCards[slot];
            if (detached.Id == 0 || (detached.Flags & CardFlags.DeckCard) == 0)
                continue;
            int match = FindDeckCard(run, detached.Id, detached.Upgrade);
            if (match < 0)
                continue;
            // A referenced card is still held. An unreferenced card that never lost its
            // RemovedFromState marker belonged to an escaped thief and is lost; a returned card
            // has the marker cleared by the death removal and native appended it to the deck end.
            // SwipePower.BeforeDeath adds the returned DeckVersion to the run card scope, not to
            // the player's deck; the card only re-enters the deck when the SpecialCardReward is
            // claimed. A still-held (escaped) card is likewise gone from the deck. Either way the
            // write-back must remove it here.
            _ = IsDetachedCardHeld(in battle, slot);
            RemoveDeckCardAt(ref run, match);
        }
    }

    private static bool IsDetachedCardHeld(in Battle battle, int slot)
    {
        for (int index = 0; index < battle.PowerCount; index++)
        {
            Power power = battle.Powers[index];
            if (power.Aux0 == slot + 1 && Registry.Power(power.Id).UsesDetachedCard)
                return true;
        }
        return false;
    }

    private static void ApplyPersistentGrowth(ref RunState run, in Battle battle, ContentTable table)
    {
        for (int index = 0; index < run.Deck.Count; index++)
        {
            ref RunCard runCard = ref run.Deck.Cards[index];
            string nativeId = Registry.Card(runCard.Id).NativeId;
            if (nativeId == "GENETIC_ALGORITHM")
            {
                ApplyGrowthProperty(ref runCard, in battle, "IncreasedBlock", "CurrentBlock",
                    checked((int)table.Card(runCard.Id, runCard.Upgrade).Vars[0]));
            }
            else if (nativeId == "THE_SCYTHE")
            {
                ApplyGrowthProperty(ref runCard, in battle, "IncreasedDamage", "CurrentDamage",
                    checked((int)table.Card(runCard.Id, runCard.Upgrade).Vars[0]));
            }
            if (nativeId == "GOOPY_CARD" || runCard.HasEnchantment == 0)
                continue;
            if (Registry.Enchantment(runCard.EnchantmentId).NativeId != "GOOPY")
                continue;
            int amount = runCard.EnchantmentAmount;
            for (int cardIndex = 0; cardIndex < battle.CardCount; cardIndex++)
            {
                Card card = battle.Cards[cardIndex];
                if ((card.Flags & CardFlags.DeckCard) == 0 || card.Id != runCard.Id
                    || card.EnchantmentId == 0)
                {
                    continue;
                }
                if (Registry.Enchantment((ushort)(card.EnchantmentId - 1)).NativeId != "GOOPY")
                    continue;
                if (card.EnchantmentAmount > amount)
                    amount = card.EnchantmentAmount;
            }
            runCard.EnchantmentAmount = checked((short)amount);
        }
    }

    private static void ApplyGrowthProperty(ref RunCard card, in Battle battle, string propertyName,
        string currentName, int baseValue)
    {
        ulong hash = RunProperties.NameHash(propertyName);
        int stored = RunProperties.TryGetInt(card.Properties, hash, out int value) ? value : 0;
        int best = stored;
        for (int index = 0; index < battle.CardCount; index++)
        {
            Card candidate = battle.Cards[index];
            if ((candidate.Flags & CardFlags.DeckCard) == 0 || candidate.Id != card.Id)
                continue;
            int growth = candidate.Var0;
            if (growth > best)
                best = growth;
        }
        if (best != stored)
            RunProperties.SetInt(ref card.Properties, hash, best, RunLimits.CardPropertyBytes);
        // Native BuffFromPlay also updates CurrentBlock/CurrentDamage through UpdateBlock/UpdateDamage.
        ulong currentHash = RunProperties.NameHash(currentName);
        int current = baseValue + best;
        if (!RunProperties.TryGetInt(card.Properties, currentHash, out int existing) || existing != current)
            RunProperties.SetInt(ref card.Properties, currentHash, current, RunLimits.CardPropertyBytes);
    }

    private static void ApplyDeckAfterCombatEnd(ref RunState run)
    {
        ulong combatsSeen = RunProperties.NameHash("CombatsSeen");
        for (int index = run.Deck.Count - 1; index >= 0; index--)
        {
            ref RunCard card = ref run.Deck.Cards[index];
            if (Registry.Card(card.Id).NativeId != "GUILTY")
                continue;
            int value = RunProperties.TryGetInt(card.Properties, combatsSeen, out int seen) ? seen : 0;
            value++;
            RunProperties.SetInt(ref card.Properties, combatsSeen, value, RunLimits.CardPropertyBytes);
            if (value >= GuiltyCombatsBeforeRemoval)
                RemoveDeckCardAt(ref run, index);
        }
    }

    private static void ApplyPowerAfterCombatEnd(ref RunState run, in Battle battle, ContentTable table)
    {
        for (int index = 0; index < battle.PowerCount; index++)
        {
            Power power = battle.Powers[index];
            if (power.Amount <= 0 || power.Owner != Limits.PlayerOwner)
                continue;
            if (Registry.Power(power.Id).NativeId != "IMPROVEMENT_POWER")
                continue;
            UpgradeRandomDeckCards(ref run, table, RunRngStream.CombatCardSelection, power.Amount);
        }
    }

    private static void ApplyRelicAfterCombatEnd(ref RunState run, in Battle battle, ContentTable table,
        RunRoomType roomType)
    {
        for (int index = 0; index < run.Relics.Count; index++)
        {
            ref RunRelic relic = ref run.Relics.Items[index];
            string nativeId = Registry.Relic(relic.Id).NativeId;
            switch (nativeId)
            {
                case "FISHING_ROD":
                {
                    if (roomType != RunRoomType.Monster)
                        break;
                    int combats = IntVar(table, relic.Id, "Combats");
                    int seen = ReadRelicInt(in relic, "CombatsSeen") + 1;
                    SetRelicInt(ref relic, "CombatsSeen", seen);
                    if (seen % combats == 0)
                        UpgradeRandomDeckCards(ref run, table, RunRngStream.Niche, 1);
                    break;
                }
                case "TOY_BOX":
                {
                    int combats = IntVar(table, relic.Id, "Combats");
                    int relics = IntVar(table, relic.Id, "Relics");
                    int seen = ReadRelicInt(in relic, "CombatsSeen");
                    if (seen >= combats * relics)
                        break;
                    seen++;
                    SetRelicInt(ref relic, "CombatsSeen", seen);
                    if (seen % combats == 0)
                    {
                        for (int other = 0; other < run.Relics.Count; other++)
                        {
                            ref RunRelic wax = ref run.Relics.Items[other];
                            if ((wax.Flags & RunRelicFlags.Wax) != 0
                                && (wax.Flags & RunRelicFlags.Melted) == 0)
                            {
                                wax.Flags |= RunRelicFlags.Melted;
                                break;
                            }
                        }
                    }
                    break;
                }
                case "PAELS_TOOTH":
                    ApplyPaelsTooth(ref run, ref relic, table);
                    break;
                case "PUMPKIN_CANDLE":
                {
                    int kindle = Math.Max(0, ReadRelicInt(in relic, "KindleCount") - 1);
                    SetRelicInt(ref relic, "KindleCount", kindle);
                    relic.DisplayAmount = kindle;
                    relic.Status = kindle <= 0 ? RunRelicStatus.Disabled : RunRelicStatus.Normal;
                    break;
                }
                case "WONGOS_MYSTERY_TICKET":
                {
                    int finished = ReadRelicInt(in relic, "CombatsFinished") + 1;
                    SetRelicInt(ref relic, "CombatsFinished", finished);
                    relic.DisplayAmount = IntVar(table, relic.Id, "RemainingCombats") - finished;
                    break;
                }
                case "CHOSEN_CHEESE":
                {
                    int maxHp = IntVar(table, relic.Id, "MaxHp");
                    run.Player.MaxHp += maxHp;
                    run.Player.CurrentHp += maxHp;
                    break;
                }
                default:
                    break;
            }
        }
    }

    private static void ApplyPaelsTooth(ref RunState run, ref RunRelic relic, ContentTable table)
    {
        ulong hash = RunProperties.NameHash("SerializableCards");
        Span<RunCard> cards = stackalloc RunCard[RunLimits.PropertyCards];
        int count = RunProperties.GetCardArray(relic.Properties, hash, cards);
        if (count == 0)
            return;
        int chosen = Rng(ref run, RunRngStream.Rewards).NextInt(count);
        RunCard added = cards[chosen];
        run.AddCard(added);
        ref RunCard addedDeckCard = ref run.Deck.Cards[run.Deck.Count - 1];
        if (addedDeckCard.Upgrade < table.Card(addedDeckCard.Id, addedDeckCard.Upgrade).MaxUpgradeLevel)
            addedDeckCard.Upgrade++;
        for (int index = chosen; index + 1 < count; index++)
            cards[index] = cards[index + 1];
        RunProperties.SetCardArray(ref relic.Properties, hash, cards[..(count - 1)],
            RunLimits.RelicPropertyBytes);
        relic.DisplayAmount = count - 1;
        relic.Status = count - 1 <= 0 ? RunRelicStatus.Disabled : RunRelicStatus.Normal;
    }

    private static void ApplyRelicAfterCombatVictory(ref RunState run, in Battle battle,
        ContentTable table, RunRoomType roomType)
    {
        // MeatOnTheBone's early pass runs before the regular victory pass.
        for (int index = 0; index < run.Relics.Count; index++)
        {
            ref RunRelic relic = ref run.Relics.Items[index];
            if (Registry.Relic(relic.Id).NativeId != "MEAT_ON_THE_BONE")
                continue;
            int threshold = IntVar(table, relic.Id, "HpThreshold");
            if (run.Player.CurrentHp <= run.Player.MaxHp * threshold / 100)
            {
                run.Player.CurrentHp = Math.Min(run.Player.MaxHp,
                    run.Player.CurrentHp + IntVar(table, relic.Id, "Heal"));
                relic.Status = RunRelicStatus.Normal;
            }
            break;
        }
        for (int index = 0; index < run.Relics.Count; index++)
        {
            ref RunRelic relic = ref run.Relics.Items[index];
            string nativeId = Registry.Relic(relic.Id).NativeId;
            switch (nativeId)
            {
                case "BURNING_BLOOD":
                case "BLACK_BLOOD":
                    run.Player.CurrentHp = Math.Min(run.Player.MaxHp,
                        run.Player.CurrentHp + IntVar(table, relic.Id, "Heal"));
                    break;
                case "SWORD_OF_STONE":
                {
                    if (roomType != RunRoomType.Elite)
                        break;
                    int elites = ReadRelicInt(in relic, "ElitesDefeated") + 1;
                    SetRelicInt(ref relic, "ElitesDefeated", elites);
                    relic.DisplayAmount = elites;
                    if (elites >= IntVar(table, relic.Id, "Elites"))
                    {
                        if (Registry.TryRelic("SWORD_OF_JADE", out ushort jade))
                        {
                            relic.Id = jade;
                            relic.Status = RunRelicStatus.Normal;
                            relic.Properties = default;
                            relic.DisplayAmount = 0;
                        }
                    }
                    break;
                }
                case "WAR_HAMMER":
                {
                    if (roomType != RunRoomType.Elite)
                        break;
                    UpgradeStableShuffledDeckCards(ref run, table, RunRngStream.Niche,
                        IntVar(table, relic.Id, "Cards"));
                    break;
                }
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Projects combat counters that are persisted in the run relic (native SavedProperty or
    /// <c>DisplayAmount</c>) before the end-of-combat status pass. This runs on every outcome: a
    /// defeat does not run the end hooks, so the values are whatever the last combat boundary saw.
    /// </summary>
    private static void ApplyRelicCounterState(ref RunState run, in Battle battle, ContentTable table)
    {
        for (int index = 0; index < run.Relics.Count && index < battle.RelicCount; index++)
        {
            ref RunRelic relic = ref run.Relics.Items[index];
            string nativeId = Registry.Relic(relic.Id).NativeId;
            int counter = battle.Relics[index].Counter;
            switch (nativeId)
            {
                case "PEN_NIB":
                    SetRelicInt(ref relic, "AttacksPlayed", counter);
                    relic.DisplayAmount = counter % PenNibCycle;
                    break;
                case "GALACTIC_DUST":
                    SetRelicInt(ref relic, "StarsSpent", counter);
                    relic.DisplayAmount = counter % IntVar(table, relic.Id, "Stars");
                    break;
                case "PENDULUM":
                case "HAPPY_FLOWER":
                case "FAKE_HAPPY_FLOWER":
                    SetRelicInt(ref relic, "TurnsSeen", counter);
                    relic.DisplayAmount = counter;
                    break;
                case "KUSARIGAMA":
                case "SHURIKEN":
                case "ORNAMENTAL_FAN":
                case "LETTER_OPENER":
                {
                    int cards = IntVar(table, relic.Id, "Cards");
                    relic.DisplayAmount = cards == 0 ? 0 : counter % cards;
                    break;
                }
                case "POCKETWATCH":
                    relic.DisplayAmount = counter;
                    break;
                case "STONE_CALENDAR":
                    relic.DisplayAmount = -1;
                    break;
                case "METRONOME":
                    relic.DisplayAmount = Math.Min(counter, IntVar(table, relic.Id, "OrbCount"));
                    break;
                case "TUNING_FORK":
                    SetRelicInt(ref relic, "SkillsPlayed", counter);
                    relic.DisplayAmount = counter;
                    break;
                case "LIZARD_TAIL":
                    SetRelicBool(ref relic, "WasUsed", counter != 0);
                    relic.Status = counter != 0 ? RunRelicStatus.Disabled : RunRelicStatus.Normal;
                    break;
                case "LAVA_LAMP":
                    SetRelicBool(ref relic, "TookDamageThisCombat", counter != 0);
                    break;
                case "JOSS_PAPER":
                    if (counter == 0)
                    {
                        RunProperties.Remove(ref relic.Properties,
                            RunProperties.NameHash("CardsExhausted"), RunLimits.RelicPropertyBytes);
                    }
                    else
                    {
                        SetRelicInt(ref relic, "CardsExhausted", counter);
                    }
                    relic.DisplayAmount = counter;
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Status at the native loss endpoint: <c>ProcessPendingLoss</c> never runs
    /// <c>AfterCombatEnd</c>, so the last in-combat status survives. The statuses below are fully
    /// determined by the final battle counters/HP; other relics keep their entry status.
    /// </summary>
    private static void ApplyDefeatRelicStatus(ref RunState run, in Battle battle, ContentTable table)
    {
        for (int index = 0; index < run.Relics.Count && index < battle.RelicCount; index++)
        {
            ref RunRelic relic = ref run.Relics.Items[index];
            if (relic.Status != RunRelicStatus.Disabled)
                relic.Status = RunRelicStatus.Normal;
            string nativeId = Registry.Relic(relic.Id).NativeId;
            int counter = battle.Relics[index].Counter;
            switch (nativeId)
            {
                case "PENDULUM":
                case "HAPPY_FLOWER":
                case "FAKE_HAPPY_FLOWER":
                    relic.Status = counter == IntVar(table, relic.Id, "Turns") - 1
                        ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                case "KUSARIGAMA":
                case "SHURIKEN":
                case "ORNAMENTAL_FAN":
                case "LETTER_OPENER":
                {
                    int cards = IntVar(table, relic.Id, "Cards");
                    relic.Status = cards > 0 && counter % cards == cards - 1
                        ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                }
                case "POCKETWATCH":
                    relic.Status = counter <= IntVar(table, relic.Id, "CardThreshold")
                        ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                case "ART_OF_WAR":
                    relic.Status = counter == 0 && battle.Relics[index].Aux == 0
                        ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                case "RAINBOW_RING":
                    relic.Status = counter > 0 ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                case "METRONOME":
                    relic.Status = counter == IntVar(table, relic.Id, "OrbCount") - 1
                        ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                case "MEAT_ON_THE_BONE":
                    relic.Status = run.Player.CurrentHp
                        <= run.Player.MaxHp * IntVar(table, relic.Id, "HpThreshold") / 100
                        ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                case "RED_SKULL":
                    relic.Status = run.Player.CurrentHp
                        <= run.Player.MaxHp * IntVar(table, relic.Id, "HpThreshold") / 100
                        ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                case "BELT_BUCKLE":
                    relic.Status = counter != 0 ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                case "TUNING_FORK":
                    relic.Status = counter == IntVar(table, relic.Id, "Cards") - 1
                        ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                case "LIZARD_TAIL":
                    relic.Status = counter != 0 ? RunRelicStatus.Disabled : RunRelicStatus.Normal;
                    break;
                case "BURNING_STICKS":
                    relic.Status = counter == 0 ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                default:
                    break;
            }
        }
    }

    private static void ApplyRelicFinalState(ref RunState run, in Battle battle, ContentTable table)
    {
        for (int index = 0; index < run.Relics.Count; index++)
        {
            ref RunRelic relic = ref run.Relics.Items[index];
            string nativeId = Registry.Relic(relic.Id).NativeId;
            switch (nativeId)
            {
                case "EMBER_TEA":
                    // Status was decided from CombatsLeft when the room was entered and does not
                    // change at combat end.
                    break;
                case "PUMPKIN_CANDLE":
                    relic.Status = relic.DisplayAmount <= 0
                        ? RunRelicStatus.Disabled : RunRelicStatus.Normal;
                    break;
                case "PAELS_TOOTH":
                    break;
                case "TOY_BOX":
                    relic.DisplayAmount = ReadRelicInt(in relic, "CombatsSeen")
                        % IntVar(table, relic.Id, "Combats");
                    if (ReadRelicInt(in relic, "CombatsSeen") >=
                        IntVar(table, relic.Id, "Combats") * IntVar(table, relic.Id, "Relics"))
                    {
                        relic.Status = RunRelicStatus.Disabled;
                    }
                    break;
                case "FISHING_ROD":
                    relic.DisplayAmount = ReadRelicInt(in relic, "CombatsSeen")
                        % IntVar(table, relic.Id, "Combats");
                    break;
                case "SHURIKEN":
                case "POCKETWATCH":
                    relic.DisplayAmount = 0;
                    relic.Status = RunRelicStatus.Normal;
                    break;
                case "METRONOME":
                    relic.DisplayAmount = 0;
                    relic.Status = RunRelicStatus.Normal;
                    break;
                case "KUSARIGAMA":
                {
                    int cards = IntVar(table, relic.Id, "Cards");
                    relic.Status = cards > 0 && relic.DisplayAmount == cards - 1
                        ? RunRelicStatus.Active : RunRelicStatus.Normal;
                    break;
                }
                case "LIZARD_TAIL":
                    // No AfterCombatEnd reset: a used tail stays Disabled.
                    break;
                case "SWORD_OF_STONE":
                    relic.DisplayAmount = ReadRelicInt(in relic, "ElitesDefeated");
                    break;
                default:
                    relic.Status = RunRelicStatus.Normal;
                    break;
            }
        }
    }

    /// <summary>
    /// Native WarHammer: upgradable deck cards are sorted by <c>CardModel.CompareTo</c> (model id
    /// then upgrade), stable-shuffled with the Niche stream, then the first <paramref name="count"/>
    /// are upgraded.
    /// </summary>
    private static void UpgradeStableShuffledDeckCards(ref RunState run, ContentTable table,
        RunRngStream stream, int count)
    {
        Span<int> candidates = stackalloc int[RunLimits.DeckCards];
        int candidateCount = 0;
        for (int index = 0; index < run.Deck.Count; index++)
        {
            RunCard card = run.Deck.Cards[index];
            if (card.Upgrade < table.Card(card.Id, card.Upgrade).MaxUpgradeLevel)
                candidates[candidateCount++] = index;
        }
        Span<int> order = candidates[..candidateCount];
        for (int index = 1; index < order.Length; index++)
        {
            int value = order[index];
            int move = index;
            while (move > 0 && CompareDeckCards(run.Deck.Cards[order[move - 1]], run.Deck.Cards[value]) > 0)
            {
                order[move] = order[move - 1];
                move--;
            }
            order[move] = value;
        }
        for (int index = order.Length - 1; index > 0; index--)
        {
            int swap = Rng(ref run, stream).NextInt(index + 1);
            (order[index], order[swap]) = (order[swap], order[index]);
        }
        int upgrades = Math.Min(count, order.Length);
        for (int index = 0; index < upgrades; index++)
            run.Deck.Cards[order[index]].Upgrade++;
    }

    private static int CompareDeckCards(in RunCard left, in RunCard right)
    {
        int compare = string.CompareOrdinal(Registry.Card(left.Id).NativeId,
            Registry.Card(right.Id).NativeId);
        return compare != 0 ? compare : left.Upgrade.CompareTo(right.Upgrade);
    }

    private static void UpgradeRandomDeckCards(ref RunState run, ContentTable table,
        RunRngStream stream, int count)
    {
        Span<int> candidates = stackalloc int[RunLimits.DeckCards];
        int candidateCount = 0;
        for (int index = 0; index < run.Deck.Count; index++)
        {
            RunCard card = run.Deck.Cards[index];
            if (card.Upgrade < table.Card(card.Id, card.Upgrade).MaxUpgradeLevel)
                candidates[candidateCount++] = index;
        }
        int upgrades = Math.Min(count, candidateCount);
        for (int index = 0; index < upgrades; index++)
        {
            int pick = Rng(ref run, stream).NextInt(candidateCount - index);
            ref RunCard card = ref run.Deck.Cards[candidates[pick]];
            card.Upgrade++;
            for (int move = pick; move + 1 < candidateCount - index; move++)
                candidates[move] = candidates[move + 1];
        }
    }

    private static ref RngState Rng(ref RunState run, RunRngStream stream)
        => ref run.Rng.Streams[(int)stream];

    private static int IntVar(ContentTable table, ushort relicId, string name)
        => checked((int)table.RelicVar(relicId, name));

    private static int ReadRelicInt(in RunRelic relic, string name)
        => RunProperties.TryGetInt(relic.Properties, RunProperties.NameHash(name), out int value)
            ? value : 0;

    private static void SetRelicBool(ref RunRelic relic, string name, bool value)
        => RunProperties.SetBool(ref relic.Properties, RunProperties.NameHash(name), value,
            RunLimits.RelicPropertyBytes);

    private static void SetRelicInt(ref RunRelic relic, string name, int value)
        => RunProperties.SetInt(ref relic.Properties, RunProperties.NameHash(name), value,
            RunLimits.RelicPropertyBytes);

    private static int FindDeckCard(in RunState run, ushort id, int upgrade)
    {
        for (int index = 0; index < run.Deck.Count; index++)
        {
            RunCard card = run.Deck.Cards[index];
            if (card.Id == id && card.Upgrade == upgrade)
                return index;
        }
        return -1;
    }

    private static void RemoveDeckCardAt(ref RunState run, int index)
    {
        for (int move = index; move + 1 < run.Deck.Count; move++)
            run.Deck.Cards[move] = run.Deck.Cards[move + 1];
        run.Deck.Count--;
        run.Deck.Cards[run.Deck.Count] = default;
    }
}
