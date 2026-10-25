// Ported from CombatSolver src/Search/Probes/PrimaryIncumbentPolicy.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
using BlitzSolver.Search.Policy;

namespace BlitzSolver.Search.Quality;

/// <summary>
/// The existing primary member's incumbent pruning rule, shared by the Beam retention policy and
/// the Blitz retained-seat lane. Pure functions over frozen request values.
/// </summary>
internal static class IncumbentPolicy
{
    internal static bool ShouldPruneByIncumbent(
        int strategicHpLowerBound,
        int turn,
        SearchIncumbent incumbent,
        bool allowTurnTieBound = true,
        bool pruneEqualHp = false)
        => strategicHpLowerBound > incumbent.StrategicHpDeficit
            || pruneEqualHp && strategicHpLowerBound == incumbent.StrategicHpDeficit
            || allowTurnTieBound && strategicHpLowerBound == incumbent.StrategicHpDeficit
                && turn > incumbent.CombatEndedTurn;

    internal static bool TryTightenIncumbent(
        PotionFreePolicyBaseline? auditedPotionFreeBaseline,
        int minimumPotionUses,
        int? maximumPotionUses,
        bool candidateCompleteVictory,
        bool candidateSatisfiesHardRules,
        int candidateExplicitPotionUses,
        int candidateStrategicHpDeficit,
        int? candidateCombatEndedTurn,
        ref SearchIncumbent? incumbent,
        PotionUseMode? effectivePotionPolicy = null,
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
            && effectivePotionPolicy is PotionUseMode.Disabled or PotionUseMode.Smart;
        bool eligibleExactPotionVictory = auditedPotionFreeBaseline is { } baseline
            && minimumPotionUses > 0
            && maximumPotionUses == minimumPotionUses
            && InterimResultOrdering.ComparePrimaryQuality(
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

        SearchIncumbent candidate = new(
            candidateStrategicHpDeficit,
            combatEndedTurn,
            minimumPotionUses == 0 ? 0 : candidateExplicitPotionStrategicCost);
        if (incumbent is { } current
            && InterimResultOrdering.ComparePrimaryQuality(
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
