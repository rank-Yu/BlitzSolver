// --reward-compare: run a combat to its native end, compare the R2a-3 write-back, then compare
// post-combat reward generation and every legal reward-screen action between the native game and
// the Core reward layer. Native actions use RewardsCmd/RewardsSetSynchronizer/PotionCmd and the
// test card selector; Blitz actions use RunKernel.Step. Every option starts from the same
// SerializableRun snapshot and is compared field by field.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BlitzSolver.Ai.Combat;
using BlitzSolver.Autoplay;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;
using RunState = BlitzSolver.Simulation.Run.RunState;

namespace OfflineHost;

internal static class RewardCompare
{
    private static readonly string[] Characters =
        ["Ironclad", "Silent", "Regent", "Defect", "Necrobinder"];

    private static readonly string[] Presets = ["empty", "all"];
    private const int MaxJobs = 8;

    private static ContentTable? _table;
    private static OfflineCombatWaiter? _waiter;
    private static MainLoopContext? _loop;
    private static bool _skipRoomEntry;

    public static int Run(string[] args)
    {
        string data = "tests/Golden/data";
        string corpora = "tests/corpora";
        string kind = "";
        int seeds = 8;
        int jobs = 6;
        int shard = 0;
        int shards = 1;
        int steps = 400;
        string? root = null;
        string? character = null;
        int ascension = 0;
        string? seed = null;
        string? preset = null;
        int? encounter = null;
        string? d1Seed = null;
        bool pilot = false;
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
                case "--reward-kind" when index + 1 < args.Length:
                    kind = args[++index];
                    break;
                case "--reward-seeds" when index + 1 < args.Length:
                    seeds = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--reward-jobs" when index + 1 < args.Length:
                    jobs = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--reward-shard" when index + 2 < args.Length:
                    shard = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    shards = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--reward-steps" when index + 1 < args.Length:
                    steps = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--reward-root" when index + 1 < args.Length:
                    root = args[++index];
                    break;
                case "--reward-character" when index + 1 < args.Length:
                    character = args[++index];
                    break;
                case "--reward-ascension" when index + 1 < args.Length:
                    ascension = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--reward-seed" when index + 1 < args.Length:
                    seed = args[++index];
                    break;
                case "--reward-preset" when index + 1 < args.Length:
                    preset = args[++index];
                    break;
                case "--reward-encounter" when index + 1 < args.Length:
                    encounter = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--reward-d1-seed" when index + 1 < args.Length:
                    d1Seed = args[++index];
                    break;
                case "--reward-pilot":
                    pilot = true;
                    break;
                default:
                    Console.Error.WriteLine($"未知参数：{args[index]}");
                    return 2;
            }
        }
        if (jobs is < 1 or > MaxJobs || shards < 1 || shard < 0 || shard >= shards)
        {
            Console.Error.WriteLine("--reward-jobs 必须在 1 到 8 之间，分片参数必须合法。");
            return 2;
        }
        if (root != null || character != null)
            return RunChild(data, corpora, root, character, ascension, seed ?? "", preset ?? "all",
                encounter, d1Seed, pilot, kind, steps);
        if (kind.Length == 0)
        {
            Console.Error.WriteLine("需要 --reward-kind natural|d1|all，或单例参数。");
            return 2;
        }
        return RunParent(data, corpora, kind, seeds, jobs, shard, shards, steps);
    }

    private static int RunChild(string data, string corpora, string? root, string? character,
        int ascension, string seed, string preset, int? encounter, string? d1Seed, bool pilot,
        string kind, int steps)
    {
        Initialize();
        if (root != null)
            return CompareD1(data, corpora, root, steps, kind, d1Seed);
        if (character != null && encounter != null)
            return CompareNaturalEncounter(character, ascension, seed, preset, encounter.Value,
                kind == "thief", steps);
        if (character != null && kind is "run" or "run-pilot")
            return RunWalk(character, ascension, seed, preset, 12, steps,
                pilot || kind == "run-pilot");
        if (character != null)
            return CompareNatural(character, ascension, seed, preset, steps);
        Console.Error.WriteLine("子进程缺少单例参数。");
        return 2;
    }

    private static int RunParent(string data, string corpora, string kind, int seeds, int jobs,
        int shard, int shards, int steps)
    {
        List<(string Kind, string[] Args, string Label)> cases = [];
        if (kind is "all" or "natural")
        {
            for (int seedIndex = 0; seedIndex < seeds; seedIndex++)
            {
                foreach (string character in Characters)
                {
                    foreach (int ascension in new[] { 0, 10 })
                    {
                        foreach (string preset in Presets)
                        {
                            string seedText = $"R2C1-{character}-A{ascension}-{preset}-{seedIndex:D2}";
                            cases.Add(("natural",
                                ["--reward-character", character, "--reward-ascension",
                                    ascension.ToString(CultureInfo.InvariantCulture),
                                    "--reward-seed", seedText, "--reward-preset", preset],
                                $"natural/{character}/A{ascension}/{preset}/{seedText}"));
                        }
                    }
                }
            }
        }
        if (kind is "all" or "run" or "run-pilot")
        {
            for (int seedIndex = 0; seedIndex < 2; seedIndex++)
            {
                foreach (string character in Characters)
                {
                    foreach (int ascension in new[] { 0, 10 })
                    {
                        foreach (string preset in Presets)
                        {
                            string seedText = $"R2C1-RUN-{character}-A{ascension}-{preset}-{seedIndex}";
                            string runKind = kind == "run-pilot" ? "run-pilot" : "run";
                            string[] extra = kind == "run-pilot" ? ["--reward-pilot"] : [];
                            cases.Add((runKind,
                                ["--reward-character", character, "--reward-ascension",
                                    ascension.ToString(CultureInfo.InvariantCulture),
                                    "--reward-seed", seedText, "--reward-preset", preset,
                                    .. extra],
                                $"run/{character}/A{ascension}/{preset}/{seedText}"));
                        }
                    }
                }
            }
        }
        if (kind is "all" or "d1")
        {
            foreach (string root in EnumerateRoots(data))
                cases.Add(("d1", ["--reward-root", root], $"d1/{root}"));
        }
        if (kind is "all" or "pilot")
        {
            foreach (string root in EnumerateRoots(data))
                cases.Add(("pilot", ["--reward-root", root], $"pilot/{root}"));
        }
        if (kind is "all" or "pilot-extra")
        {
            foreach (string root in EnumerateRoots(data))
            {
                if (!IsEliteOrBossRoot(corpora, root))
                    continue;
                for (int seedIndex = 1; seedIndex <= 3; seedIndex++)
                {
                    string seedText = $"R2C1-PILOT-EXTRA-{seedIndex}";
                    cases.Add(("pilot",
                        ["--reward-root", root, "--reward-d1-seed", seedText],
                        $"pilot-extra/{root}/{seedIndex}"));
                }
            }
        }
        if (kind is "all" or "thief")
        {
            for (int id = 0; id < RunCatalog.EncounterCount; id++)
            {
                string encounterName = RunCatalog.EncounterName((ushort)id);
                if (!encounterName.StartsWith("THIEVING_HOPPER", StringComparison.Ordinal))
                    continue;
                foreach (int ascension in new[] { 0, 10 })
                {
                    for (int seedIndex = 0; seedIndex < 10; seedIndex++)
                    {
                        string seedText = $"R2C1-THIEF-{ascension}-{id:D3}-{seedIndex}";
                        cases.Add(("thief",
                            ["--reward-character", "Ironclad", "--reward-ascension",
                                ascension.ToString(CultureInfo.InvariantCulture),
                                "--reward-seed", seedText, "--reward-preset", "all",
                                "--reward-encounter", id.ToString(CultureInfo.InvariantCulture)],
                            $"thief/{encounterName}/{seedText}"));
                    }
                }
            }
        }
        if (kind is "all" or "encounter-pilot")
        {
            for (int id = 0; id < RunCatalog.EncounterCount; id++)
            {
                string encounterName = RunCatalog.EncounterName((ushort)id);
                if (!encounterName.EndsWith("_ELITE", StringComparison.Ordinal)
                    && !encounterName.EndsWith("_BOSS", StringComparison.Ordinal))
                    continue;
                foreach (int ascension in new[] { 0, 10 })
                {
                    for (int seedIndex = 0; seedIndex < 6; seedIndex++)
                    {
                        string seedText = $"R2C1-ENC-PILOT-{ascension}-{id:D3}-{seedIndex}";
                        cases.Add(("encounter-pilot",
                            ["--reward-character", "Ironclad", "--reward-ascension",
                                ascension.ToString(CultureInfo.InvariantCulture),
                                "--reward-seed", seedText, "--reward-preset", "all",
                                "--reward-encounter", id.ToString(CultureInfo.InvariantCulture)],
                            $"encounter-pilot/{encounterName}/{seedText}"));
                    }
                }
            }
        }
        if (shards > 1)
            cases = cases.Where((_, index) => index % shards == shard).ToList();
        if (cases.Count == 0)
        {
            Console.Error.WriteLine($"--reward-kind {kind} 没有用例。");
            return 2;
        }
        object gate = new();
        int same = 0, diff = 0, reject = 0, rejectCases = 0, crash = 0;
        long runSteps = 0, runCombats = 0, runRewards = 0;
        int runCount = 0;
        Dictionary<string, int> coverage = new(StringComparer.Ordinal);
        Dictionary<string, int> stops = new(StringComparer.Ordinal);
        int casesWithRewards = 0, casesWithoutRewards = 0;
        long optionComparisons = 0;
        Dictionary<string, int> rejects = new(StringComparer.Ordinal);
        Dictionary<string, (int Same, int Diff, int Reject)> groups = [];
        Dictionary<string, (int Same, int Diff, int Reject)> rewardTypes = [];
        foreach (string reward in new[] { "Gold", "Potion", "Card", "Relic", "SpecialCard",
                     "CardRemoval" })
            rewardTypes[$"reward:{reward}"] = (0, 0, 0);
        Dictionary<string, int> actionCounts = [];
        Dictionary<string, int> displayMismatches = [];
        Console.WriteLine($"REWARD cases={cases.Count} kind={kind} jobs={jobs} shard={shard}/{shards}");
        Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = jobs }, item =>
        {
            (int code, string stdout, string stderr) = RunChildProcess(data, corpora,
                [.. item.Args, "--reward-kind", item.Kind, "--reward-steps",
                    steps.ToString(CultureInfo.InvariantCulture)]);
            lock (gate)
            {
                string? summary = stdout.Split('\n')
                    .FirstOrDefault(line => line.StartsWith("REWARD-CASE ", StringComparison.Ordinal));
                if (summary == null)
                {
                    crash++;
                    Console.WriteLine($"REWARD-CRASH label={item.Label} code={code}");
                    string first = stderr.Split('\n').FirstOrDefault(line => line.Length != 0)
                        ?? "no stderr";
                    Console.WriteLine("  " + first);
                    foreach (string line in stdout.Split('\n'))
                    {
                        if (line.StartsWith("DIFF ", StringComparison.Ordinal)
                            || line.StartsWith("  ", StringComparison.Ordinal))
                            Console.WriteLine(line);
                    }
                    return;
                }
                string status = Field(summary, "status", "?");
                string group = Field(summary, "group", "?");
                int options = int.TryParse(Field(summary, "options", "0"), out int parsed)
                    ? parsed : 0;
                optionComparisons += options + 1;
                if (status == "same")
                    same++;
                else if (status == "reject")
                    rejectCases++;
                else
                {
                    diff++;
                    Console.WriteLine($"REWARD-DIFFCASE {item.Label}");
                }
                if (group == "run")
                {
                    runCount++;
                    runSteps += long.TryParse(Field(summary, "steps", "0"), out long parsedSteps)
                        ? parsedSteps : 0;
                    runCombats += long.TryParse(Field(summary, "combats", "0"), out long parsedCombats)
                        ? parsedCombats : 0;
                    long currentRewards = long.TryParse(Field(summary, "rewards", "0"),
                        out long parsedRewards) ? parsedRewards : 0;
                    runRewards += currentRewards;
                    optionComparisons += currentRewards;
                    string stop = Field(summary, "stop", "?");
                    stops[stop] = stops.GetValueOrDefault(stop) + 1;
                }
                string rewards = Field(summary, "rewards", "0");
                if (rewards == "0")
                    casesWithoutRewards++;
                else
                    casesWithRewards++;
                (int Same, int Diff, int Reject) counts =
                    groups.TryGetValue(group, out var existing) ? existing : (0, 0, 0);
                groups[group] = status switch
                {
                    "same" => (counts.Same + 1, counts.Diff, counts.Reject),
                    "reject" => (counts.Same, counts.Diff, counts.Reject + 1),
                    _ => (counts.Same, counts.Diff + 1, counts.Reject),
                };
                foreach (string line in stdout.Split('\n'))
                {
                    if (line.StartsWith("REWARD-REJECT ", StringComparison.Ordinal))
                    {
                        reject++;
                        string reason = Field(line, "reason", "?");
                        rejects[reason] = rejects.GetValueOrDefault(reason) + 1;
                        Console.WriteLine($"REWARD-REJECTCASE {item.Label} reason={reason}");
                    }
                    else if (line.StartsWith("REWARD-DISPLAY-MISMATCH ", StringComparison.Ordinal))
                    {
                        string relic = Field(line, "relic", "?");
                        displayMismatches[relic] = displayMismatches.GetValueOrDefault(relic) + 1;
                    }
                    else if (line.StartsWith("REWARD-COVER ", StringComparison.Ordinal))
                    {
                        string key = Field(line, "key", "?");
                        coverage[key] = coverage.GetValueOrDefault(key) + 1;
                        if (key.StartsWith("reward:", StringComparison.Ordinal))
                        {
                            (int Same, int Diff, int Reject) typeCounts =
                                rewardTypes.TryGetValue(key, out var typeExisting)
                                    ? typeExisting : (0, 0, 0);
                            rewardTypes[key] = status switch
                            {
                                "same" => (typeCounts.Same + 1, typeCounts.Diff, typeCounts.Reject),
                                "reject" => (typeCounts.Same, typeCounts.Diff,
                                    typeCounts.Reject + 1),
                                _ => (typeCounts.Same, typeCounts.Diff + 1, typeCounts.Reject),
                            };
                        }
                    }
                    else if (line.StartsWith("REWARD-ACTION ", StringComparison.Ordinal))
                    {
                        string action = Field(line, "action", "?");
                        int count = int.TryParse(Field(line, "count", "0"), out int parsedCount)
                            ? parsedCount : 0;
                        actionCounts[action] = actionCounts.GetValueOrDefault(action) + count;
                    }
                    else if (status != "same"
                        && (line.StartsWith("DIFF ", StringComparison.Ordinal)
                            || line.StartsWith("  ", StringComparison.Ordinal)))
                    {
                        Console.WriteLine(line);
                    }
                }
            }
        });
        Console.WriteLine(
            $"REWARD-SUMMARY kind={kind} cases={cases.Count} same={same} diff={diff} " +
            $"rejects={reject} rejectCases={rejectCases} crash={crash} withRewards={casesWithRewards} " +
            $"withoutRewards={casesWithoutRewards} options={optionComparisons}");
        foreach ((string group, (int groupSame, int groupDiff, int groupReject)) in groups
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine(
                $"REWARD-GROUP {group} same={groupSame} diff={groupDiff} reject={groupReject}");
        foreach ((string type, (int typeSame, int typeDiff, int typeReject)) in rewardTypes
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine(
                $"REWARD-TYPE {type} same={typeSame} diff={typeDiff} reject={typeReject}");
        foreach ((string relic, int count) in displayMismatches.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
            Console.WriteLine($"REWARD-DISPLAY-MISMATCH-TOTAL relic={relic} count={count}");
        foreach ((string key, int count) in coverage
                     .Where(pair => pair.Key.StartsWith("relic:", StringComparison.Ordinal))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"REWARD-RELIC {key} count={count}");
        foreach ((string reason, int count) in rejects.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
            Console.WriteLine($"REWARD-REJECT {reason}={count}");
        foreach ((string action, int count) in actionCounts.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
            Console.WriteLine($"REWARD-ACTION {action}={count}");
        foreach ((string key, int count) in coverage.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
            Console.WriteLine($"REWARD-COVER {key}={count}");
        if (runCount != 0)
        {
            Console.WriteLine(
                $"REWARD-RUN runs={runCount} avgSteps={(double)runSteps / runCount:F2} " +
                $"avgCombats={(double)runCombats / runCount:F2} " +
                $"avgRewards={(double)runRewards / runCount:F2}");
            foreach ((string stop, int count) in stops.OrderBy(pair => pair.Key,
                         StringComparer.Ordinal))
                Console.WriteLine($"REWARD-RUN-STOP {stop}={count}");
        }
        return diff == 0 && crash == 0 ? 0 : 1;
    }

    private static string Field(string line, string name, string fallback)
    {
        string marker = name + "=";
        int start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return fallback;
        start += marker.Length;
        int end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }

    private static void Initialize()
    {
        _loop = new MainLoopContext();
        SynchronizationContext.SetSynchronizationContext(_loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();
        _table = ContentTableExporter.Export();
        _waiter = new OfflineCombatWaiter(_loop);
        MethodInfo enterRoom = AccessTools.Method(typeof(RunManager), "EnterRoomInternal")
            ?? throw new MissingMethodException(typeof(RunManager).FullName, "EnterRoomInternal");
        GameBootstrap.Harmony.Patch(enterRoom, prefix: new HarmonyMethod(
            typeof(RewardCompare).GetMethod(nameof(SkipRoomEntryPrefix),
                BindingFlags.Static | BindingFlags.NonPublic)!));
    }

    private static int CompareNatural(string characterName, int ascension, string seed,
        string presetName, int steps)
    {
        CharacterModel character = ModelDb.AllCharacters.FirstOrDefault(
            candidate => candidate.GetType().Name == characterName)
            ?? throw new InvalidDataException($"未知角色 {characterName}。");
        UnlockState nativeUnlock = PresetUnlock(presetName);
        Task<(NativeRunState Run, Player Player)> start = RunTestHost.StartNewRunBeforeChoicesAsync(
            character, ascension, seed, nativeUnlock);
        _loop!.RunUntilCompleted(start, TimeSpan.FromSeconds(120), "新局");
        (NativeRunState native, Player player) = start.GetAwaiter().GetResult();
        if (native.ExtraFields.StartedWithNeow)
        {
            _skipRoomEntry = true;
            _loop.RunUntilCompleted(RunManager.Instance.EnterMapCoord(native.Map.StartingMapPoint.coord),
                TimeSpan.FromSeconds(120), "Neow 起点");
            _skipRoomEntry = false;
            _loop.RunUntilCompleted(RunManager.Instance.EnterRoom(new MapRoom()),
                TimeSpan.FromSeconds(120), "Neow 后地图");
        }
        MapPoint first = native.Map.StartingMapPoint.Children
            .FirstOrDefault(static point => point.PointType == MapPointType.Monster)
            ?? throw new InvalidDataException("第一排没有战斗点。");
        return CompareEntry(native, player, first.coord, "natural", seed, null, steps);
    }

    private static int CompareNaturalEncounter(string characterName, int ascension, string seed,
        string presetName, int encounterId, bool thief, int steps)
    {
        ContentEncounter content = _table!.Encounter((ushort)encounterId);
        if ((!thief && content.RoomType is not (RunRoomType.Elite or RunRoomType.Boss))
            || Registry.Encounter((ushort)encounterId).EndsByEventScript)
        {
            Console.WriteLine(
                $"REWARD-CASE status=reject group=pilot rewards=0 options=0 label={seed}");
            Console.WriteLine("REWARD-REJECT reason=not-elite-boss");
            return 0;
        }
        CharacterModel character = ModelDb.AllCharacters.FirstOrDefault(
            candidate => candidate.GetType().Name == characterName)
            ?? throw new InvalidDataException($"未知角色 {characterName}。");
        UnlockState nativeUnlock = PresetUnlock(presetName);
        Task<(NativeRunState Run, Player Player)> start = RunTestHost.StartNewRunBeforeChoicesAsync(
            character, ascension, seed, nativeUnlock);
        _loop!.RunUntilCompleted(start, TimeSpan.FromSeconds(120), "自然遭遇新局");
        (NativeRunState native, Player player) = start.GetAwaiter().GetResult();
        if (native.ExtraFields.StartedWithNeow)
        {
            _skipRoomEntry = true;
            _loop.RunUntilCompleted(RunManager.Instance.EnterMapCoord(native.Map.StartingMapPoint.coord),
                TimeSpan.FromSeconds(120), "Neow 起点");
            _skipRoomEntry = false;
            _loop.RunUntilCompleted(RunManager.Instance.EnterRoom(new MapRoom()),
                TimeSpan.FromSeconds(120), "Neow 后地图");
        }
        EncounterModel canonical = ModelDb.All.OfType<EncounterModel>()
            .FirstOrDefault(encounter => encounter.Id.Entry == content.NativeId)
            ?? throw new InvalidDataException($"原版没有遭遇 {content.NativeId}。");
        int actIndex = 0;
        for (int index = 0; index < native.Acts.Count; index++)
        {
            if (native.Acts[index].AllEncounters.Any(encounter => encounter.Id == canonical.Id))
            {
                actIndex = index;
                break;
            }
        }
        if (actIndex != 0)
        {
            Task setAct = RunManager.Instance.SetActInternal(actIndex);
            _loop.RunUntilCompleted(setAct, TimeSpan.FromSeconds(120), "自然遭遇设置幕");
        }
        ForceNextEncounter(native, canonical, actIndex);
        MapPoint point = FindPoint(native, canonical.RoomType);
        return CompareEntry(native, player, point.coord, thief ? "thief" : "pilot",
            $"{(thief ? "thief" : "encounter-pilot")}/{content.NativeId}/{seed}",
            content.NativeId, steps);
    }

    private static int CompareD1(string data, string corpora, string root, int steps, string kind,
        string? seedOverride)
    {
        OfflineRequest request = Program.LoadRequest(data, corpora, root);
        if (seedOverride != null)
            request = request with { Seed = seedOverride };
        string label = seedOverride == null ? root : $"{root}#{seedOverride}";
        if (request.Relics.Any(static relic => relic.IntegerMembers.Count != 0
            || relic.BooleanMembers.Count != 0))
        {
            Console.WriteLine(
                $"REWARD-CASE status=reject group={kind} rewards=0 options=0 label={label}");
            Console.WriteLine("REWARD-REJECT reason=unsupported-relic-state");
            return 0;
        }
        GeneratedScenarioSetup generated = GeneratedScenarioSetup.Prepare(request);
        Task<ScenarioBuilder.PreparedRun> prepare = ScenarioBuilder.PrepareRunAsync(
            _waiter!, generated.Request, preparedSetup: generated);
        _loop!.RunUntilCompleted(prepare, TimeSpan.FromSeconds(120), "D1 建局");
        using ScenarioBuilder.PreparedRun prepared = prepare.GetAwaiter().GetResult();
        if (!RunCatalog.TryEncounter(prepared.Encounter.Id.Entry, out _))
            throw new InvalidDataException($"D1 遭遇不在目录：{prepared.Encounter.Id.Entry}");
        EncounterModel target = prepared.Encounter;
        int actIndex = prepared.Run.CurrentActIndex;
        ForceNextEncounter(prepared.Run, target, actIndex);
        MapPoint point = FindPoint(prepared.Run, target.RoomType);
        return CompareEntry(prepared.Run, prepared.Player, point.coord, kind, label,
            target.Id.Entry, steps);
    }

    private static int CompareEntry(NativeRunState native, Player player, MapCoord mapCoord,
        string kind, string label, string? encounterName, int steps)
    {
        bool pilot = kind == "pilot";
        RunCoord coord = new((byte)mapCoord.col, (byte)mapCoord.row);
        try
        {
            RunState before = RunStateImporter.Import(native, player);
            Task enter = RunManager.Instance.EnterMapCoord(mapCoord);
            _loop!.RunUntilCompleted(enter, TimeSpan.FromSeconds(120), "原生进点");
            Task<CombatState> playable = _waiter!.WaitForPlayableCombatAsync();
            _loop.RunUntilCompleted(playable, TimeSpan.FromSeconds(120), "等待可出牌");
            CombatState combat = playable.GetAwaiter().GetResult();
            Player combatPlayer = LocalContext.GetMe(combat)
                ?? throw new InvalidDataException("原生战斗找不到玩家。");
            NativeRunState after = combat.RunState as NativeRunState
                ?? throw new InvalidDataException("原生战斗的 RunState 类型不对。");
            Rules nativeRules = RulesCompiler.Compile(combat, combatPlayer, after);
            LiveCombatTracker tracker = new(combat);
            Battle opening = tracker.ImportRoot(combat, combatPlayer, nativeRules);
            CombatSetupResult setup = RunCombatSetup.EnterMapCoord(before, _table!, coord);
            if (setup.Run.Rooms.Count != 0)
                kind = $"{kind}/{setup.Run.Rooms.Items[setup.Run.Rooms.Count - 1].RoomType}";
            List<string> setupDiff = BattleDump.Diff(opening, setup.Battle, "native", "blitz");
            if (setupDiff.Count != 0)
                return Diff(kind, label, -1, "setup", setupDiff, encounterName);
            if (encounterName == null && setup.Run.History.Count != 0)
            {
                RunHistoryEntry history = setup.Run.History.Entries[setup.Run.History.Count - 1];
                if (history.RoomCount != 0)
                {
                    RunRef model = history.Rooms[history.RoomCount - 1].Model;
                    if (model.Kind == RunRefKind.Encounter)
                        encounterName = RunCatalog.EncounterName(model.Id);
                }
            }
            tracker.Observe(opening);
            Random random = new(unchecked((int)SeedFor(label)));
            CombatActionCoverage coverage = new();
            Battle current = setup.Battle;
            int actionIndex = 0;
            if (pilot)
            {
                SearchPolicy pilotPolicy = SearchPolicyCapture.Capture(combat, combatPlayer);
                NativePilotHost host = new(_loop, combat, combatPlayer, nativeRules, tracker,
                    opening, pilotPolicy, SearchPerformanceProfile.For(SearchPerformanceTier.Low),
                    0);
                CombatPilotResult result = CombatPilot.RunAsync(host, label, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (result.DeploymentFailures != 0 || result.Deviations != 0
                    || result.Status is not ("victory" or "defeat" or "ended"))
                {
                    if (result.Deviations != 0)
                    {
                        CombatPilotDeviationEntry deviation = result.DeviationLog[0];
                        return Diff(kind, label, actionIndex, "pilot-boundary",
                            [$"{deviation.Action}: {string.Join(" | ", deviation.Fields)}"],
                            encounterName);
                    }
                    if (result.FailureLog.Any(static failure =>
                            failure.Reason is "root_deviation" or "boundary_diff"))
                    {
                        CombatPilotFailureEntry failure = result.FailureLog.First(static failure =>
                            failure.Reason is "root_deviation" or "boundary_diff");
                        return Diff(kind, label, actionIndex, "pilot-" + failure.Reason,
                            [$"{failure.Reason}: {failure.Detail}"], encounterName);
                    }
                    string reason = result.Status == "failed" && result.Outcome == "ended"
                        ? "pilot-event-end"
                        : result.FailureLog.Length != 0
                            ? "pilot-deploy-" + result.FailureLog[0].Reason
                            : "pilot-" + result.Status;
                    string detail = result.FailureLog.Length != 0
                        ? result.FailureLog[0].Detail
                        : $"status={result.Status} outcome={result.Outcome}";
                    return Reject(kind, label, reason, detail, encounterName, "pilot");
                }
                current = host.Current;
                actionIndex = result.Turns;
            }
            else
            {
                NativeActionExecutor executor = new(_loop, combat, combatPlayer, nativeRules,
                    tracker);
                for (; actionIndex < steps && current.Outcome == Outcome.None; actionIndex++)
                {
                ReplayedAction? planned = label.Contains("thieving-hopper", StringComparison.OrdinalIgnoreCase)
                    && current.PlayerTurn <= 2
                        ? RandomCombatPolicy.ChooseEndTurn(current, setup.Rules, coverage)
                        : RandomCombatPolicy.Choose(current, setup.Rules, random, coverage);
                if (planned == null)
                    break;
                coverage.CountAction(planned.Action.Kind);
                Battle live;
                try
                {
                    live = executor.Execute(planned);
                }
                catch (OfflineDeploymentException error)
                {
                    if (IsMismatchDeploymentReason(error.Reason))
                        return Diff(kind, label, actionIndex, "combat-executor",
                            [error.Reason + ": " + error.Message], encounterName);
                    return Reject(kind, label, "combat-executor-" + error.Reason, error.Message,
                        encounterName, "combat-executor");
                }
                if (!BattleReconciler.Same(planned.After, live))
                {
                    return Diff(kind, label, actionIndex, planned.Action.ToString(),
                        [.. BattleReconciler.Diff(planned.After, live)], encounterName);
                }
                    tracker.Observe(live);
                    current = planned.After;
                }
            }
            if (current.Outcome == Outcome.None)
            {
                Console.WriteLine(
                    $"REWARD-CASE status=reject group={kind} rewards=0 options=0 label={label}");
                Console.WriteLine("REWARD-REJECT reason=action-limit");
                return 0;
            }
            if (current.Outcome != Outcome.Victory || current.Player.IsDead)
            {
                Console.WriteLine(
                    $"REWARD-CASE status=reject group={kind} rewards=0 options=0 label={label}");
                Console.WriteLine("REWARD-REJECT reason=combat-defeat");
                return 0;
            }
            CombatState? liveState = CombatManager.Instance.DebugOnlyGetState()
                ?? throw new InvalidDataException("战斗结束找不到原生状态。");
            Player finalPlayer = LocalContext.GetMe(liveState)
                ?? throw new InvalidDataException("战斗结束找不到玩家。");
            NativeRunState finalRun = liveState.RunState as NativeRunState
                ?? throw new InvalidDataException("战斗结束的 RunState 类型不对。");
            RunState written = RunCombatWriteback.Apply(setup.Run, current, _table!);
            string beforeRewards = RunDump.Capture(written);
            string nativeBefore = NativeRunDump.Capture(finalRun, finalPlayer);
            string? preDiff = FirstDifference(nativeBefore, beforeRewards);
            if (preDiff != null)
                return Diff(kind, label, 0, "writeback", [preDiff], encounterName);

            RestoredRelicState[] relicState = CaptureRelicState(finalPlayer);
            SerializableRun snapshot = RunManager.Instance.ToSave(finalRun.CurrentRoom);
            AbstractRoom rewardRoom = finalRun.CurrentRoom
                ?? throw new InvalidDataException("战斗结束没有当前房间。");
            Task<NativeRewardSession> create = NativeRewardSession.CreateAsync(finalPlayer,
                rewardRoom);
            _loop.RunUntilCompleted(create, TimeSpan.FromSeconds(120), "原生奖励生成");
            NativeRewardSession session = create.GetAwaiter().GetResult();
            RunRewardGenerator.GenerateForCombat(ref written, _table!);
            for (int relic = 0; relic < written.Relics.Count; relic++)
                Console.WriteLine($"REWARD-COVER key=relic:{Registry.Relic(written.Relics.Items[relic].Id).NativeId}");
            for (int rewardIndex = 0; rewardIndex < written.Rewards.Count; rewardIndex++)
                Console.WriteLine($"REWARD-COVER key=reward:{written.Rewards.Items[rewardIndex].Type}");
            string nativeGenerated = NativeRunDump.Capture(finalRun, finalPlayer, session.Set,
                session.RemainingRewards);
            string blitzGenerated = RunDump.Capture(written);
            string? generatedDiff = FirstDifference(nativeGenerated, blitzGenerated);
            if (generatedDiff != null)
                return Diff(kind, label, actionIndex, "reward-generation", [generatedDiff],
                    encounterName);

            _relicState = relicState;
            int options = CompareOptions(kind, label, snapshot, written, session, encounterName);
            if (options < 0)
                return Diff(kind, label, 0, "options", ["see option DIFF above"], encounterName);
            return sameResult(kind, label, written, options);
        }
        catch (ContentUnavailableException error)
        {
            if (IsMismatchStepReason(error.Reason))
                return Diff(kind, label, 0, "content-mismatch", [error.Reason + ": " + error.Detail],
                    encounterName);
            return Reject(kind, label, error.Reason, error.Detail, encounterName, "content");
        }
        catch (UnsupportedImportException error)
        {
            return Reject(kind, label, error.Reason, error.Detail, encounterName, "import");
        }
        catch (BlitzSolver.Simulation.Combat.UnsupportedException error)
        {
            return Reject(kind, label, "core-unsupported", error.Message, encounterName, "core");
        }
        catch (CapacityException error)
        {
            return Reject(kind, label, "capacity", error.Message, encounterName, "capacity");
        }
    }

    private static int sameResult(string kind, string label, in RunState written, int options)
    {
        Console.WriteLine(
            $"REWARD-CASE status=same group={kind} rewards={(written.Rewards.Count == 0 ? 0 : 1)} " +
            $"options={options} label={label}");
        return 0;
    }

    private static RestoredRelicState[] _relicState = [];

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
        | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
    private static int CompareOptions(string kind, string label, SerializableRun snapshot,
        RunState written, NativeRewardSession session, string? encounterName)
    {
        List<NativeRewardChoice> nativeChoices = [.. session.Choices()];
        RunDecisionRequest request = RunDecisionRequest.Capture(in written);
        if (!SameRequest(nativeChoices, in request, out string requestDiff))
        {
            Console.WriteLine(
                $"DIFF {kind} {label} options native={Describe(nativeChoices)} blitz={Describe(in request)}");
            return -1;
        }
        int options = 0;
        for (int index = 0; index < nativeChoices.Count; index++)
        {
            NativeRewardChoice choice = nativeChoices[index];
            Console.WriteLine($"REWARD-ACTION action={ActionName(choice.Kind)} count=1");
            RestoredRun restoredRun = RestoreAndGenerate(snapshot, written.ActFloor, _relicState);
            NativeRewardSession restored = restoredRun.Session;
            ApplyNative(restored, choice.Kind, choice.Index, []);
            RunState blitz = written;
            RunStepResult step = StepBlitz(ref blitz, choice, request, index);
            if (step.Status == RunStepStatus.Rejected)
            {
                if (IsMismatchStepReason(step.Reason))
                {
                    Console.WriteLine($"DIFF {kind} {label} option={index} rejected={step.Reason}");
                    return -1;
                }
                Console.WriteLine($"REWARD-REJECT reason={step.Reason}");
                options++;
                continue;
            }
            (RunRewardStage nativeStage, int nativeChoiceIndex, int nativeChoiceCount) =
                NativeStage(restored);
            string nativeDump = NativeRunDump.Capture(restoredRun.Run, restoredRun.Player,
                restored.Set, restored.RemainingRewards, nativeStage, nativeChoiceIndex,
                nativeChoiceCount, restoredRun.DisplayOverrides);
            string? diff = FirstDifference(nativeDump, RunDump.Capture(blitz));
            if (restoredRun.UncoveredDisplayMismatches.Count != 0)
            {
                Console.WriteLine($"DIFF {kind} {label} option={index} display-mismatch " +
                    string.Join("; ", restoredRun.UncoveredDisplayMismatches) +
                    $" raw={diff ?? "equal"}");
                return -1;
            }
            if (diff != null)
            {
                Console.WriteLine($"DIFF {kind} {label} option={index} {diff}");
                return -1;
            }
            options++;
            if (nativeStage is RunRewardStage.Card or RunRewardStage.PotionDiscard)
            {
                int sub = CompareSubOptions(kind, label, snapshot, written, blitz,
                    [(choice.Kind, choice.Index)], restored, encounterName);
                options += sub;
                if (sub < 0)
                    return -1;
            }
        }
        int sequence = RandomSequence(kind, label, snapshot, written, _relicState, encounterName);
        if (sequence < 0)
            return -1;
        return options + sequence;
    }

    private static int CompareSubOptions(string kind, string label, SerializableRun snapshot,
        RunState written, RunState blitzPrefix,
        IReadOnlyList<(NativeRewardChoiceKind Kind, int Index)> prefix,
        NativeRewardSession prefixSession, string? encounterName)
    {
        List<NativeRewardChoice> subChoices = [.. prefixSession.Choices()];
        RunDecisionRequest request = RunDecisionRequest.Capture(in blitzPrefix);
        if (!SameRequest(subChoices, in request, out string requestDiff))
        {
            Console.WriteLine(
                $"DIFF {kind} {label} sub-options native={Describe(subChoices)} " +
                $"blitz={Describe(in request)}");
            return -1;
        }
        int options = 0;
        for (int index = 0; index < subChoices.Count; index++)
        {
            NativeRewardChoice choice = subChoices[index];
            Console.WriteLine($"REWARD-ACTION action={ActionName(choice.Kind)} count=1");
            RestoredRun restoredRun = RestoreAndGenerate(snapshot, written.ActFloor, _relicState);
            NativeRewardSession restored = restoredRun.Session;
            ApplyNative(restored, choice.Kind, choice.Index, prefix);
            RunState blitz = blitzPrefix;
            RunStepResult step = StepBlitz(ref blitz, choice, request, index);
            if (step.Status == RunStepStatus.Rejected)
            {
                if (IsMismatchStepReason(step.Reason))
                {
                    Console.WriteLine($"DIFF {kind} {label} option={index} rejected={step.Reason}");
                    return -1;
                }
                Console.WriteLine($"REWARD-REJECT reason={step.Reason}");
                options++;
                continue;
            }
            (RunRewardStage nativeStage, int nativeChoiceIndex, int nativeChoiceCount) =
                NativeStage(restored);
            string nativeDump = NativeRunDump.Capture(restoredRun.Run, restoredRun.Player,
                restored.Set, restored.RemainingRewards, nativeStage, nativeChoiceIndex,
                nativeChoiceCount, restoredRun.DisplayOverrides);
            string? diff = FirstDifference(nativeDump, RunDump.Capture(blitz));
            if (restoredRun.UncoveredDisplayMismatches.Count != 0)
            {
                Console.WriteLine($"DIFF {kind} {label} sub={index} display-mismatch " +
                    string.Join("; ", restoredRun.UncoveredDisplayMismatches) +
                    $" raw={diff ?? "equal"}");
                return -1;
            }
            if (diff != null)
            {
                Console.WriteLine($"DIFF {kind} {label} sub={index} {diff}");
                return -1;
            }
            options++;
        }
        _ = encounterName;
        return options;
    }

    private static int RandomSequence(string kind, string label, SerializableRun snapshot,
        RunState written, RestoredRelicState[] relicState, string? encounterName)
    {
        RestoredRun restoredRun = RestoreAndGenerate(snapshot, written.ActFloor, relicState);
        NativeRewardSession session = restoredRun.Session;
        NativeRunState run = restoredRun.Run;
        RunState blitz = written;
        Random random = new(unchecked((int)SeedFor(label + "/sequence")));
        int actions = 0;
        while (actions < 32)
        {
            List<NativeRewardChoice> choices = [.. session.Choices()];
            if (choices.Count == 0)
                break;
            RunDecisionRequest request = RunDecisionRequest.Capture(in blitz);
            if (!SameRequest(choices, in request, out _))
            {
                Console.WriteLine($"DIFF {kind} {label} sequence-request native={Describe(choices)} " +
                    $"blitz={Describe(in request)}");
                return -1;
            }
            int pick = random.Next(choices.Count);
            NativeRewardChoice choice = choices[pick];
            ApplyNative(session, choice.Kind, choice.Index, []);
            RunStepResult step = StepBlitz(ref blitz, choice, request, pick);
            if (step.Status == RunStepStatus.Rejected)
            {
                if (IsMismatchStepReason(step.Reason))
                {
                    Console.WriteLine($"DIFF {kind} {label} sequence rejected={step.Reason}");
                    return -1;
                }
                Console.WriteLine($"REWARD-REJECT reason={step.Reason}");
                return actions;
            }
            actions++;
            Console.WriteLine($"REWARD-ACTION action={ActionName(choice.Kind)} count=1");
            (RunRewardStage nativeStage, int nativeChoiceIndex, int nativeChoiceCount) =
                NativeStage(session);
            string nativeDump = NativeRunDump.Capture(run, restoredRun.Player, session.Set,
                session.RemainingRewards, nativeStage, nativeChoiceIndex, nativeChoiceCount,
                restoredRun.DisplayOverrides);
            string? diff = FirstDifference(nativeDump, RunDump.Capture(blitz));
            if (restoredRun.UncoveredDisplayMismatches.Count != 0)
            {
                Console.WriteLine($"DIFF {kind} {label} sequence step={actions} display-mismatch " +
                    string.Join("; ", restoredRun.UncoveredDisplayMismatches) +
                    $" raw={diff ?? "equal"}");
                return -1;
            }
            if (diff != null)
            {
                Console.WriteLine($"DIFF {kind} {label} sequence step={actions} {diff}");
                return -1;
            }
            if (session.Left)
                break;
        }
        if (session.Left)
        {
            RunDecisionRequest final = RunDecisionRequest.Capture(in blitz);
            List<MapPoint> points = [.. RunTestHost.GetTravelablePoints(run)
                .OrderBy(point => point.coord.col).ThenBy(point => point.coord.row)];
            if (points.Count == 0)
            {
                if (final.Kind is not (RunDecision.None or RunDecision.MapPoint)
                    || final.OptionCount != 0)
                {
                    Console.WriteLine($"DIFF {kind} {label} map-after-rewards {final.Kind}/{final.OptionCount}");
                    return -1;
                }
            }
            else if (final.Kind != RunDecision.MapPoint || final.OptionCount != points.Count
                || !SameMapPoints(in final, points))
            {
                Console.WriteLine($"DIFF {kind} {label} map-after-rewards native={points.Count} " +
                    $"blitz={final.Kind}/{final.OptionCount}");
                return -1;
            }
        }
        _ = encounterName;
        return actions;
    }

    private static bool IsMismatchDeploymentReason(string reason)
        => reason is "card_mismatch" or "choice_mismatch" or "energy_drift" or "hand_drift"
            or "potion_mismatch" or "potion_slot_drift" or "stars_drift" or "turn_drift"
            or "target_missing" or "card_missing" or "card_unplayable" or "action_kind"
            or "combat_changed" or "combat_state_changed";

    private static bool IsMismatchStepReason(string? reason)
        => reason is RunRewardReason.NotRewardDecision or RunRewardReason.RewardIndex
            or RunRewardReason.PotionSlot or RunRewardReason.CardOption
            or RunRewardReason.RewardState or RunRewardReason.RewardNotGenerated
            or RunRewardReason.RewardOpen;

    private static bool SameMapPoints(in RunDecisionRequest request, List<MapPoint> points)
    {
        for (int index = 0; index < points.Count; index++)
        {
            if (!request.Options[index].Coord.Equals(Coord(points[index].coord)))
                return false;
        }
        return true;
    }

    private static int RunWalk(string characterName, int ascension, string seed, string presetName,
        int maxSteps, int combatSteps, bool pilot)
    {
        CharacterModel character = ModelDb.AllCharacters.FirstOrDefault(
            candidate => candidate.GetType().Name == characterName)
            ?? throw new InvalidDataException($"未知角色 {characterName}。");
        UnlockState unlock = PresetUnlock(presetName);
        Task<(NativeRunState Run, Player Player)> start = RunTestHost.StartNewRunBeforeChoicesAsync(
            character, ascension, seed, unlock);
        _loop!.RunUntilCompleted(start, TimeSpan.FromSeconds(120), "新局");
        (NativeRunState native, Player player) = start.GetAwaiter().GetResult();
        if (native.ExtraFields.StartedWithNeow)
        {
            _skipRoomEntry = true;
            _loop.RunUntilCompleted(RunManager.Instance.EnterMapCoord(native.Map.StartingMapPoint.coord),
                TimeSpan.FromSeconds(120), "Neow 起点");
            _skipRoomEntry = false;
            _loop.RunUntilCompleted(RunManager.Instance.EnterRoom(new MapRoom()),
                TimeSpan.FromSeconds(120), "Neow 后地图");
        }
        RunState blitz = RunStateImporter.Import(native, player);
        RunKernel kernel = new(_table!);
        Random random = new(unchecked((int)SeedFor(seed + "/walk")));
        int mapSteps = 0;
        int combats = 0;
        int rewardActions = 0;
        string stop = "steps";
        while (mapSteps < maxSteps)
        {
            if (RunManager.Instance.State is not { } liveRun || liveRun.IsGameOver)
            {
                stop = "game-over";
                break;
            }
            List<MapPoint> points = [.. RunTestHost.GetTravelablePoints(liveRun)];
            if (points.Count == 0)
            {
                stop = "act-transition";
                break;
            }
            RunDecisionRequest request = RunDecisionRequest.Capture(in blitz);
            if (request.Kind != RunDecision.MapPoint || request.OptionCount != points.Count)
            {
                Console.WriteLine($"DIFF run {seed} map-request native={points.Count} blitz={request.Kind}/{request.OptionCount}");
                return RunDiff(seed);
            }
            List<MapPoint> sorted = [.. points.OrderBy(point => point.coord.col)
                .ThenBy(point => point.coord.row)];
            for (int index = 0; index < sorted.Count; index++)
            {
                if (!request.Options[index].Coord.Equals(Coord(sorted[index].coord)))
                {
                    Console.WriteLine($"DIFF run {seed} map-option {index}");
                    return RunDiff(seed);
                }
            }
            MapPoint chosen = sorted[random.Next(sorted.Count)];
            RunState probe = blitz;
            RunStepResult predicted = kernel.Step(ref probe,
                RunAction.SelectMapPoint(Coord(chosen.coord)));
            if (predicted.Status == RunStepStatus.UnsupportedRoom)
            {
                stop = $"room:{predicted.RoomType}";
                blitz = probe;
                break;
            }
            if (predicted.Status == RunStepStatus.Rejected)
            {
                stop = $"reject:{predicted.Reason}";
                break;
            }
            if (predicted.Status == RunStepStatus.Decision
                && predicted.Request.Kind == RunDecision.Shop)
            {
                _loop.RunUntilCompleted(RunManager.Instance.EnterMapCoord(chosen.coord),
                    TimeSpan.FromSeconds(120), "进商店");
                mapSteps++;
                if (!LeaveNativeShop(ref probe, kernel, seed))
                    return RunDiff(seed);
                blitz = probe;
                continue;
            }
            if (predicted.Status != RunStepStatus.Combat || predicted.Combat is null)
            {
                Console.WriteLine($"DIFF run {seed} step-status {predicted.Status}/{predicted.Reason}");
                return RunDiff(seed);
            }
            _loop.RunUntilCompleted(RunManager.Instance.EnterMapCoord(chosen.coord),
                TimeSpan.FromSeconds(120), "进点");
            mapSteps++;
            Task<CombatState> playable = _waiter!.WaitForPlayableCombatAsync();
            _loop.RunUntilCompleted(playable, TimeSpan.FromSeconds(120), "等待可出牌");
            CombatState combat = playable.GetAwaiter().GetResult();
            Player combatPlayer = LocalContext.GetMe(combat)
                ?? throw new InvalidDataException("战斗找不到玩家。");
            NativeRunState after = combat.RunState as NativeRunState
                ?? throw new InvalidDataException("战斗 RunState 类型不对。");
            Rules nativeRules = RulesCompiler.Compile(combat, combatPlayer, after);
            LiveCombatTracker tracker = new(combat);
            Battle opening = tracker.ImportRoot(combat, combatPlayer, nativeRules);
            List<string> setupDiff = BattleDump.Diff(opening, predicted.Combat.Battle,
                "native", "blitz");
            if (setupDiff.Count != 0)
            {
                Console.WriteLine($"DIFF run {seed} setup {setupDiff[0]}");
                return RunDiff(seed);
            }
            tracker.Observe(opening);
            CombatActionCoverage coverage = new();
            Battle current = predicted.Combat.Battle;
            int actions = 0;
            if (pilot)
            {
                SearchPolicy pilotPolicy = SearchPolicyCapture.Capture(combat, combatPlayer);
                NativePilotHost host = new(_loop, combat, combatPlayer, nativeRules, tracker,
                    opening, pilotPolicy, SearchPerformanceProfile.For(SearchPerformanceTier.Low),
                    0);
                CombatPilotResult result = CombatPilot.RunAsync(host, seed, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (result.DeploymentFailures != 0 || result.Deviations != 0
                    || result.Status is not ("victory" or "defeat" or "ended"))
                {
                    stop = "pilot-" + (result.PauseReason ?? result.Status);
                    break;
                }
                current = host.Current;
                actions = result.Turns;
            }
            else
            {
                NativeActionExecutor executor = new(_loop, combat, combatPlayer, nativeRules,
                    tracker);
                for (; actions < combatSteps && current.Outcome == Outcome.None; actions++)
                {
                ReplayedAction? planned = seed.Contains("thieving-hopper", StringComparison.OrdinalIgnoreCase)
                    && current.PlayerTurn <= 2
                        ? RandomCombatPolicy.ChooseEndTurn(current, predicted.Combat.Rules, coverage)
                        : RandomCombatPolicy.Choose(current, predicted.Combat.Rules, random, coverage);
                if (planned == null)
                    break;
                coverage.CountAction(planned.Action.Kind);
                Battle live;
                try
                {
                    live = executor.Execute(planned);
                }
                catch (OfflineDeploymentException error)
                {
                    stop = $"combat-executor:{error.Reason}";
                    break;
                }
                if (!BattleReconciler.Same(planned.After, live))
                {
                    Console.WriteLine($"DIFF run {seed} combat {planned.Action}");
                    return RunDiff(seed);
                }
                    tracker.Observe(live);
                    current = planned.After;
                }
            }
            if (current.Outcome == Outcome.None)
            {
                stop = "combat-action-limit";
                break;
            }
            if (current.Outcome != Outcome.Victory || current.Player.IsDead)
            {
                stop = "combat-defeat";
                break;
            }
            CombatState liveState = CombatManager.Instance.DebugOnlyGetState()
                ?? throw new InvalidDataException("战斗结束找不到原生状态。");
            Player finalPlayer = LocalContext.GetMe(liveState)
                ?? throw new InvalidDataException("战斗结束找不到玩家。");
            NativeRunState finalRun = liveState.RunState as NativeRunState
                ?? throw new InvalidDataException("战斗结束 RunState 类型不对。");
            RunState written = RunCombatWriteback.Apply(predicted.Combat.Run, current, _table!);
            string nativeBefore = NativeRunDump.Capture(finalRun, finalPlayer);
            string? preDiff = FirstDifference(nativeBefore, RunDump.Capture(written));
            if (preDiff != null)
            {
                Console.WriteLine($"DIFF run {seed} writeback {preDiff}");
                Console.WriteLine(
                    $"REWARD-CASE status=diff group=run rewards=0 options=0 label={seed}");
                return 0;
            }
            SerializableRun snapshot = RunManager.Instance.ToSave(finalRun.CurrentRoom);
            RestoredRelicState[] relicState = [];
            AbstractRoom rewardRoom = finalRun.CurrentRoom
                ?? throw new InvalidDataException("战斗结束没有当前房间。");
            Task<NativeRewardSession> create = NativeRewardSession.CreateAsync(finalPlayer,
                rewardRoom);
            _loop.RunUntilCompleted(create, TimeSpan.FromSeconds(120), "原生奖励生成");
            NativeRewardSession session = create.GetAwaiter().GetResult();
            RunRewardGenerator.GenerateForCombat(ref written, _table!);
            string nativeGenerated = NativeRunDump.Capture(finalRun, finalPlayer, session.Set,
                session.RemainingRewards);
            string? generatedDiff = FirstDifference(nativeGenerated, RunDump.Capture(written));
            if (generatedDiff != null)
            {
                Console.WriteLine($"DIFF run {seed} reward-generation {generatedDiff}");
                return RunDiff(seed);
            }
            int rewardCount = DriveRewards(ref written, session, seed, random);
            if (rewardCount < 0)
                return RunDiff(seed);
            rewardActions += rewardCount;
            blitz = written;
            combats++;
        }
        Console.WriteLine(
            $"REWARD-CASE status=same group=run steps={mapSteps} combats={combats} " +
            $"rewards={rewardActions} stop={stop} label={seed}");
        return 0;
    }

    private static bool LeaveNativeShop(ref RunState blitz, RunKernel kernel, string seed)
    {
        NativeRunState native = RunManager.Instance.State
            ?? throw new InvalidDataException("进商店后没有跑局状态。");
        Player player = LocalContext.GetMe(native)
            ?? throw new InvalidDataException("进商店后没有玩家。");
        NativeShopSession session = NativeShopSession.TryCreate(native, player)
            ?? throw new InvalidDataException("原生没有进入商人房间。");
        string? entryDiff = FirstDifference(NativeRunDump.Capture(native, player),
            RunDump.Capture(blitz));
        if (entryDiff != null)
        {
            Console.WriteLine($"DIFF run {seed} shop-entry {entryDiff}");
            return false;
        }
        RunDecisionRequest request = RunDecisionRequest.Capture(in blitz);
        if (!SameShopChoices(session.Choices(), in request))
        {
            Console.WriteLine($"DIFF run {seed} shop-request");
            return false;
        }
        session.Leave();
        RunStepResult step = kernel.Step(ref blitz, new RunAction
        {
            Kind = RunActionKind.LeaveShop,
        });
        if (step.Status == RunStepStatus.Rejected)
        {
            Console.WriteLine($"DIFF run {seed} shop-leave {step.Reason}");
            return false;
        }
        native = RunManager.Instance.State
            ?? throw new InvalidDataException("离开商店后没有跑局状态。");
        player = LocalContext.GetMe(native)
            ?? throw new InvalidDataException("离开商店后没有玩家。");
        string? leaveDiff = FirstDifference(NativeRunDump.Capture(native, player),
            RunDump.Capture(blitz));
        if (leaveDiff != null)
        {
            Console.WriteLine($"DIFF run {seed} shop-left {leaveDiff}");
            return false;
        }
        List<MapPoint> points = [.. RunTestHost.GetTravelablePoints(native)
            .OrderBy(point => point.coord.col).ThenBy(point => point.coord.row)];
        request = RunDecisionRequest.Capture(in blitz);
        if (request.Kind != RunDecision.MapPoint || request.OptionCount != points.Count)
        {
            Console.WriteLine($"DIFF run {seed} shop-map");
            return false;
        }
        for (int index = 0; index < points.Count; index++)
        {
            if (!request.Options[index].Coord.Equals(Coord(points[index].coord)))
            {
                Console.WriteLine($"DIFF run {seed} shop-map-option");
                return false;
            }
        }
        return true;
    }

    private static bool SameShopChoices(IReadOnlyList<NativeShopChoice> choices,
        in RunDecisionRequest request)
    {
        if (request.Kind != RunDecision.Shop || request.OptionCount != choices.Count)
            return false;
        for (int index = 0; index < choices.Count; index++)
        {
            NativeShopChoice choice = choices[index];
            RunDecisionOption option = request.Options[index];
            if (choice.Kind == NativeShopChoiceKind.Leave)
            {
                if (option.Kind != RunDecisionOptionKind.Skip)
                    return false;
                continue;
            }
            RunDecisionOptionKind expected = choice.Kind == NativeShopChoiceKind.CardRemoval
                ? RunDecisionOptionKind.CardRemoval : RunDecisionOptionKind.ShopEntry;
            if (option.Kind != expected || option.Index != choice.Index
                || option.Amount != choice.Price)
            {
                return false;
            }
            if (choice.Kind == NativeShopChoiceKind.Card
                && (option.Model.Kind != RunRefKind.Card
                    || Registry.Card(option.Model.Id).NativeId != choice.Card!.Id.Entry
                    || option.Upgrade != choice.Card.CurrentUpgradeLevel))
            {
                return false;
            }
            if (choice.Kind == NativeShopChoiceKind.Relic
                && (option.Model.Kind != RunRefKind.Relic
                    || Registry.Relic(option.Model.Id).NativeId != choice.Relic!.Id.Entry))
            {
                return false;
            }
            if (choice.Kind == NativeShopChoiceKind.Potion
                && (option.Model.Kind != RunRefKind.Potion
                    || Registry.Potion(option.Model.Id).NativeId != choice.Potion!.Id.Entry))
            {
                return false;
            }
        }
        return true;
    }

    private static int RunDiff(string seed)
    {
        Console.WriteLine($"REWARD-CASE status=diff group=run rewards=0 options=0 label={seed}");
        return 0;
    }

    private static int DriveRewards(ref RunState blitz, NativeRewardSession session, string seed,
        Random random)
    {
        int actions = 0;
        while (actions < 32)
        {
            List<NativeRewardChoice> choices = [.. session.Choices()];
            if (choices.Count == 0)
                break;
            RunDecisionRequest request = RunDecisionRequest.Capture(in blitz);
            if (!SameRequest(choices, in request, out _))
            {
                Console.WriteLine($"DIFF run {seed} reward-request step={actions}");
                return -1;
            }
            int pick = random.Next(choices.Count);
            NativeRewardChoice choice = choices[pick];
            ApplyNative(session, choice.Kind, choice.Index, []);
            RunStepResult step = StepBlitz(ref blitz, choice, request, pick);
            if (step.Status == RunStepStatus.Rejected)
            {
                Console.WriteLine($"DIFF run {seed} reward-reject {step.Reason}");
                return -1;
            }
            actions++;
            NativeRunState run = RunManager.Instance.State
                ?? throw new InvalidDataException("奖励动作后没有跑局状态。");
            Player player = LocalContext.GetMe(run)
                ?? throw new InvalidDataException("奖励动作后没有玩家。");
            (RunRewardStage stage, int choiceIndex, int choiceCount) = NativeStage(session);
            string nativeDump = NativeRunDump.Capture(run, player, session.Set,
                session.RemainingRewards, stage, choiceIndex, choiceCount);
            string? diff = FirstDifference(nativeDump, RunDump.Capture(blitz));
            if (diff != null)
            {
                Console.WriteLine($"DIFF run {seed} reward step={actions} {diff}");
                return -1;
            }
            if (session.Left)
                break;
        }
        return actions;
    }

    private static (RunRewardStage Stage, int ChoiceIndex, int ChoiceCount) NativeStage(
        NativeRewardSession session)
    {
        if (session.Left)
            return (RunRewardStage.None, 0, 0);
        switch (session.Stage)
        {
            case NativeRewardStage.Card:
            {
                Reward? open = session.OpenCardReward;
                IReadOnlyList<Reward> remaining = session.RemainingRewards;
                int index = -1;
                for (int position = 0; position < remaining.Count; position++)
                {
                    if (ReferenceEquals(remaining[position], open))
                    {
                        index = position;
                        break;
                    }
                }
                int count = open is CardReward card ? card.Cards.Count() : 0;
                return (RunRewardStage.Card, index, count);
            }
            case NativeRewardStage.PotionDiscard:
            {
                Reward? open = session.OpenPotionReward;
                IReadOnlyList<Reward> remaining = session.RemainingRewards;
                int index = -1;
                for (int position = 0; position < remaining.Count; position++)
                {
                    if (ReferenceEquals(remaining[position], open))
                    {
                        index = position;
                        break;
                    }
                }
                return (RunRewardStage.PotionDiscard, index, 0);
            }
            default:
                return (RunRewardStage.List, 0, 0);
        }
    }

    private static RunStepResult StepBlitz(ref RunState state, NativeRewardChoice choice,
        in RunDecisionRequest request, int optionIndex)
    {
        RunAction action = new()
        {
            Kind = choice.Kind switch
            {
                NativeRewardChoiceKind.Reward => RunActionKind.TakeReward,
                NativeRewardChoiceKind.Skip => request.Kind == RunDecision.CombatReward
                    ? RunActionKind.LeaveRewards : RunActionKind.SkipReward,
                NativeRewardChoiceKind.Card => RunActionKind.ChooseCard,
                NativeRewardChoiceKind.Potion => RunActionKind.ChoosePotion,
                NativeRewardChoiceKind.Reroll => RunActionKind.RerollCardReward,
                _ => RunActionKind.TakeReward,
            },
            Option = checked((byte)optionIndex),
        };
        RunKernel kernel = new(_table!);
        return kernel.Step(ref state, action);
    }

    private static void ApplyNative(NativeRewardSession session, NativeRewardChoiceKind kind,
        int optionIndex, IReadOnlyList<(NativeRewardChoiceKind Kind, int Index)> prefix)
    {
        foreach ((NativeRewardChoiceKind prefixKind, int prefixIndex) in prefix)
            ApplyOne(session, prefixKind, prefixIndex);
        ApplyOne(session, kind, optionIndex);
    }

    private static Task SkipTask(NativeRewardSession session)
    {
        session.SkipCurrent();
        return Task.CompletedTask;
    }

    private static Task LeaveTask(NativeRewardSession session)
    {
        session.Leave();
        return Task.CompletedTask;
    }

    private static void ApplyOne(NativeRewardSession session, NativeRewardChoiceKind kind,
        int optionIndex)
    {
        IReadOnlyList<NativeRewardChoice> choices = session.Choices();
        if ((uint)optionIndex >= (uint)choices.Count)
            throw new InvalidDataException(
                $"原版奖励选项 {optionIndex} 超出 {choices.Count}（阶段 {session.Stage}）。");
        NativeRewardChoice choice = choices[optionIndex];
        if (choice.Kind != kind)
            throw new InvalidDataException(
                $"原版奖励阶段 {session.Stage} 的选项 {optionIndex} 是 {choice.Kind}，期望 {kind}。");
        Task task = choice.Kind switch
        {
            NativeRewardChoiceKind.Reward => session.TakeRewardAsync(choice),
            NativeRewardChoiceKind.Skip => session.Stage == NativeRewardStage.List
                ? LeaveTask(session) : SkipTask(session),
            NativeRewardChoiceKind.Card or NativeRewardChoiceKind.Reroll =>
                session.ChooseCardAsync(choice),
            NativeRewardChoiceKind.Potion => session.ChoosePotionAsync(choice),
            _ => throw new InvalidDataException($"未知原版奖励选项 {choice.Kind}。"),
        };
        if (task.IsCompleted)
            task.GetAwaiter().GetResult();
        else
            _loop!.RunUntilCompleted(task, TimeSpan.FromSeconds(120), "原版奖励动作");
    }

    private sealed class RestoredRun
    {
        public required NativeRewardSession Session;
        public required NativeRunState Run;
        public required Player Player;
        public required Dictionary<int, int> DisplayOverrides;
        public required List<string> UncoveredDisplayMismatches;
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static RestoredRun RestoreAndGenerate(SerializableRun snapshot, int actFloor,
        RestoredRelicState[] relicState)
    {
        Task<NativeRunState> restore = RunTestHost.RestoreRewardSnapshotAsync(snapshot);
        _loop!.RunUntilCompleted(restore, TimeSpan.FromSeconds(120), "恢复奖励前快照");
        NativeRunState run = restore.GetAwaiter().GetResult();
        run.ActFloor = actFloor;
        FieldInfo refreshAllowed = typeof(RelicGrabBag).GetField("_refreshAllowed",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(RelicGrabBag).FullName, "_refreshAllowed");
        refreshAllowed.SetValue(run.SharedRelicGrabBag, true);
        Player player = LocalContext.GetMe(run)
            ?? throw new InvalidDataException("恢复后找不到玩家。");
        HashSet<int> displayMismatch = CheckRelicDisplay(player, relicState);
        Dictionary<int, int> displayOverrides = [];
        List<string> uncoveredDisplayMismatches = [];
        foreach (int index in displayMismatch)
        {
            string id = player.Relics[index].Id.Entry;
            if (TransientDisplayRelics.Contains(id))
            {
                displayOverrides[index] = relicState[index].Display;
                continue;
            }
            uncoveredDisplayMismatches.Add(
                $"{id}[{index}] recorded={relicState[index].Display} " +
                $"restored={player.Relics[index].DisplayAmount}");
        }
        AbstractRoom room = run.CurrentRoom
            ?? throw new InvalidDataException("恢复后没有当前房间。");
        Task<NativeRewardSession> create = NativeRewardSession.CreateAsync(player, room,
            useSynchronizer: false);
        _loop.RunUntilCompleted(create, TimeSpan.FromSeconds(120), "恢复后生成奖励");
        return new RestoredRun
        {
            Session = create.GetAwaiter().GetResult(),
            Run = run,
            Player = player,
            DisplayOverrides = displayOverrides,
            UncoveredDisplayMismatches = uncoveredDisplayMismatches,
        };
    }

    private static bool SameRequest(List<NativeRewardChoice> native, in RunDecisionRequest request,
        out string diff)
    {
        diff = "";
        if (request.Kind is not (RunDecision.CombatReward or RunDecision.CardReward
                or RunDecision.PotionDiscard)
            || request.OptionCount != native.Count)
        {
            return false;
        }
        for (int index = 0; index < native.Count; index++)
        {
            NativeRewardChoice choice = native[index];
            RunDecisionOption option = request.Options[index];
            RunDecisionOptionKind expected = choice.Kind switch
            {
                NativeRewardChoiceKind.Reward => RunDecisionOptionKind.Reward,
                NativeRewardChoiceKind.Skip => RunDecisionOptionKind.Skip,
                NativeRewardChoiceKind.Card => RunDecisionOptionKind.Card,
                NativeRewardChoiceKind.Potion => RunDecisionOptionKind.Potion,
                NativeRewardChoiceKind.Reroll => RunDecisionOptionKind.Reroll,
                _ => RunDecisionOptionKind.None,
            };
            if (option.Kind != expected)
                return false;
        }
        return true;
    }

    private static string Describe(List<NativeRewardChoice> choices)
        => string.Join(',', choices.Select(static choice => choice.Kind switch
        {
            NativeRewardChoiceKind.Reward => $"R:{choice.Reward?.GetType().Name}",
            NativeRewardChoiceKind.Card => $"C:{choice.Card?.Id.Entry}",
            NativeRewardChoiceKind.Potion => $"P:{choice.Potion?.Id.Entry}",
            _ => choice.Kind.ToString(),
        }));

    private static string Describe(in RunDecisionRequest request)
    {
        RunDecisionRequest copy = request;
        return string.Join(',', Enumerable.Range(0, copy.OptionCount)
            .Select(index => copy.Options[index].Kind.ToString()));
    }

    private static string ActionName(NativeRewardChoiceKind kind) => kind.ToString();

    private static int Diff(string kind, string label, int action, string where,
        List<string> differences, string? encounterName)
    {
        Console.WriteLine(
            $"REWARD-CASE status=diff group={kind} rewards=0 options={action} label={label}");
        foreach (string line in differences.Take(8))
            Console.WriteLine($"DIFF {kind} {label} {where} {line}");
        _ = encounterName;
        return 0;
    }

    private static int Reject(string kind, string label, string reason, string detail,
        string? encounterName, string stage)
    {
        Console.WriteLine(
            $"REWARD-CASE status=reject group={kind} rewards=0 options=0 label={label}");
        Console.WriteLine($"REWARD-REJECT reason={reason}");
        Console.WriteLine($"  {stage}: {Short(detail)}");
        _ = encounterName;
        return 0;
    }

    private static string Short(string text)
        => text.Length <= 200 ? text.Replace('\n', ' ') : text[..200].Replace('\n', ' ');

    private sealed class RestoredRelicState
    {
        public required int Index;
        public required int Display;
        public required int Status;
    }

    private static RestoredRelicState[] CaptureRelicState(Player player)
    {
        RestoredRelicState[] states = new RestoredRelicState[player.Relics.Count];
        for (int relicIndex = 0; relicIndex < player.Relics.Count; relicIndex++)
        {
            RelicModel relic = player.Relics[relicIndex];
            states[relicIndex] = new RestoredRelicState
            {
                Index = relicIndex,
                Display = relic.DisplayAmount,
                Status = (int)relic.Status,
            };
        }
        return states;
    }

    /// <summary>
    /// RelicModel.DisplayAmount is a computed getter with no setter. The run save restores the
    /// saved properties it is derived from (for example LastingCandy.CombatRewardsSeen). Record
    /// the pre-generation value and compare it here; a mismatch is printed and left in the raw
    /// dump instead of being normalized away.
    /// </summary>
    // These native DisplayAmount getters are computed from per-turn fields that the run save does
    // not carry; the restored snapshot reports 0 while the live pre-generation value is what the
    // option comparison should use. This is the small, explicit set allowed by the review.
    private static readonly HashSet<string> TransientDisplayRelics = new(StringComparer.Ordinal)
    {
        "ORNAMENTAL_FAN", "LETTER_OPENER", "KUSARIGAMA", "SHURIKEN",
        "RAINBOW_RING", "BEATING_REMNANT", "MUSIC_BOX",
    };

    private static readonly HashSet<string> ReportedDisplayMismatch = new(StringComparer.Ordinal);

    private static HashSet<int> CheckRelicDisplay(Player player, RestoredRelicState[] states)
    {
        HashSet<int> mismatch = [];
        for (int relicIndex = 0; relicIndex < states.Length; relicIndex++)
        {
            RestoredRelicState state = states[relicIndex];
            if (state.Index >= player.Relics.Count)
                continue;
            RelicModel relic = player.Relics[state.Index];
            // Native save/restore does not carry relic Status; it falls back to Normal. Write the
            // pre-snapshot status back so the option comparison isolates the reward action.
            relic.Status = (RelicStatus)state.Status;
            if (relic.DisplayAmount == state.Display)
                continue;
            mismatch.Add(state.Index);
            if (ReportedDisplayMismatch.Add($"{relic.Id.Entry}:{state.Index}"))
            {
                Console.WriteLine(
                    $"REWARD-DISPLAY-MISMATCH relic={relic.Id.Entry} index={state.Index} " +
                    $"recorded={state.Display} restored={relic.DisplayAmount}");
            }
        }
        return mismatch;
    }

    private static string? FirstDifference(string expected, string actual)
    {
        string[] left = expected.Split('\n');
        string[] right = actual.Split('\n');
        int shared = Math.Min(left.Length, right.Length);
        for (int index = 0; index < shared; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                return $"native=[{left[index]}] blitz=[{right[index]}]";
        }
        if (left.Length != right.Length)
            return $"行数 native={left.Length} blitz={right.Length}";
        return null;
    }

    private static RunCoord Coord(MapCoord coord)
        => new(checked((byte)coord.col), checked((byte)coord.row));

    private static long SeedFor(string label)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte value in System.Text.Encoding.UTF8.GetBytes(label))
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        return (long)(hash & 0x7FFFFFFFFFFFFFFFUL);
    }

    private static bool SkipRoomEntryPrefix(AbstractRoom room, ref Task __result)
    {
        if (!_skipRoomEntry || room is MapRoom or CombatRoom)
            return true;
        _skipRoomEntry = false;
        __result = Task.CompletedTask;
        return false;
    }

    private static UnlockState PresetUnlock(string name) => name switch
    {
        "empty" => new UnlockState([], [], 0),
        "all" => new UnlockState([.. EpochModel.AllEpochIds],
            [.. Enumerable.Range(0, RunCatalog.EncounterCount)
                .Select(index => new ModelId("ENCOUNTER", RunCatalog.EncounterName((ushort)index)))],
            999999999),
        _ => throw new InvalidDataException($"未知存档预设 {name}。"),
    };

    private static bool IsEliteOrBossRoot(string corpora, string root)
    {
        string? path = null;
        string fixture = Path.Combine(corpora, "fixtures", root + ".json");
        if (File.Exists(fixture))
        {
            path = fixture;
        }
        else if (root.StartsWith("quality-", StringComparison.Ordinal))
        {
            string quality = Path.Combine(corpora, "quality", root["quality-".Length..],
                "request.json");
            if (File.Exists(quality))
                path = quality;
        }
        if (path == null)
            return false;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("encounterId", out JsonElement encounter))
            return false;
        string? id = encounter.GetString();
        return id != null
            && (id.EndsWith("_ELITE", StringComparison.Ordinal)
                || id.EndsWith("_BOSS", StringComparison.Ordinal));
    }

    private static IEnumerable<string> EnumerateRoots(string data)
    {
        string d1 = Path.Combine(data, "d1");
        return Directory.EnumerateDirectories(d1)
            .Select(Path.GetFileName)
            .Where(static name => name != null)
            .OrderBy(static name => name, StringComparer.Ordinal)!;
    }

    private static void ForceNextEncounter(NativeRunState run, EncounterModel encounter, int actIndex)
    {
        ActModel act = run.Acts[actIndex];
        RoomSet rooms = (RoomSet)(typeof(ActModel)
            .GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(act) ?? throw new InvalidDataException("ActModel._rooms 不存在。"));
        switch (encounter.RoomType)
        {
            case RoomType.Monster:
                if (rooms.normalEncounters.Count == 0)
                    throw new InvalidDataException("本幕没有普通遭遇。");
                rooms.normalEncounters[rooms.normalEncountersVisited % rooms.normalEncounters.Count] =
                    encounter;
                break;
            case RoomType.Elite:
                if (rooms.eliteEncounters.Count == 0)
                    throw new InvalidDataException("本幕没有精英遭遇。");
                rooms.eliteEncounters[rooms.eliteEncountersVisited % rooms.eliteEncounters.Count] =
                    encounter;
                break;
            case RoomType.Boss:
                rooms.Boss = encounter;
                break;
            default:
                throw new InvalidDataException($"不是战斗遭遇：{encounter.Id.Entry}。");
        }
    }

    private static MapPoint FindPoint(NativeRunState run, RoomType roomType)
    {
        MapPointType type = roomType switch
        {
            RoomType.Monster => MapPointType.Monster,
            RoomType.Elite => MapPointType.Elite,
            RoomType.Boss => MapPointType.Boss,
            _ => throw new InvalidDataException($"不是战斗房间：{roomType}。"),
        };
        if (type == MapPointType.Boss)
            return run.Map.BossMapPoint;
        foreach (MapPoint point in run.Map.StartingMapPoint.Children)
        {
            if (point.PointType == type && !run.VisitedMapCoords.Contains(point.coord))
                return point;
        }
        foreach (MapPoint point in run.Map.GetAllMapPoints())
        {
            if (point.PointType == type && !run.VisitedMapCoords.Contains(point.coord))
                return point;
        }
        throw new InvalidDataException($"地图没有可用的 {type} 点。");
    }

    private static (int Code, string Stdout, string Stderr) RunChildProcess(string data,
        string corpora, string[] caseArgs)
    {
        string assembly = typeof(RewardCompare).Assembly.Location;
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--reward-compare");
        start.ArgumentList.Add("--data");
        start.ArgumentList.Add(data);
        start.ArgumentList.Add("--corpora");
        start.ArgumentList.Add(corpora);
        foreach (string argument in caseArgs)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动对照子进程。");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.GetAwaiter().GetResult(),
            stderr.GetAwaiter().GetResult());
    }
}
