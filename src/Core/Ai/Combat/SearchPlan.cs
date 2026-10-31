using System.Collections.Immutable;
using Sts2Solver.Ai.Combat.Policy;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Ai.Combat;

internal enum SearchEvaluation { Standard, Base, Offensive, QueenSetup, Setup, FocusedSetup, ContinuousSetup }
internal enum SearchRetention { Standard, Power, SecondBand, QueenKillTiming }
internal enum SearchPotionLimit { None = 0, One = 1, Allowed = 2, Two = 3, Three = 4 }

/// <summary>Default schedule semantics preserved across versioned requests.</summary>
internal enum DefaultPotionSchedule { OneAllowed = 0, OneTwoThree = 1, StagedPortfolio = 2, CompactOnePortfolio = 3, CompactPotionPortfolio = 4 }

/// <summary>Each member describes the four independent search dimensions.</summary>
internal sealed record SearchMember(int Width, SearchEvaluation Evaluation,
    SearchRetention Retention, SearchPotionLimit Potions)
{
    /// <summary>Whole member budget including initialization and replay; zero inherits the plan.</summary>
    public int TimeLimitMilliseconds { get; init; }
}

internal sealed record SearchPlan(int BaselineWidth, int NarrowWidth, int WideWidth,
    int OffensiveWidth, int TotalMilliseconds, long MemoryBytes)
{
    /// <summary>Time per beam slot; positive selects independent member deadlines, zero retains legacy replay.</summary>
    public int MillisecondsPerWidth { get; init; }

    /// <summary>Encounter-specific default portfolio; disabled when decoding historical plans.</summary>
    public bool QueenStrategies { get; init; }

    /// <summary>Generic setup members replace the historical QueenSetup member.</summary>
    public bool SetupStrategies { get; init; }

    /// <summary>Wide continuous-gain supplement in the Allowed stage only.</summary>
    public bool ContinuousSetupStrategies { get; init; }

    /// <summary>Historical v8 narrow/focused pair, retained only for replay.</summary>
    public bool LegacySetupPortfolio { get; init; }

    internal static bool IsLongEncounter(bool isBoss, bool isElite, int actIndex)
        => isBoss || isElite && actIndex == 2;

    /// <summary>Historical shared-budget encounter multiplier; independent budgets never multiply.</summary>
    internal SearchPlan ForEncounter(bool longEncounter)
        => longEncounter && MillisecondsPerWidth == 0
            ? this with { TotalMilliseconds = checked(TotalMilliseconds * 2) } : this;

    /// <summary>Historical v6 single-supplement switch, retained for replay compatibility.</summary>
    public bool ExperimentalQueenSupplement { get; init; }
    public ImmutableArray<SearchMember> CustomMembers { get; init; } = [];
    public DefaultPotionSchedule DefaultPotionSchedule { get; init; } = DefaultPotionSchedule.CompactPotionPortfolio;
    internal bool UsesCompactOneStage => DefaultPotionSchedule is DefaultPotionSchedule.CompactOnePortfolio or DefaultPotionSchedule.CompactPotionPortfolio;
    internal bool UsesStagedPortfolio => DefaultPotionSchedule == DefaultPotionSchedule.StagedPortfolio || UsesCompactOneStage;
    public int EndedKeepWidths { get; init; } = 8;
    public int EndedTriggerWidths { get; init; } = 16;
}

/// <summary>A frozen root and policy with the complete decision budget.</summary>
internal sealed class CombatSearchRequest
{
    private readonly SearchCase _case;

    internal CombatSearchRequest(SearchCase searchCase, SearchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(searchCase);
        ArgumentNullException.ThrowIfNull(plan);
        _case = CopyCase(searchCase);
        Plan = plan;
    }

    // Rules and scoring tables are root-frozen by their compilers. Copy the mutable root and
    // request-policy arrays both on entry and on access so callers cannot mutate the request.
    internal SearchCase Case => CopyCase(_case);
    internal SearchPlan Plan { get; }

