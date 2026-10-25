using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Relics;

/// <summary>
/// <c>IronClub</c>: <c>AfterCardPlayed</c> counts cards toward <c>Cards</c>, then draws 1 when the
/// counter wraps (<c>AfterCardPlayedMirrors.HandleIronClub</c>). Counter = cards played toward the
/// cycle; the bridge projects <c>CounterPredictionState.Value</c>, falling back to
/// <c>IronClub.CardsPlayed</c>.
/// </summary>
[BlitzContent("IRON_CLUB")]
public sealed class IronClub : BlitzRelicDef
{
    // IronClub.cs: CardsVar(4).
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter++;
        if (!BlitzKernel.IsInProgress(x.B) || relic.Counter % (int)x.R.RelicVar(Id, 0) != 0)
            return;
        BlitzKernel.Draw(ref x, 1, fromHandDraw: false);
    }
}

/// <summary>
/// <c>Sai</c>: <c>AfterSideTurnStart</c> gains its unpowered block on every player side
/// (<c>SimulatedCombatState.RelicTurnStart.TriggerRelicsAfterSideTurnStart</c>).
/// </summary>
[BlitzContent("SAI")]
public sealed class Sai : BlitzRelicDef
{
    // Sai.cs: BlockVar(7m, ValueProp.Unpowered).
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player)
            return;
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.R.RelicVar(Id, 0),
            BlitzValueProps.Unpowered, fromCard: false);
    }
}

/// <summary>
/// <c>VelvetChoker</c>: <c>ModifyMaxEnergy</c> adds Energy; <c>ShouldPlay</c> forbids playing once
/// Cards cards were played this turn; <c>AfterCardPlayed</c> counts them and
/// <c>BeforeSideTurnStart</c> resets (<c>ShouldPlayMirrors.HandleVelvetChoker</c>,
/// <c>AfterCardPlayedMirrors.HandleVelvetChoker</c>). Counter = cards played this turn, projected from
/// <c>CounterPredictionState.Value</c> with the live field as fallback.
/// </summary>
[BlitzContent("VELVET_CHOKER")]
public sealed class VelvetChoker : BlitzRelicDef
{
    // VelvetChoker.cs: CardsVar(6), EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Cards", "Energy"];

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 1);

    public override bool ShouldPlay(in BlitzBattle battle, BlitzRules rules, BlitzListener self, in BlitzCard card)
        => battle.Relics[self.Index].Counter < (int)rules.RelicVar(Id, 0);

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
        => x.B.Relics[self.Index].Counter++;

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
        => x.B.Relics[self.Index].Counter = 0;
}

/// <summary>
/// <c>Fiddle</c>: <c>ModifyHandDraw</c> adds Cards; <c>ShouldDraw</c> refuses a non-hand draw while the
/// player side is current (<c>ShouldDrawMirrors.HandleFiddle</c>). The native
/// <c>AfterPreventingDraw</c> only flashes.
/// </summary>
[BlitzContent("FIDDLE")]
public sealed class Fiddle : BlitzRelicDef
{
    // Fiddle.cs: CardsVar(2).
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);

    public override bool ShouldDraw(ref BlitzExec x, BlitzListener self, bool fromHandDraw)
        => fromHandDraw || x.B.Side != BlitzSide.Player;
}

