using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Status;

/// <summary>
/// Native <c>Slimed</c>: cost 1, Exhaust; drawing one card. The existing engine runs the inferred
/// <c>Cards</c> draw recipe (<c>CardDrawCardMirrors</c> / general draw), so Flash draws after the
/// normal mirrored/compensated split with no other effects.
/// </summary>
[FlashContent("SLIMED")]
public sealed class Slimed : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);
}
