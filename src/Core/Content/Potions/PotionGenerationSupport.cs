using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Potions;

/// <summary>
/// The shared 0..1 GenerateToHand choice of the four card-generation potions. Candidate generation is
/// the potion's case in <c>CardGenerationPotionMirrors.Generate</c>: three distinct cards (type filtered
/// before the shuffle for the character cards, the pool itself for colorless) from
/// <c>CombatCardGeneration</c>. The selection is <c>PotionChoiceSupport.GetSpec/Apply</c>: clone the
/// chosen option, <c>SetToFreeThisTurn</c>, then add it to hand at the bottom.
/// </summary>
internal static class GenerateToHand
{
    public static void Choose(ref FlashExec x, FlashCardType? type, string poolKey)
    {
        Span<FlashCard> options = stackalloc FlashCard[3];
        int count;
        if (type is { } cardType)
            count = FlashCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, cardType, 3, options);
        else
            count = FlashKernel.SampleCardPool(ref x.B, x.R, poolKey, 3, distinct: true,
                FlashRngStream.CombatCardGeneration, options);
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseGeneratedCardsInRange(ref x, FlashChoiceEffect.GenerateToHand, options[..count],
                0, 1, selected, out int selectedCount))
            return;
        for (int index = 0; index < selectedCount; index++)
        {
            // PotionChoiceSupport.Apply uses PredictedCard.Clone (not CreateClone): the value is copied
            // without setting IsClone.
            FlashCard card = options[selected[index]];
            FlashKernel.MakeFreeThisTurn(x.R, ref card);
            FlashKernel.AddGeneratedCard(ref x, card, FlashPile.Hand);
            if (x.NeedsChoice)
                return;
        }
    }
}
