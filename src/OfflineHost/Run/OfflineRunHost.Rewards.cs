// Native reward execution follows sts2 v0.111.0 RewardsSetSynchronizer/CardReward and NPotionPopup.
// Snapshot binding pattern adapted from tests/Differential/Run/NativeDecisionTestSession.RewardsShop.cs
// at baseline 16128953 (BlitzSolver, MIT).
using BlitzSolver.Ai.Run;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace BlitzSolver.OfflineHost.Run;

public sealed partial class OfflineRunHost
{
    private RewardPresentation? _rewards;
    internal async ValueTask OfferCombatRewardsAsync(CombatRoom room, CancellationToken token)
    {
        lock (_gate)
        {
            CheckAvailable(executing: true); ValidateRun();
            if (!_inCombatCallback || _pending != null || _operation == null || _operation.IsCancellationRequested)
                throw new InvalidDataException("host-reward-outside-combat-completion");
            if (_rewards != null) throw new InvalidDataException("host-reward-presentation-exists");
            _rewards = new(_player, room, _lifetime.Token);
        }
        await _rewards.Start().WaitAsync(token);
    }
    internal bool RewardsSetCompleted => _rewards is { SetCompleted: true };
    internal async ValueTask SettleBossRewardsAsync(CancellationToken token)
    {
        var presentation = _rewards ?? throw new InvalidDataException("host-no-rewards");
        await presentation.SettleCompletedBoss(token);
    }
    internal bool RewardsProceedConfirmed => _rewards is { Proceeded: true };
    internal bool RewardsTravelEnabled => _rewards is { TravelEnabled: true };
    internal async ValueTask ConfirmRewardsRoomExitAsync(CancellationToken token)
    {
        RewardPresentation presentation;
        lock (_gate)
        { CheckAvailable(executing: true); token.ThrowIfCancellationRequested(); ValidateRun();
            presentation = _rewards ?? throw new InvalidDataException("host-no-rewards"); }
        await presentation.SettleRoomExit(token);
        await presentation.DisposeAsync();
        lock (_gate)
        { CheckAvailable(executing: true); ValidateRun();
            if (!ReferenceEquals(_rewards, presentation)) throw new InvalidDataException("host-reward-lease-changed");
            _rewards = null;
        }
    }
    private RunExecutor.Pending RewardPending(RunDecisionToken token)
    {
        RewardPresentation presentation = _rewards ?? throw new InvalidDataException("host-no-rewards");
        RewardsSet set = presentation.Set ?? throw new InvalidDataException("host-no-reward-set");
        return RunExecutor.Bind(RunDecisionImporter.Rewards(set,
            () => set.Rewards.Where(r => !r.SuccessfullySelected).ToArray(),
            () => presentation.Active, () => presentation.Active && !presentation.IsBoss, token), async (selected, _) =>
        {
            if (ReferenceEquals(selected[0], set)) { await presentation.Leave(); return; }
            Reward reward = (Reward)selected[0];
            bool success;
            if (reward is CardReward card)
            {
                using IDisposable screen = RewardCardScreen.Use(new(this, card));
                success = await RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
            }
            else success = await RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
            if (!success && reward is PotionReward potion)
            {
                object selectedPotion = await DecideRewardChild(
                    t => RunDecisionImporter.PotionDiscard(potion, () => presentation.Active, t));
                if (selectedPotion is PotionModel discard)
                {
                    int slot = -1;
                    for (int i = 0; i < _player.PotionSlots.Count; i++)
                        if (ReferenceEquals(_player.PotionSlots[i], discard)) slot = i;
                    if (slot < 0) throw new InvalidDataException("host-potion-instance");
                    Execution scope = Active.Value ?? throw new InvalidDataException("host-no-execution");
                    DiscardPotionGameAction action = new(_player, (uint)slot, false);
                    RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
                    await action.CompletionTask.WaitAsync(scope.Token);
                    if (action.Exception != null) throw new InvalidDataException("host-potion-discard-failed", action.Exception);
                    if (action.State != GameActionState.Finished) throw new InvalidDataException("host-potion-discard-incomplete");
                    await RunManager.Instance.ActionExecutor.FinishedExecutingActions().WaitAsync(scope.Token);
                }
            }
        });
    }
    internal async Task<int?> SelectRewardCardAsync(CardReward reward, IReadOnlyList<CardCreationResult> cards,
        IReadOnlyList<CardRewardAlternative> alternatives)
    {
        object selected = await DecideRewardChild(t => RunDecisionImporter.CardReward(reward, cards, alternatives,
            () => _rewards is { Active: true }, t));
        for (int i = 0; i < cards.Count; i++) if (ReferenceEquals(cards[i].Card, selected)) return i;
        for (int i = 0; i < alternatives.Count; i++) if (ReferenceEquals(alternatives[i], selected)) return cards.Count + i;
        throw new InvalidDataException("host-card-reward-instance");
    }
    private async Task<object> DecideRewardChild(Func<RunDecisionToken, NativeDecisionSnapshot<object>> import)
    {
        Execution scope = Active.Value ?? throw new InvalidDataException("host-unrelated-reward-child");
        if (!ReferenceEquals(scope.Host, this) || !scope.Open) throw new InvalidDataException("host-unrelated-reward-child");
        RunExecutor.Pending pending;
        NativeDecisionSnapshot<object> snapshot;
        RunView view;
        lock (_gate)
        {
            CheckAvailable(executing: true); scope.Token.ThrowIfCancellationRequested(); ValidateRun();
            if (_pending != null) throw new InvalidDataException("host-child-busy");
            snapshot = import(NextToken());
            pending = RunExecutor.Bind(snapshot, (_, _) => Task.CompletedTask);
            view = Fresh(pending.Request); _pending = pending;
        }
        try
        {
            RunPilotAnswer answer = await scope.Decide(view, scope.Token).AsTask().WaitAsync(scope.Token);
            lock (_gate)
            {
                CheckAvailable(executing: true); scope.Token.ThrowIfCancellationRequested(); ValidateRun();
                if (!scope.Open || !ReferenceEquals(_pending, pending)) throw new InvalidDataException("host-child-expired");
                Validate(pending, answer); _pending = null;
                return snapshot.Resolve(answer.Action).Single();
            }
        }
        finally { lock (_gate) { if (ReferenceEquals(_pending, pending)) _pending = null; } }
    }
}
