using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Colorless;

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

[FlashContent("AUTOMATION")]
public sealed class Automation : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        ushort id = FlashRegistry.PowerId<Powers.AutomationPower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, id, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int index = FlashKernel.FindLastPower(x.B, FlashLimits.PlayerOwner, id);
        if (index >= 0)
            x.B.Powers[index].Aux0 = 10;
    }
}

[FlashContent("PANACHE")]
public sealed class Panache : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PanacheDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        ushort id = FlashRegistry.PowerId<Powers.PanachePower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, id, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int index = FlashKernel.FindLastPower(x.B, FlashLimits.PlayerOwner, id);
        if (index >= 0)
        {
            // SimulatedCombatState.AddPowerInstance sets _target = owner (unlike Apply<T>).
            x.B.Powers[index].Target = FlashLimits.PlayerOwner;
            x.B.Powers[index].Aux0 = 5;
        }
    }
}

[FlashContent("ROLLING_BOULDER")]
public sealed class RollingBoulder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["RollingBoulderPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.RollingBoulderPower>(), (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
}

[FlashContent("NOSTALGIA")]
public sealed class Nostalgia : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.NostalgiaPower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("STRATAGEM")]
public sealed class Stratagem : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.StratagemPower>(), 1, FlashLimits.PlayerOwner);
}

/// <summary>
/// Native <c>Mayhem</c> applies its power, whose effect is <c>AfterAutoPrePlayPhaseEntered</c>. The
/// existing engine has no mirror for that hook (HookMirrors only dispatches the PostPlay phase), so
/// Mayhem intentionally does nothing in this content set; the definition exists so the ColorlessPotion
/// generation pool stays usable, and the kernel matches the authoritative engine.
/// </summary>
[FlashContent("MAYHEM")]
public sealed class Mayhem : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.MayhemPower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("ENTROPY")]
public sealed class Entropy : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["ENTROPY_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.EntropyPower>(), (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
}
