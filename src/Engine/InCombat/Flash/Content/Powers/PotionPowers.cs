namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Powers applied by the potions in Content/Potions. Each mirrors the existing engine's handling, not
// the decompiled original where the two differ.

/// <summary>
/// Native <c>ClarityPower</c>: draws one extra card per turn while its owner is the player and loses
/// one stack at its owner's side turn start
/// (<c>PersistentPowerSupport.TriggerOwnerAfterSideTurnStart</c>).
/// </summary>
[FlashContent("CLARITY_POWER")]
public sealed class ClarityPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    // ClarityPower.ModifyHandDraw: only the player's own clarity adds one, independent of the amount.
    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
        => self.Owner == FlashLimits.PlayerOwner ? amount + 1m : amount;

    // PersistentPowerSupport.TriggerOwnerAfterSideTurnStart: decrement one stack.
    public override void OwnerAfterSideTurnStart(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }
}

/// <summary>
/// Native <c>FlexPotionPower</c> (a positive <c>TemporaryStrengthPower</c>): at its owner's side turn
/// end it is removed and its amount of Strength is taken back
/// (<c>EndTurnPowerSupport.TriggerRegular</c>, <c>SimulatedCombatState.RetireTemporaryStat</c>).
/// </summary>
[FlashContent("FLEX_POTION_POWER")]
public sealed class FlexPotionPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override string? InternallyAppliedPowerId => "STRENGTH_POWER";

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsTemporaryPower => true;
    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.RetireTemporaryStrength(ref x, self);
    }
}

/// <summary>
/// Native <c>ShacklingPotionPower</c> (a negative <c>TemporaryStrengthPower</c>): at its owner's side
/// turn end it is removed and its amount of Strength is restored
/// (<c>EndTurnPowerSupport.TriggerRegular</c>, <c>SimulatedCombatState.RetireTemporaryStat</c>).
/// </summary>
[FlashContent("SHACKLING_POTION_POWER")]
public sealed class ShacklingPotionPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.RetireTemporaryStrength(ref x, self);
    }
}

/// <summary>
/// Native <c>SpeedPotionPower</c> (a positive <c>TemporaryDexterityPower</c>): at its owner's side
/// turn end it is removed and its amount of Dexterity is taken back
/// (<c>EndTurnPowerSupport.TriggerRegular</c>, <c>SimulatedCombatState.RetireTemporaryStat</c>).
/// </summary>
[FlashContent("SPEED_POTION_POWER")]
public sealed class SpeedPotionPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override string? InternallyAppliedPowerId => "DEXTERITY_POWER";

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsTemporaryPower => true;
    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
        if (amount == 0)
            return;
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<DexterityPower>(), -amount, self.Owner);
    }
}

/// <summary>
/// Native <c>RadiancePower</c>: gains <c>Energy</c> energy at the owner's energy reset, then loses one
/// stack (<c>AfterEnergyResetMirrors.HandleRadiancePower</c>). No Energy Gain is not supported yet, so
/// the guard only fires when that power is registered.
/// </summary>
[FlashContent("RADIANCE_POWER")]
public sealed class RadiancePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterEnergyReset(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (!FlashRegistry.TryPower("NO_ENERGY_GAIN_POWER", out ushort noEnergyGain)
            || FlashKernel.GetAmount(x.B, self.Owner, noEnergyGain) <= 0)
        {
            FlashKernel.GainEnergy(ref x, x.R.PowerVar(Id, 0));
        }
        if (amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }
}

/// <summary>
/// Native <c>RegenPower</c>: heals its amount at its owner's side turn end and loses one stack. The
/// existing engine heals in <c>BeforeSideTurnEndMirrors.HandleRegenPower</c> for the player and in
/// <c>CorePowerSupport.TriggerEnemySideTurnEndEffects</c> for enemies; the player's decrement happens
/// in <c>CompletePlayerEarlySideTurnEndEffects</c> after the turn-end hand effects.
/// </summary>
[FlashContent("REGEN_POWER")]
public sealed class RegenPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeSideTurnEndEarly(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (!FlashKernel.Participates(participants, self.Owner) || x.B.Creature(self.Owner).IsDead)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            FlashKernel.Heal(ref x, self.Owner, amount);
    }

    public override void EnemySideTurnEnd(ref FlashExec x, FlashListener self)
    {
        if (x.B.Creature(self.Owner).IsDead)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        FlashKernel.Heal(ref x, self.Owner, amount);
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }

    // CorePowerSupport.CompletePlayerEarlySideTurnEndEffects: the player's regen stack is removed after DoTurnEnd.
    public override void CompletePlayerEarlySideTurnEnd(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }
}

