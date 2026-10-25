using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// Native <c>PaperCutsPower</c>: after its owner deals unblocked powered-attack damage to a player,
/// that player loses <c>Amount</c> max HP
/// (<c>AfterDamageGivenMirrors.HandlePaperCutsPower</c>, <c>AfterDamageGiven</c> pass).
/// </summary>
[BlitzContent("PAPER_CUTS_POWER")]
public sealed class PaperCutsPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterDamageGiven(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (dealer != self.Owner
            || result.Receiver != BlitzLimits.PlayerOwner
            || !result.Props.IsPoweredAttack()
            || result.UnblockedDamage <= 0)
        {
            return;
        }
        BlitzKernel.LoseMaxHp(ref x, BlitzLimits.PlayerOwner, x.B.Powers[self.Index].Amount);
    }
}

/// <summary>
/// Native <c>RampartPower</c>: at the player side turn start it grants its amount as Unpowered Block
/// to every living Turret Operator (<c>PersistentPowerSupport.TriggerRampart</c>).
/// </summary>
[BlitzContent("RAMPART_POWER")]
public sealed class RampartPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void Rampart(ref BlitzExec x, BlitzListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0 || !BlitzRegistry.TryMonster("TURRET_OPERATOR", out ushort turret))
            return;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (x.B.Enemy(owner).MonsterId == turret && x.B.Creature(owner).IsAlive)
            {
                BlitzKernel.GainBlock(ref x, owner, amount, BlitzValueProps.Unpowered, fromCard: false);
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
[BlitzContent("SOAR_POWER")]
public sealed class SoarPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override IReadOnlyList<string> VarNames => ["DamageDecrease"];

    public override decimal ModifyDamageMultiplicative(ref BlitzExec x, BlitzListener self, in BlitzDamageContext damage, decimal amount)
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
/// while the player plays the card). Every card in a Blitz battle is the player's, so the target is
/// the player owner. Its <c>AfterCardEnteredCombat</c> is mirrored by
/// <c>BlitzKernel.NormalizeCardAfflictions</c> (newly entered Power cards get Galvanized with the
/// power's amount).
/// </summary>
[BlitzContent("GALVANIC_POWER")]
public sealed class GalvanicPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (play.Card.AfflictionId == 0
            || play.Card.AfflictionId - 1 != BlitzRegistry.AfflictionId<Afflictions.GalvanizedAffliction>())
        {
            return;
        }
        Span<byte> target = stackalloc byte[1];
        target[0] = BlitzLimits.PlayerOwner;
        BlitzKernel.Damage(ref x, target, x.B.Powers[self.Index].Amount,
            BlitzValueProps.Unpowered, BlitzLimits.NoOwner, hasCardSource: false, default);
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
[BlitzContent("CHAINS_OF_BINDING_POWER")]
public sealed class ChainsOfBindingPower : BlitzPowerDef
{
    private const int BoundCardPlayedAux = 0;
    private const int AfflictedThisTurnAux = 1;

    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool ShouldPlay(in BlitzBattle battle, BlitzRules rules, BlitzListener self, in BlitzCard card)
    {
        if (card.AfflictionId == 0
            || card.AfflictionId - 1 != BlitzRegistry.AfflictionId<Afflictions.BoundAffliction>())
        {
            return true;
        }
        int index = BlitzKernel.FindPower(battle, self.Owner, Id);
        return index < 0 || battle.Powers[index].Aux0 == 0;
    }

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        // BeforeCardPlayedMirrors.HandleChainsOfBindingPower skips CardModel.IsDupe, not IsClone.
        // The only dupes Blitz creates are HistoryCourse copies; BlitzCard.IsClone marks native
        // CreateClone copies (Swift, AdaptiveStrike), which must set the mark when Bound.
        if (x.CurrentPlayIsHistoryCourseDupe || play.Card.AfflictionId == 0
            || play.Card.AfflictionId - 1 != BlitzRegistry.AfflictionId<Afflictions.BoundAffliction>())
        {
            return;
        }
        x.B.Powers[self.Index].Aux0 = 1;
    }

    public override void AfterCardDrawn(ref BlitzExec x, BlitzListener self, int drawnPosition, bool fromHandDraw)
    {
        if (x.B.Side != BlitzSide.Player
            || self.Owner != BlitzLimits.PlayerOwner
            || x.B.Powers[self.Index].Aux1 >= x.B.Powers[self.Index].Amount)
        {
            return;
        }
        ref readonly BlitzCard card = ref x.B.Cards[drawnPosition];
        ushort bound = BlitzRegistry.AfflictionId<Afflictions.BoundAffliction>();
        BlitzKernel.ApplyAffliction(ref x, drawnPosition, bound, x.B.Powers[self.Index].Amount);
        if (x.B.Cards[drawnPosition].AfflictionId == bound + 1)
        {
            x.B.Powers[self.Index].Aux1++;
        }
    }

    /// <summary>
    /// Native keeps the per-turn <c>CardAfflictedEntry</c> count until the player turn number
    /// changes; only the play mark and the Bound afflictions are cleared at the side turn end.
    /// Reset the count at the next player side start instead of here, so the turn-end state still
    /// reports this turn's count like <c>ChainsOfBindingPredictionState</c>.
    /// </summary>
    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, bool participates)
    {
        if (!participates || self.Owner != BlitzLimits.PlayerOwner)
            return;
        x.B.Powers[self.Index].Aux1 = 0;
    }

    public override void BeforeSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        if (side != BlitzSide.Player
            || self.Owner != BlitzLimits.PlayerOwner
            || !BlitzKernel.Participates(participants, self.Owner))
        {
            return;
        }
        x.B.Powers[self.Index].Aux0 = 0;
        BlitzKernel.ClearAfflictions(ref x.B, BlitzRegistry.AfflictionId<Afflictions.BoundAffliction>());
    }
}

