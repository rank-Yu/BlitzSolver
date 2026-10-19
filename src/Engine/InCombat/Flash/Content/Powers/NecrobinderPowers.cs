namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Necrobinder powers. Conditions and order are copied from the existing engine path named on each
// definition; the decompiled model is used only to confirm constants and hook bodies.

/// <summary>
/// <c>DieForYouPower</c>: while Osty is alive, a powered attack on its owner redirects the unblocked HP
/// loss to Osty (<c>Hook.ModifyUnblockedDamageTarget</c>; existing engine path
/// <c>CombatPredictionSimulator.TryDamageTarget</c>). The pet is created with one layer and keeps the
/// power through death (<c>ShouldPowerBeRemovedAfterOwnerDeath</c> false).
/// </summary>
[FlashContent("DIE_FOR_YOU_POWER")]
public sealed class DieForYouPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath => false;

    public override byte ModifyUnblockedDamageTarget(ref FlashExec x, FlashListener self, byte target,
        decimal amount, FlashValueProps props, byte dealer)
    {
        if (target != FlashLimits.PlayerOwner || x.B.Osty.IsDead || !props.IsPoweredAttack())
            return target;
        return FlashLimits.OstyOwner;
    }
}

/// <summary>
/// <c>DoomPower</c>: the enemy-side turn end kills every living participant whose HP is at or below the
/// total Doom on it (<c>BeforeSideTurnEndMirrors.HandleDoomPower</c>,
/// <c>EndTurnPowerSupport.TriggerEnemyDoom</c>); the player-side turn end kills its own owner through
/// the per-listener <c>TriggerRegular</c> case. Doom is applied by cards and counted by the evaluation.
/// </summary>
[FlashContent("DOOM_POWER")]
public sealed class DoomPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        // BeforeSideTurnEndMirrors.HandleDoomPower: enemy side only, the owner must participate and be
        // alive, and only the first doomed owner in creature order starts the shared kill.
        if (FlashKernel.IsOverOrEnding(x.B) || side == FlashSide.Player
            || !FlashKernel.Participates(participants, self.Owner)
            || x.B.Creature(self.Owner).IsDead)
        {
            return;
        }
        bool ownerDoomed = false;
        bool ownerIsFirst = false;
        Span<byte> doomed = stackalloc byte[FlashLimits.Enemies + 2];
        int doomedCount = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
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
        FlashKernel.DoomKill(ref x, doomed[..doomedCount]);
    }

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (side == FlashSide.Enemy || !participates)
            return;
        ref readonly FlashPower power = ref x.B.Powers[self.Index];
        ref readonly FlashCreature creature = ref x.B.Creature(self.Owner);
        if (power.Amount <= 0 || creature.IsDead || creature.CurrentHp > power.Amount)
            return;
        Span<byte> one = stackalloc byte[1];
        one[0] = self.Owner;
        FlashKernel.Kill(ref x, one, force: false);
    }

    /// <summary>The total positive Doom on one creature (the existing engine sums listener amounts).</summary>
    internal static int DoomFor(in FlashBattle battle, byte owner)
    {
        if (!FlashRegistry.TryPower("DOOM_POWER", out ushort id))
            return 0;
        return FlashKernel.GetAmount(battle, owner, id);
    }
}

/// <summary>
/// <c>NecroMasteryPower</c>: HP lost by the pet deals that much times the layer count to every hittable
/// enemy (<c>AfterCurrentHpChangedMirrors.HandleNecroMasteryPower</c>).
/// </summary>
[FlashContent("NECRO_MASTERY_POWER")]
public sealed class NecroMasteryPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCurrentHpChanged(ref FlashExec x, FlashListener self, byte creature, decimal delta)
    {
        if (delta >= 0m || creature != FlashLimits.OstyOwner || self.Owner != FlashLimits.PlayerOwner)
            return;
        decimal amount = -delta * x.B.Powers[self.Index].Amount;
        FlashCardEffects.DamageAllEnemies(ref x, amount,
            FlashValueProps.Unblockable | FlashValueProps.Unpowered, self.Owner);
    }
}

