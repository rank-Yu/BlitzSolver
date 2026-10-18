using CombatSolver.Engine.InCombat.Flash.Content.Powers;

namespace CombatSolver.Engine.InCombat.Flash.Content.Relics;

// Class 4 subset: relics whose existing-engine behaviour is driven by a private counter or latch.
// Flash keeps that value in FlashRelic.Counter / FlashRelic.Aux (the bridge projects the engine's
// state into the same slot, see src/Search/FlashBeam/Bridge/FlashStateProjections.cs); every
// definition maintains the slot exactly as the existing engine maintains its state. Relics whose
// required hook the kernel does not dispatch yet stay undefined and are listed in the task report.

/// <summary>
/// <c>ArtOfWar</c>: <c>AfterCardPlayed</c> marks an attack played this turn; <c>AfterSideTurnEnd</c>
/// moves that mark to last turn; <c>AfterEnergyReset</c> grants its Energy when last turn had no
/// attack (<c>SimulatedCombatState.RecordRelicCardPlayed</c>, <c>CompleteRelicsAfterSideTurnEnd</c>,
/// <c>TriggerRelicsAfterEnergyReset</c>).
/// Counter = any attack played this turn (0/1), Aux = any attack played last turn (0/1).
/// </summary>
[FlashContent("ART_OF_WAR")]
public sealed class ArtOfWar : FlashRelicDef
{
    // ArtOfWar.cs: EnergyVar("GainEnergy", 1).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type == FlashCardType.Attack)
            x.B.Relics[self.Index].Counter = 1;
    }

    public override void AfterSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Aux = relic.Counter;
        relic.Counter = 0;
    }

    public override void AfterEnergyReset(ref FlashExec x, FlashListener self)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (x.B.PlayerTurn > 1 && relic.Aux == 0)
            FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
        relic.Counter = 0;
        relic.Aux = 0;
    }
}

/// <summary>
/// <c>BurningSticks</c>: the first Skill exhausted in the combat puts a clone into the hand
/// (<c>AfterCardExhaustedMirrors.HandleBurningSticks</c>).
/// Counter = 1 once used this combat.
/// </summary>
[FlashContent("BURNING_STICKS")]
public sealed class BurningSticks : FlashRelicDef
{
    public override void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card, bool causedByEthereal)
    {
        if (x.R.Card(card.Id, card.Upgrade).Type != FlashCardType.Skill || x.B.Relics[self.Index].Counter != 0)
            return;
        if (!FlashKernel.AddGeneratedCard(ref x, FlashKernel.CloneCard(card), FlashPile.Hand))
            return;
        x.B.Relics[self.Index].Counter = 1;
    }
}

/// <summary>
/// <c>CentennialPuzzle</c>: the first unblocked damage taken draws its Cards
/// (<c>AfterDamageReceivedMirrors</c>, engine case in <c>SimulatedCombatState</c> mirror).
/// Counter = 1 once used this combat.
/// </summary>
[FlashContent("CENTENNIAL_PUZZLE")]
public sealed class CentennialPuzzle : FlashRelicDef
{
    // CentennialPuzzle.cs: CardsVar(3).
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (!FlashKernel.IsInProgress(x.B) || result.Receiver != FlashLimits.PlayerOwner
            || result.UnblockedDamage <= 0 || x.B.Relics[self.Index].Counter != 0)
            return;
        x.B.Relics[self.Index].Counter = 1;
        FlashKernel.Draw(ref x, (int)x.R.RelicVar(Id, 0), fromHandDraw: false);
    }
}

