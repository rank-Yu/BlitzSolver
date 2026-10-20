using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    /// <summary>The request's theft policy; <c>PreserveResources</c> adds the existing final ordering's recovery keys.</summary>
    public SolverTheftPolicy? TheftPolicy { get; init; }

    /// <summary>
    /// The request's forced potion directives, as canonical Flash potion ids. Only a request without a
    /// potion-policy override enforces them, matching <c>CombatBeamSolver.EnforcesPotionDirectives</c>.
    /// </summary>
    public FlashForcedPotionDirectives ForcedPotionDirectives { get; init; } = FlashForcedPotionDirectives.None;

    /// <summary>Whether the forced directives are part of the final policy filter (<c>potionPolicyOverride == null</c>).</summary>
    public bool EnforceForcedPotionDirectives { get; init; }

    /// <summary>The request's minimum explicit potion uses; the primary member passes 0.</summary>
    public int MinimumPotionUses { get; init; }

    /// <summary>
    /// Explicit potion uses already spent by the fixed prefix (included in the request's cumulative
    /// minimum). The root-potion diff only sees uses after the search root, so the max-use gate adds
    /// this frozen count.
    /// </summary>
    public int InitialExplicitPotionUses { get; init; }

    /// <summary>Automatic death-save potion uses already spent by the fixed prefix.</summary>
    public int InitialAutomaticPotionUses { get; init; }

    /// <summary>
    /// The audited potion-free baseline the existing final ordering compares an exact potion layer
    /// against; null means the evaluated candidates decide it. Only potion-audit requests set it.
    /// </summary>
    public PotionFreePolicyBaseline? PotionFreeBaseline { get; init; }

    /// <summary>The existing final ordering's one-reward potion replacement credit.</summary>
    public int PotionReplacementHpCredit { get; init; }

    /// <summary>
    /// The existing exact-layer HP ceiling (<c>CombatBeamSolver._smartPotionEligibilityHpCeiling</c>).
    /// Null means the existing search would not prune this layer either.
    /// </summary>
    public int? SmartPotionEligibilityHpCeiling { get; init; }

    /// <summary>Root ambergris potion id, or <see cref="ushort.MaxValue"/> when not registered.</summary>
    public ushort AmbergrisPotionId { get; init; } = ushort.MaxValue;

    /// <summary>Ambergris uses already spent by the fixed prefix.</summary>
    public int InitialAmbergrisUses { get; init; }

    /// <summary>
    /// Port the existing final ordering's potion policy filter and policy-deficit key. Set only for
    /// the Smart potion audits; primary/refinement and clean continuations keep the prior route key.
    /// </summary>
    public bool AuditPotionPolicy { get; init; }

    /// <summary>
    /// Actions already executed before the search root (a fixed prefix). They are added to every
    /// node's action count so <c>Score</c>, the route key and the complete-victory checks see the
    /// same count as the existing solver, which starts counting at the request root.
    /// </summary>
    public int InitialActionCount { get; init; }

    /// <summary>
    /// <c>SearchPolicySnapshot.Act3BossStrategy</c>. Enables the existing first-turn commitment
    /// expansion of a fetched, payable power before the layer is ranked.
    /// </summary>
    public bool Act3BossStrategy { get; init; }

    /// <summary>
    /// The plan commitment of a <c>ContinuationPurpose.PlanCommitment</c> member
    /// (<c>CombatBeamSolver.Expansion</c>'s <c>_planCommitment</c>): the payoff evidence counted
    /// along a route and the CopyPower representative seats. Null for every other request.
    /// </summary>
    public FlashPlanCommitment? PlanCommitment { get; init; }

    /// <summary>
    /// Payoff evidence already realized by the fixed prefix. The existing search starts its node chain
    /// at the request root, so its <c>CountRealizedPayoffs</c> includes prefix plays; FlashBeam starts at
    /// the prefix end and seeds this count instead.
    /// </summary>
    public int InitialPlanPayoffCount { get; init; }

    /// <summary>
    /// <c>CombatBeamSolver</c>'s <c>earlyTurnScoutDepth</c>: stop after this many searched turn layers
    /// and report each completed layer's frontier. Zero disables the scout.
    /// </summary>
    public int EarlyTurnScoutDepth { get; init; }

    /// <summary>
    /// The request root's combat turn (before a fixed prefix), matching the existing solver's
    /// <c>_startTurnNumber</c> for the scout's turn filter.
    /// </summary>
    public int InitialPlayerTurn { get; init; }
}

/// <summary>
/// The payoff evidence kinds FlashBeam ports. <c>RegisteredPowerBenefit</c> reads the existing
/// <c>PowerCommitment.RealizedEvidence</c>, which FlashBeam does not model; requests using it are
/// rejected at the dispatch boundary.
/// </summary>
internal enum FlashPlanPayoffKind : byte
{
    CardPlayed,
}

/// <summary>
/// A <see cref="PlanCommitment"/> reduced to the values FlashBeam reads: the CopyPower representative
/// seats and the per-route payoff count. The card is a Flash content id resolved by the bridge.
/// </summary>
internal sealed record FlashPlanCommitment(
    bool CopyPowerSeats,
    int OpenedTurn,
    int EarliestTurn,
    FlashPlanPayoffKind PayoffKind,
    ushort PayoffCardId);

/// <summary>One early-turn scout candidate: the route from the request root and its existing score.</summary>
internal sealed record FlashScoutCandidate(FlashAction[] Actions, double Score)
{
    /// <summary>Index of the turn-setup root the candidate started from (0 for an ordinary root).</summary>
    public int RootIndex { get; init; }
}

/// <summary>The candidates the existing search's observer reports after one searched turn layer.</summary>
internal sealed record FlashScoutLayer(int Turns, IReadOnlyList<FlashScoutCandidate> Candidates);

/// <summary>The request's HP-target stop (<c>CombatBeamSolver.MeetsHpTarget</c>): a complete victory
/// without death saves, with at least <paramref name="RequiredPotionUses"/> and at most
/// <paramref name="EarlyStopPotionUses"/> explicit potion uses, satisfying the enforced forced
/// directives, and with <paramref name="HpLostSoFar"/> plus its cumulative HP loss within
/// <paramref name="AcceptableBattleHpLoss"/>, ends the search before the next parent.
/// </summary>
internal sealed record FlashHpTargetStop(
    int AcceptableBattleHpLoss,
    int HpLostSoFar,
    int EarlyStopPotionUses,
    int RequiredPotionUses = 0,
    bool EnforceForcedPotionDirectives = false);

/// <summary>The root values <c>FinalPlanOrdering.Select</c> reads for the route keys FlashBeam ports.</summary>
internal sealed record FlashRouteOrdering(
    int InitialPlayerHp,
    int InitialPlayerMaxHp,
    BossHpRelief BossHpRelief,
    PostCombatRelicHealProfile PostCombatRelicHeal);

/// <summary>
/// The existing search's potion constraints at the root (<c>EnumeratePlannedPotionActions</c>): the
/// earliest player turn, the maximum number of uses, and the strategy/policy identity gate the existing
/// <c>AllowsPotionUse</c> applies. The gate is by (slot, potion identity), so a potion generated into a
/// slot during the search is offered exactly when the existing search would offer it.
/// </summary>
internal sealed record FlashPotionPolicy(
    int EarliestPlayerTurn,
    int? MaximumUses,
    PotionStrategySnapshot Strategy,
    SolverPotionPolicy EffectivePolicy,
    bool ForceAllDisabled)
{
    public static FlashPotionPolicy Disabled { get; } = new(
        int.MaxValue, 0,
        new PotionStrategySnapshot(SolverPotionPolicy.Disabled, Array.Empty<PotionSlotDirective>()),
        SolverPotionPolicy.Disabled, false);

    /// <summary><c>PotionStrategySnapshot.AllowsExplicitUse</c> for the potion now in the slot.</summary>
    public bool AllowsExplicitUse(int slot, ushort potionId)
    {
        FlashPotionDef definition = FlashRegistry.Potion(potionId);
        return definition.Searchable
            && Strategy.AllowsExplicitUse(slot, definition.NativeId, EffectivePolicy, ForceAllDisabled);
    }
}

