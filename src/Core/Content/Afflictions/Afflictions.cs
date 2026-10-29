using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Afflictions;

/// <summary>
/// <c>Tainted</c>: Skill cards only, stackable. Vital Spark applies it (once at combat start without a
/// null check, once when a Skill enters combat with a null check), and TaintedPower adds the amount
/// to the player's powered attacks.
/// </summary>
[ContentId("TAINTED")]
public sealed class TaintedAffliction : AfflictionDef
{
    public override bool IsStackable => true;

    public override bool CanAfflictCardType(CardType type) => type == CardType.Skill;
}

/// <summary><c>Entangled</c>: no card-type restriction, not stackable; TangledPower charges +1 energy.</summary>
[ContentId("ENTANGLED")]
public sealed class EntangledAffliction : AfflictionDef;

/// <summary><c>Ringing</c>: no card-type restriction, not stackable; RingingPower forbids the second card play.</summary>
[ContentId("RINGING")]
public sealed class RingingAffliction : AfflictionDef;

/// <summary><c>Smog</c>: no card-type restriction, not stackable; SmoggyPower makes it unplayable.</summary>
[ContentId("SMOG")]
public sealed class SmogAffliction : AfflictionDef;

/// <summary>
/// <c>Hexed</c>: no card-type restriction, not stackable. Hexed.AfterCardEnteredCombat clears itself
/// when the owner has no HexPower; the normalization pass does the same, and the applier's death
/// removes the power.
/// </summary>
[ContentId("HEXED")]
public sealed class HexedAffliction : AfflictionDef;

/// <summary>
/// <c>Galvanized</c>: no card-type restriction, stackable. GalvanicPower afflicts newly entered
/// Power cards (<c>NormalizeCardAfflictions</c>) and damages the card's owner when one is played.
/// </summary>
[ContentId("GALVANIZED")]
public sealed class GalvanizedAffliction : AfflictionDef
{
    public override bool IsStackable => true;
}

/// <summary>
/// <c>Bound</c>: no card-type restriction, not stackable. ChainsOfBindingPower applies and clears
/// it and uses it for its play restriction.
/// </summary>
[ContentId("BOUND")]
public sealed class BoundAffliction : AfflictionDef;
