// Exports the process-local content table from the canonical native models. Every value is read
// from the game assembly loaded by this process; nothing is written to disk and there is no version
// check. The per-id card cloning, power/relic/potion variable reads and monster state-machine
// compilation reuse the helpers of RulesCompiler so the native compiler and the run-layer builder
// share one content interpretation.
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Unlocks;

using NativeCreature = MegaCrit.Sts2.Core.Entities.Creatures.Creature;
using EngineCardRarity = BlitzSolver.Simulation.Combat.CardRarity;
using EngineCardType = BlitzSolver.Simulation.Combat.CardType;

namespace BlitzSolver.Import;

/// <summary>Builds the one process-local <see cref="ContentTable"/> from <c>ModelDb</c>.</summary>
public static class ContentTableExporter
{
    // Native DecimillipedeSegment.AfterAddedToRoom increments MaxHp by 2m while avoiding a
    // teammate's HP. It is an algorithm step, not a DynamicVar; this is the only such constant.
    private const int DecimillipedeHpStep = 2;

    private static readonly Dictionary<string, ulong> CardEpochMasks = BuildCardEpochMasks();
    private static readonly Dictionary<string, ulong> PotionEpochMasks = BuildPotionEpochMasks();

    public static ContentTable Export(bool forcePotionPriceVaries = false)
    {
        Player scratch = Player.CreateForNewRun(
            ModelDb.AllCharacters.First(), UnlockState.all, 1UL);
        using ScratchAscension ascension = new();
        return new ContentTable
        {
            Cards = ExportCards(scratch, out ContentUnsupported?[] cardUnsupported),
            CardUnsupported = cardUnsupported,
            Encounters = ExportEncounters(ascension),
            Characters = ExportCharacters(out ushort[] characterOrder),
            CharacterOrder = characterOrder,
            Acts = NewRunContentExporter.ExportActs(out ushort[] defaultActs),
            DefaultActs = defaultActs,

            PowerVars = ExportPowerVars(out ContentUnsupported?[] powerUnsupported),
            PowerUnsupported = powerUnsupported,
            PowerDeathHistoryRisk = ExportPowerDeathRisk(),
            Potions = ExportPotions(out ContentUnsupported?[] potionUnsupported),
            PotionUnsupported = potionUnsupported,
            PotionGeneratableInCombat = ExportPotionGeneration(),
            Enchantments = ExportEnchantments(out ContentUnsupported?[] enchantmentUnsupported),
            EnchantmentUnsupported = enchantmentUnsupported,
            RelicVars = ExportRelicVars(out ContentUnsupported?[] relicUnsupported),
            RelicStartVars = ExportRelicStartVars(),
            RelicUnsupported = relicUnsupported,
            SpawnMonsters = ExportSpawnMonsters(ascension,
                out ContentUnsupported?[] monsterUnsupported),
            MonsterUnsupported = monsterUnsupported,
            ColorlessPool = ExportCardPool(ModelDb.CardPool<ColorlessCardPool>()),
            StatusPool = ExportCardPool(ModelDb.CardPool<StatusCardPool>()),
            CursePool = ExportCardPool(ModelDb.CardPool<CurseCardPool>()),
            SharedPotionPool = ExportPotionPool(ModelDb.PotionPool<SharedPotionPool>(), 0),
            SharedRelicPool = NewRunContentExporter.ExportSharedRelicPool(),
            SharedEvents = NewRunContentExporter.ExportSharedEvents(
                out ulong[] eventRequiredEpochs),
            EventRequiredEpochs = eventRequiredEpochs,
            SharedAncients = NewRunContentExporter.ExportSharedAncients(),

            MapRules = NewRunContentExporter.ExportMapRules(ascension),
            BaseOdds = NewRunContentExporter.ExportOdds(),
            AscensionRules = NewRunContentExporter.ExportAscensionRules(),
            RewardRules = ExportRewardRules(ascension),
            ShopRules = ExportShopRules(ascension, forcePotionPriceVaries),
            TutorialRewards = ExportTutorialRewards(),
            RelicRewardFacts = ExportRelicRewardFacts(),
            CardRewardHooks = ExportHookMasks(Registry.CardCount, static id =>
                RulesCompiler.CanonicalCardModels.TryGetValue(Registry.Card(id).NativeId,
                    out CardModel? model) ? model.GetType() : null,
                static id => Registry.Card(id).NativeId),
            EnchantmentRewardHooks = ExportHookMasks(Registry.EnchantmentCount, static id =>
                CanonicalEnchantmentModels.TryGetValue(Registry.Enchantment(id).NativeId,
                    out EnchantmentModel? model) ? model.GetType() : null,
                static id => Registry.Enchantment(id).NativeId),
            PotionRewardHooks = ExportHookMasks(Registry.PotionCount, static id =>
                RulesCompiler.CanonicalPotionModels.TryGetValue(Registry.Potion(id).NativeId,
                    out PotionModel? model) ? model.GetType() : null,
                static id => Registry.Potion(id).NativeId),
            CardShopHooks = ExportShopHookMasks(Registry.CardCount, static id =>
                RulesCompiler.CanonicalCardModels.TryGetValue(Registry.Card(id).NativeId,
                    out CardModel? model) ? model.GetType() : null,
                static id => Registry.Card(id).NativeId),
            EnchantmentShopHooks = ExportShopHookMasks(Registry.EnchantmentCount, static id =>
                CanonicalEnchantmentModels.TryGetValue(Registry.Enchantment(id).NativeId,
                    out EnchantmentModel? model) ? model.GetType() : null,
                static id => Registry.Enchantment(id).NativeId),
            PotionShopHooks = ExportShopHookMasks(Registry.PotionCount, static id =>
                RulesCompiler.CanonicalPotionModels.TryGetValue(Registry.Potion(id).NativeId,
                    out PotionModel? model) ? model.GetType() : null,
                static id => Registry.Potion(id).NativeId),
            CardSetupHooks = ExportSetupHookMasks(Registry.CardCount, static id =>
                RulesCompiler.CanonicalCardModels.TryGetValue(Registry.Card(id).NativeId,
                    out CardModel? model) ? model.GetType() : null,
                static id => Registry.Card(id).GetType(), typeof(CardDef),
                static id => Registry.Card(id).NativeId, CardSetupHookSpecs),
            RelicSetupHooks = ExportSetupHookMasks(Registry.RelicCount, static id =>
                RulesCompiler.CanonicalRelicModels.TryGetValue(Registry.Relic(id).NativeId,
                    out RelicModel? model) ? model.GetType() : null,
                static id => Registry.Relic(id).GetType(), typeof(RelicDef),
                static id => Registry.Relic(id).NativeId, RelicSetupHookSpecs),
            PowerSetupHooks = ExportSetupHookMasks(Registry.PowerCount, static id =>
                RulesCompiler.CanonicalPowerModels.TryGetValue(Registry.Power(id).NativeId,
                    out PowerModel? model) ? model.GetType() : null,
                static id => Registry.Power(id).GetType(), typeof(PowerDef),
                static id => Registry.Power(id).NativeId, PowerSetupHookSpecs),
            EnchantmentSetupHooks = ExportSetupHookMasks(Registry.EnchantmentCount, static id =>
                CanonicalEnchantmentModels.TryGetValue(Registry.Enchantment(id).NativeId,
                    out EnchantmentModel? model) ? model.GetType() : null,
                static id => Registry.Enchantment(id).GetType(), typeof(EnchantmentDef),
                static id => Registry.Enchantment(id).NativeId, EnchantmentSetupHookSpecs),
            NeowEpoch = NewRunContentExporter.ExportNeowEpoch(),
            MapHookRelics = NewRunContentExporter.ExportMapHookRelics(),
            MapHookCards = NewRunContentExporter.ExportMapHookCards(),
            FreeTravelRelics = NewRunContentExporter.ExportFreeTravelRelics(),
            UnknownPointHookRelics = NewRunContentExporter.ExportUnknownPointHookRelics(),
            UnknownPointHookCards = NewRunContentExporter.ExportUnknownPointHookCards(),
            NextEventHookCards = NewRunContentExporter.ExportNextEventHookCards(),
            EventAllowedOverride = NewRunContentExporter.ExportEventAllowedOverrides(),
        };
    }

    private static IReadOnlyDictionary<string, decimal>?[] ExportRelicStartVars()
    {
        IReadOnlyDictionary<string, decimal>?[] vars =
            new IReadOnlyDictionary<string, decimal>?[Registry.RelicCount];
        for (ushort id = 0; id < Registry.RelicCount; id++)
        {
            RelicDef def = Registry.Relic(id);
            if (!RulesCompiler.CanonicalRelicModels.TryGetValue(def.NativeId, out RelicModel? canonical))
                continue;
            Dictionary<string, decimal> map = new(StringComparer.Ordinal);
            foreach ((string name, DynamicVar variable) in canonical.DynamicVars)
                map[name] = variable.BaseValue;
            // These two values are method-body literals in the native modals, not DynamicVars.
            // Keep them here, with the native member named, until the content table can execute
            // the relic hooks itself.
            if (def.NativeId is "SNECKO_EYE" or "FAKE_SNECKO_EYE")
                map["ConfusedPower"] = 1m; // SneckoEye.cs / FakeSneckoEye.cs ApplyPower: 1m
            if (def.NativeId == "FUR_COAT")
                map["SpawnHp"] = 1m; // FurCoat.cs BeforeCombatStart: SetCurrentHp(item, 1m)
            vars[id] = map;
        }
        return vars;
    }

    private static ContentCard?[] ExportCards(Player scratch,
        out ContentUnsupported?[] unsupported)
    {
        ContentCard?[] cards = new ContentCard?[Registry.CardCount];
        unsupported = new ContentUnsupported?[Registry.CardCount];
        for (ushort id = 0; id < Registry.CardCount; id++)
        {
            CardDef def = Registry.Card(id);
            if (!RulesCompiler.CanonicalCardModels.TryGetValue(def.NativeId, out CardModel? canonical))
            {
                unsupported[id] = new ContentUnsupported
                {
                    Reason = ContentReason.UnsupportedCard,
                    Detail = $"{def.NativeId} 没有规范模型",
                };
                continue;
            }
            int maxUpgrade = canonical.MaxUpgradeLevel;
            CardStats[] levels = new CardStats[maxUpgrade + 1];
            RunPropertyBag[] defaultProperties = new RunPropertyBag[maxUpgrade + 1];
            int upgrade = 0;
            try
            {
                for (; upgrade <= maxUpgrade; upgrade++)
                {
                    levels[upgrade] = RulesCompiler.CanonicalStats(canonical, upgrade, scratch);
                    CardModel mutable = canonical.ToMutable();
                    for (int level = 0; level < upgrade; level++)
                    {
                        mutable.UpgradeInternal();
                        mutable.FinalizeUpgradeInternal();
                    }
                    defaultProperties[upgrade] = RunStateImporter.EncodeProperties(
                        mutable.ToSerializable().Props, RunLimits.CardPropertyBytes, def.NativeId);
                }
            }
            catch (UnsupportedImportException error)
            {
                unsupported[id] = new ContentUnsupported
                {
                    Reason = error.Reason,
                    Detail = $"{def.NativeId}+{upgrade}: {error.Detail}",
                };
                continue;
            }
            cards[id] = new ContentCard
            {
                Levels = levels,
                DefaultProperties = defaultProperties,
            };
        }
        return cards;
    }

