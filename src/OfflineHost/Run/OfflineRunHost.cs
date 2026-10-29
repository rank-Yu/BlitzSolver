using BlitzSolver.Autoplay;
using BlitzSolver.Ai.Run;
using BlitzSolver.Import;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Runs;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;

namespace BlitzSolver.OfflineHost.Run;

/// <summary>Single native session. The embedding runtime owns game initialization and boundary discovery.</summary>
public sealed partial class OfflineRunHost : IRunPilotHost, IAsyncDisposable
{
    private static long _nextSession;
    private static OfflineRunHost? _owner;
    private static readonly AsyncLocal<Execution?> Active = new();
    // Combat turn-loop continuations may inherit the completed Map dispatch context.
    // The combat delegate owns its own native selector/queue lifecycle in that phase.
    internal static bool HasExecutionContext => Active.Value is { } execution
        && (execution.Open || _owner?._inCombatCallback != true);
    internal static bool IsExecuting => Active.Value is { Open: true };
    private readonly object _gate = new();
    private readonly NativeRun _run;
    private readonly Player _player;
    private readonly Func<CancellationToken, ValueTask<OfflineRunBoundary>> _boundaries;
    private readonly Func<CancellationToken, ValueTask<RunCombatResult>> _combat;
    private readonly RunExecutor _executor = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ulong _session = checked((ulong)Interlocked.Increment(ref _nextSession));
    private ulong _revision;
    private RunExecutor.Pending? _pending;
    private Task? _execution;
    private bool _disposed;
    private bool _waiting;
    private bool _combatPending;
    private bool _inCombatCallback;
    private bool _shopRemoval;
    internal static bool IsShopRemoval => Active.Value is { Open: true } scope && scope.Host._shopRemoval;
    private CancellationTokenSource? _operation;
    private CancellationTokenSource? _boundaryOperation;
    private sealed record Execution(OfflineRunHost Host, RunDecisionCallback Decide, CancellationToken Token)
    { public bool Open { get; set; } = true; }

    public OfflineRunHost(NativeRun run, Player player,
        Func<CancellationToken, ValueTask<OfflineRunBoundary>> boundaries,
        Func<CancellationToken, ValueTask<RunCombatResult>>? combat = null)
    {
        _run = run; _player = player; _boundaries = boundaries;
        _combat = combat ?? (_ => ValueTask.FromResult(new RunCombatResult(RunStopReason.Paused, "combat-boundary")));
        if (Interlocked.CompareExchange(ref _owner, this, null) != null)
        { _lifetime.Dispose(); throw new InvalidDataException("host-native-session-busy"); }
        bool acquired = false;
        try
        {
            NativeCallbacks.Install();
            acquired = true;
        }
        finally
        {
            if (!acquired) { _lifetime.Dispose(); Interlocked.CompareExchange(ref _owner, null, this); }
        }
    }

    private void CheckAvailable(bool executing = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lifetime.Token.ThrowIfCancellationRequested();
        if (!executing && (_waiting || _execution is { IsCompleted: false })) throw new InvalidDataException("host-busy");
    }
    private RunDecisionToken NextToken() => new(_session, checked(++_revision));
    private RunView Fresh(RunDecisionRequest request)
        => RunView.From(RunStateImporter.Import(_run, _player)).WithRequest(request);

