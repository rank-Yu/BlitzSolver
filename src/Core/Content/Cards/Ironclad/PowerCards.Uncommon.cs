using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Ironclad;

[BlitzContent("STAMPEDE")]
public sealed class Stampede : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.StampedePower>(), (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
}

[BlitzContent("INFLAME")]
public sealed class Inflame : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("FEEL_NO_PAIN")]
public sealed class FeelNoPain : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.FeelNoPainPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("CRUELTY")]
public sealed class Cruelty : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CrueltyPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.CrueltyPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("JUGGLING")]
public sealed class Juggling : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.JugglingPower>(),
            1, BlitzLimits.PlayerOwner);
        int index = BlitzKernel.FindPower(x.B, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.JugglingPower>());
        if (index < 0)
            throw new BlitzUnsupportedException("杂耍状态施加后未找到对应 Power。");
        // SimulatedCombatState.InitializeJugglingAfterApplied: attacks started this turn.
        x.B.Powers[index].Aux0 = x.B.Counter(BlitzCounter.AttackPlayStartsThisTurn);
    }
}

[BlitzContent("INFERNO")]
public sealed class Inferno : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["InfernoPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        ushort id = BlitzRegistry.PowerId<Powers.InfernoPower>();
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, id, (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
        int index = BlitzKernel.FindPower(x.B, BlitzLimits.PlayerOwner, id);
        if (index < 0)
            throw new BlitzUnsupportedException("炼狱状态施加后未找到对应 Power。");
        // CardOnPlaySupport: InfernoPower.IncrementSelfDamage.
        x.B.Powers[index].Aux0++;
    }
}

[BlitzContent("STONE_ARMOR")]
public sealed class StoneArmor : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.PlatingPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("RUPTURE")]
public sealed class Rupture : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.RupturePower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("VICIOUS")]
public sealed class Vicious : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.ViciousPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}
