// --offline-combat: whole-combat replay inside the Blitz kernel. The decision loop is the same
// FullCombatDriver the Mod runs; this host supplies the imported opening root, deterministic search
// and kernel stepping instead of native deployment.
using System.Diagnostics;
using System.Text.Json;
using BlitzSolver.Engine;
using BlitzSolver.Import;
using BlitzSolver.Search;
using BlitzSolver.Search.Policy;
using BlitzSolver.TestHost;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace OfflineHost;

internal sealed record OfflineCombatSettings(
    string Data,
    string Corpora,
    string Output,
    SearchPerformanceTier Tier,
    SearchPerformanceProfile Profile,
    long MemoryBudgetBytes,
    int WarmupNodes,
    int? MaxExpandedNodesOverride,
    int? PoolStateBudgetMb)
{
    /// <summary>0.15 x 16 GiB, the fixed machine size the offline comparison assumes.</summary>
    internal static long DefaultMemoryBudgetBytes => (16L << 30) / 100 * 15 + (16L << 30) % 100 * 15 / 100;
}

internal sealed record OfflineCombatRootResult(
    string Root,
    string Status,
    string Outcome,
    int Turns,
    int PlayerHp,
    int Searches,
    long ElapsedMs,
    string? Error,
    int ExitCode,
    string? ExceptionType);

internal static class OfflineCombat
{
    public static int Run(OfflineCombatSettings settings, string? root, string? rootsFile, int jobs)
    {
        Directory.CreateDirectory(settings.Output);
        if (root != null)
        {
            if (rootsFile != null)
            {
                Console.Error.WriteLine("--offline-combat 的 --root 与 --roots-file 不能同时使用。");
                return 2;
            }
            return RunRoot(settings, root);
        }

        string[] roots;
        if (rootsFile != null)
        {
            roots = File.ReadAllLines(rootsFile)
                .Select(static line => line.Trim())
                .Where(static line => line.Length > 0 && !line.StartsWith('#'))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
        }
        else
        {
            roots = EnumerateQualityRoots(settings);
        }
        if (roots.Length == 0)
        {
            Console.Error.WriteLine("--offline-combat 没有找到任何根。");
            return 2;
        }
        Console.WriteLine(
            $"offline-combat roots={roots.Length} jobs={jobs} tier={settings.Tier} " +
            $"beam={settings.Profile.StartWidth}..{settings.Profile.MaxWidth} " +
            $"nodes_per_width={settings.Profile.MaxExpandedNodesPerWidth} " +
            $"time_ms={settings.Profile.TotalTimeBudgetMilliseconds} out={settings.Output}");
        Stopwatch wall = Stopwatch.StartNew();
        List<OfflineCombatRootResult> results = [];
        object gate = new();
        int failed = 0;
        Parallel.ForEach(
            roots,
            new ParallelOptions { MaxDegreeOfParallelism = jobs },
            name =>
            {
                (int code, string stdout, string stderr) = RunChild(settings, name);
                OfflineCombatRootResult? entry = ReadSummary(settings.Output, name);
                entry = entry is null
                    ? FailedRoot(name, code, stderr)
                    : entry with { ExitCode = code };
                lock (gate)
                {
                    results.Add(entry);
                    if (code != 0 || entry.Status is "failed" or "error" or "crashed")
                        failed++;
                }
                Console.WriteLine(
                    $"root={name} exit={code} status={entry.Status} outcome={entry.Outcome} " +
                    $"turns={entry.Turns} hp={entry.PlayerHp} searches={entry.Searches} " +
                    $"ms={entry.ElapsedMs}" +
                    (entry.Error is { Length: > 0 } error ? $" error={error}" : ""));
                if (stdout.Length > 0)
                    Console.Write(stdout);
                if (code != 0 && stderr.Length > 0)
                    Console.Error.Write(stderr);
            });
        wall.Stop();
        results.Sort(static (left, right) => string.CompareOrdinal(left.Root, right.Root));
        WriteSummary(settings, roots.Length, failed, wall.ElapsedMilliseconds, results);
        Console.WriteLine(
            $"offline-combat done roots={roots.Length} failed={failed} " +
            $"wall_ms={wall.ElapsedMilliseconds}");
        return failed == 0 ? 0 : 1;
    }

