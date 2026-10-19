namespace CombatSolver.Engine.InCombat.Flash;

public static partial class FlashKernel
{
    private const int BaseHandDrawCount = 5;

    /// <summary>
    /// Mirrors <c>CombatBeamSolver.AdvanceRound</c>: player turn end, the enemy turn, and the next
    /// player turn start. Branches for unsupported powers and relics are omitted; admission rejects
    /// roots that could reach them.
    /// </summary>
    public static void AdvanceRound(ref FlashExec x)
    {
        if (!IsInProgress(x.B))
            return;

        x.ChoiceTiming = FlashChoiceTiming.PlayerTurnEnd;
        // PlayerTurnEndLifecycle.RunPhaseOne.
        x.B.Phase = FlashPlayerPhase.End;
        // EndTurnPowerSupport.TriggerVeryEarly: no supported sources.
        RelicPass(ref x, RelicPhase.PrepareBeforeSideTurnEnd, FlashSide.Player);
        if (x.NeedsChoice)
            return;
        // SimulateEndPlayerTurnBeforeOrbPassives.
        x.B.Phase = FlashPlayerPhase.AutoPostPlay;
        AfterAutoPostPlayPhaseEnteredHook afterAutoPostPlay = new();
        FlashHooks.Dispatch(ref x, ref afterAutoPostPlay, gatedByEnding: true);
        if (x.NeedsChoice)
            return;
        x.B.Phase = FlashPlayerPhase.End;
        BeforeSideTurnEnd(ref x, FlashSide.Player, ParticipantMask(FlashLimits.PlayerOwner));
        if (x.NeedsChoice)
            return;
        CheckWinCondition(ref x.B);
        if (!IsOverOrEnding(x.B))
        {
            // OrbQueue.BeforeTurnEnd: every orb in the queue snapshot, each passive resolving its
            // pending enemy deaths; a passive that ends the combat skips the turn-end hand effects.
            TriggerBeforeTurnEndOrbs(ref x);
            if (x.NeedsChoice)
                return;
            if (!IsOverOrEnding(x.B))
            {
                DoTurnEnd(ref x);
                if (x.NeedsChoice)
                    return;
                CheckWinCondition(ref x.B);
            }
            CompletePlayerEarlySideTurnEnd(ref x);
            if (!x.NeedsChoice)
                NormalizeCardAfflictions(ref x);
        }
        ApplyEnemyDeathPowers(ref x);
        if (!IsInProgress(x.B))
            return;
        FlushPlayerHandAtTurnEnd(ref x);
        int turnEndShuffleEvents = x.B.ShuffleEventCount;
        // PlayerTurnEndLifecycle.RunPhaseTwo.
        PlayerTurnEndPhaseTwo(ref x);
        if (x.NeedsChoice)
            return;
        x.B.Phase = FlashPlayerPhase.None;
        x.B.ShufflesCrossed += x.B.ShuffleEventCount - turnEndShuffleEvents;
        ApplyEnemyDeathPowers(ref x);

        // ShouldTakeExtraTurn: no supported sources.
        x.ChoiceTiming = FlashChoiceTiming.EnemyTurn;
        RunEnemyTurn(ref x);
        // The performed-move marks only carry information within one enemy turn. The
        // forced-must-perform mark is the same kind of in-turn kernel state
        // (SimulatedCombatState.MonsterAi.cs:122-126): PrepareMonsterMovesForNextRound consumes it when
        // the turn reaches that pass, but an early return (the player died during the enemy turn or a
        // choice was requested) skips the pass, and it must still be zero at every stable boundary.
        for (int index = 0; index < x.B.EnemyCount; index++)
        {
            x.B.Enemies[index].Flags &= ~(FlashEnemyFlags.PerformedMoveThisRound | FlashEnemyFlags.ForcedMustPerformAfterMove);
        }
        if (x.NeedsChoice || !IsInProgress(x.B))
            return;
        AdvanceRoundPlayerStart(ref x);
    }

