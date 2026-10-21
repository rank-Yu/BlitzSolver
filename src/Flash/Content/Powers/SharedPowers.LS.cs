using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Powers;

/// <summary>
/// Native <c>HelicalDartPower</c> (a positive <c>TemporaryDexterityPower</c>): at its owner's side
/// turn end it is removed and its amount of Dexterity is taken back
/// (<c>EndTurnPowerSupport.TriggerRegular</c>, <c>SimulatedCombatState.RetireTemporaryStat</c>).
/// </summary>
[FlashContent("HELICAL_DART_POWER")]
public sealed class HelicalDartPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override string? InternallyAppliedPowerId => "DEXTERITY_POWER";

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsTemporaryPower => true;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
        if (amount == 0)
            return;
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<DexterityPower>(), -amount, self.Owner);
    }
}
