namespace CombatSolver.Engine.InCombat.Flash.Content.Relics;

// Potions-and-relics batch. Every in-combat definition names the existing engine location it
// mirrors; numbers come from the per-root relic rule table. Relics whose only native hook runs
// outside combat or before the first searchable root carry no Hook override and say why.

// ---------------------------------------------------------------------------------------------
// The 17 relics the FI inventory found with an existing-engine mirror/compensation reference.
// ---------------------------------------------------------------------------------------------

/// <summary>
/// <c>BiiigHug</c>: after a shuffle by its owner, create one Soot into the draw pile at a random
/// position (<c>AfterShuffleMirrors.HandleBiiigHug</c>). <c>AfterObtained</c> is a deck-only effect.
/// </summary>
[FlashContent("BIIIG_HUG")]
public sealed class BiiigHug : FlashRelicDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOOT", 0)];

    public override void AfterShuffle(ref FlashExec x, FlashListener self, byte shuffler)
    {
        if (shuffler != FlashLimits.PlayerOwner)
            return;
        FlashCard soot = FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Soot>());
        FlashKernel.AddGeneratedCard(ref x, soot, FlashPile.Draw, FlashPilePosition.Random);
    }
}

/// <summary>
/// <c>BoneFlute</c>: after an attack by the owner's Osty, gain its Block variable
/// (<c>AfterAttackMirrors.HandleBoneFlute</c>; the BlockVar is Unpowered).
/// </summary>
[FlashContent("BONE_FLUTE")]
public sealed class BoneFlute : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterAttack(ref FlashExec x, FlashListener self, byte attacker)
    {
        if (attacker != FlashLimits.OstyOwner)
            return;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            fromCard: false);
    }
}

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
/// <c>ChoicesParadox</c>: on the first player turn start, generate <c>Cards</c> distinct character
/// cards, add Retain to each, and add the one chosen from the generated options to hand
/// (<c>SimulatedCombatState.RelicTurnStart</c> case ChoicesParadox / <c>TurnStartChoiceSupport.ResolveGeneratedToHand</c>).
/// </summary>
[FlashContent("CHOICES_PARADOX")]
public sealed class ChoicesParadox : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
        if (x.B.PlayerTurn > 1)
            return;
        int count = (int)x.R.RelicVar(Id, 0);
        if (count <= 0)
            return;
        if (count > FlashLimits.Cards)
            throw new FlashCapacityException("GeneratedCards", FlashLimits.Cards);
        Span<FlashCard> options = stackalloc FlashCard[count];
        int sampled = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.CharacterAll, count,
            distinct: true, FlashRngStream.CombatCardGeneration, options);
        if (sampled == 0)
            return;
        for (int index = 0; index < sampled; index++)
            options[index].Keywords |= FlashKeywords.Retain;
        if (!FlashKernel.ChooseOption(ref x, FlashChoiceEffect.GenerateToHand, options[..sampled], out int selected))
            return;
        FlashKernel.AddGeneratedCard(ref x, options[selected], FlashPile.Hand, FlashPilePosition.Bottom);
    }
}

/// <summary>
/// <c>Crossbow</c>: at every player side turn start, generate one distinct character Attack, make it
/// free this turn and add it to hand (<c>SimulatedCombatState.RelicTurnStart</c> case Crossbow).
/// </summary>
[FlashContent("CROSSBOW")]
public sealed class Crossbow : FlashRelicDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAttacks];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player)
            return;
        Span<FlashCard> selected = stackalloc FlashCard[1];
        int sampled = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.CharacterAttacks, 1,
            distinct: true, FlashRngStream.CombatCardGeneration, selected);
        if (sampled == 0)
            return;
        FlashCard card = selected[0];
        FlashKernel.MakeFreeThisTurn(x.R, ref card);
        FlashKernel.AddGeneratedCard(ref x, card, FlashPile.Hand, FlashPilePosition.Bottom);
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
    }

    public override void AfterRelicRound(ref FlashExec x, FlashListener self)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter = relic.Aux;
        relic.Aux = 0;
    }
}

