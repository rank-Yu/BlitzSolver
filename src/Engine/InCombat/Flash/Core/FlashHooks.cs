namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Hook dispatch mirroring <c>HookMirrors</c>. Listeners are visited in the existing engine's
/// combat listener order (<c>SimulatedCombatState</c> list construction): for each creature —
/// player, Osty, then enemies in roster order — its powers, then the player's relics and potions or
/// the enemy's monster model; then the player's orbs; then cards in Hand, Draw, Discard, Exhaust and
/// Play order, each followed by its affliction and enchantment.
/// </summary>
[System.Runtime.CompilerServices.SkipLocalsInit]
public static partial class FlashHooks
{
    private const int MaxListeners = FlashLimits.Powers + FlashLimits.Relics + FlashLimits.PotionSlots
        + FlashLimits.Enemies + FlashLimits.Orbs + FlashLimits.Cards * 3;

    /// <summary>Upper bound of <see cref="CollectListeners"/> output, for callers' stack buffers.</summary>
    public const int MaxListenerCount = MaxListeners;

    /// <summary>Collects the listeners whose definition overrides <paramref name="hook"/>, in dispatch order.</summary>
    public static int CollectListeners(in FlashBattle battle, FlashHookMask hook, Span<FlashListener> output)
        => (FlashRegistry.AnyHooks & hook) == 0 ? 0 : CollectListeners(battle, new HookBits(hook, FlashHookMaskExt.None), output);

    /// <summary>The <see cref="FlashHookMaskExt"/> counterpart of the overload above, in the same dispatch order.</summary>
    public static int CollectListeners(in FlashBattle battle, FlashHookMaskExt hook, Span<FlashListener> output)
        => (FlashRegistry.AnyHooksExt & hook) == 0 ? 0 : CollectListeners(battle, new HookBits(FlashHookMask.None, hook), output);

    private readonly record struct HookBits(FlashHookMask Main, FlashHookMaskExt Ext);

    private static int CollectListeners(in FlashBattle battle, HookBits hook, Span<FlashListener> output)
    {
        int count = 0;
        bool playerHooks = (battle.Flags & FlashBattleFlags.PlayerHooksActive) != 0;
        int nextPower = 0;

        // Player creature.
        nextPower = CollectPowers(battle, FlashLimits.PlayerOwner, hook, nextPower, output, ref count);
        if (playerHooks)
        {
            for (int index = 0; index < battle.RelicCount; index++)
            {
                ref readonly FlashRelic relic = ref battle.Relics[index];
                if ((relic.Flags & FlashRelicFlags.Melted) == 0 && Has(FlashRegistry.Relic(relic.Id), hook))
                    output[count++] = new FlashListener(FlashListenerKind.Relic, (byte)index, FlashLimits.PlayerOwner);
            }
            for (int slot = 0; slot < battle.PotionSlotCount; slot++)
            {
                ushort potion = battle.Potions[slot];
                if (potion != 0 && Has(FlashRegistry.Potion((ushort)(potion - 1)), hook))
                    output[count++] = new FlashListener(FlashListenerKind.Potion, (byte)slot, FlashLimits.PlayerOwner);
            }
        }

        // Osty: inserted before the enemy block.
        if ((battle.Flags & FlashBattleFlags.OstyPresent) != 0)
            nextPower = CollectPowers(battle, FlashLimits.OstyOwner, hook, nextPower, output, ref count);

        // Enemies in roster order: powers, then the monster itself.
        for (int position = 0; position < battle.RosterCount; position++)
        {
            byte enemyIndex = battle.Roster[position];
            byte owner = FlashKernel.EnemyOwner(enemyIndex);
            CollectPowers(battle, owner, hook, 0, output, ref count);
            if (Has(FlashRegistry.Monster(battle.Enemies[enemyIndex].MonsterId), hook))
                output[count++] = new FlashListener(FlashListenerKind.Monster, enemyIndex, owner);
        }

        if (!playerHooks)
            return count;

        for (int index = 0; index < battle.OrbCount; index++)
        {
            if (Has(FlashRegistry.Orb(battle.Orbs[index].Id), hook))
                output[count++] = new FlashListener(FlashListenerKind.Orb, (byte)index, FlashLimits.PlayerOwner);
        }

        // The per-card Has test cannot match when no card/affliction/enchantment definition answers
        // this hook; skip the whole card-zone scan in that case (the cost and threat-projection hooks
        // have no card-side listeners).
        if ((hook.Main & FlashRegistry.CardHooks) != 0 || (hook.Ext & FlashRegistry.CardHooksExt) != 0)
        {
            int total = battle.CardCount;
            for (int position = 0; position < total; position++)
            {
                ref readonly FlashCard card = ref battle.Cards[position];
                if (Has(FlashRegistry.Card(card.Id), hook))
                    output[count++] = new FlashListener(FlashListenerKind.Card, (byte)position, FlashLimits.PlayerOwner);
                if (card.AfflictionId != 0 && Has(FlashRegistry.Affliction((ushort)(card.AfflictionId - 1)), hook))
                    output[count++] = new FlashListener(FlashListenerKind.Affliction, (byte)position, FlashLimits.PlayerOwner);
                if (card.EnchantmentId != 0 && Has(FlashRegistry.Enchantment((ushort)(card.EnchantmentId - 1)), hook))
                    output[count++] = new FlashListener(FlashListenerKind.Enchantment, (byte)position, FlashLimits.PlayerOwner);
            }
        }
        return count;
    }

