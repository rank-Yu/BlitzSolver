using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

[BlitzContent("MURDER")]
public sealed class Murder : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    // CalculatedVarSpecRegistry.Murder: CalculationBase + ExtraDamage * cards drawn this combat.
    public override void OnPlay(ref BlitzExec x)
    {
        decimal damage = BlitzCardEffects.Var(x, 0)
            + BlitzCardEffects.Var(x, 1) * x.B.Counter(BlitzCounter.CardsDrawn);
        BlitzCardEffects.Attack(ref x, damage);
    }
}

[BlitzContent("ASSASSINATE")]
public sealed class Assassinate : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

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
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[BlitzContent("GRAND_FINALE")]
public sealed class GrandFinale : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // GrandFinale.cs.IsPlayable: only when the draw pile is empty (CardIsPlayableMirrors).
    public override bool IsPlayable(in BlitzBattle battle, BlitzRules rules, in BlitzCard card)
        => battle.DrawCount == 0;

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

/// <summary>
/// <c>TheHunt</c>: attack, and when the selected target's powers all allow a fatal kill and the hit
/// killed it, gain 1 <c>TheHuntPower</c> and 30 long-term resource. The extra card reward itself
/// lives on the combat room and has no representation in either simulated state; the existing
/// engine likewise only records the power and the long-term resource. Existing engine:
/// <c>CorePowerSupport</c>'s TheHunt case.
/// </summary>
[BlitzContent("THE_HUNT")]
public sealed class TheHunt : BlitzCardDef
{
    private const int LongTermResourceValue = 30;

    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<string> AppliedPowers => ["THE_HUNT_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        byte target = x.Target;
        bool fatal = target != BlitzLimits.NoOwner && BlitzKernel.TargetDeathTriggersFatal(x.B, target);
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || target == BlitzLimits.NoOwner || x.B.Creature(target).IsAlive || !fatal)
            return;
        // CorePowerSupport: Apply<TheHuntPower>(owner, 1, owner) then RecordLongTermResource(30).
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<TheHuntPower>(), 1,
            BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        x.B.AddCounter(BlitzCounter.LongTermResourceValue, LongTermResourceValue);
        BlitzGrowthRewards.Add(ref x.B, BlitzGrowthSource.TheHunt);
    }
}
