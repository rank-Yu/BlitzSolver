// --shop-compare: enter a merchant from a natural map run or a D1 loadout, compare the generated
// shelf, every legal buy/removal/leave action and the return to the map. Native actions use the
// public merchant entries and the test card selector; Blitz actions use RunKernel.Step. Every
// sequence starts from the same pre-entry SerializableRun snapshot and re-enters the shop.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Simulation.Run;
using BlitzSolver.TestHost;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Unlocks;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;
using RunState = BlitzSolver.Simulation.Run.RunState;

namespace OfflineHost;

internal static class ShopCompare
{
    private const int MaxJobs = 8;
    private const int NaturalGold = 500;
    private const int D1Gold = 2000;
    private const int GreedyGold = 5000;
    private const int GreedyMaxPurchases = 16;

    private static readonly string[] Characters =
        ["Ironclad", "Silent", "Regent", "Defect", "Necrobinder"];

    private static readonly string[] Presets = ["empty", "all"];

    private static ContentTable? _table;
    private static RunKernel? _kernel;
    private static MainLoopContext? _loop;
    private static bool _livePrices;
    private static bool _forceCourier;
    private static int _livePriceCalcCosts;

    private static bool _skipRoomEntry;
    private static bool _forceShopPoint;
    private static RunCoord _shopCoord;

