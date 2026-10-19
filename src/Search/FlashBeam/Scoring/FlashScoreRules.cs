using CombatSolver.Engine.InCombat.Flash;

namespace CombatSolver.FlashBeam;

/// <summary>Strategic-effect classification of one admitted power (port of <c>StrategicEffectModel.Evaluate</c>).</summary>
internal enum FlashStrategicKind : byte
{
    Scaling = 0,
    Afterimage,
    Buffer,
    FeelNoPain,
    DarkEmbrace,
    Thorns,
    EchoForm,
    Envenom,
    Accuracy,
    SleightOfFlesh,
    Strength,
    Lethality,
    ReaperForm,
    Dexterity,
    DemonForm,
    Curious,
    Corruption,
    CreativeAi,
    Iteration,
    MasterPlanner,
    Focus,
    Furnace,
    Thunder,
    LightningRod,

    /// <summary>Automation: recurring energy from drawn cards in the refund window.</summary>
    RecurringEnergy,

    /// <summary>Orbit: recurring energy from the refund window's energy spend plus the remainder.</summary>
    OrbitEnergy,
}

/// <summary>Port of <c>StrategicEffectRequirements</c>; bit values must match the existing enum.</summary>
[Flags]
internal enum FlashStrategicRequirements
{
    None = 0,
    RemainingTurns = 1 << 0,
    UsefulCardPlays = 1 << 1,
    AttackPlays = 1 << 2,
    SkillPlays = 1 << 3,
    BlockSkillPlays = 1 << 4,
    PowerPlays = 1 << 5,
    ExhaustPlays = 1 << 6,
    ShivPlays = 1 << 7,
    DebuffApplications = 1 << 8,
    SkillEnergySpend = 1 << 9,
    PowerEnergySpend = 1 << 10,
    AverageCardValue = 1 << 11,
    BestCardValue = 1 << 12,
    AverageAttackValue = 1 << 13,
    StatusDrawTriggers = 1 << 14,
    AttackHits = 1 << 15,
}

/// <summary>Port of <c>PersistentSetupTraits</c>; bit values must match the existing enum.</summary>
[Flags]
internal enum FlashSetupTraits
{
    None = 0,
    Curious = 1 << 0,
    EchoForm = 1 << 1,
    Buffer = 1 << 2,
    Focus = 1 << 3,
    Thunder = 1 << 4,
    RecurringScaling = 1 << 5,
    OrbEngine = 1 << 6,
}

/// <summary>Which calculated-damage multiplier the card's native spec uses (port of <c>CalculatedVarSpecRegistry</c> for the admitted cards).</summary>
internal enum FlashCalculatedKind : byte
{
    None = 0,
    PlayerBlock,
    StrikeCardCount,
    ExhaustPileCount,
    PlayerStrength,
    TargetVulnerable,
    DrawPileCount,
    TargetDebuffPowerCount,
    FinishedCardPlays,
    CardsDiscardedThisTurn,
    NegativeHandCount,
    CardsDrawn,

    /// <summary>CrescentSpear: cards in the combat piles whose canonical star cost is ≥ 0 or X.</summary>
    StarCardCount,

    /// <summary>Unleash: Osty's current HP.</summary>
    OstyCurrentHp,

    /// <summary>Protector: Osty's max HP.</summary>
    OstyMaxHp,

    /// <summary>Rattle: 1 + Osty's attacks this turn.</summary>
    OstyAttacksPlusOne,

    /// <summary>Squeeze: every other OstyAttack card in the combat piles.</summary>
    OstyAttackCardsMinusSelf,

    /// <summary>SoulStorm: Soul cards in the exhaust pile.</summary>
    ExhaustSoulCount,

    /// <summary>PullFromBelow: Ethereal card plays this combat.</summary>
    EtherealPlays,

    /// <summary>NoEscape: floor(target Doom / DoomThreshold).</summary>
    DoomOverThreshold,

    /// <summary>TimesUp: the target's Doom amount.</summary>
    TargetDoom,

    /// <summary>Supermassive: cards the player generated this combat.</summary>
    GeneratedCardCount,

    /// <summary>DeathMarch: non-hand draws this turn.</summary>
    NonHandDraws,

    /// <summary>A registered calculated-damage card the port does not model; the root is rejected.</summary>
    Unsupported,
}

/// <summary>Port of the per-card attack-hit fact (<c>CardMechanismFacts.AttackHits</c>).</summary>
internal enum FlashAttackHitKind : byte
{
    One = 0,
    Two,
    RepeatVar,
}

