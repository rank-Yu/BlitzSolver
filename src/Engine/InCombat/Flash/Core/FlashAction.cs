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

    /// <summary>Pick one of a fixed option list (Knowledge Demon's curse cards).</summary>
    SelectOption = 2,
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

    /// <summary>Add copies of the selected card to hand (Dual Wield, Heirloom Hammer).</summary>
    Duplicate,

    /// <summary>Auto-play the selected card a number of times (Decisions, Decisions).</summary>
    AutoPlayRepeated,

    /// <summary>Give the selected cards single-turn Sly (<c>CardCmd.ApplySingleTurnSly</c>, Hand Trick).</summary>
    ApplySly,

    /// <summary>Add the local Retain keyword to the selected cards (<c>CardCmd.ApplyKeyword</c>, Snap).</summary>
    ApplyRetain,

    /// <summary>Add the local Ethereal keyword to the selected cards (<c>CardCmd.ApplyKeyword</c>, SculptingStrike).</summary>
    ApplyEthereal,

    /// <summary>Modify the selected cards in place (Transfigure's cost and replay count).</summary>
    Modify,

    /// <summary>
    /// Knowledge Demon's CURSE_OF_KNOWLEDGE: the selected card option applies its curse power to the
    /// player and advances the demon's <c>_curseOfKnowledgeCounter</c>.
    /// </summary>
    ApplyKnowledgeCurse,
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
    public byte Target;
    public int Delta;
}

[InlineArray(FlashLimits.PendingPowerAmountChanges)]
public struct FlashPowerAmountChanges
{
    private FlashPowerAmountChange _first;
}

/// <summary>Which <c>AfterCardPlayed</c> listener snapshotted the amount at <c>BeforeCardPlayed</c>.</summary>
public enum FlashCardPlayPairKind : byte
{
    /// <summary><c>AfterimagePower</c>: the power amount becomes Unpowered block after the play.</summary>
    AfterimageBlock = 0,

    /// <summary><c>StranglePower</c>: the power amount is dealt to its owner after the play.</summary>
    StrangleDamage = 1,

    /// <summary><c>StormPower</c>: the power amount becomes Lightning channels after a Power play.</summary>
    StormLightning = 2,

    /// <summary><c>SubroutinePower</c>: the power amount becomes energy after a Power play.</summary>
    SubroutineEnergy = 3,

    /// <summary><c>OblivionPower</c>: the power amount at play start becomes Doom on its owner after the play.</summary>
    OblivionDoom = 4,

    /// <summary><c>SerpentFormPower</c>: the power amount at play start becomes Unpowered damage after the play.</summary>
    SerpentFormDamage = 5,
}

/// <summary>
/// One <c>BeforeCardPlayed</c> amount snapshot, keyed by the play index it belongs to (the existing
/// engine keys <c>CardPlayPairPredictionState</c> by the <c>CardPlay</c> object).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FlashCardPlayPair
{
    public ushort PowerId;
    public byte Owner;
    public FlashCardPlayPairKind Kind;
    public int Amount;
}

[InlineArray(FlashLimits.CardPlayPairs)]
public struct FlashCardPlayPairs
{
    private FlashCardPlayPair _first;
}

