using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// <c>PoisonPower</c>: the counter is triggered by the kernel's <c>TriggerPoison</c> (enemy side start
/// for enemies, enemy side end for the player), matching <c>CorePowerSupport.TriggerPoison</c>. No hook
/// of its own runs there.
/// </summary>
[BlitzContent("POISON_POWER")]
public sealed class PoisonPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;
}

/// <summary>
/// <c>AccelerantPower</c>: read only by the poison trigger (one extra hit per layer, capped at the
/// poison amount in <c>TriggerPoison</c>).
/// </summary>
[BlitzContent("ACCELERANT_POWER")]
public sealed class AccelerantPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;
}

/// <summary>
/// <c>DrawCardsNextTurnPower</c>: <c>ModifyHandDraw</c> adds the amount once it was snapshotted at a
/// turn start (<c>AmountOnTurnStart != 0</c>); <c>BaseSideTurnStart</c> clears it
/// (<c>SimulatedCombatState.TriggerBaseSideTurnStart</c>). The not-yet-snapshotted case is added by the
/// kernel's <c>ConsumeModifiedHandDraw</c> equivalent.
/// </summary>
[BlitzContent("DRAW_CARDS_NEXT_TURN_POWER")]
public sealed class DrawCardsNextTurnPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool UsesAmountOnTurnStart => true;

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
    {
        ref readonly BlitzPower power = ref x.B.Powers[self.Index];
        if (power.AmountOnTurnStart != 0 && power.Amount > 0)
        {
            return amount + power.Amount;
        }
        return amount;
    }

    public override void BaseSideTurnStart(ref BlitzExec x, BlitzListener self, bool decrementPlating)
    {
        ref readonly BlitzPower power = ref x.B.Powers[self.Index];
        if (power.Amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, power.Id, 0);
    }
}

/// <summary>
/// <c>EnergyNextTurnPower</c>: consumed directly by the turn-start energy reset
/// (<c>SimulatedCombatState.ConsumeEnergyNextTurn</c>), so its native AfterEnergyReset never runs.
/// </summary>
[BlitzContent("ENERGY_NEXT_TURN_POWER")]
public sealed class EnergyNextTurnPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;
}

/// <summary>
/// <c>DoubleDamagePower</c>: doubles powered card attacks of its owner (or its owner's pets), and
/// decrements at its owner's side turn end (<c>CorePowerSupport.TriggerPlayerRegularSideTurnEndEffects</c>
/// for the player, <c>TriggerEnemySideTurnEndEffects</c> for enemies).
/// </summary>
[BlitzContent("DOUBLE_DAMAGE_POWER")]
public sealed class DoubleDamagePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage,
        decimal amount)
    {
        bool pet = self.Owner == BlitzLimits.PlayerOwner && damage.Dealer == BlitzLimits.OstyOwner;
        if (damage.Dealer != self.Owner && !pet)
            return 1m;
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource)
            return 1m;
        return 2m;
    }

    public override void PlayerSideTurnEnd(ref BlitzExec x, BlitzListener self)
        => Tick(ref x, self);

    public override void EnemySideTurnEnd(ref BlitzExec x, BlitzListener self)
        => Tick(ref x, self);

    private static void Tick(ref BlitzExec x, BlitzListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, amount - 1);
    }
}

/// <summary>
/// <c>BlurPower</c>: <c>ShouldClearBlock</c> keeps the owner's block at the side start, then the power
/// decrements there (<c>SimulatedCombatState.TriggerBaseSideTurnStart</c>'s <c>TickDuration&lt;BlurPower&gt;</c>).
/// </summary>
[BlitzContent("BLUR_POWER")]
public sealed class BlurPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool ShouldClearBlock(ref BlitzExec x, BlitzListener self, byte owner)
    {
        if (owner == self.Owner)
        {
            return false;
        }
        return true;
    }

    public override void BaseSideTurnStart(ref BlitzExec x, BlitzListener self, bool decrementPlating)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, amount - 1);
    }
}

