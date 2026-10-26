using System.Diagnostics;
using System.Runtime.CompilerServices;
using BlitzSolver.Engine;
using BlitzSolver.Golden;
using BlitzSolver.Search;

// Pure-core Blitz golden verifier: Engine + Content + Search, with only the BCL as a dependency.

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}
try
{
    if (Environment.GetEnvironmentVariable("BLITZ_VERIFY_SEARCH_EQUIVALENCE") == "1")
        SearchVerificationHooks.VerifySearchEquivalence = true;
    switch (args[0])
    {
        case "k0":
            RulesCodecContract.Run();
            string magics = FormatContract.Run();
            Console.WriteLine($"K0 Passed fields={Rules.SchemaFieldCount} "
                + $"battleBytes={Unsafe.SizeOf<Battle>()} formatVersion={Codec.FormatVersion} "
                + $"magics={magics}");
            return 0;
        case "verify":
            return VerifyCommand.Run(args[1..]);
        case "compare":
            return CompareCommand.Run(args[1..]);
        case "refreeze":
            return RefreezeCommand.Run(args[1..]);
        case "rerecord":
            return RerecordCommand.Run(args[1..]);
        case "manifest":
            return ManifestCommand.Run(args[1..]);
        default:
            PrintUsage();
            return 2;
    }
}
catch (InvalidDataException error)
{
    Console.Error.WriteLine($"GOLDEN_INVALID {error.Message}");
    return 1;
}
catch (AggregateException error)
{
    AggregateException flattened = error.Flatten();
    Exception? first = flattened.InnerExceptions.FirstOrDefault();
    if (first is InvalidDataException invalid)
        Console.Error.WriteLine($"GOLDEN_INVALID {invalid.Message}");
    else
        Console.Error.WriteLine($"GOLDEN_FAILED {first?.GetType().Name}: {first?.Message}");
    return 1;
}

static void PrintUsage()
    => Console.Error.WriteLine(
        "用法：Golden verify <data-dir> [--jobs N] [--only d1|d2] [--shard i/n] | "
        + "compare <data-dir> [--jobs N] [--config none|play|choice|all|choice-fill|all-fill] "
        + "[--out csv] [--shard i/n] "
        + "[--only hash-prefix] [--verify] | refreeze <source-dir> <target-dir> [--jobs N] | "
        + "rerecord <source-dir> <target-dir> --roots <file> [--jobs N] [--reason text] | "
        + "manifest <data-dir> | k0");

