// Ported from CombatSolver src/Search/StrategicHpRecoveryBound.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

internal static partial class StrategicHpRecoveryBound
{
    internal static int OptimisticRecoveredHp(
        int recoveredHp, int playerHp, int playerMaxHp, int futureHealPotential)
        => recoveredHp + Math.Min(
            Math.Max(0, playerMaxHp - playerHp), futureHealPotential);
}
