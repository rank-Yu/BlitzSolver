using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Defect;

[FlashContent("CONSUMING_SHADOW")]
public sealed class ConsumingShadow : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat", "ConsumingShadowPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("DARK_ORB"), (int)FlashCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.ConsumingShadowPower>(), 1);
    }
}

[FlashContent("MACHINE_LEARNING")]
public sealed class MachineLearning : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.MachineLearningPower>(), 0);
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

[FlashContent("COOLANT")]
public sealed class Coolant : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CoolantPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.CoolantPower>(), 0);
}

[FlashContent("TRASH_TO_TREASURE")]
public sealed class TrashToTreasure : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.TrashToTreasurePower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("DEFRAGMENT")]
public sealed class Defragment : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.FocusPower>(), 0);
}
