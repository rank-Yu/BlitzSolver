using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

/// <summary>
/// <c>TheSealedThrone</c>: applies TheSealedThronePower 1; the power gains its Amount stars before
/// each card play by its owner (<c>BeforeCardPlayedMirrors.HandleTheSealedThronePower</c>).
/// </summary>
[FlashContent("THE_SEALED_THRONE")]
public sealed class TheSealedThrone : FlashCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["THE_SEALED_THRONE_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.TheSealedThronePower>(), 1, FlashLimits.PlayerOwner);
    }
}
