using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace BlitzSolver.OfflineHost;

/// <summary>Standalone process input. No game or test types cross this boundary.</summary>
public sealed record FirstCombatOptions(string GameDataDirectory, string Seed = "R3B2-DECISION",
    string Character = "Ironclad", int Ascension = 0, string UnlockProfile = "neow-epoch-v1",
    string SearchTier = "Low", int TimeoutSeconds = 300, long MemoryBudgetBytes = 2576980377);
public sealed record VisibleChoice(string Kind, string[] Options, string[] Selected);
public sealed record StateSummary(string Sha256, int Hp, int MaxHp, int Gold, string[] Deck,
    string[] Relics, string[] Potions);
public sealed record CombatSummary(string Status, string Outcome, int Turns, int Actions,
    int Searches, long ExpandedNodes, int Deviations, int DeploymentFailures,
    string? Failure, string? OpeningSha256, string? TerminalSha256, string TerminalPath,
    int? TerminalHp, int? TerminalMaxHp, int? TerminalGold);
public sealed record FirstCombatResult(string Status, string? Milestone, string? Detail,
    FirstCombatOptions Configuration, string Policy, VisibleChoice[] Choices,
    StateSummary? Starting, StateSummary? AfterCombat, CombatSummary? Combat);

public static class FirstCombat
{
    private static int _started;
    private static string _data = "";
    public static FirstCombatResult Run(FirstCombatOptions options, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.GameDataDirectory) || string.IsNullOrWhiteSpace(options.Seed)
            || options.Ascension is < 0 or > 10 || options.TimeoutSeconds is < 1 or > 86400
            || options.MemoryBudgetBytes < 1 || options.UnlockProfile != "neow-epoch-v1"
            || options.SearchTier is not ("Low" or "Medium" or "High" or "ExtraHigh")
            || options.Character is not ("Ironclad" or "Silent" or "Regent" or "Necrobinder" or "Defect"))
            throw new InvalidDataException("Invalid first-combat configuration.");
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("One native run per process is required.");
        _data = Path.GetFullPath(options.GameDataDirectory);
        foreach (string name in new[] { "sts2.dll", "GodotSharp.dll", "0Harmony.dll" })
            if (!File.Exists(Path.Combine(_data, name)))
                throw new InvalidDataException("Missing game assembly: " + Path.Combine(_data, name));
        AssemblyLoadContext.Default.Resolving += Resolve;
        try { return RunCore(options, token); }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
    }
    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        string path = Path.Combine(_data, name.Name + ".dll");
        return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static FirstCombatResult RunCore(FirstCombatOptions options, CancellationToken token)
        => Runtime.FirstCombatSession.Run(options, token);
}