    private static string[] EnumerateQualityRoots(OfflineCombatSettings settings)
    {
        string quality = Path.Combine(settings.Corpora, "quality");
        if (!Directory.Exists(quality))
            return [];
        return Directory.EnumerateDirectories(quality)
            .Where(directory => File.Exists(Path.Combine(directory, "spec.json")))
            .Select(directory => "quality-" + Path.GetFileName(directory))
            .Where(name => File.Exists(
                Path.Combine(settings.Data, "d1", name, "meta.json")))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static int RunRoot(OfflineCombatSettings settings, string root)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string directory = Path.Combine(settings.Output, root);
        string stale = Path.Combine(directory, "full-combat.json");
        if (File.Exists(stale))
            File.Delete(stale);
        string staleFailure = Path.Combine(directory, "failure.json");
        if (File.Exists(staleFailure))
            File.Delete(staleFailure);
        try
        {
            (CombatState combat, OfflineRequest request) = Build(settings, root);
            Player player = LocalContext.GetMe(combat)
                ?? throw new InvalidOperationException("找不到本地玩家。");
            if (combat.RunState is not RunState runState)
                throw new InvalidDataException("原生战斗的 RunState 不是具体 RunState。");
            Rules rules = RulesCompiler.Compile(combat, player, runState);
            LiveCombatTracker tracker = new(combat);
            Battle opening = tracker.ImportRoot(combat, player, rules);
            tracker.Observe(opening);
            SearchPolicy policy = SearchPolicyOverrides.ApplyOverrides(
                SearchPolicyCapture.Capture(combat, player), request, player);
            OfflineHostCombat host = new(combat, player, rules, policy, settings.Profile,
                settings.MemoryBudgetBytes, settings.WarmupNodes,
                settings.MaxExpandedNodesOverride, settings.PoolStateBudgetMb, opening);
            FullCombatResult result = FullCombatDriver.RunAsync(host, root, CancellationToken.None)
                .GetAwaiter().GetResult();
            long wallMs = stopwatch.ElapsedMilliseconds;
            WriteResult(settings.Output, root, result);
            Console.WriteLine(
                $"root={root} status={result.Status} outcome={result.Outcome} turns={result.Turns} " +
                $"hp={result.PlayerHp}/{result.PlayerMaxHp} searches={result.Searches} " +
                $"reused={result.ReusedTurns} deviations={result.Deviations} " +
                $"failures={result.DeploymentFailures} combat_ms={result.ElapsedMs} wall_ms={wallMs}");
            return result.Outcome is "victory" or "defeat" or "ended" ? 0 : 5;
        }
        catch (UnsupportedImportException error)
        {
            WriteFailure(settings.Output, root, $"UNSUPPORTED reason={error.Reason} {error.Detail}");
            Console.Error.WriteLine($"root={root} UNSUPPORTED reason={error.Reason} {error.Detail}");
            return 3;
        }
        catch (UnsupportedException error)
        {
            WriteFailure(settings.Output, root, $"UNSUPPORTED {error.Message}");
            Console.Error.WriteLine($"root={root} UNSUPPORTED {error.Message}");
            return 3;
        }
        catch (NotSupportedException error)
        {
            WriteFailure(settings.Output, root, $"UNSUPPORTED {error.Message}");
            Console.Error.WriteLine($"root={root} UNSUPPORTED {error.Message}");
            return 3;
        }
        catch (CapacityException error)
        {
            WriteFailure(settings.Output, root, $"CAPACITY region={error.Region} {error.Message}");
            Console.Error.WriteLine($"root={root} CAPACITY region={error.Region} {error.Message}");
            return 3;
        }
        catch (InvalidDataException error)
        {
            WriteFailure(settings.Output, root, $"INVALID_DATA {error.Message}");
            Console.Error.WriteLine($"root={root} INVALID_DATA {error.Message}");
            return 2;
        }
        catch (InvalidOperationException error)
        {
            WriteFailure(settings.Output, root, $"FAILED {error.Message}");
            Console.Error.WriteLine($"root={root} FAILED {error.Message}");
            return 5;
        }
        catch (TimeoutException error)
        {
            WriteFailure(settings.Output, root, $"TIMEOUT {error.Message}");
            Console.Error.WriteLine($"root={root} TIMEOUT {error.Message}");
            return 4;
        }
    }

