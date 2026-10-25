using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// Native <c>RitualPower</c>: at the end of its owner's side it applies Strength equal to its amount
/// to the owner (<c>EndTurnPowerSupport.TriggerRegular</c> case <c>RitualPower when ownerParticipates</c>
/// → <c>PersistentPowerSupport.TriggerRitual</c>), unless the enemy has just gained it:
/// <c>ConsumeRitualApplicationDelay</c> clears <c>_wasJustAppliedByEnemy</c> and skips that trigger.
/// </summary>
[BlitzContent("RITUAL_POWER")]
public sealed class RitualPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool MarksJustAppliedByEnemy => true;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (!participates)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        if (power.Amount <= 0)
            return;
        if ((power.Flags & BlitzPowerFlags.WasJustAppliedByEnemy) != 0)
        {
            power.Flags &= ~BlitzPowerFlags.WasJustAppliedByEnemy;
            return;
        }
        BlitzKernel.ApplyPower(ref x, self.Owner, BlitzRegistry.PowerId<StrengthPower>(), power.Amount, self.Owner);
    }
}

/// <summary>
/// Native <c>PlatingPower</c>: at its owner's side start it loses <c>Decrement</c> layers from round
/// two on (<c>SimulatedCombatState.TriggerBaseSideTurnStart</c>, <c>decrementPlating</c>) and at the end
/// of its owner's side it gains that many points of Unpowered block
/// (<c>CorePowerSupport.TriggerEnemySideTurnEndEffects</c>, case <c>PlatingPower</c>). The first-turn
/// block is applied before the root and imported. For a single player <c>Decrement</c> is 1; it is read
/// from the compiled power variables (`PlatingPower.AfterApplied` sets it to the player count).
/// </summary>
[BlitzContent("PLATING_POWER")]
public sealed class PlatingPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override IReadOnlyList<string> VarNames => ["Decrement"];

    public override void BaseSideTurnStart(ref BlitzExec x, BlitzListener self, bool decrementPlating)
    {
        if (!decrementPlating)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - (int)x.R.PowerVar(Id, 0));
    }

    public override void EnemySideTurnEnd(ref BlitzExec x, BlitzListener self)
    {
        // EndTurnPowerSupport.TriggerVeryEarly runs before the per-enemy Plating block gain and removes
        // Plating when an enemy's AsleepPower amount is already <= 1 (its last decrement happens later,
        // in TriggerRegular). This is that removal at the same point in the sequence.
        if (BlitzRegistry.TryPower("ASLEEP_POWER", out ushort asleep)
            && BlitzKernel.GetAmount(x.B, self.Owner, asleep) is > 0 and <= 1)
        {
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
            return;
        }
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        BlitzKernel.GainBlock(ref x, self.Owner, amount, BlitzValueProps.Unpowered, fromCard: false);
    }

    // BeforeSideTurnEndMirrors.HandlePlatingPower (Early pass): a participating owner gains its amount
    // as Unpowered block — the player-side Plating at the player's turn end.
    public override void BeforeSideTurnEndEarly(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        if (BlitzKernel.Participates(participants, self.Owner))
            BlitzKernel.GainBlock(ref x, self.Owner, x.B.Powers[self.Index].Amount, BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// Native <c>SuckPower</c> (FossilStalker, three stacks applied in <c>AfterAddedToRoom</c>): after its
/// owner's powered attack, the owner gains <c>Amount</c> Strength once per hit list that dealt
/// unblocked damage to the other side (<c>AfterAttackMirrors.HandleSuckPower</c>). The single-player
/// target side has one creature, so each unblocked hit is one hit list.
/// Aux0 = unblocked hits counted in the current attack; the bridge projects no slot because the
/// existing engine keeps no state for this power and the counter is zero at every action boundary.
/// </summary>
[BlitzContent("SUCK_POWER")]
public sealed class SuckPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    // AfterDamageGiven runs for every damage result, including a hit that kills the target; the
    // native AfterAttack counts those hit lists as well.
    public override void AfterDamageGiven(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (dealer != self.Owner || result.UnblockedDamage <= 0 || !result.Props.IsPoweredAttack())
            return;
        x.B.Powers[self.Index].Aux0++;
    }

    public override void AfterAttack(ref BlitzExec x, BlitzListener self, byte attacker)
    {
        if (attacker != self.Owner)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        int hits = power.Aux0;
        if (hits == 0)
            return;
        int amount = power.Amount;
        power.Aux0 = 0;
        BlitzKernel.ApplyPower(ref x, self.Owner, BlitzRegistry.PowerId<StrengthPower>(), amount * hits, self.Owner);
    }
}

/// <summary>
/// Native <c>HardenedShellPower</c> (SkulkingColony, 20 stacks applied in <c>AfterAddedToRoom</c>):
/// each hit against the owner loses at most the shell's remaining points for the turn
/// (<c>ModifyHpLostMirrors.HandleHardenedShellPower</c> through <c>HardenedShellPredictionState</c>),
/// unblocked damage is accumulated (<c>AfterDamageReceivedMirrors.HandleHardenedShellPower</c>), and
/// the accumulator resets at every side start (<c>HardenedShellPower.BeforeSideTurnStart</c>).
/// Aux0 = damageReceivedThisTurn; the bridge projects it from the prediction state, falling back to
/// the model's live internal data.
/// </summary>
[BlitzContent("HARDENED_SHELL_POWER")]
public sealed class HardenedShellPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyHpLostBeforeOstyLate(ref BlitzExec x, BlitzListener self, in BlitzHpLossContext loss,
        decimal amount)
    {
        if (loss.Target != self.Owner || amount == 0m)
            return amount;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        return Math.Min(amount, power.Amount - power.Aux0);
    }

    public override void AfterDamageReceived(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || result.WasFullyBlocked)
            return;
        x.B.Powers[self.Index].Aux0 += result.UnblockedDamage;
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, bool participates)
        => x.B.Powers[self.Index].Aux0 = 0;
}

/// <summary>
/// Native <c>ThornsPower</c>: before its owner receives a powered attack (or an Omnislice hit), damages the
/// dealer for its amount as Unpowered damage with the owner as dealer
/// (<c>M/Hooks/Damage/BeforeDamageReceivedMirrors.HandleThornsPower</c>).
/// </summary>
[BlitzContent("THORNS_POWER")]
public sealed class ThornsPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeDamageReceived(ref BlitzExec x, BlitzListener self, byte target, decimal amount,
        BlitzValueProps props, byte dealer, bool hasCardSource, in BlitzCard cardSource)
    {
        if (target != self.Owner || dealer == BlitzLimits.NoOwner)
            return;
        bool omnislice = hasCardSource && BlitzRegistry.TryCard("OMNISLICE", out ushort omnisliceId) && cardSource.Id == omnisliceId;
        if (!props.IsPoweredAttack() && !omnislice)
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = dealer;
        BlitzKernel.Damage(ref x, targets, x.B.Powers[self.Index].Amount,
            BlitzValueProps.Unpowered | BlitzValueProps.SkipHurtAnim, self.Owner, hasCardSource: false, default);
    }
}
