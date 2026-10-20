namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// The AutoPrePlay phase at the player's turn start, mirroring
/// <c>SimulatedCombatState.TriggerAutoPrePlayEarly</c> / <c>ContinueAutoPrePlay</c>: the exhaust pile's
/// Bombardment cards first, then the scheduled auto-plays (Mayhem's amount of draw-pile top cards moved
/// to the play pile, then HistoryCourse's clone of the previous player turn's last attack), then the
/// Imbued enchantment's auto-plays. WhisperingEarring remains a scheduled source with no combat effect
/// (its only first-turn auto-play is a relic trigger with no state in either engine).
/// </summary>
public static partial class FlashKernel
{
    internal const string BombardmentId = "BOMBARDMENT";
    internal const string ImbuedEnchantmentId = "IMBUED";

    internal static void AutoPrePlay(ref FlashExec x)
    {
        if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
            return;
        // The current slot is the previous turn's last attack (set by CommitHistoryCourseTurn at the
        // prior turn end). Take it before any Bombardment/Mayhem auto-play records this turn's last
        // attack into the same slot; the existing engine keeps the two dictionaries separate.
        FlashCard historyCoursePrevious = default;
        bool hasHistoryCoursePrevious = false;
        if (x.R.HasHistoryCourse)
        {
            hasHistoryCoursePrevious = (x.B.Flags & FlashBattleFlags.HistoryCourseCardPresent) != 0;
            historyCoursePrevious = x.B.HistoryCourseCard;
            x.B.HistoryCourseCard = default;
            x.B.Flags &= ~FlashBattleFlags.HistoryCourseCardPresent;
        }
        AutoPlayBombardments(ref x);
        if (x.NeedsChoice)
            return;
        AutoPlayScheduled(ref x);
        if (x.NeedsChoice)
            return;
        if (hasHistoryCoursePrevious)
        {
            AutoPlayHistoryCourse(ref x, historyCoursePrevious);
            if (x.NeedsChoice)
                return;
        }
        AutoPlayImbued(ref x);
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

    /// <summary>
    /// <c>SimulatedCombatState.ContinueScheduledAutoPlays</c>' HistoryCourse stage (after Mayhem's
    /// cards, before the Imbued stage): for every non-melted HistoryCourse relic, clone the previous
    /// player turn's last attack and auto-play the clone. <c>CreateDupeForPlayer</c> is a clone with
    /// <c>IsDupe = true</c> and the Exhaust keyword removed; the kernel's dupe flag makes the play
    /// leave combat instead of moving to a result pile and keeps it out of the recorded last attack.
    /// </summary>
    private static void AutoPlayHistoryCourse(ref FlashExec x, in FlashCard previous)
    {
        if (x.B.PlayerTurn <= 1 || !FlashRegistry.TryRelic("HISTORY_COURSE", out ushort historyCourse))
            return;
        for (int relic = 0; relic < x.B.RelicCount; relic++)
        {
            ref readonly FlashRelic row = ref x.B.Relics[relic];
            if (row.Id != historyCourse || (row.Flags & FlashRelicFlags.Melted) != 0)
                continue;
            FlashCard copy = CloneCard(previous);
            copy.Keywords &= ~FlashKeywords.Exhaust;
            // The clone is a new object: it is not in the existing engine's _returnToHandNextTurn set.
            copy.ReturnToHandNextTurn = false;
            AutoPlay(ref x, copy, FlashLimits.NoOwner, skipXCapture: false, historyCourseDupe: true);
            if (x.NeedsChoice)
                return;
            if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
                return;
        }
    }

    /// <summary>
    /// <c>EnchantmentLifecycleSupport.TriggerAutoPrePlay</c>: on the first player turn, auto-play every
    /// card carrying the Imbued enchantment in <c>AllCards</c> order (hand, draw, discard, exhaust,
    /// play). The existing engine snapshots the card objects before the loop and keeps playing those
    /// objects. Flash has no card identity, so it snapshots each card's absolute position plus a stable
    /// key (id, enchantment, affliction, clone) and tracks the position through the net move of every
    /// auto-play. A position whose key no longer matches, a changed card count (generated/removed cards)
    /// or a played card whose final position cannot be captured is an explicit boundary: the kernel
    /// must not guess which copy the existing engine would play. Cost modifiers, upgrades and the
    /// one-time affliction-normalization bit may change in place without moving the card and are read
    /// from the current value.
    /// </summary>
    private static void AutoPlayImbued(ref FlashExec x)
    {
        if (x.B.PlayerTurn > 1 || !FlashRegistry.TryEnchantment(ImbuedEnchantmentId, out ushort imbued))
            return;
        int count = 0;
        Span<ImbuedCardKey> keys = stackalloc ImbuedCardKey[FlashLimits.Cards];
        Span<int> positions = stackalloc int[FlashLimits.Cards];
        byte enchantment = (byte)(imbued + 1);
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref readonly FlashCard card = ref x.B.Cards[position];
            if (card.EnchantmentId != enchantment)
                continue;
            keys[count] = ImbuedCardKey.Of(card);
            positions[count] = position;
            count++;
        }
        for (int index = 0; index < count; index++)
        {
            if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
                return;
            int position = positions[index];
            if ((uint)position >= (uint)x.B.CardCount || !keys[index].Matches(x.B.Cards[position]))
                throw new FlashUnsupportedException(
                    $"Imbued 自动预打出的卡位置已变化：{FlashRegistry.Card(keys[index].Id).NativeId}");
            int before = x.B.CardCount;
            FlashCard sourceCard = x.B.Cards[position];
            FlashPile sourcePile = FlashPiles.PileOf(x.B, position, out int sourceIndex);
            bool played = AutoPlay(ref x, sourcePile, sourceIndex, FlashLimits.NoOwner, skipXCapture: false,
                out _, out FlashPile resultPile, out int resultIndex);
            if (x.NeedsChoice)
                return;
            int destination;
            if (!played)
            {
                // MoveToResultPileWithoutPlaying does not run play hooks; the card goes to the exhaust
                // pile when it would exhaust, otherwise to the discard pile, at the end.
                if (x.B.CardCount != before)
                    throw new FlashUnsupportedException("Imbued 未打出的卡改变了战斗牌数。");
                bool exhaust = (sourceCard.Flags & FlashCardFlags.ExhaustOnNextPlay) != 0
                    || HasKeyword(x.B, sourceCard, FlashKeywords.Exhaust);
                FlashPile target = exhaust ? FlashPile.Exhaust : FlashPile.Discard;
                destination = FlashPiles.Start(x.B, target) + FlashPiles.Count(x.B, target) - 1;
                if (!FlashCardEffects.Equal(x.B.Cards[destination], sourceCard))
                    throw new FlashUnsupportedException("Imbued 未打出的卡去向无法捕获。");
            }
            else if (resultIndex < 0)
            {
                if (x.B.CardCount != before - 1)
                    throw new FlashUnsupportedException("Imbued 打出的卡离场时战斗牌数不一致。");
                destination = -1;
            }
            else
            {
                if (x.B.CardCount != before)
                    throw new FlashUnsupportedException("Imbued 打出的卡改变了战斗牌数。");
                destination = FlashPiles.Start(x.B, resultPile) + resultIndex;
            }
            for (int later = index + 1; later < count; later++)
                positions[later] = MoveCardPosition(positions[later], position, destination);
        }
    }

