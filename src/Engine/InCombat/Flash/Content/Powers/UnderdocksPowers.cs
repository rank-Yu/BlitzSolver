namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Powers the first-act (Underdocks) monsters apply to the player or carry themselves. Each mirrors the
// existing engine's implementation, not the decompiled original where the two differ.

/// <summary>
/// Native <c>RitualPower</c>: at the end of its owner's side it applies Strength equal to its amount
/// to the owner (<c>EndTurnPowerSupport.TriggerRegular</c> case <c>RitualPower when ownerParticipates</c>
/// → <c>PersistentPowerSupport.TriggerRitual</c>), unless the enemy has just gained it:
/// <c>ConsumeRitualApplicationDelay</c> clears <c>_wasJustAppliedByEnemy</c> and skips that trigger.
/// </summary>
[FlashContent("RITUAL_POWER")]
public sealed class RitualPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool MarksJustAppliedByEnemy => true;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Amount <= 0)
            return;
        if ((power.Flags & FlashPowerFlags.WasJustAppliedByEnemy) != 0)
        {
            power.Flags &= ~FlashPowerFlags.WasJustAppliedByEnemy;
            return;
        }
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<StrengthPower>(), power.Amount, self.Owner);
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
[FlashContent("PLATING_POWER")]
public sealed class PlatingPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override IReadOnlyList<string> VarNames => ["Decrement"];

    public override void BaseSideTurnStart(ref FlashExec x, FlashListener self, bool decrementPlating)
    {
        if (!decrementPlating)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - (int)x.R.PowerVar(Id, 0));
    }

    public override void EnemySideTurnEnd(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        FlashKernel.GainBlock(ref x, self.Owner, amount, FlashValueProps.Unpowered, fromCard: false);
    }

    // BeforeSideTurnEndMirrors.HandlePlatingPower (Early pass): a participating owner gains its amount
    // as Unpowered block — the player-side Plating at the player's turn end.
    public override void BeforeSideTurnEndEarly(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (FlashKernel.Participates(participants, self.Owner))
            FlashKernel.GainBlock(ref x, self.Owner, x.B.Powers[self.Index].Amount, FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// Native <c>ThornsPower</c>: before its owner receives a powered attack (or an Omnislice hit), damages the
/// dealer for its amount as Unpowered damage with the owner as dealer
/// (<c>M/Hooks/Damage/BeforeDamageReceivedMirrors.HandleThornsPower</c>).
/// </summary>
[FlashContent("THORNS_POWER")]
public sealed class ThornsPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeDamageReceived(ref FlashExec x, FlashListener self, byte target, decimal amount,
        FlashValueProps props, byte dealer, bool hasCardSource, in FlashCard cardSource)
    {
        if (target != self.Owner || dealer == FlashLimits.NoOwner)
            return;
        bool omnislice = hasCardSource && FlashRegistry.TryCard("OMNISLICE", out ushort omnisliceId) && cardSource.Id == omnisliceId;
        if (!props.IsPoweredAttack() && !omnislice)
            return;
        Span<byte> targets = stackalloc byte[1];
        targets[0] = dealer;
        FlashKernel.Damage(ref x, targets, x.B.Powers[self.Index].Amount,
            FlashValueProps.Unpowered | FlashValueProps.SkipHurtAnim, self.Owner, hasCardSource: false, default);
    }
}
