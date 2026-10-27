// Independent native run-state dump. It reads the original RunState/Player with publicized game
// members only and formats the same canonical text as BlitzSolver.Simulation.Run.RunDump. It does
// not call RunStateImporter, so a natural-start comparison exercises the importer against a second
// reader rather than against itself.
using System.Globalization;
using System.Reflection;
using System.Text;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.Rngs;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;

namespace BlitzSolver.TestHost;

internal static class NativeRunDump
{
    public static string Capture(NativeRunState native, Player player, RewardsSet? rewards = null,
        IReadOnlyList<Reward>? rewardList = null, RunRewardStage rewardStage = RunRewardStage.List,
        int rewardChoiceIndex = 0, int rewardChoiceCount = 0,
        IReadOnlyDictionary<int, int>? relicDisplayOverrides = null)
    {
        StringBuilder text = new(16 * 1024);
        void Line(string key, string value)
            => text.Append(key).Append('=').Append(value).Append('\n');
        void Int(string key, long value)
            => Line(key, value.ToString(CultureInfo.InvariantCulture));

        Int("run.version", 1);
        Int("run.ascension", native.AscensionLevel);
        Int("run.act.index", native.CurrentActIndex);
        Int("run.act.floor", native.ActFloor);
        Line("run.gameMode", native.GameMode.ToString());
        Line("run.decision",
            (native.IsGameOver ? RunDecision.GameOver : DecisionFor(native.CurrentRoom)).ToString());
        Int("run.decision.options", 0);
        Line("run.character", player.Character.Id.Entry);
        Int("run.hp", player.Creature.CurrentHp);
        Int("run.maxHp", player.Creature.MaxHp);
        Int("run.maxEnergy", player.MaxEnergy);
        Int("run.gold", player.Gold);
        Int("run.orbSlots", player.BaseOrbSlotCount);
        Int("run.potionSlots", player.MaxPotionCount);
        Line("run.extra",
            $"{(native.ExtraFields.StartedWithNeow ? 1 : 0)},{native.ExtraFields.TestSubjectKills}");
        Line("run.extraPlayer",
            $"{player.ExtraFields.CardShopRemovalsUsed},{player.ExtraFields.WongoPoints}");
        Line("run.odds",
            $"{F(native.Odds.UnknownMapPoint.MonsterOdds)},{F(native.Odds.UnknownMapPoint.EliteOdds)},{F(native.Odds.UnknownMapPoint.TreasureOdds)},{F(native.Odds.UnknownMapPoint.ShopOdds)}");
        Line("run.playerOdds",
            $"{F(player.PlayerOdds.CardRarity.CurrentValue)},{F(player.PlayerOdds.PotionReward.CurrentValue)}");
        Line("run.seed", native.Rng.Seed.ToString(CultureInfo.InvariantCulture));
        Line("run.playerSeed", player.PlayerRng.Seed.ToString(CultureInfo.InvariantCulture));

        Int("unlock.runs", native.UnlockState.NumberOfRuns);
        List<(ushort Id, string Name)> epochs = [];
        foreach (string epoch in native.UnlockState._unlockedEpochIds)
        {
            if (!RunCatalog.TryEpoch(epoch, out ushort id))
                throw new InvalidDataException($"原生解锁 epoch {epoch} 不在目录内。");
            epochs.Add((id, epoch));
        }
        epochs.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        Line("unlock.epochs", string.Join(',', epochs.Select(static item => item.Name)));
        List<(ushort Id, string Name)> encounters = [];
        foreach (ModelId encounter in native.UnlockState._encountersSeen)
        {
            if (!RunCatalog.TryEncounter(encounter.Entry, out ushort id))
                throw new InvalidDataException($"原生已见遭遇 {encounter.Entry} 不在目录内。");
            encounters.Add((id, encounter.Entry));
        }
        encounters.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        Line("unlock.encounters", string.Join(',', encounters.Select(static item => item.Name)));

        for (int index = 0; index < player.Deck.Cards.Count; index++)
        {
            CardModel card = player.Deck.Cards[index];
            Line($"deck.{index}", FormatCard(card.ToSerializable()));
        }
        for (int index = 0; index < player.Relics.Count; index++)
        {
            int display = relicDisplayOverrides != null
                && relicDisplayOverrides.TryGetValue(index, out int overrideDisplay)
                    ? overrideDisplay
                    : player.Relics[index].DisplayAmount;
            Line($"relic.{index}", FormatRelic(player.Relics[index], display));
        }
        for (int index = 0; index < player.MaxPotionCount; index++)
        {
            PotionModel? potion = player.GetPotionAtSlotIndex(index);
            Line($"potion.{index}", potion?.Id.Entry ?? "-");
        }
        IReadOnlyList<Reward> rewardItems = rewardList ?? rewards?.Rewards
            ?? (native.CurrentRoom is CombatRoom rewardRoom &&
                rewardRoom.ExtraRewards.TryGetValue(player, out List<Reward>? extras)
                    ? extras
                    : []);
        Line("rewards.stage", rewards != null ? rewardStage.ToString() : "None");
        Int("rewards.generated", rewards != null ? 1 : 0);
        Int("rewards.count", rewardItems.Count);
        Int("rewards.choiceIndex", rewardChoiceIndex);
        Int("rewards.choiceCount", rewardChoiceCount);
        for (int index = 0; index < rewardItems.Count; index++)
            Line($"rewards.{index}", FormatReward(rewardItems[index]));
        DumpBag(text, "sharedBag", native.SharedRelicGrabBag);
        DumpBag(text, "playerBag", player.RelicGrabBag);

        Int("acts.count", native.Acts.Count);
        for (int index = 0; index < native.Acts.Count; index++)
        {
            ActModel act = native.Acts[index];
            RoomSet rooms = act._rooms;
            string prefix = $"act.{index}";
            Line(prefix + ".id", act.Id.Entry);
            Line(prefix + ".ancient", rooms.HasAncient ? rooms.Ancient.Id.Entry : "-");
            Line(prefix + ".boss", rooms.Boss.Id.Entry);
            Line(prefix + ".secondBoss", rooms.HasSecondBoss ? rooms.SecondBoss!.Id.Entry : "-");
            Line(prefix + ".visited",
                $"{rooms.eventsVisited},{rooms.normalEncountersVisited},{rooms.eliteEncountersVisited},{rooms.bossEncountersVisited}");
            Line(prefix + ".events", string.Join(',', rooms.events.Select(static model => model.Id.Entry)));
            Line(prefix + ".normal",
                string.Join(',', rooms.normalEncounters.Select(static model => model.Id.Entry)));
            Line(prefix + ".elite",
                string.Join(',', rooms.eliteEncounters.Select(static model => model.Id.Entry)));
        }

        Line("map.size", $"{native.Map.GetColumnCount()}x{native.Map.GetRowCount()}");
        Line("map.starting", Coord(native.Map.StartingMapPoint.coord));
        Line("map.boss", Coord(native.Map.BossMapPoint.coord));
        Line("map.secondBoss",
            native.Map.SecondBossMapPoint is { } second ? Coord(second.coord) : "-");
        Line("map.starts", string.Join(';', native.Map.startMapPoints
            .Select(static point => point.coord)
            .OrderBy(static coord => coord.col)
            .ThenBy(static coord => coord.row)
            .Select(Coord)));
        List<MapPoint> points = [.. native.Map.GetAllMapPoints()];
        points.Add(native.Map.StartingMapPoint);
        points.Add(native.Map.BossMapPoint);
        if (native.Map.SecondBossMapPoint is { } secondBoss)
            points.Add(secondBoss);
        points.Sort(static (left, right) => left.coord.CompareTo(right.coord));
        Int("map.nodes", points.Count);
        for (int index = 0; index < points.Count; index++)
        {
            MapPoint point = points[index];
            string children = string.Join(';', point.Children
                .Select(static child => child.coord)
                .OrderBy(static coord => coord.col)
                .ThenBy(static coord => coord.row)
                .Select(Coord));
            Line($"map.node.{index}",
                $"{Coord(point.coord)},{point.PointType},modified={(point.CanBeModified ? 1 : 0)},children={children}");
        }

        List<(byte Act, MapPointHistoryEntry Entry)> history = [];
        for (int act = 0; act < native.MapPointHistory.Count; act++)
        {
            foreach (MapPointHistoryEntry entry in native.MapPointHistory[act])
                history.Add(((byte)act, entry));
        }
        Int("history.count", history.Count);
        for (int index = 0; index < history.Count; index++)
        {
            (byte act, MapPointHistoryEntry entry) = history[index];
            StringBuilder line = new();
            line.Append(act).Append(':').Append(entry.MapPointType);
            foreach (MapPointRoomHistoryEntry room in entry.Rooms)
            {
                line.Append('|').Append(room.RoomType).Append('/');
                line.Append(room.ModelId?.Entry ?? "-").Append('/');
                line.Append(string.Join('+', room.MonsterIds.Select(static id => id.Entry)));
                line.Append('/').Append(room.TurnsTaken);
            }
            Line($"history.{index}", line.ToString());
        }

        Line("visited.coords",
            string.Join(';', native.VisitedMapCoords.Select(static coord => Coord(coord))));
        Line("visited.events", string.Join(',', native.VisitedEventIds
            .Select(static id => (Id: id.Entry, Index: IndexOfEvent(id.Entry)))
            .OrderBy(static item => item.Index)
            .Select(static item => item.Id)));

        Int("rooms.count", native.CurrentRoomCount);
        for (int index = 0; index < native._currentRooms.Count; index++)
        {
            AbstractRoom room = native._currentRooms[index];
            string model = room switch
            {
                CombatRoom combat => combat.ModelId.Entry,
                EventRoom eventRoom => eventRoom.ModelId.Entry,
                _ => "-",
            };
            string parent = room is CombatRoom { ParentEventId: { } parentId }
                ? parentId.Entry
                : "-";
            Line($"room.{index}",
                $"{room.RoomType},{model},{parent},pre={(room.IsPreFinished ? 1 : 0)},gold={F(room is CombatRoom combatRoom ? combatRoom.GoldProportion : 1f)}");
        }

        RngState[] streams = new RngState[RunLimits.RngStreams];
        streams[(int)RunRngStream.UpFront] = Rng(native.Rng.UpFront);
        streams[(int)RunRngStream.UnknownMapPoint] = Rng(native.Rng.UnknownMapPoint);
        streams[(int)RunRngStream.TreasureRoomRelics] = Rng(native.Rng.TreasureRoomRelics);
        streams[(int)RunRngStream.Rewards] = Rng(player.PlayerRng.Rewards);
        streams[(int)RunRngStream.Shops] = Rng(player.PlayerRng.Shops);
        streams[(int)RunRngStream.Transformations] = Rng(player.PlayerRng.Transformations);
        streams[(int)RunRngStream.Shuffle] = Rng(native.Rng.Shuffle);
        streams[(int)RunRngStream.CombatCardGeneration] = Rng(native.Rng.CombatCardGeneration);
        streams[(int)RunRngStream.CombatPotionGeneration] = Rng(native.Rng.CombatPotionGeneration);
        streams[(int)RunRngStream.CombatCardSelection] = Rng(native.Rng.CombatCardSelection);
        streams[(int)RunRngStream.CombatEnergyCosts] = Rng(native.Rng.CombatEnergyCosts);
        streams[(int)RunRngStream.CombatTargets] = Rng(native.Rng.CombatTargets);
        streams[(int)RunRngStream.CombatOrbGeneration] = Rng(native.Rng.CombatOrbGeneration);
        streams[(int)RunRngStream.MonsterAi] = Rng(native.Rng.MonsterAi);
        streams[(int)RunRngStream.Niche] = Rng(native.Rng.Niche);
        for (int index = 0; index < RunLimits.RngStreams; index++)
            Line("rng.stream." + RunRng.StreamName((RunRngStream)index), RunDump.Rng(streams[index]));
        for (int act = 0; act < RunLimits.ActMapRngs; act++)
        {
            // The native map RNG is local to StandardActMap and gone after generation. Store the
            // same derived initial state the importer keeps, and mark acts whose map was generated.
            RngState mapRng = RunRng.ActMap(native.Rng.Seed, act);
            Line($"rng.map.{act + 1}",
                $"{RunDump.Rng(mapRng)},consumed={(act <= native.CurrentActIndex ? 1 : 0)}");
        }
        EventSynchronizer synchronizer = RunManager.Instance.EventSynchronizer;
        EventModel? localEvent = synchronizer is { Events.Count: > 0 }
            ? synchronizer.GetLocalEvent()
            : null;
        int eventCount = localEvent?.Rng == null ? 0 : 1;
        Int("rng.events", eventCount);
        if (eventCount != 0)
        {
            Line("rng.event.0",
                $"{localEvent!.Id.Entry},{RunDump.Rng(Rng(localEvent.Rng!))}");
        }
        return text.ToString();
    }

