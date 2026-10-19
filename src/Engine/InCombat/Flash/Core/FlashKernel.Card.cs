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
        if (HasKeyword(battle, card, FlashKeywords.Unplayable))
            return false;
        int energyCost = EnergyCostWithModifiers(battle, rules, card, FlashPile.Hand);
        int starCost = StarCostWithModifiers(battle, card);
        // ShouldPayExcessEnergyCostWithStars has no supported listeners yet.
        if (energyCost > battle.Energy || starCost > battle.Stars)
            return false;
        if (!ShouldPlay(battle, rules, card))
            return false;
        return def.IsPlayable(battle, rules, card);
    }

    /// <summary>
    /// The effective keyword set at a stable boundary: the card's local keywords, plus the Ethereal
    /// that <c>HexPower.TryModifyKeywordsInCombat</c> grants to a Hexed card while the player holds
    /// HexPower (the existing engine calls the native hook lazily, so the value is not stored).
    /// </summary>
    public static bool HasKeyword(in FlashBattle battle, in FlashCard card, FlashKeywords keyword)
    {
        if (card.HasKeyword(keyword))
            return true;
        if (keyword != FlashKeywords.Ethereal
            || !FlashRegistry.TryAffliction("HEXED", out ushort hexed)
            || !FlashRegistry.TryPower("HEX_POWER", out ushort hex)
            || card.AfflictionId != hexed + 1)
        {
            return false;
        }
        return GetAmount(battle, FlashLimits.PlayerOwner, hex) > 0;
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

    /// <summary>
    /// Mirrors <c>GetEnergyCostWithModifiers</c> (X cost pays all current energy). <paramref name="pile"/>
    /// is the card's current pile, which <c>FreeAttackPower</c> reads (only Hand and Play are free).
    /// </summary>
    public static int EnergyCostWithModifiers(in FlashBattle battle, FlashRules rules, in FlashCard card, FlashPile pile)
    {
        if ((card.Flags & FlashCardFlags.CostsX) != 0)
            return battle.Energy;
        int cost = EnergyCostWithLocalModifiers(battle, card);
        if (cost < 0)
            return Math.Max(0, cost);
        // HookMirrors.ModifyEnergyCostInCombat: normal listeners then late listeners; the existing
        // engine truncates the decimal and clamps at zero afterwards.
        cost = (int)FlashHooks.ModifyEnergyCost(battle, rules, pile, card, cost);
        return Math.Max(0, cost);
    }

    /// <summary>
    /// Mirrors <c>CardEnergyCost.GetWithModifiers(CostModifiers.Local)</c>: the base cost plus the
    /// card's local modifiers, without the combat cost hooks. The existing engine's evaluation uses
    /// this value (never the hook-modified one) for free-card opportunity and skill energy.
    /// </summary>
    public static int EnergyCostWithLocalModifiers(in FlashBattle battle, in FlashCard card)
    {
        int cost = card.BaseCost;
        if (cost < 0)
            return cost;
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
        return Math.Max(0, cost);
    }

    /// <summary>
    /// Mirrors <c>CardModel.ResolveEnergyXValue</c>: the captured X value with <c>Hook.ModifyXValue</c>
    /// applied (Chemical X). Every effect reads X through here, at the point native code calls
    /// <c>ResolveEnergyXValue</c>, not when the resources are spent. Only X-cost cards may resolve it.
    /// </summary>
    public static int ResolveEnergyXValue(ref FlashExec x)
    {
        if ((x.Card.Flags & FlashCardFlags.CostsX) == 0)
            throw new InvalidOperationException("这张卡不是 X 费卡，不能解析 X 值。");
        return FlashHooks.ModifyXValue(x.B, x.R, x.Card, x.XValue);
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
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);

        // SpendResources (the card is still in hand).
        int energyValue = EnergyCostWithModifiers(battle, x.R, card, FlashPile.Hand);
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

        x.Card = card;
        x.Stats = stats;
        x.Target = target;
        PlayFromPlay(ref x, card, target, energyValue, playPosition, isAutoPlay: false);
    }

    /// <summary>
    /// The shared body of <c>OnPlayWrapper</c> once the card is in the play pile: result location,
    /// play count, every play index, and the result-pile tail. Manual and auto-play differ only in
    /// how the card reached the play pile and how resources were handled.
    /// </summary>
    private static void PlayFromPlay(ref FlashExec x, FlashCard card, byte target, int energyValue,
        int playPosition, bool isAutoPlay)
    {
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        FlashPile result = ResultLocation(x.B, card, stats, out bool removeFromCombat);
        // CardModel.GetResultLocationForCardPlay clears ExhaustOnNextPlay when the result is Exhaust
        // (Havoc/Cascade force-exhaust the auto-played card).
        if (result == FlashPile.Exhaust)
        {
            card.Flags &= ~FlashCardFlags.ExhaustOnNextPlay;
            if (playPosition < x.B.PlayCount)
                FlashPiles.At(ref x.B, FlashPile.Play, playPosition).Flags &= ~FlashCardFlags.ExhaustOnNextPlay;
        }
        // HookMirrors.ModifyCardPlayResultLocation, then CardModel.OnPlayWrapper's direct after dispatch
        // over the modifiers that changed the location.
        Span<FlashListener> locationModifiers = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        FlashPile hooked = FlashHooks.ModifyCardPlayResultLocation(ref x, card, isAutoPlay, energyValue, result,
            locationModifiers, out int locationModifierCount, out FlashPilePosition resultPosition);
        if (x.NeedsChoice)
            return;
        if (hooked != result)
        {
            if (removeFromCombat)
                throw new FlashUnsupportedException("出牌去向修改把移出战斗的卡改成了牌堆。");
            if (hooked == FlashPile.Play)
                throw new FlashUnsupportedException($"出牌去向修改返回了无效牌堆 {hooked}。");
            result = hooked;
        }
        if (resultPosition != FlashPilePosition.Bottom && result is not (FlashPile.Draw or FlashPile.Discard or FlashPile.Hand))
            throw new FlashUnsupportedException($"出牌去向位置对 {result} 无定义。");
        FlashHooks.AfterModifyingCardPlayResultLocation(ref x, card, result, locationModifiers[..locationModifierCount]);
        if (x.NeedsChoice)
            return;
        // HookMirrors.ModifyCardPlayCount then Hook.AfterModifyingCardPlayCount. The base count is
        // CardModel.GetEnchantedReplayCount() (the enchantment modifies the replay count first).
        int basePlayCount = EnchantedReplayCount(card);
        Span<FlashListener> playCountModifiers = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        int playCount = FlashHooks.ModifyCardPlayCount(ref x, card, target, basePlayCount + 1,
            playCountModifiers, out int playCountModifierCount);
        FlashHooks.AfterModifyingCardPlayCount(ref x, card, playCountModifiers[..playCountModifierCount]);
        if (x.NeedsChoice)
            return;
        if (x.B.Player.IsDead)
            return;

        // Per-play damage history used by Fisticuffs/Omnislice/HandOfGreed; never part of the state.
        x.CardDamageTotal = 0;
        x.CardKilledTarget = false;
        x.EffectDepth++;
        FlashGeneratedChoiceOptions savedOptionCards = x.GeneratedOptions;
        int savedGeneratedOptions = x.GeneratedOptionCount;
        bool completed = true;
        for (int index = 0; index < playCount; index++)
        {
            if (IsOverOrEnding(x.B))
                break;
            x.PlayIndex = index;
            FlashCardPlayInfo play = new(card, target, index, playCount, removeFromCombat ? FlashPile.Play : result,
                FlashPiles.Start(x.B, FlashPile.Play) + playPosition);
            BeforeCardPlayedHook before = new() { Play = play };
            FlashHooks.Dispatch(ref x, ref before, gatedByEnding: true);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            RecordCardPlayStarted(ref x.B, stats, index, energyValue, isAutoPlay);
            FlashRegistry.Card(card.Id).OnPlay(ref x);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            // CorePowerSupport.ApplyCardPowers resolves queued power amount changes after the card's
            // compensation effects (Vicious draws here, not inside the effect that applied Vulnerable).
            ResolvePowerAmountChanges(ref x);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            // ICombatPredictionCardExecutionSink.ApplyCardPlayEffects settles newly dead enemies right
            // after the card's Power compensation (before the card's own choice and tail). An auto-play
            // that kills the Phrog Parasite must spawn its Wrigglers before the next queued auto-play
            // picks a target.
            ApplyEnemyDeathPowers(ref x);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            // The existing engine normalizes card afflictions at the end of the compensated card
            // effects, before the card's own choice.
            NormalizeCardAfflictions(ref x);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            FlashRegistry.Card(card.Id).OwnChoice(ref x);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            // Tail: abort if the owner died, then EnchantmentModel.OnPlay, then AfflictionModel.OnPlay
            // (CombatPredictionSimulator.CardTailContinuation's stages).
            if (x.B.Player.IsDead)
            {
                completed = false;
                break;
            }
            if (!RunCardAttachmentOnPlay(ref x, playPosition, target))
            {
                completed = false;
                break;
            }
            // CardModel is mutable: the played card value as seen by AfterCardPlayed includes the
            // effects and attachment OnPlay updates of this play index.
            if ((uint)playPosition < (uint)x.B.PlayCount)
            {
                play = new FlashCardPlayInfo(FlashPiles.At(ref x.B, FlashPile.Play, playPosition), target, index,
                    playCount, removeFromCombat ? FlashPile.Play : result,
                    FlashPiles.Start(x.B, FlashPile.Play) + playPosition);
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
            // SimulatedCombatState.CompleteCardPlayEffects resolves the remaining power amount changes
            // before recording the play and its lifecycle counters.
            ResolvePowerAmountChanges(ref x);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            RecordCardPlayed(ref x.B, stats);
            // The tail fails when the owner died during the completion effects.
            if (x.B.Player.IsDead)
            {
                completed = false;
                break;
            }
        }
        x.EffectDepth--;
        x.GeneratedOptions = savedOptionCards;
        x.GeneratedOptionCount = savedGeneratedOptions;
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
                MoveCard(ref x.B, FlashPile.Play, playPosition, result, resultPosition);
        }
        // AfterHandEmptied has no supported listeners yet.
    }

    /// <summary>Mirrors <c>SimulatedCombatState.RecordCardPlayStarted</c> for a manual or auto play.</summary>
    private static void RecordCardPlayStarted(ref FlashBattle battle, FlashCardStats stats, int playIndex,
        int energyValue, bool isAutoPlay)
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
        if (!isAutoPlay)
            battle.AddCounter(FlashCounter.ManualCardsPlayedThisTurn, 1);
    }

    /// <summary>
    /// <c>CardModel.GetEnchantedReplayCount</c>: the base replay count after the enchantment's
    /// <c>EnchantPlayCount</c>. Pure; the evaluation and search read it without mutating the card.
    /// </summary>
    public static int EnchantedReplayCount(in FlashCard card)
    {
        int replayCount = card.ReplayCount;
        if (card.EnchantmentId != 0)
            replayCount = FlashRegistry.Enchantment((ushort)(card.EnchantmentId - 1))
                .EnchantPlayCount(card, replayCount);
        return replayCount;
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
        // CardPlayFinished entry: CombatPredictionHistory records one per completed card play.
        battle.AddCounter(FlashCounter.FinishedCardPlays, 1);
        if (stats.Type == FlashCardType.Skill)
        {
            battle.AddCounter(FlashCounter.SkillCardsPlayedThisTurn, 1);
            // SimulatedCombatState.RecordSkillPlayed's _skillsPlayedThisTurn membership.
            battle.SetCounter(FlashCounter.SkillPlayedThisSideTurn, 1);
        }
    }

    /// <summary>
    /// The current card in the play pile while its tail runs. The play pile segment is located from
    /// the live counts because effects may have added or removed other cards.
    /// </summary>
    public static ref FlashCard CurrentPlayCard(ref FlashExec x)
    {
        if ((uint)x.PlayPosition >= (uint)x.B.PlayCount)
            throw new InvalidOperationException("当前没有正在打出的卡。");
        return ref FlashPiles.At(ref x.B, FlashPile.Play, x.PlayPosition);
    }

    /// <summary>Copies the live play-pile card back into <see cref="FlashExec.Card"/> after a tail hook changed it.</summary>
    public static void SyncCurrentPlayCard(ref FlashExec x) => x.Card = CurrentPlayCard(ref x);

    /// <summary>
    /// <c>EnchantmentModel.OnPlay</c> then <c>AfflictionModel.OnPlay</c> for the card at
    /// <paramref name="playPosition"/>. Returns false when a choice interrupted the tail or the owner
    /// died, matching <c>CardTailContinuation</c>.
    /// </summary>
    private static bool RunCardAttachmentOnPlay(ref FlashExec x, int playPosition, byte target)
    {
        if ((uint)playPosition >= (uint)x.B.PlayCount)
            return true;
        // Nested auto-plays overwrite FlashExec.Card/Stats/Target; restore the outer play context so
        // the attachment hooks see this card and this target (CardModel/CardPlay are per-play objects
        // in the existing engine).
        ref FlashCard played = ref FlashPiles.At(ref x.B, FlashPile.Play, playPosition);
        x.Card = played;
        x.Stats = x.R.Card(played.Id, played.Upgrade);
        x.Target = target;
        x.PlayPosition = playPosition;
        if (played.EnchantmentId != 0)
        {
            FlashRegistry.Enchantment((ushort)(played.EnchantmentId - 1)).OnPlay(ref x, played, target);
            if (x.NeedsChoice || x.B.Player.IsDead)
                return false;
            played = ref FlashPiles.At(ref x.B, FlashPile.Play, playPosition);
        }
        if (played.AfflictionId != 0)
        {
            FlashRegistry.Affliction((ushort)(played.AfflictionId - 1)).OnPlay(ref x, played, target);
            if (x.NeedsChoice || x.B.Player.IsDead)
                return false;
        }
        return true;
    }

    /// <summary>Mirrors <c>CardResultLocationMirrors.GetResultLocation</c> for the supported cards.</summary>
    private static FlashPile ResultLocation(in FlashBattle battle, in FlashCard card, FlashCardStats stats, out bool removeFromCombat)
    {
        removeFromCombat = false;
        if (stats.Type == FlashCardType.Power)
        {
            removeFromCombat = true;
            return FlashPile.Play;
        }
        if ((card.Flags & FlashCardFlags.ExhaustOnNextPlay) != 0 || HasKeyword(battle, card, FlashKeywords.Exhaust))
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
