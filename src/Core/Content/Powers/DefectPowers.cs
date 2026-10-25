using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

[BlitzContent("FOCUS_POWER")]
public sealed class FocusPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool AllowNegative => true;

    public override decimal ModifyOrbValue(in BlitzBattle battle, BlitzRules rules, BlitzListener self, in BlitzOrb orb,
        decimal value)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return value;
        return Math.Max(value + battle.Powers[self.Index].Amount, 0m);
    }
}

public abstract class TemporaryFocusDef : BlitzPowerDef
{
    public override BlitzPowerType Type => IsPositive ? BlitzPowerType.Buff : BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    protected virtual bool IsPositive => true;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
            BlitzKernel.RetireTemporaryFocus(ref x, self);
    }
}

[BlitzContent("FOCUSED_STRIKE_POWER")]
public sealed class FocusedStrikePower : TemporaryFocusDef;

[BlitzContent("HOTFIX_POWER")]
public sealed class HotfixPower : TemporaryFocusDef;

[BlitzContent("SYNCHRONIZE_POWER")]
public sealed class SynchronizePower : TemporaryFocusDef;

[BlitzContent("HYPERBEAM_FOCUS_DOWN_POWER")]
public sealed class HyperbeamFocusDownPower : TemporaryFocusDef
{
    protected override bool IsPositive => false;
}

[BlitzContent("ITERATION_POWER")]
public sealed class IterationPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardDrawn(ref BlitzExec x, BlitzListener self, int drawnPosition, bool fromHandDraw)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        if ((uint)drawnPosition >= (uint)x.B.CardCount)
            throw new BlitzUnsupportedException("抽牌触发时抽到的牌已不在牌堆。");
        BlitzCard card = x.B.Cards[drawnPosition];
        if (BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Status)
            return;
        if (x.B.Counter(BlitzCounter.StatusCardsDrawnThisTurn) > 1)
            return;
        BlitzKernel.Draw(ref x, x.B.Powers[self.Index].Amount, fromHandDraw: false);
    }
}

[BlitzContent("MACHINE_LEARNING_POWER")]
public sealed class MachineLearningPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
        => self.Owner == BlitzLimits.PlayerOwner ? amount + x.B.Powers[self.Index].Amount : amount;
}

[BlitzContent("LOOP_POWER")]
public sealed class LoopPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterPlayerTurnStart(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner || x.B.OrbCount == 0)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        for (int index = 0; index < amount; index++)
        {
            BlitzKernel.OrbPassiveAt(ref x, 0);
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("CREATIVE_AI_POWER")]
public sealed class CreativeAiPower : BlitzPowerDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterPowers];

    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeHandDraw(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        Span<BlitzCard> generated = stackalloc BlitzCard[BlitzLimits.MaxHandSize];
        int count = 0;
        for (int index = 0; index < amount; index++)
        {
            // CardFactory.GetDistinctForCombat(..., 1, CombatCardGeneration) once per stack.
            if (count >= generated.Length)
                throw new BlitzCapacityException("CreativeAi.Generated", generated.Length);
            if (BlitzCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, BlitzCardType.Power, 1,
                    generated[count..], BlitzCardPools.CharacterPowers) == 0)
            {
                break;
            }
            count++;
        }
        // TurnStartPowerSupport queues all generated cards first, then adds them one by one.
        for (int index = 0; index < count; index++)
        {
            if (!BlitzKernel.AddGeneratedCard(ref x, generated[index], BlitzPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("SPINNER_POWER")]
public sealed class SpinnerPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterEnergyReset(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("GLASS_ORB"), x.B.Powers[self.Index].Amount);
    }
}

[BlitzContent("LIGHTNING_ROD_POWER")]
public sealed class LightningRodPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterEnergyReset(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("LIGHTNING_ORB"));
        if (x.NeedsChoice)
            return;
        BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, amount - 1);
    }
}

