using System.Runtime.CompilerServices;
using BlitzSolver.Engine;
using BlitzSolver.Search;

namespace BlitzSolver.Search.Scoring;

/// <summary>
/// Ports the existing search's state evaluation to <see cref="BlitzBattle"/>. Every computation follows
/// <c>the existing search's Snapshot</c> (the existing search's StateEvaluation.cs) and
/// <c>BeamRetentionPolicy.BeamRankScore</c> (the existing search's BeamRetentionPolicy.Ranking.cs) exactly:
/// the same terms, the same order of operations, the same checked/rounding/clamping.
/// </summary>
/// <remarks>
/// The per-content values (card values, latent setup values and traits, power strategic effects) come from
/// <see cref="BlitzScoreRules"/>, which <c>BlitzScoreBridge</c> compiles from the existing engine's
/// models at the root. This type only reads the battle, the rule table and that compile output.
/// It allocates nothing on the hot path.
/// </remarks>
[System.Runtime.CompilerServices.SkipLocalsInit]
internal sealed class BlitzScorer
{
    private readonly BlitzScoreRules _score;

    public BlitzScorer(BlitzScoreRules score) => _score = score;

    /// <summary>Complete frozen score table, for the deterministic golden request codec.</summary>
    internal BlitzScoreRules RulesForCodec => _score;

    private BlitzRules Rules => _score.Rules;

    /// <summary><c>SearchRunContext.InitialRetainedAttackValue</c>, frozen by the root score bridge.</summary>
    public int InitialRetainedAttackValue => _score.InitialRetainedAttackValue;

    /// <summary>The root-frozen kernel rule table, used by the retention stand-pat replay.</summary>
    internal BlitzRules RulesForRetention => _score.Rules;

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
    private static decimal Var(in BlitzBattle battle, in BlitzCard card, BlitzCardStats stats, int index)
    {
        if (index < 0 || index >= stats.Vars.Length)
            return 0m;
        decimal delta = card.Private > 0
            ? BlitzDecimalVars.Delta(in battle, in card, index)
            : index switch { 0 => card.Var0, 1 => card.Var1, _ => 0m };
        return stats.Vars[index] + delta;
    }

    /// <summary>
    /// <c>CardChoiceSupport.CardValue</c> for one stored card, used by the stand-pat probe's
    /// rule-based automatic turn-start answer (the same ordering <c>BuildAutomaticPolicyChoice</c>
    /// uses: ascending hand values, descending pile/generated values).
    /// </summary>
    internal double AutomaticChoiceValue(in BlitzBattle battle, in BlitzCard card)
        => CardValue(battle, card, Rules.Card(card.Id, card.Upgrade), _score.Card(card.Id, card.Upgrade));

    /// <summary>
    /// Port of <c>CardChoiceSupport.CardValue</c> with the instance's variable deltas applied. The
    /// existing engine converts each completed decimal <c>BaseValue</c> to double once; adding a
    /// double delta to a double base would round differently for decimals such as 0.75.
    /// </summary>
    private double CardValue(in BlitzBattle battle, in BlitzCard card, BlitzCardStats stats, BlitzCardScore score)
        => VarDouble(battle, card, stats, score.DamageVar, score.BaseDamage)
            + VarDouble(battle, card, stats, score.BlockVar, score.BaseBlock) * 0.8d
            + VarDouble(battle, card, stats, score.CardsVar, score.BaseCards) * 3d
            + (score.DynamicCardType
                ? (CardType(battle, card, stats) == BlitzCardType.Power ? 8d : 0d)
                : (score.IsPower ? 8d : 0d));

    /// <summary>
    /// Native live <c>CardModel.Type</c>: the rule table's canonical value unless the definition reads
    /// per-card state (MadScience's saved TinkerTimeType). Roots without such cards take the cheap branch.
    /// </summary>
    private BlitzCardType CardType(in BlitzBattle battle, in BlitzCard card, BlitzCardStats stats)
        => stats.HasDynamicType ? BlitzRegistry.Card(card.Id).CardType(battle, Rules, card) : stats.Type;

    private static double VarDouble(in BlitzBattle battle, in BlitzCard card, BlitzCardStats stats, int index,
        double fallback)
    {
        if (index < 0)
            return fallback;
        decimal delta = card.Private > 0
            ? BlitzDecimalVars.Delta(in battle, in card, index)
            : index switch { 0 => card.Var0, 1 => card.Var1, _ => 0m };
        return (double)(stats.Vars[index] + delta);
    }

