namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Silent powers. Each definition's conditions are copied from the existing engine path named in its
// comment; the decompiled model is used only to confirm constants.

/// <summary>
/// <c>PoisonPower</c>: the counter is triggered by the kernel's <c>TriggerPoison</c> (enemy side start
/// for enemies, enemy side end for the player), matching <c>CorePowerSupport.TriggerPoison</c>. No hook
/// of its own runs there.
/// </summary>
[FlashContent("POISON_POWER")]
public sealed class PoisonPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;
}

/// <summary>
/// <c>AccelerantPower</c>: read only by the poison trigger (one extra hit per layer, capped at the
/// poison amount in <c>TriggerPoison</c>).
/// </summary>
[FlashContent("ACCELERANT_POWER")]
public sealed class AccelerantPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;
}

/// <summary>
/// <c>DrawCardsNextTurnPower</c>: <c>ModifyHandDraw</c> adds the amount once it was snapshotted at a
/// turn start (<c>AmountOnTurnStart != 0</c>); <c>BaseSideTurnStart</c> clears it
/// (<c>SimulatedCombatState.TriggerBaseSideTurnStart</c>). The not-yet-snapshotted case is added by the
/// kernel's <c>ConsumeModifiedHandDraw</c> equivalent.
/// </summary>
[FlashContent("DRAW_CARDS_NEXT_TURN_POWER")]
public sealed class DrawCardsNextTurnPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool UsesAmountOnTurnStart => true;

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
    {
        ref readonly FlashPower power = ref x.B.Powers[self.Index];
        if (power.AmountOnTurnStart != 0 && power.Amount > 0)
        {
            return amount + power.Amount;
        }
        return amount;
    }

    public override void BaseSideTurnStart(ref FlashExec x, FlashListener self, bool decrementPlating)
    {
        ref readonly FlashPower power = ref x.B.Powers[self.Index];
        if (power.Amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, power.Id, 0);
    }
}

/// <summary>
/// <c>EnergyNextTurnPower</c>: consumed directly by the turn-start energy reset
/// (<c>SimulatedCombatState.ConsumeEnergyNextTurn</c>), so its native AfterEnergyReset never runs.
/// </summary>
[FlashContent("ENERGY_NEXT_TURN_POWER")]
public sealed class EnergyNextTurnPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;
}

/// <summary>
/// <c>DoubleDamagePower</c>: doubles powered card attacks of its owner (or its owner's pets), and
/// decrements at its owner's side turn end (<c>CorePowerSupport.TriggerPlayerRegularSideTurnEndEffects</c>
/// for the player, <c>TriggerEnemySideTurnEndEffects</c> for enemies).
/// </summary>
[FlashContent("DOUBLE_DAMAGE_POWER")]
public sealed class DoubleDamagePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self, in FlashDamageContext damage,
        decimal amount)
    {
        bool pet = self.Owner == FlashLimits.PlayerOwner && damage.Dealer == FlashLimits.OstyOwner;
        if (damage.Dealer != self.Owner && !pet)
            return 1m;
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource)
            return 1m;
        return 2m;
    }

    public override void PlayerSideTurnEnd(ref FlashExec x, FlashListener self)
        => Tick(ref x, self);

    public override void EnemySideTurnEnd(ref FlashExec x, FlashListener self)
        => Tick(ref x, self);

    private static void Tick(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, amount - 1);
    }
}

/// <summary>
/// <c>BlurPower</c>: <c>ShouldClearBlock</c> keeps the owner's block at the side start, then the power
/// decrements there (<c>SimulatedCombatState.TriggerBaseSideTurnStart</c>'s <c>TickDuration&lt;BlurPower&gt;</c>).
/// </summary>
[FlashContent("BLUR_POWER")]
public sealed class BlurPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool ShouldClearBlock(ref FlashExec x, FlashListener self, byte owner)
    {
        if (owner == self.Owner)
        {
            return false;
        }
        return true;
    }

    public override void BaseSideTurnStart(ref FlashExec x, FlashListener self, bool decrementPlating)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, amount - 1);
    }
}

/// <summary>
/// <c>ShadowStepPower</c>: at the owner's side turn start it applies DoubleDamage with its amount and
/// clears itself (<c>PersistentPowerSupport.TriggerOwnerAfterSideTurnStart</c>'s SHADOW_STEP_POWER case).
/// </summary>
[FlashContent("SHADOW_STEP_POWER")]
public sealed class ShadowStepPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<DoubleDamagePower>(), amount, self.Owner);
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// <c>AccuracyPower</c>: <c>ModifyDamageAdditive</c> adds the amount to powered attacks from a Shiv
/// (<c>AccuracyPower.ModifyDamageAdditive</c>; the card is read through the damage source).
/// </summary>
[FlashContent("ACCURACY_POWER")]
public sealed class AccuracyPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self, in FlashDamageContext damage,
        decimal amount)
    {
        if (damage.Dealer != self.Owner || !damage.Props.IsPoweredAttack() || !damage.HasCardSource)
            return 0m;
        if ((x.R.Card(damage.CardSource.Id, damage.CardSource.Upgrade).Tags & FlashCardTag.Shiv) == 0)
            return 0m;
        return x.B.Powers[self.Index].Amount;
    }
}

