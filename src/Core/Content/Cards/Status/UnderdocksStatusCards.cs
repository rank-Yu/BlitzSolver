using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Status;

/// <summary>
/// Native <c>Dazed</c>: cost 0, Unplayable, Ethereal. It never executes an OnPlay effect; the kernel
/// exhausts it from the hand at turn end through the Ethereal flag.
/// </summary>
[BlitzContent("DAZED")]
public sealed class Dazed : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
    }
}