/// <summary>
/// <c>PaelsBlood</c>: <c>ModifyHandDraw</c> adds Cards on every player turn
/// (<c>PersistentPowerSupport</c> native relic contribution).
/// </summary>
[BlitzContent("PAELS_BLOOD")]
public sealed class PaelsBlood : BlitzRelicDef
{
    // PaelsBlood.cs: CardsVar(1).
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>PaelsFlesh</c>: <c>ModifyMaxEnergy</c> adds Energy from player turn 3
/// (<c>PersistentPowerSupport.GetModifiedMaxEnergy</c> native relic contribution).
/// </summary>
[BlitzContent("PAELS_FLESH")]
public sealed class PaelsFlesh : BlitzRelicDef
{
    // PaelsFlesh.cs: EnergyVar(1); the turn threshold 3 is a code constant.
    private const int EnergyFromTurn = 3;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => x.B.PlayerTurn < EnergyFromTurn ? amount : amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>PaelsTears</c>: <c>BeforeSideTurnEnd</c> records leftover energy and <c>AfterSideTurnStart</c>
/// gains Energy while the flag is set (<c>SimulatedCombatState.RelicTurnStart</c>).
/// Counter = HadLeftoverEnergy (<c>StatefulRelicState.Current</c>).
/// </summary>
[BlitzContent("PAELS_TEARS")]
public sealed class PaelsTears : BlitzRelicDef
{
    // PaelsTears.cs: EnergyVar(2).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void BeforeSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        if (!BlitzKernel.Participates(participants, self.Owner))
            return;
        x.B.Relics[self.Index].Counter = x.B.Energy > 0 ? 1 : 0;
    }

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player || x.B.Relics[self.Index].Counter == 0)
            return;
        BlitzKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
    }
}

/// <summary>
/// <c>BlessedAntler</c>: <c>ModifyMaxEnergy</c> adds Energy. The native turn-1 Dazed generation runs at
/// the turn start before the play-phase root and is imported; later turns do not satisfy its condition.
/// </summary>
[BlitzContent("BLESSED_ANTLER")]
public sealed class BlessedAntler : BlitzRelicDef
{
    // BlessedAntler.cs: EnergyVar(1), CardsVar(3).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>BloodSoakedRose</c>: <c>ModifyMaxEnergy</c> adds Energy. The Enthralled curse is added to the deck
/// when the relic is obtained, outside combat.
/// </summary>
[BlitzContent("BLOOD_SOAKED_ROSE")]
public sealed class BloodSoakedRose : BlitzRelicDef
{
    // BloodSoakedRose.cs: EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>Ectoplasm</c>: <c>ModifyMaxEnergy</c> adds Energy; the gold modifier only runs outside combat.
/// </summary>
[BlitzContent("ECTOPLASM")]
public sealed class Ectoplasm : BlitzRelicDef
{
    // Ectoplasm.cs: EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>PhilosophersStone</c>: <c>ModifyMaxEnergy</c> adds Energy. The enemy Strength from
/// <c>AfterRoomEntered</c> is applied before the root and imported.
/// </summary>
[BlitzContent("PHILOSOPHERS_STONE")]
public sealed class PhilosophersStone : BlitzRelicDef
{
    // PhilosophersStone.cs: PowerVar&lt;StrengthPower&gt;(1m), EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>PrismaticGem</c>: <c>ModifyMaxEnergy</c> adds Energy; the card-reward pool modifier is out of combat.
/// </summary>
[BlitzContent("PRISMATIC_GEM")]
public sealed class PrismaticGem : BlitzRelicDef
{
    // PrismaticGem.cs: EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>Sozu</c>: <c>ModifyMaxEnergy</c> adds Energy; the potion-procurement refusal is out of combat.
/// </summary>
[BlitzContent("SOZU")]
public sealed class Sozu : BlitzRelicDef
{
    // Sozu.cs: EnergyVar(1).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>PumpkinCandle</c>: <c>ModifyMaxEnergy</c> adds Energy while KindleCount is positive. Counter =
/// KindleCount, projected from the live model field (the value does not change during combat).
/// </summary>
[BlitzContent("PUMPKIN_CANDLE")]
public sealed class PumpkinCandle : BlitzRelicDef
{
    // PumpkinCandle.cs: EnergyVar(1), DynamicVar("CombatCount", 5m).
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => x.B.Relics[self.Index].Counter > 0 ? amount + x.R.RelicVar(Id, 0) : amount;
}

/// <summary>
/// <c>RunicPyramid</c>: <c>ShouldFlush</c> declines the owner's hand flush on every turn
/// (<c>PersistentRelicSupport.ShouldFlush</c>).
/// </summary>
[BlitzContent("RUNIC_PYRAMID")]
public sealed class RunicPyramid : BlitzRelicDef
{
    public override bool ShouldFlush(ref BlitzExec x, BlitzListener self) => false;
}

/// <summary>
/// <c>VeryHotCocoa</c>: <c>AfterSideTurnStart</c> gains 4 energy only when the player turn number is
/// &lt;= 1 (<c>TriggerRelicsAfterSideTurnStart</c>). The player turn 1 start is before the root.
/// </summary>
[BlitzContent("VERY_HOT_COCOA")]
public sealed class VeryHotCocoa : BlitzRelicDef;

/// <summary><c>LoomingFruit</c>: <c>AfterObtained</c> max-HP gain only.</summary>
[BlitzContent("LOOMING_FRUIT")]
public sealed class LoomingFruit : BlitzRelicDef;

/// <summary>
/// <c>BiiigHug</c>: after a shuffle by its owner, create one Soot into the draw pile at a random
/// position (<c>AfterShuffleMirrors.HandleBiiigHug</c>). <c>AfterObtained</c> is a deck-only effect.
/// </summary>
[BlitzContent("BIIIG_HUG")]
public sealed class BiiigHug : BlitzRelicDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOOT", 0)];

