using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Relics;

/// <summary>
/// <c>Bookmark</c>: after the turn-end flush, pick one retained hand card that is not X-cost and whose
/// local cost is positive, and add an until-played −1
/// (<c>SimulatedCombatState.TriggerBookmarkAfterFlush</c> / <c>CorePowerSupport.FlushPlayerHandAtTurnEnd</c>).
/// </summary>
[FlashContent("BOOKMARK")]
public sealed class Bookmark : FlashRelicDef
{
    public override void AfterFlush(ref FlashExec x, FlashListener self)
    {
        int handCount = x.B.HandCount;
        Span<int> candidates = stackalloc int[handCount];
        int count = 0;
        for (int index = 0; index < handCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            if ((card.Flags & FlashCardFlags.CostsX) != 0)
                continue;
            if (FlashKernel.EnergyCostWithLocalModifiers(x.B, card) > 0)
                candidates[count++] = index;
        }
        if (count == 0)
            return;
        int pick = candidates[x.B.RngStream(FlashRngStream.CombatCardSelection).NextInt(0, count)];
        FlashKernel.AddEnergyCostModifier(ref x.B.Cards[pick], -1, FlashCostType.Relative,
            FlashCostExpiration.WhenPlayed, reduceOnly: false);
    }
}

/// <summary>
/// <c>EmotionChip</c>: trigger every orb's passive at the player turn start when its owner took
/// unblocked damage in the previous turn. The two counters are the existing
/// <c>StatefulRelicState</c> Current/Previous pair and rotate in
/// <c>SimulatedCombatState.RecordRelicRoundDamage</c>.
/// </summary>
[FlashContent("EMOTION_CHIP")]
public sealed class EmotionChip : FlashRelicDef
{
    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver == FlashLimits.PlayerOwner && result.UnblockedDamage > 0)
            x.B.Relics[self.Index].Aux = 1;
    }

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
        if (x.B.Relics[self.Index].Counter == 0)
            return;
        int count = x.B.OrbCount;
        for (int index = 0; index < count; index++)
        {
            FlashKernel.TriggerOrbPassive(ref x, index);
            if (x.NeedsChoice)
                return;
        }
        // SimulatedCombatState.ReactiveRelics.TriggerEmotionChip runs
        // TriggeredPowerSupport.CompensateHistorySince after all passives; this is the one old call
        // site outside the card/potion/turn-end boundaries, so flush the deferred block-break here.
        FlashKernel.ApplyBlockBreakCompensations(ref x);
    }

    public override void AfterRelicRound(ref FlashExec x, FlashListener self)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter = relic.Aux;
        relic.Aux = 0;
    }
}

/// <summary>
/// <c>Metronome</c>: every orb channelled increments the relic counter; when it equals OrbCount it
/// deals its Unpowered Damage to every hittable enemy (<c>AfterOrbChanneledMirrors.HandleMetronome</c>).
/// The counter is not reset by the trigger.
/// </summary>
[FlashContent("METRONOME")]
public sealed class Metronome : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "OrbCount"];

    public override void AfterOrbChanneled(ref FlashExec x, FlashListener self, in FlashOrb orb)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (relic.Counter != (int)x.R.RelicVar(Id, 1))
            return;
        FlashCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>ToughBandages</c>: after one of the current side's cards is discarded, gain its Block variable
/// (Unpowered; <c>AfterCardDiscardedMirrors.HandleToughBandages</c>).
/// </summary>
[FlashContent("TOUGH_BANDAGES")]
public sealed class ToughBandages : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterCardDiscarded(ref FlashExec x, FlashListener self, in FlashCard card)
    {
        if (x.B.Side != FlashSide.Player)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            fromCard: false);
    }
}

