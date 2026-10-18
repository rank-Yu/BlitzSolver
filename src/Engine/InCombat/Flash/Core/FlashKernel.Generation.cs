namespace CombatSolver.Engine.InCombat.Flash;

public static partial class FlashKernel
{
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
            Keywords = stats.Keywords,
            BaseCost = checked((sbyte)(stats.CostsX ? 0 : stats.EnergyCost)),
            BaseStarCost = checked((sbyte)stats.StarCost),
        };
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
        if (!stats.CostsX && stats.EnergyCost >= 0)
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
    /// Replaces the card at <paramref name="index"/> in <paramref name="pile"/> with
    /// <paramref name="replacement"/> at the same position, mirroring <c>CardCmd.Transform</c> as the
    /// existing engine resolves it (<c>CardChoiceResolution.AddTransformedCard</c>). The original has
    /// no instance identity in Flash, so the position is the identity.
    /// </summary>
    public static void ReplaceCard(ref FlashExec x, FlashPile pile, int index, in FlashCard replacement)
    {
        if (IsOverOrEnding(x.B))
            return;
        FlashPiles.RemoveAt(ref x.B, pile, index);
        int insert = Math.Min(index, FlashPiles.Count(x.B, pile));
        FlashPiles.Insert(ref x.B, pile, insert, replacement);
        EnteredCombat(ref x, replacement, pile, insert);
        // AfterCardGeneratedForCombat and the AfterTransformedFrom/To overrides have no supported
        // listeners or definitions in this content set.
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
        FlashRegistry.Card(card.Id).AfterEnteredCombat(ref x, pile, index);
    }

    /// <summary>Mirrors <c>CombatPredictionSimulator.AddGeneratedCardsToCombat</c> for one card: insertion as
    /// <c>AddToPile</c> does for a card entering combat (full hand diverts to discard, random position
    /// from the Shuffle stream), then the entered-combat sink and <c>AfterCardGeneratedForCombat</c>.
    /// Returns false when the card was not added.
    /// </summary>
    public static bool AddGeneratedCard(ref FlashExec x, in FlashCard card, FlashPile pile,
        FlashPilePosition position = FlashPilePosition.Bottom)
    {
        if (!IsInProgress(x.B))
            return false;
        // History.CardGenerated: no tracked counter yet.
        if (IsEnding(x.B) || x.B.Player.IsDead)
            return false;
        FlashPile target = pile == FlashPile.Hand && x.B.HandCount >= x.B.MaxHandSize ? FlashPile.Discard : pile;
        int count = FlashPiles.Count(x.B, target);
        int index = position switch
        {
            FlashPilePosition.Bottom => count,
            FlashPilePosition.Top => 0,
            FlashPilePosition.Random => x.B.RngStream(FlashRngStream.Shuffle).NextInt(count + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
        };
        FlashPiles.Insert(ref x.B, target, index, card);
        EnteredCombat(ref x, card, target, index);
        // AfterCardGeneratedForCombat: no supported listeners yet.
        return !x.NeedsChoice;
    }

    /// <summary>
    /// Mirrors <c>CardModel.CreateClone</c>: the clone keeps the card's mutable state but not
    /// ExhaustOnNextPlay, and <c>AfterCloned</c> clears the deck link and the removed-from-state mark.
    /// </summary>
    public static FlashCard CloneCard(in FlashCard card)
    {
        FlashCard clone = card;
        clone.Flags &= ~(FlashCardFlags.ExhaustOnNextPlay | FlashCardFlags.DeckCard | FlashCardFlags.RemovedFromState);
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
