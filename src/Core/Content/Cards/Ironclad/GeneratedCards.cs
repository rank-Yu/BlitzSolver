using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Ironclad;

[BlitzContent("GIANT_ROCK")]
public sealed class GiantRock : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}
