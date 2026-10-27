using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Native <c>UnknownMapPointOdds</c> in Core. The four current odds live in
/// <see cref="RunState.Odds"/>; the base values they reset/grow from live in
/// <see cref="ContentTable.BaseOdds"/>. The roll order is the native dictionary insertion order
/// (Monster, Elite, Treasure, Shop), and the growth pass uses the base value, not the current one.
/// </summary>
internal static class RunUnknownPoint
{
    /// <summary>
    /// Native <c>RoomType Roll(blacklist, runState)</c>. The first-run pity branch returns without
    /// consuming the unknown-map-point RNG and without changing the odds.
    /// </summary>
    public static RunRoomType Roll(ref RunState state, ContentTable content, bool shopBlacklisted)
    {
        if (state.Unlock.NumberOfRuns == 0)
        {
            int unknownPoints = 0;
            for (int index = 0; index < state.History.Count; index++)
            {
                if (state.History.Entries[index].MapPointType == RunMapPointType.Unknown)
                    unknownPoints++;
            }
            if (unknownPoints < 2)
                return RunRoomType.Event;
            if (unknownPoints == 2)
                return RunRoomType.Monster;
        }

        RejectRoomTypeHooks(in state, content);
        Span<bool> candidates = stackalloc bool[(int)RunRoomType.Map + 1];
        candidates[(int)RunRoomType.Monster] = true;
        candidates[(int)RunRoomType.Elite] = true;
        candidates[(int)RunRoomType.Treasure] = true;
        // Native appends Event before Except(blacklist); the map blacklist only ever contains Shop.
        candidates[(int)RunRoomType.Shop] = !shopBlacklisted;
        candidates[(int)RunRoomType.Event] = true;

        RngState rng = state.Rng.Streams[(int)RunRngStream.UnknownMapPoint];
        float roll = rng.NextFloat(1f);
        float cumulative = 0f;
        RunRoomType roomType = RunRoomType.Event;
        ReadOnlySpan<RunRoomType> rollOrder =
        [
            RunRoomType.Monster, RunRoomType.Elite, RunRoomType.Treasure, RunRoomType.Shop,
        ];
        foreach (RunRoomType candidate in rollOrder)
        {
            float odds = OddsOf(in state, candidate);
            if (!candidates[(int)candidate] || odds < 0f)
                continue;
            cumulative += odds;
            if (roll <= cumulative)
            {
                roomType = candidate;
                break;
            }
        }

        foreach (RunRoomType candidate in rollOrder)
        {
            float baseOdds = BaseOddsOf(content, candidate);
            if (roomType == candidate)
                SetOdds(ref state, candidate, baseOdds);
            else if (candidates[(int)candidate])
                SetOdds(ref state, candidate, OddsOf(in state, candidate) + baseOdds);
        }
        state.Rng.Streams[(int)RunRngStream.UnknownMapPoint] = rng;
        return roomType;
    }

    /// <summary>Native <c>TryGetRoomTypeForTutorial</c> in the non-test, single-player path.</summary>
    public static bool TryGetRoomTypeForTutorial(in RunState state, RunMapPointType pointType,
        out RunRoomType roomType)
    {
        roomType = RunRoomType.Unassigned;
        // Native returns false when TestMode is on; Core runs the game path only.
        if (pointType != RunMapPointType.Unassigned)
            return false;
        if (state.Unlock.NumberOfRuns > 0)
            return false;
        for (int index = 0; index < state.History.Count; index++)
        {
            if (state.History.Entries[index].MapPointType == RunMapPointType.Unassigned)
                return false;
        }
        roomType = RunRoomType.Event;
        return true;
    }

    /// <summary>Native <c>BuildRoomTypeBlacklist</c> for the current point's children.</summary>
    public static bool IsShopBlacklisted(in RunState state)
    {
        if (state.History.Count != 0)
        {
            ref readonly RunHistoryEntry previous = ref state.History.Entries[state.History.Count - 1];
            for (int index = 0; index < previous.RoomCount; index++)
            {
                if (previous.Rooms[index].RoomType == RunRoomType.Shop)
                    return true;
            }
        }
        if (!RunTravel.TryCurrentPoint(in state, out RunMapPoint current) || current.ChildCount == 0)
            return false;
        for (int index = 0; index < current.ChildCount; index++)
        {
            if (!RunTravel.TryFindPoint(in state, current.Children[index], out RunMapPoint child)
                || child.Type != RunMapPointType.Shop)
                return false;
        }
        return true;
    }

    private static void RejectRoomTypeHooks(in RunState state, ContentTable content)
    {
        for (int index = 0; index < state.Relics.Count; index++)
        {
            ushort id = state.Relics.Items[index].Id;
            if (Array.IndexOf(content.UnknownPointHookRelics, id) >= 0)
                throw new ContentUnavailableException(RunStepReason.UnknownPointHook,
                    $"遗物 {Registry.Relic(id).NativeId} 会改写未知点房间类型。");
        }
        for (int index = 0; index < state.Deck.Count; index++)
        {
            ushort id = state.Deck.Cards[index].Id;
            if (Array.IndexOf(content.UnknownPointHookCards, id) >= 0)
                throw new ContentUnavailableException(RunStepReason.UnknownPointHook,
                    $"卡牌 {Registry.Card(id).NativeId} 会改写未知点房间类型。");
        }
    }

    private static float OddsOf(in RunState state, RunRoomType roomType) => roomType switch
    {
        RunRoomType.Monster => state.Odds.Monster,
        RunRoomType.Elite => state.Odds.Elite,
        RunRoomType.Treasure => state.Odds.Treasure,
        RunRoomType.Shop => state.Odds.Shop,
        _ => throw new ArgumentOutOfRangeException(nameof(roomType), roomType, null),
    };

    private static void SetOdds(ref RunState state, RunRoomType roomType, float value)
    {
        switch (roomType)
        {
            case RunRoomType.Monster: state.Odds.Monster = value; break;
            case RunRoomType.Elite: state.Odds.Elite = value; break;
            case RunRoomType.Treasure: state.Odds.Treasure = value; break;
            case RunRoomType.Shop: state.Odds.Shop = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(roomType), roomType, null);
        }
    }

    private static float BaseOddsOf(ContentTable content, RunRoomType roomType) => roomType switch
    {
        RunRoomType.Monster => content.BaseOdds.Monster,
        RunRoomType.Elite => content.BaseOdds.Elite,
        RunRoomType.Treasure => content.BaseOdds.Treasure,
        RunRoomType.Shop => content.BaseOdds.Shop,
        _ => throw new ArgumentOutOfRangeException(nameof(roomType), roomType, null),
    };
}
