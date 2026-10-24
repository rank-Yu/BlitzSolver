using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Event;

[FlashContent("BYRDONIS_EGG")]
public sealed class ByrdonisEgg : FlashCardDef
{
    // Native CardType.Quest, cost -1, Unplayable; no OnPlay override.
    public override void OnPlay(ref FlashExec x)
    {
    }
}

[FlashContent("DOWSING")]
public sealed class Dowsing : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Rooms"];

    // Native CardType.Quest, cost -1, Unplayable; the Rooms variable is only read out of combat.
    public override void OnPlay(ref FlashExec x)
    {
    }
}

[FlashContent("LANTERN_KEY")]
public sealed class LanternKey : FlashCardDef
{
    // Native CardType.Quest, cost -1, Unplayable; only map-point / event hooks out of combat.
    public override void OnPlay(ref FlashExec x)
    {
    }
}

[FlashContent("SPOILS_MAP")]
public sealed class SpoilsMap : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Gold"];

    // Native CardType.Quest, cost -1, Unplayable; only act-map hooks out of combat.
    public override void OnPlay(ref FlashExec x)
    {
    }
}
