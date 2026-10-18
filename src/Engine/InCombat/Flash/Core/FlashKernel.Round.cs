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

        // PlayerTurnEndLifecycle.RunPhaseOne.
        x.B.Phase = FlashPlayerPhase.End;
        // EndTurnPowerSupport.TriggerVeryEarly and relic BeforeSideTurnEnd: no supported sources.
        // SimulateEndPlayerTurnBeforeOrbPassives.
        x.B.Phase = FlashPlayerPhase.AutoPostPlay;
        // AfterAutoPostPlayPhaseEntered and BeforeSideTurnEnd: no supported listeners.
        x.B.Phase = FlashPlayerPhase.End;
        CheckWinCondition(ref x.B);
        if (!IsOverOrEnding(x.B))
        {
            // Orb passives at turn end: no supported orbs.
            DoTurnEnd(ref x);
            if (x.NeedsChoice)
                return;
            CheckWinCondition(ref x.B);
            // CompletePlayerEarlySideTurnEndEffects (Regen): not supported.
        }
        ApplyEnemyDeathPowers(ref x);
        if (!IsInProgress(x.B))
            return;
        FlushPlayerHandAtTurnEnd(ref x);
        int turnEndShuffleEvents = x.B.ShuffleEventCount;
        // PlayerTurnEndLifecycle.RunPhaseTwo: no supported regular or transient side-turn-end sources.
        x.B.Phase = FlashPlayerPhase.None;
        x.B.ShufflesCrossed += x.B.ShuffleEventCount - turnEndShuffleEvents;
        ApplyEnemyDeathPowers(ref x);

        // ShouldTakeExtraTurn: no supported sources.
        RunEnemyTurn(ref x);
        // The performed-move marks only carry information within one enemy turn.
        for (int index = 0; index < x.B.EnemyCount; index++)
            x.B.Enemies[index].Flags &= ~FlashEnemyFlags.PerformedMoveThisRound;
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

        x.B.Side = FlashSide.Enemy;
        for (int position = 0; position < x.B.RosterCount; position++)
            SnapshotPowerAmountsAtTurnStart(ref x.B, EnemyOwner(x.B.Roster[position]));
        // BeforeSideTurnStart: no supported listeners.
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            ref FlashCreature enemy = ref x.B.Creature(EnemyOwner(x.B.Roster[position]));
            // ShouldClearBlock has no supported preventers.
            if (enemy.Block > 0)
                DamageBlock(ref enemy, enemy.Block, FlashValueProps.Move);
            // TriggerAfterBlockCleared: no supported sources.
        }
        // TriggerSideTurnStart (Blur, DrawCardsNextTurn, Plating, Slow, AfterSideTurnStart): no
        // supported sources. TriggerPoison: Poison is not supported.
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

        TriggerEnemySideTurnEndEffects(ref x);
        if (x.NeedsChoice)
            return;
        ApplyEnemyDeathPowers(ref x);
        // Player poison and NoDraw: not supported.
        if (CheckWinCondition(ref x.B))
            return;
        PrepareMonsterMovesForNextRound(ref x);
    }

    /// <summary>
    /// Mirrors <c>CorePowerSupport.TriggerEnemySideTurnEndEffects</c>. Only the duration ticks have
    /// supported sources.
    /// </summary>
    private static void TriggerEnemySideTurnEndEffects(ref FlashExec x)
    {
        // VeryEarly, Doom, Regen, Plating, DoubleDamage, TriggerRegular and transient powers: not supported.
        TickDurations(ref x.B);
        // AfterSideTurnEndLate: no supported listeners.
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

    /// <summary>Mirrors <c>CombatPredictionSimulator.DoTurnEnd</c>.</summary>
    private static void DoTurnEnd(ref FlashExec x)
    {
        if (IsOverOrEnding(x.B))
            return;
        // Ethereal cards exhaust in hand order; turn-end-in-hand cards are not supported yet.
        for (int index = 0; index < x.B.HandCount;)
        {
            ref FlashCard card = ref x.B.Cards[index];
            if (card.HasKeyword(FlashKeywords.Ethereal))
            {
                // ShouldEtherealTrigger has no supported listeners.
                Exhaust(ref x, FlashPile.Hand, index, causedByEthereal: true);
                if (x.NeedsChoice)
                    return;
                continue;
            }
            index++;
        }
    }

    /// <summary>Mirrors <c>CorePowerSupport.FlushPlayerHandAtTurnEnd</c>.</summary>
    private static void FlushPlayerHandAtTurnEnd(ref FlashExec x)
    {
        // BeforeFlush enchantments and ShouldFlush relics: not supported.
        for (int index = 0; index < x.B.HandCount;)
        {
            ref FlashCard card = ref x.B.Cards[index];
            bool retain = card.HasKeyword(FlashKeywords.Retain) || (card.Flags & FlashCardFlags.RetainThisTurn) != 0;
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
        x.B.Side = FlashSide.Player;
        x.B.Round++;
        x.B.PlayerTurn++;
        SnapshotPowerAmountsAtTurnStart(ref x.B, FlashLimits.PlayerOwner);
        // BeforeSideTurnStart: no supported listeners.
        if (x.B.Player.Block > 0)
            DamageBlock(ref x.B.Player, x.B.Player.Block, FlashValueProps.Move);
        // TriggerAfterBlockCleared: no supported sources.

        // Energy reset (Ice Cream not supported) plus modified max energy (no supported modifiers)
        // and energy next turn (not supported).
        x.B.Energy = 0;
        x.B.Energy = (int)Math.Clamp((long)x.B.Energy + Math.Max(0, x.R.BaseMaxEnergy), 0, FlashLimits.ValueClamp);
        // AfterEnergyReset (powers, relics, late): no supported listeners.

        int beforeHandShuffles = x.B.ShuffleEventCount;
        // PrepareBeforeHandDraw.
        x.B.Phase = FlashPlayerPhase.Start;
        // BeforeHandDraw: no supported listeners.
        x.B.ShufflesCrossed += x.B.ShuffleEventCount - beforeHandShuffles;
        // ConsumeModifiedHandDraw: no supported modifiers.
        int drawCount = BaseHandDrawCount;
        int effectiveDraw = Math.Min(drawCount, x.B.MaxHandSize - x.B.HandCount);
        bool willShuffle = effectiveDraw > x.B.DrawCount && x.B.DiscardCount != 0;
        Draw(ref x, drawCount, fromHandDraw: true);
        if (willShuffle)
            x.B.ShufflesCrossed++;
        if (x.NeedsChoice)
            return;
        // Draw compensation and AfterPlayerTurnStart: no supported sources.
        // TriggerSideTurnStart(Player): no supported sources.
        ApplyEnemyDeathPowers(ref x);
        // Enchantment orb hooks and auto pre-play: no supported sources.
        x.B.Phase = FlashPlayerPhase.AutoPrePlay;
        x.B.Phase = FlashPlayerPhase.Play;
        CheckWinCondition(ref x.B);
    }
}
