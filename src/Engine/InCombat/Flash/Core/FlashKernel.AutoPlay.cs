namespace CombatSolver.Engine.InCombat.Flash;

public static partial class FlashKernel
{
    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.AutoPlay</c> for a card that is in a combat pile. The
    /// card play itself reuses the manual pipeline (<see cref="PlayFromPlay"/>) so every hook, counter
    /// and result-pile rule is the same; only the resource and target steps differ.
    /// </summary>
    /// <returns>False when nothing was played or a nested choice is outstanding.</returns>
    public static bool AutoPlay(ref FlashExec x, FlashPile pile, int index, byte target = FlashLimits.NoOwner,
        bool skipXCapture = false)
        => AutoPlay(ref x, pile, index, target, skipXCapture, out _);

    /// <summary>
    /// The same auto-play, also reporting the card's value after its tail hooks (the played value can
    /// change during the play). A failed auto-play reports the value at the source position.
    /// </summary>
    public static bool AutoPlay(ref FlashExec x, FlashPile pile, int index, byte target, bool skipXCapture,
        out FlashCard played)
        => AutoPlay(ref x, pile, index, target, skipXCapture, out played, out _, out _);

    /// <summary>
    /// The same auto-play, reporting the card's post-tail value and its final pile/index (or
    /// <see cref="FlashPile.Play"/>/-1 when it left combat or its pile position cannot be captured).
    /// </summary>
    public static bool AutoPlay(ref FlashExec x, FlashPile pile, int index, byte target, bool skipXCapture,
        out FlashCard played, out FlashPile playedPile, out int playedIndex)
    {
        playedPile = FlashPile.Play;
        playedIndex = -1;
        played = default;
        if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
            return false;
        FlashCard card = FlashPiles.At(ref x.B, pile, index);
        played = card;
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        // A nested auto-play must not leave its own card as the exec's current play: the caller's
        // effect continues afterwards and reads its own card value (HiddenDaggers' Shivs var,
        // StormOfSteel's upgrade level).
        FlashCard callerCard = x.Card;
        FlashCardStats? callerStats = x.Stats;
        byte callerTarget = x.Target;
        bool callerAutoPlay = x.CurrentPlayIsAutoPlay;
        bool callerHistoryCourseDupe = x.CurrentPlayIsHistoryCourseDupe;
        x.CurrentPlayIsHistoryCourseDupe = false;
        if (HasKeyword(x.B, card, FlashKeywords.Unplayable)
            || !ShouldPlay(x.B, x.R, card)
            || !TryResolveAutoPlayTarget(ref x.B, x.R, card, ref target))
        {
            MoveToResultPileWithoutPlaying(ref x, card, pile, index, inPile: true);
            x.CurrentPlayIsHistoryCourseDupe = callerHistoryCourseDupe;
            return false;
        }

        // SpendResources(card, isAutoPlay: true, skipXCapture) captures X and the star value
        // (CardModel.LastStarsSpent) without paying anything.
        int energyValue = EnergyCostWithModifiers(x.B, x.R, card, pile);
        int starValue = StarCostWithModifiers(x.B, x.R, card, pile);
        if (!skipXCapture)
        {
            x.XValue = energyValue;
            x.StarValue = starValue;
        }

        // OnPlayWrapper's isAutoPlay branch moves the card to the play pile.
        FlashCard moved = FlashPiles.RemoveAt(ref x.B, pile, index);
        int playPosition = x.B.PlayCount;
        FlashPiles.Append(ref x.B, FlashPile.Play, moved);
        x.Card = moved;
        x.Stats = stats;
        x.Target = target;
        x.CurrentPlayIsAutoPlay = true;
        PlayFromPlay(ref x, moved, target, energyValue, playPosition, isAutoPlay: true,
            out playedPile, out playedIndex);
        played = x.Card;
        bool completed = !x.NeedsChoice;
        if (completed)
        {
            x.Card = callerCard;
            x.Stats = callerStats;
            x.Target = callerTarget;
            x.CurrentPlayIsAutoPlay = callerAutoPlay;
            x.CurrentPlayIsHistoryCourseDupe = callerHistoryCourseDupe;
        }
        return completed;
    }

