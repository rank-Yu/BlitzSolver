using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Powers;

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
/// Native <c>MinionPower</c> (KinFollower, one stack applied in <c>AfterAddedToRoom</c>): its owner is
/// a secondary enemy, so killing the primary enemy kills the remaining minions and a minion's death
/// does not count as a fatal kill. In the existing engine that is
/// <c>PowerModel.OwnerIsSecondaryEnemy</c> plus <c>ShouldOwnerDeathTriggerFatal</c>; the kernel's
/// <c>IsPrimaryEnemy</c> and the Feed fatal-kill check read it through
/// <see cref="FlashPowerDef.OwnerIsSecondaryEnemy"/>. The power is not removed when its owner dies
/// (<c>ShouldPowerBeRemovedAfterOwnerDeath</c>), which is inert because a removed enemy is no longer
/// in the listener roster; no private slot.
/// </summary>
[FlashContent("MINION_POWER")]
public sealed class MinionPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override bool OwnerIsSecondaryEnemy => true;

    // Native MinionPower.ShouldPowerBeRemovedAfterOwnerDeath: false.
    public override bool ShouldPowerBeRemovedAfterOwnerDeath => false;

    // Native MinionPower.ShouldOwnerDeathTriggerFatal: false.
    public override bool ShouldOwnerDeathTriggerFatal(in FlashBattle battle, FlashListener self) => false;
}

/// <summary>
/// Native <c>IllusionPower</c> (EyeWithTeeth and Parafright): a single buff that keeps its owner in
/// the roster after death and, on death, forces the synthetic REVIVE_MOVE. The solver's
/// <c>BeginIllusionRevive</c> (<c>SimulatedCombatState.DeathLifecycle.cs:100-119</c>) selects the
/// last logged non-revive move as the follow-up; <c>ReviveMove</c> heals the owner to its max HP and
/// clears the reviving death phase (<c>ResolveReviveMove</c>, same file). While the power is on the
/// dead owner, the solver keeps its buffs and temporary debuffs and removes other debuffs
/// (<c>RemovePowersAfterDeath</c>), exposed to the kernel as
/// <see cref="KeepsOwnerBuffsAfterDeath"/>.
/// </summary>
[FlashContent("ILLUSION_POWER")]
public sealed class IllusionPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    // ICombatPredictionCreatureSemantics.ShouldRemoveAfterDeath keeps an owner with IllusionPower.
    public override bool PreventsRemovalAfterDeath => true;

    public override bool KeepsOwnerBuffsAfterDeath => true;

    public override void AfterDeath(ref FlashExec x, FlashListener self, byte creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || creature != self.Owner)
            return;
        // The existing engine's IllusionPower death dispatch is not mirrored and records
        // MethodNotMirrored risk even though BeginIllusionRevive applies the same state.
        x.HistoryRisk = true;
        FlashKernel.BeginIllusionRevive(ref x, self.Owner);
    }
}

/// <summary>
/// Native <c>SkittishPower</c> (PhantasmalGardener, <c>SkittishAmount</c> stacks applied in
/// <c>AfterAddedToRoom</c>): the first damage result against the owner in a card attack that is
/// still unblocked makes it gain its amount as Unpowered block, at most once per player side
/// (<c>AfterAttackMirrors.HandleSkittishPower</c> through <c>SkittishPredictionState</c>). The flag
/// resets at the end of the player side (<c>SkittishPower.AfterSideTurnEnd</c>).
/// Aux0 = HasGainedBlockThisTurn; Aux1 = a result for the owner was already seen in the current
/// attack (the native hook inspects the first result for the owner only). The bridge projects Aux0
/// from the prediction state, falling back to the model's live flag.
/// </summary>
[FlashContent("SKITTISH_POWER")]
public sealed class SkittishPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    // AfterAttackMirrors.HandleSkittishPower reads the first result for the owner out of the attack
    // command; a result is only considered when the attack is a powered card attack.
    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Aux1 != 0)
            return;
        if (dealer != FlashLimits.PlayerOwner || !result.Props.IsPoweredAttack())
            return;
        power.Aux1 = 1;
        if (power.Aux0 != 0 || result.UnblockedDamage == 0)
            return;
        power.Aux0 = 1;
        FlashKernel.GainBlock(ref x, self.Owner, power.Amount, FlashValueProps.Unpowered, fromCard: false);
    }

    // The native hook runs once per attack command; this is the end-of-attack reset of the
    // "first result seen" latch.
    public override void AfterAttack(ref FlashExec x, FlashListener self, byte attacker)
        => x.B.Powers[self.Index].Aux1 = 0;

    // SkittishPower.AfterSideTurnEnd resets HasGainedBlockThisTurn when the ending side is not the
    // owner's side; the owner is an enemy, so that is the player side end.
    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (side == FlashSide.Player)
            x.B.Powers[self.Index].Aux0 = 0;
    }
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

