using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using CombatSolver;
using CombatSolver.Flash.Kernel;
using CombatSolver.Flash.Search;
using CombatSolver.Flash.Search.Scoring;

namespace FlashGolden;

internal sealed record D1Summary(
    int Files,
    int Roots,
    int K1,
    int Walks,
    int WalkStates,
    int Transitions,
    long FeatureStates,
    long FeatureTraits,
    long RulesObjects);

internal sealed record D1FileResult(
    int Roots,
    int K1,
    int Walks,
    int WalkStates,
    int Transitions,
    long FeatureStates,
    long FeatureTraits);

internal static class D1Verifier
{
    private static readonly ConcurrentDictionary<string, FlashRules> RulesCache = new(StringComparer.Ordinal);

    internal static D1Summary Run(string dataDirectory, int jobs, int shard, int shards)
    {
        string d1Directory = Path.Combine(dataDirectory, "d1");
        if (!Directory.Exists(d1Directory))
            throw new InvalidDataException($"D1 目录不存在：{d1Directory}");
        string[] selected = Directory.EnumerateDirectories(d1Directory)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Where((_, index) => index % shards == shard)
            .ToArray();
        if (selected.Length == 0)
            throw new InvalidDataException($"--shard {shard}/{shards} 没有选到 D1 文件。");

        ConcurrentDictionary<string, D1FileResult> results = new(StringComparer.Ordinal);
        Parallel.ForEach(selected, new ParallelOptions { MaxDegreeOfParallelism = jobs }, directory =>
        {
            string name = Path.GetFileName(directory);
            results[name] = VerifyFile(dataDirectory, directory);
        });

        int roots = 0, k1 = 0, walks = 0, states = 0, transitions = 0;
        long featureStates = 0, featureTraits = 0;
        foreach (string directory in selected)
        {
            string name = Path.GetFileName(directory);
            D1FileResult result = results[name];
            roots += result.Roots;
            k1 += result.K1;
            walks += result.Walks;
            states += result.WalkStates;
            transitions += result.Transitions;
            featureStates += result.FeatureStates;
            featureTraits += result.FeatureTraits;
            Console.WriteLine($"ROOT {name} roots={result.Roots} k1={result.K1} walks={result.Walks} "
                + $"states={result.WalkStates} transitions={result.Transitions} "
                + $"features={result.FeatureStates} traits={result.FeatureTraits} OK");
        }
        return new D1Summary(selected.Length, roots, k1, walks, states, transitions,
            featureStates, featureTraits, RulesCache.Count);
    }

    private static D1FileResult VerifyFile(string dataDirectory, string directory)
    {
        string rootName = Path.GetFileName(directory);
        string goldenPath = Path.Combine(directory, "golden.br");
        string metaPath = Path.Combine(directory, "meta.json");
        if (!File.Exists(goldenPath) || !File.Exists(metaPath))
            throw new InvalidDataException($"根 {rootName} 缺少 golden.br 或 meta.json。");

        using JsonDocument meta = JsonDocument.Parse(File.ReadAllText(metaPath));
        JsonElement metaRoot = meta.RootElement;
        if (metaRoot.GetProperty("formatVersion").GetInt32() != FlashCodec.FormatVersion)
            throw new InvalidDataException($"根 {rootName} 的格式版本不符。");
        if (metaRoot.GetProperty("battleSize").GetInt32() != Unsafe.SizeOf<FlashBattle>())
            throw new InvalidDataException($"根 {rootName} 的 FlashBattle 大小不符。");

        FlashGoldenRootRecord record = FlashGoldenData.Decode(GoldenFiles.ReadCompressed(goldenPath));
        FlashRules rules = LoadRules(dataDirectory, record.RulesSha256);
        FlashSearchCase config = LoadConfig(directory, rootName, record.RulesSha256);
        FlashScorer scorer = config.Options.Scorer
            ?? throw new InvalidDataException($"根 {rootName} 的特征配置没有 Scorer。");
        FlashBoundFeatures? bound = config.Options.Bound;
        int rootActionCount = config.Options.InitialActionCount;

        if (record.Roots.Count == 0)
            throw new InvalidDataException($"根 {rootName} 没有 Flash 根。");
        JsonElement counts = metaRoot.GetProperty("counts");
        if (counts.GetProperty("roots").GetInt32() != record.Roots.Count
            || counts.GetProperty("k1").GetInt32() != record.K1.Count
            || counts.GetProperty("walks").GetInt32() != record.Walks.Count)
            throw new InvalidDataException($"根 {rootName} 的 meta 计数与 golden 文件不符。");

        long featureStates = 0, featureTraits = 0;
        int walkStates = 0, transitions = 0;

        for (int index = 0; index < record.Roots.Count; index++)
        {
            FlashGoldenBattleRoot root = record.Roots[index];
            CheckFeatures(rootName, $"root[{index}]", root.Battle, rootActionCount,
                scorer, bound, root.ScoreHash, root.BoundStatus, root.BoundHash);
            featureStates++;
        }

        foreach (FlashGoldenK1Action entry in record.K1)
        {
            if ((uint)entry.RootIndex >= (uint)record.Roots.Count)
                throw new InvalidDataException($"根 {rootName} 的 K1 根下标 {entry.RootIndex} 越界。");
            VerifyK1(rootName, record.Roots[entry.RootIndex].Battle, entry, rules,
                scorer, bound, rootActionCount, ref featureStates, ref featureTraits);
        }

        foreach (FlashGoldenWalk walk in record.Walks)
        {
            if ((uint)walk.RootIndex >= (uint)record.Roots.Count)
                throw new InvalidDataException($"根 {rootName} 的游走根下标 {walk.RootIndex} 越界。");
            VerifyWalk(rootName, walk, record.Roots[walk.RootIndex].Battle, rules,
                scorer, bound, rootActionCount, ref featureStates, ref featureTraits,
                ref walkStates, ref transitions);
        }

        if (counts.GetProperty("walkStates").GetInt32() != walkStates
            || counts.GetProperty("transitions").GetInt32() != transitions)
            throw new InvalidDataException($"根 {rootName} 的游走计数与 meta 不符。");

        return new D1FileResult(record.Roots.Count, record.K1.Count, record.Walks.Count,
            walkStates, transitions, featureStates, featureTraits);
    }

