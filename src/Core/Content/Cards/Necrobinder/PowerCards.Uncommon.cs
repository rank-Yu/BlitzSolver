using Sts2Solver.Content.Powers;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Cards.Necrobinder;

[ContentId("PAGESTORM")]
public sealed class Pagestorm : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["PAGESTORM_POWER"];

    // CardPowerOnPlaySupport: combat.Apply<PagestormPower>(owner, card.DynamicVars.Cards.IntValue, owner).
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<PagestormPower>(), (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("DANSE_MACABRE")]
public sealed class DanseMacabre : CardDef
{
    public override IReadOnlyList<string> VarNames => ["DanseMacabrePower"];

    public override IReadOnlyList<string> AppliedPowers => ["DANSE_MACABRE_POWER"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<DanseMacabrePower>(), (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

/// <summary>
/// <c>Friendship</c>: lose Strength, then apply FriendshipPower for its Energy
/// (<c>CorePowerSupport</c>'s Friendship case).
/// </summary>
[ContentId("FRIENDSHIP")]
public sealed class Friendship : CardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "Energy"];

    public override void OnPlay(ref Exec x)
    {
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StrengthPower>(),
            -(int)NecrobinderContent.Var(ref x, 0), Limits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.FriendshipPower>(),
            (int)NecrobinderContent.Var(ref x, 1), Limits.PlayerOwner);
    }
}

/// <summary><c>Countdown</c>: apply CountdownPower for its amount (<c>CardPowerOnPlaySupport</c>).</summary>
[ContentId("COUNTDOWN")]
public sealed class Countdown : CardDef
{
    public override IReadOnlyList<string> VarNames => ["CountdownPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.CountdownPower>(),
            (int)NecrobinderContent.Var(ref x, 0), Limits.PlayerOwner);
}

/// <summary><c>Shroud</c>: apply ShroudPower for its Block variable (<c>CardPowerOnPlaySupport.SToZ</c>).</summary>
[ContentId("SHROUD")]
public sealed class Shroud : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.ShroudPower>(),
            (int)NecrobinderContent.Var(ref x, 0), Limits.PlayerOwner);
}

/// <summary><c>Calcify</c>: apply CalcifyPower (<c>CardPowerOnPlaySupport</c>).</summary>
[ContentId("CALCIFY")]
public sealed class Calcify : CardDef
{
    public override IReadOnlyList<string> VarNames => ["CalcifyPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.CalcifyPower>(),
            (int)NecrobinderContent.Var(ref x, 0), Limits.PlayerOwner);
}

/// <summary><c>Lethality</c>: apply LethalityPower (<c>CardPowerOnPlaySupport</c>).</summary>
[ContentId("LETHALITY")]
public sealed class Lethality : CardDef
{
    public override IReadOnlyList<string> VarNames => ["LethalityPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.LethalityPower>(),
            (int)NecrobinderContent.Var(ref x, 0), Limits.PlayerOwner);
}

/// <summary><c>Haunt</c>: apply HauntPower for its HpLoss variable (<c>CardPowerOnPlaySupport</c>).</summary>
[ContentId("HAUNT")]
public sealed class Haunt : CardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.HauntPower>(),
            (int)NecrobinderContent.Var(ref x, 0), Limits.PlayerOwner);
}

[ContentId("SLEIGHT_OF_FLESH")]
public sealed class SleightOfFlesh : CardDef
{
    public override IReadOnlyList<string> VarNames => ["SleightOfFleshPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.SleightOfFleshPower>(),
            (int)NecrobinderContent.Var(ref x, 0), Limits.PlayerOwner);
}
