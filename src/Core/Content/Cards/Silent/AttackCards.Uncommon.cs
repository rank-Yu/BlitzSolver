using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

[FlashContent("SKEWER")]
public sealed class Skewer : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // GeneralCardMirrors.GeneralAttackOnPlay: an X-cost attack uses ResolveEnergyXValue as its hit
    // count (resolved once).
    public override void OnPlay(ref FlashExec x)
    {
        int hits = FlashKernel.ResolveEnergyXValue(ref x);
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hits);
    }
}

[FlashContent("DASH")]
public sealed class Dash : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Block"];

    // Dash.cs: GainBlock before the attack; the inferred recipe keeps that order.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.GeneralAttack(ref x, 0);
    }
}

[FlashContent("POUNCE")]
public sealed class Pounce : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry.Pounce: attack, then FreeSkillPower 1.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.FreeSkillPower>(), 1, FlashLimits.PlayerOwner);
    }
}

[FlashContent("MEMENTO_MORI")]
public sealed class MementoMori : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    // CalculatedVarSpecRegistry.MementoMori: CalculationBase + ExtraDamage * cards discarded this turn.
    public override void OnPlay(ref FlashExec x)
    {
        decimal damage = FlashCardEffects.Var(x, 0)
            + FlashCardEffects.Var(x, 1) * x.B.Counter(FlashCounter.CardsDiscardedThisTurn);
        FlashCardEffects.Attack(ref x, damage);
    }
}

[FlashContent("PRECISE_CUT")]
public sealed class PreciseCut : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    // CalculatedVarSpecRegistry.PreciseCut: the played card is in the play pile, so the multiplier is
    // -hand count.
    public override void OnPlay(ref FlashExec x)
    {
        decimal damage = FlashCardEffects.Var(x, 0) - FlashCardEffects.Var(x, 1) * x.B.HandCount;
        FlashCardEffects.Attack(ref x, damage);
    }
}

[FlashContent("FINISHER")]
public sealed class Finisher : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    // CalculatedVarSpecRegistry.Finisher: CalculatedHits = CalculationBase + CalculationExtra *
    // attacks played this turn (GetAttacksPlayedThisTurn), the hit count of the Damage attack.
    public override void OnPlay(ref FlashExec x)
    {
        int hits = (int)(FlashCardEffects.Var(x, 1)
            + FlashCardEffects.Var(x, 2) * x.B.Counter(FlashCounter.AttacksPlayedThisTurn));
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hits);
    }
}

[FlashContent("BACKSTAB")]
public sealed class Backstab : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("STRANGLE")]
public sealed class Strangle : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StranglePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.StranglePower>(), 1);
    }
}

[FlashContent("PINPOINT")]
public sealed class Pinpoint : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    // Pinpoint.cs: entering combat reduces its cost by the Skills already finished this turn.
    public override void AfterEnteredCombat(ref FlashExec x, FlashPile pile, int index)
    {
        int amount = x.B.Counter(FlashCounter.SkillCardsPlayedThisTurn);
        if (amount <= 0)
            return;
        ref FlashCard card = ref FlashPiles.At(ref x.B, pile, index);
        if (card.Id != Id)
            return;
        FlashKernel.AddEnergyCostModifier(ref card, -amount, FlashCostType.Relative,
            FlashCostExpiration.EndOfTurn, reduceOnly: false);
    }

    // Pinpoint.cs.AfterCardPlayed: each Skill its owner plays reduces its cost by 1 this turn, while
    // it sits in any combat pile.
    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        if (FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Skill)
            return;
        ref FlashCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        FlashKernel.AddEnergyCostModifier(ref card, -1, FlashCostType.Relative,
            FlashCostExpiration.EndOfTurn, reduceOnly: false);
    }
}

/// <summary>
/// <c>EchoingSlash</c>: one attack context hits all enemies, and every target killed adds one more
/// wave. Existing engine: <c>CardOnPlaySupport.Batch042.ApplyEchoingSlash</c>.
/// </summary>
[FlashContent("ECHOING_SLASH")]
public sealed class EchoingSlash : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.CardAttackRepeating(ref x, 0, initialWaves: 1);
}
