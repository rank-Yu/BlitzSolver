using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// Native <c>ClarityPower</c>: draws one extra card per turn while its owner is the player and loses
/// one stack at its owner's side turn start
/// (<c>PersistentPowerSupport.TriggerOwnerAfterSideTurnStart</c>).
/// </summary>
[BlitzContent("CLARITY_POWER")]
public sealed class ClarityPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    // ClarityPower.ModifyHandDraw: only the player's own clarity adds one, independent of the amount.
    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
        => self.Owner == BlitzLimits.PlayerOwner ? amount + 1m : amount;

    // PersistentPowerSupport.TriggerOwnerAfterSideTurnStart: decrement one stack.
    public override void OwnerAfterSideTurnStart(ref BlitzExec x, BlitzListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }
}

/// <summary>
/// Native <c>FlexPotionPower</c> (a positive <c>TemporaryStrengthPower</c>): at its owner's side turn
/// end it is removed and its amount of Strength is taken back
/// (<c>EndTurnPowerSupport.TriggerRegular</c>, <c>SimulatedCombatState.RetireTemporaryStat</c>).
/// </summary>
[BlitzContent("FLEX_POTION_POWER")]
public sealed class FlexPotionPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override string? InternallyAppliedPowerId => "STRENGTH_POWER";

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool IsTemporaryPower => true;
    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
            BlitzKernel.RetireTemporaryStrength(ref x, self);
    }
}

/// <summary>
/// Native <c>ShacklingPotionPower</c> (a negative <c>TemporaryStrengthPower</c>): at its owner's side
/// turn end it is removed and its amount of Strength is restored
/// (<c>EndTurnPowerSupport.TriggerRegular</c>, <c>SimulatedCombatState.RetireTemporaryStat</c>).
/// </summary>
[BlitzContent("SHACKLING_POTION_POWER")]
public sealed class ShacklingPotionPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
            BlitzKernel.RetireTemporaryStrength(ref x, self);
    }
}

/// <summary>
/// Native <c>SpeedPotionPower</c> (a positive <c>TemporaryDexterityPower</c>): at its owner's side
/// turn end it is removed and its amount of Dexterity is taken back
/// (<c>EndTurnPowerSupport.TriggerRegular</c>, <c>SimulatedCombatState.RetireTemporaryStat</c>).
/// </summary>
[BlitzContent("SPEED_POTION_POWER")]
public sealed class SpeedPotionPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override string? InternallyAppliedPowerId => "DEXTERITY_POWER";

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool IsTemporaryPower => true;
    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (!participates)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
        if (amount == 0)
            return;
        BlitzKernel.ApplyPower(ref x, self.Owner, BlitzRegistry.PowerId<DexterityPower>(), -amount, self.Owner);
    }
}

/// <summary>
/// Native <c>RadiancePower</c>: gains <c>Energy</c> energy at the owner's energy reset, then loses one
/// stack (<c>AfterEnergyResetMirrors.HandleRadiancePower</c>). No Energy Gain is not supported yet, so
/// the guard only fires when that power is registered.
/// </summary>
[BlitzContent("RADIANCE_POWER")]
public sealed class RadiancePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterEnergyReset(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (!BlitzRegistry.TryPower("NO_ENERGY_GAIN_POWER", out ushort noEnergyGain)
            || BlitzKernel.GetAmount(x.B, self.Owner, noEnergyGain) <= 0)
        {
            BlitzKernel.GainEnergy(ref x, x.R.PowerVar(Id, 0));
        }
        if (amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }
}

/// <summary>
/// Native <c>RegenPower</c>: heals its amount at its owner's side turn end and loses one stack. The
/// existing engine heals in <c>BeforeSideTurnEndMirrors.HandleRegenPower</c> for the player and in
/// <c>CorePowerSupport.TriggerEnemySideTurnEndEffects</c> for enemies; the player's decrement happens
/// in <c>CompletePlayerEarlySideTurnEndEffects</c> after the turn-end hand effects.
/// </summary>
[BlitzContent("REGEN_POWER")]
public sealed class RegenPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeSideTurnEndEarly(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        if (!BlitzKernel.Participates(participants, self.Owner) || x.B.Creature(self.Owner).IsDead)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            BlitzKernel.Heal(ref x, self.Owner, amount);
    }

    public override void EnemySideTurnEnd(ref BlitzExec x, BlitzListener self)
    {
        if (x.B.Creature(self.Owner).IsDead)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        BlitzKernel.Heal(ref x, self.Owner, amount);
        BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }

    // CorePowerSupport.CompletePlayerEarlySideTurnEndEffects: the player's regen stack is removed after DoTurnEnd.
    public override void CompletePlayerEarlySideTurnEnd(ref BlitzExec x, BlitzListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }
}

