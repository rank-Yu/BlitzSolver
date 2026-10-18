namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Ironclad;

// Existing engine paths for this batch:
// - Inflame, FeelNoPain, Juggernaut and Cruelty have no OnPlay mirror; CardPowerOnPlaySupport
//   applies the power and CorePowerSupport falls back to the same cards.
// - FeelNoPainPower, JuggernautPower and CrueltyPower keep their native hooks in M/Hooks/**.
// - StoneArmor (CardPowerOnPlaySupport.SToZ) applies PlatingPower, Aggression (CorePowerSupport)
//   applies AggressionPower and Unmovable / Rupture (CardPowerOnPlaySupport) apply
//   UnmovablePower / RupturePower; the powers are defined in Content/Powers/IroncladPowers.cs.

[FlashContent("INFLAME")]
public sealed class Inflame : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("FEEL_NO_PAIN")]
public sealed class FeelNoPain : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FeelNoPainPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("JUGGERNAUT")]
public sealed class Juggernaut : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["JuggernautPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.JuggernautPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("CRUELTY")]
public sealed class Cruelty : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CrueltyPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.CrueltyPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

// P4 batch: FlameBarrier is a block plus power application; its power is removed by
// CorePowerSupport.TriggerTransientSideTurnEndPowers, which the Flash turn phases copy. Pyre,
// DemonForm and Barricade are pure CardPowerOnPlaySupport / CorePowerSupport cases (no mirror
// half). Rage, Juggling, DarkEmbrace, CrimsonMantle and Inferno were undefined at this point and
// were added by the P8 batch; StoneArmor, Unmovable and Aggression were added by the P14 batch.

[FlashContent("FLAME_BARRIER")]
public sealed class FlameBarrier : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "DamageBack"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FlameBarrierPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
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


// P8 batch: Rage and Juggling keep their private counters in FlashPower.Aux0 (see the power
// definitions); CrimsonMantle and Inferno keep the private SelfDamage dynamic variable there too.
// The existing engine applies the power in CorePowerSupport / CardOnPlaySupport and then increments
// the counter, which the definitions below copy.

[FlashContent("RAGE")]
public sealed class Rage : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RagePower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("JUGGLING")]
public sealed class Juggling : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.JugglingPower>(),
            1, FlashLimits.PlayerOwner);
        int index = FlashKernel.FindPower(x.B, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.JugglingPower>());
        if (index < 0)
            throw new FlashUnsupportedException("杂耍状态施加后未找到对应 Power。");
        // SimulatedCombatState.InitializeJugglingAfterApplied: attacks started this turn.
        x.B.Powers[index].Aux0 = x.B.Counter(FlashCounter.AttackPlayStartsThisTurn);
    }
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

[FlashContent("INFERNO")]
public sealed class Inferno : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["InfernoPower"];

    public override void OnPlay(ref FlashExec x)
    {
        ushort id = FlashRegistry.PowerId<Powers.InfernoPower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, id, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        int index = FlashKernel.FindPower(x.B, FlashLimits.PlayerOwner, id);
        if (index < 0)
            throw new FlashUnsupportedException("炼狱状态施加后未找到对应 Power。");
        // CardOnPlaySupport: InfernoPower.IncrementSelfDamage.
        x.B.Powers[index].Aux0++;
    }
}

// P14 batch: pure power applications with no mirror half.
// - Aggression applies AggressionPower 1 (CorePowerSupport's Aggression case).
// - StoneArmor applies PlatingPower from its PlatingPower dynamic variable
//   (CardPowerOnPlaySupport.SToZ, StoneArmor case).
// - Unmovable applies UnmovablePower 1 (CardPowerOnPlaySupport.SToZ, Unmovable case).
// - Rupture applies RupturePower from its StrengthPower dynamic variable
//   (CardPowerOnPlaySupport, Rupture case).

[FlashContent("AGGRESSION")]
public sealed class Aggression : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.AggressionPower>(),
            1, FlashLimits.PlayerOwner);
}

[FlashContent("STONE_ARMOR")]
public sealed class StoneArmor : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.PlatingPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("UNMOVABLE")]
public sealed class Unmovable : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.UnmovablePower>(),
            1, FlashLimits.PlayerOwner);
}

[FlashContent("RUPTURE")]
public sealed class Rupture : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RupturePower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}
