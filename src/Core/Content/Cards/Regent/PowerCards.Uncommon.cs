using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary>
/// <c>BlackHole</c>: CardPowerOnPlaySupport applies BlackHolePower with the "BlackHolePower" variable;
/// the power damages every enemy after a star gain and after a manual card series that spent stars.
/// </summary>
[BlitzContent("BLACK_HOLE")]
public sealed class BlackHole : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["BlackHolePower"];

    public override IReadOnlyList<string> AppliedPowers => ["BLACK_HOLE_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.BlackHolePower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>PillarOfCreation</c>: CardPowerOnPlaySupport applies PillarOfCreationPower with the Block
/// variable; the power grants Unpowered block for every card generated for its owner.
/// </summary>
[BlitzContent("PILLAR_OF_CREATION")]
public sealed class PillarOfCreation : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<string> AppliedPowers => ["PILLAR_OF_CREATION_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.PillarOfCreationPower>(), (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
}

/// <summary>
/// Native <c>Orbit</c>: add an instanced OrbitPower with the Energy variable as its per-trigger
/// amount. Existing engine: <c>CardPowerOnPlaySupport.AddPowerInstance&lt;OrbitPower&gt;</c>.
/// </summary>
[BlitzContent("ORBIT")]
public sealed class Orbit : BlitzCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override IReadOnlyList<string> AppliedPowers => ["ORBIT_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!BlitzRegistry.TryPower("ORBIT_POWER", out ushort power))
            throw new BlitzUnsupportedException("Orbit 需要 ORBIT_POWER 定义。");
        // SimulatedCombatState.AddPowerInstance sets _target = owner (unlike Apply<T>).
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, power, (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner, BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// Native <c>PaleBlueDot</c>: apply <c>Cards</c> stacks of PaleBlueDotPower. A first application
/// initializes the per-turn latch to false; stacking keeps it. Existing engine:
/// <c>CardPowerOnPlaySupport</c>'s PaleBlueDot case and <c>InitializePaleBlueDot</c>.
/// </summary>
[BlitzContent("PALE_BLUE_DOT")]
public sealed class PaleBlueDot : BlitzCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["PALE_BLUE_DOT_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!BlitzRegistry.TryPower("PALE_BLUE_DOT_POWER", out ushort power))
            throw new BlitzUnsupportedException("PaleBlueDot 需要 PALE_BLUE_DOT_POWER 定义。");
        bool alreadyHad = BlitzKernel.GetAmount(x.B, BlitzLimits.PlayerOwner, power) > 0;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, power, (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
        if (x.NeedsChoice || alreadyHad)
            return;
        int row = BlitzKernel.FindLastPower(x.B, BlitzLimits.PlayerOwner, power);
        if (row >= 0)
            x.B.Powers[row].Aux0 = 0;
    }
}

/// <summary><c>Furnace</c>: CardPowerOnPlaySupport applies FurnacePower with the Forge variable.</summary>
[BlitzContent("FURNACE")]
public sealed class Furnace : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override IReadOnlyList<string> AppliedPowers => ["FURNACE_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.FurnacePower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>Parry</c>: CardPowerOnPlaySupport applies ParryPower with the "ParryPower" variable; Sovereign
/// Blade reads the power for its block.
/// </summary>
[BlitzContent("PARRY")]
public sealed class Parry : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["ParryPower"];

    public override IReadOnlyList<string> AppliedPowers => ["PARRY_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.ParryPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

/// <summary><c>ChildOfTheStars</c>: CardPowerOnPlaySupport applies ChildOfTheStarsPower.</summary>
[BlitzContent("CHILD_OF_THE_STARS")]
public sealed class ChildOfTheStars : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["BlockForStars"];

    public override IReadOnlyList<string> AppliedPowers => ["CHILD_OF_THE_STARS_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.ChildOfTheStarsPower>(), (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
}
