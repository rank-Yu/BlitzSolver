using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary>
/// <c>MeteorShower</c>: attack every enemy, then CorePowerSupport.ApplyCardPowers applies Weak and
/// Vulnerable to every hittable enemy in roster order (resolving power amount changes between
/// targets).
/// </summary>
[BlitzContent("METEOR_SHOWER")]
public sealed class MeteorShower : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower", "WeakPower"];

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
        BlitzCardEffects.ApplyToAllEnemies(ref x, BlitzRegistry.PowerId<Powers.WeakPower>(), 2);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToAllEnemies(ref x, BlitzRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}
