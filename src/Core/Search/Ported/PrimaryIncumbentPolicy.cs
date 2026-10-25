// Ported from CombatSolver src/Search/Probes/PrimaryIncumbentPolicy.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

/// <summary>
/// The existing primary member's incumbent pruning rule, shared by the Beam retention policy and
/// the Flash retained-seat lane. Pure functions over frozen request values.
/// </summary>
internal static class PrimaryIncumbentPolicy
{
    internal static bool ShouldPruneByPrimaryIncumbent(
        int strategicHpLowerBound,
        int turn,
        PrimarySearchIncumbent incumbent,
        bool allowTurnTieBound = true,
        bool pruneEqualHp = false)
        => strategicHpLowerBound > incumbent.StrategicHpDeficit
            || pruneEqualHp && strategicHpLowerBound == incumbent.StrategicHpDeficit
            || allowTurnTieBound && strategicHpLowerBound == incumbent.StrategicHpDeficit
                && turn > incumbent.CombatEndedTurn;

    internal static bool TryTightenPrimarySearchIncumbent(
        PotionFreePolicyBaseline? auditedPotionFreeBaseline,
        int minimumPotionUses,
        int? maximumPotionUses,
        bool candidateCompleteVictory,
        bool candidateSatisfiesHardRules,
        int candidateExplicitPotionUses,
        int candidateStrategicHpDeficit,
        int? candidateCombatEndedTurn,
        ref PrimarySearchIncumbent? incumbent,
        SolverPotionPolicy? effectivePotionPolicy = null,
        int candidateDeathSaveUseCount = 0,
        int? candidateExplicitPotionStrategicCost = null)
    {
        if (!candidateCompleteVictory
            || !candidateSatisfiesHardRules
            || candidateDeathSaveUseCount > 0
            || candidateExplicitPotionUses != minimumPotionUses
            || candidateCombatEndedTurn is not { } combatEndedTurn)
        {
            return false;
        }

        // A complete, hard-policy-compliant victory without explicit potion use is
        // already eligible under Disabled/Smart. It needs no separate potion audit.
        // Positive exact layers retain their stricter, audited eligibility proof.
        bool eligiblePotionFreeVictory = minimumPotionUses == 0
            && effectivePotionPolicy is SolverPotionPolicy.Disabled or SolverPotionPolicy.Smart;
        bool eligibleExactPotionVictory = auditedPotionFreeBaseline is { } baseline
            && minimumPotionUses > 0
            && maximumPotionUses == minimumPotionUses
            && SolverInterimResultOrdering.ComparePrimaryQuality(
                candidateCompleteVictory: true,
                candidateStrategicHpDeficit,
                candidateCombatEndedTurn,
                currentCompleteVictory: baseline.Won,
                currentStrategicHpDeficit: baseline.HpDeficit,
                currentCombatEndedTurn: baseline.CombatEndedTurn,
                candidateDeathSaveUseCount: candidateDeathSaveUseCount,
                currentDeathSaveUseCount: baseline.DeathSaveUseCount) < 0;
        if (!eligiblePotionFreeVictory && !eligibleExactPotionVictory)
        {
            return false;
        }

        PrimarySearchIncumbent candidate = new(
            candidateStrategicHpDeficit,
            combatEndedTurn,
            minimumPotionUses == 0 ? 0 : candidateExplicitPotionStrategicCost);
        if (incumbent is { } current
            && SolverInterimResultOrdering.ComparePrimaryQuality(
                candidateCompleteVictory: true,
                candidate.StrategicHpDeficit,
                candidate.CombatEndedTurn,
                currentCompleteVictory: true,
                currentStrategicHpDeficit: current.StrategicHpDeficit,
                currentCombatEndedTurn: current.CombatEndedTurn) >= 0)
        {
            return false;
        }

        incumbent = candidate;
        return true;
    }
}
