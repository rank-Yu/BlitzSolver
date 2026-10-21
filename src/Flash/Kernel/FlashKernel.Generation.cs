namespace CombatSolver.Flash.Kernel;

public static partial class FlashKernel
{
    /// <summary>Bit 0 of <see cref="FlashCard.Private"/>: this exact Fetch card played this turn.</summary>
    internal const short FetchPlayedBit = 1;

    /// <summary>
    /// A fresh canonical card at <paramref name="upgrade"/>, as the existing engine creates generated
    /// cards (<c>PredictedCard.Create(CanonicalModels.Card&lt;T&gt;())</c>): canonical cost, star cost and
    /// keywords, no deck version, no modifiers.
    /// </summary>
    public static FlashCard CreateCard(FlashRules rules, ushort cardId, int upgrade = 0)
    {
        FlashCardStats stats = rules.Card(cardId, upgrade);
        FlashCard card = new()
        {
            Id = cardId,
            Upgrade = checked((byte)upgrade),
            Keywords = stats.Keywords | FlashKeywords.CreatedAfterRoot,
            BaseCost = checked((sbyte)(stats.CostsX ? 0 : stats.EnergyCost)),
            BaseStarCost = checked((sbyte)stats.StarCost),
        };
        if (FlashRegistry.Card(cardId).UsesIdentityLedger)
            card.Private = FlashLimits.UninitializedLedger;
        if (stats.CostsX)
            card.Flags |= FlashCardFlags.CostsX;
        if (stats.StarCostsX)
            card.Flags |= FlashCardFlags.StarCostsX;
        return card;
    }