/// <summary>
/// Native <c>AsleepPower</c> (LagavulinMatriarch, three stacks applied in <c>AfterAddedToRoom</c>):
/// unblocked damage wakes the owner — the existing engine removes Plating, forces
/// <c>STUNNED</c> with SLASH_MOVE as the follow-up and removes this power
/// (<c>AfterDamageReceivedMirrors.HandleAsleepPower</c>). At its side turn end it loses one stack
/// (<c>EndTurnPowerSupport.TriggerRegular</c>); reaching zero removes Plating. The separate
/// <c>TriggerVeryEarly</c> Plating removal is handled in <see cref="PlatingPower.EnemySideTurnEnd"/>,
/// where it precedes the Plating block gain exactly as in <c>CorePowerSupport</c>.
/// </summary>
[FlashContent("ASLEEP_POWER")]
public sealed class AsleepPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || result.UnblockedDamage == 0)
            return;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, FlashRegistry.PowerId<PlatingPower>(), 0);
        FlashKernel.ForceStunnedMove(ref x, self.Owner, FlashKernel.MonsterRules(ref x, self.Owner).NodeIndex("SLASH_MOVE"));
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        int remaining = x.B.Powers[self.Index].Amount - 1;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, remaining);
        if (remaining <= 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, FlashRegistry.PowerId<PlatingPower>(), 0);
    }
}

/// <summary>
/// Native <c>RavenousPower</c> (CorpseSlug, applied with its native starting amount): when a living
/// teammate dies, the owner is stunned with the default follow-up and gains its amount as Strength
/// (<c>DeathPowerSupport.Trigger</c>). The private <c>_isRavenous</c> flag only selects animations.
/// </summary>
[FlashContent("RAVENOUS_POWER")]
public sealed class RavenousPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterDeath(ref FlashExec x, FlashListener self, byte creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented
            || creature == self.Owner
            || !FlashKernel.IsEnemyOwner(creature)
            || x.B.Creature(self.Owner).IsDead)
        {
            return;
        }
        int amount = x.B.Powers[self.Index].Amount;
        FlashKernel.ForceStunnedMove(ref x, self.Owner, -1);
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<StrengthPower>(), amount, self.Owner);
    }
}

/// <summary>
/// Native <c>ShriekPower</c> (TerrorEel, applied with its native starting amount 70/75): unblocked
/// damage that leaves the owner at or below that amount forces <c>STUNNED</c> with TERROR_MOVE as the
/// follow-up and removes the power (<c>AfterDamageReceivedMirrors.HandleShriekPower</c>).
/// </summary>
[FlashContent("SHRIEK_POWER")]
public sealed class ShriekPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool AllowNegative => true;

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner
            || result.UnblockedDamage <= 0
            || x.B.Creature(self.Owner).CurrentHp > x.B.Powers[self.Index].Amount)
        {
            return;
        }
        FlashKernel.ForceStunnedMove(ref x, self.Owner, FlashKernel.MonsterRules(ref x, self.Owner).NodeIndex("TERROR_MOVE"));
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}

/// <summary>
/// Native <c>VigorPower</c> (TerrorEel THRASH applies 6): a powered attack by the owner deals its
/// amount as extra damage and loses the amount captured when that attack started
/// (<c>VigorPowerMirrors.BeforeAttack</c>/<c>ModifyDamageAdditive</c>/<c>AfterAttack</c>).
/// Aux0 = amount captured for the current attack; zero at every action boundary. The existing
/// engine keeps the same pair in a private <c>VigorPowerMirrors.State</c> store entry that is
/// asserted clean before a fork and claimed by the bridge projection.
/// </summary>
[FlashContent("VIGOR_POWER")]
public sealed class VigorPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    /// <summary>
    /// <c>VigorPowerMirrors.BeforeAttack</c>: the owner's powered attack from a card or a monster move
    /// captures the amount once (Aux0); <see cref="AfterAttack"/> spends exactly that amount.
    /// </summary>
    public override void BeforeAttack(ref FlashExec x, FlashListener self, byte attacker, FlashValueProps props)
    {
        if (attacker != self.Owner || !props.IsPoweredAttack())
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Amount > 0 && power.Aux0 == 0)
            power.Aux0 = power.Amount;
    }

    /// <summary>Side-effect free, like every modifier: forecasts and the scorer call it on read-only states.</summary>
    public override decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self, in FlashDamageContext damage, decimal amount)
    {
        if (damage.Dealer != self.Owner || !damage.Props.IsPoweredAttack())
            return 0m;
        int vigor = x.B.Powers[self.Index].Amount;
        return vigor > 0 ? vigor : 0m;
    }

    public override void AfterAttack(ref FlashExec x, FlashListener self, byte attacker)
    {
        if (attacker != self.Owner)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        int captured = power.Aux0;
        if (captured == 0)
            return;
        power.Aux0 = 0;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, Math.Max(0, power.Amount - captured));
    }
}
