using System.Runtime.CompilerServices;
using CombatSolver.Engine.InCombat.Flash;

namespace CombatSolver.FlashBeam;

/// <summary>
/// Ports the existing search's state evaluation to <see cref="FlashBattle"/>. Every computation follows
/// <c>the existing search's Snapshot</c> (the existing search's StateEvaluation.cs) and
/// <c>BeamRetentionPolicy.BeamRankScore</c> (the existing search's BeamRetentionPolicy.Ranking.cs) exactly:
/// the same terms, the same order of operations, the same checked/rounding/clamping.
/// </summary>
/// <remarks>
/// The per-content values (card values, latent setup values and traits, power strategic effects) come from
/// <see cref="FlashScoreRules"/>, which <see cref="FlashScoreBridge"/> compiles from the existing engine's
/// models at the root. This type only reads the battle, the rule table and that compile output.
/// It allocates nothing on the hot path.
/// </remarks>
[System.Runtime.CompilerServices.SkipLocalsInit]
internal sealed class FlashScorer
{
    private readonly FlashScoreRules _score;

    public FlashScorer(FlashScoreRules score) => _score = score;

    private FlashRules Rules => _score.Rules;

    /// <summary><c>SearchRunContext.InitialRetainedAttackValue</c>, frozen by the root score bridge.</summary>
    public int InitialRetainedAttackValue => _score.InitialRetainedAttackValue;

    /// <summary>The root-frozen kernel rule table, used by the retention stand-pat replay.</summary>
    internal FlashRules RulesForRetention => _score.Rules;

    /// <summary>
    /// The played card's primary <c>CardChoiceSupport</c> effect is <c>MoveToHand</c>. Root-frozen
    /// with the rest of the score table; used by the first-turn fetched-power commitment.
    /// </summary>
    public bool IsPrimaryMoveToHandCard(ushort id, int upgrade)
        => _score.Card(id, upgrade).PrimaryMoveToHandChoice;

    /// <summary>The registered Droplet of Precognition id, or <see cref="ushort.MaxValue"/>.</summary>
    public ushort DropletOfPrecognitionPotionId => _score.DropletOfPrecognitionPotionId;

    // ---------------------------------------------------------------------------------------------
    // Card values (CardChoiceSupport.CardValue / DynamicVarBaseValue, plus BranchCardValue extras).

    /// <summary>Current value of variable <paramref name="index"/>: canonical value plus the instance delta.</summary>
    private static decimal Var(in FlashBattle battle, in FlashCard card, FlashCardStats stats, int index)
    {
        if (index < 0 || index >= stats.Vars.Length)
            return 0m;
        decimal delta = card.Private > 0
            ? FlashDecimalVars.Delta(in battle, in card, index)
            : index switch { 0 => card.Var0, 1 => card.Var1, _ => 0m };
        return stats.Vars[index] + delta;
    }

    /// <summary>
    /// Port of <c>CardChoiceSupport.CardValue</c> with the instance's variable deltas applied. The
    /// existing engine converts each completed decimal <c>BaseValue</c> to double once; adding a
    /// double delta to a double base would round differently for decimals such as 0.75.
    /// </summary>
    private double CardValue(in FlashBattle battle, in FlashCard card, FlashCardStats stats, FlashCardScore score)
        => VarDouble(battle, card, stats, score.DamageVar, score.BaseDamage)
            + VarDouble(battle, card, stats, score.BlockVar, score.BaseBlock) * 0.8d
            + VarDouble(battle, card, stats, score.CardsVar, score.BaseCards) * 3d
            + (score.DynamicCardType
                ? (CardType(battle, card, stats) == FlashCardType.Power ? 8d : 0d)
                : (score.IsPower ? 8d : 0d));

    /// <summary>
    /// Native live <c>CardModel.Type</c>: the rule table's canonical value unless the definition reads
    /// per-card state (MadScience's saved TinkerTimeType). Roots without such cards take the cheap branch.
    /// </summary>
    private FlashCardType CardType(in FlashBattle battle, in FlashCard card, FlashCardStats stats)
        => stats.HasDynamicType ? FlashRegistry.Card(card.Id).CardType(battle, Rules, card) : stats.Type;

    private static double VarDouble(in FlashBattle battle, in FlashCard card, FlashCardStats stats, int index,
        double fallback)
    {
        if (index < 0)
            return fallback;
        decimal delta = card.Private > 0
            ? FlashDecimalVars.Delta(in battle, in card, index)
            : index switch { 0 => card.Var0, 1 => card.Var1, _ => 0m };
        return (double)(stats.Vars[index] + delta);
    }

    /// <summary>Port of <c>the existing search's BranchCardValue</c> (calculated damage where registered).</summary>
    private double BranchCardValue(in FlashBattle battle, in FlashCard card, FlashCardStats stats, FlashCardScore score)
    {
        double value = CardValue(battle, card, stats, score);
        if (CardType(battle, card, stats) != FlashCardType.Attack || score.Calculated == FlashCalculatedKind.None)
            return value;
        decimal baseVar = Var(battle, card, stats, score.CalculationBaseVar);
        decimal extraVar = Var(battle, card, stats, score.CalculationExtraVar);
        decimal damage = 0m;
        for (int position = 0; position < battle.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(battle.Roster[position]);
            if (!FlashKernel.IsHittable(battle, owner))
                continue;
            decimal multiplier = score.Calculated switch
            {
                FlashCalculatedKind.PlayerBlock => battle.Player.Block,
                FlashCalculatedKind.StrikeCardCount => CountCardsWithTag(battle, FlashCardTag.Strike),
                FlashCalculatedKind.ExhaustPileCount => battle.ExhaustCount,
                FlashCalculatedKind.PlayerStrength => Math.Max(0, GetAmount(battle, _score.StrengthPowerId, FlashLimits.PlayerOwner)),
                FlashCalculatedKind.TargetVulnerable => GetAmount(battle, _score.VulnerablePowerId, owner),
                FlashCalculatedKind.DrawPileCount => battle.DrawCount,
                FlashCalculatedKind.TargetDebuffPowerCount => CountTargetDebuffPowers(battle, owner),
                FlashCalculatedKind.FinishedCardPlays => battle.Counter(FlashCounter.FinishedCardPlays),
                FlashCalculatedKind.CardsDiscardedThisTurn => battle.Counter(FlashCounter.CardsDiscardedThisTurn),
                FlashCalculatedKind.NegativeHandCount => -battle.HandCount,
                FlashCalculatedKind.CardsDrawn => battle.Counter(FlashCounter.CardsDrawn),
                FlashCalculatedKind.StarCardCount => CountStarCostCards(battle),
                FlashCalculatedKind.OstyCurrentHp => (battle.Flags & FlashBattleFlags.OstyPresent) != 0
                    && battle.Osty.IsAlive ? battle.Osty.CurrentHp : 0,
                FlashCalculatedKind.OstyMaxHp => (battle.Flags & FlashBattleFlags.OstyPresent) != 0
                    && battle.Osty.IsAlive ? battle.Osty.MaxHp : 0,
                FlashCalculatedKind.OstyAttacksPlusOne => 1 + battle.Counter(FlashCounter.OstyAttacksThisTurn),
                FlashCalculatedKind.OstyAttackCardsMinusSelf => CountCardsWithTag(battle, FlashCardTag.OstyAttack)
                    - ((stats.Tags & FlashCardTag.OstyAttack) != 0 ? 1 : 0),
                FlashCalculatedKind.ExhaustSoulCount => CountExhaustSoulCards(battle),
                FlashCalculatedKind.EtherealPlays => battle.Counter(FlashCounter.EtherealPlays),
                FlashCalculatedKind.DoomOverThreshold => Math.Floor(
                    GetAmount(battle, _score.DoomPowerId, owner) / Var(battle, card, stats, score.DoomThresholdVar)),
                FlashCalculatedKind.TargetDoom => GetAmount(battle, _score.DoomPowerId, owner),
                FlashCalculatedKind.NonHandDraws => battle.Counter(FlashCounter.NonHandDrawsThisTurn),
                FlashCalculatedKind.GeneratedCardCount => battle.Counter(FlashCounter.CardsGeneratedThisCombat),
                _ => 0,
            };
            decimal candidate = baseVar + extraVar * multiplier;
            damage = Math.Max(damage, candidate);
        }
        return value + (double)damage - score.BaseDamage;
    }

    /// <summary>
    /// Port of <c>the existing search's CalculateReachableHandPotential</c>'s value/count half: each card
    /// playable from hand contributes its modified energy and star cost plus the same ceiling of
    /// <see cref="BranchCardValue"/> (including the enchanted replay multiplier), and the exact
    /// <see cref="ReachableHandValue"/> knapsack is fed the player's current energy and stars.
    /// </summary>
    internal (int Value, int ZeroCostPlayableCount, int PlayableAttackCount) ReachableHandPotential(in FlashBattle battle)
    {
        Span<(int Energy, int Stars, int Value)> playable = battle.HandCount <= 64
            ? stackalloc (int, int, int)[battle.HandCount]
            : new (int, int, int)[battle.HandCount];
        int count = 0;
        int zeroCostPlayableCount = 0;
        int playableAttackCount = 0;
        for (int index = 0; index < battle.HandCount; index++)
        {
            ref readonly FlashCard card = ref battle.Cards[index];
            // CanPlayCard computes the same effective hand costs; reuse them instead of running the
            // energy/star cost hook dispatch a second time for the same card and pile.
            if (!FlashKernel.CanPlayCard(battle, Rules, index, out int energyCost, out int starCost))
                continue;
            energyCost = Math.Max(0, energyCost);
            starCost = Math.Max(0, starCost);
            FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
            FlashCardScore cardScore = _score.Card(card.Id, card.Upgrade);
            double cardValue = BranchCardValue(battle, card, stats, cardScore)
                * (1 + Math.Max(0, FlashKernel.EnchantedReplayCount(card)));
            playable[count++] = (energyCost, starCost,
                Math.Max(1, (int)Math.Ceiling(cardValue)));
            if (energyCost == 0 && starCost == 0 && !stats.CostsX && !stats.StarCostsX)
                zeroCostPlayableCount++;
            if (CardType(battle, card, stats) == FlashCardType.Attack)
                playableAttackCount++;
        }
        return (ReachableHandValue.Calculate(playable[..count], battle.Energy, battle.Stars),
            zeroCostPlayableCount, playableAttackCount);
    }

    /// <summary><c>CalculatedVarSpecRegistry.CrescentSpear</c>: the combat piles' star-cost cards.</summary>
    private int CountStarCostCards(in FlashBattle battle)
    {
        int count = 0;
        for (int position = 0; position < battle.CardCount; position++)
        {
            ref readonly FlashCard card = ref battle.Cards[position];
            FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
            if (stats.StarCost >= 0 || stats.StarCostsX)
                count++;
        }
        return count;
    }

    private static readonly FlashPile[] LivePiles = [FlashPile.Discard, FlashPile.Draw, FlashPile.Hand];

    /// <summary><c>liveCards.Count(card => card.Preview is Soul)</c> over Discard, Draw and Hand.</summary>
    private int CountLiveSoulCards(in FlashBattle battle)
    {
        int count = 0;
        foreach (FlashPile pile in LivePiles)
        {
            int start = FlashPiles.Start(battle, pile);
            int size = FlashPiles.Count(battle, pile);
            for (int offset = 0; offset < size; offset++)
            {
                ref readonly FlashCard card = ref battle.Cards[start + offset];
                if (Rules.Card(card.Id, card.Upgrade).IsSoul)
                    count++;
            }
        }
        return count;
    }

    /// <summary><c>exhaustPile.Cards.Count(candidate =&gt; candidate.Preview is Soul)</c> (SoulStorm).</summary>
    private int CountExhaustSoulCards(in FlashBattle battle)
    {
        int start = FlashPiles.Start(battle, FlashPile.Exhaust);
        int count = FlashPiles.Count(battle, FlashPile.Exhaust);
        int souls = 0;
        for (int offset = 0; offset < count; offset++)
        {
            ref readonly FlashCard card = ref battle.Cards[start + offset];
            if (Rules.Card(card.Id, card.Upgrade).IsSoul)
                souls++;
        }
        return souls;
    }

    /// <summary><c>liveCards.Count(card =&gt; card.Preview.Tags.Contains(CardTag.OstyAttack))</c>.</summary>
    private int CountLiveOstyAttackCards(in FlashBattle battle)
    {
        int count = 0;
        foreach (FlashPile pile in LivePiles)
        {
            int start = FlashPiles.Start(battle, pile);
            int size = FlashPiles.Count(battle, pile);
            for (int offset = 0; offset < size; offset++)
            {
                ref readonly FlashCard card = ref battle.Cards[start + offset];
                if ((Rules.Card(card.Id, card.Upgrade).Tags & FlashCardTag.OstyAttack) != 0)
                    count++;
            }
        }
        return count;
    }

    private int CountCardsWithTag(in FlashBattle battle, FlashCardTag tag)
    {
        int count = 0;
        for (int position = 0; position < battle.CardCount; position++)
        {
            ref readonly FlashCard card = ref battle.Cards[position];
            // The rule-table tag set is canonical; instance keywords/tags never change.
            if ((Rules.Card(card.Id, card.Upgrade).Tags & tag) != 0)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Port of <c>CalculatedVarSpecRegistry.TryMultiplier</c>'s Rend case: the target's powers whose
    /// type for their current amount is Debuff, excluding temporary powers.
    /// </summary>
    private static int CountTargetDebuffPowers(in FlashBattle battle, byte owner)
    {
        int count = 0;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (power.Owner != owner)
                continue;
            FlashPowerDef def = FlashRegistry.Power(power.Id);
            if (def.TypeForAmount(power.Amount) == FlashPowerType.Debuff && !def.IsTemporaryPower)
                count++;
        }
        return count;
    }

    // ---------------------------------------------------------------------------------------------
    // Power amounts.

    private static int GetAmount(in FlashBattle battle, ushort powerId, byte owner)
    {
        if (powerId == ushort.MaxValue)
            return 0;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (power.Id == powerId && power.Owner == owner)
                return power.Amount;
        }
        return 0;
    }

    private static bool HasPower(in FlashBattle battle, ushort powerId, byte owner)
        => powerId != ushort.MaxValue && GetAmount(battle, powerId, owner) > 0;

    // ---------------------------------------------------------------------------------------------
    // Growth rewards and configured relic counters (the existing search's Snapshot).

    /// <summary>
    /// <c>SimulatedCombatState.GrowthRewards</c> from the packed counters. The kernel packs two
    /// 16-bit source counts per counter; see <see cref="FlashGrowthRewards"/>.
    /// </summary>
    private static GrowthValues CaptureGrowthRewards(in FlashBattle battle) => new(
        FlashGrowthRewards.Get(battle, FlashGrowthSource.HandOfGreed),
        FlashGrowthRewards.Get(battle, FlashGrowthSource.TheHunt),
        FlashGrowthRewards.Get(battle, FlashGrowthSource.Feed),
        FlashGrowthRewards.Get(battle, FlashGrowthSource.Royalties),
        FlashGrowthRewards.Get(battle, FlashGrowthSource.Alchemize),
        FlashGrowthRewards.Get(battle, FlashGrowthSource.GeneticAlgorithm),
        FlashGrowthRewards.Get(battle, FlashGrowthSource.TheScythe),
        FlashGrowthRewards.Get(battle, FlashGrowthSource.Goopy),
        FlashGrowthRewards.Get(battle, FlashGrowthSource.ForbiddenGrimoire),
        FlashGrowthRewards.Get(battle, FlashGrowthSource.MadScience));

