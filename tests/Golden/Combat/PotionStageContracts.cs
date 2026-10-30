using Sts2Solver.Ai.Combat;
using Sts2Solver.Ai.Combat.Policy;
using Sts2Solver.Ai.Combat.Scoring;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Golden;

internal static class PotionStageContracts
{
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidDataException("potion-stage: " + message); }

    internal static void Run(SearchCase fixture, SearchPlan plan)
    {
        Assert(Registry.TryPotion("FIRE_POTION", out ushort fire), "fire potion");
        Assert(Registry.TryPotion("STRENGTH_POTION", out ushort cheap), "cheap potion fixture");
        Assert(Registry.TryPotion("FRUIT_JUICE", out ushort fruit), "fruit juice");
        Assert(Registry.TryCard("FEED", out ushort feed), "Feed");
        ScoreRules scores = fixture.Options.Scorer!.RulesForCodec;
        Assert(!scores.GrowthBudgets.IsEnabled && !scores.RequiredGrowthRewards.IsEnabled
            && scores.RelicCounterRules.Length == 0, "neutral score fixture");
        int savedFire = scores.PotionStrategicCost[fire], savedCheap = scores.PotionStrategicCost[cheap];
        try
        {
            scores.PotionStrategicCost[fire] = 9;
            scores.PotionStrategicCost[cheap] = 1;
            RulesDraft draft = new();
            draft.SetPotion(fire, new PotionStats { Target = TargetType.AnyEnemy, Rarity = PotionRarity.Common,
                StrategicCost = 9, Vars = [], VarProps = [] });
            draft.SetPotion(cheap, new PotionStats { Target = TargetType.Self, Rarity = PotionRarity.Common,
                StrategicCost = 1, Vars = [], VarProps = [] });
            Rules rules = draft.Build();
            Battle root = default;
            root.Player.CurrentHp = 75; root.Player.MaxHp = 75;
            root.PotionSlotCount = 1; root.Potions[0] = (ushort)(fire + 1);
            BeamOptions options = fixture.Options with
            {
                InitialExplicitPotionUses = 0, MinimumPotionUses = 0, HasUnboundedGrowthTargets = false,
                HpTargetStop = null, EnforceForcedPotionDirectives = false,
                ForcedPotionDirectives = ForcedPotionDirectives.None, TheftPolicy = null,
                EffectivePotionPolicy = PotionUseMode.Smart, PotionReplacementHpCredit = 0,
                Ordering = new(75, 75, BossHpRelief.None, PostCombatRelicHealProfile.None),
                Potions = new(int.MinValue, null, new(PotionUseMode.Smart, []), PotionUseMode.Smart, false),
            };
            BeamSolver.Candidate selected = default(BeamSolver.Candidate) with
            {
                Key = default(BeamSolver.RouteKey) with { Won = true, Survives = true, StrategicHpDeficit = 8 },
                ForcedUsesSatisfied = true,
            };
            bool Skip(Battle b, Rules r, BeamOptions o, BeamSolver.Candidate? c)
                => PotionStageGate.CanSkip([b], r, o, c, c);
            Assert(Skip(root, rules, options, selected), "eight loss cannot justify cost nine");
            BeamSolver.Candidate oldFree = selected with { Key = selected.Key with { StrategicHpDeficit = 28 } };
            Assert(!PotionStageGate.CanSkip([root], rules, options, selected, oldFree),
                "a later eight-loss zero-potion result must not replace the 28-loss Smart baseline");
            Assert(!Skip(root, rules, options, selected with { Key = selected.Key with { StrategicHpDeficit = 9 } }),
                "equal benefit can still improve tie breaks");
            Battle wounded = root; wounded.Player.CurrentHp = 70;
            Assert(!Skip(wounded, rules, options, selected), "pre-existing wounds can be healed");
            Battle history = root; history.SetCounter(Counter.RecoveredHp, 20);
            Assert(!Skip(history, rules, options, selected), "already recovered HP keeps search open");
            history = root; history.SetCounter(Counter.CumulativeHpLost, 1);
            Assert(!Skip(history, rules, options, selected), "existing damage ledger keeps search open");
            history = root; history.SetCounter(Counter.DeathSaveUseCount, 1);
            Assert(!Skip(history, rules, options, selected), "existing death-save use keeps search open");
            Assert(!Skip(root, rules, options with { InitialAutomaticPotionUses = 1 }, selected),
                "fixed-prefix automatic potion keeps search open");
            Assert(!Skip(root, rules, options with { PotionReplacementHpCredit = 1 }, selected),
                "one reward credit reduces the minimum cost");
            Assert(!Skip(root, rules, options, selected with { ExplicitPotionUseCount = 1 }),
                "after a one-potion victory search the unrestricted stage again");
            Assert(!Skip(root, rules, options with { MinimumPotionUses = 1 }, selected), "minimum use bypasses gate");
            Assert(!Skip(root, rules, options with { MinimumPotionUses = 2 },
                    selected with { Key = selected.Key with { StrategicHpDeficit = 0 } }),
                "minimum two bypasses gate even at zero loss");
            Assert(!Skip(root, rules, options with { EnforceForcedPotionDirectives = true }, selected),
                "forced directives bypass gate at eight loss");
            Assert(!Skip(root, rules, options, selected with { ProjectedDeathSaveUseCount = 1 }), "death save bypasses gate");
            Assert(!Skip(root, rules, options with { HpTargetStop = new(0, 0, 0) }, selected), "goal bypasses gate");
            RulesDraft lowPool = new();
            lowPool.SetPotion(fire, new PotionStats { Target = TargetType.AnyEnemy, Rarity = PotionRarity.Common,
                StrategicCost = 9, Vars = [], VarProps = [] });
            lowPool.SetPotion(cheap, new PotionStats { Target = TargetType.Self, Rarity = PotionRarity.Common,
                StrategicCost = 1, Vars = [], VarProps = [] });
            lowPool.SetPotionPool([cheap]);
            Assert(!Skip(root, lowPool.Build(), options, selected), "generated low-cost potion is included");
            PotionStrategySnapshot protectedCheap = new(PotionUseMode.Smart,
                [new(0, "STRENGTH_POTION", PotionDirective.Disabled)]);
            Assert(Skip(root, lowPool.Build(), options with { Potions = options.Potions with { Strategy = protectedCheap } }, selected),
                "disabled generated bottle does not reduce the allowed minimum");
            RulesDraft zeroPool = new();
            zeroPool.SetPotion(fire, new PotionStats { Target = TargetType.AnyEnemy, Rarity = PotionRarity.Common,
                StrategicCost = 0, Vars = [], VarProps = [] });
            Assert(!Skip(root, zeroPool.Build(), options, selected), "zero-cost potion keeps search open");
            Assert(!Skip(root, rules, options with { TheftPolicy = TheftPolicy.PreserveResources },
                selected with { Key = selected.Key with { OutstandingStolenResource = 1 } }), "stolen resources bypass gate");
            RulesDraft fruitPool = new();
            fruitPool.SetPotion(fire, new PotionStats { Target = TargetType.AnyEnemy, Rarity = PotionRarity.Common,
                StrategicCost = 9, Vars = [], VarProps = [] });
            fruitPool.SetPotionPool([fruit]);
            Assert(!Skip(root, fruitPool.Build(), options, selected), "possible max HP potion bypasses gate");
            RulesDraft feedRules = new();
            feedRules.SetCard(feed, 0, new CardStats { Type = CardType.Attack, Target = TargetType.AnyEnemy,
                Rarity = CardRarity.Rare, EnergyCost = 1, CostsX = false, StarCost = -1,
                StarCostsX = false, Keywords = Keywords.None, MaxUpgradeLevel = 1, Vars = [] });
            Assert(!Skip(root, feedRules.Build(), options, selected), "possible max HP card bypasses gate");
            Assert(Skip(root, rules, options with { Ordering = new(75, 75, BossHpRelief.ActClearHeal,
                PostCombatRelicHealProfile.None) }, selected), "boss HP policy is applied");
        }
        finally
        {
            scores.PotionStrategicCost[fire] = savedFire;
            scores.PotionStrategicCost[cheap] = savedCheap;
        }

        Battle scheduleRoot = fixture.Battles[0];
        scheduleRoot.PotionSlotCount = 1; scheduleRoot.Potions[0] = (ushort)(fire + 1);
        BeamOptions scheduleOptions = fixture.Options with
        {
            InitialExplicitPotionUses = 0, MinimumPotionUses = 0,
            EnforceForcedPotionDirectives = false, ForcedPotionDirectives = ForcedPotionDirectives.None,
            PowerCommitments = new PowerCommitmentCatalog(new PowerCommitmentRule[1], 0),
            Potions = new(int.MinValue, 1, new(PotionUseMode.Smart, []), PotionUseMode.Smart, false),
        };
        SearchCase scheduleCase = fixture with { Battles = [scheduleRoot], Options = scheduleOptions };
        long clock = 0;
        List<BeamOptions> observed = [];
        ValueTask<SearchMemberMeasurement> Run(SearchCase item, SearchStorage _, CancellationToken __)
        { observed.Add(item.Options); clock += 10; return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 10)); }
        CombatSearchResult Solve(SearchCase c, SearchPlan p)
        { clock = 0; observed.Clear(); return CombatSearchCoordinator.RunAsync(new(c, p), new(), Run,
            CancellationToken.None, () => clock).AsTask().GetAwaiter().GetResult(); }
        SearchPlan staged = plan with { CustomMembers = [], DefaultPotionSchedule = DefaultPotionSchedule.StagedPortfolio };
        SearchMember[] phaseMembers = PortfolioBuilder.Build(staged, power: true, forced: false);
        List<SearchMemberResult> done = phaseMembers.Take(6).Select(m => new SearchMemberResult(m,
            true, null, 0, 0, "frontier-empty", false, null, null, false, 0, 0)).ToList();
        Assert(phaseMembers.Skip(6).Take(6).All(m => PotionStageGate.HasRunCombination(done, m))
            && !PotionStageGate.HasRunCombination(done, phaseMembers[13])
            && phaseMembers.Skip(12).Where((_, i) => i != 1).All(m => PotionStageGate.HasRunCombination(done, m)),
            "economic gate preserves the previously unrun ordinary baseline-width combination");
        done[1] = done[1] with { Ran = false };
        Assert(!PotionStageGate.HasRunCombination(done, phaseMembers[7]), "an unrun combination is not covered");
        CombatSearchResult one = Solve(scheduleCase, staged);
        Assert(one.Members.Length == 18 && one.Members.Take(12).All(m => m.Ran)
            && one.Members[13].Ran && one.Members[13].Member.Width == staged.BaselineWidth
            && one.Members.Skip(12).Where((_, i) => i != 1).All(m => m.SkippedReason == "duplicate-limit"),
            "maximum one preserves distinct Allowed baseline width and deduplicates the others");
        SearchCase minTwo = scheduleCase with { Options = scheduleOptions with
        { MinimumPotionUses = 2, Potions = scheduleOptions.Potions with { MaximumUses = 2 } } };
        CombatSearchResult required = Solve(minTwo, staged);
        Assert(required.Members.Length == 6 && required.Members.All(m => m.Ran && m.Member.Potions == SearchPotionLimit.Allowed)
            && required.Members.Select(m => m.Member.Width).SequenceEqual(new[] { 12, 12, 16, 12, 12, 5 })
            && observed.All(o => o.MinimumPotionUses == 2 && o.Potions.MaximumUses == 2 && o.CollectPotionCandidates),
            "minimum two directly runs six audited Allowed members");
        SearchCase requireMode = scheduleCase with { Options = scheduleOptions with
        { EffectivePotionPolicy = PotionUseMode.RequireAtLeastOne, MinimumPotionUses = 0 } };
        CombatSearchResult modeResult = Solve(requireMode, staged);
        Assert(modeResult.Members.Length == 6 && modeResult.Members.All(m => m.Ran && m.Member.Potions == SearchPotionLimit.Allowed)
            && observed.All(o => o.EffectivePotionPolicy == PotionUseMode.RequireAtLeastOne
                && o.MinimumPotionUses == 0 && o.AuditPotionPolicy && o.CollectPotionCandidates),
            "RequireAtLeastOne without slot directives directly runs audited Allowed members");
        SearchCase minOneUsed = scheduleCase with { Options = scheduleOptions with
        { InitialExplicitPotionUses = 1, MinimumPotionUses = 1, Potions = scheduleOptions.Potions with { MaximumUses = 1 } } };
        CombatSearchResult alreadyRequired = Solve(minOneUsed, staged);
        Assert(alreadyRequired.Members.Length == 6 && alreadyRequired.Members.All(m => m.Ran),
            "already-satisfied minimum still runs Allowed baseline");
        Assert(Registry.TryPotion("STRENGTH_POTION", out ushort secondPotion), "forced second potion");
        Battle forcedRoot = scheduleRoot;
        forcedRoot.PotionSlotCount = 2; forcedRoot.Potions[1] = (ushort)(secondPotion + 1);
        PotionStrategySnapshot forcedStrategy = new(PotionUseMode.Smart,
            [new(0, "FIRE_POTION", PotionDirective.Force), new(1, "STRENGTH_POTION", PotionDirective.Force)]);
        SearchCase forcedCase = fixture with { Battles = [forcedRoot], Options = scheduleOptions with
        {
            MinimumPotionUses = 2, EnforceForcedPotionDirectives = true,
            ForcedPotionDirectives = new([0, 1], [fire, secondPotion], [9, 9], 0, 0),
            Potions = new(int.MinValue, 2, forcedStrategy, PotionUseMode.Smart, false),
        } };
        CombatSearchResult forcedResult = Solve(forcedCase, staged);
        Assert(forcedResult.Members.Length == 6 && forcedResult.Members.All(m => m.Ran && m.Member.Potions == SearchPotionLimit.Allowed)
            && observed.All(o => o.EnforceForcedPotionDirectives && o.ForcedPotionDirectives.Count == 2
                && o.Potions.MaximumUses == 2 && o.MinimumPotionUses == 2), "two forced bottles retain slots and cap");
        SearchMember none = new(8, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.None);
        SearchMember oneMember = none with { Potions = SearchPotionLimit.One };
        SearchPlan custom = staged with { CustomMembers = [none, oneMember, oneMember, oneMember with { Potions = SearchPotionLimit.Allowed }] };
        CombatSearchResult customResult = Solve(scheduleCase, custom);
        Assert(customResult.Members.All(m => m.Ran) && customResult.Members.Select(m => m.Member).SequenceEqual(custom.CustomMembers),
            "custom order and intentional duplicates are preserved");
        Console.WriteLine("POTION_STAGE costs/history/targets/plans=pass");
    }
}
