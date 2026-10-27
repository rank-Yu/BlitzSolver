// Diagnostic: exports the process-local content table once and reports its coverage, time and
// retained memory. The table is never cached to disk.
using System.Diagnostics;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;

namespace OfflineHost;

internal static class ContentTableReport
{
    public static int Run()
    {
        MainLoopContext loop = new();
        SynchronizationContext.SetSynchronizationContext(loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();

        GC.Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);
        Stopwatch stopwatch = Stopwatch.StartNew();
        ContentTable table = ContentTableExporter.Export();
        stopwatch.Stop();
        GC.Collect();
        long after = GC.GetTotalMemory(forceFullCollection: true);

        int cardIds = 0, cardLevels = 0;
        for (int id = 0; id < table.Cards.Length; id++)
        {
            if (table.Cards[id] is not { } card)
                continue;
            cardIds++;
            cardLevels += card.Levels.Length;
        }
        int powerVars = table.PowerVars.Count(static vars => vars is { Length: > 0 });
        int potions = table.Potions.Count(static potion => potion != null);
        int relics = table.RelicVars.Count(static vars => vars is { Length: > 0 });
        int spawnMonsters = table.SpawnMonsters[0].Count(static monster => monster != null);
        int encounters = table.Encounters.Count(static encounter => encounter != null);
        int enchantments = table.Enchantments.Count(static enchantment => enchantment != null);
        int unsupportedEncounters = table.Encounters.Count(
            static encounter => encounter is { UnsupportedReason: not null });
        int characters = table.Characters.Count(static character => character != null);
        int poolCards = table.ColorlessPool.Cards.Length + table.StatusPool.Cards.Length
            + table.CursePool.Cards.Length;
        for (int id = 0; id < table.Characters.Length; id++)
        {
            if (table.Characters[id] is { } character)
                poolCards += character.CardPool.Cards.Length;
        }
        ContentRewardRules rr = table.RewardRules;
        Console.WriteLine($"CONTENT-ODDS regular={rr.Regular.Rare}/{rr.Regular.Uncommon} elite={rr.Elite.Rare}/{rr.Elite.Uncommon} boss={rr.Boss.Rare}/{rr.Boss.Uncommon} scarcityBoss={rr.ScarcityBoss.Rare}/{rr.ScarcityBoss.Uncommon}");
        Console.WriteLine(
            $"CONTENT cards={cardIds}/{table.Cards.Length} levels={cardLevels} " +
            $"powerVars={powerVars}/{table.PowerVars.Length} potions={potions}/{table.Potions.Length} " +
            $"relicVars={relics}/{table.RelicVars.Length} monsters={spawnMonsters}/{table.SpawnMonsters[0].Length} " +
            $"encounters={encounters}/{table.Encounters.Length} unsupportedEncounters={unsupportedEncounters} " +
            $"enchantments={enchantments}/{table.Enchantments.Length} " +
            $"characters={characters} poolCards={poolCards}");
        for (int id = 0; id < table.SpawnMonsters[0].Length; id++)
        {
            if (table.SpawnMonsters[0][id] == null)
            {
                if (table.MonsterUnsupported[id] == null)
                    Console.WriteLine($"CONTENT-MISSING monster={Registry.Monster((ushort)id).NativeId}");
            }
            else
            {
                Console.WriteLine($"CONTENT-MONSTER id={id} name={Registry.Monster((ushort)id).NativeId}");
            }
        }
        PrintNewRun(table);
        PrintRewardHooks(table);
        PrintShopHooks(table);
        PrintUnsupported(table);
        for (int id = 0; id < table.Encounters.Length; id++)
        {
            if (table.Encounters[id] is not { } encounter)
            {
                Console.WriteLine($"CONTENT-MISSING encounter={RunCatalog.EncounterName((ushort)id)}");
                continue;
            }
            Console.WriteLine($"CONTENT-ENCOUNTER id={id} name={encounter.NativeId} " +
                $"room={encounter.RoomType} rewards={encounter.ShouldGiveRewards} " +
                $"eventScript={Registry.Encounter((ushort)id).EndsByEventScript}");
        }
        Console.WriteLine(
            $"CONTENT-TIME ms={stopwatch.Elapsed.TotalMilliseconds:0.0} " +
            $"retainedBytes={after - before} before={before} after={after}");
        return 0;
    }