/// <summary>
/// <c>CalcifyPower</c>: adds its amount to a powered attack made by the owner's Osty
/// (<c>ModifyDamageMirrors</c> invokes <c>CalcifyPower.ModifyDamageAdditive</c>).
/// </summary>
[FlashContent("CALCIFY_POWER")]
public sealed class CalcifyPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self, in FlashDamageContext damage,
        decimal amount)
    {
        if (damage.Dealer != FlashLimits.OstyOwner || self.Owner != FlashLimits.PlayerOwner
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
[FlashContent("COUNTDOWN_POWER")]
public sealed class CountdownPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void GlobalAfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates || self.Owner != FlashLimits.PlayerOwner || x.B.Powers[self.Index].Amount <= 0)
            return;
        Span<byte> candidates = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (FlashKernel.IsHittable(x.B, enemy))
                candidates[count++] = enemy;
        }
        if (count == 0)
            return;
        byte target = candidates[x.B.RngStream(FlashRngStream.CombatTargets).NextInt(0, count)];
        FlashKernel.ApplyPower(ref x, target, DoomId(), x.B.Powers[self.Index].Amount, self.Owner);
    }

    internal static ushort DoomId()
        => FlashRegistry.TryPower("DOOM_POWER", out ushort id)
            ? id
            : throw new FlashUnsupportedException("倒计时需要 DoomPower 的 Flash 定义。");
}

/// <summary>
/// <c>NeurosurgePower</c>: at its owner's side turn start, apply its amount as Doom to its owner
/// (<c>PersistentPowerSupport.TriggerOwnerAfterSideTurnStart</c>, the NEUROSURGE case; the kernel's
/// fixed owner sequence calls it).
/// </summary>
[FlashContent("NEUROSURGE_POWER")]
public sealed class NeurosurgePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        FlashKernel.ApplyPower(ref x, self.Owner, CountdownPower.DoomId(), amount, self.Owner);
    }
}

/// <summary>
/// <c>HauntPower</c>: after its owner plays a Soul, deal its amount to a random hittable enemy as
/// unblockable unpowered damage with no dealer (<c>AfterCardPlayedMirrors.HandleHauntPower</c>).
/// </summary>
[FlashContent("HAUNT_POWER")]
public sealed class HauntPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Owner != FlashLimits.PlayerOwner
            || !FlashRegistry.TryCard("SOUL", out ushort soul) || play.Card.Id != soul)
        {
            return;
        }
        Span<byte> targets = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (FlashKernel.IsHittable(x.B, enemy))
                targets[count++] = enemy;
        }
        if (count == 0)
            return;
        byte target = targets[x.B.RngStream(FlashRngStream.CombatTargets).NextInt(0, count)];
        Span<byte> one = stackalloc byte[1];
        one[0] = target;
        FlashKernel.Damage(ref x, one, x.B.Powers[self.Index].Amount,
            FlashValueProps.Unblockable | FlashValueProps.Unpowered, FlashLimits.NoOwner,
            hasCardSource: false, default);
    }
}

/// <summary>
/// <c>LethalityPower</c>: multiplies the first attack card played each turn by 1 + amount / 100
/// (<c>ModifyDamageMirrors.HandleLethalityPower</c>). The attack's play index must be zero and the
/// card's owner must be the power owner.
/// </summary>
[FlashContent("LETHALITY_POWER")]
public sealed class LethalityPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self, in FlashDamageContext damage,
        decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource || self.Owner != FlashLimits.PlayerOwner)
            return 1m;
        bool inPlay = x.EffectDepth > 0;
        if (inPlay && x.PlayIndex > 0)
            return 1m;
        int started = x.B.Counter(FlashCounter.AttackPlayStartsThisTurn);
        return started <= (inPlay ? 1 : 0)
            ? 1m + (decimal)x.B.Powers[self.Index].Amount / 100m
            : 1m;
    }
}

