namespace BlitzSolver.Engine;

/// <summary>
/// Star hooks (Regent). The existing engine's order is copied from
/// <c>HookMirrors.ModifyStarCost</c>, <c>PowerLifecycleSupport.AfterStarsSpent</c> and
/// <c>HookMirrors.AfterStarsGained</c>.
/// </summary>
public static partial class BlitzHooks
{
    /// <summary>
    /// Mirrors <c>HookMirrors.ModifyStarCost</c>: only a non-negative original cost is offered to the
    /// listeners, in combat-listener order, and the combat-ending gate of
    /// <c>IterateCombatHookListeners</c> applies. The caller truncates and clamps at zero.
    /// </summary>
    public static decimal ModifyStarCost(in BlitzBattle battle, BlitzRules rules, BlitzPile pile,
        in BlitzCard card, decimal amount)
    {
        if (amount < 0 || (BlitzRegistry.AnyHooks & BlitzHookMask.TryModifyStarCost) == 0
            || BlitzKernel.IsOverOrEnding(battle))
        {
            return amount;
        }
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(battle, BlitzHookMask.TryModifyStarCost, listeners);
        for (int index = 0; index < count; index++)
        {
            amount = Definition(battle, listeners[index]).TryModifyStarCost(battle, rules, listeners[index],
                card, pile, amount);
        }
        return amount;
    }

    /// <summary>
    /// <c>PowerLifecycleSupport.AfterStarsSpent</c>: the existing engine does not walk the listener
    /// list. It runs <c>TriggerRelicsAfterStarsSpent</c> over the player's relics in relic order and
    /// then the <c>ChildOfTheStarsPower</c> loop over <c>EffectivePowers()</c>. Blitz therefore runs
    /// the relic listeners first and the power listeners second; both groups keep table order. No
    /// vanilla potion, card or monster implements the hook (only MiniRegent, GalacticDust and
    /// ChildOfTheStarsPower do), so no other listener kind can observe this order.
    /// </summary>
    public static void AfterStarsSpent(ref BlitzExec x, int amount)
    {
        if (amount <= 0 || (BlitzRegistry.AnyHooks & BlitzHookMask.AfterStarsSpent) == 0)
            return;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMask.AfterStarsSpent, listeners);
        RejectUnsupportedAfterStarsSpentKinds(listeners[..count]);
        for (int index = 0; index < count; index++)
        {
            if (listeners[index].Kind != BlitzListenerKind.Relic)
                continue;
            Definition(x.B, listeners[index]).AfterStarsSpent(ref x, listeners[index], amount);
            if (x.NeedsChoice)
                return;
        }
        for (int index = 0; index < count; index++)
        {
            if (listeners[index].Kind != BlitzListenerKind.Power)
                continue;
            Definition(x.B, listeners[index]).AfterStarsSpent(ref x, listeners[index], amount);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// The existing engine's two AfterStarsSpent phases only visit player relics and powers; a
    /// potion, card, monster or orb implementing the hook would be dropped silently, so a capture of
    /// such a listener is an explicit boundary.
    /// </summary>
    private static void RejectUnsupportedAfterStarsSpentKinds(scoped ReadOnlySpan<BlitzListener> listeners)
    {
        for (int index = 0; index < listeners.Length; index++)
        {
            if (listeners[index].Kind is not (BlitzListenerKind.Relic or BlitzListenerKind.Power))
                throw new BlitzUnsupportedException($"星能消耗 Hook 的监听者类型 {listeners[index].Kind} 未支持。");
        }
    }

    /// <summary>
    /// Mirrors <c>HookMirrors.AfterStarsGained</c>: every combat listener in order, stopping at a
    /// pending choice. The existing engine's <c>GainStars</c> already skipped the gain while the combat
    /// is ending, so this pass needs no extra gate.
    /// </summary>
    public static bool AfterStarsGained(ref BlitzExec x, int amount)
    {
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.AfterStarsGained) == 0)
            return true;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMask.AfterStarsGained, listeners);
        for (int index = 0; index < count; index++)
        {
            Definition(x.B, listeners[index]).AfterStarsGained(ref x, listeners[index], amount);
            if (x.NeedsChoice)
                return false;
        }
        return true;
    }
}
