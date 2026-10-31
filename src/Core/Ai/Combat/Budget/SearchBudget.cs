namespace Sts2Solver.Ai.Combat;

/// <summary>Monotonic decision clock, with a shared deadline only for historical requests.</summary>
internal sealed class SearchBudget
{
    private readonly Func<long> _clock;
    private readonly bool _independentMembers;
    private readonly long _started;
    private readonly long _deadline;
    private long _mainDeadline;
    private readonly int _milliseconds;

    internal SearchBudget(int milliseconds, bool reservePotionPhase, Func<long>? clock, bool independentMembers = false)
    {
        if (!independentMembers && milliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        _independentMembers = independentMembers;
        _clock = clock ?? (() => Environment.TickCount64);
        _started = _clock();
        _milliseconds = milliseconds;
        _deadline = independentMembers ? long.MaxValue : checked(_started + milliseconds);
        _mainDeadline = reservePotionPhase && !independentMembers ? _started + milliseconds * 7L / 10L : _deadline;
    }

    internal void ReservePotionPhase(bool reserve)
        => _mainDeadline = reserve && !_independentMembers ? _started + _milliseconds * 7L / 10L : _deadline;

    internal long Now => _clock();
    internal long Elapsed => Math.Max(0, Now - _started);
    internal bool Expired() => !_independentMembers && Now >= _deadline;
    internal long Remaining(bool potionPhase) => _independentMembers
        ? throw new InvalidOperationException("独立成员没有共享剩余预算。")
        : Math.Max(0, (potionPhase ? _deadline : _mainDeadline) - Now);
    internal static long SearchShare(long remaining, int members)
    {
        long share = Math.Max(1, remaining / Math.Max(1, members));
        // Keep time for replay and bookkeeping even when this is the last member.
        return Math.Max(1, share - Math.Min(2_000, Math.Max(100, share / 10)));
    }
}
