// Exports the process-local content table from the canonical native models. Every value is read
// from the game assembly loaded by this process; nothing is written to disk and there is no version
// check. The per-id card cloning, power/relic/potion variable reads and monster state-machine
// compilation reuse the helpers of RulesCompiler so the native compiler and the run-layer builder
// share one content interpretation.
using System.Reflection;
using System.Runtime.CompilerServices;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Unlocks;

using NativeCreature = MegaCrit.Sts2.Core.Entities.Creatures.Creature;
using EngineCardRarity = BlitzSolver.Simulation.Combat.CardRarity;
using EngineCardType = BlitzSolver.Simulation.Combat.CardType;

namespace BlitzSolver.Import;

/// <summary>Builds the one process-local <see cref="ContentTable"/> from <c>ModelDb</c>.</summary>
public static class ContentTableExporter
{
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
            PowerVars = ExportPowerVars(out ContentUnsupported?[] powerUnsupported),
            PowerUnsupported = powerUnsupported,
            PowerDeathHistoryRisk = ExportPowerDeathRisk(),
            Potions = ExportPotions(out ContentUnsupported?[] potionUnsupported),
            PotionUnsupported = potionUnsupported,
            PotionGeneratableInCombat = ExportPotionGeneration(),
            Enchantments = ExportEnchantments(out ContentUnsupported?[] enchantmentUnsupported),
            EnchantmentUnsupported = enchantmentUnsupported,
            RelicVars = ExportRelicVars(out ContentUnsupported?[] relicUnsupported),
            RelicUnsupported = relicUnsupported,
            SpawnMonsters = ExportSpawnMonsters(ascension,
                out ContentUnsupported?[] monsterUnsupported),
            MonsterUnsupported = monsterUnsupported,
            Encounters = ExportEncounters(ascension),
            Characters = ExportCharacters(out ushort[] characterOrder),
            CharacterOrder = characterOrder,
            ColorlessPool = ExportCardPool(ModelDb.CardPool<ColorlessCardPool>()),
            StatusPool = ExportCardPool(ModelDb.CardPool<StatusCardPool>()),
            CursePool = ExportCardPool(ModelDb.CardPool<CurseCardPool>()),
            SharedPotionPool = ExportPotionPool(ModelDb.PotionPool<SharedPotionPool>(), 0),
        };
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
            int upgrade = 0;
            try
            {
                for (; upgrade <= maxUpgrade; upgrade++)
                    levels[upgrade] = RulesCompiler.CanonicalStats(canonical, upgrade, scratch);
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
            cards[id] = new ContentCard { Levels = levels };
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
                encounters[id] = new ContentEncounter
                {
                    NativeId = nativeId,
                    Slots = [],
                    Monsters = [],
                    UnsupportedReason = ContentReason.UnsupportedEncounter,
                    UnsupportedDetail = $"{nativeId} 没有规范模型",
                };
                continue;
            }
            try
            {
                EncounterModel mutable = encounter.ToMutable();
                mutable.GenerateMonstersWithSlots(NullRunState.Instance);
                List<ContentEncounterMonster> monsters = [];
                string? unsupportedReason = null;
                string? unsupportedDetail = null;
                foreach ((MonsterModel monster, string? slot) in mutable.MonstersWithSlots)
                {
                    if (!Registry.TryMonster(monster.Id.Entry, out ushort monsterId))
                    {
                        unsupportedReason = ContentReason.UnsupportedMonster;
                        unsupportedDetail = monster.Id.Entry;
                        break;
                    }
                    MonsterRules[] byAscension = new MonsterRules[ContentTable.AscensionCount];
                    for (int level = 0; level < byAscension.Length; level++)
                    {
                        ascension.Set(level);
                        MonsterModel variant = (MonsterModel)monster.ClonePreservingMutability();
                        NativeCreature creature = RulesCompiler.CreateScratchMonster(
                            variant, slot, NullRunState.Instance);
                        byAscension[level] = RulesCompiler.CompileMonster(variant, creature,
                            rootPowerAmountsFromCreature: false);
                    }
                    monsters.Add(new ContentEncounterMonster
                    {
                        MonsterId = monsterId,
                        Slot = slot,
                        RulesByAscension = byAscension,
                    });
                }
                encounters[id] = new ContentEncounter
                {
                    NativeId = nativeId,
                    Slots = [.. mutable.Slots],
                    Monsters = [.. monsters],
                    UnsupportedReason = unsupportedReason,
                    UnsupportedDetail = unsupportedDetail,
                };
            }
            catch (UnsupportedImportException error)
            {
                encounters[id] = new ContentEncounter
                {
                    NativeId = nativeId,
                    Slots = [],
                    Monsters = [],
                    UnsupportedReason = error.Reason,
                    UnsupportedDetail = error.Detail,
                };
            }
        }
        return encounters;
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
    private sealed class ScratchAscension : IDisposable
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
