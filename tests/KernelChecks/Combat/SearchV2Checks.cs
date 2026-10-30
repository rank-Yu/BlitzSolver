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

        foreach (SearchPlan preset in new[] { low, medium, high, extra })
        {
            SearchPlan boss = preset.ForEncounter(true);
            Assert(boss.TotalMilliseconds == checked(preset.TotalMilliseconds * 2)
                && boss with { TotalMilliseconds = preset.TotalMilliseconds } == preset,
                "boss doubles time only, for every tier");
            Assert(preset.ForEncounter(false) == preset, "ordinary encounter budget unchanged");
        }

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
        CheckQueenSupplement(rules, options, tiny, forced);
        CheckSetupPortfolio(rules, options, tiny, forced);
        Console.WriteLine("COMBAT_SEARCH_V2 basic contracts=pass");
    }
    private static void CheckQueenSupplement(Rules rules, BeamOptions options, SearchPlan plan, BeamOptions forced)
    {
        Assert(Registry.TryMonster("QUEEN", out ushort queenId)
            && Registry.TryMonster("TORCH_HEAD_AMALGAM", out _), "queen ids");
        Registry.TryMonster("TORCH_HEAD_AMALGAM", out ushort torchId);
        Battle root = default;
        root.PlayerTurn = 1; root.Player.CurrentHp = root.Player.MaxHp = 75;
        root.EnemyCount = root.RosterCount = 2;
        root.Roster[0] = 0; root.Roster[1] = 1;
        root.Enemies[0].MonsterId = torchId;
        root.Enemies[1].MonsterId = queenId;
        for (int i = 0; i < 2; i++)
        {
            root.Enemies[i].Creature.CurrentHp = root.Enemies[i].Creature.MaxHp = 419;
            root.Enemies[i].Flags = EnemyFlags.InCombat;
        }
        QueenSearchPolicy policy = QueenSearchPolicy.Capture(root, rules)
            ?? throw new InvalidDataException("queen opening not detected");
        Battle advanced = root; advanced.PlayerTurn = 2;
        Assert(QueenSearchPolicy.Capture(advanced, rules) is null, "no midcombat opening strategy");
        advanced = root; advanced.SetCounter(Counter.CardsPlayedThisTurn, 1);
        Assert(QueenSearchPolicy.Capture(advanced, rules) is null, "no partly played opening strategy");
        advanced = root; advanced.Enemies[0].Creature.CurrentHp = 0;
        Assert(!policy.Accepts(advanced), "early torch death rejected only by supplement");
        advanced.Enemies[1].Creature.CurrentHp = QueenSearchPolicy.QueenHpThreshold;
        Assert(policy.Accepts(advanced), "threshold opens torch kill");
        SearchCase search = new(rules, [root], options);
        SearchCase coded = search with { Options = options with
            { Scorer = new Scorer(options.Scorer!.RulesForCodec, baseOnly: true, queen: policy) } };
        SearchCase decoded = SearchCodec.DecodeRequest(SearchCodec.EncodeRequest(coded));
        Assert(decoded.Options.Scorer!.QueenPolicy == policy, "supplement survives single-search transport");
        Assert(SearchCodec.EncodeRequest(decoded).SequenceEqual(SearchCodec.EncodeRequest(coded)),
            "supplement request round trip stable");
        SearchPlan enabled = plan with { ExperimentalQueenSupplement = true };
        Assert(CombatSearchCodec.Decode(CombatSearchCodec.Encode(new(search, enabled))).Plan.ExperimentalQueenSupplement,
            "supplement survives combination transport");
        Assert(!CombatSearchCodec.Decode(CombatSearchCodec.Encode(new(search, plan))).Plan.ExperimentalQueenSupplement,
            "legacy/default requests remain disabled");
        List<BeamOptions> observed = [];
        long now = 0;
        ValueTask<SearchMemberMeasurement> Run(SearchCase item, SearchStorage _, CancellationToken __)
        {
            observed.Add(item.Options); now += 10;
            return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 10));
        }
        CombatSearchCoordinator.RunAsync(new(search, plan), new(), Run, CancellationToken.None, () => now)
            .AsTask().GetAwaiter().GetResult();
        TimeSpan firstShare = observed[0].SoftTimeBudget;
        observed.Clear(); now = 0;
        var result = CombatSearchCoordinator.RunAsync(new(search, enabled), new(), Run, CancellationToken.None, () => now)
            .AsTask().GetAwaiter().GetResult();
        Assert(observed.Count == 6 && observed.Take(5).All(o => o.Scorer!.QueenPolicy is null)
            && observed[5].Scorer!.QueenPolicy == policy, "only one supplement after ordinary searches");
        Assert(observed[0].SoftTimeBudget == firstShare, "supplement does not take ordinary time shares");
        Assert(observed.All(o => o.SearchMemoryBudgetBytes == plan.MemoryBytes
            && o.DeadlineExpired is not null), "supplement retains memory and deadline contracts");
        observed.Clear(); now = 0;
        ValueTask<SearchMemberMeasurement> Exhaust(SearchCase item, SearchStorage _, CancellationToken __)
        {
            observed.Add(item.Options);
            now += observed.Count == 5 ? plan.TotalMilliseconds : 10;
            return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 10));
        }
        result = CombatSearchCoordinator.RunAsync(new(search, enabled), new(), Exhaust, CancellationToken.None, () => now)
            .AsTask().GetAwaiter().GetResult();
        Assert(observed.Count == 5 && result.Members.Single(m => m.Member.Evaluation == SearchEvaluation.QueenSetup)
            .SkippedReason == "time", "expired budget skips supplemental search");
        observed.Clear(); now = 0;
        result = CombatSearchCoordinator.RunAsync(new(search with { Battles = [default] }, enabled),
            new(), Run, CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(result.Members.All(m => m.Member.Evaluation != SearchEvaluation.QueenSetup),
            "non-queen encounters get no extra member");
        observed.Clear(); now = 0;
        result = CombatSearchCoordinator.RunAsync(new(search with { Options = forced }, enabled),
            new(), Run, CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(result.Members.All(m => m.Member.Evaluation != SearchEvaluation.QueenSetup),
            "forced potion policy does not receive a no-potion supplement");
        var custom = enabled with { CustomMembers = [
            new(2, SearchEvaluation.Base, SearchRetention.Standard, SearchPotionLimit.None),
            new(2, SearchEvaluation.Base, SearchRetention.Standard, SearchPotionLimit.Allowed)] };
        observed.Clear(); now = 0;
        result = CombatSearchCoordinator.RunAsync(new(search, custom), new(), Run, CancellationToken.None, () => now)
            .AsTask().GetAwaiter().GetResult();
        Assert(result.Members.All(m => m.Member.Evaluation != SearchEvaluation.QueenSetup),
            "explicit custom portfolio stays authoritative");

        CheckSupplementPortfolio(rules, root, options, plan, forced, policy);
    }

    private static void CheckSupplementPortfolio(Rules rules, Battle root, BeamOptions options,
        SearchPlan plan, BeamOptions forced, QueenSearchPolicy policy)
    {
        Registry.TryPotion("FIRE_POTION", out ushort fire);
        root.PotionSlotCount = 2;
        root.Potions[0] = root.Potions[1] = (ushort)(fire + 1);
        options = options with { PowerCommitments = new([default], 1) };
        SearchPlan enabled = plan with { QueenStrategies = true, ExperimentalQueenSupplement = false,
            DefaultPotionSchedule = DefaultPotionSchedule.StagedPortfolio, TotalMilliseconds = 120_000 };
        SearchCase search = new(rules, [root], options);
        List<BeamOptions> observed = [];
        long now = 0;
        ValueTask<SearchMemberMeasurement> Run(SearchCase item, SearchStorage _, CancellationToken __)
        {
            observed.Add(item.Options); now += 10;
            return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 10));
        }
        CombatSearchResult baseline = CombatSearchCoordinator.RunAsync(
            new(search, enabled with { QueenStrategies = false }), new(), Run, CancellationToken.None, () => now)
            .AsTask().GetAwaiter().GetResult();
        TimeSpan[] originalShares = observed.Take(6).Select(o => o.SoftTimeBudget).ToArray();
        observed.Clear(); now = 0;
        CombatSearchResult result = CombatSearchCoordinator.RunAsync(new(search, enabled), new(), Run,
            CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(result.Members.Length == 24 && observed.Count == 24, "queen runs eight members in each potion stage");
        for (int stage = 0; stage < 3; stage++)
        {
            int at = stage * 8;
            Assert(result.Members.Skip(at).Take(6).Select(m => m.Member)
                .SequenceEqual(baseline.Members.Skip(stage * 6).Take(6).Select(m => m.Member)),
                "queen keeps original stage members");
            Assert(result.Members[at + 6].Member == new SearchMember(enabled.BaselineWidth,
                SearchEvaluation.QueenSetup, SearchRetention.Standard, result.Members[at].Member.Potions),
                "delay-kill member follows each stage");
            Assert(result.Members[at + 7].Member == new SearchMember(enabled.NarrowWidth,
                SearchEvaluation.Standard, SearchRetention.QueenKillTiming, result.Members[at].Member.Potions),
                "kill-timing member follows delay-kill at narrow width");
            Assert(observed[at + 6].Scorer!.QueenPolicy is { PreserveKillTiming: false }
                && observed[at + 7].Scorer!.QueenPolicy is { PreserveKillTiming: true },
                "supplement mode is frozen on each request");
        }
        Assert(observed.Take(6).Select(o => o.SoftTimeBudget).SequenceEqual(originalShares),
            "queen additions do not divide original no-potion time shares");
        Assert(observed.All(o => o.SearchMemoryBudgetBytes == enabled.MemoryBytes && o.DeadlineExpired != null),
            "all queen members share the configured memory ceiling and deadline");
        CombatSearchRequest decodedPlan = CombatSearchCodec.Decode(CombatSearchCodec.Encode(new(search, enabled.ForEncounter(true))));
        Assert(decodedPlan.Plan.QueenStrategies && decodedPlan.Plan.TotalMilliseconds == 240_000,
            "v7 preserves queen defaults and resolved boss budget without doubling again");
        QueenSearchPolicy timing = policy with { PreserveKillTiming = true };
        Battle deadTorch = root; deadTorch.Enemies[policy.TorchSlot].Creature.CurrentHp = 0;
        Assert(timing.Accepts(deadTorch) && timing.Accepts(root) && timing.Bonus(root) == 0,
            "timing preservation allows either kill order and adds no score reward");
        SearchCase coded = search with { Options = options with {
            Scorer = new Scorer(options.Scorer!.RulesForCodec, queen: timing) } };
        byte[] bytes = SearchCodec.EncodeRequest(coded);
        SearchCase decoded = SearchCodec.DecodeRequest(bytes);
        Assert(decoded.Options.Scorer!.QueenPolicy == timing
            && SearchCodec.EncodeRequest(decoded).SequenceEqual(bytes), "QSP2 timing policy round trip");

        observed.Clear(); now = 0;
        BeamOptions limited = options with { Potions = options.Potions with { MaximumUses = 1 } };
        result = CombatSearchCoordinator.RunAsync(new(search with { Options = limited }, enabled),
            new(), Run, CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(result.Members[22].SkippedReason == "duplicate-limit" && result.Members[23].SkippedReason == "duplicate-limit",
            "both queen members deduplicate one versus allowed when effective potion limits match");

        observed.Clear(); now = 0;
        result = CombatSearchCoordinator.RunAsync(new(search with { Options = forced with {
            PowerCommitments = options.PowerCommitments } }, enabled), new(), Run,
            CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(observed.Count == 8 && result.Members.All(m => m.Member.Potions == SearchPotionLimit.Allowed)
            && observed.All(o => o.EnforceForcedPotionDirectives),
            "forced potions get both supplements without losing forced directives");

        observed.Clear(); now = 0;
        ValueTask<SearchMemberMeasurement> Exhaust(SearchCase item, SearchStorage _, CancellationToken __)
        {
            observed.Add(item.Options); now += observed.Count == 6 ? enabled.TotalMilliseconds : 10;
            return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 10));
        }
        result = CombatSearchCoordinator.RunAsync(new(search, enabled), new(), Exhaust,
            CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(observed.Count == 6 && result.Members.Skip(6).All(m => !m.Ran),
            "neither queen supplement extends an exhausted global deadline");
    }

    private static void CheckSetupPortfolio(Rules rules, BeamOptions options, SearchPlan plan, BeamOptions forced)
    {
        for (int act = 0; act < 3; act++)
        {
            Assert(SearchPlan.IsLongEncounter(true, false, act), "all act bosses have setup");
            Assert(SearchPlan.IsLongEncounter(false, true, act) == (act > 0), "only late act elites have setup");
            Assert(!SearchPlan.IsLongEncounter(false, false, act), "normal rooms unchanged");
        }
        Registry.TryPotion("FIRE_POTION", out ushort fire);
        Battle root = default;
        root.PotionSlotCount = 3;
        root.Potions[0] = root.Potions[1] = root.Potions[2] = (ushort)(fire + 1);
        options = options with { PowerCommitments = new([default], 1) };
        SearchCase search = new(rules, [root], options);
        SearchPlan enabled = plan with { SetupStrategies = true, QueenStrategies = true,
            DefaultPotionSchedule = DefaultPotionSchedule.StagedPortfolio, TotalMilliseconds = 240_000 };
        List<BeamOptions> seen = [];
        long now = 0;
        ValueTask<SearchMemberMeasurement> Run(SearchCase item, SearchStorage _, CancellationToken __)
        {
            seen.Add(item.Options); now += 10;
            return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 10));
        }
        CombatSearchResult baseline = CombatSearchCoordinator.RunAsync(new(search, enabled with { SetupStrategies = false }),
            new(), Run, CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        TimeSpan[] shares = seen.Take(6).Select(o => o.SoftTimeBudget).ToArray();
        seen.Clear(); now = 0;
        CombatSearchResult result = CombatSearchCoordinator.RunAsync(new(search, enabled), new(), Run,
            CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(result.Members.Length == 24 && seen.Count == 24, "six generic supplements across three stages");
        for (int stage = 0; stage < 3; stage++)
        {
            int at = stage * 8;
            Assert(result.Members.Skip(at).Take(6).Select(m => m.Member).SequenceEqual(
                baseline.Members.Skip(stage * 6).Take(6).Select(m => m.Member)), "original six preserved");
            Assert(result.Members.Skip(at + 6).Take(2).Select(m => m.Member.Width)
                .SequenceEqual(new[] { enabled.NarrowWidth, enabled.WideWidth }), "narrow then wide setup");
            Assert(seen.Skip(at).Take(6).All(o => !o.Scorer!.SetupPreference)
                && seen.Skip(at + 6).Take(2).All(o => o.Scorer!.SetupPreference && o.Scorer.QueenPolicy is null),
                "only added members use generic setup scoring");
        }
        Assert(seen.Take(6).Select(o => o.SoftTimeBudget).SequenceEqual(shares), "original no-potion shares preserved");
        byte[] bytes = CombatSearchCodec.Encode(new(search, enabled));
        CombatSearchRequest decoded = CombatSearchCodec.Decode(bytes);
        Assert(decoded.Plan.SetupStrategies && decoded.Plan.TotalMilliseconds == 240_000
            && CombatSearchCodec.Encode(decoded).SequenceEqual(bytes), "v8 frozen plan round trip");
        SearchCase setup = search with { Options = options with { Scorer = new(options.Scorer!.RulesForCodec, setup: SetupSearchPolicy.Capture(root, rules)) } };
        byte[] single = SearchCodec.EncodeRequest(setup);
        Assert(SearchCodec.DecodeRequest(single).Options.Scorer!.SetupPreference
            && SearchCodec.EncodeRequest(SearchCodec.DecodeRequest(single)).SequenceEqual(single), "SUP1 single-member replay");
        Battle multi = root;
        multi.EnemyCount = multi.RosterCount = 2;
        multi.Roster[0] = 0; multi.Roster[1] = 1;
        multi.Enemies[0].CombatId = 11; multi.Enemies[1].CombatId = 22;
        multi.Enemies[0].Creature.CurrentHp = multi.Enemies[0].Creature.MaxHp = 100;
        multi.Enemies[1].Creature.CurrentHp = 80; multi.Enemies[1].Creature.MaxHp = 200;
        SetupSearchPolicy focus = SetupSearchPolicy.Capture(multi, rules, focused: true);
        Assert(focus.FocusSlot == 1 && focus.FocusHp(multi) == 80,
            "focused setup chooses original maximum HP, not current attack threat or character");
        SearchCase focusedCase = search with { Battles = [multi], Options = options with {
            Scorer = new(options.Scorer!.RulesForCodec, baseOnly: true, setup: focus) } };
        byte[] focusedBytes = SearchCodec.EncodeRequest(focusedCase);
        Scorer restoredFocus = SearchCodec.DecodeRequest(focusedBytes).Options.Scorer!;
        Assert(restoredFocus.SetupPolicy!.Focused && restoredFocus.SetupPolicy.FocusHp(multi) == 80
            && SearchCodec.EncodeRequest(SearchCodec.DecodeRequest(focusedBytes)).SequenceEqual(focusedBytes),
            "focused rank and frozen target survive single-member transport");
        multi.Enemies[1].CombatId = 33;
        Assert(focus.FocusHp(multi) == 0, "recycled enemy slot does not inherit focus");
        Assert(SetupSearchPolicy.Capture(root, rules, focused: true).FocusSlot == -1,
            "single/no-enemy roots do not gain an extra target weight");
        ScoreFeatures terminal = new() { Score = 123, SetupPotentialHp = 100 };
        Assert(setup.Options.Scorer!.BeamRankScore(root, terminal, true, false) == 123, "setup never rewards terminal results");

        seen.Clear(); now = 0;
        result = CombatSearchCoordinator.RunAsync(new(search with { Options = options with {
            Potions = options.Potions with { MaximumUses = 1 } } }, enabled), new(), Run,
            CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(result.Members.Skip(22).All(m => !m.Ran && m.SkippedReason == "duplicate-limit"),
            "same effective potion limit is not searched twice");
        seen.Clear(); now = 0;
        result = CombatSearchCoordinator.RunAsync(new(search with { Options = forced with {
            PowerCommitments = options.PowerCommitments } }, enabled), new(), Run,
            CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(seen.Count == 8 && seen.All(o => o.EnforceForcedPotionDirectives)
            && result.Members.All(m => m.Member.Potions == SearchPotionLimit.Allowed),
            "forced potion stage retains both setup members and directives");

        // The generic members replace QueenSetup; only kill-timing retention is retained.
        SearchMember[] queen = SupplementPortfolio.AppendToStages(
            PortfolioBuilder.Build(enabled, true, false), enabled, queen: true);
        Assert(queen.Length == 27 && queen.Count(m => m.Evaluation is SearchEvaluation.Setup or SearchEvaluation.FocusedSetup) == 6
            && queen.Count(m => m.Retention == SearchRetention.QueenKillTiming) == 3
            && queen.All(m => m.Evaluation != SearchEvaluation.QueenSetup), "old queen setup replaced");

        seen.Clear(); now = 0;
        ValueTask<SearchMemberMeasurement> Exhaust(SearchCase item, SearchStorage _, CancellationToken __)
        {
            seen.Add(item.Options); now += enabled.TotalMilliseconds;
            return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, enabled.TotalMilliseconds));
        }
        result = CombatSearchCoordinator.RunAsync(new(search, enabled), new(), Exhaust,
            CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(seen.Count == 1 && result.Members.Skip(1).All(m => !m.Ran), "supplements respect global deadline");
    }

}
