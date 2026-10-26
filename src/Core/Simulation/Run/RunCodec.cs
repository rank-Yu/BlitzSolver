using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Whole-block <see cref="RunState"/> file: magic, format version, exact struct size and the
/// canonical byte image. The state is unmanaged and reference-free, so the byte image is the whole
/// state once inactive slots and order-independent collections are canonicalized.
/// </summary>
public static class RunCodec
{
    internal const uint Magic = 0x534E5552; // "RUNS"
    internal const ushort FormatVersion = 1;

    public const int HeaderSize = 4 + 2 + 4;

    public static byte[] Encode(in RunState state)
    {
        int size = Unsafe.SizeOf<RunState>();
        if (size != RunState.ExpectedSize)
            throw new InvalidOperationException(
                $"RunState 大小 {size} 与布局断言 {RunState.ExpectedSize} 不符。");
        RunStateValidator.ValidateCounts(state);
        RunState canonical = state;
        canonical.Canonicalize();
        RunStateValidator.Validate(canonical);
        byte[] bytes = new byte[HeaderSize + size];
        Span<byte> header = bytes.AsSpan(0, HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[6..], size);
        MemoryMarshal.Write(bytes.AsSpan(HeaderSize), in canonical);
        return bytes;
    }

    public static RunState Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
            throw new InvalidDataException($"RunState 数据只有 {bytes.Length} 字节，短于头部 {HeaderSize}。");
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (magic != Magic)
            throw new InvalidDataException($"RunState 魔数 0x{magic:X8} 不是 0x{Magic:X8}。");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        if (version != FormatVersion)
            throw new InvalidDataException($"RunState 版本 {version} 不受支持（当前 {FormatVersion}）。");
        int size = BinaryPrimitives.ReadInt32LittleEndian(bytes[6..]);
        int expected = Unsafe.SizeOf<RunState>();
        if (size != expected || bytes.Length != HeaderSize + expected)
            throw new InvalidDataException(
                $"RunState 大小 {size}/{bytes.Length} 与布局 {expected} 不符。");
        RunState state = MemoryMarshal.Read<RunState>(bytes[HeaderSize..]);
        RunStateValidator.Validate(state);
        return state;
    }

    public static ulong Hash(in RunState state)
    {
        byte[] bytes = Encode(state);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }
}

/// <summary>Structural checks shared by the codec, importer and tests.</summary>
public static class RunStateValidator
{
    /// <summary>Count bounds only; used before canonicalization can index the fixed arrays.</summary>
    public static void ValidateCounts(in RunState state)
    {
        if (state.Deck.Count > RunLimits.DeckCards)
            throw new InvalidDataException($"RunState 牌组 {state.Deck.Count} 超出 {RunLimits.DeckCards}。");
        if (state.Relics.Count > RunLimits.Relics)
            throw new InvalidDataException($"RunState 遗物 {state.Relics.Count} 超出 {RunLimits.Relics}。");
        if (state.Player.Potions.Count > RunLimits.PotionSlots)
            throw new InvalidDataException(
                $"RunState 药水槽 {state.Player.Potions.Count} 超出 {RunLimits.PotionSlots}。");
        if (state.Acts.Count > RunLimits.Acts)
            throw new InvalidDataException($"RunState.Acts.Count {state.Acts.Count} 超出。");
        if (state.Map.NodeCount > RunLimits.MapPoints
            || state.Map.StartCount > RunLimits.MapStarts
            || state.Visited.CoordCount > RunLimits.VisitedCoords
            || state.History.Count > RunLimits.MapHistory
            || state.Rooms.Count > RunLimits.RoomStack
            || state.Rng.EventCount > RunLimits.EventRngs)
            throw new InvalidDataException("RunState 数组计数超出容量。");
        for (int index = 0; index < RunLimits.Acts; index++)
        {
            RunAct act = state.Acts.Items[index];
            if (act.EventCount > RunLimits.ActEvents
                || act.NormalCount > RunLimits.ActNormalEncounters
                || act.EliteCount > RunLimits.ActEliteEncounters)
                throw new InvalidDataException("RunState 幕房间序列计数超出容量。");
        }
        for (int index = 0; index < RunLimits.MapPoints; index++)
        {
            if (state.Map.Nodes[index].ChildCount > RunLimits.MapChildren)
                throw new InvalidDataException("RunState 地图子节点数超出容量。");
        }
        for (int rarity = 0; rarity < RunLimits.RelicRarities; rarity++)
        {
            if (state.SharedBag.Segments[rarity].Count > RunLimits.RelicBagPerRarity
                || state.PlayerBag.Segments[rarity].Count > RunLimits.RelicBagPerRarity)
                throw new InvalidDataException("RunState 遗物袋段计数超出容量。");
        }
    }

