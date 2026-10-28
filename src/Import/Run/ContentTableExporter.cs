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

    public static ContentTable Export()
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

            PowerVars = ExportPowerVars(out ContentUnsupported?[] powerUnsupported),
            PowerUnsupported = powerUnsupported,
            PowerDeathHistoryRisk = ExportPowerDeathRisk(),
            Potions = ExportPotions(out ContentUnsupported?[] potionUnsupported),
            PotionUnsupported = potionUnsupported,
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

            BaseOdds = ExportOdds(),
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
                CanonicalEnchantmentModels.TryGetValue(
                    Registry.Enchantment(id).NativeId,
                    out EnchantmentModel? model) ? model.GetType() : null,
                static id => Registry.Enchantment(id).GetType(), typeof(EnchantmentDef),
                static id => Registry.Enchantment(id).NativeId, EnchantmentSetupHookSpecs),
            UnknownPointHookRelics = ExportUnknownPointHookRelics(),
            UnknownPointHookCards = ExportUnknownPointHookCards(),
            NextEventHookCards = ExportNextEventHookCards(),
            EventAllowedOverride = ExportEventAllowedOverrides(),
        };
    }

    private static ContentOdds ExportOdds()
    {
        RunOddsSet odds = new(new MegaCrit.Sts2.Core.Random.Rng(0UL));
        return new ContentOdds
        {
            Monster = odds.UnknownMapPoint.MonsterOdds,
            Elite = odds.UnknownMapPoint.EliteOdds,
            Treasure = odds.UnknownMapPoint.TreasureOdds,
            Shop = odds.UnknownMapPoint.ShopOdds,
        };
    }

    private static ushort[] ExportUnknownPointHookRelics()
        => [.. ModelDb.All.OfType<RelicModel>()
            .Where(model => OverridesHook(model, "ModifyUnknownMapPointRoomTypes",
                "ModifyOddsIncreaseForUnrolledRoomType"))
            .Select(RequireRelic)
            .Distinct()];

    private static ushort[] ExportUnknownPointHookCards()
        => [.. ModelDb.All.OfType<CardModel>()
            .Where(model => OverridesHook(model, "ModifyUnknownMapPointRoomTypes",
                "ModifyOddsIncreaseForUnrolledRoomType"))
            .Select(RequireCard)
            .Distinct()];

    private static ushort[] ExportNextEventHookCards()
        => [.. ModelDb.All.OfType<CardModel>()
            .Where(model => OverridesHook(model, "ModifyNextEvent"))
            .Select(RequireCard)
            .Distinct()];

    private static bool[] ExportEventAllowedOverrides()
    {
        bool[] overrides = new bool[RunCatalog.EventCount];
        for (int eventId = 0; eventId < RunCatalog.EventCount; eventId++)
        {
            string name = RunCatalog.EventName((ushort)eventId);
            EventModel? model = ModelDb.All.OfType<EventModel>()
                .FirstOrDefault(candidate => candidate.Id.Entry == name);
            if (model == null)
                throw new InvalidDataException($"原版没有事件 {name}。");
            overrides[eventId] = OverridesHook(model, "IsAllowed");
        }
        return overrides;
    }

    private static bool OverridesHook(AbstractModel model, params string[] names)
    {
        foreach (string name in names)
        {
            MethodInfo? method = model.GetType().GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public);
            if (method?.DeclaringType is { } declaring && declaring != typeof(AbstractModel)
                && declaring != typeof(RelicModel) && declaring != typeof(CardModel)
                && declaring != typeof(EventModel))
                return true;
        }
        return false;
    }

    private static IReadOnlyDictionary<string, decimal>?[] ExportRelicStartVars()
    {
        IReadOnlyDictionary<string, decimal>?[] vars =
            new IReadOnlyDictionary<string, decimal>?[Registry.RelicCount];
        for (ushort id = 0; id < Registry.RelicCount; id++)
        {
            RelicDef def = Registry.Relic(id);
            if (!RulesCompiler.CanonicalRelicModels.TryGetValue(def.NativeId,
                out RelicModel? canonical))
            {
                continue;
            }
            Dictionary<string, decimal> map = new(StringComparer.Ordinal);
            foreach ((string name, DynamicVar variable) in canonical.DynamicVars)
                map[name] = variable.BaseValue;
            // These two values are method-body literals in the native models, not DynamicVars.
            if (def.NativeId is "SNECKO_EYE" or "FAKE_SNECKO_EYE")
                map["ConfusedPower"] = 1m;
            if (def.NativeId == "FUR_COAT")
                map["SpawnHp"] = 1m;
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
            result[id] = new ContentCharacter
            {
                Id = id,
                NativeId = character.Id.Entry,
                UnlockEpochs = CharacterUnlockMask(character),
                CardPool = ExportCardPool(character.CardPool),
                PotionPool = ExportPotionPool(character.PotionPool,
                    PotionUnlockMask(character.PotionPool)),
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
        new(StringComparer.Ordinal)
        {
            "BIG_MUSHROOM", "BRONZE_SCALES", "BURNING_STICKS", "DATA_DISK", "DIVINE_RIGHT",
            "EMBER_TEA", "ETERNAL_FEATHER", "FAKE_VENERABLE_TEA_SET", "GHOST_SEED", "GIRYA",
            "GORGET", "LAVA_LAMP", "MAW_BANK", "MEAL_TICKET", "METRONOME",
            "ODDLY_SMOOTH_STONE", "PANTOGRAPH", "PERMAFROST", "PHILOSOPHERS_STONE",
            "PLANISPHERE", "RED_SKULL", "REGAL_PILLOW", "SILVER_CRUCIBLE", "SLING_OF_COURAGE",
            "STONE_CALENDAR", "STONE_CRACKER", "SWORD_OF_JADE", "THROWING_AXE", "VAJRA",
            "VELVET_CHOKER", "VENERABLE_TEA_SET", "WINGED_BOOTS",
        };

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

    private static bool Overrides(Type modelType, Type baseType, string methodName)
    {
        MethodInfo? baseMethod = baseType.GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(baseType.FullName, methodName);
        MethodInfo? method = modelType.GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public);
        return method != null && method.DeclaringType != baseMethod.DeclaringType;
    }

    private static readonly Dictionary<string, EnchantmentModel> CanonicalEnchantmentModels =
        ModelDb.All.OfType<EnchantmentModel>()
            .GroupBy(enchantment => enchantment.Id.Entry, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static ushort RequireCard(CardModel card)
        => Registry.TryCard(card.Id.Entry, out ushort id)
            ? id
            : throw new InvalidDataException($"内容表没有卡牌 {card.Id.Entry}。");

    private static ushort RequireRelic(RelicModel relic)
        => Registry.TryRelic(relic.Id.Entry, out ushort id)
            ? id
            : throw new InvalidDataException($"内容表没有遗物 {relic.Id.Entry}。");

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
}
