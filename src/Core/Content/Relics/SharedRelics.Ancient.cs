using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Relics;

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
/// <c>RunicPyramid</c>: <c>ShouldFlush</c> declines the owner's hand flush on every turn
/// (<c>PersistentRelicSupport.ShouldFlush</c>).
/// </summary>
[FlashContent("RUNIC_PYRAMID")]
public sealed class RunicPyramid : FlashRelicDef
{
    public override bool ShouldFlush(ref FlashExec x, FlashListener self) => false;
}

/// <summary>
/// <c>VeryHotCocoa</c>: <c>AfterSideTurnStart</c> gains 4 energy only when the player turn number is
/// &lt;= 1 (<c>TriggerRelicsAfterSideTurnStart</c>). The player turn 1 start is before the root.
/// </summary>
[FlashContent("VERY_HOT_COCOA")]
public sealed class VeryHotCocoa : FlashRelicDef;

/// <summary><c>LoomingFruit</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[FlashContent("LOOMING_FRUIT")]
public sealed class LoomingFruit : FlashRelicDef;

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
/// <c>FurCoat</c>: enemies created at a marked map coordinate start at 1 current HP. The marked
/// coordinate is out-of-combat data frozen into <see cref="FlashRules.FurCoatSpawnHpOne"/>
/// (<c>MonsterSpawnSupport.ApplyCreatureAddedRelics</c>); the relic itself has no combat hook.
/// </summary>
[FlashContent("FUR_COAT")]
public sealed class FurCoat : FlashRelicDef
{
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
/// <c>RadiantPearl</c>: adds its Cards Luminesce cards to hand at the first <c>BeforeHandDraw</c>,
/// before the captured root; the cards are already in the root hand and have their own definition.
/// </summary>
[FlashContent("RADIANT_PEARL")]
public sealed class RadiantPearl : FlashRelicDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("LUMINESCE", 0), ("LUMINESCE", 1)];
}

// --- rewards, shops, rest sites, map and pickup effects --------------------------------
[FlashContent("ALCHEMICAL_COFFER")] public sealed class AlchemicalCoffer : FlashRelicDef; // AfterObtained / TryModifyRewards

[FlashContent("ARCANE_SCROLL")] public sealed class ArcaneScroll : FlashRelicDef; // AfterObtained

[FlashContent("ARCHAIC_TOOTH")] public sealed class ArchaicTooth : FlashRelicDef; // AfterCloned / AfterObtained

[FlashContent("ASTROLABE")] public sealed class Astrolabe : FlashRelicDef; // AfterObtained

[FlashContent("BEAUTIFUL_BRACELET")] public sealed class BeautifulBracelet : FlashRelicDef; // AfterObtained

[FlashContent("BLACK_STAR")] public sealed class BlackStar : FlashRelicDef; // TryModifyRewards

[FlashContent("CALLING_BELL")] public sealed class CallingBell : FlashRelicDef; // AfterObtained

[FlashContent("CLAWS")] public sealed class Claws : FlashRelicDef; // AfterObtained

[FlashContent("CURSED_PEARL")] public sealed class CursedPearl : FlashRelicDef; // AfterObtained

[FlashContent("DISTINGUISHED_CAPE")] public sealed class DistinguishedCape : FlashRelicDef; // AfterObtained

[FlashContent("DOWSING_ROD")] public sealed class DowsingRod : FlashRelicDef; // AfterObtained / TryModifyRewards

[FlashContent("DRIFTWOOD")] public sealed class Driftwood : FlashRelicDef; // TryModifyRewardsLate

[FlashContent("DUSTY_TOME")] public sealed class DustyTome : FlashRelicDef; // AfterObtained

[FlashContent("ELECTRIC_SHRYMP")] public sealed class ElectricShrymp : FlashRelicDef; // AfterObtained

[FlashContent("EMPTY_CAGE")] public sealed class EmptyCage : FlashRelicDef; // AfterObtained

[FlashContent("FISHING_ROD")] public sealed class FishingRod : FlashRelicDef; // AfterCombatEnd

[FlashContent("GLASS_EYE")] public sealed class GlassEye : FlashRelicDef; // AfterObtained

[FlashContent("GLITTER")] public sealed class Glitter : FlashRelicDef; // TryModifyCardRewardOptionsLate

[FlashContent("GOLDEN_COMPASS")] public sealed class GoldenCompass : FlashRelicDef; // AfterObtained / map

[FlashContent("GOLDEN_PEARL")] public sealed class GoldenPearl : FlashRelicDef; // AfterObtained

[FlashContent("HEFTY_TABLET")] public sealed class HeftyTablet : FlashRelicDef; // AfterObtained

[FlashContent("JEWELRY_BOX")] public sealed class JewelryBox : FlashRelicDef; // AfterObtained

[FlashContent("KALEIDOSCOPE")] public sealed class Kaleidoscope : FlashRelicDef; // AfterObtained / IsAllowedAtNeow

[FlashContent("LARGE_CAPSULE")] public sealed class LargeCapsule : FlashRelicDef; // AfterObtained

[FlashContent("LAVA_ROCK")] public sealed class LavaRock : FlashRelicDef; // TryModifyRewards

[FlashContent("LEAD_PAPERWEIGHT")] public sealed class LeadPaperweight : FlashRelicDef; // AfterObtained