    /// <summary>Net position of an untouched card after one card moved from <paramref name="source"/> to <paramref name="destination"/> (-1 = left combat).</summary>
    private static int MoveCardPosition(int position, int source, int destination)
    {
        int shifted = position > source ? position - 1 : position;
        if (destination >= 0 && shifted >= destination)
            shifted++;
        return shifted;
    }

    /// <summary>
    /// The stable part of a card's identity for the Imbued snapshot: the fields an in-place effect does
    /// not change except through a transform/upgrade (upgrade is intentionally excluded so Dampen's
    /// re-leveling still matches the same object).
    /// </summary>
    private readonly struct ImbuedCardKey
    {
        private readonly ushort _id;
        private readonly byte _enchantment;
        private readonly byte _affliction;
        private readonly bool _clone;

        private ImbuedCardKey(in FlashCard card)
        {
            _id = card.Id;
            _enchantment = card.EnchantmentId;
            _affliction = card.AfflictionId;
            _clone = card.IsClone;
        }

        public ushort Id => _id;

        public static ImbuedCardKey Of(in FlashCard card) => new(card);

        public bool Matches(in FlashCard card)
            => card.Id == _id && card.EnchantmentId == _enchantment && card.AfflictionId == _affliction
                && card.IsClone == _clone;
    }
}
