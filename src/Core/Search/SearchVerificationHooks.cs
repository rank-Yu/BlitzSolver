namespace BlitzSolver.Search;

/// <summary>
/// Default-off verification switches for search optimizations that share work between executions
/// or temporarily mutate a read-only state. They are test hooks outside <see cref="BeamOptions"/>
/// so a request format never carries a diagnostic mode. Enabling one only adds comparison passes;
/// it never changes the search.
/// </summary>
internal static class SearchVerificationHooks
{
    /// <summary>
    /// When set before a solve:
    /// <list type="bullet">
    /// <item>every end-turn selection leaf that reuses a prepared prefix also runs the complete
    /// <see cref="Engine.Kernel.Step"/> from its parent and compares the battle bytes, step result,
    /// choice request and history-risk flag;</item>
    /// <item>every threat projection compares the <see cref="Engine.Battle"/> before and after the
    /// in-place projection and reports the first differing field.</item>
    /// </list>
    /// A difference throws immediately.
    /// </summary>
    internal static bool VerifySearchEquivalence { get; set; }
}
