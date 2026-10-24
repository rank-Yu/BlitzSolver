// Ported from CombatSolver src/Search/PotionUsePolicy.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

internal static class PotionUsePolicy
{
    public const decimal AmbergrisMinimumHpSavedFraction = 0.40m;

    public static int RequiredHpSaved(int potionCount)
        => potionCount * SolverWeights.PotionMinimumHpSaved;

    public static int ExplicitUseCount(int potionCount, int automaticPotionCount)
        => potionCount - automaticPotionCount;

    public static int AdditionalRequiredUseStrategicHpCost(int strategicHpCost)
        => Math.Max(0, strategicHpCost - SolverWeights.PotionMinimumHpSaved);

    public static int ApplyReplacementCredit(
        int optionalPotionStrategicCost,
        int optionalPotionCount,
        int replacementHpCredit)
        => optionalPotionStrategicCost > 0 && optionalPotionCount > 0 && replacementHpCredit > 0
            ? Math.Max(1, optionalPotionStrategicCost - replacementHpCredit)
            : optionalPotionStrategicCost;

    public static int HpSaved(int potionFreeHpDeficit, int potionRouteHpDeficit)
        => Math.Max(0, potionFreeHpDeficit - potionRouteHpDeficit);

    public static int SmartRequiredHpSaved(
        int strategicHpCost,
        BossHpRelief bossHpRelief = BossHpRelief.None)
        => ActEndingBossPolicy.RawHpRequiredForPersistentValue(
            strategicHpCost,
            bossHpRelief);

    public static int AmbergrisRequiredHpSaved(int maximumHp)
        => (int)Math.Ceiling(maximumHp * AmbergrisMinimumHpSavedFraction);

    public static int EffectiveStrategicHpCost(
        int strategicHpCost,
        int ambergrisCount,
        int maximumHp)
        => strategicHpCost + ambergrisCount
            * (AmbergrisRequiredHpSaved(maximumHp) - SolverWeights.PotionMinimumHpSaved);

    public static bool MeetsAmbergrisRestriction(
        bool hasPotionFreeBaseline,
        int ambergrisCount,
        int strategicHpCost,
        int maximumHp,
        int potionFreePlayerHp,
        int potionRoutePlayerHp)
    {
        if (ambergrisCount == 0)
            return true;
        if (!hasPotionFreeBaseline)
            return false;
        int required = EffectiveStrategicHpCost(strategicHpCost, ambergrisCount, maximumHp);
        return Math.Max(0, potionRoutePlayerHp - potionFreePlayerHp) >= required;
    }

    public static bool IsEligible(
        SolverPotionPolicy policy,
        int explicitPotionCount,
        int strategicHpCost,
        bool potionFreeWon,
        int potionFreeHpDeficit,
        bool anyRouteWon,
        bool potionRouteWon,
        int potionRouteHpDeficit)
        => policy switch
        {
            SolverPotionPolicy.Disabled => explicitPotionCount == 0,
            SolverPotionPolicy.RequireAtLeastOne => explicitPotionCount > 0
                && (!anyRouteWon || potionRouteWon)
                && (explicitPotionCount == 1
                    || !potionFreeWon
                    || HpSaved(potionFreeHpDeficit, potionRouteHpDeficit)
                        >= AdditionalRequiredUseStrategicHpCost(strategicHpCost)),
            SolverPotionPolicy.Smart => explicitPotionCount == 0
                || potionRouteWon && !potionFreeWon
                || HpSaved(potionFreeHpDeficit, potionRouteHpDeficit)
                    >= SmartRequiredHpSaved(strategicHpCost),
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };
}

internal readonly record struct PotionFreePolicyBaseline(
    bool Won,
    int HpDeficit,
    int PlayerHp,
    int? CombatEndedTurn)
{
    public int DeathSaveUseCount { get; init; }
}
