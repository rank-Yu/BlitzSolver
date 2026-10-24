using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Powers;

/// <summary>
/// Native <c>DanseMacabrePower</c>: when its owner plays a card whose resolved energy cost is at
/// least the power's <c>Energy</c> var (2), gain its amount of unpowered block
/// (<c>BeforeCardPlayedMirrors.HandleDanseMacabrePower</c>).
/// </summary>
[FlashContent("DANSE_MACABRE_POWER")]
public sealed class DanseMacabrePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        if (x.CurrentPlayEnergyValue < x.R.PowerVar(Id, 0))
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.B.Powers[self.Index].Amount,
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// Native <c>ReaperFormPower</c>: after a powered attack by its owner (or the owner's pet) deals
/// damage, apply Doom to the target equal to TotalDamage × amount, with the power owner as applier
/// and no card source (<c>AfterDamageGivenMirrors.HandleReaperFormPower</c>).
/// </summary>
[FlashContent("REAPER_FORM_POWER")]
public sealed class ReaperFormPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterDamageGiven(ref FlashExec x, FlashListener self, in FlashDamageResult result,
        byte dealer)
    {
        if (dealer != FlashLimits.PlayerOwner && dealer != FlashLimits.OstyOwner)
            return;
        if (!result.Props.IsPoweredAttack())
            return;
        int total = result.BlockedDamage + result.UnblockedDamage;
        if (total <= 0)
            return;
        if (!FlashRegistry.TryPower("DOOM_POWER", out ushort doom))
            throw new FlashUnsupportedException("Doom 没有 Flash 定义。");
        FlashKernel.ApplyPowerWithoutCardSource(ref x, result.Receiver, doom,
            total * x.B.Powers[self.Index].Amount, self.Owner);
    }
}
