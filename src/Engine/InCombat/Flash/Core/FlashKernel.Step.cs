namespace CombatSolver.Engine.InCombat.Flash;

public static partial class FlashKernel
{
    /// <summary>
    /// Executes one player action to the next stable boundary, mirroring the action branches of
    /// <c>CombatBeamSolver.Replay</c>. On <see cref="FlashStepResult.NeedsChoice"/> the battle is
    /// partial and must be discarded.
    /// </summary>
    public static FlashStepResult Step(ref FlashBattle battle, in FlashAction action, FlashRules rules,
        out FlashChoiceRequest request)
        => Step(ref battle, action, rules, out request, out _);

    /// <summary>
    /// As <see cref="Step(ref FlashBattle, in FlashAction, FlashRules, out FlashChoiceRequest)"/>, also
    /// reporting whether a mirrored path recorded a prediction risk history entry during the action
    /// (used by the routing trait's pure input; transient, not battle state).
    /// </summary>
    public static FlashStepResult Step(ref FlashBattle battle, in FlashAction action, FlashRules rules,
        out FlashChoiceRequest request, out bool historyRisk)
    {
        if (!IsInProgress(battle))
            throw new InvalidOperationException("战斗已经结束，不能再执行动作。");
        FlashExec x = new(ref battle, rules, action);
        switch (action.Kind)
        {
            case FlashActionKind.PlayCard:
                PlayCardAction(ref x, action);
                break;
            case FlashActionKind.EndTurn:
                AdvanceRound(ref x);
                if (!x.NeedsChoice)
                {
                    x.B.Flags &= ~FlashBattleFlags.PlayerTurnEndRequested;
                }
                break;
            case FlashActionKind.UsePotion:
                UsePotionAction(ref x, action);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action.Kind, null);
        }
        // SettleReplayActionBoundary: resolve the queued changes and renormalize card afflictions.
        // The existing engine skips SettleReplayActionBoundary for every non-None boundary, including
        // EventDefeat (CombatBeamSolver.Expansion.Replay.cs:432). Only the Flash-internal transient
        // flags and decimal slots are still normalized below.
        if (!x.NeedsChoice && x.B.Outcome != FlashOutcome.EventDefeat)
        {
            ResolvePowerAmountChanges(ref x);
            if (!x.NeedsChoice)
                NormalizeCardAfflictions(ref x);
        }
        if (!x.NeedsChoice && rules.HasDecimalCardVars)
            FlashDecimalVars.Canonicalize(ref battle);
        if (!x.NeedsChoice)
            ClearPendingPowerAmountStates(ref battle);
        if (!x.NeedsChoice && rules.PenNibRelicIndex >= 0
            && rules.PenNibRelicIndex < battle.RelicCount)
        {
            // Projection/normalization: a play cut short before AfterCardPlayed (player death) leaves
            // the doubled-attack latch; no later settlement can read it and the importer projects 0.
            battle.Relics[rules.PenNibRelicIndex].Aux = 0;
        }
        historyRisk = x.HistoryRisk;
        if (x.NeedsChoice)
        {
            request = x.Request;
            return FlashStepResult.NeedsChoice;
        }
        if (!x.AllChoicesConsumed)
            throw new InvalidOperationException($"动作 {action} 携带了未使用的选择。");
        request = default;
        return FlashStepResult.Completed;
    }

    private static void PlayCardAction(ref FlashExec x, in FlashAction action)
    {
        if (action.HandIndex >= x.B.HandCount)
            throw new ArgumentOutOfRangeException(nameof(action), action.HandIndex, "手牌位置越界。");
        if (!CanPlayCard(x.B, x.R, action.HandIndex))
            throw new InvalidOperationException($"手牌位置 {action.HandIndex} 的卡当前不能打出。");
        int shuffleEvents = x.B.ShuffleEventCount;
        ManualPlay(ref x, action.HandIndex, action.Target);
        if (x.NeedsChoice)
            return;
        // CompleteCardExecution and the replay loop both run the deferred enemy-death pass.
        ApplyEnemyDeathPowers(ref x);
        ApplyEnemyDeathPowers(ref x);
        // SettleReplayActionBoundary: no supported power reactions or normalizations.
        if (x.B.ShuffleEventCount != shuffleEvents)
            x.B.ShufflesCrossed++;
        CheckWinCondition(ref x.B);
        if ((x.B.Flags & FlashBattleFlags.PlayerTurnEndRequested) != 0)
        {
            x.B.Flags &= ~FlashBattleFlags.PlayerTurnEndRequested;
            if (IsInProgress(x.B))
                AdvanceRound(ref x);
            x.B.Flags &= ~FlashBattleFlags.PlayerTurnEndRequested;
        }
    }

    /// <summary>
    /// Enumerates legal actions in the existing search's order: playable hand cards (each with its
    /// hittable targets), then end turn. Identical cards are not deduplicated here.
    /// </summary>
    public static int LegalActions(in FlashBattle battle, FlashRules rules, Span<FlashAction> output)
    {
        if (!IsInProgress(battle))
            return 0;
        int count = 0;
        for (int hand = 0; hand < battle.HandCount; hand++)
        {
            if (!CanPlayCard(battle, rules, hand))
                continue;
            FlashTargetType targetType = FlashRegistry.Card(battle.Cards[hand].Id)
                .TargetType(battle, rules, battle.Cards[hand]);
            if (targetType == FlashTargetType.AnyEnemy)
            {
                for (int position = 0; position < battle.RosterCount; position++)
                {
                    byte owner = EnemyOwner(battle.Roster[position]);
                    if (IsHittable(battle, owner))
                        output[count++] = FlashAction.PlayCard(hand, owner);
                }
            }
            else
            {
                output[count++] = FlashAction.PlayCard(hand, FlashLimits.NoOwner);
            }
        }
        // EnumeratePlannedPotionActions: every potion slot, with TargetsForPotion. The search applies
        // the potion policy (allowed slots, earliest turn, maximum uses) on top of this list.
        for (int slot = 0; slot < battle.PotionSlotCount; slot++)
        {
            ushort stored = battle.Potions[slot];
            if (stored == 0 || !FlashRegistry.Potion((ushort)(stored - 1)).Searchable)
                continue;
            switch (rules.Potion((ushort)(stored - 1)).Target)
            {
                case FlashTargetType.AnyEnemy:
                    for (int position = 0; position < battle.RosterCount; position++)
                    {
                        byte owner = EnemyOwner(battle.Roster[position]);
                        if (IsHittable(battle, owner))
                            output[count++] = FlashAction.UsePotion(slot, owner);
                    }
                    break;
                case FlashTargetType.AnyPlayer or FlashTargetType.Self:
                    if (battle.Player.IsAlive)
                        output[count++] = FlashAction.UsePotion(slot, FlashLimits.NoOwner);
                    break;
                case FlashTargetType.AllEnemies or FlashTargetType.TargetedNoCreature:
                    output[count++] = FlashAction.UsePotion(slot, FlashLimits.NoOwner);
                    break;
            }
        }
        output[count++] = FlashAction.EndTurn();
        return count;
    }

    /// <summary>Upper bound of <see cref="LegalActions"/> output.</summary>
    public const int MaxLegalActions = FlashLimits.MaxHandSize * FlashLimits.Enemies + FlashLimits.PotionSlots * FlashLimits.Enemies + 1;
}
