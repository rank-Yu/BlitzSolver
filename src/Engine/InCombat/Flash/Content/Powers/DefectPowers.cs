namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Defect powers. Existing engine locations:
//   FocusPower              ModifyOrbValue (focused orbs) and the fixed Focus consumers (BiasedCognition).
//   FocusedStrike/Hotfix/   TemporaryFocusPower: ApplyTemporaryFocus* and EndTurnPowerSupport.TriggerRegular's
//   Synchronize/Hyperbeam   TemporaryFocusPower retirement (RetireTemporaryStat).
//   Buffer                  ModifyHpLostMirrors/AfterModifyingHpLostAfterOstyMirrors.
//   Iteration               AfterCardDrawnMirrors.HandleIterationPower.
//   MachineLearning         ModifyHandDraw in PersistentPowerSupport.GetModifiedHandDraw.
//   Loop                    AfterPlayerTurnStartMirrors.Vanilla (OrbCmd.Passive on the first orb).
//   CreativeAi              TurnStartPowerSupport.ContinueBeforeHandDraw (CharacterCombatGenerationPool.Powers).
//   Spinner/LightningRod    AfterEnergyResetMirrors.
//   Hailstorm               BeforeSideTurnEndMirrors.HandleHailstormPower.
//   Storm/Subroutine        BeforeCardPlayed/AfterCardPlayed mirrors with their card-play pairs.
//   EchoForm                ModifyCardPlayCountMirrors.HandleEchoFormPower.
//   SignalBoost             ModifyCardPlayCountMirrors (extra Power play, then decrement).
//   Feral                   PersistentPowerSupport.TriggerOwnerAfterSideTurnStart (reset) and
//                           ModifyCardPlayResultLocationMirrors (zero-cost attacks to hand top).
//   ConsumingShadow         EndTurnPowerSupport.TriggerRegular's EvokeLastOrbs.
//   Coolant                 PersistentPowerSupport.TriggerOwnerAfterSideTurnStart (distinct orbs × amount).
//   BiasedCognition         PersistentPowerSupport.TriggerOwnerAfterSideTurnStart (-Focus).
//   ReptileTrinketPower     SimulatedCombatState.TriggerRelicsAfterPotionUsed's temporary Strength.

[FlashContent("FOCUS_POWER")]
public sealed class FocusPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool AllowNegative => true;

    public override decimal ModifyOrbValue(in FlashBattle battle, FlashRules rules, FlashListener self, in FlashOrb orb,
        decimal value)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return value;
        return Math.Max(value + battle.Powers[self.Index].Amount, 0m);
    }
}

public abstract class TemporaryFocusDef : FlashPowerDef
{
    public override FlashPowerType Type => IsPositive ? FlashPowerType.Buff : FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    protected virtual bool IsPositive => true;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.RetireTemporaryFocus(ref x, self);
    }
}

[FlashContent("FOCUSED_STRIKE_POWER")]
public sealed class FocusedStrikePower : TemporaryFocusDef;

[FlashContent("HOTFIX_POWER")]
public sealed class HotfixPower : TemporaryFocusDef;

[FlashContent("SYNCHRONIZE_POWER")]
public sealed class SynchronizePower : TemporaryFocusDef;

[FlashContent("HYPERBEAM_FOCUS_DOWN_POWER")]
public sealed class HyperbeamFocusDownPower : TemporaryFocusDef
{
    protected override bool IsPositive => false;
}

[FlashContent("ITERATION_POWER")]
public sealed class IterationPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        if ((uint)drawnPosition >= (uint)x.B.CardCount)
            throw new FlashUnsupportedException("抽牌触发时抽到的牌已不在牌堆。");
        FlashCard card = x.B.Cards[drawnPosition];
        if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Status)
            return;
        if (x.B.Counter(FlashCounter.StatusCardsDrawnThisTurn) > 1)
            return;
        FlashKernel.Draw(ref x, x.B.Powers[self.Index].Amount, fromHandDraw: false);
    }
}

[FlashContent("MACHINE_LEARNING_POWER")]
public sealed class MachineLearningPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
        => self.Owner == FlashLimits.PlayerOwner ? amount + x.B.Powers[self.Index].Amount : amount;
}

[FlashContent("LOOP_POWER")]
public sealed class LoopPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterPlayerTurnStart(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner || x.B.OrbCount == 0)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        for (int index = 0; index < amount; index++)
        {
            FlashKernel.OrbPassiveAt(ref x, 0);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("CREATIVE_AI_POWER")]
