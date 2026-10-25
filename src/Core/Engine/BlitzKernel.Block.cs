namespace BlitzSolver.Engine;

public static partial class BlitzKernel
{
    /// <summary>Mirrors <c>CombatPredictionSimulator.GainBlock</c>.</summary>
    public static decimal GainBlock(ref BlitzExec x, byte owner, decimal amount, BlitzValueProps props, bool fromCard)
        => GainBlock(ref x, owner, amount, props, fromCard, fromCard ? x.Card : default);

    /// <summary>
    /// <c>GainBlock</c> with the explicit card source. The existing engine passes the source card and
    /// card play to every block hook; the kernel's card effects use the current play card above.
    /// </summary>
    public static decimal GainBlock(ref BlitzExec x, byte owner, decimal amount, BlitzValueProps props,
        bool hasCardSource, in BlitzCard cardSource)
    {
        if (IsOverOrEnding(x.B) || x.B.Creature(owner).IsDead)
            return 0m;
        // BeforeBlockGained has no supported listeners yet.
        decimal modified;
        // AfterModifyingBlockAmount only needs the changed-listener list when a listener can change the
        // block: Vambrace (identity content) or PaelsLegion's x2 while its cooldown is down.
        if (x.R.HasIdentityContent || x.R.HasPaelsLegion)
        {
            Span<BlitzListener> changed = stackalloc BlitzListener[BlitzHooks.MaxListenerCount * 2];
            modified = BlitzHooks.ModifyBlock(ref x, owner, amount, props, hasCardSource, cardSource, out _,
                changed, out int changedCount);
            modified = Math.Max(modified, 0m);
            if (changedCount > 0)
            {
                BlitzHooks.AfterModifyingBlockAmount(ref x, owner, modified, props, hasCardSource, cardSource,
                    changed[..changedCount]);
            }
        }
        else
        {
            modified = BlitzHooks.ModifyBlock(ref x, owner, amount, props, hasCardSource, cardSource, out _);
            modified = Math.Max(modified, 0m);
        }
        if (x.NeedsChoice)
            return modified;
        if (modified > 0m)
        {
            // RecordPoweredCardBlockGained: block gained inside a card play with a Move prop.
            if (hasCardSource && (props & BlitzValueProps.Move) != 0)
                x.B.AddCounter(BlitzCounter.BlockCardsPlayedThisTurn, 1);
            ref BlitzCreature creature = ref x.B.Creature(owner);
            creature.Block = (int)Math.Min(creature.Block + modified, BlitzLimits.ValueClamp);
        }
        AfterBlockGainedHook after = new() { Owner = owner, Amount = modified, Props = props, FromCard = hasCardSource };
        BlitzHooks.Dispatch(ref x, ref after, gatedByEnding: true);
        return modified;
    }
}
