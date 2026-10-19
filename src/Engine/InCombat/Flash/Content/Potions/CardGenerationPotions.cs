namespace CombatSolver.Engine.InCombat.Flash.Content.Potions;

// Potions whose existing-engine case generates cards from a frozen pool. OrobicAcid:
// CardGenerationPotionMirrors.GenerateOrobicAcidCards (AddsToHand true) samples one distinct card of each
// type, calls SetToFreeThisTurn on all of them and adds them to the hand. Candidate pools are frozen by the
// Bridge with the existing engine's own queries; the kernel only samples. Definitions hold no state.
// AttackPotion/SkillPotion/PowerPotion/ColorlessPotion generate three distinct candidates and let the
// player pick 0..1 (PotionChoiceSupport.GetSpec's GenerateToHand RangeSpec); the selected card is cloned,
// made free this turn and added to hand (PotionChoiceSupport.Apply).

[FlashContent("ATTACK_POTION")]
public sealed class AttackPotion : FlashPotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void OnUse(ref FlashExec x)
        => GenerateToHand.Choose(ref x, FlashCardType.Attack, FlashCardPools.CharacterAll);
}

[FlashContent("SKILL_POTION")]
public sealed class SkillPotion : FlashPotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void OnUse(ref FlashExec x)
        => GenerateToHand.Choose(ref x, FlashCardType.Skill, FlashCardPools.CharacterAll);
}

[FlashContent("POWER_POTION")]
public sealed class PowerPotion : FlashPotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void OnUse(ref FlashExec x)
        => GenerateToHand.Choose(ref x, FlashCardType.Power, FlashCardPools.CharacterAll);
}

[FlashContent("COLORLESS_POTION")]
public sealed class ColorlessPotion : FlashPotionDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.ColorlessAll];

    public override void OnUse(ref FlashExec x)
        => GenerateToHand.Choose(ref x, null, FlashCardPools.ColorlessAll);
}

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

[FlashContent("OROBIC_ACID")]
public sealed class OrobicAcid : FlashPotionDef
{
    // One distinct Attack, then Skill, then Power from the character pool; the Attack/Skill/Power
    // union is the whole character pool, so the full pool is the exact closure.
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void OnUse(ref FlashExec x)
    {
        Span<FlashCard> generated = stackalloc FlashCard[3];
        Span<FlashCard> one = stackalloc FlashCard[1];
        int count = 0;
        if (FlashCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, FlashCardType.Attack, 1, one) == 1)
            generated[count++] = one[0];
        if (FlashCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, FlashCardType.Skill, 1, one) == 1)
            generated[count++] = one[0];
        if (FlashCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, FlashCardType.Power, 1, one) == 1)
            generated[count++] = one[0];
        // CardGenerationPotionMirrors.GenerateOrobicAcidCards: SetToFreeThisTurn on every generated
        // card, then AddGeneratedCardsToCombat(hand, Bottom) keeps that order.
        for (int index = 0; index < count; index++)
        {
            FlashKernel.MakeFreeThisTurn(x.R, ref generated[index]);
            FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Hand);
        }
    }
}
