using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Potions;

/// <summary>
/// The shared 0..1 GenerateToHand choice of the four card-generation potions. Candidate generation is
/// the potion's case in <c>CardGenerationPotionMirrors.Generate</c>: three distinct cards (type filtered
/// before the shuffle for the character cards, the pool itself for colorless) from
/// <c>CombatCardGeneration</c>. The selection is <c>PotionChoiceSupport.GetSpec/Apply</c>: clone the
/// chosen option, <c>SetToFreeThisTurn</c>, then add it to hand at the bottom.
/// </summary>
internal static class GenerateToHand
{
    public static void Choose(ref Exec x, CardType? type, string poolKey)
    {
        Span<Card> options = stackalloc Card[3];
        int count;
        if (type is { } cardType)
            count = CardEffects.SampleCharacterCardsOfType(ref x.B, x.R, cardType, 3, options);
        else
            count = Kernel.SampleCardPool(ref x.B, x.R, poolKey, 3, distinct: true,
                RngStream.CombatCardGeneration, options);
        Span<int> selected = stackalloc int[1];
        if (!Kernel.ChooseGeneratedCardsInRange(ref x, ChoiceEffect.GenerateToHand, options[..count],
                0, 1, selected, out int selectedCount))
            return;
        for (int index = 0; index < selectedCount; index++)
        {
            // PotionChoiceSupport.Apply uses PredictedCard.Clone (not CreateClone): the value is copied
            // without setting IsClone.
            Card card = options[selected[index]];
            Kernel.MakeFreeThisTurn(x.R, ref card);
            Kernel.AddGeneratedCard(ref x, card, Pile.Hand);
            if (x.NeedsChoice)
                return;
        }
    }
}