    private static decimal[]?[] ExportPowerVars(out ContentUnsupported?[] unsupported)
    {
        decimal[]?[] vars = new decimal[]?[Registry.PowerCount];
        unsupported = new ContentUnsupported?[Registry.PowerCount];
        for (ushort id = 0; id < Registry.PowerCount; id++)
        {
            PowerDef def = Registry.Power(id);
            if (!RulesCompiler.CanonicalPowerModels.ContainsKey(def.NativeId))
            {
                unsupported[id] = new ContentUnsupported
                {
                    Reason = ContentReason.UnsupportedPower,
                    Detail = $"{def.NativeId} 没有规范模型",
                };
                if (def.VarNames.Count == 0)
                    vars[id] = [];
                continue;
            }
            if (def.VarNames.Count == 0)
            {
                vars[id] = [];
                continue;
            }
            try
            {
                vars[id] = RulesCompiler.CompilePowerVars(def.NativeId);
            }
            catch (UnsupportedImportException error)
            {
                unsupported[id] = new ContentUnsupported
                {
                    Reason = error.Reason,
                    Detail = $"{def.NativeId}: {error.Detail}",
                };
            }
        }
        return vars;
    }

    private static bool[] ExportPowerDeathRisk()
    {
        bool[] risk = new bool[Registry.PowerCount];
        for (ushort id = 0; id < Registry.PowerCount; id++)
        {
            string nativeId = Registry.Power(id).NativeId;
            if (!RulesCompiler.CanonicalPowerModels.ContainsKey(nativeId))
                continue;
            risk[id] = MirrorRiskTables.PowerRecordsDeathHistoryRisk(nativeId);
        }
        return risk;
    }

    private static PotionStats?[] ExportPotions(out ContentUnsupported?[] unsupported)
    {
        PotionStats?[] potions = new PotionStats?[Registry.PotionCount];
        unsupported = new ContentUnsupported?[Registry.PotionCount];
        for (ushort id = 0; id < Registry.PotionCount; id++)
        {
            string nativeId = Registry.Potion(id).NativeId;
            if (!RulesCompiler.CanonicalPotionModels.ContainsKey(nativeId))
            {
                unsupported[id] = new ContentUnsupported
                {
                    Reason = ContentReason.UnsupportedPotion,
                    Detail = $"{nativeId} 没有规范模型",
                };
                continue;
            }
            try
            {
                potions[id] = RulesCompiler.CompilePotionStats(id);
            }
            catch (UnsupportedImportException error)
            {
                unsupported[id] = new ContentUnsupported
                {
                    Reason = error.Reason,
                    Detail = $"{nativeId}: {error.Detail}",
                };
            }
        }
        return potions;
    }

