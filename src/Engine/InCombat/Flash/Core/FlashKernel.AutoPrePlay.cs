namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// The AutoPrePlay phase at the player's turn start, mirroring
/// <c>SimulatedCombatState.TriggerAutoPrePlayEarly</c> / <c>ContinueAutoPrePlay</c>: the exhaust pile's
/// Bombardment cards first, then the scheduled auto-plays (Mayhem's amount of draw-pile top cards moved
/// to the play pile). Imbued, HistoryCourse and WhisperingEarring are the remaining scheduled sources;
/// none of them has a Flash definition, so admission rejects a root that holds them.
/// </summary>
public static partial class FlashKernel
{
    internal const string BombardmentId = "BOMBARDMENT";

    internal static void AutoPrePlay(ref FlashExec x)
    {
        if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
            return;
        AutoPlayBombardments(ref x);
        if (x.NeedsChoice)
            return;
        AutoPlayScheduled(ref x);
    }

    /// <summary>
    /// <c>ContinueAutoPrePlay</c>'s Bombardments stage: the Bombardment instances that were in the
    /// exhaust pile when the phase started, in exhaust order. A played Bombardment returns to the
    /// exhaust pile (its result is Exhaust), so the pile still holds exactly the captured count; the
    /// next unplayed copy of a value is always its first copy in the pile, because every played copy
    /// was appended after the unplayed ones.
    /// </summary>
    private static void AutoPlayBombardments(ref FlashExec x)
    {
        if (!FlashRegistry.TryCard(BombardmentId, out ushort bombardment))
            return;
        Span<FlashCard> captured = stackalloc FlashCard[FlashLimits.Cards];
        int count = 0;
        int exhaustStart = FlashPiles.Start(x.B, FlashPile.Exhaust);
        for (int offset = 0; offset < x.B.ExhaustCount; offset++)
        {
            FlashCard card = x.B.Cards[exhaustStart + offset];
            if (card.Id == bombardment)
                captured[count++] = card;
        }
        for (int index = 0; index < count; index++)
        {
            if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
                return;
            int start = FlashPiles.Start(x.B, FlashPile.Exhaust);
            int present = 0;
            for (int offset = 0; offset < x.B.ExhaustCount; offset++)
            {
                if (x.B.Cards[start + offset].Id == bombardment)
                    present++;
            }
            if (present != count)
                throw new FlashUnsupportedException("自动预打出期间消耗堆的轰击数量发生变化。");
            int position = -1;
            for (int offset = 0; offset < x.B.ExhaustCount; offset++)
            {
                FlashCard candidate = x.B.Cards[start + offset];
                if (candidate.Id == captured[index].Id && candidate.Upgrade == captured[index].Upgrade)
                {
                    position = offset;
                    break;
                }
            }
            if (position < 0)
                throw new FlashUnsupportedException("自动预打出的轰击已不在消耗堆。");
            AutoPlay(ref x, FlashPile.Exhaust, position);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// <c>ContinueScheduledAutoPlays</c>' Mayhem stage: MayhemPower's amount of draw-pile top cards
    /// are moved to the play pile first (<c>MoveCardsForAutoPlay</c>, shuffling when the draw pile is
    /// empty) and then auto-played in that order.
    /// </summary>
    private static void AutoPlayScheduled(ref FlashExec x)
    {
        if (!FlashRegistry.TryPower("MAYHEM_POWER", out ushort mayhem))
            return;
        int count = GetAmount(x.B, FlashLimits.PlayerOwner, mayhem);
        if (count <= 0)
            return;
        if (count > FlashLimits.Cards)
            throw new FlashCapacityException("AutoPlayCards", FlashLimits.Cards);
        Span<FlashCard> captured = stackalloc FlashCard[count];
        int moved = 0;
        for (int index = 0; index < count; index++)
        {
            ShuffleIfNecessary(ref x);
            if (x.NeedsChoice)
                return;
            if (x.B.DrawCount == 0)
                break;
            FlashCard card = FlashPiles.RemoveAt(ref x.B, FlashPile.Draw, 0);
            FlashPiles.Append(ref x.B, FlashPile.Play, card);
            captured[moved++] = card;
        }
        for (int index = 0; index < moved; index++)
        {
            if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
                return;
            int playStart = FlashPiles.Start(x.B, FlashPile.Play);
            int position = -1;
            for (int offset = 0; offset < x.B.PlayCount; offset++)
            {
                FlashCard candidate = x.B.Cards[playStart + offset];
                if (candidate.Id == captured[index].Id && candidate.Upgrade == captured[index].Upgrade)
                {
                    position = offset;
                    break;
                }
            }
            if (position < 0)
                throw new FlashUnsupportedException("自动预打出队列中的卡已不在打出区。");
            AutoPlay(ref x, FlashPile.Play, position);
            if (x.NeedsChoice)
                return;
        }
    }
}
