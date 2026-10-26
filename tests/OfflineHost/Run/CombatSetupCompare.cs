// --combat-setup-compare: enter a combat map point twice in the same process, once through the
// native RunManager and once through the Core RunCombatSetup, then compare the opening Battle,
// Rules and post-entry RunState. Child-process model matches RulesBuildCompare: each case starts
// from a fresh static game state.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;
using RunState = BlitzSolver.Simulation.Run.RunState;

namespace OfflineHost;

internal static class CombatSetupCompare
{
    private static readonly string[] Characters =
        ["Ironclad", "Silent", "Regent", "Necrobinder", "Defect"];
    private static ContentTable? _table;

    public static int Run(string[] args)
    {
        string data = "tests/Golden/data";
        string corpora = "tests/corpora";
        string kind = "";
        int seeds = 20;
        int jobs = 8;
        int shard = 0;
        int shards = 1;
        string? root = null;
        string? character = null;
        int ascension = 0;
        string? seed = null;
        int? encounter = null;
        bool cover = false;
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
                case "--setup-kind" when index + 1 < args.Length:
                    kind = args[++index];
                    break;
                case "--setup-seeds" when index + 1 < args.Length:
                    seeds = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--setup-jobs" when index + 1 < args.Length:
                    jobs = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--setup-shard" when index + 2 < args.Length:
                    shard = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    shards = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--setup-root" when index + 1 < args.Length:
                    root = args[++index];
                    break;
                case "--setup-character" when index + 1 < args.Length:
                    character = args[++index];
                    break;
                case "--setup-ascension" when index + 1 < args.Length:
                    ascension = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--setup-seed" when index + 1 < args.Length:
                    seed = args[++index];
                    break;
                case "--setup-encounter" when index + 1 < args.Length:
                    encounter = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--setup-cover":
                    cover = true;
                    break;
                default:
                    Console.Error.WriteLine($"未知参数：{args[index]}");
                    return 2;
            }
        }
        if (jobs is < 1 or > 8 || shards < 1 || shard < 0 || shard >= shards)
        {
            Console.Error.WriteLine("--setup-jobs 必须在 1 到 8 之间，分片参数必须合法。");
            return 2;
        }
        if (root != null || character != null || encounter != null)
            return RunChild(data, corpora, root, character, ascension, seed, encounter, cover);
        if (kind.Length == 0)
        {
            Console.Error.WriteLine("需要 --setup-kind natural|encounter|d1，或单例模式参数。");
            return 2;
        }
        return cover
            ? RunCoverage(data, corpora, seeds, jobs)
            : RunParent(data, corpora, kind, seeds, jobs, shard, shards);
    }

    private static int RunChild(string data, string corpora, string? root, string? character,
        int ascension, string? seed, int? encounter, bool cover = false)
    {
        MainLoopContext loop = new();
        SynchronizationContext.SetSynchronizationContext(loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();
        _table = ContentTableExporter.Export();
        if (root != null)
            return CompareD1(loop, data, corpora, root, seed ?? "", cover);
        if (character != null)
            return CompareNatural(loop, character, ascension, seed ?? "");
        if (encounter is { } encounterId)
            return CompareEncounter(loop, encounterId, ascension, seed ?? $"R2A2-ENC-{ascension}-{encounterId:D3}");
        Console.Error.WriteLine("子进程缺少单例参数。");
        return 2;
    }

    private static int RunParent(string data, string corpora, string kind, int seeds, int jobs,
        int shard, int shards)
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
                        string seedText = $"R2A2-{character}-A{ascension}-{seed:D3}";
                        cases.Add(("natural",
                            ["--setup-character", character, "--setup-ascension",
                                ascension.ToString(CultureInfo.InvariantCulture), "--setup-seed", seedText],
                            $"natural/{character}/A{ascension}/{seedText}"));
                    }
                }
            }
        }
        if (kind is "all" or "encounter")
        {
            for (int id = 0; id < RunCatalog.EncounterCount; id++)
            {
                foreach (int ascension in new[] { 0, 10 })
                {
                    for (int seed = 0; seed < seeds; seed++)
                    {
                        string seedText = $"R2A2-ENC-{ascension}-{id:D3}-{seed:D2}";
                        cases.Add(("encounter",
                            ["--setup-encounter", id.ToString(CultureInfo.InvariantCulture),
                                "--setup-ascension", ascension.ToString(CultureInfo.InvariantCulture),
                                "--setup-seed", seedText],
                            $"encounter/A{ascension}/{RunCatalog.EncounterName((ushort)id)}/{seedText}"));
                    }
                }
            }
        }
        if (kind is "all" or "d1")
        {
            foreach (string root in EnumerateRoots(data))
            {
                for (int seed = 0; seed < seeds; seed++)
                {
                    string seedText = seed == 0 ? "" : $"#seed{seed}";
                    string[] caseArgs = ["--setup-root", root];
                    if (seedText.Length != 0)
                        caseArgs = [.. caseArgs, "--setup-seed", $"R2A2-D1-{seed}"];
                    cases.Add(("d1", caseArgs, $"d1/{root}{seedText}"));
                }
            }
        }
        if (shards > 1)
            cases = cases.Where((_, index) => index % shards == shard).ToList();
        if (cases.Count == 0)
        {
            Console.Error.WriteLine($"--setup-kind {kind} 没有用例。");
            return 2;
        }

        object gate = new();
        int same = 0, diff = 0, reject = 0;
        Dictionary<string, int> rejects = new(StringComparer.Ordinal);
        HashSet<string> encounters = new(StringComparer.Ordinal);
        Console.WriteLine($"SETUP cases={cases.Count} jobs={jobs} shard={shard}/{shards}");
        Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = jobs }, item =>
        {
            (int code, string stdout, string stderr) = RunChildProcess(data, corpora, item.Args);
            List<string> lines = stdout.Split('\n')
                .Where(static line => line.StartsWith("SETUP ", StringComparison.Ordinal)
                    || line.StartsWith("  ", StringComparison.Ordinal))
                .ToList();
            lock (gate)
            {
                string? status = null;
                foreach (string line in lines)
                {
                    if (line.StartsWith("SETUP ", StringComparison.Ordinal))
                    {
                        status = line;
                        break;
                    }
                }
                if (status == null)
                {
                    diff++;
                    Console.WriteLine($"SETUP kind={item.Kind} label={item.Label} status=crash code={code}");
                    if (stderr.Length > 0)
                        Console.WriteLine("  " + stderr.Split('\n')[0]);
                    return;
                }
                if (status.Contains(" status=same", StringComparison.Ordinal))
                    same++;
                else if (status.Contains(" status=reject", StringComparison.Ordinal))
                {
                    reject++;
                    string reason = status.Split("reason=", 2).ElementAtOrDefault(1)?.Split(' ')[0] ?? "?";
                    rejects[reason] = rejects.GetValueOrDefault(reason) + 1;
                    Console.WriteLine($"SETUP-REJECT-CASE {item.Label} {reason}");
                }
                else
                {
                    diff++;
                    Console.WriteLine(status);
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("  ", StringComparison.Ordinal))
                            Console.WriteLine(line);
                    }
                    if (lines.Count > 1)
                        Console.WriteLine("  " + lines[1]);
                }
                int slash = item.Label.IndexOf('/');
                if (slash >= 0)
                    encounters.Add(item.Label[..item.Label.LastIndexOf('/')]);
            }
        });
        Console.WriteLine($"SETUP-SUMMARY kind={kind} same={same} diff={diff} reject={reject}");
        foreach ((string reason, int count) in rejects.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"SETUP-REJECT {reason}={count}");
        Console.WriteLine($"SETUP-COVERAGE groups={encounters.Count}");
        return diff == 0 ? 0 : 1;
    }

    private static int RunCoverage(string data, string corpora, int seeds, int jobs)
    {
        string[] roots = [.. EnumerateRoots(data)];
        Dictionary<string, int> relics = new(StringComparer.Ordinal);
        Dictionary<string, int> powers = new(StringComparer.Ordinal);
        Dictionary<string, int> enchantments = new(StringComparer.Ordinal);
        Dictionary<string, int> rejects = new(StringComparer.Ordinal);
        object gate = new();
        Parallel.ForEach(roots, new ParallelOptions { MaxDegreeOfParallelism = jobs }, root =>
        {
            (int code, string stdout, string stderr) = RunChildProcess(data, corpora,
                ["--setup-root", root, "--setup-cover"]);
            lock (gate)
            {
                foreach (string line in stdout.Split('\n'))
                {
                    if (line.StartsWith("SETUP-COVER ", StringComparison.Ordinal))
                    {
                        AccumulateCoverage(line, "relics=", relics);
                        AccumulateCoverage(line, "powers=", powers);
                        AccumulateCoverage(line, "enchantments=", enchantments);
                    }
                    else if (line.StartsWith("SETUP-COVER-REJECT ", StringComparison.Ordinal))
                    {
                        string reason = line["SETUP-COVER-REJECT reason=".Length..].Trim();
                        rejects[reason] = rejects.GetValueOrDefault(reason) + 1;
                    }
                }
            }
        });
        Console.WriteLine($"SETUP-COVERAGE roots={roots.Length} seeds={seeds}");
        foreach ((string id, int count) in relics.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"SETUP-USED-RELIC {id}={count}");
        foreach ((string id, int count) in powers.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"SETUP-USED-POWER {id}={count}");
        foreach ((string id, int count) in enchantments.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"SETUP-USED-ENCHANTMENT {id}={count}");
        foreach ((string reason, int count) in rejects.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"SETUP-COVER-REJECT {reason}={count}");
        return 0;
    }

    private static void AccumulateCoverage(string line, string field, Dictionary<string, int> counts)
    {
        int start = line.IndexOf(field, StringComparison.Ordinal);
        if (start < 0)
            return;
        start += field.Length;
        int end = line.IndexOf(' ', start);
        string value = end < 0 ? line[start..] : line[start..end];
        if (value.Length == 0)
            return;
        foreach (string id in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            counts[id] = counts.GetValueOrDefault(id) + 1;
    }

    private static int CompareNatural(MainLoopContext loop, string characterName, int ascension, string seed)
    {
        CharacterModel character = ModelDb.AllCharacters.FirstOrDefault(
            candidate => candidate.GetType().Name == characterName)
            ?? throw new InvalidDataException($"未知角色 {characterName}。");
        OfflineCombatWaiter waiter = new(loop);
        Task<(NativeRunState Run, Player Player)> start = RunTestHost.StartNewRunAsync(
            waiter, character, ascension, seed);
        loop.RunUntilCompleted(start, TimeSpan.FromSeconds(120), "自然开局");
        (NativeRunState native, Player player) = start.GetAwaiter().GetResult();
        MapPoint first = native.Map.StartingMapPoint.Children
            .FirstOrDefault(static point => point.PointType == MapPointType.Monster)
            ?? throw new InvalidDataException("第一排没有战斗点。");
        return CompareEntry(loop, waiter, native, player, first.coord, "natural", seed, null);
    }

    private static int CompareEncounter(MainLoopContext loop, int encounterId, int ascension, string seed)
    {
        if ((uint)encounterId >= (uint)RunCatalog.EncounterCount)
        {
            Console.Error.WriteLine($"遭遇 id 越界：{encounterId}");
            return 2;
        }
        string nativeId = RunCatalog.EncounterName((ushort)encounterId);
        EncounterModel canonical = ModelDb.All.OfType<EncounterModel>()
            .FirstOrDefault(encounter => encounter.Id.Entry == nativeId)
            ?? throw new InvalidDataException($"原版没有遭遇 {nativeId}。");
        CharacterModel character = ModelDb.AllCharacters.First(
            candidate => candidate.GetType().Name == "Ironclad");
        OfflineCombatWaiter waiter = new(loop);
        Task<(NativeRunState Run, Player Player)> start = RunTestHost.StartNewRunAsync(
            waiter, character, ascension, seed);
        loop.RunUntilCompleted(start, TimeSpan.FromSeconds(120), "遭遇开局");
        (NativeRunState native, Player player) = start.GetAwaiter().GetResult();
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
            loop.RunUntilCompleted(setAct, TimeSpan.FromSeconds(120), "设置测试幕");
        }
        ForceNextEncounter(native, canonical, actIndex);
        MapPoint point = FindPoint(native, canonical.RoomType);
        return CompareEntry(loop, waiter, native, player, point.coord, "encounter", nativeId, nativeId);
    }

    private static int CompareD1(MainLoopContext loop, string data, string corpora, string root,
        string seedOverride, bool cover)
    {
        OfflineRequest request = Program.LoadRequest(data, corpora, root);
        if (request.Relics.Any(static relic => relic.IntegerMembers.Count != 0
            || relic.BooleanMembers.Count != 0))
        {
            Console.WriteLine(
                $"SETUP kind=d1 label={root} status=reject reason=unsupported-relic-state encounter=-");
            Console.WriteLine("  D1 请求给遗物注入了不进入 RunState 的整数字段。");
            return 0;
        }
        GeneratedScenarioSetup setup = GeneratedScenarioSetup.Prepare(request);
        OfflineRequest applied = setup.Request;
        if (seedOverride.Length != 0)
            applied = applied with { Seed = seedOverride };
        OfflineCombatWaiter waiter = new(loop);
        Task<ScenarioBuilder.PreparedRun> prepare = ScenarioBuilder.PrepareRunAsync(
            waiter, applied, preparedSetup: setup);
        loop.RunUntilCompleted(prepare, TimeSpan.FromSeconds(120), "D1 建局");
        using ScenarioBuilder.PreparedRun prepared = prepare.GetAwaiter().GetResult();
        if (!RunCatalog.TryEncounter(prepared.Encounter.Id.Entry, out ushort encounterId))
            throw new InvalidDataException($"D1 遭遇不在目录：{prepared.Encounter.Id.Entry}");
        EncounterModel target = prepared.Encounter;
        int actIndex = prepared.Run.CurrentActIndex;
        ForceNextEncounter(prepared.Run, target, actIndex);
        MapPoint point = FindPoint(prepared.Run, target.RoomType);
        if (cover)
        {
            RunState before = RunStateImporter.Import(prepared.Run, prepared.Player);
            try
            {
                CombatSetupResult result = RunCombatSetup.EnterMapCoord(before, _table!,
                    new RunCoord((byte)point.coord.col, (byte)point.coord.row));
                Console.WriteLine("SETUP-COVER relics=" + string.Join(",",
                    Enumerable.Range(0, before.Relics.Count)
                        .Select(index => Registry.Relic(before.Relics.Items[index].Id).NativeId))
                    + " powers=" + string.Join(",",
                        Enumerable.Range(0, result.Battle.PowerCount)
                            .Select(index => Registry.Power(result.Battle.Powers[index].Id).NativeId))
                    + " enchantments=" + string.Join(",",
                        Enumerable.Range(0, result.Battle.CardCount)
                            .Where(index => result.Battle.Cards[index].EnchantmentId != 0)
                            .Select(index => Registry.Enchantment((ushort)(result.Battle.Cards[index].EnchantmentId - 1)).NativeId)));
                return 0;
            }
            catch (ContentUnavailableException error)
            {
                Console.WriteLine($"SETUP-COVER-REJECT reason={error.Reason}");
                return 0;
            }
            catch (BlitzSolver.Simulation.Combat.UnsupportedException)
            {
                Console.WriteLine("SETUP-COVER-REJECT reason=core-unsupported");
                return 0;
            }
        }
        return CompareEntry(loop, waiter, prepared.Run, prepared.Player, point.coord, "d1",
            root + (seedOverride.Length == 0 ? "" : "#" + seedOverride), target.Id.Entry);
    }

    private static int CompareEntry(MainLoopContext loop, OfflineCombatWaiter waiter,
        NativeRunState native, Player player, MapCoord mapCoord, string kind, string label,
        string? encounterName)
    {
        RunCoord coord = new((byte)mapCoord.col, (byte)mapCoord.row);
        try
        {
            RunState before = RunStateImporter.Import(native, player);
            Task enter = RunManager.Instance.EnterMapCoord(mapCoord);
            loop.RunUntilCompleted(enter, TimeSpan.FromSeconds(120), "原生进点");
            Task<CombatState> playable = waiter.WaitForPlayableCombatAsync();
            loop.RunUntilCompleted(playable, TimeSpan.FromSeconds(120), "原生等待可出牌");
            CombatState combat = playable.GetAwaiter().GetResult();
            Player combatPlayer = LocalContext.GetMe(combat)
                ?? throw new InvalidDataException("原生战斗找不到玩家。");
            NativeRunState after = combat.RunState as NativeRunState
                ?? throw new InvalidDataException("原生战斗的 RunState 类型不对。");
            Rules nativeRules = RulesCompiler.Compile(combat, combatPlayer, after);
            LiveCombatTracker tracker = new(combat);
            Battle expected = tracker.ImportRoot(combat, combatPlayer, nativeRules);
            string expectedRun = NativeRunDump.Capture(after, combatPlayer);

            CombatSetupResult actual = RunCombatSetup.EnterMapCoord(before, _table!, coord);
            List<string> battleDiff = BattleDump.Diff(expected, actual.Battle, "native", "blitz");
            byte[] nativeRuleBytes = RulesCodec.Encode(nativeRules);
            byte[] blitzRuleBytes = RulesCodec.Encode(actual.Rules);
            string? rulesDiff = nativeRuleBytes.AsSpan().SequenceEqual(blitzRuleBytes)
                ? null
                : string.Join(" | ", RulesDiff.Compare(nativeRules, actual.Rules).Take(4));
            string blitzRun = RunDump.Capture(actual.Run);
            string? runDiff = FirstDifference(expectedRun, blitzRun);
            if (battleDiff.Count == 0 && rulesDiff == null && runDiff == null)
            {
                Console.WriteLine(
                    $"SETUP kind={kind} label={label} status=same encounter={encounterName ?? "-"}");
                return 0;
            }
            Console.WriteLine(
                $"SETUP kind={kind} label={label} status=diff battle={battleDiff.Count} rules={rulesDiff != null} run={runDiff != null} encounter={encounterName ?? "-"}");
            foreach (string line in battleDiff.Take(12))
                Console.WriteLine("  BATL " + line);
            if (rulesDiff != null)
                Console.WriteLine("  RULE " + rulesDiff);
            if (runDiff != null)
                Console.WriteLine("  RUN  " + runDiff);
            return 0;
        }
        catch (ContentUnavailableException error)
        {
            Console.WriteLine($"SETUP kind={kind} label={label} status=reject reason={error.Reason} encounter={encounterName ?? "-"}");
            Console.WriteLine("  " + error.Detail);
            return 0;
        }
        catch (UnsupportedImportException error)
        {
            Console.WriteLine($"SETUP kind={kind} label={label} status=reject reason={error.Reason} encounter={encounterName ?? "-"}");
            Console.WriteLine("  " + error.Detail);
            return 0;
        }
        catch (BlitzSolver.Simulation.Combat.UnsupportedException error)
        {
            Console.WriteLine($"SETUP kind={kind} label={label} status=reject reason=core-unsupported encounter={encounterName ?? "-"}");
            Console.WriteLine("  " + error.Message);
            return 0;
        }
        catch (CapacityException error)
        {
            Console.WriteLine($"SETUP kind={kind} label={label} status=reject reason=capacity encounter={encounterName ?? "-"}");
            Console.WriteLine("  " + error.Message);
            return 0;
        }
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

    private static string? FirstDifference(string expected, string actual)
    {
        string[] left = expected.Split('\n');
        string[] right = actual.Split('\n');
        int shared = Math.Min(left.Length, right.Length);
        for (int index = 0; index < shared; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                return $"line {index + 1}: native=[{left[index]}] blitz=[{right[index]}]";
        }
        if (left.Length != right.Length)
            return $"行数 native={left.Length} blitz={right.Length}";
        return null;
    }

    private static IEnumerable<string> EnumerateRoots(string data)
    {
        string d1 = Path.Combine(data, "d1");
        return Directory.EnumerateDirectories(d1)
            .Select(Path.GetFileName)
            .Where(static name => name != null)
            .OrderBy(static name => name, StringComparer.Ordinal)!;
    }

    private static (int Code, string Stdout, string Stderr) RunChildProcess(string data, string corpora,
        string[] caseArgs)
    {
        string assembly = typeof(CombatSetupCompare).Assembly.Location;
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--combat-setup-compare");
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
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
