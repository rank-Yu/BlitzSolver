namespace CombatSolver.Engine.InCombat.Flash.Content.Relics;

// Class 2: the effect is settled at combat start, before the search root (the root is captured in the
// player's play phase). None of these has a branch that can still run on a later turn. The existing
// engine location is named per definition; each one runs once while the root is being set up, and
// Flash imports the resulting state (powers, HP, block, hand, energy) instead of replaying it.

/// <summary>
/// <c>Akabeko</c>: <c>AfterSideTurnStart</c> applies Vigor only when the player turn number is &lt;= 1
/// (<c>SimulatedCombatState.RelicTurnStart.TriggerRelicsAfterSideTurnStart</c>). The player-side turn 1
/// start precedes the root; the relic owner never participates in the enemy side's relic batch
/// (<c>RelicsParticipatingInSideTurn</c> requires the owner creature in the participants), so no later
/// side can trigger it.
/// </summary>
[FlashContent("AKABEKO")]
public sealed class Akabeko : FlashRelicDef;

/// <summary>
/// <c>Anchor</c>: <c>BeforeCombatStart</c> gains 10 unpowered block. That is before the combat root and
/// no hook remains afterwards.
/// </summary>
[FlashContent("ANCHOR")]
public sealed class Anchor : FlashRelicDef;

/// <summary>
/// <c>BagOfMarbles</c>: <c>BeforeSideTurnStart</c> applies Vulnerable only when the player turn number is
/// &lt;= 1 (<c>PrepareRelicBeforeSideTurnStart</c>). Only the player side participates, and the player turn
/// 1 start is before the root; from turn 2 the condition is false.
/// </summary>
[FlashContent("BAG_OF_MARBLES")]
public sealed class BagOfMarbles : FlashRelicDef;

/// <summary>
/// <c>BagOfPreparation</c>: <c>ModifyHandDraw</c> adds 2 only when the turn number is &lt;= 1
/// (<c>PersistentPowerSupport.IsBranchOwnedHandDrawRelic</c> and <c>GetTurnBasedHandDrawContribution</c>).
/// The turn 1 hand draw happens before the root; on turn 2 the condition is false.
/// </summary>
[FlashContent("BAG_OF_PREPARATION")]
public sealed class BagOfPreparation : FlashRelicDef;

/// <summary>
/// <c>Bellows</c>: <c>AfterPlayerTurnStart</c> upgrades the hand only when the turn number is &lt;= 1
/// (<c>ContinueRelicsAfterPlayerTurnStart</c>). The player turn 1 start is before the root and turn 2 no
/// longer satisfies the condition.
/// </summary>
[FlashContent("BELLOWS")]
public sealed class Bellows : FlashRelicDef;

/// <summary>
/// <c>BloodVial</c>: <c>AfterPlayerTurnStartLate</c> heals 2 on turn &lt;= 1; the existing engine runs the
/// same heal from <c>ContinueRelicsAfterPlayerTurnStart</c> on turn &lt;= 1. That is before the root and
/// no later turn satisfies it.
/// </summary>
[FlashContent("BLOOD_VIAL")]
public sealed class BloodVial : FlashRelicDef;

/// <summary>
/// <c>BronzeScales</c>: <c>AfterRoomEntered</c> applies Thorns when entering a combat room, i.e. during
/// combat setup. The root already carries the power, and the relic has no later hook.
/// </summary>
[FlashContent("BRONZE_SCALES")]
public sealed class BronzeScales : FlashRelicDef;

/// <summary>
/// <c>FestivePopper</c>: <c>AfterPlayerTurnStart</c> damages all opponents only when the turn number is
/// &lt;= 1 (<c>ContinueRelicsAfterPlayerTurnStart</c>). The turn 1 start is before the root.
/// </summary>
[FlashContent("FESTIVE_POPPER")]
public sealed class FestivePopper : FlashRelicDef;

/// <summary>
/// <c>GamblingChip</c>: <c>AfterPlayerTurnStart</c> offers its discard/draw choice only on turn 1
/// (<c>TurnStartChoiceSupport.ResolveDiscardAndDraw</c> from <c>ContinueRelicsAfterPlayerTurnStart</c>).
/// The choice belongs to the player turn start, before the play-phase root.
/// </summary>
[FlashContent("GAMBLING_CHIP")]
public sealed class GamblingChip : FlashRelicDef;

