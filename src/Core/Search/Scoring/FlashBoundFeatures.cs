using CombatSolver.Flash.Kernel;
using CombatSolver.Flash.Search;

namespace CombatSolver.Flash.Search.Scoring;

/// <summary>Which remaining-healing certificate the root uses; mirrors <c>RemainingHealingPotentialCore</c>.</summary>
internal enum FlashHealingCertificate : byte
{
    /// <summary>No certificate: the existing search passes no remaining-healing function.</summary>
    None = 0,
    Component = 1,
    KnownNative = 2,
}

/// <summary>
/// The frozen per-state bound inputs the incumbent pruning reads. One evaluation mirrors the existing
/// search's <c>SimulationSnapshot</c> members plus <c>RemainingHealingPotential</c> and
/// <c>StrategicHpLowerBound</c>; <see cref="FlashBoundFeatures.Diff"/> is the K5 comparison.
/// </summary>
internal readonly record struct FlashBoundValues(
    bool HasRisk,
    int FutureHealPotential,
    int RemainingHealingPotential,
    int FutureHealUsed,
    int CumulativePlayerHpLost,
    int RecoveredPlayerHp,
    int PlayerHp,
    int PlayerMaxHp,
    int DeathSaveHpRestored,
    int ExplicitPotionStrategicCost,
    int StrategicHpLowerBound,
    int SmartPotionRemainingHealingPotential,
    int SmartPotionHpLowerBound);

/// <summary>
/// Root-frozen healing-bound tables for FlashBeam and the K5 probe. The bridge classifies every admitted
/// content id with the existing engine's own predicates; evaluation reads only <see cref="FlashBattle"/>
/// and this table. When a bound input cannot be represented exactly the bridge disables the whole bound,
/// so the solver never prunes with an approximation.
/// </summary>
internal sealed class FlashBoundFeatures
{
    internal sealed class Config
    {
        public required bool Enabled { get; init; }
        public required string DisabledReason { get; init; }
        public required int FutureHealPotential { get; init; }
        public required FlashHealingCertificate Certificate { get; init; }
        public required int PostCombatHeal { get; init; }
        public required BossHpRelief BossHpRelief { get; init; }
        public required bool RequireCertifiedHealingProof { get; init; }
        public required bool PruneEqualHp { get; init; }
        public required bool ForceAllPotionsDisabled { get; init; }
        public required int? MaximumPotionUses { get; init; }
        public required int InitialExplicitPotionUses { get; init; }
        public required int InitialAutomaticPotionUses { get; init; }
        public required bool[] ComponentUnsafeCard { get; init; }
        public required bool[] ComponentUnsafePower { get; init; }
        public required bool[] ComponentUnsafeRelic { get; init; }
        public required bool[] ComponentUnsafeMonster { get; init; }
        public required bool[] ComponentUnsafePotion { get; init; }
        public required bool[] SafeEnchantment { get; init; }
        public required bool[] SafeAffliction { get; init; }
        public required bool[] KnownBlockingCard { get; init; }
        public required bool[] KnownBlockingRelic { get; init; }
        public required bool[] RegenPower { get; init; }
        public required bool[] ReattachPower { get; init; }
        public required bool[] SwipePower { get; init; }
        public required bool[] RegenPotion { get; init; }
        public required bool[] DirectHealPotion { get; init; }
        public required bool[] FruitJuicePotion { get; init; }
        public required bool[] FairyPotion { get; init; }
        public required bool[] NativePotion { get; init; }
        public required int[] RegenPotionAmount { get; init; }
        public required decimal[] DirectHealPotionPercent { get; init; }
        public required bool[] ReattachSafeMonster { get; init; }
        public required int RegenPowerId { get; init; }
        public required int ReattachPowerId { get; init; }
        public required ushort[] RootPotions { get; init; }
        public required int RootPotionSlotCount { get; init; }
    }

    private readonly Config _config;

    public FlashBoundFeatures(Config config) => _config = config;

    /// <summary>Complete frozen bound configuration, for the deterministic golden request codec.</summary>
    internal Config ConfigForCodec => _config;

    public bool Enabled => _config.Enabled;

    public string DisabledReason => _config.DisabledReason;

    public int FutureHealPotential => _config.FutureHealPotential;

    public BossHpRelief BossHpRelief => _config.BossHpRelief;

    public bool RequireCertifiedHealingProof => _config.RequireCertifiedHealingProof;

    public bool PruneEqualHp => _config.PruneEqualHp;

    /// <summary>
    /// The existing engine's <c>CombatPredictionSimulator.HasRisk</c>. Risk is recorded when a generation
    /// effect leaves the player's choice unresolved (Discovery/Splash) and stays true for the rest of the
    /// combat, so it maps to one monotonic battle flag.
    /// </summary>
    public bool HasRisk(in FlashBattle battle) => (battle.Flags & FlashBattleFlags.HasRisk) != 0;

