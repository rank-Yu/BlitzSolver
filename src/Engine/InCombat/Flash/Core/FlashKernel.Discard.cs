namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// The card-effect discard pipeline, mirroring <c>CombatPredictionSimulator.Discard</c> /
/// <c>DiscardAndDraw</c> (<c>CardCmd.Discard</c>). The turn-end hand flush is separate: the existing
/// engine moves those cards with <c>AddToPile</c> directly, so no discard hook or Sly auto-play runs
/// there (<c>CorePowerSupport.FlushPlayerHandAtTurnEnd</c>).
/// </summary>
public static partial class FlashKernel
{
    /// <summary>Native <c>CardModel.IsSlyThisTurn</c>: the Sly keyword or the single-turn flag.</summary>
    public static bool IsSlyThisTurn(in FlashBattle battle, in FlashCard card)
        => HasKeyword(battle, card, FlashKeywords.Sly) || (card.Flags & FlashCardFlags.SlyThisTurn) != 0;

    /// <summary>Mirrors <c>CombatPredictionSimulator.Discard(PredictedCard)</c>: one card, its hook, then Sly auto-play.</summary>
    public static void Discard(ref FlashExec x, FlashPile pile, int index)
    {
        if (IsOverOrEnding(x.B))
            return;
        FlashCard card = FlashPiles.At(ref x.B, pile, index);
        bool sly = IsSlyThisTurn(x.B, card);
        int occurrence = CountEqualInPile(x.B, FlashPile.Discard, card);
        bool moved = MoveCard(ref x.B, pile, index, FlashPile.Discard);
        x.B.AddCounter(FlashCounter.CardsDiscardedThisTurn, 1);
        AfterCardDiscardedHook hook = new() { Card = card };
        FlashHooks.Dispatch(ref x, ref hook, gatedByEnding: true);
        if (x.NeedsChoice || !moved || !sly)
            return;
        int position = LocateOccurrence(x.B, FlashPile.Discard, card, occurrence);
        AutoPlay(ref x, FlashPile.Discard, position);
    }

    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.DiscardAndDraw</c> / <c>ContinueDiscardAndDrawExecution</c>:
    /// discard every selected card in selection order (counter and <c>AfterCardDiscarded</c> after each),
    /// then draw, then auto-play the cards that were Sly at discard time in that same order.
    /// <paramref name="selected"/> holds indexes into <paramref name="pile"/>, in the existing engine's
    /// option order.
    /// </summary>
    /// <remarks>
    /// The auto-play happens after the draw, so a shuffle may have moved the discarded card into the
    /// draw pile. Flash has no card identity, so each Sly card is located by value and its occurrence
    /// among byte-equal cards over the whole battle, captured when it was discarded. The existing
    /// engine holds the card object and auto-plays it from whichever pile it is in.
    /// </remarks>
    public static void DiscardAndDraw(ref FlashExec x, FlashPile pile, scoped ReadOnlySpan<int> selected, int drawCount)
    {
        if (IsOverOrEnding(x.B) || selected.IsEmpty && drawCount == 0)
            return;
        Span<SelectedCard> cards = stackalloc SelectedCard[selected.Length];
        SnapshotSelected(x.B, pile, selected, cards);
        Span<SelectedCard> sly = stackalloc SelectedCard[selected.Length];
        int slyCount = 0;
        // The existing engine keeps the discarded CardModel and auto-plays it from whichever pile the
        // draw moved it to. Two byte-equal copies are not interchangeable: playing a copy that is still
        // in hand (or that stayed in the discard pile) leaves a different pile distribution, so each
        // discarded Sly card gets a one-action identity byte that travels with its value through the
        // shuffle and draw. Marks 64..255 are reserved for these batches; BeatDown uses 1..63.
        int markBase = x.TransientMarkBase;
        if (64 + markBase + selected.Length > 256)
            throw new FlashCapacityException("TransientMarks", 256 - 64);
        x.TransientMarkBase = checked((byte)(markBase + selected.Length));
        int markStart = 64 + markBase;
        try
        {
            for (int item = 0; item < cards.Length; item++)
            {
                int index = LocateSelected(x.B, pile, cards, item);
                FlashCard card = FlashPiles.At(ref x.B, pile, index);
                bool isSly = IsSlyThisTurn(x.B, card);
                // AddToPile(card, Discard) appends at the bottom; its result is ignored by the existing
                // loop, which still records the discard and dispatches the hook.
                MoveCard(ref x.B, pile, index, FlashPile.Discard);
                x.B.AddCounter(FlashCounter.CardsDiscardedThisTurn, 1);
                AfterCardDiscardedHook hook = new() { Card = card };
                FlashHooks.Dispatch(ref x, ref hook, gatedByEnding: true);
                if (x.NeedsChoice)
                    return;
                if (!isSly)
                    continue;
                // AddToPile appended the card at the bottom of the discard pile. A hook may have
                // removed or moved it; if the same copy is still there, its mark travels with it
                // through the draw. Otherwise no mark exists and the auto-play reports unsupported.
                byte mark = checked((byte)(markStart + slyCount));
                sly[slyCount++] = new SelectedCard { Card = card, Position = mark };
                if (x.B.DiscardCount > 0
                    && FlashFingerprint.Equal(FlashPiles.At(ref x.B, FlashPile.Discard, x.B.DiscardCount - 1), card))
                {
                    x.B.Cards[FlashPiles.Start(x.B, FlashPile.Discard) + x.B.DiscardCount - 1].TransientMark = mark;
                }
            }
            Draw(ref x, drawCount, fromHandDraw: false);
            if (x.NeedsChoice)
                return;
            for (int item = 0; item < slyCount; item++)
            {
                if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
                    return;
                if (!TryFindMarked(x.B, (byte)sly[item].Position, out FlashPile currentPile, out int currentIndex))
                {
                    // A hook cleared the identity byte or removed the card from every combat pile;
                    // the object identity the existing engine holds cannot be reproduced.
                    throw new FlashUnsupportedException(
                        $"弃牌 {FlashRegistry.Card(sly[item].Card.Id).NativeId}+{sly[item].Card.Upgrade} " +
                        $"在自动打出前失去瞬态标记（cards={x.B.CardCount}）。");
                }
                x.B.Cards[FlashPiles.Start(x.B, currentPile) + currentIndex].TransientMark = 0;
                AutoPlay(ref x, currentPile, currentIndex);
                if (x.NeedsChoice)
                    return;
            }
        }
        finally
        {
            for (int position = 0; position < x.B.CardCount; position++)
            {
                if (x.B.Cards[position].TransientMark >= markStart
                    && x.B.Cards[position].TransientMark < markStart + slyCount)
                {
                    x.B.Cards[position].TransientMark = 0;
                }
            }
            x.TransientMarkBase = checked((byte)markBase);
        }
    }

