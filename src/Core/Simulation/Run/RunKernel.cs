namespace BlitzSolver.Simulation.Run;

/// <summary>
/// The run kernel: one action in, one result out, mirroring the combat kernel's "action + choice"
/// shape. R2b-2 implements the map-point action only. Later phases answer the same
/// <see cref="RunDecisionRequest"/> with the matching <see cref="RunActionKind"/> and reuse this
/// boundary: run state enters through <c>ref</c>, a rejected action leaves it untouched, and a
/// room whose content belongs to a later phase returns <see cref="RunStepStatus.UnsupportedRoom"/>
/// after advancing to the native "room built, not entered" state.
/// </summary>
public sealed class RunKernel
{
    private readonly ContentTable _content;

    public RunKernel(ContentTable content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
    }

    public RunStepResult Step(ref RunState state, in RunAction action)
    {
        RunDecisionRequest request = RunDecisionRequest.Capture(in state);
        if (state.Player.CurrentHp <= 0 || request.Kind == RunDecision.GameOver)
            return new RunStepResult { Status = RunStepStatus.Ended };
        if (action.Kind != RunActionKind.SelectMapPoint)
            return Reject(request, RunStepReject.WrongActionKind, RunStepReason.WrongActionKind,
                $"R2b-2 只实现地图选点，收到 {action.Kind}。");
        if (request.Kind != RunDecision.MapPoint)
            return Reject(request, RunStepReject.NotMapDecision, RunStepReason.NotMapDecision,
                $"当前决策是 {request.Kind}，不是地图选点。");
        if (!RunTravel.TryFindPoint(in state, action.Coord, out _))
            return Reject(request, RunStepReject.UnknownMapPoint, RunStepReason.UnknownMapPoint,
                $"地图没有坐标 {action.Coord}。");
        if (!RunTravel.IsTravelable(in state, action.Coord))
            return Reject(request, RunStepReject.NotTravelable, RunStepReason.NotTravelable,
                $"坐标 {action.Coord} 不是当前的合法选点。");
        if (RunTravel.HoldsFreeTravelRelic(in state, _content))
            return Reject(request, RunStepReject.FreeTravelHook, RunStepReason.FreeTravelHook,
                "持有改写自由移动的遗物，R2b-2 未实现自由移动选项。");

        RunState next = state;
        RunMapEntryResult entry;
        try
        {
            entry = RunMapEntry.Enter(in next, _content, action.Coord);
        }
        catch (ContentUnavailableException exception)
        {
            return Reject(request, RejectFor(exception.Reason), exception.Reason,
                exception.Detail);
        }

        state = entry.Run;
        if (entry.Combat != null)
        {
            return new RunStepResult
            {
                Status = RunStepStatus.Combat,
                Combat = entry.Combat,
                RoomType = entry.RoomType,
                PointType = entry.PointType,
            };
        }
        string reason = entry.RoomType switch
        {
            RunRoomType.Shop => RunStepReason.ShopContent,
            RunRoomType.Treasure => RunStepReason.TreasureContent,
            RunRoomType.RestSite => RunStepReason.RestSiteContent,
            RunRoomType.Event => entry.PointType == RunMapPointType.Ancient
                ? RunStepReason.AncientContent
                : RunStepReason.EventContent,
            _ => RunStepReason.CombatUnsupported,
        };
        return new RunStepResult
        {
            Status = RunStepStatus.UnsupportedRoom,
            Reason = reason,
            Detail = $"{entry.RoomType} 的内容生成属于后续阶段。",
            RoomType = entry.RoomType,
            PointType = entry.PointType,
        };
    }

    private static RunStepResult Reject(in RunDecisionRequest request, RunStepReject reject,
        string reason, string detail) => new()
        {
            Status = RunStepStatus.Rejected,
            Reject = reject,
            Reason = reason,
            Detail = detail,
            Request = request,
        };

    private static RunStepReject RejectFor(string reason) => reason switch
    {
        RunStepReason.RoomStackActive => RunStepReject.RoomStackActive,
        RunStepReason.UnknownPointHook => RunStepReject.UnknownMapPointHook,
        RunStepReason.EventHook => RunStepReject.EventHook,
        RunStepReason.EventAllowedCheck => RunStepReject.EventAllowedCheck,
        RunStepReason.CorruptMap => RunStepReject.CorruptMap,
        _ => RunStepReject.CombatUnsupported,
    };
}
