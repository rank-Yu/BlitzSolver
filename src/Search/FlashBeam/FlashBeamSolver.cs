using System.Diagnostics;
using CombatSolver.Engine.InCombat.Flash;

namespace CombatSolver.FlashBeam;

internal sealed record FlashBeamOptions(
    int BeamWidth,
    int MaxExpandedNodes,
    int MaxTurns,
    TimeSpan SoftTimeBudget,
    int InitialPlayerMaxHp)
{
    /// <summary>Share of each beam layer reserved for the most offensive states (lowest enemy HP).</summary>
    public double OffensiveShare { get; init; } = 0.25;

    /// <summary>Which root potions may be used and what each use costs; disabled unless the caller sets it.</summary>
    public FlashPotionPolicy Potions { get; init; } = FlashPotionPolicy.Disabled;

    /// <summary>
    /// The ported existing-engine evaluation. Set by <see cref="FlashRouteMaterializer"/> from
    /// <see cref="FlashScoreBridge"/>; a caller that supplies it is responsible for the same root.
    /// </summary>
    public FlashScorer? Scorer { get; init; }

    /// <summary>Root values the final route ordering reads; set together with <see cref="Scorer"/>.</summary>
    public FlashRouteOrdering? Ordering { get; init; }

    /// <summary>The acceptable battle HP-loss stop; null when the request cannot stop at an HP target.</summary>
    public FlashHpTargetStop? HpTargetStop { get; init; }

    /// <summary>
    /// The ported incumbent lower bound; null or disabled means the root never prunes. Set together with
    /// <see cref="Incumbent"/> by <see cref="FlashRouteMaterializer"/>.
    /// </summary>
    public FlashBoundFeatures? Bound { get; init; }

    /// <summary>The request's initial incumbent, already translated to Flash player-turn units.</summary>
    public PrimarySearchIncumbent? Incumbent { get; init; }

    /// <summary>The existing primary member's effective potion policy, used to qualify incumbent tightening.</summary>
    public SolverPotionPolicy EffectivePotionPolicy { get; init; } = SolverPotionPolicy.Smart;

    /// <summary>The request's minimum explicit potion uses; the primary member passes 0.</summary>
    public int MinimumPotionUses { get; init; }

    /// <summary>
    /// Actions already executed before the search root (a fixed prefix). They are added to every
    /// node's action count so <c>Score</c>, the route key and the complete-victory checks see the
    /// same count as the existing solver, which starts counting at the request root.
    /// </summary>
    public int InitialActionCount { get; init; }
}

/// <summary>
/// The request values of the existing search's HP-target stop (<c>CombatBeamSolver.SolveCore</c>,
/// <c>MeetsHpTarget</c>): a complete victory without death saves, with at most
/// <paramref name="EarlyStopPotionUses"/> explicit potion uses and with
/// <paramref name="HpLostSoFar"/> plus its cumulative HP loss within
/// <paramref name="AcceptableBattleHpLoss"/>, ends the search before the next parent.
/// </summary>
internal sealed record FlashHpTargetStop(int AcceptableBattleHpLoss, int HpLostSoFar, int EarlyStopPotionUses);

/// <summary>The root values <c>FinalPlanOrdering.Select</c> reads for the route keys FlashBeam ports.</summary>
internal sealed record FlashRouteOrdering(
    int InitialPlayerHp,
    int InitialPlayerMaxHp,
    BossHpRelief BossHpRelief,
    PostCombatRelicHealProfile PostCombatRelicHeal);

/// <summary>
/// The existing search's potion constraints at the root (<c>EnumeratePlannedPotionActions</c>): slots whose
/// potion may be used explicitly, the earliest player turn, the maximum number of uses, and each slot's
/// strategic HP cost (<c>PotionUsePolicy.StrategicHpCost</c>). Only the potions present at the root are
/// covered; a potion gained during the search is not offered.
/// </summary>
internal sealed record FlashPotionPolicy(byte AllowedSlots, int EarliestPlayerTurn, int? MaximumUses, int[] SlotCosts)
{
    public static FlashPotionPolicy Disabled { get; } = new(0, int.MaxValue, 0, new int[FlashLimits.PotionSlots]);
}

internal sealed record FlashBeamResult(
    FlashAction[] BestRoute,
    bool BestIsVictory,
    bool BestIsDefeat,
    double BestScore,
    int ExpandedNodes,
    int Transitions,
    int Duplicates,
    int TurnsSearched,
    int PrunedIncumbents,
    TimeSpan Elapsed)
{
    /// <summary>Children dropped because their state exceeded a Flash capacity (a search boundary).</summary>
    public int CapacityPruned { get; init; }
}

/// <summary>
/// Turn-layered beam search over <see cref="FlashBattle"/>. Within a turn it expands card plays
/// depth by depth; end-turn children seed the next turn layer. Children are deduplicated by full
/// byte equality and the best score is kept. Only <see cref="FlashBattle"/> and
/// <see cref="FlashRules"/> are touched; translation and materialization happen in the bridge.
/// </summary>
[System.Runtime.CompilerServices.SkipLocalsInit]
internal sealed class FlashBeamSolver
{
    /// <summary>Selections explored per choice request (lexicographic within each size).</summary>
    private const int MaxChoiceBranches = 32;

    private readonly FlashRules _rules;
    private readonly FlashBeamOptions _options;
    private readonly FlashScorer _scorer;
    private readonly FlashRouteOrdering _ordering;
    private readonly List<(int Parent, int Depth, FlashAction Action, int Lineage)> _nodes = new(4096);
    private readonly FlashAction[] _actions = new FlashAction[FlashKernel.MaxLegalActions];
    private readonly ushort[] _rootPotions = new ushort[FlashLimits.PotionSlots];
    private readonly FlashBoundFeatures? _bound;
    private readonly bool _pruneEqualHp;
    private readonly bool _preservePotionCostOpportunities;
    private readonly SolverPotionPolicy _effectivePotionPolicy;
    private PrimarySearchIncumbent? _incumbent;
    private int _expanded;
    private int _transitions;
    private int _duplicates;
    private int _capacityPruned;
    private int _pruned;

    public FlashBeamSolver(FlashRules rules, FlashBeamOptions options)
    {
        _rules = rules;
        _options = options;
        _scorer = options.Scorer
            ?? throw new InvalidOperationException("FlashBeamOptions.Scorer 未设置。");
        _ordering = options.Ordering
            ?? throw new InvalidOperationException("FlashBeamOptions.Ordering 未设置。");
        _bound = options.Bound is { Enabled: true } ? options.Bound : null;
        _pruneEqualHp = _bound?.PruneEqualHp ?? false;
        _preservePotionCostOpportunities = _bound?.RequireCertifiedHealingProof ?? false;
        _effectivePotionPolicy = options.EffectivePotionPolicy;
        _incumbent = _bound == null ? null : options.Incumbent;
    }

