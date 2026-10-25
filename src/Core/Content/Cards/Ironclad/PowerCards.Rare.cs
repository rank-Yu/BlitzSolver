using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Ironclad;

[BlitzContent("HELLRAISER")]
public sealed class Hellraiser : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.HellraiserPower>(), 1, BlitzLimits.PlayerOwner);
}

[BlitzContent("JUGGERNAUT")]
public sealed class Juggernaut : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["JuggernautPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.JuggernautPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("PYRE")]
public sealed class Pyre : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.PyrePower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("DEMON_FORM")]
public sealed class DemonForm : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.DemonFormPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("BARRICADE")]
public sealed class Barricade : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.BarricadePower>(),
            1, BlitzLimits.PlayerOwner);
}

[BlitzContent("DARK_EMBRACE")]
public sealed class DarkEmbrace : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.DarkEmbracePower>(),
            1, BlitzLimits.PlayerOwner);
}

[BlitzContent("CRIMSON_MANTLE")]
public sealed class CrimsonMantle : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CrimsonMantlePower"];

    public override void OnPlay(ref BlitzExec x)
    {
        ushort id = BlitzRegistry.PowerId<Powers.CrimsonMantlePower>();
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, id, (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
        int index = BlitzKernel.FindPower(x.B, BlitzLimits.PlayerOwner, id);
        if (index < 0)
            throw new BlitzUnsupportedException("绯红披风状态施加后未找到对应 Power。");
        // SimulatedCombatState.IncrementCrimsonMantle: SelfDamage.BaseValue++.
        x.B.Powers[index].Aux0++;
    }
}

[BlitzContent("AGGRESSION")]
public sealed class Aggression : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.AggressionPower>(),
            1, BlitzLimits.PlayerOwner);
}

[BlitzContent("UNMOVABLE")]
public sealed class Unmovable : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.UnmovablePower>(),
            1, BlitzLimits.PlayerOwner);
}
