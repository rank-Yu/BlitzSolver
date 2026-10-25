// Ported from CombatSolver/src/Testing/Host/UnattendedCardSelector.cs
// at commit a62f1850 (MIT, Copyright (c) 2026 Torch1230 and contributors).
// Only the deterministic "__FIRST__" behavior needed to answer the native turn-setup page is kept.
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;

namespace OfflineHost;

internal sealed class FirstChoiceCardSelector : ICardSelector
{
    /// <summary>Card ids answered by this selector; used to identify the ordinary setup root.</summary>
    public static List<string> LastSelections { get; } = [];

    public Task<IEnumerable<CardModel>> GetSelectedCards(
        IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        List<CardModel> remaining = options.ToList();
        List<CardModel> selected = remaining.Count > 0 ? [remaining[0]] : [];
        if (selected.Count > 0)
            LastSelections.Add(selected[0].Id.Entry);
        if (selected.Count < minSelect || selected.Count > maxSelect)
            throw new InvalidOperationException(
                $"原生测试选择 {selected.Count} 张牌，但界面要求 {minSelect}..{maxSelect} 张。");
        return Task.FromResult<IEnumerable<CardModel>>(selected);
    }

    public CardRewardSelection GetSelectedCardReward(
        IReadOnlyList<CardCreationResult> options,
        IReadOnlyList<CardRewardAlternative> alternatives)
    {
        CardModel? selected = options.FirstOrDefault()?.Card;
        return selected == null ? default : new CardRewardSelection { card = selected };
    }
}