    private static void PrintRewardHooks(ContentTable table)
    {
        PrintHookCategory("card", table.CardRewardHooks,
            id => Registry.Card((ushort)id).NativeId);
        PrintHookCategory("enchantment", table.EnchantmentRewardHooks,
            id => Registry.Enchantment((ushort)id).NativeId);
        PrintHookCategory("potion", table.PotionRewardHooks,
            id => Registry.Potion((ushort)id).NativeId);
        int relicOverridden = 0, relicUnsupported = 0;
        for (int id = 0; id < table.RelicRewardFacts.Length; id++)
        {
            if (table.RelicRewardFacts[id] is not { } facts)
                continue;
            if (facts.RewardHooks.Overridden != 0)
            {
                relicOverridden++;
                if (facts.RewardHooks.Unsupported != 0)
                    relicUnsupported++;
                Console.WriteLine(
                    $"CONTENT-HOOK relic={Registry.Relic((ushort)id).NativeId} " +
                    $"overridden={(ContentRewardHook)facts.RewardHooks.Overridden} " +
                    $"implemented={(ContentRewardHook)facts.RewardHooks.Implemented} " +
                    $"unsupported={(ContentRewardHook)facts.RewardHooks.Unsupported}");
            }
            if (facts.Allowed == ContentRelicAllowedKind.Unsupported)
            {
                Console.WriteLine(
                    $"CONTENT-ALLOWED-UNSUPPORTED relic={Registry.Relic((ushort)id).NativeId}");
            }
        }
        Console.WriteLine($"CONTENT-REWARD-HOOK relic overridden={relicOverridden} " +
            $"unsupported={relicUnsupported} total={table.RelicRewardFacts.Length}");
    }

    private static void PrintShopHooks(ContentTable table)
    {
        PrintShopHookCategory("card", table.CardShopHooks,
            id => Registry.Card((ushort)id).NativeId);
        PrintShopHookCategory("enchantment", table.EnchantmentShopHooks,
            id => Registry.Enchantment((ushort)id).NativeId);
        PrintShopHookCategory("potion", table.PotionShopHooks,
            id => Registry.Potion((ushort)id).NativeId);
        int relicOverridden = 0, relicUnsupported = 0;
        for (int id = 0; id < table.RelicRewardFacts.Length; id++)
        {
            if (table.RelicRewardFacts[id] is not { } facts
                || facts.ShopHooks.Overridden == 0)
            {
                continue;
            }
            relicOverridden++;
            if (facts.ShopHooks.Unsupported != 0)
                relicUnsupported++;
            Console.WriteLine(
                $"CONTENT-SHOP-HOOK relic={Registry.Relic((ushort)id).NativeId} " +
                $"overridden={(ContentShopHook)facts.ShopHooks.Overridden} " +
                $"implemented={(ContentShopHook)facts.ShopHooks.Implemented} " +
                $"unsupported={(ContentShopHook)facts.ShopHooks.Unsupported}");
        }
        Console.WriteLine($"CONTENT-SHOP-HOOK relic overridden={relicOverridden} " +
            $"unsupported={relicUnsupported} total={table.RelicRewardFacts.Length}");
    }

    private static void PrintShopHookCategory(string kind, ContentShopHookMask[] masks,
        Func<int, string> name)
    {
        int overridden = 0, unsupported = 0;
        for (int id = 0; id < masks.Length; id++)
        {
            if (masks[id].Overridden == 0)
                continue;
            overridden++;
            if (masks[id].Unsupported == 0)
                continue;
            unsupported++;
            Console.WriteLine(
                $"CONTENT-SHOP-HOOK {kind}={name(id)} " +
                $"overridden={(ContentShopHook)masks[id].Overridden} " +
                $"implemented={(ContentShopHook)masks[id].Implemented} " +
                $"unsupported={(ContentShopHook)masks[id].Unsupported}");
        }
        Console.WriteLine($"CONTENT-SHOP-HOOK {kind} overridden={overridden} " +
            $"unsupported={unsupported} total={masks.Length}");
    }

    private static void PrintHookCategory(string kind, ContentRewardHookMask[] masks,
        Func<int, string> name)
    {
        int overridden = 0, unsupported = 0;
        for (int id = 0; id < masks.Length; id++)
        {
            if (masks[id].Overridden == 0)
                continue;
            overridden++;
            if (masks[id].Unsupported == 0)
                continue;
            unsupported++;
            Console.WriteLine(
                $"CONTENT-HOOK {kind}={name(id)} " +
                $"overridden={(ContentRewardHook)masks[id].Overridden} " +
                $"implemented={(ContentRewardHook)masks[id].Implemented} " +
                $"unsupported={(ContentRewardHook)masks[id].Unsupported}");
        }
        Console.WriteLine($"CONTENT-REWARD-HOOK {kind} overridden={overridden} " +
            $"unsupported={unsupported} total={masks.Length}");
    }

