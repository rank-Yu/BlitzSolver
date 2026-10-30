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
        byte[] oldDefault = CombatSearchCodec.Encode(new(original, plan with { CustomMembers = [] }));
        oldDefault = oldDefault[..^4]; // v1–v3 predate the v4 schedule field.
        oldDefault[4] = 2; oldDefault[5] = 0;
        CombatSearchRequest oldDecoded = CombatSearchCodec.Decode(oldDefault);
        Assert(oldDecoded.Plan.CustomMembers.TakeLast(2).Select(m => m.Potions)
            .SequenceEqual(new[] { SearchPotionLimit.One, SearchPotionLimit.Allowed }),
            "v2 default retains historical schedule");
        byte[] v1Default = oldDefault[..^4];
        v1Default[4] = 1;
        Assert(CombatSearchCodec.Decode(v1Default).Plan.CustomMembers.SequenceEqual(oldDecoded.Plan.CustomMembers),
            "v1 default retains historical schedule");
        CombatSearchRequest oldReencoded = CombatSearchCodec.Decode(CombatSearchCodec.Encode(oldDecoded));
        Assert(oldReencoded.Plan.CustomMembers.SequenceEqual(oldDecoded.Plan.CustomMembers),
            "legacy default stays stable when re-encoded");
        SearchPlan newLimits = plan with { CustomMembers = [
            new(8, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.None),
            new(8, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.Two),
            new(8, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.Three)] };
        byte[] newBytes = CombatSearchCodec.Encode(new(original, newLimits));
        Assert(CombatSearchCodec.Decode(newBytes).Plan.CustomMembers.SequenceEqual(newLimits.CustomMembers),
            "v4 limits round trip");
        newBytes = newBytes[..^4];
        newBytes[4] = 2; newBytes[5] = 0;
        bool rejectedNewLimit = false;
        try { _ = CombatSearchCodec.Decode(newBytes); }
        catch (InvalidDataException) { rejectedNewLimit = true; }
        Assert(rejectedNewLimit, "v2 cannot silently read v3 limits");
        SearchPlan defaultPlan = plan with { CustomMembers = [], DefaultPotionSchedule = DefaultPotionSchedule.OneAllowed };
        byte[] v4Bytes = CombatSearchCodec.Encode(new(original, defaultPlan)); v4Bytes[4] = 4;
        CombatSearchRequest v4Default = CombatSearchCodec.Decode(v4Bytes);
        Assert(v4Default.Plan.DefaultPotionSchedule == DefaultPotionSchedule.OneAllowed
            && v4Default.Plan.CustomMembers.IsDefaultOrEmpty, "v4 default schedule round trip");
        SearchPlan triplePlan = defaultPlan with { DefaultPotionSchedule = DefaultPotionSchedule.OneTwoThree };
        byte[] tripleBytes = CombatSearchCodec.Encode(new(original, triplePlan));
        Assert(CombatSearchCodec.Decode(tripleBytes).Plan.DefaultPotionSchedule == DefaultPotionSchedule.OneTwoThree,
            "v4 three-layer schedule round trip");
        byte[] v3Bytes = tripleBytes[..^4]; v3Bytes[4] = 3; v3Bytes[5] = 0;
        CombatSearchRequest v3 = CombatSearchCodec.Decode(v3Bytes);
        Assert(v3.Plan.DefaultPotionSchedule == DefaultPotionSchedule.OneTwoThree
            && v3.Plan.CustomMembers.IsDefaultOrEmpty, "v3 default keeps three layers and default deduplication");
        SearchPlan v3RoundTrip = CombatSearchCodec.Decode(CombatSearchCodec.Encode(v3)).Plan;
        Assert(v3RoundTrip with { CustomMembers = v3.Plan.CustomMembers } == v3.Plan
            && v3RoundTrip.CustomMembers.SequenceEqual(v3.Plan.CustomMembers),
            "v3 schedule survives v4 re-encoding");
        SearchPlan staged = plan with { CustomMembers = [], DefaultPotionSchedule = DefaultPotionSchedule.StagedPortfolio };
        byte[] stagedBytes = CombatSearchCodec.Encode(new(original, staged));
        Assert(CombatSearchCodec.Decode(stagedBytes).Plan.DefaultPotionSchedule == DefaultPotionSchedule.StagedPortfolio,
            "v5 staged schedule round trip");
        byte[] invalidV4 = (byte[])stagedBytes.Clone(); invalidV4[4] = 4;
        bool rejectsV5ScheduleInV4 = false;
        try { _ = CombatSearchCodec.Decode(invalidV4); }
        catch (InvalidDataException) { rejectsV5ScheduleInV4 = true; }
        Assert(rejectsV5ScheduleInV4, "v4 rejects the v5 schedule value");
        SearchMember[] stagedMembers = PortfolioBuilder.Build(staged, power: true, forced: false);
        Assert(stagedMembers.Length == 18
            && stagedMembers.Take(6).All(m => m.Potions == SearchPotionLimit.None)
            && stagedMembers.Skip(6).Take(6).All(m => m.Potions == SearchPotionLimit.One)
            && stagedMembers.Skip(12).All(m => m.Potions == SearchPotionLimit.Allowed)
            && stagedMembers.Take(6).Select(m => m.Width).SequenceEqual(new[] { 12, 8, 16, 12, 12, 5 })
            && stagedMembers.Skip(12).Select(m => m.Width).SequenceEqual(new[] { 12, 12, 16, 12, 12, 5 })
            && Enumerable.Range(0, 6).All(i => stagedMembers[i] with { Potions = SearchPotionLimit.One } == stagedMembers[i + 6]
                && stagedMembers[i] with { Potions = SearchPotionLimit.Allowed,
                    Width = i == 1 ? staged.BaselineWidth : stagedMembers[i].Width } == stagedMembers[i + 12]),
            "staged portfolio repeats all six dimensions in stage order");
        Assert(PortfolioBuilder.Build(staged, false, false).Length == 15
            && PortfolioBuilder.Build(staged, true, true).Length == 6
            && PortfolioBuilder.Build(staged, true, true).SequenceEqual(stagedMembers.Skip(12)),
            "power-free and forced staged portfolios");
        byte[] unknownVersion = (byte[])tripleBytes.Clone(); unknownVersion[4] = 8;
        bool invalidVersion = false;
        try { _ = CombatSearchCodec.Decode(unknownVersion); }
        catch (InvalidDataException) { invalidVersion = true; }
        Assert(invalidVersion, "unknown codec version rejected");
        byte[] unknownSchedule = (byte[])tripleBytes.Clone(); unknownSchedule[^4] = 99;
        bool invalidSchedule = false;
        try { _ = CombatSearchCodec.Decode(unknownSchedule); }
        catch (InvalidDataException) { invalidSchedule = true; }
        Assert(invalidSchedule, "unknown schedule rejected");
        bool encodeInvalidSchedule = false;
        try { _ = CombatSearchCodec.Encode(new(original, defaultPlan with { DefaultPotionSchedule = (DefaultPotionSchedule)99 })); }
        catch (InvalidDataException) { encodeInvalidSchedule = true; }
        Assert(encodeInvalidSchedule, "invalid schedule cannot be encoded");
        Assert(Registry.TryPotion("FIRE_POTION", out ushort schedulePotion), "schedule fixture potion");
        Battle scheduleRoot = original.Battles[0];
        scheduleRoot.PotionSlotCount = 1; scheduleRoot.Potions[0] = (ushort)(schedulePotion + 1);
        BeamOptions scheduleOptions = original.Options with
        {
            InitialExplicitPotionUses = 0, MinimumPotionUses = 0,
            EnforceForcedPotionDirectives = false, ForcedPotionDirectives = ForcedPotionDirectives.None,
            Potions = new(int.MinValue, 2, new(PotionUseMode.Smart, []), PotionUseMode.Smart, false),
        };
        SearchCase scheduleCase = original with { Battles = [scheduleRoot], Options = scheduleOptions };
        long scheduleClock = 0;
        ValueTask<SearchMemberMeasurement> ScheduleRun(SearchCase _, SearchStorage __, CancellationToken ___)
        { scheduleClock += 10; return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 10)); }
        CombatSearchResult v3Run = CombatSearchCoordinator.RunAsync(new(scheduleCase, v3.Plan), new(), ScheduleRun,
            CancellationToken.None, () => scheduleClock).AsTask().GetAwaiter().GetResult();
        Assert(v3Run.Members[^3].Ran && v3Run.Members[^2].Ran
            && v3Run.Members[^1].SkippedReason == "duplicate-limit", "decoded v3 max two skips duplicate Three");
        scheduleClock = 0;
        SearchCase maxOneCase = scheduleCase with { Options = scheduleOptions with
            { Potions = scheduleOptions.Potions with { MaximumUses = 1 } } };
        CombatSearchResult oldRun = CombatSearchCoordinator.RunAsync(new(maxOneCase, oldDecoded.Plan), new(), ScheduleRun,
            CancellationToken.None, () => scheduleClock).AsTask().GetAwaiter().GetResult();
        Assert(oldRun.Members[^2].Ran && oldRun.Members[^1].Ran,
            "v1/v2 materialized custom schedule retains historical no-dedup behavior");
        scheduleClock = 0;
        CombatSearchResult v4Run = CombatSearchCoordinator.RunAsync(new(maxOneCase, v4Default.Plan), new(), ScheduleRun,
            CancellationToken.None, () => scheduleClock).AsTask().GetAwaiter().GetResult();
        Assert(v4Run.Members[^2].Ran && v4Run.Members[^1].SkippedReason == "duplicate-limit",
            "v4 default max one deduplicates");
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
        Assert(first.Duplicates > 0, "candidate deduplication is exercised");
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
        bool longPoolLimit = false;
        try
        {
            BeamOptions tinyNew = batched with { BeamWidth = 100,
                StatePoolBudgetBytes = (long)Battle.ExpectedSize * 64,
                PoolStateBudgetBytes = 20_000_000 };
            _ = new BeamSolver(original.Rules, tinyNew).Solve(original.Battles, CancellationToken.None);
        }
        catch (SearchMemoryBudgetException error) when (error.Region == "ended-pool-capacity") { longPoolLimit = true; }
        Assert(longPoolLimit, "actual beam obeys long pool budget ahead of legacy override");
        BeamResult largePool = new BeamSolver(original.Rules, batched with
        {
            MaxExpandedNodes = 1, StatePoolBudgetBytes = 3_000_000_000,
            PoolStateBudgetBytes = Battle.ExpectedSize * 64,
        }).Solve(original.Battles, CancellationToken.None);
        Assert(largePool.ExpandedNodesTotal > 0, "actual beam supports long pool budget above 2 GiB");
        BeamResult noveltyLargePool = new BeamSolver(original.Rules, legacy with
        {
            StatePoolBudgetBytes = 3_000_000_000,
            Novelty = new NoveltyOptions(1, 0, 64, 256, 100, 2),
        }).Solve(original.Battles, CancellationToken.None);
        Assert(noveltyLargePool.ExpandedNodesTotal > 0, "novelty handles long pool capacity without overflow");

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
        PotionStageContracts.Run(original, plan);
        CheckPotionCandidates();
        Console.WriteLine($"COMBAT_SEARCH_V2 compaction comparison={compactionComparison} " +
            $"batched={first.BestIsVictory} uncompressed={uncompressed.BestIsVictory}");
        Console.WriteLine($"COMBAT_SEARCH_V2 real expanded={first.ExpandedNodesTotal} compactions={first.EndedCompactions} victory={first.BestIsVictory}");
    }

    private static void CheckPotionCandidates()
    {
        using FileStream input = File.OpenRead("tests/Golden/data/d2/requests/023d47d587f3c92f1784abbeb0d4bcbf43f69012f1cfb5c8d6cea34ff99da775.request.br");
        using BrotliStream compressed = new(input, CompressionMode.Decompress);
        using MemoryStream bytes = new();
        compressed.CopyTo(bytes);
        SearchCase fixture = SearchCodec.DecodeRequest(bytes.ToArray());
        BeamOptions options = fixture.Options with
        {
            BeamWidth = 20, MaxExpandedNodes = 3000, SoftTimeBudget = TimeSpan.MaxValue,
            CandidateScanCount = 0, AuditPotionPolicy = true, CollectPotionCandidates = true,
            MinimumPotionUses = 0, Potions = fixture.Options.Potions with
                { MaximumUses = 2, ForceAllDisabled = false },
            EnforceForcedPotionDirectives = false, ForcedPotionDirectives = ForcedPotionDirectives.None,
            SearchMemoryBudgetBytes = 100_000_000,
        };
        BeamResult Search(BeamOptions settings) => new BeamSolver(fixture.Rules, settings)
            .Solve(fixture.Battles, CancellationToken.None);
        BeamResult result = Search(options);
        Assert(result.PotionPolicySatisfied && result.PotionCandidatesComplete
            && result.PotionCandidates.Select(c => c.ExplicitUses).Order().SequenceEqual(new[] { 1, 2 })
            && result.PotionCandidates.All(c => c.PolicyEligible),
            "one search records distinct visited candidates by actual potion count");
        Assert(options.InitialExplicitPotionUses == 1
            && result.BestRoute.Select((a, i) => (a, i)).Any(x => x.a.Kind == PlayerActionKind.UsePotion
                && result.BestRoute.Take(x.i).Any(a => a.Kind == PlayerActionKind.PlayCard))
            && options.InitialExplicitPotionUses + result.BestRoute.Count(a => a.Kind == PlayerActionKind.UsePotion) <= 2,
            "playing before potion is allowed and cumulative limit is respected");
        _ = ReplayedRoute.Build(fixture.Battles[result.BestRootIndex], fixture.Rules,
            result.BestRoute, result.BestIsVictory);
        BeamResult one = Search(options with { Potions = options.Potions with { MaximumUses = 1 } });
        Assert(one.PotionCandidates.All(c => c.ExplicitUses <= 1)
            && one.BestRoute.All(a => a.Kind != PlayerActionKind.UsePotion), "one-use cap includes the initial use");

        long summaryCharge = 64L * result.PotionCandidates.Count;
        BeamResult memory = Search(options with { SearchMemoryBudgetBytes = result.SearchMemoryUsedBytes - summaryCharge });
        Assert(memory.SearchMemoryBudgetExceeded && !memory.PotionCandidatesComplete
            && memory.PotionCandidates.Count == 0 && memory.BestRoute.Length > 0,
            "summary allocation failure retains the route and marks diagnostics incomplete");
        _ = ReplayedRoute.Build(fixture.Battles[memory.BestRootIndex], fixture.Rules,
            memory.BestRoute, memory.BestIsVictory);
        int checks = 0;
        BeamResult counted = Search(options with { DeadlineExpired = () => { checks++; return false; } });
        int limit = checks - 2;
        checks = 0;
        BeamResult deadline = Search(options with { DeadlineExpired = () => ++checks >= limit });
        Assert(counted.PotionCandidatesComplete && deadline.SoftTimeBudgetReached
            && !deadline.PotionCandidatesComplete && deadline.BestRoute.Length > 0,
            "deadline during summary marks it incomplete without losing the route");
        _ = ReplayedRoute.Build(fixture.Battles[deadline.BestRootIndex], fixture.Rules,
            deadline.BestRoute, deadline.BestIsVictory);
        Console.WriteLine("COMBAT_SEARCH_V2 real potion candidate/limit/summary contracts=pass");
    }

}