/// <summary>
/// <c>DemonTongue</c>: on the player's side, the first unblocked damage taken in a turn is healed
/// back; <c>BeforeSideTurnStart</c> resets the latch (<c>AfterDamageReceivedMirrors</c>,
/// <c>RelicPredictionStateSupport.ResetBeforeSideTurnStart</c>).
/// Counter = 1 once triggered this turn.
/// </summary>
[FlashContent("DEMON_TONGUE")]
public sealed class DemonTongue : FlashRelicDef
{
    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (!FlashKernel.IsInProgress(x.B) || x.B.Side != FlashSide.Player
            || result.Receiver != FlashLimits.PlayerOwner || result.UnblockedDamage <= 0
            || x.B.Relics[self.Index].Counter != 0)
            return;
        x.B.Relics[self.Index].Counter = 1;
        FlashKernel.Heal(ref x, FlashLimits.PlayerOwner, result.UnblockedDamage);
    }

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>HappyFlower</c>: <c>AfterSideTurnStart</c> advances its cycle and grants Energy on wrap
/// (<c>SimulatedCombatState.RelicTurnStart.TriggerRelicsAfterSideTurnStart</c>).
/// Counter = turns seen in the current cycle.
/// </summary>
[FlashContent("HAPPY_FLOWER")]
public sealed class HappyFlower : FlashRelicDef
{
    // HappyFlower.cs: EnergyVar(1), DynamicVar("Turns", 3m).
    public override IReadOnlyList<string> VarNames => ["Energy", "Turns"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        int turns = (int)x.R.RelicVar(Id, 1);
        relic.Counter = (relic.Counter + 1) % turns;
        if (relic.Counter == 0)
            FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>JossPaper</c>: exhausted cards advance a counter and draw per threshold; Ethereal exhausts are
/// deferred to <c>AfterSideTurnEnd</c> (<c>AfterCardExhaustedMirrors.HandleJossPaper</c> and the
/// native <c>AfterSideTurnEnd</c>).
/// Counter = exhausts toward the threshold, Aux = Ethereal exhausts awaiting the turn-end flush.
/// </summary>
[FlashContent("JOSS_PAPER")]
public sealed class JossPaper : FlashRelicDef
{
    // JossPaper.cs: DynamicVar("ExhaustAmount", 5m).
    public override IReadOnlyList<string> VarNames => ["ExhaustAmount"];

    public override void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card, bool causedByEthereal)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (causedByEthereal)
        {
            relic.Aux++;
            return;
        }
        relic.Counter++;
        DrawIfThresholdMet(ref x, ref relic);
    }

    public override void AfterSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter += relic.Aux;
        relic.Aux = 0;
        DrawIfThresholdMet(ref x, ref relic);
    }

    private void DrawIfThresholdMet(ref FlashExec x, ref FlashRelic relic)
    {
        int threshold = (int)x.R.RelicVar(Id, 0);
        if (relic.Counter < threshold)
            return;
        int draws = relic.Counter / threshold;
        FlashKernel.Draw(ref x, draws, fromHandDraw: false);
        relic.Counter %= threshold;
    }
}

/// <summary>
/// <c>Kunai</c>: every third attack played this turn applies Dexterity; <c>BeforeSideTurnStart</c>
/// resets the counter (<c>AfterCardPlayedMirrors.HandleKunai</c> and
/// <c>RelicPredictionStateSupport.ResetBeforeSideTurnStart</c>).
/// Counter = attacks played this turn.
/// </summary>
[FlashContent("KUNAI")]
public sealed class Kunai : FlashRelicDef
{
    // Kunai.cs: CardsVar(3), PowerVar<DexterityPower>(1m).
    public override IReadOnlyList<string> VarNames => ["Cards", "DexterityPower"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (!FlashKernel.IsInProgress(x.B) || x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Attack)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        // ApplyRelicStatPower declines while the combat is ending; the counter still increments.
        if (relic.Counter % (int)x.R.RelicVar(Id, 0) != 0 || FlashKernel.IsEnding(x.B))
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<DexterityPower>(),
            (int)x.R.RelicVar(Id, 1), FlashLimits.PlayerOwner);
    }

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>Kusarigama</c>: every third attack played this turn deals its Damage to a random enemy;
/// <c>AfterSideTurnEnd</c> resets the counter (<c>AfterCardPlayedMirrors.HandleKusarigama</c>,
/// <c>RelicPredictionStateSupport.ResetAfterSideTurnEnd</c>).
/// Counter = attacks played this turn.
/// </summary>
[FlashContent("KUSARIGAMA")]
public sealed class Kusarigama : FlashRelicDef
{
    // Kusarigama.cs: CardsVar(3), DamageVar(6m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Cards", "Damage"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Attack)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (relic.Counter % (int)x.R.RelicVar(Id, 0) != 0)
            return;
        FlashCardEffects.DamageRandomEnemy(ref x, x.R.RelicVar(Id, 1), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
    }

