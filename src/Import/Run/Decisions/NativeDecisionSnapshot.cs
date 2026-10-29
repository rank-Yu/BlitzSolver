using Sts2Solver.Simulation.Run;

namespace Sts2Solver.Import;

/// <summary>Immutable request-local native bindings. Game references remain outside the pure request.
/// This is not a session: the host must own fresh tokens, consumption, busy state and cancellation dispatch.</summary>
public sealed class NativeDecisionSnapshot<T> where T : class
{
    private readonly RunDecisionRequest _request;
    private readonly T[] _bindings;
    private readonly Func<NativeDecisionSnapshot<T>> _readCurrent;
    private readonly Func<bool> _sameContext;

    internal NativeDecisionSnapshot(RunDecisionRequest request, T[] bindings,
        Func<NativeDecisionSnapshot<T>> readCurrent, Func<bool> sameContext)
    {
        RunDecisionProtocol.ValidateRequest(request);
        if (bindings.Length != request.OptionCount)
            throw new InvalidDataException("native-bindings: count differs from request");
        HashSet<T> instances = new(ReferenceEqualityComparer.Instance);
        foreach (T binding in bindings)
            if (binding == null || !instances.Add(binding))
                throw new InvalidDataException("native-bindings: null or repeated native instance");
        _request = request;
        _bindings = bindings;
        _readCurrent = readCurrent;
        _sameContext = sameContext;
    }

    /// <summary>Returns a value copy. Mutating it cannot change the snapshot.</summary>
    public RunDecisionRequest Request => _request;

    /// <summary>Re-reads visible data and checks order, instance identity, payloads and source context.
    /// The host calls this immediately before consuming the request and executing any native action.</summary>
    public void ValidateCurrent()
    {
        if (!_sameContext()) throw new InvalidDataException("native-stale: source context changed");
        NativeDecisionSnapshot<T> current = _readCurrent();
        if (_bindings.Length != current._bindings.Length || !Same(_request, current._request))
            throw new InvalidDataException("native-stale: visible choice changed");
        for (int index = 0; index < _bindings.Length; index++)
            if (!ReferenceEquals(_bindings[index], current._bindings[index]))
                throw new InvalidDataException("native-stale: candidate identity or order changed");
    }

    /// <summary>Validates the entire action before resolving any id. Cancel returns no instances;
    /// the host must still dispatch cancellation separately from an empty Select.</summary>
    public IReadOnlyList<T> Resolve(in RunAction action)
    {
        RunDecisionProtocol.ValidateAction(_request, action);
        T[] selected = new T[action.Count];
        for (int index = 0; index < action.Count; index++)
            selected[index] = _bindings[RunDecisionProtocol.ResolveOptionIndex(_request, action.OptionIds[index])];
        return Array.AsReadOnly(selected);
    }

    private static bool Same(in RunDecisionRequest a, in RunDecisionRequest b)
    {
        if (a.Token != b.Token || a.Source != b.Source || a.PageKey != b.PageKey
            || a.OptionCount != b.OptionCount || a.MinSelect != b.MinSelect || a.MaxSelect != b.MaxSelect
            || a.FactCount != b.FactCount || a.CardCount != b.CardCount || a.KeyByteCount != b.KeyByteCount
            || a.Kind != b.Kind || a.SelectionPurpose != b.SelectionPurpose
            || a.SelectionPreviewKnowledge != b.SelectionPreviewKnowledge
            || a.Cancelable != b.Cancelable || a.RequireConfirmation != b.RequireConfirmation) return false;
        for (int i = 0; i < a.OptionCount; i++) if (!a.Options[i].Equals(b.Options[i])) return false;
        for (int i = 0; i < a.FactCount; i++) if (!a.Facts[i].Equals(b.Facts[i])) return false;
        for (int i = 0; i < a.CardCount; i++) if (!a.Cards[i].Equals(b.Cards[i])) return false;
        for (int i = 0; i < a.KeyByteCount; i++) if (a.Keys[i] != b.Keys[i]) return false;
        return true;
    }
}