    private static void VerifyK1(string rootName, in FlashBattle root, FlashGoldenK1Action entry,
        FlashRules rules, FlashScorer scorer, FlashBoundFeatures? bound, int rootActionCount,
        ref long featureStates, ref long featureTraits)
    {
        FlashAction action = entry.BaseAction;
        for (int index = 0; index < entry.Requests.Length; index++)
        {
            FlashBattle partial = root;
            FlashStepResult result = FlashKernel.Step(ref partial, action, rules,
                out FlashChoiceRequest request, out _);
            if (result != FlashStepResult.NeedsChoice)
                throw Mismatch(rootName, $"K1 root={entry.RootIndex} choice[{index}] expected needs-choice but got {result}");
            CompareRequest(rootName, $"K1 root={entry.RootIndex} choice[{index}]", entry.Requests[index], request);
            if (index >= entry.Selections.Length)
                throw Mismatch(rootName, $"K1 root={entry.RootIndex} missing selection {index}");
            action = action.WithChoice(entry.Selections[index]);
        }
        if (entry.Requests.Length != entry.Selections.Length)
            throw Mismatch(rootName, $"K1 root={entry.RootIndex} request/selection counts differ");

        FlashBattle child = root;
        FlashStepResult final = FlashKernel.Step(ref child, action, rules, out _, out bool historyRisk);
        if (final != FlashStepResult.Completed)
            throw Mismatch(rootName, $"K1 root={entry.RootIndex} final step returned {final}");
        ulong expectedChild = FlashCodec.BattleHash(child);
        if (entry.ChildHash != expectedChild)
            throw Mismatch(rootName, $"K1 root={entry.RootIndex} child hash expected={entry.ChildHash:X16} actual={expectedChild:X16}");
        if (entry.Ended != !FlashKernel.IsInProgress(child))
            throw Mismatch(rootName, $"K1 root={entry.RootIndex} ended expected={entry.Ended} actual={!FlashKernel.IsInProgress(child)}");

        FlashScoreFeatures after = CheckFeatures(rootName, $"K1 root={entry.RootIndex}", child, 1,
            scorer, bound, entry.ScoreHash, entry.BoundStatus, entry.BoundHash);
        featureStates++;
        FlashScoreFeatures before = scorer.Features(root, rootActionCount);
        CheckTrait(rootName, $"K1 root={entry.RootIndex}", root, before, after, child, action,
            rules, historyRisk, entry.TraitPresent, entry.TraitHash);
        if (entry.TraitPresent)
            featureTraits++;
    }

