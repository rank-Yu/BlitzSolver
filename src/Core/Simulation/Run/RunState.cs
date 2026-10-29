using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Simulation.Run;

/// <summary>
/// All branch-mutable state of one single-player run at a stable boundary. Unmanaged and
/// reference-free: a copy is a deep fork. The native model ids are dense <c>ushort</c> ids from
/// <see cref="RunCatalog"/> (acts/encounters/events/characters/epochs) or <see cref="Registry"/>
/// (cards/relics/potions/enchantments). Model-private saved properties are kept as canonical
/// fixed-size bags (<see cref="RunProperties"/>).
/// <para>
/// The layout is asserted by <c>tests/KernelChecks</c>; see <c>docs/run-state-layout.md</c>.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunState
{
    // Update after a layout change; RunCodec rejects a mismatch instead of writing a stale image.
    public const int ExpectedSize = 49912;

    public int Ascension;
    public int ActIndex;
    public int ActFloor;
    public RunGameMode GameMode;
    public RunDecision Decision;
    public byte DecisionOptionCount;
    public byte _padding0;
    public ushort _padding1;

    public RunOdds Odds;
    public RunPlayerOdds PlayerOdds;
    public RunUnlockState Unlock;
    public RunExtraFields Extra;
    public RunExtraPlayerFields ExtraPlayer;
    public RunPlayer Player;
    public RunRewards Rewards;
    public RunShop Shop;
    public RunDeck Deck;
    public RunRelics Relics;
    public RunRelicBag SharedBag;
    public RunRelicBag PlayerBag;
    public RunActs Acts;
    public RunMap Map;
    public RunHistory History;
    public RunRooms Rooms;
    public RunVisited Visited;
    public RunRngState Rng;

    public readonly int CardCount => Deck.Count;
    public readonly int RelicCount => Relics.Count;

    public void SetPlayer(in RunPlayer player) => Player = player;

    public void AddCard(in RunCard card)
    {
        if (Deck.Count >= RunLimits.DeckCards)
            throw new CapacityException("RunState.Deck", RunLimits.DeckCards);
        Deck.Cards[Deck.Count++] = card;
    }

    public void AddRelic(in RunRelic relic)
    {
        if (Relics.Count >= RunLimits.Relics)
            throw new CapacityException("RunState.Relics", RunLimits.Relics);
        Relics.Items[Relics.Count++] = relic;
    }

    public void AddReward(in RunReward reward)
    {
        if (Rewards.Count >= RunLimits.Rewards)
            throw new CapacityException("RunState.Rewards", RunLimits.Rewards);
        Rewards.Items[Rewards.Count++] = reward;
    }

    /// <summary>
    /// Reserves <paramref name="count"/> consecutive card-option slots at the end of the current
    /// reward set and returns the first index. The caller writes the options with
    /// <see cref="SetRewardCard"/> before adding the owning reward.
    /// </summary>
    public byte ReserveRewardCards(int count)
    {
        int start = 0;
        for (int index = 0; index < Rewards.Count; index++)
        {
            RunReward item = Rewards.Items[index];
            start = Math.Max(start, item.OptionStart + item.OptionCount);
        }
        if (count < 0 || start + count > RunLimits.RewardCardOptions)
            throw new CapacityException("RunState.RewardCardOptions", RunLimits.RewardCardOptions);
        return checked((byte)start);
    }

    public void SetRewardCard(int index, in RunCard card)
    {
        if ((uint)index >= (uint)RunLimits.RewardCardOptions)
            throw new CapacityException("RunState.RewardCardOptions", RunLimits.RewardCardOptions);
        Rewards.Choices[index] = card;
    }

    public void ClearRewards()
    {
        Rewards.Stage = RunRewardStage.None;
        Rewards.Count = 0;
        Rewards.Generated = 0;
        Rewards.ChoiceIndex = 0;
        Rewards.ChoiceCount = 0;
        Rewards.Items = default;
        Rewards.Choices = default;
    }

    public void ClearShop()
    {
        Shop = default;
    }

    public void SetPotion(int slot, ushort id)
    {
        if ((uint)slot >= (uint)RunLimits.PotionSlots)
            throw new CapacityException("RunState.PotionSlots", RunLimits.PotionSlots);
        Player.Potions.Items[slot] = new RunPotion { Id = id, Present = 1 };
    }

    public void AddAct(in RunAct act)
    {
        if (Acts.Count >= RunLimits.Acts)
            throw new CapacityException("RunState.Acts", RunLimits.Acts);
        Acts.Items[Acts.Count++] = act;
    }

    /// <summary>Appends one pre-generated event id to an act's room sequence.</summary>
    public static void AddActEvent(ref RunAct act, ushort eventId)
    {
        if (act.EventCount >= RunLimits.ActEvents)
            throw new CapacityException("RunState.ActEvents", RunLimits.ActEvents);
        act.Events[act.EventCount++] = eventId;
    }

    /// <summary>Appends one pre-generated normal encounter id to an act's room sequence.</summary>
    public static void AddActNormalEncounter(ref RunAct act, ushort encounterId)
    {
        if (act.NormalCount >= RunLimits.ActNormalEncounters)
            throw new CapacityException("RunState.ActNormalEncounters", RunLimits.ActNormalEncounters);
        act.Normal[act.NormalCount++] = encounterId;
    }

    /// <summary>Appends one pre-generated elite encounter id to an act's room sequence.</summary>
    public static void AddActEliteEncounter(ref RunAct act, ushort encounterId)
    {
        if (act.EliteCount >= RunLimits.ActEliteEncounters)
            throw new CapacityException("RunState.ActEliteEncounters", RunLimits.ActEliteEncounters);
        act.Elite[act.EliteCount++] = encounterId;
    }



    public void AddMapPoint(in RunMapPoint point)
    {
        if (Map.NodeCount >= RunLimits.MapPoints)
            throw new CapacityException("RunState.Map", RunLimits.MapPoints);
        Map.Nodes[Map.NodeCount++] = point;
    }

    public void AddMapStart(RunCoord coord)
    {
        if (Map.StartCount >= RunLimits.MapStarts)
            throw new CapacityException("RunState.MapStarts", RunLimits.MapStarts);
        Map.Starts[Map.StartCount++] = coord;
    }

    public void AddVisitedCoord(RunCoord coord)
    {
        if (Visited.CoordCount >= RunLimits.VisitedCoords)
            throw new CapacityException("RunState.VisitedCoords", RunLimits.VisitedCoords);
        Visited.Coords[Visited.CoordCount++] = coord;
    }

    public void AddHistory(in RunHistoryEntry entry)
    {
        if (History.Count >= RunLimits.MapHistory)
            throw new CapacityException("RunState.MapHistory", RunLimits.MapHistory);
        History.Entries[History.Count++] = entry;
    }

    public void AddRoom(in RunRoom room)
    {
        if (Rooms.Count >= RunLimits.RoomStack)
            throw new CapacityException("RunState.RoomStack", RunLimits.RoomStack);
        Rooms.Items[Rooms.Count++] = room;
    }

    public void AddEventRng(in RunEventRng eventRng)
    {
        if (Rng.EventCount >= RunLimits.EventRngs)
            throw new CapacityException("RunState.EventRngs", RunLimits.EventRngs);
        Rng.Events[Rng.EventCount++] = eventRng;
    }

    public void MarkEpoch(ushort epochId)
    {
        if (epochId >= RunCatalog.EpochCount)
            throw new CapacityException("RunState.UnlockEpoch", RunCatalog.EpochCount);
        Unlock.Epochs[epochId >> 6] |= 1UL << (epochId & 63);
    }

    public readonly bool HasEpoch(ushort epochId)
        => epochId < RunCatalog.EpochCount
            && (Unlock.Epochs[epochId >> 6] & (1UL << (epochId & 63))) != 0;

    public void MarkEncounterSeen(ushort encounterId)
    {
        if (encounterId >= RunCatalog.EncounterCount)
            throw new CapacityException("RunState.UnlockEncounter", RunCatalog.EncounterCount);
        Unlock.EncountersSeen[encounterId >> 6] |= 1UL << (encounterId & 63);
    }

    public readonly bool HasSeenEncounter(ushort encounterId)
        => encounterId < RunCatalog.EncounterCount
            && (Unlock.EncountersSeen[encounterId >> 6] & (1UL << (encounterId & 63))) != 0;

    public void MarkVisitedEvent(ushort eventId)
    {
        if (eventId >= RunCatalog.EventCount)
            throw new CapacityException("RunState.VisitedEvent", RunCatalog.EventCount);
        Visited.Events[eventId >> 6] |= 1UL << (eventId & 63);
    }

    public readonly bool HasVisitedEvent(ushort eventId)
        => eventId < RunCatalog.EventCount
            && (Visited.Events[eventId >> 6] & (1UL << (eventId & 63))) != 0;

    /// <summary>
    /// Zeroes every slot past a count, sorts order-independent collections and clears unused unlock
    /// bits. Two states with the same meaning then have the same byte image. Callers run this on a
    /// copy in <see cref="RunCodec.Encode"/>.
    /// </summary>
    public void Canonicalize()
    {
        for (int index = Math.Min((int)Deck.Count, RunLimits.DeckCards); index < RunLimits.DeckCards; index++)
            Deck.Cards[index] = default;
        int rewardCount = Math.Min((int)Rewards.Count, RunLimits.Rewards);
        for (int index = rewardCount; index < RunLimits.Rewards; index++)
            Rewards.Items[index] = default;
        int usedRewardCards = 0;
        for (int index = 0; index < rewardCount; index++)
        {
            RunReward item = Rewards.Items[index];
            usedRewardCards = Math.Max(usedRewardCards, item.OptionStart + item.OptionCount);
        }
        for (int index = Math.Min(usedRewardCards, RunLimits.RewardCardOptions);
             index < RunLimits.RewardCardOptions; index++)
        {
            Rewards.Choices[index] = default;
        }
        if (Rewards.Stage != RunRewardStage.Card)
            Rewards.ChoiceCount = 0;
        if (Rewards.Stage is RunRewardStage.None or RunRewardStage.List)
            Rewards.ChoiceIndex = 0;
        if (Shop.Stage == RunShopStage.None)
        {
            Shop = default;
        }
        else
        {
            for (int index = 0; index < RunLimits.ShopCharacterCards; index++)
                CanonicalizeShopCard(ref Shop.CharacterCards[index]);
            for (int index = 0; index < RunLimits.ShopColorlessCards; index++)
                CanonicalizeShopCard(ref Shop.ColorlessCards[index]);
            for (int index = 0; index < RunLimits.ShopRelics; index++)
                CanonicalizeShopModel(ref Shop.Relics[index]);
            for (int index = 0; index < RunLimits.ShopPotions; index++)
                CanonicalizeShopModel(ref Shop.Potions[index]);
            if ((Shop.Removal.Used & 1) == 0 && Shop.Removal.Cost == 0 && Shop.Removal.Price == 0)
                Shop.Removal = default;
            else
                Shop.Removal.Used = (byte)(Shop.Removal.Used != 0 ? 1 : 0);
        }
        for (int index = Math.Min((int)Relics.Count, RunLimits.Relics); index < RunLimits.Relics; index++)
            Relics.Items[index] = default;
        for (int index = Math.Min((int)Player.Potions.Count, RunLimits.PotionSlots); index < RunLimits.PotionSlots; index++)
            Player.Potions.Items[index] = default;
        for (int index = Math.Min((int)Acts.Count, RunLimits.Acts); index < RunLimits.Acts; index++)
            Acts.Items[index] = default;
        for (int index = Math.Min((int)Map.NodeCount, RunLimits.MapPoints); index < RunLimits.MapPoints; index++)
            Map.Nodes[index] = default;
        for (int index = Math.Min((int)Map.StartCount, RunLimits.MapStarts); index < RunLimits.MapStarts; index++)
            Map.Starts[index] = default;
        for (int index = Math.Min((int)Visited.CoordCount, RunLimits.VisitedCoords); index < RunLimits.VisitedCoords; index++)
            Visited.Coords[index] = default;
        for (int index = Math.Min((int)History.Count, RunLimits.MapHistory); index < RunLimits.MapHistory; index++)
            History.Entries[index] = default;
        for (int index = Math.Min((int)Rooms.Count, RunLimits.RoomStack); index < RunLimits.RoomStack; index++)
            Rooms.Items[index] = default;
        for (int index = Math.Min((int)Rng.EventCount, RunLimits.EventRngs); index < RunLimits.EventRngs; index++)
            Rng.Events[index] = default;
        for (int index = 0; index < RunLimits.Acts; index++)
        {
            ref RunAct act = ref Acts.Items[index];
            for (int eventIndex = act.EventCount; eventIndex < RunLimits.ActEvents; eventIndex++)
                act.Events[eventIndex] = 0;
            for (int normalIndex = act.NormalCount; normalIndex < RunLimits.ActNormalEncounters; normalIndex++)
                act.Normal[normalIndex] = 0;
            for (int eliteIndex = act.EliteCount; eliteIndex < RunLimits.ActEliteEncounters; eliteIndex++)
                act.Elite[eliteIndex] = 0;
            if (act.HasAncient == 0)
                act.AncientId = 0;
            if (act.HasSecondBoss == 0)
                act.SecondBossId = 0;
        }
        for (int rarity = 0; rarity < RunLimits.RelicRarities; rarity++)
        {
            ref RunRelicBagSegment shared = ref SharedBag.Segments[rarity];
            for (int index = shared.Count; index < RunLimits.RelicBagPerRarity; index++)
                shared.Ids[index] = 0;
            ref RunRelicBagSegment player = ref PlayerBag.Segments[rarity];
            for (int index = player.Count; index < RunLimits.RelicBagPerRarity; index++)
                player.Ids[index] = 0;
        }
        if (Map.HasSecondBoss == 0)
            Map.SecondBoss = default;
        int deckCount = Math.Min((int)Deck.Count, RunLimits.DeckCards);
        for (int index = 0; index < deckCount; index++)
        {
            ref RunCard card = ref Deck.Cards[index];
            if (card.HasEnchantment == 0)
            {
                card.EnchantmentId = 0;
                card.EnchantmentAmount = 0;
                card.EnchantmentProperties = default;
            }
        }
        for (int index = 0; index < RunLimits.MapPoints; index++)
        {
            ref RunMapPoint point = ref Map.Nodes[index];
            for (int child = point.ChildCount; child < RunLimits.MapChildren; child++)
                point.Children[child] = default;
        }
        for (int index = 0; index < RunLimits.MapHistory; index++)
        {
            ref RunHistoryEntry entry = ref History.Entries[index];
            int roomCount = Math.Min((int)entry.RoomCount, RunLimits.HistoryRooms);
            for (int room = roomCount; room < RunLimits.HistoryRooms; room++)
                entry.Rooms[room] = default;
            for (int room = 0; room < roomCount; room++)
            {
                ref RunHistoryRoom historyRoom = ref entry.Rooms[room];
                int monsterCount = Math.Min((int)historyRoom.MonsterCount,
                    RunLimits.HistoryMonsters);
                for (int monster = monsterCount; monster < RunLimits.HistoryMonsters; monster++)
                    historyRoom.Monsters[monster] = 0;
            }
        }

        ClearUnusedBits(ref Unlock.Epochs, RunCatalog.EpochCount);
        ClearUnusedBits(ref Unlock.EncountersSeen, RunCatalog.EncounterCount);
        ClearUnusedBits(ref Visited.Events, RunCatalog.EventCount);

        SortMap();
        SortEventRngs();
    }

    private static void CanonicalizeShopCard(ref RunShopCard entry)
    {
        RunShopEntryFlags onSale = entry.Flags & RunShopEntryFlags.OnSale;
        if ((entry.Flags & RunShopEntryFlags.Stocked) == 0)
        {
            entry = default;
            entry.Flags = onSale;
            return;
        }
        entry.Flags = RunShopEntryFlags.Stocked | onSale;
    }

    private static void CanonicalizeShopModel(ref RunShopModel entry)
    {
        if ((entry.Flags & RunShopEntryFlags.Stocked) == 0)
        {
            entry = default;
            return;
        }
        entry.Flags &= RunShopEntryFlags.Stocked;
    }

    private static void ClearUnusedBits<T>(ref T words, int count) where T : unmanaged
    {
        int wordCount = Unsafe.SizeOf<T>() / sizeof(ulong);
        Span<ulong> span = MemoryMarshal.CreateSpan(ref Unsafe.As<T, ulong>(ref words), wordCount);
        for (int index = 0; index < span.Length; index++)
        {
            int first = index * 64;
            if (first >= count)
                span[index] = 0;
            else if (count - first < 64)
                span[index] &= (1UL << (count - first)) - 1;
        }
    }

    private void SortMap()
    {
        // Sort nodes by coordinate; children and starts reference coordinates, so order is not
        // semantically meaningful. Insertion sort keeps this allocation-free.
        Span<RunMapPoint> nodes = Map.Nodes;
        int nodeCount = Math.Min((int)Map.NodeCount, RunLimits.MapPoints);
        for (int index = 1; index < nodeCount; index++)
        {
            RunMapPoint value = nodes[index];
            int position = index - 1;
            while (position >= 0 && Compare(nodes[position].Coord, value.Coord) > 0)
            {
                nodes[position + 1] = nodes[position];
                position--;
            }
            nodes[position + 1] = value;
        }
        for (int index = 0; index < nodeCount; index++)
        {
            ref RunMapPoint point = ref Map.Nodes[index];
            Span<RunCoord> children = point.Children;
            for (int child = 1; child < point.ChildCount; child++)
            {
                RunCoord value = children[child];
                int position = child - 1;
                while (position >= 0 && Compare(children[position], value) > 0)
                {
                    children[position + 1] = children[position];
                    position--;
                }
                children[position + 1] = value;
            }
        }
        Span<RunCoord> starts = Map.Starts;
        int startCount = Math.Min((int)Map.StartCount, RunLimits.MapStarts);
        for (int index = 1; index < startCount; index++)
        {
            RunCoord value = starts[index];
            int position = index - 1;
            while (position >= 0 && Compare(starts[position], value) > 0)
            {
                starts[position + 1] = starts[position];
                position--;
            }
            starts[position + 1] = value;
        }
    }

    private void SortEventRngs()
    {
        Span<RunEventRng> events = Rng.Events;
        int eventCount = Math.Min((int)Rng.EventCount, RunLimits.EventRngs);
        for (int index = 1; index < eventCount; index++)
        {
            RunEventRng value = events[index];
            int position = index - 1;
            while (position >= 0 && events[position].EventId > value.EventId)
            {
                events[position + 1] = events[position];
                position--;
            }
            events[position + 1] = value;
        }
    }

    private static int Compare(RunCoord left, RunCoord right)
        => left.Col != right.Col ? left.Col - right.Col : left.Row - right.Row;
}