    /// <summary>Finds the card carrying the one-action identity byte and its current pile position.</summary>
    private static bool TryFindMarked(in FlashBattle battle, byte mark, out FlashPile pile, out int index)
    {
        for (int absolute = 0; absolute < battle.CardCount; absolute++)
        {
            if (battle.Cards[absolute].TransientMark != mark)
                continue;
            pile = FlashPiles.PileOf(battle, absolute, out index);
            return true;
        }
        pile = FlashPile.Hand;
        index = -1;
        return false;
    }

    /// <summary>Discards every card in the hand, then draws <paramref name="drawCount"/> (CalculatedGamble, ShadowStep).</summary>
    public static void DiscardAllInHand(ref FlashExec x, int drawCount)
    {
        Span<int> selected = stackalloc int[x.B.HandCount];
        for (int index = 0; index < selected.Length; index++)
            selected[index] = index;
        DiscardAndDraw(ref x, FlashPile.Hand, selected, drawCount);
    }

    /// <summary>Number of cards byte-equal to <paramref name="card"/> currently in <paramref name="pile"/>.</summary>
    private static int CountEqualInPile(in FlashBattle battle, FlashPile pile, in FlashCard card)
    {
        ReadOnlySpan<FlashCard> cards = FlashPiles.ReadCards(battle, pile);
        int count = 0;
        foreach (FlashCard candidate in cards)
        {
            if (FlashFingerprint.Equal(candidate, card))
                count++;
        }
        return count;
    }

    /// <summary>Number of cards byte-equal to <paramref name="card"/> before absolute position <paramref name="position"/>.</summary>
    private static int CountEqualBefore(in FlashBattle battle, int position, in FlashCard card)
    {
        int count = 0;
        for (int index = 0; index < position; index++)
        {
            if (FlashFingerprint.Equal(battle.Cards[index], card))
                count++;
        }
        return count;
    }

    /// <summary>Finds the <paramref name="occurrence"/>-th byte-equal card over the whole battle and its pile.</summary>
    private static bool TryLocateAnywhere(in FlashBattle battle, in FlashCard card, int occurrence,
        out FlashPile pile, out int index)
    {
        int found = -1;
        for (int position = 0; position < battle.CardCount; position++)
        {
            if (!FlashFingerprint.Equal(battle.Cards[position], card))
                continue;
            if (occurrence-- == 0)
            {
                found = position;
                break;
            }
        }
        if (found < 0)
        {
            pile = FlashPile.Hand;
            index = -1;
            return false;
        }
        ReadOnlySpan<FlashPile> piles = [FlashPile.Hand, FlashPile.Draw, FlashPile.Discard, FlashPile.Exhaust, FlashPile.Play];
        foreach (FlashPile candidate in piles)
        {
            int start = FlashPiles.Start(battle, candidate);
            int count = FlashPiles.Count(battle, candidate);
            if (found < start || found >= start + count)
                continue;
            pile = candidate;
            index = found - start;
            return true;
        }
        pile = FlashPile.Hand;
        index = -1;
        return false;
    }

    /// <summary>
    /// Pile index of the <paramref name="occurrence"/>-th (0-based) card byte-equal to
    /// <paramref name="card"/>. Returns -1 when that card is no longer in the pile.
    /// </summary>
    private static int LocateOccurrence(in FlashBattle battle, FlashPile pile, in FlashCard card, int occurrence)
    {
        ReadOnlySpan<FlashCard> cards = FlashPiles.ReadCards(battle, pile);
        for (int index = 0; index < cards.Length; index++)
        {
            if (!FlashFingerprint.Equal(cards[index], card))
                continue;
            if (occurrence-- == 0)
                return index;
        }
        throw new FlashUnsupportedException("弃牌在结算前已离开弃牌堆。");
    }
}