    public override void AfterShuffle(ref BlitzExec x, BlitzListener self, byte shuffler)
    {
        if (shuffler != BlitzLimits.PlayerOwner)
            return;
        BlitzCard soot = BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Soot>());
        BlitzKernel.AddGeneratedCard(ref x, soot, BlitzPile.Draw, BlitzPilePosition.Random);
    }
}

/// <summary>
/// <c>ChoicesParadox</c>: on the first player turn start, generate <c>Cards</c> distinct character
/// cards, add Retain to each, and add the one chosen from the generated options to hand
/// (<c>SimulatedCombatState.RelicTurnStart</c> case ChoicesParadox / <c>TurnStartChoiceSupport.ResolveGeneratedToHand</c>).
/// </summary>
[BlitzContent("CHOICES_PARADOX")]
public sealed class ChoicesParadox : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAll];

    public override void AfterPlayerTurnStart(ref BlitzExec x, BlitzListener self)
    {
        if (x.B.PlayerTurn > 1)
            return;
        int count = (int)x.R.RelicVar(Id, 0);
        if (count <= 0)
            return;
        if (count > BlitzLimits.Cards)
            throw new BlitzCapacityException("GeneratedCards", BlitzLimits.Cards);
        Span<BlitzCard> options = stackalloc BlitzCard[count];
        int sampled = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.CharacterAll, count,
            distinct: true, BlitzRngStream.CombatCardGeneration, options);
        if (sampled == 0)
            return;
        for (int index = 0; index < sampled; index++)
            options[index].Keywords |= BlitzKeywords.Retain;
        if (!BlitzKernel.ChooseOption(ref x, BlitzChoiceEffect.GenerateToHand, options[..sampled], out int selected))
            return;
        BlitzKernel.AddGeneratedCard(ref x, options[selected], BlitzPile.Hand, BlitzPilePosition.Bottom);
    }
}

/// <summary>
/// <c>Crossbow</c>: at every player side turn start, generate one distinct character Attack, make it
/// free this turn and add it to hand (<c>SimulatedCombatState.RelicTurnStart</c> case Crossbow).
/// </summary>
[BlitzContent("CROSSBOW")]
public sealed class Crossbow : BlitzRelicDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAttacks];

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player)
            return;
        Span<BlitzCard> selected = stackalloc BlitzCard[1];
        int sampled = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.CharacterAttacks, 1,
            distinct: true, BlitzRngStream.CombatCardGeneration, selected);
        if (sampled == 0)
            return;
        BlitzCard card = selected[0];
        BlitzKernel.MakeFreeThisTurn(x.R, ref card);
        BlitzKernel.AddGeneratedCard(ref x, card, BlitzPile.Hand, BlitzPilePosition.Bottom);
    }
}

