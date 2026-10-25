using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Relics;

/// <summary>
/// <c>TwistedFunnel</c>: at the first player side turn start applies Poison to every hittable enemy.
/// The first turn starts before the captured root (the existing engine registers it as ignored for
/// the same reason), so the definition has no combat hook.
/// </summary>
[BlitzContent("TWISTED_FUNNEL")]
public sealed class TwistedFunnel : BlitzRelicDef
{
}

/// <summary>
/// <c>BookRepairKnife</c>: heals its owner after a Doom kill. The Doom settlement
/// (<c>BlitzKernel.DoomKill</c>) already reads the relic's Heal variable; this definition exists so a
/// root holding it can be admitted and provides the variable.
/// </summary>
[BlitzContent("BOOK_REPAIR_KNIFE")]
public sealed class BookRepairKnife : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Heal"];
}

/// <summary>
/// <c>FuneraryMask</c>: adds 3 Souls to the draw pile at the first <c>BeforeHandDraw</c>, before the
/// captured root; the Souls and their random positions are already in the root.
/// </summary>
[BlitzContent("FUNERARY_MASK")]
public sealed class FuneraryMask : BlitzRelicDef
{
}

/// <summary>
/// <c>Tingsha</c>: after a card is discarded, while its owner is the current side, deal 3 unpowered
/// damage to a random hittable enemy (<c>AfterCardDiscardedMirrors.HandleTingsha</c>).
/// </summary>
[BlitzContent("TINGSHA")]
public sealed class Tingsha : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterCardDiscarded(ref BlitzExec x, BlitzListener self, in BlitzCard card)
    {
        if (x.B.Side != BlitzSide.Player)
            return;
        BlitzCardEffects.DamageRandomEnemy(ref x, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered,
            BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Candelabra</c>: <c>AfterSideTurnStart</c>, player turn 2, gains 2 energy
/// (<c>SimulatedCombatState.RelicTurnStart.TriggerRelicsAfterSideTurnStart</c>). Turn 2 is reached after
/// the root through <c>AdvanceRound</c>.
/// </summary>
[BlitzContent("CANDELABRA")]
public sealed class Candelabra : BlitzRelicDef
{
    // Candelabra.cs: EnergyVar(2), _energyTurn = 2.
    private const int EnergyTurn = 2;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side == BlitzSide.Player && x.B.PlayerTurn == EnergyTurn)
            BlitzKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>HornCleat</c>: <c>AfterBlockCleared</c>, player turn 2, gains 14 unpowered block
/// (<c>TriggerRelicsAfterBlockCleared</c>).
/// </summary>
[BlitzContent("HORN_CLEAT")]
public sealed class HornCleat : BlitzRelicDef
{
    // HornCleat.cs: BlockVar(14m, ValueProp.Unpowered); block turn 2 is a code constant.
    private const int BlockTurn = 2;

    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterBlockCleared(ref BlitzExec x, BlitzListener self)
    {
        if (x.B.PlayerTurn == BlockTurn)
            BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.R.RelicVar(Id, 0),
                BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>SparklingRouge</c>: <c>AfterBlockCleared</c>, player turn 3, applies 1 Strength and 1 Dexterity
/// (<c>TriggerRelicsAfterBlockCleared</c>).
/// </summary>
[BlitzContent("SPARKLING_ROUGE")]
public sealed class SparklingRouge : BlitzRelicDef
{
    // SparklingRouge.cs: PowerVar<StrengthPower>(1m), PowerVar<DexterityPower>(1m); block turn 3 is
    // a code constant.
    private const int BlockTurn = 3;

    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    public override void AfterBlockCleared(ref BlitzExec x, BlitzListener self)
    {
        if (x.B.PlayerTurn != BlockTurn)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<StrengthPower>(),
            (int)x.R.RelicVar(Id, 0), BlitzLimits.PlayerOwner);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<DexterityPower>(),
            (int)x.R.RelicVar(Id, 1), BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>RippleBasin</c>: <c>BeforeSideTurnEnd</c>, if no attack card was played this turn, gains 4 block
/// (<c>BeforeSideTurnEndMirrors.HandleRippleBasin</c>; Blitz's
/// <see cref="BlitzCounter.AttacksPlayedThisTurn"/> is the branch-local counterpart of
/// <c>GetAttacksPlayedThisTurn</c>).
/// </summary>
[BlitzContent("RIPPLE_BASIN")]
public sealed class RippleBasin : BlitzRelicDef
{
    // RippleBasin.cs: BlockVar(4m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void BeforeSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        if (!BlitzKernel.Participates(participants, self.Owner) || x.B.Counter(BlitzCounter.AttacksPlayedThisTurn) != 0)
            return;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>MercuryHourglass</c>: <c>AfterPlayerTurnStart</c> deals 3 unpowered damage to every hittable enemy
/// on every player turn (<c>ContinueRelicsAfterPlayerTurnStart</c>).
/// </summary>
[BlitzContent("MERCURY_HOURGLASS")]
public sealed class MercuryHourglass : BlitzRelicDef
{
    // MercuryHourglass.cs: DamageVar(3m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterPlayerTurnStart(ref BlitzExec x, BlitzListener self)
        => BlitzCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered,
            BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>MiniatureCannon</c>: <c>ModifyDamageAdditive</c> adds 3 to powered attacks from an upgraded card
/// (<c>ModifyDamageMirrors</c> native fallback; <c>PredictionCoverage.IsVerifiedNativeRelicHook</c>).
/// </summary>
[BlitzContent("MINIATURE_CANNON")]
public sealed class MiniatureCannon : BlitzRelicDef
{
    // MiniatureCannon.cs: DynamicVar("ExtraDamage", 3m).
    public override IReadOnlyList<string> VarNames => ["ExtraDamage"];

    public override decimal ModifyDamageAdditive(ref BlitzExec x, BlitzListener self,
        in BlitzDamageContext damage, decimal amount)
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
[BlitzContent("PARRYING_SHIELD")]
public sealed class ParryingShield : BlitzRelicDef
{
    // ParryingShield.cs: BlockVar(10m, ValueProp.Unpowered), DamageVar(6m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block", "Damage"];

    public override void AfterSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player || x.B.Creature(BlitzLimits.PlayerOwner).Block < x.R.RelicVar(Id, 0))
            return;
        BlitzCardEffects.DamageRandomEnemy(ref x, x.R.RelicVar(Id, 1), BlitzValueProps.Unpowered,
            BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>PaperPhrog</c>: the relic has no hook of its own; <c>VulnerablePower</c> adds 0.25 to its
/// multiplier when the owner deals the powered attack (native <c>ModifyVulnerableMultiplier</c>,
/// called from <c>ModifyDamageMirrors.HandleVulnerablePower</c>). The empty definition admits roots
/// holding it; the power carries the effect.
/// </summary>
[BlitzContent("PAPER_PHROG")]
public sealed class PaperPhrog : BlitzRelicDef;

/// <summary>
/// <c>GremlinHorn</c>: <c>AfterDeath</c> of an enemy gains Energy and draws Cards
/// (<c>AfterDeathMirrors.HandleGremlinHorn</c>).
/// </summary>
[BlitzContent("GREMLIN_HORN")]
public sealed class GremlinHorn : BlitzRelicDef
{
    // GremlinHorn.cs: EnergyVar(1), CardsVar(1).
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    public override void AfterDeath(ref BlitzExec x, BlitzListener self, byte creature, bool wasRemovalPrevented)
    {
        if (!BlitzKernel.IsEnemyOwner(creature))
            return;
        BlitzKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, (int)x.R.RelicVar(Id, 1), fromHandDraw: false);
    }
}

/// <summary>
/// <c>SelfFormingClay</c>: <c>AfterDamageReceived</c> applies <c>BlockNextTurn</c> worth of
/// <see cref="SelfFormingClayPower"/> on unblocked damage to the owner
/// (<c>AfterDamageReceivedMirrors.HandleSelfFormingClay</c>).
/// </summary>
[BlitzContent("SELF_FORMING_CLAY")]
public sealed class SelfFormingClay : BlitzRelicDef
{
    // SelfFormingClay.cs: DynamicVar("BlockNextTurn", 3m).
    public override IReadOnlyList<string> VarNames => ["BlockNextTurn"];

    public override void AfterDamageReceived(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result,
        byte dealer)
    {
        if (!BlitzKernel.IsInProgress(x.B) || result.Receiver != BlitzLimits.PlayerOwner
            || result.UnblockedDamage <= 0)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<SelfFormingClayPower>(),
            (int)x.R.RelicVar(Id, 0), BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Akabeko</c>: <c>AfterSideTurnStart</c> applies Vigor only when the player turn number is &lt;= 1
/// (<c>SimulatedCombatState.RelicTurnStart.TriggerRelicsAfterSideTurnStart</c>). The player-side turn 1
/// start precedes the root; the relic owner never participates in the enemy side's relic batch
/// (<c>RelicsParticipatingInSideTurn</c> requires the owner creature in the participants), so no later
/// side can trigger it.
/// </summary>
[BlitzContent("AKABEKO")]
public sealed class Akabeko : BlitzRelicDef;

/// <summary>
/// <c>Pantograph</c>: <c>BeforeCombatStart</c> heals 25 only in a boss room, during combat setup; no later
/// hook reads it.
/// </summary>
[BlitzContent("PANTOGRAPH")]
public sealed class Pantograph : BlitzRelicDef;

/// <summary>
/// <c>PetrifiedToad</c>: <c>BeforeCombatStartLate</c> procures a PotionShapedRock before the combat root;
/// the potion is part of the imported starting state and there is no later hook.
/// </summary>
[BlitzContent("PETRIFIED_TOAD")]
public sealed class PetrifiedToad : BlitzRelicDef;

/// <summary>
/// <c>StoneCracker</c>: <c>AfterRoomEntered</c> upgrades two upgradable draw-pile cards when entering a
/// combat room, before the root; the upgraded cards are imported.
/// </summary>
[BlitzContent("STONE_CRACKER")]
public sealed class StoneCracker : BlitzRelicDef;

/// <summary><c>BowlerHat</c>: <c>ModifyGoldGained</c>/<c>AfterModifyingGoldGained</c> only; the existing engine registers it in <c>GoldGainedMirrors</c>, never as a combat hook.</summary>
[BlitzContent("BOWLER_HAT")]
public sealed class BowlerHat : BlitzRelicDef;

/// <summary><c>EternalFeather</c>: <c>AfterRoomEntered</c> rest-site heal only.</summary>
[BlitzContent("ETERNAL_FEATHER")]
public sealed class EternalFeather : BlitzRelicDef;

/// <summary><c>LastingCandy</c>: <c>BeforeCombatRewardOffered</c>, reward card options and <c>IsAllowed</c> only.</summary>
[BlitzContent("LASTING_CANDY")]
public sealed class LastingCandy : BlitzRelicDef;

/// <summary><c>LuckyFysh</c>: <c>AfterCardChangedPiles</c> only for deck changes, plus <c>IsAllowed</c>.</summary>
[BlitzContent("LUCKY_FYSH")]
public sealed class LuckyFysh : BlitzRelicDef;

/// <summary><c>Pear</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[BlitzContent("PEAR")]
public sealed class Pear : BlitzRelicDef;

/// <summary><c>Planisphere</c>: <c>AfterRoomEntered</c> and <c>IsAllowed</c> only.</summary>
[BlitzContent("PLANISPHERE")]
public sealed class Planisphere : BlitzRelicDef;

/// <summary><c>TinyMailbox</c>: rest-site heal reward hooks only.</summary>
[BlitzContent("TINY_MAILBOX")]
public sealed class TinyMailbox : BlitzRelicDef;

/// <summary>
/// <c>JossPaper</c>: exhausted cards advance a counter and draw per threshold; Ethereal exhausts are
/// deferred to <c>AfterSideTurnEnd</c> (<c>AfterCardExhaustedMirrors.HandleJossPaper</c> and the
/// native <c>AfterSideTurnEnd</c>).
/// Counter = exhausts toward the threshold, Aux = Ethereal exhausts awaiting the turn-end flush.
/// </summary>
[BlitzContent("JOSS_PAPER")]
public sealed class JossPaper : BlitzRelicDef
{
    // JossPaper.cs: DynamicVar("ExhaustAmount", 5m).
    public override IReadOnlyList<string> VarNames => ["ExhaustAmount"];

    public override void AfterCardExhausted(ref BlitzExec x, BlitzListener self, in BlitzCard card, bool causedByEthereal)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (causedByEthereal)
        {
            relic.Aux++;
            return;
        }
        relic.Counter++;
        DrawIfThresholdMet(ref x, ref relic);
    }

    public override void AfterSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter += relic.Aux;
        relic.Aux = 0;
        DrawIfThresholdMet(ref x, ref relic);
    }

    private void DrawIfThresholdMet(ref BlitzExec x, ref BlitzRelic relic)
    {
        int threshold = (int)x.R.RelicVar(Id, 0);
        if (relic.Counter < threshold)
            return;
        int draws = relic.Counter / threshold;
        BlitzKernel.Draw(ref x, draws, fromHandDraw: false);
        relic.Counter %= threshold;
    }
}

/// <summary>
/// <c>Kusarigama</c>: every third attack played this turn deals its Damage to a random enemy;
/// <c>AfterSideTurnEnd</c> resets the counter (<c>AfterCardPlayedMirrors.HandleKusarigama</c>,
/// <c>RelicPredictionStateSupport.ResetAfterSideTurnEnd</c>).
/// Counter = attacks played this turn.
/// </summary>
[BlitzContent("KUSARIGAMA")]
public sealed class Kusarigama : BlitzRelicDef
{
    // Kusarigama.cs: CardsVar(3), DamageVar(6m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Cards", "Damage"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Attack)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (relic.Counter % (int)x.R.RelicVar(Id, 0) != 0)
            return;
        BlitzCardEffects.DamageRandomEnemy(ref x, x.R.RelicVar(Id, 1), BlitzValueProps.Unpowered,
            BlitzLimits.PlayerOwner);
    }

    public override void AfterSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side == BlitzSide.Player)
            x.B.Relics[self.Index].Counter = 0;
    }
}

/// <summary>
/// <c>LetterOpener</c>: every third Skill played this turn deals its Damage to all enemies;
/// <c>AfterSideTurnStart</c> resets the counter after turn 1
/// (<c>AfterCardPlayedMirrors.HandleLetterOpener</c>, <c>ResetAfterSideTurnStart</c>).
/// Counter = Skills played this turn.
/// </summary>
[BlitzContent("LETTER_OPENER")]
public sealed class LetterOpener : BlitzRelicDef
{
    // LetterOpener.cs: CardsVar(3), DamageVar(5m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Cards", "Damage"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (!BlitzKernel.IsInProgress(x.B) || BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Skill)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (relic.Counter % (int)x.R.RelicVar(Id, 0) != 0)
            return;
        BlitzCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 1), BlitzValueProps.Unpowered,
            BlitzLimits.PlayerOwner);
    }

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side == BlitzSide.Player && x.B.PlayerTurn > 1)
            x.B.Relics[self.Index].Counter = 0;
    }
}

