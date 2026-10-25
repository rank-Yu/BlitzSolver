using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Relics;

/// <summary>
/// <c>Bookmark</c>: after the turn-end flush, pick one retained hand card that is not X-cost and whose
/// local cost is positive, and add an until-played −1
/// (<c>SimulatedCombatState.TriggerBookmarkAfterFlush</c> / <c>CorePowerSupport.FlushPlayerHandAtTurnEnd</c>).
/// </summary>
[BlitzContent("BOOKMARK")]
public sealed class Bookmark : BlitzRelicDef
{
    public override void AfterFlush(ref BlitzExec x, BlitzListener self)
    {
        int handCount = x.B.HandCount;
        Span<int> candidates = stackalloc int[handCount];
        int count = 0;
        for (int index = 0; index < handCount; index++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[index];
            if ((card.Flags & BlitzCardFlags.CostsX) != 0)
                continue;
            if (BlitzKernel.EnergyCostWithLocalModifiers(x.B, card) > 0)
                candidates[count++] = index;
        }
        if (count == 0)
            return;
        int pick = candidates[x.B.RngStream(BlitzRngStream.CombatCardSelection).NextInt(0, count)];
        BlitzKernel.AddEnergyCostModifier(ref x.B.Cards[pick], -1, BlitzCostType.Relative,
            BlitzCostExpiration.WhenPlayed, reduceOnly: false);
    }
}

/// <summary>
/// <c>EmotionChip</c>: trigger every orb's passive at the player turn start when its owner took
/// unblocked damage in the previous turn. The two counters are the existing
/// <c>StatefulRelicState</c> Current/Previous pair and rotate in
/// <c>SimulatedCombatState.RecordRelicRoundDamage</c>.
/// </summary>
[BlitzContent("EMOTION_CHIP")]
public sealed class EmotionChip : BlitzRelicDef
{
    public override void AfterDamageReceived(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (result.Receiver == BlitzLimits.PlayerOwner && result.UnblockedDamage > 0)
            x.B.Relics[self.Index].Aux = 1;
    }

    public override void AfterPlayerTurnStart(ref BlitzExec x, BlitzListener self)
    {
        if (x.B.Relics[self.Index].Counter == 0)
            return;
        int count = x.B.OrbCount;
        for (int index = 0; index < count; index++)
        {
            BlitzKernel.TriggerOrbPassive(ref x, index);
            if (x.NeedsChoice)
                return;
        }
        // SimulatedCombatState.ReactiveRelics.TriggerEmotionChip runs
        // TriggeredPowerSupport.CompensateHistorySince after all passives; this is the one old call
        // site outside the card/potion/turn-end boundaries, so flush the deferred block-break here.
        BlitzKernel.ApplyBlockBreakCompensations(ref x);
    }

    public override void AfterRelicRound(ref BlitzExec x, BlitzListener self)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter = relic.Aux;
        relic.Aux = 0;
    }
}

/// <summary>
/// <c>Metronome</c>: every orb channelled increments the relic counter; when it equals OrbCount it
/// deals its Unpowered Damage to every hittable enemy (<c>AfterOrbChanneledMirrors.HandleMetronome</c>).
/// The counter is not reset by the trigger.
/// </summary>
[BlitzContent("METRONOME")]
public sealed class Metronome : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "OrbCount"];

    public override void AfterOrbChanneled(ref BlitzExec x, BlitzListener self, in BlitzOrb orb)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (relic.Counter != (int)x.R.RelicVar(Id, 1))
            return;
        BlitzCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered,
            BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>ToughBandages</c>: after one of the current side's cards is discarded, gain its Block variable
/// (Unpowered; <c>AfterCardDiscardedMirrors.HandleToughBandages</c>).
/// </summary>
[BlitzContent("TOUGH_BANDAGES")]
public sealed class ToughBandages : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterCardDiscarded(ref BlitzExec x, BlitzListener self, in BlitzCard card)
    {
        if (x.B.Side != BlitzSide.Player)
            return;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered,
            fromCard: false);
    }
}

