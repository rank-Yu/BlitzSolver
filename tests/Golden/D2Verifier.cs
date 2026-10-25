using System.Security.Cryptography;
using BlitzSolver.Engine;
using BlitzSolver.Search;

namespace BlitzSolver.Golden;

internal sealed record D2Summary(int Selected, int Equal, int Mismatch, long Expanded);

internal static class D2Verifier
{
    internal static D2Summary Run(string dataDirectory, int jobs, int shard, int shards)
    {
        string requestDirectory = Path.Combine(dataDirectory, "d2", "requests");
        string expectedDirectory = Path.Combine(dataDirectory, "d2", "expected");
        if (!Directory.Exists(requestDirectory) || !Directory.Exists(expectedDirectory))
            throw new InvalidDataException("D2 requests/expected 目录不完整。");
        string[] selected = Directory.EnumerateFiles(requestDirectory, "*.request.br")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Where((_, index) => index % shards == shard)
            .ToArray();
        if (selected.Length == 0)
            throw new InvalidDataException($"--shard {shard}/{shards} 没有选到 D2 请求。");

        int equal = 0, completed = 0;
        long expanded = 0;
        Parallel.ForEach(selected, new ParallelOptions { MaxDegreeOfParallelism = jobs }, path =>
        {
            string hash = Path.GetFileName(path)[..^".request.br".Length];
            string expectedPath = Path.Combine(expectedDirectory, hash + ".result.br");
            if (!File.Exists(expectedPath))
                throw new InvalidDataException($"D2 请求 {hash} 缺少期望结果。");

            byte[] requestBytes = GoldenFiles.ReadCompressed(path);
            string requestHash = Convert.ToHexString(SHA256.HashData(requestBytes)).ToLowerInvariant();
            if (requestHash != hash)
                throw new InvalidDataException($"D2 请求 {hash} 的 SHA-256 是 {requestHash}。");

            SearchCase search = SearchCodec.DecodeRequest(requestBytes);
            BeamOptions options = search.Options with
            {
                SoftTimeBudget = TimeSpan.MaxValue,
                Pacer = null,
                Progress = null,
                PollTakeover = null,
                Trace = null,
            };
            BeamResult actual = new BeamSolver(search.Rules, options)
                .Solve(search.Battles, CancellationToken.None);
            BeamResult expected = SearchCodec.DecodeResult(
                GoldenFiles.ReadCompressed(expectedPath));
            string? difference = FirstDifference(expected, actual);
            if (difference != null)
            {
                throw new InvalidDataException(
                    $"D2 请求 {hash} 的期望结果不一致：{difference}");
            }
            Interlocked.Increment(ref equal);
            Interlocked.Add(ref expanded, actual.ExpandedNodes);
            int done = Interlocked.Increment(ref completed);
            if (done % 100 == 0)
                Console.WriteLine($"D2 progress={done}/{selected.Length}");
        });
        return new D2Summary(selected.Length, equal, selected.Length - equal, expanded);
    }

