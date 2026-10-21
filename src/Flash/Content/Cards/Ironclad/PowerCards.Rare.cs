using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Ironclad;

[FlashContent("HELLRAISER")]
public sealed class Hellraiser : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.HellraiserPower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("JUGGERNAUT")]
public sealed class Juggernaut : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["JuggernautPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.JuggernautPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("PYRE")]
public sealed class Pyre : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.PyrePower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("DEMON_FORM")]
public sealed class DemonForm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DemonFormPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("BARRICADE")]
public sealed class Barricade : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.BarricadePower>(),
            1, FlashLimits.PlayerOwner);
}

[FlashContent("DARK_EMBRACE")]
public sealed class DarkEmbrace : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DarkEmbracePower>(),
            1, FlashLimits.PlayerOwner);
}

[FlashContent("CRIMSON_MANTLE")]
public sealed class CrimsonMantle : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CrimsonMantlePower"];

    public override void OnPlay(ref FlashExec x)
    {
        ushort id = FlashRegistry.PowerId<Powers.CrimsonMantlePower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, id, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        int index = FlashKernel.FindPower(x.B, FlashLimits.PlayerOwner, id);
        if (index < 0)
            throw new FlashUnsupportedException("绯红披风状态施加后未找到对应 Power。");
        // SimulatedCombatState.IncrementCrimsonMantle: SelfDamage.BaseValue++.
        x.B.Powers[index].Aux0++;
    }
}

[FlashContent("AGGRESSION")]
public sealed class Aggression : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.AggressionPower>(),
            1, FlashLimits.PlayerOwner);
}

[FlashContent("UNMOVABLE")]
public sealed class Unmovable : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.UnmovablePower>(),
            1, FlashLimits.PlayerOwner);
}
