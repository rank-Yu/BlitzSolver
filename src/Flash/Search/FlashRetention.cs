using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Search;

/// <summary>One Flash retention boundary, aligned with the existing solver's boundary key.</summary>
internal readonly record struct FlashRetentionBoundary(
    int BoundaryId,
    SearchRetentionBoundaryKind Kind,
    int TurnLayer,
    int PlayDepth,
    int InputCount,
    int Width);

/// <summary>
/// Opt-in K6 retention capture for the Flash layer loop. Only the offline retention diff probe
/// installs one; production requests leave <see cref="FlashBeamOptions.Retention"/> null and every
/// call site is a null check.
/// </summary>
internal abstract class FlashRetentionObserver
{
    /// <summary>The canonical projection key of a Flash state (the offline probe hashes the dump).</summary>
    public abstract SearchRetentionStateKey Project(in FlashBattle battle);

    /// <summary>Starts a retention boundary. <paramref name="state"/> is the unselected input pool.</summary>
    public virtual void Boundary(in FlashRetentionBoundary boundary)
    {
    }

    /// <summary>One pool insertion attempt: a new seat, a byte-identical duplicate or a dominating label.</summary>
    public virtual void Attempt(int node, int existingNode, in FlashBattle state, string reason)
    {
    }

    /// <summary>One input state of the boundary's selection pass.</summary>
    public virtual void PoolState(
        int boundaryId,
        int node,
        double score,
        int rankPosition,
        bool required,
        bool selected,
        string lane,
        int turn,
        int depth,
        string lastAction)
    {
    }

    /// <summary>A selected entry removed again by the incumbent / Smart potion bound.</summary>
    public virtual void Pruned(int boundaryId, int node, string reason)
    {
    }

    /// <summary>The end-turn pool dropped entries at its capacity bound.</summary>
    public virtual void Compacted(int before, int after)
    {
    }
}