/// <summary>
/// Native <c>RetainHandPower</c>: <c>ShouldFlush</c> keeps every card in the owner's hand at the
/// player's turn end, and the power loses one stack in
/// <c>CorePowerSupport.TriggerTransientSideTurnEndPowers</c>.
/// </summary>
[FlashContent("RETAIN_HAND_POWER")]
public sealed class RetainHandPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    // RetainHandPower.ShouldFlush: false for its owner's player, so the hand is not flushed.
    public override bool ShouldFlush(ref FlashExec x, FlashListener self) => !FlashKernel.IsPlayerSide(self.Owner);
}

/// <summary>
/// Native <c>BlockNextTurnPower</c>: when its owner's block is cleared, gains its amount as
/// Unpowered block and is removed (<c>CorePowerSupport.TriggerAfterBlockCleared</c>).
/// </summary>
[FlashContent("BLOCK_NEXT_TURN_POWER")]
public sealed class BlockNextTurnPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterBlockCleared(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        FlashKernel.GainBlock(ref x, self.Owner, amount, FlashValueProps.Unpowered, fromCard: false);
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}

/// <summary>
/// Native <c>DemisePower</c>: at its owner's side turn end, damages the owner for its amount as
/// Unblockable | Unpowered damage with no dealer
/// (<c>EndTurnPowerSupport.TriggerRegular</c>).
/// </summary>
[FlashContent("DEMISE_POWER")]
public sealed class DemisePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates || x.B.Creature(self.Owner).IsDead)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        Span<byte> target = stackalloc byte[1];
        target[0] = self.Owner;
        FlashKernel.Damage(ref x, target, amount,
            FlashValueProps.Unblockable | FlashValueProps.Unpowered, FlashLimits.NoOwner,
            hasCardSource: false, default);
    }
}

/// <summary>
/// Native <c>DuplicationPower</c> (Duplicator potion): each played card gets one extra play;
/// <c>AfterModifyingCardPlayCount</c> decrements the power, and the player turn end removes it
/// (<c>ModifyCardPlayCountMirrors</c>, <c>CorePowerSupport.TriggerTransientSideTurnEndPowers</c>).
/// </summary>
[FlashContent("DUPLICATION_POWER")]
public sealed class DuplicationPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override int ModifyCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card, byte target,
        int playCount)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return playCount;
        return playCount + 1;
    }

    public override void AfterModifyingCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }
}

/// <summary>
/// Native <c>GigantificationPower</c>: the next powered Attack card play deals triple damage and
/// consumes one stack. BeforeAttack records the armed attack, ModifyDamageMultiplicative applies 3x
/// while the same card's attack runs, AfterAttack decrements. Aux0 is the armed flag; CommandToModify's
/// card identity is unambiguous because Flash resolves one card attack at a time.
/// </summary>
[FlashContent("GIGANTIFICATION_POWER")]
public sealed class GigantificationPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeAttack(ref FlashExec x, FlashListener self, byte attacker, FlashValueProps props)
    {
        if (self.Owner != attacker || !props.IsPoweredAttack())
            return;
        if (x.Card.Id == 0 || x.Stats?.Type != FlashCardType.Attack)
            return;
        if (x.B.Powers[self.Index].Amount > 0)
            x.B.Powers[self.Index].Aux0 = 1;
    }

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self,
        in FlashDamageContext damage, decimal amount)
    {
        if (self.Owner != FlashLimits.PlayerOwner || x.B.Powers[self.Index].Aux0 == 0)
            return 1m;
        if (!damage.HasCardSource || !damage.Props.IsPoweredAttack())
            return 1m;
        return 3m;
    }

    public override void AfterAttack(ref FlashExec x, FlashListener self, byte attacker)
    {
        if (self.Owner != attacker)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Aux0 == 0)
            return;
        power.Aux0 = 0;
        if (power.Amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, power.Amount - 1);
    }
}
