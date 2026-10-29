using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Cards.Defect;

[ContentId("QUADCAST")]
public sealed class Quadcast : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    // OrbCardMirrors.QuadcastOnPlay: OrbEvokeNext with Repeat hits; only the last one dequeues.
    public override void OnPlay(ref Exec x)
    {
        Kernel.EvokeNext(ref x, (int)CardEffects.Var(x, 0));
    }
}
