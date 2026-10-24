// Ported from CombatSolver src/Search/SolverWeights.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

/// <summary>
/// 生存仍优先，但允许用小额战损换取足够输出、易伤与更早击杀。
/// </summary>
internal static class SolverWeights
{
    public const double DeathPenalty = -1_000_000_000_000d;
    public const double VictoryBonus = 10_000_000_000d;
    public const double Hp = 30_000d;
    public const double EnemyHp = -10_000d;
    public const double RiskPenalty = -2_000d;
    public const double ActionPenalty = -1d;
    public const int VulnerableAttackWindowCap = 24;
    public const double VulnerableAttackMultiplierBeamValue = 5_000d;
    public const double OffTargetVulnerableAttackMultiplierBeamValue = 1_000d;
    public const int CurrentEnergyBeamCap = 6;
    public const double CurrentEnergyBeamValue = 60_000d;
    public const int ExactStatesPerProjectedShuffleOrder = 1;
    public const int OrderedPileVariantsPerTacticalState = 24;
    public const int PocketwatchParetoCandidatesPerCadence = 32;
    public const int PersistentBuffDeltaBeamCap = 32;
    public const double PersistentBuffDeltaBeamValue = 50_000d;
    public const int StandardPersistentBuffDeltaBeamCap = 4;
    public const double StandardPersistentBuffDeltaBeamValue = 150_000d;
    public const int LatentSetupBeamCap = 24;
    public const double LatentSetupBeamValue = 12_000d;
    public const int ReplayPotentialBeamCap = 64;
    public const double ReplayPotentialBeamValue = 10_000d;
    public const double LongTermResourceBeamValue = 25_000d;
    public const double LongTermResourceBeamCap = 25_000d;
    public const double AngerCopyBeamPenalty = -15_000d;
    public const int RetainedAttackGrowthBeamCap = 16;
    public const double RetainedAttackGrowthBeamValue = 20_000d;
    public const double FutureResourceBeamValue = 10_000d;
    public const double DelayedDamageBeamValue = 10_000d;
    public const double SandpitTurnBeamValue = 30_000d;
    public const int EnemyStrengthSuppressionBeamCap = 16;
    public const int EnemyWeakTurnsBeamCap = 16;
    public const int StandardEnemyStrengthSuppressionHorizon = 4;
    public const int BossEnemyStrengthSuppressionHorizon = 8;
    public const int StandardEnemyWeakExpectedHpSaved = 1;
    public const int BossEnemyWeakExpectedHpSaved = 2;
    public const double LiveDeckClutterPenalty = -8_000d;
    public const double OutstandingStolenResourcePenalty = -1_000_000d;
    public const int PotionMinimumHpSaved = 9;
    public const int DeathSavePremiumPercent = 900;
    public const int SetupValueHorizonTurns = 16;
    public const int BackgroundWorkSliceMilliseconds = 4;
    public const int BackgroundYieldCheckInterval = 16;
}
