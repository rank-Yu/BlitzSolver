namespace Sts2Solver.Ai.Combat;

internal enum CombatSearchTier { Low, Medium, High, ExtraHigh }

internal static class SearchPresets
{
    internal static SearchPlan For(CombatSearchTier tier, long memoryBytes) => tier switch
    {
        CombatSearchTier.Low => new(225, 150, 338, 90, 60_000, memoryBytes),
        CombatSearchTier.Medium => new(300, 200, 450, 120, 120_000, memoryBytes),
        CombatSearchTier.High => new(450, 300, 675, 180, 180_000, memoryBytes),
        CombatSearchTier.ExtraHigh => new(675, 450, 1013, 270, 300_000, memoryBytes),
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };
}
