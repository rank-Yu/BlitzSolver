namespace CombatSolver.FlashBeam;

/// <summary>
/// The evaluation values FlashBeam ranks by, computed from a <see cref="Engine.InCombat.Flash.FlashBattle"/>
/// by <see cref="FlashScorer"/>. Every member has the name and meaning of the
/// <see cref="SimulationSnapshot"/> member it mirrors (<c>CombatBeamSolver.Snapshot</c>), and the K4
/// check (docs/flash-search/admission-and-verification.md §3) requires each one to equal the snapshot
/// of the same state. <see cref="Score"/> is compared as an exact double.
/// </summary>
internal readonly record struct FlashScoreFeatures
{
    // Snapshot score and its terms.
    public double Score { get; init; }
    public bool PlayerDead { get; init; }
    public bool AllEnemiesDead { get; init; }
    public int PlayerHp { get; init; }
    public int PlayerMaxHp { get; init; }
    public int PlayerBlock { get; init; }
    public int CumulativePlayerHpLost { get; init; }
    public int RecoveredPlayerHp { get; init; }
    public int DeathSaveHpRestored { get; init; }
    public int DeathSaveUseCount { get; init; }
    public int ProjectedDeathSaveUseCount { get; init; }
    public int ProjectedPlayerHp { get; init; }
    public int DefensiveBlockValue { get; init; }
    public int LongTermResourceValue { get; init; }
    public int GrowthHpCredit { get; init; }
    public int AngerCopiesGenerated { get; init; }
    public int EnemyHp { get; init; }
    public int EnemyBlock { get; init; }
    public int RawEnemyHp { get; init; }
    public int MaxCurrentEnemyHp { get; init; }
    public int AliveEnemyCount { get; init; }
    public ulong AliveEnemyMask { get; init; }
    public int RevivingEnemyCount { get; init; }
    public int LiveDeckClutter { get; init; }
    public int LiveDeckSize { get; init; }
    public int OutstandingStolenResource { get; init; }
    public int RetainedAttackValue { get; init; }
    public uint? FocusTargetCombatId { get; init; }
    public int FocusTargetPressure { get; init; }
    public int FocusTargetRemainingHp { get; init; }
    public int FocusTargetCurrentThreat { get; init; }
    public int FocusTargetVulnerableTurns { get; init; }
    public uint? MostVulnerableTargetCombatId { get; init; }
    public int EnemyVulnerableTurns { get; init; }

    // BeamRankScore terms.
    public int Energy { get; init; }
    public int HandCount { get; init; }
    public int PersistentBuffValue { get; init; }
    public int LatentSetupValue { get; init; }
    public int FutureResourceValue { get; init; }
    public int ReplayPotentialValue { get; init; }
    public int DelayedDamageValue { get; init; }
    public int ReactiveDamageValue { get; init; }
    public int OffensiveProgressValue { get; init; }
    public int SandpitRemaining { get; init; }
    public int EnemyStrengthSuppression { get; init; }
    public int EnemyWeakTurns { get; init; }

    // Potion accounting (final ordering and Pareto dimensions).
    public int PotionUseCount { get; init; }
    public int PotionStrategicCost { get; init; }
    public int AutomaticPotionUseCount { get; init; }
    public int ExplicitPotionStrategicCost { get; init; }
    public int ExplicitPotionUseCount { get; init; }

    /// <summary>One line per member that differs, as <c>name old=… flash=…</c>.</summary>
    public static List<string> Diff(in FlashScoreFeatures old, in FlashScoreFeatures flash)
    {
        List<string> lines = [];
        void Check<T>(string name, T left, T right)
        {
            if (!EqualityComparer<T>.Default.Equals(left, right))
                lines.Add($"{name} old={left} flash={right}");
        }
        Check(nameof(Score), BitConverter.DoubleToInt64Bits(old.Score), BitConverter.DoubleToInt64Bits(flash.Score));
        if (lines.Count > 0)
            lines[0] = $"{nameof(Score)} old={old.Score:R} flash={flash.Score:R} delta={flash.Score - old.Score:R}";
        Check(nameof(PlayerDead), old.PlayerDead, flash.PlayerDead);
        Check(nameof(AllEnemiesDead), old.AllEnemiesDead, flash.AllEnemiesDead);
        Check(nameof(PlayerHp), old.PlayerHp, flash.PlayerHp);
        Check(nameof(PlayerMaxHp), old.PlayerMaxHp, flash.PlayerMaxHp);
        Check(nameof(PlayerBlock), old.PlayerBlock, flash.PlayerBlock);
        Check(nameof(CumulativePlayerHpLost), old.CumulativePlayerHpLost, flash.CumulativePlayerHpLost);
        Check(nameof(RecoveredPlayerHp), old.RecoveredPlayerHp, flash.RecoveredPlayerHp);
        Check(nameof(DeathSaveHpRestored), old.DeathSaveHpRestored, flash.DeathSaveHpRestored);
        Check(nameof(DeathSaveUseCount), old.DeathSaveUseCount, flash.DeathSaveUseCount);
        Check(nameof(ProjectedDeathSaveUseCount), old.ProjectedDeathSaveUseCount, flash.ProjectedDeathSaveUseCount);
        Check(nameof(ProjectedPlayerHp), old.ProjectedPlayerHp, flash.ProjectedPlayerHp);
        Check(nameof(DefensiveBlockValue), old.DefensiveBlockValue, flash.DefensiveBlockValue);
        Check(nameof(LongTermResourceValue), old.LongTermResourceValue, flash.LongTermResourceValue);
        Check(nameof(GrowthHpCredit), old.GrowthHpCredit, flash.GrowthHpCredit);
        Check(nameof(AngerCopiesGenerated), old.AngerCopiesGenerated, flash.AngerCopiesGenerated);
        Check(nameof(EnemyHp), old.EnemyHp, flash.EnemyHp);
        Check(nameof(EnemyBlock), old.EnemyBlock, flash.EnemyBlock);
        Check(nameof(RawEnemyHp), old.RawEnemyHp, flash.RawEnemyHp);
        Check(nameof(MaxCurrentEnemyHp), old.MaxCurrentEnemyHp, flash.MaxCurrentEnemyHp);
        Check(nameof(AliveEnemyCount), old.AliveEnemyCount, flash.AliveEnemyCount);
        Check(nameof(AliveEnemyMask), old.AliveEnemyMask, flash.AliveEnemyMask);
        Check(nameof(RevivingEnemyCount), old.RevivingEnemyCount, flash.RevivingEnemyCount);
        Check(nameof(LiveDeckClutter), old.LiveDeckClutter, flash.LiveDeckClutter);
        Check(nameof(LiveDeckSize), old.LiveDeckSize, flash.LiveDeckSize);
        Check(nameof(OutstandingStolenResource), old.OutstandingStolenResource, flash.OutstandingStolenResource);
        Check(nameof(RetainedAttackValue), old.RetainedAttackValue, flash.RetainedAttackValue);
        Check(nameof(FocusTargetCombatId), old.FocusTargetCombatId, flash.FocusTargetCombatId);
        Check(nameof(FocusTargetPressure), old.FocusTargetPressure, flash.FocusTargetPressure);
        Check(nameof(FocusTargetRemainingHp), old.FocusTargetRemainingHp, flash.FocusTargetRemainingHp);
        Check(nameof(FocusTargetCurrentThreat), old.FocusTargetCurrentThreat, flash.FocusTargetCurrentThreat);
        Check(nameof(FocusTargetVulnerableTurns), old.FocusTargetVulnerableTurns, flash.FocusTargetVulnerableTurns);
        Check(nameof(MostVulnerableTargetCombatId), old.MostVulnerableTargetCombatId, flash.MostVulnerableTargetCombatId);
        Check(nameof(EnemyVulnerableTurns), old.EnemyVulnerableTurns, flash.EnemyVulnerableTurns);
        Check(nameof(Energy), old.Energy, flash.Energy);
        Check(nameof(HandCount), old.HandCount, flash.HandCount);
        Check(nameof(PersistentBuffValue), old.PersistentBuffValue, flash.PersistentBuffValue);
        Check(nameof(LatentSetupValue), old.LatentSetupValue, flash.LatentSetupValue);
        Check(nameof(FutureResourceValue), old.FutureResourceValue, flash.FutureResourceValue);
        Check(nameof(ReplayPotentialValue), old.ReplayPotentialValue, flash.ReplayPotentialValue);
        Check(nameof(DelayedDamageValue), old.DelayedDamageValue, flash.DelayedDamageValue);
        Check(nameof(ReactiveDamageValue), old.ReactiveDamageValue, flash.ReactiveDamageValue);
        Check(nameof(OffensiveProgressValue), old.OffensiveProgressValue, flash.OffensiveProgressValue);
        Check(nameof(SandpitRemaining), old.SandpitRemaining, flash.SandpitRemaining);
        Check(nameof(EnemyStrengthSuppression), old.EnemyStrengthSuppression, flash.EnemyStrengthSuppression);
        Check(nameof(EnemyWeakTurns), old.EnemyWeakTurns, flash.EnemyWeakTurns);
        Check(nameof(PotionUseCount), old.PotionUseCount, flash.PotionUseCount);
        Check(nameof(PotionStrategicCost), old.PotionStrategicCost, flash.PotionStrategicCost);
        Check(nameof(AutomaticPotionUseCount), old.AutomaticPotionUseCount, flash.AutomaticPotionUseCount);
        Check(nameof(ExplicitPotionStrategicCost), old.ExplicitPotionStrategicCost, flash.ExplicitPotionStrategicCost);
        Check(nameof(ExplicitPotionUseCount), old.ExplicitPotionUseCount, flash.ExplicitPotionUseCount);
        return lines;
    }
}
