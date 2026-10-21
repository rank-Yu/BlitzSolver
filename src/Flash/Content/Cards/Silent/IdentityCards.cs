using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

// Silent identity cards: Nightmare's selected-card clone and Phantom Blades' per-instance effect.
// Existing-engine paths are named per definition.

/// <summary>
/// Native <c>Nightmare</c>: choose a hand card, then apply an instanced NightmarePower that clones the
/// (affliction-cleared) selected card at the next BeforeHandDraw. Existing engine:
/// <c>CardChoiceSupport.GetSpec</c>'s Nightmare case, <c>CardChoiceResolution.ApplyNightmare</c> and
/// <c>SimulatedCombatState.SetNightmareSelection</c>.
/// </summary>
[FlashContent("NIGHTMARE")]
public sealed class Nightmare : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> AppliedPowers => ["NIGHTMARE_POWER"];

    // Nightmare's OnPlay only starts the hand selection; the power application is its post-choice
    // effect (CardChoiceResolution.ApplyNightmare), so the card effect itself is empty.
    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Nightmare, FlashPile.Hand, options, 1, selected,
                out int chosen))
            return;
        if (chosen == 0)
            return;
        if (!FlashRegistry.TryPower("NIGHTMARE_POWER", out ushort power))
            throw new FlashUnsupportedException("Nightmare 需要 NIGHTMARE_POWER 定义。");
        FlashCard snapshot = FlashKernel.CloneCard(FlashPiles.At(ref x.B, FlashPile.Hand, selected[0]));
        snapshot.AfflictionId = 0;
        snapshot.AfflictionAmount = 0;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, 3, FlashLimits.PlayerOwner,
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int row = FlashKernel.FindLastPower(x.B, FlashLimits.PlayerOwner, power);
        if (row < 0)
            return;
        int slot = FlashKernel.AllocateDetachedCard(ref x.B, snapshot);
        x.B.Powers[row].Aux0 = slot + 1;
    }
}

/// <summary>
/// Native <c>PhantomBlades</c>: apply PhantomBladesPower and, on the first stack, give every Shiv in
/// combat the local Retain keyword (<c>PhantomBladesPowerMirrors.AfterApplied</c>, called when the
/// power transitions from zero). Existing engine: <c>CardPowerOnPlaySupport</c>'s PhantomBlades case.
/// </summary>
[FlashContent("PHANTOM_BLADES")]
public sealed class PhantomBlades : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["PhantomBladesPower"];

    public override IReadOnlyList<string> AppliedPowers => ["PHANTOM_BLADES_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryPower("PHANTOM_BLADES_POWER", out ushort power))
            throw new FlashUnsupportedException("PhantomBlades 需要 PHANTOM_BLADES_POWER 定义。");
        bool alreadyPresent = FlashKernel.GetAmount(x.B, FlashLimits.PlayerOwner, power) > 0;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice || alreadyPresent)
            return;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref FlashCard card = ref x.B.Cards[position];
            FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if ((stats.Tags & FlashCardTag.Shiv) != 0)
                card.Keywords |= FlashKeywords.Retain;
        }
    }
}
