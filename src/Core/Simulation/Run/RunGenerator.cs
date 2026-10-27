using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Input of one standard single-player new run: the character, ascension, native seed string, the
/// unlock state and whether the tutorial discovery order applies. Everything else is derived from
/// <see cref="ContentTable"/> exactly like <c>RunState.CreateForNewRun</c> +
/// <c>RunManager.InitializeNewRun</c> + <c>RunManager.GenerateRooms</c> + <c>SetActInternal(0)</c>.
/// </summary>
public sealed class NewRunRequest
{
    public required ushort Character { get; init; }

    public required int Ascension { get; init; }

    public required string Seed { get; init; }

    public required RunUnlockState Unlock { get; init; }

    /// <summary>Native <c>RunManager.ShouldApplyTutorialModifications()</c> for this run.</summary>
    public required bool TutorialModifications { get; init; }
}

/// <summary>
/// Native new-run generation in Core. The order of random draws follows the native call order:
/// shared relic bag, player relic bag, shared-ancient subset split, per-act room generation
/// (events, weak encounters, regular encounters, elites, boss, ancient), the ascension-10 second
/// boss, and finally the act map.
/// </summary>
public static class RunGenerator
{
    /// <summary>
    /// Creates the run state immediately before the first choice: acts and rooms generated, first
    /// act map generated, no room entered and no map point visited.
    /// </summary>
    public static RunState CreateNewRun(ContentTable content, in NewRunRequest request,
        out RngState actMapRng)
    {
        if (request.Ascension < 0 || request.Ascension > content.AscensionRules.MaxLevel)
            throw new UnsupportedException(
                $"进阶 {request.Ascension} 超出 0..{content.AscensionRules.MaxLevel}。");
        // Native Player.CreateForNewRun does not gate on the character unlock epoch: the
        // character select screen does. A new run with a locked character is still legal input.
        ContentCharacter character = content.Character(request.Character);

        RunState state = default;
        state.Ascension = request.Ascension;
        state.ActIndex = 0;
        state.GameMode = RunGameMode.Standard;
        state.Decision = RunDecision.None;
        state.Unlock = request.Unlock;
        state.Extra.StartedWithNeow = IsRevealed(request.Unlock, 1UL << content.NeowEpoch)
            ? (byte)1
            : (byte)0;
        ResetOdds(content, ref state);

        ulong seed = RunRng.SeedFromString(request.Seed);
        // Native Player.InitializeSeed hashes the *string* with the modern hash even when
        // RunRngSet took the "old" legacy-hash branch, so the player seed is not simply seed + 0.
        ulong playerSeed = RunHash.XxHash64(
            System.Text.Encoding.UTF8.GetBytes(request.Seed)) + 0UL; // single player slot 0
        state.Rng = RunRng.NewRun(seed, playerSeed);

        CreatePlayer(content, character, ref state);

        RngState upFront = state.Rng.Streams[(int)RunRngStream.UpFront];
        state.SharedBag = BuildSharedBag(content, request.Unlock, ref upFront);
        state.PlayerBag = BuildPlayerBag(content, character, request.Unlock, ref upFront);
        state.Rng.Streams[(int)RunRngStream.UpFront] = upFront;

        ApplyAscensionEffects(content, request.Ascension, ref state);
        GenerateRooms(content, request.Unlock, request.TutorialModifications, ref state);
        SetAct(content, ref state, 0, out actMapRng);
        return state;
    }