    private static ContentEnchantment?[] ExportEnchantments(
        out ContentUnsupported?[] unsupported)
    {
        Dictionary<string, EnchantmentModel> canonical = ModelDb.All.OfType<EnchantmentModel>()
            .GroupBy(enchantment => enchantment.Id.Entry, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        ContentEnchantment?[] enchantments = new ContentEnchantment?[Registry.EnchantmentCount];
        unsupported = new ContentUnsupported?[Registry.EnchantmentCount];
        for (ushort id = 0; id < Registry.EnchantmentCount; id++)
        {
            EnchantmentDef def = Registry.Enchantment(id);
            if (!canonical.ContainsKey(def.NativeId))
            {
                unsupported[id] = new ContentUnsupported
                {
                    Reason = ContentReason.UnsupportedEnchantment,
                    Detail = $"{def.NativeId} 没有规范模型",
                };
                continue;
            }
            enchantments[id] = new ContentEnchantment
            {
                NativeId = def.NativeId,
                HooksExt = def.HooksExt,
            };
        }
        return enchantments;
    }

    private static bool[] ExportPotionGeneration()
    {
        bool[] generatable = new bool[Registry.PotionCount];
        for (ushort id = 0; id < Registry.PotionCount; id++)
        {
            if (RulesCompiler.CanonicalPotionModels.TryGetValue(Registry.Potion(id).NativeId,
                    out PotionModel? potion))
                generatable[id] = potion.CanBeGeneratedInCombat;
        }
        return generatable;
    }

    private static decimal[]?[] ExportRelicVars(out ContentUnsupported?[] unsupported)
    {
        decimal[]?[] vars = new decimal[]?[Registry.RelicCount];
        unsupported = new ContentUnsupported?[Registry.RelicCount];
        for (ushort id = 0; id < Registry.RelicCount; id++)
        {
            RelicDef def = Registry.Relic(id);
            if (!RulesCompiler.CanonicalRelicModels.ContainsKey(def.NativeId))
            {
                unsupported[id] = new ContentUnsupported
                {
                    Reason = ContentReason.UnsupportedRelic,
                    Detail = $"{def.NativeId} 没有规范模型",
                };
                if (def.VarNames.Count == 0)
                    vars[id] = [];
                continue;
            }
            if (def.VarNames.Count == 0)
            {
                vars[id] = [];
                continue;
            }
            try
            {
                vars[id] = RulesCompiler.CompileRelicVars(id);
            }
            catch (UnsupportedImportException error)
            {
                unsupported[id] = new ContentUnsupported
                {
                    Reason = error.Reason,
                    Detail = $"{def.NativeId}: {error.Detail}",
                };
            }
        }
        return vars;
    }

    private static MonsterRules?[][] ExportSpawnMonsters(ScratchAscension ascension,
        out ContentUnsupported?[] unsupported)
    {
        MonsterRules?[][] rules = new MonsterRules?[ContentTable.AscensionCount][];
        for (int level = 0; level < rules.Length; level++)
            rules[level] = new MonsterRules?[Registry.MonsterCount];
        unsupported = new ContentUnsupported?[Registry.MonsterCount];
        for (ushort id = 0; id < Registry.MonsterCount; id++)
        {
            MonsterDef def = Registry.Monster(id);
            if (!RulesCompiler.CanonicalMonsterModels.TryGetValue(def.NativeId, out MonsterModel? canonical))
            {
                unsupported[id] = new ContentUnsupported
                {
                    Reason = ContentReason.UnsupportedMonster,
                    Detail = $"{def.NativeId} 没有规范模型",
                };
                continue;
            }
            try
            {
                for (int level = 0; level < ContentTable.AscensionCount; level++)
                {
                    ascension.Set(level);
                    MonsterModel mutable = canonical.ToMutable();
                    if (def.SpawnedStunned && mutable is Wriggler wriggler)
                        wriggler.StartStunned = true;
                    if (def.SpawnStockAmount >= 0 && mutable is Axebot axebot)
                        axebot.StockAmount = def.SpawnStockAmount;
                    NativeCreature creature = RulesCompiler.CreateScratchMonster(
                        mutable, def.SpawnSlotName, NullRunState.Instance);
                    rules[level][id] = RulesCompiler.CompileMonster(mutable, creature,
                        rootPowerAmountsFromCreature: false);
                }
            }
            catch (UnsupportedImportException error)
            {
                unsupported[id] = new ContentUnsupported
                {
                    Reason = error.Reason,
                    Detail = $"{def.NativeId}: {error.Detail}",
                };
                for (int level = 0; level < ContentTable.AscensionCount; level++)
                    rules[level][id] = null;
            }
        }
        return rules;
    }

    /// <summary>
    /// Runs the native <c>AfterAddedToRoom</c> over one generated encounter and records the powers,
    /// HP and auxiliary values that are not present in <c>DynamicVars</c>.
    /// </summary>
    private static Dictionary<(string MonsterId, string? Slot), CombatStartRecord> CaptureCombatStarts(
        EncounterModel encounter, int level, ScratchAscension ascension)
    {
        ascension.Set(level);
        EncounterModel scratchEncounter = encounter.ToMutable();
        scratchEncounter.GenerateMonstersWithSlots(NullRunState.Instance);
        Player player = Player.CreateForNewRun(ModelDb.AllCharacters.First(), UnlockState.all, 1UL);
        CombatState state = new(scratchEncounter, NullRunState.Instance);
        state.AddPlayer(player);
        player.ResetCombatState();
        List<(string Id, string? Slot, NativeCreature Creature)> created = [];
        foreach ((MonsterModel monster, string? slot) in scratchEncounter.MonstersWithSlots)
        {
            NativeCreature creature = state.CreateCreature(monster, CombatSide.Enemy, slot);
            monster.SetUpForCombat();
            state.AddCreature(creature);
            created.Add((monster.Id.Entry, slot, creature));
        }
        foreach ((string _, string? _, NativeCreature creature) in created)
        {
            if (!creature.Monster!.AfterAddedToRoom().Wait(TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException(
                    $"原版 {creature.Monster!.Id.Entry}.AfterAddedToRoom 未在 5 秒内完成。");
        }
        Dictionary<(string, string?), CombatStartRecord> result = [];
        foreach ((string id, string? slot, NativeCreature creature) in created)
        {
            List<CombatStartPower> powers = [];
            foreach (PowerModel power in creature.Powers)
            {
                bool ownerIsTarget = power.Owner == creature;
                bool ownerIsPlayer = power.Owner == player.Creature;
                if (!ownerIsTarget && !ownerIsPlayer)
                    continue;
                if (power.Applier != creature && power.Applier != null)
                    continue;
                int aux0 = 0;
                if (power is WitheringPresencePower withering)
                    aux0 = withering.DynamicVars["CardsLeft"].IntValue;
                powers.Add(new CombatStartPower
                {
                    PowerId = Registry.TryPower(power.Id.Entry, out ushort powerId)
                        ? powerId
                        : throw new InvalidOperationException($"原版 Power {power.Id.Entry} 没有 Blitz 定义。"),
                    Amount = power.Amount,
                    Target = ownerIsPlayer ? CombatStartPowerTarget.Player : CombatStartPowerTarget.Owner,
                    ApplierIsOwner = power.Applier == creature,
                    TargetsPlayer = power.Target == player.Creature,
                    Aux0 = aux0,
                });
            }
            foreach (PowerModel power in player.Creature.Powers)
            {
                if (power.Applier != creature)
                    continue;
                powers.Add(new CombatStartPower
                {
                    PowerId = Registry.TryPower(power.Id.Entry, out ushort powerId)
                        ? powerId
                        : throw new InvalidOperationException($"原版 Power {power.Id.Entry} 没有 Blitz 定义。"),
                    Amount = power.Amount,
                    Target = CombatStartPowerTarget.Player,
                    ApplierIsOwner = true,
                    TargetsPlayer = false,
                    Aux0 = 0,
                });
            }
            CombatStartHpMode hpMode = creature.Monster is DecimillipedeSegment
                ? CombatStartHpMode.UniqueEven
                : CombatStartHpMode.None;
            result[(id, slot)] = new CombatStartRecord
            {
                Powers = [.. powers],
                HpMode = hpMode,
                // Native DecimillipedeSegment.AfterAddedToRoom increments by 2m; this is an
                // algorithm step, not a DynamicVar. It is the only such fallback in this table.
                UniqueHpStep = hpMode == CombatStartHpMode.UniqueEven ? DecimillipedeHpStep : 0,
            };
        }
        return result;
    }

    private static CombatStartRecord CaptureSingleStart(EncounterModel encounter, string monsterId,
        string? slot, int level, ScratchAscension ascension)
    {
        ascension.Set(level);
        MonsterModel canonical = encounter.AllPossibleMonsters
            .First(monster => monster.Id.Entry == monsterId);
        MonsterModel mutable = canonical.ToMutable();
        Player player = Player.CreateForNewRun(ModelDb.AllCharacters.First(), UnlockState.all, 1UL);
        CombatState state = new(null, NullRunState.Instance);
        state.AddPlayer(player);
        player.ResetCombatState();
        NativeCreature creature = state.CreateCreature(mutable, CombatSide.Enemy, slot);
        mutable.SetUpForCombat();
        state.AddCreature(creature);
        if (!creature.Monster!.AfterAddedToRoom().Wait(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException(
                $"原版 {creature.Monster!.Id.Entry}.AfterAddedToRoom 未在 5 秒内完成。");
        List<CombatStartPower> powers = [];
        foreach (PowerModel power in creature.Powers)
        {
            bool ownerIsTarget = power.Owner == creature;
            bool ownerIsPlayer = power.Owner == player.Creature;
            if (!ownerIsTarget && !ownerIsPlayer)
                continue;
            if (power.Applier != creature && power.Applier != null)
                continue;
            int aux0 = power is WitheringPresencePower withering
                ? withering.DynamicVars["CardsLeft"].IntValue
                : 0;
            powers.Add(new CombatStartPower
            {
                PowerId = Registry.TryPower(power.Id.Entry, out ushort powerId)
                    ? powerId
                    : throw new InvalidOperationException($"原版 Power {power.Id.Entry} 没有 Blitz 定义。"),
                Amount = power.Amount,
                Target = ownerIsPlayer ? CombatStartPowerTarget.Player : CombatStartPowerTarget.Owner,
                ApplierIsOwner = power.Applier == creature,
                TargetsPlayer = power.Target == player.Creature,
                Aux0 = aux0,
            });
        }
        foreach (PowerModel power in player.Creature.Powers)
        {
            if (power.Applier != creature)
                continue;
            powers.Add(new CombatStartPower
            {
                PowerId = Registry.TryPower(power.Id.Entry, out ushort powerId)
                    ? powerId
                    : throw new InvalidOperationException($"原版 Power {power.Id.Entry} 没有 Blitz 定义。"),
                Amount = power.Amount,
                Target = CombatStartPowerTarget.Player,
                ApplierIsOwner = true,
                TargetsPlayer = false,
                Aux0 = 0,
            });
        }
        CombatStartHpMode hpMode = creature.Monster is DecimillipedeSegment
            ? CombatStartHpMode.UniqueEven
            : CombatStartHpMode.None;
        return new CombatStartRecord
        {
            Powers = [.. powers],
            HpMode = hpMode,
            UniqueHpStep = hpMode == CombatStartHpMode.UniqueEven ? DecimillipedeHpStep : 0,
        };
    }

    private static ContentEncounter?[] ExportEncounters(ScratchAscension ascension)
    {
        Dictionary<string, EncounterModel> canonical = ModelDb.All.OfType<EncounterModel>()
            .GroupBy(encounter => encounter.Id.Entry, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        ContentEncounter?[] encounters = new ContentEncounter?[RunCatalog.EncounterCount];
        for (ushort id = 0; id < RunCatalog.EncounterCount; id++)
        {
            string nativeId = RunCatalog.EncounterName(id);
            if (!canonical.TryGetValue(nativeId, out EncounterModel? encounter))
            {
                encounters[id] = UnsupportedEncounter(nativeId,
                    ContentReason.UnsupportedEncounter, $"{nativeId} 没有规范模型");
                continue;
            }
            try
            {
                EncounterModel probe = encounter.ToMutable();
                probe.GenerateMonstersWithSlots(NullRunState.Instance);
                List<string> slots = [.. probe.Slots];
                List<(string MonsterId, string? Slot)> combinations = [];
                HashSet<(string, string?)> seen = [];
                Dictionary<string, HashSet<string?>> observed = new(StringComparer.Ordinal);
                foreach ((MonsterModel monster, string? slot) in probe.MonstersWithSlots)
                {
                    string? mapped = slot != null && slots.Contains(slot, StringComparer.Ordinal) ? slot : null;
                    if (!observed.TryGetValue(monster.Id.Entry, out HashSet<string?>? set))
                    {
                        set = new HashSet<string?>();
                        observed.Add(monster.Id.Entry, set);
                    }
                    set.Add(mapped);
                }
                foreach (MonsterModel possible in encounter.AllPossibleMonsters)
                {
                    IEnumerable<string?> candidates = observed.TryGetValue(possible.Id.Entry, out HashSet<string?>? set)
                        ? set
                        : slots.Count == 0 ? [null] : SlotCandidates(possible, slots);
                    foreach (string? slot in candidates)
                    {
                        if (seen.Add((possible.Id.Entry, slot)))
                            combinations.Add((possible.Id.Entry, slot));
                    }
                }
                foreach ((string monsterId, HashSet<string?> set) in observed)
                {
                    foreach (string? slot in set)
                    {
                        if (seen.Add((monsterId, slot)))
                            combinations.Add((monsterId, slot));
                    }
                }
                Dictionary<int, Dictionary<(string, string?), CombatStartRecord>> starts = [];
                for (int level = 0; level < ContentTable.AscensionCount; level++)
                    starts[level] = CaptureCombatStarts(encounter, level, ascension);
                List<ContentEncounterMonster> monsters = [];
                string? unsupportedReason = null;
                string? unsupportedDetail = null;
                HashSet<(ushort, string?)> emitted = [];
                foreach ((string possibleId, string? slot) in combinations)
                {
                    if (!Registry.TryMonster(possibleId, out ushort monsterId))
                    {
                        unsupportedReason ??= ContentReason.UnsupportedMonster;
                        unsupportedDetail ??= possibleId;
                        continue;
                    }
                    if (!emitted.Add((monsterId, slot)))
                        continue;
                    MonsterModel canonicalMonster = encounter.AllPossibleMonsters
                        .First(monster => monster.Id.Entry == possibleId);
                    MonsterRules[] byAscension = new MonsterRules[ContentTable.AscensionCount];
                    bool compiled = true;
                    for (int level = 0; level < byAscension.Length; level++)
                    {
                        ascension.Set(level);
                        MonsterModel variant = canonicalMonster.ToMutable();
                        try
                        {
                            if (variant is MegaCrit.Sts2.Core.Models.Monsters.WaterfallGiant waterfall)
                            {
                                Type type = typeof(MegaCrit.Sts2.Core.Models.Monsters.WaterfallGiant);
                                PropertyInfo basePressure = type.GetProperty("BasePressureGunDamage",
                                        BindingFlags.Instance | BindingFlags.NonPublic)
                                    ?? throw new MissingMemberException(type.FullName, "BasePressureGunDamage");
                                FieldInfo pressure = type.GetField("_currentPressureGunDamage",
                                        BindingFlags.Instance | BindingFlags.NonPublic)
                                    ?? throw new MissingFieldException(type.FullName, "_currentPressureGunDamage");
                                int value = basePressure.GetValue(waterfall) is int damage
                                    ? damage
                                    : throw new InvalidOperationException(
                                        "WaterfallGiant.BasePressureGunDamage 不是 int。");
                                pressure.SetValue(waterfall, value);
                            }
                            NativeCreature creature = RulesCompiler.CreateScratchMonster(
                                variant, slot, NullRunState.Instance);
                            byAscension[level] = RulesCompiler.CompileMonster(variant, creature,
                                rootPowerAmountsFromCreature: false);
                        }
                        catch (UnsupportedImportException error)
                        {
                            unsupportedReason ??= error.Reason;
                            unsupportedDetail ??= $"{possibleId}{(slot == null ? "" : "/" + slot)}: {error.Detail}";
                            compiled = false;
                            break;
                        }
                    }
                    if (!compiled)
                    {
                        emitted.Remove((monsterId, slot));
                        continue;
                    }
                    CombatStartRecord?[] startByAscension =
                        new CombatStartRecord?[ContentTable.AscensionCount];
                    for (int level = 0; level < startByAscension.Length; level++)
                    {
                        if (!starts[level].TryGetValue((possibleId, slot), out CombatStartRecord? record))
                        {
                            record = CaptureSingleStart(encounter, possibleId, slot, level, ascension);
                            starts[level][(possibleId, slot)] = record;
                        }
                        startByAscension[level] = record;
                    }
                    monsters.Add(new ContentEncounterMonster
                    {
                        MonsterId = monsterId,
                        Slot = slot,
                        RulesByAscension = byAscension,
                        CombatStartByAscension = startByAscension,
                    });
                }
                encounters[id] = new ContentEncounter
                {
                    NativeId = nativeId,
                    Slots = [.. slots],
                    Monsters = [.. monsters],
                    RoomType = (RunRoomType)(int)encounter.RoomType,
                    IsWeak = encounter.IsWeak,
                    TagMask = ExportEncounterTags(encounter),
                    ShouldGiveRewards = encounter.ShouldGiveRewards,
                    MinGoldByAscension = ExportEncounterGold(encounter, ascension, minimum: true),
                    MaxGoldByAscension = ExportEncounterGold(encounter, ascension, minimum: false),
                    CustomGoldProportion = HasCustomGoldProportion(encounter),
                    UnsupportedReason = unsupportedReason,
                    UnsupportedDetail = unsupportedDetail,
                };
            }
            catch (UnsupportedImportException error)
            {
                encounters[id] = UnsupportedEncounter(nativeId, error.Reason, error.Detail);
            }
        }
        return encounters;
    }

    private static int[] ExportEncounterGold(EncounterModel encounter, ScratchAscension? ascension,
        bool minimum)
    {
        int[] values = new int[ContentTable.AscensionCount];
        for (int level = 0; level < values.Length; level++)
        {
            if (ascension == null)
            {
                values[level] = minimum ? encounter.MinGoldReward : encounter.MaxGoldReward;
                continue;
            }
            ascension.Set(level);
            values[level] = minimum ? encounter.MinGoldReward : encounter.MaxGoldReward;
        }
        ascension?.Set(0);
        return values;
    }

    private static bool HasCustomGoldProportion(EncounterModel encounter)
        => encounter.GetType().GetMethod(nameof(EncounterModel.CalculateGoldProportion),
            BindingFlags.Instance | BindingFlags.Public)?.DeclaringType != typeof(EncounterModel);

    private static ulong ExportEncounterTags(EncounterModel encounter)
    {
        ulong mask = 0;
        foreach (MegaCrit.Sts2.Core.Entities.Encounters.EncounterTag tag in encounter.Tags)
            mask |= 1UL << (int)tag;
        return mask;
    }

    private static IReadOnlyList<string?> SlotCandidates(MonsterModel monster, List<string> slots)
    {
        if (Registry.TryMonster(monster.Id.Entry, out ushort id))
        {
            string? spawnSlot = Registry.Monster(id).SpawnSlotName;
            if (spawnSlot != null)
            {
                int end = spawnSlot.Length;
                while (end > 0 && char.IsDigit(spawnSlot[end - 1]))
                    end--;
                string prefix = spawnSlot[..end];
                List<string?> filtered = [.. slots
                    .Where(slot => slot.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(static slot => (string?)slot)];
                if (filtered.Count != 0)
                    return filtered;
            }
        }
        return [.. slots.Select(static slot => (string?)slot)];
    }

    private static ContentEncounter UnsupportedEncounter(string nativeId, string reason, string detail)
    {
        EncounterModel? canonical = ModelDb.All.OfType<EncounterModel>()
            .FirstOrDefault(model => model.Id.Entry == nativeId);
        return new ContentEncounter
        {
            NativeId = nativeId,
            Slots = [],
            Monsters = [],
            RoomType = canonical == null ? RunRoomType.Unassigned : (RunRoomType)(int)canonical.RoomType,
            IsWeak = canonical?.IsWeak ?? false,
            TagMask = canonical == null ? 0UL : ExportEncounterTags(canonical),
            ShouldGiveRewards = canonical?.ShouldGiveRewards ?? true,
            MinGoldByAscension = canonical == null
                ? new int[ContentTable.AscensionCount]
                : ExportEncounterGold(canonical, null, minimum: true),
            MaxGoldByAscension = canonical == null
                ? new int[ContentTable.AscensionCount]
                : ExportEncounterGold(canonical, null, minimum: false),
            CustomGoldProportion = canonical != null && HasCustomGoldProportion(canonical),
            UnsupportedReason = reason,
            UnsupportedDetail = detail,
        };
    }

    private static ContentCharacter?[] ExportCharacters(out ushort[] order)
    {
        List<CharacterModel> characters = [.. ModelDb.AllCharacters];
        ContentCharacter?[] result = new ContentCharacter?[RunCatalog.CharacterCount];
        List<ushort> ids = [];
        foreach (CharacterModel character in characters)
        {
            if (!RunCatalog.TryCharacter(character.Id.Entry, out ushort id))
                continue;
            ulong unlockMask = CharacterUnlockMask(character);
            ulong potionUnlockMask = PotionUnlockMask(character.PotionPool);
            result[id] = new ContentCharacter
            {
                Id = id,
                NativeId = character.Id.Entry,
                UnlockEpochs = unlockMask,
                CardPool = ExportCardPool(character.CardPool),
                PotionPool = ExportPotionPool(character.PotionPool, potionUnlockMask),
                PotionUnlockEpochs = potionUnlockMask,
                Starting = NewRunContentExporter.ExportStarting(character),
                RelicPool = NewRunContentExporter.ExportCharacterRelicPool(character),
            };
            ids.Add(id);
        }
        order = [.. ids];
        return result;
    }

    private static ContentCardPool ExportCardPool(CardPoolModel pool)
        => new()
        {
            Cards = [.. pool.AllCards.Select(card => new ContentPoolEntry
            {
                CardId = Registry.TryCard(card.Id.Entry, out ushort id) ? id : ContentPoolEntry.NoCard,
                NativeId = card.Id.Entry,
                Type = (EngineCardType)(byte)card.Type,
                Rarity = (EngineCardRarity)(byte)card.Rarity,
                CanBeGeneratedInCombat = card.CanBeGeneratedInCombat,
                MultiplayerOnly = card.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly,
                EnergyCost = card.EnergyCost.Canonical,
                CostsX = card.EnergyCost.CostsX,
                Ethereal = card.Keywords.Contains(CardKeyword.Ethereal),
                RequiredEpochs = CardEpochMasks.GetValueOrDefault(card.Id.Entry),
            })],
        };

    private static ContentPotionEntry[] ExportPotionPool(PotionPoolModel pool, ulong extraEpochs)
        => [.. pool.AllPotions.Select(potion => new ContentPotionEntry
        {
            PotionId = Registry.TryPotion(potion.Id.Entry, out ushort id) ? id : ContentPotionEntry.NoPotion,
            NativeId = potion.Id.Entry,
            CanBeGeneratedInCombat = potion.CanBeGeneratedInCombat,
            RequiredEpochs = PotionEpochMasks.GetValueOrDefault(potion.Id.Entry) | extraEpochs,
        })];

    private static ulong CharacterUnlockMask(CharacterModel character)
    {
        ulong mask = 0;
        foreach (string epochId in EpochModel.AllEpochIds)
        {
            if (!RunCatalog.TryEpoch(epochId, out ushort id))
                continue;
            UnlockState without = WithoutEpoch(epochId);
            if (!without.Characters.Contains(character))
                mask |= 1UL << id;
        }
        return mask;
    }

    private static ulong PotionUnlockMask(PotionPoolModel pool)
    {
        ulong mask = 0;
        foreach (string epochId in EpochModel.AllEpochIds)
        {
            if (!RunCatalog.TryEpoch(epochId, out ushort id))
                continue;
            UnlockState without = WithoutEpoch(epochId);
            if (!pool.GetUnlockedPotions(without).Any())
                mask |= 1UL << id;
        }
        return mask;
    }

    private static UnlockState WithoutEpoch(string excluded)
        => new(EpochModel.AllEpochIds.Where(id => !string.Equals(id, excluded, StringComparison.Ordinal)),
            Array.Empty<ModelId>(), 0);

    private static Dictionary<string, ulong> BuildCardEpochMasks()
        => BuildEpochMasks("Cards");

    private static Dictionary<string, ulong> BuildPotionEpochMasks()
        => BuildEpochMasks("Potions");

    private static Dictionary<string, ulong> BuildEpochMasks(string propertyName)
    {
        Dictionary<string, ulong> masks = new(StringComparer.Ordinal);
        foreach (Type type in EpochModel.AllEpochs)
        {
            PropertyInfo? property = type.GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Static);
            if (property?.GetValue(null) is not System.Collections.IEnumerable models)
                continue;
            string? epochId = EpochModel.AllEpochIds.FirstOrDefault(
                id => EpochModel.Get(id)?.GetType() == type);
            if (epochId is null || !RunCatalog.TryEpoch(epochId, out ushort epoch))
                continue;
            foreach (object? model in models)
            {
                if (model is not AbstractModel abstractModel)
                    continue;
                masks[abstractModel.Id.Entry] =
                    masks.GetValueOrDefault(abstractModel.Id.Entry) | (1UL << epoch);
            }
        }
        return masks;
    }

    /// <summary>
    /// Temporary ascension context for exporting the ascension-dependent monster numbers. The
    /// native content getters read <c>RunManager.Instance.HasAscension</c>; outside a run that
    /// always answers false. The exporter sets the manager's private state to an uninitialized
    /// placeholder (only the non-null test is read) and an <see cref="AscensionManager"/> for each
    /// level, then restores both. It refuses to run while a real run is in progress.
    /// </summary>
    internal sealed class ScratchAscension : IDisposable
    {
        private readonly RunManager _manager;
        private readonly FieldInfo _stateField;
        private readonly FieldInfo _ascensionField;
        private readonly object? _savedState;
        private readonly object? _savedAscension;

        public ScratchAscension()
        {
            _manager = RunManager.Instance;
            if (_manager.IsInProgress)
                throw new InvalidOperationException("内容表必须在跑局开始前导出。");
            _stateField = BackingField("State");
            _ascensionField = BackingField("AscensionManager");
            _savedState = _stateField.GetValue(_manager);
            _savedAscension = _ascensionField.GetValue(_manager);
            _stateField.SetValue(_manager,
                RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Runs.RunState)));
            _ascensionField.SetValue(_manager, new AscensionManager(0));
        }

        public void Set(int level)
            => _ascensionField.SetValue(_manager, new AscensionManager(level));

        public void Dispose()
        {
            _stateField.SetValue(_manager, _savedState);
            _ascensionField.SetValue(_manager, _savedAscension);
        }

        private static FieldInfo BackingField(string property)
            => typeof(RunManager).GetField($"<{property}>k__BackingField",
                   BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(RunManager).FullName, property);
    }

    // ---------------------------------------------------------------- reward content

    private static ContentRewardRules ExportRewardRules(ScratchAscension ascension)
    {
        ascension.Set(0);
        ContentCardRarityOdds normal = new()
        {
            Rare = (float)InvokeBaseOdds(CardRarityOddsType.RegularEncounter,
                MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Rare),
            Uncommon = (float)InvokeBaseOdds(CardRarityOddsType.RegularEncounter,
                MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Uncommon),
        };
        ContentCardRarityOdds elite = new()
        {
            Rare = (float)InvokeBaseOdds(CardRarityOddsType.EliteEncounter,
                MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Rare),
            Uncommon = (float)InvokeBaseOdds(CardRarityOddsType.EliteEncounter,
                MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Uncommon),
        };
        ContentCardRarityOdds boss = new()
        {
            Rare = (float)InvokeBaseOdds(CardRarityOddsType.BossEncounter,
                MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Rare),
            Uncommon = (float)InvokeBaseOdds(CardRarityOddsType.BossEncounter,
                MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Uncommon),
        };
        float rarityGrowth = new CardRarityOdds(new MegaCrit.Sts2.Core.Random.Rng(0UL)).RarityGrowth;
        float upgradeScaling = (float)ReadStaticProperty<decimal>(typeof(CardFactory),
            "UpgradedCardOddScaling");
        ascension.Set((int)AscensionLevel.Scarcity);
        ContentCardRarityOdds scarcityNormal = new()
        {
            Rare = (float)InvokeBaseOdds(CardRarityOddsType.RegularEncounter,
                MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Rare),
            Uncommon = (float)InvokeBaseOdds(CardRarityOddsType.RegularEncounter,
                MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Uncommon),
        };
        ContentCardRarityOdds scarcityElite = new()
        {
            Rare = (float)InvokeBaseOdds(CardRarityOddsType.EliteEncounter,
                MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Rare),
            Uncommon = (float)InvokeBaseOdds(CardRarityOddsType.EliteEncounter,
                MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Uncommon),
        };
        ContentCardRarityOdds scarcityBoss = new()
        {
            Rare = (float)InvokeBaseOdds(CardRarityOddsType.BossEncounter,
                MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Rare),
            Uncommon = (float)InvokeBaseOdds(CardRarityOddsType.BossEncounter,
                MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Uncommon),
        };
        float scarcityGrowth = new CardRarityOdds(new MegaCrit.Sts2.Core.Random.Rng(0UL)).RarityGrowth;
        float scarcityUpgrade = (float)ReadStaticProperty<decimal>(typeof(CardFactory),
            "UpgradedCardOddScaling");

        PotionRewardOdds potion = new(new MegaCrit.Sts2.Core.Random.Rng(0UL));
        float[] potionRoll = [.. FloatConstants(typeof(PotionRewardOdds).GetMethod(
            nameof(PotionRewardOdds.Roll))!).Where(static value => value > 0f)];
        float[] relicRoll = [.. FloatConstants(typeof(RelicFactory).GetMethod(
            nameof(RelicFactory.RollRarity), [typeof(MegaCrit.Sts2.Core.Random.Rng)])!)
            .Where(static value => value < 1f)];
        float[] potionCreate = [.. FloatConstants(typeof(PotionFactory).GetMethod(
            "CreateRandomPotions", BindingFlags.NonPublic | BindingFlags.Static)!)
            .Where(static value => value < 1f)];
        RequireConstantCount(nameof(PotionRewardOdds.Roll), potionRoll, 5);
        if (potionRoll[0] != PotionRewardOdds.eliteBonus
            || potionRoll[2] != 1f
            || potionRoll[^2] != potionRoll[^1])
        {
            throw new InvalidDataException(
                $"PotionRewardOdds.Roll 的常量结构不符：{string.Join(',', potionRoll)}");
        }
        RequireThresholds(nameof(RelicFactory.RollRarity), relicRoll);
        RequireThresholds("PotionFactory.CreateRandomPotions", potionCreate);
        return new ContentRewardRules
        {
            PotionBaseOdds = potion.CurrentValue,
            PotionPityStep = potionRoll[^1],
            PotionEliteBonus = PotionRewardOdds.eliteBonus,
            PotionEliteBonusScale = potionRoll[1],
            RarityOffsetBase = ConstField(typeof(CardRarityOdds), "_baseRarityOffset"),
            RarityOffsetMax = ConstField(typeof(CardRarityOdds), "_maxRarityOffset"),
            Regular = normal,
            Elite = elite,
            Boss = boss,
            ScarcityRegular = scarcityNormal,
            ScarcityElite = scarcityElite,
            ScarcityBoss = scarcityBoss,
            RarityGrowth = rarityGrowth,
            ScarcityRarityGrowth = scarcityGrowth,
            UpgradeScaling = upgradeScaling,
            ScarcityUpgradeScaling = scarcityUpgrade,
            RelicCommonThreshold = relicRoll[0],
            RelicUncommonThreshold = relicRoll[1],
            PotionRareThreshold = potionCreate[0],
            PotionUncommonThreshold = potionCreate[1],
        };
    }

    private static void RequireConstantCount(string method, float[] values, int expected)
    {
        if (values.Length != expected)
        {
            throw new InvalidDataException(
                $"{method} 过滤后的常量个数是 {values.Length}，预期 {expected}：" +
                string.Join(',', values));
        }
    }

    private static void RequireConstantCount(string method, int[] values, int expected)
    {
        if (values.Length != expected)
        {
            throw new InvalidDataException(
                $"{method} 过滤后的常量个数是 {values.Length}，预期 {expected}：" +
                string.Join(',', values));
        }
    }

    private static void RequireThresholds(string method, float[] values)
    {
        RequireConstantCount(method, values, 2);
        if (!(values[0] > 0f && values[0] < values[1] && values[1] < 1f))
        {
            throw new InvalidDataException(
                $"{method} 的稀有度阈值不是 (0,1) 内的严格递增序列：" +
                string.Join(',', values));
        }
    }

    private static object InvokeBaseOdds(CardRarityOddsType type,
        MegaCrit.Sts2.Core.Entities.Cards.CardRarity rarity)
    {
        MethodInfo method = typeof(CardRarityOdds).GetMethod("GetBaseOdds",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(CardRarityOdds).FullName, "GetBaseOdds");
        return method.Invoke(null, [type, rarity])
            ?? throw new InvalidDataException("CardRarityOdds.GetBaseOdds 返回空。");
    }

    private static T ReadStaticProperty<T>(Type type, string name)
    {
        PropertyInfo property = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public
            | BindingFlags.Static)
            ?? throw new MissingMemberException(type.FullName, name);
        return (T)(property.GetValue(null)
            ?? throw new InvalidDataException($"{type.Name}.{name} 返回空。"));
    }

    private static float ConstField(Type type, string name)
    {
        FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static
            | BindingFlags.Public)
            ?? throw new MissingFieldException(type.FullName, name);
        return (float)(field.GetRawConstantValue()
            ?? throw new InvalidDataException($"{type.Name}.{name} 不是常量。"));
    }

    /// <summary>
    /// Reads every <c>ldc.r4</c>/<c>ldc.r8</c> literal of a method body in execution order. The
    /// reward odds contain a few inline method-body constants that no field exposes; the content
    /// table still has to carry the exact values rather than a Core literal.
    /// </summary>
    private static float[] FloatConstants(MethodBase method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidDataException($"{method.DeclaringType?.Name}.{method.Name} 没有 IL。");
        Dictionary<ushort, OpCode> opcodes = [];
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode opcode)
                opcodes[(ushort)opcode.Value] = opcode;
        }
        List<float> values = [];
        int offset = 0;
        while (offset < il.Length)
        {
            ushort code = il[offset++];
            if (code == 0xFE)
                code = (ushort)(0xFE00 | il[offset++]);
            OpCode opcode = opcodes.TryGetValue(code, out OpCode found)
                ? found
                : throw new InvalidDataException(
                    $"{method.Name} 的 IL 含未知操作码 0x{code:X4}。");
            if (opcode == OpCodes.Ldc_R4)
            {
                values.Add(BitConverter.ToSingle(il, offset));
                offset += 4;
                continue;
            }
            if (opcode == OpCodes.Ldc_R8)
            {
                values.Add((float)BitConverter.ToDouble(il, offset));
                offset += 8;
                continue;
            }
            offset += OperandSize(opcode.OperandType, il, offset);
        }
        return [.. values];
    }

