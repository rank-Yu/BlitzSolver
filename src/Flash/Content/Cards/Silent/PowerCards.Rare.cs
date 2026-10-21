using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

[FlashContent("ENVENOM")]
public sealed class Envenom : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["EnvenomPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.EnvenomPower>(), 0);
}

[FlashContent("FAN_OF_KNIVES")]
public sealed class FanOfKnives : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Shivs"];

    public override void OnPlay(ref FlashExec x)
    {
        // CardPowerOnPlaySupport then CardPileOnPlaySupport: the power first, then Shivs Shivs
        // (the new power makes those Shivs target all enemies when played).
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.FanOfKnivesPower>(), 1, FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)FlashCardEffects.Var(x, 0), upgraded: false);
    }
}

[FlashContent("AFTERIMAGE")]
public sealed class Afterimage : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["AfterimagePower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.AfterimagePower>(), 0);
}

[FlashContent("MASTER_PLANNER")]
public sealed class MasterPlanner : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.MasterPlannerPower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("WELL_LAID_PLANS")]
public sealed class WellLaidPlans : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.WellLaidPlansPower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("TRACKING")]
public sealed class Tracking : FlashCardDef
{
    // CardOnPlaySupport.Batch042.Tracking: TrackingPower 50.
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.TrackingPower>(), 50, FlashLimits.PlayerOwner);
}

[FlashContent("ABRASIVE")]
public sealed class Abrasive : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["ThornsPower", "DexterityPower"];

    // CorePowerSupport.Abrasive: Dexterity then Thorns (the canonical variable order is Thorns,
    // Dexterity).
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.DexterityPower>(), 1);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.ThornsPower>(), 0);
    }
}

[FlashContent("TOOLS_OF_THE_TRADE")]
public sealed class ToolsOfTheTrade : FlashCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["TOOLS_OF_THE_TRADE_POWER"];

    // CardPowerOnPlaySupport.SToZ: combat.Apply<ToolsOfTheTradePower>(owner, 1, owner).
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<ToolsOfTheTradePower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("SERPENT_FORM")]
public sealed class SerpentForm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SerpentFormPower"];

    public override IReadOnlyList<string> AppliedPowers => ["SERPENT_FORM_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<SerpentFormPower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}
