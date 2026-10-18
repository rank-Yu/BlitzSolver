namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

/// <summary>Native <c>StrengthPower</c>: additive damage for its owner's powered attacks.</summary>
[FlashContent("STRENGTH_POWER")]
public sealed class StrengthPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool AllowNegative => true;

    public override decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self, in FlashDamageContext damage, decimal amount)
    {
        if (self.Owner != damage.Dealer || !damage.Props.IsPoweredAttack())
            return 0m;
        return x.B.Powers[self.Index].Amount;
    }
}

/// <summary>
/// Native <c>VulnerablePower</c> as mirrored by the existing engine: multiplies powered attacks
/// against its owner by <c>DamageIncrease</c>. Paper Phrog, Cruelty and Debilitate are rejected at
/// admission until supported. Ticks at the end of the enemy side (<c>TickDurations</c>).
/// </summary>
[FlashContent("VULNERABLE_POWER")]
public sealed class VulnerablePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool UsesNativeDurationSkip => true;

    public override bool TicksAtEnemySideEnd => true;

    public override IReadOnlyList<string> VarNames => ["DamageIncrease"];

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self, in FlashDamageContext damage, decimal amount)
    {
        if (damage.Target != self.Owner || !damage.Props.IsPoweredAttack())
            return 1m;
        return x.R.PowerVar(Id, 0);
    }
}
