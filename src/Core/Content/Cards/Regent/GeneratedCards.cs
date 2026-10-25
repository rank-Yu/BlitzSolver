using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary><c>MinionDiveBomb</c>: the inferred single-target attack (13 damage, +3 on upgrade).</summary>
[BlitzContent("MINION_DIVE_BOMB")]
public sealed class MinionDiveBomb : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

/// <summary><c>MinionSacrifice</c>: the inferred self block (7, +3 on upgrade).</summary>
[BlitzContent("MINION_SACRIFICE")]
public sealed class MinionSacrifice : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);
}

/// <summary><c>MinionStrike</c>: the inferred attack (6 damage) then draw the Cards variable (1).</summary>
[BlitzContent("MINION_STRIKE")]
public sealed class MinionStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers settles newly dead enemies after the mirror attack and
        // before the card-specific draw compensation; HittableEnemies/Hellraiser read that phase.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

/// <summary>
/// <c>SovereignBlade</c> (a token): BespokeCardMirrors attacks once per Repeat with the card's Damage
/// (SeekingEdgePower switches the target to every enemy), then gains block equal to ParryPower when
/// its owner has one. Repeat is always 1: no vanilla effect calls <c>SetRepeats</c>.
/// </summary>
[BlitzContent("SOVEREIGN_BLADE")]
public sealed class SovereignBlade : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override bool UsesIdentityLedger => true;

    public override BlitzTargetType TargetType(in BlitzBattle battle, BlitzRules rules, in BlitzCard card)
        => RegentContent.HasSeekingEdge(battle) ? BlitzTargetType.AllEnemies : BlitzTargetType.AnyEnemy;

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: (int)BlitzCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        int parry = RegentContent.ParryAmount(x.B);
        if (parry > 0)
            BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, parry, BlitzValueProps.Move, fromCard: true);
    }
}
