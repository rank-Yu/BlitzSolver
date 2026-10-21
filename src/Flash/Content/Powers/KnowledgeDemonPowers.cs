using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Powers;

/// <summary>Native <c>DisintegrationPower</c>: at its owner's side turn end late, take its amount as
/// unpowered damage dealt by itself.</summary>
[FlashContent("DISINTEGRATION_POWER")]
public sealed class DisintegrationPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterSideTurnEndLate(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates || x.B.Powers[self.Index].Amount <= 0 || !x.B.Creature(self.Owner).IsAlive)
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = self.Owner;
        FlashKernel.Damage(ref x, targets, x.B.Powers[self.Index].Amount,
            FlashValueProps.Unpowered, self.Owner, hasCardSource: false, default);
    }
}

/// <summary>Native <c>MindRotPower</c>: the owner draws <c>max(0, count - amount)</c> cards.</summary>
[FlashContent("MIND_ROT_POWER")]
public sealed class MindRotPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
        => self.Owner == FlashLimits.PlayerOwner
            ? Math.Max(0m, amount - x.B.Powers[self.Index].Amount)
            : amount;
}

/// <summary>
/// Native <c>SlothPower</c>: the owner may play at most <c>Amount</c> cards each side turn. The
/// native private counter increments in <c>BeforeCardPlayed</c> and resets at the owner's
/// <c>BeforeSideTurnStart</c>; it is read by <c>ShouldPlay</c>.
/// </summary>
[FlashContent("SLOTH_POWER")]
public sealed class SlothPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool ShouldPlay(in FlashBattle battle, FlashRules rules, FlashListener self, in FlashCard card)
        => battle.Powers[self.Index].Aux0 < battle.Powers[self.Index].Amount;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
        => x.B.Powers[self.Index].Aux0++;

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, bool participates)
    {
        if (participates)
            x.B.Powers[self.Index].Aux0 = 0;
    }
}

/// <summary>Native <c>WasteAwayPower</c>: the owner's maximum energy is reduced by its amount.</summary>
[FlashContent("WASTE_AWAY_POWER")]
public sealed class WasteAwayPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => self.Owner == FlashLimits.PlayerOwner
            ? amount - x.B.Powers[self.Index].Amount
            : amount;
}
