namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Ironclad powers whose existing-engine handling lives in M/Hooks/**. Rage and FlameBarrier are
// intentionally absent: their AfterSideTurnEnd removal has no Flash turn-phase hook, so defining
// them would make them persist past the turn in which they were applied. Rupture is absent because
// the existing engine creates an unrepresentable RupturePredictionState on every card play.

/// <summary>
/// Native <c>FeelNoPainPower</c>: <c>AfterCardExhausted</c> gains its amount as Unpowered block
/// when the exhausted card belongs to the power owner
/// (M/Hooks/Card/AfterCardExhaustedMirrors.HandleFeelNoPainPower).
/// </summary>
[FlashContent("FEEL_NO_PAIN_POWER")]
public sealed class FeelNoPainPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card, bool causedByEthereal)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        FlashKernel.GainBlock(ref x, self.Owner, x.B.Powers[self.Index].Amount,
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// Native <c>JuggernautPower</c>: after its owner gains positive block, deal its amount as Unpowered
/// damage to a random hittable enemy (M/Hooks/Block/AfterBlockGainedMirrors.HandleJuggernautPower).
/// </summary>
[FlashContent("JUGGERNAUT_POWER")]
public sealed class JuggernautPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterBlockGained(ref FlashExec x, FlashListener self, byte owner, decimal amount,
        FlashValueProps props, bool fromCard)
    {
        if (owner != self.Owner || amount <= 0m)
            return;
        Span<byte> targets = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte candidate = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (FlashKernel.IsHittable(x.B, candidate))
                targets[count++] = candidate;
        }
        // Rng.NextItem returns before drawing when the list is empty.
        if (count == 0)
            return;
        int pick = x.B.RngStream(FlashRngStream.CombatTargets).NextInt(0, count);
        FlashKernel.Damage(ref x, targets[pick..(pick + 1)], x.B.Powers[self.Index].Amount,
            FlashValueProps.Unpowered, self.Owner, hasCardSource: false, default);
    }
}

/// <summary>
/// Native <c>CrueltyPower</c>: its amount is added to the Vulnerable multiplier as a percentage by
/// <c>ModifyDamageMirrors.HandleVulnerablePower</c>, which the Flash Vulnerable definition mirrors.
/// The power itself has no hook of its own.
/// </summary>
[FlashContent("CRUELTY_POWER")]
public sealed class CrueltyPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;
}
