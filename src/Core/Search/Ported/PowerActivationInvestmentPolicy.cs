// Ported from CombatSolver src/Search/PowerCardValuation/Commitments/PowerActivationInvestmentPolicy.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

internal static class PowerActivationInvestmentPolicy
{
    internal const int ValuePerEnergy = 8;

    internal static int EnergyInvestment(
        int spentEnergy,
        int totalFloor,
        int combatTurnOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spentEnergy);
        ArgumentOutOfRangeException.ThrowIfNegative(totalFloor);
        ArgumentOutOfRangeException.ThrowIfNegative(combatTurnOffset);
        int investment = checked(spentEnergy * ValuePerEnergy);
        if (combatTurnOffset > 1 || investment == 0)
            return investment;

        int numerator = totalFloor switch
        {
            <= 15 => 8,
            <= 30 => 5,
            _ => 3,
        };
        return (investment * numerator + 7) / 8;
    }
}
