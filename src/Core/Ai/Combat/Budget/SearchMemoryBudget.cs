using System.Runtime.CompilerServices;

namespace Sts2Solver.Ai.Combat;

/// <summary>
/// Raised when a search-owned allocation would cross <see cref="BeamOptions.SearchMemoryBudgetBytes"/>.
/// The solver catches it at the search loop and returns the best result found so far, exactly like the
/// node limit. It deliberately does not derive from <see cref="CapacityException"/> so a single-branch
/// capacity handler cannot turn a budget stop into a dropped child.
/// </summary>
internal sealed class SearchMemoryBudgetException : Exception
{
    internal SearchMemoryBudgetException(string region, long usedBytes, long limitBytes)
        : base($"搜索内存预算不足：region={region} used={usedBytes} limit={limitBytes}。")
    {
        Region = region;
        UsedBytes = usedBytes;
        LimitBytes = limitBytes;
    }

    internal string Region { get; }

    internal long UsedBytes { get; }

    internal long LimitBytes { get; }
}

/// <summary>
/// Logical search-memory ledger. It counts allocated search-owned blocks, never GC or process
/// memory, so the offline host and the game see the same decisions. A limit of zero (the default for
/// decoded legacy requests and direct kernel tests) is unlimited.
/// </summary>
internal sealed class SearchMemoryLedger
{
    /// <summary>Bytes retained by this storage across solves; never decreases.</summary>
    internal long UsedBytes { get; private set; }

    /// <summary>Usage when the current solve started, before any of its own allocations.</summary>
    internal long SolveStartBytes { get; private set; }

    /// <summary>Highest usage observed during the current solve.</summary>
    internal long PeakBytes { get; private set; }

    /// <summary>Limit of the current solve; zero means unlimited.</summary>
    internal long LimitBytes { get; private set; }

    /// <summary>True once the current solve requested a block that would cross the limit.</summary>
    internal bool Exceeded { get; private set; }

    /// <summary>Usage at the first rejected allocation of the current solve.</summary>
    internal long ExceededAtBytes { get; private set; }

    /// <summary>Lifetime allocated bytes by accounting region (diagnostics only).</summary>
    internal IReadOnlyDictionary<string, long> AllocatedByRegion => _byRegion;

    private readonly Dictionary<string, long> _byRegion = new(StringComparer.Ordinal);

    internal void BeginSolve(long limitBytes, bool releaseTransient = false)
    {
        // Per-solve structures are no longer reachable after the previous solve.
        if (releaseTransient)
            foreach (string region in new[] { "pool-aux", "candidate-routes", "pool-labels", "routing-signature" })
            {
                UsedBytes -= _byRegion.GetValueOrDefault(region);
                _byRegion.Remove(region);
            }
        LimitBytes = Math.Max(0, limitBytes);
        SolveStartBytes = UsedBytes;
        PeakBytes = UsedBytes;
        Exceeded = false;
        ExceededAtBytes = 0;
        if (LimitBytes != 0 && UsedBytes > LimitBytes)
        {
            Exceeded = true;
            ExceededAtBytes = UsedBytes;
        }
    }

    /// <summary>
    /// Charges one logical block. The block size is the allocated array/chunk length, not the live
    /// payload. A rejected block is not counted, so <see cref="UsedBytes"/> never exceeds the limit.
    /// </summary>
    internal void Charge(long bytes, string region)
    {
        if (bytes <= 0)
            return;
        if (Exceeded)
            throw new SearchMemoryBudgetException(region, ExceededAtBytes, LimitBytes);
        if (LimitBytes != 0 && UsedBytes + bytes > LimitBytes)
        {
            Exceeded = true;
            ExceededAtBytes = UsedBytes;
            throw new SearchMemoryBudgetException(region, UsedBytes, LimitBytes);
        }
        UsedBytes += bytes;
        _byRegion[region] = _byRegion.GetValueOrDefault(region) + bytes;
        if (UsedBytes > PeakBytes)
            PeakBytes = UsedBytes;
    }

    internal void ReleaseArray(long bytes)
    {
        UsedBytes -= bytes;
        _byRegion["array"] -= bytes;
    }

    /// <summary>Releases a retained block that was replaced; used only for the growable int scratch.</summary>
    internal void Release(long bytes)
    {
        if (bytes <= 0)
            return;
        UsedBytes = Math.Max(0, UsedBytes - bytes);
        long scratch = _byRegion.GetValueOrDefault("scratch") - bytes;
        _byRegion["scratch"] = Math.Max(0, scratch);
    }
}

