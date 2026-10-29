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
        SearchPlan tiny = new(3, 2, 4, 1, 1000, 100_000_000);
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
        Assert(observed[5].Potions.MaximumUses == 1 && !observed[6].Potions.ForceAllDisabled, "bounded then broad potions");
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
        BeamOptions forced = options with { Potions = forcedPolicy, EnforceForcedPotionDirectives = true,
            ForcedPotionDirectives = new([0], [fire], [9], 0, 0) };
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
