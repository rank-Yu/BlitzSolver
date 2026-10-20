namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Powers referenced by the event / event-reward cards (EC batch). Each definition names the
// existing-engine path it mirrors; hooks that the existing engine only settles outside combat stay
// inert here (ForbiddenGrimoirePower, ImprovementPower).
// - FeedingFrenzyPower is a TemporaryStrengthPower: CardPowerOnPlaySupport applies Strength with the
//   card and then the temporary power; EndTurnPowerSupport.TriggerRegular retires it at its owner's
//   side turn end (src/Prediction/CardPowerOnPlaySupport.cs, src/Prediction/EndTurnPowerSupport.cs).
// - HelloWorldPower is the HelloWorldPower case of TurnStartPowerSupport.ContinueBeforeHandDraw
//   (distinct common character cards, AmountOnTurnStart of them).
// - ReboundPower is ModifyCardPlayResultLocationMirrors.HandleReboundPower/After plus the
//   REBOUND_POWER entry of CorePowerSupport.TriggerTransientSideTurnEndPowers.
// - ToricToughnessPower is CorePowerSupport.ApplyCardPowers' AddPowerInstance + SetBlock and the
//   TriggerAfterBlockCleared case (instanced; Amount is the turn count, the block value is Aux0).
// - WraithFormPower is the WRAITH_FORM_POWER case of PersistentPowerSupport.TriggerOwnerAfterSideTurnStart.
// - CuriousPower is HookMirrors.ModifyEnergyCostInCombat's CuriousPower case.
// - ForbiddenGrimoirePower/ImprovementPower only implement AfterCombatEnd, which the existing engine
//   does not run inside a combat; they carry the counter for the evaluation and admission closure.

/// <summary>
/// Native <c>FeedingFrenzyPower</c>, a positive <c>TemporaryStrengthPower</c>: the application adds
/// Strength (inside <c>BeforeApplied</c>) and the owner's side turn end removes it and takes the
/// Strength back.
/// </summary>
[FlashContent("FEEDING_FRENZY_POWER")]
public sealed class FeedingFrenzyPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override string? InternallyAppliedPowerId => "STRENGTH_POWER";

    public override bool IsTemporaryPower => true;

    // EndTurnPowerSupport.TriggerRegular: case TemporaryStrengthPower when the owner participates.
    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (participates)
        {
            FlashKernel.RetireTemporaryStrength(ref x, self);
        }
    }
}

/// <summary>
/// Native <c>HelloWorldPower</c>: BeforeHandDraw adds <c>AmountOnTurnStart</c> distinct common
/// character cards to the player's hand (<c>TurnStartPowerSupport.ContinueBeforeHandDraw</c>).
/// </summary>
[FlashContent("HELLO_WORLD_POWER")]
public sealed class HelloWorldPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool UsesAmountOnTurnStart => true;

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void BeforeHandDraw(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].AmountOnTurnStart;
        if (amount < 1)
            return;
        if (amount > FlashLimits.Cards)
            throw new FlashCapacityException("HelloWorld.Generated", FlashLimits.Cards);
        Span<FlashCard> generated = stackalloc FlashCard[amount];
        int sampled = FlashCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, FlashCardType.None, amount,
            generated, FlashCardPools.CharacterAll, FlashCardRarity.Common);
        for (int index = 0; index < sampled; index++)
        {
            if (!FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Hand))
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
[FlashContent("REBOUND_POWER")]
public sealed class ReboundPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override FlashPile ModifyCardPlayResultLocation(ref FlashExec x, FlashListener self, in FlashCard card,
        bool isAutoPlay, int energyValue, FlashPile location, out FlashPilePosition position)
    {
        position = FlashPilePosition.Bottom;
        if (self.Owner != FlashLimits.PlayerOwner || location != FlashPile.Discard)
            return location;
        position = FlashPilePosition.Top;
        return FlashPile.Draw;
    }

    public override void AfterModifyingCardPlayResultLocation(ref FlashExec x, FlashListener self, in FlashCard card,
        FlashPile location)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        FlashKernel.SetPowerAmountAt(ref x.B, self.Index, power.Amount - 1);
    }
}

