using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>Native <c>StrengthPower</c>: additive damage for its owner's powered attacks.</summary>
[BlitzContent("STRENGTH_POWER")]
public sealed class StrengthPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool AllowNegative => true;

    public override decimal ModifyDamageAdditive(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage, decimal amount)
    {
        if (self.Owner != damage.Dealer || !damage.Props.IsPoweredAttack())
            return 0m;
        return x.B.Powers[self.Index].Amount;
    }
}

/// <summary>
/// Native <c>VulnerablePower</c> as mirrored by the existing engine: multiplies powered attacks
/// against its owner by <c>DamageIncrease</c>, plus Paper Phrog's 0.25 and the attacker's Cruelty
/// percentage (<c>ModifyDamageMirrors.HandleVulnerablePower</c>). Debilitate is rejected at admission
/// until supported. Ticks at the end of the enemy side (<c>TickDurations</c>).
/// </summary>
[BlitzContent("VULNERABLE_POWER")]
public sealed class VulnerablePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool UsesNativeDurationSkip => true;

    public override bool TicksAtEnemySideEnd => true;

    public override IReadOnlyList<string> VarNames => ["DamageIncrease"];

    public override decimal ModifyDamageMultiplicative(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage, decimal amount)
    {
        if (damage.Target != self.Owner || !damage.Props.IsPoweredAttack())
            return 1m;
        decimal multiplier = x.R.PowerVar(Id, 0);
        // ModifyDamageMirrors.HandleVulnerablePower calls PaperPhrog.ModifyVulnerableMultiplier when the
        // attacker's player owns the relic: it adds 0.25 unless the relic owner is the target.
        if (damage.Dealer == BlitzLimits.PlayerOwner && damage.Target != BlitzLimits.PlayerOwner
            && OwnsPaperPhrog(x.B))
            multiplier += 0.25m;
        // ModifyDamageMirrors.HandleVulnerablePower adds CrueltyPower.Amount / 100m; the native
        // CrueltyPower.ModifyVulnerableMultiplier uses the same percentage divisor.
        if (damage.Dealer != BlitzLimits.NoOwner)
            multiplier += (decimal)BlitzKernel.GetAmount(x.B, damage.Dealer, BlitzRegistry.PowerId<CrueltyPower>()) / 100m;
        // ModifyDamageMirrors.HandleVulnerablePower applies DebilitatePower on the vulnerable target:
        // multiplier += multiplier - 1.
        if (BlitzRegistry.TryPower("DEBILITATE_POWER", out ushort debilitate)
            && BlitzKernel.GetAmount(x.B, self.Owner, debilitate) > 0)
        {
            multiplier += multiplier - 1m;
        }
        return multiplier;
    }

    /// <summary><c>Player.GetRelic&lt;PaperPhrog&gt;()</c> for the single player.</summary>
    private static bool OwnsPaperPhrog(in BlitzBattle battle)
    {
        if (!BlitzRegistry.TryRelic("PAPER_PHROG", out ushort id))
            return false;
        for (int index = 0; index < battle.RelicCount; index++)
        {
            ref readonly BlitzRelic relic = ref battle.Relics[index];
            if (relic.Id == id)
                return true;
        }
        return false;
    }
}

/// <summary>
/// Native <c>WeakPower</c>: multiplies its owner's powered attacks by <c>DamageDecrease</c>.
/// Paper Krane and Debilitate are rejected at admission until supported. Ticks at the end of the
/// enemy side (<c>TickDurations</c>) and skips the first player-side duration tick.
/// </summary>
[BlitzContent("WEAK_POWER")]
public sealed class WeakPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool UsesNativeDurationSkip => true;

    public override bool TicksAtEnemySideEnd => true;

    public override IReadOnlyList<string> VarNames => ["DamageDecrease"];

    public override decimal ModifyDamageMultiplicative(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage, decimal amount)
    {
        if (damage.Dealer != self.Owner || !damage.Props.IsPoweredAttack())
            return 1m;
        decimal multiplier = x.R.PowerVar(Id, 0);
        // ModifyDamageMirrors.HandleWeakPower asks the target player's PaperKrane relic for the
        // weak multiplier (subtract 0.15) before folding in DebilitatePower.
        if (damage.Target == BlitzLimits.PlayerOwner
            && BlitzRegistry.TryRelic("PAPER_KRANE", out ushort paperKrane))
        {
            for (int index = 0; index < x.B.RelicCount; index++)
            {
                ref readonly BlitzRelic relic = ref x.B.Relics[index];
                if (relic.Id == paperKrane && (relic.Flags & BlitzRelicFlags.Melted) == 0)
                {
                    multiplier -= 0.15m;
                    break;
                }
            }
        }
        // ModifyDamageMirrors.HandleWeakPower folds in DebilitatePower on the attacking owner:
        // multiplier -= 1 - multiplier.
        if (BlitzRegistry.TryPower("DEBILITATE_POWER", out ushort debilitate)
            && BlitzKernel.GetAmount(x.B, self.Owner, debilitate) > 0)
        {
            multiplier -= 1m - multiplier;
        }
        return multiplier;
    }
}

