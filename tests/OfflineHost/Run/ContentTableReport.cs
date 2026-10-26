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
        PrintUnsupported(table);
        for (int id = 0; id < table.Encounters.Length; id++)
        {
            if (table.Encounters[id] == null)
                Console.WriteLine($"CONTENT-MISSING encounter={RunCatalog.EncounterName((ushort)id)}");
        }
        Console.WriteLine(
            $"CONTENT-TIME ms={stopwatch.Elapsed.TotalMilliseconds:0.0} " +
            $"retainedBytes={after - before} before={before} after={after}");
        return 0;
    }

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
