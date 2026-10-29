// Shared entry point for building a brand-new native run before the player makes the first
// choice. Hosts decide how the run is driven; this layer only owns the test-only setup contract.
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace Sts2Solver.TestHost;

internal static class RunTestHost
{
    /// <summary>
    /// Creates a new single-player run through the host and returns the state before the first
    /// choice. The run must have no visited map point and no map-point history yet.
    /// </summary>
    public static async Task<(RunState Run, Player Player)> StartNewRunAsync(
        ICombatHost host, CharacterModel character, int ascension, string seed)
    {
        RunState run = await host.StartRunAsync(character, ascension, seed);
        Player? player = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(run);
        if (player is null)
            throw new InvalidOperationException("新局找不到本地玩家。");
        if (run.VisitedMapCoords.Count != 0 || run.MapPointHistory.Count != 0)
            throw new InvalidOperationException("新局已经走过了地图点，不是第一个选择之前。");
        return (run, player);
    }

    /// <summary>
    /// Creates a new single-player run with an explicit unlock state and stops after the first
    /// act's map is generated. This is <c>RunState.CreateForNewRun</c> +
    /// <c>RunManager.SetUpNewSingleplayer</c> + <c>FinalizeStartingRelics</c> + <c>Launch</c> +
    /// <c>GenerateMap</c>: the state before any room is entered.
    /// <para>
    /// The real game enters the start map point inside <c>EnterAct(0)</c>; for a save with the
    /// Neow epoch that opens the ancient event, and for every save it selects a map point. Both are
    /// R2b-2 (map point choice and ancient events), so this entry stops before them and callers
    /// compare only the generated run.
    /// </para>
    /// </summary>
    public static async Task<(RunState Run, Player Player)> StartNewRunBeforeChoicesAsync(
        CharacterModel character, int ascension, string seed, UnlockState unlock)
    {
        if (RunManager.Instance.IsInProgress)
            throw new InvalidOperationException("已经有进行中的跑局。");
        RunState run = RunState.CreateForNewRun(
            [Player.CreateForNewRun(character, unlock, 1uL)],
            ActModel.GetDefaultList().Select(act => act.ToMutable()).ToList(),
            [],
            GameMode.Standard,
            ascension,
            seed);
        RunManager.Instance.SetUpNewSingleplayer(run, shouldSave: false);
        await RunManager.Instance.FinalizeStartingRelics();
        RunManager.Instance.Launch();
        await RunManager.Instance.GenerateMap();
        Player? player = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(run);
        if (player is null)
            throw new InvalidOperationException("新局找不到本地玩家。");
        if (run.VisitedMapCoords.Count != 0 || run.MapPointHistory.Count != 0
            || run.CurrentRoomCount != 0)
            throw new InvalidOperationException("新局已经进入房间，不是第一个选择之前。");
        return (run, player);
    }

    /// <summary>Normal native startup with explicit unlocks, including EnterAct and its hooks.</summary>
    public static async Task<(RunState Run, Player Player)> StartNewRunWithUnlocksAsync(
        CharacterModel character, int ascension, string seed, UnlockState unlock)
    {
        if (RunManager.Instance.IsInProgress)
            throw new InvalidOperationException("已经有进行中的跑局。");
        RunState run = RunState.CreateForNewRun(
            [Player.CreateForNewRun(character, unlock, 1uL)],
            ActModel.GetDefaultList().Select(act => act.ToMutable()).ToList(), [],
            GameMode.Standard, ascension, seed);
        RunManager.Instance.SetUpNewSingleplayer(run, shouldSave: false);
        await RunManager.Instance.FinalizeStartingRelics();
        RunManager.Instance.Launch();
        await RunManager.Instance.EnterAct(0, doTransition: false);
        return (run, MegaCrit.Sts2.Core.Context.LocalContext.GetMe(run)
            ?? throw new InvalidOperationException("新局找不到本地玩家。"));
    }

