using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Status;

/// <summary>Native <c>Disintegration</c>: choosing it applies 6 + the demon's curse counter as DisintegrationPower.</summary>
[BlitzContent("DISINTEGRATION")]
public sealed class Disintegration : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => throw new BlitzUnsupportedException("知识恶魔的诅咒卡只作为选择选项。");
}

/// <summary>Native <c>MindRot</c>: choosing it applies MindRotPower 1.</summary>
[BlitzContent("MIND_ROT")]
public sealed class MindRot : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => throw new BlitzUnsupportedException("知识恶魔的诅咒卡只作为选择选项。");
}

/// <summary>Native <c>Sloth</c>: choosing it applies SlothPower 3.</summary>
[BlitzContent("SLOTH")]
public sealed class Sloth : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => throw new BlitzUnsupportedException("知识恶魔的诅咒卡只作为选择选项。");
}

/// <summary>Native <c>WasteAway</c>: choosing it applies WasteAwayPower 1.</summary>
[BlitzContent("WASTE_AWAY")]
public sealed class WasteAway : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => throw new BlitzUnsupportedException("知识恶魔的诅咒卡只作为选择选项。");
}
