namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>Native <c>CardPilePosition</c>.</summary>
public enum FlashPilePosition : byte
{
    Bottom = 0,
    Top = 1,
    Random = 2,
}

public static partial class FlashKernel
{
    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.AddToPile</c> for a card already in a combat pile.
    /// Returns false when the move is refused (combat ending or owner dead).
    /// </summary>
    public static bool MoveCard(ref FlashBattle battle, FlashPile from, int index, FlashPile to,
        FlashPilePosition position = FlashPilePosition.Bottom)
    {
        if (IsEnding(battle) || battle.Player.IsDead)
            return false;
        FlashPile target = to == FlashPile.Hand && battle.HandCount >= battle.MaxHandSize ? FlashPile.Discard : to;
        FlashCard card = FlashPiles.RemoveAt(ref battle, from, index);
        int count = FlashPiles.Count(battle, target);
        int insert = position switch
        {
            FlashPilePosition.Bottom => count,
            FlashPilePosition.Top => 0,
            FlashPilePosition.Random => battle.RngStream(FlashRngStream.Shuffle).NextInt(count + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
        };
        FlashPiles.Insert(ref battle, target, insert, card);
        return true;
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.Draw</c> / <c>DrawCore</c>.</summary>
    public static void Draw(ref FlashExec x, int count, bool fromHandDraw)
    {
        // ShouldDraw has no supported listeners yet.
        if (IsOverOrEnding(x.B))
            return;
        for (int index = 0; index < count; index++)
        {
            if (IsOverOrEnding(x.B) || x.B.HandCount >= x.B.MaxHandSize)
                break;
            ShuffleIfNecessary(ref x);
            if (x.B.DrawCount == 0 || x.B.HandCount >= x.B.MaxHandSize)
                break;
            // AddToPile(top draw card, Hand, Bottom): the draw segment directly follows the hand.
            FlashPiles.DrawTopToHand(ref x.B);
            // History.CardDrawn / RecordCardDrawn / AfterCardDrawn: no tracked counters or
            // supported listeners yet.
        }
    }

    /// <summary>Decimal overload: rounds up like <c>CombatPredictionSimulator.Draw(Player, decimal, bool)</c>.</summary>
    public static void Draw(ref FlashExec x, decimal count, bool fromHandDraw)
        => Draw(ref x, count > 0m ? (int)Math.Ceiling(count) : 0, fromHandDraw);

    private static void ShuffleIfNecessary(ref FlashExec x)
    {
        if (x.B.DrawCount == 0 && x.B.DiscardCount != 0)
            Shuffle(ref x);
    }

    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.Shuffle</c>: discard (in order) followed by the draw pile,
    /// stable-shuffled with the Shuffle stream, all placed into the draw pile.
    /// </summary>
    public static void Shuffle(ref FlashExec x)
    {
        if (IsOverOrEnding(x.B))
            return;
        int drawCount = x.B.DrawCount;
        int discardCount = x.B.DiscardCount;
        int total = drawCount + discardCount;
        Span<FlashCard> list = stackalloc FlashCard[FlashLimits.Cards];
        list = list[..total];
        FlashPiles.ReadCards(x.B, FlashPile.Discard).CopyTo(list);
        FlashPiles.ReadCards(x.B, FlashPile.Draw).CopyTo(list[discardCount..]);

        // ListExtensions.StableShuffle: sort, then UnstableShuffle (Fisher-Yates from the end).
        FlashCardSort.Sort(list, x.R);
        ref FlashRngState rng = ref x.B.RngStream(FlashRngStream.Shuffle);
        for (int n = total; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
        // ModifyShuffleOrder has no supported listeners yet.

        // Draw and discard are adjacent segments: the region becomes the new draw pile.
        list.CopyTo(System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref x.B.Cards[x.B.HandCount], total));
        x.B.DrawCount = checked((byte)total);
        x.B.DiscardCount = 0;
        x.B.ShuffleEventCount++;
        // AfterShuffle has no supported listeners yet.
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.Exhaust</c>.</summary>
    public static void Exhaust(ref FlashExec x, FlashPile from, int index)
    {
        if (IsOverOrEnding(x.B))
            return;
        MoveCard(ref x.B, from, index, FlashPile.Exhaust);
        // RecordCardExhausted and AfterCardExhausted: no tracked counters or listeners yet.
    }
}
