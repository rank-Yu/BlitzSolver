using BlitzSolver.Engine;

namespace BlitzSolver.Content.Relics;

/// <summary>Native <c>BurningBlood</c>: heals after combat only, so it has no in-combat hooks.</summary>
[BlitzContent("BURNING_BLOOD")]
public sealed class BurningBlood : BlitzRelicDef;

/// <summary>
/// <c>RingOfTheSnake</c>: <c>ModifyHandDraw</c> adds Cards while <c>TurnNumber &lt;= 1</c>
/// (RingOfTheSnake.cs; the existing engine runs the same contribution from
/// <c>PersistentPowerSupport.GetTurnBasedHandDrawContribution</c>). The turn 1 hand draw happens before
/// the root, so later roots see nothing from it.
/// </summary>
[BlitzContent("RING_OF_THE_SNAKE")]
public sealed class RingOfTheSnake : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override decimal ModifyHandDraw(ref BlitzExec x, BlitzListener self, decimal amount)
        => x.B.PlayerTurn <= 1 ? amount + x.R.RelicVar(Id, 0) : amount;
}

/// <summary>
/// <c>BoundPhylactery</c>: the starter relic summons Osty for its Summon variable at combat start
/// (<c>BeforeCombatStart</c>, before the root) and again at every player turn start after turn 1
/// (<c>AfterEnergyResetLate</c>; the existing engine's relic batch calls it at the same point,
/// <c>TurnStartRelicSupport.TriggerAfterEnergyResetLate</c>).
/// </summary>
[BlitzContent("BOUND_PHYLACTERY")]
public sealed class BoundPhylactery : BlitzRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void AfterEnergyResetLate(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner || x.B.PlayerTurn <= 1)
            return;
        BlitzKernel.SummonOsty(ref x, (int)x.R.RelicVar(Id, 0));
    }
}