/// <summary>
/// Native <c>ToricToughnessPower</c> (instanced): <c>AfterBlockCleared</c> gains the block value
/// stored by <c>SetBlock</c> as unpowered block, then decrements the turn count. Flash stores the
/// block value in <see cref="FlashPower.Aux0"/> (Amount is the turn count).
/// </summary>
[FlashContent("TORIC_TOUGHNESS_POWER")]
public sealed class ToricToughnessPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsInstanced => true;

    // Two instances can be removed in the same block-clear pass, which shifts the occurrence of the
    // later row; the listener pass must track removals (root-level HasIdentityContent).
    public override bool IsIdentityContent => true;

    public override void AfterBlockCleared(ref FlashExec x, FlashListener self)
    {
        ref FlashPower power = ref x.B.Powers[self.Index];
        int block = power.Aux0;
        if (block > 0)
        {
            FlashKernel.GainBlock(ref x, self.Owner, block, FlashValueProps.Unpowered, fromCard: false);
            if (x.NeedsChoice)
                return;
        }
        FlashKernel.SetPowerAmountAt(ref x.B, self.Index, power.Amount - 1);
    }
}

/// <summary>
/// Native <c>WraithFormPower</c>: at its owner's side turn start it applies -Amount Dexterity
/// (<c>PersistentPowerSupport.TriggerOwnerAfterSideTurnStart</c>).
/// </summary>
[FlashContent("WRAITH_FORM_POWER")]
public sealed class WraithFormPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void OwnerAfterSideTurnStart(ref FlashExec x, FlashListener self)
    {
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<DexterityPower>(), -amount, self.Owner);
    }
}

/// <summary>
/// Native <c>CuriousPower</c>: its owner's Power cards cost <c>Amount</c> less
/// (<c>HookMirrors.ModifyEnergyCostInCombat</c>, a pure query; the caller clamps at zero).
/// </summary>
[FlashContent("CURIOUS_POWER")]
public sealed class CuriousPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override decimal TryModifyEnergyCostInCombat(in FlashBattle battle, FlashRules rules, FlashListener self,
        in FlashCard card, FlashPile pile, decimal amount)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return amount;
        if (FlashRegistry.Card(card.Id).CardType(battle, rules, card) != FlashCardType.Power)
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
/// the encounter's <c>RanOutOfTime</c> and makes the dummy escape; the existing engine maps that to
/// <c>SearchBoundaryReason.EventDefeat</c>. Flash has no event-defeat outcome, so the last tick is an
/// explicit boundary instead of a wrong victory.
/// </summary>
[FlashContent("BATTLEWORN_DUMMY_TIME_LIMIT_POWER")]
public sealed class BattlewornDummyTimeLimitPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates || !x.B.Creature(self.Owner).IsAlive)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Amount > 1)
        {
            FlashKernel.SetPowerAmountAt(ref x.B, self.Index, power.Amount - 1);
            return;
        }
        throw new FlashUnsupportedException(
            "战损假人超时：现有引擎记 EventDefeat 边界（遭遇 RanOutOfTime / 怪物逃跑），Flash 没有该结局。");
    }
}

/// <summary>
/// Native <c>ForbiddenGrimoirePower</c>: its only hook is <c>AfterCombatEnd</c> (adds a card-removal
/// reward). A combat has no such effect, so the definition only carries the counter; the existing
/// engine records <c>RecordLongTermResource(50)</c> and a growth reward when the card is played.
/// </summary>
[FlashContent("FORBIDDEN_GRIMOIRE_POWER")]
public sealed class ForbiddenGrimoirePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;
}

/// <summary>
/// Native <c>ImprovementPower</c>: its only hook is <c>AfterCombatEnd</c> (upgrades run-deck cards
/// after the fight). No combat settlement reads it, so the definition carries the counter only.
/// </summary>
[FlashContent("IMPROVEMENT_POWER")]
public sealed class ImprovementPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;
}