/// <summary>
/// <c>FurCoat</c>: enemies created at a marked map coordinate start at 1 current HP. The marked
/// coordinate is out-of-combat data frozen into <see cref="FlashRules.FurCoatSpawnHpOne"/>
/// (<c>MonsterSpawnSupport.ApplyCreatureAddedRelics</c>); the relic itself has no combat hook.
/// </summary>
[FlashContent("FUR_COAT")]
public sealed class FurCoat : FlashRelicDef
{
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
/// <c>MusicBox</c>: the first Attack played each turn is cloned with Ethereal and added to hand once
/// (<c>BeforeCardPlayedMirrors.HandleMusicBox</c> + <c>AfterCardPlayedMirrors.HandleMusicBox</c>;
/// the per-turn latch is <c>MusicBoxPredictionState.WasUsedThisTurn</c>).
/// </summary>
[FlashContent("MUSIC_BOX")]
public sealed class MusicBox : FlashRelicDef
{
    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter != 0 || x.MusicBoxPlayIdentity != 0)
            return;
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Attack)
            return;
        x.MusicBoxPlayIdentity = x.PlayIdentity;
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (x.MusicBoxPlayIdentity == 0 || x.MusicBoxPlayIdentity != x.LastPlayIdentity)
            return;
        x.MusicBoxPlayIdentity = 0;
        FlashCard clone = FlashKernel.CloneCard(play.Card);
        clone.Keywords |= FlashKeywords.Ethereal;
        if (!FlashKernel.AddGeneratedCard(ref x, clone, FlashPile.Hand, FlashPilePosition.Bottom))
            return;
        if (x.NeedsChoice)
            return;
        relic.Counter = 1;
    }

    public override void BeforeSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side == FlashSide.Player)
            x.B.Relics[self.Index].Counter = 0;
    }
}

/// <summary>
/// <c>PaelsEye</c>: at the end of a player turn with no manually played card, exhaust the hand and
/// take an extra turn once per combat. <c>BeforeSideTurnEndEarly</c> exhausts, <c>ShouldTakeExtraTurn</c>
/// grants the turn and <c>AfterTakingExtraTurn</c> sets the used latch
/// (<c>SimulatedCombatState.ReactiveRelics.cs</c> + <c>ExtraTurnMirrors</c>).
/// </summary>
[FlashContent("PAELS_EYE")]
public sealed class PaelsEye : FlashRelicDef
{
    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
        // TriggerRelicsAfterPlayerTurnStart case PaelsEye: state.Previous = 1.
        x.B.Relics[self.Index].Aux = 1;
    }

    public override void BeforeSideTurnEndEarly(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (side != FlashSide.Player
            || !FlashKernel.Participates(participants, FlashLimits.PlayerOwner)
            || !ShouldTrigger(x.B, x.B.Relics[self.Index]))
            return;
        int handCount = x.B.HandCount;
        Span<int> selected = stackalloc int[handCount];
        for (int index = 0; index < handCount; index++)
            selected[index] = index;
        FlashKernel.ExhaustSelected(ref x, FlashPile.Hand, selected);
    }

    public override bool ShouldTakeExtraTurn(ref FlashExec x, FlashListener self)
        => ShouldTrigger(x.B, x.B.Relics[self.Index]);

    public override void AfterTakingExtraTurn(ref FlashExec x, FlashListener self)
    {
        x.B.Relics[self.Index].Counter = 1;
    }

    private bool ShouldTrigger(in FlashBattle battle, in FlashRelic relic)
    {
        if (relic.Counter != 0 || relic.Aux == 0)
            return false;
        if (battle.Counter(FlashCounter.ManualCardsPlayedThisTurn) != 0)
            return false;
        if (battle.PlayerTurn == 1 && HasRelic(battle, "WHISPERING_EARRING"))
            return false;
        return true;
    }

    private static bool HasRelic(in FlashBattle battle, string nativeId)
    {
        if (!FlashRegistry.TryRelic(nativeId, out ushort id))
            return false;
        for (int index = 0; index < battle.RelicCount; index++)
        {
            if (battle.Relics[index].Id == id && (battle.Relics[index].Flags & FlashRelicFlags.Melted) == 0)
                return true;
        }
        return false;
    }
}

