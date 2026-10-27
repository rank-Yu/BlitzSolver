// Exports the static content new-run generation needs: starting inventories, relic pools with
// their unlock masks, per-act event/encounter/ancient pools and tags, the map parameters, the two
// map-point-count draws of every act (inferred from the native method), and the tutorial
// discovery-order swaps (inferred from the native method). Everything is read from the canonical
// models of the game assembly loaded by this process.
using System.Reflection;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Unlocks;

namespace BlitzSolver.Import;

/// <summary>Builds the new-run half of <see cref="ContentTable"/> from the loaded game models.</summary>
internal static class NewRunContentExporter
{
    private const int ProbeCount = 48;

    // StandardActMap method-body literals that no model exposes. They are universal map algorithm
    // constants, not per-act content; the act-specific counts are inferred from the native method.
    private const int TreasureRowFromEnd = 7;      // StandardActMap.AssignPointTypes: GetRowCount() - 7
    private const int LowerRestrictionRow = 6;     // StandardActMap.IsValidForLower: coord.row < 6
    private const int UpperRestrictionFromEnd = 3; // StandardActMap.IsValidForUpper: row >= length - 3

    public static ContentOdds ExportOdds()
    {
        RunOddsSet odds = new(new MegaCrit.Sts2.Core.Random.Rng(0UL));
        MegaCrit.Sts2.Core.Odds.CardRarityOdds card =
            new(new MegaCrit.Sts2.Core.Random.Rng(0UL));
        MegaCrit.Sts2.Core.Odds.PotionRewardOdds potion =
            new(new MegaCrit.Sts2.Core.Random.Rng(0UL));
        return new ContentOdds
        {
            Monster = odds.UnknownMapPoint.MonsterOdds,
            Elite = odds.UnknownMapPoint.EliteOdds,
            Treasure = odds.UnknownMapPoint.TreasureOdds,
            Shop = odds.UnknownMapPoint.ShopOdds,
            CardRarity = card.CurrentValue,
            PotionReward = potion.CurrentValue,
        };
    }

    public static ContentStartingInventory ExportStarting(CharacterModel character)
    {
        List<ushort> deck = [];
        foreach (CardModel card in character.StartingDeck)
            deck.Add(RequireCard(card));
        List<ContentStartingRelic> relics = [];
        foreach (RelicModel relic in character.StartingRelics)
        {
            RelicModel mutable = relic.ToMutable();
            relics.Add(new ContentStartingRelic
            {
                Id = RequireRelic(relic),
                NativeId = relic.Id.Entry,
                DisplayAmount = mutable.DisplayAmount,
            });
        }
        List<ushort> potions = [];
        foreach (PotionModel potion in character.StartingPotions)
        {
            if (!Registry.TryPotion(potion.Id.Entry, out ushort id))
                throw new InvalidDataException($"原版起始药水 {potion.Id.Entry} 没有 Blitz 定义。");
            potions.Add(id);
        }
        return new ContentStartingInventory
        {
            Deck = [.. deck],
            Relics = [.. relics],
            Potions = [.. potions],
            Hp = character.StartingHp,
            Gold = character.StartingGold,
            MaxEnergy = character.MaxEnergy,
            OrbSlots = character.BaseOrbSlotCount,
            PotionSlots = 3, // Player.CreateForNewRun passes the literal 3 potion slots
        };
    }

    /// <summary>Shared pool order + per-relic unlock mask; the source of both grab bags.</summary>
    public static ContentRelicPoolEntry[] ExportSharedRelicPool()
        => ExportRelicPool(ModelDb.RelicPool<SharedRelicPool>());

    public static ContentRelicPoolEntry[] ExportCharacterRelicPool(CharacterModel character)
        => ExportRelicPool(character.RelicPool);

    private static ContentRelicPoolEntry[] ExportRelicPool(RelicPoolModel pool)
    {
        List<RelicModel> all = [.. pool.AllRelics];
        ulong[] masks = new ulong[all.Count];
        foreach (string epoch in EpochModel.AllEpochIds)
        {
            if (!RunCatalog.TryEpoch(epoch, out ushort id))
                continue;
            HashSet<ModelId> unlocked = [.. pool.GetUnlockedRelics(WithoutEpoch(epoch))
                .Select(relic => relic.Id)];
            for (int index = 0; index < all.Count; index++)
            {
                if (!unlocked.Contains(all[index].Id))
                    masks[index] |= 1UL << id;
            }
        }
        ContentRelicPoolEntry[] entries = new ContentRelicPoolEntry[all.Count];
        for (int index = 0; index < all.Count; index++)
        {
            RelicModel relic = all[index];
            if (!Enum.IsDefined(relic.Rarity))
                throw new InvalidDataException($"原版遗物 {relic.Id.Entry} 稀有度 {(int)relic.Rarity} 非法。");
            entries[index] = new ContentRelicPoolEntry
            {
                RelicId = RequireRelic(relic),
                NativeId = relic.Id.Entry,
                Rarity = (RunRelicRarity)(byte)relic.Rarity,
                RequiredEpochs = masks[index],
            };
        }
        return entries;
    }