/// <summary>
/// Stack-only execution context of one kernel step. Holds the battle by reference and the
/// transient facts of the current action; nothing here survives the step.
/// </summary>
public ref struct FlashExec
{
    public ref FlashBattle B;
    public readonly FlashRules R;

    /// <summary>Unblocked-player hits in the current attack context (PainfulStabs after the attack).</summary>
    public int AttackUnblockedHits;
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
        StarValue = 0;
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
        CardPlayPairs = default;
        CardPlayPairCount = 0;
        CurrentPlayIsAutoPlay = false;
        CurrentPlayEnergyValue = 0;
        CardDamageTotal = 0;
        CardKilledTarget = false;
        LastDamageTotal = 0;
        LastDamageOverkill = 0;
    }

    /// <summary>Set when an effect asked for a choice the action did not carry; callers unwind immediately.</summary>
    public bool NeedsChoice;

    /// <summary>
    /// Sum of <c>TotalDamage + OverkillDamage</c> over the damage results of the current card play
    /// (the <c>CombatPredictionDamageReceivedEntry</c> history the existing engine's Fisticuffs sums).
    /// Reset when a card starts; transient, never part of the state.
    /// </summary>
    public int CardDamageTotal;

    /// <summary>Whether the current card play killed its chosen target (HandOfGreed's WasFatalKill).</summary>
    public bool CardKilledTarget;

    /// <summary>
    /// Number of targets killed by the most recent <c>FlashKernel.Damage</c> call. EchoingSlash uses it
    /// to add one extra attack wave per kill; nested damage overwrites it, so read it immediately.
    /// </summary>
    public int LastDamageKills;

    /// <summary>
    /// <c>TotalDamage + OverkillDamage</c> of the most recent <c>Damage</c> call, captured before its
    /// result hooks run (Omnislice copies this amount to the other enemies).
    /// </summary>
    public int LastDamageTotal;

    public int LastDamageOverkill;

    public FlashChoiceRequest Request;

    /// <summary>Value of the card currently being played (copy taken when it entered the play pile).</summary>
    public FlashCard Card;

    public FlashCardStats? Stats;

    /// <summary>Target owner of the current card or potion, or <see cref="FlashLimits.NoOwner"/>.</summary>
    public byte Target;

    /// <summary>Energy X value captured by the last card play (<c>EnergyCost.CapturedXValue</c>).</summary>
    public int XValue;

    /// <summary>
    /// Star value captured by the last card play (<c>CardModel.LastStarsSpent</c>): the star cost paid,
    /// or the star cost computed for an auto-play. <c>ResolveStarXValue</c> reads it.
    /// </summary>
    public int StarValue;

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

    /// <summary>
    /// <c>BeforeCardPlayed</c> amount snapshots waiting for the matching <c>AfterCardPlayed</c>
    /// listener. A nested auto-play pushes above the outer play's entries and consumes its own first;
    /// empty outside a card play, so it never reaches a stable boundary.
    /// </summary>
    public FlashCardPlayPairs CardPlayPairs;

    public int CardPlayPairCount;

    /// <summary>
    /// The play whose <c>OnPlay</c>/<c>OwnChoice</c> is running was started by <c>AutoPlay</c>
    /// (<c>CardPlay.IsAutoPlay</c>). The existing engine's nested-choice sink applies a required
    /// empty choice without asking; a manual play asks it.
    /// </summary>
    public bool CurrentPlayIsAutoPlay;

    /// <summary>
    /// <c>CardPlay.Resources.EnergyValue</c> of the play whose hooks are running: the energy the card paid
    /// (<c>CardEnergyCost.GetAmountToSpend</c>), zero for an auto-play.
    /// </summary>
    public int CurrentPlayEnergyValue;

    /// <summary>Records one <c>BeforeCardPlayed</c> amount for its <c>AfterCardPlayed</c> consumer.</summary>
    public void PushCardPlayPair(FlashCardPlayPairKind kind, byte owner, ushort powerId, int amount)
    {
        if (CardPlayPairCount >= FlashLimits.CardPlayPairs)
            throw new FlashCapacityException("CardPlayPairs", FlashLimits.CardPlayPairs);
        CardPlayPairs[CardPlayPairCount++] = new FlashCardPlayPair
        {
            PowerId = powerId,
            Owner = owner,
            Kind = kind,
            Amount = amount,
        };
    }

    /// <summary>
    /// Takes the most recent matching snapshot. Entries are keyed by the play index (the nested play's
    /// entries sit above the outer play's), and there is at most one per (kind, owner, power).
    /// </summary>
    public bool TryTakeCardPlayPair(FlashCardPlayPairKind kind, byte owner, ushort powerId, out int amount)
    {
        for (int index = CardPlayPairCount - 1; index >= 0; index--)
        {
            FlashCardPlayPair pair = CardPlayPairs[index];
            if (pair.Kind != kind || pair.Owner != owner || pair.PowerId != powerId)
                continue;
            amount = pair.Amount;
            for (int move = index + 1; move < CardPlayPairCount; move++)
                CardPlayPairs[move - 1] = CardPlayPairs[move];
            CardPlayPairCount--;
            return true;
        }
        amount = 0;
        return false;
    }

    /// <summary>Queues one applied power amount change for <see cref="FlashKernel.ResolvePowerAmountChanges"/>.</summary>
    public void RecordPowerAmountChange(ushort powerId, int delta, byte applier, byte target)
    {
        if (delta == 0)
            return;
        if (PendingPowerChangeCount >= FlashLimits.PendingPowerAmountChanges)
            throw new FlashCapacityException("PendingPowerAmountChanges", FlashLimits.PendingPowerAmountChanges);
        PendingPowerChanges[PendingPowerChangeCount++] = new FlashPowerAmountChange
        {
            PowerId = powerId,
            Applier = applier,
            Target = target,
            Delta = delta,
        };
    }

    /// <summary>
    /// Empties the queue after its entries were copied out for dispatch. The existing engine's
    /// <c>DrainPowerAmountChanges</c> clears the list before handlers run, so a nested resolution
    /// (for example Vicious drawing a Strike that Hellraiser auto-plays) only sees changes appended
    /// during the handlers, never the batch currently being dispatched.
    /// </summary>
    public void ClearPowerAmountChanges() => PendingPowerChangeCount = 0;

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
