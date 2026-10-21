namespace CombatSolver.Engine.InCombat.Flash.Content.Relics;

/// <summary>
/// <c>BigHat</c>: on <c>AfterSideTurnStart</c> while the player's turn number is at most 1, generate
/// two distinct Ethereal character cards into the hand. That hook only fires during player turn 1's
/// start, which happens before the captured root (the root is taken later in the turn after the
/// side-turn-start batch), so the imported root already contains those cards. The existing engine has
/// no mirror for it and the Flash root never observes turn 1 side start; the definition is empty and
/// the evaluation reads the cards from the root.
/// </summary>
[FlashContent("BIG_HAT")]
public sealed class BigHat : FlashRelicDef
{
    // Turn-start generation samples the character pool; the turn-setup root is captured before
    // this hook fires, so the rules table must carry the generated card stats.
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];
}
