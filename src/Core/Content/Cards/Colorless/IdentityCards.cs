using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Colorless;

/// <summary>
/// Native <c>BundleOfJoy</c>: add <c>Cards</c> distinct colorless cards to the hand.
/// <c>CardGenerationCardMirrors.BundleOfJoyOnPlay</c>.
/// </summary>
[BlitzContent("BUNDLE_OF_JOY")]
public sealed class BundleOfJoy : BlitzCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.ColorlessAll];

    public override void OnPlay(ref BlitzExec x)
    {
        int count = Math.Max(0, (int)BlitzCardEffects.Var(x, 0));
        if (count > BlitzLimits.Cards)
            throw new BlitzCapacityException("GeneratedCards", BlitzLimits.Cards);
        Span<BlitzCard> generated = stackalloc BlitzCard[count];
        int sampled = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.ColorlessAll, count,
            distinct: true, BlitzRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < sampled; index++)
        {
            BlitzKernel.AddGeneratedCard(ref x, generated[index], BlitzPile.Hand);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>ManifestAuthority</c>: gain <c>Block</c>, then add one distinct colorless card (upgraded
/// with the card) to the hand. <c>CardGenerationCardMirrors.ManifestAuthorityOnPlay</c>.
/// </summary>
[BlitzContent("MANIFEST_AUTHORITY")]
public sealed class ManifestAuthority : BlitzCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.ColorlessAll];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        Span<BlitzCard> generated = stackalloc BlitzCard[1];
        int sampled = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.ColorlessAll, 1,
            distinct: true, BlitzRngStream.CombatCardGeneration, generated);
        if (sampled == 0)
            return;
        BlitzCard card = generated[0];
        if (x.Card.Upgrade > 0)
            BlitzKernel.UpgradeCard(x.R, ref card);
        BlitzKernel.AddGeneratedCard(ref x, card, BlitzPile.Hand);
    }
}

/// <summary>
/// Native <c>Quasar</c>: sample three distinct colorless cards (upgrade them when Quasar is upgraded),
/// then choose one to add to the hand; the choice may be skipped.
/// <c>CardGenerationCardMirrors.QuasarOnPlay</c> and <c>CardChoiceResolution.AddGeneratedSelectionToHand</c>.
/// </summary>
[BlitzContent("QUASAR")]
public sealed class Quasar : BlitzCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.ColorlessAll];

    public override void OnPlay(ref BlitzExec x)
    {
        Span<BlitzCard> options = stackalloc BlitzCard[3];
        int count = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.ColorlessAll, 3,
            distinct: true, BlitzRngStream.CombatCardGeneration, options);
        if (count == 0)
            return;
        for (int index = 0; index < count; index++)
        {
            if (x.Card.Upgrade > 0)
                BlitzKernel.UpgradeCard(x.R, ref options[index]);
            x.GeneratedOptions[index] = options[index];
        }
        x.GeneratedOptionCount = count;
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        int count = x.GeneratedOptionCount;
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseGeneratedCardsInRange(ref x, BlitzChoiceEffect.GenerateToHand,
                x.GeneratedOptions[..count], minCount: 0, maxCount: 1, selected, out int selectedCount))
            return;
        if (selectedCount == 0)
            return;
        // Quasar's GenerateToHand branch is not in the free-this-turn set; the option value is added
        // as-is (PredictedCard.Clone copies the option, not a gameplay clone).
        BlitzKernel.AddGeneratedCard(ref x, x.GeneratedOptions[selected[0]], BlitzPile.Hand);
    }
}

/// <summary>
/// Native <c>SpectrumShift</c>: apply SpectrumShiftPower with the Cards variable.
/// <c>CardPowerOnPlaySupport.SToZ</c>'s SpectrumShift case.
/// </summary>
[BlitzContent("SPECTRUM_SHIFT")]
public sealed class SpectrumShift : BlitzCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["SPECTRUM_SHIFT_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!BlitzRegistry.TryPower("SPECTRUM_SHIFT_POWER", out ushort power))
            throw new BlitzUnsupportedException("SpectrumShift 需要 SPECTRUM_SHIFT_POWER 定义。");
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, power, (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
    }
}
