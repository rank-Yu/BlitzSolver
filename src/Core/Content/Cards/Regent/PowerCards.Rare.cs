using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary>
/// <c>VoidForm</c>: CardOnPlaySupport.Batch043 applies VoidFormPower with the "VoidFormPower" variable
/// after <c>PrepareVoidFormApplication</c> (an already-active power's per-turn counter is maxed so the
/// new layer does not show free cards during the forced turn end), then ends the player turn.
/// </summary>
[BlitzContent("VOID_FORM")]
public sealed class VoidForm : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["VoidFormPower"];

    public override IReadOnlyList<string> AppliedPowers => ["VOID_FORM_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        ushort power = BlitzRegistry.PowerId<Powers.VoidFormPower>();
        int existing = BlitzKernel.FindPower(x.B, BlitzLimits.PlayerOwner, power);
        if (existing >= 0 && x.B.Powers[existing].Amount > 0)
            x.B.Powers[existing].Aux0 = 999_999_999;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, power, (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        BlitzKernel.RequestPlayerTurnEnd(ref x.B);
    }
}

/// <summary><c>NeutronAegis</c>: CardPowerOnPlaySupport applies PlatingPower.</summary>
[BlitzContent("NEUTRON_AEGIS")]
public sealed class NeutronAegis : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    public override IReadOnlyList<string> AppliedPowers => ["PLATING_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.PlatingPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>Royalties</c>: CardPowerOnPlaySupport applies RoyaltiesPower with the Gold variable and records
/// the same amount as long-term resource value (<c>combat.RecordLongTermResource</c>).
/// </summary>
[BlitzContent("ROYALTIES")]
public sealed class Royalties : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Gold"];

    public override IReadOnlyList<string> AppliedPowers => ["ROYALTIES_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        int gold = (int)BlitzCardEffects.Var(x, 0);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.RoyaltiesPower>(),
            gold, BlitzLimits.PlayerOwner);
        x.B.AddCounter(BlitzCounter.LongTermResourceValue, gold);
        BlitzGrowthRewards.Add(ref x.B, BlitzGrowthSource.Royalties);
    }
}

/// <summary><c>MonarchsGaze</c>: CardPowerOnPlaySupport applies MonarchsGazePower with "StrengthLoss".</summary>
[BlitzContent("MONARCHS_GAZE")]
public sealed class MonarchsGaze : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    public override IReadOnlyList<string> AppliedPowers => ["MONARCHS_GAZE_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.MonarchsGazePower>(), (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>Tyranny</c>: applies TyrannyPower 1 (<c>CardPowerOnPlaySupport.SToZ</c>); the power's exhaust
/// choice runs at the player turn start.
/// </summary>
[BlitzContent("TYRANNY")]
public sealed class Tyranny : BlitzCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["TYRANNY_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.TyrannyPower>(), 1, BlitzLimits.PlayerOwner);
}

/// <summary>
/// Native <c>SwordSage</c>: apply SwordSagePower. The power's per-blade replay normalization is in
/// <see cref="BlitzKernel.NormalizeCardAfflictions"/>; existing engine
/// <c>CardPowerOnPlaySupport.SToZ</c>'s SwordSage case.
/// </summary>
[BlitzContent("SWORD_SAGE")]
public sealed class SwordSage : BlitzCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["SwordSagePower"];

    public override IReadOnlyList<string> AppliedPowers => ["SWORD_SAGE_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!BlitzRegistry.TryPower("SWORD_SAGE_POWER", out ushort power))
            throw new BlitzUnsupportedException("SwordSage 需要 SWORD_SAGE_POWER 定义。");
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, power, (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>SeekingEdge</c>: CardPowerOnPlaySupport's late pass applies SeekingEdgePower 1 before
/// CardOnPlaySupport's SeekingEdge case forges. The Forge's generated Sovereign Blade triggers
/// ArsenalPower, so the power row order is observable.
/// </summary>
[BlitzContent("SEEKING_EDGE")]
public sealed class SeekingEdge : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override IReadOnlyList<string> AppliedPowers => ["SEEKING_EDGE_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        // CardPowerOnPlaySupport.ApplyLate runs SeekingEdgePower before CardOnPlaySupport's SeekingEdge
        // case forges. The Forge may generate a Sovereign Blade, whose AfterCardGeneratedForCombat
        // dispatch (ArsenalPower) applies Strength; the power row order is part of the state.
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.SeekingEdgePower>(), 1,
            BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Forge(ref x, (int)BlitzCardEffects.Var(x, 0));
    }
}

/// <summary><c>Genesis</c>: CardPowerOnPlaySupport applies GenesisPower with "StarsPerTurn".</summary>
[BlitzContent("GENESIS")]
public sealed class Genesis : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StarsPerTurn"];

    public override IReadOnlyList<string> AppliedPowers => ["GENESIS_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.GenesisPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

/// <summary><c>Arsenal</c>: CardPowerOnPlaySupport applies ArsenalPower with "ArsenalPower".</summary>
[BlitzContent("ARSENAL")]
public sealed class Arsenal : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["ArsenalPower"];

    public override IReadOnlyList<string> AppliedPowers => ["ARSENAL_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.ArsenalPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}
