using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Native <c>MerchantInventory.CreateForNormalMerchant</c> and the merchant entries' purchase,
/// removal and exit flow. The generation order and RNG use are the native ones:
/// <list type="number">
/// <item><c>Shops.NextInt(coloredCardTypes.Length)</c> picks the sale slot;</item>
/// <item>each character/colorless card rolls rarity on <c>Rewards</c>, picks on <c>Shops</c>,
/// draws one <c>Rewards</c> upgrade roll and one <c>Shops</c> price roll (the sale slot draws the
/// price roll twice and halves the second);</item>
/// <item>two relic slots roll rarity on <c>Rewards</c>, the third is Shop; each pulls from the
/// back of the player bag and draws one <c>Shops</c> price roll;</item>
/// <item>three potions roll rarity and pick on <c>Shops</c>, then each draws one <c>Shops</c>
/// price roll when the native test mode allows it;</item>
/// <item>the removal price is <c>BaseCost + PriceIncrease * CardShopRemovalsUsed</c>.</item>
/// </list>
/// Merchant prices then pass through <c>Hook.ModifyMerchantPrice</c> in listener order. Any
/// listener that overrides a hook the Core does not implement rejects instead of being ignored.
/// </summary>
public static class RunShopGenerator
{
    public const int CharacterCardBase = 0;
    public const int ColorlessCardBase = RunLimits.ShopCharacterCards;
    public const int RelicBase = ColorlessCardBase + RunLimits.ShopColorlessCards;
    public const int PotionBase = RelicBase + RunLimits.ShopRelics;
    public const int RemovalIndex = PotionBase + RunLimits.ShopPotions;

    /// <summary>Reward-flow hooks the merchant flow actually calls; other reward hooks are not.</summary>
    private const ContentRewardHook ShopRewardHooks =
        ContentRewardHook.ModifyCardRewardUpgradeOdds
        | ContentRewardHook.ShouldProcurePotion
        | ContentRewardHook.AfterPotionProcured
        | ContentRewardHook.ModifyGoldGained
        | ContentRewardHook.AfterModifyingGoldGained
        | ContentRewardHook.AfterGoldGained
        | ContentRewardHook.ShouldAddToDeck
        | ContentRewardHook.TryModifyCardBeingAddedToDeck
        | ContentRewardHook.TryModifyCardBeingAddedToDeckLate
        | ContentRewardHook.AfterCardChangedPiles
        | ContentRewardHook.AfterCardChangedPilesLate
        | ContentRewardHook.AfterCurrentHpChanged;

    /// <summary>Native <c>MerchantInventory.CreateForNormalMerchant</c> after the room is pushed.</summary>
    public static void Generate(ref RunState state, ContentTable table)
    {
        RequireSupportedListeners(in state, table);
        RunState work = state;
        work.Shop = default;
        ContentShopRules rules = table.ShopRules;
        ContentCharacter character = table.Character(work.Player.Character);
        ref RngState shops = ref ShopsRng(ref work);
        ref RngState rewards = ref RewardsRng(ref work);

        int saleSlot = shops.NextInt(rules.CharacterCardTypes.Length);
        List<ContentPoolEntry> characterPool = BuildCardPool(in work, character.CardPool);
        List<ContentPoolEntry> colorlessPool = BuildCardPool(in work, table.ColorlessPool);
        HashSet<ushort> used = [];

        for (int slot = 0; slot < rules.CharacterCardTypes.Length; slot++)
        {
            RunShopCard card = CreateCharacterCard(ref work, table, characterPool, used,
                rules.CharacterCardTypes[slot], slot == saleSlot);
            work.Shop.CharacterCards[slot] = card;
            used.Add(card.Card.Id);
        }
        for (int slot = 0; slot < rules.ColorlessCardRarities.Length; slot++)
        {
            RunShopCard card = CreateColorlessCard(ref work, table, colorlessPool, used,
                rules.ColorlessCardRarities[slot]);
            work.Shop.ColorlessCards[slot] = card;
            used.Add(card.Card.Id);
        }

        for (int slot = 0; slot < rules.RandomRelicRaritySlots; slot++)
        {
            RunRelicRarity rarity = RollRelicRarity(ref rewards, table.RewardRules);
            work.Shop.Relics[slot] = CreateRelic(ref work, table, rarity, null);
        }
        for (int slot = rules.RandomRelicRaritySlots; slot < rules.RelicSlots; slot++)
            work.Shop.Relics[slot] = CreateRelic(ref work, table, rules.FixedRelicRarity, null);

        List<(ContentPotionEntry Entry, PotionRarity Rarity)> potionPool =
            BuildPotionPool(in work, table, character);
        (ContentPotionEntry Entry, PotionRarity Rarity)[] potions =
            new (ContentPotionEntry, PotionRarity)[rules.PotionSlots];
        for (int slot = 0; slot < rules.PotionSlots; slot++)
        {
            float roll = shops.NextFloat(1f);
            PotionRarity rarity = roll <= table.RewardRules.PotionRareThreshold
                ? PotionRarity.Rare
                : roll <= table.RewardRules.PotionUncommonThreshold
                    ? PotionRarity.Uncommon
                    : PotionRarity.Common;
            List<int> candidates = [];
            for (int index = 0; index < potionPool.Count; index++)
            {
                if (potionPool[index].Rarity == rarity)
                    candidates.Add(index);
            }
            if (candidates.Count == 0)
                throw new ContentUnavailableException(RunShopReason.Potion,
                    $"药水池没有稀有度 {rarity} 的候选。");
            int pick = candidates[shops.NextInt(candidates.Count)];
            potions[slot] = potionPool[pick];
            potionPool.RemoveAt(pick);
        }
        for (int slot = 0; slot < rules.PotionSlots; slot++)
        {
            (ContentPotionEntry entry, PotionRarity rarity) = potions[slot];
            int cost = rules.PotionCosts[(int)rarity];
            if (rules.PotionPriceVaries)
            {
                cost = RoundToInt(cost * shops.NextFloat(rules.PotionPriceMin,
                    rules.PotionPriceMax));
            }
            RunShopModel model = default;
            model.ModelId = entry.PotionId;
            model.Cost = cost;
            model.Flags = RunShopEntryFlags.Stocked;
            work.Shop.Potions[slot] = model;
        }

        bool inflation = work.Ascension >= rules.InflationLevel;
        work.Shop.Removal = new RunShopRemoval
        {
            Cost = (inflation ? rules.RemovalBaseCostInflation : rules.RemovalBaseCost)
                + (inflation ? rules.RemovalPriceIncreaseInflation : rules.RemovalPriceIncrease)
                    * work.ExtraPlayer.CardShopRemovalsUsed,
            Used = 0,
        };
        work.Shop.Stage = RunShopStage.Shop;
        work.Decision = RunDecision.Shop;
        RefreshPrices(ref work, table);
        work.Canonicalize();
        state = work;
    }

