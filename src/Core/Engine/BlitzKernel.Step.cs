namespace BlitzSolver.Engine;

public static partial class BlitzKernel
{
    /// <summary>
    /// Executes one player action to the next stable boundary, mirroring the action branches of
    /// <c>CombatBeamSolver.Replay</c>. On <see cref="BlitzStepResult.NeedsChoice"/> the battle is
    /// partial and must be discarded.
    /// </summary>
    public static BlitzStepResult Step(ref BlitzBattle battle, in BlitzAction action, BlitzRules rules,
        out BlitzChoiceRequest request)
        => Step(ref battle, action, rules, out request, out _);

    /// <summary>
    /// As <see cref="Step(ref BlitzBattle, in BlitzAction, BlitzRules, out BlitzChoiceRequest)"/>, also
    /// reporting whether a mirrored path recorded a prediction risk history entry during the action
    /// (used by the routing trait's pure input; transient, not battle state).
    /// </summary>
    public static BlitzStepResult Step(ref BlitzBattle battle, in BlitzAction action, BlitzRules rules,
        out BlitzChoiceRequest request, out bool historyRisk)
    {
        if (!IsInProgress(battle))
            throw new InvalidOperationException("战斗已经结束，不能再执行动作。");
        BlitzExec x = new(ref battle, rules, action);
        switch (action.Kind)
        {
            case BlitzActionKind.PlayCard:
                PlayCardAction(ref x, action);
                break;
            case BlitzActionKind.EndTurn:
                AdvanceRound(ref x);
                if (!x.NeedsChoice)
                {
                    x.B.Flags &= ~BlitzBattleFlags.PlayerTurnEndRequested;
                }
                break;
            case BlitzActionKind.UsePotion:
                UsePotionAction(ref x, action);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action.Kind, null);
        }
        // SettleReplayActionBoundary: resolve the queued changes and renormalize card afflictions.
        // The existing engine skips SettleReplayActionBoundary for every non-None boundary, including
        // EventDefeat (CombatBeamSolver.Expansion.Replay.cs:432). Only the Blitz-internal transient
        // flags and decimal slots are still normalized below.
        if (!x.NeedsChoice && x.B.Outcome != BlitzOutcome.EventDefeat)
        {
            ResolvePowerAmountChanges(ref x);
            if (!x.NeedsChoice)
                NormalizeCardAfflictions(ref x);
        }
        if (!x.NeedsChoice && rules.HasDecimalCardVars)
            BlitzDecimalVars.Canonicalize(ref battle);
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
            return BlitzStepResult.NeedsChoice;
        }
        if (!x.AllChoicesConsumed)
            throw new InvalidOperationException($"动作 {action} 携带了未使用的选择。");
        request = default;
        return BlitzStepResult.Completed;
    }

    private static void PlayCardAction(ref BlitzExec x, in BlitzAction action)
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
        if ((x.B.Flags & BlitzBattleFlags.PlayerTurnEndRequested) != 0)
        {
            x.B.Flags &= ~BlitzBattleFlags.PlayerTurnEndRequested;
            if (IsInProgress(x.B))
                AdvanceRound(ref x);
            x.B.Flags &= ~BlitzBattleFlags.PlayerTurnEndRequested;
        }
    }

    /// <summary>
    /// Enumerates legal actions in the existing search's order: playable hand cards (each with its
    /// hittable targets), then end turn. Identical cards are not deduplicated here.
    /// </summary>
    public static int LegalActions(in BlitzBattle battle, BlitzRules rules, Span<BlitzAction> output)
    {
        if (!IsInProgress(battle))
            return 0;
        int count = 0;
        for (int hand = 0; hand < battle.HandCount; hand++)
        {
            if (!CanPlayCard(battle, rules, hand))
                continue;
            BlitzTargetType targetType = BlitzRegistry.Card(battle.Cards[hand].Id)
                .TargetType(battle, rules, battle.Cards[hand]);
            if (targetType == BlitzTargetType.AnyEnemy)
            {
                for (int position = 0; position < battle.RosterCount; position++)
                {
                    byte owner = EnemyOwner(battle.Roster[position]);
                    if (IsHittable(battle, owner))
                        output[count++] = BlitzAction.PlayCard(hand, owner);
                }
            }
            else
            {
                output[count++] = BlitzAction.PlayCard(hand, BlitzLimits.NoOwner);
            }
        }
        // EnumeratePlannedPotionActions: every potion slot, with TargetsForPotion. The search applies
        // the potion policy (allowed slots, earliest turn, maximum uses) on top of this list.
        for (int slot = 0; slot < battle.PotionSlotCount; slot++)
        {
            ushort stored = battle.Potions[slot];
            if (stored == 0 || !BlitzRegistry.Potion((ushort)(stored - 1)).Searchable)
                continue;
            switch (rules.Potion((ushort)(stored - 1)).Target)
            {
                case BlitzTargetType.AnyEnemy:
                    for (int position = 0; position < battle.RosterCount; position++)
                    {
                        byte owner = EnemyOwner(battle.Roster[position]);
                        if (IsHittable(battle, owner))
                            output[count++] = BlitzAction.UsePotion(slot, owner);
                    }
                    break;
                case BlitzTargetType.AnyPlayer or BlitzTargetType.Self:
                    if (battle.Player.IsAlive)
                        output[count++] = BlitzAction.UsePotion(slot, BlitzLimits.NoOwner);
                    break;
                case BlitzTargetType.AllEnemies or BlitzTargetType.TargetedNoCreature:
                    output[count++] = BlitzAction.UsePotion(slot, BlitzLimits.NoOwner);
                    break;
            }
        }
        output[count++] = BlitzAction.EndTurn();
        return count;
    }

    /// <summary>Upper bound of <see cref="LegalActions"/> output.</summary>
    public const int MaxLegalActions = BlitzLimits.MaxHandSize * BlitzLimits.Enemies + BlitzLimits.PotionSlots * BlitzLimits.Enemies + 1;
}
