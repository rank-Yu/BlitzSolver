// Ported from CombatSolver src/Search/StrategicHpRecoveryBound.Remaining.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

internal static partial class StrategicHpRecoveryBound
{
    internal static int RegenerationHealingUpperBound(long regen, int postCombatHeal)
    {
        if (regen >= 65_536)
            return int.MaxValue;
        long amount = Math.Max(0, regen);
        return (int)Math.Min(int.MaxValue,
            amount * (amount + 1) / 2 + Math.Max(0, postCombatHeal));
    }
}
