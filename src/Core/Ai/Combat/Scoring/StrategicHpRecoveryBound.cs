// Ported from CombatSolver src/Search/StrategicHpRecoveryBound.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace BlitzSolver.Ai.Combat.Scoring;

/// <summary>
/// A deliberately small closed set of roots whose future actions cannot restore player HP.
/// Unclassified cards, relics, powers, potions and gameplay extensions retain the full HP headroom.
/// The supported native enemy effects only add harmful status cards; revisit this proof if an enemy
/// starts granting a healing card, potion, relic or player power.
/// </summary>
internal static partial class StrategicHpRecoveryBound
{
    internal static int OptimisticRecoveredHp(
        int recoveredHp, int playerHp, int playerMaxHp, int futureHealPotential)
        => recoveredHp + Math.Min(
            Math.Max(0, playerMaxHp - playerHp), futureHealPotential);
}