/// <summary>
/// The request's forced potion directives (<c>PotionStrategySnapshot.Directives</c> with
/// <see cref="SolverPotionDirective.Force"/>), resolved to canonical Flash potion ids. A directive is
/// satisfied by one explicit use of that potion identity in its slot; the fixed prefix's uses are
/// folded into <paramref name="AlreadyUsedMask"/> because they are not Flash nodes.
/// </summary>
internal sealed record FlashForcedPotionDirectives(
    int[] Slots,
    ushort[] PotionIds,
    int[] StrategicHpCosts,
    int AlreadyUsedMask,
    int AlreadyUsedStrategicHpCost)
{
    public static FlashForcedPotionDirectives None { get; } = new([], [], [], 0, 0);

    public int Count => Slots.Length;
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
    internal static readonly IReadOnlyDictionary<string, int> NoCapacityDrops =
        new Dictionary<string, int>(0);

    /// <summary>Children dropped because their state exceeded a Flash capacity (a search boundary).</summary>
    public int CapacityPruned { get; init; }

    /// <summary>
    /// Dropped children per capacity region (e.g. <c>Cards</c>, <c>Powers</c>,
    /// <c>Action.Choices</c>). The state-level regions are child boundaries: one instance is
    /// dropped and the rest of the search continues. The pool region only appears for the
    /// per-action-layer pools; the cumulative end-turn pool is compacted instead.
    /// </summary>
    public IReadOnlyDictionary<string, int> CapacityDroppedChildren { get; init; } = NoCapacityDrops;

    /// <summary>How often the end-turn pool was compacted at its capacity bound.</summary>
    public int EndedCompactions { get; init; }

    /// <summary>End-turn children dropped by those compactions.</summary>
    public int EndedCompressedStates { get; init; }

    /// <summary>Audited branches removed by the Smart exact-layer HP ceiling.</summary>
    public int SmartPotionPruned { get; init; }

    /// <summary>
    /// The selected route's plan payoff evidence count, including
    /// <see cref="FlashBeamOptions.InitialPlanPayoffCount"/>. Zero when the request has no plan
    /// commitment or none was realized.
    /// </summary>
    public int BestPayoffCount { get; init; }

    /// <summary>The scout layers reported by an early-turn scout request, in searched order.</summary>
    public IReadOnlyList<FlashScoutLayer> ScoutLayers { get; init; } = [];

    /// <summary>
    /// Which root of the solve the selected route started from: 0 for an ordinary root, or the
    /// turn-setup root candidate index (the bridge maps it back to the setup choices).
    /// </summary>
    public int BestRootIndex { get; init; }

    /// <summary>
    /// False when an audited request found no route satisfying the ported potion policy. The caller
    /// falls back to the existing solver: "Flash found none" is not the existing search's verdict.
    /// </summary>
    public bool PotionPolicySatisfied { get; init; } = true;
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

    /// <summary>The existing <c>EarlyTurnCandidatesPerLayer</c>.</summary>
    private const int EarlyTurnCandidatesPerLayer = 24;

    /// <summary>
    /// When the cumulative end-turn pool reaches its capacity it is compacted to this many beam
    /// widths and keeps accumulating. The bound is the same <c>CopyTopTo</c> retention rule used at
    /// the turn boundary (rank leaders, the offensive share and every required representative), so a
    /// compacted pool still contains every state that can win a turn-boundary seat; only a root whose
    /// end-turn pool actually reaches the capacity can observe the compaction.
    /// </summary>
    private const int EndedCompactionWidthFactor = 8;

    /// <summary>
    /// Byte budget of each of the two growable pools (<c>next</c> and <c>ended</c>). The state count
    /// is <c>PoolStateBudgetBytes / sizeof(FlashBattle)</c>, rounded down to the 64-state chunk so the
    /// allocated chunked arrays do not overshoot the budget. The effective capacity is the smaller of
    /// this count and the original <c>width * (MaxLegalActions + 1)</c> bound. Without it a pathological
    /// turn (REGENT-V-06: 434 action layers in one turn) retains hundreds of megabytes per pool in the
    /// game process; 200 MiB per pool keeps the search well below the old ~0.8-1.0 GB single-pool peak.
    /// </summary>
    private const int PoolStateBudgetBytes = 200 * 1024 * 1024;

    private readonly FlashRules _rules;
    private readonly FlashBeamOptions _options;
    private readonly FlashScorer _scorer;
    private readonly FlashRouteOrdering _ordering;
    private readonly List<(int Parent, int Depth, FlashAction Action, int Lineage, int PayoffCount,
        ushort PotionId)> _nodes = new(4096);
    private readonly FlashAction[] _actions = new FlashAction[FlashKernel.MaxLegalActions];
    private readonly ushort[] _rootPotions = new ushort[FlashLimits.PotionSlots];
    private readonly FlashBoundFeatures? _bound;
    private readonly bool _pruneEqualHp;
    private readonly bool _preservePotionCostOpportunities;
    private readonly SolverPotionPolicy _effectivePotionPolicy;
    private readonly SolverTheftPolicy? _theftPolicy;
    private readonly bool _auditPotionPolicy;
    private readonly FlashForcedPotionDirectives _forcedPotionDirectives;
    private readonly bool _enforceForcedPotionDirectives;
    private readonly int _initialExplicitPotionUses;
    private readonly int _initialAutomaticPotionUses;
    private readonly PotionFreePolicyBaseline? _potionFreeBaseline;
    private readonly int _potionReplacementHpCredit;
    private readonly int? _smartPotionEligibilityHpCeiling;
    private readonly ushort _ambergrisPotionId;
    private readonly int _initialAmbergrisUses;
    private Dictionary<int, Candidate>? _auditBestByPotionCount;
    private Candidate? _bestPotionFree;
    private bool _anyAuditedVictory;
    private PrimarySearchIncumbent? _incumbent;
    private int _expanded;
    private int _transitions;
    private int _duplicates;
    private int _capacityPruned;
    private int _pruned;
    private int _smartPruned;
    private int _endedCompactions;
    private int _endedCompressedStates;
    private int _endedCompactionTarget;
    private readonly Dictionary<string, int> _capacityDropped = new(StringComparer.Ordinal);

    /// <summary>
    /// True only on the first completed turn layer when the three-act boss policy is frozen on. The
    /// existing search expands a fetched, payable power's next decision before ranking that layer
    /// (<c>CombatBeamSolver.Phases.cs</c>, <c>searchedTurnLayers == 0</c>).
    /// </summary>
    private bool _expandFetchedPowers;

    /// <summary>The existing search's HP-target stop, so a commitment batch stops like its parent layer.</summary>
    private bool _hpTargetReached;

    private CancellationToken _cancellationToken;

    /// <summary>
    /// Separate action buffer for the commitment expansion: the outer layer loop still iterates its
    /// own <see cref="_actions"/> span while <see cref="ExpandAction"/> runs.
    /// </summary>
    private readonly FlashAction[] _commitmentActions = new FlashAction[FlashKernel.MaxLegalActions];

    private readonly FlashPlanCommitment? _planCommitment;
    private readonly int _earlyTurnScoutDepth;
    private readonly int _initialPlayerTurn;
    private readonly List<FlashScoutLayer> _scoutLayers = [];

    private Stopwatch? _solveWatch;

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
        _theftPolicy = options.TheftPolicy;
        _auditPotionPolicy = options.AuditPotionPolicy;
        _forcedPotionDirectives = options.ForcedPotionDirectives;
        _enforceForcedPotionDirectives = options.EnforceForcedPotionDirectives;
        _initialExplicitPotionUses = options.InitialExplicitPotionUses;
        _initialAutomaticPotionUses = options.InitialAutomaticPotionUses;
        _potionFreeBaseline = options.PotionFreeBaseline;
        _potionReplacementHpCredit = options.PotionReplacementHpCredit;
        _smartPotionEligibilityHpCeiling = options.SmartPotionEligibilityHpCeiling;
        _ambergrisPotionId = options.AmbergrisPotionId;
        _initialAmbergrisUses = options.InitialAmbergrisUses;
        _planCommitment = options.PlanCommitment;
        _earlyTurnScoutDepth = Math.Max(0, options.EarlyTurnScoutDepth);
        _initialPlayerTurn = options.InitialPlayerTurn;
        if (_smartPotionEligibilityHpCeiling != null && _bound == null)
            throw new InvalidOperationException("Smart 药水资格上界需要启用下界输入。");
        _incumbent = _bound == null ? null : options.Incumbent;
    }

    public FlashBeamResult Solve(in FlashBattle root, CancellationToken cancellationToken)
        => Solve([root], cancellationToken);

    /// <summary>
    /// Solves from one or more starting states. A turn-setup request hands over every enumerable setup
    /// root (the existing search's frontier does the same); the returned <see cref="FlashBeamResult.BestRootIndex"/>
    /// identifies which one the selected route started from.
    /// </summary>
    public FlashBeamResult Solve(IReadOnlyList<FlashBattle> roots, CancellationToken cancellationToken)
    {
        if (roots.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(roots), "至少需要一个搜索根。");
        Stopwatch watch = Stopwatch.StartNew();
        _cancellationToken = cancellationToken;
        _solveWatch = watch;
        _hpTargetReached = false;
        for (int slot = 0; slot < FlashLimits.PotionSlots; slot++)
            _rootPotions[slot] = slot < roots[0].PotionSlotCount ? roots[0].Potions[slot] : (ushort)0;
        int width = Math.Max(1, _options.BeamWidth);
        // current and turnFrontier only receive CopyTopTo(width) and the roots. next and ended receive
        // children built in the scratch/free slot; their chunks are allocated on first write and never
        // copied, so a duplicate or terminal child does not grow them. The capacity bound below is an
        // explicit rejection boundary, not a true upper bound for ended: ended accumulates one end-turn
        // child per action layer, and a deep single turn can add more layers than MaxLegalActions.
        int frontierCapacity = Math.Max(width, roots.Count);
        // Every turn-setup root is a first-layer parent, even when the profile's beam is narrower than
        // the setup choice set; the existing search also expands its whole root frontier.
        int maxChildren = checked(frontierCapacity * (FlashKernel.MaxLegalActions + 1));
        // Both growable pools use the same byte budget; the original per-action-layer bound stays as
        // the semantic upper bound for small widths. The state count is rounded down to the 64-state
        // chunk so the chunked arrays stay inside the budget.
        int chunkStates = ChunkedArray<FlashBattle>.ChunkSize;
        int budgetStates = PoolStateBudgetBytes / Unsafe.SizeOf<FlashBattle>() / chunkStates * chunkStates;
        int poolLimit = Math.Min(maxChildren, Math.Max(chunkStates, budgetStates));
        int initialChildren = Math.Min(poolLimit, Math.Max(64, width * 8));
        // The compaction target is measured in beam widths: it always keeps more than the turn
        // boundary consumes (width) plus the same retention lanes, and never exceeds the new pool
        // limit. Reaching the limit is the only trigger, so a root below it keeps the previous
        // behavior bit for bit.
        _endedCompactionTarget = Math.Min(poolLimit, Math.Max(width, width * EndedCompactionWidthFactor));
        _endedCompactions = 0;
        _endedCompressedStates = 0;
        _capacityDropped.Clear();
        bool preserveTheft = _theftPolicy == SolverTheftPolicy.PreserveResources;
        Pool current = new(frontierCapacity, frontierCapacity, preserveTheft, _scorer, _planCommitment);
        Pool next = new(initialChildren, poolLimit, preserveTheft, _scorer, _planCommitment);
        Pool ended = new(initialChildren, poolLimit, preserveTheft, _scorer, _planCommitment);
        Pool turnFrontier = new(frontierCapacity, frontierCapacity, preserveTheft, _scorer, _planCommitment);

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
        void ConsiderCandidate(in FlashBattle state, int node, in FlashScoreFeatures features, in RouteKey key)
        {
            if (_auditPotionPolicy)
                ConsiderAudited(node, features, key);
            else
                Consider(state, node, key);
            TightenIncumbent(state, node, features, key);
        }
        void ConsiderTerminal(in FlashBattle state, int node, double score)
        {
            int actionCount = Depth(node);
            FlashScoreFeatures features = _scorer.Features(state, actionCount, includeRetentionOnly: false);
            RouteKey key = Key(state, features, actionCount);
            ConsiderCandidate(state, node, features, key);
            if (!features.AllEnemiesDead)
                return;
            int explicitPotionUses = ExplicitPotionUses(node);
            if (key.Won && _options.HpTargetStop is { } stop
                && _scorer.GrowthTargetsSatisfied(features)
                && _scorer.RelicTargetsSatisfied(features)
                && features.ProjectedDeathSaveUseCount == 0
                && explicitPotionUses >= stop.RequiredPotionUses
                && (!stop.EnforceForcedPotionDirectives
                    || EvaluateForcedUses(node, out _, out _, out _))
                && explicitPotionUses <= stop.EarlyStopPotionUses
                && stop.HpLostSoFar + features.CumulativePlayerHpLost <= stop.AcceptableBattleHpLoss)
            {
                hpTargetReached = true;
                _hpTargetReached = true;
            }
            if (explicitPotionUses == 0
                && features.CumulativePlayerHpLost == 0
                && features.PlayerMaxHp >= _options.InitialPlayerMaxHp
                && features.PlayerHp >= features.PlayerMaxHp)
            {
                zeroDamageVictory = true;
            }
        }

        _nodes.Clear();
        for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            FlashBattle root = roots[rootIndex];
            (double rootScore, double rootRank, double rootPureRank, int rootPotionCost,
                FlashScoreFeatures rootFeatures) =
                Score(root, _options.InitialActionCount, terminal: false, isEndTurn: false);
            _nodes.Add((-1, Math.Max(0, _options.InitialActionCount), default, -1,
                _options.InitialPlanPayoffCount, ushort.MaxValue));
            (int rootReachable, int rootZeroCost) = _scorer.ReachableHandPotential(root);
            RetentionFacts rootFacts = RetentionFacts.From(
                rootFeatures, _options.InitialActionCount, rootReachable, rootZeroCost);
            turnFrontier.Add(root, rootIndex, rootScore, rootRank, rootPureRank, rootPotionCost, rootFacts, -1,
                _options.InitialPlanPayoffCount);
        }
        TerminalSink terminal = ConsiderTerminal;
        int turns = 0;
        while (turnFrontier.Count > 0 && turns < _options.MaxTurns && !OutOfBudget(watch)
            && !hpTargetReached
            && (_earlyTurnScoutDepth == 0 || turns < _earlyTurnScoutDepth))
        {
            turns++;
            current.Clear();
            turnFrontier.CopyTopTo(current, turns == 1 ? frontierCapacity : width, 0d);
            ended.Clear();
            // Turn one is the existing search's searchedTurnLayers == 0.
            _expandFetchedPowers = _options.Act3BossStrategy && turns == 1;
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
                        ExpandAction(ref parent, parentNode, _actions[actionIndex], next, ended, terminal);
                    }
                }
                current.Clear();
                next.CopyTopTo(current, width, _options.OffensiveShare);
                PrunePool(current);
            }
            if (_earlyTurnScoutDepth > 0 && turns <= _earlyTurnScoutDepth)
                CollectEarlyTurnScout(ended, turns, roots);
            turnFrontier.Clear();
            // BeamRetentionPolicy.RankBest at the end-turn frontier: the turn-boundary hand leader is
            // a required representative of every potion group.
            ended.CopyTopTo(turnFrontier, width, _options.OffensiveShare, turnBoundary: true);
            PrunePool(turnFrontier);
            if (zeroDamageVictory)
                break;
        }

        if (_auditPotionPolicy)
        {
            // The existing final ordering evaluates every surviving boundary state next to the terminal
            // routes; an exact layer must also see a potion-use frontier even when a (too-expensive)
            // victory already exists, so the boundary states are always collected here.
            for (int index = 0; index < turnFrontier.Count; index++)
            {
                int node = turnFrontier.Node(index);
                int actionCount = Depth(node);
                ref FlashBattle state = ref turnFrontier.State(index);
                FlashScoreFeatures features = _scorer.Features(state, actionCount, includeRetentionOnly: false);
                ConsiderCandidate(state, node, features, Key(state, features, actionCount));
            }
            if (SelectAudited(out int auditedNode, out RouteKey auditedKey, out bool auditedVictory, out bool auditedDefeat))
            {
                best = auditedKey;
                bestNode = auditedNode;
                bestVictory = auditedVictory;
                bestDefeat = auditedDefeat;
            }
            FlashBeamResult audited = new(_auditPotionPolicy && best is null ? [] : Route(bestNode),
                bestVictory, bestDefeat, best?.Score ?? double.NegativeInfinity,
                _expanded, _transitions, _duplicates, turns, _pruned, watch.Elapsed)
            {
                CapacityPruned = _capacityPruned,
                CapacityDroppedChildren = _capacityDropped.Count == 0
                    ? FlashBeamResult.NoCapacityDrops
                    : _capacityDropped,
                EndedCompactions = _endedCompactions,
                EndedCompressedStates = _endedCompressedStates,
                SmartPotionPruned = _smartPruned,
                PotionPolicySatisfied = best is not null,
                BestPayoffCount = _nodes[bestNode].PayoffCount,
                BestRootIndex = RootIndex(bestNode),
                ScoutLayers = _scoutLayers,
            };
            return audited;
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
                Consider(state, node, Key(state,
                    _scorer.Features(state, actionCount, includeRetentionOnly: false), actionCount));
            }
        }
        FlashBeamResult result = new(Route(bestNode), bestVictory, bestDefeat,
            best?.Score ?? double.NegativeInfinity,
            _expanded, _transitions, _duplicates, turns, _pruned, watch.Elapsed)
        {
            CapacityPruned = _capacityPruned,
            CapacityDroppedChildren = _capacityDropped.Count == 0
                ? FlashBeamResult.NoCapacityDrops
                : _capacityDropped,
            EndedCompactions = _endedCompactions,
            EndedCompressedStates = _endedCompressedStates,
            SmartPotionPruned = _smartPruned,
            BestPayoffCount = _nodes[bestNode].PayoffCount,
            BestRootIndex = RootIndex(bestNode),
            ScoutLayers = _scoutLayers,
        };
        return result;
    }

    private void ConsiderAudited(int node, in FlashScoreFeatures features, in RouteKey key)
    {
        if (key.Won)
            _anyAuditedVictory = true;
        bool forcedSatisfied = true;
        int forcedCount = 0, forcedCost = 0, forcedAmbergris = 0;
        if (_enforceForcedPotionDirectives && _forcedPotionDirectives.Count > 0)
        {
            forcedSatisfied = EvaluateForcedUses(node,
                out forcedCount, out forcedCost, out forcedAmbergris);
        }
        int optionalPotionCount = Math.Max(0, features.ExplicitPotionUseCount - forcedCount);
        int optionalPotionStrategicCost = OptionalPotionStrategicCost(
            features.ExplicitPotionStrategicCost, optionalPotionCount, forcedCost);
        SolverPotionPolicy effectivePotionPolicy = _effectivePotionPolicy switch
        {
            SolverPotionPolicy.RequireAtLeastOne when forcedCount > 0 => SolverPotionPolicy.Smart,
            SolverPotionPolicy.Disabled when optionalPotionCount > 0 => SolverPotionPolicy.Smart,
            _ => _effectivePotionPolicy,
        };
        int policyHpDeficit = effectivePotionPolicy == SolverPotionPolicy.RequireAtLeastOne
            ? key.StrategicHpDeficit
                + PotionUsePolicy.AdditionalRequiredUseStrategicHpCost(optionalPotionStrategicCost)
            : key.StrategicHpDeficit;
        RouteKey auditedKey = key with
        {
            OptionalPotionCount = optionalPotionCount,
            PolicyHpDeficit = policyHpDeficit,
        };
        Candidate candidate = new(
            node,
            auditedKey,
            features.ExplicitPotionUseCount,
            features.ExplicitPotionStrategicCost,
            optionalPotionStrategicCost,
            AmbergrisUses(node),
            features.PlayerHp,
            features.ProjectedDeathSaveUseCount,
            forcedSatisfied,
            forcedCount,
            forcedCost,
            forcedAmbergris,
            effectivePotionPolicy);
        if (candidate.ExplicitPotionUseCount == 0
            && (_bestPotionFree is not { } free || ComparePotionFree(candidate, free) < 0))
        {
            _bestPotionFree = candidate;
        }
        Dictionary<int, Candidate> bestByCount = _auditBestByPotionCount ??= new();
        // A route that does not satisfy a forced directive can never pass the policy filter, but it
        // must not mask a satisfying route of the same explicit count.
        int bucket = checked(candidate.ExplicitPotionUseCount * 2 + (forcedSatisfied ? 0 : 1));
        if (!bestByCount.TryGetValue(bucket, out Candidate current)
            || CompareAudited(candidate, current) < 0)
        {
            bestByCount[bucket] = candidate;
        }
    }

    private int OptionalPotionStrategicCost(int explicitPotionStrategicCost, int optionalPotionCount,
        int forcedPotionStrategicCost = 0)
        => PotionUsePolicy.ApplyReplacementCredit(
            Math.Max(0, explicitPotionStrategicCost - forcedPotionStrategicCost),
            optionalPotionCount,
            _potionReplacementHpCredit);

    /// <summary>
    /// <c>PotionStrategySnapshot.EvaluateForcedUses(SearchNode)</c> over the Flash node chain: one
    /// explicit use of each forced potion identity in its slot, with the fixed prefix's uses folded in.
    /// Returns whether every forced directive was used.
    /// </summary>
    private bool EvaluateForcedUses(int node, out int count, out int strategicCost,
        out int ambergrisCount)
    {
        FlashForcedPotionDirectives directives = _forcedPotionDirectives;
        count = 0;
        strategicCost = 0;
        ambergrisCount = 0;
        for (int index = 0; index < directives.Count; index++)
        {
            int slot = directives.Slots[index];
            bool used = (directives.AlreadyUsedMask & (1 << index)) != 0;
            if (!used)
            {
                for (int cursor = node; _nodes[cursor].Parent >= 0; cursor = _nodes[cursor].Parent)
                {
                    FlashAction action = _nodes[cursor].Action;
                    if (action.Kind == FlashActionKind.UsePotion && action.PotionSlot == slot
                        && _nodes[cursor].PotionId == directives.PotionIds[index])
                    {
                        used = true;
                        break;
                    }
                }
            }
            if (!used)
                continue;
            count++;
            strategicCost += (directives.AlreadyUsedMask & (1 << index)) != 0
                ? 0
                : directives.StrategicHpCosts[index];
            if (_ambergrisPotionId != ushort.MaxValue
                && directives.PotionIds[index] == _ambergrisPotionId)
            {
                ambergrisCount++;
            }
        }
        strategicCost += directives.AlreadyUsedStrategicHpCost;
        return count == directives.Count;
    }

    /// <summary>
    /// The existing <c>FinalPlanOrdering.Select</c> filter and ordering, applied to the best candidate
    /// per explicit potion count. The request's audited baseline is authoritative when present; otherwise
    /// the best potion-free candidate of this search supplies it, exactly like the existing evaluated set.
    /// </summary>
    private bool SelectAudited(out int node, out RouteKey key, out bool victory, out bool defeat)
    {
        bool hasPotionFree = _potionFreeBaseline is not null || _bestPotionFree is not null;
        bool potionFreeWon = _potionFreeBaseline?.Won ?? _bestPotionFree?.Key.Won ?? false;
        int potionFreeDeficit = _potionFreeBaseline?.HpDeficit
            ?? _bestPotionFree?.Key.StrategicHpDeficit
            ?? _ordering.InitialPlayerHp;
        int potionFreePlayerHp = _potionFreeBaseline?.PlayerHp ?? _bestPotionFree?.PlayerHp ?? 0;
        int potionFreeDeathSaves = _potionFreeBaseline?.DeathSaveUseCount
            ?? (_bestPotionFree is { } free ? free.ProjectedDeathSaveUseCount : int.MaxValue);
        int potionFreeOutstanding = _bestPotionFree?.Key.OutstandingStolenResource ?? int.MaxValue;
        bool anyRouteWon = potionFreeWon || _anyAuditedVictory;
        Candidate? selected = null;
        if (_auditBestByPotionCount is { } bestByCount)
        {
            foreach (Candidate candidate in bestByCount.Values)
            {
                if (!IsAuditedPolicyEligible(candidate, hasPotionFree, potionFreeWon, potionFreeDeficit,
                        potionFreePlayerHp, potionFreeDeathSaves, potionFreeOutstanding, anyRouteWon))
                {
                    continue;
                }
                if (selected is not { } current || CompareAudited(candidate, current) < 0)
                    selected = candidate;
            }
        }
        if (selected is not { } bestCandidate)
        {
            node = 0;
            key = default;
            victory = false;
            defeat = false;
            return false;
        }
        node = bestCandidate.Node;
        key = bestCandidate.Key;
        victory = bestCandidate.Key.Won;
        defeat = !bestCandidate.Key.Won && !bestCandidate.Key.Survives;
        return true;
    }

    private bool IsAuditedPolicyEligible(Candidate candidate, bool hasPotionFree, bool potionFreeWon,
        int potionFreeDeficit, int potionFreePlayerHp, int potionFreeDeathSaves, int potionFreeOutstanding,
        bool anyRouteWon)
    {
        if (!candidate.ForcedUsesSatisfied)
            return false;
        if (candidate.ExplicitPotionUseCount < _options.MinimumPotionUses)
            return false;
        if (_options.Potions.MaximumUses is { } maximum
            && candidate.ExplicitPotionUseCount > maximum)
        {
            return false;
        }
        bool protectsDeathSave = hasPotionFree && potionFreeWon && candidate.Key.Won
            && _effectivePotionPolicy != SolverPotionPolicy.Disabled
            && candidate.Key.OptionalPotionCount > 0
            && candidate.ProjectedDeathSaveUseCount < potionFreeDeathSaves;
        // FinalPlanOrdering.Select: under the preserve policy, more potion use is allowed when it
        // recovers outstanding loot.
        bool recoversLoot = _theftPolicy == SolverTheftPolicy.PreserveResources
            && candidate.Key.OutstandingStolenResource < potionFreeOutstanding;
        bool passesSoftPolicy = PotionUsePolicy.IsEligible(
                candidate.EffectivePotionPolicy,
                candidate.Key.OptionalPotionCount,
                PotionUsePolicy.SmartRequiredHpSaved(
                    candidate.OptionalPotionStrategicCost, _ordering.BossHpRelief),
                potionFreeWon,
                potionFreeDeficit,
                anyRouteWon,
                candidate.Key.Won,
                candidate.Key.StrategicHpDeficit)
            || protectsDeathSave
            || recoversLoot;
        bool passesAmbergris = protectsDeathSave
            || recoversLoot
            || PotionUsePolicy.MeetsAmbergrisRestriction(
                hasPotionFree,
                Math.Max(0, candidate.AmbergrisUses - candidate.ForcedAmbergrisCount),
                candidate.OptionalPotionStrategicCost,
                _ordering.InitialPlayerMaxHp,
                potionFreePlayerHp,
                candidate.PlayerHp);
        return passesSoftPolicy && passesAmbergris;
    }

    /// <summary>Negative when <paramref name="left"/> wins the audited final ordering.</summary>
    private int CompareAudited(in Candidate left, in Candidate right)
    {
        int comparison = CompareRoutes(left.Key, right.Key);
        return comparison != 0 ? comparison : left.Node.CompareTo(right.Node);
    }

    /// <summary>
    /// <c>CombatBeamSolver.FinalPlanOrdering.ComparePotionFreePolicyBaselines</c> restricted to the
    /// admitted keys (growth and sold HP are zero/rejected; the theft recovery key is included); the
    /// route index breaks exact ties.
    /// </summary>
    private int ComparePotionFree(in Candidate left, in Candidate right)
    {
        int comparison = right.Key.Won.CompareTo(left.Key.Won);
        if (comparison != 0) return comparison;
        if (!left.Key.Won && !right.Key.Won)
        {
            comparison = RightSurvives(right).CompareTo(RightSurvives(left));
            if (comparison != 0) return comparison;
        }
        comparison = left.Key.DeathSaveUseCount.CompareTo(right.Key.DeathSaveUseCount);
        if (comparison != 0) return comparison;
        if (_theftPolicy == SolverTheftPolicy.PreserveResources)
        {
            comparison = left.Key.OutstandingStolenResource.CompareTo(right.Key.OutstandingStolenResource);
            if (comparison != 0) return comparison;
        }
        comparison = left.Key.StrategicHpDeficit.CompareTo(right.Key.StrategicHpDeficit);
        if (comparison != 0) return comparison;
        comparison = right.Key.GrowthHpCredit.CompareTo(left.Key.GrowthHpCredit);
        if (comparison != 0) return comparison;
        comparison = right.Key.GrowthRewardCount.CompareTo(left.Key.GrowthRewardCount);
        if (comparison != 0) return comparison;
        comparison = left.Key.CombatEndedTurn.CompareTo(right.Key.CombatEndedTurn);
        if (comparison != 0) return comparison;
        comparison = left.Key.HealthResourceCost.CompareTo(right.Key.HealthResourceCost);
        if (comparison != 0) return comparison;
        comparison = right.Key.LongTermResourceValue.CompareTo(left.Key.LongTermResourceValue);
        if (comparison != 0) return comparison;
        comparison = left.Key.AngerCopiesGenerated.CompareTo(right.Key.AngerCopiesGenerated);
        if (comparison != 0) return comparison;
        comparison = left.Key.EnemyHp.CompareTo(right.Key.EnemyHp);
        if (comparison != 0) return comparison;
        comparison = right.Key.Score.CompareTo(left.Key.Score);
        if (comparison != 0) return comparison;
        comparison = left.Key.ActionCount.CompareTo(right.Key.ActionCount);
        return comparison != 0 ? comparison : left.Node.CompareTo(right.Node);
    }

    private static int RightSurvives(in Candidate candidate) => candidate.Key.Survives ? 1 : 0;

    private delegate void TerminalSink(in FlashBattle state, int node, double score);

    /// <summary>
    /// The keys of the existing final ordering (<c>FinalPlanOrdering.Select</c>) that the admitted requests
    /// reach. Growth and relic-counter credits are zero because their settings are rejected at admission;
    /// the sold-HP and boundary tie-breakers are not ported yet. The theft recovery key is carried only
    /// under <c>PreserveResources</c>, matching <c>TheftEncounterStrategy.CompareRecovery</c>.
    /// </summary>
    private readonly record struct RouteKey(
        bool Won,
        bool Survives,
        int DeathSaveUseCount,
        int OutstandingStolenResource,
        int StrategicHpDeficit,
        int GrowthHpCredit,
        int GrowthRewardCount,
        int CombatEndedTurn,
        int PolicyHpDeficit,
        int HealthResourceCost,
        int LongTermResourceValue,
        int AngerCopiesGenerated,
        int OptionalPotionCount,
        int EnemyHp,
        double Score,
        int ActionCount);

    /// <summary>An audited final candidate: the ordering key plus the values its policy filter reads.</summary>
    private readonly record struct Candidate(
        int Node,
        RouteKey Key,
        int ExplicitPotionUseCount,
        int ExplicitPotionStrategicCost,
        int OptionalPotionStrategicCost,
        int AmbergrisUses,
        int PlayerHp,
        int ProjectedDeathSaveUseCount,
        bool ForcedUsesSatisfied,
        int ForcedUseCount,
        int ForcedStrategicHpCost,
        int ForcedAmbergrisCount,
        SolverPotionPolicy EffectivePotionPolicy);

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
            features.DeathSaveHpRestored) - features.StrategicHpCredit;
        int optionalPotionCount = features.ExplicitPotionUseCount;
        int optionalPotionStrategicCost = OptionalPotionStrategicCost(
            features.ExplicitPotionStrategicCost, optionalPotionCount);
        // The policy-deficit key is part of the audited final ordering only. Primary/refinement
        // members keep their prior route key so the Flash adoption decisions they already passed do
        // not move; the exact-layer requests port the key together with the potion filter.
        int policyHpDeficit = _auditPotionPolicy
            && _effectivePotionPolicy == SolverPotionPolicy.RequireAtLeastOne
                ? strategicHpDeficit
                    + PotionUsePolicy.AdditionalRequiredUseStrategicHpCost(optionalPotionStrategicCost)
                : strategicHpDeficit;
        return new RouteKey(
            won,
            !features.PlayerDead && features.ProjectedPlayerHp > 0,
            features.ProjectedDeathSaveUseCount,
            _theftPolicy == SolverTheftPolicy.PreserveResources ? features.OutstandingStolenResource : 0,
            strategicHpDeficit,
            features.StrategyGoalHpCredit,
            features.StrategyGoalCount,
            won ? state.PlayerTurn : int.MaxValue,
            policyHpDeficit,
            _ordering.InitialPlayerHp - features.PlayerHp + _ordering.InitialPlayerMaxHp - features.PlayerMaxHp,
            features.LongTermResourceValue,
            features.AngerCopiesGenerated,
            optionalPotionCount,
            features.EnemyHp,
            features.Score,
            actionCount);
    }

    /// <summary>Negative when <paramref name="left"/> is the better route.</summary>
    private int CompareRoutes(in RouteKey left, in RouteKey right)
    {
        int comparison = right.Won.CompareTo(left.Won);
        if (comparison != 0) return comparison;
        comparison = (!left.Won && !left.Survives ? 1 : 0).CompareTo(!right.Won && !right.Survives ? 1 : 0);
        if (comparison != 0) return comparison;
        comparison = left.DeathSaveUseCount.CompareTo(right.DeathSaveUseCount);
        if (comparison != 0) return comparison;
        // TheftEncounterStrategy.CompareRecovery: only the preserve policy ranks the unrecovered loot.
        if (_theftPolicy == SolverTheftPolicy.PreserveResources)
        {
            comparison = left.OutstandingStolenResource.CompareTo(right.OutstandingStolenResource);
            if (comparison != 0) return comparison;
        }
        comparison = left.StrategicHpDeficit.CompareTo(right.StrategicHpDeficit);
        if (comparison != 0) return comparison;
        comparison = right.GrowthHpCredit.CompareTo(left.GrowthHpCredit);
        if (comparison != 0) return comparison;
        comparison = right.GrowthRewardCount.CompareTo(left.GrowthRewardCount);
        if (comparison != 0) return comparison;
        comparison = left.CombatEndedTurn.CompareTo(right.CombatEndedTurn);
        if (comparison != 0) return comparison;
        comparison = left.PolicyHpDeficit.CompareTo(right.PolicyHpDeficit);
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
        => ExpandAction(ref parent, parentNode, action, next, ended, terminal,
            null, default, allowFetchedPowerCommit: true);

    private void ExpandAction(ref FlashBattle parent, int parentNode, FlashAction action, Pool next, Pool ended,
        TerminalSink terminal, FlashChoiceEffect? primaryEffect, scoped ReadOnlySpan<FlashCard> selectedPowers,
        bool allowFetchedPowerCommit)
    {
        ushort usedPotion = ushort.MaxValue;
        if (action.Kind == FlashActionKind.UsePotion
            && (uint)action.PotionSlot < (uint)parent.PotionSlotCount
            && parent.Potions[action.PotionSlot] != 0)
        {
            usedPotion = (ushort)(parent.Potions[action.PotionSlot] - 1);
        }
        // A capacity overflow is a child boundary, not a simulation error: the one child that needs
        // more cards, powers, enemies, choice slots or action slots than FlashBattle holds is dropped
        // and the rest of the search continues. Every adopted route is still verified step by step
        // against the existing engine. The catch covers the whole construction of this child
        // (reserve, step, score and commit); the per-selection catch below keeps one overflowing
        // choice branch from cancelling its siblings.
        try
        {
            // The child is built in next's first free slot and stepped in place: one copy of the parent per
            // transition. It is committed to next, copied to ended, or dropped as a terminal; the slot is not
            // touched by anything else until then (a choice re-expansion reuses it from the start).
            ref FlashBattle child = ref next.Reserve();
            child = parent;
            _transitions++;
            FlashStepResult stepResult;
            FlashChoiceRequest request;
            stepResult = FlashKernel.Step(ref child, action, _rules, out request);
            if (stepResult == FlashStepResult.NeedsChoice)
            {
                // Every executed selection is exact; the cap only bounds how many are explored.
                Span<ulong> selections = stackalloc ulong[MaxChoiceBranches];
                int count = FlashChoiceEnumerator.Enumerate(request, selections);
                Span<FlashCard> capturedPowers = stackalloc FlashCard[FlashLimits.MaxHandSize];
                for (int index = 0; index < count; index++)
                {
                    FlashChoiceEffect? effect = primaryEffect;
                    scoped ReadOnlySpan<FlashCard> chosenPowers = selectedPowers;
                    if (allowFetchedPowerCommit && primaryEffect is null
                        && request.Timing == FlashChoiceTiming.Action
                        && IsPrimaryMoveToHandAction(parent, action))
                    {
                        effect = request.Effect;
                        int captured = 0;
                        if (effect == FlashChoiceEffect.MoveToHand && !request.OptionsAreGenerated)
                        {
                            ulong bits = selections[index];
                            for (int option = 0; option < request.OptionCount; option++)
                            {
                                if ((bits & (1UL << option)) == 0)
                                    continue;
                                ref readonly FlashCard optionCard = ref child.Cards[request.Options[option]];
                                if (FlashKernel.CardType(child, _rules, optionCard) == FlashCardType.Power)
                                    capturedPowers[captured++] = optionCard;
                            }
                        }
                        chosenPowers = capturedPowers[..captured];
                    }
                    try
                    {
                        ExpandAction(ref parent, parentNode, action.WithChoice(selections[index]), next, ended, terminal,
                            effect, chosenPowers, allowFetchedPowerCommit);
                    }
                    catch (FlashCapacityException ex)
                    {
                        RecordCapacityDrop(ex);
                    }
                }
                return;
            }
            int node = _nodes.Count;
            int depth = _nodes[parentNode].Depth + 1;
            // The existing search's root-action lineage groups every descendant by the first action
            // taken from the request root (BeamRetentionPolicy.BuildRootActionLineageSignature).
            int lineage = parentNode == 0 ? node : _nodes[parentNode].Lineage;
            int payoffCount = NextPayoffCount(parentNode, parent, action);
            _nodes.Add((parentNode, depth, action, lineage, payoffCount, usedPotion));
            bool isTerminal = !FlashKernel.IsInProgress(child);
            (double score, double rank, double pureRank, int potionCost, FlashScoreFeatures features) =
                Score(child, depth, isTerminal, action.Kind == FlashActionKind.EndTurn);
            if (isTerminal)
            {
                terminal(child, node, score);
                return;
            }
            if (allowFetchedPowerCommit
                && _expandFetchedPowers
                && primaryEffect == FlashChoiceEffect.MoveToHand
                && HasPlayableFetchedPower(child, selectedPowers)
                && CommitmentBudgetAvailable())
            {
                // CombatBeamSolver.Phases.cs: settle the fetched, payable power's next decision before
                // ranking the intermediate selection. The intermediate state is not committed; every
                // successor goes through the normal transition/dedup/pool accounting.
                ExpandFetchedPowerSuccessors(ref child, node, next, ended, terminal);
                return;
            }
            // The retention-only hand values are computed by the pool only when the state is actually
            // stored (a duplicate keeps its existing values); a terminal state never needs them.
            bool added = action.Kind == FlashActionKind.EndTurn || child.PlayerTurn != parent.PlayerTurn
                ? AddEndTurnChild(ended, child, node, score, rank, pureRank, potionCost, features, depth,
                    lineage, payoffCount)
                : next.CommitReserved(node, score, rank, pureRank, potionCost, features, depth, lineage,
                    payoffCount);
            if (!added)
                _duplicates++;
        }
        catch (FlashCapacityException ex)
        {
            RecordCapacityDrop(ex);
        }
    }

    /// <summary>
    /// The cumulative end-turn layer is the only pool that can reach its bound (the per-action-layer
    /// pool is cleared each layer and receives at most <c>width * MaxLegalActions</c> children). When
    /// a unique end-turn child no longer fits, compact the pool with the same retention rule the
    /// turn boundary uses, then add the child. Duplicates never reach the bound and stay free.
    /// </summary>
    private bool AddEndTurnChild(Pool ended, in FlashBattle child, int node, double score, double rank,
        double pureRank, int potionCost, in FlashScoreFeatures features, int depth, int lineage,
        int payoffCount)
    {
        try
        {
            return ended.AddScored(child, node, score, rank, pureRank, potionCost, features, depth,
                lineage, payoffCount);
        }
        catch (FlashCapacityException) when (ended.IsFull)
        {
            int dropped = ended.Compact(_endedCompactionTarget, _options.OffensiveShare, turnBoundary: true);
            _endedCompactions++;
            _endedCompressedStates += dropped;
            return ended.AddScored(child, node, score, rank, pureRank, potionCost, features, depth,
                lineage, payoffCount);
        }
    }

    private void RecordCapacityDrop(FlashCapacityException ex)
    {
        _capacityPruned++;
        CollectionsMarshal.GetValueRefOrAddDefault(_capacityDropped, ex.Region, out _)++;
    }

    private bool CommitmentBudgetAvailable()
        => _expanded < _options.MaxExpandedNodes
            && !_hpTargetReached
            && (_solveWatch is not { } watch || watch.Elapsed < _options.SoftTimeBudget);

    /// <summary>
    /// The existing search's <c>Expand(commitment)</c>: every legal successor of the fetched-power
    /// state enters the current layer through the normal path. The commitment state itself is dropped
    /// (<c>nextPlays.Remove(commitment)</c>). The action buffer is separate because the enclosing layer
    /// loop still iterates its own actions.
    /// </summary>
    private void ExpandFetchedPowerSuccessors(ref FlashBattle commitment, int commitmentNode,
        Pool next, Pool ended, TerminalSink terminal)
    {
        _expanded++;
        FlashBattle state = commitment;
        int count = FlashKernel.LegalActions(state, _rules, _commitmentActions);
        for (int index = 0; index < count; index++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            FlashAction action = _commitmentActions[index];
            if (action.Kind == FlashActionKind.UsePotion && !PotionAllowed(state, action.PotionSlot))
                continue;
            // ExpandAction handles a capacity overflow for this successor as its own child; a
            // sibling's overflow never cancels the rest of the commitment batch.
            ExpandAction(ref state, commitmentNode, action, next, ended, terminal,
                null, default, allowFetchedPowerCommit: false);
            if (!CommitmentBudgetAvailable())
                break;
        }
    }

    /// <summary>
    /// <c>CombatBeamSolver.HasPlayableFetchedPower</c>: the choice moved a card to hand and a matching
    /// Power from that selection is now playable in hand.
    /// </summary>
    private bool HasPlayableFetchedPower(in FlashBattle battle, scoped ReadOnlySpan<FlashCard> selectedPowers)
    {
        if (selectedPowers.Length == 0)
            return false;
        for (int index = 0; index < battle.HandCount; index++)
        {
            ref readonly FlashCard card = ref battle.Cards[index];
            if (FlashKernel.CardType(battle, _rules, card) != FlashCardType.Power
                || !FlashKernel.CanPlayCard(battle, _rules, index))
            {
                continue;
            }
            for (int selected = 0; selected < selectedPowers.Length; selected++)
            {
                if (SameCardIdentity(card, selectedPowers[selected]))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The played card's primary <c>CardChoiceSupport</c> spec is <c>MoveToHand</c>, or the potion is
    /// Droplet of Precognition (its <c>PotionChoiceSupport</c> spec is also <c>MoveToHand</c>).
    /// </summary>
    private bool IsPrimaryMoveToHandAction(in FlashBattle parent, in FlashAction action)
    {
        if (action.Kind == FlashActionKind.PlayCard)
        {
            if (action.HandIndex >= parent.HandCount)
                return false;
            ref readonly FlashCard card = ref parent.Cards[action.HandIndex];
            return _scorer.IsPrimaryMoveToHandCard(card.Id, card.Upgrade);
        }
        if (action.Kind != FlashActionKind.UsePotion
            || _scorer.DropletOfPrecognitionPotionId == ushort.MaxValue
            || action.PotionSlot >= parent.PotionSlotCount)
        {
            return false;
        }
        return parent.Potions[action.PotionSlot]
            == (ushort)(_scorer.DropletOfPrecognitionPotionId + 1);
    }

    private static bool SameCardIdentity(in FlashCard left, in FlashCard right)
        => left.Id == right.Id
            && left.Upgrade == right.Upgrade
            && left.EnchantmentId == right.EnchantmentId
            && left.EnchantmentAmount == right.EnchantmentAmount
            && left.AfflictionId == right.AfflictionId
            && left.AfflictionAmount == right.AfflictionAmount;

    private bool OutOfBudget(Stopwatch watch)
        => _expanded >= _options.MaxExpandedNodes || watch.Elapsed >= _options.SoftTimeBudget;

    private int Depth(int node) => _nodes[node].Depth;

    private int ExplicitPotionUses(int node)
    {
        int uses = _initialExplicitPotionUses;
        for (int cursor = node; _nodes[cursor].Parent >= 0; cursor = _nodes[cursor].Parent)
        {
            if (_nodes[cursor].Action.Kind == FlashActionKind.UsePotion)
                uses++;
        }
        return uses;
    }

    private int AmbergrisUses(int node)
    {
        if (_ambergrisPotionId == ushort.MaxValue)
            return 0;
        int uses = _initialAmbergrisUses;
        for (int cursor = node; _nodes[cursor].Parent >= 0; cursor = _nodes[cursor].Parent)
        {
            FlashAction action = _nodes[cursor].Action;
            if (action.Kind != FlashActionKind.UsePotion)
                continue;
            if (_nodes[cursor].PotionId == _ambergrisPotionId)
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

    /// <summary>The root node an action chain descends from (the first <c>nodes</c> entries are roots).</summary>
    private int RootIndex(int node)
    {
        int cursor = node;
        while (_nodes[cursor].Parent >= 0)
            cursor = _nodes[cursor].Parent;
        return cursor;
    }

    private readonly record struct FlashScoutOpening(
        FlashActionKind Kind, ushort CardId, ushort PotionId, uint? TargetCombatId);

    /// <summary>
    /// <c>CombatBeamSolver.EarlyTurnStrategyKey</c> without <c>SearchRouteTraits</c>: FlashBeam does not
    /// model the accumulated route trait flags, so the scout groups by the K4-checked setup traits and
    /// the remaining existing bands. The omission can only merge groups; it never invents a candidate.
    /// </summary>
    private readonly record struct FlashScoutStrategy(
        FlashScoutOpening Opening,
        PersistentSetupTraits SetupTraits,
        int PotionCount,
        int PersistentBuffBand,
        int HandValueBand,
        uint? FocusTargetId);

    private sealed record FlashScoutEntry(int Node, FlashScoutStrategy Strategy, double Score);

    /// <summary>
    /// <c>CombatBeamSolver.SelectEarlyTurnFrontier</c> over the ended pool: filter the completed turn,
    /// keep the top two scores per strategy key, group by opening, then round-robin the opening groups
    /// up to 24 candidates. The pool already holds one representative per byte-identical state.
    /// </summary>
    private void CollectEarlyTurnScout(Pool ended, int completedTurns, IReadOnlyList<FlashBattle> roots)
    {
        List<FlashScoutEntry> entries = new(ended.Count);
        for (int index = 0; index < ended.Count; index++)
        {
            ref FlashBattle state = ref ended.State(index);
            if (state.PlayerTurn != _initialPlayerTurn + completedTurns)
                continue;
            int node = ended.Node(index);
            int rootIndex = RootIndex(node);
            FlashScoutOpening opening = ScoutOpening(roots[rootIndex], node);
            RetentionFacts facts = ended.Facts(index);
            FlashScoutStrategy strategy = new(
                opening,
                facts.SetupTraits,
                Math.Min(3, facts.PotionCount),
                facts.PersistentBuffValue / 8,
                facts.ReachableHandValue / 8,
                facts.FocusTargetCombatId);
            entries.Add(new(node, strategy, facts.Score));
        }
        FlashScoutEntry[][] openings = entries
            .GroupBy(entry => entry.Strategy)
            .SelectMany(group => group.OrderByDescending(entry => entry.Score).Take(2))
            .GroupBy(entry => entry.Strategy.Opening)
            .Select(group => group.OrderByDescending(entry => entry.Score).ToArray())
            .OrderByDescending(group => group[0].Score)
            .ToArray();
        List<FlashScoutCandidate> selected = [];
        for (int rank = 0; selected.Count < EarlyTurnCandidatesPerLayer; rank++)
        {
            bool found = false;
            foreach (FlashScoutEntry[] opening in openings)
            {
                if (rank >= opening.Length)
                    continue;
                found = true;
                FlashScoutEntry entry = opening[rank];
                selected.Add(new FlashScoutCandidate(Route(entry.Node), entry.Score)
                {
                    RootIndex = RootIndex(entry.Node),
                });
                if (selected.Count == EarlyTurnCandidatesPerLayer)
                    break;
            }
            if (!found)
                break;
        }
        _scoutLayers.Add(new FlashScoutLayer(completedTurns, selected));
    }

    /// <summary>
    /// The opening key of the existing <c>FirstAction</c>: the first action taken from the search root.
    /// The scout is only admitted for an empty fixed prefix, so its parent state is the battle root.
    /// </summary>
    private FlashScoutOpening ScoutOpening(in FlashBattle root, int node)
    {
        int first = node;
        while (_nodes[first].Parent >= 0)
            first = _nodes[first].Parent;
        FlashAction action = _nodes[first].Action;
        ushort cardId = 0;
        ushort potionId = 0;
        uint? target = null;
        if (action.Kind == FlashActionKind.PlayCard)
        {
            if (action.HandIndex < root.HandCount)
                cardId = root.Cards[action.HandIndex].Id;
        }
        else if (action.Kind == FlashActionKind.UsePotion)
        {
            if (action.PotionSlot < root.PotionSlotCount)
                potionId = root.Potions[action.PotionSlot];
        }
        if (action.Target >= FlashLimits.FirstEnemyOwner
            && action.Target - FlashLimits.FirstEnemyOwner < root.EnemyCount)
        {
            target = root.Enemies[action.Target - FlashLimits.FirstEnemyOwner].CombatId;
        }
        return new FlashScoutOpening(action.Kind, cardId, potionId, target);
    }

    /// <summary>
    /// The existing primary member's terminal-victory tightening
    /// (<c>TightenPrimarySearchIncumbentAtTurnLayer</c>): a complete, risk-free, potion-free victory
    /// publishes a strictly better incumbent at the commit boundary, so the same layer's retention can
    /// use it. The shared <see cref="PrimaryIncumbentTable"/> is not connected.
    /// </summary>
    private void TightenIncumbent(in FlashBattle state, int node, in FlashScoreFeatures features, in RouteKey key)
    {
        // CombatBeamSolver.TightenPrimarySearchIncumbentAtTurnLayer: only a potion-free victory, or an
        // exact audited layer with a baseline and no enforced directives, may establish the bound.
        bool canEstablishPotionFree = _options.MinimumPotionUses == 0
            && _effectivePotionPolicy is SolverPotionPolicy.Disabled or SolverPotionPolicy.Smart;
        bool canEstablishExactPotion = _potionFreeBaseline != null
            && _options.MinimumPotionUses > 0
            && _options.Potions.MaximumUses == _options.MinimumPotionUses
            && !_enforceForcedPotionDirectives;
        if (!canEstablishPotionFree && !canEstablishExactPotion)
            return;
        if (_bound == null || !key.Won || features.ProjectedDeathSaveUseCount > 0)
            return;
        int explicitPotionUses = features.ExplicitPotionUseCount;
        if (explicitPotionUses != _options.MinimumPotionUses)
            return;
        if (_enforceForcedPotionDirectives && _forcedPotionDirectives.Count > 0
            && !EvaluateForcedUses(node, out _, out _, out _))
        {
            return;
        }
        int strategicHpDeficit = ActEndingBossPolicy.StrategicHpDeficit(
            features.CumulativePlayerHpLost,
            Math.Max(0, _options.InitialPlayerMaxHp - features.PlayerMaxHp),
            features.RecoveredPlayerHp + ActEndingBossPolicy.RankedPostCombatRelicHeal(
                _ordering.PostCombatRelicHeal, true, features.PlayerHp, features.PlayerMaxHp),
            _bound.BossHpRelief,
            features.DeathSaveHpRestored) - features.StrategicHpCredit;
        PrimarySearchIncumbent? candidate = _incumbent;
        if (CombatBeamSolver.TryTightenPrimarySearchIncumbent(
                auditedPotionFreeBaseline: _potionFreeBaseline,
                minimumPotionUses: _options.MinimumPotionUses,
                maximumPotionUses: _options.Potions.MaximumUses,
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
        PrimarySearchIncumbent? incumbent = _bound == null ? null : _incumbent;
        int? ceiling = _smartPotionEligibilityHpCeiling;
        if (ceiling is null && (incumbent is null || _bound is null))
            return;
        int write = 0;
        for (int read = 0; read < pool.Count; read++)
        {
            FlashBoundValues values = _bound!.Evaluate(pool.State(read), pool.PotionCost(read),
                includeSmartPotionLowerBound: ceiling is not null);
            bool certifiedUnknown = _bound.RequireCertifiedHealingProof && _pruneEqualHp
                && values.RemainingHealingPotential == int.MaxValue;
            bool incumbentPrune = false;
            if (incumbent is { } bounded && !certifiedUnknown)
            {
                bool costClosed = !_preservePotionCostOpportunities
                    || (bounded.ExplicitPotionStrategicCost is { } cost
                        && values.ExplicitPotionStrategicCost >= cost);
                incumbentPrune = CombatBeamSolver.ShouldPruneByPrimaryIncumbent(
                    values.StrategicHpLowerBound,
                    pool.State(read).PlayerTurn,
                    bounded,
                    allowTurnTieBound: costClosed,
                    pruneEqualHp: _pruneEqualHp && !values.HasRisk && costClosed);
            }
            bool smartPrune = !incumbentPrune && ceiling is { } limit
                && !(_bound.RequireCertifiedHealingProof
                    && values.SmartPotionRemainingHealingPotential == int.MaxValue)
                && values.SmartPotionHpLowerBound > limit;
            if (!incumbentPrune && !smartPrune)
            {
                if (write != read)
                    pool.Move(read, write);
                write++;
                continue;
            }
            // The existing pipeline applies the incumbent bound first and the potion ceiling to what
            // survives, so a branch removed by both is attributed to the incumbent.
            if (incumbentPrune)
                _pruned++;
            else
                _smartPruned++;
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
    private (double Score, double Rank, double PureRank, int PotionCost, FlashScoreFeatures Features) Score(
        in FlashBattle battle, int actionCount, bool terminal, bool isEndTurn)
    {
        // ReachableHandValue / ZeroCostPlayableCount feed only the retention comparators, not Score,
        // BeamRankScore or RouteKey. The retention pool computes them when a state is stored, which
        // skips duplicate and terminal children.
        FlashScoreFeatures features = _scorer.Features(battle, actionCount, includeRetentionOnly: false);
        // The plan representative seats keep the existing MaxBy(BeamRankScore) tie order, which does
        // not include Flash's potion-cost rank penalty. The two values differ only by that term.
        double pureRank = _scorer.BeamRankScore(battle, features, terminal, isEndTurn);
        // ExplicitPotionStrategicCost includes the fixed prefix and potions generated during the
        // search; the previous root-slot diff saw neither generated uses nor their identity costs.
        // An audited exact layer must spend its potions, so the Flash-specific "spending is free"
        // correction would rank the required line below states that have not spent them yet; the
        // final ordering still charges the cost. Non-audit members keep the correction.
        double rank = _auditPotionPolicy && _options.MinimumPotionUses > 1
            ? pureRank
            : pureRank - features.ExplicitPotionStrategicCost * SolverWeights.Hp;
        return (features.Score, rank, pureRank, features.ExplicitPotionStrategicCost, features);
    }

    /// <summary>
    /// The route's realized plan payoff count up to this action, mirroring
    /// <c>PlanCommitment.CountRealizedPayoffs</c>: a matching card play on or after the payoff turn.
    /// The count starts at <see cref="FlashBeamOptions.InitialPlanPayoffCount"/>, so prefix plays are
    /// included exactly as the existing search's node chain includes them.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int NextPayoffCount(int parentNode, in FlashBattle parent, in FlashAction action)
    {
        if (_planCommitment is not { PayoffKind: FlashPlanPayoffKind.CardPlayed } plan)
            return 0;
        int count = _nodes[parentNode].PayoffCount;
        if (parent.PlayerTurn < plan.EarliestTurn
            || action.Kind != FlashActionKind.PlayCard
            || action.HandIndex >= parent.HandCount
            || parent.Cards[action.HandIndex].Id != plan.PayoffCardId)
        {
            return count;
        }
        return checked(count + 1);
    }

    /// <summary>
    /// The slot holds a searchable potion the request's strategy/policy offers now. The existing
    /// <c>EnumeratePlannedPotionActions</c> reads the potion currently in the slot, so a potion generated
    /// during the search is offered by its own identity rather than by the root potion's policy.
    /// </summary>
    private bool PotionAllowed(in FlashBattle battle, int slot)
    {
        FlashPotionPolicy policy = _options.Potions;
        if ((uint)slot >= (uint)battle.PotionSlotCount || battle.PlayerTurn < policy.EarliestPlayerTurn)
            return false;
        ushort stored = battle.Potions[slot];
        if (stored == 0 || !policy.AllowsExplicitUse(slot, (ushort)(stored - 1)))
            return false;
        if (policy.MaximumUses is { } maximum && ExplicitPotionUsesInState(battle) >= maximum)
            return false;
        return true;
    }

    /// <summary>Root potions consumed after the search root (automatic death saves included).</summary>
    private int MissingRootPotions(in FlashBattle battle)
    {
        int used = 0;
        for (int slot = 0; slot < FlashLimits.PotionSlots; slot++)
        {
            if (_rootPotions[slot] != 0 && (slot >= battle.PotionSlotCount || battle.Potions[slot] != _rootPotions[slot]))
                used++;
        }
        return used;
    }

    /// <summary>
    /// The existing <c>ExplicitPotionUseCount</c>: cumulative explicit uses, including the fixed
    /// prefix's and potions generated during the search, without automatic death-save potions.
    /// </summary>
    private int ExplicitPotionUsesInState(in FlashBattle battle)
    {
        int automaticAfterRoot = battle.Counter(FlashCounter.AutomaticPotionUseCount)
            - _initialAutomaticPotionUses;
        return _initialExplicitPotionUses + MissingRootPotions(battle)
            + battle.Counter(FlashCounter.GeneratedPotionUseCount) - automaticAfterRoot;
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
        int OstyHp,
        int OstyMaxHp,
        int DefensiveBlockValue,
        int AliveEnemyCount,
        int RawEnemyHp,
        int EnemyHp,
        int MaxCurrentEnemyHp,
        int RevivingEnemyCount,
        int RetainedAttackValue,
        int StrategicRetentionValue,
        int SetupValue,
        int SetupComboValue,
        int LiveDeckSize,
        int LiveDeckClutter,
        long AttackDensity,
        int EnemyStrengthSuppression,
        int EnemyWeakTurns,
        int FocusTargetPressure,
        int FocusTargetRemainingHp,
        int FocusTargetCurrentThreat,
        uint? FocusTargetCombatId,
        uint? MostVulnerableTargetCombatId,
        int FocusTargetVulnerableTurns,
        int EnemyVulnerableTurns,
        int SandpitRemaining,
        int ReactiveDamageValue,
        int LongTermResourceValue,
        int Energy,
        int Stars,
        int HandCount,
        int ReachableHandValue,
        int ZeroCostPlayableCount,
        int FutureResourceValue,
        int LatentSetupValue,
        int ReplayPotentialValue,
        int DelayedDamageValue,
        PersistentSetupTraits SetupTraits,
        int PersistentBuffValue,
        int OffensiveProgressValue,
        int OutstandingStolenResource,
        double Score)
    {
        public static RetentionFacts From(in FlashScoreFeatures features, int depth,
            int reachableHandValue, int zeroCostPlayableCount) => new(
            depth,
            features.PotionUseCount,
            features.ProjectedPlayerHp,
            features.OstyHp,
            features.OstyMaxHp,
            features.DefensiveBlockValue,
            features.AliveEnemyCount,
            features.RawEnemyHp,
            features.EnemyHp,
            features.MaxCurrentEnemyHp,
            features.RevivingEnemyCount,
            features.RetainedAttackValue,
            features.StrategicRetentionValue,
            SetupLane(features),
            SetupCombo(features, reachableHandValue),
            features.LiveDeckSize,
            features.LiveDeckClutter,
            (long)features.RetainedAttackValue * 1024 / Math.Max(1, features.LiveDeckSize),
            features.EnemyStrengthSuppression,
            features.EnemyWeakTurns,
            features.FocusTargetPressure,
            features.FocusTargetRemainingHp,
            features.FocusTargetCurrentThreat,
            features.FocusTargetCombatId,
            features.MostVulnerableTargetCombatId,
            features.FocusTargetVulnerableTurns,
            features.EnemyVulnerableTurns,
            features.SandpitRemaining,
            features.ReactiveDamageValue,
            features.LongTermResourceValue,
            features.Energy,
            features.Stars,
            features.HandCount,
            reachableHandValue,
            zeroCostPlayableCount,
            features.FutureResourceValue,
            features.LatentSetupValue,
            features.ReplayPotentialValue,
            features.DelayedDamageValue,
            features.PersistentSetupTraits,
            features.PersistentBuffValue,
            features.OffensiveProgressValue,
            features.OutstandingStolenResource,
            features.Score);

        /// <summary>
        /// BeamRetentionPolicy.SetupLaneValue: StrategicEffects.RetentionValue * 16
        /// + LatentSetupValue * 8 + ReplayPotentialValue * 16 + FutureResourceValue.
        /// </summary>
        private static int SetupLane(in FlashScoreFeatures features)
            => features.StrategicRetentionValue * 16
                + features.LatentSetupValue * 8
                + features.ReplayPotentialValue * 16
                + features.FutureResourceValue;

        /// <summary>
        /// BeamRetentionPolicy.FindBestSetup: Scaling + Resource + Control lanes (LaneValue in
        /// BeamRetentionPolicy.Ranking.cs). Every term is the existing formula; the resource lane's
        /// Stars and ReachableHandValue and the control lane's SandpitRemaining are K4-checked.
        /// </summary>
        private static int SetupCombo(in FlashScoreFeatures features, int reachableHandValue)
            => features.StrategicRetentionValue * 16
                + features.LatentSetupValue * 8
                + features.ReplayPotentialValue * 16
                + features.FutureResourceValue
                + features.DelayedDamageValue
                + features.Energy * 16
                + features.Stars * 8
                + features.HandCount
                + reachableHandValue
                + features.FutureResourceValue
                + features.OstyHp * 16
                + features.OstyMaxHp * 4
                + features.SandpitRemaining * 32
                + features.EnemyStrengthSuppression * 32
                + features.EnemyWeakTurns * 8
                + features.FocusTargetVulnerableTurns * 4
                    * Math.Min(SolverWeights.VulnerableAttackWindowCap, features.RetainedAttackValue)
                + Math.Max(0, features.EnemyVulnerableTurns - features.FocusTargetVulnerableTurns)
                    * Math.Min(SolverWeights.VulnerableAttackWindowCap, features.RetainedAttackValue)
                + features.DelayedDamageValue
                - features.LiveDeckClutter * 8;
    }

    // The leader comparators below are the existing BeamRetentionPolicy.Ranking comparators with the
    // same tie order. The Osty terms are imported from the pet state (IsBetterDefensive and the Resource
    // lane), matching the existing engine's leader comparators.
    private static bool IsBetterDefensive(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.ProjectedPlayerHp > current.ProjectedPlayerHp
            || candidate.ProjectedPlayerHp == current.ProjectedPlayerHp
                && (candidate.OstyHp > current.OstyHp
                    || candidate.OstyHp == current.OstyHp
                        && (candidate.OstyMaxHp > current.OstyMaxHp
                            || candidate.OstyMaxHp == current.OstyMaxHp
                                && (candidate.DefensiveBlockValue > current.DefensiveBlockValue
                                    || candidate.DefensiveBlockValue == current.DefensiveBlockValue
                                        && candidate.Score > current.Score)));

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

    /// <summary>CombatBeamSolver.BeamRetentionPolicy.IsBetterSearchNode: score, then fewer actions.</summary>
    private static bool IsBetterSearchNode(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.Score > current.Score
            || candidate.Score.Equals(current.Score) && candidate.Depth < current.Depth;

    /// <summary>FindBestTacticalEnabler: free card plays, reachable hand value, hand size.</summary>
    private static bool IsBetterTacticalEnabler(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.ZeroCostPlayableCount > current.ZeroCostPlayableCount
            || candidate.ZeroCostPlayableCount == current.ZeroCostPlayableCount
                && (candidate.ReachableHandValue > current.ReachableHandValue
                    || candidate.ReachableHandValue == current.ReachableHandValue
                        && (candidate.HandCount > current.HandCount
                            || candidate.HandCount == current.HandCount
                                && IsBetterSearchNode(candidate, current)));

    /// <summary>
    /// FindBestTurnBoundaryHand (BeamRetentionPolicy.Ranking.cs): projected HP, then Osty HP, hand
    /// size, reachable hand value and enemy HP at the turn boundary.
    /// </summary>
    private static bool IsBetterTurnBoundaryHand(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.ProjectedPlayerHp > current.ProjectedPlayerHp
            || candidate.ProjectedPlayerHp == current.ProjectedPlayerHp
                && (candidate.OstyHp > current.OstyHp
                    || candidate.OstyHp == current.OstyHp
                        && (candidate.HandCount > current.HandCount
                            || candidate.HandCount == current.HandCount
                                && (candidate.ReachableHandValue > current.ReachableHandValue
                                    || candidate.ReachableHandValue == current.ReachableHandValue
                                        && (candidate.EnemyHp < current.EnemyHp
                                            || candidate.EnemyHp == current.EnemyHp
                                                && candidate.Score > current.Score))));

    /// <summary>FindBestTargetPressure: focus pressure, remaining HP, threat, then safety.</summary>
    private static bool IsBetterTargetPressure(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.FocusTargetPressure > current.FocusTargetPressure
            || candidate.FocusTargetPressure == current.FocusTargetPressure
                && (candidate.FocusTargetRemainingHp < current.FocusTargetRemainingHp
                    || candidate.FocusTargetRemainingHp == current.FocusTargetRemainingHp
                        && (candidate.FocusTargetCurrentThreat > current.FocusTargetCurrentThreat
                            || candidate.FocusTargetCurrentThreat == current.FocusTargetCurrentThreat
                                && (candidate.ProjectedPlayerHp > current.ProjectedPlayerHp
                                    || candidate.ProjectedPlayerHp == current.ProjectedPlayerHp
                                        && candidate.Score > current.Score)));

    /// <summary>FindBestTargetSetup: setup lane, then focus pressure, then safety.</summary>
    private static bool IsBetterTargetSetup(in RetentionFacts candidate, in RetentionFacts current)
        => candidate.SetupValue > current.SetupValue
            || candidate.SetupValue == current.SetupValue
                && (candidate.RetainedAttackValue > current.RetainedAttackValue
                    || candidate.RetainedAttackValue == current.RetainedAttackValue
                        && (candidate.FocusTargetPressure > current.FocusTargetPressure
                            || candidate.FocusTargetPressure == current.FocusTargetPressure
                                && (candidate.ProjectedPlayerHp > current.ProjectedPlayerHp
                                    || candidate.ProjectedPlayerHp == current.ProjectedPlayerHp
                                        && candidate.Score > current.Score)));

    /// <summary>
    /// BeamRetentionPolicy.Ranking.LaneValue. The existing leader is restricted to nodes whose
    /// accumulated route traits contain the lane; FlashBeam does not model route traits, so the
    /// comparator runs over the whole group. A state with the extreme lane value almost always
    /// carries the trait, which is the case the existing retention protects.
    /// </summary>
    private static int LaneValue(in RetentionFacts facts, SearchRouteTraits trait)
        => trait switch
        {
            SearchRouteTraits.Scaling => facts.SetupValue + facts.DelayedDamageValue,
            SearchRouteTraits.Resource => facts.Energy * 16
                + facts.Stars * 8
                + facts.HandCount
                + facts.ReachableHandValue
                + facts.FutureResourceValue
                + facts.OstyHp * 16
                + facts.OstyMaxHp * 4,
            SearchRouteTraits.LongTermResource => facts.LongTermResourceValue,
            SearchRouteTraits.Control => facts.SandpitRemaining * 32
                + facts.EnemyStrengthSuppression * 32
                + facts.EnemyWeakTurns * 8
                + facts.FocusTargetVulnerableTurns * 4
                    * Math.Min(SolverWeights.VulnerableAttackWindowCap, facts.RetainedAttackValue)
                + Math.Max(0, facts.EnemyVulnerableTurns - facts.FocusTargetVulnerableTurns)
                    * Math.Min(SolverWeights.VulnerableAttackWindowCap, facts.RetainedAttackValue)
                + facts.DelayedDamageValue
                - facts.LiveDeckClutter * 8,
            SearchRouteTraits.RevivalWindow => facts.RevivingEnemyCount * 1024
                - facts.RawEnemyHp * 4
                - facts.MaxCurrentEnemyHp * 8,
            SearchRouteTraits.DeclinedExtraTurn => 0,
            SearchRouteTraits.ReactiveDamage => facts.ReactiveDamageValue,
            SearchRouteTraits.EndTurnDeckCompression => facts.Energy * 64
                + facts.FutureResourceValue * 16
                + (int)Math.Min(int.MaxValue, facts.AttackDensity)
                + facts.FocusTargetPressure
                - facts.LiveDeckSize * 16,
            SearchRouteTraits.HpInvestment => facts.StrategicRetentionValue * 16
                + facts.FutureResourceValue * 8
                + facts.DelayedDamageValue * 8
                + facts.FocusTargetPressure,
            _ => throw new ArgumentOutOfRangeException(nameof(trait), trait, null),
        };

    private static bool IsBetterLane(in RetentionFacts candidate, in RetentionFacts current,
        SearchRouteTraits trait)
    {
        int candidateValue = LaneValue(candidate, trait);
        int currentValue = LaneValue(current, trait);
        return candidateValue > currentValue
            || candidateValue == currentValue
                && (candidate.ProjectedPlayerHp > current.ProjectedPlayerHp
                    || candidate.ProjectedPlayerHp == current.ProjectedPlayerHp
                        && (candidate.AliveEnemyCount < current.AliveEnemyCount
                            || candidate.AliveEnemyCount == current.AliveEnemyCount
                                && (candidate.EnemyHp < current.EnemyHp
                                    || candidate.EnemyHp == current.EnemyHp
                                        && candidate.Score > current.Score)));
    }

    private static readonly SearchRouteTraits[] RetainedLanes =
    [
        SearchRouteTraits.Scaling,
        SearchRouteTraits.Resource,
        SearchRouteTraits.Control,
        SearchRouteTraits.RevivalWindow,
        SearchRouteTraits.ReactiveDamage,
        SearchRouteTraits.EndTurnDeckCompression,
        SearchRouteTraits.LongTermResource,
        SearchRouteTraits.HpInvestment,
    ];

    private sealed class LineageLeaders
    {
        public int Leader = -1;
        public int Defensive = -1;
        public int Offensive = -1;
        public int Setup = -1;
        public int SetupCombo = -1;
        public int Curation = -1;
        public int Compressed = -1;
        public int Tactical = -1;
        public int TargetPressure = -1;
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
        public int Tactical = -1;
        public int TurnBoundary = -1;
        public int Resource = -1;
        public readonly int[] Lanes = new int[8];

        public PotionLeaders()
        {
            Array.Fill(Lanes, -1);
        }
    }

    private sealed class TraitLeaders
    {
        public int Defensive = -1;
        public int Setup = -1;
        public int Position = int.MaxValue;
    }

    private sealed class FocusLeaders
    {
        public int Pressure = -1;
        public int Setup = -1;
        public int MaxPressure = int.MinValue;
        public int Position = int.MaxValue;
    }

    /// <summary>
    /// A fixed-size chunked array. Chunks are allocated on first write and never copied, so growing a
    /// pool has no reallocation. Every chunk is made at least 85,000 bytes (except the state chunks,
    /// which are deliberately larger; see <see cref="ComputeShift"/>) so it lands in the LOH instead of
    /// being promoted repeatedly through Gen0/Gen1.
    /// </summary>
    private sealed class ChunkedArray<T>
    {
        private static readonly int Shift = ComputeShift();
        public static int ChunkSize => 1 << Shift;
        public static int ChunkMask => ChunkSize - 1;

        private readonly T[]?[] _chunks;

        public ChunkedArray(int maxCapacity)
            => _chunks = new T[]?[(maxCapacity + ChunkSize - 1) >> Shift];

        public ref T this[int index]
        {
            get
            {
                int chunkIndex = index >> Shift;
                T[] chunk = _chunks[chunkIndex] ??= CreateChunk();
                return ref chunk[index & ChunkMask];
            }
        }

        private static T[] CreateChunk() => new T[ChunkSize];

        private static int ComputeShift()
        {
            int size = Unsafe.SizeOf<T>();
            int shift = 0;
            while ((1 << shift) * size < 85_000)
                shift++;
            // FlashBattle is 12,600 bytes. 64 states (806,400 bytes) halve the LOH object count of
            // the 32-state variant while remaining small enough to place as one block.
            if (typeof(T) == typeof(FlashBattle) && shift < 6)
                shift = 6;
            return shift;
        }
    }

    /// <summary>
    /// State pool with byte-exact deduplication keeping the best score. State and parallel metadata
    /// live in fixed-size chunks that are allocated on demand and never reallocated; exceeding
    /// <c>maxCapacity</c> is a capacity rejection.
    /// </summary>
    private sealed class Pool
    {
        private readonly int _maxCapacity;
        private readonly ChunkedArray<FlashBattle> _states;
        private readonly ChunkedArray<int> _nodes;
        private readonly ChunkedArray<int> _lineages;
        private readonly ChunkedArray<double> _scores;
        private readonly ChunkedArray<double> _ranks;
        private readonly ChunkedArray<ulong> _hashes;
        private readonly ChunkedArray<RetentionFacts> _facts;
        private readonly ChunkedArray<int> _potionCosts;
        private readonly ChunkedArray<double> _pureRanks;
        private readonly ChunkedArray<int> _payoffs;
        private int[] _table;
        private readonly int[] _order;
        private readonly int[] _rankOrder;
        private readonly int[] _rankPosition;
        private readonly bool[] _taken;
        private readonly bool[] _required;
        private FlashBattle _scratch;
        private bool _scratchReserved;
        private readonly List<int> _selected = new();
        private readonly List<int> _requiredScratch = new();
        private readonly Dictionary<int, LineageLeaders> _lineageLeaders = new();
        private readonly List<int> _lineageOrder = new();
        private readonly Dictionary<int, PotionLeaders> _potionLeaders = new();
        private readonly Dictionary<(int Potion, PersistentSetupTraits Traits), TraitLeaders> _traitLeaders = new();
        private readonly List<(int Potion, PersistentSetupTraits Traits)> _traitOrder = new();
        private readonly Dictionary<(int Potion, uint? Target), FocusLeaders> _focusLeaders = new();
        private readonly List<(int Potion, uint? Target)> _focusOrder = new();
        private readonly FlashScorer _scorer;
        private readonly bool _preserveTheft;
        private readonly FlashPlanCommitment? _planCommitment;
        private readonly bool _trackPlanSeats;

        public Pool(int initialCapacity, int maxCapacity, bool preserveTheft, FlashScorer scorer,
            FlashPlanCommitment? planCommitment)
        {
            _maxCapacity = maxCapacity;
            _preserveTheft = preserveTheft;
            _scorer = scorer;
            _planCommitment = planCommitment;
            _trackPlanSeats = planCommitment is { CopyPowerSeats: true };
            _states = new ChunkedArray<FlashBattle>(maxCapacity);
            _nodes = new ChunkedArray<int>(maxCapacity);
            _lineages = new ChunkedArray<int>(maxCapacity);
            _scores = new ChunkedArray<double>(maxCapacity);
            _ranks = new ChunkedArray<double>(maxCapacity);
            _hashes = new ChunkedArray<ulong>(maxCapacity);
            _facts = new ChunkedArray<RetentionFacts>(maxCapacity);
            _potionCosts = new ChunkedArray<int>(maxCapacity);
            _pureRanks = new ChunkedArray<double>(maxCapacity);
            _payoffs = new ChunkedArray<int>(maxCapacity);
            // The rank/selection scratch stays contiguous because Span.Sort needs a single span. It is
            // sized to the capacity bound once (three int arrays plus two flags, ~1 MB at width 512)
            // instead of being reallocated while the beam grows.
            _order = new int[maxCapacity];
            _rankOrder = new int[maxCapacity];
            _rankPosition = new int[maxCapacity];
            _taken = new bool[maxCapacity];
            _required = new bool[maxCapacity];
            _table = new int[TableSize(Math.Clamp(initialCapacity, 1, maxCapacity))];
        }

        private static int TableSize(int capacity)
        {
            int size = 1;
            while (size < capacity * 2)
                size <<= 1;
            return size;
        }

        private int FreeSlot(ulong hash, int mask)
        {
            int slot = (int)(hash & (ulong)mask);
            while (_table[slot] != 0)
                slot = (slot + 1) & mask;
            return slot;
        }

        private void GrowTable()
        {
            int[] table = new int[_table.Length * 2];
            int mask = table.Length - 1;
            for (int index = 0; index < Count; index++)
            {
                ulong hash = _hashes[index];
                int slot = (int)(hash & (ulong)mask);
                while (table[slot] != 0)
                    slot = (slot + 1) & mask;
                table[slot] = index + 1;
            }
            _table = table;
        }

        public int Count { get; private set; }

        public ref FlashBattle State(int index) => ref _states[index];

        public int Node(int index) => _nodes[index];

        public double Score(int index) => _scores[index];

        public double Rank(int index) => _ranks[index];

        public int PotionCost(int index) => _potionCosts[index];

        public RetentionFacts Facts(int index) => _facts[index];

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
            if (_trackPlanSeats)
            {
                _pureRanks[to] = _pureRanks[from];
                _payoffs[to] = _payoffs[from];
            }
        }

        /// <summary>Drops the tail after an incumbent bound; removed seats are not refilled.</summary>
        public void Truncate(int count)
        {
            Count = count;
            RebuildTable();
        }

        private void RebuildTable()
        {
            Array.Clear(_table);
            int mask = _table.Length - 1;
            for (int index = 0; index < Count; index++)
                _table[FreeSlot(_hashes[index], mask)] = index + 1;
        }

        public void Clear()
        {
            Count = 0;
            Array.Clear(_table);
        }

        /// <summary>The pool has no free seat; the next unique state needs a compaction or a drop.</summary>
        public bool IsFull => Count >= _maxCapacity;

        /// <summary>
        /// The first free slot, for building a state in place before <see cref="CommitReserved"/>. A
        /// chunk boundary or the capacity bound uses the scratch slot instead, so a duplicate or a
        /// terminal child never allocates a chunk. Any other call on this pool invalidates the
        /// returned reference.
        /// </summary>
        public ref FlashBattle Reserve()
        {
            // The per-action-layer pool reaches this bound only through a choice/commitment
            // expansion that multiplies one parent action into many children. Each unique child that
            // cannot get a seat is dropped and counted as a capacity boundary; the cumulative
            // end-turn pool is compacted in AddEndTurnChild instead.
            if (Count >= _maxCapacity)
                throw new FlashCapacityException("FlashBeam.Pool", _maxCapacity);
            if ((Count & ChunkedArray<FlashBattle>.ChunkMask) != 0)
            {
                _scratchReserved = false;
                return ref _states[Count];
            }
            _scratchReserved = true;
            return ref _scratch;
        }

        /// <summary>Adds the state built in the reserved slot; returns false when an identical state was already present.</summary>
        public bool CommitReserved(int node, double score, double rank, double pureRank, int potionCost,
            in FlashScoreFeatures features, int depth, int lineage, int payoffCount)
        {
            if (_scratchReserved)
            {
                return Insert(ref _scratch, inPlace: false, node, score, rank, pureRank, potionCost,
                    features, default, factsReady: false, depth, lineage, payoffCount);
            }
            return Insert(ref _states[Count], inPlace: true, node, score, rank, pureRank, potionCost,
                features, default, factsReady: false, depth, lineage, payoffCount);
        }

        /// <summary>Adds a scored state and computes its retention-only values when it is stored.</summary>
        public bool AddScored(in FlashBattle state, int node, double score, double rank, double pureRank,
            int potionCost, in FlashScoreFeatures features, int depth, int lineage, int payoffCount)
            => Insert(ref Unsafe.AsRef(in state), inPlace: false,
                node, score, rank, pureRank, potionCost, features, default, factsReady: false, depth,
                lineage, payoffCount);

        /// <summary>Adds a state whose retention facts are already computed (pool-to-pool copies).</summary>
        public bool Add(in FlashBattle state, int node, double score, double rank, double pureRank,
            int potionCost, in RetentionFacts facts, int lineage, int payoffCount)
            => Insert(ref Unsafe.AsRef(in state), inPlace: false,
                node, score, rank, pureRank, potionCost, default, facts, factsReady: true, facts.Depth,
                lineage, payoffCount);

        private bool Insert(ref FlashBattle state, bool inPlace, int node, double score, double rank,
            double pureRank, int potionCost, in FlashScoreFeatures features, in RetentionFacts knownFacts,
            bool factsReady, int depth, int lineage, int payoffCount)
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
                        if (_trackPlanSeats)
                        {
                            _pureRanks[existing] = pureRank;
                            _payoffs[existing] = payoffCount;
                        }
                        _facts[existing] = factsReady
                            ? knownFacts
                            // The byte-identical state has the same retention-only hand values; only
                            // the depth-dependent fields move with the better-ranked representative.
                            : RetentionFacts.From(features, depth,
                                _facts[existing].ReachableHandValue, _facts[existing].ZeroCostPlayableCount);
                        _lineages[existing] = lineage;
                    }
                    return false;
                }
                slot = (slot + 1) & mask;
            }
            // The capacity bound is checked only after the state is known to be unique: a duplicate or a
            // terminal child built in the scratch slot never needs a seat.
            if (Count >= _maxCapacity)
                throw new FlashCapacityException("FlashBeam.Pool", _maxCapacity);
            RetentionFacts facts = knownFacts;
            if (!factsReady)
            {
                (int reachableHandValue, int zeroCostPlayableCount) =
                    _scorer.ReachableHandPotential(state);
                facts = RetentionFacts.From(
                    features, depth, reachableHandValue, zeroCostPlayableCount);
            }
            if ((Count + 1) * 2 > _table.Length)
                GrowTable();
            ref FlashBattle destination = ref _states[Count];
            if (!inPlace)
                destination = state;
            _nodes[Count] = node;
            _lineages[Count] = lineage;
            _scores[Count] = score;
            _ranks[Count] = rank;
            if (_trackPlanSeats)
            {
                _pureRanks[Count] = pureRank;
                _payoffs[Count] = payoffCount;
            }
            _hashes[Count] = hash;
            _facts[Count] = facts;
            _potionCosts[Count] = potionCost;
            _table[FreeSlot(hash, _table.Length - 1)] = Count + 1;
            Count++;
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
        public void CopyTopTo(Pool target, int width, double offensiveShare, bool turnBoundary = false)
        {
            List<int> selected = SelectTop(width, offensiveShare, turnBoundary);
            for (int index = 0; index < selected.Count; index++)
            {
                int source = selected[index];
                target.Add(_states[source], _nodes[source], _scores[source], _ranks[source],
                    _trackPlanSeats ? _pureRanks[source] : 0d, _potionCosts[source],
                    _facts[source], _lineages[source],
                    _trackPlanSeats ? _payoffs[source] : 0);
            }
        }

        /// <summary>
        /// Compacts the pool to <paramref name="keep"/> states with the same selection rule as
        /// <see cref="CopyTopTo"/> (rank seats, the offensive share and every required representative)
        /// applied at the compaction width. The kept entries are moved to the front in pool order and
        /// the dedup table is rebuilt; the dropped count is returned.
        /// </summary>
        public int Compact(int keep, double offensiveShare, bool turnBoundary)
        {
            int count = Count;
            if (keep >= count)
                return 0;
            List<int> selected = SelectTop(keep, offensiveShare, turnBoundary);
            // SelectTop orders by rank position; compaction needs ascending source indexes so a move
            // never clobbers a later source. Destination slots (0..count-1) are always below the
            // current source, and selected sources are distinct.
            CollectionsMarshal.AsSpan(selected).Sort();
            for (int index = 0; index < selected.Count; index++)
            {
                int source = selected[index];
                if (source != index)
                    Move(source, index);
            }
            int dropped = count - selected.Count;
            Count = selected.Count;
            RebuildTable();
            return dropped;
        }

        /// <summary>
        /// The shared selection rule of <see cref="CopyTopTo"/> and <see cref="Compact"/>: rank
        /// leaders, the offensive share, then every required representative replacing the last
        /// ordinary seat. Returns the selected source indexes in rank order.
        /// </summary>
        private List<int> SelectTop(int width, double offensiveShare, bool turnBoundary)
        {
            int count = Count;
            List<int> selected = _selected;
            selected.Clear();
            if (count == 0)
                return selected;
            for (int index = 0; index < count; index++)
            {
                _order[index] = index;
                _taken[index] = false;
                _required[index] = false;
            }
            SortByRank(count);
            int seats = Math.Min(width, count);
            int offensive = (int)(seats * offensiveShare);
            int byScore = seats - offensive;
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
            // appended last), so the score leaders and then the required set are preserved. The
            // collection limits scale with the requested width exactly as before.
            CollectRepresentatives(required, count, width, turnBoundary);
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
            if (_planCommitment is { CopyPowerSeats: true })
                AdmitPlanCommitmentSeats(selected, required, width);
            CollectionsMarshal.AsSpan(selected).Sort(new RankPositionComparer(_rankPosition));
            return selected;
        }

        /// <summary>
        /// <c>BeamRetentionPolicy.AdmitPlanCommitmentRepresentatives</c>: a CopyPower plan keeps the
        /// best-ranked state of the three highest realized-payoff progress bands. A representative
        /// replaces only a selected non-required state with strictly lower progress (the existing
        /// <c>FindLastIndex</c>), so equal-progress seats are not displaced.
        /// </summary>
        private void AdmitPlanCommitmentSeats(List<int> selected, List<int> required, int width)
        {
            // The existing selection takes the three highest distinct progress bands, then each
            // band's MaxBy(BeamRankScore). Collect the bands first; the best member is scanned below.
            Span<int> progressBands = stackalloc int[3];
            int bandCount = 0;
            for (int index = 0; index < Count; index++)
            {
                int progress = _payoffs[index];
                if (progress <= 0)
                    continue;
                int insert = 0;
                while (insert < bandCount && progressBands[insert] > progress)
                    insert++;
                if (insert < bandCount && progressBands[insert] == progress)
                    continue;
                if (insert >= 3)
                    continue;
                int moveTo = Math.Min(bandCount, 2);
                while (moveTo > insert)
                {
                    progressBands[moveTo] = progressBands[moveTo - 1];
                    moveTo--;
                }
                progressBands[insert] = progress;
                if (bandCount < 3)
                    bandCount++;
            }
            // Pick the best pure-rank member of every retained progress band. The scan is bounded by
            // the pool and runs only for a CopyPower plan member.
            for (int group = 0; group < bandCount; group++)
            {
                int progress = progressBands[group];
                int best = -1;
                for (int index = 0; index < Count; index++)
                {
                    if (_payoffs[index] != progress)
                        continue;
                    if (best < 0 || _pureRanks[index] > _pureRanks[best])
                        best = index;
                }
                int candidate = best;
                int candidateProgress = progress;
                if (selected.Contains(candidate))
                {
                    AddRequired(required, candidate, width);
                    continue;
                }
                int replaceIndex = -1;
                for (int index = selected.Count - 1; index >= 0; index--)
                {
                    int node = selected[index];
                    if (!_required[node] && _payoffs[node] < candidateProgress)
                    {
                        replaceIndex = index;
                        break;
                    }
                }
                if (replaceIndex < 0)
                    continue;
                _taken[selected[replaceIndex]] = false;
                selected[replaceIndex] = candidate;
                _taken[candidate] = true;
                AddRequired(required, candidate, width);
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
        /// evaluates exactly: one leader set per potion count, the global leaders, the strongest
        /// root-action lineages (clamp(width / 8, 4, 16)), the turn-boundary hand leader, the setup
        /// trait and focus target groups, and the eight lane leaders.
        /// </summary>
        private void CollectRepresentatives(List<int> required, int count, int width, bool turnBoundary)
        {
            _lineageLeaders.Clear();
            _potionLeaders.Clear();
            _traitLeaders.Clear();
            _traitOrder.Clear();
            _focusLeaders.Clear();
            _focusOrder.Clear();
            int bestDefensive = -1, bestOffensive = -1, bestSetup = -1, bestUtilityDefensive = -1;
            int bestSetupCombo = -1, bestCuration = -1, bestCompressed = -1;
            int bestPotionFree = -1, bestPotionFreeDefensive = -1, bestPotionFreeOffensive = -1;
            int bestPotionFreeSetup = -1, bestPotionFreeSetupCombo = -1, bestPotionFreeUtilityDefensive = -1;
            int bestPotion = -1, bestPotionDefensive = -1, bestPotionOffensive = -1, bestPotionSetup = -1;
            int bestPotionSetupCombo = -1;
            int bestLongTermResource = -1, bestHpInvestment = -1, bestResourcePreserving = -1;
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
                if (potion.Tactical < 0 || IsBetterTacticalEnabler(facts, _facts[potion.Tactical]))
                    potion.Tactical = source;
                if (potion.TurnBoundary < 0
                    || IsBetterTurnBoundaryHand(facts, _facts[potion.TurnBoundary]))
                {
                    potion.TurnBoundary = source;
                }
                if (_preserveTheft
                    && (potion.Resource < 0 || IsBetterResourcePreserving(facts, _facts[potion.Resource])))
                {
                    potion.Resource = source;
                }
                for (int lane = 0; lane < RetainedLanes.Length; lane++)
                {
                    if (potion.Lanes[lane] < 0
                        || IsBetterLane(facts, _facts[potion.Lanes[lane]], RetainedLanes[lane]))
                    {
                        potion.Lanes[lane] = source;
                    }
                }

                // BeamRetentionPolicy.RankBest: per potion group and persistent setup trait, keep the
                // defensive and setup leaders of each signature (limit max(4, width / 6) groups).
                if (facts.SetupTraits != PersistentSetupTraits.None)
                {
                    (int, PersistentSetupTraits) traitKey = (potionCount, facts.SetupTraits);
                    if (!_traitLeaders.TryGetValue(traitKey, out TraitLeaders? trait))
                    {
                        trait = new TraitLeaders { Position = position };
                        _traitLeaders.Add(traitKey, trait);
                    }
                    else if (position < trait.Position)
                    {
                        trait.Position = position;
                    }
                    if (trait.Defensive < 0 || IsBetterDefensive(facts, _facts[trait.Defensive]))
                        trait.Defensive = source;
                    if (trait.Setup < 0 || IsBetterSetup(facts, _facts[trait.Setup]))
                        trait.Setup = source;
                }

                // BeamRetentionPolicy.RankBest: per potion group and focus target, keep the pressure
                // and target-setup leaders (limit clamp(width / 10, 2, 4) targets).
                if (facts.FocusTargetCombatId is { } focus)
                {
                    (int, uint?) focusKey = (potionCount, focus);
                    if (!_focusLeaders.TryGetValue(focusKey, out FocusLeaders? leader))
                    {
                        leader = new FocusLeaders { Position = position };
                        _focusLeaders.Add(focusKey, leader);
                    }
                    else if (position < leader.Position)
                    {
                        leader.Position = position;
                    }
                    if (facts.FocusTargetPressure > leader.MaxPressure)
                        leader.MaxPressure = facts.FocusTargetPressure;
                    if (leader.Pressure < 0 || IsBetterTargetPressure(facts, _facts[leader.Pressure]))
                        leader.Pressure = source;
                    if (leader.Setup < 0 || IsBetterTargetSetup(facts, _facts[leader.Setup]))
                        leader.Setup = source;
                }

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
                if (line.Tactical < 0 || IsBetterTacticalEnabler(facts, _facts[line.Tactical]))
                    line.Tactical = source;
                if (line.TargetPressure < 0
                    || IsBetterTargetPressure(facts, _facts[line.TargetPressure]))
                {
                    line.TargetPressure = source;
                }

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
                if (bestLongTermResource < 0
                    || IsBetterLane(facts, _facts[bestLongTermResource],
                        SearchRouteTraits.LongTermResource))
                {
                    bestLongTermResource = source;
                }
                if (bestHpInvestment < 0
                    || IsBetterLane(facts, _facts[bestHpInvestment], SearchRouteTraits.HpInvestment))
                {
                    bestHpInvestment = source;
                }
                if (bestUtilityDefensive < 0
                    || IsBetterUtilityDefensive(facts, _facts[bestUtilityDefensive]))
                {
                    bestUtilityDefensive = source;
                }
                if (_preserveTheft
                    && (bestResourcePreserving < 0
                        || IsBetterResourcePreserving(facts, _facts[bestResourcePreserving])))
                {
                    bestResourcePreserving = source;
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
                AddRequired(required, potion.Tactical, width);
                AddRequired(required, potion.Curation, width);
                AddRequired(required, potion.Compressed, width);
                if (turnBoundary)
                    AddRequired(required, potion.TurnBoundary, width);
                if (_preserveTheft)
                    AddRequired(required, potion.Resource, width);
                for (int lane = 0; lane < potion.Lanes.Length; lane++)
                    AddRequired(required, potion.Lanes[lane], width);
            }

            // BeamRetentionPolicy.RankBest: persistent setup trait groups (Focus, Thunder, OrbEngine,
            // …) keep their defensive and setup leaders; the group limit is max(4, width / 6).
            foreach ((int Potion, PersistentSetupTraits Traits) key in _traitLeaders.Keys)
                _traitOrder.Add(key);
            CollectionsMarshal.AsSpan(_traitOrder).Sort(new TraitOrderComparer(_traitLeaders));
            int traitLimit = Math.Max(4, width / 6);
            for (int index = 0; index < _traitOrder.Count && index < traitLimit; index++)
            {
                TraitLeaders trait = _traitLeaders[_traitOrder[index]];
                AddRequired(required, trait.Defensive, width);
                AddRequired(required, trait.Setup, width);
            }

            // BeamRetentionPolicy.RankBest: focus target groups (clamp(width / 10, 2, 4) targets per
            // potion group) keep their pressure and target-setup leaders.
            foreach ((int Potion, uint? Target) key in _focusLeaders.Keys)
                _focusOrder.Add(key);
            CollectionsMarshal.AsSpan(_focusOrder).Sort(new FocusOrderComparer(_focusLeaders));
            int focusLimit = Math.Clamp(width / 10, 2, 4);
            for (int index = 0; index < _focusOrder.Count && index < focusLimit; index++)
            {
                FocusLeaders leader = _focusLeaders[_focusOrder[index]];
                AddRequired(required, leader.Pressure, width);
                AddRequired(required, leader.Setup, width);
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
            AddRequired(required, bestLongTermResource, width);
            AddRequired(required, bestHpInvestment, width);
            if (_preserveTheft)
                AddRequired(required, bestResourcePreserving, width);

            _lineageOrder.Clear();
            foreach (int key in _lineageLeaders.Keys)
                _lineageOrder.Add(key);
            CollectionsMarshal.AsSpan(_lineageOrder).Sort(new LineageOrderComparer(_lineageLeaders, _rankPosition));
            int lineageLimit = Math.Clamp(width / 8, 4, 16);
            for (int index = 0; index < _lineageOrder.Count && index < lineageLimit; index++)
            {
                LineageLeaders lineage = _lineageLeaders[_lineageOrder[index]];
                AddRequired(required, lineage.Leader, width);
                AddRequired(required, lineage.Defensive, width);
                AddRequired(required, lineage.Offensive, width);
                AddRequired(required, lineage.Setup, width);
                AddRequired(required, lineage.SetupCombo, width);
                AddRequired(required, lineage.Tactical, width);
                AddRequired(required, lineage.Curation, width);
                AddRequired(required, lineage.Compressed, width);
                AddRequired(required, lineage.TargetPressure, width);
            }
        }

        /// <summary>
        /// BeamRetentionPolicy.Ranking.IsBetterResourcePreserving: fewer outstanding stolen resources,
        /// then higher projected HP, then a higher score.
        /// </summary>
        private static bool IsBetterResourcePreserving(in RetentionFacts candidate, in RetentionFacts current)
            => candidate.OutstandingStolenResource < current.OutstandingStolenResource
                || candidate.OutstandingStolenResource == current.OutstandingStolenResource
                    && (candidate.ProjectedPlayerHp > current.ProjectedPlayerHp
                        || candidate.ProjectedPlayerHp == current.ProjectedPlayerHp
                            && candidate.Score > current.Score);

        private readonly struct RankOrderComparer : IComparer<int>
        {
            private readonly ChunkedArray<double> _ranks;
            private readonly ChunkedArray<RetentionFacts> _facts;

            public RankOrderComparer(ChunkedArray<double> ranks, ChunkedArray<RetentionFacts> facts)
            {
                _ranks = ranks;
                _facts = facts;
            }

            public int Compare(int left, int right)
            {
                // CombatBeamSolver.CompareBeamRankOrder: rank, then fewer actions, then offensive progress.
                int comparison = _ranks[right].CompareTo(_ranks[left]);
                if (comparison != 0)
                    return comparison;
                comparison = _facts[left].Depth.CompareTo(_facts[right].Depth);
                if (comparison != 0)
                    return comparison;
                comparison = _facts[right].OffensiveProgressValue.CompareTo(_facts[left].OffensiveProgressValue);
                return comparison != 0 ? comparison : left.CompareTo(right);
            }
        }

        private readonly struct EnemyHpOrderComparer : IComparer<int>
        {
            private readonly ChunkedArray<RetentionFacts> _facts;
            private readonly int[] _rankPosition;

            public EnemyHpOrderComparer(ChunkedArray<RetentionFacts> facts, int[] rankPosition)
            {
                _facts = facts;
                _rankPosition = rankPosition;
            }

            public int Compare(int left, int right)
            {
                int comparison = _facts[left].EnemyHp.CompareTo(_facts[right].EnemyHp);
                return comparison != 0
                    ? comparison
                    : _rankPosition[left].CompareTo(_rankPosition[right]);
            }
        }

        private readonly struct RankPositionComparer : IComparer<int>
        {
            private readonly int[] _rankPosition;

            public RankPositionComparer(int[] rankPosition) => _rankPosition = rankPosition;

            public int Compare(int left, int right) => _rankPosition[left].CompareTo(_rankPosition[right]);
        }

        private readonly struct TraitOrderComparer
            : IComparer<(int Potion, PersistentSetupTraits Traits)>
        {
            private readonly Dictionary<(int Potion, PersistentSetupTraits Traits), TraitLeaders> _leaders;

            public TraitOrderComparer(
                Dictionary<(int Potion, PersistentSetupTraits Traits), TraitLeaders> leaders)
                => _leaders = leaders;

            public int Compare((int Potion, PersistentSetupTraits Traits) left,
                (int Potion, PersistentSetupTraits Traits) right)
                => _leaders[left].Position.CompareTo(_leaders[right].Position);
        }

        private readonly struct FocusOrderComparer : IComparer<(int Potion, uint? Target)>
        {
            private readonly Dictionary<(int Potion, uint? Target), FocusLeaders> _leaders;

            public FocusOrderComparer(Dictionary<(int Potion, uint? Target), FocusLeaders> leaders)
                => _leaders = leaders;

            public int Compare((int Potion, uint? Target) left, (int Potion, uint? Target) right)
            {
                FocusLeaders leftLeader = _leaders[left];
                FocusLeaders rightLeader = _leaders[right];
                int comparison = rightLeader.MaxPressure.CompareTo(leftLeader.MaxPressure);
                return comparison != 0
                    ? comparison
                    : leftLeader.Position.CompareTo(rightLeader.Position);
            }
        }

        private readonly struct LineageOrderComparer : IComparer<int>
        {
            private readonly Dictionary<int, LineageLeaders> _leaders;
            private readonly int[] _rankPosition;

            public LineageOrderComparer(Dictionary<int, LineageLeaders> leaders, int[] rankPosition)
            {
                _leaders = leaders;
                _rankPosition = rankPosition;
            }

            public int Compare(int left, int right)
                => _rankPosition[_leaders[left].Leader]
                    .CompareTo(_rankPosition[_leaders[right].Leader]);
        }

        private void SortByRank(int count)
        {
            _order.AsSpan(0, count).Sort(new RankOrderComparer(_ranks, _facts));
            for (int position = 0; position < count; position++)
            {
                int source = _order[position];
                _rankOrder[position] = source;
                _rankPosition[source] = position;
            }
        }

        private void SortByEnemyHp(int count)
            => _order.AsSpan(0, count).Sort(new EnemyHpOrderComparer(_facts, _rankPosition));
    }
}