/// <summary>
/// <c>FurCoat</c>: enemies created at a marked map coordinate start at 1 current HP. The marked
/// coordinate is out-of-combat data frozen into <see cref="BlitzRules.FurCoatSpawnHpOne"/>
/// (<c>MonsterSpawnSupport.ApplyCreatureAddedRelics</c>); the relic itself has no combat hook.
/// </summary>
[BlitzContent("FUR_COAT")]
public sealed class FurCoat : BlitzRelicDef
{
}

/// <summary>
/// <c>MusicBox</c>: the first Attack played each turn is cloned with Ethereal and added to hand once
/// (<c>BeforeCardPlayedMirrors.HandleMusicBox</c> + <c>AfterCardPlayedMirrors.HandleMusicBox</c>;
/// the per-turn latch is <c>MusicBoxPredictionState.WasUsedThisTurn</c>).
/// </summary>
[BlitzContent("MUSIC_BOX")]
public sealed class MusicBox : BlitzRelicDef
{
    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (relic.Counter != 0 || x.MusicBoxPlayIdentity != 0)
            return;
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != BlitzCardType.Attack)
            return;
        x.MusicBoxPlayIdentity = x.PlayIdentity;
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (x.MusicBoxPlayIdentity == 0 || x.MusicBoxPlayIdentity != x.LastPlayIdentity)
            return;
        x.MusicBoxPlayIdentity = 0;
        BlitzCard clone = BlitzKernel.CloneCard(play.Card);
        clone.Keywords |= BlitzKeywords.Ethereal;
        if (!BlitzKernel.AddGeneratedCard(ref x, clone, BlitzPile.Hand, BlitzPilePosition.Bottom))
            return;
        if (x.NeedsChoice)
            return;
        relic.Counter = 1;
    }

    public override void BeforeSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side == BlitzSide.Player)
            x.B.Relics[self.Index].Counter = 0;
    }
}

/// <summary>
/// <c>PaelsEye</c>: at the end of a player turn with no manually played card, exhaust the hand and
/// take an extra turn once per combat. <c>BeforeSideTurnEndEarly</c> exhausts, <c>ShouldTakeExtraTurn</c>
/// grants the turn and <c>AfterTakingExtraTurn</c> sets the used latch
/// (<c>SimulatedCombatState.ReactiveRelics.cs</c> + <c>ExtraTurnMirrors</c>).
/// </summary>
[BlitzContent("PAELS_EYE")]
public sealed class PaelsEye : BlitzRelicDef
{
    public override void AfterPlayerTurnStart(ref BlitzExec x, BlitzListener self)
    {
        // TriggerRelicsAfterPlayerTurnStart case PaelsEye: state.Previous = 1.
        x.B.Relics[self.Index].Aux = 1;
    }

    public override void BeforeSideTurnEndEarly(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        if (side != BlitzSide.Player
            || !BlitzKernel.Participates(participants, BlitzLimits.PlayerOwner)
            || !ShouldTrigger(x.B, x.B.Relics[self.Index]))
            return;
        int handCount = x.B.HandCount;
        Span<int> selected = stackalloc int[handCount];
        for (int index = 0; index < handCount; index++)
            selected[index] = index;
        BlitzKernel.ExhaustSelected(ref x, BlitzPile.Hand, selected);
    }

    public override bool ShouldTakeExtraTurn(ref BlitzExec x, BlitzListener self)
        => ShouldTrigger(x.B, x.B.Relics[self.Index]);

    public override void AfterTakingExtraTurn(ref BlitzExec x, BlitzListener self)
    {
        x.B.Relics[self.Index].Counter = 1;
    }

    private bool ShouldTrigger(in BlitzBattle battle, in BlitzRelic relic)
    {
        if (relic.Counter != 0 || relic.Aux == 0)
            return false;
        if (battle.Counter(BlitzCounter.ManualCardsPlayedThisTurn) != 0)
            return false;
        if (battle.PlayerTurn == 1 && HasRelic(battle, "WHISPERING_EARRING"))
            return false;
        return true;
    }

