using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Powers;

/// <summary>
/// Native <c>SelfFormingClayPower</c>: when its owner's block is cleared, gains block equal to the
/// power amount and removes itself (<c>CorePowerSupport.TriggerAfterBlockCleared</c>).
/// </summary>
[ContentId("SELF_FORMING_CLAY_POWER")]
public sealed class SelfFormingClayPower : PowerDef
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterBlockCleared(ref Exec x, Listener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        Kernel.GainBlock(ref x, self.Owner, amount, ValueProps.Unpowered, fromCard: false);
        if (x.NeedsChoice)
            return;
        Kernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}
