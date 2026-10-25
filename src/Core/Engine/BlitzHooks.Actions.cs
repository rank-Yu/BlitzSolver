namespace BlitzSolver.Engine;

/// <summary>A hook invocation: which mask selects listeners and how to call one of them.</summary>
public interface IBlitzActionHook
{
    static abstract BlitzHookMask Mask { get; }

    void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self);
}

/// <summary>Information about the card play an <c>AfterCardPlayed</c> or <c>BeforeCardPlayed</c> listener observes.</summary>
public readonly struct BlitzCardPlayInfo
{
    public BlitzCardPlayInfo(in BlitzCard card, byte target, int playIndex, int playCount, BlitzPile resultPile,
        int position = -1)
    {
        Card = card;
        Target = target;
        PlayIndex = playIndex;
        PlayCount = playCount;
        ResultPile = resultPile;
        Position = position;
    }

    public BlitzCard Card { get; }

    public byte Target { get; }

    public int PlayIndex { get; }

    public int PlayCount { get; }

    public BlitzPile ResultPile { get; }

    /// <summary>Absolute position of the played card in <see cref="BlitzBattle.Cards"/> while its tail runs.</summary>
    public int Position { get; }
}

public static partial class BlitzHooks
{
    private struct SnapshotEntry
    {
        public BlitzListener Listener;
        public ushort Id;

        /// <summary>Card value of a card-attached listener at snapshot time, for re-location.</summary>
        public BlitzCard Card;

        /// <summary>Orb value at snapshot time, for re-location.</summary>
        public BlitzOrb Orb;

        /// <summary>Index among byte-equal cards (or orbs) at snapshot time, or -1.</summary>
        public int Occurrence;
    }

    /// <summary>
    /// Dispatches an action hook over a snapshot of the listeners taken at the start, mirroring the
    /// existing engine's listener-list enumeration. Each listener is re-resolved before its call
    /// (power slots move when powers are removed); a listener that left in between is skipped.
    /// Stops as soon as an effect needs a choice.
    /// </summary>
    public static void Dispatch<T>(ref BlitzExec x, ref T hook, bool gatedByEnding) where T : struct, IBlitzActionHook
    {
        if ((BlitzRegistry.AnyHooks & T.Mask) == 0 || gatedByEnding && BlitzKernel.IsOverOrEnding(x.B))
            return;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, T.Mask, listeners);
        if (count == 0)
            return;
        Span<SnapshotEntry> snapshot = stackalloc SnapshotEntry[count];
        for (int index = 0; index < count; index++)
            snapshot[index] = Snapshot(x.B, listeners[index]);
        for (int index = 0; index < count; index++)
        {
            if (!TryResolve(x.B, snapshot[index], out BlitzListener current))
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
    public static void DispatchPowerHook<T>(ref BlitzExec x, ref T hook) where T : struct, IBlitzActionHook
    {
        if ((BlitzRegistry.AnyHooks & T.Mask) == 0)
            return;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, T.Mask, listeners);
        if (count == 0)
            return;
        Span<SnapshotEntry> snapshot = stackalloc SnapshotEntry[count];
        int powers = 0;
        for (int index = 0; index < count; index++)
        {
            if (listeners[index].Kind != BlitzListenerKind.Power)
                continue;
            snapshot[powers++] = Snapshot(x.B, listeners[index]);
        }
        for (int index = 0; index < powers; index++)
        {
            if (!TryResolve(x.B, snapshot[index], out BlitzListener current))
                continue;
            if (current.Kind != BlitzListenerKind.Power || x.B.Powers[current.Index].Amount <= 0)
                continue;
            hook.Invoke(ref x, Definition(x.B, current), current);
            if (x.NeedsChoice)
                return;
        }
    }

    private static SnapshotEntry Snapshot(in BlitzBattle battle, BlitzListener listener)
    {
        SnapshotEntry entry = new() { Listener = listener, Id = ListenerId(battle, listener), Occurrence = -1 };
        if (listener.Kind == BlitzListenerKind.Orb)
        {
            entry.Orb = battle.Orbs[listener.Index];
            int orbOccurrence = 0;
            for (int index = 0; index < listener.Index; index++)
            {
                if (OrbEqual(battle.Orbs[index], entry.Orb))
                    orbOccurrence++;
            }
            entry.Occurrence = orbOccurrence;
            return entry;
        }
        if (listener.Kind is not (BlitzListenerKind.Card or BlitzListenerKind.Affliction or BlitzListenerKind.Enchantment))
            return entry;
        entry.Card = battle.Cards[listener.Index];
        int occurrence = 0;
        for (int position = 0; position < listener.Index; position++)
        {
            if (SameListenerCard(battle.Cards[position], entry.Card))
                occurrence++;
        }
        entry.Occurrence = occurrence;
        return entry;
    }

