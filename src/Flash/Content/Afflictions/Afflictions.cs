using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Afflictions;

// Vanilla card afflictions. Each native affliction is a marker: the behavior lives in the power that
// applies it (TaintedPower, TangledPower, RingingPower, SmoggyPower, HexPower). The card type and
// stacking flags reproduce AfflictionModel.CanAfflict so CardCmd.Afflict accepts the same cards.

/// <summary>
/// <c>Tainted</c>: Skill cards only, stackable. Vital Spark applies it (once at combat start without a
/// null check, once when a Skill enters combat with a null check), and TaintedPower adds the amount
/// to the player's powered attacks.
/// </summary>
[FlashContent("TAINTED")]
public sealed class TaintedAffliction : FlashAfflictionDef
{
    public override bool IsStackable => true;

    public override bool CanAfflictCardType(FlashCardType type) => type == FlashCardType.Skill;
}

/// <summary><c>Entangled</c>: no card-type restriction, not stackable; TangledPower charges +1 energy.</summary>
[FlashContent("ENTANGLED")]
public sealed class EntangledAffliction : FlashAfflictionDef;

/// <summary><c>Ringing</c>: no card-type restriction, not stackable; RingingPower forbids the second card play.</summary>
[FlashContent("RINGING")]
public sealed class RingingAffliction : FlashAfflictionDef;

/// <summary><c>Smog</c>: no card-type restriction, not stackable; SmoggyPower makes it unplayable.</summary>
[FlashContent("SMOG")]
public sealed class SmogAffliction : FlashAfflictionDef;

/// <summary>
/// <c>Hexed</c>: no card-type restriction, not stackable. Hexed.AfterCardEnteredCombat clears itself
/// when the owner has no HexPower; the normalization pass does the same, and the applier's death
/// removes the power.
/// </summary>
[FlashContent("HEXED")]
public sealed class HexedAffliction : FlashAfflictionDef;

/// <summary>
/// <c>Galvanized</c>: no card-type restriction, stackable. GalvanicPower afflicts newly entered
/// Power cards (<c>NormalizeCardAfflictions</c>) and damages the card's owner when one is played.
/// </summary>
[FlashContent("GALVANIZED")]
public sealed class GalvanizedAffliction : FlashAfflictionDef
{
    public override bool IsStackable => true;
}

/// <summary>
/// <c>Bound</c>: no card-type restriction, not stackable. ChainsOfBindingPower applies and clears
/// it and uses it for its play restriction.
/// </summary>
[FlashContent("BOUND")]
public sealed class BoundAffliction : FlashAfflictionDef;
