using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// <c>DieForYouPower</c>: while Osty is alive, a powered attack on its owner redirects the unblocked HP
/// loss to Osty (<c>Hook.ModifyUnblockedDamageTarget</c>; existing engine path
/// <c>CombatPredictionSimulator.TryDamageTarget</c>). The pet is created with one layer and keeps the
/// power through death (<c>ShouldPowerBeRemovedAfterOwnerDeath</c> false).
/// </summary>
[BlitzContent("DIE_FOR_YOU_POWER")]
public sealed class DieForYouPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath => false;

    public override byte ModifyUnblockedDamageTarget(ref BlitzExec x, BlitzListener self, byte target,
        decimal amount, BlitzValueProps props, byte dealer)
    {
        if (target != BlitzLimits.PlayerOwner || x.B.Osty.IsDead || !props.IsPoweredAttack())
            return target;
        return BlitzLimits.OstyOwner;
    }
}

/// <summary>
/// <c>DoomPower</c>: the enemy-side turn end kills every living participant whose HP is at or below the
/// total Doom on it (<c>BeforeSideTurnEndMirrors.HandleDoomPower</c>,
/// <c>EndTurnPowerSupport.TriggerEnemyDoom</c>); the player-side turn end kills its own owner through
/// the per-listener <c>TriggerRegular</c> case. Doom is applied by cards and counted by the evaluation.
/// </summary>
[BlitzContent("DOOM_POWER")]
public sealed class DoomPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        // BeforeSideTurnEndMirrors.HandleDoomPower: enemy side only, the owner must participate and be
        // alive, and only the first doomed owner in creature order starts the shared kill.
        if (BlitzKernel.IsOverOrEnding(x.B) || side == BlitzSide.Player
            || !BlitzKernel.Participates(participants, self.Owner)
            || x.B.Creature(self.Owner).IsDead)
        {
            return;
        }
        bool ownerDoomed = false;
        bool ownerIsFirst = false;
        Span<byte> doomed = stackalloc byte[BlitzLimits.Enemies + 2];
        int doomedCount = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            int doom = DoomFor(x.B, owner);
            bool isDoomed = x.B.Creature(owner).IsAlive && doom > 0 && x.B.Creature(owner).CurrentHp <= doom;
            if (isDoomed)
            {
                if (doomedCount == 0)
                    ownerIsFirst = owner == self.Owner;
                doomed[doomedCount++] = owner;
            }
            if (owner == self.Owner)
                ownerDoomed = isDoomed;
        }
        if (!ownerDoomed || !ownerIsFirst)
            return;
        BlitzKernel.DoomKill(ref x, doomed[..doomedCount]);
    }

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (side == BlitzSide.Enemy || !participates)
            return;
        ref readonly BlitzPower power = ref x.B.Powers[self.Index];
        ref readonly BlitzCreature creature = ref x.B.Creature(self.Owner);
        if (power.Amount <= 0 || creature.IsDead || creature.CurrentHp > power.Amount)
            return;
        Span<byte> one = stackalloc byte[1];
        one[0] = self.Owner;
        BlitzKernel.Kill(ref x, one, force: false);
    }

    /// <summary>The total positive Doom on one creature (the existing engine sums listener amounts).</summary>
    internal static int DoomFor(in BlitzBattle battle, byte owner)
    {
        if (!BlitzRegistry.TryPower("DOOM_POWER", out ushort id))
            return 0;
        return BlitzKernel.GetAmount(battle, owner, id);
    }
}

/// <summary>
/// <c>NecroMasteryPower</c>: HP lost by the pet deals that much times the layer count to every hittable
/// enemy (<c>AfterCurrentHpChangedMirrors.HandleNecroMasteryPower</c>).
/// </summary>
[BlitzContent("NECRO_MASTERY_POWER")]
public sealed class NecroMasteryPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCurrentHpChanged(ref BlitzExec x, BlitzListener self, byte creature, decimal delta)
    {
        if (delta >= 0m || creature != BlitzLimits.OstyOwner || self.Owner != BlitzLimits.PlayerOwner)
            return;
        decimal amount = -delta * x.B.Powers[self.Index].Amount;
        BlitzCardEffects.DamageAllEnemies(ref x, amount,
            BlitzValueProps.Unblockable | BlitzValueProps.Unpowered, self.Owner);
    }
}

/// <summary>
/// <c>CalcifyPower</c>: adds its amount to a powered attack made by the owner's Osty
/// (<c>ModifyDamageMirrors</c> invokes <c>CalcifyPower.ModifyDamageAdditive</c>).
/// </summary>
[BlitzContent("CALCIFY_POWER")]
public sealed class CalcifyPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyDamageAdditive(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage,
        decimal amount)
    {
        if (damage.Dealer != BlitzLimits.OstyOwner || self.Owner != BlitzLimits.PlayerOwner
            || !damage.Props.IsPoweredAttack())
        {
            return 0m;
        }
        return x.B.Powers[self.Index].Amount;
    }
}

