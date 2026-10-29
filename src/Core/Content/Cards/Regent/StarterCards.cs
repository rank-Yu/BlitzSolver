using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Cards.Regent;

[ContentId("STRIKE_REGENT")]
public sealed class StrikeRegent : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralAttack(ref x, 0);
}

[ContentId("DEFEND_REGENT")]
public sealed class DefendRegent : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralBlock(ref x, 0);
}

/// <summary>
/// <c>FallingStar</c> (2 star cost): DamageCmd.Attack, then CorePowerSupport's
/// <c>FallingStar</c> case applies Weak and then Vulnerable after the deferred enemy deaths.
/// </summary>
[ContentId("FALLING_STAR")]
public sealed class FallingStar : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower", "WeakPower"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        Kernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || x.Target == Limits.NoOwner)
            return;
        Kernel.ApplyPower(ref x, x.Target, Registry.PowerId<Powers.WeakPower>(),
            (int)CardEffects.Var(x, 2), Limits.PlayerOwner);
        Kernel.ApplyPower(ref x, x.Target, Registry.PowerId<Powers.VulnerablePower>(),
            (int)CardEffects.Var(x, 1), Limits.PlayerOwner);
    }
}

/// <summary><c>Venerate</c>: CardOnPlaySupport's <c>Venerate</c> case gains the Stars variable.</summary>
[ContentId("VENERATE")]
public sealed class Venerate : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Stars"];

    public override void OnPlay(ref Exec x)
        => Kernel.GainStars(ref x, (int)CardEffects.Var(x, 0));
}
