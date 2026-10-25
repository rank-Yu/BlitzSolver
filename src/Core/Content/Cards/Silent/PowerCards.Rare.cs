using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

[BlitzContent("ENVENOM")]
public sealed class Envenom : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["EnvenomPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.EnvenomPower>(), 0);
}

[BlitzContent("FAN_OF_KNIVES")]
public sealed class FanOfKnives : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Shivs"];

    public override void OnPlay(ref BlitzExec x)
    {
        // CardPowerOnPlaySupport then CardPileOnPlaySupport: the power first, then Shivs Shivs
        // (the new power makes those Shivs target all enemies when played).
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.FanOfKnivesPower>(), 1, BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)BlitzCardEffects.Var(x, 0), upgraded: false);
    }
}

[BlitzContent("AFTERIMAGE")]
public sealed class Afterimage : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["AfterimagePower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.AfterimagePower>(), 0);
}

[BlitzContent("MASTER_PLANNER")]
public sealed class MasterPlanner : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.MasterPlannerPower>(), 1, BlitzLimits.PlayerOwner);
}

[BlitzContent("WELL_LAID_PLANS")]
public sealed class WellLaidPlans : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.WellLaidPlansPower>(), 1, BlitzLimits.PlayerOwner);
}

[BlitzContent("TRACKING")]
public sealed class Tracking : BlitzCardDef
{
    // CardOnPlaySupport.Batch042.Tracking: TrackingPower 50.
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.TrackingPower>(), 50, BlitzLimits.PlayerOwner);
}

[BlitzContent("ABRASIVE")]
public sealed class Abrasive : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["ThornsPower", "DexterityPower"];

    // CorePowerSupport.Abrasive: Dexterity then Thorns (the canonical variable order is Thorns,
    // Dexterity).
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.DexterityPower>(), 1);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.ThornsPower>(), 0);
    }
}

[BlitzContent("TOOLS_OF_THE_TRADE")]
public sealed class ToolsOfTheTrade : BlitzCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["TOOLS_OF_THE_TRADE_POWER"];

    // CardPowerOnPlaySupport.SToZ: combat.Apply<ToolsOfTheTradePower>(owner, 1, owner).
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<ToolsOfTheTradePower>(), 1, BlitzLimits.PlayerOwner);
}

[BlitzContent("SERPENT_FORM")]
public sealed class SerpentForm : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["SerpentFormPower"];

    public override IReadOnlyList<string> AppliedPowers => ["SERPENT_FORM_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<SerpentFormPower>(), (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}
