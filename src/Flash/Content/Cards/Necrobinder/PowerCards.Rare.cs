using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

[FlashContent("SENTRY_MODE")]
public sealed class SentryMode : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SentryModePower"];

    public override IReadOnlyList<string> AppliedPowers => ["SENTRY_MODE_POWER"];

    // CardPowerOnPlaySupport.SToZ: combat.Apply<SentryModePower>(owner, power var, owner).
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<SentryModePower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("CALL_OF_THE_VOID")]
public sealed class CallOfTheVoid : FlashCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["CALL_OF_THE_VOID_POWER"];

    // CardPowerOnPlaySupport: combat.Apply<CallOfTheVoidPower>(owner, card.DynamicVars.Cards.IntValue, owner).
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<CallOfTheVoidPower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("REAPER_FORM")]
public sealed class ReaperForm : FlashCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["REAPER_FORM_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<ReaperFormPower>(), 1, FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>NecroMastery</c>: summon Osty, then apply one NecroMasteryPower
/// (<c>CardOnPlaySupport.Batch043</c>'s NecroMastery case).
/// </summary>
[FlashContent("NECRO_MASTERY")]
public sealed class NecroMastery : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.NecroMasteryPower>(), 1,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Neurosurge</c>: gain energy, draw cards, then apply NeurosurgePower
/// (<c>CardDrawCardMirrors.NeurosurgeOnPlay</c>).
/// </summary>
[FlashContent("NEUROSURGE")]
public sealed class Neurosurge : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["NeurosurgePower", "Energy", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.GainEnergy(ref x, NecrobinderContent.Var(ref x, 1));
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 2), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.NeurosurgePower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
    }
}

[FlashContent("SPIRIT_OF_ASH")]
public sealed class SpiritOfAsh : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["BlockOnExhaust"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.SpiritOfAshPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("DEVOUR_LIFE")]
public sealed class DevourLife : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DevourLifePower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DevourLifePower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("DEMESNE")]
public sealed class Demesne : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DemesnePower>(),
            (int)NecrobinderContent.Var(ref x, 1), FlashLimits.PlayerOwner);
}