    private static void VerifyWalk(string rootName, FlashGoldenWalk walk, in FlashBattle root,
        FlashRules rules, FlashScorer scorer, FlashBoundFeatures? bound, int rootActionCount,
        ref long featureStates, ref long featureTraits, ref int walkStates, ref int transitions)
    {
        FlashBattle battle = root;
        Span<FlashAction> actions = stackalloc FlashAction[FlashKernel.MaxLegalActions];
        for (int stateIndex = 0; stateIndex < walk.States.Count; stateIndex++)
        {
            FlashGoldenState state = walk.States[stateIndex];
            ulong hash = FlashCodec.BattleHash(battle);
            if (state.StateHash != hash)
                throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} hash expected={state.StateHash:X16} actual={hash:X16}");
            int count = FlashKernel.LegalActions(battle, rules, actions);
            if (count != state.LegalActions.Length)
                throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} legal count expected={state.LegalActions.Length} actual={count}");
            for (int index = 0; index < count; index++)
            {
                FlashAction actual = actions[index];
                FlashAction expected = state.LegalActions[index];
                if (!GoldenFiles.ActionEquals(actual, expected))
                    throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} legal[{index}] mismatch");
                if (index >= state.ImmediateStatuses.Length)
                    throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} missing status {index}");
                FlashBattle copy = battle;
                byte status;
                FlashChoiceRequest request = default;
                try
                {
                    FlashStepResult result = FlashKernel.Step(ref copy, actual, rules, out request, out _);
                    status = result == FlashStepResult.NeedsChoice ? (byte)1 : (byte)0;
                }
                catch (FlashUnsupportedException)
                {
                    status = 2;
                }
                catch (FlashCapacityException)
                {
                    status = 2;
                }
                if (state.ImmediateStatuses[index] != status)
                    throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} legal[{index}] status expected={state.ImmediateStatuses[index]} actual={status}");
                if (status == 1)
                    CompareRequest(rootName, $"walk seed={walk.Seed} state={stateIndex} legal[{index}]",
                        state.ImmediateRequests[index], request);
            }
            walkStates++;
            if (!state.HasStep)
            {
                if (stateIndex != walk.States.Count - 1)
                    throw Mismatch(rootName, $"walk seed={walk.Seed} has states after a no-step state");
                break;
            }
            transitions++;

            FlashAction action = state.StepBase;
            for (int index = 0; index < state.StepRequests.Length; index++)
            {
                FlashBattle partial = battle;
                FlashStepResult result = FlashKernel.Step(ref partial, action, rules,
                    out FlashChoiceRequest request, out _);
                if (result != FlashStepResult.NeedsChoice)
                    throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} step choice[{index}] expected needs-choice but got {result}");
                CompareRequest(rootName, $"walk seed={walk.Seed} state={stateIndex} step choice[{index}]",
                    state.StepRequests[index], request);
                if (index >= state.StepSelections.Length)
                    throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} missing step selection {index}");
                action = action.WithChoice(state.StepSelections[index]);
            }
            if (state.StepRequests.Length != state.StepSelections.Length)
                throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} step request/selection counts differ");

            FlashBattle child = battle;
            FlashStepResult final = FlashKernel.Step(ref child, action, rules, out _, out bool historyRisk);
            if (final != FlashStepResult.Completed)
                throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} step returned {final}");
            ulong childHash = FlashCodec.BattleHash(child);
            if (state.ChildHash != childHash)
                throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} child hash expected={state.ChildHash:X16} actual={childHash:X16}");
            if (state.Ended != !FlashKernel.IsInProgress(child))
                throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} ended expected={state.Ended} actual={!FlashKernel.IsInProgress(child)}");

            FlashScoreFeatures after = CheckFeatures(rootName, $"walk seed={walk.Seed} state={stateIndex}",
                child, 1, scorer, bound, state.ScoreHash, state.BoundStatus, state.BoundHash);
            featureStates++;
            FlashScoreFeatures before = scorer.Features(battle, 0);
            CheckTrait(rootName, $"walk seed={walk.Seed} state={stateIndex}", battle, before, after, child,
                action, rules, historyRisk, state.TraitPresent, state.TraitHash);
            if (state.TraitPresent)
                featureTraits++;
            battle = child;
        }
        if (walk.States.Count > 0 && walk.States[^1].HasStep && walk.States[^1].Ended)
            throw Mismatch(rootName, $"walk seed={walk.Seed} ended without the recorded terminal state");
    }

    private static FlashScoreFeatures CheckFeatures(string rootName, string where, in FlashBattle battle,
        int actionCount, FlashScorer scorer, FlashBoundFeatures? bound, ulong scoreHash,
        byte boundStatus, ulong boundHash)
    {
        FlashScoreFeatures features = scorer.Features(battle, actionCount);
        ulong actualScore = FlashGoldenHash.Score(features);
        if (actualScore != scoreHash)
            throw Mismatch(rootName, $"{where} scoreHash expected={scoreHash:X16} actual={actualScore:X16}");

        byte actualStatus;
        ulong actualBound = 0;
        if (bound is null)
        {
            actualStatus = 0;
        }
        else if (!bound.Enabled)
        {
            actualStatus = 2;
        }
        else
        {
            actualStatus = 1;
            FlashBoundValues values = bound.Evaluate(battle, features.ExplicitPotionStrategicCost,
                includeSmartPotionLowerBound: true);
            actualBound = FlashGoldenHash.Bound(values);
        }
        if (actualStatus != boundStatus)
            throw Mismatch(rootName, $"{where} boundStatus expected={boundStatus} actual={actualStatus}");
        if (actualBound != boundHash)
            throw Mismatch(rootName, $"{where} boundHash expected={boundHash:X16} actual={actualBound:X16}");
        return features;
    }

    private static void CheckTrait(string rootName, string where, in FlashBattle beforeBattle,
        in FlashScoreFeatures before, in FlashScoreFeatures after, in FlashBattle child,
        in FlashAction action, FlashRules rules, bool historyRisk, bool present, ulong expected)
    {
        if (!present)
        {
            if (expected != 0)
                throw Mismatch(rootName, $"{where} trait hash present=false expected={expected:X16}");
            return;
        }
        bool pure = FlashBeamSolver.IsPureTransitionForTesting(beforeBattle, child, action, rules, historyRisk);
        bool declined = FlashBeamSolver.DeclinedExtraTurn(beforeBattle);
        SearchRouteTraits traits = FlashBeamSolver.ClassifyRouteTraitsForTesting(before, after, action, declined, !pure);
        FlashBeamSolver.PocketwatchState(child, rules, out int threshold, out int played);
        ulong actual = FlashGoldenHash.Trait(traits, pure, declined, threshold, played);
        if (actual != expected)
            throw Mismatch(rootName, $"{where} traitHash expected={expected:X16} actual={actual:X16}");
    }

    private static FlashRules LoadRules(string dataDirectory, string hash)
        => RulesCache.GetOrAdd(hash, key =>
        {
            string path = Path.Combine(dataDirectory, "objects", "rules", key + ".br");
            if (!File.Exists(path))
                throw new InvalidDataException($"缺少规则对象 {key}。");
            byte[] bytes = GoldenFiles.ReadCompressed(path);
            string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (actual != key)
                throw new InvalidDataException($"规则对象 {key} 的 SHA-256 是 {actual}。");
            return FlashRulesCodec.Decode(bytes);
        });

    private static FlashSearchCase LoadConfig(string directory, string rootName, string rulesHash)
    {
        string path = Path.Combine(directory, "config.br");
        if (!File.Exists(path))
            throw new InvalidDataException($"缺少 D1 特征配置：{path}");
        FlashSearchCase config = FlashSearchCodec.DecodeRequest(GoldenFiles.ReadCompressed(path));
        string actual = Convert.ToHexString(SHA256.HashData(FlashRulesCodec.Encode(config.Rules))).ToLowerInvariant();
        if (actual != rulesHash)
            throw new InvalidDataException($"根 {rootName} 的特征配置规则哈希是 {actual}，期望 {rulesHash}。");
        return config;
    }

    private static void CompareRequest(string rootName, string where,
        in FlashChoiceRequest expected, in FlashChoiceRequest actual)
    {
        if (expected.Kind != actual.Kind || expected.Effect != actual.Effect
            || expected.Timing != actual.Timing || expected.Source != actual.Source
            || expected.SourceIsNone != actual.SourceIsNone
            || expected.OptionsAreGenerated != actual.OptionsAreGenerated
            || expected.OptionCount != actual.OptionCount || expected.MinCount != actual.MinCount
            || expected.MaxCount != actual.MaxCount)
            throw Mismatch(rootName, $"{where} request header expected="
                + $"{expected.Kind}/{expected.Effect}/{expected.Timing}/{expected.Source}/"
                + $"{expected.OptionCount}/{expected.MinCount}/{expected.MaxCount} actual="
                + $"{actual.Kind}/{actual.Effect}/{actual.Timing}/{actual.Source}/"
                + $"{actual.OptionCount}/{actual.MinCount}/{actual.MaxCount}");
        for (int index = 0; index < expected.OptionCount; index++)
        {
            if (expected.Options[index] != actual.Options[index])
                throw Mismatch(rootName, $"{where} option[{index}] expected={expected.Options[index]} actual={actual.Options[index]}");
        }
        if (expected.OptionsAreGenerated)
        {
            for (int index = 0; index < expected.OptionCount; index++)
            {
                if (!FlashFingerprint.Equal(expected.GeneratedOptions[index], actual.GeneratedOptions[index]))
                    throw Mismatch(rootName, $"{where} generated option[{index}] mismatch");
            }
        }
    }

    private static InvalidDataException Mismatch(string rootName, string detail)
        => new($"D1 {rootName}：{detail}");
}