    private static bool HasRelic(in BlitzBattle battle, string nativeId)
    {
        if (!BlitzRegistry.TryRelic(nativeId, out ushort id))
            return false;
        for (int index = 0; index < battle.RelicCount; index++)
        {
            if (battle.Relics[index].Id == id && (battle.Relics[index].Flags & BlitzRelicFlags.Melted) == 0)
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
[BlitzContent("PAELS_LEGION")]
public sealed class PaelsLegion : BlitzRelicDef
{
    public override bool IsPaelsLegion => true;

    public override IReadOnlyList<string> VarNames => ["Turns"];

    public override decimal ModifyBlockMultiplicative(ref BlitzExec x, BlitzListener self, in BlitzBlockContext block,
        decimal amount)
    {
        if ((block.Props & BlitzValueProps.Move) == 0 || !block.HasCardSource)
            return 1m;
        return x.B.Relics[self.Index].Counter > 0 ? 1m : 2m;
    }

    public override void AfterModifyingBlockAmount(ref BlitzExec x, BlitzListener self, decimal modified,
        in BlitzBlockContext block)
    {
        if (modified <= 0m)
            return;
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (relic.Aux != 0 && relic.Aux != x.PlayIdentity)
            return;
        relic.Aux = x.PlayIdentity;
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        if (relic.Aux == 0 || relic.Aux != x.LastPlayIdentity)
            return;
        relic.Aux = 0;
        relic.Counter = (int)x.R.RelicVar(Id, 0);
        relic.Flags |= BlitzRelicFlags.UsedThisCombat;
    }

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        // RelicPredictionStateSupport.ResetAfterSideTurnStart case PaelsLegion runs before the case
        // switch: cooldown-- and the display latch clears, whatever the current cooldown sign is.
        ref BlitzRelic relic = ref x.B.Relics[self.Index];
        relic.Counter--;
        relic.Flags &= ~BlitzRelicFlags.UsedThisCombat;
    }
}

/// <summary>
/// <c>SpikedGauntlets</c>: +Energy maximum energy, and Power cards cost 1 more
/// (<c>ModifyMaxEnergy</c> and the verified native <c>TryModifyEnergyCostInCombat</c>).
/// </summary>
[BlitzContent("SPIKED_GAUNTLETS")]
public sealed class SpikedGauntlets : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);

    public override decimal TryModifyEnergyCostInCombat(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzCard card, BlitzPile pile, decimal amount)
        => rules.Card(card.Id, card.Upgrade).Type == BlitzCardType.Power ? amount + 1m : amount;
}

/// <summary>
/// <c>ThrowingAxe</c>: the first card played each combat is played one extra time
/// (<c>ModifyCardPlayCountMirrors.HandleThrowingAxe</c> / <c>HandleThrowingAxeAfter</c>; the latch is
/// <c>ThrowingAxePredictionState.UsedThisCombat</c>, reset out of combat by <c>AfterRoomEntered</c>).
/// </summary>
[BlitzContent("THROWING_AXE")]
public sealed class ThrowingAxe : BlitzRelicDef
{
    public override int ModifyCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card, byte target,
        int playCount)
        => x.B.Relics[self.Index].Counter == 0 ? playCount + 1 : playCount;

    public override void AfterModifyingCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card)
    {
        x.B.Relics[self.Index].Counter = 1;
    }
}

/// <summary>
/// <c>ToastyMittens</c>: at the player turn start exhaust one card from hand, then apply its Strength
/// variable (<c>SimulatedCombatState.RelicTurnStart</c> case ToastyMittens).
/// </summary>
[BlitzContent("TOASTY_MITTENS")]
public sealed class ToastyMittens : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void AfterPlayerTurnStart(ref BlitzExec x, BlitzListener self)
    {
        if (x.B.HandCount > 0)
        {
            Span<int> options = stackalloc int[x.B.HandCount];
            for (int index = 0; index < options.Length; index++)
                options[index] = index;
            Span<int> selected = stackalloc int[1];
            if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Exhaust, BlitzPile.Hand, options, 1, selected,
                    out int chosen))
            {
                return;
            }
            BlitzKernel.StartPlayerSideTurnIfNeeded(ref x);
            if (x.NeedsChoice)
                return;
            BlitzKernel.ExhaustSelected(ref x, BlitzPile.Hand, selected[..chosen]);
            if (x.NeedsChoice)
                return;
        }
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.StrengthPower>(), (int)x.R.RelicVar(Id, 0), BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>SealOfGold</c>: at the player side turn start, if the owner has at least its Gold variable, gain
/// Energy and lose that gold (<c>SimulatedCombatState.RelicTurnStart</c> case SealOfGold).
/// </summary>
[BlitzContent("SEAL_OF_GOLD")]
public sealed class SealOfGold : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Gold"];

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player)
            return;
        int gold = (int)x.R.RelicVar(Id, 1);
        if (x.B.Gold < gold)
            return;
        BlitzKernel.GainEnergy(ref x, x.R.RelicVar(Id, 0));
        if (x.NeedsChoice)
            return;
        x.B.Gold = Math.Max(0, x.B.Gold - gold);
    }
}