    private static (CombatState Combat, OfflineRequest Request) Build(
        OfflineCombatSettings settings, string root)
    {
        OfflineRequest request = Program.LoadRequest(settings.Data, settings.Corpora, root);
        if (request.FlashPrefixActions.Length != 0 || request.FlashPrefixPlan.Length != 0)
            throw new NotSupportedException("离线整场不支持前缀根。");
        if (request.FlashTurnSetupRootForTest)
            throw new NotSupportedException("离线整场不支持回合准备根。");
        MainLoopContext loop = new();
        SynchronizationContext.SetSynchronizationContext(loop);
        GameBootstrap.ApplyGodotBypasses();
        GameBootstrap.SkipGodotNodeStaticConstructors();
        GameBootstrap.InitializeStaticState();
        Task<CombatState> build = ScenarioBuilder.BuildAsync(new OfflineCombatWaiter(loop), request);
        loop.RunUntilCompleted(build, TimeSpan.FromSeconds(600), $"建局 {root}");
        return (build.GetAwaiter().GetResult(), request);
    }

    private static (int Code, string Stdout, string Stderr) RunChild(
        OfflineCombatSettings settings, string root)
    {
        ProcessStartInfo start = new("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--offline-combat");
        start.ArgumentList.Add("--root");
        start.ArgumentList.Add(root);
        start.ArgumentList.Add("--data");
        start.ArgumentList.Add(settings.Data);
        start.ArgumentList.Add("--corpora");
        start.ArgumentList.Add(settings.Corpora);
        start.ArgumentList.Add("--out");
        start.ArgumentList.Add(settings.Output);
        start.ArgumentList.Add("--search-tier");
        start.ArgumentList.Add(settings.Tier.ToString());
        start.ArgumentList.Add("--beam-width");
        start.ArgumentList.Add(settings.Profile.StartWidth.ToString());
        start.ArgumentList.Add("--max-width");
        start.ArgumentList.Add(settings.Profile.MaxWidth.ToString());
        start.ArgumentList.Add("--nodes-per-width");
        start.ArgumentList.Add(settings.Profile.MaxExpandedNodesPerWidth.ToString());
        if (settings.MaxExpandedNodesOverride is { } maxNodes)
        {
            start.ArgumentList.Add("--max-nodes");
            start.ArgumentList.Add(maxNodes.ToString());
        }
        if (settings.PoolStateBudgetMb is { } poolMb)
        {
            start.ArgumentList.Add("--pool-state-budget-mb");
            start.ArgumentList.Add(poolMb.ToString());
        }
        start.ArgumentList.Add("--memory-budget-bytes");
        start.ArgumentList.Add(settings.MemoryBudgetBytes.ToString());
        start.ArgumentList.Add("--jobs");
        start.ArgumentList.Add("1");
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动离线整场子进程。");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult());
    }

