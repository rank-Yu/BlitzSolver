using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// Native <c>SelfFormingClayPower</c>: when its owner's block is cleared, gains block equal to the
/// power amount and removes itself (<c>CorePowerSupport.TriggerAfterBlockCleared</c>).
/// </summary>
[BlitzContent("SELF_FORMING_CLAY_POWER")]
public sealed class SelfFormingClayPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterBlockCleared(ref BlitzExec x, BlitzListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        BlitzKernel.GainBlock(ref x, self.Owner, amount, BlitzValueProps.Unpowered, fromCard: false);
        if (x.NeedsChoice)
            return;
        BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}