/// <summary>
/// <c>CountdownPower</c>: at its owner's side turn start, apply its amount as Doom to a random hittable
/// opponent (<c>TurnStartPowerSupport.TriggerAfterSideTurnStart</c>, the COUNTDOWN case; the kernel's
/// fixed <c>GlobalAfterSideTurnStart</c> sequence calls it in listener order).
/// </summary>
[BlitzContent("COUNTDOWN_POWER")]
public sealed class CountdownPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void GlobalAfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (!participates || self.Owner != BlitzLimits.PlayerOwner || x.B.Powers[self.Index].Amount <= 0)
            return;
        Span<byte> candidates = stackalloc byte[BlitzLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (BlitzKernel.IsHittable(x.B, enemy))
                candidates[count++] = enemy;
        }
        if (count == 0)
            return;
        byte target = candidates[x.B.RngStream(BlitzRngStream.CombatTargets).NextInt(0, count)];
        BlitzKernel.ApplyPower(ref x, target, DoomId(), x.B.Powers[self.Index].Amount, self.Owner);
    }

    internal static ushort DoomId()
        => BlitzRegistry.TryPower("DOOM_POWER", out ushort id)
            ? id
            : throw new BlitzUnsupportedException("倒计时需要 DoomPower 的 Blitz 定义。");
}

/// <summary>
/// <c>NeurosurgePower</c>: at its owner's side turn start, apply its amount as Doom to its owner
/// (<c>PersistentPowerSupport.TriggerOwnerAfterSideTurnStart</c>, the NEUROSURGE case; the kernel's
/// fixed owner sequence calls it).
/// </summary>
[BlitzContent("NEUROSURGE_POWER")]
public sealed class NeurosurgePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref BlitzExec x, BlitzListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        BlitzKernel.ApplyPower(ref x, self.Owner, CountdownPower.DoomId(), amount, self.Owner);
    }
}

/// <summary>
/// <c>HauntPower</c>: after its owner plays a Soul, deal its amount to a random hittable enemy as
/// unblockable unpowered damage with no dealer (<c>AfterCardPlayedMirrors.HandleHauntPower</c>).
/// </summary>
[BlitzContent("HAUNT_POWER")]
public sealed class HauntPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Owner != BlitzLimits.PlayerOwner
            || !BlitzRegistry.TryCard("SOUL", out ushort soul) || play.Card.Id != soul)
        {
            return;
        }
        Span<byte> targets = stackalloc byte[BlitzLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (BlitzKernel.IsHittable(x.B, enemy))
                targets[count++] = enemy;
        }
        if (count == 0)
            return;
        byte target = targets[x.B.RngStream(BlitzRngStream.CombatTargets).NextInt(0, count)];
        Span<byte> one = stackalloc byte[1];
        one[0] = target;
        BlitzKernel.Damage(ref x, one, x.B.Powers[self.Index].Amount,
            BlitzValueProps.Unblockable | BlitzValueProps.Unpowered, BlitzLimits.NoOwner,
            hasCardSource: false, default);
    }
}

/// <summary>
/// <c>LethalityPower</c>: multiplies the first attack card played each turn by 1 + amount / 100
/// (<c>ModifyDamageMirrors.HandleLethalityPower</c>). The attack's play index must be zero and the
/// card's owner must be the power owner.
/// </summary>
[BlitzContent("LETHALITY_POWER")]
public sealed class LethalityPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage,
        decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource || self.Owner != BlitzLimits.PlayerOwner)
            return 1m;
        bool inPlay = x.EffectDepth > 0;
        if (inPlay && x.PlayIndex > 0)
            return 1m;
        int started = x.B.Counter(BlitzCounter.AttackPlayStartsThisTurn);
        return started <= (inPlay ? 1 : 0)
            ? 1m + (decimal)x.B.Powers[self.Index].Amount / 100m
            : 1m;
    }
}