    /// <summary>
    /// The <c>card.GetPile(State) is null</c> branch of <c>AutoPlay</c>: a freshly generated card that
    /// is not in any pile is added to the play pile (running <c>AfterCardEnteredCombat</c>) before the
    /// resource step.
    /// </summary>
    public static bool AutoPlay(ref FlashExec x, in FlashCard card, byte target = FlashLimits.NoOwner,
        bool skipXCapture = false, bool historyCourseDupe = false)
    {
        if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
            return false;
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        FlashCard callerCard = x.Card;
        FlashCardStats? callerStats = x.Stats;
        byte callerTarget = x.Target;
        bool callerAutoPlay = x.CurrentPlayIsAutoPlay;
        bool callerHistoryCourseDupe = x.CurrentPlayIsHistoryCourseDupe;
        x.CurrentPlayIsHistoryCourseDupe = historyCourseDupe;
        if (HasKeyword(x.B, card, FlashKeywords.Unplayable)
            || !ShouldPlay(x.B, x.R, card)
            || !TryResolveAutoPlayTarget(ref x.B, x.R, card, ref target))
        {
            MoveToResultPileWithoutPlaying(ref x, card, FlashPile.Play, 0, inPile: false);
            return false;
        }

        int enteredPosition = x.B.PlayCount;
        FlashPiles.Append(ref x.B, FlashPile.Play, card);
        EnteredCombat(ref x, card, FlashPile.Play, enteredPosition);
        int energyValue = EnergyCostWithModifiers(x.B, x.R, card, FlashPile.Play);
        int starValue = StarCostWithModifiers(x.B, x.R, card, FlashPile.Play);
        if (!skipXCapture)
        {
            x.XValue = energyValue;
            x.StarValue = starValue;
        }

        // OnPlayWrapper's AddToPile moves the card to the end of the play pile again. AfterCardEnteredCombat
        // may have mutated the value in place (the one-time affliction check and normalization), so an
        // exact value lookup can fail; fall back to the slot the card was appended to.
        int index = PlayIndexOf(ref x.B, card, enteredPosition);
        FlashCard moved = FlashPiles.RemoveAt(ref x.B, FlashPile.Play, index);
        int playPosition = x.B.PlayCount;
        FlashPiles.Append(ref x.B, FlashPile.Play, moved);
        x.Card = moved;
        x.Stats = stats;
        x.Target = target;
        x.CurrentPlayIsAutoPlay = true;
        PlayFromPlay(ref x, moved, target, energyValue, playPosition, isAutoPlay: true);
        bool completed = !x.NeedsChoice;
        if (completed)
        {
            x.Card = callerCard;
            x.Stats = callerStats;
            x.Target = callerTarget;
            x.CurrentPlayIsAutoPlay = callerAutoPlay;
            x.CurrentPlayIsHistoryCourseDupe = callerHistoryCourseDupe;
        }
        return completed;
    }

    /// <summary>
    /// Queue identity of a card moved to the play pile for <c>AutoPlayFromDrawPile</c>. The existing
    /// engine holds live <c>CardModel</c> references, so state mutations (affliction/enchantment
    /// amounts, PowerAfflictionChecked) during an auto-play do not change the queue order; only a
    /// different card value does. Duplicate cards are interchangeable for that order check.
    /// </summary>
    private static bool SameAutoPlayQueueCard(in FlashCard left, in FlashCard right)
        => left.Id == right.Id && left.Upgrade == right.Upgrade
            && left.AfflictionId == right.AfflictionId && left.EnchantmentId == right.EnchantmentId;

