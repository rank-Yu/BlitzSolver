using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CombatSolver.Flash.Kernel;

// Pure-core Flash golden verifier: Kernel + Content only. It proves that the exported D1 data is
// reproducible from the Flash source alone: K1 completions, K2 walk steps, legal action sets and
// choice requests are replayed and compared before any host/native state is involved.

if (args.Length == 0)
{
    Console.Error.WriteLine("用法：FlashGolden verify <golden-dir> | k0");
    return 2;
}
try
{
    switch (args[0])
    {
        case "k0":
            FlashRulesCodecContract.Run();
            Console.WriteLine($"K0 Passed fields={FlashRules.SchemaFieldCount} battleBytes={Unsafe.SizeOf<FlashBattle>()}");
            return 0;
        case "verify" when args.Length == 2:
            return Verify(args[1]);
        default:
            Console.Error.WriteLine("用法：FlashGolden verify <golden-dir> | k0");
            return 2;
    }
}
catch (InvalidDataException error)
{
    Console.Error.WriteLine($"FLASH_GOLDEN_INVALID {error.Message}");
    return 1;
}

static int Verify(string directory)
{
    FlashRulesCodecContract.Run();
    string rootDirectory = Path.Combine(directory, "d1");
    if (!Directory.Exists(rootDirectory))
        throw new InvalidDataException($"D1 目录不存在：{rootDirectory}");
    SortedDictionary<string, long> rulesCache = new(StringComparer.Ordinal);
    int rootsChecked = 0, k1Checked = 0, walksChecked = 0, statesChecked = 0, transitionsChecked = 0;
    int files = 0;
    foreach (string rootPath in Directory.EnumerateDirectories(rootDirectory)
                 .OrderBy(static path => path, StringComparer.Ordinal))
    {
        string rootName = Path.GetFileName(rootPath);
        string goldenPath = Path.Combine(rootPath, "golden.br");
        string metaPath = Path.Combine(rootPath, "meta.json");
        if (!File.Exists(goldenPath) || !File.Exists(metaPath))
            throw new InvalidDataException($"根 {rootName} 缺少 golden.br 或 meta.json。");
        using JsonDocument meta = JsonDocument.Parse(File.ReadAllText(metaPath, Encoding.UTF8));
        JsonElement metaRoot = meta.RootElement;
        if (metaRoot.GetProperty("formatVersion").GetInt32() != FlashCodec.FormatVersion)
            throw new InvalidDataException($"根 {rootName} 的格式版本不符。");
        if (metaRoot.GetProperty("battleSize").GetInt32() != Unsafe.SizeOf<FlashBattle>())
            throw new InvalidDataException($"根 {rootName} 的 FlashBattle 大小不符。");
        FlashGoldenRootRecord record = FlashGoldenData.Decode(ReadCompressed(goldenPath));
        files++;
        FlashRules rules = LoadRules(directory, record.RulesSha256, rulesCache);
        if (record.Roots.Count == 0)
            throw new InvalidDataException($"根 {rootName} 没有 Flash 根。");
        JsonElement counts = metaRoot.GetProperty("counts");
        if (counts.GetProperty("roots").GetInt32() != record.Roots.Count
            || counts.GetProperty("k1").GetInt32() != record.K1.Count
            || counts.GetProperty("walks").GetInt32() != record.Walks.Count)
            throw new InvalidDataException($"根 {rootName} 的 meta 计数与 golden 文件不符。");
        int walkStates = 0, transitions = 0;
        foreach (FlashGoldenK1Action entry in record.K1)
        {
            if ((uint)entry.RootIndex >= (uint)record.Roots.Count)
                throw new InvalidDataException($"根 {rootName} 的 K1 根下标 {entry.RootIndex} 越界。");
            VerifyK1(rootName, record.Roots[entry.RootIndex].Battle, entry, rules);
            k1Checked++;
        }
        foreach (FlashGoldenWalk walk in record.Walks)
        {
            if ((uint)walk.RootIndex >= (uint)record.Roots.Count)
                throw new InvalidDataException($"根 {rootName} 的游走根下标 {walk.RootIndex} 越界。");
            VerifyWalk(rootName, walk, record.Roots[walk.RootIndex].Battle, rules);
            walksChecked++;
            walkStates += walk.States.Count;
            transitions += walk.States.Count(static state => state.HasStep);
        }
        if (counts.GetProperty("walkStates").GetInt32() != walkStates
            || counts.GetProperty("transitions").GetInt32() != transitions)
            throw new InvalidDataException($"根 {rootName} 的游走计数与 meta 不符。");
        statesChecked += walkStates;
        transitionsChecked += transitions;
        rootsChecked += record.Roots.Count;
        Console.WriteLine($"ROOT {rootName} roots={record.Roots.Count} k1={record.K1.Count} walks={record.Walks.Count} "
            + $"states={walkStates} transitions={transitions} OK");
    }
    string manifestPath = Path.Combine(directory, "manifest.json");
    if (!File.Exists(manifestPath))
        throw new InvalidDataException($"缺少 manifest.json：{manifestPath}");
    using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
    JsonElement manifestRoot = manifest.RootElement;
    if (manifestRoot.GetProperty("formatVersion").GetInt32() != FlashCodec.FormatVersion
        || manifestRoot.GetProperty("flashBattleSize").GetInt32() != Unsafe.SizeOf<FlashBattle>())
        throw new InvalidDataException("manifest.json 的格式版本或 FlashBattle 大小不符。");
    JsonElement manifestCounts = manifestRoot.GetProperty("counts").GetProperty("d1");
    if (manifestCounts.GetProperty("roots").GetInt32() != rootsChecked
        || manifestCounts.GetProperty("k1").GetInt32() != k1Checked
        || manifestCounts.GetProperty("walkStates").GetInt32() != statesChecked
        || manifestCounts.GetProperty("transitions").GetInt32() != transitionsChecked)
        throw new InvalidDataException("manifest.json 的 D1 计数与校验结果不符。");
    long declaredBytes = 0;
    int manifestFiles = 0;
    foreach (JsonElement entry in manifestRoot.GetProperty("files").EnumerateArray())
    {
        string relative = entry.GetProperty("path").GetString()
            ?? throw new InvalidDataException("manifest.json 文件条目缺少 path。");
        string path = Path.Combine(directory, relative);
        if (!File.Exists(path))
            throw new InvalidDataException($"manifest.json 列出的文件不存在：{relative}");
        byte[] data = File.ReadAllBytes(path);
        long size = entry.GetProperty("bytes").GetInt64();
        string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        if (data.Length != size || hash != entry.GetProperty("sha256").GetString())
            throw new InvalidDataException($"manifest.json 文件大小或 SHA-256 不符：{relative}");
        declaredBytes += size;
        manifestFiles++;
    }
    if (declaredBytes != manifestRoot.GetProperty("totalBytes").GetInt64())
        throw new InvalidDataException("manifest.json 的 totalBytes 与文件合计不符。");
    Console.WriteLine($"FLASH_GOLDEN_VERIFY roots={rootsChecked} k1={k1Checked} walks={walksChecked} "
        + $"states={statesChecked} transitions={transitionsChecked} mismatched=0 files={files} "
        + $"manifestFiles={manifestFiles} totalBytes={declaredBytes}");
    return 0;
}