    public override void AfterSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side == FlashSide.Player)
            x.B.Relics[self.Index].Counter = 0;
    }
}

/// <summary>
/// <c>LetterOpener</c>: every third Skill played this turn deals its Damage to all enemies;
/// <c>AfterSideTurnStart</c> resets the counter after turn 1
/// (<c>AfterCardPlayedMirrors.HandleLetterOpener</c>, <c>ResetAfterSideTurnStart</c>).
/// Counter = Skills played this turn.
/// </summary>
[FlashContent("LETTER_OPENER")]
public sealed class LetterOpener : FlashRelicDef
{
    // LetterOpener.cs: CardsVar(3), DamageVar(5m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Cards", "Damage"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (!FlashKernel.IsInProgress(x.B) || x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Skill)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (relic.Counter % (int)x.R.RelicVar(Id, 0) != 0)
            return;
        FlashCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 1), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
    }

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side == FlashSide.Player && x.B.PlayerTurn > 1)
            x.B.Relics[self.Index].Counter = 0;
    }
}

/// <summary>
/// <c>Nunchaku</c>: every tenth attack played grants Energy; the counter survives turns
/// (<c>AfterCardPlayedMirrors.HandleNunchaku</c>).
/// Counter = attacks played this combat.
/// </summary>
[FlashContent("NUNCHAKU")]
public sealed class Nunchaku : FlashRelicDef
{
    // Nunchaku.cs: CardsVar(10), EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Cards", "Energy"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Attack)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (!FlashKernel.IsInProgress(x.B) || relic.Counter % (int)x.R.RelicVar(Id, 0) != 0)
            return;
        FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 1));
    }
}

/// <summary>
/// <c>Orichalcum</c>: <c>BeforeSideTurnEndVeryEarly</c> latches when the player has no block;
/// <c>BeforeSideTurnEnd</c> spends the latch for its Block; <c>BeforeSideTurnStart</c> clears it
/// (<c>OrichalcumMirrors</c>).
/// Counter = latch set by the VeryEarly pass (always 0 at a stable boundary).
/// </summary>
[FlashContent("ORICHALCUM")]
public sealed class Orichalcum : FlashRelicDef
{
    // Orichalcum.cs: BlockVar(6m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void BeforeSideTurnEndVeryEarly(ref FlashExec x, FlashListener self, FlashSide side,
        ushort participants)
    {
        if (FlashKernel.Participates(participants, self.Owner)
            && x.B.Creature(self.Owner).Block <= 0)
            x.B.Relics[self.Index].Counter = 1;
    }

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter == 0)
            return;
        relic.Counter = 0;
        FlashKernel.GainBlock(ref x, self.Owner, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered, fromCard: false);
    }

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>OrnamentalFan</c>: every third attack played this turn gains its Block;
/// <c>BeforeSideTurnStart</c> resets the counter (<c>AfterCardPlayedMirrors.HandleOrnamentalFan</c>).
/// Counter = attacks played this turn.
/// </summary>
[FlashContent("ORNAMENTAL_FAN")]
public sealed class OrnamentalFan : FlashRelicDef
{
    // OrnamentalFan.cs: CardsVar(3), BlockVar(4m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Cards", "Block"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (!FlashKernel.IsInProgress(x.B) || x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Attack)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (relic.Counter % (int)x.R.RelicVar(Id, 0) != 0)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 1),
            FlashValueProps.Unpowered, fromCard: false);
    }

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>PenNib</c>: <c>BeforeCardPlayed</c> advances the attack counter; the tenth attack doubles its
/// own damage and the latch clears when that card finishes (<c>BeforeCardPlayedMirrors.HandlePenNib</c>,
/// <c>ModifyDamageMirrors.HandlePenNib</c>, <c>AfterCardPlayedMirrors.HandlePenNib</c>).
/// Counter = attacks played modulo 10, Aux = 1 while the current attack is the doubled one.
/// </summary>
[FlashContent("PEN_NIB")]
public sealed class PenNib : FlashRelicDef
{
    // PenNib.cs: _attacksThreshold = 10 (a code constant, not a DynamicVar).
    private const int AttacksThreshold = 10;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Attack)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter = (relic.Counter + 1) % AttacksThreshold;
        if (relic.Counter == 0)
            relic.Aux = 1;
    }

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self,
        in FlashDamageContext damage, decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource
            || damage.Dealer != FlashLimits.PlayerOwner
            || x.B.Relics[self.Index].Aux == 0)
            return 1m;
        return FlashFingerprint.Equal(damage.CardSource, x.Card) ? 2m : 1m;
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.B.Relics[self.Index].Aux != 0)
            x.B.Relics[self.Index].Aux = 0;
    }
}