    public static void Validate(in RunState state)
    {
        ValidateCounts(state);
        if (state.GameMode is < RunGameMode.None or > RunGameMode.Custom)
            throw new InvalidDataException($"RunState.GameMode {state.GameMode} 非法。");
        if (state.Decision is < RunDecision.None or > RunDecision.GameOver)
            throw new InvalidDataException($"RunState.Decision {state.Decision} 非法。");
        if (state.Acts.Count is 0 or > RunLimits.Acts)
            throw new InvalidDataException($"RunState.Acts.Count {state.Acts.Count} 非法。");
        if ((uint)state.ActIndex >= state.Acts.Count)
            throw new InvalidDataException($"RunState.ActIndex {state.ActIndex} 超出幕数 {state.Acts.Count}。");
        if (state.CardCount > RunLimits.DeckCards)
            throw new InvalidDataException($"RunState 牌组 {state.CardCount} 超出 {RunLimits.DeckCards}。");
        if (state.RelicCount > RunLimits.Relics)
            throw new InvalidDataException($"RunState 遗物 {state.RelicCount} 超出 {RunLimits.Relics}。");
        if (state.Player.Potions.Count > RunLimits.PotionSlots)
            throw new InvalidDataException($"RunState 药水槽 {state.Player.Potions.Count} 超出 {RunLimits.PotionSlots}。");
        if (state.Map.NodeCount > RunLimits.MapPoints
            || state.Map.StartCount > RunLimits.MapStarts
            || state.Visited.CoordCount > RunLimits.VisitedCoords
            || state.History.Count > RunLimits.MapHistory
            || state.Rooms.Count > RunLimits.RoomStack
            || state.Rng.EventCount > RunLimits.EventRngs)
            throw new InvalidDataException("RunState 数组计数超出容量。");

        ValidateCollectionBits(state);
        for (int index = 0; index < state.CardCount; index++)
            ValidateCard(state.Deck.Cards[index], $"deck[{index}]");
        for (int index = 0; index < state.RelicCount; index++)
            ValidateRelic(state.Relics.Items[index], $"relic[{index}]");
        for (int index = 0; index < state.Player.Potions.Count; index++)
        {
            RunPotion potion = state.Player.Potions.Items[index];
            if (potion.Present > 1)
                throw new InvalidDataException($"potion[{index}] Present={potion.Present} 非法。");
            if (potion.Present != 0 && potion.Id >= Registry.PotionCount)
                throw new InvalidDataException($"potion[{index}] id={potion.Id} 未知。");
        }
        ValidateBag(state.SharedBag, "sharedBag");
        ValidateBag(state.PlayerBag, "playerBag");
        for (int index = 0; index < state.Acts.Count; index++)
            ValidateAct(state.Acts.Items[index], $"act[{index}]");
        ValidateMap(state.Map);
        ValidateHistory(state.History, state.Acts.Count);
        for (int index = 0; index < state.Rooms.Count; index++)
            ValidateRoom(state.Rooms.Items[index], $"room[{index}]");
        for (int index = 0; index < state.Rng.EventCount; index++)
        {
            RunEventRng eventRng = state.Rng.Events[index];
            if (eventRng.EventId >= RunCatalog.EventCount)
                throw new InvalidDataException($"eventRng[{index}] id={eventRng.EventId} 未知。");
            if (index > 0 && state.Rng.Events[index - 1].EventId >= eventRng.EventId)
                throw new InvalidDataException("eventRng 未按 event id 排序。");
        }
        int expectedSize = Unsafe.SizeOf<RunState>();
        if (RunState.ExpectedSize != 0 && expectedSize != RunState.ExpectedSize)
            throw new InvalidDataException(
                $"RunState 大小 {expectedSize} 与布局断言 {RunState.ExpectedSize} 不符。");
    }

    private static void ValidateCollectionBits(in RunState state)
    {
        for (int index = 0; index < RunLimits.UnlockEpochWords; index++)
            CheckWord(state.Unlock.Epochs[index], index, RunCatalog.EpochCount, "unlock.epochs");
        for (int index = 0; index < RunLimits.UnlockEncounterWords; index++)
            CheckWord(state.Unlock.EncountersSeen[index], index, RunCatalog.EncounterCount,
                "unlock.encounters");
        for (int index = 0; index < RunLimits.VisitedEventWords; index++)
            CheckWord(state.Visited.Events[index], index, RunCatalog.EventCount, "visited.events");
    }

