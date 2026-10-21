using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

/// <summary>
/// <c>BlackHole</c>: CardPowerOnPlaySupport applies BlackHolePower with the "BlackHolePower" variable;
/// the power damages every enemy after a star gain and after a manual card series that spent stars.
/// </summary>
[FlashContent("BLACK_HOLE")]
public sealed class BlackHole : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["BlackHolePower"];

    public override IReadOnlyList<string> AppliedPowers => ["BLACK_HOLE_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.BlackHolePower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>PillarOfCreation</c>: CardPowerOnPlaySupport applies PillarOfCreationPower with the Block
/// variable; the power grants Unpowered block for every card generated for its owner.
/// </summary>
[FlashContent("PILLAR_OF_CREATION")]
public sealed class PillarOfCreation : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<string> AppliedPowers => ["PILLAR_OF_CREATION_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.PillarOfCreationPower>(), (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
}

/// <summary>
/// Native <c>Orbit</c>: add an instanced OrbitPower with the Energy variable as its per-trigger
/// amount. Existing engine: <c>CardPowerOnPlaySupport.AddPowerInstance&lt;OrbitPower&gt;</c>.
/// </summary>
[FlashContent("ORBIT")]
public sealed class Orbit : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override IReadOnlyList<string> AppliedPowers => ["ORBIT_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryPower("ORBIT_POWER", out ushort power))
            throw new FlashUnsupportedException("Orbit 需要 ORBIT_POWER 定义。");
        // SimulatedCombatState.AddPowerInstance sets _target = owner (unlike Apply<T>).
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner, FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// Native <c>PaleBlueDot</c>: apply <c>Cards</c> stacks of PaleBlueDotPower. A first application
/// initializes the per-turn latch to false; stacking keeps it. Existing engine:
/// <c>CardPowerOnPlaySupport</c>'s PaleBlueDot case and <c>InitializePaleBlueDot</c>.
/// </summary>
[FlashContent("PALE_BLUE_DOT")]
public sealed class PaleBlueDot : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["PALE_BLUE_DOT_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryPower("PALE_BLUE_DOT_POWER", out ushort power))
            throw new FlashUnsupportedException("PaleBlueDot 需要 PALE_BLUE_DOT_POWER 定义。");
        bool alreadyHad = FlashKernel.GetAmount(x.B, FlashLimits.PlayerOwner, power) > 0;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice || alreadyHad)
            return;
        int row = FlashKernel.FindLastPower(x.B, FlashLimits.PlayerOwner, power);
        if (row >= 0)
            x.B.Powers[row].Aux0 = 0;
    }
}

/// <summary><c>Furnace</c>: CardPowerOnPlaySupport applies FurnacePower with the Forge variable.</summary>
[FlashContent("FURNACE")]
public sealed class Furnace : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override IReadOnlyList<string> AppliedPowers => ["FURNACE_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FurnacePower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>Parry</c>: CardPowerOnPlaySupport applies ParryPower with the "ParryPower" variable; Sovereign
/// Blade reads the power for its block.
/// </summary>
[FlashContent("PARRY")]
public sealed class Parry : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["ParryPower"];

    public override IReadOnlyList<string> AppliedPowers => ["PARRY_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ParryPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>ChildOfTheStars</c>: CardPowerOnPlaySupport applies ChildOfTheStarsPower.</summary>
[FlashContent("CHILD_OF_THE_STARS")]
public sealed class ChildOfTheStars : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["BlockForStars"];

    public override IReadOnlyList<string> AppliedPowers => ["CHILD_OF_THE_STARS_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.ChildOfTheStarsPower>(), (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
}