[FlashContent("LEAFY_POULTICE")] public sealed class LeafyPoultice : FlashRelicDef; // AfterObtained

[FlashContent("LORDS_PARASOL")] public sealed class LordsParasol : FlashRelicDef; // AfterRoomEntered (merchant)

[FlashContent("LOST_COFFER")] public sealed class LostCoffer : FlashRelicDef; // AfterObtained

[FlashContent("MASSIVE_SCROLL")] public sealed class MassiveScroll : FlashRelicDef; // IsAllowed / AfterObtained

[FlashContent("MEAT_CLEAVER")] public sealed class MeatCleaver : FlashRelicDef; // TryModifyRestSiteOptions

[FlashContent("NEOWS_BONES")] public sealed class NeowsBones : FlashRelicDef; // AfterObtained

[FlashContent("NEOWS_SACRIFICE")] public sealed class NeowsSacrifice : FlashRelicDef; // AfterObtained

[FlashContent("NEOWS_TALISMAN")] public sealed class NeowsTalisman : FlashRelicDef; // AfterObtained

[FlashContent("NEOWS_TORMENT")] public sealed class NeowsTorment : FlashRelicDef; // AfterObtained

[FlashContent("NEW_LEAF")] public sealed class NewLeaf : FlashRelicDef; // AfterObtained

[FlashContent("NUTRITIOUS_OYSTER")] public sealed class NutritiousOyster : FlashRelicDef; // AfterObtained

[FlashContent("NUTRITIOUS_SOUP")] public sealed class NutritiousSoup : FlashRelicDef; // AfterObtained

[FlashContent("PAELS_CLAW")] public sealed class PaelsClaw : FlashRelicDef; // AfterObtained (deck enchantment)

[FlashContent("PAELS_GROWTH")] public sealed class PaelsGrowth : FlashRelicDef; // AfterObtained / rest site

[FlashContent("PAELS_HORN")] public sealed class PaelsHorn : FlashRelicDef; // AfterObtained

[FlashContent("PAELS_TOOTH")] public sealed class PaelsTooth : FlashRelicDef; // rewards / AfterObtained

[FlashContent("PAELS_WING")] public sealed class PaelsWing : FlashRelicDef; // TryModifyCardRewardAlternatives

[FlashContent("PANDORAS_BOX")] public sealed class PandorasBox : FlashRelicDef; // AfterObtained

[FlashContent("PHIAL_HOLSTER")] public sealed class PhialHolster : FlashRelicDef; // AfterObtained

[FlashContent("POMANDER")] public sealed class Pomander : FlashRelicDef; // AfterObtained

[FlashContent("PRECARIOUS_SHEARS")] public sealed class PrecariousShears : FlashRelicDef; // AfterObtained

[FlashContent("PRECISE_SCISSORS")] public sealed class PreciseScissors : FlashRelicDef; // AfterObtained

[FlashContent("PRESERVED_FOG")] public sealed class PreservedFog : FlashRelicDef; // AfterObtained

[FlashContent("SAND_CASTLE")] public sealed class SandCastle : FlashRelicDef; // AfterObtained

[FlashContent("SCROLL_BOXES")] public sealed class ScrollBoxes : FlashRelicDef; // AfterObtained / IsAllowedAtNeow

[FlashContent("SEA_GLASS")] public sealed class SeaGlass : FlashRelicDef; // AfterObtained

[FlashContent("SERE_TALON")] public sealed class SereTalon : FlashRelicDef; // AfterObtained

[FlashContent("SIGNET_RING")] public sealed class SignetRing : FlashRelicDef; // AfterObtained

[FlashContent("SILKEN_TRESS")] public sealed class SilkenTress : FlashRelicDef; // TryModifyCardRewardOptionsLate

[FlashContent("SILVER_CRUCIBLE")] public sealed class SilverCrucible : FlashRelicDef; // rewards / IsAllowed

[FlashContent("SMALL_CAPSULE")] public sealed class SmallCapsule : FlashRelicDef; // AfterObtained

[FlashContent("STONE_HUMIDIFIER")] public sealed class StoneHumidifier : FlashRelicDef; // AfterRestSiteHeal

[FlashContent("STORYBOOK")] public sealed class Storybook : FlashRelicDef; // AfterObtained

[FlashContent("TANXS_WHISTLE")] public sealed class TanxsWhistle : FlashRelicDef; // AfterObtained

[FlashContent("TOUCH_OF_OROBAS")] public sealed class TouchOfOrobas : FlashRelicDef; // AfterCloned / AfterObtained

[FlashContent("TOY_BOX")] public sealed class ToyBox : FlashRelicDef; // AfterObtained / AfterCombatEnd

[FlashContent("TRI_BOOMERANG")] public sealed class TriBoomerang : FlashRelicDef; // AfterObtained

[FlashContent("WAR_HAMMER")] public sealed class WarHammer : FlashRelicDef; // AfterCombatVictory

[FlashContent("WINGED_BOOTS")] public sealed class WingedBoots : FlashRelicDef; // map / IsAllowed

[FlashContent("YUMMY_COOKIE")] public sealed class YummyCookie : FlashRelicDef; // AfterObtained

[FlashContent("BOOMING_CONCH")] public sealed class BoomingConch : FlashRelicDef; // turn 1 elite draw / energy

[FlashContent("JEWELED_MASK")] public sealed class JeweledMask : FlashRelicDef; // turn 1 BeforeHandDraw