    public static ushort[] ExportSharedEvents(out ulong[] requiredEpochs)
    {
        List<ushort> events = [];
        foreach (EventModel model in ModelDb.AllSharedEvents)
        {
            if (!RunCatalog.TryEvent(model.Id.Entry, out ushort id))
                throw new InvalidDataException($"原版共享事件 {model.Id.Entry} 不在目录内。");
            events.Add(id);
        }
        requiredEpochs = ExportEventEpochMasks();
        return [.. events];
    }

    /// <summary>
    /// The event filters in <c>ActModel.GenerateRooms</c> are the static <c>Events</c> lists of the
    /// Event1/2/3 epochs; a bit is set for every epoch whose list contains the event.
    /// </summary>
    private static ulong[] ExportEventEpochMasks()
    {
        ulong[] masks = new ulong[RunCatalog.EventCount];
        foreach (Type type in EpochModel.AllEpochs)
        {
            PropertyInfo? property = type.GetProperty("Events",
                BindingFlags.Public | BindingFlags.Static);
            if (property?.GetValue(null) is not System.Collections.IEnumerable models)
                continue;
            string? epochId = EpochModel.AllEpochIds.FirstOrDefault(
                id => EpochModel.Get(id)?.GetType() == type);
            if (epochId is null || !RunCatalog.TryEpoch(epochId, out ushort epoch))
                continue;
            foreach (object? model in models)
            {
                if (model is not EventModel eventModel)
                    continue;
                if (!RunCatalog.TryEvent(eventModel.Id.Entry, out ushort eventId))
                    continue; // deprecated events are not part of the vanilla pools
                masks[eventId] |= 1UL << epoch;
            }
        }
        return masks;
    }

    public static ContentAncientEntry[] ExportSharedAncients()
    {
        List<ContentAncientEntry> ancients = [];
        foreach (AncientEventModel model in ModelDb.AllSharedAncients)
            ancients.Add(ExportAncient(model));
        return [.. ancients];
    }

    private static ContentAncientEntry ExportAncient(AncientEventModel model)
    {
        if (!RunCatalog.TryEvent(model.Id.Entry, out ushort id))
            throw new InvalidDataException($"原版先古之民 {model.Id.Entry} 不在目录内。");
        ulong mask = 0;
        foreach (string epoch in EpochModel.AllEpochIds)
        {
            if (!RunCatalog.TryEpoch(epoch, out ushort epochId))
                continue;
            UnlockState without = WithoutEpoch(epoch);
            if (!without.SharedAncients.Any(candidate => candidate.Id == model.Id))
                mask |= 1UL << epochId;
        }
        return new ContentAncientEntry
        {
            EventId = id,
            NativeId = model.Id.Entry,
            RequiredEpochs = mask,
        };
    }

