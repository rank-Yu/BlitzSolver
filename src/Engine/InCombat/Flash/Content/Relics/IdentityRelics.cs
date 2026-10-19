namespace CombatSolver.Engine.InCombat.Flash.Content.Relics;

// Relics whose existing-engine state is a per-card ledger and/or a per-temporary-power type set.

/// <summary>
/// Native <c>Vambrace</c>: the first card-sourced block each combat is doubled once; the triggering
/// card's identity lives only until that card's AfterCardPlayed. Existing engine:
/// <c>AfterModifyingBlockAmountMirrors.HandleVambrace</c>,
/// <c>ModifyBlockMultiplicativeMirrors.HandleVambrace</c> and
/// <c>AfterCardPlayedMirrors.HandleVambrace</c> (VambracePredictionState).
/// </summary>
[FlashContent("VAMBRACE")]
public sealed class Vambrace : FlashRelicDef
{
    public override bool IsIdentityContent => true;

    public override decimal ModifyBlockMultiplicative(ref FlashExec x, FlashListener self, in FlashBlockContext block,
        decimal amount)
    {
        if (x.B.Relics[self.Index].Counter != 0
            || (block.Props & FlashValueProps.Move) == 0
            || !block.HasCardSource)
        {
            return 1m;
        }
        if (x.VambraceTriggerActive && x.VambraceTriggerSerial != x.CardPowerSourceSerial)
            return 1m;
        return 2m;
    }

    public override void AfterModifyingBlockAmount(ref FlashExec x, FlashListener self, decimal modified,
        in FlashBlockContext block)
    {
        if (modified <= 0m || !block.HasCardSource)
            return;
        x.VambraceTriggerActive = true;
        x.VambraceTriggerPosition = x.PlayPosition;
        x.VambraceTriggerSerial = x.CardPowerSourceSerial;
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.B.Relics[self.Index].Counter != 0 || !x.VambraceTriggerActive)
            return;
        int relative = play.Position - FlashPiles.Start(x.B, FlashPile.Play);
        if (relative != x.VambraceTriggerPosition || x.VambraceTriggerSerial != x.LastCardPowerSourceSerial)
            return;
        x.B.Relics[self.Index].Counter = 1;
    }
}

/// <summary>
/// Native <c>UnsettlingLamp</c>: the first debuff a player's card applies doubles once per instance of
/// that temporary power's internal type. The triggering card lives only inside the card's power
/// application scope; the finished latch is the relic Counter. Existing engine:
/// <c>SimulatedCombatState.ObserveUnsettlingLamp/ModifyPowerAmountForUnsettlingLamp/
/// CompleteCardPowerApplication</c>.
/// </summary>
[FlashContent("UNSETTLING_LAMP")]
public sealed class UnsettlingLamp : FlashRelicDef
{
    public override bool IsIdentityContent => true;

    public override bool IsUnsettlingLamp => true;

    public override void BeforePowerAmountChanged(ref FlashExec x, FlashListener self, ushort powerId, int amount,
        byte target, byte applier, int cardSourceSerial)
    {
        if (cardSourceSerial == 0
            || x.LampTriggerSerial != 0
            || x.B.Relics[self.Index].Counter != 0
            || applier != self.Owner
            || FlashKernel.IsPlayerSide(target))
        {
            return;
        }
        FlashPowerDef def = FlashRegistry.Power(powerId);
        if (!def.IsVisible || def.TypeForAmount(amount) != FlashPowerType.Debuff)
            return;
        if (FlashRegistry.TryPower("ARTIFACT_POWER", out ushort artifact)
            && FlashKernel.GetAmount(x.B, target, artifact) > 0)
        {
            return;
        }
        x.LampTriggerSerial = cardSourceSerial;
        if (!def.IsTemporaryPower || def.InternallyAppliedPowerId is not { } internalId)
            return;
        if (!FlashRegistry.TryPower(internalId, out ushort internalPower))
            throw new FlashUnsupportedException($"临时 Power {def.NativeId} 的内部 Power {internalId} 没有定义。");
        for (int index = 0; index < x.LampInternalTypeCount; index++)
        {
            if (x.LampInternalTypes[index] == internalPower)
                return;
        }
        if (x.LampInternalTypeCount >= FlashLimits.LampInternalPowerTypes)
            throw new FlashCapacityException("LampInternalPowerTypes", FlashLimits.LampInternalPowerTypes);
        x.LampInternalTypes[x.LampInternalTypeCount++] = internalPower;
    }

    public override decimal ModifyPowerAmountGivenMultiplicative(ref FlashExec x, FlashListener self,
        ushort powerId, byte giver, byte target, decimal amount, int cardSourceSerial)
    {
        if (cardSourceSerial == 0
            || x.LampTriggerSerial == 0
            || x.LampTriggerSerial != cardSourceSerial)
        {
            return 1m;
        }
        for (int index = 0; index < x.LampInternalTypeCount; index++)
        {
            if (x.LampInternalTypes[index] == powerId)
                return 1m;
        }
        if (FlashRegistry.Power(powerId).TypeForAmount((int)amount) != FlashPowerType.Debuff)
            return 1m;
        return 2m;
    }
}
