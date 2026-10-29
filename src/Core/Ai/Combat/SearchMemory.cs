using System.Runtime.CompilerServices;

namespace Sts2Solver.Ai.Combat;

/// <summary>
/// Raised when a search-owned allocation would cross <see cref="BeamOptions.SearchMemoryBudgetBytes"/>.
/// The solver catches it at the search loop and returns the best result found so far, exactly like the
/// node limit. It deliberately does not derive from <see cref="CapacityException"/> so a single-branch
/// capacity handler cannot turn a budget stop into a dropped child.
/// </summary>
internal sealed class SearchMemoryBudgetException : Exception
{
    internal SearchMemoryBudgetException(string region, long usedBytes, long limitBytes)
        : base($"搜索内存预算不足：region={region} used={usedBytes} limit={limitBytes}。")
    {
        Region = region;
        UsedBytes = usedBytes;
        LimitBytes = limitBytes;
    }

    internal string Region { get; }

    internal long UsedBytes { get; }

    internal long LimitBytes { get; }
}

/// <summary>
/// Logical search-memory ledger. It counts allocated search-owned blocks, never GC or process
/// memory, so the offline host and the game see the same decisions. A limit of zero (the default for
/// decoded legacy requests and direct kernel tests) is unlimited.
/// </summary>
internal sealed class SearchMemoryLedger
{
    /// <summary>Bytes retained by this storage across solves; never decreases.</summary>
    internal long UsedBytes { get; private set; }

    /// <summary>Usage when the current solve started, before any of its own allocations.</summary>
    internal long SolveStartBytes { get; private set; }

    /// <summary>Highest usage observed during the current solve.</summary>
    internal long PeakBytes { get; private set; }

    /// <summary>Limit of the current solve; zero means unlimited.</summary>
    internal long LimitBytes { get; private set; }

    /// <summary>True once the current solve requested a block that would cross the limit.</summary>
    internal bool Exceeded { get; private set; }

    /// <summary>Usage at the first rejected allocation of the current solve.</summary>
    internal long ExceededAtBytes { get; private set; }

    /// <summary>Lifetime allocated bytes by accounting region (diagnostics only).</summary>
    internal IReadOnlyDictionary<string, long> AllocatedByRegion => _byRegion;

    private readonly Dictionary<string, long> _byRegion = new(StringComparer.Ordinal);

    internal void BeginSolve(long limitBytes)
    {
        LimitBytes = Math.Max(0, limitBytes);
        SolveStartBytes = UsedBytes;
        PeakBytes = UsedBytes;
        Exceeded = false;
        ExceededAtBytes = 0;
        if (LimitBytes != 0 && UsedBytes > LimitBytes)
        {
            Exceeded = true;
            ExceededAtBytes = UsedBytes;
        }
    }

    /// <summary>
    /// Charges one logical block. The block size is the allocated array/chunk length, not the live
    /// payload. A rejected block is not counted, so <see cref="UsedBytes"/> never exceeds the limit.
    /// </summary>
    internal void Charge(long bytes, string region)
    {
        if (bytes <= 0)
            return;
        if (Exceeded)
            throw new SearchMemoryBudgetException(region, ExceededAtBytes, LimitBytes);
        if (LimitBytes != 0 && UsedBytes + bytes > LimitBytes)
        {
            Exceeded = true;
            ExceededAtBytes = UsedBytes;
            throw new SearchMemoryBudgetException(region, UsedBytes, LimitBytes);
        }
        UsedBytes += bytes;
        _byRegion[region] = _byRegion.GetValueOrDefault(region) + bytes;
        if (UsedBytes > PeakBytes)
            PeakBytes = UsedBytes;
    }

    /// <summary>Releases a retained block that was replaced; used only for the growable int scratch.</summary>
    internal void Release(long bytes)
    {
        if (bytes <= 0)
            return;
        UsedBytes = Math.Max(0, UsedBytes - bytes);
        long scratch = _byRegion.GetValueOrDefault("scratch") - bytes;
        _byRegion["scratch"] = Math.Max(0, scratch);
    }
}

/// <summary>
/// Reusable search-owned memory. Chunks and fixed arrays are retained between solves of the same
/// storage; the next solve rents them again before allocating. The ledger counts the retained blocks,
/// so a multi-round widening loop stays under one budget instead of paying for every round's
/// high-water mark.
/// </summary>
internal sealed class SearchStorage
{
    private readonly List<Array> _chunks = [];
    private readonly List<(Type Type, int Length, Array Array)> _arrays = [];
    private readonly Dictionary<(Type Type, int Length), Stack<Array>> _freeChunks = [];
    private readonly Dictionary<(Type Type, int Length), Stack<Array>> _freeArrays = [];
    private int[] _intScratch = [];

