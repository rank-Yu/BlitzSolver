namespace CombatSolver.Engine.InCombat.Flash;

public static partial class FlashKernel
{
    /// <summary>
    /// Mirrors <c>SimulatedCombatState.CanPlayCard</c> → <c>CombatPredictionSimulator.CanPlay</c>
    /// for a card in hand.
    /// </summary>
    public static bool CanPlayCard(in FlashBattle battle, FlashRules rules, int handIndex)
    {
        ref readonly FlashCard card = ref battle.Cards[handIndex];
        // Enthralled lock and Smog are rejected at admission until supported.
        if (card.HasKeyword(FlashKeywords.Unplayable))
            return false;
        int energyCost = EnergyCostWithModifiers(battle, card);
        int starCost = StarCostWithModifiers(battle, card);
        // ShouldPayExcessEnergyCostWithStars has no supported listeners yet.
        if (energyCost > battle.Energy || starCost > battle.Stars)
            return false;
        // ShouldPlay has no supported listeners yet; CardIsPlayable conditions are per card.
        return FlashRegistry.Card(card.Id).IsPlayable(battle, rules, card);
    }

    /// <summary>Mirrors <c>GetEnergyCostWithModifiers</c> (X cost pays all current energy).</summary>
    public static int EnergyCostWithModifiers(in FlashBattle battle, in FlashCard card)
    {
        if ((card.Flags & FlashCardFlags.CostsX) != 0)
            return battle.Energy;
        int cost = card.BaseCost;
        if (cost < 0)
            return Math.Max(0, cost);
        for (int index = 0; index < card.EnergyModifierCount; index++)
        {
            FlashCostModifier modifier = card.EnergyModifiers[index];
            cost = modifier.Type switch
            {
                FlashCostType.Absolute => modifier.IsReduceOnly ? Math.Min(cost, modifier.Amount) : modifier.Amount,
                FlashCostType.Relative => modifier.IsReduceOnly ? Math.Min(cost, cost + modifier.Amount) : cost + modifier.Amount,
                _ => throw new FlashUnsupportedException($"未知的费用修饰类型 {modifier.Type}。"),
            };
        }
        // ModifyEnergyCostInCombat (normal and late passes) has no supported listeners yet.
        return Math.Max(0, cost);
    }

    /// <summary>Mirrors <c>GetStarCostWithModifiers</c>.</summary>
    public static int StarCostWithModifiers(in FlashBattle battle, in FlashCard card)
    {
        if ((card.Flags & FlashCardFlags.StarCostsX) != 0)
            return battle.Stars;
        int cost = card.HasTemporaryStarCost ? card.TemporaryStarCost : card.BaseStarCost;
        // ModifyStarCost has no supported listeners yet.
        return Math.Max(0, cost);
    }

    /// <summary>
    /// Mirrors a manual play through <c>CombatPredictionSimulator.ManualPlay</c>: spend resources,
    /// move to the play pile, run every play index (effect then tail) and settle the result pile.
    /// The caller has already checked playability and the target.
    /// </summary>
    public static void ManualPlay(ref FlashExec x, int handIndex, byte target)
    {
        ref FlashBattle battle = ref x.B;
        FlashCard card = battle.Cards[handIndex];
        FlashCardDef def = FlashRegistry.Card(card.Id);
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);

        // SpendResources.
        int energyValue = EnergyCostWithModifiers(battle, card);
        int starValue = StarCostWithModifiers(battle, card);
        x.XValue = energyValue;
        if (energyValue > 0)
            battle.Energy = (int)Math.Clamp((long)battle.Energy - energyValue, 0, FlashLimits.ValueClamp);
        // AfterEnergySpent (Orbit) is not supported yet.
        if (starValue > 0)
        {
            battle.Stars = (int)Math.Clamp((long)battle.Stars - starValue, 0, FlashLimits.ValueClamp);
            // AfterStarsSpent is not supported yet.
        }

        // OnPlayWrapper: AddDuringManualCardPlay.
        if (IsOverOrEnding(battle))
            return;
        FlashCard removed = FlashPiles.RemoveAt(ref battle, FlashPile.Hand, handIndex);
        int playPosition = battle.PlayCount;
        FlashPiles.Append(ref battle, FlashPile.Play, removed);

        FlashPile result = ResultLocation(card, stats, out bool removeFromCombat);
        // ModifyCardPlayResultLocation and ModifyCardPlayCount have no supported listeners yet.
        int playCount = card.ReplayCount + 1;
        if (battle.Player.IsDead)
            return;

        x.Card = card;
        x.Stats = stats;
        x.Target = target;
        x.EffectDepth++;
        bool completed = true;
        for (int index = 0; index < playCount; index++)
        {
            if (IsOverOrEnding(x.B))
                break;
            x.PlayIndex = index;
            // BeforeCardPlayed has no supported listeners yet.
            def.OnPlay(ref x);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            // Tail: abort if the owner died; enchantment and affliction OnPlay are not supported yet.
            if (x.B.Player.IsDead)
            {
                completed = false;
                break;
            }
            // CardPlayFinished, AfterCardPlayed and CompleteCardPlayEffects have no tracked
            // counters or supported listeners yet.
        }
        x.EffectDepth--;
        if (x.NeedsChoice || !completed)
            return;

        // ContinueCardResultExecution: the card is still in the play pile at its position. The
        // native cost cleanup touches only this card, so it runs before the move here.
        if (playPosition < x.B.PlayCount)
        {
            if (removeFromCombat)
            {
                FlashPiles.RemoveAt(ref x.B, FlashPile.Play, playPosition);
                return;
            }
            AfterCardPlayedCleanup(ref FlashPiles.At(ref x.B, FlashPile.Play, playPosition));
            if (result == FlashPile.Exhaust)
                Exhaust(ref x, FlashPile.Play, playPosition);
            else
                MoveCard(ref x.B, FlashPile.Play, playPosition, result);
        }
        // AfterHandEmptied has no supported listeners yet.
    }

    /// <summary>Mirrors <c>CardResultLocationMirrors.GetResultLocation</c> for the supported cards.</summary>
    private static FlashPile ResultLocation(in FlashCard card, FlashCardStats stats, out bool removeFromCombat)
    {
        removeFromCombat = false;
        if (stats.Type == FlashCardType.Power)
        {
            removeFromCombat = true;
            return FlashPile.Play;
        }
        if ((card.Flags & FlashCardFlags.ExhaustOnNextPlay) != 0 || card.HasKeyword(FlashKeywords.Exhaust))
            return FlashPile.Exhaust;
        return FlashPile.Discard;
    }

    /// <summary>Mirrors <c>CardEnergyCost.AfterCardPlayedCleanup</c> and the temporary star cost cleanup.</summary>
    private static void AfterCardPlayedCleanup(ref FlashCard card)
    {
        int kept = 0;
        for (int index = 0; index < card.EnergyModifierCount; index++)
        {
            FlashCostModifier modifier = card.EnergyModifiers[index];
            if (modifier.Expiration == FlashCostExpiration.WhenPlayed)
                continue;
            card.EnergyModifiers[kept++] = modifier;
        }
        for (int index = kept; index < FlashLimits.CardEnergyCostModifiers; index++)
            card.EnergyModifiers[index] = default;
        card.EnergyModifierCount = kept;
        if (card.HasTemporaryStarCost && card.TemporaryStarCostClearsWhenPlayed)
            card.ClearTemporaryStarCost();
    }
}
