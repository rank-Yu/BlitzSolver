using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Simulation.Run;

/// <summary>Opaque host session plus monotonically increasing revision; neither is a content hash.
/// The host owns uniqueness, invalidation and consumption. Pure validation stores no replay history.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct RunDecisionToken(ulong SessionId, ulong Revision);

/// <summary>Request-local pool slice; empty slices have a zero start.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct RunDecisionSlice(ushort Start, ushort Count);

public enum RunDecisionKnowledge : byte
{
    Unknown = 0,
    Partial = 1,
    Complete = 2,
}

[Flags]
public enum RunDecisionOptionFlags : byte
{
    None = 0,
    Disabled = 1,
    Locked = 2,
}

public enum RunDecisionPayloadKind : byte
{
    None = 0,
    Map = 1,
    Card = 2,
    Model = 3,
    Reward = 4,
    CardBundle = 5,
}

public enum RunSelectionPurpose : byte
{
    None = 0,
    Remove = 1,
    Upgrade = 2,
    Transform = 3,
    Enchant = 4,
}

/// <summary>Visible facts only, not commands or outcome predictions. Unknown mechanics remain unknown.</summary>
public enum RunDecisionFactKind : byte
{
    None = 0,
    GoldLoss = 1,
    HpLoss = 2,
    GoldGain = 3,
    RemoveCards = 4,
    AddCard = 5,
}

[StructLayout(LayoutKind.Sequential)]
public struct RunDecisionFact
{
    public int Amount;
    /// <summary>Only AddCard carries a card; Amount is its count.</summary>
    public RunView.CardView Card;
    public RunDecisionFactKind Kind;
}

/// <summary>One visible row. OptionId is nonzero and unique in this request; order, model and
/// native text key are not identity. Only fields selected by PayloadKind may be populated.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunDecisionOption
{
    public uint OptionId;
    public RunView.CardView Card;
    public RunRef Model;
    public RunDecisionSlice Costs;
    public RunDecisionSlice Effects;
    public RunDecisionSlice Bundle;
    /// <summary>Exact native machine key in the request UTF-8 pool, never an execution identity.</summary>
    public RunDecisionSlice NativeKey;
    public RunCoord Coord;
    public RunDecisionOptionKind Kind;
    public RunDecisionPayloadKind PayloadKind;
    public RunDecisionOptionFlags Flags;
    public RunDecisionKnowledge CostsKnowledge;
    public RunDecisionKnowledge EffectsKnowledge;
    public RunMapPointType PointType;
    public RunRewardType RewardType;
}

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
    Leave = 13,
    CardBundle = 14,
    Open = 15,
}

[InlineArray(RunLimits.DecisionOptions)]
public struct RunDecisionOptionArray { private RunDecisionOption _first; }

[InlineArray(RunDecisionLimits.Facts)]
public struct RunDecisionFactArray { private RunDecisionFact _first; }

[InlineArray(RunDecisionLimits.Cards)]
public struct RunDecisionCardArray { private RunView.CardView _first; }

[InlineArray(RunDecisionLimits.KeyBytes)]
public struct RunDecisionKeyArray { private byte _first; }

/// <summary>Engineering budgets, not claims of native upper bounds. Overflow is explicit.</summary>
public static class RunDecisionLimits
{
    public const int Facts = 256;
    public const int Cards = 256;
    public const int KeyBytes = 4096;
}

/// <summary>A pending native choice with bounded shared visible payloads. None/GameOver are not
/// requests. RunView.From preserves a default request until a host attaches a real pending choice.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunDecisionRequest
{
    public RunDecisionToken Token;
    /// <summary>Visible origin (None, Event or Relic), never an execution identity.</summary>
    public RunRef Source;
    public RunDecisionSlice PageKey;
    public ushort OptionCount;
    public ushort MinSelect;
    public ushort MaxSelect;
    public ushort FactCount;
    public ushort CardCount;
    public ushort KeyByteCount;
    public RunDecision Kind;
    public RunSelectionPurpose SelectionPurpose;
    /// <summary>Knowledge of the displayed modification preview. Unknown never promises a resulting card.</summary>
    public RunDecisionKnowledge SelectionPreviewKnowledge;
    public byte Cancelable;
    public byte RequireConfirmation;
    public RunDecisionOptionArray Options;
    public RunDecisionFactArray Facts;
    public RunDecisionCardArray Cards;
    public RunDecisionKeyArray Keys;
}