    /// <summary>Rejects if a current listener overrides a merchant-flow hook the Core does not model.</summary>
    public static void RequireSupportedListeners(in RunState state, ContentTable table)
    {
        for (int index = 0; index < state.Deck.Count; index++)
        {
            RunCard card = state.Deck.Cards[index];
            RequireMask(table.CardShopHooks[card.Id], table.CardRewardHooks[card.Id],
                $"卡牌 {Registry.Card(card.Id).NativeId}");
            if (card.HasEnchantment != 0)
            {
                RequireMask(table.EnchantmentShopHooks[card.EnchantmentId],
                    table.EnchantmentRewardHooks[card.EnchantmentId],
                    $"附魔 {Registry.Enchantment(card.EnchantmentId).NativeId}");
            }
        }
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            ContentRelicRewardFacts facts = table.RelicRewardFacts[relic.Id]
                ?? throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
                    $"遗物 {Registry.Relic(relic.Id).NativeId} 没有奖励事实。");
            RequireMask(facts.ShopHooks, facts.RewardHooks,
                $"遗物 {Registry.Relic(relic.Id).NativeId}");
        }
        for (int slot = 0; slot < state.Player.Potions.Count; slot++)
        {
            RunPotion potion = state.Player.Potions.Items[slot];
            if (potion.Present != 0)
            {
                RequireMask(table.PotionShopHooks[potion.Id], table.PotionRewardHooks[potion.Id],
                    $"药水 {Registry.Potion(potion.Id).NativeId}");
            }
        }
    }

    private static void RequireMask(in ContentShopHookMask shop, in ContentRewardHookMask reward,
        string what)
    {
        uint shopUnsupported = shop.Unsupported;
        if (shopUnsupported != 0)
        {
            throw new ContentUnavailableException(RunShopReason.HookListener,
                $"{what} 重写了未实现的商店 Hook {(ContentShopHook)shopUnsupported}。");
        }
        uint rewardUnsupported = reward.Unsupported & (uint)ShopRewardHooks;
        if (rewardUnsupported != 0)
        {
            throw new ContentUnavailableException(RunShopReason.HookListener,
                $"{what} 重写了未实现的商店奖励 Hook {(ContentRewardHook)rewardUnsupported}。");
        }
    }

    // ---------------------------------------------------------------- generation helpers

    private static List<ContentPoolEntry> BuildCardPool(in RunState state, ContentCardPool pool)
    {
        List<ContentPoolEntry> entries = [];
        for (int index = 0; index < pool.Cards.Length; index++)
        {
            ContentPoolEntry entry = pool.Cards[index];
            if (!RunRewardGenerator.EpochsRevealed(in state, entry.RequiredEpochs)
                || entry.MultiplayerOnly
                || entry.Rarity == CardRarity.Basic)
            {
                continue;
            }
            entries.Add(entry);
        }
        return entries;
    }

    private static RunShopCard CreateCharacterCard(ref RunState state, ContentTable table,
        List<ContentPoolEntry> pool, HashSet<ushort> used, CardType type, bool onSale)
    {
        ContentShopRules rules = table.ShopRules;
        ref RngState rewards = ref RewardsRng(ref state);
        bool scarcity = state.Ascension >= table.AscensionRules.ScarcityLevel;
        float rare = scarcity ? rules.ScarcityShopRareOdds : rules.ShopRareOdds;
        float uncommon = scarcity ? rules.ScarcityShopUncommonOdds : rules.ShopUncommonOdds;
        float roll = rewards.NextFloat(1f);
        float offset = state.PlayerOdds.CardRarity;
        CardRarity rarity = roll < rare + offset
            ? CardRarity.Rare
            : roll < uncommon + rare + offset ? CardRarity.Uncommon : CardRarity.Common;
        CardRarity allowed = NextAllowedRarity(rarity, pool, used, type);
        if (allowed == CardRarity.None)
            throw new ContentUnavailableException(ContentReason.UnsupportedCard,
                $"商人卡池没有类型 {type} 的卡牌。");
        List<ContentPoolEntry> items = [];
        for (int index = 0; index < pool.Count; index++)
        {
            ContentPoolEntry entry = pool[index];
            if (!used.Contains(entry.CardId) && entry.Rarity == allowed
                && entry.Type == type)
            {
                items.Add(entry);
            }
        }
        return CreateCardFromItems(ref state, table, items, colorless: false, onSale);
    }

    private static RunShopCard CreateColorlessCard(ref RunState state, ContentTable table,
        List<ContentPoolEntry> pool, HashSet<ushort> used, CardRarity rarity)
    {
        List<ContentPoolEntry> items = [];
        for (int index = 0; index < pool.Count; index++)
        {
            ContentPoolEntry entry = pool[index];
            if (!used.Contains(entry.CardId) && entry.Rarity == rarity)
                items.Add(entry);
        }
        if (items.Count == 0)
            throw new ContentUnavailableException(ContentReason.UnsupportedCard,
                $"无色卡池没有稀有度 {rarity} 的卡牌。");
        return CreateCardFromItems(ref state, table, items, colorless: true, onSale: false);
    }

    private static RunShopCard CreateCardFromItems(ref RunState state, ContentTable table,
        List<ContentPoolEntry> items, bool colorless, bool onSale)
    {
        ref RngState shops = ref ShopsRng(ref state);
        ref RngState rewards = ref RewardsRng(ref state);
        ContentPoolEntry chosen = items[shops.NextInt(items.Count)];
        if (chosen.CardId == ContentPoolEntry.NoCard)
            throw new ContentUnavailableException(ContentReason.UnsupportedCard,
                $"卡牌 {chosen.NativeId} 没有 Blitz 定义。");
        ContentCard content = table.Card(chosen.CardId);
        RunCard card = default;
        card.Id = chosen.CardId;
        card.Properties = content.DefaultProperties[0];
        CardStats facts = table.Card(card.Id, 0);
        card.Flags = (facts.Keywords & Keywords.Eternal) == 0
            ? RunCardFlags.Removable : RunCardFlags.None;
        if (facts.Type == CardType.Curse)
            card.Flags |= RunCardFlags.Curse;
        _ = rewards.NextFloat(1f);
        // Native RollForUpgrade(..., -999999999m) can never upgrade; no model overrides the odds.
        RunShopCard entry = default;
        entry.Card = card;
        entry.Flags = RunShopEntryFlags.Stocked;
        // Native Populate runs ModifyMerchantCardCreationResults before CalcCost.
        ApplyCardCreationHook(ref state, table, ref entry);
        ContentShopRules rules = table.ShopRules;
        int baseCost = (colorless ? rules.ColorlessCardCosts : rules.CardCosts)[(int)chosen.Rarity];
        if (baseCost <= 0)
            throw new ContentUnavailableException(ContentReason.UnsupportedCard,
                $"卡牌 {chosen.NativeId} 没有商人价格。");
        float priceRoll = shops.NextFloat(rules.CardPriceMin, rules.CardPriceMax);
        int cost = RoundToInt(baseCost * priceRoll);
        if (onSale)
        {
            float saleRoll = shops.NextFloat(rules.CardPriceMin, rules.CardPriceMax);
            cost = RoundToInt(baseCost * saleRoll) / rules.SaleDivisor;
        }
        entry.Cost = cost;
        entry.Flags = RunShopEntryFlags.Stocked
            | (onSale ? RunShopEntryFlags.OnSale : RunShopEntryFlags.None);
        return entry;
    }

    private static CardRarity NextAllowedRarity(CardRarity rarity,
        List<ContentPoolEntry> pool, HashSet<ushort> used, CardType type)
    {
        CardRarity current = rarity;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            for (int index = 0; index < pool.Count; index++)
            {
                ContentPoolEntry entry = pool[index];
                if (!used.Contains(entry.CardId) && entry.Rarity == current
                    && entry.Type == type)
                {
                    return current;
                }
            }
            current = current switch
            {
                CardRarity.Basic => CardRarity.Common,
                CardRarity.Common => CardRarity.Uncommon,
                CardRarity.Uncommon => CardRarity.Rare,
                CardRarity.Rare => CardRarity.Common,
                _ => CardRarity.None,
            };
            if (current == rarity || current == CardRarity.None)
                break;
        }
        return CardRarity.None;
    }

    private static RunRelicRarity RollRelicRarity(ref RngState rewards, ContentRewardRules rules)
    {
        float roll = rewards.NextFloat(1f);
        return roll < rules.RelicCommonThreshold ? RunRelicRarity.Common
            : roll < rules.RelicUncommonThreshold ? RunRelicRarity.Uncommon
            : RunRelicRarity.Rare;
    }

    private static RunShopModel CreateRelic(ref RunState state, ContentTable table,
        RunRelicRarity rarity, HashSet<ushort>? blacklist)
    {
        ushort id = PullRelicFromBack(ref state, table, rarity, blacklist);
        ContentRelicRewardFacts facts = table.RelicRewardFacts[id]
            ?? throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
                $"遗物 {Registry.Relic(id).NativeId} 没有奖励事实。");
        ContentShopRules rules = table.ShopRules;
        float roll = ShopsRng(ref state).NextFloat(rules.RelicPriceMin, rules.RelicPriceMax);
        RunShopModel model = default;
        model.ModelId = id;
        model.Cost = RoundToInt(facts.MerchantCost * roll);
        model.Flags = RunShopEntryFlags.Stocked;
        return model;
    }

    private static ushort PullRelicFromBack(ref RunState state, ContentTable table,
        RunRelicRarity rarity, HashSet<ushort>? blacklist)
    {
        RunRewardGenerator.RemoveDisallowedRelics(ref state, table);
        RunRelicRarity current = rarity;
        int segment = (int)current;
        if (state.PlayerBag.Segments[segment].Count == 0
            && state.PlayerBag.RefreshAllowed != 0)
        {
            throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
                "玩家遗物袋可刷新，本批未实现刷新。");
        }
        while (!BagHasShopRelic(in state, table, current, blacklist))
        {
            current = current switch
            {
                RunRelicRarity.Shop => RunRelicRarity.Common,
                RunRelicRarity.Common => RunRelicRarity.Uncommon,
                RunRelicRarity.Uncommon => RunRelicRarity.Rare,
                _ => RunRelicRarity.None,
            };
            if (current == RunRelicRarity.None)
                break;
        }
        ushort id = 0;
        bool found = false;
        if (current != RunRelicRarity.None)
        {
            segment = (int)current;
            RunRelicBagSegment bag = state.PlayerBag.Segments[segment];
            for (int index = bag.Count - 1; index >= 0; index--)
            {
                ushort candidate = bag.Ids[index];
                ContentRelicRewardFacts facts = table.RelicRewardFacts[candidate]
                    ?? throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
                        $"遗物袋里的 {Registry.Relic(candidate).NativeId} 没有奖励事实。");
                if (!facts.AllowedInShops
                    || (blacklist != null && blacklist.Contains(candidate)))
                {
                    continue;
                }
                id = candidate;
                found = true;
                for (int move = index; move + 1 < bag.Count; move++)
                    bag.Ids[move] = bag.Ids[move + 1];
                bag.Count--;
                bag.Ids[bag.Count] = 0;
                state.PlayerBag.Segments[segment] = bag;
                break;
            }
        }
        if (!found)
        {
            if (!Registry.TryRelic("CIRCLET", out id))
                throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
                    "遗物袋耗尽，且没有 CIRCLET 定义。");
        }
        RunRewardGenerator.RemoveFromSharedBag(ref state, id);
        return id;
    }

    private static bool BagHasShopRelic(in RunState state, ContentTable table,
        RunRelicRarity rarity, HashSet<ushort>? blacklist)
    {
        RunRelicBagSegment bag = state.PlayerBag.Segments[(int)rarity];
        for (int index = 0; index < bag.Count; index++)
        {
            ushort id = bag.Ids[index];
            ContentRelicRewardFacts facts = table.RelicRewardFacts[id]
                ?? throw new ContentUnavailableException(ContentReason.UnsupportedRelic,
                    $"遗物袋里的 {Registry.Relic(id).NativeId} 没有奖励事实。");
            if (facts.AllowedInShops
                && (blacklist == null || !blacklist.Contains(id)))
            {
                return true;
            }
        }
        return false;
    }

    private static List<(ContentPotionEntry Entry, PotionRarity Rarity)> BuildPotionPool(
        in RunState state, ContentTable table, ContentCharacter character)
    {
        List<(ContentPotionEntry, PotionRarity)> pool = [];
        AddPotionPool(in state, table, character.PotionPool, pool);
        AddPotionPool(in state, table, table.SharedPotionPool, pool);
        return pool;
    }

    private static void AddPotionPool(in RunState state, ContentTable table,
        ContentPotionEntry[] entries, List<(ContentPotionEntry, PotionRarity)> pool)
    {
        for (int index = 0; index < entries.Length; index++)
        {
            ContentPotionEntry entry = entries[index];
            if (!RunRewardGenerator.EpochsRevealed(in state, entry.RequiredEpochs))
                continue;
            if (entry.PotionId == ContentPotionEntry.NoPotion)
                throw new ContentUnavailableException(ContentReason.UnsupportedPotion,
                    $"药水 {entry.NativeId} 没有 Blitz 定义。");
            pool.Add((entry, table.Potion(entry.PotionId).Rarity));
        }
    }

    private static int RoundToInt(float value) => (int)Math.Round(value, MidpointRounding.ToEven);

    private static ref RngState ShopsRng(ref RunState state)
        => ref state.Rng.Streams[(int)RunRngStream.Shops];

    private static ref RngState RewardsRng(ref RunState state)
        => ref state.Rng.Streams[(int)RunRngStream.Rewards];

    // ---------------------------------------------------------------- prices

    /// <summary>Recomputes every displayed price with the merchant-price hooks.</summary>
    public static void RefreshPrices(ref RunState state, ContentTable table)
    {
        for (int index = 0; index < RunLimits.ShopCharacterCards; index++)
            RefreshCardPrice(ref state, table, ref state.Shop.CharacterCards[index]);
        for (int index = 0; index < RunLimits.ShopColorlessCards; index++)
            RefreshCardPrice(ref state, table, ref state.Shop.ColorlessCards[index]);
        for (int index = 0; index < RunLimits.ShopRelics; index++)
            RefreshModelPrice(ref state, table, ref state.Shop.Relics[index]);
        for (int index = 0; index < RunLimits.ShopPotions; index++)
            RefreshModelPrice(ref state, table, ref state.Shop.Potions[index]);
        state.Shop.Removal.Price = PriceWithHooks(in state, table, state.Shop.Removal.Cost);
    }

    private static void RefreshCardPrice(ref RunState state, ContentTable table,
        ref RunShopCard entry)
    {
        entry.Price = (entry.Flags & RunShopEntryFlags.Stocked) == 0
            ? 0 : PriceWithHooks(in state, table, entry.Cost);
    }

    private static void RefreshModelPrice(ref RunState state, ContentTable table,
        ref RunShopModel entry)
    {
        entry.Price = (entry.Flags & RunShopEntryFlags.Stocked) == 0
            ? 0 : PriceWithHooks(in state, table, entry.Cost);
    }

    private static int PriceWithHooks(in RunState state, ContentTable table, int cost)
    {
        decimal price = cost;
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            switch (Registry.Relic(relic.Id).NativeId)
            {
                case "MEMBERSHIP_CARD":
                    price *= table.RelicVar(relic.Id, "Discount") / 100m;
                    break;
                case "THE_COURIER":
                    price *= 1m - table.RelicVar(relic.Id, "Discount") / 100m;
                    break;
            }
        }
        return (int)price;
    }

    // ---------------------------------------------------------------- room hooks

    /// <summary>
    /// Native <c>Hook.BeforeRoomEntered</c> for a merchant room. The only override is Dowsing,
    /// which is not implemented; <see cref="RequireSupportedListeners"/> has already rejected it.
    /// </summary>
    public static void BeforeRoomEntered(ref RunState state, ContentTable table)
    {
        _ = state;
        _ = table;
    }

    /// <summary>Native <c>Hook.AfterRoomEntered</c> for a merchant room, in listener order.</summary>
    public static void AfterRoomEntered(ref RunState state, ContentTable table)
    {
        for (int index = 0; index < state.Relics.Count; index++)
        {
            ref RunRelic relic = ref state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            switch (Registry.Relic(relic.Id).NativeId)
            {
                case "MAW_BANK":
                    RunRewardGenerator.GainMawBankGold(ref state, table);
                    break;
                case "MEAL_TICKET":
                    RunRewardGenerator.Heal(ref state,
                        RunRewardGenerator.IntVar(table, relic.Id, "Heal"));
                    break;
                case "PLANISPHERE":
                    if (state.Rooms.Count == 1
                        && CurrentPointType(in state) == RunMapPointType.Unknown)
                    {
                        RunRewardGenerator.Heal(ref state,
                            RunRewardGenerator.IntVar(table, relic.Id, "Heal"));
                    }
                    break;
                case "LAVA_LAMP":
                    RunRewardGenerator.SetRelicBool(ref relic, "TookDamageThisCombat", false);
                    break;
                case "PANTOGRAPH":
                    relic.Status = IsBossParent(in state) ? RunRelicStatus.Active
                        : RunRelicStatus.Normal;
                    break;
                case "REGAL_PILLOW":
                    relic.Status = RunRelicStatus.Normal;
                    break;
                case "WINGED_BOOTS":
                    ApplyWingedBoots(ref state, table.ShopRules.WingedBootsRooms, ref relic);
                    break;
            }
        }
    }

    private static RunMapPointType CurrentPointType(in RunState state)
        => state.History.Count == 0
            ? RunMapPointType.Unassigned
            : state.History.Entries[state.History.Count - 1].MapPointType;

    private static bool IsBossParent(in RunState state)
    {
        if (state.Visited.CoordCount == 0)
            return false;
        RunCoord current = state.Visited.Coords[state.Visited.CoordCount - 1];
        return current.Row + 1 == state.Map.Boss.Row;
    }

    private static void ApplyWingedBoots(ref RunState state, int maxRooms,
        ref RunRelic relic)
    {
        if (maxRooms <= 0)
            return;
        if (RunRewardGenerator.ReadRelicInt(in relic, "TimesUsed") >= maxRooms)
            return;
        if (state.Rooms.Count > 1 || state.Visited.CoordCount <= 1)
            return;
        RunCoord previous = state.Visited.Coords[state.Visited.CoordCount - 2];
        RunCoord current = state.Visited.Coords[state.Visited.CoordCount - 1];
        if (!RunTravel.TryFindPoint(in state, previous, out RunMapPoint point))
            return;
        for (int child = 0; child < point.ChildCount; child++)
        {
            if (point.Children[child].Equals(current))
                return;
        }
        int used = RunRewardGenerator.ReadRelicInt(in relic, "TimesUsed") + 1;
        RunRewardGenerator.SetRelicInt(ref relic, "TimesUsed", used);
        relic.DisplayAmount = Math.Max(0, maxRooms - used);
        if (used >= maxRooms)
            relic.Status = RunRelicStatus.Disabled;
    }

    // ---------------------------------------------------------------- purchases

    /// <summary>Buy the shelf entry named by a shop option, or open the removal card selection.</summary>
    public static void BuyShopEntry(ref RunState state, ContentTable table,
        in RunDecisionOption option)
    {
        RequireSupportedListeners(in state, table);
        int index = option.Index;
        if (index is >= CharacterCardBase and < ColorlessCardBase)
        {
            BuyCard(ref state, table, ref state.Shop.CharacterCards[index],
                colorless: false, index - CharacterCardBase);
            return;
        }
        if (index is >= ColorlessCardBase and < RelicBase)
        {
            BuyCard(ref state, table, ref state.Shop.ColorlessCards[index - ColorlessCardBase],
                colorless: true, index - ColorlessCardBase);
            return;
        }
        if (index is >= RelicBase and < PotionBase)
        {
            BuyRelic(ref state, table, ref state.Shop.Relics[index - RelicBase]);
            return;
        }
        if (index is >= PotionBase and < RemovalIndex)
        {
            BuyPotion(ref state, table, ref state.Shop.Potions[index - PotionBase]);
            return;
        }
        if (index == RemovalIndex)
        {
            if (state.Shop.Removal.Used != 0)
                throw new ContentUnavailableException(RunShopReason.State,
                    "本商店的删牌服务已经用过。");
            if (!HasRemovableCard(in state))
                throw new ContentUnavailableException(RunShopReason.RemovalCard,
                    "牌组没有可删除的卡牌。");
            state.Shop.Stage = RunShopStage.CardRemoval;
            state.Decision = RunDecision.CardSelect;
            return;
        }
        throw new ContentUnavailableException(RunShopReason.State,
            $"商店条目下标 {index} 非法。");
    }

    private static void BuyCard(ref RunState state, ContentTable table, ref RunShopCard entry,
        bool colorless, int slot)
    {
        if ((entry.Flags & RunShopEntryFlags.Stocked) == 0)
            throw new ContentUnavailableException(RunShopReason.State, "卡牌槽已售罄。");
        int price = entry.Price;
        if (price > state.Player.Gold)
            throw new ContentUnavailableException(RunShopReason.State, "金币不足。");
        RunCard card = entry.Card;
        ushort boughtId = card.Id;
        RunRewardGenerator.AddCardToDeck(ref state, table, card);
        state.Player.Gold -= price;
        if (ShouldRefillMerchantEntry(in state))
        {
            entry.Flags = RunShopEntryFlags.None;
            RestockCard(ref state, table, ref entry, colorless, slot, exclude: boughtId);
        }
        else
        {
            RunShopEntryFlags onSaleFlag = entry.Flags & RunShopEntryFlags.OnSale;
            entry = default;
            entry.Flags = onSaleFlag;
        }
        AfterItemPurchased(ref state, table, price);
        UpdateEntries(ref state, table);
        RefreshPrices(ref state, table);
        state.Shop.Stage = RunShopStage.Shop;
        state.Decision = RunDecision.Shop;
    }

    private static void RestockCard(ref RunState state, ContentTable table,
        ref RunShopCard entry, bool colorless, int slot, ushort exclude)
    {
        ContentShopRules rules = table.ShopRules;
        ContentCharacter character = table.Character(state.Player.Character);
        List<ContentPoolEntry> pool = BuildCardPool(in state,
            colorless ? table.ColorlessPool : character.CardPool);
        HashSet<ushort> used = [];
        CollectStockedCards(in state, used);
        // Native Populate still sees the purchased CreationResult, so the bought card is excluded.
        used.Add(exclude);
        RunShopCard card = colorless
            ? CreateColorlessCard(ref state, table, pool, used, rules.ColorlessCardRarities[slot])
            : CreateCharacterCard(ref state, table, pool, used, rules.CharacterCardTypes[slot],
                onSale: false);
        entry = card;
    }

    private static void BuyRelic(ref RunState state, ContentTable table, ref RunShopModel entry)
    {
        if ((entry.Flags & RunShopEntryFlags.Stocked) == 0)
            throw new ContentUnavailableException(RunShopReason.State, "遗物槽已售罄。");
        int price = entry.Price;
        if (price > state.Player.Gold)
            throw new ContentUnavailableException(RunShopReason.State, "金币不足。");
        ushort id = entry.ModelId;
        RunRewardGenerator.CheckRelicObtainable(ref state, table, id);
        state.Player.Gold -= price;
        RunRewardGenerator.ObtainRelic(ref state, table, id);
        if (ShouldRefillMerchantEntry(in state))
        {
            HashSet<ushort> blacklist = [];
            for (int index = 0; index < RunLimits.ShopRelics; index++)
            {
                RunShopModel other = state.Shop.Relics[index];
                if ((other.Flags & RunShopEntryFlags.Stocked) != 0)
                    blacklist.Add(other.ModelId);
            }
            RunRelicRarity rarity = RollRelicRarity(ref RewardsRng(ref state),
                table.RewardRules);
            entry = CreateRelic(ref state, table, rarity, blacklist);
        }
        else
        {
            entry = default;
        }
        AfterItemPurchased(ref state, table, price);
        UpdateEntries(ref state, table);
        RefreshPrices(ref state, table);
        state.Shop.Stage = RunShopStage.Shop;
        state.Decision = RunDecision.Shop;
    }

    private static void BuyPotion(ref RunState state, ContentTable table, ref RunShopModel entry)
    {
        if ((entry.Flags & RunShopEntryFlags.Stocked) == 0)
            throw new ContentUnavailableException(RunShopReason.State, "药水槽已售罄。");
        int price = entry.Price;
        if (price > state.Player.Gold)
            throw new ContentUnavailableException(RunShopReason.State, "金币不足。");
        if (!CanProcurePotion(in state))
            throw new ContentUnavailableException(RunShopReason.Potion, "药水不能获得。");
        int slot = FirstEmptyPotionSlot(in state);
        if (slot < 0)
            throw new ContentUnavailableException(RunShopReason.Potion, "药水槽已满。");
        ushort id = entry.ModelId;
        state.SetPotion(slot, id);
        for (int index = 0; index < state.Relics.Count; index++)
        {
            ref RunRelic relic = ref state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) == 0
                && Registry.Relic(relic.Id).NativeId == "BELT_BUCKLE")
            {
                relic.Status = RunRelicStatus.Normal;
            }
        }
        state.Player.Gold -= price;
        if (ShouldRefillMerchantEntry(in state))
        {
            ContentShopRules rules = table.ShopRules;
            List<(ContentPotionEntry Entry, PotionRarity Rarity)> pool =
                BuildPotionPool(in state, table, table.Character(state.Player.Character));
            RunShopModel model = default;
            float roll = ShopsRng(ref state).NextFloat(1f);
            PotionRarity rarity = roll <= table.RewardRules.PotionRareThreshold
                ? PotionRarity.Rare
                : roll <= table.RewardRules.PotionUncommonThreshold
                    ? PotionRarity.Uncommon : PotionRarity.Common;
            List<int> candidates = [];
            for (int index = 0; index < pool.Count; index++)
            {
                if (pool[index].Rarity == rarity)
                    candidates.Add(index);
            }
            if (candidates.Count == 0)
                throw new ContentUnavailableException(RunShopReason.Potion,
                    $"药水池没有稀有度 {rarity} 的候选。");
            (ContentPotionEntry chosen, _) = pool[candidates[ShopsRng(ref state)
                .NextInt(candidates.Count)]];
            model.ModelId = chosen.PotionId;
            int cost = rules.PotionCosts[(int)rarity];
            if (rules.PotionPriceVaries)
            {
                cost = RoundToInt(cost * ShopsRng(ref state).NextFloat(rules.PotionPriceMin,
                    rules.PotionPriceMax));
            }
            model.Cost = cost;
            model.Flags = RunShopEntryFlags.Stocked;
            entry = model;
        }
        else
        {
            entry = default;
        }
        AfterItemPurchased(ref state, table, price);
        UpdateEntries(ref state, table);
        RefreshPrices(ref state, table);
        state.Shop.Stage = RunShopStage.Shop;
        state.Decision = RunDecision.Shop;
    }

    /// <summary>Confirm the removal selection and remove the deck card at <paramref name="deckIndex"/>.</summary>
    public static void RemoveCard(ref RunState state, ContentTable table, int deckIndex)
    {
        RequireSupportedListeners(in state, table);
        if (state.Shop.Stage != RunShopStage.CardRemoval)
            throw new ContentUnavailableException(RunShopReason.State, "当前不在删牌选卡。");
        if ((uint)deckIndex >= (uint)state.Deck.Count)
            throw new ContentUnavailableException(RunShopReason.RemovalCard,
                $"牌组下标 {deckIndex} 非法。");
        RunCard card = state.Deck.Cards[deckIndex];
        if (!IsRemovable(card))
            throw new ContentUnavailableException(RunShopReason.RemovalCard,
                $"卡牌 {Registry.Card(card.Id).NativeId} 不可删除。");
        int price = state.Shop.Removal.Price;
        if (price > state.Player.Gold)
            throw new ContentUnavailableException(RunShopReason.State, "金币不足。");
        // The native deck removal takes the first card equal to the selection; two copies of the
        // same card/upgrade are indistinguishable on the deck screen.
        int target = FirstSameCard(in state, card, deckIndex);
        state.Player.Gold -= price;
        for (int move = target; move + 1 < state.Deck.Count; move++)
            state.Deck.Cards[move] = state.Deck.Cards[move + 1];
        state.Deck.Count--;
        state.Deck.Cards[state.Deck.Count] = default;
        state.ExtraPlayer.CardShopRemovalsUsed++;
        state.Shop.Removal.Used = 1;
        AfterItemPurchased(ref state, table, price);
        UpdateEntries(ref state, table);
        RefreshPrices(ref state, table);
        state.Shop.Stage = RunShopStage.Shop;
        state.Decision = RunDecision.Shop;
    }

    private static int FirstSameCard(in RunState state, in RunCard selected, int fallback)
    {
        for (int index = 0; index < state.Deck.Count; index++)
        {
            RunCard candidate = state.Deck.Cards[index];
            if (candidate.Id == selected.Id && candidate.Upgrade == selected.Upgrade
                && candidate.HasEnchantment == selected.HasEnchantment
                && candidate.EnchantmentId == selected.EnchantmentId
                && candidate.EnchantmentAmount == selected.EnchantmentAmount)
            {
                return index;
            }
        }
        return fallback;
    }

    /// <summary>Cancel the removal selection; no gold is spent and the service stays stocked.</summary>
    public static void CancelCardRemoval(ref RunState state)
    {
        if (state.Shop.Stage != RunShopStage.CardRemoval)
            throw new ContentUnavailableException(RunShopReason.State, "当前不在删牌选卡。");
        state.Shop.Stage = RunShopStage.Shop;
        state.Decision = RunDecision.Shop;
    }

    /// <summary>Close the inventory and pop the merchant room; the next request is a map point.</summary>
    public static void Leave(ref RunState state)
    {
        if (state.Shop.Stage != RunShopStage.Shop)
            throw new ContentUnavailableException(RunShopReason.State, "当前不在商店界面。");
        state.Shop = default;
        state.Decision = RunDecision.None;
        if (state.Rooms.Count != 0
            && state.Rooms.Items[state.Rooms.Count - 1].RoomType == RunRoomType.Shop)
        {
            state.Rooms.Count--;
            state.Rooms.Items[state.Rooms.Count] = default;
        }
        state.Canonicalize();
    }

    private static void AfterItemPurchased(ref RunState state, ContentTable table, int goldSpent)
    {
        if (goldSpent <= 0)
            return;
        for (int index = 0; index < state.Relics.Count; index++)
        {
            ref RunRelic relic = ref state.Relics.Items[index];
            if (Registry.Relic(relic.Id).NativeId != "MAW_BANK"
                || (relic.Flags & RunRelicFlags.Melted) != 0
                || RunRewardGenerator.ReadRelicBool(in relic, "HasItemBeenBought"))
            {
                continue;
            }
            RunRewardGenerator.SetRelicBool(ref relic, "HasItemBeenBought", true);
            relic.Status = RunRelicStatus.Disabled;
        }
    }

    /// <summary>Native <c>MerchantInventory.UpdateEntries</c>: re-run the card-creation hook.</summary>
    private static void UpdateEntries(ref RunState state, ContentTable table)
    {
        if (!HoldsEgg(in state, table))
            return;
        ApplyCardCreationHook(ref state, table, ref state.Shop.CharacterCards);
        ApplyCardCreationHook(ref state, table, ref state.Shop.ColorlessCards);
    }

    private static bool HoldsEgg(in RunState state, ContentTable table)
    {
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            string nativeId = Registry.Relic(relic.Id).NativeId;
            if (nativeId is "MOLTEN_EGG" or "FROZEN_EGG" or "TOXIC_EGG")
                return true;
        }
        return false;
    }

    private static void ApplyCardCreationHook(ref RunState state, ContentTable table,
        ref RunShopCharacterCardArray cards)
    {
        for (int index = 0; index < RunLimits.ShopCharacterCards; index++)
            ApplyCardCreationHook(ref state, table, ref cards[index]);
    }

    private static void ApplyCardCreationHook(ref RunState state, ContentTable table,
        ref RunShopColorlessCardArray cards)
    {
        for (int index = 0; index < RunLimits.ShopColorlessCards; index++)
            ApplyCardCreationHook(ref state, table, ref cards[index]);
    }

    private static void ApplyCardCreationHook(ref RunState state, ContentTable table,
        ref RunShopCard entry)
    {
        if ((entry.Flags & RunShopEntryFlags.Stocked) == 0)
            return;
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) != 0)
                continue;
            CardType? type = Registry.Relic(relic.Id).NativeId switch
            {
                "MOLTEN_EGG" => CardType.Attack,
                "FROZEN_EGG" => CardType.Power,
                "TOXIC_EGG" => CardType.Skill,
                _ => null,
            };
            if (type is not { } wanted)
                continue;
            CardStats stats = table.Card(entry.Card.Id, entry.Card.Upgrade);
            if (stats.Type != wanted || entry.Card.Upgrade >= stats.MaxUpgradeLevel)
                continue;
            entry.Card.Upgrade++;
            entry.Card.Properties = table.Card(entry.Card.Id).DefaultProperties[entry.Card.Upgrade];
        }
    }

    private static void CollectStockedCards(in RunState state, HashSet<ushort> used)
    {
        for (int index = 0; index < RunLimits.ShopCharacterCards; index++)
        {
            RunShopCard entry = state.Shop.CharacterCards[index];
            if ((entry.Flags & RunShopEntryFlags.Stocked) != 0)
                used.Add(entry.Card.Id);
        }
        for (int index = 0; index < RunLimits.ShopColorlessCards; index++)
        {
            RunShopCard entry = state.Shop.ColorlessCards[index];
            if ((entry.Flags & RunShopEntryFlags.Stocked) != 0)
                used.Add(entry.Card.Id);
        }
    }

    // ---------------------------------------------------------------- eligibility and requests

    public static bool HasRemovableCard(in RunState state)
    {
        for (int index = 0; index < state.Deck.Count; index++)
        {
            if (IsRemovable(state.Deck.Cards[index]))
                return true;
        }
        return false;
    }

    public static bool IsRemovable(in RunCard card)
        => (card.Flags & RunCardFlags.Removable) != 0;

    public static bool CanProcurePotion(in RunState state)
    {
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) == 0
                && Registry.Relic(relic.Id).NativeId == "SOZU")
            {
                return false;
            }
        }
        return FirstEmptyPotionSlot(in state) >= 0;
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

    private static bool ShouldRefillMerchantEntry(in RunState state)
    {
        for (int index = 0; index < state.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            if ((relic.Flags & RunRelicFlags.Melted) == 0
                && Registry.Relic(relic.Id).NativeId == "THE_COURIER")
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Builds the merchant-screen request (stocked, affordable entries plus leave).</summary>
    public static RunDecisionRequest CaptureShop(in RunState state)
    {
        RunDecisionRequest request = default;
        request.Kind = RunDecision.Shop;
        int count = 0;
        for (int index = 0; index < RunLimits.ShopCharacterCards; index++)
            AddCardOption(in state, ref request, ref count, state.Shop.CharacterCards[index],
                CharacterCardBase + index);
        for (int index = 0; index < RunLimits.ShopColorlessCards; index++)
            AddCardOption(in state, ref request, ref count, state.Shop.ColorlessCards[index],
                ColorlessCardBase + index);
        for (int index = 0; index < RunLimits.ShopRelics; index++)
            AddModelOption(in state, ref request, ref count, state.Shop.Relics[index],
                RelicBase + index, RunRewardType.Relic);
        for (int index = 0; index < RunLimits.ShopPotions; index++)
        {
            if (!CanProcurePotion(in state))
                break;
            AddModelOption(in state, ref request, ref count, state.Shop.Potions[index],
                PotionBase + index, RunRewardType.Potion);
        }
        if (state.Shop.Removal.Used == 0
            && state.Shop.Removal.Price <= state.Player.Gold
            && HasRemovableCard(in state))
        {
            RunDecisionOption option = default;
            option.Kind = RunDecisionOptionKind.CardRemoval;
            option.Index = checked((byte)RemovalIndex);
            option.RewardType = RunRewardType.CardRemoval;
            option.Amount = state.Shop.Removal.Price;
            request.Options[count++] = option;
        }
        RunDecisionOption leave = default;
        leave.Kind = RunDecisionOptionKind.Skip;
        leave.Index = checked((byte)count);
        request.Options[count++] = leave;
        request.OptionCount = checked((byte)count);
        return request;
    }

    private static void AddCardOption(in RunState state, ref RunDecisionRequest request,
        ref int count, in RunShopCard entry, int globalIndex)
    {
        if ((entry.Flags & RunShopEntryFlags.Stocked) == 0 || entry.Price > state.Player.Gold)
            return;
        RunDecisionOption option = default;
        option.Kind = RunDecisionOptionKind.ShopEntry;
        option.Index = checked((byte)globalIndex);
        option.RewardType = RunRewardType.Card;
        option.Model = new RunRef(RunRefKind.Card, entry.Card.Id);
        option.Upgrade = entry.Card.Upgrade;
        option.HasEnchantment = entry.Card.HasEnchantment;
        option.EnchantmentId = entry.Card.EnchantmentId;
        option.EnchantmentAmount = entry.Card.EnchantmentAmount;
        option.Amount = entry.Price;
        request.Options[count++] = option;
    }

    private static void AddModelOption(in RunState state, ref RunDecisionRequest request,
        ref int count, in RunShopModel entry, int globalIndex, RunRewardType type)
    {
        if ((entry.Flags & RunShopEntryFlags.Stocked) == 0 || entry.Price > state.Player.Gold)
            return;
        RunDecisionOption option = default;
        option.Kind = RunDecisionOptionKind.ShopEntry;
        option.Index = checked((byte)globalIndex);
        option.RewardType = type;
        option.Model = type == RunRewardType.Relic
            ? new RunRef(RunRefKind.Relic, entry.ModelId)
            : new RunRef(RunRefKind.Potion, entry.ModelId);
        option.Amount = entry.Price;
        request.Options[count++] = option;
    }

    /// <summary>Builds the removable-card selection: curses first, then deck order, plus cancel.</summary>
    public static RunDecisionRequest CaptureCardRemoval(in RunState state)
    {
        RunDecisionRequest request = default;
        request.Kind = RunDecision.CardSelect;
        int count = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int index = 0; index < state.Deck.Count; index++)
            {
                RunCard card = state.Deck.Cards[index];
                if (!IsRemovable(card))
                    continue;
                bool curse = (card.Flags & RunCardFlags.Curse) != 0;
                if ((pass == 0) != curse)
                    continue;
                if (count >= RunLimits.DecisionOptions - 1)
                    throw new CapacityException("RunDecision.Options", RunLimits.DecisionOptions);
                RunDecisionOption option = default;
                option.Kind = RunDecisionOptionKind.Card;
                option.Index = checked((byte)index);
                option.Model = new RunRef(RunRefKind.Card, card.Id);
                option.Upgrade = card.Upgrade;
                option.HasEnchantment = card.HasEnchantment;
                option.EnchantmentId = card.EnchantmentId;
                option.EnchantmentAmount = card.EnchantmentAmount;
                request.Options[count++] = option;
            }
        }
        RunDecisionOption cancel = default;
        cancel.Kind = RunDecisionOptionKind.Skip;
        cancel.Index = checked((byte)count);
        request.Options[count++] = cancel;
        request.OptionCount = checked((byte)count);
        return request;
    }
}

/// <summary>
/// Stable reasons of the merchant boundary.
/// </summary>
public static class RunShopReason
{
    public const string HookListener = "shop-hook-listener";
    public const string State = "shop-state";
    public const string RemovalCard = "shop-removal-card";
    public const string Potion = "shop-potion";
}
