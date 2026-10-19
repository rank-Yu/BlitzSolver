namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Powers the second-act (Hive) monsters carry or apply. Each mirrors the existing engine's
// implementation, not the decompiled original where the two differ.

/// <summary>
/// Native <c>HardToKillPower</c> (Exoskeleton, nine stacks applied in <c>AfterAddedToRoom</c>): the
/// owner's damage taken from one hit is capped at the power amount
/// (<c>ModifyDamageCap</c>). No private slot.
/// </summary>
[FlashContent("HARD_TO_KILL_POWER")]
public sealed class HardToKillPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyDamageCap(ref FlashExec x, FlashListener self, in FlashDamageContext damage)
        => damage.Target == self.Owner ? x.B.Powers[self.Index].Amount : decimal.MaxValue;
}

/// <summary>
/// Native <c>PersonalHivePower</c> (Entomancer, one stack applied in <c>AfterAddedToRoom</c>): a
/// powered attack that hits the owner puts its amount of Dazed into the player's draw pile at random
/// (<c>AfterDamageReceivedMirrors.HandlePersonalHivePower</c>). No private slot.
/// </summary>
[FlashContent("PERSONAL_HIVE_POWER")]
public sealed class PersonalHivePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DAZED", 0)];

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || !result.Props.IsPoweredAttack())
            return;
        if (dealer != FlashLimits.PlayerOwner && dealer != FlashLimits.OstyOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        for (int index = 0; index < amount; index++)
        {
            FlashKernel.AddGeneratedCard(ref x,
                FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Dazed>()),
                FlashPile.Draw, FlashPilePosition.Random, creator: FlashLimits.NoOwner);
        }
    }
}

/// <summary>
/// Native <c>HatchPower</c> (ToughEgg, one or two stacks applied on entrance): loses one stack at the
/// end of its side (<c>EndTurnPowerSupport.TriggerRegular</c> case HatchPower). The count only gates
/// the hatch animation; the move state machine already starts at HATCH_MOVE.
/// </summary>
[FlashContent("HATCH_POWER")]
public sealed class HatchPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, x.B.Powers[self.Index].Amount - 1);
    }
}

/// <summary>
/// Native <c>BurrowedPower</c> (Tunneler's BURROW_MOVE): the owner keeps its block at its side start
/// (<c>ShouldClearBlock</c>) and when that block is broken it removes this power, loses its remaining
/// block as unpowered block damage and is forced to the synthetic STUNNED with <c>BITE_MOVE</c> as
/// the follow-up (<c>TriggeredPowerSupport.CompensateBurrow</c>). The private <c>_isStunned</c> flag
/// is animation-only.
/// </summary>
[FlashContent("BURROWED_POWER")]
public sealed class BurrowedPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override bool ShouldClearBlock(ref FlashExec x, FlashListener self, byte owner)
        => owner != self.Owner;

    public override void AfterBlockBroken(ref FlashExec x, FlashListener self, byte target, byte breaker)
    {
        if (target != self.Owner || x.B.Powers[self.Index].Amount <= 0)
            return;
        // TriggeredPowerSupport.CompensateBurrow runs after the damage pass has applied the enemy
        // death powers: a hit that kills the Tunneler removes BurrowedPower first, so a lethal hit
        // never forces the stun. AfterBlockBroken runs before that deferred pass, so skip the
        // still-dead owner here.
        if (x.B.Creature(self.Owner).IsDead)
            return;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
        int block = x.B.Creature(self.Owner).Block;
        if (block > 0)
            FlashKernel.DamageBlock(ref x.B.Creature(self.Owner), block, FlashValueProps.Move);
        FlashKernel.ForceStunnedMove(ref x, self.Owner,
            FlashKernel.MonsterRules(ref x, self.Owner).NodeIndex("BITE_MOVE"));
    }
}