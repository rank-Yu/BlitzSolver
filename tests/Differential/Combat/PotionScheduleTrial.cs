using System.Collections.Immutable;
using System.IO.Compression;
using Sts2Solver.Ai.Combat;
using Sts2Solver.Ai.Combat.Policy;
using Sts2Solver.Simulation.Combat;

namespace Differential;

internal sealed record PotionScheduleObservation(int Turn, int? MaximumUses,
    int InitialUses, int MinimumUses, bool Forced);

/// <summary>Offline-only experiment; default production plans remain untouched.</summary>
internal static class PotionScheduleTrial
{
    internal static bool IsValid(string? name) => name is null or "one-allowed" or "one-two-three" or "one-two-allowed";

    internal static SearchPlan Build(SearchPlan plan, BeamOptions options, string name, bool forced)
    {
        if (!IsValid(name)) throw new InvalidDataException("未知实验用药调度。");
        if (forced) return plan;
        // Both defaults keep the coordinator's existing duplicate-limit handling.
        if (name == "one-allowed") return plan with { DefaultPotionSchedule = DefaultPotionSchedule.OneAllowed };
        if (name == "one-two-three") return plan with { DefaultPotionSchedule = DefaultPotionSchedule.OneTwoThree };
        SearchMember[] defaults = PortfolioBuilder.Build(plan,
            options.PowerCommitments is { Rules.Length: > 0 }, forced: false);
        List<SearchMember> members = [.. defaults.Where(m => m.Potions == SearchPotionLimit.None)];
        members.Add(new(plan.NarrowWidth, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.One));
        members.Add(new(plan.BaselineWidth, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.Two));
        members.Add(new(plan.BaselineWidth, SearchEvaluation.Standard, SearchRetention.Standard, SearchPotionLimit.Allowed));
        return plan with { CustomMembers = ImmutableArray.CreateRange(members) };
    }

    internal static int Checks()
    {
        static void Assert(bool value, string message)
        { if (!value) throw new InvalidDataException("potion-schedule: " + message); }
        using FileStream input = File.OpenRead("tests/Golden/data/d2/requests/023d47d587f3c92f1784abbeb0d4bcbf43f69012f1cfb5c8d6cea34ff99da775.request.br");
        using BrotliStream compressed = new(input, CompressionMode.Decompress);
        using MemoryStream bytes = new(); compressed.CopyTo(bytes);
        SearchCase fixture = SearchCodec.DecodeRequest(bytes.ToArray());
        BeamOptions options = fixture.Options with
        {
            InitialExplicitPotionUses = 0, MinimumPotionUses = 0,
            EnforceForcedPotionDirectives = false, ForcedPotionDirectives = ForcedPotionDirectives.None,
            Potions = new(int.MinValue, null, new(PotionUseMode.Smart, []), PotionUseMode.Smart, false),
        };
        fixture = fixture with { Options = options };
        SearchPlan plan = new(30, 20, 45, 12, 10_000, 100_000_000);
        foreach ((string name, SearchPotionLimit[] tail, int?[] caps) in new[]
        {
            ("one-allowed", new[] { SearchPotionLimit.One, SearchPotionLimit.Allowed }, new int?[] { 1, null }),
            ("one-two-three", new[] { SearchPotionLimit.One, SearchPotionLimit.Two, SearchPotionLimit.Three }, new int?[] { 1, 2, 3 }),
            ("one-two-allowed", new[] { SearchPotionLimit.One, SearchPotionLimit.Two, SearchPotionLimit.Allowed }, new int?[] { 1, 2, null }),
        })
        {
            SearchPlan trial = Build(plan, options, name, forced: false);
            SearchMember[] members = PortfolioBuilder.Build(trial, options.PowerCommitments is { Rules.Length: > 0 }, false);
            SearchMember[] primary = PortfolioBuilder.Build(plan, options.PowerCommitments is { Rules.Length: > 0 }, false)
                .Where(m => m.Potions == SearchPotionLimit.None).ToArray();
            Assert(members.Take(members.Length - tail.Length).SequenceEqual(primary), name + " unchanged primary members");
            Assert(members.TakeLast(tail.Length).Select(m => m.Potions).SequenceEqual(tail), name + " planned order");
            Assert(members.TakeLast(tail.Length).Select(m => m.Width).SequenceEqual(
                Enumerable.Repeat(plan.BaselineWidth, tail.Length).Select((w, i) => i == 0 ? plan.NarrowWidth : w)), name + " widths");
            List<BeamOptions> observed = [];
            long clock = 0;
            ValueTask<SearchMemberMeasurement> Run(SearchCase item, SearchStorage _, CancellationToken __)
            {
                observed.Add(item.Options); clock += 10;
                return ValueTask.FromResult(new SearchMemberMeasurement(null, "none", null, 10));
            }
            CombatSearchResult result = CombatSearchCoordinator.RunAsync(new(fixture, trial), new(), Run,
                CancellationToken.None, () => clock).AsTask().GetAwaiter().GetResult();
            Assert(result.Members.All(m => m.Ran) && observed.TakeLast(caps.Length)
                .Select(o => o.Potions.MaximumUses).SequenceEqual(caps), name + " coordinator order and caps");
            Assert(observed.All(o => o.InitialExplicitPotionUses == 0 && o.MinimumPotionUses == 0), name + " counts preserved");
            Assert(Build(plan, options, name, forced: true) == plan, name + " forced plan unchanged");
        }
        Assert(!IsValid("bad") && IsValid(null), "validation");
        Console.WriteLine("POTION_SCHEDULE contracts=pass order/caps/forced/default");
        return 0;
    }
}
