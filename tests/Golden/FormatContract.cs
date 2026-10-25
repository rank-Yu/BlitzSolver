using System.Buffers.Binary;
using System.Text;
using BlitzSolver.Engine;
using BlitzSolver.Search;

namespace BlitzSolver.Golden;

/// <summary>
/// K0 contract for the on-disk format tags. Every codec magic must decode, in little-endian byte
/// order, to exactly the ASCII label below; the version is shared by all codecs.
/// </summary>
internal static class FormatContract
{
    internal static string Run()
    {
        (string Label, uint Magic)[] tags =
        [
            ("BATL", BattleCodec.Magic),
            ("RULE", RulesCodec.Magic),
            ("GOLD", GoldenData.Magic),
            ("SREQ", SearchCodec.RequestMagic),
            ("SRES", SearchCodec.ResultMagic),
        ];
        Span<byte> bytes = stackalloc byte[4];
        string[] labels = new string[tags.Length];
        for (int index = 0; index < tags.Length; index++)
        {
            (string expected, uint magic) = tags[index];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, magic);
            string actual = Encoding.ASCII.GetString(bytes);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"格式魔数 {magic:X8} 按小端字节是 {actual}，不是 {expected}。");
            labels[index] = actual;
        }
        if (Codec.FormatVersion != 2)
            throw new InvalidOperationException($"数据格式版本应为 2，实际为 {Codec.FormatVersion}。");

        // The battle header is the innermost format: reject the old magic and version explicitly.
        byte[] current = BattleCodec.Encode(default);
        byte[] oldVersion = (byte[])current.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(oldVersion.AsSpan(4), 1);
        ExpectInvalid(() => BattleCodec.Decode(oldVersion), "BATL version 1");
        byte[] oldMagic = (byte[])current.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(oldMagic, 0x54414246);
        ExpectInvalid(() => BattleCodec.Decode(oldMagic), "旧战斗魔数");
        return string.Join(',', labels);
    }

    private static void ExpectInvalid(Action decode, string what)
    {
        try
        {
            decode();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException($"{what} 未被拒绝。");
    }
}