public sealed class CreativeAiPower : FlashPowerDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterPowers];

    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeHandDraw(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        Span<FlashCard> generated = stackalloc FlashCard[FlashLimits.MaxHandSize];
        int count = 0;
        for (int index = 0; index < amount; index++)
        {
            // CardFactory.GetDistinctForCombat(..., 1, CombatCardGeneration) once per stack.
            if (count >= generated.Length)
                throw new FlashCapacityException("CreativeAi.Generated", generated.Length);
            if (FlashCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, FlashCardType.Power, 1,
                    generated[count..], FlashCardPools.CharacterPowers) == 0)
            {
                break;
            }
            count++;
        }
        // TurnStartPowerSupport queues all generated cards first, then adds them one by one.
        for (int index = 0; index < count; index++)
        {
            if (!FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("SPINNER_POWER")]
public sealed class SpinnerPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterEnergyReset(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("GLASS_ORB"), x.B.Powers[self.Index].Amount);
    }
}

[FlashContent("LIGHTNING_ROD_POWER")]
public sealed class LightningRodPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterEnergyReset(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("LIGHTNING_ORB"));
        if (x.NeedsChoice)
            return;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, amount - 1);
    }
}

[FlashContent("FREE_POWER_POWER")]
public sealed class FreePowerPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombatLate(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount)
    {
        if (self.Owner != FlashLimits.PlayerOwner
            || FlashRegistry.Card(card.Id).CardType(battle, rules, card) != FlashCardType.Power)
            return amount;
        return pile is FlashPile.Hand or FlashPile.Play ? 0m : amount;
    }

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Power)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, amount - 1);
    }
}

[FlashContent("HAILSTORM_POWER")]
public sealed class HailstormPower : FlashPowerDef
{
    public override IReadOnlyList<string> VarNames => ["FrostOrbs"];

    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeSideTurnEnd(ref FlashExec x, FlashListener self, FlashSide side, ushort participants)
    {
        if (!FlashKernel.Participates(participants, self.Owner))
            return;
        int frost = FlashKernel.CountOrbs(x.B, "FROST_ORB");
        if (frost < (int)x.R.PowerVar(x.B.Powers[self.Index].Id, 0))
            return;
        FlashCardEffects.DamageAllEnemies(ref x, x.B.Powers[self.Index].Amount, FlashValueProps.Unpowered,
            self.Owner);
    }
}

[FlashContent("STORM_POWER")]
public sealed class StormPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (FlashKernel.CardType(x.B, x.R, play.Card) == FlashCardType.Power)
            x.PushCardPlayPair(FlashCardPlayPairKind.StormLightning, self.Owner, x.B.Powers[self.Index].Id,
                x.B.Powers[self.Index].Amount);
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.TryTakeCardPlayPair(FlashCardPlayPairKind.StormLightning, self.Owner, x.B.Powers[self.Index].Id,
                out int amount) && amount > 0)
        {
            FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("LIGHTNING_ORB"), amount);
        }
    }
}

[FlashContent("SUBROUTINE_POWER")]
public sealed class SubroutinePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (FlashKernel.CardType(x.B, x.R, play.Card) == FlashCardType.Power)
            x.PushCardPlayPair(FlashCardPlayPairKind.SubroutineEnergy, self.Owner, x.B.Powers[self.Index].Id,
                x.B.Powers[self.Index].Amount);
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (x.TryTakeCardPlayPair(FlashCardPlayPairKind.SubroutineEnergy, self.Owner, x.B.Powers[self.Index].Id,
                out int amount) && amount > 0)
        {
            FlashKernel.GainEnergy(ref x, amount);
        }
    }
}

[FlashContent("THUNDER_POWER")]
public sealed class ThunderPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterOrbEvoked(ref FlashExec x, FlashListener self, in FlashOrb orb,
        scoped ReadOnlySpan<byte> targets)
    {
        if (self.Owner != FlashLimits.PlayerOwner || !FlashRegistry.TryOrb("LIGHTNING_ORB", out ushort lightning)
            || orb.Id != lightning)
        {
            return;
        }
        Span<byte> living = stackalloc byte[FlashLimits.Enemies + 1];
        int count = 0;
        foreach (byte target in targets)
        {
            if (FlashKernel.IsHittable(x.B, target))
                living[count++] = target;
        }
        if (count > 0)
            FlashKernel.Damage(ref x, living[..count], x.B.Powers[self.Index].Amount, FlashValueProps.Unpowered,
                self.Owner, hasCardSource: false, default);
    }
}

