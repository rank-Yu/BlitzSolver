namespace BlitzSolver.Engine;

public static partial class BlitzKernel
{
    /// <summary>
    /// Mirrors <c>SimulatedCombatState.CanPlayCard</c> → <c>CombatPredictionSimulator.CanPlay</c>
    /// for a card in hand.
    /// </summary>
    public static bool CanPlayCard(in BlitzBattle battle, BlitzRules rules, int handIndex)
        => CanPlayCard(battle, rules, handIndex, out _, out _);

    /// <summary>
    /// Same check, also returning the effective hand costs it computed on the way. The costs are only
    /// meaningful when the result is true; a false result may return before computing them (the
    /// Enthralled lock and Unplayable checks run first). Callers that feed the costs to
    /// <c>ReachableHandValue</c> must not run the energy/star cost hooks a second time.
    /// </summary>
    public static bool CanPlayCard(in BlitzBattle battle, BlitzRules rules, int handIndex,
        out int energyCost, out int starCost)
    {
        energyCost = 0;
        starCost = 0;
        ref readonly BlitzCard card = ref battle.Cards[handIndex];
        BlitzCardDef def = BlitzRegistry.Card(card.Id);
        // SimulatedCombatState.IsCardPlayPrevented: the Enthralled lock. Smog is rejected at admission.
        if (!def.LocksOtherHandCards)
        {
            for (int index = 0; index < battle.HandCount; index++)
            {
                if (BlitzRegistry.Card(battle.Cards[index].Id).LocksOtherHandCards)
                    return false;
            }
        }
        if (HasKeyword(battle, card, BlitzKeywords.Unplayable))
            return false;
        energyCost = EnergyCostWithModifiers(battle, rules, card, BlitzPile.Hand);
        starCost = StarCostWithModifiers(battle, rules, card, BlitzPile.Hand);
        // ShouldPayExcessEnergyCostWithStars has no vanilla listener (Hook.ShouldPayExcessEnergyCostWithStars
        // only observes models, and no model overrides it), so the excess-energy conversion cannot trigger.
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
    public static bool HasKeyword(in BlitzBattle battle, in BlitzCard card, BlitzKeywords keyword)
    {
        if (card.HasKeyword(keyword))
            return true;
        if (keyword != BlitzKeywords.Ethereal
            || !BlitzRegistry.TryAffliction("HEXED", out ushort hexed)
            || !BlitzRegistry.TryPower("HEX_POWER", out ushort hex)
            || card.AfflictionId != hexed + 1)
        {
            return false;
        }
        return GetAmount(battle, BlitzLimits.PlayerOwner, hex) > 0;
    }

    /// <summary>Native <c>ShouldPlay</c> through <c>HookMirrors.ShouldPlay</c>: any combat listener may forbid the play.</summary>
    private static bool ShouldPlay(in BlitzBattle battle, BlitzRules rules, in BlitzCard card)
    {
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.ShouldPlay) == 0 || IsOverOrEnding(battle))
            return true;
        Span<BlitzListener> listeners = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
        int count = BlitzHooks.CollectListeners(battle, BlitzHookMask.ShouldPlay, listeners);
        for (int index = 0; index < count; index++)
        {
            if (!BlitzHooks.Definition(battle, listeners[index]).ShouldPlay(battle, rules, listeners[index], card))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Mirrors <c>GetEnergyCostWithModifiers</c> (X cost pays all current energy). <paramref name="pile"/>
    /// is the card's current pile, which <c>FreeAttackPower</c> reads (only Hand and Play are free).
    /// </summary>
    public static int EnergyCostWithModifiers(in BlitzBattle battle, BlitzRules rules, in BlitzCard card, BlitzPile pile)
    {
        if ((card.Flags & BlitzCardFlags.CostsX) != 0)
            return battle.Energy;
        int cost = EnergyCostWithLocalModifiers(battle, card);
        if (cost < 0)
            return Math.Max(0, cost);
        // HookMirrors.ModifyEnergyCostInCombat: normal listeners then late listeners; the existing
        // engine truncates the decimal and clamps at zero afterwards.
        cost = (int)BlitzHooks.ModifyEnergyCost(battle, rules, pile, card, cost);
        return Math.Max(0, cost);
    }

    /// <summary>
    /// Mirrors <c>CombatPredictedCardExtensions.GetEnergyCostValueWithModifiers</c>: a negative base
    /// costs (Unplayable cards) and X costs are returned unchanged; otherwise local modifiers and the
    /// combat cost hooks apply and the result is clamped at zero. Scrape uses it to decide which drawn
    /// cards are not free, so a -1 Unplayable cost must stay distinguishable from 0.
    /// </summary>
    public static int EnergyCostValueWithModifiers(in BlitzBattle battle, BlitzRules rules, in BlitzCard card,
        BlitzPile pile)
    {
        int cost = card.BaseCost;
        if (cost < 0 || (card.Flags & BlitzCardFlags.CostsX) != 0)
            return cost;
        for (int index = 0; index < card.EnergyModifierCount; index++)
        {
            BlitzCostModifier modifier = card.EnergyModifiers[index];
            cost = modifier.Type switch
            {
                BlitzCostType.Absolute => modifier.IsReduceOnly ? Math.Min(cost, modifier.Amount) : modifier.Amount,
                BlitzCostType.Relative => modifier.IsReduceOnly ? Math.Min(cost, cost + modifier.Amount) : cost + modifier.Amount,
                _ => throw new BlitzUnsupportedException($"未知的费用修饰类型 {modifier.Type}。"),
            };
        }
        cost = (int)BlitzHooks.ModifyEnergyCost(battle, rules, pile, card, cost);
        return Math.Max(0, cost);
    }

    /// <summary>
    /// Mirrors <c>CardEnergyCost.GetWithModifiers(CostModifiers.Local)</c>: the base cost plus the
    /// card's local modifiers, without the combat cost hooks. The existing engine's evaluation uses
    /// this value (never the hook-modified one) for free-card opportunity and skill energy.
    /// </summary>
    public static int EnergyCostWithLocalModifiers(in BlitzBattle battle, in BlitzCard card)
    {
        int cost = card.BaseCost;
        if (cost < 0)
            return cost;
        for (int index = 0; index < card.EnergyModifierCount; index++)
        {
            BlitzCostModifier modifier = card.EnergyModifiers[index];
            cost = modifier.Type switch
            {
                BlitzCostType.Absolute => modifier.IsReduceOnly ? Math.Min(cost, modifier.Amount) : modifier.Amount,
                BlitzCostType.Relative => modifier.IsReduceOnly ? Math.Min(cost, cost + modifier.Amount) : cost + modifier.Amount,
                _ => throw new BlitzUnsupportedException($"未知的费用修饰类型 {modifier.Type}。"),
            };
        }
        return Math.Max(0, cost);
    }

    /// <summary>
    /// Mirrors <c>CardModel.ResolveEnergyXValue</c>: the captured X value with <c>Hook.ModifyXValue</c>
    /// applied (Chemical X). Every effect reads X through here, at the point native code calls
    /// <c>ResolveEnergyXValue</c>, not when the resources are spent. Only X-cost cards may resolve it.
    /// </summary>
    public static int ResolveEnergyXValue(ref BlitzExec x)
    {
        if ((x.Card.Flags & BlitzCardFlags.CostsX) == 0)
            throw new InvalidOperationException("这张卡不是 X 费卡，不能解析 X 值。");
        return BlitzHooks.ModifyXValue(x.B, x.R, x.Card, x.XValue);
    }

    /// <summary>
    /// Mirrors a manual play through <c>CombatPredictionSimulator.ManualPlay</c>: spend resources,
    /// move to the play pile, run every play index (effect then tail) and settle the result pile.
    /// The caller has already checked playability and the target.
    /// </summary>
    public static void ManualPlay(ref BlitzExec x, int handIndex, byte target)
    {
        ref BlitzBattle battle = ref x.B;
        BlitzCard card = battle.Cards[handIndex];
        BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);

        // SpendResources (the card is still in hand).
        int energyValue = EnergyCostWithModifiers(battle, x.R, card, BlitzPile.Hand);
        int starValue = StarCostWithModifiers(battle, x.R, card, BlitzPile.Hand);
        x.XValue = energyValue;
        // LastStarsSpent: captured even when the play is free (auto-play captures it too).
        x.StarValue = starValue;
        if (energyValue > 0)
            battle.AddCounter(BlitzCounter.EnergySpentThisTurn, energyValue);
        if (energyValue > 0)
            battle.Energy = (int)Math.Clamp((long)battle.Energy - energyValue, 0, BlitzLimits.ValueClamp);
        // CardModel.SpendResources: AfterEnergySpent runs before the star cost (OrbitPower).
        if (x.R.HasIdentityContent)
        {
            BlitzHooks.AfterEnergySpent(ref x, card, energyValue);
            if (x.NeedsChoice)
                return;
        }
        if (starValue > 0)
        {
            battle.Stars = (int)Math.Clamp((long)battle.Stars - starValue, 0, BlitzLimits.ValueClamp);
            // PowerLifecycleSupport.AfterStarsSpent: the engine returns from ManualPlay while a pending
            // choice is outstanding, before the card leaves the hand.
            BlitzHooks.AfterStarsSpent(ref x, starValue);
            if (x.NeedsChoice)
                return;
        }

        // OnPlayWrapper: AddDuringManualCardPlay.
        if (IsOverOrEnding(battle))
            return;
        BlitzCard removed = BlitzPiles.RemoveAt(ref battle, BlitzPile.Hand, handIndex);
        int playPosition = battle.PlayCount;
        BlitzPiles.Append(ref battle, BlitzPile.Play, removed);

        x.Card = card;
        x.Stats = stats;
        x.Target = target;
        x.CurrentPlayEnergyValue = energyValue;
        x.CurrentPlayIsAutoPlay = false;
        PlayFromPlay(ref x, card, target, energyValue, playPosition, isAutoPlay: false);
    }

