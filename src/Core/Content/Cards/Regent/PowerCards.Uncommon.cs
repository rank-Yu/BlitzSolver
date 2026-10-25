using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary>
/// <c>BlackHole</c>: CardPowerOnPlaySupport applies BlackHolePower with the "BlackHolePower" variable;
/// the power damages every enemy after a star gain and after a manual card series that spent stars.
/// </summary>
[ContentId("BLACK_HOLE")]
public sealed class BlackHole : CardDef
{
    public override IReadOnlyList<string> VarNames => ["BlackHolePower"];

    public override IReadOnlyList<string> AppliedPowers => ["BLACK_HOLE_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.BlackHolePower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

/// <summary>
/// <c>PillarOfCreation</c>: CardPowerOnPlaySupport applies PillarOfCreationPower with the Block
/// variable; the power grants Unpowered block for every card generated for its owner.
/// </summary>
[ContentId("PILLAR_OF_CREATION")]
public sealed class PillarOfCreation : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<string> AppliedPowers => ["PILLAR_OF_CREATION_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.PillarOfCreationPower>(), (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
}

/// <summary>
/// Native <c>Orbit</c>: add an instanced OrbitPower with the Energy variable as its per-trigger
/// amount. Existing engine: <c>CardPowerOnPlaySupport.AddPowerInstance&lt;OrbitPower&gt;</c>.
/// </summary>
[ContentId("ORBIT")]
public sealed class Orbit : CardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override IReadOnlyList<string> AppliedPowers => ["ORBIT_POWER"];

    public override void OnPlay(ref Exec x)
    {
        if (!Registry.TryPower("ORBIT_POWER", out ushort power))
            throw new UnsupportedException("Orbit 需要 ORBIT_POWER 定义。");
        // SimulatedCombatState.AddPowerInstance sets _target = owner (unlike Apply<T>).
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, power, (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner, Limits.PlayerOwner);
    }
}

/// <summary>
/// Native <c>PaleBlueDot</c>: apply <c>Cards</c> stacks of PaleBlueDotPower. A first application
/// initializes the per-turn latch to false; stacking keeps it. Existing engine:
/// <c>CardPowerOnPlaySupport</c>'s PaleBlueDot case and <c>InitializePaleBlueDot</c>.
/// </summary>
[ContentId("PALE_BLUE_DOT")]
public sealed class PaleBlueDot : CardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["PALE_BLUE_DOT_POWER"];

    public override void OnPlay(ref Exec x)
    {
        if (!Registry.TryPower("PALE_BLUE_DOT_POWER", out ushort power))
            throw new UnsupportedException("PaleBlueDot 需要 PALE_BLUE_DOT_POWER 定义。");
        bool alreadyHad = Kernel.GetAmount(x.B, Limits.PlayerOwner, power) > 0;
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, power, (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
        if (x.NeedsChoice || alreadyHad)
            return;
        int row = Kernel.FindLastPower(x.B, Limits.PlayerOwner, power);
        if (row >= 0)
            x.B.Powers[row].Aux0 = 0;
    }
}

/// <summary><c>Furnace</c>: CardPowerOnPlaySupport applies FurnacePower with the Forge variable.</summary>
[ContentId("FURNACE")]
public sealed class Furnace : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override IReadOnlyList<string> AppliedPowers => ["FURNACE_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.FurnacePower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

/// <summary>
/// <c>Parry</c>: CardPowerOnPlaySupport applies ParryPower with the "ParryPower" variable; Sovereign
/// Blade reads the power for its block.
/// </summary>
[ContentId("PARRY")]
public sealed class Parry : CardDef
{
    public override IReadOnlyList<string> VarNames => ["ParryPower"];

    public override IReadOnlyList<string> AppliedPowers => ["PARRY_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.ParryPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

/// <summary><c>ChildOfTheStars</c>: CardPowerOnPlaySupport applies ChildOfTheStarsPower.</summary>
[ContentId("CHILD_OF_THE_STARS")]
public sealed class ChildOfTheStars : CardDef
{
    public override IReadOnlyList<string> VarNames => ["BlockForStars"];

    public override IReadOnlyList<string> AppliedPowers => ["CHILD_OF_THE_STARS_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.ChildOfTheStarsPower>(), (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
}
