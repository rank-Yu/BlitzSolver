using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// Native <c>FeedingFrenzyPower</c>, a positive <c>TemporaryStrengthPower</c>: the application adds
/// Strength (inside <c>BeforeApplied</c>) and the owner's side turn end removes it and takes the
/// Strength back.
/// </summary>
[BlitzContent("FEEDING_FRENZY_POWER")]
public sealed class FeedingFrenzyPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override string? InternallyAppliedPowerId => "STRENGTH_POWER";

    public override bool IsTemporaryPower => true;

    // EndTurnPowerSupport.TriggerRegular: case TemporaryStrengthPower when the owner participates.
    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (participates)
        {
            BlitzKernel.RetireTemporaryStrength(ref x, self);
        }
    }
}

/// <summary>
/// Native <c>HelloWorldPower</c>: BeforeHandDraw adds <c>AmountOnTurnStart</c> distinct common
/// character cards to the player's hand (<c>TurnStartPowerSupport.ContinueBeforeHandDraw</c>).
/// </summary>
[BlitzContent("HELLO_WORLD_POWER")]
public sealed class HelloWorldPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool UsesAmountOnTurnStart => true;

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAll];

    public override void BeforeHandDraw(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].AmountOnTurnStart;
        if (amount < 1)
            return;
        if (amount > BlitzLimits.Cards)
            throw new BlitzCapacityException("HelloWorld.Generated", BlitzLimits.Cards);
        Span<BlitzCard> generated = stackalloc BlitzCard[amount];
        int sampled = BlitzCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, BlitzCardType.None, amount,
            generated, BlitzCardPools.CharacterAll, BlitzCardRarity.Common);
        for (int index = 0; index < sampled; index++)
        {
            if (!BlitzKernel.AddGeneratedCard(ref x, generated[index], BlitzPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>ReboundPower</c>: the next card played from the owner's hand goes to the top of the
/// draw pile instead of the discard pile; then the power decrements. It is removed at the owner's
/// side turn end by <c>CorePowerSupport.TriggerTransientSideTurnEndPowers</c>.
/// </summary>
[BlitzContent("REBOUND_POWER")]
public sealed class ReboundPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override BlitzPile ModifyCardPlayResultLocation(ref BlitzExec x, BlitzListener self, in BlitzCard card,
        bool isAutoPlay, int energyValue, BlitzPile location, out BlitzPilePosition position)
    {
        position = BlitzPilePosition.Bottom;
        // ModifyCardPlayResultLocationMirrors.HandleReboundPower skips CardModel.IsDupe.
        if (x.CurrentPlayIsHistoryCourseDupe
            || self.Owner != BlitzLimits.PlayerOwner || location != BlitzPile.Discard)
            return location;
        position = BlitzPilePosition.Top;
        return BlitzPile.Draw;
    }

    public override void AfterModifyingCardPlayResultLocation(ref BlitzExec x, BlitzListener self, in BlitzCard card,
        BlitzPile location)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        BlitzKernel.SetPowerAmountAt(ref x.B, self.Index, power.Amount - 1);
    }
}

/// <summary>
/// Native <c>ToricToughnessPower</c> (instanced): <c>AfterBlockCleared</c> gains the block value
/// stored by <c>SetBlock</c> as unpowered block, then decrements the turn count. Blitz stores the
/// block value in <see cref="BlitzPower.Aux0"/> (Amount is the turn count).
/// </summary>
[BlitzContent("TORIC_TOUGHNESS_POWER")]
public sealed class ToricToughnessPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool IsInstanced => true;

    // Two instances can be removed in the same block-clear pass, which shifts the occurrence of the
    // later row; the listener pass must track removals (root-level HasIdentityContent).
    public override bool IsIdentityContent => true;

    public override void AfterBlockCleared(ref BlitzExec x, BlitzListener self)
    {
        ref BlitzPower power = ref x.B.Powers[self.Index];
        int block = power.Aux0;
        if (block > 0)
        {
            BlitzKernel.GainBlock(ref x, self.Owner, block, BlitzValueProps.Unpowered, fromCard: false);
            if (x.NeedsChoice)
                return;
        }
        BlitzKernel.SetPowerAmountAt(ref x.B, self.Index, power.Amount - 1);
    }
}

