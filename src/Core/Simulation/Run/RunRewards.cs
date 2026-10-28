using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Post-combat reward generation at the native <c>RewardsSet.WithRewardsFromRoom</c> +
/// <c>GenerateWithoutOffering</c> endpoint. The entry is called after
/// <see cref="RunCombatWriteback"/>; it reads the finished combat room, its encounter and the run's
/// relic/potion/reward state, and fills <see cref="RunState.Rewards"/> with the same populated list
/// and in the same RNG order as the native screen. Unsupported encounter/hook content throws
/// <see cref="ContentUnavailableException"/> with a stable reason instead of guessing.
/// </summary>
public static class RunRewardGenerator
{
    private const ushort NoPotion = ushort.MaxValue;

    private static readonly int[] RewardSetIndex =
    [
        0, // None
        1, // Gold
        2, // Potion
        5, // Card
        3, // Relic
        4, // SpecialCard
        7, // CardRemoval
    ];

    public static void GenerateForCombat(ref RunState state, ContentTable table)
    {
        if (state.Rewards.Generated != 0)
            return;
        if (state.Rooms.Count == 0)
            throw new InvalidDataException("奖励生成时房间栈为空。");
        RunState work = state;
        RunRoom room = work.Rooms.Items[work.Rooms.Count - 1];
        if (room.Model.Kind != RunRefKind.Encounter)
            throw new ContentUnavailableException(ContentReason.UnsupportedRoom,
                $"奖励房间的模型 {room.Model.Kind} 不是遭遇。");
        ContentEncounter encounter = table.Encounter(room.Model.Id);
        if (!encounter.ShouldGiveRewards)
        {
            work.ClearRewards();
            work.Rewards.Generated = 1;
            work.Rewards.Stage = RunRewardStage.None;
            work.Canonicalize();
            state = work;
            return;
        }
        bool lastActBoss = room.RoomType == RunRoomType.Boss
            && work.ActIndex >= work.Acts.Count - 1;
        if (!lastActBoss)
        {
            if (encounter.CustomGoldProportion)
            {
                throw new ContentUnavailableException(RunRewardReason.GoldProportion,
                    $"遭遇 {encounter.NativeId} 的金币比例依赖战斗私有状态。");
            }
            RequireSupportedListeners(in work, table);
        }

        Span<RunReward> extras = stackalloc RunReward[RunLimits.Rewards];
        Span<RunCard> extraCards = stackalloc RunCard[RunLimits.RewardCardOptions];
        int extraCount = work.Rewards.Count;
        int extraCardCount = 0;
        for (int index = 0; index < extraCount; index++)
        {
            RunReward item = work.Rewards.Items[index];
            extras[index] = item;
            for (int option = 0; option < item.OptionCount; option++)
            {
                if (extraCardCount >= RunLimits.RewardCardOptions)
                    throw new CapacityException("RunState.RewardCardOptions", RunLimits.RewardCardOptions);
                extraCards[extraCardCount++] = work.Rewards.Choices[item.OptionStart + option];
            }
        }
        work.Rewards.Items = default;
        work.Rewards.Choices = default;
        work.Rewards.Count = 0;
        work.Rewards.Stage = RunRewardStage.None;
        work.Rewards.ChoiceIndex = 0;
        work.Rewards.ChoiceCount = 0;

        if (!lastActBoss)
        {
            if (!TryAddTutorial(ref work, table, room, encounter))
                AddDefaultRewards(ref work, table, room, encounter);
            AddExtras(ref work, extras[..extraCount], extraCards[..extraCardCount]);
        }

        // Native GenerateWithoutOffering: populate every reward still unpopulated, in list order.
        for (int index = 0; index < work.Rewards.Count; index++)
        {
            RunReward item = work.Rewards.Items[index];
            if ((item.Flags & RunRewardFlags.Populated) == 0
                || item.Type == RunRewardType.Gold)
                PopulateReward(ref work, table, room, index);
        }

        ApplyRewardModifiers(ref work, table, room);
        SortRewards(ref work);
        BeforeCombatRewardOffered(ref work);
        work.Rewards.Generated = 1;
        work.Rewards.Stage = RunRewardStage.List;
        work.Canonicalize();
        state = work;
    }

