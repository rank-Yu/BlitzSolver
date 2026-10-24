using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Status;

/// <summary>Native <c>Disintegration</c>: choosing it applies 6 + the demon's curse counter as DisintegrationPower.</summary>
[FlashContent("DISINTEGRATION")]
public sealed class Disintegration : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => throw new FlashUnsupportedException("知识恶魔的诅咒卡只作为选择选项。");
}

/// <summary>Native <c>MindRot</c>: choosing it applies MindRotPower 1.</summary>
[FlashContent("MIND_ROT")]
public sealed class MindRot : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => throw new FlashUnsupportedException("知识恶魔的诅咒卡只作为选择选项。");
}

/// <summary>Native <c>Sloth</c>: choosing it applies SlothPower 3.</summary>
[FlashContent("SLOTH")]
public sealed class Sloth : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => throw new FlashUnsupportedException("知识恶魔的诅咒卡只作为选择选项。");
}

/// <summary>Native <c>WasteAway</c>: choosing it applies WasteAwayPower 1.</summary>
[FlashContent("WASTE_AWAY")]
public sealed class WasteAway : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => throw new FlashUnsupportedException("知识恶魔的诅咒卡只作为选择选项。");
}
