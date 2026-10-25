using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

[ContentId("STRIKE_SILENT")]
public sealed class StrikeSilent : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralAttack(ref x, 0);
}

[ContentId("DEFEND_SILENT")]
public sealed class DefendSilent : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralBlock(ref x, 0);
}

[ContentId("NEUTRALIZE")]
public sealed class Neutralize : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

    public override void OnPlay(ref Exec x)
    {
        // Neutralize.cs: DamageCmd.Attack, then PowerCmd.Apply<WeakPower>(target, Weak).
        CardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        Kernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        CardEffects.ApplyToTarget(ref x, Registry.PowerId<Powers.WeakPower>(), 1);
    }
}

[ContentId("SURVIVOR")]
public sealed class Survivor : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref Exec x)
    {
        // GetSpec: Survivor → Discard, Hand, 1, every hand card. The played card is in the play pile
        // during resolution, so it is not an option; an empty hand asks nothing.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!Kernel.ChooseCards(ref x, ChoiceEffect.Discard, Pile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        Kernel.DiscardAndDraw(ref x, Pile.Hand, selected[..chosen], 0);
    }
}
