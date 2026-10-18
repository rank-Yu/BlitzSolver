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
    /// Mirrors <c>CombatPredictionSimulator.AddGeneratedCardsToCombat</c> for one card: insertion as
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
        // SimulatedCombatState.AfterCardEnteredCombat: card-specific cost adjustments.
        FlashRegistry.Card(card.Id).AfterEnteredCombat(ref x, target, index);
        // AfterCardGeneratedForCombat: no supported listeners yet.
        return true;
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
