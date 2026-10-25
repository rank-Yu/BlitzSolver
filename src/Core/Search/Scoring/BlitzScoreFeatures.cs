using BlitzSolver.Engine;
using BlitzSolver.Search;

namespace BlitzSolver.Search.Scoring;

/// <summary>
/// The evaluation values BlitzBeam ranks by, computed from a <see cref="BlitzBattle"/>
/// by <see cref="BlitzScorer"/>. Every member has the name and meaning of the
/// <see cref="SimulationSnapshot"/> member it mirrors (<c>the existing search's Snapshot</c>), and the K4
/// check (docs/flash-search/admission-and-verification.md §3) requires each one to equal the snapshot
/// of the same state. <see cref="Score"/> is compared as an exact double.
/// </summary>
internal readonly record struct BlitzScoreFeatures
{
    // Snapshot score and its terms.
    public double Score { get; init; }
    public bool PlayerDead { get; init; }
    public bool AllEnemiesDead { get; init; }
    public int PlayerHp { get; init; }
    public int PlayerMaxHp { get; init; }
    public int PlayerBlock { get; init; }
    public int OstyHp { get; init; }
    public int OstyMaxHp { get; init; }
    public int CumulativePlayerHpLost { get; init; }
    public int RecoveredPlayerHp { get; init; }
    public int DeathSaveHpRestored { get; init; }
    public int DeathSaveUseCount { get; init; }
    public int ProjectedDeathSaveUseCount { get; init; }
    public int ProjectedPlayerHp { get; init; }
    public int DefensiveBlockValue { get; init; }
    public int LongTermResourceValue { get; init; }
    public int GrowthHpCredit { get; init; }

    /// <summary><c>Snapshot.GrowthRewards</c>: the per-source realized growth rewards.</summary>
    public GrowthValues GrowthRewards { get; init; }

    /// <summary><c>Snapshot.GrowthRewards.Total</c>.</summary>
    public int GrowthRewardCount { get; init; }

    /// <summary><c>Snapshot.StrategyGoalHpCredit</c> (growth + relic HP credit).</summary>
    public int StrategyGoalHpCredit { get; init; }

    /// <summary><c>Snapshot.StrategicHpCredit</c> (strategy HP credit + Meat on the Bone heal).</summary>
    public int StrategicHpCredit { get; init; }

    /// <summary><c>Snapshot.StrategyGoalCount</c> (growth total + satisfied relic priority).</summary>
    public int StrategyGoalCount { get; init; }
    public ulong RelicSatisfiedMask { get; init; }
    public ulong RelicTargetMask { get; init; }
    public ulong RelicCounterValues { get; init; }
    public ulong RelicTargetMinimums { get; init; }
    public ulong RelicTargetMaximums { get; init; }
    public int RelicHpCredit { get; init; }
    public int RelicDistance { get; init; }
    public int RelicSatisfiedPriority { get; init; }
    public int RelicHealingHpCredit { get; init; }
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

    /// <summary>
    /// <c>SimulatedCombatState.MinimumOutstandingStolenResource</c>: the part of the outstanding loot
    /// that is already irrecoverable. The existing engine reads it only for the primary incumbent's
    /// loot bucket; K4 compares it on every matched state.
    /// </summary>
    public int MinimumOutstandingStolenResource { get; init; }
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
    public int Stars { get; init; }
    public int HandCount { get; init; }

    /// <summary><c>SearchNode.Turn</c> (the snapshot's combat turn, equal to the Blitz player turn).</summary>
    public int Turn { get; init; }
    public int ReachableHandValue { get; init; }
    public int ZeroCostPlayableCount { get; init; }

    /// <summary><c>Snapshot.PlayableAttackCount</c>: playable attack cards in the current hand.</summary>
    public int PlayableAttackCount { get; init; }
    public int PersistentBuffValue { get; init; }
    public int StrategicRetentionValue { get; init; }

    /// <summary><c>Snapshot.StrategicEffects.DamagePotential</c>; novelty facts read the five components.</summary>
    public int StrategicDamagePotential { get; init; }

    /// <summary><c>Snapshot.StrategicEffects.PreventionPotential</c>.</summary>
    public int StrategicPreventionPotential { get; init; }

    /// <summary><c>Snapshot.StrategicEffects.ResourcePotential</c>.</summary>
    public int StrategicResourcePotential { get; init; }

    /// <summary><c>Snapshot.StrategicEffects.CardAccessPotential</c>.</summary>
    public int StrategicCardAccessPotential { get; init; }

    /// <summary><c>Snapshot.StrategicEffects.ScalingPotential</c>.</summary>
    public int StrategicScalingPotential { get; init; }

    /// <summary><c>Snapshot.HasRisk</c> (<c>CombatPredictionSimulator.HasRisk</c>).</summary>
    public bool HasRisk { get; init; }

    /// <summary>
    /// <c>StrategicEffectContext.Act3BossInteractions</c>: the frozen three-act boss policy reached a
    /// contributing strategic power. Diagnostic only; its effect is already in the score terms.
    /// </summary>
    public bool Act3BossInteractions { get; init; }

    /// <summary><c>StrategicEffectContext.FirstAttackDamage</c>, read by Lethality in the boss branch.</summary>
    public int FirstAttackDamage { get; init; }

    /// <summary><c>StrategicEffectContext.EtherealDrawTriggers</c> (Pagestorm).</summary>
    public int EtherealDrawTriggers { get; init; }

    /// <summary><c>StrategicEffectContext.PagestormBonusDrawCapacity</c>.</summary>
    public int PagestormBonusDrawCapacity { get; init; }

    /// <summary><c>StrategicEffectContext.HighEnergyPlays</c> (Danse Macabre).</summary>
    public int HighEnergyPlays { get; init; }

    /// <summary><c>StrategicEffectContext.DemesneEnergyGain</c>.</summary>
    public int DemesneEnergyGain { get; init; }

    /// <summary><c>StrategicEffectContext.DemesneDrawGain</c>.</summary>
    public int DemesneDrawGain { get; init; }
    public PersistentSetupTraits PersistentSetupTraits { get; init; }
    public PersistentSetupTraits StrategicSetupTraits { get; init; }
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

    /// <summary>
    /// <c>Snapshot.ProjectedShuffleOrderValue</c>: the value of the live pile after the stable shuffle
    /// projection (base card value weighted by the projected position). Retention-only; K4 compares it
    /// on every projected state.
    /// </summary>
    public int ProjectedShuffleOrderValue { get; init; }

    /// <summary>
    /// Blitz-side projected-shuffle key. The existing key hashes the existing engine's per-card state
    /// fingerprint; Blitz hashes the complete <see cref="BlitzCard"/> byte image instead. It partitions
    /// the exact projected card sequence the same way (equal sequence of equal card states), which is
    /// what the ordered-pile cohorts consume; the numeric hash is not comparable across engines.
    /// </summary>
    public ulong ProjectedShuffleOrderKeyFirst { get; init; }
    public ulong ProjectedShuffleOrderKeySecond { get; init; }

    /// <summary><c>SimulatedCombatState.CanTriggerArtOfWarNextTurn</c>: an unmelted Art of War relic
    /// whose current-turn attack flag is still zero.</summary>
    public bool CanTriggerArtOfWarNextTurn { get; init; }

    /// <summary>
    /// <c>Snapshot.EnemyCombatDistributionKey</c>: per known enemy (CombatId, current HP, block,
    /// effective HP, containment) in <see cref="BlitzBattle.Enemies"/> order. K4 compares both halves.
    /// </summary>
    public ulong EnemyCombatDistributionFirst { get; init; }
    public ulong EnemyCombatDistributionSecond { get; init; }

    /// <summary>
    /// <c>Snapshot.EnemyControlDistributionKey</c>: per living, contained enemy (CombatId, strength
    /// suppression, weak turns, vulnerable turns). K4 compares both halves.
    /// </summary>
    public ulong EnemyControlDistributionFirst { get; init; }
    public ulong EnemyControlDistributionSecond { get; init; }

    /// <summary>
    /// Blitz-side unordered pile key: the order-invariant multiset hash over hand, draw, discard and
    /// exhaust. The existing key sums the existing per-card state fingerprints; the numeric value is
    /// therefore not comparable, but the equivalence class (same multiset of equal card states) is.
    /// </summary>
    public ulong UnorderedPileKeyFirst { get; init; }
    public ulong UnorderedPileKeySecond { get; init; }

    /// <summary>One line per member that differs, as <c>name old=… blitz=…</c>.</summary>
    public static List<string> Diff(in BlitzScoreFeatures old, in BlitzScoreFeatures blitz)
    {
        List<string> lines = [];
        void Check<T>(string name, T left, T right)
        {
            if (!EqualityComparer<T>.Default.Equals(left, right))
                lines.Add($"{name} old={left} blitz={right}");
        }
        Check(nameof(Score), BitConverter.DoubleToInt64Bits(old.Score), BitConverter.DoubleToInt64Bits(blitz.Score));
        if (lines.Count > 0)
            lines[0] = $"{nameof(Score)} old={old.Score:R} blitz={blitz.Score:R} delta={blitz.Score - old.Score:R}";
        Check(nameof(PlayerDead), old.PlayerDead, blitz.PlayerDead);
        Check(nameof(AllEnemiesDead), old.AllEnemiesDead, blitz.AllEnemiesDead);
        Check(nameof(PlayerHp), old.PlayerHp, blitz.PlayerHp);
        Check(nameof(PlayerMaxHp), old.PlayerMaxHp, blitz.PlayerMaxHp);
        Check(nameof(PlayerBlock), old.PlayerBlock, blitz.PlayerBlock);
        Check(nameof(OstyHp), old.OstyHp, blitz.OstyHp);
        Check(nameof(OstyMaxHp), old.OstyMaxHp, blitz.OstyMaxHp);
        Check(nameof(CumulativePlayerHpLost), old.CumulativePlayerHpLost, blitz.CumulativePlayerHpLost);
        Check(nameof(RecoveredPlayerHp), old.RecoveredPlayerHp, blitz.RecoveredPlayerHp);
        Check(nameof(DeathSaveHpRestored), old.DeathSaveHpRestored, blitz.DeathSaveHpRestored);
        Check(nameof(DeathSaveUseCount), old.DeathSaveUseCount, blitz.DeathSaveUseCount);
        Check(nameof(ProjectedDeathSaveUseCount), old.ProjectedDeathSaveUseCount, blitz.ProjectedDeathSaveUseCount);
        Check(nameof(ProjectedPlayerHp), old.ProjectedPlayerHp, blitz.ProjectedPlayerHp);
        Check(nameof(DefensiveBlockValue), old.DefensiveBlockValue, blitz.DefensiveBlockValue);
        Check(nameof(LongTermResourceValue), old.LongTermResourceValue, blitz.LongTermResourceValue);
        Check(nameof(GrowthHpCredit), old.GrowthHpCredit, blitz.GrowthHpCredit);
        Check(nameof(GrowthRewardCount), old.GrowthRewardCount, blitz.GrowthRewardCount);
        foreach (GrowthSource source in Enum.GetValues<GrowthSource>())
        {
            Check($"{nameof(GrowthRewards)}.{source}",
                old.GrowthRewards.Get(source), blitz.GrowthRewards.Get(source));
        }
        Check(nameof(StrategyGoalHpCredit), old.StrategyGoalHpCredit, blitz.StrategyGoalHpCredit);
        Check(nameof(StrategicHpCredit), old.StrategicHpCredit, blitz.StrategicHpCredit);
        Check(nameof(StrategyGoalCount), old.StrategyGoalCount, blitz.StrategyGoalCount);
        Check(nameof(RelicSatisfiedMask), old.RelicSatisfiedMask, blitz.RelicSatisfiedMask);
        Check(nameof(RelicTargetMask), old.RelicTargetMask, blitz.RelicTargetMask);
        Check(nameof(RelicCounterValues), old.RelicCounterValues, blitz.RelicCounterValues);
        Check(nameof(RelicTargetMinimums), old.RelicTargetMinimums, blitz.RelicTargetMinimums);
        Check(nameof(RelicTargetMaximums), old.RelicTargetMaximums, blitz.RelicTargetMaximums);
        Check(nameof(RelicHpCredit), old.RelicHpCredit, blitz.RelicHpCredit);
        Check(nameof(RelicDistance), old.RelicDistance, blitz.RelicDistance);
        Check(nameof(RelicSatisfiedPriority), old.RelicSatisfiedPriority, blitz.RelicSatisfiedPriority);
        Check(nameof(RelicHealingHpCredit), old.RelicHealingHpCredit, blitz.RelicHealingHpCredit);
        Check(nameof(AngerCopiesGenerated), old.AngerCopiesGenerated, blitz.AngerCopiesGenerated);
        Check(nameof(EnemyHp), old.EnemyHp, blitz.EnemyHp);
        Check(nameof(EnemyBlock), old.EnemyBlock, blitz.EnemyBlock);
        Check(nameof(RawEnemyHp), old.RawEnemyHp, blitz.RawEnemyHp);
        Check(nameof(MaxCurrentEnemyHp), old.MaxCurrentEnemyHp, blitz.MaxCurrentEnemyHp);
        Check(nameof(AliveEnemyCount), old.AliveEnemyCount, blitz.AliveEnemyCount);
        Check(nameof(AliveEnemyMask), old.AliveEnemyMask, blitz.AliveEnemyMask);
        Check(nameof(RevivingEnemyCount), old.RevivingEnemyCount, blitz.RevivingEnemyCount);
        Check(nameof(LiveDeckClutter), old.LiveDeckClutter, blitz.LiveDeckClutter);
        Check(nameof(LiveDeckSize), old.LiveDeckSize, blitz.LiveDeckSize);
        Check(nameof(OutstandingStolenResource), old.OutstandingStolenResource, blitz.OutstandingStolenResource);
        Check(nameof(MinimumOutstandingStolenResource), old.MinimumOutstandingStolenResource,
            blitz.MinimumOutstandingStolenResource);
        Check(nameof(RetainedAttackValue), old.RetainedAttackValue, blitz.RetainedAttackValue);
        Check(nameof(FocusTargetCombatId), old.FocusTargetCombatId, blitz.FocusTargetCombatId);
        Check(nameof(FocusTargetPressure), old.FocusTargetPressure, blitz.FocusTargetPressure);
        Check(nameof(FocusTargetRemainingHp), old.FocusTargetRemainingHp, blitz.FocusTargetRemainingHp);
        Check(nameof(FocusTargetCurrentThreat), old.FocusTargetCurrentThreat, blitz.FocusTargetCurrentThreat);
        Check(nameof(FocusTargetVulnerableTurns), old.FocusTargetVulnerableTurns, blitz.FocusTargetVulnerableTurns);
        Check(nameof(MostVulnerableTargetCombatId), old.MostVulnerableTargetCombatId, blitz.MostVulnerableTargetCombatId);
        Check(nameof(EnemyVulnerableTurns), old.EnemyVulnerableTurns, blitz.EnemyVulnerableTurns);
        Check(nameof(Energy), old.Energy, blitz.Energy);
        Check(nameof(Stars), old.Stars, blitz.Stars);
        Check(nameof(HandCount), old.HandCount, blitz.HandCount);
        Check(nameof(Turn), old.Turn, blitz.Turn);
        Check(nameof(ReachableHandValue), old.ReachableHandValue, blitz.ReachableHandValue);
        Check(nameof(ZeroCostPlayableCount), old.ZeroCostPlayableCount, blitz.ZeroCostPlayableCount);
        Check(nameof(PlayableAttackCount), old.PlayableAttackCount, blitz.PlayableAttackCount);
        Check(nameof(PersistentBuffValue), old.PersistentBuffValue, blitz.PersistentBuffValue);
        Check(nameof(StrategicRetentionValue), old.StrategicRetentionValue, blitz.StrategicRetentionValue);
        Check(nameof(StrategicDamagePotential), old.StrategicDamagePotential, blitz.StrategicDamagePotential);
        Check(nameof(StrategicPreventionPotential), old.StrategicPreventionPotential, blitz.StrategicPreventionPotential);
        Check(nameof(StrategicResourcePotential), old.StrategicResourcePotential, blitz.StrategicResourcePotential);
        Check(nameof(StrategicCardAccessPotential), old.StrategicCardAccessPotential, blitz.StrategicCardAccessPotential);
        Check(nameof(StrategicScalingPotential), old.StrategicScalingPotential, blitz.StrategicScalingPotential);
        Check(nameof(HasRisk), old.HasRisk, blitz.HasRisk);
        Check(nameof(Act3BossInteractions), old.Act3BossInteractions, blitz.Act3BossInteractions);
        Check(nameof(FirstAttackDamage), old.FirstAttackDamage, blitz.FirstAttackDamage);
        Check(nameof(EtherealDrawTriggers), old.EtherealDrawTriggers, blitz.EtherealDrawTriggers);
        Check(nameof(PagestormBonusDrawCapacity), old.PagestormBonusDrawCapacity,
            blitz.PagestormBonusDrawCapacity);
        Check(nameof(HighEnergyPlays), old.HighEnergyPlays, blitz.HighEnergyPlays);
        Check(nameof(DemesneEnergyGain), old.DemesneEnergyGain, blitz.DemesneEnergyGain);
        Check(nameof(DemesneDrawGain), old.DemesneDrawGain, blitz.DemesneDrawGain);
        Check(nameof(PersistentSetupTraits), old.PersistentSetupTraits, blitz.PersistentSetupTraits);
        Check(nameof(StrategicSetupTraits), old.StrategicSetupTraits, blitz.StrategicSetupTraits);
        Check(nameof(LatentSetupValue), old.LatentSetupValue, blitz.LatentSetupValue);
        Check(nameof(FutureResourceValue), old.FutureResourceValue, blitz.FutureResourceValue);
        Check(nameof(ReplayPotentialValue), old.ReplayPotentialValue, blitz.ReplayPotentialValue);
        Check(nameof(DelayedDamageValue), old.DelayedDamageValue, blitz.DelayedDamageValue);
        Check(nameof(ReactiveDamageValue), old.ReactiveDamageValue, blitz.ReactiveDamageValue);
        Check(nameof(OffensiveProgressValue), old.OffensiveProgressValue, blitz.OffensiveProgressValue);
        Check(nameof(SandpitRemaining), old.SandpitRemaining, blitz.SandpitRemaining);
        Check(nameof(EnemyStrengthSuppression), old.EnemyStrengthSuppression, blitz.EnemyStrengthSuppression);
        Check(nameof(EnemyWeakTurns), old.EnemyWeakTurns, blitz.EnemyWeakTurns);
        Check(nameof(PotionUseCount), old.PotionUseCount, blitz.PotionUseCount);
        Check(nameof(PotionStrategicCost), old.PotionStrategicCost, blitz.PotionStrategicCost);
        Check(nameof(AutomaticPotionUseCount), old.AutomaticPotionUseCount, blitz.AutomaticPotionUseCount);
        Check(nameof(ExplicitPotionStrategicCost), old.ExplicitPotionStrategicCost, blitz.ExplicitPotionStrategicCost);
        Check(nameof(ExplicitPotionUseCount), old.ExplicitPotionUseCount, blitz.ExplicitPotionUseCount);
        Check(nameof(ProjectedShuffleOrderValue), old.ProjectedShuffleOrderValue,
            blitz.ProjectedShuffleOrderValue);
        Check(nameof(CanTriggerArtOfWarNextTurn), old.CanTriggerArtOfWarNextTurn,
            blitz.CanTriggerArtOfWarNextTurn);
        Check(nameof(EnemyCombatDistributionFirst), old.EnemyCombatDistributionFirst,
            blitz.EnemyCombatDistributionFirst);
        Check(nameof(EnemyCombatDistributionSecond), old.EnemyCombatDistributionSecond,
            blitz.EnemyCombatDistributionSecond);
        Check(nameof(EnemyControlDistributionFirst), old.EnemyControlDistributionFirst,
            blitz.EnemyControlDistributionFirst);
        Check(nameof(EnemyControlDistributionSecond), old.EnemyControlDistributionSecond,
            blitz.EnemyControlDistributionSecond);
        return lines;
    }
}
