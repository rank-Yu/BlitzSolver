using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[BlitzContent("STRIKE_DEFECT")]
public sealed class StrikeDefect : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("DEFEND_DEFECT")]
public sealed class DefendDefect : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);
}

[BlitzContent("ZAP")]
public sealed class Zap : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("LIGHTNING_ORB"));
}

[BlitzContent("DUALCAST")]
public sealed class Dualcast : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        // Vanilla: if the queue is non-empty, EvokeNext(dequeue: false) then EvokeNext(). That is the
        // same two evokes with only the last dequeueing.
        if (x.B.OrbCount == 0)
            return;
        BlitzKernel.EvokeNext(ref x, repeat: 2);
    }
}
