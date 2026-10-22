using CombatSolver.Flash.Kernel;
using CombatSolver.Flash.Search.Scoring;

namespace CombatSolver.Flash.Search;

/// <summary>
/// The existing expansion's per-parent action admission: dominated card candidates are dropped,
/// then the surviving candidates are ordered by score and the profile's
/// <c>MaxCardBranchesPerNode</c> family portfolio decides the admitted set. Potions and EndTurn
/// stay outside this stage, as they do in the existing expansion.
/// </summary>
internal sealed partial class FlashBeamSolver
{
    private readonly record struct AdmittedCandidate(
        int Slot,
        int Node,
        double Score,
        double Normalized,
        FlashCardType CardType,
        int TargetCombatId,
        int EnergySpent,
        int StarsSpent,
        int Damage,
        int Block,
        int Hp,
        int MaxHp,
        int CumulativeHpLost,
        int LongTermResourceValue,
        int StrategicHpCredit,
        ulong RelicSatisfiedMask,
        int StrategyGoalCount,
        int AngerCopiesGenerated,
        ActionOptionFamily Families,
        bool IsPure,
        int RevivingEnemyCount,
        int AliveEnemyCount,
        int RawEnemyHp,
        int MaxCurrentEnemyHp,
        int ProjectedHp);

    private void AdmitActionLayerCandidates(Pool next, Pool current)
    {
        if (next.Count == 0 || current.Count == 0)
            return;
        Dictionary<int, int> parentSlots = new(current.Count);
        for (int index = 0; index < current.Count; index++)
            parentSlots[current.Node(index)] = index;
        Dictionary<int, FlashScoreFeatures> parentFeatures = new(current.Count);
        Dictionary<int, List<AdmittedCandidate>> groups = new();
        for (int slot = 0; slot < next.Count; slot++)
        {
            int node = next.Node(slot);
            RouteNode route = _nodes[node];
            if (route.Action.Kind != FlashActionKind.PlayCard
                || !parentSlots.TryGetValue(route.Parent, out int parentSlot))
            {
                continue;
            }
            ref FlashBattle parent = ref current.State(parentSlot);
            if (route.Action.HandIndex >= parent.HandCount)
                continue;
            if (!parentFeatures.TryGetValue(route.Parent, out FlashScoreFeatures before))
            {
                before = _scorer.Features(parent, route.Depth - 1, includeRetentionOnly: true);
                parentFeatures.Add(route.Parent, before);
            }
            FlashScoreFeatures after = _scorer.Features(
                next.State(slot), route.Depth, includeRetentionOnly: true);
            int energy = Math.Max(0, before.Energy - after.Energy);
            int stars = Math.Max(0, before.Stars - after.Stars);
            int damage = Math.Max(0, before.EnemyHp - after.EnemyHp);
            int block = Math.Max(0, after.PlayerBlock - before.PlayerBlock);
            double resource = Math.Max(0.5d, energy + stars * 0.5d);
            FlashCardType cardType = FlashKernel.CardType(
                parent, _rules, parent.Cards[route.Action.HandIndex]);
            ActionOptionFamily families = ClassifyActionFamilies(
                before, after, cardType, route.TargetCombatId, damage, block, route.IsPure);
            AdmittedCandidate candidate = new(slot, node, next.Score(slot),
                (damage + block * 0.8d) / resource, cardType, route.TargetCombatId,
                energy, stars, damage, block, after.PlayerHp, after.PlayerMaxHp,
                after.CumulativePlayerHpLost, after.LongTermResourceValue, after.StrategicHpCredit,
                after.RelicSatisfiedMask, after.StrategyGoalCount, after.AngerCopiesGenerated,
                families, route.IsPure, after.RevivingEnemyCount, after.AliveEnemyCount,
                after.RawEnemyHp, after.MaxCurrentEnemyHp, after.ProjectedPlayerHp);
            if (!groups.TryGetValue(route.Parent, out List<AdmittedCandidate>? group))
            {
                group = new List<AdmittedCandidate>(8);
                groups.Add(route.Parent, group);
            }
            group.Add(candidate);
        }
        if (groups.Count == 0)
            return;
        HashSet<int> remove = new();
        foreach ((int parentNode, List<AdmittedCandidate> group) in groups)
        {
            FlashScoreFeatures parentFacts = parentFeatures[parentNode];
            List<AdmittedCandidate> nonDominated = NonDominated(group);
            HashSet<int> selected = SelectCandidates(nonDominated, parentFacts);
            for (int index = 0; index < group.Count; index++)
            {
                if (!selected.Contains(group[index].Slot))
                    remove.Add(group[index].Slot);
            }
        }
        next.RemoveSlots(remove);
    }

