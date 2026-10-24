using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

[FlashContent("PAGESTORM")]
public sealed class Pagestorm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["PAGESTORM_POWER"];

    // CardPowerOnPlaySupport: combat.Apply<PagestormPower>(owner, card.DynamicVars.Cards.IntValue, owner).
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<PagestormPower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("DANSE_MACABRE")]
public sealed class DanseMacabre : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DanseMacabrePower"];

    public override IReadOnlyList<string> AppliedPowers => ["DANSE_MACABRE_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<DanseMacabrePower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>Friendship</c>: lose Strength, then apply FriendshipPower for its Energy
/// (<c>CorePowerSupport</c>'s Friendship case).
/// </summary>
[FlashContent("FRIENDSHIP")]
public sealed class Friendship : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            -(int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FriendshipPower>(),
            (int)NecrobinderContent.Var(ref x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary><c>Countdown</c>: apply CountdownPower for its amount (<c>CardPowerOnPlaySupport</c>).</summary>
[FlashContent("COUNTDOWN")]
public sealed class Countdown : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CountdownPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.CountdownPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>Shroud</c>: apply ShroudPower for its Block variable (<c>CardPowerOnPlaySupport.SToZ</c>).</summary>
[FlashContent("SHROUD")]
public sealed class Shroud : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ShroudPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>Calcify</c>: apply CalcifyPower (<c>CardPowerOnPlaySupport</c>).</summary>
[FlashContent("CALCIFY")]
public sealed class Calcify : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalcifyPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.CalcifyPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>Lethality</c>: apply LethalityPower (<c>CardPowerOnPlaySupport</c>).</summary>
[FlashContent("LETHALITY")]
public sealed class Lethality : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["LethalityPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.LethalityPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>Haunt</c>: apply HauntPower for its HpLoss variable (<c>CardPowerOnPlaySupport</c>).</summary>
[FlashContent("HAUNT")]
public sealed class Haunt : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.HauntPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("SLEIGHT_OF_FLESH")]
public sealed class SleightOfFlesh : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SleightOfFleshPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.SleightOfFleshPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}