    public FlashBeamResult Solve(in FlashBattle root, CancellationToken cancellationToken)
    {
        Stopwatch watch = Stopwatch.StartNew();
        for (int slot = 0; slot < FlashLimits.PotionSlots; slot++)
            _rootPotions[slot] = slot < root.PotionSlotCount ? root.Potions[slot] : (ushort)0;
        _nodes.Clear();
        _nodes.Add((-1, Math.Max(0, _options.InitialActionCount), default, -1));
        int width = Math.Max(1, _options.BeamWidth);
        // current and turnFrontier only receive CopyTopTo(width) and the root. next and ended are bounded
        // by every kept state expanding every legal action, but a layer holds width x (actual legal actions),
        // so they start small and grow toward that bound.
        int maxChildren = checked(width * (FlashKernel.MaxLegalActions + 1));
        int initialChildren = Math.Min(maxChildren, Math.Max(64, width * 8));
        Pool current = new(width, width);
        Pool next = new(initialChildren, maxChildren);
        Pool ended = new(initialChildren, maxChildren);
        Pool turnFrontier = new(width, width);

        RouteKey? best = null;
        int bestNode = 0;
        bool bestVictory = false, bestDefeat = false;
        void Consider(in FlashBattle state, int node, in RouteKey key)
        {
            if (best is { } current && CompareRoutes(key, current) >= 0)
                return;
            best = key;
            bestNode = node;
            bestVictory = state.Outcome == FlashOutcome.Victory;
            bestDefeat = state.Outcome == FlashOutcome.Defeat;
        }
        // The existing search's two early stops. The HP-target stop ends the search before the next parent
        // (CombatBeamSolver.Phases.cs, MeetsHpTarget); the zero-damage stop ends it after the current turn
        // once a victory lost no HP, used no explicit potion and left the player at full, undiminished HP
        // (growth targets, which disable it there, are rejected at admission).
        bool hpTargetReached = false, zeroDamageVictory = false;
        void ConsiderTerminal(in FlashBattle state, int node, double score)
        {
            int actionCount = Depth(node);
            FlashScoreFeatures features = _scorer.Features(state, actionCount);
            RouteKey key = Key(state, features, actionCount);
            Consider(state, node, key);
            TightenIncumbent(state, features, key);
            if (!features.AllEnemiesDead)
                return;
            int explicitPotionUses = ExplicitPotionUses(node);
            if (key.Won && _options.HpTargetStop is { } stop
                && features.ProjectedDeathSaveUseCount == 0
                && explicitPotionUses <= stop.EarlyStopPotionUses
                && stop.HpLostSoFar + features.CumulativePlayerHpLost <= stop.AcceptableBattleHpLoss)
            {
                hpTargetReached = true;
            }
            if (explicitPotionUses == 0
                && features.CumulativePlayerHpLost == 0
                && features.PlayerMaxHp >= _options.InitialPlayerMaxHp
                && features.PlayerHp >= features.PlayerMaxHp)
            {
                zeroDamageVictory = true;
            }
        }

        (double rootScore, double rootRank, int rootPotionCost, RetentionFacts rootFacts) =
            Score(root, _options.InitialActionCount);
        turnFrontier.Add(root, 0, rootScore, rootRank, rootPotionCost, rootFacts, -1);
        int turns = 0;
        while (turnFrontier.Count > 0 && turns < _options.MaxTurns && !OutOfBudget(watch) && !hpTargetReached)
        {
            turns++;
            current.Clear();
            turnFrontier.CopyTopTo(current, width);
            ended.Clear();
            while (current.Count > 0 && !OutOfBudget(watch) && !hpTargetReached)
            {
                next.Clear();
                for (int index = 0; index < current.Count && !OutOfBudget(watch) && !hpTargetReached; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _expanded++;
                    ref FlashBattle parent = ref current.State(index);
                    int parentNode = current.Node(index);
                    int count = FlashKernel.LegalActions(parent, _rules, _actions);
                    for (int actionIndex = 0; actionIndex < count; actionIndex++)
                    {
                        if (_actions[actionIndex].Kind == FlashActionKind.UsePotion && !PotionAllowed(parent, _actions[actionIndex].PotionSlot))
                            continue;
                        ExpandAction(ref parent, parentNode, _actions[actionIndex], next, ended, ConsiderTerminal);
                    }
                }
                current.Clear();
                next.CopyTopTo(current, width, _options.OffensiveShare);
                PrunePool(current);
            }
            turnFrontier.Clear();
            ended.CopyTopTo(turnFrontier, width, _options.OffensiveShare);
            PrunePool(turnFrontier);
            if (zeroDamageVictory)
                break;
        }

        // Without a victory, the surviving turn-start states compete with the terminal routes under the
        // same final ordering (the existing search evaluates its boundary nodes alongside terminals).
        if (!bestVictory)
        {
            for (int index = 0; index < turnFrontier.Count; index++)
            {
                int node = turnFrontier.Node(index);
                int actionCount = Depth(node);
                ref FlashBattle state = ref turnFrontier.State(index);
                Consider(state, node, Key(state, _scorer.Features(state, actionCount), actionCount));
            }
        }
        return new FlashBeamResult(Route(bestNode), bestVictory, bestDefeat, best?.Score ?? double.NegativeInfinity,
            _expanded, _transitions, _duplicates, turns, _pruned, watch.Elapsed) { CapacityPruned = _capacityPruned };
    }

    private delegate void TerminalSink(in FlashBattle state, int node, double score);

    /// <summary>
    /// The keys of the existing final ordering (<c>FinalPlanOrdering.Select</c>) that the admitted requests
    /// reach. Growth and relic-counter credits are zero because their settings are rejected at admission;
    /// the theft key and the sold-HP and boundary tie-breakers are not ported yet.
    /// </summary>
    private readonly record struct RouteKey(
        bool Won,
        bool Survives,
        int DeathSaveUseCount,
        int StrategicHpDeficit,
        int CombatEndedTurn,
        int HealthResourceCost,
        int LongTermResourceValue,
        int AngerCopiesGenerated,
        int OptionalPotionCount,
        int EnemyHp,
        double Score,
        int ActionCount);