/// <summary>
/// <c>Pendulum</c>: <c>BeforeHandDraw</c> advances its cycle, and <c>ModifyHandDraw</c> adds its
/// Cards on the turn the cycle wraps (<c>SimulatedCombatState.Relics.PrepareRelicsBeforeHandDraw</c>
/// and <c>PersistentPowerSupport.GetStatefulRelicHandDrawContribution</c>).
/// Counter = turns seen in the current cycle.
/// </summary>
[FlashContent("PENDULUM")]
public sealed class Pendulum : FlashRelicDef
{
    // Pendulum.cs: CardsVar(1), DynamicVar("Turns", 3m).
    public override IReadOnlyList<string> VarNames => ["Cards", "Turns"];

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        int turns = (int)x.R.RelicVar(Id, 1);
        relic.Counter = (relic.Counter + 1) % turns;
        return relic.Counter == 0 ? amount + x.R.RelicVar(Id, 0) : amount;
    }
}

/// <summary>
/// <c>Permafrost</c>: the first Power card played this combat gains its block
/// (<c>AfterCardPlayedMirrors.HandlePermafrost</c>).
/// Counter = 1 once used this combat.
/// </summary>
[FlashContent("PERMAFROST")]
public sealed class Permafrost : FlashRelicDef
{
    // Permafrost.cs: BlockVar(7m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (!FlashKernel.IsInProgress(x.B)
            || x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Power
            || x.B.Relics[self.Index].Counter != 0)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            FlashValueProps.Unpowered, fromCard: false);
        x.B.Relics[self.Index].Counter = 1;
    }
}

/// <summary>
/// <c>Pocketwatch</c>: counts cards played each turn; <c>BeforeSideTurnStart</c> shifts this turn's
/// count to last turn; <c>ModifyHandDraw</c> adds its Cards while last turn's count did not exceed the
/// threshold (<c>AfterCardPlayedMirrors</c> ignored case,
/// <c>SimulatedCombatState.RecordRelicCardPlayed</c>, <c>PrepareRelicBeforeSideTurnStart</c> and
/// <c>PersistentPowerSupport.GetStatefulRelicHandDrawContribution</c>).
/// Counter = cards played this turn, Aux = cards played last turn.
/// </summary>
[FlashContent("POCKETWATCH")]
public sealed class Pocketwatch : FlashRelicDef
{
    // Pocketwatch.cs: DynamicVar("CardThreshold", 3m), CardsVar(3).
    public override IReadOnlyList<string> VarNames => ["CardThreshold", "Cards"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
        => x.B.Relics[self.Index].Counter++;

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Aux = relic.Counter;
        relic.Counter = 0;
    }

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
    {
        if (x.B.PlayerTurn == 1 || x.B.Relics[self.Index].Aux > x.R.RelicVar(Id, 0))
            return amount;
        return amount + x.R.RelicVar(Id, 1);
    }
}