/// <summary>
/// <c>PaelsLegion</c>: doubles its owner's card block while the cooldown is down; a block-gaining
/// card play starts a cooldown of Turns. The cooldown ticks down at each player side turn start
/// (<c>ModifyBlockMultiplicativeMirrors</c>, <c>AfterModifyingBlockAmountMirrors</c>,
/// <c>AfterCardPlayedMirrors</c> and <c>RelicPredictionStateSupport.ResetAfterSideTurnStart</c>).
/// </summary>
[FlashContent("PAELS_LEGION")]
public sealed class PaelsLegion : FlashRelicDef
{
    public override bool IsPaelsLegion => true;

    public override IReadOnlyList<string> VarNames => ["Turns"];

    public override decimal ModifyBlockMultiplicative(ref FlashExec x, FlashListener self, in FlashBlockContext block,
        decimal amount)
    {
        if ((block.Props & FlashValueProps.Move) == 0 || !block.HasCardSource)
            return 1m;
        return x.B.Relics[self.Index].Counter > 0 ? 1m : 2m;
    }

    public override void AfterModifyingBlockAmount(ref FlashExec x, FlashListener self, decimal modified,
        in FlashBlockContext block)
    {
        if (modified <= 0m)
            return;
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (relic.Aux != 0 && relic.Aux != x.PlayIdentity)
            return;
        relic.Aux = x.PlayIdentity;
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        if (relic.Aux == 0 || relic.Aux != x.LastPlayIdentity)
            return;
        relic.Aux = 0;
        relic.Counter = (int)x.R.RelicVar(Id, 0);
        relic.Flags |= FlashRelicFlags.UsedThisCombat;
    }

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        // RelicPredictionStateSupport.ResetAfterSideTurnStart case PaelsLegion runs before the case
        // switch: cooldown-- and the display latch clears, whatever the current cooldown sign is.
        ref FlashRelic relic = ref x.B.Relics[self.Index];
        relic.Counter--;
        relic.Flags &= ~FlashRelicFlags.UsedThisCombat;
    }
}

/// <summary>
/// <c>SpikedGauntlets</c>: +Energy maximum energy, and Power cards cost 1 more
/// (<c>ModifyMaxEnergy</c> and the verified native <c>TryModifyEnergyCostInCombat</c>).
/// </summary>
[FlashContent("SPIKED_GAUNTLETS")]
public sealed class SpikedGauntlets : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);

    public override decimal TryModifyEnergyCostInCombat(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount)
        => rules.Card(card.Id, card.Upgrade).Type == FlashCardType.Power ? amount + 1m : amount;
}

/// <summary>
/// <c>ThrowingAxe</c>: the first card played each combat is played one extra time
/// (<c>ModifyCardPlayCountMirrors.HandleThrowingAxe</c> / <c>HandleThrowingAxeAfter</c>; the latch is
/// <c>ThrowingAxePredictionState.UsedThisCombat</c>, reset out of combat by <c>AfterRoomEntered</c>).
/// </summary>
[FlashContent("THROWING_AXE")]
public sealed class ThrowingAxe : FlashRelicDef
{
    public override int ModifyCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card, byte target,
        int playCount)
        => x.B.Relics[self.Index].Counter == 0 ? playCount + 1 : playCount;

    public override void AfterModifyingCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card)
    {
        x.B.Relics[self.Index].Counter = 1;
    }
}

/// <summary>
/// <c>ToastyMittens</c>: at the player turn start exhaust one card from hand, then apply its Strength
/// variable (<c>SimulatedCombatState.RelicTurnStart</c> case ToastyMittens).
/// </summary>
[FlashContent("TOASTY_MITTENS")]
public sealed class ToastyMittens : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
        if (x.B.HandCount > 0)
        {
            Span<int> options = stackalloc int[x.B.HandCount];
            for (int index = 0; index < options.Length; index++)
                options[index] = index;
            Span<int> selected = stackalloc int[1];
            if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand, options, 1, selected,
                    out int chosen))
            {
                return;
            }
            FlashKernel.StartPlayerSideTurnIfNeeded(ref x);
            if (x.NeedsChoice)
                return;
            FlashKernel.ExhaustSelected(ref x, FlashPile.Hand, selected[..chosen]);
            if (x.NeedsChoice)
                return;
        }
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.StrengthPower>(), (int)x.R.RelicVar(Id, 0), FlashLimits.PlayerOwner);
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
/// <c>TwistedFunnel</c>: at the first player side turn start applies Poison to every hittable enemy.
/// The first turn starts before the captured root (the existing engine registers it as ignored for
/// the same reason), so the definition has no combat hook.
/// </summary>
[FlashContent("TWISTED_FUNNEL")]
public sealed class TwistedFunnel : FlashRelicDef
{
}