    public static FlashListenerDef Definition(in FlashBattle battle, FlashListener listener) => listener.Kind switch
    {
        FlashListenerKind.Power => FlashRegistry.Power(battle.Powers[listener.Index].Id),
        FlashListenerKind.Relic => FlashRegistry.Relic(battle.Relics[listener.Index].Id),
        FlashListenerKind.Potion => FlashRegistry.Potion((ushort)(battle.Potions[listener.Index] - 1)),
        FlashListenerKind.Monster => FlashRegistry.Monster(battle.Enemies[listener.Index].MonsterId),
        FlashListenerKind.Orb => FlashRegistry.Orb(battle.Orbs[listener.Index].Id),
        FlashListenerKind.Card => FlashRegistry.Card(battle.Cards[listener.Index].Id),
        FlashListenerKind.Affliction => FlashRegistry.Affliction((ushort)(battle.Cards[listener.Index].AfflictionId - 1)),
        FlashListenerKind.Enchantment => FlashRegistry.Enchantment((ushort)(battle.Cards[listener.Index].EnchantmentId - 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(listener), listener.Kind, null),
    };

    /// <summary>
    /// Mirrors <c>HookMirrors.ModifyDamage</c>: card enchantment, additive pass, multiplicative pass,
    /// then the minimum cap, clamped at zero. Decimal throughout, never rounded here.
    /// </summary>
    public static decimal ModifyDamage(ref FlashExec x, byte target, byte dealer, decimal damage, FlashValueProps props,
        bool hasCardSource, in FlashCard cardSource)
    {
        // HookMirrors.ModifyDamage: the source card's enchantment runs before every listener.
        if (hasCardSource && cardSource.EnchantmentId != 0)
        {
            FlashEnchantmentDef enchantment = FlashRegistry.Enchantment((ushort)(cardSource.EnchantmentId - 1));
            damage += enchantment.EnchantDamageAdditive(cardSource, damage, props);
            damage *= enchantment.EnchantDamageMultiplicative(cardSource, damage, props);
        }
        // Run listeners put out-of-combat deck cards before the combat list; deck cards with
        // damage hooks are rejected at admission.
        FlashDamageContext context = new(target, dealer, props, hasCardSource, cardSource);
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];

        int count = CollectListeners(x.B, FlashHookMask.ModifyDamageAdditive, listeners);
        for (int index = 0; index < count; index++)
            damage += Definition(x.B, listeners[index]).ModifyDamageAdditive(ref x, listeners[index], context, damage);

        count = CollectListeners(x.B, FlashHookMask.ModifyDamageMultiplicative, listeners);
        for (int index = 0; index < count; index++)
            damage *= Definition(x.B, listeners[index]).ModifyDamageMultiplicative(ref x, listeners[index], context, damage);

        decimal cap = decimal.MaxValue;
        count = CollectListeners(x.B, FlashHookMask.ModifyDamageCap, listeners);
        for (int index = 0; index < count; index++)
            cap = Math.Min(cap, Definition(x.B, listeners[index]).ModifyDamageCap(ref x, listeners[index], context));

        return Math.Max(0, Math.Min(damage, cap));
    }