/// <summary>
/// <c>LunarPastry</c>: gains its Stars variable at the end of each side turn it participates in
/// (<c>SimulatedCombatState.TriggerRelicsAfterSideTurnEnd</c> case LunarPastry).
/// </summary>
[FlashContent("LUNAR_PASTRY")]
public sealed class LunarPastry : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Stars"];

    public override void AfterSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player)
            return;
        FlashKernel.GainStars(ref x, (int)x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>Chandelier</c>: <c>AfterSideTurnStart</c>, player turn 3, gains 3 energy
/// (<c>TriggerRelicsAfterSideTurnStart</c>).
/// </summary>
[FlashContent("CHANDELIER")]
public sealed class Chandelier : FlashRelicDef
{
    // Chandelier.cs: EnergyVar(3), _energyTurn = 3.
    private const int EnergyTurn = 3;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side == FlashSide.Player && x.B.PlayerTurn == EnergyTurn)
            FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>CaptainsWheel</c>: <c>AfterBlockCleared</c>, player turn 3, gains 18 unpowered block
/// (<c>SimulatedCombatState.ReactiveRelics.TriggerRelicsAfterBlockCleared</c>).
/// </summary>
[FlashContent("CAPTAINS_WHEEL")]
public sealed class CaptainsWheel : FlashRelicDef
{
    // CaptainsWheel.cs: BlockVar(18m, ValueProp.Unpowered); block turn 3 is a code constant.
    private const int BlockTurn = 3;

    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterBlockCleared(ref FlashExec x, FlashListener self)
    {
        if (x.B.PlayerTurn == BlockTurn)
            FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
                FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>CloakClasp</c>: <c>BeforeSideTurnEnd</c> hook (normal pass) gains 1 unpowered block per card in hand
/// (<c>BeforeSideTurnEndMirrors.HandleCloakClasp</c>).
/// </summary>
[FlashContent("CLOAK_CLASP")]
public sealed class CloakClasp : FlashRelicDef
{
    // CloakClasp.cs: BlockVar(1m, ValueProp.Unpowered), multiplied by the hand count.
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (!FlashKernel.Participates(participants, self.Owner) || x.B.HandCount == 0)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.B.HandCount * x.R.RelicVar(Id, 0),
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>StoneCalendar</c>: <c>BeforeSideTurnEnd</c> on player turn 7 deals 52 unpowered damage to every
/// hittable enemy (<c>BeforeSideTurnEndMirrors.HandleStoneCalendar</c>). The existing engine reads the
/// turn number, not a private counter, so no imported state is needed.
/// </summary>
[FlashContent("STONE_CALENDAR")]
public sealed class StoneCalendar : FlashRelicDef
{
    // StoneCalendar.cs: DamageVar(52m, ValueProp.Unpowered), DynamicVar("DamageTurn", 7m).
    public override IReadOnlyList<string> VarNames => ["Damage", "DamageTurn"];

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (!FlashKernel.Participates(participants, self.Owner)
            || x.B.PlayerTurn != (int)x.R.RelicVar(Id, 1))
            return;
        FlashCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>GamePiece</c>: <c>AfterCardPlayed</c>, after a Power card, draws 1
/// (<c>AfterCardPlayedMirrors.HandleGamePiece</c>).
/// </summary>
[FlashContent("GAME_PIECE")]
public sealed class GamePiece : FlashRelicDef
{
    // GamePiece.cs: CardsVar(1).
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Power)
            return;
        FlashKernel.Draw(ref x, (int)x.R.RelicVar(Id, 0), fromHandDraw: false);
    }
}

/// <summary>
/// <c>RazorTooth</c>: <c>AfterCardPlayed</c> upgrades the played Attack or Skill when it is still
/// upgradable (<c>AfterCardPlayedMirrors.HandleRazorTooth</c>).
/// </summary>
[FlashContent("RAZOR_TOOTH")]
public sealed class RazorTooth : FlashRelicDef
{
    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        FlashCardStats stats = x.R.Card(play.Card.Id, play.Card.Upgrade);
        if (stats.Type is not (FlashCardType.Attack or FlashCardType.Skill))
            return;
        if (play.Card.Upgrade >= stats.MaxUpgradeLevel)
            return;
        FlashCardEffects.UpgradeCurrentCard(ref x);
    }
}

/// <summary>
/// <c>IntimidatingHelmet</c>: <c>BeforeCardPlayed</c>, when the play's energy value is at least 2, gains
/// 4 unpowered block (<c>BeforeCardPlayedMirrors.HandleIntimidatingHelmet</c>; Flash's
/// <see cref="FlashExec.XValue"/> carries the same <c>CardPlay.Resources.EnergyValue</c>).
/// </summary>
[FlashContent("INTIMIDATING_HELMET")]
public sealed class IntimidatingHelmet : FlashRelicDef
{
    // IntimidatingHelmet.cs: BlockVar(4m, ValueProp.Unpowered), EnergyVar(2).
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.XValue < (int)x.R.RelicVar(Id, 1))
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            FlashValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>CharonsAshes</c>: <c>AfterCardExhausted</c> deals 3 unpowered damage to every hittable enemy
/// (<c>AfterCardExhaustedMirrors.HandleCharonsAshes</c>).
/// </summary>
[FlashContent("CHARONS_ASHES")]
public sealed class CharonsAshes : FlashRelicDef
{
    // CharonsAshes.cs: DamageVar(3m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card, bool causedByEthereal)
        => FlashCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>SturdyClamp</c>: <c>ShouldClearBlock</c> declines the player's block clear and
/// <c>BlockAfterPreventingClear</c> keeps at most 10
/// (<c>PersistentRelicSupport.ShouldClearBlock</c>/<c>BlockAfterPreventingClear</c>).
/// </summary>
[FlashContent("STURDY_CLAMP")]
public sealed class SturdyClamp : FlashRelicDef
{
    // SturdyClamp.cs: BlockVar(10m, ValueProp.Unpowered); the engine reads DynamicVars.Block.
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override bool ShouldClearBlock(ref FlashExec x, FlashListener self, byte owner)
        => owner != FlashLimits.PlayerOwner;

    public override int BlockAfterPreventingClear(ref FlashExec x, FlashListener self, byte owner, int block)
        => owner == FlashLimits.PlayerOwner ? (int)Math.Min(block, x.R.RelicVar(Id, 0)) : block;
}

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
            || FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Power)
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
/// <c>UnceasingTop</c>: the draw is a kernel special case in
/// <c>SimulatedCombatState.TriggerRelicsAfterHandEmptied</c> (the existing engine does not dispatch a
/// generic relic hook for it), so the definition only makes the relic admissible.
/// </summary>
[FlashContent("UNCEASING_TOP")]
public sealed class UnceasingTop : FlashRelicDef;

/// <summary>
/// <c>Bellows</c>: <c>AfterPlayerTurnStart</c> upgrades the hand only when the turn number is &lt;= 1
/// (<c>ContinueRelicsAfterPlayerTurnStart</c>). The player turn 1 start is before the root and turn 2 no
/// longer satisfies the condition.
/// </summary>
[FlashContent("BELLOWS")]
public sealed class Bellows : FlashRelicDef;

/// <summary>
/// <c>GamblingChip</c>: <c>AfterPlayerTurnStart</c> offers its discard/draw choice only on turn 1
/// (<c>TurnStartChoiceSupport.ResolveDiscardAndDraw</c> from <c>ContinueRelicsAfterPlayerTurnStart</c>).
/// The choice belongs to the player turn start, before the play-phase root.
/// </summary>
[FlashContent("GAMBLING_CHIP")]
public sealed class GamblingChip : FlashRelicDef;

/// <summary>
/// <c>VexingPuzzlebox</c>: <c>AfterPlayerTurnStart</c> generates one free card only when the turn number is
/// 1 (<c>ContinueRelicsAfterPlayerTurnStart</c>), at the player turn start before the play-phase root.
/// </summary>
[FlashContent("VEXING_PUZZLEBOX")]
public sealed class VexingPuzzlebox : FlashRelicDef
{
    // Turn-start generation samples the character pool; the turn-setup root is captured before
    // this hook fires, so the rules table must carry the generated card stats.
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];
}

/// <summary><c>FrozenEgg</c>: merchant/card-reward upgrade hooks and <c>IsAllowed</c> only.</summary>
[FlashContent("FROZEN_EGG")]
public sealed class FrozenEgg : FlashRelicDef;

/// <summary><c>Girya</c>: <c>AfterRoomEntered</c>, <c>TryModifyRestSiteOptions</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("GIRYA")]
public sealed class Girya : FlashRelicDef;

/// <summary><c>Mango</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[FlashContent("MANGO")]
public sealed class Mango : FlashRelicDef;

/// <summary>
/// <c>MeatOnTheBone</c>: <c>BeforeCombatStart</c> and <c>AfterCurrentHpChanged</c> only maintain the
/// relic status for the after-victory heal in <c>AfterCombatVictoryEarly</c>. The existing engine's
/// combat-hook catalog marks it NotCombatRelevant, so there is no combat dispatch to mirror.
/// </summary>
[FlashContent("MEAT_ON_THE_BONE")]
public sealed class MeatOnTheBone : FlashRelicDef;

/// <summary><c>MoltenEgg</c>: merchant/card-reward upgrade hooks and <c>IsAllowed</c> only.</summary>
[FlashContent("MOLTEN_EGG")]
public sealed class MoltenEgg : FlashRelicDef;

/// <summary><c>OldCoin</c>: <c>AfterObtained</c> gold gain and <c>IsAllowed</c> only.</summary>
[FlashContent("OLD_COIN")]
public sealed class OldCoin : FlashRelicDef;

/// <summary><c>PrayerWheel</c>: <c>TryModifyRewards</c> only.</summary>
[FlashContent("PRAYER_WHEEL")]
public sealed class PrayerWheel : FlashRelicDef;

/// <summary><c>Shovel</c>: <c>TryModifyRestSiteOptions</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("SHOVEL")]
public sealed class Shovel : FlashRelicDef;

/// <summary><c>TheCourier</c>: <c>ModifyMerchantPrice</c> and <c>ShouldRefillMerchantEntry</c> only.</summary>
[FlashContent("THE_COURIER")]
public sealed class TheCourier : FlashRelicDef;

/// <summary><c>ToxicEgg</c>: merchant/card-reward upgrade hooks and <c>IsAllowed</c> only.</summary>
[FlashContent("TOXIC_EGG")]
public sealed class ToxicEgg : FlashRelicDef;

/// <summary><c>WhiteBeastStatue</c>: <c>ShouldForcePotionReward</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("WHITE_BEAST_STATUE")]
public sealed class WhiteBeastStatue : FlashRelicDef;

/// <summary><c>WhiteStar</c>: <c>TryModifyRewards</c> and <c>IsAllowed</c> only.</summary>
[FlashContent("WHITE_STAR")]
public sealed class WhiteStar : FlashRelicDef;

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
        if (FlashKernel.CardType(x.B, x.R, play.Card) == FlashCardType.Attack)
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
        if (!FlashKernel.IsInProgress(x.B) || FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Attack)
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
        FlashCardType type = FlashKernel.CardType(x.B, x.R, play.Card);
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
        if (!FlashKernel.IsInProgress(x.B) || FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Attack)
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
        int hpBeforeRevive = x.B.Creature(creature).CurrentHp;
        FlashKernel.Heal(ref x, creature, amount);
        int restored = x.B.Creature(creature).CurrentHp - hpBeforeRevive;
        if (restored > 0)
        {
            x.B.AddCounter(FlashCounter.DeathSaveRelicHpRestored, restored);
            x.B.AddCounter(FlashCounter.DeathSaveUseCount, 1);
        }
    }
}

