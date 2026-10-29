using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sts2Solver.Simulation.Combat;
using Sts2Solver.Ai.Combat.Novelty;
using Sts2Solver.Ai.Combat.Policy;
using Sts2Solver.Ai.Combat.Quality;
using Sts2Solver.Ai.Combat.Scoring;

namespace Sts2Solver.Ai.Combat;

internal sealed record BeamOptions(
    int BeamWidth,
    int MaxExpandedNodes,
    int MaxTurns,
    TimeSpan SoftTimeBudget,
    int InitialPlayerMaxHp)
{
    /// <summary>Production searches share time and memory, without a node ceiling.</summary>
    public bool UnlimitedNodes { get; init; }

    /// <summary>End-turn batch trigger and target, in states; zero preserves legacy behavior.</summary>
    public int EndedBatchTrigger { get; init; }
    public int EndedBatchTarget { get; init; }

    /// <summary>Number of final candidate routes retained for offline replay scanning (0 = off).</summary>
    public int CandidateScanCount { get; init; }

    /// <summary>
    /// Hard cap on search-owned logical bytes for this solve, or zero for no cap. The host supplies
    /// one number (the Mod's 15%-of-available default or the offline host's 0.15 x 16 GiB); the request
    /// codec does not encode it, so decoded legacy requests and D2 run unlimited.
    /// </summary>
    public long SearchMemoryBudgetBytes { get; init; }

    /// <summary>
    /// Byte budget of each growable state pool, or zero for the built-in 200 MiB. The multi-round
    /// widening search raises it with the beam width; the request codec does not encode it, so
    /// decoded legacy requests, direct kernel calls and D2 keep the old single-pool bound.
    /// </summary>
    public int PoolStateBudgetBytes { get; init; }

    /// <summary>Which root potions may be used and what each use costs; disabled unless the caller sets it.</summary>
    public PotionPolicy Potions { get; init; } = PotionPolicy.Disabled;

    /// <summary>
    /// The ported existing-engine evaluation. Set by <c>RouteMaterializer</c> from
    /// <c>ScoreBridge</c>; a caller that supplies it is responsible for the same root.
    /// </summary>
    public Scorer? Scorer { get; init; }

    /// <summary>Root values the final route ordering reads; set together with <see cref="Scorer"/>.</summary>
    public RouteOrdering? Ordering { get; init; }

    /// <summary>The acceptable battle HP-loss stop; null when the request cannot stop at an HP target.</summary>
    public HpTargetStop? HpTargetStop { get; init; }

    /// <summary>
    /// The ported incumbent lower bound; null or disabled means the root never prunes. Set together with
    /// <see cref="Incumbent"/> by <c>RouteMaterializer</c>.
    /// </summary>
    public BoundFeatures? Bound { get; init; }

    /// <summary>The request's initial incumbent, already translated to Sts2Solver player-turn units.</summary>
    public SearchIncumbent? Incumbent { get; init; }

    /// <summary>The existing primary member's effective potion policy, used to qualify incumbent tightening.</summary>
    public PotionUseMode EffectivePotionPolicy { get; init; } = PotionUseMode.Smart;

    /// <summary>The request's theft policy; <c>PreserveResources</c> adds the existing final ordering's recovery keys.</summary>
    public TheftPolicy? TheftPolicy { get; init; }

    /// <summary>
    /// The request's forced potion directives, as canonical Sts2Solver potion ids. Only a request without a
    /// potion-policy override enforces them, matching <c>the existing search's EnforcesPotionDirectives</c>.
    /// </summary>
    public ForcedPotionDirectives ForcedPotionDirectives { get; init; } = ForcedPotionDirectives.None;

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
    public PlanCommitment? PlanCommitment { get; init; }

    /// <summary>
    /// Payoff evidence already realized by the fixed prefix. The existing search starts its node chain
    /// at the request root, so its <c>CountRealizedPayoffs</c> includes prefix plays; Beam starts at
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
    public NoveltyOptions? Novelty { get; init; }

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
    /// <c>the existing search's SecondRankBand</c>: the global prune's ordinary seats come from score
    /// ranks W+1..2W instead of the top W. Required representatives, power-commitment seats and plan
    /// seats are unchanged. Only the members that explicitly set the profile switch read it.
    /// </summary>
    public bool SecondRankBand { get; init; }

    /// <summary>
    /// The existing profile's band width (<c>effectiveLimit</c> in <c>RankBest</c>). Zero means the Sts2Solver
    /// layer width. The bridge freezes the request profile's width so the W+1..2W band uses the same
    /// width as the regular search.
    /// </summary>
    public int SecondRankBandWidth { get; init; }

    /// <summary>
    /// <c>the existing search's AggressivePowerCommitment</c>: raise the power-commitment seat quota to
    /// half the beam and extend a commitment's lifetime from two round transitions to three. The
    /// per-node commitment metadata is route history, not <see cref="Battle"/> state.
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
    public PowerCommitmentCatalog? PowerCommitments { get; init; }

    /// <summary>
    /// The shared frame-pressure pacer. The bridge creates it from
    /// <see cref="SearchPolicySnapshot.FramePressureSignal"/> so the Sts2Solver layer and expansion loops
    /// yield exactly like the existing search's replay checkpoints. Null in direct kernel tests.
    /// </summary>
    public SearchWorkPacer? Pacer { get; init; }

    /// <summary>
    /// Progress sink. The bridge translates one snapshot into an existing <c>SolverProgress</c> and
    /// materializes the route preview lazily; the solver only copies route metadata.
    /// </summary>
    public Action<ProgressSnapshot>? Progress { get; init; }

    /// <summary>
    /// Polls the pending takeover request. The bridge owns the <c>SearchInteractionState</c> mapping;
    /// the solver only sees the three-way decision.
    /// </summary>
    public Func<TakeoverAction>? PollTakeover { get; init; }

    /// <summary>
    /// Offline route trace. Null in production and in every request that does not ask for a trace;
    /// the solver only reports the points it already evaluates.
    /// </summary>
    public BeamTrace? Trace { get; init; }
}

/// <summary>One registered power card's existing commitment descriptor.</summary>
internal readonly record struct PowerCommitmentRule(
    PowerCommitmentFamily Family,
    PowerRoutePriority Priority,
    PowerRouteAdmissionPolicy Admission,
    int Index);

/// <summary>
/// The root-frozen power-commitment catalog. Indexed by the kernel's card id; entries with
/// <see cref="PowerCommitmentFamily.None"/> are not registered as commitments.
/// </summary>
internal sealed record PowerCommitmentCatalog(
    PowerCommitmentRule[] Rules,
    int TotalFloor);

/// <summary>The payoff evidence kinds Beam ports from <see cref="PlanCommitment"/>.</summary>
internal enum PlanPayoffKind : byte
{
    CardPlayed,
    FreePotionUsed,
    RegisteredPowerBenefit,
}

/// <summary>
/// A <see cref="PlanCommitment"/> reduced to the values Beam reads: the CopyPower representative
/// seats and the per-route payoff count. The card is a Sts2Solver content id resolved by the bridge.
/// </summary>
internal sealed record PlanCommitment(
    bool CopyPowerSeats,
    int OpenedTurn,
    int EarliestTurn,
    PlanPayoffKind PayoffKind,
    ushort PayoffCardId,
    ushort PayoffPotionId = ushort.MaxValue,
    int PayoffCardBit = -1);

/// <summary>One early-turn scout candidate: the route from the request root and its existing score.</summary>
internal sealed record ScoutCandidate(PlayerAction[] Actions, double Score)
{
    /// <summary>Index of the turn-setup root the candidate started from (0 for an ordinary root).</summary>
    public int RootIndex { get; init; }
}

/// <summary>The candidates the existing search's observer reports after one searched turn layer.</summary>
internal sealed record ScoutLayer(int Turns, IReadOnlyList<ScoutCandidate> Candidates);

/// <summary>The request's HP-target stop (<c>the existing search's MeetsHpTarget</c>): a complete victory
/// without death saves, with at least <paramref name="RequiredPotionUses"/> and at most
/// <paramref name="EarlyStopPotionUses"/> explicit potion uses, satisfying the enforced forced
/// directives, and with <paramref name="HpLostSoFar"/> plus its cumulative HP loss within
/// <paramref name="AcceptableBattleHpLoss"/>, ends the search before the next parent.
/// </summary>
internal sealed record HpTargetStop(
    int AcceptableBattleHpLoss,
    int HpLostSoFar,
    int EarlyStopPotionUses,
    int RequiredPotionUses = 0,
    bool EnforceForcedPotionDirectives = false);

