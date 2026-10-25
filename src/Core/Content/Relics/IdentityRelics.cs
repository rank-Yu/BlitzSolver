using BlitzSolver.Engine;

namespace BlitzSolver.Content.Relics;

/// <summary>
/// Native <c>Vambrace</c>: the first card-sourced block each combat is doubled once; the triggering
/// card's identity lives only until that card's AfterCardPlayed. Existing engine:
/// <c>AfterModifyingBlockAmountMirrors.HandleVambrace</c>,
/// <c>ModifyBlockMultiplicativeMirrors.HandleVambrace</c> and
/// <c>AfterCardPlayedMirrors.HandleVambrace</c> (VambracePredictionState).
/// </summary>
[BlitzContent("VAMBRACE")]
public sealed class Vambrace : BlitzRelicDef
{
    public override bool IsIdentityContent => true;

    public override decimal ModifyBlockMultiplicative(ref BlitzExec x, BlitzListener self, in BlitzBlockContext block,
        decimal amount)
    {
        if (x.B.Relics[self.Index].Counter != 0
            || (block.Props & BlitzValueProps.Move) == 0
            || !block.HasCardSource)
        {
            return 1m;
        }
        if (x.VambraceTriggerActive && x.VambraceTriggerSerial != x.CardPowerSourceSerial)
            return 1m;
        return 2m;
    }

    public override void AfterModifyingBlockAmount(ref BlitzExec x, BlitzListener self, decimal modified,
        in BlitzBlockContext block)
    {
        if (modified <= 0m || !block.HasCardSource)
            return;
        x.VambraceTriggerActive = true;
        x.VambraceTriggerPosition = x.PlayPosition;
        x.VambraceTriggerSerial = x.CardPowerSourceSerial;
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (x.B.Relics[self.Index].Counter != 0 || !x.VambraceTriggerActive)
            return;
        int relative = play.Position - BlitzPiles.Start(x.B, BlitzPile.Play);
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
[BlitzContent("UNSETTLING_LAMP")]
public sealed class UnsettlingLamp : BlitzRelicDef
{
    public override bool IsIdentityContent => true;

    public override bool IsUnsettlingLamp => true;

    public override void BeforePowerAmountChanged(ref BlitzExec x, BlitzListener self, ushort powerId, int amount,
        byte target, byte applier, int cardSourceSerial)
    {
        if (cardSourceSerial == 0
            || x.LampTriggerSerial != 0
            || x.B.Relics[self.Index].Counter != 0
            || applier != self.Owner
            || BlitzKernel.IsPlayerSide(target))
        {
            return;
        }
        BlitzPowerDef def = BlitzRegistry.Power(powerId);
        if (!def.IsVisible || def.TypeForAmount(amount) != BlitzPowerType.Debuff)
            return;
        if (BlitzRegistry.TryPower("ARTIFACT_POWER", out ushort artifact)
            && BlitzKernel.GetAmount(x.B, target, artifact) > 0)
        {
            return;
        }
        x.LampTriggerSerial = cardSourceSerial;
        if (!def.IsTemporaryPower || def.InternallyAppliedPowerId is not { } internalId)
            return;
        if (!BlitzRegistry.TryPower(internalId, out ushort internalPower))
            throw new BlitzUnsupportedException($"临时 Power {def.NativeId} 的内部 Power {internalId} 没有定义。");
        for (int index = 0; index < x.LampInternalTypeCount; index++)
        {
            if (x.LampInternalTypes[index] == internalPower)
                return;
        }
        if (x.LampInternalTypeCount >= BlitzLimits.LampInternalPowerTypes)
            throw new BlitzCapacityException("LampInternalPowerTypes", BlitzLimits.LampInternalPowerTypes);
        x.LampInternalTypes[x.LampInternalTypeCount++] = internalPower;
    }

    public override decimal ModifyPowerAmountGivenMultiplicative(ref BlitzExec x, BlitzListener self,
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
        if (BlitzRegistry.Power(powerId).TypeForAmount((int)amount) != BlitzPowerType.Debuff)
            return 1m;
        return 2m;
    }
}
