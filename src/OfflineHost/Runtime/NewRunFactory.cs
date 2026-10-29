// Adapted from src/TestHost/Run/RunTestHost.cs at 6a5f645 (Sts2Solver, MIT).
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace Sts2Solver.OfflineHost.Runtime;

internal static class NewRunFactory
{
    internal static async Task<(RunState Run, Player Player)> Create(FirstCombatOptions options,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (RunManager.Instance.IsInProgress) throw new InvalidDataException("native-run-already-active");
        CharacterModel character = ModelDb.AllCharacters.SingleOrDefault(c => c.GetType().Name == options.Character)
            ?? throw new InvalidDataException("unknown-character:" + options.Character);
        UnlockState unlock = options.UnlockProfile switch
        {
            "neow-epoch-v1" => new UnlockState([new NeowEpoch().Id], [], 1),
            _ => throw new InvalidDataException("unknown-unlock-profile:" + options.UnlockProfile),
        };
        RunState run = RunState.CreateForNewRun([Player.CreateForNewRun(character, unlock, 1uL)],
            ActModel.GetDefaultList().Select(act => act.ToMutable()).ToList(), [],
            GameMode.Standard, options.Ascension, options.Seed);
        RunManager.Instance.SetUpNewSingleplayer(run, shouldSave: false);
        await RunManager.Instance.FinalizeStartingRelics();
        token.ThrowIfCancellationRequested();
        RunManager.Instance.Launch();
        await RunManager.Instance.EnterAct(0, doTransition: false);
        token.ThrowIfCancellationRequested();
        return (run, MegaCrit.Sts2.Core.Context.LocalContext.GetMe(run)
            ?? throw new InvalidDataException("native-local-player-missing"));
    }
}