/// <summary>
/// <c>AfterimagePower</c>: <c>BeforeCardPlayed</c> snapshots the amount per play, and
/// <c>AfterCardPlayed</c> gains that much Unpowered block. The snapshot is the existing engine's
/// <c>CardPlayPairPredictionState</c>, carried on the transient play stack.
/// </summary>
[FlashContent("AFTERIMAGE_POWER")]
public sealed class AfterimagePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        x.PushCardPlayPair(FlashCardPlayPairKind.AfterimageBlock, self.Owner, x.B.Powers[self.Index].Id,
            x.B.Powers[self.Index].Amount);
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (!x.TryTakeCardPlayPair(FlashCardPlayPairKind.AfterimageBlock, self.Owner,
                x.B.Powers[self.Index].Id, out int amount))
        {
            return;
        }
        if (amount > 0)
        {
            FlashKernel.GainBlock(ref x, self.Owner, amount, FlashValueProps.Unpowered, fromCard: false);
        }
    }
}

/// <summary>
/// <c>InfiniteBladesPower</c>: <c>BeforeHandDraw</c> adds its amount of Shivs to hand
/// (<c>SimulatedCombatState.GenerateTurnStartPowerCards</c>).
/// </summary>
[FlashContent("INFINITE_BLADES_POWER")]
public sealed class InfiniteBladesPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SHIV", 0)];

    public override void BeforeHandDraw(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0 || self.Owner != FlashLimits.PlayerOwner)
            return;
        if (!FlashRegistry.TryCard("SHIV", out ushort shiv))
            throw new FlashUnsupportedException("无限刀刃需要 SHIV 的 Flash 定义。");
        for (int index = 0; index < amount; index++)
        {
            if (!FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, shiv), FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>MasterPlannerPower</c>: <c>AfterCardPlayed</c> adds the Sly keyword to a Skill its owner played
/// (<c>AfterCardPlayedMirrors.HandleMasterPlannerPower</c>). Flash has no card identity, so the keyword
/// is written to the card's play-pile entry, which becomes the discard-pile entry.
/// </summary>
[FlashContent("MASTER_PLANNER_POWER")]
public sealed class MasterPlannerPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Skill)
            return;
        if ((uint)play.Position >= (uint)x.B.CardCount)
            throw new FlashUnsupportedException("策划大师找不到刚打出的技能牌。");
        x.B.Cards[play.Position].Keywords |= FlashKeywords.Sly;
    }
}

/// <summary>
/// <c>FanOfKnivesPower</c>: a marker read by <c>Shiv.TargetType</c> (all enemies while present). The
/// amount is not read.
/// </summary>
[FlashContent("FAN_OF_KNIVES_POWER")]
public sealed class FanOfKnivesPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;
}

/// <summary><c>WellLaidPlansPower</c>: <c>ShouldFlush</c> keeps the whole hand at the turn end.</summary>
[FlashContent("WELL_LAID_PLANS_POWER")]
public sealed class WellLaidPlansPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override bool ShouldFlush(ref FlashExec x, FlashListener self) => self.Owner != FlashLimits.PlayerOwner;
}

/// <summary>
/// <c>SpeedsterPower</c>: <c>AfterCardDrawn</c> for a draw outside the hand draw deals its amount
/// Unpowered to every hittable enemy (<c>AfterCardDrawnMirrors.HandleSpeedsterPower</c>).
/// </summary>
[FlashContent("SPEEDSTER_POWER")]
public sealed class SpeedsterPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        if (fromHandDraw || self.Owner != FlashLimits.PlayerOwner || x.B.Side != FlashSide.Player)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        FlashCardEffects.DamageAllEnemies(ref x, amount, FlashValueProps.Unpowered, self.Owner);
    }
}

/// <summary>
/// <c>EnvenomPower</c>: <c>AfterDamageGiven</c> applies Poison to the target of a powered attack that
/// dealt unblocked damage (<c>AfterDamageGivenMirrors.HandleEnvenomPower</c>).
/// </summary>
[FlashContent("ENVENOM_POWER")]
public sealed class EnvenomPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterDamageGiven(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (dealer != self.Owner || !result.Props.IsPoweredAttack() || result.UnblockedDamage <= 0)
            return;
        FlashKernel.ApplyPowerWithoutCardSource(ref x, result.Receiver, FlashRegistry.PowerId<PoisonPower>(),
            x.B.Powers[self.Index].Amount, self.Owner);
    }
}

