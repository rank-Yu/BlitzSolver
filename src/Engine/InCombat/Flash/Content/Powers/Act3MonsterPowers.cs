namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Third-act (Glory) monster powers. Each definition copies the existing engine's mirror for the same
// power; where the existing engine has no mirror the power stays inert so the differential stays
// equal. The native model is named in the comment for constants.

/// <summary>
/// Native <c>PaperCutsPower</c>: after its owner deals unblocked powered-attack damage to a player,
/// that player loses <c>Amount</c> max HP
/// (<c>AfterDamageGivenMirrors.HandlePaperCutsPower</c>, <c>AfterDamageGiven</c> pass).
/// </summary>
[FlashContent("PAPER_CUTS_POWER")]
public sealed class PaperCutsPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterDamageGiven(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (dealer != self.Owner
            || result.Receiver != FlashLimits.PlayerOwner
            || !result.Props.IsPoweredAttack()
            || result.UnblockedDamage <= 0)
        {
            return;
        }
        FlashKernel.LoseMaxHp(ref x, FlashLimits.PlayerOwner, x.B.Powers[self.Index].Amount);
    }
}

/// <summary>
/// Native <c>RampartPower</c>: at the player side turn start it grants its amount as Unpowered Block
/// to every living Turret Operator (<c>PersistentPowerSupport.TriggerRampart</c>).
/// </summary>
[FlashContent("RAMPART_POWER")]
public sealed class RampartPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void Rampart(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0 || !FlashRegistry.TryMonster("TURRET_OPERATOR", out ushort turret))
            return;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (x.B.Enemy(owner).MonsterId == turret && x.B.Creature(owner).IsAlive)
            {
                FlashKernel.GainBlock(ref x, owner, amount, FlashValueProps.Unpowered, fromCard: false);
                if (x.NeedsChoice)
                    return;
            }
        }
    }
}

/// <summary>
/// Native <c>SoarPower</c>: multiplies powered-attack damage dealt to its owner by
/// <c>DamageDecrease / 100</c>. The existing engine leaves Soar unregistered in
/// <c>ModifyDamageMirrors</c> and therefore falls back to the native model method.
/// </summary>
[FlashContent("SOAR_POWER")]
public sealed class SoarPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override IReadOnlyList<string> VarNames => ["DamageDecrease"];

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self, in FlashDamageContext damage, decimal amount)
    {
        if (damage.Target != self.Owner || !damage.Props.IsPoweredAttack())
            return 1m;
        return x.R.PowerVar(Id, 0) / 100m;
    }
}

/// <summary>
/// Native <c>GalvanicPower</c>: after a Galvanized card is played, the <b>card's owner</b> takes
/// <c>Amount</c> Unpowered damage (<c>AfterCardPlayedMirrors.HandleGalvanicPower</c> damages
/// <c>context.PreviewCard.Owner.Creature</c>, not the power's owner; the Globe Head carries the power
/// while the player plays the card). Every card in a Flash battle is the player's, so the target is
/// the player owner. Its <c>AfterCardEnteredCombat</c> is mirrored by
/// <c>FlashKernel.NormalizeCardAfflictions</c> (newly entered Power cards get Galvanized with the
/// power's amount).
/// </summary>
[FlashContent("GALVANIC_POWER")]
public sealed class GalvanicPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (play.Card.AfflictionId == 0
            || play.Card.AfflictionId - 1 != FlashRegistry.AfflictionId<Afflictions.GalvanizedAffliction>())
        {
            return;
        }
        Span<byte> target = stackalloc byte[1];
        target[0] = FlashLimits.PlayerOwner;
        FlashKernel.Damage(ref x, target, x.B.Powers[self.Index].Amount,
            FlashValueProps.Unpowered, FlashLimits.NoOwner, hasCardSource: false, default);
    }
}