/// <summary>
/// <c>Nunchaku</c>: every tenth attack played grants Energy; the counter survives turns
/// (<c>AfterCardPlayedMirrors.HandleNunchaku</c>).
/// Counter = attacks played this combat.
/// </summary>
[BlitzContent("NUNCHAKU")]
public sealed class Nunchaku : BlitzRelicDef
{
    // Nunchaku.cs: CardsVar(10), EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Cards", "Energy"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Attack)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (!BlitzKernel.IsInProgress(x.B) || relic.Counter % (int)x.R.RelicVar(Id, 0) != 0)
            return;
        BlitzKernel.GainEnergy(ref x, x.R.RelicVar(Id, 1));
    }
}

/// <summary>
/// <c>Orichalcum</c>: <c>BeforeSideTurnEndVeryEarly</c> latches when the player has no block;
/// <c>BeforeSideTurnEnd</c> spends the latch for its Block; <c>BeforeSideTurnStart</c> clears it
/// (<c>OrichalcumMirrors</c>).
/// Counter = latch set by the VeryEarly pass (always 0 at a stable boundary).
/// </summary>
[BlitzContent("ORICHALCUM")]
public sealed class Orichalcum : BlitzRelicDef
{
    // Orichalcum.cs: BlockVar(6m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void BeforeSideTurnEndVeryEarly(ref BlitzExec x, BlitzListener self, BlitzSide side,
        ushort participants)
    {
        if (BlitzKernel.Participates(participants, self.Owner)
            && x.B.Creature(self.Owner).Block <= 0)
            x.B.Relics[self.Index].Counter = 1;
    }

    public override void BeforeSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter == 0)
            return;
        relic.Counter = 0;
        BlitzKernel.GainBlock(ref x, self.Owner, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered, fromCard: false);
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>OrnamentalFan</c>: every third attack played this turn gains its Block;
/// <c>BeforeSideTurnStart</c> resets the counter (<c>AfterCardPlayedMirrors.HandleOrnamentalFan</c>).
/// Counter = attacks played this turn.
/// </summary>
[BlitzContent("ORNAMENTAL_FAN")]
public sealed class OrnamentalFan : BlitzRelicDef
{
    // OrnamentalFan.cs: CardsVar(3), BlockVar(4m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Cards", "Block"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (!BlitzKernel.IsInProgress(x.B) || BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Attack)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (relic.Counter % (int)x.R.RelicVar(Id, 0) != 0)
            return;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.R.RelicVar(Id, 1),
            BlitzValueProps.Unpowered, fromCard: false);
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>PenNib</c>: <c>BeforeCardPlayed</c> advances the attack counter; the tenth attack doubles its
/// own damage and the latch clears when that card finishes (<c>BeforeCardPlayedMirrors.HandlePenNib</c>,
/// <c>ModifyDamageMirrors.HandlePenNib</c>, <c>AfterCardPlayedMirrors.HandlePenNib</c>).
/// Counter = attacks played modulo 10.
/// </summary>
[BlitzContent("PEN_NIB")]
public sealed class PenNib : BlitzRelicDef
{
    // PenNib.cs: _attacksThreshold = 10 (a code constant, not a DynamicVar).
    private const int AttacksThreshold = 10;

