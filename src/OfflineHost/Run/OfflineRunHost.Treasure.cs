// Gameplay sequence follows sts2 v0.111.0 NTreasureRoom.OpenChest and
// NTreasureRoomRelicCollection.AnimateRelicAwards; only their presentation is replaced.
using Sts2Solver.Import;
using Sts2Solver.Simulation.Run;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Solver.OfflineHost.Run;

public sealed partial class OfflineRunHost
{
    private TreasureRoom? _treasureRoom;
    // 0 closed, 1 revealed, 2 resolved, 3 proceeded. No player choice is made while discovering a boundary.
    private int _treasureStage;
    internal bool TreasureProceedConfirmed(TreasureRoom room)
        => ReferenceEquals(_treasureRoom, room) && _treasureStage == 3;

    private RunExecutor.Pending TreasurePending(TreasureRoom room, RunDecisionToken token)
    {
        if (!ReferenceEquals(_treasureRoom, room))
        {
            _treasureRoom = room; _treasureStage = 0;
        }
        var snapshot = RunDecisionImporter.Treasure(room, _player, () => _treasureStage, token);
        return RunExecutor.Bind(snapshot, async (selected, action) =>
        {
            Execution scope = Active.Value ?? throw new InvalidDataException("host-treasure-no-execution");
            int index = RunDecisionProtocol.ResolveOptionIndex(snapshot.Request, action.OptionIds[0]);
            switch (snapshot.Request.Options[index].Kind)
            {
                case RunDecisionOptionKind.Open:
                    if (_treasureStage != 0) throw new InvalidDataException("host-treasure-open-duplicate");
                    _treasureStage = -1; // A failed open cannot be retried as a fresh chest.
                    await room.DoNormalRewards();
                    scope.Token.ThrowIfCancellationRequested();
                    using (TreasureRewards extra = TreasureRewards.Begin(_player, room))
                    {
                        await room.DoExtraRewardsIfNeeded();
                        await extra.Settle(scope.Token);
                    }
                    scope.Token.ThrowIfCancellationRequested(); ValidateRun();
                    if (!ReferenceEquals(_run.CurrentRoom, room)) throw new InvalidDataException("host-treasure-room-changed");
                    // Empty chests already ended voting in BeginRelicPicking; no later award event exists.
                    if (RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics is { Count: > 0 }) _treasureStage = 1;
                    else
                    {
                        RunManager.Instance.TreasureRoomRelicSynchronizer.CompleteWithNoRelics();
                        _treasureStage = 2;
                    }
                    break;
                case RunDecisionOptionKind.Relic:
                    await PickTreasure((RelicModel)selected[0], scope);
                    break;
                case RunDecisionOptionKind.Skip:
                    if (_treasureStage is not (0 or 1)) throw new InvalidDataException("host-treasure-skip-duplicate");
                    if (RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics is { Count: > 0 })
                        await CompleteTreasurePick(null, scope);
                    _treasureStage = 2;
                    break;
                case RunDecisionOptionKind.Leave:
                    if (_treasureStage != 2) throw new InvalidDataException("host-treasure-leave-before-resolved");
                    await RunManager.Instance.ProceedFromTerminalRewardsScreen();
                    _treasureStage = 3;
                    break;
                default: throw new InvalidDataException("host-treasure-option");
            }
        });
    }
    private async Task PickTreasure(RelicModel relic, Execution scope)
    {
        if (_treasureStage != 1) throw new InvalidDataException("host-treasure-pick-before-open");
        var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics
            ?? throw new InvalidDataException("host-treasure-relics-missing");
        int index = -1;
        for (int i = 0; i < relics.Count; i++) if (ReferenceEquals(relics[i], relic)) index = i;
        if (index < 0) throw new InvalidDataException("host-treasure-relic-instance");
        await CompleteTreasurePick(index, scope);
        _treasureStage = 2;
    }
    private async Task CompleteTreasurePick(int? index, Execution scope)
    {
        var synchronizer = RunManager.Instance.TreasureRoomRelicSynchronizer;
        List<RelicPickingResult>? awarded = null;
        bool open = true;
        void Awarded(List<RelicPickingResult> results)
        {
            if (!open || awarded != null) throw new InvalidDataException("host-treasure-award-duplicate");
            awarded = results.ToList(); // Preserve native results; obtain runs in this command's async context.
        }
        synchronizer.RelicsAwarded += Awarded;
        try
        {
            _treasureStage = -1;
            PickRelicAction action = NativeCallbacks.PickTreasure(index);
            await action.CompletionTask;
            if (action.Exception != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(action.Exception).Throw();
            if (action.State != GameActionState.Finished) throw new InvalidDataException("host-treasure-action-incomplete");
            await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
            scope.Token.ThrowIfCancellationRequested(); ValidateRun();
            if (!ReferenceEquals(_run.CurrentRoom, _treasureRoom)) throw new InvalidDataException("host-treasure-room-changed");
            if (!index.HasValue)
            {
                // Native singleplayer skip retains CurrentRelics until TreasureRoom.Exit.
                if (awarded != null || synchronizer.CurrentRelics == null)
                    throw new InvalidDataException("host-treasure-skip-result");
                return;
            }
            if (awarded == null || synchronizer.CurrentRelics != null)
                throw new InvalidDataException("host-treasure-award-missing");
            awarded.Sort((a, b) => a.type.CompareTo(b.type));
            foreach (RelicPickingResult result in awarded)
            {
                RelicModel mutable = result.relic.ToMutable();
                if (result.type != RelicPickingResultType.Skipped)
                {
                    if (!ReferenceEquals(result.player, _player)) throw new InvalidDataException("host-treasure-award-player");
                    await RelicCmd.Obtain(mutable, result.player);
                }
                foreach (var player in _run.Players)
                    if (!ReferenceEquals(player, result.player)) player.RelicGrabBag.MoveToFallback(result.relic);
            }
            await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        }
        finally { open = false; synchronizer.RelicsAwarded -= Awarded; }
    }
}
