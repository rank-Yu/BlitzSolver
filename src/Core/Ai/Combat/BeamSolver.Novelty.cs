using System.Diagnostics;
using System.Runtime.CompilerServices;
using Sts2Solver.Simulation.Combat;
using Sts2Solver.Ai.Combat.Novelty;
using Sts2Solver.Ai.Combat.Policy;
using Sts2Solver.Ai.Combat.Quality;
using Sts2Solver.Ai.Combat.Scoring;

namespace Sts2Solver.Ai.Combat;

/// <summary>
/// The Bfws novelty scheduler of <c>the existing search's RunNoveltyOpen</c>, over <see cref="Battle"/>.
/// Novelty facts, the open queue and the escape budgets are route/search metadata: they never enter
/// the battle state, its fingerprint or a state key.
/// </summary>
internal sealed record NoveltyOptions(
    int Width,
    int FamiliarAllowance,
    int MaxOpen,
    int MaxNoveltyEntries,
    int MaxActions,
    int MaxTurns)
{
    public static NoveltyOptions From(NoveltySearchOptions options)
    {
        options.Validate();
        return new(options.Width, options.FamiliarAllowance, options.MaxOpen,
            options.MaxNoveltyEntries, options.MaxActions, options.MaxTurns);
    }
}

internal sealed partial class BeamSolver
{
    private readonly List<BfwsFact> _noveltyFacts = [];
    private readonly List<(RouteKey Key, int Node)> _noveltyCandidateRoots = [];
    private BfwsPackedNovelty? _noveltyPacked;
    private BfwsBoundedOpen<NoveltyOpenEntry>? _noveltyOpen;
    private long _noveltySequence;
    private int _noveltyGenerated;
    private int _noveltyPeakOpen;
    private int _noveltyHorizonLeaves;
    private int _noveltyNonNovelPruned;
    private int _noveltyFamiliarAdmitted;
    private int _noveltyOpenDropped;
    private int _noveltyWins;
    private long? _noveltyFirstVictoryMs;
    private int? _noveltyFirstVictoryExpanded;
    private int? _noveltyMinimumLoss;
    private int? _noveltyMinimumLossExpanded;
    private long? _noveltyMinimumLossMs;
    private string _noveltyStop = "not_started";
    private readonly List<NoveltySearchImprovement> _noveltyImprovements = [];
    private int _noveltyMaximumTurn;
    private bool _noveltyTargetReached;
    private NoveltyFallback? _noveltyFallback;
    private RouteKey? _noveltyBest;
    private int _noveltyBestNode;
    private bool _noveltyBestVictory;
    private bool _noveltyHasTerminal;

    private readonly record struct NoveltyOpenEntry(
        Battle State, int Node, BfwsEscapeBudget? Escape, double Score, int Turn, int Depth);

    private readonly record struct NoveltyFallback(Battle State, int Node, double Score);

    private void ObserveNoveltyChild(in Battle child, int node, in ScoreFeatures features)
    {
        _noveltyGenerated++;
        _noveltyMaximumTurn = Math.Max(_noveltyMaximumTurn, features.Turn);
        if (_noveltyFallback is not { } fallback || features.Score > fallback.Score)
            _noveltyFallback = new(child, node, features.Score);
        bool victory = features.AllEnemiesDead && !features.PlayerDead && !features.HasRisk
            && child.Outcome != Outcome.EventDefeat;
        if (!victory)
            return;
        _noveltyWins++;
        _noveltyFirstVictoryMs ??= _solveWatch?.ElapsedMilliseconds ?? 0;
        _noveltyFirstVictoryExpanded ??= _expanded;
        if (_noveltyMinimumLoss is { } previous && previous <= features.CumulativePlayerHpLost)
            return;
        _noveltyMinimumLoss = features.CumulativePlayerHpLost;
        _noveltyMinimumLossExpanded = _expanded;
        _noveltyMinimumLossMs = _solveWatch?.ElapsedMilliseconds ?? 0;
        _noveltyImprovements.Add(new(_noveltyMinimumLossMs.Value, _expanded,
            features.CumulativePlayerHpLost, features.PotionUseCount, features.Turn));
    }

    private void ObserveNoveltySeed(in Battle state, int node, in ScoreFeatures features)
    {
        _noveltyMaximumTurn = Math.Max(_noveltyMaximumTurn, features.Turn);
        if (_noveltyFallback is not { } fallback || features.Score > fallback.Score)
            _noveltyFallback = new(state, node, features.Score);
    }

