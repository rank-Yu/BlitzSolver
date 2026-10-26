// Ported from CombatSolver src/Search/BfwsBoundedOpen.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace BlitzSolver.Ai.Combat.Novelty;

// A stable double-ended queue releases the worst entry as soon as the frontier
// reaches its bound. It never retains tombstones containing simulation graphs.
internal sealed class BfwsBoundedOpen<T>(int capacity)
{
    private readonly SortedSet<Entry> _entries = new(Comparer<Entry>.Create(
        (a, b) => a.Priority.CompareTo(b.Priority)));
    private sealed record Entry(T Value, (double Primary, double Secondary, long Sequence) Priority);

    public int Count => _entries.Count;
    public IEnumerable<T> Values => _entries.Select(entry => entry.Value);

    public bool Enqueue(T value, (double, double, long) priority, out T dropped)
    {
        if (!_entries.Add(new(value, priority)))
            throw new InvalidOperationException("OPEN priorities require a unique sequence.");
        if (_entries.Count <= capacity) { dropped = default!; return false; }
        Entry worst = _entries.Max!;
        _entries.Remove(worst);
        dropped = worst.Value;
        return true;
    }

    public T Dequeue()
    {
        Entry best = _entries.Min ?? throw new InvalidOperationException("OPEN is empty.");
        _entries.Remove(best);
        return best.Value;
    }

    public void Clear() => _entries.Clear();

    /// <summary>
    /// Rewrites the stored values while preserving every priority, so a route-node index cleanup does
    /// not change the open-queue order.
    /// </summary>
    public void Remap(Func<T, T> map)
    {
        Entry[] entries = [.. _entries];
        _entries.Clear();
        foreach (Entry entry in entries)
            _entries.Add(new Entry(map(entry.Value), entry.Priority));
    }
}
