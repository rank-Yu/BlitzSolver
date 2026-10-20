namespace CombatSolver.FlashBeam;

/// <summary>
/// The evaluation values FlashBeam ranks by, computed from a <see cref="Engine.InCombat.Flash.FlashBattle"/>
/// by <see cref="FlashScorer"/>. Every member has the name and meaning of the
/// <see cref="SimulationSnapshot"/> member it mirrors (<c>the existing search's Snapshot</c>), and the K4
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

    /// <summary><c>SearchNode.Turn</c> (the snapshot's combat turn, equal to the Flash player turn).</summary>
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
    /// Flash-side projected-shuffle key. The existing key hashes the existing engine's per-card state
    /// fingerprint; Flash hashes the complete <see cref="FlashCard"/> byte image instead. It partitions
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
    /// effective HP, containment) in <see cref="FlashBattle.Enemies"/> order. K4 compares both halves.
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
    /// Flash-side unordered pile key: the order-invariant multiset hash over hand, draw, discard and
    /// exhaust. The existing key sums the existing per-card state fingerprints; the numeric value is
    /// therefore not comparable, but the equivalence class (same multiset of equal card states) is.
    /// </summary>
    public ulong UnorderedPileKeyFirst { get; init; }
    public ulong UnorderedPileKeySecond { get; init; }

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
        Check(nameof(OstyHp), old.OstyHp, flash.OstyHp);
        Check(nameof(OstyMaxHp), old.OstyMaxHp, flash.OstyMaxHp);
        Check(nameof(CumulativePlayerHpLost), old.CumulativePlayerHpLost, flash.CumulativePlayerHpLost);
        Check(nameof(RecoveredPlayerHp), old.RecoveredPlayerHp, flash.RecoveredPlayerHp);
        Check(nameof(DeathSaveHpRestored), old.DeathSaveHpRestored, flash.DeathSaveHpRestored);
        Check(nameof(DeathSaveUseCount), old.DeathSaveUseCount, flash.DeathSaveUseCount);
        Check(nameof(ProjectedDeathSaveUseCount), old.ProjectedDeathSaveUseCount, flash.ProjectedDeathSaveUseCount);
        Check(nameof(ProjectedPlayerHp), old.ProjectedPlayerHp, flash.ProjectedPlayerHp);
        Check(nameof(DefensiveBlockValue), old.DefensiveBlockValue, flash.DefensiveBlockValue);
        Check(nameof(LongTermResourceValue), old.LongTermResourceValue, flash.LongTermResourceValue);
        Check(nameof(GrowthHpCredit), old.GrowthHpCredit, flash.GrowthHpCredit);
        Check(nameof(GrowthRewardCount), old.GrowthRewardCount, flash.GrowthRewardCount);
        foreach (GrowthSource source in Enum.GetValues<GrowthSource>())
        {
            Check($"{nameof(GrowthRewards)}.{source}",
                old.GrowthRewards.Get(source), flash.GrowthRewards.Get(source));
        }
        Check(nameof(StrategyGoalHpCredit), old.StrategyGoalHpCredit, flash.StrategyGoalHpCredit);
        Check(nameof(StrategicHpCredit), old.StrategicHpCredit, flash.StrategicHpCredit);
        Check(nameof(StrategyGoalCount), old.StrategyGoalCount, flash.StrategyGoalCount);
        Check(nameof(RelicSatisfiedMask), old.RelicSatisfiedMask, flash.RelicSatisfiedMask);
        Check(nameof(RelicTargetMask), old.RelicTargetMask, flash.RelicTargetMask);
        Check(nameof(RelicCounterValues), old.RelicCounterValues, flash.RelicCounterValues);
        Check(nameof(RelicTargetMinimums), old.RelicTargetMinimums, flash.RelicTargetMinimums);
        Check(nameof(RelicTargetMaximums), old.RelicTargetMaximums, flash.RelicTargetMaximums);
        Check(nameof(RelicHpCredit), old.RelicHpCredit, flash.RelicHpCredit);
        Check(nameof(RelicDistance), old.RelicDistance, flash.RelicDistance);
        Check(nameof(RelicSatisfiedPriority), old.RelicSatisfiedPriority, flash.RelicSatisfiedPriority);
        Check(nameof(RelicHealingHpCredit), old.RelicHealingHpCredit, flash.RelicHealingHpCredit);
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
        Check(nameof(MinimumOutstandingStolenResource), old.MinimumOutstandingStolenResource,
            flash.MinimumOutstandingStolenResource);
        Check(nameof(RetainedAttackValue), old.RetainedAttackValue, flash.RetainedAttackValue);
        Check(nameof(FocusTargetCombatId), old.FocusTargetCombatId, flash.FocusTargetCombatId);
        Check(nameof(FocusTargetPressure), old.FocusTargetPressure, flash.FocusTargetPressure);
        Check(nameof(FocusTargetRemainingHp), old.FocusTargetRemainingHp, flash.FocusTargetRemainingHp);
        Check(nameof(FocusTargetCurrentThreat), old.FocusTargetCurrentThreat, flash.FocusTargetCurrentThreat);
        Check(nameof(FocusTargetVulnerableTurns), old.FocusTargetVulnerableTurns, flash.FocusTargetVulnerableTurns);
        Check(nameof(MostVulnerableTargetCombatId), old.MostVulnerableTargetCombatId, flash.MostVulnerableTargetCombatId);
        Check(nameof(EnemyVulnerableTurns), old.EnemyVulnerableTurns, flash.EnemyVulnerableTurns);
        Check(nameof(Energy), old.Energy, flash.Energy);
        Check(nameof(Stars), old.Stars, flash.Stars);
        Check(nameof(HandCount), old.HandCount, flash.HandCount);
        Check(nameof(Turn), old.Turn, flash.Turn);
        Check(nameof(ReachableHandValue), old.ReachableHandValue, flash.ReachableHandValue);
        Check(nameof(ZeroCostPlayableCount), old.ZeroCostPlayableCount, flash.ZeroCostPlayableCount);
        Check(nameof(PlayableAttackCount), old.PlayableAttackCount, flash.PlayableAttackCount);
        Check(nameof(PersistentBuffValue), old.PersistentBuffValue, flash.PersistentBuffValue);
        Check(nameof(StrategicRetentionValue), old.StrategicRetentionValue, flash.StrategicRetentionValue);
        Check(nameof(StrategicDamagePotential), old.StrategicDamagePotential, flash.StrategicDamagePotential);
        Check(nameof(StrategicPreventionPotential), old.StrategicPreventionPotential, flash.StrategicPreventionPotential);
        Check(nameof(StrategicResourcePotential), old.StrategicResourcePotential, flash.StrategicResourcePotential);
        Check(nameof(StrategicCardAccessPotential), old.StrategicCardAccessPotential, flash.StrategicCardAccessPotential);
        Check(nameof(StrategicScalingPotential), old.StrategicScalingPotential, flash.StrategicScalingPotential);
        Check(nameof(HasRisk), old.HasRisk, flash.HasRisk);
        Check(nameof(Act3BossInteractions), old.Act3BossInteractions, flash.Act3BossInteractions);
        Check(nameof(FirstAttackDamage), old.FirstAttackDamage, flash.FirstAttackDamage);
        Check(nameof(EtherealDrawTriggers), old.EtherealDrawTriggers, flash.EtherealDrawTriggers);
        Check(nameof(PagestormBonusDrawCapacity), old.PagestormBonusDrawCapacity,
            flash.PagestormBonusDrawCapacity);
        Check(nameof(HighEnergyPlays), old.HighEnergyPlays, flash.HighEnergyPlays);
        Check(nameof(DemesneEnergyGain), old.DemesneEnergyGain, flash.DemesneEnergyGain);
        Check(nameof(DemesneDrawGain), old.DemesneDrawGain, flash.DemesneDrawGain);
        Check(nameof(PersistentSetupTraits), old.PersistentSetupTraits, flash.PersistentSetupTraits);
        Check(nameof(StrategicSetupTraits), old.StrategicSetupTraits, flash.StrategicSetupTraits);
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
        Check(nameof(ProjectedShuffleOrderValue), old.ProjectedShuffleOrderValue,
            flash.ProjectedShuffleOrderValue);
        Check(nameof(CanTriggerArtOfWarNextTurn), old.CanTriggerArtOfWarNextTurn,
            flash.CanTriggerArtOfWarNextTurn);
        Check(nameof(EnemyCombatDistributionFirst), old.EnemyCombatDistributionFirst,
            flash.EnemyCombatDistributionFirst);
        Check(nameof(EnemyCombatDistributionSecond), old.EnemyCombatDistributionSecond,
            flash.EnemyCombatDistributionSecond);
        Check(nameof(EnemyControlDistributionFirst), old.EnemyControlDistributionFirst,
            flash.EnemyControlDistributionFirst);
        Check(nameof(EnemyControlDistributionSecond), old.EnemyControlDistributionSecond,
            flash.EnemyControlDistributionSecond);
        return lines;
    }
}