/// <summary>The root values <c>FinalPlanOrdering.Select</c> reads for the route keys Beam ports.</summary>
internal sealed record RouteOrdering(
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
internal sealed record PotionPolicy(
    int EarliestPlayerTurn,
    int? MaximumUses,
    PotionStrategySnapshot Strategy,
    PotionUseMode EffectivePolicy,
    bool ForceAllDisabled)
{
    public static PotionPolicy Disabled { get; } = new(
        int.MaxValue, 0,
        new PotionStrategySnapshot(PotionUseMode.Disabled, Array.Empty<PotionSlotDirective>()),
        PotionUseMode.Disabled, false);

    /// <summary><c>PotionStrategySnapshot.AllowsExplicitUse</c> for the potion now in the slot.</summary>
    public bool AllowsExplicitUse(int slot, ushort potionId)
    {
        PotionDef definition = Registry.Potion(potionId);
        return definition.Searchable
            && Strategy.AllowsExplicitUse(slot, definition.NativeId, EffectivePolicy, ForceAllDisabled);
    }
}

/// <summary>
/// The request's forced potion directives (<c>PotionStrategySnapshot.Directives</c> with
/// <see cref="PotionDirective.Force"/>), resolved to canonical Sts2Solver potion ids. A directive is
/// satisfied by one explicit use of that potion identity in its slot; the fixed prefix's uses are
/// folded into <paramref name="AlreadyUsedMask"/> because they are not Sts2Solver nodes.
/// </summary>
internal sealed record ForcedPotionDirectives(
    int[] Slots,
    ushort[] PotionIds,
    int[] StrategicHpCosts,
    int AlreadyUsedMask,
    int AlreadyUsedStrategicHpCost)
{
    public static ForcedPotionDirectives None { get; } = new([], [], [], 0, 0);

    public int Count => Slots.Length;
}

internal sealed record BeamResult(
    PlayerAction[] BestRoute,
    IReadOnlyList<PlayerAction[]> TopRoutes,
    bool BestIsVictory,
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

    /// <summary>Children dropped because their state exceeded a Sts2Solver capacity (a search boundary).</summary>
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

    /// <summary>Search-owned logical bytes charged to the reusable storage at the end of the solve.</summary>
    public long SearchMemoryUsedBytes { get; init; }

    /// <summary>Highest search-owned logical byte count observed during the solve.</summary>
    public long SearchMemoryPeakBytes { get; init; }

    /// <summary>Configured search-memory limit, or zero when unlimited.</summary>
    public long SearchMemoryLimitBytes { get; init; }

    /// <summary>True when an allocation was refused because it would cross the memory limit.</summary>
    public bool SearchMemoryBudgetExceeded { get; init; }

    /// <summary>Charged bytes at the first refused allocation; zero when not exceeded.</summary>
    public long SearchMemoryExceededAtBytes { get; init; }

    /// <summary>
    /// The solve stopped because its soft-time budget was reached (only possible for a limited
    /// solve). The widening loop uses this to mark the round as truncated by the in-game time
    /// insurance. Not encoded in SRES.
    /// </summary>
    public bool SoftTimeBudgetReached { get; init; }

    /// <summary>Bytes occupied by the route-node chunks (the largest search-owned growing store).</summary>
    public long RouteNodeStorageBytes { get; init; }

    /// <summary>Route nodes still retained after the last boundary cleanup.</summary>
    public int RouteNodeCount { get; init; }

    /// <summary>Highest route-node count observed during the solve, before boundary cleanup.</summary>
    public int RouteNodePeakCount { get; init; }

    /// <summary>
    /// The selected route's plan payoff evidence count, including
    /// <see cref="BeamOptions.InitialPlanPayoffCount"/>. Zero when the request has no plan
    /// commitment or none was realized.
    /// </summary>
    public int BestPayoffCount { get; init; }

    /// <summary>The scout layers reported by an early-turn scout request, in searched order.</summary>
    public IReadOnlyList<ScoutLayer> ScoutLayers { get; init; } = [];

    /// <summary>The novelty statistics of a <see cref="BeamOptions.Novelty"/> request.</summary>
    public NoveltySearchTelemetry? NoveltyTelemetry { get; init; }

    /// <summary>
    /// Which root of the solve the selected route started from: 0 for an ordinary root, or the
    /// turn-setup root candidate index (the bridge maps it back to the setup choices).
    /// </summary>
    public int BestRootIndex { get; init; }

    /// <summary>
    /// False when an audited request found no route satisfying the ported potion policy. The dispatch
    /// point reports it in the existing search's <c>PotionPolicyUnsatisfiedException</c> shape;
    /// "Sts2Solver found none" is not the existing search's own verdict.
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
    /// End-turn actions that reused a prepared player-turn-start prefix. Diagnostics only; not part
    /// of the result encoding or the D2 field comparison.
    /// </summary>
    public int EndTurnPrefixShared { get; init; }

    /// <summary>
    /// End-turn actions whose prefix was not shareable (a choice or ending in the first half, or an
    /// extra turn); those actions ran the ordinary complete step. Diagnostics only.
    /// </summary>
    public int EndTurnPrefixUnshared { get; init; }

    /// <summary>
    /// Selection leaves checked against the complete-step path by the default-off equivalence hook.
    /// Diagnostics only.
    /// </summary>
    public int EndTurnPrefixVerified { get; init; }

    /// <summary>
    /// In-place threat projections whose before/after battle bytes were compared by the default-off
    /// equivalence hook. Diagnostics only.
    /// </summary>
    public int ThreatProjectionVerified { get; init; }

    /// <summary>Selection leaves that continued a prepared prefix (at least one per shared parent).</summary>
    public int EndTurnPrefixReusedLeaves { get; init; }

    /// <summary>Unshared prefixes whose first half requested a choice before the player turn start.</summary>
    public int EndTurnPrefixChoiceUnshared { get; init; }

    /// <summary>Unshared prefixes for an extra turn.</summary>
    public int EndTurnPrefixExtraTurnUnshared { get; init; }

    /// <summary>Unshared prefixes because the parent was already over.</summary>
    public int EndTurnPrefixNotInProgress { get; init; }

    /// <summary>
    /// Set when the search stopped at a takeover checkpoint instead of running to its own stop
    /// condition. <see cref="BestRoute"/> then identifies the adopted current-turn route (or is empty
    /// for <see cref="TakeoverAction.AdoptRoute"/>, where the bridge materializes the requested
    /// seed itself).
    /// </summary>
    public TakeoverAction Takeover { get; init; }

    /// <summary>
    /// Counts of the two content-deduplications and, when the verification switch is on, of the
    /// skipped-alternative comparisons. Diagnostics only; not part of the result encoding.
    /// </summary>
    public SearchEquivalenceDiagnostics Equivalence { get; init; } = SearchEquivalenceDiagnostics.Empty;
}

/// <summary>
/// Turn-layered beam search over <see cref="Battle"/>. Within a turn it expands card plays
/// depth by depth; end-turn children seed the next turn layer. Children are deduplicated by full
/// byte equality and the best score is kept. Only <see cref="Battle"/> and
/// <see cref="Rules"/> are touched; translation and materialization happen in the bridge.
/// </summary>
[System.Runtime.CompilerServices.SkipLocalsInit]
internal sealed partial class BeamSolver
{
    /// <summary>Selections explored per choice request (lexicographic within each size).</summary>
    private const int MaxChoiceBranches = 32;

    /// <summary>
    /// Cap on skipped physical choice alternatives checked per request by the verification switch.
    /// The equality statistics are a bounded sample; the search itself is unaffected.
    /// </summary>
    private const int MaxChoiceAlternativesVerifiedPerRequest = 8;

    /// <summary>The existing <c>EarlyTurnCandidatesPerLayer</c>.</summary>
    private const int EarlyTurnCandidatesPerLayer = 24;

    /// <summary>
    /// When the cumulative end-turn pool reaches its capacity it is compacted to this many beam
    /// widths and keeps accumulating. The bound is the same <c>CopyTopTo</c> retention rule used at
    /// the turn boundary (rank leaders and every required representative), so a
    /// compacted pool still contains every state that can win a turn-boundary seat; only a root whose
    /// end-turn pool actually reaches the capacity can observe the compaction.
    /// </summary>
    private const int EndedCompactionWidthFactor = 8;

    /// <summary>
    /// Byte budget of each of the two growable pools (<c>next</c> and <c>ended</c>). The state count
    /// is <c>PoolStateBudgetBytes / sizeof(Battle)</c>, rounded down to the 64-state chunk so the
    /// allocated chunked arrays do not overshoot the budget. The effective capacity is the smaller of
    /// this count and the original <c>width * (MaxLegalActions + 1)</c> bound. Without it a pathological
    /// turn (REGENT-V-06: 434 action layers in one turn) retains hundreds of megabytes per pool in the
    /// game process; 200 MiB per pool keeps the search well below the old ~0.8-1.0 GB single-pool peak.
    /// </summary>
    private const int DefaultPoolStateBudgetBytes = 200 * 1024 * 1024;

    /// <summary>Dictionary/bucket allowance charged once per unique routing signature.</summary>
    private const int RoutingSignatureDictionaryBytes = 64;

    private readonly Rules _rules;
    private readonly BeamOptions _options;
    private readonly Scorer _scorer;
    private readonly RouteOrdering _ordering;
    private RouteNodeStore _nodes = null!;
    private RoutingMetaStore _routingMetas = null!;
    private SearchStorage _storage = null!;
    private int _nodeCleanupThreshold = 1_000_000;
    private readonly List<int> _compactRoots = [];
    private readonly List<int> _auditKeysScratch = [];

    /// <summary>
    /// Routing-choice signatures accumulated on the route. They are node metadata only; no value here
    /// enters <see cref="Battle"/>, the fingerprint or the state key.
    /// </summary>
    private readonly record struct RoutingSignature(
        int Turn,
        int SourceId,
        ChoiceEffect Effect,
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

    private ChunkedList<RoutingSignature> _routingSignatures = null!;
    private readonly Dictionary<RoutingSignature, int> _routingSignatureIds = new();

    /// <summary>
    /// One route node's accumulated metadata. <see cref="SearchRouteTraits"/> and the potion-use
    /// lineage are route history: they never enter <see cref="Battle"/>, the fingerprint or the
    /// state key.
    /// </summary>
    private readonly record struct RouteNode(
        int Parent,
        int Depth,
        PlayerAction Action,
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
        int ParentTurn,
        int RoutingMetaId);

    /// <summary>
    /// Route-node power-commitment metadata (1-based node field; 0 means no commitment). The existing
    /// search holds <c>PowerCommitment</c> on <c>SearchNode</c>, not in the simulated state, so this list
    /// never enters <see cref="Battle"/>, the fingerprint or the state key.
    /// </summary>
    private ChunkedList<PowerCommitmentState> _commitments = null!;
    private readonly PowerCommitmentCatalog? _powerCommitments;
    private readonly bool _aggressivePowerCommitment;
    private readonly bool _preserveReplayAllocatorOpening;
    private readonly PlayerAction[] _actions = new PlayerAction[Kernel.MaxLegalActions];
    private readonly ushort[] _rootPotions = new ushort[Limits.PotionSlots];
    private readonly BoundFeatures? _bound;
    private readonly bool _pruneEqualHp;
    private readonly bool _preservePotionCostOpportunities;
    private readonly PotionUseMode _effectivePotionPolicy;
    private readonly TheftPolicy? _theftPolicy;
    private readonly bool _auditPotionPolicy;
    private readonly ForcedPotionDirectives _forcedPotionDirectives;
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
    private SearchIncumbent? _incumbent;
    private int _expanded;
    private int _transitions;
    private int _duplicates;
    private bool _memoryBudgetExceeded;
    private long _memoryBudgetExceededAtBytes;
    private int _capacityPruned;
    private int _pruned;
    private int _smartPruned;
    private int _endedCompactions;
    private int _endedCompressedStates;
    private int _endedCompactionTarget;
    private readonly Dictionary<string, int> _capacityDropped = new(StringComparer.Ordinal);

    // Shared end-turn prefix. A prefix is keyed by the expansion generation (one per expanded parent,
    // also advanced when a fetched-power successor batch starts) and by the parent route node, so a
    // recursive selection expansion of the same parent reuses it while any other parent cannot.
    private int _expandGeneration;
    private int _endTurnPrefixAttemptGeneration = -1;
    private int _endTurnPrefixAttemptNode = -1;
    private int _endTurnPrefixGeneration = -1;
    private int _endTurnPrefixNode = -1;
    private bool _endTurnPrefixValid;
    private int _endTurnPrefixShared;
    private int _endTurnPrefixUnshared;
    private int _endTurnPrefixVerified;
    private int _endTurnPrefixReusedLeaves;
    private int _endTurnPrefixChoiceUnshared;
    private int _endTurnPrefixExtraTurnUnshared;
    private int _endTurnPrefixNotInProgress;
    private bool _verifySearchEquivalence;
    private bool _dedupePlayActions;
    private bool _dedupeChoicesByClass;
    private bool _choiceFillVariants;
    private long _playDeduped;
    private long _playVerifiedExact;
    private long _playVerifiedApprox;
    private long _playVerifiedApproxEqual;
    private long _playVerifiedUnresolved;
    private long _choiceByClassRequests;
    private long _choiceVerifiedExact;
    private long _choiceVerifiedApprox;
    private long _choiceVerifiedApproxEqual;
    private long _choiceVerifiedUnresolved;
    private EndTurnPrefix _endTurnPrefix;

    /// <summary>Offline route trace (null in production) and the pool phase it reports.</summary>
    private readonly BeamTrace? _trace;
    private string _tracePhase = "next";
    private bool _singleRoot;

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
    private readonly PlayerAction[] _commitmentActions = new PlayerAction[Kernel.MaxLegalActions];

    private readonly PlanCommitment? _planCommitment;
    private readonly int _earlyTurnScoutDepth;
    private readonly int _initialPlayerTurn;
    private readonly int _initialSearchTurn;
    private readonly List<ScoutLayer> _scoutLayers = [];

    private Stopwatch? _solveWatch;

    /// <summary>The physical expanded-parent count of the current solve (readable after a canceled solve).</summary>
    internal int ExpandedNodes => _expanded;

    /// <summary>The physical transition count of the current solve (readable after a canceled solve).</summary>
    internal int TransitionCount => _transitions;

    public BeamSolver(Rules rules, BeamOptions options)
    {
        _rules = rules;
        _options = options;
        _scorer = options.Scorer
            ?? throw new InvalidOperationException("BeamOptions.Scorer 未设置。");
        _ordering = options.Ordering
            ?? throw new InvalidOperationException("BeamOptions.Ordering 未设置。");
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
        _trace = options.Trace;
        if (_smartPotionEligibilityHpCeiling != null && _bound == null)
            throw new InvalidOperationException("Smart 药水资格上界需要启用下界输入。");
        _incumbent = _bound == null ? null : options.Incumbent;
    }

    /// <summary>
    /// Solves from one or more starting states. A turn-setup request hands over every enumerable setup
    /// root (the existing search's frontier does the same); the returned <see cref="BeamResult.BestRootIndex"/>
    /// identifies which one the selected route started from.
    /// </summary>
    public BeamResult Solve(IReadOnlyList<Battle> roots, CancellationToken cancellationToken)
        => Solve(roots, new SearchStorage(), cancellationToken);

    /// <summary>
    /// Solves with a caller-owned reusable storage. The storage is cleared first and its retained
    /// chunks are rented again before new blocks are allocated; the ledger keeps counting the retained
    /// blocks, so repeated rounds share one memory budget.
    /// </summary>
    public BeamResult Solve(IReadOnlyList<Battle> roots, SearchStorage storage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (roots.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(roots), "至少需要一个搜索根。");
        _storage = storage;
        _storage.BeginSolve(_options.SearchMemoryBudgetBytes);
        _routingMetas = new RoutingMetaStore(storage);
        _routingMetas.Clear();
        _nodes = new RouteNodeStore(storage, _routingMetas);
        _routingSignatures = new ChunkedList<RoutingSignature>(storage);
        _commitments = new ChunkedList<PowerCommitmentState>(storage);
        _nodeCleanupThreshold = NodeCleanupThreshold(_options.SearchMemoryBudgetBytes);
        _singleRoot = roots.Count == 1;
        _verifySearchEquivalence = SearchVerificationHooks.VerifySearchEquivalence;
        Kernel.VerifyStableBoundaryInvariants = _verifySearchEquivalence;
        _dedupePlayActions = SearchVerificationHooks.DedupeExactPlayActions;
        _dedupeChoicesByClass = SearchVerificationHooks.DedupeEquivalentChoicesByClass;
        _choiceFillVariants = SearchVerificationHooks.ChoiceFillPhysicalVariants;
        _playDeduped = 0;
        _playVerifiedExact = 0;
        _playVerifiedApprox = 0;
        _playVerifiedApproxEqual = 0;
        _playVerifiedUnresolved = 0;
        _choiceByClassRequests = 0;
        _choiceVerifiedExact = 0;
        _choiceVerifiedApprox = 0;
        _choiceVerifiedApproxEqual = 0;
        _choiceVerifiedUnresolved = 0;
        _scorer.VerifyThreatProjection = _verifySearchEquivalence;
        Stopwatch watch = Stopwatch.StartNew();
        _cancellationToken = cancellationToken;
        _solveWatch = watch;
        ResetProgressState();
        _expanded = 0;
        _transitions = 0;
        _duplicates = 0;
        _memoryBudgetExceeded = false;
        _memoryBudgetExceededAtBytes = 0;
        _bestPotionFree = null;
        _auditBestByPotionCount = null;
        _anyAuditedVictory = false;
        _endTurnPrefixNode = -1;
        _endTurnPrefixAttemptNode = -1;
        _endTurnPrefixGeneration = -1;
        _endTurnPrefixAttemptGeneration = -1;
        _endTurnPrefixValid = false;
        _endTurnPrefixShared = 0;
        _endTurnPrefixUnshared = 0;
        _endTurnPrefixVerified = 0;
        _endTurnPrefixReusedLeaves = 0;
        _endTurnPrefixChoiceUnshared = 0;
        _endTurnPrefixExtraTurnUnshared = 0;
        _endTurnPrefixNotInProgress = 0;
        _scoutLayers.Clear();
        if (_options.Novelty is { } novelty)
            return SolveNovelty(roots, novelty, watch, cancellationToken);
        _hpTargetReached = false;
        for (int slot = 0; slot < Limits.PotionSlots; slot++)
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
        int maxChildren = checked(frontierCapacity * (Kernel.MaxLegalActions + 1));
        // Both growable pools use the same byte budget; the original per-action-layer bound stays as
        // the semantic upper bound for small widths. The state count is rounded down to the 64-state
        // chunk so the chunked arrays stay inside the budget.
        int chunkStates = ChunkedArray<Battle>.ChunkSize;
        int poolStateBudgetBytes = EffectivePoolStateBudgetBytes;
        int budgetStates = poolStateBudgetBytes / Unsafe.SizeOf<Battle>() / chunkStates * chunkStates;
        int poolLimit = Math.Min(maxChildren, Math.Max(chunkStates, budgetStates));
        int endedLimit = _options.EndedBatchTrigger > 0
            ? Math.Min(poolLimit, Math.Max(width + 1, _options.EndedBatchTrigger)) : poolLimit;
        int initialChildren = Math.Min(poolLimit, Math.Max(64, width * 8));
        // The compaction target is measured in beam widths: it always keeps more than the turn
        // boundary consumes (width) plus the same retention lanes, and never exceeds the new pool
        // limit. Reaching the limit is the only trigger, so a root below it keeps the previous
        // behavior bit for bit.
        _endedCompactionTarget = _options.EndedBatchTarget > 0
            ? Math.Clamp(_options.EndedBatchTarget, width, endedLimit - 1)
            : Math.Min(poolLimit, Math.Max(width, width * EndedCompactionWidthFactor));
        _endedCompactions = 0;
        _endedCompressedStates = 0;
        _capacityDropped.Clear();
        bool preserveTheft = _theftPolicy == TheftPolicy.PreserveResources;
        int bandWidth = _options.SecondRankBandWidth > 0
            ? _options.SecondRankBandWidth : width;
        bool checkPotionQuota = _effectivePotionPolicy != PotionUseMode.Disabled;
        Pool current = new(frontierCapacity, frontierCapacity, preserveTheft, _scorer, _planCommitment,
            _options.SecondRankBand, bandWidth, _aggressivePowerCommitment, _powerCommitments, _commitments,
            _nodes, _routingSignatures, checkPotionQuota, _options.InitialActionCount,
            _options.PreserveReplayAllocatorOpening, _options.Trace, _storage,
            lazyScratch: _options.EndedBatchTrigger > 0);
        Pool next = new(initialChildren, poolLimit, preserveTheft, _scorer, _planCommitment,
            _options.SecondRankBand, bandWidth, _aggressivePowerCommitment, _powerCommitments, _commitments,
            _nodes, _routingSignatures, checkPotionQuota, _options.InitialActionCount,
            _options.PreserveReplayAllocatorOpening, _options.Trace, _storage,
            lazyScratch: _options.EndedBatchTrigger > 0);
        Pool ended = new(Math.Min(initialChildren, endedLimit), endedLimit, preserveTheft, _scorer, _planCommitment,
            _options.SecondRankBand, bandWidth, _aggressivePowerCommitment, _powerCommitments, _commitments,
            _nodes, _routingSignatures, checkPotionQuota, _options.InitialActionCount,
            _options.PreserveReplayAllocatorOpening, _options.Trace, _storage,
            lazyScratch: _options.EndedBatchTrigger > 0);
        Pool turnFrontier = new(frontierCapacity, frontierCapacity, preserveTheft, _scorer, _planCommitment,
            _options.SecondRankBand, bandWidth, _aggressivePowerCommitment, _powerCommitments, _commitments,
            _nodes, _routingSignatures, checkPotionQuota, _options.InitialActionCount,
            _options.PreserveReplayAllocatorOpening, _options.Trace, _storage,
            lazyScratch: _options.EndedBatchTrigger > 0);

        RouteKey? best = null;
        int bestNode = 0;
        bool bestVictory = false;
        List<(RouteKey Key, int Node)> candidateRoutes = [];
        void Consider(in Battle state, int node, in RouteKey key,
            in ScoreFeatures features)
        {
            if (best is { } current && CompareRoutes(key, current) >= 0)
                return;
            best = key;
            bestNode = node;
            bestVictory = state.Outcome == Outcome.Victory;
            _bestFeatures = features;
            _bestFeaturesNode = node;
        }
        // The existing search's two early stops. The HP-target stop ends the search before the next parent
        // (the existing search's Phases.cs, MeetsHpTarget); the zero-damage stop ends it after the current turn
        // once a victory lost no HP, used no explicit potion and left the player at full, undiminished HP
        // (growth targets, which disable it there, are rejected at admission).
        bool hpTargetReached = false, zeroDamageVictory = false;
        void ConsiderCandidate(in Battle state, int node, in ScoreFeatures features, in RouteKey key)
        {
            if (_options.CandidateScanCount > 0)
            {
                candidateRoutes.Add((key, node));
                _storage.Memory.Charge(32, "candidate-routes");
            }
            if (_auditPotionPolicy)
                ConsiderAudited(node, features, key);
            else
                Consider(state, node, key, features);
            TightenIncumbent(state, node, features, key);
        }
        void ConsiderTerminal(in Battle state, int node, double score)
        {
            int actionCount = Depth(node);
            ScoreFeatures features = _scorer.Features(state, actionCount, includeRetentionOnly: false);
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

        _routingSignatureIds.Clear();
        for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            Battle root = roots[rootIndex];
            (double rootScore, double rootRank, double rootPureRank, int rootPotionCost,
                ScoreFeatures rootFeatures) =
                Score(root, _options.InitialActionCount, terminal: false, isEndTurn: false);
            _nodes.Add(new RouteNode(-1, Math.Max(0, _options.InitialActionCount), default, 0, 0,
                _options.InitialPlanPayoffCount, ushort.MaxValue, ushort.MaxValue, int.MinValue, 0,
                0, int.MinValue, 0, 0, 0, int.MaxValue, 0d, 0, 0, 0), 0);
            (int rootReachable, int rootZeroCost, _) = _scorer.ReachableHandPotential(root);
            PocketwatchState(root, out int rootPocketwatchThreshold, out int rootPocketwatchPlayed,
                out int rootPocketwatchLastTurn);
            RetentionFacts rootFacts = RetentionFacts.From(
                rootFeatures, _options.InitialActionCount, rootReachable, rootZeroCost,
                SearchRouteTraits.None, rootPocketwatchThreshold, rootPocketwatchPlayed,
                rootPocketwatchLastTurn, _scorer.InitialRetainedAttackValue,
                retention: _scorer.CaptureRetentionValues(root));
            turnFrontier.Add(root, rootIndex, rootScore, rootRank, rootPureRank, rootPotionCost, rootFacts,
                0, _options.InitialPlanPayoffCount, 0);
        }
        TerminalSink terminal = ConsiderTerminal;
        PublishProgress(0, 0, turnFrontier.Count, 0, "初始化", force: true, routeNode: -1);
        int turns = 0;
        try
        {
        while (turnFrontier.Count > 0 && turns < _options.MaxTurns && !OutOfBudget(watch)
            && !hpTargetReached && _takeoverAction == TakeoverAction.None
            && (_earlyTurnScoutDepth == 0 || turns < _earlyTurnScoutDepth))
        {
            SearchCheckpoint();
            if (TryHandleTakeover(turnFrontier))
                break;
            turns++;
            // The previous turn's end-turn pool is no longer a route consumer; clear it before the
            // boundary mark-sweep so only states that can still win a seat are retained.
            ended.Clear();
            next.Clear();
            CompactRouteNodes(candidateRoutes, ref bestNode, current, next, ended, turnFrontier);
            current.Clear();
            _tracePhase = "turn-frontier";
            turnFrontier.TracePhase = _tracePhase;
            turnFrontier.CopyTopTo(current, turns == 1 ? frontierCapacity : width);
            // Turn one is the existing search's searchedTurnLayers == 0.
            _expandFetchedPowers = _options.Act3BossStrategy && turns == 1;
            PublishProgressWithPreview(turns - 1, 0, current.Count, 0, "展开回合", force: true,
                best is null ? -1 : bestNode, best, current);
            int playDepth = 0;
            while (current.Count > 0 && !OutOfBudget(watch) && !hpTargetReached
                && _takeoverAction == TakeoverAction.None)
            {
                SearchCheckpoint();
                if (TryHandleTakeover(current))
                    break;
                next.Clear();
                if (_nodes.Count >= _nodeCleanupThreshold)
                {
                    CompactRouteNodes(candidateRoutes, ref bestNode, current, next, ended, turnFrontier);
                    _nodeCleanupThreshold = Math.Max(_nodeCleanupThreshold, _nodes.Count * 2);
                }
                for (int index = 0; index < current.Count && !OutOfBudget(watch) && !hpTargetReached; index++)
                {
                    SearchCheckpoint();
                    if (_expanded < int.MaxValue) _expanded++;
                    _expandGeneration++;
                    ref Battle parent = ref current.State(index);
                    int parentNode = current.Node(index);
                    int count = Kernel.LegalActions(parent, _rules, _actions);
                    scoped ReadOnlySpan<PlayerAction> legal = _actions.AsSpan(0, count);
                    for (int actionIndex = 0; actionIndex < count; actionIndex++)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        ref readonly PlayerAction action = ref legal[actionIndex];
                        if (action.Kind == PlayerActionKind.UsePotion && !PotionAllowed(parent, action.PotionSlot))
                            continue;
                        if (_dedupePlayActions
                            && TryFindEarlierEquivalentPlay(in parent, legal, actionIndex,
                                out int representative, out bool exact)
                            && exact)
                        {
                            _playDeduped++;
                            if (_verifySearchEquivalence)
                            {
                                VerifySkippedPlay(in parent, legal[representative], action, exact);
                            }
                            continue;
                        }
                        ExpandAction(ref parent, parentNode, action, next, ended, terminal,
                            current.Facts(index), current.RankPosition(index), current.Rank(index));
                    }
                    PublishProgressWithPreview(turns - 1, playDepth,
                        Math.Max(0, current.Count - index - 1) + next.Count, ended.Count,
                        "展开出牌序列", force: false,
                        best is null ? -1 : bestNode, best, current);
                }
                current.Clear();
                _tracePhase = "next";
                next.TracePhase = _tracePhase;
                next.CopyTopTo(current, width);
                PrunePool(current);
                PublishProgressWithPreview(turns - 1, playDepth, current.Count, ended.Count,
                    "剪枝候选", force: true,
                    best is null ? -1 : bestNode, best, current);
                playDepth++;
            }
            if (_takeoverAction != TakeoverAction.None)
                break;
            if (_earlyTurnScoutDepth > 0 && turns <= _earlyTurnScoutDepth)
                CollectEarlyTurnScout(ended, turns, roots);
            turnFrontier.Clear();
            // BeamRetentionPolicy.RankBest at the end-turn frontier: the turn-boundary hand leader is
            // a required representative of every potion group.
            _tracePhase = "turn-boundary";
            ended.TracePhase = _tracePhase;
            ended.CopyTopTo(turnFrontier, width, turnBoundary: true);
            PrunePool(turnFrontier);
            PublishProgressWithPreview(turns, 0, turnFrontier.Count, ended.Count, "回合层完成",
                force: true, best is null ? -1 : bestNode, best, turnFrontier);
            if (TryHandleTakeover(turnFrontier))
                break;
            if (zeroDamageVictory)
                break;
        }
        }
        catch (SearchMemoryBudgetException error)
        {
            _memoryBudgetExceeded = true;
            _memoryBudgetExceededAtBytes = error.UsedBytes;
        }
        PublishProgressWithPreview(turns, 0, turnFrontier.Count, ended.Count, "复核最终候选",
            force: true, best is null ? -1 : bestNode, best, turnFrontier);
        if (_takeoverAction != TakeoverAction.None)
            return TakeoverResult(turns);

        PlayerAction[][] TopCandidateRoutes()
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
                ref Battle state = ref turnFrontier.State(index);
                ScoreFeatures features = _scorer.Features(state, actionCount, includeRetentionOnly: false);
                ConsiderCandidate(state, node, features, Key(state, features, actionCount));
            }
            if (SelectAudited(out int auditedNode, out RouteKey auditedKey, out bool auditedVictory))
            {
                best = auditedKey;
                bestNode = auditedNode;
                bestVictory = auditedVictory;
            }
            _trace?.Finish(TraceFinishReason(hpTargetReached, zeroDamageVictory, watch, turns),
                turns, _expanded);
            BeamResult audited = new(_auditPotionPolicy && best is null ? [] : Route(bestNode),
                TopCandidateRoutes(), bestVictory, best?.Score ?? double.NegativeInfinity,
                _expanded, _transitions, _duplicates, turns, _pruned, watch.Elapsed)
            {
                CapacityPruned = _capacityPruned,
                EndTurnPrefixShared = _endTurnPrefixShared,
                EndTurnPrefixUnshared = _endTurnPrefixUnshared,
                EndTurnPrefixVerified = _endTurnPrefixVerified,
                ThreatProjectionVerified = _scorer.ThreatProjectionChecks,
                EndTurnPrefixReusedLeaves = _endTurnPrefixReusedLeaves,
                EndTurnPrefixChoiceUnshared = _endTurnPrefixChoiceUnshared,
                EndTurnPrefixExtraTurnUnshared = _endTurnPrefixExtraTurnUnshared,
                EndTurnPrefixNotInProgress = _endTurnPrefixNotInProgress,
                CapacityDroppedChildren = _capacityDropped.Count == 0
                    ? BeamResult.NoCapacityDrops
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
                Equivalence = EquivalenceDiagnostics(),
            SearchMemoryUsedBytes = _storage.Memory.UsedBytes,
            SearchMemoryPeakBytes = _storage.Memory.PeakBytes,
            SearchMemoryLimitBytes = _storage.Memory.LimitBytes,
            SearchMemoryBudgetExceeded = _memoryBudgetExceeded
                || _storage.Memory.Exceeded,
            SearchMemoryExceededAtBytes = _memoryBudgetExceeded
                ? _memoryBudgetExceededAtBytes : _storage.Memory.ExceededAtBytes,
            SoftTimeBudgetReached = SoftTimeReached(watch),
            RouteNodeStorageBytes = _nodes.StorageBytes,
            RouteNodeCount = _nodes.Count,
            RouteNodePeakCount = _nodes.PeakCount,
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
                ref Battle state = ref turnFrontier.State(index);
                ScoreFeatures features =
                    _scorer.Features(state, actionCount, includeRetentionOnly: false);
                Consider(state, node, Key(state, features, actionCount), features);
            }
        }
        if (_memoryBudgetExceeded && Route(bestNode).Length == 0)
        {
            int fallback = BudgetFallbackNode(current, ended, turnFrontier);
            if (fallback >= 0)
                bestNode = fallback;
        }
        _trace?.Finish(TraceFinishReason(hpTargetReached, zeroDamageVictory, watch, turns),
            turns, _expanded);
        BeamResult result = new(Route(bestNode), TopCandidateRoutes(), bestVictory,
            best?.Score ?? double.NegativeInfinity,
            _expanded, _transitions, _duplicates, turns, _pruned, watch.Elapsed)
        {
            CapacityPruned = _capacityPruned,
            EndTurnPrefixShared = _endTurnPrefixShared,
            EndTurnPrefixUnshared = _endTurnPrefixUnshared,
            EndTurnPrefixVerified = _endTurnPrefixVerified,
            ThreatProjectionVerified = _scorer.ThreatProjectionChecks,
            EndTurnPrefixReusedLeaves = _endTurnPrefixReusedLeaves,
            EndTurnPrefixChoiceUnshared = _endTurnPrefixChoiceUnshared,
            EndTurnPrefixExtraTurnUnshared = _endTurnPrefixExtraTurnUnshared,
            EndTurnPrefixNotInProgress = _endTurnPrefixNotInProgress,
            CapacityDroppedChildren = _capacityDropped.Count == 0
                ? BeamResult.NoCapacityDrops
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
            Equivalence = EquivalenceDiagnostics(),
            SearchMemoryUsedBytes = _storage.Memory.UsedBytes,
            SearchMemoryPeakBytes = _storage.Memory.PeakBytes,
            SearchMemoryLimitBytes = _storage.Memory.LimitBytes,
            SearchMemoryBudgetExceeded = _memoryBudgetExceeded
                || _storage.Memory.Exceeded,
            SearchMemoryExceededAtBytes = _memoryBudgetExceeded
                ? _memoryBudgetExceededAtBytes : _storage.Memory.ExceededAtBytes,
            SoftTimeBudgetReached = SoftTimeReached(watch),
            RouteNodeStorageBytes = _nodes.StorageBytes,
            RouteNodeCount = _nodes.Count,
            RouteNodePeakCount = _nodes.PeakCount,
        };
        return result;
    }

    private string TraceFinishReason(bool hpTargetReached, bool zeroDamageVictory, Stopwatch watch, int turns)
    {
        if (_takeoverAction != TakeoverAction.None)
            return "takeover";
        if (hpTargetReached)
            return "hp-target";
        if (zeroDamageVictory)
            return "zero-damage-victory";
        if (OutOfBudget(watch))
            return "budget";
        if (turns >= _options.MaxTurns)
            return "max-turns";
        return "frontier-empty";
    }

    private void ConsiderAudited(int node, in ScoreFeatures features, in RouteKey key)
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
        PotionUseMode effectivePotionPolicy = _effectivePotionPolicy switch
        {
            PotionUseMode.RequireAtLeastOne when forcedCount > 0 => PotionUseMode.Smart,
            PotionUseMode.Disabled when optionalPotionCount > 0 => PotionUseMode.Smart,
            _ => _effectivePotionPolicy,
        };
        int policyHpDeficit = effectivePotionPolicy == PotionUseMode.RequireAtLeastOne
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
    /// <c>PotionStrategySnapshot.EvaluateForcedUses(SearchNode)</c> over the Sts2Solver node chain: one
    /// explicit use of each forced potion identity in its slot, with the fixed prefix's uses folded in.
    /// Returns whether every forced directive was used.
    /// </summary>
    private bool EvaluateForcedUses(int node, out int count, out int strategicCost,
        out int ambergrisCount)
    {
        ForcedPotionDirectives directives = _forcedPotionDirectives;
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
                    PlayerAction action = _nodes[cursor].Action;
                    if (action.Kind == PlayerActionKind.UsePotion && action.PotionSlot == slot
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
    private bool SelectAudited(out int node, out RouteKey key, out bool victory)
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
            return false;
        }
        node = bestCandidate.Node;
        key = bestCandidate.Key;
        victory = bestCandidate.Key.Won;
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
            && _effectivePotionPolicy != PotionUseMode.Disabled
            && candidate.Key.OptionalPotionCount > 0
            && candidate.ProjectedDeathSaveUseCount < potionFreeDeathSaves;
        // FinalPlanOrdering.Select: under the preserve policy, more potion use is allowed when it
        // recovers outstanding loot; the soft-policy exception requires an actual potion use, while
        // the Ambergris exception applies to the recovered-loot key even with zero potions.
        bool recoversLoot = _theftPolicy == TheftPolicy.PreserveResources
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
        if (_theftPolicy == TheftPolicy.PreserveResources)
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

    private delegate void TerminalSink(in Battle state, int node, double score);

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
        PotionUseMode EffectivePotionPolicy);

    private RouteKey Key(in Battle state, in ScoreFeatures features, int actionCount)
    {
        bool won = InterimResultOrdering.IsCompleteVictory(
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
        // members keep their prior route key so the Sts2Solver adoption decisions they already passed do
        // not move; the exact-layer requests port the key together with the potion filter.
        int policyHpDeficit = _auditPotionPolicy
            && _effectivePotionPolicy == PotionUseMode.RequireAtLeastOne
                ? strategicHpDeficit
                    + PotionUsePolicy.AdditionalRequiredUseStrategicHpCost(optionalPotionStrategicCost)
                : strategicHpDeficit;
        return new RouteKey(
            won,
            !features.PlayerDead && features.ProjectedPlayerHp > 0,
            features.ProjectedDeathSaveUseCount,
            _theftPolicy == TheftPolicy.PreserveResources ? features.OutstandingStolenResource : 0,
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
            state.Outcome == Outcome.EventDefeat ? 4 : 0,
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
        if (_theftPolicy == TheftPolicy.PreserveResources)
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
        // FinalPlanOrdering.Select: PolicyBoundaryRank, then OptionalPotionCount, then StrategicSold.
        // StrategicSold is the request's committed sold HP plus the route's future sold HP; Beam
        // keeps no route-level future sold HP, so the key is the same for every route of a request
        // and is left out.
        comparison = left.BoundaryRank.CompareTo(right.BoundaryRank);
        if (comparison != 0) return comparison;
        comparison = left.OptionalPotionCount.CompareTo(right.OptionalPotionCount);
        if (comparison != 0) return comparison;
        comparison = left.EnemyHp.CompareTo(right.EnemyHp);
        if (comparison != 0) return comparison;
        comparison = right.Score.CompareTo(left.Score);
        return comparison != 0 ? comparison : left.ActionCount.CompareTo(right.ActionCount);
    }

    /// <summary>
    /// Compares two solved results with the same final-ordering key the solver used to select its best
    /// route. Both routes are replayed from their recorded root and scored at their final state; a
    /// result whose route cannot be replayed or whose root index is invalid is treated as the worse
    /// one. Negative means <paramref name="left"/> is better, zero equal, positive worse. The report
    /// command calls this after solving with the current code; the frozen result's route is replayed
    /// with the same kernel, so the comparison is against the solver's own key, not a proxy score.
    /// </summary>
    internal int CompareResultQuality(IReadOnlyList<Battle> roots, in BeamResult left, in BeamResult right)
    {
        if (!TryCompareResultQuality(roots, left, right, out int comparison))
            return 0;
        return comparison;
    }

    /// <summary>
    /// As <see cref="CompareResultQuality"/>, but reports whether both routes could be replayed and
    /// scored. The report harness uses the false case as "unknown" instead of treating it as a tie.
    /// </summary>
    internal bool TryCompareResultQuality(IReadOnlyList<Battle> roots, in BeamResult left,
        in BeamResult right, out int comparison)
    {
        comparison = 0;
        bool leftOk = TryEvaluateResultQuality(roots, left, out RouteKey leftKey);
        bool rightOk = TryEvaluateResultQuality(roots, right, out RouteKey rightKey);
        if (!leftOk || !rightOk)
            return false;
        comparison = CompareRoutes(leftKey, rightKey);
        return true;
    }

    private bool TryEvaluateResultQuality(IReadOnlyList<Battle> roots, in BeamResult result,
        out RouteKey key)
    {
        key = default;
        if ((uint)result.BestRootIndex >= (uint)roots.Count)
            return false;
        Battle state = roots[result.BestRootIndex];
        int actionCount = _options.InitialActionCount;
        foreach (PlayerAction action in result.BestRoute)
        {
            try
            {
                StepResult step = Kernel.Step(ref state, action, _rules, out ChoiceRequest request);
                if (step != StepResult.Completed)
                    return false;
            }
            catch (CapacityException)
            {
                return false;
            }
            actionCount++;
        }
        ScoreFeatures features = _scorer.Features(state, actionCount, includeRetentionOnly: false);
        key = Key(state, features, actionCount);
        return true;
    }

    /// <summary>
    /// Finds the lowest-index play action with the same hand-card content and target. The action list
    /// is ordered by hand index, so the first match is the representative. Exact means that every
    /// hand position between the two copies is content-equal: playing either leaves the same hand
    /// sequence. Any other content-equal copy is an approximate sibling in the A/B sense.
    /// </summary>
    private static bool TryFindEarlierEquivalentPlay(in Battle parent,
        scoped ReadOnlySpan<PlayerAction> actions, int actionIndex, out int representative,
        out bool exact)
    {
        representative = -1;
        exact = false;
        ref readonly PlayerAction action = ref actions[actionIndex];
        if (action.Kind != PlayerActionKind.PlayCard || action.HandIndex >= parent.HandCount)
            return false;
        Card card = parent.Cards[action.HandIndex];
        for (int prior = 0; prior < actionIndex; prior++)
        {
            ref readonly PlayerAction candidate = ref actions[prior];
            if (candidate.Kind != PlayerActionKind.PlayCard || candidate.Target != action.Target
                || candidate.HandIndex >= parent.HandCount)
                continue;
            if (!CardEquivalence.ContentEquals(in card, in parent.Cards[candidate.HandIndex]))
                continue;
            representative = prior;
            exact = CardEquivalence.SameHandRun(in parent, action.HandIndex, candidate.HandIndex);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Runs the skipped play action and its representative from the same parent with one deterministic
    /// choice policy, then compares the completed battles. An exact prediction must match; an
    /// approximate one only contributes to the equality count.
    /// </summary>
    private void VerifySkippedPlay(in Battle parent, in PlayerAction representative,
        in PlayerAction skipped, bool exact)
    {
        if (!TryExecuteForVerification(in parent, representative, out Battle expected)
            || !TryExecuteForVerification(in parent, skipped, out Battle actual))
        {
            _playVerifiedUnresolved++;
            return;
        }
        bool equal = Fingerprint.Equal(expected, actual);
        if (exact)
        {
            _playVerifiedExact++;
            if (!equal)
            {
                throw new InvalidOperationException(
                    $"同名出牌完全等价校验失败：representative=hand{representative.HandIndex} "
                    + $"skipped=hand{skipped.HandIndex} target={skipped.Target}。");
            }
            return;
        }
        _playVerifiedApprox++;
        if (equal)
            _playVerifiedApproxEqual++;
    }

    /// <summary>
    /// For every enumerated class vector, checks one distinct physical selection (the last occurrence
    /// of each class) against the vector's representative. A class vector is exact when the selected
    /// content can only leave the same source-pile order; generated options are always exact.
    /// </summary>
    private void VerifyChoiceAlternatives(in Battle parent, in Battle choiceState,
        in PlayerAction action, in ChoiceRequest request, scoped ReadOnlySpan<Card> cards,
        scoped ReadOnlySpan<ulong> selections)
    {
        int checkedAlternatives = 0;
        for (int index = 0; index < selections.Length
            && checkedAlternatives < MaxChoiceAlternativesVerifiedPerRequest; index++)
        {
            if (!ChoiceEnumerator.TryBuildLastOccurrenceSelection(request, cards, selections[index],
                    out ulong alternative))
                continue;
            checkedAlternatives++;
            bool exact = ChoiceEnumerator.IsExactByClass(in choiceState, request, cards, selections[index]);
            bool expectedComplete, actualComplete;
            Battle expected, actual;
            try
            {
                expectedComplete = TryExecuteForVerification(in parent,
                    action.WithChoice(selections[index]), out expected);
                actualComplete = TryExecuteForVerification(in parent,
                    action.WithChoice(alternative), out actual);
            }
            catch (CapacityException)
            {
                _choiceVerifiedUnresolved++;
                continue;
            }
            if (!expectedComplete || !actualComplete)
            {
                _choiceVerifiedUnresolved++;
                continue;
            }
            bool equal = Fingerprint.Equal(expected, actual);
            if (exact)
            {
                _choiceVerifiedExact++;
                if (!equal)
                {
                    System.Text.StringBuilder optionsText = new();
                    for (int option = 0; option < request.OptionCount; option++)
                    {
                        if (option != 0)
                            optionsText.Append(',');
                        optionsText.Append(option).Append(':')
                            .Append(cards[option].Id).Append('+').Append(cards[option].Upgrade)
                            .Append('@')
                            .Append(request.OptionsAreGenerated ? -1 : request.Options[option]);
                    }
                    throw new InvalidOperationException(
                        $"选牌等价类完全等价校验失败：effect={request.Effect} "
                        + $"source={request.Source} options={request.OptionCount} "
                        + $"choices={action.ChoiceCount} representative=0x{selections[index]:x} "
                        + $"alternative=0x{alternative:x} cards=[{optionsText}] "
                        + string.Join("; ", BattleDump.Diff(expected, actual, "representative", "alternative")));
                }
                continue;
            }
            _choiceVerifiedApprox++;
            if (equal)
                _choiceVerifiedApproxEqual++;
        }
    }

    /// <summary>
    /// Executes one action from a parent with the same deterministic continuation for both sides of a
    /// comparison: after every <see cref="StepResult.NeedsChoice"/> it reruns the action with the
    /// first class-vector selection appended. Returns false when the action hits a child capacity
    /// boundary or exceeds the choice depth; those comparisons are reported as unresolved.
    /// </summary>
    private bool TryExecuteForVerification(in Battle parent, in PlayerAction action, out Battle result)
    {
        Span<Card> optionCards = stackalloc Card[Limits.MaxChoiceOptions];
        Span<ulong> selections = stackalloc ulong[MaxChoiceBranches];
        PlayerAction current = action;
        for (int depth = 0; depth <= Limits.MaxChoices; depth++)
        {
            Battle state = parent;
            StepResult step;
            ChoiceRequest request;
            try
            {
                step = Kernel.Step(ref state, current, _rules, out request);
            }
            catch (CapacityException)
            {
                result = default;
                return false;
            }
            if (step == StepResult.Completed)
            {
                result = state;
                return true;
            }
            if (current.ChoiceCount >= Limits.MaxChoices)
                break;
            for (int option = 0; option < request.OptionCount; option++)
            {
                if (request.OptionsAreGenerated)
                {
                    optionCards[option] = request.GeneratedOptions[option];
                    continue;
                }
                int position = request.Options[option];
                if ((uint)position >= Limits.Cards)
                {
                    result = default;
                    return false;
                }
                optionCards[option] = state.Cards[position];
            }
            int count = ChoiceEnumerator.EnumerateByClass(request, optionCards[..request.OptionCount],
                selections);
            if (count == 0)
                break;
            try
            {
                current = current.WithChoice(selections[0]);
            }
            catch (CapacityException)
            {
                result = default;
                return false;
            }
        }
        result = default;
        return false;
    }

    private SearchEquivalenceDiagnostics EquivalenceDiagnostics()
        => new(_playDeduped, _playVerifiedExact, _playVerifiedApprox, _playVerifiedApproxEqual,
            _playVerifiedUnresolved, _choiceByClassRequests, _choiceVerifiedExact,
            _choiceVerifiedApprox, _choiceVerifiedApproxEqual, _choiceVerifiedUnresolved);

    private void ExpandAction(ref Battle parent, int parentNode, PlayerAction action, Pool next,
        Pool ended, TerminalSink terminal, in RetentionFacts parentFacts, int parentRank,
        double parentScore)
        => ExpandAction(ref parent, parentNode, action, next, ended, terminal, parentFacts,
            parentRank, parentScore, null, default, null, allowFetchedPowerCommit: true);

    private void ExpandAction(ref Battle parent, int parentNode, PlayerAction action, Pool next, Pool ended,
        TerminalSink terminal, in RetentionFacts parentFacts, int parentRank, double parentScore,
        ChoiceEffect? primaryEffect, scoped ReadOnlySpan<Card> selectedPowers,
        RoutingChoice? routingChoice, bool allowFetchedPowerCommit)
    {
        ushort usedPotion = ushort.MaxValue;
        if (action.Kind == PlayerActionKind.UsePotion
            && (uint)action.PotionSlot < (uint)parent.PotionSlotCount
            && parent.Potions[action.PotionSlot] != 0)
        {
            usedPotion = (ushort)(parent.Potions[action.PotionSlot] - 1);
        }
        // A capacity overflow is a child boundary, not a simulation error: the one child that needs
        // more cards, powers, enemies, choice slots or action slots than Battle holds is dropped
        // and the rest of the search continues. Every adopted route is still verified step by step
        // against the existing engine. The catch covers the whole construction of this child
        // (reserve, step, score and commit); the per-selection catch below keeps one overflowing
        // choice branch from cancelling its siblings.
        try
        {
            // The child is built in next's first free slot and stepped in place: one copy of the parent per
            // transition. It is committed to next, copied to ended, or dropped as a terminal; the slot is not
            // touched by anything else until then (a choice re-expansion reuses it from the start).
            ref Battle child = ref next.Reserve();
            child = parent;
            _transitions++;
            StepResult stepResult;
            ChoiceRequest request;
            bool historyRisk;
            if (action.Kind == PlayerActionKind.EndTurn && CanReuseEndTurnPrefix(parentNode))
            {
                _endTurnPrefixReusedLeaves++;
                stepResult = Kernel.StepEndTurnFromPrefix(ref child, action, _rules, in _endTurnPrefix,
                    out request, out historyRisk);
                if (_verifySearchEquivalence)
                    VerifyPreparedEndTurn(ref parent, action, ref child, stepResult, in request, historyRisk);
            }
            else if (action.Kind == PlayerActionKind.EndTurn && IsFirstEndTurnLeaf(parentNode))
            {
                stepResult = Kernel.StepEndTurnSharingPrefix(ref child, action, _rules, out request,
                    out historyRisk, out EndTurnPrefix prefix, out EndTurnPrefixStatus prefixStatus);
                StoreEndTurnPrefix(in prefix, prefixStatus, parentNode);
            }
            else
            {
                stepResult = Kernel.Step(ref child, action, _rules, out request, out historyRisk);
            }
            if (stepResult == StepResult.NeedsChoice)
            {
                // The branch loop below expands every selection through the same pool. A recursive
                // ExpandAction reserves and overwrites next's current slot, so `child` is no longer a
                // stable view of the partial state once the first branch runs. Capture the option card
                // values now: they are the only child state the loop and its routing metadata read.
                Span<Card> optionCards = stackalloc Card[request.OptionCount];
                for (int option = 0; option < request.OptionCount; option++)
                {
                    if (request.OptionsAreGenerated)
                    {
                        optionCards[option] = request.GeneratedOptions[option];
                        continue;
                    }
                    int position = request.Options[option];
                    if ((uint)position >= Limits.Cards)
                        throw new InvalidOperationException(
                            $"选择选项 {option} 的卡牌位置 {position} 超出牌数组容量。");
                    optionCards[option] = child.Cards[position];
                }
                // Every executed selection is exact; the cap only bounds how many are explored. With
                // class enumeration each slot is a different content multiset.
                Span<ulong> selections = stackalloc ulong[MaxChoiceBranches];
                int count;
                if (!_dedupeChoicesByClass)
                {
                    count = ChoiceEnumerator.Enumerate(request, selections);
                }
                else if (_choiceFillVariants)
                {
                    bool truncated = ChoiceEnumerator.Count(request, MaxChoiceBranches + 1)
                        > MaxChoiceBranches;
                    count = truncated
                        ? ChoiceEnumerator.EnumerateByClassFilled(request, optionCards, selections)
                        : ChoiceEnumerator.Enumerate(request, selections);
                }
                else
                {
                    count = ChoiceEnumerator.EnumerateByClass(request, optionCards, selections);
                }
                if (_dedupeChoicesByClass)
                {
                    _choiceByClassRequests++;
                    if (_verifySearchEquivalence)
                        VerifyChoiceAlternatives(in parent, in child, action, request, optionCards,
                            selections[..count]);
                }
                bool captureFetchedPowers = allowFetchedPowerCommit && primaryEffect is null
                    && request.Timing == ChoiceTiming.Action
                    && IsPrimaryMoveToHandAction(parent, action)
                    && request.Effect == ChoiceEffect.MoveToHand
                    && !request.OptionsAreGenerated;
                Span<bool> optionIsPower = stackalloc bool[captureFetchedPowers ? request.OptionCount : 0];
                for (int option = 0; option < optionIsPower.Length; option++)
                    optionIsPower[option] =
                        Kernel.ResolveCardType(child, _rules, optionCards[option]) == CardType.Power;
                Span<Card> capturedPowers = stackalloc Card[Limits.MaxChoiceOptions];
                for (int index = 0; index < count; index++)
                {
                    ChoiceEffect? effect = primaryEffect;
                    scoped ReadOnlySpan<Card> chosenPowers = selectedPowers;
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
                                throw new CapacityException("FetchedPowers", capturedPowers.Length);
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
                    catch (CapacityException ex)
                    {
                        RecordCapacityDrop(ex);
                    }
                }
                return;
            }
            int depth = _nodes[parentNode].Depth + 1;
            bool isTerminal = !Kernel.IsInProgress(child);
            int traceStep = _trace?.Match(parentNode, action) ?? 0;
            (double score, double rank, double pureRank, int potionCost, ScoreFeatures features) =
                Score(child, depth, isTerminal, action.Kind == PlayerActionKind.EndTurn);
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
            RoutingMeta routingMeta;
            int routingMetaId;
            if (routingSignature != 0)
            {
                routingMeta = new RoutingMeta(routingSignature, routingChoice!.Value.Turn,
                    routingChoice.Value.ChoiceDepth, routingChoice.Value.ParentRank,
                    routingChoice.Value.ParentScore, routingChoice.Value.ParentEnergy);
                routingMetaId = _routingMetas.Add(routingMeta);
            }
            else
            {
                RoutingMeta parentMeta = _routingMetas[parentRoute.RoutingMetaId];
                if (parentMeta.SignatureId == 0)
                {
                    routingMeta = parentMeta;
                    routingMetaId = parentRoute.RoutingMetaId;
                }
                else
                {
                    // Descendants keep the inherited context fields but the answered signature is no
                    // longer current; one shared entry serves the rest of that route segment.
                    routingMeta = parentMeta with { SignatureId = 0 };
                    routingMetaId = _routingMetas.Add(routingMeta);
                }
            }
            int actionCardId = action.Kind == PlayerActionKind.PlayCard && action.HandIndex < parent.HandCount
                ? parent.Cards[action.HandIndex].Id : 0;
            _nodes.Add(new RouteNode(parentNode, depth, action, rootActionKey, lineage, payoffCount,
                usedPotion, firstCardId, firstCardTarget, commitment, actionCardId,
                TargetCombatId(parent, action), routingMeta.SignatureId, routingMeta.Turn,
                routingMeta.ChoiceDepth, routingMeta.ParentRank, routingMeta.ParentScore,
                routingMeta.ParentEnergy, parentFacts.Turn, routingMetaId), routingMetaId);
            if (traceStep > 0)
                _trace!.NodeCreated(node, traceStep);
            if (_options.Novelty != null)
                ObserveNoveltyChild(child, node, features);
            if (isTerminal)
            {
                terminal(child, node, score);
                return;
            }
            if (allowFetchedPowerCommit
                && _expandFetchedPowers
                && primaryEffect == ChoiceEffect.MoveToHand
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
            bool added = action.Kind == PlayerActionKind.EndTurn || child.PlayerTurn != parent.PlayerTurn
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
        catch (CapacityException ex)
        {
            RecordCapacityDrop(ex);
        }
    }

    /// <summary>
    /// True when this is the first end-turn leaf of the current parent. The first leaf executes the
    /// complete step and captures the prefix as a by-product; recursive selection leaves reuse it.
    /// </summary>
    private bool IsFirstEndTurnLeaf(int parentNode)
    {
        if (_endTurnPrefixAttemptGeneration == _expandGeneration
            && _endTurnPrefixAttemptNode == parentNode)
            return false;
        _endTurnPrefixAttemptGeneration = _expandGeneration;
        _endTurnPrefixAttemptNode = parentNode;
        return true;
    }

    private bool CanReuseEndTurnPrefix(int parentNode)
        => _endTurnPrefixValid
            && _endTurnPrefixGeneration == _expandGeneration
            && _endTurnPrefixNode == parentNode;

    private void StoreEndTurnPrefix(in EndTurnPrefix prefix, EndTurnPrefixStatus status, int parentNode)
    {
        if (prefix.IsPrepared)
        {
            _endTurnPrefix = prefix;
            _endTurnPrefixValid = true;
            _endTurnPrefixGeneration = _expandGeneration;
            _endTurnPrefixNode = parentNode;
            _endTurnPrefixShared++;
            return;
        }
        _endTurnPrefixValid = false;
        _endTurnPrefixUnshared++;
        switch (status)
        {
            case EndTurnPrefixStatus.ChoiceBeforePlayerStart:
                _endTurnPrefixChoiceUnshared++;
                break;
            case EndTurnPrefixStatus.ExtraTurn:
                _endTurnPrefixExtraTurnUnshared++;
                break;
            case EndTurnPrefixStatus.NotInProgress:
                _endTurnPrefixNotInProgress++;
                break;
        }
    }

    /// <summary>
    /// Default-off differential check: runs the complete step from the parent and compares every
    /// observable result of the prefix continuation against it. Only enabled by
    /// <see cref="SearchVerificationHooks.VerifySearchEquivalence"/>.
    /// </summary>
    private void VerifyPreparedEndTurn(ref Battle parent, in PlayerAction action, ref Battle actual,
        StepResult actualResult, in ChoiceRequest actualRequest, bool actualHistoryRisk)
    {
        Battle expected = parent;
        StepResult expectedResult = Kernel.Step(ref expected, action, _rules, out ChoiceRequest expectedRequest,
            out bool expectedHistoryRisk);
        if (expectedResult == actualResult && expectedHistoryRisk == actualHistoryRisk
            && BattleBytesEqual(expected, actual) && RequestEquals(expectedRequest, actualRequest))
        {
            _endTurnPrefixVerified++;
            return;
        }
        string? requestDifference = RequestDifference(expectedRequest, actualRequest);
        List<string> battleDifference = BattleBytesEqual(expected, actual)
            ? []
            : BattleDump.Diff(expected, actual, "complete", "prefix");
        throw new InvalidOperationException(
            $"EndTurn 前缀等价校验失败：kind={action.Kind} hand={action.HandIndex} "
            + $"target={action.Target} choices={action.ChoiceCount} "
            + $"result complete={expectedResult} prefix={actualResult}；"
            + $"historyRisk complete={expectedHistoryRisk} prefix={actualHistoryRisk}；"
            + $"request={(requestDifference ?? "same")}；battle="
            + (battleDifference.Count == 0 ? "same" : string.Join(" | ", battleDifference)));
    }

    private static bool BattleBytesEqual(in Battle expected, in Battle actual)
    {
        ReadOnlySpan<byte> left = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in expected), 1));
        ReadOnlySpan<byte> right = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in actual), 1));
        return left.SequenceEqual(right);
    }

    private static bool RequestEquals(in ChoiceRequest expected, in ChoiceRequest actual)
        => RequestDifference(expected, actual) is null;

    private static string? RequestDifference(in ChoiceRequest expected, in ChoiceRequest actual)
    {
        if (expected.Kind != actual.Kind)
            return $"Kind {expected.Kind}/{actual.Kind}";
        if (expected.Effect != actual.Effect)
            return $"Effect {expected.Effect}/{actual.Effect}";
        if (expected.Timing != actual.Timing)
            return $"Timing {expected.Timing}/{actual.Timing}";
        if (expected.Source != actual.Source)
            return $"Source {expected.Source}/{actual.Source}";
        if (expected.SourceIsNone != actual.SourceIsNone)
            return $"SourceIsNone {expected.SourceIsNone}/{actual.SourceIsNone}";
        if (expected.OptionsAreGenerated != actual.OptionsAreGenerated)
            return $"OptionsAreGenerated {expected.OptionsAreGenerated}/{actual.OptionsAreGenerated}";
        if (expected.OptionCount != actual.OptionCount || expected.MinCount != actual.MinCount
            || expected.MaxCount != actual.MaxCount)
            return $"counts {expected.OptionCount}/{actual.OptionCount} {expected.MinCount}/{actual.MinCount} "
                + $"{expected.MaxCount}/{actual.MaxCount}";
        for (int index = 0; index < expected.OptionCount; index++)
        {
            if (expected.OptionsAreGenerated)
            {
                if (!CardBytesEqual(expected.GeneratedOptions[index], actual.GeneratedOptions[index]))
                    return $"GeneratedOptions[{index}]";
            }
            else if (expected.Options[index] != actual.Options[index])
            {
                return $"Options[{index}] {expected.Options[index]}/{actual.Options[index]}";
            }
        }
        return null;
    }

    private static bool CardBytesEqual(in Card left, in Card right)
    {
        ReadOnlySpan<byte> leftBytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in left), 1));
        ReadOnlySpan<byte> rightBytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in right), 1));
        return leftBytes.SequenceEqual(rightBytes);
    }

    /// <summary>
    /// Builds the routing-choice signature of one answered selection request. The existing
    /// <c>RoutingChoiceSignature</c> also carries source/context ids and three enemy/pile distribution
    /// fingerprints; Sts2Solver has no string source ids or distribution keys on the route node, so those
    /// dimensions are not part of the key (choices from different sources of the same turn with the same
    /// effect and option merge). The supported effect set is the non-mutation routing set; persistent
    /// ordered-mutation effects are not part of this key (Sts2Solver does not generate that lineage).
    /// </summary>
    private bool TryBuildRoutingChoice(in Battle parent, in PlayerAction action,
        in ChoiceRequest request, scoped ReadOnlySpan<Card> optionCards, ulong selection,
        int parentNode, int parentTurn, int parentRank, double parentScore, in RetentionFacts parentFacts,
        out RoutingChoice choice)
    {
        choice = default;
        if (request.Effect is not (ChoiceEffect.MoveToHand or ChoiceEffect.MoveToDrawTop
            or ChoiceEffect.Discard or ChoiceEffect.GenerateToHand))
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
            Card card = optionCards[option];
            cardId = card.Id;
            upgrade = card.Upgrade;
            stateKey = CardStateKey(card);
            occurrence = option;
        }
        // The existing PlanCardChoice.SourceId is the requesting card's entry (the potion's entry
        // for potion choices, the relic's for turn-start relic choices). Sts2Solver has no string on the
        // request, so the currently played card or potion identifies the source; a card/potion id is
        // prefixed with its kind so the two id spaces cannot merge.
        int sourceId = action.Kind switch
        {
            PlayerActionKind.PlayCard when action.HandIndex < parent.HandCount
                => (1 << 16) | parent.Cards[action.HandIndex].Id,
            PlayerActionKind.UsePotion when action.PotionSlot < parent.PotionSlotCount
                => (2 << 16) | parent.Potions[action.PotionSlot],
            _ => 0,
        };
        int turn = action.Kind == PlayerActionKind.EndTurn
            && request.Timing == ChoiceTiming.PlayerTurnStart
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
            // The chunk itself is charged by ChunkedList; this covers the dictionary entry and its
            // bucket headroom, the only signature allocation outside the chunk store.
            _storage.Memory.Charge(RoutingSignatureDictionaryBytes, "routing-signature");
        }
        choice = new RoutingChoice(id, turn, _nodes[parentNode].Depth + 1, parentRank, parentScore,
            parentFacts.Energy);
        return true;
    }

    /// <summary>A 32-bit state key for the selected option card: FNV-1a over the card value. It stands
    /// in for the existing <c>PlanCardToken.StateKey</c>, which distinguishes equal-id card variants.</summary>
    private static int CardStateKey(in Card card)
    {
        ulong hash = 1469598103934665603UL;
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in card), 1));
        for (int index = 0; index < bytes.Length; index++)
            hash = (hash ^ bytes[index]) * 1099511628211UL;
        return (int)(hash ^ (hash >> 32));
    }

    /// <summary>
    /// The existing <c>BeamRetentionPolicy.BuildRootActionLineageSignature</c> over Sts2Solver actions.
    /// The root action's kind/card/potion/target is frozen on the first step; when the root has an
    /// unused card replay allocator the route's first played card is part of the signature too. The
    /// signature is a stable value, so equal first actions from different turn-setup roots group
    /// together exactly as the existing search groups them.
    /// </summary>
    private ulong NextLineage(int parentNode, in Battle parent, in PlayerAction action, int node,
        out ulong rootActionKey, out ushort firstCardId, out int firstCardTarget)
    {
        RouteNode entry = _nodes[parentNode];
        rootActionKey = entry.RootActionKey;
        firstCardId = entry.FirstCardId;
        firstCardTarget = entry.FirstCardTarget;
        if (entry.Parent >= 0)
        {
            if (_preserveReplayAllocatorOpening && firstCardId == ushort.MaxValue
                && action.Kind == PlayerActionKind.PlayCard && action.HandIndex < parent.HandCount)
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
        if (_preserveReplayAllocatorOpening && action.Kind == PlayerActionKind.PlayCard
            && action.HandIndex < parent.HandCount)
        {
            firstCardId = parent.Cards[action.HandIndex].Id;
            firstCardTarget = TargetCombatId(parent, action);
        }
        // With one root every first action keeps its own lineage. Signature grouping is only applied
        // when multiple turn-setup roots must join the same opening. A previous 120-root measurement
        // showed signature grouping regresses the aggregate, so it is not kept.
        return _singleRoot ? (ulong)node : CombineLineage(rootActionKey, firstCardId, firstCardTarget);
    }

    private static ulong RootActionSignature(in Battle parent, in PlayerAction action)
    {
        ulong kind = action.Kind switch
        {
            PlayerActionKind.PlayCard => 1UL,
            PlayerActionKind.UsePotion => 2UL,
            _ => 3UL,
        };
        ulong cardId = 0;
        ulong potionId = 0;
        if (action.Kind == PlayerActionKind.PlayCard && action.HandIndex < parent.HandCount)
            cardId = parent.Cards[action.HandIndex].Id;
        if (action.Kind == PlayerActionKind.UsePotion
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

    /// <summary>The existing <c>PlanAction.TargetCombatId</c> for a Sts2Solver target owner.</summary>
    private static int TargetCombatId(in Battle parent, in PlayerAction action)
    {
        if (action.Target == Limits.NoOwner)
            return int.MinValue;
        int enemy = action.Target - Limits.FirstEnemyOwner;
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
    private bool AddEndTurnChild(Pool ended, in Battle child, int node, double score, double rank,
        double pureRank, int potionCost, in ScoreFeatures features, int depth, ulong lineage,
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
        catch (CapacityException) when (ended.IsFull)
        {
            _tracePhase = "ended-compact";
            ended.TracePhase = _tracePhase;
            int dropped = ended.Compact(_endedCompactionTarget);
            _endedCompactions++;
            _endedCompressedStates += dropped;
            return ended.AddScored(child, node, score, rank, pureRank, potionCost, features, depth,
                lineage, payoffCount, commitment, traits, pocketwatchThreshold, pocketwatchPlayed,
                pocketwatchLastTurn, parentEnemyBlock, parentEnemyHp, parentFutureResourceValue,
                parentStrategicResourcePotential);
        }
    }

    private void RecordCapacityDrop(CapacityException ex)
    {
        _capacityPruned++;
        CollectionsMarshal.GetValueRefOrAddDefault(_capacityDropped, ex.Region, out _)++;
    }

    private bool CommitmentBudgetAvailable()
        => NodeBudgetAvailable
            && !_memoryBudgetExceeded
            && !_hpTargetReached
            && (_solveWatch is not { } watch || watch.Elapsed < _options.SoftTimeBudget);

    /// <summary>
    /// The existing search's <c>Expand(commitment)</c>: every legal successor of the fetched-power
    /// state enters the current layer through the normal path. The commitment state itself is dropped
    /// (<c>nextPlays.Remove(commitment)</c>). The action buffer is separate because the enclosing layer
    /// loop still iterates its own actions.
    /// </summary>
    private void ExpandFetchedPowerSuccessors(ref Battle commitment, int commitmentNode,
        Pool next, Pool ended, TerminalSink terminal, SearchRouteTraits traits,
        int pocketwatchThreshold, int pocketwatchPlayed, int parentRank, double parentScore,
        int parentEnemyBlock, int parentEnemyHp, int parentFutureResourceValue,
        int parentStrategicResourcePotential)
    {
        if (_expanded < int.MaxValue) _expanded++;
        _expandGeneration++;
        Battle state = commitment;
        // The intermediate selection state is the parent of every successor. Its retention facts are
        // computed once; the retained state itself is never committed.
        int commitmentDepth = Depth(commitmentNode);
        ScoreFeatures commitmentFeatures =
            _scorer.Features(state, commitmentDepth, includeRetentionOnly: true);
        (int commitmentReachable, int commitmentZeroCost, _) = _scorer.ReachableHandPotential(state);
        RetentionFacts commitmentFacts = RetentionFacts.From(commitmentFeatures, commitmentDepth,
            commitmentReachable, commitmentZeroCost, traits, pocketwatchThreshold, pocketwatchPlayed,
            _scorer.InitialRetainedAttackValue, parentEnemyBlock, parentEnemyHp,
            parentFutureResourceValue, parentStrategicResourcePotential);
        int count = Kernel.LegalActions(state, _rules, _commitmentActions);
        for (int index = 0; index < count; index++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            PlayerAction action = _commitmentActions[index];
            if (action.Kind == PlayerActionKind.UsePotion && !PotionAllowed(state, action.PotionSlot))
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
    private bool HasPlayableFetchedPower(in Battle battle, scoped ReadOnlySpan<Card> selectedPowers)
    {
        if (selectedPowers.Length == 0)
            return false;
        for (int index = 0; index < battle.HandCount; index++)
        {
            ref readonly Card card = ref battle.Cards[index];
            if (Kernel.ResolveCardType(battle, _rules, card) != CardType.Power
                || !Kernel.CanPlayCard(battle, _rules, index))
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
    private bool IsPrimaryMoveToHandAction(in Battle parent, in PlayerAction action)
    {
        if (action.Kind == PlayerActionKind.PlayCard)
        {
            if (action.HandIndex >= parent.HandCount)
                return false;
            ref readonly Card card = ref parent.Cards[action.HandIndex];
            return _scorer.IsPrimaryMoveToHandCard(card.Id, card.Upgrade);
        }
        if (action.Kind != PlayerActionKind.UsePotion
            || _scorer.DropletOfPrecognitionPotionId == ushort.MaxValue
            || action.PotionSlot >= parent.PotionSlotCount)
        {
            return false;
        }
        return parent.Potions[action.PotionSlot]
            == (ushort)(_scorer.DropletOfPrecognitionPotionId + 1);
    }

    private static bool SameCardIdentity(in Card left, in Card right)
        => left.Id == right.Id
            && left.Upgrade == right.Upgrade
            && left.EnchantmentId == right.EnchantmentId
            && left.EnchantmentAmount == right.EnchantmentAmount
            && left.AfflictionId == right.AfflictionId
            && left.AfflictionAmount == right.AfflictionAmount;

    /// <summary>Per-pool state budget: the request override, or the original 200 MiB.</summary>
    private int EffectivePoolStateBudgetBytes
        => _options.PoolStateBudgetBytes > 0
            ? _options.PoolStateBudgetBytes : DefaultPoolStateBudgetBytes;

    private bool NodeBudgetAvailable => _options.UnlimitedNodes || _expanded < _options.MaxExpandedNodes;

    private bool OutOfBudget(Stopwatch watch)
        => _memoryBudgetExceeded || !NodeBudgetAvailable
            || watch.Elapsed >= _options.SoftTimeBudget;

    /// <summary>
    /// The loop ended on the soft-time limit rather than the node cap or an early stop. A search
    /// that happened to cross the limit exactly at its natural end is reported as timed out; the
    /// flag is diagnostic and never enters the result codec.
    /// </summary>
    private bool SoftTimeReached(Stopwatch watch)
        => _options.SoftTimeBudget != TimeSpan.MaxValue
            && !_memoryBudgetExceeded
            && NodeBudgetAvailable
            && watch.Elapsed >= _options.SoftTimeBudget;

    private int Depth(int node) => _nodes[node].Depth;

    private int ExplicitPotionUses(int node)
    {
        int uses = _initialExplicitPotionUses;
        for (int cursor = node; _nodes[cursor].Parent >= 0; cursor = _nodes[cursor].Parent)
        {
            if (_nodes[cursor].Action.Kind == PlayerActionKind.UsePotion)
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
            PlayerAction action = _nodes[cursor].Action;
            if (action.Kind != PlayerActionKind.UsePotion)
                continue;
            if (_nodes[cursor].PotionId == _ambergrisPotionId)
                uses++;
        }
        return uses;
    }

    private PlayerAction[] Route(int node)
    {
        List<PlayerAction> route = [];
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

    private readonly record struct ScoutOpening(
        PlayerActionKind Kind, ushort CardId, ushort PotionId, uint? TargetCombatId);

    /// <summary>
    /// <c>the existing search's EarlyTurnStrategyKey</c> without <c>SearchRouteTraits</c>: Beam does not
    /// model the accumulated route trait flags, so the scout groups by the K4-checked setup traits and
    /// the remaining existing bands. The omission can only merge groups; it never invents a candidate.
    /// </summary>
    private readonly record struct ScoutStrategy(
        ScoutOpening Opening,
        PersistentSetupTraits SetupTraits,
        int PotionCount,
        int PersistentBuffBand,
        int HandValueBand,
        uint? FocusTargetId);

    private sealed record ScoutEntry(int Node, ScoutStrategy Strategy, double Score);

    /// <summary>
    /// <c>the existing search's SelectEarlyTurnFrontier</c> over the ended pool: filter the completed turn,
    /// keep the top two scores per strategy key, group by opening, then round-robin the opening groups
    /// up to 24 candidates. The pool already holds one representative per byte-identical state.
    /// </summary>
    private void CollectEarlyTurnScout(Pool ended, int completedTurns, IReadOnlyList<Battle> roots)
    {
        List<ScoutEntry> entries = new(ended.Count);
        for (int index = 0; index < ended.Count; index++)
        {
            ref Battle state = ref ended.State(index);
            if (state.PlayerTurn != _initialSearchTurn + completedTurns)
                continue;
            int node = ended.Node(index);
            int rootIndex = RootIndex(node);
            ScoutOpening opening = CaptureScoutOpening(roots[rootIndex], node);
            RetentionFacts facts = ended.Facts(index);
            ScoutStrategy strategy = new(
                opening,
                facts.SetupTraits,
                Math.Min(3, facts.PotionCount),
                facts.PersistentBuffValue / 8,
                facts.ReachableHandValue / 8,
                facts.FocusTargetCombatId);
            entries.Add(new(node, strategy, facts.Score));
        }
        ScoutEntry[][] openings = entries
            .GroupBy(entry => entry.Strategy)
            .SelectMany(group => group.OrderByDescending(entry => entry.Score).Take(2))
            .GroupBy(entry => entry.Strategy.Opening)
            .Select(group => group.OrderByDescending(entry => entry.Score).ToArray())
            .OrderByDescending(group => group[0].Score)
            .ToArray();
        List<ScoutCandidate> selected = [];
        for (int rank = 0; selected.Count < EarlyTurnCandidatesPerLayer; rank++)
        {
            bool found = false;
            foreach (ScoutEntry[] opening in openings)
            {
                if (rank >= opening.Length)
                    continue;
                found = true;
                ScoutEntry entry = opening[rank];
                selected.Add(new ScoutCandidate(Route(entry.Node), entry.Score)
                {
                    RootIndex = RootIndex(entry.Node),
                });
                if (selected.Count == EarlyTurnCandidatesPerLayer)
                    break;
            }
            if (!found)
                break;
        }
        _scoutLayers.Add(new ScoutLayer(completedTurns, selected));
    }

    /// <summary>
    /// The opening key of the existing <c>FirstAction</c>: the first action taken from the search root.
    /// The scout is only admitted for an empty fixed prefix, so its parent state is the battle root.
    /// </summary>
    private ScoutOpening CaptureScoutOpening(in Battle root, int node)
    {
        int first = node;
        while (_nodes[first].Parent >= 0)
            first = _nodes[first].Parent;
        PlayerAction action = _nodes[first].Action;
        ushort cardId = 0;
        ushort potionId = 0;
        uint? target = null;
        if (action.Kind == PlayerActionKind.PlayCard)
        {
            if (action.HandIndex < root.HandCount)
                cardId = root.Cards[action.HandIndex].Id;
        }
        else if (action.Kind == PlayerActionKind.UsePotion)
        {
            if (action.PotionSlot < root.PotionSlotCount)
                potionId = root.Potions[action.PotionSlot];
        }
        if (action.Target >= Limits.FirstEnemyOwner
            && action.Target - Limits.FirstEnemyOwner < root.EnemyCount)
        {
            target = root.Enemies[action.Target - Limits.FirstEnemyOwner].CombatId;
        }
        return new ScoutOpening(action.Kind, cardId, potionId, target);
    }

    /// <summary>
    /// The existing primary member's terminal-victory tightening
    /// (<c>TightenSearchIncumbentAtTurnLayer</c>): a complete, risk-free, potion-free victory
    /// publishes a strictly better incumbent at the commit boundary, so the same layer's retention can
    /// use it. The shared <see cref="PrimaryIncumbentTable"/> is not connected.
    /// </summary>
    private void TightenIncumbent(in Battle state, int node, in ScoreFeatures features, in RouteKey key)
    {
        // the existing search's TightenSearchIncumbentAtTurnLayer: only a potion-free victory, or an
        // exact audited layer with a baseline and no enforced directives, may establish the bound.
        bool canEstablishPotionFree = _options.MinimumPotionUses == 0
            && _effectivePotionPolicy is PotionUseMode.Disabled or PotionUseMode.Smart;
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
        SearchIncumbent? candidate = _incumbent;
        if (IncumbentPolicy.TryTightenIncumbent(
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
        SearchIncumbent? incumbent = _bound == null ? null : _incumbent;
        int? ceiling = _smartPotionEligibilityHpCeiling;
        if (ceiling is null && (incumbent is null || _bound is null))
            return;
        int write = 0;
        for (int read = 0; read < pool.Count; read++)
        {
            BoundValues values = _bound!.Evaluate(pool.State(read), pool.PotionCost(read),
                includeSmartPotionLowerBound: ceiling is not null);
            bool certifiedUnknown = _bound.RequireCertifiedHealingProof && _pruneEqualHp
                && values.RemainingHealingPotential == int.MaxValue;
            bool incumbentPrune = false;
            if (incumbent is { } bounded && !certifiedUnknown)
            {
                bool costClosed = !_preservePotionCostOpportunities
                    || (bounded.ExplicitPotionStrategicCost is { } cost
                        && values.ExplicitPotionStrategicCost >= cost);
                incumbentPrune = IncumbentPolicy.ShouldPruneByIncumbent(
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
            if (_trace != null)
            {
                int node = pool.Node(read);
                if (_trace.IsWatched(node))
                {
                    _trace.NodePruned(node, pool.TracePhase,
                        incumbentPrune ? "incumbent-lower-bound" : "smart-potion-ceiling");
                }
            }
        }
        pool.Truncate(write);
    }

    /// <summary>
    /// The node's final score and its intermediate ranking value. The final score is the ported
    /// <see cref="Scorer.Features"/> score; the ranking value is
    /// <see cref="Scorer.BeamRankScore"/> minus the strategic HP cost of the potions this route has
    /// spent. The potion term is Sts2Solver-specific: the existing snapshot score does not include potion cost,
    /// so without it the beam would spend potions for free.
    /// </summary>
    private (double Score, double Rank, double PureRank, int PotionCost, ScoreFeatures Features) Score(
        in Battle battle, int actionCount, bool terminal, bool isEndTurn)
    {
        // ReachableHandValue / ZeroCostPlayableCount feed only the retention comparators, not Score,
        // BeamRankScore or RouteKey. The retention pool computes them when a state is stored, which
        // skips duplicate and terminal children.
        ScoreFeatures features = _scorer.Features(battle, actionCount, includeRetentionOnly: false);
        // The plan representative seats keep the existing MaxBy(BeamRankScore) tie order, which does
        // not include Sts2Solver's potion-cost rank penalty. The two values differ only by that term.
        double pureRank = _scorer.BeamRankScore(battle, features, terminal, isEndTurn);
        // ExplicitPotionStrategicCost includes the fixed prefix and potions generated during the
        // search; the previous root-slot diff saw neither generated uses nor their identity costs.
        // An audited exact layer must spend its potions, so the Sts2Solver-specific "spending is free"
        // correction would rank the required line below states that have not spent them yet; the
        // final ordering still charges the cost. Non-audit members keep the correction.
        double rank = _auditPotionPolicy && _options.MinimumPotionUses > 1
            ? pureRank
            : pureRank - features.ExplicitPotionStrategicCost * ScoreWeights.Hp;
        return (features.Score, rank, pureRank, features.ExplicitPotionStrategicCost, features);
    }

    /// <summary>
    /// The route's realized plan payoff count up to this action, mirroring
    /// <c>PlanCommitment.CountRealizedPayoffs</c>: a matching card play on or after the payoff turn.
    /// The count starts at <see cref="BeamOptions.InitialPlanPayoffCount"/>, so prefix plays are
    /// included exactly as the existing search's node chain includes them.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int NextPayoffCount(int parentNode, in Battle parent, in PlayerAction action,
        in ScoreFeatures childFeatures, in RetentionFacts parentFacts, int commitment)
    {
        if (_planCommitment is not { } plan)
            return 0;
        int count = _nodes[parentNode].PayoffCount;
        switch (plan.PayoffKind)
        {
            case PlanPayoffKind.CardPlayed:
                if (parent.PlayerTurn < plan.EarliestTurn
                    || action.Kind != PlayerActionKind.PlayCard
                    || action.HandIndex >= parent.HandCount
                    || parent.Cards[action.HandIndex].Id != plan.PayoffCardId)
                {
                    return count;
                }
                return checked(count + 1);
            case PlanPayoffKind.FreePotionUsed:
                if (parent.PlayerTurn < plan.EarliestTurn
                    || action.Kind != PlayerActionKind.UsePotion
                    || action.PotionSlot >= parent.PotionSlotCount
                    || parent.Potions[action.PotionSlot] == 0
                    || parent.Potions[action.PotionSlot] - 1 != plan.PayoffPotionId
                    || childFeatures.PotionStrategicCost != parentFacts.PotionStrategicCost)
                {
                    return count;
                }
                return checked(count + 1);
            case PlanPayoffKind.RegisteredPowerBenefit:
                if (commitment <= 0 || childFeatures.Turn < plan.EarliestTurn)
                    return count;
                PowerCommitmentState state = _commitments[commitment - 1];
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

    private static bool CommitmentHasCard(in PowerCommitmentState state, int bit)
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
    private bool PotionAllowed(in Battle battle, int slot)
    {
        PotionPolicy policy = _options.Potions;
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
    private int MissingRootPotions(in Battle battle)
    {
        int used = 0;
        for (int slot = 0; slot < Limits.PotionSlots; slot++)
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
    private int ExplicitPotionUsesInState(in Battle battle)
    {
        int automaticAfterRoot = battle.Counter(Counter.AutomaticPotionUseCount)
            - _initialAutomaticPotionUses;
        return _initialExplicitPotionUses + MissingRootPotions(battle)
            + battle.Counter(Counter.GeneratedPotionUseCount) - automaticAfterRoot;
    }

    /// <summary>
    /// A route node's power-commitment metadata: the ported <c>PowerCommitment</c> without its string
    /// card list. Registered cards are indexed in the bridge catalog, so the played set is an exact
    /// 128-bit mask (87 registered powers today). This is route history and never enters
    /// <see cref="Battle"/>, the fingerprint or the state key.
    /// </summary>
    /// <summary>
    /// <c>the existing search's AttachPowerCommitment</c> over a Sts2Solver route node. Returns the 1-based
    /// <see cref="_commitments"/> index, or 0 when the child carries no commitment. A child whose parent
    /// has no commitment and that does not play a registered power returns immediately, so the lane
    /// costs nothing on ordinary transitions.
    /// </summary>
    private int AttachPowerCommitment(int parentNode, in Battle parent, in Battle child,
        in PlayerAction action, in RetentionFacts parentFacts, in ScoreFeatures childFeatures,
        int depth)
    {
        if (_powerCommitments is not { } catalog)
            return 0;
        int parentIndex = _nodes[parentNode].Commitment;
        PowerCommitmentState parentState = parentIndex > 0
            ? _commitments[parentIndex - 1]
            : default;
        bool powerPlay = action.Kind == PlayerActionKind.PlayCard
            && action.HandIndex < parent.HandCount;
        PowerCommitmentRule rule = default;
        if (powerPlay)
            powerPlay = TryRegisteredPower(parent, parent.Cards[action.HandIndex], catalog, out rule);
        if (!powerPlay && parentIndex == 0)
            return 0;

        int progress = 0, realized = 0;
        RetentionFacts childFacts = default;
        PowerEvidence.View childView = new(child, childFeatures, _rules, _scorer);
        ushort playedCardId = powerPlay && action.HandIndex < parent.HandCount
            ? parent.Cards[action.HandIndex].Id
            : ushort.MaxValue;
        if (parentIndex > 0)
        {
            (int reachableHandValue, int zeroCostPlayableCount, _) = _scorer.ReachableHandPotential(child);
            childFacts = RetentionFacts.From(childFeatures, depth,
                reachableHandValue, zeroCostPlayableCount);
            PowerEvidence.View parentView = new(parent, default, _rules, _scorer);
            progress = PowerEvidence.SilentProgress(parentState, parentView, childView);
            int specialized = PowerEvidence.SilentRealized(parentState,
                parentFacts.PlayerBlock, parentFacts.ProjectedPlayerHp, parentFacts.ReachableHandValue,
                parentFacts.Turn, childFacts.PlayerBlock, childFacts.ProjectedPlayerHp,
                childFacts.ReachableHandValue, childFacts.Turn, childFacts.AliveEnemyCount,
                parent, child, playedCardId, action.Kind == PlayerActionKind.PlayCard,
                childView, out bool handled);
            realized = handled
                ? specialized
                : GenericPowerCommitmentRealized(parentFacts, childFacts);
        }

        bool attached = false;
        PowerCommitmentState commitment = parentState;
        if (powerPlay)
        {
            int spentEnergy = Math.Max(0, parentFacts.Energy - childFeatures.Energy);
            int turnOffset = Math.Max(0, childFeatures.Turn - _initialPlayerTurn);
            int investment = PowerActivationInvestmentPolicy.EnergyInvestment(
                spentEnergy, catalog.TotalFloor, turnOffset);
            investment = SaturatingCommitmentAdd(investment,
                Math.Max(0, childFeatures.CumulativePlayerHpLost - parentFacts.CumulativePlayerHpLost));
            int setupGain = PowerCommitmentSetupGain(parentFacts, childFeatures);
            int projectedPotential = PowerEvidence.ProjectionPotential(playedCardId, childView, parent);
            int triggerFloor = PowerEvidence.TriggerProjectionFloor(playedCardId, childView);
            PowerRouteAdmissionResult admission = PowerRouteAdmission.Evaluate(new(
                CardId: Registry.Card(playedCardId).NativeId,
                IsAutoPlay: false,
                SpentEnergy: spentEnergy,
                RemainingEnergy: childFeatures.Energy,
                HasTriggerEvidence: PowerEvidence.HasTriggerEvidence(playedCardId, parent, childView),
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
            PowerCommitmentState? advanced = AdvancePowerCommitment(parentState,
                parent.PlayerTurn, child.PlayerTurn,
                _aggressivePowerCommitment ? 3 : 2, progress, realized);
            if (advanced is not { } next)
                return 0;
            commitment = next;
        }
        _commitments.Add(commitment);
        return _commitments.Count;
    }

    private bool TryRegisteredPower(in Battle battle, in Card card,
        PowerCommitmentCatalog catalog, out PowerCommitmentRule rule)
    {
        rule = default;
        return card.Id < catalog.Rules.Length
            && (rule = catalog.Rules[card.Id]).Family != PowerCommitmentFamily.None
            && Kernel.ResolveCardType(battle, _rules, card) == CardType.Power;
    }

    private static PowerCommitmentState AddPowerCommitment(
        in PowerCommitmentState commitment,
        in PowerCommitmentRule rule,
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
    private static PowerCommitmentState? AdvancePowerCommitment(
        in PowerCommitmentState commitment,
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
    private static int PowerCommitmentSetupGain(in RetentionFacts before, in ScoreFeatures after)
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
    /// <see cref="Scorer.Features"/> and checked field by field against the existing snapshot by K4,
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
        public Scorer.RetentionValues RetentionProjection()
            => new(ProjectedShuffleOrderValue, ProjectedShuffleOrderKeyFirst, ProjectedShuffleOrderKeySecond,
                CanTriggerArtOfWarNextTurn, EnemyCombatDistributionFirst, EnemyCombatDistributionSecond,
                EnemyControlDistributionFirst, EnemyControlDistributionSecond, UnorderedPileKeyFirst,
                UnorderedPileKeySecond);

        public static RetentionFacts From(in ScoreFeatures features, int depth,
            int reachableHandValue, int zeroCostPlayableCount, SearchRouteTraits traits = SearchRouteTraits.None,
            int pocketwatchCardThreshold = -1, int pocketwatchCardsPlayedThisTurn = 0,
            int pocketwatchCardsPlayedLastTurn = 0,
            int initialRetainedAttackValue = 0, int parentEnemyBlock = 0, int parentEnemyHp = 0,
            int parentFutureResourceValue = 0, int parentStrategicResourcePotential = 0,
            in Scorer.RetentionValues retention = default) => new(
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
            Math.Min(ScoreWeights.RetainedAttackGrowthBeamCap,
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
        private static int SetupLane(in ScoreFeatures features)
            => features.StrategicRetentionValue * 16
                + features.LatentSetupValue * 8
                + features.ReplayPotentialValue * 16
                + features.FutureResourceValue;

        /// <summary>
        /// BeamRetentionPolicy.FindBestSetup: Scaling + Resource + Control lanes (LaneValue in
        /// BeamRetentionPolicy.Ranking.cs). Every term is the existing formula; the resource lane's
        /// Stars and ReachableHandValue and the control lane's SandpitRemaining are K4-checked.
        /// </summary>
        private static int SetupCombo(in ScoreFeatures features, int reachableHandValue)
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
                    * Math.Min(ScoreWeights.VulnerableAttackWindowCap, features.RetainedAttackValue)
                + Math.Max(0, features.EnemyVulnerableTurns - features.FocusTargetVulnerableTurns)
                    * Math.Min(ScoreWeights.VulnerableAttackWindowCap, features.RetainedAttackValue)
                + features.DelayedDamageValue
                - features.LiveDeckClutter * 8;
    }

    /// <summary>
    /// K4 entry: the Sts2Solver route-trait delta for one transition, computed from the same feature
    /// projection the probe compares field by field. The reference side calls the existing
    /// <c>Classify*Traits</c>; <c>pure</c> and the PaelsEye declined flag are inputs the probe derives
    /// from the simulator history/relics for both sides.
    /// </summary>
    internal static SearchRouteTraits ClassifyRouteTraitsForTesting(
        in ScoreFeatures before, in ScoreFeatures after, PlayerAction action,
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
    /// potion generation, so those transitions are detected from the branch state the Sts2Solver kernel
    /// changed. The probe checks this input against the history-derived reference on every transition.
    /// </summary>
    internal static bool IsPureTransitionForTesting(in Battle before, in Battle after,
        in PlayerAction action, Rules rules, bool historyRisk)
        => IsPureTransition(before, after, action, rules, historyRisk);

    private static bool IsPureTransition(in Battle before, in Battle after, in PlayerAction action,
        Rules rules, bool historyRisk)
    {
        if (action.Kind != PlayerActionKind.PlayCard)
            return false;
        if (historyRisk)
            return false;
        if (action.ChoiceCount > 0)
            return false;
        // A card whose OnPlay mirror dispatch is inferred/unsupported records a prediction risk entry
        // on every play; the existing history-derived pure input is false for it.
        if (action.HandIndex >= before.HandCount)
            return false;
        Card played = before.Cards[action.HandIndex];
        if (rules.Card(played.Id, played.Upgrade).PlaysRecordRisk)
            return false;
        if (after.Counter(Counter.CardsDrawn) != before.Counter(Counter.CardsDrawn))
            return false;
        // Any CardGenerated/CardGenerationResolved entry is non-pure, including a card generated by
        // an enemy (the combat counter only counts the player's). A completed play keeps the total
        // card count unchanged (it moves hand -> play -> discard/exhaust), so a larger total is a
        // generated card.
        if (after.CardCount > before.CardCount)
            return false;
        if (after.Counter(Counter.CardsGeneratedThisCombat)
            != before.Counter(Counter.CardsGeneratedThisCombat))
        {
            return false;
        }
        if (after.Counter(Counter.OrbChannels) != before.Counter(Counter.OrbChannels))
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

    private static (ulong First, ulong Second) AfflictionMultiset(in Battle battle)
    {
        ulong first = 0, second = 0;
        for (int index = 0; index < battle.CardCount; index++)
        {
            ref readonly Card card = ref battle.Cards[index];
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
    /// <see cref="Battle"/>, the fingerprint or the state key.
    /// <para>
    /// The existing <c>pure</c> input is derived from the simulator history entry types
    /// (<c>CardPlayStarted</c>/<c>CardPlayFinished</c>/<c>CreatureAttacked</c>/<c>DamageReceived</c>).
    /// Sts2Solver route metadata has no history entry stream, so a card play is classified as not pure. That
    /// only widens the <see cref="SearchRouteTraits.Control"/> candidate set; the other terms of the same
    /// classification are exact feature comparisons.
    /// </para>
    /// </summary>
    private static SearchRouteTraits ClassifyRouteTraits(in RetentionFacts before,
        in ScoreFeatures after, in PlayerAction action, bool declinedExtraTurn, bool nonPure)
    {
        SearchRouteTraits traits = before.Traits;
        if (action.Kind == PlayerActionKind.PlayCard)
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
        else if (action.Kind == PlayerActionKind.UsePotion)
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

    private static bool OpensRevivalWindow(in RetentionFacts before, in ScoreFeatures after)
        => after.RevivingEnemyCount > before.RevivingEnemyCount
            || after.RawEnemyHp < before.RawEnemyHp && after.EnemyHp >= before.EnemyHp
            || (before.AliveEnemyMask & ~after.AliveEnemyMask) != 0
                && (after.AliveEnemyMask & ~before.AliveEnemyMask) != 0;

    /// <summary><c>SimulatedCombatState.TryGetPocketwatchState</c>: the first non-melted relic's threshold
    /// and cards played this turn. A root without the relic reports -1/0, exactly like the snapshot.</summary>
    private void PocketwatchState(in Battle battle, out int threshold,
        out int cardsPlayedThisTurn, out int cardsPlayedLastTurn)
        => PocketwatchState(battle, _rules, out threshold, out cardsPlayedThisTurn, out cardsPlayedLastTurn);

    internal static void PocketwatchState(in Battle battle, Rules rules,
        out int threshold, out int cardsPlayedThisTurn)
        => PocketwatchState(battle, rules, out threshold, out cardsPlayedThisTurn, out _);

    /// <summary>
    /// <c>SimulatedCombatState.TryGetPocketwatchState</c>: the first non-melted relic's threshold,
    /// this turn's plays (Counter) and last turn's plays (Aux; the bridge projects the engine's
    /// StatefulRelicState Previous into Aux).
    /// </summary>
    internal static void PocketwatchState(in Battle battle, Rules rules,
        out int threshold, out int cardsPlayedThisTurn, out int cardsPlayedLastTurn)
    {
        threshold = -1;
        cardsPlayedThisTurn = 0;
        cardsPlayedLastTurn = 0;
        ushort id = Registry.PocketwatchRelicId;
        if (id == 0)
            return;
        for (int index = 0; index < battle.RelicCount; index++)
        {
            ref readonly Relic relic = ref battle.Relics[index];
            if (relic.Id != id || (relic.Flags & RelicFlags.Melted) != 0)
                continue;
            threshold = (int)rules.RelicVar(id, 0);
            cardsPlayedThisTurn = relic.Counter;
            cardsPlayedLastTurn = relic.Aux;
            return;
        }
    }

    /// <summary><c>SimulatedCombatState.IsPaelsEyeUnused</c>: the relic exists, is not melted and its
    /// stateful counter is still zero.</summary>
    internal static bool DeclinedExtraTurn(in Battle battle)
    {
        ushort id = Registry.PaelsEyeRelicId;
        if (id == 0)
            return false;
        for (int index = 0; index < battle.RelicCount; index++)
        {
            ref readonly Relic relic = ref battle.Relics[index];
            if (relic.Id == id && (relic.Flags & RelicFlags.Melted) == 0 && relic.Counter == 0)
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
    /// accumulated route traits contain the lane; Beam does not model route traits, so the
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
                    * Math.Min(ScoreWeights.VulnerableAttackWindowCap, facts.RetainedAttackValue)
                + Math.Max(0, facts.EnemyVulnerableTurns - facts.FocusTargetVulnerableTurns)
                    * Math.Min(ScoreWeights.VulnerableAttackWindowCap, facts.RetainedAttackValue)
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
        private readonly SearchStorage? _storage;

        public ChunkedArray(int maxCapacity, SearchStorage? storage = null)
        {
            _storage = storage;
            int chunks = (maxCapacity + ChunkSize - 1) >> Shift;
            _chunks = storage?.RentArray<T[]?>(chunks) ?? new T[]?[chunks];
        }

        public ref T this[int index]
        {
            get
            {
                int chunkIndex = index >> Shift;
                T[] chunk = _chunks[chunkIndex] ??= CreateChunk();
                return ref chunk[index & ChunkMask];
            }
        }

        private T[] CreateChunk()
            => _storage?.RentChunk<T>(ChunkSize) ?? new T[ChunkSize];

        private static int ComputeShift()
        {
            int size = Unsafe.SizeOf<T>();
            int shift = 0;
            while ((1 << shift) * size < 85_000)
                shift++;
            // Battle is 12,600 bytes. 64 states (806,400 bytes) halve the LOH object count of
            // the 32-state variant while remaining small enough to place as one block.
            if (typeof(T) == typeof(Battle) && shift < 6)
                shift = 6;
            return shift;
        }
    }

}