    private static bool OrbEqual(in BlitzOrb left, in BlitzOrb right)
        => left.Id == right.Id && left.Aux == right.Aux;

    /// <summary>
    /// Identity used to relocate a captured card listener. The existing engine holds a
    /// <c>CardModel</c> reference, so a hook that changes a card's mutable value (upgrade, cost
    /// modifiers, dynamic-variable deltas, keywords, affliction or enchantment) must not make the
    /// listener disappear; only an actual replacement of the card (a different native id, or a clone
    /// versus a non-clone) does. Two copies that differ only in mutable values are interchangeable for
    /// listener order, exactly like the native object references.
    /// </summary>
    private static bool SameListenerCard(in BlitzCard left, in BlitzCard right)
        => left.Id == right.Id && left.IsClone == right.IsClone;

    private static ushort ListenerId(in BlitzBattle battle, BlitzListener listener) => listener.Kind switch
    {
        BlitzListenerKind.Power => battle.Powers[listener.Index].Id,
        BlitzListenerKind.Relic => battle.Relics[listener.Index].Id,
        BlitzListenerKind.Potion => battle.Potions[listener.Index],
        BlitzListenerKind.Monster => battle.Enemies[listener.Index].MonsterId,
        BlitzListenerKind.Orb => battle.Orbs[listener.Index].Id,
        BlitzListenerKind.Card => battle.Cards[listener.Index].Id,
        BlitzListenerKind.Affliction => battle.Cards[listener.Index].AfflictionId,
        BlitzListenerKind.Enchantment => battle.Cards[listener.Index].EnchantmentId,
        _ => throw new ArgumentOutOfRangeException(nameof(listener), listener.Kind, null),
    };

    private static bool TryResolve(in BlitzBattle battle, in SnapshotEntry entry, out BlitzListener current)
    {
        current = entry.Listener;
        switch (entry.Listener.Kind)
        {
            case BlitzListenerKind.Power:
                if (entry.Listener.Index < battle.PowerCount
                    && battle.Powers[entry.Listener.Index].Id == entry.Id
                    && battle.Powers[entry.Listener.Index].Owner == entry.Listener.Owner)
                {
                    return true;
                }
                int found = BlitzKernel.FindPower(battle, entry.Listener.Owner, entry.Id);
                if (found < 0)
                    return false;
                current = new BlitzListener(BlitzListenerKind.Power, (byte)found, entry.Listener.Owner);
                return true;
            case BlitzListenerKind.Monster:
                // The existing engine still calls a monster captured in the listener snapshot.
                return true;
            case BlitzListenerKind.Orb:
                // The queue shifts when an earlier listener channels/evokes. Re-locate the captured orb
                // value by its occurrence among equal orbs, like cards: two orbs with the same value
                // are interchangeable, but the listener must still point at one of them.
                if (entry.Listener.Index < battle.OrbCount && OrbEqual(battle.Orbs[entry.Listener.Index], entry.Orb))
                    return true;
                int occurrence = entry.Occurrence;
                for (int index = 0; index < battle.OrbCount; index++)
                {
                    if (!OrbEqual(battle.Orbs[index], entry.Orb))
                        continue;
                    if (occurrence-- == 0)
                    {
                        current = new BlitzListener(BlitzListenerKind.Orb, (byte)index, entry.Listener.Owner);
                        return true;
                    }
                }
                return false;
            case BlitzListenerKind.Card:
            case BlitzListenerKind.Affliction:
            case BlitzListenerKind.Enchantment:
                // The position is still valid while the card stays where it was (it may have been
                // modified in place). Otherwise re-locate the snapshot value by its occurrence among
                // equal cards, so a hook that moves cards cannot skip or double-visit a listener.
                if (entry.Listener.Index < battle.CardCount && ListenerId(battle, entry.Listener) == entry.Id)
                    return true;
                if (!TryLocateCard(battle, entry, out int located))
                    return false;
                current = new BlitzListener(entry.Listener.Kind, (byte)located, entry.Listener.Owner);
                return true;
            default:
                return ListenerId(battle, entry.Listener) == entry.Id;
        }
    }

