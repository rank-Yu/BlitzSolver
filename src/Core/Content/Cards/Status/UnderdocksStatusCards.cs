using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Content.Cards.Status;

/// <summary>
/// Native <c>Dazed</c>: cost 0, Unplayable, Ethereal. It never executes an OnPlay effect; the kernel
/// exhausts it from the hand at turn end through the Ethereal flag.
/// </summary>
[ContentId("DAZED")]
public sealed class Dazed : CardDef
{
    public override void OnPlay(ref Exec x)
    {
    }
}
