namespace BlitzSolver.Engine;

/// <summary>
/// Hook dispatch mirroring <c>HookMirrors</c>. Listeners are visited in the existing engine's
/// combat listener order (<c>SimulatedCombatState</c> list construction): for each creature —
/// player, Osty, then enemies in roster order — its powers, then the player's relics and potions or
/// the enemy's monster model; then the player's orbs; then cards in Hand, Draw, Discard, Exhaust and
/// Play order, each followed by its affliction and enchantment.
/// </summary>
[System.Runtime.CompilerServices.SkipLocalsInit]
public static partial class BlitzHooks
{
    private const int MaxListeners = BlitzLimits.Powers + BlitzLimits.Relics + BlitzLimits.PotionSlots
        + BlitzLimits.Enemies + BlitzLimits.Orbs + BlitzLimits.Cards * 3;

    /// <summary>Upper bound of <see cref="CollectListeners"/> output, for callers' stack buffers.</summary>
    public const int MaxListenerCount = MaxListeners;

    /// <summary>Collects the listeners whose definition overrides <paramref name="hook"/>, in dispatch order.</summary>
    public static int CollectListeners(in BlitzBattle battle, BlitzHookMask hook, Span<BlitzListener> output)
        => (BlitzRegistry.AnyHooks & hook) == 0 ? 0 : CollectListeners(battle, new HookBits(hook, BlitzHookMaskExt.None), output);

    /// <summary>The <see cref="BlitzHookMaskExt"/> counterpart of the overload above, in the same dispatch order.</summary>
    public static int CollectListeners(in BlitzBattle battle, BlitzHookMaskExt hook, Span<BlitzListener> output)
        => (BlitzRegistry.AnyHooksExt & hook) == 0 ? 0 : CollectListeners(battle, new HookBits(BlitzHookMask.None, hook), output);

    private readonly record struct HookBits(BlitzHookMask Main, BlitzHookMaskExt Ext);

    private static int CollectListeners(in BlitzBattle battle, HookBits hook, Span<BlitzListener> output)
    {
        int count = 0;
        bool playerHooks = (battle.Flags & BlitzBattleFlags.PlayerHooksActive) != 0;
        int nextPower = 0;

        // Player creature.
        nextPower = CollectPowers(battle, BlitzLimits.PlayerOwner, hook, nextPower, output, ref count);
        if (playerHooks)
        {
            for (int index = 0; index < battle.RelicCount; index++)
            {
                ref readonly BlitzRelic relic = ref battle.Relics[index];
                if ((relic.Flags & BlitzRelicFlags.Melted) == 0 && Has(BlitzRegistry.Relic(relic.Id), hook))
                    output[count++] = new BlitzListener(BlitzListenerKind.Relic, (byte)index, BlitzLimits.PlayerOwner);
            }
            for (int slot = 0; slot < battle.PotionSlotCount; slot++)
            {
                ushort potion = battle.Potions[slot];
                if (potion != 0 && Has(BlitzRegistry.Potion((ushort)(potion - 1)), hook))
                    output[count++] = new BlitzListener(BlitzListenerKind.Potion, (byte)slot, BlitzLimits.PlayerOwner);
            }
        }

        // Osty: inserted before the enemy block.
        if ((battle.Flags & BlitzBattleFlags.OstyPresent) != 0)
            nextPower = CollectPowers(battle, BlitzLimits.OstyOwner, hook, nextPower, output, ref count);

        // Enemies in roster order: powers, then the monster itself.
        for (int position = 0; position < battle.RosterCount; position++)
        {
            byte enemyIndex = battle.Roster[position];
            byte owner = BlitzKernel.EnemyOwner(enemyIndex);
            CollectPowers(battle, owner, hook, 0, output, ref count);
            if (Has(BlitzRegistry.Monster(battle.Enemies[enemyIndex].MonsterId), hook))
                output[count++] = new BlitzListener(BlitzListenerKind.Monster, enemyIndex, owner);
        }

        if (!playerHooks)
            return count;

        for (int index = 0; index < battle.OrbCount; index++)
        {
            if (Has(BlitzRegistry.Orb(battle.Orbs[index].Id), hook))
                output[count++] = new BlitzListener(BlitzListenerKind.Orb, (byte)index, BlitzLimits.PlayerOwner);
        }

        // The per-card Has test cannot match when no card/affliction/enchantment definition answers
        // this hook; skip the whole card-zone scan in that case (the cost and threat-projection hooks
        // have no card-side listeners).
        if ((hook.Main & BlitzRegistry.CardHooks) != 0 || (hook.Ext & BlitzRegistry.CardHooksExt) != 0)
        {
            int total = battle.CardCount;
            for (int position = 0; position < total; position++)
            {
                ref readonly BlitzCard card = ref battle.Cards[position];
                if (Has(BlitzRegistry.Card(card.Id), hook))
                    output[count++] = new BlitzListener(BlitzListenerKind.Card, (byte)position, BlitzLimits.PlayerOwner);
                if (card.AfflictionId != 0 && Has(BlitzRegistry.Affliction((ushort)(card.AfflictionId - 1)), hook))
                    output[count++] = new BlitzListener(BlitzListenerKind.Affliction, (byte)position, BlitzLimits.PlayerOwner);
                if (card.EnchantmentId != 0 && Has(BlitzRegistry.Enchantment((ushort)(card.EnchantmentId - 1)), hook))
                    output[count++] = new BlitzListener(BlitzListenerKind.Enchantment, (byte)position, BlitzLimits.PlayerOwner);
            }
        }
        return count;
    }