    private static SearchCase CopyCase(SearchCase source)
    {
        BeamOptions options = source.Options;
        ForcedPotionDirectives forced = options.ForcedPotionDirectives;
        PowerCommitmentCatalog? power = options.PowerCommitments;
        return source with
        {
            Battles = (Battle[])source.Battles.Clone(),
            Options = options with
            {
                Potions = options.Potions with { Strategy = options.Potions.Strategy.Copy() },
                ForcedPotionDirectives = forced with
                {
                    Slots = (int[])forced.Slots.Clone(),
                    PotionIds = (ushort[])forced.PotionIds.Clone(),
                    StrategicHpCosts = (int[])forced.StrategicHpCosts.Clone(),
                },
                PowerCommitments = power is null ? null
                    : power with { Rules = (PowerCommitmentRule[])power.Rules.Clone() },
            },
        };
    }
}

internal enum CombatSearchStatus { Victory, Defeat, Incomplete, Cancelled, Invalid }

internal sealed record SearchMemberResult(SearchMember Member, bool Ran, string? SkippedReason,
    long ElapsedMilliseconds, long ExpandedNodes, string StopReason, bool Victory,
    int? HpLost, int? PotionUses, bool Adopted, long MemoryPeakBytes, int EndedCompactions)
{
    public int? EffectivePotionLimit { get; init; }
    public IReadOnlyList<PotionCandidateSummary> PotionCandidates { get; init; } = [];
    public bool PotionCandidatesComplete { get; init; } = true;
    public CombatSearchStatus? ResultStatus { get; init; }
    public bool? PolicyEligible { get; init; }
    public int CandidatePoolPeakCount { get; init; }
    public int ActualWidth => Ran ? Member.Width : 0;
    public long MemoryUsedBytes { get; init; }
    public bool MemoryBudgetExceeded { get; init; }
    public long MemoryExceededAtBytes { get; init; }
    public int PoolCapacityDropped { get; init; }
    public long RouteNodeStorageBytes { get; init; }
    public int RouteNodePeakCount { get; init; }
    public long ManagedHeapPeakBytes { get; init; }
    public long WorkingSetPeakBytes { get; init; }
}

internal sealed record CombatSearchResult(CombatSearchStatus Status, BeamResult? Beam,
    int SelectedMember, long ElapsedMilliseconds, long ExpandedNodes,
    SearchMemberResult[] Members, string? Error)
{
    internal long ElapsedMs => ElapsedMilliseconds;
    internal long TotalExpandedNodes => ExpandedNodes;
    internal string FailureStatus => Status.ToString().ToLowerInvariant();
    internal string? FailureError => Error;
    internal int MembersRun => Members.Count(m => m.Ran);
    internal int MembersSkippedByTime => Members.Count(m => m.SkippedReason == "time");
    internal bool SoftTimeRisk => Members.Any(m => m.SkippedReason == "time" || m.StopReason is "time" or "deadline");
    internal bool MemoryBudgetExceeded => Members.Any(m => m.MemoryBudgetExceeded);
    internal long MemoryExceededAtBytes => Members.Select(m => m.MemoryExceededAtBytes).FirstOrDefault(v => v != 0);
    internal int PoolCapacityDropped => (int)Math.Min(int.MaxValue, Members.Sum(m => (long)m.PoolCapacityDropped));
    internal long SearchMemoryUsedBytes => Members.Select(m => m.MemoryUsedBytes).DefaultIfEmpty().Max();
    internal long SearchMemoryPeakBytes => Members.Select(m => m.MemoryPeakBytes).DefaultIfEmpty().Max();
    internal long RouteNodeStorageBytes => Members.Select(m => m.RouteNodeStorageBytes).DefaultIfEmpty().Max();
    internal int RouteNodePeakCount => Members.Select(m => m.RouteNodePeakCount).DefaultIfEmpty().Max();
    internal long ManagedHeapPeakBytes => Members.Select(m => m.ManagedHeapPeakBytes).DefaultIfEmpty().Max();
    internal long WorkingSetPeakBytes => Members.Select(m => m.WorkingSetPeakBytes).DefaultIfEmpty().Max();
}
