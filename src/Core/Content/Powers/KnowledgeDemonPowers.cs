using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>Native <c>DisintegrationPower</c>: at its owner's side turn end late, take its amount as
/// unpowered damage dealt by itself.</summary>
[BlitzContent("DISINTEGRATION_POWER")]
public sealed class DisintegrationPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterSideTurnEndLate(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (!participates || x.B.Powers[self.Index].Amount <= 0 || !x.B.Creature(self.Owner).IsAlive)
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = self.Owner;
        BlitzKernel.Damage(ref x, targets, x.B.Powers[self.Index].Amount,
            BlitzValueProps.Unpowered, self.Owner, hasCardSource: false, default);
    }
}

/// <summary>Native <c>MindRotPower</c>: the owner draws <c>max(0, count - amount)</c> cards.</summary>
[BlitzContent("MIND_ROT_POWER")]
public sealed class MindRotPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
        => self.Owner == BlitzLimits.PlayerOwner
            ? Math.Max(0m, amount - x.B.Powers[self.Index].Amount)
            : amount;
}

/// <summary>
/// Native <c>SlothPower</c>: the owner may play at most <c>Amount</c> cards each side turn. The
/// native private counter increments in <c>BeforeCardPlayed</c> and resets at the owner's
/// <c>BeforeSideTurnStart</c>; it is read by <c>ShouldPlay</c>.
/// </summary>
[BlitzContent("SLOTH_POWER")]
public sealed class SlothPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool ShouldPlay(in BlitzBattle battle, BlitzRules rules, BlitzListener self, in BlitzCard card)
        => battle.Powers[self.Index].Aux0 < battle.Powers[self.Index].Amount;

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
        => x.B.Powers[self.Index].Aux0++;

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, bool participates)
    {
        if (participates)
            x.B.Powers[self.Index].Aux0 = 0;
    }
}

/// <summary>Native <c>WasteAwayPower</c>: the owner's maximum energy is reduced by its amount.</summary>
[BlitzContent("WASTE_AWAY_POWER")]
public sealed class WasteAwayPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => self.Owner == BlitzLimits.PlayerOwner
            ? amount - x.B.Powers[self.Index].Amount
            : amount;
}
