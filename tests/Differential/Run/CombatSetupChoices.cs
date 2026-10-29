// Opening-combat choice enumeration. Core treats the build as a pure function of the pre-entry
// RunState and the selection sequence; this file walks every sequence the comparison can afford.
using Sts2Solver.Simulation.Combat;
using Sts2Solver.Simulation.Run;

namespace Differential;

internal sealed class CombatSetupSequence
{
    public required ulong[] Masks { get; init; }

    public required ChoiceRequest[] Requests { get; init; }

    public required CombatSetupResult Result { get; init; }
}

internal static class CombatSetupChoices
{
    private const int MaxSequences = 65536;
    private const int MaxEnumeratedOptions = 20;
    private const int SampledSubsets = 16;

    public static List<CombatSetupSequence> Enumerate(in RunState before, ContentTable table,
        RunCoord coord, string label)
    {
        List<CombatSetupSequence> results = [];
        List<ulong> masks = [];
        List<ChoiceRequest> requests = [];
        Random random = new(unchecked((int)Seed(label)));
        Recurse(in before, table, coord, masks, requests, results, random);
        return results;
    }

    /// <summary>The run-pilot answer: first option, or the empty subset for GamblingChip.</summary>
    public static CombatSetupResult ResolveFirst(in RunState before, ContentTable table,
        RunCoord coord, bool emptyGambling, out ulong[] masks, out ChoiceRequest[] requests)
    {
        List<ulong> choices = [];
        List<ChoiceRequest> pendingRequests = [];
        while (true)
        {
            CombatSetupAttempt attempt = RunCombatSetup.TryEnterMapCoord(in before, table, coord,
                choices.ToArray());
            if (attempt.Pending is null)
            {
                masks = [.. choices];
                requests = [.. pendingRequests];
                return attempt.Result!;
            }
            if (choices.Count >= RunLimits.CombatSetupChoices)
                throw new CapacityException("Run.CombatSetupChoices", RunLimits.CombatSetupChoices);
            pendingRequests.Add(attempt.Pending.Request);
            choices.Add(FirstMask(attempt.Pending.Request, emptyGambling));
        }
    }

    public static ulong FirstMask(in ChoiceRequest request, bool emptyGambling = true)
    {
        if (emptyGambling
            && request.Effect == ChoiceEffect.DiscardAndDraw
            && !request.OptionsAreGenerated
            && request.Source == Pile.Hand
            && request.MinCount == 0
            && request.MaxCount > 1)
        {
            return 0;
        }
        int take = Math.Min(Math.Max(request.MinCount, request.MaxCount > 0 ? 1 : 0),
            request.OptionCount);
        return take >= 64 ? ulong.MaxValue : (1UL << take) - 1;
    }

    private static void Recurse(in RunState before, ContentTable table, RunCoord coord,
        List<ulong> masks, List<ChoiceRequest> requests, List<CombatSetupSequence> results,
        Random random)
    {
        CombatSetupAttempt attempt = RunCombatSetup.TryEnterMapCoord(in before, table, coord,
            masks.ToArray());
        if (attempt.Pending is { } pending)
        {
            requests.Add(pending.Request);
            foreach (ulong mask in Masks(pending.Request, random))
            {
                masks.Add(mask);
                Recurse(in before, table, coord, masks, requests, results, random);
                masks.RemoveAt(masks.Count - 1);
            }
            requests.RemoveAt(requests.Count - 1);
            return;
        }
        if (results.Count >= MaxSequences)
            throw new CapacityException("CombatSetupSequences", MaxSequences);
        results.Add(new CombatSetupSequence
        {
            Masks = [.. masks],
            Requests = [.. requests],
            Result = attempt.Result!,
        });
    }

    private static IEnumerable<ulong> Masks(ChoiceRequest request, Random random)
    {
        int count = request.OptionCount;
        int min = request.MinCount;
        int max = request.MaxCount;
        if (count == 0)
        {
            yield return 0;
            yield break;
        }
        if (count > MaxEnumeratedOptions)
        {
            throw new CapacityException("Choice.Options", MaxEnumeratedOptions);
        }
        bool gambling = request.Effect == ChoiceEffect.DiscardAndDraw
            && !request.OptionsAreGenerated
            && request.Source == Pile.Hand
            && min == 0;
        if (gambling && count > 7)
        {
            HashSet<ulong> seen = [];
            foreach (ulong mask in Sampled(count, min, max, random))
            {
                if (seen.Add(mask))
                    yield return mask;
            }
            yield break;
        }
        ulong limit = 1UL << count;
        for (ulong mask = 0; mask < limit; mask++)
        {
            int selected = System.Numerics.BitOperations.PopCount(mask);
            if (selected >= min && selected <= max)
                yield return mask;
        }
    }

    private static IEnumerable<ulong> Sampled(int count, int min, int max, Random random)
    {
        yield return 0;
        for (int index = 0; index < count; index++)
            yield return 1UL << index;
        yield return count >= 64 ? ulong.MaxValue : (1UL << count) - 1;
        int upper = Math.Min(max, count);
        for (int sample = 0; sample < SampledSubsets; sample++)
        {
            int size = random.Next(min, upper + 1);
            ulong mask = 0;
            while (System.Numerics.BitOperations.PopCount(mask) < size)
                mask |= 1UL << random.Next(count);
            yield return mask;
        }
    }

    private static ulong Seed(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
