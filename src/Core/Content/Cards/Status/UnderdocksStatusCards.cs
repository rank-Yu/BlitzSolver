using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Status;

/// <summary>
/// Native <c>Dazed</c>: cost 0, Unplayable, Ethereal. It never executes an OnPlay effect; the kernel
/// exhausts it from the hand at turn end through the Ethereal flag.
/// </summary>
[FlashContent("DAZED")]
public sealed class Dazed : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }
}
