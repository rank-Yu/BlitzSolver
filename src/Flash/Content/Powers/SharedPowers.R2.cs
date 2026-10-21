using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Powers;

// Powers introduced by the relic batch R2. Each one is the existing engine's behaviour for the
// native power: the mirror that implements it is named on the type.

/// <summary>
/// Native <c>SelfFormingClayPower</c>: when its owner's block is cleared, gains block equal to the
/// power amount and removes itself (<c>CorePowerSupport.TriggerAfterBlockCleared</c>).
/// </summary>
[FlashContent("SELF_FORMING_CLAY_POWER")]
public sealed class SelfFormingClayPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterBlockCleared(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        FlashKernel.GainBlock(ref x, self.Owner, amount, FlashValueProps.Unpowered, fromCard: false);
        if (x.NeedsChoice)
            return;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}