/// <summary>
/// Native <c>RetainHandPower</c>: <c>ShouldFlush</c> keeps every card in the owner's hand at the
/// player's turn end, and the power loses one stack in
/// <c>CorePowerSupport.TriggerTransientSideTurnEndPowers</c>.
/// </summary>
[BlitzContent("RETAIN_HAND_POWER")]
public sealed class RetainHandPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    // RetainHandPower.ShouldFlush: false for its owner's player, so the hand is not flushed.
    public override bool ShouldFlush(ref BlitzExec x, BlitzListener self) => !BlitzKernel.IsPlayerSide(self.Owner);
}

/// <summary>
/// Native <c>BlockNextTurnPower</c>: when its owner's block is cleared, gains its amount as
/// Unpowered block and is removed (<c>CorePowerSupport.TriggerAfterBlockCleared</c>).
/// </summary>
[BlitzContent("BLOCK_NEXT_TURN_POWER")]
public sealed class BlockNextTurnPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterBlockCleared(ref BlitzExec x, BlitzListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        BlitzKernel.GainBlock(ref x, self.Owner, amount, BlitzValueProps.Unpowered, fromCard: false);
        BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}

/// <summary>
/// Native <c>DemisePower</c>: at its owner's side turn end, damages the owner for its amount as
/// Unblockable | Unpowered damage with no dealer
/// (<c>EndTurnPowerSupport.TriggerRegular</c>).
/// </summary>
[BlitzContent("DEMISE_POWER")]
public sealed class DemisePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (!participates || x.B.Creature(self.Owner).IsDead)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        Span<byte> target = stackalloc byte[1];
        target[0] = self.Owner;
        BlitzKernel.Damage(ref x, target, amount,
            BlitzValueProps.Unblockable | BlitzValueProps.Unpowered, BlitzLimits.NoOwner,
            hasCardSource: false, default);
    }
}

/// <summary>
/// Native <c>DuplicationPower</c> (Duplicator potion): each played card gets one extra play;
/// <c>AfterModifyingCardPlayCount</c> decrements the power, and the player turn end removes it
/// (<c>ModifyCardPlayCountMirrors</c>, <c>CorePowerSupport.TriggerTransientSideTurnEndPowers</c>).
/// </summary>
[BlitzContent("DUPLICATION_POWER")]
public sealed class DuplicationPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override int ModifyCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card, byte target,
        int playCount)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return playCount;
        return playCount + 1;
    }

    public override void AfterModifyingCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }
}

/// <summary>
/// Native <c>GigantificationPower</c>: the next powered Attack card play deals triple damage and
/// consumes one stack. BeforeAttack records the armed attack, ModifyDamageMultiplicative applies 3x
/// while the same card's attack runs, AfterAttack decrements. Aux0 is the armed flag; CommandToModify's
/// card identity is unambiguous because Blitz resolves one card attack at a time.
/// </summary>
[BlitzContent("GIGANTIFICATION_POWER")]
public sealed class GigantificationPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeAttack(ref BlitzExec x, BlitzListener self, byte attacker, BlitzValueProps props)
    {
        // GigantificationPowerMirrors.BeforeAttack calls StateStore.GetPowerAmount(power) before any
        // predicate, for every attack (including the enemy's). That creates the native
        // PowerAmountPredictionState; the action-boundary synchronization restores the model amount
        // from it, so a player death during the same action leaves the power in place. The flag lets
        // BlitzKernel.HandlePlayerDeath keep the row; Step clears it at the stable boundary.
        x.B.Powers[self.Index].Flags |= BlitzPowerFlags.PendingAmountState;
        if (self.Owner != attacker || !props.IsPoweredAttack())
            return;
        if (x.Card.Id == 0 || x.Stats?.Type != BlitzCardType.Attack)
            return;
        if (x.B.Powers[self.Index].Amount > 0)
            x.B.Powers[self.Index].Aux0 = 1;
    }

    public override decimal ModifyDamageMultiplicative(ref BlitzExec x, BlitzListener self,
        in BlitzDamageContext damage, decimal amount)
    {
        if (self.Owner != BlitzLimits.PlayerOwner || x.B.Powers[self.Index].Aux0 == 0)
            return 1m;
        if (!damage.HasCardSource || !damage.Props.IsPoweredAttack())
            return 1m;
        return 3m;
    }

    public override void AfterAttack(ref BlitzExec x, BlitzListener self, byte attacker)
    {
        if (self.Owner != attacker)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        if (power.Aux0 == 0)
            return;
        power.Aux0 = 0;
        if (power.Amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, Id, power.Amount - 1);
    }
}
