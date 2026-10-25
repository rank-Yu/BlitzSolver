using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Necrobinder;

[BlitzContent("STRIKE_NECROBINDER")]
public sealed class StrikeNecrobinder : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("DEFEND_NECROBINDER")]
public sealed class DefendNecrobinder : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);
}

[BlitzContent("BODYGUARD")]
public sealed class Bodyguard : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref BlitzExec x) => BlitzKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
}

/// <summary>
/// <c>Unleash</c>: a powered Osty attack whose CalculatedDamage is CalculationBase + ExtraDamage ×
/// Osty's current HP (<c>CalculatedVarSpecRegistry</c>); with a missing Osty nothing is attacked.
/// The rule table stores CalculationBase and ExtraDamage, so the card computes the native
/// <c>CalculatedVar.Calculate</c> formula itself.
/// </summary>
[BlitzContent("UNLEASH")]
public sealed class Unleash : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        decimal damage = NecrobinderContent.Calculated(ref x, 0, 1, x.B.Osty.CurrentHp);
        NecrobinderContent.OstyAttack(ref x, damage);
    }
}