    /// <summary>
    /// Native <c>RunManager.SetActInternal</c> without the room entry: switches the act, clears the
    /// visited coordinates, resets the unknown-map-point odds and generates the act's map from
    /// <c>Rng.Map[actIndex]</c>. The room stack and the "enter starting map point" step are R2b-2.
    /// </summary>
    public static void SetAct(ContentTable content, ref RunState state, int actIndex,
        out RngState actMapRng)
    {
        if ((uint)actIndex >= (uint)state.Acts.Count)
            throw new UnsupportedException($"幕序号 {actIndex} 超出 {state.Acts.Count} 幕。");
        RejectMapHooks(content, in state);
        state.ActIndex = actIndex;
        // Native ClearVisitedMapCoordsDebug clears the coordinates and zeroes ActFloor.
        state.ActFloor = 0;
        state.Visited.CoordCount = 0;
        state.Visited.Coords = default;
        ResetOdds(content, ref state);
        RunMap map = RunMapGenerator.Generate(content, state, actIndex, out actMapRng);
        if (actIndex == 0 && state.Extra.StartedWithNeow == 0)
        {
            for (int index = 0; index < map.NodeCount; index++)
            {
                if (map.Nodes[index].Coord.Equals(map.Starting))
                    map.Nodes[index].Type = RunMapPointType.Monster;
            }
        }
        state.Map = map;
        state.Rng.MapConsumed[actIndex] = 1;
    }

    /// <summary>
    /// FurCoat/GoldenCompass/SpoilsMap rewrite the generated map. R2b-1 implements only
    /// <c>StandardActMap</c>, so a run holding them fails explicitly instead of producing a map the
    /// game would not.
    /// </summary>
    private static void RejectMapHooks(ContentTable content, in RunState state)
    {
        for (int index = 0; index < state.Relics.Count; index++)
        {
            ushort id = state.Relics.Items[index].Id;
            if (Array.IndexOf(content.MapHookRelics, id) < 0)
                continue;
            throw new UnsupportedException(
                $"地图 Hook 未实现：遗物 {Registry.Relic(id).NativeId} 会改写生成后的地图。");
        }
        for (int index = 0; index < state.Deck.Count; index++)
        {
            ushort id = state.Deck.Cards[index].Id;
            if (Array.IndexOf(content.MapHookCards, id) < 0)
                continue;
            throw new UnsupportedException(
                $"地图 Hook 未实现：卡牌 {Registry.Card(id).NativeId} 会改写生成后的地图。");
        }
    }

    private static void ResetOdds(ContentTable content, ref RunState state)
    {
        state.Odds = new RunOdds
        {
            Monster = content.BaseOdds.Monster,
            Elite = content.BaseOdds.Elite,
            Treasure = content.BaseOdds.Treasure,
            Shop = content.BaseOdds.Shop,
        };
        state.PlayerOdds = new RunPlayerOdds
        {
            CardRarity = content.BaseOdds.CardRarity,
            PotionReward = content.BaseOdds.PotionReward,
        };
    }

    private static void CreatePlayer(ContentTable content, ContentCharacter character,
        ref RunState state)
    {
        ContentStartingInventory starting = character.Starting;
        RunPlayer player = default;
        player.Character = character.Id;
        player.CurrentHp = starting.Hp;
        player.MaxHp = starting.Hp;
        player.MaxEnergy = starting.MaxEnergy;
        player.Gold = starting.Gold;
        player.BaseOrbSlots = starting.OrbSlots;
        int potionSlots = starting.PotionSlots;
        if (state.Ascension >= content.AscensionRules.TightBeltLevel)
            potionSlots--;
        if (potionSlots < 0)
            potionSlots = 0;
        if (potionSlots > RunLimits.PotionSlots)
            throw new CapacityException("RunState.PotionSlots", RunLimits.PotionSlots);
        player.Potions.Count = (byte)potionSlots;
        state.Player = player;

        for (int index = 0; index < starting.Deck.Length; index++)
        {
            RunCard card = default;
            card.Id = starting.Deck[index];
            CardStats stats = content.Card(card.Id, 0);
            card.Flags = (stats.Keywords & Keywords.Eternal) == 0
                ? RunCardFlags.Removable : RunCardFlags.None;
            if (stats.Type == CardType.Curse)
                card.Flags |= RunCardFlags.Curse;
            card.FloorAddedToDeck = 1;
            state.AddCard(card);
        }
        for (int index = 0; index < starting.Relics.Length; index++)
        {
            ContentStartingRelic source = starting.Relics[index];
            RunRelic relic = default;
            relic.Id = source.Id;
            relic.Status = RunRelicStatus.Normal;
            relic.StackCount = 1;
            relic.DisplayAmount = source.DisplayAmount;
            relic.FloorAddedToDeck = 1;
            state.AddRelic(relic);
        }
        for (int index = 0; index < starting.Potions.Length; index++)
            state.SetPotion(index, starting.Potions[index]);
    }

