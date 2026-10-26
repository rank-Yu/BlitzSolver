using System.IO.Compression;
using BlitzSolver.Simulation.Combat;

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

    public static void WriteCompressed(string path, byte[] bytes)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory != null)
            Directory.CreateDirectory(directory);
        using FileStream file = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using BrotliStream brotli = new(file, CompressionLevel.Optimal, leaveOpen: false);
        brotli.Write(bytes);
    }

    /// <summary>
    /// The M0 recorder's Brotli level. D1 re-recording uses it so unchanged semantics reproduce the
    /// frozen <c>golden.br</c> bytes, not merely the decoded fields.
    /// </summary>
    public static void WriteCompressedSmallest(string path, byte[] bytes)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory != null)
            Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        using (FileStream file = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (BrotliStream brotli = new(file, CompressionLevel.SmallestSize, leaveOpen: false))
            brotli.Write(bytes);
        File.Move(temporary, path, overwrite: true);
    }

    public static void CopyDirectory(string source, string target)
    {
        string sourcePrefix = source + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(target);
        foreach (string path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = path[sourcePrefix.Length..];
            string destination = Path.Combine(target, relative);
            string? directory = Path.GetDirectoryName(destination);
            if (directory != null)
                Directory.CreateDirectory(directory);
            File.Copy(path, destination, overwrite: false);
        }
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
