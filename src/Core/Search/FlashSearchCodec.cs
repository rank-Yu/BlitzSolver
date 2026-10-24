using System.Text;
using CombatSolver.Flash.Kernel;
using CombatSolver.Flash.Search.Scoring;

namespace CombatSolver.Flash.Search;

/// <summary>
/// One complete Flash search request: the frozen rules, every root battle and the exact
/// <see cref="FlashBeamOptions"/> the bridge constructed.
/// </summary>
internal sealed record FlashSearchCase(FlashRules Rules, FlashBattle[] Battles, FlashBeamOptions Options);

/// <summary>
/// Deterministic encoding of one <see cref="FlashBeamSolver"/> request and result. The request
/// carries the complete rule and score/bound tables Flash reads, so a decoded request can be solved
/// without the existing engine. Host-only callbacks (<see cref="FlashBeamOptions.Pacer"/>,
/// <see cref="FlashBeamOptions.Progress"/>, <see cref="FlashBeamOptions.PollTakeover"/>,
/// <see cref="FlashBeamOptions.Trace"/>) are recorded as presence bits and not reconstructed: they
/// do not participate in route selection, but the report lists them explicitly.
/// </summary>
internal static class FlashSearchCodec
{
    private const uint RequestMagic = 0x31535246; // "FRS1"
    private const uint ResultMagic = 0x31535252;  // "FRR1"

