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
        FlashCardDef def = FlashRegistry.Card(card.Id);
        // SimulatedCombatState.IsCardPlayPrevented: the Enthralled lock. Smog is rejected at admission.
        if (!def.LocksOtherHandCards)
        {
            for (int index = 0; index < battle.HandCount; index++)
            {
                if (FlashRegistry.Card(battle.Cards[index].Id).LocksOtherHandCards)
                    return false;
            }
        }
        if (card.HasKeyword(FlashKeywords.Unplayable))
            return false;
        int energyCost = EnergyCostWithModifiers(battle, card);
        int starCost = StarCostWithModifiers(battle, card);
        // ShouldPayExcessEnergyCostWithStars has no supported listeners yet.
        if (energyCost > battle.Energy || starCost > battle.Stars)
            return false;
        if (!ShouldPlay(battle, rules, card))
            return false;
        return def.IsPlayable(battle, rules, card);
    }

    /// <summary>Native <c>ShouldPlay</c> through <c>HookMirrors.ShouldPlay</c>: any combat listener may forbid the play.</summary>
    private static bool ShouldPlay(in FlashBattle battle, FlashRules rules, in FlashCard card)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.ShouldPlay) == 0 || IsOverOrEnding(battle))
            return true;
        Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        int count = FlashHooks.CollectListeners(battle, FlashHookMask.ShouldPlay, listeners);
        for (int index = 0; index < count; index++)
        {
            if (!FlashHooks.Definition(battle, listeners[index]).ShouldPlay(battle, rules, listeners[index], card))
                return false;
        }
        return true;
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
            battle.AddCounter(FlashCounter.EnergySpentThisTurn, energyValue);
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
            FlashCardPlayInfo play = new(card, target, index, playCount, removeFromCombat ? FlashPile.Play : result);
            BeforeCardPlayedHook before = new() { Play = play };
            FlashHooks.Dispatch(ref x, ref before, gatedByEnding: true);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            RecordCardPlayStarted(ref x.B, stats, index, energyValue);
            def.OnPlay(ref x);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            def.OwnChoice(ref x);
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
            // CardPlayFinished: no tracked counter yet. AfterCardPlayed runs its normal pass, then
            // re-reads the listeners for the late pass, without the ending gate.
            AfterCardPlayedHook after = new() { Play = play };
            FlashHooks.Dispatch(ref x, ref after, gatedByEnding: false);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            AfterCardPlayedLateHook afterLate = new() { Play = play };
            FlashHooks.Dispatch(ref x, ref afterLate, gatedByEnding: false);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            // CompleteCardPlayEffects: power amount resolution has no supported listeners yet.
            RecordCardPlayed(ref x.B, stats);
            // The tail fails when the owner died during the completion effects.
            if (x.B.Player.IsDead)
            {
                completed = false;
                break;
            }
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

    /// <summary>Mirrors <c>SimulatedCombatState.RecordCardPlayStarted</c> for a manual play.</summary>
    private static void RecordCardPlayStarted(ref FlashBattle battle, FlashCardStats stats, int playIndex, int energyValue)
    {
        battle.AddCounter(FlashCounter.CardPlayStartsThisTurn, 1);
        if (stats.Type == FlashCardType.Attack)
            battle.AddCounter(FlashCounter.AttackPlayStartsThisTurn, 1);
        if (stats.Type is FlashCardType.Attack or FlashCardType.Skill)
            battle.AddCounter(FlashCounter.AttackSkillStartsThisTurn, 1);
        if (stats.Type == FlashCardType.Attack && energyValue == 0)
            battle.AddCounter(FlashCounter.ZeroCostAttackStartsThisTurn, 1);
        if (playIndex != 0)
            return;
        battle.AddCounter(FlashCounter.CardPlaySeriesStartedThisTurn, 1);
        battle.AddCounter(FlashCounter.ManualCardsPlayedThisTurn, 1);
    }

    /// <summary>
    /// Mirrors <c>RecordCardPlayed</c> then <c>RecordCardLifecycle</c> in
    /// <c>SimulatedCombatState.CompleteCardPlayEffects</c>. Slow, Fetch, relic card counters and
    /// returning cards are not supported yet.
    /// </summary>
    private static void RecordCardPlayed(ref FlashBattle battle, FlashCardStats stats)
    {
        if (stats.Type == FlashCardType.Attack)
        {
            battle.AddCounter(FlashCounter.AttacksPlayedThisTurn, 1);
            if ((stats.Tags & FlashCardTag.Shiv) != 0)
                battle.AddCounter(FlashCounter.ShivsPlayedThisTurn, 1);
        }
        battle.AddCounter(FlashCounter.CardsPlayedThisTurn, 1);
        if (stats.Type == FlashCardType.Skill)
            battle.AddCounter(FlashCounter.SkillCardsPlayedThisTurn, 1);
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
            // CardEnergyCost.AfterCardPlayedCleanup: Expiration.HasFlag(WhenPlayed), so EndOfTurn | WhenPlayed goes too.
            if (((byte)modifier.Expiration & (byte)FlashCostExpiration.WhenPlayed) != 0)
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
