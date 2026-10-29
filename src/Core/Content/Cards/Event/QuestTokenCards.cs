using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Cards.Event;

[ContentId("BYRDONIS_EGG")]
public sealed class ByrdonisEgg : CardDef
{
    // Native CardType.Quest, cost -1, Unplayable; no OnPlay override.
    public override void OnPlay(ref Exec x)
    {
    }
}

[ContentId("DOWSING")]
public sealed class Dowsing : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Rooms"];

    // Native CardType.Quest, cost -1, Unplayable; the Rooms variable is only read out of combat.
    public override void OnPlay(ref Exec x)
    {
    }
}

[ContentId("LANTERN_KEY")]
public sealed class LanternKey : CardDef
{
    // Native CardType.Quest, cost -1, Unplayable; only map-point / event hooks out of combat.
    public override void OnPlay(ref Exec x)
    {
    }
}

[ContentId("SPOILS_MAP")]
public sealed class SpoilsMap : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Gold"];

    // Native CardType.Quest, cost -1, Unplayable; only act-map hooks out of combat.
    public override void OnPlay(ref Exec x)
    {
    }
}