    private static void ApplyAscensionEffects(ContentTable content, int ascension, ref RunState state)
    {
        ContentAscensionRules rules = content.AscensionRules;
        if (ascension < rules.AscendersBaneLevel)
            return;
        RunCard bane = default;
        bane.Id = rules.AscendersBaneCardId;
        bane.FloorAddedToDeck = 1;
        state.AddCard(bane);
    }

    /// <summary>
    /// Native <c>RelicGrabBag.Populate(Player, Rng)</c>: shared pool then character pool, filtered
    /// by unlock, then restricted to the four rollable rarities. Buckets are shuffled in the order
    /// their rarity first appears.
    /// </summary>
    private static RunRelicBag BuildPlayerBag(ContentTable content, ContentCharacter character,
        in RunUnlockState unlock, ref RngState rng)
    {
        Span<ushort> ids = stackalloc ushort[
            content.SharedRelicPool.Length + character.RelicPool.Length];
        Span<byte> rarities = stackalloc byte[ids.Length];
        int count = 0;
        for (int index = 0; index < content.SharedRelicPool.Length; index++)
        {
            ContentRelicPoolEntry entry = content.SharedRelicPool[index];
            if (!IsRollable(entry.Rarity) || !IsRevealed(unlock, entry.RequiredEpochs))
                continue;
            ids[count] = entry.RelicId;
            rarities[count] = (byte)entry.Rarity;
            count++;
        }
        for (int index = 0; index < character.RelicPool.Length; index++)
        {
            ContentRelicPoolEntry entry = character.RelicPool[index];
            if (!IsRollable(entry.Rarity) || !IsRevealed(unlock, entry.RequiredEpochs))
                continue;
            ids[count] = entry.RelicId;
            rarities[count] = (byte)entry.Rarity;
            count++;
        }
        return BuildBag(ids[..count], rarities[..count], refreshAllowed: false, ref rng);
    }

    /// <summary>Native <c>RelicGrabBag.Populate(IEnumerable&lt;RelicModel&gt;, Rng)</c>.</summary>
    private static RunRelicBag BuildSharedBag(ContentTable content, in RunUnlockState unlock,
        ref RngState rng)
    {
        Span<ushort> ids = stackalloc ushort[content.SharedRelicPool.Length];
        Span<byte> rarities = stackalloc byte[ids.Length];
        int count = 0;
        for (int index = 0; index < content.SharedRelicPool.Length; index++)
        {
            ContentRelicPoolEntry entry = content.SharedRelicPool[index];
            if (!IsRevealed(unlock, entry.RequiredEpochs))
                continue;
            ids[count] = entry.RelicId;
            rarities[count] = (byte)entry.Rarity;
            count++;
        }
        return BuildBag(ids[..count], rarities[..count], refreshAllowed: true, ref rng);
    }

    private static RunRelicBag BuildBag(ReadOnlySpan<ushort> ids, ReadOnlySpan<byte> rarities,
        bool refreshAllowed, ref RngState rng)
    {
        RunRelicBag bag = default;
        bag.RefreshAllowed = refreshAllowed ? (byte)1 : (byte)0;
        Span<byte> bucketOrder = stackalloc byte[RunLimits.RelicRarities];
        int bucketCount = 0;
        for (int index = 0; index < ids.Length; index++)
        {
            byte rarity = rarities[index];
            bool seen = false;
            for (int order = 0; order < bucketCount; order++)
            {
                if (bucketOrder[order] == rarity)
                {
                    seen = true;
                    break;
                }
            }
            if (!seen)
                bucketOrder[bucketCount++] = rarity;
        }
        Span<ushort> bucket = stackalloc ushort[RunLimits.RelicBagPerRarity];
        for (int order = 0; order < bucketCount; order++)
        {
            byte rarity = bucketOrder[order];
            int bucketSize = 0;
            for (int index = 0; index < ids.Length; index++)
            {
                if (rarities[index] != rarity)
                    continue;
                if (bucketSize >= RunLimits.RelicBagPerRarity)
                    throw new CapacityException("RunRelicBag.Segment", RunLimits.RelicBagPerRarity);
                bucket[bucketSize++] = ids[index];
            }
            RunRandom.UnstableShuffle(ref rng, bucket[..bucketSize]);
            RunRelicBagSegment segment = default;
            segment.Count = (ushort)bucketSize;
            for (int index = 0; index < bucketSize; index++)
                segment.Ids[index] = bucket[index];
            bag.Segments[rarity] = segment;
        }
        return bag;
    }

