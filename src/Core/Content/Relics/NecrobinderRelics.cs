using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Relics;

/// <summary>
/// <c>BigHat</c>: on <c>AfterSideTurnStart</c> while the player's turn number is at most 1, generate
/// its <c>Cards</c> distinct Ethereal character cards into the hand.
/// </summary>
[ContentId("BIG_HAT")]
public sealed class BigHat : RelicDef
{
    // The old play-phase closure froze the whole character pool; keep it for rule parity.
    public override IReadOnlyList<string> GeneratedCardPools => [CardPools.CharacterAll];

    // Only the Ethereal subset participates while the opening battle is built.
    public override IReadOnlyList<string> SetupOnlyCardPools => [CardPools.CharacterEthereal];

    public override void AfterSideTurnStart(ref Exec x, Listener self, Side side)
    {
        if (side != Side.Player || x.B.PlayerTurn > 1)
            return;
        int count = StartVars.Int(ref x, self, "Cards");
        if (count <= 0)
            return;
        if (count > Limits.Cards)
            throw new CapacityException("GeneratedCards", Limits.Cards);
        Span<Card> cards = stackalloc Card[count];
        int sampled = Kernel.SampleCardPool(ref x.B, x.R, CardPools.CharacterEthereal, count,
            distinct: true, RngStream.CombatCardGeneration, cards);
        for (int index = 0; index < sampled; index++)
        {
            Kernel.AddGeneratedCard(ref x, cards[index], Pile.Hand, PilePosition.Bottom);
            if (x.NeedsChoice)
                return;
        }
    }
}
