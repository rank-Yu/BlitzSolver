using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

[BlitzContent("STRIKE_SILENT")]
public sealed class StrikeSilent : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("DEFEND_SILENT")]
public sealed class DefendSilent : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);
}

[BlitzContent("NEUTRALIZE")]
public sealed class Neutralize : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        // Neutralize.cs: DamageCmd.Attack, then PowerCmd.Apply<WeakPower>(target, Weak).
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[BlitzContent("SURVIVOR")]
public sealed class Survivor : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref BlitzExec x)
    {
        // GetSpec: Survivor → Discard, Hand, 1, every hand card. The played card is in the play pile
        // during resolution, so it is not an option; an empty hand asks nothing.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Discard, BlitzPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        BlitzKernel.DiscardAndDraw(ref x, BlitzPile.Hand, selected[..chosen], 0);
    }
}