    /// <summary>
    /// Rejects if any current listener overrides a reward-flow hook the Core does not implement.
    /// The listener set is the one <c>RunState.IterateHookListeners(null)</c> walks: deck cards,
    /// their enchantments, non-melted relics and held potions.
    /// </summary>
    private static void RequireSupportedListeners(in RunState state, ContentTable table)
    {
        for (int index = 0; index < state.Deck.Count; index++)
        {
            RunCard card = state.Deck.Cards[index];
            RequireSupportedMask(table.CardRewardHooks[card.Id],
                $"卡牌 {Registry.Card(card.Id).NativeId}");
            if (card.HasEnchantment != 0)
                RequireSupportedMask(table.EnchantmentRewardHooks[card.EnchantmentId],
                    $"附魔 {Registry.Enchantment(card.EnchantmentId).NativeId}");
        }
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            ContentRelicRewardFacts? facts = table.RelicRewardFacts[relic.Id]
                ?? throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
                    $"遗物 {Registry.Relic(relic.Id).NativeId} 没有奖励事实。");
            RequireSupportedMask(facts.RewardHooks,
                $"遗物 {Registry.Relic(relic.Id).NativeId}");
        }
        for (int slot = 0; slot < state.Player.Potions.Count; slot++)
        {
            RunPotion potion = state.Player.Potions.Items[slot];
            if (potion.Present != 0)
                RequireSupportedMask(table.PotionRewardHooks[potion.Id],
                    $"药水 {Registry.Potion(potion.Id).NativeId}");
        }
    }

    private static void RequireSupportedMask(in ContentRewardHookMask mask, string what)
    {
        uint unsupported = mask.Unsupported;
        if (unsupported == 0)
            return;
        throw new ContentUnavailableException(RunRewardReason.RewardHookListener,
            $"{what} 重写了未实现的奖励 Hook {(ContentRewardHook)unsupported}。");
    }

    private static void AddDefaultRewards(ref RunState state, ContentTable table, in RunRoom room,
        ContentEncounter encounter)
    {
        switch (room.RoomType)
        {
            case RunRoomType.Monster:
                if (room.GoldProportion > 0f)
                {
                    AddGold(ref state,
                        (int)Math.Round((float)encounter.MinGold(state.Ascension) * room.GoldProportion),
                        (int)Math.Round((float)encounter.MaxGold(state.Ascension) * room.GoldProportion));
                }
                RollPotion(ref state, table, room.RoomType);
                AddCardReward(ref state, 3, RaritySource(room.RoomType), fromCombat: true);
                break;
            case RunRoomType.Elite:
                AddGold(ref state, encounter.MinGold(state.Ascension),
                    encounter.MaxGold(state.Ascension));
                RollPotion(ref state, table, room.RoomType);
                AddCardReward(ref state, 3, RaritySource(room.RoomType), fromCombat: true);
                AddRelicReward(ref state);
                break;
            case RunRoomType.Boss:
                AddGold(ref state, encounter.MinGold(state.Ascension),
                    encounter.MaxGold(state.Ascension));
                RollPotion(ref state, table, room.RoomType);
                AddCardReward(ref state, 3, RaritySource(room.RoomType), fromCombat: true);
                break;
            default:
                throw new ContentUnavailableException(ContentReason.UnsupportedRoom,
                    $"房间 {room.RoomType} 不产生战后奖励。");
        }
    }

    private static bool TryAddTutorial(ref RunState state, ContentTable table, in RunRoom room,
        ContentEncounter encounter)
    {
        ContentTutorialRewards tutorial = table.TutorialRewards;
        if (state.Unlock.NumberOfRuns != 0 || EpochUnlockCount(in state) != 0
            || RunCatalog.CharacterName(state.Player.Character) != "IRONCLAD")
        {
            return false;
        }
        if (room.RoomType == RunRoomType.Monster)
        {
            int monsters = CountHistoryRooms(in state, RunRoomType.Monster, null);
            if (monsters > tutorial.Monsters.Length || monsters < 1)
                return false;
            ContentTutorialCardOffer offer = tutorial.Monsters[monsters - 1];
            AddGold(ref state, tutorial.MonsterGoldMin, tutorial.MonsterGoldMax);
            if (offer.Potion != ContentTutorialCardOffer.NoPotion)
                AddPotionModel(ref state, offer.Potion);
            AddManualCardReward(ref state, offer.Cards, RoomRarityOdds.Regular,
                fromCombat: false);
            return true;
        }
        if (room.RoomType == RunRoomType.Elite)
        {
            int elites = CountHistoryRooms(in state, null, RunMapPointType.Elite);
            ContentTutorialCardOffer? offer = elites switch
            {
                1 => tutorial.EliteFirst,
                2 => tutorial.EliteSecond,
                _ => null,
            };
            if (offer == null)
                return false;
            AddGold(ref state, encounter.MinGold(state.Ascension),
                encounter.MaxGold(state.Ascension));
            if (offer.Potion != ContentTutorialCardOffer.NoPotion)
                AddPotionModel(ref state, offer.Potion);
            ushort relic = elites == 1 ? tutorial.EliteFirstRelic : tutorial.EliteSecondRelic;
            AddTutorialRelic(ref state, relic);
            AddManualCardReward(ref state, offer.Cards, RoomRarityOdds.Regular,
                fromCombat: false);
            return true;
        }
        if (room.RoomType == RunRoomType.Boss)
        {
            int bosses = CountHistoryRooms(in state, null, RunMapPointType.Boss);
            if (bosses != 1)
                return false;
            AddGold(ref state, encounter.MinGold(state.Ascension),
                encounter.MaxGold(state.Ascension));
            AddManualCardReward(ref state, tutorial.BossFirst.Cards,
                RoomRarityOdds.Regular, fromCombat: false);
            return true;
        }
        return false;
    }

    private static int EpochUnlockCount(in RunState state)
    {
        int count = 0;
        for (int epoch = 0; epoch < RunCatalog.EpochCount; epoch++)
        {
            if ((state.Unlock.Epochs[epoch >> 6] & (1UL << (epoch & 63))) != 0)
                count++;
        }
        return count;
    }

    private static int CountHistoryRooms(in RunState state, RunRoomType? roomType,
        RunMapPointType? pointType)
    {
        int count = 0;
        for (int index = 0; index < state.History.Count; index++)
        {
            RunHistoryEntry entry = state.History.Entries[index];
            if (pointType is { } wanted)
            {
                if (entry.MapPointType == wanted)
                    count++;
                continue;
            }
            for (int room = 0; room < entry.RoomCount; room++)
            {
                if (entry.Rooms[room].RoomType == roomType)
                    count++;
            }
        }
        return count;
    }

    private static RoomRarityOdds RaritySource(RunRoomType roomType) => roomType switch
    {
        RunRoomType.Monster => RoomRarityOdds.Regular,
        RunRoomType.Elite => RoomRarityOdds.Elite,
        RunRoomType.Boss => RoomRarityOdds.Boss,
        _ => RoomRarityOdds.Regular,
    };

    private static void AddGold(ref RunState state, int min, int max)
    {
        // WithRewardsFromRoom only appends the reward; GenerateWithoutOffering draws later, after
        // the potion roll. Keeping Amount -1 until Populate preserves that RNG order.
        RunReward reward = default;
        reward.Type = RunRewardType.Gold;
        reward.Amount = min;
        reward.AmountMax = max;
        state.AddReward(reward);
    }

    private static void AddGoldAmount(ref RunState state, int amount, bool stolenBack)
    {
        RunReward reward = default;
        reward.Type = RunRewardType.Gold;
        reward.Amount = amount;
        reward.AmountMax = amount;
        reward.Flags = RunRewardFlags.Populated
            | (stolenBack ? RunRewardFlags.GoldStolenBack : RunRewardFlags.None);
        state.AddReward(reward);
    }

    private static void AddPotionModel(ref RunState state, ushort potion)
    {
        RunReward reward = default;
        reward.Type = RunRewardType.Potion;
        reward.ModelId = potion;
        reward.Flags = RunRewardFlags.Populated;
        state.AddReward(reward);
    }

    private static void AddPotionReward(ref RunState state)
    {
        RunReward reward = default;
        reward.Type = RunRewardType.Potion;
        state.AddReward(reward);
    }

    private static void AddCardReward(ref RunState state, byte count, RoomRarityOdds source,
        bool fromCombat)
    {
        RunReward reward = default;
        reward.Type = RunRewardType.Card;
        reward.CardCount = count;
        reward.RaritySource = (byte)source;
        reward.Flags = fromCombat ? RunRewardFlags.CardFromCombat : RunRewardFlags.None;
        state.AddReward(reward);
    }

    private static void AddManualCardReward(ref RunState state, ushort[] cards,
        RoomRarityOdds source, bool fromCombat)
    {
        byte start = state.ReserveRewardCards(cards.Length);
        for (int index = 0; index < cards.Length; index++)
        {
            RunCard card = default;
            card.Id = cards[index];
            state.SetRewardCard(start + index, card);
        }
        RunReward reward = default;
        reward.Type = RunRewardType.Card;
        reward.OptionStart = start;
        reward.OptionCount = checked((byte)cards.Length);
        reward.CardCount = checked((byte)cards.Length);
        reward.RaritySource = (byte)source;
        reward.Flags = RunRewardFlags.ManualCards
            | (fromCombat ? RunRewardFlags.CardFromCombat : RunRewardFlags.None);
        state.AddReward(reward);
    }

    private static void AddRelicReward(ref RunState state)
    {
        RunReward reward = default;
        reward.Type = RunRewardType.Relic;
        state.AddReward(reward);
    }

    private static void AddTutorialRelic(ref RunState state, ushort relic)
    {
        RunReward reward = default;
        reward.Type = RunRewardType.Relic;
        reward.Flags = RunRewardFlags.Populated;
        reward.ModelId = BagContains(in state, relic) ? relic : (ushort)0;
        state.AddReward(reward);
    }

    private static void AddExtras(ref RunState state, ReadOnlySpan<RunReward> extras,
        ReadOnlySpan<RunCard> extraCards)
    {
        int cardIndex = 0;
        for (int index = 0; index < extras.Length; index++)
        {
            RunReward reward = extras[index];
            if (reward.Type is RunRewardType.Card or RunRewardType.SpecialCard
                && reward.OptionCount != 0)
            {
                byte start = state.ReserveRewardCards(reward.OptionCount);
                for (int option = 0; option < reward.OptionCount; option++)
                    state.SetRewardCard(start + option, extraCards[cardIndex + option]);
                cardIndex += reward.OptionCount;
                reward.OptionStart = start;
            }
            else
            {
                reward.OptionStart = 0;
                reward.OptionCount = 0;
            }
            state.AddReward(reward);
        }
    }

    private static void RollPotion(ref RunState state, ContentTable table, RunRoomType roomType)
    {
        if (HasRelic(in state, "WHITE_BEAST_STATUE"))
        {
            AddPotionReward(ref state);
            return;
        }
        ContentRewardRules rules = table.RewardRules;
        float current = state.PlayerOdds.PotionReward;
        float bonus = roomType == RunRoomType.Elite
            ? rules.PotionEliteBonus * rules.PotionEliteBonusScale
            : 0f;
        ref RngState rng = ref RewardsRng(ref state);
        float roll = rng.NextFloat(1f);
        if (roll < current + bonus)
        {
            state.PlayerOdds.PotionReward = current - rules.PotionPityStep;
            AddPotionReward(ref state);
        }
        else
        {
            state.PlayerOdds.PotionReward = current + rules.PotionPityStep;
        }
    }

    private static void PopulateReward(ref RunState state, ContentTable table, in RunRoom room,
        int rewardIndex)
    {
        RunReward reward = state.Rewards.Items[rewardIndex];
        switch (reward.Type)
        {
            case RunRewardType.Gold:
            {
                ref RngState rng = ref RewardsRng(ref state);
                reward.Amount = rng.NextInt(reward.Amount, reward.AmountMax + 1);
                reward.AmountMax = reward.Amount;
                reward.Flags |= RunRewardFlags.Populated;
                state.Rewards.Items[rewardIndex] = reward;
                break;
            }
            case RunRewardType.Potion:
            {
                reward.ModelId = PullPotion(ref state, table);
                reward.Flags |= RunRewardFlags.Populated;
                state.Rewards.Items[rewardIndex] = reward;
                break;
            }
            case RunRewardType.Relic:
            {
                reward.ModelId = PullRelic(ref state, table);
                reward.Flags |= RunRewardFlags.Populated;
                state.Rewards.Items[rewardIndex] = reward;
                break;
            }
            case RunRewardType.Card:
                PopulateCardReward(ref state, table, room, rewardIndex);
                break;
            case RunRewardType.SpecialCard:
            case RunRewardType.CardRemoval:
                reward.Flags |= RunRewardFlags.Populated;
                state.Rewards.Items[rewardIndex] = reward;
                break;
            default:
                throw new UnsupportedException($"未知奖励类型 {(byte)reward.Type}。");
        }
    }

    private static ushort PullPotion(ref RunState state, ContentTable table)
    {
        ContentCharacter character = table.Character(state.Player.Character);
        List<ContentPotionEntry> pool = [];
        for (int index = 0; index < character.PotionPool.Length; index++)
        {
            ContentPotionEntry entry = character.PotionPool[index];
            if (EpochsRevealed(in state, entry.RequiredEpochs))
                pool.Add(entry);
        }
        for (int index = 0; index < table.SharedPotionPool.Length; index++)
        {
            ContentPotionEntry entry = table.SharedPotionPool[index];
            if (EpochsRevealed(in state, entry.RequiredEpochs))
                pool.Add(entry);
        }
        if (pool.Count == 0)
            throw new ContentUnavailableException(ContentReason.UnsupportedPotion, "药水池为空。");
        ContentRewardRules rules = table.RewardRules;
        ref RngState rng = ref RewardsRng(ref state);
        float roll = rng.NextFloat(1f);
        PotionRarity rarity = roll <= rules.PotionRareThreshold ? PotionRarity.Rare
            : roll <= rules.PotionUncommonThreshold ? PotionRarity.Uncommon : PotionRarity.Common;
        List<int> candidates = [];
        for (int index = 0; index < pool.Count; index++)
        {
            if (RarityOf(table, pool[index]) == rarity)
                candidates.Add(index);
        }
        if (candidates.Count == 0)
            throw new ContentUnavailableException(ContentReason.UnsupportedPotion,
                $"药水池没有稀有度 {rarity} 的候选。");
        int pick = candidates[rng.NextInt(candidates.Count)];
        ContentPotionEntry chosen = pool[pick];
        if (chosen.PotionId == ContentPotionEntry.NoPotion)
            throw new ContentUnavailableException(ContentReason.UnsupportedPotion,
                $"药水 {chosen.NativeId} 没有 Blitz 定义。");
        return chosen.PotionId;
    }

    private static PotionRarity RarityOf(ContentTable table, ContentPotionEntry entry)
    {
        if (entry.PotionId == ContentPotionEntry.NoPotion)
            throw new ContentUnavailableException(ContentReason.UnsupportedPotion,
                $"药水 {entry.NativeId} 没有 Blitz 定义。");
        // ContentPotionEntry does not carry rarity; read it from the potion stats table.
        return table.Potions[entry.PotionId] is { } stats
            ? stats.Rarity
            : throw new ContentUnavailableException(ContentReason.UnsupportedPotion,
                $"药水 {entry.NativeId} 没有属性。");
    }

    private static ushort PullRelic(ref RunState state, ContentTable table)
    {
        ref RngState rng = ref RewardsRng(ref state);
        ContentRewardRules rules = table.RewardRules;
        float roll = rng.NextFloat(1f);
        RunRelicRarity rarity = roll < rules.RelicCommonThreshold ? RunRelicRarity.Common
            : roll < rules.RelicUncommonThreshold ? RunRelicRarity.Uncommon : RunRelicRarity.Rare;
        RemoveDisallowedRelics(ref state, table);
        int segment = (int)rarity;
        if (state.PlayerBag.Segments[segment].Count == 0
            && state.PlayerBag.RefreshAllowed != 0)
        {
            throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
                "玩家遗物袋可刷新，本批未实现刷新。");
        }
        while (state.PlayerBag.Segments[(int)rarity].Count == 0)
        {
            rarity = rarity switch
            {
                RunRelicRarity.Shop => RunRelicRarity.Common,
                RunRelicRarity.Common => RunRelicRarity.Uncommon,
                RunRelicRarity.Uncommon => RunRelicRarity.Rare,
                _ => RunRelicRarity.None,
            };
            segment = (int)rarity;
            if (rarity == RunRelicRarity.None)
                break;
        }
        if (rarity == RunRelicRarity.None || state.PlayerBag.Segments[segment].Count == 0)
        {
            if (!Registry.TryRelic("CIRCLET", out ushort circlet))
                throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
                    "遗物袋耗尽，且没有 CIRCLET 定义。");
            RemoveFromSharedBag(ref state, circlet);
            return circlet;
        }
        RunRelicBagSegment bag = state.PlayerBag.Segments[segment];
        ushort id = bag.Ids[0];
        for (int index = 0; index + 1 < bag.Count; index++)
            bag.Ids[index] = bag.Ids[index + 1];
        bag.Count--;
        bag.Ids[bag.Count] = 0;
        state.PlayerBag.Segments[segment] = bag;
        RemoveFromSharedBag(ref state, id);
        return id;
    }

    internal static void RemoveDisallowedRelics(ref RunState state, ContentTable table)
    {
        for (int rarity = 0; rarity < RunLimits.RelicRarities; rarity++)
        {
            RunRelicBagSegment segment = state.PlayerBag.Segments[rarity];
            int write = 0;
            for (int read = 0; read < segment.Count; read++)
            {
                ushort id = segment.Ids[read];
                ContentRelicRewardFacts? facts = table.RelicRewardFacts[id];
                if (facts is { Allowed: ContentRelicAllowedKind.Unsupported })
                {
                    throw new ContentUnavailableException(RunRewardReason.RelicAllowedHook,
                        $"遗物 {Registry.Relic(id).NativeId} 重写了 IsAllowed，本批未映射。");
                }
                bool allowed = facts == null || RelicAllowed(in state, facts.Allowed);
                if (!allowed)
                    continue;
                segment.Ids[write++] = id;
            }
            for (int index = write; index < segment.Count; index++)
                segment.Ids[index] = 0;
            segment.Count = checked((ushort)write);
            state.PlayerBag.Segments[rarity] = segment;
        }
    }

    private static bool RelicAllowed(in RunState state, ContentRelicAllowedKind kind) => kind switch
    {
        ContentRelicAllowedKind.Always => true,
        ContentRelicAllowedKind.BeforeAct3TreasureChest => state.History.Count < 41,
        ContentRelicAllowedKind.SinglePlayerOnly => true,
        ContentRelicAllowedKind.NotFirstRunIroncladBeforeAct3 =>
            !(state.Unlock.NumberOfRuns == 0
                && RunCatalog.CharacterName(state.Player.Character) == "IRONCLAD")
            && state.History.Count < 41,
        _ => false,
    };

    internal static void RemoveFromSharedBag(ref RunState state, ushort id)
    {
        for (int rarity = 0; rarity < RunLimits.RelicRarities; rarity++)
        {
            RunRelicBagSegment segment = state.SharedBag.Segments[rarity];
            int write = 0;
            for (int read = 0; read < segment.Count; read++)
            {
                if (segment.Ids[read] != id)
                    segment.Ids[write++] = segment.Ids[read];
            }
            for (int index = write; index < segment.Count; index++)
                segment.Ids[index] = 0;
            segment.Count = checked((ushort)write);
            state.SharedBag.Segments[rarity] = segment;
        }
    }

    private static void PopulateCardReward(ref RunState state, ContentTable table, in RunRoom room,
        int rewardIndex)
    {
        RunReward reward = state.Rewards.Items[rewardIndex];
        bool manual = (reward.Flags & RunRewardFlags.ManualCards) != 0;
        byte count = reward.CardCount;
        if (manual)
        {
            count = reward.OptionCount;
            for (int option = 0; option < count; option++)
            {
                ref RunCard card = ref state.Rewards.Choices[reward.OptionStart + option];
                card.Properties = table.Card(card.Id).DefaultProperties[card.Upgrade];
            }
        }
        else
        {
            byte start = ReserveCardOptions(ref state);
            HashSet<ushort> blacklist = [];
            List<ContentPoolEntry> pools = BuildCardPools(ref state, table, reward);
            for (int option = 0; option < count; option++)
            {
                RunCard card = GenerateCard(ref state, table, pools, blacklist,
                    (RoomRarityOdds)reward.RaritySource, useBaseOdds: false);
                blacklist.Add(card.Id);
                state.SetRewardCard(start + option, card);
            }
            reward.OptionStart = start;
            reward.OptionCount = count;
            state.Rewards.Items[rewardIndex] = reward;
        }
        ApplyCardOptionHooks(ref state, table, rewardIndex, count);
        reward = state.Rewards.Items[rewardIndex];
        reward.Flags |= RunRewardFlags.Populated;
        state.Rewards.Items[rewardIndex] = reward;
    }

    private static byte ReserveCardOptions(ref RunState state)
    {
        int end = 0;
        for (int index = 0; index < state.Rewards.Count; index++)
        {
            RunReward item = state.Rewards.Items[index];
            end = Math.Max(end, item.OptionStart + item.OptionCount);
        }
        if (end + 1 > RunLimits.RewardCardOptions)
            throw new CapacityException("RunState.RewardCardOptions", RunLimits.RewardCardOptions);
        return checked((byte)end);
    }

    private static List<ContentPoolEntry> BuildCardPools(ref RunState state, ContentTable table,
        in RunReward reward)
    {
        ContentCharacter character = table.Character(state.Player.Character);
        List<ContentCardPool> pools = [character.CardPool];
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            string nativeId = Registry.Relic(relic.Id).NativeId;
            switch (nativeId)
            {
                case "DINGY_RUG":
                    if (!pools.Contains(table.ColorlessPool))
                        pools.Add(table.ColorlessPool);
                    break;
                case "PRISMATIC_GEM":
                    List<ContentCardPool> prismatic = [];
                    for (int order = 0; order < table.CharacterOrder.Length; order++)
                    {
                        ContentCharacter other = table.Character(table.CharacterOrder[order]);
                        if (EpochsRevealed(in state, other.UnlockEpochs))
                            prismatic.Add(other.CardPool);
                    }
                    for (int pool = 0; pool < pools.Count; pool++)
                    {
                        if (!prismatic.Contains(pools[pool]))
                            prismatic.Add(pools[pool]);
                    }
                    pools = prismatic;
                    break;
            }
        }
        List<ContentPoolEntry> entries = [];
        for (int pool = 0; pool < pools.Count; pool++)
            entries.AddRange(pools[pool].Cards);
        return entries;
    }

    private static RunCard GenerateCard(ref RunState state, ContentTable table,
        List<ContentPoolEntry> pools, HashSet<ushort> blacklist, RoomRarityOdds source,
        bool useBaseOdds)
    {
        List<ContentPoolEntry> candidates = [];
        HashSet<CardRarity> allowed = [];
        for (int index = 0; index < pools.Count; index++)
        {
            ContentPoolEntry entry = pools[index];
            if (!EpochsRevealed(in state, entry.RequiredEpochs) || entry.MultiplayerOnly)
                continue;
            if (blacklist.Contains(entry.CardId))
                continue;
            candidates.Add(entry);
            allowed.Add(entry.Rarity);
        }
        if (candidates.Count == 0)
            throw new ContentUnavailableException(ContentReason.UnsupportedCard,
                "卡牌奖励没有候选卡。");
        ContentRewardRules rules = table.RewardRules;
        bool scarcity = state.Ascension >= table.AscensionRules.ScarcityLevel;
        ContentCardRarityOdds odds = rules.Odds(source, scarcity);
        ref RngState rng = ref RewardsRng(ref state);
        float roll = rng.NextFloat(1f);
        CardRarity rarity = RollRarity(ref state, rules, odds, source, roll, useBaseOdds, scarcity);
        rarity = NextAllowedRarity(rarity, allowed);
        if (rarity == CardRarity.None)
            throw new ContentUnavailableException(ContentReason.UnsupportedCard,
                "卡牌奖励的稀有度无法落入卡池。");
        List<int> items = [];
        for (int index = 0; index < candidates.Count; index++)
        {
            if (candidates[index].Rarity == rarity)
                items.Add(index);
        }
        if (items.Count == 0)
            throw new ContentUnavailableException(ContentReason.UnsupportedCard,
                $"卡牌奖励的稀有度 {rarity} 没有候选。");
        int pick = items[rng.NextInt(items.Count)];
        ContentPoolEntry chosen = candidates[pick];
        if (chosen.CardId == ContentPoolEntry.NoCard)
            throw new ContentUnavailableException(ContentReason.UnsupportedCard,
                $"卡牌 {chosen.NativeId} 没有 Blitz 定义。");
        RunCard card = default;
        card.Id = chosen.CardId;
        card.Properties = table.Card(card.Id).DefaultProperties[0];
        CardStats stats = table.Card(card.Id, 0);
        decimal upgradeChance = stats.Rarity == CardRarity.Rare
            ? 0m
            : (decimal)state.ActIndex
                * (decimal)(scarcity ? rules.ScarcityUpgradeScaling : rules.UpgradeScaling);
        float upgradeRoll = rng.NextFloat(1f);
        if (stats.MaxUpgradeLevel > 0 && (decimal)upgradeRoll <= upgradeChance)
        {
            card.Upgrade = 1;
            card.Properties = table.Card(card.Id).DefaultProperties[1];
        }
        return card;
    }

    private static CardRarity RollRarity(ref RunState state, ContentRewardRules rules,
        ContentCardRarityOdds odds, RoomRarityOdds source, float roll, bool useBaseOdds,
        bool scarcity)
    {
        float offset = source == RoomRarityOdds.Boss ? 0f : state.PlayerOdds.CardRarity;
        if (useBaseOdds)
            offset = 0f;
        CardRarity rarity;
        if (roll < odds.Rare + offset)
            rarity = CardRarity.Rare;
        else if (roll < odds.Uncommon + odds.Rare + offset)
            rarity = CardRarity.Uncommon;
        else
            rarity = CardRarity.Common;
        if (!useBaseOdds)
        {
            if (rarity == CardRarity.Rare)
                state.PlayerOdds.CardRarity = rules.RarityOffsetBase;
            else
            {
                float growth = scarcity ? rules.ScarcityRarityGrowth : rules.RarityGrowth;
                state.PlayerOdds.CardRarity = Math.Min(
                    state.PlayerOdds.CardRarity + growth, rules.RarityOffsetMax);
            }
        }
        return rarity;
    }

    private static CardRarity NextAllowedRarity(CardRarity rarity, HashSet<CardRarity> allowed)
    {
        CardRarity current = rarity;
        while (!allowed.Contains(current) && current != CardRarity.None)
        {
            current = current switch
            {
                CardRarity.Basic => CardRarity.Common,
                CardRarity.Common => CardRarity.Uncommon,
                CardRarity.Uncommon => CardRarity.Rare,
                CardRarity.Rare => CardRarity.Common,
                _ => CardRarity.None,
            };
            if (current == rarity)
                return CardRarity.None;
        }
        return current;
    }

    private static void ApplyCardOptionHooks(ref RunState state, ContentTable table,
        int rewardIndex, byte count)
    {
        RunReward reward = state.Rewards.Items[rewardIndex];
        // First pass: Lasting Candy appends one Power card.
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if (Registry.Relic(relic.Id).NativeId != "LASTING_CANDY"
                || (relic.Flags & RunRelicFlags.Melted) != 0)
            {
                continue;
            }
            int seen = ReadRelicInt(in relic, "CombatRewardsSeen");
            if (seen <= 0 || seen % 2 == 0
                || (reward.Flags & RunRewardFlags.CardFromCombat) == 0)
            {
                continue;
            }
            if (!TryAppendLastingCandy(ref state, table, rewardIndex, count))
                continue;
            reward = state.Rewards.Items[rewardIndex];
            count = reward.OptionCount;
        }
        // Late pass: eggs, Lava Lamp and the unsupported enchantment hooks.
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            string nativeId = Registry.Relic(relic.Id).NativeId;
            switch (nativeId)
            {
                case "MOLTEN_EGG":
                    UpgradeOptionCards(ref state, table, rewardIndex, count, CardType.Attack);
                    break;
                case "FROZEN_EGG":
                    UpgradeOptionCards(ref state, table, rewardIndex, count, CardType.Power);
                    break;
                case "TOXIC_EGG":
                    UpgradeOptionCards(ref state, table, rewardIndex, count, CardType.Skill);
                    break;
                case "LAVA_LAMP":
                    if (!ReadRelicBool(in relic, "TookDamageThisCombat"))
                        UpgradeOptionCards(ref state, table, rewardIndex, count, null);
                    break;
            }
        }
        reward = state.Rewards.Items[rewardIndex];
        reward.OptionCount = count;
        state.Rewards.Items[rewardIndex] = reward;
    }

    private static void UpgradeOptionCards(ref RunState state, ContentTable table, int rewardIndex,
        int count, CardType? type)
    {
        RunReward reward = state.Rewards.Items[rewardIndex];
        for (int option = 0; option < count; option++)
        {
            ref RunCard card = ref state.Rewards.Choices[reward.OptionStart + option];
            CardStats stats = table.Card(card.Id, card.Upgrade);
            if (type is { } wanted && stats.Type != wanted)
                continue;
            if (card.Upgrade < stats.MaxUpgradeLevel)
            {
                card.Upgrade++;
                card.Properties = table.Card(card.Id).DefaultProperties[card.Upgrade];
            }
        }
    }

    private static bool TryAppendLastingCandy(ref RunState state, ContentTable table,
        int rewardIndex, byte count)
    {
        RunReward reward = state.Rewards.Items[rewardIndex];
        HashSet<ushort> present = [];
        for (int option = 0; option < count; option++)
            present.Add(state.Rewards.Choices[reward.OptionStart + option].Id);
        List<ContentPoolEntry> pools = BuildCardPools(ref state, table, reward);
        List<ContentPoolEntry> filtered = FilterLastingCandy(in state, pools, present, false);
        if (filtered.Count == 0)
            filtered = FilterLastingCandy(in state, pools, present, true);
        if (filtered.Count == 0)
            return false;
        RunCard card = GenerateCardFromFiltered(ref state, table, filtered,
            (RoomRarityOdds)reward.RaritySource, useBaseOdds: true);
        if (count >= RunLimits.RewardCardOptions - reward.OptionStart)
            throw new CapacityException("RunState.RewardCardOptions", RunLimits.RewardCardOptions);
        state.SetRewardCard(reward.OptionStart + count, card);
        reward.OptionCount = checked((byte)(count + 1));
        reward.CardCount = reward.OptionCount;
        state.Rewards.Items[rewardIndex] = reward;
        return true;
    }

    private static List<ContentPoolEntry> FilterLastingCandy(in RunState state,
        List<ContentPoolEntry> pools, HashSet<ushort> present, bool allowDupes)
    {
        List<ContentPoolEntry> result = [];
        for (int index = 0; index < pools.Count; index++)
        {
            ContentPoolEntry entry = pools[index];
            if (!EpochsRevealed(in state, entry.RequiredEpochs) || entry.MultiplayerOnly)
                continue;
            if (entry.Type != CardType.Power)
                continue;
            if (entry.CardId == ContentPoolEntry.NoCard)
                throw new ContentUnavailableException(ContentReason.UnsupportedCard,
                    $"卡牌 {entry.NativeId} 没有 Blitz 定义。");
            if (!allowDupes && present.Contains(entry.CardId))
                continue;
            result.Add(entry);
        }
        return result;
    }

    private static RunCard GenerateCardFromFiltered(ref RunState state, ContentTable table,
        List<ContentPoolEntry> candidates, RoomRarityOdds source, bool useBaseOdds)
    {
        HashSet<CardRarity> allowed = [];
        for (int index = 0; index < candidates.Count; index++)
            allowed.Add(candidates[index].Rarity);
        ContentRewardRules rules = table.RewardRules;
        bool scarcity = state.Ascension >= table.AscensionRules.ScarcityLevel;
        ContentCardRarityOdds odds = rules.Odds(source, scarcity);
        ref RngState rng = ref RewardsRng(ref state);
        float roll = rng.NextFloat(1f);
        CardRarity rarity = RollRarity(ref state, rules, odds, source, roll, useBaseOdds, scarcity);
        rarity = NextAllowedRarity(rarity, allowed);
        if (rarity == CardRarity.None)
            throw new ContentUnavailableException(ContentReason.UnsupportedCard,
                "Lasting Candy 的稀有度无法落入卡池。");
        List<int> items = [];
        for (int index = 0; index < candidates.Count; index++)
        {
            if (candidates[index].Rarity == rarity)
                items.Add(index);
        }
        if (items.Count == 0)
            throw new ContentUnavailableException(ContentReason.UnsupportedCard,
                "Lasting Candy 的稀有度没有候选。");
        int pick = items[rng.NextInt(items.Count)];
        ContentPoolEntry chosen = candidates[pick];
        RunCard card = default;
        card.Id = chosen.CardId;
        card.Properties = table.Card(card.Id).DefaultProperties[0];
        CardStats stats = table.Card(card.Id, 0);
        decimal upgradeChance = stats.Rarity == CardRarity.Rare
            ? 0m
            : (decimal)state.ActIndex
                * (decimal)(scarcity ? rules.ScarcityUpgradeScaling : rules.UpgradeScaling);
        float upgradeRoll = rng.NextFloat(1f);
        if (stats.MaxUpgradeLevel > 0 && (decimal)upgradeRoll <= upgradeChance)
        {
            card.Upgrade = 1;
            card.Properties = table.Card(card.Id).DefaultProperties[1];
        }
        return card;
    }

    private static void ApplyRewardModifiers(ref RunState state, ContentTable table,
        in RunRoom room)
    {
        int before = state.Rewards.Count;
        List<int> modifiers = [];
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            string nativeId = Registry.Relic(relic.Id).NativeId;
            bool modified = nativeId switch
            {
                "AMETHYST_AUBERGINE" => AddAmethyst(ref state, table, room),
                "BLACK_STAR" => AddBlackStar(ref state, room),
                "LAVA_ROCK" => AddLavaRock(ref state, table, room, ref state.Relics.Items[index]),
                "PRAYER_WHEEL" => AddPrayerWheel(ref state, table, room),
                "WHITE_STAR" => AddWhiteStar(ref state, table, room),
                "WONGOS_MYSTERY_TICKET" =>
                    AddWongos(ref state, table, room, ref state.Relics.Items[index]),
                _ => false,
            };
            if (modified)
                modifiers.Add(index);
        }
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            if (Registry.Relic(relic.Id).NativeId == "DRIFTWOOD")
            {
                for (int reward = 0; reward < state.Rewards.Count; reward++)
                {
                    if (state.Rewards.Items[reward].Type == RunRewardType.Card)
                        state.Rewards.Items[reward].Flags |= RunRewardFlags.CardCanReroll;
                }
                modifiers.Add(index);
            }
        }
        for (int index = before; index < state.Rewards.Count; index++)
        {
            if ((state.Rewards.Items[index].Flags & RunRewardFlags.Populated) == 0)
                PopulateReward(ref state, table, room, index);
        }
        for (int index = 0; index < modifiers.Count; index++)
        {
            ref RunRelic relic = ref state.Relics.Items[modifiers[index]];
            string nativeId = Registry.Relic(relic.Id).NativeId;
            if (nativeId == "WONGOS_MYSTERY_TICKET")
                SetRelicBool(ref relic, "GaveRelic", true);
        }
    }

    private static bool AddAmethyst(ref RunState state, ContentTable table, in RunRoom room)
    {
        if (room.RoomType is not (RunRoomType.Monster or RunRoomType.Elite or RunRoomType.Boss))
            return false;
        ushort id = RelicId(state, "AMETHYST_AUBERGINE");
        AddGoldAmount(ref state, IntVar(table, id, "Gold"), stolenBack: false);
        return true;
    }

    private static bool AddBlackStar(ref RunState state, in RunRoom room)
    {
        if (room.RoomType != RunRoomType.Elite)
            return false;
        AddRelicReward(ref state);
        return true;
    }

    private static bool AddWhiteStar(ref RunState state, ContentTable table, in RunRoom room)
    {
        if (room.RoomType != RunRoomType.Elite)
            return false;
        AddCardReward(ref state, 3, RoomRarityOdds.Boss, fromCombat: false);
        return true;
    }

    private static bool AddPrayerWheel(ref RunState state, ContentTable table, in RunRoom room)
    {
        if (room.RoomType != RunRoomType.Monster)
            return false;
        AddCardReward(ref state, 3, RoomRarityOdds.Regular, fromCombat: false);
        return true;
    }

    private static bool AddLavaRock(ref RunState state, ContentTable table, in RunRoom room,
        ref RunRelic relic)
    {
        if (room.RoomType != RunRoomType.Boss || state.ActIndex != 0
            || ReadRelicBool(in relic, "HasTriggered"))
        {
            return false;
        }
        int count = IntVar(table, relic.Id, "Relics");
        for (int index = 0; index < count; index++)
            AddRelicReward(ref state);
        SetRelicBool(ref relic, "HasTriggered", true);
        relic.Status = RunRelicStatus.Disabled;
        return true;
    }

    private static bool AddWongos(ref RunState state, ContentTable table, in RunRoom room,
        ref RunRelic relic)
    {
        if (room.RoomType is not (RunRoomType.Monster or RunRoomType.Elite or RunRoomType.Boss)
            || ReadRelicBool(in relic, "GaveRelic"))
        {
            return false;
        }
        int remaining = 5 - ReadRelicInt(in relic, "CombatsFinished");
        if (remaining > 0)
            return false;
        int count = IntVar(table, relic.Id, "Repeat");
        for (int index = 0; index < count; index++)
            AddRelicReward(ref state);
        return true;
    }

    private static void BeforeCombatRewardOffered(ref RunState state)
    {
        bool hasCard = false;
        for (int index = 0; index < state.Rewards.Count; index++)
        {
            if (state.Rewards.Items[index].Type == RunRewardType.Card)
            {
                hasCard = true;
                break;
            }
        }
        if (!hasCard)
            return;
        for (int index = 0; index < state.Relics.Count; index++)
        {
            ref RunRelic relic = ref state.Relics.Items[index];
            if (Registry.Relic(relic.Id).NativeId != "LASTING_CANDY")
                continue;
            int seen = ReadRelicInt(in relic, "CombatRewardsSeen") + 1;
            SetRelicInt(ref relic, "CombatRewardsSeen", seen);
            relic.DisplayAmount = seen % 2;
        }
    }

    private static void SortRewards(ref RunState state)
    {
        Span<RunReward> items = state.Rewards.Items;
        int count = state.Rewards.Count;
        for (int index = 1; index < count; index++)
        {
            RunReward value = items[index];
            int position = index - 1;
            while (position >= 0
                && RewardSetIndex[(int)items[position].Type]
                    > RewardSetIndex[(int)value.Type])
            {
                items[position + 1] = items[position];
                position--;
            }
            items[position + 1] = value;
        }
    }

    internal static bool EpochsRevealed(in RunState state, ulong mask)
    {
        for (int epoch = 0; epoch < RunCatalog.EpochCount; epoch++)
        {
            if ((mask & (1UL << epoch)) != 0 && !state.HasEpoch((ushort)epoch))
                return false;
        }
        return true;
    }

    private static bool BagContains(in RunState state, ushort id)
    {
        for (int rarity = 0; rarity < RunLimits.RelicRarities; rarity++)
        {
            RunRelicBagSegment segment = state.PlayerBag.Segments[rarity];
            for (int index = 0; index < segment.Count; index++)
            {
                if (segment.Ids[index] == id)
                    return true;
            }
        }
        return false;
    }

    private static bool HasRelic(in RunState state, string nativeId)
        => RelicIdOrZero(in state, nativeId) != 0;

    private static ushort RelicId(in RunState state, string nativeId)
    {
        ushort id = RelicIdOrZero(in state, nativeId);
        return id != 0
            ? id
            : throw new InvalidDataException($"当前遗物栏没有 {nativeId}。");
    }

    private static ushort RelicIdOrZero(in RunState state, string nativeId)
    {
        for (int index = 0; index < state.Relics.Count; index++)
        {
            ushort id = state.Relics.Items[index].Id;
            if (Registry.Relic(id).NativeId == nativeId)
                return id;
        }
        return 0;
    }

    internal static int IntVar(ContentTable table, ushort relicId, string name)
        => checked((int)table.RelicVar(relicId, name));

    internal static int ReadRelicInt(in RunRelic relic, string name)
        => RunProperties.TryGetInt(relic.Properties, RunProperties.NameHash(name), out int value)
            ? value : 0;

    internal static bool ReadRelicBool(in RunRelic relic, string name)
        => RunProperties.TryGetBool(relic.Properties, RunProperties.NameHash(name), out bool value)
            && value;

    internal static void SetRelicInt(ref RunRelic relic, string name, int value)
        => RunProperties.SetInt(ref relic.Properties, RunProperties.NameHash(name), value,
            RunLimits.RelicPropertyBytes);

    internal static void SetRelicBool(ref RunRelic relic, string name, bool value)
        => RunProperties.SetBool(ref relic.Properties, RunProperties.NameHash(name), value,
            RunLimits.RelicPropertyBytes);

    private static ref RngState RewardsRng(ref RunState state)
        => ref state.Rng.Streams[(int)RunRngStream.Rewards];
    // ---------------------------------------------------------------- reward selection

    /// <summary>Take the reward named by a <see cref="RunDecisionOptionKind.Reward"/> option.</summary>
    public static void TakeReward(ref RunState state, ContentTable table, in RunDecisionOption option)
    {
        RequireSupportedListeners(in state, table);
        int index = option.Index;
        if ((uint)index >= state.Rewards.Count)
            throw new ContentUnavailableException(RunRewardReason.RewardIndex,
                $"奖励下标 {index} 超出 {state.Rewards.Count}。");
        RunReward reward = state.Rewards.Items[index];
        if (reward.Type != option.RewardType)
            throw new ContentUnavailableException(RunRewardReason.RewardState,
                $"奖励 {index} 是 {reward.Type}，动作说 {option.RewardType}。");
        switch (reward.Type)
        {
            case RunRewardType.Gold:
                RemoveRewardAt(ref state, index);
                GainGold(ref state, table, reward.Amount,
                    (reward.Flags & RunRewardFlags.GoldStolenBack) != 0);
                break;
            case RunRewardType.Potion:
                RequireSupportedMask(table.PotionRewardHooks[reward.ModelId],
                    $"药水 {Registry.Potion(reward.ModelId).NativeId}");
                TakePotionReward(ref state, index, table);
                break;
            case RunRewardType.Card:
                state.Rewards.Stage = RunRewardStage.Card;
                state.Rewards.ChoiceIndex = checked((byte)index);
                state.Rewards.ChoiceCount = reward.OptionCount;
                break;
            case RunRewardType.Relic:
                CheckRelicObtainable(ref state, table, reward.ModelId);
                RemoveRewardAt(ref state, index);
                ObtainRelic(ref state, table, reward.ModelId);
                break;
            case RunRewardType.SpecialCard:
                RemoveRewardAt(ref state, index);
                TakeSpecialCard(ref state, table, reward);
                break;
            case RunRewardType.CardRemoval:
                throw new ContentUnavailableException(RunRewardReason.RewardState,
                    "删牌奖励需要选牌界面，属于 R2d。");
            default:
                throw new ContentUnavailableException(RunRewardReason.RewardState,
                    $"未知奖励类型 {(byte)reward.Type}。");
        }
    }

    /// <summary>Choose one card of the currently open card reward.</summary>
    public static void ChooseCard(ref RunState state, ContentTable table, int cardOption,
        bool reroll)
    {
        if (reroll)
        {
            RerollCardReward(ref state, table);
            return;
        }
        RunReward reward = state.Rewards.Items[state.Rewards.ChoiceIndex];
        if ((uint)cardOption >= reward.OptionCount)
            throw new ContentUnavailableException(RunRewardReason.CardOption,
                $"卡牌选项 {cardOption} 超出 {reward.OptionCount}。");
        RunCard card = state.Rewards.Choices[reward.OptionStart + cardOption];
        RequireSupportedMask(table.CardRewardHooks[card.Id],
            $"卡牌 {Registry.Card(card.Id).NativeId}");
        if (card.HasEnchantment != 0)
            RequireSupportedMask(table.EnchantmentRewardHooks[card.EnchantmentId],
                $"附魔 {Registry.Enchantment(card.EnchantmentId).NativeId}");
        AddCardToDeck(ref state, table, card);
        RemoveRewardAt(ref state, state.Rewards.ChoiceIndex);
        state.Rewards.Stage = RunRewardStage.List;
        state.Rewards.ChoiceIndex = 0;
        state.Rewards.ChoiceCount = 0;
    }

    /// <summary>Discard one potion slot, then take the pending potion reward.</summary>
    public static void ChoosePotion(ref RunState state, ContentTable table, int slot)
    {
        if ((uint)slot >= state.Player.Potions.Count
            || state.Player.Potions.Items[slot].Present == 0)
        {
            throw new ContentUnavailableException(RunRewardReason.PotionSlot,
                $"药水槽 {slot} 为空。");
        }
        RunReward potionReward = state.Rewards.Items[state.Rewards.ChoiceIndex];
        RequireSupportedMask(table.PotionRewardHooks[potionReward.ModelId],
            $"药水 {Registry.Potion(potionReward.ModelId).NativeId}");
        int index = state.Rewards.ChoiceIndex;
        DiscardPotion(ref state, slot);
        TakePotionReward(ref state, index, table);
    }

    /// <summary>Skip the card or potion reward currently open.</summary>
    public static void SkipCurrentReward(ref RunState state)
    {
        if (state.Rewards.Stage is not (RunRewardStage.Card or RunRewardStage.PotionDiscard))
            throw new ContentUnavailableException(RunRewardReason.RewardState,
                "当前阶段没有可跳过的单条奖励。");
        RemoveRewardAt(ref state, state.Rewards.ChoiceIndex);
        state.Rewards.Stage = RunRewardStage.List;
        state.Rewards.ChoiceIndex = 0;
        state.Rewards.ChoiceCount = 0;
    }

    /// <summary>Leave the reward screen; every remaining reward is skipped.</summary>
    public static void Leave(ref RunState state)
    {
        state.Rewards.Stage = RunRewardStage.None;
        state.Rewards.Count = 0;
        state.Rewards.ChoiceIndex = 0;
        state.Rewards.ChoiceCount = 0;
        state.Rewards.Items = default;
        state.Rewards.Choices = default;
        state.Canonicalize();
    }

    public static void RerollCardReward(ref RunState state, ContentTable table)
    {
        RunReward reward = state.Rewards.Items[state.Rewards.ChoiceIndex];
        if ((reward.Flags & RunRewardFlags.CardCanReroll) == 0)
            throw new ContentUnavailableException(RunRewardReason.CardRewardAlternative,
                "该卡牌奖励没有 reroll 选项。");
        int start = reward.OptionStart;
        byte count = reward.CardCount;
        HashSet<ushort> blacklist = [];
        for (int option = 0; option < count; option++)
            state.Rewards.Choices[start + option] = default;
        for (int option = 0; option < count; option++)
        {
            RunCard card = GenerateCard(ref state, table,
                BuildCardPools(ref state, table, reward), blacklist,
                (RoomRarityOdds)reward.RaritySource, useBaseOdds: false);
            blacklist.Add(card.Id);
            state.Rewards.Choices[start + option] = card;
        }
        reward.OptionCount = count;
        reward.Flags &= ~RunRewardFlags.CardCanReroll;
        state.Rewards.Items[state.Rewards.ChoiceIndex] = reward;
        ApplyCardOptionHooks(ref state, table, state.Rewards.ChoiceIndex, count);
        state.Rewards.ChoiceCount = state.Rewards.Items[state.Rewards.ChoiceIndex].OptionCount;
    }

    private static void TakePotionReward(ref RunState state, int index, ContentTable table)
    {
        RunReward reward = state.Rewards.Items[index];
        if (HasRelic(in state, "SOZU"))
        {
            RemoveRewardAt(ref state, index);
            return;
        }
        int slot = FirstEmptyPotionSlot(in state);
        if (slot < 0)
        {
            state.Rewards.Stage = RunRewardStage.PotionDiscard;
            state.Rewards.ChoiceIndex = checked((byte)index);
            state.Rewards.ChoiceCount = 0;
            return;
        }
        RemoveRewardAt(ref state, index);
        state.SetPotion(slot, reward.ModelId);
        state.Rewards.Stage = RunRewardStage.List;
        state.Rewards.ChoiceIndex = 0;
        state.Rewards.ChoiceCount = 0;
    }

    private static int FirstEmptyPotionSlot(in RunState state)
    {
        for (int slot = 0; slot < state.Player.Potions.Count; slot++)
        {
            if (state.Player.Potions.Items[slot].Present == 0)
                return slot;
        }
        return -1;
    }

    private static void DiscardPotion(ref RunState state, int slot)
    {
        state.Player.Potions.Items[slot] = default;
        for (int index = 0; index < state.Relics.Count; index++)
        {
            ref RunRelic relic = ref state.Relics.Items[index];
            if (Registry.Relic(relic.Id).NativeId == "BELT_BUCKLE")
                relic.Status = RunRelicStatus.Normal;
        }
    }

    private static void TakeSpecialCard(ref RunState state, ContentTable table, in RunReward reward)
    {
        RunCard card = state.Rewards.Choices[reward.OptionStart];
        RequireSupportedMask(table.CardRewardHooks[card.Id],
            $"卡牌 {Registry.Card(card.Id).NativeId}");
        // Native SpecialCardReward holds the exact returned DeckVersion and adds it unconditionally;
        // another copy with the same id/upgrade in the deck must not suppress the return.
        AddCardToDeck(ref state, table, card);
    }

    internal static void AddCardToDeck(ref RunState state, ContentTable table, RunCard card)
    {
        CardStats facts = table.Card(card.Id, card.Upgrade);
        card.Flags = (facts.Keywords & Keywords.Eternal) == 0
            ? RunCardFlags.Removable : RunCardFlags.None;
        if (facts.Type == CardType.Curse)
            card.Flags |= RunCardFlags.Curse;
        RequireSupportedMask(table.CardRewardHooks[card.Id],
            $"卡牌 {Registry.Card(card.Id).NativeId}");
        if (card.HasEnchantment != 0)
            RequireSupportedMask(table.EnchantmentRewardHooks[card.EnchantmentId],
                $"附魔 {Registry.Enchantment(card.EnchantmentId).NativeId}");
        // CardPileCmd.Add -> Hook.ModifyCardBeingAddedToDeck (eggs), then deck add.
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            string nativeId = Registry.Relic(relic.Id).NativeId;
            bool upgrades = nativeId switch
            {
                "MOLTEN_EGG" => table.Card(card.Id, card.Upgrade).Type == CardType.Attack,
                "FROZEN_EGG" => table.Card(card.Id, card.Upgrade).Type == CardType.Power,
                "TOXIC_EGG" => table.Card(card.Id, card.Upgrade).Type == CardType.Skill,
                _ => false,
            };
            if (upgrades && card.Upgrade == 0
                && table.Card(card.Id, 0).MaxUpgradeLevel > 0)
            {
                card.Upgrade = 1;
                card.Properties = table.Card(card.Id).DefaultProperties[1];
            }
        }
        card.FloorAddedToDeck = state.History.Count;
        state.AddCard(card);
        ApplyCardAddedHooks(ref state, table, card);
    }

    private static void ApplyCardAddedHooks(ref RunState state, ContentTable table, in RunCard card)
    {
        for (int index = 0; index < state.Relics.Count; index++)
        {
            ref RunRelic relic = ref state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            string nativeId = Registry.Relic(relic.Id).NativeId;
            switch (nativeId)
            {
                case "BOOK_OF_FIVE_RINGS":
                {
                    int added = ReadRelicInt(in relic, "CardsAdded") + 1;
                    SetRelicInt(ref relic, "CardsAdded", added);
                    int cycle = IntVar(table, relic.Id, "Cards");
                    relic.DisplayAmount = cycle == 0 ? 0 : added % cycle;
                    if (cycle != 0 && added % cycle == 0)
                        Heal(ref state, IntVar(table, relic.Id, "Heal"));
                    break;
                }
                case "DARKSTONE_PERIAPT":
                    if (table.Card(card.Id, card.Upgrade).Type == CardType.Curse)
                        GainMaxHp(ref state, IntVar(table, relic.Id, "MaxHp"));
                    break;
                case "LUCKY_FYSH":
                    GainGold(ref state, table, IntVar(table, relic.Id, "Gold"), false);
                    break;
            }
        }
    }

    /// <summary>
    /// Native <c>MawBank.AfterRoomEntered</c>: the first room of a map point, before the relic has
    /// paid for anything. Shared by the shop and combat room-entry paths.
    /// </summary>
    internal static void GainMawBankGold(ref RunState state, ContentTable table)
    {
        if (state.Rooms.Count != 1)
            return;
        for (int index = 0; index < state.Relics.Count; index++)
        {
            ref RunRelic relic = ref state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            if (Registry.Relic(relic.Id).NativeId != "MAW_BANK"
                || ReadRelicBool(in relic, "HasItemBeenBought"))
            {
                continue;
            }
            GainGold(ref state, table, IntVar(table, relic.Id, "Gold"), false);
        }
    }

    internal static void GainGold(ref RunState state, ContentTable table, int amount, bool stolenBack)
    {
        decimal value = amount;
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            switch (Registry.Relic(relic.Id).NativeId)
            {
                case "BOWLER_HAT":
                    value *= table.RelicVar(relic.Id, "GoldIncrease");
                    break;
                case "ECTOPLASM":
                    value = 0m;
                    break;
            }
        }
        if (value <= 0m)
            return;
        state.Player.Gold += (int)value;
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            if (Registry.Relic(relic.Id).NativeId == "DRAGON_FRUIT")
                GainMaxHp(ref state, IntVar(table, relic.Id, "MaxHp"));
        }
        _ = stolenBack;
    }

    internal static void GainMaxHp(ref RunState state, int amount)
    {
        if (amount <= 0)
            return;
        state.Player.MaxHp += amount;
        state.Player.CurrentHp = Math.Min(state.Player.MaxHp,
            state.Player.CurrentHp + amount);
    }

    internal static void Heal(ref RunState state, int amount)
    {
        if (amount <= 0)
            return;
        state.Player.CurrentHp = Math.Min(state.Player.MaxHp,
            state.Player.CurrentHp + amount);
    }

    internal static void CheckRelicObtainable(ref RunState state, ContentTable table, ushort relicId)
    {
        ContentRelicRewardFacts facts = table.RelicRewardFacts[relicId]
            ?? throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
                $"遗物 {Registry.Relic(relicId).NativeId} 没有奖励事实。");
        if (facts.Obtain == ContentRelicObtainKind.Unsupported)
        {
            throw new ContentUnavailableException(RunRewardReason.RelicObtainEffect,
                $"遗物 {Registry.Relic(relicId).NativeId} 的获得效果本批未实现。");
        }
        RequireSupportedMask(facts.RewardHooks,
            $"遗物 {Registry.Relic(relicId).NativeId}");
        if (facts.Allowed == ContentRelicAllowedKind.Unsupported)
        {
            throw new ContentUnavailableException(RunRewardReason.RelicAllowedHook,
                $"遗物 {Registry.Relic(relicId).NativeId} 重写了 IsAllowed，本批未映射。");
        }
    }

    internal static void ObtainRelic(ref RunState state, ContentTable table, ushort relicId)
    {
        ContentRelicRewardFacts facts = table.RelicRewardFacts[relicId]
            ?? throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
                $"遗物 {Registry.Relic(relicId).NativeId} 没有奖励事实。");
        RunRelic relic = default;
        relic.Id = relicId;
        relic.Status = RunRelicStatus.Normal;
        relic.StackCount = 1;
        relic.DisplayAmount = facts.InitialDisplayAmount;
        relic.Properties = facts.DefaultProperties;
        relic.FloorAddedToDeck = state.History.Count;
        state.AddRelic(relic);
        if (!facts.Stackable)
        {
            RemoveFromPlayerBag(ref state, relicId);
            RemoveFromSharedBag(ref state, relicId);
        }
        ApplyRelicObtainEffect(ref state, table, relicId);
        ApplyRelicObtainedCardHooks(ref state, table, relicId);
    }

    internal static void RemoveFromPlayerBag(ref RunState state, ushort id)
    {
        for (int rarity = 0; rarity < RunLimits.RelicRarities; rarity++)
        {
            RunRelicBagSegment segment = state.PlayerBag.Segments[rarity];
            int write = 0;
            for (int read = 0; read < segment.Count; read++)
            {
                if (segment.Ids[read] != id)
                    segment.Ids[write++] = segment.Ids[read];
            }
            for (int index = write; index < segment.Count; index++)
                segment.Ids[index] = 0;
            segment.Count = checked((ushort)write);
            state.PlayerBag.Segments[rarity] = segment;
        }
    }

    private static void ApplyRelicObtainEffect(ref RunState state, ContentTable table, ushort relicId)
    {
        string nativeId = Registry.Relic(relicId).NativeId;
        switch (nativeId)
        {
            case "STRAWBERRY":
            case "MANGO":
            case "PEAR":
            case "FAKE_MANGO":
            case "NUTRITIOUS_OYSTER":
            case "LOOMING_FRUIT":
                GainMaxHp(ref state, IntVar(table, relicId, "MaxHp"));
                break;
            case "LEES_WAFFLE":
                GainMaxHp(ref state, IntVar(table, relicId, "MaxHp"));
                state.Player.CurrentHp = state.Player.MaxHp;
                break;
            case "FAKE_LEES_WAFFLE":
                Heal(ref state, state.Player.MaxHp * IntVar(table, relicId, "Heal") / 100);
                break;
            case "OLD_COIN":
            case "GOLDEN_PEARL":
                GainGold(ref state, table, IntVar(table, relicId, "Gold"), false);
                break;
            case "POTION_BELT":
                GrowPotionSlots(ref state, IntVar(table, relicId, "PotionSlots"));
                break;
            case "BELT_BUCKLE":
                break;
            case "WAR_PAINT":
                UpgradeStableShuffled(ref state, table, relicId, CardType.Skill);
                break;
            case "WHETSTONE":
                UpgradeStableShuffled(ref state, table, relicId, CardType.Attack);
                break;
            default:
                if (table.RelicRewardFacts[relicId]!.Obtain == ContentRelicObtainKind.Implemented)
                {
                    throw new ContentUnavailableException(RunRewardReason.RelicObtainEffect,
                        $"遗物 {nativeId} 标记为实现但缺少效果。");
                }
                break;
        }
    }

    private static void GrowPotionSlots(ref RunState state, int amount)
    {
        int count = state.Player.Potions.Count + amount;
        if (count > RunLimits.PotionSlots)
            throw new CapacityException("RunState.PotionSlots", RunLimits.PotionSlots);
        for (int slot = state.Player.Potions.Count; slot < count; slot++)
            state.Player.Potions.Items[slot] = default;
        state.Player.Potions.Count = checked((byte)count);
    }

    private static void UpgradeStableShuffled(ref RunState state, ContentTable table,
        ushort relicId, CardType type)
    {
        Span<int> candidates = stackalloc int[RunLimits.DeckCards];
        int count = 0;
        for (int index = 0; index < state.Deck.Count; index++)
        {
            RunCard card = state.Deck.Cards[index];
            CardStats stats = table.Card(card.Id, card.Upgrade);
            if (stats.Type == type && card.Upgrade < stats.MaxUpgradeLevel)
                candidates[count++] = index;
        }
        Span<int> order = candidates[..count];
        for (int index = 1; index < order.Length; index++)
        {
            int value = order[index];
            int position = index - 1;
            while (position >= 0 && CompareDeckCards(state.Deck.Cards[order[position]],
                       state.Deck.Cards[value]) > 0)
            {
                order[position + 1] = order[position];
                position--;
            }
            order[position + 1] = value;
        }
        ref RngState niche = ref state.Rng.Streams[(int)RunRngStream.Niche];
        RunRandom.UnstableShuffle(ref niche, order);
        int upgrades = Math.Min(IntVar(table, relicId, "Cards"), order.Length);
        for (int index = 0; index < upgrades; index++)
            state.Deck.Cards[order[index]].Upgrade++;
    }

    private static int CompareDeckCards(in RunCard left, in RunCard right)
    {
        int compare = string.CompareOrdinal(Registry.Card(left.Id).NativeId,
            Registry.Card(right.Id).NativeId);
        return compare != 0 ? compare : left.Upgrade.CompareTo(right.Upgrade);
    }

    private static void ApplyRelicObtainedCardHooks(ref RunState state, ContentTable table,
        ushort relicId)
    {
        string nativeId = Registry.Relic(relicId).NativeId;
        if (nativeId == "DRIFTWOOD")
        {
            for (int index = 0; index < state.Rewards.Count; index++)
            {
                if (state.Rewards.Items[index].Type == RunRewardType.Card)
                    state.Rewards.Items[index].Flags |= RunRewardFlags.CardCanReroll;
            }
            return;
        }
        for (int rewardIndex = 0; rewardIndex < state.Rewards.Count; rewardIndex++)
        {
            RunReward reward = state.Rewards.Items[rewardIndex];
            if (reward.Type != RunRewardType.Card)
                continue;
            switch (nativeId)
            {
                case "MOLTEN_EGG":
                    UpgradeOptionCards(ref state, table, rewardIndex, reward.OptionCount,
                        CardType.Attack);
                    break;
                case "FROZEN_EGG":
                    UpgradeOptionCards(ref state, table, rewardIndex, reward.OptionCount,
                        CardType.Power);
                    break;
                case "TOXIC_EGG":
                    UpgradeOptionCards(ref state, table, rewardIndex, reward.OptionCount,
                        CardType.Skill);
                    break;
                case "LAVA_LAMP":
                    UpgradeOptionCards(ref state, table, rewardIndex, reward.OptionCount, null);
                    break;
            }
        }
    }

    private static void RemoveRewardAt(ref RunState state, int index)
    {
        if ((uint)index >= state.Rewards.Count)
            throw new ContentUnavailableException(RunRewardReason.RewardIndex,
                $"奖励下标 {index} 超出。");
        for (int move = index; move + 1 < state.Rewards.Count; move++)
            state.Rewards.Items[move] = state.Rewards.Items[move + 1];
        state.Rewards.Count--;
        state.Rewards.Items[state.Rewards.Count] = default;
    }

}

/// <summary>Stable reasons of the reward-generation boundary.</summary>
public static class RunRewardReason
{
    public const string GoldProportion = "gold-proportion-encounter";
    public const string RewardHookRelic = "reward-hook-relic";
    public const string RewardHookListener = "reward-hook-listener";
    public const string RelicAllowedHook = "relic-allowed-hook";
    public const string RelicObtainEffect = "relic-obtain-effect";
    public const string CardRewardAlternative = "card-reward-alternative";
    public const string RewardNotGenerated = "rewards-not-generated";
    public const string RewardOpen = "reward-open";
    public const string NotRewardDecision = "not-reward-decision";
    public const string RewardIndex = "reward-index";
    public const string PotionSlot = "potion-slot";
    public const string CardOption = "card-option";
    public const string RewardState = "reward-state";
}