    /// <summary>Port of <c>the existing search's BranchCardValue</c> (calculated damage where registered).</summary>
    private double BranchCardValue(in BlitzBattle battle, in BlitzCard card, BlitzCardStats stats, BlitzCardScore score)
    {
        double value = CardValue(battle, card, stats, score);
        if (CardType(battle, card, stats) != BlitzCardType.Attack || score.Calculated == BlitzCalculatedKind.None)
            return value;
        decimal baseVar = Var(battle, card, stats, score.CalculationBaseVar);
        decimal extraVar = Var(battle, card, stats, score.CalculationExtraVar);
        decimal damage = 0m;
        for (int position = 0; position < battle.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(battle.Roster[position]);
            if (!BlitzKernel.IsHittable(battle, owner))
                continue;
            decimal multiplier = score.Calculated switch
            {
                BlitzCalculatedKind.PlayerBlock => battle.Player.Block,
                BlitzCalculatedKind.StrikeCardCount => CountCardsWithTag(battle, BlitzCardTag.Strike),
                BlitzCalculatedKind.ExhaustPileCount => battle.ExhaustCount,
                BlitzCalculatedKind.PlayerStrength => Math.Max(0, GetAmount(battle, _score.StrengthPowerId, BlitzLimits.PlayerOwner)),
                BlitzCalculatedKind.TargetVulnerable => GetAmount(battle, _score.VulnerablePowerId, owner),
                BlitzCalculatedKind.DrawPileCount => battle.DrawCount,
                BlitzCalculatedKind.TargetDebuffPowerCount => CountTargetDebuffPowers(battle, owner),
                BlitzCalculatedKind.FinishedCardPlays => battle.Counter(BlitzCounter.FinishedCardPlays),
                BlitzCalculatedKind.CardsDiscardedThisTurn => battle.Counter(BlitzCounter.CardsDiscardedThisTurn),
                BlitzCalculatedKind.NegativeHandCount => -battle.HandCount,
                BlitzCalculatedKind.CardsDrawn => battle.Counter(BlitzCounter.CardsDrawn),
                BlitzCalculatedKind.StarCardCount => CountStarCostCards(battle),
                BlitzCalculatedKind.OstyCurrentHp => (battle.Flags & BlitzBattleFlags.OstyPresent) != 0
                    && battle.Osty.IsAlive ? battle.Osty.CurrentHp : 0,
                BlitzCalculatedKind.OstyMaxHp => (battle.Flags & BlitzBattleFlags.OstyPresent) != 0
                    && battle.Osty.IsAlive ? battle.Osty.MaxHp : 0,
                BlitzCalculatedKind.OstyAttacksPlusOne => 1 + battle.Counter(BlitzCounter.OstyAttacksThisTurn),
                BlitzCalculatedKind.OstyAttackCardsMinusSelf => CountCardsWithTag(battle, BlitzCardTag.OstyAttack)
                    - ((stats.Tags & BlitzCardTag.OstyAttack) != 0 ? 1 : 0),
                BlitzCalculatedKind.ExhaustSoulCount => CountExhaustSoulCards(battle),
                BlitzCalculatedKind.EtherealPlays => battle.Counter(BlitzCounter.EtherealPlays),
                BlitzCalculatedKind.DoomOverThreshold => Math.Floor(
                    GetAmount(battle, _score.DoomPowerId, owner) / Var(battle, card, stats, score.DoomThresholdVar)),
                BlitzCalculatedKind.TargetDoom => GetAmount(battle, _score.DoomPowerId, owner),
                BlitzCalculatedKind.NonHandDraws => battle.Counter(BlitzCounter.NonHandDrawsThisTurn),
                BlitzCalculatedKind.GeneratedCardCount => battle.Counter(BlitzCounter.CardsGeneratedThisCombat),
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
    internal (int Value, int ZeroCostPlayableCount, int PlayableAttackCount) ReachableHandPotential(in BlitzBattle battle)
    {
        Span<(int Energy, int Stars, int Value)> playable = battle.HandCount <= 64
            ? stackalloc (int, int, int)[battle.HandCount]
            : new (int, int, int)[battle.HandCount];
        int count = 0;
        int zeroCostPlayableCount = 0;
        int playableAttackCount = 0;
        for (int index = 0; index < battle.HandCount; index++)
        {
            ref readonly BlitzCard card = ref battle.Cards[index];
            // CanPlayCard computes the same effective hand costs; reuse them instead of running the
            // energy/star cost hook dispatch a second time for the same card and pile.
            if (!BlitzKernel.CanPlayCard(battle, Rules, index, out int energyCost, out int starCost))
                continue;
            energyCost = Math.Max(0, energyCost);
            starCost = Math.Max(0, starCost);
            BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
            BlitzCardScore cardScore = _score.Card(card.Id, card.Upgrade);
            double cardValue = BranchCardValue(battle, card, stats, cardScore)
                * (1 + Math.Max(0, BlitzKernel.EnchantedReplayCount(card)));
            playable[count++] = (energyCost, starCost,
                Math.Max(1, (int)Math.Ceiling(cardValue)));
            if (energyCost == 0 && starCost == 0 && !stats.CostsX && !stats.StarCostsX)
                zeroCostPlayableCount++;
            if (CardType(battle, card, stats) == BlitzCardType.Attack)
                playableAttackCount++;
        }
        return (ReachableHandValue.Calculate(playable[..count], battle.Energy, battle.Stars),
            zeroCostPlayableCount, playableAttackCount);
    }

    /// <summary><c>CalculatedVarSpecRegistry.CrescentSpear</c>: the combat piles' star-cost cards.</summary>
    private int CountStarCostCards(in BlitzBattle battle)
    {
        int count = 0;
        for (int position = 0; position < battle.CardCount; position++)
        {
            ref readonly BlitzCard card = ref battle.Cards[position];
            BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
            if (stats.StarCost >= 0 || stats.StarCostsX)
                count++;
        }
        return count;
    }

    private static readonly BlitzPile[] LivePiles = [BlitzPile.Discard, BlitzPile.Draw, BlitzPile.Hand];

    /// <summary><c>liveCards.Count(card => card.Preview is Soul)</c> over Discard, Draw and Hand.</summary>
    private int CountLiveSoulCards(in BlitzBattle battle)
    {
        int count = 0;
        foreach (BlitzPile pile in LivePiles)
        {
            int start = BlitzPiles.Start(battle, pile);
            int size = BlitzPiles.Count(battle, pile);
            for (int offset = 0; offset < size; offset++)
            {
                ref readonly BlitzCard card = ref battle.Cards[start + offset];
                if (Rules.Card(card.Id, card.Upgrade).IsSoul)
                    count++;
            }
        }
        return count;
    }

    /// <summary><c>exhaustPile.Cards.Count(candidate =&gt; candidate.Preview is Soul)</c> (SoulStorm).</summary>
    private int CountExhaustSoulCards(in BlitzBattle battle)
    {
        int start = BlitzPiles.Start(battle, BlitzPile.Exhaust);
        int count = BlitzPiles.Count(battle, BlitzPile.Exhaust);
        int souls = 0;
        for (int offset = 0; offset < count; offset++)
        {
            ref readonly BlitzCard card = ref battle.Cards[start + offset];
            if (Rules.Card(card.Id, card.Upgrade).IsSoul)
                souls++;
        }
        return souls;
    }

    /// <summary><c>liveCards.Count(card =&gt; card.Preview.Tags.Contains(CardTag.OstyAttack))</c>.</summary>
    private int CountLiveOstyAttackCards(in BlitzBattle battle)
    {
        int count = 0;
        foreach (BlitzPile pile in LivePiles)
        {
            int start = BlitzPiles.Start(battle, pile);
            int size = BlitzPiles.Count(battle, pile);
            for (int offset = 0; offset < size; offset++)
            {
                ref readonly BlitzCard card = ref battle.Cards[start + offset];
                if ((Rules.Card(card.Id, card.Upgrade).Tags & BlitzCardTag.OstyAttack) != 0)
                    count++;
            }
        }
        return count;
    }

    private int CountCardsWithTag(in BlitzBattle battle, BlitzCardTag tag)
    {
        int count = 0;
        for (int position = 0; position < battle.CardCount; position++)
        {
            ref readonly BlitzCard card = ref battle.Cards[position];
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
    private static int CountTargetDebuffPowers(in BlitzBattle battle, byte owner)
    {
        int count = 0;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly BlitzPower power = ref battle.Powers[index];
            if (power.Owner != owner)
                continue;
            BlitzPowerDef def = BlitzRegistry.Power(power.Id);
            if (def.TypeForAmount(power.Amount) == BlitzPowerType.Debuff && !def.IsTemporaryPower)
                count++;
        }
        return count;
    }

    // ---------------------------------------------------------------------------------------------
    // Power amounts.

    private static int GetAmount(in BlitzBattle battle, ushort powerId, byte owner)
    {
        if (powerId == ushort.MaxValue)
            return 0;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly BlitzPower power = ref battle.Powers[index];
            if (power.Id == powerId && power.Owner == owner)
                return power.Amount;
        }
        return 0;
    }

    private static bool HasPower(in BlitzBattle battle, ushort powerId, byte owner)
        => powerId != ushort.MaxValue && GetAmount(battle, powerId, owner) > 0;

    // ---------------------------------------------------------------------------------------------
    // Growth rewards and configured relic counters (the existing search's Snapshot).

    /// <summary>
    /// <c>SimulatedCombatState.GrowthRewards</c> from the packed counters. The kernel packs two
    /// 16-bit source counts per counter; see <see cref="BlitzGrowthRewards"/>.
    /// </summary>
    private static GrowthValues CaptureGrowthRewards(in BlitzBattle battle) => new(
        BlitzGrowthRewards.Get(battle, BlitzGrowthSource.HandOfGreed),
        BlitzGrowthRewards.Get(battle, BlitzGrowthSource.TheHunt),
        BlitzGrowthRewards.Get(battle, BlitzGrowthSource.Feed),
        BlitzGrowthRewards.Get(battle, BlitzGrowthSource.Royalties),
        BlitzGrowthRewards.Get(battle, BlitzGrowthSource.Alchemize),
        BlitzGrowthRewards.Get(battle, BlitzGrowthSource.GeneticAlgorithm),
        BlitzGrowthRewards.Get(battle, BlitzGrowthSource.TheScythe),
        BlitzGrowthRewards.Get(battle, BlitzGrowthSource.Goopy),
        BlitzGrowthRewards.Get(battle, BlitzGrowthSource.ForbiddenGrimoire),
        BlitzGrowthRewards.Get(battle, BlitzGrowthSource.MadScience));

    /// <summary>
    /// <c>SimulatedCombatState.EvaluateRelicCounters</c> over the frozen policy targets, including
    /// the Meat on the Bone victory heal credit the existing snapshot adds in
    /// <c>the existing search's Snapshot</c>.
    /// </summary>
    private RelicCounterEvaluation EvaluateRelicCounters(in BlitzBattle battle, bool won)
    {
        RelicCounterEvaluation result = default;
        for (int index = 0; index < _score.RelicCounterRules.Length; index++)
        {
            BlitzRelicCounterRule rule = _score.RelicCounterRules[index];
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
    /// <c>SimulatedCombatState.ReadRelicCounter</c> for the supported relics. Blitz already carries
    /// each counter that the native relic reads for gameplay; only the Meat on the Bone threshold
    /// health comparison is recomputed from the battle.
    /// </summary>
    private static int ReadRelicCounter(in BlitzBattle battle, in BlitzRelicCounterRule rule)
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
        throw new BlitzUnsupportedException(
            $"遗物计数目标 {rule.Target.Id} 的遗物不在 Blitz 状态中。");
    }

    // ---------------------------------------------------------------------------------------------
    // Snapshot.

    /// <param name="includeRetentionOnly">
    /// When false, the two values read only by the retention comparators
    /// (<see cref="BlitzScoreFeatures.ReachableHandValue"/> and
    /// <see cref="BlitzScoreFeatures.ZeroCostPlayableCount"/>) are left at zero. The solver computes
    /// them when a state actually enters a retention pool; the K4 differential calls the default
    /// (true) and keeps checking every field.
    /// </param>
    public BlitzScoreFeatures Features(in BlitzBattle battle, int actionCount, bool includeRetentionOnly = true)
        => Evaluate(battle, actionCount, includeRetentionOnly);

    /// <summary>
    /// <c>SearchPolicySnapshot.GrowthTargetSatisfied</c>: every bounded growth goal has been realized.
    /// The request already disabled the HP-target stop for unbounded targets.
    /// </summary>
    public bool GrowthTargetsSatisfied(in BlitzScoreFeatures features)
        => features.GrowthRewards.Satisfies(_score.RequiredGrowthRewards);

    /// <summary><c>SearchPolicySnapshot.RelicTargetsSatisfied</c>.</summary>
    public bool RelicTargetsSatisfied(in BlitzScoreFeatures features)
        => (features.RelicSatisfiedMask & features.RelicTargetMask) == features.RelicTargetMask;

    private BlitzScoreFeatures Evaluate(in BlitzBattle battle, int actionCount, bool includeRetentionOnly)
    {
        int enemyHp = 0, enemyBlock = 0, rawEnemyHp = 0, maxCurrentEnemyHp = 0;
        int revivingEnemyCount = 0, aliveEnemyCount = 0;
        ulong aliveEnemyMask = 0;
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            ref readonly BlitzEnemy enemy = ref battle.Enemies[index];
            int currentHp = enemy.Creature.CurrentHp;
            int effectiveHp = EffectiveEnemyHp(battle, index);
            bool inCombat = (enemy.Flags & BlitzEnemyFlags.InCombat) != 0;
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


        bool dead = battle.Player.IsDead || battle.Outcome == BlitzOutcome.Defeat;
        bool won = !dead && battle.Outcome == BlitzOutcome.Victory;
        // The existing snapshot takes ThreatProjection(player.CurrentHp, 0, 0) whenever `won` is false
        // but the boundary is not None (the existing search's StateEvaluation.cs). EventDefeat is such a
        // boundary: it must not project the enemies' next intents or death saves.
        bool eventDefeat = battle.Outcome == BlitzOutcome.EventDefeat;
        bool risk = (battle.Flags & BlitzBattleFlags.HasRisk) != 0;
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

        int cumulativePlayerHpLost = battle.Counter(BlitzCounter.CumulativeHpLost);
        int recoveredPlayerHp = battle.Counter(BlitzCounter.RecoveredHp);
        int deathSaveHpRestored = battle.Counter(BlitzCounter.DeathSaveRelicHpRestored)
            + battle.Counter(BlitzCounter.DeathSavePotionHpRestored);
        int deathSaveUseCount = battle.Counter(BlitzCounter.DeathSaveUseCount);
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
            : battle.Counter(BlitzCounter.LongTermResourceValue);
        int exhaustedTheHunts = 0;
        if (_score.TheHuntCardId != ushort.MaxValue)
        {
            int start = BlitzPiles.Start(battle, BlitzPile.Exhaust);
            int count = BlitzPiles.Count(battle, BlitzPile.Exhaust);
            for (int index = 0; index < count; index++)
            {
                if (battle.Cards[start + index].Id == _score.TheHuntCardId)
                    exhaustedTheHunts++;
            }
        }
        int rewardedTheHunts = _score.TheHuntPowerId == ushort.MaxValue
            ? 0
            : Math.Max(0, GetAmount(battle, _score.TheHuntPowerId, BlitzLimits.PlayerOwner));
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

        int angerCopiesGenerated = battle.Counter(BlitzCounter.AngerCopiesGenerated);
        score += angerCopiesGenerated * SolverWeights.AngerCopyBeamPenalty;
        if (won)
            score += SolverWeights.VictoryBonus;
        score += enemyHp * SolverWeights.EnemyHp;

        int liveDeckClutter = 0;
        int liveCardCount = battle.HandCount + battle.DrawCount + battle.DiscardCount;
        for (int pile = 0; pile < 3; pile++)
        {
            BlitzPile segment = pile switch
            {
                0 => BlitzPile.Discard,
                1 => BlitzPile.Draw,
                _ => BlitzPile.Hand,
            };
            int start = BlitzPiles.Start(battle, segment);
            int count = BlitzPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly BlitzCard card = ref battle.Cards[start + offset];
                BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
                BlitzCardScore cardScore = _score.Card(card.Id, card.Upgrade);
                if ((CardType(battle, card, stats) is BlitzCardType.Status or BlitzCardType.Curse)
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
            outstandingStolenResource = BlitzKernel.OutstandingStolenResource(battle);
            minimumOutstandingStolenResource = BlitzKernel.MinimumOutstandingStolenResource(battle);
        }
        if (_score.TheftPolicy == SolverTheftPolicy.PreserveResources)
            score += outstandingStolenResource * SolverWeights.OutstandingStolenResourcePenalty;

        int retainedAttackValue = 0;
        for (int pile = 0; pile < 3; pile++)
        {
            BlitzPile segment = pile switch
            {
                0 => BlitzPile.Discard,
                1 => BlitzPile.Draw,
                _ => BlitzPile.Hand,
            };
            int start = BlitzPiles.Start(battle, segment);
            int count = BlitzPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly BlitzCard card = ref battle.Cards[start + offset];
                BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
                if (CardType(battle, card, stats) != BlitzCardType.Attack)
                    continue;
                BlitzCardScore cardScore = _score.Card(card.Id, card.Upgrade);
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
        BlitzSetupTraits persistentSetupTraits = BlitzSetupTraits.None;
        BlitzStrategicRequirements requirements = BlitzStrategicRequirements.None;
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
            ref readonly BlitzPower power = ref battle.Powers[index];
            if (power.Owner != BlitzLimits.PlayerOwner)
                continue;
            BlitzPowerScore rules = _score.Power(power.Id);
            if (!Contributes(power, rules))
                continue;
            requirements |= rules.Requirements;
            needsExhaustDrawTiming |= rules.Kind == BlitzStrategicKind.DarkEmbrace;
            skillsExhaust |= rules.Kind == BlitzStrategicKind.Corruption;
            needsRecurringEnergy |= rules.Kind is BlitzStrategicKind.RecurringEnergy or BlitzStrategicKind.OrbitEnergy;
            needsFirstAttackDamage |= rules.Kind == BlitzStrategicKind.Lethality;
            if (_score.Act3BossStrategy)
            {
                hasPagestorm |= rules.Kind == BlitzStrategicKind.Pagestorm;
                if (rules.Kind == BlitzStrategicKind.DanseMacabre)
                {
                    danseMacabreEnergyThreshold = Math.Max(
                        danseMacabreEnergyThreshold, rules.DanseMacabreEnergyThreshold);
                }
                if (rules.Kind == BlitzStrategicKind.Demesne)
                    demesneAmount += Math.Max(0, power.Amount);
            }
        }

        StrategicEffectContext? strategicContext = null;
        int refundSpend = 0, refundCapacity = 0, refundDraws = 0;
        bool refundCaptured = false;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly BlitzPower power = ref battle.Powers[index];
            if (power.Owner != BlitzLimits.PlayerOwner)
                continue;
            BlitzPowerScore rules = _score.Power(power.Id);
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
            if (rules.Kind == BlitzStrategicKind.RecurringEnergy)
            {
                int cardsLeft = power.Aux0;
                int triggers = refundDraws < cardsLeft ? 0 : 1 + (refundDraws - cardsLeft) / 10;
                int gain = (int)Math.Min(refundCapacity, (long)triggers * power.Amount);
                refundCapacity -= gain;
                effectContext = effectContext with { RecurringEnergyGain = gain };
            }
            else if (rules.Kind == BlitzStrategicKind.OrbitEnergy)
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
            persistentSetupTraits |= BlitzSetupTraits.OrbEngine;
            persistentBuffValue = checked(persistentBuffValue + OrbRetentionValue(battle, aliveEnemyCount));
        }

        int latentSetupValue = 0;
        BlitzSetupTraits latentSetupTraits = BlitzSetupTraits.None;
        for (int pile = 0; pile < 3; pile++)
        {
            BlitzPile segment = pile switch
            {
                0 => BlitzPile.Discard,
                1 => BlitzPile.Draw,
                _ => BlitzPile.Hand,
            };
            int start = BlitzPiles.Start(battle, segment);
            int count = BlitzPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly BlitzCard card = ref battle.Cards[start + offset];
                BlitzCardScore cardScore = _score.Card(card.Id, card.Upgrade);
                BlitzSetupTraits trait = cardScore.LatentTrait;
                int latentValue = cardScore.LatentValue;
                int rider = cardScore.DynamicCardType ? BlitzRegistry.Card(card.Id).PrivateRider(card) : -1;
                if (cardScore.DynamicCardType
                    && rider >= 0
                    && CardType(battle, card, stats: Rules.Card(card.Id, card.Upgrade))
                        == BlitzCardType.Power)
                {
                    // the existing search's StateEvaluation.LatentCardSetupValue's MadScience cases.
                    trait = rider == BlitzSolver.Content.Cards.Event.MadScience.RiderCurious
                        ? BlitzSetupTraits.Curious
                        : BlitzSetupTraits.RecurringScaling;
                    latentValue = rider == BlitzSolver.Content.Cards.Event.MadScience.RiderCurious
                        ? 10
                        : 5;
                }
                latentSetupTraits |= trait;
                if (trait == BlitzSetupTraits.None || !persistentSetupTraits.HasFlag(trait))
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
            ref readonly BlitzCard card = ref battle.Cards[offset];
            if ((card.Flags & BlitzCardFlags.RetainThisTurn) == 0
                && (card.Keywords & BlitzKeywords.Retain) == 0)
                continue;
            BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
            BlitzCardScore cardScore = _score.Card(card.Id, card.Upgrade);
            retainedHandValue = checked(retainedHandValue + Math.Max(
                1,
                (int)Math.Ceiling(CardValue(battle, card, stats, cardScore) * 2d)));
        }
        // Free-card opportunity (FreeAttackPower), Void Form and summon next turn. Void Form has no
        // admitted Blitz content.
        int summonNextTurn = GetAmount(battle, _score.SummonNextTurnPowerId, BlitzLimits.PlayerOwner);
        int summonNextTurnValue = summonNextTurn == 0
            ? 0
            : summonNextTurn * (4 + Math.Min(12, CountLiveOstyAttackCards(battle) * 2));
        int futureResourceValue = GetAmount(battle, _score.EnergyNextTurnPowerId, BlitzLimits.PlayerOwner) * 16
            + GetAmount(battle, _score.DrawCardsNextTurnPowerId, BlitzLimits.PlayerOwner) * 8
            + GetAmount(battle, _score.StarNextTurnPowerId, BlitzLimits.PlayerOwner) * 8
            + GetAmount(battle, _score.RetainHandPowerId, BlitzLimits.PlayerOwner) * 4
            + summonNextTurnValue
            + retainedHandValue
            + FreeCardOpportunityValue(battle, BlitzCardType.Attack, _score.FreeAttackPowerId)
            + FreeCardOpportunityValue(battle, BlitzCardType.Skill, _score.FreeSkillPowerId)
            + FreeCardOpportunityValue(battle, BlitzCardType.Power, _score.FreePowerPowerId)
            + VoidFormOpportunityValue(battle);

        int delayedDamageValue = 0;
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            ref readonly BlitzEnemy enemy = ref battle.Enemies[index];
            if ((enemy.Flags & BlitzEnemyFlags.InCombat) == 0 || !enemy.Creature.IsAlive)
                continue;
            byte owner = BlitzKernel.EnemyOwner((byte)index);
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
        int reactiveDamageValue = Math.Max(0, GetAmount(battle, _score.SleightOfFleshPowerId, BlitzLimits.PlayerOwner));

        bool hasBlockDamagePayoff = false;
        for (int pile = 0; pile < 3 && !hasBlockDamagePayoff; pile++)
        {
            BlitzPile segment = pile switch
            {
                0 => BlitzPile.Discard,
                1 => BlitzPile.Draw,
                _ => BlitzPile.Hand,
            };
            int start = BlitzPiles.Start(battle, segment);
            int count = BlitzPiles.Count(battle, segment);
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
            ref readonly BlitzEnemy enemy = ref battle.Enemies[index];
            if ((enemy.Flags & BlitzEnemyFlags.InCombat) == 0 || !enemy.Creature.IsAlive)
                continue;
            byte owner = BlitzKernel.EnemyOwner((byte)index);
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
                ref readonly BlitzPower power = ref battle.Powers[index];
                if (power.Id != _score.SandpitPowerId
                    || power.Target != BlitzLimits.PlayerOwner
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
        for (int slot = 0; slot < _score.RootPotionSlotCount && slot < BlitzLimits.PotionSlots; slot++)
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
        int automaticPotionUseCount = battle.Counter(BlitzCounter.AutomaticPotionUseCount);
        // Generated potions never occupied a root slot, so the slot diff cannot see their use. The
        // packed cost distinguishes explicit from automatic uses (Fairy in a Bottle); the free
        // EntropicBrew uses carry zero.
        potionUseCount = checked(potionUseCount + battle.Counter(BlitzCounter.GeneratedPotionUseCount));
        int generatedCosts = battle.Counter(BlitzCounter.GeneratedPotionStrategicCost);
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

        // Retention-only projections (the existing search's StateEvaluation): the projected shuffle
        // order, the unordered pile multiset and the Art of War next-turn window. None of these enter
        // Score, BeamRankScore or the route key, so a plain scored state skips them.
        BlitzRetentionValues retention = includeRetentionOnly ? RetentionValues(battle) : default;

        return new BlitzScoreFeatures
        {
            Score = score,
            PlayerDead = dead,
            AllEnemiesDead = won,
            PlayerHp = battle.Player.CurrentHp,
            PlayerMaxHp = battle.Player.MaxHp,
            PlayerBlock = battle.Player.Block,
            OstyHp = (battle.Flags & BlitzBattleFlags.OstyPresent) != 0 ? battle.Osty.CurrentHp : 0,
            OstyMaxHp = (battle.Flags & BlitzBattleFlags.OstyPresent) != 0 ? battle.Osty.MaxHp : 0,
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
            ProjectedShuffleOrderValue = retention.ProjectedShuffleOrderValue,
            ProjectedShuffleOrderKeyFirst = retention.ProjectedShuffleOrderKeyFirst,
            ProjectedShuffleOrderKeySecond = retention.ProjectedShuffleOrderKeySecond,
            CanTriggerArtOfWarNextTurn = retention.CanTriggerArtOfWarNextTurn,
            EnemyCombatDistributionFirst = retention.EnemyCombatDistributionFirst,
            EnemyCombatDistributionSecond = retention.EnemyCombatDistributionSecond,
            EnemyControlDistributionFirst = retention.EnemyControlDistributionFirst,
            EnemyControlDistributionSecond = retention.EnemyControlDistributionSecond,
            UnorderedPileKeyFirst = retention.UnorderedPileKeyFirst,
            UnorderedPileKeySecond = retention.UnorderedPileKeySecond,
        };
    }

    /// <summary>
    /// Retention-only values shared by K4 and the retention facts. Computed once per stored state (and
    /// once per K4-checked state), never on the plain score path.
    /// </summary>
    internal readonly record struct BlitzRetentionValues(
        int ProjectedShuffleOrderValue,
        ulong ProjectedShuffleOrderKeyFirst,
        ulong ProjectedShuffleOrderKeySecond,
        bool CanTriggerArtOfWarNextTurn,
        ulong EnemyCombatDistributionFirst,
        ulong EnemyCombatDistributionSecond,
        ulong EnemyControlDistributionFirst,
        ulong EnemyControlDistributionSecond,
        ulong UnorderedPileKeyFirst,
        ulong UnorderedPileKeySecond);

    internal BlitzRetentionValues RetentionValues(in BlitzBattle battle)
    {
        (int value, ulong projectedFirst, ulong projectedSecond) = CaptureProjectedShuffleOrder(battle);
        (ulong unorderedFirst, ulong unorderedSecond) = CaptureUnorderedPileKey(battle);
        (ulong combatFirst, ulong combatSecond) = CaptureEnemyCombatDistribution(battle);
        (ulong controlFirst, ulong controlSecond) = CaptureEnemyControlDistribution(battle);
        bool artOfWar = false;
        ushort artOfWarId = BlitzRegistry.ArtOfWarRelicId;
        if (artOfWarId != 0)
        {
            for (int index = 0; index < battle.RelicCount; index++)
            {
                ref readonly BlitzRelic relic = ref battle.Relics[index];
                if (relic.Id == artOfWarId && (relic.Flags & BlitzRelicFlags.Melted) == 0
                    && relic.Counter == 0)
                {
                    artOfWar = true;
                    break;
                }
            }
        }
        return new BlitzRetentionValues(value, projectedFirst, projectedSecond, artOfWar,
            combatFirst, combatSecond, controlFirst, controlSecond, unorderedFirst, unorderedSecond);
    }

    /// <summary>
    /// <c>SearchEvaluator</c>'s enemy combat distribution: per known enemy, in list order,
    /// (CombatId, current HP, block, effective HP, containment).
    /// </summary>
    private (ulong First, ulong Second) CaptureEnemyCombatDistribution(in BlitzBattle battle)
    {
        StateFingerprintBuilder key = new();
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            ref readonly BlitzEnemy enemy = ref battle.Enemies[index];
            key.Add(enemy.CombatId);
            key.Add(enemy.Creature.CurrentHp);
            key.Add(enemy.Creature.Block);
            key.Add(EffectiveEnemyHp(battle, index));
            key.Add((enemy.Flags & BlitzEnemyFlags.InCombat) != 0);
        }
        StateFingerprint finish = key.Finish();
        return (finish.First, finish.Second);
    }

    /// <summary>
    /// <c>SearchEvaluator</c>'s enemy control distribution: per living, contained enemy,
    /// (CombatId, strength suppression, weak turns, vulnerable turns).
    /// </summary>
    private (ulong First, ulong Second) CaptureEnemyControlDistribution(in BlitzBattle battle)
    {
        StateFingerprintBuilder key = new();
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            ref readonly BlitzEnemy enemy = ref battle.Enemies[index];
            if ((enemy.Flags & BlitzEnemyFlags.InCombat) == 0 || !enemy.Creature.IsAlive)
                continue;
            byte owner = BlitzKernel.EnemyOwner((byte)index);
            key.Add(enemy.CombatId);
            key.Add(-GetAmount(battle, _score.StrengthPowerId, owner));
            key.Add(Math.Max(0, GetAmount(battle, _score.WeakPowerId, owner)));
            key.Add(Math.Max(0, GetAmount(battle, _score.VulnerablePowerId, owner)));
        }
        StateFingerprint finish = key.Finish();
        return (finish.First, finish.Second);
    }

    /// <summary>
    /// <c>SearchEvaluator.BuildProjectedShuffleOrder</c>: discard + draw + hand in that order, the
    /// native stable-shuffle projection (unstable sort by card id/upgrade, then Fisher-Yates from the
    /// end on a copy of the shuffle RNG), then the value sum and a card-state key. The existing key
    /// hashes the existing engine's per-card fingerprint; Blitz hashes the card byte image instead.
    /// </summary>
    private (int Value, ulong KeyFirst, ulong KeySecond) CaptureProjectedShuffleOrder(in BlitzBattle battle)
    {
        int total = battle.DiscardCount + battle.DrawCount + battle.HandCount;
        Span<BlitzCard> cards = stackalloc BlitzCard[BlitzLimits.Cards];
        cards = cards[..total];
        int discardCount = battle.DiscardCount;
        int drawCount = battle.DrawCount;
        BlitzPiles.ReadCards(battle, BlitzPile.Discard).CopyTo(cards);
        BlitzPiles.ReadCards(battle, BlitzPile.Draw).CopyTo(cards[discardCount..]);
        BlitzPiles.ReadCards(battle, BlitzPile.Hand).CopyTo(cards[(discardCount + drawCount)..]);
        BlitzCardSort.Sort(cards, Rules);
        BlitzRngState shuffle = battle.RngStream(BlitzRngStream.Shuffle);
        int shuffleCounter = shuffle.Counter;
        for (int n = total; n > 1;)
        {
            n--;
            int k = shuffle.NextInt(n + 1);
            (cards[k], cards[n]) = (cards[n], cards[k]);
        }
        StateFingerprintBuilder key = new();
        key.Add(shuffleCounter);
        key.Add(total);
        int value = 0;
        for (int index = 0; index < total; index++)
        {
            BlitzCard card = cards[index];
            BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
            BlitzCardScore cardScore = _score.Card(card.Id, card.Upgrade);
            // CardChoiceSupport.CardValue reads the live DynamicVars BaseValue, which some cards
            // (SovereignBlade's forged damage) grow per instance; the instance deltas are the same
            // values the score table's CardValue applies.
            double cardValue = CardValue(battle, card, stats, cardScore);
            value += (int)Math.Round(cardValue * (total - index));
            ulong hash = BlitzFingerprint.HashCard(card);
            key.Add(StateFingerprintBuilder.MixFirst(hash));
            key.Add(StateFingerprintBuilder.MixSecond(hash));
        }
        StateFingerprint finish = key.Finish();
        return (value, finish.First, finish.Second);
    }

    /// <summary>
    /// <c>SearchEvaluator.BuildUnorderedPileKey</c>: per pile (hand, draw, discard, exhaust, in that
    /// order) the order-invariant sum of the mixed per-card hashes, followed by the pile marker, count
    /// and both sums. The existing engine hashes its own card fingerprint; the equivalence class is
    /// the same card-state multiset.
    /// </summary>
    private (ulong First, ulong Second) CaptureUnorderedPileKey(in BlitzBattle battle)
    {
        StateFingerprintBuilder unordered = new();
        AppendUnorderedPile(ref unordered, battle, BlitzPile.Hand, 'H');
        AppendUnorderedPile(ref unordered, battle, BlitzPile.Draw, 'D');
        AppendUnorderedPile(ref unordered, battle, BlitzPile.Discard, 'C');
        AppendUnorderedPile(ref unordered, battle, BlitzPile.Exhaust, 'X');
        StateFingerprint finish = unordered.Finish();
        return (finish.First, finish.Second);
    }

    private static void AppendUnorderedPile(ref StateFingerprintBuilder unordered, in BlitzBattle battle,
        BlitzPile pile, char marker)
    {
        ReadOnlySpan<BlitzCard> cards = BlitzPiles.ReadCards(battle, pile);
        ulong first = 0, second = 0;
        for (int index = 0; index < cards.Length; index++)
        {
            ulong hash = BlitzFingerprint.HashCard(cards[index]);
            first += StateFingerprintBuilder.MixFirst(hash);
            second += StateFingerprintBuilder.MixSecond(hash);
        }
        unordered.Add(marker);
        unordered.Add(cards.Length);
        unordered.Add(first);
        unordered.Add(second);
    }

    private int EffectiveEnemyHp(in BlitzBattle battle, int index)
    {
        ref readonly BlitzEnemy enemy = ref battle.Enemies[index];
        // SimulatedCombatState.EffectiveEnemyHp: an armed Steam Eruption phase counts as zero HP.
        if (enemy.SteamPhase != BlitzSteamPhase.None)
            return 0;
        // SimulatedCombatState.EffectiveEnemyHp's TestSubject branch: while Adaptable is alive, the
        // remaining forms' HP is part of the effective HP even at full current HP.
        if (_score.AdaptablePowerId != ushort.MaxValue
            && HasPower(battle, _score.AdaptablePowerId, BlitzKernel.EnemyOwner((byte)index))
            && enemy.DeathPhase != BlitzDeathPhase.PermanentlyDead
            && BlitzRegistry.Monster(enemy.MonsterId).HasAdaptableFormHp)
        {
            BlitzMonsterRules rules = Rules.MonsterRule(enemy.Rules);
            int remaining = Math.Max(0, enemy.Creature.CurrentHp);
            if (enemy.Ai[0] < 1)
                remaining += enemy.Ai[2];
            if (enemy.Ai[0] < 2)
                remaining += enemy.Ai[3];
            return remaining;
        }
        if (enemy.Creature.CurrentHp > 0)
            return enemy.Creature.CurrentHp;
        if (enemy.DeathPhase != BlitzDeathPhase.Reviving)
            return 0;
        byte owner = BlitzKernel.EnemyOwner((byte)index);
        // SimulatedCombatState.RevivingEnemyHp: a Decimillipede segment restores its ReattachPower
        // amount only while another reattaching segment has not completed death (alive or reviving).
        if (HasPower(battle, _score.ReattachPowerId, owner))
        {
            for (int position = 0; position < battle.RosterCount; position++)
            {
                byte other = BlitzKernel.EnemyOwner(battle.Roster[position]);
                if (other == owner || !HasPower(battle, _score.ReattachPowerId, other))
                    continue;
                if (battle.Enemies[battle.Roster[position]].DeathPhase != BlitzDeathPhase.PermanentlyDead)
                    return Math.Max(0, GetAmount(battle, _score.ReattachPowerId, owner));
            }
            return 0;
        }
        // Illusion-powered revives restore the captured max HP; the power amount is irrelevant.
        return HasPower(battle, _score.IllusionPowerId, owner) ? enemy.Creature.MaxHp : 0;
    }

    private bool LeavesHandAtTurnEnd(in BlitzBattle battle, BlitzPile pile, in BlitzCard card, BlitzCardScore score)
    {
        if (pile != BlitzPile.Hand)
            return false;
        if (score.HasTurnEndInHandEffect)
            return false;
        // The existing engine reads `card.HasKeyword(State, Ethereal)`, i.e. through Hook.ModifyKeywords:
        // HexPower grants Ethereal to every Hexed card, so a Hexed status/curse in hand leaves at the
        // turn end even without a local Ethereal keyword. The local keyword alone under-counts the
        // exception and makes the card look like persistent clutter.
        // Hook.ShouldEtherealTrigger has no supported listeners, so it is always true here.
        return BlitzKernel.HasKeyword(battle, card, BlitzKeywords.Ethereal);
    }

    private int ReplayPotentialValue(in BlitzBattle battle)
    {
        int total = 0;
        for (int pile = 0; pile < 3; pile++)
        {
            BlitzPile segment = pile switch
            {
                0 => BlitzPile.Discard,
                1 => BlitzPile.Draw,
                _ => BlitzPile.Hand,
            };
            int start = BlitzPiles.Start(battle, segment);
            int count = BlitzPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly BlitzCard card = ref battle.Cards[start + offset];
                BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
                int replayCount = Math.Max(0, BlitzKernel.EnchantedReplayCount(card));
                if (replayCount == 0
                    || CardType(battle, card, stats) is not (BlitzCardType.Attack or BlitzCardType.Skill or BlitzCardType.Power))
                    continue;
                BlitzCardScore cardScore = _score.Card(card.Id, card.Upgrade);
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

    private ThreatProjection ProjectHpAfterThreat(in BlitzBattle battle)
    {
        // the existing search's ProjectHpAfterThreat walks the run-listener table, which still contains the
        // player's relics and cards after the player dies; Blitz's PlayerHooksActive flag only gates the
        // combat dispatch. The projection must include those modifiers (Tungsten Rod's per-hit reduction
        // is the visible case) even on a dead snapshot, and it consumes Buffer stacks, so it runs on a
        // local copy: the evaluated state stays read-only through the `in` parameter.
        BlitzBattle probe = battle;
        probe.Flags |= BlitzBattleFlags.PlayerHooksActive;
        return ProjectHpAfterThreatCore(ref probe);
    }

    private ThreatProjection ProjectHpAfterThreatCore(ref BlitzBattle probe)
    {
        BlitzExec x = new(ref probe, Rules, default);
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
                ref readonly BlitzPower power = ref probe.Powers[index];
                if (power.Id == _score.SandpitPowerId && power.Amount == 1
                    && power.Target == BlitzLimits.PlayerOwner
                    && probe.Creature(power.Owner).IsAlive)
                {
                    return new ThreatProjection(0, 0, 0);
                }
            }
        }
        // the existing search's ProjectHpAfterThreat's ostyHp: the pet absorbs redirected powered attacks
        // until it dies; the remaining loss spills over to the player.
        int ostyHp = (probe.Flags & BlitzBattleFlags.OstyPresent) != 0 ? probe.Osty.CurrentHp : 0;
        ProjectedDeathPrevention prevention = new(probe, _score, Rules);
        // the existing search's ProjectHpAfterThreat's gambitActive latch: the first unblocked damage from a
        // powered attack kills the owner through TheGambitPower, then death prevention may revive it.
        bool gambitActive = _score.TheGambitPowerId != ushort.MaxValue
            && GetAmount(probe, _score.TheGambitPowerId, BlitzLimits.PlayerOwner) > 0;
        for (int position = 0; position < probe.RosterCount; position++)
        {
            ref readonly BlitzEnemy enemy = ref probe.Enemies[probe.Roster[position]];
            byte owner = BlitzKernel.EnemyOwner(probe.Roster[position]);
            if (!enemy.Creature.IsAlive)
                continue;
            if ((enemy.Flags & BlitzEnemyFlags.SkipNextMove) != 0)
                continue;
            // SimulatedCombatState.TryGetForcedMoveId: about-to-blow has no damage intent; explode
            // projects its recorded damage once, without the normal move's attacks.
            if (enemy.SteamPhase == BlitzSteamPhase.AboutToBlow)
                continue;
            if (enemy.SteamPhase == BlitzSteamPhase.Explode)
            {
                ProjectThreatHit(ref x, owner, enemy.Ai[BlitzKernel.SteamEruptionDamageRegister],
                    ref ostyHp, ref block, ref hp, ref gambitActive, ref prevention);
                continue;
            }
            BlitzMonsterNode move = Rules.MonsterRule(enemy.Rules).Nodes[enemy.Move];
            for (int attackIndex = 0; attackIndex < move.Attacks.Length; attackIndex++)
            {
                BlitzMonsterAttack attack = move.Attacks[attackIndex];
                BlitzMonsterDef def = BlitzRegistry.Monster(enemy.MonsterId);
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

    private void ProjectThreatHit(ref BlitzExec x, byte attacker, int baseDamage,
        ref int ostyHp, ref int block, ref int hp, ref bool gambitActive, ref ProjectedDeathPrevention prevention)
    {
        decimal modified = BlitzHooks.ModifyDamage(
            ref x, BlitzLimits.PlayerOwner, attacker, baseDamage, BlitzValueProps.Move, false, default);
        int adjustedHit = Math.Max(0, (int)Math.Floor(modified));
        int blocked = Math.Min(block, adjustedHit);
        block -= blocked;

        BlitzHpLossContext loss = new(
            BlitzLimits.PlayerOwner, BlitzValueProps.Move, attacker, false, default);
        Span<BlitzListener> changed = stackalloc BlitzListener[BlitzHooks.MaxListenerCount * 2];
        decimal hpLoss = ProjectedModifyHpLost(
            ref x, loss, adjustedHit - blocked, afterOsty: false, changed, out _);
        // the existing search's ProjectThreatHit: Hook.ModifyUnblockedDamageTarget, then the engine's explicit
        // DieForYou fallback and dead-Osty guard, then the after-Osty modifiers on the chosen receiver.
        byte target = BlitzHooks.ModifyUnblockedDamageTarget(
            ref x, BlitzLimits.PlayerOwner, hpLoss, BlitzValueProps.Move, attacker);
        if (target == BlitzLimits.PlayerOwner
            && (x.B.Flags & BlitzBattleFlags.OstyPresent) != 0
            && ostyHp > 0
            && GetAmount(x.B, _score.DieForYouPowerId, BlitzLimits.OstyOwner) > 0)
        {
            target = BlitzLimits.OstyOwner;
        }
        if (target == BlitzLimits.OstyOwner && ostyHp <= 0)
            target = BlitzLimits.PlayerOwner;
        BlitzHpLossContext redirected = new(
            target, BlitzValueProps.Move, attacker, false, default);
        hpLoss = ProjectedModifyHpLost(ref x, redirected, hpLoss, afterOsty: true, changed, out int changedCount);
        // The projected Buffer consumption is local to this forecast: notify the changed listeners so the
        // probe's Buffer amount drops, exactly as ProjectedHpLossModifiers.Consume does in the engine.
        BlitzHooks.AfterModifyingHpLostAfterOsty(ref x, changed[..changedCount]);
        int damage = Math.Max(0, (int)Math.Floor(hpLoss));
        if (target == BlitzLimits.OstyOwner)
        {
            int absorbed = Math.Min(ostyHp, damage);
            ostyHp -= absorbed;
            // Native redirected damage applies the original receiver's AfterOsty modifiers to the
            // overkill as a separate loss; the engine also filters Buffer there.
            Span<BlitzListener> overflowChanges = stackalloc BlitzListener[BlitzHooks.MaxListenerCount * 2];
            BlitzHpLossContext overflow = new(
                BlitzLimits.PlayerOwner, BlitzValueProps.Move, attacker, false, default);
            decimal overflowLoss = ProjectedModifyHpLost(
                ref x, overflow, damage - absorbed, afterOsty: true, overflowChanges, out int overflowCount);
            BlitzHooks.AfterModifyingHpLostAfterOsty(ref x, overflowChanges[..overflowCount]);
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
    /// <see cref="BlitzHooks.ModifyHpLost"/> with the engine's projected Buffer filter: a Buffer whose
    /// locally projected remaining count is exhausted no longer zeroes later hits.
    /// </summary>
    private decimal ProjectedModifyHpLost(ref BlitzExec x, in BlitzHpLossContext loss, decimal amount,
        bool afterOsty, scoped Span<BlitzListener> changed, out int changedCount)
    {
        changedCount = 0;
        BlitzHookMask normalMask = afterOsty ? BlitzHookMask.ModifyHpLostAfterOsty : BlitzHookMask.ModifyHpLostBeforeOsty;
        BlitzHookMask lateMask = afterOsty ? BlitzHookMask.ModifyHpLostAfterOstyLate : BlitzHookMask.ModifyHpLostBeforeOstyLate;
        if ((BlitzRegistry.AnyHooks & (normalMask | lateMask)) == 0)
            return amount;
        Span<BlitzListener> listeners = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
        for (int pass = 0; pass < 2; pass++)
        {
            int count = BlitzHooks.CollectListeners(x.B, pass == 0 ? normalMask : lateMask, listeners);
            for (int index = 0; index < count; index++)
            {
                BlitzListener listener = listeners[index];
                if (listener.Kind == BlitzListenerKind.Power
                    && x.B.Powers[listener.Index].Id == _score.BufferPowerId
                    && x.B.Powers[listener.Index].Amount <= 0)
                {
                    continue;
                }
                BlitzListenerDef def = BlitzHooks.Definition(x.B, listener);
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

        public ProjectedDeathPrevention(in BlitzBattle battle, BlitzScoreRules score, BlitzRules rules)
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
                    ref readonly BlitzRelic relic = ref battle.Relics[index];
                    if (relic.Id != score.LizardTailRelicId || relic.Counter != 0
                        || (relic.Flags & BlitzRelicFlags.Melted) != 0)
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

    private int MeasureDefensiveBlockReserve(in BlitzBattle battle, int blockConsumed)
    {
        int consumed = Math.Clamp(blockConsumed, 0, Math.Max(0, battle.Player.Block));
        int remaining = Math.Max(0, battle.Player.Block - consumed);
        if (remaining == 0)
            return consumed;
        // ShouldClearBlock and BlockAfterPreventingClear are pure queries in every registered definition
        // (Calipers only reads its relic var; Barricade/Blur/Hive read owner equality; no implementation
        // writes x.B), and the BlitzExec constructor only stores fields. The query therefore runs on the
        // read-only state without a 12 KiB copy; this is the only place a BlitzExec wraps a battle that is
        // still reachable through an `in` parameter, and it performs no write.
        ref BlitzBattle state = ref Unsafe.AsRef(in battle);
        BlitzExec x = new(ref state, Rules, default);
        if (ShouldClearBlock(ref x, BlitzLimits.PlayerOwner, out BlitzListener preventer))
            return consumed;
        int retained = BlitzHooks.Definition(state, preventer)
            .BlockAfterPreventingClear(ref x, preventer, BlitzLimits.PlayerOwner, remaining);
        return consumed + Math.Min(Math.Max(0, retained), Math.Max(0, battle.Player.MaxHp));
    }

    private static bool ShouldClearBlock(ref BlitzExec x, byte owner, out BlitzListener preventer)
    {
        preventer = default;
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.ShouldClearBlock) == 0)
            return true;
        Span<BlitzListener> listeners = stackalloc BlitzListener[BlitzHooks.MaxListenerCount];
        int count = BlitzHooks.CollectListeners(x.B, BlitzHookMask.ShouldClearBlock, listeners);
        for (int index = 0; index < count; index++)
        {
            if (!BlitzHooks.Definition(x.B, listeners[index]).ShouldClearBlock(ref x, listeners[index], owner))
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
    // Hook.ModifyOrbValue (Focus) through OrbMirrors.GetPassiveValue; BlitzKernel.OrbPassiveValue is
    // the same pure query over the Blitz listener list.
    private int OrbRetentionValue(in BlitzBattle battle, int aliveEnemyCount)
    {
        decimal value = 0m;
        for (int index = 0; index < battle.OrbCount; index++)
        {
            decimal passive = Math.Max(0m, BlitzKernel.OrbPassiveValue(battle, Rules, index));
            value += passive * BlitzRegistry.Orb(battle.Orbs[index].Id).RetentionMultiplier(battle, aliveEnemyCount);
        }
        return checked((int)Math.Ceiling(value));
    }

    // Threat focus (BuildThreatFocus).

    private readonly record struct ThreatFocus(
        uint? CombatId, int Pressure, int RemainingHp, int CurrentThreat, int TotalThreat, int IncomingHitCount);

    private ThreatFocus BuildThreatFocus(in BlitzBattle battle)
    {
        uint? bestCombatId = null;
        int bestPressure = 0;
        int bestRemainingHp = int.MaxValue;
        int bestThreat = 0;
        int totalThreat = 0;
        int incomingHitCount = 0;
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            ref readonly BlitzEnemy enemy = ref battle.Enemies[index];
            if ((enemy.Flags & BlitzEnemyFlags.InCombat) == 0 || !enemy.Creature.IsAlive)
                continue;
            byte owner = BlitzKernel.EnemyOwner((byte)index);
            int remainingHp = Math.Max(0, enemy.Creature.CurrentHp);
            int maxHp = Math.Max(1, enemy.Creature.MaxHp);
            int currentThreat = 0;
            if ((enemy.Flags & BlitzEnemyFlags.SkipNextMove) == 0)
            {
                BlitzMonsterNode move = Rules.MonsterRule(enemy.Rules).Nodes[enemy.Move];
                for (int attackIndex = 0; attackIndex < move.Attacks.Length; attackIndex++)
                {
                    BlitzMonsterAttack attack = move.Attacks[attackIndex];
                    BlitzMonsterDef def = BlitzRegistry.Monster(enemy.MonsterId);
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

    private static bool Contributes(in BlitzPower power, BlitzPowerScore rules)
        => power.Amount > 0
            && !rules.IsTemporary
            && rules.Type == BlitzPowerType.Buff;

    private StrategicEffectContext BuildStrategicContext(
        in BlitzBattle battle, int enemyHp, int incomingDamage, int incomingHitCount,
        BlitzStrategicRequirements requirements, bool skillsExhaust)
    {
        if (requirements == BlitzStrategicRequirements.None)
        {
            return new StrategicEffectContext(
                Math.Max(0, enemyHp), Math.Max(0, incomingDamage), Math.Max(0, incomingHitCount),
                1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0);
        }

        const BlitzStrategicRequirements reachablePlayRequirements =
            BlitzStrategicRequirements.UsefulCardPlays
            | BlitzStrategicRequirements.AttackPlays
            | BlitzStrategicRequirements.SkillPlays
            | BlitzStrategicRequirements.BlockSkillPlays
            | BlitzStrategicRequirements.PowerPlays
            | BlitzStrategicRequirements.ExhaustPlays
            | BlitzStrategicRequirements.ShivPlays
            | BlitzStrategicRequirements.DebuffApplications
            | BlitzStrategicRequirements.StatusDrawTriggers
            | BlitzStrategicRequirements.AttackHits;
        bool needsRemainingTurns = requirements.HasFlag(BlitzStrategicRequirements.RemainingTurns)
            || (requirements & reachablePlayRequirements) != 0;
        bool needsAllCardValues = requirements.HasFlag(BlitzStrategicRequirements.AverageCardValue)
            || requirements.HasFlag(BlitzStrategicRequirements.BestCardValue);
        bool needsAttackValues = needsRemainingTurns
            || requirements.HasFlag(BlitzStrategicRequirements.AverageAttackValue);
        bool needsAttackCount = requirements.HasFlag(BlitzStrategicRequirements.AttackPlays)
            || requirements.HasFlag(BlitzStrategicRequirements.AverageAttackValue);
        bool needsSkillCount = requirements.HasFlag(BlitzStrategicRequirements.SkillPlays);
        bool needsBlockSkillCount = requirements.HasFlag(BlitzStrategicRequirements.BlockSkillPlays);
        bool needsPowerCount = requirements.HasFlag(BlitzStrategicRequirements.PowerPlays);
        bool needsExhaustCount = requirements.HasFlag(BlitzStrategicRequirements.ExhaustPlays);
        bool needsShivCount = requirements.HasFlag(BlitzStrategicRequirements.ShivPlays)
            || requirements.HasFlag(BlitzStrategicRequirements.AttackHits);
        bool needsDebuffCount = requirements.HasFlag(BlitzStrategicRequirements.DebuffApplications);
        bool needsStatusCount = requirements.HasFlag(BlitzStrategicRequirements.StatusDrawTriggers);
        bool needsSkillEnergy = requirements.HasFlag(BlitzStrategicRequirements.SkillEnergySpend);
        bool needsPowerEnergy = requirements.HasFlag(BlitzStrategicRequirements.PowerEnergySpend);

        int attackCount = 0, attackHitCount = 0, skillCount = 0, blockSkillCount = 0;
        int powerCount = 0, exhaustCount = 0, shivCount = 0, reusableShivCount = 0;
        int generatedShivCount = 0, shivGeneratorCount = 0;
        int singleUseGeneratedShivs = 0, singleUseShivGenerators = 0;
        int debuffCount = 0, statusCount = 0, skillEnergy = 0, powerEnergy = 0;
        int totalCardValue = 0, bestCardValue = 0, totalAttackValue = 0;
        int liveCount = battle.DiscardCount + battle.DrawCount + battle.HandCount;
        for (int pile = 0; pile < 3; pile++)
        {
            BlitzPile segment = pile switch
            {
                0 => BlitzPile.Discard,
                1 => BlitzPile.Draw,
                _ => BlitzPile.Hand,
            };
            int start = BlitzPiles.Start(battle, segment);
            int count = BlitzPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly BlitzCard card = ref battle.Cards[start + offset];
                BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
                BlitzCardScore cardScore = _score.Card(card.Id, card.Upgrade);
                BlitzCardType cardType = CardType(battle, card, stats);
                int cardValue = 0;
                if (needsAllCardValues || (needsAttackValues && cardType == BlitzCardType.Attack))
                {
                    cardValue = Math.Max(1, (int)Math.Ceiling(CardValue(battle, card, stats, cardScore)));
                    if (needsAllCardValues)
                    {
                        totalCardValue += cardValue;
                        bestCardValue = Math.Max(bestCardValue, cardValue);
                    }
                    if (needsAttackValues && cardType == BlitzCardType.Attack)
                        totalAttackValue += cardValue;
                }
                switch (cardType)
                {
                    case BlitzCardType.Attack:
                        if (needsAttackCount)
                            attackCount++;
                        if (requirements.HasFlag(BlitzStrategicRequirements.AttackHits)
                            && !cardScore.HasOstyAttackTag && !cardScore.HasShivTag)
                        {
                            attackHitCount += AttackHits(battle, cardScore, card, stats);
                        }
                        break;
                    case BlitzCardType.Skill:
                        if (needsSkillCount)
                            skillCount++;
                        if (needsSkillEnergy)
                            skillEnergy += EnergyCost(battle, card, stats, segment);
                        if (needsBlockSkillCount && cardScore.HasBlockDynamicVar)
                            blockSkillCount++;
                        break;
                    case BlitzCardType.Power:
                        if (needsPowerCount)
                            powerCount++;
                        if (needsPowerEnergy)
                            powerEnergy += EnergyCost(battle, card, stats, segment);
                        break;
                    case BlitzCardType.Status:
                        if (needsStatusCount)
                            statusCount++;
                        break;
                }
                bool hasExhaustKeyword = (card.Keywords & BlitzKeywords.Exhaust) != 0;
                bool exhaustsOnPlay = hasExhaustKeyword
                    || skillsExhaust && cardType == BlitzCardType.Skill;
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
                    if (generated > 0 && (cardType == BlitzCardType.Power
                        || skillsExhaust && cardType == BlitzCardType.Skill
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
        int attackPlays = requirements.HasFlag(BlitzStrategicRequirements.AttackPlays)
            ? ReachablePlays(attackCount, deckSize, reachableCards) : 0;
        int skillPlays = requirements.HasFlag(BlitzStrategicRequirements.SkillPlays)
            ? ReachablePlays(skillCount, deckSize, reachableCards) : 0;
        int blockSkillPlays = requirements.HasFlag(BlitzStrategicRequirements.BlockSkillPlays)
            ? ReachablePlays(blockSkillCount, deckSize, reachableCards) : 0;
        int powerPlays = requirements.HasFlag(BlitzStrategicRequirements.PowerPlays)
            ? Math.Min(powerCount, reachableCards) : 0;
        int exhaustPlays = requirements.HasFlag(BlitzStrategicRequirements.ExhaustPlays)
            ? Math.Min(exhaustCount, reachableCards) : 0;
        int shivPlays = requirements.HasFlag(BlitzStrategicRequirements.ShivPlays)
            ? EstimatedShivPlays(shivCount, generatedShivCount, shivGeneratorCount, deckSize, reachableCards,
                reusableShivCount, singleUseGeneratedShivs, singleUseShivGenerators)
            : 0;
        int debuffApplications = requirements.HasFlag(BlitzStrategicRequirements.DebuffApplications)
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
            requirements.HasFlag(BlitzStrategicRequirements.AverageCardValue)
                ? Math.Max(1, totalCardValue / deckSize) : 1,
            requirements.HasFlag(BlitzStrategicRequirements.BestCardValue)
                ? Math.Max(1, bestCardValue) : 1,
            requirements.HasFlag(BlitzStrategicRequirements.AverageAttackValue) && attackCount > 0
                ? Math.Max(1, totalAttackValue / attackCount) : 0,
            requirements.HasFlag(BlitzStrategicRequirements.StatusDrawTriggers)
                ? Math.Min(remainingTurns, ReachablePlays(statusCount, deckSize, reachableCards)) : 0)
        {
            AttackHits = requirements.HasFlag(BlitzStrategicRequirements.AttackHits)
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
    private int FreeCardOpportunityValue(in BlitzBattle battle, BlitzCardType cardType, ushort powerId)
    {
        int freeUses = GetAmount(battle, powerId, BlitzLimits.PlayerOwner);
        if (freeUses <= 0)
            return 0;
        Span<int> values = stackalloc int[BlitzLimits.MaxHandSize];
        int count = 0;
        for (int index = 0; index < battle.HandCount; index++)
        {
            ref readonly BlitzCard card = ref battle.Cards[index];
            BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
            if (CardType(battle, card, stats) != cardType
                || (card.Flags & BlitzCardFlags.CostsX) != 0
                || !BlitzKernel.CanPlayCard(battle, Rules, index))
            {
                continue;
            }
            int normalCost = Math.Max(0, BlitzKernel.EnergyCostWithLocalModifiers(battle, card));
            int currentCost = Math.Max(0, BlitzKernel.EnergyCostWithModifiers(battle, Rules, card, BlitzPile.Hand));
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
    /// are summed. <see cref="BlitzPower.Aux0"/> is the power's CardsPlayedThisTurn counter.
    /// </summary>
    private int VoidFormOpportunityValue(in BlitzBattle battle)
    {
        if (_score.VoidFormPowerId == ushort.MaxValue)
            return 0;
        int powerIndex = BlitzKernel.FindPower(battle, BlitzLimits.PlayerOwner, _score.VoidFormPowerId);
        if (powerIndex < 0)
            return 0;
        int freeUses = battle.Powers[powerIndex].Amount - battle.Powers[powerIndex].Aux0;
        if (freeUses <= 0)
            return 0;
        Span<int> values = stackalloc int[BlitzLimits.MaxHandSize];
        int count = 0;
        for (int hand = 0; hand < battle.HandCount; hand++)
        {
            ref readonly BlitzCard card = ref battle.Cards[hand];
            BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
            if (stats.CostsX || stats.StarCostsX || !BlitzKernel.CanPlayCard(battle, Rules, hand))
                continue;
            int normalEnergy = Math.Max(0, BlitzKernel.EnergyCostWithLocalModifiers(battle, card));
            int currentEnergy = Math.Max(0, BlitzKernel.EnergyCostWithModifiers(battle, Rules, card, BlitzPile.Hand));
            int normalStars = Math.Max(0, stats.StarCost);
            int currentStars = Math.Max(0, BlitzKernel.StarCostWithModifiers(battle, Rules, card, BlitzPile.Hand));
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
    private (int Spend, int Capacity) CaptureEnergyRefundWindow(in BlitzBattle battle,
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
        BlitzBattle probe = battle;
        BlitzExec probeExec = new(ref probe, Rules, default);
        int maxEnergy = Math.Max(0, BlitzKernel.ModifiedMaxEnergy(ref probeExec));
        int handCount = battle.HandCount;
        int futureTurns = Math.Min(Math.Max(0, context.RemainingTurns - 1),
            (Math.Max(0, context.ReachableCards - handCount) + 5 - 1) / 5);
        long energySupply = Math.Max(0, battle.Energy) + (long)maxEnergy * futureTurns;
        double visits = (double)context.ReachableCards / liveCount;
        double energyDemand = 0;
        for (int pile = 0; pile < 3; pile++)
        {
            BlitzPile segment = pile switch
            {
                0 => BlitzPile.Discard,
                1 => BlitzPile.Draw,
                _ => BlitzPile.Hand,
            };
            int start = BlitzPiles.Start(battle, segment);
            int count = BlitzPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly BlitzCard card = ref battle.Cards[start + offset];
                BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
                if (card.HasKeyword(BlitzKeywords.Unplayable) || skillsExhaust && CardType(battle, card, stats) == BlitzCardType.Skill)
                    continue;
                int cost = (card.Flags & BlitzCardFlags.CostsX) != 0
                    ? maxEnergy
                    : Math.Max(0, BlitzKernel.EnergyCostWithLocalModifiers(battle, card));
                bool singleUse = CardType(battle, card, stats) == BlitzCardType.Power || card.HasKeyword(BlitzKeywords.Exhaust);
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
    private int ModifiedMaxEnergy(in BlitzBattle battle)
    {
        BlitzBattle probe = battle;
        BlitzExec probeExec = new(ref probe, Rules, default);
        return Math.Max(0, BlitzKernel.ModifiedMaxEnergy(ref probeExec));
    }

    /// <summary>
    /// <c>PersistentPowerSupport.GetModifiedHandDraw</c> through a copy. The base draw is the game's
    /// <c>CombatManager.baseHandDrawCount</c> (5), the same literal the existing capture uses.
    /// </summary>
    /// <summary><c>PersistentPowerSupport.GetModifiedHandDraw</c> for the commitment evidence port.</summary>
    internal int ModifiedHandDrawForEvidence(in BlitzBattle battle) => ModifiedHandDraw(battle);

    /// <summary><c>PersistentPowerSupport.GetModifiedMaxEnergy</c> for the commitment evidence port.</summary>
    internal int ModifiedMaxEnergyForEvidence(in BlitzBattle battle)
    {
        BlitzBattle probe = battle;
        BlitzExec probeExec = new(ref probe, Rules, default);
        return Math.Max(0, BlitzKernel.ModifiedMaxEnergy(ref probeExec));
    }

    /// <summary><c>CardChoiceSupport.CardValue</c> of a live card, with its current variables.</summary>
    internal double CardValueForEvidence(in BlitzBattle battle, in BlitzCard card)
    {
        BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
        BlitzCardScore score = _score.Cards[card.Id]?[card.Upgrade]
            ?? throw new InvalidOperationException($"评分表缺少卡牌 {card.Id}+{card.Upgrade}。");
        return CardValue(battle, card, stats, score);
    }

    /// <summary>A named dynamic variable's current value for the commitment evidence port.</summary>
    internal int VarForEvidence(in BlitzBattle battle, in BlitzCard card, int index)
    {
        BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
        BlitzCardScore score = CardScoreForEvidence(card.Id, card.Upgrade);
        return (int)Var(battle, card, stats, index);
    }

    /// <summary><c>CardMechanismFacts.AttackHits</c> for the commitment evidence port.</summary>
    internal int AttackHitsForEvidence(in BlitzBattle battle, in BlitzCard card)
    {
        BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
        BlitzCardScore score = CardScoreForEvidence(card.Id, card.Upgrade);
        return AttackHits(battle, score, card, stats);
    }

    /// <summary><c>CardMechanismFacts.ImmediateShivSupply</c> for the commitment evidence port.</summary>
    internal int ImmediateShivSupplyForEvidence(in BlitzBattle battle, in BlitzCard card)
    {
        BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
        BlitzCardScore score = CardScoreForEvidence(card.Id, card.Upgrade);
        return ImmediateShivSupply(battle, score, card, stats);
    }

    /// <summary>The compiled per-card score definition, for variable indexes and block/debuff flags.</summary>
    internal BlitzCardScore CardScoreForEvidence(ushort id, int upgrade)
        => _score.Cards[id]?[upgrade]
            ?? throw new InvalidOperationException($"评分表缺少卡牌 {id}+{upgrade}。");

    private int ModifiedHandDraw(in BlitzBattle battle)
    {
        BlitzBattle probe = battle;
        BlitzExec probeExec = new(ref probe, Rules, default);
        return Math.Max(0, BlitzKernel.ModifiedHandDraw(ref probeExec, 5));
    }

    /// <summary>
    /// Port of <c>the existing search's CaptureFirstAttackDamage</c>: the best total damage of a payable
    /// Attack in the live piles, counting every <c>Eradicate</c> hit the available energy pays for.
    /// The energy ceiling is the current energy or the modified maximum, whichever is larger.
    /// </summary>
    private int CaptureFirstAttackDamage(in BlitzBattle battle)
    {
        int energy = Math.Max(battle.Energy, ModifiedMaxEnergy(battle));
        int best = 0;
        for (int pile = 0; pile < 3; pile++)
        {
            BlitzPile segment = pile switch
            {
                0 => BlitzPile.Discard,
                1 => BlitzPile.Draw,
                _ => BlitzPile.Hand,
            };
            int start = BlitzPiles.Start(battle, segment);
            int count = BlitzPiles.Count(battle, segment);
            for (int offset = 0; offset < count; offset++)
            {
                ref readonly BlitzCard card = ref battle.Cards[start + offset];
                BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
                if (CardType(battle, card, stats) != BlitzCardType.Attack || card.HasKeyword(BlitzKeywords.Unplayable))
                    continue;
                BlitzCardScore score = _score.Card(card.Id, card.Upgrade);
                if (score.HasOstyAttackTag)
                    continue;
                if (BlitzKernel.EnergyCostWithModifiers(battle, Rules, card, segment) > energy)
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
            in BlitzBattle battle,
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
                BlitzPile segment = pile == 0 ? BlitzPile.Draw : BlitzPile.Discard;
                int start = BlitzPiles.Start(battle, segment);
                int count = BlitzPiles.Count(battle, segment);
                for (int offset = 0; offset < count; offset++)
                {
                    // the existing search's StateEvaluation uses `HasKeyword(State, Ethereal)` here too,
                    // so a Hexed card in the draw/discard pile is part of Pagestorm's projection.
                    if (BlitzKernel.HasKeyword(battle, battle.Cards[start + offset], BlitzKeywords.Ethereal))
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
                BlitzPile segment = pile switch
                {
                    0 => BlitzPile.Discard,
                    1 => BlitzPile.Draw,
                    _ => BlitzPile.Hand,
                };
                int start = BlitzPiles.Start(battle, segment);
                int count = BlitzPiles.Count(battle, segment);
                for (int offset = 0; offset < count; offset++)
                {
                    ref readonly BlitzCard card = ref battle.Cards[start + offset];
                    BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
                    if (card.HasKeyword(BlitzKeywords.Unplayable))
                        continue;
                    int cost = BlitzKernel.EnergyCostWithModifiers(battle, Rules, card, segment);
                    if (cost < danseMacabreEnergyThreshold)
                        continue;
                    highEnergyCards++;
                    highEnergyCosts[costCount++] = cost;
                    bool singleUse = CardType(battle, card, stats) == BlitzCardType.Power
                        || card.HasKeyword(BlitzKeywords.Exhaust)
                        || skillsExhaust && CardType(battle, card, stats) == BlitzCardType.Skill;
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
                BlitzPile segment = pile switch
                {
                    0 => BlitzPile.Discard,
                    1 => BlitzPile.Draw,
                    _ => BlitzPile.Hand,
                };
                int start = BlitzPiles.Start(battle, segment);
                int count = BlitzPiles.Count(battle, segment);
                for (int offset = 0; offset < count; offset++)
                {
                    ref readonly BlitzCard card = ref battle.Cards[start + offset];
                    if (card.HasKeyword(BlitzKeywords.Unplayable))
                        continue;
                    deckEnergyDemand += Math.Max(0,
                        BlitzKernel.EnergyCostWithModifiers(battle, Rules, card, segment));
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

    private static bool ShouldRetainThisTurn(in BlitzCard card)
        => (card.Flags & BlitzCardFlags.RetainThisTurn) != 0
            || (card.Keywords & BlitzKeywords.Retain) != 0;

    /// <summary><c>combat.RelicsOf(player).Any(relic =&gt; relic is Fiddle &amp;&amp; !relic.IsMelted)</c>.</summary>
    private bool HasUnmeltedFiddle(in BlitzBattle battle)
    {
        if (_score.FiddleRelicId == ushort.MaxValue)
            return false;
        for (int index = 0; index < battle.RelicCount; index++)
        {
            ref readonly BlitzRelic relic = ref battle.Relics[index];
            if (relic.Id == _score.FiddleRelicId
                && (relic.Flags & BlitzRelicFlags.Melted) == 0)
            {
                return true;
            }
        }
        return false;
    }

    private int EnergyCost(in BlitzBattle battle, in BlitzCard card, BlitzCardStats stats, BlitzPile pile)
    {
        if ((card.Flags & BlitzCardFlags.CostsX) != 0)
            return 0;
        // StrategicEffectModel.EnergyCost reads CostModifiers.All (local only), not the combat hooks.
        return Math.Max(0, BlitzKernel.EnergyCostWithLocalModifiers(battle, card));
    }

    private static int ReachablePlays(int matchingCards, int deckSize, int reachableCards)
        => matchingCards == 0
            ? 0
            : Math.Max(1, (int)Math.Ceiling((double)matchingCards * reachableCards / deckSize));

    private static int AttackHits(in BlitzBattle battle, BlitzCardScore score, in BlitzCard card,
        BlitzCardStats stats)
        => score.AttackHits switch
        {
            BlitzAttackHitKind.Two => 2,
            BlitzAttackHitKind.RepeatVar => Math.Max(0, (int)Var(battle, card, stats, score.RepeatVar)),
            _ => 1,
        };

    private static int ImmediateShivSupply(in BlitzBattle battle, BlitzCardScore score, in BlitzCard card,
        BlitzCardStats stats)
        => score.ShivSupply switch
        {
            BlitzShivSupplyKind.CardsVar => Math.Max(0, (int)Var(battle, card, stats, score.ShivCardsVar)),
            BlitzShivSupplyKind.ShivsVar => Math.Max(0, (int)Var(battle, card, stats, score.ShivShivsVar)),
            _ => 0,
        };

    private StrategicEffectContext WithExhaustDrawTiming(
        StrategicEffectContext context, in BlitzBattle battle, bool skillsExhaust)
    {
        int noDrawIndex = -1, embraceIndex = -1;
        int visit = 0;
        bool corruption = false;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly BlitzPower power = ref battle.Powers[index];
            if (power.Owner != BlitzLimits.PlayerOwner)
                continue;
            BlitzPowerScore rules = _score.Power(power.Id);
            if (power.Id == _score.NoDrawPowerId)
                noDrawIndex = visit;
            if (rules.Kind == BlitzStrategicKind.DarkEmbrace)
                embraceIndex = visit;
            if (rules.Kind == BlitzStrategicKind.Corruption)
                corruption = true;
            visit++;
        }
        if (embraceIndex < 0)
            return context;
        int ethereal = 0, exhaustingEthereal = 0;
        for (int offset = 0; offset < battle.HandCount; offset++)
        {
            ref readonly BlitzCard card = ref battle.Cards[offset];
            if ((card.Keywords & BlitzKeywords.Ethereal) == 0)
                continue;
            ethereal++;
            BlitzCardStats stats = Rules.Card(card.Id, card.Upgrade);
            if ((card.Keywords & BlitzKeywords.Exhaust) != 0
                || (corruption || skillsExhaust) && CardType(battle, card, stats) == BlitzCardType.Skill)
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
        BlitzStrategicKind kind, int amountRaw, in StrategicEffectContext context)
    {
        int amount = Math.Max(1, amountRaw);
        int enemyHp = context.EnemyHp;
        int energyUnit = Math.Max(3, context.AverageCardValue);
        int cardAccessUnit = context.AverageCardValue;
        return kind switch
        {
            BlitzStrategicKind.Afterimage => Prevention(amount * context.UsefulCardPlays, context),
            BlitzStrategicKind.Buffer => Prevention(BufferPrevention(amount, context), context),
            BlitzStrategicKind.FeelNoPain => Prevention(amount * context.ExhaustPlays, context),
            BlitzStrategicKind.DarkEmbrace => CardAccess(
                amount * (context.ExhaustDrawPlays ?? context.ExhaustPlays) * cardAccessUnit),
            BlitzStrategicKind.Thorns => Damage(amount * context.IncomingHitCount, enemyHp),
            BlitzStrategicKind.EchoForm when context.UsefulCardPlays > 0 => Damage(
                context.BestCardValue
                    * Math.Min(amount, Math.Max(1, context.UsefulCardPlays))
                    * context.RemainingTurns,
                enemyHp),
            BlitzStrategicKind.EchoForm => default,
            BlitzStrategicKind.Envenom => Damage(amount * context.AttackPlays, enemyHp),
            BlitzStrategicKind.Accuracy => Damage(amount * context.ShivPlays, enemyHp),
            BlitzStrategicKind.SleightOfFlesh => Damage(amount * context.DebuffApplications, enemyHp),
            BlitzStrategicKind.Strength => Damage(
                amount * (context.AttackHits ?? context.AttackPlays), enemyHp),
            BlitzStrategicKind.Lethality when context.Act3BossInteractions && context.AttackPlays > 0
                => Damage(
                    context.FirstAttackDamage * Math.Min(context.AttackPlays, context.RemainingTurns)
                        * amount / 100,
                    enemyHp),
            BlitzStrategicKind.Lethality when context.AttackPlays > 0 => Damage(
                context.AverageAttackValue
                    * Math.Min(context.AttackPlays, context.RemainingTurns)
                    * amount / 100,
                enemyHp),
            BlitzStrategicKind.ReaperForm when context.AttackPlays > 0 => Damage(
                context.AverageAttackValue * context.AttackPlays * amount, enemyHp),
            BlitzStrategicKind.Lethality or BlitzStrategicKind.ReaperForm => default,
            BlitzStrategicKind.Dexterity => Prevention(amount * context.BlockSkillPlays, context),
            BlitzStrategicKind.DemonForm when context.AttackPlays > 0 => Damage(
                amount * Math.Max(1, context.AttackPlays / Math.Max(1, context.RemainingTurns))
                    * context.RemainingTurns * (context.RemainingTurns + 1) / 2,
                enemyHp),
            BlitzStrategicKind.DemonForm => default,
            BlitzStrategicKind.Curious => Resource(
                Math.Min(context.PowerEnergySpend, amount * context.PowerPlays) * energyUnit),
            BlitzStrategicKind.Corruption => Resource(context.SkillEnergySpend * energyUnit),
            BlitzStrategicKind.RecurringEnergy or BlitzStrategicKind.OrbitEnergy
                => Resource(context.RecurringEnergyGain * energyUnit),
            BlitzStrategicKind.CreativeAi => CardAccess(
                amount * context.RemainingTurns * cardAccessUnit),
            BlitzStrategicKind.Iteration => CardAccess(
                amount * context.StatusDrawTriggers * cardAccessUnit),
            BlitzStrategicKind.MasterPlanner => CardAccess(context.SkillPlays * cardAccessUnit),
            BlitzStrategicKind.Pagestorm when context.Act3BossInteractions => CardAccess(
                Math.Min(
                    context.PagestormBonusDrawCapacity,
                    amount * context.EtherealDrawTriggers) * cardAccessUnit),
            BlitzStrategicKind.DanseMacabre when context.Act3BossInteractions => Prevention(
                amount * context.HighEnergyPlays,
                context),
            BlitzStrategicKind.Demesne when context.Act3BossInteractions =>
                Resource(context.DemesneEnergyGain * energyUnit)
                + CardAccess(context.DemesneDrawGain * cardAccessUnit),
            BlitzStrategicKind.PrepTime when context.Act3BossInteractions => Damage(
                amount * Math.Min(context.RemainingTurns, context.AttackPlays), enemyHp),
            BlitzStrategicKind.Focus => Scaling(amount * context.RemainingTurns * 2),
            BlitzStrategicKind.Furnace => Scaling(amount * context.RemainingTurns * 2),
            BlitzStrategicKind.Thunder => Damage(amount * context.RemainingTurns, enemyHp),
            BlitzStrategicKind.LightningRod => Scaling(amount * context.RemainingTurns),
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

    public double BeamRankScore(in BlitzBattle battle, in BlitzScoreFeatures features,
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
    /// <c>ContextualRankingModel.Adjustment</c> for a Blitz state. Terminal and non-surviving
    /// contexts bypass the model; the four retention-only inputs are recomputed here because the
    /// ordinary child score skips them, and the rest are the K4-checked features.
    /// </summary>
    private double ContextualAdjustment(in BlitzBattle battle, in BlitzScoreFeatures features,
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

    internal double ContextualAdjustmentForTesting(in BlitzBattle battle,
        in BlitzScoreFeatures features, bool terminal)
        => ContextualAdjustment(battle, features, terminal);

    private int RetainedAttackGrowth(in BlitzScoreFeatures features)
        => Math.Min(
            SolverWeights.RetainedAttackGrowthBeamCap,
            Math.Max(0, features.RetainedAttackValue - _score.InitialRetainedAttackValue));
}
