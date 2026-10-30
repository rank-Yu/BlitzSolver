using Sts2Solver.Ai.Combat;
using Sts2Solver.Ai.Combat.Policy;
using Sts2Solver.Ai.Combat.Scoring;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.KernelChecks;

internal static class SearchV2Checks
{
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidDataException("combat-search-v2: " + message); }

    internal static void Check(Rules rules)
    {
        SearchPlan low = SearchPresets.For(CombatSearchTier.Low, 10_000);
        SearchPlan medium = SearchPresets.For(CombatSearchTier.Medium, 10_000);
        SearchPlan high = SearchPresets.For(CombatSearchTier.High, 10_000);
        SearchPlan extra = SearchPresets.For(CombatSearchTier.ExtraHigh, 10_000);
        Assert((low.BaselineWidth, low.NarrowWidth, low.WideWidth, low.OffensiveWidth, low.TotalMilliseconds)
            == (225, 150, 338, 90, 60_000), "low preset");
        Assert((medium.BaselineWidth, medium.NarrowWidth, medium.WideWidth, medium.OffensiveWidth, medium.TotalMilliseconds)
            == (300, 200, 450, 120, 120_000), "medium preset");
        Assert((high.BaselineWidth, high.NarrowWidth, high.WideWidth, high.OffensiveWidth, high.TotalMilliseconds)
            == (450, 300, 675, 180, 180_000), "high preset");
        Assert((extra.BaselineWidth, extra.NarrowWidth, extra.WideWidth, extra.OffensiveWidth, extra.TotalMilliseconds)
            == (675, 450, 1013, 270, 300_000), "extra preset");

        Assert(new[] { low, medium, high, extra }.All(p => p.DefaultPotionSchedule == DefaultPotionSchedule.StagedPortfolio),
            "production presets use staged portfolio");
        Assert(PortfolioBuilder.Build(medium, power: true, forced: false).Length == 18
            && PortfolioBuilder.Build(medium, power: false, forced: false).Length == 15,
            "staged preset covers six or five members in each phase");

        int twoGiB = SearchMemoryBudget.PoolStateCapacity(int.MaxValue, Battle.ExpectedSize, 64);
        int threeGiB = SearchMemoryBudget.PoolStateCapacity(3_000_000_000L, Battle.ExpectedSize, 64);
        Assert(threeGiB > twoGiB && threeGiB % 64 == 0
            && (long)threeGiB * Battle.ExpectedSize <= 3_000_000_000L,
            "state pool planning supports budgets above two GiB");
        bool unrepresentable = false;
        try { _ = SearchMemoryBudget.PoolStateCapacity(long.MaxValue, Battle.ExpectedSize, 64); }
        catch (InvalidDataException) { unrepresentable = true; }
        Assert(unrepresentable, "unrepresentable pool capacity is rejected explicitly");

        SearchStorage legacy = new();
        legacy.BeginSolve(1000);
        legacy.Memory.Charge(100, "pool-aux");
        legacy.BeginSolve(1000);
        Assert(legacy.Memory.UsedBytes == 100, "legacy transient accounting retained");
        SearchStorage shared = new();
        shared.BeginSolve(1000, releaseTransient: true);
        shared.Memory.Charge(100, "pool-aux");
        _ = shared.RentArray<int>(10);
        shared.BeginSolve(1000, releaseTransient: true);
        Assert(shared.Memory.UsedBytes == 40, "transient released, retained array charged");
        _ = shared.RentArray<int>(10);
        Assert(shared.Memory.UsedBytes == 40, "array reused without duplicate charge");
        bool budget = false;
        try { _ = shared.RentArray<int>(241); }
        catch (SearchMemoryBudgetException) { budget = true; }
        Assert(budget && shared.Memory.UsedBytes == 40, "allocation cannot exceed shared budget");

        ScoreRules score = new()
        {
            Rules = rules, Cards = [], Powers = [], PotionStrategicCost = [],
            InitialPlayerMaxHp = 80, IsActEndingBoss = false, Act3BossStrategy = false,
            IgnoreLongTermRewards = false, TheftPolicy = null, InitialEnemyCount = 0,
            InitialPersistentBuffValue = 0, InitialEnemyStrengthSuppression = 0,
            InitialEnemyWeakTurns = 0, InitialRetainedAttackValue = 0,
            RootPotions = [], RootPotionSlotCount = 0,
        };
        BeamOptions options = new(2, 1, 64, TimeSpan.MaxValue, 80)
        { Scorer = new Scorer(score), Ordering = new(80, 80, BossHpRelief.None, PostCombatRelicHealProfile.None),
          Potions = new(int.MinValue, null, new(PotionUseMode.Smart, []), PotionUseMode.Smart, false) };
        Assert(Registry.TryPotion("FIRE_POTION", out ushort fire), "fire potion registered");
        Battle root = default;
        root.PotionSlotCount = 1;
        root.Potions[0] = (ushort)(fire + 1);
        SearchCase search = new(rules, [root], options);
        SearchPlan tiny = new(3, 2, 4, 1, 1000, 100_000_000)
        { DefaultPotionSchedule = DefaultPotionSchedule.OneAllowed };
        search.Battles[0].Player.CurrentHp = 42;
        CombatSearchRequest frozen = new(search, tiny);
        search.Battles[0].Player.CurrentHp = 7;
        SearchCase leaked = frozen.Case;
        Assert(leaked.Battles[0].Player.CurrentHp == 42, "request owns the root snapshot");
        leaked.Battles[0].Player.CurrentHp = 3;
        Assert(frozen.Case.Battles[0].Player.CurrentHp == 42, "request does not expose its mutable root");
        CombatSearchRequest request = new(search, tiny);
        List<BeamOptions> observed = [];
        long time = 100;
        ValueTask<SearchMemberMeasurement> Run(SearchCase item, SearchStorage _, CancellationToken __)
        {
            observed.Add(item.Options);
            time += 20;
            return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 20));
        }
        CombatSearchResult result = CombatSearchCoordinator.RunAsync(request, new(), Run,
            CancellationToken.None, () => time).AsTask().GetAwaiter().GetResult();
        Assert(result.Status == CombatSearchStatus.Incomplete && result.Beam == null, "no result is incomplete");
        Assert(observed.Count == 7 && result.Members.All(m => m.Ran), "all applicable members run");
        Assert(observed.All(o => o.UnlimitedNodes && o.SecondRankBandWidth == o.BeamWidth), "unlimited and member retention width");
        Assert(observed.Take(5).All(o => o.Potions.ForceAllDisabled), "true potion-free baseline");
        Assert(observed.Skip(5).Select(o => o.Potions.MaximumUses).SequenceEqual(new int?[] { 1, null }), "one then allowed potion layers");
        Assert(observed[2].SecondRankBand && observed[3].Scorer != observed[0].Scorer, "evaluation and retention dimensions");
        long firstShare = (long)observed[0].SoftTimeBudget.TotalMilliseconds;
        Assert((long)observed[1].SoftTimeBudget.TotalMilliseconds > firstShare,
            "unused member time transfers to later members");
        Assert(!PotionSearchSchedule.HasAllowedUse([default], rules, options), "empty roots have no potion stage");
        observed.Clear(); time = 100;
        CombatSearchResult noPotion = CombatSearchCoordinator.RunAsync(
            new(search with { Battles = [default] }, tiny), new(), Run, CancellationToken.None,
            () => time).AsTask().GetAwaiter().GetResult();
        Assert(noPotion.Members.Count(m => m.SkippedReason == "no-allowed-potions") == 2
            && observed.Count == 5, "unavailable potion members skip");
        Assert((long)observed[0].SoftTimeBudget.TotalMilliseconds > firstShare,
            "unneeded potion reserve transfers to main members");
        SearchMember[] defaults = PortfolioBuilder.Build(tiny, power: false, forced: false);
        Assert(defaults.TakeLast(2).Select(m => (m.Width, m.Potions)).SequenceEqual(new[]
            { (2, SearchPotionLimit.One), (3, SearchPotionLimit.Allowed) }),
            "default potion limits and widths");
        Assert(observed.All(o => o.DeadlineExpired != null), "each member has a deadline");
        observed.Clear(); time = 100;
        BeamOptions counted = options with { InitialExplicitPotionUses = 1, MinimumPotionUses = 2,
            Potions = options.Potions with { MaximumUses = 2 } };
        CombatSearchResult countedResult = CombatSearchCoordinator.RunAsync(
            new(search with { Options = counted }, tiny), new(), Run, CancellationToken.None,
            () => time).AsTask().GetAwaiter().GetResult();
        Assert(observed.Count == 1 && observed[0].Potions.MaximumUses == 2
            && observed[0].InitialExplicitPotionUses == 1 && observed[0].MinimumPotionUses == 2,
            "initial and minimum counts are preserved");
        Assert(countedResult.Members.Take(6).All(m => m.SkippedReason == "minimum-exceeds-limit")
            && countedResult.Members[6].Ran,
            "infeasible limits skip before allocating shares");
        observed.Clear(); time = 100;
        counted = counted with { MinimumPotionUses = 0, Potions = counted.Potions with { MaximumUses = 1 } };
        CombatSearchResult alreadyUsed = CombatSearchCoordinator.RunAsync(
            new(search with { Options = counted }, tiny), new(), Run, CancellationToken.None,
            () => time).AsTask().GetAwaiter().GetResult();
        Assert(observed.Count == 5 && observed.All(o => o.Potions.ForceAllDisabled
            && o.Potions.MaximumUses == 1) && alreadyUsed.Members.Skip(5).All(m => !m.Ran),
            "initially used maximum still permits no-new-potion search");
        observed.Clear(); time = 100;
        SearchPlan custom = tiny with { CustomMembers = [
            new(2, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.None),
            new(2, SearchEvaluation.Base, SearchRetention.Standard, SearchPotionLimit.One),
            new(3, SearchEvaluation.Standard, SearchRetention.SecondBand, SearchPotionLimit.One)] };
        CombatSearchResult customResult = CombatSearchCoordinator.RunAsync(
            new(search, custom), new(), Run, CancellationToken.None, () => time)
            .AsTask().GetAwaiter().GetResult();
        Assert(observed.Count == 3 && customResult.Members.All(m => m.Ran),
            "custom members with the same limit retain distinct dimensions");
        foreach (int max in new[] { 1, 2 })
        {
            observed.Clear(); time = 100;
            BeamOptions limited = options with { Potions = options.Potions with { MaximumUses = max } };
            CombatSearchResult limitedResult = CombatSearchCoordinator.RunAsync(
                new(search with { Options = limited }, tiny), new(), Run, CancellationToken.None,
                () => time).AsTask().GetAwaiter().GetResult();
            Assert(limitedResult.Members[5].Ran && (max == 1
                ? limitedResult.Members[6].SkippedReason == "duplicate-limit"
                : limitedResult.Members[6].Ran), "default intersected limit deduplication " + max);
        }
        observed.Clear(); time = 100;
        SearchPlan three = tiny with { DefaultPotionSchedule = DefaultPotionSchedule.OneTwoThree };
        BeamOptions maxTwo = options with { Potions = options.Potions with { MaximumUses = 2 } };
        CombatSearchResult threeResult = CombatSearchCoordinator.RunAsync(
            new(search with { Options = maxTwo }, three), new(), Run, CancellationToken.None,
            () => time).AsTask().GetAwaiter().GetResult();
        Assert(threeResult.Members[5].Ran && threeResult.Members[6].Ran
            && threeResult.Members[7].SkippedReason == "duplicate-limit", "three-layer max two deduplication");
        RulesDraft generatedDraft = new();
        generatedDraft.SetPotionPool([fire]);
        Battle emptySlot = root; emptySlot.Potions[0] = 0;
        Assert(PotionSearchSchedule.HasAllowedUse([emptySlot], generatedDraft.Build(), options),
            "empty root inventory does not exclude generated potions");
        Assert(!PotionSearchSchedule.HasAllowedUse([root], rules,
            options with { Potions = options.Potions with { ForceAllDisabled = true } }), "disabled potions skip");
        SearchBudget fixedBudget = new(1000, true, () => time);
        Assert(fixedBudget.Remaining(false) == 700 && fixedBudget.Remaining(true) == 1000,
            "one deadline and reserved potion phase");
        time += 1000;
        Assert(fixedBudget.Expired() && fixedBudget.Remaining(true) == 0, "absolute deadline");
        PotionPolicy protectedPolicy = options.Potions with
        {
            Strategy = new(PotionUseMode.Smart, [new(0, "FIRE_POTION", PotionDirective.Disabled)]),
        };
        Assert(!PotionSearchSchedule.HasAllowedUse([root], rules, options with { Potions = protectedPolicy }),
            "protected potion is not offered");
        PotionPolicy forcedPolicy = options.Potions with
        {
            Strategy = new(PotionUseMode.Smart, [new(0, "FIRE_POTION", PotionDirective.Force)]),
        };
        int[] forceSlots = [0];
        ushort[] forceIds = [fire];
        int[] forceCosts = [9];
        BeamOptions forced = options with { Potions = forcedPolicy, EnforceForcedPotionDirectives = true,
            ForcedPotionDirectives = new(forceSlots, forceIds, forceCosts, 0, 0) };
        CombatSearchRequest frozenPolicy = new(search with { Options = forced }, tiny);
        forceSlots[0] = 1;
        forceCosts[0] = 99;
        Assert(frozenPolicy.Case.Options.ForcedPotionDirectives.Slots[0] == 0
            && frozenPolicy.Case.Options.ForcedPotionDirectives.StrategicHpCosts[0] == 9,
            "request owns forced policy arrays");
        SearchCase policyCopy = frozenPolicy.Case;
        policyCopy.Options.ForcedPotionDirectives.PotionIds[0] = ushort.MaxValue;
        Assert(frozenPolicy.Case.Options.ForcedPotionDirectives.PotionIds[0] == fire,
            "request does not expose its forced policy arrays");
        forceSlots[0] = 0;
        forceCosts[0] = 9;
        Assert(PotionSearchSchedule.HasForcedDirectives(forced), "forced directive accepted");
        Assert(PortfolioBuilder.Build(tiny, power: false, forced: true).All(m => m.Potions == SearchPotionLimit.Allowed),
            "force survives every member limit");
        bool badMask = false;
        try { _ = PotionSearchSchedule.HasForcedDirectives(forced with
            { ForcedPotionDirectives = new([0], [fire], [9], 2, 0) }); }
        catch (InvalidDataException) { badMask = true; }
        Assert(badMask, "invalid forced-use mask rejected");
        bool missingForce = false;
        try { _ = PotionSearchSchedule.HasForcedDirectives(options with { Potions = forcedPolicy }); }
        catch (InvalidDataException) { missingForce = true; }
        Assert(missingForce, "force cannot silently become optional");
        using CancellationTokenSource cancel = new();
        cancel.Cancel();
        bool cancelled = false;
        try { _ = CombatSearchCoordinator.RunAsync(request, new(), Run, cancel.Token, () => time).AsTask().GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled, "cancellation propagates");
        Console.WriteLine("COMBAT_SEARCH_V2 basic contracts=pass");
    }
}
