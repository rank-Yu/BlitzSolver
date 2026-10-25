using BlitzSolver.Engine;

namespace BlitzSolver.Content.Potions;

/// <summary>
/// The shared 0..1 GenerateToHand choice of the four card-generation potions. Candidate generation is
/// the potion's case in <c>CardGenerationPotionMirrors.Generate</c>: three distinct cards (type filtered
/// before the shuffle for the character cards, the pool itself for colorless) from
/// <c>CombatCardGeneration</c>. The selection is <c>PotionChoiceSupport.GetSpec/Apply</c>: clone the
/// chosen option, <c>SetToFreeThisTurn</c>, then add it to hand at the bottom.
/// </summary>
internal static class GenerateToHand
{
    public static void Choose(ref BlitzExec x, BlitzCardType? type, string poolKey)
    {
        Span<BlitzCard> options = stackalloc BlitzCard[3];
        int count;
        if (type is { } cardType)
            count = BlitzCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, cardType, 3, options);
        else
            count = BlitzKernel.SampleCardPool(ref x.B, x.R, poolKey, 3, distinct: true,
                BlitzRngStream.CombatCardGeneration, options);
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseGeneratedCardsInRange(ref x, BlitzChoiceEffect.GenerateToHand, options[..count],
                0, 1, selected, out int selectedCount))
            return;
        for (int index = 0; index < selectedCount; index++)
        {
            // PotionChoiceSupport.Apply uses PredictedCard.Clone (not CreateClone): the value is copied
            // without setting IsClone.
            BlitzCard card = options[selected[index]];
            BlitzKernel.MakeFreeThisTurn(x.R, ref card);
            BlitzKernel.AddGeneratedCard(ref x, card, BlitzPile.Hand);
            if (x.NeedsChoice)
                return;
        }
    }
}