/// <summary>
/// <c>ShadowStepPower</c>: at the owner's side turn start it applies DoubleDamage with its amount and
/// clears itself (<c>PersistentPowerSupport.TriggerOwnerAfterSideTurnStart</c>'s SHADOW_STEP_POWER case).
/// </summary>
[BlitzContent("SHADOW_STEP_POWER")]
public sealed class ShadowStepPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref BlitzExec x, BlitzListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        BlitzKernel.ApplyPower(ref x, self.Owner, BlitzRegistry.PowerId<DoubleDamagePower>(), amount, self.Owner);
        BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// <c>AccuracyPower</c>: <c>ModifyDamageAdditive</c> adds the amount to powered attacks from a Shiv
/// (<c>AccuracyPower.ModifyDamageAdditive</c>; the card is read through the damage source).
/// </summary>
[BlitzContent("ACCURACY_POWER")]
public sealed class AccuracyPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyDamageAdditive(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage,
        decimal amount)
    {
        if (damage.Dealer != self.Owner || !damage.Props.IsPoweredAttack() || !damage.HasCardSource)
            return 0m;
        if ((x.R.Card(damage.CardSource.Id, damage.CardSource.Upgrade).Tags & BlitzCardTag.Shiv) == 0)
            return 0m;
        return x.B.Powers[self.Index].Amount;
    }
}

/// <summary>
/// <c>AfterimagePower</c>: <c>BeforeCardPlayed</c> snapshots the amount per play, and
/// <c>AfterCardPlayed</c> gains that much Unpowered block. The snapshot is the existing engine's
/// <c>CardPlayPairPredictionState</c>, carried on the transient play stack.
/// </summary>
[BlitzContent("AFTERIMAGE_POWER")]
public sealed class AfterimagePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        x.PushCardPlayPair(BlitzCardPlayPairKind.AfterimageBlock, self.Owner, x.B.Powers[self.Index].Id,
            x.B.Powers[self.Index].Amount);
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (!x.TryTakeCardPlayPair(BlitzCardPlayPairKind.AfterimageBlock, self.Owner,
                x.B.Powers[self.Index].Id, out int amount))
        {
            return;
        }
        if (amount > 0)
        {
            BlitzKernel.GainBlock(ref x, self.Owner, amount, BlitzValueProps.Unpowered, fromCard: false);
        }
    }
}

/// <summary>
/// <c>InfiniteBladesPower</c>: <c>BeforeHandDraw</c> adds its amount of Shivs to hand
/// (<c>SimulatedCombatState.GenerateTurnStartPowerCards</c>).
/// </summary>
[BlitzContent("INFINITE_BLADES_POWER")]
public sealed class InfiniteBladesPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SHIV", 0)];

    public override void BeforeHandDraw(ref BlitzExec x, BlitzListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0 || self.Owner != BlitzLimits.PlayerOwner)
            return;
        if (!BlitzRegistry.TryCard("SHIV", out ushort shiv))
            throw new BlitzUnsupportedException("无限刀刃需要 SHIV 的 Blitz 定义。");
        for (int index = 0; index < amount; index++)
        {
            if (!BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CreateCard(x.R, shiv), BlitzPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>MasterPlannerPower</c>: <c>AfterCardPlayed</c> adds the Sly keyword to a Skill its owner played
/// (<c>AfterCardPlayedMirrors.HandleMasterPlannerPower</c>). Blitz has no card identity, so the keyword
/// is written to the card's play-pile entry, which becomes the discard-pile entry.
/// </summary>
[BlitzContent("MASTER_PLANNER_POWER")]
public sealed class MasterPlannerPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Skill)
            return;
        if ((uint)play.Position >= (uint)x.B.CardCount)
            throw new BlitzUnsupportedException("策划大师找不到刚打出的技能牌。");
        x.B.Cards[play.Position].Keywords |= BlitzKeywords.Sly;
    }
}

