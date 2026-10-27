using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>The run state at the native "room built, not entered" boundary plus the created room.</summary>
internal sealed class RunMapEntryResult
{
    public required RunState Run { get; init; }

    public required RunMapPointType PointType { get; init; }

    public required RunRoomType RoomType { get; init; }

    public RunRef Model { get; init; }

    public ushort EncounterId { get; init; }

    public ushort EventId { get; init; }

    public CombatSetupResult? Combat { get; init; }
}

/// <summary>
/// Native <c>RunManager.EnterMapPointInternal</c> up to the point where content generation starts.
/// The order is fixed: update the history stats (not stored in <see cref="RunState"/>), set
/// <c>ActFloor</c>, exit the rooms, add the visited coordinate, roll the room type, create the
/// room, append the map-point history, then either build the combat (R2a-2 boundary) or stop just
/// before <c>EnterRoomInternal</c> (non-combat boundary, R2b-2).
/// </summary>
internal static class RunMapEntry
{
    public static RunMapEntryResult Enter(in RunState run, ContentTable table, RunCoord coord,
        int? actFloor = null)
    {
        if (!RunTravel.TryFindPoint(in run, coord, out RunMapPoint point))
            throw new ContentUnavailableException(ContentReason.UnsupportedRoom,
                $"地图没有坐标 {coord}。");
        for (int index = 0; index < run.Rooms.Count; index++)
        {
            RunRoom room = run.Rooms.Items[index];
            if (room.RoomType != RunRoomType.Map && room.IsPreFinished == 0)
                throw new ContentUnavailableException(RunStepReason.RoomStackActive,
                    $"进入地图点时房间栈含未完成的 {room.RoomType}。");
        }

        RunState next = run;
        next.Rooms = default;
        next.Decision = RunDecision.None;
        next.DecisionOptionCount = 0;
        next.ActFloor = actFloor ?? coord.Row + 1;
        next.AddVisitedCoord(coord);

        RunRoomType roomType = RollRoomType(ref next, table, point.Type);
        RunRef model = RunRef.None;
        ushort encounterId = 0;
        ushort eventId = 0;
        switch (roomType)
        {
            case RunRoomType.Monster:
            case RunRoomType.Elite:
            case RunRoomType.Boss:
                encounterId = PullEncounter(ref next, roomType);
                model = new RunRef(RunRefKind.Encounter, encounterId);
                break;
            case RunRoomType.Event:
                eventId = PullEvent(ref next, table, point.Type);
                model = new RunRef(RunRefKind.Event, eventId);
                break;
            case RunRoomType.Shop:
            case RunRoomType.Treasure:
            case RunRoomType.RestSite:
                break;
            default:
                throw new ContentUnavailableException(ContentReason.UnsupportedRoom,
                    $"地图点 {point.Type} 生成了房间 {roomType}。");
        }

        RunHistoryEntry history = new()
        {
            ActIndex = checked((byte)next.ActIndex),
            MapPointType = point.Type,
            RoomCount = 1,
        };
        history.Rooms[0] = new RunHistoryRoom
        {
            RoomType = roomType,
            Model = model,
        };
        next.AddHistory(history);

        if (roomType is RunRoomType.Monster or RunRoomType.Elite or RunRoomType.Boss)
        {
            next.AddRoom(new RunRoom
            {
                RoomType = roomType,
                Model = model,
                ParentEvent = RunRef.None,
            });
            // Native marks the room visited after EnterRoom returns; the counter is only read when
            // the next encounter/event is pulled, so setting it before the battle result is built
            // keeps the returned CombatSetupResult.Run complete.
            MarkVisited(ref next.Acts.Items[next.ActIndex], roomType);
            CombatSetupResult combat = RunCombatSetup.BuildCombat(ref next, table, roomType,
                encounterId);
            next.Canonicalize();
            return new RunMapEntryResult
            {
                Run = next,
                PointType = point.Type,
                RoomType = roomType,
                Model = model,
                EncounterId = encounterId,
                Combat = combat,
            };
        }

        next.Canonicalize();
        return new RunMapEntryResult
        {
            Run = next,
            PointType = point.Type,
            RoomType = roomType,
            Model = model,
            EventId = eventId,
        };
    }

    private static RunRoomType RollRoomType(ref RunState state, ContentTable table,
        RunMapPointType pointType)
    {
        if (RunUnknownPoint.TryGetRoomTypeForTutorial(in state, pointType, out RunRoomType tutorial))
            return tutorial;
        return pointType switch
        {
            RunMapPointType.Unknown => RunUnknownPoint.Roll(ref state, table,
                RunUnknownPoint.IsShopBlacklisted(in state)),
            RunMapPointType.Shop => RunRoomType.Shop,
            RunMapPointType.Treasure => RunRoomType.Treasure,
            RunMapPointType.RestSite => RunRoomType.RestSite,
            RunMapPointType.Monster => RunRoomType.Monster,
            RunMapPointType.Elite => RunRoomType.Elite,
            RunMapPointType.Boss => RunRoomType.Boss,
            RunMapPointType.Ancient => RunRoomType.Event,
            _ => throw new ContentUnavailableException(ContentReason.UnsupportedRoom,
                $"地图点 {pointType} 没有房间类型。"),
        };
    }

