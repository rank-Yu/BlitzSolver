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
    public RunRewardType RewardType;
    public byte Upgrade;
    public byte HasEnchantment;
    public ushort EnchantmentId;
    public short EnchantmentAmount;
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
    Reroll = 12,
}

[InlineArray(RunLimits.DecisionOptions)]
public struct RunDecisionOptionArray
{
    private RunDecisionOption _first;
}

/// <summary>
/// Complete decision request: the kind the native screen is waiting on and every legal option in
/// the order the decision layer should present them. <see cref="RunView.Request"/> carries the
/// current request; the R3b <c>RunDecisionImporter</c> fills it from the native screen instead of
/// extending this type.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunDecisionRequest
{
    public RunDecision Kind;
    public byte OptionCount;
    private ushort _padding;
    public RunDecisionOptionArray Options;
}

/// <summary>An action the run host asks the run executor to take.</summary>
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
    LeaveShop = 14,
    CancelCardSelection = 15,
    RestSiteOption = 10,
    ChooseBossRelic = 11,
    LeaveRewards = 12,
    RerollCardReward = 13,
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


/// <summary>Stable reason codes for content the map-entry boundary cannot represent.</summary>
public static class RunStepReason
{
    public const string UnknownPointHook = "unknown-point-hook";
    public const string EventHook = "event-hook";
    public const string EventAllowedCheck = "event-allowed-check";
    public const string RoomStackActive = "room-stack-active";
}