/// <summary>
/// <c>LunarPastry</c>: gains its Stars variable at the end of each side turn it participates in
/// (<c>SimulatedCombatState.TriggerRelicsAfterSideTurnEnd</c> case LunarPastry).
/// </summary>
[BlitzContent("LUNAR_PASTRY")]
public sealed class LunarPastry : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Stars"];

    public override void AfterSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player)
            return;
        BlitzKernel.GainStars(ref x, (int)x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>Chandelier</c>: <c>AfterSideTurnStart</c>, player turn 3, gains 3 energy
/// (<c>TriggerRelicsAfterSideTurnStart</c>).
/// </summary>
[BlitzContent("CHANDELIER")]
public sealed class Chandelier : BlitzRelicDef
{
    // Chandelier.cs: EnergyVar(3), _energyTurn = 3.
    private const int EnergyTurn = 3;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side == BlitzSide.Player && x.B.PlayerTurn == EnergyTurn)
            BlitzKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>CaptainsWheel</c>: <c>AfterBlockCleared</c>, player turn 3, gains 18 unpowered block
/// (<c>SimulatedCombatState.ReactiveRelics.TriggerRelicsAfterBlockCleared</c>).
/// </summary>
[BlitzContent("CAPTAINS_WHEEL")]
public sealed class CaptainsWheel : BlitzRelicDef
{
    // CaptainsWheel.cs: BlockVar(18m, ValueProp.Unpowered); block turn 3 is a code constant.
    private const int BlockTurn = 3;

    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterBlockCleared(ref BlitzExec x, BlitzListener self)
    {
        if (x.B.PlayerTurn == BlockTurn)
            BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.R.RelicVar(Id, 0),
                BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>CloakClasp</c>: <c>BeforeSideTurnEnd</c> hook (normal pass) gains 1 unpowered block per card in hand
/// (<c>BeforeSideTurnEndMirrors.HandleCloakClasp</c>).
/// </summary>
[BlitzContent("CLOAK_CLASP")]
public sealed class CloakClasp : BlitzRelicDef
{
    // CloakClasp.cs: BlockVar(1m, ValueProp.Unpowered), multiplied by the hand count.
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void BeforeSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        if (!BlitzKernel.Participates(participants, self.Owner) || x.B.HandCount == 0)
            return;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.B.HandCount * x.R.RelicVar(Id, 0),
            BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>StoneCalendar</c>: <c>BeforeSideTurnEnd</c> on player turn 7 deals 52 unpowered damage to every
/// hittable enemy (<c>BeforeSideTurnEndMirrors.HandleStoneCalendar</c>). The existing engine reads the
/// turn number, not a private counter, so no imported state is needed.
/// </summary>
[BlitzContent("STONE_CALENDAR")]
public sealed class StoneCalendar : BlitzRelicDef
{
    // StoneCalendar.cs: DamageVar(52m, ValueProp.Unpowered), DynamicVar("DamageTurn", 7m).
    public override IReadOnlyList<string> VarNames => ["Damage", "DamageTurn"];

    public override void BeforeSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        if (!BlitzKernel.Participates(participants, self.Owner)
            || x.B.PlayerTurn != (int)x.R.RelicVar(Id, 1))
            return;
        BlitzCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered,
            BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>GamePiece</c>: <c>AfterCardPlayed</c>, after a Power card, draws 1
/// (<c>AfterCardPlayedMirrors.HandleGamePiece</c>).
/// </summary>
[BlitzContent("GAME_PIECE")]
public sealed class GamePiece : BlitzRelicDef
{
    // GamePiece.cs: CardsVar(1).
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Power)
            return;
        BlitzKernel.Draw(ref x, (int)x.R.RelicVar(Id, 0), fromHandDraw: false);
    }
}

/// <summary>
/// <c>RazorTooth</c>: <c>AfterCardPlayed</c> upgrades the played Attack or Skill when it is still
/// upgradable (<c>AfterCardPlayedMirrors.HandleRazorTooth</c>).
/// </summary>
[BlitzContent("RAZOR_TOOTH")]
public sealed class RazorTooth : BlitzRelicDef
{
    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        BlitzCardStats stats = x.R.Card(play.Card.Id, play.Card.Upgrade);
        if (stats.Type is not (BlitzCardType.Attack or BlitzCardType.Skill))
            return;
        if (play.Card.Upgrade >= stats.MaxUpgradeLevel)
            return;
        BlitzCardEffects.UpgradeCurrentCard(ref x);
    }
}

/// <summary>
/// <c>IntimidatingHelmet</c>: <c>BeforeCardPlayed</c>, when the play's energy value is at least 2, gains
/// 4 unpowered block (<c>BeforeCardPlayedMirrors.HandleIntimidatingHelmet</c>; Blitz's
/// <see cref="BlitzExec.XValue"/> carries the same <c>CardPlay.Resources.EnergyValue</c>).
/// </summary>
[BlitzContent("INTIMIDATING_HELMET")]
public sealed class IntimidatingHelmet : BlitzRelicDef
{
    // IntimidatingHelmet.cs: BlockVar(4m, ValueProp.Unpowered), EnergyVar(2).
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (x.XValue < (int)x.R.RelicVar(Id, 1))
            return;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>CharonsAshes</c>: <c>AfterCardExhausted</c> deals 3 unpowered damage to every hittable enemy
/// (<c>AfterCardExhaustedMirrors.HandleCharonsAshes</c>).
/// </summary>
[BlitzContent("CHARONS_ASHES")]
public sealed class CharonsAshes : BlitzRelicDef
{
    // CharonsAshes.cs: DamageVar(3m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterCardExhausted(ref BlitzExec x, BlitzListener self, in BlitzCard card, bool causedByEthereal)
        => BlitzCardEffects.DamageAllEnemies(ref x, x.R.RelicVar(Id, 0), BlitzValueProps.Unpowered,
            BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>SturdyClamp</c>: <c>ShouldClearBlock</c> declines the player's block clear and
/// <c>BlockAfterPreventingClear</c> keeps at most 10
/// (<c>PersistentRelicSupport.ShouldClearBlock</c>/<c>BlockAfterPreventingClear</c>).
/// </summary>
[BlitzContent("STURDY_CLAMP")]
public sealed class SturdyClamp : BlitzRelicDef
{
    // SturdyClamp.cs: BlockVar(10m, ValueProp.Unpowered); the engine reads DynamicVars.Block.
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override bool ShouldClearBlock(ref BlitzExec x, BlitzListener self, byte owner)
        => owner != BlitzLimits.PlayerOwner;

    public override int BlockAfterPreventingClear(ref BlitzExec x, BlitzListener self, byte owner, int block)
        => owner == BlitzLimits.PlayerOwner ? (int)Math.Min(block, x.R.RelicVar(Id, 0)) : block;
}

/// <summary>
/// <c>TungstenRod</c>: <c>ModifyHpLostAfterOsty</c> reduces the owner's HP loss by
/// <c>HpLossReduction</c> (<c>ModifyHpLostMirrors</c> native fallback,
/// <c>PredictionCoverage.IsVerifiedNativeRelicHook</c>). <c>AfterModifyingHpLostAfterOsty</c> only flashes.
/// </summary>
[BlitzContent("TUNGSTEN_ROD")]
public sealed class TungstenRod : BlitzRelicDef
{
    // TungstenRod.cs: DynamicVar("HpLossReduction", 1m).
    public override IReadOnlyList<string> VarNames => ["HpLossReduction"];

    public override decimal ModifyHpLostAfterOsty(ref BlitzExec x, BlitzListener self, in BlitzHpLossContext loss,
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
[BlitzContent("MUMMIFIED_HAND")]
public sealed class MummifiedHand : BlitzRelicDef
{
    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (!BlitzKernel.IsInProgress(x.B)
            || BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Power)
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
        BlitzKernel.MakeFreeThisTurn(x.R, ref x.B.Cards[index]);
    }

    private static int PickHandCard(ref BlitzExec x, bool naturallyCostly, bool costsResources)
    {
        int matches = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (Matches(ref x, index, naturallyCostly, costsResources))
                matches++;
        }
        if (matches == 0)
            return -1;
        int pick = x.B.RngStream(BlitzRngStream.CombatCardSelection).NextInt(0, matches);
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (!Matches(ref x, index, naturallyCostly, costsResources))
                continue;
            if (pick-- == 0)
                return index;
        }
        throw new InvalidOperationException("手牌随机选择没有落到任何候选。");
    }

    private static bool Matches(ref BlitzExec x, int index, bool naturallyCostly, bool costsResources)
    {
        if (naturallyCostly && !IsNaturallyCostly(x.B, x.B.Cards[index]))
            return false;
        if (!costsResources)
            return true;
        ref readonly BlitzCard card = ref x.B.Cards[index];
        return BlitzKernel.EnergyCostWithModifiers(x.B, x.R, card, BlitzPile.Hand) > 0
            || BlitzKernel.StarCostWithModifiers(x.B, x.R, card, BlitzPile.Hand) > 0;
    }

    /// <summary>
    /// <c>CardEnergyCost.GetWithModifiers(CostModifiers.None)</c>: the current base cost (upgrade
    /// included) without local or global modifiers. A negative (Unplayable) or X cost is not costly.
    /// </summary>
    private static bool IsNaturallyCostly(in BlitzBattle battle, in BlitzCard card)
        => card.BaseStarCost > 0 || card.BaseCost > 0;
}

/// <summary>
/// <c>IvoryTile</c>: <c>AfterCardPlayed</c> gains Energy when the play's energy value reaches
/// <c>EnergyThreshold</c> (<c>AfterCardPlayedMirrors.HandleIvoryTile</c>; <see cref="BlitzExec.XValue"/>
/// carries <c>CardPlay.Resources.EnergyValue</c>).
/// </summary>
[BlitzContent("IVORY_TILE")]
public sealed class IvoryTile : BlitzRelicDef
{
    // IvoryTile.cs: EnergyVar(1), EnergyVar("EnergyThreshold", 3).
    public override IReadOnlyList<string> VarNames => ["Energy", "EnergyThreshold"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (x.XValue < (int)x.R.RelicVar(Id, 1))
            return;
        BlitzKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>IceCream</c>: the player keeps their energy between turns after the first
/// (<c>PersistentRelicSupport.ShouldPlayerResetEnergy</c>: the native relic returns false from turn 2).
/// </summary>
[BlitzContent("ICE_CREAM")]
public sealed class IceCream : BlitzRelicDef
{
    public override bool ShouldPlayerResetEnergy(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        byte player)
        => self.Owner != player || battle.PlayerTurn <= 1;
}

/// <summary>
/// <c>RuinedHelmet</c>: the first positive Strength received this combat is doubled
/// (<c>SimulatedCombatState.ModifyPowerAmountForRelics</c> / <c>ModifyPowerAmountForRuinedHelmet</c>,
/// native <c>TryModifyPowerAmountReceived</c> and <c>AfterModifyingPowerAmountReceived</c>). The
/// used flag lives in <see cref="BlitzRelicFlags.UsedThisCombat"/>; the bridge projects
/// <c>RuinedHelmet._usedThisCombat</c>.
/// </summary>
[BlitzContent("RUINED_HELMET")]
public sealed class RuinedHelmet : BlitzRelicDef
{
    public override bool TryModifyPowerAmountReceived(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        ushort powerId, byte target, int amount, out int modified)
    {
        modified = amount;
        if (amount <= 0
            || target != self.Owner
            || !BlitzRegistry.TryPower("STRENGTH_POWER", out ushort strength)
            || powerId != strength
            || (battle.Relics[self.Index].Flags & BlitzRelicFlags.UsedThisCombat) != 0)
        {
            return false;
        }
        modified = amount * 2;
        return true;
    }

    public override void AfterModifyingPowerAmountReceived(ref BlitzExec x, BlitzListener self, ushort powerId,
        byte target)
        => x.B.Relics[self.Index].Flags |= BlitzRelicFlags.UsedThisCombat;
}

/// <summary>
/// <c>UnceasingTop</c>: the draw is a kernel special case in
/// <c>SimulatedCombatState.TriggerRelicsAfterHandEmptied</c> (the existing engine does not dispatch a
/// generic relic hook for it), so the definition only makes the relic admissible.
/// </summary>
[BlitzContent("UNCEASING_TOP")]
public sealed class UnceasingTop : BlitzRelicDef;

/// <summary>
/// <c>Bellows</c>: <c>AfterPlayerTurnStart</c> upgrades the hand only when the turn number is &lt;= 1
/// (<c>ContinueRelicsAfterPlayerTurnStart</c>). The player turn 1 start is before the root and turn 2 no
/// longer satisfies the condition.
/// </summary>
[BlitzContent("BELLOWS")]
public sealed class Bellows : BlitzRelicDef;

/// <summary>
/// <c>GamblingChip</c>: <c>AfterPlayerTurnStart</c> offers its discard/draw choice only on turn 1
/// (<c>TurnStartChoiceSupport.ResolveDiscardAndDraw</c> from <c>ContinueRelicsAfterPlayerTurnStart</c>).
/// The choice belongs to the player turn start, before the play-phase root.
/// </summary>
[BlitzContent("GAMBLING_CHIP")]
public sealed class GamblingChip : BlitzRelicDef;

/// <summary>
/// <c>VexingPuzzlebox</c>: <c>AfterPlayerTurnStart</c> generates one free card only when the turn number is
/// 1 (<c>ContinueRelicsAfterPlayerTurnStart</c>), at the player turn start before the play-phase root.
/// </summary>
[BlitzContent("VEXING_PUZZLEBOX")]
public sealed class VexingPuzzlebox : BlitzRelicDef
{
    // Turn-start generation samples the character pool; the turn-setup root is captured before
    // this hook fires, so the rules table must carry the generated card stats.
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAll];
}

/// <summary><c>FrozenEgg</c>: merchant/card-reward upgrade hooks and <c>IsAllowed</c> only.</summary>
[BlitzContent("FROZEN_EGG")]
public sealed class FrozenEgg : BlitzRelicDef;

/// <summary><c>Girya</c>: <c>AfterRoomEntered</c>, <c>TryModifyRestSiteOptions</c> and <c>IsAllowed</c> only.</summary>
[BlitzContent("GIRYA")]
public sealed class Girya : BlitzRelicDef;

/// <summary><c>Mango</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[BlitzContent("MANGO")]
public sealed class Mango : BlitzRelicDef;

/// <summary>
/// <c>MeatOnTheBone</c>: <c>BeforeCombatStart</c> and <c>AfterCurrentHpChanged</c> only maintain the
/// relic status for the after-victory heal in <c>AfterCombatVictoryEarly</c>. The existing engine's
/// combat-hook catalog marks it NotCombatRelevant, so there is no combat dispatch to mirror.
/// </summary>
[BlitzContent("MEAT_ON_THE_BONE")]
public sealed class MeatOnTheBone : BlitzRelicDef;

/// <summary><c>MoltenEgg</c>: merchant/card-reward upgrade hooks and <c>IsAllowed</c> only.</summary>
[BlitzContent("MOLTEN_EGG")]
public sealed class MoltenEgg : BlitzRelicDef;

/// <summary><c>OldCoin</c>: <c>AfterObtained</c> gold gain and <c>IsAllowed</c> only.</summary>
[BlitzContent("OLD_COIN")]
public sealed class OldCoin : BlitzRelicDef;

/// <summary><c>PrayerWheel</c>: <c>TryModifyRewards</c> only.</summary>
[BlitzContent("PRAYER_WHEEL")]
public sealed class PrayerWheel : BlitzRelicDef;

/// <summary><c>Shovel</c>: <c>TryModifyRestSiteOptions</c> and <c>IsAllowed</c> only.</summary>
[BlitzContent("SHOVEL")]
public sealed class Shovel : BlitzRelicDef;

/// <summary><c>TheCourier</c>: <c>ModifyMerchantPrice</c> and <c>ShouldRefillMerchantEntry</c> only.</summary>
[BlitzContent("THE_COURIER")]
public sealed class TheCourier : BlitzRelicDef;

/// <summary><c>ToxicEgg</c>: merchant/card-reward upgrade hooks and <c>IsAllowed</c> only.</summary>
[BlitzContent("TOXIC_EGG")]
public sealed class ToxicEgg : BlitzRelicDef;

/// <summary><c>WhiteBeastStatue</c>: <c>ShouldForcePotionReward</c> and <c>IsAllowed</c> only.</summary>
[BlitzContent("WHITE_BEAST_STATUE")]
public sealed class WhiteBeastStatue : BlitzRelicDef;

/// <summary><c>WhiteStar</c>: <c>TryModifyRewards</c> and <c>IsAllowed</c> only.</summary>
[BlitzContent("WHITE_STAR")]
public sealed class WhiteStar : BlitzRelicDef;

/// <summary>
/// <c>ArtOfWar</c>: <c>AfterCardPlayed</c> marks an attack played this turn; <c>AfterSideTurnEnd</c>
/// moves that mark to last turn; <c>AfterEnergyReset</c> grants its Energy when last turn had no
/// attack (<c>SimulatedCombatState.RecordRelicCardPlayed</c>, <c>CompleteRelicsAfterSideTurnEnd</c>,
/// <c>TriggerRelicsAfterEnergyReset</c>).
/// Counter = any attack played this turn (0/1), Aux = any attack played last turn (0/1).
/// </summary>
[BlitzContent("ART_OF_WAR")]
public sealed class ArtOfWar : BlitzRelicDef
{
    // ArtOfWar.cs: EnergyVar("GainEnergy", 1).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) == BlitzCardType.Attack)
            x.B.Relics[self.Index].Counter = 1;
    }

    public override void AfterSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Aux = relic.Counter;
        relic.Counter = 0;
    }

    public override void AfterEnergyReset(ref BlitzExec x, BlitzListener self)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (x.B.PlayerTurn > 1 && relic.Aux == 0)
            BlitzKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
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
[BlitzContent("DEMON_TONGUE")]
public sealed class DemonTongue : BlitzRelicDef
{
    public override void AfterDamageReceived(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (!BlitzKernel.IsInProgress(x.B) || x.B.Side != BlitzSide.Player
            || result.Receiver != BlitzLimits.PlayerOwner || result.UnblockedDamage <= 0
            || x.B.Relics[self.Index].Counter != 0)
            return;
        x.B.Relics[self.Index].Counter = 1;
        BlitzKernel.Heal(ref x, BlitzLimits.PlayerOwner, result.UnblockedDamage);
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>Kunai</c>: every third attack played this turn applies Dexterity; <c>BeforeSideTurnStart</c>
/// resets the counter (<c>AfterCardPlayedMirrors.HandleKunai</c> and
/// <c>RelicPredictionStateSupport.ResetBeforeSideTurnStart</c>).
/// Counter = attacks played this turn.
/// </summary>
[BlitzContent("KUNAI")]
public sealed class Kunai : BlitzRelicDef
{
    // Kunai.cs: CardsVar(3), PowerVar<DexterityPower>(1m).
    public override IReadOnlyList<string> VarNames => ["Cards", "DexterityPower"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (!BlitzKernel.IsInProgress(x.B) || BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Attack)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        // ApplyRelicStatPower declines while the combat is ending; the counter still increments.
        if (relic.Counter % (int)x.R.RelicVar(Id, 0) != 0 || BlitzKernel.IsEnding(x.B))
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<DexterityPower>(),
            (int)x.R.RelicVar(Id, 1), BlitzLimits.PlayerOwner);
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
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
[BlitzContent("POCKETWATCH")]
public sealed class Pocketwatch : BlitzRelicDef
{
    // Pocketwatch.cs: DynamicVar("CardThreshold", 3m), CardsVar(3).
    public override IReadOnlyList<string> VarNames => ["CardThreshold", "Cards"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
        => x.B.Relics[self.Index].Counter++;

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Aux = relic.Counter;
        relic.Counter = 0;
    }

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
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
/// <see cref="BlitzCounter.AttacksPlayedThisTurn"/> and <see cref="BlitzCounter.SkillCardsPlayedThisTurn"/>
/// (the existing engine's own fields are not read after the latch closes).
/// </summary>
[BlitzContent("RAINBOW_RING")]
public sealed class RainbowRing : BlitzRelicDef
{
    // RainbowRing.cs: PowerVar<StrengthPower>(1m), PowerVar<DexterityPower>(1m).
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter = 0;
        relic.Aux = 0;
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (!BlitzKernel.IsInProgress(x.B) || x.B.Relics[self.Index].Counter >= 1)
            return;
        BlitzCardType type = BlitzKernel.CardType(x.B, x.R, play.Card);
        int attacks = x.B.Counter(BlitzCounter.AttacksPlayedThisTurn) + (type == BlitzCardType.Attack ? 1 : 0);
        int skills = x.B.Counter(BlitzCounter.SkillCardsPlayedThisTurn) + (type == BlitzCardType.Skill ? 1 : 0);
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (type == BlitzCardType.Power)
            relic.Aux++;
        if (attacks == 0 || skills == 0 || relic.Aux == 0)
            return;
        if (!BlitzKernel.IsEnding(x.B))
        {
            BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<StrengthPower>(),
                (int)x.R.RelicVar(Id, 0), BlitzLimits.PlayerOwner);
            BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<DexterityPower>(),
                (int)x.R.RelicVar(Id, 1), BlitzLimits.PlayerOwner);
        }
        relic.Counter = 1;
    }
}

/// <summary>
/// <c>Shuriken</c>: every third attack played this turn applies Strength;
/// <c>BeforeSideTurnStart</c> resets the counter (<c>AfterCardPlayedMirrors.HandleShuriken</c>).
/// Counter = attacks played this turn.
/// </summary>
[BlitzContent("SHURIKEN")]
public sealed class Shuriken : BlitzRelicDef
{
    // Shuriken.cs: CardsVar(3), PowerVar<StrengthPower>(1m).
    public override IReadOnlyList<string> VarNames => ["Cards", "StrengthPower"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (!BlitzKernel.IsInProgress(x.B) || BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Attack)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        // ApplyRelicStatPower declines while the combat is ending; the counter still increments.
        if (relic.Counter % (int)x.R.RelicVar(Id, 0) != 0 || BlitzKernel.IsEnding(x.B))
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<StrengthPower>(),
            (int)x.R.RelicVar(Id, 1), BlitzLimits.PlayerOwner);
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
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
[BlitzContent("BEATING_REMNANT")]
public sealed class BeatingRemnant : BlitzRelicDef
{
    // BeatingRemnant.cs: DynamicVar("MaxHpLoss", 20m).
    public override IReadOnlyList<string> VarNames => ["MaxHpLoss"];

    public override decimal ModifyHpLostAfterOsty(ref BlitzExec x, BlitzListener self, in BlitzHpLossContext loss,
        decimal amount)
    {
        if (loss.Target != self.Owner)
            return amount;
        return Math.Min(amount, x.R.RelicVar(Id, 0) - x.B.Relics[self.Index].Counter);
    }

    public override void AfterDamageReceived(ref BlitzExec x, BlitzListener self, in BlitzDamageResult result, byte dealer)
    {
        if (result.Receiver == self.Owner)
            x.B.Relics[self.Index].Counter += result.UnblockedDamage;
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>LizardTail</c>: <c>ShouldDieLate</c> prevents its owner's first death of the combat, then
/// <c>AfterPreventingDeath</c> marks it used and heals Heal percent of max HP
/// (<c>DeathPreventerMirrors.LizardTailMirrors</c>). Counter = WasUsed (0/1); the bridge projects
/// <c>LizardTailPredictionState.WasUsed</c>.
/// </summary>
[BlitzContent("LIZARD_TAIL")]
public sealed class LizardTail : BlitzRelicDef
{
    // LizardTail.cs: HealVar(50m).
    public override IReadOnlyList<string> VarNames => ["Heal"];

    public override bool ShouldDieLate(ref BlitzExec x, BlitzListener self, byte creature)
        => creature != self.Owner || x.B.Relics[self.Index].Counter != 0;

    public override void AfterPreventingDeath(ref BlitzExec x, BlitzListener self, byte creature)
    {
        x.B.Relics[self.Index].Counter = 1;
        decimal amount = Math.Max(1m, x.B.Creature(creature).MaxHp * (x.R.RelicVar(Id, 0) / 100m));
        int hpBeforeRevive = x.B.Creature(creature).CurrentHp;
        BlitzKernel.Heal(ref x, creature, amount);
        int restored = x.B.Creature(creature).CurrentHp - hpBeforeRevive;
        if (restored > 0)
        {
            x.B.AddCounter(BlitzCounter.DeathSaveRelicHpRestored, restored);
            x.B.AddCounter(BlitzCounter.DeathSaveUseCount, 1);
        }
    }
}

/// <summary>
/// <c>HelicalDart</c>: after a card with the Shiv tag is played, apply 1 temporary Dexterity
/// (<c>AfterCardPlayedMirrors.HandleHelicalDart</c> → <c>ApplyTemporaryDexterity</c>).
/// </summary>
[BlitzContent("HELICAL_DART")]
public sealed class HelicalDart : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if ((x.R.Card(play.Card.Id, play.Card.Upgrade).Tags & BlitzCardTag.Shiv) == 0)
            return;
        int amount = (int)x.R.RelicVar(Id, 0);
        ushort temporary = BlitzRegistry.PowerId<Powers.HelicalDartPower>();
        ushort dexterity = BlitzRegistry.PowerId<Powers.DexterityPower>();
        bool alreadyApplied = BlitzKernel.GetAmount(x.B, BlitzLimits.PlayerOwner, temporary) != 0;
        if (!alreadyApplied)
            BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, dexterity, amount, BlitzLimits.PlayerOwner);
        int applied = BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, temporary, amount,
            BlitzLimits.PlayerOwner);
        if (alreadyApplied && applied != 0)
            BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, dexterity, applied, BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>PaperKrane</c>: <c>ModifyWeakMultiplier</c> subtracts 0.15 for powered attacks against the
/// owner. The existing engine routes it through <c>WeakPower</c>'s multiplier; the Blitz kernel reads
/// the relic there, so this definition only declares the rule-table variable.
/// </summary>
[BlitzContent("PAPER_KRANE")]
public sealed class PaperKrane : BlitzRelicDef
{
}