    private static void CheckWord(ulong value, int index, int count, string what)
    {
        int first = index * 64;
        if (first >= count)
        {
            if (value != 0)
                throw new InvalidDataException($"{what} 第 {index} 字有超出内容的位。");
            return;
        }
        int bits = Math.Min(64, count - first);
        if (bits < 64 && (value >> bits) != 0)
            throw new InvalidDataException($"{what} 第 {index} 字有超出内容的位。");
    }

    private static void ValidateCard(in RunCard card, string what)
    {
        if (card.Id >= Registry.CardCount)
            throw new InvalidDataException($"{what} 卡牌 id={card.Id} 未知。");
        if (card.HasEnchantment > 1)
            throw new InvalidDataException($"{what} HasEnchantment={card.HasEnchantment} 非法。");
        if (card.HasEnchantment != 0 && card.EnchantmentId >= Registry.EnchantmentCount)
            throw new InvalidDataException($"{what} 附魔 id={card.EnchantmentId} 未知。");
        RunProperties.Validate(card.Properties, RunLimits.CardPropertyBytes, what + ".props");
        RunProperties.Validate(card.EnchantmentProperties, RunLimits.CardPropertyBytes,
            what + ".enchantProps");
    }

    private static void ValidateRelic(in RunRelic relic, string what)
    {
        if (relic.Id >= Registry.RelicCount)
            throw new InvalidDataException($"{what} 遗物 id={relic.Id} 未知。");
        if (relic.Status is < RunRelicStatus.Normal or > RunRelicStatus.Disabled)
            throw new InvalidDataException($"{what} Status={relic.Status} 非法。");
        if ((relic.Flags & ~(RunRelicFlags.Wax | RunRelicFlags.Melted)) != 0)
            throw new InvalidDataException($"{what} Flags={relic.Flags} 非法。");
        RunProperties.Validate(relic.Properties, RunLimits.RelicPropertyBytes, what + ".props");
    }

    private static void ValidateBag(in RunRelicBag bag, string what)
    {
        for (int rarity = 0; rarity < RunLimits.RelicRarities; rarity++)
        {
            RunRelicBagSegment segment = bag.Segments[rarity];
            if (segment.Count > RunLimits.RelicBagPerRarity)
                throw new InvalidDataException($"{what}.{rarity} 数量 {segment.Count} 超出。");
            for (int index = 0; index < segment.Count; index++)
            {
                if (segment.Ids[index] >= Registry.RelicCount)
                    throw new InvalidDataException($"{what}.{rarity}[{index}] 遗物 id 未知。");
            }
        }
    }

    private static void ValidateAct(in RunAct act, string what)
    {
        if (act.Id >= RunCatalog.ActCount)
            throw new InvalidDataException($"{what}.id={act.Id} 未知。");
        if (act.EventCount > RunLimits.ActEvents
            || act.NormalCount > RunLimits.ActNormalEncounters
            || act.EliteCount > RunLimits.ActEliteEncounters)
            throw new InvalidDataException($"{what} 房间序列超出容量。");
        if (act.HasAncient > 1 || act.HasSecondBoss > 1)
            throw new InvalidDataException($"{what} 布尔标记非法。");
        if (act.HasAncient != 0 && act.AncientId >= RunCatalog.EventCount)
            throw new InvalidDataException($"{what}.ancient={act.AncientId} 未知。");
        if (act.BossId >= RunCatalog.EncounterCount)
            throw new InvalidDataException($"{what}.boss={act.BossId} 未知。");
        if (act.HasSecondBoss != 0 && act.SecondBossId >= RunCatalog.EncounterCount)
            throw new InvalidDataException($"{what}.secondBoss={act.SecondBossId} 未知。");
        for (int index = 0; index < act.EventCount; index++)
            RequireEvent(act.Events[index], $"{what}.event[{index}]");
        for (int index = 0; index < act.NormalCount; index++)
            RequireEncounter(act.Normal[index], $"{what}.normal[{index}]");
        for (int index = 0; index < act.EliteCount; index++)
            RequireEncounter(act.Elite[index], $"{what}.elite[{index}]");
    }

