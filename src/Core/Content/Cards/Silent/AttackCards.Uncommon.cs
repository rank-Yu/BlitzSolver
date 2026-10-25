using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

[BlitzContent("SKEWER")]
public sealed class Skewer : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // GeneralCardMirrors.GeneralAttackOnPlay: an X-cost attack uses ResolveEnergyXValue as its hit
    // count (resolved once).
    public override void OnPlay(ref BlitzExec x)
    {
        int hits = BlitzKernel.ResolveEnergyXValue(ref x);
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), hits);
    }
}

[BlitzContent("DASH")]
public sealed class Dash : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Block"];

    // Dash.cs: GainBlock before the attack; the inferred recipe keeps that order.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.GeneralAttack(ref x, 0);
    }
}

[BlitzContent("POUNCE")]
public sealed class Pounce : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry.Pounce: attack, then FreeSkillPower 1.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.FreeSkillPower>(), 1, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("MEMENTO_MORI")]
public sealed class MementoMori : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    // CalculatedVarSpecRegistry.MementoMori: CalculationBase + ExtraDamage * cards discarded this turn.
    public override void OnPlay(ref BlitzExec x)
    {
        decimal damage = BlitzCardEffects.Var(x, 0)
            + BlitzCardEffects.Var(x, 1) * x.B.Counter(BlitzCounter.CardsDiscardedThisTurn);
        BlitzCardEffects.Attack(ref x, damage);
    }
}

[BlitzContent("PRECISE_CUT")]
public sealed class PreciseCut : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    // CalculatedVarSpecRegistry.PreciseCut: the played card is in the play pile, so the multiplier is
    // -hand count.
    public override void OnPlay(ref BlitzExec x)
    {
        decimal damage = BlitzCardEffects.Var(x, 0) - BlitzCardEffects.Var(x, 1) * x.B.HandCount;
        BlitzCardEffects.Attack(ref x, damage);
    }
}

[BlitzContent("FINISHER")]
public sealed class Finisher : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    // CalculatedVarSpecRegistry.Finisher: CalculatedHits = CalculationBase + CalculationExtra *
    // attacks played this turn (GetAttacksPlayedThisTurn), the hit count of the Damage attack.
    public override void OnPlay(ref BlitzExec x)
    {
        int hits = (int)(BlitzCardEffects.Var(x, 1)
            + BlitzCardEffects.Var(x, 2) * x.B.Counter(BlitzCounter.AttacksPlayedThisTurn));
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), hits);
    }
}

[BlitzContent("BACKSTAB")]
public sealed class Backstab : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("STRANGLE")]
public sealed class Strangle : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StranglePower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.StranglePower>(), 1);
    }
}

[BlitzContent("PINPOINT")]
public sealed class Pinpoint : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);

    // Pinpoint.cs: entering combat reduces its cost by the Skills already finished this turn.
    public override void AfterEnteredCombat(ref BlitzExec x, BlitzPile pile, int index)
    {
        int amount = x.B.Counter(BlitzCounter.SkillCardsPlayedThisTurn);
        if (amount <= 0)
            return;
        ref BlitzCard card = ref BlitzPiles.At(ref x.B, pile, index);
        if (card.Id != Id)
            return;
        BlitzKernel.AddEnergyCostModifier(ref card, -amount, BlitzCostType.Relative,
            BlitzCostExpiration.EndOfTurn, reduceOnly: false);
    }

    // Pinpoint.cs.AfterCardPlayed: each Skill its owner plays reduces its cost by 1 this turn, while
    // it sits in any combat pile.
    public override void AfterCardPlayed(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        if (BlitzKernel.CardType(x.B, x.R, play.Card) != BlitzCardType.Skill)
            return;
        ref BlitzCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        BlitzKernel.AddEnergyCostModifier(ref card, -1, BlitzCostType.Relative,
            BlitzCostExpiration.EndOfTurn, reduceOnly: false);
    }
}

/// <summary>
/// <c>EchoingSlash</c>: one attack context hits all enemies, and every target killed adds one more
/// wave. Existing engine: <c>CardOnPlaySupport.Batch042.ApplyEchoingSlash</c>.
/// </summary>
[BlitzContent("ECHOING_SLASH")]
public sealed class EchoingSlash : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.CardAttackRepeating(ref x, 0, initialWaves: 1);
}
