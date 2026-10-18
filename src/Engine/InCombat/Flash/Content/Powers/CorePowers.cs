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

/// <summary>
/// Native <c>WeakPower</c>: multiplies its owner's powered attacks by <c>DamageDecrease</c>.
/// Paper Krane and Debilitate are rejected at admission until supported. Ticks at the end of the
/// enemy side (<c>TickDurations</c>) and skips the first player-side duration tick.
/// </summary>
[FlashContent("WEAK_POWER")]
public sealed class WeakPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool UsesNativeDurationSkip => true;

    public override bool TicksAtEnemySideEnd => true;

    public override IReadOnlyList<string> VarNames => ["DamageDecrease"];

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self, in FlashDamageContext damage, decimal amount)
    {
        if (damage.Dealer != self.Owner || !damage.Props.IsPoweredAttack())
            return 1m;
        return x.R.PowerVar(Id, 0);
    }
}

/// <summary>
/// Native <c>FrailPower</c>: multiplies powered card or monster-move block gained by its owner.
/// Ticks at the end of the enemy side (<c>TickDurations</c>) and skips the first player-side tick.
/// </summary>
[FlashContent("FRAIL_POWER")]
public sealed class FrailPower : FlashPowerDef
{
    // FrailPower.ModifyBlockMultiplicative returns this literal; the power declares no dynamic var.
    private const decimal BlockMultiplier = 0.75m;

    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool UsesNativeDurationSkip => true;

    public override bool TicksAtEnemySideEnd => true;

    public override decimal ModifyBlockMultiplicative(ref FlashExec x, FlashListener self, in FlashBlockContext block, decimal amount)
    {
        if (block.Target != self.Owner || !block.Props.IsPoweredAttack())
            return 1m;
        return BlockMultiplier;
    }
}

/// <summary>
/// Native <c>DexterityPower</c>: adds its amount to powered block gained by its owner. Positive
/// amounts are buffs; negative amounts are allowed and treated as debuffs by
/// <see cref="FlashPowerDef.TypeForAmount"/>.
/// </summary>
[FlashContent("DEXTERITY_POWER")]
public sealed class DexterityPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool AllowNegative => true;

    public override decimal ModifyBlockAdditive(ref FlashExec x, FlashListener self, in FlashBlockContext block, decimal amount)
    {
        // Card sources are player-owned; monster-move block has no card source and must target its
        // owner directly. Both branches and the powered-block filter match
        // DexterityPower.ModifyBlockAdditive.
        if (block.HasCardSource)
        {
            if (self.Owner != FlashLimits.PlayerOwner)
                return 0m;
        }
        else if (self.Owner != block.Target)
        {
            return 0m;
        }
        if (!block.Props.IsPoweredAttack())
            return 0m;
        return x.B.Powers[self.Index].Amount;
    }
}

/// <summary>
/// Native <c>ColossusPower</c>: a powered attack against its owner by a dealer that has Vulnerable
/// is multiplied by <c>DamageDecrease</c>. Ticks at the end of the enemy side
/// (<c>ColossusPower.AfterSideTurnEnd</c>).
/// </summary>
[FlashContent("COLOSSUS_POWER")]
public sealed class ColossusPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool TicksAtEnemySideEnd => true;

    public override IReadOnlyList<string> VarNames => ["DamageDecrease"];

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self, in FlashDamageContext damage, decimal amount)
    {
        if (damage.Target != self.Owner || !damage.Props.IsPoweredAttack() || damage.Dealer == FlashLimits.NoOwner)
            return 1m;
        if (FlashKernel.GetAmount(x.B, damage.Dealer, FlashRegistry.PowerId<VulnerablePower>()) <= 0)
            return 1m;
        return x.R.PowerVar(Id, 0);
    }
}
