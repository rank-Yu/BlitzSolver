using BlitzSolver.Engine;
using BlitzSolver.Search.Scoring;

namespace BlitzSolver.Search;

/// <summary>
/// The takeover requests a Blitz search honors at the same checkpoints as the existing search
/// (<c>Phases.cs</c>: before a novelty parent, before every play depth, after every
/// turn layer). The bridge maps <see cref="BlitzSolver.Search.SearchTakeoverKind"/> to this enum so the
/// solver never reads Runtime interaction state directly.
/// </summary>
internal enum BlitzTakeoverAction
{
    None,
    ApplyCurrentTurn,
    AdoptRoute,
}

/// <summary>
/// One progress snapshot of a running Blitz search. <see cref="Route"/> is the currently advertised
/// route from the search root (a fixed prefix is added by the bridge), and
/// <see cref="CandidateVersion"/> changes only when that route changes.
/// </summary>
internal sealed record BlitzProgressSnapshot(
    int CurrentTurnNumber,
    int CompletedTurnLayers,
    int PlayDepth,
    int ExpandedNodes,
    int Transitions,
    int MaxNodes,
    int FrontierNodes,
    int EndedNodes,
    long ElapsedMilliseconds,
    string Phase,
    int CandidateVersion,
    int RootIndex,
    BlitzAction[] Route,
    bool HasRoute,
    int ActionCount = 0,
    BlitzScoreFeatures Features = default,
    bool HasFeatures = false);

/// <summary>
/// Progress reporting, takeover polling, cancellation and frame-pressure checkpoints. These are
/// attached to the layer/expansion loops in the same places the existing search reads
/// <c>_interaction?.CurrentTakeoverRequest</c> and publishes <c>SolverProgress</c>.
/// </summary>
internal sealed partial class BlitzBeamSolver
{
    private const int ProgressIntervalMilliseconds = 100;
    private long _lastProgressMilliseconds = -100;
    private int _lastProgressRouteNode = int.MinValue;
    private int _progressCandidateVersion;
    private BlitzAction[] _publishedProgressRoute = [];
    private int _publishedProgressRootIndex;
    private BlitzTakeoverAction _takeoverAction;
    private int _takeoverNode = -1;
    private RouteKey _takeoverKey;
    private bool _takeoverVictory;
    private double _takeoverScore;
    private BlitzScoreFeatures _bestFeatures;
    private int _bestFeaturesNode = -1;
    private BlitzScoreFeatures _noveltyBestFeatures;