    private static bool IsRollable(RunRelicRarity rarity)
        => rarity is RunRelicRarity.Common or RunRelicRarity.Uncommon or RunRelicRarity.Rare
            or RunRelicRarity.Shop;

    private static void GenerateRooms(ContentTable content, in RunUnlockState unlock,
        bool tutorial, ref RunState state)
    {
        RngState upFront = state.Rng.Streams[(int)RunRngStream.UpFront];

        // RunManager.GenerateRooms: shuffle the shared ancients, then split a random prefix into
        // every act after the first.
        Span<ushort> sharedAncients = stackalloc ushort[content.SharedAncients.Length];
        int sharedCount = 0;
        for (int index = 0; index < content.SharedAncients.Length; index++)
        {
            ContentAncientEntry entry = content.SharedAncients[index];
            if (IsRevealed(unlock, entry.RequiredEpochs))
                sharedAncients[sharedCount++] = entry.EventId;
        }
        RunRandom.UnstableShuffle(ref upFront, sharedAncients[..sharedCount]);

        ushort[][] subsets = new ushort[RunLimits.Acts][];
        for (int act = 0; act < subsets.Length; act++)
            subsets[act] = [];
        int remaining = sharedCount;
        for (int act = 1; act < content.DefaultActs.Length; act++)
        {
            int count = upFront.NextInt(remaining + 1);
            ushort[] subset = new ushort[count];
            for (int index = 0; index < count; index++)
                subset[index] = sharedAncients[index];
            for (int index = 0; index < remaining - count; index++)
                sharedAncients[index] = sharedAncients[index + count];
            remaining -= count;
            subsets[act] = subset;
        }

        state.Rng.Streams[(int)RunRngStream.UpFront] = upFront;

        for (int actIndex = 0; actIndex < content.DefaultActs.Length; actIndex++)
        {
            ushort actId = content.DefaultActs[actIndex];
            ContentAct act = content.Act(actId);
            RunAct target = default;
            target.Id = actId;
            GenerateActRooms(content, act, unlock, subsets[actIndex], ref state.Rng, ref target);
            if (tutorial)
                ApplyDiscoveryOrder(content, act, unlock, ref target);
            if (actIndex == content.DefaultActs.Length - 1
                && state.Ascension >= content.AscensionRules.DoubleBossLevel)
            {
                RngState after = state.Rng.Streams[(int)RunRngStream.UpFront];
                target.HasSecondBoss = 1;
                target.SecondBossId = NextBossExcluding(content, act, target.BossId, ref after);
                state.Rng.Streams[(int)RunRngStream.UpFront] = after;
            }
            state.AddAct(target);
        }
    }

