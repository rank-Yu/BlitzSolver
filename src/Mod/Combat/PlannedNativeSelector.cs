// Native card-choice driver for route deployment. Setting CardSelectCmd's automated selector is the
// same surface CombatSolver's headless PlannedCardSelector uses at a62f1850 (MIT, Copyright (c)
// 2026 Torch1230 and contributors); this version additionally binds every option to the route's
// frozen card identities and pile before returning a selection.
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;
using BlitzSolver.Simulation.Combat;

namespace BlitzSolver;

internal sealed class ChoicePlanMismatchException(string message) : InvalidOperationException(message);

/// <summary>
/// Answers native card-selection requests from one action's frozen choice plan. Every request is
/// matched to the plan's next explicit choice by option count and card identity; a missing or
/// shifted request throws <see cref="ChoicePlanMismatchException"/> instead of guessing.
/// </summary>
internal sealed class PlannedNativeSelector(
    IReadOnlyList<PlannedChoice> choices,
    Player player,
    string owner) : ICardSelector
{
    private readonly IReadOnlyList<PlannedChoice> _choices = choices;
    private int _index;

    internal int PendingCount => _choices.Count - _index;

    internal static IDisposable Activate(PlannedNativeSelector selector)
        => CardSelectCmd.PushSelector(selector);

    internal void AssertConsumed()
    {
        if (_index != _choices.Count)
        {
            throw new ChoicePlanMismatchException(
                $"{owner} 仍有 {_choices.Count - _index} 个计划选择没有被游戏请求。" +
                $"下一个={Describe(_choices[_index])}");
        }
    }

    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,
        int minSelect, int maxSelect)
    {
        List<CardModel> live = options.ToList();
        if (_index >= _choices.Count)
            throw new ChoicePlanMismatchException(
                $"{owner} 请求了计划外的选牌：options={live.Count} bounds={minSelect}..{maxSelect} " +
                $"cards=[{string.Join(',', live.Select(CardName))}]");
        PlannedChoice choice = _choices[_index++];
        int[] map = MapOptions(choice, live);
        int expected = choice.SelectedIndexes().Count();
        int actualMin = Math.Min(minSelect, live.Count);
        int actualMax = Math.Min(maxSelect, live.Count);
        if (expected < actualMin || expected > actualMax)
        {
            throw new ChoicePlanMismatchException(
                $"{owner} 计划选择 {expected} 张，原生界面要求 {actualMin}..{actualMax} 张。" +
                $"choice={Describe(choice)}");
        }
        List<CardModel> selected = new(expected);
        foreach (int option in choice.SelectedIndexes())
            selected.Add(live[map[option]]);
        ModLog.Info(
            $"[BlitzSolver] DEPLOY_CHOICE {owner} index={_index - 1}/{_choices.Count} " +
            $"source={choice.Request.Source} effect={choice.Request.Effect} " +
            $"plan_bounds={choice.Request.MinCount}..{choice.Request.MaxCount} " +
            $"native_bounds={minSelect}..{maxSelect} options={live.Count} " +
            $"chosen=[{string.Join(',', selected.Select(CardName))}] " +
            $"identities=[{string.Join(',', live.Select(CardName))}]");
        return Task.FromResult<IEnumerable<CardModel>>(selected);
    }

    public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
        IReadOnlyList<CardRewardAlternative> alternatives)
        => throw new ChoicePlanMismatchException($"{owner} 请求了战斗内计划外的卡牌奖励选择。");

    private int[] MapOptions(PlannedChoice choice, List<CardModel> live)
    {
        int count = choice.Request.OptionCount;
        if (live.Count != count)
        {
            throw new ChoicePlanMismatchException(
                $"{owner} 选项数 {live.Count} 与计划 {count} 不符。" +
                $"choice={Describe(choice)} native=[{string.Join(',', live.Select(CardName))}]");
        }
        if (!choice.OptionsAreGenerated && !choice.Request.SourceIsNone)
        {
            PlayerCombatState state = player.PlayerCombatState
                ?? throw new ChoicePlanMismatchException($"{owner} 玩家没有战斗牌堆。");
            IReadOnlyList<CardModel> source = PileCards(state, choice.Request.Source);
            for (int index = 0; index < live.Count; index++)
            {
                if (!ContainsReference(source, live[index]))
                {
                    throw new ChoicePlanMismatchException(
                        $"{owner} 选项 {CardName(live[index])} 不在计划的牌堆 {choice.Request.Source} 中。" +
                        $"choice={Describe(choice)}");
                }
            }
        }
        bool[] used = new bool[count];
        int[] map = new int[count];
        for (int option = 0; option < count; option++)
        {
            Card predicted = choice.OptionsAreGenerated
                ? choice.GeneratedOptions[option]
                : choice.PileOptions[option];
            CardIdentity key = Identity(predicted);
            int match = -1;
            for (int candidate = 0; candidate < live.Count; candidate++)
            {
                if (!used[candidate] && Identity(live[candidate]) == key)
                {
                    match = candidate;
                    break;
                }
            }
            if (match < 0)
            {
                throw new ChoicePlanMismatchException(
                    $"{owner} 找不到选项 {option} 对应的原生卡 {key}。" +
                    $"choice={Describe(choice)} native=[{string.Join(',', live.Select(CardName))}]");
            }
            used[match] = true;
            map[option] = match;
        }
        return map;
    }

    private static IReadOnlyList<CardModel> PileCards(PlayerCombatState state, Pile pile) => pile switch
    {
        Pile.Hand => state.Hand.Cards,
        Pile.Draw => state.DrawPile.Cards,
        Pile.Discard => state.DiscardPile.Cards,
        Pile.Exhaust => state.ExhaustPile.Cards,
        Pile.Play => state.PlayPile.Cards,
        _ => throw new ChoicePlanMismatchException($"选择来源牌堆 {pile} 无效。"),
    };

    private static bool ContainsReference(IReadOnlyList<CardModel> cards, CardModel card)
    {
        for (int index = 0; index < cards.Count; index++)
        {
            if (ReferenceEquals(cards[index], card))
                return true;
        }
        return false;
    }

    private static CardIdentity Identity(Card card)
        => new(Registry.Card(card.Id).NativeId, card.Upgrade);

    private static CardIdentity Identity(CardModel card)
        => new(card.Id.Entry, card.CurrentUpgradeLevel);

    private static string CardName(CardModel card) => $"{card.Id.Entry}+{card.CurrentUpgradeLevel}";

    private static string Describe(PlannedChoice choice)
    {
        string[] cards = choice.OptionsAreGenerated
            ? [.. Enumerable.Range(0, choice.OptionCount)
                .Select(index => $"{Registry.Card(choice.GeneratedOptions[index].Id).NativeId}+{choice.GeneratedOptions[index].Upgrade}#gen")]
            : [.. Enumerable.Range(0, choice.OptionCount)
                .Select(index => $"{Registry.Card(choice.PileOptions[index].Id).NativeId}+{choice.PileOptions[index].Upgrade}#{choice.Request.Source}")];
        return $"{choice.Request.Kind}/{choice.Request.Effect} source={choice.Request.Source} " +
            $"count={choice.OptionCount} bounds={choice.Request.MinCount}..{choice.Request.MaxCount} " +
            $"selected=0x{choice.Selection:X} options=[{string.Join(',', cards)}]";
    }

    private readonly record struct CardIdentity(string Id, int Upgrade);
}