    /// <summary>Locates the snapshot card by its value and occurrence over the whole battle.</summary>
    private static bool TryLocateCard(in BlitzBattle battle, in SnapshotEntry entry, out int index)
    {
        int occurrence = entry.Occurrence;
        for (int position = 0; position < battle.CardCount; position++)
        {
            if (!SameListenerCard(battle.Cards[position], entry.Card))
                continue;
            if (occurrence-- == 0)
            {
                index = position;
                return ListenerId(battle, new BlitzListener(entry.Listener.Kind, (byte)position, entry.Listener.Owner))
                    == entry.Id;
            }
        }
        index = -1;
        return false;
    }
}

// Hook invokers. Each is a value type so dispatch is specialized per hook without allocation.

public struct AfterCurrentHpChangedHook : IBlitzActionHook
{
    public byte Creature;
    public decimal Delta;

    public static BlitzHookMask Mask => BlitzHookMask.AfterCurrentHpChanged;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterCurrentHpChanged(ref x, self, Creature, Delta);
}

public struct BeforeDeathHook : IBlitzActionHook
{
    public byte Creature;

    public static BlitzHookMask Mask => BlitzHookMask.BeforeDeath;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.BeforeDeath(ref x, self, Creature);
}

public struct AfterDeathHook : IBlitzActionHook
{
    public byte Creature;
    public bool WasRemovalPrevented;

    public static BlitzHookMask Mask => BlitzHookMask.AfterDeath;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterDeath(ref x, self, Creature, WasRemovalPrevented);
}

public struct AfterBlockBrokenHook : IBlitzActionHook
{
    public byte Target;
    public byte Breaker;

    public static BlitzHookMask Mask => BlitzHookMask.AfterBlockBroken;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterBlockBroken(ref x, self, Target, Breaker);
}

public struct AfterCardDrawnEarlyHook : IBlitzActionHook
{
    public int DrawnPosition;
    public bool FromHandDraw;

    public static BlitzHookMask Mask => BlitzHookMask.AfterCardDrawnEarly;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterCardDrawnEarly(ref x, self, DrawnPosition, FromHandDraw);
}

public struct AfterCardDrawnHook : IBlitzActionHook
{
    public int DrawnPosition;
    public bool FromHandDraw;

    public static BlitzHookMask Mask => BlitzHookMask.AfterCardDrawn;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterCardDrawn(ref x, self, DrawnPosition, FromHandDraw);
}

public struct AfterAutoPostPlayPhaseEnteredHook : IBlitzActionHook
{
    public static BlitzHookMask Mask => BlitzHookMask.AfterAutoPostPlayPhaseEntered;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterAutoPostPlayPhaseEntered(ref x, self);
}

public struct BeforeSideTurnEndVeryEarlyHook : IBlitzActionHook
{
    public BlitzSide Side;
    public ushort Participants;

    public static BlitzHookMask Mask => BlitzHookMask.BeforeSideTurnEndVeryEarly;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.BeforeSideTurnEndVeryEarly(ref x, self, Side, Participants);
}

public struct BeforeSideTurnEndEarlyHook : IBlitzActionHook
{
    public BlitzSide Side;
    public ushort Participants;

    public static BlitzHookMask Mask => BlitzHookMask.BeforeSideTurnEndEarly;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.BeforeSideTurnEndEarly(ref x, self, Side, Participants);
}

public struct BeforeSideTurnEndHook : IBlitzActionHook
{
    public BlitzSide Side;
    public ushort Participants;

    public static BlitzHookMask Mask => BlitzHookMask.BeforeSideTurnEnd;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.BeforeSideTurnEnd(ref x, self, Side, Participants);
}

public struct BeforeDamageReceivedHook : IBlitzActionHook
{
    public byte Target;
    public decimal Amount;
    public BlitzValueProps Props;
    public byte Dealer;
    public bool HasCardSource;
    public BlitzCard CardSource;