    private static void PrintNewRun(ContentTable table)
    {
        int startingRows = 0;
        for (int id = 0; id < table.Characters.Length; id++)
        {
            if (table.Characters[id] is { } character)
            {
                startingRows++;
                Console.WriteLine(
                    $"CONTENT-STARTING character={character.NativeId} " +
                    $"deck={character.Starting.Deck.Length} relics={character.Starting.Relics.Length} " +
                    $"hp={character.Starting.Hp} gold={character.Starting.Gold} " +
                    $"energy={character.Starting.MaxEnergy} orbs={character.Starting.OrbSlots} " +
                    $"relicPool={character.RelicPool.Length}");
            }
        }
        for (int id = 0; id < table.Acts.Length; id++)
        {
            if (table.Acts[id] is not { } act)
                continue;
            Console.WriteLine(
                $"CONTENT-ACT id={id} name={act.NativeId} rooms={act.BaseRooms} " +
                $"weak={act.WeakEncounters} events={act.Events.Length} " +
                $"encounters={act.Encounters.Length} ancients={act.Ancients.Length} " +
                $"swaps={act.TutorialSwaps.Length} firstRun={act.TutorialSwapsFirstRunOnly} " +
                $"rest=[{Describe(act.RestDraw)}] unknown=[{Describe(act.UnknownDraw)}]");
        }
        Console.WriteLine(
            $"CONTENT-NEWRUN sharedRelics={table.SharedRelicPool.Length} " +
            $"sharedEvents={table.SharedEvents.Length} sharedAncients={table.SharedAncients.Length} " +
            $"defaultActs={table.DefaultActs.Length} startingRows={startingRows} " +
            $"mapWidth={table.MapRules.Width} starts={table.MapRules.StartPaths} " +
            $"elitesA0={table.MapRules.ElitesByAscension[0]} " +
            $"elitesA10={table.MapRules.ElitesByAscension[10]} " +
            $"neowEpoch={RunCatalog.EpochName(table.NeowEpoch)} " +
            $"doubleBossLevel={table.AscensionRules.DoubleBossLevel} " +
            $"mapHookRelics={string.Join(',', table.MapHookRelics.Select(id => Registry.Relic(id).NativeId))} " +
            $"mapHookCards={string.Join(',', table.MapHookCards.Select(id => Registry.Card(id).NativeId))}");
    }

    private static string Describe(in ContentCountDraw draw)
        => $"{draw.Kind},mean={draw.Mean},std={draw.StdDev},min={draw.Min},max={draw.Max}," +
            $"offset={draw.Offset}";

    private static void PrintUnsupported(ContentTable table)
    {
        for (int id = 0; id < table.CardUnsupported.Length; id++)
            Print("card", id, Registry.Card((ushort)id).NativeId, table.CardUnsupported[id]);
        for (int id = 0; id < table.PowerUnsupported.Length; id++)
            Print("power", id, Registry.Power((ushort)id).NativeId, table.PowerUnsupported[id]);
        for (int id = 0; id < table.PotionUnsupported.Length; id++)
            Print("potion", id, Registry.Potion((ushort)id).NativeId, table.PotionUnsupported[id]);
        for (int id = 0; id < table.RelicUnsupported.Length; id++)
            Print("relic", id, Registry.Relic((ushort)id).NativeId, table.RelicUnsupported[id]);
        for (int id = 0; id < table.EnchantmentUnsupported.Length; id++)
            Print("enchantment", id, Registry.Enchantment((ushort)id).NativeId,
                table.EnchantmentUnsupported[id]);
        for (int id = 0; id < table.MonsterUnsupported.Length; id++)
            Print("monster", id, Registry.Monster((ushort)id).NativeId, table.MonsterUnsupported[id]);
        for (int id = 0; id < table.Encounters.Length; id++)
        {
            if (table.Encounters[id] is not { UnsupportedReason: { } reason } encounter)
                continue;
            Console.WriteLine(
                $"CONTENT-UNSUPPORTED type=encounter id={id} name={encounter.NativeId} " +
                $"reason={reason} detail={encounter.UnsupportedDetail}");
        }
        for (int id = 0; id < table.Characters.Length; id++)
        {
            if (table.Characters[id] is not { } character)
                continue;
            PrintPool("character", character.NativeId, character.CardPool);
        }
        PrintPool("colorless", "COLORLESS", table.ColorlessPool);
        PrintPool("status", "STATUS", table.StatusPool);
        PrintPool("curse", "CURSE", table.CursePool);
    }

    private static void PrintPool(string kind, string name, ContentCardPool pool)
    {
        foreach (ContentPoolEntry entry in pool.Cards)
        {
            if (entry.CardId != ContentPoolEntry.NoCard)
                continue;
            Console.WriteLine(
                $"CONTENT-UNSUPPORTED type=card-pool pool={kind}:{name} id=<none> " +
                $"name={entry.NativeId} reason={ContentReason.UnsupportedCard} " +
                "detail=卡池候选没有 Blitz 定义");
        }
    }

    private static void Print(string type, int id, string name, ContentUnsupported? unsupported)
    {
        if (unsupported is null)
            return;
        Console.WriteLine(
            $"CONTENT-UNSUPPORTED type={type} id={id} name={name} " +
            $"reason={unsupported.Reason} detail={unsupported.Detail}");
    }
}
