// TEST ONLY. Native selectors remain native: CardReward's test callback is synchronous;
// tests answer inside that callback, and its native while loop calls us again after reroll.
using BlitzSolver.Import;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace Differential;

internal sealed partial class NativeDecisionTestSession
{
    internal CardReward? SelectingReward { get; private set; }
    internal bool InShopRemoval { get; private set; }

    public void Rewards(RewardsSet set, Func<bool> active, Func<bool> canLeave, Action close)
        => Begin(token => RunDecisionImporter.Rewards(set,
            () => set.Rewards.Where(r => !r.SuccessfullySelected).ToArray(), active, canLeave, token), async (selected, _) =>
        {
            if (ReferenceEquals(selected[0], set))
            {
                if (!RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(set))
                    RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
                close(); return;
            }
            Reward reward = (Reward)selected[0];
            SelectingReward = reward as CardReward;
            try { await RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward); }
            finally { SelectingReward = null; }
        });

    public CardRewardSelection RewardCards(CardReward reward, IReadOnlyList<CardCreationResult> cards,
        IReadOnlyList<CardRewardAlternative> alternatives, Action<RunDecisionRequest> answer)
    {
        bool active = true, completed = false;
        CardRewardSelection result = default;
        Begin(token => RunDecisionImporter.CardReward(reward, cards, alternatives, () => active, token), (selected, _) =>
        {
            result = selected[0] switch
            {
                CardModel card => new CardRewardSelection { card = card },
                CardRewardAlternative alternative => new CardRewardSelection { alternative = alternative },
                _ => throw new InvalidDataException("host-reward-binding"),
            };
            completed = true; return Task.CompletedTask;
        }, nested: true);
        RunDecisionToken callbackToken = Request.Token;
        try
        {
            answer(Request);
            if (!completed) throw new InvalidDataException("test-card-reward: synchronous native callback was not answered");
            return result;
        }
        finally
        {
            active = false;
            // A synchronous test callback cannot leave an unanswered child alive after returning
            // or throwing. Only clear this callback's token, never an unrelated replacement.
            if (!completed && _current?.Request.Token == callbackToken) _current = null;
        }
    }

    public void Potions(PotionReward reward, Func<bool> active, Action back)
        => Begin(token => RunDecisionImporter.PotionDiscard(reward, active, token), async (selected, _) =>
        {
            if (selected[0] is PotionModel potion) await PotionCmd.Discard(potion);
            back(); // No automatic retry or reward removal: native UI requires another reward click.
        });

    public void Shop(MerchantRoom room, Player player, Func<bool> active, Func<bool> canLeave, Action close)
        => Begin(token => RunDecisionImporter.Shop(room, player, active, canLeave, token), async (selected, _) =>
        {
            if (selected[0] is MerchantInventory) { close(); return; }
            if (selected[0] is MerchantCardRemovalEntry removal)
            {
                InShopRemoval = true;
                try
                {
                    if (await removal.OnTryPurchaseWrapper(room.GetLocalInventory(), cancelable: true))
                        removal.SetUsed(); // NMerchantCardRemoval.OnCardRemovalUsed's UI-only model responsibility.
                }
                finally { InShopRemoval = false; }
            }
            else await ((MerchantEntry)selected[0]).OnTryPurchaseWrapper(room.GetLocalInventory());
        });
}

internal sealed class PendingRewardCardSelector(NativeDecisionTestSession session,
    Action<RunDecisionRequest> answer) : ICardSelector
{
    public int Calls { get; private set; }
    public IReadOnlyList<CardCreationResult> Cards { get; private set; } = [];
    public IReadOnlyList<CardRewardAlternative> Alternatives { get; private set; } = [];
    public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
        IReadOnlyList<CardRewardAlternative> alternatives)
    {
        Calls++; Cards = options; Alternatives = alternatives;
        return session.RewardCards(session.SelectingReward ?? throw new InvalidDataException("test-no-selecting-reward"),
            options, alternatives, answer);
    }
    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        => throw new NotSupportedException("test-reward-selector: deck selection not expected");
}
