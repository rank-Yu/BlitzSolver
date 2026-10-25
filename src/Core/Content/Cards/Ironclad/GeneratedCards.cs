using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Ironclad;

[ContentId("GIANT_ROCK")]
public sealed class GiantRock : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralAttack(ref x, 0);
}
