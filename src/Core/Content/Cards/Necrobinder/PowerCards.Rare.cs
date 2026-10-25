using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Necrobinder;

[BlitzContent("SENTRY_MODE")]
public sealed class SentryMode : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["SentryModePower"];

    public override IReadOnlyList<string> AppliedPowers => ["SENTRY_MODE_POWER"];

    // CardPowerOnPlaySupport.SToZ: combat.Apply<SentryModePower>(owner, power var, owner).
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<SentryModePower>(), (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("CALL_OF_THE_VOID")]
public sealed class CallOfTheVoid : BlitzCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["CALL_OF_THE_VOID_POWER"];

    // CardPowerOnPlaySupport: combat.Apply<CallOfTheVoidPower>(owner, card.DynamicVars.Cards.IntValue, owner).
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<CallOfTheVoidPower>(), (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("REAPER_FORM")]
public sealed class ReaperForm : BlitzCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["REAPER_FORM_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<ReaperFormPower>(), 1, BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>NecroMastery</c>: summon Osty, then apply one NecroMasteryPower
/// (<c>CardOnPlaySupport.Batch043</c>'s NecroMastery case).
/// </summary>
[BlitzContent("NECRO_MASTERY")]
public sealed class NecroMastery : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.NecroMasteryPower>(), 1,
            BlitzLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Neurosurge</c>: gain energy, draw cards, then apply NeurosurgePower
/// (<c>CardDrawCardMirrors.NeurosurgeOnPlay</c>).
/// </summary>
[BlitzContent("NEUROSURGE")]
public sealed class Neurosurge : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["NeurosurgePower", "Energy", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.GainEnergy(ref x, NecrobinderContent.Var(ref x, 1));
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 2), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.NeurosurgePower>(),
            (int)NecrobinderContent.Var(ref x, 0), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("SPIRIT_OF_ASH")]
public sealed class SpiritOfAsh : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["BlockOnExhaust"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.SpiritOfAshPower>(),
            (int)NecrobinderContent.Var(ref x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("DEVOUR_LIFE")]
public sealed class DevourLife : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["DevourLifePower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.DevourLifePower>(),
            (int)NecrobinderContent.Var(ref x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("DEMESNE")]
public sealed class Demesne : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.DemesnePower>(),
            (int)NecrobinderContent.Var(ref x, 1), BlitzLimits.PlayerOwner);
}