/// <summary>
/// <c>SneckoEye</c>: adds its Cards variable to the player's hand draw; ConfusedPower is applied before
/// combat and is already in the root.
/// </summary>
[BlitzContent("SNECKO_EYE")]
public sealed class SneckoEye : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
    {
        return amount + x.R.RelicVar(Id, 0);
    }
}

/// <summary>
/// <c>WhisperingEarring</c>: +Energy maximum energy. Its first-turn auto-play
/// (<c>AfterAutoPrePlayPhaseEnteredLate</c>) runs before the captured root, like Imbued; the relic's
/// presence is also read by PaelsEye's first-turn gate.
/// </summary>
[BlitzContent("WHISPERING_EARRING")]
public sealed class WhisperingEarring : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override decimal ModifyMaxEnergy(ref BlitzExec x, BlitzListener self, decimal amount)
        => amount + x.R.RelicVar(Id, 0);
}

/// <summary>
/// <c>DelicateFrond</c>: fills the open potion slots at <c>BeforeCombatStart</c>; the potions are in
/// the captured root, so there is no in-combat hook.
/// </summary>
[BlitzContent("DELICATE_FROND")]
public sealed class DelicateFrond : BlitzRelicDef
{
}

/// <summary>
/// <c>DiamondDiadem</c>: gain 20 Unpowered block and 1 Blur at the first side turn start
/// (<c>AfterSideTurnStart</c> with <c>TurnNumber &lt;= 1</c>), before the captured root.
/// </summary>
[BlitzContent("DIAMOND_DIADEM")]
public sealed class DiamondDiadem : BlitzRelicDef
{
}

/// <summary>
/// <c>RadiantPearl</c>: adds its Cards Luminesce cards to hand at the first <c>BeforeHandDraw</c>,
/// before the captured root; the cards are already in the root hand and have their own definition.
/// </summary>
[BlitzContent("RADIANT_PEARL")]
public sealed class RadiantPearl : BlitzRelicDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("LUMINESCE", 0), ("LUMINESCE", 1)];
}

// --- rewards, shops, rest sites, map and pickup effects --------------------------------
[BlitzContent("ALCHEMICAL_COFFER")] public sealed class AlchemicalCoffer : BlitzRelicDef; // AfterObtained / TryModifyRewards

[BlitzContent("ARCANE_SCROLL")] public sealed class ArcaneScroll : BlitzRelicDef; // AfterObtained

[BlitzContent("ARCHAIC_TOOTH")] public sealed class ArchaicTooth : BlitzRelicDef; // AfterCloned / AfterObtained

[BlitzContent("ASTROLABE")] public sealed class Astrolabe : BlitzRelicDef; // AfterObtained

[BlitzContent("BEAUTIFUL_BRACELET")] public sealed class BeautifulBracelet : BlitzRelicDef; // AfterObtained

[BlitzContent("BLACK_STAR")] public sealed class BlackStar : BlitzRelicDef; // TryModifyRewards

[BlitzContent("CALLING_BELL")] public sealed class CallingBell : BlitzRelicDef; // AfterObtained

[BlitzContent("CLAWS")] public sealed class Claws : BlitzRelicDef; // AfterObtained