    /// <summary>
    /// The shared body of <c>OnPlayWrapper</c> once the card is in the play pile: result location,
    /// play count, every play index, and the result-pile tail. Manual and auto-play differ only in
    /// how the card reached the play pile and how resources were handled.
    /// </summary>
    private static void PlayFromPlay(ref BlitzExec x, BlitzCard card, byte target, int energyValue,
        int playPosition, bool isAutoPlay)
        => PlayFromPlay(ref x, card, target, energyValue, playPosition, isAutoPlay, out _, out _);

    private static void PlayFromPlay(ref BlitzExec x, BlitzCard card, byte target, int energyValue,
        int playPosition, bool isAutoPlay, out BlitzPile resultPile, out int resultIndex)
    {
        // A nested auto-play inside this play's effects runs its own PlayFromPlay and moves
        // PlayPosition to its slot; put this play's slot back afterwards so CurrentPlayCard keeps
        // addressing this card (nested cards are appended after it and leave the play pile first).
        int outerPlayPosition = x.PlayPosition;
        // A nested auto-play inside this play's effects (OwnChoice discarding a Sly card) completes its
        // own power-application scope, which overwrites LastCardPowerSourceSerial. Restore the outer
        // play's completed scope so Vambrace's AfterCardPlayed still matches its triggering card.
        int outerLastSourceSerial = x.LastCardPowerSourceSerial;
        int outerPlayIdentity = x.PlayIdentity;
        int outerLastPlayIdentity = x.LastPlayIdentity;
        PlayFromPlayCore(ref x, card, target, energyValue, playPosition, isAutoPlay, out resultPile, out resultIndex);
        x.PlayPosition = outerPlayPosition;
        x.LastCardPowerSourceSerial = outerLastSourceSerial;
        x.PlayIdentity = outerPlayIdentity;
        x.LastPlayIdentity = outerLastPlayIdentity;
    }

