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
    {
        if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
            return false;
        FlashCard card = FlashPiles.At(ref x.B, pile, index);
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        if (card.HasKeyword(FlashKeywords.Unplayable)
            || !ShouldPlay(x.B, x.R, card)
            || !TryResolveAutoPlayTarget(ref x.B, stats, ref target))
        {
            MoveToResultPileWithoutPlaying(ref x, card, pile, index, inPile: true);
            return false;
        }

        // SpendResources(card, isAutoPlay: true, skipXCapture) captures X and the star value without
        // paying anything. Flash has no persistent LastStarsSpent slot; no supported card reads it.
        int energyValue = EnergyCostWithModifiers(x.B, x.R, card, pile);
        if (!skipXCapture)
            x.XValue = energyValue;

        // OnPlayWrapper's isAutoPlay branch moves the card to the play pile.
        FlashCard moved = FlashPiles.RemoveAt(ref x.B, pile, index);
        int playPosition = x.B.PlayCount;
        FlashPiles.Append(ref x.B, FlashPile.Play, moved);
        x.Card = moved;
        x.Stats = stats;
        x.Target = target;
        PlayFromPlay(ref x, moved, target, energyValue, playPosition, isAutoPlay: true);
        return !x.NeedsChoice;
    }

    /// <summary>
    /// The <c>card.GetPile(State) is null</c> branch of <c>AutoPlay</c>: a freshly generated card that
    /// is not in any pile is added to the play pile (running <c>AfterCardEnteredCombat</c>) before the
    /// resource step.
    /// </summary>
    public static bool AutoPlay(ref FlashExec x, in FlashCard card, byte target = FlashLimits.NoOwner,
        bool skipXCapture = false)
    {
        if (IsOverOrEnding(x.B) || x.B.Player.IsDead)
            return false;
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        if (card.HasKeyword(FlashKeywords.Unplayable)
            || !ShouldPlay(x.B, x.R, card)
            || !TryResolveAutoPlayTarget(ref x.B, stats, ref target))
        {
            MoveToResultPileWithoutPlaying(ref x, card, FlashPile.Play, 0, inPile: false);
            return false;
        }

        FlashPiles.Append(ref x.B, FlashPile.Play, card);
        EnteredCombat(ref x, card, FlashPile.Play, x.B.PlayCount - 1);
        int energyValue = EnergyCostWithModifiers(x.B, x.R, card, FlashPile.Play);
        if (!skipXCapture)
            x.XValue = energyValue;

        // OnPlayWrapper's AddToPile moves the card to the end of the play pile again.
        int index = PlayIndexOf(ref x.B, card);
        FlashCard moved = FlashPiles.RemoveAt(ref x.B, FlashPile.Play, index);
        int playPosition = x.B.PlayCount;
        FlashPiles.Append(ref x.B, FlashPile.Play, moved);
        x.Card = moved;
        x.Stats = stats;
        x.Target = target;
        PlayFromPlay(ref x, moved, target, energyValue, playPosition, isAutoPlay: true);
        return !x.NeedsChoice;
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
        int start = x.B.PlayCount;
        int moved = 0;
        Span<FlashCard> cards = stackalloc FlashCard[Math.Min(count, FlashLimits.Cards)];
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
        if ((stored.Flags & FlashCardFlags.ExhaustOnNextPlay) != 0 || stored.HasKeyword(FlashKeywords.Exhaust))
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
    private static bool TryResolveAutoPlayTarget(ref FlashBattle battle, FlashCardStats stats, ref byte target)
    {
        switch (stats.Target)
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

    private static int PlayIndexOf(ref FlashBattle battle, in FlashCard card)
    {
        int count = FlashPiles.Count(battle, FlashPile.Play);
        int start = FlashPiles.Start(battle, FlashPile.Play);
        for (int index = count - 1; index >= 0; index--)
        {
            if (FlashFingerprint.Equal(battle.Cards[start + index], card))
                return index;
        }
        throw new FlashUnsupportedException("自动打出的卡不在打出区。");
    }
}
