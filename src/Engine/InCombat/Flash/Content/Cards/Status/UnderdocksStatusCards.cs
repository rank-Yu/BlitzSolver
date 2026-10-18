namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Status;

// Status cards the first-act (Underdocks) monsters put into the player's piles. Their canonical stats
// come from the rule compiler; the definitions only mirror the existing engine's OnPlay.

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