/// <summary>
/// <c>FanOfKnivesPower</c>: a marker read by <c>Shiv.TargetType</c> (all enemies while present). The
/// amount is not read.
/// </summary>
[BlitzContent("FAN_OF_KNIVES_POWER")]
public sealed class FanOfKnivesPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;
}

/// <summary><c>WellLaidPlansPower</c>: <c>ShouldFlush</c> keeps the whole hand at the turn end.</summary>
[BlitzContent("WELL_LAID_PLANS_POWER")]
public sealed class WellLaidPlansPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override bool ShouldFlush(ref BlitzExec x, BlitzListener self) => self.Owner != BlitzLimits.PlayerOwner;
}

/// <summary>
/// <c>SpeedsterPower</c>: <c>AfterCardDrawn</c> for a draw outside the hand draw deals its amount
/// Unpowered to every hittable enemy (<c>AfterCardDrawnMirrors.HandleSpeedsterPower</c>).
/// </summary>
[BlitzContent("SPEEDSTER_POWER")]
public sealed class SpeedsterPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardDrawn(ref BlitzExec x, BlitzListener self, int drawnPosition, bool fromHandDraw)
    {
        if (fromHandDraw || self.Owner != BlitzLimits.PlayerOwner || x.B.Side != BlitzSide.Player)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        BlitzCardEffects.DamageAllEnemies(ref x, amount, BlitzValueProps.Unpowered, self.Owner);
    }
}

/// <summary>
/// <c>EnvenomPower</c>: <c>AfterDamageGiven</c> applies Poison to the target of a powered attack that
/// dealt unblocked damage (<c>AfterDamageGivenMirrors.HandleEnvenomPower</c>).
/// </summary>
[BlitzContent("ENVENOM_POWER")]
public sealed class EnvenomPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterDamageGiven(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (dealer != self.Owner || !result.Props.IsPoweredAttack() || result.UnblockedDamage <= 0)
            return;
        BlitzKernel.ApplyPowerWithoutCardSource(ref x, result.Receiver, BlitzRegistry.PowerId<PoisonPower>(),
            x.B.Powers[self.Index].Amount, self.Owner);
    }
}

/// <summary>
/// <c>TrackingPower</c>: <c>ModifyDamageMultiplicative</c> makes a powered card attack deal 1 + amount%
/// against a Weak target (<c>ModifyDamageMirrors.HandleTrackingPower</c>).
/// </summary>
[BlitzContent("TRACKING_POWER")]
public sealed class TrackingPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(ref BlitzExec x, BlitzListener self,
        in BlitzDamageContext damage, decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource || damage.Target == BlitzLimits.NoOwner)
            return 1m;
        bool pet = self.Owner == BlitzLimits.PlayerOwner && damage.Dealer == BlitzLimits.OstyOwner;
        if (damage.Dealer != self.Owner && !pet)
            return 1m;
        return BlitzKernel.GetAmount(x.B, damage.Target, BlitzRegistry.PowerId<WeakPower>()) > 0
            ? 1m + (decimal)x.B.Powers[self.Index].Amount / 100m
            : 1m;
    }
}