/// <summary>
/// Native <c>ChainsOfBindingPower</c>: on the player's card draws during the player's side this turn
/// it afflicts up to <c>Amount</c> cards with Bound (<c>AfterCardDrawnMirrors</c>), a Bound card's
/// play marks <c>boundCardPlayed</c> (<c>BeforeCardPlayedMirrors</c>) which makes every Bound card
/// unplayable until the player side turn end clears the mark and every Bound affliction
/// (<c>ShouldPlayMirrors</c>, <c>BeforeSideTurnEndMirrors</c>). Aux0 is the mark and Aux1 the count
/// of Bound cards afflicted this turn (ChainsOfBindingPredictionState).
/// </summary>
[FlashContent("CHAINS_OF_BINDING_POWER")]
public sealed class ChainsOfBindingPower : FlashPowerDef
{
    private const int BoundCardPlayedAux = 0;
    private const int AfflictedThisTurnAux = 1;

    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool ShouldPlay(in FlashBattle battle, FlashRules rules, FlashListener self, in FlashCard card)
    {
        if (card.AfflictionId == 0
            || card.AfflictionId - 1 != FlashRegistry.AfflictionId<Afflictions.BoundAffliction>())
        {
            return true;
        }
        int index = FlashKernel.FindPower(battle, self.Owner, Id);
        return index < 0 || battle.Powers[index].Aux0 == 0;
    }

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        // BeforeCardPlayedMirrors.HandleChainsOfBindingPower skips CardModel.IsDupe, not IsClone.
        // Dupes are rejected at import (FlashStateImporter), and FlashCard.IsClone marks native
        // CreateClone copies (Swift, AdaptiveStrike), which must set the mark when Bound.
        if (play.Card.AfflictionId == 0
            || play.Card.AfflictionId - 1 != FlashRegistry.AfflictionId<Afflictions.BoundAffliction>())
        {
            return;
        }
        x.B.Powers[self.Index].Aux0 = 1;
    }

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        if (x.B.Side != FlashSide.Player
            || self.Owner != FlashLimits.PlayerOwner
            || x.B.Powers[self.Index].Aux1 >= x.B.Powers[self.Index].Amount)
        {
            return;
        }
        ref readonly FlashCard card = ref x.B.Cards[drawnPosition];
        ushort bound = FlashRegistry.AfflictionId<Afflictions.BoundAffliction>();
        FlashKernel.ApplyAffliction(ref x, drawnPosition, bound, x.B.Powers[self.Index].Amount);
        if (x.B.Cards[drawnPosition].AfflictionId == bound + 1)
        {
            x.B.Powers[self.Index].Aux1++;
        }
    }

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (side != FlashSide.Player
            || self.Owner != FlashLimits.PlayerOwner
            || !FlashKernel.Participates(participants, self.Owner))
        {
            return;
        }
        x.B.Powers[self.Index].Aux0 = 0;
        x.B.Powers[self.Index].Aux1 = 0;
        FlashKernel.ClearAfflictions(ref x.B, FlashRegistry.AfflictionId<Afflictions.BoundAffliction>());
    }
}

/// <summary>
/// Native <c>WitheringPresencePower</c> (instanced, applied by Aeonglass to the player): after the
/// player plays a card it decrements its private <c>CardsLeft</c>; at zero it adds one Wither to the
/// hand and resets to 6 (<c>AfterCardPlayedMirrors.HandleWitheringPresencePower</c>). Aux0 is
/// <c>CardsLeft</c> (CounterPredictionState when present, otherwise the native dynamic variable).
/// </summary>
[FlashContent("WITHERING_PRESENCE_POWER")]
public sealed class WitheringPresencePower : FlashPowerDef
{
    private const int CardsLeftAux = 0;
    private const int BaseCardsLeft = 6;

    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsInstanced => true;

    public override IReadOnlyList<string> VarNames => ["CardsLeft"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("WITHER", 0)];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref FlashPower power = ref x.B.Powers[self.Index];
        power.Aux0--;
        if (power.Aux0 > 0)
            return;
        FlashKernel.AddGeneratedCard(ref x,
            FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Wither>()), FlashPile.Hand, creator: FlashLimits.NoOwner);
        if (x.NeedsChoice)
            return;
        x.B.Powers[self.Index].Aux0 = BaseCardsLeft;
    }
}

/// <summary>
/// Native <c>DampenPower</c>: a player-side debuff with no counter behavior of its own. Its casters
/// are tracked in the kernel's <see cref="FlashCounter.DampenCasterMask"/>; MagiKnight applies the
/// downgrades and restores them when the last caster dies.
/// </summary>
[FlashContent("DAMPEN_POWER")]
public sealed class DampenPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.None;
}

/// <summary>
/// Native <c>HighVoltagePower</c> (Zapbot's entrance power): at its owner's side turn end it applies
/// its amount as Strength (<c>EndTurnPowerSupport</c>, <c>EndTurnRegular</c>).
/// </summary>
[FlashContent("HIGH_VOLTAGE_POWER")]
public sealed class HighVoltagePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<StrengthPower>(),
            x.B.Powers[self.Index].Amount, self.Owner);
    }
}

/// <summary>
/// Native <c>PossessStrengthPower</c>: whenever its owner lowers the player's Strength, the negative
/// delta is recorded; when the owner dies the accumulated amount is given back with no applier
/// (<c>AfterPowerAmountChanged</c>, <c>SimulatedCombatState.RefundPossessedStats</c>). Aux0 is the
/// ledger value (SimulatedCombatState._stolenStrength).
/// </summary>
[FlashContent("POSSESS_STRENGTH_POWER")]
public sealed class PossessStrengthPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override void AfterPowerAmountChanged(ref FlashExec x, FlashListener self, ushort powerId, int delta,
        byte applier, byte target)
    {
        if (applier != self.Owner
            || target != FlashLimits.PlayerOwner
            || delta >= 0
            || powerId != FlashRegistry.PowerId<StrengthPower>())
        {
            return;
        }
        x.B.Powers[self.Index].Aux0 += delta;
    }

    public override void DeathTrigger(ref FlashExec x, FlashListener self, byte dead)
    {
        if (dead != self.Owner)
            return;
        int stolen = x.B.Powers[self.Index].Aux0;
        if (stolen != 0)
        {
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<StrengthPower>(),
                -stolen, FlashLimits.NoOwner);
        }
    }
}

