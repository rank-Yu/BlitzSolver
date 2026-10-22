namespace CombatSolver.Flash.Kernel;

/// <summary>
/// A fixed capacity of <see cref="FlashBattle"/> was exceeded. A child-state overflow inside the
/// expansion loop is a search boundary handled by <c>FlashBeamSolver</c>; a root-level overflow
/// abandons the whole Flash solve, and On mode reports <c>FLASH_SEARCH_FAILED reason=kernel-capacity</c>
/// instead of falling back to the existing backend.
/// </summary>
public sealed class FlashCapacityException(string region, int capacity)
    : Exception($"Flash 状态区块 {region} 超出容量 {capacity}。")
{
    public string Region { get; } = region;

    public int Capacity { get; } = capacity;
}

/// <summary>
/// The kernel reached content or a mechanic it does not implement. Admission should have
/// rejected the root; reaching this at run time abandons the whole Flash solve.
/// </summary>
public sealed class FlashUnsupportedException(string detail)
    : Exception($"Flash 内核不支持：{detail}");