    /// <summary>The existing seed-terminal <c>NoveltyFor(seed)</c>: record the facts without queueing.</summary>
    private int EvaluateNoveltyState(in Battle state, in ScoreFeatures features)
    {
        _noveltyFacts.Clear();
        NoveltyFacts.Capture(state, features, _noveltyFacts);
        return _noveltyPacked!.Evaluate((features.EnemyHp / 10, features.PotionUseCount), _noveltyFacts);
    }

    private void EnqueueNovelty(in Battle state, int node, BfwsEscapeBudget? parentEscape,
        bool initialSeed, double score, int turn, int depth, NoveltyOptions noveltyOptions)
    {
        BfwsPackedNovelty novelty = _noveltyPacked
            ?? throw new InvalidOperationException("新颖度队列未初始化。");
        ScoreFeatures features = _scorer.Features(state, depth, includeRetentionOnly: true);
        int width = EvaluateNoveltyState(state, features);
        if (!initialSeed && width > noveltyOptions.Width)
        {
            if (parentEscape?.TryAdmit() != true)
            {
                _noveltyNonNovelPruned++;
                return;
            }
            _noveltyFamiliarAdmitted++;
        }
        BfwsEscapeBudget? escape = noveltyOptions.FamiliarAllowance > 0
            ? initialSeed || width <= noveltyOptions.Width
                ? new(noveltyOptions.FamiliarAllowance) : parentEscape
            : null;
        (double, double, long) rank = ((double)width, -score, _noveltySequence++);
        BfwsBoundedOpen<NoveltyOpenEntry> open = _noveltyOpen
            ?? throw new InvalidOperationException("新颖度 OPEN 未初始化。");
        if (open.Enqueue(new(state, node, escape, score, turn, depth), rank, out _))
            _noveltyOpenDropped++;
        _noveltyPeakOpen = Math.Max(_noveltyPeakOpen, open.Count);
    }

    private void ConsiderNoveltyTerminal(in Battle state, int node, in ScoreFeatures features,
        in RouteKey key)
    {
        _noveltyHasTerminal = true;
        if (_auditPotionPolicy)
            ConsiderAudited(node, features, key);
        else if (_noveltyBest is not { } current || CompareRoutes(key, current) < 0)
        {
            _noveltyBest = key;
            _noveltyBestNode = node;
            _noveltyBestVictory = state.Outcome == Outcome.Victory;
            _noveltyBestFeatures = features;
        }
        TightenIncumbent(state, node, features, key);
    }