    public static BlitzListenerDef Definition(in BlitzBattle battle, BlitzListener listener) => listener.Kind switch
    {
        BlitzListenerKind.Power => BlitzRegistry.Power(battle.Powers[listener.Index].Id),
        BlitzListenerKind.Relic => BlitzRegistry.Relic(battle.Relics[listener.Index].Id),
        BlitzListenerKind.Potion => BlitzRegistry.Potion((ushort)(battle.Potions[listener.Index] - 1)),
        BlitzListenerKind.Monster => BlitzRegistry.Monster(battle.Enemies[listener.Index].MonsterId),
        BlitzListenerKind.Orb => BlitzRegistry.Orb(battle.Orbs[listener.Index].Id),
        BlitzListenerKind.Card => BlitzRegistry.Card(battle.Cards[listener.Index].Id),
        BlitzListenerKind.Affliction => BlitzRegistry.Affliction((ushort)(battle.Cards[listener.Index].AfflictionId - 1)),
        BlitzListenerKind.Enchantment => BlitzRegistry.Enchantment((ushort)(battle.Cards[listener.Index].EnchantmentId - 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(listener), listener.Kind, null),
    };

    /// <summary>
    /// Mirrors <c>HookMirrors.ModifyDamage</c>: card enchantment, additive pass, multiplicative pass,
    /// then the minimum cap, clamped at zero. Decimal throughout, never rounded here.
    /// </summary>
    public static decimal ModifyDamage(ref BlitzExec x, byte target, byte dealer, decimal damage, BlitzValueProps props,
        bool hasCardSource, in BlitzCard cardSource)
    {
        // HookMirrors.ModifyDamage: the source card's enchantment runs before every listener.
        if (hasCardSource && cardSource.EnchantmentId != 0)
        {
            BlitzEnchantmentDef enchantment = BlitzRegistry.Enchantment((ushort)(cardSource.EnchantmentId - 1));
            damage += enchantment.EnchantDamageAdditive(cardSource, damage, props);
            damage *= enchantment.EnchantDamageMultiplicative(cardSource, damage, props);
        }
        // Run listeners put out-of-combat deck cards before the combat list; deck cards with
        // damage hooks are rejected at admission.
        BlitzDamageContext context = new(target, dealer, props, hasCardSource, cardSource);
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];

        int count = CollectListeners(x.B, BlitzHookMask.ModifyDamageAdditive, listeners);
        for (int index = 0; index < count; index++)
            damage += Definition(x.B, listeners[index]).ModifyDamageAdditive(ref x, listeners[index], context, damage);

        count = CollectListeners(x.B, BlitzHookMask.ModifyDamageMultiplicative, listeners);
        for (int index = 0; index < count; index++)
            damage *= Definition(x.B, listeners[index]).ModifyDamageMultiplicative(ref x, listeners[index], context, damage);

        decimal cap = decimal.MaxValue;
        count = CollectListeners(x.B, BlitzHookMask.ModifyDamageCap, listeners);
        for (int index = 0; index < count; index++)
            cap = Math.Min(cap, Definition(x.B, listeners[index]).ModifyDamageCap(ref x, listeners[index], context));

        return Math.Max(0, Math.Min(damage, cap));
    }

