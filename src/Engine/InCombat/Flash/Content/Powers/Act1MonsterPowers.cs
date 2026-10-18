namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Powers the first-act (Overgrowth) monsters apply to the player or carry themselves. Each mirrors
// the existing engine's implementation, not the decompiled original where the two differ.

/// <summary>
/// Native <c>ShrinkPower</c>: the Shrunk creature's powered attacks deal less damage. Applied with
/// amount -1 by ShrinkerBeetle (<c>MonsterMoveEffects</c> "ShrinkerBeetle", "SHRINKER_MOVE",
/// <c>combat.Apply&lt;ShrinkPower&gt;</c>), and decremented at the end of the owner's side
/// (<c>EndTurnPowerSupport.TriggerRegular</c> case <c>ShrinkPower</c>). The existing engine skips
/// powers whose amount is not positive, so the -1 form never ticks down. The engine registers no
/// mirror for this power's damage multiplier, so it calls the native
/// <c>ModifyDamageMultiplicative</c> through <c>ModifyDamageMirrors.InvokeOriginalMultiplicative</c>:
/// <c>(100 - DamageDecrease) / 100</c> against the power owner's powered attacks. Removed when its
/// applier dies (<c>CorePowerSupport.ApplyEnemyDeathPowers</c>).
/// </summary>
[FlashContent("SHRINK_POWER")]
public sealed class ShrinkPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool AllowNegative => true;

    public override IReadOnlyList<string> VarNames => ["DamageDecrease"];

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self, in FlashDamageContext damage, decimal amount)
    {
        if (damage.Dealer != self.Owner || !damage.Props.IsPoweredAttack())
            return 1m;
        return (100m - x.R.PowerVar(Id, 0)) / 100m;
    }

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }

    // CorePowerSupport.ApplyEnemyDeathPowers: SetAmount to 0 when its applier died.
    public override void ApplierDied(ref FlashExec x, FlashListener self)
        => FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
}

/// <summary>
/// Native <c>ConstrictPower</c>: applied to the player by SlitheringStrangler
/// (<c>MonsterMoveEffects</c> "SlitheringStrangler", "CONSTRICT", <c>combat.Apply&lt;ConstrictPower&gt;</c>).
/// At the end of the owner's side it damages the owner for its amount as Unpowered damage with the
/// owner as dealer (<c>CorePowerSupport.TriggerPlayerRegularSideTurnEndEffects</c>).
/// </summary>
[FlashContent("CONSTRICT_POWER")]
public sealed class ConstrictPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void PlayerSideTurnEnd(ref FlashExec x, FlashListener self)
    {
        if (x.B.Creature(self.Owner).IsDead)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = self.Owner;
        FlashKernel.Damage(ref x, targets, amount, FlashValueProps.Unpowered, self.Owner, hasCardSource: false, default);
    }

    // CorePowerSupport.ApplyEnemyDeathPowers: SetAmount to 0 when its applier died.
    public override void ApplierDied(ref FlashExec x, FlashListener self)
        => FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
}

/// <summary>
/// Native <c>TerritorialPower</c> as mirrored by the existing engine: at the end of its owner's side
/// it applies Strength equal to its amount to the owner
/// (<c>EndTurnPowerSupport.TriggerRegular</c> case <c>TerritorialPower</c>). Byrdonis carries one.
/// </summary>
[FlashContent("TERRITORIAL_POWER")]
public sealed class TerritorialPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<StrengthPower>(),
            x.B.Powers[self.Index].Amount, self.Owner);
    }
}

/// <summary>
/// Native <c>ArtifactPower</c>: carried by CubexConstruct (one stack at combat start). The kernel's
/// <c>ConsumeArtifact</c> already negates one debuff application and decrements this power; the
/// definition only needs to exist so the root can be imported and the power can be stacked.
/// </summary>
[FlashContent("ARTIFACT_POWER")]
public sealed class ArtifactPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;
}

/// <summary>
/// Native <c>SlipperyPower</c> (Inklet, one stack applied in <c>AfterAddedToRoom</c>): the owner's
/// HP loss of at least 1 is capped at 1 (<c>ModifyHpLostAfterOsty</c>) and the stack decrements
/// after unblocked damage (<c>AfterDamageReceived</c>). The existing engine keeps the effective
/// amount in a <c>PowerAmountPredictionState</c> instead of the cloned model
/// (<c>ModifyHpLostMirrors</c> / <c>AfterDamageReceivedMirrors.HandleSlipperyPower</c>); the kernel
/// keeps it in the power amount itself, and the bridge's synchronization applies the same value at
/// the next boundary. No private slot.
/// </summary>
[FlashContent("SLIPPERY_POWER")]
public sealed class SlipperyPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyHpLostAfterOsty(ref FlashExec x, FlashListener self, in FlashHpLossContext loss,
        decimal amount)
    {
        if (loss.Target != self.Owner || amount < 1m)
            return amount;
        return 1m;
    }

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || result.UnblockedDamage < 1)
            return;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, x.B.Powers[self.Index].Amount - 1);
    }
}

/// <summary>
/// Native <c>SlowPower</c> (BygoneEffigy, one stack applied in <c>AfterAddedToRoom</c>):
/// <c>AfterCardPlayed</c> advances the private <c>SlowAmount</c> counter and a powered attack
/// against the owner is multiplied by <c>1 + 0.1 * counter</c>; <c>BaseSideTurnStart</c> resets the
/// counter when the owner's side starts. The existing engine stores the counter in a
/// <c>CounterPredictionState</c> (<c>AfterCardPlayedMirrors.HandleSlowPower</c>,
/// <c>ModifyDamageMirrors.HandleSlowPower</c>, <c>SimulatedCombatState.TriggerBaseSideTurnStart</c>).
/// Aux0 = SlowAmount counter; the bridge projects the store entry, falling back to the model's
/// <c>DynamicVars["SlowAmount"].BaseValue</c>.
/// </summary>
[FlashContent("SLOW_POWER")]
public sealed class SlowPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
        => x.B.Powers[self.Index].Aux0++;

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self,
        in FlashDamageContext damage, decimal amount)
    {
        if (damage.Target != self.Owner || !damage.Props.IsPoweredAttack())
            return 1m;
        return 1m + 0.1m * x.B.Powers[self.Index].Aux0;
    }

    public override void BaseSideTurnStart(ref FlashExec x, FlashListener self, bool decrementPlating)
        => x.B.Powers[self.Index].Aux0 = 0;
}