/// <summary>
/// Native <c>FrailPower</c>: multiplies powered card or monster-move block gained by its owner.
/// Ticks at the end of the enemy side (<c>TickDurations</c>) and skips the first player-side tick.
/// </summary>
[BlitzContent("FRAIL_POWER")]
public sealed class FrailPower : BlitzPowerDef
{
    // FrailPower.ModifyBlockMultiplicative returns this literal; the power declares no dynamic var.
    private const decimal BlockMultiplier = 0.75m;

    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool UsesNativeDurationSkip => true;

    public override bool TicksAtEnemySideEnd => true;

    public override decimal ModifyBlockMultiplicative(ref BlitzExec x, BlitzListener self, in BlitzBlockContext block, decimal amount)
    {
        if (block.Target != self.Owner || !block.Props.IsPoweredAttack())
            return 1m;
        return BlockMultiplier;
    }
}

/// <summary>
/// Native <c>DexterityPower</c>: adds its amount to powered block gained by its owner. Positive
/// amounts are buffs; negative amounts are allowed and treated as debuffs by
/// <see cref="BlitzPowerDef.TypeForAmount"/>.
/// </summary>
[BlitzContent("DEXTERITY_POWER")]
public sealed class DexterityPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool AllowNegative => true;

    public override decimal ModifyBlockAdditive(ref BlitzExec x, BlitzListener self, in BlitzBlockContext block, decimal amount)
    {
        // Card sources are player-owned; monster-move block has no card source and must target its
        // owner directly. Both branches and the powered-block filter match
        // DexterityPower.ModifyBlockAdditive.
        if (block.HasCardSource)
        {
            if (self.Owner != BlitzLimits.PlayerOwner)
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
[BlitzContent("COLOSSUS_POWER")]
public sealed class ColossusPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool TicksAtEnemySideEnd => true;

    public override IReadOnlyList<string> VarNames => ["DamageDecrease"];

    public override decimal ModifyDamageMultiplicative(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage, decimal amount)
    {
        if (damage.Target != self.Owner || !damage.Props.IsPoweredAttack() || damage.Dealer == BlitzLimits.NoOwner)
            return 1m;
        if (BlitzKernel.GetAmount(x.B, damage.Dealer, BlitzRegistry.PowerId<VulnerablePower>()) <= 0)
            return 1m;
        return x.R.PowerVar(Id, 0);
    }
}

/// <summary>
/// Native <c>IntangiblePower</c>, called through the existing engine's original-method fallback (no mirror is
/// registered): <c>ModifyHpLostAfterOsty</c> caps its owner's HP loss of at least 1 to 1 and
/// <c>ModifyDamageCap</c> caps damage to its owner at 1. The native in-progress check reads the live combat,
/// which is in progress whenever the existing engine predicts, so it is not mirrored. Ticks at the end of the
/// enemy side (<c>CorePowerSupport.TickDurations</c>); <c>AfterModifyingHpLostAfterOsty</c> only flashes.
/// </summary>
[BlitzContent("INTANGIBLE_POWER")]
public sealed class IntangiblePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool TicksAtEnemySideEnd => true;

    public override decimal ModifyDamageCap(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage)
        => damage.Target == self.Owner ? 1m : decimal.MaxValue;

    public override decimal ModifyHpLostAfterOsty(ref BlitzExec x, BlitzListener self, in BlitzHpLossContext loss, decimal amount)
        => loss.Target == self.Owner && amount >= 1m ? 1m : amount;
}

/// <summary>
/// Native <c>NoDrawPower</c> (BattleTrance, BulletTime and similar): while its owner is a player,
/// a non-hand draw is refused (<c>ShouldDrawMirrors.HandleNoDrawPower</c>). The existing engine removes
/// it in <c>EndTurnPowerSupport.TriggerRegular</c> when the owner participates in the side turn end;
/// Blitz's <c>EndTurnRegular</c> is that pass. No private slot.
/// </summary>
[BlitzContent("NO_DRAW_POWER")]
public sealed class NoDrawPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override bool ShouldDraw(ref BlitzExec x, BlitzListener self, bool fromHandDraw)
        => fromHandDraw || self.Owner != BlitzLimits.PlayerOwner;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}

/// <summary>
/// Native <c>BufferPower</c> (LuckyTonic): the owner's after-Osty HP loss is reduced to 0 in the Late
/// pass and one stack is consumed by <c>AfterModifyingHpLostAfterOsty</c>
/// (<c>ModifyHpLostMirrors.HandleBufferPower</c> /
/// <c>AfterModifyingHpLostAfterOstyMirrors.HandleBufferPower</c>). No private slot.
/// </summary>
[BlitzContent("BUFFER_POWER")]
public sealed class BufferPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyHpLostAfterOstyLate(ref BlitzExec x, BlitzListener self, in BlitzHpLossContext loss,
        decimal amount)
        => loss.Target == self.Owner ? 0m : amount;

    public override void AfterModifyingHpLostAfterOsty(ref BlitzExec x, BlitzListener self)
        => BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, Math.Max(0, x.B.Powers[self.Index].Amount - 1));
}
