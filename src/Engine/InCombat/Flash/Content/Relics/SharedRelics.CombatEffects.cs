using CombatSolver.Engine.InCombat.Flash.Content.Powers;

namespace CombatSolver.Engine.InCombat.Flash.Content.Relics;

// Class 3/4 relic batch R2: combat effects implemented through an existing hook or turn-phase relic
// batch. The existing-engine location is named on each definition. Numeric values come from the
// per-root relic rule table (FlashRelicDef.VarNames + x.R.RelicVar); native code constants stay here
// with the decompiled source named.

/// <summary>
/// <c>TungstenRod</c>: <c>ModifyHpLostAfterOsty</c> reduces the owner's HP loss by
/// <c>HpLossReduction</c> (<c>ModifyHpLostMirrors</c> native fallback,
/// <c>PredictionCoverage.IsVerifiedNativeRelicHook</c>). <c>AfterModifyingHpLostAfterOsty</c> only flashes.
/// </summary>
[FlashContent("TUNGSTEN_ROD")]
public sealed class TungstenRod : FlashRelicDef
{
    // TungstenRod.cs: DynamicVar("HpLossReduction", 1m).
    public override IReadOnlyList<string> VarNames => ["HpLossReduction"];

    public override decimal ModifyHpLostAfterOsty(ref FlashExec x, FlashListener self, in FlashHpLossContext loss,
        decimal amount)
    {
        if (loss.Target != self.Owner)
            return amount;
        return Math.Max(0m, amount - x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>TheBoot</c>: <c>ModifyHpLostAfterOstyLate</c> raises a powered attack from the owner to another
/// creature to at least <c>DamageMinimum</c> when the truncated amount is between 1 and below it
/// (<c>ModifyHpLostMirrors</c> native fallback). Osty is rejected at admission, so only the owner can
/// deal the attack.
/// </summary>
[FlashContent("THE_BOOT")]
public sealed class TheBoot : FlashRelicDef
{
    // TheBoot.cs: DynamicVar("DamageMinimum", 5m); _damageMinimum and DamageThreshold are constants and
    // the threshold is not read by the hook.
    public override IReadOnlyList<string> VarNames => ["DamageMinimum"];

    public override decimal ModifyHpLostAfterOstyLate(ref FlashExec x, FlashListener self, in FlashHpLossContext loss,
        decimal amount)
    {
        if (loss.Dealer != self.Owner || loss.Target == self.Owner)
            return amount;
        if (!loss.Props.IsPoweredAttack() || amount < 1m)
            return amount;
        decimal minimum = x.R.RelicVar(Id, 0);
        return amount >= minimum ? amount : minimum;
    }
}

/// <summary>
/// <c>FakeStrikeDummy</c>: <c>ModifyDamageAdditive</c> adds <c>ExtraDamage</c> to a powered attack whose
/// card has the Strike tag (<c>ModifyDamageMirrors</c> native fallback).
/// </summary>
[FlashContent("FAKE_STRIKE_DUMMY")]
public sealed class FakeStrikeDummy : FlashRelicDef
{
    // FakeStrikeDummy.cs: DynamicVar("ExtraDamage", 1m).
    public override IReadOnlyList<string> VarNames => ["ExtraDamage"];

    public override decimal ModifyDamageAdditive(ref FlashExec x, FlashListener self, in FlashDamageContext damage,
        decimal amount)
    {
        if (!damage.Props.IsPoweredAttack() || !damage.HasCardSource)
            return 0m;
        return (x.R.Card(damage.CardSource.Id, damage.CardSource.Upgrade).Tags & FlashCardTag.Strike) != 0
            ? x.R.RelicVar(Id, 0)
            : 0m;
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
/// <c>ForgottenSoul</c>: <c>AfterCardExhausted</c> deals its Damage to one random hittable enemy
/// (<c>AfterCardExhaustedMirrors.HandleForgottenSoul</c>).
/// </summary>
[FlashContent("FORGOTTEN_SOUL")]
public sealed class ForgottenSoul : FlashRelicDef
{
    // ForgottenSoul.cs: DamageVar(1m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card,
        bool causedByEthereal)
        => FlashCardEffects.DamageRandomEnemy(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>HandDrill</c>: <c>AfterBlockBroken</c> applies Vulnerable when the owner breaks a non-player
/// creature's block (<c>AfterBlockBrokenMirrors.HandleHandDrill</c>). Osty is rejected at admission.
/// </summary>
[FlashContent("HAND_DRILL")]
public sealed class HandDrill : FlashRelicDef
{
    // HandDrill.cs: PowerVar&lt;VulnerablePower&gt;(2m).
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    public override void AfterBlockBroken(ref FlashExec x, FlashListener self, byte target, byte breaker)
    {
        if (breaker != self.Owner || target == FlashLimits.PlayerOwner)
            return;
        FlashKernel.ApplyPower(ref x, target, FlashRegistry.PowerId<VulnerablePower>(),
            (int)x.R.RelicVar(Id, 0), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>DaughterOfTheWind</c>: <c>AfterCardPlayed</c>, after an Attack, gains its unpowered block
/// (<c>AfterCardPlayedMirrors.HandleDaughterOfTheWind</c>).
/// </summary>
[FlashContent("DAUGHTER_OF_THE_WIND")]
public sealed class DaughterOfTheWind : FlashRelicDef
{
    // DaughterOfTheWind.cs: BlockVar(1m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Attack)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>LostWisp</c>: <c>AfterCardPlayed</c>, after a Power, deals its unpowered damage to every hittable
/// enemy (<c>AfterCardPlayedMirrors.HandleLostWisp</c>).
/// </summary>
[FlashContent("LOST_WISP")]
public sealed class LostWisp : FlashRelicDef
{
    // LostWisp.cs: DamageVar(8m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (!FlashKernel.IsInProgress(x.B)
            || x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Power)
            return;
        FlashCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
    }
}

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
/// <c>MummifiedHand</c>: <c>AfterCardPlayed</c>, after a Power, picks one hand card with the
/// <c>CombatCardSelection</c> stream and makes it free this turn
/// (<c>AfterCardPlayedMirrors.HandleMummifiedHand</c>; native <c>CardModel.SetToFreeThisTurn</c>).
/// </summary>
[FlashContent("MUMMIFIED_HAND")]
public sealed class MummifiedHand : FlashRelicDef
{
    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (!FlashKernel.IsInProgress(x.B)
            || x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Power)
            return;
        // Native: naturallyCostly.Where(CostsResources), then all hand.Where(CostsResources),
        // then naturallyCostly, then all hand. Rng.NextItem consumes the stream only for a non-empty list.
        int index = PickHandCard(ref x, naturallyCostly: true, costsResources: true);
        if (index < 0)
            index = PickHandCard(ref x, naturallyCostly: false, costsResources: true);
        if (index < 0)
            index = PickHandCard(ref x, naturallyCostly: true, costsResources: false);
        if (index < 0)
            index = PickHandCard(ref x, naturallyCostly: false, costsResources: false);
        if (index < 0)
            return;
        // CardModel.SetToFreeThisTurn: the energy modifier is only added when the card's base cost is
        // not negative (an Unplayable card stays Unplayable with no modifier), and the star cost is
        // set to 0 for this turn.
        FlashKernel.MakeFreeThisTurn(x.R, ref x.B.Cards[index]);
    }

    private static int PickHandCard(ref FlashExec x, bool naturallyCostly, bool costsResources)
    {
        int matches = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (Matches(ref x, index, naturallyCostly, costsResources))
                matches++;
        }
        if (matches == 0)
            return -1;
        int pick = x.B.RngStream(FlashRngStream.CombatCardSelection).NextInt(0, matches);
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (!Matches(ref x, index, naturallyCostly, costsResources))
                continue;
            if (pick-- == 0)
                return index;
        }
        throw new InvalidOperationException("手牌随机选择没有落到任何候选。");
    }

    private static bool Matches(ref FlashExec x, int index, bool naturallyCostly, bool costsResources)
    {
        if (naturallyCostly && !IsNaturallyCostly(x.B, x.B.Cards[index]))
            return false;
        if (!costsResources)
            return true;
        ref readonly FlashCard card = ref x.B.Cards[index];
        return FlashKernel.EnergyCostWithModifiers(x.B, x.R, card, FlashPile.Hand) > 0
            || FlashKernel.StarCostWithModifiers(x.B, x.R, card, FlashPile.Hand) > 0;
    }

    /// <summary>
    /// <c>CardEnergyCost.GetWithModifiers(CostModifiers.None)</c>: the current base cost (upgrade
    /// included) without local or global modifiers. A negative (Unplayable) or X cost is not costly.
    /// </summary>
    private static bool IsNaturallyCostly(in FlashBattle battle, in FlashCard card)
        => card.BaseStarCost > 0 || card.BaseCost > 0;
}

/// <summary>
/// <c>IronClub</c>: <c>AfterCardPlayed</c> counts cards toward <c>Cards</c>, then draws 1 when the
/// counter wraps (<c>AfterCardPlayedMirrors.HandleIronClub</c>). Counter = cards played toward the
/// cycle; the bridge projects <c>CounterPredictionState.Value</c>, falling back to
/// <c>IronClub.CardsPlayed</c>.
/// </summary>
[FlashContent("IRON_CLUB")]
public sealed class IronClub : FlashRelicDef
{
    // IronClub.cs: CardsVar(4).
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (!FlashKernel.IsInProgress(x.B) || relic.Counter % (int)x.R.RelicVar(Id, 0) != 0)
            return;
        FlashKernel.Draw(ref x, 1, fromHandDraw: false);
    }
}

/// <summary>
/// <c>IvoryTile</c>: <c>AfterCardPlayed</c> gains Energy when the play's energy value reaches
/// <c>EnergyThreshold</c> (<c>AfterCardPlayedMirrors.HandleIvoryTile</c>; <see cref="FlashExec.XValue"/>
/// carries <c>CardPlay.Resources.EnergyValue</c>).
/// </summary>
[FlashContent("IVORY_TILE")]
public sealed class IvoryTile : FlashRelicDef
{
    // IvoryTile.cs: EnergyVar(1), EnergyVar("EnergyThreshold", 3).
    public override IReadOnlyList<string> VarNames => ["Energy", "EnergyThreshold"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.XValue < (int)x.R.RelicVar(Id, 1))
            return;
        FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>MrStruggles</c>: <c>AfterPlayerTurnStart</c> deals unpowered damage equal to the player turn
/// number to every hittable enemy (existing-engine relic batch, <c>ContinueRelicsAfterPlayerTurnStart</c>).
/// </summary>
[FlashContent("MR_STRUGGLES")]
public sealed class MrStruggles : FlashRelicDef
{
    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
        => FlashCardEffects.DamageAllEnemies(ref x, x.B.PlayerTurn, FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>Sai</c>: <c>AfterSideTurnStart</c> gains its unpowered block on every player side
/// (<c>SimulatedCombatState.RelicTurnStart.TriggerRelicsAfterSideTurnStart</c>).
/// </summary>
[FlashContent("SAI")]
public sealed class Sai : FlashRelicDef
{
    // Sai.cs: BlockVar(7m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>FakeHappyFlower</c>: <c>AfterSideTurnStart</c> advances the cycle and grants Energy on wrap
/// (<c>TriggerRelicsAfterSideTurnStart</c>). Counter = turns seen (the engine's
/// <c>StatefulRelicState.Current</c>).
/// </summary>
[FlashContent("FAKE_HAPPY_FLOWER")]
public sealed class FakeHappyFlower : FlashRelicDef
{
    // FakeHappyFlower.cs: EnergyVar(1), DynamicVar("Turns", 5m).
    public override IReadOnlyList<string> VarNames => ["Energy", "Turns"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter = (relic.Counter + 1) % (int)x.R.RelicVar(Id, 1);
        if (relic.Counter == 0)
            FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>FakeVenerableTeaSet</c>: <c>AfterEnergyReset</c> grants Energy once while the pending flag is
/// set (<c>TriggerRelicsAfterEnergyReset</c>). Counter = the engine's
/// <c>StatefulRelicState.Current</c> (GainEnergyInNextCombat).
/// </summary>
[FlashContent("FAKE_VENERABLE_TEA_SET")]
public sealed class FakeVenerableTeaSet : FlashRelicDef
{
    // FakeVenerableTeaSet.cs: EnergyVar(1).
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
/// <c>FakeOrichalcum</c>: <c>BeforeSideTurnEndVeryEarly</c> latches when the player has no block;
/// <c>BeforeSideTurnEnd</c> spends the latch for its Block; <c>BeforeSideTurnStart</c> clears it
/// (<c>OrichalcumMirrors</c>). Counter = the latched trigger, projected from the same
/// <c>OrichalcumMirrors</c> store entry as Orichalcum.
/// </summary>
[FlashContent("FAKE_ORICHALCUM")]
public sealed class FakeOrichalcum : FlashRelicDef
{
    // FakeOrichalcum.cs: BlockVar(3m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void BeforeSideTurnEndVeryEarly(ref FlashExec x, FlashListener self, FlashSide side,
        ushort participants)
    {
        if (FlashKernel.Participates(participants, self.Owner) && x.B.Creature(self.Owner).Block <= 0)
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
/// <c>PollinousCore</c>: <c>BeforeHandDraw</c> advances TurnsSeen and <c>ModifyHandDraw</c> adds Cards
/// once the cycle completes, then <c>CompleteRelicHandDraw</c> resets it
/// (<c>SimulatedCombatState.Relics.PrepareRelicsBeforeHandDraw</c>,
/// <c>GetStatefulRelicHandDrawContribution</c>, <c>CompleteRelicHandDraw</c>).
/// <see cref="FlashListenerDef.ModifyHandDraw"/> is evaluated exactly once per turn-start hand draw, so
/// the increment, contribution and reset happen in that one method. Counter = TurnsSeen
/// (<c>StatefulRelicState.Current</c>).
/// </summary>
[FlashContent("POLLINOUS_CORE")]
public sealed class PollinousCore : FlashRelicDef
{
    // PollinousCore.cs: CardsVar(2), DynamicVar("Turns", 4m).
    public override IReadOnlyList<string> VarNames => ["Cards", "Turns"];

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (relic.Counter < (int)x.R.RelicVar(Id, 1))
            return amount;
        relic.Counter = 0;
        return amount + x.R.RelicVar(Id, 0);
    }
}

/// <summary>
/// <c>VelvetChoker</c>: <c>ModifyMaxEnergy</c> adds Energy; <c>ShouldPlay</c> forbids playing once
/// Cards cards were played this turn; <c>AfterCardPlayed</c> counts them and
/// <c>BeforeSideTurnStart</c> resets (<c>ShouldPlayMirrors.HandleVelvetChoker</c>,
/// <c>AfterCardPlayedMirrors.HandleVelvetChoker</c>). Counter = cards played this turn, projected from
/// <c>CounterPredictionState.Value</c> with the live field as fallback.
/// </summary>
[FlashContent("VELVET_CHOKER")]
public sealed class VelvetChoker : FlashRelicDef
{
    // VelvetChoker.cs: CardsVar(6), EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Cards", "Energy"];

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 1);

    public override bool ShouldPlay(in FlashBattle battle, FlashRules rules, FlashListener self, in FlashCard card)
        => battle.Relics[self.Index].Counter < (int)rules.RelicVar(Id, 0);

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
        => x.B.Relics[self.Index].Counter++;

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>Fiddle</c>: <c>ModifyHandDraw</c> adds Cards; <c>ShouldDraw</c> refuses a non-hand draw while the
/// player side is current (<c>ShouldDrawMirrors.HandleFiddle</c>). The native
/// <c>AfterPreventingDraw</c> only flashes.
/// </summary>
[FlashContent("FIDDLE")]
public sealed class Fiddle : FlashRelicDef
{
    // Fiddle.cs: CardsVar(2).
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);

    public override bool ShouldDraw(ref FlashExec x, FlashListener self, bool fromHandDraw)
        => fromHandDraw || x.B.Side != FlashSide.Player;
}

/// <summary>
/// <c>RingOfTheDrake</c>: <c>ModifyHandDraw</c> adds Cards while the player turn number is at most
/// Turns (<c>PersistentPowerSupport</c> native relic contribution).
/// </summary>
[FlashContent("RING_OF_THE_DRAKE")]
public sealed class RingOfTheDrake : FlashRelicDef
{
    // RingOfTheDrake.cs: CardsVar(2), DynamicVar("Turns", 3m).
    public override IReadOnlyList<string> VarNames => ["Cards", "Turns"];

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
        => x.B.PlayerTurn > (int)x.R.RelicVar(Id, 1) ? amount : amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>PaelsBlood</c>: <c>ModifyHandDraw</c> adds Cards on every player turn
/// (<c>PersistentPowerSupport</c> native relic contribution).
/// </summary>
[FlashContent("PAELS_BLOOD")]
public sealed class PaelsBlood : FlashRelicDef
{
    // PaelsBlood.cs: CardsVar(1).
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>PaelsFlesh</c>: <c>ModifyMaxEnergy</c> adds Energy from player turn 3
/// (<c>PersistentPowerSupport.GetModifiedMaxEnergy</c> native relic contribution).
/// </summary>
[FlashContent("PAELS_FLESH")]
public sealed class PaelsFlesh : FlashRelicDef
{
    // PaelsFlesh.cs: EnergyVar(1); the turn threshold 3 is a code constant.
    private const int EnergyFromTurn = 3;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => x.B.PlayerTurn < EnergyFromTurn ? amount : amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>PaelsTears</c>: <c>BeforeSideTurnEnd</c> records leftover energy and <c>AfterSideTurnStart</c>
/// gains Energy while the flag is set (<c>SimulatedCombatState.RelicTurnStart</c>).
/// Counter = HadLeftoverEnergy (<c>StatefulRelicState.Current</c>).
/// </summary>
[FlashContent("PAELS_TEARS")]
public sealed class PaelsTears : FlashRelicDef
{
    // PaelsTears.cs: EnergyVar(2).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (!FlashKernel.Participates(participants, self.Owner))
            return;
        x.B.Relics[self.Index].Counter = x.B.Energy > 0 ? 1 : 0;
    }

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player || x.B.Relics[self.Index].Counter == 0)
            return;
        FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>BlessedAntler</c>: <c>ModifyMaxEnergy</c> adds Energy. The native turn-1 Dazed generation runs at
/// the turn start before the play-phase root and is imported; later turns do not satisfy its condition.
/// </summary>
[FlashContent("BLESSED_ANTLER")]
public sealed class BlessedAntler : FlashRelicDef
{
    // BlessedAntler.cs: EnergyVar(1), CardsVar(3).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>BloodSoakedRose</c>: <c>ModifyMaxEnergy</c> adds Energy. The Enthralled curse is added to the deck
/// when the relic is obtained, outside combat.
/// </summary>
[FlashContent("BLOOD_SOAKED_ROSE")]
public sealed class BloodSoakedRose : FlashRelicDef
{
    // BloodSoakedRose.cs: EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>Ectoplasm</c>: <c>ModifyMaxEnergy</c> adds Energy; the gold modifier only runs outside combat.
/// </summary>
[FlashContent("ECTOPLASM")]
public sealed class Ectoplasm : FlashRelicDef
{
    // Ectoplasm.cs: EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>PhilosophersStone</c>: <c>ModifyMaxEnergy</c> adds Energy. The enemy Strength from
/// <c>AfterRoomEntered</c> is applied before the root and imported.
/// </summary>
[FlashContent("PHILOSOPHERS_STONE")]
public sealed class PhilosophersStone : FlashRelicDef
{
    // PhilosophersStone.cs: PowerVar&lt;StrengthPower&gt;(1m), EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>PrismaticGem</c>: <c>ModifyMaxEnergy</c> adds Energy; the card-reward pool modifier is out of combat.
/// </summary>
[FlashContent("PRISMATIC_GEM")]
public sealed class PrismaticGem : FlashRelicDef
{
    // PrismaticGem.cs: EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>Sozu</c>: <c>ModifyMaxEnergy</c> adds Energy; the potion-procurement refusal is out of combat.
/// </summary>
[FlashContent("SOZU")]
public sealed class Sozu : FlashRelicDef
{
    // Sozu.cs: EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>PumpkinCandle</c>: <c>ModifyMaxEnergy</c> adds Energy while KindleCount is positive. Counter =
/// KindleCount, projected from the live model field (the value does not change during combat).
/// </summary>
[FlashContent("PUMPKIN_CANDLE")]
public sealed class PumpkinCandle : FlashRelicDef
{
    // PumpkinCandle.cs: EnergyVar(1), DynamicVar("CombatCount", 5m).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => x.B.Relics[self.Index].Counter > 0 ? amount + x.R.RelicVar(Id, 0) : amount;
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
/// <c>RunicPyramid</c>: <c>ShouldFlush</c> declines the owner's hand flush on every turn
/// (<c>PersistentRelicSupport.ShouldFlush</c>).
/// </summary>
[FlashContent("RUNIC_PYRAMID")]
public sealed class RunicPyramid : FlashRelicDef
{
    public override bool ShouldFlush(ref FlashExec x, FlashListener self) => false;
}

/// <summary>
/// <c>RingingTriangle</c>: <c>ShouldFlush</c> declines the owner's hand flush on player turn 1 only
/// (<c>PersistentRelicSupport.ShouldFlush</c> uses the simulated turn number).
/// </summary>
[FlashContent("RINGING_TRIANGLE")]
public sealed class RingingTriangle : FlashRelicDef
{
    public override bool ShouldFlush(ref FlashExec x, FlashListener self) => x.B.PlayerTurn > 1;
}

/// <summary>
/// <c>TheAbacus</c>: <c>AfterShuffle</c> gains its unpowered block when the shuffling player is its
/// owner (<c>AfterShuffleMirrors.HandleTheAbacus</c>).
/// </summary>
[FlashContent("THE_ABACUS")]
public sealed class TheAbacus : FlashRelicDef
{
    // TheAbacus.cs: BlockVar(6m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterShuffle(ref FlashExec x, FlashListener self, byte shuffler)
    {
        if (self.Owner != FlashLimits.PlayerOwner || shuffler != FlashLimits.PlayerOwner)
            return;
        FlashKernel.GainBlock(ref x, self.Owner, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>ChemicalX</c>: <c>ModifyXValue</c> adds its <c>Increase</c> variable to an X value read by its
/// owner's card (<c>CardModel.ResolveEnergyXValue</c>); the native <c>BeforeCardPlayed</c> only flashes.
/// </summary>
[FlashContent("CHEMICAL_X")]
public sealed class ChemicalX : FlashRelicDef
{
    // ChemicalX.cs: DynamicVar("Increase", 2m).
    public override IReadOnlyList<string> VarNames => ["Increase"];

    public override int ModifyXValue(in FlashBattle battle, FlashRules rules, FlashListener self, in FlashCard card,
        int value)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return value;
        return value + (int)rules.RelicVar(Id, 0);
    }
}

/// <summary>
/// <c>IceCream</c>: the player keeps their energy between turns after the first
/// (<c>PersistentRelicSupport.ShouldPlayerResetEnergy</c>: the native relic returns false from turn 2).
/// </summary>
[FlashContent("ICE_CREAM")]
public sealed class IceCream : FlashRelicDef
{
    public override bool ShouldPlayerResetEnergy(in FlashBattle battle, FlashRules rules, FlashListener self,
        byte player)
        => self.Owner != player || battle.PlayerTurn <= 1;
}

/// <summary>
/// <c>RuinedHelmet</c>: the first positive Strength received this combat is doubled
/// (<c>SimulatedCombatState.ModifyPowerAmountForRelics</c> / <c>ModifyPowerAmountForRuinedHelmet</c>,
/// native <c>TryModifyPowerAmountReceived</c> and <c>AfterModifyingPowerAmountReceived</c>). The
/// used flag lives in <see cref="FlashRelicFlags.UsedThisCombat"/>; the bridge projects
/// <c>RuinedHelmet._usedThisCombat</c>.
/// </summary>
[FlashContent("RUINED_HELMET")]
public sealed class RuinedHelmet : FlashRelicDef
{
    public override bool TryModifyPowerAmountReceived(in FlashBattle battle, FlashRules rules, FlashListener self,
        ushort powerId, byte target, int amount, out int modified)
    {
        modified = amount;
        if (amount <= 0
            || target != self.Owner
            || !FlashRegistry.TryPower("STRENGTH_POWER", out ushort strength)
            || powerId != strength
            || (battle.Relics[self.Index].Flags & FlashRelicFlags.UsedThisCombat) != 0)
        {
            return false;
        }
        modified = amount * 2;
        return true;
    }

    public override void AfterModifyingPowerAmountReceived(ref FlashExec x, FlashListener self, ushort powerId,
        byte target)
        => x.B.Relics[self.Index].Flags |= FlashRelicFlags.UsedThisCombat;
}

/// <summary>
/// <c>GhostSeed</c>: a Basic Strike or Defend that enters combat without Ethereal gains it
/// (<c>SimulatedCombatState.CardEventHistory.AfterCardEnteredCombat</c> /
/// <c>GhostSeedMirrors.AfterCardEnteredCombat</c>).
/// </summary>
[FlashContent("GHOST_SEED")]
public sealed class GhostSeed : FlashRelicDef
{
    public override void AfterCardEnteredCombat(ref FlashExec x, FlashListener self, FlashPile pile, int index,
        in FlashCard card)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        if (stats.Rarity != FlashCardRarity.Basic
            || (stats.Tags & (FlashCardTag.Strike | FlashCardTag.Defend)) == 0)
        {
            return;
        }
        ref FlashCard stored = ref FlashPiles.At(ref x.B, pile, index);
        if ((stored.Keywords & FlashKeywords.Ethereal) == 0)
            stored.Keywords |= FlashKeywords.Ethereal;
    }
}

/// <summary>
/// <c>UnceasingTop</c>: the draw is a kernel special case in
/// <c>SimulatedCombatState.TriggerRelicsAfterHandEmptied</c> (the existing engine does not dispatch a
/// generic relic hook for it), so the definition only makes the relic admissible.
/// </summary>
[FlashContent("UNCEASING_TOP")]
public sealed class UnceasingTop : FlashRelicDef;
