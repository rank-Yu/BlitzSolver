// Adapted from CombatSolver src/Search/CombatSearchCoordinator.BeamPortfolio.cs and
// CombatSearchCoordinator.PotionChain.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230
// and contributors). Scheduling uses one absolute deadline and no node allowance.
using Sts2Solver.Ai.Combat.Policy;
using Sts2Solver.Ai.Combat.Scoring;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Ai.Combat;

internal sealed record SearchMemberMeasurement(BeamResult? Beam, string Status, string? Error,
    long ElapsedMilliseconds, long ManagedHeapPeakBytes = 0, long WorkingSetPeakBytes = 0);

internal static class CombatSearchCoordinator
{
    internal static async ValueTask<CombatSearchResult> RunAsync(CombatSearchRequest request,
        SearchStorage storage,
        Func<SearchCase, SearchStorage, CancellationToken, ValueTask<SearchMemberMeasurement>> run,
        CancellationToken token, Func<long>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(run);
        SearchPlan plan = request.Plan;
        SearchBudget budget = new(plan.TotalMilliseconds, reservePotionPhase: false, clock);
        if (plan.BaselineWidth <= 0 || plan.NarrowWidth <= 0 || plan.WideWidth <= 0
            || plan.OffensiveWidth <= 0 || plan.TotalMilliseconds <= 0 || plan.MemoryBytes <= 0
            || plan.EndedKeepWidths < 2 || plan.EndedTriggerWidths <= plan.EndedKeepWidths)
            throw new ArgumentOutOfRangeException(nameof(request), "搜索参数无效。");
        SearchCase initial = request.Case;
        if (initial.Battles.Length == 0) throw new InvalidDataException("搜索根为空。");
        if (initial.Options.Scorer is not { } scorer) throw new InvalidDataException("缺少评分配置。");
        bool forced = PotionSearchSchedule.HasForcedDirectives(initial.Options);
        bool canUsePotions = PotionSearchSchedule.HasAllowedUse(initial.Battles, initial.Rules, initial.Options);
        budget.ReservePotionPhase(!forced && canUsePotions);
        SearchMember[] members = PortfolioBuilder.Build(plan,
            initial.Options.PowerCommitments is { Rules.Length: > 0 }, forced);
        bool Expired() => budget.Expired();
        BeamSolver quality = new(initial.Rules, initial.Options with { AuditPotionPolicy = true });
        List<SearchMemberResult> logs = new(members.Length);
        BeamSolver.Candidate? free = null, selected = null;
        BeamResult? best = null;
        Outcome bestOutcome = Outcome.None;
        int bestIndex = -1;
        long expanded = 0;
        string? error = null;
        bool cancelled = false;
        bool goalReached = false;
        for (int index = 0; index < members.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            SearchMember member = members[index];
            bool potionPhase = !forced && member.Potions != SearchPotionLimit.None;
            if (potionPhase && !canUsePotions)
            {
                logs.Add(new(member, false, "no-allowed-potions", 0, 0, "skipped", false, null, null, false, 0, 0));
                continue;
            }
            long remaining = budget.Remaining(potionPhase);
            int phaseRemaining = 0;
            for (int i = index; i < members.Length; i++)
                if ((!forced && members[i].Potions != SearchPotionLimit.None) == potionPhase)
                    phaseRemaining++;
            if (remaining <= 1 || Expired())
            {
                logs.Add(new(member, false, "time", 0, 0, "skipped", false, null, null, false, 0, 0));
                continue;
            }
            // Leave a small part of each share for replay, bookkeeping and cancellation checkpoints.
            long searchMs = SearchBudget.SearchShare(remaining, phaseRemaining);
            PotionPolicy potions = PotionSearchSchedule.Limit(initial.Options.Potions, member.Potions);
            long memberDeadline = checked(budget.Now + searchMs);
            BeamOptions options = initial.Options with
            {
                BeamWidth = member.Width,
                MaxTurns = int.MaxValue,
                UnlimitedNodes = true,
                DeadlineExpired = () => budget.Now >= memberDeadline || budget.Expired(),
                SoftTimeBudget = TimeSpan.FromMilliseconds(searchMs),
                SearchMemoryBudgetBytes = plan.MemoryBytes,
                StatePoolBudgetBytes = initial.Options.PoolStateBudgetBytes > 0
                    ? initial.Options.PoolStateBudgetBytes : plan.MemoryBytes,
                EndedBatchTarget = checked(member.Width * plan.EndedKeepWidths),
                EndedBatchTrigger = checked(member.Width * plan.EndedTriggerWidths),
                Scorer = new Scorer(scorer.RulesForCodec,
                    baseOnly: member.Evaluation == SearchEvaluation.Base,
                    offensive: member.Evaluation == SearchEvaluation.Offensive),
                SecondRankBand = member.Retention == SearchRetention.SecondBand,
                SecondRankBandWidth = member.Width,
                AggressivePowerCommitment = member.Retention == SearchRetention.Power,
                Potions = potions,
                AuditPotionPolicy = potionPhase || forced,
                PotionFreeBaseline = free is { } freeCandidate ? BeamSolver.PotionBaseline(freeCandidate) : null,
            };
            long memberStarted = budget.Now;
            SearchMemberMeasurement measurement;
            try
            {
                measurement = await run(initial with { Options = options }, storage, token);
            }
            catch (SearchMemoryBudgetException exception)
            {
                measurement = new(null, "memory", exception.Message, Math.Max(0, budget.Now - memberStarted));
            }
            token.ThrowIfCancellationRequested();
            BeamResult? beam = measurement.Beam;
            if (beam == null && measurement.Status == "cancelled") cancelled = true;
            else if (beam == null && measurement.Status is not ("ok" or "none" or "memory"))
                error = measurement.Error ?? measurement.Status;
            expanded += beam?.ExpandedNodesTotal ?? 0;
            bool adopted = false;
            int? hpLost = null;
            int? potionCount = null;
            bool? policyEligible = null;
            CombatSearchStatus? memberStatus = null;
            string stop = beam == null ? measurement.Status
                : beam.SearchMemoryBudgetExceeded ? "memory"
                : beam.SoftTimeBudgetReached ? "time" : beam.StopReason;
            if (beam != null && beam.PotionPolicySatisfied)
            {
                try
                {
                    BeamSolver.Candidate candidate = quality.AssessResult(initial.Battles, beam,
                        token, Expired, out Outcome outcome, out int rawHpLost, out bool meetsGoal);
                    hpLost = rawHpLost;
                    potionCount = candidate.ExplicitPotionUseCount;
                    memberStatus = outcome == Outcome.Victory ? CombatSearchStatus.Victory
                        : outcome is Outcome.Defeat or Outcome.EventDefeat ? CombatSearchStatus.Defeat
                        : CombatSearchStatus.Incomplete;
                    if (!forced && member.Potions == SearchPotionLimit.None
                        && (free is null || quality.ComparePolicyResults(candidate, free.Value) < 0))
                        free = candidate;
                    policyEligible = quality.IsPolicyEligible(candidate, forced ? null : free);
                    if (policyEligible.Value
                        && (selected is null || quality.ComparePolicyResults(candidate, selected.Value) < 0))
                    {
                        selected = candidate;
                        best = beam;
                        bestOutcome = outcome;
                        bestIndex = index;
                        adopted = true;
                        goalReached = meetsGoal;
                    }
                }
                catch (TimeoutException) { stop = "deadline"; }
                catch (InvalidDataException exception) { error = exception.Message; stop = "invalid"; }
                catch (CapacityException exception) { error = exception.Message; stop = "invalid"; }
            }
            logs.Add(new(member, true, null, Math.Max(0, budget.Now - memberStarted),
                beam?.ExpandedNodesTotal ?? 0, stop, beam?.BestIsVictory ?? false, hpLost, potionCount,
                adopted, beam?.SearchMemoryPeakBytes ?? storage.Memory.PeakBytes,
                beam?.EndedCompactions ?? 0)
            {
                ResultStatus = memberStatus,
                PolicyEligible = policyEligible,
                CandidatePoolPeakCount = beam?.CandidatePoolPeakCount ?? 0,
                MemoryUsedBytes = beam?.SearchMemoryUsedBytes ?? storage.Memory.UsedBytes,
                MemoryBudgetExceeded = beam?.SearchMemoryBudgetExceeded ?? stop == "memory",
                MemoryExceededAtBytes = beam?.SearchMemoryExceededAtBytes ?? 0,
                PoolCapacityDropped = beam != null && beam.CapacityDroppedChildren.TryGetValue("Beam.Pool", out int dropped) ? dropped : 0,
                RouteNodeStorageBytes = beam?.RouteNodeStorageBytes ?? 0,
                RouteNodePeakCount = beam?.RouteNodePeakCount ?? 0,
                ManagedHeapPeakBytes = measurement.ManagedHeapPeakBytes,
                WorkingSetPeakBytes = measurement.WorkingSetPeakBytes,
            });
            if (error != null || cancelled || stop == "memory" || goalReached) break;
        }
        while (logs.Count < members.Length)
            logs.Add(new(members[logs.Count], false, error != null ? "invalid" : cancelled ? "cancelled" : goalReached ? "target-reached" : "memory", 0, 0, "skipped", false, null, null, false, 0, 0));
        if (bestIndex >= 0)
            for (int i = 0; i < logs.Count; i++) logs[i] = logs[i] with { Adopted = i == bestIndex };
        CombatSearchStatus status = cancelled ? CombatSearchStatus.Cancelled
            : error != null ? CombatSearchStatus.Invalid
            : best == null ? CombatSearchStatus.Incomplete
            : bestOutcome == Outcome.Victory ? CombatSearchStatus.Victory
            : bestOutcome is Outcome.Defeat or Outcome.EventDefeat ? CombatSearchStatus.Defeat
            : CombatSearchStatus.Incomplete;
        return new(status, error == null && !cancelled ? best : null, bestIndex, budget.Elapsed,
            expanded, [.. logs], error);
    }
}