    public static ContentAct?[] ExportActs(out ushort[] defaultActs)
    {
        ContentAct?[] acts = new ContentAct?[RunCatalog.ActCount];
        Dictionary<string, ActModel> canonical = ModelDb.All.OfType<ActModel>()
            .GroupBy(act => act.Id.Entry, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        for (ushort id = 0; id < RunCatalog.ActCount; id++)
        {
            string nativeId = RunCatalog.ActName(id);
            if (!canonical.TryGetValue(nativeId, out ActModel? act))
                continue;
            acts[id] = ExportAct(act);
        }
        defaultActs = [.. ActModel.GetDefaultList().Select(act => RequireAct(act))];
        return acts;
    }

    private static ContentAct ExportAct(ActModel act)
    {
        PropertyInfo baseRooms = typeof(ActModel).GetProperty("BaseNumberOfRooms",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(ActModel).FullName, "BaseNumberOfRooms");
        PropertyInfo weakEncounters = typeof(ActModel).GetProperty("NumberOfWeakEncounters",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(ActModel).FullName, "NumberOfWeakEncounters");
        List<ushort> events = [];
        foreach (EventModel model in act.AllEvents)
            events.Add(RequireEvent(model));
        List<ushort> encounters = [];
        foreach (EncounterModel model in act.AllEncounters)
            encounters.Add(RequireEncounter(model));
        List<ContentAncientEntry> ancients = [];
        foreach (AncientEventModel model in act.AllAncients)
        {
            if (!RunCatalog.TryEvent(model.Id.Entry, out ushort id))
                throw new InvalidDataException($"原版先古之民 {model.Id.Entry} 不在目录内。");
            ulong mask = 0;
            foreach (string epoch in EpochModel.AllEpochIds)
            {
                if (!RunCatalog.TryEpoch(epoch, out ushort epochId))
                    continue;
                UnlockState without = WithoutEpoch(epoch);
                if (!act.GetUnlockedAncients(without).Any(candidate => candidate.Id == model.Id))
                    mask |= 1UL << epochId;
            }
            ancients.Add(new ContentAncientEntry
            {
                EventId = id,
                NativeId = model.Id.Entry,
                RequiredEpochs = mask,
            });
        }
        List<ushort> bossOrder = [];
        foreach (EncounterModel model in act.BossDiscoveryOrder)
            bossOrder.Add(RequireEncounter(model));
        (ContentCountDraw rest, ContentCountDraw unknown) = InferMapCountDraws(act);
        (ContentRoomSwap[] swaps, bool firstRunOnly) = InferTutorialSwaps(act);
        return new ContentAct
        {
            Id = RequireAct(act),
            NativeId = act.Id.Entry,
            Index = act.Index,
            IsDefault = act.IsDefault,
            UnlockEpochs = ActUnlockMask(act),
            BaseRooms = (int)(baseRooms.GetValue(act)
                ?? throw new InvalidOperationException($"原版 {act.Id.Entry}.BaseNumberOfRooms 为空。")),
            WeakEncounters = (int)(weakEncounters.GetValue(act)
                ?? throw new InvalidOperationException($"原版 {act.Id.Entry}.NumberOfWeakEncounters 为空。")),
            Events = [.. events],
            Encounters = [.. encounters],
            Ancients = [.. ancients],
            BossDiscoveryOrder = [.. bossOrder],
            RestDraw = rest,
            UnknownDraw = unknown,
            TutorialSwaps = swaps,
            TutorialSwapsFirstRunOnly = firstRunOnly,
        };
    }

    private static ulong ActUnlockMask(ActModel act)
    {
        if (!act.IsUnlocked(UnlockState.all))
            throw new InvalidDataException($"原版幕 {act.Id.Entry} 在全部解锁下仍不可用。");
        ulong mask = 0;
        foreach (string epoch in EpochModel.AllEpochIds)
        {
            if (!RunCatalog.TryEpoch(epoch, out ushort id))
                continue;
            if (!act.IsUnlocked(WithoutEpoch(epoch)))
                mask |= 1UL << id;
        }
        return mask;
    }

    public static ContentMapRules ExportMapRules(ContentTableExporter.ScratchAscension ascension)
    {
        Type type = typeof(StandardActMap);
        int width = (int)(type.GetField("_mapWidth",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetRawConstantValue()
            ?? throw new MissingFieldException(type.FullName, "_mapWidth"));
        int startPaths = (int)(type.GetField("_iterations",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetRawConstantValue()
            ?? throw new MissingFieldException(type.FullName, "_iterations"));
        int maxElites = (int)(type.GetField("maxElites",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetRawConstantValue()
            ?? throw new MissingFieldException(type.FullName, "maxElites"));
        int[] elites = new int[ContentTable.AscensionCount];
        for (int level = 0; level < elites.Length; level++)
        {
            ascension.Set(level);
            MapPointTypeCounts counts = new(0, 0);
            elites[level] = counts.NumOfElites;
        }
        MapPointTypeCounts shopCounts = new(0, 0);
        return new ContentMapRules
        {
            Width = width,
            StartPaths = startPaths,
            MaxElites = maxElites,
            TreasureRowFromEnd = TreasureRowFromEnd,
            LowerRestrictionRow = LowerRestrictionRow,
            UpperRestrictionFromEnd = UpperRestrictionFromEnd,
            TypeAssignIterations = 3,
            PruneRepairIterations = 3,
            PruneIterationLimit = 50,
            ParentRestrictions = PointTypeMask("_parentMapPointRestrictions"),
            ChildRestrictions = PointTypeMask("_childMapPointRestrictions"),
            SiblingRestrictions = PointTypeMask("_siblingPointTypeRestrictions"),
            LowerRestrictions = PointTypeMask("_lowerMapPointRestrictions"),
            UpperRestrictions = PointTypeMask("_upperMapPointRestrictions"),
            ElitesByAscension = elites,
            Shops = shopCounts.NumOfShops,
        };
    }

    private static uint PointTypeMask(string field)
    {
        FieldInfo info = typeof(StandardActMap).GetField(field,
                BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(StandardActMap).FullName, field);
        if (info.GetValue(null) is not IEnumerable<MapPointType> types)
            throw new InvalidDataException($"原版 StandardActMap.{field} 不是 MapPointType 集合。");
        uint mask = 0;
        foreach (MapPointType pointType in types)
            mask |= 1U << (int)pointType;
        return mask;
    }

    /// <summary>
    /// Models that override any map-generation hook. R2b-1 does not implement the variant maps
    /// (FurCoat quests, GoldenPath, Spoils), so run generation rejects a state that holds them
    /// instead of producing a different map.
    /// </summary>
    public static ushort[] ExportMapHookRelics()
        => [.. ModelDb.All.OfType<RelicModel>()
            .Where(model => OverridesHook(model, "ModifyGeneratedMap", "ModifyGeneratedMapLate",
                "AfterMapGenerated"))
            .Select(RequireRelic)
            .Distinct()];

    public static ushort[] ExportMapHookCards()
        => [.. ModelDb.All.OfType<CardModel>()
            .Where(model => OverridesHook(model, "ModifyGeneratedMap", "ModifyGeneratedMapLate",
                "AfterMapGenerated"))
            .Select(RequireCard)
            .Distinct()];

    /// <summary>Relics that change the map travel rule (<c>WingedBoots</c>).</summary>
    public static ushort[] ExportFreeTravelRelics()
        => [.. ModelDb.All.OfType<RelicModel>()
            .Where(model => OverridesHook(model, "ShouldAllowFreeTravel"))
            .Select(RequireRelic)
            .Distinct()];

    /// <summary>
    /// Relics that change the unknown-map-point room set or its odds growth (GoldenCompass,
    /// JuzuBracelet). R2b-2 rejects an unknown roll while one is held.
    /// </summary>
    public static ushort[] ExportUnknownPointHookRelics()
        => [.. ModelDb.All.OfType<RelicModel>()
            .Where(model => OverridesHook(model, "ModifyUnknownMapPointRoomTypes",
                "ModifyOddsIncreaseForUnrolledRoomType"))
            .Select(RequireRelic)
            .Distinct()];

    public static ushort[] ExportUnknownPointHookCards()
        => [.. ModelDb.All.OfType<CardModel>()
            .Where(model => OverridesHook(model, "ModifyUnknownMapPointRoomTypes",
                "ModifyOddsIncreaseForUnrolledRoomType"))
            .Select(RequireCard)
            .Distinct()];

    /// <summary>Cards that replace the next pulled event (<c>LanternKey</c>).</summary>
    public static ushort[] ExportNextEventHookCards()
        => [.. ModelDb.All.OfType<CardModel>()
            .Where(model => OverridesHook(model, "ModifyNextEvent"))
            .Select(RequireCard)
            .Distinct()];

    /// <summary>
    /// Events whose <c>IsAllowed</c> depends on run state. R2b-2 only pulls an event when this
    /// check is the native base implementation; an overridden check is an explicit rejection until
    /// the event phase (R2e) owns that rule.
    /// </summary>
    public static bool[] ExportEventAllowedOverrides()
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

    public static ushort ExportNeowEpoch()
    {
        string epoch = EpochModel.GetId<MegaCrit.Sts2.Core.Timeline.Epochs.NeowEpoch>();
        if (!RunCatalog.TryEpoch(epoch, out ushort id))
            throw new InvalidDataException($"Neow epoch {epoch} 不在目录内。");
        return id;
    }

    public static ContentAscensionRules ExportAscensionRules()
    {
        if (!Registry.TryCard(ModelDb.Card<AscendersBane>().Id.Entry, out ushort bane))
            throw new InvalidDataException("原版 AscendersBane 没有 Blitz 定义。");
        return new ContentAscensionRules
        {
            MaxLevel = AscensionManager.maxAscensionAllowed,
            SwarmingElitesLevel = (int)AscensionLevel.SwarmingElites,
            TightBeltLevel = (int)AscensionLevel.TightBelt,
            AscendersBaneLevel = (int)AscensionLevel.AscendersBane,
            AscendersBaneCardId = bane,
            DoubleBossLevel = (int)AscensionLevel.DoubleBoss,
        };
    }

    // ---------------------------------------------------------------- map point count draws

    /// <summary>
    /// Infers the two draws of <c>ActModel.GetMapPointTypes</c> by probing the native method with
    /// controlled <c>Rng</c> states and replaying candidate uniform/Gaussian draws. The probe set
    /// includes states whose first normal deviate is far from the mean, so the integer acceptance
    /// bounds of the native rejection loop are observable. The unique candidate pair is exported;
    /// an ambiguous result fails the export instead of guessing.
    /// </summary>
    private static (ContentCountDraw RestDraw, ContentCountDraw UnknownDraw) InferMapCountDraws(
        ActModel act)
    {
        List<CountProbe> probes = CaptureCountProbes(act);
        List<ContentCountDraw> candidates = BuildCountCandidates();
        (ContentCountDraw First, ContentCountDraw Second)? natural = FitPair(probes, candidates);
        (ContentCountDraw First, ContentCountDraw Second)? swapped =
            FitPair(probes, candidates, swapValues: true);
        if (natural is { } found)
        {
            if (swapped is not null)
            {
                throw new InvalidDataException(
                    $"原版 {act.Id.Entry}.GetMapPointTypes 的两次抽取无法区分先后："
                    + $"[{Describe(found.First)}|{Describe(found.Second)}]。");
            }
            return (found.First, found.Second);
        }
        if (swapped is { } alternate)
            return (alternate.Second, alternate.First);
        throw new InvalidDataException(
            $"无法从原版 {act.Id.Entry}.GetMapPointTypes 推断两次抽取的参数。");
    }

    private static string Describe(in ContentCountDraw draw)
        => $"{draw.Kind},mean={draw.Mean},std={draw.StdDev},min={draw.Min},max={draw.Max}";

    private sealed record CountProbe(int Rest, int Unknown, int Delta, double[] Doubles);

    /// <summary>
    /// Sixteen far-tail bins plus random states. The far-tail probes are what make the native
    /// rejection bounds observable: a draw whose first attempt succeeds never tests them.
    /// </summary>
    private static List<CountProbe> CaptureCountProbes(ActModel act)
    {
        List<CountProbe> probes = [];
        int[] binned = new int[FarTailBins];
        for (int index = 0; index < RandomProbeCount; index++)
            probes.Add(CaptureCountProbe(act, Splitmix((ulong)(index + 1) * 0x9E3779B97F4A7C15UL)));
        const long scanLimit = 4_000_000;
        for (ulong scan = 1; scan <= (ulong)scanLimit; scan++)
        {
            ulong seed = Splitmix(scan * 0xD1342543DE82EF95UL);
            MegaCrit.Sts2.Core.Random.Rng twin = new(seed);
            double first = 1.0 - twin.NextDouble();
            double second = 1.0 - twin.NextDouble();
            double normal = Math.Sqrt(-2.0 * Math.Log(first)) * Math.Sin(Math.PI * 2.0 * second);
            int bin = FarTailBin(normal);
            if (bin < 0 || binned[bin] >= FarTailPerBin)
                continue;
            binned[bin]++;
            probes.Add(CaptureCountProbe(act, seed));
            int filled = 0;
            foreach (int count in binned)
                filled += count;
            if (filled == FarTailBins * FarTailPerBin)
                break;
        }
        return probes;
    }

    private const int RandomProbeCount = 2400;
    private const int FarTailBins = 16;
    private const int FarTailPerBin = 3;

    /// <summary>Bins <c>[-4,4)</c> in 0.5 steps; -1 outside.</summary>
    private static int FarTailBin(double normal)
    {
        if (normal < -4.0 || normal >= 4.0)
            return -1;
        return (int)Math.Floor((normal + 4.0) * 2.0);
    }

    private static CountProbe CaptureCountProbe(ActModel act, ulong seed)
    {
        MegaCrit.Sts2.Core.Random.Rng actRng = new(seed);
        MapPointTypeCounts counts = act.GetMapPointTypes(actRng);
        int delta = actRng.ToSerializable().counter;
        MegaCrit.Sts2.Core.Random.Rng twin = new(seed);
        double[] doubles = new double[Math.Max(delta, 2)];
        for (int draw = 0; draw < doubles.Length; draw++)
            doubles[draw] = twin.NextDouble();
        return new CountProbe(counts.NumOfRests, counts.NumOfUnknowns, delta, doubles);
    }

    private static ulong Splitmix(ulong seed)
    {
        ulong value = seed;
        MegaCrit.Sts2.Core.Random.MegaRandom.Splitmix64(ref value);
        return value;
    }

    /// <summary>
    /// Candidates use <c>Offset = 0</c>: <c>round(mean + z) + offset</c> is exactly
    /// <c>round((mean + offset) + z)</c> with the bounds shifted by the same offset, so every
    /// native draw has an equivalent offset-free representative.
    /// </summary>
    private static List<ContentCountDraw> BuildCountCandidates()
    {
        List<ContentCountDraw> candidates = [];
        for (int min = 0; min <= 32; min++)
        {
            for (int max = min + 1; max <= 33; max++)
            {
                candidates.Add(new ContentCountDraw
                {
                    Kind = ContentDrawKind.Uniform,
                    Min = min,
                    Max = max,
                });
            }
        }
        for (int mean = 0; mean <= 32; mean++)
        {
            for (int std = 1; std <= 3; std++)
            {
                for (int min = 0; min <= 32; min++)
                {
                    for (int max = min; max <= 33; max++)
                    {
                        candidates.Add(new ContentCountDraw
                        {
                            Kind = ContentDrawKind.Gaussian,
                            Mean = mean,
                            StdDev = std,
                            Min = min,
                            Max = max,
                        });
                    }
                }
            }
        }
        return candidates;
    }

    private static (ContentCountDraw First, ContentCountDraw Second)? FitPair(
        List<CountProbe> probes, List<ContentCountDraw> candidates, bool swapValues = false)
    {
        List<ContentCountDraw> firsts = [];
        foreach (ContentCountDraw candidate in candidates)
        {
            if (FitsFirst(probes, candidate, swapValues))
                firsts.Add(candidate);
        }
        (ContentCountDraw, ContentCountDraw)? found = null;
        foreach (ContentCountDraw first in firsts)
        {
            foreach (ContentCountDraw second in candidates)
            {
                if (!FitsSecond(probes, first, second, swapValues))
                    continue;
                if (found is { } existing)
                {
                    if (Describe(existing.Item1) == Describe(first)
                        && Describe(existing.Item2) == Describe(second))
                        continue;
                    return null;
                }
                found = (first, second);
            }
        }
        return found;
    }

    private static bool FitsFirst(List<CountProbe> probes, in ContentCountDraw draw,
        bool swapValues)
    {
        foreach (CountProbe probe in probes)
        {
            if (!Simulate(draw, probe.Doubles, 0, out int value, out int consumed))
                return false;
            int expected = swapValues ? probe.Unknown : probe.Rest;
            if (value != expected)
                return false;
            if (probe.Delta - consumed < 1)
                return false;
        }
        return true;
    }

    private static bool FitsSecond(List<CountProbe> probes, in ContentCountDraw first,
        in ContentCountDraw second, bool swapValues)
    {
        foreach (CountProbe probe in probes)
        {
            if (!Simulate(first, probe.Doubles, 0, out _, out int consumedFirst))
                return false;
            if (!Simulate(second, probe.Doubles, consumedFirst, out int value,
                    out int consumedSecond))
                return false;
            int expected = swapValues ? probe.Rest : probe.Unknown;
            if (value != expected)
                return false;
            if (consumedFirst + consumedSecond != probe.Delta)
                return false;
        }
        return true;
    }

    /// <summary>Replays one candidate draw over a recorded double sequence.</summary>
    private static bool Simulate(in ContentCountDraw draw, double[] doubles, int start,
        out int value, out int consumed)
    {
        value = 0;
        consumed = 0;
        if (draw.Kind == ContentDrawKind.Uniform)
        {
            if (start >= doubles.Length)
                return false;
            consumed = 1;
            int scale = Math.Max(draw.Max - draw.Min, 0);
            value = (int)(doubles[start] * scale) + draw.Min + draw.Offset;
            return true;
        }
        for (int attempt = 0; attempt < 32; attempt++)
        {
            int index = start + attempt * 2;
            if (index + 1 >= doubles.Length)
                return false;
            double first = 1.0 - doubles[index];
            double second = 1.0 - doubles[index + 1];
            double normal = Math.Sqrt(-2.0 * Math.Log(first)) * Math.Sin(Math.PI * 2.0 * second);
            int raw = (int)Math.Round(draw.Mean + draw.StdDev * normal);
            if (raw < draw.Min || raw > draw.Max)
                continue;
            value = raw + draw.Offset;
            consumed = (attempt + 1) * 2;
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- tutorial discovery order

    /// <summary>
    /// Infers <c>ActModel.ApplyActDiscoveryOrderModifications</c>: the set of
    /// <c>RoomSet.SwapToOrCreateAtIndex</c> calls, their per-list order, and whether they only run
    /// while <c>UnlockState.NumberOfRuns == 0</c>. All of it is observed by calling the native
    /// method on crafted room sets.
    /// </summary>
    private static (ContentRoomSwap[] Swaps, bool FirstRunOnly) InferTutorialSwaps(ActModel act)
    {
        List<EncounterModel> encounterFillers = ModelDb.All.OfType<EncounterModel>()
            .Where(model => !act.AllEncounters.Any(own => own.Id == model.Id))
            .Where(model => RunCatalog.TryEncounter(model.Id.Entry, out _))
            .GroupBy(model => model.Id.Entry).Select(group => group.First())
            .ToList();
        List<EventModel> eventFillers = ModelDb.All.OfType<EventModel>()
            .Where(model => !act.AllEvents.Any(own => own.Id == model.Id))
            .Where(model => RunCatalog.TryEvent(model.Id.Entry, out _))
            .GroupBy(model => model.Id.Entry).Select(group => group.First())
            .ToList();
        if (encounterFillers.Count < 16 || eventFillers.Count < 8)
            throw new InvalidDataException($"原版 {act.Id.Entry} 的发现顺序探针缺少填充模型。");

        ActModel mutable = act.ToMutable();
        FieldInfo roomsField = typeof(ActModel).GetField("_rooms",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(ActModel).FullName, "_rooms");
        List<ContentRoomSwap> discovered = [];
        foreach (ContentRoomList kind in new[]
                 {
                     ContentRoomList.Events, ContentRoomList.Normal, ContentRoomList.Elite,
                 })
        {
            ushort[] before = FillerIds(kind, eventFillers, encounterFillers);
            ushort[] after = ProbeSingleList(mutable, roomsField, kind, before, 0,
                eventFillers, encounterFillers);
            for (int index = 0; index < before.Length; index++)
            {
                if (after[index] == before[index])
                    continue;
                discovered.Add(new ContentRoomSwap
                {
                    List = kind,
                    Index = (byte)index,
                    ModelId = after[index],
                });
            }
        }
        if (discovered.Count == 0)
            return ([], false);

        ushort[] encounterPool = [.. act.AllEncounters.Select(RequireEncounter)];
        ushort[] eventPool = [.. act.AllEvents.Select(RequireEvent)];
        foreach (ContentRoomSwap swap in discovered)
        {
            bool known = swap.List == ContentRoomList.Events
                ? Array.IndexOf(eventPool, swap.ModelId) >= 0
                : Array.IndexOf(encounterPool, swap.ModelId) >= 0;
            if (!known)
                throw new InvalidDataException(
                    $"原版 {act.Id.Entry} 的发现顺序插入了幕外模型 {swap.ModelId}。");
        }

        List<ContentRoomSwap> ordered = [];
        foreach (IGrouping<ContentRoomList, ContentRoomSwap> group in discovered
                     .GroupBy(swap => swap.List))
        {
            List<ContentRoomSwap> ops = [.. group.OrderBy(swap => swap.Index)];
            ordered.AddRange(ResolveSwapOrder(act, mutable, roomsField, ops, eventFillers,
                encounterFillers));
        }

        bool firstRunOnly = true;
        for (int runs = 1; runs <= 2; runs++)
        {
            foreach (ContentRoomList kind in new[]
                     {
                         ContentRoomList.Events, ContentRoomList.Normal, ContentRoomList.Elite,
                     })
            {
                ushort[] before = FillerIds(kind, eventFillers, encounterFillers);
                ushort[] after = ProbeSingleList(mutable, roomsField, kind, before, runs,
                    eventFillers, encounterFillers);
                if (!before.SequenceEqual(after))
                {
                    firstRunOnly = false;
                    break;
                }
            }
            if (!firstRunOnly)
                break;
        }
        return ([.. ordered], firstRunOnly);
    }

    private static ushort[] FillerIds(ContentRoomList kind, List<EventModel> eventFillers,
        List<EncounterModel> encounterFillers)
    {
        int length = kind switch
        {
            ContentRoomList.Events => 32,
            ContentRoomList.Normal => RunLimits.ActNormalEncounters,
            _ => RunLimits.ActEliteEncounters,
        };
        ushort[] ids = new ushort[length];
        if (kind == ContentRoomList.Events)
        {
            for (int index = 0; index < length; index++)
                ids[index] = RequireEvent(eventFillers[index % eventFillers.Count]);
        }
        else
        {
            int shift = kind == ContentRoomList.Elite ? 5 : 0;
            for (int index = 0; index < length; index++)
                ids[index] = RequireEncounter(
                    encounterFillers[(index + shift) % encounterFillers.Count]);
        }
        return ids;
    }

    private static ushort[] ProbeSingleList(ActModel mutable, FieldInfo roomsField,
        ContentRoomList kind, ushort[] before, int numberOfRuns, List<EventModel> eventFillers,
        List<EncounterModel> encounterFillers)
    {
        RoomSet rooms = BuildRooms(kind, before, eventFillers, encounterFillers);
        roomsField.SetValue(mutable, rooms);
        mutable.ApplyDiscoveryOrderModifications(new UnlockState(
            Array.Empty<string>(), Array.Empty<ModelId>(), numberOfRuns));
        return ExtractIds(kind, rooms);
    }

    private static RoomSet BuildRooms(ContentRoomList kind, ushort[] ids,
        List<EventModel> eventFillers, List<EncounterModel> encounterFillers)
    {
        RoomSet rooms = new();
        ushort[] eventIds = FillerIds(ContentRoomList.Events, eventFillers, encounterFillers);
        ushort[] normalIds = FillerIds(ContentRoomList.Normal, eventFillers, encounterFillers);
        ushort[] eliteIds = FillerIds(ContentRoomList.Elite, eventFillers, encounterFillers);
        if (kind == ContentRoomList.Events)
            eventIds = ids;
        else if (kind == ContentRoomList.Normal)
            normalIds = ids;
        else
            eliteIds = ids;
        foreach (ushort id in eventIds)
            rooms.events.Add(ResolveEvent(id));
        foreach (ushort id in normalIds)
            rooms.normalEncounters.Add(ResolveEncounter(id));
        foreach (ushort id in eliteIds)
            rooms.eliteEncounters.Add(ResolveEncounter(id));
        rooms.Boss = encounterFillers.First(model => model.RoomType == RoomType.Boss);
        return rooms;
    }

    private static ushort[] ExtractIds(ContentRoomList kind, RoomSet rooms)
        => kind switch
        {
            ContentRoomList.Events => [.. rooms.events.Select(RequireEvent)],
            ContentRoomList.Normal => [.. rooms.normalEncounters.Select(RequireEncounter)],
            _ => [.. rooms.eliteEncounters.Select(RequireEncounter)],
        };

    private static List<ContentRoomSwap> ResolveSwapOrder(ActModel act, ActModel mutable,
        FieldInfo roomsField, List<ContentRoomSwap> ops, List<EventModel> eventFillers,
        List<EncounterModel> encounterFillers)
    {
        if (ops.Count <= 1)
            return ops;
        if (ops.Count > 8)
            throw new InvalidDataException(
                $"原版 {act.Id.Entry} 的发现顺序列表有 {ops.Count} 次交换，超出可推断范围。");
        List<int[]> permutations = [];
        Permute([], [.. Enumerable.Range(0, ops.Count)], permutations);
        List<ushort[]> probes = [];
        List<ushort[]> natives = [];
        for (int seed = 0; seed < 48; seed++)
        {
            ushort[] before = BuildProbeList(ops, seed, eventFillers, encounterFillers);
            probes.Add(before);
            natives.Add(ProbeSingleList(mutable, roomsField, ops[0].List, before, 0,
                eventFillers, encounterFillers));
        }
        List<int[]> matching = [];
        int width = probes[0].Length;
        Span<ushort> simulated = stackalloc ushort[width];
        foreach (int[] permutation in permutations)
        {
            bool ok = true;
            for (int index = 0; index < probes.Count; index++)
            {
                probes[index].CopyTo(simulated);
                ApplySwaps(simulated, ops, permutation);
                if (!natives[index].AsSpan().SequenceEqual(simulated))
                {
                    ok = false;
                    break;
                }
            }
            if (ok)
                matching.Add(permutation);
        }
        if (matching.Count == 0)
            throw new InvalidDataException(
                $"原版 {act.Id.Entry} 的发现顺序交换顺序没有候选。");
        if (matching.Count > 1)
        {
            // Some call orders are algebraically equivalent (for example two swaps with distinct
            // targets). Accept several candidates only when they agree on every random list.
            for (int seed = 1000; seed < 1512; seed++)
            {
                ushort[] probe = BuildProbeList(ops, seed, eventFillers, encounterFillers);
                ushort[] reference = SimulateSwaps(probe, ops, matching[0]);
                for (int index = 1; index < matching.Count; index++)
                {
                    if (!SimulateSwaps(probe, ops, matching[index]).SequenceEqual(reference))
                        throw new InvalidDataException(
                            $"原版 {act.Id.Entry} 的发现顺序交换顺序无法唯一确定"
                            + $"（{matching.Count} 个非等价候选）。");
                }
            }
        }
        return [.. matching[0].Select(index => ops[index])];
    }

    private static void Permute(List<int> prefix, List<int> remaining, List<int[]> output)
    {
        if (remaining.Count == 0)
        {
            output.Add([.. prefix]);
            return;
        }
        for (int index = 0; index < remaining.Count; index++)
        {
            int value = remaining[index];
            prefix.Add(value);
            List<int> next = [.. remaining];
            next.RemoveAt(index);
            Permute(prefix, next, output);
            prefix.RemoveAt(prefix.Count - 1);
        }
    }

    private static ushort[] BuildProbeList(List<ContentRoomSwap> ops, int seed,
        List<EventModel> eventFillers, List<EncounterModel> encounterFillers)
    {
        ushort[] ids = FillerIds(ops[0].List, eventFillers, encounterFillers);
        bool[] used = new bool[ids.Length];
        Random random = new(seed * 7919 + 17);
        for (int index = 0; index < ops.Count; index++)
        {
            int slot = random.Next(ids.Length);
            while (used[slot])
                slot = random.Next(ids.Length);
            used[slot] = true;
            ids[slot] = ops[index].ModelId;
        }
        return ids;
    }

    private static ushort[] SimulateSwaps(ushort[] before, List<ContentRoomSwap> ops, int[] order)
    {
        ushort[] ids = [.. before];
        ApplySwaps(ids, ops, order);
        return ids;
    }

    private static void ApplySwaps(Span<ushort> ids, List<ContentRoomSwap> ops, int[] order)
    {
        foreach (int opIndex in order)
        {
            ContentRoomSwap op = ops[opIndex];
            int found = -1;
            for (int index = 0; index < ids.Length; index++)
            {
                if (ids[index] != op.ModelId)
                    continue;
                found = index;
                break;
            }
            if (found >= 0)
                (ids[op.Index], ids[found]) = (ids[found], ids[op.Index]);
            else
                ids[op.Index] = op.ModelId;
        }
    }

    // ---------------------------------------------------------------- shared helpers

    private static readonly Dictionary<ushort, EventModel> EventModels = ModelDb.All
        .OfType<EventModel>()
        .GroupBy(model => model.Id.Entry, StringComparer.Ordinal)
        .Select(group => group.First())
        .Where(model => RunCatalog.TryEvent(model.Id.Entry, out _))
        .ToDictionary(model => RequireEvent(model), model => model);

    private static readonly Dictionary<ushort, EncounterModel> EncounterModels = ModelDb.All
        .OfType<EncounterModel>()
        .GroupBy(model => model.Id.Entry, StringComparer.Ordinal)
        .Select(group => group.First())
        .Where(model => RunCatalog.TryEncounter(model.Id.Entry, out _))
        .ToDictionary(model => RequireEncounter(model), model => model);

    private static EventModel ResolveEvent(ushort id)
        => EventModels.TryGetValue(id, out EventModel? model)
            ? model
            : throw new InvalidDataException($"原版事件 id {id} 没有规范模型。");

    private static EncounterModel ResolveEncounter(ushort id)
        => EncounterModels.TryGetValue(id, out EncounterModel? model)
            ? model
            : throw new InvalidDataException($"原版遭遇 id {id} 没有规范模型。");

    private static UnlockState WithoutEpoch(string excluded)
        => new(EpochModel.AllEpochIds.Where(id => !string.Equals(id, excluded, StringComparison.Ordinal)),
            Array.Empty<ModelId>(), 0);

    private static ushort RequireCard(CardModel model)
        => Registry.TryCard(model.Id.Entry, out ushort id)
            ? id
            : throw new InvalidDataException($"原版卡牌 {model.Id.Entry} 没有 Blitz 定义。");

    private static ushort RequireRelic(RelicModel model)
        => Registry.TryRelic(model.Id.Entry, out ushort id)
            ? id
            : throw new InvalidDataException($"原版遗物 {model.Id.Entry} 没有 Blitz 定义。");

    private static ushort RequireAct(ActModel model)
        => RunCatalog.TryAct(model.Id.Entry, out ushort id)
            ? id
            : throw new InvalidDataException($"原版幕 {model.Id.Entry} 不在目录内。");

    private static ushort RequireEvent(EventModel model)
        => RunCatalog.TryEvent(model.Id.Entry, out ushort id)
            ? id
            : throw new InvalidDataException($"原版事件 {model.Id.Entry} 不在目录内。");

    private static ushort RequireEncounter(EncounterModel model)
        => RunCatalog.TryEncounter(model.Id.Entry, out ushort id)
            ? id
            : throw new InvalidDataException($"原版遭遇 {model.Id.Entry} 不在目录内。");
}
