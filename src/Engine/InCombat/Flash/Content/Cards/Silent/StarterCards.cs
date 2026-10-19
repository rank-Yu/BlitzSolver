namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Silent;

// Silent starter deck. Existing engine paths: StrikeSilent/DefendSilent run the general
// attack/block recipe; Neutralize runs the attack recipe then CorePowerSupport.ApplyCardPowers
// applies Weak; Survivor gains block then resolves CardChoiceSupport.GetSpec's
// Discard(Hand, 1) selection through CardCmd.Discard.

[FlashContent("STRIKE_SILENT")]
public sealed class StrikeSilent : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("DEFEND_SILENT")]
public sealed class DefendSilent : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("NEUTRALIZE")]
public sealed class Neutralize : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        // Neutralize.cs: DamageCmd.Attack, then PowerCmd.Apply<WeakPower>(target, Weak).
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[FlashContent("SURVIVOR")]
public sealed class Survivor : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: Survivor → Discard, Hand, 1, every hand card. The played card is in the play pile
        // during resolution, so it is not an option; an empty hand asks nothing.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Discard, FlashPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        FlashKernel.DiscardAndDraw(ref x, FlashPile.Hand, selected[..chosen], 0);
    }
}