    /// <summary>Native <c>RunManager.SetActInternal</c>: switch act and regenerate its map.</summary>
    public static Task SetActAsync(int actIndex) => RunManager.Instance.SetActInternal(actIndex);

    /// <summary>
    /// Native legal map points at the current decision, mirroring
    /// <c>NMapScreen.RecalculateTravelability</c>: the starting point before any travel, the second
    /// boss after the boss, the boss after the last normal row, and otherwise
    /// <c>MapTravel.GetTravelablePointsFrom</c>. The UI is never read.
    /// </summary>
    public static IReadOnlyList<MapPoint> GetTravelablePoints(RunState run)
    {
        List<MapPoint> points = [];
        if (run.VisitedMapCoords.Count == 0)
        {
            points.Add(run.Map.StartingMapPoint);
            return points;
        }
        MapCoord last = run.VisitedMapCoords[^1];
        MapPoint current = run.Map.GetPoint(last)
            ?? throw new InvalidDataException($"visited coord {last} 不在当前地图。");
        if (run.Map.SecondBossMapPoint != null && last.Equals(run.Map.BossMapPoint.coord))
        {
            points.Add(run.Map.SecondBossMapPoint);
            return points;
        }
        if (last.row == run.Map.GetRowCount() - 1)
        {
            points.Add(run.Map.BossMapPoint);
            return points;
        }
        points.AddRange(MapTravel.GetTravelablePointsFrom(run, current));
        return points;
    }

    /// <summary>Native <c>RunManager.EnterMapCoord</c>.</summary>
    public static Task EnterMapCoordAsync(MapCoord coord) => RunManager.Instance.EnterMapCoord(coord);

    /// <summary>
    /// Restores a run snapshot into a fresh <c>RunManager</c> and regenerates the current act's map
    /// from the saved map, without entering the last map point's room. Used to replay each legal
    /// option from the same map decision.
    /// <para>
    /// <c>RunState.FromSerializable</c> keeps the save's inner history lists, so each restore gets
    /// fresh lists; otherwise appending a map point would mutate the snapshot and the next option
    /// would see the previous option's history. Saves are disabled because the snapshot's
    /// non-serialized fields (the shared grab bag's <c>refreshAllowed</c>) are not part of the
    /// native save format.
    /// </para>
    /// </summary>
    public static async Task<RunState> RestoreSnapshotAsync(SerializableRun save)
    {
        if (RunManager.Instance.IsInProgress)
            RunManager.Instance.CleanUp(graceful: false);
        // RunState.FromSerializable keeps the save's inner history list objects. Swap in fresh
        // copies for the restore and put the snapshot's own lists back afterwards, so appending
        // the entered map point cannot grow the snapshot shared by the next option.
        List<List<MapPointHistoryEntry>> history = save.MapPointHistory;
        save.MapPointHistory = [.. history.Select(static entries => entries.ToList())];
        RunState state;
        try
        {
            state = RunState.FromSerializable(save);
        }
        finally
        {
            save.MapPointHistory = history;
        }
        await RunManager.Instance.SetUpSavedSingleplayer(state, save);
        typeof(RunManager).GetProperty(nameof(RunManager.ShouldSave))!
            .SetValue(RunManager.Instance, false);
        RunManager.Instance.Launch();
        await RunManager.Instance.GenerateMap();
        return state;
    }

    /// <summary>
    /// Restores a reward-screen snapshot: the normal save restore followed by the saved
    /// <c>PreFinishedRoom</c>, which the run save carries separately from the room stack. The room
    /// is pushed without entering it, so no reward screen is generated and no state changes.
    /// </summary>
    public static async Task<RunState> RestoreRewardSnapshotAsync(SerializableRun save)
    {
        RunState state = await RestoreSnapshotAsync(save);
        if (save.PreFinishedRoom is { } preFinished)
        {
            CombatRoom room = CombatRoom.FromSerializable(preFinished, state);
            state.PushRoom(room);
        }
        return state;
    }
}
