using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

/// <summary>
/// <c>VoidForm</c>: CardOnPlaySupport.Batch043 applies VoidFormPower with the "VoidFormPower" variable
/// after <c>PrepareVoidFormApplication</c> (an already-active power's per-turn counter is maxed so the
/// new layer does not show free cards during the forced turn end), then ends the player turn.
/// </summary>
[FlashContent("VOID_FORM")]
public sealed class VoidForm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["VoidFormPower"];

    public override IReadOnlyList<string> AppliedPowers => ["VOID_FORM_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        ushort power = FlashRegistry.PowerId<Powers.VoidFormPower>();
        int existing = FlashKernel.FindPower(x.B, FlashLimits.PlayerOwner, power);
        if (existing >= 0 && x.B.Powers[existing].Amount > 0)
            x.B.Powers[existing].Aux0 = 999_999_999;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        FlashKernel.RequestPlayerTurnEnd(ref x.B);
    }
}

/// <summary><c>NeutronAegis</c>: CardPowerOnPlaySupport applies PlatingPower.</summary>
[FlashContent("NEUTRON_AEGIS")]
public sealed class NeutronAegis : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    public override IReadOnlyList<string> AppliedPowers => ["PLATING_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.PlatingPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>Royalties</c>: CardPowerOnPlaySupport applies RoyaltiesPower with the Gold variable and records
/// the same amount as long-term resource value (<c>combat.RecordLongTermResource</c>).
/// </summary>
[FlashContent("ROYALTIES")]
public sealed class Royalties : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Gold"];

    public override IReadOnlyList<string> AppliedPowers => ["ROYALTIES_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        int gold = (int)FlashCardEffects.Var(x, 0);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RoyaltiesPower>(),
            gold, FlashLimits.PlayerOwner);
        x.B.AddCounter(FlashCounter.LongTermResourceValue, gold);
        FlashGrowthRewards.Add(ref x.B, FlashGrowthSource.Royalties);
    }
}

/// <summary><c>MonarchsGaze</c>: CardPowerOnPlaySupport applies MonarchsGazePower with "StrengthLoss".</summary>
[FlashContent("MONARCHS_GAZE")]
public sealed class MonarchsGaze : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    public override IReadOnlyList<string> AppliedPowers => ["MONARCHS_GAZE_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.MonarchsGazePower>(), (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>Tyranny</c>: applies TyrannyPower 1 (<c>CardPowerOnPlaySupport.SToZ</c>); the power's exhaust
/// choice runs at the player turn start.
/// </summary>
[FlashContent("TYRANNY")]
public sealed class Tyranny : FlashCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["TYRANNY_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.TyrannyPower>(), 1, FlashLimits.PlayerOwner);
}

/// <summary>
/// Native <c>SwordSage</c>: apply SwordSagePower. The power's per-blade replay normalization is in
/// <see cref="FlashKernel.NormalizeCardAfflictions"/>; existing engine
/// <c>CardPowerOnPlaySupport.SToZ</c>'s SwordSage case.
/// </summary>
[FlashContent("SWORD_SAGE")]
public sealed class SwordSage : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["SwordSagePower"];

    public override IReadOnlyList<string> AppliedPowers => ["SWORD_SAGE_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryPower("SWORD_SAGE_POWER", out ushort power))
            throw new FlashUnsupportedException("SwordSage 需要 SWORD_SAGE_POWER 定义。");
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>SeekingEdge</c>: CardPowerOnPlaySupport's late pass applies SeekingEdgePower 1 before
/// CardOnPlaySupport's SeekingEdge case forges. The Forge's generated Sovereign Blade triggers
/// ArsenalPower, so the power row order is observable.
/// </summary>
[FlashContent("SEEKING_EDGE")]
public sealed class SeekingEdge : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override IReadOnlyList<string> AppliedPowers => ["SEEKING_EDGE_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        // CardPowerOnPlaySupport.ApplyLate runs SeekingEdgePower before CardOnPlaySupport's SeekingEdge
        // case forges. The Forge may generate a Sovereign Blade, whose AfterCardGeneratedForCombat
        // dispatch (ArsenalPower) applies Strength; the power row order is part of the state.
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.SeekingEdgePower>(), 1,
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 0));
    }
}

/// <summary><c>Genesis</c>: CardPowerOnPlaySupport applies GenesisPower with "StarsPerTurn".</summary>
[FlashContent("GENESIS")]
public sealed class Genesis : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StarsPerTurn"];

    public override IReadOnlyList<string> AppliedPowers => ["GENESIS_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.GenesisPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>Arsenal</c>: CardPowerOnPlaySupport applies ArsenalPower with "ArsenalPower".</summary>
[FlashContent("ARSENAL")]
public sealed class Arsenal : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["ArsenalPower"];

    public override IReadOnlyList<string> AppliedPowers => ["ARSENAL_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ArsenalPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}