    private static void ValidateMap(in RunMap map)
    {
        if (map.Width is 0 or > 16 || map.Height is 0 or > 64)
            throw new InvalidDataException($"地图尺寸 {map.Width}x{map.Height} 非法。");
        for (int index = 0; index < map.NodeCount; index++)
        {
            RunMapPoint point = map.Nodes[index];
            if (point.Type is < RunMapPointType.Unassigned or > RunMapPointType.Ancient)
                throw new InvalidDataException($"map[{index}].type={point.Type} 非法。");
            if (point.CanBeModified > 1 || point.ChildCount > RunLimits.MapChildren)
                throw new InvalidDataException($"map[{index}] 标记或子节点数非法。");
            if ((point.Coord.Col >= map.Width || point.Coord.Row >= map.Height)
                && point.Type is not (RunMapPointType.Ancient or RunMapPointType.Boss))
                throw new InvalidDataException($"map[{index}] 坐标 {point.Coord} 超出网格。");
        }
        RequirePoint(map, map.Starting, "map.start");
        RequirePoint(map, map.Boss, "map.boss");
        if (map.HasSecondBoss > 1)
            throw new InvalidDataException("map.HasSecondBoss 非法。");
        if (map.HasSecondBoss != 0)
            RequirePoint(map, map.SecondBoss, "map.secondBoss");
        for (int index = 0; index < map.NodeCount; index++)
        {
            RunMapPoint point = map.Nodes[index];
            for (int child = 0; child < point.ChildCount; child++)
                RequirePoint(map, point.Children[child], $"map[{index}].child[{child}]");
        }
        for (int index = 0; index < map.StartCount; index++)
            RequirePoint(map, map.Starts[index], $"map.start[{index}]");
    }

    private static void RequirePoint(in RunMap map, RunCoord coord, string what)
    {
        for (int index = 0; index < map.NodeCount; index++)
        {
            if (map.Nodes[index].Coord.Equals(coord))
                return;
        }
        throw new InvalidDataException($"{what}={coord} 不在节点表内。");
    }

    private static void ValidateHistory(in RunHistory history, int actCount)
    {
        for (int index = 0; index < history.Count; index++)
        {
            RunHistoryEntry entry = history.Entries[index];
            if (entry.ActIndex >= actCount)
                throw new InvalidDataException($"history[{index}].act={entry.ActIndex} 超出幕数。");
            if (entry.MapPointType is < RunMapPointType.Unassigned or > RunMapPointType.Ancient)
                throw new InvalidDataException($"history[{index}].type={entry.MapPointType} 非法。");
            if (entry.RoomCount > RunLimits.HistoryRooms)
                throw new InvalidDataException($"history[{index}] 房间数超出。");
            for (int room = 0; room < entry.RoomCount; room++)
            {
                RunHistoryRoom historyRoom = entry.Rooms[room];
                if (historyRoom.RoomType is < RunRoomType.Unassigned or > RunRoomType.Map)
                    throw new InvalidDataException($"history[{index}].room[{room}] 类型非法。");
                if (historyRoom.MonsterCount > RunLimits.HistoryMonsters)
                    throw new InvalidDataException($"history[{index}].room[{room}] 怪物数超出。");
                RequireRef(historyRoom.Model, $"history[{index}].room[{room}].model");
                for (int monster = 0; monster < historyRoom.MonsterCount; monster++)
                {
                    if (historyRoom.Monsters[monster] >= Registry.MonsterCount)
                        throw new InvalidDataException($"history[{index}].room[{room}].monster[{monster}] 未知。");
                }
            }
        }
    }

    private static void ValidateRoom(in RunRoom room, string what)
    {
        if (room.RoomType is < RunRoomType.Unassigned or > RunRoomType.Map)
            throw new InvalidDataException($"{what}.type={room.RoomType} 非法。");
        if (room.IsPreFinished > 1)
            throw new InvalidDataException($"{what}.IsPreFinished 非法。");
        RequireRef(room.Model, what + ".model");
        RequireRef(room.ParentEvent, what + ".parentEvent");
        switch (room.RoomType)
        {
            case RunRoomType.Monster or RunRoomType.Elite or RunRoomType.Boss:
                if (room.Model.Kind != RunRefKind.Encounter)
                    throw new InvalidDataException($"{what} 战斗房间的 model 不是遭遇。");
                break;
            case RunRoomType.Event:
                if (room.Model.Kind != RunRefKind.Event)
                    throw new InvalidDataException($"{what} 事件房间的 model 不是事件。");
                break;
            default:
                if (!room.Model.IsNone)
                    throw new InvalidDataException($"{what} 房间 {room.RoomType} 不应带 model。");
                break;
        }
    }

    private static void RequireRef(RunRef reference, string what)
    {
        if (!RunCatalog.IsKnown(reference))
            throw new InvalidDataException($"{what} 引用 {reference.Kind}:{reference.Id} 未知。");
    }

    private static void RequireEvent(ushort id, string what)
    {
        if (id >= RunCatalog.EventCount)
            throw new InvalidDataException($"{what} 事件 id={id} 未知。");
    }

    private static void RequireEncounter(ushort id, string what)
    {
        if (id >= RunCatalog.EncounterCount)
            throw new InvalidDataException($"{what} 遭遇 id={id} 未知。");
    }
}