/// <summary>Native <c>PossessSpeedPower</c>: the Dexterity counterpart of <see cref="PossessStrengthPower"/>.</summary>
[FlashContent("POSSESS_SPEED_POWER")]
public sealed class PossessSpeedPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override void AfterPowerAmountChanged(ref FlashExec x, FlashListener self, ushort powerId, int delta,
        byte applier, byte target)
    {
        if (applier != self.Owner
            || target != FlashLimits.PlayerOwner
            || delta >= 0
            || powerId != FlashRegistry.PowerId<DexterityPower>())
        {
            return;
        }
        x.B.Powers[self.Index].Aux0 += delta;
    }

    public override void DeathTrigger(ref FlashExec x, FlashListener self, byte dead)
    {
        if (dead != self.Owner)
            return;
        int stolen = x.B.Powers[self.Index].Aux0;
        if (stolen != 0)
        {
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<DexterityPower>(),
                -stolen, FlashLimits.NoOwner);
        }
    }
}

/// <summary>
/// Native <c>StockPower</c>: on its owner's death it spawns a replacement Axebot in the same slot with
/// one less StockPower, and the combat does not end while unresolved (<c>ShouldStopCombatFromEnding</c>,
/// <c>DeathPowerSupport.SpawnsPrimaryEnemyOnDeath</c>). The immediate spawn is implemented by the
/// Axebot monster's <c>AfterDeath</c>; this definition only carries the removal/ending flags.
/// </summary>
[FlashContent("STOCK_POWER")]
public sealed class StockPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool SpawnsPrimaryEnemyOnDeath => true;
}

/// <summary>
/// Native <c>AdaptablePower</c> (Test Subject): its owner is not removed on death; the deferred death
/// pass forces the native RESPAWN_MOVE and marks the death phase reviving
/// (<c>DeathPowerSupport.Trigger</c> → <c>BeginAdaptableRevive</c>). Its power survives the owner's
/// death and keeps the combat from ending while the next form is pending.
/// </summary>
[FlashContent("ADAPTABLE_POWER")]
public sealed class AdaptablePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override bool PreventsRemovalAfterDeath => true;

    public override bool PreventsCombatEnding => true;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath => false;

    public override void DeathTrigger(ref FlashExec x, FlashListener self, byte dead)
    {
        if (dead != self.Owner)
            return;
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, self.Owner);
        x.B.Enemy(self.Owner).DeathPhase = FlashDeathPhase.Reviving;
        FlashKernel.ForceMonsterMove(ref x, self.Owner, rules.NodeIndex("RESPAWN_MOVE"));
    }
}

/// <summary>
/// Native <c>PainfulStabsPower</c>: after its owner's powered attack, each player hit with unblocked
/// damage adds <c>Amount</c> Wound to the discard pile
/// (<c>AfterAttackMirrors.HandlePainfulStabsPower</c>). The unblocked hit count is kept on the
/// stack-only <see cref="FlashExec.AttackUnblockedHits"/> for the current attack.
/// </summary>
[FlashContent("PAINFUL_STABS_POWER")]
public sealed class PainfulStabsPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath => false;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("WOUND", 0)];

    public override void AfterAttack(ref FlashExec x, FlashListener self, byte attacker)
    {
        if (attacker != self.Owner || x.AttackUnblockedHits <= 0)
            return;
        int count = x.AttackUnblockedHits * x.B.Powers[self.Index].Amount;
        for (int index = 0; index < count; index++)
        {
            FlashKernel.AddGeneratedCard(ref x,
                FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Wound>()), FlashPile.Discard, creator: FlashLimits.NoOwner);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>NemesisPower</c>: at its owner's side turn end it toggles a private flag and applies or
/// removes Intangible 1 (<c>EndTurnPowerSupport.TriggerNemesis</c>). Aux0 is the toggled flag
/// (SimulatedCombatState._nemesisShouldApplyIntangible).
/// </summary>
[FlashContent("NEMESIS_POWER")]
public sealed class NemesisPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (side != FlashSide.Enemy || !participates)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        bool apply = power.Aux0 == 0;
        power.Aux0 = apply ? 1 : 0;
        if (apply)
            FlashKernel.ApplyPowerFromMonster(ref x, self.Owner,
                FlashRegistry.PowerId<IntangiblePower>(), 1, self.Owner);
        else
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, FlashRegistry.PowerId<IntangiblePower>(), 0);
    }
}

/// <summary>
/// Native <c>EnragePower</c> (Test Subject's entrance power): after any Skill card is played its owner
/// gains <c>Amount</c> Strength (<c>AfterCardPlayedMirrors.HandleEnragePower</c>).
/// </summary>
[FlashContent("ENRAGE_POWER")]
public sealed class EnragePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Skill)
            return;
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<StrengthPower>(),
            x.B.Powers[self.Index].Amount, self.Owner);
    }
}
