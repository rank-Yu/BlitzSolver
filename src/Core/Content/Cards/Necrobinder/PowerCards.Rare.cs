using Sts2Solver.Content.Powers;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Cards.Necrobinder;

[ContentId("SENTRY_MODE")]
public sealed class SentryMode : CardDef
{
    public override IReadOnlyList<string> VarNames => ["SentryModePower"];

    public override IReadOnlyList<string> AppliedPowers => ["SENTRY_MODE_POWER"];

    // CardPowerOnPlaySupport.SToZ: combat.Apply<SentryModePower>(owner, power var, owner).
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<SentryModePower>(), (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("CALL_OF_THE_VOID")]
public sealed class CallOfTheVoid : CardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["CALL_OF_THE_VOID_POWER"];

    // CardPowerOnPlaySupport: combat.Apply<CallOfTheVoidPower>(owner, card.DynamicVars.Cards.IntValue, owner).
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<CallOfTheVoidPower>(), (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("REAPER_FORM")]
public sealed class ReaperForm : CardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["REAPER_FORM_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<ReaperFormPower>(), 1, Limits.PlayerOwner);
}

/// <summary>
/// <c>NecroMastery</c>: summon Osty, then apply one NecroMasteryPower
/// (<c>CardOnPlaySupport.Batch043</c>'s NecroMastery case).
/// </summary>
[ContentId("NECRO_MASTERY")]
public sealed class NecroMastery : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref Exec x)
    {
        Kernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.NecroMasteryPower>(), 1,
            Limits.PlayerOwner);
    }
}

/// <summary>
/// <c>Neurosurge</c>: gain energy, draw cards, then apply NeurosurgePower
/// (<c>CardDrawCardMirrors.NeurosurgeOnPlay</c>).
/// </summary>
[ContentId("NEUROSURGE")]
public sealed class Neurosurge : CardDef
{
    public override IReadOnlyList<string> VarNames => ["NeurosurgePower", "Energy", "Cards"];

    public override void OnPlay(ref Exec x)
    {
        Kernel.GainEnergy(ref x, NecrobinderContent.Var(ref x, 1));
        if (x.NeedsChoice)
            return;
        Kernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 2), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.NeurosurgePower>(),
            (int)NecrobinderContent.Var(ref x, 0), Limits.PlayerOwner);
    }
}

[ContentId("SPIRIT_OF_ASH")]
public sealed class SpiritOfAsh : CardDef
{
    public override IReadOnlyList<string> VarNames => ["BlockOnExhaust"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.SpiritOfAshPower>(),
            (int)NecrobinderContent.Var(ref x, 0), Limits.PlayerOwner);
}

[ContentId("DEVOUR_LIFE")]
public sealed class DevourLife : CardDef
{
    public override IReadOnlyList<string> VarNames => ["DevourLifePower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.DevourLifePower>(),
            (int)NecrobinderContent.Var(ref x, 0), Limits.PlayerOwner);
}

[ContentId("DEMESNE")]
public sealed class Demesne : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.DemesnePower>(),
            (int)NecrobinderContent.Var(ref x, 1), Limits.PlayerOwner);
}