    private RouteKey Key(in FlashBattle state, in FlashScoreFeatures features, int actionCount)
    {
        bool won = SolverInterimResultOrdering.IsCompleteVictory(
            actionCount, features.AllEnemiesDead, features.PlayerDead, features.ProjectedPlayerHp);
        int relicHeal = ActEndingBossPolicy.RankedPostCombatRelicHeal(
            _ordering.PostCombatRelicHeal, won, features.PlayerHp, features.PlayerMaxHp);
        int strategicHpDeficit = ActEndingBossPolicy.StrategicHpDeficit(
            features.CumulativePlayerHpLost,
            Math.Max(0, _ordering.InitialPlayerMaxHp - features.PlayerMaxHp),
            features.RecoveredPlayerHp + relicHeal,
            _ordering.BossHpRelief,
            features.DeathSaveHpRestored) - features.GrowthHpCredit;
        return new RouteKey(
            won,
            !features.PlayerDead && features.ProjectedPlayerHp > 0,
            features.ProjectedDeathSaveUseCount,
            strategicHpDeficit,
            won ? state.PlayerTurn : int.MaxValue,
            _ordering.InitialPlayerHp - features.PlayerHp + _ordering.InitialPlayerMaxHp - features.PlayerMaxHp,
            features.LongTermResourceValue,
            features.AngerCopiesGenerated,
            features.PotionUseCount - features.AutomaticPotionUseCount,
            features.EnemyHp,
            features.Score,
            actionCount);
    }

    /// <summary>Negative when <paramref name="left"/> is the better route.</summary>
    private static int CompareRoutes(in RouteKey left, in RouteKey right)
    {
        int comparison = right.Won.CompareTo(left.Won);
        if (comparison != 0) return comparison;
        comparison = (!left.Won && !left.Survives ? 1 : 0).CompareTo(!right.Won && !right.Survives ? 1 : 0);
        if (comparison != 0) return comparison;
        comparison = left.DeathSaveUseCount.CompareTo(right.DeathSaveUseCount);
        if (comparison != 0) return comparison;
        comparison = left.StrategicHpDeficit.CompareTo(right.StrategicHpDeficit);
        if (comparison != 0) return comparison;
        comparison = left.CombatEndedTurn.CompareTo(right.CombatEndedTurn);
        if (comparison != 0) return comparison;
        comparison = left.HealthResourceCost.CompareTo(right.HealthResourceCost);
        if (comparison != 0) return comparison;
        comparison = right.LongTermResourceValue.CompareTo(left.LongTermResourceValue);
        if (comparison != 0) return comparison;
        comparison = left.AngerCopiesGenerated.CompareTo(right.AngerCopiesGenerated);
        if (comparison != 0) return comparison;
        comparison = left.OptionalPotionCount.CompareTo(right.OptionalPotionCount);
        if (comparison != 0) return comparison;
        comparison = left.EnemyHp.CompareTo(right.EnemyHp);
        if (comparison != 0) return comparison;
        comparison = right.Score.CompareTo(left.Score);
        return comparison != 0 ? comparison : left.ActionCount.CompareTo(right.ActionCount);
    }

    private void ExpandAction(ref FlashBattle parent, int parentNode, FlashAction action, Pool next, Pool ended, TerminalSink terminal)
    {
        // The child is built in next's first free slot and stepped in place: one copy of the parent per
        // transition. It is committed to next, copied to ended, or dropped as a terminal; the slot is not
        // touched by anything else until then (a choice re-expansion reuses it from the start).
        ref FlashBattle child = ref next.Reserve();
        child = parent;
        _transitions++;
        FlashStepResult stepResult;
        FlashChoiceRequest request;
        try
        {
            stepResult = FlashKernel.Step(ref child, action, _rules, out request);
        }
        catch (FlashCapacityException)
        {
            // A search boundary like the node budget: this child needs more cards, powers or enemies than
            // FlashBattle holds, so it is dropped (the reserved slot stays free). Every adopted route is
            // still verified step by step against the existing engine.
            _capacityPruned++;
            return;
        }
        if (stepResult == FlashStepResult.NeedsChoice)
        {
            // Every executed selection is exact; the cap only bounds how many are explored.
            Span<ulong> selections = stackalloc ulong[MaxChoiceBranches];
            int count = FlashChoiceEnumerator.Enumerate(request, selections);
            for (int index = 0; index < count; index++)
                ExpandAction(ref parent, parentNode, action.WithChoice(selections[index]), next, ended, terminal);
            return;
        }
        int node = _nodes.Count;
        int depth = _nodes[parentNode].Depth + 1;
        // The existing search's root-action lineage groups every descendant by the first action
        // taken from the request root (BeamRetentionPolicy.BuildRootActionLineageSignature).
        int lineage = parentNode == 0 ? node : _nodes[parentNode].Lineage;
        _nodes.Add((parentNode, depth, action, lineage));
        (double score, double rank, int potionCost, RetentionFacts facts) = Score(child, depth);
        if (!FlashKernel.IsInProgress(child))
        {
            terminal(child, node, score);
            return;
        }
        bool added = action.Kind == FlashActionKind.EndTurn || child.PlayerTurn != parent.PlayerTurn
            ? ended.Add(child, node, score, rank, potionCost, facts, lineage)
            : next.CommitReserved(node, score, rank, potionCost, facts, lineage);
        if (!added)
            _duplicates++;
    }

    private bool OutOfBudget(Stopwatch watch)
        => _expanded >= _options.MaxExpandedNodes || watch.Elapsed >= _options.SoftTimeBudget;

    private int Depth(int node) => _nodes[node].Depth;

    private int ExplicitPotionUses(int node)
    {
        int uses = 0;
        for (int cursor = node; _nodes[cursor].Parent >= 0; cursor = _nodes[cursor].Parent)
        {
            if (_nodes[cursor].Action.Kind == FlashActionKind.UsePotion)
                uses++;
        }
        return uses;
    }

    private FlashAction[] Route(int node)
    {
        List<FlashAction> route = [];
        for (int cursor = node; _nodes[cursor].Parent >= 0; cursor = _nodes[cursor].Parent)
            route.Add(_nodes[cursor].Action);
        route.Reverse();
        return [.. route];
    }