public enum RunActionKind : byte { Select = 1, Cancel = 2 }

[InlineArray(RunLimits.DecisionOptions)]
public struct RunOptionIdArray { private uint _first; }

/// <summary>One atomic answer, preserving order. Empty Select and Cancel are distinct.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunAction
{
    public RunDecisionToken Token;
    public RunOptionIdArray OptionIds;
    public ushort Count;
    public RunActionKind Kind;
}

/// <summary>Allocation-free bounded construction helpers; validate the final request before use.</summary>
public static class RunDecisionBuilder
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void AddOption(ref RunDecisionRequest request, in RunDecisionOption option)
    {
        CheckCount(request.OptionCount, RunLimits.DecisionOptions, "Decision.Options");
        if (request.OptionCount == RunLimits.DecisionOptions)
            throw new CapacityException("Decision.Options", RunLimits.DecisionOptions);
        request.Options[request.OptionCount++] = option;
    }

    public static RunDecisionSlice AddFacts(ref RunDecisionRequest request,
        ReadOnlySpan<RunDecisionFact> facts)
    {
        CheckCount(request.FactCount, RunDecisionLimits.Facts, "Decision.Facts");
        if (facts.Length > RunDecisionLimits.Facts - request.FactCount)
            throw new CapacityException("Decision.Facts", RunDecisionLimits.Facts);
        if (facts.Length == 0)
            return default;
        RunDecisionSlice slice = new(request.FactCount, checked((ushort)facts.Length));
        Span<RunDecisionFact> target = request.Facts;
        facts.CopyTo(target.Slice(slice.Start, slice.Count));
        request.FactCount += slice.Count;
        return slice;
    }

    public static RunDecisionSlice AddCards(ref RunDecisionRequest request,
        ReadOnlySpan<RunView.CardView> cards)
    {
        CheckCount(request.CardCount, RunDecisionLimits.Cards, "Decision.Cards");
        if (cards.Length > RunDecisionLimits.Cards - request.CardCount)
            throw new CapacityException("Decision.Cards", RunDecisionLimits.Cards);
        if (cards.Length == 0)
            return default;
        RunDecisionSlice slice = new(request.CardCount, checked((ushort)cards.Length));
        Span<RunView.CardView> target = request.Cards;
        cards.CopyTo(target.Slice(slice.Start, slice.Count));
        request.CardCount += slice.Count;
        return slice;
    }

    public static RunDecisionSlice AddKey(ref RunDecisionRequest request, ReadOnlySpan<char> key)
    {
        CheckCount(request.KeyByteCount, RunDecisionLimits.KeyBytes, "Decision.Keys");
        int count;
        try { count = StrictUtf8.GetByteCount(key); }
        catch (EncoderFallbackException) { throw new InvalidDataException("key-utf8: invalid UTF-16 key"); }
        if (count > RunDecisionLimits.KeyBytes - request.KeyByteCount)
            throw new CapacityException("Decision.Keys", RunDecisionLimits.KeyBytes);
        if (count == 0)
            return default;
        RunDecisionSlice slice = new(request.KeyByteCount, checked((ushort)count));
        Span<byte> keys = request.Keys;
        StrictUtf8.GetBytes(key, keys.Slice(slice.Start, count));
        request.KeyByteCount += slice.Count;
        return slice;
    }

    internal static void CheckCount(int count, int capacity, string region)
    {
        if (count > capacity)
            throw new CapacityException(region, capacity);
    }
}

/// <summary>Stable reason codes for content the map-entry boundary cannot represent.</summary>
public static class RunStepReason
{
    public const string UnknownPointHook = "unknown-point-hook";
    public const string EventHook = "event-hook";
    public const string EventAllowedCheck = "event-allowed-check";
    public const string RoomStackActive = "room-stack-active";
}
