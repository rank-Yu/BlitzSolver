using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Relics;

/// <summary>
/// <c>InfusedCore</c>: channels 3 Lightning at the first side turn start (before the root) and adds
/// its ExtraDamage to every Lightning orb value (<c>SimulatedCombatState.RelicTurnStart</c> case
/// InfusedCore; native <c>ModifyOrbValue</c> over the combat listeners).
/// </summary>
[BlitzContent("INFUSED_CORE")]
public sealed class InfusedCore : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Lightning", "ExtraDamage"];

    public override decimal ModifyOrbValue(in BlitzBattle battle, BlitzRules rules, BlitzListener self,
        in BlitzOrb orb, decimal value)
    {
        if (!BlitzRegistry.TryOrb("LIGHTNING_ORB", out ushort lightning) || orb.Id != lightning)
            return value;
        return value + rules.RelicVar(Id, 1);
    }
}

/// <summary>
/// <c>PhylacteryUnbound</c>: summons Osty for its StartOfTurn amount at every player side turn start;
/// the StartOfCombat summon is already in the captured root (<c>SimulatedCombatState.RelicTurnStart</c>).
/// </summary>
[BlitzContent("PHYLACTERY_UNBOUND")]
public sealed class PhylacteryUnbound : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["StartOfCombat", "StartOfTurn"];

    public override void AfterSideTurnStart(ref BlitzExec x, BlitzListener self, BlitzSide side)
    {
        if (side != BlitzSide.Player)
            return;
        BlitzKernel.SummonOsty(ref x, (int)x.R.RelicVar(Id, 1));
    }
}

/// <summary>
/// <c>DivineDestiny</c>: gains 7 stars at the first side turn start
/// (<c>AfterSideTurnStart</c> with <c>TurnNumber &lt;= 1</c>), before the captured root.
/// </summary>
[BlitzContent("DIVINE_DESTINY")]
public sealed class DivineDestiny : BlitzRelicDef
{
}

[BlitzContent("BLACK_BLOOD")] public sealed class BlackBlood : BlitzRelicDef; // AfterCombatVictory

/// <summary>
/// <c>RingOfTheDrake</c>: <c>ModifyHandDraw</c> adds Cards while the player turn number is at most
/// Turns (<c>PersistentPowerSupport</c> native relic contribution).
/// </summary>
[BlitzContent("RING_OF_THE_DRAKE")]
public sealed class RingOfTheDrake : BlitzRelicDef
{
    // RingOfTheDrake.cs: CardsVar(2), DynamicVar("Turns", 3m).
    public override IReadOnlyList<string> VarNames => ["Cards", "Turns"];

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
        => x.B.PlayerTurn > (int)x.R.RelicVar(Id, 1) ? amount : amount + x.R.RelicVar(Id, 0);
}