/// <summary>Port of the per-card immediate shiv supply fact (<c>CardMechanismFacts.ImmediateShivSupply</c>).</summary>
internal enum FlashShivSupplyKind : byte
{
    None = 0,
    CardsVar,
    ShivsVar,
}

/// <summary>
/// Root-frozen per-content evaluation values for one card id at one upgrade level. Every admitted card
/// has an entry; the values themselves are read from the existing engine's models when
/// <see cref="FlashScoreBridge"/> builds the table.
/// </summary>
internal sealed class FlashCardScore
{
    /// <summary><c>CardChoiceSupport.DynamicVarBaseValue</c> of the canonical upgraded card's named variables.</summary>
    public double BaseDamage { get; init; }

    public double BaseBlock { get; init; }

    public double BaseCards { get; init; }

    /// <summary>The card is a power; <c>CardChoiceSupport.CardValue</c> adds the flat 8.</summary>
    public bool IsPower { get; init; }

    /// <summary>Index of the named variables inside <see cref="FlashCardStats.Vars"/>, or -1.</summary>
    public sbyte DamageVar { get; init; } = -1;

    public sbyte BlockVar { get; init; } = -1;

    public sbyte CardsVar { get; init; } = -1;

    public sbyte RepeatVar { get; init; } = -1;

    public sbyte ShivCardsVar { get; init; } = -1;

    public sbyte ShivShivsVar { get; init; } = -1;

    public sbyte CalculationBaseVar { get; init; } = -1;

    public sbyte CalculationExtraVar { get; init; } = -1;

    /// <summary>NoEscape's <c>DoomThreshold</c> variable index, or -1.</summary>
    public sbyte DoomThresholdVar { get; init; } = -1;

    public FlashAttackHitKind AttackHits { get; init; }

    public FlashShivSupplyKind ShivSupply { get; init; }

    /// <summary>The card has any dynamic variable whose key names a block (port of <c>StrategicEffectModel.IsBlockDynamicVar</c>).</summary>
    public bool HasBlockDynamicVar { get; init; }

    /// <summary>The card has any dynamic variable whose key names a debuff (port of <c>StrategicEffectModel.IsDebuffDynamicVar</c>).</summary>
    public bool HasDebuffDynamicVar { get; init; }

    public bool HasTurnEndInHandEffect { get; init; }

    public bool HasOstyAttackTag { get; init; }

    public bool HasShivTag { get; init; }

    public int LatentValue { get; init; }

    public FlashSetupTraits LatentTrait { get; init; }

    public FlashCalculatedKind Calculated { get; init; }
}

/// <summary>Root-frozen strategic-effect values of one admitted power.</summary>
internal sealed class FlashPowerScore
{
    public FlashStrategicKind Kind { get; init; }

    public FlashStrategicRequirements Requirements { get; init; }

    public FlashSetupTraits Trait { get; init; }

    public bool IsTemporary { get; init; }

    public FlashPowerType Type { get; init; }
}

/// <summary>
/// Everything <see cref="FlashScorer"/> reads besides the <see cref="FlashBattle"/> and
/// <see cref="FlashRules"/>: per-card and per-power evaluation values compiled from the existing
/// engine at the root, plus the request settings the evaluation freezes.
/// </summary>
internal sealed class FlashScoreRules
{
    public required FlashRules Rules { get; init; }

    /// <summary>Indexed by card id, then upgrade level; null when the rule table has no such level.</summary>
    public required FlashCardScore?[][] Cards { get; init; }

    /// <summary>Indexed by power id.</summary>
    public required FlashPowerScore[] Powers { get; init; }

    /// <summary>Indexed by potion id; <c>PotionUsePolicy.StrategicHpCost</c> of the canonical potion.</summary>
    public required int[] PotionStrategicCost { get; init; }

    public required int InitialPlayerMaxHp { get; init; }

    public required bool IsActEndingBoss { get; init; }

    /// <summary><c>Profile.BaseScoreOnly</c>: the intermediate rank is the base score alone.</summary>
    public bool BaseScoreOnly { get; init; }

    /// <summary><c>Profile.BeamWeightPerturbation</c>: scales one intermediate ranking term.</summary>
    public BeamWeightPerturbation? BeamWeightPerturbation { get; init; }

    public required bool IgnoreLongTermRewards { get; init; }

    public required SolverTheftPolicy? TheftPolicy { get; init; }

    public required int InitialEnemyCount { get; init; }

