using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[BlitzContent("HAILSTORM")]
public sealed class Hailstorm : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["HailstormPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.HailstormPower>(), 0);
}

[BlitzContent("LOOP")]
public sealed class Loop : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Loop"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.LoopPower>(), 0);
}

[BlitzContent("STORM")]
public sealed class Storm : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StormPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.StormPower>(), 0);
}

[BlitzContent("ITERATION")]
public sealed class Iteration : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["IterationPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.IterationPower>(), 0);
}

[BlitzContent("FERAL")]
public sealed class Feral : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["FeralPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        int amount = (int)BlitzCardEffects.Var(x, 0);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.FeralPower>(), amount,
            BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        // SimulatedCombatState.InitializeFeralAfterApplied: the new instance starts from the
        // zero-cost attacks already played this turn.
        int index = BlitzKernel.FindPower(x.B, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.FeralPower>());
        if (index < 0)
            return;
        x.B.Powers[index].Aux0 = x.B.Counter(BlitzCounter.ZeroCostAttackStartsThisTurn);
    }
}

[BlitzContent("THUNDER")]
public sealed class Thunder : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["ThunderPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.ThunderPower>(), 0);
}

[BlitzContent("SMOKESTACK")]
public sealed class Smokestack : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["SmokestackPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.SmokestackPower>(), 0);
}

[BlitzContent("SUBROUTINE")]
public sealed class Subroutine : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.SubroutinePower>(), 1, BlitzLimits.PlayerOwner);
}

[BlitzContent("BULK_UP")]
public sealed class BulkUp : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["OrbSlots", "StrengthPower", "DexterityPower"];

    // CorePowerSupport.BulkUp: remove slots, then Strength and Dexterity.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.RemoveOrbSlots(ref x.B, (int)BlitzCardEffects.Var(x, 0));
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.StrengthPower>(), 1);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.DexterityPower>(), 2);
    }
}

[BlitzContent("CAPACITOR")]
public sealed class Capacitor : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.AddOrbSlots(ref x.B, (int)BlitzCardEffects.Var(x, 0));
}
