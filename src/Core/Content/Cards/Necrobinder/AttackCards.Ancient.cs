using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Necrobinder;

/// <summary>
/// <c>Protector</c>: Osty attack whose calculated damage is CalculationBase + ExtraDamage × Osty's max HP
/// (<c>CalculatedVarSpecRegistry.Protector</c>).
/// </summary>
[ContentId("PROTECTOR")]
public sealed class Protector : CardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref Exec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        decimal damage = NecrobinderContent.Calculated(ref x, 0, 1, x.B.Osty.MaxHp);
        NecrobinderContent.OstyAttack(ref x, damage);
    }
}