    private static void PlayFromPlayCore(ref BlitzExec x, BlitzCard card, byte target, int energyValue,
        int playPosition, bool isAutoPlay, out BlitzPile resultPile, out int resultIndex)
    {
        resultPile = BlitzPile.Play;
        resultIndex = -1;
        // The play-pile slot is fixed before the first effect runs (nested auto-plays append after
        // it), so OnPlay code can address its own live card with CurrentPlayCard.
        x.PlayPosition = playPosition;
        x.PlayIdentity = ++x.PlayIdentityCounter;
        BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
        x.CurrentPlayEnergyValue = energyValue;
        BlitzPile result = ResultLocation(x.B, x.R, card, stats, x.CurrentPlayIsHistoryCourseDupe,
            out bool removeFromCombat, out BlitzPilePosition basePosition);
        // CardModel.GetResultLocationForCardPlay clears ExhaustOnNextPlay when the result is Exhaust
        // (Havoc/Cascade force-exhaust the auto-played card).
        if (result == BlitzPile.Exhaust)
        {
            card.Flags &= ~BlitzCardFlags.ExhaustOnNextPlay;
            if (playPosition < x.B.PlayCount)
                BlitzPiles.At(ref x.B, BlitzPile.Play, playPosition).Flags &= ~BlitzCardFlags.ExhaustOnNextPlay;
        }
        // HookMirrors.ModifyCardPlayResultLocation, then CardModel.OnPlayWrapper's direct after dispatch
        // over the modifiers that changed the location.
        Span<BlitzListener> locationModifiers = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
        BlitzPile hooked = BlitzHooks.ModifyCardPlayResultLocation(ref x, card, isAutoPlay, energyValue, result,
            basePosition, locationModifiers, out int locationModifierCount, out BlitzPilePosition resultPosition);
        if (x.NeedsChoice)
            return;
        if (hooked != result)
        {
            if (removeFromCombat)
                throw new BlitzUnsupportedException("出牌去向修改把移出战斗的卡改成了牌堆。");
            if (hooked == BlitzPile.Play)
                throw new BlitzUnsupportedException($"出牌去向修改返回了无效牌堆 {hooked}。");
            result = hooked;
        }
        if (resultPosition != BlitzPilePosition.Bottom && result is not (BlitzPile.Draw or BlitzPile.Discard or BlitzPile.Hand))
            throw new BlitzUnsupportedException($"出牌去向位置对 {result} 无定义。");
        BlitzHooks.AfterModifyingCardPlayResultLocation(ref x, card, result, locationModifiers[..locationModifierCount]);
        if (x.NeedsChoice)
            return;
        // HookMirrors.ModifyCardPlayCount then Hook.AfterModifyingCardPlayCount. The base count is
        // CardModel.GetEnchantedReplayCount() (the enchantment modifies the replay count first).
        int basePlayCount = EnchantedReplayCount(card);
        Span<BlitzListener> playCountModifiers = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
        int playCount = BlitzHooks.ModifyCardPlayCount(ref x, card, target, basePlayCount + 1,
            playCountModifiers, out int playCountModifierCount);
        BlitzHooks.AfterModifyingCardPlayCount(ref x, card, playCountModifiers[..playCountModifierCount]);
        if (x.NeedsChoice)
            return;
        if (x.B.Player.IsDead)
            return;

        // Per-play damage history used by Fisticuffs/Omnislice/HandOfGreed; never part of the state.
        // The existing engine captures a fresh history start for every CardPlay of a replay/Echo Form
        // series, so a compensation on a later play must not see an earlier play's damage.
        x.EffectDepth++;
        BlitzGeneratedChoiceOptions savedOptionCards = x.GeneratedOptions;
        int savedGeneratedOptions = x.GeneratedOptionCount;
        bool completed = true;
        for (int index = 0; index < playCount; index++)
        {
            if (IsOverOrEnding(x.B))
                break;
            x.CardDamageTotal = 0;
            x.CardKilledTarget = false;
            x.PlayIndex = index;
            BlitzCardPlayInfo play = new(card, target, index, playCount, removeFromCombat ? BlitzPile.Play : result,
                BlitzPiles.Start(x.B, BlitzPile.Play) + playPosition);
            BeforeCardPlayedHook before = new() { Play = play };
            BlitzHooks.Dispatch(ref x, ref before, gatedByEnding: true);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            RecordCardPlayStarted(ref x.B, stats, BlitzRegistry.Card(card.Id).CardType(x.B, x.R, card),
                index, energyValue, isAutoPlay);
            bool identityScope = x.R.HasIdentityContent;
            int monologueBase = 0;
            int outerSourceSerial = 0;
            bool outerSourceActive = false;
            int outerMonologueBase = 0;
            bool outerMonologuesResolved = false;
            if (identityScope)
            {
                monologueBase = x.MonologuePendingCount;
                CaptureMonologuePending(ref x);
                outerSourceSerial = x.CardPowerSourceSerial;
                outerSourceActive = x.CardPowerSourceActive;
                outerMonologueBase = x.CurrentMonologueBase;
                outerMonologuesResolved = x.MonologuesResolved;
                x.CurrentMonologueBase = monologueBase;
                x.MonologuesResolved = false;
                x.CardPowerSourceSerial = ++x.CardPowerSourceSerialCounter;
                x.CardPowerSourceActive = true;
            }
            BlitzRegistry.Card(card.Id).OnPlay(ref x);
            if (!x.NeedsChoice && identityScope)
            {
                if (!x.MonologuesResolved)
                {
                    ResolveMonologuePending(ref x, monologueBase);
                    x.MonologuesResolved = true;
                }
                AfterCardPlayPowerLifecycle(ref x);
            }
            if (x.NeedsChoice)
            {
                if (identityScope)
                {
                    CompleteCardPowerSource(ref x);
                    x.CardPowerSourceSerial = outerSourceSerial;
                    x.CardPowerSourceActive = outerSourceActive;
                    x.CurrentMonologueBase = outerMonologueBase;
                    x.MonologuesResolved = outerMonologuesResolved;
                }
                completed = false;
                break;
            }
            // CorePowerSupport.ApplyCardPowers resolves queued power amount changes after the card's
            // compensation effects (Vicious draws here, not inside the effect that applied Vulnerable).
            ResolvePowerAmountChanges(ref x);
            if (x.NeedsChoice)
            {
                if (identityScope)
                {
                    CompleteCardPowerSource(ref x);
                    x.CardPowerSourceSerial = outerSourceSerial;
                    x.CardPowerSourceActive = outerSourceActive;
                    x.CurrentMonologueBase = outerMonologueBase;
                    x.MonologuesResolved = outerMonologuesResolved;
                }
                completed = false;
                break;
            }
            // ICombatPredictionCardExecutionSink.ApplyCardPlayEffects settles newly dead enemies right
            // after the card's Power compensation (before the card's own choice and tail). An auto-play
            // that kills the Phrog Parasite must spawn its Wrigglers before the next queued auto-play
            // picks a target.
            ApplyEnemyDeathPowers(ref x);
            if (!x.NeedsChoice)
                ApplyBlockBreakCompensations(ref x);
            if (x.NeedsChoice)
            {
                if (identityScope)
                {
                    CompleteCardPowerSource(ref x);
                    x.CardPowerSourceSerial = outerSourceSerial;
                    x.CardPowerSourceActive = outerSourceActive;
                    x.CurrentMonologueBase = outerMonologueBase;
                    x.MonologuesResolved = outerMonologuesResolved;
                }
                completed = false;
                break;
            }
            // The existing engine normalizes card afflictions at the end of the compensated card
            // effects, before the card's own choice.
            NormalizeCardAfflictions(ref x);
            // The card power-application scope ends after ApplyCardPlayEffects, before OwnChoice
            // (CardExecutionContinuation's using block).
            if (identityScope)
            {
                CompleteCardPowerSource(ref x);
                x.CardPowerSourceSerial = outerSourceSerial;
                x.CardPowerSourceActive = outerSourceActive;
                x.CurrentMonologueBase = outerMonologueBase;
                x.MonologuesResolved = outerMonologuesResolved;
            }
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            BlitzRegistry.Card(card.Id).OwnChoice(ref x);
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
                play = new BlitzCardPlayInfo(BlitzPiles.At(ref x.B, BlitzPile.Play, playPosition), target, index,
                    playCount, removeFromCombat ? BlitzPile.Play : result,
                    BlitzPiles.Start(x.B, BlitzPile.Play) + playPosition);
            }
            // CardPlayFinished: no tracked counter yet. AfterCardPlayed runs its normal pass, then
            // re-reads the listeners for the late pass, without the ending gate. The native history
            // records the finished play before AfterCardPlayed, so effects triggered there (Calamity
            // generating a card, Stomp.AfterEnteredCombat counting attacks) must see this play.
            RecordCardPlayFinished(ref x, stats, BlitzRegistry.Card(card.Id).CardType(x.B, x.R, card));
            x.LastPlayIdentity = x.PlayIdentity;
            AfterCardPlayedHook after = new() { Play = play };
            BlitzHooks.Dispatch(ref x, ref after, gatedByEnding: false);
            if (x.NeedsChoice)
            {
                completed = false;
                break;
            }
            AfterCardPlayedLateHook afterLate = new() { Play = play };
            BlitzHooks.Dispatch(ref x, ref afterLate, gatedByEnding: false);
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
            BlitzCard completedCard = (uint)playPosition < (uint)x.B.PlayCount
                ? BlitzPiles.At(ref x.B, BlitzPile.Play, playPosition)
                : x.Card;
            RecordCardPlayed(ref x, stats, BlitzRegistry.Card(card.Id).CardType(x.B, x.R, card),
                completedCard);
            // SimulatedCombatState.RecordCardLifecycle: the completed play records the exact Fetch
            // object in _fetchCardsPlayedThisTurn. The value still sits in the play pile here.
            if ((uint)playPosition < (uint)x.B.PlayCount
                && BlitzRegistry.Card(card.Id).TracksFetchPlay)
            {
                BlitzPiles.At(ref x.B, BlitzPile.Play, playPosition).Private |= FetchPlayedBit;
            }
            // CardPlayFinishedEntry.WasEthereal is `card.HasKeyword(State, Ethereal)`, which runs
            // Hook.ModifyKeywords: a Hexed card counts as Ethereal while HexPower is up, so the
            // battle-aware helper (local keyword plus Hexed+HexPower) is the source.
            if ((uint)playPosition < (uint)x.B.PlayCount
                && HasKeyword(x.B, BlitzPiles.At(ref x.B, BlitzPile.Play, playPosition), BlitzKeywords.Ethereal))
            {
                x.B.AddCounter(BlitzCounter.EtherealPlays, 1);
            }
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
        // native cost cleanup touches only this card, so it runs before the move here. Content that
        // removes an earlier play-pile card (SummonForth moving a queued Sovereign Blade) shifts the
        // live slot; PlayPosition is the adjusted identity, while the captured local can be stale.
        int resultSlot = playPosition;
        if ((uint)x.PlayPosition < (uint)x.B.PlayCount
            && BlitzFingerprint.Equal(BlitzPiles.At(ref x.B, BlitzPile.Play, x.PlayPosition), x.Card))
        {
            resultSlot = x.PlayPosition;
        }
        if (resultSlot < x.B.PlayCount)
        {
            if (removeFromCombat)
            {
                BlitzPiles.RemoveAt(ref x.B, BlitzPile.Play, resultSlot);
            }
            else
            {
                AfterCardPlayedCleanup(ref BlitzPiles.At(ref x.B, BlitzPile.Play, resultSlot));
                if (result == BlitzPile.Exhaust)
                {
                    int exhaustCount = x.B.ExhaustCount;
                    Exhaust(ref x, BlitzPile.Play, resultSlot);
                    resultPile = BlitzPile.Exhaust;
                    resultIndex = exhaustCount;
                }
                else if (MoveCard(ref x.B, BlitzPile.Play, resultSlot, result, resultPosition,
                             out BlitzPile movedPile, out int movedIndex))
                {
                    resultPile = movedPile;
                    resultIndex = movedIndex;
                }
            }
        }
        // CombatManager.CheckForEmptyHand runs after the result-pile move. The existing engine
        // special-cases UnceasingTop in SimulatedCombatState.TriggerRelicsAfterHandEmptied (there is no
        // generic AfterHandEmptied listener), so the kernel does the same after the outermost effect.
        AfterHandEmptied(ref x);
    }

