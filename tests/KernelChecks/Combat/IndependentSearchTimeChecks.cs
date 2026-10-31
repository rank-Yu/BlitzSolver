using Sts2Solver.Ai.Combat;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.KernelChecks;

internal static class IndependentSearchTimeChecks
{
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidDataException("member-time: " + message); }

    internal static void Check(Rules rules, BeamOptions options, BeamOptions forced)
    {
        Registry.TryPotion("FIRE_POTION", out ushort fire);
        Battle root = default;
        root.PotionSlotCount = 3;
        root.Potions[0] = root.Potions[1] = root.Potions[2] = (ushort)(fire + 1);
        options = options with { PowerCommitments = new([default], 1) };
        SearchCase search = new(rules, [root], options);
        SearchPlan plan = new(3, 2, 4, 1, 1, 100_000_000)
            { MillisecondsPerWidth = 1000, SetupStrategies = true };
        long now = 0;
        List<BeamOptions> seen = [];
        ValueTask<SearchMemberMeasurement> Exhaust(SearchCase item, SearchStorage _, CancellationToken __)
        {
            Assert(!item.Options.DeadlineExpired!(), "each member starts with fresh time");
            seen.Add(item.Options);
            now += 10_000;
            Assert(item.Options.DeadlineExpired!(), "queue and search consume the same member deadline");
            return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 10_000));
        }
        CombatSearchResult result = CombatSearchCoordinator.RunAsync(new(search, plan), new(), Exhaust,
            CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(result.Members.Length == 16 && result.Members.All(m => m.Ran)
            && result.ElapsedMs == 16 * 10_000 && result.Members.All(m => m.Member.TimeLimitMilliseconds == m.Member.Width * 1000),
            "exhausting every member never exhausts a global budget");
        Assert(seen.All(o => o.SoftTimeBudget == TimeSpan.FromMilliseconds(SearchBudget.SearchShare(o.BeamWidth * 1000, 1))),
            "all potion phases and supplements receive budgets based only on their own width");
        ValueTask<SearchMemberMeasurement> Quick(SearchCase item, SearchStorage _, CancellationToken __)
        {
            seen.Add(item.Options); now++;
            return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 1));
        }
        seen.Clear(); now = 0;
        result = CombatSearchCoordinator.RunAsync(new(search, plan), new(), Quick,
            CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(seen.All(o => o.SoftTimeBudget == TimeSpan.FromMilliseconds(SearchBudget.SearchShare(o.BeamWidth * 1000, 1))),
            "unused time does not enlarge a later member");

        SearchPlan custom = plan with { SetupStrategies = false, CustomMembers = [
            new(3, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.None)
                { TimeLimitMilliseconds = 700 },
            new(3, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.None),
            new(3, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.Allowed)] };
        seen.Clear(); now = 0;
        result = CombatSearchCoordinator.RunAsync(new(search, custom), new(), Quick,
            CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(seen.Select(o => (long)o.SoftTimeBudget.TotalMilliseconds).SequenceEqual(new long[] { 600, 2700, 2700 }),
            "individual overrides do not change other members or depend on portfolio size");
        SearchPlan doubled = custom.ForEncounter(true);
        Assert(doubled == custom, "encounters do not multiply independent or overridden budgets");

        byte[] bytes = CombatSearchCodec.Encode(new(search, custom));
        CombatSearchRequest decoded = CombatSearchCodec.Decode(bytes);
        Assert(BitConverter.ToUInt16(bytes, 4) == 12 && decoded.Plan.MillisecondsPerWidth == 1000
            && decoded.Plan.CustomMembers[0].TimeLimitMilliseconds == 700
            && CombatSearchCodec.Encode(decoded).SequenceEqual(bytes), "v12 freezes independent and overridden times");
        byte[] invalid = (byte[])bytes.Clone(); Array.Clear(invalid, invalid.Length - 4, 4);
        bool rejected = false;
        try { _ = CombatSearchCodec.Decode(invalid); }
        catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "v12 cannot silently disable its independent budget");
        SearchPlan historical = plan with { MillisecondsPerWidth = 0, TotalMilliseconds = 1000 };
        byte[] legacy = CombatSearchCodec.Encode(new(search, historical));
        Assert(BitConverter.ToUInt16(legacy, 4) == 11
            && CombatSearchCodec.Decode(legacy).Plan.MillisecondsPerWidth == 0,
            "v11 keeps its historical shared deadline");

        seen.Clear(); now = 0;
        result = CombatSearchCoordinator.RunAsync(new(search with { Options = forced with {
            PowerCommitments = options.PowerCommitments } }, plan), new(), Quick,
            CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(seen.Count == 6 && seen.All(o => o.EnforceForcedPotionDirectives
            && o.SoftTimeBudget == TimeSpan.FromMilliseconds(SearchBudget.SearchShare(o.BeamWidth * 1000, 1))),
            "forced potion members preserve policy and independent time");
        seen.Clear(); now = 0;
        result = CombatSearchCoordinator.RunAsync(new(search with { Battles = [default] }, plan), new(), Quick,
            CancellationToken.None, () => now).AsTask().GetAwaiter().GetResult();
        Assert(seen.Count == 7 && seen.All(o => o.SoftTimeBudget == TimeSpan.FromMilliseconds(SearchBudget.SearchShare(o.BeamWidth * 1000, 1)))
            && result.Members.Where(m => m.Member.Potions != SearchPotionLimit.None).All(m => !m.Ran),
            "unavailable potion stages stay skipped without enlarging main search time");
        using CancellationTokenSource cancel = new();
        ValueTask<SearchMemberMeasurement> Cancel(SearchCase item, SearchStorage _, CancellationToken __)
        {
            cancel.Cancel();
            return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 0));
        }
        bool cancelled = false;
        try { _ = CombatSearchCoordinator.RunAsync(new(search, plan), new(), Cancel, cancel.Token)
            .AsTask().GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled, "cancellation stops the portfolio between members");
    }
}