    private static ushort PullEncounter(ref RunState state, RunRoomType roomType)
    {
        ref RunAct act = ref state.Acts.Items[state.ActIndex];
        return roomType switch
        {
            RunRoomType.Monster => act.NormalCount == 0
                ? throw new ContentUnavailableException(ContentReason.UnsupportedEncounter,
                    "本幕没有普通遭遇序列。")
                : act.Normal[act.NormalVisited % act.NormalCount],
            RunRoomType.Elite => act.EliteCount == 0
                ? throw new ContentUnavailableException(ContentReason.UnsupportedEncounter,
                    "本幕没有精英遭遇序列。")
                : act.Elite[act.EliteVisited % act.EliteCount],
            RunRoomType.Boss => act.BossVisited != 0 && act.HasSecondBoss != 0
                ? act.SecondBossId
                : act.BossId,
            _ => throw new ContentUnavailableException(ContentReason.UnsupportedRoom,
                $"不是战斗房间：{roomType}。"),
        };
    }

    /// <summary>
    /// Native <c>ActModel.PullNextEvent</c> / <c>PullAncient</c> plus the event RNG that
    /// <c>EventModel.BeginEvent</c> creates on entry. R2b-2 stops before the event page, so the
    /// RNG is created here at its initial state: <c>seed + slot(0) + hash(event id)</c>.
    /// </summary>
    private static ushort PullEvent(ref RunState state, ContentTable table, RunMapPointType pointType)
    {
        ref RunAct act = ref state.Acts.Items[state.ActIndex];
        ushort eventId;
        if (pointType == RunMapPointType.Ancient)
        {
            if (act.HasAncient == 0)
                throw new ContentUnavailableException(ContentReason.UnsupportedEvent,
                    "先古点没有先古事件。");
            eventId = act.AncientId;
        }
        else
        {
            eventId = PullNextEvent(ref state, table);
        }

        RejectNextEventHooks(in state, table);
        state.AddEventRng(new RunEventRng
        {
            EventId = eventId,
            Active = 1,
            State = RunRng.Event(state.Rng.Seed, 0, eventId),
        });
        return eventId;
    }

    /// <summary>
    /// Native <c>RoomSet.EnsureNextEventIsValid</c> followed by <c>NextEvent</c> and
    /// <c>RunState.AddVisitedEvent</c>. A visited event advances the cursor without a draw; an
    /// event whose <c>IsAllowed</c> is state-dependent is rejected until R2e owns that rule.
    /// </summary>
    private static ushort PullNextEvent(ref RunState state, ContentTable table)
    {
        ref RunAct act = ref state.Acts.Items[state.ActIndex];
        if (act.EventCount == 0)
            throw new ContentUnavailableException(ContentReason.UnsupportedEvent,
                "本幕没有事件序列。");
        int visited = act.EventsVisited;
        for (int attempt = 0; attempt < act.EventCount; attempt++)
        {
            ushort candidate = act.Events[visited % act.EventCount];
            if (!state.HasVisitedEvent(candidate))
            {
                RejectEventAllowedCheck(table, candidate);
                act.EventsVisited = visited;
                state.MarkVisitedEvent(candidate);
                return candidate;
            }
            visited++;
        }
        // Native logs and allows repetition after every unique event was exhausted.
        act.EventsVisited = visited;
        ushort repeated = act.Events[visited % act.EventCount];
        RejectEventAllowedCheck(table, repeated);
        state.MarkVisitedEvent(repeated);
        return repeated;
    }

    private static void RejectEventAllowedCheck(ContentTable table, ushort eventId)
    {
        if (table.EventAllowedOverride[eventId])
            throw new ContentUnavailableException(RunStepReason.EventAllowedCheck,
                $"事件 {RunCatalog.EventName(eventId)} 的 IsAllowed 依赖跑局状态。");
    }

    private static void RejectNextEventHooks(in RunState state, ContentTable table)
    {
        for (int index = 0; index < state.Deck.Count; index++)
        {
            ushort id = state.Deck.Cards[index].Id;
            if (Array.IndexOf(table.NextEventHookCards, id) >= 0)
                throw new ContentUnavailableException(RunStepReason.EventHook,
                    $"卡牌 {Registry.Card(id).NativeId} 会改写下一个事件。");
        }
    }

    private static void MarkVisited(ref RunAct act, RunRoomType roomType)
    {
        switch (roomType)
        {
            case RunRoomType.Monster: act.NormalVisited++; break;
            case RunRoomType.Elite: act.EliteVisited++; break;
            case RunRoomType.Boss: act.BossVisited++; break;
            default: throw new ContentUnavailableException(ContentReason.UnsupportedRoom,
                $"不是战斗房间：{roomType}。");
        }
    }
}