    /// <summary>Mirrors the enemy-turn block of <c>CombatBeamSolver.AdvanceRound</c>.</summary>
    private static void RunEnemyTurn(ref FlashExec x)
    {
        Span<byte> acting = stackalloc byte[FlashLimits.Enemies];
        int actingCount = x.B.RosterCount;
        for (int position = 0; position < actingCount; position++)
            acting[position] = EnemyOwner(x.B.Roster[position]);

        SetSide(ref x.B, FlashSide.Enemy);
        // BeginSideTurn for the enemies: their per-turn counters are not tracked.
        for (int position = 0; position < x.B.RosterCount; position++)
            SnapshotPowerAmountsAtTurnStart(ref x.B, EnemyOwner(x.B.Roster[position]));
        // Each phase reads the live enemy list, as the existing engine passes simulatedCombat.Enemies.
        Span<byte> participants = stackalloc byte[FlashLimits.Enemies];
        BeforeSideTurnStart(ref x, FlashSide.Enemy, participants[..RosterOwners(x.B, participants)]);
        if (x.NeedsChoice)
            return;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            ClearBlockAtSideStart(ref x, EnemyOwner(x.B.Roster[position]));
            if (x.NeedsChoice)
                return;
        }
        TriggerSideTurnStart(ref x, FlashSide.Enemy, participants[..RosterOwners(x.B, participants)], decrementPlating: x.B.Round > 1);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.TriggerPoison over the current enemies, before the deferred death pass.
        if (!TriggerPoison(ref x, participants[..RosterOwners(x.B, participants)]))
            return;
        ApplyEnemyDeathPowers(ref x);
        if (CheckWinCondition(ref x.B))
            return;

        for (int index = 0; index < actingCount; index++)
        {
            byte owner = acting[index];
            ref FlashEnemy enemy = ref x.B.Enemy(owner);
            if (!CanPerformMonsterMove(enemy))
                continue;
            if ((enemy.Flags & FlashEnemyFlags.SkipNextMove) != 0)
            {
                enemy.Flags &= ~FlashEnemyFlags.SkipNextMove;
                enemy.Flags |= FlashEnemyFlags.PerformedMoveThisRound;
                if (CheckWinCondition(ref x.B))
                    return;
                continue;
            }
            // Forced moves (Steam Eruption) are not supported.
            bool playerDied = ExecuteMonsterMove(ref x, owner);
            x.B.Enemy(owner).Flags |= FlashEnemyFlags.PerformedMoveThisRound;
            if (x.NeedsChoice)
                return;
            if (CheckWinCondition(ref x.B) || playerDied)
                return;
        }

