using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// Native <c>HardToKillPower</c> (Exoskeleton, nine stacks applied in <c>AfterAddedToRoom</c>): the
/// owner's damage taken from one hit is capped at the power amount
/// (<c>ModifyDamageCap</c>). No private slot.
/// </summary>
[BlitzContent("HARD_TO_KILL_POWER")]
public sealed class HardToKillPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyDamageCap(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage)
        => damage.Target == self.Owner ? x.B.Powers[self.Index].Amount : decimal.MaxValue;
}

/// <summary>
/// Native <c>PersonalHivePower</c> (Entomancer, one stack applied in <c>AfterAddedToRoom</c>): a
/// powered attack that hits the owner puts its amount of Dazed into the player's draw pile at random
/// (<c>AfterDamageReceivedMirrors.HandlePersonalHivePower</c>). No private slot.
/// </summary>
[BlitzContent("PERSONAL_HIVE_POWER")]
public sealed class PersonalHivePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DAZED", 0)];

    public override void AfterDamageReceived(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || !result.Props.IsPoweredAttack())
            return;
        if (dealer != BlitzLimits.PlayerOwner && dealer != BlitzLimits.OstyOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        for (int index = 0; index < amount; index++)
        {
            BlitzKernel.AddGeneratedCard(ref x,
                BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Dazed>()),
                BlitzPile.Draw, BlitzPilePosition.Random, creator: BlitzLimits.NoOwner);
        }
    }
}

/// <summary>
/// Native <c>HatchPower</c> (ToughEgg, one or two stacks applied on entrance): loses one stack at the
/// end of its side (<c>EndTurnPowerSupport.TriggerRegular</c> case HatchPower). The count only gates
/// the hatch animation; the move state machine already starts at HATCH_MOVE.
/// </summary>
[BlitzContent("HATCH_POWER")]
public sealed class HatchPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (!participates)
            return;
        BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, x.B.Powers[self.Index].Amount - 1);
    }
}

/// <summary>
/// Native <c>BurrowedPower</c> (Tunneler's BURROW_MOVE): the owner keeps its block at its side start
/// (<c>ShouldClearBlock</c>) and when that block is broken it removes this power, loses its remaining
/// block as unpowered block damage and is forced to the synthetic STUNNED with <c>BITE_MOVE</c> as
/// the follow-up (<c>TriggeredPowerSupport.CompensateBurrow</c>). The private <c>_isStunned</c> flag
/// is animation-only.
/// </summary>
[BlitzContent("BURROWED_POWER")]
public sealed class BurrowedPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override bool ShouldClearBlock(ref BlitzExec x, BlitzListener self, byte owner)
        => owner != self.Owner;

    // TriggeredPowerSupport.CompensateBurrow runs from CompensateHistorySince after the effect's
    // damage pass has applied the enemy death powers: a hit that kills the Tunneler removes
    // BurrowedPower first, so a lethal hit never forces the stun. This is not the native
    // AfterBlockBroken hook (which the existing engine only mirrors for HandDrill), and it must
    // not see block breaks from the enemy turn or from a phase-two evoke.
    public override bool CompensatesBlockBreakAfterDamage => true;

    public override void AfterBlockBrokenCompensated(ref BlitzExec x, BlitzListener self, byte target, byte breaker)
    {
        if (target != self.Owner || x.B.Powers[self.Index].Amount <= 0)
            return;
        if (x.B.Creature(self.Owner).IsDead)
            return;
        BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
        int block = x.B.Creature(self.Owner).Block;
        if (block > 0)
            BlitzKernel.DamageBlock(ref x.B.Creature(self.Owner), block, BlitzValueProps.Move);
        BlitzKernel.ForceStunnedMove(ref x, self.Owner,
            BlitzKernel.MonsterRules(ref x, self.Owner).NodeIndex("BITE_MOVE"));
    }
}

/// <summary>
/// Native <c>SandpitPower</c> (TheInsatiable's LIQUIFY_GROUND): a target-marking debuff. The existing
/// engine's compensation runs it in <c>TurnStartPowerSupport.TriggerAfterSideTurnStart</c> on the
/// enemy side (<c>S/../Prediction/TurnStartPowerSupport.cs:449</c>): decrement the exact stack, and
/// when it reaches zero force-kill the recorded target through the damage pipeline while both the
/// owner and the target are alive. The native screen/music hooks are out of scope.
/// </summary>
[BlitzContent("SANDPIT_POWER")]
public sealed class SandpitPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    // SimulatedCombatState.ApplyTargeted branches on PowerInstanceType.Instanced: every LIQUIFY_GROUND
    // adds a fresh native Sandpit instance instead of stacking into the previous one.
    public override bool IsInstanced => true;

    public override void GlobalAfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (side != BlitzSide.Enemy || !participates)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        if (power.Amount <= 0)
            return;
        byte target = power.Target;
        int remaining = power.Amount - 1;
        BlitzKernel.SetPowerAmountAt(ref x.B, self.Index, remaining);
        if (remaining > 0)
            return;
        if (target == BlitzLimits.NoOwner)
            throw new BlitzUnsupportedException("流沙坑归零时没有目标。");
        if (!x.B.Creature(self.Owner).IsAlive || !x.B.Creature(target).IsAlive)
            return;
        Span<byte> victims = stackalloc byte[1];
        victims[0] = target;
        BlitzKernel.Kill(ref x, victims, force: true);
    }
}
