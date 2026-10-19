namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Star hooks (Regent). The existing engine's order is copied from
/// <c>HookMirrors.ModifyStarCost</c>, <c>PowerLifecycleSupport.AfterStarsSpent</c> and
/// <c>HookMirrors.AfterStarsGained</c>.
/// </summary>
public static partial class FlashHooks
{
    /// <summary>
    /// Mirrors <c>HookMirrors.ModifyStarCost</c>: only a non-negative original cost is offered to the
    /// listeners, in combat-listener order, and the combat-ending gate of
    /// <c>IterateCombatHookListeners</c> applies. The caller truncates and clamps at zero.
    /// </summary>
    public static decimal ModifyStarCost(in FlashBattle battle, FlashRules rules, FlashPile pile,
        in FlashCard card, decimal amount)
    {
        if (amount < 0 || (FlashRegistry.AnyHooks & FlashHookMask.TryModifyStarCost) == 0
            || FlashKernel.IsOverOrEnding(battle))
        {
            return amount;
        }
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(battle, FlashHookMask.TryModifyStarCost, listeners);
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
    /// then the <c>ChildOfTheStarsPower</c> loop over <c>EffectivePowers()</c>. Flash therefore runs
    /// the relic listeners first and the power listeners second; both groups keep table order. No
    /// vanilla potion, card or monster implements the hook (only MiniRegent, GalacticDust and
    /// ChildOfTheStarsPower do), so no other listener kind can observe this order.
    /// </summary>
    public static void AfterStarsSpent(ref FlashExec x, int amount)
    {
        if (amount <= 0 || (FlashRegistry.AnyHooks & FlashHookMask.AfterStarsSpent) == 0)
            return;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMask.AfterStarsSpent, listeners);
        RejectUnsupportedAfterStarsSpentKinds(listeners[..count]);
        for (int index = 0; index < count; index++)
        {
            if (listeners[index].Kind != FlashListenerKind.Relic)
                continue;
            Definition(x.B, listeners[index]).AfterStarsSpent(ref x, listeners[index], amount);
            if (x.NeedsChoice)
                return;
        }
        for (int index = 0; index < count; index++)
        {
            if (listeners[index].Kind != FlashListenerKind.Power)
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
    private static void RejectUnsupportedAfterStarsSpentKinds(scoped ReadOnlySpan<FlashListener> listeners)
    {
        for (int index = 0; index < listeners.Length; index++)
        {
            if (listeners[index].Kind is not (FlashListenerKind.Relic or FlashListenerKind.Power))
                throw new FlashUnsupportedException($"星能消耗 Hook 的监听者类型 {listeners[index].Kind} 未支持。");
        }
    }

    /// <summary>
    /// Mirrors <c>HookMirrors.AfterStarsGained</c>: every combat listener in order, stopping at a
    /// pending choice. The existing engine's <c>GainStars</c> already skipped the gain while the combat
    /// is ending, so this pass needs no extra gate.
    /// </summary>
    public static bool AfterStarsGained(ref FlashExec x, int amount)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.AfterStarsGained) == 0)
            return true;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMask.AfterStarsGained, listeners);
        for (int index = 0; index < count; index++)
        {
            Definition(x.B, listeners[index]).AfterStarsGained(ref x, listeners[index], amount);
            if (x.NeedsChoice)
                return false;
        }
        return true;
    }
}
