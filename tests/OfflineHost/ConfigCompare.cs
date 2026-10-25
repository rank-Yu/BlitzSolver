// M2a-4 evaluation-config comparison: compile ScoreRules/Bound from the native root, assemble the
// same SREQ request the M0 harness wrote, and compare it with d1/<root>/config.br byte for byte.
using System.IO.Compression;
using System.Text;
using BlitzSolver.Engine;
using BlitzSolver.Golden;
using BlitzSolver.Import;
using BlitzSolver.Search;
using BlitzSolver.Search.Scoring;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace OfflineHost;

internal enum ConfigStatus
{
    Same,
    Different,
    Rejected,
}

internal sealed record ConfigResult(ConfigStatus Status, string Reason, string FirstField,
    string Detail, bool SubParts)
{
    public string Line => $"CONFIG{(SubParts ? " scope=subparts" : "")} " + Status switch
    {
        ConfigStatus.Same => "status=same",
        ConfigStatus.Different => $"status=diff field={FirstField}",
        _ => $"status=reject reason={Reason}",
    };
}

internal static class ConfigCompare
{
    public static ConfigResult Run(string data, string root, CombatState state,
        IReadOnlyList<string> setupSelections, bool preInjectionSetupRoot,
        OfflineRequest request)
    {
        if (request.FlashPrefixActions.Length != 0 || request.FlashPrefixPlan.Length != 0)
        {
            return new(ConfigStatus.Rejected, ImportReason.RootPhase, "",
                "前缀根的评估配置留给 M2a-5", false);
        }
        Player player = LocalContext.GetMe(state)
            ?? throw new InvalidOperationException("找不到本地玩家。");
        if (state.RunState is not RunState runState)
            return new(ConfigStatus.Rejected, ImportReason.RulesMismatch, "", "原生战斗的 RunState 不是具体 RunState", preInjectionSetupRoot);

        Rules rules;
        try
        {
            rules = RulesCompiler.Compile(state, player, runState);
        }
        catch (UnsupportedImportException error)
        {
            return new(ConfigStatus.Rejected, error.Reason, "", error.Detail, preInjectionSetupRoot);
        }
        catch (UnsupportedException error)
        {
            return new(ConfigStatus.Rejected, "core-unsupported", "", error.Message, preInjectionSetupRoot);
        }
        catch (CapacityException error)
        {
            return new(ConfigStatus.Rejected, ImportReason.Capacity, error.Region, error.Message, preInjectionSetupRoot);
        }

        Battle battle;
        try
        {
            battle = BattleImporter.Import(state, player, rules,
                preInjectionSetupRoot ? ImportOptions.Default with { TurnSetupRoot = true } : ImportOptions.Default);
        }
        catch (UnsupportedImportException error)
        {
            return new(ConfigStatus.Rejected, error.Reason, "", error.Detail, preInjectionSetupRoot);
        }
        catch (UnsupportedException error)
        {
            return new(ConfigStatus.Rejected, "core-unsupported", "", error.Message, preInjectionSetupRoot);
        }
        catch (CapacityException error)
        {
            return new(ConfigStatus.Rejected, ImportReason.Capacity, error.Region, error.Message, preInjectionSetupRoot);
        }

        byte[] expectedBytes;
        SearchCase expected;
        GoldenRootRecord golden;
        try
        {
            string directory = Path.Combine(data, "d1", root);
            expectedBytes = ReadBrotli(Path.Combine(directory, "config.br"));
            expected = SearchCodec.DecodeRequest(expectedBytes);
            golden = GoldenData.Decode(ReadBrotli(Path.Combine(directory, "golden.br")));
        }
        catch (InvalidDataException error)
        {
            return new(ConfigStatus.Rejected, "invalid-golden", "", error.Message, preInjectionSetupRoot);
        }

        SearchPolicy policy;
        try
        {
            policy = ApplyOverrides(SearchPolicyCapture.Capture(state, player), request, player);
        }
        catch (UnsupportedImportException error)
        {
            return new(ConfigStatus.Rejected, error.Reason, "", error.Detail, preInjectionSetupRoot);
        }
        int actionCount = expected.Options.InitialActionCount;
        EvaluationRootFacts facts = EvaluationRootFacts.Default with
        {
            InitialActionCount = actionCount,
            TurnSetupRoot = preInjectionSetupRoot,
        };

        Scorer scorer;
        BoundFeatures bound;
        try
        {
            scorer = EvaluationConfigCompiler.CompileScorer(state, player, rules, battle, policy, facts);
            bound = EvaluationConfigCompiler.CompileBound(state, player, battle, policy, facts);
        }
        catch (UnsupportedImportException error)
        {
            return new(ConfigStatus.Rejected, error.Reason, "", error.Detail, preInjectionSetupRoot);
        }
        catch (UnsupportedException error)
        {
            return new(ConfigStatus.Rejected, "core-unsupported", "", error.Message, preInjectionSetupRoot);
        }
        catch (CapacityException error)
        {
            return new(ConfigStatus.Rejected, ImportReason.Capacity, error.Region, error.Message, preInjectionSetupRoot);
        }

        if (preInjectionSetupRoot)
        {
            string? section = CompareSubParts(expected, scorer, bound, out string detail);
            if (section != null)
                return new(ConfigStatus.Different, "", section, detail, true);
            (Battle _, int selectedIndex) = ImportCompare.SelectExpectedRoot(golden, setupSelections, true);
            return CheckHashes(golden.Roots[selectedIndex], battle, scorer, bound, actionCount, true);
        }

        SearchCase actual = EvaluationConfigCompiler.CompileRequest(
            state, player, rules, battle, policy, facts);
        byte[] actualBytes = SearchCodec.EncodeRequest(actual);
        if (!actualBytes.AsSpan().SequenceEqual(expectedBytes))
        {
            string? section = DescribeRequestDifference(expected, actual, out string detail);
            return new(ConfigStatus.Different, "", section ?? "<bytes>", detail, false);
        }

        return CheckHashes(golden.Roots[0], battle, scorer, bound, actionCount, false);
    }



