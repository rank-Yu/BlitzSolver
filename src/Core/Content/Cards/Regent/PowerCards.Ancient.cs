using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Cards.Regent;

/// <summary>
/// <c>TheSealedThrone</c>: applies TheSealedThronePower 1; the power gains its Amount stars before
/// each card play by its owner (<c>BeforeCardPlayedMirrors.HandleTheSealedThronePower</c>).
/// </summary>
[ContentId("THE_SEALED_THRONE")]
public sealed class TheSealedThrone : CardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["THE_SEALED_THRONE_POWER"];

    public override void OnPlay(ref Exec x)
    {
        Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.TheSealedThronePower>(), 1, Limits.PlayerOwner);
    }
}
