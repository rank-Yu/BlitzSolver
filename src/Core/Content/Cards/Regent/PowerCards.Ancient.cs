using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary>
/// <c>TheSealedThrone</c>: applies TheSealedThronePower 1; the power gains its Amount stars before
/// each card play by its owner (<c>BeforeCardPlayedMirrors.HandleTheSealedThronePower</c>).
/// </summary>
[BlitzContent("THE_SEALED_THRONE")]
public sealed class TheSealedThrone : BlitzCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["THE_SEALED_THRONE_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.TheSealedThronePower>(), 1, BlitzLimits.PlayerOwner);
    }
}
