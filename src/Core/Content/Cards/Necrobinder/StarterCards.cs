using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Cards.Necrobinder;

[ContentId("STRIKE_NECROBINDER")]
public sealed class StrikeNecrobinder : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralAttack(ref x, 0);
}

[ContentId("DEFEND_NECROBINDER")]
public sealed class DefendNecrobinder : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralBlock(ref x, 0);
}

[ContentId("BODYGUARD")]
public sealed class Bodyguard : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref Exec x) => Kernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
}

/// <summary>
/// <c>Unleash</c>: a powered Osty attack whose CalculatedDamage is CalculationBase + ExtraDamage ×
/// Osty's current HP (<c>CalculatedVarSpecRegistry</c>); with a missing Osty nothing is attacked.
/// The rule table stores CalculationBase and ExtraDamage, so the card computes the native
/// <c>CalculatedVar.Calculate</c> formula itself.
/// </summary>
[ContentId("UNLEASH")]
public sealed class Unleash : CardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref Exec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        decimal damage = NecrobinderContent.Calculated(ref x, 0, 1, x.B.Osty.CurrentHp);
        NecrobinderContent.OstyAttack(ref x, damage);
    }
}
