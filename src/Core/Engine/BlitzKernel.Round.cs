namespace BlitzSolver.Engine;

public static partial class BlitzKernel
{
    private const int BaseHandDrawCount = 5;

    /// <summary>
    /// Mirrors <c>CombatBeamSolver.AdvanceRound</c>: player turn end, the enemy turn, and the next
    /// player turn start. Branches for unsupported powers and relics are omitted; admission rejects
    /// roots that could reach them.
    /// </summary>
    public static void AdvanceRound(ref BlitzExec x)
    {
        if (!IsInProgress(x.B))
            return;

        x.ChoiceTiming = BlitzChoiceTiming.PlayerTurnEnd;
        // PlayerTurnEndLifecycle.RunPhaseOne.
        x.B.Phase = BlitzPlayerPhase.End;
        // EndTurnPowerSupport.TriggerVeryEarly: no supported sources.
        RelicPass(ref x, RelicPhase.PrepareBeforeSideTurnEnd, BlitzSide.Player);
        if (x.NeedsChoice)
            return;
        // SimulateEndPlayerTurnBeforeOrbPassives.
        x.B.Phase = BlitzPlayerPhase.AutoPostPlay;
        AfterAutoPostPlayPhaseEnteredHook afterAutoPostPlay = new();
        BlitzHooks.Dispatch(ref x, ref afterAutoPostPlay, gatedByEnding: true);
        if (x.NeedsChoice)
            return;
        x.B.Phase = BlitzPlayerPhase.End;
        BeforeSideTurnEnd(ref x, BlitzSide.Player, ParticipantMask(BlitzLimits.PlayerOwner));
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
            // OrbLifecycleSupport.TriggerBeforeTurnEnd compensates the block breaks of the orb pass.
            ApplyBlockBreakCompensations(ref x);
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
        x.B.Phase = BlitzPlayerPhase.None;
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
            x.ChoiceTiming = BlitzChoiceTiming.EnemyTurn;
            // Block breaks from the player's turn end were settled at their own compensation points;
            // an enemy-turn break is never compensated (the existing engine only replays card, orb,
            // potion and reactive-relic history).
            x.PendingBlockBreakOwners = 0;
            RunEnemyTurn(ref x);
        }
        // The performed-move marks only carry information within one enemy turn. The
        // forced-must-perform mark is the same kind of in-turn kernel state
        // (SimulatedCombatState.MonsterAi.cs:122-126): PrepareMonsterMovesForNextRound consumes it when
        // the turn reaches that pass, but an early return (the player died during the enemy turn or a
        // choice was requested) skips the pass, and it must still be zero at every stable boundary.
        for (int index = 0; index < x.B.EnemyCount; index++)
        {
            x.B.Enemies[index].Flags &= ~(BlitzEnemyFlags.PerformedMoveThisRound | BlitzEnemyFlags.ForcedMustPerformAfterMove);
        }
        if (x.NeedsChoice || !IsInProgress(x.B))
            return;
        AdvanceRoundPlayerStart(ref x, takingExtraTurn);
    }

    /// <summary>Mirrors the enemy-turn block of <c>CombatBeamSolver.AdvanceRound</c>.</summary>
    private static void RunEnemyTurn(ref BlitzExec x)
    {
        Span<uint> acting = stackalloc uint[BlitzLimits.Enemies];
        Span<byte> steamPlayer = stackalloc byte[1];
        Span<byte> steamSelf = stackalloc byte[1];
        steamPlayer[0] = BlitzLimits.PlayerOwner;
        int actingCount = x.B.RosterCount;
        for (int position = 0; position < actingCount; position++)
            acting[position] = x.B.Enemies[x.B.Roster[position]].CombatId;

        SetSide(ref x.B, BlitzSide.Enemy);
        // BeginSideTurn for the enemies: their per-turn counters are not tracked, but
        // ResetCardLifecycleTurn clears the single shared _fetchCardsPlayedThisTurn set.
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref BlitzCard card = ref x.B.Cards[position];
            if (BlitzRegistry.Card(card.Id).TracksFetchPlay)
                card.Private &= ~FetchPlayedBit;
        }
        for (int position = 0; position < x.B.RosterCount; position++)
            SnapshotPowerAmountsAtTurnStart(ref x.B, EnemyOwner(x.B.Roster[position]));
        // Each phase reads the live enemy list, as the existing engine passes simulatedCombat.Enemies.
        Span<byte> participants = stackalloc byte[BlitzLimits.Enemies];
        BeforeSideTurnStart(ref x, BlitzSide.Enemy, participants[..RosterOwners(x.B, participants)]);
        if (x.NeedsChoice)
            return;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            ClearBlockAtSideStart(ref x, EnemyOwner(x.B.Roster[position]));
            if (x.NeedsChoice)
                return;
        }
        TriggerSideTurnStart(ref x, BlitzSide.Enemy, participants[..RosterOwners(x.B, participants)], decrementPlating: x.B.Round > 1);
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
            ref BlitzEnemy enemy = ref x.B.Enemies[actingRow];
            if (!CanPerformMonsterMove(enemy))
                continue;
            if ((enemy.Flags & BlitzEnemyFlags.SkipNextMove) != 0)
            {
                enemy.Flags &= ~BlitzEnemyFlags.SkipNextMove;
                enemy.Flags |= BlitzEnemyFlags.PerformedMoveThisRound;
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
                x.B.Enemy(owner).Flags |= BlitzEnemyFlags.PerformedMoveThisRound;
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
                x.B.Enemies[performedRow].Flags |= BlitzEnemyFlags.PerformedMoveThisRound;
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
        if (x.B.Outcome == BlitzOutcome.EventDefeat)
            return;
        ApplyEnemyDeathPowers(ref x);
        // CorePowerSupport.TriggerPoison over the player, then ClearNoDraw (no supported sources).
        Span<byte> players = stackalloc byte[1];
        players[0] = BlitzLimits.PlayerOwner;
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
    private static void BeforeSideTurnEnd(ref BlitzExec x, BlitzSide side, ushort participants)
    {
        BeforeSideTurnEndVeryEarlyHook veryEarly = new() { Side = side, Participants = participants };
        BlitzHooks.Dispatch(ref x, ref veryEarly, gatedByEnding: true);
        if (x.NeedsChoice)
            return;
        BeforeSideTurnEndEarlyHook early = new() { Side = side, Participants = participants };
        BlitzHooks.Dispatch(ref x, ref early, gatedByEnding: true);
        if (x.NeedsChoice)
            return;
        BeforeSideTurnEndHook normal = new() { Side = side, Participants = participants };
        BlitzHooks.Dispatch(ref x, ref normal, gatedByEnding: true);
    }

    public static ushort ParticipantMask(byte owner) => (ushort)(1 << owner);

    /// <summary>Whether <paramref name="owner"/> is in a participant bit set.</summary>
    public static bool Participates(ushort participants, byte owner) => (participants & (1 << owner)) != 0;

    /// <summary><c>SimulatedCombatState.CurrentSide</c> setter: a side change clears the lost-HP marks.</summary>
    private static void SetSide(ref BlitzBattle battle, BlitzSide side)
    {
        if (battle.Side != side)
            ClearLostHpThisTurn(ref battle);
        battle.Side = side;
    }

    /// <summary>
    /// <c>HasLostHpThisTurn</c> after <c>AdvancePlayerTurn</c>'s cache reset: only the root history's
    /// per-creature answer remains (<see cref="BlitzRules.RootLostHpMask"/>).
    /// </summary>
    private static void RestoreRootLostHp(ref BlitzBattle battle, ushort mask)
    {
        SetLostHpFlag(ref battle.Player, (mask & (1 << BlitzLimits.PlayerOwner)) != 0);
        SetLostHpFlag(ref battle.Osty, (mask & (1 << BlitzLimits.OstyOwner)) != 0);
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            SetLostHpFlag(ref battle.Enemies[index].Creature,
                (mask & (1 << EnemyOwner(index))) != 0);
        }
    }

    private static void SetLostHpFlag(ref BlitzCreature creature, bool lost)
    {
        if (lost)
            creature.Flags |= BlitzCreatureFlags.LostHpThisTurn;
        else
            creature.Flags &= ~BlitzCreatureFlags.LostHpThisTurn;
    }

    private static void ClearLostHpThisTurn(ref BlitzBattle battle)
    {
        battle.Player.Flags &= ~BlitzCreatureFlags.LostHpThisTurn;
        battle.Osty.Flags &= ~BlitzCreatureFlags.LostHpThisTurn;
        for (int index = 0; index < battle.EnemyCount; index++)
            battle.Enemies[index].Creature.Flags &= ~BlitzCreatureFlags.LostHpThisTurn;
    }

    /// <summary><c>SimulatedCombatState.BeginSideTurn</c> for the player: resets the per-turn counters.</summary>
    private static void BeginPlayerSideTurn(ref BlitzBattle battle)
    {
        for (BlitzCounter counter = BlitzCounter.CardsPlayedThisTurn; counter <= BlitzCounter.EnergySpentThisTurn; counter++)
            battle.SetCounter(counter, 0);
        // BeginSideTurn's ResetTurnCounter(_starsGainedThisTurn) for the player.
        battle.SetCounter(BlitzCounter.StarsGainedThisTurn, 0);
        // SimulatedCombatState.BeginSideTurn(player): Osty's per-turn counters are reset through the
        // native `ownerPlayer.Osty` pet field, which only exists for a pet that was already present when
        // the root was captured. A pet created during the search keeps accumulating (existing behavior).
        if ((battle.Flags & BlitzBattleFlags.OstyWasRootPet) != 0)
            battle.SetCounter(BlitzCounter.OstyAttacksThisTurn, 0);
        battle.SetCounter(BlitzCounter.DoomAppliedThisTurn, 0);
        // BeginSideTurn -> ResetCardLifecycleTurn: _fetchCardsPlayedThisTurn is cleared for this side.
        for (int position = 0; position < battle.CardCount; position++)
        {
            ref BlitzCard card = ref battle.Cards[position];
            if (BlitzRegistry.Card(card.Id).TracksFetchPlay)
                card.Private &= ~BlitzKernel.FetchPlayedBit;
        }
        // SimulatedCombatState.BeginSideTurn(player) -> RemovePoweredAttackHitsDealtBy(player):
        // BeatIntoShape's per-target hit count is a this-turn value keyed by the dealer's side.
        for (int index = 0; index < battle.EnemyCount; index++)
            battle.Enemies[index].PoweredAttackHitsThisTurn = 0;
    }

    /// <summary>Owners of the roster enemies in roster order.</summary>
    internal static int RosterOwners(in BlitzBattle battle, Span<byte> owners)
    {
        for (int position = 0; position < battle.RosterCount; position++)
            owners[position] = EnemyOwner(battle.Roster[position]);
        return battle.RosterCount;
    }

    /// <summary>
    /// Mirrors <c>CorePowerSupport.TickDurations</c>: Weak, Vulnerable, Frail and Intangible, then NoBlock,
    /// for every creature. Blitz marks those powers with <see cref="BlitzPowerDef.TicksAtEnemySideEnd"/>.
    /// </summary>
    private static void TickDurations(ref BlitzBattle battle)
    {
        Span<byte> creatures = stackalloc byte[BlitzLimits.Enemies + 2];
        int count = 0;
        creatures[count++] = BlitzLimits.PlayerOwner;
        if ((battle.Flags & BlitzBattleFlags.OstyPresent) != 0)
            creatures[count++] = BlitzLimits.OstyOwner;
        for (int position = 0; position < battle.RosterCount; position++)
            creatures[count++] = EnemyOwner(battle.Roster[position]);

        Span<ushort> ticking = stackalloc ushort[BlitzLimits.Powers];
        for (int index = 0; index < count; index++)
        {
            byte owner = creatures[index];
            int tickingCount = 0;
            for (int power = 0; power < battle.PowerCount; power++)
            {
                if (battle.Powers[power].Owner == owner && BlitzRegistry.Power(battle.Powers[power].Id).TicksAtEnemySideEnd)
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
    private static void DoTurnEnd(ref BlitzExec x)
    {
        if (IsOverOrEnding(x.B))
            return;
        int handCount = x.B.HandCount;
        Span<int> ethereal = stackalloc int[handCount];
        Span<int> turnEnd = stackalloc int[handCount];
        int etherealCount = 0, turnEndCount = 0;
        for (int index = 0; index < handCount; index++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[index];
            if (BlitzRegistry.Card(card.Id).HasTurnEndInHandEffect)
                turnEnd[turnEndCount++] = index;
            else if (HasKeyword(x.B, card, BlitzKeywords.Ethereal))
                ethereal[etherealCount++] = index;
        }
        Span<SelectedCard> etherealCards = stackalloc SelectedCard[etherealCount];
        SnapshotSelected(x.B, BlitzPile.Hand, ethereal[..etherealCount], etherealCards);
        Span<SelectedCard> turnEndCards = stackalloc SelectedCard[turnEndCount];
        SnapshotSelected(x.B, BlitzPile.Hand, turnEnd[..turnEndCount], turnEndCards);
        for (int item = 0; item < etherealCount; item++)
        {
            Exhaust(ref x, BlitzPile.Hand, LocateSelected(x.B, BlitzPile.Hand, etherealCards, item), causedByEthereal: true);
            if (x.NeedsChoice)
                return;
        }
        for (int item = 0; item < turnEndCount; item++)
        {
            int index = LocateSelected(x.B, BlitzPile.Hand, turnEndCards, item);
            BlitzCard card = turnEndCards[item].Card;
            MoveCard(ref x.B, BlitzPile.Hand, index, BlitzPile.Play);
            x.Card = card;
            x.Stats = x.R.Card(card.Id, card.Upgrade);
            x.Target = BlitzLimits.NoOwner;
            BlitzRegistry.Card(card.Id).OnTurnEndInHand(ref x);
            if (x.NeedsChoice)
                return;
            // An in-hand turn-end effect (Infection) may kill the player; the existing engine stops
            // the lifecycle there, so the card is no longer required to sit in the play pile.
            if (x.B.Player.IsDead)
                return;
            // The effect may change the card's own state (a private counter), never its identity or place.
            int position = x.B.PlayCount - 1;
            if (position < 0 || BlitzPiles.At(ref x.B, BlitzPile.Play, position).Id != card.Id
                || BlitzPiles.At(ref x.B, BlitzPile.Play, position).Upgrade != card.Upgrade)
                throw new BlitzUnsupportedException("回合末在手效果移动了打出区的卡。");
            if (HasKeyword(x.B, BlitzPiles.At(ref x.B, BlitzPile.Play, position), BlitzKeywords.Ethereal))
            {
                Exhaust(ref x, BlitzPile.Play, position, causedByEthereal: true);
                if (x.NeedsChoice)
                    return;
            }
            else
            {
                MoveCard(ref x.B, BlitzPile.Play, position, BlitzPile.Discard);
            }
        }
    }

    /// <summary>Mirrors <c>CorePowerSupport.FlushPlayerHandAtTurnEnd</c>.</summary>
    private static void FlushPlayerHandAtTurnEnd(ref BlitzExec x)
    {
        // EnchantmentLifecycleSupport.BeforeFlush runs before ShouldFlush and before any card moves.
        BeforeFlush(ref x);
        // A refused flush keeps the whole hand.
        bool flush = ShouldFlush(ref x);
        for (int index = 0; flush && index < x.B.HandCount;)
        {
            ref BlitzCard card = ref x.B.Cards[index];
            bool retain = HasKeyword(x.B, card, BlitzKeywords.Retain) || (card.Flags & BlitzCardFlags.RetainThisTurn) != 0;
            if (retain)
            {
                index++;
                continue;
            }
            if (IsEnding(x.B) || x.B.Player.IsDead)
                break;
            BlitzCard flushed = BlitzPiles.RemoveAt(ref x.B, BlitzPile.Hand, index);
            BlitzPiles.Append(ref x.B, BlitzPile.Discard, flushed);
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
    internal static void BeforeFlush(ref BlitzExec x)
    {
        if (!x.R.HasBeforeFlushEnchantment
            || (BlitzRegistry.AnyHooksExt & BlitzHookMaskExt.BeforeFlush) == 0)
            return;
        int handCount = x.B.HandCount;
        for (int index = 0; index < handCount; index++)
        {
            ref BlitzCard card = ref x.B.Cards[index];
            if (card.EnchantmentId == 0)
                continue;
            BlitzEnchantmentDef def = BlitzRegistry.Enchantment((ushort)(card.EnchantmentId - 1));
            if ((def.HooksExt & BlitzHookMaskExt.BeforeFlush) == 0)
                continue;
            def.BeforeFlush(ref x, new BlitzListener(BlitzListenerKind.Enchantment, (byte)index, BlitzLimits.PlayerOwner));
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>Mirrors <c>PersistentRelicSupport.ShouldFlush</c>: every combat listener, without the ending gate.</summary>
    private static bool ShouldFlush(ref BlitzExec x)
    {
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.ShouldFlush) == 0)
            return true;
        Span<BlitzListener> listeners = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
        int count = BlitzHooks.CollectListeners(x.B, BlitzHookMask.ShouldFlush, listeners);
        for (int index = 0; index < count; index++)
        {
            if (!BlitzHooks.Definition(x.B, listeners[index]).ShouldFlush(ref x, listeners[index]))
                return false;
        }
        return true;
    }

    /// <summary>Mirrors <c>CardModel.EndOfTurnCleanup</c>.</summary>
    private static void EndOfTurnCleanup(ref BlitzCard card)
    {
        card.Flags &= ~(BlitzCardFlags.ExhaustOnNextPlay | BlitzCardFlags.RetainThisTurn | BlitzCardFlags.SlyThisTurn);
        int kept = 0;
        for (int index = 0; index < card.EnergyModifierCount; index++)
        {
            BlitzCostModifier modifier = card.EnergyModifiers[index];
            if (((byte)modifier.Expiration & (byte)BlitzCostExpiration.EndOfTurn) != 0)
                continue;
            card.EnergyModifiers[kept++] = modifier;
        }
        for (int index = kept; index < BlitzLimits.CardEnergyCostModifiers; index++)
            card.EnergyModifiers[index] = default;
        card.EnergyModifierCount = kept;
        if (card.HasTemporaryStarCost)
            card.RemoveTemporaryStarCosts(atTurnEnd: true);
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.SnapshotPowerAmountsAtTurnStart</c> for one creature, limited
    /// to the powers whose <c>AmountOnTurnStart</c> any settlement reads
    /// (<see cref="BlitzPowerDef.UsesAmountOnTurnStart"/>). The native snapshot writes the model
    /// amount, which lags a mirrored decrement by one action boundary; Blitz has no such lag, so the
    /// remaining powers keep this inert field at zero instead of reproducing the stale model value.
    /// </summary>
    private static void SnapshotPowerAmountsAtTurnStart(ref BlitzBattle battle, byte owner)
    {
        for (int index = 0; index < battle.PowerCount; index++)
        {
            if (battle.Powers[index].Owner != owner)
                continue;
            battle.Powers[index].AmountOnTurnStart = BlitzRegistry.Power(battle.Powers[index].Id).UsesAmountOnTurnStart
                ? battle.Powers[index].Amount
                : 0;
        }
    }

    /// <summary>
    /// Mirrors <c>CombatBeamSolver.AdvanceRoundPlayerStart</c> and <c>ContinuePlayerStart</c> for a
    /// round without turn-start choices.
    /// </summary>
    private static void AdvanceRoundPlayerStart(ref BlitzExec x, bool takingExtraTurn = false)
    {
        // Enemy-turn damage is not part of any compensated history window.
        x.PendingBlockBreakOwners = 0;
        x.ChoiceTiming = BlitzChoiceTiming.PlayerTurnStart;
        SetSide(ref x.B, BlitzSide.Player);
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
        SnapshotPowerAmountsAtTurnStart(ref x.B, BlitzLimits.PlayerOwner);
        ReadOnlySpan<byte> player = [BlitzLimits.PlayerOwner];
        BeforeSideTurnStart(ref x, BlitzSide.Player, player);
        if (x.NeedsChoice)
            return;
        ClearBlockAtSideStart(ref x, BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;

        // Energy reset: PersistentRelicSupport.ShouldPlayerResetEnergy over every combat listener
        // (Ice Cream keeps the previous turn's energy from turn 2 on). Modified max energy and energy
        // next turn follow.
        if (BlitzHooks.ShouldPlayerResetEnergy(x.B, x.R, BlitzLimits.PlayerOwner))
            x.B.Energy = 0;
        // CombatBeamSolver.RoundTransition: GainEnergy(modified max energy + ConsumeEnergyNextTurn).
        int energyNextTurn = ConsumeEnergyNextTurn(ref x.B);
        x.B.Energy = (int)Math.Clamp((long)x.B.Energy + Math.Max(0, ModifiedMaxEnergy(ref x)) + energyNextTurn,
            0, BlitzLimits.ValueClamp);
        PowersAfterEnergyReset(ref x);
        if (x.NeedsChoice)
            return;
        RelicPass(ref x, RelicPhase.AfterEnergyReset, BlitzSide.Player);
        if (x.NeedsChoice)
            return;
        RelicPass(ref x, RelicPhase.AfterEnergyResetLate, BlitzSide.Player);
        if (x.NeedsChoice)
            return;

        // ContinuePlayerStart. A turn-start choice taken before the side starts starts it on the take
        // (TurnStartChoiceCursor.BeforeNextTake); the choice hooks call StartPlayerSideTurnIfNeeded.
        x.TakingExtraTurn = takingExtraTurn;
        int beforeHandShuffles = x.B.ShuffleEventCount;
        x.B.Phase = BlitzPlayerPhase.Start;
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
        // auto-plays. Imbued / HistoryCourse / WhisperingEarring have no Blitz definitions.
        x.B.Phase = BlitzPlayerPhase.AutoPrePlay;
        AutoPrePlay(ref x);
        if (x.NeedsChoice)
            return;
        x.B.Phase = BlitzPlayerPhase.Play;
        CheckWinCondition(ref x.B);
    }

    /// <summary>
    /// The player side start, either at its normal position (after the hand draw and
    /// <c>AfterPlayerTurnStart</c>) or when a pre-side turn-start choice is taken
    /// (<c>TurnStartChoiceCursor.BeforeNextTake</c>). Runs once per action execution.
    /// </summary>
    internal static void StartPlayerSideTurnIfNeeded(ref BlitzExec x)
    {
        if (x.SideTurnStartedEarly)
            return;
        x.SideTurnStartedEarly = true;
        ReadOnlySpan<byte> player = [BlitzLimits.PlayerOwner];
        TriggerSideTurnStart(ref x, BlitzSide.Player, player, decrementPlating: x.B.PlayerTurn != 1,
            isExtraTurn: x.TakingExtraTurn);
    }

// This batch's turn-boundary relic hooks (BlitzHookMaskExt bits 3-6). Each is dispatched at the
// existing engine's position and over the same listener order as the action-hook dispatcher.

/// <summary><c>CorePowerSupport.FlushPlayerHandAtTurnEnd</c>: <c>AfterFlush</c> over the combat listeners.</summary>
internal static void AfterFlushPass(ref BlitzExec x)
{
    if ((BlitzRegistry.AnyHooksExt & BlitzHookMaskExt.AfterFlush) == 0)
        return;
    Span<BlitzListener> listeners = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
    int count = BlitzHooks.CollectListeners(x.B, BlitzHookMaskExt.AfterFlush, listeners);
    for (int index = 0; index < count; index++)
    {
        BlitzListener self = listeners[index];
        BlitzHooks.Definition(x.B, self).AfterFlush(ref x, self);
        if (x.NeedsChoice)
            return;
    }
}

/// <summary><c>HookMirrors.ShouldTakeExtraTurn</c>: the first listener answering true wins.</summary>
internal static bool ShouldTakeExtraTurnPass(ref BlitzExec x)
{
    if ((BlitzRegistry.AnyHooksExt & BlitzHookMaskExt.ShouldTakeExtraTurn) == 0)
        return false;
    Span<BlitzListener> listeners = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
    int count = BlitzHooks.CollectListeners(x.B, BlitzHookMaskExt.ShouldTakeExtraTurn, listeners);
    for (int index = 0; index < count; index++)
    {
        BlitzListener self = listeners[index];
        if (BlitzHooks.Definition(x.B, self).ShouldTakeExtraTurn(ref x, self))
            return true;
        if (x.NeedsChoice)
            return false;
    }
    return false;
}

/// <summary><c>HookMirrors.AfterTakingExtraTurn</c>: a fixed listener snapshot, in dispatch order.</summary>
internal static void AfterTakingExtraTurnPass(ref BlitzExec x)
{
    if ((BlitzRegistry.AnyHooksExt & BlitzHookMaskExt.AfterTakingExtraTurn) == 0)
        return;
    Span<BlitzListener> listeners = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
    int count = BlitzHooks.CollectListeners(x.B, BlitzHookMaskExt.AfterTakingExtraTurn, listeners);
    for (int index = 0; index < count; index++)
    {
        BlitzListener self = listeners[index];
        BlitzHooks.Definition(x.B, self).AfterTakingExtraTurn(ref x, self);
        if (x.NeedsChoice)
            return;
    }
}

/// <summary><c>SimulatedCombatState.RecordRelicRoundDamage</c>: the per-round relic bookkeeping.</summary>
internal static void AfterRelicRoundPass(ref BlitzExec x)
{
    if ((BlitzRegistry.AnyHooksExt & BlitzHookMaskExt.AfterRelicRound) == 0)
        return;
    Span<BlitzListener> listeners = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
    int count = BlitzHooks.CollectListeners(x.B, BlitzHookMaskExt.AfterRelicRound, listeners);
    for (int index = 0; index < count; index++)
    {
        BlitzListener self = listeners[index];
        BlitzHooks.Definition(x.B, self).AfterRelicRound(ref x, self);
        if (x.NeedsChoice)
            return;
    }
}
}