    /// <summary>
    /// <c>SimulatedCombatState.TriggerRelicsAfterHandEmptied</c>: with an empty hand, a live
    /// <c>UnceasingTop</c> and a non-empty draw or discard pile, draw one card. The existing engine
    /// invokes this from the card/potion completion path only when no card or potion effect is running.
    /// </summary>
    private static void AfterHandEmptied(ref BlitzExec x)
    {
        if (!IsInProgress(x.B) || x.EffectDepth != 0)
            return;
        if (x.B.Phase is not (BlitzPlayerPhase.AutoPrePlay or BlitzPlayerPhase.Play or BlitzPlayerPhase.AutoPostPlay))
            return;
        if (x.B.HandCount != 0 || (x.B.DrawCount == 0 && x.B.DiscardCount == 0))
            return;
        if (!BlitzRegistry.TryRelic("UNCEASING_TOP", out ushort top))
            return;
        bool hasTop = false;
        for (int index = 0; index < x.B.RelicCount; index++)
        {
            ref readonly BlitzRelic relic = ref x.B.Relics[index];
            if (relic.Id == top && (relic.Flags & BlitzRelicFlags.Melted) == 0)
            {
                hasTop = true;
                break;
            }
        }
        if (!hasTop)
            return;
        Draw(ref x, 1, fromHandDraw: false);
    }

