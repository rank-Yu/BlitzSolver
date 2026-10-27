using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// One option of a decision request. The option list is the complete set the native screen offers;
/// a run action selects an option by <see cref="Index"/> (map points also carry their coordinate).
/// <para>
/// The representation is deliberately one fixed struct for every future decision kind: a reward
/// carries its model in <see cref="Model"/> and amount in <see cref="Amount"/>, an event page or
/// rest site carries the native option index, and a shop entry carries the entry index. New
/// decisions add <see cref="RunDecisionOptionKind"/> values instead of new request types.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunDecisionOption
{
    public RunDecisionOptionKind Kind;
    public byte Index;
    public RunMapPointType PointType;
    public byte _padding;
    public RunCoord Coord;
    /// <summary>Card/relic/potion/event target when the option names a model.</summary>
    public RunRef Model;
    /// <summary>Gold, HP, price or another option-specific number.</summary>
    public int Amount;
}

/// <summary>What one option in a decision request represents.</summary>
public enum RunDecisionOptionKind : byte
{
    None = 0,
    MapPoint = 1,
    EventOption = 2,
    Reward = 3,
    Skip = 4,
    Card = 5,
    Potion = 6,
    Relic = 7,
    ShopEntry = 8,
    CardRemoval = 9,
    RestSiteOption = 10,
    BossRelic = 11,
}

[InlineArray(RunLimits.DecisionOptions)]
public struct RunDecisionOptionArray
{
    private RunDecisionOption _first;
}

/// <summary>
/// Complete decision request: the kind the native screen is waiting on and every legal option in
/// the order the decision layer should present them. <see cref="RunView.Request"/> carries the
/// current request; later decisions (rewards, event pages, shop, rest site, card selection, boss
/// relics) fill the same struct from state instead of extending this type.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunDecisionRequest
{
    public RunDecision Kind;
    public byte OptionCount;
    private ushort _padding;
    public RunDecisionOptionArray Options;

    /// <summary>
    /// Reads the request implied by the state. A map point is the request whenever the run can
    /// travel (no unfinished room in the stack) and the map has at least one legal point; the
    /// native <see cref="RunState.Decision"/> field is only the importer's hint from the current
    /// room and is not authoritative at a map screen. Other decisions are not implemented in
    /// R2b-2 and report their kind with no options.
    /// </summary>
    public static RunDecisionRequest Capture(in RunState state)
    {
        RunDecisionRequest request = default;
        if (state.Player.CurrentHp <= 0)
        {
            request.Kind = RunDecision.GameOver;
            return request;
        }
        if (!RunTravel.CanTravel(in state))
        {
            // The importer reports MapPoint for a MapRoom; with no legal point the run is between
            // acts (R2f) or finished, not waiting for a map choice.
            request.Kind = state.Decision == RunDecision.MapPoint
                ? RunDecision.None : state.Decision;
            return request;
        }
        request.Kind = RunDecision.MapPoint;
        Span<RunCoord> points = stackalloc RunCoord[RunLimits.DecisionOptions];
        int count = RunTravel.Collect(in state, points);
        for (int index = 0; index < count; index++)
        {
            RunDecisionOption option = default;
            option.Kind = RunDecisionOptionKind.MapPoint;
            option.Index = checked((byte)index);
            option.Coord = points[index];
            if (RunTravel.TryFindPoint(in state, points[index], out RunMapPoint point))
                option.PointType = point.Type;
            request.Options[index] = option;
        }
        request.OptionCount = checked((byte)count);
        return request;
    }
}

/// <summary>An action the run host asks the kernel to take.</summary>
public enum RunActionKind : byte
{
    SelectMapPoint = 1,
    ChooseEventOption = 2,
    TakeReward = 3,
    SkipReward = 4,
    ChooseCard = 5,
    ChoosePotion = 6,
    ChooseRelic = 7,
    BuyShopEntry = 8,
    RemoveCard = 9,
    RestSiteOption = 10,
    ChooseBossRelic = 11,
}

/// <summary>
/// One run action. <see cref="Kind"/> names the decision it answers, <see cref="Option"/> is the
/// index into the matching <see cref="RunDecisionRequest"/> and <see cref="Coord"/> is the target
/// of a map selection. Later decisions reuse <see cref="Option"/>; no hidden state is carried.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunAction
{
    public RunActionKind Kind;
    public byte Option;
    private ushort _padding;
    public RunCoord Coord;

    public static RunAction SelectMapPoint(RunCoord coord) => new()
    {
        Kind = RunActionKind.SelectMapPoint,
        Coord = coord,
    };
}

/// <summary>Result category of one <see cref="RunKernel.Step"/>.</summary>
public enum RunStepStatus : byte
{
    /// <summary>The action completed and the state is waiting for the next decision.</summary>
    Decision = 0,
    /// <summary>A combat started; <see cref="RunStepResult.Combat"/> carries the opening boundary.</summary>
    Combat = 1,
    /// <summary>The run is over.</summary>
    Ended = 2,
    /// <summary>
    /// The room was created and the history was appended, but generating its content is a later
    /// phase. The state is at the native "room built, not entered" boundary; the caller must not
    /// ask for another decision until that content exists.
    /// </summary>
    UnsupportedRoom = 3,
    /// <summary>The action was illegal or not supported; the state is unchanged.</summary>
    Rejected = 4,
}

/// <summary>Stable reason code of a rejected action.</summary>
public enum RunStepReject : byte
{
    None = 0,
    WrongActionKind = 1,
    NotMapDecision = 2,
    UnknownMapPoint = 3,
    NotTravelable = 4,
    FreeTravelHook = 5,
    UnknownMapPointHook = 6,
    EventHook = 7,
    EventAllowedCheck = 8,
    RoomStackActive = 9,
    CombatUnsupported = 10,
    CorruptMap = 11,
}

/// <summary>Stable reason code of an advancing but incomplete <see cref="RunStepStatus.UnsupportedRoom"/>.</summary>
public static class RunStepReason
{
    public const string ShopContent = "room-content-shop";
    public const string TreasureContent = "room-content-treasure";
    public const string RestSiteContent = "room-content-rest-site";
    public const string EventContent = "room-content-event";
    public const string AncientContent = "room-content-ancient";

    public const string WrongActionKind = "wrong-action-kind";
    public const string NotMapDecision = "not-map-decision";
    public const string UnknownMapPoint = "unknown-map-point";
    public const string NotTravelable = "not-travelable";
    public const string FreeTravelHook = "free-travel-hook";
    public const string UnknownPointHook = "unknown-point-hook";
    public const string EventHook = "event-hook";
    public const string EventAllowedCheck = "event-allowed-check";
    public const string RoomStackActive = "room-stack-active";
    public const string CombatUnsupported = "combat-unsupported";
    public const string CorruptMap = "corrupt-map";
}

/// <summary>Result of one run step. The state itself is updated through <c>ref</c> on success.</summary>
public sealed class RunStepResult
{
    public required RunStepStatus Status { get; init; }

    public RunStepReject Reject { get; init; }

    /// <summary>Stable reason code; set for <see cref="RunStepStatus.UnsupportedRoom"/> and non-empty rejects.</summary>
    public string? Reason { get; init; }

    public string? Detail { get; init; }

    /// <summary>Current or next request. For a reject this is the request the caller failed to satisfy.</summary>
    public RunDecisionRequest Request { get; init; }

    public RunRoomType RoomType { get; init; }

    public RunMapPointType PointType { get; init; }

    public CombatSetupResult? Combat { get; init; }
}