/// <summary>
/// <c>UndyingSigil</c>: powered attacks from a doomed creature against its owner are multiplied by
/// DamageDecrease (<c>ModifyDamageMirrors.HandleUndyingSigil</c>).
/// </summary>
[FlashContent("UNDYING_SIGIL")]
public sealed class UndyingSigil : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["DamageDecrease"];

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self,
        in FlashDamageContext damage, decimal amount)
    {
        if (damage.Dealer == FlashLimits.NoOwner
            || damage.Dealer == FlashLimits.PlayerOwner
            || damage.Target != FlashLimits.PlayerOwner
            || !damage.Props.IsPoweredAttack())
            return 1m;
        ushort doom = FlashRegistry.PowerId<Powers.DoomPower>();
        return x.B.Creature(damage.Dealer).CurrentHp <= FlashKernel.GetAmount(x.B, damage.Dealer, doom)
            ? x.R.RelicVar(Id, 0)
            : 1m;
    }
}

// ---------------------------------------------------------------------------------------------
// The 20 relics without an existing-engine mirror reference. Each one is either implemented here,
// proven inert for the combat window, or left undefined (rejected at admission) with the reason.
// ---------------------------------------------------------------------------------------------

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
/// <c>InfusedCore</c>: channels 3 Lightning at the first side turn start (before the root) and adds
/// its ExtraDamage to every Lightning orb value (<c>SimulatedCombatState.RelicTurnStart</c> case
/// InfusedCore; native <c>ModifyOrbValue</c> over the combat listeners).
/// </summary>
[FlashContent("INFUSED_CORE")]
public sealed class InfusedCore : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Lightning", "ExtraDamage"];

    public override decimal ModifyOrbValue(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashOrb orb, decimal value)
    {
        if (!FlashRegistry.TryOrb("LIGHTNING_ORB", out ushort lightning) || orb.Id != lightning)
            return value;
        return value + rules.RelicVar(Id, 1);
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
/// <c>PhylacteryUnbound</c>: summons Osty for its StartOfTurn amount at every player side turn start;
/// the StartOfCombat summon is already in the captured root (<c>SimulatedCombatState.RelicTurnStart</c>).
/// </summary>
[FlashContent("PHYLACTERY_UNBOUND")]
public sealed class PhylacteryUnbound : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["StartOfCombat", "StartOfTurn"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player)
            return;
        FlashKernel.SummonOsty(ref x, (int)x.R.RelicVar(Id, 1));
    }
}

/// <summary>
/// <c>SealOfGold</c>: at the player side turn start, if the owner has at least its Gold variable, gain
/// Energy and lose that gold (<c>SimulatedCombatState.RelicTurnStart</c> case SealOfGold).
/// </summary>
[FlashContent("SEAL_OF_GOLD")]
public sealed class SealOfGold : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Gold"];

    public override void AfterSideTurnStart(ref FlashExec x, FlashListener self, FlashSide side)
    {
        if (side != FlashSide.Player)
            return;
        int gold = (int)x.R.RelicVar(Id, 1);
        if (x.B.Gold < gold)
            return;
        FlashKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
        if (x.NeedsChoice)
            return;
        x.B.Gold = Math.Max(0, x.B.Gold - gold);
    }
}

/// <summary>
/// <c>SneckoEye</c>: adds its Cards variable to the player's hand draw; ConfusedPower is applied before
/// combat and is already in the root.
/// </summary>
[FlashContent("SNECKO_EYE")]
public sealed class SneckoEye : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
    {
        return amount + x.R.RelicVar(Id, 0);
    }
}