    /// <summary>Mirrors <c>SimulatedCombatState.RecordCardPlayStarted</c> for a manual or auto play.</summary>
    private static void RecordCardPlayStarted(ref BlitzBattle battle, BlitzCardStats stats,
        BlitzCardType cardType, int playIndex, int energyValue, bool isAutoPlay)
    {
        battle.AddCounter(BlitzCounter.CardPlayStartsThisTurn, 1);
        if (cardType == BlitzCardType.Attack)
            battle.AddCounter(BlitzCounter.AttackPlayStartsThisTurn, 1);
        if (cardType is BlitzCardType.Attack or BlitzCardType.Skill)
            battle.AddCounter(BlitzCounter.AttackSkillStartsThisTurn, 1);
        if (cardType == BlitzCardType.Attack && energyValue == 0)
            battle.AddCounter(BlitzCounter.ZeroCostAttackStartsThisTurn, 1);
        if (playIndex != 0)
            return;
        battle.AddCounter(BlitzCounter.CardPlaySeriesStartedThisTurn, 1);
        if (!isAutoPlay)
            battle.AddCounter(BlitzCounter.ManualCardsPlayedThisTurn, 1);
    }

    /// <summary>
    /// <c>CardModel.GetEnchantedReplayCount</c>: the base replay count after the enchantment's
    /// <c>EnchantPlayCount</c>. Pure; the evaluation and search read it without mutating the card.
    /// </summary>
    public static int EnchantedReplayCount(in BlitzCard card)
    {
        int replayCount = card.ReplayCount;
        if (card.EnchantmentId != 0)
            replayCount = BlitzRegistry.Enchantment((ushort)(card.EnchantmentId - 1))
                .EnchantPlayCount(card, replayCount);
        return replayCount;
    }

