// Ported from CombatSolver src/Search/SolverInterimResultOrdering.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

internal static class SolverInterimResultOrdering
{
    public static bool IsCompleteVictory(
        int actionCount,
        bool allEnemiesDead,
        bool playerDead,
        int projectedPlayerHp)
        => actionCount > 0
            && allEnemiesDead
            && !playerDead
            && projectedPlayerHp > 0;

    public static int ComparePrimaryQuality(
        bool candidateCompleteVictory,
        int candidateStrategicHpDeficit,
        int? candidateCombatEndedTurn,
        bool currentCompleteVictory,
        int currentStrategicHpDeficit,
        int? currentCombatEndedTurn,
        int candidateGrowthHpCredit = 0,
        int currentGrowthHpCredit = 0,
        int candidateGrowthRewardCount = 0,
        int currentGrowthRewardCount = 0,
        int candidateDeathSaveUseCount = 0,
        int currentDeathSaveUseCount = 0)
        => RouteQualityPolicy.Compare(
            RouteQuality.Primary(candidateCompleteVictory, candidateStrategicHpDeficit,
                candidateCombatEndedTurn, candidateGrowthHpCredit, candidateGrowthRewardCount,
                candidateDeathSaveUseCount),
            RouteQuality.Primary(currentCompleteVictory, currentStrategicHpDeficit,
                currentCombatEndedTurn, currentGrowthHpCredit, currentGrowthRewardCount,
                currentDeathSaveUseCount),
            RouteQualityProjection.Primary);

    internal static bool IsResourceTradeImprovement(
        int candidateHpDeficit,
        int candidatePotionCost,
        int currentHpDeficit,
        int currentPotionCost)
    {
        int candidateBurden = checked(candidateHpDeficit + candidatePotionCost);
        int currentBurden = checked(currentHpDeficit + currentPotionCost);
        return candidateBurden < currentBurden
            || candidateBurden == currentBurden && candidateHpDeficit < currentHpDeficit;
    }
}
