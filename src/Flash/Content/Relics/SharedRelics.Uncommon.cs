using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Relics;

/// <summary>
/// <c>TwistedFunnel</c>: at the first player side turn start applies Poison to every hittable enemy.
/// The first turn starts before the captured root (the existing engine registers it as ignored for
/// the same reason), so the definition has no combat hook.
/// </summary>
[FlashContent("TWISTED_FUNNEL")]
public sealed class TwistedFunnel : FlashRelicDef
{
}

/// <summary>
/// <c>BookRepairKnife</c>: heals its owner after a Doom kill. The Doom settlement
/// (<c>FlashKernel.DoomKill</c>) already reads the relic's Heal variable; this definition exists so a
/// root holding it can be admitted and provides the variable.
/// </summary>
[FlashContent("BOOK_REPAIR_KNIFE")]
public sealed class BookRepairKnife : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Heal"];
}

/// <summary>
/// <c>FuneraryMask</c>: adds 3 Souls to the draw pile at the first <c>BeforeHandDraw</c>, before the
/// captured root; the Souls and their random positions are already in the root.
/// </summary>
[FlashContent("FUNERARY_MASK")]
public sealed class FuneraryMask : FlashRelicDef
{
}

/// <summary>
/// <c>Tingsha</c>: after a card is discarded, while its owner is the current side, deal 3 unpowered
/// damage to a random hittable enemy (<c>AfterCardDiscardedMirrors.HandleTingsha</c>).
/// </summary>
[FlashContent("TINGSHA")]
public sealed class Tingsha : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterCardDiscarded(ref FlashExec x, FlashListener self, in FlashCard card)
    {
        if (x.B.Side != FlashSide.Player)
            return;
        FlashCardEffects.DamageRandomEnemy(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Candelabra</c>: <c>AfterSideTurnStart</c>, player turn 2, gains 2 energy
/// (<c>SimulatedCombatState.RelicTurnStart.TriggerRelicsAfterSideTurnStart</c>). Turn 2 is reached after
/// the root through <c>AdvanceRound</c>.
/// </summary>
[FlashContent("CANDELABRA")]
public sealed class Candelabra : FlashRelicDef
{
    // Candelabra.cs: EnergyVar(2), _energyTurn = 2.
    private const int EnergyTurn = 2;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side == FlashSide.Player && x.B.PlayerTurn == EnergyTurn)
            FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>HornCleat</c>: <c>AfterBlockCleared</c>, player turn 2, gains 14 unpowered block
/// (<c>TriggerRelicsAfterBlockCleared</c>).
/// </summary>
[FlashContent("HORN_CLEAT")]
public sealed class HornCleat : FlashRelicDef
{
    // HornCleat.cs: BlockVar(14m, ValueProp.Unpowered); block turn 2 is a code constant.
    private const int BlockTurn = 2;

    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterBlockCleared(ref FlashExec x, FlashListener self)
    {
        if (x.B.PlayerTurn == BlockTurn)
            FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
                FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>SparklingRouge</c>: <c>AfterBlockCleared</c>, player turn 3, applies 1 Strength and 1 Dexterity
/// (<c>TriggerRelicsAfterBlockCleared</c>).
/// </summary>
[FlashContent("SPARKLING_ROUGE")]
public sealed class SparklingRouge : FlashRelicDef
{
    // SparklingRouge.cs: PowerVar<StrengthPower>(1m), PowerVar<DexterityPower>(1m); block turn 3 is
    // a code constant.
    private const int BlockTurn = 3;

    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    public override void AfterBlockCleared(ref FlashExec x, FlashListener self)
    {
        if (x.B.PlayerTurn != BlockTurn)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<StrengthPower>(),
            (int)x.R.RelicVar(Id, 0), FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<DexterityPower>(),
            (int)x.R.RelicVar(Id, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>RippleBasin</c>: <c>BeforeSideTurnEnd</c>, if no attack card was played this turn, gains 4 block
/// (<c>BeforeSideTurnEndMirrors.HandleRippleBasin</c>; Flash's
/// <see cref="FlashCounter.AttacksPlayedThisTurn"/> is the branch-local counterpart of
/// <c>GetAttacksPlayedThisTurn</c>).
/// </summary>
[FlashContent("RIPPLE_BASIN")]
public sealed class RippleBasin : FlashRelicDef
{
    // RippleBasin.cs: BlockVar(4m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (!FlashKernel.Participates(participants, self.Owner) || x.B.Counter(FlashCounter.AttacksPlayedThisTurn) != 0)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>MercuryHourglass</c>: <c>AfterPlayerTurnStart</c> deals 3 unpowered damage to every hittable enemy
/// on every player turn (<c>ContinueRelicsAfterPlayerTurnStart</c>).
/// </summary>
[FlashContent("MERCURY_HOURGLASS")]
public sealed class MercuryHourglass : FlashRelicDef
{
    // MercuryHourglass.cs: DamageVar(3m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
        => FlashCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>MiniatureCannon</c>: <c>ModifyDamageAdditive</c> adds 3 to powered attacks from an upgraded card
/// (<c>ModifyDamageMirrors</c> native fallback; <c>PredictionCoverage.IsVerifiedNativeRelicHook</c>).
/// </summary>
[FlashContent("MINIATURE_CANNON")]
public sealed class MiniatureCannon : FlashRelicDef
{
    // MiniatureCannon.cs: DynamicVar("ExtraDamage", 3m).
    public override IReadOnlyList<string> VarNames => ["ExtraDamage"];

    public override decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self,
        in FlashDamageContext damage, decimal amount)
    {
        // Native condition: reject only when neither the dealer is the owner's creature nor the
        // card source belongs to the owner. An Osty attack has the pet as dealer but the player's
        // card as source, so it still gains the bonus.
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource)
            return 0m;
        return damage.CardSource.Upgrade > 0 ? x.R.RelicVar(Id, 0) : 0m;
    }
}

/// <summary>
/// <c>ParryingShield</c>: <c>AfterSideTurnEnd</c>, when the player has at least 10 block, deals 6 unpowered
/// damage to one random hittable enemy (<c>SimulatedCombatState.ReactiveRelics.TriggerRelicsAfterSideTurnEnd</c>).
/// </summary>
[FlashContent("PARRYING_SHIELD")]
public sealed class ParryingShield : FlashRelicDef
{
    // ParryingShield.cs: BlockVar(10m, ValueProp.Unpowered), DamageVar(6m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block", "Damage"];

    public override void AfterSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player || x.B.Creature(FlashLimits.PlayerOwner).Block < x.R.RelicVar(Id, 0))
            return;
        FlashCardEffects.DamageRandomEnemy(ref x, x.R.RelicVar(Id, 1), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>PaperPhrog</c>: the relic has no hook of its own; <c>VulnerablePower</c> adds 0.25 to its
/// multiplier when the owner deals the powered attack (native <c>ModifyVulnerableMultiplier</c>,
/// called from <c>ModifyDamageMirrors.HandleVulnerablePower</c>). The empty definition admits roots
/// holding it; the power carries the effect.
/// </summary>
[FlashContent("PAPER_PHROG")]
public sealed class PaperPhrog : FlashRelicDef;

/// <summary>
/// <c>GremlinHorn</c>: <c>AfterDeath</c> of an enemy gains Energy and draws Cards
/// (<c>AfterDeathMirrors.HandleGremlinHorn</c>).
/// </summary>
[FlashContent("GREMLIN_HORN")]
public sealed class GremlinHorn : FlashRelicDef
{
    // GremlinHorn.cs: EnergyVar(1), CardsVar(1).
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    public override void AfterDeath(ref FlashExec x, FlashListener self, byte creature, bool wasRemovalPrevented)
    {
        if (!FlashKernel.IsEnemyOwner(creature))
            return;
        FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)x.R.RelicVar(Id, 1), fromHandDraw: false);
    }
}

/// <summary>
/// <c>SelfFormingClay</c>: <c>AfterDamageReceived</c> applies <c>BlockNextTurn</c> worth of
/// <see cref="SelfFormingClayPower"/> on unblocked damage to the owner
/// (<c>AfterDamageReceivedMirrors.HandleSelfFormingClay</c>).
/// </summary>
[FlashContent("SELF_FORMING_CLAY")]
public sealed class SelfFormingClay : FlashRelicDef
{
    // SelfFormingClay.cs: DynamicVar("BlockNextTurn", 3m).
    public override IReadOnlyList<string> VarNames => ["BlockNextTurn"];

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result,
        byte dealer)
    {
        if (!FlashKernel.IsInProgress(x.B) || result.Receiver != FlashLimits.PlayerOwner
            || result.UnblockedDamage <= 0)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<SelfFormingClayPower>(),
            (int)x.R.RelicVar(Id, 0), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Akabeko</c>: <c>AfterSideTurnStart</c> applies Vigor only when the player turn number is &lt;= 1
/// (<c>SimulatedCombatState.RelicTurnStart.TriggerRelicsAfterSideTurnStart</c>). The player-side turn 1
/// start precedes the root; the relic owner never participates in the enemy side's relic batch
/// (<c>RelicsParticipatingInSideTurn</c> requires the owner creature in the participants), so no later
/// side can trigger it.
/// </summary>
[FlashContent("AKABEKO")]
public sealed class Akabeko : FlashRelicDef;

/// <summary>
/// <c>Pantograph</c>: <c>BeforeCombatStart</c> heals 25 only in a boss room, during combat setup; no later
/// hook reads it.
/// </summary>
[FlashContent("PANTOGRAPH")]
public sealed class Pantograph : FlashRelicDef;

/// <summary>
/// <c>PetrifiedToad</c>: <c>BeforeCombatStartLate</c> procures a PotionShapedRock before the combat root;
/// the potion is part of the imported starting state and there is no later hook.
/// </summary>
[FlashContent("PETRIFIED_TOAD")]
public sealed class PetrifiedToad : FlashRelicDef;

/// <summary>
/// <c>StoneCracker</c>: <c>AfterRoomEntered</c> upgrades two upgradable draw-pile cards when entering a
/// combat room, before the root; the upgraded cards are imported.
/// </summary>
[FlashContent("STONE_CRACKER")]
public sealed class StoneCracker : FlashRelicDef;

/// <summary><c>BowlerHat</c>: <c>ModifyGoldGained</c>/<c>AfterModifyingGoldGained</c> only; the existing engine registers it in <c>GoldGainedMirrors</c>, never as a combat hook.</summary>
[FlashContent("BOWLER_HAT")]
public sealed class BowlerHat : FlashRelicDef;

/// <summary><c>EternalFeather</c>: <c>AfterRoomEntered</c> rest-site heal only.</summary>
[FlashContent("ETERNAL_FEATHER")]
public sealed class EternalFeather : FlashRelicDef;

/// <summary><c>LastingCandy</c>: <c>BeforeCombatRewardOffered</c>, reward card options and <c>IsAllowed</c> only.</summary>
[FlashContent("LASTING_CANDY")]
public sealed class LastingCandy : FlashRelicDef;

/// <summary><c>LuckyFysh</c>: <c>AfterCardChangedPiles</c> only for deck changes, plus <c>IsAllowed</c>.</summary>
[FlashContent("LUCKY_FYSH")]
public sealed class LuckyFysh : FlashRelicDef;

/// <summary><c>Pear</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[FlashContent("PEAR")]
public sealed class Pear : FlashRelicDef;

/// <summary><c>Planisphere</c>: <c>AfterRoomEntered</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("PLANISPHERE")]
public sealed class Planisphere : FlashRelicDef;

/// <summary><c>TinyMailbox</c>: rest-site heal reward hooks only.</summary>
[FlashContent("TINY_MAILBOX")]
public sealed class TinyMailbox : FlashRelicDef;

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
        if (FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Attack)
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
        if (!FlashKernel.IsInProgress(x.B) || FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Skill)
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
        if (FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Attack)
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
        if (!FlashKernel.IsInProgress(x.B) || FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Attack)
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
/// Counter = attacks played modulo 10.
/// </summary>
[FlashContent("PEN_NIB")]
public sealed class PenNib : FlashRelicDef
{
    // PenNib.cs: _attacksThreshold = 10 (a code constant, not a DynamicVar).
    private const int AttacksThreshold = 10;

    // The native latch is the CardPlay whose damage to double (PenNibPredictionState.AttackToDouble).
    // Flash stores that play's one-action identity instead of a boolean: a Sly card auto-played by the
    // tenth attack's own selection is a different CardPlay and must not inherit the doubling.
    // Aux is 0 at every stable boundary because the triggering card's AfterCardPlayed clears it.
    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Attack)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter = (relic.Counter + 1) % AttacksThreshold;
        if (relic.Counter == 0)
            relic.Aux = x.PlayIdentity;
    }

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self,
        in FlashDamageContext damage, decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource
            || damage.Dealer != FlashLimits.PlayerOwner)
            return 1m;
        // Native: `AttackToDouble == context.CardSource.Original`. The play identity names the
        // CardPlay that owns the latch; a nested auto-play has already replaced x.PlayIdentity.
        return x.B.Relics[self.Index].Aux != 0 && x.PlayIdentity == x.B.Relics[self.Index].Aux
            ? 2m
            : 1m;
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (relic.Aux != 0 && relic.Aux == x.LastPlayIdentity)
            relic.Aux = 0;
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
            || FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Power
            || x.B.Relics[self.Index].Counter != 0)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            FlashValueProps.Unpowered, fromCard: false);
        x.B.Relics[self.Index].Counter = 1;
    }
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
        if (FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Skill)
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
