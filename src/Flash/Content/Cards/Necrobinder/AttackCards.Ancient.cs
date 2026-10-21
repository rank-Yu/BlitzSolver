using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

/// <summary>
/// <c>Protector</c>: Osty attack whose calculated damage is CalculationBase + ExtraDamage × Osty's max HP
/// (<c>CalculatedVarSpecRegistry.Protector</c>).
/// </summary>
[FlashContent("PROTECTOR")]
public sealed class Protector : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        decimal damage = NecrobinderContent.Calculated(ref x, 0, 1, x.B.Osty.MaxHp);
        NecrobinderContent.OstyAttack(ref x, damage);
    }
}