internal static class VerifyCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("缺少数据目录。");
            return 2;
        }
        string dataDirectory = args[0];
        int jobs = Environment.ProcessorCount;
        string only = "all";
        int shard = 0, shards = 1;
        for (int index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--jobs":
                    jobs = ParsePositiveInt(Next(args, ref index, "--jobs"), "--jobs");
                    break;
                case "--only":
                    only = Next(args, ref index, "--only");
                    if (only is not ("d1" or "d2"))
                        throw new InvalidDataException($"--only 只能是 d1 或 d2，实际为 {only}。");
                    break;
                case "--shard":
                    (shard, shards) = ParseShard(Next(args, ref index, "--shard"));
                    break;
                default:
                    throw new InvalidDataException($"未知参数：{args[index]}");
            }
        }
        if (!Directory.Exists(dataDirectory))
            throw new InvalidDataException($"数据目录不存在：{dataDirectory}");

        ManifestSummary manifest = ManifestVerifier.Verify(dataDirectory);
        Console.WriteLine($"MANIFEST_OK files={manifest.DataFiles} totalBytes={manifest.TotalBytes} "
            + $"d1Files={manifest.D1Files} d1Roots={manifest.D1Roots} d1K1={manifest.D1K1} "
            + $"d1Walks={manifest.D1Walks} d1WalkStates={manifest.D1WalkStates} "
            + $"d1Transitions={manifest.D1Transitions} d2Requests={manifest.D2Requests} "
            + $"d2Expected={manifest.D2Expected}");

        D1Summary? d1 = null;
        D2Summary? d2 = null;
        if (only != "d2")
        {
            Stopwatch watch = Stopwatch.StartNew();
            d1 = D1Verifier.Run(dataDirectory, jobs, shard, shards);
            watch.Stop();
            Console.WriteLine($"D1_OK files={d1.Files} roots={d1.Roots} k1={d1.K1} walks={d1.Walks} "
                + $"states={d1.WalkStates} transitions={d1.Transitions} features={d1.FeatureStates} "
                + $"traits={d1.FeatureTraits} rules={d1.RulesObjects} mismatched=0 "
                + $"wall={watch.Elapsed.TotalSeconds:F1}s jobs={jobs}");
            Console.WriteLine($"D1_TIME wall={watch.Elapsed.TotalSeconds:F3}s jobs={jobs} "
                + $"files={d1.Files} states={d1.WalkStates} features={d1.FeatureStates}");
        }
        if (only != "d1")
        {
            Stopwatch watch = Stopwatch.StartNew();
            d2 = D2Verifier.Run(dataDirectory, jobs, shard, shards);
            watch.Stop();
            Console.WriteLine($"D2_OK selected={d2.Selected} equal={d2.Equal} mismatch={d2.Mismatch} "
                + $"expanded={d2.Expanded} prefixChecks={d2.PrefixChecks} threatChecks={d2.ThreatChecks} "
                + $"prefixShared={d2.PrefixShared} prefixUnshared={d2.PrefixUnshared} "
                + $"prefixReuse={d2.PrefixReuse} prefixChoice={d2.PrefixChoiceUnshared} "
                + $"prefixExtraTurn={d2.PrefixExtraTurnUnshared} prefixNotInProgress={d2.PrefixNotInProgress} "
                + $"playDeduped={d2.PlayDeduped} playVerifiedExact={d2.PlayVerifiedExact} "
                + $"playVerifiedApprox={d2.PlayVerifiedApprox} "
                + $"playVerifiedApproxEqual={d2.PlayVerifiedApproxEqual} "
                + $"playVerifiedUnresolved={d2.PlayVerifiedUnresolved} "
                + $"choiceByClassRequests={d2.ChoiceByClassRequests} "
                + $"choiceVerifiedExact={d2.ChoiceVerifiedExact} "
                + $"choiceVerifiedApprox={d2.ChoiceVerifiedApprox} "
                + $"choiceVerifiedApproxEqual={d2.ChoiceVerifiedApproxEqual} "
                + $"choiceVerifiedUnresolved={d2.ChoiceVerifiedUnresolved} "
                + $"wall={watch.Elapsed.TotalSeconds:F1}s jobs={jobs}");
            Console.WriteLine($"D2_TIME wall={watch.Elapsed.TotalSeconds:F3}s jobs={jobs} selected={d2.Selected}");
        }

        string totals = (d1, d2) switch
        {
            (not null, not null) => $"d1Files={d1.Files} d1Roots={d1.Roots} d1K1={d1.K1} "
                + $"d1Walks={d1.Walks} d1States={d1.WalkStates} d1Transitions={d1.Transitions} "
                + $"features={d1.FeatureStates} traits={d1.FeatureTraits} "
                + $"d2Selected={d2.Selected} d2Equal={d2.Equal} d2Mismatch={d2.Mismatch}",
            (not null, null) => $"d1Files={d1.Files} d1Roots={d1.Roots} d1K1={d1.K1} "
                + $"d1Walks={d1.Walks} d1States={d1.WalkStates} d1Transitions={d1.Transitions} "
                + $"features={d1.FeatureStates} traits={d1.FeatureTraits}",
            (null, not null) => $"d2Selected={d2.Selected} d2Equal={d2.Equal} d2Mismatch={d2.Mismatch}",
            _ => "",
        };
        Console.WriteLine($"GOLDEN_VERIFY {totals} only={only} shard={shard}/{shards} mismatched=0");
        return 0;
    }

    private static string Next(string[] args, ref int index, string name)
    {
        if (++index >= args.Length)
            throw new InvalidDataException($"{name} 缺少参数值。");
        return args[index];
    }

    private static int ParsePositiveInt(string text, string name)
    {
        if (!int.TryParse(text, out int value) || value <= 0)
            throw new InvalidDataException($"{name} 需要正整数，实际为 {text}。");
        return value;
    }

    private static (int Shard, int Shards) ParseShard(string text)
    {
        string[] parts = text.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int shard)
            || !int.TryParse(parts[1], out int shards)
            || shards <= 0 || shard < 0 || shard >= shards)
            throw new InvalidDataException($"--shard 需要 i/n 且 0 <= i < n，实际为 {text}。");
        return (shard, shards);
    }
}
