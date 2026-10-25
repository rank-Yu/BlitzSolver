// Import-compare mode: builds the native root, imports it with BlitzSolver.Import, and compares the
// decoded expected GOLD root byte for byte (the first root, or the matching ordinary setup root for
// the three turn-setup fixtures).
using System.IO.Compression;
using BlitzSolver.Engine;
using BlitzSolver.Import;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;

namespace OfflineHost;

internal enum ImportStatus
{
    Same,
    Different,
    Rejected,
}

internal sealed record ImportResult(ImportStatus Status, string Reason, string FirstField, string Detail)
{
    public string Line => Status switch
    {
        ImportStatus.Same => "IMPORT status=same",
        ImportStatus.Different => $"IMPORT status=diff field={FirstField}",
        _ => $"IMPORT status=reject reason={Reason}",
    };
}

internal static class ImportCompare
{
    public static ImportResult Run(string data, string root, CombatState state,
        IReadOnlyList<string> setupSelections, bool preInjectionSetupRoot)
    {
        Battle expected;
        Rules rules;
        try
        {
            string directory = Path.Combine(data, "d1", root);
            using System.Text.Json.JsonDocument meta = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(directory, "meta.json")));
            string sha = meta.RootElement.GetProperty("rulesSha256").GetString()
                ?? throw new InvalidDataException($"{root} 缺少 rulesSha256。");
            GoldenRootRecord golden = GoldenData.Decode(ReadBrotli(Path.Combine(directory, "golden.br")));
            (Battle expectedRoot, int rootIndex) = SelectExpectedRoot(golden, setupSelections, preInjectionSetupRoot);
            expected = expectedRoot;
            rules = RulesCodec.Decode(ReadBrotli(Path.Combine(data, "objects", "rules", sha + ".br")));
            Console.WriteLine($"IMPORT-ROOT index={rootIndex} of={golden.Roots.Count}");
        }
        catch (InvalidDataException error)
        {
            return new(ImportStatus.Rejected, "invalid-golden", "", error.Message);
        }

        Player player = LocalContext.GetMe(state)
            ?? throw new InvalidOperationException("找不到本地玩家。");
        Battle actual;
        try
        {
            actual = BattleImporter.Import(state, player, rules,
                preInjectionSetupRoot ? ImportOptions.Default with { TurnSetupRoot = true } : ImportOptions.Default);
        }
        catch (UnsupportedImportException error)
        {
            return new(ImportStatus.Rejected, error.Reason, "", error.Detail);
        }
        catch (UnsupportedException error)
        {
            return new(ImportStatus.Rejected, "core-unsupported", "", error.Message);
        }
        catch (CapacityException error)
        {
            return new(ImportStatus.Rejected, ImportReason.Capacity, error.Region, error.Message);
        }

        if (SameBytes(expected, actual))
            return new(ImportStatus.Same, "", "", "");
        List<string> diff = BattleDump.Diff(expected, actual, "golden", "import");
        string first = diff.Count == 0 ? "<bytes>" : FieldOf(diff[0]);
        return new(ImportStatus.Different, "", first, string.Join('\n', diff.Take(24)));
    }

    public static Rules LoadRules(string data, string root)
    {
        string directory = Path.Combine(data, "d1", root);
        using System.Text.Json.JsonDocument meta = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(directory, "meta.json")));
        string sha = meta.RootElement.GetProperty("rulesSha256").GetString()
            ?? throw new InvalidDataException($"{root} 缺少 rulesSha256。");
        return RulesCodec.Decode(ReadBrotli(Path.Combine(data, "objects", "rules", sha + ".br")));
    }

    private static (Battle Root, int Index) SelectExpectedRoot(GoldenRootRecord golden,
        IReadOnlyList<string> setupSelections, bool preInjectionSetupRoot)
    {
        if (!preInjectionSetupRoot || setupSelections.Count == 0)
            return (golden.Roots[0].Battle, 0);
        // A turn-setup fixture's ordinary root is the one reached by the native first-option setup
        // choice; the remaining roots enumerate other setup choices (M2a-5). Match the recorded
        // selection to the golden choice breadcrumb instead of assuming index 0.
        for (int index = 0; index < golden.Roots.Count; index++)
        {
            string choices = golden.Roots[index].Choices;
            bool all = true;
            foreach (string id in setupSelections)
            {
                if (!choices.Contains("/" + id + "+", StringComparison.Ordinal))
                {
                    all = false;
                    break;
                }
            }
            if (all)
                return (golden.Roots[index].Battle, index);
        }
        return (golden.Roots[0].Battle, 0);
    }

    public static bool SameBytes(in Battle left, in Battle right)
    {
        byte[] leftBytes = BattleCodec.Encode(left);
        byte[] rightBytes = BattleCodec.Encode(right);
        return leftBytes.AsSpan(BattleCodec.HeaderSize).SequenceEqual(rightBytes.AsSpan(BattleCodec.HeaderSize));
    }

    private static string FieldOf(string diffLine)
    {
        int space = diffLine.IndexOf(' ');
        if (space < 0)
            return diffLine;
        string rest = diffLine[(space + 1)..];
        int colon = rest.IndexOf(':');
        return colon < 0 ? rest : rest[..colon];
    }

    private static byte[] ReadBrotli(string path)
    {
        using FileStream file = File.OpenRead(path);
        using BrotliStream brotli = new(file, CompressionMode.Decompress);
        using MemoryStream buffer = new();
        brotli.CopyTo(buffer);
        return buffer.ToArray();
    }
}
