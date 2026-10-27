using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Canonical text view of a <see cref="RunState"/> for field-by-field comparison with an
/// independent native dump. It is intentionally exhaustive (including RNG and private bags) and is
/// not the player-visible projection; use <see cref="RunView"/> for decisions.
/// </summary>
public static class RunDump
{
    public static string Capture(in RunState input)
    {
        RunState state = input;
        state.Canonicalize();
        StringBuilder text = new(16 * 1024);
        void Line(string key, string value)
            => text.Append(key).Append('=').Append(value).Append('\n');
        void Int(string key, long value)
            => Line(key, value.ToString(CultureInfo.InvariantCulture));

        Int("run.version", 1);
        Int("run.ascension", state.Ascension);
        Int("run.act.index", state.ActIndex);
        Int("run.act.floor", state.ActFloor);
        Line("run.gameMode", state.GameMode.ToString());
        Line("run.decision", state.Decision.ToString());
        Int("run.decision.options", state.DecisionOptionCount);
        Line("run.character", RunCatalog.CharacterName(state.Player.Character));
        Int("run.hp", state.Player.CurrentHp);
        Int("run.maxHp", state.Player.MaxHp);
        Int("run.maxEnergy", state.Player.MaxEnergy);
        Int("run.gold", state.Player.Gold);
        Int("run.orbSlots", state.Player.BaseOrbSlots);
        Int("run.potionSlots", state.Player.Potions.Count);
        Line("run.extra", $"{state.Extra.StartedWithNeow},{state.Extra.TestSubjectKills}");
        Line("run.extraPlayer", $"{state.ExtraPlayer.CardShopRemovalsUsed},{state.ExtraPlayer.WongoPoints}");
        Line("run.odds", $"{F(state.Odds.Monster)},{F(state.Odds.Elite)},{F(state.Odds.Treasure)},{F(state.Odds.Shop)}");
        Line("run.playerOdds", $"{F(state.PlayerOdds.CardRarity)},{F(state.PlayerOdds.PotionReward)}");
        Line("run.seed", state.Rng.Seed.ToString(CultureInfo.InvariantCulture));
        Line("run.playerSeed", state.Rng.PlayerSeed.ToString(CultureInfo.InvariantCulture));

        Int("unlock.runs", state.Unlock.NumberOfRuns);
        Line("unlock.epochs", JoinBits(state.Unlock.Epochs, RunCatalog.EpochCount, RunCatalog.EpochName));
        Line("unlock.encounters", JoinBits(state.Unlock.EncountersSeen, RunCatalog.EncounterCount,
            RunCatalog.EncounterName));

        for (int index = 0; index < state.Deck.Count; index++)
            Line($"deck.{index}", Card(state.Deck.Cards[index]));
        for (int index = 0; index < state.Relics.Count; index++)
            Line($"relic.{index}", Relic(state.Relics.Items[index]));
        for (int index = 0; index < state.Player.Potions.Count; index++)
        {
            RunPotion potion = state.Player.Potions.Items[index];
            Line($"potion.{index}", potion.Present == 0 ? "-" : Registry.Potion(potion.Id).NativeId);
        }
        Line("rewards.stage", state.Rewards.Stage.ToString());
        Int("rewards.generated", state.Rewards.Generated);
        Int("rewards.count", state.Rewards.Count);
        Int("rewards.choiceIndex", state.Rewards.ChoiceIndex);
        Int("rewards.choiceCount", state.Rewards.ChoiceCount);
        for (int index = 0; index < state.Rewards.Count; index++)
            Line($"rewards.{index}", Reward(state.Rewards, index));
        Line("shop.stage", state.Shop.Stage.ToString());
        Line("shop.removal",
            $"{state.Shop.Removal.Used},{state.Shop.Removal.Cost},{state.Shop.Removal.Price}");
        for (int index = 0; index < RunLimits.ShopCharacterCards; index++)
            Line($"shop.character.{index}", ShopCard(state.Shop.CharacterCards[index]));
        for (int index = 0; index < RunLimits.ShopColorlessCards; index++)
            Line($"shop.colorless.{index}", ShopCard(state.Shop.ColorlessCards[index]));
        for (int index = 0; index < RunLimits.ShopRelics; index++)
            Line($"shop.relic.{index}", ShopModel(state.Shop.Relics[index], relic: true));
        for (int index = 0; index < RunLimits.ShopPotions; index++)
            Line($"shop.potion.{index}", ShopModel(state.Shop.Potions[index], relic: false));
        DumpBag(text, "sharedBag", state.SharedBag);
        DumpBag(text, "playerBag", state.PlayerBag);

        Int("acts.count", state.Acts.Count);
        for (int index = 0; index < state.Acts.Count; index++)
        {
            RunAct act = state.Acts.Items[index];
            string prefix = $"act.{index}";
            Line(prefix + ".id", RunCatalog.ActName(act.Id));
            Line(prefix + ".ancient", act.HasAncient == 0 ? "-" : RunCatalog.EventName(act.AncientId));
            Line(prefix + ".boss", RunCatalog.EncounterName(act.BossId));
            Line(prefix + ".secondBoss", act.HasSecondBoss == 0
                ? "-" : RunCatalog.EncounterName(act.SecondBossId));
            Line(prefix + ".visited",
                $"{act.EventsVisited},{act.NormalVisited},{act.EliteVisited},{act.BossVisited}");
            Line(prefix + ".events", JoinIds(act.Events, act.EventCount, RunCatalog.EventName));
            Line(prefix + ".normal", JoinIds(act.Normal, act.NormalCount, RunCatalog.EncounterName));
            Line(prefix + ".elite", JoinIds(act.Elite, act.EliteCount, RunCatalog.EncounterName));
        }

        Line("map.size", $"{state.Map.Width}x{state.Map.Height}");
        Line("map.starting", state.Map.Starting.ToString());
        Line("map.boss", state.Map.Boss.ToString());
        Line("map.secondBoss", state.Map.HasSecondBoss == 0 ? "-" : state.Map.SecondBoss.ToString());
        Line("map.starts", JoinCoords(state.Map.Starts, state.Map.StartCount));
        Int("map.nodes", state.Map.NodeCount);
        for (int index = 0; index < state.Map.NodeCount; index++)
        {
            RunMapPoint point = state.Map.Nodes[index];
            Line($"map.node.{index}",
                $"{point.Coord},{point.Type},modified={point.CanBeModified},children={JoinCoords(point.Children, point.ChildCount)}");
        }

        Int("history.count", state.History.Count);
        for (int index = 0; index < state.History.Count; index++)
        {
            RunHistoryEntry entry = state.History.Entries[index];
            StringBuilder line = new();
            line.Append(entry.ActIndex).Append(':').Append(entry.MapPointType);
            for (int room = 0; room < entry.RoomCount; room++)
            {
                RunHistoryRoom historyRoom = entry.Rooms[room];
                line.Append('|').Append(historyRoom.RoomType).Append('/');
                line.Append(RefName(historyRoom.Model)).Append('/');
                for (int monster = 0; monster < historyRoom.MonsterCount; monster++)
                {
                    if (monster != 0)
                        line.Append('+');
                    line.Append(Registry.Monster(historyRoom.Monsters[monster]).NativeId);
                }
                line.Append('/').Append(historyRoom.TurnsTaken);
            }
            Line($"history.{index}", line.ToString());
        }

        Line("visited.coords", JoinCoords(state.Visited.Coords, state.Visited.CoordCount));
        Line("visited.events", JoinBits(state.Visited.Events, RunCatalog.EventCount, RunCatalog.EventName));

        Int("rooms.count", state.Rooms.Count);
        for (int index = 0; index < state.Rooms.Count; index++)
        {
            RunRoom room = state.Rooms.Items[index];
            Line($"room.{index}",
                $"{room.RoomType},{RefName(room.Model)},{RefName(room.ParentEvent)},pre={room.IsPreFinished},gold={F(room.GoldProportion)}");
        }

        for (int index = 0; index < RunLimits.RngStreams; index++)
        {
            RngState rng = state.Rng.Streams[index];
            Line("rng.stream." + RunRng.StreamName((RunRngStream)index), Rng(rng));
        }
        for (int index = 0; index < RunLimits.ActMapRngs; index++)
        {
            RngState rng = state.Rng.Map[index];
            Line($"rng.map.{index + 1}", $"{Rng(rng)},consumed={state.Rng.MapConsumed[index]}");
        }
        Int("rng.events", state.Rng.EventCount);
        for (int index = 0; index < state.Rng.EventCount; index++)
        {
            RunEventRng eventRng = state.Rng.Events[index];
            Line($"rng.event.{index}",
                $"{RunCatalog.EventName(eventRng.EventId)},{Rng(eventRng.State)}");
        }
        return text.ToString();
    }

