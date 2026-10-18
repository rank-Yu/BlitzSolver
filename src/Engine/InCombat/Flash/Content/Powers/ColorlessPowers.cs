namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Colorless-pool powers whose native hook is dispatched by the kernel. Existing engine paths:
// - DarkShacklesPower is a native TemporaryStrengthPower applied through
//   SimulatedCombatState.ApplyTemporaryStrengthLoss and retired by EndTurnPowerSupport.TriggerRegular
//   (src/Prediction/CardOnPlaySupport.cs, src/Search/SimulatedCombatState.cs).
// - FastenPower.ModifyBlockAdditive is invoked by HookMirrors.ModifyBlock through the native listener
//   method (src/Engine/InCombat/Mirrors/HookMirrors.cs:53); its AfterModifyingBlockAmount flash is
//   presentation-only (AfterModifyingBlockAmountMirrors registers it as ignored).
// - NoBlockPower.ModifyBlockMultiplicative is invoked through the same native fallback
//   (ModifyBlockMultiplicativeMirrors.Invoke) and its duration ticks at the enemy side end in
//   CorePowerSupport.TickDurations.
// - PrepTimePower is the PREP_TIME_POWER case of PersistentPowerSupport.TriggerOwnerAfterSideTurnStart
//   (src/Prediction/PersistentPowerSupport.cs:213).

/// <summary>
/// Native <c>DarkShacklesPower</c> (a negative <c>TemporaryStrengthPower</c>): the application path
/// also changes Strength by -amount (DarkShackles card), and the power is removed at its owner's side
/// turn end while restoring that Strength.
/// </summary>
[FlashContent("DARK_SHACKLES_POWER")]
public sealed class DarkShacklesPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsTemporaryPower => true;

    // EndTurnPowerSupport.TriggerRegular: case TemporaryStrengthPower when the owner participates.
    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.RetireTemporaryStrength(ref x, self);
    }
}

/// <summary>
/// Native <c>FastenPower</c>: additive block for its owner's powered card/monster-move block, and
/// only for card sources tagged Defend. The native <c>AfterModifyingBlockAmount</c> body is a visual
/// flash, which the existing engine registers as ignored.
/// </summary>
[FlashContent("FASTEN_POWER")]
public sealed class FastenPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyBlockAdditive(ref FlashExec x, FlashListener self, in FlashBlockContext block, decimal amount)
    {
        if (block.Target != self.Owner)
            return 0m;
        if ((block.Props & FlashValueProps.Move) == 0 || (block.Props & FlashValueProps.Unpowered) != 0)
            return 0m;
        if (block.HasCardSource
            && (x.R.Card(block.CardSource.Id, block.CardSource.Upgrade).Tags & FlashCardTag.Defend) == 0)
        {
            return 0m;
        }
        return x.B.Powers[self.Index].Amount;
    }
}

/// <summary>
/// Native <c>NoBlockPower</c>: powered block from a card source becomes zero for its owner, and the
/// power loses one stack at the end of the enemy side (CorePowerSupport.TickDurations).
/// </summary>
[FlashContent("NO_BLOCK_POWER")]
public sealed class NoBlockPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool TicksAtEnemySideEnd => true;

    public override decimal ModifyBlockMultiplicative(ref FlashExec x, FlashListener self, in FlashBlockContext block, decimal amount)
    {
        if (block.Target != self.Owner)
            return 1m;
        if ((block.Props & FlashValueProps.Unpowered) != 0)
            return 1m;
        if (!block.HasCardSource)
            return 1m;
        return 0m;
    }
}

/// <summary>
/// Native <c>PrepTimePower</c>: at its owner's side turn start it applies <c>Amount</c> Vigor to the
/// owner. The existing engine resolves it in PersistentPowerSupport.TriggerOwnerAfterSideTurnStart.
/// </summary>
[FlashContent("PREP_TIME_POWER")]
public sealed class PrepTimePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref FlashExec x, FlashListener self)
    {
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<VigorPower>(),
            x.B.Powers[self.Index].Amount, self.Owner);
    }
}

/// <summary>
/// Native <c>TheGambitPower</c>: a single-stack debuff that removes itself and kills its owner when
/// the owner takes unblocked damage from a powered attack (AfterDamageReceivedMirrors.HandleTheGambitPower).
/// </summary>
[FlashContent("THE_GAMBIT_POWER")]
public sealed class TheGambitPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || !result.Props.IsPoweredAttack() || result.UnblockedDamage <= 0)
            return;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
        Span<byte> targets = stackalloc byte[1];
        targets[0] = self.Owner;
        // CreatureCmd.Kill(owner): a normal (non-forced) kill, so death prevention still runs.
        FlashKernel.Kill(ref x, targets, force: false);
    }
}
