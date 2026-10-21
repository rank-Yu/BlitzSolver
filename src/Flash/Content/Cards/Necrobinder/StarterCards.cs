using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

[FlashContent("STRIKE_NECROBINDER")]
public sealed class StrikeNecrobinder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("DEFEND_NECROBINDER")]
public sealed class DefendNecrobinder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("BODYGUARD")]
public sealed class Bodyguard : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
}

/// <summary>
/// <c>Unleash</c>: a powered Osty attack whose CalculatedDamage is CalculationBase + ExtraDamage ×
/// Osty's current HP (<c>CalculatedVarSpecRegistry</c>); with a missing Osty nothing is attacked.
/// The rule table stores CalculationBase and ExtraDamage, so the card computes the native
/// <c>CalculatedVar.Calculate</c> formula itself.
/// </summary>
[FlashContent("UNLEASH")]
public sealed class Unleash : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        decimal damage = NecrobinderContent.Calculated(ref x, 0, 1, x.B.Osty.CurrentHp);
        NecrobinderContent.OstyAttack(ref x, damage);
    }
}
