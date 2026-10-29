using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Powers;

/// <summary>
/// Native <c>HelicalDartPower</c> (a positive <c>TemporaryDexterityPower</c>): at its owner's side
/// turn end it is removed and its amount of Dexterity is taken back
/// (<c>EndTurnPowerSupport.TriggerRegular</c>, <c>SimulatedCombatState.RetireTemporaryStat</c>).
/// </summary>
[ContentId("HELICAL_DART_POWER")]
public sealed class HelicalDartPower : PowerDef
{
    public override PowerType Type => PowerType.Buff;

    public override string? InternallyAppliedPowerId => "DEXTERITY_POWER";

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool IsTemporaryPower => true;

    public override void EndTurnRegular(ref Exec x, Listener self, Side side, bool participates)
    {
        if (!participates)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        Kernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
        if (amount == 0)
            return;
        Kernel.ApplyPower(ref x, self.Owner, Registry.PowerId<DexterityPower>(), -amount, self.Owner);
    }
}
