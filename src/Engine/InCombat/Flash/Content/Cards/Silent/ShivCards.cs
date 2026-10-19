namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Silent;

// Shiv generation and the powers that interact with Shivs. Existing engine paths:
// CardPileOnPlaySupport.Apply (BladeDance/BladeOfInk/CloakAndDagger/FanOfKnives/UpMySleeve),
// BespokeCardMirrors.LeadingStrikeOnPlay, CardPowerOnPlaySupport (FanOfKnives/InfiniteBlades/
// MasterPlanner/WellLaidPlans), CorePowerSupport.ApplyCardPowers (Accuracy/Afterimage).

/// <summary>
/// <c>Shiv</c>: 0-cost attack that exhausts. Its native <c>TargetType</c> is AllEnemies while the
/// owner holds FanOfKnivesPower and AnyEnemy otherwise (Shiv.cs), so the rule-table target is
/// overridden dynamically.
/// </summary>
[FlashContent("SHIV")]
public sealed class Shiv : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override FlashTargetType TargetType(in FlashBattle battle, FlashRules rules, in FlashCard card)
        => SilentContent.HasFanOfKnives(battle) ? FlashTargetType.AllEnemies
            : rules.Card(card.Id, card.Upgrade).Target;

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("BLADE_DANCE")]
public sealed class BladeDance : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => SilentContent.GenerateShivs(ref x, (int)FlashCardEffects.Var(x, 0), upgraded: false);
}

[FlashContent("CLOAK_AND_DAGGER")]
public sealed class CloakAndDagger : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)FlashCardEffects.Var(x, 1), upgraded: false);
    }
}

[FlashContent("BLADE_OF_INK")]
public sealed class BladeOfInk : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => SilentContent.GenerateInkyShivs(ref x, (int)FlashCardEffects.Var(x, 0));
}

[FlashContent("UP_MY_SLEEVE")]
public sealed class UpMySleeve : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        SilentContent.GenerateShivs(ref x, (int)FlashCardEffects.Var(x, 0), upgraded: false);
        if (x.NeedsChoice)
            return;
        // UpMySleeve.cs: playedCard.EnergyCost.AddThisCombat(-1). The played card is the last entry of
        // the play pile while its OnPlay runs.
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("袖中藏刃没有正在打出的卡。");
        FlashKernel.AddEnergyCostModifier(ref FlashPiles.At(ref x.B, FlashPile.Play, position), -1,
            FlashCostType.Relative, FlashCostExpiration.EndOfCombat, reduceOnly: false);
    }
}

[FlashContent("LEADING_STRIKE")]
public sealed class LeadingStrike : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Shivs", "Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        // BespokeCardMirrors.LeadingStrikeOnPlay: attack, then Shivs Shivs to hand.
        FlashCardEffects.GeneralAttack(ref x, 1);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)FlashCardEffects.Var(x, 0), upgraded: false);
    }
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

[FlashContent("INFINITE_BLADES")]
public sealed class InfiniteBlades : ShivGeneratingCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.InfiniteBladesPower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("ACCURACY")]
public sealed class Accuracy : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["AccuracyPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.AccuracyPower>(), 0);
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
