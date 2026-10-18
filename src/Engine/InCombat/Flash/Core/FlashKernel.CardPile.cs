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
        if (IsOverOrEnding(x.B) || !ShouldDraw(ref x, fromHandDraw))
            return;
        for (int index = 0; index < count; index++)
        {
            if (!DrawOneCore(ref x, fromHandDraw, out _) || x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// One <c>Draw(..., 1, ...)</c> call that also reports the card value at draw time. Pillage uses the
    /// drawn card's type even when the draw hook auto-played and removed that card (Hellraiser), so a
    /// hand-position check is not enough.
    /// </summary>
    public static bool TryDrawOne(ref FlashExec x, bool fromHandDraw, out FlashCard card)
    {
        card = default;
        if (IsOverOrEnding(x.B) || !ShouldDraw(ref x, fromHandDraw))
            return false;
        return DrawOneCore(ref x, fromHandDraw, out card) && !x.NeedsChoice;
    }

    private static bool DrawOneCore(ref FlashExec x, bool fromHandDraw, out FlashCard card)
    {
        card = default;
        if (IsOverOrEnding(x.B) || x.B.HandCount >= x.B.MaxHandSize)
            return false;
        ShuffleIfNecessary(ref x);
        if (x.NeedsChoice)
            return false;
        if (x.B.DrawCount == 0 || x.B.HandCount >= x.B.MaxHandSize)
            return false;
        // AddToPile(top draw card, Hand, Bottom): the draw segment directly follows the hand.
        FlashPiles.DrawTopToHand(ref x.B);
        int position = x.B.HandCount - 1;
        card = x.B.Cards[position];
        RecordCardDrawn(ref x, card, fromHandDraw);
        AfterCardDrawn(ref x, position, fromHandDraw);
        return !x.NeedsChoice;
    }

    /// <summary>Native <c>Hook.ShouldDraw</c> through <c>HookMirrors.ShouldDraw</c>: any combat listener may prevent the draw.</summary>
    private static bool ShouldDraw(ref FlashExec x, bool fromHandDraw)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.ShouldDraw) == 0 || IsOverOrEnding(x.B))
            return true;
        Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        int count = FlashHooks.CollectListeners(x.B, FlashHookMask.ShouldDraw, listeners);
        for (int index = 0; index < count; index++)
        {
            if (!FlashHooks.Definition(x.B, listeners[index]).ShouldDraw(ref x, listeners[index], fromHandDraw))
                return false;
        }
        return true;
    }

    /// <summary>Mirrors <c>SimulatedCombatState.RecordCardDrawn</c>.</summary>
    private static void RecordCardDrawn(ref FlashExec x, in FlashCard card, bool fromHandDraw)
    {
        if (!fromHandDraw)
            x.B.AddCounter(FlashCounter.NonHandDrawsThisTurn, 1);
        if (x.R.Card(card.Id, card.Upgrade).Type == FlashCardType.Status)
            x.B.AddCounter(FlashCounter.StatusCardsDrawnThisTurn, 1);
    }

    /// <summary>
    /// Mirrors <c>HookMirrors.AfterCardDrawn</c>: the Early and normal passes over the combat listeners (gated by
    /// combat ending), then the drawn card's own handler when its listener visit did not claim it.
    /// </summary>
    private static void AfterCardDrawn(ref FlashExec x, int position, bool fromHandDraw)
    {
        int savedPosition = x.DrawnCardPosition;
        bool savedHandled = x.IntrinsicDrawHandled;
        x.DrawnCardPosition = position;
        x.IntrinsicDrawHandled = false;
        AfterCardDrawnEarlyHook early = new() { DrawnPosition = position, FromHandDraw = fromHandDraw };
        FlashHooks.Dispatch(ref x, ref early, gatedByEnding: true);
        if (x.NeedsChoice)
            return;
        AfterCardDrawnHook normal = new() { DrawnPosition = position, FromHandDraw = fromHandDraw };
        FlashHooks.Dispatch(ref x, ref normal, gatedByEnding: true);
        if (x.NeedsChoice)
            return;
        if (!x.IntrinsicDrawHandled)
        {
            FlashCardDef def = FlashRegistry.Card(x.B.Cards[position].Id);
            if ((def.Hooks & FlashHookMask.AfterCardDrawn) != 0)
                def.AfterCardDrawn(ref x, new FlashListener(FlashListenerKind.Card, (byte)position, FlashLimits.PlayerOwner), position, fromHandDraw);
            if (x.NeedsChoice)
                return;
        }
        x.DrawnCardPosition = savedPosition;
        x.IntrinsicDrawHandled = savedHandled;
    }

    /// <summary>
    /// For a card listener: whether this visit is the drawn card's own handler, claimed once per draw
    /// (<c>AfterCardDrawnMirrorContext.IntrinsicCardHandled</c> with <c>Card.References</c>).
    /// </summary>
    public static bool TakeIntrinsicDraw(ref FlashExec x, FlashListener self, int drawnPosition)
    {
        if (x.IntrinsicDrawHandled || self.Kind != FlashListenerKind.Card || self.Index != drawnPosition)
            return false;
        x.IntrinsicDrawHandled = true;
        return true;
    }

    /// <summary>Decimal overload: rounds up like <c>CombatPredictionSimulator.Draw(Player, decimal, bool)</c>.</summary>
    public static void Draw(ref FlashExec x, decimal count, bool fromHandDraw)
        => Draw(ref x, count > 0m ? (int)Math.Ceiling(count) : 0, fromHandDraw);

    internal static void ShuffleIfNecessary(ref FlashExec x)
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
        // CombatPredictionSimulator.Shuffle calls HookMirrors.AfterShuffle only while the combat is
        // not over or ending. The hook then iterates the combat listeners without its own gate.
        if (IsOverOrEnding(x.B))
            return;
        AfterShuffleHook after = new() { Shuffler = FlashLimits.PlayerOwner };
        FlashHooks.Dispatch(ref x, ref after, gatedByEnding: false);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.Exhaust</c>.</summary>
    public static void Exhaust(ref FlashExec x, FlashPile from, int index, bool causedByEthereal = false)
    {
        if (IsOverOrEnding(x.B))
            return;
        FlashCard card = FlashPiles.At(ref x.B, from, index);
        MoveCard(ref x.B, from, index, FlashPile.Exhaust);
        x.B.AddCounter(FlashCounter.CardsExhaustedThisTurn, 1);
        AfterCardExhaustedHook after = new() { Card = card, CausedByEthereal = causedByEthereal };
        FlashHooks.Dispatch(ref x, ref after, gatedByEnding: true);
    }
}
