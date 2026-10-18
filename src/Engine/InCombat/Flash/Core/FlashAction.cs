using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CombatSolver.Engine.InCombat.Flash;

public enum FlashActionKind : byte
{
    PlayCard = 0,
    UsePotion = 1,
    EndTurn = 2,
}

[InlineArray(FlashLimits.MaxChoices)]
public struct FlashChoiceSelections
{
    private ulong _first;
}

/// <summary>
/// One player action plus every choice its execution needs, as bit sets over the option list each
/// choice request produces. Cards are referenced by hand position, never by identity.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FlashAction
{
    public FlashActionKind Kind;
    public byte HandIndex;

    /// <summary>Target owner, or <see cref="FlashLimits.NoOwner"/>.</summary>
    public byte Target;

    public byte PotionSlot;
    public byte ChoiceCount;
    private byte _padding0;
    private short _padding1;
    public FlashChoiceSelections Choices;

    public static FlashAction PlayCard(int handIndex, byte target)
        => new() { Kind = FlashActionKind.PlayCard, HandIndex = checked((byte)handIndex), Target = target, PotionSlot = byte.MaxValue };

    public static FlashAction UsePotion(int slot, byte target)
        => new() { Kind = FlashActionKind.UsePotion, HandIndex = byte.MaxValue, Target = target, PotionSlot = checked((byte)slot) };

    public static FlashAction EndTurn()
        => new() { Kind = FlashActionKind.EndTurn, HandIndex = byte.MaxValue, Target = FlashLimits.NoOwner, PotionSlot = byte.MaxValue };

    public readonly FlashAction WithChoice(ulong selection)
    {
        if (ChoiceCount >= FlashLimits.MaxChoices)
            throw new FlashCapacityException("Action.Choices", FlashLimits.MaxChoices);
        FlashAction next = this;
        next.Choices[next.ChoiceCount] = selection;
        next.ChoiceCount++;
        return next;
    }

    public override readonly string ToString() => Kind switch
    {
        FlashActionKind.PlayCard => $"Play(hand={HandIndex}, target={Target}, choices={ChoiceCount})",
        FlashActionKind.UsePotion => $"Potion(slot={PotionSlot}, target={Target}, choices={ChoiceCount})",
        _ => $"EndTurn(choices={ChoiceCount})",
    };
}

public enum FlashStepResult : byte
{
    /// <summary>The action ran to a stable boundary; the battle may now be over.</summary>
    Completed = 0,

    /// <summary>
    /// The action needs a choice it did not carry. The battle is left in an unspecified partial
    /// state and must be discarded; re-run the action from the parent with the choice appended.
    /// </summary>
    NeedsChoice = 1,
}

public enum FlashChoiceKind : byte
{
    None = 0,
    SelectCards = 1,
}

/// <summary>What the selected cards undergo; mirrors the supported <c>PlanChoiceEffect</c> values.</summary>
public enum FlashChoiceEffect : byte
{
    MoveToHand,
    MoveToDrawTop,
    Discard,
    Exhaust,
    Upgrade,

    /// <summary>Pick one generated option and add a free-this-turn clone to hand (Discovery, Splash).</summary>
    GenerateToHand,

    /// <summary>Replace the selected cards with random cards from the transform pool (Entropy).</summary>
    Transform,
}

/// <summary>Phase the choice belongs to; mirrors <c>PlanChoiceTiming</c>.</summary>
public enum FlashChoiceTiming : byte
{
    Action,
    PlayerTurnEnd,
    EnemyTurn,
    PlayerTurnStart,
}

[InlineArray(FlashLimits.MaxChoiceOptions)]
public struct FlashChoiceOptions
{
    private byte _first;
}

[InlineArray(FlashLimits.MaxGeneratedChoiceOptions)]
public struct FlashGeneratedChoiceOptions
{
    private FlashCard _first;
}

/// <summary>
/// Option list of a choice the kernel could not resolve from the action. Options are absolute
/// positions in <see cref="FlashBattle.Cards"/> of the (partial) battle at the time of the request,
/// in the existing engine's option order; selections are bit sets over this list. Generated-card
/// choices (the existing engine's <c>PlanChoiceEffect.GenerateToHand</c>) have no pile, so their
/// options are carried by value in <see cref="GeneratedOptions"/> instead.
/// </summary>
public struct FlashChoiceRequest
{
    public FlashChoiceKind Kind;
    public FlashChoiceEffect Effect;
    public FlashChoiceTiming Timing;
    public FlashPile Source;

    /// <summary>True for a generated-card choice, whose existing spec has <c>PileType.None</c>.</summary>
    public bool SourceIsNone;

    /// <summary>True when <see cref="GeneratedOptions"/> holds the options instead of pile positions.</summary>
    public bool OptionsAreGenerated;

    public byte OptionCount;

    /// <summary>Selection size bounds, already clamped to the option count as the existing engine does.</summary>
    public byte MinCount;
    public byte MaxCount;
    public FlashChoiceOptions Options;
    public FlashGeneratedChoiceOptions GeneratedOptions;
}

/// <summary>
/// One queued power amount change (<c>SimulatedPowerAmountChange</c>): the changed power id, the
/// applied delta and the applier. Only transient between <see cref="FlashKernel.ApplyPower"/> and the
/// next <see cref="FlashKernel.ResolvePowerAmountChanges"/>; a stable boundary has none queued.
/// </summary>
public struct FlashPowerAmountChange
{
    public ushort PowerId;
    public byte Applier;
    public int Delta;
}

