using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

[BlitzContent("STRIKE_REGENT")]
public sealed class StrikeRegent : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("DEFEND_REGENT")]
public sealed class DefendRegent : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);
}

/// <summary>
/// <c>FallingStar</c> (2 star cost): DamageCmd.Attack, then CorePowerSupport's
/// <c>FallingStar</c> case applies Weak and then Vulnerable after the deferred enemy deaths.
/// </summary>
[BlitzContent("FALLING_STAR")]
public sealed class FallingStar : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower", "WeakPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.WeakPower>(),
            (int)BlitzCardEffects.Var(x, 2), BlitzLimits.PlayerOwner);
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.VulnerablePower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}

/// <summary><c>Venerate</c>: CardOnPlaySupport's <c>Venerate</c> case gains the Stars variable.</summary>
[BlitzContent("VENERATE")]
public sealed class Venerate : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.GainStars(ref x, (int)BlitzCardEffects.Var(x, 0));
}