[FlashContent("SMOKESTACK_POWER")]
public sealed class SmokestackPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardGeneratedForCombat(ref FlashExec x, FlashListener self, int position, byte creator)
    {
        if (creator != FlashLimits.PlayerOwner)
            return;
        if ((uint)position >= (uint)x.B.CardCount)
            throw new FlashUnsupportedException("生成牌触发时该牌已不在牌堆。");
        FlashCard card = x.B.Cards[position];
        if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Status)
            return;
        FlashCardEffects.DamageAllEnemies(ref x, x.B.Powers[self.Index].Amount, FlashValueProps.Unpowered,
            self.Owner);
    }
}

[FlashContent("TRASH_TO_TREASURE_POWER")]
public sealed class TrashToTreasurePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardGeneratedForCombat(ref FlashExec x, FlashListener self, int position, byte creator)
    {
        if (creator != FlashLimits.PlayerOwner)
            return;
        if ((uint)position >= (uint)x.B.CardCount)
            throw new FlashUnsupportedException("生成牌触发时该牌已不在牌堆。");
        FlashCard card = x.B.Cards[position];
        if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Status)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        for (int index = 0; index < amount; index++)
        {
            ushort orb = FlashKernel.RandomOrb(ref x.B);
            if (!FlashKernel.ChannelOrb(ref x, orb))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("ECHO_FORM_POWER")]
public sealed class EchoFormPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override int ModifyCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card, byte target,
        int playCount)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return playCount;
        int started = x.B.Counter(FlashCounter.CardPlaySeriesStartedThisTurn);
        return started < x.B.Powers[self.Index].Amount ? playCount + 1 : playCount;
    }
}

[FlashContent("SIGNAL_BOOST_POWER")]
public sealed class SignalBoostPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override int ModifyCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card, byte target,
        int playCount)
        => FlashKernel.CardType(x.B, x.R, card) == FlashCardType.Power ? playCount + 1 : playCount;

    public override void AfterModifyingCardPlayCount(ref FlashExec x, FlashListener self, in FlashCard card)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, amount - 1);
    }
}

[FlashContent("FERAL_POWER")]
public sealed class FeralPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    // FeralPredictionState.zeroCostAttacksPlayed lives in FlashPower.Aux0.
    public override void OwnerAfterSideTurnStart(ref FlashExec x, FlashListener self)
    {
        x.B.Powers[self.Index].Aux0 = 0;
    }

    public override FlashPile ModifyCardPlayResultLocation(ref FlashExec x, FlashListener self, in FlashCard card,
        bool isAutoPlay, int energyValue, FlashPile location, out FlashPilePosition position)
    {
        position = FlashPilePosition.Bottom;
        // Native FeralPower checks CardModel.IsDupe, not IsClone. Flash has no dupe cards (the
        // importer rejects them); FlashCard.IsClone marks native CreateClone copies (AdaptiveStrike),
        // which FeralPower does not exclude.
        if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Attack || energyValue > 0)
            return location;
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Aux0 >= power.Amount)
            return location;
        position = FlashPilePosition.Top;
        return FlashPile.Hand;
    }

    public override void AfterModifyingCardPlayResultLocation(ref FlashExec x, FlashListener self, in FlashCard card,
        FlashPile location)
    {
        x.B.Powers[self.Index].Aux0++;
    }
}

[FlashContent("CONSUMING_SHADOW_POWER")]
public sealed class ConsumingShadowPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates || x.B.OrbCount == 0)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        for (int index = 0; index < amount && x.B.OrbCount > 0; index++)
        {
            FlashKernel.EvokeOrb(ref x, x.B.OrbCount - 1);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("COOLANT_POWER")]
public sealed class CoolantPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref FlashExec x, FlashListener self)
    {
        int block = FlashKernel.DistinctOrbCount(x.B) * x.B.Powers[self.Index].Amount;
        if (block > 0)
            FlashKernel.GainBlock(ref x, self.Owner, block, FlashValueProps.Unpowered, fromCard: false);
    }
}

[FlashContent("BIASED_COGNITION_POWER")]
public sealed class BiasedCognitionPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref FlashExec x, FlashListener self)
    {
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<FocusPower>(),
            -x.B.Powers[self.Index].Amount, self.Owner);
    }
}

[FlashContent("REPTILE_TRINKET_POWER")]
public sealed class ReptileTrinketPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
            FlashKernel.RetireTemporaryStrength(ref x, self);
    }
}