    private static int OperandSize(OperandType type, byte[] il, int offset) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
            or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
            or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
        _ => throw new InvalidDataException($"未知 IL 操作数类型 {type}。"),
    };

    // ---------------------------------------------------------------- merchant content

    private static ContentShopRules ExportShopRules(ScratchAscension ascension,
        bool forcePotionPriceVaries)
    {
        MegaCrit.Sts2.Core.Entities.Cards.CardType[] cardTypes =
            ReadStaticField<MegaCrit.Sts2.Core.Entities.Cards.CardType[]>(
                typeof(MerchantInventory), "_coloredCardTypes");
        MegaCrit.Sts2.Core.Entities.Cards.CardRarity[] colorlessRarities =
            ReadStaticField<MegaCrit.Sts2.Core.Entities.Cards.CardRarity[]>(
                typeof(MerchantInventory), "_colorlessCardRarities");
        if (cardTypes.Length != RunLimits.ShopCharacterCards
            || colorlessRarities.Length != RunLimits.ShopColorlessCards)
        {
            throw new InvalidDataException(
                $"MerchantInventory 的卡槽数量变了：colored={cardTypes.Length} colorless={colorlessRarities.Length}");
        }

        int costSlots = (int)EngineCardRarity.Quest + 1;
        int[] cardCosts = new int[costSlots];
        int[] colorlessCosts = new int[costSlots];
        MethodInfo getCardCost = typeof(MerchantCardEntry).GetMethod("GetCost",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(MerchantCardEntry).FullName, "GetCost");
        foreach (EngineCardRarity rarity in new[]
                 {
                     EngineCardRarity.Common, EngineCardRarity.Uncommon, EngineCardRarity.Rare,
                 })
        {
            CardModel character = FindCanonicalCard(rarity, colorless: false)
                ?? throw new InvalidDataException(
                    $"找不到稀有度 {rarity} 的角色卡来读取商人价格。");
            cardCosts[(int)rarity] = (int)(getCardCost.Invoke(null, [character])
                ?? throw new InvalidDataException("MerchantCardEntry.GetCost 返回空。"));
            CardModel? colorless = FindCanonicalCard(rarity, colorless: true);
            if (colorless != null)
            {
                colorlessCosts[(int)rarity] = (int)(getCardCost.Invoke(null, [colorless])
                    ?? throw new InvalidDataException("MerchantCardEntry.GetCost 返回空。"));
            }
        }

        int[] potionCosts = new int[(int)MegaCrit.Sts2.Core.Entities.Potions.PotionRarity.Token + 1];
        MethodInfo getPotionCost = typeof(MerchantPotionEntry).GetMethod("GetCost",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(MerchantPotionEntry).FullName, "GetCost");
        foreach (MegaCrit.Sts2.Core.Entities.Potions.PotionRarity rarity in new[]
                 {
                     MegaCrit.Sts2.Core.Entities.Potions.PotionRarity.Common,
                     MegaCrit.Sts2.Core.Entities.Potions.PotionRarity.Uncommon,
                     MegaCrit.Sts2.Core.Entities.Potions.PotionRarity.Rare,
                 })
        {
            potionCosts[(int)rarity] = (int)(getPotionCost.Invoke(null, [rarity])
                ?? throw new InvalidDataException("MerchantPotionEntry.GetCost 返回空。"));
        }

        float[] cardRoll = [.. FloatConstants(typeof(MerchantCardEntry).GetMethod(
            nameof(MerchantCardEntry.CalcCost))!).Where(static value => value is > 0f and < 10f)];
        float[] relicRoll = [.. FloatConstants(typeof(MerchantRelicEntry).GetMethod(
            nameof(MerchantRelicEntry.CalcCost))!).Where(static value => value is > 0f and < 10f)];
        float[] potionRoll = [.. FloatConstants(typeof(MerchantPotionEntry).GetMethod(
            nameof(MerchantPotionEntry.CalcCost))!).Where(static value => value is > 0f and < 10f)];
        RequireFactorPair("MerchantCardEntry.CalcCost", cardRoll);
        RequireFactorPair("MerchantRelicEntry.CalcCost", relicRoll);
        RequireFactorPair("MerchantPotionEntry.CalcCost", potionRoll);
        int[] sale = [.. IntConstants(typeof(MerchantCardEntry).GetMethod(
            nameof(MerchantCardEntry.CalcCost))!).Where(static value => value > 1)];
        RequireConstantCount(nameof(MerchantCardEntry.CalcCost), sale, 1);
        if (sale[0] < 2)
            throw new InvalidDataException($"商人打折除数不是大于 1 的整数：{sale[0]}");

        int inflation = (int)AscensionLevel.Inflation;
        ascension.Set(0);
        int removalBase = ReadStaticProperty<int>(typeof(MerchantCardRemovalEntry), "BaseCost");
        int removalIncrease = ReadStaticProperty<int>(typeof(MerchantCardRemovalEntry),
            "PriceIncrease");
        float shopRare = (float)InvokeBaseOdds(CardRarityOddsType.Shop,
            MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Rare);
        float shopUncommon = (float)InvokeBaseOdds(CardRarityOddsType.Shop,
            MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Uncommon);
        ascension.Set((int)AscensionLevel.Scarcity);
        float scarcityShopRare = (float)InvokeBaseOdds(CardRarityOddsType.Shop,
            MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Rare);
        float scarcityShopUncommon = (float)InvokeBaseOdds(CardRarityOddsType.Shop,
            MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Uncommon);
        ascension.Set(inflation);
        int removalBaseInflation = ReadStaticProperty<int>(typeof(MerchantCardRemovalEntry),
            "BaseCost");
        int removalIncreaseInflation = ReadStaticProperty<int>(typeof(MerchantCardRemovalEntry),
            "PriceIncrease");
        return new ContentShopRules
        {
            CharacterCardTypes = [.. cardTypes.Select(static type => (EngineCardType)(int)type)],
            ColorlessCardRarities =
                [.. colorlessRarities.Select(static rarity => (EngineCardRarity)(int)rarity)],
            RelicSlots = RunLimits.ShopRelics,
            RandomRelicRaritySlots = 2,
            FixedRelicRarity = RunRelicRarity.Shop,
            PotionSlots = RunLimits.ShopPotions,
            CardCosts = cardCosts,
            ColorlessCardCosts = colorlessCosts,
            CardPriceMin = cardRoll[0],
            CardPriceMax = cardRoll[1],
            SaleDivisor = sale[0],
            RelicPriceMin = relicRoll[0],
            RelicPriceMax = relicRoll[1],
            PotionCosts = potionCosts,
            PotionPriceMin = potionRoll[0],
            PotionPriceMax = potionRoll[1],
            PotionPriceVaries = forcePotionPriceVaries || TestMode.IsOff,
            WingedBootsRooms = ModelDb.Relic<WingedBoots>().DynamicVars["Rooms"].IntValue,
            RemovalBaseCost = removalBase,
            RemovalBaseCostInflation = removalBaseInflation,
            RemovalPriceIncrease = removalIncrease,
            RemovalPriceIncreaseInflation = removalIncreaseInflation,
            InflationLevel = inflation,
            ShopRareOdds = shopRare,
            ShopUncommonOdds = shopUncommon,
            ScarcityShopRareOdds = scarcityShopRare,
            ScarcityShopUncommonOdds = scarcityShopUncommon,
        };
    }

    private static CardModel? FindCanonicalCard(EngineCardRarity rarity, bool colorless)
    {
        IEnumerable<CardModel> cards = colorless
            ? ModelDb.CardPool<ColorlessCardPool>().AllCards
            : ModelDb.AllCharacters.SelectMany(static character => character.CardPool.AllCards);
        return cards.FirstOrDefault(card => card.Rarity
                == (MegaCrit.Sts2.Core.Entities.Cards.CardRarity)(int)rarity
            && (card.Pool is ColorlessCardPool) == colorless);
    }

    private static T ReadStaticField<T>(Type type, string name)
    {
        FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static
            | BindingFlags.Public)
            ?? throw new MissingFieldException(type.FullName, name);
        return (T)(field.GetValue(null)
            ?? throw new InvalidDataException($"{type.Name}.{name} 返回空。"));
    }

    private static void RequireFactorPair(string method, float[] values)
    {
        RequireConstantCount(method, values, 2);
        if (!(values[0] < 1f && values[1] > 1f && values[1] - values[0] < 1f))
        {
            throw new InvalidDataException(
                $"{method} 的价格倍率不是 1 两侧的一对常量：{string.Join(',', values)}");
        }
    }

    private static ContentShopHookMask[] ExportShopHookMasks(int count,
        Func<ushort, Type?> modelType, Func<ushort, string> nativeId)
    {
        ContentShopHookMask[] masks = new ContentShopHookMask[count];
        for (ushort id = 0; id < count; id++)
        {
            Type? type = modelType(id);
            masks[id] = type == null
                ? new ContentShopHookMask { Overridden = AllShopHooks, Implemented = 0 }
                : BuildShopHookMask(type, nativeId(id));
        }
        return masks;
    }

    private static ContentShopHookMask BuildShopHookMask(Type modelType, string nativeId)
    {
        List<(ContentShopHook Hook, bool Overridden, bool Implemented)> values = [];
        foreach ((ContentShopHook hook, string method, HashSet<string> implemented) in ShopHookSpecs)
        {
            bool overridden = Overrides(modelType, typeof(AbstractModel), method);
            bool listed = implemented.Contains(nativeId);
            if (listed && !overridden)
                throw new InvalidDataException(
                    $"商店 Hook 名单过期：{nativeId} 没有重写 {method}。");
            values.Add((hook, overridden, listed));
        }
        return ContentShopHookMask.From(values);
    }

    private static readonly uint AllShopHooks =
        (uint)(ContentShopHook.ModifyMerchantPrice
            | ContentShopHook.ModifyMerchantCardPool
            | ContentShopHook.ModifyMerchantCardRarity
            | ContentShopHook.ModifyMerchantCardCreationResults
            | ContentShopHook.ShouldRefillMerchantEntry
            | ContentShopHook.AfterItemPurchased
            | ContentShopHook.BeforeCardRemoved
            | ContentShopHook.BeforeShopRoomEntered
            | ContentShopHook.AfterShopRoomEntered);

    /// <summary>
    /// Every native <c>AfterRoomEntered</c> override except LordsParasol. The switch in
    /// <c>RunShopGenerator</c> models the shop-room effect of each; combat/rest-site/treasure-only
    /// branches are no-ops for a merchant room. Achievements/badges are not run-state listeners.
    /// </summary>
    private static readonly HashSet<string> ShopAfterRoomEntered = new(StringComparer.Ordinal)
    {
        "BIG_MUSHROOM", "BRONZE_SCALES", "BURNING_STICKS", "DATA_DISK", "DIVINE_RIGHT",
        "EMBER_TEA", "ETERNAL_FEATHER", "FAKE_VENERABLE_TEA_SET", "GHOST_SEED", "GIRYA",
        "GORGET", "LAVA_LAMP", "MAW_BANK", "MEAL_TICKET", "METRONOME", "ODDLY_SMOOTH_STONE",
        "PANTOGRAPH", "PERMAFROST", "PHILOSOPHERS_STONE", "PLANISPHERE", "RED_SKULL",
        "REGAL_PILLOW", "SILVER_CRUCIBLE", "SLING_OF_COURAGE", "STONE_CALENDAR",
        "STONE_CRACKER", "SWORD_OF_JADE", "THROWING_AXE", "VAJRA", "VELVET_CHOKER",
        "VENERABLE_TEA_SET", "WINGED_BOOTS",
    };

    private static readonly (ContentShopHook Hook, string Method, HashSet<string> Implemented)[]
        ShopHookSpecs =
        [
            (ContentShopHook.ModifyMerchantPrice, "ModifyMerchantPrice",
                Set("MEMBERSHIP_CARD", "THE_COURIER")),
            (ContentShopHook.ModifyMerchantCardPool, "ModifyMerchantCardPool", Set()),
            (ContentShopHook.ModifyMerchantCardRarity, "ModifyMerchantCardRarity", Set()),
            (ContentShopHook.ModifyMerchantCardCreationResults,
                "ModifyMerchantCardCreationResults", Set("FROZEN_EGG", "MOLTEN_EGG", "TOXIC_EGG")),
            (ContentShopHook.ShouldRefillMerchantEntry, "ShouldRefillMerchantEntry",
                Set("THE_COURIER")),
            (ContentShopHook.AfterItemPurchased, "AfterItemPurchased", Set("MAW_BANK")),
            (ContentShopHook.BeforeCardRemoved, "BeforeCardRemoved", Set()),
            (ContentShopHook.BeforeShopRoomEntered, "BeforeRoomEntered", Set()),
            (ContentShopHook.AfterShopRoomEntered, "AfterRoomEntered", ShopAfterRoomEntered),
        ];

    /// <summary>Reads every <c>ldc.i4</c> literal of a method body in execution order.</summary>
    private static int[] IntConstants(MethodBase method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidDataException($"{method.DeclaringType?.Name}.{method.Name} 没有 IL。");
        Dictionary<ushort, OpCode> opcodes = [];
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode opcode)
                opcodes[(ushort)opcode.Value] = opcode;
        }
        List<int> values = [];
        int offset = 0;
        while (offset < il.Length)
        {
            ushort code = il[offset++];
            if (code == 0xFE)
                code = (ushort)(0xFE00 | il[offset++]);
            OpCode opcode = opcodes.TryGetValue(code, out OpCode found)
                ? found
                : throw new InvalidDataException(
                    $"{method.Name} 的 IL 含未知操作码 0x{code:X4}。");
            if (opcode == OpCodes.Ldc_I4_M1)
            {
                values.Add(-1);
                continue;
            }
            if (opcode == OpCodes.Ldc_I4_0)
            {
                values.Add(0);
                continue;
            }
            if (opcode == OpCodes.Ldc_I4_1)
            {
                values.Add(1);
                continue;
            }
            if (opcode == OpCodes.Ldc_I4_2)
            {
                values.Add(2);
                continue;
            }
            if (opcode == OpCodes.Ldc_I4_3)
            {
                values.Add(3);
                continue;
            }
            if (opcode == OpCodes.Ldc_I4_4)
            {
                values.Add(4);
                continue;
            }
            if (opcode == OpCodes.Ldc_I4_5)
            {
                values.Add(5);
                continue;
            }
            if (opcode == OpCodes.Ldc_I4_6)
            {
                values.Add(6);
                continue;
            }
            if (opcode == OpCodes.Ldc_I4_7)
            {
                values.Add(7);
                continue;
            }
            if (opcode == OpCodes.Ldc_I4_8)
            {
                values.Add(8);
                continue;
            }
            if (opcode == OpCodes.Ldc_I4_S)
            {
                values.Add((sbyte)il[offset]);
                offset += 1;
                continue;
            }
            if (opcode == OpCodes.Ldc_I4)
            {
                values.Add(BitConverter.ToInt32(il, offset));
                offset += 4;
                continue;
            }
            offset += OperandSize(opcode.OperandType, il, offset);
        }
        return [.. values];
    }

    private static ContentTutorialRewards ExportTutorialRewards()
    {
        static ContentTutorialCardOffer Offer(ushort[] cards, ushort potion = ContentTutorialCardOffer.NoPotion)
            => new() { Cards = cards, Potion = potion };
        ushort[] Cards<T1, T2, T3>() where T1 : CardModel where T2 : CardModel where T3 : CardModel
            => [RequireCard(ModelDb.Card<T1>()), RequireCard(ModelDb.Card<T2>()),
                RequireCard(ModelDb.Card<T3>())];
        ushort Potion<T>() where T : PotionModel => RequirePotion(ModelDb.Potion<T>());
        ushort Relic<T>() where T : RelicModel => RequireRelic(ModelDb.Relic<T>());
        return new ContentTutorialRewards
        {
            MonsterGoldMin = GoldReward.defaultMinGoldAmount,
            MonsterGoldMax = GoldReward.defaultMaxGoldAmount,
            Monsters =
            [
                Offer(Cards<SetupStrike, Tremble, BloodWall>()),
                Offer(Cards<Breakthrough, Inflame, Anger>()),
                Offer(Cards<IronWave, Dismantle, Cinder>(), Potion<FirePotion>()),
                Offer(Cards<Stomp, ShrugItOff, Armaments>()),
                Offer(Cards<Thunderclap, SetupStrike, Rage>(), Potion<StrengthPotion>()),
                Offer(Cards<BattleTrance, TrueGrit, Uppercut>()),
                Offer(Cards<Bloodletting, Whirlwind, Tremble>(), Potion<EnergyPotion>()),
            ],
            EliteFirst = Offer(Cards<Bludgeon, Pyre, EvilEye>(), Potion<BlockPotion>()),
            EliteSecond = Offer(Cards<Pillage, Rampage, FlameBarrier>()),
            BossFirst = Offer(Cards<PrimalForce, DemonForm, Thrash>()),
            EliteFirstRelic = Relic<Vajra>(),
            EliteSecondRelic = Relic<OrnamentalFan>(),
        };
    }

    private static ContentRelicRewardFacts?[] ExportRelicRewardFacts()
    {
        ContentRelicRewardFacts?[] facts = new ContentRelicRewardFacts?[Registry.RelicCount];
        for (ushort id = 0; id < Registry.RelicCount; id++)
        {
            RelicDef def = Registry.Relic(id);
            if (!RulesCompiler.CanonicalRelicModels.TryGetValue(def.NativeId, out RelicModel? canonical))
                continue;
            RelicModel mutable = canonical.ToMutable();
            bool rollable = canonical.Rarity is RelicRarity.Common or RelicRarity.Uncommon
                or RelicRarity.Rare or RelicRarity.Shop;
            facts[id] = new ContentRelicRewardFacts
            {
                Allowed = RelicAllowed(canonical, def.NativeId),
                Stackable = canonical.IsStackable,
                InitialDisplayAmount = rollable ? mutable.DisplayAmount : 0,
                DefaultProperties = RunStateImporter.EncodeProperties(
                    mutable.ToSerializable().Props, RunLimits.RelicPropertyBytes, def.NativeId),
                Obtain = RelicObtain(canonical),
                RewardHooks = BuildRewardHookMask(canonical.GetType(), def.NativeId),
                ShopHooks = BuildShopHookMask(canonical.GetType(), def.NativeId),
                MerchantCost = canonical.MerchantCost,
                AllowedInShops = canonical.IsAllowedInShops,
            };
        }
        return facts;
    }

    private static ContentRelicAllowedKind RelicAllowed(RelicModel canonical, string nativeId)
    {
        bool overridden = Overrides(canonical.GetType(), typeof(RelicModel), "IsAllowed");
        bool listed = RelicAllowedKinds.TryGetValue(nativeId, out ContentRelicAllowedKind kind);
        if (overridden && !listed)
            return ContentRelicAllowedKind.Unsupported;
        if (!overridden && listed)
            throw new InvalidDataException(
                $"RelicAllowed 名单过期：{nativeId} 没有重写 IsAllowed。");
        return overridden ? kind : ContentRelicAllowedKind.Always;
    }

    private static readonly Dictionary<string, ContentRelicAllowedKind> RelicAllowedKinds =
        new(StringComparer.Ordinal)
        {
            ["MASSIVE_SCROLL"] = ContentRelicAllowedKind.Never,
            ["LASTING_CANDY"] = ContentRelicAllowedKind.NotFirstRunIroncladBeforeAct3,
            ["AMETHYST_AUBERGINE"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["BOOK_OF_FIVE_RINGS"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["BOWLER_HAT"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["DRAGON_FRUIT"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["FROZEN_EGG"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["GIRYA"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["JUZU_BRACELET"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["LUCKY_FYSH"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["MEAL_TICKET"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["MOLTEN_EGG"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["OLD_COIN"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["PLANISPHERE"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["SHOVEL"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["TOXIC_EGG"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["WHITE_BEAST_STATUE"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["WHITE_STAR"] = ContentRelicAllowedKind.BeforeAct3TreasureChest,
            ["SILVER_CRUCIBLE"] = ContentRelicAllowedKind.Always,
            ["WINGED_BOOTS"] = ContentRelicAllowedKind.Always,
        };

    private static ContentRelicObtainKind RelicObtain(RelicModel canonical)
    {
        bool overridden = Overrides(canonical.GetType(), typeof(RelicModel), "AfterObtained");
        bool listed = ImplementedRelicObtains.Contains(canonical.Id.Entry);
        if (overridden && !listed)
            return ContentRelicObtainKind.Unsupported;
        if (!overridden && listed)
            throw new InvalidDataException(
                $"RelicObtain 名单过期：{canonical.Id.Entry} 没有重写 AfterObtained。");
        return overridden ? ContentRelicObtainKind.Implemented : ContentRelicObtainKind.SimpleAdd;
    }

    private static readonly HashSet<string> ImplementedRelicObtains = new(StringComparer.Ordinal)
    {
        "POTION_BELT", "STRAWBERRY", "MANGO", "PEAR", "LEES_WAFFLE", "OLD_COIN",
        "BELT_BUCKLE", "WAR_PAINT", "WHETSTONE", "FAKE_MANGO", "FAKE_LEES_WAFFLE",
        "GOLDEN_PEARL", "NUTRITIOUS_OYSTER", "LOOMING_FRUIT",
    };

    private static readonly (ContentSetupHook Hook, string Method)[] RelicSetupHookSpecs =
    [
        (ContentSetupHook.AfterRoomEntered, "AfterRoomEntered"),
        (ContentSetupHook.BeforeCombatStart, "BeforeCombatStart"),
        (ContentSetupHook.BeforeCombatStartLate, "BeforeCombatStartLate"),
        (ContentSetupHook.BeforeSideTurnStart, "BeforeSideTurnStart"),
        (ContentSetupHook.AfterEnergyReset, "AfterEnergyReset"),
        (ContentSetupHook.AfterEnergyResetLate, "AfterEnergyResetLate"),
        (ContentSetupHook.BeforeHandDraw, "BeforeHandDraw"),
        (ContentSetupHook.BeforeHandDrawLate, "BeforeHandDrawLate"),
        (ContentSetupHook.ModifyHandDraw, "ModifyHandDraw"),
        (ContentSetupHook.ModifyHandDrawLate, "ModifyHandDrawLate"),
        (ContentSetupHook.AfterPlayerTurnStartEarly, "AfterPlayerTurnStartEarly"),
        (ContentSetupHook.AfterPlayerTurnStart, "AfterPlayerTurnStart"),
        (ContentSetupHook.AfterPlayerTurnStartLate, "AfterPlayerTurnStartLate"),
        (ContentSetupHook.AfterSideTurnStart, "AfterSideTurnStart"),
        (ContentSetupHook.AfterSideTurnStartLate, "AfterSideTurnStartLate"),
        (ContentSetupHook.ModifyMaxEnergy, "ModifyMaxEnergy"),
        (ContentSetupHook.ModifyOrbValue, "ModifyOrbValue"),
        (ContentSetupHook.ShouldProcurePotion, "ShouldProcurePotion"),
        (ContentSetupHook.AfterCardDrawnEarly, "AfterCardDrawnEarly"),
        (ContentSetupHook.AfterCardDrawn, "AfterCardDrawn"),
        (ContentSetupHook.AfterCardEnteredCombat, "AfterCardEnteredCombat"),
        (ContentSetupHook.AfterCardGeneratedForCombat, "AfterCardGeneratedForCombat"),
        (ContentSetupHook.ModifyShuffleOrder, "ModifyShuffleOrder"),
        (ContentSetupHook.AfterAutoPrePlayPhaseEntered, "AfterAutoPrePlayPhaseEntered"),
        (ContentSetupHook.AfterAutoPrePlayPhaseEnteredLate, "AfterAutoPrePlayPhaseEnteredLate"),
    ];

    private static readonly (ContentSetupHook Hook, string Method)[] CardSetupHookSpecs =
    [
        (ContentSetupHook.AfterCardDrawnEarly, "AfterCardDrawnEarly"),
        (ContentSetupHook.AfterCardDrawn, "AfterCardDrawn"),
        (ContentSetupHook.AfterCardEnteredCombat, "AfterCardEnteredCombat"),
        (ContentSetupHook.AfterCardGeneratedForCombat, "AfterCardGeneratedForCombat"),
        (ContentSetupHook.ModifyShuffleOrder, "ModifyShuffleOrder"),
    ];

    private static readonly (ContentSetupHook Hook, string Method)[] PowerSetupHookSpecs =
    [
        (ContentSetupHook.BeforeCombatStart, "BeforeCombatStart"),
        (ContentSetupHook.BeforeCombatStartLate, "BeforeCombatStartLate"),
        (ContentSetupHook.BeforeSideTurnStart, "BeforeSideTurnStart"),
        (ContentSetupHook.AfterEnergyReset, "AfterEnergyReset"),
        (ContentSetupHook.AfterEnergyResetLate, "AfterEnergyResetLate"),
        (ContentSetupHook.BeforeHandDraw, "BeforeHandDraw"),
        (ContentSetupHook.BeforeHandDrawLate, "BeforeHandDrawLate"),
        (ContentSetupHook.ModifyHandDraw, "ModifyHandDraw"),
        (ContentSetupHook.ModifyHandDrawLate, "ModifyHandDrawLate"),
        (ContentSetupHook.AfterPlayerTurnStartEarly, "AfterPlayerTurnStartEarly"),
        (ContentSetupHook.AfterPlayerTurnStart, "AfterPlayerTurnStart"),
        (ContentSetupHook.AfterPlayerTurnStartLate, "AfterPlayerTurnStartLate"),
        (ContentSetupHook.AfterSideTurnStart, "AfterSideTurnStart"),
        (ContentSetupHook.AfterSideTurnStartLate, "AfterSideTurnStartLate"),
        (ContentSetupHook.ModifyMaxEnergy, "ModifyMaxEnergy"),
        (ContentSetupHook.ModifyOrbValue, "ModifyOrbValue"),
        (ContentSetupHook.ShouldProcurePotion, "ShouldProcurePotion"),
        (ContentSetupHook.AfterCardDrawnEarly, "AfterCardDrawnEarly"),
        (ContentSetupHook.AfterCardDrawn, "AfterCardDrawn"),
        (ContentSetupHook.AfterCardEnteredCombat, "AfterCardEnteredCombat"),
        (ContentSetupHook.AfterCardGeneratedForCombat, "AfterCardGeneratedForCombat"),
        (ContentSetupHook.ModifyShuffleOrder, "ModifyShuffleOrder"),
        (ContentSetupHook.AfterAutoPrePlayPhaseEntered, "AfterAutoPrePlayPhaseEntered"),
        (ContentSetupHook.AfterAutoPrePlayPhaseEnteredLate, "AfterAutoPrePlayPhaseEnteredLate"),
    ];

    private static readonly (ContentSetupHook Hook, string Method)[] EnchantmentSetupHookSpecs =
    [
        (ContentSetupHook.AfterCardDrawnEarly, "AfterCardDrawnEarly"),
        (ContentSetupHook.AfterCardDrawn, "AfterCardDrawn"),
        (ContentSetupHook.AfterCardEnteredCombat, "AfterCardEnteredCombat"),
        (ContentSetupHook.AfterCardGeneratedForCombat, "AfterCardGeneratedForCombat"),
        (ContentSetupHook.ModifyShuffleOrder, "ModifyShuffleOrder"),
    ];

    /// <summary>
    /// AfterRoomEntered relics whose combat-room branch is a no-op or handled by RunCombatSetup.
    /// The list is the R2c-2 shop-room list plus the combat-room reset/routed relics; each one is
    /// explicitly reviewed there. ModifyShuffleOrder is intentionally unsupported until the kernel
    /// dispatches it (initial shuffle currently only rejects enchantments).
    /// </summary>
    private static readonly HashSet<string> SetupAfterRoomEnteredNoEffect =
        new(ShopAfterRoomEntered, StringComparer.Ordinal);

    /// <summary>
    /// Native (id, hook) pairs whose Core implementation deliberately uses a different method name.
    /// BloodVial's native hook is AfterPlayerTurnStartLate; the kernel's opening pass is the regular
    /// AfterPlayerTurnStart, so the implementation is registered under the native Late bit.
    /// </summary>
    private static readonly Dictionary<(string Id, ContentSetupHook Hook), string> SetupHookAliases =
        new()
        {
            [("BLOOD_VIAL", ContentSetupHook.AfterPlayerTurnStartLate)] = "AfterPlayerTurnStart",
            [("PAELS_EYE", ContentSetupHook.AfterSideTurnStart)] = "AfterPlayerTurnStart",
        };

    /// <summary>
    /// Native setup hooks that have no first-turn effect in single-player, or whose effect is
    /// implemented by a different kernel path. Each entry is the reason it is safe to admit; the
    /// corresponding model still gets a matrix case. This is the reviewed no-effect half of the
    /// per-(model, hook) list, not a blanket "has any override" shortcut.
    /// </summary>
    private static readonly Dictionary<(string Id, ContentSetupHook Hook), string> SetupHookNoEffect =
        new()
        {
            [("PLATING_POWER", ContentSetupHook.BeforeSideTurnStart)] = "Kernel.TurnPhases applies Plating",
            [("PLATING_POWER", ContentSetupHook.AfterSideTurnStart)] = "Kernel.TurnPhases applies Plating",
            [("SLOW_POWER", ContentSetupHook.AfterSideTurnStart)] = "Kernel damage calculation reads Slow",
            [("RAMPART_POWER", ContentSetupHook.AfterSideTurnStart)] = "no opening-turn branch",
            [("GALVANIC_POWER", ContentSetupHook.AfterCardEnteredCombat)] = "initial deck does not fire this hook",
            [("VITAL_SPARK_POWER", ContentSetupHook.AfterCardEnteredCombat)] = "initial deck does not fire this hook",
            [("KUSARIGAMA", ContentSetupHook.BeforeCombatStart)] = "counter is reset by RunCombatSetup",
            [("STONE_CALENDAR", ContentSetupHook.AfterSideTurnStart)] = "turn counter is not due on turn 1",
            [("RIPPLE_BASIN", ContentSetupHook.BeforeSideTurnStart)] = "block branch is not active on turn 1",
            [("VAMBRACE", ContentSetupHook.BeforeCombatStart)] = "counter is reset by RunCombatSetup",
            [("LETTER_OPENER", ContentSetupHook.BeforeCombatStart)] = "cards-played counter starts at zero",
            [("UNSETTLING_LAMP", ContentSetupHook.BeforeCombatStart)] = "room status is handled by RunCombatSetup",
            [("POCKETWATCH", ContentSetupHook.AfterSideTurnStart)] = "turn counter is not due on turn 1",
            [("PENDULUM", ContentSetupHook.BeforeHandDraw)] = "ModifyHandDraw owns the turn counter",
            [("POWER_CELL", ContentSetupHook.BeforeSideTurnStart)] = "no combat effect",
            [("LORDS_PARASOL", ContentSetupHook.AfterRoomEntered)] = "merchant-room only",
            [("PINPOINT", ContentSetupHook.AfterCardEnteredCombat)] = "initial deck does not fire this hook",
            [("FLATTEN", ContentSetupHook.AfterCardEnteredCombat)] = "initial deck does not fire this hook",
            [("STOMP", ContentSetupHook.AfterCardEnteredCombat)] = "initial deck does not fire this hook",
            [("BANSHEES_CRY", ContentSetupHook.AfterCardEnteredCombat)] = "initial deck does not fire this hook",
            [("HISTORY_COURSE", ContentSetupHook.AfterAutoPrePlayPhaseEntered)] = "kernel HistoryCourse replay handles the first turn",
        };

    private static ContentSetupHookMask[] ExportSetupHookMasks(int count,
        Func<ushort, Type?> nativeType, Func<ushort, Type> coreType, Type coreBase,
        Func<ushort, string> nativeId, (ContentSetupHook Hook, string Method)[] specs)
    {
        ContentSetupHookMask[] masks = new ContentSetupHookMask[count];
        for (ushort id = 0; id < count; id++)
        {
            Type? type = nativeType(id);
            masks[id] = type == null
                ? new ContentSetupHookMask { Overridden = AllSetupHooks, Implemented = 0 }
                : BuildSetupHookMask(type, coreType(id), coreBase, nativeId(id), specs);
        }
        return masks;
    }

    private static ContentSetupHookMask BuildSetupHookMask(Type nativeType, Type coreType,
        Type coreBase, string nativeId, (ContentSetupHook Hook, string Method)[] specs)
    {
        List<(ContentSetupHook Hook, bool Overridden, bool Implemented)> values = [];
        foreach ((ContentSetupHook hook, string method) in specs)
        {
            bool overridden = Overrides(nativeType, typeof(AbstractModel), method);
            string coreMethod = SetupHookAliases.GetValueOrDefault((nativeId, hook), method);
            bool noEffect = SetupHookNoEffect.ContainsKey((nativeId, hook));
            if (noEffect && !overridden)
                throw new InvalidDataException(
                    $"Setup Hook 无效果名单过期：{nativeId} 没有重写 {method}。");
            bool implemented = overridden
                && (CoreOverrides(coreType, coreBase, coreMethod) || noEffect
                    || (hook == ContentSetupHook.AfterRoomEntered
                        && SetupAfterRoomEnteredNoEffect.Contains(nativeId)));
            values.Add((hook, overridden, implemented));
        }
        return ContentSetupHookMask.From(values);
    }

    private static bool CoreOverrides(Type coreType, Type coreBase, string method)
    {
        MethodInfo? baseMethod = coreBase.GetMethod(method,
            BindingFlags.Instance | BindingFlags.Public);
        if (baseMethod == null)
            return false;
        MethodInfo? declared = coreType.GetMethod(method,
            BindingFlags.Instance | BindingFlags.Public);
        return declared != null && declared.DeclaringType != baseMethod.DeclaringType;
    }

    private static readonly uint AllSetupHooks =
        (uint)(ContentSetupHook.AfterRoomEntered | ContentSetupHook.BeforeCombatStart
            | ContentSetupHook.BeforeCombatStartLate | ContentSetupHook.BeforeSideTurnStart
            | ContentSetupHook.AfterEnergyReset | ContentSetupHook.AfterEnergyResetLate
            | ContentSetupHook.BeforeHandDraw | ContentSetupHook.BeforeHandDrawLate
            | ContentSetupHook.ModifyHandDraw | ContentSetupHook.ModifyHandDrawLate
            | ContentSetupHook.AfterPlayerTurnStartEarly | ContentSetupHook.AfterPlayerTurnStart
            | ContentSetupHook.AfterPlayerTurnStartLate | ContentSetupHook.AfterSideTurnStart
            | ContentSetupHook.AfterSideTurnStartLate | ContentSetupHook.ModifyMaxEnergy
            | ContentSetupHook.ModifyOrbValue | ContentSetupHook.ShouldProcurePotion
            | ContentSetupHook.AfterCardDrawnEarly | ContentSetupHook.AfterCardDrawn
            | ContentSetupHook.AfterCardEnteredCombat
            | ContentSetupHook.AfterCardGeneratedForCombat
            | ContentSetupHook.ModifyShuffleOrder
            | ContentSetupHook.AfterAutoPrePlayPhaseEntered
            | ContentSetupHook.AfterAutoPrePlayPhaseEnteredLate);

    private static ContentRewardHookMask[] ExportHookMasks(int count,
        Func<ushort, Type?> modelType, Func<ushort, string> nativeId)
    {
        ContentRewardHookMask[] masks = new ContentRewardHookMask[count];
        for (ushort id = 0; id < count; id++)
        {
            Type? type = modelType(id);
            masks[id] = type == null
                ? new ContentRewardHookMask { Overridden = AllRewardHooks, Implemented = 0 }
                : BuildRewardHookMask(type, nativeId(id));
        }
        return masks;
    }

    private static ContentRewardHookMask BuildRewardHookMask(Type modelType, string nativeId)
    {
        List<(ContentRewardHook Hook, bool Overridden, bool Implemented)> values = [];
        foreach ((ContentRewardHook hook, string method, HashSet<string> implemented) in HookSpecs)
        {
            bool overridden = Overrides(modelType, typeof(AbstractModel), method);
            bool listed = implemented.Contains(nativeId);
            if (listed && !overridden)
                throw new InvalidDataException(
                    $"奖励 Hook 名单过期：{nativeId} 没有重写 {method}。");
            values.Add((hook, overridden, listed));
        }
        return ContentRewardHookMask.From(values);
    }

    private static bool Overrides(Type modelType, Type baseType, string methodName)
    {
        MethodInfo? baseMethod = baseType.GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(baseType.FullName, methodName);
        MethodInfo? method = modelType.GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public);
        return method != null && method.DeclaringType != baseMethod.DeclaringType;
    }

    private static readonly uint AllRewardHooks =
        (uint)(ContentRewardHook.TryModifyRewards | ContentRewardHook.TryModifyRewardsLate
            | ContentRewardHook.AfterModifyingRewards
            | ContentRewardHook.BeforeCombatRewardOffered
            | ContentRewardHook.ModifyCardRewardCreationOptions
            | ContentRewardHook.ModifyCardRewardCreationOptionsLate
            | ContentRewardHook.TryModifyCardRewardOptions
            | ContentRewardHook.TryModifyCardRewardOptionsLate
            | ContentRewardHook.AfterModifyingCardRewardOptions
            | ContentRewardHook.ModifyCardRewardUpgradeOdds
            | ContentRewardHook.TryModifyCardRewardAlternatives
            | ContentRewardHook.ShouldAllowSelectingMoreCardRewards
            | ContentRewardHook.ShouldForcePotionReward
            | ContentRewardHook.ShouldProcurePotion
            | ContentRewardHook.AfterPotionProcured
            | ContentRewardHook.AfterPotionDiscarded
            | ContentRewardHook.AfterRewardTaken
            | ContentRewardHook.ModifyGoldGained
            | ContentRewardHook.AfterModifyingGoldGained
            | ContentRewardHook.AfterGoldGained
            | ContentRewardHook.ShouldAddToDeck
            | ContentRewardHook.TryModifyCardBeingAddedToDeck
            | ContentRewardHook.TryModifyCardBeingAddedToDeckLate
            | ContentRewardHook.AfterCardChangedPiles
            | ContentRewardHook.AfterCardChangedPilesLate
            | ContentRewardHook.AfterCurrentHpChanged
            | ContentRewardHook.ShouldProceedToNextMapPoint);

    private static readonly HashSet<string> NoHooks = new(StringComparer.Ordinal);

    private static readonly (ContentRewardHook Hook, string Method, HashSet<string> Implemented)[]
        HookSpecs =
        [
            (ContentRewardHook.TryModifyRewards, "TryModifyRewards", Set(
                "AMETHYST_AUBERGINE", "BLACK_STAR", "LAVA_ROCK", "PRAYER_WHEEL", "WHITE_STAR",
                "WONGOS_MYSTERY_TICKET")),
            (ContentRewardHook.TryModifyRewardsLate, "TryModifyRewardsLate", Set("DRIFTWOOD")),
            (ContentRewardHook.AfterModifyingRewards, "AfterModifyingRewards", Set(
                "AMETHYST_AUBERGINE", "WONGOS_MYSTERY_TICKET")),
            (ContentRewardHook.BeforeCombatRewardOffered, "BeforeCombatRewardOffered",
                Set("LASTING_CANDY")),
            (ContentRewardHook.ModifyCardRewardCreationOptions,
                "ModifyCardRewardCreationOptions", Set("DINGY_RUG", "PRISMATIC_GEM")),
            (ContentRewardHook.ModifyCardRewardCreationOptionsLate,
                "ModifyCardRewardCreationOptionsLate", NoHooks),
            (ContentRewardHook.TryModifyCardRewardOptions, "TryModifyCardRewardOptions",
                Set("LASTING_CANDY")),
            (ContentRewardHook.TryModifyCardRewardOptionsLate, "TryModifyCardRewardOptionsLate",
                Set("FROZEN_EGG", "MOLTEN_EGG", "TOXIC_EGG", "LAVA_LAMP")),
            (ContentRewardHook.AfterModifyingCardRewardOptions,
                "AfterModifyingCardRewardOptions", NoHooks),
            (ContentRewardHook.ModifyCardRewardUpgradeOdds, "ModifyCardRewardUpgradeOdds",
                NoHooks),
            (ContentRewardHook.TryModifyCardRewardAlternatives,
                "TryModifyCardRewardAlternatives", NoHooks),
            (ContentRewardHook.ShouldAllowSelectingMoreCardRewards,
                "ShouldAllowSelectingMoreCardRewards", NoHooks),
            (ContentRewardHook.ShouldForcePotionReward, "ShouldForcePotionReward",
                Set("WHITE_BEAST_STATUE")),
            (ContentRewardHook.ShouldProcurePotion, "ShouldProcurePotion", Set("SOZU")),
            (ContentRewardHook.AfterPotionProcured, "AfterPotionProcured", Set("BELT_BUCKLE")),
            (ContentRewardHook.AfterPotionDiscarded, "AfterPotionDiscarded", Set("BELT_BUCKLE")),
            (ContentRewardHook.AfterRewardTaken, "AfterRewardTaken", NoHooks),
            (ContentRewardHook.ModifyGoldGained, "ModifyGoldGained",
                Set("BOWLER_HAT", "ECTOPLASM")),
            (ContentRewardHook.AfterModifyingGoldGained, "AfterModifyingGoldGained",
                Set("BOWLER_HAT", "ECTOPLASM")),
            (ContentRewardHook.AfterGoldGained, "AfterGoldGained", Set("DRAGON_FRUIT")),
            (ContentRewardHook.ShouldAddToDeck, "ShouldAddToDeck", NoHooks),
            (ContentRewardHook.TryModifyCardBeingAddedToDeck, "TryModifyCardBeingAddedToDeck",
                Set("FROZEN_EGG", "MOLTEN_EGG", "TOXIC_EGG")),
            (ContentRewardHook.TryModifyCardBeingAddedToDeckLate,
                "TryModifyCardBeingAddedToDeckLate", NoHooks),
            (ContentRewardHook.AfterCardChangedPiles, "AfterCardChangedPiles",
                Set("BOOK_OF_FIVE_RINGS", "DARKSTONE_PERIAPT", "LUCKY_FYSH",
                    "SOVEREIGN_BLADE")),
            (ContentRewardHook.AfterCardChangedPilesLate, "AfterCardChangedPilesLate", NoHooks),
            (ContentRewardHook.AfterCurrentHpChanged, "AfterCurrentHpChanged",
                Set("MEAT_ON_THE_BONE", "RED_SKULL")),
            (ContentRewardHook.ShouldProceedToNextMapPoint, "ShouldProceedToNextMapPoint",
                NoHooks),
        ];

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);

    private static readonly Dictionary<string, EnchantmentModel> CanonicalEnchantmentModels =
        ModelDb.All.OfType<EnchantmentModel>()
            .GroupBy(enchantment => enchantment.Id.Entry, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static ushort RequireCard(CardModel card)
        => Registry.TryCard(card.Id.Entry, out ushort id)
            ? id
            : throw new InvalidDataException($"教程奖励卡 {card.Id.Entry} 没有 Blitz 定义。");

    private static ushort RequirePotion(PotionModel potion)
        => Registry.TryPotion(potion.Id.Entry, out ushort id)
            ? id
            : throw new InvalidDataException($"教程奖励药水 {potion.Id.Entry} 没有 Blitz 定义。");

    private static ushort RequireRelic(RelicModel relic)
        => Registry.TryRelic(relic.Id.Entry, out ushort id)
            ? id
            : throw new InvalidDataException($"教程奖励遗物 {relic.Id.Entry} 没有 Blitz 定义。");

}
