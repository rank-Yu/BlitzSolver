namespace BlitzSolver.Engine;

public static partial class BlitzKernel
{
    /// <summary>Bit 0 of <see cref="BlitzCard.Private"/>: this exact Fetch card played this turn.</summary>
    internal const short FetchPlayedBit = 1;

    /// <summary>
    /// A fresh canonical card at <paramref name="upgrade"/>, as the existing engine creates generated
    /// cards (<c>PredictedCard.Create(CanonicalModels.Card&lt;T&gt;())</c>): canonical cost, star cost and
    /// keywords, no deck version, no modifiers.
    /// </summary>
    public static BlitzCard CreateCard(BlitzRules rules, ushort cardId, int upgrade = 0)
    {
        BlitzCardStats stats = rules.Card(cardId, upgrade);
        BlitzCard card = new()
        {
            Id = cardId,
            Upgrade = checked((byte)upgrade),
            Keywords = stats.Keywords | BlitzKeywords.CreatedAfterRoot,
            BaseCost = checked((sbyte)(stats.CostsX ? 0 : stats.EnergyCost)),
            BaseStarCost = checked((sbyte)stats.StarCost),
        };
        if (BlitzRegistry.Card(cardId).UsesIdentityLedger)
            card.Private = BlitzLimits.UninitializedLedger;
        if (stats.CostsX)
            card.Flags |= BlitzCardFlags.CostsX;
        if (stats.StarCostsX)
            card.Flags |= BlitzCardFlags.StarCostsX;
        return card;
    }

    /// <summary>
    /// Samples <paramref name="count"/> cards from a root-frozen generation pool. Distinct selection
    /// mirrors <c>IEnumerableExtensions.TakeRandom</c> (<c>UnstableShuffle</c> over the whole list, then
    /// Take), so it consumes one Shuffle-stream draw per element beyond the first even when fewer cards
    /// are taken. With-replacement selection mirrors <c>Rng.NextItem</c> per card. Cards are created at
    /// upgrade 0, as <c>PredictedCard.Create(canonical, player)</c> does.
    /// </summary>
    public static int SampleCardPool(ref BlitzBattle battle, BlitzRules rules, string poolKey, int count,
        bool distinct, BlitzRngStream stream, Span<BlitzCard> selected)
    {
        BlitzCardPool pool = rules.CardPool(poolKey);
        if (pool.Cards.Length == 0)
            return 0;
        if (count > 0 && selected.Length < count)
            throw new BlitzCapacityException("GeneratedCards", selected.Length);
        ref BlitzRngState rng = ref battle.RngStream(stream);
        if (distinct)
        {
            Span<ushort> order = stackalloc ushort[pool.Cards.Length];
            pool.Cards.AsSpan().CopyTo(order);
            for (int n = order.Length; n > 1;)
            {
                n--;
                int k = rng.NextInt(n + 1);
                (order[k], order[n]) = (order[n], order[k]);
            }
            int take = Math.Min(count, order.Length);
            for (int index = 0; index < take; index++)
                selected[index] = CreateCard(rules, order[index]);
            return take;
        }
        int sampled = Math.Max(0, count);
        for (int index = 0; index < sampled; index++)
            selected[index] = CreateCard(rules, pool.Cards[rng.NextInt(0, pool.Cards.Length)]);
        return sampled;
    }

    /// <summary>Mirrors <c>CardModel.SetToFreeThisTurn</c>: an absolute 0 energy modifier until the end of
    /// the turn or the next play, and a 0 temporary star cost cleared at turn end.</summary>
    public static void MakeFreeThisTurn(BlitzRules rules, ref BlitzCard card)
    {
        BlitzCardStats stats = rules.Card(card.Id, card.Upgrade);
        // CardEnergyCost.SetThisTurnOrUntilPlayed: the modifier is added when cost != 0 or the
        // canonical cost is >= 0, so X-cost cards (canonical 0) still receive it.
        if (stats.EnergyCost >= 0)
        {
            if (card.EnergyModifierCount >= BlitzLimits.CardEnergyCostModifiers)
                throw new BlitzCapacityException("Card.EnergyModifiers", BlitzLimits.CardEnergyCostModifiers);
            card.EnergyModifiers[card.EnergyModifierCount] = BlitzCostModifier.Create(
                0, BlitzCostType.Absolute, BlitzCostExpiration.EndOfTurnOrWhenPlayed, reduceOnly: false);
            card.EnergyModifierCount++;
        }
        card.SetTemporaryStarCost(0, clearsAtTurnEnd: true, clearsWhenPlayed: true);
    }