    /// <summary>
    /// The existing primary member's terminal-victory tightening
    /// (<c>TightenPrimarySearchIncumbentAtTurnLayer</c>): a complete, risk-free, potion-free victory
    /// publishes a strictly better incumbent at the commit boundary, so the same layer's retention can
    /// use it. The shared <see cref="PrimaryIncumbentTable"/> is not connected.
    /// </summary>
    private void TightenIncumbent(in FlashBattle state, in FlashScoreFeatures features, in RouteKey key)
    {
        if (_bound == null || !key.Won || features.ProjectedDeathSaveUseCount > 0)
            return;
        if (_options.MinimumPotionUses != 0)
            return;
        if (_effectivePotionPolicy is not (SolverPotionPolicy.Disabled or SolverPotionPolicy.Smart))
            return;
        int explicitPotionUses = features.PotionUseCount - features.AutomaticPotionUseCount;
        if (explicitPotionUses != _options.MinimumPotionUses)
            return;
        int strategicHpDeficit = ActEndingBossPolicy.StrategicHpDeficit(
            features.CumulativePlayerHpLost,
            Math.Max(0, _options.InitialPlayerMaxHp - features.PlayerMaxHp),
            features.RecoveredPlayerHp + ActEndingBossPolicy.RankedPostCombatRelicHeal(
                _ordering.PostCombatRelicHeal, true, features.PlayerHp, features.PlayerMaxHp),
            _bound.BossHpRelief,
            features.DeathSaveHpRestored) - features.GrowthHpCredit;
        PrimarySearchIncumbent? candidate = _incumbent;
        if (CombatBeamSolver.TryTightenPrimarySearchIncumbent(
                auditedPotionFreeBaseline: null,
                minimumPotionUses: _options.MinimumPotionUses,
                maximumPotionUses: null,
                candidateCompleteVictory: true,
                candidateSatisfiesHardRules: !_bound.HasRisk(state),
                candidateExplicitPotionUses: explicitPotionUses,
                candidateStrategicHpDeficit: strategicHpDeficit,
                candidateCombatEndedTurn: state.PlayerTurn,
                ref candidate,
                effectivePotionPolicy: _effectivePotionPolicy,
                candidateDeathSaveUseCount: features.ProjectedDeathSaveUseCount,
                candidateExplicitPotionStrategicCost: features.ExplicitPotionStrategicCost))
        {
            _incumbent = candidate;
        }
    }

    /// <summary>
    /// The existing <c>ApplyPrimaryIncumbentBound</c> local path. It runs after the ranked retention
    /// (action layer and turn boundary) and compacts the pool; removed seats are deliberately not refilled.
    /// </summary>
    private void PrunePool(Pool pool)
    {
        if (_bound == null || _incumbent is not { } incumbent)
            return;
        int write = 0;
        for (int read = 0; read < pool.Count; read++)
        {
            FlashBoundValues values = _bound.Evaluate(pool.State(read), pool.PotionCost(read));
            bool prune;
            if (_bound.RequireCertifiedHealingProof && _pruneEqualHp
                && values.RemainingHealingPotential == int.MaxValue)
            {
                // A certified equal-HP proof needs the branch's own certificate; keep the node.
                prune = false;
            }
            else
            {
                bool costClosed = !_preservePotionCostOpportunities
                    || (incumbent.ExplicitPotionStrategicCost is { } cost
                        && values.ExplicitPotionStrategicCost >= cost);
                prune = CombatBeamSolver.ShouldPruneByPrimaryIncumbent(
                    values.StrategicHpLowerBound,
                    pool.State(read).PlayerTurn,
                    incumbent,
                    allowTurnTieBound: costClosed,
                    pruneEqualHp: _pruneEqualHp && !values.HasRisk && costClosed);
            }
            if (prune)
            {
                _pruned++;
                continue;
            }
            if (write != read)
                pool.Move(read, write);
            write++;
        }
        pool.Truncate(write);
    }

    /// <summary>
    /// The node's final score and its intermediate ranking value. The final score is the ported
    /// <see cref="FlashScorer.Features"/> score; the ranking value is
    /// <see cref="FlashScorer.BeamRankScore"/> minus the strategic HP cost of the potions this route has
    /// spent. The potion term is Flash-specific: the existing snapshot score does not include potion cost,
    /// so without it the beam would spend potions for free.
    /// </summary>
    private (double Score, double Rank, int PotionCost, RetentionFacts Facts) Score(in FlashBattle battle, int actionCount)
    {
        FlashScoreFeatures features = _scorer.Features(battle, actionCount);
        double rank = _scorer.BeamRankScore(features)
            - UsedPotionCost(battle) * SolverWeights.Hp;
        return (features.Score, rank, features.ExplicitPotionStrategicCost,
            RetentionFacts.From(features, actionCount));
    }

    /// <summary>A slot still holds its root potion and the policy offers it now.</summary>
    private bool PotionAllowed(in FlashBattle battle, int slot)
    {
        FlashPotionPolicy policy = _options.Potions;
        if ((policy.AllowedSlots & (1 << slot)) == 0 || battle.PlayerTurn < policy.EarliestPlayerTurn)
            return false;
        if (battle.Potions[slot] == 0 || battle.Potions[slot] != _rootPotions[slot])
            return false;
        if (policy.MaximumUses is { } maximum && UsedPotions(battle) >= maximum)
            return false;
        return true;
    }

    private int UsedPotions(in FlashBattle battle)
    {
        int used = 0;
        for (int slot = 0; slot < FlashLimits.PotionSlots; slot++)
        {
            if (_rootPotions[slot] != 0 && (slot >= battle.PotionSlotCount || battle.Potions[slot] != _rootPotions[slot]))
                used++;
        }
        return used;
    }

    private int UsedPotionCost(in FlashBattle battle)
    {
        int cost = 0;
        for (int slot = 0; slot < FlashLimits.PotionSlots; slot++)
        {
            if (_rootPotions[slot] != 0 && (slot >= battle.PotionSlotCount || battle.Potions[slot] != _rootPotions[slot]))
                cost += _options.Potions.SlotCosts[slot];
        }
        return cost;
    }