/// <summary>
/// <c>RainbowRing</c>: counts one attack, Skill and Power per turn; once all three have appeared it
/// applies Strength and Dexterity and stops counting (<c>AfterCardPlayedMirrors.HandleRainbowRing</c>,
/// <c>BeforeSideTurnStart</c> reset). Counter = activation count this turn (the latch), Aux = Powers
/// played this turn; the attack and Skill counts are the kernel's
/// <see cref="FlashCounter.AttacksPlayedThisTurn"/> and <see cref="FlashCounter.SkillCardsPlayedThisTurn"/>
/// (the existing engine's own fields are not read after the latch closes).
/// </summary>
[FlashContent("RAINBOW_RING")]
public sealed class RainbowRing : FlashRelicDef
{
    // RainbowRing.cs: PowerVar<StrengthPower>(1m), PowerVar<DexterityPower>(1m).
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter = 0;
        relic.Aux = 0;
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (!FlashKernel.IsInProgress(x.B) || x.B.Relics[self.Index].Counter >= 1)
            return;
        FlashCardType type = x.R.Card(play.Card.Id, play.Card.Upgrade).Type;
        int attacks = x.B.Counter(FlashCounter.AttacksPlayedThisTurn) + (type == FlashCardType.Attack ? 1 : 0);
        int skills = x.B.Counter(FlashCounter.SkillCardsPlayedThisTurn) + (type == FlashCardType.Skill ? 1 : 0);
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (type == FlashCardType.Power)
            relic.Aux++;
        if (attacks == 0 || skills == 0 || relic.Aux == 0)
            return;
        if (!FlashKernel.IsEnding(x.B))
        {
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<StrengthPower>(),
                (int)x.R.RelicVar(Id, 0), FlashLimits.PlayerOwner);
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<DexterityPower>(),
                (int)x.R.RelicVar(Id, 1), FlashLimits.PlayerOwner);
        }
        relic.Counter = 1;
    }
}

/// <summary>
/// <c>Shuriken</c>: every third attack played this turn applies Strength;
/// <c>BeforeSideTurnStart</c> resets the counter (<c>AfterCardPlayedMirrors.HandleShuriken</c>).
/// Counter = attacks played this turn.
/// </summary>
[FlashContent("SHURIKEN")]
public sealed class Shuriken : FlashRelicDef
{
    // Shuriken.cs: CardsVar(3), PowerVar<StrengthPower>(1m).
    public override IReadOnlyList<string> VarNames => ["Cards", "StrengthPower"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (!FlashKernel.IsInProgress(x.B) || x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Attack)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        // ApplyRelicStatPower declines while the combat is ending; the counter still increments.
        if (relic.Counter % (int)x.R.RelicVar(Id, 0) != 0 || FlashKernel.IsEnding(x.B))
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<StrengthPower>(),
            (int)x.R.RelicVar(Id, 1), FlashLimits.PlayerOwner);
    }

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>TuningFork</c>: every tenth Skill played gains its Block and the counter wraps
/// (<c>AfterCardPlayedMirrors.HandleTuningFork</c>). Counter = Skills played toward the threshold.
/// </summary>
[FlashContent("TUNING_FORK")]
public sealed class TuningFork : FlashRelicDef
{
    // TuningFork.cs: CardsVar(10), BlockVar(7m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Cards", "Block"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Skill)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        int threshold = (int)x.R.RelicVar(Id, 0);
        if (relic.Counter < threshold)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 1),
            FlashValueProps.Unpowered, fromCard: false);
        relic.Counter -= threshold;
    }
}

/// <summary>
/// <c>VenerableTeaSet</c>: <c>AfterEnergyReset</c> grants its Energy once when the pre-combat tea is
/// pending (<c>SimulatedCombatState.TriggerRelicsAfterEnergyReset</c>). Counter = 1 while pending.
/// </summary>
[FlashContent("VENERABLE_TEA_SET")]
public sealed class VenerableTeaSet : FlashRelicDef
{
    // VenerableTeaSet.cs: EnergyVar(2).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterEnergyReset(ref FlashExec x, FlashListener self)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter == 0)
            return;
        FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
        relic.Counter = 0;
    }
}

