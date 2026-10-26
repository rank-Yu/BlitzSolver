// Shared entry point for building a brand-new native run before the player makes the first
// choice. Hosts decide how the run is driven; this layer only owns the test-only setup contract.
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

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
}
