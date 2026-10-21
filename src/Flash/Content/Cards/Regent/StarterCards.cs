using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

// Regent starter deck. Existing engine paths: StrikeRegent/DefendRegent run the general
// attack/block recipe (CardOnPlayMirrors); FallingStar's attack is the mirror, then
// CorePowerSupport.ApplyCardPowers applies Weak and Vulnerable after the enemy-death pass;
// Venerate runs CardOnPlaySupport's GainStars.

[FlashContent("STRIKE_REGENT")]
public sealed class StrikeRegent : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("DEFEND_REGENT")]
public sealed class DefendRegent : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

/// <summary>
/// <c>FallingStar</c> (2 star cost): DamageCmd.Attack, then CorePowerSupport's
/// <c>FallingStar</c> case applies Weak and then Vulnerable after the deferred enemy deaths.
/// </summary>
[FlashContent("FALLING_STAR")]
public sealed class FallingStar : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.WeakPower>(),
            (int)FlashCardEffects.Var(x, 2), FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.VulnerablePower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary><c>Venerate</c>: CardOnPlaySupport's <c>Venerate</c> case gains the Stars variable.</summary>
[FlashContent("VENERATE")]
public sealed class Venerate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 0));
}