    internal SearchMemoryLedger Memory { get; } = new();

    /// <summary>Total bytes allocated by this storage over its lifetime.</summary>
    internal long AllocatedBytes => Memory.UsedBytes;

    /// <summary>
    /// Growable int scratch for the boundary node compaction (mark/remap arrays). It survives solves
    /// and is rounded to a power of two so the next solve reuses it.
    /// </summary>
    internal int[] IntScratch(int minimumLength)
    {
        if (_intScratch.Length >= minimumLength)
            return _intScratch;
        int capacity = 1;
        while (capacity < minimumLength)
            capacity <<= 1;
        long previous = (long)_intScratch.Length * sizeof(int);
        long next = (long)capacity * sizeof(int);
        Memory.Release(previous);
        Memory.Charge(next, "scratch");
        _intScratch = new int[capacity];
        return _intScratch;
    }

    /// <summary>
    /// Starts one solve: clears the budget-exceeded flag, makes every retained block rentable, and
    /// keeps the retained bytes on the ledger.
    /// </summary>
    internal void BeginSolve(long limitBytes)
    {
        Memory.BeginSolve(limitBytes);
        _freeChunks.Clear();
        for (int index = 0; index < _chunks.Count; index++)
        {
            Array chunk = _chunks[index];
            Push(_freeChunks, (chunk.GetType().GetElementType()!, chunk.Length), chunk);
        }
        _freeArrays.Clear();
        for (int index = 0; index < _arrays.Count; index++)
        {
            (Type type, int length, Array array) = _arrays[index];
            Push(_freeArrays, (type, length), array);
        }
    }

    /// <summary>Rents a fixed-size chunk, reusing a retained block of the same element type and length.</summary>
    internal T[] RentChunk<T>(int length)
        => Rent<T>(length, _freeChunks, chunk: true);

    /// <summary>Rents a fixed-size array, reusing a retained block of the same element type and length.</summary>
    internal T[] RentArray<T>(int length)
        => Rent<T>(length, _freeArrays, chunk: false);

    private T[] Rent<T>(int length, Dictionary<(Type Type, int Length), Stack<Array>> free,
        bool chunk)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        (Type Type, int Length) key = (typeof(T), length);
        if (free.TryGetValue(key, out Stack<Array>? stack) && stack.Count > 0)
        {
            T[] reused = (T[])stack.Pop();
            Array.Clear(reused);
            return reused;
        }
        long bytes = (long)length * Unsafe.SizeOf<T>();
        Memory.Charge(bytes, chunk ? "chunk" : "array");
        T[] array = new T[length];
        if (chunk)
            _chunks.Add(array);
        else
            _arrays.Add((typeof(T), length, array));
        return array;
    }

    private static void Push<TKey>(Dictionary<TKey, Stack<Array>> free, TKey key, Array array)
        where TKey : notnull
    {
        if (!free.TryGetValue(key, out Stack<Array>? stack))
        {
            stack = new Stack<Array>();
            free.Add(key, stack);
        }
        stack.Push(array);
    }
}

/// <summary>
/// Field storage for the structure-of-arrays route nodes and the routing/commitment side tables.
/// Chunks are rented from the reusable <see cref="SearchStorage"/>; <see cref="Clear"/> only resets
/// the count so a compaction never copies the whole store.
/// </summary>
internal sealed class ChunkedList<T>
{
    private const int ChunkShift = 12;
    private const int ChunkSize = 1 << ChunkShift;
    private const int ChunkMask = ChunkSize - 1;

    private readonly SearchStorage _storage;
    private readonly List<T[]> _chunks = [];

    internal ChunkedList(SearchStorage storage)
        => _storage = storage;

    internal int Count { get; private set; }

    internal T this[int index]
    {
        get => _chunks[index >> ChunkShift][index & ChunkMask];
        set
        {
            int chunk = index >> ChunkShift;
            if (chunk >= _chunks.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _chunks[chunk][index & ChunkMask] = value;
        }
    }

    internal void Add(T value)
    {
        int chunk = Count >> ChunkShift;
        if (chunk == _chunks.Count)
            _chunks.Add(_storage.RentChunk<T>(ChunkSize));
        _chunks[chunk][Count & ChunkMask] = value;
        Count++;
    }

    internal void Move(int from, int to)
    {
        if (to >= Count)
            throw new ArgumentOutOfRangeException(nameof(to));
        this[to] = this[from];
    }

    internal void Truncate(int count)
    {
        if ((uint)count > (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(count));
        Count = count;
    }

    internal void Clear()
    {
        Count = 0;
        _chunks.Clear();
    }
}