    private static List<AdmittedCandidate> NonDominated(List<AdmittedCandidate> candidates)
    {
        List<AdmittedCandidate> kept = new(candidates.Count);
        for (int index = 0; index < candidates.Count; index++)
        {
            AdmittedCandidate candidate = candidates[index];
            bool dominated = false;
            for (int prior = kept.Count - 1; prior >= 0; prior--)
            {
                if (!Dominates(kept[prior], candidate))
                    continue;
                dominated = true;
                break;
            }
            if (dominated)
                continue;
            for (int prior = kept.Count - 1; prior >= 0; prior--)
            {
                if (Dominates(candidate, kept[prior]))
                    kept.RemoveAt(prior);
            }
            kept.Add(candidate);
        }
        return kept;
    }

    private static bool Dominates(in AdmittedCandidate left, in AdmittedCandidate right)
    {
        if (!left.IsPure || !right.IsPure
            || left.CardType != right.CardType
            || left.TargetCombatId != right.TargetCombatId
            || left.Families != right.Families
            || left.EnergySpent != right.EnergySpent
            || left.StarsSpent != right.StarsSpent)
        {
            return false;
        }
        bool noWorse = left.Damage >= right.Damage
            && left.Block >= right.Block
            && left.Hp >= right.Hp
            && left.MaxHp >= right.MaxHp
            && left.CumulativeHpLost <= right.CumulativeHpLost
            && left.LongTermResourceValue >= right.LongTermResourceValue
            && left.StrategicHpCredit >= right.StrategicHpCredit
            && (left.RelicSatisfiedMask & right.RelicSatisfiedMask) == right.RelicSatisfiedMask
            && left.StrategyGoalCount >= right.StrategyGoalCount
            && left.AngerCopiesGenerated <= right.AngerCopiesGenerated;
        if (!noWorse)
            return false;
        return left.Damage > right.Damage
            || left.Block > right.Block
            || left.Hp > right.Hp
            || left.MaxHp > right.MaxHp
            || left.CumulativeHpLost < right.CumulativeHpLost
            || left.LongTermResourceValue > right.LongTermResourceValue
            || left.AngerCopiesGenerated < right.AngerCopiesGenerated;
    }

    private HashSet<int> SelectCandidates(
        List<AdmittedCandidate> candidates, in FlashScoreFeatures parent)
    {
        List<AdmittedCandidate> sorted = new(candidates);
        sorted.Sort(static (left, right) =>
        {
            int comparison = right.Score.CompareTo(left.Score);
            return comparison != 0 ? comparison : right.Normalized.CompareTo(left.Normalized);
        });
        int limit = Math.Min(_options.MaxCardBranchesPerNode, sorted.Count);
        if (limit <= 0)
            return [];
        List<int> selected = new(limit + 2);
        HashSet<int> selectedSlots = new();
        void Add(AdmittedCandidate candidate, bool allowOverflow = false)
        {
            if (!allowOverflow && selected.Count >= limit)
                return;
            if (!selectedSlots.Add(candidate.Slot))
                return;
            selected.Add(candidate.Slot);
        }

        // A resolved routing choice is a semantic branch boundary and keeps the existing
        // overflow behavior.
        for (int index = 0; index < sorted.Count; index++)
        {
            if (_nodes[sorted[index].Node].RoutingSignatureId != 0)
                Add(sorted[index], allowOverflow: true);
        }
        AdmittedCandidate? revival = null;
        for (int index = 0; index < sorted.Count; index++)
        {
            AdmittedCandidate candidate = sorted[index];
            if (candidate.RevivingEnemyCount <= parent.RevivingEnemyCount)
                continue;
            if (revival is not { } current)
            {
                revival = candidate;
                continue;
            }
            int comparison = current.RevivingEnemyCount.CompareTo(candidate.RevivingEnemyCount);
            if (comparison == 0)
                comparison = candidate.RawEnemyHp.CompareTo(current.RawEnemyHp);
            if (comparison == 0)
                comparison = candidate.MaxCurrentEnemyHp.CompareTo(current.MaxCurrentEnemyHp);
            if (comparison == 0)
                comparison = current.ProjectedHp.CompareTo(candidate.ProjectedHp);
            if (comparison >= 0)
                continue;
            revival = candidate;
        }
        if (revival is { } revivalCandidate)
            Add(revivalCandidate, allowOverflow: true);

        ReadOnlySpan<ActionOptionFamily> families =
        [
            ActionOptionFamily.ImmediateDefense,
            ActionOptionFamily.ImmediateOffense,
            ActionOptionFamily.ResourceAndCycle,
            ActionOptionFamily.PersistentSetup,
            ActionOptionFamily.Control,
            ActionOptionFamily.TargetRemoval,
            ActionOptionFamily.HpInvestment,
        ];
        foreach (ActionOptionFamily family in families)
        {
            for (int index = 0; index < sorted.Count; index++)
            {
                if (!sorted[index].Families.HasFlag(family))
                    continue;
                Add(sorted[index]);
                break;
            }
        }
        HashSet<int> seenDamageTargets = [];
        for (int index = 0; index < sorted.Count; index++)
        {
            AdmittedCandidate candidate = sorted[index];
            if (candidate.TargetCombatId == int.MinValue
                || candidate.Damage <= 0
                || !seenDamageTargets.Add(candidate.TargetCombatId))
            {
                continue;
            }
            Add(candidate);
        }
        for (int index = 0; index < sorted.Count; index++)
            Add(sorted[index]);
        return selectedSlots;
    }

