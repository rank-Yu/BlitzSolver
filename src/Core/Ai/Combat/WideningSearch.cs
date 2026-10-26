// Multi-round widening search shared by the Mod and the offline host. The host owns only one
// round's execution; the escalation widths, per-round node caps, pool growth, reusable storage,
// final-route ordering, the damage-free stop and the per-decision time insurance all live here.
using System.Diagnostics;
using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Ai.Combat;

/// <summary>
/// One decision's widening policy. The offline host passes <see cref="EnforceTimeBudget"/> false:
/// it runs every width deterministically and marks the rounds the game would skip or truncate.
/// </summary>
internal sealed record WideningSearchPlan(
    int StartWidth,
    int MaxWidth,
    int MaxExpandedNodesPerWidth,
    int TotalTimeBudgetMilliseconds,
    bool EnforceTimeBudget)
{
    /// <summary>Testing override for every round's pool budget; zero uses the width-scaled value.</summary>
    public int PoolStateBudgetOverrideBytes { get; init; }

    /// <summary>Testing override for every round's node cap; zero uses width × coefficient.</summary>
    public int MaxExpandedNodesOverride { get; init; }

    /// <summary>
    /// HP/max HP at the combat's opening boundary. The damage-free stop uses these for every
    /// decision, so damage taken in earlier turns still counts on a re-search. Zero falls back to
    /// the search root (direct single-search use).
    /// </summary>
    public int CombatStartPlayerHp { get; init; }

    public int CombatStartPlayerMaxHp { get; init; }

    /// <summary>The original single-pool state budget (200 MiB), raised only by this loop.</summary>
    internal const int BasePoolStateBudgetBytes = 200 * 1024 * 1024;

    /// <summary>One extra MiB of state budget per beam seat; 256 seats keep 256 MiB.</summary>
    internal const int PoolStateBudgetBytesPerWidth = 1024 * 1024;

    /// <summary>
    /// Per-pool cap; two pools plus the rest of the search still fit one global budget. 1728 MiB is
    /// the smallest tested 64-MiB-aligned cap that removes the 2048-seat round's Beam.Pool drops on
    /// the 120-root corpus (1664 and 1696 MiB still dropped); the 0.15 × 16 GiB offline budget
    /// still stops only REGENT-013, as it did with the old 1 GiB cap.
    /// </summary>
    internal const int MaximumPoolStateBudgetBytes = 1728 * 1024 * 1024;

    internal int MaxExpandedNodesFor(int width)
    {
        if (MaxExpandedNodesOverride > 0)
            return MaxExpandedNodesOverride;
        long nodes = (long)width * MaxExpandedNodesPerWidth;
        return nodes <= int.MaxValue
            ? (int)nodes
            : throw new ArgumentOutOfRangeException(nameof(width),
                $"宽度 {width} × 每宽节点 {MaxExpandedNodesPerWidth} 超过 32 位节点上限。");
    }

    /// <summary>
    /// Pool capacity grows with the beam width. At the 256-seat start it is 256 MiB; the width ×
    /// 1 MiB product is computed in 64 bits and then clamped to 1728 MiB, so a 2048-seat round
    /// stays representable in <see cref="BeamOptions.PoolStateBudgetBytes"/>. The pool chunks are
    /// charged to the shared search-memory ledger as they are allocated.
    /// </summary>
    internal int PoolStateBudgetBytesFor(int width)
    {
        if (PoolStateBudgetOverrideBytes > 0)
            return PoolStateBudgetOverrideBytes;
        long bytes = Math.Clamp((long)width * PoolStateBudgetBytesPerWidth,
            (long)BasePoolStateBudgetBytes, (long)MaximumPoolStateBudgetBytes);
        return (int)bytes;
    }
}

/// <summary>One round handed to the host. The case already carries the round's width and limits.</summary>
internal sealed record WideningRoundRequest(int Round, int Width, SearchCase Case,
    long RemainingMilliseconds, long SoftTimeMilliseconds, bool WouldSkip);

/// <summary>One round's execution result. <see cref="Beam"/> is null when the round failed.</summary>
internal sealed record WideningRoundMeasurement(
    BeamResult? Beam,
    string Status,
    string? Error,
    long ElapsedMs,
    long ManagedHeapPeakBytes,
    long WorkingSetPeakBytes);