/// <summary>
/// <c>ShroudPower</c>: gain block equal to its amount whenever its owner applies Doom
/// (<c>PowerLifecycleSupport.ResolvePowerAmountChanges</c>, the Shroud case).
/// </summary>
[FlashContent("SHROUD_POWER")]
public sealed class ShroudPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterPowerAmountChanged(ref FlashExec x, FlashListener self, ushort powerId, int delta,
        byte applier, byte target)
    {
        if (applier != self.Owner || powerId != CountdownPower.DoomId())
            return;
        FlashKernel.GainBlock(ref x, self.Owner, x.B.Powers[self.Index].Amount,
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>SicEmPower</c>: when the applier's Osty damages the owner, summon that much HP for the pet
/// (<c>AfterDamageGivenMirrors.HandleSicEmPower</c>); removed at its owner's side turn end
/// (<c>EndTurnPowerSupport.TriggerBatch048</c>).
/// </summary>
[FlashContent("SIC_EM_POWER")]
public sealed class SicEmPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterDamageGiven(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (dealer == FlashLimits.OstyOwner && result.Receiver == self.Owner
            && self.Owner != FlashLimits.PlayerOwner
            && x.B.Powers[self.Index].Amount > 0)
        {
            FlashKernel.SummonOsty(ref x, x.B.Powers[self.Index].Amount);
        }
    }

    public override void EndTurnBatch048(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// <c>OblivionPower</c>: records its amount at each play start (the kernel's transient play stack, the
/// existing engine's <c>CardPlayPairPredictionState</c>) and applies that amount as Doom after the play;
/// removed at the player's side turn end (<c>EndTurnPowerSupport.TriggerBatch048</c>).
/// </summary>
[FlashContent("OBLIVION_POWER")]
public sealed class OblivionPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref readonly FlashPower power = ref x.B.Powers[self.Index];
        if (power.Applier != FlashLimits.PlayerOwner)
            return;
        x.PushCardPlayPair(FlashCardPlayPairKind.OblivionDoom, self.Owner, power.Id, power.Amount);
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref readonly FlashPower power = ref x.B.Powers[self.Index];
        if (!x.TryTakeCardPlayPair(FlashCardPlayPairKind.OblivionDoom, self.Owner, power.Id, out int amount))
            return;
        FlashKernel.ApplyPower(ref x, self.Owner, CountdownPower.DoomId(), amount, power.Applier);
    }

    public override void EndTurnBatch048(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (side == FlashSide.Player)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// <c>VeilpiercerPower</c>: Ethereal cards in hand or in play cost zero (the Late cost pass) and
/// decrement the power when played (<c>ModifyEnergyCostInCombatMirrors</c>,
/// <c>BeforeCardPlayedMirrors.HandleVeilpiercerPower</c>).
/// </summary>
[FlashContent("VEILPIERCER_POWER")]
public sealed class VeilpiercerPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombatLate(in FlashBattle battle, FlashRules rules,
        FlashListener self, in FlashCard card, FlashPile pile, decimal amount)
    {
        if (self.Owner != FlashLimits.PlayerOwner || !card.HasKeyword(FlashKeywords.Ethereal)
            || pile is not (FlashPile.Hand or FlashPile.Play))
        {
            return amount;
        }
        return 0m;
    }

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref readonly FlashPower power = ref x.B.Powers[self.Index];
        if (self.Owner != FlashLimits.PlayerOwner || power.Amount <= 0
            || !play.Card.HasKeyword(FlashKeywords.Ethereal))
        {
            return;
        }
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, power.Id, power.Amount - 1);
    }
}

/// <summary>
/// <c>DebilitatePower</c>: the owner takes double Vulnerable damage and deals half Weak damage
/// (<c>ModifyDamageMirrors</c> folds this into the Vulnerable and Weak multipliers); decrements at its
/// owner's side turn end (<c>EndTurnPowerSupport.TriggerBatch048</c>).
/// </summary>
[FlashContent("DEBILITATE_POWER")]
public sealed class DebilitatePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void EndTurnBatch048(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id,
                x.B.Powers[self.Index].Amount - 1);
    }
}

/// <summary>
/// <c>HangPower</c>: the next Hang card played against its owner deals damage times this amount
/// (<c>HangPower.ModifyDamageMultiplicative</c> through <c>ModifyDamageMirrors</c>).
/// </summary>
[FlashContent("HANG_POWER")]
public sealed class HangPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self, in FlashDamageContext damage,
        decimal amount)
    {
        if (damage.Target != self.Owner || !damage.HasCardSource
            || !FlashRegistry.TryCard("HANG", out ushort hang) || damage.CardSource.Id != hang)
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
[FlashContent("BORROWED_TIME_POWER")]
public sealed class BorrowedTimePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombat(in FlashBattle battle, FlashRules rules,
        FlashListener self, in FlashCard card, FlashPile pile, decimal amount)
        => self.Owner == FlashLimits.PlayerOwner ? amount + battle.Powers[self.Index].Amount : amount;
}