/// <summary>
/// <c>BeatingRemnant</c>: HP loss from the owner is capped at its MaxHpLoss per player turn and
/// unblocked damage taken accumulates until the next player side turn start
/// (<c>ModifyHpLostMirrors.HandleBeatingRemnant</c>,
/// <c>AfterDamageReceivedMirrors.HandleBeatingRemnant</c>,
/// <c>RelicPredictionStateSupport.ResetBeforeSideTurnStart</c>).
/// Counter = damage received this turn (the engine's decimal state only ever accumulates the
/// integer <c>UnblockedDamage</c>); the bridge projects
/// <c>BeatingRemnantPredictionState.DamageReceivedThisTurn</c>.
/// </summary>
[FlashContent("BEATING_REMNANT")]
public sealed class BeatingRemnant : FlashRelicDef
{
    // BeatingRemnant.cs: DynamicVar("MaxHpLoss", 20m).
    public override IReadOnlyList<string> VarNames => ["MaxHpLoss"];

    public override decimal ModifyHpLostAfterOsty(ref FlashExec x, FlashListener self, in FlashHpLossContext loss,
        decimal amount)
    {
        if (loss.Target != self.Owner)
            return amount;
        return Math.Min(amount, x.R.RelicVar(Id, 0) - x.B.Relics[self.Index].Counter);
    }

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver == self.Owner)
            x.B.Relics[self.Index].Counter += result.UnblockedDamage;
    }

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>LizardTail</c>: <c>ShouldDieLate</c> prevents its owner's first death of the combat, then
/// <c>AfterPreventingDeath</c> marks it used and heals Heal percent of max HP
/// (<c>DeathPreventerMirrors.LizardTailMirrors</c>). Counter = WasUsed (0/1); the bridge projects
/// <c>LizardTailPredictionState.WasUsed</c>.
/// </summary>
[FlashContent("LIZARD_TAIL")]
public sealed class LizardTail : FlashRelicDef
{
    // LizardTail.cs: HealVar(50m).
    public override IReadOnlyList<string> VarNames => ["Heal"];

    public override bool ShouldDieLate(ref FlashExec x, FlashListener self, byte creature)
        => creature != self.Owner || x.B.Relics[self.Index].Counter != 0;

    public override void AfterPreventingDeath(ref FlashExec x, FlashListener self, byte creature)
    {
        x.B.Relics[self.Index].Counter = 1;
        decimal amount = Math.Max(1m, x.B.Creature(creature).MaxHp * (x.R.RelicVar(Id, 0) / 100m));
        FlashKernel.Heal(ref x, creature, amount);
    }
}

/// <summary>
/// <c>RedSkull</c>: whenever any creature's current HP changes, the relic keeps Strength applied
/// exactly while its owner is at or below the HP threshold
/// (<c>AfterCurrentHpChangedMirrors.HandleRedSkull</c>; the native room-entry application happens
/// before the root and is imported). Counter = the engine's stateful StrengthApplied flag (0/1),
/// projected by <c>ClaimStatefulRelic</c>.
/// </summary>
[FlashContent("RED_SKULL")]
public sealed class RedSkull : FlashRelicDef
{
    // RedSkull.cs: DynamicVar("HpThreshold", 50m), PowerVar<StrengthPower>(3m).
    public override IReadOnlyList<string> VarNames => ["HpThreshold", "StrengthPower"];

    public override void AfterCurrentHpChanged(ref FlashExec x, FlashListener self, byte creature, decimal delta)
    {
        decimal threshold = x.B.Player.MaxHp * (x.R.RelicVar(Id, 0) / 100m);
        bool shouldApply = x.B.Player.CurrentHp <= threshold;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (shouldApply == (relic.Counter != 0))
            return;
        int strength = (int)x.R.RelicVar(Id, 1);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<StrengthPower>(),
            shouldApply ? strength : -strength, FlashLimits.PlayerOwner);
        relic.Counter = shouldApply ? 1 : 0;
    }
}
