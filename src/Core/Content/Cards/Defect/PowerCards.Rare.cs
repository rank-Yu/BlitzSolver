using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[BlitzContent("CONSUMING_SHADOW")]
public sealed class ConsumingShadow : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat", "ConsumingShadowPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("DARK_ORB"), (int)BlitzCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.ConsumingShadowPower>(), 1);
    }
}

[BlitzContent("MACHINE_LEARNING")]
public sealed class MachineLearning : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.MachineLearningPower>(), 0);
}

[BlitzContent("CREATIVE_AI")]
public sealed class CreativeAi : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CreativeAi"];

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterPowers];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.CreativeAiPower>(), 0);
}

[BlitzContent("SPINNER")]
public sealed class Spinner : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["SpinnerPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Card.Upgrade > 0)
        {
            BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("GLASS_ORB"));
            if (x.NeedsChoice)
                return;
        }
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.SpinnerPower>(), 0);
    }
}

[BlitzContent("ECHO_FORM")]
public sealed class EchoForm : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["EchoForm"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.EchoFormPower>(), 0);
}

[BlitzContent("BUFFER")]
public sealed class Buffer : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["BufferPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.BufferPower>(), 0);
}

[BlitzContent("COOLANT")]
public sealed class Coolant : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CoolantPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.CoolantPower>(), 0);
}

[BlitzContent("TRASH_TO_TREASURE")]
public sealed class TrashToTreasure : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.TrashToTreasurePower>(), 1, BlitzLimits.PlayerOwner);
}

[BlitzContent("DEFRAGMENT")]
public sealed class Defragment : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.FocusPower>(), 0);
}
