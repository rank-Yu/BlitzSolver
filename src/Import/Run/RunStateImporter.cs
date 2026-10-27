// Reads the native RunState/Player into the fixed RunState. This is the only place that reads the
// native run save model and its private fields; it never mutates the game.
using System.Reflection;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.Rngs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RunState = BlitzSolver.Simulation.Run.RunState;
using RngState = BlitzSolver.Simulation.Combat.RngState;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;

namespace BlitzSolver.Import;

/// <summary>
/// Native <c>RunState</c> + local <c>Player</c> to <see cref="RunState"/>. The importer is read-only.
/// Unknown/third-party/deprecated content, multiplayer state and out-of-scope modes are rejected
/// with a stable <see cref="ImportReason"/> code; capacities throw <see cref="CapacityException"/>.
/// </summary>
public static class RunStateImporter
{
    public static RunState Import(NativeRunState native, Player localPlayer,
        RewardsSet? rewards = null, IReadOnlyList<Reward>? rewardList = null,
        RunShopStage shopStage = RunShopStage.None)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(localPlayer);
        if (native.Players.Count != 1 || !ReferenceEquals(native.Players[0], localPlayer))
            throw new UnsupportedImportException(ImportReason.MultiPlayer,
                $"跑局有 {native.Players.Count} 名玩家，只导入单人本地玩家");
        if (native.GameMode != GameMode.Standard)
            throw new UnsupportedImportException(ImportReason.UnsupportedGameMode,
                native.GameMode.ToString());
        if (native.Modifiers.Count != 0)
            throw new UnsupportedImportException(ImportReason.UnsupportedModifier,
                string.Join(',', native.Modifiers.Select(m => m.Id.Entry)));
        if (native.Acts.Count > RunLimits.Acts)
            throw new CapacityException("RunState.Acts", RunLimits.Acts);
        if (native.CurrentActIndex < 0 || native.CurrentActIndex >= native.Acts.Count)
            throw new UnsupportedImportException(ImportReason.UnsupportedRun,
                $"CurrentActIndex {native.CurrentActIndex}");

        RunState state = default;
        state.Ascension = native.AscensionLevel;
        state.ActIndex = native.CurrentActIndex;
        state.ActFloor = native.ActFloor;
        state.GameMode = RunGameMode.Standard;
        state.Decision = native.IsGameOver ? RunDecision.GameOver : DecisionFor(native.CurrentRoom);
        state.DecisionOptionCount = 0;
        state.Odds = new RunOdds
        {
            Monster = native.Odds.UnknownMapPoint.MonsterOdds,
            Elite = native.Odds.UnknownMapPoint.EliteOdds,
            Treasure = native.Odds.UnknownMapPoint.TreasureOdds,
            Shop = native.Odds.UnknownMapPoint.ShopOdds,
        };
        state.PlayerOdds = new RunPlayerOdds
        {
            CardRarity = localPlayer.PlayerOdds.CardRarity.CurrentValue,
            PotionReward = localPlayer.PlayerOdds.PotionReward.CurrentValue,
        };
        state.Extra = new RunExtraFields
        {
            StartedWithNeow = native.ExtraFields.StartedWithNeow ? (byte)1 : (byte)0,
            TestSubjectKills = native.ExtraFields.TestSubjectKills,
        };
        state.ExtraPlayer = new RunExtraPlayerFields
        {
            CardShopRemovalsUsed = localPlayer.ExtraFields.CardShopRemovalsUsed,
            WongoPoints = localPlayer.ExtraFields.WongoPoints,
        };