    public FlashBoundValues Evaluate(in FlashBattle battle, int explicitPotionStrategicCost,
        bool includeSmartPotionLowerBound = false)
    {
        // the existing search's RemainingHealingPotentialCore: an unresolved risk invalidates the
        // certificate and returns int.MaxValue regardless of the configured component.
        int remaining = HasRisk(battle)
            ? int.MaxValue
            : _config.Certificate switch
            {
                FlashHealingCertificate.Component => ComponentRemaining(battle),
                FlashHealingCertificate.KnownNative => KnownNativeRemaining(battle),
                _ => int.MaxValue,
            };
        int futureUsed = _config.RequireCertifiedHealingProof && _config.PruneEqualHp
            ? remaining
            : Math.Min(remaining, _config.FutureHealPotential);
        int cumulativeHpLost = battle.Counter(FlashCounter.CumulativeHpLost);
        int recoveredHp = battle.Counter(FlashCounter.RecoveredHp);
        int deathSaveHpRestored = battle.Counter(FlashCounter.DeathSaveRelicHpRestored)
            + battle.Counter(FlashCounter.DeathSavePotionHpRestored);
        int lowerBound = ActEndingBossPolicy.StrategicHpDeficit(
            cumulativeHpLost,
            maxHpDeficit: 0,
            StrategicHpRecoveryBound.OptimisticRecoveredHp(
                recoveredHp, battle.Player.CurrentHp, battle.Player.MaxHp, futureUsed),
            _config.BossHpRelief,
            deathSaveHpRestored);
        // the existing search's ApplySmartPotionEligibilityBound passes RemainingHealingPotential
        // directly (not the incumbent's min with FutureHealPotential), so the ceiling keeps its
        // own lower-bound input. Non-audit searches never read it and skip the second
        // StrategicHpDeficit call on the hot retention path.
        int smartLowerBound = includeSmartPotionLowerBound
            ? ActEndingBossPolicy.StrategicHpDeficit(
                cumulativeHpLost,
                maxHpDeficit: 0,
                StrategicHpRecoveryBound.OptimisticRecoveredHp(
                    recoveredHp, battle.Player.CurrentHp, battle.Player.MaxHp, remaining),
                _config.BossHpRelief,
                deathSaveHpRestored)
            : lowerBound;
        return new FlashBoundValues(
            HasRisk: HasRisk(battle),
            FutureHealPotential: _config.FutureHealPotential,
            RemainingHealingPotential: remaining,
            FutureHealUsed: futureUsed,
            CumulativePlayerHpLost: cumulativeHpLost,
            RecoveredPlayerHp: recoveredHp,
            PlayerHp: battle.Player.CurrentHp,
            PlayerMaxHp: battle.Player.MaxHp,
            DeathSaveHpRestored: deathSaveHpRestored,
            ExplicitPotionStrategicCost: explicitPotionStrategicCost,
            StrategicHpLowerBound: lowerBound,
            SmartPotionRemainingHealingPotential: remaining,
            SmartPotionHpLowerBound: smartLowerBound);
    }

