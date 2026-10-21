using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Ironclad;

[FlashContent("STAMPEDE")]
public sealed class Stampede : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.StampedePower>(), (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
}

[FlashContent("INFLAME")]
public sealed class Inflame : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("FEEL_NO_PAIN")]
public sealed class FeelNoPain : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FeelNoPainPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("CRUELTY")]
public sealed class Cruelty : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CrueltyPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.CrueltyPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("JUGGLING")]
public sealed class Juggling : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.JugglingPower>(),
            1, FlashLimits.PlayerOwner);
        int index = FlashKernel.FindPower(x.B, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.JugglingPower>());
        if (index < 0)
            throw new FlashUnsupportedException("杂耍状态施加后未找到对应 Power。");
        // SimulatedCombatState.InitializeJugglingAfterApplied: attacks started this turn.
        x.B.Powers[index].Aux0 = x.B.Counter(FlashCounter.AttackPlayStartsThisTurn);
    }
}

[FlashContent("INFERNO")]
public sealed class Inferno : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["InfernoPower"];

    public override void OnPlay(ref FlashExec x)
    {
        ushort id = FlashRegistry.PowerId<Powers.InfernoPower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, id, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        int index = FlashKernel.FindPower(x.B, FlashLimits.PlayerOwner, id);
        if (index < 0)
            throw new FlashUnsupportedException("炼狱状态施加后未找到对应 Power。");
        // CardOnPlaySupport: InfernoPower.IncrementSelfDamage.
        x.B.Powers[index].Aux0++;
    }
}

[FlashContent("STONE_ARMOR")]
public sealed class StoneArmor : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.PlatingPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("RUPTURE")]
public sealed class Rupture : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RupturePower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("VICIOUS")]
public sealed class Vicious : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ViciousPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}
