namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>A hook invocation: which mask selects listeners and how to call one of them.</summary>
public interface IFlashActionHook
{
    static abstract FlashHookMask Mask { get; }

    void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self);
}

/// <summary>Information about the card play an <c>AfterCardPlayed</c> or <c>BeforeCardPlayed</c> listener observes.</summary>
public readonly struct FlashCardPlayInfo
{
    public FlashCardPlayInfo(in FlashCard card, byte target, int playIndex, int playCount, FlashPile resultPile)
    {
        Card = card;
        Target = target;
        PlayIndex = playIndex;
        PlayCount = playCount;
        ResultPile = resultPile;
    }

    public FlashCard Card { get; }

    public byte Target { get; }

    public int PlayIndex { get; }

    public int PlayCount { get; }

    public FlashPile ResultPile { get; }
}

public static partial class FlashHooks
{
    private struct SnapshotEntry
    {
        public FlashListener Listener;
        public ushort Id;

        /// <summary>Card value of a card-attached listener at snapshot time, for re-location.</summary>
        public FlashCard Card;

        /// <summary>Index among byte-equal cards in the whole battle at snapshot time, or -1.</summary>
        public int Occurrence;
    }

    /// <summary>
    /// Dispatches an action hook over a snapshot of the listeners taken at the start, mirroring the
    /// existing engine's listener-list enumeration. Each listener is re-resolved before its call
    /// (power slots move when powers are removed); a listener that left in between is skipped.
    /// Stops as soon as an effect needs a choice.
    /// </summary>
    public static void Dispatch<T>(ref FlashExec x, ref T hook, bool gatedByEnding) where T : struct, IFlashActionHook
    {
        if ((FlashRegistry.AnyHooks & T.Mask) == 0 || gatedByEnding && FlashKernel.IsOverOrEnding(x.B))
            return;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, T.Mask, listeners);
        if (count == 0)
            return;
        Span<SnapshotEntry> snapshot = stackalloc SnapshotEntry[count];
        for (int index = 0; index < count; index++)
            snapshot[index] = Snapshot(x.B, listeners[index]);
        for (int index = 0; index < count; index++)
        {
            if (!TryResolve(x.B, snapshot[index], out FlashListener current))
                continue;
            hook.Invoke(ref x, Definition(x.B, current), current);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// Power-only action hook dispatch. The existing engine's <c>AfterPowerAmountChanged</c> loop
    /// iterates the current powers (<c>PowerLifecycleSupport.ResolvePowerAmountChanges</c>), not every
    /// combat listener, so the general listener list is filtered to powers while keeping its order.
    /// </summary>
    public static void DispatchPowerHook<T>(ref FlashExec x, ref T hook) where T : struct, IFlashActionHook
    {
        if ((FlashRegistry.AnyHooks & T.Mask) == 0)
            return;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, T.Mask, listeners);
        if (count == 0)
            return;
        Span<SnapshotEntry> snapshot = stackalloc SnapshotEntry[count];
        int powers = 0;
        for (int index = 0; index < count; index++)
        {
            if (listeners[index].Kind != FlashListenerKind.Power)
                continue;
            snapshot[powers++] = Snapshot(x.B, listeners[index]);
        }
        for (int index = 0; index < powers; index++)
        {
            if (!TryResolve(x.B, snapshot[index], out FlashListener current))
                continue;
            if (current.Kind != FlashListenerKind.Power || x.B.Powers[current.Index].Amount <= 0)
                continue;
            hook.Invoke(ref x, Definition(x.B, current), current);
            if (x.NeedsChoice)
                return;
        }
    }

    private static SnapshotEntry Snapshot(in FlashBattle battle, FlashListener listener)
    {
        SnapshotEntry entry = new() { Listener = listener, Id = ListenerId(battle, listener), Occurrence = -1 };
        if (listener.Kind is not (FlashListenerKind.Card or FlashListenerKind.Affliction or FlashListenerKind.Enchantment))
            return entry;
        entry.Card = battle.Cards[listener.Index];
        int occurrence = 0;
        for (int position = 0; position < listener.Index; position++)
        {
            if (FlashFingerprint.Equal(battle.Cards[position], entry.Card))
                occurrence++;
        }
        entry.Occurrence = occurrence;
        return entry;
    }

    private static ushort ListenerId(in FlashBattle battle, FlashListener listener) => listener.Kind switch
    {
        FlashListenerKind.Power => battle.Powers[listener.Index].Id,
        FlashListenerKind.Relic => battle.Relics[listener.Index].Id,
        FlashListenerKind.Potion => battle.Potions[listener.Index],
        FlashListenerKind.Monster => battle.Enemies[listener.Index].MonsterId,
        FlashListenerKind.Orb => battle.Orbs[listener.Index].Id,
        FlashListenerKind.Card => battle.Cards[listener.Index].Id,
        FlashListenerKind.Affliction => battle.Cards[listener.Index].AfflictionId,
        FlashListenerKind.Enchantment => battle.Cards[listener.Index].EnchantmentId,
        _ => throw new ArgumentOutOfRangeException(nameof(listener), listener.Kind, null),
    };

