using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Ironclad;

[FlashContent("BREAK")]
public sealed class Break : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

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
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}