/// <summary>
/// One round's deterministic/capacity diagnostics plus the game-time insurance estimate. The
/// offline host runs rounds that the game would skip or truncate so the potential is visible;
/// <see cref="WouldSkip"/> and <see cref="WouldTruncate"/> mark those rounds.
/// </summary>
internal sealed record WideningRoundLog(
    int Round,
    int Width,
    int MaxExpandedNodes,
    int PoolStateBudgetBytes,
    long RemainingBeforeMilliseconds,
    long EstimatedMilliseconds,
    long SoftTimeMilliseconds,
    bool Started,
    bool SkippedByTime,
    bool WouldSkip,
    bool WouldTruncate,
    long ElapsedMs,
    int ExpandedNodes,
    int RouteActions,
    bool Victory,
    bool DamageFreeVictory,
    string Status,
    string? Error,
    bool MemoryBudgetExceeded,
    long MemoryExceededAtBytes,
    int PoolCapacityDropped,
    long SearchMemoryUsedBytes,
    long SearchMemoryPeakBytes,
    long RouteNodeStorageBytes,
    int RouteNodePeakCount,
    long ManagedHeapPeakBytes,
    long WorkingSetPeakBytes);

/// <summary>The best round of one decision plus the full round log.</summary>
internal sealed record WideningSearchOutcome(
    BeamResult? Beam,
    int BestRound,
    bool DamageFreeVictory,
    bool SoftTimeRisk,
    bool MemoryBudgetExceeded,
    long MemoryExceededAtBytes,
    long ElapsedMs,
    string FailureStatus,
    string? FailureError,
    WideningRoundLog[] Rounds)
{
    internal int RoundsRun => Rounds.Count(static round => round.Started);

    internal int RoundsSkippedByTime => Rounds.Count(static round => !round.Started);

    internal int RoundsWouldSkip => Rounds.Count(static round => round.WouldSkip);

    internal int RoundsWouldTruncate => Rounds.Count(static round => round.WouldTruncate);

    internal int TotalExpandedNodes
        => Rounds.Where(static round => round.Started).Sum(static round => round.ExpandedNodes);

    internal int PoolCapacityDropped
        => Rounds.Where(static round => round.Started).Sum(static round => round.PoolCapacityDropped);

    internal long SearchMemoryUsedBytes
        => Rounds.Length == 0 ? 0 : Rounds.Max(static round => round.SearchMemoryUsedBytes);

    internal long SearchMemoryPeakBytes
        => Rounds.Length == 0 ? 0 : Rounds.Max(static round => round.SearchMemoryPeakBytes);

    internal long RouteNodeStorageBytes
        => Rounds.Length == 0 ? 0 : Rounds.Max(static round => round.RouteNodeStorageBytes);

    internal int RouteNodePeakCount
        => Rounds.Length == 0 ? 0 : Rounds.Max(static round => round.RouteNodePeakCount);

    internal long ManagedHeapPeakBytes
        => Rounds.Length == 0 ? 0 : Rounds.Max(static round => round.ManagedHeapPeakBytes);

    internal long WorkingSetPeakBytes
        => Rounds.Length == 0 ? 0 : Rounds.Max(static round => round.WorkingSetPeakBytes);
}