/// <summary>
/// <c>HelicalDart</c>: after a card with the Shiv tag is played, apply 1 temporary Dexterity
/// (<c>AfterCardPlayedMirrors.HandleHelicalDart</c> → <c>ApplyTemporaryDexterity</c>).
/// </summary>
[FlashContent("HELICAL_DART")]
public sealed class HelicalDart : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if ((x.R.Card(play.Card.Id, play.Card.Upgrade).Tags & FlashCardTag.Shiv) == 0)
            return;
        int amount = (int)x.R.RelicVar(Id, 0);
        ushort temporary = FlashRegistry.PowerId<Powers.HelicalDartPower>();
        ushort dexterity = FlashRegistry.PowerId<Powers.DexterityPower>();
        bool alreadyApplied = FlashKernel.GetAmount(x.B, FlashLimits.PlayerOwner, temporary) != 0;
        if (!alreadyApplied)
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, dexterity, amount, FlashLimits.PlayerOwner);
        int applied = FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, temporary, amount,
            FlashLimits.PlayerOwner);
        if (alreadyApplied && applied != 0)
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, dexterity, applied, FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>PaperKrane</c>: <c>ModifyWeakMultiplier</c> subtracts 0.15 for powered attacks against the
/// owner. The existing engine routes it through <c>WeakPower</c>'s multiplier; the Flash kernel reads
/// the relic there, so this definition only declares the rule-table variable.
/// </summary>
[FlashContent("PAPER_KRANE")]
public sealed class PaperKrane : FlashRelicDef
{
}
