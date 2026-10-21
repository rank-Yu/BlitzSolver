using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

/// <summary>
/// <c>Soul</c>: the 0-cost Exhaust token that draws its <c>Cards</c> variable
/// (<c>CardOnPlayInferrer</c>'s owner-draw recipe; <c>CardDrawCardMirrors</c>-style behavior).
/// </summary>
[FlashContent("SOUL")]
public sealed class Soul : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 0), fromHandDraw: false);
}

/// <summary>
/// <c>SweepingGaze</c> token: 0-cost Ethereal/Exhaust Osty attack with random target. Existing engine:
/// <c>GeneralCardMirrors.GeneralAttackOnPlay</c> (the Osty tag selects the pet as dealer and the rule
/// table's RandomEnemy target type selects a random opponent).
/// </summary>
[FlashContent("SWEEPING_GAZE")]
public sealed class SweepingGaze : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage"];

    public override void OnPlay(ref FlashExec x)
        => NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
}