/// <summary>
/// The one multi-round loop. Starting at <see cref="WideningSearchPlan.StartWidth"/>, it doubles the
/// width while the best route is not a damage-free victory. Every round reuses the same
/// <see cref="SearchStorage"/> and compares its best route with the incumbent through
/// <see cref="BeamSolver.TryCompareResultQuality"/> (the same final-ordering key the solver uses).
/// A memory-budget stop ends the decision after that round; a time-insurance skip ends it before the
/// next round. The first round's route always stays in the candidate set.
/// </summary>
internal static class WideningSearch
{
    public static async ValueTask<WideningSearchOutcome> RunAsync(
        SearchCase searchCase,
        WideningSearchPlan plan,
        SearchStorage storage,
        Func<WideningRoundRequest, ValueTask<WideningRoundMeasurement>> runRound,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(searchCase);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(runRound);
        if (plan.StartWidth <= 0 || plan.MaxWidth < plan.StartWidth)
            throw new ArgumentOutOfRangeException(nameof(plan), "加宽档位的宽度范围无效。");
        if (plan.MaxExpandedNodesPerWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(plan), "每轮节点上限系数必须为正。");
        if (plan.MaxWidth > int.MaxValue / plan.MaxExpandedNodesPerWidth)
            throw new ArgumentOutOfRangeException(nameof(plan),
                $"最多宽度 {plan.MaxWidth} × 每宽节点 {plan.MaxExpandedNodesPerWidth} 超过 32 位节点上限。");
        if (plan.TotalTimeBudgetMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(plan), "决策总时间不能为负。");

        // One solver instance supplies the final-ordering key for every round. It never searches,
        // so the scorer state it shares with the round solvers is only read.
        BeamSolver quality = new(searchCase.Rules, searchCase.Options);
        Stopwatch decisionWatch = Stopwatch.StartNew();
        List<WideningRoundLog> rounds = [];
        BeamResult? best = null;
        int bestRound = -1;
        bool bestDamageFree = false;
        bool memoryExceeded = false;
        long memoryExceededAt = 0;
        string failureStatus = "";
        string? failureError = null;
        long simulatedRemaining = Math.Max(0, plan.TotalTimeBudgetMilliseconds);
        bool simulatedReached = true;
        long previousElapsed = 0;
        int round = 0;

        for (int width = plan.StartWidth; ; width = NextWidth(width, plan.MaxWidth))
        {
            token.ThrowIfCancellationRequested();
            int maxNodes = plan.MaxExpandedNodesFor(width);
            int poolBytes = plan.PoolStateBudgetBytesFor(width);
            long remaining = plan.EnforceTimeBudget
                ? Remaining(plan.TotalTimeBudgetMilliseconds, decisionWatch.ElapsedMilliseconds)
                : simulatedRemaining;
            long estimated = round == 0 ? 0 : Estimated(previousElapsed);
            bool wouldSkip = plan.TotalTimeBudgetMilliseconds > 0 && round > 0
                && (!simulatedReached || remaining < estimated);
            if (plan.EnforceTimeBudget && wouldSkip)
            {
                rounds.Add(new WideningRoundLog(round, width, maxNodes, poolBytes, remaining,
                    estimated, 0, false, true, true, false, 0, 0, 0, false, false, "skipped",
                    null, false, 0, 0, 0, 0, 0, 0, 0, 0));
                break;
            }
            if (!plan.EnforceTimeBudget && wouldSkip)
                simulatedReached = false;

            TimeSpan softTime = plan.EnforceTimeBudget && plan.TotalTimeBudgetMilliseconds > 0
                ? TimeSpan.FromMilliseconds(Math.Max(0, remaining))
                : TimeSpan.MaxValue;
            BeamOptions options = searchCase.Options with
            {
                BeamWidth = width,
                MaxExpandedNodes = maxNodes,
                SoftTimeBudget = softTime,
                SecondRankBandWidth = width,
                PoolStateBudgetBytes = poolBytes,
            };
            SearchCase roundCase = searchCase with { Options = options };
            WideningRoundMeasurement measurement = await runRound(
                new WideningRoundRequest(round, width, roundCase, remaining,
                    softTime == TimeSpan.MaxValue ? 0 : (long)softTime.TotalMilliseconds, wouldSkip));
            token.ThrowIfCancellationRequested();
            BeamResult? beam = measurement.Beam;
            long elapsed = Math.Max(0, measurement.ElapsedMs);
            bool damageFree = false;
            if (beam != null)
            {
                damageFree = IsDamageFreeVictory(searchCase.Battles, searchCase.Rules, beam, plan);
                if (best == null)
                {
                    best = beam;
                    bestRound = round;
                    bestDamageFree = damageFree;
                }
                else
                {
                    if (!quality.TryCompareResultQuality(searchCase.Battles, beam, best,
                            out int comparison))
                    {
                        throw new InvalidDataException(
                            $"第 {round + 1} 轮路线无法按终局比较键重放（宽度 {width}）。");
                    }
                    if (comparison < 0)
                    {
                        best = beam;
                        bestRound = round;
                        bestDamageFree = damageFree;
                    }
                }
                if (beam.SearchMemoryBudgetExceeded)
                {
                    memoryExceeded = true;
                    if (memoryExceededAt == 0)
                        memoryExceededAt = beam.SearchMemoryExceededAtBytes;
                }
            }
            else if (best == null)
            {
                failureStatus = measurement.Status;
                failureError = measurement.Error;
            }

            int drops = beam != null && beam.CapacityDroppedChildren.TryGetValue("Beam.Pool",
                out int dropped) ? dropped : 0;
            rounds.Add(new WideningRoundLog(round, width, maxNodes, poolBytes, remaining,
                estimated, softTime == TimeSpan.MaxValue ? 0 : (long)softTime.TotalMilliseconds,
                true, false, wouldSkip, false, elapsed, beam?.ExpandedNodes ?? 0,
                beam?.BestRoute.Length ?? 0, beam?.BestIsVictory ?? false, damageFree,
                measurement.Status, measurement.Error, beam?.SearchMemoryBudgetExceeded ?? false,
                beam?.SearchMemoryExceededAtBytes ?? 0, drops, beam?.SearchMemoryUsedBytes ?? 0,
                beam?.SearchMemoryPeakBytes ?? 0, beam?.RouteNodeStorageBytes ?? 0,
                beam?.RouteNodePeakCount ?? 0, measurement.ManagedHeapPeakBytes,
                measurement.WorkingSetPeakBytes));

            bool wouldTruncate = false;
            if (plan.TotalTimeBudgetMilliseconds > 0)
            {
                if (plan.EnforceTimeBudget)
                {
                    wouldTruncate = beam?.SoftTimeBudgetReached ?? false;
                    if (wouldTruncate)
                    {
                        simulatedRemaining = 0;
                        simulatedReached = false;
                    }
                    else
                    {
                        simulatedRemaining = Remaining(plan.TotalTimeBudgetMilliseconds,
                            decisionWatch.ElapsedMilliseconds);
                    }
                }
                else if (simulatedReached)
                {
                    wouldTruncate = elapsed > remaining;
                    if (wouldTruncate)
                    {
                        simulatedRemaining = 0;
                        simulatedReached = false;
                    }
                    else
                    {
                        simulatedRemaining = Math.Max(0, remaining - elapsed);
                    }
                }
            }
            if (wouldTruncate)
            {
                WideningRoundLog last = rounds[^1];
                rounds[^1] = last with { WouldTruncate = true };
            }

            if (beam == null || memoryExceeded || damageFree || width >= plan.MaxWidth)
                break;
            previousElapsed = elapsed;
            round++;
        }

        decisionWatch.Stop();
        WideningRoundLog[] log = [.. rounds];
        return new WideningSearchOutcome(best, Math.Max(0, bestRound), bestDamageFree,
            log.Any(static item => item.WouldSkip || item.WouldTruncate || item.SkippedByTime),
            memoryExceeded, memoryExceededAt, decisionWatch.ElapsedMilliseconds, failureStatus,
            failureError, log);
    }

