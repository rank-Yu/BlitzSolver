using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary>
/// <c>VoidForm</c>: CardOnPlaySupport.Batch043 applies VoidFormPower with the "VoidFormPower" variable
/// after <c>PrepareVoidFormApplication</c> (an already-active power's per-turn counter is maxed so the
/// new layer does not show free cards during the forced turn end), then ends the player turn.
/// </summary>
[ContentId("VOID_FORM")]
public sealed class VoidForm : CardDef
{
    public override IReadOnlyList<string> VarNames => ["VoidFormPower"];

    public override IReadOnlyList<string> AppliedPowers => ["VOID_FORM_POWER"];

    public override void OnPlay(ref Exec x)
    {
        ushort power = Registry.PowerId<Powers.VoidFormPower>();
        int existing = Kernel.FindPower(x.B, Limits.PlayerOwner, power);
        if (existing >= 0 && x.B.Powers[existing].Amount > 0)
            x.B.Powers[existing].Aux0 = 999_999_999;
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, power, (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        Kernel.RequestPlayerTurnEnd(ref x.B);
    }
}

/// <summary><c>NeutronAegis</c>: CardPowerOnPlaySupport applies PlatingPower.</summary>
[ContentId("NEUTRON_AEGIS")]
public sealed class NeutronAegis : CardDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    public override IReadOnlyList<string> AppliedPowers => ["PLATING_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.PlatingPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

/// <summary>
/// <c>Royalties</c>: CardPowerOnPlaySupport applies RoyaltiesPower with the Gold variable and records
/// the same amount as long-term resource value (<c>combat.RecordLongTermResource</c>).
/// </summary>
[ContentId("ROYALTIES")]
public sealed class Royalties : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Gold"];

    public override IReadOnlyList<string> AppliedPowers => ["ROYALTIES_POWER"];

    public override void OnPlay(ref Exec x)
    {
        int gold = (int)CardEffects.Var(x, 0);
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.RoyaltiesPower>(),
            gold, Limits.PlayerOwner);
        x.B.AddCounter(Counter.LongTermResourceValue, gold);
        GrowthRewards.Add(ref x.B, GrowthRewardSource.Royalties);
    }
}

/// <summary><c>MonarchsGaze</c>: CardPowerOnPlaySupport applies MonarchsGazePower with "StrengthLoss".</summary>
[ContentId("MONARCHS_GAZE")]
public sealed class MonarchsGaze : CardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    public override IReadOnlyList<string> AppliedPowers => ["MONARCHS_GAZE_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.MonarchsGazePower>(), (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
}

/// <summary>
/// <c>Tyranny</c>: applies TyrannyPower 1 (<c>CardPowerOnPlaySupport.SToZ</c>); the power's exhaust
/// choice runs at the player turn start.
/// </summary>
[ContentId("TYRANNY")]
public sealed class Tyranny : CardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["TYRANNY_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.TyrannyPower>(), 1, Limits.PlayerOwner);
}

/// <summary>
/// Native <c>SwordSage</c>: apply SwordSagePower. The power's per-blade replay normalization is in
/// <see cref="Kernel.NormalizeCardAfflictions"/>; existing engine
/// <c>CardPowerOnPlaySupport.SToZ</c>'s SwordSage case.
/// </summary>
[ContentId("SWORD_SAGE")]
public sealed class SwordSage : CardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["SwordSagePower"];

    public override IReadOnlyList<string> AppliedPowers => ["SWORD_SAGE_POWER"];

    public override void OnPlay(ref Exec x)
    {
        if (!Registry.TryPower("SWORD_SAGE_POWER", out ushort power))
            throw new UnsupportedException("SwordSage 需要 SWORD_SAGE_POWER 定义。");
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, power, (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
    }
}

/// <summary>
/// <c>SeekingEdge</c>: CardPowerOnPlaySupport's late pass applies SeekingEdgePower 1 before
/// CardOnPlaySupport's SeekingEdge case forges. The Forge's generated Sovereign Blade triggers
/// ArsenalPower, so the power row order is observable.
/// </summary>
[ContentId("SEEKING_EDGE")]
public sealed class SeekingEdge : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override IReadOnlyList<string> AppliedPowers => ["SEEKING_EDGE_POWER"];

    public override void OnPlay(ref Exec x)
    {
        // CardPowerOnPlaySupport.ApplyLate runs SeekingEdgePower before CardOnPlaySupport's SeekingEdge
        // case forges. The Forge may generate a Sovereign Blade, whose AfterCardGeneratedForCombat
        // dispatch (ArsenalPower) applies Strength; the power row order is part of the state.
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.SeekingEdgePower>(), 1,
            Limits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        Kernel.Forge(ref x, (int)CardEffects.Var(x, 0));
    }
}

/// <summary><c>Genesis</c>: CardPowerOnPlaySupport applies GenesisPower with "StarsPerTurn".</summary>
[ContentId("GENESIS")]
public sealed class Genesis : CardDef
{
    public override IReadOnlyList<string> VarNames => ["StarsPerTurn"];

    public override IReadOnlyList<string> AppliedPowers => ["GENESIS_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.GenesisPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

/// <summary><c>Arsenal</c>: CardPowerOnPlaySupport applies ArsenalPower with "ArsenalPower".</summary>
[ContentId("ARSENAL")]
public sealed class Arsenal : CardDef
{
    public override IReadOnlyList<string> VarNames => ["ArsenalPower"];

    public override IReadOnlyList<string> AppliedPowers => ["ARSENAL_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.ArsenalPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}
