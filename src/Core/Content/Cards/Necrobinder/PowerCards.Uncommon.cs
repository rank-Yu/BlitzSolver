using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Necrobinder;

[BlitzContent("PAGESTORM")]
public sealed class Pagestorm : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["PAGESTORM_POWER"];

    // CardPowerOnPlaySupport: combat.Apply<PagestormPower>(owner, card.DynamicVars.Cards.IntValue, owner).
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<PagestormPower>(), (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("DANSE_MACABRE")]
public sealed class DanseMacabre : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["DanseMacabrePower"];

    public override IReadOnlyList<string> AppliedPowers => ["DANSE_MACABRE_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<DanseMacabrePower>(), (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>Friendship</c>: lose Strength, then apply FriendshipPower for its Energy
/// (<c>CorePowerSupport</c>'s Friendship case).
/// </summary>
[BlitzContent("FRIENDSHIP")]
public sealed class Friendship : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "Energy"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
            -(int)NecrobinderContent.Var(ref x, 0), BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.FriendshipPower>(),
            (int)NecrobinderContent.Var(ref x, 1), BlitzLimits.PlayerOwner);
    }
}

/// <summary><c>Countdown</c>: apply CountdownPower for its amount (<c>CardPowerOnPlaySupport</c>).</summary>
[BlitzContent("COUNTDOWN")]
public sealed class Countdown : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CountdownPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.CountdownPower>(),
            (int)NecrobinderContent.Var(ref x, 0), BlitzLimits.PlayerOwner);
}

/// <summary><c>Shroud</c>: apply ShroudPower for its Block variable (<c>CardPowerOnPlaySupport.SToZ</c>).</summary>
[BlitzContent("SHROUD")]
public sealed class Shroud : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.ShroudPower>(),
            (int)NecrobinderContent.Var(ref x, 0), BlitzLimits.PlayerOwner);
}

/// <summary><c>Calcify</c>: apply CalcifyPower (<c>CardPowerOnPlaySupport</c>).</summary>
[BlitzContent("CALCIFY")]
public sealed class Calcify : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalcifyPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.CalcifyPower>(),
            (int)NecrobinderContent.Var(ref x, 0), BlitzLimits.PlayerOwner);
}

/// <summary><c>Lethality</c>: apply LethalityPower (<c>CardPowerOnPlaySupport</c>).</summary>
[BlitzContent("LETHALITY")]
public sealed class Lethality : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["LethalityPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.LethalityPower>(),
            (int)NecrobinderContent.Var(ref x, 0), BlitzLimits.PlayerOwner);
}

/// <summary><c>Haunt</c>: apply HauntPower for its HpLoss variable (<c>CardPowerOnPlaySupport</c>).</summary>
[BlitzContent("HAUNT")]
public sealed class Haunt : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.HauntPower>(),
            (int)NecrobinderContent.Var(ref x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("SLEIGHT_OF_FLESH")]
public sealed class SleightOfFlesh : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["SleightOfFleshPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.SleightOfFleshPower>(),
            (int)NecrobinderContent.Var(ref x, 0), BlitzLimits.PlayerOwner);
}