    private static int NextWidth(int width, int maxWidth)
        => width >= maxWidth / 2 ? Math.Max(width + 1, maxWidth) : width * 2;

    private static long Remaining(int budget, long elapsed)
        => budget <= 0 ? 0 : Math.Max(0, budget - elapsed);

    private static long Estimated(long previousElapsed)
        => previousElapsed > long.MaxValue / 2 ? long.MaxValue : previousElapsed * 2;

    private static bool IsDamageFreeVictory(IReadOnlyList<Battle> roots, Rules rules,
        BeamResult beam, WideningSearchPlan plan)
    {
        if (!beam.BestIsVictory || (uint)beam.BestRootIndex >= (uint)roots.Count)
            return false;
        Battle root = roots[beam.BestRootIndex];
        ReplayedRoute route = ReplayedRoute.Build(root, rules, beam.BestRoute, expectedVictory: true);
        Battle final = route.Count == 0 ? root : route[route.Count - 1].After;
        int startHp = plan.CombatStartPlayerHp > 0 ? plan.CombatStartPlayerHp : root.Player.CurrentHp;
        int startMaxHp = plan.CombatStartPlayerMaxHp > 0
            ? plan.CombatStartPlayerMaxHp : root.Player.MaxHp;
        return final.Outcome == Outcome.Victory
            && final.Player.CurrentHp >= startHp
            && final.Player.MaxHp >= startMaxHp;
    }
}