        TriggerEnemySideTurnEndEffects(ref x, participants[..RosterOwners(x.B, participants)]);
        if (x.NeedsChoice)
            return;
        ApplyEnemyDeathPowers(ref x);
        // CorePowerSupport.TriggerPoison over the player, then ClearNoDraw (no supported sources).
        Span<byte> players = stackalloc byte[1];
        players[0] = FlashLimits.PlayerOwner;
        if (!TriggerPoison(ref x, players))
            return;
        if (!x.NeedsChoice)
            NormalizeCardAfflictions(ref x);
        if (CheckWinCondition(ref x.B))
            return;
        PrepareMonsterMovesForNextRound(ref x);
    }

    /// <summary>Mirrors <c>HookMirrors.BeforeSideTurnEnd</c>: the VeryEarly, Early and normal passes.</summary>
    private static void BeforeSideTurnEnd(ref FlashExec x, FlashSide side, ushort participants)
    {
        BeforeSideTurnEndVeryEarlyHook veryEarly = new() { Side = side, Participants = participants };
        FlashHooks.Dispatch(ref x, ref veryEarly, gatedByEnding: true);
        if (x.NeedsChoice)
            return;
        BeforeSideTurnEndEarlyHook early = new() { Side = side, Participants = participants };
        FlashHooks.Dispatch(ref x, ref early, gatedByEnding: true);
        if (x.NeedsChoice)
            return;
        BeforeSideTurnEndHook normal = new() { Side = side, Participants = participants };
        FlashHooks.Dispatch(ref x, ref normal, gatedByEnding: true);
    }

    public static ushort ParticipantMask(byte owner) => (ushort)(1 << owner);

    /// <summary>Whether <paramref name="owner"/> is in a participant bit set.</summary>
    public static bool Participates(ushort participants, byte owner) => (participants & (1 << owner)) != 0;

    /// <summary><c>SimulatedCombatState.CurrentSide</c> setter: a side change clears the lost-HP marks.</summary>
    private static void SetSide(ref FlashBattle battle, FlashSide side)
    {
        if (battle.Side != side)
            ClearLostHpThisTurn(ref battle);
        battle.Side = side;
    }

    private static void ClearLostHpThisTurn(ref FlashBattle battle)
    {
        battle.Player.Flags &= ~FlashCreatureFlags.LostHpThisTurn;
        battle.Osty.Flags &= ~FlashCreatureFlags.LostHpThisTurn;
        for (int index = 0; index < battle.EnemyCount; index++)
            battle.Enemies[index].Creature.Flags &= ~FlashCreatureFlags.LostHpThisTurn;
    }

    /// <summary><c>SimulatedCombatState.BeginSideTurn</c> for the player: resets the per-turn counters.</summary>
    private static void BeginPlayerSideTurn(ref FlashBattle battle)
    {
        for (FlashCounter counter = FlashCounter.CardsPlayedThisTurn; counter <= FlashCounter.EnergySpentThisTurn; counter++)
            battle.SetCounter(counter, 0);
    }

    /// <summary>Owners of the roster enemies in roster order.</summary>
    private static int RosterOwners(in FlashBattle battle, Span<byte> owners)
    {
        for (int position = 0; position < battle.RosterCount; position++)
            owners[position] = EnemyOwner(battle.Roster[position]);
        return battle.RosterCount;
    }

    /// <summary>
    /// Mirrors <c>CorePowerSupport.TickDurations</c>: Weak, Vulnerable, Frail and Intangible, then NoBlock,
    /// for every creature. Flash marks those powers with <see cref="FlashPowerDef.TicksAtEnemySideEnd"/>.
    /// </summary>
    private static void TickDurations(ref FlashBattle battle)
    {
        Span<byte> creatures = stackalloc byte[FlashLimits.Enemies + 2];
        int count = 0;
        creatures[count++] = FlashLimits.PlayerOwner;
        if ((battle.Flags & FlashBattleFlags.OstyPresent) != 0)
            creatures[count++] = FlashLimits.OstyOwner;
        for (int position = 0; position < battle.RosterCount; position++)
            creatures[count++] = EnemyOwner(battle.Roster[position]);

        Span<ushort> ticking = stackalloc ushort[FlashLimits.Powers];
        for (int index = 0; index < count; index++)
        {
            byte owner = creatures[index];
            int tickingCount = 0;
            for (int power = 0; power < battle.PowerCount; power++)
            {
                if (battle.Powers[power].Owner == owner && FlashRegistry.Power(battle.Powers[power].Id).TicksAtEnemySideEnd)
                    ticking[tickingCount++] = battle.Powers[power].Id;
            }
            for (int tick = 0; tick < tickingCount; tick++)
                TickDuration(ref battle, owner, ticking[tick]);
        }
    }

    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.DoTurnEnd</c>: the hand is split once into turn-end-in-hand cards
    /// and the remaining Ethereal cards (<c>ShouldEtherealTrigger</c> has no supported listeners); the
    /// Ethereal ones exhaust first, then <c>DoTurnEndCards</c> moves each turn-end card to the play pile,
    /// runs its effect, and exhausts it when Ethereal (without the hook check) or discards it. Cards are
    /// located again before each step, since exhaust and effect hooks may change the hand in between.
    /// </summary>
    private static void DoTurnEnd(ref FlashExec x)
    {
        if (IsOverOrEnding(x.B))
            return;
        int handCount = x.B.HandCount;
        Span<int> ethereal = stackalloc int[handCount];
        Span<int> turnEnd = stackalloc int[handCount];
        int etherealCount = 0, turnEndCount = 0;
        for (int index = 0; index < handCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            if (FlashRegistry.Card(card.Id).HasTurnEndInHandEffect)
                turnEnd[turnEndCount++] = index;
            else if (HasKeyword(x.B, card, FlashKeywords.Ethereal))
                ethereal[etherealCount++] = index;
        }
        Span<SelectedCard> etherealCards = stackalloc SelectedCard[etherealCount];
        SnapshotSelected(x.B, FlashPile.Hand, ethereal[..etherealCount], etherealCards);
        Span<SelectedCard> turnEndCards = stackalloc SelectedCard[turnEndCount];
        SnapshotSelected(x.B, FlashPile.Hand, turnEnd[..turnEndCount], turnEndCards);
        for (int item = 0; item < etherealCount; item++)
        {
            Exhaust(ref x, FlashPile.Hand, LocateSelected(x.B, FlashPile.Hand, etherealCards, item), causedByEthereal: true);
            if (x.NeedsChoice)
                return;
        }
        for (int item = 0; item < turnEndCount; item++)
        {
            int index = LocateSelected(x.B, FlashPile.Hand, turnEndCards, item);
            FlashCard card = turnEndCards[item].Card;
            MoveCard(ref x.B, FlashPile.Hand, index, FlashPile.Play);
            x.Card = card;
            x.Stats = x.R.Card(card.Id, card.Upgrade);
            x.Target = FlashLimits.NoOwner;
            FlashRegistry.Card(card.Id).OnTurnEndInHand(ref x);
            if (x.NeedsChoice)
                return;
            // The effect may change the card's own state (a private counter), never its identity or place.
            int position = x.B.PlayCount - 1;
            if (position < 0 || FlashPiles.At(ref x.B, FlashPile.Play, position).Id != card.Id
                || FlashPiles.At(ref x.B, FlashPile.Play, position).Upgrade != card.Upgrade)
                throw new FlashUnsupportedException("回合末在手效果移动了打出区的卡。");
            if (HasKeyword(x.B, FlashPiles.At(ref x.B, FlashPile.Play, position), FlashKeywords.Ethereal))
            {
                Exhaust(ref x, FlashPile.Play, position, causedByEthereal: true);
                if (x.NeedsChoice)
                    return;
            }
            else
            {
                MoveCard(ref x.B, FlashPile.Play, position, FlashPile.Discard);
            }
        }
    }

    /// <summary>Mirrors <c>CorePowerSupport.FlushPlayerHandAtTurnEnd</c>.</summary>
    private static void FlushPlayerHandAtTurnEnd(ref FlashExec x)
    {
        // BeforeFlush enchantments: not supported. A refused flush keeps the whole hand.
        bool flush = ShouldFlush(ref x);
        for (int index = 0; flush && index < x.B.HandCount;)
        {
            ref FlashCard card = ref x.B.Cards[index];
            bool retain = HasKeyword(x.B, card, FlashKeywords.Retain) || (card.Flags & FlashCardFlags.RetainThisTurn) != 0;
            if (retain)
            {
                index++;
                continue;
            }
            if (IsEnding(x.B) || x.B.Player.IsDead)
                break;
            FlashCard flushed = FlashPiles.RemoveAt(ref x.B, FlashPile.Hand, index);
            FlashPiles.Append(ref x.B, FlashPile.Discard, flushed);
        }
        // Bookmark is not supported. End-of-turn cleanup for every combat card:
        int total = x.B.CardCount;
        for (int position = 0; position < total; position++)
            EndOfTurnCleanup(ref x.B.Cards[position]);
    }

    /// <summary>Mirrors <c>PersistentRelicSupport.ShouldFlush</c>: every combat listener, without the ending gate.</summary>
    private static bool ShouldFlush(ref FlashExec x)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.ShouldFlush) == 0)
            return true;
        Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        int count = FlashHooks.CollectListeners(x.B, FlashHookMask.ShouldFlush, listeners);
        for (int index = 0; index < count; index++)
        {
            if (!FlashHooks.Definition(x.B, listeners[index]).ShouldFlush(ref x, listeners[index]))
                return false;
        }
        return true;
    }

    /// <summary>Mirrors <c>CardModel.EndOfTurnCleanup</c>.</summary>
    private static void EndOfTurnCleanup(ref FlashCard card)
    {
        card.Flags &= ~(FlashCardFlags.ExhaustOnNextPlay | FlashCardFlags.RetainThisTurn | FlashCardFlags.SlyThisTurn);
        int kept = 0;
        for (int index = 0; index < card.EnergyModifierCount; index++)
        {
            FlashCostModifier modifier = card.EnergyModifiers[index];
            if (((byte)modifier.Expiration & (byte)FlashCostExpiration.EndOfTurn) != 0)
                continue;
            card.EnergyModifiers[kept++] = modifier;
        }
        for (int index = kept; index < FlashLimits.CardEnergyCostModifiers; index++)
            card.EnergyModifiers[index] = default;
        card.EnergyModifierCount = kept;
        if (card.HasTemporaryStarCost && card.TemporaryStarCostClearsAtTurnEnd)
            card.ClearTemporaryStarCost();
    }

    /// <summary>Mirrors <c>SimulatedCombatState.SnapshotPowerAmountsAtTurnStart</c> for one creature.</summary>
    private static void SnapshotPowerAmountsAtTurnStart(ref FlashBattle battle, byte owner)
    {
        for (int index = 0; index < battle.PowerCount; index++)
        {
            if (battle.Powers[index].Owner == owner)
                battle.Powers[index].AmountOnTurnStart = battle.Powers[index].Amount;
        }
    }

    /// <summary>
    /// Mirrors <c>CombatBeamSolver.AdvanceRoundPlayerStart</c> and <c>ContinuePlayerStart</c> for a
    /// round without turn-start choices.
    /// </summary>
    private static void AdvanceRoundPlayerStart(ref FlashExec x)
    {
        x.ChoiceTiming = FlashChoiceTiming.PlayerTurnStart;
        SetSide(ref x.B, FlashSide.Player);
        x.B.Round++;
        x.B.PlayerTurn++;
        // RoundNumber and AdvancePlayerTurn also clear the lost-HP marks; BeginSideTurn(player):
        ClearLostHpThisTurn(ref x.B);
        BeginPlayerSideTurn(ref x.B);
        SnapshotPowerAmountsAtTurnStart(ref x.B, FlashLimits.PlayerOwner);
        ReadOnlySpan<byte> player = [FlashLimits.PlayerOwner];
        BeforeSideTurnStart(ref x, FlashSide.Player, player);
        if (x.NeedsChoice)
            return;
        ClearBlockAtSideStart(ref x, FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;

        // Energy reset: PersistentRelicSupport.ShouldPlayerResetEnergy over every combat listener
        // (Ice Cream keeps the previous turn's energy from turn 2 on). Modified max energy and energy
        // next turn follow.
        if (FlashHooks.ShouldPlayerResetEnergy(x.B, x.R, FlashLimits.PlayerOwner))
            x.B.Energy = 0;
        // CombatBeamSolver.RoundTransition: GainEnergy(modified max energy + ConsumeEnergyNextTurn).
        int energyNextTurn = ConsumeEnergyNextTurn(ref x.B);
        x.B.Energy = (int)Math.Clamp((long)x.B.Energy + Math.Max(0, ModifiedMaxEnergy(ref x)) + energyNextTurn,
            0, FlashLimits.ValueClamp);
        PowersAfterEnergyReset(ref x);
        if (x.NeedsChoice)
            return;
        RelicPass(ref x, RelicPhase.AfterEnergyReset, FlashSide.Player);
        if (x.NeedsChoice)
            return;
        RelicPass(ref x, RelicPhase.AfterEnergyResetLate, FlashSide.Player);
        if (x.NeedsChoice)
            return;

        // ContinuePlayerStart. A turn-start choice taken before the side starts would start it first
        // (TurnStartChoiceCursor.BeforeNextTake); turn-start choices are not supported yet.
        int beforeHandShuffles = x.B.ShuffleEventCount;
        x.B.Phase = FlashPlayerPhase.Start;
        BeforeHandDraw(ref x);
        if (x.NeedsChoice)
            return;
        // CombatBeamSolver.TurnExecutionContinuation resolves BeforeHandDraw's power changes before
        // the pre-draw checkpoint.
        ResolvePowerAmountChanges(ref x);
        if (x.NeedsChoice)
            return;
        x.B.ShufflesCrossed += x.B.ShuffleEventCount - beforeHandShuffles;
        // ConsumeModifiedHandDraw: base + the not-yet-snapshotted DrawCardsNextTurn amount, then the
        // listener passes (DrawCardsNextTurnPower.ModifyHandDraw adds its snapshotted amount).
        int drawCount = ModifiedHandDraw(ref x, BaseHandDrawCount + DrawNextTurnNotYetSnapshotted(x.B));
        int effectiveDraw = Math.Min(drawCount, x.B.MaxHandSize - x.B.HandCount);
        bool willShuffle = effectiveDraw > x.B.DrawCount && x.B.DiscardCount != 0;
        Draw(ref x, drawCount, fromHandDraw: true);
        if (willShuffle)
            x.B.ShufflesCrossed++;
        if (x.NeedsChoice)
            return;
        // Draw history compensation: no supported sources.
        AfterPlayerTurnStart(ref x);
        if (x.NeedsChoice)
            return;
        TriggerSideTurnStart(ref x, FlashSide.Player, player, decrementPlating: x.B.PlayerTurn != 1);
        if (x.NeedsChoice)
            return;
        ApplyEnemyDeathPowers(ref x);
        if (!x.NeedsChoice)
            NormalizeCardAfflictions(ref x);
        // EnchantmentLifecycleSupport.TriggerAfterTurnStartOrbs: Plasma's AfterTurnStartOrbTrigger
        // (through TriggerPassive, so ModifyOrbPassiveTriggerCounts applies).
        TriggerAfterTurnStartOrbs(ref x);
        if (x.NeedsChoice)
            return;
        // Auto pre-play has no supported sources.
        x.B.Phase = FlashPlayerPhase.AutoPrePlay;
        x.B.Phase = FlashPlayerPhase.Play;
        CheckWinCondition(ref x.B);
    }
}