    private BeamResult SolveNovelty(IReadOnlyList<Battle> roots, NoveltyOptions noveltyOptions,
        Stopwatch watch, CancellationToken cancellationToken)
    {
        if (roots.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(roots), "至少需要一个搜索根。");
        _singleRoot = roots.Count == 1;
        _cancellationToken = cancellationToken;
        _solveWatch = watch;
        ResetProgressState();
        for (int slot = 0; slot < Limits.PotionSlots; slot++)
            _rootPotions[slot] = slot < roots[0].PotionSlotCount ? roots[0].Potions[slot] : (ushort)0;
        _noveltyPacked = new BfwsPackedNovelty(noveltyOptions.Width, noveltyOptions.MaxNoveltyEntries);
        _noveltyOpen = new BfwsBoundedOpen<NoveltyOpenEntry>(noveltyOptions.MaxOpen);
        _storage.Memory.Charge((long)noveltyOptions.MaxNoveltyEntries * 32, "novelty-packed");
        _storage.Memory.Charge(
            (long)noveltyOptions.MaxOpen * (Unsafe.SizeOf<NoveltyOpenEntry>() + 64), "novelty-open");
        _noveltyStop = "open_exhausted";
        _noveltyMaximumTurn = _initialPlayerTurn;
        _noveltySequence = 0;
        _noveltyGenerated = 0;
        _noveltyPeakOpen = 0;
        _noveltyHorizonLeaves = 0;
        _noveltyNonNovelPruned = 0;
        _noveltyFamiliarAdmitted = 0;
        _noveltyOpenDropped = 0;
        _noveltyWins = 0;
        _noveltyFirstVictoryMs = null;
        _noveltyFirstVictoryExpanded = null;
        _noveltyMinimumLoss = null;
        _noveltyMinimumLossExpanded = null;
        _noveltyMinimumLossMs = null;
        _noveltyImprovements.Clear();
        _noveltyFallback = null;
        _noveltyBest = null;
        _noveltyBestNode = 0;
        _noveltyBestVictory = false;
        _noveltyHasTerminal = false;
        _routingSignatureIds.Clear();
        int width = Math.Max(1, _options.BeamWidth);
        int frontierCapacity = Math.Max(width, roots.Count);
        int maxChildren = checked(frontierCapacity * (Kernel.MaxLegalActions + 1));
        int chunkStates = ChunkedArray<Battle>.ChunkSize;
        int budgetStates = EffectivePoolStateBudgetBytes / Unsafe.SizeOf<Battle>()
            / chunkStates * chunkStates;
        int poolLimit = Math.Min(maxChildren, Math.Max(chunkStates, budgetStates));
        // A novelty parent expands every legal successor at once (choice branches multiply), so its
        // scratch pools are not bounded by the beam. They are cleared after each parent and therefore
        // only need room for one expansion batch.
        int noveltyPoolLimit = Math.Min(Math.Max(poolLimit, noveltyOptions.MaxOpen * 2), budgetStates);
        _endedCompactionTarget = noveltyPoolLimit;
        int initialChildren = Math.Min(noveltyPoolLimit, Math.Max(256, width * 8));
        bool preserveTheft = _theftPolicy == TheftPolicy.PreserveResources;
        int bandWidth = _options.SecondRankBandWidth > 0 ? _options.SecondRankBandWidth : width;
        bool checkPotionQuota = _effectivePotionPolicy != PotionUseMode.Disabled;
        Pool next = new(initialChildren, noveltyPoolLimit, preserveTheft, _scorer, _planCommitment,
            _options.SecondRankBand, bandWidth, _aggressivePowerCommitment, _powerCommitments, _commitments,
            _nodes, _routingSignatures, checkPotionQuota, _options.InitialActionCount,
            _options.PreserveReplayAllocatorOpening, trace: null, _storage);
        Pool ended = new(initialChildren, noveltyPoolLimit, preserveTheft, _scorer, _planCommitment,
            _options.SecondRankBand, bandWidth, _aggressivePowerCommitment, _powerCommitments, _commitments,
            _nodes, _routingSignatures, checkPotionQuota, _options.InitialActionCount,
            _options.PreserveReplayAllocatorOpening, trace: null, _storage);

        void Terminal(in Battle state, int node, double score)
        {
            int actionCount = Depth(node);
            ScoreFeatures features = _scorer.Features(state, actionCount, includeRetentionOnly: false);
            ConsiderNoveltyTerminal(state, node, features, Key(state, features, actionCount));
            if (keyWon(features, actionCount))
                _noveltyTargetReached |= MeetsNoveltyHpTarget(state, node, features);
        }
        TerminalSink terminal = Terminal;

        for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            Battle root = roots[rootIndex];
            (double rootScore, _, _, _, ScoreFeatures rootFeatures) =
                Score(root, _options.InitialActionCount, terminal: false, isEndTurn: false);
            _nodes.Add(new RouteNode(-1, Math.Max(0, _options.InitialActionCount), default, 0, 0,
                _options.InitialPlanPayoffCount, ushort.MaxValue, ushort.MaxValue, int.MinValue, 0,
                0, int.MinValue, 0, 0, 0, int.MaxValue, 0d, 0, 0, 0), 0);
            bool terminalRoot = !Kernel.IsInProgress(root);
            ObserveNoveltySeed(root, rootIndex, rootFeatures);
            if (terminalRoot)
            {
                _ = EvaluateNoveltyState(root, rootFeatures);
                ConsiderNoveltyTerminal(root, rootIndex, rootFeatures,
                    Key(root, rootFeatures, _options.InitialActionCount));
                _noveltyTargetReached |= MeetsNoveltyHpTarget(root, rootIndex, rootFeatures);
                continue;
            }
            EnqueueNovelty(root, rootIndex, null, initialSeed: true, rootScore,
                rootFeatures.Turn, _options.InitialActionCount, noveltyOptions);
        }
        if (_noveltyTargetReached)
            _noveltyStop = "hp_target";
        PublishProgress(0, 0, _noveltyOpen?.Count ?? 0, 0, "初始化", force: true, routeNode: -1);
        try
        {
        while (_noveltyOpen!.Count > 0 && !_noveltyTargetReached
            && _takeoverAction == TakeoverAction.None)
        {
            SearchCheckpoint();
            if (TryHandleTakeover(ended))
                break;
            if (!NodeBudgetAvailable)
            {
                _noveltyStop = "node_limit";
                break;
            }
            if (watch.Elapsed >= _options.SoftTimeBudget)
            {
                _noveltyStop = "time_limit";
                break;
            }
            NoveltyOpenEntry entry = _noveltyOpen.Dequeue();
            if (entry.Depth >= noveltyOptions.MaxActions
                || entry.Turn >= _initialPlayerTurn + noveltyOptions.MaxTurns)
            {
                _noveltyHorizonLeaves++;
                continue;
            }
            Battle parent = entry.State;
            ScoreFeatures parentFeatures =
                _scorer.Features(parent, entry.Depth, includeRetentionOnly: true);
            _noveltyFacts.Clear();
            NoveltyFacts.Capture(parent, parentFeatures, _noveltyFacts);
            _noveltyPacked.PrepareParent((parentFeatures.EnemyHp / 10, parentFeatures.PotionUseCount),
                _noveltyFacts);
            (int reachableHandValue, int zeroCostPlayableCount, _) = _scorer.ReachableHandPotential(parent);
            RetentionFacts parentFacts = RetentionFacts.From(parentFeatures, entry.Depth,
                reachableHandValue, zeroCostPlayableCount, initialRetainedAttackValue: _scorer.InitialRetainedAttackValue);
            next.Clear();
            ended.Clear();
            if (_expanded < int.MaxValue) _expanded++;
            int count = Kernel.LegalActions(parent, _rules, _actions);
            for (int actionIndex = 0; actionIndex < count; actionIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (_actions[actionIndex].Kind == PlayerActionKind.UsePotion
                    && !PotionAllowed(parent, _actions[actionIndex].PotionSlot))
                {
                    continue;
                }
                ExpandAction(ref parent, entry.Node, _actions[actionIndex], next, ended, terminal,
                    parentFacts, 0, parentFeatures.Score);
            }
            for (int index = 0; index < next.Count; index++)
            {
                int node = next.Node(index);
                EnqueueNovelty(next.State(index), node, entry.Escape, initialSeed: false,
                    next.Score(index), next.State(index).PlayerTurn, Depth(node), noveltyOptions);
            }
            for (int index = 0; index < ended.Count; index++)
            {
                int node = ended.Node(index);
                EnqueueNovelty(ended.State(index), node, entry.Escape, initialSeed: false,
                    ended.Score(index), ended.State(index).PlayerTurn, Depth(node), noveltyOptions);
            }
            PublishProgress(Math.Max(0, _noveltyMaximumTurn - _initialPlayerTurn),
                entry.Depth, _noveltyOpen.Count, ended.Count, "探索不同路线", force: false,
                routeNode: NoveltyPreviewNode());
            if (TryHandleTakeover(ended))
                break;
            if (_nodes.Count >= _nodeCleanupThreshold)
            {
                int preview = -1;
                CompactRouteNodes(_noveltyCandidateRoots, ref preview, null, next, ended, null);
                _nodeCleanupThreshold = Math.Max(_nodeCleanupThreshold, _nodes.Count * 2);
            }
            if (_noveltyPacked.LimitReached)
            {
                _noveltyStop = "novelty_limit";
                break;
            }
        }
        }
        catch (SearchMemoryBudgetException error)
        {
            _memoryBudgetExceeded = true;
            _memoryBudgetExceededAtBytes = error.UsedBytes;
            _noveltyStop = "memory_budget";
        }
        PublishProgress(Math.Max(0, _noveltyMaximumTurn - _initialPlayerTurn), 0,
            _noveltyOpen?.Count ?? 0, 0, "复核最终候选", force: true,
            routeNode: NoveltyPreviewNode());
        if (_takeoverAction != TakeoverAction.None)
            return TakeoverResult(Math.Max(0, _noveltyMaximumTurn - _initialPlayerTurn));
        int turnLayers = Math.Max(0, _noveltyMaximumTurn - _initialPlayerTurn);
        int bestNode = _noveltyBestNode;
        bool bestVictory = _noveltyBestVictory;
        bool auditedSelected = true;
        if (_auditPotionPolicy)
        {
            auditedSelected = SelectAudited(out int auditedNode, out RouteKey auditedKey,
                out bool auditedVictory);
            if (auditedSelected)
            {
                _noveltyBest = auditedKey;
                bestNode = auditedNode;
                bestVictory = auditedVictory;
            }
        }
        else if (!_noveltyHasTerminal && _noveltyFallback is { } fallback)
        {
            ScoreFeatures fallbackFeatures =
                _scorer.Features(fallback.State, Depth(fallback.Node), includeRetentionOnly: false);
            _noveltyBest = Key(fallback.State, fallbackFeatures, Depth(fallback.Node));
            bestNode = fallback.Node;
            bestVictory = fallback.State.Outcome == Outcome.Victory;
        }
        NoveltySearchTelemetry telemetry = new(
            _noveltyGenerated, _noveltyPeakOpen, _noveltyHorizonLeaves, _noveltyNonNovelPruned,
            _noveltyFamiliarAdmitted, _noveltyOpenDropped, _noveltyWins, _noveltyFirstVictoryMs,
            _noveltyFirstVictoryExpanded, _noveltyMinimumLoss, _noveltyMinimumLossExpanded,
            _noveltyMinimumLossMs, _noveltyStop, _noveltyPacked.Entries, _noveltyPacked.AtomCount,
            _noveltyPacked.Partitions, _noveltyPacked.Unary, _noveltyPacked.Binary,
            _noveltyPacked.Familiar, _noveltyImprovements.ToArray());
        BeamResult result = new(
            _noveltyBest is null ? [] : Route(bestNode),
            [],
            bestVictory, _noveltyBest?.Score ?? double.NegativeInfinity,
            _expanded, _transitions, _duplicates, turnLayers, _pruned, watch.Elapsed)
        {
            CapacityPruned = _capacityPruned,
            CapacityDroppedChildren = _capacityDropped.Count == 0
                ? BeamResult.NoCapacityDrops
                : _capacityDropped,
            EndedCompactions = _endedCompactions,
            EndedCompressedStates = _endedCompressedStates,
            SmartPotionPruned = _smartPruned,
            PotionPolicySatisfied = auditedSelected,
            BestRootIndex = bestNode >= 0 && bestNode < _nodes.Count ? RootIndex(bestNode) : 0,
            NoveltyTelemetry = telemetry,
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
            RouteNodeStorageBytes = _nodes.StorageBytes,
            RouteNodeCount = _nodes.Count,
            RouteNodePeakCount = _nodes.PeakCount,
        };
        return result;

