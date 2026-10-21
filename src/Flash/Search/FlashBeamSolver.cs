using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CombatSolver.Flash.Kernel;
using CombatSolver.Flash.Search.Scoring;
using CombatSolver.Flash.Bridge;

namespace CombatSolver.Flash.Search;

internal sealed record FlashBeamOptions(
    int BeamWidth,
    int MaxExpandedNodes,
    int MaxTurns,
    TimeSpan SoftTimeBudget,
    int InitialPlayerMaxHp)
{
    /// <summary>Number of final candidate routes retained for offline replay scanning (0 = off).</summary>
    public int CandidateScanCount { get; init; }

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
    /// potion-policy override enforces them, matching <c>the existing search's EnforcesPotionDirectives</c>.
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
    /// The existing exact-layer HP ceiling (<c>the existing search's _smartPotionEligibilityHpCeiling</c>).
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
    /// (<c>the existing search's Expansion</c>'s <c>_planCommitment</c>): the payoff evidence counted
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
    /// <c>the existing search</c>'s <c>earlyTurnScoutDepth</c>: stop after this many searched turn layers
    /// and report each completed layer's frontier. Zero disables the scout.
    /// </summary>
    public int EarlyTurnScoutDepth { get; init; }

    /// <summary>
    /// The request's <c>NoveltySearchOptions</c>. Set only for the coordinator's novelty exploration
    /// and adaptive refinement; the solver then runs the Bfws open-queue scheduler instead of the beam.
    /// </summary>
    public FlashNoveltyOptions? Novelty { get; init; }

    /// <summary>
    /// The request root's combat turn (before a fixed prefix), matching the existing solver's
    /// <c>_startTurnNumber</c> for the power-commitment turn offset.
    /// </summary>
    public int InitialPlayerTurn { get; init; }

    /// <summary>
    /// The first searched turn layer's combat turn, i.e. the search root after a fixed prefix. The
    /// existing scout filters each completed layer by <c>_startTurnNumber + searchedTurnLayers</c> over
    /// nodes that already include the prefix; with no prefix this equals <see cref="InitialPlayerTurn"/>.
    /// </summary>
    public int InitialSearchTurn { get; init; }

    /// <summary>
    /// <c>SolverSearchProfile.SecondRankBand</c>: the global prune's ordinary seats come from score
    /// ranks W+1..2W instead of the top W. Required representatives, power-commitment seats and plan
    /// seats are unchanged. Only the members that explicitly set the profile switch read it.
    /// </summary>
    public bool SecondRankBand { get; init; }

    /// <summary>
    /// The existing profile's band width (<c>effectiveLimit</c> in <c>RankBest</c>). Zero means the Flash
    /// layer width. The bridge freezes the request profile's width so the Flash-only 4x width does not
    /// move the W+1..2W band.
    /// </summary>
    public int SecondRankBandWidth { get; init; }

    /// <summary>
    /// <c>SolverSearchProfile.AggressivePowerCommitment</c>: raise the power-commitment seat quota to
    /// half the beam and extend a commitment's lifetime from two round transitions to three. The
    /// per-node commitment metadata is route history, not <see cref="FlashBattle"/> state.
    /// </summary>
    public bool AggressivePowerCommitment { get; init; }

    /// <summary>
    /// <c>CombatRootSnapshot.HasUnusedCardReplayAllocator</c>: the existing root-action lineage also
    /// keys the route's first played card when the allocator is unused.
    /// </summary>
    public bool PreserveReplayAllocatorOpening { get; init; }

    /// <summary>
    /// Per-card power-commitment descriptors compiled by the bridge, plus the request's frozen
    /// <c>TotalFloor</c>. Null when the root holds no registered power card: the existing solver also
    /// skips the whole lane for such roots.
    /// </summary>
    public FlashPowerCommitmentCatalog? PowerCommitments { get; init; }

    /// <summary>
    /// The shared frame-pressure pacer. The bridge creates it from
    /// <see cref="SearchPolicySnapshot.FramePressureSignal"/> so the Flash layer and expansion loops
    /// yield exactly like the existing search's replay checkpoints. Null in direct kernel tests.
    /// </summary>
    public SearchWorkPacer? Pacer { get; init; }

    /// <summary>
    /// Progress sink. The bridge translates one snapshot into an existing <c>SolverProgress</c> and
    /// materializes the route preview lazily; the solver only copies route metadata.
    /// </summary>
    public Action<FlashProgressSnapshot>? Progress { get; init; }

    /// <summary>
    /// Polls the pending takeover request. The bridge owns the <c>SearchInteractionState</c> mapping;
    /// the solver only sees the three-way decision.
    /// </summary>
    public Func<FlashTakeoverAction>? PollTakeover { get; init; }
}

/// <summary>One registered power card's existing commitment descriptor.</summary>
internal readonly record struct FlashPowerCommitmentRule(
    PowerCommitmentFamily Family,
    PowerRoutePriority Priority,
    PowerRouteAdmissionPolicy Admission,
    int Index);

/// <summary>
/// The root-frozen power-commitment catalog. Indexed by the kernel's card id; entries with
/// <see cref="PowerCommitmentFamily.None"/> are not registered as commitments.
/// </summary>
internal sealed record FlashPowerCommitmentCatalog(
    FlashPowerCommitmentRule[] Rules,
    int TotalFloor);

/// <summary>The payoff evidence kinds FlashBeam ports from <see cref="PlanCommitment"/>.</summary>
internal enum FlashPlanPayoffKind : byte
{
    CardPlayed,
    FreePotionUsed,
    RegisteredPowerBenefit,
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
    ushort PayoffCardId,
    ushort PayoffPotionId = ushort.MaxValue,
    int PayoffCardBit = -1);

/// <summary>One early-turn scout candidate: the route from the request root and its existing score.</summary>
internal sealed record FlashScoutCandidate(FlashAction[] Actions, double Score)
{
    /// <summary>Index of the turn-setup root the candidate started from (0 for an ordinary root).</summary>
    public int RootIndex { get; init; }
}

/// <summary>The candidates the existing search's observer reports after one searched turn layer.</summary>
internal sealed record FlashScoutLayer(int Turns, IReadOnlyList<FlashScoutCandidate> Candidates);

/// <summary>The request's HP-target stop (<c>the existing search's MeetsHpTarget</c>): a complete victory
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
    IReadOnlyList<FlashAction[]> TopRoutes,
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

    /// <summary>The novelty statistics of a <see cref="FlashBeamOptions.Novelty"/> request.</summary>
    public NoveltySearchTelemetry? NoveltyTelemetry { get; init; }

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

    /// <summary>Frame-pressure yields performed by the shared <see cref="SearchWorkPacer"/>.</summary>
    public int WorkerYieldCount { get; init; }

    /// <summary>How often the pacer waited for a main-thread frame after real frame pressure.</summary>
    public int FrameRecoveryWaitCount { get; init; }

    /// <summary>Total time spent waiting for frame recovery.</summary>
    public TimeSpan FrameRecoveryWaitDuration { get; init; }

    /// <summary>Largest GC pause observed by the pacer during this solve.</summary>
    public TimeSpan MaxObservedGcPause { get; init; }

    /// <summary>
    /// Set when the search stopped at a takeover checkpoint instead of running to its own stop
    /// condition. <see cref="BestRoute"/> then identifies the adopted current-turn route (or is empty
    /// for <see cref="FlashTakeoverAction.AdoptRoute"/>, where the bridge materializes the requested
    /// seed itself).
    /// </summary>
    public FlashTakeoverAction Takeover { get; init; }
}

