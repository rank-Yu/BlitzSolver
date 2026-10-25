using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[BlitzContent("FUEL")]
public sealed class Fuel : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref BlitzExec x) => BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 0));
}