    internal static byte[] EncodeRequest(FlashSearchCase search)
        => FlashCodec.ToBytes(writer =>
        {
            writer.Write(RequestMagic);
            writer.Write(FlashCodec.FormatVersion);
            byte[] rules = FlashRulesCodec.Encode(search.Rules);
            writer.Write(rules.Length);
            writer.Write(rules);
            writer.Write(search.Battles.Length);
            foreach (FlashBattle battle in search.Battles)
            {
                byte[] bytes = FlashBattleCodec.Encode(battle);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
            EncodeOptions(writer, search.Options, search.Rules);
        });

    internal static FlashSearchCase DecodeRequest(byte[] bytes)
        => FlashCodec.Read(bytes, "Flash 搜索请求", reader =>
        {
            if (reader.ReadUInt32() != RequestMagic)
                throw new InvalidDataException("Flash 搜索请求魔数不符。");
            ushort version = reader.ReadUInt16();
            if (version != FlashCodec.FormatVersion)
                throw new InvalidDataException($"Flash 搜索请求版本 {version} 不受支持。");
            int rulesLength = FlashCodec.ReadCount(reader, 1 << 28, "规则字节");
            byte[] rulesBytes = reader.ReadBytes(rulesLength);
            if (rulesBytes.Length != rulesLength)
                throw new InvalidDataException("Flash 搜索请求的规则数据被截断。");
            FlashRules rules = FlashRulesCodec.Decode(rulesBytes);
            int battleCount = FlashCodec.ReadCount(reader, 4096, "搜索根");
            FlashBattle[] battles = new FlashBattle[battleCount];
            for (int index = 0; index < battleCount; index++)
            {
                int length = FlashCodec.ReadCount(reader,
                    FlashBattleCodec.HeaderSize + FlashBattle.ExpectedSize, "搜索根字节");
                byte[] battleBytes = reader.ReadBytes(length);
                if (battleBytes.Length != length)
                    throw new InvalidDataException("Flash 搜索根数据被截断。");
                battles[index] = FlashBattleCodec.Decode(battleBytes);
            }
            FlashBeamOptions options = DecodeOptions(reader, rules);
            return new FlashSearchCase(rules, battles, options);
        });

    private static void EncodeOptions(BinaryWriter writer, FlashBeamOptions options, FlashRules rules)
    {
        writer.Write(options.BeamWidth);
        writer.Write(options.MaxExpandedNodes);
        writer.Write(options.MaxTurns);
        writer.Write(options.SoftTimeBudget.Ticks);
        writer.Write(options.InitialPlayerMaxHp);
        writer.Write(options.CandidateScanCount);
        EncodePotionPolicy(writer, options.Potions);
        writer.Write(options.Scorer != null);
        if (options.Scorer != null)
            EncodeScoreRules(writer, options.Scorer.RulesForCodec);
        writer.Write(options.Ordering != null);
        if (options.Ordering is { } ordering)
            EncodeOrdering(writer, ordering);
        writer.Write(options.HpTargetStop != null);
        if (options.HpTargetStop is { } stop)
            EncodeHpTargetStop(writer, stop);
        writer.Write(options.Bound != null);
        if (options.Bound is { } bound)
            EncodeBoundConfig(writer, bound.ConfigForCodec);
        writer.Write(options.Incumbent != null);
        if (options.Incumbent is { } incumbent)
        {
            writer.Write(incumbent.StrategicHpDeficit);
            writer.Write(incumbent.CombatEndedTurn);
            writer.Write(incumbent.ExplicitPotionStrategicCost != null);
            writer.Write(incumbent.ExplicitPotionStrategicCost ?? 0);
        }
        writer.Write((int)options.EffectivePotionPolicy);
        writer.Write(options.TheftPolicy != null);
        if (options.TheftPolicy is { } theft)
            writer.Write((int)theft);
        EncodeForcedDirectives(writer, options.ForcedPotionDirectives);
        writer.Write(options.EnforceForcedPotionDirectives);
        writer.Write(options.MinimumPotionUses);
        writer.Write(options.InitialExplicitPotionUses);
        writer.Write(options.InitialAutomaticPotionUses);
        writer.Write(options.PotionFreeBaseline != null);
        if (options.PotionFreeBaseline is { } baseline)
            EncodePotionFreeBaseline(writer, baseline);
        writer.Write(options.PotionReplacementHpCredit);
        writer.Write(options.SmartPotionEligibilityHpCeiling != null);
        writer.Write(options.SmartPotionEligibilityHpCeiling ?? 0);
        writer.Write(options.AmbergrisPotionId);
        writer.Write(options.InitialAmbergrisUses);
        writer.Write(options.AuditPotionPolicy);
        writer.Write(options.InitialActionCount);
        writer.Write(options.Act3BossStrategy);
        writer.Write(options.PlanCommitment != null);
        if (options.PlanCommitment is { } plan)
            EncodePlanCommitment(writer, plan);
        writer.Write(options.InitialPlanPayoffCount);
        writer.Write(options.EarlyTurnScoutDepth);
        writer.Write(options.Novelty != null);
        if (options.Novelty is { } novelty)
        {
            writer.Write(novelty.Width);
            writer.Write(novelty.FamiliarAllowance);
            writer.Write(novelty.MaxOpen);
            writer.Write(novelty.MaxNoveltyEntries);
            writer.Write(novelty.MaxActions);
            writer.Write(novelty.MaxTurns);
        }
        writer.Write(options.InitialPlayerTurn);
        writer.Write(options.InitialSearchTurn);
        writer.Write(options.SecondRankBand);
        writer.Write(options.SecondRankBandWidth);
        writer.Write(options.AggressivePowerCommitment);
        writer.Write(options.PreserveReplayAllocatorOpening);
        writer.Write(options.PowerCommitments != null);
        if (options.PowerCommitments is { } commitments)
            EncodePowerCommitments(writer, commitments);
        byte hooks = 0;
        if (options.Pacer != null) hooks |= 1;
        if (options.Progress != null) hooks |= 2;
        if (options.PollTakeover != null) hooks |= 4;
        if (options.Trace != null) hooks |= 8;
        writer.Write(hooks);
    }

    private static FlashBeamOptions DecodeOptions(BinaryReader reader, FlashRules rules)
    {
        int beamWidth = reader.ReadInt32();
        int maxExpanded = reader.ReadInt32();
        int maxTurns = reader.ReadInt32();
        TimeSpan budget = TimeSpan.FromTicks(reader.ReadInt64());
        int initialHp = reader.ReadInt32();
        int scan = reader.ReadInt32();
        FlashPotionPolicy potions = DecodePotionPolicy(reader);
        bool hasScorer = FlashCodec.ReadBool(reader);
        FlashScorer? scorer = null;
        if (hasScorer)
            scorer = new FlashScorer(DecodeScoreRules(reader, rules));
        bool hasOrdering = FlashCodec.ReadBool(reader);
        FlashRouteOrdering? ordering = hasOrdering ? DecodeOrdering(reader) : null;
        bool hasStop = FlashCodec.ReadBool(reader);
        FlashHpTargetStop? stop = hasStop ? DecodeHpTargetStop(reader) : null;
        bool hasBound = FlashCodec.ReadBool(reader);
        FlashBoundFeatures? bound = hasBound ? new FlashBoundFeatures(DecodeBoundConfig(reader)) : null;
        bool hasIncumbent = FlashCodec.ReadBool(reader);
        PrimarySearchIncumbent? incumbent = null;
        if (hasIncumbent)
        {
            int deficit = reader.ReadInt32();
            int turn = reader.ReadInt32();
            bool hasCost = FlashCodec.ReadBool(reader);
            int costValue = reader.ReadInt32();
            incumbent = new PrimarySearchIncumbent(deficit, turn, hasCost ? costValue : null);
        }
        SolverPotionPolicy effective = ReadEnumInt<SolverPotionPolicy>(reader, "有效药水政策");
        SolverTheftPolicy? theft = FlashCodec.ReadBool(reader)
            ? ReadEnumInt<SolverTheftPolicy>(reader, "窃取政策") : null;
        FlashForcedPotionDirectives forced = DecodeForcedDirectives(reader);
        bool enforceForced = FlashCodec.ReadBool(reader);
        int minPotions = reader.ReadInt32();
        int initialExplicit = reader.ReadInt32();
        int initialAutomatic = reader.ReadInt32();
        bool hasBaseline = FlashCodec.ReadBool(reader);
        PotionFreePolicyBaseline? baseline = hasBaseline ? DecodePotionFreeBaseline(reader) : null;
        int replacementCredit = reader.ReadInt32();
        bool hasCeiling = FlashCodec.ReadBool(reader);
        int ceilingValue = reader.ReadInt32();
        int? ceiling = hasCeiling ? ceilingValue : null;
        ushort ambergris = reader.ReadUInt16();
        int initialAmbergris = reader.ReadInt32();
        bool audit = FlashCodec.ReadBool(reader);
        int initialAction = reader.ReadInt32();
        bool act3 = FlashCodec.ReadBool(reader);
        bool hasPlan = FlashCodec.ReadBool(reader);
        FlashPlanCommitment? plan = hasPlan ? DecodePlanCommitment(reader) : null;
        int initialPayoff = reader.ReadInt32();
        int scoutDepth = reader.ReadInt32();
        bool hasNovelty = FlashCodec.ReadBool(reader);
        FlashNoveltyOptions? novelty = hasNovelty
            ? new FlashNoveltyOptions(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32())
            : null;
        int initialTurn = reader.ReadInt32();
        int initialSearchTurn = reader.ReadInt32();
        bool secondRank = FlashCodec.ReadBool(reader);
        int secondRankWidth = reader.ReadInt32();
        bool aggressive = FlashCodec.ReadBool(reader);
        bool preserveOpening = FlashCodec.ReadBool(reader);
        bool hasCommitments = FlashCodec.ReadBool(reader);
        FlashPowerCommitmentCatalog? commitments = hasCommitments ? DecodePowerCommitments(reader) : null;
        byte hooks = reader.ReadByte();
        if ((hooks & ~0x0F) != 0)
            throw new InvalidDataException($"Flash 搜索请求的宿主回调位 {hooks:X} 非法。");
        return new FlashBeamOptions(beamWidth, maxExpanded, maxTurns, budget, initialHp)
        {
            CandidateScanCount = scan,
            Potions = potions,
            Scorer = scorer,
            Ordering = ordering,
            HpTargetStop = stop,
            Bound = bound,
            Incumbent = incumbent,
            EffectivePotionPolicy = effective,
            TheftPolicy = theft,
            ForcedPotionDirectives = forced,
            EnforceForcedPotionDirectives = enforceForced,
            MinimumPotionUses = minPotions,
            InitialExplicitPotionUses = initialExplicit,
            InitialAutomaticPotionUses = initialAutomatic,
            PotionFreeBaseline = baseline,
            PotionReplacementHpCredit = replacementCredit,
            SmartPotionEligibilityHpCeiling = ceiling,
            AmbergrisPotionId = ambergris,
            InitialAmbergrisUses = initialAmbergris,
            AuditPotionPolicy = audit,
            InitialActionCount = initialAction,
            Act3BossStrategy = act3,
            PlanCommitment = plan,
            InitialPlanPayoffCount = initialPayoff,
            EarlyTurnScoutDepth = scoutDepth,
            Novelty = novelty,
            InitialPlayerTurn = initialTurn,
            InitialSearchTurn = initialSearchTurn,
            SecondRankBand = secondRank,
            SecondRankBandWidth = secondRankWidth,
            AggressivePowerCommitment = aggressive,
            PreserveReplayAllocatorOpening = preserveOpening,
            PowerCommitments = commitments,
        };
    }

    private static void EncodePotionPolicy(BinaryWriter writer, FlashPotionPolicy policy)
    {
        writer.Write(policy.EarliestPlayerTurn);
        writer.Write(policy.MaximumUses != null);
        writer.Write(policy.MaximumUses ?? 0);
        EncodePotionStrategy(writer, policy.Strategy);
        writer.Write((int)policy.EffectivePolicy);
        writer.Write(policy.ForceAllDisabled);
    }

    private static FlashPotionPolicy DecodePotionPolicy(BinaryReader reader)
    {
        int earliest = reader.ReadInt32();
        bool hasMaximum = FlashCodec.ReadBool(reader);
        int maximumValue = reader.ReadInt32();
        int? maximum = hasMaximum ? maximumValue : null;
        PotionStrategySnapshot strategy = DecodePotionStrategy(reader);
        SolverPotionPolicy effective = ReadEnumInt<SolverPotionPolicy>(reader, "药水政策");
        bool forceDisabled = FlashCodec.ReadBool(reader);
        return new FlashPotionPolicy(earliest, maximum, strategy, effective, forceDisabled);
    }

    private static void EncodePotionStrategy(BinaryWriter writer, PotionStrategySnapshot strategy)
    {
        writer.Write((int)strategy.DefaultPolicy);
        writer.Write(strategy.Directives.Count);
        foreach (PotionSlotDirective directive in strategy.Directives)
        {
            writer.Write(directive.Slot);
            FlashCodec.WriteString(writer, directive.PotionId);
            writer.Write((int)directive.Directive);
        }
    }

    private static PotionStrategySnapshot DecodePotionStrategy(BinaryReader reader)
    {
        SolverPotionPolicy defaultPolicy = ReadEnumInt<SolverPotionPolicy>(reader, "药水默认政策");
        int count = FlashCodec.ReadCount(reader, FlashLimits.PotionSlots, "药水指令");
        PotionSlotDirective[] directives = new PotionSlotDirective[count];
        for (int index = 0; index < count; index++)
        {
            int slot = reader.ReadInt32();
            string potionId = FlashCodec.ReadString(reader)
                ?? throw new InvalidDataException("药水指令 id 缺失。");
            directives[index] = new PotionSlotDirective(slot, potionId,
                ReadEnumInt<SolverPotionDirective>(reader, "药水指令"));
        }
        return new PotionStrategySnapshot(defaultPolicy, directives);
    }

    private static void EncodeOrdering(BinaryWriter writer, FlashRouteOrdering ordering)
    {
        writer.Write(ordering.InitialPlayerHp);
        writer.Write(ordering.InitialPlayerMaxHp);
        writer.Write((int)ordering.BossHpRelief);
        EncodePostCombatHeal(writer, ordering.PostCombatRelicHeal);
    }

    private static FlashRouteOrdering DecodeOrdering(BinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadInt32(),
            ReadEnumInt<BossHpRelief>(reader, "Boss HP 减免"),
            DecodePostCombatHeal(reader));

    private static void EncodeHpTargetStop(BinaryWriter writer, FlashHpTargetStop stop)
    {
        writer.Write(stop.AcceptableBattleHpLoss);
        writer.Write(stop.HpLostSoFar);
        writer.Write(stop.EarlyStopPotionUses);
        writer.Write(stop.RequiredPotionUses);
        writer.Write(stop.EnforceForcedPotionDirectives);
    }

    private static FlashHpTargetStop DecodeHpTargetStop(BinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
            FlashCodec.ReadBool(reader));

    private static void EncodeForcedDirectives(BinaryWriter writer, FlashForcedPotionDirectives directives)
    {
        FlashCodec.WriteIntArray(writer, directives.Slots);
        FlashCodec.WriteUShortArray(writer, directives.PotionIds);
        FlashCodec.WriteIntArray(writer, directives.StrategicHpCosts);
        writer.Write(directives.AlreadyUsedMask);
        writer.Write(directives.AlreadyUsedStrategicHpCost);
    }

    private static FlashForcedPotionDirectives DecodeForcedDirectives(BinaryReader reader)
        => new(FlashCodec.ReadIntArray(reader, FlashLimits.PotionSlots, "强制药水槽"),
            FlashCodec.ReadUShortArray(reader, FlashLimits.PotionSlots, "强制药水 id"),
            FlashCodec.ReadIntArray(reader, FlashLimits.PotionSlots, "强制药水代价"),
            reader.ReadInt32(), reader.ReadInt32());

    private static void EncodePotionFreeBaseline(BinaryWriter writer, PotionFreePolicyBaseline baseline)
    {
        writer.Write(baseline.Won);
        writer.Write(baseline.HpDeficit);
        writer.Write(baseline.PlayerHp);
        writer.Write(baseline.CombatEndedTurn != null);
        writer.Write(baseline.CombatEndedTurn ?? 0);
        writer.Write(baseline.DeathSaveUseCount);
    }

    private static PotionFreePolicyBaseline DecodePotionFreeBaseline(BinaryReader reader)
    {
        bool won = FlashCodec.ReadBool(reader);
        int deficit = reader.ReadInt32();
        int hp = reader.ReadInt32();
        bool hasTurn = FlashCodec.ReadBool(reader);
        int turnValue = reader.ReadInt32();
        int? turn = hasTurn ? turnValue : null;
        int deathSaves = reader.ReadInt32();
        return new PotionFreePolicyBaseline(won, deficit, hp, turn) { DeathSaveUseCount = deathSaves };
    }

    private static void EncodePlanCommitment(BinaryWriter writer, FlashPlanCommitment plan)
    {
        writer.Write(plan.CopyPowerSeats);
        writer.Write(plan.OpenedTurn);
        writer.Write(plan.EarliestTurn);
        writer.Write((byte)plan.PayoffKind);
        writer.Write(plan.PayoffCardId);
        writer.Write(plan.PayoffPotionId);
        writer.Write(plan.PayoffCardBit);
    }

    private static FlashPlanCommitment DecodePlanCommitment(BinaryReader reader)
        => new(FlashCodec.ReadBool(reader), reader.ReadInt32(), reader.ReadInt32(),
            (FlashPlanPayoffKind)ReadByteEnum(reader, "计划收益类型"),
            reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadInt32());

    private static void EncodePowerCommitments(BinaryWriter writer, FlashPowerCommitmentCatalog catalog)
    {
        writer.Write(catalog.Rules.Length);
        foreach (FlashPowerCommitmentRule rule in catalog.Rules)
        {
            writer.Write((int)rule.Family);
            writer.Write((int)rule.Priority);
            EncodeAdmission(writer, rule.Admission);
            writer.Write(rule.Index);
        }
        writer.Write(catalog.TotalFloor);
    }

    private static FlashPowerCommitmentCatalog DecodePowerCommitments(BinaryReader reader)
    {
        int count = FlashCodec.ReadCount(reader, 1 << 16, "能力承诺规则");
        FlashPowerCommitmentRule[] rules = new FlashPowerCommitmentRule[count];
        for (int index = 0; index < count; index++)
        {
            PowerCommitmentFamily family = ReadFlagsInt<PowerCommitmentFamily>(reader, "能力承诺族");
            PowerRoutePriority priority = ReadEnumInt<PowerRoutePriority>(reader, "能力路线优先级");
            PowerRouteAdmissionPolicy admission = DecodeAdmission(reader);
            rules[index] = new FlashPowerCommitmentRule(family, priority, admission, reader.ReadInt32());
        }
        return new FlashPowerCommitmentCatalog(rules, reader.ReadInt32());
    }

    private static void EncodeAdmission(BinaryWriter writer, PowerRouteAdmissionPolicy policy)
    {
        writer.Write((int)policy.Priority);
        writer.Write(policy.MinimumProjection);
        writer.Write(policy.AllowTriggerBackedProjectionFloor);
        writer.Write(policy.PreferFreeActivation);
        writer.Write(policy.RequireFreeOrSpareActivation);
        writer.Write(policy.RequireImmediateDefenseGain);
        writer.Write(policy.PreferDedicatedSearch);
        writer.Write(policy.RequirePositiveProjection);
        writer.Write(policy.NoInCombatCommitment);
    }

    private static PowerRouteAdmissionPolicy DecodeAdmission(BinaryReader reader)
        => new(ReadEnumInt<PowerRoutePriority>(reader, "能力路线准入优先级"),
            reader.ReadInt32(), FlashCodec.ReadBool(reader), FlashCodec.ReadBool(reader),
            FlashCodec.ReadBool(reader), FlashCodec.ReadBool(reader), FlashCodec.ReadBool(reader),
            FlashCodec.ReadBool(reader), FlashCodec.ReadBool(reader));

    private static void EncodeScoreRules(BinaryWriter writer, FlashScoreRules rules)
    {
        writer.Write(rules.Cards.Length);
        for (int id = 0; id < rules.Cards.Length; id++)
        {
            FlashCardScore?[]? levels = rules.Cards[id];
            writer.Write(levels != null);
            if (levels == null)
                continue;
            writer.Write(levels.Length);
            for (int upgrade = 0; upgrade < levels.Length; upgrade++)
            {
                writer.Write(levels[upgrade] != null);
                if (levels[upgrade] is { } score)
                    EncodeCardScore(writer, score);
            }
        }
        writer.Write(rules.Powers.Length);
        for (int id = 0; id < rules.Powers.Length; id++)
        {
            writer.Write(rules.Powers[id] != null);
            if (rules.Powers[id] is { } score)
                EncodePowerScore(writer, score);
        }
        FlashCodec.WriteIntArray(writer, rules.PotionStrategicCost);
        EncodeGrowthValues(writer, rules.GrowthBudgets);
        EncodeGrowthValues(writer, rules.RequiredGrowthRewards);
        writer.Write(rules.RelicCounterRules.Length);
        foreach (FlashRelicCounterRule rule in rules.RelicCounterRules)
        {
            EncodeRelicCounterTarget(writer, rule.Target);
            writer.Write(rule.RelicId);
            writer.Write(rule.MeatOnTheBoneThreshold);
        }
        EncodePostCombatHeal(writer, rules.PostCombatRelicHeal);
        writer.Write((int)rules.BossHpRelief);
        writer.Write(rules.ContinuousThreatRanking);
        if (rules.ContextualRanking != null)
            throw new NotSupportedException(
                "ContextualRankingModel 只有私有文档，无法在不修改 src/Search 的前提下导出；"
                + "本批协调器请求均为 null。");
        writer.Write(false);
        writer.Write(rules.InitialPlayerMaxHp);
        writer.Write(rules.IsActEndingBoss);
        writer.Write(rules.Act3BossStrategy);
        writer.Write(rules.BaseScoreOnly);
        writer.Write(rules.BeamWeightPerturbation != null);
        if (rules.BeamWeightPerturbation is { } perturbation)
        {
            writer.Write((int)perturbation.Term);
            writer.Write(BitConverter.DoubleToInt64Bits(perturbation.Scale));
        }
        writer.Write(rules.IgnoreLongTermRewards);
        writer.Write(rules.TheftPolicy != null);
        writer.Write((int)(rules.TheftPolicy ?? SolverTheftPolicy.LetEscape));
        writer.Write(rules.InitialEnemyCount);
        writer.Write(rules.InitialPersistentBuffValue);
        writer.Write(rules.InitialEnemyStrengthSuppression);
        writer.Write(rules.InitialEnemyWeakTurns);
        writer.Write(rules.InitialRetainedAttackValue);
        FlashCodec.WriteUShortArray(writer, rules.RootPotions);
        writer.Write(rules.InitialPotionUseCount);
        writer.Write(rules.InitialPotionStrategicCost);
        writer.Write(rules.InitialExplicitPotionStrategicCost);
        WriteId(writer, rules.StrengthPowerId);
        WriteId(writer, rules.WeakPowerId);
        WriteId(writer, rules.VulnerablePowerId);
        WriteId(writer, rules.PoisonPowerId);
        WriteId(writer, rules.DemisePowerId);
        WriteId(writer, rules.DoomPowerId);
        WriteId(writer, rules.SandpitPowerId);
        WriteId(writer, rules.SummonNextTurnPowerId);
        WriteId(writer, rules.DieForYouPowerId);
        WriteId(writer, rules.SleightOfFleshPowerId);
        WriteId(writer, rules.RetainHandPowerId);
        WriteId(writer, rules.EnergyNextTurnPowerId);
        WriteId(writer, rules.DrawCardsNextTurnPowerId);
        WriteId(writer, rules.StarNextTurnPowerId);
        WriteId(writer, rules.FreeAttackPowerId);
        WriteId(writer, rules.VoidFormPowerId);
        WriteId(writer, rules.FreeSkillPowerId);
        WriteId(writer, rules.FreePowerPowerId);
        WriteId(writer, rules.IllusionPowerId);
        WriteId(writer, rules.ReattachPowerId);
        WriteId(writer, rules.AdaptablePowerId);
        WriteId(writer, rules.BodySlamCardId);
        WriteId(writer, rules.EradicateCardId);
        WriteId(writer, rules.FiddleRelicId);
        WriteId(writer, rules.DropletOfPrecognitionPotionId);
        WriteId(writer, rules.TheHuntCardId);
        WriteId(writer, rules.TheHuntPowerId);
        WriteId(writer, rules.BufferPowerId);
        WriteId(writer, rules.NoDrawPowerId);
        WriteId(writer, rules.TheGambitPowerId);
        WriteId(writer, rules.FairyInABottlePotionId);
        WriteId(writer, rules.AmbergrisPotionId);
        WriteId(writer, rules.LizardTailRelicId);
        writer.Write(rules.RootPotionSlotCount);
    }

    private static FlashScoreRules DecodeScoreRules(BinaryReader reader, FlashRules rules)
    {
        int cardCount = FlashCodec.ReadCount(reader, 1 << 16, "评分卡牌表");
        FlashCardScore?[][] cards = new FlashCardScore?[cardCount][];
        for (int id = 0; id < cardCount; id++)
        {
            if (!FlashCodec.ReadBool(reader))
                continue;
            int levels = FlashCodec.ReadCount(reader, 64, "评分卡牌升级表");
            cards[id] = new FlashCardScore?[levels];
            for (int upgrade = 0; upgrade < levels; upgrade++)
            {
                if (FlashCodec.ReadBool(reader))
                    cards[id][upgrade] = DecodeCardScore(reader);
            }
        }
        int powerCount = FlashCodec.ReadCount(reader, 1 << 16, "评分 Power 表");
        FlashPowerScore[] powers = new FlashPowerScore[powerCount];
        for (int id = 0; id < powerCount; id++)
        {
            if (FlashCodec.ReadBool(reader))
                powers[id] = DecodePowerScore(reader);
        }
        int[] potionCosts = FlashCodec.ReadIntArray(reader, 1 << 16, "药水战略代价");
        GrowthValues growth = DecodeGrowthValues(reader);
        GrowthValues required = DecodeGrowthValues(reader);
        int relicRuleCount = FlashCodec.ReadCount(reader, 1 << 16, "遗物计数器规则");
        FlashRelicCounterRule[] relicRules = new FlashRelicCounterRule[relicRuleCount];
        for (int index = 0; index < relicRuleCount; index++)
        {
            RelicCounterTarget target = DecodeRelicCounterTarget(reader);
            relicRules[index] = new FlashRelicCounterRule(target, reader.ReadUInt16(), reader.ReadInt32());
        }
        PostCombatRelicHealProfile heal = DecodePostCombatHeal(reader);
        BossHpRelief relief = ReadEnumInt<BossHpRelief>(reader, "Boss HP 减免");
        bool continuous = FlashCodec.ReadBool(reader);
        if (FlashCodec.ReadBool(reader))
            throw new InvalidDataException("本格式不解码 ContextualRankingModel。");
        int initialMaxHp = reader.ReadInt32();
        bool actEnding = FlashCodec.ReadBool(reader);
        bool act3 = FlashCodec.ReadBool(reader);
        bool baseOnly = FlashCodec.ReadBool(reader);
        BeamWeightPerturbation? perturbation = null;
        bool hasBeam = FlashCodec.ReadBool(reader);
        if (hasBeam)
        {
            BeamWeightTerm term = ReadEnumInt<BeamWeightTerm>(reader, "Beam 权重项");
            double scale = BitConverter.Int64BitsToDouble(reader.ReadInt64());
            perturbation = new BeamWeightPerturbation(term, scale);
        }
        bool ignoreLongTerm = FlashCodec.ReadBool(reader);
        bool hasTheft = FlashCodec.ReadBool(reader);
        int theftValue = reader.ReadInt32();
        SolverTheftPolicy? theft = hasTheft
            ? ReadEnumInt<SolverTheftPolicy>(theftValue, "窃取政策") : null;
        int enemyCount = reader.ReadInt32();
        int persistent = reader.ReadInt32();
        int suppression = reader.ReadInt32();
        int weak = reader.ReadInt32();
        int retained = reader.ReadInt32();
        ushort[] rootPotions = FlashCodec.ReadUShortArray(reader, FlashLimits.PotionSlots, "评分根药水");
        int potionUses = reader.ReadInt32();
        int potionCost = reader.ReadInt32();
        int explicitCost = reader.ReadInt32();
        return new FlashScoreRules
        {
            Rules = rules,
            Cards = cards,
            Powers = powers,
            PotionStrategicCost = potionCosts,
            GrowthBudgets = growth,
            RequiredGrowthRewards = required,
            RelicCounterRules = relicRules,
            PostCombatRelicHeal = heal,
            BossHpRelief = relief,
            ContinuousThreatRanking = continuous,
            ContextualRanking = null,
            InitialPlayerMaxHp = initialMaxHp,
            IsActEndingBoss = actEnding,
            Act3BossStrategy = act3,
            BaseScoreOnly = baseOnly,
            BeamWeightPerturbation = perturbation,
            IgnoreLongTermRewards = ignoreLongTerm,
            TheftPolicy = theft,
            InitialEnemyCount = enemyCount,
            InitialPersistentBuffValue = persistent,
            InitialEnemyStrengthSuppression = suppression,
            InitialEnemyWeakTurns = weak,
            InitialRetainedAttackValue = retained,
            RootPotions = rootPotions,
            InitialPotionUseCount = potionUses,
            InitialPotionStrategicCost = potionCost,
            InitialExplicitPotionStrategicCost = explicitCost,
            StrengthPowerId = ReadId(reader),
            WeakPowerId = ReadId(reader),
            VulnerablePowerId = ReadId(reader),
            PoisonPowerId = ReadId(reader),
            DemisePowerId = ReadId(reader),
            DoomPowerId = ReadId(reader),
            SandpitPowerId = ReadId(reader),
            SummonNextTurnPowerId = ReadId(reader),
            DieForYouPowerId = ReadId(reader),
            SleightOfFleshPowerId = ReadId(reader),
            RetainHandPowerId = ReadId(reader),
            EnergyNextTurnPowerId = ReadId(reader),
            DrawCardsNextTurnPowerId = ReadId(reader),
            StarNextTurnPowerId = ReadId(reader),
            FreeAttackPowerId = ReadId(reader),
            VoidFormPowerId = ReadId(reader),
            FreeSkillPowerId = ReadId(reader),
            FreePowerPowerId = ReadId(reader),
            IllusionPowerId = ReadId(reader),
            ReattachPowerId = ReadId(reader),
            AdaptablePowerId = ReadId(reader),
            BodySlamCardId = ReadId(reader),
            EradicateCardId = ReadId(reader),
            FiddleRelicId = ReadId(reader),
            DropletOfPrecognitionPotionId = ReadId(reader),
            TheHuntCardId = ReadId(reader),
            TheHuntPowerId = ReadId(reader),
            BufferPowerId = ReadId(reader),
            NoDrawPowerId = ReadId(reader),
            TheGambitPowerId = ReadId(reader),
            FairyInABottlePotionId = ReadId(reader),
            AmbergrisPotionId = ReadId(reader),
            LizardTailRelicId = ReadId(reader),
            RootPotionSlotCount = reader.ReadInt32(),
        };
    }

    private static void EncodeCardScore(BinaryWriter writer, FlashCardScore score)
    {
        writer.Write(score.BaseDamage);
        writer.Write(score.BaseBlock);
        writer.Write(score.BaseCards);
        writer.Write(score.IsPower);
        writer.Write(score.PrimaryMoveToHandChoice);
        writer.Write(score.DamageVar);
        writer.Write(score.BlockVar);
        writer.Write(score.CardsVar);
        writer.Write(score.RepeatVar);
        writer.Write(score.ShivCardsVar);
        writer.Write(score.ShivShivsVar);
        writer.Write(score.CalculationBaseVar);
        writer.Write(score.CalculationExtraVar);
        writer.Write(score.DoomThresholdVar);
        writer.Write((byte)score.AttackHits);
        writer.Write((byte)score.ShivSupply);
        writer.Write(score.HasBlockDynamicVar);
        writer.Write(score.HasDebuffDynamicVar);
        writer.Write(score.HasTurnEndInHandEffect);
        writer.Write(score.HasOstyAttackTag);
        writer.Write(score.HasShivTag);
        writer.Write(score.LatentValue);
        writer.Write((int)score.LatentTrait);
        writer.Write(score.DynamicCardType);
        writer.Write((byte)score.Calculated);
    }

    private static FlashCardScore DecodeCardScore(BinaryReader reader)
        => new()
        {
            BaseDamage = reader.ReadDouble(),
            BaseBlock = reader.ReadDouble(),
            BaseCards = reader.ReadDouble(),
            IsPower = FlashCodec.ReadBool(reader),
            PrimaryMoveToHandChoice = FlashCodec.ReadBool(reader),
            DamageVar = reader.ReadSByte(),
            BlockVar = reader.ReadSByte(),
            CardsVar = reader.ReadSByte(),
            RepeatVar = reader.ReadSByte(),
            ShivCardsVar = reader.ReadSByte(),
            ShivShivsVar = reader.ReadSByte(),
            CalculationBaseVar = reader.ReadSByte(),
            CalculationExtraVar = reader.ReadSByte(),
            DoomThresholdVar = reader.ReadSByte(),
            AttackHits = (FlashAttackHitKind)ReadByteEnum(reader, "攻击段数"),
            ShivSupply = (FlashShivSupplyKind)ReadByteEnum(reader, "飞刀来源"),
            HasBlockDynamicVar = FlashCodec.ReadBool(reader),
            HasDebuffDynamicVar = FlashCodec.ReadBool(reader),
            HasTurnEndInHandEffect = FlashCodec.ReadBool(reader),
            HasOstyAttackTag = FlashCodec.ReadBool(reader),
            HasShivTag = FlashCodec.ReadBool(reader),
            LatentValue = reader.ReadInt32(),
            LatentTrait = (FlashSetupTraits)reader.ReadInt32(),
            DynamicCardType = FlashCodec.ReadBool(reader),
            Calculated = (FlashCalculatedKind)ReadByteEnum(reader, "计算伤害类型"),
        };

    private static void EncodePowerScore(BinaryWriter writer, FlashPowerScore score)
    {
        writer.Write((int)score.Kind);
        writer.Write((int)score.Requirements);
        writer.Write((int)score.Trait);
        writer.Write(score.IsTemporary);
        writer.Write((byte)score.Type);
        writer.Write(score.DanseMacabreEnergyThreshold);
    }

    private static FlashPowerScore DecodePowerScore(BinaryReader reader)
        => new()
        {
            Kind = (FlashStrategicKind)ReadInt(reader),
            Requirements = (FlashStrategicRequirements)ReadInt(reader),
            Trait = (FlashSetupTraits)ReadInt(reader),
            IsTemporary = FlashCodec.ReadBool(reader),
            Type = (FlashPowerType)ReadByteEnum(reader, "Power 类型"),
            DanseMacabreEnergyThreshold = reader.ReadInt32(),
        };

    private static void EncodeBoundConfig(BinaryWriter writer, FlashBoundFeatures.Config config)
    {
        writer.Write(config.Enabled);
        FlashCodec.WriteString(writer, config.DisabledReason);
        writer.Write(config.FutureHealPotential);
        writer.Write((byte)config.Certificate);
        writer.Write(config.PostCombatHeal);
        writer.Write((int)config.BossHpRelief);
        writer.Write(config.RequireCertifiedHealingProof);
        writer.Write(config.PruneEqualHp);
        writer.Write(config.ForceAllPotionsDisabled);
        writer.Write(config.MaximumPotionUses != null);
        writer.Write(config.MaximumPotionUses ?? 0);
        writer.Write(config.InitialExplicitPotionUses);
        writer.Write(config.InitialAutomaticPotionUses);
        WriteBools(writer, config.ComponentUnsafeCard);
        WriteBools(writer, config.ComponentUnsafePower);
        WriteBools(writer, config.ComponentUnsafeRelic);
        WriteBools(writer, config.ComponentUnsafeMonster);
        WriteBools(writer, config.ComponentUnsafePotion);
        WriteBools(writer, config.SafeEnchantment);
        WriteBools(writer, config.SafeAffliction);
        WriteBools(writer, config.KnownBlockingCard);
        WriteBools(writer, config.KnownBlockingRelic);
        WriteBools(writer, config.RegenPower);
        WriteBools(writer, config.ReattachPower);
        WriteBools(writer, config.SwipePower);
        WriteBools(writer, config.RegenPotion);
        WriteBools(writer, config.DirectHealPotion);
        WriteBools(writer, config.FruitJuicePotion);
        WriteBools(writer, config.FairyPotion);
        WriteBools(writer, config.NativePotion);
        FlashCodec.WriteIntArray(writer, config.RegenPotionAmount);
        WriteDecimals(writer, config.DirectHealPotionPercent);
        WriteBools(writer, config.ReattachSafeMonster);
        writer.Write(config.RegenPowerId);
        writer.Write(config.ReattachPowerId);
        FlashCodec.WriteUShortArray(writer, config.RootPotions);
        writer.Write(config.RootPotionSlotCount);
    }

    private static FlashBoundFeatures.Config DecodeBoundConfig(BinaryReader reader)
    {
        bool enabled = FlashCodec.ReadBool(reader);
        string reason = FlashCodec.ReadString(reader)
            ?? throw new InvalidDataException("下界失效原因缺失。");
        int future = reader.ReadInt32();
        FlashHealingCertificate certificate = (FlashHealingCertificate)
            ReadByteEnum(reader, "治疗证书");
        int postCombat = reader.ReadInt32();
        BossHpRelief relief = ReadEnumInt<BossHpRelief>(reader, "Boss HP 减免");
        bool certified = FlashCodec.ReadBool(reader);
        bool pruneEqual = FlashCodec.ReadBool(reader);
        bool forceDisabled = FlashCodec.ReadBool(reader);
        bool hasMaximum = FlashCodec.ReadBool(reader);
        int maximumValue = reader.ReadInt32();
        int? maximum = hasMaximum ? maximumValue : null;
        int explicitUses = reader.ReadInt32();
        int automaticUses = reader.ReadInt32();
        return new FlashBoundFeatures.Config
        {
            Enabled = enabled,
            DisabledReason = reason,
            FutureHealPotential = future,
            Certificate = certificate,
            PostCombatHeal = postCombat,
            BossHpRelief = relief,
            RequireCertifiedHealingProof = certified,
            PruneEqualHp = pruneEqual,
            ForceAllPotionsDisabled = forceDisabled,
            MaximumPotionUses = maximum,
            InitialExplicitPotionUses = explicitUses,
            InitialAutomaticPotionUses = automaticUses,
            ComponentUnsafeCard = ReadBools(reader, 1 << 16, "下界卡牌安全表"),
            ComponentUnsafePower = ReadBools(reader, 1 << 16, "下界 Power 安全表"),
            ComponentUnsafeRelic = ReadBools(reader, 1 << 16, "下界遗物安全表"),
            ComponentUnsafeMonster = ReadBools(reader, 1 << 16, "下界怪物安全表"),
            ComponentUnsafePotion = ReadBools(reader, 1 << 16, "下界药水安全表"),
            SafeEnchantment = ReadBools(reader, 1 << 10, "下界附魔安全表"),
            SafeAffliction = ReadBools(reader, 1 << 10, "下界苦难安全表"),
            KnownBlockingCard = ReadBools(reader, 1 << 16, "已知阻断卡牌表"),
            KnownBlockingRelic = ReadBools(reader, 1 << 16, "已知阻断遗物表"),
            RegenPower = ReadBools(reader, 1 << 16, "再生 Power 表"),
            ReattachPower = ReadBools(reader, 1 << 16, "重挂 Power 表"),
            SwipePower = ReadBools(reader, 1 << 16, "窃取 Power 表"),
            RegenPotion = ReadBools(reader, 1 << 16, "再生药水表"),
            DirectHealPotion = ReadBools(reader, 1 << 16, "直接治疗药水表"),
            FruitJuicePotion = ReadBools(reader, 1 << 16, "果汁药水表"),
            FairyPotion = ReadBools(reader, 1 << 16, "精灵药水表"),
            NativePotion = ReadBools(reader, 1 << 16, "原版药水表"),
            RegenPotionAmount = FlashCodec.ReadIntArray(reader, 1 << 16, "再生药水数值"),
            DirectHealPotionPercent = ReadDecimals(reader),
            ReattachSafeMonster = ReadBools(reader, 1 << 16, "重挂安全怪物表"),
            RegenPowerId = reader.ReadInt32(),
            ReattachPowerId = reader.ReadInt32(),
            RootPotions = FlashCodec.ReadUShortArray(reader, FlashLimits.PotionSlots, "下界根药水"),
            RootPotionSlotCount = reader.ReadInt32(),
        };
    }

    private static void EncodeGrowthValues(BinaryWriter writer, GrowthValues values)
    {
        if (!values.Extras.IsEmpty)
            throw new NotSupportedException("第三方成长来源未移植，无法导出 GrowthValues。");
        writer.Write(values.HandOfGreed);
        writer.Write(values.TheHunt);
        writer.Write(values.Feed);
        writer.Write(values.Royalties);
        writer.Write(values.Alchemize);
        writer.Write(values.GeneticAlgorithm);
        writer.Write(values.TheScythe);
        writer.Write(values.Goopy);
        writer.Write(values.ForbiddenGrimoire);
        writer.Write(values.MadScience);
    }

    private static GrowthValues DecodeGrowthValues(BinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadInt32(), reader.ReadInt32());

    private static void EncodeRelicCounterTarget(BinaryWriter writer, RelicCounterTarget target)
    {
        writer.Write((int)target.Id);
        writer.Write(target.Minimum);
        writer.Write(target.Maximum);
        writer.Write(target.HpAllowance);
        writer.Write(target.Period);
        writer.Write(target.Priority);
    }

    private static RelicCounterTarget DecodeRelicCounterTarget(BinaryReader reader)
        => new(ReadEnumInt<RelicCounterId>(reader, "遗物计数器"),
            reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadInt32());

    private static void EncodePostCombatHeal(BinaryWriter writer, PostCombatRelicHealProfile heal)
    {
        writer.Write(heal.UnconditionalHeal);
        writer.Write(heal.WoundedHeal);
        writer.Write(heal.WoundedHpPercent);
    }

    private static PostCombatRelicHealProfile DecodePostCombatHeal(BinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

    private static void WriteId(BinaryWriter writer, ushort id) => writer.Write(id);

    private static ushort ReadId(BinaryReader reader) => reader.ReadUInt16();

    private static void WriteBools(BinaryWriter writer, bool[] values)
    {
        writer.Write(values.Length);
        for (int index = 0; index < values.Length; index++)
            writer.Write(values[index]);
    }

    private static bool[] ReadBools(BinaryReader reader, int max, string what)
    {
        int count = FlashCodec.ReadCount(reader, max, what);
        bool[] values = new bool[count];
        for (int index = 0; index < count; index++)
            values[index] = FlashCodec.ReadBool(reader);
        return values;
    }

    private static void WriteDecimals(BinaryWriter writer, decimal[] values)
    {
        writer.Write(values.Length);
        for (int index = 0; index < values.Length; index++)
            FlashCodec.WriteDecimal(writer, values[index]);
    }

    private static decimal[] ReadDecimals(BinaryReader reader)
    {
        int count = FlashCodec.ReadCount(reader, 1 << 16, "decimal 数组");
        decimal[] values = new decimal[count];
        for (int index = 0; index < count; index++)
            values[index] = FlashCodec.ReadDecimal(reader);
        return values;
    }

    private static int ReadInt(BinaryReader reader) => reader.ReadInt32();

    private static T ReadEnumInt<T>(BinaryReader reader, string what) where T : struct, Enum
        => ReadEnumInt<T>(reader.ReadInt32(), what);

    private static T ReadFlagsInt<T>(BinaryReader reader, string what) where T : struct, Enum
    {
        int value = reader.ReadInt32();
        int all = 0;
        foreach (T item in Enum.GetValues<T>())
            all |= Convert.ToInt32(item);
        if ((value & ~all) != 0)
            throw new InvalidDataException($"{what}位 {value:X} 含未定义标志。");
        return (T)Enum.ToObject(typeof(T), value);
    }

    private static T ReadEnumInt<T>(int value, string what) where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value))
            throw new InvalidDataException($"{what}值 {value} 未定义。");
        return (T)Enum.ToObject(typeof(T), value);
    }

    private static byte ReadByteEnum(BinaryReader reader, string what) => reader.ReadByte();

    // ---------------------------------------------------------------------------------------------
    // Result codec: every deterministic field, no wall-clock, GC or frame-pressure measurements.

    internal static byte[] EncodeResult(FlashBeamResult result)
        => FlashCodec.ToBytes(writer =>
        {
            writer.Write(ResultMagic);
            writer.Write(FlashCodec.FormatVersion);
            WriteRoute(writer, result.BestRoute);
            writer.Write(result.TopRoutes.Count);
            foreach (FlashAction[] route in result.TopRoutes)
                WriteRoute(writer, route);
            writer.Write(result.BestIsVictory);
            writer.Write(BitConverter.DoubleToInt64Bits(result.BestScore));
            writer.Write(result.ExpandedNodes);
            writer.Write(result.Transitions);
            writer.Write(result.Duplicates);
            writer.Write(result.TurnsSearched);
            writer.Write(result.PrunedIncumbents);
            writer.Write(result.CapacityPruned);
            writer.Write(result.CapacityDroppedChildren.Count);
            foreach (KeyValuePair<string, int> entry in result.CapacityDroppedChildren
                         .OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                FlashCodec.WriteString(writer, entry.Key);
                writer.Write(entry.Value);
            }
            writer.Write(result.EndedCompactions);
            writer.Write(result.EndedCompressedStates);
            writer.Write(result.SmartPotionPruned);
            writer.Write(result.BestPayoffCount);
            writer.Write(result.ScoutLayers.Count);
            foreach (FlashScoutLayer layer in result.ScoutLayers)
            {
                writer.Write(layer.Turns);
                writer.Write(layer.Candidates.Count);
                foreach (FlashScoutCandidate candidate in layer.Candidates)
                {
                    WriteRoute(writer, candidate.Actions);
                    writer.Write(BitConverter.DoubleToInt64Bits(candidate.Score));
                    writer.Write(candidate.RootIndex);
                }
            }
            if (result.NoveltyTelemetry != null)
                throw new NotSupportedException(
                    "NoveltySearchTelemetry 含有时间字段且不在本批协调器请求中，未导出。");
            writer.Write(false);
            writer.Write(result.BestRootIndex);
            writer.Write(result.PotionPolicySatisfied);
            writer.Write((byte)result.Takeover);
        });

    internal static FlashBeamResult DecodeResult(byte[] bytes, int expectedBestRootIndex = -1)
        => FlashCodec.Read(bytes, "Flash 搜索结果", reader =>
        {
            if (reader.ReadUInt32() != ResultMagic)
                throw new InvalidDataException("Flash 搜索结果魔数不符。");
            ushort version = reader.ReadUInt16();
            if (version != FlashCodec.FormatVersion)
                throw new InvalidDataException($"Flash 搜索结果版本 {version} 不受支持。");
            FlashAction[] best = ReadRoute(reader);
            int topCount = FlashCodec.ReadCount(reader, 1 << 16, "最终候选路线");
            FlashAction[][] top = new FlashAction[topCount][];
            for (int index = 0; index < topCount; index++)
                top[index] = ReadRoute(reader);
            bool victory = FlashCodec.ReadBool(reader);
            double score = BitConverter.Int64BitsToDouble(reader.ReadInt64());
            int expanded = reader.ReadInt32();
            int transitions = reader.ReadInt32();
            int duplicates = reader.ReadInt32();
            int turns = reader.ReadInt32();
            int pruned = reader.ReadInt32();
            int capacityPruned = reader.ReadInt32();
            int capacityCount = FlashCodec.ReadCount(reader, 1 << 10, "容量丢弃表");
            Dictionary<string, int> capacity = new(StringComparer.Ordinal);
            for (int index = 0; index < capacityCount; index++)
            {
                string key = FlashCodec.ReadString(reader)
                    ?? throw new InvalidDataException("容量丢弃键缺失。");
                if (!capacity.TryAdd(key, reader.ReadInt32()))
                    throw new InvalidDataException($"容量丢弃键 {key} 重复。");
            }
            int endedCompactions = reader.ReadInt32();
            int endedCompressed = reader.ReadInt32();
            int smartPruned = reader.ReadInt32();
            int payoff = reader.ReadInt32();
            int layerCount = FlashCodec.ReadCount(reader, 1 << 10, "侦察层");
            FlashScoutLayer[] layers = new FlashScoutLayer[layerCount];
            for (int index = 0; index < layerCount; index++)
            {
                int layerTurns = reader.ReadInt32();
                int candidateCount = FlashCodec.ReadCount(reader, 1 << 16, "侦察候选");
                FlashScoutCandidate[] candidates = new FlashScoutCandidate[candidateCount];
                for (int candidate = 0; candidate < candidateCount; candidate++)
                {
                    FlashAction[] route = ReadRoute(reader);
                    double candidateScore = BitConverter.Int64BitsToDouble(reader.ReadInt64());
                    candidates[candidate] = new FlashScoutCandidate(route, candidateScore)
                    {
                        RootIndex = reader.ReadInt32(),
                    };
                }
                layers[index] = new FlashScoutLayer(layerTurns, candidates);
            }
            if (FlashCodec.ReadBool(reader))
                throw new InvalidDataException("本格式不解码 NoveltySearchTelemetry。");
            int bestRoot = reader.ReadInt32();
            bool potionSatisfied = FlashCodec.ReadBool(reader);
            FlashTakeoverAction takeover = (FlashTakeoverAction)ReadByteEnum(reader, "接管动作");
            if (expectedBestRootIndex >= 0 && bestRoot != expectedBestRootIndex)
                throw new InvalidDataException(
                    $"Flash 搜索结果根下标 {bestRoot} 与请求的 {expectedBestRootIndex} 不同。");
            return new FlashBeamResult(best, top, victory, score, expanded, transitions,
                duplicates, turns, pruned, TimeSpan.Zero)
            {
                CapacityPruned = capacityPruned,
                CapacityDroppedChildren = capacity,
                EndedCompactions = endedCompactions,
                EndedCompressedStates = endedCompressed,
                SmartPotionPruned = smartPruned,
                BestPayoffCount = payoff,
                ScoutLayers = layers,
                BestRootIndex = bestRoot,
                PotionPolicySatisfied = potionSatisfied,
                Takeover = takeover,
            };
        });

    private static void WriteRoute(BinaryWriter writer, FlashAction[] route)
    {
        writer.Write(route.Length);
        foreach (FlashAction action in route)
            FlashGoldenData.WriteAction(writer, action);
    }

    private static FlashAction[] ReadRoute(BinaryReader reader)
    {
        int count = FlashCodec.ReadCount(reader, 1 << 20, "路线动作");
        FlashAction[] route = new FlashAction[count];
        for (int index = 0; index < count; index++)
            route[index] = FlashGoldenData.ReadAction(reader);
        return route;
    }
}
