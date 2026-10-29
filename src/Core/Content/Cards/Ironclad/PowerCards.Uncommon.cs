using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Cards.Ironclad;

[ContentId("STAMPEDE")]
public sealed class Stampede : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.StampedePower>(), (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
}

[ContentId("INFLAME")]
public sealed class Inflame : CardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StrengthPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("FEEL_NO_PAIN")]
public sealed class FeelNoPain : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.FeelNoPainPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("CRUELTY")]
public sealed class Cruelty : CardDef
{
    public override IReadOnlyList<string> VarNames => ["CrueltyPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.CrueltyPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("JUGGLING")]
public sealed class Juggling : CardDef
{
    public override void OnPlay(ref Exec x)
    {
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.JugglingPower>(),
            1, Limits.PlayerOwner);
        int index = Kernel.FindPower(x.B, Limits.PlayerOwner, Registry.PowerId<Powers.JugglingPower>());
        if (index < 0)
            throw new UnsupportedException("杂耍状态施加后未找到对应 Power。");
        // SimulatedCombatState.InitializeJugglingAfterApplied: attacks started this turn.
        x.B.Powers[index].Aux0 = x.B.Counter(Counter.AttackPlayStartsThisTurn);
    }
}

[ContentId("INFERNO")]
public sealed class Inferno : CardDef
{
    public override IReadOnlyList<string> VarNames => ["InfernoPower"];

    public override void OnPlay(ref Exec x)
    {
        ushort id = Registry.PowerId<Powers.InfernoPower>();
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, id, (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
        int index = Kernel.FindPower(x.B, Limits.PlayerOwner, id);
        if (index < 0)
            throw new UnsupportedException("炼狱状态施加后未找到对应 Power。");
        // CardOnPlaySupport: InfernoPower.IncrementSelfDamage.
        x.B.Powers[index].Aux0++;
    }
}

[ContentId("STONE_ARMOR")]
public sealed class StoneArmor : CardDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.PlatingPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("RUPTURE")]
public sealed class Rupture : CardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.RupturePower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("VICIOUS")]
public sealed class Vicious : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.ViciousPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}
