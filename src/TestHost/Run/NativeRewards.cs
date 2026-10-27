// Native post-combat reward driver. It uses the same public entries the reward screen uses:
// RewardsCmd.GenerateForRoomEnd, RewardsSetSynchronizer.BeginRewardsSet/SelectLocalReward/
// SkipLocalRewardsSet, the test ICardSelector hook, and PotionCmd.Discard. The UI is never read.
using System.Linq;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace BlitzSolver.TestHost;

public enum NativeRewardStage
{
    List,
    Card,
    PotionDiscard,
}

public enum NativeRewardChoiceKind
{
    Reward,
    Card,
    Skip,
    Potion,
    Reroll,
}

/// <summary>One legal choice the native reward UI offers at the current stage.</summary>
public sealed class NativeRewardChoice
{
    public required NativeRewardChoiceKind Kind { get; init; }

    public required int Index { get; init; }

    public Reward? Reward { get; init; }

    public CardModel? Card { get; init; }

    public PotionModel? Potion { get; init; }
}

/// <summary>One live native reward screen, advanced through the game's public reward entries.</summary>
public sealed class NativeRewardSession
{
    private readonly RewardsSet _set;
    private readonly Player _player;
    private readonly bool _useSynchronizer;
    private readonly HashSet<Reward> _skipped = [];
    private NativeRewardStage _stage;
    private Reward? _cardReward;
    private Reward? _potionReward;
    private Task _completion;

    private NativeRewardSession(RewardsSet set, Player player, bool useSynchronizer)
    {
        _set = set;
        _player = player;
        _useSynchronizer = useSynchronizer;
        _stage = NativeRewardStage.List;
        LocalContext.NetId = player.NetId;
        _completion = useSynchronizer
            ? RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(set)
            : Task.CompletedTask;
    }

    public static async Task<NativeRewardSession> CreateAsync(Player player, AbstractRoom room,
        bool useSynchronizer = true)
    {
        RewardsSet set = await RewardsCmd.GenerateForRoomEnd(player, room);
        return new NativeRewardSession(set, player, useSynchronizer);
    }

    /// <summary>
    /// Wraps an already generated set (used when the caller compares generation and selection from
    /// one snapshot). The set must be the local player's.
    /// </summary>
    public static NativeRewardSession Attach(RewardsSet set, Player player,
        bool useSynchronizer = true)
        => new(set, player, useSynchronizer);

    public RewardsSet Set => _set;

    public IReadOnlyList<Reward> Rewards => _set.Rewards;

    /// <summary>Rewards still shown by the native UI (taken and skipped ones are removed).</summary>
    public IReadOnlyList<Reward> RemainingRewards
        => Left
            ? []
            : [.. _set.Rewards.Where(reward => !reward.SuccessfullySelected
                && !_skipped.Contains(reward))];

    public IReadOnlyList<NativeRewardChoice> Choices()
    {
        List<NativeRewardChoice> choices = [];
        if (_stage == NativeRewardStage.List)
        {
            int index = 0;
            foreach (Reward reward in _set.Rewards)
            {
                if (reward.SuccessfullySelected || _skipped.Contains(reward))
                    continue;
                choices.Add(new NativeRewardChoice
                {
                    Kind = NativeRewardChoiceKind.Reward,
                    Index = index++,
                    Reward = reward,
                });
            }
            choices.Add(new NativeRewardChoice
            {
                Kind = NativeRewardChoiceKind.Skip,
                Index = index,
            });
            return choices;
        }
        if (_stage == NativeRewardStage.Card)
        {
            CardReward reward = (CardReward)_cardReward!;
            int index = 0;
            foreach (CardModel card in reward.Cards)
            {
                choices.Add(new NativeRewardChoice
                {
                    Kind = NativeRewardChoiceKind.Card,
                    Index = index++,
                    Reward = reward,
                    Card = card,
                });
            }
            choices.Add(new NativeRewardChoice
            {
                Kind = NativeRewardChoiceKind.Skip,
                Index = index++,
                Reward = reward,
            });
            if (reward.CanReroll)
            {
                choices.Add(new NativeRewardChoice
                {
                    Kind = NativeRewardChoiceKind.Reroll,
                    Index = index,
                    Reward = reward,
                });
            }
            return choices;
        }
        int slot = 0;
        for (int index = 0; index < _player.MaxPotionCount; index++)
        {
            PotionModel? potion = _player.GetPotionAtSlotIndex(index);
            if (potion == null)
                continue;
            choices.Add(new NativeRewardChoice
            {
                Kind = NativeRewardChoiceKind.Potion,
                Index = slot++,
                Potion = potion,
                Reward = _potionReward,
            });
        }
        choices.Add(new NativeRewardChoice
        {
            Kind = NativeRewardChoiceKind.Skip,
            Index = slot,
            Reward = _potionReward,
        });
        return choices;
    }