/// <summary>
/// <c>Gorget</c>: <c>AfterRoomEntered</c> applies Plating when entering a combat room, during combat
/// setup, and has no later hook.
/// </summary>
[FlashContent("GORGET")]
public sealed class Gorget : FlashRelicDef;

/// <summary>
/// <c>Lantern</c>: <c>AfterSideTurnStart</c> gains 1 energy only when the player turn number is &lt;= 1
/// (<c>TriggerRelicsAfterSideTurnStart</c>). The player turn 1 start is before the root.
/// </summary>
[FlashContent("LANTERN")]
public sealed class Lantern : FlashRelicDef;

/// <summary>
/// <c>OddlySmoothStone</c>: <c>AfterRoomEntered</c> applies Dexterity when entering a combat room, during
/// combat setup, and has no later hook.
/// </summary>
[FlashContent("ODDLY_SMOOTH_STONE")]
public sealed class OddlySmoothStone : FlashRelicDef;

/// <summary>
/// <c>Pantograph</c>: <c>BeforeCombatStart</c> heals 25 only in a boss room, during combat setup; no later
/// hook reads it.
/// </summary>
[FlashContent("PANTOGRAPH")]
public sealed class Pantograph : FlashRelicDef;

/// <summary>
/// <c>PetrifiedToad</c>: <c>BeforeCombatStartLate</c> procures a PotionShapedRock before the combat root;
/// the potion is part of the imported starting state and there is no later hook.
/// </summary>
[FlashContent("PETRIFIED_TOAD")]
public sealed class PetrifiedToad : FlashRelicDef;

/// <summary>
/// <c>RedMask</c>: <c>BeforeSideTurnStart</c> applies Weak only when the player turn number is &lt;= 1
/// (<c>PrepareRelicBeforeSideTurnStart</c>); only the player side participates and the turn 1 start is
/// before the root.
/// </summary>
[FlashContent("RED_MASK")]
public sealed class RedMask : FlashRelicDef;

/// <summary>
/// <c>SlingOfCourage</c>: <c>AfterRoomEntered</c> applies Strength only for an elite room, during combat
/// setup, and has no later hook.
/// </summary>
[FlashContent("SLING_OF_COURAGE")]
public sealed class SlingOfCourage : FlashRelicDef;

/// <summary>
/// <c>StoneCracker</c>: <c>AfterRoomEntered</c> upgrades two upgradable draw-pile cards when entering a
/// combat room, before the root; the upgraded cards are imported.
/// </summary>
[FlashContent("STONE_CRACKER")]
public sealed class StoneCracker : FlashRelicDef;

/// <summary>
/// <c>Toolbox</c>: <c>BeforeHandDraw</c> offers its colourless card choice only on turn 1
/// (<c>SimulatedCombatState.Relics.ContinueRelicsBeforeHandDraw</c>), i.e. at the player turn start
/// before the play-phase root.
/// </summary>
[FlashContent("TOOLBOX")]
public sealed class Toolbox : FlashRelicDef
{
    public override bool MayGenerateIdentityContent => true;
}

/// <summary>
/// <c>Vajra</c>: <c>AfterRoomEntered</c> applies Strength when entering a combat room, during combat
/// setup, and has no later hook.
/// </summary>
[FlashContent("VAJRA")]
public sealed class Vajra : FlashRelicDef;

/// <summary>
/// <c>VeryHotCocoa</c>: <c>AfterSideTurnStart</c> gains 4 energy only when the player turn number is
/// &lt;= 1 (<c>TriggerRelicsAfterSideTurnStart</c>). The player turn 1 start is before the root.
/// </summary>
[FlashContent("VERY_HOT_COCOA")]
public sealed class VeryHotCocoa : FlashRelicDef;

/// <summary>
/// <c>VexingPuzzlebox</c>: <c>AfterPlayerTurnStart</c> generates one free card only when the turn number is
/// 1 (<c>ContinueRelicsAfterPlayerTurnStart</c>), at the player turn start before the play-phase root.
/// </summary>
[FlashContent("VEXING_PUZZLEBOX")]
public sealed class VexingPuzzlebox : FlashRelicDef;