    private void ValidateRun()
    {
        if (!ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), _run)
            || _run.Players.Count != 1 || !ReferenceEquals(_run.Players[0], _player))
        {
            _pending = null; _combatPending = false;
            throw new InvalidDataException("host-run-changed");
        }
    }
    public async ValueTask<RunPilotBoundary> WaitForBoundaryAsync(CancellationToken token)
    {
        CancellationTokenSource linked;
        lock (_gate)
        {
            CheckAvailable();
            if (_pending != null || _combatPending) throw new InvalidDataException("host-pending");
            _waiting = true;
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            _boundaryOperation = linked;
        }
        try
        {
            OfflineRunBoundary boundary = await _boundaries(linked.Token).AsTask().WaitAsync(linked.Token);
            lock (_gate)
            {
                CheckAvailable(executing: true); linked.Token.ThrowIfCancellationRequested();
                if (_pending != null || _combatPending) throw new InvalidDataException("host-pending");
                if (boundary.Kind == OfflineBoundaryKind.Paused)
                { ValidateRun(); return new(RunBoundaryKind.Paused, Detail: boundary.Detail); }
                if (boundary.Kind == OfflineBoundaryKind.Terminal)
                    return new(RunBoundaryKind.Terminal, Terminal: boundary.Terminal, Detail: boundary.Detail);
                ValidateRun();
                if (boundary.Kind == OfflineBoundaryKind.Combat)
                { _combatPending = true; return new(RunBoundaryKind.Combat); }
                if (boundary.Event != null && !ReferenceEquals(boundary.Event.Owner?.RunState, _run))
                    throw new InvalidDataException("host-event-run-changed");
                RunExecutor.Pending pending = boundary.Kind == OfflineBoundaryKind.Rewards
                    ? RewardPending(NextToken()) : boundary.Kind == OfflineBoundaryKind.Treasure
                        ? TreasurePending(boundary.Treasure ?? throw new InvalidDataException("host-treasure-required"), NextToken()) : boundary.Kind == OfflineBoundaryKind.Shop
                        ? ShopPending(boundary, NextToken()) : _executor.Import(boundary, _run, _player, NextToken());
                RunView view = Fresh(pending.Request); _pending = pending;
                return new(RunBoundaryKind.Decision, view);
            }
        }
        finally { lock (_gate) { _waiting = false; _boundaryOperation = null; linked.Dispose(); } }
    }

    public ValueTask ExecuteAsync(RunPilotAnswer answer, RunDecisionCallback decideChild, CancellationToken token)
    {
        lock (_gate)
        {
            CheckAvailable(); token.ThrowIfCancellationRequested();
            RunExecutor.Pending pending = _pending ?? throw new InvalidDataException("host-no-pending");
            ValidateRun(); Validate(pending, answer);
            // Consumed before native dispatch; validation failure also invalidates the stale pending request.
            _pending = null;
            TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _execution = ready.Task;
            Task task = ExecuteCore(pending, answer.Action, decideChild, token, ready);
            return new(task);
        }
    }
    private async Task ExecuteCore(RunExecutor.Pending pending, RunAction action,
        RunDecisionCallback decide, CancellationToken token, TaskCompletionSource settled)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        lock (_gate) { _operation = linked; }
        Execution scope = new(this, decide, linked.Token); Execution? previous = Active.Value; Active.Value = scope;
        try
        {
            using IDisposable selector = CardSelectCmd.UseSelector(new Selector(this));
            linked.Token.ThrowIfCancellationRequested(); await pending.Dispatch(action); linked.Token.ThrowIfCancellationRequested();
        }
        finally
        {
            scope.Open = false; Active.Value = previous;
            lock (_gate) { _pending = null; _operation = null; }
            settled.TrySetResult();
        }
    }
    private void Validate(RunExecutor.Pending pending, RunPilotAnswer answer)
    {
        RunDecisionProtocol.ValidateAction(pending.Request, answer.Action);
        if (answer.Action.Kind == RunActionKind.Select && pending.Request.RequireConfirmation != 0 && !answer.Confirmed)
            throw new InvalidDataException("host-confirmation-required");
        pending.ValidateBindings(answer.Action);
        bool valid = false;
        try { pending.ValidateCurrent(); valid = true; }
        finally { if (!valid) _pending = null; }
    }

    private async Task<IEnumerable<CardModel>> SelectAsync(IEnumerable<CardModel> options, int minimum, int maximum)
    {
        Execution scope = Active.Value ?? throw new InvalidDataException("host-unrelated-card-callback");
        NativeCallbacks.Selection selection = NativeCallbacks.CurrentSelection
            ?? throw new InvalidDataException("host-card-prefs-missing");
        if (!ReferenceEquals(scope.Host, this) || !scope.Open || !ReferenceEquals(selection.Player, _player)
            || selection.CallbackMin != minimum || selection.CallbackMax != maximum)
            throw new InvalidDataException("host-unrelated-card-callback");
        if (Interlocked.Exchange(ref selection.Consumed, 1) != 0) throw new InvalidDataException("host-card-prefs-consumed");
        scope.Token.ThrowIfCancellationRequested();
        CardModel[] cards = options.ToArray();
        NativeDecisionSnapshot<CardModel> snapshot;
        RunExecutor.Pending pending;
        RunView view;
        lock (_gate)
        {
            CheckAvailable(executing: true);
            if (_pending != null) throw new InvalidDataException("host-child-busy");
            snapshot = RunDecisionImporter.CardSelect(_player, cards, selection.Prefs, selection.Kind, NextToken(),
                _shopRemoval ? RunSelectionPurpose.Remove : selection.Kind switch
                {
                    NativeCardSelectionKind.DeckUpgrade => RunSelectionPurpose.Upgrade,
                    NativeCardSelectionKind.DeckRemove => RunSelectionPurpose.Remove,
                    NativeCardSelectionKind.DeckTransform => RunSelectionPurpose.Transform,
                    NativeCardSelectionKind.DeckEnchant => RunSelectionPurpose.Enchant,
                    _ => RunSelectionPurpose.None,
                }, selection.Enchantment, selection.Amount, selection.Filter);
            pending = RunExecutor.Bind(snapshot, (_, _) => Task.CompletedTask);
            Action validate = pending.ValidateCurrent;
            pending = pending with { ValidateCurrent = () =>
            {
                ValidateRun();
                if (!Enumerable.SequenceEqual<CardModel>(options, cards, ReferenceEqualityComparer.Instance))
                    throw new InvalidDataException("host-card-candidates-changed");
                validate();
            } };
            view = Fresh(pending.Request); _pending = pending;
        }
        try
        {
            RunPilotAnswer answer = await scope.Decide(view, scope.Token).AsTask().WaitAsync(scope.Token);
            lock (_gate)
            {
                CheckAvailable(executing: true); scope.Token.ThrowIfCancellationRequested();
                if (!scope.Open || !ReferenceEquals(_pending, pending)) throw new InvalidDataException("host-child-expired");
                Validate(pending, answer); _pending = null;
                return answer.Action.Kind == RunActionKind.Cancel ? Array.Empty<CardModel>() : snapshot.Resolve(answer.Action);
            }
        }
        finally { lock (_gate) { if (ReferenceEquals(_pending, pending)) _pending = null; } }
    }
    public ValueTask<RunCombatResult> RunCombatAsync(CancellationToken token)
    {
        lock (_gate)
        {
            CheckAvailable(); token.ThrowIfCancellationRequested(); ValidateRun();
            if (!_combatPending || _pending != null) throw new InvalidDataException("host-no-combat-boundary");
            _combatPending = false;
            TaskCompletionSource settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _execution = settled.Task;
            return new(CombatCore(token, settled));
        }
    }
    private async Task<RunCombatResult> CombatCore(CancellationToken token, TaskCompletionSource settled)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        lock (_gate) { _operation = linked; }
        try { linked.Token.ThrowIfCancellationRequested(); _inCombatCallback = true; RunCombatResult result = await _combat(linked.Token); linked.Token.ThrowIfCancellationRequested(); return result; }
        finally { lock (_gate) { _inCombatCallback = false; _operation = null; } settled.TrySetResult(); }
    }
    public void InvalidatePending()
    {
        lock (_gate) { _pending = null; _combatPending = false; _operation?.Cancel(); _boundaryOperation?.Cancel(); }
    }
    public async ValueTask DisposeAsync()
    {
        Task? execution;
        lock (_gate) { if (_disposed) return; _disposed = true; _pending = null; execution = _execution; }
        try { _lifetime.Cancel(); }
        finally
        {
            try
            {
                try { if (execution != null) await execution; }
                finally { if (_rewards != null) await _rewards.DisposeAsync(); }
            }
            finally { _lifetime.Dispose(); Interlocked.CompareExchange(ref _owner, null, this); }
        }
    }
    private sealed class Selector(OfflineRunHost host) : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
            => host.SelectAsync(options, minSelect, maxSelect);
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<CardRewardAlternative> alternatives)
            => throw new BlitzSolver.Simulation.Combat.UnsupportedException("host-sync-card-reward: asynchronous policy is unsupported");
    }
}
