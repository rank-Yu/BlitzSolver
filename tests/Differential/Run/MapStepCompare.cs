// --map-step-compare: drive a native run from a new run to the first choice of each map point,
// then at every map decision execute each legal option from the *same* native snapshot:
//   native: RunManager.EnterMapCoord, with a test-only Harmony prefix that stops a non-combat
//           room just before EnterRoomInternal (the "room built, history appended, not entered"
//           boundary) and lets combat continue to the playable first turn;
//   blitz:  RunStateImporter.Import(native) + RunMapEntry.Enter.
// Combat options compare Battle/Rules/RunState like R2a-2; non-combat options compare the
// RunState at the before-entry boundary. The native snapshot is SerializableRun, restored with
// SetUpSavedSingleplayer + GenerateMap, so every option starts from the identical state.
//
// Shortcuts (never inside a comparison): combats are won by killing the enemies immediately;
// reward screens, shop stock, treasure contents and event pages are skipped; act transitions use
// RunManager.EnterNextAct. The import for each comparison is taken after the shortcut, so the
// shortcut only determines which states are reached, not what is compared.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Unlocks;
using NativeCreature = MegaCrit.Sts2.Core.Entities.Creatures.Creature;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;
using RunState = BlitzSolver.Simulation.Run.RunState;

namespace Differential;

internal static class MapStepCompare
{
    private static readonly string[] Characters =
        ["Ironclad", "Silent", "Regent", "Necrobinder", "Defect"];

    private static readonly string[] Presets = ["empty", "all"];
    private const int MaxJobs = 4;

    private static ContentTable? _table;
    private static OfflineCombatWaiter? _waiter;
    private static MainLoopContext? _loop;

