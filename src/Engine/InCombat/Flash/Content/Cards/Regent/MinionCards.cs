namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Regent;

// Regent Minion tokens (Charge, Guards and Begone transform into these). Each is the plain inferred
// attack/block/draw recipe; Token rarity cards are generated content only.

/// <summary><c>MinionDiveBomb</c>: the inferred single-target attack (13 damage, +3 on upgrade).</summary>
[FlashContent("MINION_DIVE_BOMB")]
public sealed class MinionDiveBomb : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

/// <summary><c>MinionSacrifice</c>: the inferred self block (7, +3 on upgrade).</summary>
[FlashContent("MINION_SACRIFICE")]
public sealed class MinionSacrifice : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

/// <summary><c>MinionStrike</c>: the inferred attack (6 damage) then draw the Cards variable (1).</summary>
[FlashContent("MINION_STRIKE")]
public sealed class MinionStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers settles newly dead enemies after the mirror attack and
        // before the card-specific draw compensation; HittableEnemies/Hellraiser read that phase.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}
