// Ported from CombatSolver src/Search/StrategicHpRecoveryBound.Remaining.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members Sts2Solver reads.
namespace Sts2Solver.Ai.Combat.Scoring;

internal static partial class StrategicHpRecoveryBound
{
    internal static int RegenerationHealingUpperBound(long regen, int postCombatHeal)
    {
        // Stacking all remaining doses before the first tick is an upper bound on every
        // later ordering. Regen's native callback heals the amount, then decrements it.
        // Saturate before multiplication; arbitrary large amounts remain conservative.
        if (regen >= 65_536)
            return int.MaxValue;
        long amount = Math.Max(0, regen);
        return (int)Math.Min(int.MaxValue,
            amount * (amount + 1) / 2 + Math.Max(0, postCombatHeal));
    }
}
