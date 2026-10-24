// Ported from CombatSolver src/Search/PowerCardValuation/PowerCardValuationContracts.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

[Flags]
internal enum PowerCommitmentFamily
{
    None = 0,
    DefenseEfficiency = 1 << 0,
    ShivEngine = 1 << 1,
    PoisonEngine = 1 << 2,
    HandEngine = 1 << 3,
    DamageEngine = 1 << 4,
    StrengthGrowth = 1 << 5,
    DexterityGrowth = 1 << 6,
    BlockTriggerEngine = 1 << 7,
    ExhaustEngine = 1 << 8,
    EnergyEngine = 1 << 9,
    OrbEngine = 1 << 10,
    FocusEngine = 1 << 11,
    StarEngine = 1 << 12,
    SummonEngine = 1 << 13,
    DoomEngine = 1 << 14,
    CardGenerationEngine = 1 << 15,
    RetainEngine = 1 << 16,
    CostReductionEngine = 1 << 17,
    AutoPlayEngine = 1 << 18,
    StatusAmplifier = 1 << 19,
    LifeInvestment = 1 << 20,
    CrossCombatGrowth = 1 << 21,
    RandomGeneration = 1 << 22,
}

internal enum PowerRoutePriority
{
    Low,
    Normal,
    Strong,
    Core,
    Dedicated,
}

internal readonly record struct PowerRouteAdmissionPolicy(
    PowerRoutePriority Priority = PowerRoutePriority.Low,
    int MinimumProjection = 1,
    bool AllowTriggerBackedProjectionFloor = false,
    bool PreferFreeActivation = false,
    bool RequireFreeOrSpareActivation = false,
    bool RequireImmediateDefenseGain = false,
    bool PreferDedicatedSearch = false,
    bool RequirePositiveProjection = false,
    bool NoInCombatCommitment = false);

internal static class PowerCardValuationMath
{
    public static int SaturatingAddNonNegative(int left, int right)
        => (int)Math.Clamp((long)left + right, 0L, int.MaxValue);
}