    /// <summary>
    /// Mirrors <c>RecordCardPlayed</c> then <c>RecordCardLifecycle</c> in
    /// <c>SimulatedCombatState.CompleteCardPlayEffects</c>. Slow, Fetch, relic card counters and
    /// returning cards are not supported yet.
    /// </summary>
    private static void RecordCardPlayFinished(ref BlitzExec x, BlitzCardStats stats, BlitzCardType cardType)
    {
        ref BlitzBattle battle = ref x.B;
        if (cardType != BlitzCardType.Attack)
            return;
        battle.AddCounter(BlitzCounter.AttacksPlayedThisTurn, 1);
        if ((stats.Tags & BlitzCardTag.Shiv) != 0)
            battle.AddCounter(BlitzCounter.ShivsPlayedThisTurn, 1);
    }

    private static void RecordCardPlayed(ref BlitzExec x, BlitzCardStats stats, BlitzCardType cardType,
        in BlitzCard completedCard)
    {
        ref BlitzBattle battle = ref x.B;
        if (cardType == BlitzCardType.Attack)
        {
            // SimulatedCombatState.RecordHistoryCourseAttack: the last attack completed this turn,
            // skipping dupes (HistoryCourse's own copies). Recorded after the tail hooks, from the
            // live play-pile value, exactly like the existing engine's mutable CardModel reference.
            if (x.R.HasHistoryCourse && !x.CurrentPlayIsHistoryCourseDupe)
            {
                battle.HistoryCourseCard = completedCard;
                battle.Flags |= BlitzBattleFlags.HistoryCourseCardPresent;
            }
        }
        battle.AddCounter(BlitzCounter.CardsPlayedThisTurn, 1);
        // CardPlayFinished entry: CombatPredictionHistory records one per completed card play.
        battle.AddCounter(BlitzCounter.FinishedCardPlays, 1);
        if (cardType == BlitzCardType.Skill)
        {
            battle.AddCounter(BlitzCounter.SkillCardsPlayedThisTurn, 1);
            // SimulatedCombatState.RecordSkillPlayed's _skillsPlayedThisTurn membership.
            battle.SetCounter(BlitzCounter.SkillPlayedThisSideTurn, 1);
        }
    }

    /// <summary>
    /// The current card in the play pile while its tail runs. The play pile segment is located from
    /// the live counts because effects may have added or removed other cards.
    /// </summary>
    public static ref BlitzCard CurrentPlayCard(ref BlitzExec x)
    {
        if ((uint)x.PlayPosition >= (uint)x.B.PlayCount)
            throw new InvalidOperationException("当前没有正在打出的卡。");
        return ref BlitzPiles.At(ref x.B, BlitzPile.Play, x.PlayPosition);
    }

    /// <summary>Copies the live play-pile card back into <see cref="BlitzExec.Card"/> after a tail hook changed it.</summary>
    public static void SyncCurrentPlayCard(ref BlitzExec x)
    {
        x.Card = CurrentPlayCard(ref x);
        x.Stats = x.R.Card(x.Card.Id, x.Card.Upgrade);
    }

