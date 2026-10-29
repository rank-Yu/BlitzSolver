using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Cards.Ironclad;

[ContentId("HELLRAISER")]
public sealed class Hellraiser : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.HellraiserPower>(), 1, Limits.PlayerOwner);
}

[ContentId("JUGGERNAUT")]
public sealed class Juggernaut : CardDef
{
    public override IReadOnlyList<string> VarNames => ["JuggernautPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.JuggernautPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("PYRE")]
public sealed class Pyre : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.PyrePower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("DEMON_FORM")]
public sealed class DemonForm : CardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.DemonFormPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("BARRICADE")]
public sealed class Barricade : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.BarricadePower>(),
            1, Limits.PlayerOwner);
}

[ContentId("DARK_EMBRACE")]
public sealed class DarkEmbrace : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.DarkEmbracePower>(),
            1, Limits.PlayerOwner);
}

[ContentId("CRIMSON_MANTLE")]
public sealed class CrimsonMantle : CardDef
{
    public override IReadOnlyList<string> VarNames => ["CrimsonMantlePower"];

    public override void OnPlay(ref Exec x)
    {
        ushort id = Registry.PowerId<Powers.CrimsonMantlePower>();
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, id, (int)CardEffects.Var(x, 0),
            Limits.PlayerOwner);
        int index = Kernel.FindPower(x.B, Limits.PlayerOwner, id);
        if (index < 0)
            throw new UnsupportedException("绯红披风状态施加后未找到对应 Power。");
        // SimulatedCombatState.IncrementCrimsonMantle: SelfDamage.BaseValue++.
        x.B.Powers[index].Aux0++;
    }
}

[ContentId("AGGRESSION")]
public sealed class Aggression : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.AggressionPower>(),
            1, Limits.PlayerOwner);
}

[ContentId("UNMOVABLE")]
public sealed class Unmovable : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.UnmovablePower>(),
            1, Limits.PlayerOwner);
}
