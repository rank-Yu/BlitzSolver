namespace BlitzSolver.Engine;

/// <summary>
/// Shared card effect building blocks, mirroring <c>GeneralCardMirrors</c> and the common
/// compensation calls. Variable index i reads <c>Stats.Vars[i]</c> plus the card's delta slot i.
/// </summary>
public static class BlitzCardEffects
{
    /// <summary>Number of cards in every combat pile whose canonical tags include <paramref name="tag"/>.</summary>
    public static int CountCardsWithTag(in BlitzExec x, BlitzCardTag tag)
    {
        int count = 0;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[position];
            if ((x.R.Card(card.Id, card.Upgrade).Tags & tag) != 0)
                count++;
        }
        return count;
    }

    /// <summary>Current value of the card's i-th declared dynamic variable.</summary>
    public static decimal Var(in BlitzExec x, int index)
    {
        BlitzCardStats stats = x.Stats ?? throw new InvalidOperationException("当前没有正在打出的卡。");
        if ((uint)index >= (uint)stats.Vars.Length)
            throw new BlitzUnsupportedException(
                $"卡牌 {BlitzRegistry.Card(x.Card.Id).NativeId} 定义读取第 {index} 个动态变量，但规则表只有 {stats.Vars.Length} 个。");
        return stats.Vars[index] + BlitzDecimalVars.Delta(in x.B, in x.Card, index);
    }

    /// <summary>Mirrors <c>GeneralCardMirrors.GeneralAttackOnPlay</c> for a plain damage variable and one hit.</summary>
    public static void GeneralAttack(ref BlitzExec x, int damageVar, int hitCount = 1)
        => Attack(ref x, Var(x, damageVar), hitCount);

    /// <summary>
    /// The attack half of the general recipe with an already-calculated damage value (BodySlam,
    /// PerfectedStrike and the other <c>CalculatedDamageVar</c> cards).
    /// </summary>
    public static void Attack(ref BlitzExec x, decimal damage, int hitCount = 1)
    {
        BlitzCardStats stats = x.Stats ?? throw new InvalidOperationException("当前没有正在打出的卡。");
        BlitzTargetType targetType = BlitzRegistry.Card(x.Card.Id).TargetType(x.B, x.R, x.Card);
        BlitzAttackTargeting targeting = targetType switch
        {
            BlitzTargetType.AnyEnemy => BlitzAttackTargeting.Single,
            BlitzTargetType.AllEnemies => BlitzAttackTargeting.AllOpponents,
            BlitzTargetType.RandomEnemy => BlitzAttackTargeting.Random,
            _ => throw new BlitzUnsupportedException($"攻击目标类型 {targetType} 尚未实现。"),
        };
        BlitzKernel.CardAttack(ref x, damage, hitCount, targeting, x.Target);
    }

    /// <summary>
    /// Self damage from the compensation path: <c>simulator.Damage(owner, HpLoss, Unblockable |
    /// Unpowered | Move, owner)</c> with no card source (BloodWall, Breakthrough, Hemokinesis).
    /// </summary>
    public static void LoseHp(ref BlitzExec x, decimal amount)
    {
        Span<byte> targets = stackalloc byte[1];
        targets[0] = BlitzLimits.PlayerOwner;
        BlitzKernel.Damage(ref x, targets, amount,
            BlitzValueProps.Unblockable | BlitzValueProps.Unpowered | BlitzValueProps.Move,
            BlitzLimits.PlayerOwner, hasCardSource: false, default);
    }

    /// <summary>
    /// Self damage from a card that keeps the card as its damage source, matching
    /// <c>Simulator.Damage(..., playedCard, cardPlay)</c> (Offering, Bloodletting).
    /// </summary>
    public static void LoseHpFromCard(ref BlitzExec x, decimal amount)
    {
        Span<byte> targets = stackalloc byte[1];
        targets[0] = BlitzLimits.PlayerOwner;
        BlitzKernel.Damage(ref x, targets, amount,
            BlitzValueProps.Unblockable | BlitzValueProps.Unpowered | BlitzValueProps.Move,
            BlitzLimits.PlayerOwner, hasCardSource: true, x.Card);
    }

    /// <summary>
    /// Damage from the card to its owner with the card as source, matching
    /// <c>CreatureCmd.Damage(context, card.Owner.Creature, amount, props, card, null)</c> (the
    /// turn-end-in-hand cards: <c>CardOnTurnEndInHandMirrors.DamageOwner</c>).
    /// </summary>
    public static void DamageOwnerFromCard(ref BlitzExec x, decimal amount, BlitzValueProps props)
    {
        Span<byte> targets = stackalloc byte[1];
        targets[0] = BlitzLimits.PlayerOwner;
        BlitzKernel.Damage(ref x, targets, amount, props, BlitzLimits.PlayerOwner, hasCardSource: true, x.Card);
    }

    /// <summary>Mirrors <c>GeneralCardMirrors.GeneralBlockOnPlay</c>.</summary>
    public static void GeneralBlock(ref BlitzExec x, int blockVar)
        => BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, Var(x, blockVar), BlitzValueProps.Move, fromCard: true);

    /// <summary>
    /// The common compensation <c>combat.Apply&lt;T&gt;(target, card.DynamicVars.X.IntValue, owner)</c>
    /// guarded by <c>when target != null</c>.
    /// </summary>
    public static void ApplyToTarget(ref BlitzExec x, ushort powerId, int amountVar)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, powerId, (int)Var(x, amountVar), BlitzLimits.PlayerOwner);
    }

    /// <summary>
    /// The common compensation <c>combat.Apply&lt;T&gt;(owner, card.DynamicVars.X.IntValue, owner)</c>.
    /// </summary>
    public static void ApplyToOwner(ref BlitzExec x, ushort powerId, int amountVar)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, powerId, (int)Var(x, amountVar),
            BlitzLimits.PlayerOwner);

    /// <summary>
    /// The common compensation <c>combat.Apply&lt;T&gt;(enemy, ...)</c> over
    /// <c>SimulatedCombatState.HittableEnemies</c> in roster order.
    /// </summary>
    public static void ApplyToAllEnemies(ref BlitzExec x, ushort powerId, int amountVar)
    {
        int amount = (int)Var(x, amountVar);
        // SimulatedCombatState.HittableEnemies returns a snapshot: an application that kills its target
        // (Sleight of Flesh, Doom kill) compacts the roster, and the native foreach still visits every
        // enemy that was hittable when the effect started. Iterating the live roster count would skip
        // the entries that shifted left.
        Span<byte> owners = stackalloc byte[BlitzLimits.Enemies];
        int count = x.B.RosterCount;
        for (int position = 0; position < count; position++)
            owners[position] = BlitzKernel.EnemyOwner(x.B.Roster[position]);
        for (int position = 0; position < count; position++)
        {
            byte owner = owners[position];
            if (!BlitzKernel.IsHittable(x.B, owner))
                continue;
            BlitzKernel.ApplyPower(ref x, owner, powerId, amount, BlitzLimits.PlayerOwner);
            // CorePowerSupport.ApplyAll resolves after each target's application.
            BlitzKernel.ResolvePowerAmountChanges(ref x);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// Adds <paramref name="delta"/> to both the current play's card copy and the card in the play
    /// pile, mirroring a native <c>DynamicVars[...].BaseValue +=</c> on the played card (Rampage).
    /// Only the two native short-sized variable slots are writable.
    /// </summary>
    public static void AddCurrentCardVar(ref BlitzExec x, int index, decimal delta)
    {
        if (delta == 0m)
            return;
        // The current card sits at BlitzExec.PlayPosition, not at the top of the play pile: a nested
        // auto-play (OwnChoice discarding a Sly card, an in-effect auto-play) appends its card after
        // the outer one and leaves before the outer tail resumes.
        int position = x.PlayPosition;
        if ((uint)position >= (uint)x.B.PlayCount)
            throw new InvalidOperationException("当前没有正在打出的卡。");
        ref BlitzCard played = ref BlitzPiles.At(ref x.B, BlitzPile.Play, position);
        if (played.Id != x.Card.Id)
            throw new InvalidOperationException("当前打出的卡与打出堆位置不一致。");
        // The live play-pile copy is the native mutable CardModel; a mid-play upgrade (Dampen's last
        // caster dying during this card's attack) changes it before the tail reads its variables.
        if (played.Upgrade != x.Card.Upgrade)
            BlitzKernel.SyncCurrentPlayCard(ref x);
        if (index == 0 && BlitzDecimalVars.IsTracked(in played))
        {
            decimal current = BlitzDecimalVars.TryGet(in x.B, in played, out decimal existing) ? existing : 0m;
            decimal decimalValue = current + delta;
            // DynamicVar.BaseValue's setter clamps at 999,999,999m while Thrash.ExtraDamage is not
            // clamped; a clamped offset could no longer reproduce AfterDowngraded. Keep the clamp as
            // an explicit boundary instead of storing a value the native field would have truncated.
            BlitzCardStats decimalStats = x.R.Card(played.Id, played.Upgrade);
            if (decimalStats.Vars.Length > 0 && decimalStats.Vars[0] + decimalValue > BlitzLimits.ValueClamp)
                throw new BlitzUnsupportedException("Thrash 的小数伤害变量超过 DynamicVar.BaseValue 的 999999999 钳制上限。");
            BlitzDecimalVars.Assign(ref x.B, ref played, decimalValue);
            x.Card.Private = played.Private;
            return;
        }
        // BlitzCard's integer variable slots are the representation for every other card. A
        // fractional delta on a card that did not opt into the decimal table is a kernel boundary.
        if (delta != decimal.Truncate(delta))
            throw new BlitzUnsupportedException($"卡牌变量偏移 {delta} 不是整数，Blitz 卡牌变量无法表示。");
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
    public static void UpgradeCurrentCard(ref BlitzExec x)
    {
        // CombatPredictionSimulator.Upgrade is a no-op while the combat is ending; RazorTooth uses it
        // for the played card and a card that lands the killing blow must stay unupgraded in the play
        // pile at the resulting stable boundary.
        if (BlitzKernel.IsEnding(x.B))
            return;
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("当前没有正在打出的卡。");
        ref BlitzCard played = ref BlitzPiles.At(ref x.B, BlitzPile.Play, position);
        if (played.Id != x.Card.Id)
            throw new InvalidOperationException("打出堆顶不是当前卡。");
        BlitzKernel.UpgradeCard(x.R, ref played);
    }

    /// <summary>
    /// Damage from a relic to every hittable enemy in roster order, with no card source
    /// (<c>simulator.Damage(state.HittableEnemies, ..., ValueProp.Unpowered, relic.Owner.Creature)</c>).
    /// </summary>
    public static void DamageAllEnemies(ref BlitzExec x, decimal amount, BlitzValueProps props, byte dealer)
    {
        Span<byte> targets = stackalloc byte[BlitzLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (BlitzKernel.IsHittable(x.B, owner))
                targets[count++] = owner;
        }
        if (count == 0)
            return;
        BlitzKernel.Damage(ref x, targets[..count], amount, props, dealer, hasCardSource: false, default);
    }

    /// <summary>
    /// Samples <paramref name="count"/> distinct cards of one canonical type from the root-frozen
    /// <see cref="BlitzCardPools.CharacterAll"/> pool, mirroring
    /// <c>CombatCardGenerationExtensions.GetDistinctUnlockedCharacterCardsForCombat</c>'s predicate
    /// path: the caller's type filter is applied to the combat/player-count-filtered candidates in
    /// pool order, then <c>TakeRandom</c> runs a full Fisher-Yates over the filtered list before
    /// taking the first cards (the distinct path of <see cref="BlitzKernel.SampleCardPool"/>, with the
    /// filter applied first). <paramref name="type"/> <see cref="BlitzCardType.None"/> matches every
    /// type; a non-None <paramref name="rarity"/> adds the native rarity predicate. Cards are created
    /// at upgrade 0 by <see cref="BlitzKernel.CreateCard"/>.
    /// </summary>
    public static int SampleCharacterCardsOfType(ref BlitzBattle battle, BlitzRules rules, BlitzCardType type,
        int count, Span<BlitzCard> selected, string poolKey = BlitzCardPools.CharacterAll,
        BlitzCardRarity rarity = BlitzCardRarity.None)
    {
        BlitzCardPool pool = rules.CardPool(poolKey);
        if (pool.Cards.Length == 0)
            return 0;
        if (count > 0 && selected.Length < count)
            throw new BlitzCapacityException("GeneratedCards", selected.Length);
        Span<ushort> order = stackalloc ushort[pool.Cards.Length];
        int eligible = 0;
        for (int index = 0; index < pool.Cards.Length; index++)
        {
            ushort id = pool.Cards[index];
            BlitzCardStats stats = rules.Card(id, 0);
            if (type != BlitzCardType.None && stats.Type != type)
                continue;
            if (rarity != BlitzCardRarity.None && stats.Rarity != rarity)
                continue;
            order[eligible++] = id;
        }
        if (eligible == 0)
            return 0;
        order = order[..eligible];
        ref BlitzRngState rng = ref battle.RngStream(BlitzRngStream.CombatCardGeneration);
        for (int n = order.Length; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (order[k], order[n]) = (order[n], order[k]);
        }
        int take = Math.Min(count, order.Length);
        for (int index = 0; index < take; index++)
            selected[index] = BlitzKernel.CreateCard(rules, order[index]);
        return take;
    }

    /// <summary>Number of cards in every non-exhaust pile whose canonical type is Status (FlakCannon).</summary>
    public static int CountStatusCards(in BlitzBattle battle, BlitzRules rules)
    {
        int count = 0;
        for (int position = 0; position < battle.CardCount; position++)
        {
            ref readonly BlitzCard card = ref battle.Cards[position];
            if (BlitzKernel.CardType(battle, rules, card) == BlitzCardType.Status)
                count++;
        }
        // The exhaust pile is included in CardCount but excluded from the native AllCards filter.
        for (int position = 0; position < battle.ExhaustCount; position++)
        {
            int absolute = BlitzPiles.Start(battle, BlitzPile.Exhaust) + position;
            if (BlitzKernel.CardType(battle, rules, battle.Cards[absolute]) == BlitzCardType.Status)
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
    public static void ExhaustAllStatusCards(ref BlitzExec x)
    {
        Span<BlitzCard> cards = stackalloc BlitzCard[BlitzLimits.Cards];
        Span<byte> sourcePile = stackalloc byte[BlitzLimits.Cards];
        int count = 0;
        ReadOnlySpan<BlitzPile> piles = [BlitzPile.Hand, BlitzPile.Draw, BlitzPile.Discard, BlitzPile.Play];
        for (int pileIndex = 0; pileIndex < piles.Length; pileIndex++)
        {
            BlitzPile pile = piles[pileIndex];
            int start = BlitzPiles.Start(x.B, pile);
            int pileCount = BlitzPiles.Count(x.B, pile);
            for (int offset = 0; offset < pileCount; offset++)
            {
                BlitzCard card = x.B.Cards[start + offset];
                if (BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Status)
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
            BlitzPile pile = piles[sourcePile[item]];
            if (TryExhaustFirstEqual(ref x, pile, cards[item]))
            {
                found = true;
            }
            else
            {
                foreach (BlitzPile fallback in piles)
                {
                    if (!TryExhaustFirstEqual(ref x, fallback, cards[item]))
                        continue;
                    found = true;
                    break;
                }
            }
            if (!found)
                throw new BlitzUnsupportedException("主动消耗前一状态牌离开了战斗牌堆。");
            if (x.NeedsChoice)
                return;
        }
    }

    private static bool TryExhaustFirstEqual(ref BlitzExec x, BlitzPile pile, in BlitzCard card)
    {
        int start = BlitzPiles.Start(x.B, pile);
        int count = BlitzPiles.Count(x.B, pile);
        for (int offset = 0; offset < count; offset++)
        {
            if (!Equal(x.B.Cards[start + offset], card))
                continue;
            BlitzKernel.Exhaust(ref x, pile, offset);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Discards cards that were just drawn, by value and per-pile occurrence, mirroring
    /// <c>Simulator.Discard(IReadOnlyList&lt;PredictedCard&gt;)</c> (the card is located wherever it
    /// currently is; a draw hook may have moved it).
    /// </summary>
    public static void DiscardCards(ref BlitzExec x, scoped ReadOnlySpan<BlitzCard> cards)
    {
        if (BlitzKernel.IsOverOrEnding(x.B))
            return;
        ReadOnlySpan<BlitzPile> piles = [BlitzPile.Hand, BlitzPile.Draw, BlitzPile.Discard, BlitzPile.Exhaust, BlitzPile.Play];
        foreach (BlitzCard card in cards)
        {
            foreach (BlitzPile pile in piles)
            {
                int start = BlitzPiles.Start(x.B, pile);
                int pileCount = BlitzPiles.Count(x.B, pile);
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
                BlitzKernel.Discard(ref x, pile, found);
                if (x.NeedsChoice)
                    return;
                break;
            }
        }
    }

    /// <summary>
    /// Discards cards just drawn by hand position (Scrape). Positions are ascending in draw order;
    /// each prior discard shifts the remaining positions down by one. A hook that moved a selected
    /// card out of its recorded slot makes the assumption invalid and is rejected explicitly rather
    /// than discarding a different byte-equal copy.
    /// </summary>
    public static void DiscardDrawn(ref BlitzExec x, scoped ReadOnlySpan<BlitzCard> drawn,
        scoped ReadOnlySpan<int> positions, scoped ReadOnlySpan<int> selected)
    {
        int removed = 0;
        foreach (int item in selected)
        {
            int position = positions[item];
            int index = position - removed;
            if (position < 0 || (uint)index >= (uint)x.B.HandCount
                || !Equal(x.B.Cards[index], drawn[item]))
            {
                throw new BlitzUnsupportedException("抓牌后被抽牌钩子移动的牌无法按原位置弃置。");
            }
            BlitzKernel.Discard(ref x, BlitzPile.Hand, index);
            removed++;
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>Moves the whole hand to the bottom of the draw pile in hand order (Reboot).</summary>
    public static void MoveHandToDraw(ref BlitzExec x)
    {
        int handCount = x.B.HandCount;
        Span<BlitzCard> hand = stackalloc BlitzCard[handCount];
        BlitzPiles.ReadCards(x.B, BlitzPile.Hand).CopyTo(hand);
        foreach (BlitzCard card in hand)
        {
            int start = BlitzPiles.Start(x.B, BlitzPile.Hand);
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
                throw new BlitzUnsupportedException("移动手牌时目标牌已不在手牌。");
            if (!BlitzKernel.MoveCard(ref x.B, BlitzPile.Hand, index, BlitzPile.Draw))
                return;
        }
    }

    internal static bool Equal(in BlitzCard left, in BlitzCard right) => BlitzFingerprint.Equal(left, right);

    /// <summary>
    /// One hit on a random hittable enemy, mirroring <c>Rng.CombatTargets.NextItem(HittableEnemies)</c>:
    /// the CombatTargets stream is consumed only when the candidate list is non-empty.
    /// </summary>
    public static void DamageRandomEnemy(ref BlitzExec x, decimal amount, BlitzValueProps props, byte dealer)
    {
        Span<byte> targets = stackalloc byte[BlitzLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (BlitzKernel.IsHittable(x.B, owner))
                targets[count++] = owner;
        }
        if (count == 0)
            return;
        Span<byte> target = stackalloc byte[1];
        target[0] = targets[x.B.RngStream(BlitzRngStream.CombatTargets).NextInt(0, count)];
        BlitzKernel.Damage(ref x, target, amount, props, dealer, hasCardSource: false, default);
    }
}
