// OfflineHost entry point. Builds the native CombatState for frozen snapshot inputs and writes
// a deterministic state dump; also compares a dump directory against a reference directory.
using System.Diagnostics;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;

namespace OfflineHost;

internal static class Program
{
    private const string DefaultData = "tests/Golden/data";
    private const string DefaultCorpora = "tests/corpora";
    private const string DefaultOutput = ".local/offlinehost/out";

    private static Task<int> Main(string[] args)
    {
        try
        {
            return Task.FromResult(Run(args));
        }
        catch (UnsupportedException error)
        {
            Console.Error.WriteLine($"UNSUPPORTED: {error.Message}");
            return Task.FromResult(3);
        }
        catch (NotSupportedException error)
        {
            Console.Error.WriteLine($"UNSUPPORTED: {error.Message}");
            return Task.FromResult(3);
        }
        catch (InvalidDataException error)
        {
            Console.Error.WriteLine($"INVALID_INPUT: {error.Message}");
            return Task.FromResult(2);
        }
        catch (TimeoutException error)
        {
            Console.Error.WriteLine($"TIMEOUT: {error.Message}");
            return Task.FromResult(4);
        }
    }

    private static int Run(string[] args)
    {
        string data = DefaultData;
        string corpora = DefaultCorpora;
        string output = DefaultOutput;
        string? root = null;
        string? compareDir = null;
        string? compareOnlyNew = null;
        string? compareOnlyRef = null;
        bool all = false;
        int jobs = 1;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--all":
                    all = true;
                    break;
                case "--root" when index + 1 < args.Length:
                    root = args[++index];
                    break;
                case "--data" when index + 1 < args.Length:
                    data = args[++index];
                    break;
                case "--corpora" when index + 1 < args.Length:
                    corpora = args[++index];
                    break;
                case "--out" when index + 1 < args.Length:
                    output = args[++index];
                    break;
                case "--jobs" when index + 1 < args.Length:
                    jobs = int.Parse(args[++index]);
                    break;
                case "--compare" when index + 1 < args.Length:
                    compareDir = args[++index];
                    break;
                case "--compare-only" when index + 2 < args.Length:
                    compareOnlyNew = args[++index];
                    compareOnlyRef = args[++index];
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"未知参数：{args[index]}");
                    PrintUsage();
                    return 2;
            }
        }

        if (compareOnlyNew != null)
            return Compare(compareOnlyNew, compareOnlyRef!, roots: null);

        if (!all && root == null)
        {
            Console.Error.WriteLine("需要 --root <name>、--all，或 --compare-only <new> <ref>。");
            return 2;
        }
        if (jobs is < 1 or > 8)
        {
            Console.Error.WriteLine("--jobs 必须在 1 到 8 之间。");
            return 2;
        }
        Directory.CreateDirectory(output);

        if (root != null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int code = BuildInProcess(data, corpora, output, root);
            stopwatch.Stop();
            if (code != 0)
                return code;
            Console.WriteLine($"root={root} ms={stopwatch.Elapsed.TotalMilliseconds:0}");
            return compareDir == null ? 0 : Compare(output, compareDir, [root]);
        }

        string[] roots = EnumerateRoots(data);
        Console.WriteLine($"roots={roots.Length} jobs={jobs} out={output}");
        int failures = 0;
        long totalMilliseconds = 0;
        object gate = new();
        Parallel.ForEach(
            roots,
            new ParallelOptions { MaxDegreeOfParallelism = jobs },
            name =>
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                int code = BuildChild(data, corpora, output, name);
                stopwatch.Stop();
                lock (gate)
                {
                    totalMilliseconds += (long)stopwatch.Elapsed.TotalMilliseconds;
                    if (code != 0)
                        failures++;
                }
                Console.WriteLine($"root={name} ms={stopwatch.Elapsed.TotalMilliseconds:0} status={(code == 0 ? "ok" : "fail")}");
            });
        Console.WriteLine($"done roots={roots.Length} failures={failures} sum_ms={totalMilliseconds}");
        int compareCode = compareDir == null ? 0 : Compare(output, compareDir, roots);
        return failures == 0 ? compareCode : 1;
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

    private static int BuildChild(string data, string corpora, string output, string root)
    {
        ProcessStartInfo start = new("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--root");
        start.ArgumentList.Add(root);
        start.ArgumentList.Add("--data");
        start.ArgumentList.Add(data);
        start.ArgumentList.Add("--corpora");
        start.ArgumentList.Add(corpora);
        start.ArgumentList.Add("--out");
        start.ArgumentList.Add(output);
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动子进程。");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"root={root} exit={process.ExitCode}");
            if (stderr.Length > 0)
                Console.Error.WriteLine(stderr.TrimEnd());
        }
        else if (stdout.Length > 0)
        {
            Console.WriteLine(stdout.TrimEnd());
        }
        return process.ExitCode;
    }

    private static int BuildInProcess(string data, string corpora, string output, string root)
    {
        OfflineRequest request = LoadRequest(data, corpora, root);
        string stage = "init";
        try
        {
            MainLoopContext loop = new();
            SynchronizationContext.SetSynchronizationContext(loop);
            GameBootstrap.ApplyGodotBypasses();
            GameBootstrap.SkipGodotNodeStaticConstructors();
            GameBootstrap.InitializeStaticState();
            stage = "build";
            Task<CombatState> build = ScenarioBuilder.BuildAsync(loop, request);
            loop.RunUntilCompleted(build, TimeSpan.FromSeconds(600), $"建局 {root}");
            CombatState combat = build.GetAwaiter().GetResult();
            stage = "dump";
            string path = Path.Combine(output, root + ".native.txt");
            NativeStateDump.Write(combat, path);
            Console.WriteLine($"root={root} dump={path}");
            return 0;
        }
        catch (UnsupportedException error)
        {
            Console.Error.WriteLine($"root={root} stage={stage} UNSUPPORTED {error.Message}");
            return 3;
        }
        catch (NotSupportedException error)
        {
            Console.Error.WriteLine($"root={root} stage={stage} UNSUPPORTED {error.Message}");
            return 3;
        }
        catch (InvalidDataException error)
        {
            Console.Error.WriteLine($"root={root} stage={stage} INVALID_INPUT {error.Message}");
            return 2;
        }
        catch (TimeoutException error)
        {
            Console.Error.WriteLine($"root={root} stage={stage} TIMEOUT {error.Message}");
            return 4;
        }
        catch (InvalidOperationException error)
        {
            Console.Error.WriteLine($"root={root} stage={stage} FAILED {error.Message}");
            return 5;
        }
    }

    private static OfflineRequest LoadRequest(string data, string corpora, string root)
    {
        string metaPath = Path.Combine(data, "d1", root, "meta.json");
        using JsonDocument meta = JsonDocument.Parse(File.ReadAllText(metaPath));
        JsonElement inputElement = meta.RootElement.GetProperty("input");
        string inputText = inputElement.ValueKind == JsonValueKind.String
            ? inputElement.GetString() ?? throw new InvalidDataException($"{metaPath} 的 input 为空。")
            : inputElement.GetRawText();
        using JsonDocument inputDocument = JsonDocument.Parse(inputText);
        JsonElement input = inputDocument.RootElement;
        string kind = input.GetProperty("kind").GetString()
            ?? throw new InvalidDataException($"{metaPath} 的 input.kind 为空。");

        OfflineRequest request;
        if (kind == "fixture")
        {
            string sourcePath = input.GetProperty("fixture").GetString()
                ?? throw new InvalidDataException($"{metaPath} 的 input.fixture 为空。");
            string expectedName = "flash-" + root + ".json";
            if (!string.Equals(Path.GetFileName(sourcePath), expectedName, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"{metaPath} 的 fixture 文件名 {Path.GetFileName(sourcePath)} 与根名 {root} 不对应（预期 {expectedName}）。");
            string fixturePath = Path.Combine(corpora, "fixtures", root + ".json");
            if (!File.Exists(fixturePath))
                throw new FileNotFoundException($"找不到 fixture：{fixturePath}", fixturePath);
            request = JsonSerializer.Deserialize<OfflineRequest>(
                File.ReadAllText(fixturePath), OfflineJson.Options)
                ?? throw new InvalidDataException($"fixture 为空：{fixturePath}。");
            if (request.GeneratedScenarioPath is { Length: > 0 } generatedPath)
            {
                string specName = Path.GetFileName(generatedPath);
                string specPath = Path.Combine(corpora, "fixtures", "specs", specName);
                if (!File.Exists(specPath))
                    throw new FileNotFoundException(
                        $"fixture {root} 引用的生成场景配置不存在：{generatedPath}（本地查找 {specPath}）。",
                        specPath);
                request = request with { GeneratedScenarioPath = specPath };
            }
        }
        else if (kind == "quality")
        {
            string name = root.StartsWith("quality-", StringComparison.Ordinal)
                ? root["quality-".Length..]
                : throw new InvalidDataException($"quality 根名不以 quality- 开头：{root}。");
            string specSource = input.GetProperty("spec").GetString()
                ?? throw new InvalidDataException($"{metaPath} 的 input.spec 为空。");
            if (!specSource.EndsWith("/" + name + "/spec.json", StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"{metaPath} 的 spec 路径 {specSource} 与根名 {root} 不对应。");
            string directory = Path.Combine(corpora, "quality", name);
            string requestPath = Path.Combine(directory, "request.json");
            string specPath = Path.Combine(directory, "spec.json");
            if (!File.Exists(requestPath) || !File.Exists(specPath))
                throw new FileNotFoundException($"质量语料不完整：{directory}");
            request = JsonSerializer.Deserialize<OfflineRequest>(
                File.ReadAllText(requestPath), OfflineJson.Options)
                ?? throw new InvalidDataException($"quality request 为空：{requestPath}。");
            request = request with { GeneratedScenarioPath = specPath };
        }
        else
        {
            throw new InvalidDataException($"不支持的 input.kind：{kind}。");
        }
        return request;
    }

    private static int Compare(string newDirectory, string referenceDirectory, IReadOnlyList<string>? roots)
    {
        string[] names = roots?.ToArray()
            ?? Directory.EnumerateFiles(newDirectory, "*.native.txt")
                .Select(path => Path.GetFileName(path)[..^".native.txt".Length])
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        int mismatches = 0;
        foreach (string name in names)
        {
            string newPath = Path.Combine(newDirectory, name + ".native.txt");
            string referencePath = Path.Combine(referenceDirectory, name + ".native.txt");
            if (!File.Exists(newPath))
            {
                Console.Error.WriteLine($"root={name} MISSING_NEW {newPath}");
                mismatches++;
                continue;
            }
            if (!File.Exists(referencePath))
            {
                Console.Error.WriteLine($"root={name} MISSING_REFERENCE {referencePath}");
                mismatches++;
                continue;
            }
            string[] actual = File.ReadAllText(newPath).Split('\n');
            string[] expected = File.ReadAllText(referencePath).Split('\n');
            int shared = Math.Min(actual.Length, expected.Length);
            int first = -1;
            for (int index = 0; index < shared; index++)
            {
                if (!string.Equals(actual[index], expected[index], StringComparison.Ordinal))
                {
                    first = index;
                    break;
                }
            }
            if (first < 0 && actual.Length != expected.Length)
                first = shared;
            if (first < 0)
                continue;
            mismatches++;
            string expectedLine = first < expected.Length ? expected[first] : "<eof>";
            string actualLine = first < actual.Length ? actual[first] : "<eof>";
            Console.Error.WriteLine(
                $"root={name} MISMATCH line={first + 1} expected=[{expectedLine}] actual=[{actualLine}]");
        }
        Console.WriteLine($"compare roots={names.Length} mismatches={mismatches} reference={referenceDirectory}");
        return mismatches == 0 ? 0 : 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            用法：
              dotnet OfflineHost.dll --root <name> [--out <dir>] [--data <dir>] [--corpora <dir>]
              dotnet OfflineHost.dll --all [--jobs 1..8] [--out <dir>]
              dotnet OfflineHost.dll --compare-only <newDir> <referenceDir>
              dotnet OfflineHost.dll --all --compare <referenceDir>
            """);
    }
}
