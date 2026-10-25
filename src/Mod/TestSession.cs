using System.Diagnostics;
using System.Text.Json;
using BlitzSolver.Engine;
using BlitzSolver.Import;
using BlitzSolver.TestHost;
using BlitzSolver.Search;
using BlitzSolver.Search.Scoring;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace BlitzSolver;

/// <summary>
/// 无头测试模式：按文件名顺序依次处理 requests/*.json，每个请求建局、注入、等到可出牌，
/// 在主线程导入并把 BATL/RULE/SREQ 与原生状态转储写到 results/&lt;name&gt;/。
/// </summary>
internal static class TestSession
{
    public const string DirectoryVariable = "BLITZ_TEST_DIR";

    private static bool _started;

    public static bool Active { get; private set; }

    public static void Start()
    {
        if (_started || !ModEntry.Enabled || !ModEntry.TestMode)
            return;
        _started = true;
        string? configured = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            ModLog.Error("[BlitzSolver] TEST_NOT_STARTED reason=missing_BLITZ_TEST_DIR");
            return;
        }
        string root = Path.GetFullPath(configured);
        string requestDirectory = Path.Combine(root, "requests");
        if (!Directory.Exists(requestDirectory))
        {
            ModLog.Error($"[BlitzSolver] TEST_NOT_STARTED reason=missing_requests directory={requestDirectory}");
            return;
        }
        Directory.CreateDirectory(Path.Combine(root, "results"));
        NGame? host = NGame.Instance;
        if (host == null)
        {
            ModLog.Error("[BlitzSolver] TEST_NOT_STARTED reason=NGame_instance_missing");
            return;
        }
        Active = true;
        ModLog.Info($"[BlitzSolver] TEST_STARTED requests={requestDirectory}");
        _ = RunAsync(host, root, requestDirectory);
    }

    public static void OnCombatEnded()
    {
        if (Active)
            ModLog.Info("[BlitzSolver] COMBAT_ENDED test_session_active=true");
    }

    private static async Task RunAsync(NGame host, string testRoot, string requestDirectory)
    {
        string resultRoot = Path.Combine(testRoot, "results");
        await WaitForMainMenuReadyAsync(host);
        ModLog.Info("[BlitzSolver] TEST_MAIN_MENU_READY");
        string[] files = Directory.GetFiles(requestDirectory, "*.json")
            .Where(path => !string.Equals(Path.GetFileName(path), "manifest.json", StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        List<ResultEntry> results = [];
        Stopwatch total = Stopwatch.StartNew();
        int ok = 0;
        int failed = 0;
        foreach (string file in files)
        {
            string root = Path.GetFileNameWithoutExtension(file);
            string resultDirectory = Path.Combine(resultRoot, root);
            Directory.CreateDirectory(resultDirectory);
            Stopwatch stopwatch = Stopwatch.StartNew();
            string stage = "load";
            try
            {
                OfflineRequest request = JsonSerializer.Deserialize<OfflineRequest>(
                    File.ReadAllText(file), OfflineJson.Options)
                    ?? throw new InvalidDataException("请求为空。");
                Validate(request);
                FirstChoiceCardSelector.LastSelections.Clear();
                if (request.FlashTurnSetupRootForTest)
                    TurnSetupRootCapture.BeginBuild();
                stage = "build";
                ModLog.Info($"[BlitzSolver] BUILD_STARTED root={root}");
                CombatState combat = await ScenarioBuilder.BuildAsync(
                    new GodotCombatWaiter(host), request,
                    skipInitialInjection: request.FlashTurnSetupRootForTest);
                ModLog.Info($"[BlitzSolver] BUILD_COMPLETED root={root}");
                stage = "settle";
                await GodotCombatWaiter.SettleAsync(host);
                stage = "capture";
                Capture(resultDirectory, root, request, combat, stopwatch.ElapsedMilliseconds);
                ok++;
                results.Add(new ResultEntry(root, "ok", stage, stopwatch.ElapsedMilliseconds, null,
                    [.. FirstChoiceCardSelector.LastSelections], request.FlashTurnSetupRootForTest));
                ModLog.Info($"[BlitzSolver] CAPTURE_OK root={root} ms={stopwatch.ElapsedMilliseconds}");
            }
            catch (UnsupportedImportException error)
            {
                failed++;
                results.Add(Failure(root, stage, stopwatch, $"unsupported reason={error.Reason} {error.Detail}", null, false));
                ModLog.Error($"[BlitzSolver] CAPTURE_FAILED root={root} stage={stage} unsupported reason={error.Reason} {error.Detail}");
            }
            catch (UnsupportedException error)
            {
                failed++;
                results.Add(Failure(root, stage, stopwatch, error.Message, null, false));
                ModLog.Error($"[BlitzSolver] CAPTURE_FAILED root={root} stage={stage} unsupported {error.Message}");
            }
            catch (CapacityException error)
            {
                failed++;
                results.Add(Failure(root, stage, stopwatch, $"capacity region={error.Region} {error.Message}", null, false));
                ModLog.Error($"[BlitzSolver] CAPTURE_FAILED root={root} stage={stage} capacity region={error.Region} {error.Message}");
            }
            catch (NotSupportedException error)
            {
                failed++;
                results.Add(Failure(root, stage, stopwatch, error.Message, null, false));
                ModLog.Error($"[BlitzSolver] CAPTURE_FAILED root={root} stage={stage} unsupported {error.Message}");
            }
            catch (InvalidDataException error)
            {
                failed++;
                results.Add(Failure(root, stage, stopwatch, error.Message, null, false));
                ModLog.Error($"[BlitzSolver] CAPTURE_FAILED root={root} stage={stage} invalid_data {error.Message}");
            }
            catch (InvalidOperationException error)
            {
                failed++;
                results.Add(Failure(root, stage, stopwatch, error.Message, null, false));
                ModLog.Error($"[BlitzSolver] CAPTURE_FAILED root={root} stage={stage} invalid_operation {error.Message}");
            }
            catch (TimeoutException error)
            {
                failed++;
                results.Add(Failure(root, stage, stopwatch, error.Message, null, false));
                ModLog.Error($"[BlitzSolver] CAPTURE_FAILED root={root} stage={stage} timeout {error.Message}");
            }
            catch (OperationCanceledException error)
            {
                failed++;
                results.Add(Failure(root, stage, stopwatch, error.Message, null, false));
                ModLog.Error($"[BlitzSolver] CAPTURE_FAILED root={root} stage={stage} canceled {error.Message}");
            }
            catch (IOException error)
            {
                failed++;
                results.Add(Failure(root, stage, stopwatch, error.Message, null, false));
                ModLog.Error($"[BlitzSolver] CAPTURE_FAILED root={root} stage={stage} io {error.Message}");
            }
            catch (JsonException error)
            {
                failed++;
                results.Add(Failure(root, stage, stopwatch, error.Message, null, false));
                ModLog.Error($"[BlitzSolver] CAPTURE_FAILED root={root} stage={stage} json {error.Message}");
            }
            catch (UnauthorizedAccessException error)
            {
                failed++;
                results.Add(Failure(root, stage, stopwatch, error.Message, null, false));
                ModLog.Error($"[BlitzSolver] CAPTURE_FAILED root={root} stage={stage} access {error.Message}");
            }

            bool breakAfterCleanup = false;
            try
            {
                await ResetToMainMenuAsync(host);
            }
            catch (TimeoutException error)
            {
                ModLog.Error($"[BlitzSolver] RESET_FAILED root={root} timeout {error.Message}");
                breakAfterCleanup = true;
            }
            catch (InvalidOperationException error)
            {
                ModLog.Error($"[BlitzSolver] RESET_FAILED root={root} invalid_operation {error.Message}");
                breakAfterCleanup = true;
            }
            if (breakAfterCleanup)
            {
                failed++;
                results.Add(Failure(root + "-cleanup", "cleanup", stopwatch,
                    "返回主菜单失败，后续请求不再执行", null, false));
                break;
            }
        }
        total.Stop();
        DoneEntry done = new(1, files.Length, ok, failed, total.Elapsed.TotalMilliseconds, [.. results]);
        WriteJson(Path.Combine(resultRoot, "done.json"), done);
        ModLog.Info(
            $"[BlitzSolver] TEST_DONE requests={files.Length} ok={ok} failed={failed} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:0}");
        Active = false;
        host.GetTree().Quit(failed == 0 ? 0 : 1);
    }

    private static void Capture(string directory, string root, OfflineRequest request,
        CombatState combat, long elapsedMilliseconds)
    {
        Player player = LocalContext.GetMe(combat)
            ?? throw new InvalidOperationException("找不到本地玩家。");
        if (combat.RunState is not RunState runState)
            throw new InvalidDataException("原生战斗的 RunState 不是具体 RunState。");
        Rules rules = RulesCompiler.Compile(combat, player, runState);
        ImportOptions importOptions = request.FlashTurnSetupRootForTest
            ? ImportOptions.Default with
            {
                TurnSetupRoot = true,
                RootCards = TurnSetupRootCapture.StartCards
                    ?? throw new InvalidDataException("回合准备根缺少 Start 卡牌集合。"),
            }
            : ImportOptions.Default;
        Battle battle = BattleImporter.Import(combat, player, rules, importOptions);
        byte[] battleBytes = BattleCodec.Encode(battle);
        byte[] rulesBytes = RulesCodec.Encode(rules);
        SearchPolicy policy = SearchPolicyOverrides.ApplyOverrides(
            SearchPolicyCapture.Capture(combat, player), request, player);
        EvaluationRootFacts facts = EvaluationRootFacts.Default with
        {
            InitialActionCount = 0,
            TurnSetupRoot = request.FlashTurnSetupRootForTest,
        };
        SearchCase searchCase = EvaluationConfigCompiler.CompileRequest(
            combat, player, rules, battle, policy, facts);
        byte[] requestBytes = SearchCodec.EncodeRequest(searchCase);
        WriteBytes(Path.Combine(directory, "BATL"), battleBytes);
        WriteBytes(Path.Combine(directory, "RULE"), rulesBytes);
        WriteBytes(Path.Combine(directory, "SREQ"), requestBytes);
        NativeStateDump.Write(combat, Path.Combine(directory, "native.txt"));
        WriteJson(Path.Combine(directory, "status.json"), new StatusEntry(
            1, root, "ok", "capture", elapsedMilliseconds, null,
            [.. FirstChoiceCardSelector.LastSelections], request.FlashTurnSetupRootForTest, 0));
    }

    private static ResultEntry Failure(string root, string stage, Stopwatch stopwatch,
        string error, string[]? selections, bool turnSetupRoot)
    {
        return new ResultEntry(root, "failed", stage, stopwatch.ElapsedMilliseconds, error,
            selections ?? [], turnSetupRoot);
    }

    private static void Validate(OfflineRequest request)
    {
        if (request.SchemaVersion != 1)
            throw new NotSupportedException($"不支持的请求 schemaVersion：{request.SchemaVersion}。");
        if (request.FlashPrefixActions.Length != 0 || request.FlashPrefixPlan.Length != 0)
            throw new NotSupportedException("本批实机捕获只支持普通根；前缀根请用离线宿主。");
        if (request.GeneratedScenarioPath is { Length: > 0 } path && !Path.IsPathRooted(path))
            throw new NotSupportedException($"generatedScenarioPath 必须是绝对路径：{path}");
    }

    private static async Task WaitForMainMenuReadyAsync(NGame host)
    {
        long deadline = Environment.TickCount64 + 120_000;
        while (Environment.TickCount64 < deadline)
        {
            if (host.MainMenu != null && !RunManager.Instance.IsInProgress)
            {
                await GodotCombatWaiter.NextFrameAsync(host);
                await GodotCombatWaiter.NextFrameAsync(host);
                return;
            }
            await GodotCombatWaiter.NextFrameAsync(host);
        }
        throw new TimeoutException("等待主菜单就绪超时。");
    }

    private static async Task ResetToMainMenuAsync(NGame host)
    {
        if (RunManager.Instance.IsInProgress)
            await host.ReturnToMainMenu();
        long deadline = Environment.TickCount64 + 60_000;
        while (Environment.TickCount64 < deadline)
        {
            if (!RunManager.Instance.IsInProgress
                && host.MainMenu != null
                && CombatManager.Instance.DebugOnlyGetState() == null)
            {
                return;
            }
            await GodotCombatWaiter.NextFrameAsync(host);
        }
        throw new TimeoutException("返回主菜单超时。");
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
    }

    private static void WriteJson<T>(string path, T value)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, OfflineJson.Options));
        File.Move(temporary, path, true);
    }

    private sealed record StatusEntry(
        int SchemaVersion,
        string Root,
        string Status,
        string Stage,
        long ElapsedMs,
        string? Error,
        string[] SetupSelections,
        bool TurnSetupRoot,
        int InitialActionCount);

    private sealed record ResultEntry(
        string Root,
        string Status,
        string Stage,
        long ElapsedMs,
        string? Error,
        string[] SetupSelections,
        bool TurnSetupRoot);

    private sealed record DoneEntry(
        int SchemaVersion,
        int Requests,
        int Ok,
        int Failed,
        double TotalMs,
        ResultEntry[] Results);
}
