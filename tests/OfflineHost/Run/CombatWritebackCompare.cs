// --combat-writeback-compare: enter the same combat twice in one child process (native
// RunManager and Core RunCombatSetup), then drive a Blitz-selected route through both the kernel and
// the original game's public action entry points. Every action boundary imports the native Battle
// with the whole-combat options and compares it to the kernel prediction; after the terminal action
// the Core post-combat write-back is compared field by field with the native run at the same
// pre-reward endpoint.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using BlitzSolver.Ai.Combat;
using BlitzSolver.Autoplay;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;
using RunState = BlitzSolver.Simulation.Run.RunState;

namespace OfflineHost;

internal static class CombatWritebackCompare
{
    private static readonly string[] Characters =
        ["Ironclad", "Silent", "Regent", "Defect", "Necrobinder"];

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
        int steps = 400;
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
                case "--writeback-kind" when index + 1 < args.Length:
                    kind = args[++index];
                    break;
                case "--writeback-seeds" when index + 1 < args.Length:
                    seeds = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--writeback-jobs" when index + 1 < args.Length:
                    jobs = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--writeback-shard" when index + 2 < args.Length:
                    shard = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    shards = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--writeback-root" when index + 1 < args.Length:
                    root = args[++index];
                    break;
                case "--writeback-character" when index + 1 < args.Length:
                    character = args[++index];
                    break;
                case "--writeback-ascension" when index + 1 < args.Length:
                    ascension = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--writeback-seed" when index + 1 < args.Length:
                    seed = args[++index];
                    break;
                case "--writeback-encounter" when index + 1 < args.Length:
                    encounter = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--writeback-steps" when index + 1 < args.Length:
                    steps = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                default:
                    Console.Error.WriteLine($"未知参数：{args[index]}");
                    return 2;
            }
        }
        if (jobs is < 1 or > 8 || shards < 1 || shard < 0 || shard >= shards)
        {
            Console.Error.WriteLine("--writeback-jobs 必须在 1 到 8 之间，分片参数必须合法。");
            return 2;
        }
        if (root != null || character != null || encounter != null)
            return RunChild(data, corpora, root, character, ascension, seed, encounter, kind, steps);
        if (kind.Length == 0)
        {
            Console.Error.WriteLine(
                "需要 --writeback-kind natural|encounter|d1|pilot，或单例模式参数。");
            return 2;
        }
        return RunParent(data, corpora, kind, seeds, jobs, shard, shards, steps);
    }

    private static int RunChild(string data, string corpora, string? root, string? character,
        int ascension, string? seed, int? encounter, string kind, int steps)
    {
        MainLoopContext loop = new();
        SynchronizationContext.SetSynchronizationContext(loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();
        _table = ContentTableExporter.Export();
        if (root != null)
        {
            if (kind == "pilot")
                return ComparePilot(loop, data, corpora, root);
            return CompareD1(loop, data, corpora, root, seed ?? "", steps);
        }
        if (character != null)
            return CompareNatural(loop, character, ascension, seed ?? "", steps);
        if (encounter is { } encounterId)
            return CompareEncounter(loop, encounterId, ascension,
                seed ?? $"R2A3-ENC-{ascension}-{encounterId:D3}", steps);
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
                        string seedText = $"R2A3-{character}-A{ascension}-{seedIndex:D3}";
                        cases.Add(("natural",
                            ["--writeback-character", character, "--writeback-ascension",
                                ascension.ToString(CultureInfo.InvariantCulture),
                                "--writeback-seed", seedText],
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
                    for (int seedIndex = 0; seedIndex < seeds; seedIndex++)
                    {
                        string seedText = $"R2A3-ENC-{ascension}-{id:D3}-{seedIndex:D2}";
                        cases.Add(("encounter",
                            ["--writeback-encounter", id.ToString(CultureInfo.InvariantCulture),
                                "--writeback-ascension",
                                ascension.ToString(CultureInfo.InvariantCulture),
                                "--writeback-seed", seedText],
                            $"encounter/A{ascension}/{RunCatalog.EncounterName((ushort)id)}/{seedText}"));
                    }
                }
            }
        }
        if (kind is "all" or "d1")
        {
            foreach (string root in EnumerateRoots(data))
            {
                cases.Add(("d1", ["--writeback-root", root], $"d1/{root}"));
            }
        }
        if (kind is "all" or "pilot")
        {
            foreach (string root in EnumeratePilotRoots(data, corpora))
                cases.Add(("pilot", ["--writeback-root", root], $"pilot/{root}"));
        }
        if (shards > 1)
            cases = cases.Where((_, index) => index % shards == shard).ToList();
        if (cases.Count == 0)
        {
            Console.Error.WriteLine($"--writeback-kind {kind} 没有用例。");
            return 2;
        }

        object gate = new();
        int same = 0, diff = 0, reject = 0, crash = 0;
        int victories = 0, defeats = 0, otherOutcomes = 0;
        int actions = 0;
        Dictionary<string, int> rejects = new(StringComparer.Ordinal);
        Dictionary<string, int> actionCoverage = new(StringComparer.Ordinal);
        Dictionary<string, int> effectCoverage = new(StringComparer.Ordinal);
        Console.WriteLine($"WRITEBACK cases={cases.Count} kind={kind} jobs={jobs} shard={shard}/{shards}");
        Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = jobs }, item =>
        {
            (int code, string stdout, string stderr) = RunChildProcess(data, corpora,
                ["--writeback-kind", item.Kind, .. item.Args, "--writeback-steps",
                    steps.ToString(CultureInfo.InvariantCulture)]);
            List<string> lines = stdout.Split('\n')
                .Where(static line => line.StartsWith("WRITEBACK ", StringComparison.Ordinal)
                    || line.StartsWith("WRITEBACK-COVER ", StringComparison.Ordinal)
                    || line.StartsWith("  ", StringComparison.Ordinal))
                .ToList();
            lock (gate)
            {
                string? status = null;
                foreach (string line in lines)
                {
                    if (line.StartsWith("WRITEBACK ", StringComparison.Ordinal))
                    {
                        status = line;
                        break;
                    }
                }
                if (status == null)
                {
                    crash++;
                    Console.WriteLine($"WRITEBACK-CRASH kind={item.Kind} label={item.Label} code={code}");
                    string first = stderr.Split('\n').FirstOrDefault(static line => line.Length != 0)
                        ?? "no stderr";
                    Console.WriteLine("  " + first);
                    return;
                }
                if (status.Contains(" status=same", StringComparison.Ordinal))
                    same++;
                else if (status.Contains(" status=reject", StringComparison.Ordinal))
                {
                    reject++;
                    string reason = Field(status, "reason", "?");
                    rejects[reason] = rejects.GetValueOrDefault(reason) + 1;
                    Console.WriteLine($"WRITEBACK-REJECT-CASE {item.Label} {reason}");
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
                }
                string outcome = Field(status, "outcome", "-");
                if (outcome is "victory")
                    victories++;
                else if (outcome is "defeat")
                    defeats++;
                else if (outcome is not ("reject" or "diff" or "executor-diff"))
                    otherOutcomes++;
                actions += int.TryParse(Field(status, "actions", "0"), out int count) ? count : 0;
                foreach (string line in lines)
                {
                    if (!line.StartsWith("WRITEBACK-COVER ", StringComparison.Ordinal))
                        continue;
                    Accumulate(line, "actions=", actionCoverage);
                    Accumulate(line, "effects=", effectCoverage);
                }
            }
        });
        Console.WriteLine(
            $"WRITEBACK-SUMMARY kind={kind} cases={cases.Count} same={same} diff={diff} " +
            $"reject={reject} crash={crash} victory={victories} defeat={defeats} " +
            $"other={otherOutcomes} actions={actions}");
        foreach ((string reason, int count) in rejects.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"WRITEBACK-REJECT {reason}={count}");
        foreach ((string action, int count) in actionCoverage.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"WRITEBACK-ACTION {action}={count}");
        foreach ((string effect, int count) in effectCoverage.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"WRITEBACK-EFFECT {effect}={count}");
        return diff == 0 && crash == 0 ? 0 : 1;
    }

    private static void Accumulate(string line, string field, Dictionary<string, int> counts)
    {
        int start = line.IndexOf(field, StringComparison.Ordinal);
        if (start < 0)
            return;
        start += field.Length;
        int end = line.IndexOf(' ', start);
        string value = end < 0 ? line[start..] : line[start..end];
        if (value.Length == 0)
            return;
        foreach (string entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = entry.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(entry[(colon + 1)..], out int count))
                continue;
            string key = entry[..colon];
            counts[key] = counts.GetValueOrDefault(key) + count;
        }
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

    private static int CompareNatural(MainLoopContext loop, string characterName, int ascension,
        string seed, int steps)
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
        return CompareEntry(loop, waiter, native, player, first.coord, "natural", seed, null, steps);
    }

    private static int CompareEncounter(MainLoopContext loop, int encounterId, int ascension,
        string seed, int steps)
    {
        if ((uint)encounterId >= (uint)RunCatalog.EncounterCount)
        {
            Console.Error.WriteLine($"遭遇 id 越界：{encounterId}");
            return 2;
        }
        string nativeId = RunCatalog.EncounterName((ushort)encounterId);
        if (Registry.Encounter((ushort)encounterId).EndsByEventScript)
        {
            Console.WriteLine(
                $"WRITEBACK kind=encounter label={nativeId} status=reject reason=event-script-end " +
                $"outcome=reject actions=0 encounter={nativeId}");
            Console.WriteLine("  原版事件脚本在玩家行动前结束房间；该遭遇没有可执行的战斗终点。");
            return 0;
        }
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
        return CompareEntry(loop, waiter, native, player, point.coord, "encounter", nativeId,
            nativeId, steps);
    }

    private static int CompareD1(MainLoopContext loop, string data, string corpora, string root,
        string seedOverride, int steps)
    {
        OfflineRequest request = Program.LoadRequest(data, corpora, root);
        if (request.Relics.Any(static relic => relic.IntegerMembers.Count != 0
            || relic.BooleanMembers.Count != 0))
        {
            Console.WriteLine(
                $"WRITEBACK kind=d1 label={root} status=reject reason=unsupported-relic-state outcome=reject actions=0");
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
        if (!RunCatalog.TryEncounter(prepared.Encounter.Id.Entry, out _))
            throw new InvalidDataException($"D1 遭遇不在目录：{prepared.Encounter.Id.Entry}");
        EncounterModel target = prepared.Encounter;
        int actIndex = prepared.Run.CurrentActIndex;
        ForceNextEncounter(prepared.Run, target, actIndex);
        MapPoint point = FindPoint(prepared.Run, target.RoomType);
        return CompareEntry(loop, waiter, prepared.Run, prepared.Player, point.coord, "d1",
            root + (seedOverride.Length == 0 ? "" : "#" + seedOverride), target.Id.Entry, steps);
    }

    private static int CompareEntry(MainLoopContext loop, OfflineCombatWaiter waiter,
        NativeRunState native, Player player, MapCoord mapCoord, string kind, string label,
        string? encounterName, int steps)
    {
        RunCoord coord = new((byte)mapCoord.col, (byte)mapCoord.row);
        try
        {
            RunState before = RunStateImporter.Import(native, player);
            CombatSetupResult setup = CombatSetupChoices.ResolveFirst(before, _table!, coord,
                emptyGambling: true, out _, out _);
            CombatState combat;
            Player combatPlayer;
            NativeRunState after;
            Rules nativeRules;
            Battle opening;
            LiveCombatTracker tracker;
            using (CardSelectCmd.PushSelector(new FirstChoiceCombatSetupSelector()))
            {
                Task enter = RunManager.Instance.EnterMapCoord(mapCoord);
                loop.RunUntilCompleted(enter, TimeSpan.FromSeconds(120), "原生进点");
                Task<CombatState> playable = waiter.WaitForPlayableCombatAsync();
                loop.RunUntilCompleted(playable, TimeSpan.FromSeconds(120), "原生等待可出牌");
                combat = playable.GetAwaiter().GetResult();
                combatPlayer = LocalContext.GetMe(combat)
                    ?? throw new InvalidDataException("原生战斗找不到玩家。");
                after = combat.RunState as NativeRunState
                    ?? throw new InvalidDataException("原生战斗的 RunState 类型不对。");
                nativeRules = RulesCompiler.Compile(combat, combatPlayer, after);
                tracker = new LiveCombatTracker(combat);
                opening = tracker.ImportRoot(combat, combatPlayer, nativeRules);
            }
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
            NativeActionExecutor executor = new(loop, combat, combatPlayer, nativeRules, tracker);
            Random random = new(unchecked((int)SeedFor(label)));
            CombatActionCoverage coverage = new();
            Battle current = setup.Battle;
            int actionIndex = 0;
            for (; actionIndex < steps && current.Outcome == Outcome.None; actionIndex++)
            {
                ReplayedAction? planned = RandomCombatPolicy.Choose(current, setup.Rules, random,
                    coverage);
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
                    Console.WriteLine(
                        $"WRITEBACK kind={kind} label={label} status=reject reason={error.Reason} " +
                        $"outcome=executor actions={actionIndex} encounter={encounterName ?? "-"}");
                    Console.WriteLine("  " + error.Message);
                    return 0;
                }
                if (!BattleReconciler.Same(planned.After, live))
                {
                    return Diff(kind, label, actionIndex, planned.Action.ToString(),
                        [.. BattleReconciler.Diff(planned.After, live)], encounterName);
                }
                tracker.Observe(live);
                current = planned.After;
            }
            if (current.Outcome == Outcome.None)
            {
                Console.WriteLine(
                    $"WRITEBACK kind={kind} label={label} status=reject reason=action-limit " +
                    $"outcome=in_progress actions={actionIndex} turns={current.PlayerTurn} " +
                    $"encounter={encounterName ?? "-"}");
                WriteCoverage(coverage);
                return 0;
            }
            return CompareFinalRun(kind, label, current, setup, encounterName, coverage,
                actionIndex);
        }
        catch (ContentUnavailableException error)
        {
            Console.WriteLine(
                $"WRITEBACK kind={kind} label={label} status=reject reason={error.Reason} outcome=reject " +
                $"actions=0 encounter={encounterName ?? "-"}");
            Console.WriteLine("  " + error.Detail);
            return 0;
        }
        catch (UnsupportedImportException error)
        {
            Console.WriteLine(
                $"WRITEBACK kind={kind} label={label} status=reject reason={error.Reason} outcome=reject " +
                $"actions=0 encounter={encounterName ?? "-"}");
            Console.WriteLine("  " + error.Detail);
            return 0;
        }
        catch (BlitzSolver.Simulation.Combat.UnsupportedException error)
        {
            Console.WriteLine(
                $"WRITEBACK kind={kind} label={label} status=reject reason=core-unsupported " +
                $"outcome=reject actions=0 encounter={encounterName ?? "-"}");
            Console.WriteLine("  " + error.Message);
            return 0;
        }
        catch (CapacityException error)
        {
            Console.WriteLine(
                $"WRITEBACK kind={kind} label={label} status=reject reason=capacity outcome=reject " +
                $"actions=0 encounter={encounterName ?? "-"}");
            Console.WriteLine("  " + error.Message);
            return 0;
        }
    }

    private static int CompareFinalRun(string kind, string label, Battle current,
        CombatSetupResult setup, string? encounterName, CombatActionCoverage coverage,
        int actionIndex, string? terminalRun = null, bool? terminalDead = null)
    {
        CombatState? liveState = CombatManager.Instance.DebugOnlyGetState();
        Player? finalPlayer = liveState == null ? null : LocalContext.GetMe(liveState);
        string expected;
        bool nativeDead;
        int nativeHp;
        if (terminalRun != null)
        {
            expected = terminalRun;
            nativeDead = terminalDead ?? current.Player.IsDead;
            nativeHp = current.Player.CurrentHp;
        }
        else
        {
            if (liveState == null || finalPlayer == null)
            {
                Console.WriteLine(
                    $"WRITEBACK kind={kind} label={label} status=reject reason=live-missing " +
                    $"outcome=unknown actions={actionIndex} encounter={encounterName ?? "-"}");
                return 0;
            }
            NativeRunState finalRun = liveState.RunState as NativeRunState
                ?? throw new InvalidDataException("战斗结束的 RunState 类型不对。");
            expected = NativeRunDump.Capture(finalRun, finalPlayer);
            nativeDead = finalPlayer.Creature.IsDead;
            nativeHp = finalPlayer.Creature.CurrentHp;
        }
        if (nativeDead != current.Player.IsDead)
        {
            Console.WriteLine(
                $"WRITEBACK kind={kind} label={label} status=executor-diff action={actionIndex} " +
                $"outcome={OutcomeName(current.Outcome)} actions={actionIndex} " +
                $"turns={current.PlayerTurn} encounter={encounterName ?? "-"}");
            Console.WriteLine(
                $"  TERMINAL kernel_dead={current.Player.IsDead} native_dead={nativeDead} " +
                $"kernel_hp={current.Player.CurrentHp} native_hp={nativeHp}");
            WriteCoverage(coverage);
            return 0;
        }
        RunState written;
        try
        {
            written = RunCombatWriteback.Apply(setup.Run, current, _table!);
        }
        catch (ContentUnavailableException error)
        {
            Console.WriteLine(
                $"WRITEBACK kind={kind} label={label} status=reject reason={error.Reason} " +
                $"outcome={OutcomeName(current.Outcome)} actions={actionIndex} " +
                $"encounter={encounterName ?? "-"}");
            Console.WriteLine("  " + error.Detail);
            return 0;
        }
        string actual = RunDump.Capture(written);
        string? runDiff = FirstDifference(expected, actual);
        string outcome = OutcomeName(current.Outcome);
        if (runDiff == null)
        {
            Console.WriteLine(
                $"WRITEBACK kind={kind} label={label} status=same outcome={outcome} " +
                $"actions={actionIndex} turns={current.PlayerTurn} " +
                $"encounter={encounterName ?? "-"}");
            WriteCoverage(coverage);
            return 0;
        }
        Console.WriteLine(
            $"WRITEBACK kind={kind} label={label} status=diff action={actionIndex} " +
            $"outcome={outcome} actions={actionIndex} turns={current.PlayerTurn} " +
            $"encounter={encounterName ?? "-"}");
        Console.WriteLine("  RUN  " + runDiff);
        WriteCoverage(coverage);
        return 0;
    }

    private static int ComparePilot(MainLoopContext loop, string data, string corpora, string root)
    {
        OfflineRequest request = Program.LoadRequest(data, corpora, root);
        if (request.Relics.Any(static relic => relic.IntegerMembers.Count != 0
            || relic.BooleanMembers.Count != 0))
        {
            Console.WriteLine(
                $"WRITEBACK kind=pilot label={root} status=reject reason=unsupported-relic-state " +
                "outcome=reject actions=0");
            return 0;
        }
        GeneratedScenarioSetup setup = GeneratedScenarioSetup.Prepare(request);
        OfflineCombatWaiter waiter = new(loop);
        Task<ScenarioBuilder.PreparedRun> prepare = ScenarioBuilder.PrepareRunAsync(
            waiter, setup.Request, preparedSetup: setup);
        loop.RunUntilCompleted(prepare, TimeSpan.FromSeconds(120), "pilot 建局");
        using ScenarioBuilder.PreparedRun prepared = prepare.GetAwaiter().GetResult();
        EncounterModel target = prepared.Encounter;
        int actIndex = prepared.Run.CurrentActIndex;
        ForceNextEncounter(prepared.Run, target, actIndex);
        MapPoint point = FindPoint(prepared.Run, target.RoomType);
        RunCoord coord = new((byte)point.coord.col, (byte)point.coord.row);
        try
        {
            RunState before = RunStateImporter.Import(prepared.Run, prepared.Player);
            CombatSetupResult setupEntry = CombatSetupChoices.ResolveFirst(before, _table!, coord,
                emptyGambling: true, out _, out _);
            CombatState combat;
            Player combatPlayer;
            NativeRunState after;
            Rules nativeRules;
            Battle opening;
            LiveCombatTracker tracker;
            using (CardSelectCmd.PushSelector(new FirstChoiceCombatSetupSelector()))
            {
                Task enter = RunManager.Instance.EnterMapCoord(point.coord);
                loop.RunUntilCompleted(enter, TimeSpan.FromSeconds(120), "pilot 原生进点");
                combat = waiter.WaitForPlayableCombatAsync()
                    .GetAwaiter().GetResult();
                combatPlayer = LocalContext.GetMe(combat)
                    ?? throw new InvalidDataException("原生战斗找不到玩家。");
                after = combat.RunState as NativeRunState
                    ?? throw new InvalidDataException("原生战斗的 RunState 类型不对。");
                nativeRules = RulesCompiler.Compile(combat, combatPlayer, after);
                tracker = new LiveCombatTracker(combat);
                opening = tracker.ImportRoot(combat, combatPlayer, nativeRules);
            }
            List<string> setupDiff = BattleDump.Diff(opening, setupEntry.Battle, "native", "blitz");
            if (setupDiff.Count != 0)
                return Diff("pilot", root, -1, "setup", setupDiff, target.Id.Entry);
            tracker.Observe(opening);
            SearchPolicy policy = SearchPolicyCapture.Capture(combat, combatPlayer);
            NativePilotHost host = new(loop, combat, combatPlayer, nativeRules, tracker, opening,
                policy, SearchPerformanceProfile.For(SearchPerformanceTier.Low), 0);
            CombatPilotResult result = CombatPilot.RunAsync(host, root, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (result.DeploymentFailures != 0 || result.Deviations != 0
                || result.Status is not ("victory" or "defeat" or "ended"))
            {
                string reason = result.Deviations != 0 ? "boundary_diff"
                    : result.FailureLog.Length != 0 ? result.FailureLog[0].Reason
                    : "pilot_" + result.Status;
                string detail = result.Deviations != 0
                    ? result.DeviationLog[0].Action + ": " + string.Join(" | ", result.DeviationLog[0].Fields)
                    : result.FailureLog.Length != 0 ? result.FailureLog[0].Detail
                    : result.PauseReason ?? "";
                string status = result.Status == "rejected" ? "reject" : "executor-diff";
                Console.WriteLine(
                    $"WRITEBACK kind=pilot label={root} status={status} reason={reason} " +
                    $"outcome={result.Outcome} actions={result.Turns} encounter={target.Id.Entry}");
                Console.WriteLine("  " + detail);
                WriteCoverage(host.Coverage);
                return 0;
            }
            return CompareFinalRun("pilot", root, host.Current, setupEntry, target.Id.Entry,
                host.Coverage, result.Turns, host.TerminalRun, host.TerminalDead);
        }
        catch (ContentUnavailableException error)
        {
            Console.WriteLine(
                $"WRITEBACK kind=pilot label={root} status=reject reason={error.Reason} " +
                "outcome=reject actions=0");
            Console.WriteLine("  " + error.Detail);
            return 0;
        }
        catch (UnsupportedImportException error)
        {
            Console.WriteLine(
                $"WRITEBACK kind=pilot label={root} status=reject reason={error.Reason} " +
                "outcome=reject actions=0");
            Console.WriteLine("  " + error.Detail);
            return 0;
        }
        catch (BlitzSolver.Simulation.Combat.UnsupportedException error)
        {
            Console.WriteLine(
                $"WRITEBACK kind=pilot label={root} status=reject reason=core-unsupported " +
                "outcome=reject actions=0");
            Console.WriteLine("  " + error.Message);
            return 0;
        }
        catch (CapacityException error)
        {
            Console.WriteLine(
                $"WRITEBACK kind=pilot label={root} status=reject reason=capacity " +
                "outcome=reject actions=0");
            Console.WriteLine("  " + error.Message);
            return 0;
        }
    }

    private static int Diff(string kind, string label, int actionIndex, string action,
        List<string> diff, string? encounterName)
    {
        Console.WriteLine(
            $"WRITEBACK kind={kind} label={label} status=executor-diff action={actionIndex} " +
            $"outcome=diff actions={actionIndex} encounter={encounterName ?? "-"}");
        Console.WriteLine("  ACTION " + action);
        foreach (string line in diff.Take(16))
            Console.WriteLine("  BATL " + line);
        return 0;
    }

    private static void WriteCoverage(CombatActionCoverage coverage)
    {
        Console.WriteLine("WRITEBACK-COVER actions=" + string.Join(',',
            coverage.Actions.Select(static pair => $"{pair.Key}:{pair.Count}"))
            + " effects=" + string.Join(',',
                coverage.Effects.Select(static pair => $"{pair.Key}:{pair.Count}")));
    }

    private static string OutcomeName(Outcome outcome) => outcome switch
    {
        Outcome.Victory => "victory",
        Outcome.Defeat => "defeat",
        Outcome.EventDefeat => "event-defeat",
        _ => "none",
    };

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

    private static IEnumerable<string> EnumerateRoots(string data)
    {
        string d1 = Path.Combine(data, "d1");
        return Directory.EnumerateDirectories(d1)
            .Select(Path.GetFileName)
            .Where(static name => name != null)
            .OrderBy(static name => name, StringComparer.Ordinal)!;
    }

    private static IEnumerable<string> EnumeratePilotRoots(string data, string corpora)
    {
        string quality = Path.Combine(corpora, "quality");
        if (!Directory.Exists(quality))
            yield break;
        foreach (string name in Directory.EnumerateDirectories(quality)
                     .Where(directory => File.Exists(Path.Combine(directory, "spec.json")))
                     .Select(directory => "quality-" + Path.GetFileName(directory))
                     .Where(name => File.Exists(Path.Combine(data, "d1", name, "meta.json")))
                     .OrderBy(static name => name, StringComparer.Ordinal))
        {
            yield return name;
        }
    }

    private static (int Code, string Stdout, string Stderr) RunChildProcess(string data,
        string corpora, string[] caseArgs)
    {
        string assembly = typeof(CombatWritebackCompare).Assembly.Location;
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--combat-writeback-compare");
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