    private static SearchPolicy ApplyOverrides(SearchPolicy policy, OfflineRequest request,
        Player player)
    {
        if (request.GrowthBudgets is { } budgets)
            policy = policy with { GrowthBudgets = budgets.ToPolicy() };
        if (request.IgnoreLongTermRewards is { } ignore)
            policy = policy with { IgnoreLongTermRewards = ignore };
        if (request.ActTransitionBossHpStrategy is { Length: > 0 } actStrategy)
            policy = policy with { ActTransitionBossHpStrategy = ParseBossStrategy(actStrategy) };
        if (request.FinalBossHpStrategy is { Length: > 0 } finalStrategy)
            policy = policy with { FinalBossHpStrategy = ParseBossStrategy(finalStrategy) };
        if (request.TheftPolicyForTest is { Length: > 0 } theft)
            policy = policy with { TheftPolicy = ParseTheftPolicy(theft) };
        if (request.Act3BossStrategyForTest is { } act3)
            policy = policy with { Act3BossStrategy = act3 };
        if (request.BaseScoreOnlyForTest is { } baseOnly)
            policy = policy with { BaseScoreOnly = baseOnly };
        if (request.ContinuousThreatRankingForTest is { } continuous)
            policy = policy with { ContinuousThreatRanking = continuous };
        if (request.BeamWeightTermForTest is { Length: > 0 } term
            || request.BeamWeightScaleForTest is { } scale)
        {
            if (request.BeamWeightTermForTest is not { Length: > 0 } termText
                || request.BeamWeightScaleForTest is not { } scaleValue)
            {
                throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                    "BeamWeightTermForTest 与 BeamWeightScaleForTest 必须同时给出");
            }
            if (!Enum.TryParse(termText, ignoreCase: true, out SearchBeamWeightTerm parsed))
            {
                throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                    $"未知 BeamWeightTermForTest：{termText}");
            }
            policy = policy with
            {
                BeamWeightPerturbation = new SearchBeamWeightPerturbation(parsed, scaleValue),
            };
        }
        if (request.RelicCounterRules.Length != 0)
            policy = policy with { RelicTargets = ParseRelicTargets(request.RelicCounterRules, player) };
        return policy;
    }

    private static SearchBossHpStrategy ParseBossStrategy(string text)
        => Enum.TryParse(text, ignoreCase: true, out SearchBossHpStrategy value)
            ? value
            : throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                $"未知 Boss HP 策略：{text}");

    private static SearchTheftPolicy ParseTheftPolicy(string text)
        => Enum.TryParse(text, ignoreCase: true, out SearchTheftPolicy value)
            ? value
            : throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                $"未知偷窃策略：{text}");

    private static IReadOnlyList<SearchRelicCounterTarget> ParseRelicTargets(
        IReadOnlyList<OfflineRelicCounterRule> rules, Player player)
    {
        List<SearchRelicCounterTarget> targets = [];
        HashSet<SearchRelicCounter> seen = [];
        foreach (OfflineRelicCounterRule rule in rules)
        {
            if (!Enum.TryParse(rule.Id, ignoreCase: true, out SearchRelicCounter id)
                || !Enum.IsDefined(id))
            {
                throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                    $"未知遗物计数目标：{rule.Id}");
            }
            if (!rule.Enabled || !OwnsRelic(player, id))
                continue;
            int period = RelicPeriod(id);
            if (!seen.Add(id) || rule.Minimum < 0 || rule.Maximum < rule.Minimum
                || rule.Maximum >= period || rule.HpAllowance is < 0 or > 1000
                || rule.Priority is < 1 or > 3)
            {
                throw new UnsupportedImportException(ImportReason.UnsupportedPolicy,
                    $"遗物计数规则非法：{rule.Id}");
            }
            if (rule.Minimum > 0 || rule.Maximum < period - 1)
            {
                targets.Add(new SearchRelicCounterTarget(id, rule.Minimum, rule.Maximum,
                    rule.HpAllowance, period, rule.Priority));
            }
        }
        targets.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        return targets;
    }

    private static bool OwnsRelic(Player player, SearchRelicCounter id)
    {
        foreach (var relic in player.Relics)
        {
            if (relic.IsMelted)
                continue;
            if (IdentifyRelic(relic) == id)
                return true;
        }
        return false;
    }

    private static SearchRelicCounter? IdentifyRelic(MegaCrit.Sts2.Core.Models.RelicModel relic)
        => relic switch
        {
            MegaCrit.Sts2.Core.Models.Relics.HappyFlower => SearchRelicCounter.HappyFlower,
            MegaCrit.Sts2.Core.Models.Relics.FakeHappyFlower => SearchRelicCounter.FakeHappyFlower,
            MegaCrit.Sts2.Core.Models.Relics.Pendulum => SearchRelicCounter.Pendulum,
            MegaCrit.Sts2.Core.Models.Relics.PollinousCore => SearchRelicCounter.PollinousCore,
            MegaCrit.Sts2.Core.Models.Relics.PenNib => SearchRelicCounter.PenNib,
            MegaCrit.Sts2.Core.Models.Relics.Nunchaku => SearchRelicCounter.Nunchaku,
            MegaCrit.Sts2.Core.Models.Relics.TuningFork => SearchRelicCounter.TuningFork,
            MegaCrit.Sts2.Core.Models.Relics.JossPaper => SearchRelicCounter.JossPaper,
            MegaCrit.Sts2.Core.Models.Relics.IronClub => SearchRelicCounter.IronClub,
            MegaCrit.Sts2.Core.Models.Relics.GalacticDust => SearchRelicCounter.GalacticDust,
            MegaCrit.Sts2.Core.Models.Relics.MeatOnTheBone => SearchRelicCounter.MeatOnTheBone,
            _ => null,
        };

    private static int RelicPeriod(SearchRelicCounter id) => id switch
    {
        SearchRelicCounter.HappyFlower => 3,
        SearchRelicCounter.FakeHappyFlower => 5,
        SearchRelicCounter.Pendulum => 3,
        SearchRelicCounter.PollinousCore => 4,
        SearchRelicCounter.PenNib => 10,
        SearchRelicCounter.Nunchaku => 10,
        SearchRelicCounter.TuningFork => 10,
        SearchRelicCounter.JossPaper => 5,
        SearchRelicCounter.IronClub => 4,
        SearchRelicCounter.GalacticDust => 10,
        SearchRelicCounter.MeatOnTheBone => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    private static ConfigResult CheckHashes(GoldenBattleRoot golden,
        Battle battle, Scorer scorer, BoundFeatures bound, int actionCount, bool subParts)
    {
        ScoreFeatures features = scorer.Features(battle, actionCount);
        ulong expectedScore = golden.ScoreHash;
        ulong actualScore = GoldenHash.Score(features);
        if (expectedScore != actualScore)
        {
            return new(ConfigStatus.Different, "", "scoreHash",
                $"scoreHash golden={expectedScore:X16} compiled={actualScore:X16}", subParts);
        }
        byte expectedStatus = golden.BoundStatus;
        ulong expectedBound = golden.BoundHash;
        byte actualStatus;
        ulong actualBound = 0;
        if (!bound.Enabled)
        {
            actualStatus = 2;
        }
        else
        {
            actualStatus = 1;
            BoundValues values = bound.Evaluate(battle,
                features.ExplicitPotionStrategicCost, includeSmartPotionLowerBound: true);
            actualBound = GoldenHash.Bound(values);
        }
        if (expectedStatus != actualStatus || expectedBound != actualBound)
        {
            return new(ConfigStatus.Different, "", "boundHash",
                $"boundStatus golden={expectedStatus} compiled={actualStatus} "
                + $"boundHash golden={expectedBound:X16} compiled={actualBound:X16}", subParts);
        }
        return new(ConfigStatus.Same, "", "", "", subParts);
    }

    private static string? CompareSubParts(SearchCase expected, Scorer scorer,
        BoundFeatures bound, out string detail)
    {
        byte[] expectedScore = EncodeScoreProbe(expected);
        byte[] actualScore = EncodeScoreProbe(expected.Rules,
            expected.Options.InitialActionCount, scorer);
        if (!expectedScore.AsSpan().SequenceEqual(actualScore))
        {
            detail = ScoreRulesDiff(expected.Options.Scorer!.RulesForCodec,
                scorer.RulesForCodec);
            return "score";
        }
        byte[] expectedBound = EncodeBoundProbe(expected);
        byte[] actualBound = EncodeBoundProbe(expected.Rules,
            expected.Options.InitialActionCount, bound);
        if (!expectedBound.AsSpan().SequenceEqual(actualBound))
        {
            detail = BoundConfigDiff(expected.Options.Bound!.ConfigForCodec,
                bound.ConfigForCodec);
            return "bound";
        }
        detail = "";
        return null;
    }

    private static byte[] EncodeScoreProbe(SearchCase expected)
        => EncodeScoreProbe(expected.Rules, expected.Options.InitialActionCount,
            expected.Options.Scorer!);

    private static byte[] EncodeScoreProbe(Rules rules, int actionCount, Scorer scorer)
    {
        BeamOptions options = new(1, 1, 1, TimeSpan.Zero, 1)
        {
            Scorer = scorer,
            InitialActionCount = actionCount,
        };
        return SearchCodec.EncodeRequest(new SearchCase(rules, [], options));
    }

    private static byte[] EncodeBoundProbe(SearchCase expected)
        => EncodeBoundProbe(expected.Rules, expected.Options.InitialActionCount,
            expected.Options.Bound!);

    private static byte[] EncodeBoundProbe(Rules rules, int actionCount, BoundFeatures bound)
    {
        BeamOptions options = new(1, 1, 1, TimeSpan.Zero, 1)
        {
            Bound = bound,
            InitialActionCount = actionCount,
        };
        return SearchCodec.EncodeRequest(new SearchCase(rules, [], options));
    }

    private static string? DescribeRequestDifference(SearchCase expected, SearchCase actual,
        out string detail)
    {
        if (!RulesCodec.Encode(expected.Rules).AsSpan().SequenceEqual(RulesCodec.Encode(actual.Rules)))
        {
            IReadOnlyList<string> differences = RulesDiff.Compare(expected.Rules, actual.Rules);
            detail = string.Join('\n', differences.Count == 0 ? ["rules bytes"] : differences.Take(16));
            return differences.Count == 0 ? "rules" : differences[0].Split(' ')[0];
        }
        if (expected.Battles.Length != actual.Battles.Length)
        {
            detail = $"battle count expected={expected.Battles.Length} actual={actual.Battles.Length}";
            return "battleCount";
        }
        for (int index = 0; index < expected.Battles.Length; index++)
        {
            if (!BattleCodec.Encode(expected.Battles[index]).AsSpan()
                .SequenceEqual(BattleCodec.Encode(actual.Battles[index])))
            {
                List<string> differences = BattleDump.Diff(expected.Battles[index],
                    actual.Battles[index], "golden", "compiled");
                detail = string.Join('\n', differences.Take(16));
                return differences.Count == 0 ? "battle" : FieldOf(differences[0]);
            }
        }
        SearchCase expectedScore = expected with
        {
            Battles = [],
            Options = new BeamOptions(1, 1, 1, TimeSpan.Zero, 1)
            {
                Scorer = expected.Options.Scorer,
                InitialActionCount = expected.Options.InitialActionCount,
            },
        };
        SearchCase actualScore = expectedScore with { Options = expectedScore.Options with { Scorer = actual.Options.Scorer } };
        if (!SearchCodec.EncodeRequest(expectedScore).AsSpan()
            .SequenceEqual(SearchCodec.EncodeRequest(actualScore)))
        {
            detail = ScoreRulesDiff(expected.Options.Scorer!.RulesForCodec,
                actual.Options.Scorer!.RulesForCodec);
            return "score";
        }
        SearchCase expectedBound = expected with
        {
            Battles = [],
            Options = new BeamOptions(1, 1, 1, TimeSpan.Zero, 1)
            {
                Bound = expected.Options.Bound,
                InitialActionCount = expected.Options.InitialActionCount,
            },
        };
        SearchCase actualBound = expectedBound with { Options = expectedBound.Options with { Bound = actual.Options.Bound } };
        if (!SearchCodec.EncodeRequest(expectedBound).AsSpan()
            .SequenceEqual(SearchCodec.EncodeRequest(actualBound)))
        {
            detail = BoundConfigDiff(expected.Options.Bound!.ConfigForCodec,
                actual.Options.Bound!.ConfigForCodec);
            return "bound";
        }
        detail = OptionDiff(expected.Options, actual.Options);
        return "options";
    }

    private static string OptionDiff(BeamOptions expected, BeamOptions actual)
    {
        if (expected.BeamWidth != actual.BeamWidth)
            return $"beamWidth {expected.BeamWidth}->{actual.BeamWidth}";
        if (expected.MaxExpandedNodes != actual.MaxExpandedNodes)
            return $"maxExpandedNodes {expected.MaxExpandedNodes}->{actual.MaxExpandedNodes}";
        if (expected.MaxTurns != actual.MaxTurns)
            return $"maxTurns {expected.MaxTurns}->{actual.MaxTurns}";
        if (expected.SoftTimeBudget != actual.SoftTimeBudget)
            return $"softTimeBudget {expected.SoftTimeBudget}->{actual.SoftTimeBudget}";
        if (expected.InitialPlayerMaxHp != actual.InitialPlayerMaxHp)
            return $"initialPlayerMaxHp {expected.InitialPlayerMaxHp}->{actual.InitialPlayerMaxHp}";
        if (expected.InitialActionCount != actual.InitialActionCount)
            return $"initialActionCount {expected.InitialActionCount}->{actual.InitialActionCount}";
        if (expected.Act3BossStrategy != actual.Act3BossStrategy)
            return $"act3BossStrategy {expected.Act3BossStrategy}->{actual.Act3BossStrategy}";
        if (expected.TheftPolicy != actual.TheftPolicy)
            return $"theftPolicy {expected.TheftPolicy}->{actual.TheftPolicy}";
        return $"options bytes differ (initialMaxHp={expected.InitialPlayerMaxHp})";
    }

    private static string ScoreRulesDiff(ScoreRules expected, ScoreRules actual)
    {
        if (expected.Cards.Length != actual.Cards.Length)
            return $"cards.length {expected.Cards.Length}->{actual.Cards.Length}";
        for (int id = 0; id < expected.Cards.Length; id++)
        {
            CardScore?[]? left = expected.Cards[id];
            CardScore?[]? right = actual.Cards[id];
            if (left == null != (right == null))
                return $"cards[{id}] presence";
            if (left == null)
                continue;
            if (left.Length != right!.Length)
                return $"cards[{id}].length {left.Length}->{right.Length}";
            for (int upgrade = 0; upgrade < left.Length; upgrade++)
            {
                CardScore? l = left[upgrade];
                CardScore? r = right[upgrade];
                if (l == null != (r == null))
                    return $"cards[{id}+{upgrade}] presence";
                if (l != null && !CardScoreEquals(l, r!))
                    return $"cards[{id}+{upgrade}]";
            }
        }
        if (expected.Powers.Length != actual.Powers.Length)
            return $"powers.length {expected.Powers.Length}->{actual.Powers.Length}";
        for (int id = 0; id < expected.Powers.Length; id++)
        {
            PowerScore? l = expected.Powers[id];
            PowerScore? r = actual.Powers[id];
            if (l == null != (r == null))
                return $"powers[{id}] presence";
            if (l != null && !PowerScoreEquals(l, r!))
                return $"powers[{id}]";
        }
        if (!expected.PotionStrategicCost.AsSpan().SequenceEqual(actual.PotionStrategicCost))
            return "potionStrategicCost";
        if (!expected.GrowthBudgets.Equals(actual.GrowthBudgets))
            return "growthBudgets";
        if (!expected.RequiredGrowthRewards.Equals(actual.RequiredGrowthRewards))
            return "requiredGrowthRewards";
        if (expected.RelicCounterRules.Length != actual.RelicCounterRules.Length)
            return "relicCounterRules.length";
        for (int index = 0; index < expected.RelicCounterRules.Length; index++)
        {
            if (!expected.RelicCounterRules[index].Equals(actual.RelicCounterRules[index]))
                return $"relicCounterRules[{index}]";
        }
        if (!expected.PostCombatRelicHeal.Equals(actual.PostCombatRelicHeal))
            return "postCombatRelicHeal";
        if (expected.BossHpRelief != actual.BossHpRelief)
            return "bossHpRelief";
        if (expected.ContinuousThreatRanking != actual.ContinuousThreatRanking)
            return "continuousThreatRanking";
        if ((expected.ContextualRanking != null) != (actual.ContextualRanking != null))
            return "contextualRanking";
        if (expected.InitialPlayerMaxHp != actual.InitialPlayerMaxHp)
            return "initialPlayerMaxHp";
        if (expected.IsActEndingBoss != actual.IsActEndingBoss)
            return "isActEndingBoss";
        if (expected.Act3BossStrategy != actual.Act3BossStrategy)
            return "act3BossStrategy";
        if (expected.BaseScoreOnly != actual.BaseScoreOnly)
            return "baseScoreOnly";
        if (!Equals(expected.BeamWeightPerturbation, actual.BeamWeightPerturbation))
            return "beamWeightPerturbation";
        if (expected.IgnoreLongTermRewards != actual.IgnoreLongTermRewards)
            return "ignoreLongTermRewards";
        if (expected.TheftPolicy != actual.TheftPolicy)
            return "theftPolicy";
        if (expected.InitialEnemyCount != actual.InitialEnemyCount)
            return "initialEnemyCount";
        if (expected.InitialPersistentBuffValue != actual.InitialPersistentBuffValue)
            return "initialPersistentBuffValue";
        if (expected.InitialEnemyStrengthSuppression != actual.InitialEnemyStrengthSuppression)
            return "initialEnemyStrengthSuppression";
        if (expected.InitialEnemyWeakTurns != actual.InitialEnemyWeakTurns)
            return "initialEnemyWeakTurns";
        if (expected.InitialRetainedAttackValue != actual.InitialRetainedAttackValue)
            return "initialRetainedAttackValue";
        if (!expected.RootPotions.AsSpan().SequenceEqual(actual.RootPotions))
            return "rootPotions";
        if (expected.InitialPotionUseCount != actual.InitialPotionUseCount)
            return "initialPotionUseCount";
        if (expected.InitialPotionStrategicCost != actual.InitialPotionStrategicCost)
            return "initialPotionStrategicCost";
        if (expected.InitialExplicitPotionStrategicCost != actual.InitialExplicitPotionStrategicCost)
            return "initialExplicitPotionStrategicCost";
        if (expected.RootPotionSlotCount != actual.RootPotionSlotCount)
            return "rootPotionSlotCount";
        return "scoreRules";
    }

    private static bool CardScoreEquals(CardScore left, CardScore right)
        => BitConverter.DoubleToInt64Bits(left.BaseDamage) == BitConverter.DoubleToInt64Bits(right.BaseDamage)
            && BitConverter.DoubleToInt64Bits(left.BaseBlock) == BitConverter.DoubleToInt64Bits(right.BaseBlock)
            && BitConverter.DoubleToInt64Bits(left.BaseCards) == BitConverter.DoubleToInt64Bits(right.BaseCards)
            && left.IsPower == right.IsPower
            && left.PrimaryMoveToHandChoice == right.PrimaryMoveToHandChoice
            && left.DamageVar == right.DamageVar && left.BlockVar == right.BlockVar
            && left.CardsVar == right.CardsVar && left.RepeatVar == right.RepeatVar
            && left.ShivCardsVar == right.ShivCardsVar && left.ShivShivsVar == right.ShivShivsVar
            && left.CalculationBaseVar == right.CalculationBaseVar
            && left.CalculationExtraVar == right.CalculationExtraVar
            && left.DoomThresholdVar == right.DoomThresholdVar
            && left.AttackHits == right.AttackHits && left.ShivSupply == right.ShivSupply
            && left.HasBlockDynamicVar == right.HasBlockDynamicVar
            && left.HasDebuffDynamicVar == right.HasDebuffDynamicVar
            && left.HasTurnEndInHandEffect == right.HasTurnEndInHandEffect
            && left.HasOstyAttackTag == right.HasOstyAttackTag
            && left.HasShivTag == right.HasShivTag && left.LatentValue == right.LatentValue
            && left.LatentTrait == right.LatentTrait && left.DynamicCardType == right.DynamicCardType
            && left.Calculated == right.Calculated;

    private static bool PowerScoreEquals(PowerScore left, PowerScore right)
        => left.Kind == right.Kind && left.Requirements == right.Requirements
            && left.Trait == right.Trait && left.IsTemporary == right.IsTemporary
            && left.Type == right.Type
            && left.DanseMacabreEnergyThreshold == right.DanseMacabreEnergyThreshold;

    private static string BoundConfigDiff(BoundFeatures.Config expected, BoundFeatures.Config actual)
    {
        if (expected.Enabled != actual.Enabled)
            return "enabled";
        if (expected.DisabledReason != actual.DisabledReason)
            return "disabledReason";
        if (expected.FutureHealPotential != actual.FutureHealPotential)
            return "futureHealPotential";
        if (expected.Certificate != actual.Certificate)
            return $"certificate golden={expected.Certificate} compiled={actual.Certificate}";
        if (expected.PostCombatHeal != actual.PostCombatHeal)
            return "postCombatHeal";
        if (expected.BossHpRelief != actual.BossHpRelief)
            return "bossHpRelief";
        if (expected.RequireCertifiedHealingProof != actual.RequireCertifiedHealingProof)
            return "requireCertifiedHealingProof";
        if (expected.PruneEqualHp != actual.PruneEqualHp)
            return "pruneEqualHp";
        if (expected.ForceAllPotionsDisabled != actual.ForceAllPotionsDisabled)
            return "forceAllPotionsDisabled";
        if (expected.MaximumPotionUses != actual.MaximumPotionUses)
            return "maximumPotionUses";
        if (expected.InitialExplicitPotionUses != actual.InitialExplicitPotionUses)
            return "initialExplicitPotionUses";
        if (expected.InitialAutomaticPotionUses != actual.InitialAutomaticPotionUses)
            return "initialAutomaticPotionUses";
        if (!expected.ComponentUnsafeCard.AsSpan().SequenceEqual(actual.ComponentUnsafeCard))
            return "componentUnsafeCard";
        if (!expected.ComponentUnsafePower.AsSpan().SequenceEqual(actual.ComponentUnsafePower))
            return "componentUnsafePower";
        if (!expected.ComponentUnsafeRelic.AsSpan().SequenceEqual(actual.ComponentUnsafeRelic))
            return "componentUnsafeRelic";
        if (!expected.ComponentUnsafeMonster.AsSpan().SequenceEqual(actual.ComponentUnsafeMonster))
            return "componentUnsafeMonster";
        if (!expected.ComponentUnsafePotion.AsSpan().SequenceEqual(actual.ComponentUnsafePotion))
            return "componentUnsafePotion";
        if (!expected.SafeEnchantment.AsSpan().SequenceEqual(actual.SafeEnchantment))
            return "safeEnchantment";
        if (!expected.SafeAffliction.AsSpan().SequenceEqual(actual.SafeAffliction))
            return "safeAffliction";
        if (!expected.KnownBlockingCard.AsSpan().SequenceEqual(actual.KnownBlockingCard))
            return "knownBlockingCard";
        if (!expected.KnownBlockingRelic.AsSpan().SequenceEqual(actual.KnownBlockingRelic))
            return "knownBlockingRelic";
        if (!expected.RegenPower.AsSpan().SequenceEqual(actual.RegenPower))
            return "regenPower";
        if (!expected.ReattachPower.AsSpan().SequenceEqual(actual.ReattachPower))
            return "reattachPower";
        if (!expected.SwipePower.AsSpan().SequenceEqual(actual.SwipePower))
            return "swipePower";
        if (!expected.RegenPotion.AsSpan().SequenceEqual(actual.RegenPotion))
            return "regenPotion";
        if (!expected.DirectHealPotion.AsSpan().SequenceEqual(actual.DirectHealPotion))
            return "directHealPotion";
        if (!expected.FruitJuicePotion.AsSpan().SequenceEqual(actual.FruitJuicePotion))
            return "fruitJuicePotion";
        if (!expected.FairyPotion.AsSpan().SequenceEqual(actual.FairyPotion))
            return "fairyPotion";
        if (!expected.NativePotion.AsSpan().SequenceEqual(actual.NativePotion))
            return "nativePotion";
        if (!expected.RegenPotionAmount.AsSpan().SequenceEqual(actual.RegenPotionAmount))
            return "regenPotionAmount";
        if (!expected.DirectHealPotionPercent.AsSpan().SequenceEqual(actual.DirectHealPotionPercent))
            return "directHealPotionPercent";
        if (!expected.ReattachSafeMonster.AsSpan().SequenceEqual(actual.ReattachSafeMonster))
            return "reattachSafeMonster";
        if (expected.RegenPowerId != actual.RegenPowerId)
            return "regenPowerId";
        if (expected.ReattachPowerId != actual.ReattachPowerId)
            return "reattachPowerId";
        if (!expected.RootPotions.AsSpan().SequenceEqual(actual.RootPotions))
            return "rootPotions";
        if (expected.RootPotionSlotCount != actual.RootPotionSlotCount)
            return "rootPotionSlotCount";
        return "boundConfig";
    }

    private static string FieldOf(string line)
    {
        int space = line.IndexOf(' ');
        if (space < 0)
            return line;
        string rest = line[(space + 1)..];
        int colon = rest.IndexOf(':');
        return colon < 0 ? rest : rest[..colon];
    }

    private static byte[] ReadBrotli(string path)
    {
        using FileStream file = File.OpenRead(path);
        using BrotliStream brotli = new(file, CompressionMode.Decompress);
        using MemoryStream buffer = new();
        brotli.CopyTo(buffer);
        return buffer.ToArray();
    }
}
