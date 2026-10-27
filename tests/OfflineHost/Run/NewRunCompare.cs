// --new-run-compare: builds the same new run twice in one process — the native
// RunState.CreateForNewRun + RunManager new-run pipeline, and the Core RunGenerator — and compares
// the native dump with the Core dump field by field, including every RNG stream and the consumed
// act-map RNG. Then switches acts through SetActInternal and compares the next maps.
//
// The native boundary is "the first act map is generated, no room entered": RunTestHost stops
// before EnterAct's map-point/ancient entry, which is R2b-2. The tutorial discovery-order flag is
// applied with a test-session patch on RunManager.ShouldApplyTutorialModifications() only; TestMode
// stays on, matching the offline host convention (docs/testing.md 6.4, M2b-0b).
using System.Diagnostics;
using System.Globalization;
using System.Text;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Unlocks;
using RunState = BlitzSolver.Simulation.Run.RunState;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;

namespace OfflineHost;

internal static class NewRunCompare
{
    private static readonly string[] Characters =
        ["Ironclad", "Silent", "Regent", "Necrobinder", "Defect"];

    private const int MaxJobs = 4;

    public static int Run(string[] args)
    {
        string? batch = null;
        string? oneCase = null;
        int seeds = 30;
        int jobs = MaxJobs;
        int shard = 0;
        int shards = 1;
        int acts = 3;
        string saves = "all";
        string tutorial = "both";
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--new-run-batch" when index + 1 < args.Length:
                    batch = args[++index];
                    break;
                case "--new-run-case" when index + 1 < args.Length:
                    oneCase = args[++index];
                    break;
                case "--new-run-seeds" when index + 1 < args.Length:
                    seeds = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--new-run-jobs" when index + 1 < args.Length:
                    jobs = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--new-run-shard" when index + 1 < args.Length:
                {
                    string text = args[++index];
                    string[] parts = text.Split('/');
                    shard = int.Parse(parts[0], CultureInfo.InvariantCulture);
                    shards = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    break;
                }
                case "--new-run-acts" when index + 1 < args.Length:
                    acts = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--new-run-saves" when index + 1 < args.Length:
                    saves = args[++index];
                    break;
                case "--new-run-tutorial" when index + 1 < args.Length:
                    tutorial = args[++index];
                    break;
                default:
                    Console.Error.WriteLine($"未知参数：{args[index]}");
                    return 2;
            }
        }
        if (batch != null)
            return RunBatch(batch, acts);
        if (oneCase != null)
            return RunSingleCase(oneCase, acts);
        if (jobs is < 1 or > MaxJobs)
        {
            Console.Error.WriteLine($"--new-run-jobs 必须在 1 到 {MaxJobs} 之间。");
            return 2;
        }
        if (shards < 1 || shard < 0 || shard >= shards)
        {
            Console.Error.WriteLine("--new-run-shard 必须是 i/n 且 0 <= i < n。");
            return 2;
        }
        if (acts is < 1 or > 3)
        {
            Console.Error.WriteLine("--new-run-acts 必须在 1 到 3 之间。");
            return 2;
        }
        return RunParent(seeds, jobs, shard, shards, acts, saves, tutorial);
    }

    // ------------------------------------------------------------------ presets

    private sealed record SavePreset(string Name, string[] Epochs, string[] Encounters, int Runs,
        bool FromDefaultProgress);

    private static readonly string[] ActOneEncounters =
    [
        "NIBBITS_WEAK", "SLIMES_WEAK", "SHRINKER_BEETLE_WEAK", "INKLETS_NORMAL",
        "BYRDONIS_ELITE", "MAWLER_NORMAL", "SLUDGE_SPINNER_WEAK", "SNAPPING_JAXFRUIT_NORMAL",
        "OVERGROWTH_CRAWLERS",
    ];

    // Real unlock states of three in-game runs captured by CombatSolver's regression snapshots
    // (coverage/fixtures/regressions/snapshots/*.save). The D1 roots in this repository are
    // synthetic combat fixtures that all start from the default progress save, so they carry no
    // partial unlock state; these three are the real partial `encountersSeen`/`NumberOfRuns`
    // states the game produced.
    private static readonly string[] Real360Encounters =
    [
        "BYRDONIS_ELITE", "CORPSE_SLUGS_WEAK", "FUZZY_WURM_CRAWLER_WEAK", "INKLETS_NORMAL",
        "NIBBITS_WEAK", "OVERGROWTH_CRAWLERS", "SHRINKER_BEETLE_WEAK", "SLIMES_WEAK",
        "SLUDGE_SPINNER_WEAK", "SNAPPING_JAXFRUIT_NORMAL", "TOADPOLES_WEAK",
    ];

    private static readonly string[] Real375Encounters =
    [
        "BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER", "BOWLBUGS_WEAK", "BYGONE_EFFIGY_ELITE",
        "BYRDONIS_ELITE", "CEREMONIAL_BEAST_BOSS", "CONSTRUCT_MENAGERIE_NORMAL",
        "CORPSE_SLUGS_NORMAL", "CORPSE_SLUGS_WEAK", "CUBEX_CONSTRUCT_NORMAL",
        "DENSE_VEGETATION_EVENT_ENCOUNTER", "ENTOMANCER_ELITE", "FABRICATOR_NORMAL",
        "FOGMOG_NORMAL", "FOSSIL_STALKER_NORMAL", "FUZZY_WURM_CRAWLER_WEAK",
        "GREMLIN_MERC_NORMAL", "HUNTER_KILLER_NORMAL", "INFESTED_PRISMS_ELITE", "INKLETS_NORMAL",
        "KNIGHTS_ELITE", "KNOWLEDGE_DEMON_BOSS", "LAGAVULIN_MATRIARCH_BOSS",
        "LOUSE_PROGENITOR_NORMAL", "MAWLER_NORMAL", "MECHA_KNIGHT_ELITE", "MYTES_NORMAL",
        "NIBBITS_NORMAL", "NIBBITS_WEAK", "OVERGROWTH_CRAWLERS", "OVICOPTER_NORMAL",
        "PHANTASMAL_GARDENERS_ELITE", "PHROG_PARASITE_ELITE", "PUNCH_CONSTRUCT_NORMAL",
        "QUEEN_BOSS", "SCROLLS_OF_BITING_WEAK", "SEAPUNK_WEAK", "SEWER_CLAM_NORMAL",
        "SHRINKER_BEETLE_WEAK", "SLIMES_WEAK", "SLITHERING_STRANGLER_NORMAL",
        "SLUDGE_SPINNER_WEAK", "SNAPPING_JAXFRUIT_NORMAL", "SOUL_FYSH_BOSS", "TERROR_EEL_ELITE",
        "THE_INSATIABLE_BOSS", "THE_KIN_BOSS", "THE_LOST_AND_FORGOTTEN_NORMAL",
        "THIEVING_HOPPER_WEAK", "TOADPOLES_WEAK", "TURRET_OPERATOR_WEAK",
        "TWO_TAILED_RATS_NORMAL", "VANTOM_BOSS", "VINE_SHAMBLER_NORMAL", "WATERFALL_GIANT_BOSS",
    ];

    private static string[] AllEpochs()
        => [.. EpochModel.AllEpochIds];

    private static string[] RealisticEarlyEpochs(int count)
        => [.. EpochModel.AgnosticUnlocks.Take(count).Select(unlock => unlock.EpochId)];

    private static readonly string[] KnownSaves =
    [
        "empty", "all", "real-360", "real-375", "early-6", "early-12",
    ];

    private static string[] AllEncounters()
        => [.. Enumerable.Range(0, RunCatalog.EncounterCount)
            .Select(index => RunCatalog.EncounterName((ushort)index))];

    private static IReadOnlyList<SavePreset> Presets(string names)
    {
        List<SavePreset> presets = [];
        foreach (string name in names.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            presets.Add(name switch
            {
                "empty" => new SavePreset("empty", [], [], 0, true),
                "all" => new SavePreset("all", AllEpochs(), AllEncounters(), 999999999, false),
                "real-360" => new SavePreset("real-360", AllEpochs(), Real360Encounters, 6, false),
                "real-375" => new SavePreset("real-375", AllEpochs(), Real375Encounters, 15, false),
                "early-6" => new SavePreset("early-6", RealisticEarlyEpochs(6), ActOneEncounters, 1,
                    false),
                "early-12" => new SavePreset("early-12", RealisticEarlyEpochs(12), ActOneEncounters,
                    3, false),
                _ => throw new InvalidDataException($"未知存档预设 {name}。"),
            });
        }
        return presets;
    }

    private static bool[] TutorialFlags(string mode) => mode switch
    {
        "on" => [true],
        "off" => [false],
        "both" => [false, true],
        _ => throw new InvalidDataException($"未知教学重排开关 {mode}。"),
    };

    // ------------------------------------------------------------------ parent

    private static int RunParent(int seeds, int jobs, int shard, int shards, int acts,
        string saveNames, string tutorialMode)
    {
        string[] presets = saveNames.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (string preset in presets)
        {
            if (Array.IndexOf(KnownSaves, preset) < 0)
            {
                Console.Error.WriteLine($"未知存档预设 {preset}。");
                return 2;
            }
        }
        bool[] tutorials = TutorialFlags(tutorialMode);
        List<string> cases = [];
        foreach (string character in Characters)
        {
            for (int ascension = 0; ascension <= 10; ascension++)
            {
                foreach (string preset in presets)
                {
                    foreach (bool tutorial in tutorials)
                    {
                        for (int seed = 0; seed < seeds; seed++)
                        {
                            cases.Add(string.Join('|', character,
                                ascension.ToString(CultureInfo.InvariantCulture),
                                preset,
                                tutorial ? "1" : "0",
                                $"R2B1-{character}-A{ascension}-{preset}-{(tutorial ? "T" : "N")}-{seed:D2}"));
                        }
                    }
                }
            }
        }
        List<string> selected = [];
        for (int index = 0; index < cases.Count; index++)
        {
            if (index % shards == shard)
                selected.Add(cases[index]);
        }
        if (selected.Count == 0)
        {
            Console.Error.WriteLine("没有用例。");
            return 2;
        }
        string directory = Path.Combine(Directory.GetCurrentDirectory(), ".local", "tool-tasks",
            "blitz", "new-run-batches");
        Directory.CreateDirectory(directory);
        List<string> batches = [];
        for (int worker = 0; worker < jobs; worker++)
        {
            string path = Path.Combine(directory,
                $"batch-{Environment.ProcessId}-{shard}-{worker}.txt");
            using StreamWriter writer = new(path);
            for (int index = worker; index < selected.Count; index += jobs)
                writer.WriteLine(selected[index]);
            batches.Add(path);
        }
        Console.WriteLine(
            $"NEWRUN cases={selected.Count} jobs={jobs} shard={shard}/{shards} acts={acts} saves={saveNames} tutorial={tutorialMode}");
        object gate = new();
        int same = 0, diff = 0, reject = 0, error = 0, mapDiff = 0, rngDiff = 0;
        Dictionary<string, (int Same, int Diff, int Reject, int Error)> groups = [];
        Dictionary<string, int> rejects = [];
        Parallel.ForEach(batches, new ParallelOptions { MaxDegreeOfParallelism = jobs }, path =>
        {
            (int code, string stdout, string stderr) = RunChild(path, acts);
            lock (gate)
            {
                foreach (string line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!line.StartsWith("CASE ", StringComparison.Ordinal))
                        continue;
                    string label = Field(line, "case");
                    string status = Field(line, "status");
                    (string character, int ascension, string save, string tutorial) =
                        ParseLabel(label);
                    string group = $"{character}/A{ascension}/{save}/t{tutorial}";
                    (int Same, int Diff, int Reject, int Error) counts =
                        groups.TryGetValue(group, out var existing) ? existing : (0, 0, 0, 0);
                    switch (status)
                    {
                        case "same":
                            same++;
                            counts.Same++;
                            break;
                        case "diff":
                            diff++;
                            counts.Diff++;
                            break;
                        case "reject":
                            reject++;
                            counts.Reject++;
                            string reason = Field(line, "reason");
                            rejects[reason] = rejects.GetValueOrDefault(reason) + 1;
                            break;
                        default:
                            error++;
                            counts.Error++;
                            break;
                    }
                    groups[group] = counts;
                    if (status == "diff")
                    {
                        if (Field(line, "map") == "diff")
                            mapDiff++;
                        if (Field(line, "rng") == "diff")
                            rngDiff++;
                    }
                }
                if (code != 0 && stderr.Length > 0)
                    Console.WriteLine($"CHILD-ERROR exit={code} {stderr.Trim().Split('\n')[0]}");
                foreach (string line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("DIFF ", StringComparison.Ordinal)
                        || line.StartsWith("CASE ", StringComparison.Ordinal) && line.Contains("status=diff"))
                        Console.WriteLine(line);
                }
            }
        });
        foreach (string path in batches)
            File.Delete(path);
        Console.WriteLine($"NEWRUN-SUMMARY same={same} diff={diff} reject={reject} error={error}");
        Console.WriteLine($"NEWRUN-SUMMARY mapDiff={mapDiff} rngDiff={rngDiff}");
        if (rejects.Count != 0)
        {
            foreach ((string reason, int count) in rejects.OrderBy(pair => pair.Key,
                         StringComparer.Ordinal))
                Console.WriteLine($"NEWRUN-REJECT reason={reason} count={count}");
        }
        foreach ((string group, (int groupSame, int groupDiff, int groupReject, int groupError)) in
                 groups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Console.WriteLine(
                $"NEWRUN-GROUP {group} same={groupSame} diff={groupDiff} reject={groupReject} error={groupError}");
        }
        return diff + reject + error + mapDiff + rngDiff == 0 ? 0 : 1;
    }

    private static (string Character, int Ascension, string Save, string Tutorial)
        ParseLabel(string label)
    {
        string[] parts = label.Split('|');
        return (parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture), parts[2], parts[3]);
    }

    private static string Field(string line, string name)
    {
        int index = line.IndexOf(" " + name + "=", StringComparison.Ordinal);
        if (index < 0)
            return "";
        int start = index + name.Length + 2;
        int end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }

    private static (int Code, string Stdout, string Stderr) RunChild(string batch, int acts)
    {
        ProcessStartInfo start = new("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--new-run-compare");
        start.ArgumentList.Add("--new-run-batch");
        start.ArgumentList.Add(batch);
        start.ArgumentList.Add("--new-run-acts");
        start.ArgumentList.Add(acts.ToString(CultureInfo.InvariantCulture));
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动子进程。");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    // ------------------------------------------------------------------ child

    private static int RunBatch(string batchPath, int acts)
    {
        MainLoopContext loop = Initialize();
        ContentTable content = ContentTableExporter.Export();
        int same = 0, diff = 0, reject = 0, error = 0;
        foreach (string line in File.ReadLines(batchPath))
        {
            if (line.Length == 0)
                continue;
            string[] parts = line.Split('|');
            string label = line;
            try
            {
                CaseResult result = RunCase(content, loop, parts[0],
                    int.Parse(parts[1], CultureInfo.InvariantCulture), parts[4], parts[2],
                    parts[3] == "1", acts);
                Console.WriteLine(
                    $"CASE case={label} status={result.Status} acts={result.Acts} map={result.Map} rng={result.Rng}");
                foreach (string detail in result.Details)
                    Console.WriteLine($"DIFF {label} {detail}");
                switch (result.Status)
                {
                    case "same":
                        same++;
                        break;
                    case "diff":
                        diff++;
                        break;
                    case "reject":
                        reject++;
                        break;
                    default:
                        error++;
                        break;
                }
            }
            catch (ContentUnavailableException exception)
            {
                Console.WriteLine($"CASE case={label} status=reject reason={exception.Reason} acts=0 map=same rng=same");
                reject++;
            }
            catch (UnsupportedException exception)
            {
                Console.WriteLine($"CASE case={label} status=reject reason=unsupported acts=0 map=same rng=same detail={exception.Message.Replace(' ', '_')}");
                reject++;
            }
            catch (CapacityException exception)
            {
                Console.WriteLine($"CASE case={label} status=reject reason=capacity acts=0 map=same rng=same region={exception.Region}");
                reject++;
            }
            catch (InvalidDataException exception)
            {
                Console.WriteLine($"CASE case={label} status=error reason=invalid-data acts=0 map=same rng=same detail={exception.Message.Replace(' ', '_')}");
                error++;
            }
            if (RunManager.Instance.IsInProgress)
                RunManager.Instance.CleanUp(graceful: false);
        }
        Console.WriteLine($"CHILD same={same} diff={diff} reject={reject} error={error}");
        return diff + reject + error == 0 ? 0 : 1;
    }

    private static int RunSingleCase(string label, int acts)
    {
        MainLoopContext loop = Initialize();
        ContentTable content = ContentTableExporter.Export();
        string[] parts = label.Split('|');
        CaseResult result = RunCase(content, loop, parts[0],
            int.Parse(parts[1], CultureInfo.InvariantCulture), parts[4], parts[2], parts[3] == "1",
            acts);
        Console.WriteLine(
            $"CASE case={label} status={result.Status} acts={result.Acts} map={result.Map} rng={result.Rng}");
        foreach (string detail in result.Details)
            Console.WriteLine($"DIFF {label} {detail}");
        if (RunManager.Instance.IsInProgress)
            RunManager.Instance.CleanUp(graceful: false);
        return result.Status == "same" ? 0 : 1;
    }

    private sealed record CaseResult(string Status, int Acts, string Map, string Rng,
        List<string> Details);

    private static MainLoopContext Initialize()
    {
        MainLoopContext loop = new();
        SynchronizationContext.SetSynchronizationContext(loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();
        InstallTutorialPatch();
        return loop;
    }

    // ------------------------------------------------------------------ tutorial patch

    private static int _tutorialOverride = -1;

    private static void InstallTutorialPatch()
    {
        System.Reflection.MethodInfo target = AccessTools.Method(typeof(RunManager),
            nameof(RunManager.ShouldApplyTutorialModifications));
        System.Reflection.MethodInfo prefix = AccessTools.Method(typeof(NewRunCompare),
            nameof(TutorialPrefix));
        GameBootstrap.Harmony.Patch(target, prefix: new HarmonyMethod(prefix));
    }

    private static bool TutorialPrefix(ref bool __result)
    {
        if (_tutorialOverride < 0)
            return true;
        __result = _tutorialOverride != 0;
        return false;
    }

    // ------------------------------------------------------------------ one case

    private static CaseResult RunCase(ContentTable content, MainLoopContext loop,
        string characterName, int ascension, string seed, string saveName, bool tutorial, int acts)
    {
        _tutorialOverride = tutorial ? 1 : 0;
        SavePreset preset = Presets(saveName)[0];
        CharacterModel character = ModelDb.AllCharacters.FirstOrDefault(
            candidate => candidate.GetType().Name == characterName)
            ?? throw new InvalidDataException($"未知角色 {characterName}。");
        if (!RunCatalog.TryCharacter(character.Id.Entry, out ushort characterId))
            throw new InvalidDataException($"角色 {character.Id.Entry} 不在目录内。");

        UnlockState nativeUnlock = preset.FromDefaultProgress
            ? SaveManager.Instance.GenerateUnlockStateFromProgress()
            : BuildNativeUnlock(preset);
        Task<(NativeRunState Run, Player Player)> start =
            RunTestHost.StartNewRunBeforeChoicesAsync(character, ascension, seed, nativeUnlock);
        loop.RunUntilCompleted(start, TimeSpan.FromSeconds(120), "新局 " + characterName);
        (NativeRunState native, Player player) = start.GetAwaiter().GetResult();

        RunUnlockState coreUnlock = BuildCoreUnlock(preset);
        RunState blitz = RunGenerator.CreateNewRun(content, new NewRunRequest
        {
            Character = characterId,
            Ascension = ascension,
            Seed = seed,
            Unlock = coreUnlock,
            TutorialModifications = tutorial,
        }, out RngState actMapRng);

        List<string> details = [];
        string map = "same";
        string rng = "same";
        int compared = 0;
        CompareAct(native, player, blitz, 0, actMapRng, details, ref map, ref rng);
        compared++;
        for (int act = 1; act < acts; act++)
        {
            Task setAct = RunTestHost.SetActAsync(act);
            loop.RunUntilCompleted(setAct, TimeSpan.FromSeconds(120),
                $"换幕 {act} " + characterName);
            player = LocalContext.GetMe(native)
                ?? throw new InvalidDataException("换幕后找不到本地玩家。");
            RunGenerator.SetAct(content, ref blitz, act, out RngState mapRng);
            CompareAct(native, player, blitz, act, mapRng, details, ref map, ref rng);
            compared++;
        }
        string status = details.Count == 0 ? "same" : "diff";
        return new CaseResult(status, compared, map, rng, details);
    }

    private static void CompareAct(NativeRunState native, Player player, RunState blitz,
        int actIndex, RngState actMapRng, List<string> details, ref string map, ref string rng)
    {
        RunState imported = RunStateImporter.Import(native, player);
        string nativeDump = NativeRunDump.Capture(native, player);
        string coreDump = RunDump.Capture(blitz);
        List<string> differences = RunImportCompare.DiffDumps(nativeDump, coreDump);
        if (differences.Any(line => line.StartsWith("map.", StringComparison.Ordinal)
                || line.StartsWith("act.", StringComparison.Ordinal)))
            map = "diff";

        MegaCrit.Sts2.Core.Random.Rng probe =
            new(native.Rng.Seed, $"act_{actIndex + 1}_map");
        _ = new StandardActMap(probe, native.Act, isMultiplayer: false,
            shouldReplaceTreasureWithElites: false, native.Act.HasSecondBoss);
        SerializableRng consumed = probe.ToSerializable();
        bool mapRngSame = consumed.counter == actMapRng.Counter
            && consumed.state0 == actMapRng.S0 && consumed.state1 == actMapRng.S1
            && consumed.state2 == actMapRng.S2 && consumed.state3 == actMapRng.S3;
        if (!mapRngSame)
        {
            rng = "diff";
            differences.Add(
                $"rng.map.{actIndex + 1}.consumed: native={consumed.counter}/"
                + $"{consumed.state0:x16}{consumed.state1:x16}{consumed.state2:x16}{consumed.state3:x16} "
                + $"core={actMapRng.Counter}/"
                + $"{actMapRng.S0:x16}{actMapRng.S1:x16}{actMapRng.S2:x16}{actMapRng.S3:x16}");
        }
        int draws = 0;
        for (int index = 0; index < RunLimits.RngStreams; index++)
        {
            MegaCrit.Sts2.Core.Random.Rng nativeRng = RunImportCompare.NativeStream(native, player,
                (RunRngStream)index);
            draws += RunImportCompare.CompareDraws(nativeRng, imported.Rng.Streams[index],
                RunRng.StreamName((RunRngStream)index));
        }
        if (draws != 0)
        {
            rng = "diff";
            differences.Add($"rng.streams.draws: mismatches={draws}");
        }
        for (int index = 0; index < Math.Min(8, differences.Count); index++)
            details.Add(differences[index]);
    }

    // ------------------------------------------------------------------ unlock states

    private static UnlockState BuildNativeUnlock(SavePreset preset)
    {
        HashSet<ModelId> encounters = [];
        foreach (string name in preset.Encounters)
        {
            // Native UnlockState only compares ModelIds; event encounters are not all listed in
            // ModelDb.AllEncounters, so fall back to the canonical id form.
            EncounterModel? model = ModelDb.All.OfType<EncounterModel>()
                .FirstOrDefault(candidate => candidate.Id.Entry == name);
            encounters.Add(model is null ? new ModelId("ENCOUNTER", name) : model.Id);
        }
        return new UnlockState(preset.Epochs, encounters, preset.Runs);
    }

    private static RunUnlockState BuildCoreUnlock(SavePreset preset)
    {
        RunUnlockState unlock = default;
        unlock.NumberOfRuns = preset.Runs;
        foreach (string name in preset.Epochs)
        {
            if (!RunCatalog.TryEpoch(name, out ushort id))
                throw new InvalidDataException($"epoch {name} 不在目录内。");
            unlock.Epochs[id >> 6] |= 1UL << (id & 63);
        }
        foreach (string name in preset.Encounters)
        {
            if (!RunCatalog.TryEncounter(name, out ushort id))
                throw new InvalidDataException($"遭遇 {name} 不在目录内。");
            unlock.EncountersSeen[id >> 6] |= 1UL << (id & 63);
        }
        return unlock;
    }
}