static FlashRules LoadRules(string directory, string hash, SortedDictionary<string, long> cache)
{
    if (cache.ContainsKey(hash))
        return FlashRulesCache.Get(hash);
    string path = Path.Combine(directory, "objects", "rules", hash + ".br");
    if (!File.Exists(path))
        throw new InvalidDataException($"缺少规则对象 {hash}。");
    byte[] bytes = ReadCompressed(path);
    string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    if (actual != hash)
        throw new InvalidDataException($"规则对象 {hash} 的 SHA-256 是 {actual}。");
    FlashRules rules = FlashRulesCodec.Decode(bytes);
    FlashRulesCache.Add(hash, rules);
    cache[hash] = bytes.Length;
    return rules;
}

static void VerifyK1(string rootName, in FlashBattle root, FlashGoldenK1Action entry, FlashRules rules)
{
    FlashAction action = entry.BaseAction;
    for (int index = 0; index < entry.Requests.Length; index++)
    {
        FlashBattle partial = root;
        FlashStepResult result = FlashKernel.Step(ref partial, action, rules,
            out FlashChoiceRequest request, out _);
        if (result != FlashStepResult.NeedsChoice)
            throw Mismatch(rootName, $"K1 root={entry.RootIndex} choice[{index}] expected needs-choice but got {result}");
        CompareRequest(rootName, $"K1 root={entry.RootIndex} choice[{index}]", entry.Requests[index], request);
        if (index >= entry.Selections.Length)
            throw Mismatch(rootName, $"K1 root={entry.RootIndex} missing selection {index}");
        action = action.WithChoice(entry.Selections[index]);
    }
    if (entry.Requests.Length != entry.Selections.Length)
        throw Mismatch(rootName, $"K1 root={entry.RootIndex} request/selection counts differ");
    FlashBattle child = root;
    FlashStepResult final = FlashKernel.Step(ref child, action, rules, out _, out _);
    if (final != FlashStepResult.Completed)
        throw Mismatch(rootName, $"K1 root={entry.RootIndex} final step returned {final}");
    ulong expected = FlashCodec.BattleHash(child);
    if (entry.ChildHash != expected)
        throw Mismatch(rootName, $"K1 root={entry.RootIndex} child hash expected={entry.ChildHash:X16} actual={expected:X16}");
    if (entry.Ended != !FlashKernel.IsInProgress(child))
        throw Mismatch(rootName, $"K1 root={entry.RootIndex} ended expected={entry.Ended} actual={!FlashKernel.IsInProgress(child)}");
}