/// <summary>
/// Native <c>WitheringPresencePower</c> (instanced, applied by Aeonglass to the player): after the
/// player plays a card it decrements its private <c>CardsLeft</c>; at zero it adds one Wither to the
/// hand and resets to 6 (<c>AfterCardPlayedMirrors.HandleWitheringPresencePower</c>). Aux0 is
/// <c>CardsLeft</c> (CounterPredictionState when present, otherwise the native dynamic variable).
/// </summary>
[BlitzContent("WITHERING_PRESENCE_POWER")]
public sealed class WitheringPresencePower : BlitzPowerDef
{
    private const int CardsLeftAux = 0;
    private const int BaseCardsLeft = 6;

    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool IsInstanced => true;

    public override IReadOnlyList<string> VarNames => ["CardsLeft"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("WITHER", 0)];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        ref BlitzPower power = ref x.B.Powers[self.Index];
        power.Aux0--;
        if (power.Aux0 > 0)
            return;
        BlitzKernel.AddGeneratedCard(ref x,
            BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Wither>()), BlitzPile.Hand, creator: BlitzLimits.NoOwner);
        if (x.NeedsChoice)
            return;
        x.B.Powers[self.Index].Aux0 = BaseCardsLeft;
    }
}

/// <summary>
/// Native <c>DampenPower</c>: a player-side debuff with no counter behavior of its own. Its casters
/// are tracked in the kernel's <see cref="BlitzCounter.DampenCasterMask"/>; MagiKnight applies the
/// downgrades and restores them when the last caster dies.
/// </summary>
[BlitzContent("DAMPEN_POWER")]
public sealed class DampenPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.None;
}

/// <summary>
/// Native <c>HighVoltagePower</c> (Zapbot's entrance power): at its owner's side turn end it applies
/// its amount as Strength (<c>EndTurnPowerSupport</c>, <c>EndTurnRegular</c>).
/// </summary>
[BlitzContent("HIGH_VOLTAGE_POWER")]
public sealed class HighVoltagePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (!participates)
            return;
        BlitzKernel.ApplyPower(ref x, self.Owner, BlitzRegistry.PowerId<StrengthPower>(),
            x.B.Powers[self.Index].Amount, self.Owner);
    }
}

/// <summary>
/// Native <c>PossessStrengthPower</c>: whenever its owner lowers the player's Strength, the negative
/// delta is recorded; when the owner dies the accumulated amount is given back with no applier
/// (<c>AfterPowerAmountChanged</c>, <c>SimulatedCombatState.RefundPossessedStats</c>). Aux0 is the
/// ledger value (SimulatedCombatState._stolenStrength).
/// </summary>
[BlitzContent("POSSESS_STRENGTH_POWER")]
public sealed class PossessStrengthPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override void AfterPowerAmountChanged(ref BlitzExec x, BlitzListener self, ushort powerId, int delta,
        byte applier, byte target)
    {
        if (applier != self.Owner
            || target != BlitzLimits.PlayerOwner
            || delta >= 0
            || powerId != BlitzRegistry.PowerId<StrengthPower>())
        {
            return;
        }
        x.B.Powers[self.Index].Aux0 += delta;
    }

    public override void DeathTrigger(ref BlitzExec x, BlitzListener self, byte dead)
    {
        if (dead != self.Owner)
            return;
        int stolen = x.B.Powers[self.Index].Aux0;
        if (stolen != 0)
        {
            BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<StrengthPower>(),
                -stolen, BlitzLimits.NoOwner);
        }
    }
}

