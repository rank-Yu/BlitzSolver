using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Draw helpers over <see cref="RngState"/> that native run generation uses but the combat kernel
/// does not: the Gaussian integer used by act map point counts, the in-place Fisher-Yates shuffle,
/// random item picks and the weighted-only-by-count <c>GrabBag</c> of encounter selection.
/// </summary>
public static class RunRandom
{
    /// <summary>
    /// Native <c>Rng.NextGaussianInt(mean, stdDev, min, max)</c>: rejection sampling in the
    /// inclusive integer range, two <c>NextDouble</c> draws per attempt, banker's rounding.
    /// </summary>
    public static int NextGaussianInt(ref RngState rng, int mean, int stdDev, int min, int max)
    {
        if (min > max)
            throw new ArgumentOutOfRangeException(nameof(min), "Minimum must not be higher than maximum.");
        if (mean < min || mean > max)
            throw new ArgumentOutOfRangeException(nameof(mean), mean, "Mean must be within [min, max].");
        int value;
        do
        {
            double first = 1.0 - rng.NextDouble();
            double second = 1.0 - rng.NextDouble();
            double normal = Math.Sqrt(-2.0 * Math.Log(first)) * Math.Sin(Math.PI * 2.0 * second);
            value = (int)Math.Round(mean + stdDev * normal);
        }
        while (value < min || value > max);
        return value;
    }

    /// <summary>Native <c>ListExtensions.UnstableShuffle</c> over a span.</summary>
    public static void UnstableShuffle<T>(ref RngState rng, Span<T> list)
    {
        int count = list.Length;
        while (count > 1)
        {
            count--;
            int index = rng.NextInt(count + 1);
            (list[count], list[index]) = (list[index], list[count]);
        }
    }

    /// <summary>Array overload so call sites with <c>int[]</c> need no explicit span type.</summary>
    public static void UnstableShuffle(ref RngState rng, int[] list)
        => UnstableShuffle(ref rng, list.AsSpan());

    /// <summary>Native <c>Rng.NextItem</c>: one <c>NextInt(0, count)</c> draw; default when empty.</summary>
    public static ushort NextItem(ref RngState rng, ReadOnlySpan<ushort> items)
        => items.Length == 0 ? default : items[rng.NextInt(items.Length)];
}

/// <summary>
/// Native <c>GrabBag&lt;T&gt;</c> for encounter selection. Weights are always 1.0 at new-run
/// generation; the weighted accumulation is still reproduced exactly so a future caller can reuse
/// it.
/// </summary>
public struct RunGrabBag
{
    public const int Capacity = 64;

    private RunGrabBagEntries _ids;
    private RunGrabBagWeights _weights;
    private int _count;
    private double _totalWeight;

    public readonly int Count => _count;

    public readonly bool Any => _count != 0;

    public readonly ushort this[int index] => _ids[index];

    /// <summary>Native <c>GrabBag.Add</c>; the bag never holds a duplicate at these call sites.</summary>
    public void Add(ushort id, double weight)
    {
        if (_count >= Capacity)
            throw new CapacityException("RunGrabBag", Capacity);
        _ids[_count] = id;
        _weights[_count] = weight;
        _count++;
        _totalWeight += weight;
    }

    public readonly bool AnyMatch(Func<ushort, bool> predicate)
    {
        for (int index = 0; index < _count; index++)
        {
            if (predicate(_ids[index]))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Native <c>GrabBag.GrabAndRemove</c>. A predicate that matches nothing returns false without
    /// consuming a draw.
    /// </summary>
    public bool GrabAndRemove(ref RngState rng, Func<ushort, bool>? predicate, out ushort id)
    {
        id = default;
        // Native GrabIndex: a predicate with no matching entry returns -1 without a draw; the
        // no-predicate path still draws even when the bag is empty (roll * 0 == 0, no entries).
        if (predicate != null && !AnyMatch(predicate))
            return false;
        int index;
        do
        {
            index = GrabIndex(ref rng);
        }
        while (predicate != null && index >= 0 && !predicate(_ids[index]));
        if (index < 0)
            return false;
        id = _ids[index];
        Remove(index);
        return true;
    }

    private readonly int GrabIndex(ref RngState rng)
    {
        double roll = rng.NextDouble() * _totalWeight;
        double cumulative = 0.0;
        for (int index = 0; index < _count; index++)
        {
            cumulative += _weights[index];
            if (roll < cumulative)
                return index;
        }
        return -1;
    }

    private void Remove(int index)
    {
        _totalWeight -= _weights[index];
        for (int move = index; move < _count - 1; move++)
        {
            _ids[move] = _ids[move + 1];
            _weights[move] = _weights[move + 1];
        }
        _count--;
        _ids[_count] = default;
        _weights[_count] = default;
    }
}

/// <summary>The native grab bag holds at most one act's encounter pool; 64 covers the largest.</summary>
[System.Runtime.CompilerServices.InlineArray(RunGrabBag.Capacity)]
public struct RunGrabBagEntries
{
    private ushort _first;
}

[System.Runtime.CompilerServices.InlineArray(RunGrabBag.Capacity)]
public struct RunGrabBagWeights
{
    private double _first;
}
