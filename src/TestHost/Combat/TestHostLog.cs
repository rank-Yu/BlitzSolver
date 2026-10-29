namespace Sts2Solver.TestHost;

/// <summary>Trace channel used by the shared scenario builder in both hosts.</summary>
internal static class TestHostLog
{
    public static void Trace(string stage)
    {
        Console.Error.WriteLine($"[scenario] {stage}");
        Console.Error.Flush();
    }
}