/// <summary>Native <c>PossessSpeedPower</c>: the Dexterity counterpart of <see cref="PossessStrengthPower"/>.</summary>
[BlitzContent("POSSESS_SPEED_POWER")]
public sealed class PossessSpeedPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override void AfterPowerAmountChanged(ref BlitzExec x, BlitzListener self, ushort powerId, int delta,
        byte applier, byte target)
    {
        if (applier != self.Owner
            || target != BlitzLimits.PlayerOwner
            || delta >= 0
            || powerId != BlitzRegistry.PowerId<DexterityPower>())
        {
            return;
        }
        x.B.Powers[self.Index].Aux0 += delta;
    }

    public override void DeathTrigger(ref BlitzExec x, BlitzListener self, byte dead)
    {
        if (dead != self.Owner)
            return;
        int stolen = x.B.Powers[self.Index].Aux0;
        if (stolen != 0)
        {
            BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<DexterityPower>(),
                -stolen, BlitzLimits.NoOwner);
        }
    }
}

/// <summary>
/// Native <c>StockPower</c>: on its owner's death it spawns a replacement Axebot in the same slot with
/// one less StockPower, and the combat does not end while unresolved (<c>ShouldStopCombatFromEnding</c>,
/// <c>DeathPowerSupport.SpawnsPrimaryEnemyOnDeath</c>). The immediate spawn is implemented by the
/// Axebot monster's <c>AfterDeath</c>; this definition only carries the removal/ending flags.
/// </summary>
[BlitzContent("STOCK_POWER")]
public sealed class StockPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool SpawnsPrimaryEnemyOnDeath => true;
}

/// <summary>
/// Native <c>AdaptablePower</c> (Test Subject): its owner is not removed on death; the deferred death
/// pass forces the native RESPAWN_MOVE and marks the death phase reviving
/// (<c>DeathPowerSupport.Trigger</c> → <c>BeginAdaptableRevive</c>). Its power survives the owner's
/// death and keeps the combat from ending while the next form is pending.
/// </summary>
[BlitzContent("ADAPTABLE_POWER")]
public sealed class AdaptablePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override bool PreventsRemovalAfterDeath => true;

    public override bool PreventsCombatEnding => true;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath => false;

    public override void DeathTrigger(ref BlitzExec x, BlitzListener self, byte dead)
    {
        if (dead != self.Owner)
            return;
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, self.Owner);
        x.B.Enemy(self.Owner).DeathPhase = BlitzDeathPhase.Reviving;
        BlitzKernel.ForceMonsterMove(ref x, self.Owner, rules.NodeIndex("RESPAWN_MOVE"));
    }
}

/// <summary>
/// Native <c>PainfulStabsPower</c>: after its owner's powered attack, each player hit with unblocked
/// damage adds <c>Amount</c> Wound to the discard pile
/// (<c>AfterAttackMirrors.HandlePainfulStabsPower</c>). The unblocked hit count is kept on the
/// stack-only <see cref="BlitzExec.AttackUnblockedHits"/> for the current attack.
/// </summary>
[BlitzContent("PAINFUL_STABS_POWER")]
public sealed class PainfulStabsPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath => false;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("WOUND", 0)];

    public override void AfterAttack(ref BlitzExec x, BlitzListener self, byte attacker)
    {
        if (attacker != self.Owner || x.AttackUnblockedHits <= 0)
            return;
        int count = x.AttackUnblockedHits * x.B.Powers[self.Index].Amount;
        for (int index = 0; index < count; index++)
        {
            BlitzKernel.AddGeneratedCard(ref x,
                BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Wound>()), BlitzPile.Discard, creator: BlitzLimits.NoOwner);
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
[BlitzContent("NEMESIS_POWER")]
public sealed class NemesisPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (side != BlitzSide.Enemy || !participates)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        bool apply = power.Aux0 == 0;
        power.Aux0 = apply ? 1 : 0;
        if (apply)
            BlitzKernel.ApplyPowerFromMonster(ref x, self.Owner,
                BlitzRegistry.PowerId<IntangiblePower>(), 1, self.Owner);
        else
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, BlitzRegistry.PowerId<IntangiblePower>(), 0);
    }
}

/// <summary>
/// Native <c>EnragePower</c> (Test Subject's entrance power): after any Skill card is played its owner
/// gains <c>Amount</c> Strength (<c>AfterCardPlayedMirrors.HandleEnragePower</c>).
/// </summary>
[BlitzContent("ENRAGE_POWER")]
public sealed class EnragePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Skill)
            return;
        BlitzKernel.ApplyPower(ref x, self.Owner, BlitzRegistry.PowerId<StrengthPower>(),
            x.B.Powers[self.Index].Amount, self.Owner);
    }
}
