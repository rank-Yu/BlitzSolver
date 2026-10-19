namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Shared card effect building blocks, mirroring <c>GeneralCardMirrors</c> and the common
/// compensation calls. Variable index i reads <c>Stats.Vars[i]</c> plus the card's delta slot i.
/// </summary>
public static class FlashCardEffects
{
    /// <summary>Number of cards in every combat pile whose canonical tags include <paramref name="tag"/>.</summary>
    public static int CountCardsWithTag(in FlashExec x, FlashCardTag tag)
    {
        int count = 0;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref readonly FlashCard card = ref x.B.Cards[position];
            if ((x.R.Card(card.Id, card.Upgrade).Tags & tag) != 0)
                count++;
        }
        return count;
    }

    /// <summary>Current value of the card's i-th declared dynamic variable.</summary>
    public static decimal Var(in FlashExec x, int index)
    {
        FlashCardStats stats = x.Stats ?? throw new InvalidOperationException("当前没有正在打出的卡。");
        decimal delta = index switch
        {
            0 => x.Card.Var0,
            1 => x.Card.Var1,
            _ => 0,
        };
        if ((uint)index >= (uint)stats.Vars.Length)
            throw new FlashUnsupportedException(
                $"卡牌 {FlashRegistry.Card(x.Card.Id).NativeId} 定义读取第 {index} 个动态变量，但规则表只有 {stats.Vars.Length} 个。");
        return stats.Vars[index] + delta;
    }

    /// <summary>Mirrors <c>GeneralCardMirrors.GeneralAttackOnPlay</c> for a plain damage variable and one hit.</summary>
    public static void GeneralAttack(ref FlashExec x, int damageVar, int hitCount = 1)
        => Attack(ref x, Var(x, damageVar), hitCount);

    /// <summary>
    /// The attack half of the general recipe with an already-calculated damage value (BodySlam,
    /// PerfectedStrike and the other <c>CalculatedDamageVar</c> cards).
    /// </summary>
    public static void Attack(ref FlashExec x, decimal damage, int hitCount = 1)
    {
        FlashCardStats stats = x.Stats ?? throw new InvalidOperationException("当前没有正在打出的卡。");
        FlashTargetType targetType = FlashRegistry.Card(x.Card.Id).TargetType(x.B, x.R, x.Card);
        FlashAttackTargeting targeting = targetType switch
        {
            FlashTargetType.AnyEnemy => FlashAttackTargeting.Single,
            FlashTargetType.AllEnemies => FlashAttackTargeting.AllOpponents,
            FlashTargetType.RandomEnemy => FlashAttackTargeting.Random,
            _ => throw new FlashUnsupportedException($"攻击目标类型 {targetType} 尚未实现。"),
        };
        FlashKernel.CardAttack(ref x, damage, hitCount, targeting, x.Target);
    }

    /// <summary>
    /// Self damage from the compensation path: <c>simulator.Damage(owner, HpLoss, Unblockable |
    /// Unpowered | Move, owner)</c> with no card source (BloodWall, Breakthrough, Hemokinesis).
    /// </summary>
    public static void LoseHp(ref FlashExec x, decimal amount)
    {
        Span<byte> targets = stackalloc byte[1];
        targets[0] = FlashLimits.PlayerOwner;
        FlashKernel.Damage(ref x, targets, amount,
            FlashValueProps.Unblockable | FlashValueProps.Unpowered | FlashValueProps.Move,
            FlashLimits.PlayerOwner, hasCardSource: false, default);
    }

    /// <summary>
    /// Self damage from a card that keeps the card as its damage source, matching
    /// <c>Simulator.Damage(..., playedCard, cardPlay)</c> (Offering, Bloodletting).
    /// </summary>
    public static void LoseHpFromCard(ref FlashExec x, decimal amount)
    {
        Span<byte> targets = stackalloc byte[1];
        targets[0] = FlashLimits.PlayerOwner;
        FlashKernel.Damage(ref x, targets, amount,
            FlashValueProps.Unblockable | FlashValueProps.Unpowered | FlashValueProps.Move,
            FlashLimits.PlayerOwner, hasCardSource: true, x.Card);
    }

    /// <summary>
    /// Damage from the card to its owner with the card as source, matching
    /// <c>CreatureCmd.Damage(context, card.Owner.Creature, amount, props, card, null)</c> (the
    /// turn-end-in-hand cards: <c>CardOnTurnEndInHandMirrors.DamageOwner</c>).
    /// </summary>
    public static void DamageOwnerFromCard(ref FlashExec x, decimal amount, FlashValueProps props)
    {
        Span<byte> targets = stackalloc byte[1];
        targets[0] = FlashLimits.PlayerOwner;
        FlashKernel.Damage(ref x, targets, amount, props, FlashLimits.PlayerOwner, hasCardSource: true, x.Card);
    }

    /// <summary>Mirrors <c>GeneralCardMirrors.GeneralBlockOnPlay</c>.</summary>
    public static void GeneralBlock(ref FlashExec x, int blockVar)
        => FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, Var(x, blockVar), FlashValueProps.Move, fromCard: true);

    /// <summary>
    /// The common compensation <c>combat.Apply&lt;T&gt;(target, card.DynamicVars.X.IntValue, owner)</c>
    /// guarded by <c>when target != null</c>.
    /// </summary>
    public static void ApplyToTarget(ref FlashExec x, ushort powerId, int amountVar)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, powerId, (int)Var(x, amountVar), FlashLimits.PlayerOwner);
    }

    /// <summary>
    /// The common compensation <c>combat.Apply&lt;T&gt;(owner, card.DynamicVars.X.IntValue, owner)</c>.
    /// </summary>
    public static void ApplyToOwner(ref FlashExec x, ushort powerId, int amountVar)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, powerId, (int)Var(x, amountVar),
            FlashLimits.PlayerOwner);

    /// <summary>
    /// The common compensation <c>combat.Apply&lt;T&gt;(enemy, ...)</c> over
    /// <c>SimulatedCombatState.HittableEnemies</c> in roster order.
    /// </summary>
    public static void ApplyToAllEnemies(ref FlashExec x, ushort powerId, int amountVar)
    {
        int amount = (int)Var(x, amountVar);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyPower(ref x, owner, powerId, amount, FlashLimits.PlayerOwner);
            // CorePowerSupport.ApplyAll resolves after each target's application.
            FlashKernel.ResolvePowerAmountChanges(ref x);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// Adds <paramref name="delta"/> to both the current play's card copy and the card in the play
    /// pile, mirroring a native <c>DynamicVars[...].BaseValue +=</c> on the played card (Rampage).
    /// Only the two native short-sized variable slots are writable.
    /// </summary>
    public static void AddCurrentCardVar(ref FlashExec x, int index, decimal delta)
    {
        if (delta == 0m)
            return;
        // FlashCard variables are integers. The existing engine keeps decimal dynamic variables (Thrash adds a
        // ModifyDamage result, which multipliers can make fractional); such a state is outside Flash.
        if (delta != decimal.Truncate(delta))
            throw new FlashUnsupportedException($"卡牌变量偏移 {delta} 不是整数，Flash 卡牌变量无法表示。");
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("当前没有正在打出的卡。");
        ref FlashCard played = ref FlashPiles.At(ref x.B, FlashPile.Play, position);
        if (played.Id != x.Card.Id || played.Upgrade != x.Card.Upgrade)
            throw new InvalidOperationException("打出堆顶不是当前卡。");
        short value = checked((short)(index switch
        {
            0 => played.Var0,
            1 => played.Var1,
            _ => throw new ArgumentOutOfRangeException(nameof(index), index, "只有前两个卡牌变量可写。"),
        } + (short)delta));
        if (index == 0)
        {
            played.Var0 = value;
            x.Card.Var0 = value;
        }
        else
        {
            played.Var1 = value;
            x.Card.Var1 = value;
        }
    }

    /// <summary>
    /// Upgrades the card currently being played (RazorTooth's <c>CardCmd.Upgrade(cardPlay.Card)</c>).
    /// The played card is the top of the play pile while its hooks run; only its identity is required
    /// because an earlier replay may already have upgraded the pile entry.
    /// </summary>
    public static void UpgradeCurrentCard(ref FlashExec x)
    {
        // CombatPredictionSimulator.Upgrade is a no-op while the combat is ending; RazorTooth uses it
        // for the played card and a card that lands the killing blow must stay unupgraded in the play
        // pile at the resulting stable boundary.
        if (FlashKernel.IsEnding(x.B))
            return;
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("当前没有正在打出的卡。");
        ref FlashCard played = ref FlashPiles.At(ref x.B, FlashPile.Play, position);
        if (played.Id != x.Card.Id)
            throw new InvalidOperationException("打出堆顶不是当前卡。");
        FlashKernel.UpgradeCard(x.R, ref played);
    }

    /// <summary>
    /// Damage from a relic to every hittable enemy in roster order, with no card source
    /// (<c>simulator.Damage(state.HittableEnemies, ..., ValueProp.Unpowered, relic.Owner.Creature)</c>).
    /// </summary>
    public static void DamageAllEnemies(ref FlashExec x, decimal amount, FlashValueProps props, byte dealer)
    {
        Span<byte> targets = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (FlashKernel.IsHittable(x.B, owner))
                targets[count++] = owner;
        }
        if (count == 0)
            return;
        FlashKernel.Damage(ref x, targets[..count], amount, props, dealer, hasCardSource: false, default);
    }

    /// <summary>
    /// Samples <paramref name="count"/> distinct cards of one canonical type from the root-frozen
    /// <see cref="FlashCardPools.CharacterAll"/> pool, mirroring
    /// <c>CombatCardGenerationExtensions.GetDistinctUnlockedCharacterCardsForCombat</c>'s predicate
    /// path: the caller's type filter is applied to the combat/player-count-filtered candidates in
    /// pool order, then <c>TakeRandom</c> runs a full Fisher-Yates over the filtered list before
    /// taking the first cards (the distinct path of <see cref="FlashKernel.SampleCardPool"/>, with the
    /// filter applied first). Cards are created at upgrade 0 by <see cref="FlashKernel.CreateCard"/>.
    /// </summary>
    public static int SampleCharacterCardsOfType(ref FlashBattle battle, FlashRules rules, FlashCardType type,
        int count, Span<FlashCard> selected, string poolKey = FlashCardPools.CharacterAll)
    {
        FlashCardPool pool = rules.CardPool(poolKey);
        if (pool.Cards.Length == 0)
            return 0;
        if (count > 0 && selected.Length < count)
            throw new FlashCapacityException("GeneratedCards", selected.Length);
        Span<ushort> order = stackalloc ushort[pool.Cards.Length];
        int eligible = 0;
        for (int index = 0; index < pool.Cards.Length; index++)
        {
            ushort id = pool.Cards[index];
            if (rules.Card(id, 0).Type == type)
                order[eligible++] = id;
        }
        if (eligible == 0)
            return 0;
        order = order[..eligible];
        ref FlashRngState rng = ref battle.RngStream(FlashRngStream.CombatCardGeneration);
        for (int n = order.Length; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (order[k], order[n]) = (order[n], order[k]);
        }
        int take = Math.Min(count, order.Length);
        for (int index = 0; index < take; index++)
            selected[index] = FlashKernel.CreateCard(rules, order[index]);
        return take;
    }

    /// <summary>Number of cards in every non-exhaust pile whose canonical type is Status (FlakCannon).</summary>
    public static int CountStatusCards(in FlashBattle battle, FlashRules rules)
    {
        int count = 0;
        for (int position = 0; position < battle.CardCount; position++)
        {
            ref readonly FlashCard card = ref battle.Cards[position];
            if (rules.Card(card.Id, card.Upgrade).Type == FlashCardType.Status)
                count++;
        }
        // The exhaust pile is included in CardCount but excluded from the native AllCards filter.
        for (int position = 0; position < battle.ExhaustCount; position++)
        {
            int absolute = FlashPiles.Start(battle, FlashPile.Exhaust) + position;
            if (rules.Card(battle.Cards[absolute].Id, battle.Cards[absolute].Upgrade).Type == FlashCardType.Status)
                count--;
        }
        return count;
    }

    /// <summary>
    /// Exhausts every Status card outside the exhaust pile in <c>AllCards</c> order
    /// (Hand, Draw, Discard, Play), like FlakCannon's <c>GetStatuses</c> + <c>CardCmd.Exhaust</c>.
    /// The values are snapshotted and relocated by (value, occurrence) because exhaust hooks may move
    /// cards.
    /// </summary>
    public static void ExhaustAllStatusCards(ref FlashExec x)
    {
        Span<FlashCard> cards = stackalloc FlashCard[FlashLimits.Cards];
        Span<byte> sourcePile = stackalloc byte[FlashLimits.Cards];
        int count = 0;
        ReadOnlySpan<FlashPile> piles = [FlashPile.Hand, FlashPile.Draw, FlashPile.Discard, FlashPile.Play];
        for (int pileIndex = 0; pileIndex < piles.Length; pileIndex++)
        {
            FlashPile pile = piles[pileIndex];
            int start = FlashPiles.Start(x.B, pile);
            int pileCount = FlashPiles.Count(x.B, pile);
            for (int offset = 0; offset < pileCount; offset++)
            {
                FlashCard card = x.B.Cards[start + offset];
                if (x.R.Card(card.Id, card.Upgrade).Type != FlashCardType.Status)
                    continue;
                cards[count] = card;
                sourcePile[count] = checked((byte)pileIndex);
                count++;
            }
        }
        for (int item = 0; item < count; item++)
        {
            // Equal status cards are interchangeable, so locating the first equal card in the recorded
            // pile preserves the value sequence even when an earlier equal card was removed first.
            bool found = false;
            FlashPile pile = piles[sourcePile[item]];
            if (TryExhaustFirstEqual(ref x, pile, cards[item]))
            {
                found = true;
            }
            else
            {
                foreach (FlashPile fallback in piles)
                {
                    if (!TryExhaustFirstEqual(ref x, fallback, cards[item]))
                        continue;
                    found = true;
                    break;
                }
            }
            if (!found)
                throw new FlashUnsupportedException("主动消耗前一状态牌离开了战斗牌堆。");
            if (x.NeedsChoice)
                return;
        }
    }

    private static bool TryExhaustFirstEqual(ref FlashExec x, FlashPile pile, in FlashCard card)
    {
        int start = FlashPiles.Start(x.B, pile);
        int count = FlashPiles.Count(x.B, pile);
        for (int offset = 0; offset < count; offset++)
        {
            if (!Equal(x.B.Cards[start + offset], card))
                continue;
            FlashKernel.Exhaust(ref x, pile, offset);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Discards cards that were just drawn, by value and per-pile occurrence, mirroring
    /// <c>Simulator.Discard(IReadOnlyList&lt;PredictedCard&gt;)</c> (the card is located wherever it
    /// currently is; a draw hook may have moved it).
    /// </summary>
    public static void DiscardCards(ref FlashExec x, scoped ReadOnlySpan<FlashCard> cards)
    {
        if (FlashKernel.IsOverOrEnding(x.B))
            return;
        ReadOnlySpan<FlashPile> piles = [FlashPile.Hand, FlashPile.Draw, FlashPile.Discard, FlashPile.Exhaust, FlashPile.Play];
        foreach (FlashCard card in cards)
        {
            foreach (FlashPile pile in piles)
            {
                int start = FlashPiles.Start(x.B, pile);
                int pileCount = FlashPiles.Count(x.B, pile);
                int found = -1;
                for (int offset = 0; offset < pileCount; offset++)
                {
                    if (!Equal(x.B.Cards[start + offset], card))
                        continue;
                    found = offset;
                    break;
                }
                if (found < 0)
                    continue;
                FlashKernel.Discard(ref x, pile, found);
                if (x.NeedsChoice)
                    return;
                break;
            }
        }
    }

    /// <summary>Moves the whole hand to the bottom of the draw pile in hand order (Reboot).</summary>
    public static void MoveHandToDraw(ref FlashExec x)
    {
        int handCount = x.B.HandCount;
        Span<FlashCard> hand = stackalloc FlashCard[handCount];
        FlashPiles.ReadCards(x.B, FlashPile.Hand).CopyTo(hand);
        foreach (FlashCard card in hand)
        {
            int start = FlashPiles.Start(x.B, FlashPile.Hand);
            int index = -1;
            for (int offset = 0; offset < x.B.HandCount; offset++)
            {
                if (Equal(x.B.Cards[start + offset], card))
                {
                    index = offset;
                    break;
                }
            }
            if (index < 0)
                throw new FlashUnsupportedException("移动手牌时目标牌已不在手牌。");
            if (!FlashKernel.MoveCard(ref x.B, FlashPile.Hand, index, FlashPile.Draw))
                return;
        }
    }

    internal static bool Equal(in FlashCard left, in FlashCard right) => FlashFingerprint.Equal(left, right);

    /// <summary>
    /// One hit on a random hittable enemy, mirroring <c>Rng.CombatTargets.NextItem(HittableEnemies)</c>:
    /// the CombatTargets stream is consumed only when the candidate list is non-empty.
    /// </summary>
    public static void DamageRandomEnemy(ref FlashExec x, decimal amount, FlashValueProps props, byte dealer)
    {
        Span<byte> targets = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (FlashKernel.IsHittable(x.B, owner))
                targets[count++] = owner;
        }
        if (count == 0)
            return;
        Span<byte> target = stackalloc byte[1];
        target[0] = targets[x.B.RngStream(FlashRngStream.CombatTargets).NextInt(0, count)];
        FlashKernel.Damage(ref x, target, amount, props, dealer, hasCardSource: false, default);
    }
}
