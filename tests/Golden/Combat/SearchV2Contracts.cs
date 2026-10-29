using System.IO.Compression;
using Sts2Solver.Ai.Combat;
using Sts2Solver.Ai.Combat.Policy;
using Sts2Solver.Ai.Combat.Scoring;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Golden;

internal static class SearchV2Contracts
{
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidDataException("combat-search-v2: " + message); }

    internal static void Run()
    {
        using FileStream input = File.OpenRead("tests/Golden/data/d2/requests/8317b4a80d18d8e416ee6c37394990f448055559c1711393b5d41560865b895f.request.br");
        using BrotliStream compressed = new(input, CompressionMode.Decompress);
        using MemoryStream bytes = new();
        compressed.CopyTo(bytes);
        SearchCase original = SearchCodec.DecodeRequest(bytes.ToArray());
        SearchPlan plan = new(12, 8, 16, 5, 10_000, 100_000_000)
        {
            EndedKeepWidths = 2, EndedTriggerWidths = 4,
            CustomMembers = [new(8, SearchEvaluation.Base, SearchRetention.SecondBand, SearchPotionLimit.None),
                             new(12, SearchEvaluation.Offensive, SearchRetention.Standard, SearchPotionLimit.Allowed)],
        };
        original = original with { Options = original.Options with { PoolStateBudgetBytes = 123_456_789 } };
        CombatSearchRequest decoded = CombatSearchCodec.Decode(CombatSearchCodec.Encode(new(original, plan)));
        Assert(decoded.Case.Options.PoolStateBudgetBytes == 123_456_789, "pool override survives SCV2");
        Assert(decoded.Plan with { CustomMembers = plan.CustomMembers } == plan
            && decoded.Plan.CustomMembers.SequenceEqual(plan.CustomMembers), "versioned plan round trip");
        Assert(SearchCodec.EncodeRequest(original).AsSpan().SequenceEqual(SearchCodec.EncodeRequest(decoded.Case)),
            "versioned request keeps legacy root and policy");
        Scorer standardScorer = original.Options.Scorer ?? throw new InvalidDataException("夹具没有评分器。");
        ScoreFeatures rootFeatures = standardScorer.Features(original.Battles[0], 0);
        double standardRank = standardScorer.BeamRankScore(original.Battles[0], rootFeatures, false, false);
        double baseRank = new Scorer(standardScorer.RulesForCodec, baseOnly: true)
            .BeamRankScore(original.Battles[0], rootFeatures, false, false);
        double attackRank = new Scorer(standardScorer.RulesForCodec, offensive: true)
            .BeamRankScore(original.Battles[0], rootFeatures, false, false);
        Assert(baseRank != standardRank && attackRank != standardRank, "evaluation dimension changes ranking");
        BeamOptions legacy = original.Options with { BeamWidth = 8, MaxExpandedNodes = 1,
            SoftTimeBudget = TimeSpan.MaxValue, CandidateScanCount = 0 };
        BeamResult capped = new BeamSolver(original.Rules, legacy).Solve(original.Battles, CancellationToken.None);
        BeamResult uncompressed = new BeamSolver(original.Rules, legacy with { MaxExpandedNodes = 2_000 })
            .Solve(original.Battles, CancellationToken.None);
        _ = ReplayedRoute.Build(original.Battles[uncompressed.BestRootIndex], original.Rules,
            uncompressed.BestRoute, uncompressed.BestIsVictory);
        BeamOptions batched = legacy with { MaxExpandedNodes = 2_000,
            EndedBatchTrigger = 16, EndedBatchTarget = 8, SearchMemoryBudgetBytes = 100_000_000 };
        BeamOptions unlimited = batched with { UnlimitedNodes = true, MaxExpandedNodes = 1,
            SoftTimeBudget = TimeSpan.FromSeconds(1) };
        BeamResult unbounded = new BeamSolver(original.Rules, unlimited).Solve(original.Battles, CancellationToken.None);
        SearchStorage storage = new();
        BeamResult first = new BeamSolver(original.Rules, batched).Solve(original.Battles, storage, CancellationToken.None);
        Assert(capped.ExpandedNodes <= 1 && unbounded.ExpandedNodesTotal > 1, "new search has no hidden node cap");
        Assert(first.EndedCompactions > 0, "end-turn batch actually compacts");
        _ = ReplayedRoute.Build(original.Battles[first.BestRootIndex], original.Rules,
            first.BestRoute, first.BestIsVictory);
        BeamResult second = new BeamSolver(original.Rules, batched).Solve(original.Battles, storage, CancellationToken.None);
        Assert(SearchCodec.EncodeResult(first).AsSpan().SequenceEqual(SearchCodec.EncodeResult(second)),
            "compacted pools and route references survive storage reuse");
        long clock = 0;
        ValueTask<SearchMemberMeasurement> DelayedStart(SearchCase member, SearchStorage shared, CancellationToken token)
        {
            clock = 10_001;
            BeamResult delayed = new BeamSolver(member.Rules, member.Options).Solve(member.Battles, shared, token);
            Assert(delayed.SoftTimeBudgetReached && delayed.ExpandedNodesTotal == 0
                && !delayed.PotionPolicySatisfied, "expired member must not start");
            return ValueTask.FromResult(new SearchMemberMeasurement(delayed, "ok", null, 0));
        }
        CombatSearchResult delayedOutcome = CombatSearchCoordinator.RunAsync(new(original, plan),
            new SearchStorage(), DelayedStart, CancellationToken.None, () => clock)
            .AsTask().GetAwaiter().GetResult();
        Assert(delayedOutcome.Beam == null && delayedOutcome.ExpandedNodes == 0
            && delayedOutcome.Status == CombatSearchStatus.Incomplete,
            "shared deadline covers host queue before member starts");
        clock = 0;
        int delayedCalls = 0;
        ValueTask<SearchMemberMeasurement> DelayedShare(SearchCase member, SearchStorage shared, CancellationToken token)
        {
            if (delayedCalls++ == 0)
            {
                clock = (long)member.Options.SoftTimeBudget.TotalMilliseconds + 1;
                Assert(clock < plan.TotalMilliseconds, "test delay stays inside total deadline");
                BeamResult delayed = new BeamSolver(member.Rules, member.Options).Solve(member.Battles, shared, token);
                Assert(delayed.SoftTimeBudgetReached && delayed.ExpandedNodesTotal == 0,
                    "member share includes host queue");
                return ValueTask.FromResult(new SearchMemberMeasurement(delayed, "ok", null, 0));
            }
            return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 0));
        }
        CombatSearchResult delayedShare = CombatSearchCoordinator.RunAsync(new(original, plan),
            new SearchStorage(), DelayedShare, CancellationToken.None, () => clock)
            .AsTask().GetAwaiter().GetResult();
        Assert(delayedShare.Members[0].StopReason == "time" && delayedShare.Members[0].ExpandedNodes == 0,
            "member cutoff is independent of total cutoff");
        int retentionChecks = 0;
        bool expired = false;
        BeamOptions interruptedOptions = batched with
        {
            UnlimitedNodes = true,
            DeadlineExpired = () => expired,
            RetentionCheckpoint = () => { if (++retentionChecks == 2) expired = true; },
        };
        BeamResult interrupted = new BeamSolver(original.Rules, interruptedOptions)
            .Solve(original.Battles, CancellationToken.None);
        Assert(retentionChecks >= 2 && interrupted.ExpandedNodesTotal > 0
            && interrupted.SoftTimeBudgetReached && interrupted.StopReason == "time",
            "deadline interrupts retention at a safe checkpoint");
        _ = ReplayedRoute.Build(original.Battles[interrupted.BestRootIndex], original.Rules,
            interrupted.BestRoute, interrupted.BestIsVictory);
        using CancellationTokenSource cancel = new();
        int cancellationChecks = 0;
        BeamOptions cancelOptions = batched with
        {
            UnlimitedNodes = true,
            RetentionCheckpoint = () => { if (++cancellationChecks == 2) cancel.Cancel(); },
        };
        bool cancelled = false;
        try { _ = new BeamSolver(original.Rules, cancelOptions).Solve(original.Battles, cancel.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled, "user cancellation propagates independently of time stops");
        bool smallPool = false;
        try
        {
            BeamOptions tiny = batched with { BeamWidth = 100, PoolStateBudgetBytes = Battle.ExpectedSize * 64 };
            _ = new BeamSolver(original.Rules, tiny).Solve(original.Battles, CancellationToken.None);
        }
        catch (SearchMemoryBudgetException error) when (error.Region == "ended-pool-capacity") { smallPool = true; }
        Assert(smallPool, "too-small pool is an explicit budget failure");
        Assert(Registry.TryPotion("FIRE_POTION", out ushort fire), "forced potion fixture");
        Assert(first.BestRoute.All(a => a.Kind != PlayerActionKind.UsePotion), "fixture has a potion-free route");
        int forcedSlot = Limits.PotionSlots - 1;
        PotionPolicy forcedPolicy = new(int.MinValue, null,
            new(PotionUseMode.Smart, [new(forcedSlot, "FIRE_POTION", PotionDirective.Force)]),
            PotionUseMode.Smart, false);
        SearchCase forcedCase = original with { Options = original.Options with
        {
            Potions = forcedPolicy, EffectivePotionPolicy = PotionUseMode.Smart,
            EnforceForcedPotionDirectives = true,
            ForcedPotionDirectives = new([forcedSlot], [fire], [9], 0, 0),
        } };
        ValueTask<SearchMemberMeasurement> ForceRunner(SearchCase _, SearchStorage __, CancellationToken ___)
            => ValueTask.FromResult(new SearchMemberMeasurement(first, "ok", null, 0));
        CombatSearchResult forcedOutcome = CombatSearchCoordinator.RunAsync(new(forcedCase, plan with { CustomMembers = [] }),
            new SearchStorage(), ForceRunner, CancellationToken.None, () => 0)
            .AsTask().GetAwaiter().GetResult();
        Assert(forcedOutcome.Beam == null && forcedOutcome.Status == CombatSearchStatus.Incomplete,
            "a route missing a forced potion is never adopted");
        bool fallback = false;
        for (long limit = 1_000_000; limit <= 20_000_000 && !fallback; limit += 250_000)
        {
            try
            {
                BeamResult limited = new BeamSolver(original.Rules, batched with { SearchMemoryBudgetBytes = limit })
                    .Solve(original.Battles, CancellationToken.None);
                if (!limited.SearchMemoryBudgetExceeded) continue;
                Assert(limited.SearchMemoryUsedBytes <= limit, "low-memory allocation stays within ledger");
                _ = ReplayedRoute.Build(original.Battles[limited.BestRootIndex], original.Rules,
                    limited.BestRoute, limited.BestIsVictory);
                fallback = true;
            }
            catch (SearchMemoryBudgetException) { } // Root setup may not fit this trial budget.
        }
        Assert(fallback, "low-memory search returns a replayable fallback");
        Assert(new BeamSolver(original.Rules, legacy).TryCompareResultQuality(original.Battles,
            first, uncompressed, out int compactionComparison), "batch comparison replays both routes");
        Console.WriteLine($"COMBAT_SEARCH_V2 compaction comparison={compactionComparison} " +
            $"batched={first.BestIsVictory} uncompressed={uncompressed.BestIsVictory}");
        Console.WriteLine($"COMBAT_SEARCH_V2 real expanded={first.ExpandedNodesTotal} compactions={first.EndedCompactions} victory={first.BestIsVictory}");
    }

}
