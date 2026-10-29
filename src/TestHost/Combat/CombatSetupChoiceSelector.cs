// Native driver for the opening-combat choice sequence. One harness run answers exactly the
// selections the Sts2Solver replay consumed, in order, and records the request the game made so the
// offline comparison can check kind, option list and bounds.
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;

namespace Sts2Solver.TestHost;

/// <summary>One native card-selection request observed during the opening build.</summary>
internal sealed class NativeSetupChoiceRecord
{
    public required bool Generated { get; init; }
    public required int MinCount { get; init; }
    public required int MaxCount { get; init; }
    public required string[] CardIds { get; init; }
    public required int[] Upgrades { get; init; }
    public required int[] SelectedIndexes { get; init; }
}

internal sealed class CombatSetupChoiceSelector(IReadOnlyList<ulong> selections) : ICardSelector
{
    private int _index;

    public List<NativeSetupChoiceRecord> Records { get; } = [];

    public int Calls => Records.Count;

    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,
        int minSelect, int maxSelect)
    {
        List<CardModel> live = [.. options];
        if (_index >= selections.Count)
        {
            throw new InvalidDataException(
                $"原生开战请求了 {_index + 1} 个选择，序列只有 {selections.Count} 个。");
        }
        int min = Math.Min(minSelect, live.Count);
        int max = Math.Min(maxSelect, live.Count);
        ulong mask = selections[_index++];
        ulong valid = live.Count >= 64 ? ulong.MaxValue : (1UL << live.Count) - 1;
        int selectedCount = System.Numerics.BitOperations.PopCount(mask);
        if ((mask & ~valid) != 0 || selectedCount < min || selectedCount > max)
        {
            throw new InvalidDataException(
                $"原生开战选择 {mask:X} 不符合 {live.Count} 项、{min}..{max} 张的限制。");
        }
        bool generated = live.Count != 0 && live[0].Pile == null;
        for (int index = 0; index < live.Count; index++)
        {
            bool inHand = live[index].Pile?.Type == PileType.Hand;
            if (inHand == generated)
            {
                throw new InvalidDataException("原生开战选择的候选同时包含手牌与非牌堆卡。");
            }
        }
        string[] ids = new string[live.Count];
        int[] upgrades = new int[live.Count];
        for (int index = 0; index < live.Count; index++)
        {
            ids[index] = live[index].Id.Entry;
            upgrades[index] = live[index].CurrentUpgradeLevel;
        }
        Records.Add(new NativeSetupChoiceRecord
        {
            Generated = generated,
            MinCount = min,
            MaxCount = max,
            CardIds = ids,
            Upgrades = upgrades,
            SelectedIndexes = [.. Selected(mask, live.Count)],
        });
        List<CardModel> selected = [];
        for (int index = 0; index < live.Count; index++)
        {
            if ((mask & (1UL << index)) != 0)
                selected.Add(live[index]);
        }
        return Task.FromResult<IEnumerable<CardModel>>(selected);
    }

    private static IEnumerable<int> Selected(ulong mask, int count)
    {
        for (int index = 0; index < count; index++)
        {
            if ((mask & (1UL << index)) != 0)
                yield return index;
        }
    }

    public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
        IReadOnlyList<CardRewardAlternative> alternatives)
        => throw new InvalidDataException("开战建局请求了卡牌奖励选择。");
}

/// <summary>
/// The run-pilot answer: first option, except an optional subset (GamblingChip) selects none.
/// </summary>
internal sealed class FirstChoiceCombatSetupSelector : ICardSelector
{
    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,
        int minSelect, int maxSelect)
    {
        List<CardModel> live = [.. options];
        int take = live.Count == 0 || (minSelect == 0 && maxSelect > 1) ? 0 : 1;
        if (take < Math.Min(minSelect, live.Count) || take > Math.Min(maxSelect, live.Count))
        {
            throw new InvalidOperationException(
                $"开战第一选项 {take} 张不满足原生 {minSelect}..{maxSelect} 张。");
        }
        return Task.FromResult<IEnumerable<CardModel>>(live.Take(take));
    }

    public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
        IReadOnlyList<CardRewardAlternative> alternatives)
        => throw new InvalidDataException("开战建局请求了卡牌奖励选择。");
}
