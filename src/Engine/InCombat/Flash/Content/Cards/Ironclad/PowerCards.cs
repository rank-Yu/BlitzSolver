namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Ironclad;

// Existing engine paths for this batch:
// - Inflame, FeelNoPain, Juggernaut and Cruelty have no OnPlay mirror; CardPowerOnPlaySupport
//   applies the power and CorePowerSupport falls back to the same cards.
// - FeelNoPainPower, JuggernautPower and CrueltyPower keep their native hooks in M/Hooks/**.
//   Rage and FlameBarrier are not defined because their AfterSideTurnEnd removal has no Flash
//   turn-phase hook. Rupture is not defined because the existing engine records a
//   RupturePredictionState entry on every card play once it is present, which Flash cannot import.

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

[FlashContent("JUGGERNAUT")]
public sealed class Juggernaut : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["JuggernautPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.JuggernautPower>(),
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

// P4 batch: FlameBarrier is a block plus power application; its power is removed by
// CorePowerSupport.TriggerTransientSideTurnEndPowers, which the Flash turn phases copy. Pyre,
// DemonForm and Barricade are pure CardPowerOnPlaySupport / CorePowerSupport cases (no mirror
// half). Rage, Juggling, DarkEmbrace, CrimsonMantle, Inferno, StoneArmor, Unmovable and Aggression
// remain undefined; see the task report for the missing kernel capability of each.

[FlashContent("FLAME_BARRIER")]
public sealed class FlameBarrier : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "DamageBack"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FlameBarrierPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("PYRE")]
public sealed class Pyre : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.PyrePower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("DEMON_FORM")]
public sealed class DemonForm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DemonFormPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("BARRICADE")]
public sealed class Barricade : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.BarricadePower>(),
            1, FlashLimits.PlayerOwner);
}