    /// <summary>
    /// <c>EnchantmentModel.OnPlay</c> then <c>AfflictionModel.OnPlay</c> for the card at
    /// <paramref name="playPosition"/>. Returns false when a choice interrupted the tail or the owner
    /// died, matching <c>CardTailContinuation</c>.
    /// </summary>
    private static bool RunCardAttachmentOnPlay(ref BlitzExec x, int playPosition, byte target)
    {
        if ((uint)playPosition >= (uint)x.B.PlayCount)
            return true;
        // Nested auto-plays overwrite BlitzExec.Card/Stats/Target; restore the outer play context so
        // the attachment hooks see this card and this target (CardModel/CardPlay are per-play objects
        // in the existing engine).
        ref BlitzCard played = ref BlitzPiles.At(ref x.B, BlitzPile.Play, playPosition);
        x.Card = played;
        x.Stats = x.R.Card(played.Id, played.Upgrade);
        x.Target = target;
        x.PlayPosition = playPosition;
        if (played.EnchantmentId != 0)
        {
            BlitzRegistry.Enchantment((ushort)(played.EnchantmentId - 1)).OnPlay(ref x, played, target);
            if (x.NeedsChoice || x.B.Player.IsDead)
                return false;
            played = ref BlitzPiles.At(ref x.B, BlitzPile.Play, playPosition);
        }
        if (played.AfflictionId != 0)
        {
            BlitzRegistry.Affliction((ushort)(played.AfflictionId - 1)).OnPlay(ref x, played, target);
            if (x.NeedsChoice || x.B.Player.IsDead)
                return false;
        }
        return true;
    }

    /// <summary>Mirrors <c>CardResultLocationMirrors.GetResultLocation</c> for the supported cards.</summary>
    private static BlitzPile ResultLocation(in BlitzBattle battle, BlitzRules rules, in BlitzCard card,
        BlitzCardStats stats, bool historyCourseDupe, out bool removeFromCombat,
        out BlitzPilePosition position)
    {
        removeFromCombat = false;
        position = BlitzPilePosition.Bottom;
        // CardModel.GetResultLocationForCardPlay: IsDupe returns PileType.None before the Power and
        // result-location-override branches. The card leaves combat after its play.
        if (historyCourseDupe)
        {
            removeFromCombat = true;
            return BlitzPile.Play;
        }
        if (BlitzRegistry.Card(card.Id).CardType(battle, rules, card) == BlitzCardType.Power)
        {
            removeFromCombat = true;
            return BlitzPile.Play;
        }
        BlitzPile baseLocation = (card.Flags & BlitzCardFlags.ExhaustOnNextPlay) != 0
            || HasKeyword(battle, card, BlitzKeywords.Exhaust)
                ? BlitzPile.Exhaust
                : BlitzPile.Discard;
        // CardModel.GetResultLocationForCardPlay override (ShiningStrike, ParticleWall).
        return BlitzRegistry.Card(card.Id).ResultLocation(battle, rules, card, baseLocation, out position);
    }

    /// <summary>
    /// Appends one native <c>LocalCostModifier</c> to a card value
    /// (<c>CardEnergyCost.AddThisCombat</c>/<c>AddThisTurn</c>/<c>SetThisCombat</c>).
    /// </summary>
    public static void AddEnergyCostModifier(ref BlitzCard card, int amount, BlitzCostType type,
        BlitzCostExpiration expiration, bool reduceOnly)
    {
        // CardEnergyCost.SetThisCombat adds an absolute 0 modifier for a non-negative canonical cost;
        // AddThisCombat/AddUntilPlayed ignore zero.
        if (amount == 0 && type != BlitzCostType.Absolute)
            return;
        if (card.EnergyModifierCount >= BlitzLimits.CardEnergyCostModifiers)
            throw new BlitzCapacityException("Card.EnergyModifiers", BlitzLimits.CardEnergyCostModifiers);
        card.EnergyModifiers[card.EnergyModifierCount] = BlitzCostModifier.Create(amount, type, expiration, reduceOnly);
        card.EnergyModifierCount++;
    }

    /// <summary>Mirrors <c>CardEnergyCost.AfterCardPlayedCleanup</c> and the temporary star cost cleanup.</summary>
    private static void AfterCardPlayedCleanup(ref BlitzCard card)
    {
        int kept = 0;
        for (int index = 0; index < card.EnergyModifierCount; index++)
        {
            BlitzCostModifier modifier = card.EnergyModifiers[index];
            // CardEnergyCost.AfterCardPlayedCleanup: Expiration.HasFlag(WhenPlayed), so EndOfTurn | WhenPlayed goes too.
            if (((byte)modifier.Expiration & (byte)BlitzCostExpiration.WhenPlayed) != 0)
                continue;
            card.EnergyModifiers[kept++] = modifier;
        }
        for (int index = kept; index < BlitzLimits.CardEnergyCostModifiers; index++)
            card.EnergyModifiers[index] = default;
        card.EnergyModifierCount = kept;
        if (card.HasTemporaryStarCost)
            card.RemoveTemporaryStarCosts(atTurnEnd: false);
    }