        bool keyWon(in ScoreFeatures features, int actionCount)
            => InterimResultOrdering.IsCompleteVictory(actionCount, features.AllEnemiesDead,
                features.PlayerDead, features.ProjectedPlayerHp);
    }

    /// <summary>
    /// The advertised novelty route: the best terminal candidate, otherwise the best-scoring
    /// fallback state (the existing search publishes its completed pool, which is a superset of
    /// this fallback). Returns -1 before any candidate exists.
    /// </summary>
    private int NoveltyPreviewNode()
    {
        if (_noveltyBest is not null && _noveltyBestNode >= 0)
            return _noveltyBestNode;
        return _noveltyFallback?.Node ?? -1;
    }

    /// <summary>
    /// <c>the existing search's MeetsHpTarget</c> reduced to the same values the Sts2Solver terminal sink uses.
    /// The target was frozen by the bridge from the request's policy and battle-damage snapshot.
    /// </summary>
    private bool MeetsNoveltyHpTarget(in Battle state, int node, in ScoreFeatures features)
    {
        if (_options.HpTargetStop is not { } stop)
            return false;
        if (!InterimResultOrdering.IsCompleteVictory(Depth(node), features.AllEnemiesDead,
                features.PlayerDead, features.ProjectedPlayerHp))
        {
            return false;
        }
        if (!_scorer.GrowthTargetsSatisfied(features) || !_scorer.RelicTargetsSatisfied(features))
            return false;
        if (features.ProjectedDeathSaveUseCount != 0)
            return false;
        int explicitPotionUses = ExplicitPotionUses(node);
        if (explicitPotionUses < stop.RequiredPotionUses || explicitPotionUses > stop.EarlyStopPotionUses)
            return false;
        if (stop.EnforceForcedPotionDirectives && !EvaluateForcedUses(node, out _, out _, out _))
            return false;
        return stop.HpLostSoFar + features.CumulativePlayerHpLost <= stop.AcceptableBattleHpLoss;
    }
}