static void VerifyWalk(string rootName, FlashGoldenWalk walk, in FlashBattle root, FlashRules rules)
{
    FlashBattle battle = root;
    for (int stateIndex = 0; stateIndex < walk.States.Count; stateIndex++)
    {
        FlashGoldenState state = walk.States[stateIndex];
        ulong hash = FlashCodec.BattleHash(battle);
        if (state.StateHash != hash)
            throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} hash expected={state.StateHash:X16} actual={hash:X16}");
        Span<FlashAction> actions = stackalloc FlashAction[FlashKernel.MaxLegalActions];
        int count = FlashKernel.LegalActions(battle, rules, actions);
        if (count != state.LegalActions.Length)
            throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} legal count expected={state.LegalActions.Length} actual={count}");
        for (int index = 0; index < count; index++)
        {
            FlashAction actual = actions[index];
            FlashAction expected = state.LegalActions[index];
            if (!ActionEquals(actual, expected))
                throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} legal[{index}] mismatch");
            if (index >= state.ImmediateStatuses.Length)
                throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} missing status {index}");
            FlashBattle copy = battle;
            byte status;
            FlashChoiceRequest request = default;
            try
            {
                FlashStepResult result = FlashKernel.Step(ref copy, actual, rules, out request, out _);
                status = result == FlashStepResult.NeedsChoice ? (byte)1 : (byte)0;
            }
            catch (FlashUnsupportedException)
            {
                status = 2;
            }
            catch (FlashCapacityException)
            {
                status = 2;
            }
            if (state.ImmediateStatuses[index] != status)
                throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} legal[{index}] status expected={state.ImmediateStatuses[index]} actual={status}");
            if (status == 1)
                CompareRequest(rootName, $"walk seed={walk.Seed} state={stateIndex} legal[{index}]",
                    state.ImmediateRequests[index], request);
        }
        if (!state.HasStep)
        {
            if (stateIndex != walk.States.Count - 1)
                throw Mismatch(rootName, $"walk seed={walk.Seed} has states after a no-step state");
            break;
        }
        FlashAction action = state.StepBase;
        for (int index = 0; index < state.StepRequests.Length; index++)
        {
            FlashBattle partial = battle;
            FlashStepResult result = FlashKernel.Step(ref partial, action, rules,
                out FlashChoiceRequest request, out _);
            if (result != FlashStepResult.NeedsChoice)
                throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} step choice[{index}] expected needs-choice but got {result}");
            CompareRequest(rootName, $"walk seed={walk.Seed} state={stateIndex} step choice[{index}]",
                state.StepRequests[index], request);
            if (index >= state.StepSelections.Length)
                throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} missing step selection {index}");
            action = action.WithChoice(state.StepSelections[index]);
        }
        if (state.StepRequests.Length != state.StepSelections.Length)
            throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} step request/selection counts differ");
        FlashBattle child = battle;
        FlashStepResult final = FlashKernel.Step(ref child, action, rules, out _, out _);
        if (final != FlashStepResult.Completed)
            throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} step returned {final}");
        ulong childHash = FlashCodec.BattleHash(child);
        if (state.ChildHash != childHash)
            throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} child hash expected={state.ChildHash:X16} actual={childHash:X16}");
        if (state.Ended != !FlashKernel.IsInProgress(child))
            throw Mismatch(rootName, $"walk seed={walk.Seed} state={stateIndex} ended expected={state.Ended} actual={!FlashKernel.IsInProgress(child)}");
        battle = child;
    }
    if (walk.States.Count > 0 && walk.States[^1].HasStep && walk.States[^1].Ended)
        throw Mismatch(rootName, $"walk seed={walk.Seed} ended without the recorded terminal state");
}

