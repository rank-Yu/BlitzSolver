// --rules-build-compare: compiles Rules the native way (RulesCompiler on the live root), imports the
// same root's RunState and Battle, rebuilds Rules with the Core RulesBuilder, and compares the
// canonical encodings byte for byte. Cases: every D1 input (including the turn-setup fixtures), the
// first natural combat for 5 characters x ascension 0/10 x seeds, and every RunCatalog encounter.
using System.Diagnostics;
using System.Globalization;
using System.Text;
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
using RunState = BlitzSolver.Simulation.Run.RunState;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;

namespace OfflineHost;

internal static class RulesBuildCompare
{
    private static readonly string[] Characters =
        ["Ironclad", "Silent", "Regent", "Necrobinder", "Defect"];

    private static ContentTable? _table;
    private static bool _printContent;

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
        bool printContent = false;
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
                case "--build-kind" when index + 1 < args.Length:
                    kind = args[++index];
                    break;
                case "--build-seeds" when index + 1 < args.Length:
                    seeds = int.Parse(args[++index]);
                    break;
                case "--build-jobs" when index + 1 < args.Length:
                    jobs = int.Parse(args[++index]);
                    break;
                case "--build-shard" when index + 2 < args.Length:
                    shard = int.Parse(args[++index]);
                    shards = int.Parse(args[++index]);
                    break;
                case "--build-root" when index + 1 < args.Length:
                    root = args[++index];
                    break;
                case "--build-character" when index + 1 < args.Length:
                    character = args[++index];
                    break;
                case "--build-ascension" when index + 1 < args.Length:
                    ascension = int.Parse(args[++index]);
                    break;
                case "--build-seed" when index + 1 < args.Length:
                    seed = args[++index];
                    break;
                case "--build-encounter" when index + 1 < args.Length:
                    encounter = int.Parse(args[++index]);
                    break;
                case "--build-content":
                    printContent = true;
                    break;
                default:
                    Console.Error.WriteLine($"未知参数：{args[index]}");
                    return 2;
            }
        }
        if (jobs is < 1 or > 8 || shards < 1 || shard < 0 || shard >= shards)
        {
            Console.Error.WriteLine("--build-jobs 必须在 1 到 8 之间，分片参数必须合法。");
            return 2;
        }
        if (root != null || character != null || encounter != null)
        {
            _printContent = printContent;
            return RunChild(data, corpora, root, character, ascension, seed, encounter);
        }
        if (kind.Length == 0)
        {
            Console.Error.WriteLine("需要 --build-kind d1|natural|encounter|all，或单例模式参数。");
            return 2;
        }
        return RunParent(data, corpora, kind, seeds, jobs, shard, shards, printContent);
    }

    private static int RunChild(string data, string corpora, string? root, string? character,
        int ascension, string? seed, int? encounter)
    {
        MainLoopContext loop = new();
        SynchronizationContext.SetSynchronizationContext(loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();
        _table = ContentTableExporter.Export();
        if (root != null)
            return CompareD1(data, corpora, loop, root);
        if (character != null)
            return CompareNatural(loop, character, ascension, seed ?? "");
        if (encounter is { } encounterId)
            return CompareEncounter(loop, encounterId, seed ?? $"R2A1-ENC-{encounterId:D3}");
        Console.Error.WriteLine("子进程缺少单例参数。");
        return 2;
    }

    private static int RunParent(string data, string corpora, string kind, int seeds, int jobs,
        int shard, int shards, bool printContent)
    {
        if (printContent)
        {
            MainLoopContext loop = new();
            SynchronizationContext.SetSynchronizationContext(loop);
            GameBootstrap.ApplyGodotBypasses();
            GameBootstrap.SkipGodotNodeStaticConstructors();
            GameBootstrap.InitializeStaticState();
            _table = ContentTableExporter.Export();
        }
        List<(string Kind, string[] Args, string Label)> cases = [];
        if (kind is "all" or "natural")
        {
            for (int seed = 0; seed < seeds; seed++)
            {
                foreach (string character in Characters)
                {
                    foreach (int ascension in new[] { 0, 10 })
                    {
                        string seedText = $"R2A1-{character}-A{ascension}-{seed:D2}";
                        cases.Add(("natural",
                            ["--build-character", character, "--build-ascension",
                                ascension.ToString(CultureInfo.InvariantCulture), "--build-seed", seedText],
                            $"natural/{character}/A{ascension}/{seedText}"));
                    }
                }
            }
        }
        if (kind is "all" or "d1")
        {
            foreach (string root in EnumerateRoots(data))
                cases.Add(("d1", ["--build-root", root], $"d1/{root}"));
        }
        if (kind is "all" or "encounter")
        {
            for (int id = 0; id < RunCatalog.EncounterCount; id++)
                cases.Add(("encounter", ["--build-encounter", id.ToString(CultureInfo.InvariantCulture)],
                    $"encounter/{RunCatalog.EncounterName((ushort)id)}"));
            // The Ruby Raiders encounter rolls three of five raiders from the run seed. This extra
            // seed covers the two raider types the default seed does not generate, so the content
            // coverage check sees every monster.
            cases.Add(("encounter",
                ["--build-encounter", "55", "--build-seed", "R2A1-RUBY-8"],
                "encounter/RUBY_RAIDERS_NORMAL#coverage"));
        }
        if (shards > 1)
        {
            cases = cases.Where((_, index) => index % shards == shard).ToList();
        }
        if (cases.Count == 0)
        {
            Console.Error.WriteLine($"--build-kind {kind} 没有用例。");
            return 2;
        }

        object gate = new();
        int same = 0, diff = 0, reject = 0, skip = 0;
        Dictionary<string, int> rejectReasons = new(StringComparer.Ordinal);
        Dictionary<string, int> diffTables = new(StringComparer.Ordinal);
        HashSet<string> usedCards = new(StringComparer.Ordinal);
        HashSet<string> usedMonsters = new(StringComparer.Ordinal);
        int casesRun = 0;
        Console.WriteLine($"BUILD cases={cases.Count} jobs={jobs} shard={shard}/{shards}");
        Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = jobs }, item =>
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string[] caseArgs = printContent ? [.. item.Args, "--build-content"] : item.Args;
            (int code, string stdout, string stderr) = RunChildProcess(data, corpora, caseArgs);
            stopwatch.Stop();
            List<string> lines = stdout.Split('\n')
                .Where(static line => line.StartsWith("BUILD", StringComparison.Ordinal)
                    || line.StartsWith("  ", StringComparison.Ordinal))
                .ToList();
            lock (gate)
            {
                casesRun++;
                foreach (string line in lines)
                {
                    if (line.StartsWith("BUILD-CONTENT ", StringComparison.Ordinal))
                    {
                        AccumulateUsed(line, "cards=", usedCards);
                        AccumulateUsed(line, "monsters=", usedMonsters);
                        continue;
                    }
                    if (!line.StartsWith("BUILD ", StringComparison.Ordinal))
                        continue;
                    if (line.Contains("status=same", StringComparison.Ordinal))
                        same++;
                    else if (line.Contains("status=diff", StringComparison.Ordinal))
                    {
                        diff++;
                        string table = ValueOf(line, "table=");
                        diffTables[table] = diffTables.GetValueOrDefault(table) + 1;
                    }
                    else if (line.Contains("status=reject", StringComparison.Ordinal))
                    {
                        reject++;
                        string reason = ValueOf(line, "reason=");
                        rejectReasons[reason] = rejectReasons.GetValueOrDefault(reason) + 1;
                    }
                    else if (line.Contains("status=skip", StringComparison.Ordinal))
                    {
                        skip++;
                    }
                }
                Console.WriteLine($"case={item.Label} exit={code} ms={stopwatch.Elapsed.TotalMilliseconds:0}");
                foreach (string line in lines)
                    Console.WriteLine(line);
                if (stderr.Length > 0)
                    Console.Error.WriteLine(stderr.TrimEnd());
            }
        });
        Console.WriteLine(
            $"BUILD-SUMMARY cases={casesRun} same={same} diff={diff} reject={reject} skip={skip}");
        foreach ((string reason, int count) in rejectReasons.OrderByDescending(entry => entry.Value)
                     .ThenBy(entry => entry.Key, StringComparer.Ordinal))
            Console.WriteLine($"BUILD-REJECT reason={reason} count={count}");
        foreach ((string table, int count) in diffTables.OrderByDescending(entry => entry.Value)
                     .ThenBy(entry => entry.Key, StringComparer.Ordinal))
            Console.WriteLine($"BUILD-DIFF table={table} count={count}");
        if (printContent && _table != null)
        {
            int unusedCards = 0, totalCards = 0;
            for (int id = 0; id < _table.Cards.Length; id++)
            {
                if (_table.Cards[id] is not { } card)
                    continue;
                for (int upgrade = 0; upgrade < card.Levels.Length; upgrade++)
                {
                    totalCards++;
                    if (!usedCards.Contains($"{id}:{upgrade}"))
                        unusedCards++;
                }
            }
            int unusedMonsters = 0;
            int totalMonsters = _table.SpawnMonsters[0].Length;
            for (int id = 0; id < totalMonsters; id++)
            {
                if (!usedMonsters.Contains(id.ToString(CultureInfo.InvariantCulture)))
                    unusedMonsters++;
            }
            Console.WriteLine(
                $"BUILD-CONTENT-SUMMARY cards={usedCards.Count}/{totalCards} unusedCards={unusedCards} " +
                $"monsters={usedMonsters.Count}/{totalMonsters} unusedMonsters={unusedMonsters} " +
                $"powers={_table.PowerVars.Length} relics={_table.RelicVars.Length} potions={_table.Potions.Length}");
        }
        return diff == 0 && reject == 0 ? 0 : 1;
    }

    private static (int Code, string Stdout, string Stderr) RunChildProcess(string data,
        string corpora, string[] extra)
    {
        ProcessStartInfo start = new("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--rules-build-compare");
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

    private static int CompareD1(string data, string corpora, MainLoopContext loop, string root)
    {
        OfflineRequest request = Program.LoadRequest(data, corpora, root);
        bool hasPrefix = request.FlashPrefixActions.Length != 0
            || request.FlashPrefixPlan.Length != 0;
        if (hasPrefix)
            return ComparePrefixD1(loop, request, root);
        if (request.FlashTurnSetupRootForTest)
        {
            SetupRootSet set = SetupRootSet.Build(loop, request, needConfig: false);
            RunState setupRun = set.Run;
            ushort encounterId = EncounterOf(in setupRun);
            for (int index = 0; index < set.Roots.Count; index++)
            {
                Console.WriteLine(
                    $"BUILD kind=d1 label={root}#setup{index} status=begin");
                CompareOne("d1", $"{root}#setup{index}", set.Rules, set.Roots[index],
                    in setupRun, encounterId);
            }
            return 0;
        }

        FirstChoiceCardSelector.LastSelections.Clear();
        Task<CombatState> build = ScenarioBuilder.BuildAsync(new OfflineCombatWaiter(loop), request);
        loop.RunUntilCompleted(build, TimeSpan.FromSeconds(600), $"建局 {root}");
        CombatState combat = build.GetAwaiter().GetResult();
        return CompareNativeCombat("d1", root, combat);
    }

    private static int ComparePrefixD1(MainLoopContext loop, OfflineRequest request, string root)
    {
        Task<CombatState> build = ScenarioBuilder.BuildAsync(new OfflineCombatWaiter(loop), request);
        loop.RunUntilCompleted(build, TimeSpan.FromSeconds(600), "建局 " + root);
        CombatState combat = build.GetAwaiter().GetResult();
        Player player = LocalContext.GetMe(combat)
            ?? throw new InvalidOperationException("找不到本地玩家。");
        NativeRunState native = combat.RunState as NativeRunState
            ?? throw new InvalidDataException("原生战斗的 RunState 不是具体 RunState。");
        Rules preRules = RulesCompiler.Compile(combat, player, native);
        Battle pre = BattleImporter.Import(combat, player, preRules, ImportOptions.Default);
        PrefixAction[] actions = PrefixPlan.Parse(request);
        PrefixReplayResult replay = PrefixReplay.Replay(pre, preRules, actions);
        Rules nativeAfter = RulesCompiler.CompileAfterPrefix(combat, player, native, replay.Battle);
        RunState run = RunStateImporter.Import(native, player);
        return CompareOne("d1", root, nativeAfter, replay.Battle, in run, EncounterOf(combat),
            includeRootPowerAmounts: true);
    }

    private static int CompareNatural(MainLoopContext loop, string characterName, int ascension,
        string seed)
    {
        CharacterModel character = ModelDb.AllCharacters.FirstOrDefault(
            candidate => candidate.GetType().Name == characterName)
            ?? throw new InvalidDataException($"未知角色 {characterName}。");
        OfflineCombatWaiter waiter = new(loop);
        Task<(NativeRunState Run, Player Player)> start = RunTestHost.StartNewRunAsync(
            waiter, character, ascension, seed);
        loop.RunUntilCompleted(start, TimeSpan.FromSeconds(120), "自然开局");
        (NativeRunState native, _) = start.GetAwaiter().GetResult();
        MapPoint first = native.Map.StartingMapPoint.Children
            .FirstOrDefault(static point => point.PointType == MapPointType.Monster)
            ?? throw new InvalidDataException("第一排没有战斗点。");
        Console.WriteLine($"BUILD-POINT kind=natural seed={seed} coord={first.coord.col},{first.coord.row}");
        Task enter = RunManager.Instance.EnterMapCoord(first.coord);
        loop.RunUntilCompleted(enter, TimeSpan.FromSeconds(120), "进入第一场战斗");
        Task<CombatState> playable = waiter.WaitForPlayableCombatAsync();
        loop.RunUntilCompleted(playable, TimeSpan.FromSeconds(120), "等待第一场战斗");
        return CompareNativeCombat("natural", seed, playable.GetAwaiter().GetResult());
    }

    private static int CompareEncounter(MainLoopContext loop, int encounterId, string seed)
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
            waiter, character, 0, seed);
        loop.RunUntilCompleted(start, TimeSpan.FromSeconds(120), "遭遇开局");
        (NativeRunState native, _) = start.GetAwaiter().GetResult();
        int actIndex = 0;
        for (int index = 0; index < native.Acts.Count; index++)
        {
            if (native.Acts[index].AllEncounters.Any(encounter => encounter.Id == canonical.Id))
            {
                actIndex = index;
                break;
            }
        }
        Task setAct = RunManager.Instance.SetActInternal(actIndex);
        loop.RunUntilCompleted(setAct, TimeSpan.FromSeconds(120), "设置测试幕");
        (RoomType roomType, MapPointType pointType) = canonical.RoomType switch
        {
            RoomType.Monster => (RoomType.Monster, MapPointType.Monster),
            RoomType.Elite => (RoomType.Elite, MapPointType.Elite),
            RoomType.Boss => (RoomType.Boss, MapPointType.Boss),
            _ => throw new InvalidDataException($"{nativeId} 的房间类型 {canonical.RoomType} 不是战斗。"),
        };
        Task<AbstractRoom> enter = RunManager.Instance.EnterRoomDebug(
            roomType, pointType, canonical.ToMutable());
        loop.RunUntilCompleted(enter, TimeSpan.FromSeconds(120), "进入遭遇");
        Task<CombatState> playable = waiter.WaitForPlayableCombatAsync();
        loop.RunUntilCompleted(playable, TimeSpan.FromSeconds(120), "等待遭遇战斗");
        return CompareNativeCombat("encounter", nativeId, playable.GetAwaiter().GetResult());
    }

    private static int CompareNativeCombat(string kind, string label, CombatState combat)
    {
        Player player = LocalContext.GetMe(combat)
            ?? throw new InvalidOperationException("找不到本地玩家。");
        NativeRunState native = combat.RunState as NativeRunState
            ?? throw new InvalidDataException("原生战斗的 RunState 不是具体 RunState。");
        Rules nativeRules;
        try
        {
            nativeRules = RulesCompiler.Compile(combat, player, native);
        }
        catch (UnsupportedImportException error)
        {
            Console.WriteLine($"BUILD kind={kind} label={label} status=reject reason={error.Reason}");
            return 0;
        }
        catch (UnsupportedException error)
        {
            Console.WriteLine($"BUILD kind={kind} label={label} status=reject reason=core-unsupported");
            Console.WriteLine($"  {error.Message}");
            return 0;
        }
        catch (CapacityException)
        {
            Console.WriteLine($"BUILD kind={kind} label={label} status=reject reason=capacity");
            return 0;
        }
        Battle battle = BattleImporter.Import(combat, player, nativeRules, ImportOptions.Default);
        RunState run = RunStateImporter.Import(native, player);
        ushort encounterId = EncounterOf(combat);
        return CompareOne(kind, label, nativeRules, battle, run, encounterId);
    }

    private static int CompareOne(string kind, string label, Rules nativeRules, in Battle battle,
        in RunState run, ushort encounterId, bool includeRootPowerAmounts = false)
    {
        try
        {
            PrintContent(nativeRules);
            RulesRootFacts facts = Facts(nativeRules, in battle, includeRootPowerAmounts);
            Rules built = RulesBuilder.Build(_table!, in run, in battle, encounterId, facts);
            byte[] expected = RulesCodec.Encode(nativeRules);
            byte[] actual = RulesCodec.Encode(built);
            if (expected.AsSpan().SequenceEqual(actual))
            {
                Console.WriteLine($"BUILD kind={kind} label={label} status=same");
                return 0;
            }
            IReadOnlyList<string> differences = RulesDiff.Compare(nativeRules, built);
            string table = differences.Count == 0
                ? "<bytes>"
                : differences[0].Split(' ')[0];
            Console.WriteLine($"BUILD kind={kind} label={label} status=diff table={table}");
            foreach (string difference in differences.Take(24))
                Console.WriteLine("  " + difference);
            return 0;
        }
        catch (ContentUnavailableException error)
        {
            Console.WriteLine(
                $"BUILD kind={kind} label={label} status=reject reason={error.Reason}");
            Console.WriteLine($"  {error.Detail}");
            return 0;
        }
        catch (UnsupportedImportException error)
        {
            Console.WriteLine(
                $"BUILD kind={kind} label={label} status=reject reason={error.Reason}");
            return 0;
        }
        catch (UnsupportedException error)
        {
            Console.WriteLine(
                $"BUILD kind={kind} label={label} status=reject reason=core-unsupported");
            Console.WriteLine($"  {error.Message}");
            return 0;
        }
        catch (CapacityException)
        {
            Console.WriteLine(
                $"BUILD kind={kind} label={label} status=reject reason=capacity");
            return 0;
        }
        catch (InvalidOperationException error)
        {
            Console.WriteLine(
                $"BUILD kind={kind} label={label} status=reject reason=rules-build");
            Console.WriteLine($"  {error.Message}");
            return 0;
        }
    }

    private static RulesRootFacts Facts(Rules rules, in Battle battle,
        bool includeRootPowerAmounts)
    {
        byte[] initial = new byte[battle.EnemyCount];
        int[][] conditional = new int[battle.EnemyCount][];
        MonsterAttack[]?[][] attacks = new MonsterAttack[]?[battle.EnemyCount][];
        int[]?[] rootPowers = new int[]?[battle.EnemyCount];
        bool[] moveLogged = new bool[battle.EnemyCount];
        for (int index = 0; index < battle.EnemyCount; index++)
        {
            MonsterRules row = rules.MonsterRule(battle.Enemies[index].Rules);
            initial[index] = checked((byte)row.InitialNode);
            if (includeRootPowerAmounts)
                rootPowers[index] = row.RootPowerAmounts;
            int[] targets = new int[row.Nodes.Length];
            Array.Fill(targets, RulesRootFacts.UnknownConditionalTarget);
            MonsterAttack[]?[] nodeAttacks = new MonsterAttack[]?[row.Nodes.Length];
            for (int node = 0; node < row.Nodes.Length; node++)
            {
                nodeAttacks[node] = row.Nodes[node].Attacks;
                if (row.Nodes[node].Kind == MonsterNodeKind.ConditionalBranch)
                    targets[node] = row.Nodes[node].ConditionalTarget;
            }
            conditional[index] = targets;
            attacks[index] = nodeAttacks;
            moveLogged[index] = row.InitialMoveLogged;
        }
        return new RulesRootFacts
        {
            EnemyInitialNodes = initial,
            EnemyConditionalTargets = conditional,
            EnemyNodeAttacks = attacks,
            EnemyRootPowerAmounts = rootPowers,
            EnemyInitialMoveLogged = moveLogged,
        };
    }

    private static ushort EncounterOf(in RunState run)
    {
        if (run.Rooms.Count == 0)
            return ContentTable.NoEncounter;
        RunRoom room = run.Rooms.Items[run.Rooms.Count - 1];
        return room.Model.Kind == RunRefKind.Encounter
            ? room.Model.Id
            : ContentTable.NoEncounter;
    }

    private static ushort EncounterOf(CombatState combat)
    {
        if (combat.Encounter is null)
            return ContentTable.NoEncounter;
        return RunCatalog.TryEncounter(combat.Encounter.Id.Entry, out ushort id)
            ? id
            : ContentTable.NoEncounter;
    }

    private static void PrintContent(Rules rules)
    {
        if (!_printContent)
            return;
        StringBuilder cards = new();
        for (int id = 0; id < rules.CardTable.Length; id++)
        {
            CardStats?[] levels = rules.CardTable[id];
            for (int upgrade = 0; upgrade < levels.Length; upgrade++)
            {
                if (levels[upgrade] == null)
                    continue;
                if (cards.Length > 0)
                    cards.Append(',');
                cards.Append(id).Append(':').Append(upgrade);
            }
        }
        StringBuilder monsters = new();
        for (int index = 0; index < rules.MonsterTable.Length; index++)
        {
            if (rules.MonsterTable[index] is not { } monster)
                continue;
            if (monsters.Length > 0)
                monsters.Append(',');
            monsters.Append(monster.MonsterId);
        }
        foreach (ushort spawn in rules.SpawnRuleTable.Keys)
        {
            if (monsters.Length > 0)
                monsters.Append(',');
            monsters.Append(spawn);
        }
        Console.WriteLine($"BUILD-CONTENT cards={cards} monsters={monsters}");
    }

    private static void AccumulateUsed(string line, string key, HashSet<string> used)
    {
        int start = line.IndexOf(key, StringComparison.Ordinal);
        if (start < 0)
            return;
        start += key.Length;
        int end = line.IndexOf(' ', start);
        ReadOnlySpan<char> text = (end < 0 ? line.AsSpan(start) : line.AsSpan(start, end - start)).Trim();
        while (text.Length > 0)
        {
            int comma = text.IndexOf(',');
            ReadOnlySpan<char> item = comma < 0 ? text : text[..comma];
            if (item.Length > 0)
                used.Add(item.ToString());
            if (comma < 0)
                break;
            text = text[(comma + 1)..];
        }
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

    private static string ValueOf(string line, string key)
    {
        int start = line.IndexOf(key, StringComparison.Ordinal);
        if (start < 0)
            return "<unknown>";
        start += key.Length;
        int end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }
}