    /// <summary>
    /// Mirrors <c>CardPileCmd.AutoPlayFromDrawPile</c>: first move up to <paramref name="count"/>
    /// cards from the top of the draw pile to the play pile (shuffling between moves), then auto-play
    /// them in order, setting <c>ExhaustOnNextPlay</c> from <paramref name="forceExhaust"/>.
    /// </summary>
    public static void AutoPlayFromDrawPile(ref FlashExec x, int count, bool forceExhaust)
    {
        if (IsOverOrEnding(x.B) || count <= 0)
            return;
        if (count > FlashLimits.Cards)
            throw new FlashCapacityException("AutoPlayCards", FlashLimits.Cards);
        int start = x.B.PlayCount;
        int moved = 0;
        Span<FlashCard> cards = stackalloc FlashCard[count];
        for (int index = 0; index < count; index++)
        {
            ShuffleIfNecessary(ref x);
            if (x.NeedsChoice)
                return;
            if (x.B.DrawCount == 0)
                break;
            FlashCard card = FlashPiles.RemoveAt(ref x.B, FlashPile.Draw, 0);
            FlashPiles.Append(ref x.B, FlashPile.Play, card);
            cards[moved++] = card;
        }
        for (int index = 0; index < moved; index++)
        {
            if (x.B.Player.IsDead)
                return;
            // ContinueAutoPlayCards still writes ExhaustOnNextPlay on every remaining queued card
            // before its AutoPlay call returns immediately because the combat is over/ending. The
            // card that ended the combat moved itself to the tail; the not-yet-played cards stay
            // contiguous at the index recorded before the move phase.
            if (IsOverOrEnding(x.B))
            {
                for (int remaining = index; remaining < moved; remaining++)
                {
                    int position = start + remaining - index;
                    if (position >= x.B.PlayCount)
                        throw new FlashUnsupportedException("自动打出的牌堆顺序在结算中被改变。");
                    ref FlashCard queued = ref FlashPiles.At(ref x.B, FlashPile.Play, position);
                    if (forceExhaust)
                        queued.Flags |= FlashCardFlags.ExhaustOnNextPlay;
                    else
                        queued.Flags &= ~FlashCardFlags.ExhaustOnNextPlay;
                }
                return;
            }
            // The moved cards stay contiguous at the play-pile index recorded before the move phase;
            // earlier auto-plays remove themselves (or move to another pile) and shift the rest left.
            if (start >= x.B.PlayCount || !SameAutoPlayQueueCard(FlashPiles.At(ref x.B, FlashPile.Play, start), cards[index]))
                throw new FlashUnsupportedException("自动打出的牌堆顺序在结算中被改变。");
            ref FlashCard played = ref FlashPiles.At(ref x.B, FlashPile.Play, start);
            if (forceExhaust)
                played.Flags |= FlashCardFlags.ExhaustOnNextPlay;
            else
                played.Flags &= ~FlashCardFlags.ExhaustOnNextPlay;
            AutoPlay(ref x, FlashPile.Play, start);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// Mirrors <c>CardCmd.MoveToResultPileWithoutPlaying</c>: add the card to the play pile, then
    /// exhaust it (dupe or exhaust keyword/next-play flag) or move it to the discard pile. No card-play
    /// hooks, counters or cost cleanup run.
    /// </summary>
    public static void MoveToResultPileWithoutPlaying(ref FlashExec x, FlashPile pile, int index)
    {
        FlashCard card = FlashPiles.At(ref x.B, pile, index);
        MoveToResultPileWithoutPlaying(ref x, card, pile, index, inPile: true);
    }

    private static void MoveToResultPileWithoutPlaying(ref FlashExec x, in FlashCard card, FlashPile pile, int index,
        bool inPile)
    {
        // CardModel.GetResultLocationForCardPlay returns PileType.None for IsDupe: the card leaves
        // combat instead of exhausting/discarding. The HistoryCourse copy is the only dupe Flash creates.
        if (x.CurrentPlayIsHistoryCourseDupe)
        {
            if (inPile)
                FlashPiles.RemoveAt(ref x.B, pile, index);
            return;
        }
        int position;
        if (inPile)
        {
            FlashCard moved = FlashPiles.RemoveAt(ref x.B, pile, index);
            position = x.B.PlayCount;
            FlashPiles.Append(ref x.B, FlashPile.Play, moved);
        }
        else
        {
            position = x.B.PlayCount;
            FlashPiles.Append(ref x.B, FlashPile.Play, card);
            EnteredCombat(ref x, card, FlashPile.Play, position);
            if (x.NeedsChoice)
                return;
        }
        ref FlashCard stored = ref FlashPiles.At(ref x.B, FlashPile.Play, position);
        if ((stored.Flags & FlashCardFlags.ExhaustOnNextPlay) != 0 || HasKeyword(x.B, stored, FlashKeywords.Exhaust))
            Exhaust(ref x, FlashPile.Play, position);
        else
            MoveCard(ref x.B, FlashPile.Play, position, FlashPile.Discard);
    }

    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.TryResolveAutoPlayTarget</c>. AnyEnemy picks a random
    /// hittable enemy from the CombatTargets stream; with no candidate the list is empty and
    /// <c>NextItem</c> does not advance the stream. Single-player has no other ally, so AnyAlly only
    /// succeeds when the caller already supplied a target.
    /// </summary>
    private static bool TryResolveAutoPlayTarget(ref FlashBattle battle, FlashRules rules, in FlashCard card, ref byte target)
    {
        FlashTargetType targetType = FlashRegistry.Card(card.Id).TargetType(battle, rules, card);
        switch (targetType)
        {
            case FlashTargetType.AnyEnemy:
                if (target == FlashLimits.NoOwner)
                    target = RandomHittableEnemy(ref battle);
                return target != FlashLimits.NoOwner;
            case FlashTargetType.AnyAlly:
                return target != FlashLimits.NoOwner;
            default:
                return true;
        }
    }

    /// <summary>
    /// <c>Rng.CombatTargets.NextItem(State.HittableEnemies)</c>: a roster-order random hittable enemy,
    /// or <see cref="FlashLimits.NoOwner"/> without consuming the stream when none can be hit.
    /// </summary>
    public static byte RandomHittableEnemy(ref FlashBattle battle)
    {
        Span<byte> targets = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        for (int position = 0; position < battle.RosterCount; position++)
        {
            byte owner = EnemyOwner(battle.Roster[position]);
            // State.HittableEnemies excludes reviving and permanently dead enemies.
            if (battle.Enemies[battle.Roster[position]].DeathPhase != FlashDeathPhase.None)
                continue;
            if (IsHittable(battle, owner))
                targets[count++] = owner;
        }
        if (count == 0)
            return FlashLimits.NoOwner;
        return targets[battle.RngStream(FlashRngStream.CombatTargets).NextInt(0, count)];
    }

    private static int PlayIndexOf(ref FlashBattle battle, in FlashCard card, int fallbackPosition = -1)
    {
        int count = FlashPiles.Count(battle, FlashPile.Play);
        int start = FlashPiles.Start(battle, FlashPile.Play);
        for (int index = count - 1; index >= 0; index--)
        {
            if (FlashFingerprint.Equal(battle.Cards[start + index], card))
                return index;
        }
        // The appended object is still the same card even if an entry hook changed its value (the
        // one-time Vital Spark/Smog/Galvanic check mutates it). The queue order is that object.
        if ((uint)fallbackPosition < (uint)count)
        {
            ref readonly FlashCard candidate = ref battle.Cards[start + fallbackPosition];
            if (candidate.Id == card.Id && candidate.Upgrade == card.Upgrade)
                return fallbackPosition;
        }
        throw new FlashUnsupportedException("自动打出的卡不在打出区。");
    }
}
