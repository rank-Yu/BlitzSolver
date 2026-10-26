// Negative run-import cases: every out-of-scope or unknown state must reject with a stable
// ImportReason code instead of returning a partially defaulted RunState.
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;

namespace OfflineHost;

internal static class RunImportNegative
{
    public static int Run()
    {
        MainLoopContext loop = new();
        SynchronizationContext.SetSynchronizationContext(loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();
        int failures = 0;
        failures += ExpectDirect("multiplayer", ImportReason.MultiPlayer, () =>
        {
            CharacterModel character = ModelDb.AllCharacters.First();
            Player second = Player.CreateForNewRun(character, UnlockState.none, 2UL);
            Player first = Player.CreateForNewRun(character, UnlockState.none, 1UL);
            NativeRunState two = NativeRunState.CreateForNewRun(
                [first, second], ActModel.GetDefaultList().Select(act => act.ToMutable()).ToList(),
                [], GameMode.Standard, 0, "R1B-NEG-MP");
            return (two, first);
        });
        failures += ExpectDirect("game-mode", ImportReason.UnsupportedGameMode, () =>
        {
            CharacterModel character = ModelDb.AllCharacters.First();
            Player player = Player.CreateForNewRun(character, UnlockState.none, 1UL);
            NativeRunState daily = NativeRunState.CreateForNewRun(
                [player], ActModel.GetDefaultList().Select(act => act.ToMutable()).ToList(),
                [], GameMode.Daily, 0, "R1B-NEG-DAILY");
            return (daily, player);
        });
        failures += ExpectDirect("modifier", ImportReason.UnsupportedModifier, () =>
        {
            CharacterModel character = ModelDb.AllCharacters.First();
            Player player = Player.CreateForNewRun(character, UnlockState.none, 1UL);
            ModifierModel modifier = ModelDb.All.OfType<ModifierModel>().First();
            NativeRunState custom = NativeRunState.CreateForNewRun(
                [player], ActModel.GetDefaultList().Select(act => act.ToMutable()).ToList(),
                [modifier], GameMode.Standard, 0, "R1B-NEG-MOD");
            return (custom, player);
        });
        failures += Expect(loop, "unknown-event", ImportReason.UnsupportedEvent, run =>
        {
            EventModel mock = ModelDb.All.OfType<EventModel>()
                .First(model => model.Id.Entry == "MOCK_EVENT_MODEL");
            run.Acts[0]._rooms.events[0] = mock;
        });
        failures += Expect(loop, "unknown-unlock", ImportReason.UnsupportedUnlock, run =>
        {
            run.UnlockState._unlockedEpochIds.Add("MOCK_EPOCH");
        });
        failures += Expect(loop, "unsupported-map", ImportReason.UnsupportedMap, run =>
        {
            run.Map = NullActMap.Instance;
        });
        failures += Expect(loop, "capacity-deck", ImportReason.Capacity, run =>
        {
            Player player = LocalContext.GetMe(run) ?? throw new InvalidOperationException();
            for (int index = 0; index < 140; index++)
            {
                CardModel card = player.Deck.Cards[0].CanonicalInstance.ToMutable();
                player.Deck.AddInternal(card);
                run.AddCard(card, player);
            }
        }, expectCapacityRegion: "RunState.Deck");
        Console.WriteLine($"RUNNEGATIVE failed={failures}");
        return failures == 0 ? 0 : 1;
    }

    private static int Expect(MainLoopContext loop, string name, string reason,
        Action<NativeRunState> mutate, string? expectCapacityRegion = null)
    {
        try
        {
            (NativeRunState run, Player player) = Start(loop);
            mutate(run);
            return CheckImport(name, reason, run, player, expectCapacityRegion);
        }
        catch (InvalidOperationException error)
        {
            Console.WriteLine($"RUNNEGATIVE case={name} status=fail setup={error.Message}");
            return 1;
        }
        finally
        {
            if (RunManager.Instance.IsInProgress)
                RunManager.Instance.CleanUp(true);
        }
    }

    private static int ExpectDirect(string name, string reason,
        Func<(NativeRunState Run, Player Player)> create)
    {
        try
        {
            (NativeRunState run, Player player) = create();
            return CheckImport(name, reason, run, player, null);
        }
        catch (InvalidOperationException error)
        {
            Console.WriteLine($"RUNNEGATIVE case={name} status=fail setup={error.Message}");
            return 1;
        }
        finally
        {
            if (RunManager.Instance.IsInProgress)
                RunManager.Instance.CleanUp(true);
        }
    }

    private static int CheckImport(string name, string reason, NativeRunState run, Player player,
        string? expectCapacityRegion)
    {
        try
        {
            _ = RunStateImporter.Import(run, player);
        }
        catch (UnsupportedImportException error)
        {
            if (error.Reason == reason)
            {
                Console.WriteLine($"RUNNEGATIVE case={name} status=reject reason={error.Reason}");
                return 0;
            }
            Console.WriteLine($"RUNNEGATIVE case={name} status=fail expected={reason} actual={error.Reason}");
            return 1;
        }
        catch (CapacityException error)
        {
            if (reason == ImportReason.Capacity
                && (expectCapacityRegion == null
                    || error.Region.Contains(expectCapacityRegion, StringComparison.Ordinal)))
            {
                Console.WriteLine($"RUNNEGATIVE case={name} status=reject reason={error.Region}");
                return 0;
            }
            Console.WriteLine($"RUNNEGATIVE case={name} status=fail capacity={error.Region}");
            return 1;
        }
        Console.WriteLine($"RUNNEGATIVE case={name} status=fail no-exception expected={reason}");
        return 1;
    }

    private static (NativeRunState Run, Player Player) Start(MainLoopContext loop)
    {
        CharacterModel character = ModelDb.AllCharacters.First();
        OfflineCombatWaiter waiter = new(loop);
        Task<RunState> task = waiter.StartRunAsync(character, 0, "R1B-NEG-" + Guid.NewGuid().ToString("N")[..8]);
        loop.RunUntilCompleted(task, TimeSpan.FromSeconds(120), "负例新局");
        NativeRunState run = task.GetAwaiter().GetResult();
        Player player = LocalContext.GetMe(run)
            ?? throw new InvalidOperationException("找不到本地玩家。");
        return (run, player);
    }
}
