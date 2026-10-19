namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Defect;

// Defect starter deck. Existing engine paths: StrikeDefect/DefendDefect run the general
// attack/block recipe; Zap runs OrbCardMirrors.ZapOnPlay (OrbChannel<LightningOrb>); Dualcast runs
// OrbCardMirrors.DualcastOnPlay (OrbEvokeNext repeat 2, dequeue only on the last evoke).

[FlashContent("STRIKE_DEFECT")]
public sealed class StrikeDefect : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("DEFEND_DEFECT")]
public sealed class DefendDefect : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("ZAP")]
public sealed class Zap : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("LIGHTNING_ORB"));
}

[FlashContent("DUALCAST")]
public sealed class Dualcast : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        // Vanilla: if the queue is non-empty, EvokeNext(dequeue: false) then EvokeNext(). That is the
        // same two evokes with only the last dequeueing.
        if (x.B.OrbCount == 0)
            return;
        FlashKernel.EvokeNext(ref x, repeat: 2);
    }
}