/// <summary>
/// <c>StranglePower</c>: <c>BeforeCardPlayed</c> snapshots the amount for the applier's card plays and
/// <c>AfterCardPlayed</c> deals it to the owner (the power's own application happens after its play's
/// snapshot, so it never triggers on itself). <c>AfterSideTurnEnd</c> removes it when the owner
/// participates (<c>EndTurnPowerSupport.TriggerBatch048</c>).
/// </summary>
[BlitzContent("STRANGLE_POWER")]
public sealed class StranglePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        ref readonly BlitzPower power = ref x.B.Powers[self.Index];
        if (power.Applier != BlitzLimits.PlayerOwner)
            return;
        x.PushCardPlayPair(BlitzCardPlayPairKind.StrangleDamage, self.Owner, power.Id, power.Amount);
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if ((uint)self.Index >= (uint)x.B.PowerCount)
            return;
        ushort id = x.B.Powers[self.Index].Id;
        if (!x.TryTakeCardPlayPair(BlitzCardPlayPairKind.StrangleDamage, self.Owner, id, out int amount))
            return;
        Span<byte> target = stackalloc byte[1];
        target[0] = self.Owner;
        BlitzKernel.Damage(ref x, target, amount,
            BlitzValueProps.Unblockable | BlitzValueProps.Unpowered, BlitzLimits.NoOwner,
            hasCardSource: false, default);
    }

    public override void EndTurnBatch048(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// <c>NoxiousFumesPower</c>: at its owner's side turn start, applies its amount of Poison to every
/// hittable enemy, resolving the queued amount changes after each
/// (<c>PersistentPowerSupport.TriggerOwnerAfterSideTurnStart</c>'s NOXIOUS_FUMES_POWER case).
/// </summary>
[BlitzContent("NOXIOUS_FUMES_POWER")]
public sealed class NoxiousFumesPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref BlitzExec x, BlitzListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        ushort poison = BlitzRegistry.PowerId<PoisonPower>();
        // Noxious Fumes resolves after each application; a Sleight of Flesh death compacts the live
        // roster, so visit the same snapshot the native loop sees.
        Span<byte> owners = stackalloc byte[BlitzLimits.Enemies];
        int rosterCount = BlitzKernel.RosterOwners(x.B, owners);
        for (int position = 0; position < rosterCount; position++)
        {
            byte enemy = owners[position];
            if (!BlitzKernel.IsHittable(x.B, enemy))
                continue;
            BlitzKernel.ApplyPower(ref x, enemy, poison, amount, self.Owner);
            BlitzKernel.ResolvePowerAmountChanges(ref x);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>CorrosiveWavePower</c>: every card drawn by its owner applies the power amount of Poison to each
/// hittable enemy (<c>AfterCardDrawnMirrors.HandleCorrosiveWavePower</c>); it is removed at the owner's
/// side turn end (<c>EndTurnPowerSupport.TriggerRegular</c>'s CORROSIVE_WAVE_POWER case).
/// </summary>
[BlitzContent("CORROSIVE_WAVE_POWER")]
public sealed class CorrosiveWavePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardDrawn(ref BlitzExec x, BlitzListener self, int drawnPosition, bool fromHandDraw)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        ushort poison = BlitzRegistry.PowerId<PoisonPower>();
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (!BlitzKernel.IsHittable(x.B, enemy))
                continue;
            BlitzKernel.ApplyPower(ref x, enemy, poison, amount, self.Owner);
            if (x.NeedsChoice)
                return;
        }
    }

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// <c>FreeSkillPower</c> (Pounce): a Skill in hand or in the play pile costs 0 in the late cost pass,
/// and playing a Skill decrements the power (<c>ModifyEnergyCostInCombatMirrors.HandleFreeSkillPower</c>,
/// <c>BeforeCardPlayedMirrors.HandleFreeSkillPower</c>).
/// </summary>
[BlitzContent("FREE_SKILL_POWER")]
public sealed class FreeSkillPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombatLate(in BlitzBattle battle, BlitzRules rules,
        BlitzListener self, in BlitzCard card, BlitzPile pile, decimal amount)
    {
        if (self.Owner != BlitzLimits.PlayerOwner || pile is not (BlitzPile.Hand or BlitzPile.Play))
            return amount;
        return BlitzRegistry.Card(card.Id).CardType(battle, rules, card) == BlitzCardType.Skill ? 0m : amount;
    }

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Skill)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, amount - 1);
    }
}

/// <summary>
/// <c>PiercingWailPower</c> (a negative <c>TemporaryStrengthPower</c>): applied through
/// <c>ApplyTemporaryStrengthLoss</c> and retired at its owner's side turn end while restoring Strength
/// (<c>EndTurnPowerSupport.TriggerRegular</c>'s TemporaryStrengthPower case).
/// </summary>
[BlitzContent("PIERCING_WAIL_POWER")]
public sealed class PiercingWailPower : BlitzPowerDef
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