/// <summary>
/// <c>FriendshipPower</c>: each layer adds one maximum energy (<c>FriendshipPower.ModifyMaxEnergy</c>
/// through <c>PersistentPowerSupport.GetModifiedMaxEnergy</c>).
/// </summary>
[FlashContent("FRIENDSHIP_POWER")]
public sealed class FriendshipPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => self.Owner == FlashLimits.PlayerOwner ? amount + x.B.Powers[self.Index].Amount : amount;
}

/// <summary>
/// <c>EnfeeblingTouchPower</c> (a negative <c>TemporaryStrengthPower</c>): application changes Strength
/// by -amount and its owner's side turn end retires it, restoring Strength.
/// </summary>
[FlashContent("ENFEEBLING_TOUCH_POWER")]
public sealed class EnfeeblingTouchPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsTemporaryPower => true;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.RetireTemporaryStrength(ref x, self);
    }
}

/// <summary>
/// <c>SummonNextTurnPower</c>: at the player's turn start, summon that much HP for Osty and consume the
/// power (<c>TurnStartPowerSupport</c>'s SUMMON_NEXT_TURN case).
/// </summary>
[FlashContent("SUMMON_NEXT_TURN_POWER")]
public sealed class SummonNextTurnPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        FlashKernel.SummonOsty(ref x, amount);
        if (!x.NeedsChoice)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// <c>SleightOfFleshPower</c>: when its owner applies a non-temporary debuff to an enemy, deal its amount
/// to that enemy as unpowered damage (<c>PowerLifecycleSupport.ResolvePowerAmountChanges</c>).
/// </summary>
[FlashContent("SLEIGHT_OF_FLESH_POWER")]
public sealed class SleightOfFleshPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterPowerAmountChanged(ref FlashExec x, FlashListener self, ushort powerId, int delta,
        byte applier, byte target)
    {
        if (delta == 0 || applier != self.Owner || !FlashKernel.IsEnemyOwner(target)
            || FlashRegistry.Power(powerId).TypeForAmount(delta) != FlashPowerType.Debuff
            || FlashRegistry.Power(powerId).IsTemporaryPower)
        {
            return;
        }
        Span<byte> one = stackalloc byte[1];
        one[0] = target;
        FlashKernel.Damage(ref x, one, x.B.Powers[self.Index].Amount, FlashValueProps.Unpowered, self.Owner,
            hasCardSource: false, default);
    }
}

/// <summary>
/// <c>SpiritOfAshPower</c>: gain its amount as unpowered block before its owner plays an Ethereal card
/// (<c>BeforeCardPlayedMirrors.HandleSpiritOfAshPower</c>).
/// </summary>
[FlashContent("SPIRIT_OF_ASH_POWER")]
public sealed class SpiritOfAshPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Owner != FlashLimits.PlayerOwner
            || !FlashKernel.HasKeyword(x.B, play.Card, FlashKeywords.Ethereal))
        {
            return;
        }
        FlashKernel.GainBlock(ref x, self.Owner, x.B.Powers[self.Index].Amount, FlashValueProps.Unpowered,
            fromCard: false);
    }
}

/// <summary>
/// <c>DevourLifePower</c>: after its owner plays a Soul, summon its amount of Osty HP
/// (<c>AfterCardPlayedMirrors.HandleDevourLifePower</c>).
/// </summary>
[FlashContent("DEVOUR_LIFE_POWER")]
public sealed class DevourLifePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Owner != FlashLimits.PlayerOwner
            || !FlashRegistry.TryCard("SOUL", out ushort soul) || play.Card.Id != soul)
        {
            return;
        }
        FlashKernel.SummonOsty(ref x, x.B.Powers[self.Index].Amount);
    }
}

/// <summary>
/// <c>DemesnePower</c>: its amount adds both hand draw and maximum energy
/// (<c>PersistentPowerSupport.GetModifiedHandDraw</c>/<c>GetModifiedMaxEnergy</c> invoke the native methods).
/// </summary>
[FlashContent("DEMESNE_POWER")]
public sealed class DemesnePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
        => self.Owner == FlashLimits.PlayerOwner ? amount + x.B.Powers[self.Index].Amount : amount;

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => self.Owner == FlashLimits.PlayerOwner ? amount + x.B.Powers[self.Index].Amount : amount;
}
