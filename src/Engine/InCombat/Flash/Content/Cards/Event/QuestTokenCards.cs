namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Event;

// Quest cards (EC batch). The four quest cards are added to the run deck by events (ByrdonisNest,
// TheLegendsWereTrue, TheLanternKey, Neow/WarHistorianRepy) and are shuffled into the combat piles
// like any deck card; they are unplayable and have no combat hook. Their native hooks
// (TryModifyRestSiteOptions, BeforeRoomEntered, ModifyGeneratedMap, ...) are not combat hooks and are
// never dispatched by the existing combat engine, so these definitions only carry the card value.
// Admission's deck-card check only rejects run-listener (combat) hooks and therefore does not reject
// them. Luminesce (the RadiantPearl Token) is defined by the PR batch in
// Content/Cards/Colorless/Luminesce.cs and is not duplicated here.

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