    /// <summary>
    /// <c>CorePowerSupport.CapturePendingMonologues</c>: every positive MonologuePower instance on the
    /// player, in table (listener) order. Captured before the card's effects; resolved afterwards.
    /// </summary>
    private static void CaptureMonologuePending(ref BlitzExec x)
    {
        if (!x.R.HasIdentityContent)
            return;
        if (!BlitzRegistry.TryPower("MONOLOGUE_POWER", out ushort id))
            return;
        byte occurrence = 0;
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly BlitzPower power = ref x.B.Powers[index];
            if (power.Id != id || power.Owner != BlitzLimits.PlayerOwner)
                continue;
            if (power.Amount > 0)
                x.PushMonologuePending(occurrence, power.Aux0);
            occurrence++;
        }
    }

    /// <summary>
    /// Resolves the current card's pending Monologue Strength before the first power application when
    /// the card's compensation is the engine's post-Monologue switch. Cards whose powers are applied in
    /// CardPowerOnPlaySupport keep the default (resolve after OnPlay).
    /// </summary>
    internal static void TryResolveMonologuesBeforeCompensation(ref BlitzExec x)
    {
        if (x.MonologuesResolved
            || x.CurrentMonologueBase >= x.MonologuePendingCount
            || x.Card.Id == 0
            || !BlitzRegistry.Card(x.Card.Id).ResolvesMonologuesBeforeCompensation)
        {
            return;
        }
        x.MonologuesResolved = true;
        ResolveMonologuePending(ref x, x.CurrentMonologueBase);
    }

    /// <summary>
    /// <c>SimulatedCombatState.ResolveMonologues</c>: apply the captured Strength and accumulate the
    /// instance's StrengthApplied. The Strength application is not gated on the power still existing
    /// (the existing engine applies it to the captured model even after a removal).
    /// </summary>
    private static void ResolveMonologuePending(ref BlitzExec x, int baseCount)
    {
        if (x.MonologuePendingCount <= baseCount)
            return;
        if (!BlitzRegistry.TryPower("MONOLOGUE_POWER", out ushort monologue)
            || !BlitzRegistry.TryPower("STRENGTH_POWER", out ushort strength))
        {
            throw new BlitzUnsupportedException("Monologue 结算需要 MONOLOGUE_POWER 与 STRENGTH_POWER。");
        }
        for (int entryIndex = baseCount; entryIndex < x.MonologuePendingCount; entryIndex++)
        {
            BlitzMonologuePending entry = x.MonologuePending[entryIndex];
            ApplyPower(ref x, BlitzLimits.PlayerOwner, strength, entry.Strength, BlitzLimits.PlayerOwner);
            if (x.NeedsChoice)
                break;
            int row = FindPowerOccurrence(x.B, BlitzLimits.PlayerOwner, monologue, entry.Occurrence);
            if (row >= 0)
                x.B.Powers[row].Aux1 = (int)Math.Clamp((long)x.B.Powers[row].Aux1 + entry.Strength,
                    int.MinValue, int.MaxValue);
        }
        x.MonologuePendingCount = baseCount;
    }

    /// <summary>Marks the end of the current card power-application scope (UnsettlingLamp latch).</summary>
    internal static void CompleteCardPowerSource(ref BlitzExec x)
    {
        x.LastCardPowerSourceSerial = x.CardPowerSourceSerial;
        if (x.LampTriggerSerial == 0 || x.LampTriggerSerial != x.CardPowerSourceSerial)
            return;
        if (BlitzRegistry.TryRelic("UNSETTLING_LAMP", out ushort lamp))
        {
            for (int index = 0; index < x.B.RelicCount; index++)
            {
                if (x.B.Relics[index].Id == lamp && x.B.Relics[index].Counter == 0)
                    x.B.Relics[index].Counter = 1;
            }
        }
        x.LampTriggerSerial = 0;
        x.LampInternalTypeCount = 0;
    }

    /// <summary>
    /// <c>PowerLifecycleSupport.AfterCardPlayed</c>'s PaleBlueDot branch: every positive instance on the
    /// player, when the fifth card of the turn has just been played, latches and grants next-turn draws.
    /// </summary>
    private static void AfterCardPlayPowerLifecycle(ref BlitzExec x)
    {
        if (!x.R.HasIdentityContent)
            return;
        if (!BlitzRegistry.TryPower("PALE_BLUE_DOT_POWER", out ushort paleBlueDot)
            || !BlitzRegistry.TryPower("DRAW_CARDS_NEXT_TURN_POWER", out ushort drawNextTurn))
            return;
        int cardsPlayed = x.B.Counter(BlitzCounter.CardsPlayedThisTurn);
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref BlitzPower power = ref x.B.Powers[index];
            if (power.Id != paleBlueDot || power.Owner != BlitzLimits.PlayerOwner || power.Amount <= 0)
                continue;
            if (power.Aux0 != 0 || cardsPlayed + 1 < 5)
                continue;
            power.Aux0 = 1;
            ApplyPower(ref x, BlitzLimits.PlayerOwner, drawNextTurn, power.Amount, BlitzLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// <c>SimulatedCombatState.ApplyPowerFromSource</c> with a non-null card source outside the normal
    /// OnPlay scope (Inky's enchantment OnPlay). Opens and completes a one-application source scope.
    /// </summary>
    public static int ApplyPowerFromCard(ref BlitzExec x, byte target, ushort powerId, int amount, byte applier,
        in BlitzCard card)
    {
        _ = card;
        int outerSerial = x.CardPowerSourceSerial;
        bool outerActive = x.CardPowerSourceActive;
        x.CardPowerSourceSerial = ++x.CardPowerSourceSerialCounter;
        x.CardPowerSourceActive = true;
        int applied = ApplyPower(ref x, target, powerId, amount, applier);
        CompleteCardPowerSource(ref x);
        x.CardPowerSourceSerial = outerSerial;
        x.CardPowerSourceActive = outerActive;
        return applied;
    }
}
