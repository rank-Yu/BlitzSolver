using System.IO.Compression;
using BlitzSolver.Engine;

namespace BlitzSolver.Golden;

internal static class GoldenFiles
{
    public static byte[] ReadCompressed(string path)
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BrotliStream brotli = new(file, CompressionMode.Decompress);
        using MemoryStream stream = new();
        brotli.CopyTo(stream);
        return stream.ToArray();
    }

    public static bool ActionEquals(in PlayerAction left, in PlayerAction right)
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

    public static string Describe(in PlayerAction action)
        => $"{action.Kind} hand={action.HandIndex} target={action.Target} potion={action.PotionSlot} "
            + $"choices={action.ChoiceCount}";
}