    /// <summary>
    /// Samples <paramref name="count"/> cards from a root-frozen generation pool. Distinct selection
    /// mirrors <c>IEnumerableExtensions.TakeRandom</c> (<c>UnstableShuffle</c> over the whole list, then
    /// Take), so it consumes one Shuffle-stream draw per element beyond the first even when fewer cards
    /// are taken. With-replacement selection mirrors <c>Rng.NextItem</c> per card. Cards are created at
    /// upgrade 0, as <c>PredictedCard.Create(canonical, player)</c> does.
    /// </summary>
    public static int SampleCardPool(ref FlashBattle battle, FlashRules rules, string poolKey, int count,
        bool distinct, FlashRngStream stream, Span<FlashCard> selected)
    {
        FlashCardPool pool = rules.CardPool(poolKey);
        if (pool.Cards.Length == 0)
            return 0;
        if (count > 0 && selected.Length < count)
            throw new FlashCapacityException("GeneratedCards", selected.Length);
        ref FlashRngState rng = ref battle.RngStream(stream);
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
    public static void MakeFreeThisTurn(FlashRules rules, ref FlashCard card)
    {
        FlashCardStats stats = rules.Card(card.Id, card.Upgrade);
        // CardEnergyCost.SetThisTurnOrUntilPlayed: the modifier is added when cost != 0 or the
        // canonical cost is >= 0, so X-cost cards (canonical 0) still receive it.
        if (stats.EnergyCost >= 0)
        {
            if (card.EnergyModifierCount >= FlashLimits.CardEnergyCostModifiers)
                throw new FlashCapacityException("Card.EnergyModifiers", FlashLimits.CardEnergyCostModifiers);
            card.EnergyModifiers[card.EnergyModifierCount] = FlashCostModifier.Create(
                0, FlashCostType.Absolute, FlashCostExpiration.EndOfTurnOrWhenPlayed, reduceOnly: false);
            card.EnergyModifierCount++;
        }
        card.SetTemporaryStarCost(0, clearsAtTurnEnd: true, clearsWhenPlayed: true);
    }

    /// <summary>
    /// Mirrors <c>CardModel.SetToFreeThisCombat</c>: an absolute 0 energy modifier until the end of
    /// combat and a 0 temporary star cost that clears neither at turn end nor when played. Only used
    /// on freshly generated cards (Metamorphosis), so the single temporary-star-cost slot is free.
    /// </summary>
    public static void MakeFreeThisCombat(FlashRules rules, ref FlashCard card)
    {
        FlashCardStats stats = rules.Card(card.Id, card.Upgrade);
        if (stats.EnergyCost >= 0)
        {
            if (card.EnergyModifierCount >= FlashLimits.CardEnergyCostModifiers)
                throw new FlashCapacityException("Card.EnergyModifiers", FlashLimits.CardEnergyCostModifiers);
            card.EnergyModifiers[card.EnergyModifierCount] = FlashCostModifier.Create(
                0, FlashCostType.Absolute, FlashCostExpiration.EndOfCombat, reduceOnly: false);
            card.EnergyModifierCount++;
        }
        card.SetTemporaryStarCost(0, clearsAtTurnEnd: false, clearsWhenPlayed: false);
    }

    /// <summary>
    /// Stores the block an instanced power grants when its owner's block is cleared
    /// (<c>ToricToughnessPower.SetBlock</c>): the native field is a decimal, but the observed value
    /// is the settled card block gain, an integer. A fractional value cannot be represented in
    /// <see cref="FlashPower.Aux0"/> and is rejected as an explicit boundary.
    /// </summary>
    public static void SetInstanceBlockValue(ref FlashBattle battle, int index, decimal block)
    {
        if ((uint)index >= (uint)battle.PowerCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Power 行越界。");
        if (block != decimal.Truncate(block) || block < 0m || block > int.MaxValue)
            throw new FlashUnsupportedException($"实例 Power 的格挡量 {block} 不是可表示的非负整数。");
        battle.Powers[index].Aux0 = (int)block;
    }

    /// <summary>
    /// Replaces the card at <paramref name="index"/> in <paramref name="pile"/> with
    /// <paramref name="replacement"/> at the same position, mirroring <c>CardCmd.Transform</c> as the
    /// existing engine resolves it (<c>CardChoiceResolution.AddTransformedCard</c>). The original has
    /// no instance identity in Flash, so the position is the identity.
    /// </summary>
    public static void ReplaceCard(ref FlashExec x, FlashPile pile, int index, in FlashCard replacement)
    {
        // CardChoiceSupport.TransformCardToGeneratedReplacement has no ending gate: Entropy still
        // transforms a hand card when the draw already killed the last enemy (the turn-start power
        // pass walks the power table directly). Only the card- and relic-listener dispatches stop.
        FlashCard entered = replacement;
        entered.Keywords |= FlashKeywords.CreatedAfterRoot;
        // A replacement is a new native card: Dampen's per-card original upgrade belongs to the object
        // that was downgraded, not to a generated/transformed value.
        entered.DampenOriginalUpgrade = 0;
        FlashPiles.RemoveAt(ref x.B, pile, index);
        int insert = Math.Min(index, FlashPiles.Count(x.B, pile));
        FlashPiles.Insert(ref x.B, pile, insert, entered);
        x.B.AddCounter(FlashCounter.CardsGeneratedThisCombat, 1);
        EnteredCombat(ref x, replacement, pile, insert);
        if (!x.NeedsChoice)
            AfterGeneratedForCombat(ref x, target: pile, insert, FlashLimits.PlayerOwner);
    }

    /// <summary>
    /// Mirrors <c>CardChoiceSupport.TransformCardBatch</c> / <c>CardCmd.Transform</c>: every selected
    /// card is removed first (recording its index in the shrinking pile), then the replacements are
    /// inserted in (pile, index) order, each entering combat and firing
    /// <c>AfterCardGeneratedForCombat</c>. Used by Compact's Status → Fuel transformation.
    /// </summary>
    public static void TransformCards(ref FlashExec x, FlashPile pile, scoped ReadOnlySpan<int> selected,
        FlashCard replacement, int upgrade)
    {
        if (selected.IsEmpty || IsOverOrEnding(x.B))
            return;
        Span<FlashCard> originals = stackalloc FlashCard[selected.Length];
        Span<int> occurrences = stackalloc int[selected.Length];
        Span<int> removal = stackalloc int[selected.Length];
        int count = FlashPiles.Count(x.B, pile);
        ReadOnlySpan<FlashCard> before = FlashPiles.ReadCards(x.B, pile);
        for (int item = 0; item < selected.Length; item++)
        {
            if ((uint)selected[item] >= (uint)count)
                throw new FlashUnsupportedException("变换选中的卡不在原牌堆。");
            originals[item] = before[selected[item]];
            int occurrence = 0;
            for (int prior = 0; prior < selected[item]; prior++)
            {
                if (FlashFingerprint.Equal(before[prior], originals[item]))
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
                    && FlashFingerprint.Equal(originals[prior], originals[item]))
                {
                    occurrence--;
                }
            }
            int position = -1;
            ReadOnlySpan<FlashCard> cards = FlashPiles.ReadCards(x.B, pile);
            for (int index = 0; index < cards.Length; index++)
            {
                if (!FlashFingerprint.Equal(cards[index], originals[item]))
                    continue;
                if (occurrence-- == 0)
                {
                    position = index;
                    break;
                }
            }
            if (position < 0)
                throw new FlashUnsupportedException("变换选中的卡在结算前离开了原牌堆。");
            removal[item] = position;
            FlashPiles.RemoveAt(ref x.B, pile, position);
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
            FlashCard card = replacement;
            card.Upgrade = checked((byte)upgrade);
            card.DampenOriginalUpgrade = 0;
            card.BaseCost = checked((sbyte)x.R.Card(card.Id, upgrade).EnergyCost);
            card.BaseStarCost = checked((sbyte)x.R.Card(card.Id, upgrade).StarCost);
            card.Keywords = x.R.Card(card.Id, upgrade).Keywords | FlashKeywords.CreatedAfterRoot;
            int insert = Math.Min(removal[item], FlashPiles.Count(x.B, pile));
            FlashPiles.Insert(ref x.B, pile, insert, card);
            x.B.AddCounter(FlashCounter.CardsGeneratedThisCombat, 1);
            EnteredCombat(ref x, card, pile, insert);
            if (x.NeedsChoice)
                return;
            if (!AfterGeneratedForCombat(ref x, pile, insert, FlashLimits.PlayerOwner))
                return;
        }
    }

    /// <summary>
    /// <c>SimulatedCombatState.CardEventHistory.AfterCardEnteredCombat</c>: the player's relic listeners
    /// (<c>AfterCardEnteredCombat</c>, GhostSeed) run before the card-specific cost reactions
    /// (Stomp, Pinpoint, Banshee's Cry), then <c>FlashCardDef.AfterEnteredCombat</c>.
    /// </summary>
    private static void EnteredCombat(ref FlashExec x, in FlashCard card, FlashPile pile, int index)
    {
        AfterCardEnteredCombatHook entered = new() { Pile = pile, Index = index, Card = card };
        FlashHooks.Dispatch(ref x, ref entered, gatedByEnding: false);
        if (x.NeedsChoice)
            return;
        // CardEventHistory.AfterCardEnteredCombat returns before its card-specific switch for clones.
        if (!card.IsClone)
        {
            FlashRegistry.Card(card.Id).AfterEnteredCombat(ref x, pile, index);
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
    public static bool AddGeneratedCard(ref FlashExec x, in FlashCard card, FlashPile pile,
        FlashPilePosition position = FlashPilePosition.Bottom, byte creator = FlashLimits.PlayerOwner)
    {
        if (!IsInProgress(x.B))
            return false;
        // CombatPredictionHistory.CardGenerated is recorded before AddToPile; a card refused because the
        // combat is ending still counts as generated (HeirloomHammer clones a card on a killing blow).
        // AddGeneratedCardsToCombat keeps looping and the card's OnPlay keeps running after the refused
        // add, so this reports success without inserting the card: callers must not treat the value as
        // being in a pile, and no generation hook runs for it.
        if (creator == FlashLimits.PlayerOwner)
            x.B.AddCounter(FlashCounter.CardsGeneratedThisCombat, 1);
        if (IsEnding(x.B) || x.B.Player.IsDead)
            return true;
        FlashPile target = pile == FlashPile.Hand && x.B.HandCount >= x.B.MaxHandSize ? FlashPile.Discard : pile;
        int count = FlashPiles.Count(x.B, target);
        int index = position switch
        {
            FlashPilePosition.Bottom => count,
            FlashPilePosition.Top => 0,
            FlashPilePosition.Random => x.B.RngStream(FlashRngStream.Shuffle).NextInt(count + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
        };
        FlashCard added = card;
        added.Keywords |= FlashKeywords.CreatedAfterRoot;
        FlashPiles.Insert(ref x.B, target, index, added);
        EnteredCombat(ref x, added, target, index);
        if (x.NeedsChoice)
            return false;
        return AfterGeneratedForCombat(ref x, target, index, creator);
    }

    /// <summary>
    /// <c>HookMirrors.AfterCardGeneratedForCombat</c> after a generated card entered combat. The hook
    /// receives the card's absolute position; <paramref name="index"/> is pile-local.
    /// </summary>
    private static bool AfterGeneratedForCombat(ref FlashExec x, FlashPile target, int index, byte creator)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.AfterCardGeneratedForCombat) != 0)
        {
            Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
            int listenerCount = FlashHooks.CollectListeners(x.B, FlashHookMask.AfterCardGeneratedForCombat, listeners);
            for (int listener = 0; listener < listenerCount; listener++)
            {
                FlashHooks.Definition(x.B, listeners[listener])
                    .AfterCardGeneratedForCombat(ref x, listeners[listener],
                        FlashPiles.Start(x.B, target) + index, creator);
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
    public static FlashCard CloneCard(in FlashCard card)
    {
        FlashCard clone = card;
        clone.Flags &= ~(FlashCardFlags.ExhaustOnNextPlay | FlashCardFlags.DeckCard
            | FlashCardFlags.RemovedFromState | FlashCardFlags.PowerAfflictionChecked);
        clone.Keywords |= FlashKeywords.CreatedAfterRoot;
        clone.IsClone = true;
        // The clone is a new native object; a BeatDown transient mark and Dampen's per-card original
        // upgrade belong to the original.
        clone.TransientMark = 0;
        clone.DampenOriginalUpgrade = 0;
        // PredictedCard/CardModel clones are new objects: the existing engine's per-card dictionaries
        // have no entry for them until their first normalization/play. Other cards keep Private (the
        // native clone copies Claw/Rampage-style private counters).
        FlashCardDef cloneDef = FlashRegistry.Card(card.Id);
        if (cloneDef.UsesIdentityLedger)
            clone.Private = FlashLimits.UninitializedLedger;
        if (cloneDef.TracksFetchPlay)
            clone.Private &= ~FetchPlayedBit;
        return clone;
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.Heal</c>.</summary>
    public static void Heal(ref FlashExec x, byte owner, decimal amount)
    {
        if (IsEnding(x.B) && owner != FlashLimits.PlayerOwner)
            return;
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "治疗量不能为负。");
        ref FlashCreature creature = ref x.B.Creature(owner);
        int before = creature.CurrentHp;
        creature.CurrentHp = (int)Math.Min(creature.CurrentHp + amount, creature.MaxHp);
        int restored = creature.CurrentHp - before;
        if (restored > 0 && owner == FlashLimits.PlayerOwner)
            x.B.AddCounter(FlashCounter.RecoveredHp, restored);
        if (amount > 0m)
            AfterCurrentHpChanged(ref x, owner, amount);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.GainMaxHp</c>: the capped max-HP delta is then healed.</summary>
    public static void GainMaxHp(ref FlashExec x, byte owner, decimal amount)
    {
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "最大生命增加量不能为负。");
        ref FlashCreature creature = ref x.B.Creature(owner);
        int before = creature.MaxHp;
        creature.MaxHp = Math.Clamp((int)(before + amount), 1, FlashLimits.ValueClamp);
        creature.CurrentHp = Math.Min(creature.CurrentHp, creature.MaxHp);
        Heal(ref x, owner, creature.MaxHp - before);
    }

    /// <summary>
    /// Mirrors <c>AfterDamageGivenMirrors.HandlePaperCutsPower</c>'s max-HP loss: when the new maximum
    /// is below the current HP, the difference is dealt as unblockable unpowered damage with no
    /// dealer, then the maximum is clamped to at least 1.
    /// </summary>
    public static void LoseMaxHp(ref FlashExec x, byte owner, int amount)
        => LoseMaxHp(ref x, owner, amount, FlashValueProps.Unblockable | FlashValueProps.Unpowered);

    /// <summary>
    /// <see cref="LoseMaxHp(ref FlashExec, byte, int)"/> with the native <c>isFromCard</c> damage props.
    /// <c>CreatureCmd.LoseMaxHp(..., isFromCard: true)</c> (BrightestFlame) adds
    /// <c>ValueProp.Move</c> to the unblockable unpowered HP loss.
    /// </summary>
    public static void LoseMaxHp(ref FlashExec x, byte owner, int amount, FlashValueProps props)
    {
        if (amount <= 0)
            return;
        ref FlashCreature creature = ref x.B.Creature(owner);
        int newMaxHp = creature.MaxHp - amount;
        if (newMaxHp < creature.CurrentHp)
        {
            Span<byte> targets = stackalloc byte[1];
            targets[0] = owner;
            Damage(ref x, targets, creature.CurrentHp - newMaxHp, props, FlashLimits.NoOwner,
                hasCardSource: false, default);
            if (x.NeedsChoice)
                return;
        }
        ref FlashCreature current = ref x.B.Creature(owner);
        current.MaxHp = Math.Max(1, newMaxHp);
        // Creature.SetMaxHpInternal clamps the current HP to the new maximum.
        current.CurrentHp = Math.Min(current.CurrentHp, current.MaxHp);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.GainEnergy</c> (no supported energy-gain modifiers yet).</summary>
    public static void GainEnergy(ref FlashExec x, decimal amount)
    {
        if (IsEnding(x.B) || amount <= 0m)
            return;
        x.B.Energy = (int)Math.Clamp(x.B.Energy + amount, 0m, FlashLimits.ValueClamp);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.LoseEnergy</c>.</summary>
    public static void LoseEnergy(ref FlashExec x, decimal amount)
    {
        if (IsEnding(x.B) || amount <= 0m)
            return;
        x.B.Energy = (int)Math.Clamp(x.B.Energy - amount, 0m, FlashLimits.ValueClamp);
    }
}
