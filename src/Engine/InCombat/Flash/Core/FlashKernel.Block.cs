namespace CombatSolver.Engine.InCombat.Flash;

public static partial class FlashKernel
{
    /// <summary>Mirrors <c>CombatPredictionSimulator.GainBlock</c>.</summary>
    public static decimal GainBlock(ref FlashExec x, byte owner, decimal amount, FlashValueProps props, bool fromCard)
    {
        if (IsOverOrEnding(x.B) || x.B.Creature(owner).IsDead)
            return 0m;
        // BeforeBlockGained has no supported listeners yet.
        decimal modified = FlashHooks.ModifyBlock(ref x, owner, amount, props, fromCard, x.Card, out _);
        modified = Math.Max(modified, 0m);
        // AfterModifyingBlockAmount has no supported listeners yet.
        if (modified > 0m)
        {
            ref FlashCreature creature = ref x.B.Creature(owner);
            creature.Block = (int)Math.Min(creature.Block + modified, FlashLimits.ValueClamp);
        }
        // AfterBlockGained has no supported listeners yet.
        return modified;
    }
}
