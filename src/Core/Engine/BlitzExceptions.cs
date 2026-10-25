namespace BlitzSolver.Engine;

/// <summary>
/// A fixed capacity of <see cref="BlitzBattle"/> was exceeded. A child-state overflow inside the
/// expansion loop is a search boundary handled by <c>BlitzBeamSolver</c>; a root-level overflow
/// abandons the whole Blitz solve, and On mode reports <c>BLITZ_SEARCH_FAILED reason=kernel-capacity</c>
/// instead of falling back to the existing backend.
/// </summary>
public sealed class BlitzCapacityException(string region, int capacity)
    : Exception($"Blitz 状态区块 {region} 超出容量 {capacity}。")
{
    public string Region { get; } = region;

    public int Capacity { get; } = capacity;
}

/// <summary>
/// The kernel reached content or a mechanic it does not implement. Admission should have
/// rejected the root; reaching this at run time abandons the whole Blitz solve.
/// </summary>
public sealed class BlitzUnsupportedException(string detail)
    : Exception($"Blitz 内核不支持：{detail}");
