namespace CombatSolver.Engine.InCombat.Flash;

public static partial class FlashKernel
{
    /// <summary>Mirrors <c>CombatPredictionSimulator.GainBlock</c>.</summary>
    public static decimal GainBlock(ref FlashExec x, byte owner, decimal amount, FlashValueProps props, bool fromCard)
        => GainBlock(ref x, owner, amount, props, fromCard, fromCard ? x.Card : default);

    /// <summary>
    /// <c>GainBlock</c> with the explicit card source. The existing engine passes the source card and
    /// card play to every block hook; the kernel's card effects use the current play card above.
    /// </summary>
    public static decimal GainBlock(ref FlashExec x, byte owner, decimal amount, FlashValueProps props,
        bool hasCardSource, in FlashCard cardSource)
    {
        if (IsOverOrEnding(x.B) || x.B.Creature(owner).IsDead)
            return 0m;
        // BeforeBlockGained has no supported listeners yet.
        decimal modified;
        if (x.R.HasIdentityContent)
        {
            Span<FlashListener> changed = stackalloc FlashListener[FlashHooks.MaxListenerCount];
            modified = FlashHooks.ModifyBlock(ref x, owner, amount, props, hasCardSource, cardSource, out _,
                changed, out int changedCount);
            modified = Math.Max(modified, 0m);
            if (changedCount > 0)
            {
                FlashHooks.AfterModifyingBlockAmount(ref x, owner, modified, props, hasCardSource, cardSource,
                    changed[..changedCount]);
            }
        }
        else
        {
            modified = FlashHooks.ModifyBlock(ref x, owner, amount, props, hasCardSource, cardSource, out _);
            modified = Math.Max(modified, 0m);
        }
        if (x.NeedsChoice)
            return modified;
        if (modified > 0m)
        {
            // RecordPoweredCardBlockGained: block gained inside a card play with a Move prop.
            if (hasCardSource && (props & FlashValueProps.Move) != 0)
                x.B.AddCounter(FlashCounter.BlockCardsPlayedThisTurn, 1);
            ref FlashCreature creature = ref x.B.Creature(owner);
            creature.Block = (int)Math.Min(creature.Block + modified, FlashLimits.ValueClamp);
        }
        AfterBlockGainedHook after = new() { Owner = owner, Amount = modified, Props = props, FromCard = hasCardSource };
        FlashHooks.Dispatch(ref x, ref after, gatedByEnding: true);
        return modified;
    }
}