[InlineArray(FlashLimits.PendingPowerAmountChanges)]
public struct FlashPowerAmountChanges
{
    private FlashPowerAmountChange _first;
}

/// <summary>
/// Stack-only execution context of one kernel step. Holds the battle by reference and the
/// transient facts of the current action; nothing here survives the step.
/// </summary>
public ref struct FlashExec
{
    public ref FlashBattle B;
    public readonly FlashRules R;
    private readonly FlashChoiceSelections _choices;
    private readonly int _choiceCount;
    private int _choicesTaken;

    public FlashExec(ref FlashBattle battle, FlashRules rules, in FlashAction action)
    {
        B = ref battle;
        R = rules;
        _choices = action.Choices;
        _choiceCount = action.ChoiceCount;
        _choicesTaken = 0;
        NeedsChoice = false;
        Request = default;
        Card = default;
        Stats = null;
        Target = FlashLimits.NoOwner;
        XValue = 0;
        PlayIndex = 0;
        PlayPosition = -1;
        EffectDepth = 0;
        ChoiceTiming = FlashChoiceTiming.Action;
        DrawnCardPosition = -1;
        IntrinsicDrawHandled = false;
        GeneratedOptions = default;
        GeneratedOptionCount = 0;
        PendingPowerChanges = default;
        PendingPowerChangeCount = 0;
    }

    /// <summary>Set when an effect asked for a choice the action did not carry; callers unwind immediately.</summary>
    public bool NeedsChoice;

    public FlashChoiceRequest Request;

    /// <summary>Value of the card currently being played (copy taken when it entered the play pile).</summary>
    public FlashCard Card;

    public FlashCardStats? Stats;

    /// <summary>Target owner of the current card or potion, or <see cref="FlashLimits.NoOwner"/>.</summary>
    public byte Target;

    public int XValue;

    public int PlayIndex;

    /// <summary>Relative play-pile position of the card whose tail/attachment hook is running (-1 outside a play).</summary>
    public int PlayPosition;

    /// <summary>Nesting depth of card/potion effects; <c>AfterHandEmptied</c> fires only at depth zero.</summary>
    public int EffectDepth;

    /// <summary>Timing stamped on choice requests (<c>SimulatedCombatState.SetActionChoiceTiming</c>).</summary>
    public FlashChoiceTiming ChoiceTiming;

    /// <summary>Absolute position of the card whose <c>AfterCardDrawn</c> is being dispatched, or -1.</summary>
    public int DrawnCardPosition;

    /// <summary><c>AfterCardDrawnMirrorContext.IntrinsicCardHandled</c> for the current drawn card.</summary>
    public bool IntrinsicDrawHandled;

    /// <summary>
    /// Generated-card options produced by the current card's OnPlay, consumed by its OwnChoice
    /// (Discovery/Splash). Transient: a needs-choice unwind discards the whole partial battle and
    /// re-execution regenerates the same options from the same parent RNG state.
    /// </summary>
    public FlashGeneratedChoiceOptions GeneratedOptions;

    public int GeneratedOptionCount;

    /// <summary>Power amount changes recorded since the last resolution (empty at a stable boundary).</summary>
    public FlashPowerAmountChanges PendingPowerChanges;

    public int PendingPowerChangeCount;

    /// <summary>Queues one applied power amount change for <see cref="FlashKernel.ResolvePowerAmountChanges"/>.</summary>
    public void RecordPowerAmountChange(ushort powerId, int delta, byte applier)
    {
        if (delta == 0)
            return;
        if (PendingPowerChangeCount >= FlashLimits.PendingPowerAmountChanges)
            throw new FlashCapacityException("PendingPowerAmountChanges", FlashLimits.PendingPowerAmountChanges);
        PendingPowerChanges[PendingPowerChangeCount++] = new FlashPowerAmountChange
        {
            PowerId = powerId,
            Applier = applier,
            Delta = delta,
        };
    }

    /// <summary>Removes the first <paramref name="count"/> resolved changes, keeping any appended during them.</summary>
    public void ConsumePowerAmountChanges(int count)
    {
        int kept = PendingPowerChangeCount - count;
        for (int index = 0; index < kept; index++)
            PendingPowerChanges[index] = PendingPowerChanges[count + index];
        for (int index = kept; index < PendingPowerChangeCount; index++)
            PendingPowerChanges[index] = default;
        PendingPowerChangeCount = kept;
    }

    /// <summary>
    /// Takes the next carried choice. When none is left, records <paramref name="request"/>, sets
    /// <see cref="NeedsChoice"/> and returns false; the caller must return without further effects.
    /// </summary>
    public bool TryTakeChoice(in FlashChoiceRequest request, out ulong selection)
    {
        if (_choicesTaken < _choiceCount)
        {
            selection = _choices[_choicesTaken++];
            ulong valid = request.OptionCount >= 64 ? ulong.MaxValue : (1UL << request.OptionCount) - 1;
            int count = System.Numerics.BitOperations.PopCount(selection);
            if ((selection & ~valid) != 0 || count < request.MinCount || count > request.MaxCount)
                throw new InvalidOperationException($"选择 {selection:X} 不符合选项规格（{request.OptionCount} 项，{request.MinCount}-{request.MaxCount} 个）。");
            return true;
        }
        selection = 0;
        NeedsChoice = true;
        Request = request;
        return false;
    }

    public readonly bool AllChoicesConsumed => _choicesTaken == _choiceCount;
}
