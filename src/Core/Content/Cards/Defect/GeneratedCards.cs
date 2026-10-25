using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[ContentId("FUEL")]
public sealed class Fuel : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref Exec x) => Kernel.GainEnergy(ref x, CardEffects.Var(x, 0));
}