    private static string FormatReward(Reward reward)
    {
        StringBuilder text = new();
        int flags = 0;
        if (reward.IsPopulated)
            flags |= (int)RunRewardFlags.Populated;
        switch (reward)
        {
            case GoldReward gold:
            {
                if (ReadPrivateField<bool>(gold, "_wasGoldStolenBack"))
                    flags |= (int)RunRewardFlags.GoldStolenBack;
                text.Append("Gold,flags=").Append(flags);
                text.Append(",amount=").Append(gold.Amount);
                text.Append(",amountMax=").Append(gold.Amount);
                return text.ToString();
            }
            case PotionReward potion:
                text.Append("Potion,flags=").Append(flags).Append(',')
                    .Append(potion.Potion?.Id.Entry ?? "-");
                return text.ToString();
            case RelicReward relic:
                text.Append("Relic,flags=").Append(flags).Append(',')
                    .Append(relic.Relic?.Id.Entry ?? "-");
                return text.ToString();
            case CardReward cards:
            {
                CardCreationOptions options = ReadPrivateProperty<CardCreationOptions>(cards,
                    "Options");
                int desired = ReadPrivateProperty<int>(cards, "OptionCount");
                if (cards.CanReroll)
                    flags |= (int)RunRewardFlags.CardCanReroll;
                if ((options.Flags & CardCreationFlags.IsFromCombat) != 0)
                    flags |= (int)RunRewardFlags.CardFromCombat;
                if (ReadPrivateField<bool>(cards, "_cardsWereManuallySet"))
                    flags |= (int)RunRewardFlags.ManualCards;
                text.Append("Card,flags=").Append(flags);
                text.Append(",cardCount=").Append(desired);
                text.Append(",rarity=").Append(options.RarityOdds switch
                {
                    CardRarityOddsType.EliteEncounter => "Elite",
                    CardRarityOddsType.BossEncounter => "Boss",
                    _ => "Regular",
                });
                List<CardModel> models = [.. cards.Cards];
                text.Append(",options=").Append(models.Count);
                text.Append(",cards=[");
                for (int index = 0; index < models.Count; index++)
                {
                    if (index != 0)
                        text.Append(';');
                    text.Append(FormatCard(models[index].ToSerializable()));
                }
                return text.Append(']').ToString();
            }
            case SpecialCardReward special:
            {
                SerializableCard save = special.ToSerializable().SpecialCard
                    ?? throw new InvalidDataException("SpecialCardReward 没有卡牌。");
                text.Append("SpecialCard,flags=").Append(flags);
                text.Append(",cardCount=1,rarity=Regular,options=1,cards=[");
                text.Append(FormatCard(save)).Append(']');
                return text.ToString();
            }
            case CardRemovalReward:
                return $"CardRemoval,flags={flags}";
            default:
                throw new InvalidDataException($"未知奖励类型 {reward.GetType().Name}。");
        }
    }