[BlitzContent("FREE_POWER_POWER")]
public sealed class FreePowerPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombatLate(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzCard card, BlitzPile pile, decimal amount)
    {
        if (self.Owner != BlitzLimits.PlayerOwner
            || BlitzRegistry.Card(card.Id).CardType(battle, rules, card) != BlitzCardType.Power)
            return amount;
        return pile is BlitzPile.Hand or BlitzPile.Play ? 0m : amount;
    }

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Power)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, amount - 1);
    }
}

[BlitzContent("HAILSTORM_POWER")]
public sealed class HailstormPower : BlitzPowerDef
{
    public override IReadOnlyList<string> VarNames => ["FrostOrbs"];

    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeSideTurnEnd(ref BlitzExec x, BlitzListener self, BlitzSide side, ushort participants)
    {
        if (!BlitzKernel.Participates(participants, self.Owner))
            return;
        int frost = BlitzKernel.CountOrbs(x.B, "FROST_ORB");
        if (frost < (int)x.R.PowerVar(x.B.Powers[self.Index].Id, 0))
            return;
        BlitzCardEffects.DamageAllEnemies(ref x, x.B.Powers[self.Index].Amount, BlitzValueProps.Unpowered,
            self.Owner);
    }
}

[BlitzContent("STORM_POWER")]
public sealed class StormPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) == BlitzCardType.Power)
            x.PushCardPlayPair(BlitzCardPlayPairKind.StormLightning, self.Owner, x.B.Powers[self.Index].Id,
                x.B.Powers[self.Index].Amount);
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (x.TryTakeCardPlayPair(BlitzCardPlayPairKind.StormLightning, self.Owner, x.B.Powers[self.Index].Id,
                out int amount) && amount > 0)
        {
            BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("LIGHTNING_ORB"), amount);
        }
    }
}

[BlitzContent("SUBROUTINE_POWER")]
public sealed class SubroutinePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void BeforeCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (BlitzKernel.CardType(x.B, x.R, play.Card) == BlitzCardType.Power)
            x.PushCardPlayPair(BlitzCardPlayPairKind.SubroutineEnergy, self.Owner, x.B.Powers[self.Index].Id,
                x.B.Powers[self.Index].Amount);
    }

    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (x.TryTakeCardPlayPair(BlitzCardPlayPairKind.SubroutineEnergy, self.Owner, x.B.Powers[self.Index].Id,
                out int amount) && amount > 0)
        {
            BlitzKernel.GainEnergy(ref x, amount);
        }
    }
}

[BlitzContent("THUNDER_POWER")]
public sealed class ThunderPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterOrbEvoked(ref BlitzExec x, BlitzListener self, in BlitzOrb orb,
        scoped ReadOnlySpan<byte> targets)
    {
        if (self.Owner != BlitzLimits.PlayerOwner || !BlitzRegistry.TryOrb("LIGHTNING_ORB", out ushort lightning)
            || orb.Id != lightning)
        {
            return;
        }
        Span<byte> living = stackalloc byte[BlitzLimits.Enemies + 1];
        int count = 0;
        foreach (byte target in targets)
        {
            if (BlitzKernel.IsHittable(x.B, target))
                living[count++] = target;
        }
        if (count > 0)
            BlitzKernel.Damage(ref x, living[..count], x.B.Powers[self.Index].Amount, BlitzValueProps.Unpowered,
                self.Owner, hasCardSource: false, default);
    }
}

[BlitzContent("SMOKESTACK_POWER")]
public sealed class SmokestackPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardGeneratedForCombat(ref BlitzExec x, BlitzListener self, int position, byte creator)
    {
        if (creator != BlitzLimits.PlayerOwner)
            return;
        if ((uint)position >= (uint)x.B.CardCount)
            throw new BlitzUnsupportedException("生成牌触发时该牌已不在牌堆。");
        BlitzCard card = x.B.Cards[position];
        if (BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Status)
            return;
        BlitzCardEffects.DamageAllEnemies(ref x, x.B.Powers[self.Index].Amount, BlitzValueProps.Unpowered,
            self.Owner);
    }
}

