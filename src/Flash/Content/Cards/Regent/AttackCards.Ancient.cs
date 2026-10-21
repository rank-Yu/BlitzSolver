using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

/// <summary>
/// <c>MeteorShower</c>: attack every enemy, then CorePowerSupport.ApplyCardPowers applies Weak and
/// Vulnerable to every hittable enemy in roster order (resolving power amount changes between
/// targets).
/// </summary>
[FlashContent("METEOR_SHOWER")]
public sealed class MeteorShower : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower", "WeakPower"];

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
        FlashCardEffects.ApplyToAllEnemies(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 2);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToAllEnemies(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}
