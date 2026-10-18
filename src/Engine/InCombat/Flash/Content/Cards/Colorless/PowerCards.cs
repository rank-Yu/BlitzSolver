namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Colorless;

// Colorless power cards. Existing engine paths for this batch:
// - EternalArmor, Fasten, PrepTime and Prowess are CardPowerOnPlaySupport cases; the applied powers
//   keep their native hooks in M/Hooks/** (the Flash definitions live in Content/Powers).
// Power cards whose native power is Instanced (Automation, Panache, RollingBoulder, TheBomb) are not
// defined: the kernel's power table has no per-instance identity, so two copies collapse in
// FlashKernel.FindPower / FlashHooks.TryResolve. See the task report's skip list.

[FlashContent("ETERNAL_ARMOR")]
public sealed class EternalArmor : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.PlatingPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("FASTEN")]
public sealed class Fasten : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["ExtraBlock"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FastenPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("PREP_TIME")]
public sealed class PrepTime : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PrepTimePower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.PrepTimePower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("PROWESS")]
public sealed class Prowess : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    public override void OnPlay(ref FlashExec x)
    {
        // CorePowerSupport `case Prowess`: Strength first, then Dexterity, both on the owner.
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DexterityPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}