/// <summary>
/// Turn-layered beam search over <see cref="FlashBattle"/>. Within a turn it expands card plays
/// depth by depth; end-turn children seed the next turn layer. Children are deduplicated by full
/// byte equality and the best score is kept. Only <see cref="FlashBattle"/> and
/// <see cref="FlashRules"/> are touched; translation and materialization happen in the bridge.
/// </summary>
[System.Runtime.CompilerServices.SkipLocalsInit]
internal sealed partial class FlashBeamSolver
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
    private readonly List<RouteNode> _nodes = new(4096);

    /// <summary>
    /// Routing-choice signatures accumulated on the route. They are node metadata only; no value here
    /// enters <see cref="FlashBattle"/>, the fingerprint or the state key.
    /// </summary>
    private readonly record struct RoutingSignature(
        int Turn,
        int SourceId,
        FlashChoiceEffect Effect,
        int SourcePile,
        int CardId,
        int Upgrade,
        int CardStateKey,
        int Occurrence,
        int CardCount,
        int ContextId,
        ulong EnemyCombatDistributionFirst,
        ulong EnemyCombatDistributionSecond,
        ulong EnemyControlDistributionFirst,
        ulong EnemyControlDistributionSecond,
        ulong UnorderedPileFirst,
        ulong UnorderedPileSecond);

    private readonly record struct RoutingChoice(
        int SignatureId, int Turn, int ChoiceDepth, int ParentRank, double ParentScore,
        int ParentEnergy);

    private readonly List<RoutingSignature> _routingSignatures = new();
    private readonly Dictionary<RoutingSignature, int> _routingSignatureIds = new();

    /// <summary>
    /// One route node's accumulated metadata. <see cref="SearchRouteTraits"/> and the potion-use
    /// lineage are route history: they never enter <see cref="FlashBattle"/>, the fingerprint or the
    /// state key.
    /// </summary>
    private readonly record struct RouteNode(
        int Parent,
        int Depth,
        FlashAction Action,
        ulong RootActionKey,
        ulong Lineage,
        int PayoffCount,
        ushort PotionId,
        ushort FirstCardId,
        int FirstCardTarget,
        int Commitment,
        int ActionCardId,
        int TargetCombatId,
        int RoutingSignatureId,
        int RoutingChoiceTurn,
        int RoutingChoiceDepth,
        int RoutingParentRank,
        double RoutingParentScore,
        int RoutingParentEnergy,
        int ParentTurn);

    /// <summary>
    /// Route-node power-commitment metadata (1-based node field; 0 means no commitment). The existing
    /// search holds <c>PowerCommitment</c> on <c>SearchNode</c>, not in the simulated state, so this list
    /// never enters <see cref="FlashBattle"/>, the fingerprint or the state key.
    /// </summary>
    private readonly List<FlashPowerCommitmentState> _commitments = new(256);
    private readonly FlashPowerCommitmentCatalog? _powerCommitments;
    private readonly bool _aggressivePowerCommitment;
    private readonly bool _preserveReplayAllocatorOpening;
    private bool _singleRoot;
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
    /// (<c>the existing search's Phases.cs</c>, <c>searchedTurnLayers == 0</c>).
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
    private readonly int _initialSearchTurn;
    private readonly List<FlashScoutLayer> _scoutLayers = [];

    private Stopwatch? _solveWatch;

    /// <summary>The physical expanded-parent count of the current solve (readable after a canceled solve).</summary>
    internal int ExpandedNodes => _expanded;

    /// <summary>The physical transition count of the current solve (readable after a canceled solve).</summary>
    internal int TransitionCount => _transitions;

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
        _initialSearchTurn = options.InitialSearchTurn > 0
            ? options.InitialSearchTurn : options.InitialPlayerTurn;
        _powerCommitments = options.PowerCommitments is { Rules.Length: > 0 } catalog ? catalog : null;
        _aggressivePowerCommitment = options.AggressivePowerCommitment;
        _preserveReplayAllocatorOpening = options.PreserveReplayAllocatorOpening;
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
        _singleRoot = roots.Count == 1;
        Stopwatch watch = Stopwatch.StartNew();
        _cancellationToken = cancellationToken;
        _solveWatch = watch;
        ResetProgressState();
        if (_options.Novelty is { } novelty)
            return SolveNovelty(roots, novelty, watch, cancellationToken);
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
        int bandWidth = _options.SecondRankBandWidth > 0
            ? _options.SecondRankBandWidth : width;
        bool checkPotionQuota = _effectivePotionPolicy != SolverPotionPolicy.Disabled;
        Pool current = new(frontierCapacity, frontierCapacity, preserveTheft, _scorer, _planCommitment,
            _options.SecondRankBand, bandWidth, _aggressivePowerCommitment, _powerCommitments, _commitments,
            _nodes, _routingSignatures, checkPotionQuota, _options.InitialActionCount,
            _options.PreserveReplayAllocatorOpening);
        Pool next = new(initialChildren, poolLimit, preserveTheft, _scorer, _planCommitment,
            _options.SecondRankBand, bandWidth, _aggressivePowerCommitment, _powerCommitments, _commitments,
            _nodes, _routingSignatures, checkPotionQuota, _options.InitialActionCount,
            _options.PreserveReplayAllocatorOpening);
        Pool ended = new(initialChildren, poolLimit, preserveTheft, _scorer, _planCommitment,
            _options.SecondRankBand, bandWidth, _aggressivePowerCommitment, _powerCommitments, _commitments,
            _nodes, _routingSignatures, checkPotionQuota, _options.InitialActionCount,
            _options.PreserveReplayAllocatorOpening);
        Pool turnFrontier = new(frontierCapacity, frontierCapacity, preserveTheft, _scorer, _planCommitment,
            _options.SecondRankBand, bandWidth, _aggressivePowerCommitment, _powerCommitments, _commitments,
            _nodes, _routingSignatures, checkPotionQuota, _options.InitialActionCount,
            _options.PreserveReplayAllocatorOpening);

        RouteKey? best = null;
        int bestNode = 0;
        bool bestVictory = false, bestDefeat = false;
        List<(RouteKey Key, int Node)> candidateRoutes = [];
        void Consider(in FlashBattle state, int node, in RouteKey key,
            in FlashScoreFeatures features)
        {
            if (best is { } current && CompareRoutes(key, current) >= 0)
                return;
            best = key;
            bestNode = node;
            bestVictory = state.Outcome == FlashOutcome.Victory;
            bestDefeat = state.Outcome == FlashOutcome.Defeat;
            _bestFeatures = features;
            _bestFeaturesNode = node;
        }
        // The existing search's two early stops. The HP-target stop ends the search before the next parent
        // (the existing search's Phases.cs, MeetsHpTarget); the zero-damage stop ends it after the current turn
        // once a victory lost no HP, used no explicit potion and left the player at full, undiminished HP
        // (growth targets, which disable it there, are rejected at admission).
        bool hpTargetReached = false, zeroDamageVictory = false;
        void ConsiderCandidate(in FlashBattle state, int node, in FlashScoreFeatures features, in RouteKey key)
        {
            if (_options.CandidateScanCount > 0)
                candidateRoutes.Add((key, node));
            if (_auditPotionPolicy)
                ConsiderAudited(node, features, key);
            else
                Consider(state, node, key, features);
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
        _commitments.Clear();
        _routingSignatures.Clear();
        _routingSignatureIds.Clear();
        for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            FlashBattle root = roots[rootIndex];
            (double rootScore, double rootRank, double rootPureRank, int rootPotionCost,
                FlashScoreFeatures rootFeatures) =
                Score(root, _options.InitialActionCount, terminal: false, isEndTurn: false);
            _nodes.Add(new RouteNode(-1, Math.Max(0, _options.InitialActionCount), default, 0, 0,
                _options.InitialPlanPayoffCount, ushort.MaxValue, ushort.MaxValue, int.MinValue, 0,
                0, int.MinValue, 0, 0, 0, int.MaxValue, 0d, 0, 0));
            (int rootReachable, int rootZeroCost, _) = _scorer.ReachableHandPotential(root);
            PocketwatchState(root, out int rootPocketwatchThreshold, out int rootPocketwatchPlayed,
                out int rootPocketwatchLastTurn);
            RetentionFacts rootFacts = RetentionFacts.From(
                rootFeatures, _options.InitialActionCount, rootReachable, rootZeroCost,
                SearchRouteTraits.None, rootPocketwatchThreshold, rootPocketwatchPlayed,
                rootPocketwatchLastTurn, _scorer.InitialRetainedAttackValue,
                retention: _scorer.RetentionValues(root));
            turnFrontier.Add(root, rootIndex, rootScore, rootRank, rootPureRank, rootPotionCost, rootFacts,
                0, _options.InitialPlanPayoffCount, 0);
        }
        TerminalSink terminal = ConsiderTerminal;
        PublishProgress(0, 0, turnFrontier.Count, 0, "初始化", force: true, routeNode: -1);
        int turns = 0;
        while (turnFrontier.Count > 0 && turns < _options.MaxTurns && !OutOfBudget(watch)
            && !hpTargetReached && _takeoverAction == FlashTakeoverAction.None
            && (_earlyTurnScoutDepth == 0 || turns < _earlyTurnScoutDepth))
        {
            SearchCheckpoint();
            if (TryHandleTakeover(turnFrontier))
                break;
            turns++;
            current.Clear();
            turnFrontier.CopyTopTo(current, turns == 1 ? frontierCapacity : width, 0d);
            ended.Clear();
            // Turn one is the existing search's searchedTurnLayers == 0.
            _expandFetchedPowers = _options.Act3BossStrategy && turns == 1;
            PublishProgressWithPreview(turns - 1, 0, current.Count, 0, "展开回合", force: true,
                best is null ? -1 : bestNode, best, current);
            int playDepth = 0;
            while (current.Count > 0 && !OutOfBudget(watch) && !hpTargetReached
                && _takeoverAction == FlashTakeoverAction.None)
            {
                SearchCheckpoint();
                if (TryHandleTakeover(current))
                    break;
                next.Clear();
                for (int index = 0; index < current.Count && !OutOfBudget(watch) && !hpTargetReached; index++)
                {
                    SearchCheckpoint();
                    _expanded++;
                    ref FlashBattle parent = ref current.State(index);
                    int parentNode = current.Node(index);
                    int count = FlashKernel.LegalActions(parent, _rules, _actions);
                    for (int actionIndex = 0; actionIndex < count; actionIndex++)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        if (_actions[actionIndex].Kind == FlashActionKind.UsePotion && !PotionAllowed(parent, _actions[actionIndex].PotionSlot))
                            continue;
                        ExpandAction(ref parent, parentNode, _actions[actionIndex], next, ended, terminal,
                            current.Facts(index), current.RankPosition(index), current.Rank(index));
                    }
                    PublishProgressWithPreview(turns - 1, playDepth,
                        Math.Max(0, current.Count - index - 1) + next.Count, ended.Count,
                        "展开出牌序列", force: false,
                        best is null ? -1 : bestNode, best, current);
                }
                current.Clear();
                next.CopyTopTo(current, width, _options.OffensiveShare);
                PrunePool(current);
                PublishProgressWithPreview(turns - 1, playDepth, current.Count, ended.Count,
                    "剪枝候选", force: true,
                    best is null ? -1 : bestNode, best, current);
                playDepth++;
            }
            if (_takeoverAction != FlashTakeoverAction.None)
                break;
            if (_earlyTurnScoutDepth > 0 && turns <= _earlyTurnScoutDepth)
                CollectEarlyTurnScout(ended, turns, roots);
            turnFrontier.Clear();
            // BeamRetentionPolicy.RankBest at the end-turn frontier: the turn-boundary hand leader is
            // a required representative of every potion group.
            ended.CopyTopTo(turnFrontier, width, _options.OffensiveShare, turnBoundary: true);
            PrunePool(turnFrontier);
            PublishProgressWithPreview(turns, 0, turnFrontier.Count, ended.Count, "回合层完成",
                force: true, best is null ? -1 : bestNode, best, turnFrontier);
            if (TryHandleTakeover(turnFrontier))
                break;
            if (zeroDamageVictory)
                break;
        }
        PublishProgressWithPreview(turns, 0, turnFrontier.Count, ended.Count, "复核最终候选",
            force: true, best is null ? -1 : bestNode, best, turnFrontier);
        if (_takeoverAction != FlashTakeoverAction.None)
            return TakeoverResult(turns);

        FlashAction[][] TopCandidateRoutes()
        {
            int count = Math.Min(_options.CandidateScanCount, candidateRoutes.Count);
            if (count <= 0)
                return [];
            List<(RouteKey Key, int Node)> ordered = [.. candidateRoutes];
            ordered.Sort((left, right) =>
            {
                int comparison = CompareRoutes(left.Key, right.Key);
                return comparison != 0 ? comparison : left.Node.CompareTo(right.Node);
            });
            return ordered.Take(count).Select(item => Route(item.Node)).ToArray();
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
                TopCandidateRoutes(), bestVictory, bestDefeat, best?.Score ?? double.NegativeInfinity,
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
                WorkerYieldCount = _options.Pacer?.YieldCount ?? 0,
                FrameRecoveryWaitCount = _options.Pacer?.FrameRecoveryWaitCount ?? 0,
                FrameRecoveryWaitDuration = _options.Pacer?.FrameRecoveryWaitDuration ?? TimeSpan.Zero,
                MaxObservedGcPause = _options.Pacer?.MaxObservedGcPause ?? TimeSpan.Zero,
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
                FlashScoreFeatures features =
                    _scorer.Features(state, actionCount, includeRetentionOnly: false);
                Consider(state, node, Key(state, features, actionCount), features);
            }
        }
        FlashBeamResult result = new(Route(bestNode), TopCandidateRoutes(), bestVictory, bestDefeat,
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
            WorkerYieldCount = _options.Pacer?.YieldCount ?? 0,
            FrameRecoveryWaitCount = _options.Pacer?.FrameRecoveryWaitCount ?? 0,
            FrameRecoveryWaitDuration = _options.Pacer?.FrameRecoveryWaitDuration ?? TimeSpan.Zero,
            MaxObservedGcPause = _options.Pacer?.MaxObservedGcPause ?? TimeSpan.Zero,
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
            features.PotionUseCount,
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
        // recovers outstanding loot; the soft-policy exception requires an actual potion use, while
        // the Ambergris exception applies to the recovered-loot key even with zero potions.
        bool recoversLoot = _theftPolicy == SolverTheftPolicy.PreserveResources
            && candidate.Key.OutstandingStolenResource < potionFreeOutstanding;
        bool recoversLootWithPotion = recoversLoot && candidate.PotionUseCount > 0;
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
            || recoversLootWithPotion;
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
    /// <c>the existing search's FinalPlanOrdering.ComparePotionFreePolicyBaselines</c> restricted to the
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
        int BoundaryRank,
        int EnemyHp,
        double Score,
        int ActionCount);

    /// <summary>An audited final candidate: the ordering key plus the values its policy filter reads.</summary>
    private readonly record struct Candidate(
        int Node,
        RouteKey Key,
        int ExplicitPotionUseCount,
        int PotionUseCount,
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
            // the existing search's PolicyBoundaryRank: None 0, EventDefeat 4. The final ordering compares
            // it after the resource/anger keys and before enemy HP (FinalPlanOrdering).
            state.Outcome == FlashOutcome.EventDefeat ? 4 : 0,
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
        comparison = left.BoundaryRank.CompareTo(right.BoundaryRank);
        if (comparison != 0) return comparison;
        comparison = left.EnemyHp.CompareTo(right.EnemyHp);
        if (comparison != 0) return comparison;
        comparison = right.Score.CompareTo(left.Score);
        return comparison != 0 ? comparison : left.ActionCount.CompareTo(right.ActionCount);
    }

    private void ExpandAction(ref FlashBattle parent, int parentNode, FlashAction action, Pool next,
        Pool ended, TerminalSink terminal, in RetentionFacts parentFacts, int parentRank,
        double parentScore)
        => ExpandAction(ref parent, parentNode, action, next, ended, terminal, parentFacts,
            parentRank, parentScore, null, default, null, allowFetchedPowerCommit: true);

    private void ExpandAction(ref FlashBattle parent, int parentNode, FlashAction action, Pool next, Pool ended,
        TerminalSink terminal, in RetentionFacts parentFacts, int parentRank, double parentScore,
        FlashChoiceEffect? primaryEffect, scoped ReadOnlySpan<FlashCard> selectedPowers,
        RoutingChoice? routingChoice, bool allowFetchedPowerCommit)
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
            stepResult = FlashKernel.Step(ref child, action, _rules, out request, out bool historyRisk);
            if (stepResult == FlashStepResult.NeedsChoice)
            {
                // Every executed selection is exact; the cap only bounds how many are explored.
                Span<ulong> selections = stackalloc ulong[MaxChoiceBranches];
                int count = FlashChoiceEnumerator.Enumerate(request, selections);
                // The branch loop below expands every selection through the same pool. A recursive
                // ExpandAction reserves and overwrites next's current slot, so `child` is no longer a
                // stable view of the partial state once the first branch runs. Capture the option card
                // values now: they are the only child state the loop and its routing metadata read.
                Span<FlashCard> optionCards = stackalloc FlashCard[request.OptionCount];
                for (int option = 0; option < request.OptionCount; option++)
                {
                    if (request.OptionsAreGenerated)
                    {
                        optionCards[option] = request.GeneratedOptions[option];
                        continue;
                    }
                    int position = request.Options[option];
                    if ((uint)position >= FlashLimits.Cards)
                        throw new InvalidOperationException(
                            $"选择选项 {option} 的卡牌位置 {position} 超出牌数组容量。");
                    optionCards[option] = child.Cards[position];
                }
                bool captureFetchedPowers = allowFetchedPowerCommit && primaryEffect is null
                    && request.Timing == FlashChoiceTiming.Action
                    && IsPrimaryMoveToHandAction(parent, action)
                    && request.Effect == FlashChoiceEffect.MoveToHand
                    && !request.OptionsAreGenerated;
                Span<bool> optionIsPower = stackalloc bool[captureFetchedPowers ? request.OptionCount : 0];
                for (int option = 0; option < optionIsPower.Length; option++)
                    optionIsPower[option] =
                        FlashKernel.CardType(child, _rules, optionCards[option]) == FlashCardType.Power;
                Span<FlashCard> capturedPowers = stackalloc FlashCard[FlashLimits.MaxHandSize];
                for (int index = 0; index < count; index++)
                {
                    FlashChoiceEffect? effect = primaryEffect;
                    scoped ReadOnlySpan<FlashCard> chosenPowers = selectedPowers;
                    if (captureFetchedPowers)
                    {
                        effect = request.Effect;
                        int captured = 0;
                        ulong bits = selections[index];
                        for (int option = 0; option < request.OptionCount; option++)
                        {
                            if ((bits & (1UL << option)) == 0 || !optionIsPower[option])
                                continue;
                            if (captured >= capturedPowers.Length)
                                throw new FlashCapacityException("FetchedPowers", capturedPowers.Length);
                            capturedPowers[captured++] = optionCards[option];
                        }
                        chosenPowers = capturedPowers[..captured];
                    }
                    try
                    {
                        RoutingChoice? answered = routingChoice;
                        if (TryBuildRoutingChoice(parent, action, request, optionCards, selections[index],
                                parentNode, parentFacts.Turn, parentRank, parentScore, parentFacts,
                                out RoutingChoice choiceFacts))
                        {
                            answered = choiceFacts;
                        }
                        ExpandAction(ref parent, parentNode, action.WithChoice(selections[index]), next, ended,
                            terminal, parentFacts, parentRank, parentScore, effect, chosenPowers, answered,
                            allowFetchedPowerCommit);
                    }
                    catch (FlashCapacityException ex)
                    {
                        RecordCapacityDrop(ex);
                    }
                }
                return;
            }
            int depth = _nodes[parentNode].Depth + 1;
            bool isTerminal = !FlashKernel.IsInProgress(child);
            (double score, double rank, double pureRank, int potionCost, FlashScoreFeatures features) =
                Score(child, depth, isTerminal, action.Kind == FlashActionKind.EndTurn);
            // Route traits are accumulated from the K4-checked parent/child feature values. They are
            // node metadata only: the state bytes, fingerprint and key are untouched.
            bool nonPure = !IsPureTransition(parent, child, action, _rules, historyRisk);
            SearchRouteTraits childTraits = ClassifyRouteTraits(parentFacts, features, action,
                DeclinedExtraTurn(parent), nonPure);
            PocketwatchState(child, out int childPocketwatchThreshold, out int childPocketwatchPlayed,
                out int childPocketwatchLastTurn);
            // Power-commitment metadata is route history; it is computed before the node is published
            // so the pool can store the 1-based commitment index with the seat. Terminal children
            // carry none.
            int commitment = isTerminal
                ? 0
                : AttachPowerCommitment(parentNode, parent, child, action, parentFacts, features, depth);
            int node = _nodes.Count;
            ulong lineage = NextLineage(parentNode, parent, action, node,
                out ulong rootActionKey, out ushort firstCardId, out int firstCardTarget);
            int payoffCount = NextPayoffCount(parentNode, parent, action, features, parentFacts, commitment);
            RouteNode parentRoute = _nodes[parentNode];
            int routingSignature = routingChoice?.SignatureId ?? 0;
            int routingTurn, routingDepth, routingParentRank, routingParentEnergy;
            double routingParentScore;
            if (routingSignature != 0)
            {
                routingTurn = routingChoice!.Value.Turn;
                routingDepth = routingChoice.Value.ChoiceDepth;
                routingParentRank = routingChoice.Value.ParentRank;
                routingParentScore = routingChoice.Value.ParentScore;
                routingParentEnergy = routingChoice.Value.ParentEnergy;
            }
            else
            {
                routingTurn = parentRoute.RoutingChoiceTurn;
                routingDepth = parentRoute.RoutingChoiceDepth;
                routingParentRank = parentRoute.RoutingParentRank;
                routingParentScore = parentRoute.RoutingParentScore;
                routingParentEnergy = parentRoute.RoutingParentEnergy;
            }
            int actionCardId = action.Kind == FlashActionKind.PlayCard && action.HandIndex < parent.HandCount
                ? parent.Cards[action.HandIndex].Id : 0;
            _nodes.Add(new RouteNode(parentNode, depth, action, rootActionKey, lineage, payoffCount,
                usedPotion, firstCardId, firstCardTarget, commitment, actionCardId,
                TargetCombatId(parent, action), routingSignature, routingTurn, routingDepth,
                routingParentRank, routingParentScore, routingParentEnergy, parentFacts.Turn));
            if (_options.Novelty != null)
                ObserveNoveltyChild(child, node, features);
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
                // the existing search's Phases.cs: settle the fetched, payable power's next decision before
                // ranking the intermediate selection. The intermediate state is not committed; every
                // successor goes through the normal transition/dedup/pool accounting.
                ExpandFetchedPowerSuccessors(ref child, node, next, ended, terminal, childTraits,
                    childPocketwatchThreshold, childPocketwatchPlayed, parentRank, parentScore,
                    parentFacts.EnemyBlock, parentFacts.EnemyHp, parentFacts.FutureResourceValue,
                    parentFacts.StrategicResourcePotential);
                return;
            }
            // The retention-only hand values are computed by the pool only when the state is actually
            // stored (a duplicate keeps its existing values); a terminal state never needs them.
            bool added = action.Kind == FlashActionKind.EndTurn || child.PlayerTurn != parent.PlayerTurn
                ? AddEndTurnChild(ended, child, node, score, rank, pureRank, potionCost, features, depth,
                    lineage, payoffCount, commitment, childTraits, childPocketwatchThreshold,
                    childPocketwatchPlayed, childPocketwatchLastTurn, parentFacts.EnemyBlock, parentFacts.EnemyHp,
                    parentFacts.FutureResourceValue, parentFacts.StrategicResourcePotential)
                : next.CommitReserved(node, score, rank, pureRank, potionCost, features, depth, lineage,
                    payoffCount, commitment, childTraits, childPocketwatchThreshold,
                    childPocketwatchPlayed, childPocketwatchLastTurn, parentFacts.EnemyBlock, parentFacts.EnemyHp,
                    parentFacts.FutureResourceValue, parentFacts.StrategicResourcePotential);
            if (!added)
                _duplicates++;
        }
        catch (FlashCapacityException ex)
        {
            RecordCapacityDrop(ex);
        }
    }

    /// <summary>
    /// Builds the routing-choice signature of one answered selection request. The existing
    /// <c>RoutingChoiceSignature</c> also carries source/context ids and three enemy/pile distribution
    /// fingerprints; Flash has no string source ids or distribution keys on the route node, so those
    /// dimensions are not part of the key (choices from different sources of the same turn with the same
    /// effect and option merge). The supported effect set is the non-mutation routing set; persistent
    /// ordered-mutation effects stay in the RS2 batch.
    /// </summary>
    private bool TryBuildRoutingChoice(in FlashBattle parent, in FlashAction action,
        in FlashChoiceRequest request, scoped ReadOnlySpan<FlashCard> optionCards, ulong selection,
        int parentNode, int parentTurn, int parentRank, double parentScore, in RetentionFacts parentFacts,
        out RoutingChoice choice)
    {
        choice = default;
        if (request.Effect is not (FlashChoiceEffect.MoveToHand or FlashChoiceEffect.MoveToDrawTop
            or FlashChoiceEffect.Discard or FlashChoiceEffect.GenerateToHand))
        {
            return false;
        }
        int count = System.Numerics.BitOperations.PopCount(selection);
        if (count <= 0)
            return false;
        int cardId = 0, upgrade = 0, stateKey = 0, occurrence = 0, cardCount = 1;
        if (count > 1)
        {
            cardCount = count;
            occurrence = count;
        }
        else
        {
            int option = System.Numerics.BitOperations.TrailingZeroCount(selection);
            FlashCard card = optionCards[option];
            cardId = card.Id;
            upgrade = card.Upgrade;
            stateKey = CardStateKey(card);
            occurrence = option;
        }
        // The existing PlanCardChoice.SourceId is the requesting card's entry (the potion's entry
        // for potion choices, the relic's for turn-start relic choices). Flash has no string on the
        // request, so the currently played card or potion identifies the source; a card/potion id is
        // prefixed with its kind so the two id spaces cannot merge.
        int sourceId = action.Kind switch
        {
            FlashActionKind.PlayCard when action.HandIndex < parent.HandCount
                => (1 << 16) | parent.Cards[action.HandIndex].Id,
            FlashActionKind.UsePotion when action.PotionSlot < parent.PotionSlotCount
                => (2 << 16) | parent.Potions[action.PotionSlot],
            _ => 0,
        };
        int turn = action.Kind == FlashActionKind.EndTurn
            && request.Timing == FlashChoiceTiming.PlayerTurnStart
                ? parentTurn + 1
                : parentTurn;
        RoutingSignature signature = new(turn, sourceId, request.Effect,
            request.SourceIsNone ? -1 : (int)request.Source, cardId, upgrade, stateKey, occurrence,
            cardCount, ContextId: 0, parentFacts.EnemyCombatDistributionFirst,
            parentFacts.EnemyCombatDistributionSecond, parentFacts.EnemyControlDistributionFirst,
            parentFacts.EnemyControlDistributionSecond, parentFacts.UnorderedPileKeyFirst,
            parentFacts.UnorderedPileKeySecond);
        if (!_routingSignatureIds.TryGetValue(signature, out int id))
        {
            _routingSignatures.Add(signature);
            id = _routingSignatures.Count;
            _routingSignatureIds.Add(signature, id);
        }
        choice = new RoutingChoice(id, turn, _nodes[parentNode].Depth + 1, parentRank, parentScore,
            parentFacts.Energy);
        return true;
    }

    /// <summary>A 32-bit state key for the selected option card: FNV-1a over the card value. It stands
    /// in for the existing <c>PlanCardToken.StateKey</c>, which distinguishes equal-id card variants.</summary>
    private static int CardStateKey(in FlashCard card)
    {
        ulong hash = 1469598103934665603UL;
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in card), 1));
        for (int index = 0; index < bytes.Length; index++)
            hash = (hash ^ bytes[index]) * 1099511628211UL;
        return (int)(hash ^ (hash >> 32));
    }

    /// <summary>
    /// The existing <c>BeamRetentionPolicy.BuildRootActionLineageSignature</c> over Flash actions.
    /// The root action's kind/card/potion/target is frozen on the first step; when the root has an
    /// unused card replay allocator the route's first played card is part of the signature too. The
    /// signature is a stable value, so equal first actions from different turn-setup roots group
    /// together exactly as the existing search groups them.
    /// </summary>
    private ulong NextLineage(int parentNode, in FlashBattle parent, in FlashAction action, int node,
        out ulong rootActionKey, out ushort firstCardId, out int firstCardTarget)
    {
        RouteNode entry = _nodes[parentNode];
        rootActionKey = entry.RootActionKey;
        firstCardId = entry.FirstCardId;
        firstCardTarget = entry.FirstCardTarget;
        if (entry.Parent >= 0)
        {
            if (_preserveReplayAllocatorOpening && firstCardId == ushort.MaxValue
                && action.Kind == FlashActionKind.PlayCard && action.HandIndex < parent.HandCount)
            {
                firstCardId = parent.Cards[action.HandIndex].Id;
                firstCardTarget = TargetCombatId(parent, action);
                return CombineLineage(rootActionKey, firstCardId, firstCardTarget);
            }
            return entry.Lineage;
        }
        rootActionKey = RootActionSignature(parent, action);
        firstCardId = ushort.MaxValue;
        firstCardTarget = int.MinValue;
        if (_preserveReplayAllocatorOpening && action.Kind == FlashActionKind.PlayCard
            && action.HandIndex < parent.HandCount)
        {
            firstCardId = parent.Cards[action.HandIndex].Id;
            firstCardTarget = TargetCombatId(parent, action);
        }
        // With one root every first action used to carry its own lineage. Keep that exact behavior:
        // the signature grouping is only needed to join the same opening across turn-setup roots,
        // and changing a single-root grouping would change the retained representatives.
        return _singleRoot ? (ulong)node : CombineLineage(rootActionKey, firstCardId, firstCardTarget);
    }

    private static ulong RootActionSignature(in FlashBattle parent, in FlashAction action)
    {
        ulong kind = action.Kind switch
        {
            FlashActionKind.PlayCard => 1UL,
            FlashActionKind.UsePotion => 2UL,
            _ => 3UL,
        };
        ulong cardId = 0;
        ulong potionId = 0;
        if (action.Kind == FlashActionKind.PlayCard && action.HandIndex < parent.HandCount)
            cardId = parent.Cards[action.HandIndex].Id;
        if (action.Kind == FlashActionKind.UsePotion
            && (uint)action.PotionSlot < (uint)parent.PotionSlotCount
            && parent.Potions[action.PotionSlot] != 0)
        {
            potionId = (ushort)(parent.Potions[action.PotionSlot] - 1);
        }
        int target = TargetCombatId(parent, action);
        ulong hash = 1469598103934665603UL;
        hash = MixLineage(hash, kind);
        hash = MixLineage(hash, cardId);
        hash = MixLineage(hash, potionId);
        hash = MixLineage(hash, target == int.MinValue ? 0UL : (ulong)(uint)target + 1UL);
        return hash;
    }

    private static ulong CombineLineage(ulong rootActionKey, ushort firstCardId, int firstCardTarget)
    {
        ulong hash = MixLineage(rootActionKey, firstCardId == ushort.MaxValue ? 0UL : (ulong)firstCardId + 1UL);
        return MixLineage(hash, firstCardTarget == int.MinValue ? 0UL : (ulong)(uint)firstCardTarget + 1UL);
    }

    private static ulong MixLineage(ulong hash, ulong value) => (hash ^ value) * 1099511628211UL;

    /// <summary>The existing <c>PlanAction.TargetCombatId</c> for a Flash target owner.</summary>
    private static int TargetCombatId(in FlashBattle parent, in FlashAction action)
    {
        if (action.Target == FlashLimits.NoOwner)
            return int.MinValue;
        int enemy = action.Target - FlashLimits.FirstEnemyOwner;
        if ((uint)enemy >= (uint)parent.EnemyCount)
            return int.MinValue;
        return (int)parent.Enemies[enemy].CombatId;
    }

    /// <summary>
    /// The cumulative end-turn layer is the only pool that can reach its bound (the per-action-layer
    /// pool is cleared each layer and receives at most <c>width * MaxLegalActions</c> children). When
    /// a unique end-turn child no longer fits, compact the pool with the same retention rule the
    /// turn boundary uses, then add the child. Duplicates never reach the bound and stay free.
    /// </summary>
    private bool AddEndTurnChild(Pool ended, in FlashBattle child, int node, double score, double rank,
        double pureRank, int potionCost, in FlashScoreFeatures features, int depth, ulong lineage,
        int payoffCount, int commitment, SearchRouteTraits traits, int pocketwatchThreshold,
        int pocketwatchPlayed, int pocketwatchLastTurn, int parentEnemyBlock, int parentEnemyHp,
        int parentFutureResourceValue, int parentStrategicResourcePotential)
    {
        try
        {
            return ended.AddScored(child, node, score, rank, pureRank, potionCost, features, depth,
                lineage, payoffCount, commitment, traits, pocketwatchThreshold, pocketwatchPlayed,
                pocketwatchLastTurn, parentEnemyBlock, parentEnemyHp, parentFutureResourceValue,
                parentStrategicResourcePotential);
        }
        catch (FlashCapacityException) when (ended.IsFull)
        {
            int dropped = ended.Compact(_endedCompactionTarget, _options.OffensiveShare, turnBoundary: true);
            _endedCompactions++;
            _endedCompressedStates += dropped;
            return ended.AddScored(child, node, score, rank, pureRank, potionCost, features, depth,
                lineage, payoffCount, commitment, traits, pocketwatchThreshold, pocketwatchPlayed,
                pocketwatchLastTurn, parentEnemyBlock, parentEnemyHp, parentFutureResourceValue,
                parentStrategicResourcePotential);
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
        Pool next, Pool ended, TerminalSink terminal, SearchRouteTraits traits,
        int pocketwatchThreshold, int pocketwatchPlayed, int parentRank, double parentScore,
        int parentEnemyBlock, int parentEnemyHp, int parentFutureResourceValue,
        int parentStrategicResourcePotential)
    {
        _expanded++;
        FlashBattle state = commitment;
        // The intermediate selection state is the parent of every successor. Its retention facts are
        // computed once; the retained state itself is never committed.
        int commitmentDepth = Depth(commitmentNode);
        FlashScoreFeatures commitmentFeatures =
            _scorer.Features(state, commitmentDepth, includeRetentionOnly: true);
        (int commitmentReachable, int commitmentZeroCost, _) = _scorer.ReachableHandPotential(state);
        RetentionFacts commitmentFacts = RetentionFacts.From(commitmentFeatures, commitmentDepth,
            commitmentReachable, commitmentZeroCost, traits, pocketwatchThreshold, pocketwatchPlayed,
            _scorer.InitialRetainedAttackValue, parentEnemyBlock, parentEnemyHp,
            parentFutureResourceValue, parentStrategicResourcePotential);
        int count = FlashKernel.LegalActions(state, _rules, _commitmentActions);
        for (int index = 0; index < count; index++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            FlashAction action = _commitmentActions[index];
            if (action.Kind == FlashActionKind.UsePotion && !PotionAllowed(state, action.PotionSlot))
                continue;
            // ExpandAction handles a capacity overflow for this successor as its own child; a
            // sibling's overflow never cancels the rest of the commitment batch.
            ExpandAction(ref state, commitmentNode, action, next, ended, terminal, commitmentFacts,
                parentRank, parentScore, null, default, null, allowFetchedPowerCommit: false);
            if (!CommitmentBudgetAvailable())
                break;
        }
    }

    /// <summary>
    /// <c>the existing search's HasPlayableFetchedPower</c>: the choice moved a card to hand and a matching
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
    /// <c>the existing search's EarlyTurnStrategyKey</c> without <c>SearchRouteTraits</c>: FlashBeam does not
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
    /// <c>the existing search's SelectEarlyTurnFrontier</c> over the ended pool: filter the completed turn,
    /// keep the top two scores per strategy key, group by opening, then round-robin the opening groups
    /// up to 24 candidates. The pool already holds one representative per byte-identical state.
    /// </summary>
    private void CollectEarlyTurnScout(Pool ended, int completedTurns, IReadOnlyList<FlashBattle> roots)
    {
        List<FlashScoutEntry> entries = new(ended.Count);
        for (int index = 0; index < ended.Count; index++)
        {
            ref FlashBattle state = ref ended.State(index);
            if (state.PlayerTurn != _initialSearchTurn + completedTurns)
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
        // the existing search's TightenPrimarySearchIncumbentAtTurnLayer: only a potion-free victory, or an
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
        if (PrimaryIncumbentPolicy.TryTightenPrimarySearchIncumbent(
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
                incumbentPrune = PrimaryIncumbentPolicy.ShouldPruneByPrimaryIncumbent(
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
    private int NextPayoffCount(int parentNode, in FlashBattle parent, in FlashAction action,
        in FlashScoreFeatures childFeatures, in RetentionFacts parentFacts, int commitment)
    {
        if (_planCommitment is not { } plan)
            return 0;
        int count = _nodes[parentNode].PayoffCount;
        switch (plan.PayoffKind)
        {
            case FlashPlanPayoffKind.CardPlayed:
                if (parent.PlayerTurn < plan.EarliestTurn
                    || action.Kind != FlashActionKind.PlayCard
                    || action.HandIndex >= parent.HandCount
                    || parent.Cards[action.HandIndex].Id != plan.PayoffCardId)
                {
                    return count;
                }
                return checked(count + 1);
            case FlashPlanPayoffKind.FreePotionUsed:
                if (parent.PlayerTurn < plan.EarliestTurn
                    || action.Kind != FlashActionKind.UsePotion
                    || action.PotionSlot >= parent.PotionSlotCount
                    || parent.Potions[action.PotionSlot] == 0
                    || parent.Potions[action.PotionSlot] - 1 != plan.PayoffPotionId
                    || childFeatures.PotionStrategicCost != parentFacts.PotionStrategicCost)
                {
                    return count;
                }
                return checked(count + 1);
            case FlashPlanPayoffKind.RegisteredPowerBenefit:
                if (commitment <= 0 || childFeatures.Turn < plan.EarliestTurn)
                    return count;
                FlashPowerCommitmentState state = _commitments[commitment - 1];
                int bit = plan.PayoffCardBit;
                if (bit < 0 && _powerCommitments is { } catalog
                    && plan.PayoffCardId < catalog.Rules.Length)
                {
                    bit = catalog.Rules[plan.PayoffCardId].Index;
                }
                if (state.RealizedEvidence > 0 && CommitmentHasCard(state, bit))
                    return Math.Max(count, state.RealizedEvidence);
                return count;
            default:
                throw new InvalidOperationException("未知的计划收益证据类型。");
        }
    }

    private static bool CommitmentHasCard(in FlashPowerCommitmentState state, int bit)
    {
        if (bit is < 0 or >= 128)
            return false;
        return bit < 64
            ? (state.CardsLow & (1UL << bit)) != 0
            : (state.CardsHigh & (1UL << (bit - 64))) != 0;
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
    /// A route node's power-commitment metadata: the ported <c>PowerCommitment</c> without its string
    /// card list. Registered cards are indexed in the bridge catalog, so the played set is an exact
    /// 128-bit mask (87 registered powers today). This is route history and never enters
    /// <see cref="FlashBattle"/>, the fingerprint or the state key.
    /// </summary>
    /// <summary>
    /// <c>the existing search's AttachPowerCommitment</c> over a Flash route node. Returns the 1-based
    /// <see cref="_commitments"/> index, or 0 when the child carries no commitment. A child whose parent
    /// has no commitment and that does not play a registered power returns immediately, so the lane
    /// costs nothing on ordinary transitions.
    /// </summary>
    private int AttachPowerCommitment(int parentNode, in FlashBattle parent, in FlashBattle child,
        in FlashAction action, in RetentionFacts parentFacts, in FlashScoreFeatures childFeatures,
        int depth)
    {
        if (_powerCommitments is not { } catalog)
            return 0;
        int parentIndex = _nodes[parentNode].Commitment;
        FlashPowerCommitmentState parentState = parentIndex > 0
            ? _commitments[parentIndex - 1]
            : default;
        bool powerPlay = action.Kind == FlashActionKind.PlayCard
            && action.HandIndex < parent.HandCount;
        FlashPowerCommitmentRule rule = default;
        if (powerPlay)
            powerPlay = TryRegisteredPower(parent, parent.Cards[action.HandIndex], catalog, out rule);
        if (!powerPlay && parentIndex == 0)
            return 0;

        int progress = 0, realized = 0;
        RetentionFacts childFacts = default;
        FlashPowerEvidence.View childView = new(child, childFeatures, _rules, _scorer);
        ushort playedCardId = powerPlay && action.HandIndex < parent.HandCount
            ? parent.Cards[action.HandIndex].Id
            : ushort.MaxValue;
        if (parentIndex > 0)
        {
            (int reachableHandValue, int zeroCostPlayableCount, _) = _scorer.ReachableHandPotential(child);
            childFacts = RetentionFacts.From(childFeatures, depth,
                reachableHandValue, zeroCostPlayableCount);
            FlashPowerEvidence.View parentView = new(parent, default, _rules, _scorer);
            progress = FlashPowerEvidence.SilentProgress(parentState, parentView, childView);
            int specialized = FlashPowerEvidence.SilentRealized(parentState,
                parentFacts.PlayerBlock, parentFacts.ProjectedPlayerHp, parentFacts.ReachableHandValue,
                parentFacts.Turn, childFacts.PlayerBlock, childFacts.ProjectedPlayerHp,
                childFacts.ReachableHandValue, childFacts.Turn, childFacts.AliveEnemyCount,
                parent, child, playedCardId, action.Kind == FlashActionKind.PlayCard,
                childView, out bool handled);
            realized = handled
                ? specialized
                : GenericPowerCommitmentRealized(parentFacts, childFacts);
        }

        bool attached = false;
        FlashPowerCommitmentState commitment = parentState;
        if (powerPlay)
        {
            int spentEnergy = Math.Max(0, parentFacts.Energy - childFeatures.Energy);
            int turnOffset = Math.Max(0, childFeatures.Turn - _initialPlayerTurn);
            int investment = PowerActivationInvestmentPolicy.EnergyInvestment(
                spentEnergy, catalog.TotalFloor, turnOffset);
            investment = SaturatingCommitmentAdd(investment,
                Math.Max(0, childFeatures.CumulativePlayerHpLost - parentFacts.CumulativePlayerHpLost));
            int setupGain = PowerCommitmentSetupGain(parentFacts, childFeatures);
            int projectedPotential = FlashPowerEvidence.ProjectionPotential(playedCardId, childView, parent);
            int triggerFloor = FlashPowerEvidence.TriggerProjectionFloor(playedCardId, childView);
            PowerRouteAdmissionResult admission = PowerRouteAdmission.Evaluate(new(
                CardId: FlashRegistry.Card(playedCardId).NativeId,
                IsAutoPlay: false,
                SpentEnergy: spentEnergy,
                RemainingEnergy: childFeatures.Energy,
                HasTriggerEvidence: FlashPowerEvidence.HasTriggerEvidence(playedCardId, parent, childView),
                ImmediateDefenseGain: Math.Max(0,
                    childFeatures.ProjectedPlayerHp - parentFacts.ProjectedPlayerHp),
                SetupGain: setupGain,
                ProjectedPotential: projectedPotential,
                TriggerProjectionFloor: triggerFloor,
                Investment: investment), rule.Admission);
            if (admission.Admitted)
            {
                int cardBit = rule.Index;
                if (parentIndex == 0)
                {
                    commitment = new(rule.Family, rule.Priority,
                        cardBit < 64 ? 1UL << cardBit : 0UL,
                        cardBit >= 64 ? 1UL << (cardBit - 64) : 0UL,
                        0, childFeatures.Turn, investment, admission.Potential, 0, 0, depth);
                }
                else
                {
                    commitment = AddPowerCommitment(parentState, rule, cardBit,
                        investment, admission.Potential, progress, realized, childFeatures.Turn);
                }
                attached = true;
            }
        }

        if (!attached)
        {
            if (parentIndex == 0)
                return 0;
            FlashPowerCommitmentState? advanced = AdvancePowerCommitment(parentState,
                parent.PlayerTurn, child.PlayerTurn,
                _aggressivePowerCommitment ? 3 : 2, progress, realized);
            if (advanced is not { } next)
                return 0;
            commitment = next;
        }
        _commitments.Add(commitment);
        return _commitments.Count;
    }

    private bool TryRegisteredPower(in FlashBattle battle, in FlashCard card,
        FlashPowerCommitmentCatalog catalog, out FlashPowerCommitmentRule rule)
    {
        rule = default;
        return card.Id < catalog.Rules.Length
            && (rule = catalog.Rules[card.Id]).Family != PowerCommitmentFamily.None
            && FlashKernel.CardType(battle, _rules, card) == FlashCardType.Power;
    }

    private static FlashPowerCommitmentState AddPowerCommitment(
        in FlashPowerCommitmentState commitment,
        in FlashPowerCommitmentRule rule,
        int cardBit,
        int investment,
        int potential,
        int progressEvidence,
        int realizedEvidence,
        int turn)
    {
        int unrealizedPriorPotential = Math.Max(0,
            commitment.ProvisionalPotential - realizedEvidence);
        ulong cardsLow = commitment.CardsLow;
        ulong cardsHigh = commitment.CardsHigh;
        if (cardBit < 64)
            cardsLow |= 1UL << cardBit;
        else
            cardsHigh |= 1UL << (cardBit - 64);
        return commitment with
        {
            Family = commitment.Family | rule.Family,
            Priority = rule.Priority > commitment.Priority ? rule.Priority : commitment.Priority,
            CardsLow = cardsLow,
            CardsHigh = cardsHigh,
            Investment = SaturatingCommitmentAdd(commitment.Investment, investment),
            ProvisionalPotential = SaturatingCommitmentAdd(unrealizedPriorPotential, potential),
            ProgressEvidence = SaturatingCommitmentAdd(commitment.ProgressEvidence, progressEvidence),
            RealizedEvidence = SaturatingCommitmentAdd(commitment.RealizedEvidence, realizedEvidence),
            LastEvidenceTurn = progressEvidence > 0 || realizedEvidence > 0
                ? turn
                : commitment.LastEvidenceTurn,
        };
    }

    /// <summary><c>PowerCommitmentLifecycle.Advance</c>; null means expired or realized.</summary>
    private static FlashPowerCommitmentState? AdvancePowerCommitment(
        in FlashPowerCommitmentState commitment,
        int parentTurn,
        int childTurn,
        int maximumTransitions,
        int progressEvidence,
        int realizedEvidence)
    {
        int roundTransitions = commitment.RoundTransitions
            + Math.Max(0, childTurn - parentTurn);
        bool hasEvidence = progressEvidence > 0 || realizedEvidence > 0;
        int lastEvidenceTurn = hasEvidence ? childTurn : commitment.LastEvidenceTurn;
        if (roundTransitions > maximumTransitions
            || roundTransitions > 0 && childTurn - lastEvidenceTurn > 1)
        {
            return null;
        }
        int remainingPotential = Math.Max(0,
            commitment.ProvisionalPotential - realizedEvidence);
        if (remainingPotential == 0 && realizedEvidence > 0)
            return null;
        return commitment with
        {
            RoundTransitions = roundTransitions,
            LastEvidenceTurn = lastEvidenceTurn,
            ProgressEvidence = SaturatingCommitmentAdd(commitment.ProgressEvidence, progressEvidence),
            RealizedEvidence = SaturatingCommitmentAdd(commitment.RealizedEvidence, realizedEvidence),
            ProvisionalPotential = remainingPotential,
        };
    }

    /// <summary><c>PowerCommitmentSetupGain</c> over the K4-checked feature deltas.</summary>
    private static int PowerCommitmentSetupGain(in RetentionFacts before, in FlashScoreFeatures after)
    {
        long gain = Math.Max(0, after.PersistentBuffValue - before.PersistentBuffValue);
        gain += Math.Max(0, after.StrategicRetentionValue - before.StrategicRetentionValue);
        gain += Math.Max(0, after.FutureResourceValue - before.FutureResourceValue);
        gain += Math.Max(0, after.LatentSetupValue - before.LatentSetupValue);
        return (int)Math.Min(int.MaxValue, gain);
    }

    /// <summary><c>PowerCommitmentProgressEvidence</c> generic fallback (no specialized card table).</summary>
    private static int GenericPowerCommitmentProgress(in RetentionFacts before, in RetentionFacts after)
        => (int)Math.Min(int.MaxValue,
            (long)Math.Max(0, after.OffensiveProgressValue - before.OffensiveProgressValue)
            + Math.Max(0, after.ReachableHandValue - before.ReachableHandValue)
            + Math.Max(0, after.ZeroCostPlayableCount - before.ZeroCostPlayableCount) * 4L
            + Math.Max(0, after.ProjectedPlayerHp - before.ProjectedPlayerHp));

    /// <summary><c>GenericPowerCommitmentEvidence</c>, also used as the realized signal.</summary>
    private static int GenericPowerCommitmentRealized(in RetentionFacts before, in RetentionFacts after)
        => GenericPowerCommitmentProgress(before, after);

    private static int SaturatingCommitmentAdd(int left, int right)
        => (int)Math.Clamp((long)left + right, 0L, int.MaxValue);

    /// <summary>
    /// The evaluation values the ported retention comparators read. Every member is already frozen by
    /// <see cref="FlashScorer.Features"/> and checked field by field against the existing snapshot by K4,
    /// so retention does not re-evaluate a state or introduce a second scoring path.
    /// </summary>
    private readonly record struct RetentionFacts(
        int Depth,
        int PotionCount,
        int ProjectedPlayerHp,
        int PlayerBlock,
        int OstyHp,
        int OstyMaxHp,
        int DefensiveBlockValue,
        int AliveEnemyCount,
        int RawEnemyHp,
        int EnemyBlock,
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
        PersistentSetupTraits StrategySetupTraits,
        int PersistentBuffValue,
        int OffensiveProgressValue,
        int OutstandingStolenResource,
        int Turn,
        int CumulativePlayerHpLost,
        int PotionStrategicCost,
        double Score,
        int PlayerHp,
        int PlayerMaxHp,
        ulong AliveEnemyMask,
        bool AllEnemiesDead,
        bool PlayerDead,
        int PocketwatchCardThreshold,
        int PocketwatchCardsPlayedThisTurn,
        int PocketwatchCardsPlayedLastTurn,
        SearchRouteTraits Traits,
        int StrategicHpCredit,
        ulong RelicSatisfiedMask,
        int StrategyGoalCount,
        int AngerCopiesGenerated,
        int RetainedAttackGrowth,
        int FutureSoldHp,
        int PlayableAttackCount,
        int StrategicResourcePotential,
        int ParentEnemyBlock,
        int ParentEnemyHp,
        int ParentFutureResourceValue,
        int ParentStrategicResourcePotential,
        int ProjectedShuffleOrderValue,
        ulong ProjectedShuffleOrderKeyFirst,
        ulong ProjectedShuffleOrderKeySecond,
        bool CanTriggerArtOfWarNextTurn,
        ulong EnemyCombatDistributionFirst,
        ulong EnemyCombatDistributionSecond,
        ulong EnemyControlDistributionFirst,
        ulong EnemyControlDistributionSecond,
        ulong UnorderedPileKeyFirst,
        ulong UnorderedPileKeySecond)
    {
        /// <summary>Re-projects the retention-only values of an already stored byte-identical state.</summary>
        public FlashScorer.FlashRetentionValues RetentionProjection()
            => new(ProjectedShuffleOrderValue, ProjectedShuffleOrderKeyFirst, ProjectedShuffleOrderKeySecond,
                CanTriggerArtOfWarNextTurn, EnemyCombatDistributionFirst, EnemyCombatDistributionSecond,
                EnemyControlDistributionFirst, EnemyControlDistributionSecond, UnorderedPileKeyFirst,
                UnorderedPileKeySecond);

        public static RetentionFacts From(in FlashScoreFeatures features, int depth,
            int reachableHandValue, int zeroCostPlayableCount, SearchRouteTraits traits = SearchRouteTraits.None,
            int pocketwatchCardThreshold = -1, int pocketwatchCardsPlayedThisTurn = 0,
            int pocketwatchCardsPlayedLastTurn = 0,
            int initialRetainedAttackValue = 0, int parentEnemyBlock = 0, int parentEnemyHp = 0,
            int parentFutureResourceValue = 0, int parentStrategicResourcePotential = 0,
            in FlashScorer.FlashRetentionValues retention = default) => new(
            depth,
            features.PotionUseCount,
            features.ProjectedPlayerHp,
            features.PlayerBlock,
            features.OstyHp,
            features.OstyMaxHp,
            features.DefensiveBlockValue,
            features.AliveEnemyCount,
            features.RawEnemyHp,
            features.EnemyBlock,
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
            (PersistentSetupTraits)features.StrategicSetupTraits,
            features.PersistentBuffValue,
            features.OffensiveProgressValue,
            features.OutstandingStolenResource,
            features.Turn,
            features.CumulativePlayerHpLost,
            features.PotionStrategicCost,
            features.Score,
            features.PlayerHp,
            features.PlayerMaxHp,
            features.AliveEnemyMask,
            features.AllEnemiesDead,
            features.PlayerDead,
            pocketwatchCardThreshold,
            pocketwatchCardsPlayedThisTurn,
            pocketwatchCardsPlayedLastTurn,
            traits,
            features.StrategicHpCredit,
            features.RelicSatisfiedMask,
            features.StrategyGoalCount,
            features.AngerCopiesGenerated,
            Math.Min(SolverWeights.RetainedAttackGrowthBeamCap,
                Math.Max(0, features.RetainedAttackValue - initialRetainedAttackValue)),
            0,
            features.PlayableAttackCount,
            features.StrategicResourcePotential,
            parentEnemyBlock,
            parentEnemyHp,
            parentFutureResourceValue,
            parentStrategicResourcePotential,
            retention.ProjectedShuffleOrderValue,
            retention.ProjectedShuffleOrderKeyFirst,
            retention.ProjectedShuffleOrderKeySecond,
            retention.CanTriggerArtOfWarNextTurn,
            retention.EnemyCombatDistributionFirst,
            retention.EnemyCombatDistributionSecond,
            retention.EnemyControlDistributionFirst,
            retention.EnemyControlDistributionSecond,
            retention.UnorderedPileKeyFirst,
            retention.UnorderedPileKeySecond);

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

    /// <summary>
    /// K4 entry: the Flash route-trait delta for one transition, computed from the same feature
    /// projection the probe compares field by field. The reference side calls the existing
    /// <c>Classify*Traits</c>; <c>pure</c> and the PaelsEye declined flag are inputs the probe derives
    /// from the simulator history/relics for both sides.
    /// </summary>
    internal static SearchRouteTraits ClassifyRouteTraitsForTesting(
        in FlashScoreFeatures before, in FlashScoreFeatures after, FlashAction action,
        bool declinedExtraTurn, bool nonPure)
    {
        RetentionFacts facts = RetentionFacts.From(before, 0,
            before.ReachableHandValue, before.ZeroCostPlayableCount);
        return ClassifyRouteTraits(facts, after, action, declinedExtraTurn, nonPure);
    }

    /// <summary>
    /// K4 entry for the production <c>pure</c> input: the history entries of one play are pure exactly
    /// when the play only started/finished cards and dealt/received damage (the existing
    /// <c>SearchProbeHelpers.IsPureHistoryEntry</c> set). Every other history entry is produced by a
    /// draw, generation, selection, channel, affliction, cost randomisation, draw-pile auto-play or
    /// potion generation, so those transitions are detected from the branch state the Flash kernel
    /// changed. The probe checks this input against the history-derived reference on every transition.
    /// </summary>
    internal static bool IsPureTransitionForTesting(in FlashBattle before, in FlashBattle after,
        in FlashAction action, FlashRules rules, bool historyRisk)
        => IsPureTransition(before, after, action, rules, historyRisk);

    private static bool IsPureTransition(in FlashBattle before, in FlashBattle after, in FlashAction action,
        FlashRules rules, bool historyRisk)
    {
        if (action.Kind != FlashActionKind.PlayCard)
            return false;
        if (historyRisk)
            return false;
        if (action.ChoiceCount > 0)
            return false;
        // A card whose OnPlay mirror dispatch is inferred/unsupported records a prediction risk entry
        // on every play; the existing history-derived pure input is false for it.
        if (action.HandIndex >= before.HandCount)
            return false;
        FlashCard played = before.Cards[action.HandIndex];
        if (rules.Card(played.Id, played.Upgrade).PlaysRecordRisk)
            return false;
        if (after.Counter(FlashCounter.CardsDrawn) != before.Counter(FlashCounter.CardsDrawn))
            return false;
        // Any CardGenerated/CardGenerationResolved entry is non-pure, including a card generated by
        // an enemy (the combat counter only counts the player's). A completed play keeps the total
        // card count unchanged (it moves hand -> play -> discard/exhaust), so a larger total is a
        // generated card.
        if (after.CardCount > before.CardCount)
            return false;
        if (after.Counter(FlashCounter.CardsGeneratedThisCombat)
            != before.Counter(FlashCounter.CardsGeneratedThisCombat))
        {
            return false;
        }
        if (after.Counter(FlashCounter.OrbChannels) != before.Counter(FlashCounter.OrbChannels))
            return false;
        if (after.DrawCount < before.DrawCount)
            return false;

        for (int index = 0; index < after.PotionSlotCount; index++)
        {
            ushort potion = after.Potions[index];
            if (potion != 0 && potion != before.Potions[index])
                return false;
        }

        // Cards move between piles within one play (hand -> play -> discard), so afflictions are
        // compared as a multiset of (id, amount) over the whole card image, not by position.
        (ulong beforeFirst, ulong beforeSecond) = AfflictionMultiset(before);
        (ulong afterFirst, ulong afterSecond) = AfflictionMultiset(after);
        return beforeFirst == afterFirst && beforeSecond == afterSecond;
    }

    private static (ulong First, ulong Second) AfflictionMultiset(in FlashBattle battle)
    {
        ulong first = 0, second = 0;
        for (int index = 0; index < battle.CardCount; index++)
        {
            ref readonly FlashCard card = ref battle.Cards[index];
            ulong value = ((ulong)(ushort)card.AfflictionId << 32)
                | (ushort)card.AfflictionAmount;
            first += StateFingerprintBuilder.MixFirst(value);
            second += StateFingerprintBuilder.MixSecond(value);
        }
        return (first, second);
    }

    /// <summary>
    /// <c>SearchProbeHelpers.ClassifyCardTraits</c>/<c>ClassifyPotionTraits</c>/<c>ClassifyRoundTransitionTraits</c>
    /// over the parent/child feature values. The cumulative route traits are node metadata: they never enter
    /// <see cref="FlashBattle"/>, the fingerprint or the state key.
    /// <para>
    /// The existing <c>pure</c> input is derived from the simulator history entry types
    /// (<c>CardPlayStarted</c>/<c>CardPlayFinished</c>/<c>CreatureAttacked</c>/<c>DamageReceived</c>).
    /// Flash route metadata has no history entry stream, so a card play is classified as not pure. That
    /// only widens the <see cref="SearchRouteTraits.Control"/> candidate set; the other terms of the same
    /// classification are exact feature comparisons.
    /// </para>
    /// </summary>
    private static SearchRouteTraits ClassifyRouteTraits(in RetentionFacts before,
        in FlashScoreFeatures after, in FlashAction action, bool declinedExtraTurn, bool nonPure)
    {
        SearchRouteTraits traits = before.Traits;
        if (action.Kind == FlashActionKind.PlayCard)
        {
            if (declinedExtraTurn)
                traits |= SearchRouteTraits.DeclinedExtraTurn;
            if (after.PersistentBuffValue > before.PersistentBuffValue
                || after.DelayedDamageValue > before.DelayedDamageValue
                || after.ReplayPotentialValue > before.ReplayPotentialValue
                || after.LongTermResourceValue > before.LongTermResourceValue)
            {
                traits |= SearchRouteTraits.Scaling;
            }
            if (after.LongTermResourceValue > before.LongTermResourceValue)
                traits |= SearchRouteTraits.LongTermResource;
            if (after.PlayerHp < before.PlayerHp
                || after.PlayerMaxHp < before.PlayerMaxHp
                || after.CumulativePlayerHpLost > before.CumulativePlayerHpLost)
            {
                traits |= SearchRouteTraits.HpInvestment;
            }
            if (after.ReactiveDamageValue > before.ReactiveDamageValue)
                traits |= SearchRouteTraits.ReactiveDamage;
            if (after.Energy > before.Energy
                || after.Stars > before.Stars
                || after.HandCount >= before.HandCount
                || after.ReachableHandValue > before.ReachableHandValue
                || after.ZeroCostPlayableCount > before.ZeroCostPlayableCount
                || after.FutureResourceValue > before.FutureResourceValue)
            {
                traits |= SearchRouteTraits.Resource;
            }
            if (nonPure
                || after.SandpitRemaining > before.SandpitRemaining
                || after.EnemyStrengthSuppression > before.EnemyStrengthSuppression
                || after.EnemyWeakTurns > before.EnemyWeakTurns
                || after.EnemyVulnerableTurns > before.EnemyVulnerableTurns
                || after.LiveDeckClutter < before.LiveDeckClutter
                || after.DelayedDamageValue > before.DelayedDamageValue)
            {
                traits |= SearchRouteTraits.Control;
            }
            if (OpensRevivalWindow(before, after))
                traits |= SearchRouteTraits.RevivalWindow;
        }
        else if (action.Kind == FlashActionKind.UsePotion)
        {
            if (after.PersistentBuffValue > before.PersistentBuffValue
                || after.DelayedDamageValue > before.DelayedDamageValue)
            {
                traits |= SearchRouteTraits.Scaling;
            }
            if (after.Energy > before.Energy
                || after.Stars > before.Stars
                || after.HandCount > before.HandCount
                || after.FutureResourceValue > before.FutureResourceValue)
            {
                traits |= SearchRouteTraits.Resource;
            }
            if (after.SandpitRemaining > before.SandpitRemaining
                || after.EnemyStrengthSuppression > before.EnemyStrengthSuppression
                || after.EnemyWeakTurns > before.EnemyWeakTurns
                || after.LiveDeckClutter < before.LiveDeckClutter
                || after.DelayedDamageValue > before.DelayedDamageValue
                || after.EnemyHp == before.EnemyHp && after.PlayerBlock == before.PlayerBlock)
            {
                traits |= SearchRouteTraits.Control;
            }
            if (before.Energy == 0
                && after.HandCount == 0
                && before.LiveDeckSize - after.LiveDeckSize >= 6
                && before.PocketwatchCardThreshold >= 0
                && before.PocketwatchCardsPlayedThisTurn == before.PocketwatchCardThreshold)
            {
                traits |= SearchRouteTraits.EndTurnDeckCompression;
            }
            if (OpensRevivalWindow(before, after))
                traits |= SearchRouteTraits.RevivalWindow;
        }
        else if (OpensRevivalWindow(before, after))
        {
            traits |= SearchRouteTraits.RevivalWindow;
        }
        return traits;
    }

    private static bool OpensRevivalWindow(in RetentionFacts before, in FlashScoreFeatures after)
        => after.RevivingEnemyCount > before.RevivingEnemyCount
            || after.RawEnemyHp < before.RawEnemyHp && after.EnemyHp >= before.EnemyHp
            || (before.AliveEnemyMask & ~after.AliveEnemyMask) != 0
                && (after.AliveEnemyMask & ~before.AliveEnemyMask) != 0;

    /// <summary><c>SimulatedCombatState.TryGetPocketwatchState</c>: the first non-melted relic's threshold
    /// and cards played this turn. A root without the relic reports -1/0, exactly like the snapshot.</summary>
    private void PocketwatchState(in FlashBattle battle, out int threshold,
        out int cardsPlayedThisTurn, out int cardsPlayedLastTurn)
        => PocketwatchState(battle, _rules, out threshold, out cardsPlayedThisTurn, out cardsPlayedLastTurn);

    internal static void PocketwatchState(in FlashBattle battle, FlashRules rules,
        out int threshold, out int cardsPlayedThisTurn)
        => PocketwatchState(battle, rules, out threshold, out cardsPlayedThisTurn, out _);

    /// <summary>
    /// <c>SimulatedCombatState.TryGetPocketwatchState</c>: the first non-melted relic's threshold,
    /// this turn's plays (Counter) and last turn's plays (Aux; the bridge projects the engine's
    /// StatefulRelicState Previous into Aux).
    /// </summary>
    internal static void PocketwatchState(in FlashBattle battle, FlashRules rules,
        out int threshold, out int cardsPlayedThisTurn, out int cardsPlayedLastTurn)
    {
        threshold = -1;
        cardsPlayedThisTurn = 0;
        cardsPlayedLastTurn = 0;
        ushort id = FlashRegistry.PocketwatchRelicId;
        if (id == 0)
            return;
        for (int index = 0; index < battle.RelicCount; index++)
        {
            ref readonly FlashRelic relic = ref battle.Relics[index];
            if (relic.Id != id || (relic.Flags & FlashRelicFlags.Melted) != 0)
                continue;
            threshold = (int)rules.RelicVar(id, 0);
            cardsPlayedThisTurn = relic.Counter;
            cardsPlayedLastTurn = relic.Aux;
            return;
        }
    }

    /// <summary><c>SimulatedCombatState.IsPaelsEyeUnused</c>: the relic exists, is not melted and its
    /// stateful counter is still zero.</summary>
    internal static bool DeclinedExtraTurn(in FlashBattle battle)
    {
        ushort id = FlashRegistry.PaelsEyeRelicId;
        if (id == 0)
            return false;
        for (int index = 0; index < battle.RelicCount; index++)
        {
            ref readonly FlashRelic relic = ref battle.Relics[index];
            if (relic.Id == id && (relic.Flags & FlashRelicFlags.Melted) == 0 && relic.Counter == 0)
                return true;
        }
        return false;
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

    /// <summary>the existing search's BeamRetentionPolicy.IsBetterSearchNode: score, then fewer actions.</summary>
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

    /// <summary>
    /// FindBestCuratedTurnBoundaryHand (BeamRetentionPolicy.Ranking.cs): projected HP, Osty HP, the
    /// projected shuffle order value, reachable hand value, fewer hand cards, enemy HP, then score.
    /// </summary>
    private static bool IsBetterCuratedTurnBoundaryHand(in RetentionFacts candidate,
        in RetentionFacts current)
    {
        if (candidate.ProjectedPlayerHp != current.ProjectedPlayerHp)
            return candidate.ProjectedPlayerHp > current.ProjectedPlayerHp;
        if (candidate.OstyHp != current.OstyHp)
            return candidate.OstyHp > current.OstyHp;
        if (candidate.ProjectedShuffleOrderValue != current.ProjectedShuffleOrderValue)
            return candidate.ProjectedShuffleOrderValue > current.ProjectedShuffleOrderValue;
        if (candidate.ReachableHandValue != current.ReachableHandValue)
            return candidate.ReachableHandValue > current.ReachableHandValue;
        if (candidate.HandCount != current.HandCount)
            return candidate.HandCount < current.HandCount;
        if (candidate.EnemyHp != current.EnemyHp)
            return candidate.EnemyHp < current.EnemyHp;
        return candidate.Score > current.Score;
    }

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
        public double MaxRank = double.NegativeInfinity;
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
        private readonly ChunkedArray<ulong> _lineages;
        private readonly ChunkedArray<double> _scores;
        private readonly ChunkedArray<double> _ranks;
        private readonly ChunkedArray<ulong> _hashes;
        private readonly ChunkedArray<RetentionFacts> _facts;
        private readonly ChunkedArray<int> _potionCosts;
        private readonly ChunkedArray<double> _pureRanks;
        private readonly ChunkedArray<int> _payoffs;
        private readonly ChunkedArray<int> _commitment;
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
        private readonly Dictionary<ulong, LineageLeaders> _lineageLeaders = new();
        private readonly List<ulong> _lineageOrder = new();
        private readonly Dictionary<int, PotionLeaders> _potionLeaders = new();
        private readonly List<int> _potionCountOrder = new();
        private readonly Dictionary<(int Potion, PersistentSetupTraits Traits), TraitLeaders> _traitLeaders = new();
        private readonly List<(int Potion, PersistentSetupTraits Traits)> _traitOrder = new();
        private readonly Dictionary<(int Potion, uint? Target), FocusLeaders> _focusLeaders = new();
        private readonly List<(int Potion, uint? Target)> _focusOrder = new();
        private readonly FlashScorer _scorer;
        private readonly bool _preserveTheft;
        private readonly FlashPlanCommitment? _planCommitment;
        private readonly bool _trackPlanSeats;
        private readonly bool _secondRankBand;
        private readonly int _secondRankBandWidth;
        private readonly bool _aggressivePowerCommitment;
        private readonly bool _trackPowerCommitments;
        private readonly List<FlashPowerCommitmentState> _commitmentStates;
        private readonly List<RouteNode> _routeNodes;
        private readonly List<RoutingSignature> _routingSignatures;
        private readonly bool _checkPotionQuota;
        private readonly int _initialActionCount;
        private readonly bool _preserveReplayAllocatorOpening;
        private readonly int _policyWidth;
        private readonly HashSet<int> _potionReservations = new();
        private readonly List<ushort> _potionLineageScratch = new();
        private readonly Dictionary<string, PotionLineageLeaders> _potionLineageGroups =
            new(StringComparer.Ordinal);
        private readonly List<string> _potionLineageOrder = new();
        private const int RoutingChoiceLimit = 96;
        private readonly record struct RoutingFamilyKey(int Turn, int SourceId, int Effect, int Pile);
        private readonly record struct RoutingOptionKey(int CardId, int Upgrade, int StateKey, int CardCount);
        private sealed class RoutingGroup
        {
            public readonly List<int> Nodes = new();
            public int BestScore = -1;
            public int BestOffense = -1;
            public int BestDefense = -1;
            public int BestSetup = -1;
            public int BestPileOrder = -1;
            public int MinParentRank = int.MaxValue;
            public double MaxParentScore = double.NegativeInfinity;
            public double MaxBeamRank = double.NegativeInfinity;
        }
        private sealed class RoutingBucket
        {
            public readonly List<int> Groups = new();
            public int MinParentRank = int.MaxValue;
            public double MaxParentScore = double.NegativeInfinity;
            public double MaxBeamRank = double.NegativeInfinity;
        }
        private readonly Dictionary<int, RoutingGroup> _routingGroups = new();
        private readonly List<int> _routingGroupOrder = new();
        private readonly Dictionary<RoutingFamilyKey, RoutingBucket> _routingFamilies = new();
        private readonly List<RoutingFamilyKey> _routingFamilyOrder = new();
        private readonly Dictionary<RoutingOptionKey, RoutingBucket> _routingOptions = new();
        private readonly List<RoutingOptionKey> _routingOptionOrder = new();
        private readonly List<int> _routingChoices = new();
        private readonly record struct ParetoCohortKey(
            ulong EnemyCombatFirst, ulong EnemyCombatSecond,
            ulong EnemyControlFirst, ulong EnemyControlSecond,
            ulong UnorderedPileFirst, ulong UnorderedPileSecond);
        private readonly Dictionary<ParetoCohortKey, List<int>> _paretoCohorts = new();
        private readonly List<int> _pareto = new(3);
        private readonly List<int> _standPatGroup = new();
        private readonly record struct StandPatEvaluation(
            bool AllEnemiesDead, int DelayedDamage, int ProjectedPlayerHp, int ResourceValue,
            bool Available);
        private readonly Dictionary<CommitmentGroupKey, CommitmentGroupLeader> _commitmentGroups = new();
        private readonly List<CommitmentGroupKey> _commitmentGroupOrder = new();
        private readonly List<int> _powerRepresentatives = new();
        private readonly bool[] _representative;
        private bool _hasCommitments;

        public Pool(int initialCapacity, int maxCapacity, bool preserveTheft, FlashScorer scorer,
            FlashPlanCommitment? planCommitment, bool secondRankBand, int secondRankBandWidth,
            bool aggressivePowerCommitment, FlashPowerCommitmentCatalog? powerCommitments,
            List<FlashPowerCommitmentState> commitmentStates, List<RouteNode> routeNodes,
            List<RoutingSignature> routingSignatures, bool checkPotionQuota, int initialActionCount,
            bool preserveReplayAllocatorOpening)
        {
            _maxCapacity = maxCapacity;
            _preserveTheft = preserveTheft;
            _scorer = scorer;
            _planCommitment = planCommitment;
            _trackPlanSeats = planCommitment is { CopyPowerSeats: true };
            _secondRankBand = secondRankBand;
            _secondRankBandWidth = secondRankBandWidth;
            _aggressivePowerCommitment = aggressivePowerCommitment;
            _trackPowerCommitments = powerCommitments is { Rules.Length: > 0 };
            _commitmentStates = commitmentStates;
            _routeNodes = routeNodes;
            _routingSignatures = routingSignatures;
            _checkPotionQuota = checkPotionQuota;
            _initialActionCount = initialActionCount;
            _preserveReplayAllocatorOpening = preserveReplayAllocatorOpening;
            _policyWidth = secondRankBandWidth > 0 ? secondRankBandWidth : maxCapacity;
            _states = new ChunkedArray<FlashBattle>(maxCapacity);
            _nodes = new ChunkedArray<int>(maxCapacity);
            _lineages = new ChunkedArray<ulong>(maxCapacity);
            _scores = new ChunkedArray<double>(maxCapacity);
            _ranks = new ChunkedArray<double>(maxCapacity);
            _hashes = new ChunkedArray<ulong>(maxCapacity);
            _facts = new ChunkedArray<RetentionFacts>(maxCapacity);
            _potionCosts = new ChunkedArray<int>(maxCapacity);
            _pureRanks = new ChunkedArray<double>(maxCapacity);
            _payoffs = new ChunkedArray<int>(maxCapacity);
            _commitment = new ChunkedArray<int>(maxCapacity);
            // The rank/selection scratch stays contiguous because Span.Sort needs a single span. It is
            // sized to the capacity bound once (three int arrays plus two flags, ~1 MB at width 512)
            // instead of being reallocated while the beam grows.
            _order = new int[maxCapacity];
            _rankOrder = new int[maxCapacity];
            _rankPosition = new int[maxCapacity];
            _taken = new bool[maxCapacity];
            _required = new bool[maxCapacity];
            _representative = new bool[maxCapacity];
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

        public int RankPosition(int index) => _rankPosition[index];

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
            _commitment[to] = _commitment[from];
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
            _hasCommitments = false;
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
            in FlashScoreFeatures features, int depth, ulong lineage, int payoffCount, int commitment,
            SearchRouteTraits traits, int pocketwatchThreshold, int pocketwatchPlayed,
            int pocketwatchLastTurn, int parentEnemyBlock, int parentEnemyHp, int parentFutureResourceValue,
            int parentStrategicResourcePotential)
        {
            if (_scratchReserved)
            {
                return Insert(ref _scratch, inPlace: false, node, score, rank, pureRank, potionCost,
                    features, default, factsReady: false, depth, lineage, payoffCount, commitment, traits,
                    pocketwatchThreshold, pocketwatchPlayed, pocketwatchLastTurn, parentEnemyBlock,
                    parentEnemyHp, parentFutureResourceValue, parentStrategicResourcePotential);
            }
            return Insert(ref _states[Count], inPlace: true, node, score, rank, pureRank, potionCost,
                features, default, factsReady: false, depth, lineage, payoffCount, commitment, traits,
                pocketwatchThreshold, pocketwatchPlayed, pocketwatchLastTurn, parentEnemyBlock,
                parentEnemyHp, parentFutureResourceValue, parentStrategicResourcePotential);
        }

        /// <summary>Adds a scored state and computes its retention-only values when it is stored.</summary>
        public bool AddScored(in FlashBattle state, int node, double score, double rank, double pureRank,
            int potionCost, in FlashScoreFeatures features, int depth, ulong lineage, int payoffCount,
            int commitment, SearchRouteTraits traits, int pocketwatchThreshold, int pocketwatchPlayed,
            int pocketwatchLastTurn, int parentEnemyBlock, int parentEnemyHp, int parentFutureResourceValue,
            int parentStrategicResourcePotential)
            => Insert(ref Unsafe.AsRef(in state), inPlace: false,
                node, score, rank, pureRank, potionCost, features, default, factsReady: false, depth,
                lineage, payoffCount, commitment, traits, pocketwatchThreshold, pocketwatchPlayed,
                pocketwatchLastTurn, parentEnemyBlock, parentEnemyHp, parentFutureResourceValue,
                parentStrategicResourcePotential);

        /// <summary>Adds a state whose retention facts are already computed (pool-to-pool copies).</summary>
        public bool Add(in FlashBattle state, int node, double score, double rank, double pureRank,
            int potionCost, in RetentionFacts facts, ulong lineage, int payoffCount, int commitment)
            => Insert(ref Unsafe.AsRef(in state), inPlace: false,
                node, score, rank, pureRank, potionCost, default, facts, factsReady: true, facts.Depth,
                lineage, payoffCount, commitment, facts.Traits, facts.PocketwatchCardThreshold,
                facts.PocketwatchCardsPlayedThisTurn, facts.PocketwatchCardsPlayedLastTurn, 0, 0, 0, 0);

        private bool Insert(ref FlashBattle state, bool inPlace, int node, double score, double rank,
            double pureRank, int potionCost, in FlashScoreFeatures features, in RetentionFacts knownFacts,
            bool factsReady, int depth, ulong lineage, int payoffCount, int commitment,
            SearchRouteTraits traits, int pocketwatchThreshold, int pocketwatchPlayed,
            int pocketwatchLastTurn, int parentEnemyBlock, int parentEnemyHp, int parentFutureResourceValue,
            int parentStrategicResourcePotential)
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
                                _facts[existing].ReachableHandValue, _facts[existing].ZeroCostPlayableCount,
                                traits, pocketwatchThreshold, pocketwatchPlayed, pocketwatchLastTurn,
                                _scorer.InitialRetainedAttackValue, parentEnemyBlock, parentEnemyHp,
                                parentFutureResourceValue, parentStrategicResourcePotential,
                                _facts[existing].RetentionProjection());
                        _lineages[existing] = lineage;
                        _commitment[existing] = commitment;
                        _hasCommitments |= commitment > 0;
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
                (int reachableHandValue, int zeroCostPlayableCount, _) =
                    _scorer.ReachableHandPotential(state);
                facts = RetentionFacts.From(
                    features, depth, reachableHandValue, zeroCostPlayableCount,
                    traits, pocketwatchThreshold, pocketwatchPlayed, pocketwatchLastTurn,
                    _scorer.InitialRetainedAttackValue, parentEnemyBlock, parentEnemyHp,
                    parentFutureResourceValue, parentStrategicResourcePotential,
                    _scorer.RetentionValues(state));
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
            _commitment[Count] = commitment;
            _hasCommitments |= commitment > 0;
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
                    _trackPlanSeats ? _payoffs[source] : 0, _commitment[source]);
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
            // BeamRetentionPolicy.RankBest: a band member rotates the score-leading width to the tail
            // before the ordinary seats are taken, so ranks W+1..2W win (with the original order as
            // backfill when fewer than 2W candidates exist). Required representatives, the power
            // commitment seats and the plan seats are collected/added below and are unaffected.
            if (_secondRankBand && count > seats)
                RotateLeadingBandToTail(count, Math.Clamp(_secondRankBandWidth, 1, seats));
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
            if (_trackPowerCommitments && _hasCommitments)
                AdmitPowerCommitmentSeats(selected, required, width);
            if (_planCommitment is { CopyPowerSeats: true })
                AdmitPlanCommitmentSeats(selected, required, width);
            if (_checkPotionQuota)
                ApplyPotionUseQuota(selected, width);
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

        /// <summary>
        /// <c>BeamRetentionPolicy.FeasiblePotionUseQuotas</c> and the two quota passes after the
        /// ordinary/required seats are chosen. The quota only swaps seats; it never widens the beam.
        /// </summary>
        private void ApplyPotionUseQuota(List<int> selected, int limit)
        {
            bool anyUsed = false, anyUnused = false;
            for (int position = 0; position < Count; position++)
            {
                if (UsesPotion(_rankOrder[position]))
                    anyUsed = true;
                else
                    anyUnused = true;
                if (anyUsed && anyUnused)
                    break;
            }
            if (!anyUsed || !anyUnused)
                return;
            (int usedQuota, int unusedQuota) = FeasiblePotionUseQuotas(_policyWidth);
            _potionReservations.Clear();
            for (int index = 0; index < _requiredScratch.Count; index++)
                _potionReservations.Add(_requiredScratch[index]);
            ReservePotionQuotaLeaders(usesPotion: true, usedQuota);
            ReservePotionQuotaLeaders(usesPotion: false, unusedQuota);
            EnforcePotionUseQuota(selected, usesPotion: true, usedQuota);
            EnforcePotionUseQuota(selected, usesPotion: false, unusedQuota);
        }

        private static (int Used, int Unused) FeasiblePotionUseQuotas(int limit)
        {
            if (limit < 1)
                throw new ArgumentOutOfRangeException(nameof(limit));
            int used = limit < 4 ? 1 : Math.Max(2, limit / 3);
            return (used, limit - used);
        }

        private bool UsesPotion(int index) => _facts[index].PotionCount > 0;

        private void ReservePotionQuotaLeaders(bool usesPotion, int quota)
        {
            if (quota <= 0)
                return;
            int reserved = 0;
            for (int position = 0; position < Count; position++)
            {
                int candidate = _rankOrder[position];
                if (UsesPotion(candidate) != usesPotion)
                    continue;
                _potionReservations.Add(candidate);
                reserved++;
                if (reserved >= quota)
                    return;
            }
        }

        private void EnforcePotionUseQuota(List<int> selected, bool usesPotion, int quota)
        {
            int retained = 0;
            for (int index = 0; index < selected.Count; index++)
            {
                if (UsesPotion(selected[index]) == usesPotion)
                    retained++;
            }
            if (retained >= quota)
                return;
            for (int position = 0; position < Count; position++)
            {
                if (retained >= quota)
                    return;
                int candidate = _rankOrder[position];
                if (UsesPotion(candidate) != usesPotion || selected.Contains(candidate))
                    continue;
                int replaceIndex = -1;
                for (int index = selected.Count - 1; index >= 0; index--)
                {
                    int node = selected[index];
                    if (UsesPotion(node) != usesPotion && !_potionReservations.Contains(node))
                    {
                        replaceIndex = index;
                        break;
                    }
                }
                if (replaceIndex < 0)
                    return;
                selected[replaceIndex] = candidate;
                retained++;
            }
        }

        /// <summary>Left-rotate <c>_order[0..count)</c> by <paramref name="bandWidth"/> in place.</summary>
        private void RotateLeadingBandToTail(int count, int bandWidth)
        {
            Span<int> order = _order.AsSpan(0, count);
            order.Reverse();
            order[..(count - bandWidth)].Reverse();
            order[(count - bandWidth)..].Reverse();
        }

        /// <summary>
        /// <c>BeamRetentionPolicy.AdmitPowerCommitmentRepresentatives</c>. The pool is the existing
        /// <c>quotaPool</c> (score order); the quota is half the beam for aggressive members and
        /// <c>clamp(W/12, 2, 12)</c> otherwise. Representatives replace the last non-required,
        /// non-representative seat and are marked required. The beam width is unchanged.
        /// </summary>
        private void AdmitPowerCommitmentSeats(List<int> selected, List<int> required, int width)
        {
            int quota = PowerCommitmentSeatPolicy.SeatQuota(width, _aggressivePowerCommitment);
            if (quota <= 0)
                return;
            _commitmentGroups.Clear();
            _commitmentGroupOrder.Clear();
            _powerRepresentatives.Clear();
            // Existing RankRepresentatives receives the score-ordered quota pool; walk the same
            // rank order so a first-occurrence tie in the group keys matches.
            for (int position = 0; position < Count; position++)
            {
                int index = _rankOrder[position];
                int commitmentIndex = _commitment[index];
                if (commitmentIndex <= 0)
                    continue;
                FlashPowerCommitmentState commitment = _commitmentStates[commitmentIndex - 1];
                RetentionFacts facts = _facts[index];
                CommitmentGroupKey key = new(
                    (int)commitment.Family,
                    facts.PotionCount,
                    facts.Turn,
                    commitment.CardsLow,
                    commitment.CardsHigh);
                if (_commitmentGroups.TryGetValue(key, out CommitmentGroupLeader leader))
                {
                    if (IsBetterPowerRepresentative(index, leader.Winner))
                        _commitmentGroups[key] = leader with { Winner = index };
                }
                else
                {
                    // The existing GroupBy keeps the first occurrence order; record it for the stable
                    // subgroup ordering after the primary keys tie.
                    _commitmentGroups.Add(key, new CommitmentGroupLeader(index, position));
                    _commitmentGroupOrder.Add(key);
                }
            }
            if (_commitmentGroupOrder.Count == 0)
                return;
            _commitmentGroupOrder.Sort(new CommitmentGroupComparer(this));
            int take = Math.Min(quota, _commitmentGroupOrder.Count);
            for (int index = 0; index < take; index++)
                _powerRepresentatives.Add(_commitmentGroups[_commitmentGroupOrder[index]].Winner);
            for (int index = 0; index < _powerRepresentatives.Count; index++)
                _representative[_powerRepresentatives[index]] = true;
            foreach (int candidate in _powerRepresentatives)
            {
                if (selected.Contains(candidate))
                {
                    AddRequired(required, candidate, width);
                    continue;
                }
                int replaceIndex = -1;
                for (int index = selected.Count - 1; index >= 0; index--)
                {
                    int node = selected[index];
                    if (!_required[node] && !_representative[node])
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
            foreach (int candidate in _powerRepresentatives)
                _representative[candidate] = false;
        }

        /// <summary><c>PowerCommitmentRetention.RankRepresentatives</c> in-group order.</summary>
        private bool IsBetterPowerRepresentative(int candidate, int current)
        {
            FlashPowerCommitmentState a = _commitmentStates[_commitment[candidate] - 1];
            FlashPowerCommitmentState b = _commitmentStates[_commitment[current] - 1];
            if (a.RealizedEvidence != b.RealizedEvidence)
                return a.RealizedEvidence > b.RealizedEvidence;
            if (a.Priority != b.Priority)
                return a.Priority > b.Priority;
            if (a.ProgressEvidence != b.ProgressEvidence)
                return a.ProgressEvidence > b.ProgressEvidence;
            if (a.NetUnrealizedValue != b.NetUnrealizedValue)
                return a.NetUnrealizedValue > b.NetUnrealizedValue;
            if (a.OpenedActionCount != b.OpenedActionCount)
                return a.OpenedActionCount < b.OpenedActionCount;
            if (a.RoundTransitions != b.RoundTransitions)
                return a.RoundTransitions < b.RoundTransitions;
            if (_facts[candidate].ProjectedPlayerHp != _facts[current].ProjectedPlayerHp)
                return _facts[candidate].ProjectedPlayerHp > _facts[current].ProjectedPlayerHp;
            if (_facts[candidate].OffensiveProgressValue != _facts[current].OffensiveProgressValue)
                return _facts[candidate].OffensiveProgressValue > _facts[current].OffensiveProgressValue;
            if (_scores[candidate] != _scores[current])
                return _scores[candidate] > _scores[current];
            return _facts[candidate].Depth < _facts[current].Depth;
        }

        private readonly record struct CommitmentGroupKey(
            int Family, int PotionCount, int Turn, ulong CardsLow, ulong CardsHigh);

        private readonly record struct CommitmentGroupLeader(int Winner, int Position);

        /// <summary>The existing representative ordering: descending keys, then ascending family.</summary>
        private readonly struct CommitmentGroupComparer : IComparer<CommitmentGroupKey>
        {
            private readonly Pool _pool;

            public CommitmentGroupComparer(Pool pool) => _pool = pool;

            public int Compare(CommitmentGroupKey left, CommitmentGroupKey right)
            {
                int leftWinner = _pool._commitmentGroups[left].Winner;
                int rightWinner = _pool._commitmentGroups[right].Winner;
                FlashPowerCommitmentState a = _pool._commitmentStates[_pool._commitment[leftWinner] - 1];
                FlashPowerCommitmentState b = _pool._commitmentStates[_pool._commitment[rightWinner] - 1];
                int comparison = b.RealizedEvidence.CompareTo(a.RealizedEvidence);
                if (comparison != 0) return comparison;
                comparison = ((int)b.Priority).CompareTo((int)a.Priority);
                if (comparison != 0) return comparison;
                comparison = b.ProgressEvidence.CompareTo(a.ProgressEvidence);
                if (comparison != 0) return comparison;
                comparison = b.NetUnrealizedValue.CompareTo(a.NetUnrealizedValue);
                if (comparison != 0) return comparison;
                comparison = a.OpenedActionCount.CompareTo(b.OpenedActionCount);
                if (comparison != 0) return comparison;
                comparison = a.RoundTransitions.CompareTo(b.RoundTransitions);
                if (comparison != 0) return comparison;
                comparison = _pool._facts[rightWinner].ProjectedPlayerHp
                    .CompareTo(_pool._facts[leftWinner].ProjectedPlayerHp);
                if (comparison != 0) return comparison;
                comparison = _pool._scores[rightWinner].CompareTo(_pool._scores[leftWinner]);
                if (comparison != 0) return comparison;
                comparison = left.Family.CompareTo(right.Family);
                return comparison != 0
                    ? comparison
                    : _pool._commitmentGroups[left].Position
                        .CompareTo(_pool._commitmentGroups[right].Position);
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
            bool wideLanes = _policyWidth >= 18;
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
                for (int lane = 0; wideLanes && lane < RetainedLanes.Length; lane++)
                {
                    // BeamRetentionPolicy.Ranking.FindBestLane: only nodes whose accumulated route
                    // traits contain the lane compete.
                    if ((facts.Traits & RetainedLanes[lane]) == 0)
                        continue;
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
                        trait = new TraitLeaders { Position = position, MaxRank = _ranks[source] };
                        _traitLeaders.Add(traitKey, trait);
                    }
                    else if (position < trait.Position)
                    {
                        trait.Position = position;
                    }
                    if (_ranks[source] > trait.MaxRank)
                        trait.MaxRank = _ranks[source];
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

                ulong lineage = _lineages[source];
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
                if ((facts.Traits & SearchRouteTraits.LongTermResource) != 0
                    && (bestLongTermResource < 0
                        || IsBetterLane(facts, _facts[bestLongTermResource],
                            SearchRouteTraits.LongTermResource)))
                {
                    bestLongTermResource = source;
                }
                if ((facts.Traits & SearchRouteTraits.HpInvestment) != 0
                    && (bestHpInvestment < 0
                        || IsBetterLane(facts, _facts[bestHpInvestment], SearchRouteTraits.HpInvestment)))
                {
                    bestHpInvestment = source;
                }
                if (facts.Traits != SearchRouteTraits.None
                    && (bestUtilityDefensive < 0
                        || IsBetterUtilityDefensive(facts, _facts[bestUtilityDefensive])))
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
                    if (facts.Traits != SearchRouteTraits.None
                        && (bestPotionFreeUtilityDefensive < 0
                            || IsBetterUtilityDefensive(facts, _facts[bestPotionFreeUtilityDefensive])))
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

            // BeamRetentionPolicy.RankBest groups by the node's actual potion-use count, not by the
            // slot capacity: a search that generates and consumes additional potions can exceed
            // FlashLimits.PotionSlots uses.
            _potionCountOrder.Clear();
            foreach (int potionCount in _potionLeaders.Keys)
                _potionCountOrder.Add(potionCount);
            CollectionsMarshal.AsSpan(_potionCountOrder).Sort();

            AddRoutingChoiceRepresentatives(required, width);
            AddPotionLineageRepresentatives(required, width);

            for (int potionIndex = 0; potionIndex < _potionCountOrder.Count; potionIndex++)
            {
                int potionCount = _potionCountOrder[potionIndex];
                PotionLeaders potion = _potionLeaders[potionCount];
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

            if (_policyWidth >= 18)
            {
                for (int potionIndex = 0; potionIndex < _potionCountOrder.Count; potionIndex++)
                {
                    int potionCount = _potionCountOrder[potionIndex];
                    _standPatGroup.Clear();
                    for (int position = 0; position < Count; position++)
                    {
                        int slot = _rankOrder[position];
                        if (_facts[slot].PotionCount == potionCount)
                            _standPatGroup.Add(slot);
                    }
                    if (_standPatGroup.Count == 0)
                        continue;
                    AddRequired(required, FindBestDamagingContinuation(_standPatGroup), width);
                    AddRequired(required, FindBestFreshResourceStandPat(_standPatGroup), width);
                    AddRequired(required, FindBestStandPat(_standPatGroup, SearchRouteTraits.Scaling), width);
                    AddRequired(required, FindBestStandPat(_standPatGroup, SearchRouteTraits.Resource), width);
                    AddRequired(required, FindBestStandPat(_standPatGroup, SearchRouteTraits.Control), width);
                }
            }

            // BeamRetentionPolicy.RankBest: persistent setup trait groups (Focus, Thunder, OrbEngine,
            // …) keep their defensive and setup leaders; the group limit is max(4, width / 6).
            foreach ((int Potion, PersistentSetupTraits Traits) key in _traitLeaders.Keys)
                _traitOrder.Add(key);
            CollectionsMarshal.AsSpan(_traitOrder).Sort(new TraitOrderComparer(_traitLeaders));
            int traitLimit = Math.Max(4, _policyWidth / 6);
            int traitPotion = -1, traitTaken = 0;
            for (int index = 0; index < _traitOrder.Count; index++)
            {
                (int Potion, PersistentSetupTraits Traits) key = _traitOrder[index];
                if (key.Potion != traitPotion)
                {
                    traitPotion = key.Potion;
                    traitTaken = 0;
                }
                if (traitTaken >= traitLimit)
                    continue;
                TraitLeaders trait = _traitLeaders[key];
                AddRequired(required, trait.Defensive, width);
                AddRequired(required, trait.Setup, width);
                traitTaken++;
            }

            // BeamRetentionPolicy.RankBest: focus target groups (clamp(width / 10, 2, 4) targets per
            // potion group) keep their pressure and target-setup leaders.
            foreach ((int Potion, uint? Target) key in _focusLeaders.Keys)
                _focusOrder.Add(key);
            CollectionsMarshal.AsSpan(_focusOrder).Sort(new FocusOrderComparer(_focusLeaders));
            int focusLimit = Math.Clamp(_policyWidth / 10, 2, 4);
            int focusPotion = -1, focusTaken = 0;
            for (int index = 0; index < _focusOrder.Count; index++)
            {
                (int Potion, uint? Target) key = _focusOrder[index];
                if (key.Potion != focusPotion)
                {
                    focusPotion = key.Potion;
                    focusTaken = 0;
                }
                if (focusTaken >= focusLimit)
                    continue;
                FocusLeaders leader = _focusLeaders[key];
                AddRequired(required, leader.Pressure, width);
                AddRequired(required, leader.Setup, width);
                focusTaken++;
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
            foreach (ulong key in _lineageLeaders.Keys)
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
                if (_preserveReplayAllocatorOpening)
                {
                    List<int> candidates = new();
                    foreach (int slot in _rankOrder)
                    {
                        if (_lineages[slot] == _lineageOrder[index])
                            candidates.Add(slot);
                    }
                    AddRequired(required, FindBestCuratedTurnBoundaryHand(candidates), width);
                    int shuffleLeader = -1;
                    for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                    {
                        int slot = candidates[candidateIndex];
                        if (shuffleLeader < 0
                            || _facts[slot].ProjectedShuffleOrderValue
                                > _facts[shuffleLeader].ProjectedShuffleOrderValue
                            || _facts[slot].ProjectedShuffleOrderValue
                                == _facts[shuffleLeader].ProjectedShuffleOrderValue
                                && _ranks[slot] > _ranks[shuffleLeader])
                        {
                            shuffleLeader = slot;
                        }
                    }
                    AddRequired(required, shuffleLeader, width);
                }
            }
            AddOrderedPileRepresentatives(required, width, turnBoundary);
            AddParetoRepresentatives(required, width);
        }

        /// <summary>The existing routing-context window; Art of War widens it to eight rounds.</summary>
        private const int PersistentRoutingContextRounds = 8;

        /// <summary>
        /// <c>BeamRetentionPolicy.TryGetRetainedRoutingChoice</c>: the last accumulated choice is
        /// current when it happened this turn, and stays current for up to eight turns while an
        /// Art of War relic can still trigger on the next turn.
        /// </summary>
        private bool IsRetainedRoutingChoice(int slot)
        {
            RouteNode route = _routeNodes[_nodes[slot]];
            RetentionFacts facts = _facts[slot];
            if (route.RoutingSignatureId == 0 || route.RoutingChoiceTurn > facts.Turn)
                return false;
            return route.RoutingChoiceTurn == facts.Turn
                || facts.CanTriggerArtOfWarNextTurn
                    && facts.Turn - route.RoutingChoiceTurn <= PersistentRoutingContextRounds;
        }

        /// <summary>
        /// <c>BeamRetentionPolicy.RankBest</c> routing-choice quota and option-leader rotation. Every
        /// option context contributes the leader selected by its actions-since-choice rule (rank leader
        /// at 0, setup leader at 1, target-pressure leader otherwise); the family also contributes its
        /// target-pressure, deck-curation and setup leaders. The first
        /// <c>max(8, 2W/5)</c> distinct candidates become required seats. The Pareto context list and the
        /// direct current-turn extremes belong to the RS2 batch.
        /// </summary>
        private void AddRoutingChoiceRepresentatives(List<int> required, int width)
        {
            _routingChoices.Clear();
            _routingGroups.Clear();
            _routingGroupOrder.Clear();
            _routingContextLists.Clear();
            _routingParetoLists.Clear();
            for (int position = 0; position < Count; position++)
            {
                int slot = _rankOrder[position];
                RouteNode route = _routeNodes[_nodes[slot]];
                int signatureId = route.RoutingSignatureId;
                if (signatureId == 0 || !IsRetainedRoutingChoice(slot))
                    continue;
                if (!_routingGroups.TryGetValue(signatureId, out RoutingGroup? group))
                {
                    group = new RoutingGroup();
                    _routingGroups.Add(signatureId, group);
                    _routingGroupOrder.Add(signatureId);
                }
                group.Nodes.Add(slot);
                if (group.BestScore < 0 || _ranks[slot] > _ranks[group.BestScore])
                    group.BestScore = slot;
                if (group.BestOffense < 0
                    || IsBetterOffensive(_facts[slot], _facts[group.BestOffense]))
                {
                    group.BestOffense = slot;
                }
                if (group.BestDefense < 0
                    || IsBetterDefensive(_facts[slot], _facts[group.BestDefense]))
                {
                    group.BestDefense = slot;
                }
                if (group.BestSetup < 0 || IsBetterSetup(_facts[slot], _facts[group.BestSetup]))
                    group.BestSetup = slot;
                if (group.BestPileOrder < 0
                    || _facts[slot].ProjectedShuffleOrderValue
                        > _facts[group.BestPileOrder].ProjectedShuffleOrderValue
                    || _facts[slot].ProjectedShuffleOrderValue
                        == _facts[group.BestPileOrder].ProjectedShuffleOrderValue
                        && IsBetterSearchNode(_facts[slot], _facts[group.BestPileOrder]))
                {
                    group.BestPileOrder = slot;
                }
                group.MinParentRank = Math.Min(group.MinParentRank, route.RoutingParentRank);
                group.MaxParentScore = Math.Max(group.MaxParentScore, route.RoutingParentScore);
                group.MaxBeamRank = Math.Max(group.MaxBeamRank, _ranks[slot]);
            }
            if (_routingGroupOrder.Count == 0)
                return;

            _routingFamilies.Clear();
            _routingFamilyOrder.Clear();
            for (int index = 0; index < _routingGroupOrder.Count; index++)
            {
                int signatureId = _routingGroupOrder[index];
                RoutingSignature signature = _routingSignatures[signatureId - 1];
                RoutingFamilyKey key = new(signature.Turn, signature.SourceId,
                    (int)signature.Effect, signature.SourcePile);
                if (!_routingFamilies.TryGetValue(key, out RoutingBucket? family))
                {
                    family = new RoutingBucket();
                    _routingFamilies.Add(key, family);
                    _routingFamilyOrder.Add(key);
                }
                family.Groups.Add(signatureId);
                RoutingGroup group = _routingGroups[signatureId];
                family.MinParentRank = Math.Min(family.MinParentRank, group.MinParentRank);
                family.MaxParentScore = Math.Max(family.MaxParentScore, group.MaxParentScore);
                family.MaxBeamRank = Math.Max(family.MaxBeamRank, group.MaxBeamRank);
            }
            CollectionsMarshal.AsSpan(_routingFamilyOrder)
                .Sort(new RoutingBucketComparer<RoutingFamilyKey>(_routingFamilies));
            for (int familyIndex = 0; familyIndex < _routingFamilyOrder.Count; familyIndex++)
            {
                RoutingBucket family = _routingFamilies[_routingFamilyOrder[familyIndex]];
                _routingOptionOrder.Clear();
                _routingOptions.Clear();
                _routingFamilyNodes.Clear();
                for (int groupIndex = 0; groupIndex < family.Groups.Count; groupIndex++)
                {
                    int signatureId = family.Groups[groupIndex];
                    RoutingSignature signature = _routingSignatures[signatureId - 1];
                    RoutingOptionKey optionKey = new(signature.CardId, signature.Upgrade,
                        signature.CardStateKey, signature.CardCount);
                    if (!_routingOptions.TryGetValue(optionKey, out RoutingBucket? option))
                    {
                        option = new RoutingBucket();
                        _routingOptions.Add(optionKey, option);
                        _routingOptionOrder.Add(optionKey);
                    }
                    option.Groups.Add(signatureId);
                    RoutingGroup group = _routingGroups[signatureId];
                    option.MinParentRank = Math.Min(option.MinParentRank, group.MinParentRank);
                    option.MaxParentScore = Math.Max(option.MaxParentScore, group.MaxParentScore);
                    option.MaxBeamRank = Math.Max(option.MaxBeamRank, group.MaxBeamRank);
                    for (int nodeIndex = 0; nodeIndex < group.Nodes.Count; nodeIndex++)
                        _routingFamilyNodes.Add(group.Nodes[nodeIndex]);
                }
                AddRoutingCandidate(PreferMostVulnerableTargetVariant(_routingFamilyNodes,
                    FindBestTargetPressure(_routingFamilyNodes)));
                AddRoutingCandidate(FindBestDeckCuration(_routingFamilyNodes));
                AddRoutingCandidate(FindBestSetup(_routingFamilyNodes));

                CollectionsMarshal.AsSpan(_routingOptionOrder)
                    .Sort(new RoutingBucketComparer<RoutingOptionKey>(_routingOptions));
                for (int optionIndex = 0; optionIndex < _routingOptionOrder.Count; optionIndex++)
                {
                    RoutingBucket option = _routingOptions[_routingOptionOrder[optionIndex]];
                    _routingOptionNodes.Clear();
                    int actionsSinceChoice = int.MaxValue;
                    for (int groupIndex = 0; groupIndex < option.Groups.Count; groupIndex++)
                    {
                        RoutingGroup group = _routingGroups[option.Groups[groupIndex]];
                        for (int nodeIndex = 0; nodeIndex < group.Nodes.Count; nodeIndex++)
                        {
                            int slot = group.Nodes[nodeIndex];
                            _routingOptionNodes.Add(slot);
                            RouteNode route = _routeNodes[_nodes[slot]];
                            actionsSinceChoice = Math.Min(actionsSinceChoice,
                                _facts[slot].Depth - route.RoutingChoiceDepth);
                        }
                    }
                    int leader;
                    if (actionsSinceChoice == 0)
                    {
                        int contextId = option.Groups[0];
                        RoutingGroup context = _routingGroups[contextId];
                        for (int groupIndex = 1; groupIndex < option.Groups.Count; groupIndex++)
                        {
                            RoutingGroup other = _routingGroups[option.Groups[groupIndex]];
                            if (other.MinParentRank < context.MinParentRank
                                || other.MinParentRank == context.MinParentRank
                                    && other.MaxParentScore > context.MaxParentScore)
                            {
                                context = other;
                            }
                        }
                        leader = context.BestScore;
                    }
                    else if (actionsSinceChoice == 1)
                    {
                        leader = FindBestSetup(_routingOptionNodes);
                    }
                    else
                    {
                        leader = PreferMostVulnerableTargetVariant(_routingOptionNodes,
                            FindBestTargetPressure(_routingOptionNodes));
                    }
                    AddRoutingCandidate(leader);
                }
                _routingContextLists.Add(OrderRoutingFamilyContexts(family));
            }
            AppendDirectRoutingChoiceExtremes();
            AppendRoutingChoiceContextPareto();
            int quota = Math.Max(8, _policyWidth * 2 / 5);
            for (int index = 0; index < _routingChoices.Count && index < quota; index++)
                AddRequired(required, _routingChoices[index], width);

            // BeamRetentionPolicy.RankBest: per potion count, the defensive and setup leaders among
            // nodes whose Art of War window can still trigger next turn.
            for (int potionIndex = 0; potionIndex < _potionCountOrder.Count; potionIndex++)
            {
                int potionCount = _potionCountOrder[potionIndex];
                _standPatGroup.Clear();
                int defensive = -1, setup = -1;
                for (int position = 0; position < Count; position++)
                {
                    int slot = _rankOrder[position];
                    RetentionFacts facts = _facts[slot];
                    if (facts.PotionCount != potionCount || !facts.CanTriggerArtOfWarNextTurn)
                        continue;
                    _standPatGroup.Add(slot);
                    if (defensive < 0 || IsBetterDefensive(facts, _facts[defensive]))
                        defensive = slot;
                    if (setup < 0 || IsBetterSetupCombo(facts, _facts[setup]))
                        setup = slot;
                }
                AddRequired(required, defensive, width);
                AddRequired(required, setup, width);
            }
        }

        private int FindBestCuratedTurnBoundaryHand(List<int> nodes)
        {
            int best = -1;
            for (int index = 0; index < nodes.Count; index++)
            {
                int node = nodes[index];
                if (best < 0 || IsBetterCuratedTurnBoundaryHand(_facts[node], _facts[best]))
                    best = node;
            }
            return best;
        }

        /// <summary>
        /// <c>BeamRetentionPolicy.OrderRoutingChoiceEventContexts</c>: option groups ordered by
        /// (min parent rank, parent score, beam rank); contexts inside each option in the same order;
        /// blocks of at most eight contexts interleaved across options.
        /// </summary>
        private List<int> OrderRoutingFamilyContexts(RoutingBucket family)
        {
            _routingContextNodes.Clear();
            _routingOptions.Clear();
            _routingOptionOrder.Clear();
            for (int groupIndex = 0; groupIndex < family.Groups.Count; groupIndex++)
            {
                int signatureId = family.Groups[groupIndex];
                RoutingSignature signature = _routingSignatures[signatureId - 1];
                RoutingOptionKey optionKey = new(signature.CardId, signature.Upgrade,
                    signature.CardStateKey, signature.CardCount);
                if (!_routingOptions.TryGetValue(optionKey, out RoutingBucket? option))
                {
                    option = new RoutingBucket();
                    _routingOptions.Add(optionKey, option);
                    _routingOptionOrder.Add(optionKey);
                }
                option.Groups.Add(signatureId);
                RoutingGroup group = _routingGroups[signatureId];
                option.MinParentRank = Math.Min(option.MinParentRank, group.MinParentRank);
                option.MaxParentScore = Math.Max(option.MaxParentScore, group.MaxParentScore);
                option.MaxBeamRank = Math.Max(option.MaxBeamRank, group.MaxBeamRank);
            }
            CollectionsMarshal.AsSpan(_routingOptionOrder)
                .Sort(new RoutingBucketComparer<RoutingOptionKey>(_routingOptions));
            List<List<int>> optionGroups = new(_routingOptionOrder.Count);
            for (int optionIndex = 0; optionIndex < _routingOptionOrder.Count; optionIndex++)
            {
                RoutingBucket option = _routingOptions[_routingOptionOrder[optionIndex]];
                option.Groups.Sort(new RoutingGroupComparer(this));
                optionGroups.Add(option.Groups);
            }
            List<int> ordered = new(family.Groups.Count);
            for (int roundStart = 0; ; roundStart += PersistentRoutingContextRounds)
            {
                bool any = false;
                for (int optionIndex = 0; optionIndex < optionGroups.Count; optionIndex++)
                {
                    List<int> group = optionGroups[optionIndex];
                    int roundEnd = Math.Min(group.Count, roundStart + PersistentRoutingContextRounds);
                    for (int index = roundStart; index < roundEnd; index++)
                        ordered.Add(group[index]);
                    any |= roundEnd > roundStart;
                }
                if (!any)
                    break;
            }
            return ordered;
        }

        private readonly struct RoutingGroupComparer : IComparer<int>
        {
            private readonly Pool _pool;

            public RoutingGroupComparer(Pool pool) => _pool = pool;

            public int Compare(int left, int right)
            {
                RoutingGroup a = _pool._routingGroups[left];
                RoutingGroup b = _pool._routingGroups[right];
                int comparison = a.MinParentRank.CompareTo(b.MinParentRank);
                if (comparison != 0) return comparison;
                comparison = b.MaxParentScore.CompareTo(a.MaxParentScore);
                if (comparison != 0) return comparison;
                return b.MaxBeamRank.CompareTo(a.MaxBeamRank);
            }
        }

        /// <summary>
        /// <c>BeamRetentionPolicy.RankBest</c>'s routing-context Pareto: every ordered context keeps
        /// its safe discard continuation (discard contexts), its short/long-horizon leaders, the first
        /// 16 rank-ordered members and the context's score/offense/defense/ordered-pile leaders, minus
        /// dominated members. The per-context lists are interleaved into the routing quota.
        /// </summary>
        private void AppendRoutingChoiceContextPareto()
        {
            _routingParetoLists.Clear();
            int rounds = 0;
            for (int index = 0; index < _routingContextLists.Count; index++)
                rounds = Math.Max(rounds, _routingContextLists[index].Count);
            _routingContextOrder.Clear();
            for (int round = 0; round < rounds; round++)
            {
                for (int family = 0; family < _routingContextLists.Count; family++)
                {
                    List<int> contexts = _routingContextLists[family];
                    if (round < contexts.Count)
                        _routingContextOrder.Add(contexts[round]);
                }
            }
            for (int contextIndex = 0; contextIndex < _routingContextOrder.Count; contextIndex++)
                _routingParetoLists.Add(BuildRoutingContextPareto(_routingContextOrder[contextIndex]));
            int paretoRound = 0;
            while (_routingChoices.Count < RoutingChoiceLimit)
            {
                bool any = false;
                for (int listIndex = 0; listIndex < _routingParetoLists.Count; listIndex++)
                {
                    List<int> pareto = _routingParetoLists[listIndex];
                    if (paretoRound >= pareto.Count)
                        continue;
                    any = true;
                    AddRoutingCandidate(pareto[paretoRound]);
                    if (_routingChoices.Count >= RoutingChoiceLimit)
                        break;
                }
                if (!any)
                    break;
                paretoRound++;
            }
        }

        private List<int> BuildRoutingContextPareto(int signatureId)
        {
            RoutingGroup group = _routingGroups[signatureId];
            List<int> nodes = group.Nodes;
            List<int> candidates = _routingParetoCandidates;
            candidates.Clear();
            RoutingSignature signature = _routingSignatures[signatureId - 1];
            if (signature.Effect == FlashChoiceEffect.Discard)
                AddContextCandidate(candidates, FindBestSafeDiscardContinuation(nodes));
            int actionsSince = int.MaxValue;
            for (int index = 0; index < nodes.Count; index++)
            {
                RouteNode route = _routeNodes[_nodes[nodes[index]]];
                actionsSince = Math.Min(actionsSince, _facts[nodes[index]].Depth - route.RoutingChoiceDepth);
            }
            int bestTarget = PreferMostVulnerableTargetVariant(nodes, FindBestTargetPressure(nodes));
            if (actionsSince <= 1)
            {
                AddContextCandidate(candidates, group.BestSetup);
                AddContextCandidate(candidates, bestTarget);
            }
            else
            {
                AddContextCandidate(candidates, bestTarget);
                AddContextCandidate(candidates, FindBestDeckCuration(nodes));
                AddContextCandidate(candidates, group.BestSetup);
            }
            for (int index = 0; index < nodes.Count && index < 16; index++)
                AddContextCandidate(candidates, nodes[index]);
            AddContextCandidate(candidates, group.BestScore);
            AddContextCandidate(candidates, group.BestOffense);
            AddContextCandidate(candidates, group.BestDefense);
            AddContextCandidate(candidates, group.BestPileOrder);
            List<int> pareto = new(candidates.Count);
            for (int index = 0; index < candidates.Count; index++)
            {
                int candidate = candidates[index];
                bool dominated = false;
                for (int other = 0; other < candidates.Count; other++)
                {
                    int comparison = candidates[other];
                    if (comparison != candidate && ParetoDominates(comparison, candidate))
                    {
                        dominated = true;
                        break;
                    }
                }
                if (!dominated)
                    pareto.Add(candidate);
            }
            return pareto;
        }

        private static void AddContextCandidate(List<int> candidates, int candidate)
        {
            if (candidate < 0 || candidates.Contains(candidate))
                return;
            candidates.Add(candidate);
        }

        /// <summary>
        /// <c>BeamRetentionPolicy.BuildDirectRoutingChoiceExtremes</c>: a current-turn choice whose
        /// parent had no energy left. Within one parent state only the widest/hardest-hitting/strongest
        /// variant survives; families keep at most 96 parents and all families together at most 96
        /// candidates in (parent rank, parent beam score) order.
        /// </summary>
        private void AppendDirectRoutingChoiceExtremes()
        {
            _directByParent.Clear();
            _directFamilies.Clear();
            _directFamilyStats.Clear();
            _directOrder.Clear();
            for (int position = 0; position < Count; position++)
            {
                int slot = _rankOrder[position];
                RouteNode route = _routeNodes[_nodes[slot]];
                RetentionFacts facts = _facts[slot];
                if (route.RoutingSignatureId == 0 || route.Parent < 0
                    || route.RoutingParentEnergy != 0
                    || route.RoutingChoiceDepth != facts.Depth
                    || route.RoutingChoiceTurn != facts.Turn)
                {
                    continue;
                }
                RoutingSignature signature = _routingSignatures[route.RoutingSignatureId - 1];
                RoutingFamilyKey family = new(signature.Turn, signature.SourceId,
                    (int)signature.Effect, signature.SourcePile);
                int parentKey = route.Parent;
                if (!_directByParent.TryGetValue(parentKey, out int current))
                    _directByParent.Add(parentKey, slot);
                else if (IsBetterDirectChoice(slot, current, signature))
                    _directByParent[parentKey] = slot;
                if (!_directFamilies.TryGetValue(family, out List<int>? familyNodes))
                {
                    familyNodes = new List<int>();
                    _directFamilies.Add(family, familyNodes);
                    _directOrder.Add(family);
                }
                familyNodes.Add(slot);
                if (_directFamilyStats.TryGetValue(family, out var stats))
                {
                    _directFamilyStats[family] = (Math.Min(stats.MinRank, route.RoutingParentRank),
                        Math.Max(stats.MaxScore, route.RoutingParentScore));
                }
                else
                {
                    _directFamilyStats.Add(family,
                        (route.RoutingParentRank, route.RoutingParentScore));
                }
            }
            if (_directByParent.Count == 0)
                return;
            CollectionsMarshal.AsSpan(_directOrder).Sort(new DirectFamilyComparer(_directFamilyStats));
            List<int> chosen = new(_directByParent.Count);
            for (int familyIndex = 0; familyIndex < _directOrder.Count; familyIndex++)
            {
                List<int> familyNodes = _directFamilies[_directOrder[familyIndex]];
                List<int> perParent = new();
                for (int index = 0; index < familyNodes.Count; index++)
                {
                    int slot = familyNodes[index];
                    if (_directByParent[_routeNodes[_nodes[slot]].Parent] == slot)
                        perParent.Add(slot);
                }
                perParent.Sort(new DirectParentComparer(this));
                for (int index = 0; index < perParent.Count && index < RoutingChoiceLimit; index++)
                    chosen.Add(perParent[index]);
            }
            chosen.Sort(new DirectCandidateComparer(this));
            for (int index = 0; index < chosen.Count && _routingChoices.Count < RoutingChoiceLimit; index++)
                AddRoutingCandidate(chosen[index]);
        }

        private bool IsBetterDirectChoice(int candidate, int current, in RoutingSignature signature)
        {
            int candidateCardinality = signature.CardCount > 1 ? signature.CardCount : 1;
            RoutingSignature currentSignature =
                _routingSignatures[_routeNodes[_nodes[current]].RoutingSignatureId - 1];
            int currentCardinality = currentSignature.CardCount > 1 ? currentSignature.CardCount : 1;
            if (candidateCardinality != currentCardinality)
                return candidateCardinality > currentCardinality;
            long candidateDensity = _facts[candidate].AttackDensity;
            long currentDensity = _facts[current].AttackDensity;
            if (candidateDensity != currentDensity)
                return candidateDensity > currentDensity;
            return _ranks[candidate] > _ranks[current];
        }

        private readonly struct DirectFamilyComparer : IComparer<RoutingFamilyKey>
        {
            private readonly Dictionary<RoutingFamilyKey, (int MinRank, double MaxScore)> _stats;

            public DirectFamilyComparer(
                Dictionary<RoutingFamilyKey, (int MinRank, double MaxScore)> stats) => _stats = stats;

            public int Compare(RoutingFamilyKey left, RoutingFamilyKey right)
            {
                (int MinRank, double MaxScore) a = _stats[left];
                (int MinRank, double MaxScore) b = _stats[right];
                int comparison = a.MinRank.CompareTo(b.MinRank);
                return comparison != 0 ? comparison : b.MaxScore.CompareTo(a.MaxScore);
            }
        }

        private readonly struct DirectParentComparer : IComparer<int>
        {
            private readonly Pool _pool;

            public DirectParentComparer(Pool pool) => _pool = pool;

            public int Compare(int left, int right)
            {
                RouteNode a = _pool._routeNodes[_pool._nodes[left]];
                RouteNode b = _pool._routeNodes[_pool._nodes[right]];
                int comparison = a.RoutingParentRank.CompareTo(b.RoutingParentRank);
                return comparison != 0 ? comparison : b.RoutingParentScore.CompareTo(a.RoutingParentScore);
            }
        }

        private readonly struct DirectCandidateComparer : IComparer<int>
        {
            private readonly Pool _pool;

            public DirectCandidateComparer(Pool pool) => _pool = pool;

            public int Compare(int left, int right)
            {
                RouteNode a = _pool._routeNodes[_pool._nodes[left]];
                RouteNode b = _pool._routeNodes[_pool._nodes[right]];
                int comparison = a.RoutingParentRank.CompareTo(b.RoutingParentRank);
                return comparison != 0 ? comparison : b.RoutingParentScore.CompareTo(a.RoutingParentScore);
            }
        }

        /// <summary>
        /// <c>BeamRetentionPolicy.FindBestSafeDiscardContinuation</c>: a non-terminal, non-turn-ending
        /// play after a retained discard context that keeps projected HP at least at the current HP,
        /// preferring projected HP, offensive progress, reachable hand value and the offensive
        /// comparator.
        /// </summary>
        private int FindBestSafeDiscardContinuation(List<int> nodes)
        {
            int best = -1;
            for (int index = 0; index < nodes.Count; index++)
            {
                int node = nodes[index];
                RetentionFacts facts = _facts[node];
                RouteNode route = _routeNodes[_nodes[node]];
                if (facts.AllEnemiesDead || facts.PlayerDead || route.Parent < 0
                    || route.Action.Kind != FlashActionKind.PlayCard
                    || facts.Turn != route.ParentTurn
                    || facts.Depth - route.RoutingChoiceDepth < 1
                    || facts.ProjectedPlayerHp < facts.PlayerHp)
                {
                    continue;
                }
                if (best < 0)
                {
                    best = node;
                    continue;
                }
                RetentionFacts current = _facts[best];
                if (facts.ProjectedPlayerHp > current.ProjectedPlayerHp
                    || facts.ProjectedPlayerHp == current.ProjectedPlayerHp
                        && (facts.OffensiveProgressValue > current.OffensiveProgressValue
                            || facts.OffensiveProgressValue == current.OffensiveProgressValue
                                && (facts.ReachableHandValue > current.ReachableHandValue
                                    || facts.ReachableHandValue == current.ReachableHandValue
                                        && IsBetterOffensive(facts, current))))
                {
                    best = node;
                }
            }
            return best;
        }

        private readonly List<int> _routingFamilyNodes = new();
        private readonly List<int> _routingOptionNodes = new();
        private readonly List<List<int>> _routingContextLists = new();
        private readonly List<List<int>> _routingParetoLists = new();
        private readonly List<int> _routingContextOrder = new();
        private readonly List<int> _routingParetoCandidates = new();
        private readonly List<RoutingFamilyKey> _directOrder = new();
        private readonly Dictionary<int, int> _directByParent = new();
        private readonly Dictionary<RoutingFamilyKey, List<int>> _directFamilies = new();
        private readonly Dictionary<RoutingFamilyKey, (int MinRank, double MaxScore)> _directFamilyStats = new();
        private readonly List<int> _routingContextNodes = new();

        private void AddRoutingCandidate(int candidate)
        {
            if (candidate < 0 || _routingChoices.Count >= RoutingChoiceLimit
                || _routingChoices.Contains(candidate))
            {
                return;
            }
            _routingChoices.Add(candidate);
        }

        private int FindBestTargetPressure(List<int> nodes)
        {
            int best = -1;
            for (int index = 0; index < nodes.Count; index++)
            {
                int node = nodes[index];
                if (best < 0 || IsBetterTargetPressure(_facts[node], _facts[best]))
                    best = node;
            }
            return best;
        }

        private int FindBestDeckCuration(List<int> nodes)
        {
            int best = -1;
            for (int index = 0; index < nodes.Count; index++)
            {
                int node = nodes[index];
                if (best < 0 || IsBetterDeckCuration(_facts[node], _facts[best]))
                    best = node;
            }
            return best;
        }

        private int FindBestSetup(List<int> nodes)
        {
            int best = -1;
            for (int index = 0; index < nodes.Count; index++)
            {
                int node = nodes[index];
                if (best < 0 || IsBetterSetupCombo(_facts[node], _facts[best]))
                    best = node;
            }
            return best;
        }

        private int PreferMostVulnerableTargetVariant(List<int> nodes, int candidate)
        {
            if (candidate < 0)
                return candidate;
            RouteNode candidateRoute = _routeNodes[_nodes[candidate]];
            if (candidateRoute.TargetCombatId == int.MinValue)
                return candidate;
            int preferred = -1;
            for (int index = 0; index < nodes.Count; index++)
            {
                int node = nodes[index];
                RouteNode route = _routeNodes[_nodes[node]];
                if (route.Action.Kind != candidateRoute.Action.Kind
                    || route.ActionCardId != candidateRoute.ActionCardId
                    || route.PotionId != candidateRoute.PotionId)
                {
                    continue;
                }
                if (_facts[node].MostVulnerableTargetCombatId != route.TargetCombatId)
                    continue;
                if (preferred < 0 || _ranks[node] > _ranks[preferred])
                    preferred = node;
            }
            return preferred >= 0 ? preferred : candidate;
        }

        private readonly struct RoutingBucketComparer<TKey> : IComparer<TKey>
            where TKey : notnull
        {
            private readonly Dictionary<TKey, RoutingBucket> _buckets;

            public RoutingBucketComparer(Dictionary<TKey, RoutingBucket> buckets) => _buckets = buckets;

            public int Compare(TKey? left, TKey? right)
            {
                RoutingBucket a = _buckets[left!];
                RoutingBucket b = _buckets[right!];
                int comparison = a.MinParentRank.CompareTo(b.MinParentRank);
                if (comparison != 0) return comparison;
                comparison = b.MaxParentScore.CompareTo(a.MaxParentScore);
                if (comparison != 0) return comparison;
                return b.MaxBeamRank.CompareTo(a.MaxBeamRank);
            }
        }

        /// <summary>
        /// <c>BeamRetentionPolicy.MultiObjectiveDominates</c> over the K4-checked retention facts.
        /// The original compares the three distribution fingerprints as a cohort gate; Flash has no
        /// fingerprint projection on the route node, so the cohort is the equivalent coarse tuple of
        /// the enemy combat/control and live-pile aggregates. All dominance dimensions are exact facts.
        /// </summary>
        private ParetoCohortKey BuildParetoCohort(int slot)
        {
            RetentionFacts f = _facts[slot];
            return new ParetoCohortKey(f.EnemyCombatDistributionFirst, f.EnemyCombatDistributionSecond,
                f.EnemyControlDistributionFirst, f.EnemyControlDistributionSecond,
                f.UnorderedPileKeyFirst, f.UnorderedPileKeySecond);
        }

        private bool ParetoDominates(int left, int right)
        {
            if (left == right)
                return false;
            // BeamRetentionPolicy.MultiObjectiveDominates: different combat/control/pile cohorts are
            // never comparable.
            if (BuildParetoCohort(left) != BuildParetoCohort(right))
                return false;
            RetentionFacts a = _facts[left];
            RetentionFacts b = _facts[right];
            bool noWorse = a.ProjectedPlayerHp >= b.ProjectedPlayerHp
                && a.PlayerMaxHp >= b.PlayerMaxHp
                && a.CumulativePlayerHpLost <= b.CumulativePlayerHpLost
                && a.LongTermResourceValue >= b.LongTermResourceValue
                && a.StrategicHpCredit >= b.StrategicHpCredit
                && (a.RelicSatisfiedMask & b.RelicSatisfiedMask) == b.RelicSatisfiedMask
                && a.StrategyGoalCount >= b.StrategyGoalCount
                && a.AngerCopiesGenerated <= b.AngerCopiesGenerated
                && (!_preserveTheft || a.OutstandingStolenResource <= b.OutstandingStolenResource)
                && a.AliveEnemyCount <= b.AliveEnemyCount
                && a.EnemyHp <= b.EnemyHp
                && a.RawEnemyHp <= b.RawEnemyHp
                && a.MaxCurrentEnemyHp <= b.MaxCurrentEnemyHp
                && a.PersistentBuffValue >= b.PersistentBuffValue
                && a.LatentSetupValue >= b.LatentSetupValue
                && a.DelayedDamageValue >= b.DelayedDamageValue
                && a.ReactiveDamageValue >= b.ReactiveDamageValue
                && a.EnemyStrengthSuppression >= b.EnemyStrengthSuppression
                && a.EnemyWeakTurns >= b.EnemyWeakTurns
                && a.EnemyVulnerableTurns >= b.EnemyVulnerableTurns
                && a.FocusTargetVulnerableTurns >= b.FocusTargetVulnerableTurns
                && a.Energy >= b.Energy
                && a.Stars >= b.Stars
                && a.FutureResourceValue >= b.FutureResourceValue
                && a.OstyHp >= b.OstyHp
                && a.OstyMaxHp >= b.OstyMaxHp
                && a.RetainedAttackGrowth >= b.RetainedAttackGrowth
                && a.ReplayPotentialValue >= b.ReplayPotentialValue
                && a.FocusTargetPressure >= b.FocusTargetPressure
                && a.SandpitRemaining >= b.SandpitRemaining
                && a.LiveDeckClutter <= b.LiveDeckClutter
                && a.LiveDeckSize <= b.LiveDeckSize
                && a.PotionCount <= b.PotionCount
                && a.PotionStrategicCost <= b.PotionStrategicCost
                && a.FutureSoldHp <= b.FutureSoldHp
                && a.Depth <= b.Depth;
            if (!noWorse)
                return false;
            return a.ProjectedPlayerHp > b.ProjectedPlayerHp
                || a.PlayerMaxHp > b.PlayerMaxHp
                || a.CumulativePlayerHpLost < b.CumulativePlayerHpLost
                || a.LongTermResourceValue > b.LongTermResourceValue
                || a.AngerCopiesGenerated < b.AngerCopiesGenerated
                || _preserveTheft && a.OutstandingStolenResource < b.OutstandingStolenResource
                || a.AliveEnemyCount < b.AliveEnemyCount
                || a.EnemyHp < b.EnemyHp
                || a.RawEnemyHp < b.RawEnemyHp
                || a.MaxCurrentEnemyHp < b.MaxCurrentEnemyHp
                || a.PersistentBuffValue > b.PersistentBuffValue
                || a.LatentSetupValue > b.LatentSetupValue
                || a.DelayedDamageValue > b.DelayedDamageValue
                || a.ReactiveDamageValue > b.ReactiveDamageValue
                || a.EnemyStrengthSuppression > b.EnemyStrengthSuppression
                || a.EnemyWeakTurns > b.EnemyWeakTurns
                || a.EnemyVulnerableTurns > b.EnemyVulnerableTurns
                || a.FocusTargetVulnerableTurns > b.FocusTargetVulnerableTurns
                || a.Energy > b.Energy
                || a.Stars > b.Stars
                || a.FutureResourceValue > b.FutureResourceValue
                || a.OstyHp > b.OstyHp
                || a.OstyMaxHp > b.OstyMaxHp
                || a.RetainedAttackGrowth > b.RetainedAttackGrowth
                || a.ReplayPotentialValue > b.ReplayPotentialValue
                || a.FocusTargetPressure > b.FocusTargetPressure
                || a.SandpitRemaining > b.SandpitRemaining
                || a.LiveDeckClutter < b.LiveDeckClutter
                || a.LiveDeckSize < b.LiveDeckSize
                || a.PotionCount < b.PotionCount
                || a.PotionStrategicCost < b.PotionStrategicCost
                || a.FutureSoldHp < b.FutureSoldHp
                || a.Depth < b.Depth;
        }

        /// <summary>
        /// <c>CardChoiceSupport.BuildAutomaticPolicyChoice</c>: <c>min(MinCount, option count)</c>
        /// options ordered by card value (ascending for hand sources, descending otherwise), ties by
        /// (card id, upgrade, card-state hash) ordinal.
        /// </summary>
        private bool TryAutomaticTurnStartChoice(in FlashBattle battle, in FlashChoiceRequest request,
            out ulong selection)
        {
            selection = 0;
            int count = Math.Min(request.MinCount, request.OptionCount);
            if (count <= 0)
                return true;
            Span<(double Value, int Id, int Upgrade, ulong Hash)> options =
                stackalloc (double, int, int, ulong)[request.OptionCount];
            for (int index = 0; index < request.OptionCount; index++)
            {
                FlashCard card = request.OptionsAreGenerated
                    ? request.GeneratedOptions[index]
                    : battle.Cards[request.Options[index]];
                options[index] = (_scorer.AutomaticChoiceValue(battle, card), card.Id, card.Upgrade,
                    FlashFingerprint.HashCard(card));
            }
            bool ascending = !request.OptionsAreGenerated && request.Source == FlashPile.Hand;
            Span<bool> taken = stackalloc bool[request.OptionCount];
            // stackalloc memory is not zero-initialized; without this clear a stale true byte skips
            // every option and the probe appends an empty selection for a 1-of-5 request.
            taken.Clear();
            for (int picked = 0; picked < count; picked++)
            {
                int best = -1;
                for (int index = 0; index < request.OptionCount; index++)
                {
                    if (taken[index])
                        continue;
                    if (best < 0 || IsBetterAutomaticChoice(options[index], options[best], ascending))
                        best = index;
                }
                if (best < 0)
                    break;
                taken[best] = true;
                selection |= 1UL << best;
            }
            return true;
        }

        private static bool IsBetterAutomaticChoice(
            (double Value, int Id, int Upgrade, ulong Hash) candidate,
            (double Value, int Id, int Upgrade, ulong Hash) current, bool ascending)
        {
            int comparison = candidate.Value.CompareTo(current.Value);
            if (!ascending)
                comparison = -comparison;
            if (comparison != 0)
                return comparison < 0;
            if (candidate.Id != current.Id)
                return candidate.Id < current.Id;
            if (candidate.Upgrade != current.Upgrade)
                return candidate.Upgrade < current.Upgrade;
            return candidate.Hash < current.Hash;
        }

        /// <summary>
        /// The existing search's <c>ComputeStandPat</c> (<c>StandPatJobs</c>/<c>Retention</c>): replay EndTurn on the retained state and read the
        /// resulting state's resources. Flash has no simulator; the kernel step is the same end-turn
        /// semantics. A request for a turn-start choice makes the probe unavailable (default), which is
        /// the one boundary the existing roll-out resolves through its choice policy.
        /// </summary>
        private StandPatEvaluation EvaluateStandPat(int slot)
        {
            FlashBattle end = _states[slot];
            try
            {
                // The existing stand-pat roll-out answers an unprepared turn-start selection with the
                // CardChoiceSupport automatic policy (ascending hand values, descending draw/discard/
                // generated values), then continues the same EndTurn replay. The probe set is thus the
                // one the existing search sees instead of a boundary-only subset.
                FlashAction action = FlashAction.EndTurn();
                FlashStepResult result;
                while (true)
                {
                    result = FlashKernel.Step(ref end, action, _scorer.RulesForRetention,
                        out FlashChoiceRequest request);
                    if (result != FlashStepResult.NeedsChoice)
                        break;
                    if (request.Timing != FlashChoiceTiming.PlayerTurnStart
                        || action.ChoiceCount >= FlashLimits.MaxChoices
                        || !TryAutomaticTurnStartChoice(end, request, out ulong automaticSelection))
                        return default;
                    action = action.WithChoice(automaticSelection);
                    end = _states[slot];
                }
                RetentionFacts before = _facts[slot];
                FlashScoreFeatures features = _scorer.Features(end, before.Depth + 1, includeRetentionOnly: true);
                return new StandPatEvaluation(
                    features.AllEnemiesDead,
                    Math.Max(0, before.EnemyHp - features.EnemyHp),
                    features.ProjectedPlayerHp,
                    features.Energy * 16
                        + features.Stars * 8
                        + features.HandCount
                        + features.ReachableHandValue
                        + features.FutureResourceValue
                        + features.OstyHp * 16
                        + features.OstyMaxHp * 4,
                    true);
            }
            catch (FlashCapacityException)
            {
                return default;
            }
        }

        /// <summary><c>FindBestDamagingContinuation</c>: a shield-breaking playable step with a legal
        /// follow-up attack inside the frozen reachable hand value.</summary>
        private int FindBestDamagingContinuation(List<int> nodes)
        {
            int best = -1;
            for (int index = 0; index < nodes.Count; index++)
            {
                int node = nodes[index];
                RetentionFacts facts = _facts[node];
                RouteNode route = _routeNodes[_nodes[node]];
                if (facts.AllEnemiesDead || facts.PlayerDead || route.Parent < 0
                    || route.Action.Kind != FlashActionKind.PlayCard
                    || facts.EnemyBlock >= facts.ParentEnemyBlock
                    || facts.EnemyHp > facts.ReachableHandValue
                    || (long)facts.EnemyHp + facts.EnemyBlock
                        >= (long)facts.ParentEnemyHp + facts.ParentEnemyBlock
                    || facts.PlayableAttackCount <= 0)
                {
                    continue;
                }
                if (best < 0 || IsBetterOffensive(facts, _facts[best]))
                    best = node;
            }
            return best;
        }

        /// <summary><c>FindBestFreshResourceStandPat</c>: the first 64 rank-ordered fresh-resource
        /// candidates, evaluated with the same EndTurn stand-pat replay and comparator.</summary>
        private int FindBestFreshResourceStandPat(List<int> nodes)
        {
            const int probeLimit = 64;
            int best = -1;
            StandPatEvaluation bestEvaluation = default;
            int evaluated = 0;
            for (int index = 0; index < nodes.Count && evaluated < probeLimit; index++)
            {
                int node = nodes[index];
                RetentionFacts facts = _facts[node];
                RouteNode route = _routeNodes[_nodes[node]];
                if (route.Parent < 0
                    || (facts.FutureResourceValue <= facts.ParentFutureResourceValue
                        && facts.StrategicResourcePotential <= facts.ParentStrategicResourcePotential))
                {
                    continue;
                }
                StandPatEvaluation evaluation = EvaluateStandPat(node);
                evaluated++;
                if (best < 0
                    || evaluation.AllEnemiesDead && !bestEvaluation.AllEnemiesDead
                    || evaluation.AllEnemiesDead == bestEvaluation.AllEnemiesDead
                        && (evaluation.ProjectedPlayerHp > bestEvaluation.ProjectedPlayerHp
                            || evaluation.ProjectedPlayerHp == bestEvaluation.ProjectedPlayerHp
                                && (evaluation.ResourceValue > bestEvaluation.ResourceValue
                                    || evaluation.ResourceValue == bestEvaluation.ResourceValue
                                        && facts.CumulativePlayerHpLost < _facts[best].CumulativePlayerHpLost
                                    || evaluation.ResourceValue == bestEvaluation.ResourceValue
                                        && facts.CumulativePlayerHpLost == _facts[best].CumulativePlayerHpLost
                                        && facts.Score > _facts[best].Score)))
                {
                    best = node;
                    bestEvaluation = evaluation;
                }
            }
            return best;
        }

        /// <summary><c>FindBestStandPat</c>: the same probe ordering and evaluation comparator.</summary>
        private int FindBestStandPat(List<int> nodes, SearchRouteTraits trait)
        {
            const int limit = 8;
            Span<int> probes = stackalloc int[limit + 1];
            int probeCount = 0;
            while (probeCount < limit)
            {
                int best = -1;
                for (int index = 0; index < nodes.Count; index++)
                {
                    int node = nodes[index];
                    RetentionFacts facts = _facts[node];
                    if ((facts.Traits & trait) == 0 || ContainsProbe(probes, probeCount, node))
                        continue;
                    if (best < 0 || IsBetterStandPatProbe(facts, _facts[best], trait))
                        best = node;
                }
                if (best < 0)
                    break;
                probes[probeCount++] = best;
            }
            if (trait == SearchRouteTraits.Control && probeCount > 0)
            {
                int delayedFinish = -1;
                for (int index = 0; index < nodes.Count; index++)
                {
                    int node = nodes[index];
                    RetentionFacts facts = _facts[node];
                    if ((facts.Traits & trait) == 0 || facts.AllEnemiesDead || facts.EnemyHp <= 0
                        || facts.DelayedDamageValue < facts.EnemyHp
                        || ContainsProbe(probes, probeCount, node))
                    {
                        continue;
                    }
                    if (delayedFinish < 0 || IsBetterDelayedFinish(facts, _facts[delayedFinish]))
                        delayedFinish = node;
                }
                if (delayedFinish >= 0)
                {
                    if (probeCount == limit)
                        probeCount--;
                    probes[probeCount++] = delayedFinish;
                }
            }
            int bestNode = -1;
            StandPatEvaluation bestEvaluation = default;
            for (int index = 0; index < probeCount; index++)
            {
                int node = probes[index];
                StandPatEvaluation evaluation = EvaluateStandPat(node);
                int evaluationValue = trait == SearchRouteTraits.Resource
                    ? evaluation.ResourceValue
                    : evaluation.DelayedDamage;
                int bestValue = trait == SearchRouteTraits.Resource
                    ? bestEvaluation.ResourceValue
                    : bestEvaluation.DelayedDamage;
                if (bestNode < 0
                    || evaluation.AllEnemiesDead && !bestEvaluation.AllEnemiesDead
                    || evaluation.AllEnemiesDead == bestEvaluation.AllEnemiesDead
                        && (evaluation.ProjectedPlayerHp > bestEvaluation.ProjectedPlayerHp
                            || evaluation.ProjectedPlayerHp == bestEvaluation.ProjectedPlayerHp
                                && (evaluationValue > bestValue
                                    || evaluationValue == bestValue
                                        && _facts[node].Score > _facts[bestNode].Score)))
                {
                    bestNode = node;
                    bestEvaluation = evaluation;
                }
            }
            return bestNode;
        }

        private static bool ContainsProbe(Span<int> probes, int count, int node)
        {
            for (int index = 0; index < count; index++)
            {
                if (probes[index] == node)
                    return true;
            }
            return false;
        }

        private static bool IsBetterStandPatProbe(in RetentionFacts candidate,
            in RetentionFacts current, SearchRouteTraits trait)
        {
            if (candidate.ProjectedPlayerHp != current.ProjectedPlayerHp)
                return candidate.ProjectedPlayerHp > current.ProjectedPlayerHp;
            int candidateLane = LaneValue(candidate, trait);
            int currentLane = LaneValue(current, trait);
            if (candidateLane != currentLane)
                return candidateLane > currentLane;
            return candidate.Score > current.Score;
        }

        private static bool IsBetterDelayedFinish(in RetentionFacts candidate,
            in RetentionFacts current)
        {
            if (candidate.CumulativePlayerHpLost != current.CumulativePlayerHpLost)
                return candidate.CumulativePlayerHpLost < current.CumulativePlayerHpLost;
            if (candidate.PlayerHp != current.PlayerHp)
                return candidate.PlayerHp > current.PlayerHp;
            if (candidate.EnemyHp != current.EnemyHp)
                return candidate.EnemyHp < current.EnemyHp;
            return candidate.Score > current.Score;
        }

        /// <summary>
        /// <c>OrderedMutationRetention.BuildOrderedPileTacticalKey</c> over the K4-checked facts. Flash
        /// has no per-node shuffle-crossed/boundary fields in the retention facts; both are constant
        /// for a ranked pool (a pool is one turn layer), so they add no grouping distinction here.
        /// </summary>
        private readonly record struct OrderedPileTacticalKey(
            int Turn, int PotionCount, int PotionCost, int FutureSoldHp, int PlayerHp,
            int ProjectedPlayerHp, int PlayerBlock, int EnemyHp, int RawEnemyHp, int MaxCurrentEnemyHp,
            ulong EnemyCombatFirst, ulong EnemyCombatSecond, ulong AliveEnemyMask, int RevivingEnemyCount,
            uint? FocusTarget, int PersistentBuffValue, PersistentSetupTraits StrategySetupTraits,
            int FutureResourceValue, int DelayedDamageValue, int EnemyStrengthSuppression,
            int EnemyWeakTurns, int EnemyVulnerableTurns, int FocusTargetVulnerableTurns,
            ulong EnemyControlFirst, ulong EnemyControlSecond, int SandpitRemaining, int LiveDeckClutter,
            int OutstandingStolenResource, int Energy, int Stars, int HandCount,
            int PocketwatchCardsPlayedThisTurn, int PocketwatchCardsPlayedLastTurn,
            int PocketwatchCardThreshold, ulong UnorderedPileFirst, ulong UnorderedPileSecond);

        private OrderedPileTacticalKey BuildOrderedPileTacticalKey(int slot)
        {
            RetentionFacts f = _facts[slot];
            return new OrderedPileTacticalKey(f.Turn, f.PotionCount, f.PotionStrategicCost,
                f.FutureSoldHp, f.PlayerHp, f.ProjectedPlayerHp, f.PlayerBlock, f.EnemyHp, f.RawEnemyHp,
                f.MaxCurrentEnemyHp, f.EnemyCombatDistributionFirst, f.EnemyCombatDistributionSecond,
                f.AliveEnemyMask, f.RevivingEnemyCount, f.FocusTargetCombatId, f.PersistentBuffValue,
                f.StrategySetupTraits, f.FutureResourceValue, f.DelayedDamageValue,
                f.EnemyStrengthSuppression, f.EnemyWeakTurns, f.EnemyVulnerableTurns,
                f.FocusTargetVulnerableTurns, f.EnemyControlDistributionFirst,
                f.EnemyControlDistributionSecond, f.SandpitRemaining, f.LiveDeckClutter,
                f.OutstandingStolenResource, f.Energy, f.Stars, f.HandCount,
                f.PocketwatchCardsPlayedThisTurn, f.PocketwatchCardsPlayedLastTurn,
                f.PocketwatchCardThreshold, f.UnorderedPileKeyFirst, f.UnorderedPileKeySecond);
        }

        private readonly record struct PocketwatchCadenceKey(
            int PotionCount, uint? FocusTarget, int RetainedAttackGrowth,
            ulong ControlFirst, ulong ControlSecond, bool WithinThreshold, bool CanStillTrigger);

        private PocketwatchCadenceKey BuildPocketwatchCadenceKey(int slot)
        {
            RetentionFacts f = _facts[slot];
            return new PocketwatchCadenceKey(f.PotionCount, f.FocusTargetCombatId, f.RetainedAttackGrowth,
                f.EnemyControlDistributionFirst, f.EnemyControlDistributionSecond,
                f.PocketwatchCardThreshold >= 0
                    && f.PocketwatchCardsPlayedLastTurn <= f.PocketwatchCardThreshold,
                f.PocketwatchCardThreshold >= 0
                    && f.PocketwatchCardsPlayedThisTurn <= f.PocketwatchCardThreshold);
        }

        private readonly record struct PocketwatchCadenceFamilyKey(
            int PotionCount, uint? FocusTarget, int RetainedAttackGrowth, bool WithinThreshold,
            bool CanStillTrigger);

        private PocketwatchCadenceFamilyKey BuildPocketwatchCadenceFamilyKey(int slot)
        {
            PocketwatchCadenceKey key = BuildPocketwatchCadenceKey(slot);
            return new PocketwatchCadenceFamilyKey(key.PotionCount, key.FocusTarget,
                key.RetainedAttackGrowth, key.WithinThreshold, key.CanStillTrigger);
        }

        private sealed class OrderedPileTacticalGroup
        {
            public readonly List<int> Nodes = new();
            public double MaxRank = double.NegativeInfinity;
            public int Position;
        }

        private sealed class PocketwatchCadenceBucket
        {
            public readonly List<OrderedPileTacticalGroup> Groups = new();
            public double MaxRank = double.NegativeInfinity;
            public int Position;
        }

        private sealed class PocketwatchCadenceFamily
        {
            public readonly List<PocketwatchCadenceBucket> Buckets = new();
            public double MaxRank = double.NegativeInfinity;
            public int Position;
        }

        private readonly List<List<int>> _orderedPileCohorts = new();
        private readonly List<int> _orderedPileBuffer = new();
        private readonly Dictionary<OrderedPileTacticalKey, OrderedPileTacticalGroup> _tacticalGroups = new();
        private readonly List<OrderedPileTacticalKey> _tacticalOrder = new();
        private readonly Dictionary<PocketwatchCadenceKey, PocketwatchCadenceBucket> _cadenceBuckets = new();
        private readonly List<PocketwatchCadenceKey> _cadenceOrder = new();
        private readonly Dictionary<PocketwatchCadenceFamilyKey, PocketwatchCadenceFamily> _cadenceFamilies = new();
        private readonly List<PocketwatchCadenceFamilyKey> _cadenceFamilyOrder = new();

        /// <summary>
        /// <c>BeamRetentionPolicy.RankBest</c>'s ordered-pile portfolio: tactical state groups by
        /// max rank, cadence buckets/families by pocketwatch state, Pareto representatives per
        /// cadence, then bounded exact projected-shuffle-order variants per selected tactical group.
        /// The representatives replace ordinary seats (the Flash beam width is unchanged).
        /// </summary>
        private void AddOrderedPileRepresentatives(List<int> required, int width, bool turnBoundary)
        {
            bool anyPocketwatch = false;
            for (int position = 0; position < Count; position++)
            {
                if (_facts[_rankOrder[position]].PocketwatchCardThreshold >= 0)
                {
                    anyPocketwatch = true;
                    break;
                }
            }
            if (!anyPocketwatch)
                return;
            _tacticalGroups.Clear();
            _tacticalOrder.Clear();
            for (int position = 0; position < Count; position++)
            {
                int slot = _rankOrder[position];
                if (_facts[slot].PocketwatchCardThreshold < 0)
                    continue;
                OrderedPileTacticalKey key = BuildOrderedPileTacticalKey(slot);
                if (!_tacticalGroups.TryGetValue(key, out OrderedPileTacticalGroup? group))
                {
                    group = new OrderedPileTacticalGroup { Position = _tacticalOrder.Count };
                    _tacticalGroups.Add(key, group);
                    _tacticalOrder.Add(key);
                }
                group.Nodes.Add(slot);
                group.MaxRank = Math.Max(group.MaxRank, _ranks[slot]);
            }
            if (_tacticalOrder.Count == 0)
                return;
            _tacticalOrder.Sort(new TacticalGroupComparer(_tacticalGroups));
            List<OrderedPileTacticalGroup> tacticalGroups = new(_tacticalOrder.Count);
            for (int index = 0; index < _tacticalOrder.Count; index++)
                tacticalGroups.Add(_tacticalGroups[_tacticalOrder[index]]);

            _cadenceBuckets.Clear();
            _cadenceOrder.Clear();
            for (int index = 0; index < tacticalGroups.Count; index++)
            {
                OrderedPileTacticalGroup group = tacticalGroups[index];
                PocketwatchCadenceKey key = BuildPocketwatchCadenceKey(group.Nodes[0]);
                if (!_cadenceBuckets.TryGetValue(key, out PocketwatchCadenceBucket? bucket))
                {
                    bucket = new PocketwatchCadenceBucket { Position = _cadenceOrder.Count };
                    _cadenceBuckets.Add(key, bucket);
                    _cadenceOrder.Add(key);
                }
                bucket.Groups.Add(group);
                bucket.MaxRank = Math.Max(bucket.MaxRank, group.MaxRank);
            }
            _cadenceOrder.Sort(new CadenceBucketComparer(_cadenceBuckets));
            List<PocketwatchCadenceBucket> buckets = new(_cadenceOrder.Count);
            for (int index = 0; index < _cadenceOrder.Count; index++)
                buckets.Add(_cadenceBuckets[_cadenceOrder[index]]);

            _cadenceFamilies.Clear();
            _cadenceFamilyOrder.Clear();
            for (int index = 0; index < buckets.Count; index++)
            {
                PocketwatchCadenceBucket bucket = buckets[index];
                PocketwatchCadenceFamilyKey key =
                    BuildPocketwatchCadenceFamilyKey(bucket.Groups[0].Nodes[0]);
                if (!_cadenceFamilies.TryGetValue(key, out PocketwatchCadenceFamily? family))
                {
                    family = new PocketwatchCadenceFamily { Position = _cadenceFamilyOrder.Count };
                    _cadenceFamilies.Add(key, family);
                    _cadenceFamilyOrder.Add(key);
                }
                family.Buckets.Add(bucket);
                family.MaxRank = Math.Max(family.MaxRank, bucket.MaxRank);
            }
            _cadenceFamilyOrder.Sort(new CadenceFamilyComparer(_cadenceFamilies));
            buckets.Clear();
            int familyRound = 0;
            while (true)
            {
                bool any = false;
                for (int index = 0; index < _cadenceFamilyOrder.Count; index++)
                {
                    PocketwatchCadenceFamily family = _cadenceFamilies[_cadenceFamilyOrder[index]];
                    if (familyRound < family.Buckets.Count)
                    {
                        buckets.Add(family.Buckets[familyRound]);
                        any = true;
                    }
                }
                if (!any)
                    break;
                familyRound++;
            }

            List<List<OrderedPileTacticalGroup>> paretoByCadence = new(buckets.Count);
            for (int index = 0; index < buckets.Count; index++)
                paretoByCadence.Add(BuildCadencePareto(buckets[index]));

            _orderedPileCohorts.Clear();
            int paretoRound = 0;
            while (true)
            {
                bool any = false;
                for (int index = 0; index < paretoByCadence.Count; index++)
                {
                    List<OrderedPileTacticalGroup> pareto = paretoByCadence[index];
                    if (paretoRound >= pareto.Count)
                        continue;
                    any = true;
                    BuildOrderedPileCohort(pareto[paretoRound]);
                }
                if (!any)
                    break;
                paretoRound++;
            }
            if (_orderedPileCohorts.Count == 0)
                return;

            int limit = Math.Max(1, _policyWidth);
            int orderedPileQuota = Math.Min(width, Math.Max(8, limit * 2 / 3));
            int orderedPileRounds = 0;
            for (int index = 0; index < _orderedPileCohorts.Count; index++)
                orderedPileRounds = Math.Max(orderedPileRounds, _orderedPileCohorts[index].Count);
            int quota = turnBoundary
                ? orderedPileQuota
                : Math.Min(width, limit + 1);
            int exactStateRounds = Math.Min(
                SolverWeights.ExactStatesPerProjectedShuffleOrder, orderedPileRounds);
            for (int round = 0; round < exactStateRounds && required.Count < quota; round++)
            {
                for (int index = 0; index < _orderedPileCohorts.Count; index++)
                {
                    List<int> cohort = _orderedPileCohorts[index];
                    if (round < cohort.Count)
                        AddRequired(required, cohort[round], quota);
                }
            }
            for (int round = exactStateRounds; round < orderedPileRounds && required.Count < quota; round++)
            {
                for (int index = 0; index < _orderedPileCohorts.Count; index++)
                {
                    List<int> cohort = _orderedPileCohorts[index];
                    if (round < cohort.Count)
                        AddRequired(required, cohort[round], quota);
                }
            }
        }

        private List<OrderedPileTacticalGroup> BuildCadencePareto(PocketwatchCadenceBucket bucket)
        {
            List<OrderedPileTacticalGroup> candidates = new();
            AddTacticalGroup(candidates, bucket.Groups[0]);
            AddTacticalGroup(candidates, BestTacticalGroupBy(bucket,
                slot => _facts[slot].ProjectedPlayerHp, descending: true));
            AddTacticalGroup(candidates, BestTacticalGroupBy(bucket,
                slot => _facts[slot].EnemyHp, descending: false));
            AddTacticalGroup(candidates, BestTacticalGroupBy(bucket,
                slot => _facts[slot].AliveEnemyCount, descending: false));
            AddTacticalGroup(candidates, BestTacticalGroupBy(bucket,
                slot => LaneValue(_facts[slot], SearchRouteTraits.Control), descending: true));
            AddTacticalGroup(candidates, BestTacticalGroupBy(bucket,
                slot => LaneValue(_facts[slot], SearchRouteTraits.Resource), descending: true));
            AddTacticalGroup(candidates, BestTacticalGroupBy(bucket,
                slot => _facts[slot].ProjectedShuffleOrderValue, descending: true));
            for (int index = 0; index < bucket.Groups.Count
                && candidates.Count < SolverWeights.PocketwatchParetoCandidatesPerCadence; index++)
            {
                AddTacticalGroup(candidates, bucket.Groups[index]);
            }
            List<OrderedPileTacticalGroup> pareto = new(candidates.Count);
            for (int index = 0; index < candidates.Count; index++)
            {
                OrderedPileTacticalGroup candidate = candidates[index];
                bool dominated = false;
                for (int other = 0; other < candidates.Count; other++)
                {
                    OrderedPileTacticalGroup comparison = candidates[other];
                    if (!ReferenceEquals(comparison, candidate)
                        && ParetoDominates(comparison.Nodes[0], candidate.Nodes[0]))
                    {
                        dominated = true;
                        break;
                    }
                }
                if (!dominated)
                    pareto.Add(candidate);
            }
            return pareto;
        }

        private OrderedPileTacticalGroup BestTacticalGroupBy(PocketwatchCadenceBucket bucket,
            Func<int, int> selector, bool descending)
        {
            OrderedPileTacticalGroup best = bucket.Groups[0];
            int bestValue = GroupExtreme(best, selector, descending);
            double bestRank = best.MaxRank;
            for (int index = 1; index < bucket.Groups.Count; index++)
            {
                OrderedPileTacticalGroup candidate = bucket.Groups[index];
                int value = GroupExtreme(candidate, selector, descending);
                bool better = descending ? value > bestValue : value < bestValue;
                if (better || value == bestValue && candidate.MaxRank > bestRank)
                {
                    best = candidate;
                    bestValue = value;
                    bestRank = candidate.MaxRank;
                }
            }
            return best;
        }

        private int GroupExtreme(OrderedPileTacticalGroup group, Func<int, int> selector,
            bool descending)
        {
            int value = selector(group.Nodes[0]);
            for (int index = 1; index < group.Nodes.Count; index++)
            {
                int candidate = selector(group.Nodes[index]);
                value = descending ? Math.Max(value, candidate) : Math.Min(value, candidate);
            }
            return value;
        }

        private static void AddTacticalGroup(List<OrderedPileTacticalGroup> groups,
            OrderedPileTacticalGroup group)
        {
            if (groups.Contains(group))
                return;
            groups.Add(group);
        }

        private void BuildOrderedPileCohort(OrderedPileTacticalGroup group)
        {
            _orderedPileBuffer.Clear();
            List<int> variants = new();
            List<(ulong First, ulong Second)> prefixes = new();
            for (int index = 0; index < group.Nodes.Count; index++)
            {
                int slot = group.Nodes[index];
                RetentionFacts facts = _facts[slot];
                (ulong First, ulong Second) key =
                    (facts.ProjectedShuffleOrderKeyFirst, facts.ProjectedShuffleOrderKeySecond);
                if (!prefixes.Contains(key))
                    prefixes.Add(key);
            }
            for (int prefix = 0; prefix < prefixes.Count; prefix++)
            {
                List<int> variantsForPrefix = new();
                for (int index = 0; index < group.Nodes.Count; index++)
                {
                    int slot = group.Nodes[index];
                    if ((_facts[slot].ProjectedShuffleOrderKeyFirst,
                            _facts[slot].ProjectedShuffleOrderKeySecond) != prefixes[prefix])
                    {
                        continue;
                    }
                    variantsForPrefix.Add(slot);
                }
                variantsForPrefix.Sort(new OrderedPileVariantComparer(this));
                for (int index = 0; index < variantsForPrefix.Count
                    && index < SolverWeights.ExactStatesPerProjectedShuffleOrder; index++)
                {
                    variants.Add(variantsForPrefix[index]);
                }
            }
            variants.Sort(new OrderedPileVariantComparer(this));
            int take = Math.Min(SolverWeights.OrderedPileVariantsPerTacticalState, variants.Count);
            List<int> cohort = new(take);
            for (int index = 0; index < take; index++)
                cohort.Add(variants[index]);
            _orderedPileCohorts.Add(cohort);
        }

        private readonly struct TacticalGroupComparer : IComparer<OrderedPileTacticalKey>
        {
            private readonly Dictionary<OrderedPileTacticalKey, OrderedPileTacticalGroup> _groups;

            public TacticalGroupComparer(
                Dictionary<OrderedPileTacticalKey, OrderedPileTacticalGroup> groups) => _groups = groups;

            public int Compare(OrderedPileTacticalKey left, OrderedPileTacticalKey right)
            {
                OrderedPileTacticalGroup a = _groups[left];
                OrderedPileTacticalGroup b = _groups[right];
                int comparison = b.MaxRank.CompareTo(a.MaxRank);
                return comparison != 0 ? comparison : a.Position.CompareTo(b.Position);
            }
        }

        private readonly struct CadenceBucketComparer : IComparer<PocketwatchCadenceKey>
        {
            private readonly Dictionary<PocketwatchCadenceKey, PocketwatchCadenceBucket> _buckets;

            public CadenceBucketComparer(
                Dictionary<PocketwatchCadenceKey, PocketwatchCadenceBucket> buckets) => _buckets = buckets;

            public int Compare(PocketwatchCadenceKey left, PocketwatchCadenceKey right)
            {
                PocketwatchCadenceBucket a = _buckets[left];
                PocketwatchCadenceBucket b = _buckets[right];
                int comparison = b.MaxRank.CompareTo(a.MaxRank);
                return comparison != 0 ? comparison : a.Position.CompareTo(b.Position);
            }
        }

        private readonly struct CadenceFamilyComparer : IComparer<PocketwatchCadenceFamilyKey>
        {
            private readonly Dictionary<PocketwatchCadenceFamilyKey, PocketwatchCadenceFamily> _families;

            public CadenceFamilyComparer(
                Dictionary<PocketwatchCadenceFamilyKey, PocketwatchCadenceFamily> families)
                => _families = families;

            public int Compare(PocketwatchCadenceFamilyKey left, PocketwatchCadenceFamilyKey right)
            {
                PocketwatchCadenceFamily a = _families[left];
                PocketwatchCadenceFamily b = _families[right];
                int comparison = b.MaxRank.CompareTo(a.MaxRank);
                return comparison != 0 ? comparison : a.Position.CompareTo(b.Position);
            }
        }

        private readonly struct OrderedPileVariantComparer : IComparer<int>
        {
            private readonly Pool _pool;

            public OrderedPileVariantComparer(Pool pool) => _pool = pool;

            public int Compare(int left, int right)
            {
                int comparison = _pool._facts[right].ProjectedShuffleOrderValue
                    .CompareTo(_pool._facts[left].ProjectedShuffleOrderValue);
                return comparison != 0 ? comparison : _pool._ranks[right].CompareTo(_pool._ranks[left]);
            }
        }

        private void AddParetoRepresentatives(List<int> required, int width)
        {
            if (_policyWidth < 18)
                return;
            _paretoCohorts.Clear();
            for (int position = 0; position < Count; position++)
            {
                int slot = _rankOrder[position];
                ParetoCohortKey key = BuildParetoCohort(slot);
                if (!_paretoCohorts.TryGetValue(key, out List<int>? cohort))
                {
                    cohort = new List<int>();
                    _paretoCohorts.Add(key, cohort);
                }
                cohort.Add(slot);
            }
            _pareto.Clear();
            for (int position = 0; position < Count; position++)
            {
                int slot = _rankOrder[position];
                List<int> cohort = _paretoCohorts[BuildParetoCohort(slot)];
                bool dominated = false;
                for (int index = 0; index < cohort.Count; index++)
                {
                    int other = cohort[index];
                    if (other != slot && ParetoDominates(other, slot))
                    {
                        dominated = true;
                        break;
                    }
                }
                if (dominated)
                    continue;
                int insert = 0;
                while (insert < _pareto.Count
                    && (_scores[_pareto[insert]] > _scores[slot]
                        || _scores[_pareto[insert]].Equals(_scores[slot])
                            && _facts[_pareto[insert]].Depth <= _facts[slot].Depth))
                {
                    insert++;
                }
                if (insert >= 3)
                    continue;
                _pareto.Insert(insert, slot);
                if (_pareto.Count > 3)
                    _pareto.RemoveAt(3);
            }
            for (int index = 0; index < _pareto.Count; index++)
                AddRequired(required, _pareto[index], width);
        }

        /// <summary>
        /// <c>BeamRetentionPolicy.RankBest</c> potion-use lineage groups: for every potion count, the
        /// best-ranked <c>clamp(width / 6, 2, 6)</c> distinct use-multisets each keep one representative.
        /// </summary>
        private void AddPotionLineageRepresentatives(List<int> required, int width)
        {
            if (!_checkPotionQuota)
                return;
            int potionLineageLimit = Math.Clamp(_policyWidth / 6, 2, 6);
            for (int potionIndex = 0; potionIndex < _potionCountOrder.Count; potionIndex++)
            {
                int potionCount = _potionCountOrder[potionIndex];
                if (potionCount == 0)
                    continue;
                _potionLineageGroups.Clear();
                _potionLineageOrder.Clear();
                for (int position = 0; position < Count; position++)
                {
                    int source = _rankOrder[position];
                    if (_facts[source].PotionCount != potionCount)
                        continue;
                    string key = PotionUseLineageKey(_nodes[source]);
                    if (!_potionLineageGroups.TryGetValue(key, out PotionLineageLeaders? leader))
                    {
                        leader = new PotionLineageLeaders { Position = position };
                        _potionLineageGroups.Add(key, leader);
                        _potionLineageOrder.Add(key);
                    }
                    leader.Nodes.Add(source);
                }
                if (_potionLineageOrder.Count == 0)
                    continue;
                foreach (string key in _potionLineageOrder)
                {
                    PotionLineageLeaders leader = _potionLineageGroups[key];
                    double maxRank = double.NegativeInfinity;
                    for (int index = 0; index < leader.Nodes.Count; index++)
                        maxRank = Math.Max(maxRank, _ranks[leader.Nodes[index]]);
                    leader.MaxRank = maxRank;
                }
                CollectionsMarshal.AsSpan(_potionLineageOrder)
                    .Sort(new PotionLineageOrderComparer(_potionLineageGroups));
                for (int index = 0;
                     index < _potionLineageOrder.Count && index < potionLineageLimit; index++)
                {
                    AddRequired(required,
                        FindBestPotionLineage(_potionLineageGroups[_potionLineageOrder[index]].Nodes),
                        width);
                }
            }
        }

        private sealed class PotionLineageLeaders
        {
            public readonly List<int> Nodes = new();
            public int Position;
            public double MaxRank;
        }

        private readonly struct PotionLineageOrderComparer : IComparer<string>
        {
            private readonly Dictionary<string, PotionLineageLeaders> _groups;

            public PotionLineageOrderComparer(Dictionary<string, PotionLineageLeaders> groups)
                => _groups = groups;

            public int Compare(string? left, string? right)
            {
                PotionLineageLeaders a = _groups[left!];
                PotionLineageLeaders b = _groups[right!];
                int comparison = b.MaxRank.CompareTo(a.MaxRank);
                return comparison != 0 ? comparison : a.Position.CompareTo(b.Position);
            }
        }

        /// <summary><c>BeamRetentionPolicy.Ranking.PotionUseLineageKey</c>: the sorted explicit potion
        /// id multiset along the route, ordinal. The route chain is the Flash node chain (prefix actions
        /// are not Flash nodes and were already excluded from the existing search's chain too).</summary>
        private string PotionUseLineageKey(int node)
        {
            _potionLineageScratch.Clear();
            int actions = 0;
            for (int cursor = node; _routeNodes[cursor].Parent >= 0;
                 cursor = _routeNodes[cursor].Parent)
            {
                actions++;
                if (_routeNodes[cursor].Action.Kind == FlashActionKind.UsePotion)
                    _potionLineageScratch.Add(_routeNodes[cursor].PotionId);
            }
            if (actions != _routeNodes[node].Depth - _initialActionCount)
            {
                throw new InvalidOperationException(
                    $"Flash 路线节点动作链长度不一致：actions={actions} "
                    + $"depth={_routeNodes[node].Depth} initial={_initialActionCount}。");
            }
            if (_potionLineageScratch.Count == 0)
                return string.Empty;
            _potionLineageScratch.Sort();
            return string.Join(',', _potionLineageScratch);
        }

        /// <summary><c>BeamRetentionPolicy.Ranking.FindBestPotionLineage</c>.</summary>
        private int FindBestPotionLineage(List<int> nodes)
        {
            int best = -1;
            for (int index = 0; index < nodes.Count; index++)
            {
                int node = nodes[index];
                RetentionFacts facts = _facts[node];
                if (best < 0)
                {
                    best = node;
                    continue;
                }
                RetentionFacts current = _facts[best];
                if (facts.AllEnemiesDead && !current.AllEnemiesDead
                    || facts.AllEnemiesDead == current.AllEnemiesDead
                        && (facts.ProjectedPlayerHp > current.ProjectedPlayerHp
                            || facts.ProjectedPlayerHp == current.ProjectedPlayerHp
                                && (facts.EnemyHp < current.EnemyHp
                                    || facts.EnemyHp == current.EnemyHp
                                        && facts.Score > current.Score)))
                {
                    best = node;
                }
            }
            return best;
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
                // the existing search's CompareBeamRankOrder: rank, then fewer actions, then offensive progress.
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
            {
                int comparison = left.Potion.CompareTo(right.Potion);
                if (comparison != 0)
                    return comparison;
                TraitLeaders a = _leaders[left];
                TraitLeaders b = _leaders[right];
                comparison = b.MaxRank.CompareTo(a.MaxRank);
                return comparison != 0 ? comparison : a.Position.CompareTo(b.Position);
            }
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
                int comparison = left.Potion.CompareTo(right.Potion);
                if (comparison != 0)
                    return comparison;
                comparison = rightLeader.MaxPressure.CompareTo(leftLeader.MaxPressure);
                return comparison != 0
                    ? comparison
                    : leftLeader.Position.CompareTo(rightLeader.Position);
            }
        }

        private readonly struct LineageOrderComparer : IComparer<ulong>
        {
            private readonly Dictionary<ulong, LineageLeaders> _leaders;
            private readonly int[] _rankPosition;

            public LineageOrderComparer(Dictionary<ulong, LineageLeaders> leaders, int[] rankPosition)
            {
                _leaders = leaders;
                _rankPosition = rankPosition;
            }

            public int Compare(ulong left, ulong right)
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
