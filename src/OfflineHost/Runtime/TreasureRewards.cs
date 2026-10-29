// Preserve sts2 v0.111.0 TreasureRoom.DoExtraRewardsIfNeeded and RewardsSet.Offer lifecycle.
// Only the empty native set is supported; hooks/generation/BeginRewardsSet still run normally.
using Sts2Solver.Simulation.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Solver.OfflineHost.Run;

internal sealed class TreasureRewards(Player player, TreasureRoom room) : IDisposable
{
    private readonly Player _player = player;
    private readonly TreasureRoom _room = room;
    private static readonly AsyncLocal<TreasureRewards?> Current = new();
    private bool _open = true;
    private RewardsSet? _set;
    private Task? _offer;
    internal static TreasureRewards Begin(Player player, TreasureRoom room)
    {
        if (Current.Value != null || !OfflineRunHost.IsExecuting)
            throw new UnsupportedException("host-treasure-rewards-scope");
        var scope = new TreasureRewards(player, room); Current.Value = scope; return scope;
    }
    internal static bool CheckOffer(RewardsSet set)
    {
        if (Current.Value is not { } scope) return false;
        if (!scope._open || !OfflineRunHost.IsExecuting || scope._set != null
            || !ReferenceEquals(set.Player, scope._player) || !ReferenceEquals(set.Room, scope._room)
            || set.Rewards.Count != 0 || !ReferenceEquals(scope._player.RunState.CurrentRoom, scope._room))
            throw new UnsupportedException("host-unsupported-treasure-extra-rewards");
        scope._set = set;
        return true;
    }
    internal static void CheckGenerated(RewardsSet set)
    {
        if (Current.Value is not { } scope) return;
        if (!scope._open || !OfflineRunHost.IsExecuting || !ReferenceEquals(scope._set, set)
            || set.Rewards.Count != 0 || !ReferenceEquals(set.Player, scope._player) || !ReferenceEquals(set.Room, scope._room))
            throw new UnsupportedException("host-unsupported-treasure-extra-rewards");
    }
    internal static void TrackOffer(RewardsSet set, Task task)
    {
        if (Current.Value is not { } scope) return;
        if (!ReferenceEquals(scope._set, set) || scope._offer != null)
            throw new InvalidDataException("host-treasure-extra-offer-identity");
        scope._offer = task;
    }
    internal async Task Settle(CancellationToken token)
    {
        await (_offer ?? throw new InvalidDataException("host-treasure-extra-offer-missing"));
        token.ThrowIfCancellationRequested();
        if (_set == null || !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(_set))
            throw new InvalidDataException("host-treasure-extra-incomplete");
    }
    public void Dispose() { _open = false; if (ReferenceEquals(Current.Value, this)) Current.Value = null; }
}