    /// <summary>
    /// <c>SearchProbeHelpers.ClassifyActionOptionFamilies</c> over the K4-checked Flash features.
    /// Every term is the same snapshot member the existing classifier reads.
    /// </summary>
    private static ActionOptionFamily ClassifyActionFamilies(
        in FlashScoreFeatures before, in FlashScoreFeatures after, FlashCardType cardType,
        int targetCombatId, int damage, int block, bool pure)
    {
        ActionOptionFamily families = ActionOptionFamily.None;
        if (block > 0
            || after.ProjectedPlayerHp > before.ProjectedPlayerHp
            || after.PlayerHp > before.PlayerHp
            || after.StrategicPreventionPotential > before.StrategicPreventionPotential)
        {
            families |= ActionOptionFamily.ImmediateDefense;
        }
        if (damage > 0
            || after.StrategicDamagePotential > before.StrategicDamagePotential)
            families |= ActionOptionFamily.ImmediateOffense;
        if (after.Energy > before.Energy
            || after.Stars > before.Stars
            || after.HandCount >= before.HandCount
            || after.ReachableHandValue > before.ReachableHandValue
            || after.ZeroCostPlayableCount > before.ZeroCostPlayableCount
            || after.FutureResourceValue > before.FutureResourceValue
            || after.StrategicResourcePotential > before.StrategicResourcePotential
            || after.StrategicCardAccessPotential > before.StrategicCardAccessPotential
            || after.LiveDeckClutter < before.LiveDeckClutter)
        {
            families |= ActionOptionFamily.ResourceAndCycle;
        }
        if (cardType == FlashCardType.Power
            || after.PersistentBuffValue > before.PersistentBuffValue
            || after.DelayedDamageValue > before.DelayedDamageValue
            || after.ReplayPotentialValue > before.ReplayPotentialValue
            || after.ReactiveDamageValue > before.ReactiveDamageValue
            || after.StrategicRetentionValue > before.StrategicRetentionValue
            || after.LongTermResourceValue > before.LongTermResourceValue)
        {
            families |= ActionOptionFamily.PersistentSetup;
        }
        if (after.SandpitRemaining > before.SandpitRemaining
            || after.EnemyStrengthSuppression > before.EnemyStrengthSuppression
            || after.EnemyWeakTurns > before.EnemyWeakTurns
            || after.EnemyVulnerableTurns > before.EnemyVulnerableTurns
            || after.LiveDeckClutter < before.LiveDeckClutter
            || !pure && damage == 0 && block == 0)
        {
            families |= ActionOptionFamily.Control;
        }
        if (targetCombatId != int.MinValue
            && (after.AliveEnemyCount < before.AliveEnemyCount
                || after.FocusTargetCurrentThreat < before.FocusTargetCurrentThreat))
        {
            families |= ActionOptionFamily.TargetRemoval;
        }
        if (after.PlayerHp < before.PlayerHp
            || after.PlayerMaxHp < before.PlayerMaxHp
            || after.CumulativePlayerHpLost > before.CumulativePlayerHpLost)
        {
            families |= ActionOptionFamily.HpInvestment;
        }
        return families;
    }
}