static void CompareRequest(string rootName, string where, in FlashChoiceRequest expected,
    in FlashChoiceRequest actual)
{
    if (expected.Kind != actual.Kind || expected.Effect != actual.Effect
        || expected.Timing != actual.Timing || expected.Source != actual.Source
        || expected.SourceIsNone != actual.SourceIsNone
        || expected.OptionsAreGenerated != actual.OptionsAreGenerated
        || expected.OptionCount != actual.OptionCount || expected.MinCount != actual.MinCount
        || expected.MaxCount != actual.MaxCount)
        throw Mismatch(rootName, $"{where} request header expected="
            + $"{expected.Kind}/{expected.Effect}/{expected.Timing}/{expected.Source}/"
            + $"{expected.OptionCount}/{expected.MinCount}/{expected.MaxCount} actual="
            + $"{actual.Kind}/{actual.Effect}/{actual.Timing}/{actual.Source}/"
            + $"{actual.OptionCount}/{actual.MinCount}/{actual.MaxCount}");
    for (int index = 0; index < expected.OptionCount; index++)
    {
        if (expected.Options[index] != actual.Options[index])
            throw Mismatch(rootName, $"{where} option[{index}] expected={expected.Options[index]} actual={actual.Options[index]}");
    }
    if (expected.OptionsAreGenerated)
    {
        for (int index = 0; index < expected.OptionCount; index++)
        {
            if (!FlashFingerprint.Equal(expected.GeneratedOptions[index], actual.GeneratedOptions[index]))
                throw Mismatch(rootName, $"{where} generated option[{index}] mismatch");
        }
    }
}

static bool ActionEquals(in FlashAction left, in FlashAction right)
{
    if (left.Kind != right.Kind || left.HandIndex != right.HandIndex || left.Target != right.Target
        || left.PotionSlot != right.PotionSlot || left.ChoiceCount != right.ChoiceCount)
        return false;
    for (int index = 0; index < left.ChoiceCount; index++)
    {
        if (left.Choices[index] != right.Choices[index])
            return false;
    }
    return true;
}

static InvalidDataException Mismatch(string rootName, string detail)
    => new($"根 {rootName}：{detail}");

static byte[] ReadCompressed(string path)
{
    using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    using BrotliStream brotli = new(file, CompressionMode.Decompress);
    using MemoryStream stream = new();
    brotli.CopyTo(stream);
    return stream.ToArray();
}

static class FlashRulesCache
{
    private static readonly Dictionary<string, FlashRules> Values = new(StringComparer.Ordinal);
    public static void Add(string hash, FlashRules rules) => Values[hash] = rules;
    public static FlashRules Get(string hash) => Values[hash];
}
