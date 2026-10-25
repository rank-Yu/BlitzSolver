using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[ContentId("CONSUMING_SHADOW")]
public sealed class ConsumingShadow : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat", "ConsumingShadowPower"];

    public override void OnPlay(ref Exec x)
    {
        Kernel.ChannelOrbs(ref x, Kernel.OrbId("DARK_ORB"), (int)CardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.ConsumingShadowPower>(), 1);
    }
}

[ContentId("MACHINE_LEARNING")]
public sealed class MachineLearning : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref Exec x)
        => CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.MachineLearningPower>(), 0);
}

[ContentId("CREATIVE_AI")]
public sealed class CreativeAi : CardDef
{
    public override IReadOnlyList<string> VarNames => ["CreativeAi"];

    public override IReadOnlyList<string> GeneratedCardPools => [CardPools.CharacterPowers];

    public override void OnPlay(ref Exec x)
        => CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.CreativeAiPower>(), 0);
}

[ContentId("SPINNER")]
public sealed class Spinner : CardDef
{
    public override IReadOnlyList<string> VarNames => ["SpinnerPower"];

    public override void OnPlay(ref Exec x)
    {
        if (x.Card.Upgrade > 0)
        {
            Kernel.ChannelOrb(ref x, Kernel.OrbId("GLASS_ORB"));
            if (x.NeedsChoice)
                return;
        }
        CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.SpinnerPower>(), 0);
    }
}

[ContentId("ECHO_FORM")]
public sealed class EchoForm : CardDef
{
    public override IReadOnlyList<string> VarNames => ["EchoForm"];

    public override void OnPlay(ref Exec x)
        => CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.EchoFormPower>(), 0);
}

[ContentId("BUFFER")]
public sealed class Buffer : CardDef
{
    public override IReadOnlyList<string> VarNames => ["BufferPower"];

    public override void OnPlay(ref Exec x)
        => CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.BufferPower>(), 0);
}

[ContentId("COOLANT")]
public sealed class Coolant : CardDef
{
    public override IReadOnlyList<string> VarNames => ["CoolantPower"];

    public override void OnPlay(ref Exec x)
        => CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.CoolantPower>(), 0);
}

[ContentId("TRASH_TO_TREASURE")]
public sealed class TrashToTreasure : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.TrashToTreasurePower>(), 1, Limits.PlayerOwner);
}

[ContentId("DEFRAGMENT")]
public sealed class Defragment : CardDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower"];

    public override void OnPlay(ref Exec x)
        => CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.FocusPower>(), 0);
}
