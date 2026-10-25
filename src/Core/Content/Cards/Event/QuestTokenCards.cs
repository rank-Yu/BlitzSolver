using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Event;

[BlitzContent("BYRDONIS_EGG")]
public sealed class ByrdonisEgg : BlitzCardDef
{
    // Native CardType.Quest, cost -1, Unplayable; no OnPlay override.
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

[BlitzContent("DOWSING")]
public sealed class Dowsing : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Rooms"];

    // Native CardType.Quest, cost -1, Unplayable; the Rooms variable is only read out of combat.
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

[BlitzContent("LANTERN_KEY")]
public sealed class LanternKey : BlitzCardDef
{
    // Native CardType.Quest, cost -1, Unplayable; only map-point / event hooks out of combat.
    public override void OnPlay(ref BlitzExec x)
    {
    }
}

[BlitzContent("SPOILS_MAP")]
public sealed class SpoilsMap : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Gold"];

    // Native CardType.Quest, cost -1, Unplayable; only act-map hooks out of combat.
    public override void OnPlay(ref BlitzExec x)
    {
    }
}
