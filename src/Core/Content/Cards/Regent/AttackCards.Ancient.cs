using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary>
/// <c>MeteorShower</c>: attack every enemy, then CorePowerSupport.ApplyCardPowers applies Weak and
/// Vulnerable to every hittable enemy in roster order (resolving power amount changes between
/// targets).
/// </summary>
[ContentId("METEOR_SHOWER")]
public sealed class MeteorShower : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower", "WeakPower"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        Kernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        CardEffects.ApplyToAllEnemies(ref x, Registry.PowerId<Powers.WeakPower>(), 2);
        if (x.NeedsChoice)
            return;
        CardEffects.ApplyToAllEnemies(ref x, Registry.PowerId<Powers.VulnerablePower>(), 1);
    }
}
