using System.Numerics;

namespace BlitzSolver.Engine;

/// <summary>
/// Exact port of .NET's <c>GenericArraySortHelper&lt;T&gt;.IntrospectiveSort</c> as used by
/// <c>List&lt;T&gt;.Sort()</c>. The native shuffle sorts with this unstable algorithm before its
/// Fisher-Yates pass, so cards that compare equal but differ in state must be permuted exactly
/// the same way. Comparison follows native <c>CardModel.CompareTo</c>: model id (ordinal), then
/// upgrade level.
/// </summary>
public static class CardSort
{
    private const int IntrosortSizeThreshold = 16;

    public static void Sort(Span<Card> keys, Rules rules)
    {
        if (keys.Length > 1)
            IntroSort(keys, 2 * (BitOperations.Log2((uint)keys.Length) + 1), rules);
    }

    public static int Compare(in Card left, in Card right, Rules rules)
    {
        int rank = rules.CardSortRank(left.Id).CompareTo(rules.CardSortRank(right.Id));
        if (rank != 0)
            return rank;
        return left.Upgrade.CompareTo(right.Upgrade);
    }

    private static void IntroSort(Span<Card> keys, int depthLimit, Rules rules)
    {
        int partitionSize = keys.Length;
        while (partitionSize > 1)
        {
            if (partitionSize <= IntrosortSizeThreshold)
            {
                if (partitionSize == 2)
                {
                    SwapIfGreater(ref keys[0], ref keys[1], rules);
                    return;
                }
                if (partitionSize == 3)
                {
                    SwapIfGreater(ref keys[0], ref keys[1], rules);
                    SwapIfGreater(ref keys[0], ref keys[2], rules);
                    SwapIfGreater(ref keys[1], ref keys[2], rules);
                    return;
                }
                InsertionSort(keys.Slice(0, partitionSize), rules);
                return;
            }
            if (depthLimit == 0)
            {
                HeapSort(keys.Slice(0, partitionSize), rules);
                return;
            }
            depthLimit--;
            int pivot = PickPivotAndPartition(keys.Slice(0, partitionSize), rules);
            IntroSort(keys[(pivot + 1)..partitionSize], depthLimit, rules);
            partitionSize = pivot;
        }
    }

    private static int PickPivotAndPartition(Span<Card> keys, Rules rules)
    {
        int last = keys.Length - 1;
        int middle = last >> 1;
        SwapIfGreater(ref keys[0], ref keys[middle], rules);
        SwapIfGreater(ref keys[0], ref keys[last], rules);
        SwapIfGreater(ref keys[middle], ref keys[last], rules);

        int nextToLast = keys.Length - 2;
        Card pivot = keys[middle];
        Swap(ref keys[middle], ref keys[nextToLast]);

        int left = 0;
        int right = nextToLast;
        while (left < right)
        {
            while (left < nextToLast && Compare(pivot, keys[++left], rules) > 0)
            {
            }
            while (right > 0 && Compare(pivot, keys[--right], rules) < 0)
            {
            }
            if (left >= right)
                break;
            Swap(ref keys[left], ref keys[right]);
        }

        if (left != nextToLast)
            Swap(ref keys[left], ref keys[nextToLast]);
        return left;
    }

    private static void HeapSort(Span<Card> keys, Rules rules)
    {
        int n = keys.Length;
        for (int i = n >> 1; i >= 1; i--)
            DownHeap(keys, i, n, rules);
        for (int i = n; i > 1; i--)
        {
            Swap(ref keys[0], ref keys[i - 1]);
            DownHeap(keys, 1, i - 1, rules);
        }
    }

    private static void DownHeap(Span<Card> keys, int i, int n, Rules rules)
    {
        Card d = keys[i - 1];
        while (i <= n >> 1)
        {
            int child = 2 * i;
            if (child < n && Compare(keys[child - 1], keys[child], rules) < 0)
                child++;
            if (!(Compare(d, keys[child - 1], rules) < 0))
                break;
            keys[i - 1] = keys[child - 1];
            i = child;
        }
        keys[i - 1] = d;
    }

    private static void InsertionSort(Span<Card> keys, Rules rules)
    {
        for (int i = 0; i < keys.Length - 1; i++)
        {
            Card t = keys[i + 1];
            int j = i;
            while (j >= 0 && Compare(t, keys[j], rules) < 0)
            {
                keys[j + 1] = keys[j];
                j--;
            }
            keys[j + 1] = t;
        }
    }

    private static void SwapIfGreater(ref Card i, ref Card j, Rules rules)
    {
        if (Compare(i, j, rules) > 0)
            Swap(ref i, ref j);
    }