    private static T ReadPrivateProperty<T>(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMemberException(target.GetType().FullName, name);
        return (T)(property.GetValue(target)
            ?? throw new InvalidDataException($"{target.GetType().Name}.{name} 返回空。"));
    }

    private static T ReadPrivateField<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, name);
        return (T)(field.GetValue(target)
            ?? throw new InvalidDataException($"{target.GetType().Name}.{name} 返回空。"));
    }

    private static RunDecision DecisionFor(AbstractRoom? room) => room?.RoomType switch
    {
        RoomType.Map => RunDecision.MapPoint,
        RoomType.Event => RunDecision.Event,
        RoomType.Shop => RunDecision.Shop,
        RoomType.RestSite => RunDecision.RestSite,
        _ => RunDecision.None,
    };

    private static void DumpBag(StringBuilder text, string prefix, RelicGrabBag bag)
    {
        text.Append(prefix).Append(".refresh=").Append(bag._refreshAllowed ? 1 : 0).Append('\n');
        for (int rarity = 0; rarity < RunLimits.RelicRarities; rarity++)
        {
            string values = bag._deques.TryGetValue((RelicRarity)rarity, out List<RelicModel>? list)
                ? string.Join(',', list.Select(static relic => relic.Id.Entry))
                : "";
            text.Append(prefix).Append('.').Append((RunRelicRarity)rarity).Append('=')
                .Append(values).Append('\n');
        }
    }

    private static string FormatCard(SerializableCard save)
    {
        StringBuilder text = new();
        text.Append(save.Id?.Entry ?? throw new InvalidDataException("卡牌 id 为空。")).Append(',');
        text.Append(save.CurrentUpgradeLevel).Append(',');
        text.Append(save.FloorAddedToDeck ?? 0).Append(',');
        if (save.Enchantment is { } enchantment)
        {
            text.Append(enchantment.Id?.Entry
                ?? throw new InvalidDataException("附魔 id 为空。")).Append(',');
            text.Append(enchantment.Amount);
        }
        else
        {
            text.Append("-,-");
        }
        text.Append(",props=").Append(FormatPropertyText(save.Props));
        text.Append(",enchantProps=")
            .Append(FormatPropertyText(save.Enchantment?.Props));
        return text.ToString();
    }

    private static string FormatRelic(RelicModel relic, int display)
    {
        SerializableRelic save = relic.ToSerializable();
        int flags = (relic.IsWax ? 1 : 0) | (relic.IsMelted ? 2 : 0);
        StringBuilder text = new();
        text.Append(save.Id?.Entry ?? throw new InvalidDataException("遗物 id 为空。")).Append(',');
        text.Append((int)relic.Status).Append(',').Append(flags).Append(',');
        text.Append(relic.StackCount).Append(',').Append(display).Append(',');
        text.Append(save.FloorAddedToDeck ?? 0).Append(",props=")
            .Append(FormatPropertyText(save.Props));
        return text.ToString();
    }

    /// <summary>
    /// Prints native <c>SavedProperties</c> directly, without the Blitz property-bag encoder.
    /// Entry format: <c>nameHash:type:value</c>, sorted by name hash; arrays are printed item by
    /// item and strings verbatim. This is the independent side of the property comparison.
    /// </summary>
    private static string FormatPropertyText(SavedProperties? properties)
    {
        if (properties == null)
            return "-";
        List<(ulong Hash, string Text)> entries = [];
        void AddInt(SavedProperties.SavedProperty<int> property)
            => Add(property.name, "int", property.value.ToString(CultureInfo.InvariantCulture));
        void AddBool(SavedProperties.SavedProperty<bool> property)
            => Add(property.name, "bool", property.value ? "true" : "false");
        void AddString(SavedProperties.SavedProperty<string> property)
            => Add(property.name, "string", property.value ?? "");
        void AddIntArray(SavedProperties.SavedProperty<int[]> property)
            => Add(property.name, "int[]", IntArrayText(property.value));
        void AddModel(SavedProperties.SavedProperty<ModelId> property)
            => Add(property.name, "model", ModelText(property.value));
        void AddCard(SavedProperties.SavedProperty<SerializableCard> property)
            => Add(property.name, "card", FormatCard(property.value));
        void AddCardArray(SavedProperties.SavedProperty<SerializableCard[]> property)
            => Add(property.name, "card[]", CardArrayText(property.value));
        void Add(string name, string type, string value)
        {
            ulong hash = RunHash.XxHash64(Encoding.UTF8.GetBytes(name));
            entries.Add((hash,
                $"{hash.ToString("x16", CultureInfo.InvariantCulture)}:{type}:{value}"));
        }
        if (properties.ints != null)
        {
            foreach (SavedProperties.SavedProperty<int> property in properties.ints)
                AddInt(property);
        }
        if (properties.bools != null)
        {
            foreach (SavedProperties.SavedProperty<bool> property in properties.bools)
                AddBool(property);
        }
        if (properties.strings != null)
        {
            foreach (SavedProperties.SavedProperty<string> property in properties.strings)
                AddString(property);
        }
        if (properties.intArrays != null)
        {
            foreach (SavedProperties.SavedProperty<int[]> property in properties.intArrays)
                AddIntArray(property);
        }
        if (properties.modelIds != null)
        {
            foreach (SavedProperties.SavedProperty<ModelId> property in properties.modelIds)
                AddModel(property);
        }
        if (properties.cards != null)
        {
            foreach (SavedProperties.SavedProperty<SerializableCard> property in properties.cards)
                AddCard(property);
        }
        if (properties.cardArrays != null)
        {
            foreach (SavedProperties.SavedProperty<SerializableCard[]> property in properties.cardArrays)
                AddCardArray(property);
        }
        if (entries.Count == 0)
            return "-";
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

    private static string IntArrayText(int[]? values)
    {
        if (values == null)
            return "[]";
        StringBuilder text = new();
        text.Append('[');
        for (int index = 0; index < values.Length; index++)
        {
            if (index != 0)
                text.Append(',');
            text.Append(values[index].ToString(CultureInfo.InvariantCulture));
        }
        return text.Append(']').ToString();
    }

    private static string CardArrayText(SerializableCard[]? values)
    {
        if (values == null)
            return "[]";
        StringBuilder text = new();
        text.Append('[');
        for (int index = 0; index < values.Length; index++)
        {
            if (index != 0)
                text.Append(';');
            text.Append(FormatCard(values[index]));
        }
        return text.Append(']').ToString();
    }

    private static string ModelText(ModelId? id)
    {
        if (id is null || id == ModelId.none)
            return "None:-";
        return $"{ModelKind(id)}:{id.Entry}";
    }

    private static RunRefKind ModelKind(ModelId id)
    {
        string category = id.Category.ToUpperInvariant();
        return category switch
        {
            "CARD" or "CARDS" => RunRefKind.Card,
            "RELIC" or "RELICS" => RunRefKind.Relic,
            "POTION" or "POTIONS" => RunRefKind.Potion,
            "MONSTER" or "MONSTERS" => RunRefKind.Monster,
            "CHARACTER" or "CHARACTERS" => RunRefKind.Character,
            "ENCHANTMENT" or "ENCHANTMENTS" => RunRefKind.Enchantment,
            "AFFLICTION" or "AFFLICTIONS" => RunRefKind.Affliction,
            "ORB" or "ORBS" => RunRefKind.Orb,
            "ENCOUNTER" or "ENCOUNTERS" => RunRefKind.Encounter,
            "EVENT" or "EVENTS" => RunRefKind.Event,
            "ACT" or "ACTS" => RunRefKind.Act,
            _ => throw new InvalidDataException($"未知 ModelId 类别 {id.Category}.{id.Entry}。"),
        };
    }

    private static int IndexOfEvent(string entry)
        => RunCatalog.TryEvent(entry, out ushort id) ? id : int.MaxValue;

    private static RngState Rng(MegaCrit.Sts2.Core.Random.Rng rng)
    {
        SerializableRng save = rng.ToSerializable();
        return RngState.FromSerializable(save.counter, save.state0, save.state1, save.state2, save.state3);
    }

    private static string Coord(MapCoord coord) => $"{coord.col},{coord.row}";
    private static string Coord(RunCoord coord) => $"{coord.Col},{coord.Row}";
    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
