using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Colorless;

[BlitzContent("ETERNAL_ARMOR")]
public sealed class EternalArmor : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.PlatingPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("FASTEN")]
public sealed class Fasten : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["ExtraBlock"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.FastenPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("PREP_TIME")]
public sealed class PrepTime : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["PrepTimePower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.PrepTimePower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("PROWESS")]
public sealed class Prowess : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        // CorePowerSupport `case Prowess`: Strength first, then Dexterity, both on the owner.
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.DexterityPower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("AUTOMATION")]
public sealed class Automation : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref BlitzExec x)
    {
        ushort id = BlitzRegistry.PowerId<Powers.AutomationPower>();
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, id, (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int index = BlitzKernel.FindLastPower(x.B, BlitzLimits.PlayerOwner, id);
        if (index >= 0)
            x.B.Powers[index].Aux0 = 10;
    }
}

[BlitzContent("PANACHE")]
public sealed class Panache : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["PanacheDamage"];

    public override void OnPlay(ref BlitzExec x)
    {
        ushort id = BlitzRegistry.PowerId<Powers.PanachePower>();
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, id, (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int index = BlitzKernel.FindLastPower(x.B, BlitzLimits.PlayerOwner, id);
        if (index >= 0)
        {
            // SimulatedCombatState.AddPowerInstance sets _target = owner (unlike Apply<T>).
            x.B.Powers[index].Target = BlitzLimits.PlayerOwner;
            x.B.Powers[index].Aux0 = 5;
        }
    }
}

[BlitzContent("ROLLING_BOULDER")]
public sealed class RollingBoulder : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["RollingBoulderPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.RollingBoulderPower>(), (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
}

[BlitzContent("NOSTALGIA")]
public sealed class Nostalgia : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.NostalgiaPower>(), 1, BlitzLimits.PlayerOwner);
}

[BlitzContent("STRATAGEM")]
public sealed class Stratagem : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.StratagemPower>(), 1, BlitzLimits.PlayerOwner);
}

/// <summary>
/// Native <c>Mayhem</c> applies its power, whose effect is <c>AfterAutoPrePlayPhaseEntered</c>. The
/// existing engine has no mirror for that hook (HookMirrors only dispatches the PostPlay phase), so
/// Mayhem intentionally does nothing in this content set; the definition exists so the ColorlessPotion
/// generation pool stays usable, and the kernel matches the authoritative engine.
/// </summary>
[BlitzContent("MAYHEM")]
public sealed class Mayhem : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.MayhemPower>(), 1, BlitzLimits.PlayerOwner);
}

[BlitzContent("ENTROPY")]
public sealed class Entropy : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["ENTROPY_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.EntropyPower>(), (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
}