    public static int Run(string[] args)
    {
        string? batch = null;
        string? oneCase = null;
        int seeds = 6;
        int jobs = MaxJobs;
        int shard = 0;
        int shards = 1;
        int acts = 3;
        string characters = string.Join(',', Characters);
        string presets = string.Join(',', Presets);
        string tutorial = "both";
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--map-step-batch" when index + 1 < args.Length:
                    batch = args[++index];
                    break;
                case "--map-step-case" when index + 1 < args.Length:
                    oneCase = args[++index];
                    break;
                case "--map-step-seeds" when index + 1 < args.Length:
                    seeds = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--map-step-jobs" when index + 1 < args.Length:
                    jobs = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--map-step-shard" when index + 1 < args.Length:
                {
                    string text = args[++index];
                    string[] parts = text.Split('/');
                    shard = int.Parse(parts[0], CultureInfo.InvariantCulture);
                    shards = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    break;
                }
                case "--map-step-acts" when index + 1 < args.Length:
                    acts = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--map-step-characters" when index + 1 < args.Length:
                    characters = args[++index];
                    break;
                case "--map-step-saves" when index + 1 < args.Length:
                    presets = args[++index];
                    break;
                case "--map-step-tutorial" when index + 1 < args.Length:
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
            Console.Error.WriteLine($"--map-step-jobs 必须在 1 到 {MaxJobs} 之间。");
            return 2;
        }
        if (shards < 1 || shard < 0 || shard >= shards)
        {
            Console.Error.WriteLine("--map-step-shard 必须是 i/n 且 0 <= i < n。");
            return 2;
        }
        if (acts is < 1 or > 3)
        {
            Console.Error.WriteLine("--map-step-acts 必须在 1 到 3 之间。");
            return 2;
        }
        return RunParent(seeds, jobs, shard, shards, acts, characters, presets, tutorial);
    }

    // ------------------------------------------------------------------ parent

    private static int RunParent(int seeds, int jobs, int shard, int shards, int acts,
        string characterNames, string saveNames, string tutorialMode)
    {
        string[] characters = characterNames.Split(',', StringSplitOptions.RemoveEmptyEntries);
        string[] presets = saveNames.Split(',', StringSplitOptions.RemoveEmptyEntries);
        bool[] tutorials = tutorialMode switch
        {
            "on" => [true],
            "off" => [false],
            "both" => [false, true],
            _ => throw new InvalidDataException($"未知教学开关 {tutorialMode}。"),
        };
        List<string> cases = [];
        foreach (string character in characters)
        {
            foreach (int ascension in new[] { 0, 10 })
            {
                foreach (string preset in presets)
                {
                    foreach (bool tutorial in tutorials)
                    {
                        for (int seed = 0; seed < seeds; seed++)
                        {
                            cases.Add(string.Join('|', character,
                                ascension.ToString(CultureInfo.InvariantCulture), preset,
                                tutorial ? "1" : "0",
                                $"R2B2-{character}-A{ascension}-{preset}-{(tutorial ? "T" : "N")}-{seed:D2}"));
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
            "blitz", "map-step-batches");
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
            $"MAPSTEP cases={selected.Count} jobs={jobs} shard={shard}/{shards} acts={acts} saves={saveNames} tutorial={tutorialMode}");
        object gate = new();
        int same = 0, diff = 0, reject = 0, error = 0;
        int decisionsSame = 0, decisionsDiff = 0;
        Dictionary<string, (int Same, int Diff, int Reject, int Error)> caseGroups = [];
        Dictionary<string, int> rejects = [];
        Dictionary<string, (int Same, int Diff)> points = [];
        Dictionary<string, (int Same, int Diff)> pointRooms = [];
        Dictionary<string, (int Same, int Diff)> actsSeen = [];
        Dictionary<string, (int Same, int Diff)> floors = [];
        Dictionary<string, int> pointRejects = [];
        Parallel.ForEach(batches, new ParallelOptions { MaxDegreeOfParallelism = jobs }, path =>
        {
            (int code, string stdout, string stderr) = RunChildProcess(path, acts);
            lock (gate)
            {
                foreach (string line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("CASE ", StringComparison.Ordinal))
                    {
                        string label = Field(line, "case");
                        string status = Field(line, "status");
                        string[] parts = label.Split('|');
                        string group = $"{parts[0]}/A{parts[1]}/{parts[2]}/t{parts[3]}";
                        (int Same, int Diff, int Reject, int Error) counts =
                            caseGroups.TryGetValue(group, out var existing) ? existing : (0, 0, 0, 0);
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
                            default:
                                error++;
                                counts.Error++;
                                break;
                        }
                        caseGroups[group] = counts;
                    }
                    else if (line.StartsWith("DECISION ", StringComparison.Ordinal))
                    {
                        if (Field(line, "status") == "same")
                            decisionsSame++;
                        else
                            decisionsDiff++;
                    }
                    else if (line.StartsWith("STEP ", StringComparison.Ordinal))
                    {
                        string status = Field(line, "status");
                        string point = Field(line, "point");
                        string room = Field(line, "room");
                        string act = Field(line, "act");
                        string floor = Field(line, "floor");
                        if (status == "reject")
                        {
                            reject++;
                            string reason = Field(line, "reason");
                            rejects[reason] = rejects.GetValueOrDefault(reason) + 1;
                            pointRejects[point + "/" + reason] =
                                pointRejects.GetValueOrDefault(point + "/" + reason) + 1;
                            string[] rejectedParts = Field(line, "case").Split('|');
                            if (rejectedParts.Length >= 4)
                            {
                                string rejectedGroup =
                                    $"{rejectedParts[0]}/A{rejectedParts[1]}/{rejectedParts[2]}/t{rejectedParts[3]}";
                                (int Same, int Diff, int Reject, int Error) rejectedCounts =
                                    caseGroups.TryGetValue(rejectedGroup, out var rejectedExisting)
                                        ? rejectedExisting : (0, 0, 0, 0);
                                caseGroups[rejectedGroup] =
                                    (rejectedCounts.Same, rejectedCounts.Diff,
                                        rejectedCounts.Reject + 1, rejectedCounts.Error);
                            }
                            continue;
                        }
                        bool sameStep = status == "same";
                        if (status == "error")
                            error++;
                        Accumulate(points, point, sameStep);
                        Accumulate(pointRooms, point + "/" + room, sameStep);
                        Accumulate(actsSeen, act, sameStep);
                        Accumulate(floors, floor, sameStep);
                    }
                }
                if (code != 0 && stderr.Length > 0)
                    Console.WriteLine($"CHILD-ERROR exit={code} {stderr.Trim().Split('\n')[0]}");
                foreach (string line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("DIFF ", StringComparison.Ordinal))
                        Console.WriteLine(line);
                }
            }
        });
        foreach (string path in batches)
            File.Delete(path);
        Console.WriteLine($"MAPSTEP-SUMMARY same={same} diff={diff} reject={reject} error={error}");
        Console.WriteLine(
            $"MAPSTEP-DECISIONS same={decisionsSame} diff={decisionsDiff}");
        PrintGroup("MAPSTEP-POINT", points);
        PrintGroup("MAPSTEP-POINT-ROOM", pointRooms);
        PrintGroup("MAPSTEP-ACT", actsSeen);
        PrintGroup("MAPSTEP-FLOOR", floors);
        foreach ((string reason, int count) in rejects.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
            Console.WriteLine($"MAPSTEP-REJECT reason={reason} count={count}");
        foreach ((string key, int count) in pointRejects.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
            Console.WriteLine($"MAPSTEP-POINT-REJECT {key} count={count}");
        foreach ((string group, (int groupSame, int groupDiff, int groupReject, int groupError)) in
                 caseGroups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Console.WriteLine(
                $"MAPSTEP-GROUP {group} same={groupSame} diff={groupDiff} reject={groupReject} error={groupError}");
        }
        return diff + error + decisionsDiff == 0 ? 0 : 1;
    }

    private static void Accumulate(Dictionary<string, (int Same, int Diff)> counts, string key,
        bool same)
    {
        (int currentSame, int currentDiff) = counts.GetValueOrDefault(key);
        counts[key] = same ? (currentSame + 1, currentDiff) : (currentSame, currentDiff + 1);
    }

    private static void PrintGroup(string prefix, Dictionary<string, (int Same, int Diff)> counts)
    {
        foreach ((string key, (int same, int diff)) in counts.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
            Console.WriteLine($"{prefix} {key} same={same} diff={diff}");
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

    private static (int Code, string Stdout, string Stderr) RunChildProcess(string batch, int acts)
    {
        ProcessStartInfo start = new("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--map-step-compare");
        start.ArgumentList.Add("--map-step-batch");
        start.ArgumentList.Add(batch);
        start.ArgumentList.Add("--map-step-acts");
        start.ArgumentList.Add(acts.ToString(CultureInfo.InvariantCulture));
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动子进程。");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.GetAwaiter().GetResult(),
            stderr.GetAwaiter().GetResult());
    }

    // ------------------------------------------------------------------ child

    private static int RunBatch(string batchPath, int acts)
    {
        Initialize();
        int same = 0, diff = 0, error = 0;
        foreach (string line in File.ReadLines(batchPath))
        {
            if (line.Length == 0)
                continue;
            string[] parts = line.Split('|');
            try
            {
                CaseResult result = RunCase(parts[0],
                    int.Parse(parts[1], CultureInfo.InvariantCulture), parts[2], parts[3] == "1",
                    parts[4], acts);
                Console.WriteLine(
                    $"CASE case={line} status={result.Status} decisions={result.Decisions} options={result.Options} rejects={result.Rejects} diffs={result.Diffs}");
                switch (result.Status)
                {
                    case "same":
                        same++;
                        break;
                    case "diff":
                        diff++;
                        break;
                    default:
                        error++;
                        break;
                }
            }
            catch (InvalidDataException exception)
            {
                Error(line, exception.Message);
                error++;
            }
            catch (UnsupportedImportException exception)
            {
                Error(line, exception.Message);
                error++;
            }
            catch (ContentUnavailableException exception)
            {
                Error(line, exception.Message);
                error++;
            }
            catch (BlitzSolver.Simulation.Combat.UnsupportedException exception)
            {
                Error(line, exception.Message);
                error++;
            }
            catch (CapacityException exception)
            {
                Error(line, exception.Message);
                error++;
            }
            catch (TimeoutException exception)
            {
                Error(line, exception.Message);
                error++;
            }
            catch (InvalidOperationException exception)
            {
                Error(line, exception.Message);
                error++;
            }
            if (RunManager.Instance.IsInProgress)
                RunManager.Instance.CleanUp(graceful: false);
        }
        Console.WriteLine($"CHILD same={same} diff={diff} error={error}");
        return error == 0 ? 0 : 1;
    }

    private static void Error(string label, string detail)
        => Console.WriteLine($"CASE case={label} status=error detail={Short(detail)}");

    private static int RunSingleCase(string label, int acts)
    {
        Initialize();
        string[] parts = label.Split('|');
        CaseResult result = RunCase(parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture),
            parts[2], parts[3] == "1", parts[4], acts);
        Console.WriteLine(
            $"CASE case={label} status={result.Status} decisions={result.Decisions} options={result.Options} rejects={result.Rejects} diffs={result.Diffs}");
        if (RunManager.Instance.IsInProgress)
            RunManager.Instance.CleanUp(graceful: false);
        return result.Status == "same" ? 0 : 1;
    }

    private sealed record CaseResult(string Status, int Decisions, int Options, int Rejects,
        int Diffs);

    private sealed class CaseState(string label, string caseKey)
    {
        public string Label { get; } = label;
        public string CaseKey { get; } = caseKey;
        public int Decisions;
        public int Options;
        public int Rejects;
        public int Diffs;

        public void Diff(string detail)
        {
            Diffs++;
            Console.WriteLine($"DIFF {CaseKey} {detail}");
        }

        public void Step(int act, int floor, RunMapPointType point, RunRoomType room,
            string status, string reason = "")
        {
            string suffix = reason.Length == 0 ? "" : $" reason={reason}";
            Console.WriteLine(
                $"STEP case={CaseKey} act={act} floor={floor} point={point} room={room} status={status}{suffix}");
        }
    }

    private static void Initialize()
    {
        _loop = new MainLoopContext();
        SynchronizationContext.SetSynchronizationContext(_loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();
        InstallTutorialPatch();
        InstallCapturePatch();
        InstallSavePatch();
        _table = ContentTableExporter.Export();
        _waiter = new OfflineCombatWaiter(_loop);
    }

    // ------------------------------------------------------------------ driving

    private static CaseResult RunCase(string characterName, int ascension, string presetName,
        bool tutorial, string seed, int acts)
    {
        CaseState state = new(seed,
            $"{characterName}|{ascension}|{presetName}|{(tutorial ? 1 : 0)}|{seed}");
        _tutorialOverride = tutorial ? 1 : 0;
        SavePreset preset = Preset(presetName);
        CharacterModel character = ModelDb.AllCharacters.FirstOrDefault(
            candidate => candidate.GetType().Name == characterName)
            ?? throw new InvalidDataException($"未知角色 {characterName}。");
        UnlockState nativeUnlock = preset.FromDefaultProgress
            ? SaveManager.Instance.GenerateUnlockStateFromProgress()
            : BuildNativeUnlock(preset);
        Task<(NativeRunState Run, Player Player)> start =
            RunTestHost.StartNewRunBeforeChoicesAsync(character, ascension, seed, nativeUnlock);
        _loop!.RunUntilCompleted(start, TimeSpan.FromSeconds(120), "新局 " + characterName);
        NativeRunState native = start.GetAwaiter().GetResult().Run;

        if (native.ExtraFields.StartedWithNeow)
        {
            // EnterAct(0) would enter the starting Ancient point without a choice. Skip its event
            // page (R2f) and stand on the map with the ancient already visited.
            ArmCapture();
            _loop.RunUntilCompleted(RunManager.Instance.EnterMapCoord(native.Map.StartingMapPoint.coord),
                TimeSpan.FromSeconds(120), "Neow 起点");
            _armed = false;
            _captured = null;
            _loop.RunUntilCompleted(RunManager.Instance.EnterRoom(new MapRoom()),
                TimeSpan.FromSeconds(120), "Neow 后地图");
        }

        int actIndex = native.CurrentActIndex;
        int decisionIndex = 0;
        while (actIndex < acts)
        {
            while (true)
            {
                native = RunManager.Instance.State
                    ?? throw new InvalidDataException("跑局状态丢失。");
                Player player = LocalContext.GetMe(native)
                    ?? throw new InvalidDataException("找不到本地玩家。");
                List<MapPoint> options = [.. RunTestHost.GetTravelablePoints(native)];
                if (options.Count == 0)
                    break;
                CompareDecision(state, native, player, options, decisionIndex);
                decisionIndex++;
            }
            if (actIndex >= acts - 1)
                break;
            _loop.RunUntilCompleted(RunManager.Instance.EnterNextAct(),
                TimeSpan.FromSeconds(120), "换幕");
            native = RunManager.Instance.State
                ?? throw new InvalidDataException("换幕后跑局状态丢失。");
            actIndex = native.CurrentActIndex;
            decisionIndex = 0;
        }
        string status = state.Diffs == 0 ? "same" : "diff";
        return new CaseResult(status, state.Decisions, state.Options, state.Rejects, state.Diffs);
    }

    private static void CompareDecision(CaseState state, NativeRunState native, Player player,
        List<MapPoint> options, int decisionIndex)
    {
        RunState imported = RunStateImporter.Import(native, player);
        Span<RunCoord> points = stackalloc RunCoord[RunLimits.DecisionOptions];
        int pointCount = RunTravel.Collect(in imported, points);
        List<MapPoint> sortedOptions = [.. options.OrderBy(option => option.coord.col)
            .ThenBy(option => option.coord.row)];
        bool same = pointCount == sortedOptions.Count;
        if (same)
        {
            for (int index = 0; index < sortedOptions.Count; index++)
            {
                RunMapPointType type = RunTravel.TryFindPoint(in imported, points[index], out RunMapPoint point)
                    ? point.Type : RunMapPointType.Unassigned;
                if (!points[index].Equals(Coord(sortedOptions[index].coord))
                    || type != (RunMapPointType)(int)sortedOptions[index].PointType)
                {
                    same = false;
                    break;
                }
            }
        }
        state.Decisions++;
        Console.WriteLine(
            $"DECISION case={state.Label} act={native.CurrentActIndex + 1} floor={native.ActFloor} options={options.Count} status={(same ? "same" : "diff")}");
        if (!same)
        {
            state.Diff(
                $"decision act={native.CurrentActIndex + 1} floor={native.ActFloor} native={OptionText(sortedOptions)} blitz={PointText(points[..pointCount])}");
        }

        SerializableRun snapshot = RunManager.Instance.ToSave(null);
        int chosen = (int)((RunRng.SeedFromString(state.Label)
            + (ulong)decisionIndex * 0x9E3779B97F4A7C15UL) % (ulong)options.Count);
        for (int index = 0; index < options.Count; index++)
        {
            if (index == chosen)
                continue;
            CompareOption(state, imported, snapshot, options[index], false);
        }
        CompareOption(state, imported, snapshot, options[chosen], true);
    }

    private static void CompareOption(CaseState state, RunState imported, SerializableRun snapshot,
        MapPoint option, bool continueAfter)
    {
        int floor = option.coord.row + 1;
        RunMapPointType pointType = (RunMapPointType)(int)option.PointType;
        try
        {
            RestoreSnapshot(snapshot);
            NativeRunState restored = RunManager.Instance.State
                ?? throw new InvalidDataException("恢复后跑局状态为空。");
            Player restoredPlayer = LocalContext.GetMe(restored)
                ?? throw new InvalidDataException("恢复后找不到本地玩家。");
            RunState optionStart = RunStateImporter.Import(restored, restoredPlayer);
            _captured = null;
            ArmCapture();
            _loop!.RunUntilCompleted(RunManager.Instance.EnterMapCoord(option.coord),
                TimeSpan.FromSeconds(120), "进点 " + option.coord);
            _armed = false;
            state.Options++;

            RunMapEntryResult entry = RunMapEntry.Enter(in optionStart, _table!,
                Coord(option.coord));
            if (_captured != null)
            {
                CompareNonCombat(state, entry, _captured, floor, pointType);
                if (continueAfter)
                    EnterMapRoom();
            }
            else
            {
                CompareCombat(state, entry, floor, pointType, continueAfter);
            }
        }
        catch (ContentUnavailableException exception)
        {
            state.Rejects++;
            state.Step(imported.ActIndex + 1, floor, pointType,
                _captured?.RoomType ?? RoomTypeOfNative(),
                "reject", exception.Reason);
        }
        catch (UnsupportedImportException exception)
        {
            state.Rejects++;
            state.Step(imported.ActIndex + 1, floor, pointType,
                _captured?.RoomType ?? RoomTypeOfNative(),
                "reject", exception.Reason);
        }
        catch (BlitzSolver.Simulation.Combat.UnsupportedException exception)
        {
            state.Rejects++;
            state.Step(imported.ActIndex + 1, floor, pointType,
                _captured?.RoomType ?? RoomTypeOfNative(),
                "reject", "core-unsupported");
            Console.WriteLine($"  {Short(exception.Message)}");
        }
        catch (CapacityException exception)
        {
            state.Step(imported.ActIndex + 1, floor, pointType,
                _captured?.RoomType ?? RoomTypeOfNative(),
                "error", "capacity");
            state.Diff($"capacity {exception.Region}");
        }
    }

    private static void CompareNonCombat(CaseState state, RunMapEntryResult entry,
        Captured captured, int floor, RunMapPointType pointType)
    {
        int act = entry.Run.ActIndex + 1;
        if (entry.RoomType != captured.RoomType)
        {
            state.Step(act, floor, pointType, captured.RoomType, "diff", "status");
            state.Diff(
                $"step act={act} floor={floor} point={pointType} native={captured.RoomType} blitz={entry.RoomType}");
            return;
        }

        string nativeDump = FilterEventRng(NativeRunDump.Capture(captured.Native, captured.Player));
        string blitzDump = FilterEventRng(RunDump.Capture(entry.Run));
        List<string> differences = RunImportCompare.DiffDumps(nativeDump, blitzDump);
        CheckEventRng(captured, entry.Run, differences);
        if (differences.Count == 0)
        {
            state.Step(act, floor, pointType, captured.RoomType, "same");
            return;
        }
        state.Step(act, floor, pointType, captured.RoomType, "diff");
        foreach (string difference in differences.Take(6))
            state.Diff($"step act={act} floor={floor} point={pointType} {difference}");
    }

    private static void CompareCombat(CaseState state, RunMapEntryResult entry,
        int floor, RunMapPointType pointType, bool continueAfter)
    {
        Task<CombatState> playable = _waiter!.WaitForPlayableCombatAsync();
        _loop!.RunUntilCompleted(playable, TimeSpan.FromSeconds(120), "等待可出牌");
        CombatState combat = playable.GetAwaiter().GetResult();
        Player combatPlayer = LocalContext.GetMe(combat)
            ?? throw new InvalidDataException("战斗找不到玩家。");
        NativeRunState after = combat.RunState as NativeRunState
            ?? throw new InvalidDataException("战斗 RunState 类型不对。");
        int act = after.CurrentActIndex + 1;
        RunRoomType nativeRoom = after.CurrentRoom is { } room
            ? (RunRoomType)(int)room.RoomType : RunRoomType.Unassigned;
        if (entry.Combat == null)
        {
            state.Step(act, floor, pointType, nativeRoom, "diff", "status");
            state.Diff(
                $"combat act={act} floor={floor} point={pointType} native={nativeRoom} blitz={entry.RoomType}");
            if (continueAfter)
            {
                FinishCombat(combat);
                EnterMapRoom();
            }
            return;
        }

        Rules nativeRules = RulesCompiler.Compile(combat, combatPlayer, after);
        Battle expected = new LiveCombatTracker(combat).ImportRoot(combat, combatPlayer,
            nativeRules);
        List<string> battleDiff = BattleDump.Diff(expected, entry.Combat.Battle, "native", "blitz");
        byte[] nativeRuleBytes = RulesCodec.Encode(nativeRules);
        byte[] blitzRuleBytes = RulesCodec.Encode(entry.Combat.Rules);
        string? rulesDiff = nativeRuleBytes.AsSpan().SequenceEqual(blitzRuleBytes)
            ? null
            : string.Join(" | ", RulesDiff.Compare(nativeRules, entry.Combat.Rules).Take(4));
        List<string> runDiff = RunImportCompare.DiffDumps(
            NativeRunDump.Capture(after, combatPlayer), RunDump.Capture(entry.Combat.Run));
        if (battleDiff.Count == 0 && rulesDiff == null && runDiff.Count == 0)
        {
            state.Step(act, floor, pointType, nativeRoom, "same");
        }
        else
        {
            state.Step(act, floor, pointType, nativeRoom, "diff");
            foreach (string line in battleDiff.Take(6))
                state.Diff($"battle act={act} floor={floor} point={pointType} {line}");
            if (rulesDiff != null)
                state.Diff($"rules act={act} floor={floor} point={pointType} {rulesDiff}");
            foreach (string line in runDiff.Take(6))
                state.Diff($"run act={act} floor={floor} point={pointType} {line}");
        }
        if (continueAfter)
        {
            FinishCombat(combat);
            EnterMapRoom();
        }
    }

    private static void FinishCombat(CombatState combat)
    {
        List<NativeCreature> enemies = [.. combat.Enemies];
        foreach (NativeCreature enemy in enemies)
        {
            enemy.RemoveAllPowersInternalExcept();
            Task kill = CreatureCmd.Kill(enemy);
            _loop!.RunUntilCompleted(kill, TimeSpan.FromSeconds(60), "杀敌");
        }
        Task<bool> win = CombatManager.Instance.CheckWinCondition();
        _loop!.RunUntilCompleted(win, TimeSpan.FromSeconds(120), "战斗胜利");
    }

    private static void EnterMapRoom()
    {
        _loop!.RunUntilCompleted(RunManager.Instance.EnterRoom(new MapRoom()),
            TimeSpan.FromSeconds(120), "进入地图房间");
    }

    private static void RestoreSnapshot(SerializableRun snapshot)
    {
        Task<NativeRunState> restore = RunTestHost.RestoreSnapshotAsync(snapshot);
        _loop!.RunUntilCompleted(restore, TimeSpan.FromSeconds(120), "恢复地图选点");
        NativeRunState restored = restore.GetAwaiter().GetResult();
        if (restored.Players.Count != 1)
            throw new InvalidDataException($"恢复后有 {restored.Players.Count} 名玩家。");
    }

    private static RunRoomType RoomTypeOfNative()
        => RunManager.Instance.State?.CurrentRoom is { } room
            ? (RunRoomType)(int)room.RoomType : RunRoomType.Unassigned;

    private static RunCoord Coord(MapCoord coord)
        => new(checked((byte)coord.col), checked((byte)coord.row));

    private static string OptionText(List<MapPoint> options)
        => string.Join(',', options.Select(option =>
            $"{option.coord.col},{option.coord.row}:{option.PointType}"));

    private static string PointText(ReadOnlySpan<RunCoord> points)
        => string.Join(',', points.ToArray().Select(static point => point.ToString()));

    private static string FilterEventRng(string dump)
    {
        string[] lines = dump.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<string> kept = lines.Where(line =>
            !line.StartsWith("rng.events=", StringComparison.Ordinal)
            && !line.StartsWith("rng.event.", StringComparison.Ordinal));
        return string.Join('\n', kept);
    }

    private static void CheckEventRng(Captured captured, in RunState blitz,
        List<string> differences)
    {
        if (captured.RoomType != RunRoomType.Event)
            return;
        if (blitz.Rng.EventCount != 1)
        {
            differences.Add($"rng.events: native=1 blitz={blitz.Rng.EventCount}");
            return;
        }
        RunEventRng eventRng = blitz.Rng.Events[0];
        if (eventRng.EventId != captured.EventId || eventRng.Active == 0)
        {
            differences.Add(
                $"rng.event: native={RunCatalog.EventName(captured.EventId)} blitz={RunCatalog.EventName(eventRng.EventId)} active={eventRng.Active}");
            return;
        }
        MegaCrit.Sts2.Core.Random.Rng expected = new(
            captured.Native.Rng.Seed + 0UL
            + MegaCrit.Sts2.Core.Helpers.StringHelper.GetDeterministicHashCode(
                RunCatalog.EventName(captured.EventId)));
        MegaCrit.Sts2.Core.Saves.SerializableRng saved = expected.ToSerializable();
        RngState actual = eventRng.State;
        if (saved.counter != actual.Counter || saved.state0 != actual.S0
            || saved.state1 != actual.S1 || saved.state2 != actual.S2 || saved.state3 != actual.S3)
        {
            differences.Add(
                $"rng.event.0: native={saved.counter}/{saved.state0:x16}{saved.state1:x16}{saved.state2:x16}{saved.state3:x16} blitz={actual.Counter}/{actual.S0:x16}{actual.S1:x16}{actual.S2:x16}{actual.S3:x16}");
        }
    }

    private static string Short(string text)
    {
        string single = text.Replace('\n', ' ').Replace(' ', '_');
        return single.Length <= 160 ? single : single[..160];
    }

    // ------------------------------------------------------------------ save patch

    private static void InstallSavePatch()
    {
        MethodInfo progress = AccessTools.Method(typeof(SaveManager),
            nameof(SaveManager.SaveProgressFile));
        MethodInfo skip = AccessTools.Method(typeof(MapStepCompare), nameof(SkipPrefix));
        GameBootstrap.Harmony.Patch(progress, prefix: new HarmonyMethod(skip));
        MethodInfo run = AccessTools.Method(typeof(SaveManager), nameof(SaveManager.SaveRun),
            [typeof(AbstractRoom), typeof(bool)]);
        MethodInfo skipTask = AccessTools.Method(typeof(MapStepCompare), nameof(SkipTaskPrefix));
        GameBootstrap.Harmony.Patch(run, prefix: new HarmonyMethod(skipTask));
    }

    private static bool SkipPrefix() => false;

    private static bool SkipTaskPrefix(ref Task __result)
    {
        __result = Task.CompletedTask;
        return false;
    }

    // ------------------------------------------------------------------ capture patch

    private static bool _armed;
    private static Captured? _captured;

    private sealed record Captured(NativeRunState Native, Player Player, RunRoomType RoomType,
        ushort EventId);

    private static void InstallCapturePatch()
    {
        MethodInfo target = AccessTools.Method(typeof(RunManager), "EnterRoomInternal")
            ?? throw new MissingMethodException(typeof(RunManager).FullName, "EnterRoomInternal");
        MethodInfo prefix = AccessTools.Method(typeof(MapStepCompare), nameof(EnterRoomPrefix));
        GameBootstrap.Harmony.Patch(target, prefix: new HarmonyMethod(prefix));
    }

    private static void ArmCapture()
    {
        _armed = true;
        _captured = null;
    }

    private static bool EnterRoomPrefix(AbstractRoom room, ref Task __result)
    {
        if (!_armed)
            return true;
        _armed = false;
        if (room is MapRoom or CombatRoom)
            return true;
        NativeRunState native = RunManager.Instance.State
            ?? throw new InvalidDataException("捕获时跑局状态为空。");
        Player player = LocalContext.GetMe(native)
            ?? throw new InvalidDataException("捕获时找不到本地玩家。");
        ushort eventId = room is EventRoom eventRoom
            ? RunCatalog.TryEvent(eventRoom.ModelId.Entry, out ushort id) ? id : (ushort)0
            : (ushort)0;
        _captured = new Captured(native, player, (RunRoomType)(int)room.RoomType, eventId);
        __result = Task.CompletedTask;
        return false;
    }

    // ------------------------------------------------------------------ tutorial patch

    private static int _tutorialOverride = -1;

    private static void InstallTutorialPatch()
    {
        MethodInfo target = AccessTools.Method(typeof(RunManager),
            nameof(RunManager.ShouldApplyTutorialModifications));
        MethodInfo prefix = AccessTools.Method(typeof(MapStepCompare),
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

    // ------------------------------------------------------------------ unlocks

    private sealed record SavePreset(string Name, string[] Epochs, string[] Encounters, int Runs,
        bool FromDefaultProgress);

    private static SavePreset Preset(string name) => name switch
    {
        "empty" => new SavePreset("empty", [], [], 0, true),
        "all" => new SavePreset("all", AllEpochs(), AllEncounters(), 999999999, false),
        _ => throw new InvalidDataException($"未知存档预设 {name}。"),
    };

    private static string[] AllEpochs() => [.. EpochModel.AllEpochIds];

    private static string[] AllEncounters()
        => [.. Enumerable.Range(0, RunCatalog.EncounterCount)
            .Select(index => RunCatalog.EncounterName((ushort)index))];

    private static UnlockState BuildNativeUnlock(SavePreset preset)
    {
        HashSet<ModelId> encounters = [];
        foreach (string name in preset.Encounters)
        {
            EncounterModel? model = ModelDb.All.OfType<EncounterModel>()
                .FirstOrDefault(candidate => candidate.Id.Entry == name);
            encounters.Add(model is null ? new ModelId("ENCOUNTER", name) : model.Id);
        }
        return new UnlockState(preset.Epochs, encounters, preset.Runs);
    }
}
