// Ported from CombatSolver src/Search/PowerCardValuation/PowerCardValuationContracts.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

/// <summary>
/// 能力承诺的机制族。所有角色共用同一组族标签，具体到卡牌的归属由各卡池目录登记；
/// 公共搜索层按族、登记的能力集合与优先级做有界保路，不按具体卡牌编写路线。
/// </summary>
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

/// <summary>能力路线的保留优先级。只影响中间保路席位，不进入终局比较。</summary>
internal enum PowerRoutePriority
{
    Low,
    Normal,
    Strong,
    Core,
    Dedicated,
}

/// <summary>
/// 逐卡路线准入政策。字段沿用静默猎手第二批生产语义，新增角色只登记数据，不改公共判定顺序。
/// </summary>
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