    /// <summary>
    /// Mirrors <c>CardModel.SetToFreeThisCombat</c>: an absolute 0 energy modifier until the end of
    /// combat and a 0 temporary star cost that clears neither at turn end nor when played. Only used
    /// on freshly generated cards (Metamorphosis), so the single temporary-star-cost slot is free.
    /// </summary>
    public static void MakeFreeThisCombat(BlitzRules rules, ref BlitzCard card)
    {
        BlitzCardStats stats = rules.Card(card.Id, card.Upgrade);
        if (stats.EnergyCost >= 0)
        {
            if (card.EnergyModifierCount >= BlitzLimits.CardEnergyCostModifiers)
                throw new BlitzCapacityException("Card.EnergyModifiers", BlitzLimits.CardEnergyCostModifiers);
            card.EnergyModifiers[card.EnergyModifierCount] = BlitzCostModifier.Create(
                0, BlitzCostType.Absolute, BlitzCostExpiration.EndOfCombat, reduceOnly: false);
            card.EnergyModifierCount++;
        }
        card.SetTemporaryStarCost(0, clearsAtTurnEnd: false, clearsWhenPlayed: false);
    }

    /// <summary>
    /// Stores the block an instanced power grants when its owner's block is cleared
    /// (<c>ToricToughnessPower.SetBlock</c>): the native field is a decimal, but the observed value
    /// is the settled card block gain, an integer. A fractional value cannot be represented in
    /// <see cref="BlitzPower.Aux0"/> and is rejected as an explicit boundary.
    /// </summary>
    public static void SetInstanceBlockValue(ref BlitzBattle battle, int index, decimal block)
    {
        if ((uint)index >= (uint)battle.PowerCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Power 行越界。");
        if (block != decimal.Truncate(block) || block < 0m || block > int.MaxValue)
            throw new BlitzUnsupportedException($"实例 Power 的格挡量 {block} 不是可表示的非负整数。");
        battle.Powers[index].Aux0 = (int)block;
    }

    /// <summary>
    /// Replaces the card at <paramref name="index"/> in <paramref name="pile"/> with
    /// <paramref name="replacement"/> at the same position, mirroring <c>CardCmd.Transform</c> as the
    /// existing engine resolves it (<c>CardChoiceResolution.AddTransformedCard</c>). The original has
    /// no instance identity in Blitz, so the position is the identity.
    /// </summary>
    public static void ReplaceCard(ref BlitzExec x, BlitzPile pile, int index, in BlitzCard replacement)
    {
        // CardChoiceSupport.TransformCardToGeneratedReplacement has no ending gate: Entropy still
        // transforms a hand card when the draw already killed the last enemy (the turn-start power
        // pass walks the power table directly). Only the card- and relic-listener dispatches stop.
        BlitzCard entered = replacement;
        entered.Keywords |= BlitzKeywords.CreatedAfterRoot;
        // A replacement is a new native card: Dampen's per-card original upgrade belongs to the object
        // that was downgraded, not to a generated/transformed value.
        entered.DampenOriginalUpgrade = 0;
        BlitzPiles.RemoveAt(ref x.B, pile, index);
        int insert = Math.Min(index, BlitzPiles.Count(x.B, pile));
        BlitzPiles.Insert(ref x.B, pile, insert, entered);
        x.B.AddCounter(BlitzCounter.CardsGeneratedThisCombat, 1);
        EnteredCombat(ref x, replacement, pile, insert);
        if (!x.NeedsChoice)
            AfterGeneratedForCombat(ref x, target: pile, insert, BlitzLimits.PlayerOwner);
    }

    /// <summary>
    /// Mirrors <c>CardChoiceSupport.TransformCardBatch</c> / <c>CardCmd.Transform</c>: every selected
    /// card is removed first (recording its index in the shrinking pile), then the replacements are
    /// inserted in (pile, index) order, each entering combat and firing
    /// <c>AfterCardGeneratedForCombat</c>. Used by Compact's Status → Fuel transformation.
    /// </summary>
    public static void TransformCards(ref BlitzExec x, BlitzPile pile, scoped ReadOnlySpan<int> selected,
        BlitzCard replacement, int upgrade)
    {
        if (selected.IsEmpty || IsOverOrEnding(x.B))
            return;
        Span<BlitzCard> originals = stackalloc BlitzCard[selected.Length];
        Span<int> occurrences = stackalloc int[selected.Length];
        Span<int> removal = stackalloc int[selected.Length];
        int count = BlitzPiles.Count(x.B, pile);
        ReadOnlySpan<BlitzCard> before = BlitzPiles.ReadCards(x.B, pile);
        for (int item = 0; item < selected.Length; item++)
        {
            if ((uint)selected[item] >= (uint)count)
                throw new BlitzUnsupportedException("变换选中的卡不在原牌堆。");
            originals[item] = before[selected[item]];
            int occurrence = 0;
            for (int prior = 0; prior < selected[item]; prior++)
            {
                if (BlitzFingerprint.Equal(before[prior], originals[item]))
                    occurrence++;
            }
            occurrences[item] = occurrence;
            removal[item] = selected[item];
        }
        // Remove in the selected order; byte-equal cards at other slots are not interchangeable.
        for (int item = 0; item < selected.Length; item++)
        {
            int occurrence = occurrences[item];
            for (int prior = 0; prior < item; prior++)
            {
                if (selected[prior] < selected[item]
                    && BlitzFingerprint.Equal(originals[prior], originals[item]))
                {
                    occurrence--;
                }
            }
            int position = -1;
            ReadOnlySpan<BlitzCard> cards = BlitzPiles.ReadCards(x.B, pile);
            for (int index = 0; index < cards.Length; index++)
            {
                if (!BlitzFingerprint.Equal(cards[index], originals[item]))
                    continue;
                if (occurrence-- == 0)
                {
                    position = index;
                    break;
                }
            }
            if (position < 0)
                throw new BlitzUnsupportedException("变换选中的卡在结算前离开了原牌堆。");
            removal[item] = position;
            BlitzPiles.RemoveAt(ref x.B, pile, position);
        }
        // CardCmd.Transform sorts by pile (all equal here) then index, ascending.
        for (int item = 1; item < removal.Length; item++)
        {
            int index = item;
            while (index > 0 && removal[index - 1] > removal[index])
            {
                (removal[index - 1], removal[index]) = (removal[index], removal[index - 1]);
                index--;
            }
        }
        for (int item = 0; item < removal.Length; item++)
        {
            BlitzCard card = replacement;
            card.Upgrade = checked((byte)upgrade);
            card.DampenOriginalUpgrade = 0;
            card.BaseCost = checked((sbyte)x.R.Card(card.Id, upgrade).EnergyCost);
            card.BaseStarCost = checked((sbyte)x.R.Card(card.Id, upgrade).StarCost);
            card.Keywords = x.R.Card(card.Id, upgrade).Keywords | BlitzKeywords.CreatedAfterRoot;
            int insert = Math.Min(removal[item], BlitzPiles.Count(x.B, pile));
            BlitzPiles.Insert(ref x.B, pile, insert, card);
            x.B.AddCounter(BlitzCounter.CardsGeneratedThisCombat, 1);
            EnteredCombat(ref x, card, pile, insert);
            if (x.NeedsChoice)
                return;
            if (!AfterGeneratedForCombat(ref x, pile, insert, BlitzLimits.PlayerOwner))
                return;
        }
    }

    /// <summary>
    /// <c>SimulatedCombatState.CardEventHistory.AfterCardEnteredCombat</c>: the player's relic listeners
    /// (<c>AfterCardEnteredCombat</c>, GhostSeed) run before the card-specific cost reactions
    /// (Stomp, Pinpoint, Banshee's Cry), then <c>BlitzCardDef.AfterEnteredCombat</c>.
    /// </summary>
    private static void EnteredCombat(ref BlitzExec x, in BlitzCard card, BlitzPile pile, int index)
    {
        AfterCardEnteredCombatHook entered = new() { Pile = pile, Index = index, Card = card };
        BlitzHooks.Dispatch(ref x, ref entered, gatedByEnding: false);
        if (x.NeedsChoice)
            return;
        // CardEventHistory.AfterCardEnteredCombat returns before its card-specific switch for clones.
        if (!card.IsClone)
        {
            BlitzRegistry.Card(card.Id).AfterEnteredCombat(ref x, pile, index);
            if (x.NeedsChoice)
                return;
        }
        // SimulatedCombatState.CardEventHistory.AfterCardEnteredCombat ends with the affliction
        // normalization (the one-time Vital Spark/Smog entry check lives there).
        NormalizeCardAfflictions(ref x);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.AddGeneratedCardsToCombat</c> for one card: insertion as
    /// <c>AddToPile</c> does for a card entering combat (full hand diverts to discard, random position
    /// from the Shuffle stream), then the entered-combat sink and <c>AfterCardGeneratedForCombat</c>.
    /// Returns false only for a combat that is no longer in progress or a pending choice; an add
    /// refused because the combat is ending still records the generation and returns true so the
    /// caller continues exactly like <c>AddGeneratedCardsToCombat</c>.
    /// </summary>
    public static bool AddGeneratedCard(ref BlitzExec x, in BlitzCard card, BlitzPile pile,
        BlitzPilePosition position = BlitzPilePosition.Bottom, byte creator = BlitzLimits.PlayerOwner)
    {
        if (!IsInProgress(x.B))
            return false;
        // CombatPredictionHistory.CardGenerated is recorded before AddToPile; a card refused because the
        // combat is ending still counts as generated (HeirloomHammer clones a card on a killing blow).
        // AddGeneratedCardsToCombat keeps looping and the card's OnPlay keeps running after the refused
        // add, so this reports success without inserting the card: callers must not treat the value as
        // being in a pile, and no generation hook runs for it.
        if (creator == BlitzLimits.PlayerOwner)
            x.B.AddCounter(BlitzCounter.CardsGeneratedThisCombat, 1);
        if (IsEnding(x.B) || x.B.Player.IsDead)
            return true;
        BlitzPile target = pile == BlitzPile.Hand && x.B.HandCount >= x.B.MaxHandSize ? BlitzPile.Discard : pile;
        int count = BlitzPiles.Count(x.B, target);
        int index = position switch
        {
            BlitzPilePosition.Bottom => count,
            BlitzPilePosition.Top => 0,
            BlitzPilePosition.Random => x.B.RngStream(BlitzRngStream.Shuffle).NextInt(count + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
        };
        BlitzCard added = card;
        added.Keywords |= BlitzKeywords.CreatedAfterRoot;
        BlitzPiles.Insert(ref x.B, target, index, added);
        EnteredCombat(ref x, added, target, index);
        if (x.NeedsChoice)
            return false;
        return AfterGeneratedForCombat(ref x, target, index, creator);
    }

    /// <summary>
    /// <c>HookMirrors.AfterCardGeneratedForCombat</c> after a generated card entered combat. The hook
    /// receives the card's absolute position; <paramref name="index"/> is pile-local.
    /// </summary>
    private static bool AfterGeneratedForCombat(ref BlitzExec x, BlitzPile target, int index, byte creator)
    {
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.AfterCardGeneratedForCombat) != 0)
        {
            Span<BlitzListener> listeners = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
            int listenerCount = BlitzHooks.CollectListeners(x.B, BlitzHookMask.AfterCardGeneratedForCombat, listeners);
            for (int listener = 0; listener < listenerCount; listener++)
            {
                BlitzHooks.Definition(x.B, listeners[listener])
                    .AfterCardGeneratedForCombat(ref x, listeners[listener],
                        BlitzPiles.Start(x.B, target) + index, creator);
                if (x.NeedsChoice)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Mirrors <c>CardModel.CreateClone</c>: the clone keeps the card's mutable state but not
    /// ExhaustOnNextPlay, and <c>AfterCloned</c> clears the deck link and the removed-from-state mark.
    /// </summary>
    public static BlitzCard CloneCard(in BlitzCard card)
    {
        BlitzCard clone = card;
        clone.Flags &= ~(BlitzCardFlags.ExhaustOnNextPlay | BlitzCardFlags.DeckCard
            | BlitzCardFlags.RemovedFromState | BlitzCardFlags.PowerAfflictionChecked);
        clone.Keywords |= BlitzKeywords.CreatedAfterRoot;
        clone.IsClone = true;
        // The clone is a new native object; a BeatDown transient mark and Dampen's per-card original
        // upgrade belong to the original.
        clone.TransientMark = 0;
        clone.DampenOriginalUpgrade = 0;
        // PredictedCard/CardModel clones are new objects: the existing engine's per-card dictionaries
        // have no entry for them until their first normalization/play. Other cards keep Private (the
        // native clone copies Claw/Rampage-style private counters).
        BlitzCardDef cloneDef = BlitzRegistry.Card(card.Id);
        if (cloneDef.UsesIdentityLedger)
            clone.Private = BlitzLimits.UninitializedLedger;
        if (cloneDef.TracksFetchPlay)
            clone.Private &= ~FetchPlayedBit;
        return clone;
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.Heal</c>.</summary>
    public static void Heal(ref BlitzExec x, byte owner, decimal amount)
    {
        if (IsEnding(x.B) && owner != BlitzLimits.PlayerOwner)
            return;
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "治疗量不能为负。");
        ref BlitzCreature creature = ref x.B.Creature(owner);
        int before = creature.CurrentHp;
        creature.CurrentHp = (int)Math.Min(creature.CurrentHp + amount, creature.MaxHp);
        int restored = creature.CurrentHp - before;
        if (restored > 0 && owner == BlitzLimits.PlayerOwner)
            x.B.AddCounter(BlitzCounter.RecoveredHp, restored);
        if (amount > 0m)
            AfterCurrentHpChanged(ref x, owner, amount);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.GainMaxHp</c>: the capped max-HP delta is then healed.</summary>
    public static void GainMaxHp(ref BlitzExec x, byte owner, decimal amount)
    {
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "最大生命增加量不能为负。");
        ref BlitzCreature creature = ref x.B.Creature(owner);
        int before = creature.MaxHp;
        creature.MaxHp = Math.Clamp((int)(before + amount), 1, BlitzLimits.ValueClamp);
        creature.CurrentHp = Math.Min(creature.CurrentHp, creature.MaxHp);
        Heal(ref x, owner, creature.MaxHp - before);
    }

    /// <summary>
    /// Mirrors <c>AfterDamageGivenMirrors.HandlePaperCutsPower</c>'s max-HP loss: when the new maximum
    /// is below the current HP, the difference is dealt as unblockable unpowered damage with no
    /// dealer, then the maximum is clamped to at least 1.
    /// </summary>
    public static void LoseMaxHp(ref BlitzExec x, byte owner, int amount)
        => LoseMaxHp(ref x, owner, amount, BlitzValueProps.Unblockable | BlitzValueProps.Unpowered);

    /// <summary>
    /// <see cref="LoseMaxHp(ref BlitzExec, byte, int)"/> with the native <c>isFromCard</c> damage props.
    /// <c>CreatureCmd.LoseMaxHp(..., isFromCard: true)</c> (BrightestFlame) adds
    /// <c>ValueProp.Move</c> to the unblockable unpowered HP loss.
    /// </summary>
    public static void LoseMaxHp(ref BlitzExec x, byte owner, int amount, BlitzValueProps props)
    {
        if (amount <= 0)
            return;
        ref BlitzCreature creature = ref x.B.Creature(owner);
        int newMaxHp = creature.MaxHp - amount;
        if (newMaxHp < creature.CurrentHp)
        {
            Span<byte> targets = stackalloc byte[1];
            targets[0] = owner;
            Damage(ref x, targets, creature.CurrentHp - newMaxHp, props, BlitzLimits.NoOwner,
                hasCardSource: false, default);
            if (x.NeedsChoice)
                return;
        }
        ref BlitzCreature current = ref x.B.Creature(owner);
        current.MaxHp = Math.Max(1, newMaxHp);
        // Creature.SetMaxHpInternal clamps the current HP to the new maximum.
        current.CurrentHp = Math.Min(current.CurrentHp, current.MaxHp);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.GainEnergy</c> (no supported energy-gain modifiers yet).</summary>
    public static void GainEnergy(ref BlitzExec x, decimal amount)
    {
        if (IsEnding(x.B) || amount <= 0m)
            return;
        x.B.Energy = (int)Math.Clamp(x.B.Energy + amount, 0m, BlitzLimits.ValueClamp);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.LoseEnergy</c>.</summary>
    public static void LoseEnergy(ref BlitzExec x, decimal amount)
    {
        if (IsEnding(x.B) || amount <= 0m)
            return;
        x.B.Energy = (int)Math.Clamp(x.B.Energy - amount, 0m, BlitzLimits.ValueClamp);
    }
}
