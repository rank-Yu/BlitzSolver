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
        bool requiredPlan = forced || plan.CustomMembers.IsDefaultOrEmpty
            && plan.DefaultPotionSchedule == DefaultPotionSchedule.StagedPortfolio
            && (initial.Options.MinimumPotionUses > 0
                || initial.Options.EffectivePotionPolicy == PotionUseMode.RequireAtLeastOne);
        SearchMember[] members = PortfolioBuilder.Build(plan,
            initial.Options.PowerCommitments is { Rules.Length: > 0 }, requiredPlan);
        QueenSearchPolicy? queen = (plan.QueenStrategies || plan.ExperimentalQueenSupplement && !requiredPlan && !forced)
            && plan.CustomMembers.IsDefaultOrEmpty && initial.Battles.Length == 1 && initial.Options.Novelty is null
            ? QueenSearchPolicy.Capture(initial.Battles[0], initial.Rules) : null;
        bool setup = plan.SetupStrategies && plan.CustomMembers.IsDefaultOrEmpty
            && initial.Options.Novelty is null && initial.Battles.Length == 1;
        SetupSearchPolicy? setupPolicy = setup ? SetupSearchPolicy.Capture(initial.Battles[0], initial.Rules) : null;
        SetupSearchPolicy? focusedSetup = setup ? SetupSearchPolicy.Capture(initial.Battles[0], initial.Rules, focused: true) : null;
        if (setup || queen is not null && plan.QueenStrategies)
            members = SupplementPortfolio.AppendToStages(members, plan, queen is not null && plan.QueenStrategies);
        else if (queen is not null)
        {
            int firstPotion = Array.FindIndex(members, m => m.Potions != SearchPotionLimit.None);
            int insertion = firstPotion < 0 ? members.Length : firstPotion;
            members = [.. members.Take(insertion),
                new(plan.BaselineWidth, SearchEvaluation.QueenSetup, SearchRetention.Standard, SearchPotionLimit.None),
                .. members.Skip(insertion)];
        }
        QueenSearchPolicy? timing = queen is null ? null : queen with { PreserveKillTiming = true };
        // Resolve all limits before dividing time so skipped and duplicate layers receive no share.
        PotionPolicy[] policies = new PotionPolicy[members.Length];
        string?[] skips = new string?[members.Length];
        HashSet<int?> seenLimits = [];
        HashSet<(int Width, SearchEvaluation Evaluation, SearchRetention Retention, int? Limit)> seenMembers = [];
        for (int i = 0; i < members.Length; i++)
        {
            SearchMember item = members[i];
            PotionPolicy policy = PotionSearchSchedule.Limit(initial.Options.Potions, item.Potions,
                initial.Options.InitialExplicitPotionUses);
            policies[i] = policy;
            bool potionPhase = !requiredPlan && !forced && item.Potions != SearchPotionLimit.None;
            if (policy.MaximumUses is { } cap && cap < initial.Options.MinimumPotionUses)
                skips[i] = "minimum-exceeds-limit";
            else if (potionPhase && !canUsePotions)
                skips[i] = "no-allowed-potions";
            else if (potionPhase && policy.MaximumUses is { } limit
                && limit <= initial.Options.InitialExplicitPotionUses)
                skips[i] = "limit-already-used";
            else if (potionPhase && plan.CustomMembers.IsDefaultOrEmpty
                && plan.DefaultPotionSchedule == DefaultPotionSchedule.StagedPortfolio
                && !seenMembers.Add((item.Width, item.Evaluation, item.Retention, policy.MaximumUses)))
                skips[i] = "duplicate-limit";
            else if (potionPhase && plan.CustomMembers.IsDefaultOrEmpty
                && plan.DefaultPotionSchedule != DefaultPotionSchedule.StagedPortfolio && !seenLimits.Add(policy.MaximumUses))
                skips[i] = "duplicate-limit";
        }
        budget.ReservePotionPhase(!requiredPlan && !forced && Enumerable.Range(0, members.Length)
            .Any(i => members[i].Potions != SearchPotionLimit.None && skips[i] is null));
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
            bool potionPhase = !requiredPlan && !forced && member.Potions != SearchPotionLimit.None;
            bool stageStart = index == 0 || members[index - 1].Potions != member.Potions;
            if (potionPhase && stageStart && !requiredPlan && plan.CustomMembers.IsDefaultOrEmpty
                && plan.DefaultPotionSchedule == DefaultPotionSchedule.StagedPortfolio
                && PotionStageGate.CanSkip(initial.Battles, initial.Rules, initial.Options, selected, free))
            {
                for (int j = index; j < members.Length && members[j].Potions == member.Potions; j++)
                    if (PotionStageGate.HasRunCombination(logs, members[j]))
                        skips[j] ??= "stage-economy";
            }
            if (skips[index] is { } reason)
            {
                logs.Add(new SearchMemberResult(member, false, reason, 0, 0, "skipped", false, null, null, false, 0, 0)
                    { EffectivePotionLimit = policies[index].MaximumUses });
                continue;
            }
            long remaining = budget.Remaining(potionPhase);
            int phaseRemaining = 0;
            for (int i = index; i < members.Length; i++)
                if (skips[i] is null
                    && (SupplementPortfolio.IsSupplement(member) || !SupplementPortfolio.IsSupplement(members[i]))
                    && (!requiredPlan && !forced && members[i].Potions != SearchPotionLimit.None) == potionPhase)
                    phaseRemaining++;
            if (remaining <= 1 || Expired())
            {
                logs.Add(new SearchMemberResult(member, false, "time", 0, 0, "skipped", false, null, null, false, 0, 0)
                    { EffectivePotionLimit = policies[index].MaximumUses });
                continue;
            }
            // Leave a small part of each share for replay, bookkeeping and cancellation checkpoints.
            long searchMs = SearchBudget.SearchShare(remaining, phaseRemaining);
            PotionPolicy potions = policies[index];
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
                    baseOnly: member.Evaluation is SearchEvaluation.Base or SearchEvaluation.QueenSetup or SearchEvaluation.FocusedSetup,
                    offensive: member.Evaluation == SearchEvaluation.Offensive,
                    queen: member.Evaluation == SearchEvaluation.QueenSetup ? queen
                        : member.Retention == SearchRetention.QueenKillTiming ? timing : null,
                    setup: member.Evaluation == SearchEvaluation.Setup ? setupPolicy
                        : member.Evaluation == SearchEvaluation.FocusedSetup ? focusedSetup : null),
                SecondRankBand = member.Retention == SearchRetention.SecondBand,
                SecondRankBandWidth = member.Width,
                AggressivePowerCommitment = member.Retention == SearchRetention.Power,
                Potions = potions,
                AuditPotionPolicy = potionPhase || requiredPlan || initial.Options.InitialExplicitPotionUses > 0
                    || initial.Options.MinimumPotionUses > 0,
                CollectPotionCandidates = potionPhase || forced
                    || requiredPlan && member.Potions != SearchPotionLimit.None,
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
                        && candidate.ExplicitPotionUseCount == 0
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
                EffectivePotionLimit = potions.MaximumUses,
                PotionCandidates = beam?.PotionCandidates ?? [],
                PotionCandidatesComplete = beam?.PotionCandidatesComplete ?? true,
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
            logs.Add(new SearchMemberResult(members[logs.Count], false, error != null ? "invalid" : cancelled ? "cancelled" : goalReached ? "target-reached" : "memory", 0, 0, "skipped", false, null, null, false, 0, 0)
                { EffectivePotionLimit = policies[logs.Count].MaximumUses });
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