/// <summary>
/// <c>ShroudPower</c>: gain block equal to its amount whenever its owner applies Doom
/// (<c>PowerLifecycleSupport.ResolvePowerAmountChanges</c>, the Shroud case).
/// </summary>
[BlitzContent("SHROUD_POWER")]
public sealed class ShroudPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterPowerAmountChanged(ref BlitzExec x, BlitzListener self, ushort powerId, int delta,
        byte applier, byte target)
    {
        if (applier != self.Owner || powerId != CountdownPower.DoomId())
            return;
        BlitzKernel.GainBlock(ref x, self.Owner, x.B.Powers[self.Index].Amount,
            BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>SicEmPower</c>: when the applier's Osty damages the owner, summon that much HP for the pet
/// (<c>AfterDamageGivenMirrors.HandleSicEmPower</c>); removed at its owner's side turn end
/// (<c>EndTurnPowerSupport.TriggerBatch048</c>).
/// </summary>
[BlitzContent("SIC_EM_POWER")]
public sealed class SicEmPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterDamageGiven(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (dealer == BlitzLimits.OstyOwner && result.Receiver == self.Owner
            && self.Owner != BlitzLimits.PlayerOwner
            && x.B.Powers[self.Index].Amount > 0)
        {
            BlitzKernel.SummonOsty(ref x, x.B.Powers[self.Index].Amount);
        }
    }

    public override void EndTurnBatch048(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// <c>OblivionPower</c>: records its amount at each play start (the kernel's transient play stack, the
/// existing engine's <c>CardPlayPairPredictionState</c>) and applies that amount as Doom after the play;
/// removed at the player's side turn end (<c>EndTurnPowerSupport.TriggerBatch048</c>).
/// </summary>
[BlitzContent("OBLIVION_POWER")]
public sealed class OblivionPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        ref readonly BlitzPower power = ref x.B.Powers[self.Index];
        if (power.Applier != BlitzLimits.PlayerOwner)
            return;
        x.PushCardPlayPair(BlitzCardPlayPairKind.OblivionDoom, self.Owner, power.Id, power.Amount);
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        ref readonly BlitzPower power = ref x.B.Powers[self.Index];
        if (!x.TryTakeCardPlayPair(BlitzCardPlayPairKind.OblivionDoom, self.Owner, power.Id, out int amount))
            return;
        BlitzKernel.ApplyPower(ref x, self.Owner, CountdownPower.DoomId(), amount, power.Applier);
    }

    public override void EndTurnBatch048(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (side == BlitzSide.Player)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// <c>VeilpiercerPower</c>: Ethereal cards in hand or in play cost zero (the Late cost pass) and
/// decrement the power when played (<c>ModifyEnergyCostInCombatMirrors</c>,
/// <c>BeforeCardPlayedMirrors.HandleVeilpiercerPower</c>).
/// </summary>
[BlitzContent("VEILPIERCER_POWER")]
public sealed class VeilpiercerPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombatLate(in BlitzBattle battle, BlitzRules rules,
        BlitzListener self, in BlitzCard card, BlitzPile pile, decimal amount)
    {
        if (self.Owner != BlitzLimits.PlayerOwner || !card.HasKeyword(BlitzKeywords.Ethereal)
            || pile is not (BlitzPile.Hand or BlitzPile.Play))
        {
            return amount;
        }
        return 0m;
    }

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        ref readonly BlitzPower power = ref x.B.Powers[self.Index];
        if (self.Owner != BlitzLimits.PlayerOwner || power.Amount <= 0
            || !play.Card.HasKeyword(BlitzKeywords.Ethereal))
        {
            return;
        }
        BlitzKernel.SetPowerAmount(ref x.B, self.Owner, power.Id, power.Amount - 1);
    }
}

/// <summary>
/// <c>DebilitatePower</c>: the owner takes double Vulnerable damage and deals half Weak damage
/// (<c>ModifyDamageMirrors</c> folds this into the Vulnerable and Weak multipliers); decrements at its
/// owner's side turn end (<c>EndTurnPowerSupport.TriggerBatch048</c>).
/// </summary>
[BlitzContent("DEBILITATE_POWER")]
public sealed class DebilitatePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void EndTurnBatch048(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id,
                x.B.Powers[self.Index].Amount - 1);
    }
}

/// <summary>
/// <c>HangPower</c>: the next Hang card played against its owner deals damage times this amount
/// (<c>HangPower.ModifyDamageMultiplicative</c> through <c>ModifyDamageMirrors</c>).
/// </summary>
[BlitzContent("HANG_POWER")]
public sealed class HangPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage,
        decimal amount)
    {
        if (damage.Target != self.Owner || !damage.HasCardSource
            || !BlitzRegistry.TryCard("HANG", out ushort hang) || damage.CardSource.Id != hang)
        {
            return 1m;
        }
        return x.B.Powers[self.Index].Amount;
    }
}

/// <summary>
/// <c>BorrowedTimePower</c>: every card costs its amount more until its owner's side turn end
/// (<c>BorrowedTimePower.TryModifyEnergyCostInCombat</c>; the kernel's transient removal pass clears it).
/// </summary>
[BlitzContent("BORROWED_TIME_POWER")]
public sealed class BorrowedTimePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombat(in BlitzBattle battle, BlitzRules rules,
        BlitzListener self, in BlitzCard card, BlitzPile pile, decimal amount)
        => self.Owner == BlitzLimits.PlayerOwner ? amount + battle.Powers[self.Index].Amount : amount;
}

