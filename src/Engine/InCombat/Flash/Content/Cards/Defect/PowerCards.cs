namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Defect;

// Defect Power cards and the two attacks that apply temporary Focus. The mirror (attack/block) runs
// first, then the compensation that applies the power, matching CorePowerSupport,
// CardPowerOnPlaySupport, CardEffectSpecRegistry and CardOnPlaySupport in the existing engine.

[FlashContent("HAILSTORM")]
public sealed class Hailstorm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HailstormPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.HailstormPower>(), 0);
}

[FlashContent("MACHINE_LEARNING")]
public sealed class MachineLearning : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.MachineLearningPower>(), 0);
}

[FlashContent("LOOP")]
public sealed class Loop : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Loop"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.LoopPower>(), 0);
}

[FlashContent("CREATIVE_AI")]
public sealed class CreativeAi : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CreativeAi"];

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterPowers];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.CreativeAiPower>(), 0);
}

[FlashContent("SPINNER")]
public sealed class Spinner : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SpinnerPower"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Card.Upgrade > 0)
        {
            FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("GLASS_ORB"));
            if (x.NeedsChoice)
                return;
        }
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.SpinnerPower>(), 0);
    }
}

[FlashContent("STORM")]
public sealed class Storm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StormPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.StormPower>(), 0);
}

[FlashContent("ECHO_FORM")]
public sealed class EchoForm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["EchoForm"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.EchoFormPower>(), 0);
}

[FlashContent("BUFFER")]
public sealed class Buffer : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["BufferPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.BufferPower>(), 0);
}

[FlashContent("ITERATION")]
public sealed class Iteration : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["IterationPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.IterationPower>(), 0);
}

[FlashContent("FERAL")]
public sealed class Feral : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["FeralPower"];

    public override void OnPlay(ref FlashExec x)
    {
        int amount = (int)FlashCardEffects.Var(x, 0);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FeralPower>(), amount,
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        // SimulatedCombatState.InitializeFeralAfterApplied: the new instance starts from the
        // zero-cost attacks already played this turn.
        int index = FlashKernel.FindPower(x.B, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FeralPower>());
        if (index < 0)
            return;
        x.B.Powers[index].Aux0 = x.B.Counter(FlashCounter.ZeroCostAttackStartsThisTurn);
    }
}

[FlashContent("COOLANT")]
public sealed class Coolant : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CoolantPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.CoolantPower>(), 0);
}

[FlashContent("THUNDER")]
public sealed class Thunder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["ThunderPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.ThunderPower>(), 0);
}

[FlashContent("SMOKESTACK")]
public sealed class Smokestack : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SmokestackPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.SmokestackPower>(), 0);
}

[FlashContent("TRASH_TO_TREASURE")]
public sealed class TrashToTreasure : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.TrashToTreasurePower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("SIGNAL_BOOST")]
public sealed class SignalBoost : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SignalBoostPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.SignalBoostPower>(), 0);
}

[FlashContent("SUBROUTINE")]
public sealed class Subroutine : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.SubroutinePower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("DEFRAGMENT")]
public sealed class Defragment : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.FocusPower>(), 0);
}

[FlashContent("FOCUSED_STRIKE")]
public sealed class FocusedStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "FocusPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardEffectSpecRegistry's FocusedStrike → ApplyTemporaryFocus<FocusedStrikePower>.
        FlashKernel.ApplyTemporaryFocus(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.FocusedStrikePower>(), (int)FlashCardEffects.Var(x, 1),
            FlashLimits.PlayerOwner, sign: 1);
    }
}

[FlashContent("HOTFIX")]
public sealed class Hotfix : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyTemporaryFocus(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.HotfixPower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner,
            sign: 1);
}

[FlashContent("HYPERBEAM")]
public sealed class Hyperbeam : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "FocusPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardEffectSpecRegistry's Hyperbeam → ApplyTemporaryFocusLoss<HyperbeamFocusDownPower>.
        FlashKernel.ApplyTemporaryFocus(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.HyperbeamFocusDownPower>(), (int)FlashCardEffects.Var(x, 1),
            FlashLimits.PlayerOwner, sign: -1);
    }
}

[FlashContent("BIASED_COGNITION")]
public sealed class BiasedCognition : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower", "BiasedCognitionPower"];

    // CorePowerSupport.BiasedCognition: Focus first, then the per-turn Focus loss power.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.FocusPower>(), 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.BiasedCognitionPower>(), 1);
    }
}

[FlashContent("BULK_UP")]
public sealed class BulkUp : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OrbSlots", "StrengthPower", "DexterityPower"];

    // CorePowerSupport.BulkUp: remove slots, then Strength and Dexterity.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.RemoveOrbSlots(ref x.B, (int)FlashCardEffects.Var(x, 0));
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.StrengthPower>(), 1);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.DexterityPower>(), 2);
    }
}