    private static bool TryResolve(in FlashBattle battle, in SnapshotEntry entry, out FlashListener current)
    {
        current = entry.Listener;
        switch (entry.Listener.Kind)
        {
            case FlashListenerKind.Power:
                if (entry.Listener.Index < battle.PowerCount
                    && battle.Powers[entry.Listener.Index].Id == entry.Id
                    && battle.Powers[entry.Listener.Index].Owner == entry.Listener.Owner)
                {
                    return true;
                }
                int found = FlashKernel.FindPower(battle, entry.Listener.Owner, entry.Id);
                if (found < 0)
                    return false;
                current = new FlashListener(FlashListenerKind.Power, (byte)found, entry.Listener.Owner);
                return true;
            case FlashListenerKind.Monster:
                // The existing engine still calls a monster captured in the listener snapshot.
                return true;
            case FlashListenerKind.Card:
            case FlashListenerKind.Affliction:
            case FlashListenerKind.Enchantment:
                // The position is still valid while the card stays where it was (it may have been
                // modified in place). Otherwise re-locate the snapshot value by its occurrence among
                // equal cards, so a hook that moves cards cannot skip or double-visit a listener.
                if (entry.Listener.Index < battle.CardCount && ListenerId(battle, entry.Listener) == entry.Id)
                    return true;
                if (!TryLocateCard(battle, entry, out int located))
                    return false;
                current = new FlashListener(entry.Listener.Kind, (byte)located, entry.Listener.Owner);
                return true;
            default:
                return ListenerId(battle, entry.Listener) == entry.Id;
        }
    }

    /// <summary>Locates the snapshot card by its value and occurrence over the whole battle.</summary>
    private static bool TryLocateCard(in FlashBattle battle, in SnapshotEntry entry, out int index)
    {
        int occurrence = entry.Occurrence;
        for (int position = 0; position < battle.CardCount; position++)
        {
            if (!FlashFingerprint.Equal(battle.Cards[position], entry.Card))
                continue;
            if (occurrence-- == 0)
            {
                index = position;
                return ListenerId(battle, new FlashListener(entry.Listener.Kind, (byte)position, entry.Listener.Owner))
                    == entry.Id;
            }
        }
        index = -1;
        return false;
    }
}

// Hook invokers. Each is a value type so dispatch is specialized per hook without allocation.

public struct AfterCurrentHpChangedHook : IFlashActionHook
{
    public byte Creature;
    public decimal Delta;

    public static FlashHookMask Mask => FlashHookMask.AfterCurrentHpChanged;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterCurrentHpChanged(ref x, self, Creature, Delta);
}

public struct BeforeDeathHook : IFlashActionHook
{
    public byte Creature;

    public static FlashHookMask Mask => FlashHookMask.BeforeDeath;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.BeforeDeath(ref x, self, Creature);
}

public struct AfterDeathHook : IFlashActionHook
{
    public byte Creature;
    public bool WasRemovalPrevented;

    public static FlashHookMask Mask => FlashHookMask.AfterDeath;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterDeath(ref x, self, Creature, WasRemovalPrevented);
}

public struct AfterBlockBrokenHook : IFlashActionHook
{
    public byte Target;
    public byte Breaker;

    public static FlashHookMask Mask => FlashHookMask.AfterBlockBroken;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterBlockBroken(ref x, self, Target, Breaker);
}

public struct AfterCardDrawnEarlyHook : IFlashActionHook
{
    public int DrawnPosition;
    public bool FromHandDraw;

    public static FlashHookMask Mask => FlashHookMask.AfterCardDrawnEarly;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterCardDrawnEarly(ref x, self, DrawnPosition, FromHandDraw);
}

public struct AfterCardDrawnHook : IFlashActionHook
{
    public int DrawnPosition;
    public bool FromHandDraw;

    public static FlashHookMask Mask => FlashHookMask.AfterCardDrawn;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterCardDrawn(ref x, self, DrawnPosition, FromHandDraw);
}

public struct AfterAutoPostPlayPhaseEnteredHook : IFlashActionHook
{
    public static FlashHookMask Mask => FlashHookMask.AfterAutoPostPlayPhaseEntered;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterAutoPostPlayPhaseEntered(ref x, self);
}

public struct BeforeSideTurnEndVeryEarlyHook : IFlashActionHook
{
    public FlashSide Side;
    public ushort Participants;

