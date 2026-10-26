using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Content.Cards.Defect;

[ContentId("STRIKE_DEFECT")]
public sealed class StrikeDefect : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralAttack(ref x, 0);
}

[ContentId("DEFEND_DEFECT")]
public sealed class DefendDefect : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralBlock(ref x, 0);
}

[ContentId("ZAP")]
public sealed class Zap : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.ChannelOrb(ref x, Kernel.OrbId("LIGHTNING_ORB"));
}

[ContentId("DUALCAST")]
public sealed class Dualcast : CardDef
{
    public override void OnPlay(ref Exec x)
    {
        // Vanilla: if the queue is non-empty, EvokeNext(dequeue: false) then EvokeNext(). That is the
        // same two evokes with only the last dequeueing.
        if (x.B.OrbCount == 0)
            return;
        Kernel.EvokeNext(ref x, repeat: 2);
    }
}
