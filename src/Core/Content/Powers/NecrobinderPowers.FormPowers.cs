using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// Native <c>DanseMacabrePower</c>: when its owner plays a card whose resolved energy cost is at
/// least the power's <c>Energy</c> var (2), gain its amount of unpowered block
/// (<c>BeforeCardPlayedMirrors.HandleDanseMacabrePower</c>).
/// </summary>
[BlitzContent("DANSE_MACABRE_POWER")]
public sealed class DanseMacabrePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        if (x.CurrentPlayEnergyValue < x.R.PowerVar(Id, 0))
            return;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.B.Powers[self.Index].Amount,
            BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// Native <c>ReaperFormPower</c>: after a powered attack by its owner (or the owner's pet) deals
/// damage, apply Doom to the target equal to TotalDamage × amount, with the power owner as applier
/// and no card source (<c>AfterDamageGivenMirrors.HandleReaperFormPower</c>).
/// </summary>
[BlitzContent("REAPER_FORM_POWER")]
public sealed class ReaperFormPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterDamageGiven(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result,
        byte dealer)
    {
        if (dealer != BlitzLimits.PlayerOwner && dealer != BlitzLimits.OstyOwner)
            return;
        if (!result.Props.IsPoweredAttack())
            return;
        int total = result.BlockedDamage + result.UnblockedDamage;
        if (total <= 0)
            return;
        if (!BlitzRegistry.TryPower("DOOM_POWER", out ushort doom))
            throw new BlitzUnsupportedException("Doom 没有 Blitz 定义。");
        BlitzKernel.ApplyPowerWithoutCardSource(ref x, result.Receiver, doom,
            total * x.B.Powers[self.Index].Amount, self.Owner);
    }
}
