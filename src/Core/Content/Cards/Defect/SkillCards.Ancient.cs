using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[BlitzContent("QUADCAST")]
public sealed class Quadcast : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    // OrbCardMirrors.QuadcastOnPlay: OrbEvokeNext with Repeat hits; only the last one dequeues.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.EvokeNext(ref x, (int)BlitzCardEffects.Var(x, 0));
    }
}