[BlitzContent("CURSED_PEARL")] public sealed class CursedPearl : BlitzRelicDef; // AfterObtained

[BlitzContent("DISTINGUISHED_CAPE")] public sealed class DistinguishedCape : BlitzRelicDef; // AfterObtained

[BlitzContent("DOWSING_ROD")] public sealed class DowsingRod : BlitzRelicDef; // AfterObtained / TryModifyRewards

[BlitzContent("DRIFTWOOD")] public sealed class Driftwood : BlitzRelicDef; // TryModifyRewardsLate

[BlitzContent("DUSTY_TOME")] public sealed class DustyTome : BlitzRelicDef; // AfterObtained

[BlitzContent("ELECTRIC_SHRYMP")] public sealed class ElectricShrymp : BlitzRelicDef; // AfterObtained

[BlitzContent("EMPTY_CAGE")] public sealed class EmptyCage : BlitzRelicDef; // AfterObtained

[BlitzContent("FISHING_ROD")] public sealed class FishingRod : BlitzRelicDef; // AfterCombatEnd

[BlitzContent("GLASS_EYE")] public sealed class GlassEye : BlitzRelicDef; // AfterObtained

[BlitzContent("GLITTER")] public sealed class Glitter : BlitzRelicDef; // TryModifyCardRewardOptionsLate

[BlitzContent("GOLDEN_COMPASS")] public sealed class GoldenCompass : BlitzRelicDef; // AfterObtained / map

[BlitzContent("GOLDEN_PEARL")] public sealed class GoldenPearl : BlitzRelicDef; // AfterObtained

[BlitzContent("HEFTY_TABLET")] public sealed class HeftyTablet : BlitzRelicDef; // AfterObtained

[BlitzContent("JEWELRY_BOX")] public sealed class JewelryBox : BlitzRelicDef; // AfterObtained

[BlitzContent("KALEIDOSCOPE")] public sealed class Kaleidoscope : BlitzRelicDef; // AfterObtained / IsAllowedAtNeow

[BlitzContent("LARGE_CAPSULE")] public sealed class LargeCapsule : BlitzRelicDef; // AfterObtained

[BlitzContent("LAVA_ROCK")] public sealed class LavaRock : BlitzRelicDef; // TryModifyRewards

[BlitzContent("LEAD_PAPERWEIGHT")] public sealed class LeadPaperweight : BlitzRelicDef; // AfterObtained

[BlitzContent("LEAFY_POULTICE")] public sealed class LeafyPoultice : BlitzRelicDef; // AfterObtained

[BlitzContent("LORDS_PARASOL")] public sealed class LordsParasol : BlitzRelicDef; // AfterRoomEntered (merchant)

[BlitzContent("LOST_COFFER")] public sealed class LostCoffer : BlitzRelicDef; // AfterObtained

[BlitzContent("MASSIVE_SCROLL")] public sealed class MassiveScroll : BlitzRelicDef; // IsAllowed / AfterObtained

[BlitzContent("MEAT_CLEAVER")] public sealed class MeatCleaver : BlitzRelicDef; // TryModifyRestSiteOptions

[BlitzContent("NEOWS_BONES")] public sealed class NeowsBones : BlitzRelicDef; // AfterObtained

[BlitzContent("NEOWS_SACRIFICE")] public sealed class NeowsSacrifice : BlitzRelicDef; // AfterObtained

[BlitzContent("NEOWS_TALISMAN")] public sealed class NeowsTalisman : BlitzRelicDef; // AfterObtained

[BlitzContent("NEOWS_TORMENT")] public sealed class NeowsTorment : BlitzRelicDef; // AfterObtained

[BlitzContent("NEW_LEAF")] public sealed class NewLeaf : BlitzRelicDef; // AfterObtained

[BlitzContent("NUTRITIOUS_OYSTER")] public sealed class NutritiousOyster : BlitzRelicDef; // AfterObtained

[BlitzContent("NUTRITIOUS_SOUP")] public sealed class NutritiousSoup : BlitzRelicDef; // AfterObtained

