namespace CombatSolver.Engine.InCombat.Flash.Content.Relics;

/// <summary>Native <c>BurningBlood</c>: heals after combat only, so it has no in-combat hooks.</summary>
[FlashContent("BURNING_BLOOD")]
public sealed class BurningBlood : FlashRelicDef;

/// <summary>
/// <c>RingOfTheSnake</c>: <c>ModifyHandDraw</c> adds Cards while <c>TurnNumber &lt;= 1</c>
/// (RingOfTheSnake.cs; the existing engine runs the same contribution from
/// <c>PersistentPowerSupport.GetTurnBasedHandDrawContribution</c>). The turn 1 hand draw happens before
/// the root, so later roots see nothing from it.
/// </summary>
[FlashContent("RING_OF_THE_SNAKE")]
public sealed class RingOfTheSnake : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override decimal ModifyHandDraw(ref FlashExec x, FlashListener self, decimal amount)
        => x.B.PlayerTurn <= 1 ? amount + x.R.RelicVar(Id, 0) : amount;
}