    private static void GenerateActRooms(ContentTable content, ContentAct act,
        in RunUnlockState unlock, ushort[] sharedSubset, ref RunRngState rng, ref RunAct target)
    {
        RngState upFront = rng.Streams[(int)RunRngStream.UpFront];

        // ActModel.GenerateRooms: act events + shared events, filtered by their epochs, shuffled.
        Span<ushort> events = stackalloc ushort[act.Events.Length + content.SharedEvents.Length];
        int eventCount = 0;
        for (int index = 0; index < act.Events.Length; index++)
        {
            ushort id = act.Events[index];
            if (IsRevealed(unlock, content.EventRequiredEpochs[id]))
                events[eventCount++] = id;
        }
        for (int index = 0; index < content.SharedEvents.Length; index++)
        {
            ushort id = content.SharedEvents[index];
            if (IsRevealed(unlock, content.EventRequiredEpochs[id]))
                events[eventCount++] = id;
        }
        RunRandom.UnstableShuffle(ref upFront, events[..eventCount]);
        for (int index = 0; index < eventCount; index++)
            RunState.AddActEvent(ref target, events[index]);

        // Weak encounters first, then regular encounters. Each bag is refilled from the act's
        // filtered pool in native order when empty, and consecutive equal tags are avoided.
        RunGrabBag weak = default;
        RunGrabBag regular = default;
        for (int index = 0; index < act.WeakEncounters; index++)
        {
            if (!weak.Any)
                FillBag(content, act, EncounterPool.Weak, ref weak);
            AddWithoutRepeatingTags(content, ref weak, ref upFront, ref target, elite: false);
        }
        for (int index = act.WeakEncounters; index < act.BaseRooms; index++)
        {
            if (!regular.Any)
                FillBag(content, act, EncounterPool.Regular, ref regular);
            AddWithoutRepeatingTags(content, ref regular, ref upFront, ref target, elite: false);
        }

        RunGrabBag eliteBag = default;
        for (int index = 0; index < content.MapRules.MaxElites; index++)
        {
            if (!eliteBag.Any)
                FillBag(content, act, EncounterPool.Elite, ref eliteBag);
            AddWithoutRepeatingTags(content, ref eliteBag, ref upFront, ref target, elite: true);
        }

        target.BossId = RunRandom.NextItem(ref upFront, content.BossEncounters(act));
        if (NextAncient(content, act, unlock, sharedSubset, ref upFront, out ushort ancient))
        {
            target.HasAncient = 1;
            target.AncientId = ancient;
        }
        rng.Streams[(int)RunRngStream.UpFront] = upFront;
    }

    private enum EncounterPool
    {
        Weak,
        Regular,
        Elite,
    }

    private static void FillBag(ContentTable content, ContentAct act, EncounterPool pool,
        ref RunGrabBag bag)
    {
        for (int index = 0; index < act.Encounters.Length; index++)
        {
            ushort id = act.Encounters[index];
            ContentEncounter encounter = content.Encounter(id);
            bool match = pool switch
            {
                EncounterPool.Weak => encounter.RoomType == RunRoomType.Monster && encounter.IsWeak,
                EncounterPool.Regular => encounter.RoomType == RunRoomType.Monster && !encounter.IsWeak,
                EncounterPool.Elite => encounter.RoomType == RunRoomType.Elite,
                _ => false,
            };
            if (match)
                bag.Add(id, 1.0);
        }
    }

    private static void AddWithoutRepeatingTags(ContentTable content, ref RunGrabBag bag,
        ref RngState rng, ref RunAct target, bool elite)
    {
        ushort count = elite ? target.EliteCount : target.NormalCount;
        ushort last = count == 0 ? default : elite ? target.Elite[count - 1] : target.Normal[count - 1];
        ulong lastTags = count == 0 ? 0UL : content.Encounter(last).TagMask;
        bool Chosen(ushort id)
            => count == 0 || (id != last && (content.Encounter(id).TagMask & lastTags) == 0);
        bool found = bag.GrabAndRemove(ref rng, Chosen, out ushort picked);
        if (!found)
            found = bag.GrabAndRemove(ref rng, null, out picked);
        if (!found)
            return;
        if (elite)
            RunState.AddActEliteEncounter(ref target, picked);
        else
            RunState.AddActNormalEncounter(ref target, picked);
    }

