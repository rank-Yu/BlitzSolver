using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Colorless;

[ContentId("ETERNAL_ARMOR")]
public sealed class EternalArmor : CardDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.PlatingPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("FASTEN")]
public sealed class Fasten : CardDef
{
    public override IReadOnlyList<string> VarNames => ["ExtraBlock"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.FastenPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("PREP_TIME")]
public sealed class PrepTime : CardDef
{
    public override IReadOnlyList<string> VarNames => ["PrepTimePower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.PrepTimePower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("PROWESS")]
public sealed class Prowess : CardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    public override void OnPlay(ref Exec x)
    {
        // CorePowerSupport `case Prowess`: Strength first, then Dexterity, both on the owner.
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StrengthPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.DexterityPower>(),
            (int)CardEffects.Var(x, 1), Limits.PlayerOwner);
    }
}

[ContentId("AUTOMATION")]
public sealed class Automation : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref Exec x)
    {
        ushort id = Registry.PowerId<Powers.AutomationPower>();
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, id, (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int index = Kernel.FindLastPower(x.B, Limits.PlayerOwner, id);
        if (index >= 0)
            x.B.Powers[index].Aux0 = 10;
    }
}

[ContentId("PANACHE")]
public sealed class Panache : CardDef
{
    public override IReadOnlyList<string> VarNames => ["PanacheDamage"];

    public override void OnPlay(ref Exec x)
    {
        ushort id = Registry.PowerId<Powers.PanachePower>();
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, id, (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int index = Kernel.FindLastPower(x.B, Limits.PlayerOwner, id);
        if (index >= 0)
            x.B.Powers[index].Aux0 = 5;
    }
}

[ContentId("ROLLING_BOULDER")]
public sealed class RollingBoulder : CardDef
{
    public override IReadOnlyList<string> VarNames => ["RollingBoulderPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.RollingBoulderPower>(), (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
}

[ContentId("NOSTALGIA")]
public sealed class Nostalgia : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.NostalgiaPower>(), 1, Limits.PlayerOwner);
}

[ContentId("STRATAGEM")]
public sealed class Stratagem : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.StratagemPower>(), 1, Limits.PlayerOwner);
}

/// <summary>
/// Native <c>Mayhem</c> applies its power, whose effect is <c>AfterAutoPrePlayPhaseEntered</c>. The
/// existing engine has no mirror for that hook (HookMirrors only dispatches the PostPlay phase), so
/// Mayhem intentionally does nothing in this content set; the definition exists so the ColorlessPotion
/// generation pool stays usable, and the kernel matches the authoritative engine.
/// </summary>
[ContentId("MAYHEM")]
public sealed class Mayhem : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.MayhemPower>(), 1, Limits.PlayerOwner);
}

[ContentId("ENTROPY")]
public sealed class Entropy : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["ENTROPY_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.EntropyPower>(), (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
}