    public static string RefName(RunRef reference) => reference.Kind switch
    {
        RunRefKind.None => "-",
        RunRefKind.Character => RunCatalog.CharacterName(reference.Id),
        RunRefKind.Act => RunCatalog.ActName(reference.Id),
        RunRefKind.Encounter => RunCatalog.EncounterName(reference.Id),
        RunRefKind.Event => RunCatalog.EventName(reference.Id),
        RunRefKind.Card => Registry.Card(reference.Id).NativeId,
        RunRefKind.Relic => Registry.Relic(reference.Id).NativeId,
        RunRefKind.Potion => Registry.Potion(reference.Id).NativeId,
        RunRefKind.Monster => Registry.Monster(reference.Id).NativeId,
        RunRefKind.Enchantment => Registry.Enchantment(reference.Id).NativeId,
        RunRefKind.Affliction => Registry.Affliction(reference.Id).NativeId,
        RunRefKind.Orb => Registry.Orb(reference.Id).NativeId,
        _ => "?",
    };

    public static string Card(in RunCard card)
    {
        StringBuilder text = new();
        text.Append(Registry.Card(card.Id).NativeId).Append(',');
        text.Append(card.Upgrade).Append(',');
        text.Append(card.FloorAddedToDeck).Append(',');
        if (card.HasEnchantment == 0)
        {
            text.Append("-,-");
        }
        else
        {
            text.Append(Registry.Enchantment(card.EnchantmentId).NativeId).Append(',');
            text.Append(card.EnchantmentAmount);
        }
        text.Append(",props=").Append(PropertyText(card.Properties));
        text.Append(",enchantProps=").Append(PropertyText(card.EnchantmentProperties));
        return text.ToString();
    }