    private static ushort NextBossExcluding(ContentTable content, ContentAct act, ushort excluded,
        ref RngState rng)
    {
        Span<ushort> candidates = stackalloc ushort[act.Encounters.Length];
        int count = 0;
        for (int index = 0; index < act.Encounters.Length; index++)
        {
            ushort id = act.Encounters[index];
            if (id == excluded || content.Encounter(id).RoomType != RunRoomType.Boss)
                continue;
            candidates[count++] = id;
        }
        if (count == 0)
            throw new UnsupportedException($"幕 {act.NativeId} 没有第二个 Boss 候选。");
        return RunRandom.NextItem(ref rng, candidates[..count]);
    }

    private static bool NextAncient(ContentTable content, ContentAct act,
        in RunUnlockState unlock, ushort[] sharedSubset, ref RngState rng, out ushort ancient)
    {
        Span<ushort> ancients = stackalloc ushort[act.Ancients.Length + sharedSubset.Length];
        int count = 0;
        for (int index = 0; index < act.Ancients.Length; index++)
        {
            ContentAncientEntry entry = act.Ancients[index];
            if (IsRevealed(unlock, entry.RequiredEpochs))
                ancients[count++] = entry.EventId;
        }
        for (int index = 0; index < sharedSubset.Length; index++)
            ancients[count++] = sharedSubset[index];
        ancient = count == 0 ? default : RunRandom.NextItem(ref rng, ancients[..count]);
        return count != 0;
    }

    private static void ApplyDiscoveryOrder(ContentTable content, ContentAct act,
        in RunUnlockState unlock, ref RunAct target)
    {
        for (int index = 0; index < act.BossDiscoveryOrder.Length; index++)
        {
            ushort boss = act.BossDiscoveryOrder[index];
            if (!HasSeenEncounter(unlock, boss))
            {
                target.BossId = boss;
                break;
            }
        }
        if (act.TutorialSwapsFirstRunOnly && unlock.NumberOfRuns != 0)
            return;
        for (int index = 0; index < act.TutorialSwaps.Length; index++)
        {
            ContentRoomSwap swap = act.TutorialSwaps[index];
            switch (swap.List)
            {
                case ContentRoomList.Events:
                {
                    Span<ushort> ids = target.Events;
                    SwapToOrCreate(ids, ref target.EventCount, swap);
                    break;
                }
                case ContentRoomList.Normal:
                {
                    Span<ushort> ids = target.Normal;
                    SwapToOrCreate(ids, ref target.NormalCount, swap);
                    break;
                }
                case ContentRoomList.Elite:
                {
                    Span<ushort> ids = target.Elite;
                    SwapToOrCreate(ids, ref target.EliteCount, swap);
                    break;
                }
                default:
                    throw new UnsupportedException($"未知的教学重排列表 {(byte)swap.List}。");
            }
        }
    }

    /// <summary>Native <c>RoomSet.SwapToOrCreateAtIndex</c>: find and swap, else place.</summary>
    private static void SwapToOrCreate(Span<ushort> ids, ref ushort count,
        in ContentRoomSwap swap)
    {
        if (swap.Index >= count)
            throw new UnsupportedException(
                $"教学重排目标下标 {swap.Index} 超出列表长度 {count}。");
        int found = -1;
        for (int index = 0; index < count; index++)
        {
            if (ids[index] != swap.ModelId)
                continue;
            found = index;
            break;
        }
        if (found >= 0)
        {
            (ids[found], ids[swap.Index]) = (ids[swap.Index], ids[found]);
            return;
        }
        ids[swap.Index] = swap.ModelId;
    }

    private static bool HasSeenEncounter(in RunUnlockState unlock, ushort encounter)
        => (unlock.EncountersSeen[encounter >> 6] & (1UL << (encounter & 63))) != 0;

    internal static bool IsRevealed(in RunUnlockState unlock, ulong requiredEpochs)
    {
        for (int epoch = 0; epoch < RunCatalog.EpochCount; epoch++)
        {
            if ((requiredEpochs & (1UL << epoch)) == 0)
                continue;
            if ((unlock.Epochs[epoch >> 6] & (1UL << (epoch & 63))) == 0)
                return false;
        }
        return true;
    }
}
