namespace BlitzSolver;

/// <summary>
/// Resolves the Mod's search-memory ceiling. The search core only receives the resulting byte count;
/// it never reads process or GC state, so the same request still produces the same route offline.
/// Default: 15% of the total memory the runtime may use, i.e. physical memory or the container or
/// job-object limit as reported by <see cref="GC.GetGCMemoryInfo"/>. It is a fixed per-machine value,
/// not the momentary free memory, so one machine keeps one ceiling. 1 GiB floor, no upper cap.
/// </summary>
internal static class SearchMemoryBudget
{
    /// <summary>Optional override in MiB. Documented in <c>docs/mod.md</c>.</summary>
    internal const string OverrideVariable = "BLITZ_SEARCH_MEMORY_MB";

    private static readonly Lazy<long> Resolved = new(Resolve, isThreadSafe: true);

    internal static long Bytes => Resolved.Value;

    private static long Resolve()
    {
        string? configured = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (long.TryParse(configured, out long mebibytes) && mebibytes > 0)
            {
                const long scale = 1024L * 1024L;
                return mebibytes > long.MaxValue / scale ? long.MaxValue : mebibytes * scale;
            }
            ModLog.Error(
                $"[BlitzSolver] SEARCH_MEMORY_IGNORED value={configured} " +
                $"reason=not_positive_integer variable={OverrideVariable}");
        }
        const long floor = 1L << 30;
        long available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (available <= 0)
            return floor;
        // Integer arithmetic avoids overflowing the 15% product on very large machines.
        return Math.Max(floor, available / 100 * 15 + available % 100 * 15 / 100);
    }
}