    private static void Swap(ref Card i, ref Card j) => (i, j) = (j, i);

/// <summary>
/// The same native <c>List&lt;T&gt;.Sort()</c> permutation as <see cref="Sort"/>, carrying a parallel
/// origin array so a caller can tell which equal-valued card ended up at each sorted position (the
/// generation and auto-play effects refer to card instances, not just values).
/// </summary>
public static void SortWithOrigins(Span<Card> keys, Span<int> origins, Rules rules)
{
    if (keys.Length != origins.Length)
        throw new ArgumentException("keys and origins must have the same length.", nameof(origins));
    if (keys.Length > 1)
        IntroSortWithOrigins(keys, origins, 2 * (BitOperations.Log2((uint)keys.Length) + 1), rules);
}

private static void IntroSortWithOrigins(Span<Card> keys, Span<int> origins, int depthLimit, Rules rules)
{
    int partitionSize = keys.Length;
    while (partitionSize > 1)
    {
        if (partitionSize <= IntrosortSizeThreshold)
        {
            if (partitionSize == 2)
            {
                SwapIfGreaterWithOrigins(ref keys[0], ref keys[1], ref origins[0], ref origins[1], rules);
                return;
            }
            if (partitionSize == 3)
            {
                SwapIfGreaterWithOrigins(ref keys[0], ref keys[1], ref origins[0], ref origins[1], rules);
                SwapIfGreaterWithOrigins(ref keys[0], ref keys[2], ref origins[0], ref origins[2], rules);
                SwapIfGreaterWithOrigins(ref keys[1], ref keys[2], ref origins[1], ref origins[2], rules);
                return;
            }
            InsertionSortWithOrigins(keys[..partitionSize], origins[..partitionSize], rules);
            return;
        }
        if (depthLimit == 0)
        {
            HeapSortWithOrigins(keys[..partitionSize], origins[..partitionSize], rules);
            return;
        }
        depthLimit--;
        int pivot = PickPivotAndPartitionWithOrigins(keys[..partitionSize], origins[..partitionSize], rules);
        IntroSortWithOrigins(keys[(pivot + 1)..partitionSize], origins[(pivot + 1)..partitionSize], depthLimit, rules);
        partitionSize = pivot;
    }
}

private static int PickPivotAndPartitionWithOrigins(Span<Card> keys, Span<int> origins, Rules rules)
{
    int last = keys.Length - 1;
    int middle = last >> 1;
    SwapIfGreaterWithOrigins(ref keys[0], ref keys[middle], ref origins[0], ref origins[middle], rules);
    SwapIfGreaterWithOrigins(ref keys[0], ref keys[last], ref origins[0], ref origins[last], rules);
    SwapIfGreaterWithOrigins(ref keys[middle], ref keys[last], ref origins[middle], ref origins[last], rules);

    int nextToLast = keys.Length - 2;
    Card pivot = keys[middle];
    SwapWithOrigins(ref keys[middle], ref keys[nextToLast], ref origins[middle], ref origins[nextToLast]);

    int left = 0;
    int right = nextToLast;
    while (left < right)
    {
        while (left < nextToLast && Compare(pivot, keys[++left], rules) > 0)
        {
        }
        while (right > 0 && Compare(pivot, keys[--right], rules) < 0)
        {
        }
        if (left >= right)
            break;
        SwapWithOrigins(ref keys[left], ref keys[right], ref origins[left], ref origins[right]);
    }

    if (left != nextToLast)
        SwapWithOrigins(ref keys[left], ref keys[nextToLast], ref origins[left], ref origins[nextToLast]);
    return left;
}

private static void HeapSortWithOrigins(Span<Card> keys, Span<int> origins, Rules rules)
{
    int n = keys.Length;
    for (int i = n >> 1; i >= 1; i--)
        DownHeapWithOrigins(keys, origins, i, n, rules);
    for (int i = n; i > 1; i--)
    {
        SwapWithOrigins(ref keys[0], ref keys[i - 1], ref origins[0], ref origins[i - 1]);
        DownHeapWithOrigins(keys, origins, 1, i - 1, rules);
    }
}

private static void DownHeapWithOrigins(Span<Card> keys, Span<int> origins, int i, int n, Rules rules)
{
    Card d = keys[i - 1];
    int dOrigin = origins[i - 1];
    while (i <= n >> 1)
    {
        int child = 2 * i;
        if (child < n && Compare(keys[child - 1], keys[child], rules) < 0)
            child++;
        if (!(Compare(d, keys[child - 1], rules) < 0))
            break;
        keys[i - 1] = keys[child - 1];
        origins[i - 1] = origins[child - 1];
        i = child;
    }
    keys[i - 1] = d;
    origins[i - 1] = dOrigin;
}

private static void InsertionSortWithOrigins(Span<Card> keys, Span<int> origins, Rules rules)
{
    for (int i = 0; i < keys.Length - 1; i++)
    {
        Card t = keys[i + 1];
        int tOrigin = origins[i + 1];
        int j = i;
        while (j >= 0 && Compare(t, keys[j], rules) < 0)
        {
            keys[j + 1] = keys[j];
            origins[j + 1] = origins[j];
            j--;
        }
        keys[j + 1] = t;
        origins[j + 1] = tOrigin;
    }
}

private static void SwapIfGreaterWithOrigins(ref Card left, ref Card right,
    ref int leftOrigin, ref int rightOrigin, Rules rules)
{
    if (Compare(left, right, rules) > 0)
        SwapWithOrigins(ref left, ref right, ref leftOrigin, ref rightOrigin);
}

private static void SwapWithOrigins(ref Card left, ref Card right, ref int leftOrigin, ref int rightOrigin)
{
    (left, right) = (right, left);
    (leftOrigin, rightOrigin) = (rightOrigin, leftOrigin);
}

}