/// <summary>
/// <c>FriendshipPower</c>: each layer adds one maximum energy (<c>FriendshipPower.ModifyMaxEnergy</c>
/// through <c>PersistentPowerSupport.GetModifiedMaxEnergy</c>).
/// </summary>
[BlitzContent("FRIENDSHIP_POWER")]
public sealed class FriendshipPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => self.Owner == BlitzLimits.PlayerOwner ? amount + x.B.Powers[self.Index].Amount : amount;
}

/// <summary>
/// <c>EnfeeblingTouchPower</c> (a negative <c>TemporaryStrengthPower</c>): application changes Strength
/// by -amount and its owner's side turn end retires it, restoring Strength.
/// </summary>
[BlitzContent("ENFEEBLING_TOUCH_POWER")]
public sealed class EnfeeblingTouchPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

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
/// <c>SummonNextTurnPower</c>: at the player's turn start, summon that much HP for Osty and consume the
/// power (<c>TurnStartPowerSupport</c>'s SUMMON_NEXT_TURN case).
/// </summary>
[BlitzContent("SUMMON_NEXT_TURN_POWER")]
public sealed class SummonNextTurnPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool UsesAmountOnTurnStart => true;

    public override void AfterPlayerTurnStart(ref BlitzExec x, BlitzListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        BlitzKernel.SummonOsty(ref x, amount);
        if (!x.NeedsChoice)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// <c>SleightOfFleshPower</c>: when its owner applies a non-temporary debuff to an enemy, deal its amount
/// to that enemy as unpowered damage (<c>PowerLifecycleSupport.ResolvePowerAmountChanges</c>).
/// </summary>
[BlitzContent("SLEIGHT_OF_FLESH_POWER")]
public sealed class SleightOfFleshPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterPowerAmountChanged(ref BlitzExec x, BlitzListener self, ushort powerId, int delta,
        byte applier, byte target)
    {
        if (delta == 0 || applier != self.Owner || !BlitzKernel.IsEnemyOwner(target)
            || BlitzRegistry.Power(powerId).TypeForAmount(delta) != BlitzPowerType.Debuff
            || BlitzRegistry.Power(powerId).IsTemporaryPower)
        {
            return;
        }
        Span<byte> one = stackalloc byte[1];
        one[0] = target;
        BlitzKernel.Damage(ref x, one, x.B.Powers[self.Index].Amount, BlitzValueProps.Unpowered, self.Owner,
            hasCardSource: false, default);
    }
}

/// <summary>
/// <c>SpiritOfAshPower</c>: gain its amount as unpowered block before its owner plays an Ethereal card
/// (<c>BeforeCardPlayedMirrors.HandleSpiritOfAshPower</c>).
/// </summary>
[BlitzContent("SPIRIT_OF_ASH_POWER")]
public sealed class SpiritOfAshPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Owner != BlitzLimits.PlayerOwner
            || !BlitzKernel.HasKeyword(x.B, play.Card, BlitzKeywords.Ethereal))
        {
            return;
        }
        BlitzKernel.GainBlock(ref x, self.Owner, x.B.Powers[self.Index].Amount, BlitzValueProps.Unpowered,
            fromCard: false);
    }
}

/// <summary>
/// <c>DevourLifePower</c>: after its owner plays a Soul, summon its amount of Osty HP
/// (<c>AfterCardPlayedMirrors.HandleDevourLifePower</c>).
/// </summary>
[BlitzContent("DEVOUR_LIFE_POWER")]
public sealed class DevourLifePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Owner != BlitzLimits.PlayerOwner
            || !BlitzRegistry.TryCard("SOUL", out ushort soul) || play.Card.Id != soul)
        {
            return;
        }
        BlitzKernel.SummonOsty(ref x, x.B.Powers[self.Index].Amount);
    }
}

/// <summary>
/// <c>DemesnePower</c>: its amount adds both hand draw and maximum energy
/// (<c>PersistentPowerSupport.GetModifiedHandDraw</c>/<c>GetModifiedMaxEnergy</c> invoke the native methods).
/// </summary>
[BlitzContent("DEMESNE_POWER")]
public sealed class DemesnePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
        => self.Owner == BlitzLimits.PlayerOwner ? amount + x.B.Powers[self.Index].Amount : amount;

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => self.Owner == BlitzLimits.PlayerOwner ? amount + x.B.Powers[self.Index].Amount : amount;
}
