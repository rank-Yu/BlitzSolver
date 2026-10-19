namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Regent;

// Regent cards fully covered by the general attack/block/draw recipe, including its Repeat and
// CalculatedHits handling in GeneralCardMirrors.GeneralAttackOnPlay, plus CardResultLocationMirrors.

/// <summary><c>CelestialMight</c>: the general attack with the Repeat variable as its hit count.</summary>
[FlashContent("CELESTIAL_MIGHT")]
public sealed class CelestialMight : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: (int)FlashCardEffects.Var(x, 1));
}

/// <summary><c>SevenStars</c> (7 star cost): every enemy takes the Repeat variable's number of hits.</summary>
[FlashContent("SEVEN_STARS")]
public sealed class SevenStars : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: (int)FlashCardEffects.Var(x, 1));
}

/// <summary>
/// <c>LunarBlast</c>: CalculatedHits = CalculationBase + CalculationExtra × Skill cards played this
/// turn (<c>CalculatedVarSpecRegistry.LunarBlast</c> → <c>GetSkillCardsPlayedThisTurn</c>), used as the
/// general attack's hit count.
/// </summary>
[FlashContent("LUNAR_BLAST")]
public sealed class LunarBlast : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref FlashExec x)
    {
        int hits = (int)(FlashCardEffects.Var(x, 1)
            + FlashCardEffects.Var(x, 2) * x.B.Counter(FlashCounter.SkillCardsPlayedThisTurn));
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: hits);
    }
}

/// <summary>
/// <c>CrescentSpear</c> (1 star cost): CalculatedDamage = CalculationBase + ExtraDamage × the number
/// of cards in the combat piles whose canonical star cost is non-negative or X
/// (<c>CalculatedVarSpecRegistry.CrescentSpear</c>).
/// </summary>
[FlashContent("CRESCENT_SPEAR")]
public sealed class CrescentSpear : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        int starCards = 0;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref readonly FlashCard candidate = ref x.B.Cards[position];
            FlashCardStats stats = x.R.Card(candidate.Id, candidate.Upgrade);
            if (stats.StarCost >= 0 || stats.StarCostsX)
                starCards++;
        }
        decimal damage = FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * starCards;
        FlashCardEffects.Attack(ref x, damage);
    }
}

/// <summary>
/// <c>ParticleWall</c> (2 star cost): the general block, then CardResultLocationMirrors sends a discard
/// result to the hand instead.
/// </summary>
[FlashContent("PARTICLE_WALL")]
public sealed class ParticleWall : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override FlashPile ResultLocation(in FlashBattle battle, FlashRules rules, in FlashCard card,
        FlashPile baseLocation, out FlashPilePosition position)
    {
        position = FlashPilePosition.Bottom;
        return baseLocation == FlashPile.Discard ? FlashPile.Hand : baseLocation;
    }
}

/// <summary><c>CloakOfStars</c> (1 star cost): the general block.</summary>
[FlashContent("CLOAK_OF_STARS")]
public sealed class CloakOfStars : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

/// <summary><c>Devastate</c> (4 star cost): the general attack.</summary>
[FlashContent("DEVASTATE")]
public sealed class Devastate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

/// <summary><c>Prophesize</c>: the general draw-card recipe with the Cards variable.</summary>
[FlashContent("PROPHESIZE")]
public sealed class Prophesize : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);
}