    private int ComponentRemaining(in FlashBattle battle)
    {
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            if (_config.ComponentUnsafeMonster[battle.Enemies[index].MonsterId])
                return int.MaxValue;
        }
        for (int index = 0; index < battle.RelicCount; index++)
        {
            if (_config.ComponentUnsafeRelic[battle.Relics[index].Id])
                return int.MaxValue;
        }
        int cardCount = battle.CardCount;
        for (int index = 0; index < cardCount; index++)
        {
            ref readonly FlashCard card = ref battle.Cards[index];
            if (_config.ComponentUnsafeCard[card.Id])
                return int.MaxValue;
            if (card.EnchantmentId != 0 && !_config.SafeEnchantment[card.EnchantmentId - 1])
                return int.MaxValue;
            if (card.AfflictionId != 0 && !_config.SafeAffliction[card.AfflictionId - 1])
                return int.MaxValue;
        }
        // Pending returning cards and player allies are not representable states (the importer rejects
        // Osty; the kernel does not implement returning cards), so both are empty here.
        long regen = 0;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (_config.ComponentUnsafePower[power.Id])
                return int.MaxValue;
            if (power.Id == _config.RegenPowerId && power.Owner == FlashLimits.PlayerOwner)
                regen += Math.Max(0, power.Amount);
            if (power.Id == _config.ReattachPowerId && !ReattachOwnerIsSafe(battle, power.Owner))
                return int.MaxValue;
        }
        for (int slot = 0; slot < battle.PotionSlotCount; slot++)
        {
            ushort potion = battle.Potions[slot];
            if (potion == 0)
                continue;
            if (_config.ComponentUnsafePotion[potion - 1] || _config.RegenPotion[potion - 1])
                return int.MaxValue;
        }
        return StrategicHpRecoveryBound.RegenerationHealingUpperBound(regen, _config.PostCombatHeal);
    }

    private bool ReattachOwnerIsSafe(in FlashBattle battle, byte owner)
    {
        if (owner < FlashLimits.FirstEnemyOwner)
            return false;
        int enemy = owner - FlashLimits.FirstEnemyOwner;
        return (uint)enemy < (uint)battle.EnemyCount
            && _config.ReattachSafeMonster[battle.Enemies[enemy].MonsterId];
    }

    private int KnownNativeRemaining(in FlashBattle battle)
    {
        int cardCount = battle.CardCount;
        for (int index = 0; index < cardCount; index++)
        {
            if (_config.KnownBlockingCard[battle.Cards[index].Id])
                return int.MaxValue;
        }
        // Pending returning cards are not representable; a SwipePower's stolen NotYet/Feed is unknown.
        long regen = 0;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (_config.SwipePower[power.Id])
                return int.MaxValue;
            if (_config.RegenPower[power.Id] && power.Owner == FlashLimits.PlayerOwner)
                regen += Math.Max(0, power.Amount);
        }
        for (int index = 0; index < battle.RelicCount; index++)
        {
            ref readonly FlashRelic relic = ref battle.Relics[index];
            if ((relic.Flags & FlashRelicFlags.Melted) == 0 && _config.KnownBlockingRelic[relic.Id])
                return int.MaxValue;
        }
        bool canUsePotion = !_config.ForceAllPotionsDisabled
            && (_config.MaximumPotionUses is not { } maximum
                || ExplicitPotionUses(battle) < maximum);
        long directHeal = 0;
        for (int slot = 0; slot < battle.PotionSlotCount; slot++)
        {
            ushort potion = battle.Potions[slot];
            if (potion == 0)
                continue;
            int id = potion - 1;
            if (!_config.NativePotion[id] || _config.FairyPotion[id])
                return int.MaxValue;
            if (!canUsePotion)
                continue;
            if (_config.RegenPotion[id])
                regen += Math.Max(0, _config.RegenPotionAmount[id]);
            else if (_config.DirectHealPotion[id])
                directHeal += (long)Math.Ceiling(
                    battle.Player.MaxHp * _config.DirectHealPotionPercent[id] / 100m);
            else if (_config.FruitJuicePotion[id])
                return int.MaxValue;
        }
        return (int)Math.Min(int.MaxValue,
            directHeal + StrategicHpRecoveryBound.RegenerationHealingUpperBound(regen, _config.PostCombatHeal));
    }

    private int ExplicitPotionUses(in FlashBattle battle)
    {
        int uses = 0;
        for (int slot = 0; slot < _config.RootPotionSlotCount; slot++)
        {
            ushort rootPotion = _config.RootPotions[slot];
            if (rootPotion == 0)
                continue;
            if (slot >= battle.PotionSlotCount || battle.Potions[slot] != rootPotion)
                uses++;
        }
        int automaticAfterRoot = battle.Counter(FlashCounter.AutomaticPotionUseCount)
            - _config.InitialAutomaticPotionUses;
        return _config.InitialExplicitPotionUses + uses - automaticAfterRoot;
    }

    /// <summary>K5: the members that differ, as <c>name old=… flash=…</c>.</summary>
    public static List<string> Diff(in FlashBoundValues old, in FlashBoundValues flash)
    {
        List<string> lines = [];
        void Check<T>(string name, T left, T right)
        {
            if (!EqualityComparer<T>.Default.Equals(left, right))
                lines.Add($"{name} old={left} flash={right}");
        }
        Check(nameof(FlashBoundValues.HasRisk), old.HasRisk, flash.HasRisk);
        Check(nameof(FlashBoundValues.FutureHealPotential), old.FutureHealPotential, flash.FutureHealPotential);
        Check(nameof(FlashBoundValues.RemainingHealingPotential), old.RemainingHealingPotential, flash.RemainingHealingPotential);
        Check(nameof(FlashBoundValues.FutureHealUsed), old.FutureHealUsed, flash.FutureHealUsed);
        Check(nameof(FlashBoundValues.CumulativePlayerHpLost), old.CumulativePlayerHpLost, flash.CumulativePlayerHpLost);
        Check(nameof(FlashBoundValues.RecoveredPlayerHp), old.RecoveredPlayerHp, flash.RecoveredPlayerHp);
        Check(nameof(FlashBoundValues.PlayerHp), old.PlayerHp, flash.PlayerHp);
        Check(nameof(FlashBoundValues.PlayerMaxHp), old.PlayerMaxHp, flash.PlayerMaxHp);
        Check(nameof(FlashBoundValues.DeathSaveHpRestored), old.DeathSaveHpRestored, flash.DeathSaveHpRestored);
        Check(nameof(FlashBoundValues.ExplicitPotionStrategicCost),
            old.ExplicitPotionStrategicCost, flash.ExplicitPotionStrategicCost);
        Check(nameof(FlashBoundValues.StrategicHpLowerBound), old.StrategicHpLowerBound, flash.StrategicHpLowerBound);
        Check(nameof(FlashBoundValues.SmartPotionRemainingHealingPotential),
            old.SmartPotionRemainingHealingPotential, flash.SmartPotionRemainingHealingPotential);
        Check(nameof(FlashBoundValues.SmartPotionHpLowerBound),
            old.SmartPotionHpLowerBound, flash.SmartPotionHpLowerBound);
        return lines;
    }
}