    public static int Run(string[] args)
    {
        string data = "tests/Golden/data";
        string corpora = "tests/corpora";
        string kind = "";
        int seeds = 5;
        int jobs = 4;
        int shard = 0;
        int shards = 1;
        string? root = null;
        string? character = null;
        int ascension = 0;
        string? seed = null;
        string? preset = null;
        string? d1Seed = null;
        bool livePrices = false;
        bool forceCourier = false;
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
                case "--shop-kind" when index + 1 < args.Length:
                    kind = args[++index];
                    break;
                case "--shop-seeds" when index + 1 < args.Length:
                    seeds = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--shop-jobs" when index + 1 < args.Length:
                    jobs = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--shop-shard" when index + 2 < args.Length:
                    shard = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    shards = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--shop-root" when index + 1 < args.Length:
                    root = args[++index];
                    break;
                case "--shop-character" when index + 1 < args.Length:
                    character = args[++index];
                    break;
                case "--shop-ascension" when index + 1 < args.Length:
                    ascension = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--shop-seed" when index + 1 < args.Length:
                    seed = args[++index];
                    break;
                case "--shop-preset" when index + 1 < args.Length:
                    preset = args[++index];
                    break;
                case "--shop-d1-seed" when index + 1 < args.Length:
                    d1Seed = args[++index];
                    break;
                case "--shop-live-prices":
                    livePrices = true;
                    break;
                case "--shop-force-courier":
                    forceCourier = true;
                    break;
                default:
                    Console.Error.WriteLine($"未知参数：{args[index]}");
                    return 2;
            }
        }
        if (kind.EndsWith("-live-prices", StringComparison.Ordinal))
        {
            kind = kind[..^"-live-prices".Length];
            livePrices = true;
        }
        _livePrices = livePrices;
        _forceCourier = forceCourier;
        if (kind.StartsWith("natural-courier", StringComparison.Ordinal))
        {
            kind = "natural";
            _forceCourier = true;
        }
        if (jobs is < 1 or > MaxJobs || shards < 1 || shard < 0 || shard >= shards)
        {
            Console.Error.WriteLine("--shop-jobs 必须在 1 到 8 之间，分片参数必须合法。");
            return 2;
        }
        Initialize();
        if (root != null)
            return RunD1Case(data, corpora, root, d1Seed);
        if (character != null)
            return RunNaturalCase(character, ascension, seed ?? "", preset ?? "all");
        if (kind.Length == 0)
        {
            Console.Error.WriteLine("需要 --shop-kind natural|d1|all，或单例参数。");
            return 2;
        }
        return RunParent(data, corpora, kind, seeds, jobs, shard, shards);
    }

    private static void Initialize()
    {
        _loop = new MainLoopContext();
        SynchronizationContext.SetSynchronizationContext(_loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();
        _table = ContentTableExporter.Export(forcePotionPriceVaries: _livePrices);
        _kernel = new RunKernel(_table);
        InstallSavePatch();
        if (_livePrices)
            InstallLivePricePatch();
        MethodInfo enterRoom = AccessTools.Method(typeof(RunManager), "EnterRoomInternal")
            ?? throw new MissingMethodException(typeof(RunManager).FullName, "EnterRoomInternal");
        GameBootstrap.Harmony.Patch(enterRoom, prefix: new HarmonyMethod(
            typeof(ShopCompare).GetMethod(nameof(SkipRoomEntryPrefix),
                BindingFlags.Static | BindingFlags.NonPublic)!));
    }

    // ------------------------------------------------------------------ parent / child cases

    private static int RunParent(string data, string corpora, string kind, int seeds, int jobs,
        int shard, int shards)
    {
        List<string[]> cases = [];
        int caseIndex = 0;
        if (kind is "natural" or "all")
        {
            foreach (string character in Characters)
            {
                foreach (int ascension in new[] { 0, 10 })
                {
                    foreach (string preset in Presets)
                    {
                        for (int seedIndex = 0; seedIndex < seeds; seedIndex++)
                        {
                            string seed = $"shop-{character}-{ascension}-{preset}-{seedIndex}";
                            if (caseIndex++ % shards == shard)
                            {
                                string[] caseArgs = ["--shop-character", character,
                                    "--shop-ascension", ascension.ToString(CultureInfo.InvariantCulture),
                                    "--shop-seed", seed, "--shop-preset", preset];
                                if (_livePrices)
                                    caseArgs = [.. caseArgs, "--shop-live-prices"];
                                if (_forceCourier)
                                    caseArgs = [.. caseArgs, "--shop-force-courier"];
                                cases.Add(caseArgs);
                            }
                        }
                    }
                }
            }
        }
        if (kind is "d1" or "all")
        {
            foreach (string root in EnumerateRoots(data))
            {
                if (caseIndex++ % shards == shard)
                {
                    string[] caseArgs = ["--shop-root", root];
                    if (_livePrices)
                        caseArgs = [.. caseArgs, "--shop-live-prices"];
                    cases.Add(caseArgs);
                }
            }
        }
        return RunChildren(data, corpora, cases, jobs);
    }

    private static int RunChildren(string data, string corpora, List<string[]> cases, int jobs)
    {
        int running = 0;
        int same = 0;
        int diff = 0;
        int reject = 0;
        int skip = 0;
        int skips = 0;
        Dictionary<string, int> skipReasons = new(StringComparer.Ordinal);
        long compares = 0;
        long optionElements = 0;
        long steps = 0;
        Dictionary<string, int> rejects = new(StringComparer.Ordinal);
        Dictionary<string, long> actions = new(StringComparer.Ordinal);
        long livePriceCalls = 0;
        long greedyPurchases = 0;
        int courierCases = 0;
        Queue<string[]> queue = new(cases);
        List<(Process Process, Task<string> Out, Task<string> Err, string[] Args)> active = [];
        while (queue.Count != 0 || active.Count != 0)
        {
            while (running < jobs && queue.Count != 0)
            {
                string[] caseArgs = queue.Dequeue();
                ProcessStartInfo start = new("dotnet") { RedirectStandardOutput = true,
                    RedirectStandardError = true, UseShellExecute = false };
                start.ArgumentList.Add(typeof(ShopCompare).Assembly.Location);
                start.ArgumentList.Add("--shop-compare");
                start.ArgumentList.Add("--data");
                start.ArgumentList.Add(data);
                start.ArgumentList.Add("--corpora");
                start.ArgumentList.Add(corpora);
                foreach (string arg in caseArgs)
                    start.ArgumentList.Add(arg);
                Process process = Process.Start(start)
                    ?? throw new InvalidOperationException("无法启动对照子进程。");
                active.Add((process, process.StandardOutput.ReadToEndAsync(),
                    process.StandardError.ReadToEndAsync(), caseArgs));
                running++;
            }
            (Process Process, Task<string> Out, Task<string> Err, string[] Args) done = active[0];
            active.RemoveAt(0);
            done.Process.WaitForExit();
            running--;
            string text = done.Out.GetAwaiter().GetResult();
            string error = done.Err.GetAwaiter().GetResult();
            if (done.Process.ExitCode != 0)
            {
                Console.WriteLine($"SHOP-ERROR exit={done.Process.ExitCode} args={string.Join(' ', done.Args)}");
                if (error.Length != 0)
                    Console.WriteLine(error);
                diff++;
                continue;
            }
            foreach (string line in text.Split('\n'))
            {
                if (!line.StartsWith("SHOP-CASE ", StringComparison.Ordinal))
                {
                    if (line.StartsWith("SHOP-DIFF ", StringComparison.Ordinal)
                        || line.StartsWith("SHOP-REJECT-DETAIL ", StringComparison.Ordinal))
                    {
                        Console.WriteLine(line);
                    }
                    if (line.StartsWith("SHOP-LIVE-PRICES calls=", StringComparison.Ordinal))
                        livePriceCalls += ParseLong(line["SHOP-LIVE-PRICES calls=".Length..]);
                    if (line.StartsWith("SHOP-SKIP ", StringComparison.Ordinal))
                    {
                        string reason = Field(line, "reason");
                        skipReasons[reason] = skipReasons.GetValueOrDefault(reason) +
                            (int)ParseLong(Field(line, "count"));
                    }
                    continue;
                }
                string status = Field(line, "status");
                switch (status)
                {
                    case "same": same++; break;
                    case "diff": diff++; break;
                    case "reject":
                        reject++;
                        string reason = Field(line, "reason");
                        rejects[reason] = rejects.GetValueOrDefault(reason) + 1;
                        break;
                    case "skip": skip++; break;
                }
                compares += ParseLong(Field(line, "compares"));
                skips += (int)ParseLong(Field(line, "skips"));
                greedyPurchases += ParseLong(Field(line, "greedy"));
                if (Field(line, "courier") == "1")
                    courierCases++;
                optionElements += ParseLong(Field(line, "options"));
                steps += ParseLong(Field(line, "steps"));
                foreach (string action in (Field(line, "actions") ?? "").Split(','))
                {
                    int split = action.IndexOfAny(['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']);
                    if (split <= 0)
                        continue;
                    string key = action[..split];
                    actions[key] = actions.GetValueOrDefault(key) + ParseLong(action[split..]);
                }
                if (status == "diff")
                    Console.WriteLine(line);
            }
        }
        Console.WriteLine(
            $"SHOP-SUMMARY cases={cases.Count} same={same} diff={diff} reject={reject} skip={skip} " +
            $"skips={skips} compares={compares} optionElements={optionElements} steps={steps}");
        foreach ((string reason, int count) in rejects.OrderByDescending(static pair => pair.Value))
            Console.WriteLine($"SHOP-REJECT reason={reason} count={count}");
        foreach ((string reason, int count) in skipReasons.OrderByDescending(static pair => pair.Value))
            Console.WriteLine($"SHOP-SKIP reason={reason} count={count}");
        Console.WriteLine("SHOP-ACTIONS " + string.Join(' ',
            actions.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"{pair.Key}={pair.Value}")));
        if (livePriceCalls != 0)
            Console.WriteLine($"SHOP-LIVE-PRICES total={livePriceCalls}");
        Console.WriteLine($"SHOP-GREEDY purchases={greedyPurchases} courierCases={courierCases}");
        return diff == 0 ? 0 : 1;
    }

    private static string Field(string line, string name)
    {
        foreach (string part in line.Split(' '))
        {
            if (part.StartsWith("reason=", StringComparison.Ordinal))
            {
                if (name == "reason")
                    return part["reason=".Length..];
            }
            else if (part.StartsWith(name + "=", StringComparison.Ordinal))
            {
                return part[(name.Length + 1)..];
            }
        }
        return name == "reason" ? "unknown" : "0";
    }

    private static long ParseLong(string text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value : 0;

    // ------------------------------------------------------------------ natural case

    private static int RunNaturalCase(string characterName, int ascension, string seed,
        string presetName)
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
            _loop.RunUntilCompleted(RunManager.Instance.EnterMapCoord(
                native.Map.StartingMapPoint.coord), TimeSpan.FromSeconds(120), "Neow 起点");
            _skipRoomEntry = false;
            _loop.RunUntilCompleted(RunManager.Instance.EnterRoom(new MapRoom()),
                TimeSpan.FromSeconds(120), "Neow 后地图");
            native = RunManager.Instance.State!;
            player = LocalContext.GetMe(native)!;
        }

        if (_forceCourier
            && !player.Relics.Any(static relic => relic.Id.Entry == "THE_COURIER"))
        {
            RelicModel courier = ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.TheCourier>()
                .ToMutable();
            Task obtain = RelicCmd.Obtain(courier, player);
            _loop.RunUntilCompleted(obtain, TimeSpan.FromSeconds(60), "注入 TheCourier");
            native = RunManager.Instance.State!;
            player = LocalContext.GetMe(native)!;
        }
        MapCoord? shopCoord = FindReachableShop(native);
        if (shopCoord is null)
        {
            Console.WriteLine(
                $"SHOP-CASE status=skip group=natural label={seed} reason=no-shop compares=0 options=0 steps=0");
            return 0;
        }
        SerializableRun snapshot = RunManager.Instance.ToSave(null);
        _shopCoord = Coord(shopCoord.Value);
        _forceShopPoint = false;
        int result = CompareShop(native, player, snapshot, _shopCoord, "natural", seed, NaturalGold);
        if (_livePrices)
            Console.WriteLine($"SHOP-LIVE-PRICES calls={_livePriceCalcCosts}");
        return result;
    }

    private static MapCoord? FindReachableShop(NativeRunState native)
    {
        for (int step = 0; step < 12; step++)
        {
            native = RunManager.Instance.State
                ?? throw new InvalidDataException("导航时跑局状态丢失。");
            Player player = LocalContext.GetMe(native)
                ?? throw new InvalidDataException("导航时找不到本地玩家。");
            List<MapPoint> options = [.. RunTestHost.GetTravelablePoints(native)];
            if (options.Count == 0)
                return null;
            MapPoint? shop = options.FirstOrDefault(static point =>
                point.PointType == MapPointType.Shop);
            if (shop != null)
                return shop.coord;
            MapPoint chosen = ChooseTowardShop(options);
            _skipRoomEntry = true;
            _loop!.RunUntilCompleted(RunManager.Instance.EnterMapCoord(chosen.coord),
                TimeSpan.FromSeconds(120), "导航进点");
            _skipRoomEntry = false;
            _loop.RunUntilCompleted(RunManager.Instance.EnterRoom(new MapRoom()),
                TimeSpan.FromSeconds(120), "导航回地图");
            _ = player;
        }
        return null;
    }

    private static MapPoint ChooseTowardShop(List<MapPoint> options)
    {
        foreach (MapPoint option in options)
        {
            if (HasShopDescendant(option, 2))
                return option;
        }
        return options[0];
    }

    private static bool HasShopDescendant(MapPoint point, int depth)
    {
        if (depth < 0)
            return false;
        foreach (MapPoint child in point.Children)
        {
            if (child.PointType == MapPointType.Shop || HasShopDescendant(child, depth - 1))
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ D1 case

    private static int RunD1Case(string data, string corpora, string root, string? seedOverride)
    {
        OfflineRequest request = Program.LoadRequest(data, corpora, root);
        if (seedOverride != null)
            request = request with { Seed = seedOverride };
        if (request.Relics.Any(static relic => relic.IntegerMembers.Count != 0
            || relic.BooleanMembers.Count != 0))
        {
            Console.WriteLine(
                $"SHOP-CASE status=reject group=d1 label={root} reason=unsupported-relic-state compares=0 options=0 steps=0");
            return 0;
        }
        GeneratedScenarioSetup generated = GeneratedScenarioSetup.Prepare(request);
        Task<ScenarioBuilder.PreparedRun> prepare = ScenarioBuilder.PrepareRunAsync(
            new OfflineCombatWaiter(_loop!), generated.Request, preparedSetup: generated);
        _loop!.RunUntilCompleted(prepare, TimeSpan.FromSeconds(120), "D1 建局");
        using ScenarioBuilder.PreparedRun prepared = prepare.GetAwaiter().GetResult();
        NativeRunState native = prepared.Run;
        Player player = prepared.Player;
        MapPoint point = native.Map.StartingMapPoint;
        point.PointType = MapPointType.Shop;
        SerializableRun snapshot = RunManager.Instance.ToSave(null);
        _shopCoord = Coord(point.coord);
        _forceShopPoint = true;
        int result = CompareShop(native, player, snapshot, _shopCoord, "d1", root, D1Gold);
        if (_livePrices)
            Console.WriteLine($"SHOP-LIVE-PRICES calls={_livePriceCalcCosts}");
        return result;
    }

    // ------------------------------------------------------------------ compare one shop

    private static int CompareShop(NativeRunState initialNative, Player initialPlayer,
        SerializableRun snapshot, RunCoord shopCoord, string group, string label, int gold)
    {
        CaseStats stats = new(group, label);
        try
        {
            // The listener audit is a pre-entry check: a rejected listener is never executed
            // natively, so no unsupported room hook can mutate the native run first.
            RunState pre = RunStateImporter.Import(initialNative, initialPlayer);
            RunShopGenerator.RequireSupportedListeners(in pre, _table!);
        }
        catch (UnsupportedImportException exception)
        {
            return Reject(stats, exception.Reason, exception.Message);
        }
        catch (ContentUnavailableException exception)
        {
            return Reject(stats, exception.Reason, exception.Detail);
        }

        SequenceResult entry;
        try
        {
            entry = EnterFresh(snapshot, shopCoord, gold);
        }
        catch (UnsupportedImportException exception)
        {
            return Reject(stats, exception.Reason);
        }
        if (entry.Reject != null)
            return Reject(stats, entry.Reject, entry.RejectDetail);
        if (entry.Diff != null)
            return Diff(stats, entry.Diff);

        // Sequence definitions are derived from the entry state; each sequence re-enters the
        // shop from the same pre-entry snapshot.
        List<Intent> buys = BuildBuyIntents(entry.Session!, entry.Blitz, stats);
        foreach (Intent intent in buys)
        {
            if (intent.Kind == IntentKind.OpenRemoval)
                continue;
            RunSequence(stats, snapshot, shopCoord, gold, [intent, Intent.Leave()]);
            if (stats.Rejected || stats.Failed)
                return stats.Failed ? 1 : 0;
        }
        if (entry.Session!.RemovalChoices().Count != 0)
        {
            IReadOnlyList<CardModel> removal = entry.Session.RemovalChoices();
            int firstIndex = DeckIndexOf(entry.Player!, removal[0]);
            int lastIndex = DeckIndexOf(entry.Player!, removal[^1]);
            RunSequence(stats, snapshot, shopCoord, gold,
                [Intent.OpenRemoval(), Intent.Remove(firstIndex), Intent.Leave()]);
            if (stats.Rejected || stats.Failed)
                return stats.Failed ? 1 : 0;
            RunSequence(stats, snapshot, shopCoord, gold,
                [Intent.OpenRemoval(), Intent.Remove(lastIndex), Intent.Leave()]);
            if (stats.Rejected || stats.Failed)
                return stats.Failed ? 1 : 0;
            RunSequence(stats, snapshot, shopCoord, gold,
                [Intent.OpenRemoval(), Intent.Cancel(), Intent.Leave()]);
            if (stats.Rejected || stats.Failed)
                return stats.Failed ? 1 : 0;
        }
        RunSequence(stats, snapshot, shopCoord, gold, [Intent.Leave()]);
        if (stats.Rejected || stats.Failed)
            return stats.Failed ? 1 : 0;
        RunSequence(stats, snapshot, shopCoord, gold,
            BuildRandomSequence(entry.Session, entry.Blitz, stats, label, gold));
        if (stats.Rejected || stats.Failed)
            return stats.Failed ? 1 : 0;
        stats.Courier = entry.Player != null && entry.Player.Relics.Any(static relic =>
            relic.Id.Entry == "THE_COURIER");
        RunGreedySequence(stats, snapshot, shopCoord);
        if (stats.Rejected || stats.Failed)
            return stats.Failed ? 1 : 0;
        Console.WriteLine(
            $"SHOP-CASE status=same group={group} label={label} compares={stats.Compares} " +
            $"options={stats.OptionElements} steps={stats.Steps} " +
            $"actions=card{stats.CharacterBuys},colorless{stats.ColorlessBuys}," +
            $"relic{stats.RelicBuys},potion{stats.PotionBuys},removal{stats.RemovalBuys}," +
            $"remove{stats.Removals},cancel{stats.Cancels},leave{stats.Leaves} " +
            $"greedy={stats.GreedyPurchases} courier={(stats.Courier ? 1 : 0)} " +
            $"skips={stats.Skips.Values.Sum()}");
        foreach ((string reason, int count) in stats.Skips.OrderBy(static pair => pair.Key,
                     StringComparer.Ordinal))
        {
            Console.WriteLine($"SHOP-SKIP case={label} reason={reason} count={count}");
        }
        return 0;
    }

    private static List<Intent> BuildBuyIntents(NativeShopSession session, RunState blitz,
        CaseStats stats)
    {
        List<Intent> intents = [];
        NativeShopChoice[] choices = [.. session.Choices()];
        foreach (NativeShopChoiceKind kind in new[]
                 {
                     NativeShopChoiceKind.Card, NativeShopChoiceKind.Relic,
                     NativeShopChoiceKind.Potion, NativeShopChoiceKind.CardRemoval,
                 })
        {
            NativeShopChoice? choice = choices.FirstOrDefault(candidate => candidate.Kind == kind
                && (kind == NativeShopChoiceKind.CardRemoval
                    || WouldAccept(stats, blitz, Intent.Buy(candidate.Index))));
            if (choice == null)
                continue;
            intents.Add(kind == NativeShopChoiceKind.CardRemoval
                ? Intent.OpenRemoval() : Intent.Buy(choice.Index));
        }
        return intents;
    }

    private static bool WouldAccept(CaseStats stats, RunState blitz, Intent intent)
    {
        RunState probe = blitz;
        RunStepResult step = ExecuteBlitz(intent, ref probe);
        if (step.Status == RunStepStatus.Rejected && IsUnsupportedReason(step.Reason))
        {
            string reason = step.Reason ?? "unknown";
            stats.Skips[reason] = stats.Skips.GetValueOrDefault(reason) + 1;
            return false;
        }
        return true;
    }

    private static Intent[] BuildRandomSequence(NativeShopSession session, RunState blitz,
        CaseStats stats, string label, int gold)
    {
        Random random = new(unchecked((int)SeedFor(label + "/random")));
        List<Intent> intents = [];
        List<NativeShopChoice> pool = [.. session.Choices().Where(choice =>
            choice.Kind is NativeShopChoiceKind.Card or NativeShopChoiceKind.Relic
                or NativeShopChoiceKind.Potion
            && WouldAccept(stats, blitz, Intent.Buy(choice.Index)))];
        int spent = 0;
        for (int step = 0; step < 3 && pool.Count != 0; step++)
        {
            // A potion is only safe as the first buy: buying Sozu later would make the already
            // chosen potion option illegal. Keeping potions first still exercises potion state.
            List<NativeShopChoice> candidates = step == 0
                ? pool : [.. pool.Where(static choice =>
                    choice.Kind != NativeShopChoiceKind.Potion)];
            if (candidates.Count == 0)
                break;
            int pick = random.Next(candidates.Count);
            NativeShopChoice chosen = candidates[pick];
            if (step != 0 && spent + chosen.Price > gold)
                break;
            spent += chosen.Price;
            intents.Add(Intent.Buy(chosen.Index));
            pool.Remove(chosen);
        }
        intents.Add(Intent.Leave());
        return [.. intents];
    }

    private static void RunGreedySequence(CaseStats stats, SerializableRun snapshot,
        RunCoord shopCoord)
    {
        SequenceResult entry;
        try
        {
            entry = EnterFresh(snapshot, shopCoord, GreedyGold);
        }
        catch (UnsupportedImportException exception)
        {
            Reject(stats, exception.Reason, exception.Message);
            return;
        }
        if (entry.Reject != null)
        {
            Reject(stats, entry.Reject, entry.RejectDetail);
            return;
        }
        if (entry.Diff != null)
        {
            Diff(stats, entry.Diff);
            return;
        }
        NativeRunState native = entry.Native!;
        Player player = entry.Player!;
        NativeShopSession session = entry.Session!;
        RunState blitz = entry.Blitz;
        if (!CompareEntryState(stats, native, player, session, in blitz, "greedy-entry"))
            return;
        HashSet<int> attempted = [];
        for (int purchase = 0; purchase < GreedyMaxPurchases; purchase++)
        {
            NativeShopChoice[] legal = [.. session.Choices().Where(candidate =>
                candidate.Kind is NativeShopChoiceKind.Card or NativeShopChoiceKind.Relic
                    or NativeShopChoiceKind.Potion
                && WouldAccept(stats, blitz, Intent.Buy(candidate.Index)))];
            NativeShopChoice? choice = legal.FirstOrDefault(candidate =>
                !attempted.Contains(candidate.Index));
            if (choice == null)
            {
                // TheCourier restocks immediately, so one pass over the slots never exhausts
                // the shelf. Start a new pass until the explicit purchase cap.
                if (attempted.Count == 0)
                    break;
                attempted.Clear();
                continue;
            }
            attempted.Add(choice.Index);
            Intent intent = Intent.Buy(choice.Index);
            stats.Steps++;
            RunStepResult step = ExecuteBlitz(intent, ref blitz);
            if (step.Status == RunStepStatus.Rejected)
            {
                if (IsUnsupportedReason(step.Reason))
                {
                    Reject(stats, step.Reason ?? "unknown", step.Detail);
                    return;
                }
                Diff(stats, $"greedy blitz index={intent.Index} rejected={step.Reason}");
                return;
            }
            if (!ExecuteNative(intent, native, player, session, out string? nativeError))
            {
                Diff(stats, $"greedy native index={intent.Index} failed: {nativeError}");
                return;
            }
            if (!CompareStep(stats, native, player, session, in blitz,
                    "greedy-" + intent.Index))
            {
                return;
            }
            stats.Count(intent);
            stats.GreedyPurchases++;
        }
        stats.Steps++;
        RunStepResult leave = ExecuteBlitz(Intent.Leave(), ref blitz);
        if (leave.Status == RunStepStatus.Rejected)
        {
            Diff(stats, $"greedy leave rejected={leave.Reason}");
            return;
        }
        ExecuteNative(Intent.Leave(), native, player, session, out string? leaveError);
        if (leaveError != null)
        {
            Diff(stats, $"greedy native leave failed: {leaveError}");
            return;
        }
        if (!CompareStep(stats, native, player, session, in blitz, "greedy-leave"))
            return;
        stats.Count(Intent.Leave());
    }

    private static void RunSequence(CaseStats stats, SerializableRun snapshot, RunCoord shopCoord,
        int gold, Intent[] intents)
    {
        SequenceResult entry;
        try
        {
            entry = EnterFresh(snapshot, shopCoord, gold);
        }
        catch (UnsupportedImportException exception)
        {
            Reject(stats, exception.Reason);
            return;
        }
        if (entry.Reject != null)
        {
            Reject(stats, entry.Reject, entry.RejectDetail);
            return;
        }
        if (entry.Diff != null)
        {
            Diff(stats, entry.Diff);
            return;
        }
        NativeRunState native = entry.Native!;
        Player player = entry.Player!;
        NativeShopSession session = entry.Session!;
        RunState blitz = entry.Blitz;
        if (!CompareEntryState(stats, native, player, session, in blitz, "entry"))
            return;
        foreach (Intent intent in intents)
        {
            stats.Steps++;
            RunStepResult step = ExecuteBlitz(intent, ref blitz);
            if (step.Status == RunStepStatus.Rejected)
            {
                if (IsUnsupportedReason(step.Reason))
                {
                    Reject(stats, step.Reason ?? "unknown", step.Detail);
                    return;
                }
                RunDecisionRequest current = RunDecisionRequest.Capture(in blitz);
                string options = string.Join(',', Enumerable.Range(0, current.OptionCount)
                    .Select(index => $"{current.Options[index].Kind}:{current.Options[index].Index}"));
                Diff(stats, $"blitz action {intent.Kind} index={intent.Index} " +
                    $"rejected={step.Reason} request={current.Kind} stage={blitz.Shop.Stage} " +
                    $"options={options}");
                return;
            }
            bool nativeOk = ExecuteNative(intent, native, player, session, out string? nativeError);
            if (!nativeOk)
            {
                Diff(stats, $"native action {intent.Kind} failed: {nativeError}");
                return;
            }
            if (!CompareStep(stats, native, player, session, in blitz, intent.Kind.ToString()))
                return;
            stats.Count(intent);
        }
    }

    private static SequenceResult EnterFresh(SerializableRun snapshot, RunCoord shopCoord, int gold)
    {
        _loop!.RunUntilCompleted(RunTestHost.RestoreSnapshotAsync(snapshot),
            TimeSpan.FromSeconds(120), "恢复进店前");
        NativeRunState native = RunManager.Instance.State
            ?? throw new InvalidDataException("恢复后跑局状态为空。");
        Player player = LocalContext.GetMe(native)
            ?? throw new InvalidDataException("恢复后找不到本地玩家。");
        if (_forceShopPoint)
        {
            MapPoint? point = native.Map.GetPoint(new MapCoord(_shopCoord.Col, _shopCoord.Row));
            if (point == null)
                throw new InvalidDataException($"恢复后地图没有商店点 {_shopCoord}。");
            point.PointType = MapPointType.Shop;
        }
        player.Gold = gold;
        RunState before = RunStateImporter.Import(native, player);
        RunShopGenerator.RequireSupportedListeners(in before, _table!);
        _loop.RunUntilCompleted(RunManager.Instance.EnterMapCoord(
            new MapCoord(shopCoord.Col, shopCoord.Row)), TimeSpan.FromSeconds(120), "进商店");
        native = RunManager.Instance.State
            ?? throw new InvalidDataException("进商店后状态为空。");
        player = LocalContext.GetMe(native)
            ?? throw new InvalidDataException("进商店后找不到玩家。");
        NativeShopSession session = NativeShopSession.TryCreate(native, player)
            ?? throw new InvalidDataException("原生没有进入商人房间。");
        RunStepResult step = _kernel!.Step(ref before,
            RunAction.SelectMapPoint(shopCoord));
        if (step.Status == RunStepStatus.Rejected)
        {
            return new SequenceResult
            {
                Reject = step.Reason ?? "shop-entry-rejected",
                RejectDetail = step.Detail,
            };
        }
        if (step.Status != RunStepStatus.Decision || before.Shop.Stage != RunShopStage.Shop)
            return new SequenceResult { Diff = $"entry status={step.Status}/{step.Reason}" };
        return new SequenceResult
        {
            Native = native,
            Player = player,
            Session = session,
            Blitz = before,
        };
    }

    private static bool ExecuteNative(Intent intent, NativeRunState native, Player player,
        NativeShopSession session, out string? error)
    {
        error = null;
        switch (intent.Kind)
            {
                case IntentKind.Buy:
                {
                    NativeShopChoice? choice = session.Choices().FirstOrDefault(candidate =>
                        candidate.Index == intent.Index);
                    if (choice == null)
                    {
                        error = $"no native choice {intent.Index}";
                        return false;
                    }
                    if (choice.Kind == NativeShopChoiceKind.CardRemoval)
                    {
                        session.EnterRemoval();
                        return true;
                    }
                    Task<bool> buy = session.BuyAsync(choice);
                    _loop!.RunUntilCompleted(buy, TimeSpan.FromSeconds(60), "购买");
                    if (!buy.GetAwaiter().GetResult())
                    {
                        error = "native purchase failed";
                        return false;
                    }
                    return true;
                }
                case IntentKind.OpenRemoval:
                    session.EnterRemoval();
                    return true;
                case IntentKind.Remove:
                {
                    CardModel card = player.Deck.Cards[intent.DeckIndex];
                    Task<bool> remove = session.RemoveAsync(card);
                    _loop!.RunUntilCompleted(remove, TimeSpan.FromSeconds(60), "删牌");
                    if (!remove.GetAwaiter().GetResult())
                    {
                        error = "native removal failed";
                        return false;
                    }
                    return true;
                }
                case IntentKind.Cancel:
                {
                    Task<bool> cancel = session.RemoveAsync(null);
                    _loop!.RunUntilCompleted(cancel, TimeSpan.FromSeconds(60), "取消删牌");
                    if (cancel.GetAwaiter().GetResult())
                    {
                        error = "native cancel removed a card";
                        return false;
                    }
                    return true;
                }
                case IntentKind.Leave:
                    session.Leave();
                    return true;
            default:
                error = "unknown intent";
                return false;
        }
    }

    private static RunStepResult ExecuteBlitz(Intent intent, ref RunState blitz)
    {
        RunDecisionRequest request = RunDecisionRequest.Capture(in blitz);
        switch (intent.Kind)
        {
            case IntentKind.Buy:
            case IntentKind.OpenRemoval:
            {
                int option = -1;
                for (int index = 0; index < request.OptionCount; index++)
                {
                    RunDecisionOption candidate = request.Options[index];
                    if (candidate.Kind is RunDecisionOptionKind.ShopEntry
                            or RunDecisionOptionKind.CardRemoval
                        && candidate.Index == intent.Index)
                    {
                        option = index;
                        break;
                    }
                }
                if (option < 0)
                    return new RunStepResult { Status = RunStepStatus.Rejected,
                        Reason = "shop-option" };
                return _kernel!.Step(ref blitz, new RunAction
                {
                    Kind = RunActionKind.BuyShopEntry,
                    Option = (byte)option,
                });
            }
            case IntentKind.Remove:
            {
                int option = -1;
                for (int index = 0; index < request.OptionCount; index++)
                {
                    RunDecisionOption candidate = request.Options[index];
                    if (candidate.Kind == RunDecisionOptionKind.Card
                        && candidate.Index == intent.DeckIndex)
                    {
                        option = index;
                        break;
                    }
                }
                if (option < 0)
                    return new RunStepResult { Status = RunStepStatus.Rejected,
                        Reason = "shop-removal-card" };
                return _kernel!.Step(ref blitz, new RunAction
                {
                    Kind = RunActionKind.RemoveCard,
                    Option = (byte)option,
                });
            }
            case IntentKind.Cancel:
                return _kernel!.Step(ref blitz, new RunAction
                {
                    Kind = RunActionKind.CancelCardSelection,
                });
            case IntentKind.Leave:
                return _kernel!.Step(ref blitz, new RunAction
                {
                    Kind = RunActionKind.LeaveShop,
                });
            default:
                return new RunStepResult { Status = RunStepStatus.Rejected, Reason = "intent" };
        }
    }

    private static bool CompareEntryState(CaseStats stats, NativeRunState native, Player player,
        NativeShopSession session, in RunState blitz, string label)
    {
        stats.Compares++;
        RunShopStage stage = session.Stage == NativeShopStage.CardRemoval
            ? RunShopStage.CardRemoval
            : native.CurrentRoom is MerchantRoom ? RunShopStage.Shop : RunShopStage.None;
        string nativeDump = NativeRunDump.Capture(native, player, shopStage: stage);
        string blitzDump = RunDump.Capture(blitz);
        List<string> differences = RunImportCompare.DiffDumps(nativeDump, blitzDump);
        if (differences.Count != 0)
        {
            Diff(stats, $"{label} state {differences[0]}");
            return false;
        }
        // Second reader: the importer must produce the same shop payload.
        RunState imported = RunStateImporter.Import(native, player, shopStage: stage);
        List<string> importDiff = RunImportCompare.DiffDumps(RunDump.Capture(imported),
            blitzDump);
        if (importDiff.Count != 0)
        {
            Diff(stats, $"{label} import {importDiff[0]}");
            return false;
        }
        if (!CompareOptions(stats, native, player, session, in blitz, label))
            return false;
        RunView view = RunView.From(in blitz);
        if (view.Shop.Stage != stage)
        {
            Diff(stats, $"{label} view stage native={stage} blitz={view.Shop.Stage}");
            return false;
        }
        return true;
    }

    private static bool CompareStep(CaseStats stats, NativeRunState native, Player player,
        NativeShopSession session, in RunState blitz, string label)
        => CompareEntryState(stats, native, player, session, in blitz, label);

    private static bool CompareOptions(CaseStats stats, NativeRunState native, Player player,
        NativeShopSession session, in RunState blitz, string label)
    {
        RunDecisionRequest request = RunDecisionRequest.Capture(in blitz);
        if (session.Stage == NativeShopStage.CardRemoval)
        {
            IReadOnlyList<CardModel> cards = session.RemovalChoices();
            if (request.Kind != RunDecision.CardSelect
                || request.OptionCount != cards.Count + 1)
            {
                Diff(stats, $"{label} removal-request native={cards.Count + 1} " +
                    $"blitz={request.Kind}/{request.OptionCount}");
                return false;
            }
            for (int index = 0; index < cards.Count; index++)
            {
                RunDecisionOption option = request.Options[index];
                if (option.Kind != RunDecisionOptionKind.Card
                    || option.Model.Kind != RunRefKind.Card
                    || Registry.Card(option.Model.Id).NativeId != cards[index].Id.Entry
                    || option.Upgrade != cards[index].CurrentUpgradeLevel)
                {
                    Diff(stats, $"{label} removal-option {index} native={cards[index].Id.Entry}+" +
                        $"{cards[index].CurrentUpgradeLevel} blitz={option.Model.Id}+{option.Upgrade}");
                    return false;
                }
            }
            stats.OptionElements += cards.Count;
            return true;
        }
        if (native.CurrentRoom is not MerchantRoom)
        {
            List<MapPoint> points = [.. RunTestHost.GetTravelablePoints(native)
                .OrderBy(static point => point.coord.col)
                .ThenBy(static point => point.coord.row)];
            if (request.Kind != RunDecision.MapPoint
                || request.OptionCount != points.Count)
            {
                Diff(stats, $"{label} map-request native={points.Count} " +
                    $"blitz={request.Kind}/{request.OptionCount}");
                return false;
            }
            for (int index = 0; index < points.Count; index++)
            {
                if (!request.Options[index].Coord.Equals(Coord(points[index].coord)))
                {
                    Diff(stats, $"{label} map-option {index}");
                    return false;
                }
            }
            stats.OptionElements += points.Count;
            return true;
        }
        IReadOnlyList<NativeShopChoice> choices = session.Choices();
        if (request.Kind != RunDecision.Shop || request.OptionCount != choices.Count)
        {
            Diff(stats, $"{label} shop-request native={choices.Count} " +
                $"blitz={request.Kind}/{request.OptionCount}");
            return false;
        }
        for (int index = 0; index < choices.Count; index++)
        {
            NativeShopChoice choice = choices[index];
            RunDecisionOption option = request.Options[index];
            string? reason = OptionDifference(choice, option);
            if (reason != null)
            {
                Diff(stats, $"{label} shop-option {index} {reason}");
                return false;
            }
        }
        stats.OptionElements += choices.Count;
        return true;
    }

    private static string? OptionDifference(NativeShopChoice choice, RunDecisionOption option)
    {
        if (choice.Kind == NativeShopChoiceKind.Leave)
            return option.Kind == RunDecisionOptionKind.Skip ? null
                : $"native=leave blitz={option.Kind}";
        RunDecisionOptionKind expected = choice.Kind == NativeShopChoiceKind.CardRemoval
            ? RunDecisionOptionKind.CardRemoval : RunDecisionOptionKind.ShopEntry;
        if (option.Kind != expected)
            return $"native={choice.Kind} blitz={option.Kind}";
        if (option.Index != choice.Index)
            return $"index native={choice.Index} blitz={option.Index}";
        if (option.Amount != choice.Price)
            return $"price native={choice.Price} blitz={option.Amount}";
        switch (choice.Kind)
        {
            case NativeShopChoiceKind.Card:
                if (option.Model.Kind != RunRefKind.Card
                    || Registry.Card(option.Model.Id).NativeId != choice.Card!.Id.Entry
                    || option.Upgrade != choice.Card.CurrentUpgradeLevel)
                {
                    return $"card native={choice.Card!.Id.Entry}+{choice.Card.CurrentUpgradeLevel} " +
                        $"blitz={option.Model.Kind}:{option.Model.Id}+{option.Upgrade}";
                }
                break;
            case NativeShopChoiceKind.Relic:
                if (option.Model.Kind != RunRefKind.Relic
                    || Registry.Relic(option.Model.Id).NativeId != choice.Relic!.Id.Entry)
                {
                    return "relic model";
                }
                break;
            case NativeShopChoiceKind.Potion:
                if (option.Model.Kind != RunRefKind.Potion
                    || Registry.Potion(option.Model.Id).NativeId != choice.Potion!.Id.Entry)
                {
                    return "potion model";
                }
                break;
        }
        return null;
    }

    private static int DeckIndexOf(Player player, CardModel card)
    {
        for (int index = 0; index < player.Deck.Cards.Count; index++)
        {
            if (ReferenceEquals(player.Deck.Cards[index], card))
                return index;
        }
        throw new InvalidDataException($"牌组没有 {card.Id.Entry}。");
    }

    private static RunCoord Coord(MapCoord coord)
        => new(checked((byte)coord.col), checked((byte)coord.row));

    private static int Reject(CaseStats stats, string reason, string? detail = null)
    {
        stats.Rejected = true;
        if (!string.IsNullOrEmpty(detail))
        {
            Console.WriteLine($"SHOP-REJECT-DETAIL group={stats.Group} label={stats.Label} " +
                $"detail={detail}");
        }
        Console.WriteLine(
            $"SHOP-CASE status=reject group={stats.Group} label={stats.Label} reason={reason} " +
            $"compares={stats.Compares} options={stats.OptionElements} steps={stats.Steps}");
        return 0;
    }

    private static int Diff(CaseStats stats, string detail)
    {
        stats.Failed = true;
        Console.WriteLine($"SHOP-DIFF group={stats.Group} label={stats.Label} {detail}");
        Console.WriteLine(
            $"SHOP-CASE status=diff group={stats.Group} label={stats.Label} " +
            $"compares={stats.Compares} options={stats.OptionElements} steps={stats.Steps}");
        return 1;
    }

    private static bool IsUnsupportedReason(string? reason)
        => reason is "shop-hook-listener" or "relic-obtain-effect" or "unsupported-relic"
            or "unsupported-potion" or "unsupported-card" or "unsupported-room"
            or "reward-hook-listener" or "relic-allowed-hook" or "gold-proportion-encounter"
            or "event-allowed-check" or "capacity";

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

    private static UnlockState PresetUnlock(string name) => name switch
    {
        "empty" => new UnlockState([], [], 0),
        "all" => new UnlockState([.. EpochModel.AllEpochIds],
            [.. Enumerable.Range(0, RunCatalog.EncounterCount)
                .Select(index => new ModelId("ENCOUNTER", RunCatalog.EncounterName((ushort)index)))],
            999999999),
        _ => throw new InvalidDataException($"未知存档预设 {name}。"),
    };

    private static IEnumerable<string> EnumerateRoots(string data)
        => Directory.EnumerateDirectories(Path.Combine(data, "d1"))
            .Select(Path.GetFileName)
            .Where(static name => name != null)
            .OrderBy(static name => name, StringComparer.Ordinal)!;

    // ------------------------------------------------------------------ Harmony patches

    private static void InstallLivePricePatch()
    {
        MethodInfo target = AccessTools.Method(typeof(MerchantPotionEntry),
            nameof(MerchantPotionEntry.CalcCost))
            ?? throw new MissingMethodException(typeof(MerchantPotionEntry).FullName,
                nameof(MerchantPotionEntry.CalcCost));
        MethodInfo prefix = AccessTools.Method(typeof(ShopCompare), nameof(LivePricePrefix));
        MethodInfo postfix = AccessTools.Method(typeof(ShopCompare), nameof(LivePricePostfix));
        GameBootstrap.Harmony.Patch(target, prefix: new HarmonyMethod(prefix),
            postfix: new HarmonyMethod(postfix));
    }

    private static void LivePricePrefix(out bool __state)
    {
        __state = TestMode.IsOn;
        TestMode.IsOn = false;
        _livePriceCalcCosts++;
    }

    private static void LivePricePostfix(bool __state)
    {
        TestMode.IsOn = __state;
    }

    private static void InstallSavePatch()
    {
        MethodInfo progress = AccessTools.Method(typeof(SaveManager),
            nameof(SaveManager.SaveProgressFile));
        MethodInfo skip = AccessTools.Method(typeof(ShopCompare), nameof(SkipPrefix));
        GameBootstrap.Harmony.Patch(progress, prefix: new HarmonyMethod(skip));
        MethodInfo run = AccessTools.Method(typeof(SaveManager), nameof(SaveManager.SaveRun),
            [typeof(AbstractRoom), typeof(bool)]);
        MethodInfo skipTask = AccessTools.Method(typeof(ShopCompare), nameof(SkipTaskPrefix));
        GameBootstrap.Harmony.Patch(run, prefix: new HarmonyMethod(skipTask));
    }

    private static bool SkipPrefix() => false;

    private static bool SkipTaskPrefix(ref Task __result)
    {
        __result = Task.CompletedTask;
        return false;
    }

    private static bool SkipRoomEntryPrefix(AbstractRoom room, ref Task __result)
    {
        if (!_skipRoomEntry || room is MapRoom)
            return true;
        _skipRoomEntry = false;
        __result = Task.CompletedTask;
        return false;
    }

    // ------------------------------------------------------------------ intents / stats

    private enum IntentKind
    {
        Buy,
        OpenRemoval,
        Remove,
        Cancel,
        Leave,
    }

    private sealed class Intent
    {
        public required IntentKind Kind { get; init; }

        public int Index { get; init; }

        public int DeckIndex { get; init; }

        public static Intent Buy(int index) => new() { Kind = IntentKind.Buy, Index = index };

        public static Intent OpenRemoval() => new() { Kind = IntentKind.OpenRemoval,
            Index = RunShopGenerator.RemovalIndex };

        public static Intent Remove(int deckIndex) => new() { Kind = IntentKind.Remove,
            DeckIndex = deckIndex };

        public static Intent Cancel() => new() { Kind = IntentKind.Cancel };

        public static Intent Leave() => new() { Kind = IntentKind.Leave };
    }

    private sealed class SequenceResult
    {
        public NativeRunState? Native { get; init; }

        public Player? Player { get; init; }

        public NativeShopSession? Session { get; init; }

        public RunState Blitz { get; init; }

        public string? Reject { get; init; }

        public string? RejectDetail { get; init; }

        public string? Diff { get; init; }
    }

    private sealed class CaseStats(string group, string label)
    {
        public string Group { get; } = group;

        public string Label { get; } = label;

        public int Compares { get; set; }

        public int OptionElements { get; set; }

        public int Steps { get; set; }

        public bool Rejected { get; set; }

        public bool Failed { get; set; }

        public int CharacterBuys { get; set; }

        public int ColorlessBuys { get; set; }

        public int RelicBuys { get; set; }

        public int PotionBuys { get; set; }

        public int RemovalBuys { get; set; }

        public int Removals { get; set; }

        public int Cancels { get; set; }

        public int Leaves { get; set; }

        public int GreedyPurchases { get; set; }

        public bool Courier { get; set; }

        public Dictionary<string, int> Skips { get; } = new(StringComparer.Ordinal);

        public void Count(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.Buy when intent.Index is >= 0
                        and < RunLimits.ShopCharacterCards:
                    CharacterBuys++;
                    break;
                case IntentKind.Buy when intent.Index is >= RunLimits.ShopCharacterCards
                        and < RunShopGenerator.RelicBase:
                    ColorlessBuys++;
                    break;
                case IntentKind.Buy when intent.Index is >= RunShopGenerator.RelicBase
                        and < RunShopGenerator.PotionBase:
                    RelicBuys++;
                    break;
                case IntentKind.Buy when intent.Index is >= RunShopGenerator.PotionBase
                        and < RunShopGenerator.RemovalIndex:
                    PotionBuys++;
                    break;
                case IntentKind.OpenRemoval:
                    RemovalBuys++;
                    break;
                case IntentKind.Remove:
                    Removals++;
                    break;
                case IntentKind.Cancel:
                    Cancels++;
                    break;
                case IntentKind.Leave:
                    Leaves++;
                    break;
            }
        }
    }
}