[BlitzContent("TRASH_TO_TREASURE_POWER")]
public sealed class TrashToTreasurePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void AfterCardGeneratedForCombat(ref BlitzExec x, BlitzListener self, int position, byte creator)
    {
        if (creator != BlitzLimits.PlayerOwner)
            return;
        if ((uint)position >= (uint)x.B.CardCount)
            throw new BlitzUnsupportedException("生成牌触发时该牌已不在牌堆。");
        BlitzCard card = x.B.Cards[position];
        if (BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Status)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        for (int index = 0; index < amount; index++)
        {
            ushort orb = BlitzKernel.RandomOrb(ref x.B);
            if (!BlitzKernel.ChannelOrb(ref x, orb))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("ECHO_FORM_POWER")]
public sealed class EchoFormPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override int ModifyCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card, byte target,
        int playCount)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return playCount;
        int started = x.B.Counter(BlitzCounter.CardPlaySeriesStartedThisTurn);
        return started < x.B.Powers[self.Index].Amount ? playCount + 1 : playCount;
    }
}

[BlitzContent("SIGNAL_BOOST_POWER")]
public sealed class SignalBoostPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override int ModifyCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card, byte target,
        int playCount)
        => BlitzKernel.CardType(x.B, x.R, card) == BlitzCardType.Power ? playCount + 1 : playCount;

    public override void AfterModifyingCardPlayCount(ref BlitzExec x, BlitzListener self, in BlitzCard card)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 0)
            BlitzKernel.SetPowerAmount(ref x.B, self.Owner, x.B.Powers[self.Index].Id, amount - 1);
    }
}

[BlitzContent("FERAL_POWER")]
public sealed class FeralPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    // FeralPredictionState.zeroCostAttacksPlayed lives in BlitzPower.Aux0.
    public override void OwnerAfterSideTurnStart(ref BlitzExec x, BlitzListener self)
    {
        x.B.Powers[self.Index].Aux0 = 0;
    }

    public override BlitzPile ModifyCardPlayResultLocation(ref BlitzExec x, BlitzListener self, in BlitzCard card,
        bool isAutoPlay, int energyValue, BlitzPile location, out BlitzPilePosition position)
    {
        position = BlitzPilePosition.Bottom;
        // Native FeralPower checks CardModel.IsDupe, not IsClone. The only dupes Blitz creates are the
        // HistoryCourse auto-play copies; BlitzCard.IsClone marks native CreateClone copies
        // (AdaptiveStrike), which FeralPower does not exclude.
        if (x.CurrentPlayIsHistoryCourseDupe
            || BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Attack || energyValue > 0)
            return location;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        if (power.Aux0 >= power.Amount)
            return location;
        position = BlitzPilePosition.Top;
        return BlitzPile.Hand;
    }

    public override void AfterModifyingCardPlayResultLocation(ref BlitzExec x, BlitzListener self, in BlitzCard card,
        BlitzPile location)
    {
        x.B.Powers[self.Index].Aux0++;
    }
}

[BlitzContent("CONSUMING_SHADOW_POWER")]
public sealed class ConsumingShadowPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (!participates || x.B.OrbCount == 0)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        for (int index = 0; index < amount && x.B.OrbCount > 0; index++)
        {
            BlitzKernel.EvokeOrb(ref x, x.B.OrbCount - 1);
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("COOLANT_POWER")]
public sealed class CoolantPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref BlitzExec x, BlitzListener self)
    {
        int block = BlitzKernel.DistinctOrbCount(x.B) * x.B.Powers[self.Index].Amount;
        if (block > 0)
            BlitzKernel.GainBlock(ref x, self.Owner, block, BlitzValueProps.Unpowered, fromCard: false);
    }
}

[BlitzContent("BIASED_COGNITION_POWER")]
public sealed class BiasedCognitionPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref BlitzExec x, BlitzListener self)
    {
        BlitzKernel.ApplyPower(ref x, self.Owner, BlitzRegistry.PowerId<FocusPower>(),
            -x.B.Powers[self.Index].Amount, self.Owner);
    }
}

[BlitzContent("REPTILE_TRINKET_POWER")]
public sealed class ReptileTrinketPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
            BlitzKernel.RetireTemporaryStrength(ref x, self);
    }
}
