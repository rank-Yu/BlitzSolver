using System.Numerics;

namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Exact port of .NET's <c>GenericArraySortHelper&lt;T&gt;.IntrospectiveSort</c> as used by
/// <c>List&lt;T&gt;.Sort()</c>. The native shuffle sorts with this unstable algorithm before its
/// Fisher-Yates pass, so cards that compare equal but differ in state must be permuted exactly
/// the same way. Comparison follows native <c>CardModel.CompareTo</c>: model id (ordinal), then
/// upgrade level.
/// </summary>
public static class FlashCardSort
{
    private const int IntrosortSizeThreshold = 16;

    public static void Sort(Span<FlashCard> keys, FlashRules rules)
    {
        if (keys.Length > 1)
            IntroSort(keys, 2 * (BitOperations.Log2((uint)keys.Length) + 1), rules);
    }

    public static int Compare(in FlashCard left, in FlashCard right, FlashRules rules)
    {
        int rank = rules.CardSortRank(left.Id).CompareTo(rules.CardSortRank(right.Id));
        if (rank != 0)
            return rank;
        return left.Upgrade.CompareTo(right.Upgrade);
    }

    private static void IntroSort(Span<FlashCard> keys, int depthLimit, FlashRules rules)
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

    private static int PickPivotAndPartition(Span<FlashCard> keys, FlashRules rules)
    {
        int last = keys.Length - 1;
        int middle = last >> 1;
        SwapIfGreater(ref keys[0], ref keys[middle], rules);
        SwapIfGreater(ref keys[0], ref keys[last], rules);
        SwapIfGreater(ref keys[middle], ref keys[last], rules);

        int nextToLast = keys.Length - 2;
        FlashCard pivot = keys[middle];
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

    private static void HeapSort(Span<FlashCard> keys, FlashRules rules)
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

    private static void DownHeap(Span<FlashCard> keys, int i, int n, FlashRules rules)
    {
        FlashCard d = keys[i - 1];
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

    private static void InsertionSort(Span<FlashCard> keys, FlashRules rules)
    {
        for (int i = 0; i < keys.Length - 1; i++)
        {
            FlashCard t = keys[i + 1];
            int j = i;
            while (j >= 0 && Compare(t, keys[j], rules) < 0)
            {
                keys[j + 1] = keys[j];
                j--;
            }
            keys[j + 1] = t;
        }
    }

    private static void SwapIfGreater(ref FlashCard i, ref FlashCard j, FlashRules rules)
    {
        if (Compare(i, j, rules) > 0)
            Swap(ref i, ref j);
    }

    private static void Swap(ref FlashCard i, ref FlashCard j) => (i, j) = (j, i);
}
