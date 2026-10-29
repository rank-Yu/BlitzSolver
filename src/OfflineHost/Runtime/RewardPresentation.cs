// UI lifecycle follows sts2 v0.111.0 CombatRoom.OfferRoomEndRewards,
// RewardsSet.Offer and NRewardsScreen.OnProceedButtonPressed. Native rewards remain authoritative.
using BlitzSolver.OfflineHost.Run;
using BlitzSolver.Simulation.Combat;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace BlitzSolver.OfflineHost.Run;

internal sealed class RewardPresentation : IAsyncDisposable
{
    private static readonly AsyncLocal<RewardPresentation?> Offering = new();
    private static readonly AsyncLocal<EmptyRestRewards?> RestRewards = new();
    private sealed class EmptyRestRewards(Player player) : IDisposable
    {
        internal readonly Player Player = player;
        internal RewardsSet? Set;
        internal bool Open = true;
        public void Dispose() { Open = false; RestRewards.Value = null; }
    }
    internal static bool EmptyRestRewardsActive => RestRewards.Value is { Open: true };
    internal static IDisposable AllowEmptyRestRewards(Player player)
    {
        if (!OfflineRunHost.IsExecuting || RestRewards.Value != null || _owner != null)
            throw new UnsupportedException("host-rest-reward-scope");
        var scope = new EmptyRestRewards(player); RestRewards.Value = scope; return scope;
    }
    internal static void CheckGenerated(RewardsSet set)
    {
        TreasureRewards.CheckGenerated(set);
        // Heal always offers a custom set, even when empty. Hooks still run in the original
        // GenerateWithoutOffering. Reject generated choices before BeginRewardsSet can select.
        if (RestRewards.Value is { } scope && ReferenceEquals(scope.Set, set)
            && (!scope.Open || set.Room != null || set.Rewards.Count != 0))
            throw new UnsupportedException("host-unsupported-rest-rewards");
    }
    private static RewardPresentation? _owner;
    private readonly Player _player;
    private readonly CombatRoom _room;
    private readonly CancellationTokenSource _lifetime;
    private readonly Func<RewardsSet, Task> _selector;
    private readonly TaskCompletionSource _shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _offer;
    private Task? _generation;
    private Task? _start;
    private bool _closed;
    internal RewardsSet? Set { get; private set; }
    internal bool Proceeded { get; private set; }
    internal bool IsBoss => _room.RoomType == RoomType.Boss;
    internal bool Active => !_closed && !Proceeded && Set != null;
    internal bool TravelEnabled => Proceeded && !_closed && !CombatManager.Instance.IsInProgress
        && ReferenceEquals(_player.RunState.CurrentRoom, _room)
        && RunManager.Instance.ActionExecutor.FinishedExecutingActions().IsCompleted;

