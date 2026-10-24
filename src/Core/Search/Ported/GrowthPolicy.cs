// Ported from CombatSolver src/Search/GrowthPolicy.cs at a62f1850 (MIT License, Copyright (c) 2026 Torch1230 and contributors); only the members BlitzSolver reads.
namespace CombatSolver;

internal enum GrowthSource
{
    HandOfGreed, TheHunt, Feed, Royalties, Alchemize, GeneticAlgorithm, TheScythe, Goopy, ForbiddenGrimoire,
    MadScience,
}

/// <summary>
/// 成长向量里第三方来源那一半，按 <c>GrowthSourceMirrors</c> 登记的 id 存。
/// </summary>
internal readonly struct GrowthExtras : IEquatable<GrowthExtras>
{
    private const int MaximumValue = 1000;

    private readonly KeyValuePair<string, int>[]? _entries;

    private GrowthExtras(KeyValuePair<string, int>[]? entries) => _entries = entries;

    public bool IsEmpty => _entries is null;

    public ReadOnlySpan<KeyValuePair<string, int>> Entries => _entries;

    public int Total
    {
        get
        {
            int total = 0;
            if (_entries is not null)
            {
                foreach (KeyValuePair<string, int> entry in _entries)
                    total = checked(total + entry.Value);
            }
            return total;
        }
    }

    public int Get(string id)
    {
        int index = IndexOf(id);
        return index < 0 ? 0 : _entries![index].Value;
    }

    public GrowthExtras With(string id, int value)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        int index = IndexOf(id);
        if (index >= 0)
        {
            if (value == _entries![index].Value)
                return this;
            if (value == 0)
            {
                if (_entries.Length == 1)
                    return default;
                KeyValuePair<string, int>[] shrunk = new KeyValuePair<string, int>[_entries.Length - 1];
                Array.Copy(_entries, shrunk, index);
                Array.Copy(_entries, index + 1, shrunk, index, shrunk.Length - index);
                return new GrowthExtras(shrunk);
            }
            KeyValuePair<string, int>[] replaced = (KeyValuePair<string, int>[])_entries.Clone();
            replaced[index] = new KeyValuePair<string, int>(id, value);
            return new GrowthExtras(replaced);
        }
        if (value == 0)
            return this;
        int insertAt = ~index;
        KeyValuePair<string, int>[] grown = new KeyValuePair<string, int>[(_entries?.Length ?? 0) + 1];
        if (_entries is not null)
        {
            Array.Copy(_entries, grown, insertAt);
            Array.Copy(_entries, insertAt, grown, insertAt + 1, _entries.Length - insertAt);
        }
        grown[insertAt] = new KeyValuePair<string, int>(id, value);
        return new GrowthExtras(grown);
    }

    public int Credit(GrowthExtras rewards)
    {
        if (_entries is null || rewards._entries is null)
            return 0;
        int credit = 0;
        foreach (KeyValuePair<string, int> reward in rewards._entries)
            credit = checked(credit + Get(reward.Key) * reward.Value);
        return credit;
    }

    public void ValidateBudgets()
    {
        if (_entries is null)
            return;
        foreach (KeyValuePair<string, int> entry in _entries)
        {
            if (string.IsNullOrEmpty(entry.Key))
                throw new InvalidDataException("Third-party growth source ID must not be empty.");
            if (entry.Value is < 0 or > MaximumValue)
                throw new InvalidDataException($"Growth budget {entry.Key} must be in 0..{MaximumValue} HP.");
        }
    }

    public void AppendFingerprint(ref StateFingerprintBuilder fingerprint)
    {
        if (_entries is null)
            return;
        fingerprint.Add('E');
        foreach (KeyValuePair<string, int> entry in _entries)
        {
            fingerprint.Add(entry.Key);
            fingerprint.Add(entry.Value);
        }
    }

    private int IndexOf(string id)
    {
        if (_entries is null)
            return -1;
        int low = 0;
        int high = _entries.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = string.CompareOrdinal(_entries[middle].Key, id);
            if (comparison == 0)
                return middle;
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return ~low;
    }

    public bool Equals(GrowthExtras other)
    {
        if (_entries is null || other._entries is null)
            return _entries is null && other._entries is null;
        if (ReferenceEquals(_entries, other._entries))
            return true;
        if (_entries.Length != other._entries.Length)
            return false;
        for (int index = 0; index < _entries.Length; index++)
        {
            if (!string.Equals(_entries[index].Key, other._entries[index].Key, StringComparison.Ordinal)
                || _entries[index].Value != other._entries[index].Value)
            {
                return false;
            }
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is GrowthExtras other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        if (_entries is not null)
        {
            foreach (KeyValuePair<string, int> entry in _entries)
            {
                hash.Add(entry.Key, StringComparer.Ordinal);
                hash.Add(entry.Value);
            }
        }
        return hash.ToHashCode();
    }

    public override string ToString()
        => _entries is null
            ? "[]"
            : "[" + string.Join(", ", _entries.Select(entry => entry.Key + " = " + entry.Value)) + "]";

    public static bool operator ==(GrowthExtras left, GrowthExtras right) => left.Equals(right);

    public static bool operator !=(GrowthExtras left, GrowthExtras right) => !left.Equals(right);
}