    public static BlitzHookMask Mask => BlitzHookMask.BeforeDamageReceived;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.BeforeDamageReceived(ref x, self, Target, Amount, Props, Dealer, HasCardSource, CardSource);
}

public struct AfterDamageReceivedHook : IBlitzActionHook
{
    public BlitzDamageResult Result;
    public byte Dealer;

    public static BlitzHookMask Mask => BlitzHookMask.AfterDamageReceived;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterDamageReceived(ref x, self, Result, Dealer);
}

public struct AfterDamageReceivedLateHook : IBlitzActionHook
{
    public BlitzDamageResult Result;
    public byte Dealer;

    public static BlitzHookMask Mask => BlitzHookMask.AfterDamageReceivedLate;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterDamageReceivedLate(ref x, self, Result, Dealer);
}

public struct AfterDamageGivenHook : IBlitzActionHook
{
    public BlitzDamageResult Result;
    public byte Dealer;

    public static BlitzHookMask Mask => BlitzHookMask.AfterDamageGiven;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterDamageGiven(ref x, self, Result, Dealer);
}

public struct BeforeCardPlayedHook : IBlitzActionHook
{
    public BlitzCardPlayInfo Play;

    public static BlitzHookMask Mask => BlitzHookMask.BeforeCardPlayed;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.BeforeCardPlayed(ref x, self, Play);
}

public struct AfterCardPlayedHook : IBlitzActionHook
{
    public BlitzCardPlayInfo Play;

    public static BlitzHookMask Mask => BlitzHookMask.AfterCardPlayed;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterCardPlayed(ref x, self, Play);
}

public struct AfterCardPlayedLateHook : IBlitzActionHook
{
    public BlitzCardPlayInfo Play;

    public static BlitzHookMask Mask => BlitzHookMask.AfterCardPlayedLate;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterCardPlayedLate(ref x, self, Play);
}

public struct AfterCardExhaustedHook : IBlitzActionHook
{
    public BlitzCard Card;
    public bool CausedByEthereal;

    public static BlitzHookMask Mask => BlitzHookMask.AfterCardExhausted;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterCardExhausted(ref x, self, Card, CausedByEthereal);
}

public struct AfterCardDiscardedHook : IBlitzActionHook
{
    public BlitzCard Card;

    public static BlitzHookMask Mask => BlitzHookMask.AfterCardDiscarded;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterCardDiscarded(ref x, self, Card);
}

public struct AfterBlockGainedHook : IBlitzActionHook
{
    public byte Owner;
    public decimal Amount;
    public BlitzValueProps Props;
    public bool FromCard;

    public static BlitzHookMask Mask => BlitzHookMask.AfterBlockGained;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterBlockGained(ref x, self, Owner, Amount, Props, FromCard);
}

public struct BeforeAttackHook : IBlitzActionHook
{
    public byte Attacker;
    public BlitzValueProps Props;

    public static BlitzHookMask Mask => BlitzHookMask.BeforeAttack;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.BeforeAttack(ref x, self, Attacker, Props);
}

public struct AfterAttackHook : IBlitzActionHook
{
    public byte Attacker;

    public static BlitzHookMask Mask => BlitzHookMask.AfterAttack;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterAttack(ref x, self, Attacker);
}

public struct AfterPowerAmountChangedHook : IBlitzActionHook
{
    public ushort PowerId;
    public int Delta;
    public byte Applier;
    public byte Target;

    public static BlitzHookMask Mask => BlitzHookMask.AfterPowerAmountChanged;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterPowerAmountChanged(ref x, self, PowerId, Delta, Applier, Target);
}

public struct AfterShuffleHook : IBlitzActionHook
{
    public byte Shuffler;

    public static BlitzHookMask Mask => BlitzHookMask.AfterShuffle;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterShuffle(ref x, self, Shuffler);
}

public struct AfterCardEnteredCombatHook : IBlitzActionHook
{
    public BlitzPile Pile;
    public int Index;
    public BlitzCard Card;

    public static BlitzHookMask Mask => BlitzHookMask.AfterCardEnteredCombat;

    public readonly void Invoke(ref BlitzExec x, BlitzListenerDef definition, BlitzListener self)
        => definition.AfterCardEnteredCombat(ref x, self, Pile, Index, Card);
}