    private static OfflineCombatRootResult? ReadSummary(string output, string root)
    {
        string path = Path.Combine(output, root, "full-combat.json");
        if (!File.Exists(path))
            return null;
        try
        {
            FullCombatResult? result = JsonSerializer.Deserialize<FullCombatResult>(
                File.ReadAllText(path), OfflineJson.Options);
            if (result == null)
                return null;
            return new OfflineCombatRootResult(result.Root, result.Status, result.Outcome,
                result.Turns, result.PlayerHp, result.Searches, result.ElapsedMs, result.PauseReason,
                0, null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteSummary(OfflineCombatSettings settings, int roots, int failed,
        long wallMs, List<OfflineCombatRootResult> results)
    {
        object summary = new
        {
            schemaVersion = 1,
            tier = settings.Tier.ToString(),
            startWidth = settings.Profile.StartWidth,
            maxWidth = settings.Profile.MaxWidth,
            maxExpandedNodesPerWidth = settings.Profile.MaxExpandedNodesPerWidth,
            totalTimeBudgetMilliseconds = settings.Profile.TotalTimeBudgetMilliseconds,
            maxExpandedNodesOverride = settings.MaxExpandedNodesOverride,
            poolStateBudgetMb = settings.PoolStateBudgetMb,
            memoryBudgetBytes = settings.MemoryBudgetBytes,
            roots,
            failed,
            wallMs,
            results,
        };
        string path = Path.Combine(settings.Output, "summary.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(summary, OfflineJson.Options));
        File.Move(temporary, path, true);
    }

    private static void WriteResult(string output, string root, FullCombatResult result)
    {
        string directory = Path.Combine(output, root);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "full-combat.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(result, OfflineJson.Options));
        File.Move(temporary, path, true);
    }

    private static void WriteFailure(string output, string root, string error)
    {
        string directory = Path.Combine(output, root);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "failure.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { root, error }, OfflineJson.Options));
    }

    /// <summary>
    /// A child that died before writing full-combat.json. Its exit code is always recorded, and the
    /// unhandled exception type/message is extracted from stderr when the runtime printed one. The
    /// scenario trace lines ("[scenario] ...") are diagnostics from the last completed stage, not an
    /// error reason, so they are never used as the summary's error text.
    /// </summary>
    private static OfflineCombatRootResult FailedRoot(string root, int exitCode, string stderr)
    {
        (string? type, string? message) = ParseChildException(stderr);
        string error;
        if (type is not null)
            error = message is { Length: > 0 } ? $"{type}: {message}" : type;
        else
            error = exitCode == 0
                ? "没有写出 full-combat.json"
                : $"exit={exitCode} 没有写出 full-combat.json";
        return new OfflineCombatRootResult(root, exitCode == 0 ? "failed" : "crashed", "",
            0, 0, 0, 0, error, exitCode, type);
    }

    /// <summary>Reads the runtime's unhandled-exception banner, e.g. "Unhandled exception. System.X: msg".</summary>
    private static (string? Type, string? Message) ParseChildException(string stderr)
    {
        string[] lines = stderr.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            const string marker = "Unhandled exception";
            if (!line.StartsWith(marker, StringComparison.Ordinal))
                continue;
            string rest = line[marker.Length..].TrimStart('.', ':', ' ').Trim();
            if (rest.Length == 0 && index + 1 < lines.Length)
                rest = lines[index + 1].Trim();
            return SplitException(rest);
        }
        return (null, null);
    }

    private static (string? Type, string? Message) SplitException(string text)
    {
        if (text.Length == 0)
            return (null, null);
        int colon = text.IndexOf(':');
        if (colon > 0)
        {
            string type = text[..colon].Trim();
            if (type.Length > 0 && type.IndexOf(' ') < 0)
                return (type, text[(colon + 1)..].Trim());
        }
        return (text, null);
    }

    /// <summary>Offline host for <see cref="FullCombatDriver"/>: kernel-only root and deployment.</summary>
    private sealed class OfflineHostCombat(
        CombatState combat,
        Player player,
        Rules rules,
        SearchPolicy policy,
        SearchPerformanceProfile profile,
        long MemoryBudgetBytes,
        int WarmupNodes,
        int? MaxExpandedNodesOverride,
        int? PoolStateBudgetMb,
        Battle opening) : IFullCombatHost
    {
        private readonly List<string> _potionsUsed = [];
        private bool _warmedUp;
        private bool _storageChecked;
        private SearchStorage? _roundStorage;
        private CancellationToken _roundToken;
        private readonly int _combatStartHp = opening.Player.CurrentHp;
        private readonly int _combatStartMaxHp = opening.Player.MaxHp;
        private Battle _current = opening;
        private ReplayedAction? _lastAction;
        private readonly PostCombatRelicHealProfile _postCombatHeal =
            ScoreCompiler.CompilePostCombatRelicHeal(player);

        /// <summary>
        /// Runs one short solve with the same code paths before the measured solve. Each offline root
        /// is a fresh process; this gives tiered JIT the call counts a warm game process already has.
        /// </summary>
        private void Warmup(SearchCase searchCase, CancellationToken token)
        {
            if (WarmupNodes <= 0)
                return;
            BeamOptions options = searchCase.Options with
            {
                BeamWidth = searchCase.Options.BeamWidth,
                MaxExpandedNodes = WarmupNodes,
                CandidateScanCount = 0,
                SearchMemoryBudgetBytes = 0,
                SoftTimeBudget = TimeSpan.MaxValue,
                Pacer = null,
                Progress = null,
                PollTakeover = null,
                Trace = null,
            };
            try
            {
                _ = new BeamSolver(searchCase.Rules, options)
                    .Solve(searchCase.Battles, token);
            }
            catch (CapacityException)
            {
                // A warm-up is best effort; the measured solve reports its own outcome.
            }
            catch (UnsupportedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        public ValueTask<FullCombatTurn?> WaitForTurnAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_current.Outcome != Outcome.None)
                return ValueTask.FromResult<FullCombatTurn?>(null);
            return ValueTask.FromResult<FullCombatTurn?>(new FullCombatTurn(
                _current.PlayerTurn, _current.Player.CurrentHp, _current.Player.MaxHp));
        }

        public ValueTask<FullCombatSearch> SearchAsync(int turn, string reason,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            SearchCase searchCase;
            try
            {
                searchCase = SearchRequestCompiler.CompileRequest(
                    combat, player, rules, _current, policy, EvaluationRootFacts.Default,
                    profile, deterministic: true, MemoryBudgetBytes);
            }
            catch (UnsupportedImportException error)
            {
                string detail = $"reason={error.Reason} {error.Detail}";
                return ValueTask.FromResult(Failed("rejected", detail, "import_failed", detail));
            }
            catch (UnsupportedException error)
            {
                return ValueTask.FromResult(Failed("rejected", error.Message,
                    "import_unsupported", error.Message));
            }
            catch (CapacityException error)
            {
                string detail = $"region={error.Region} {error.Message}";
                return ValueTask.FromResult(Failed("rejected", detail, "import_capacity", detail));
            }
            catch (InvalidDataException error)
            {
                return ValueTask.FromResult(Failed("rejected", error.Message,
                    "request_invalid", error.Message));
            }
            catch (InvalidOperationException error)
            {
                return ValueTask.FromResult(Failed("rejected", error.Message,
                    "request_invalid", error.Message));
            }

            if (!_warmedUp)
            {
                _warmedUp = true;
                Warmup(searchCase, token);
            }
            if (!_storageChecked && Environment.GetEnvironmentVariable("BLITZ_STORAGE_CHECK") == "1")
            {
                _storageChecked = true;
                StorageCheck(searchCase, token);
            }

            WideningSearchPlan plan = new(profile.StartWidth, profile.MaxWidth,
                profile.MaxExpandedNodesPerWidth, profile.TotalTimeBudgetMilliseconds,
                EnforceTimeBudget: false)
            {
                MaxExpandedNodesOverride = MaxExpandedNodesOverride ?? 0,
                PoolStateBudgetOverrideBytes = PoolStateBudgetMb is { } poolMb
                    ? checked(poolMb * 1024 * 1024) : 0,
                CombatStartPlayerHp = _combatStartHp,
                CombatStartPlayerMaxHp = _combatStartMaxHp,
            };
            SearchStorage storage = new();
            _roundStorage = storage;
            _roundToken = token;
            WideningSearchOutcome outcome;
            try
            {
                outcome = WideningSearch.RunAsync(searchCase, plan, storage, RunRound, token)
                    .AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (CapacityException error)
            {
                string detail = $"region={error.Region} {error.Message}";
                return ValueTask.FromResult(Failed("capacity", detail, "search_failed", detail));
            }
            catch (UnsupportedException error)
            {
                return ValueTask.FromResult(Failed("unsupported", error.Message, "search_failed",
                    error.Message));
            }
            catch (NotSupportedException error)
            {
                return ValueTask.FromResult(Failed("unsupported", error.Message, "search_failed",
                    error.Message));
            }
            catch (InvalidDataException error)
            {
                return ValueTask.FromResult(Failed("invalid_data", error.Message, "search_failed",
                    error.Message));
            }
            catch (InvalidOperationException error)
            {
                return ValueTask.FromResult(Failed("invalid_operation", error.Message,
                    "search_failed", error.Message));
            }

            _roundStorage = null;
            WideningRoundLog[] rounds = outcome.Rounds;
            if (outcome.Beam is not { } beam)
            {
                string status = outcome.FailureStatus.Length > 0 ? outcome.FailureStatus : "failed";
                string error = outcome.FailureError ?? "搜索没有返回路线";
                return ValueTask.FromResult(new FullCombatSearch(null, status,
                    outcome.ElapsedMs, outcome.TotalExpandedNodes, 0, false, error,
                    "search_failed", error, outcome.ManagedHeapPeakBytes,
                    outcome.WorkingSetPeakBytes, outcome.SoftTimeRisk,
                    outcome.MemoryBudgetExceeded, outcome.MemoryExceededAtBytes,
                    outcome.PoolCapacityDropped, outcome.SearchMemoryUsedBytes,
                    outcome.SearchMemoryPeakBytes, outcome.RouteNodeStorageBytes,
                    outcome.RouteNodePeakCount)
                {
                    Rounds = rounds,
                    RoundsRun = outcome.RoundsRun,
                    RoundsSkippedByTime = outcome.RoundsSkippedByTime,
                });
            }

            ReplayedRoute route;
            try
            {
                route = ReplayedRoute.Build(_current, rules, beam.BestRoute, beam.BestIsVictory);
            }
            catch (InvalidDataException error)
            {
                return ValueTask.FromResult(InvalidRoute(outcome, beam, "invalid", "route_invalid",
                    error.Message));
            }
            catch (InvalidOperationException error)
            {
                return ValueTask.FromResult(InvalidRoute(outcome, beam, "invalid", "route_invalid",
                    error.Message));
            }
            catch (UnsupportedException error)
            {
                return ValueTask.FromResult(InvalidRoute(outcome, beam, "invalid",
                    "route_unsupported", error.Message));
            }
            catch (CapacityException error)
            {
                string detail = $"region={error.Region} {error.Message}";
                return ValueTask.FromResult(InvalidRoute(outcome, beam, "invalid",
                    "route_capacity", detail));
            }

            return ValueTask.FromResult(new FullCombatSearch(route, "ok", outcome.ElapsedMs,
                outcome.TotalExpandedNodes, beam.BestRoute.Length, beam.BestIsVictory, null,
                null, null, outcome.ManagedHeapPeakBytes, outcome.WorkingSetPeakBytes,
                outcome.SoftTimeRisk, outcome.MemoryBudgetExceeded,
                outcome.MemoryExceededAtBytes, outcome.PoolCapacityDropped,
                outcome.SearchMemoryUsedBytes, outcome.SearchMemoryPeakBytes,
                outcome.RouteNodeStorageBytes, outcome.RouteNodePeakCount)
            {
                DamageFreeVictory = outcome.DamageFreeVictory,
                BestRound = outcome.BestRound,
                Rounds = rounds,
                RoundsRun = outcome.RoundsRun,
                RoundsSkippedByTime = outcome.RoundsSkippedByTime,
            });
        }

        private static FullCombatSearch InvalidRoute(WideningSearchOutcome outcome, BeamResult beam,
            string status, string reason, string detail)
            => new(null, status, outcome.ElapsedMs, outcome.TotalExpandedNodes,
                beam.BestRoute.Length, beam.BestIsVictory, detail, reason, detail,
                outcome.ManagedHeapPeakBytes, outcome.WorkingSetPeakBytes, outcome.SoftTimeRisk,
                outcome.MemoryBudgetExceeded, outcome.MemoryExceededAtBytes,
                outcome.PoolCapacityDropped, outcome.SearchMemoryUsedBytes,
                outcome.SearchMemoryPeakBytes, outcome.RouteNodeStorageBytes,
                outcome.RouteNodePeakCount)
            {
                Rounds = outcome.Rounds,
                RoundsRun = outcome.RoundsRun,
                RoundsSkippedByTime = outcome.RoundsSkippedByTime,
            };

        private ValueTask<WideningRoundMeasurement> RunRound(WideningRoundRequest request)
        {
            Stopwatch roundWatch = Stopwatch.StartNew();
            long managedPeak = 0;
            long workingSetPeak = 0;
            BeamResult beam;
            try
            {
                Task<BeamResult> solve = Task.Run(() =>
                    new BeamSolver(request.Case.Rules, request.Case.Options)
                        .Solve(request.Case.Battles, RoundStorage(), _roundToken));
                while (!solve.IsCompleted)
                {
                    SampleMemory(ref managedPeak, ref workingSetPeak);
                    Thread.Sleep(2);
                }
                SampleMemory(ref managedPeak, ref workingSetPeak);
                beam = solve.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (CapacityException error)
            {
                roundWatch.Stop();
                string detail = $"region={error.Region} {error.Message}";
                return ValueTask.FromResult(new WideningRoundMeasurement(null, "capacity", detail,
                    roundWatch.ElapsedMilliseconds, managedPeak, workingSetPeak));
            }
            catch (UnsupportedException error)
            {
                roundWatch.Stop();
                return ValueTask.FromResult(new WideningRoundMeasurement(null, "unsupported",
                    error.Message, roundWatch.ElapsedMilliseconds, managedPeak, workingSetPeak));
            }
            catch (NotSupportedException error)
            {
                roundWatch.Stop();
                return ValueTask.FromResult(new WideningRoundMeasurement(null, "unsupported",
                    error.Message, roundWatch.ElapsedMilliseconds, managedPeak, workingSetPeak));
            }
            catch (InvalidDataException error)
            {
                roundWatch.Stop();
                return ValueTask.FromResult(new WideningRoundMeasurement(null, "invalid_data",
                    error.Message, roundWatch.ElapsedMilliseconds, managedPeak, workingSetPeak));
            }
            catch (InvalidOperationException error)
            {
                roundWatch.Stop();
                return ValueTask.FromResult(new WideningRoundMeasurement(null,
                    "invalid_operation", error.Message, roundWatch.ElapsedMilliseconds, managedPeak,
                    workingSetPeak));
            }
            roundWatch.Stop();
            return ValueTask.FromResult(new WideningRoundMeasurement(beam, "ok", null,
                roundWatch.ElapsedMilliseconds, managedPeak, workingSetPeak));
        }

        private SearchStorage RoundStorage()
            => _roundStorage ?? throw new InvalidOperationException("多轮搜索没有建立共享存储。");

        private void StorageCheck(SearchCase searchCase, CancellationToken token)
        {
            SearchStorage storage = new();
            long gcStart = GC.GetAllocatedBytesForCurrentThread();
            BeamResult first = new BeamSolver(searchCase.Rules, searchCase.Options)
                .Solve(searchCase.Battles, storage, token);
            long firstBytes = storage.AllocatedBytes;
            string regionsFirst = RegionDump(storage);
            long gcFirst = GC.GetAllocatedBytesForCurrentThread();
            BeamResult second = new BeamSolver(searchCase.Rules, searchCase.Options)
                .Solve(searchCase.Battles, storage, token);
            long secondBytes = storage.AllocatedBytes;
            string regionsSecond = RegionDump(storage);
            long gcSecond = GC.GetAllocatedBytesForCurrentThread();
            bool same = SearchCodec.EncodeResult(first)
                .AsSpan().SequenceEqual(SearchCodec.EncodeResult(second));
            Console.WriteLine(
                $"STORAGE_CHECK same={same} firstBytes={firstBytes} " +
                $"secondBytes={secondBytes} secondDelta={secondBytes - firstBytes} " +
                $"nodesFirst={first.RouteNodePeakCount} nodesSecond={second.RouteNodePeakCount} " +
                $"gcFirst={gcFirst - gcStart} gcSecond={gcSecond - gcFirst} " +
                $"regionsFirst=[{regionsFirst}] regionsSecond=[{regionsSecond}]");
            if (!same)
                throw new InvalidDataException("同一请求复用存储两次的结果不同。");
        }

        private static string RegionDump(SearchStorage storage)
            => string.Join(',', storage.Memory.AllocatedByRegion
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}:{pair.Value}"));

        public ValueTask<FullCombatBoundary> CheckBoundaryAsync(ReplayedAction expected, int turn,
            int index, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new FullCombatBoundary(true,
                FullCombatAction.Describe(expected), []));
        }

        public void BeginDeployment(int turn)
        {
        }

        public ValueTask<FullCombatDeployment> DeployAsync(ReplayedAction action, int turn,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _current = action.After;
            return ValueTask.FromResult(new FullCombatDeployment(true, true, null, null,
                FullCombatAction.Describe(action), []));
        }

        public void OnActionDeployed(ReplayedAction action)
        {
            _lastAction = action;
            if (action.Action.Kind == PlayerActionKind.UsePotion)
            {
                ushort stored = action.Before.Potions[action.Action.PotionSlot];
                if (stored != 0)
                    _potionsUsed.Add(Registry.Potion((ushort)(stored - 1)).NativeId);
            }
        }

        public void OnRouteAccepted(ReplayedRoute route)
        {
        }

        public FullCombatFinal Final(bool cancelled)
        {
            string outcome;
            string status;
            if (_current.Outcome == Outcome.Victory)
            {
                outcome = "victory";
                status = "victory";
            }
            else if (_current.Outcome == Outcome.Defeat)
            {
                outcome = "defeat";
                status = "defeat";
            }
            else if (_current.Outcome == Outcome.EventDefeat)
            {
                outcome = _current.Player.IsDead ? "defeat" : "ended";
                status = outcome == "ended" ? "failed" : "defeat";
            }
            else
            {
                outcome = "in_progress";
                status = cancelled ? "cancelled" : "paused";
            }
            int playerHp = _current.Player.CurrentHp;
            if (_current.Outcome == Outcome.Victory && playerHp > 0)
            {
                // The native AfterCombatVictory relic pass heals the player before the final
                // full-combat snapshot. The kernel Battle is the pre-heal route boundary.
                playerHp += _postCombatHeal.HealFor(playerHp, _current.Player.MaxHp);
            }
            int turns = _current.PlayerTurn;
            // The native side switch increments PlayerCombatState.TurnNumber before the deferred
            // death check finishes, so a victory caused by an enemy-side turn-end effect (Doom and
            // similar) reports one more player turn than the kernel boundary. The flag comes from
            // the same kernel step, so the rule is not a per-root special case.
            if (_current.Outcome == Outcome.Victory
                && _lastAction is { EnemySideTurnEndReached: true })
            {
                turns++;
            }
            return new FullCombatFinal(outcome, status, turns,
                playerHp, _current.Player.MaxHp, [.. _potionsUsed]);
        }

        private FullCombatSearch Failed(string status, string error, string reason, string detail)
            => new(null, status, 0, 0, 0, false, error, reason, detail, 0, 0);
    }

    private static void SampleMemory(ref long managedPeak, ref long workingSetPeak)
    {
        long managed = GC.GetTotalMemory(forceFullCollection: false);
        if (managed > managedPeak)
            managedPeak = managed;
        long workingSet = Environment.WorkingSet;
        if (workingSet > workingSetPeak)
            workingSetPeak = workingSet;
    }
}
