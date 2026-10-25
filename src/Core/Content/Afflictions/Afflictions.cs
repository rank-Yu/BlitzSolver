using BlitzSolver.Engine;

namespace BlitzSolver.Content.Afflictions;

/// <summary>
/// <c>Tainted</c>: Skill cards only, stackable. Vital Spark applies it (once at combat start without a
/// null check, once when a Skill enters combat with a null check), and TaintedPower adds the amount
/// to the player's powered attacks.
/// </summary>
[BlitzContent("TAINTED")]
public sealed class TaintedAffliction : BlitzAfflictionDef
{
    public override bool IsStackable => true;

    public override bool CanAfflictCardType(BlitzCardType type) => type == BlitzCardType.Skill;
}

/// <summary><c>Entangled</c>: no card-type restriction, not stackable; TangledPower charges +1 energy.</summary>
[BlitzContent("ENTANGLED")]
public sealed class EntangledAffliction : BlitzAfflictionDef;

/// <summary><c>Ringing</c>: no card-type restriction, not stackable; RingingPower forbids the second card play.</summary>
[BlitzContent("RINGING")]
public sealed class RingingAffliction : BlitzAfflictionDef;

/// <summary><c>Smog</c>: no card-type restriction, not stackable; SmoggyPower makes it unplayable.</summary>
[BlitzContent("SMOG")]
public sealed class SmogAffliction : BlitzAfflictionDef;

/// <summary>
/// <c>Hexed</c>: no card-type restriction, not stackable. Hexed.AfterCardEnteredCombat clears itself
/// when the owner has no HexPower; the normalization pass does the same, and the applier's death
/// removes the power.
/// </summary>
[BlitzContent("HEXED")]
public sealed class HexedAffliction : BlitzAfflictionDef;

/// <summary>
/// <c>Galvanized</c>: no card-type restriction, stackable. GalvanicPower afflicts newly entered
/// Power cards (<c>NormalizeCardAfflictions</c>) and damages the card's owner when one is played.
/// </summary>
[BlitzContent("GALVANIZED")]
public sealed class GalvanizedAffliction : BlitzAfflictionDef
{
    public override bool IsStackable => true;
}

/// <summary>
/// <c>Bound</c>: no card-type restriction, not stackable. ChainsOfBindingPower applies and clears
/// it and uses it for its play restriction.
/// </summary>
[BlitzContent("BOUND")]
public sealed class BoundAffliction : BlitzAfflictionDef;