/// <summary>
/// <c>WhisperingEarring</c>: +Energy maximum energy. Its first-turn auto-play
/// (<c>AfterAutoPrePlayPhaseEnteredLate</c>) runs before the captured root, like Imbued; the relic's
/// presence is also read by PaelsEye's first-turn gate.
/// </summary>
[FlashContent("WHISPERING_EARRING")]
public sealed class WhisperingEarring : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref FlashExec x, FlashListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>HistoryCourse</c>: at the player turn start's AutoPrePlay it clones the last attack played on the
/// previous player turn and auto-plays it (<c>SimulatedCombatState.TriggerScheduledAutoPlays</c> →
/// <c>ContinueHistoryCourse</c>). The clone itself lives in the kernel's AutoPrePlay stage; the relic
/// has no other combat hook. <see cref="FlashRules.HasHistoryCourse"/> gates the per-turn card slot.
/// </summary>
[FlashContent("HISTORY_COURSE")]
public sealed class HistoryCourseRelic : FlashRelicDef
{
}

/// <summary>
/// <c>BingBong</c>: duplicates a card added to the owner's out-of-combat Deck pile
/// (<c>AfterCardChangedPiles</c> with <c>PileType.Deck</c>). Combat piles never produce a Deck-pile
/// event, so it has no hook inside the search window.
/// </summary>
[FlashContent("BING_BONG")]
public sealed class BingBong : FlashRelicDef
{
}

/// <summary>
/// <c>DarkstonePeriapt</c>: gains MaxHp when a Curse is added to the owner's Deck pile
/// (<c>AfterCardChangedPiles</c> with <c>PileType.Deck</c>); deck-only, inert in combat.
/// </summary>
[FlashContent("DARKSTONE_PERIAPT")]
public sealed class DarkstonePeriapt : FlashRelicDef
{
}

/// <summary>
/// <c>DataDisk</c>: applies Focus 1 on entering a combat room (<c>AfterRoomEntered</c>), which is
/// before the captured root; the Focus power is already in the root.
/// </summary>
[FlashContent("DATA_DISK")]
public sealed class DataDisk : FlashRelicDef
{
}

/// <summary>
/// <c>DelicateFrond</c>: fills the open potion slots at <c>BeforeCombatStart</c>; the potions are in
/// the captured root, so there is no in-combat hook.
/// </summary>
[FlashContent("DELICATE_FROND")]
public sealed class DelicateFrond : FlashRelicDef
{
}

/// <summary>
/// <c>DiamondDiadem</c>: gain 20 Unpowered block and 1 Blur at the first side turn start
/// (<c>AfterSideTurnStart</c> with <c>TurnNumber &lt;= 1</c>), before the captured root.
/// </summary>
[FlashContent("DIAMOND_DIADEM")]
public sealed class DiamondDiadem : FlashRelicDef
{
}

/// <summary>
/// <c>DivineDestiny</c>: gains 7 stars at the first side turn start
/// (<c>AfterSideTurnStart</c> with <c>TurnNumber &lt;= 1</c>), before the captured root.
/// </summary>
[FlashContent("DIVINE_DESTINY")]
public sealed class DivineDestiny : FlashRelicDef
{
}

/// <summary>
/// <c>FakeSneckoEye</c>: applies Confused 1 at <c>BeforeCombatStart</c>; the power is in the captured
/// root, so the relic itself has no in-combat hook.
/// </summary>
[FlashContent("FAKE_SNECKO_EYE")]
public sealed class FakeSneckoEye : FlashRelicDef
{
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
/// <c>NinjaScroll</c>: adds 3 Shivs to hand at the first <c>BeforeHandDraw</c>, before the captured
/// root; the Shivs are already in the root hand.
/// </summary>
[FlashContent("NINJA_SCROLL")]
public sealed class NinjaScroll : FlashRelicDef
{
}

/// <summary>
/// <c>RadiantPearl</c>: adds its Cards Luminesce cards to hand at the first <c>BeforeHandDraw</c>,
/// before the captured root; the cards are already in the root hand and have their own definition.
/// </summary>
[FlashContent("RADIANT_PEARL")]
public sealed class RadiantPearl : FlashRelicDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("LUMINESCE", 0), ("LUMINESCE", 1)];
}