        ImportPlayer(ref state, localPlayer);
        ImportDeck(ref state, localPlayer);
        ImportRelics(ref state, localPlayer);
        ImportBags(ref state, native, localPlayer);
        ImportUnlock(ref state, native.UnlockState);
        ImportActs(ref state, native);
        ImportMap(ref state, native.Map);
        ImportHistory(ref state, native);
        ImportRooms(ref state, native);
        ImportShop(ref state, native);
        if (shopStage == RunShopStage.CardRemoval && state.Shop.Stage == RunShopStage.Shop)
        {
            state.Shop.Stage = RunShopStage.CardRemoval;
            state.Decision = RunDecision.CardSelect;
        }
        ImportRewardPayload(ref state, native, localPlayer, rewards, rewardList);
        ImportVisited(ref state, native);
        ImportRng(ref state, native, localPlayer);
        state.Canonicalize();
        RunStateValidator.Validate(state);
        return state;
    }

    private static RunDecision DecisionFor(AbstractRoom? room) => room?.RoomType switch
    {
        RoomType.Map => RunDecision.MapPoint,
        RoomType.Event => RunDecision.Event,
        RoomType.Shop => RunDecision.Shop,
        RoomType.RestSite => RunDecision.RestSite,
        _ => RunDecision.None,
    };

    private static void ImportPlayer(ref RunState state, Player player)
    {
        if (!RunCatalog.TryCharacter(player.Character.Id.Entry, out ushort character))
            throw Unsupported(ImportReason.UnsupportedCharacter, player.Character.Id.Entry);
        NativeAccess.RequireOriginal(player.Character);
        RunPlayer target = default;
        target.Character = character;
        target.CurrentHp = player.Creature.CurrentHp;
        target.MaxHp = player.Creature.MaxHp;
        target.MaxEnergy = player.MaxEnergy;
        target.Gold = player.Gold;
        target.BaseOrbSlots = player.BaseOrbSlotCount;
        if (player.MaxPotionCount > RunLimits.PotionSlots)
            throw new CapacityException("RunState.PotionSlots", RunLimits.PotionSlots);
        target.Potions.Count = (byte)player.MaxPotionCount;
        for (int slot = 0; slot < player.MaxPotionCount; slot++)
        {
            PotionModel? potion = player.GetPotionAtSlotIndex(slot);
            if (potion is null)
                continue;
            NativeAccess.RequireOriginal(potion);
            if (!Registry.TryPotion(potion.Id.Entry, out ushort id))
                throw Unsupported(ImportReason.UnsupportedPotion, potion.Id.Entry);
            target.Potions.Items[slot] = new RunPotion { Id = id, Present = 1 };
        }
        state.Player = target;
    }

    private static void ImportDeck(ref RunState state, Player player)
    {
        foreach (CardModel card in player.Deck.Cards)
        {
            NativeAccess.RequireOriginal(card);
            state.AddCard(ConvertCardModel(card));
        }
    }

    private static void ImportRelics(ref RunState state, Player player)
    {
        foreach (RelicModel relic in player.Relics)
            state.AddRelic(ConvertRelic(relic));
    }

    private static RunRelic ConvertRelic(RelicModel relic)
    {
        NativeAccess.RequireOriginal(relic);
        SerializableRelic save = relic.ToSerializable();
        if (save.Id is null)
            throw Unsupported(ImportReason.UnsupportedRelic, "<null>");
        if (!Registry.TryRelic(save.Id.Entry, out ushort id))
            throw Unsupported(ImportReason.UnsupportedRelic, save.Id.Entry);
        RunRelic target = default;
        target.Id = id;
        target.Status = (RunRelicStatus)(int)relic.Status;
        if (target.Status is < RunRelicStatus.Normal or > RunRelicStatus.Disabled)
            throw Unsupported(ImportReason.UnsupportedRelicState, $"{save.Id.Entry} status={relic.Status}");
        target.Flags = RunRelicFlags.None;
        if (relic.IsWax)
            target.Flags |= RunRelicFlags.Wax;
        if (relic.IsMelted)
            target.Flags |= RunRelicFlags.Melted;
        if (relic.StackCount is < short.MinValue or > short.MaxValue)
            throw new CapacityException("RunState.RelicStackCount", short.MaxValue);
        target.StackCount = (short)relic.StackCount;
        target.DisplayAmount = relic.DisplayAmount;
        target.FloorAddedToDeck = save.FloorAddedToDeck ?? 0;
        target.Properties = EncodeProperties(save.Props, RunLimits.RelicPropertyBytes, save.Id.Entry);
        return target;
    }

    private static RunCard ConvertCard(SerializableCard save)
    {
        if (save.Id is null)
            throw Unsupported(ImportReason.UnsupportedCard, "<null>");
        if (!Registry.TryCard(save.Id.Entry, out ushort id))
            throw Unsupported(ImportReason.UnsupportedCard, save.Id.Entry);
        RunCard target = default;
        target.Id = id;
        if (save.CurrentUpgradeLevel is < 0 or > byte.MaxValue)
            throw new CapacityException("RunState.CardUpgrade", byte.MaxValue);
        target.Upgrade = (byte)save.CurrentUpgradeLevel;
        target.FloorAddedToDeck = save.FloorAddedToDeck ?? 0;
        target.Properties = EncodeProperties(save.Props, RunLimits.CardPropertyBytes, save.Id.Entry);
        if (save.Enchantment is { } enchantment)
        {
            if (enchantment.Id is null)
                throw Unsupported(ImportReason.UnsupportedEnchantment, "<null>");
            NativeAccess.RequireOriginalType(ModelDb.GetById<EnchantmentModel>(enchantment.Id).GetType());
            if (!Registry.TryEnchantment(enchantment.Id.Entry, out ushort enchantmentId))
                throw Unsupported(ImportReason.UnsupportedEnchantment, enchantment.Id.Entry);
            if (enchantment.Amount is < short.MinValue or > short.MaxValue)
                throw new CapacityException("RunState.EnchantmentAmount", short.MaxValue);
            target.HasEnchantment = 1;
            target.EnchantmentId = enchantmentId;
            target.EnchantmentAmount = (short)enchantment.Amount;
            target.EnchantmentProperties = EncodeProperties(enchantment.Props,
                RunLimits.CardPropertyBytes, save.Id.Entry + ".enchantment");
        }
        return target;
    }

    private static void ImportBags(ref RunState state, NativeRunState native, Player player)
    {
        state.SharedBag = ConvertBag(native.SharedRelicGrabBag, "shared");
        state.PlayerBag = ConvertBag(player.RelicGrabBag, "player");
        state.SharedBag.RefreshAllowed = native.SharedRelicGrabBag._refreshAllowed ? (byte)1 : (byte)0;
        state.PlayerBag.RefreshAllowed = player.RelicGrabBag._refreshAllowed ? (byte)1 : (byte)0;
    }

    private static RunRelicBag ConvertBag(RelicGrabBag bag, string what)
    {
        foreach (RelicRarity rarity in bag._deques.Keys)
        {
            if (!Enum.IsDefined(rarity))
                throw Unsupported(ImportReason.UnsupportedRelicState,
                    $"{what} 遗物袋稀有度 {(int)rarity}");
        }
        if (bag._mpFallbackDequeue.Count != 0)
            throw new UnsupportedImportException(ImportReason.MultiPlayer,
                $"{what} 遗物袋有 {bag._mpFallbackDequeue.Count} 个多人回退项");
        RunRelicBag target = default;
        for (int rarity = 0; rarity < RunLimits.RelicRarities; rarity++)
        {
            if (!bag._deques.TryGetValue((RelicRarity)rarity, out List<RelicModel>? list))
                continue;
            if (list.Count > RunLimits.RelicBagPerRarity)
                throw new CapacityException($"RunState.{what}Bag.{(RelicRarity)rarity}",
                    RunLimits.RelicBagPerRarity);
            RunRelicBagSegment segment = default;
            segment.Count = (ushort)list.Count;
            for (int index = 0; index < list.Count; index++)
            {
                RelicModel relic = list[index];
                NativeAccess.RequireOriginal(relic);
                if (!Registry.TryRelic(relic.Id.Entry, out ushort id))
                    throw Unsupported(ImportReason.UnsupportedRelic, relic.Id.Entry);
                segment.Ids[index] = id;
            }
            target.Segments[rarity] = segment;
        }
        return target;
    }

    private static void ImportUnlock(ref RunState state, UnlockState unlock)
    {
        state.Unlock.NumberOfRuns = unlock.NumberOfRuns;
        foreach (string epoch in NativeAccess.UnlockedEpochs(unlock))
        {
            if (!RunCatalog.TryEpoch(epoch, out ushort id))
                throw Unsupported(ImportReason.UnsupportedUnlock, epoch);
            state.MarkEpoch(id);
        }
        foreach (ModelId encounter in NativeAccess.EncountersSeen(unlock))
        {
            if (!RunCatalog.TryEncounter(encounter.Entry, out ushort id))
                throw Unsupported(ImportReason.UnsupportedUnlock, encounter.Entry);
            state.MarkEncounterSeen(id);
        }
    }

    private static void ImportActs(ref RunState state, NativeRunState native)
    {
        foreach (ActModel act in native.Acts)
        {
            NativeAccess.RequireOriginal(act);
            if (!RunCatalog.TryAct(act.Id.Entry, out ushort actId))
                throw Unsupported(ImportReason.UnsupportedAct, act.Id.Entry);
            RoomSet rooms = act._rooms
                ?? throw new UnsupportedImportException(ImportReason.MissingPrivateField,
                    $"{act.Id.Entry}._rooms");
            RunAct target = default;
            target.Id = actId;
            target.EventsVisited = rooms.eventsVisited;
            target.NormalVisited = rooms.normalEncountersVisited;
            target.EliteVisited = rooms.eliteEncountersVisited;
            target.BossVisited = rooms.bossEncountersVisited;
            if (rooms.events.Count > RunLimits.ActEvents
                || rooms.normalEncounters.Count > RunLimits.ActNormalEncounters
                || rooms.eliteEncounters.Count > RunLimits.ActEliteEncounters)
                throw new CapacityException("RunState.ActRooms", RunLimits.ActEvents);
            target.EventCount = (ushort)rooms.events.Count;
            for (int index = 0; index < rooms.events.Count; index++)
            {
                EventModel eventModel = rooms.events[index];
                NativeAccess.RequireOriginal(eventModel);
                if (!RunCatalog.TryEvent(eventModel.Id.Entry, out ushort id))
                    throw Unsupported(ImportReason.UnsupportedEvent, eventModel.Id.Entry);
                target.Events[index] = id;
            }
            target.NormalCount = (ushort)rooms.normalEncounters.Count;
            for (int index = 0; index < rooms.normalEncounters.Count; index++)
                target.Normal[index] = ConvertEncounter(rooms.normalEncounters[index], act.Id.Entry);
            target.EliteCount = (ushort)rooms.eliteEncounters.Count;
            for (int index = 0; index < rooms.eliteEncounters.Count; index++)
                target.Elite[index] = ConvertEncounter(rooms.eliteEncounters[index], act.Id.Entry);
            target.BossId = ConvertEncounter(rooms.Boss, act.Id.Entry);
            if (rooms.HasAncient)
            {
                NativeAccess.RequireOriginal(rooms.Ancient);
                if (!RunCatalog.TryEvent(rooms.Ancient.Id.Entry, out ushort ancientId))
                    throw Unsupported(ImportReason.UnsupportedEvent, rooms.Ancient.Id.Entry);
                target.HasAncient = 1;
                target.AncientId = ancientId;
            }
            if (rooms.HasSecondBoss)
            {
                target.HasSecondBoss = 1;
                target.SecondBossId = ConvertEncounter(rooms.SecondBoss!, act.Id.Entry);
            }
            state.AddAct(target);
        }
    }

    private static ushort ConvertEncounter(EncounterModel encounter, string where)
    {
        if (encounter is null)
            throw new UnsupportedImportException(ImportReason.MissingPrivateField,
                $"{where} 缺少遭遇");
        NativeAccess.RequireOriginal(encounter);
        if (!RunCatalog.TryEncounter(encounter.Id.Entry, out ushort id))
            throw Unsupported(ImportReason.UnsupportedEncounter, encounter.Id.Entry);
        return id;
    }

    private static void ImportMap(ref RunState state, ActMap map)
    {
        if (map is not (StandardActMap or SavedActMap))
            throw new UnsupportedImportException(ImportReason.UnsupportedMap, map.GetType().Name);
        int width = map.GetColumnCount();
        int height = map.GetRowCount();
        if (width is < 1 or > byte.MaxValue || height is < 1 or > byte.MaxValue)
            throw new CapacityException("RunState.MapSize", byte.MaxValue);
        RunMap target = default;
        target.Width = (byte)width;
        target.Height = (byte)height;
        target.Starting = Coord(map.StartingMapPoint.coord);
        target.Boss = Coord(map.BossMapPoint.coord);
        target.SecondBoss = map.SecondBossMapPoint is { } second ? Coord(second.coord) : default;
        target.HasSecondBoss = map.SecondBossMapPoint != null ? (byte)1 : (byte)0;
        foreach (MapPoint point in map.GetAllMapPoints())
            AddMapPoint(ref target, point);
        AddMapPoint(ref target, map.StartingMapPoint);
        AddMapPoint(ref target, map.BossMapPoint);
        if (map.SecondBossMapPoint is { } secondBoss)
            AddMapPoint(ref target, secondBoss);
        foreach (MapPoint start in map.startMapPoints)
        {
            if (target.StartCount >= RunLimits.MapStarts)
                throw new CapacityException("RunState.MapStarts", RunLimits.MapStarts);
            target.Starts[target.StartCount++] = Coord(start.coord);
        }
        state.Map = target;
    }

    private static void AddMapPoint(ref RunMap target, MapPoint point)
    {
        if (target.NodeCount >= RunLimits.MapPoints)
            throw new CapacityException("RunState.Map", RunLimits.MapPoints);
        RunMapPoint node = default;
        node.Coord = Coord(point.coord);
        if (!Enum.IsDefined(point.PointType))
            throw Unsupported(ImportReason.UnsupportedMap, $"地图点类型 {(int)point.PointType}");
        node.Type = (RunMapPointType)(int)point.PointType;
        node.CanBeModified = point.CanBeModified ? (byte)1 : (byte)0;
        if (point.Children.Count > RunLimits.MapChildren)
            throw new CapacityException("RunState.MapChildren", RunLimits.MapChildren);
        node.ChildCount = (byte)point.Children.Count;
        int child = 0;
        foreach (MapPoint childPoint in point.Children)
            node.Children[child++] = Coord(childPoint.coord);
        target.Nodes[target.NodeCount++] = node;
    }

    private static RunCoord Coord(MapCoord coord)
    {
        if (coord.col is < 0 or > byte.MaxValue || coord.row is < 0 or > byte.MaxValue)
            throw new CapacityException("RunState.MapCoord", byte.MaxValue);
        return new RunCoord((byte)coord.col, (byte)coord.row);
    }

    private static void ImportHistory(ref RunState state, NativeRunState native)
    {
        for (int actIndex = 0; actIndex < native.MapPointHistory.Count; actIndex++)
        {
            foreach (MapPointHistoryEntry entry in native.MapPointHistory[actIndex])
            {
                if (state.History.Count >= RunLimits.MapHistory)
                    throw new CapacityException("RunState.MapHistory", RunLimits.MapHistory);
                RunHistoryEntry target = default;
                if (!Enum.IsDefined(entry.MapPointType))
                    throw Unsupported(ImportReason.UnsupportedMap,
                        $"历史地图点类型 {(int)entry.MapPointType}");
                target.ActIndex = (byte)actIndex;
                target.MapPointType = (RunMapPointType)(int)entry.MapPointType;
                if (entry.Rooms.Count > RunLimits.HistoryRooms)
                    throw new CapacityException("RunState.HistoryRooms", RunLimits.HistoryRooms);
                target.RoomCount = (byte)entry.Rooms.Count;
                for (int room = 0; room < entry.Rooms.Count; room++)
                {
                    MapPointRoomHistoryEntry source = entry.Rooms[room];
                    if (!Enum.IsDefined(source.RoomType))
                        throw Unsupported(ImportReason.UnsupportedRoom,
                            $"历史房间类型 {(int)source.RoomType}");
                    RunHistoryRoom historyRoom = default;
                    historyRoom.RoomType = (RunRoomType)(int)source.RoomType;
                    historyRoom.Model = RefFromModelId(source.ModelId, source.RoomType);
                    historyRoom.TurnsTaken = source.TurnsTaken;
                    if (source.MonsterIds.Count > RunLimits.HistoryMonsters)
                        throw new CapacityException("RunState.HistoryMonsters", RunLimits.HistoryMonsters);
                    historyRoom.MonsterCount = (byte)source.MonsterIds.Count;
                    for (int monster = 0; monster < source.MonsterIds.Count; monster++)
                    {
                        ModelId id = source.MonsterIds[monster];
                        if (!Registry.TryMonster(id.Entry, out ushort monsterId))
                            throw Unsupported(ImportReason.UnsupportedMonster, id.Entry);
                        historyRoom.Monsters[monster] = monsterId;
                    }
                    target.Rooms[room] = historyRoom;
                }
                state.AddHistory(target);
            }
        }
    }

    private static RunRef RefFromModelId(ModelId? id, RoomType roomType)
    {
        if (id is null || id == ModelId.none)
            return RunRef.None;
        return roomType switch
        {
            RoomType.Monster or RoomType.Elite or RoomType.Boss => EncounterRef(id),
            RoomType.Event => EventRef(id),
            _ => RunRef.None,
        };
    }

    private static RunRef EncounterRef(ModelId id)
    {
        if (!RunCatalog.TryEncounter(id.Entry, out ushort value))
            throw Unsupported(ImportReason.UnsupportedEncounter, id.Entry);
        return new RunRef(RunRefKind.Encounter, value);
    }

    private static RunRef EventRef(ModelId id)
    {
        if (!RunCatalog.TryEvent(id.Entry, out ushort value))
            throw Unsupported(ImportReason.UnsupportedEvent, id.Entry);
        return new RunRef(RunRefKind.Event, value);
    }

    private static readonly FieldInfo MerchantCostField =
        typeof(MerchantEntry).GetField("_cost",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(MerchantEntry).FullName, "_cost");

    private static void ImportShop(ref RunState state, NativeRunState native)
    {
        if (native.CurrentRoom is not MerchantRoom room)
            return;
        if (room.Inventories.Count != 1)
            throw new UnsupportedImportException(ImportReason.UnsupportedRoom,
                $"商人房间有 {room.Inventories.Count} 份库存。");
        MerchantInventory inventory = room.Inventories[0];
        state.Shop.Stage = RunShopStage.Shop;
        for (int index = 0; index < RunLimits.ShopCharacterCards; index++)
        {
            state.Shop.CharacterCards[index] = ImportShopCard(
                inventory.CharacterCardEntries[index]);
        }
        for (int index = 0; index < RunLimits.ShopColorlessCards; index++)
        {
            state.Shop.ColorlessCards[index] = ImportShopCard(
                inventory.ColorlessCardEntries[index]);
        }
        for (int index = 0; index < RunLimits.ShopRelics; index++)
            state.Shop.Relics[index] = ImportShopRelic(inventory.RelicEntries[index]);
        for (int index = 0; index < RunLimits.ShopPotions; index++)
            state.Shop.Potions[index] = ImportShopPotion(inventory.PotionEntries[index]);
        MerchantCardRemovalEntry removal = inventory.CardRemovalEntry
            ?? throw new UnsupportedImportException(ImportReason.UnsupportedRoom,
                "商人库存没有删牌条目。");
        state.Shop.Removal = new RunShopRemoval
        {
            Cost = (int)MerchantCostField.GetValue(removal)!,
            Price = removal.Cost,
            Used = removal.Used ? (byte)1 : (byte)0,
        };
    }

    private static RunShopCard ImportShopCard(MerchantCardEntry entry)
    {
        RunShopCard target = default;
        target.Flags = (entry.IsStocked ? RunShopEntryFlags.Stocked : RunShopEntryFlags.None)
            | (entry.IsOnSale ? RunShopEntryFlags.OnSale : RunShopEntryFlags.None);
        if (!entry.IsStocked)
            return target;
        target.Cost = (int)MerchantCostField.GetValue(entry)!;
        target.Price = entry.Cost;
        if (entry.CreationResult is not { } result)
            throw new InvalidDataException("商人卡牌条目没有 CreationResult。");
        target.Card = ConvertCardModel(result.Card);
        return target;
    }

    private static RunShopModel ImportShopRelic(MerchantRelicEntry entry)
    {
        RunShopModel target = default;
        if (!entry.IsStocked)
            return target;
        if (entry.Model?.Id is not { } id || !Registry.TryRelic(id.Entry, out ushort relic))
            throw new UnsupportedImportException(ImportReason.UnsupportedRelic,
                entry.Model?.Id.Entry ?? "<null>");
        target.Flags = RunShopEntryFlags.Stocked;
        target.ModelId = relic;
        target.Cost = (int)MerchantCostField.GetValue(entry)!;
        target.Price = entry.Cost;
        return target;
    }

    private static RunShopModel ImportShopPotion(MerchantPotionEntry entry)
    {
        RunShopModel target = default;
        if (!entry.IsStocked)
            return target;
        if (entry.Model?.Id is not { } id || !Registry.TryPotion(id.Entry, out ushort potion))
            throw new UnsupportedImportException(ImportReason.UnsupportedPotion,
                entry.Model?.Id.Entry ?? "<null>");
        target.Flags = RunShopEntryFlags.Stocked;
        target.ModelId = potion;
        target.Cost = (int)MerchantCostField.GetValue(entry)!;
        target.Price = entry.Cost;
        return target;
    }

    private static void ImportRooms(ref RunState state, NativeRunState native)
    {
        foreach (AbstractRoom room in native._currentRooms)
        {
            if (!Enum.IsDefined(room.RoomType) || room.RoomType == RoomType.Unassigned)
                throw Unsupported(ImportReason.UnsupportedRoom, $"房间类型 {(int)room.RoomType}");
            if (state.Rooms.Count >= RunLimits.RoomStack)
                throw new CapacityException("RunState.RoomStack", RunLimits.RoomStack);
            RunRoom target = default;
            target.RoomType = (RunRoomType)(int)room.RoomType;
            target.IsPreFinished = room.IsPreFinished ? (byte)1 : (byte)0;
            target.GoldProportion = 1f;
            switch (room)
            {
                case CombatRoom combat:
                    target.Model = EncounterRef(combat.ModelId);
                    target.ParentEvent = combat.ParentEventId is { } parent
                        ? EventRef(parent)
                        : RunRef.None;
                    target.GoldProportion = combat.GoldProportion;
                    break;
                case EventRoom eventRoom:
                    target.Model = EventRef(eventRoom.ModelId);
                    break;
                default:
                    target.Model = RunRef.None;
                    break;
            }
            state.AddRoom(target);
        }
    }

    private static void ImportRewardPayload(ref RunState state, NativeRunState native,
        Player localPlayer, RewardsSet? rewards, IReadOnlyList<Reward>? rewardList)
    {
        if (rewards != null)
        {
            state.Rewards.Generated = 1;
            state.Rewards.Stage = RunRewardStage.List;
            foreach (Reward reward in rewardList ?? rewards.Rewards)
                ImportReward(ref state, reward);
            state.Canonicalize();
            return;
        }
        if (native.CurrentRoom is not CombatRoom combat)
            return;
        if (!combat.ExtraRewards.TryGetValue(localPlayer, out List<Reward>? extras))
            return;
        foreach (Reward reward in extras)
            ImportReward(ref state, reward);
        state.Canonicalize();
    }

    private static void ImportReward(ref RunState state, Reward reward)
    {
        RunReward target = default;
        target.Amount = -1;
        target.AmountMax = -1;
        switch (reward)
        {
            case GoldReward gold:
            {
                target.Type = RunRewardType.Gold;
                target.Amount = gold.Amount;
                target.AmountMax = gold.Amount;
                if (gold.Amount >= 0)
                    target.Flags |= RunRewardFlags.Populated;
                if (ReadPrivateField<bool>(gold, "_wasGoldStolenBack"))
                    target.Flags |= RunRewardFlags.GoldStolenBack;
                break;
            }
            case PotionReward potion:
            {
                target.Type = RunRewardType.Potion;
                if (potion.Potion is { } model)
                {
                    NativeAccess.RequireOriginal(model);
                    if (!Registry.TryPotion(model.Id.Entry, out ushort id))
                        throw Unsupported(ImportReason.UnsupportedPotion, model.Id.Entry);
                    target.ModelId = id;
                    target.Flags |= RunRewardFlags.Populated;
                }
                break;
            }
            case RelicReward relic:
            {
                target.Type = RunRewardType.Relic;
                if (relic.Relic is { } model)
                {
                    NativeAccess.RequireOriginal(model);
                    if (!Registry.TryRelic(model.Id.Entry, out ushort id))
                        throw Unsupported(ImportReason.UnsupportedRelic, model.Id.Entry);
                    target.ModelId = id;
                    target.Flags |= RunRewardFlags.Populated;
                }
                break;
            }
            case CardReward cards:
            {
                target.Type = RunRewardType.Card;
                CardCreationOptions options = ReadPrivateProperty<CardCreationOptions>(cards,
                    "Options");
                target.CardCount = checked((byte)ReadPrivateProperty<int>(cards, "OptionCount"));
                target.RaritySource = options.RarityOdds switch
                {
                    CardRarityOddsType.EliteEncounter => (byte)RoomRarityOdds.Elite,
                    CardRarityOddsType.BossEncounter => (byte)RoomRarityOdds.Boss,
                    _ => (byte)RoomRarityOdds.Regular,
                };
                if (cards.CanReroll)
                    target.Flags |= RunRewardFlags.CardCanReroll;
                if ((options.Flags & CardCreationFlags.IsFromCombat) != 0)
                    target.Flags |= RunRewardFlags.CardFromCombat;
                if (ReadPrivateField<bool>(cards, "_cardsWereManuallySet"))
                    target.Flags |= RunRewardFlags.ManualCards;
                if (cards.IsPopulated)
                {
                    List<CardModel> cardModels = [.. cards.Cards];
                    if (cardModels.Count > RunLimits.RewardCardOptions)
                        throw new CapacityException("RunState.RewardCardOptions",
                            RunLimits.RewardCardOptions);
                    byte start = state.ReserveRewardCards(cardModels.Count);
                    for (int index = 0; index < cardModels.Count; index++)
                    {
                        NativeAccess.RequireOriginal(cardModels[index]);
                        state.SetRewardCard(start + index, ConvertCardModel(cardModels[index]));
                    }
                    target.OptionStart = start;
                    target.OptionCount = checked((byte)cardModels.Count);
                    target.Flags |= RunRewardFlags.Populated;
                }
                break;
            }
            case SpecialCardReward special:
            {
                target.Type = RunRewardType.SpecialCard;
                SerializableCard save = special.ToSerializable().SpecialCard
                    ?? throw new InvalidDataException("SpecialCardReward 没有卡牌。");
                RunCard card = ConvertCard(save);
                target.OptionStart = state.ReserveRewardCards(1);
                target.OptionCount = 1;
                target.CardCount = 1;
                target.Flags |= RunRewardFlags.Populated;
                state.SetRewardCard(target.OptionStart, card);
                break;
            }
            case CardRemovalReward:
                target.Type = RunRewardType.CardRemoval;
                target.Flags |= RunRewardFlags.Populated;
                break;
            default:
                throw Unsupported(ImportReason.UnsupportedRun,
                    $"未知奖励类型 {reward.GetType().Name}");
        }
        state.AddReward(target);
    }

    internal static RunCard ConvertCardModel(CardModel card)
    {
        RunCard target = ConvertCard(card.ToSerializable());
        if (card.IsRemovable)
            target.Flags |= RunCardFlags.Removable;
        if (card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse)
            target.Flags |= RunCardFlags.Curse;
        return target;
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

    private static void ImportVisited(ref RunState state, NativeRunState native)
    {
        foreach (MapCoord coord in native.VisitedMapCoords)
            state.AddVisitedCoord(Coord(coord));
        foreach (ModelId eventId in native.VisitedEventIds)
        {
            if (!RunCatalog.TryEvent(eventId.Entry, out ushort id))
                throw Unsupported(ImportReason.UnsupportedEvent, eventId.Entry);
            state.MarkVisitedEvent(id);
        }
    }

    private static void ImportRng(ref RunState state, NativeRunState native, Player player)
    {
        ulong runSeed = native.Rng.Seed;
        ulong playerSeed = player.PlayerRng.Seed;
        state.Rng.Seed = runSeed;
        state.Rng.PlayerSeed = playerSeed;
        state.Rng.Streams[(int)RunRngStream.UpFront] = Rng(native.Rng.GetRng(RunRngType.UpFront));
        state.Rng.Streams[(int)RunRngStream.UnknownMapPoint] =
            Rng(native.Rng.GetRng(RunRngType.UnknownMapPoint));
        state.Rng.Streams[(int)RunRngStream.TreasureRoomRelics] =
            Rng(native.Rng.GetRng(RunRngType.TreasureRoomRelics));
        state.Rng.Streams[(int)RunRngStream.Rewards] = Rng(player.PlayerRng.GetRng(PlayerRngType.Rewards));
        state.Rng.Streams[(int)RunRngStream.Shops] = Rng(player.PlayerRng.GetRng(PlayerRngType.Shops));
        state.Rng.Streams[(int)RunRngStream.Transformations] =
            Rng(player.PlayerRng.GetRng(PlayerRngType.Transformations));
        state.Rng.Streams[(int)RunRngStream.Shuffle] = Rng(native.Rng.GetRng(RunRngType.Shuffle));
        state.Rng.Streams[(int)RunRngStream.CombatCardGeneration] =
            Rng(native.Rng.GetRng(RunRngType.CombatCardGeneration));
        state.Rng.Streams[(int)RunRngStream.CombatPotionGeneration] =
            Rng(native.Rng.GetRng(RunRngType.CombatPotionGeneration));
        state.Rng.Streams[(int)RunRngStream.CombatCardSelection] =
            Rng(native.Rng.GetRng(RunRngType.CombatCardSelection));
        state.Rng.Streams[(int)RunRngStream.CombatEnergyCosts] =
            Rng(native.Rng.GetRng(RunRngType.CombatEnergyCosts));
        state.Rng.Streams[(int)RunRngStream.CombatTargets] =
            Rng(native.Rng.GetRng(RunRngType.CombatTargets));
        state.Rng.Streams[(int)RunRngStream.CombatOrbGeneration] =
            Rng(native.Rng.GetRng(RunRngType.CombatOrbs));
        state.Rng.Streams[(int)RunRngStream.MonsterAi] =
            Rng(native.Rng.GetRng(RunRngType.MonsterAi));
        state.Rng.Streams[(int)RunRngStream.Niche] = Rng(native.Rng.GetRng(RunRngType.Niche));
        for (int act = 0; act < RunLimits.ActMapRngs; act++)
        {
            state.Rng.Map[act] = RunRng.ActMap(runSeed, act);
            state.Rng.MapConsumed[act] = act <= native.CurrentActIndex ? (byte)1 : (byte)0;
        }
        ImportEventRng(ref state, native);
    }

    private static void ImportEventRng(ref RunState state, NativeRunState native)
    {
        EventSynchronizer synchronizer = RunManager.Instance.EventSynchronizer;
        if (synchronizer == null || synchronizer.Events.Count == 0)
            return;
        EventModel? local = synchronizer.GetLocalEvent();
        if (local?.Rng == null)
            return;
        if (!RunCatalog.TryEvent(local.Id.Entry, out ushort eventId))
            throw Unsupported(ImportReason.UnsupportedEvent, local.Id.Entry);
        state.AddEventRng(new RunEventRng
        {
            EventId = eventId,
            Active = 1,
            State = Rng(local.Rng),
        });
    }

    private static RngState Rng(MegaCrit.Sts2.Core.Random.Rng rng)
    {
        SerializableRng save = rng.ToSerializable();
        return RngState.FromSerializable(save.counter, save.state0, save.state1, save.state2, save.state3);
    }

    internal static RunPropertyBag EncodeProperties(SavedProperties? properties, int capacity, string where)
    {
        RunPropertyBag result = default;
        if (properties == null)
            return result;
        List<(ulong Hash, RunPropertyBag Entry)> entries = [];
        if (properties.ints != null)
        {
            foreach (SavedProperties.SavedProperty<int> property in properties.ints)
                entries.Add((RunProperties.NameHash(property.name), Entry(where, property.name,
                    capacity, (ref RunPropertyBag bag) => RunProperties.AddInt(ref bag,
                        RunProperties.NameHash(property.name), property.value, capacity))));
        }
        if (properties.bools != null)
        {
            foreach (SavedProperties.SavedProperty<bool> property in properties.bools)
                entries.Add((RunProperties.NameHash(property.name), Entry(where, property.name,
                    capacity, (ref RunPropertyBag bag) => RunProperties.AddBool(ref bag,
                        RunProperties.NameHash(property.name), property.value, capacity))));
        }
        if (properties.strings != null)
        {
            foreach (SavedProperties.SavedProperty<string> property in properties.strings)
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(property.value ?? string.Empty);
                entries.Add((RunProperties.NameHash(property.name), Entry(where, property.name,
                    capacity, (ref RunPropertyBag bag) => RunProperties.AddString(ref bag,
                        RunProperties.NameHash(property.name), bytes, capacity))));
            }
        }
        if (properties.intArrays != null)
        {
            foreach (SavedProperties.SavedProperty<int[]> property in properties.intArrays)
                entries.Add((RunProperties.NameHash(property.name), Entry(where, property.name,
                    capacity, (ref RunPropertyBag bag) => RunProperties.AddIntArray(ref bag,
                        RunProperties.NameHash(property.name), property.value, capacity))));
        }
        if (properties.modelIds != null)
        {
            foreach (SavedProperties.SavedProperty<ModelId> property in properties.modelIds)
                entries.Add((RunProperties.NameHash(property.name), Entry(where, property.name,
                    capacity, (ref RunPropertyBag bag) => RunProperties.AddModel(ref bag,
                        RunProperties.NameHash(property.name), ModelRef(property.value), capacity))));
        }
        if (properties.cards != null)
        {
            foreach (SavedProperties.SavedProperty<SerializableCard> property in properties.cards)
                entries.Add((RunProperties.NameHash(property.name), Entry(where, property.name,
                    capacity, (ref RunPropertyBag bag) => RunProperties.AddCard(ref bag,
                        RunProperties.NameHash(property.name), ConvertCard(property.value), capacity))));
        }
        if (properties.cardArrays != null)
        {
            foreach (SavedProperties.SavedProperty<SerializableCard[]> property in properties.cardArrays)
            {
                RunCard[] cards = new RunCard[property.value.Length];
                for (int index = 0; index < property.value.Length; index++)
                    cards[index] = ConvertCard(property.value[index]);
                entries.Add((RunProperties.NameHash(property.name), Entry(where, property.name,
                    capacity, (ref RunPropertyBag bag) => RunProperties.AddCardArray(ref bag,
                        RunProperties.NameHash(property.name), cards, capacity))));
            }
        }
        if (entries.Count > RunLimits.PropertyCount)
            throw new CapacityException($"RunState.{where}.Properties", RunLimits.PropertyCount);
        entries.Sort(static (left, right) => left.Hash.CompareTo(right.Hash));
        foreach ((ulong _, RunPropertyBag entry) in entries)
            RunProperties.Append(ref result, entry, capacity);
        return result;
    }

    private delegate void EntryWriter(ref RunPropertyBag bag);

    private static RunPropertyBag Entry(string where, string name, int capacity, EntryWriter write)
    {
        try
        {
            RunPropertyBag bag = default;
            write(ref bag);
            return bag;
        }
        catch (CapacityException error)
        {
            throw new CapacityException($"{where}.{name}", error.Capacity);
        }
        catch (ArgumentException error)
        {
            throw new UnsupportedImportException(ImportReason.UnsupportedProperty,
                $"{where}.{name}: {error.Message}");
        }
    }

    private static RunRef ModelRef(ModelId id)
    {
        string category = id.Category.ToUpperInvariant();
        if (category is "CARD" or "CARDS")
        {
            if (!Registry.TryCard(id.Entry, out ushort value))
                throw Unsupported(ImportReason.UnsupportedCard, id.Entry);
            return new RunRef(RunRefKind.Card, value);
        }
        if (category is "RELIC" or "RELICS")
        {
            if (!Registry.TryRelic(id.Entry, out ushort value))
                throw Unsupported(ImportReason.UnsupportedRelic, id.Entry);
            return new RunRef(RunRefKind.Relic, value);
        }
        if (category is "POTION" or "POTIONS")
        {
            if (!Registry.TryPotion(id.Entry, out ushort value))
                throw Unsupported(ImportReason.UnsupportedPotion, id.Entry);
            return new RunRef(RunRefKind.Potion, value);
        }
        if (category is "MONSTER" or "MONSTERS")
        {
            if (!Registry.TryMonster(id.Entry, out ushort value))
                throw Unsupported(ImportReason.UnsupportedMonster, id.Entry);
            return new RunRef(RunRefKind.Monster, value);
        }
        if (category is "CHARACTER" or "CHARACTERS")
        {
            if (!RunCatalog.TryCharacter(id.Entry, out ushort value))
                throw Unsupported(ImportReason.UnsupportedCharacter, id.Entry);
            return new RunRef(RunRefKind.Character, value);
        }
        if (category is "ENCHANTMENT" or "ENCHANTMENTS")
        {
            if (!Registry.TryEnchantment(id.Entry, out ushort value))
                throw Unsupported(ImportReason.UnsupportedEnchantment, id.Entry);
            return new RunRef(RunRefKind.Enchantment, value);
        }
        if (category is "AFFLICTION" or "AFFLICTIONS")
        {
            if (!Registry.TryAffliction(id.Entry, out ushort value))
                throw Unsupported(ImportReason.UnsupportedAffliction, id.Entry);
            return new RunRef(RunRefKind.Affliction, value);
        }
        if (category is "ORB" or "ORBS")
        {
            if (!Registry.TryOrb(id.Entry, out ushort value))
                throw Unsupported(ImportReason.UnsupportedOrb, id.Entry);
            return new RunRef(RunRefKind.Orb, value);
        }
        if (category is "ENCOUNTER" or "ENCOUNTERS")
        {
            if (!RunCatalog.TryEncounter(id.Entry, out ushort value))
                throw Unsupported(ImportReason.UnsupportedEncounter, id.Entry);
            return new RunRef(RunRefKind.Encounter, value);
        }
        if (category is "EVENT" or "EVENTS")
        {
            if (!RunCatalog.TryEvent(id.Entry, out ushort value))
                throw Unsupported(ImportReason.UnsupportedEvent, id.Entry);
            return new RunRef(RunRefKind.Event, value);
        }
        if (category is "ACT" or "ACTS")
        {
            if (!RunCatalog.TryAct(id.Entry, out ushort value))
                throw Unsupported(ImportReason.UnsupportedAct, id.Entry);
            return new RunRef(RunRefKind.Act, value);
        }
        throw new UnsupportedImportException(ImportReason.UnsupportedProperty,
            $"未知 ModelId 类别 {id.Category}.{id.Entry}");
    }

    private static UnsupportedImportException Unsupported(string reason, string detail)
        => new(reason, detail);
}
