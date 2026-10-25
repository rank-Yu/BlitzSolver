using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Colorless;

/// <summary>
/// Native <c>BundleOfJoy</c>: add <c>Cards</c> distinct colorless cards to the hand.
/// <c>CardGenerationCardMirrors.BundleOfJoyOnPlay</c>.
/// </summary>
[ContentId("BUNDLE_OF_JOY")]
public sealed class BundleOfJoy : CardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [CardPools.ColorlessAll];

    public override void OnPlay(ref Exec x)
    {
        int count = Math.Max(0, (int)CardEffects.Var(x, 0));
        if (count > Limits.Cards)
            throw new CapacityException("GeneratedCards", Limits.Cards);
        Span<Card> generated = stackalloc Card[count];
        int sampled = Kernel.SampleCardPool(ref x.B, x.R, CardPools.ColorlessAll, count,
            distinct: true, RngStream.CombatCardGeneration, generated);
        for (int index = 0; index < sampled; index++)
        {
            Kernel.AddGeneratedCard(ref x, generated[index], Pile.Hand);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>ManifestAuthority</c>: gain <c>Block</c>, then add one distinct colorless card (upgraded
/// with the card) to the hand. <c>CardGenerationCardMirrors.ManifestAuthorityOnPlay</c>.
/// </summary>
[ContentId("MANIFEST_AUTHORITY")]
public sealed class ManifestAuthority : CardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<string> GeneratedCardPools => [CardPools.ColorlessAll];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        Span<Card> generated = stackalloc Card[1];
        int sampled = Kernel.SampleCardPool(ref x.B, x.R, CardPools.ColorlessAll, 1,
            distinct: true, RngStream.CombatCardGeneration, generated);
        if (sampled == 0)
            return;
        Card card = generated[0];
        if (x.Card.Upgrade > 0)
            Kernel.UpgradeCard(x.R, ref card);
        Kernel.AddGeneratedCard(ref x, card, Pile.Hand);
    }
}

/// <summary>
/// Native <c>Quasar</c>: sample three distinct colorless cards (upgrade them when Quasar is upgraded),
/// then choose one to add to the hand; the choice may be skipped.
/// <c>CardGenerationCardMirrors.QuasarOnPlay</c> and <c>CardChoiceResolution.AddGeneratedSelectionToHand</c>.
/// </summary>
[ContentId("QUASAR")]
public sealed class Quasar : CardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> GeneratedCardPools => [CardPools.ColorlessAll];

    public override void OnPlay(ref Exec x)
    {
        Span<Card> options = stackalloc Card[3];
        int count = Kernel.SampleCardPool(ref x.B, x.R, CardPools.ColorlessAll, 3,
            distinct: true, RngStream.CombatCardGeneration, options);
        if (count == 0)
            return;
        for (int index = 0; index < count; index++)
        {
            if (x.Card.Upgrade > 0)
                Kernel.UpgradeCard(x.R, ref options[index]);
            x.GeneratedOptions[index] = options[index];
        }
        x.GeneratedOptionCount = count;
    }

    public override void OwnChoice(ref Exec x)
    {
        int count = x.GeneratedOptionCount;
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!Kernel.ChooseGeneratedCardsInRange(ref x, ChoiceEffect.GenerateToHand,
                x.GeneratedOptions[..count], minCount: 0, maxCount: 1, selected, out int selectedCount))
            return;
        if (selectedCount == 0)
            return;
        // Quasar's GenerateToHand branch is not in the free-this-turn set; the option value is added
        // as-is (PredictedCard.Clone copies the option, not a gameplay clone).
        Kernel.AddGeneratedCard(ref x, x.GeneratedOptions[selected[0]], Pile.Hand);
    }
}

/// <summary>
/// Native <c>SpectrumShift</c>: apply SpectrumShiftPower with the Cards variable.
/// <c>CardPowerOnPlaySupport.SToZ</c>'s SpectrumShift case.
/// </summary>
[ContentId("SPECTRUM_SHIFT")]
public sealed class SpectrumShift : CardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["SPECTRUM_SHIFT_POWER"];

    public override void OnPlay(ref Exec x)
    {
        if (!Registry.TryPower("SPECTRUM_SHIFT_POWER", out ushort power))
            throw new UnsupportedException("SpectrumShift 需要 SPECTRUM_SHIFT_POWER 定义。");
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, power, (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
    }
}
