// --run-import-compare: imports the native run at a natural start and at every frozen combat
// root, then compares an independent native dump with the imported RunState, the 64-draw RNG
// sequence per stream, and (at combat roots) the Battle importer's HP/deck/relic/potion/RNG view.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Sts2Solver.Import;
using Sts2Solver.Simulation.Combat;
using Sts2Solver.Simulation.Run;
using Sts2Solver.TestHost;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using RunState = Sts2Solver.Simulation.Run.RunState;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;

namespace Differential;

internal static class RunImportCompare
{
    private static readonly string[] Characters =
        ["Ironclad", "Silent", "Regent", "Necrobinder", "Defect"];

    public static int Run(string[] args)
    {
        string data = "tests/Golden/data";
        string corpora = "tests/corpora";
        string kind = "all";
        int seeds = 20;
        int jobs = 8;
        string? d1Root = null;
        string? naturalClass = null;
        int naturalAscension = 0;
        string? naturalSeed = null;
        string? compareRoot = null;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--data" when index + 1 < args.Length:
                    data = args[++index];
                    break;
                case "--corpora" when index + 1 < args.Length:
                    corpora = args[++index];
                    break;
                case "--run-kind" when index + 1 < args.Length:
                    kind = args[++index];
                    break;
                case "--run-seeds" when index + 1 < args.Length:
                    seeds = int.Parse(args[++index]);
                    break;
                case "--run-jobs" when index + 1 < args.Length:
                    jobs = int.Parse(args[++index]);
                    break;
                case "--run-d1-root" when index + 1 < args.Length:
                    d1Root = args[++index];
                    break;
                case "--run-compare-natural" when index + 3 < args.Length:
                    naturalClass = args[++index];
                    naturalAscension = int.Parse(args[++index]);
                    naturalSeed = args[++index];
                    break;
                case "--run-compare-d1" when index + 1 < args.Length:
                    compareRoot = args[++index];
                    break;
                default:
                    Console.Error.WriteLine($"未知参数：{args[index]}");
                    return 2;
            }
        }
        if (naturalClass != null)
            return CompareNatural(data, corpora, naturalClass, naturalAscension, naturalSeed!);
        if (compareRoot != null)
            return CompareD1(data, corpora, compareRoot);
        if (jobs is < 1 or > 8)
        {
            Console.Error.WriteLine("--run-jobs 必须在 1 到 8 之间。");
            return 2;
        }
        return RunParent(data, corpora, kind, seeds, jobs, d1Root);
    }

    private static int RunParent(string data, string corpora, string kind, int seeds, int jobs,
        string? onlyD1Root)
    {
        List<(string Kind, string[] Args, string Label)> cases = [];
        if (kind is "all" or "natural")
        {
            for (int seed = 0; seed < seeds; seed++)
            {
                foreach (string character in Characters)
                {
                    foreach (int ascension in new[] { 0, 10 })
                    {
                        string seedText = $"R1B-{character}-A{ascension}-{seed:D2}";
                        cases.Add(("natural",
                            ["--run-compare-natural", character, ascension.ToString(CultureInfo.InvariantCulture), seedText],
                            $"natural/{character}/A{ascension}/{seedText}"));
                    }
                }
            }
        }
        if (kind is "all" or "d1")
        {
            string[] roots = onlyD1Root != null ? [onlyD1Root] : EnumerateRoots(data);
            foreach (string root in roots)
                cases.Add(("d1", ["--run-compare-d1", root], $"d1/{root}"));
        }
        if (cases.Count == 0)
        {
            Console.Error.WriteLine($"--run-kind {kind} 没有用例。");
            return 2;
        }

        int naturalSame = 0, naturalDiff = 0, naturalReject = 0;
        int d1Same = 0, d1Diff = 0, d1Reject = 0;
        int battleSame = 0, battleDiff = 0, battleReject = 0, battleSkip = 0;
        int rngSame = 0, rngDiff = 0;
        int mapSame = 0, mapDiff = 0;
        object gate = new();
        Console.WriteLine($"RUNIMPORT cases={cases.Count} jobs={jobs}");
        Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = jobs }, item =>
        {
            (int code, string stdout, string stderr) = RunChild(data, corpora, item.Args);
            string status = code == 0 ? "same" : "fail";
            if (item.Kind == "natural")
            {
                lock (gate)
                {
                    string? runLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(static line => line.StartsWith("RUNIMPORT kind=", StringComparison.Ordinal));
                    if (runLine != null && runLine.Contains("status=same", StringComparison.Ordinal))
                        naturalSame++;
                    else if (runLine != null && runLine.Contains("status=reject", StringComparison.Ordinal))
                        naturalReject++;
                    else
                        naturalDiff++;
                    rngSame += Count(stdout, "RNG status=same");
                    rngDiff += Count(stdout, "RNG status=diff");
                    mapSame += Count(stdout, "MAP status=same");
                    mapDiff += Count(stdout, "MAP status=diff");
                }
            }
            else
            {
                lock (gate)
                {
                    string? runLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(static line => line.StartsWith("RUNIMPORT kind=", StringComparison.Ordinal));
                    if (runLine != null && runLine.Contains("status=same", StringComparison.Ordinal))
                        d1Same++;
                    else if (runLine != null && runLine.Contains("status=reject", StringComparison.Ordinal))
                        d1Reject++;
                    else
                        d1Diff++;
                    battleSame += Count(stdout, "BATTLE status=same");
                    battleDiff += Count(stdout, "BATTLE status=diff");
                    battleReject += Count(stdout, "BATTLE status=reject");
                    battleSkip += Count(stdout, "BATTLE status=skip");
                    rngSame += Count(stdout, "RNG status=same");
                    rngDiff += Count(stdout, "RNG status=diff");
                    mapSame += Count(stdout, "MAP status=same");
                    mapDiff += Count(stdout, "MAP status=diff");
                }
            }
            StringBuilder output = new();
            output.Append($"case={item.Label} exit={code} status={status}\n");
            if (stdout.Length > 0)
                output.Append(stdout);
            if (stderr.Length > 0)
                output.Append(stderr);
            Console.Write(output.ToString());
        });
        Console.WriteLine($"RUNIMPORT-SUMMARY natural same={naturalSame} diff={naturalDiff} reject={naturalReject}");
        Console.WriteLine($"RUNIMPORT-SUMMARY d1 same={d1Same} diff={d1Diff} reject={d1Reject}");
        Console.WriteLine($"RUNIMPORT-SUMMARY battle same={battleSame} diff={battleDiff} reject={battleReject} skip={battleSkip}");
        Console.WriteLine($"RUNIMPORT-SUMMARY rng same={rngSame} diff={rngDiff}");
        Console.WriteLine($"RUNIMPORT-SUMMARY map same={mapSame} diff={mapDiff}");
        return naturalDiff + naturalReject + d1Diff + d1Reject + battleDiff + rngDiff + mapDiff == 0
            ? 0
            : 1;
    }

    private static int Count(string text, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static (int Code, string Stdout, string Stderr) RunChild(string data, string corpora,
        string[] extra)
    {
        ProcessStartInfo start = new("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--run-import-compare");
        start.ArgumentList.Add("--data");
        start.ArgumentList.Add(data);
        start.ArgumentList.Add("--corpora");
        start.ArgumentList.Add(corpora);
        foreach (string argument in extra)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动子进程。");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult());
    }

    private static int CompareNatural(string data, string corpora, string characterName,
        int ascension, string seed)
    {
        _ = data;
        _ = corpora;
        MainLoopContext loop = new();
        SynchronizationContext.SetSynchronizationContext(loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();
        CharacterModel character = ModelDb.AllCharacters.FirstOrDefault(
            candidate => candidate.GetType().Name == characterName)
            ?? throw new InvalidDataException($"未知角色 {characterName}。");
        OfflineCombatWaiter waiter = new(loop);
        Task<(NativeRunState Run, Player Player)> start = RunTestHost.StartNewRunAsync(
            waiter, character, ascension, seed);
        loop.RunUntilCompleted(start, TimeSpan.FromSeconds(120), "自然开局");
        (NativeRunState native, Player player) = start.GetAwaiter().GetResult();
        return CompareState("natural", native, player, seed, null, null);
    }

    private static int CompareD1(string data, string corpora, string root)
    {
        MainLoopContext loop = new();
        SynchronizationContext.SetSynchronizationContext(loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();
        OfflineRequest request = Program.LoadRequest(data, corpora, root);
        bool setup = request.FlashTurnSetupRootForTest;
        if (setup)
            TurnSetupRootCapture.BeginBuild();
        Task<CombatState> build = ScenarioBuilder.BuildAsync(new OfflineCombatWaiter(loop), request,
            skipInitialInjection: true, setup ? new TurnSetupCardSelector(0) : null);
        loop.RunUntilCompleted(build, TimeSpan.FromSeconds(600), "建局 " + root);
        CombatState combat = build.GetAwaiter().GetResult();
        Player player = LocalContext.GetMe(combat)
            ?? throw new InvalidOperationException("找不到本地玩家。");
        NativeRunState native = combat.RunState as NativeRunState
            ?? throw new InvalidDataException("原生战斗的 RunState 不是具体 RunState。");
        return CompareState("d1", native, player, root, combat, setup ? TurnSetupRootCapture.StartCards : null);
    }

    private static int CompareState(string kind, NativeRunState native, Player player, string label,
        CombatState? combat, IReadOnlySet<CardModel>? rootCards)
    {
        RunState imported;
        try
        {
            imported = RunStateImporter.Import(native, player);
        }
        catch (UnsupportedImportException error)
        {
            Console.WriteLine($"RUNIMPORT kind={kind} status=reject label={label} reason={error.Reason} detail={error.Detail}");
            return 3;
        }
        catch (CapacityException error)
        {
            Console.WriteLine($"RUNIMPORT kind={kind} status=reject label={label} reason=capacity region={error.Region}");
            return 3;
        }
        string nativeDump = NativeRunDump.Capture(native, player);
        string coreDump = RunDump.Capture(imported);
        List<string> diff = DiffDumps(nativeDump, coreDump);
        if (diff.Count != 0)
        {
            Console.WriteLine($"RUNIMPORT kind={kind} status=diff label={label} diffs={diff.Count}");
            foreach (string line in diff.Take(24))
                Console.WriteLine("  " + line);
        }
        else
        {
            Console.WriteLine($"RUNIMPORT kind={kind} status=same label={label}");
        }

        bool mapDiff = diff.Any(static line => line.StartsWith("map.", StringComparison.Ordinal));
        int edges = 0;
        for (int index = 0; index < imported.Map.NodeCount; index++)
            edges += imported.Map.Nodes[index].ChildCount;
        Console.WriteLine($"MAP status={(mapDiff ? "diff" : "same")} nodes={imported.Map.NodeCount} edges={edges}");

        int rngDiffs = CompareRng(native, player, imported);
        Console.WriteLine($"RNG status={(rngDiffs == 0 ? "same" : "diff")} streams={RunLimits.RngStreams} draws=64 mismatches={rngDiffs}");
        if (rngDiffs != 0)
            return 1;

        if (combat != null)
            CompareBattleView(combat, imported, rootCards);

        return diff.Count == 0 ? 0 : 1;
    }

    private static int CompareRng(NativeRunState native, Player player, RunState imported)
    {
        int mismatches = 0;
        for (int index = 0; index < RunLimits.RngStreams; index++)
        {
            MegaCrit.Sts2.Core.Random.Rng nativeRng = NativeStream(native, player, (RunRngStream)index);
            RngState coreRng = imported.Rng.Streams[index];
            mismatches += CompareDraws(nativeRng, coreRng, RunRng.StreamName((RunRngStream)index));
        }
        for (int act = 0; act < RunLimits.ActMapRngs; act++)
        {
            MegaCrit.Sts2.Core.Random.Rng nativeRng =
                new(native.Rng.Seed, $"act_{act + 1}_map");
            RngState coreRng = RunRng.ActMap(imported.Rng.Seed, act);
            mismatches += CompareDraws(nativeRng, coreRng, $"act_{act + 1}_map");
        }
        MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer synchronizer =
            RunManager.Instance.EventSynchronizer;
        if (synchronizer is { Events.Count: > 0 })
        {
            EventModel? local = synchronizer.GetLocalEvent();
            if (local?.Rng != null && imported.Rng.EventCount > 0)
            {
                mismatches += CompareDraws(local.Rng,
                    imported.Rng.Events[0].State, "event:" + local.Id.Entry);
            }
        }
        return mismatches;
    }

    internal static MegaCrit.Sts2.Core.Random.Rng NativeStream(NativeRunState native, Player player,
        RunRngStream stream) => stream switch
    {
        RunRngStream.UpFront => native.Rng.UpFront,
        RunRngStream.UnknownMapPoint => native.Rng.UnknownMapPoint,
        RunRngStream.TreasureRoomRelics => native.Rng.TreasureRoomRelics,
        RunRngStream.Rewards => player.PlayerRng.Rewards,
        RunRngStream.Shops => player.PlayerRng.Shops,
        RunRngStream.Transformations => player.PlayerRng.Transformations,
        RunRngStream.Shuffle => native.Rng.Shuffle,
        RunRngStream.CombatCardGeneration => native.Rng.CombatCardGeneration,
        RunRngStream.CombatPotionGeneration => native.Rng.CombatPotionGeneration,
        RunRngStream.CombatCardSelection => native.Rng.CombatCardSelection,
        RunRngStream.CombatEnergyCosts => native.Rng.CombatEnergyCosts,
        RunRngStream.CombatTargets => native.Rng.CombatTargets,
        RunRngStream.CombatOrbGeneration => native.Rng.CombatOrbGeneration,
        RunRngStream.MonsterAi => native.Rng.MonsterAi,
        RunRngStream.Niche => native.Rng.Niche,
        _ => throw new ArgumentOutOfRangeException(nameof(stream)),
    };

    internal static int CompareDraws(MegaCrit.Sts2.Core.Random.Rng native, RngState core,
        string stream)
    {
        MegaCrit.Sts2.Core.Random.Rng nativeCopy = new(native.ToSerializable());
        RngState coreCopy = core;
        for (int draw = 0; draw < 64; draw++)
        {
            bool same;
            string nativeValue;
            string coreValue;
            switch (draw % 5)
            {
                case 0:
                {
                    int a = nativeCopy.NextInt(1000);
                    int b = coreCopy.NextInt(1000);
                    same = a == b;
                    nativeValue = a.ToString(CultureInfo.InvariantCulture);
                    coreValue = b.ToString(CultureInfo.InvariantCulture);
                    break;
                }
                case 1:
                {
                    float a = nativeCopy.NextFloat(1f);
                    float b = coreCopy.NextFloat(1f);
                    same = a.Equals(b);
                    nativeValue = a.ToString("R", CultureInfo.InvariantCulture);
                    coreValue = b.ToString("R", CultureInfo.InvariantCulture);
                    break;
                }
                case 2:
                {
                    double a = nativeCopy.NextDouble();
                    double b = coreCopy.NextDouble();
                    same = a.Equals(b);
                    nativeValue = a.ToString("R", CultureInfo.InvariantCulture);
                    coreValue = b.ToString("R", CultureInfo.InvariantCulture);
                    break;
                }
                case 3:
                {
                    int a = nativeCopy.NextInt(-5, 100);
                    int b = coreCopy.NextInt(-5, 100);
                    same = a == b;
                    nativeValue = a.ToString(CultureInfo.InvariantCulture);
                    coreValue = b.ToString(CultureInfo.InvariantCulture);
                    break;
                }
                default:
                {
                    bool a = nativeCopy.NextBool();
                    bool b = coreCopy.NextBool();
                    same = a == b;
                    nativeValue = a.ToString();
                    coreValue = b.ToString();
                    break;
                }
            }
            if (!same || nativeCopy.ToSerializable().counter != coreCopy.Counter)
            {
                Console.WriteLine(
                    $"  RNG-DIFF stream={stream} draw={draw} native={nativeValue} core={coreValue}");
                return 1;
            }
        }
        return 0;
    }

    private static void CompareBattleView(CombatState combat, RunState run,
        IReadOnlySet<CardModel>? rootCards)
    {
        Player player = LocalContext.GetMe(combat)
            ?? throw new InvalidOperationException("找不到本地玩家。");
        Battle battle;
        try
        {
            Rules rules = RulesCompiler.Compile(combat, player, combat.RunState as NativeRunState
                ?? throw new InvalidDataException("原生 RunState 类型不符。"));
            battle = BattleImporter.Import(combat, player, rules, ImportOptions.Default with
            {
                TurnSetupRoot = rootCards != null,
                RootCards = rootCards,
            });
        }
        catch (UnsupportedImportException error)
        {
            Console.WriteLine($"BATTLE status=reject reason={error.Reason} detail={error.Detail}");
            return;
        }
        catch (UnsupportedException error)
        {
            Console.WriteLine($"BATTLE status=reject reason=core-unsupported detail={error.Message}");
            return;
        }
        catch (CapacityException error)
        {
            Console.WriteLine($"BATTLE status=reject reason=capacity region={error.Region}");
            return;
        }
        List<string> differences = CompareBattle(in battle, in run);
        if (differences.Count == 0)
        {
            Console.WriteLine("BATTLE status=same");
            return;
        }
        Console.WriteLine($"BATTLE status=diff diffs={differences.Count}");
        foreach (string difference in differences.Take(24))
            Console.WriteLine("  " + difference);
    }

    private static List<string> CompareBattle(in Battle battle, in RunState run)
    {
        List<string> differences = [];
        for (int index = 0; index < 9; index++)
        {
            RngState expected = run.Rng.Streams[6 + index];
            RngState actual = battle.Rng[index];
            if (!RngEqual(expected, actual))
                differences.Add($"rng.{index}");
        }
        if (battle.Player.CurrentHp != run.Player.CurrentHp
            || battle.Player.MaxHp != run.Player.MaxHp)
            differences.Add($"hp battle={battle.Player.CurrentHp}/{battle.Player.MaxHp} run={run.Player.CurrentHp}/{run.Player.MaxHp}");
        if (battle.RelicCount != run.Relics.Count)
        {
            differences.Add($"relic.count battle={battle.RelicCount} run={run.Relics.Count}");
        }
        else
        {
            for (int index = 0; index < battle.RelicCount; index++)
            {
                Relic relic = battle.Relics[index];
                RunRelic runRelic = run.Relics.Items[index];
                if (relic.Id != runRelic.Id)
                    differences.Add($"relic.{index}.id battle={relic.Id} run={runRelic.Id}");
                bool melted = (relic.Flags & RelicFlags.Melted) != 0;
                bool runMelted = (runRelic.Flags & RunRelicFlags.Melted) != 0;
                if (melted != runMelted)
                    differences.Add($"relic.{index}.melted battle={melted} run={runMelted}");
            }
        }
        for (int slot = 0; slot < Limits.PotionSlots; slot++)
        {
            ushort battlePotion = battle.Potions[slot];
            RunPotion runPotion = slot < run.Player.Potions.Count
                ? run.Player.Potions.Items[slot]
                : default;
            ushort runValue = runPotion.Present == 0 ? (ushort)0 : (ushort)(runPotion.Id + 1);
            if (battlePotion != runValue)
                differences.Add($"potion.{slot} battle={battlePotion} run={runValue}");
        }
        for (int slot = Limits.PotionSlots; slot < run.Player.Potions.Count; slot++)
        {
            if (run.Player.Potions.Items[slot].Present != 0)
                differences.Add($"potion.{slot} battle=<out-of-range> run=present");
        }
        // Compare the persistent deck exactly. Battle rows created at combat start (Toolbox,
        // Bellows and similar) have no DeckVersion and are not part of the run deck.
        Dictionary<ushort, int> battleCards = [];
        for (int index = 0; index < battle.CardCount; index++)
        {
            Card card = battle.Cards[index];
            if ((card.Flags & CardFlags.DeckCard) == 0)
                continue;
            battleCards[card.Id] = battleCards.GetValueOrDefault(card.Id) + 1;
        }
        for (int index = 0; index < run.Deck.Count; index++)
        {
            ushort id = run.Deck.Cards[index].Id;
            if (battleCards.TryGetValue(id, out int count) && count > 0)
                battleCards[id] = count - 1;
            else
                differences.Add($"deck.missing id={Registry.Card(id).NativeId}");
        }
        foreach ((ushort id, int count) in battleCards)
        {
            if (count > 0)
                differences.Add($"deck.extra id={Registry.Card(id).NativeId} count={count}");
        }
        return differences;
    }

    // Compare persistent card identity only. Combat-start effects (Bellows, SwordSage, Dampen)
    // legitimately change the combat copy's upgrade/enchantment while the run deck keeps the
    // persistent value; the dump already compares those persistent values field by field.
    private static uint CardKey(in Card card) => card.Id;

    private static uint CardKey(in RunCard card) => card.Id;

    private static bool RngEqual(in RngState left, in RngState right)
        => left.Counter == right.Counter && left.S0 == right.S0 && left.S1 == right.S1
            && left.S2 == right.S2 && left.S3 == right.S3;

    internal static List<string> DiffDumps(string native, string core)
    {
        string[] nativeLines = native.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string[] coreLines = core.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> nativeValues = Parse(nativeLines);
        Dictionary<string, string> coreValues = Parse(coreLines);
        List<string> differences = [];
        foreach ((string key, string value) in nativeValues)
        {
            if (!coreValues.TryGetValue(key, out string? coreValue))
                differences.Add($"{key}: native={value} core=<missing>");
            else if (!string.Equals(value, coreValue, StringComparison.Ordinal))
                differences.Add($"{key}: native={value} core={coreValue}");
        }
        foreach ((string key, string value) in coreValues)
        {
            if (!nativeValues.ContainsKey(key))
                differences.Add($"{key}: native=<missing> core={value}");
        }
        return differences;
    }

    internal static Dictionary<string, string> Parse(string[] lines)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            int equals = line.IndexOf('=');
            if (equals <= 0)
                continue;
            values[line[..equals]] = line[(equals + 1)..];
        }
        return values;
    }

    private static string[] EnumerateRoots(string data)
    {
        string d1 = Path.Combine(data, "d1");
        return Directory.EnumerateDirectories(d1)
            .Where(directory => File.Exists(Path.Combine(directory, "meta.json")))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()!;
    }
}