    /// <summary>
    /// The evaluation values the ported retention comparators read. Every member is already frozen by
    /// <see cref="FlashScorer.Features"/> and checked field by field against the existing snapshot by K4,
    /// so retention does not re-evaluate a state or introduce a second scoring path.
    /// </summary>
    private readonly record struct RetentionFacts(
        int Depth,
        int PotionCount,
        int ProjectedPlayerHp,
        int DefensiveBlockValue,
        int AliveEnemyCount,
        int RawEnemyHp,
        int EnemyHp,
        int RetainedAttackValue,
        int SetupValue,
        int SetupComboValue,
        int LiveDeckSize,
        int LiveDeckClutter,
        long AttackDensity,
        int EnemyStrengthSuppression,
        int EnemyWeakTurns,
        int OffensiveProgressValue,
        double Score)
    {
        public static RetentionFacts From(in FlashScoreFeatures features, int depth) => new(
            depth,
            features.PotionUseCount,
            features.ProjectedPlayerHp,
            features.DefensiveBlockValue,
            features.AliveEnemyCount,
            features.RawEnemyHp,
            features.EnemyHp,
            features.RetainedAttackValue,
            // BeamRetentionPolicy.SetupLaneValue: StrategicEffects.RetentionValue * 16
            // + LatentSetupValue * 8 + ReplayPotentialValue * 16 + FutureResourceValue.
            // FlashScoreFeatures.PersistentBuffValue is the strategic retention value (K4-checked).
            features.PersistentBuffValue * 16
                + features.LatentSetupValue * 8
                + features.ReplayPotentialValue * 16
                + features.FutureResourceValue,
            // BeamRetentionPolicy.FindBestSetup: Scaling + Resource + Control lanes. The resource
            // lane's Stars (always 0 for an admitted Ironclad root) and ReachableHandValue (no Flash
            // feature yet) are omitted; every other term is the existing formula.
            features.PersistentBuffValue * 16
                + features.LatentSetupValue * 8
                + features.ReplayPotentialValue * 16
                + features.FutureResourceValue
                + features.DelayedDamageValue
                + features.Energy * 16
                + features.HandCount
                + features.FutureResourceValue
                + features.EnemyStrengthSuppression * 32
                + features.EnemyWeakTurns * 8
                + features.FocusTargetVulnerableTurns * 4
                    * Math.Min(SolverWeights.VulnerableAttackWindowCap, features.RetainedAttackValue)
                + Math.Max(0, features.EnemyVulnerableTurns - features.FocusTargetVulnerableTurns)
                    * Math.Min(SolverWeights.VulnerableAttackWindowCap, features.RetainedAttackValue)
                + features.DelayedDamageValue
                - features.LiveDeckClutter * 8,
            features.LiveDeckSize,
            features.LiveDeckClutter,
            (long)features.RetainedAttackValue * 1024 / Math.Max(1, features.LiveDeckSize),
            features.EnemyStrengthSuppression,
            features.EnemyWeakTurns,
            features.OffensiveProgressValue,
            features.Score);
    }