/// <summary>
/// Native <c>WraithFormPower</c>: at its owner's side turn start it applies -Amount Dexterity
/// (<c>PersistentPowerSupport.TriggerOwnerAfterSideTurnStart</c>).
/// </summary>
[BlitzContent("WRAITH_FORM_POWER")]
public sealed class WraithFormPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref BlitzExec x, BlitzListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        BlitzKernel.ApplyPower(ref x, self.Owner, BlitzRegistry.PowerId<DexterityPower>(), -amount, self.Owner);
    }
}

/// <summary>
/// Native <c>CuriousPower</c>: its owner's Power cards cost <c>Amount</c> less
/// (<c>HookMirrors.ModifyEnergyCostInCombat</c>, a pure query; the caller clamps at zero).
/// </summary>
[BlitzContent("CURIOUS_POWER")]
public sealed class CuriousPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombat(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzCard card, BlitzPile pile, decimal amount)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return amount;
        if (BlitzRegistry.Card(card.Id).CardType(battle, rules, card) != BlitzCardType.Power)
            return amount;
        if (amount <= 0m)
            return amount;
        decimal modified = amount - battle.Powers[self.Index].Amount;
        return modified < 0m ? 0m : modified;
    }
}

/// <summary>
/// Native <c>BattlewornDummyTimeLimitPower</c> (the three Battleworn Dummy variants): at its owner's
/// side turn end it decrements while the amount is above 1. On the last tick the native power sets
/// the encounter's <c>RanOutOfTime</c> and escapes the dummy; the existing engine records the
/// encounter flag (<c>SimulatedCombatState.MarkBattlewornDummyTimedOut</c>) and ends the action at
/// <c>SearchBoundaryReason.EventDefeat</c> before the death/poison passes. Blitz mirrors exactly that
/// amount-1 state and sets <see cref="BlitzOutcome.EventDefeat"/>; it does not remove the dummy (the
/// existing engine returns before the escape).
/// </summary>
[BlitzContent("BATTLEWORN_DUMMY_TIME_LIMIT_POWER")]
public sealed class BattlewornDummyTimeLimitPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override void EndTurnRegular(ref BlitzExec x, BlitzListener self, BlitzSide side, bool participates)
    {
        if (!participates || !x.B.Creature(self.Owner).IsAlive)
            return;
        ref BlitzPower power = ref x.B.Powers[self.Index];
        if (power.Amount > 1)
        {
            BlitzKernel.SetPowerAmountAt(ref x.B, self.Index, power.Amount - 1);
            return;
        }
        // EndTurnPowerSupport: the last tick leaves Amount at 1 and the engine's flag is read only
        // after TriggerEnemySideTurnEndEffects returns. RunEnemyTurn stops there.
        x.B.Outcome = BlitzOutcome.EventDefeat;
    }
}

/// <summary>
/// Native <c>ForbiddenGrimoirePower</c>: its only hook is <c>AfterCombatEnd</c> (adds a card-removal
/// reward). A combat has no such effect, so the definition only carries the counter; the existing
/// engine records <c>RecordLongTermResource(50)</c> and a growth reward when the card is played.
/// </summary>
[BlitzContent("FORBIDDEN_GRIMOIRE_POWER")]
public sealed class ForbiddenGrimoirePower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;
}

/// <summary>
/// Native <c>ImprovementPower</c>: its only hook is <c>AfterCombatEnd</c> (upgrades run-deck cards
/// after the fight). No combat settlement reads it, so the definition carries the counter only.
/// </summary>
[BlitzContent("IMPROVEMENT_POWER")]
public sealed class ImprovementPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;
}