    /// <summary>
    /// Mirrors <c>Hook.ModifyUnblockedDamageTarget</c>: every combat listener in dispatch order, each
    /// receiving the target the previous listener returned, without the ending gate. Pure query.
    /// </summary>
    public static byte ModifyUnblockedDamageTarget(ref BlitzExec x, byte target, decimal amount,
        BlitzValueProps props, byte dealer)
    {
        if ((BlitzRegistry.AnyHooksExt & BlitzHookMaskExt.ModifyUnblockedDamageTarget) == 0)
            return target;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMaskExt.ModifyUnblockedDamageTarget, listeners);
        for (int index = 0; index < count; index++)
            target = Definition(x.B, listeners[index]).ModifyUnblockedDamageTarget(ref x, listeners[index], target, amount, props, dealer);
        return target;
    }

    /// <summary>
    /// Mirrors one <c>HookMirrors.ModifyHpLost</c> call: the normal then Late pass of the before-Osty
    /// (<paramref name="afterOsty"/> false) or after-Osty phase. Listeners whose call changed the truncated amount
    /// are written to <paramref name="changed"/>.
    /// </summary>
    public static decimal ModifyHpLost(ref BlitzExec x, in BlitzHpLossContext loss, decimal amount, bool afterOsty,
        scoped Span<BlitzListener> changed, out int changedCount)
    {
        changedCount = 0;
        BlitzHookMask normalMask = afterOsty ? BlitzHookMask.ModifyHpLostAfterOsty : BlitzHookMask.ModifyHpLostBeforeOsty;
        BlitzHookMask lateMask = afterOsty ? BlitzHookMask.ModifyHpLostAfterOstyLate : BlitzHookMask.ModifyHpLostBeforeOstyLate;
        if ((BlitzRegistry.AnyHooks & (normalMask | lateMask)) == 0)
            return amount;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        for (int pass = 0; pass < 2; pass++)
        {
            int count = CollectListeners(x.B, pass == 0 ? normalMask : lateMask, listeners);
            for (int index = 0; index < count; index++)
            {
                BlitzListenerDef def = Definition(x.B, listeners[index]);
                decimal previous = amount;
                amount = (afterOsty, pass) switch
                {
                    (false, 0) => def.ModifyHpLostBeforeOsty(ref x, listeners[index], loss, amount),
                    (false, _) => def.ModifyHpLostBeforeOstyLate(ref x, listeners[index], loss, amount),
                    (true, 0) => def.ModifyHpLostAfterOsty(ref x, listeners[index], loss, amount),
                    _ => def.ModifyHpLostAfterOstyLate(ref x, listeners[index], loss, amount),
                };
                if (decimal.Truncate(previous) != decimal.Truncate(amount))
                    changed[changedCount++] = listeners[index];
            }
        }
        return amount;
    }

    /// <summary>
    /// Mirrors <c>HookMirrors.AfterModifyingHpLostAfterOsty</c>: the run listeners in order, each notified when it is
    /// among <paramref name="changed"/>.
    /// </summary>
    public static void AfterModifyingHpLostAfterOsty(ref BlitzExec x, scoped ReadOnlySpan<BlitzListener> changed)
    {
        if (changed.IsEmpty)
            return;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMask.AfterModifyingHpLostAfterOsty, listeners);
        for (int index = 0; index < count; index++)
        {
            if (changed.Contains(listeners[index]))
            {
                Definition(x.B, listeners[index]).AfterModifyingHpLostAfterOsty(ref x, listeners[index]);
                if (x.NeedsChoice)
                    return;
            }
        }
    }

    /// <summary>Mirrors <c>HookMirrors.ModifyBlock</c> including the settlement overflow clamp.</summary>
    public static decimal ModifyBlock(ref BlitzExec x, byte target, decimal block, BlitzValueProps props,
        bool hasCardSource, in BlitzCard cardSource, out bool anyModifier)
        => ModifyBlock(ref x, target, block, props, hasCardSource, cardSource, out anyModifier,
            Span<BlitzListener>.Empty, out _);

    /// <summary>
    /// <c>HookMirrors.ModifyBlock</c> with the listeners whose additive/multiplicative value changed
    /// (the input of <c>AfterModifyingBlockAmount</c>), in listener order.
    /// </summary>
    public static decimal ModifyBlock(ref BlitzExec x, byte target, decimal block, BlitzValueProps props,
        bool hasCardSource, in BlitzCard cardSource, out bool anyModifier,
        scoped Span<BlitzListener> changed, out int changedCount)
    {
        anyModifier = false;
        changedCount = 0;
        bool overflowed = false;
        // HookMirrors.ModifyBlock: the source card's enchantment runs before every listener and is
        // part of the settlement overflow tracking.
        if (hasCardSource && cardSource.EnchantmentId != 0)
        {
            BlitzEnchantmentDef enchantment = BlitzRegistry.Enchantment((ushort)(cardSource.EnchantmentId - 1));
            block += enchantment.EnchantBlockAdditive(cardSource, block);
            block = MultiplyBlockWithinSettlement(block,
                enchantment.EnchantBlockMultiplicative(cardSource, block), ref overflowed);
            anyModifier = true;
        }
        BlitzBlockContext context = new(target, props, hasCardSource, cardSource);
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];

        int count = CollectListeners(x.B, BlitzHookMask.ModifyBlockAdditive, listeners);
        for (int index = 0; index < count; index++)
        {
            decimal additive = Definition(x.B, listeners[index]).ModifyBlockAdditive(ref x, listeners[index], context, block);
            block += additive;
            anyModifier |= additive != 0;
            if (additive != 0 && !changed.IsEmpty)
                changed[changedCount++] = listeners[index];
        }

        count = CollectListeners(x.B, BlitzHookMask.ModifyBlockMultiplicative, listeners);
        for (int index = 0; index < count; index++)
        {
            decimal multiplier = Definition(x.B, listeners[index]).ModifyBlockMultiplicative(ref x, listeners[index], context, block);
            block = MultiplyBlockWithinSettlement(block, multiplier, ref overflowed);
            anyModifier |= multiplier != 1;
            if (multiplier != 1 && !changed.IsEmpty)
                changed[changedCount++] = listeners[index];
        }

        return overflowed ? Math.Clamp(block, 0m, BlitzLimits.ValueClamp) : Math.Max(0, block);
    }

    /// <summary><c>HookMirrors.AfterModifyingBlockAmount</c> for the recorded listeners.</summary>
    public static void AfterModifyingBlockAmount(ref BlitzExec x, byte target, decimal modified,
        BlitzValueProps props, bool hasCardSource, in BlitzCard cardSource,
        scoped ReadOnlySpan<BlitzListener> changed)
    {
        if (changed.IsEmpty)
            return;
        BlitzBlockContext context = new(target, props, hasCardSource, cardSource);
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMaskExt.AfterModifyingBlockAmount, listeners);
        for (int index = 0; index < count; index++)
        {
            if (changed.Contains(listeners[index]))
            {
                Definition(x.B, listeners[index]).AfterModifyingBlockAmount(ref x, listeners[index], modified, context);
                if (x.NeedsChoice)
                    return;
            }
        }
    }

    /// <summary>Mirrors <c>HookMirrors.MultiplyBlockWithinSettlement</c>.</summary>
    private static decimal MultiplyBlockWithinSettlement(decimal block, decimal multiplier, ref bool overflowed)
    {
        if (block == 0m || multiplier == 0m)
            return 0m;
        if (multiplier == decimal.MaxValue)
            overflowed = true;
        if (Math.Abs(multiplier) > 1m && Math.Abs(block) > decimal.MaxValue / Math.Abs(multiplier))
        {
            overflowed = true;
            return (block < 0m) ^ (multiplier < 0m) ? decimal.MinValue : decimal.MaxValue;
        }
        return block * multiplier;
    }

    /// <summary>
    /// Mirrors <c>HookMirrors.ModifyEnergyCostInCombat</c>: the normal pass over every combat
    /// listener, then the late pass over every combat listener, without an ending gate. The caller
    /// truncates and clamps at zero.
    /// </summary>
    public static decimal ModifyEnergyCost(in BlitzBattle battle, BlitzRules rules, BlitzPile pile,
        in BlitzCard card, decimal amount)
    {
        BlitzHookMask masks = BlitzHookMask.TryModifyEnergyCostInCombat | BlitzHookMask.TryModifyEnergyCostInCombatLate;
        // HookMirrors iterates IterateCombatHookListeners, which returns no combat listeners while
        // the combat is over or ending.
        if ((BlitzRegistry.AnyHooks & masks) == 0 || BlitzKernel.IsOverOrEnding(battle))
            return amount;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(battle, BlitzHookMask.TryModifyEnergyCostInCombat, listeners);
        for (int index = 0; index < count; index++)
        {
                amount = Definition(battle, listeners[index]).TryModifyEnergyCostInCombat(battle, rules, listeners[index],
                card, pile, amount);
        }
        count = CollectListeners(battle, BlitzHookMask.TryModifyEnergyCostInCombatLate, listeners);
        for (int index = 0; index < count; index++)
        {
            amount = Definition(battle, listeners[index]).TryModifyEnergyCostInCombatLate(battle, rules, listeners[index],
                card, pile, amount);
        }
        return amount;
    }

    /// <summary>
    /// Mirrors <c>Hook.ModifyOrbValue</c>: every combat listener in order, skipped while the combat is
    /// over/ending (<c>IterateCombatHookListeners</c> yields no listeners then). A pure query.
    /// </summary>
    public static decimal ModifyOrbValue(in BlitzBattle battle, BlitzRules rules, in BlitzOrb orb, decimal value)
    {
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.ModifyOrbValue) == 0 || BlitzKernel.IsOverOrEnding(battle))
            return value;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(battle, BlitzHookMask.ModifyOrbValue, listeners);
        for (int index = 0; index < count; index++)
            value = Definition(battle, listeners[index]).ModifyOrbValue(battle, rules, listeners[index], orb, value);
        return value;
    }

    /// <summary>
    /// <c>HookMirrors.ModifyOrbPassiveTriggerCount</c>: every combat listener in order, recording the
    /// ones that changed the count. The existing engine then calls
    /// <c>AfterModifyingOrbPassiveTriggerCount</c> on those listeners; its only vanilla listener
    /// (GoldPlatedCables) is cosmetic, so the Blitz kernel does not model that pass.
    /// </summary>
    public static int ModifyOrbPassiveTriggerCounts(in BlitzBattle battle, BlitzRules rules, in BlitzOrb orb,
        int orbIndex, int triggerCount)
    {
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.ModifyOrbPassiveTriggerCounts) == 0
            || BlitzKernel.IsOverOrEnding(battle))
        {
            return triggerCount;
        }
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(battle, BlitzHookMask.ModifyOrbPassiveTriggerCounts, listeners);
        for (int index = 0; index < count; index++)
        {
            int modified = Definition(battle, listeners[index]).ModifyOrbPassiveTriggerCounts(
                battle, rules, listeners[index], orb, orbIndex, triggerCount);
            if (modified != triggerCount)
                triggerCount = modified;
        }
        return triggerCount;
    }

    /// <summary><c>HookMirrors.AfterOrbChanneled</c> over a listener snapshot.</summary>
    public static void AfterOrbChanneled(ref BlitzExec x, in BlitzOrb orb)
    {
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.AfterOrbChanneled) == 0 || BlitzKernel.IsOverOrEnding(x.B))
            return;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMask.AfterOrbChanneled, listeners);
        if (count == 0)
            return;
        Span<SnapshotEntry> snapshot = stackalloc SnapshotEntry[count];
        for (int index = 0; index < count; index++)
            snapshot[index] = Snapshot(x.B, listeners[index]);
        for (int index = 0; index < count; index++)
        {
            if (!TryResolve(x.B, snapshot[index], out BlitzListener current))
                continue;
            Definition(x.B, current).AfterOrbChanneled(ref x, current, orb);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary><c>HookMirrors.AfterOrbEvoked</c> over a listener snapshot with the evoke result targets.</summary>
    public static void AfterOrbEvoked(ref BlitzExec x, in BlitzOrb orb, scoped ReadOnlySpan<byte> targets)
    {
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.AfterOrbEvoked) == 0 || BlitzKernel.IsOverOrEnding(x.B))
            return;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMask.AfterOrbEvoked, listeners);
        if (count == 0)
            return;
        Span<SnapshotEntry> snapshot = stackalloc SnapshotEntry[count];
        for (int index = 0; index < count; index++)
            snapshot[index] = Snapshot(x.B, listeners[index]);
        for (int index = 0; index < count; index++)
        {
            if (!TryResolve(x.B, snapshot[index], out BlitzListener current))
                continue;
            Definition(x.B, current).AfterOrbEvoked(ref x, current, orb, targets);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// <c>SimulatedCombatState.AfterPotionUsed</c>'s relic batch: every combat listener carrying the
    /// hook, in listener order, after the potion effect (no ending gate in the existing engine).
    /// </summary>
    public static void AfterPotionUsed(ref BlitzExec x, ushort potionId)
    {
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.AfterPotionUsed) == 0)
            return;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMask.AfterPotionUsed, listeners);
        if (count == 0)
            return;
        Span<SnapshotEntry> snapshot = stackalloc SnapshotEntry[count];
        for (int index = 0; index < count; index++)
            snapshot[index] = Snapshot(x.B, listeners[index]);
        for (int index = 0; index < count; index++)
        {
            if (!TryResolve(x.B, snapshot[index], out BlitzListener current))
                continue;
            Definition(x.B, current).AfterPotionUsed(ref x, current, potionId);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>Mirrors <c>Hook.ModifyXValue</c>: every combat listener in order, skipped while the combat is over/ending.</summary>
    public static int ModifyXValue(in BlitzBattle battle, BlitzRules rules, in BlitzCard card, int value)
    {
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.ModifyXValue) == 0 || BlitzKernel.IsOverOrEnding(battle))
            return value;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(battle, BlitzHookMask.ModifyXValue, listeners);
        for (int index = 0; index < count; index++)
        {
            value = Definition(battle, listeners[index]).ModifyXValue(battle, rules, listeners[index], card, value);
        }
        return value;
    }

    /// <summary>
    /// Mirrors <c>PersistentRelicSupport.ShouldPlayerResetEnergy</c>: every combat listener in order,
    /// no ending gate; the first false keeps the current energy.
    /// </summary>
    public static bool ShouldPlayerResetEnergy(in BlitzBattle battle, BlitzRules rules, byte player)
    {
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.ShouldPlayerResetEnergy) == 0)
            return true;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(battle, BlitzHookMask.ShouldPlayerResetEnergy, listeners);
        for (int index = 0; index < count; index++)
        {
            if (!Definition(battle, listeners[index]).ShouldPlayerResetEnergy(battle, rules, listeners[index], player))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Mirrors <c>HookMirrors.ModifyCardPlayResultLocation</c> plus <c>CardModel.OnPlayWrapper</c>'s direct
    /// after dispatch over the listeners that changed the location. Both use the combat-listener gate.
    /// </summary>
    public static BlitzPile ModifyCardPlayResultLocation(ref BlitzExec x, in BlitzCard card, bool isAutoPlay,
        int energyValue, BlitzPile location, BlitzPilePosition basePosition,
        scoped Span<BlitzListener> modifiers, out int modifierCount, out BlitzPilePosition position)
    {
        modifierCount = 0;
        position = basePosition;
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.ModifyCardPlayResultLocation) == 0 || BlitzKernel.IsOverOrEnding(x.B))
            return location;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMask.ModifyCardPlayResultLocation, listeners);
        for (int index = 0; index < count; index++)
        {
            BlitzPile previous = location;
            BlitzPilePosition previousPosition = position;
            location = Definition(x.B, listeners[index]).ModifyCardPlayResultLocation(ref x, listeners[index], card,
                isAutoPlay, energyValue, location, out position);
            if (location != previous || position != previousPosition)
                modifiers[modifierCount++] = listeners[index];
        }
        return location;
    }

    /// <summary><c>HookMirrors.AfterModifyingCardPlayResultLocation</c>: the recorded modifiers in order.</summary>
    public static void AfterModifyingCardPlayResultLocation(ref BlitzExec x, in BlitzCard card, BlitzPile location,
        scoped ReadOnlySpan<BlitzListener> modifiers)
    {
        for (int index = 0; index < modifiers.Length; index++)
        {
            BlitzListenerDef def = Definition(x.B, modifiers[index]);
            if ((def.Hooks & BlitzHookMask.AfterModifyingCardPlayResultLocation) == 0)
                continue;
            def.AfterModifyingCardPlayResultLocation(ref x, modifiers[index], card, location);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// Mirrors <c>HookMirrors.ModifyCardPlayCount</c>: every combat listener in order; the changed
    /// listeners are recorded for the after pass. Skipped while the combat is over/ending.
    /// </summary>
    public static int ModifyCardPlayCount(ref BlitzExec x, in BlitzCard card, byte target, int playCount,
        scoped Span<BlitzListener> modifiers, out int modifierCount)
    {
        modifierCount = 0;
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.ModifyCardPlayCount) == 0 || BlitzKernel.IsOverOrEnding(x.B))
            return playCount;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMask.ModifyCardPlayCount, listeners);
        for (int index = 0; index < count; index++)
        {
            int previous = playCount;
            playCount = Definition(x.B, listeners[index]).ModifyCardPlayCount(ref x, listeners[index], card, target,
                playCount);
            if (playCount != previous)
                modifiers[modifierCount++] = listeners[index];
        }
        return playCount;
    }

    /// <summary>
    /// <c>HookMirrors.AfterModifyingCardPlayCount</c>: re-reads the listener list and calls the after
    /// hook only on the recorded modifiers. OneTwoPunch and Duplication decrement themselves here.
    /// </summary>
    public static void AfterModifyingCardPlayCount(ref BlitzExec x, in BlitzCard card,
        scoped ReadOnlySpan<BlitzListener> modifiers)
    {
        if (modifiers.IsEmpty || (BlitzRegistry.AnyHooks & BlitzHookMask.AfterModifyingCardPlayCount) == 0
            || BlitzKernel.IsOverOrEnding(x.B))
        {
            return;
        }
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMask.AfterModifyingCardPlayCount, listeners);
        for (int index = 0; index < count; index++)
        {
            if (!Contains(modifiers, listeners[index]))
                continue;
            Definition(x.B, listeners[index]).AfterModifyingCardPlayCount(ref x, listeners[index], card);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// <c>PersistentRelicSupport.ModifyPowerAmountGiven</c>: every combat listener in order adds its
    /// delta to the running amount. A pure query without the ending gate.
    /// </summary>
    public static decimal ModifyPowerAmountGiven(in BlitzBattle battle, BlitzRules rules, ushort powerId,
        byte giver, byte target, decimal amount)
    {
        if ((BlitzRegistry.AnyHooksExt & BlitzHookMaskExt.ModifyPowerAmountGiven) == 0)
            return amount;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(battle, BlitzHookMaskExt.ModifyPowerAmountGiven, listeners);
        for (int index = 0; index < count; index++)
        {
            BlitzListener listener = listeners[index];
            amount += Definition(battle, listener).ModifyPowerAmountGiven(battle, rules, listener, powerId,
                giver, target, amount);
        }
        return amount;
    }

    /// <summary>
    /// <c>Hook.ModifyPowerAmountGivenMultiplicative</c>: a pure query after the additive given pass
    /// (<c>SimulatedCombatState.ModifyPowerAmountForUnsettlingLamp</c> in the existing engine).
    /// </summary>
    public static decimal ModifyPowerAmountGivenMultiplicative(ref BlitzExec x,
        ushort powerId, byte giver, byte target, decimal amount, int cardSourceSerial)
    {
        if ((BlitzRegistry.AnyHooksExt & BlitzHookMaskExt.ModifyPowerAmountGivenMultiplicative) == 0)
            return amount;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMaskExt.ModifyPowerAmountGivenMultiplicative, listeners);
        for (int index = 0; index < count; index++)
        {
            BlitzListener listener = listeners[index];
            amount *= Definition(x.B, listener).ModifyPowerAmountGivenMultiplicative(ref x, listener,
                powerId, giver, target, amount, cardSourceSerial);
        }
        return amount;
    }

    /// <summary>
    /// <c>Hook.BeforePowerAmountChanged</c>: the observation pass
    /// (<c>SimulatedCombatState.ObserveUnsettlingLamp</c>) before the additive given pass.
    /// </summary>
    public static void BeforePowerAmountChanged(ref BlitzExec x, ushort powerId, int amount, byte target,
        byte applier, int cardSourceSerial)
    {
        if ((BlitzRegistry.AnyHooksExt & BlitzHookMaskExt.BeforePowerAmountChanged) == 0)
            return;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMaskExt.BeforePowerAmountChanged, listeners);
        for (int index = 0; index < count; index++)
        {
            BlitzListener listener = listeners[index];
            Definition(x.B, listener).BeforePowerAmountChanged(ref x, listener, powerId, amount, target, applier,
                cardSourceSerial);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary><c>AfterEnergySpent</c> over the combat listeners, in listener order.</summary>
    public static void AfterEnergySpent(ref BlitzExec x, in BlitzCard card, int amount)
    {
        if ((BlitzRegistry.AnyHooksExt & BlitzHookMaskExt.AfterEnergySpent) == 0)
            return;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(x.B, BlitzHookMaskExt.AfterEnergySpent, listeners);
        for (int index = 0; index < count; index++)
        {
            Definition(x.B, listeners[index]).AfterEnergySpent(ref x, listeners[index], card, amount);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.ModifyPowerAmountForRelics</c>'s received pass: a pure query over
    /// every combat listener; the listeners that changed the amount are recorded. Artifact stays in the
    /// kernel after this pass, as the existing engine consumes it after the relic modifiers.
    /// </summary>
    public static int ModifyPowerAmountReceived(in BlitzBattle battle, BlitzRules rules, ushort powerId,
        byte target, int amount, scoped Span<BlitzListener> modifiers, out int modifierCount)
    {
        modifierCount = 0;
        if ((BlitzRegistry.AnyHooks & BlitzHookMask.TryModifyPowerAmountReceived) == 0)
            return amount;
        Span<BlitzListener> listeners = stackalloc BlitzListener[MaxListeners];
        int count = CollectListeners(battle, BlitzHookMask.TryModifyPowerAmountReceived, listeners);
        for (int index = 0; index < count; index++)
        {
            if (!Definition(battle, listeners[index]).TryModifyPowerAmountReceived(battle, rules, listeners[index],
                powerId, target, amount, out int modified))
            {
                continue;
            }
            amount = modified;
            modifiers[modifierCount++] = listeners[index];
        }
        return amount;
    }

    /// <summary><c>Hook.AfterModifyingPowerAmountReceived</c> for the recorded listeners, in order.</summary>
    public static void AfterModifyingPowerAmountReceived(ref BlitzExec x, ushort powerId, byte target,
        scoped ReadOnlySpan<BlitzListener> modifiers)
    {
        for (int index = 0; index < modifiers.Length; index++)
        {
            BlitzListenerDef def = Definition(x.B, modifiers[index]);
            if ((def.Hooks & BlitzHookMask.AfterModifyingPowerAmountReceived) == 0)
                continue;
            def.AfterModifyingPowerAmountReceived(ref x, modifiers[index], powerId, target);
            if (x.NeedsChoice)
                return;
        }
    }

    private static bool Contains(scoped ReadOnlySpan<BlitzListener> listeners, BlitzListener listener)
    {
        for (int index = 0; index < listeners.Length; index++)
        {
            if (listeners[index] == listener)
                return true;
        }
        return false;
    }

    private static int CollectPowers(in BlitzBattle battle, byte owner, HookBits hook, int start,
        Span<BlitzListener> output, ref int count)
    {
        int index = 0;
        for (; index < battle.PowerCount; index++)
        {
            ref readonly BlitzPower power = ref battle.Powers[index];
            if (power.Owner < owner)
                continue;
            if (power.Owner > owner)
                break;
            if (Has(BlitzRegistry.Power(power.Id), hook))
                output[count++] = new BlitzListener(BlitzListenerKind.Power, (byte)index, owner);
        }
        return index;
    }

    private static bool Has(BlitzContentDef def, HookBits hook)
        => (def.Hooks & hook.Main) != 0 || (def.HooksExt & hook.Ext) != 0;
}