    // The leader comparators below are the existing BeamRetentionPolicy.Ranking comparators with the
    // same tie order. The Osty terms are constants here: Flash admission rejects roots that carry Osty,
    // so SimulatedCombatState.GetOsty returns null and both values are 0 for every admitted state.
    private static bool IsBetterDefensive(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.ProjectedPlayerHp > current.ProjectedPlayerHp
            || candidate.ProjectedPlayerHp == current.ProjectedPlayerHp
                && (candidate.DefensiveBlockValue > current.DefensiveBlockValue
                    || candidate.DefensiveBlockValue == current.DefensiveBlockValue
                        && candidate.Score > current.Score);

    private static bool IsBetterOffensive(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.AliveEnemyCount < current.AliveEnemyCount
            || candidate.AliveEnemyCount == current.AliveEnemyCount
                && (candidate.RawEnemyHp < current.RawEnemyHp
                    || candidate.RawEnemyHp == current.RawEnemyHp
                        && (candidate.EnemyHp < current.EnemyHp
                            || candidate.EnemyHp == current.EnemyHp
                                && (candidate.ProjectedPlayerHp > current.ProjectedPlayerHp
                                    || candidate.ProjectedPlayerHp == current.ProjectedPlayerHp
                                        && candidate.Score > current.Score)));

    private static bool IsBetterSetup(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.SetupValue > current.SetupValue
            || candidate.SetupValue == current.SetupValue
                && (candidate.RetainedAttackValue > current.RetainedAttackValue
                    || candidate.RetainedAttackValue == current.RetainedAttackValue
                        && (candidate.ProjectedPlayerHp > current.ProjectedPlayerHp
                            || candidate.ProjectedPlayerHp == current.ProjectedPlayerHp
                                && candidate.Score > current.Score));

    private static bool IsBetterSetupCombo(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.SetupComboValue > current.SetupComboValue
            || candidate.SetupComboValue == current.SetupComboValue
                && (candidate.RetainedAttackValue > current.RetainedAttackValue
                    || candidate.RetainedAttackValue == current.RetainedAttackValue
                        && (candidate.ProjectedPlayerHp > current.ProjectedPlayerHp
                            || candidate.ProjectedPlayerHp == current.ProjectedPlayerHp
                                && candidate.Score > current.Score));

    private static bool IsBetterDeckCuration(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.AttackDensity > current.AttackDensity
            || candidate.AttackDensity == current.AttackDensity
                && (candidate.LiveDeckClutter < current.LiveDeckClutter
                    || candidate.LiveDeckClutter == current.LiveDeckClutter
                        && IsBetterSetup(candidate, current));

    private static bool IsBetterCompressedDeck(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.LiveDeckSize < current.LiveDeckSize
            || candidate.LiveDeckSize == current.LiveDeckSize
                && (candidate.AttackDensity > current.AttackDensity
                    || candidate.AttackDensity == current.AttackDensity
                        && IsBetterSetup(candidate, current));

    private static bool IsBetterUtilityDefensive(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.ProjectedPlayerHp > current.ProjectedPlayerHp
            || candidate.ProjectedPlayerHp == current.ProjectedPlayerHp
                && candidate.Score > current.Score;

    private static bool IsBetterEnemyStrengthControl(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.EnemyStrengthSuppression > current.EnemyStrengthSuppression
            || candidate.EnemyStrengthSuppression == current.EnemyStrengthSuppression
                && (candidate.EnemyWeakTurns > current.EnemyWeakTurns
                    || candidate.EnemyWeakTurns == current.EnemyWeakTurns
                        && IsBetterDefensive(candidate, current));

    private static bool IsBetterEnemyWeakControl(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.EnemyWeakTurns > current.EnemyWeakTurns
            || candidate.EnemyWeakTurns == current.EnemyWeakTurns
                && (candidate.EnemyStrengthSuppression > current.EnemyStrengthSuppression
                    || candidate.EnemyStrengthSuppression == current.EnemyStrengthSuppression
                        && IsBetterDefensive(candidate, current));

    private sealed class LineageLeaders
    {
        public int Leader = -1;
        public int Defensive = -1;
        public int Offensive = -1;
        public int Setup = -1;
        public int SetupCombo = -1;
        public int Curation = -1;
        public int Compressed = -1;
    }

    private sealed class PotionLeaders
    {
        public int Leader = -1;
        public int Defensive = -1;
        public int Offensive = -1;
        public int Setup = -1;
        public int SetupCombo = -1;
        public int Curation = -1;
        public int Compressed = -1;
        public int Strength = -1;
        public int Weak = -1;
    }

    /// <summary>
    /// State pool with byte-exact deduplication keeping the best score. It doubles on demand up to
    /// <c>maxCapacity</c>; exceeding that bound is a capacity rejection.
    /// </summary>
    private sealed class Pool
    {
        private readonly int _maxCapacity;
        private FlashBattle[] _states;
        private int[] _nodes;
        private int[] _lineages;
        private double[] _scores;
        private double[] _ranks;
        private ulong[] _hashes;
        private RetentionFacts[] _facts;
        private int[] _table;
        private int[] _order;
        private int[] _rankOrder;
        private int[] _rankPosition;
        private int[] _potionCosts;
        private bool[] _taken;
        private bool[] _required;
        private readonly List<int> _selected = new();
        private readonly List<int> _requiredScratch = new();
        private readonly Dictionary<int, LineageLeaders> _lineageLeaders = new();
        private readonly List<int> _lineageOrder = new();
        private readonly Dictionary<int, PotionLeaders> _potionLeaders = new();

        public Pool(int initialCapacity, int maxCapacity)
        {
            _maxCapacity = maxCapacity;
            int capacity = Math.Clamp(initialCapacity, 1, maxCapacity);
            _states = new FlashBattle[capacity];
            _nodes = new int[capacity];
            _lineages = new int[capacity];
            _scores = new double[capacity];
            _ranks = new double[capacity];
            _hashes = new ulong[capacity];
            _facts = new RetentionFacts[capacity];
            _order = new int[capacity];
            _rankOrder = new int[capacity];
            _rankPosition = new int[capacity];
            _potionCosts = new int[capacity];
            _taken = new bool[capacity];
            _required = new bool[capacity];
            _table = new int[TableSize(capacity)];
        }

        private static int TableSize(int capacity)
        {
            int size = 1;
            while (size < capacity * 2)
                size <<= 1;
            return size;
        }

        private void Grow()
        {
            if (_states.Length >= _maxCapacity)
                throw new FlashCapacityException("FlashBeam.Pool", _maxCapacity);
            int capacity = (int)Math.Min(_maxCapacity, (long)_states.Length * 2);
            Array.Resize(ref _states, capacity);
            Array.Resize(ref _nodes, capacity);
            Array.Resize(ref _lineages, capacity);
            Array.Resize(ref _scores, capacity);
            Array.Resize(ref _ranks, capacity);
            Array.Resize(ref _hashes, capacity);
            Array.Resize(ref _facts, capacity);
            Array.Resize(ref _order, capacity);
            Array.Resize(ref _rankOrder, capacity);
            Array.Resize(ref _rankPosition, capacity);
            Array.Resize(ref _potionCosts, capacity);
            Array.Resize(ref _taken, capacity);
            Array.Resize(ref _required, capacity);
            _table = new int[TableSize(capacity)];
            int mask = _table.Length - 1;
            for (int index = 0; index < Count; index++)
                _table[FreeSlot(_hashes[index], mask)] = index + 1;
        }

        private int FreeSlot(ulong hash, int mask)
        {
            int slot = (int)(hash & (ulong)mask);
            while (_table[slot] != 0)
                slot = (slot + 1) & mask;
            return slot;
        }

        public int Count { get; private set; }

        public ref FlashBattle State(int index) => ref _states[index];

        public int Node(int index) => _nodes[index];

        public double Score(int index) => _scores[index];

        public double Rank(int index) => _ranks[index];

        public int PotionCost(int index) => _potionCosts[index];

        /// <summary>Copies one retained entry to a lower slot; used by the incumbent compaction.</summary>
        public void Move(int from, int to)
        {
            _states[to] = _states[from];
            _nodes[to] = _nodes[from];
            _lineages[to] = _lineages[from];
            _scores[to] = _scores[from];
            _ranks[to] = _ranks[from];
            _hashes[to] = _hashes[from];
            _facts[to] = _facts[from];
            _potionCosts[to] = _potionCosts[from];
        }

        /// <summary>Drops the tail after an incumbent bound; removed seats are not refilled.</summary>
        public void Truncate(int count)
        {
            Count = count;
            Array.Clear(_table);
            int mask = _table.Length - 1;
            for (int index = 0; index < count; index++)
                _table[FreeSlot(_hashes[index], mask)] = index + 1;
        }

        public void Clear()
        {
            Count = 0;
            Array.Clear(_table);
        }

        /// <summary>
        /// The first free slot, for building a state in place before <see cref="CommitReserved"/>. Any
        /// other call on this pool invalidates the returned reference.
        /// </summary>
        public ref FlashBattle Reserve()
        {
            if (Count >= _states.Length)
                Grow();
            return ref _states[Count];
        }

        /// <summary>Adds the state built in the reserved slot; returns false when an identical state was already present.</summary>
        public bool CommitReserved(int node, double score, double rank, int potionCost,
            in RetentionFacts facts, int lineage)
            => Insert(ref _states[Count], reserved: true, node, score, rank, potionCost, facts, lineage);

        /// <summary>Adds a state; returns false when an identical state was already present.</summary>
        public bool Add(in FlashBattle state, int node, double score, double rank, int potionCost,
            in RetentionFacts facts, int lineage)
            => Insert(ref System.Runtime.CompilerServices.Unsafe.AsRef(in state), reserved: false,
                node, score, rank, potionCost, facts, lineage);

        private bool Insert(ref FlashBattle state, bool reserved, int node, double score, double rank,
            int potionCost, in RetentionFacts facts, int lineage)
        {
            ulong hash = FlashFingerprint.Hash(state);
            int mask = _table.Length - 1;
            int slot = (int)(hash & (ulong)mask);
            while (_table[slot] != 0)
            {
                int existing = _table[slot] - 1;
                if (_hashes[existing] == hash && FlashFingerprint.Equal(_states[existing], state))
                {
                    if (rank > _ranks[existing])
                    {
                        _scores[existing] = score;
                        _ranks[existing] = rank;
                        _nodes[existing] = node;
                        _potionCosts[existing] = potionCost;
                        _facts[existing] = facts;
                        _lineages[existing] = lineage;
                    }
                    return false;
                }
                slot = (slot + 1) & mask;
            }
            if (Count >= _states.Length)
            {
                Grow();
                slot = FreeSlot(hash, _table.Length - 1);
            }
            int index = Count++;
            if (!reserved)
                _states[index] = state;
            _nodes[index] = node;
            _lineages[index] = lineage;
            _scores[index] = score;
            _ranks[index] = rank;
            _hashes[index] = hash;
            _facts[index] = facts;
            _potionCosts[index] = potionCost;
            _table[slot] = index + 1;
            return true;
        }

        public void CopyTopTo(Pool target, int width) => CopyTopTo(target, width, 0d);

        /// <summary>
        /// Keeps the best <paramref name="width"/> states. The score leaders and the reserved offensive
        /// share are the same two lanes as before; on top of them the existing search's required
        /// representatives (per potion count, global defensive/offensive/setup leaders and the strongest
        /// root-action lineages) replace the last non-required score seat. The total width is unchanged:
        /// a representative takes a seat, it does not widen the beam.
        /// </summary>
        public void CopyTopTo(Pool target, int width, double offensiveShare)
        {
            int count = Count;
            if (count == 0)
                return;
            for (int index = 0; index < count; index++)
            {
                _order[index] = index;
                _taken[index] = false;
                _required[index] = false;
            }
            SortByRank(count);
            int keep = Math.Min(width, count);
            int offensive = (int)(keep * offensiveShare);
            int byScore = keep - offensive;
            List<int> selected = _selected;
            selected.Clear();
            for (int index = 0; index < byScore; index++)
            {
                int source = _order[index];
                selected.Add(source);
                _taken[source] = true;
            }
            if (offensive > 0)
            {
                SortByEnemyHp(count);
                int added = 0;
                for (int index = 0; index < count && added < offensive; index++)
                {
                    int source = _order[index];
                    if (_taken[source])
                        continue;
                    selected.Add(source);
                    _taken[source] = true;
                    added++;
                }
            }
            List<int> required = _requiredScratch;
            required.Clear();
            // Representatives take priority over the mass offensive lane: when a required leader is
            // not in the top seats it replaces the last non-required seat first (offensive seats are
            // appended last), so the score leaders and then the required set are preserved.
            CollectRepresentatives(required, count, width);
            // BeamRetentionPolicy.RankBest: every required candidate not already ranked replaces the
            // last ordinary seat, so all required members survive without widening the layer.
            foreach (int source in required)
            {
                if (_taken[source])
                    continue;
                int replaceIndex = -1;
                for (int index = selected.Count - 1; index >= 0; index--)
                {
                    if (_required[selected[index]])
                        continue;
                    replaceIndex = index;
                    break;
                }
                if (replaceIndex < 0)
                    break;
                _taken[selected[replaceIndex]] = false;
                selected[replaceIndex] = source;
                _taken[source] = true;
            }
            selected.Sort((left, right) => _rankPosition[left].CompareTo(_rankPosition[right]));
            for (int index = 0; index < selected.Count; index++)
            {
                int source = selected[index];
                target.Add(_states[source], _nodes[source], _scores[source], _ranks[source],
                    _potionCosts[source], _facts[source], _lineages[source]);
            }
        }

        private void AddRequired(List<int> required, int source, int width)
        {
            if (source < 0 || required.Count >= width || _required[source])
                return;
            _required[source] = true;
            required.Add(source);
        }

        /// <summary>
        /// The existing RankBest required set, restricted to the mechanisms whose inputs Flash already
        /// evaluates exactly: one leader set per potion count, the global defensive/offensive/setup
        /// leaders, and the strongest root-action lineages (clamp(width / 8, 4, 16)).
        /// </summary>
        private void CollectRepresentatives(List<int> required, int count, int width)
        {
            _lineageLeaders.Clear();
            _potionLeaders.Clear();
            int bestDefensive = -1, bestOffensive = -1, bestSetup = -1, bestUtilityDefensive = -1;
            int bestSetupCombo = -1, bestCuration = -1, bestCompressed = -1;
            int bestPotionFree = -1, bestPotionFreeDefensive = -1, bestPotionFreeOffensive = -1;
            int bestPotionFreeSetup = -1, bestPotionFreeSetupCombo = -1, bestPotionFreeUtilityDefensive = -1;
            int bestPotion = -1, bestPotionDefensive = -1, bestPotionOffensive = -1, bestPotionSetup = -1;
            int bestPotionSetupCombo = -1;
            for (int position = 0; position < count; position++)
            {
                int source = _rankOrder[position];
                RetentionFacts facts = _facts[source];
                int potionCount = facts.PotionCount;
                if (!_potionLeaders.TryGetValue(potionCount, out PotionLeaders? potion))
                {
                    potion = new PotionLeaders { Leader = source };
                    _potionLeaders.Add(potionCount, potion);
                }
                if (potion.Defensive < 0 || IsBetterDefensive(facts, _facts[potion.Defensive]))
                    potion.Defensive = source;
                if (potion.Offensive < 0 || IsBetterOffensive(facts, _facts[potion.Offensive]))
                    potion.Offensive = source;
                if (potion.Setup < 0 || IsBetterSetup(facts, _facts[potion.Setup]))
                    potion.Setup = source;
                if (potion.SetupCombo < 0 || IsBetterSetupCombo(facts, _facts[potion.SetupCombo]))
                    potion.SetupCombo = source;
                if (potion.Curation < 0 || IsBetterDeckCuration(facts, _facts[potion.Curation]))
                    potion.Curation = source;
                if (potion.Compressed < 0 || IsBetterCompressedDeck(facts, _facts[potion.Compressed]))
                    potion.Compressed = source;
                if (potion.Strength < 0 || IsBetterEnemyStrengthControl(facts, _facts[potion.Strength]))
                    potion.Strength = source;
                if (potion.Weak < 0 || IsBetterEnemyWeakControl(facts, _facts[potion.Weak]))
                    potion.Weak = source;

                int lineage = _lineages[source];
                if (!_lineageLeaders.TryGetValue(lineage, out LineageLeaders? line))
                {
                    line = new LineageLeaders { Leader = source };
                    _lineageLeaders.Add(lineage, line);
                }
                if (line.Defensive < 0 || IsBetterDefensive(facts, _facts[line.Defensive]))
                    line.Defensive = source;
                if (line.Offensive < 0 || IsBetterOffensive(facts, _facts[line.Offensive]))
                    line.Offensive = source;
                if (line.Setup < 0 || IsBetterSetup(facts, _facts[line.Setup]))
                    line.Setup = source;
                if (line.SetupCombo < 0 || IsBetterSetupCombo(facts, _facts[line.SetupCombo]))
                    line.SetupCombo = source;
                if (line.Curation < 0 || IsBetterDeckCuration(facts, _facts[line.Curation]))
                    line.Curation = source;
                if (line.Compressed < 0 || IsBetterCompressedDeck(facts, _facts[line.Compressed]))
                    line.Compressed = source;

                if (bestDefensive < 0 || IsBetterDefensive(facts, _facts[bestDefensive]))
                    bestDefensive = source;
                if (bestOffensive < 0 || IsBetterOffensive(facts, _facts[bestOffensive]))
                    bestOffensive = source;
                if (bestSetup < 0 || IsBetterSetup(facts, _facts[bestSetup]))
                    bestSetup = source;
                if (bestSetupCombo < 0 || IsBetterSetupCombo(facts, _facts[bestSetupCombo]))
                    bestSetupCombo = source;
                if (bestCuration < 0 || IsBetterDeckCuration(facts, _facts[bestCuration]))
                    bestCuration = source;
                if (bestCompressed < 0 || IsBetterCompressedDeck(facts, _facts[bestCompressed]))
                    bestCompressed = source;
                if (bestUtilityDefensive < 0
                    || IsBetterUtilityDefensive(facts, _facts[bestUtilityDefensive]))
                {
                    bestUtilityDefensive = source;
                }
                if (potionCount == 0)
                {
                    if (bestPotionFree < 0)
                        bestPotionFree = source;
                    if (bestPotionFreeDefensive < 0
                        || IsBetterDefensive(facts, _facts[bestPotionFreeDefensive]))
                    {
                        bestPotionFreeDefensive = source;
                    }
                    if (bestPotionFreeOffensive < 0
                        || IsBetterOffensive(facts, _facts[bestPotionFreeOffensive]))
                    {
                        bestPotionFreeOffensive = source;
                    }
                    if (bestPotionFreeSetup < 0
                        || IsBetterSetup(facts, _facts[bestPotionFreeSetup]))
                    {
                        bestPotionFreeSetup = source;
                    }
                    if (bestPotionFreeSetupCombo < 0
                        || IsBetterSetupCombo(facts, _facts[bestPotionFreeSetupCombo]))
                    {
                        bestPotionFreeSetupCombo = source;
                    }
                    if (bestPotionFreeUtilityDefensive < 0
                        || IsBetterUtilityDefensive(facts, _facts[bestPotionFreeUtilityDefensive]))
                    {
                        bestPotionFreeUtilityDefensive = source;
                    }
                }
                else
                {
                    if (bestPotion < 0)
                        bestPotion = source;
                    if (bestPotionDefensive < 0
                        || IsBetterDefensive(facts, _facts[bestPotionDefensive]))
                    {
                        bestPotionDefensive = source;
                    }
                    if (bestPotionOffensive < 0
                        || IsBetterOffensive(facts, _facts[bestPotionOffensive]))
                    {
                        bestPotionOffensive = source;
                    }
                    if (bestPotionSetup < 0 || IsBetterSetup(facts, _facts[bestPotionSetup]))
                        bestPotionSetup = source;
                    if (bestPotionSetupCombo < 0
                        || IsBetterSetupCombo(facts, _facts[bestPotionSetupCombo]))
                    {
                        bestPotionSetupCombo = source;
                    }
                }
            }

            for (int potionCount = 0; potionCount <= FlashLimits.PotionSlots; potionCount++)
            {
                if (!_potionLeaders.TryGetValue(potionCount, out PotionLeaders? potion))
                    continue;
                AddRequired(required, potion.Leader, width);
                AddRequired(required, potion.Defensive, width);
                AddRequired(required, potion.Offensive, width);
                AddRequired(required, potion.Strength, width);
                AddRequired(required, potion.Weak, width);
                AddRequired(required, potion.Setup, width);
                AddRequired(required, potion.SetupCombo, width);
                AddRequired(required, potion.Curation, width);
                AddRequired(required, potion.Compressed, width);
            }
            AddRequired(required, bestPotionFree, width);
            AddRequired(required, bestPotionFreeDefensive, width);
            AddRequired(required, bestPotionFreeOffensive, width);
            AddRequired(required, bestPotionFreeSetup, width);
            AddRequired(required, bestPotionFreeSetupCombo, width);
            AddRequired(required, bestPotionFreeUtilityDefensive, width);
            AddRequired(required, bestPotion, width);
            AddRequired(required, bestPotionDefensive, width);
            AddRequired(required, bestPotionOffensive, width);
            AddRequired(required, bestPotionSetup, width);
            AddRequired(required, bestPotionSetupCombo, width);
            AddRequired(required, bestDefensive, width);
            AddRequired(required, bestUtilityDefensive, width);
            AddRequired(required, bestOffensive, width);
            AddRequired(required, bestSetup, width);
            AddRequired(required, bestSetupCombo, width);
            AddRequired(required, bestCuration, width);
            AddRequired(required, bestCompressed, width);

            _lineageOrder.Clear();
            foreach (int key in _lineageLeaders.Keys)
                _lineageOrder.Add(key);
            _lineageOrder.Sort((left, right) => _rankPosition[_lineageLeaders[left].Leader]
                .CompareTo(_rankPosition[_lineageLeaders[right].Leader]));
            int lineageLimit = Math.Clamp(width / 8, 4, 16);
            for (int index = 0; index < _lineageOrder.Count && index < lineageLimit; index++)
            {
                LineageLeaders lineage = _lineageLeaders[_lineageOrder[index]];
                AddRequired(required, lineage.Leader, width);
                AddRequired(required, lineage.Defensive, width);
                AddRequired(required, lineage.Offensive, width);
                AddRequired(required, lineage.Setup, width);
                AddRequired(required, lineage.SetupCombo, width);
                AddRequired(required, lineage.Curation, width);
                AddRequired(required, lineage.Compressed, width);
            }
        }

        private void SortByRank(int count)
        {
            double[] ranks = _ranks;
            RetentionFacts[] facts = _facts;
            Array.Sort(_order, 0, count, Comparer<int>.Create((left, right) =>
            {
                // CombatBeamSolver.CompareBeamRankOrder: rank, then fewer actions, then offensive progress.
                int comparison = ranks[right].CompareTo(ranks[left]);
                if (comparison != 0)
                    return comparison;
                comparison = facts[left].Depth.CompareTo(facts[right].Depth);
                if (comparison != 0)
                    return comparison;
                comparison = facts[right].OffensiveProgressValue.CompareTo(facts[left].OffensiveProgressValue);
                return comparison != 0 ? comparison : left.CompareTo(right);
            }));
            for (int position = 0; position < count; position++)
            {
                int source = _order[position];
                _rankOrder[position] = source;
                _rankPosition[source] = position;
            }
        }

        private void SortByEnemyHp(int count)
        {
            RetentionFacts[] facts = _facts;
            int[] rankPosition = _rankPosition;
            Array.Sort(_order, 0, count, Comparer<int>.Create((left, right) =>
            {
                int comparison = facts[left].EnemyHp.CompareTo(facts[right].EnemyHp);
                return comparison != 0
                    ? comparison
                    : rankPosition[left].CompareTo(rankPosition[right]);
            }));
        }
    }
}
