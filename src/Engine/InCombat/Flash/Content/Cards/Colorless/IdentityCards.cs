namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Colorless;

// Colorless cards whose closure was previously blocked by instance-identity content. Existing-engine
// paths are named per definition.

/// <summary>
/// Native <c>BundleOfJoy</c>: add <c>Cards</c> distinct colorless cards to the hand.
/// <c>CardGenerationCardMirrors.BundleOfJoyOnPlay</c>.
/// </summary>
[FlashContent("BUNDLE_OF_JOY")]
public sealed class BundleOfJoy : FlashCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.ColorlessAll];

    public override void OnPlay(ref FlashExec x)
    {
        int count = Math.Max(0, (int)FlashCardEffects.Var(x, 0));
        if (count > FlashLimits.Cards)
            throw new FlashCapacityException("GeneratedCards", FlashLimits.Cards);
        Span<FlashCard> generated = stackalloc FlashCard[count];
        int sampled = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.ColorlessAll, count,
            distinct: true, FlashRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < sampled; index++)
        {
            FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Hand);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>ManifestAuthority</c>: gain <c>Block</c>, then add one distinct colorless card (upgraded
/// with the card) to the hand. <c>CardGenerationCardMirrors.ManifestAuthorityOnPlay</c>.
/// </summary>
[FlashContent("MANIFEST_AUTHORITY")]
public sealed class ManifestAuthority : FlashCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.ColorlessAll];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        Span<FlashCard> generated = stackalloc FlashCard[1];
        int sampled = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.ColorlessAll, 1,
            distinct: true, FlashRngStream.CombatCardGeneration, generated);
        if (sampled == 0)
            return;
        FlashCard card = generated[0];
        if (x.Card.Upgrade > 0)
            FlashKernel.UpgradeCard(x.R, ref card);
        FlashKernel.AddGeneratedCard(ref x, card, FlashPile.Hand);
    }
}

/// <summary>
/// Native <c>Quasar</c>: sample three distinct colorless cards (upgrade them when Quasar is upgraded),
/// then choose one to add to the hand; the choice may be skipped.
/// <c>CardGenerationCardMirrors.QuasarOnPlay</c> and <c>CardChoiceResolution.AddGeneratedSelectionToHand</c>.
/// </summary>
[FlashContent("QUASAR")]
public sealed class Quasar : FlashCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.ColorlessAll];

    public override void OnPlay(ref FlashExec x)
    {
        Span<FlashCard> options = stackalloc FlashCard[3];
        int count = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.ColorlessAll, 3,
            distinct: true, FlashRngStream.CombatCardGeneration, options);
        if (count == 0)
            return;
        for (int index = 0; index < count; index++)
        {
            if (x.Card.Upgrade > 0)
                FlashKernel.UpgradeCard(x.R, ref options[index]);
            x.GeneratedOptions[index] = options[index];
        }
        x.GeneratedOptionCount = count;
    }

    public override void OwnChoice(ref FlashExec x)
    {
        int count = x.GeneratedOptionCount;
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseGeneratedCardsInRange(ref x, FlashChoiceEffect.GenerateToHand,
                x.GeneratedOptions[..count], minCount: 0, maxCount: 1, selected, out int selectedCount))
            return;
        if (selectedCount == 0)
            return;
        // Quasar's GenerateToHand branch is not in the free-this-turn set; the option value is added
        // as-is (PredictedCard.Clone copies the option, not a gameplay clone).
        FlashKernel.AddGeneratedCard(ref x, x.GeneratedOptions[selected[0]], FlashPile.Hand);
    }
}

/// <summary>
/// Native <c>SpectrumShift</c>: apply SpectrumShiftPower with the Cards variable.
/// <c>CardPowerOnPlaySupport.SToZ</c>'s SpectrumShift case.
/// </summary>
[FlashContent("SPECTRUM_SHIFT")]
public sealed class SpectrumShift : FlashCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["SPECTRUM_SHIFT_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryPower("SPECTRUM_SHIFT_POWER", out ushort power))
            throw new FlashUnsupportedException("SpectrumShift 需要 SPECTRUM_SHIFT_POWER 定义。");
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
    }
}
