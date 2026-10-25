// Rules-compare mode: compiles Rules from the native root and compares its canonical encoding with
// the root's frozen rule object byte for byte.
using System.IO.Compression;
using BlitzSolver.Engine;
using BlitzSolver.Import;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace OfflineHost;

internal enum RulesStatus
{
    Same,
    Different,
    Rejected,
}

internal sealed record RulesResult(RulesStatus Status, string Reason, string FirstTable,
    string Detail)
{
    public string Line => Status switch
    {
        RulesStatus.Same => "RULES status=same",
        RulesStatus.Different => $"RULES status=diff table={FirstTable}",
        _ => $"RULES status=reject reason={Reason}",
    };
}

internal static class RulesCompare
{
    public static RulesResult Run(string data, string root, CombatState state)
    {
        byte[] expected;
        int expectedLength;
        try
        {
            expected = ImportCompare.LoadRulesBytes(data, root);
            expectedLength = expected.Length;
        }
        catch (InvalidDataException error)
        {
            return new(RulesStatus.Rejected, "invalid-golden", "", error.Message);
        }

        Player player = LocalContext.GetMe(state)
            ?? throw new InvalidOperationException("找不到本地玩家。");
        Rules rules;
        try
        {
            if (state.RunState is not RunState runState)
                return new(RulesStatus.Rejected, ImportReason.RulesMismatch, "",
                    "原生战斗的 RunState 不是具体 RunState");
            rules = RulesCompiler.Compile(state, player, runState);
        }
        catch (UnsupportedImportException error)
        {
            return new(RulesStatus.Rejected, error.Reason, "", error.Detail);
        }
        catch (UnsupportedException error)
        {
            return new(RulesStatus.Rejected, "core-unsupported", "", error.Message);
        }
        catch (CapacityException error)
        {
            return new(RulesStatus.Rejected, ImportReason.Capacity, error.Region, error.Message);
        }
        catch (InvalidOperationException error)
        {
            return new(RulesStatus.Rejected, "rules-build", "", error.Message);
        }

        byte[] actual = RulesCodec.Encode(rules);
        if (actual.AsSpan().SequenceEqual(expected))
            return new(RulesStatus.Same, "", "", "");

        Rules golden;
        try
        {
            golden = RulesCodec.Decode(expected);
        }
        catch (InvalidDataException error)
        {
            return new(RulesStatus.Different, "", "<bytes>",
                $"编码长度 golden={expectedLength} compiled={actual.Length}，golden 解码失败：{error.Message}");
        }
        IReadOnlyList<string> differences = RulesDiff.Compare(golden, rules);
        string firstTable = differences.Count == 0
            ? "<bytes>"
            : differences[0].Split(' ')[0];
        return new(RulesStatus.Different, "", firstTable,
            string.Join('\n', differences.Take(16)));
    }
}