    internal RewardPresentation(Player player, CombatRoom room, CancellationToken token)
    {
        if (_owner != null || RewardsSet.testSelector != null) throw new InvalidDataException("host-reward-selector-busy");
        if (!ReferenceEquals(player.RunState.CurrentRoom, room) || !room.IsPreFinished || CombatManager.Instance.IsInProgress)
            throw new InvalidDataException("host-reward-room-not-finished");
        _player = player; _room = room;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        _selector = Show;
        _owner = this;
        RewardsSet.testSelector = _selector;
        RunManager.Instance.RewardsSetSynchronizer.RewardsSkippedDuringRoomExit += RoomExited;
    }
    internal Task Start()
    {
        if (_start != null) throw new InvalidDataException("host-reward-start-duplicate");
        return _start = StartCore();
    }
    private async Task StartCore()
    {
        RewardPresentation? previous = Offering.Value;
        Offering.Value = this;
        try
        {
            _generation = _room.OfferRoomEndRewards();
            await _generation;
            Task offer = _offer ?? throw new InvalidDataException("host-reward-offer-missing");
            Task first = await Task.WhenAny(_shown.Task, offer).WaitAsync(_lifetime.Token);
            if (ReferenceEquals(first, offer)) await offer;
            await _shown.Task.WaitAsync(_lifetime.Token);
        }
        finally { Offering.Value = previous; }
    }
    internal static void CheckOffer(RewardsSet set)
    {
        if (TreasureRewards.CheckOffer(set)) return;
        RewardPresentation? owner = Offering.Value;
        if (owner == null)
        {
            if (RestRewards.Value is { } rest)
            {
                if (OfflineRunHost.IsExecuting && rest.Open && rest.Set == null && _owner == null
                    && ReferenceEquals(rest.Player, set.Player) && set.Room == null && set.Rewards.Count == 0)
                { rest.Set = set; return; }
                // A captured ExecutionContext can outlive Dispose. It must never fall through
                // to TestMode's default reward selector after its execution/scope has closed.
                throw new UnsupportedException("host-unsupported-native-entry:RewardsSet.Offer");
            }
            if (_owner != null || OfflineRunHost.HasExecutionContext) throw new UnsupportedException("host-unsupported-native-entry:RewardsSet.Offer");
            return;
        }
        owner._lifetime.Token.ThrowIfCancellationRequested();
        if (owner._closed || owner.Set != null || !ReferenceEquals(set.Player, owner._player)
            || !ReferenceEquals(set.Room, owner._room))
            throw new UnsupportedException("host-unrelated-reward-offer");
        owner.Set = set;
    }
    internal static void TrackOffer(RewardsSet set, Task task)
    {
        TreasureRewards.TrackOffer(set, task);
        RewardPresentation? owner = Offering.Value;
        if (owner != null && ReferenceEquals(owner.Set, set))
        {
            if (owner._offer != null) throw new InvalidDataException("host-duplicate-reward-offer");
            owner._offer = task;
        }
    }
    private async Task Show(RewardsSet set)
    {
        if (_closed || !ReferenceEquals(Offering.Value, this) || !ReferenceEquals(Set, set)
            || !ReferenceEquals(_room, _player.RunState.CurrentRoom))
            throw new UnsupportedException("host-unrelated-reward-screen");
        _shown.TrySetResult();
        await _released.Task.WaitAsync(_lifetime.Token);
    }
    internal bool SetCompleted => Set != null && RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(Set);
    // Boss Proceed enqueues a vote that immediately enters the next act in singleplayer.
    // Stop before that button, after the real set has completed; release presentation only.
    internal async Task SettleCompletedBoss(CancellationToken token)
    {
        if (!Active || _room.RoomType != RoomType.Boss || !SetCompleted
            || !ReferenceEquals(_player.RunState.CurrentRoom, _room))
            throw new InvalidDataException("host-boss-rewards-incomplete");
        _released.TrySetResult();
        await (_offer ?? throw new InvalidDataException("host-reward-offer-missing")).WaitAsync(token);
        if (_generation != null) await _generation.WaitAsync(token);
        if (_start != null) await _start.WaitAsync(token);
    }
    internal async Task Leave()
    {
        if (!Active || Set == null) throw new InvalidDataException("host-reward-screen-closed");
        if (IsBoss) throw new UnsupportedException("host-boss-proceed-is-act-transition");
        // Terminal Proceed opens the map. Native EnterMapCoord owns skipping untaken rewards.
        await RunManager.Instance.ProceedFromTerminalRewardsScreen();
        Proceeded = true;
        if (RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(Set))
        {
            _released.TrySetResult();
            if (_offer != null) await _offer;
        }
    }
    private void RoomExited()
    {
        if (Proceeded) _released.TrySetResult();
    }
    internal async Task SettleRoomExit(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Proceeded || Set == null || ReferenceEquals(_room, _player.RunState.CurrentRoom)
            || !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(Set))
            throw new InvalidDataException("host-reward-exit-unconfirmed");
        _released.TrySetResult();
        await (_offer ?? throw new InvalidDataException("host-reward-offer-missing")).WaitAsync(token);
        if (_generation != null) await _generation.WaitAsync(token);
        if (_start != null) await _start.WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if (_closed || ReferenceEquals(_room, _player.RunState.CurrentRoom))
            throw new InvalidDataException("host-reward-exit-unconfirmed");
    }
    public async ValueTask DisposeAsync()
    {
        if (_closed) return;
        _closed = true;
        _lifetime.Cancel();
        try
        {
            try { if (_generation != null) await _generation; }
            finally
            {
                try { if (_offer != null) await _offer; }
                finally { if (_start != null) await _start; }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally
        {
            RunManager.Instance.RewardsSetSynchronizer.RewardsSkippedDuringRoomExit -= RoomExited;
            if (ReferenceEquals(RewardsSet.testSelector, _selector)) RewardsSet.testSelector = null;
            if (ReferenceEquals(_owner, this)) _owner = null;
            _lifetime.Dispose();
        }
    }
}
