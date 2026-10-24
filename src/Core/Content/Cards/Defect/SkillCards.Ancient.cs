using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Defect;

[FlashContent("QUADCAST")]
public sealed class Quadcast : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    // OrbCardMirrors.QuadcastOnPlay: OrbEvokeNext with Repeat hits; only the last one dequeues.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.EvokeNext(ref x, (int)FlashCardEffects.Var(x, 0));
    }
}
