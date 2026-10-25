namespace BlitzSolver.Engine;

public static partial class BlitzKernel
{
    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.AutoPlay</c> for a card that is in a combat pile. The
    /// card play itself reuses the manual pipeline (<see cref="PlayFromPlay"/>) so every hook, counter
    /// and result-pile rule is the same; only the resource and target steps differ.
    /// </summary>
    /// <returns>False when nothing was played or a nested choice is outstanding.</returns>
    public static bool AutoPlay(ref BlitzExec x, BlitzPile pile, int index, byte target = BlitzLimits.NoOwner,
        bool skipXCapture = false)
        => AutoPlay(ref x, pile, index, target, skipXCapture, out _);

    /// <summary>
    /// The same auto-play, also reporting the card's value after its tail hooks (the played value can
    /// change during the play). A failed auto-play reports the value at the source position.
    /// </summary>
    public static bool AutoPlay(ref BlitzExec x, BlitzPile pile, int index, byte target, bool skipXCapture,
        out BlitzCard played)
        => AutoPlay(ref x, pile, index, target, skipXCapture, out played, out _, out _);

    /// <summary>
    /// The same auto-play, reporting the card's post-tail value and its final pile/index (or
    /// <see cref="BlitzPile.Play"/>/-1 when it left combat or its pile position cannot be captured).
    /// </summary>
    public static bool AutoPlay(ref BlitzExec x, BlitzPile pile, int index, byte target, bool skipXCapture,
        out BlitzCard played, out BlitzPile playedPile, out int playedIndex)
    {
        playedPile = BlitzPile.Play;
        playedIndex = -1;
        played = default;
        if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
            return false;
        BlitzCard card = BlitzPiles.At(ref x.B, pile, index);
        played = card;
        BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
        // A nested auto-play must not leave its own card as the exec's current play: the caller's
        // effect continues afterwards and reads its own card value (HiddenDaggers' Shivs var,
        // StormOfSteel's upgrade level).
        BlitzCard callerCard = x.Card;
        BlitzCardStats? callerStats = x.Stats;
        byte callerTarget = x.Target;
        bool callerAutoPlay = x.CurrentPlayIsAutoPlay;
        bool callerHistoryCourseDupe = x.CurrentPlayIsHistoryCourseDupe;
        x.CurrentPlayIsHistoryCourseDupe = false;
        if (HasKeyword(x.B, card, BlitzKeywords.Unplayable)
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
        BlitzCard moved = BlitzPiles.RemoveAt(ref x.B, pile, index);
        int playPosition = x.B.PlayCount;
        BlitzPiles.Append(ref x.B, BlitzPile.Play, moved);
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
    public static bool AutoPlay(ref BlitzExec x, in BlitzCard card, byte target = BlitzLimits.NoOwner,
        bool skipXCapture = false, bool historyCourseDupe = false)
        => AutoPlay(ref x, card, target, skipXCapture, historyCourseDupe, out _, out _, out _);

    /// <summary>
    /// The same no-pile branch, reporting the post-tail card value and its final pile/index so a
    /// repeated auto-play loop can continue like the existing engine's live CardModel reference.
    /// </summary>
    public static bool AutoPlay(ref BlitzExec x, in BlitzCard card, byte target, bool skipXCapture,
        bool historyCourseDupe, out BlitzCard played, out BlitzPile playedPile, out int playedIndex)
    {
        played = card;
        playedPile = BlitzPile.Play;
        playedIndex = -1;
        if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
            return false;
        BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
        BlitzCard callerCard = x.Card;
        BlitzCardStats? callerStats = x.Stats;
        byte callerTarget = x.Target;
        bool callerAutoPlay = x.CurrentPlayIsAutoPlay;
        bool callerHistoryCourseDupe = x.CurrentPlayIsHistoryCourseDupe;
        x.CurrentPlayIsHistoryCourseDupe = historyCourseDupe;
        if (HasKeyword(x.B, card, BlitzKeywords.Unplayable)
            || !ShouldPlay(x.B, x.R, card)
            || !TryResolveAutoPlayTarget(ref x.B, x.R, card, ref target))
        {
            MoveToResultPileWithoutPlaying(ref x, card, BlitzPile.Play, 0, inPile: false);
            return false;
        }

        int enteredPosition = x.B.PlayCount;
        BlitzPiles.Append(ref x.B, BlitzPile.Play, card);
        EnteredCombat(ref x, card, BlitzPile.Play, enteredPosition);
        int energyValue = EnergyCostWithModifiers(x.B, x.R, card, BlitzPile.Play);
        int starValue = StarCostWithModifiers(x.B, x.R, card, BlitzPile.Play);
        if (!skipXCapture)
        {
            x.XValue = energyValue;
            x.StarValue = starValue;
        }

        // OnPlayWrapper's AddToPile moves the card to the end of the play pile again. AfterCardEnteredCombat
        // may have mutated the value in place (the one-time affliction check and normalization), so an
        // exact value lookup can fail; fall back to the slot the card was appended to.
        int index = PlayIndexOf(ref x.B, card, enteredPosition);
        BlitzCard moved = BlitzPiles.RemoveAt(ref x.B, BlitzPile.Play, index);
        int playPosition = x.B.PlayCount;
        BlitzPiles.Append(ref x.B, BlitzPile.Play, moved);
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
    /// Queue identity of a card moved to the play pile for <c>AutoPlayFromDrawPile</c>. The existing
    /// engine holds live <c>CardModel</c> references, so state mutations during an auto-play (upgrade,
    /// affliction/enchantment, cost modifiers, PowerAfflictionChecked) do not change the queue order;
    /// only a different native card or a clone versus its original does. Duplicate cards are
    /// interchangeable for that order check.
    /// </summary>
    private static bool SameAutoPlayQueueCard(in BlitzCard left, in BlitzCard right)
        => left.Id == right.Id && left.IsClone == right.IsClone;

    /// <summary>
    /// Mirrors <c>CardPileCmd.AutoPlayFromDrawPile</c>: first move up to <paramref name="count"/>
    /// cards from the top of the draw pile to the play pile (shuffling between moves), then auto-play
    /// them in order, setting <c>ExhaustOnNextPlay</c> from <paramref name="forceExhaust"/>.
    /// </summary>
    public static void AutoPlayFromDrawPile(ref BlitzExec x, int count, bool forceExhaust)
    {
        if (IsOverOrEnding(x.B) || count <= 0)
            return;
        if (count > BlitzLimits.Cards)
            throw new BlitzCapacityException("AutoPlayCards", BlitzLimits.Cards);
        int start = x.B.PlayCount;
        int moved = 0;
        Span<BlitzCard> cards = stackalloc BlitzCard[count];
        for (int index = 0; index < count; index++)
        {
            ShuffleIfNecessary(ref x);
            if (x.NeedsChoice)
                return;
            if (x.B.DrawCount == 0)
                break;
            BlitzCard card = BlitzPiles.RemoveAt(ref x.B, BlitzPile.Draw, 0);
            BlitzPiles.Append(ref x.B, BlitzPile.Play, card);
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
                        throw new BlitzUnsupportedException("自动打出的牌堆顺序在结算中被改变。");
                    ref BlitzCard queued = ref BlitzPiles.At(ref x.B, BlitzPile.Play, position);
                    if (forceExhaust)
                        queued.Flags |= BlitzCardFlags.ExhaustOnNextPlay;
                    else
                        queued.Flags &= ~BlitzCardFlags.ExhaustOnNextPlay;
                }
                return;
            }
            // The moved cards stay contiguous at the play-pile index recorded before the move phase;
            // earlier auto-plays remove themselves (or move to another pile) and shift the rest left.
            if (start >= x.B.PlayCount || !SameAutoPlayQueueCard(BlitzPiles.At(ref x.B, BlitzPile.Play, start), cards[index]))
                throw new BlitzUnsupportedException("自动打出的牌堆顺序在结算中被改变。");
            ref BlitzCard played = ref BlitzPiles.At(ref x.B, BlitzPile.Play, start);
            if (forceExhaust)
                played.Flags |= BlitzCardFlags.ExhaustOnNextPlay;
            else
                played.Flags &= ~BlitzCardFlags.ExhaustOnNextPlay;
            AutoPlay(ref x, BlitzPile.Play, start);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// Mirrors <c>CardCmd.MoveToResultPileWithoutPlaying</c>: add the card to the play pile, then
    /// exhaust it (dupe or exhaust keyword/next-play flag) or move it to the discard pile. No card-play
    /// hooks, counters or cost cleanup run.
    /// </summary>
    public static void MoveToResultPileWithoutPlaying(ref BlitzExec x, BlitzPile pile, int index)
    {
        BlitzCard card = BlitzPiles.At(ref x.B, pile, index);
        MoveToResultPileWithoutPlaying(ref x, card, pile, index, inPile: true);
    }

    private static void MoveToResultPileWithoutPlaying(ref BlitzExec x, in BlitzCard card, BlitzPile pile, int index,
        bool inPile)
    {
        // CardModel.GetResultLocationForCardPlay returns PileType.None for IsDupe: the card leaves
        // combat instead of exhausting/discarding. The HistoryCourse copy is the only dupe Blitz creates.
        if (x.CurrentPlayIsHistoryCourseDupe)
        {
            if (inPile)
                BlitzPiles.RemoveAt(ref x.B, pile, index);
            return;
        }
        int position;
        if (inPile)
        {
            BlitzCard moved = BlitzPiles.RemoveAt(ref x.B, pile, index);
            position = x.B.PlayCount;
            BlitzPiles.Append(ref x.B, BlitzPile.Play, moved);
        }
        else
        {
            position = x.B.PlayCount;
            BlitzPiles.Append(ref x.B, BlitzPile.Play, card);
            EnteredCombat(ref x, card, BlitzPile.Play, position);
            if (x.NeedsChoice)
                return;
        }
        ref BlitzCard stored = ref BlitzPiles.At(ref x.B, BlitzPile.Play, position);
        if ((stored.Flags & BlitzCardFlags.ExhaustOnNextPlay) != 0 || HasKeyword(x.B, stored, BlitzKeywords.Exhaust))
            Exhaust(ref x, BlitzPile.Play, position);
        else
            MoveCard(ref x.B, BlitzPile.Play, position, BlitzPile.Discard);
    }

    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.TryResolveAutoPlayTarget</c>. AnyEnemy picks a random
    /// hittable enemy from the CombatTargets stream; with no candidate the list is empty and
    /// <c>NextItem</c> does not advance the stream. Single-player has no other ally, so AnyAlly only
    /// succeeds when the caller already supplied a target.
    /// </summary>
    private static bool TryResolveAutoPlayTarget(ref BlitzBattle battle, BlitzRules rules, in BlitzCard card, ref byte target)
    {
        BlitzTargetType targetType = BlitzRegistry.Card(card.Id).TargetType(battle, rules, card);
        switch (targetType)
        {
            case BlitzTargetType.AnyEnemy:
                if (target == BlitzLimits.NoOwner)
                    target = RandomHittableEnemy(ref battle);
                return target != BlitzLimits.NoOwner;
            case BlitzTargetType.AnyAlly:
                return target != BlitzLimits.NoOwner;
            default:
                return true;
        }
    }

    /// <summary>
    /// <c>Rng.CombatTargets.NextItem(State.HittableEnemies)</c>: a roster-order random hittable enemy,
    /// or <see cref="BlitzLimits.NoOwner"/> without consuming the stream when none can be hit.
    /// </summary>
    public static byte RandomHittableEnemy(ref BlitzBattle battle)
    {
        Span<byte> targets = stackalloc byte[BlitzLimits.Enemies];
        int count = 0;
        for (int position = 0; position < battle.RosterCount; position++)
        {
            byte owner = EnemyOwner(battle.Roster[position]);
            // State.HittableEnemies excludes reviving and permanently dead enemies.
            if (battle.Enemies[battle.Roster[position]].DeathPhase != BlitzDeathPhase.None)
                continue;
            if (IsHittable(battle, owner))
                targets[count++] = owner;
        }
        if (count == 0)
            return BlitzLimits.NoOwner;
        return targets[battle.RngStream(BlitzRngStream.CombatTargets).NextInt(0, count)];
    }

    private static int PlayIndexOf(ref BlitzBattle battle, in BlitzCard card, int fallbackPosition = -1)
    {
        int count = BlitzPiles.Count(battle, BlitzPile.Play);
        int start = BlitzPiles.Start(battle, BlitzPile.Play);
        for (int index = count - 1; index >= 0; index--)
        {
            if (BlitzFingerprint.Equal(battle.Cards[start + index], card))
                return index;
        }
        // The appended object is still the same card even if an entry hook changed its value (the
        // one-time Vital Spark/Smog/Galvanic check mutates it). The queue order is that object.
        if ((uint)fallbackPosition < (uint)count)
        {
            ref readonly BlitzCard candidate = ref battle.Cards[start + fallbackPosition];
            if (candidate.Id == card.Id && candidate.Upgrade == card.Upgrade)
                return fallbackPosition;
        }
        throw new BlitzUnsupportedException("自动打出的卡不在打出区。");
    }
}
