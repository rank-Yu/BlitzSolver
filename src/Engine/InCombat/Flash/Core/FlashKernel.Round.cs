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

        // HookMirrors.ShouldTakeExtraTurn after the player turn-end phases, before the enemy turn.
        bool takingExtraTurn = ShouldTakeExtraTurnPass(ref x);
        if (x.NeedsChoice)
            return;
        if (takingExtraTurn)
        {
            // An extra turn advances the player's turn number, so damage from the just-finished turn
            // becomes Emotion Chip's "previous turn" window (recorded only when that relic is present).
            AfterRelicRoundPass(ref x);
            if (x.NeedsChoice)
                return;
            AfterTakingExtraTurnPass(ref x);
            if (x.NeedsChoice)
                return;
        }
        else
        {
            x.ChoiceTiming = FlashChoiceTiming.EnemyTurn;
            RunEnemyTurn(ref x);
        }
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
        AdvanceRoundPlayerStart(ref x, takingExtraTurn);
    }

    /// <summary>Mirrors the enemy-turn block of <c>CombatBeamSolver.AdvanceRound</c>.</summary>
    private static void RunEnemyTurn(ref FlashExec x)
    {
        Span<uint> acting = stackalloc uint[FlashLimits.Enemies];
        Span<byte> steamPlayer = stackalloc byte[1];
        Span<byte> steamSelf = stackalloc byte[1];
        steamPlayer[0] = FlashLimits.PlayerOwner;
        int actingCount = x.B.RosterCount;
        for (int position = 0; position < actingCount; position++)
            acting[position] = x.B.Enemies[x.B.Roster[position]].CombatId;

        SetSide(ref x.B, FlashSide.Enemy);
        // BeginSideTurn for the enemies: their per-turn counters are not tracked, but
        // ResetCardLifecycleTurn clears the single shared _fetchCardsPlayedThisTurn set.
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref FlashCard card = ref x.B.Cards[position];
            if (FlashRegistry.Card(card.Id).TracksFetchPlay)
                card.Private &= ~FetchPlayedBit;
        }
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
            // The acting snapshot is by CombatId: an escaped creature compacts the known array, and a
            // stored owner index would otherwise name a different enemy for every later slot.
            int actingRow = FindEnemyIndex(x.B, acting[index]);
            if (actingRow < 0)
                continue;
            byte owner = EnemyOwner(actingRow);
            ref FlashEnemy enemy = ref x.B.Enemies[actingRow];
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
            // CombatBeamSolver.Expansion.Replay: the Steam Eruption forced phase is consumed after
            // the stun skip and before the normal move. It replaces the move entirely; the performed
            // mark still advances the state machine at the end of the turn.
            if (TryConsumeSteamForcedMove(ref x, owner, out bool steamExploded, out int steamDamage))
            {
                // The performed move is the one captured before the forced phase replaces it.
                x.B.Enemy(owner).Flags |= FlashEnemyFlags.PerformedMoveThisRound;
                if (steamExploded)
                {
                    DamagePlayerFromMonster(ref x, owner, steamPlayer, steamDamage);
                    if (x.NeedsChoice)
                        return;
                    steamSelf[0] = owner;
                    Kill(ref x, steamSelf, force: true);
                    if (x.NeedsChoice)
                        return;
                    ApplyEnemyDeathPowers(ref x);
                    if (x.NeedsChoice)
                        return;
                }
                if (CheckWinCondition(ref x.B))
                    return;
                continue;
            }
            uint actingCombatId = enemy.CombatId;
            bool playerDied = ExecuteMonsterMove(ref x, owner);
            int performedRow = FindEnemyIndex(x.B, actingCombatId);
            if (performedRow >= 0)
                x.B.Enemies[performedRow].Flags |= FlashEnemyFlags.PerformedMoveThisRound;
            if (x.NeedsChoice)
                return;
            if (CheckWinCondition(ref x.B) || playerDied)
                return;
        }

        TriggerEnemySideTurnEndEffects(ref x, participants[..RosterOwners(x.B, participants)]);
        if (x.NeedsChoice)
            return;
        // CombatBeamSolver.Expansion.Replay checks BattlewornDummyTimedOut immediately after
        // TriggerEnemySideTurnEndEffects and returns SearchBoundaryReason.EventDefeat before the
        // enemy-death, poison, affliction-normalization and next-round preparation passes.
        if (x.B.Outcome == FlashOutcome.EventDefeat)
            return;
        ApplyEnemyDeathPowers(ref x);
        // CorePowerSupport.TriggerPoison over the player, then ClearNoDraw (no supported sources).
        Span<byte> players = stackalloc byte[1];
        players[0] = FlashLimits.PlayerOwner;
        if (!TriggerPoison(ref x, players))
            return;
        if (!x.NeedsChoice)
            NormalizeCardAfflictions(ref x);
        AfterRelicRoundPass(ref x);
        if (x.NeedsChoice)
            return;
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

    /// <summary>
    /// <c>HasLostHpThisTurn</c> after <c>AdvancePlayerTurn</c>'s cache reset: only the root history's
    /// per-creature answer remains (<see cref="FlashRules.RootLostHpMask"/>).
    /// </summary>
    private static void RestoreRootLostHp(ref FlashBattle battle, ushort mask)
    {
        SetLostHpFlag(ref battle.Player, (mask & (1 << FlashLimits.PlayerOwner)) != 0);
        SetLostHpFlag(ref battle.Osty, (mask & (1 << FlashLimits.OstyOwner)) != 0);
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            SetLostHpFlag(ref battle.Enemies[index].Creature,
                (mask & (1 << EnemyOwner(index))) != 0);
        }
    }

    private static void SetLostHpFlag(ref FlashCreature creature, bool lost)
    {
        if (lost)
            creature.Flags |= FlashCreatureFlags.LostHpThisTurn;
        else
            creature.Flags &= ~FlashCreatureFlags.LostHpThisTurn;
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
        // BeginSideTurn's ResetTurnCounter(_starsGainedThisTurn) for the player.
        battle.SetCounter(FlashCounter.StarsGainedThisTurn, 0);
        // SimulatedCombatState.BeginSideTurn(player): Osty's per-turn counters are reset through the
        // native `ownerPlayer.Osty` pet field, which only exists for a pet that was already present when
        // the root was captured. A pet created during the search keeps accumulating (existing behavior).
        if ((battle.Flags & FlashBattleFlags.OstyWasRootPet) != 0)
            battle.SetCounter(FlashCounter.OstyAttacksThisTurn, 0);
        battle.SetCounter(FlashCounter.DoomAppliedThisTurn, 0);
        // BeginSideTurn -> ResetCardLifecycleTurn: _fetchCardsPlayedThisTurn is cleared for this side.
        for (int position = 0; position < battle.CardCount; position++)
        {
            ref FlashCard card = ref battle.Cards[position];
            if (FlashRegistry.Card(card.Id).TracksFetchPlay)
                card.Private &= ~FlashKernel.FetchPlayedBit;
        }
        // SimulatedCombatState.BeginSideTurn(player) -> RemovePoweredAttackHitsDealtBy(player):
        // BeatIntoShape's per-target hit count is a this-turn value keyed by the dealer's side.
        for (int index = 0; index < battle.EnemyCount; index++)
            battle.Enemies[index].PoweredAttackHitsThisTurn = 0;
    }

    /// <summary>Owners of the roster enemies in roster order.</summary>
    internal static int RosterOwners(in FlashBattle battle, Span<byte> owners)
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
            // An in-hand turn-end effect (Infection) may kill the player; the existing engine stops
            // the lifecycle there, so the card is no longer required to sit in the play pile.
            if (x.B.Player.IsDead)
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
        // EnchantmentLifecycleSupport.BeforeFlush runs before ShouldFlush and before any card moves.
        BeforeFlush(ref x);
        // A refused flush keeps the whole hand.
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
        // CorePowerSupport.FlushPlayerHandAtTurnEnd runs the AfterFlush relics after the non-retained
        // hand moved and before the end-of-turn card cleanup (Bookmark).
        AfterFlushPass(ref x);
        if (x.NeedsChoice)
            return;
        // End-of-turn cleanup for every combat card:
        int total = x.B.CardCount;
        for (int position = 0; position < total; position++)
            EndOfTurnCleanup(ref x.B.Cards[position]);
    }

    /// <summary>
    /// <c>EnchantmentLifecycleSupport.BeforeFlush</c>: every hand card's enchantment gets the callback
    /// before <c>ShouldFlush</c>. The existing engine iterates the hand directly (SlumberingEssence adds
    /// one <c>WhenPlayed</c> −1 modifier per copy), so the kernel does the same instead of a generic
    /// listener scan.
    /// </summary>
    internal static void BeforeFlush(ref FlashExec x)
    {
        if (!x.R.HasBeforeFlushEnchantment
            || (FlashRegistry.AnyHooksExt & FlashHookMaskExt.BeforeFlush) == 0)
            return;
        int handCount = x.B.HandCount;
        for (int index = 0; index < handCount; index++)
        {
            ref FlashCard card = ref x.B.Cards[index];
            if (card.EnchantmentId == 0)
                continue;
            FlashEnchantmentDef def = FlashRegistry.Enchantment((ushort)(card.EnchantmentId - 1));
            if ((def.HooksExt & FlashHookMaskExt.BeforeFlush) == 0)
                continue;
            def.BeforeFlush(ref x, new FlashListener(FlashListenerKind.Enchantment, (byte)index, FlashLimits.PlayerOwner));
            if (x.NeedsChoice)
                return;
        }
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

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.SnapshotPowerAmountsAtTurnStart</c> for one creature, limited
    /// to the powers whose <c>AmountOnTurnStart</c> any settlement reads
    /// (<see cref="FlashPowerDef.UsesAmountOnTurnStart"/>). The native snapshot writes the model
    /// amount, which lags a mirrored decrement by one action boundary; Flash has no such lag, so the
    /// remaining powers keep this inert field at zero instead of reproducing the stale model value.
    /// </summary>
    private static void SnapshotPowerAmountsAtTurnStart(ref FlashBattle battle, byte owner)
    {
        for (int index = 0; index < battle.PowerCount; index++)
        {
            if (battle.Powers[index].Owner != owner)
                continue;
            battle.Powers[index].AmountOnTurnStart = FlashRegistry.Power(battle.Powers[index].Id).UsesAmountOnTurnStart
                ? battle.Powers[index].Amount
                : 0;
        }
    }

    /// <summary>
    /// Mirrors <c>CombatBeamSolver.AdvanceRoundPlayerStart</c> and <c>ContinuePlayerStart</c> for a
    /// round without turn-start choices.
    /// </summary>
    private static void AdvanceRoundPlayerStart(ref FlashExec x, bool takingExtraTurn = false)
    {
        x.ChoiceTiming = FlashChoiceTiming.PlayerTurnStart;
        SetSide(ref x.B, FlashSide.Player);
        if (!takingExtraTurn)
            x.B.Round++;
        x.B.PlayerTurn++;
        // RoundNumber and CurrentSide only clear the lost-HP marks when they change. An extra turn
        // keeps the same round and side, and AdvancePlayerTurn only drops the prediction cache: the
        // engine then answers HasLostHpThisTurn from the immutable root history. Restore exactly the
        // root answer for an extra turn; a normal turn advances the round and clears everything.
        if (takingExtraTurn)
            RestoreRootLostHp(ref x.B, x.R.RootLostHpMask);
        else
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

        // ContinuePlayerStart. A turn-start choice taken before the side starts starts it on the take
        // (TurnStartChoiceCursor.BeforeNextTake); the choice hooks call StartPlayerSideTurnIfNeeded.
        x.TakingExtraTurn = takingExtraTurn;
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
        StartPlayerSideTurnIfNeeded(ref x);
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
        // TriggerAutoPrePlayEarly: Bombardment cards in the exhaust pile, then the scheduled Mayhem
        // auto-plays. Imbued / HistoryCourse / WhisperingEarring have no Flash definitions.
        x.B.Phase = FlashPlayerPhase.AutoPrePlay;
        AutoPrePlay(ref x);
        if (x.NeedsChoice)
            return;
        x.B.Phase = FlashPlayerPhase.Play;
        CheckWinCondition(ref x.B);
    }

    /// <summary>
    /// The player side start, either at its normal position (after the hand draw and
    /// <c>AfterPlayerTurnStart</c>) or when a pre-side turn-start choice is taken
    /// (<c>TurnStartChoiceCursor.BeforeNextTake</c>). Runs once per action execution.
    /// </summary>
    internal static void StartPlayerSideTurnIfNeeded(ref FlashExec x)
    {
        if (x.SideTurnStartedEarly)
            return;
        x.SideTurnStartedEarly = true;
        ReadOnlySpan<byte> player = [FlashLimits.PlayerOwner];
        TriggerSideTurnStart(ref x, FlashSide.Player, player, decrementPlating: x.B.PlayerTurn != 1,
            isExtraTurn: x.TakingExtraTurn);
    }

// This batch's turn-boundary relic hooks (FlashHookMaskExt bits 3-6). Each is dispatched at the
// existing engine's position and over the same listener order as the action-hook dispatcher.

/// <summary><c>CorePowerSupport.FlushPlayerHandAtTurnEnd</c>: <c>AfterFlush</c> over the combat listeners.</summary>
internal static void AfterFlushPass(ref FlashExec x)
{
    if ((FlashRegistry.AnyHooksExt & FlashHookMaskExt.AfterFlush) == 0)
        return;
    Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
    int count = FlashHooks.CollectListeners(x.B, FlashHookMaskExt.AfterFlush, listeners);
    for (int index = 0; index < count; index++)
    {
        FlashListener self = listeners[index];
        FlashHooks.Definition(x.B, self).AfterFlush(ref x, self);
        if (x.NeedsChoice)
            return;
    }
}

/// <summary><c>HookMirrors.ShouldTakeExtraTurn</c>: the first listener answering true wins.</summary>
internal static bool ShouldTakeExtraTurnPass(ref FlashExec x)
{
    if ((FlashRegistry.AnyHooksExt & FlashHookMaskExt.ShouldTakeExtraTurn) == 0)
        return false;
    Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
    int count = FlashHooks.CollectListeners(x.B, FlashHookMaskExt.ShouldTakeExtraTurn, listeners);
    for (int index = 0; index < count; index++)
    {
        FlashListener self = listeners[index];
        if (FlashHooks.Definition(x.B, self).ShouldTakeExtraTurn(ref x, self))
            return true;
        if (x.NeedsChoice)
            return false;
    }
    return false;
}

/// <summary><c>HookMirrors.AfterTakingExtraTurn</c>: a fixed listener snapshot, in dispatch order.</summary>
internal static void AfterTakingExtraTurnPass(ref FlashExec x)
{
    if ((FlashRegistry.AnyHooksExt & FlashHookMaskExt.AfterTakingExtraTurn) == 0)
        return;
    Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
    int count = FlashHooks.CollectListeners(x.B, FlashHookMaskExt.AfterTakingExtraTurn, listeners);
    for (int index = 0; index < count; index++)
    {
        FlashListener self = listeners[index];
        FlashHooks.Definition(x.B, self).AfterTakingExtraTurn(ref x, self);
        if (x.NeedsChoice)
            return;
    }
}

/// <summary><c>SimulatedCombatState.RecordRelicRoundDamage</c>: the per-round relic bookkeeping.</summary>
internal static void AfterRelicRoundPass(ref FlashExec x)
{
    if ((FlashRegistry.AnyHooksExt & FlashHookMaskExt.AfterRelicRound) == 0)
        return;
    Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
    int count = FlashHooks.CollectListeners(x.B, FlashHookMaskExt.AfterRelicRound, listeners);
    for (int index = 0; index < count; index++)
    {
        FlashListener self = listeners[index];
        FlashHooks.Definition(x.B, self).AfterRelicRound(ref x, self);
        if (x.NeedsChoice)
            return;
    }
}
}
