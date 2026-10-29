// Native command order cross-checked against sts2 v0.111.0 CardSelectCmd/EventSynchronizer.
// Lifecycle adapted from tests/Differential/Run/NativeDecisionTestSession.cs at 5890e705 (BlitzSolver, MIT).
using BlitzSolver.Import;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NativeRun = MegaCrit.Sts2.Core.Runs.RunState;

namespace BlitzSolver.OfflineHost.Run;

public enum OfflineBoundaryKind { Map, Event, Rest, Combat, Terminal, Rewards, Paused, Shop }
/// <summary>Runtime signals: the library does not infer UI readiness, map enablement or victory from HP.</summary>
public sealed record OfflineRunBoundary(OfflineBoundaryKind Kind, EventModel? Event = null,
    RestSiteRoom? Rest = null, Func<bool>? TravelEnabled = null,
    MerchantRoom? Shop = null, Func<bool>? ShopActive = null, Action? CloseShop = null,
    BlitzSolver.Autoplay.RunStopReason Terminal = BlitzSolver.Autoplay.RunStopReason.Failed, string? Detail = null);

public sealed class RunExecutor
{
    internal sealed record Pending(RunDecisionRequest Request, Action ValidateCurrent,
        Action<RunAction> ValidateBindings, Func<RunAction, Task> Dispatch);

    internal Pending Import(OfflineRunBoundary boundary, NativeRun run, Player player, RunDecisionToken token)
        => boundary.Kind switch
        {
            OfflineBoundaryKind.Map => Bind(RunDecisionImporter.Map(run, token,
                boundary.TravelEnabled ?? throw new InvalidDataException("host-map-readiness-required")),
                (points, _) => RunManager.Instance.EnterMapCoord(points[0].coord)),
            OfflineBoundaryKind.Event => Bind(RunDecisionImporter.Event(boundary.Event
                ?? throw new InvalidDataException("host-event-required"), token),
                (options, _) => NativeCallbacks.ChooseEvent(boundary.Event!, options[0])),
            OfflineBoundaryKind.Rest => Bind(RunDecisionImporter.Rest(boundary.Rest
                ?? throw new InvalidDataException("host-rest-required"), player, token),
                (options, _) => ChooseRest(boundary.Rest!, options[0])),
            _ => throw new InvalidDataException("host-not-decision"),
        };

    internal static Pending Bind<T>(NativeDecisionSnapshot<T> snapshot,
        Func<IReadOnlyList<T>, RunAction, Task> dispatch) where T : class
        => new(snapshot.Request, snapshot.ValidateCurrent, action => snapshot.Resolve(action),
            action => dispatch(snapshot.Resolve(action), action));

    private static async Task ChooseRest(RestSiteRoom room, RestSiteOption option)
    {
        int index = -1;
        for (int i = 0; i < room.Options.Count; i++) if (ReferenceEquals(room.Options[i], option)) index = i;
        if (index < 0) throw new InvalidDataException("host-rest-instance");
        using IDisposable? emptyRewards = option is HealRestSiteOption ? RewardPresentation.AllowEmptyRestRewards(option.Owner) : null;
        await RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(index);
    }
}