    // The native latch is the CardPlay whose damage to double (PenNibPredictionState.AttackToDouble).
    // Blitz stores that play's one-action identity instead of a boolean: a Sly card auto-played by the
    // tenth attack's own selection is a different CardPlay and must not inherit the doubling.
    // Aux is 0 at every stable boundary because the triggering card's AfterCardPlayed clears it.
    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Attack)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter = (relic.Counter + 1) % AttacksThreshold;
        if (relic.Counter == 0)
            relic.Aux = x.PlayIdentity;
    }

    public override decimal ModifyDamageMultiplicative(ref BlitzExec x, BlitzListener self,
        in BlitzDamageContext damage, decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource
            || damage.Dealer != BlitzLimits.PlayerOwner)
            return 1m;
        // Native: `AttackToDouble == context.CardSource.Original`. The play identity names the
        // CardPlay that owns the latch; a nested auto-play has already replaced x.PlayIdentity.
        return x.B.Relics[self.Index].Aux != 0 && x.PlayIdentity == x.B.Relics[self.Index].Aux
            ? 2m
            : 1m;
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (relic.Aux != 0 && relic.Aux == x.LastPlayIdentity)
            relic.Aux = 0;
    }
}

/// <summary>
/// <c>Permafrost</c>: the first Power card played this combat gains its block
/// (<c>AfterCardPlayedMirrors.HandlePermafrost</c>).
/// Counter = 1 once used this combat.
/// </summary>
[BlitzContent("PERMAFROST")]
public sealed class Permafrost : BlitzRelicDef
{
    // Permafrost.cs: BlockVar(7m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (!BlitzKernel.IsInProgress(x.B)
            || BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Power
            || x.B.Relics[self.Index].Counter != 0)
            return;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            BlitzValueProps.Unpowered, fromCard: false);
        x.B.Relics[self.Index].Counter = 1;
    }
}

/// <summary>
/// <c>TuningFork</c>: every tenth Skill played gains its Block and the counter wraps
/// (<c>AfterCardPlayedMirrors.HandleTuningFork</c>). Counter = Skills played toward the threshold.
/// </summary>
[BlitzContent("TUNING_FORK")]
public sealed class TuningFork : BlitzRelicDef
{
    // TuningFork.cs: CardsVar(10), BlockVar(7m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Cards", "Block"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Skill)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        int threshold = (int)x.R.RelicVar(Id, 0);
        if (relic.Counter < threshold)
            return;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.R.RelicVar(Id, 1),
            BlitzValueProps.Unpowered, fromCard: false);
        relic.Counter -= threshold;
    }
}