    private static string? FirstDifference(in BeamResult expected, in BeamResult actual)
    {
        string? difference = Difference("BestRoute", expected.BestRoute, actual.BestRoute);
        if (difference != null)
            return difference;
        if (expected.TopRoutes.Count != actual.TopRoutes.Count)
            return $"TopRoutes count expected={expected.TopRoutes.Count} actual={actual.TopRoutes.Count}";
        for (int index = 0; index < expected.TopRoutes.Count; index++)
        {
            difference = Difference($"TopRoutes[{index}]", expected.TopRoutes[index], actual.TopRoutes[index]);
            if (difference != null)
                return difference;
        }
        if (expected.BestIsVictory != actual.BestIsVictory)
            return $"BestIsVictory expected={expected.BestIsVictory} actual={actual.BestIsVictory}";
        if (BitConverter.DoubleToInt64Bits(expected.BestScore) != BitConverter.DoubleToInt64Bits(actual.BestScore))
            return $"BestScore expected={expected.BestScore:R} actual={actual.BestScore:R}";
        if (expected.ExpandedNodes != actual.ExpandedNodes)
            return $"ExpandedNodes expected={expected.ExpandedNodes} actual={actual.ExpandedNodes}";
        if (expected.Transitions != actual.Transitions)
            return $"Transitions expected={expected.Transitions} actual={actual.Transitions}";
        if (expected.Duplicates != actual.Duplicates)
            return $"Duplicates expected={expected.Duplicates} actual={actual.Duplicates}";
        if (expected.TurnsSearched != actual.TurnsSearched)
            return $"TurnsSearched expected={expected.TurnsSearched} actual={actual.TurnsSearched}";
        if (expected.PrunedIncumbents != actual.PrunedIncumbents)
            return $"PrunedIncumbents expected={expected.PrunedIncumbents} actual={actual.PrunedIncumbents}";
        if (expected.CapacityPruned != actual.CapacityPruned)
            return $"CapacityPruned expected={expected.CapacityPruned} actual={actual.CapacityPruned}";
        difference = Difference(expected.CapacityDroppedChildren, actual.CapacityDroppedChildren);
        if (difference != null)
            return difference;
        if (expected.EndedCompactions != actual.EndedCompactions)
            return $"EndedCompactions expected={expected.EndedCompactions} actual={actual.EndedCompactions}";
        if (expected.EndedCompressedStates != actual.EndedCompressedStates)
            return $"EndedCompressedStates expected={expected.EndedCompressedStates} actual={actual.EndedCompressedStates}";
        if (expected.SmartPotionPruned != actual.SmartPotionPruned)
            return $"SmartPotionPruned expected={expected.SmartPotionPruned} actual={actual.SmartPotionPruned}";
        if (expected.BestPayoffCount != actual.BestPayoffCount)
            return $"BestPayoffCount expected={expected.BestPayoffCount} actual={actual.BestPayoffCount}";
        if (expected.ScoutLayers.Count != actual.ScoutLayers.Count)
            return $"ScoutLayers count expected={expected.ScoutLayers.Count} actual={actual.ScoutLayers.Count}";
        for (int layer = 0; layer < expected.ScoutLayers.Count; layer++)
        {
            ScoutLayer expectedLayer = expected.ScoutLayers[layer];
            ScoutLayer actualLayer = actual.ScoutLayers[layer];
            if (expectedLayer.Turns != actualLayer.Turns)
                return $"ScoutLayers[{layer}].Turns expected={expectedLayer.Turns} actual={actualLayer.Turns}";
            if (expectedLayer.Candidates.Count != actualLayer.Candidates.Count)
                return $"ScoutLayers[{layer}].Candidates count expected={expectedLayer.Candidates.Count} actual={actualLayer.Candidates.Count}";
            for (int candidate = 0; candidate < expectedLayer.Candidates.Count; candidate++)
            {
                ScoutCandidate expectedCandidate = expectedLayer.Candidates[candidate];
                ScoutCandidate actualCandidate = actualLayer.Candidates[candidate];
                difference = Difference($"ScoutLayers[{layer}].Candidates[{candidate}].Actions",
                    expectedCandidate.Actions, actualCandidate.Actions);
                if (difference != null)
                    return difference;
                if (BitConverter.DoubleToInt64Bits(expectedCandidate.Score)
                    != BitConverter.DoubleToInt64Bits(actualCandidate.Score))
                    return $"ScoutLayers[{layer}].Candidates[{candidate}].Score expected={expectedCandidate.Score:R} actual={actualCandidate.Score:R}";
                if (expectedCandidate.RootIndex != actualCandidate.RootIndex)
                    return $"ScoutLayers[{layer}].Candidates[{candidate}].RootIndex expected={expectedCandidate.RootIndex} actual={actualCandidate.RootIndex}";
            }
        }
        if (expected.BestRootIndex != actual.BestRootIndex)
            return $"BestRootIndex expected={expected.BestRootIndex} actual={actual.BestRootIndex}";
        if (expected.PotionPolicySatisfied != actual.PotionPolicySatisfied)
            return $"PotionPolicySatisfied expected={expected.PotionPolicySatisfied} actual={actual.PotionPolicySatisfied}";
        if (expected.Takeover != actual.Takeover)
            return $"Takeover expected={expected.Takeover} actual={actual.Takeover}";
        return null;
    }

    private static string? Difference(string label, PlayerAction[] expected, PlayerAction[] actual)
    {
        if (expected.Length != actual.Length)
            return $"{label} count expected={expected.Length} actual={actual.Length}";
        for (int index = 0; index < expected.Length; index++)
        {
            if (!GoldenFiles.ActionEquals(expected[index], actual[index]))
                return $"{label}[{index}] expected={GoldenFiles.Describe(expected[index])} "
                    + $"actual={GoldenFiles.Describe(actual[index])}";
        }
        return null;
    }

    private static string? Difference(IReadOnlyDictionary<string, int> expected,
        IReadOnlyDictionary<string, int> actual)
    {
        if (expected.Count != actual.Count)
            return $"CapacityDroppedChildren count expected={expected.Count} actual={actual.Count}";
        foreach (string key in expected.Keys.OrderBy(static key => key, StringComparer.Ordinal))
        {
            if (!actual.TryGetValue(key, out int value))
                return $"CapacityDroppedChildren missing key {key}";
            if (value != expected[key])
                return $"CapacityDroppedChildren[{key}] expected={expected[key]} actual={value}";
        }
        return null;
    }
}