    public static FlashHookMask Mask => FlashHookMask.BeforeSideTurnEndVeryEarly;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.BeforeSideTurnEndVeryEarly(ref x, self, Side, Participants);
}

public struct BeforeSideTurnEndEarlyHook : IFlashActionHook
{
    public FlashSide Side;
    public ushort Participants;

    public static FlashHookMask Mask => FlashHookMask.BeforeSideTurnEndEarly;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.BeforeSideTurnEndEarly(ref x, self, Side, Participants);
}

public struct BeforeSideTurnEndHook : IFlashActionHook
{
    public FlashSide Side;
    public ushort Participants;

    public static FlashHookMask Mask => FlashHookMask.BeforeSideTurnEnd;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.BeforeSideTurnEnd(ref x, self, Side, Participants);
}

public struct BeforeDamageReceivedHook : IFlashActionHook
{
    public byte Target;
    public decimal Amount;
    public FlashValueProps Props;
    public byte Dealer;
    public bool HasCardSource;
    public FlashCard CardSource;

    public static FlashHookMask Mask => FlashHookMask.BeforeDamageReceived;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.BeforeDamageReceived(ref x, self, Target, Amount, Props, Dealer, HasCardSource, CardSource);
}

public struct AfterDamageReceivedHook : IFlashActionHook
{
    public FlashDamageResult Result;
    public byte Dealer;

    public static FlashHookMask Mask => FlashHookMask.AfterDamageReceived;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterDamageReceived(ref x, self, Result, Dealer);
}

public struct AfterDamageReceivedLateHook : IFlashActionHook
{
    public FlashDamageResult Result;
    public byte Dealer;

    public static FlashHookMask Mask => FlashHookMask.AfterDamageReceivedLate;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterDamageReceivedLate(ref x, self, Result, Dealer);
}

public struct AfterDamageGivenHook : IFlashActionHook
{
    public FlashDamageResult Result;
    public byte Dealer;

    public static FlashHookMask Mask => FlashHookMask.AfterDamageGiven;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterDamageGiven(ref x, self, Result, Dealer);
}

public struct BeforeCardPlayedHook : IFlashActionHook
{
    public FlashCardPlayInfo Play;

    public static FlashHookMask Mask => FlashHookMask.BeforeCardPlayed;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.BeforeCardPlayed(ref x, self, Play);
}

public struct AfterCardPlayedHook : IFlashActionHook
{
    public FlashCardPlayInfo Play;

    public static FlashHookMask Mask => FlashHookMask.AfterCardPlayed;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterCardPlayed(ref x, self, Play);
}

public struct AfterCardPlayedLateHook : IFlashActionHook
{
    public FlashCardPlayInfo Play;

    public static FlashHookMask Mask => FlashHookMask.AfterCardPlayedLate;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterCardPlayedLate(ref x, self, Play);
}

public struct AfterCardExhaustedHook : IFlashActionHook
{
    public FlashCard Card;
    public bool CausedByEthereal;

    public static FlashHookMask Mask => FlashHookMask.AfterCardExhausted;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterCardExhausted(ref x, self, Card, CausedByEthereal);
}

public struct AfterBlockGainedHook : IFlashActionHook
{
    public byte Owner;
    public decimal Amount;
    public FlashValueProps Props;
    public bool FromCard;

    public static FlashHookMask Mask => FlashHookMask.AfterBlockGained;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterBlockGained(ref x, self, Owner, Amount, Props, FromCard);
}

public struct BeforeAttackHook : IFlashActionHook
{
    public byte Attacker;
    public FlashValueProps Props;

    public static FlashHookMask Mask => FlashHookMask.BeforeAttack;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.BeforeAttack(ref x, self, Attacker, Props);
}

public struct AfterAttackHook : IFlashActionHook
{
    public byte Attacker;

    public static FlashHookMask Mask => FlashHookMask.AfterAttack;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterAttack(ref x, self, Attacker);
}

public struct AfterPowerAmountChangedHook : IFlashActionHook
{
    public ushort PowerId;
    public int Delta;
    public byte Applier;

    public static FlashHookMask Mask => FlashHookMask.AfterPowerAmountChanged;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterPowerAmountChanged(ref x, self, PowerId, Delta, Applier);
}

public struct AfterShuffleHook : IFlashActionHook
{
    public byte Shuffler;

    public static FlashHookMask Mask => FlashHookMask.AfterShuffle;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterShuffle(ref x, self, Shuffler);
}

public struct AfterCardEnteredCombatHook : IFlashActionHook
{
    public FlashPile Pile;
    public int Index;
    public FlashCard Card;

    public static FlashHookMask Mask => FlashHookMask.AfterCardEnteredCombat;

    public readonly void Invoke(ref FlashExec x, FlashListenerDef definition, FlashListener self)
        => definition.AfterCardEnteredCombat(ref x, self, Pile, Index, Card);
}