    /// <summary>
    /// <c>SimulatedCombatState.EvaluateRelicCounters</c> over the frozen policy targets, including
    /// the Meat on the Bone victory heal credit the existing snapshot adds in
    /// <c>the existing search's Snapshot</c>.
    /// </summary>
    private RelicCounterEvaluation EvaluateRelicCounters(in FlashBattle battle, bool won)
    {
        RelicCounterEvaluation result = default;
        for (int index = 0; index < _score.RelicCounterRules.Length; index++)
        {
            FlashRelicCounterRule rule = _score.RelicCounterRules[index];
            int value = ReadRelicCounter(battle, rule);
            result = RelicCounterPolicy.Add(result, rule.Target, value % rule.Target.Period);
        }
        if (won
            && (result.SatisfiedMask & (1UL << (int)RelicCounterId.MeatOnTheBone)) != 0)
        {
            int thresholdHeal = _score.PostCombatRelicHeal.HealFor(
                    battle.Player.CurrentHp, battle.Player.MaxHp)
                - _score.PostCombatRelicHeal.MonotoneHealFor(
                    battle.Player.CurrentHp, battle.Player.MaxHp);
            result = result with
            {
                HealingHpCredit = ActEndingBossPolicy.PersistentValueOfRecoveredHp(
                    thresholdHeal, _score.BossHpRelief),
            };
        }
        return result;
    }

    /// <summary>
    /// <c>SimulatedCombatState.ReadRelicCounter</c> for the supported relics. Flash already carries
    /// each counter that the native relic reads for gameplay; only the Meat on the Bone threshold
    /// health comparison is recomputed from the battle.
    /// </summary>
    private static int ReadRelicCounter(in FlashBattle battle, in FlashRelicCounterRule rule)
    {
        if (rule.Target.Id == RelicCounterId.MeatOnTheBone)
        {
            return battle.Player.CurrentHp * 100 <= battle.Player.MaxHp * rule.MeatOnTheBoneThreshold
                ? 1 : 0;
        }
        for (int index = 0; index < battle.RelicCount; index++)
        {
            if (battle.Relics[index].Id == rule.RelicId)
                return battle.Relics[index].Counter;
        }
        throw new FlashUnsupportedException(
            $"遗物计数目标 {rule.Target.Id} 的遗物不在 Flash 状态中。");
    }

    // ---------------------------------------------------------------------------------------------
    // Snapshot.

    /// <param name="includeRetentionOnly">
    /// When false, the two values read only by the retention comparators
    /// (<see cref="FlashScoreFeatures.ReachableHandValue"/> and
    /// <see cref="FlashScoreFeatures.ZeroCostPlayableCount"/>) are left at zero. The solver computes
    /// them when a state actually enters a retention pool; the K4 differential calls the default
    /// (true) and keeps checking every field.
    /// </param>
    public FlashScoreFeatures Features(in FlashBattle battle, int actionCount, bool includeRetentionOnly = true)
        => Evaluate(battle, actionCount, includeRetentionOnly);

    /// <summary>
    /// <c>SearchPolicySnapshot.GrowthTargetSatisfied</c>: every bounded growth goal has been realized.
    /// The request already disabled the HP-target stop for unbounded targets.
    /// </summary>
    public bool GrowthTargetsSatisfied(in FlashScoreFeatures features)
        => features.GrowthRewards.Satisfies(_score.RequiredGrowthRewards);

    /// <summary><c>SearchPolicySnapshot.RelicTargetsSatisfied</c>.</summary>
    public bool RelicTargetsSatisfied(in FlashScoreFeatures features)
        => (features.RelicSatisfiedMask & features.RelicTargetMask) == features.RelicTargetMask;

    private FlashScoreFeatures Evaluate(in FlashBattle battle, int actionCount, bool includeRetentionOnly)
    {
        int enemyHp = 0, enemyBlock = 0, rawEnemyHp = 0, maxCurrentEnemyHp = 0;
        int revivingEnemyCount = 0, aliveEnemyCount = 0;
        ulong aliveEnemyMask = 0;
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            ref readonly FlashEnemy enemy = ref battle.Enemies[index];
            int currentHp = enemy.Creature.CurrentHp;
            int effectiveHp = EffectiveEnemyHp(battle, index);
            bool inCombat = (enemy.Flags & FlashEnemyFlags.InCombat) != 0;
            enemyHp += effectiveHp;
            if (effectiveHp > 0 && inCombat)
                enemyBlock += Math.Max(0, enemy.Creature.Block);
            rawEnemyHp += Math.Max(0, currentHp);
            maxCurrentEnemyHp = Math.Max(maxCurrentEnemyHp, Math.Max(0, currentHp));
            if (currentHp <= 0 && effectiveHp > 0)
                revivingEnemyCount++;
            if (effectiveHp > 0 && inCombat)
            {
                aliveEnemyCount++;
                aliveEnemyMask |= 1UL << index;
            }
        }

        bool dead = battle.Player.IsDead || battle.Outcome == FlashOutcome.Defeat;
        bool won = !dead && battle.Outcome == FlashOutcome.Victory;
        // The existing snapshot takes ThreatProjection(player.CurrentHp, 0, 0) whenever `won` is false
        // but the boundary is not None (the existing search's StateEvaluation.cs). EventDefeat is such a
        // boundary: it must not project the enemies' next intents or death saves.
        bool eventDefeat = battle.Outcome == FlashOutcome.EventDefeat;
        bool risk = (battle.Flags & FlashBattleFlags.HasRisk) != 0;
        int projectedHp;
        int projectedDeathSaveHpRestored = 0;
        int projectedDeathSaveUseCount = 0;
        int blockConsumed = 0;
        if (won || eventDefeat)
        {
            projectedHp = battle.Player.CurrentHp;
        }
        else
        {
            ThreatProjection threat = ProjectHpAfterThreat(battle);
            projectedHp = threat.Hp;
            projectedDeathSaveHpRestored = threat.DeathSaveHpRestored;
            projectedDeathSaveUseCount = threat.DeathSaveUseCount;
            blockConsumed = threat.BlockConsumed;
        }

        int cumulativePlayerHpLost = battle.Counter(FlashCounter.CumulativeHpLost);
        int recoveredPlayerHp = battle.Counter(FlashCounter.RecoveredHp);
        int deathSaveHpRestored = battle.Counter(FlashCounter.DeathSaveRelicHpRestored)
            + battle.Counter(FlashCounter.DeathSavePotionHpRestored);
        int deathSaveUseCount = battle.Counter(FlashCounter.DeathSaveUseCount);
        double hpWeight = SolverWeights.Hp;
        double score = dead || projectedHp <= 0
            ? SolverWeights.DeathPenalty
            : projectedHp * hpWeight;
        score += (battle.Player.MaxHp - _score.InitialPlayerMaxHp) * hpWeight;
        score -= cumulativePlayerHpLost * hpWeight;
        score -= ActEndingBossPolicy.DeathSaveBeamCost(
            deathSaveHpRestored + projectedDeathSaveHpRestored) * hpWeight;

        int realizedLongTermResourceValue = _score.IgnoreLongTermRewards
            ? 0
            : battle.Counter(FlashCounter.LongTermResourceValue);
        int exhaustedTheHunts = 0;
        if (_score.TheHuntCardId != ushort.MaxValue)
        {
            int start = FlashPiles.Start(battle, FlashPile.Exhaust);
            int count = FlashPiles.Count(battle, FlashPile.Exhaust);
            for (int index = 0; index < count; index++)
            {
                if (battle.Cards[start + index].Id == _score.TheHuntCardId)
                    exhaustedTheHunts++;
            }
        }
        int rewardedTheHunts = _score.TheHuntPowerId == ushort.MaxValue
            ? 0
            : Math.Max(0, GetAmount(battle, _score.TheHuntPowerId, FlashLimits.PlayerOwner));
        int missedTheHuntRewards = Math.Max(0, exhaustedTheHunts - rewardedTheHunts);
        int longTermResourceValue = _score.IgnoreLongTermRewards
            ? 0
            : realizedLongTermResourceValue - missedTheHuntRewards * TheHuntLongTermResourceValue;
        GrowthValues growthRewards = _score.IgnoreLongTermRewards
            ? default
            : CaptureGrowthRewards(battle);
        int growthHpCredit = _score.GrowthBudgets.Credit(growthRewards);
        score += Math.Min(
            realizedLongTermResourceValue * SolverWeights.LongTermResourceBeamValue,
            SolverWeights.LongTermResourceBeamCap);
        score += (double)growthHpCredit * hpWeight;
        RelicCounterEvaluation relicCounters = EvaluateRelicCounters(battle, won);
        score += (double)(relicCounters.HpCredit + relicCounters.HealingHpCredit) * hpWeight;
        score += relicCounters.SatisfiedPriority * 0.1 - relicCounters.Distance * 0.001;

        int strategyGoalHpCredit = growthHpCredit + relicCounters.HpCredit;
        int strategicHpCredit = strategyGoalHpCredit + relicCounters.HealingHpCredit;
        int strategyGoalCount = growthRewards.Total + relicCounters.SatisfiedPriority;

        int angerCopiesGenerated = battle.Counter(FlashCounter.AngerCopiesGenerated);
        score += angerCopiesGenerated * SolverWeights.AngerCopyBeamPenalty;
        if (won)
            score += SolverWeights.VictoryBonus;
        score += enemyHp * SolverWeights.EnemyHp;

