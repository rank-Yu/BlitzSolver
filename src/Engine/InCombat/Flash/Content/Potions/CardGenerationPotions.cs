namespace CombatSolver.Engine.InCombat.Flash.Content.Potions;

// Potions whose existing-engine case generates cards from a frozen pool. OrobicAcid:
// CardGenerationPotionMirrors.GenerateOrobicAcidCards (AddsToHand true) samples one distinct card of each
// type, calls SetToFreeThisTurn on all of them and adds them to the hand. Candidate pools are frozen by the
// Bridge with the existing engine's own queries; the kernel only samples. Definitions hold no state.
// AttackPotion/SkillPotion/PowerPotion/ColorlessPotion are not defined yet: their 0..1 GenerateToHand
// choice is resolved by PotionChoiceSupport, which the Flash replay translation does not cover.

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