    /// <summary>
    /// The cancellation/frame-pressure checkpoint the existing search performs around each parent and
    /// replay step. <see cref="SearchWorkPacer.YieldIfNeeded"/> internally rate-limits its work slice;
    /// the cancellation check runs on every call so a cancel is observed inside one action loop.
    /// </summary>
    private void SearchCheckpoint()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        _options.Pacer?.YieldIfNeeded();
    }

    private BlitzTakeoverAction PollTakeover()
        => _options.PollTakeover?.Invoke() ?? BlitzTakeoverAction.None;

    private void ResetProgressState()
    {
        _lastProgressMilliseconds = -100;
        _lastProgressRouteNode = int.MinValue;
        _progressCandidateVersion = 0;
        _publishedProgressRoute = [];
        _publishedProgressRootIndex = 0;
        _takeoverAction = BlitzTakeoverAction.None;
        _takeoverNode = -1;
        _takeoverKey = default;
        _takeoverVictory = false;
        _takeoverScore = double.NegativeInfinity;
        _bestFeatures = default;
        _bestFeaturesNode = -1;
        _noveltyBestFeatures = default;
    }

    /// <summary>
    /// Reads the pending takeover request at a checkpoint. <c>AdoptRoute</c> always stops the search:
    /// the bridge materializes the exact seed the player requested. <c>ApplyCurrentTurn</c> stops only
    /// when a current-turn boundary candidate exists, matching the existing search's
    /// <c>currentBestNode ?? currentTurnCandidateNode</c> gate. Returns true when the search must stop.
    /// </summary>
    private bool TryHandleTakeover(Pool? currentTurnBoundary)
    {
        BlitzTakeoverAction action = PollTakeover();
        if (action == BlitzTakeoverAction.None)
            return false;
        if (action == BlitzTakeoverAction.AdoptRoute)
        {
            _takeoverAction = BlitzTakeoverAction.AdoptRoute;
            return true;
        }
        int candidate = SelectCurrentTurnCandidate(currentTurnBoundary);
        if (candidate < 0)
            return false;
        _takeoverAction = BlitzTakeoverAction.ApplyCurrentTurn;
        _takeoverNode = candidate;
        return true;
    }

    /// <summary>
    /// The best surviving end-turn state of the current root turn. The existing search's
    /// <c>ConsiderCurrentTurnCandidate</c> keeps the best final-ordering key among current-turn
    /// boundaries; Blitz scans the boundary pool only when a takeover is actually requested, so the
    /// ordinary search pays nothing for this bookkeeping.
    /// </summary>
    private int SelectCurrentTurnCandidate(Pool? boundary)
    {
        if (boundary == null)
            return -1;
        int selected = -1;
        RouteKey selectedKey = default;
        for (int index = 0; index < boundary.Count; index++)
        {
            int node = boundary.Node(index);
            if (node < 0 || Depth(node) == 0)
                continue;
            // A first-layer pool holds the search roots (prefix-only routes); they are not turn
            // boundaries and must not be adopted as a current-turn route.
            if (_nodes[node].Parent < 0)
                continue;
            ref BlitzBattle state = ref boundary.State(index);
            if (state.Outcome == BlitzOutcome.Defeat)
                continue;
            if (ExplicitPotionUses(node) < _options.MinimumPotionUses)
                continue;
            if (_options.EnforceForcedPotionDirectives
                && !EvaluateForcedUses(node, out _, out _, out _))
            {
                continue;
            }
            BlitzScoreFeatures features =
                _scorer.Features(state, Depth(node), includeRetentionOnly: false);
            if (features.PlayerDead || features.ProjectedPlayerHp <= 0)
                continue;
            RouteKey key = Key(state, features, Depth(node));
            if (selected < 0 || CompareRoutes(key, selectedKey) < 0)
            {
                selected = node;
                selectedKey = key;
            }
        }
        if (selected < 0)
            return -1;
        _takeoverKey = selectedKey;
        ref BlitzBattle selectedState = ref FindBoundaryState(boundary, selected);
        _takeoverVictory = selectedState.Outcome == BlitzOutcome.Victory;
        _takeoverScore = _takeoverKey.Score;
        return selected;
    }

    private static ref BlitzBattle FindBoundaryState(Pool boundary, int node)
    {
        for (int index = 0; index < boundary.Count; index++)
        {
            if (boundary.Node(index) == node)
                return ref boundary.State(index);
        }
        throw new InvalidOperationException($"当前回合候选节点 {node} 已不在边界池中。");
    }

    /// <summary>
    /// Publishes progress at the existing search's cadence (100 ms throttle, forced at layer
    /// boundaries). The route array is rebuilt only when the advertised node changes; the bridge
    /// throttles its expensive replay-based preview separately.
    /// </summary>
    private void PublishProgress(int completedTurnLayers, int playDepth, int frontierNodes,
        int endedNodes, string phase, bool force, int routeNode)
        => PublishProgressCore(completedTurnLayers, playDepth, frontierNodes, endedNodes, phase,
            force, routeNode, bestNode: -1, bestKey: null, previewPool: null);

    /// <summary>
    /// Progress publication that also compares the live frontier with the best terminal candidate.
    /// The frontier scan only runs when a progress event is actually published; callers on hot paths
    /// must not precompute it.
    /// </summary>
    private void PublishProgressWithPreview(int completedTurnLayers, int playDepth, int frontierNodes,
        int endedNodes, string phase, bool force, int bestNode, RouteKey? bestKey, Pool previewPool)
        => PublishProgressCore(completedTurnLayers, playDepth, frontierNodes, endedNodes, phase,
            force, routeNode: -1, bestNode, bestKey, previewPool);

    private void PublishProgressCore(int completedTurnLayers, int playDepth, int frontierNodes,
        int endedNodes, string phase, bool force, int routeNode, int bestNode, RouteKey? bestKey,
        Pool? previewPool)
    {
        Action<BlitzProgressSnapshot>? callback = _options.Progress;
        if (callback == null)
            return;
        long elapsed = _solveWatch?.ElapsedMilliseconds ?? 0;
        if (!force && elapsed - _lastProgressMilliseconds < ProgressIntervalMilliseconds)
            return;
        _lastProgressMilliseconds = elapsed;
        BlitzScoreFeatures features = default;
        bool hasFeatures = false;
        if (previewPool != null)
        {
            routeNode = SelectPreviewNode(bestNode, bestKey, previewPool, out features,
                out hasFeatures);
        }
        else if (routeNode < 0)
        {
            routeNode = bestNode;
        }
        if (!hasFeatures && routeNode >= 0 && routeNode == _bestFeaturesNode)
        {
            features = _bestFeatures;
            hasFeatures = true;
        }
        if (!hasFeatures && routeNode >= 0 && _noveltyBest is not null
            && routeNode == _noveltyBestNode)
        {
            features = _noveltyBestFeatures;
            hasFeatures = true;
        }
        bool hasRoute = routeNode >= 0;
        if (hasRoute && routeNode != _lastProgressRouteNode)
        {
            _publishedProgressRoute = Route(routeNode);
            _lastProgressRouteNode = routeNode;
            _publishedProgressRootIndex = RootIndex(routeNode);
            _progressCandidateVersion++;
        }
        Action<BlitzProgressSnapshot> publish = callback;
        publish(new BlitzProgressSnapshot(
            _options.InitialPlayerTurn + completedTurnLayers,
            completedTurnLayers,
            playDepth,
            _expanded,
            _transitions,
            _options.MaxExpandedNodes,
            frontierNodes,
            endedNodes,
            elapsed,
            $"搜索·{phase}",
            _progressCandidateVersion,
            _publishedProgressRootIndex,
            hasRoute ? _publishedProgressRoute : [],
            hasRoute,
            hasRoute ? Depth(routeNode) : 0,
            features,
            hasFeatures));
    }

    /// <summary>
    /// The result of a search that stopped at a takeover checkpoint. <c>ApplyCurrentTurn</c> carries
    /// the adopted current-turn boundary route; <c>AdoptRoute</c> carries no route because the bridge
    /// materializes the exact seed the player requested.
    /// </summary>
    private BlitzBeamResult TakeoverResult(int turns)
    {
        int node = _takeoverAction == BlitzTakeoverAction.ApplyCurrentTurn ? _takeoverNode : -1;
        bool hasNode = node >= 0;
        return new BlitzBeamResult(
            hasNode ? Route(node) : [],
            [],
            hasNode && _takeoverVictory,
            hasNode ? _takeoverScore : double.NegativeInfinity,
            _expanded,
            _transitions,
            _duplicates,
            turns,
            _pruned,
            _solveWatch?.Elapsed ?? TimeSpan.Zero)
        {
            CapacityPruned = _capacityPruned,
            CapacityDroppedChildren = _capacityDropped.Count == 0
                ? BlitzBeamResult.NoCapacityDrops
                : _capacityDropped,
            EndedCompactions = _endedCompactions,
            EndedCompressedStates = _endedCompressedStates,
            SmartPotionPruned = _smartPruned,
            PotionPolicySatisfied = true,
            BestPayoffCount = hasNode ? _nodes[node].PayoffCount : 0,
            BestRootIndex = hasNode ? RootIndex(node) : 0,
            ScoutLayers = _scoutLayers,
            Takeover = _takeoverAction,
            WorkerYieldCount = _options.Pacer?.YieldCount ?? 0,
            FrameRecoveryWaitCount = _options.Pacer?.FrameRecoveryWaitCount ?? 0,
            FrameRecoveryWaitDuration = _options.Pacer?.FrameRecoveryWaitDuration ?? TimeSpan.Zero,
            MaxObservedGcPause = _options.Pacer?.MaxObservedGcPause ?? TimeSpan.Zero,
        };
    }

    /// <summary>
    /// The route preview node: the current best terminal candidate, or the best final-ordering
    /// key among the live frontier when no terminal candidate is better. Mirrors the existing
    /// search's <c>Retention.RankFinal</c> + <c>FinalPlanOrdering.Select</c> over completed and
    /// active nodes, restricted to the pools the Blitz layer currently retains.
    /// </summary>
    private int SelectPreviewNode(int currentBestNode, RouteKey? currentBestKey, Pool pool,
        out BlitzScoreFeatures features, out bool hasFeatures)
    {
        int selected = currentBestNode;
        RouteKey selectedKey = currentBestKey ?? default;
        bool hasSelected = currentBestNode >= 0;
        features = hasSelected && _bestFeaturesNode == currentBestNode ? _bestFeatures : default;
        hasFeatures = hasSelected && _bestFeaturesNode == currentBestNode;
        for (int index = 0; index < pool.Count; index++)
        {
            int node = pool.Node(index);
            if (node < 0 || node == selected)
                continue;
            ref BlitzBattle state = ref pool.State(index);
            BlitzScoreFeatures nodeFeatures =
                _scorer.Features(state, Depth(node), includeRetentionOnly: false);
            RouteKey key = Key(state, nodeFeatures, Depth(node));
            if (!hasSelected || CompareRoutes(key, selectedKey) < 0)
            {
                selected = node;
                selectedKey = key;
                hasSelected = true;
                features = nodeFeatures;
                hasFeatures = true;
            }
        }
        return selected;
    }
}
