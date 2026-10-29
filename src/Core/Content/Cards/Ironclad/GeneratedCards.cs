using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Cards.Ironclad;

[ContentId("GIANT_ROCK")]
public sealed class GiantRock : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralAttack(ref x, 0);
}
