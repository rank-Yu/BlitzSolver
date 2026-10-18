using CombatSolver.Engine.InCombat.Flash;

namespace CombatSolver.FlashBeam;

/// <summary>
/// Ports the existing search's state evaluation to <see cref="FlashBattle"/>. Every computation follows
/// <c>CombatBeamSolver.Snapshot</c> (CombatBeamSolver.StateEvaluation.cs) and
/// <c>BeamRetentionPolicy.BeamRankScore</c> (CombatBeamSolver.BeamRetentionPolicy.Ranking.cs) exactly:
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

    // ---------------------------------------------------------------------------------------------
    // Card values (CardChoiceSupport.CardValue / DynamicVarBaseValue, plus BranchCardValue extras).

    /// <summary>Current value of variable <paramref name="index"/>: canonical value plus the instance delta.</summary>
    private static decimal Var(in FlashCard card, FlashCardStats stats, int index)
    {
        if (index < 0)
            return 0m;
        decimal delta = index switch
        {
            0 => card.Var0,
            1 => card.Var1,
            _ => 0,
        };
        return stats.Vars[index] + delta;
    }

    /// <summary>Port of <c>CardChoiceSupport.CardValue</c> with the instance's variable deltas applied.</summary>
    private double CardValue(in FlashCard card, FlashCardStats stats, FlashCardScore score)
        => score.BaseDamage + DeltaDouble(card, score.DamageVar)
            + (score.BaseBlock + DeltaDouble(card, score.BlockVar)) * 0.8d
            + (score.BaseCards + DeltaDouble(card, score.CardsVar)) * 3d
            + (score.IsPower ? 8d : 0d);

    private static double DeltaDouble(in FlashCard card, int index) => index switch
    {
        0 => card.Var0,
        1 => card.Var1,
        _ => 0d,
    };

    /// <summary>Port of <c>CombatBeamSolver.BranchCardValue</c> (calculated damage where registered).</summary>
    private double BranchCardValue(in FlashBattle battle, in FlashCard card, FlashCardStats stats, FlashCardScore score)
    {
        double value = CardValue(card, stats, score);
        if (stats.Type != FlashCardType.Attack || score.Calculated == FlashCalculatedKind.None)
            return value;
        decimal baseVar = Var(card, stats, score.CalculationBaseVar);
        decimal extraVar = Var(card, stats, score.CalculationExtraVar);
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
                _ => 0,
            };
            decimal candidate = baseVar + extraVar * multiplier;
            damage = Math.Max(damage, candidate);
        }
        return value + (double)damage - score.BaseDamage;
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
    // Snapshot.

    public FlashScoreFeatures Features(in FlashBattle battle, int actionCount)
        => Evaluate(battle, actionCount);

    private FlashScoreFeatures Evaluate(in FlashBattle battle, int actionCount)
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
        // The K4 probe only compares states the existing engine left at a clean search boundary, so the
        // snapshot's boundary is None and the threat projection always runs unless the state is won.
        bool risk = false;
        int projectedHp;
        int projectedDeathSaveHpRestored = 0;
        int projectedDeathSaveUseCount = 0;
        int blockConsumed = 0;
        if (won)
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
        int longTermResourceValue = realizedLongTermResourceValue;
        int growthHpCredit = 0;
        score += Math.Min(
            realizedLongTermResourceValue * SolverWeights.LongTermResourceBeamValue,
            SolverWeights.LongTermResourceBeamCap);
        score += (double)growthHpCredit * hpWeight;

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
                if ((stats.Type is FlashCardType.Status or FlashCardType.Curse)
                    && !LeavesHandAtTurnEnd(battle, segment, card, cardScore))
                {
                    liveDeckClutter = checked(liveDeckClutter + 1);
                }
            }
        }
        score += liveDeckClutter * SolverWeights.LiveDeckClutterPenalty;

        // Theft content is rejected at admission; a PreserveResources policy therefore always reads 0.
        int outstandingStolenResource = 0;
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
                if (stats.Type != FlashCardType.Attack)
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
        }

        StrategicEffectContext? strategicContext = null;
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
                strategicContext = needsExhaustDrawTiming
                    ? WithExhaustDrawTiming(context, battle, skillsExhaust) : context;
            }
            StrategicEffectVector effect = EvaluateStrategic(rules.Kind, power.Amount, strategicContext.Value);
            strategicEffects += effect;
            offensivePersistentBuffValue += effect.DamagePotential + effect.ScalingPotential;
            persistentSetupTraits |= rules.Trait;
        }
        int persistentBuffValue = strategicEffects.RetentionValue;

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
                latentSetupTraits |= trait;
                if (trait == FlashSetupTraits.None || !persistentSetupTraits.HasFlag(trait))
                    latentSetupValue += cardScore.LatentValue;
            }
        }

        int replayPotentialValue = ReplayPotentialValue(battle);

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
                (int)Math.Ceiling(CardValue(card, stats, cardScore) * 2d)));
        }
        // Free-card opportunity, Void Form and summon next turn have no admitted Flash content.
        int futureResourceValue = GetAmount(battle, _score.RetainHandPowerId, FlashLimits.PlayerOwner) * 4
            + retainedHandValue;

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

        // Sandpit is not admitted in the Flash content closure.
        int sandpitRemaining = 0;

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

        int defensiveBlockValue = MeasureDefensiveBlockReserve(battle, blockConsumed);

        return new FlashScoreFeatures
        {
            Score = score,
            PlayerDead = dead,
            AllEnemiesDead = won,
            PlayerHp = battle.Player.CurrentHp,
            PlayerMaxHp = battle.Player.MaxHp,
            PlayerBlock = battle.Player.Block,
            CumulativePlayerHpLost = cumulativePlayerHpLost,
            RecoveredPlayerHp = recoveredPlayerHp,
            DeathSaveHpRestored = deathSaveHpRestored,
            DeathSaveUseCount = deathSaveUseCount,
            ProjectedDeathSaveUseCount = deathSaveUseCount + projectedDeathSaveUseCount,
            ProjectedPlayerHp = projectedHp,
            DefensiveBlockValue = defensiveBlockValue,
            LongTermResourceValue = longTermResourceValue,
            GrowthHpCredit = growthHpCredit,
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
            RetainedAttackValue = retainedAttackValue,
            FocusTargetCombatId = focus.CombatId,
            FocusTargetPressure = focus.Pressure,
            FocusTargetRemainingHp = focus.RemainingHp,
            FocusTargetCurrentThreat = focus.CurrentThreat,
            FocusTargetVulnerableTurns = focusTargetVulnerableTurns,
            MostVulnerableTargetCombatId = mostVulnerableTargetCombatId,
            EnemyVulnerableTurns = vulnerable,
            Energy = battle.Energy,
            PersistentBuffValue = persistentBuffValue,
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
        };
    }

    private int EffectiveEnemyHp(in FlashBattle battle, int index)
    {
        ref readonly FlashEnemy enemy = ref battle.Enemies[index];
        if (enemy.Creature.CurrentHp > 0)
            return enemy.Creature.CurrentHp;
        if (enemy.DeathPhase != FlashDeathPhase.Reviving)
            return 0;
        // Only Illusion-powered revives are representable in the admitted Flash content; its amount is
        // irrelevant because the restored HP is the captured max HP.
        return HasPower(battle, _score.IllusionPowerId, FlashKernel.EnemyOwner((byte)index))
            ? enemy.Creature.MaxHp : 0;
    }

    private bool LeavesHandAtTurnEnd(in FlashBattle battle, FlashPile pile, in FlashCard card, FlashCardScore score)
    {
        if (pile != FlashPile.Hand)
            return false;
        if (score.HasTurnEndInHandEffect)
            return false;
        // Hook.ShouldEtherealTrigger has no supported listeners, so it is always true here.
        return (card.Keywords & FlashKeywords.Ethereal) != 0;
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
                int replayCount = Math.Max(0, (int)card.ReplayCount);
                if (replayCount == 0
                    || stats.Type is not (FlashCardType.Attack or FlashCardType.Skill or FlashCardType.Power))
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
        FlashBattle probe = battle;
        FlashExec x = new(ref probe, Rules, default);
        int hp = battle.Player.CurrentHp;
        int block = battle.Player.Block;
        int originalBlock = block;
        ProjectedDeathPrevention prevention = new(battle, _score, Rules);
        for (int position = 0; position < battle.RosterCount; position++)
        {
            ref readonly FlashEnemy enemy = ref battle.Enemies[battle.Roster[position]];
            byte owner = FlashKernel.EnemyOwner(battle.Roster[position]);
            if (!enemy.Creature.IsAlive)
                continue;
            if ((enemy.Flags & FlashEnemyFlags.SkipNextMove) != 0)
                continue;
            // Forced moves are not part of the admitted Act-1 content closure; a forced node would have
            // been rejected at admission.
            FlashMonsterNode move = Rules.MonsterRule(enemy.Rules).Nodes[enemy.Move];
            for (int attackIndex = 0; attackIndex < move.Attacks.Length; attackIndex++)
            {
                FlashMonsterAttack attack = move.Attacks[attackIndex];
                int repeats = Math.Max(attack.Repeats, 1);
                for (int hit = 0; hit < repeats; hit++)
                {
                    ProjectThreatHit(ref x, owner, attack.BaseDamage, ref block, ref hp, ref prevention);
                }
            }
        }
        return new ThreatProjection(hp, prevention.DeathSaveHpRestored, prevention.UseCount,
            Math.Max(0, originalBlock - block));
    }

    private void ProjectThreatHit(ref FlashExec x, byte attacker, int baseDamage,
        ref int block, ref int hp, ref ProjectedDeathPrevention prevention)
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
        // The unblocked-damage target is the player: Osty is rejected at admission.
        hpLoss = ProjectedModifyHpLost(ref x, loss, hpLoss, afterOsty: true, changed, out int changedCount);
        // The projected Buffer consumption is local to this forecast: notify the changed listeners so the
        // probe's Buffer amount drops, exactly as ProjectedHpLossModifiers.Consume does in the engine.
        FlashHooks.AfterModifyingHpLostAfterOsty(ref x, changed[..changedCount]);
        int damage = Math.Max(0, (int)Math.Floor(hpLoss));
        hp -= damage;
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

    /// <summary>Port of <c>CombatBeamSolver.BuildProjectedDeathPrevention</c>.</summary>
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
        FlashBattle probe = battle;
        FlashExec x = new(ref probe, Rules, default);
        if (ShouldClearBlock(ref x, FlashLimits.PlayerOwner, out FlashListener preventer))
            return consumed;
        int retained = FlashHooks.Definition(probe, preventer)
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
                    int repeats = Math.Max(attack.Repeats, 1);
                    for (int hit = 0; hit < repeats; hit++)
                    {
                        incomingHitCount++;
                        currentThreat += Math.Max(0, attack.BaseDamage);
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
                FlashCardType cardType = stats.Type;
                int cardValue = 0;
                if (needsAllCardValues || (needsAttackValues && cardType == FlashCardType.Attack))
                {
                    cardValue = Math.Max(1, (int)Math.Ceiling(CardValue(card, stats, cardScore)));
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
                            attackHitCount += AttackHits(cardScore, card, stats);
                        }
                        break;
                    case FlashCardType.Skill:
                        if (needsSkillCount)
                            skillCount++;
                        if (needsSkillEnergy)
                            skillEnergy += EnergyCost(battle, card, stats);
                        if (needsBlockSkillCount && cardScore.HasBlockDynamicVar)
                            blockSkillCount++;
                        break;
                    case FlashCardType.Power:
                        if (needsPowerCount)
                            powerCount++;
                        if (needsPowerEnergy)
                            powerEnergy += EnergyCost(battle, card, stats);
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
                    int generated = ImmediateShivSupply(cardScore, card, stats);
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

    private static int EnergyCost(in FlashBattle battle, in FlashCard card, FlashCardStats stats)
    {
        if ((card.Flags & FlashCardFlags.CostsX) != 0)
            return 0;
        return Math.Max(0, FlashKernel.EnergyCostWithModifiers(battle, card));
    }

    private static int ReachablePlays(int matchingCards, int deckSize, int reachableCards)
        => matchingCards == 0
            ? 0
            : Math.Max(1, (int)Math.Ceiling((double)matchingCards * reachableCards / deckSize));

    private static int AttackHits(FlashCardScore score, in FlashCard card, FlashCardStats stats)
        => score.AttackHits switch
        {
            FlashAttackHitKind.Two => 2,
            FlashAttackHitKind.RepeatVar => Math.Max(0, (int)Var(card, stats, score.RepeatVar)),
            _ => 1,
        };

    private static int ImmediateShivSupply(FlashCardScore score, in FlashCard card, FlashCardStats stats)
        => score.ShivSupply switch
        {
            FlashShivSupplyKind.CardsVar => Math.Max(0, (int)Var(card, stats, score.ShivCardsVar)),
            FlashShivSupplyKind.ShivsVar => Math.Max(0, (int)Var(card, stats, score.ShivShivsVar)),
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
                || (corruption || skillsExhaust) && stats.Type == FlashCardType.Skill)
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
            FlashStrategicKind.CreativeAi => CardAccess(
                amount * context.RemainingTurns * cardAccessUnit),
            FlashStrategicKind.Iteration => CardAccess(
                amount * context.StatusDrawTriggers * cardAccessUnit),
            FlashStrategicKind.MasterPlanner => CardAccess(context.SkillPlays * cardAccessUnit),
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

    public double BeamRankScore(in FlashScoreFeatures features)
    {
        // CombatBeamSolver.BeamRankScore: the base-score member ranks by the base score alone (the
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
        double score = features.Score
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
        // The existing ranking scales one term for non-terminal nodes only; FlashBeam ranks only
        // non-terminal states (terminals go straight to the final route ordering).
        if (_score.BeamWeightPerturbation is { Scale: not 1d } perturbation)
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
        return score;
    }

    private int RetainedAttackGrowth(in FlashScoreFeatures features)
        => Math.Min(
            SolverWeights.RetainedAttackGrowthBeamCap,
            Math.Max(0, features.RetainedAttackValue - _score.InitialRetainedAttackValue));
}