[BlitzContent("PAELS_CLAW")] public sealed class PaelsClaw : BlitzRelicDef; // AfterObtained (deck enchantment)

[BlitzContent("PAELS_GROWTH")] public sealed class PaelsGrowth : BlitzRelicDef; // AfterObtained / rest site

[BlitzContent("PAELS_HORN")] public sealed class PaelsHorn : BlitzRelicDef; // AfterObtained

[BlitzContent("PAELS_TOOTH")] public sealed class PaelsTooth : BlitzRelicDef; // rewards / AfterObtained

[BlitzContent("PAELS_WING")] public sealed class PaelsWing : BlitzRelicDef; // TryModifyCardRewardAlternatives

[BlitzContent("PANDORAS_BOX")] public sealed class PandorasBox : BlitzRelicDef; // AfterObtained

[BlitzContent("PHIAL_HOLSTER")] public sealed class PhialHolster : BlitzRelicDef; // AfterObtained

[BlitzContent("POMANDER")] public sealed class Pomander : BlitzRelicDef; // AfterObtained

[BlitzContent("PRECARIOUS_SHEARS")] public sealed class PrecariousShears : BlitzRelicDef; // AfterObtained

[BlitzContent("PRECISE_SCISSORS")] public sealed class PreciseScissors : BlitzRelicDef; // AfterObtained

[BlitzContent("PRESERVED_FOG")] public sealed class PreservedFog : BlitzRelicDef; // AfterObtained

[BlitzContent("SAND_CASTLE")] public sealed class SandCastle : BlitzRelicDef; // AfterObtained

[BlitzContent("SCROLL_BOXES")] public sealed class ScrollBoxes : BlitzRelicDef; // AfterObtained / IsAllowedAtNeow

[BlitzContent("SEA_GLASS")] public sealed class SeaGlass : BlitzRelicDef; // AfterObtained

[BlitzContent("SERE_TALON")] public sealed class SereTalon : BlitzRelicDef; // AfterObtained

[BlitzContent("SIGNET_RING")] public sealed class SignetRing : BlitzRelicDef; // AfterObtained

[BlitzContent("SILKEN_TRESS")] public sealed class SilkenTress : BlitzRelicDef; // TryModifyCardRewardOptionsLate

[BlitzContent("SILVER_CRUCIBLE")] public sealed class SilverCrucible : BlitzRelicDef; // rewards / IsAllowed

[BlitzContent("SMALL_CAPSULE")] public sealed class SmallCapsule : BlitzRelicDef; // AfterObtained

[BlitzContent("STONE_HUMIDIFIER")] public sealed class StoneHumidifier : BlitzRelicDef; // AfterRestSiteHeal

[BlitzContent("STORYBOOK")] public sealed class Storybook : BlitzRelicDef; // AfterObtained

[BlitzContent("TANXS_WHISTLE")] public sealed class TanxsWhistle : BlitzRelicDef; // AfterObtained

[BlitzContent("TOUCH_OF_OROBAS")] public sealed class TouchOfOrobas : BlitzRelicDef; // AfterCloned / AfterObtained

[BlitzContent("TOY_BOX")] public sealed class ToyBox : BlitzRelicDef; // AfterObtained / AfterCombatEnd

[BlitzContent("TRI_BOOMERANG")] public sealed class TriBoomerang : BlitzRelicDef; // AfterObtained

[BlitzContent("WAR_HAMMER")] public sealed class WarHammer : BlitzRelicDef; // AfterCombatVictory

[BlitzContent("WINGED_BOOTS")] public sealed class WingedBoots : BlitzRelicDef; // map / IsAllowed

[BlitzContent("YUMMY_COOKIE")] public sealed class YummyCookie : BlitzRelicDef; // AfterObtained

[BlitzContent("BOOMING_CONCH")] public sealed class BoomingConch : BlitzRelicDef; // turn 1 elite draw / energy

[BlitzContent("JEWELED_MASK")] public sealed class JeweledMask : BlitzRelicDef; // turn 1 BeforeHandDraw
