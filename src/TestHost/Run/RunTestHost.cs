// Shared entry point for building a brand-new native run before the player makes the first
// choice. Hosts decide how the run is driven; this layer only owns the test-only setup contract.
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace BlitzSolver.TestHost;

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

    /// <summary>Native <c>RunManager.SetActInternal</c>: switch act and regenerate its map.</summary>
    public static Task SetActAsync(int actIndex) => RunManager.Instance.SetActInternal(actIndex);
}