    private static string ShopCard(in RunShopCard entry)
    {
        if ((entry.Flags & RunShopEntryFlags.Stocked) == 0)
            return $"{(int)entry.Flags}";
        return $"{(int)entry.Flags},{entry.Cost},{entry.Price},{Card(entry.Card)}";
    }

    private static string ShopModel(in RunShopModel entry, bool relic)
    {
        if ((entry.Flags & RunShopEntryFlags.Stocked) == 0)
            return $"{(int)entry.Flags}";
        string name = relic
            ? Registry.Relic(entry.ModelId).NativeId
            : Registry.Potion(entry.ModelId).NativeId;
        return $"{(int)entry.Flags},{name},{entry.Cost},{entry.Price}";
    }

    public static string Reward(in RunRewards rewards, int index)
    {
        RunReward item = rewards.Items[index];
        StringBuilder text = new(item.Type.ToString());
        text.Append(",flags=").Append((int)item.Flags);
        switch (item.Type)
        {
            case RunRewardType.Gold:
                text.Append(",amount=").Append(item.Amount);
                text.Append(",amountMax=").Append(item.AmountMax);
                return text.ToString();
            case RunRewardType.Potion:
                text.Append(',').Append(item.ModelId == 0 && (item.Flags & RunRewardFlags.Populated) == 0
                    ? "-" : Registry.Potion(item.ModelId).NativeId);
                return text.ToString();
            case RunRewardType.Relic:
                text.Append(',').Append(item.ModelId == 0 && (item.Flags & RunRewardFlags.Populated) == 0
                    ? "-" : Registry.Relic(item.ModelId).NativeId);
                return text.ToString();
            case RunRewardType.CardRemoval:
                return text.ToString();
            case RunRewardType.Card:
            case RunRewardType.SpecialCard:
                text.Append(",cardCount=").Append(item.CardCount);
                text.Append(",rarity=").Append((RoomRarityOdds)item.RaritySource);
                text.Append(",options=").Append(item.OptionCount);
                text.Append(",cards=[");
                for (int option = 0; option < item.OptionCount; option++)
                {
                    if (option != 0)
                        text.Append(';');
                    text.Append(Card(rewards.Choices[item.OptionStart + option]));
                }
                return text.Append(']').ToString();
            default:
                return text.ToString();
        }
    }

    public static string Relic(in RunRelic relic)
    {
        StringBuilder text = new();
        text.Append(Registry.Relic(relic.Id).NativeId).Append(',');
        text.Append((int)relic.Status).Append(',').Append((int)relic.Flags).Append(',');
        text.Append(relic.StackCount).Append(',').Append(relic.DisplayAmount).Append(',');
        text.Append(relic.FloorAddedToDeck).Append(",props=")
            .Append(PropertyText(relic.Properties));
        return text.ToString();
    }

    /// <summary>
    /// Decodes one canonical property bag into the same independent text the native dump prints
    /// directly from <c>SavedProperties</c>. Entry format: <c>nameHash:type:value</c>, sorted by
    /// name hash; arrays are printed item by item and strings verbatim.
    /// </summary>
    public static string PropertyText(in RunPropertyBag bag)
    {
        if (bag.Length == 0)
            return "-";
        RunProperties.Reader reader = new(bag);
        List<(ulong Hash, string Text)> entries = [];
        while (reader.TryNext(out ulong hash, out byte kind))
        {
            string value = KindText(ref reader, kind);
            entries.Add((hash,
                $"{hash.ToString("x16", CultureInfo.InvariantCulture)}:{KindName(kind)}:{value}"));
        }
        entries.Sort(static (left, right) => left.Hash != right.Hash
            ? left.Hash.CompareTo(right.Hash)
            : string.CompareOrdinal(left.Text, right.Text));
        StringBuilder text = new();
        for (int index = 0; index < entries.Count; index++)
        {
            if (index != 0)
                text.Append(';');
            text.Append(entries[index].Text);
        }
        return text.ToString();
    }