    public required int InitialPersistentBuffValue { get; init; }

    public required int InitialEnemyStrengthSuppression { get; init; }

    public required int InitialEnemyWeakTurns { get; init; }

    public required int InitialRetainedAttackValue { get; init; }

    /// <summary>Root potion ids by slot (0 = empty); used to derive the potion-use counters.</summary>
    public required ushort[] RootPotions { get; init; }

    /// <summary>
    /// Cumulative potion counters at the Flash search root. A fixed prefix may already have spent
    /// potions; the scorer adds these frozen totals to the root-potion diff so the derived
    /// counters equal the existing snapshot's cumulative values.
    /// </summary>
    public int InitialPotionUseCount { get; init; }

    public int InitialPotionStrategicCost { get; init; }

    public int InitialExplicitPotionStrategicCost { get; init; }

    /// <summary>Power / card ids the port names directly, or <see cref="ushort.MaxValue"/> when unregistered.</summary>
    public ushort StrengthPowerId { get; init; } = ushort.MaxValue;

    public ushort WeakPowerId { get; init; } = ushort.MaxValue;

    public ushort VulnerablePowerId { get; init; } = ushort.MaxValue;

    public ushort PoisonPowerId { get; init; } = ushort.MaxValue;

    public ushort DemisePowerId { get; init; } = ushort.MaxValue;

    public ushort DoomPowerId { get; init; } = ushort.MaxValue;

    /// <summary><c>SandpitPower</c>: the enemy-side turn-start countdown read by the control term.</summary>
    public ushort SandpitPowerId { get; init; } = ushort.MaxValue;

    /// <summary><c>SummonNextTurnPower</c>: its amount is the summon-next-turn future-resource term.</summary>
    public ushort SummonNextTurnPowerId { get; init; } = ushort.MaxValue;

    /// <summary><c>DieForYouPower</c>: the Osty redirect read by the threat projection.</summary>
    public ushort DieForYouPowerId { get; init; } = ushort.MaxValue;

    public ushort SleightOfFleshPowerId { get; init; } = ushort.MaxValue;

    public ushort RetainHandPowerId { get; init; } = ushort.MaxValue;

    public ushort EnergyNextTurnPowerId { get; init; } = ushort.MaxValue;

    public ushort DrawCardsNextTurnPowerId { get; init; } = ushort.MaxValue;

    public ushort StarNextTurnPowerId { get; init; } = ushort.MaxValue;

    public ushort FreeAttackPowerId { get; init; } = ushort.MaxValue;

    /// <summary><c>VoidFormPower</c>: the free-play window and future-resource term.</summary>
    public ushort VoidFormPowerId { get; init; } = ushort.MaxValue;

    public ushort FreeSkillPowerId { get; init; } = ushort.MaxValue;

    public ushort FreePowerPowerId { get; init; } = ushort.MaxValue;

    public ushort IllusionPowerId { get; init; } = ushort.MaxValue;

    public ushort ReattachPowerId { get; init; } = ushort.MaxValue;

    public ushort AdaptablePowerId { get; init; } = ushort.MaxValue;

    public ushort BodySlamCardId { get; init; } = ushort.MaxValue;

    public ushort TheHuntCardId { get; init; } = ushort.MaxValue;

    public ushort TheHuntPowerId { get; init; } = ushort.MaxValue;

    public ushort BufferPowerId { get; init; } = ushort.MaxValue;

    public ushort NoDrawPowerId { get; init; } = ushort.MaxValue;

    public ushort TheGambitPowerId { get; init; } = ushort.MaxValue;

    public ushort FairyInABottlePotionId { get; init; } = ushort.MaxValue;

    public ushort AmbergrisPotionId { get; init; } = ushort.MaxValue;

    public ushort LizardTailRelicId { get; init; } = ushort.MaxValue;

    public required int RootPotionSlotCount { get; init; }

    public FlashCardScore Card(ushort id, int upgrade)
    {
        FlashCardScore?[]? levels = id < Cards.Length ? Cards[id] : null;
        return levels != null && (uint)upgrade < (uint)levels.Length && levels[upgrade] is { } score
            ? score
            : throw new FlashUnsupportedException($"评分表缺少卡牌 {FlashRegistry.Card(id).NativeId}+{upgrade}。");
    }

    public FlashPowerScore Power(ushort id)
        => id < Powers.Length && Powers[id] is { } score
            ? score
            : throw new FlashUnsupportedException($"评分表缺少 Power {FlashRegistry.Power(id).NativeId}。");
}