        int liveDeckClutter = 0;
        int liveCardCount = battle.HandCount + battle.DrawCount + battle.DiscardCount;
        for (int pile = 0; pile < 3; pile++)
        {
            FlashPile segment = pile switch
            {
                0 => FlashPile.Discard,
                1 => FlashPile.Draw,
                _ => FlashPile.Hand,
            };
            int start = FlashPiles.Start(battle, segment);
            int count = FlashPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly FlashCard card = ref battle.Cards[start + offset];
                FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
                FlashCardScore cardScore = _score.Card(card.Id, card.Upgrade);
                if ((CardType(battle, card, stats) is FlashCardType.Status or FlashCardType.Curse)
                    && !LeavesHandAtTurnEnd(battle, segment, card, cardScore))
                {
                    liveDeckClutter = checked(liveDeckClutter + 1);
                }
            }
        }
        score += liveDeckClutter * SolverWeights.LiveDeckClutterPenalty;

        // SimulatedCombatState's outstanding-stolen-resource counters. Only theft roots carry them; the
        // root flag keeps every other root off these per-node scans.
        int outstandingStolenResource = 0;
        int minimumOutstandingStolenResource = 0;
        if (Rules.HasTheftContent)
        {
            outstandingStolenResource = FlashKernel.OutstandingStolenResource(battle);
            minimumOutstandingStolenResource = FlashKernel.MinimumOutstandingStolenResource(battle);
        }
        if (_score.TheftPolicy == SolverTheftPolicy.PreserveResources)
            score += outstandingStolenResource * SolverWeights.OutstandingStolenResourcePenalty;

        int retainedAttackValue = 0;
        for (int pile = 0; pile < 3; pile++)
        {
            FlashPile segment = pile switch
            {
                0 => FlashPile.Discard,
                1 => FlashPile.Draw,
                _ => FlashPile.Hand,
            };
            int start = FlashPiles.Start(battle, segment);
            int count = FlashPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly FlashCard card = ref battle.Cards[start + offset];
                FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
                if (CardType(battle, card, stats) != FlashCardType.Attack)
                    continue;
                FlashCardScore cardScore = _score.Card(card.Id, card.Upgrade);
                retainedAttackValue += Math.Max(
                    1,
                    (int)Math.Round(BranchCardValue(battle, card, stats, cardScore)));
            }
        }

        ThreatFocus focus = BuildThreatFocus(battle);

        // Strategic effects over the powers that contribute (player-side buffs, positive amount, not
        // temporary), in the existing engine's listener order.
        StrategicEffectVector strategicEffects = default;
        int offensivePersistentBuffValue = 0;
        FlashSetupTraits persistentSetupTraits = FlashSetupTraits.None;
        FlashStrategicRequirements requirements = FlashStrategicRequirements.None;
        bool needsExhaustDrawTiming = false;
        bool skillsExhaust = false;
        bool needsRecurringEnergy = false;
        // the existing search's StateEvaluation: the third-act branch reads Lethality's native first attack,
        // and the interaction potential is captured only when one of its three powers contributes.
        bool needsFirstAttackDamage = false;
        bool hasPagestorm = false;
        int danseMacabreEnergyThreshold = 0;
        int demesneAmount = 0;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (power.Owner != FlashLimits.PlayerOwner)
                continue;
            FlashPowerScore rules = _score.Power(power.Id);
            if (!Contributes(power, rules))
                continue;
            requirements |= rules.Requirements;
            needsExhaustDrawTiming |= rules.Kind == FlashStrategicKind.DarkEmbrace;
            skillsExhaust |= rules.Kind == FlashStrategicKind.Corruption;
            needsRecurringEnergy |= rules.Kind is FlashStrategicKind.RecurringEnergy or FlashStrategicKind.OrbitEnergy;
            needsFirstAttackDamage |= rules.Kind == FlashStrategicKind.Lethality;
            if (_score.Act3BossStrategy)
            {
                hasPagestorm |= rules.Kind == FlashStrategicKind.Pagestorm;
                if (rules.Kind == FlashStrategicKind.DanseMacabre)
                {
                    danseMacabreEnergyThreshold = Math.Max(
                        danseMacabreEnergyThreshold, rules.DanseMacabreEnergyThreshold);
                }
                if (rules.Kind == FlashStrategicKind.Demesne)
                    demesneAmount += Math.Max(0, power.Amount);
            }
        }

        StrategicEffectContext? strategicContext = null;
        int refundSpend = 0, refundCapacity = 0, refundDraws = 0;
        bool refundCaptured = false;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (power.Owner != FlashLimits.PlayerOwner)
                continue;
            FlashPowerScore rules = _score.Power(power.Id);
            if (!Contributes(power, rules))
                continue;
            if (strategicContext is null)
            {
                StrategicEffectContext context = BuildStrategicContext(
                    battle, enemyHp, focus.TotalThreat, focus.IncomingHitCount, requirements, skillsExhaust);
                if (_score.Act3BossStrategy)
                {
                    context = context with
                    {
                        Act3BossInteractions = true,
                        FirstAttackDamage = needsFirstAttackDamage
                            ? CaptureFirstAttackDamage(battle) : 0,
                    };
                    if (hasPagestorm || danseMacabreEnergyThreshold > 0 || demesneAmount > 0)
                    {
                        var interactions = CaptureAct3BossInteractionPotential(
                            battle,
                            context.RemainingTurns,
                            context.ReachableCards,
                            hasPagestorm,
                            skillsExhaust,
                            demesneAmount,
                            danseMacabreEnergyThreshold);
                        context = context with
                        {
                            EtherealDrawTriggers = interactions.EtherealDrawTriggers,
                            PagestormBonusDrawCapacity = interactions.PagestormBonusDrawCapacity,
                            HighEnergyPlays = interactions.HighEnergyPlays,
                            DemesneEnergyGain = interactions.DemesneEnergyGain,
                            DemesneDrawGain = interactions.DemesneDrawGain,
                        };
                    }
                }
                strategicContext = needsExhaustDrawTiming
                    ? WithExhaustDrawTiming(context, battle, skillsExhaust) : context;
            }
            StrategicEffectContext effectContext = strategicContext.Value;
            if (needsRecurringEnergy && !refundCaptured)
            {
                (refundSpend, refundCapacity) = CaptureEnergyRefundWindow(battle, effectContext, skillsExhaust);
                refundDraws = Math.Max(0, effectContext.ReachableCards - battle.HandCount);
                refundCaptured = true;
            }
            if (rules.Kind == FlashStrategicKind.RecurringEnergy)
            {
                int cardsLeft = power.Aux0;
                int triggers = refundDraws < cardsLeft ? 0 : 1 + (refundDraws - cardsLeft) / 10;
                int gain = (int)Math.Min(refundCapacity, (long)triggers * power.Amount);
                refundCapacity -= gain;
                effectContext = effectContext with { RecurringEnergyGain = gain };
            }
            else if (rules.Kind == FlashStrategicKind.OrbitEnergy)
            {
                // the existing search's StateEvaluation's Orbit branch: spend + remainder over 4, times the
                // energy granted per trigger. The shared refund capacity decreases per instance.
                int gain = (int)Math.Min(refundCapacity,
                    ((long)refundSpend + power.Aux0) / 4 * power.Amount);
                refundCapacity -= gain;
                effectContext = effectContext with { RecurringEnergyGain = gain };
            }
            StrategicEffectVector effect = EvaluateStrategic(rules.Kind, power.Amount, effectContext);
            strategicEffects += effect;
            offensivePersistentBuffValue += effect.DamagePotential + effect.ScalingPotential;
            persistentSetupTraits |= rules.Trait;
        }
        int persistentBuffValue = strategicEffects.RetentionValue;
        if (battle.OrbCount > 0)
        {
            // the existing search's StateEvaluation.OrbRetentionValue: the player's orb queue is an
            // OrbEngine setup trait, and each orb's (Focus-modified) passive adds to the persistent
            // buff value; Glass counts once per living enemy.
            persistentSetupTraits |= FlashSetupTraits.OrbEngine;
            persistentBuffValue = checked(persistentBuffValue + OrbRetentionValue(battle, aliveEnemyCount));
        }

        int latentSetupValue = 0;
        FlashSetupTraits latentSetupTraits = FlashSetupTraits.None;
        for (int pile = 0; pile < 3; pile++)
        {
            FlashPile segment = pile switch
            {
                0 => FlashPile.Discard,
                1 => FlashPile.Draw,
                _ => FlashPile.Hand,
            };
            int start = FlashPiles.Start(battle, segment);
            int count = FlashPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly FlashCard card = ref battle.Cards[start + offset];
                FlashCardScore cardScore = _score.Card(card.Id, card.Upgrade);
                FlashSetupTraits trait = cardScore.LatentTrait;
                int latentValue = cardScore.LatentValue;
                int rider = cardScore.DynamicCardType ? FlashRegistry.Card(card.Id).PrivateRider(card) : -1;
                if (cardScore.DynamicCardType
                    && rider >= 0
                    && CardType(battle, card, stats: Rules.Card(card.Id, card.Upgrade))
                        == FlashCardType.Power)
                {
                    // the existing search's StateEvaluation.LatentCardSetupValue's MadScience cases.
                    trait = rider == CombatSolver.Engine.InCombat.Flash.Content.Cards.Event.MadScience.RiderCurious
                        ? FlashSetupTraits.Curious
                        : FlashSetupTraits.RecurringScaling;
                    latentValue = rider == CombatSolver.Engine.InCombat.Flash.Content.Cards.Event.MadScience.RiderCurious
                        ? 10
                        : 5;
                }
                latentSetupTraits |= trait;
                if (trait == FlashSetupTraits.None || !persistentSetupTraits.HasFlag(trait))
                    latentSetupValue += latentValue;
            }
        }

        int replayPotentialValue = ReplayPotentialValue(battle);
        // the existing search's StateEvaluation.CalculateReachableHandPotential reads the same playable
        // hand costs/values and feeds them to ReachableHandValue.Calculate.
        int reachableHandValue = 0;
        int zeroCostPlayableCount = 0;
        int playableAttackCount = 0;
        if (includeRetentionOnly)
            (reachableHandValue, zeroCostPlayableCount, playableAttackCount) =
                ReachableHandPotential(battle);
        int strategicRetentionValue = strategicEffects.RetentionValue;

        int retainedHandValue = 0;
        for (int offset = 0; offset < battle.HandCount; offset++)
        {
            ref readonly FlashCard card = ref battle.Cards[offset];
            if ((card.Flags & FlashCardFlags.RetainThisTurn) == 0
                && (card.Keywords & FlashKeywords.Retain) == 0)
                continue;
            FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
            FlashCardScore cardScore = _score.Card(card.Id, card.Upgrade);
            retainedHandValue = checked(retainedHandValue + Math.Max(
                1,
                (int)Math.Ceiling(CardValue(battle, card, stats, cardScore) * 2d)));
        }
        // Free-card opportunity (FreeAttackPower), Void Form and summon next turn. Void Form has no
        // admitted Flash content.
        int summonNextTurn = GetAmount(battle, _score.SummonNextTurnPowerId, FlashLimits.PlayerOwner);
        int summonNextTurnValue = summonNextTurn == 0
            ? 0
            : summonNextTurn * (4 + Math.Min(12, CountLiveOstyAttackCards(battle) * 2));
        int futureResourceValue = GetAmount(battle, _score.EnergyNextTurnPowerId, FlashLimits.PlayerOwner) * 16
            + GetAmount(battle, _score.DrawCardsNextTurnPowerId, FlashLimits.PlayerOwner) * 8
            + GetAmount(battle, _score.StarNextTurnPowerId, FlashLimits.PlayerOwner) * 8
            + GetAmount(battle, _score.RetainHandPowerId, FlashLimits.PlayerOwner) * 4
            + summonNextTurnValue
            + retainedHandValue
            + FreeCardOpportunityValue(battle, FlashCardType.Attack, _score.FreeAttackPowerId)
            + FreeCardOpportunityValue(battle, FlashCardType.Skill, _score.FreeSkillPowerId)
            + FreeCardOpportunityValue(battle, FlashCardType.Power, _score.FreePowerPowerId)
            + VoidFormOpportunityValue(battle);

        int delayedDamageValue = 0;
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            ref readonly FlashEnemy enemy = ref battle.Enemies[index];
            if ((enemy.Flags & FlashEnemyFlags.InCombat) == 0 || !enemy.Creature.IsAlive)
                continue;
            byte owner = FlashKernel.EnemyOwner((byte)index);
            int poison = Math.Max(0, GetAmount(battle, _score.PoisonPowerId, owner));
            int demise = Math.Max(0, GetAmount(battle, _score.DemisePowerId, owner));
            int doom = Math.Max(0, GetAmount(battle, _score.DoomPowerId, owner));
            int currentHp = Math.Max(0, enemy.Creature.CurrentHp);
            int cappedDoom = Math.Min(currentHp, doom);
            int doomProgress = currentHp <= 0
                ? 0
                : (int)Math.Min(currentHp, (long)cappedDoom * cappedDoom / currentHp);
            delayedDamageValue = checked(delayedDamageValue + poison + demise + doomProgress);
        }
        persistentBuffValue += CountLiveSoulCards(battle);
        int reactiveDamageValue = Math.Max(0, GetAmount(battle, _score.SleightOfFleshPowerId, FlashLimits.PlayerOwner));

        bool hasBlockDamagePayoff = false;
        for (int pile = 0; pile < 3 && !hasBlockDamagePayoff; pile++)
        {
            FlashPile segment = pile switch
            {
                0 => FlashPile.Discard,
                1 => FlashPile.Draw,
                _ => FlashPile.Hand,
            };
            int start = FlashPiles.Start(battle, segment);
            int count = FlashPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                if (battle.Cards[start + offset].Id == _score.BodySlamCardId)
                {
                    hasBlockDamagePayoff = true;
                    break;
                }
            }
        }
        int offensiveProgressValue = offensivePersistentBuffValue
            + delayedDamageValue
            + reactiveDamageValue
            + (hasBlockDamagePayoff ? Math.Max(0, battle.Player.Block) : 0);

        int enemyStrengthSuppression = 0;
        int enemyWeakTurns = 0;
        int vulnerable = 0;
        int focusTargetVulnerableTurns = 0;
        uint? mostVulnerableTargetCombatId = null;
        int mostVulnerableTurns = 0;
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            ref readonly FlashEnemy enemy = ref battle.Enemies[index];
            if ((enemy.Flags & FlashEnemyFlags.InCombat) == 0 || !enemy.Creature.IsAlive)
                continue;
            byte owner = FlashKernel.EnemyOwner((byte)index);
            int strengthSuppression = -GetAmount(battle, _score.StrengthPowerId, owner);
            int weakTurns = Math.Max(0, GetAmount(battle, _score.WeakPowerId, owner));
            int vulnerableTurns = Math.Max(0, GetAmount(battle, _score.VulnerablePowerId, owner));
            enemyStrengthSuppression += strengthSuppression;
            enemyWeakTurns += weakTurns;
            vulnerable += vulnerableTurns;
            if (focus.CombatId is { } focusId && enemy.CombatId == focusId)
                focusTargetVulnerableTurns = vulnerableTurns;
            if (vulnerableTurns > mostVulnerableTurns)
            {
                mostVulnerableTurns = vulnerableTurns;
                mostVulnerableTargetCombatId = enemy.CombatId;
            }
        }

        // the existing search's StateEvaluation: positive Sandpit stacks targeting this player whose owner is
        // alive, summed with checked overflow. The existing engine visits EffectivePowers (live roster
        // powers); dead owners have their powers removed by ApplyEnemyDeathPowers.
        int sandpitRemaining = 0;
        if (_score.SandpitPowerId != ushort.MaxValue)
        {
            for (int index = 0; index < battle.PowerCount; index++)
            {
                ref readonly FlashPower power = ref battle.Powers[index];
                if (power.Id != _score.SandpitPowerId
                    || power.Target != FlashLimits.PlayerOwner
                    || !battle.Creature(power.Owner).IsAlive)
                {
                    continue;
                }
                sandpitRemaining = checked(sandpitRemaining + Math.Max(0, power.Amount));
            }
        }

        int vulnerableAttackWindow = Math.Min(
            SolverWeights.VulnerableAttackWindowCap,
            retainedAttackValue);
        score += (long)focusTargetVulnerableTurns
            * vulnerableAttackWindow
            * SolverWeights.VulnerableAttackMultiplierBeamValue;
        score += (long)Math.Max(0, vulnerable - focusTargetVulnerableTurns)
            * vulnerableAttackWindow
            * SolverWeights.OffTargetVulnerableAttackMultiplierBeamValue;
        score += actionCount * SolverWeights.ActionPenalty;
        if (risk)
            score += SolverWeights.RiskPenalty;

        int potionUseCount = 0;
        int potionStrategicCost = 0;
        int explicitPotionStrategicCost = 0;
        for (int slot = 0; slot < _score.RootPotionSlotCount && slot < FlashLimits.PotionSlots; slot++)
        {
            ushort rootPotion = _score.RootPotions[slot];
            if (rootPotion == 0)
                continue;
            if (slot >= battle.PotionSlotCount || battle.Potions[slot] != rootPotion)
            {
                int cost = _score.PotionStrategicCost[rootPotion - 1];
                potionUseCount = checked(potionUseCount + 1);
                potionStrategicCost = checked(potionStrategicCost + cost);
                // Automatic potion uses are death saves (Fairy in a Bottle), which the search never
                // offers explicitly and which empty their slot when they fire.
                if (rootPotion != _score.FairyInABottlePotionId + 1)
                    explicitPotionStrategicCost = checked(explicitPotionStrategicCost + cost);
            }
        }
        int automaticPotionUseCount = battle.Counter(FlashCounter.AutomaticPotionUseCount);
        // Generated potions never occupied a root slot, so the slot diff cannot see their use. The
        // packed cost distinguishes explicit from automatic uses (Fairy in a Bottle); the free
        // EntropicBrew uses carry zero.
        potionUseCount = checked(potionUseCount + battle.Counter(FlashCounter.GeneratedPotionUseCount));
        int generatedCosts = battle.Counter(FlashCounter.GeneratedPotionStrategicCost);
        int generatedExplicitCost = generatedCosts & 0xFFFF;
        int generatedAutomaticCost = (int)((uint)generatedCosts >> 16);
        potionStrategicCost = checked(potionStrategicCost + generatedExplicitCost + generatedAutomaticCost);
        explicitPotionStrategicCost = checked(explicitPotionStrategicCost + generatedExplicitCost);
        // A fixed prefix may already have spent potions; the search root's frozen counters keep these
        // cumulative values equal to the existing snapshot's instead of restarting at the prefix end.
        potionUseCount = checked(potionUseCount + _score.InitialPotionUseCount);
        potionStrategicCost = checked(potionStrategicCost + _score.InitialPotionStrategicCost);
        explicitPotionStrategicCost = checked(
            explicitPotionStrategicCost + _score.InitialExplicitPotionStrategicCost);

        int defensiveBlockValue = MeasureDefensiveBlockReserve(battle, blockConsumed);

        return new FlashScoreFeatures
        {
            Score = score,
            PlayerDead = dead,
            AllEnemiesDead = won,
            PlayerHp = battle.Player.CurrentHp,
            PlayerMaxHp = battle.Player.MaxHp,
            PlayerBlock = battle.Player.Block,
            OstyHp = (battle.Flags & FlashBattleFlags.OstyPresent) != 0 ? battle.Osty.CurrentHp : 0,
            OstyMaxHp = (battle.Flags & FlashBattleFlags.OstyPresent) != 0 ? battle.Osty.MaxHp : 0,
            CumulativePlayerHpLost = cumulativePlayerHpLost,
            RecoveredPlayerHp = recoveredPlayerHp,
            DeathSaveHpRestored = deathSaveHpRestored,
            DeathSaveUseCount = deathSaveUseCount,
            ProjectedDeathSaveUseCount = deathSaveUseCount + projectedDeathSaveUseCount,
            ProjectedPlayerHp = projectedHp,
            DefensiveBlockValue = defensiveBlockValue,
            LongTermResourceValue = longTermResourceValue,
            GrowthHpCredit = growthHpCredit,
            GrowthRewards = growthRewards,
            GrowthRewardCount = growthRewards.Total,
            StrategyGoalHpCredit = strategyGoalHpCredit,
            StrategicHpCredit = strategicHpCredit,
            StrategyGoalCount = strategyGoalCount,
            RelicSatisfiedMask = relicCounters.SatisfiedMask,
            RelicTargetMask = relicCounters.TargetMask,
            RelicCounterValues = relicCounters.CounterValues,
            RelicTargetMinimums = relicCounters.TargetMinimums,
            RelicTargetMaximums = relicCounters.TargetMaximums,
            RelicHpCredit = relicCounters.HpCredit,
            RelicDistance = relicCounters.Distance,
            RelicSatisfiedPriority = relicCounters.SatisfiedPriority,
            RelicHealingHpCredit = relicCounters.HealingHpCredit,
            AngerCopiesGenerated = angerCopiesGenerated,
            EnemyHp = enemyHp,
            EnemyBlock = enemyBlock,
            RawEnemyHp = rawEnemyHp,
            MaxCurrentEnemyHp = maxCurrentEnemyHp,
            AliveEnemyCount = aliveEnemyCount,
            AliveEnemyMask = aliveEnemyMask,
            RevivingEnemyCount = revivingEnemyCount,
            LiveDeckClutter = liveDeckClutter,
            LiveDeckSize = liveCardCount,
            OutstandingStolenResource = outstandingStolenResource,
            MinimumOutstandingStolenResource = minimumOutstandingStolenResource,
            RetainedAttackValue = retainedAttackValue,
            FocusTargetCombatId = focus.CombatId,
            FocusTargetPressure = focus.Pressure,
            FocusTargetRemainingHp = focus.RemainingHp,
            FocusTargetCurrentThreat = focus.CurrentThreat,
            FocusTargetVulnerableTurns = focusTargetVulnerableTurns,
            MostVulnerableTargetCombatId = mostVulnerableTargetCombatId,
            EnemyVulnerableTurns = vulnerable,
            Energy = battle.Energy,
            Stars = battle.Stars,
            HandCount = battle.HandCount,
            Turn = battle.PlayerTurn,
            ReachableHandValue = reachableHandValue,
            ZeroCostPlayableCount = zeroCostPlayableCount,
            PlayableAttackCount = playableAttackCount,
            PersistentBuffValue = persistentBuffValue,
            StrategicRetentionValue = strategicRetentionValue,
            StrategicDamagePotential = strategicEffects.DamagePotential,
            StrategicPreventionPotential = strategicEffects.PreventionPotential,
            StrategicResourcePotential = strategicEffects.ResourcePotential,
            StrategicCardAccessPotential = strategicEffects.CardAccessPotential,
            StrategicScalingPotential = strategicEffects.ScalingPotential,
            HasRisk = risk,
            Act3BossInteractions = strategicContext?.Act3BossInteractions ?? false,
            FirstAttackDamage = strategicContext?.FirstAttackDamage ?? 0,
            EtherealDrawTriggers = strategicContext?.EtherealDrawTriggers ?? 0,
            PagestormBonusDrawCapacity = strategicContext?.PagestormBonusDrawCapacity ?? 0,
            HighEnergyPlays = strategicContext?.HighEnergyPlays ?? 0,
            DemesneEnergyGain = strategicContext?.DemesneEnergyGain ?? 0,
            DemesneDrawGain = strategicContext?.DemesneDrawGain ?? 0,
            PersistentSetupTraits = (PersistentSetupTraits)persistentSetupTraits,
            StrategicSetupTraits = (PersistentSetupTraits)(persistentSetupTraits | latentSetupTraits),
            LatentSetupValue = latentSetupValue,
            FutureResourceValue = futureResourceValue,
            ReplayPotentialValue = replayPotentialValue,
            DelayedDamageValue = delayedDamageValue,
            ReactiveDamageValue = reactiveDamageValue,
            OffensiveProgressValue = offensiveProgressValue,
            SandpitRemaining = sandpitRemaining,
            EnemyStrengthSuppression = enemyStrengthSuppression,
            EnemyWeakTurns = enemyWeakTurns,
            PotionUseCount = potionUseCount,
            PotionStrategicCost = potionStrategicCost,
            AutomaticPotionUseCount = automaticPotionUseCount,
            ExplicitPotionStrategicCost = explicitPotionStrategicCost,
            ExplicitPotionUseCount = checked(potionUseCount - automaticPotionUseCount),
        };
    }

    private int EffectiveEnemyHp(in FlashBattle battle, int index)
    {
        ref readonly FlashEnemy enemy = ref battle.Enemies[index];
        // SimulatedCombatState.EffectiveEnemyHp: an armed Steam Eruption phase counts as zero HP.
        if (enemy.SteamPhase != FlashSteamPhase.None)
            return 0;
        // SimulatedCombatState.EffectiveEnemyHp's TestSubject branch: while Adaptable is alive, the
        // remaining forms' HP is part of the effective HP even at full current HP.
        if (_score.AdaptablePowerId != ushort.MaxValue
            && HasPower(battle, _score.AdaptablePowerId, FlashKernel.EnemyOwner((byte)index))
            && enemy.DeathPhase != FlashDeathPhase.PermanentlyDead
            && FlashRegistry.Monster(enemy.MonsterId).HasAdaptableFormHp)
        {
            FlashMonsterRules rules = Rules.MonsterRule(enemy.Rules);
            int remaining = Math.Max(0, enemy.Creature.CurrentHp);
            if (enemy.Ai[0] < 1)
                remaining += enemy.Ai[2];
            if (enemy.Ai[0] < 2)
                remaining += enemy.Ai[3];
            return remaining;
        }
        if (enemy.Creature.CurrentHp > 0)
            return enemy.Creature.CurrentHp;
        if (enemy.DeathPhase != FlashDeathPhase.Reviving)
            return 0;
        byte owner = FlashKernel.EnemyOwner((byte)index);
        // SimulatedCombatState.RevivingEnemyHp: a Decimillipede segment restores its ReattachPower
        // amount only while another reattaching segment has not completed death (alive or reviving).
        if (HasPower(battle, _score.ReattachPowerId, owner))
        {
            for (int position = 0; position < battle.RosterCount; position++)
            {
                byte other = FlashKernel.EnemyOwner(battle.Roster[position]);
                if (other == owner || !HasPower(battle, _score.ReattachPowerId, other))
                    continue;
                if (battle.Enemies[battle.Roster[position]].DeathPhase != FlashDeathPhase.PermanentlyDead)
                    return Math.Max(0, GetAmount(battle, _score.ReattachPowerId, owner));
            }
            return 0;
        }
        // Illusion-powered revives restore the captured max HP; the power amount is irrelevant.
        return HasPower(battle, _score.IllusionPowerId, owner) ? enemy.Creature.MaxHp : 0;
    }

    private bool LeavesHandAtTurnEnd(in FlashBattle battle, FlashPile pile, in FlashCard card, FlashCardScore score)
    {
        if (pile != FlashPile.Hand)
            return false;
        if (score.HasTurnEndInHandEffect)
            return false;
        // The existing engine reads `card.HasKeyword(State, Ethereal)`, i.e. through Hook.ModifyKeywords:
        // HexPower grants Ethereal to every Hexed card, so a Hexed status/curse in hand leaves at the
        // turn end even without a local Ethereal keyword. The local keyword alone under-counts the
        // exception and makes the card look like persistent clutter.
        // Hook.ShouldEtherealTrigger has no supported listeners, so it is always true here.
        return FlashKernel.HasKeyword(battle, card, FlashKeywords.Ethereal);
    }

    private int ReplayPotentialValue(in FlashBattle battle)
    {
        int total = 0;
        for (int pile = 0; pile < 3; pile++)
        {
            FlashPile segment = pile switch
            {
                0 => FlashPile.Discard,
                1 => FlashPile.Draw,
                _ => FlashPile.Hand,
            };
            int start = FlashPiles.Start(battle, segment);
            int count = FlashPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly FlashCard card = ref battle.Cards[start + offset];
                FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
                int replayCount = Math.Max(0, FlashKernel.EnchantedReplayCount(card));
                if (replayCount == 0
                    || CardType(battle, card, stats) is not (FlashCardType.Attack or FlashCardType.Skill or FlashCardType.Power))
                    continue;
                FlashCardScore cardScore = _score.Card(card.Id, card.Upgrade);
                double perPlayValue = Math.Max(4d, BranchCardValue(battle, card, stats, cardScore));
                total += (int)Math.Ceiling(perPlayValue * replayCount);
                if (total >= SolverWeights.ReplayPotentialBeamCap)
                    return SolverWeights.ReplayPotentialBeamCap;
            }
        }
        return total;
    }

    // ---------------------------------------------------------------------------------------------
    // Projected threat.

    private readonly record struct ThreatProjection(
        int Hp, int DeathSaveHpRestored, int DeathSaveUseCount, int BlockConsumed = 0);

    private ThreatProjection ProjectHpAfterThreat(in FlashBattle battle)
    {
        // the existing search's ProjectHpAfterThreat walks the run-listener table, which still contains the
        // player's relics and cards after the player dies; Flash's PlayerHooksActive flag only gates the
        // combat dispatch. The projection must include those modifiers (Tungsten Rod's per-hit reduction
        // is the visible case) even on a dead snapshot, and it consumes Buffer stacks, so it runs on a
        // local copy: the evaluated state stays read-only through the `in` parameter.
        FlashBattle probe = battle;
        probe.Flags |= FlashBattleFlags.PlayerHooksActive;
        return ProjectHpAfterThreatCore(ref probe);
    }

    private ThreatProjection ProjectHpAfterThreatCore(ref FlashBattle probe)
    {
        FlashExec x = new(ref probe, Rules, default);
        int hp = probe.Player.CurrentHp;
        int block = probe.Player.Block;
        int originalBlock = block;
        // the existing search's ProjectHpAfterThreat: a one-stack Sandpit targeting this player expires at
        // the enemy side turn start and force-kills the player before the attack intent. Block and
        // one-shot death saves cannot make standing pat survive that boundary.
        if (_score.SandpitPowerId != ushort.MaxValue)
        {
            for (int index = 0; index < probe.PowerCount; index++)
            {
                ref readonly FlashPower power = ref probe.Powers[index];
                if (power.Id == _score.SandpitPowerId && power.Amount == 1
                    && power.Target == FlashLimits.PlayerOwner
                    && probe.Creature(power.Owner).IsAlive)
                {
                    return new ThreatProjection(0, 0, 0);
                }
            }
        }
        // the existing search's ProjectHpAfterThreat's ostyHp: the pet absorbs redirected powered attacks
        // until it dies; the remaining loss spills over to the player.
        int ostyHp = (probe.Flags & FlashBattleFlags.OstyPresent) != 0 ? probe.Osty.CurrentHp : 0;
        ProjectedDeathPrevention prevention = new(probe, _score, Rules);
        // the existing search's ProjectHpAfterThreat's gambitActive latch: the first unblocked damage from a
        // powered attack kills the owner through TheGambitPower, then death prevention may revive it.
        bool gambitActive = _score.TheGambitPowerId != ushort.MaxValue
            && GetAmount(probe, _score.TheGambitPowerId, FlashLimits.PlayerOwner) > 0;
        for (int position = 0; position < probe.RosterCount; position++)
        {
            ref readonly FlashEnemy enemy = ref probe.Enemies[probe.Roster[position]];
            byte owner = FlashKernel.EnemyOwner(probe.Roster[position]);
            if (!enemy.Creature.IsAlive)
                continue;
            if ((enemy.Flags & FlashEnemyFlags.SkipNextMove) != 0)
                continue;
            // SimulatedCombatState.TryGetForcedMoveId: about-to-blow has no damage intent; explode
            // projects its recorded damage once, without the normal move's attacks.
            if (enemy.SteamPhase == FlashSteamPhase.AboutToBlow)
                continue;
            if (enemy.SteamPhase == FlashSteamPhase.Explode)
            {
                ProjectThreatHit(ref x, owner, enemy.Ai[FlashKernel.SteamEruptionDamageRegister],
                    ref ostyHp, ref block, ref hp, ref gambitActive, ref prevention);
                continue;
            }
            FlashMonsterNode move = Rules.MonsterRule(enemy.Rules).Nodes[enemy.Move];
            for (int attackIndex = 0; attackIndex < move.Attacks.Length; attackIndex++)
            {
                FlashMonsterAttack attack = move.Attacks[attackIndex];
                FlashMonsterDef def = FlashRegistry.Monster(enemy.MonsterId);
                int baseDamage = def.ModifyAttackDamage(probe, Rules, owner, enemy.Move, attackIndex, attack.BaseDamage);
                int repeats = Math.Max(
                    def.ModifyAttackRepeats(probe, Rules, owner, enemy.Move, attackIndex, attack.Repeats), 1);
                for (int hit = 0; hit < repeats; hit++)
                {
                    ProjectThreatHit(ref x, owner, baseDamage, ref ostyHp, ref block, ref hp, ref gambitActive, ref prevention);
                }
            }
        }
        return new ThreatProjection(hp, prevention.DeathSaveHpRestored, prevention.UseCount,
            Math.Max(0, originalBlock - block));
    }

    private void ProjectThreatHit(ref FlashExec x, byte attacker, int baseDamage,
        ref int ostyHp, ref int block, ref int hp, ref bool gambitActive, ref ProjectedDeathPrevention prevention)
    {
        decimal modified = FlashHooks.ModifyDamage(
            ref x, FlashLimits.PlayerOwner, attacker, baseDamage, FlashValueProps.Move, false, default);
        int adjustedHit = Math.Max(0, (int)Math.Floor(modified));
        int blocked = Math.Min(block, adjustedHit);
        block -= blocked;

        FlashHpLossContext loss = new(
            FlashLimits.PlayerOwner, FlashValueProps.Move, attacker, false, default);
        Span<FlashListener> changed = stackalloc FlashListener[FlashHooks.MaxListenerCount * 2];
        decimal hpLoss = ProjectedModifyHpLost(
            ref x, loss, adjustedHit - blocked, afterOsty: false, changed, out _);
        // the existing search's ProjectThreatHit: Hook.ModifyUnblockedDamageTarget, then the engine's explicit
        // DieForYou fallback and dead-Osty guard, then the after-Osty modifiers on the chosen receiver.
        byte target = FlashHooks.ModifyUnblockedDamageTarget(
            ref x, FlashLimits.PlayerOwner, hpLoss, FlashValueProps.Move, attacker);
        if (target == FlashLimits.PlayerOwner
            && (x.B.Flags & FlashBattleFlags.OstyPresent) != 0
            && ostyHp > 0
            && GetAmount(x.B, _score.DieForYouPowerId, FlashLimits.OstyOwner) > 0)
        {
            target = FlashLimits.OstyOwner;
        }
        if (target == FlashLimits.OstyOwner && ostyHp <= 0)
            target = FlashLimits.PlayerOwner;
        FlashHpLossContext redirected = new(
            target, FlashValueProps.Move, attacker, false, default);
        hpLoss = ProjectedModifyHpLost(ref x, redirected, hpLoss, afterOsty: true, changed, out int changedCount);
        // The projected Buffer consumption is local to this forecast: notify the changed listeners so the
        // probe's Buffer amount drops, exactly as ProjectedHpLossModifiers.Consume does in the engine.
        FlashHooks.AfterModifyingHpLostAfterOsty(ref x, changed[..changedCount]);
        int damage = Math.Max(0, (int)Math.Floor(hpLoss));
        if (target == FlashLimits.OstyOwner)
        {
            int absorbed = Math.Min(ostyHp, damage);
            ostyHp -= absorbed;
            // Native redirected damage applies the original receiver's AfterOsty modifiers to the
            // overkill as a separate loss; the engine also filters Buffer there.
            Span<FlashListener> overflowChanges = stackalloc FlashListener[FlashHooks.MaxListenerCount * 2];
            FlashHpLossContext overflow = new(
                FlashLimits.PlayerOwner, FlashValueProps.Move, attacker, false, default);
            decimal overflowLoss = ProjectedModifyHpLost(
                ref x, overflow, damage - absorbed, afterOsty: true, overflowChanges, out int overflowCount);
            FlashHooks.AfterModifyingHpLostAfterOsty(ref x, overflowChanges[..overflowCount]);
            int playerLoss = Math.Max(0, (int)Math.Floor(overflowLoss));
            hp -= playerLoss;
            if (playerLoss > 0 && gambitActive)
            {
                gambitActive = false;
                hp = 0;
            }
            prevention.TryRevive(ref hp);
            return;
        }
        hp -= damage;
        if (damage > 0 && gambitActive)
        {
            gambitActive = false;
            hp = 0;
        }
        prevention.TryRevive(ref hp);
    }

    /// <summary>
    /// <see cref="FlashHooks.ModifyHpLost"/> with the engine's projected Buffer filter: a Buffer whose
    /// locally projected remaining count is exhausted no longer zeroes later hits.
    /// </summary>
    private decimal ProjectedModifyHpLost(ref FlashExec x, in FlashHpLossContext loss, decimal amount,
        bool afterOsty, scoped Span<FlashListener> changed, out int changedCount)
    {
        changedCount = 0;
        FlashHookMask normalMask = afterOsty ? FlashHookMask.ModifyHpLostAfterOsty : FlashHookMask.ModifyHpLostBeforeOsty;
        FlashHookMask lateMask = afterOsty ? FlashHookMask.ModifyHpLostAfterOstyLate : FlashHookMask.ModifyHpLostBeforeOstyLate;
        if ((FlashRegistry.AnyHooks & (normalMask | lateMask)) == 0)
            return amount;
        Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        for (int pass = 0; pass < 2; pass++)
        {
            int count = FlashHooks.CollectListeners(x.B, pass == 0 ? normalMask : lateMask, listeners);
            for (int index = 0; index < count; index++)
            {
                FlashListener listener = listeners[index];
                if (listener.Kind == FlashListenerKind.Power
                    && x.B.Powers[listener.Index].Id == _score.BufferPowerId
                    && x.B.Powers[listener.Index].Amount <= 0)
                {
                    continue;
                }
                FlashListenerDef def = FlashHooks.Definition(x.B, listener);
                decimal previous = amount;
                amount = (afterOsty, pass) switch
                {
                    (false, 0) => def.ModifyHpLostBeforeOsty(ref x, listener, loss, amount),
                    (false, _) => def.ModifyHpLostBeforeOstyLate(ref x, listener, loss, amount),
                    (true, 0) => def.ModifyHpLostAfterOsty(ref x, listener, loss, amount),
                    _ => def.ModifyHpLostAfterOstyLate(ref x, listener, loss, amount),
                };
                if (decimal.Truncate(previous) != decimal.Truncate(amount))
                    changed[changedCount++] = listener;
            }
        }
        return amount;
    }

    /// <summary>Port of <c>the existing search's BuildProjectedDeathPrevention</c>.</summary>
    private struct ProjectedDeathPrevention
    {
        private int _fairyCount;
        private readonly int _fairyHeal;
        private bool _lizardTailAvailable;
        private readonly int _lizardTailHeal;

        public ProjectedDeathPrevention(in FlashBattle battle, FlashScoreRules score, FlashRules rules)
        {
            _fairyCount = 0;
            if (score.FairyInABottlePotionId != ushort.MaxValue)
            {
                for (int slot = 0; slot < battle.PotionSlotCount; slot++)
                {
                    if (battle.Potions[slot] == score.FairyInABottlePotionId + 1)
                        _fairyCount++;
                }
            }
            _fairyHeal = (int)Math.Max(1m, battle.Player.MaxHp * 0.3m);
            _lizardTailAvailable = false;
            _lizardTailHeal = 0;
            if (score.LizardTailRelicId != ushort.MaxValue)
            {
                for (int index = 0; index < battle.RelicCount; index++)
                {
                    ref readonly FlashRelic relic = ref battle.Relics[index];
                    if (relic.Id != score.LizardTailRelicId || relic.Counter != 0
                        || (relic.Flags & FlashRelicFlags.Melted) != 0)
                    {
                        continue;
                    }
                    _lizardTailAvailable = true;
                    _lizardTailHeal = (int)Math.Max(1m,
                        battle.Player.MaxHp * (rules.RelicVar(relic.Id, 0) / 100m));
                    break;
                }
            }
            DeathSaveHpRestored = 0;
            UseCount = 0;
        }

        public int DeathSaveHpRestored { get; private set; }

        public int UseCount { get; private set; }

        public void TryRevive(ref int hp)
        {
            if (hp > 0)
                return;
            if (_fairyCount > 0)
            {
                _fairyCount--;
                hp = _fairyHeal;
                DeathSaveHpRestored += _fairyHeal;
                UseCount++;
                return;
            }
            if (!_lizardTailAvailable)
                return;
            _lizardTailAvailable = false;
            DeathSaveHpRestored += _lizardTailHeal;
            UseCount++;
            hp = _lizardTailHeal;
        }
    }

    private int MeasureDefensiveBlockReserve(in FlashBattle battle, int blockConsumed)
    {
        int consumed = Math.Clamp(blockConsumed, 0, Math.Max(0, battle.Player.Block));
        int remaining = Math.Max(0, battle.Player.Block - consumed);
        if (remaining == 0)
            return consumed;
        // ShouldClearBlock and BlockAfterPreventingClear are pure queries in every registered definition
        // (Calipers only reads its relic var; Barricade/Blur/Hive read owner equality; no implementation
        // writes x.B), and the FlashExec constructor only stores fields. The query therefore runs on the
        // read-only state without a 12 KiB copy; this is the only place a FlashExec wraps a battle that is
        // still reachable through an `in` parameter, and it performs no write.
        ref FlashBattle state = ref Unsafe.AsRef(in battle);
        FlashExec x = new(ref state, Rules, default);
        if (ShouldClearBlock(ref x, FlashLimits.PlayerOwner, out FlashListener preventer))
            return consumed;
        int retained = FlashHooks.Definition(state, preventer)
            .BlockAfterPreventingClear(ref x, preventer, FlashLimits.PlayerOwner, remaining);
        return consumed + Math.Min(Math.Max(0, retained), Math.Max(0, battle.Player.MaxHp));
    }

    private static bool ShouldClearBlock(ref FlashExec x, byte owner, out FlashListener preventer)
    {
        preventer = default;
        if ((FlashRegistry.AnyHooks & FlashHookMask.ShouldClearBlock) == 0)
            return true;
        Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        int count = FlashHooks.CollectListeners(x.B, FlashHookMask.ShouldClearBlock, listeners);
        for (int index = 0; index < count; index++)
        {
            if (!FlashHooks.Definition(x.B, listeners[index]).ShouldClearBlock(ref x, listeners[index], owner))
            {
                preventer = listeners[index];
                return false;
            }
        }
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // CorePowerSupport.TheHuntLongTermResourceValue (30): an exhausted TheHunt that did not kill its
    // target loses the presumed card-reward resource.
    private const int TheHuntLongTermResourceValue = 30;

    // the existing search's StateEvaluation.OrbRetentionValue. The existing engine applies
    // Hook.ModifyOrbValue (Focus) through OrbMirrors.GetPassiveValue; FlashKernel.OrbPassiveValue is
    // the same pure query over the Flash listener list.
    private int OrbRetentionValue(in FlashBattle battle, int aliveEnemyCount)
    {
        decimal value = 0m;
        for (int index = 0; index < battle.OrbCount; index++)
        {
            decimal passive = Math.Max(0m, FlashKernel.OrbPassiveValue(battle, Rules, index));
            value += passive * FlashRegistry.Orb(battle.Orbs[index].Id).RetentionMultiplier(battle, aliveEnemyCount);
        }
        return checked((int)Math.Ceiling(value));
    }

    // Threat focus (BuildThreatFocus).

    private readonly record struct ThreatFocus(
        uint? CombatId, int Pressure, int RemainingHp, int CurrentThreat, int TotalThreat, int IncomingHitCount);

    private ThreatFocus BuildThreatFocus(in FlashBattle battle)
    {
        uint? bestCombatId = null;
        int bestPressure = 0;
        int bestRemainingHp = int.MaxValue;
        int bestThreat = 0;
        int totalThreat = 0;
        int incomingHitCount = 0;
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            ref readonly FlashEnemy enemy = ref battle.Enemies[index];
            if ((enemy.Flags & FlashEnemyFlags.InCombat) == 0 || !enemy.Creature.IsAlive)
                continue;
            byte owner = FlashKernel.EnemyOwner((byte)index);
            int remainingHp = Math.Max(0, enemy.Creature.CurrentHp);
            int maxHp = Math.Max(1, enemy.Creature.MaxHp);
            int currentThreat = 0;
            if ((enemy.Flags & FlashEnemyFlags.SkipNextMove) == 0)
            {
                FlashMonsterNode move = Rules.MonsterRule(enemy.Rules).Nodes[enemy.Move];
                for (int attackIndex = 0; attackIndex < move.Attacks.Length; attackIndex++)
                {
                    FlashMonsterAttack attack = move.Attacks[attackIndex];
                    FlashMonsterDef def = FlashRegistry.Monster(enemy.MonsterId);
                    int damage = def.ModifyAttackDamage(battle, Rules, owner, enemy.Move, attackIndex, attack.BaseDamage);
                    int repeats = Math.Max(
                        def.ModifyAttackRepeats(battle, Rules, owner, enemy.Move, attackIndex, attack.Repeats), 1);
                    for (int hit = 0; hit < repeats; hit++)
                    {
                        incomingHitCount++;
                        currentThreat += Math.Max(0, damage);
                    }
                }
            }
            totalThreat += currentThreat;
            int progress = Math.Max(0, maxHp - remainingHp);
            if (progress == 0)
                continue;
            int pressure = (int)Math.Min(
                int.MaxValue,
                (long)progress * (16 + Math.Min(64, currentThreat)) * 1024 / maxHp);
            if (pressure < bestPressure
                || pressure == bestPressure && currentThreat < bestThreat
                || pressure == bestPressure && currentThreat == bestThreat && remainingHp >= bestRemainingHp)
            {
                continue;
            }
            bestCombatId = enemy.CombatId;
            bestPressure = pressure;
            bestRemainingHp = remainingHp;
            bestThreat = currentThreat;
        }
        return new ThreatFocus(bestCombatId, bestPressure, bestRemainingHp, bestThreat, totalThreat, incomingHitCount);
    }

    // ---------------------------------------------------------------------------------------------
    // Strategic effects (StrategicEffectModel).

    private readonly record struct StrategicEffectVector(
        int DamagePotential, int PreventionPotential, int ResourcePotential, int CardAccessPotential, int ScalingPotential)
    {
        public int RetentionValue => (int)Math.Clamp(
            (long)DamagePotential + PreventionPotential + ResourcePotential + CardAccessPotential + ScalingPotential,
            0L,
            int.MaxValue);

        public static StrategicEffectVector operator +(
            StrategicEffectVector left, StrategicEffectVector right)
            => new(
                SaturatingAdd(left.DamagePotential, right.DamagePotential),
                SaturatingAdd(left.PreventionPotential, right.PreventionPotential),
                SaturatingAdd(left.ResourcePotential, right.ResourcePotential),
                SaturatingAdd(left.CardAccessPotential, right.CardAccessPotential),
                SaturatingAdd(left.ScalingPotential, right.ScalingPotential));

        private static int SaturatingAdd(int left, int right)
            => (int)Math.Clamp((long)left + right, 0L, int.MaxValue);
    }

    private readonly record struct StrategicEffectContext(
        int EnemyHp, int IncomingDamage, int IncomingHitCount, int RemainingTurns, int UsefulCardPlays,
        int AttackPlays, int SkillPlays, int BlockSkillPlays, int PowerPlays, int ExhaustPlays, int ShivPlays,
        int DebuffApplications, int SkillEnergySpend, int PowerEnergySpend, int AverageCardValue,
        int BestCardValue, int AverageAttackValue, int StatusDrawTriggers)
    {
        public int? AttackHits { get; init; }
        public int? ExhaustDrawPlays { get; init; }
        public int ReachableCards { get; init; }

        /// <summary><c>SearchPolicySnapshot.Act3BossStrategy</c> frozen in the score table.</summary>
        public bool Act3BossInteractions { get; init; }

        /// <summary><c>CaptureFirstAttackDamage</c>: the best payable attack's total first-hit damage.</summary>
        public int FirstAttackDamage { get; init; }

        /// <summary><c>CaptureAct3BossInteractionPotential</c> values (zero when not computed).</summary>
        public int EtherealDrawTriggers { get; init; }
        public int PagestormBonusDrawCapacity { get; init; }
        public int HighEnergyPlays { get; init; }
        public int DemesneEnergyGain { get; init; }
        public int DemesneDrawGain { get; init; }

        /// <summary>Automation/Orbit recurring energy from the refund window (StrategicEffectModel).</summary>
        public int RecurringEnergyGain { get; init; }
    }

    private static bool Contributes(in FlashPower power, FlashPowerScore rules)
        => power.Amount > 0
            && !rules.IsTemporary
            && rules.Type == FlashPowerType.Buff;

    private StrategicEffectContext BuildStrategicContext(
        in FlashBattle battle, int enemyHp, int incomingDamage, int incomingHitCount,
        FlashStrategicRequirements requirements, bool skillsExhaust)
    {
        if (requirements == FlashStrategicRequirements.None)
        {
            return new StrategicEffectContext(
                Math.Max(0, enemyHp), Math.Max(0, incomingDamage), Math.Max(0, incomingHitCount),
                1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0);
        }

        const FlashStrategicRequirements reachablePlayRequirements =
            FlashStrategicRequirements.UsefulCardPlays
            | FlashStrategicRequirements.AttackPlays
            | FlashStrategicRequirements.SkillPlays
            | FlashStrategicRequirements.BlockSkillPlays
            | FlashStrategicRequirements.PowerPlays
            | FlashStrategicRequirements.ExhaustPlays
            | FlashStrategicRequirements.ShivPlays
            | FlashStrategicRequirements.DebuffApplications
            | FlashStrategicRequirements.StatusDrawTriggers
            | FlashStrategicRequirements.AttackHits;
        bool needsRemainingTurns = requirements.HasFlag(FlashStrategicRequirements.RemainingTurns)
            || (requirements & reachablePlayRequirements) != 0;
        bool needsAllCardValues = requirements.HasFlag(FlashStrategicRequirements.AverageCardValue)
            || requirements.HasFlag(FlashStrategicRequirements.BestCardValue);
        bool needsAttackValues = needsRemainingTurns
            || requirements.HasFlag(FlashStrategicRequirements.AverageAttackValue);
        bool needsAttackCount = requirements.HasFlag(FlashStrategicRequirements.AttackPlays)
            || requirements.HasFlag(FlashStrategicRequirements.AverageAttackValue);
        bool needsSkillCount = requirements.HasFlag(FlashStrategicRequirements.SkillPlays);
        bool needsBlockSkillCount = requirements.HasFlag(FlashStrategicRequirements.BlockSkillPlays);
        bool needsPowerCount = requirements.HasFlag(FlashStrategicRequirements.PowerPlays);
        bool needsExhaustCount = requirements.HasFlag(FlashStrategicRequirements.ExhaustPlays);
        bool needsShivCount = requirements.HasFlag(FlashStrategicRequirements.ShivPlays)
            || requirements.HasFlag(FlashStrategicRequirements.AttackHits);
        bool needsDebuffCount = requirements.HasFlag(FlashStrategicRequirements.DebuffApplications);
        bool needsStatusCount = requirements.HasFlag(FlashStrategicRequirements.StatusDrawTriggers);
        bool needsSkillEnergy = requirements.HasFlag(FlashStrategicRequirements.SkillEnergySpend);
        bool needsPowerEnergy = requirements.HasFlag(FlashStrategicRequirements.PowerEnergySpend);

        int attackCount = 0, attackHitCount = 0, skillCount = 0, blockSkillCount = 0;
        int powerCount = 0, exhaustCount = 0, shivCount = 0, reusableShivCount = 0;
        int generatedShivCount = 0, shivGeneratorCount = 0;
        int singleUseGeneratedShivs = 0, singleUseShivGenerators = 0;
        int debuffCount = 0, statusCount = 0, skillEnergy = 0, powerEnergy = 0;
        int totalCardValue = 0, bestCardValue = 0, totalAttackValue = 0;
        int liveCount = battle.DiscardCount + battle.DrawCount + battle.HandCount;
        for (int pile = 0; pile < 3; pile++)
        {
            FlashPile segment = pile switch
            {
                0 => FlashPile.Discard,
                1 => FlashPile.Draw,
                _ => FlashPile.Hand,
            };
            int start = FlashPiles.Start(battle, segment);
            int count = FlashPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly FlashCard card = ref battle.Cards[start + offset];
                FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
                FlashCardScore cardScore = _score.Card(card.Id, card.Upgrade);
                FlashCardType cardType = CardType(battle, card, stats);
                int cardValue = 0;
                if (needsAllCardValues || (needsAttackValues && cardType == FlashCardType.Attack))
                {
                    cardValue = Math.Max(1, (int)Math.Ceiling(CardValue(battle, card, stats, cardScore)));
                    if (needsAllCardValues)
                    {
                        totalCardValue += cardValue;
                        bestCardValue = Math.Max(bestCardValue, cardValue);
                    }
                    if (needsAttackValues && cardType == FlashCardType.Attack)
                        totalAttackValue += cardValue;
                }
                switch (cardType)
                {
                    case FlashCardType.Attack:
                        if (needsAttackCount)
                            attackCount++;
                        if (requirements.HasFlag(FlashStrategicRequirements.AttackHits)
                            && !cardScore.HasOstyAttackTag && !cardScore.HasShivTag)
                        {
                            attackHitCount += AttackHits(battle, cardScore, card, stats);
                        }
                        break;
                    case FlashCardType.Skill:
                        if (needsSkillCount)
                            skillCount++;
                        if (needsSkillEnergy)
                            skillEnergy += EnergyCost(battle, card, stats, segment);
                        if (needsBlockSkillCount && cardScore.HasBlockDynamicVar)
                            blockSkillCount++;
                        break;
                    case FlashCardType.Power:
                        if (needsPowerCount)
                            powerCount++;
                        if (needsPowerEnergy)
                            powerEnergy += EnergyCost(battle, card, stats, segment);
                        break;
                    case FlashCardType.Status:
                        if (needsStatusCount)
                            statusCount++;
                        break;
                }
                bool hasExhaustKeyword = (card.Keywords & FlashKeywords.Exhaust) != 0;
                bool exhaustsOnPlay = hasExhaustKeyword
                    || skillsExhaust && cardType == FlashCardType.Skill;
                if (needsExhaustCount && exhaustsOnPlay)
                    exhaustCount++;
                if (needsShivCount)
                {
                    if (cardScore.HasShivTag)
                    {
                        shivCount++;
                        if (!hasExhaustKeyword)
                            reusableShivCount++;
                    }
                    int generated = ImmediateShivSupply(battle, cardScore, card, stats);
                    generatedShivCount += generated;
                    if (generated > 0)
                        shivGeneratorCount++;
                    if (generated > 0 && (cardType == FlashCardType.Power
                        || skillsExhaust && cardType == FlashCardType.Skill
                        || hasExhaustKeyword))
                    {
                        singleUseGeneratedShivs += generated;
                        singleUseShivGenerators++;
                    }
                }
                if (needsDebuffCount && cardScore.HasDebuffDynamicVar)
                    debuffCount++;
            }
        }

        int actualDeckSize = liveCount;
        int deckSize = Math.Max(1, actualDeckSize);
        int remainingTurns = 1;
        int reachableCards = 0;
        if (needsRemainingTurns)
        {
            int cardsPerTurn = Math.Min(10, Math.Max(1, Math.Min(5, deckSize)));
            int damagePerCycle = Math.Max(1, totalAttackValue);
            int estimatedCycles = (int)Math.Ceiling((double)Math.Max(1, enemyHp) / damagePerCycle);
            remainingTurns = Math.Clamp(
                estimatedCycles * Math.Max(1, (int)Math.Ceiling(deckSize / 5d)),
                1,
                SolverWeights.SetupValueHorizonTurns);
            reachableCards = actualDeckSize == 0
                ? 0
                : Math.Min(deckSize * 2, remainingTurns * cardsPerTurn);
        }
        int attackPlays = requirements.HasFlag(FlashStrategicRequirements.AttackPlays)
            ? ReachablePlays(attackCount, deckSize, reachableCards) : 0;
        int skillPlays = requirements.HasFlag(FlashStrategicRequirements.SkillPlays)
            ? ReachablePlays(skillCount, deckSize, reachableCards) : 0;
        int blockSkillPlays = requirements.HasFlag(FlashStrategicRequirements.BlockSkillPlays)
            ? ReachablePlays(blockSkillCount, deckSize, reachableCards) : 0;
        int powerPlays = requirements.HasFlag(FlashStrategicRequirements.PowerPlays)
            ? Math.Min(powerCount, reachableCards) : 0;
        int exhaustPlays = requirements.HasFlag(FlashStrategicRequirements.ExhaustPlays)
            ? Math.Min(exhaustCount, reachableCards) : 0;
        int shivPlays = requirements.HasFlag(FlashStrategicRequirements.ShivPlays)
            ? EstimatedShivPlays(shivCount, generatedShivCount, shivGeneratorCount, deckSize, reachableCards,
                reusableShivCount, singleUseGeneratedShivs, singleUseShivGenerators)
            : 0;
        int debuffApplications = requirements.HasFlag(FlashStrategicRequirements.DebuffApplications)
            ? ReachablePlays(debuffCount, deckSize, reachableCards) : 0;
        return new StrategicEffectContext(
            Math.Max(0, enemyHp),
            Math.Max(0, incomingDamage),
            Math.Max(0, incomingHitCount),
            remainingTurns,
            reachableCards,
            attackPlays,
            skillPlays,
            blockSkillPlays,
            powerPlays,
            exhaustPlays,
            shivPlays,
            debuffApplications,
            skillEnergy,
            powerEnergy,
            requirements.HasFlag(FlashStrategicRequirements.AverageCardValue)
                ? Math.Max(1, totalCardValue / deckSize) : 1,
            requirements.HasFlag(FlashStrategicRequirements.BestCardValue)
                ? Math.Max(1, bestCardValue) : 1,
            requirements.HasFlag(FlashStrategicRequirements.AverageAttackValue) && attackCount > 0
                ? Math.Max(1, totalAttackValue / attackCount) : 0,
            requirements.HasFlag(FlashStrategicRequirements.StatusDrawTriggers)
                ? Math.Min(remainingTurns, ReachablePlays(statusCount, deckSize, reachableCards)) : 0)
        {
            AttackHits = requirements.HasFlag(FlashStrategicRequirements.AttackHits)
                ? EstimatedAttackHits(attackHitCount, shivCount, generatedShivCount,
                    shivGeneratorCount, deckSize, reachableCards, reusableShivCount,
                    singleUseGeneratedShivs, singleUseShivGenerators)
                : null,
            ReachableCards = reachableCards,
        };
    }

    /// <summary>
    /// Port of <c>the existing search's FreeCardOpportunityValue</c> for FreeAttackPower: for each playable
    /// Attack in hand without X cost, the local cost minus the hook-modified cost times 16 plus a card
    /// value bonus; the best <c>freeUses</c> values are summed.
    /// </summary>
    private int FreeCardOpportunityValue(in FlashBattle battle, FlashCardType cardType, ushort powerId)
    {
        int freeUses = GetAmount(battle, powerId, FlashLimits.PlayerOwner);
        if (freeUses <= 0)
            return 0;
        Span<int> values = stackalloc int[FlashLimits.MaxHandSize];
        int count = 0;
        for (int index = 0; index < battle.HandCount; index++)
        {
            ref readonly FlashCard card = ref battle.Cards[index];
            FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
            if (CardType(battle, card, stats) != cardType
                || (card.Flags & FlashCardFlags.CostsX) != 0
                || !FlashKernel.CanPlayCard(battle, Rules, index))
            {
                continue;
            }
            int normalCost = Math.Max(0, FlashKernel.EnergyCostWithLocalModifiers(battle, card));
            int currentCost = Math.Max(0, FlashKernel.EnergyCostWithModifiers(battle, Rules, card, FlashPile.Hand));
            int savedEnergy = Math.Max(0, normalCost - currentCost);
            double cardValue = CardValue(battle, card, stats, _score.Card(card.Id, card.Upgrade));
            values[count++] = savedEnergy * 16
                + Math.Min(16, Math.Max(1, (int)Math.Ceiling(cardValue)));
        }
        for (int index = 1; index < count; index++)
        {
            int value = values[index];
            int position = index;
            while (position > 0 && values[position - 1] < value)
            {
                values[position] = values[position - 1];
                position--;
            }
            values[position] = value;
        }
        int total = 0;
        for (int index = 0; index < Math.Min(freeUses, count); index++)
            total += values[index];
        return total;
    }

    /// <summary>
    /// Port of <c>the existing search's VoidFormOpportunityValue</c>: while VoidFormPower has free plays
    /// left, every playable hand card without X cost saves its local cost minus the hook-modified cost
    /// times 16 plus its star saving times 8 plus a card-value bonus; the best <c>freeUses</c> values
    /// are summed. <see cref="FlashPower.Aux0"/> is the power's CardsPlayedThisTurn counter.
    /// </summary>
    private int VoidFormOpportunityValue(in FlashBattle battle)
    {
        if (_score.VoidFormPowerId == ushort.MaxValue)
            return 0;
        int powerIndex = FlashKernel.FindPower(battle, FlashLimits.PlayerOwner, _score.VoidFormPowerId);
        if (powerIndex < 0)
            return 0;
        int freeUses = battle.Powers[powerIndex].Amount - battle.Powers[powerIndex].Aux0;
        if (freeUses <= 0)
            return 0;
        Span<int> values = stackalloc int[FlashLimits.MaxHandSize];
        int count = 0;
        for (int hand = 0; hand < battle.HandCount; hand++)
        {
            ref readonly FlashCard card = ref battle.Cards[hand];
            FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
            if (stats.CostsX || stats.StarCostsX || !FlashKernel.CanPlayCard(battle, Rules, hand))
                continue;
            int normalEnergy = Math.Max(0, FlashKernel.EnergyCostWithLocalModifiers(battle, card));
            int currentEnergy = Math.Max(0, FlashKernel.EnergyCostWithModifiers(battle, Rules, card, FlashPile.Hand));
            int normalStars = Math.Max(0, stats.StarCost);
            int currentStars = Math.Max(0, FlashKernel.StarCostWithModifiers(battle, Rules, card, FlashPile.Hand));
            double cardValue = CardValue(battle, card, stats, _score.Card(card.Id, card.Upgrade));
            values[count++] = Math.Max(0, normalEnergy - currentEnergy) * 16
                + Math.Max(0, normalStars - currentStars) * 8
                + Math.Min(16, Math.Max(1, (int)Math.Ceiling(cardValue)));
        }
        for (int index = 1; index < count; index++)
        {
            int value = values[index];
            int position = index;
            while (position > 0 && values[position - 1] < value)
            {
                values[position] = values[position - 1];
                position--;
            }
            values[position] = value;
        }
        int total = 0;
        for (int index = 0; index < Math.Min(freeUses, count); index++)
            total += values[index];
        return total;
    }

    /// <summary>
    /// Port of <c>the existing search's CaptureEnergyRefundWindow</c> for Automation's recurring energy:
    /// the energy the remaining live cards can consume this cycle, and the capacity left after the
    /// current energy plus future turn resets. <c>liveCards</c> is every combat card in pile order.
    /// </summary>
    private (int Spend, int Capacity) CaptureEnergyRefundWindow(in FlashBattle battle,
        StrategicEffectContext context, bool skillsExhaust)
    {
        // the existing search's CaptureEnergyRefundWindow's liveCards are Discard, Draw and Hand only;
        // the played and exhausted piles do not participate in the projected cycle.
        int liveCount = battle.HandCount + battle.DrawCount + battle.DiscardCount;
        if (liveCount == 0 || context.ReachableCards == 0)
            return (0, 0);
        // PersistentPowerSupport.GetModifiedMaxEnergy: WasteAway and other ModifyMaxEnergy listeners
        // reduce the future-turn energy supply. Probe a copy because the evaluation must not mutate
        // the battle being scored.
        FlashBattle probe = battle;
        FlashExec probeExec = new(ref probe, Rules, default);
        int maxEnergy = Math.Max(0, FlashKernel.ModifiedMaxEnergy(ref probeExec));
        int handCount = battle.HandCount;
        int futureTurns = Math.Min(Math.Max(0, context.RemainingTurns - 1),
            (Math.Max(0, context.ReachableCards - handCount) + 5 - 1) / 5);
        long energySupply = Math.Max(0, battle.Energy) + (long)maxEnergy * futureTurns;
        double visits = (double)context.ReachableCards / liveCount;
        double energyDemand = 0;
        for (int pile = 0; pile < 3; pile++)
        {
            FlashPile segment = pile switch
            {
                0 => FlashPile.Discard,
                1 => FlashPile.Draw,
                _ => FlashPile.Hand,
            };
            int start = FlashPiles.Start(battle, segment);
            int count = FlashPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly FlashCard card = ref battle.Cards[start + offset];
                FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
                if (card.HasKeyword(FlashKeywords.Unplayable) || skillsExhaust && CardType(battle, card, stats) == FlashCardType.Skill)
                    continue;
                int cost = (card.Flags & FlashCardFlags.CostsX) != 0
                    ? maxEnergy
                    : Math.Max(0, FlashKernel.EnergyCostWithLocalModifiers(battle, card));
                bool singleUse = CardType(battle, card, stats) == FlashCardType.Power || card.HasKeyword(FlashKeywords.Exhaust);
                energyDemand += cost * (singleUse ? Math.Min(1, visits) : visits);
            }
        }
        long demand = (long)Math.Ceiling(energyDemand);
        return ((int)Math.Min(int.MaxValue, Math.Min(energySupply, demand)),
            (int)Math.Min(int.MaxValue, Math.Max(0, demand - energySupply)));
    }

    /// <summary>
    /// <c>PersistentPowerSupport.GetModifiedMaxEnergy</c> through a copy, so the evaluation never mutates
    /// the battle being scored. <c>WasteAway</c> and other listeners lower the future-turn supply.
    /// </summary>
    private int ModifiedMaxEnergy(in FlashBattle battle)
    {
        FlashBattle probe = battle;
        FlashExec probeExec = new(ref probe, Rules, default);
        return Math.Max(0, FlashKernel.ModifiedMaxEnergy(ref probeExec));
    }

    /// <summary>
    /// <c>PersistentPowerSupport.GetModifiedHandDraw</c> through a copy. The base draw is the game's
    /// <c>CombatManager.baseHandDrawCount</c> (5), the same literal the existing capture uses.
    /// </summary>
    /// <summary><c>PersistentPowerSupport.GetModifiedHandDraw</c> for the commitment evidence port.</summary>
    internal int ModifiedHandDrawForEvidence(in FlashBattle battle) => ModifiedHandDraw(battle);

    /// <summary><c>PersistentPowerSupport.GetModifiedMaxEnergy</c> for the commitment evidence port.</summary>
    internal int ModifiedMaxEnergyForEvidence(in FlashBattle battle)
    {
        FlashBattle probe = battle;
        FlashExec probeExec = new(ref probe, Rules, default);
        return Math.Max(0, FlashKernel.ModifiedMaxEnergy(ref probeExec));
    }

    /// <summary><c>CardChoiceSupport.CardValue</c> of a live card, with its current variables.</summary>
    internal double CardValueForEvidence(in FlashBattle battle, in FlashCard card)
    {
        FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
        FlashCardScore score = _score.Cards[card.Id]?[card.Upgrade]
            ?? throw new InvalidOperationException($"评分表缺少卡牌 {card.Id}+{card.Upgrade}。");
        return CardValue(battle, card, stats, score);
    }

    /// <summary>A named dynamic variable's current value for the commitment evidence port.</summary>
    internal int VarForEvidence(in FlashBattle battle, in FlashCard card, int index)
    {
        FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
        FlashCardScore score = CardScoreForEvidence(card.Id, card.Upgrade);
        return (int)Var(battle, card, stats, index);
    }

    /// <summary><c>CardMechanismFacts.AttackHits</c> for the commitment evidence port.</summary>
    internal int AttackHitsForEvidence(in FlashBattle battle, in FlashCard card)
    {
        FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
        FlashCardScore score = CardScoreForEvidence(card.Id, card.Upgrade);
        return AttackHits(battle, score, card, stats);
    }

    /// <summary><c>CardMechanismFacts.ImmediateShivSupply</c> for the commitment evidence port.</summary>
    internal int ImmediateShivSupplyForEvidence(in FlashBattle battle, in FlashCard card)
    {
        FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
        FlashCardScore score = CardScoreForEvidence(card.Id, card.Upgrade);
        return ImmediateShivSupply(battle, score, card, stats);
    }

    /// <summary>The compiled per-card score definition, for variable indexes and block/debuff flags.</summary>
    internal FlashCardScore CardScoreForEvidence(ushort id, int upgrade)
        => _score.Cards[id]?[upgrade]
            ?? throw new InvalidOperationException($"评分表缺少卡牌 {id}+{upgrade}。");

    private int ModifiedHandDraw(in FlashBattle battle)
    {
        FlashBattle probe = battle;
        FlashExec probeExec = new(ref probe, Rules, default);
        return Math.Max(0, FlashKernel.ModifiedHandDraw(ref probeExec, 5));
    }

    /// <summary>
    /// Port of <c>the existing search's CaptureFirstAttackDamage</c>: the best total damage of a payable
    /// Attack in the live piles, counting every <c>Eradicate</c> hit the available energy pays for.
    /// The energy ceiling is the current energy or the modified maximum, whichever is larger.
    /// </summary>
    private int CaptureFirstAttackDamage(in FlashBattle battle)
    {
        int energy = Math.Max(battle.Energy, ModifiedMaxEnergy(battle));
        int best = 0;
        for (int pile = 0; pile < 3; pile++)
        {
            FlashPile segment = pile switch
            {
                0 => FlashPile.Discard,
                1 => FlashPile.Draw,
                _ => FlashPile.Hand,
            };
            int start = FlashPiles.Start(battle, segment);
            int count = FlashPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly FlashCard card = ref battle.Cards[start + offset];
                FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
                if (CardType(battle, card, stats) != FlashCardType.Attack || card.HasKeyword(FlashKeywords.Unplayable))
                    continue;
                FlashCardScore score = _score.Card(card.Id, card.Upgrade);
                if (score.HasOstyAttackTag)
                    continue;
                if (FlashKernel.EnergyCostWithModifiers(battle, Rules, card, segment) > energy)
                    continue;
                int hits = _score.EradicateCardId != ushort.MaxValue && card.Id == _score.EradicateCardId
                    ? Math.Max(0, energy)
                    : AttackHits(battle, score, card, stats);
                int damage = (int)Math.Floor(Var(battle, card, stats, score.DamageVar));
                best = Math.Max(best, damage * hits);
            }
        }
        return best;
    }

    /// <summary>
    /// Port of <c>the existing search's CaptureAct3BossInteractionPotential</c>: Pagestorm's future ethereal
    /// draw triggers and unused bonus-draw capacity, Danse Macabre's affordable high-energy plays, and
    /// Demesne's future energy and draw gains. Values stay zero when the matching power did not
    /// contribute or the projected cycle cannot realize the interaction.
    /// </summary>
    private (int EtherealDrawTriggers, int PagestormBonusDrawCapacity, int HighEnergyPlays,
        int DemesneEnergyGain, int DemesneDrawGain) CaptureAct3BossInteractionPotential(
            in FlashBattle battle,
            int remainingTurns,
            int reachableCards,
            bool hasPagestorm,
            bool skillsExhaust,
            int demesneAmount,
            int danseMacabreEnergyThreshold)
    {
        int boundedTurns = Math.Max(1, remainingTurns);
        int boundedReachableCards = Math.Max(0, reachableCards);
        int liveCount = battle.HandCount + battle.DrawCount + battle.DiscardCount;
        int etherealDrawTriggers = 0;
        int pagestormBonusDrawCapacity = 0;
        if (hasPagestorm)
        {
            int futurePileCards = battle.DrawCount + battle.DiscardCount;
            int futureEtherealCards = 0;
            for (int pile = 0; pile < 2; pile++)
            {
                FlashPile segment = pile == 0 ? FlashPile.Draw : FlashPile.Discard;
                int start = FlashPiles.Start(battle, segment);
                int count = FlashPiles.Count(battle, segment);
                for (int offset = 0; offset < count; offset++)
                {
                    // the existing search's StateEvaluation uses `HasKeyword(State, Ethereal)` here too,
                    // so a Hexed card in the draw/discard pile is part of Pagestorm's projection.
                    if (FlashKernel.HasKeyword(battle, battle.Cards[start + offset], FlashKeywords.Ethereal))
                        futureEtherealCards++;
                }
            }
            int drawPerTurn = ModifiedHandDraw(battle);
            if (futurePileCards > 0 && futureEtherealCards > 0 && drawPerTurn > 0)
            {
                int reachableFutureDraws = (int)Math.Min(
                    (long)futurePileCards * 2,
                    boundedReachableCards);
                etherealDrawTriggers = (int)Math.Min(
                    (long)futureEtherealCards * 2,
                    ((long)futureEtherealCards * reachableFutureDraws
                        + futurePileCards - 1) / futurePileCards);
                if (!HasUnmeltedFiddle(battle))
                {
                    int maxHandSize = battle.MaxHandSize;
                    int retainedCards = 0;
                    for (int offset = 0; offset < battle.HandCount; offset++)
                    {
                        if (ShouldRetainThisTurn(battle.Cards[offset]))
                            retainedCards++;
                    }
                    int firstTurnSpace = Math.Max(0, maxHandSize - retainedCards);
                    int firstTurnBonusSpace = Math.Max(
                        0,
                        firstTurnSpace - Math.Min(firstTurnSpace, drawPerTurn));
                    int laterTurnBonusSpace = Math.Max(
                        0,
                        maxHandSize - Math.Min(maxHandSize, drawPerTurn));
                    pagestormBonusDrawCapacity = (int)Math.Min(
                        (long)futurePileCards * 2,
                        (long)firstTurnBonusSpace
                            + (long)Math.Max(0, boundedTurns - 1) * laterTurnBonusSpace);
                }
            }
        }

        int highEnergyPlays = 0;
        if (danseMacabreEnergyThreshold > 0 && liveCount > 0)
        {
            Span<int> highEnergyCosts = stackalloc int[liveCount * 2];
            int costCount = 0;
            int highEnergyCards = 0;
            for (int pile = 0; pile < 3; pile++)
            {
                FlashPile segment = pile switch
                {
                    0 => FlashPile.Discard,
                    1 => FlashPile.Draw,
                    _ => FlashPile.Hand,
                };
                int start = FlashPiles.Start(battle, segment);
                int count = FlashPiles.Count(battle, segment);
                for (int offset = 0; offset < count; offset++)
                {
                    ref readonly FlashCard card = ref battle.Cards[start + offset];
                    FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
                    if (card.HasKeyword(FlashKeywords.Unplayable))
                        continue;
                    int cost = FlashKernel.EnergyCostWithModifiers(battle, Rules, card, segment);
                    if (cost < danseMacabreEnergyThreshold)
                        continue;
                    highEnergyCards++;
                    highEnergyCosts[costCount++] = cost;
                    bool singleUse = CardType(battle, card, stats) == FlashCardType.Power
                        || card.HasKeyword(FlashKeywords.Exhaust)
                        || skillsExhaust && CardType(battle, card, stats) == FlashCardType.Skill;
                    if (!singleUse)
                        highEnergyCosts[costCount++] = cost;
                }
            }
            int reachableHighEnergyCards = (int)Math.Min(
                costCount,
                ((long)highEnergyCards * boundedReachableCards
                    + liveCount - 1) / liveCount);
            int futureMaxEnergy = ModifiedMaxEnergy(battle);
            Span<int> turnEnergy = stackalloc int[boundedTurns];
            turnEnergy[0] = Math.Max(0, battle.Energy);
            turnEnergy[1..].Fill(Math.Max(0, futureMaxEnergy));
            highEnergyCosts[..costCount].Sort();
            for (int index = 0;
                 index < reachableHighEnergyCards;
                 index++)
            {
                for (int turn = 0; turn < turnEnergy.Length; turn++)
                {
                    if (turnEnergy[turn] < highEnergyCosts[index]) continue;
                    turnEnergy[turn] -= highEnergyCosts[index];
                    highEnergyPlays++;
                    break;
                }
            }
        }

        int demesneEnergyGain = 0, demesneDrawGain = 0;
        int futureTurns = Math.Max(0, boundedTurns - 1);
        if (demesneAmount > 0 && futureTurns > 0 && liveCount > 0)
        {
            long deckEnergyDemand = 0;
            for (int pile = 0; pile < 3; pile++)
            {
                FlashPile segment = pile switch
                {
                    0 => FlashPile.Discard,
                    1 => FlashPile.Draw,
                    _ => FlashPile.Hand,
                };
                int start = FlashPiles.Start(battle, segment);
                int count = FlashPiles.Count(battle, segment);
                for (int offset = 0; offset < count; offset++)
                {
                    ref readonly FlashCard card = ref battle.Cards[start + offset];
                    if (card.HasKeyword(FlashKeywords.Unplayable))
                        continue;
                    deckEnergyDemand += Math.Max(0,
                        FlashKernel.EnergyCostWithModifiers(battle, Rules, card, segment));
                }
            }
            long reachableEnergyDemand = (deckEnergyDemand * boundedReachableCards
                + liveCount - 1) / liveCount;
            int baseEnergy = Math.Max(0, ModifiedMaxEnergy(battle) - demesneAmount);
            long baseEnergySupply = Math.Max(0, battle.Energy) + (long)baseEnergy * futureTurns;
            demesneEnergyGain = (int)Math.Clamp(reachableEnergyDemand - baseEnergySupply,
                0L, (long)demesneAmount * futureTurns);
            int baseDraw = Math.Max(0, ModifiedHandDraw(battle) - demesneAmount);
            int retained = 0;
            for (int offset = 0; offset < battle.HandCount; offset++)
            {
                if (ShouldRetainThisTurn(battle.Cards[offset]))
                    retained++;
            }
            int firstSpace = Math.Max(0, battle.MaxHandSize - retained - baseDraw);
            int laterSpace = Math.Max(0, battle.MaxHandSize - baseDraw);
            demesneDrawGain = (int)Math.Min((long)liveCount * 2,
                Math.Min(demesneAmount, firstSpace)
                + (long)Math.Max(0, futureTurns - 1) * Math.Min(demesneAmount, laterSpace));
        }
        return (etherealDrawTriggers, pagestormBonusDrawCapacity, highEnergyPlays,
            demesneEnergyGain, demesneDrawGain);
    }

    private static bool ShouldRetainThisTurn(in FlashCard card)
        => (card.Flags & FlashCardFlags.RetainThisTurn) != 0
            || (card.Keywords & FlashKeywords.Retain) != 0;

    /// <summary><c>combat.RelicsOf(player).Any(relic =&gt; relic is Fiddle &amp;&amp; !relic.IsMelted)</c>.</summary>
    private bool HasUnmeltedFiddle(in FlashBattle battle)
    {
        if (_score.FiddleRelicId == ushort.MaxValue)
            return false;
        for (int index = 0; index < battle.RelicCount; index++)
        {
            ref readonly FlashRelic relic = ref battle.Relics[index];
            if (relic.Id == _score.FiddleRelicId
                && (relic.Flags & FlashRelicFlags.Melted) == 0)
            {
                return true;
            }
        }
        return false;
    }

    private int EnergyCost(in FlashBattle battle, in FlashCard card, FlashCardStats stats, FlashPile pile)
    {
        if ((card.Flags & FlashCardFlags.CostsX) != 0)
            return 0;
        // StrategicEffectModel.EnergyCost reads CostModifiers.All (local only), not the combat hooks.
        return Math.Max(0, FlashKernel.EnergyCostWithLocalModifiers(battle, card));
    }

    private static int ReachablePlays(int matchingCards, int deckSize, int reachableCards)
        => matchingCards == 0
            ? 0
            : Math.Max(1, (int)Math.Ceiling((double)matchingCards * reachableCards / deckSize));

    private static int AttackHits(in FlashBattle battle, FlashCardScore score, in FlashCard card,
        FlashCardStats stats)
        => score.AttackHits switch
        {
            FlashAttackHitKind.Two => 2,
            FlashAttackHitKind.RepeatVar => Math.Max(0, (int)Var(battle, card, stats, score.RepeatVar)),
            _ => 1,
        };

    private static int ImmediateShivSupply(in FlashBattle battle, FlashCardScore score, in FlashCard card,
        FlashCardStats stats)
        => score.ShivSupply switch
        {
            FlashShivSupplyKind.CardsVar => Math.Max(0, (int)Var(battle, card, stats, score.ShivCardsVar)),
            FlashShivSupplyKind.ShivsVar => Math.Max(0, (int)Var(battle, card, stats, score.ShivShivsVar)),
            _ => 0,
        };

    private StrategicEffectContext WithExhaustDrawTiming(
        StrategicEffectContext context, in FlashBattle battle, bool skillsExhaust)
    {
        int noDrawIndex = -1, embraceIndex = -1;
        int visit = 0;
        bool corruption = false;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (power.Owner != FlashLimits.PlayerOwner)
                continue;
            FlashPowerScore rules = _score.Power(power.Id);
            if (power.Id == _score.NoDrawPowerId)
                noDrawIndex = visit;
            if (rules.Kind == FlashStrategicKind.DarkEmbrace)
                embraceIndex = visit;
            if (rules.Kind == FlashStrategicKind.Corruption)
                corruption = true;
            visit++;
        }
        if (embraceIndex < 0)
            return context;
        int ethereal = 0, exhaustingEthereal = 0;
        for (int offset = 0; offset < battle.HandCount; offset++)
        {
            ref readonly FlashCard card = ref battle.Cards[offset];
            if ((card.Keywords & FlashKeywords.Ethereal) == 0)
                continue;
            ethereal++;
            FlashCardStats stats = Rules.Card(card.Id, card.Upgrade);
            if ((card.Keywords & FlashKeywords.Exhaust) != 0
                || (corruption || skillsExhaust) && CardType(battle, card, stats) == FlashCardType.Skill)
            {
                exhaustingEthereal++;
            }
        }
        return context with
        {
            ExhaustDrawPlays = ExhaustDrawPotential(
                Math.Max(0, context.ExhaustPlays - exhaustingEthereal),
                context.RemainingTurns,
                noDrawIndex >= 0,
                ethereal,
                noDrawIndex < 0 || noDrawIndex < embraceIndex),
        };
    }

    private static int ExhaustDrawPotential(int ordinaryExhausts, int remainingTurns, bool noDrawNow,
        int etherealInHand, bool drawsAtTurnEnd)
    {
        int ordinary = noDrawNow ? (int)Math.Ceiling((double)ordinaryExhausts
            * Math.Max(0, remainingTurns - 1) / Math.Max(1, remainingTurns)) : ordinaryExhausts;
        return ordinary + (drawsAtTurnEnd ? etherealInHand : 0);
    }

    private static int EstimatedShivPlays(int existing, int generated, int generators, int deckSize, int actions,
        int reusableExisting, int singleUseGenerated, int singleUseGenerators)
    {
        if (actions <= 0 || deckSize <= 0) return 0;
        int cycleSize = deckSize + generated;
        int setupActions = ShivSetupActions(generators, cycleSize, actions, singleUseGenerators);
        int reusable = Math.Clamp(reusableExisting, 0, existing);
        int oneShot = existing - reusable;
        int existingPlays = Math.Min(oneShot, (int)Math.Ceiling((double)oneShot * actions / cycleSize))
            + (int)Math.Ceiling((double)reusable * actions / cycleSize);
        int oneShotGenerated = Math.Clamp(singleUseGenerated, 0, generated);
        int generatedPlays = Math.Min(oneShotGenerated, (int)Math.Ceiling((double)oneShotGenerated * actions / cycleSize))
            + (int)Math.Ceiling((double)(generated - oneShotGenerated) * actions / cycleSize);
        return Math.Min(actions - setupActions, existingPlays + generatedPlays);
    }

    private static int EstimatedAttackHits(int ordinaryHits, int existingShivs, int generatedShivs,
        int generators, int deckSize, int actions, int reusableShivs,
        int singleUseGenerated, int singleUseGenerators)
    {
        if (actions <= 0 || deckSize <= 0) return 0;
        int shivPlays = EstimatedShivPlays(existingShivs, generatedShivs, generators, deckSize, actions,
            reusableShivs, singleUseGenerated, singleUseGenerators);
        int setup = ShivSetupActions(generators, deckSize + generatedShivs, actions, singleUseGenerators);
        int remainingActions = Math.Max(0, actions - shivPlays - setup);
        int ordinaryDeckSize = Math.Max(1, deckSize - existingShivs - generators);
        return shivPlays + (int)Math.Ceiling((double)ordinaryHits * remainingActions / ordinaryDeckSize);
    }

    private static int ShivSetupActions(int generators, int cycleSize, int actions, int singleUseGenerators)
    {
        int oneShot = Math.Clamp(singleUseGenerators, 0, generators);
        return Math.Min(actions, Math.Min(oneShot, (int)Math.Ceiling((double)oneShot * actions / cycleSize))
            + (int)Math.Ceiling((double)(generators - oneShot) * actions / cycleSize));
    }

    private StrategicEffectVector EvaluateStrategic(
        FlashStrategicKind kind, int amountRaw, in StrategicEffectContext context)
    {
        int amount = Math.Max(1, amountRaw);
        int enemyHp = context.EnemyHp;
        int energyUnit = Math.Max(3, context.AverageCardValue);
        int cardAccessUnit = context.AverageCardValue;
        return kind switch
        {
            FlashStrategicKind.Afterimage => Prevention(amount * context.UsefulCardPlays, context),
            FlashStrategicKind.Buffer => Prevention(BufferPrevention(amount, context), context),
            FlashStrategicKind.FeelNoPain => Prevention(amount * context.ExhaustPlays, context),
            FlashStrategicKind.DarkEmbrace => CardAccess(
                amount * (context.ExhaustDrawPlays ?? context.ExhaustPlays) * cardAccessUnit),
            FlashStrategicKind.Thorns => Damage(amount * context.IncomingHitCount, enemyHp),
            FlashStrategicKind.EchoForm when context.UsefulCardPlays > 0 => Damage(
                context.BestCardValue
                    * Math.Min(amount, Math.Max(1, context.UsefulCardPlays))
                    * context.RemainingTurns,
                enemyHp),
            FlashStrategicKind.EchoForm => default,
            FlashStrategicKind.Envenom => Damage(amount * context.AttackPlays, enemyHp),
            FlashStrategicKind.Accuracy => Damage(amount * context.ShivPlays, enemyHp),
            FlashStrategicKind.SleightOfFlesh => Damage(amount * context.DebuffApplications, enemyHp),
            FlashStrategicKind.Strength => Damage(
                amount * (context.AttackHits ?? context.AttackPlays), enemyHp),
            FlashStrategicKind.Lethality when context.Act3BossInteractions && context.AttackPlays > 0
                => Damage(
                    context.FirstAttackDamage * Math.Min(context.AttackPlays, context.RemainingTurns)
                        * amount / 100,
                    enemyHp),
            FlashStrategicKind.Lethality when context.AttackPlays > 0 => Damage(
                context.AverageAttackValue
                    * Math.Min(context.AttackPlays, context.RemainingTurns)
                    * amount / 100,
                enemyHp),
            FlashStrategicKind.ReaperForm when context.AttackPlays > 0 => Damage(
                context.AverageAttackValue * context.AttackPlays * amount, enemyHp),
            FlashStrategicKind.Lethality or FlashStrategicKind.ReaperForm => default,
            FlashStrategicKind.Dexterity => Prevention(amount * context.BlockSkillPlays, context),
            FlashStrategicKind.DemonForm when context.AttackPlays > 0 => Damage(
                amount * Math.Max(1, context.AttackPlays / Math.Max(1, context.RemainingTurns))
                    * context.RemainingTurns * (context.RemainingTurns + 1) / 2,
                enemyHp),
            FlashStrategicKind.DemonForm => default,
            FlashStrategicKind.Curious => Resource(
                Math.Min(context.PowerEnergySpend, amount * context.PowerPlays) * energyUnit),
            FlashStrategicKind.Corruption => Resource(context.SkillEnergySpend * energyUnit),
            FlashStrategicKind.RecurringEnergy or FlashStrategicKind.OrbitEnergy
                => Resource(context.RecurringEnergyGain * energyUnit),
            FlashStrategicKind.CreativeAi => CardAccess(
                amount * context.RemainingTurns * cardAccessUnit),
            FlashStrategicKind.Iteration => CardAccess(
                amount * context.StatusDrawTriggers * cardAccessUnit),
            FlashStrategicKind.MasterPlanner => CardAccess(context.SkillPlays * cardAccessUnit),
            FlashStrategicKind.Pagestorm when context.Act3BossInteractions => CardAccess(
                Math.Min(
                    context.PagestormBonusDrawCapacity,
                    amount * context.EtherealDrawTriggers) * cardAccessUnit),
            FlashStrategicKind.DanseMacabre when context.Act3BossInteractions => Prevention(
                amount * context.HighEnergyPlays,
                context),
            FlashStrategicKind.Demesne when context.Act3BossInteractions =>
                Resource(context.DemesneEnergyGain * energyUnit)
                + CardAccess(context.DemesneDrawGain * cardAccessUnit),
            FlashStrategicKind.PrepTime when context.Act3BossInteractions => Damage(
                amount * Math.Min(context.RemainingTurns, context.AttackPlays), enemyHp),
            FlashStrategicKind.Focus => Scaling(amount * context.RemainingTurns * 2),
            FlashStrategicKind.Furnace => Scaling(amount * context.RemainingTurns * 2),
            FlashStrategicKind.Thunder => Damage(amount * context.RemainingTurns, enemyHp),
            FlashStrategicKind.LightningRod => Scaling(amount * context.RemainingTurns),
            _ => Scaling(amount),
        };
    }

    private static StrategicEffectVector Damage(int value, int enemyHp)
        => new(Math.Min(Math.Max(0, enemyHp), Math.Max(0, value)), 0, 0, 0, 0);

    private static StrategicEffectVector Prevention(int value, in StrategicEffectContext context)
    {
        int cap = context.IncomingDamage == 0
            ? value
            : context.IncomingDamage * Math.Min(2, context.RemainingTurns);
        return new(0, Math.Min(Math.Max(0, cap), Math.Max(0, value)), 0, 0, 0);
    }

    private static int BufferPrevention(int amount, in StrategicEffectContext context)
    {
        if (context.IncomingDamage == 0)
            return amount * 8;
        int averageHit = Math.Max(
            1,
            context.IncomingDamage / Math.Max(1, context.IncomingHitCount));
        return Math.Min(context.IncomingDamage, amount * averageHit);
    }

    private static StrategicEffectVector Resource(int value) => new(0, 0, Math.Max(0, value), 0, 0);

    private static StrategicEffectVector CardAccess(int value) => new(0, 0, 0, Math.Max(0, value), 0);

    private static StrategicEffectVector Scaling(int value) => new(0, 0, 0, 0, Math.Max(0, value));

    // ---------------------------------------------------------------------------------------------
    // Intermediate beam ranking (BeamRetentionPolicy.BeamRankScore).

    public double BeamRankScore(in FlashBattle battle, in FlashScoreFeatures features,
        bool terminal, bool isEndTurn)
    {
        // the existing search's BeamRankScore: the base-score member ranks by the base score alone (the
        // development strategy that could adjust it is rejected at admission).
        if (_score.BaseScoreOnly)
            return features.Score;
        int persistentBuffCap = _score.IsActEndingBoss
            ? SolverWeights.PersistentBuffDeltaBeamCap
            : SolverWeights.StandardPersistentBuffDeltaBeamCap;
        double persistentBuffValue = _score.IsActEndingBoss
            ? SolverWeights.PersistentBuffDeltaBeamValue
            : SolverWeights.StandardPersistentBuffDeltaBeamValue;
        bool useLatentSetup = _score.IsActEndingBoss || _score.InitialEnemyCount > 1;
        int strengthSuppressionHorizon = _score.IsActEndingBoss
            ? SolverWeights.BossEnemyStrengthSuppressionHorizon
            : SolverWeights.StandardEnemyStrengthSuppressionHorizon;
        int weakExpectedHpSaved = _score.IsActEndingBoss
            ? SolverWeights.BossEnemyWeakExpectedHpSaved
            : SolverWeights.StandardEnemyWeakExpectedHpSaved;
        double baseScore = features.Score;
        if (_score.ContinuousThreatRanking && !terminal && isEndTurn
            && !features.PlayerDead && features.ProjectedPlayerHp <= 0)
        {
            // the existing search's BeamRankScore: stand-pat projects ending now, but a living player can
            // still block, draw or kill before that intent. Keep its HP deficit continuous only in
            // intermediate ordering when a playable hand remains; final outcomes keep their policy.
            int reachable = features.ReachableHandValue;
            if (reachable == 0)
                (reachable, _, _) = ReachableHandPotential(battle);
            if (reachable > 0)
            {
                baseScore = features.Score - SolverWeights.DeathPenalty
                    + features.ProjectedPlayerHp * SolverWeights.Hp;
            }
        }
        double score = baseScore
            + Math.Min(SolverWeights.CurrentEnergyBeamCap, features.Energy)
                * SolverWeights.CurrentEnergyBeamValue
            + Math.Min(
                    persistentBuffCap,
                    Math.Max(0, features.PersistentBuffValue - _score.InitialPersistentBuffValue))
                * persistentBuffValue
            + (useLatentSetup
                ? Math.Min(SolverWeights.LatentSetupBeamCap, features.LatentSetupValue)
                    * SolverWeights.LatentSetupBeamValue
                : 0d)
            + (_score.IsActEndingBoss
                ? features.FutureResourceValue * SolverWeights.FutureResourceBeamValue
                : 0d)
            + Math.Min(
                    SolverWeights.ReplayPotentialBeamCap,
                    features.ReplayPotentialValue)
                * SolverWeights.ReplayPotentialBeamValue
            + RetainedAttackGrowth(features) * SolverWeights.RetainedAttackGrowthBeamValue
            + features.DelayedDamageValue * SolverWeights.DelayedDamageBeamValue
            + features.SandpitRemaining * SolverWeights.SandpitTurnBeamValue
            + Math.Min(
                    SolverWeights.EnemyStrengthSuppressionBeamCap,
                    Math.Max(0, features.EnemyStrengthSuppression - _score.InitialEnemyStrengthSuppression))
                * strengthSuppressionHorizon
                * SolverWeights.Hp
            + Math.Min(
                    SolverWeights.EnemyWeakTurnsBeamCap,
                    Math.Max(0, features.EnemyWeakTurns - _score.InitialEnemyWeakTurns))
                * weakExpectedHpSaved
                * SolverWeights.Hp;
        // The existing ranking scales one term for non-terminal nodes only. Terminal children never
        // enter a retention pool, but their rank is still computed before the terminal check.
        if (!terminal && _score.BeamWeightPerturbation is { Scale: not 1d } perturbation)
        {
            double term = perturbation.Term switch
            {
                BeamWeightTerm.CurrentEnergy => Math.Min(SolverWeights.CurrentEnergyBeamCap, features.Energy)
                    * SolverWeights.CurrentEnergyBeamValue,
                BeamWeightTerm.PersistentBuffDelta => Math.Min(persistentBuffCap,
                        Math.Max(0, features.PersistentBuffValue - _score.InitialPersistentBuffValue))
                    * persistentBuffValue,
                BeamWeightTerm.EnemyHp => features.EnemyHp * SolverWeights.EnemyHp,
                _ => throw new InvalidOperationException("Unknown Beam sensitivity term."),
            };
            score += (perturbation.Scale - 1d) * term;
        }
        score += ContextualAdjustment(battle, features, terminal);
        return score;
    }

    /// <summary>
    /// <c>ContextualRankingModel.Adjustment</c> for a Flash state. Terminal and non-surviving
    /// contexts bypass the model; the four retention-only inputs are recomputed here because the
    /// ordinary child score skips them, and the rest are the K4-checked features.
    /// </summary>
    private double ContextualAdjustment(in FlashBattle battle, in FlashScoreFeatures features,
        bool terminal)
    {
        if (_score.ContextualRanking is not { } model || terminal || features.ProjectedPlayerHp <= 0)
            return 0;
        (int reachableHandValue, int zeroCostPlayableCount, int playableAttackCount) =
            ReachableHandPotential(battle);
        ContextualRankingModel.Input input = new(
            features.Turn,
            false,
            features.ProjectedPlayerHp,
            features.Energy,
            features.PlayerHp,
            features.PlayerMaxHp,
            features.EnemyHp,
            features.HandCount,
            zeroCostPlayableCount,
            reachableHandValue,
            features.PersistentBuffValue,
            features.LatentSetupValue,
            features.RetainedAttackValue,
            features.ReplayPotentialValue,
            features.FutureResourceValue,
            features.DelayedDamageValue,
            features.LiveDeckClutter,
            features.LiveDeckSize,
            features.EnemyStrengthSuppression,
            features.EnemyWeakTurns,
            features.EnemyVulnerableTurns);
        return model.Adjustment(input);
    }

    internal double ContextualAdjustmentForTesting(in FlashBattle battle,
        in FlashScoreFeatures features, bool terminal)
        => ContextualAdjustment(battle, features, terminal);

    private int RetainedAttackGrowth(in FlashScoreFeatures features)
        => Math.Min(
            SolverWeights.RetainedAttackGrowthBeamCap,
            Math.Max(0, features.RetainedAttackValue - _score.InitialRetainedAttackValue));
}