    public async Task TakeRewardAsync(NativeRewardChoice choice)
    {
        if (choice.Kind != NativeRewardChoiceKind.Reward || choice.Reward is null)
            throw new InvalidOperationException("Native reward choice is not a reward.");
        if (choice.Reward is CardReward)
        {
            _cardReward = choice.Reward;
            _stage = NativeRewardStage.Card;
            return;
        }
        bool selected = _useSynchronizer
            ? await RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(choice.Reward)
            : await choice.Reward.SelectUnsynchronized();
        if (!selected && choice.Reward is PotionReward)
        {
            _potionReward = choice.Reward;
            _stage = NativeRewardStage.PotionDiscard;
            return;
        }
        _stage = NativeRewardStage.List;
    }

    public async Task ChooseCardAsync(NativeRewardChoice choice)
    {
        if (_stage != NativeRewardStage.Card || _cardReward is null)
            throw new InvalidOperationException("Native reward session is not on a card reward.");
        CardReward reward = (CardReward)_cardReward;
        if (choice.Kind == NativeRewardChoiceKind.Reroll)
        {
            reward.Reroll();
            return;
        }
        if (choice.Kind == NativeRewardChoiceKind.Skip)
        {
            using (CardSelectCmd.PushSelector(new RewardSelector(null), localOnly: false))
                await SelectCardReward(reward);
            _skipped.Add(reward);
            _cardReward = null;
            _stage = NativeRewardStage.List;
            return;
        }
        if (choice.Kind != NativeRewardChoiceKind.Card || choice.Card is null)
            throw new InvalidOperationException("Native reward choice is not a card.");
        using (CardSelectCmd.PushSelector(new RewardSelector(choice.Card), localOnly: false))
            await SelectCardReward(reward);
        _cardReward = null;
        _stage = NativeRewardStage.List;
    }

    public async Task ChoosePotionAsync(NativeRewardChoice choice)
    {
        if (_stage != NativeRewardStage.PotionDiscard || _potionReward is null)
            throw new InvalidOperationException("Native reward session is not on a potion discard.");
        if (choice.Kind == NativeRewardChoiceKind.Skip)
        {
            _skipped.Add(_potionReward);
            _potionReward = null;
            _stage = NativeRewardStage.List;
            return;
        }
        if (choice.Kind != NativeRewardChoiceKind.Potion || choice.Potion is null)
            throw new InvalidOperationException("Native reward choice is not a potion.");
        await PotionCmd.Discard(choice.Potion);
        if (_useSynchronizer)
            await RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(_potionReward);
        else
            await _potionReward.SelectUnsynchronized();
        _potionReward = null;
        _stage = NativeRewardStage.List;
    }

    public void SkipCurrent()
    {
        if (_stage == NativeRewardStage.Card && _cardReward is not null)
        {
            _skipped.Add(_cardReward);
            _cardReward = null;
        }
        else if (_stage == NativeRewardStage.PotionDiscard && _potionReward is not null)
        {
            _skipped.Add(_potionReward);
            _potionReward = null;
        }
        _stage = NativeRewardStage.List;
    }

    public void Leave()
    {
        if (_useSynchronizer && !_completion.IsCompleted)
        {
            RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
        }
        else if (!_useSynchronizer)
        {
            foreach (Reward reward in _set.Rewards)
            {
                if (!reward.SuccessfullySelected)
                    reward.OnSkipped();
            }
        }
        _stage = NativeRewardStage.List;
        Left = true;
        _skipped.Clear();
    }

    private Task<bool> SelectCardReward(CardReward reward)
        => _useSynchronizer
            ? RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward)
            : reward.SelectUnsynchronized();

    public bool IsCompleted => _completion.IsCompleted;

    public NativeRewardStage Stage => _stage;

    public Reward? OpenCardReward => _cardReward;

    public Reward? OpenPotionReward => _potionReward;

    public bool Left { get; private set; }

    private sealed class RewardSelector(CardModel? card) : ICardSelector
    {
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<CardRewardAlternative> alternatives)
        {
            if (card is null)
                return default;
            CardModel? match = options.Select(static option => option.Card)
                .FirstOrDefault(option => option.Id == card.Id
                    && option.CurrentUpgradeLevel == card.CurrentUpgradeLevel);
            if (match is null)
                throw new InvalidOperationException($"原生卡牌奖励没有 {card.Id.Entry}。");
            return new CardRewardSelection { card = match };
        }

        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,
            int minSelect, int maxSelect)
            => throw new InvalidOperationException("奖励选择器收到了非奖励的选牌请求。");
    }
}