    /// <summary>
    /// Mirrors <c>Hook.ModifyUnblockedDamageTarget</c>: every combat listener in dispatch order, each
    /// receiving the target the previous listener returned, without the ending gate. Pure query.
    /// </summary>
    public static byte ModifyUnblockedDamageTarget(ref FlashExec x, byte target, decimal amount,
        FlashValueProps props, byte dealer)
    {
        if ((FlashRegistry.AnyHooksExt & FlashHookMaskExt.ModifyUnblockedDamageTarget) == 0)
            return target;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMaskExt.ModifyUnblockedDamageTarget, listeners);
        for (int index = 0; index < count; index++)
            target = Definition(x.B, listeners[index]).ModifyUnblockedDamageTarget(ref x, listeners[index], target, amount, props, dealer);
        return target;
    }

    /// <summary>
    /// Mirrors one <c>HookMirrors.ModifyHpLost</c> call: the normal then Late pass of the before-Osty
    /// (<paramref name="afterOsty"/> false) or after-Osty phase. Listeners whose call changed the truncated amount
    /// are written to <paramref name="changed"/>.
    /// </summary>
    public static decimal ModifyHpLost(ref FlashExec x, in FlashHpLossContext loss, decimal amount, bool afterOsty,
        scoped Span<FlashListener> changed, out int changedCount)
    {
        changedCount = 0;
        FlashHookMask normalMask = afterOsty ? FlashHookMask.ModifyHpLostAfterOsty : FlashHookMask.ModifyHpLostBeforeOsty;
        FlashHookMask lateMask = afterOsty ? FlashHookMask.ModifyHpLostAfterOstyLate : FlashHookMask.ModifyHpLostBeforeOstyLate;
        if ((FlashRegistry.AnyHooks & (normalMask | lateMask)) == 0)
            return amount;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        for (int pass = 0; pass < 2; pass++)
        {
            int count = CollectListeners(x.B, pass == 0 ? normalMask : lateMask, listeners);
            for (int index = 0; index < count; index++)
            {
                FlashListenerDef def = Definition(x.B, listeners[index]);
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
    public static void AfterModifyingHpLostAfterOsty(ref FlashExec x, scoped ReadOnlySpan<FlashListener> changed)
    {
        if (changed.IsEmpty)
            return;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMask.AfterModifyingHpLostAfterOsty, listeners);
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
    public static decimal ModifyBlock(ref FlashExec x, byte target, decimal block, FlashValueProps props,
        bool hasCardSource, in FlashCard cardSource, out bool anyModifier)
        => ModifyBlock(ref x, target, block, props, hasCardSource, cardSource, out anyModifier,
            Span<FlashListener>.Empty, out _);

    /// <summary>
    /// <c>HookMirrors.ModifyBlock</c> with the listeners whose additive/multiplicative value changed
    /// (the input of <c>AfterModifyingBlockAmount</c>), in listener order.
    /// </summary>
    public static decimal ModifyBlock(ref FlashExec x, byte target, decimal block, FlashValueProps props,
        bool hasCardSource, in FlashCard cardSource, out bool anyModifier,
        scoped Span<FlashListener> changed, out int changedCount)
    {
        anyModifier = false;
        changedCount = 0;
        bool overflowed = false;
        // HookMirrors.ModifyBlock: the source card's enchantment runs before every listener and is
        // part of the settlement overflow tracking.
        if (hasCardSource && cardSource.EnchantmentId != 0)
        {
            FlashEnchantmentDef enchantment = FlashRegistry.Enchantment((ushort)(cardSource.EnchantmentId - 1));
            block += enchantment.EnchantBlockAdditive(cardSource, block);
            block = MultiplyBlockWithinSettlement(block,
                enchantment.EnchantBlockMultiplicative(cardSource, block), ref overflowed);
            anyModifier = true;
        }
        FlashBlockContext context = new(target, props, hasCardSource, cardSource);
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];

        int count = CollectListeners(x.B, FlashHookMask.ModifyBlockAdditive, listeners);
        for (int index = 0; index < count; index++)
        {
            decimal additive = Definition(x.B, listeners[index]).ModifyBlockAdditive(ref x, listeners[index], context, block);
            block += additive;
            anyModifier |= additive != 0;
            if (additive != 0 && !changed.IsEmpty)
                changed[changedCount++] = listeners[index];
        }

        count = CollectListeners(x.B, FlashHookMask.ModifyBlockMultiplicative, listeners);
        for (int index = 0; index < count; index++)
        {
            decimal multiplier = Definition(x.B, listeners[index]).ModifyBlockMultiplicative(ref x, listeners[index], context, block);
            block = MultiplyBlockWithinSettlement(block, multiplier, ref overflowed);
            anyModifier |= multiplier != 1;
            if (multiplier != 1 && !changed.IsEmpty)
                changed[changedCount++] = listeners[index];
        }

        return overflowed ? Math.Clamp(block, 0m, FlashLimits.ValueClamp) : Math.Max(0, block);
    }

    /// <summary><c>HookMirrors.AfterModifyingBlockAmount</c> for the recorded listeners.</summary>
    public static void AfterModifyingBlockAmount(ref FlashExec x, byte target, decimal modified,
        FlashValueProps props, bool hasCardSource, in FlashCard cardSource,
        scoped ReadOnlySpan<FlashListener> changed)
    {
        if (changed.IsEmpty)
            return;
        FlashBlockContext context = new(target, props, hasCardSource, cardSource);
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMaskExt.AfterModifyingBlockAmount, listeners);
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
    public static decimal ModifyEnergyCost(in FlashBattle battle, FlashRules rules, FlashPile pile,
        in FlashCard card, decimal amount)
    {
        FlashHookMask masks = FlashHookMask.TryModifyEnergyCostInCombat | FlashHookMask.TryModifyEnergyCostInCombatLate;
        // HookMirrors iterates IterateCombatHookListeners, which returns no combat listeners while
        // the combat is over or ending.
        if ((FlashRegistry.AnyHooks & masks) == 0 || FlashKernel.IsOverOrEnding(battle))
            return amount;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(battle, FlashHookMask.TryModifyEnergyCostInCombat, listeners);
        for (int index = 0; index < count; index++)
        {
                amount = Definition(battle, listeners[index]).TryModifyEnergyCostInCombat(battle, rules, listeners[index],
                card, pile, amount);
        }
        count = CollectListeners(battle, FlashHookMask.TryModifyEnergyCostInCombatLate, listeners);
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
    public static decimal ModifyOrbValue(in FlashBattle battle, FlashRules rules, in FlashOrb orb, decimal value)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.ModifyOrbValue) == 0 || FlashKernel.IsOverOrEnding(battle))
            return value;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(battle, FlashHookMask.ModifyOrbValue, listeners);
        for (int index = 0; index < count; index++)
            value = Definition(battle, listeners[index]).ModifyOrbValue(battle, rules, listeners[index], orb, value);
        return value;
    }

    /// <summary>
    /// <c>HookMirrors.ModifyOrbPassiveTriggerCount</c>: every combat listener in order, recording the
    /// ones that changed the count. The existing engine then calls
    /// <c>AfterModifyingOrbPassiveTriggerCount</c> on those listeners; its only vanilla listener
    /// (GoldPlatedCables) is cosmetic, so the Flash kernel does not model that pass.
    /// </summary>
    public static int ModifyOrbPassiveTriggerCounts(in FlashBattle battle, FlashRules rules, in FlashOrb orb,
        int orbIndex, int triggerCount)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.ModifyOrbPassiveTriggerCounts) == 0
            || FlashKernel.IsOverOrEnding(battle))
        {
            return triggerCount;
        }
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(battle, FlashHookMask.ModifyOrbPassiveTriggerCounts, listeners);
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
    public static void AfterOrbChanneled(ref FlashExec x, in FlashOrb orb)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.AfterOrbChanneled) == 0 || FlashKernel.IsOverOrEnding(x.B))
            return;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMask.AfterOrbChanneled, listeners);
        if (count == 0)
            return;
        Span<SnapshotEntry> snapshot = stackalloc SnapshotEntry[count];
        for (int index = 0; index < count; index++)
            snapshot[index] = Snapshot(x.B, listeners[index]);
        for (int index = 0; index < count; index++)
        {
            if (!TryResolve(x.B, snapshot[index], out FlashListener current))
                continue;
            Definition(x.B, current).AfterOrbChanneled(ref x, current, orb);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary><c>HookMirrors.AfterOrbEvoked</c> over a listener snapshot with the evoke result targets.</summary>
    public static void AfterOrbEvoked(ref FlashExec x, in FlashOrb orb, scoped ReadOnlySpan<byte> targets)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.AfterOrbEvoked) == 0 || FlashKernel.IsOverOrEnding(x.B))
            return;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMask.AfterOrbEvoked, listeners);
        if (count == 0)
            return;
        Span<SnapshotEntry> snapshot = stackalloc SnapshotEntry[count];
        for (int index = 0; index < count; index++)
            snapshot[index] = Snapshot(x.B, listeners[index]);
        for (int index = 0; index < count; index++)
        {
            if (!TryResolve(x.B, snapshot[index], out FlashListener current))
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
    public static void AfterPotionUsed(ref FlashExec x, ushort potionId)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.AfterPotionUsed) == 0)
            return;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMask.AfterPotionUsed, listeners);
        if (count == 0)
            return;
        Span<SnapshotEntry> snapshot = stackalloc SnapshotEntry[count];
        for (int index = 0; index < count; index++)
            snapshot[index] = Snapshot(x.B, listeners[index]);
        for (int index = 0; index < count; index++)
        {
            if (!TryResolve(x.B, snapshot[index], out FlashListener current))
                continue;
            Definition(x.B, current).AfterPotionUsed(ref x, current, potionId);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>Mirrors <c>Hook.ModifyXValue</c>: every combat listener in order, skipped while the combat is over/ending.</summary>
    public static int ModifyXValue(in FlashBattle battle, FlashRules rules, in FlashCard card, int value)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.ModifyXValue) == 0 || FlashKernel.IsOverOrEnding(battle))
            return value;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(battle, FlashHookMask.ModifyXValue, listeners);
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
    public static bool ShouldPlayerResetEnergy(in FlashBattle battle, FlashRules rules, byte player)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.ShouldPlayerResetEnergy) == 0)
            return true;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(battle, FlashHookMask.ShouldPlayerResetEnergy, listeners);
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
    public static FlashPile ModifyCardPlayResultLocation(ref FlashExec x, in FlashCard card, bool isAutoPlay,
        int energyValue, FlashPile location, FlashPilePosition basePosition,
        scoped Span<FlashListener> modifiers, out int modifierCount, out FlashPilePosition position)
    {
        modifierCount = 0;
        position = basePosition;
        if ((FlashRegistry.AnyHooks & FlashHookMask.ModifyCardPlayResultLocation) == 0 || FlashKernel.IsOverOrEnding(x.B))
            return location;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMask.ModifyCardPlayResultLocation, listeners);
        for (int index = 0; index < count; index++)
        {
            FlashPile previous = location;
            FlashPilePosition previousPosition = position;
            location = Definition(x.B, listeners[index]).ModifyCardPlayResultLocation(ref x, listeners[index], card,
                isAutoPlay, energyValue, location, out position);
            if (location != previous || position != previousPosition)
                modifiers[modifierCount++] = listeners[index];
        }
        return location;
    }

    /// <summary><c>HookMirrors.AfterModifyingCardPlayResultLocation</c>: the recorded modifiers in order.</summary>
    public static void AfterModifyingCardPlayResultLocation(ref FlashExec x, in FlashCard card, FlashPile location,
        scoped ReadOnlySpan<FlashListener> modifiers)
    {
        for (int index = 0; index < modifiers.Length; index++)
        {
            FlashListenerDef def = Definition(x.B, modifiers[index]);
            if ((def.Hooks & FlashHookMask.AfterModifyingCardPlayResultLocation) == 0)
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
    public static int ModifyCardPlayCount(ref FlashExec x, in FlashCard card, byte target, int playCount,
        scoped Span<FlashListener> modifiers, out int modifierCount)
    {
        modifierCount = 0;
        if ((FlashRegistry.AnyHooks & FlashHookMask.ModifyCardPlayCount) == 0 || FlashKernel.IsOverOrEnding(x.B))
            return playCount;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMask.ModifyCardPlayCount, listeners);
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
    public static void AfterModifyingCardPlayCount(ref FlashExec x, in FlashCard card,
        scoped ReadOnlySpan<FlashListener> modifiers)
    {
        if (modifiers.IsEmpty || (FlashRegistry.AnyHooks & FlashHookMask.AfterModifyingCardPlayCount) == 0
            || FlashKernel.IsOverOrEnding(x.B))
        {
            return;
        }
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMask.AfterModifyingCardPlayCount, listeners);
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
    public static decimal ModifyPowerAmountGiven(in FlashBattle battle, FlashRules rules, ushort powerId,
        byte giver, byte target, decimal amount)
    {
        if ((FlashRegistry.AnyHooksExt & FlashHookMaskExt.ModifyPowerAmountGiven) == 0)
            return amount;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(battle, FlashHookMaskExt.ModifyPowerAmountGiven, listeners);
        for (int index = 0; index < count; index++)
        {
            FlashListener listener = listeners[index];
            amount += Definition(battle, listener).ModifyPowerAmountGiven(battle, rules, listener, powerId,
                giver, target, amount);
        }
        return amount;
    }

    /// <summary>
    /// <c>Hook.ModifyPowerAmountGivenMultiplicative</c>: a pure query after the additive given pass
    /// (<c>SimulatedCombatState.ModifyPowerAmountForUnsettlingLamp</c> in the existing engine).
    /// </summary>
    public static decimal ModifyPowerAmountGivenMultiplicative(ref FlashExec x,
        ushort powerId, byte giver, byte target, decimal amount, int cardSourceSerial)
    {
        if ((FlashRegistry.AnyHooksExt & FlashHookMaskExt.ModifyPowerAmountGivenMultiplicative) == 0)
            return amount;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMaskExt.ModifyPowerAmountGivenMultiplicative, listeners);
        for (int index = 0; index < count; index++)
        {
            FlashListener listener = listeners[index];
            amount *= Definition(x.B, listener).ModifyPowerAmountGivenMultiplicative(ref x, listener,
                powerId, giver, target, amount, cardSourceSerial);
        }
        return amount;
    }

    /// <summary>
    /// <c>Hook.BeforePowerAmountChanged</c>: the observation pass
    /// (<c>SimulatedCombatState.ObserveUnsettlingLamp</c>) before the additive given pass.
    /// </summary>
    public static void BeforePowerAmountChanged(ref FlashExec x, ushort powerId, int amount, byte target,
        byte applier, int cardSourceSerial)
    {
        if ((FlashRegistry.AnyHooksExt & FlashHookMaskExt.BeforePowerAmountChanged) == 0)
            return;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMaskExt.BeforePowerAmountChanged, listeners);
        for (int index = 0; index < count; index++)
        {
            FlashListener listener = listeners[index];
            Definition(x.B, listener).BeforePowerAmountChanged(ref x, listener, powerId, amount, target, applier,
                cardSourceSerial);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary><c>AfterEnergySpent</c> over the combat listeners, in listener order.</summary>
    public static void AfterEnergySpent(ref FlashExec x, in FlashCard card, int amount)
    {
        if ((FlashRegistry.AnyHooksExt & FlashHookMaskExt.AfterEnergySpent) == 0)
            return;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(x.B, FlashHookMaskExt.AfterEnergySpent, listeners);
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
    public static int ModifyPowerAmountReceived(in FlashBattle battle, FlashRules rules, ushort powerId,
        byte target, int amount, scoped Span<FlashListener> modifiers, out int modifierCount)
    {
        modifierCount = 0;
        if ((FlashRegistry.AnyHooks & FlashHookMask.TryModifyPowerAmountReceived) == 0)
            return amount;
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];
        int count = CollectListeners(battle, FlashHookMask.TryModifyPowerAmountReceived, listeners);
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
    public static void AfterModifyingPowerAmountReceived(ref FlashExec x, ushort powerId, byte target,
        scoped ReadOnlySpan<FlashListener> modifiers)
    {
        for (int index = 0; index < modifiers.Length; index++)
        {
            FlashListenerDef def = Definition(x.B, modifiers[index]);
            if ((def.Hooks & FlashHookMask.AfterModifyingPowerAmountReceived) == 0)
                continue;
            def.AfterModifyingPowerAmountReceived(ref x, modifiers[index], powerId, target);
            if (x.NeedsChoice)
                return;
        }
    }

    private static bool Contains(scoped ReadOnlySpan<FlashListener> listeners, FlashListener listener)
    {
        for (int index = 0; index < listeners.Length; index++)
        {
            if (listeners[index] == listener)
                return true;
        }
        return false;
    }

    private static int CollectPowers(in FlashBattle battle, byte owner, HookBits hook, int start,
        Span<FlashListener> output, ref int count)
    {
        int index = 0;
        for (; index < battle.PowerCount; index++)
        {
            ref readonly FlashPower power = ref battle.Powers[index];
            if (power.Owner < owner)
                continue;
            if (power.Owner > owner)
                break;
            if (Has(FlashRegistry.Power(power.Id), hook))
                output[count++] = new FlashListener(FlashListenerKind.Power, (byte)index, owner);
        }
        return index;
    }

    private static bool Has(FlashContentDef def, HookBits hook)
        => (def.Hooks & hook.Main) != 0 || (def.HooksExt & hook.Ext) != 0;
}