// Immutable vectors are used for both per-event HP budgets and realized event counts.
internal readonly record struct GrowthValues(
    int HandOfGreed = 0, int TheHunt = 0, int Feed = 0, int Royalties = 0,
    int Alchemize = 0, int GeneticAlgorithm = 0, int TheScythe = 0, int Goopy = 0, int ForbiddenGrimoire = 0,
    int MadScience = 0)
{
    private readonly GrowthExtras _extras;

    public GrowthExtras Extras
    {
        get => _extras;
        init => _extras = value;
    }

    public bool IsEnabled => this != default;
    public int Total => checked(HandOfGreed + TheHunt + Feed + Royalties + Alchemize + GeneticAlgorithm + TheScythe + Goopy
        + ForbiddenGrimoire + MadScience + _extras.Total);

    public int Get(GrowthSource source) => source switch
    {
        GrowthSource.HandOfGreed => HandOfGreed,
        GrowthSource.TheHunt => TheHunt,
        GrowthSource.Feed => Feed,
        GrowthSource.Royalties => Royalties,
        GrowthSource.Alchemize => Alchemize,
        GrowthSource.GeneticAlgorithm => GeneticAlgorithm,
        GrowthSource.TheScythe => TheScythe,
        GrowthSource.Goopy => Goopy,
        GrowthSource.ForbiddenGrimoire => ForbiddenGrimoire,
        GrowthSource.MadScience => MadScience,
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    public GrowthValues With(GrowthSource source, int value) => source switch
    {
        GrowthSource.HandOfGreed => this with { HandOfGreed = value },
        GrowthSource.TheHunt => this with { TheHunt = value },
        GrowthSource.Feed => this with { Feed = value },
        GrowthSource.Royalties => this with { Royalties = value },
        GrowthSource.Alchemize => this with { Alchemize = value },
        GrowthSource.GeneticAlgorithm => this with { GeneticAlgorithm = value },
        GrowthSource.TheScythe => this with { TheScythe = value },
        GrowthSource.Goopy => this with { Goopy = value },
        GrowthSource.ForbiddenGrimoire => this with { ForbiddenGrimoire = value },
        GrowthSource.MadScience => this with { MadScience = value },
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    public int Credit(GrowthValues rewards) => checked(
        HandOfGreed * rewards.HandOfGreed + TheHunt * rewards.TheHunt
        + Feed * rewards.Feed + Royalties * rewards.Royalties
        + Alchemize * rewards.Alchemize + GeneticAlgorithm * rewards.GeneticAlgorithm
        + TheScythe * rewards.TheScythe + Goopy * rewards.Goopy
        + ForbiddenGrimoire * rewards.ForbiddenGrimoire
        + MadScience * rewards.MadScience + _extras.Credit(rewards._extras));

    public bool Satisfies(GrowthValues required)
    {
        foreach (GrowthSource source in Enum.GetValues<GrowthSource>())
        {
            if (Get(source) < required.Get(source))
                return false;
        }
        foreach (KeyValuePair<string, int> entry in required.Extras.Entries)
        {
            if (_extras.Get(entry.Key) < entry.Value)
                return false;
        }
        return true;
    }

    public void ValidateBudgets()
    {
        foreach (GrowthSource source in Enum.GetValues<GrowthSource>())
        {
            if (Get(source) is < 0 or > 1000)
                throw new InvalidDataException($"Growth budget {source} must be in 0..1000 HP.");
        }
        _extras.ValidateBudgets();
    }

    public void AppendFingerprint(ref StateFingerprintBuilder fingerprint)
    {
        fingerprint.Add(HandOfGreed);
        fingerprint.Add(TheHunt);
        fingerprint.Add(Feed);
        fingerprint.Add(Royalties);
        fingerprint.Add(Alchemize);
        fingerprint.Add(GeneticAlgorithm);
        fingerprint.Add(TheScythe);
        fingerprint.Add(Goopy);
        fingerprint.Add(ForbiddenGrimoire);
        fingerprint.Add(MadScience);
        _extras.AppendFingerprint(ref fingerprint);
    }

    public override string ToString()
    {
        string vanilla = $"{nameof(GrowthValues)} {{ {nameof(HandOfGreed)} = {HandOfGreed}, "
            + $"{nameof(TheHunt)} = {TheHunt}, {nameof(Feed)} = {Feed}, {nameof(Royalties)} = {Royalties}, "
            + $"{nameof(Alchemize)} = {Alchemize}, {nameof(GeneticAlgorithm)} = {GeneticAlgorithm}, "
            + $"{nameof(TheScythe)} = {TheScythe}, {nameof(Goopy)} = {Goopy}, {nameof(ForbiddenGrimoire)} = {ForbiddenGrimoire}, "
            + $"{nameof(MadScience)} = {MadScience}";
        return _extras.IsEmpty ? vanilla + " }" : vanilla + $", ThirdParty = {_extras} }}";
    }
}
