// Ported from CombatSolver src/Search/RouteQuality.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
using BlitzSolver.Search.Policy;

namespace BlitzSolver.Search.Quality;

internal readonly record struct RouteQuality(
    bool Won,
    bool Survives,
    int DeathSaveUseCount,
    int OutstandingStolenResource,
    int StrategicHpDeficit,
    int GrowthHpCredit,
    int GrowthRewardCount,
    int? CombatEndedTurn,
    int ProjectedBattleHpLost,
    int PotionStrategicCost,
    int ProjectedBattlePotionCount,
    int EnemyHp,
    double Score,
    TheftPolicy? TheftPolicy)
{
    public long RetentionHealthRisk { get; init; }

    public static RouteQuality Primary(
        bool won,
        int strategicHpDeficit,
        int? combatEndedTurn,
        int growthHpCredit = 0,
        int growthRewardCount = 0,
        int deathSaveUseCount = 0)
        => new(won, false, deathSaveUseCount, 0, strategicHpDeficit,
            growthHpCredit, growthRewardCount, combatEndedTurn, 0, 0, 0, 0, 0, null);
}
