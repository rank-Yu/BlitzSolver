using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

[ContentId("ENVENOM")]
public sealed class Envenom : CardDef
{
    public override IReadOnlyList<string> VarNames => ["EnvenomPower"];

    public override void OnPlay(ref Exec x)
        => CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.EnvenomPower>(), 0);
}

[ContentId("FAN_OF_KNIVES")]
public sealed class FanOfKnives : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Shivs"];

    public override void OnPlay(ref Exec x)
    {
        // CardPowerOnPlaySupport then CardPileOnPlaySupport: the power first, then Shivs Shivs
        // (the new power makes those Shivs target all enemies when played).
        Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.FanOfKnivesPower>(), 1, Limits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)CardEffects.Var(x, 0), upgraded: false);
    }
}

[ContentId("AFTERIMAGE")]
public sealed class Afterimage : CardDef
{
    public override IReadOnlyList<string> VarNames => ["AfterimagePower"];

    public override void OnPlay(ref Exec x)
        => CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.AfterimagePower>(), 0);
}

[ContentId("MASTER_PLANNER")]
public sealed class MasterPlanner : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.MasterPlannerPower>(), 1, Limits.PlayerOwner);
}

[ContentId("WELL_LAID_PLANS")]
public sealed class WellLaidPlans : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.WellLaidPlansPower>(), 1, Limits.PlayerOwner);
}

[ContentId("TRACKING")]
public sealed class Tracking : CardDef
{
    // CardOnPlaySupport.Batch042.Tracking: TrackingPower 50.
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.TrackingPower>(), 50, Limits.PlayerOwner);
}

[ContentId("ABRASIVE")]
public sealed class Abrasive : CardDef
{
    public override IReadOnlyList<string> VarNames => ["ThornsPower", "DexterityPower"];

    // CorePowerSupport.Abrasive: Dexterity then Thorns (the canonical variable order is Thorns,
    // Dexterity).
    public override void OnPlay(ref Exec x)
    {
        CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.DexterityPower>(), 1);
        if (x.NeedsChoice)
            return;
        CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.ThornsPower>(), 0);
    }
}

[ContentId("TOOLS_OF_THE_TRADE")]
public sealed class ToolsOfTheTrade : CardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["TOOLS_OF_THE_TRADE_POWER"];

    // CardPowerOnPlaySupport.SToZ: combat.Apply<ToolsOfTheTradePower>(owner, 1, owner).
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<ToolsOfTheTradePower>(), 1, Limits.PlayerOwner);
}

[ContentId("SERPENT_FORM")]
public sealed class SerpentForm : CardDef
{
    public override IReadOnlyList<string> VarNames => ["SerpentFormPower"];

    public override IReadOnlyList<string> AppliedPowers => ["SERPENT_FORM_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<SerpentFormPower>(), (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}
