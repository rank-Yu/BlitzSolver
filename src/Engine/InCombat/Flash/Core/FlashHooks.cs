namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Hook dispatch mirroring <c>HookMirrors</c>. Listeners are visited in the existing engine's
/// combat listener order (<c>SimulatedCombatState</c> list construction): for each creature —
/// player, Osty, then enemies in roster order — its powers, then the player's relics and potions or
/// the enemy's monster model; then the player's orbs; then cards in Hand, Draw, Discard, Exhaust and
/// Play order, each followed by its affliction and enchantment.
/// </summary>
[System.Runtime.CompilerServices.SkipLocalsInit]
public static class FlashHooks
{
    private const int MaxListeners = FlashLimits.Powers + FlashLimits.Relics + FlashLimits.PotionSlots
        + FlashLimits.Enemies + FlashLimits.Orbs + FlashLimits.Cards * 3;

    /// <summary>Collects the listeners whose definition overrides <paramref name="hook"/>, in dispatch order.</summary>
    public static int CollectListeners(in FlashBattle battle, FlashHookMask hook, Span<FlashListener> output)
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
        if (hasCardSource && cardSource.EnchantmentId != 0)
            throw new FlashUnsupportedException("附魔的伤害修正尚未实现。");
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

    /// <summary>Mirrors <c>HookMirrors.ModifyBlock</c> including the settlement overflow clamp.</summary>
    public static decimal ModifyBlock(ref FlashExec x, byte target, decimal block, FlashValueProps props,
        bool hasCardSource, in FlashCard cardSource, out bool anyModifier)
    {
        anyModifier = false;
        bool overflowed = false;
        if (hasCardSource && cardSource.EnchantmentId != 0)
            throw new FlashUnsupportedException("附魔的格挡修正尚未实现。");
        FlashBlockContext context = new(target, props, hasCardSource, cardSource);
        Span<FlashListener> listeners = stackalloc FlashListener[MaxListeners];

        int count = CollectListeners(x.B, FlashHookMask.ModifyBlockAdditive, listeners);
        for (int index = 0; index < count; index++)
        {
            decimal additive = Definition(x.B, listeners[index]).ModifyBlockAdditive(ref x, listeners[index], context, block);
            block += additive;
            anyModifier |= additive != 0;
        }

        count = CollectListeners(x.B, FlashHookMask.ModifyBlockMultiplicative, listeners);
        for (int index = 0; index < count; index++)
        {
            decimal multiplier = Definition(x.B, listeners[index]).ModifyBlockMultiplicative(ref x, listeners[index], context, block);
            block = MultiplyBlockWithinSettlement(block, multiplier, ref overflowed);
            anyModifier |= multiplier != 1;
        }

        return overflowed ? Math.Clamp(block, 0m, FlashLimits.ValueClamp) : Math.Max(0, block);
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

    private static int CollectPowers(in FlashBattle battle, byte owner, FlashHookMask hook, int start,
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

    private static bool Has(FlashContentDef def, FlashHookMask hook) => (def.Hooks & hook) != 0;
}
