namespace BlitzSolver.Engine;

public static partial class Kernel
{
    /// <summary>
    /// Executes one player action to the next stable boundary, mirroring the action branches of
    /// <c>CombatBeamSolver.Replay</c>. On <see cref="StepResult.NeedsChoice"/> the battle is
    /// partial and must be discarded.
    /// </summary>
    public static StepResult Step(ref Battle battle, in PlayerAction action, Rules rules,
        out ChoiceRequest request)
        => Step(ref battle, action, rules, out request, out _);

    /// <summary>
    /// As <see cref="Step(ref Battle, in PlayerAction, Rules, out ChoiceRequest)"/>, also
    /// reporting whether a mirrored path recorded a prediction risk history entry during the action
    /// (used by the routing trait's pure input; transient, not battle state).
    /// </summary>
    public static StepResult Step(ref Battle battle, in PlayerAction action, Rules rules,
        out ChoiceRequest request, out bool historyRisk)
    {
        if (!IsInProgress(battle))
            throw new InvalidOperationException("战斗已经结束，不能再执行动作。");
        Exec x = new(ref battle, rules, action);
        switch (action.Kind)
        {
            case PlayerActionKind.PlayCard:
                PlayCardAction(ref x, action);
                break;
            case PlayerActionKind.EndTurn:
                AdvanceRound(ref x);
                if (!x.NeedsChoice)
                {
                    x.B.Flags &= ~BattleFlags.PlayerTurnEndRequested;
                }
                break;
            case PlayerActionKind.UsePotion:
                UsePotionAction(ref x, action);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action.Kind, null);
        }
        return FinishStep(ref x, action, out request, out historyRisk);
    }

    /// <summary>
    /// SettleReplayActionBoundary and the step result. Shared by the ordinary action dispatch and the
    /// prepared-end-turn continuation, so both paths perform the identical post-boundary work.
    /// </summary>
    private static StepResult FinishStep(ref Exec x, in PlayerAction action, out ChoiceRequest request,
        out bool historyRisk)
    {
        // SettleReplayActionBoundary: resolve the queued changes and renormalize card afflictions.
        // The existing engine skips SettleReplayActionBoundary for every non-None boundary, including
        // EventDefeat (CombatBeamSolver.Expansion.Replay.cs:432). Only the Blitz-internal transient
        // flags and decimal slots are still normalized below.
        if (!x.NeedsChoice && x.B.Outcome != Outcome.EventDefeat)
        {
            ResolvePowerAmountChanges(ref x);
            if (!x.NeedsChoice)
                NormalizeCardAfflictions(ref x);
        }
        if (!x.NeedsChoice && x.R.HasDecimalCardVars)
            DecimalVars.Canonicalize(ref x.B);
        if (!x.NeedsChoice)
            ClearPendingPowerAmountStates(ref x.B);
        if (!x.NeedsChoice && x.R.PenNibRelicIndex >= 0
            && x.R.PenNibRelicIndex < x.B.RelicCount)
        {
            // Projection/normalization: a play cut short before AfterCardPlayed (player death) leaves
            // the doubled-attack latch; no later settlement can read it and the importer projects 0.
            x.B.Relics[x.R.PenNibRelicIndex].Aux = 0;
        }
        historyRisk = x.HistoryRisk;
        if (x.NeedsChoice)
        {
            request = x.Request;
            return StepResult.NeedsChoice;
        }
        if (!x.AllChoicesConsumed)
            throw new InvalidOperationException($"动作 {action} 携带了未使用的选择。");
        request = default;
        return StepResult.Completed;
    }

    private static void PlayCardAction(ref Exec x, in PlayerAction action)
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
        if ((x.B.Flags & BattleFlags.PlayerTurnEndRequested) != 0)
        {
            x.B.Flags &= ~BattleFlags.PlayerTurnEndRequested;
            if (IsInProgress(x.B))
                AdvanceRound(ref x);
            x.B.Flags &= ~BattleFlags.PlayerTurnEndRequested;
        }
    }

    /// <summary>
    /// Enumerates legal actions in the existing search's order: playable hand cards (each with its
    /// hittable targets), then end turn. Identical cards are not deduplicated here.
    /// </summary>
    public static int LegalActions(in Battle battle, Rules rules, Span<PlayerAction> output)
    {
        if (!IsInProgress(battle))
            return 0;
        int count = 0;
        for (int hand = 0; hand < battle.HandCount; hand++)
        {
            if (!CanPlayCard(battle, rules, hand))
                continue;
            TargetType targetType = Registry.Card(battle.Cards[hand].Id)
                .ResolveTargetType(battle, rules, battle.Cards[hand]);
            if (targetType == TargetType.AnyEnemy)
            {
                for (int position = 0; position < battle.RosterCount; position++)
                {
                    byte owner = EnemyOwner(battle.Roster[position]);
                    if (IsHittable(battle, owner))
                        output[count++] = PlayerAction.PlayCard(hand, owner);
                }
            }
            else
            {
                output[count++] = PlayerAction.PlayCard(hand, Limits.NoOwner);
            }
        }
        // EnumeratePlannedPotionActions: every potion slot, with TargetsForPotion. The search applies
        // the potion policy (allowed slots, earliest turn, maximum uses) on top of this list.
        for (int slot = 0; slot < battle.PotionSlotCount; slot++)
        {
            ushort stored = battle.Potions[slot];
            if (stored == 0 || !Registry.Potion((ushort)(stored - 1)).Searchable)
                continue;
            switch (rules.Potion((ushort)(stored - 1)).Target)
            {
                case TargetType.AnyEnemy:
                    for (int position = 0; position < battle.RosterCount; position++)
                    {
                        byte owner = EnemyOwner(battle.Roster[position]);
                        if (IsHittable(battle, owner))
                            output[count++] = PlayerAction.UsePotion(slot, owner);
                    }
                    break;
                case TargetType.AnyPlayer or TargetType.Self:
                    if (battle.Player.IsAlive)
                        output[count++] = PlayerAction.UsePotion(slot, Limits.NoOwner);
                    break;
                case TargetType.AllEnemies or TargetType.TargetedNoCreature:
                    output[count++] = PlayerAction.UsePotion(slot, Limits.NoOwner);
                    break;
            }
        }
        output[count++] = PlayerAction.EndTurn();
        return count;
    }

    /// <summary>Upper bound of <see cref="LegalActions"/> output.</summary>
    public const int MaxLegalActions = Limits.MaxHandSize * Limits.Enemies + Limits.PotionSlots * Limits.Enemies + 1;
}