    private static string KindText(ref RunProperties.Reader reader, byte kind) => kind switch
    {
        RunProperties.KindInt => reader.ReadInt().ToString(CultureInfo.InvariantCulture),
        RunProperties.KindBool => reader.ReadBool() ? "true" : "false",
        RunProperties.KindString => reader.ReadString(),
        RunProperties.KindIntArray => IntArrayText(ref reader),
        RunProperties.KindModel => ModelText(reader.ReadModel()),
        RunProperties.KindCard => Card(reader.ReadCard()),
        RunProperties.KindCardArray => CardArrayText(ref reader),
        _ => throw new InvalidDataException($"属性种类 {kind} 未知。"),
    };

    private static string KindName(byte kind) => kind switch
    {
        RunProperties.KindInt => "int",
        RunProperties.KindBool => "bool",
        RunProperties.KindString => "string",
        RunProperties.KindIntArray => "int[]",
        RunProperties.KindModel => "model",
        RunProperties.KindCard => "card",
        RunProperties.KindCardArray => "card[]",
        _ => "?",
    };

    private static string IntArrayText(ref RunProperties.Reader reader)
    {
        int count = reader.ReadByte();
        StringBuilder text = new();
        text.Append('[');
        for (int index = 0; index < count; index++)
        {
            if (index != 0)
                text.Append(',');
            text.Append(reader.ReadInt().ToString(CultureInfo.InvariantCulture));
        }
        return text.Append(']').ToString();
    }

    private static string CardArrayText(ref RunProperties.Reader reader)
    {
        int count = reader.ReadByte();
        StringBuilder text = new();
        text.Append('[');
        for (int index = 0; index < count; index++)
        {
            if (index != 0)
                text.Append(';');
            text.Append(Card(reader.ReadCard()));
        }
        return text.Append(']').ToString();
    }

    private static string ModelText(RunRef reference)
        => $"{reference.Kind}:{RefName(reference)}";

    public static string Rng(in RngState rng)
        => string.Create(CultureInfo.InvariantCulture,
            $"{rng.Counter},{rng.S0},{rng.S1},{rng.S2},{rng.S3}");

    private static void DumpBag(StringBuilder text, string prefix, in RunRelicBag bag)
    {
        text.Append(prefix).Append(".refresh=").Append(bag.RefreshAllowed).Append('\n');
        for (int rarity = 0; rarity < RunLimits.RelicRarities; rarity++)
        {
            RunRelicBagSegment segment = bag.Segments[rarity];
            StringBuilder line = new();
            for (int index = 0; index < segment.Count; index++)
            {
                if (index != 0)
                    line.Append(',');
                line.Append(Registry.Relic(segment.Ids[index]).NativeId);
            }
            text.Append(prefix).Append('.').Append((RunRelicRarity)rarity).Append('=')
                .Append(line).Append('\n');
        }
    }

    private static string JoinIds<T>(T ids, int count, Func<ushort, string> name) where T : unmanaged
    {
        Span<ushort> span = MemoryMarshal.CreateSpan(ref Unsafe.As<T, ushort>(ref ids), count);
        StringBuilder text = new();
        for (int index = 0; index < count; index++)
        {
            if (index != 0)
                text.Append(',');
            text.Append(name(span[index]));
        }
        return text.ToString();
    }

    private static string JoinCoords<T>(T coords, int count) where T : unmanaged
    {
        Span<RunCoord> span = MemoryMarshal.CreateSpan(ref Unsafe.As<T, RunCoord>(ref coords), count);
        StringBuilder text = new();
        for (int index = 0; index < count; index++)
        {
            if (index != 0)
                text.Append(';');
            text.Append(span[index]);
        }
        return text.ToString();
    }

    private static string JoinBits<T>(T words, int count, Func<ushort, string> name) where T : unmanaged
    {
        Span<ulong> span = MemoryMarshal.CreateSpan(ref Unsafe.As<T, ulong>(ref words), (count + 63) / 64);
        StringBuilder text = new();
        bool first = true;
        for (int index = 0; index < count; index++)
        {
            if ((span[index >> 6] & (1UL << (index & 63))) == 0)
                continue;
            if (!first)
                text.Append(',');
            first = false;
            text.Append(name((ushort)index));
        }
        return text.ToString();
    }

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
