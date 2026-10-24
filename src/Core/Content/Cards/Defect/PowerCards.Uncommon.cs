using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Defect;

[FlashContent("HAILSTORM")]
public sealed class Hailstorm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HailstormPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.HailstormPower>(), 0);
}

[FlashContent("LOOP")]
public sealed class Loop : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Loop"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.LoopPower>(), 0);
}

[FlashContent("STORM")]
public sealed class Storm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StormPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.StormPower>(), 0);
}

[FlashContent("ITERATION")]
public sealed class Iteration : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["IterationPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.IterationPower>(), 0);
}

[FlashContent("FERAL")]
public sealed class Feral : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["FeralPower"];

    public override void OnPlay(ref FlashExec x)
    {
        int amount = (int)FlashCardEffects.Var(x, 0);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FeralPower>(), amount,
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        // SimulatedCombatState.InitializeFeralAfterApplied: the new instance starts from the
        // zero-cost attacks already played this turn.
        int index = FlashKernel.FindPower(x.B, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FeralPower>());
        if (index < 0)
            return;
        x.B.Powers[index].Aux0 = x.B.Counter(FlashCounter.ZeroCostAttackStartsThisTurn);
    }
}

[FlashContent("THUNDER")]
public sealed class Thunder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["ThunderPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.ThunderPower>(), 0);
}

[FlashContent("SMOKESTACK")]
public sealed class Smokestack : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SmokestackPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.SmokestackPower>(), 0);
}

[FlashContent("SUBROUTINE")]
public sealed class Subroutine : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.SubroutinePower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("BULK_UP")]
public sealed class BulkUp : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OrbSlots", "StrengthPower", "DexterityPower"];

    // CorePowerSupport.BulkUp: remove slots, then Strength and Dexterity.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.RemoveOrbSlots(ref x.B, (int)FlashCardEffects.Var(x, 0));
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.StrengthPower>(), 1);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.DexterityPower>(), 2);
    }
}

[FlashContent("CAPACITOR")]
public sealed class Capacitor : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.AddOrbSlots(ref x.B, (int)FlashCardEffects.Var(x, 0));
}
