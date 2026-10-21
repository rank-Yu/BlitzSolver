using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

[FlashContent("MURDER")]
public sealed class Murder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    // CalculatedVarSpecRegistry.Murder: CalculationBase + ExtraDamage * cards drawn this combat.
    public override void OnPlay(ref FlashExec x)
    {
        decimal damage = FlashCardEffects.Var(x, 0)
            + FlashCardEffects.Var(x, 1) * x.B.Counter(FlashCounter.CardsDrawn);
        FlashCardEffects.Attack(ref x, damage);
    }
}

[FlashContent("ASSASSINATE")]
public sealed class Assassinate : FlashCardDef
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

[FlashContent("GRAND_FINALE")]
public sealed class GrandFinale : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // GrandFinale.cs.IsPlayable: only when the draw pile is empty (CardIsPlayableMirrors).
    public override bool IsPlayable(in FlashBattle battle, FlashRules rules, in FlashCard card)
        => battle.DrawCount == 0;

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

/// <summary>
/// <c>TheHunt</c>: attack, and when the selected target's powers all allow a fatal kill and the hit
/// killed it, gain 1 <c>TheHuntPower</c> and 30 long-term resource. The extra card reward itself
/// lives on the combat room and has no representation in either simulated state; the existing
/// engine likewise only records the power and the long-term resource. Existing engine:
/// <c>CorePowerSupport</c>'s TheHunt case.
/// </summary>
[FlashContent("THE_HUNT")]
public sealed class TheHunt : FlashCardDef
{
    private const int LongTermResourceValue = 30;

    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<string> AppliedPowers => ["THE_HUNT_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        byte target = x.Target;
        bool fatal = target != FlashLimits.NoOwner && FlashKernel.TargetDeathTriggersFatal(x.B, target);
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || target == FlashLimits.NoOwner || x.B.Creature(target).IsAlive || !fatal)
            return;
        // CorePowerSupport: Apply<TheHuntPower>(owner, 1, owner) then RecordLongTermResource(30).
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<TheHuntPower>(), 1,
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        x.B.AddCounter(FlashCounter.LongTermResourceValue, LongTermResourceValue);
        FlashGrowthRewards.Add(ref x.B, FlashGrowthSource.TheHunt);
    }
}
