using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

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

/// <summary>
/// <c>SovereignBlade</c> (a token): BespokeCardMirrors attacks once per Repeat with the card's Damage
/// (SeekingEdgePower switches the target to every enemy), then gains block equal to ParryPower when
/// its owner has one. Repeat is always 1: no vanilla effect calls <c>SetRepeats</c>.
/// </summary>
[FlashContent("SOVEREIGN_BLADE")]
public sealed class SovereignBlade : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override bool UsesIdentityLedger => true;

    public override FlashTargetType TargetType(in FlashBattle battle, FlashRules rules, in FlashCard card)
        => RegentContent.HasSeekingEdge(battle) ? FlashTargetType.AllEnemies : FlashTargetType.AnyEnemy;

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: (int)FlashCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        int parry = RegentContent.ParryAmount(x.B);
        if (parry > 0)
            FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, parry, FlashValueProps.Move, fromCard: true);
    }
}