/// <summary>
/// <c>TrackingPower</c>: <c>ModifyDamageMultiplicative</c> makes a powered card attack deal 1 + amount%
/// against a Weak target (<c>ModifyDamageMirrors.HandleTrackingPower</c>).
/// </summary>
[FlashContent("TRACKING_POWER")]
public sealed class TrackingPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self,
        in FlashDamageContext damage, decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource || damage.Target == FlashLimits.NoOwner)
            return 1m;
        bool pet = self.Owner == FlashLimits.PlayerOwner && damage.Dealer == FlashLimits.OstyOwner;
        if (damage.Dealer != self.Owner && !pet)
            return 1m;
        return FlashKernel.GetAmount(x.B, damage.Target, FlashRegistry.PowerId<WeakPower>()) > 0
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
[FlashContent("STRANGLE_POWER")]
public sealed class StranglePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref readonly FlashPower power = ref x.B.Powers[self.Index];
        if (power.Applier != FlashLimits.PlayerOwner)
            return;
        x.PushCardPlayPair(FlashCardPlayPairKind.StrangleDamage, self.Owner, power.Id, power.Amount);
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if ((uint)self.Index >= (uint)x.B.PowerCount)
            return;
        ushort id = x.B.Powers[self.Index].Id;
        if (!x.TryTakeCardPlayPair(FlashCardPlayPairKind.StrangleDamage, self.Owner, id, out int amount))
            return;
        Span<byte> target = stackalloc byte[1];
        target[0] = self.Owner;
        FlashKernel.Damage(ref x, target, amount,
            FlashValueProps.Unblockable | FlashValueProps.Unpowered, FlashLimits.NoOwner,
            hasCardSource: false, default);
    }

    public override void EndTurnBatch048(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// <c>NoxiousFumesPower</c>: at its owner's side turn start, applies its amount of Poison to every
/// hittable enemy, resolving the queued amount changes after each
/// (<c>PersistentPowerSupport.TriggerOwnerAfterSideTurnStart</c>'s NOXIOUS_FUMES_POWER case).
/// </summary>
[FlashContent("NOXIOUS_FUMES_POWER")]
public sealed class NoxiousFumesPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        ushort poison = FlashRegistry.PowerId<PoisonPower>();
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            FlashKernel.ApplyPower(ref x, enemy, poison, amount, self.Owner);
            FlashKernel.ResolvePowerAmountChanges(ref x);
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
[FlashContent("CORROSIVE_WAVE_POWER")]
public sealed class CorrosiveWavePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        ushort poison = FlashRegistry.PowerId<PoisonPower>();
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            FlashKernel.ApplyPower(ref x, enemy, poison, amount, self.Owner);
            if (x.NeedsChoice)
                return;
        }
    }

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, 0);
    }
}

/// <summary>
/// <c>FreeSkillPower</c> (Pounce): a Skill in hand or in the play pile costs 0 in the late cost pass,
/// and playing a Skill decrements the power (<c>ModifyEnergyCostInCombatMirrors.HandleFreeSkillPower</c>,
/// <c>BeforeCardPlayedMirrors.HandleFreeSkillPower</c>).
/// </summary>
[FlashContent("FREE_SKILL_POWER")]
public sealed class FreeSkillPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombatLate(in FlashBattle battle, FlashRules rules,
        FlashListener self, in FlashCard card, FlashPile pile, decimal amount)
    {
        if (self.Owner != FlashLimits.PlayerOwner || pile is not (FlashPile.Hand or FlashPile.Play))
            return amount;
        return rules.Card(card.Id, card.Upgrade).Type == FlashCardType.Skill ? 0m : amount;
    }

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Skill)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, amount - 1);
    }
}

/// <summary>
/// <c>PiercingWailPower</c> (a negative <c>TemporaryStrengthPower</c>): applied through
/// <c>ApplyTemporaryStrengthLoss</c> and retired at its owner's side turn end while restoring Strength
/// (<c>EndTurnPowerSupport.TriggerRegular</c>'s TemporaryStrengthPower case).
/// </summary>
[FlashContent("PIERCING_WAIL_POWER")]
public sealed class PiercingWailPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override string? InternallyAppliedPowerId => "STRENGTH_POWER";

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsTemporaryPower => true;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.RetireTemporaryStrength(ref x, self);
    }
}
