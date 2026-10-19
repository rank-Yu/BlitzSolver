using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Exact decimal card-variable deltas. Only a definition that opts in through
/// <see cref="FlashCardDef.UsesDecimalDamageVar"/> (Thrash) stores index 0 here; every other card
/// keeps its integer <see cref="FlashCard.Var0"/>/<see cref="FlashCard.Var1"/> deltas.
/// </summary>
/// <remarks>
/// The existing engine puts the decimal <c>ModifyDamage</c> result into
/// <c>CardModel.DynamicVars.Damage.BaseValue</c> and <c>Thrash.ExtraDamage</c>, then reads
/// <c>BaseValue</c> in the attack command and in the card-value estimate. Flash stores the exact
/// delta (base value minus the rule table's canonical value) as a <see cref="decimal"/>; the card's
/// <c>Private</c> field holds slot + 1. Slots are a value set, not owners: two card objects may
/// share a slot while their values are equal, and an update always moves the card to a slot whose
/// value matches. <see cref="TryCanonicalize"/> rebuilds the table in detached-card then card
/// storage order, so the byte image is independent of allocation history and matches the bridge's
/// projection of the existing engine.
/// </remarks>
public static class FlashDecimalVars
{
    /// <summary>Reads the card's decimal delta when it currently references a slot.</summary>
    public static bool TryGet(in FlashBattle battle, in FlashCard card, out decimal value)
    {
        int slot = card.Private - 1;
        if (card.Private <= 0)
        {
            value = 0m;
            return false;
        }
        if ((uint)slot >= battle.DecimalVarCount)
        {
            throw new InvalidOperationException(
                $"卡牌 {card.Id} 的小数变量槽位 {slot} 超出已用范围 {battle.DecimalVarCount}。");
        }
        value = battle.DecimalVars[slot];
        return true;
    }

    /// <summary>Delta of variable <paramref name="index"/>: the decimal table or the integer slot.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal Delta(in FlashBattle battle, in FlashCard card, int index)
    {
        if (index == 0 && card.Private > 0 && FlashRegistry.Card(card.Id).UsesDecimalDamageVar)
        {
            TryGet(in battle, in card, out decimal value);
            return value;
        }
        return index switch
        {
            0 => card.Var0,
            1 => card.Var1,
            _ => 0m,
        };
    }

    /// <summary>True when the card's definition stores variable 0 in the decimal table.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTracked(in FlashCard card)
        => card.Id != 0 && FlashRegistry.Card(card.Id).UsesDecimalDamageVar;

    /// <summary>
    /// Importer allocation: appends or reuses an exact value without canonicalizing. The caller runs
    /// <see cref="TryCanonicalize"/> after every card has been appended. Returns false when eight
    /// distinct values are already live.
    /// </summary>
    public static bool TryAllocate(ref FlashBattle battle, decimal value, out short encoded)
    {
        encoded = 0;
        if (value == 0m)
            return true;
        int count = battle.DecimalVarCount;
        if ((uint)count > FlashLimits.DecimalCardVars)
            throw new InvalidOperationException($"小数变量表已用 {count} 项，超过容量。");
        for (int slot = 0; slot < count; slot++)
        {
            if (SameBits(battle.DecimalVars[slot], value))
            {
                encoded = checked((short)(slot + 1));
                return true;
            }
        }
        if (count >= FlashLimits.DecimalCardVars)
            return false;
        battle.DecimalVars[count] = value;
        battle.DecimalVarCount = checked((byte)(count + 1));
        encoded = checked((short)(count + 1));
        return true;
    }

    /// <summary>Kernel update: points <paramref name="card"/> at <paramref name="value"/> and canonicalizes.</summary>
    public static void Assign(ref FlashBattle battle, ref FlashCard card, decimal value)
    {
        Canonicalize(ref battle);
        if (value == 0m)
        {
            card.Private = 0;
            Canonicalize(ref battle);
            return;
        }
        int count = battle.DecimalVarCount;
        int slot = FindExact(Values(ref battle)[..count], value);
        if (slot < 0)
        {
            if (count >= FlashLimits.DecimalCardVars)
                throw new FlashCapacityException("Card.DecimalVars", FlashLimits.DecimalCardVars);
            slot = count;
            battle.DecimalVars[slot] = value;
            battle.DecimalVarCount = checked((byte)(count + 1));
        }
        card.Private = checked((short)(slot + 1));
        Canonicalize(ref battle);
    }

    /// <summary>
    /// Rebuilds the table in detached-card then card storage order. Duplicate values share a slot;
    /// orphaned values disappear. Returns false with <paramref name="required"/> set when more than
    /// <see cref="FlashLimits.DecimalCardVars"/> distinct live values exist. The battle is not
    /// modified on failure.
    /// </summary>
    public static bool TryCanonicalize(ref FlashBattle battle, out int required)
    {
        required = 0;
        int oldCount = battle.DecimalVarCount;
        if ((uint)oldCount > FlashLimits.DecimalCardVars)
            throw new InvalidOperationException($"小数变量表已用 {oldCount} 项，超过容量。");
        Span<decimal> oldValues = stackalloc decimal[FlashLimits.DecimalCardVars];
        ReadValues(in battle)[..oldCount].CopyTo(oldValues);
        Span<decimal> next = stackalloc decimal[FlashLimits.DecimalCardVars];
        Span<byte> mapped = stackalloc byte[FlashLimits.DecimalCardVars];
        next.Clear();
        mapped.Clear();
        int nextCount = 0;
        for (int slot = 0; slot < FlashLimits.DetachedCards; slot++)
        {
            if (battle.DetachedCards[slot].Id == 0)
                continue;
            if (!MapSlot(in battle.DetachedCards[slot], oldValues[..oldCount], next, ref nextCount, mapped,
                    out required))
                return false;
        }
        for (int position = 0; position < battle.CardCount; position++)
        {
            if (!MapSlot(in battle.Cards[position], oldValues[..oldCount], next, ref nextCount, mapped,
                    out required))
                return false;
        }
        for (int slot = 0; slot < FlashLimits.DetachedCards; slot++)
        {
            if (battle.DetachedCards[slot].Id != 0)
                ApplySlot(ref battle.DetachedCards[slot], mapped);
        }
        for (int position = 0; position < battle.CardCount; position++)
            ApplySlot(ref battle.Cards[position], mapped);
        Span<decimal> table = Values(ref battle);
        for (int slot = 0; slot < FlashLimits.DecimalCardVars; slot++)
            table[slot] = slot < nextCount ? next[slot] : 0m;
        battle.DecimalVarCount = checked((byte)nextCount);
        return true;
    }

    /// <summary>Canonicalizes or reports a capacity boundary.</summary>
    public static void Canonicalize(ref FlashBattle battle)
    {
        if (!TryCanonicalize(ref battle, out _))
            throw new FlashCapacityException("Card.DecimalVars", FlashLimits.DecimalCardVars);
    }

    private static bool MapSlot(in FlashCard card, ReadOnlySpan<decimal> oldValues, Span<decimal> next,
        ref int nextCount, Span<byte> mapped, out int required)
    {
        required = 0;
        if (card.Private <= 0 || !IsTracked(in card))
            return true;
        int oldSlot = card.Private - 1;
        if ((uint)oldSlot >= (uint)oldValues.Length)
            throw new InvalidOperationException($"卡牌 {card.Id} 的小数变量槽位 {oldSlot} 超出旧表范围。");
        decimal value = oldValues[oldSlot];
        if (value == 0m)
            return true;
        int encoded = mapped[oldSlot];
        if (encoded == 0)
        {
            int slot = FindExact(next[..nextCount], value);
            if (slot < 0)
            {
                if (nextCount >= FlashLimits.DecimalCardVars)
                {
                    required = nextCount + 1;
                    return false;
                }
                slot = nextCount;
                next[nextCount++] = value;
            }
            encoded = slot + 1;
            mapped[oldSlot] = checked((byte)encoded);
        }
        return true;
    }

    private static void ApplySlot(ref FlashCard card, ReadOnlySpan<byte> mapped)
    {
        if (card.Private <= 0 || !IsTracked(in card))
            return;
        int oldSlot = card.Private - 1;
        if ((uint)oldSlot >= (uint)mapped.Length)
            throw new InvalidOperationException($"卡牌 {card.Id} 的小数变量槽位 {oldSlot} 超出映射范围。");
        card.Private = mapped[oldSlot];
    }

    private static int FindExact(ReadOnlySpan<decimal> values, in decimal value)
    {
        for (int slot = 0; slot < values.Length; slot++)
        {
            if (SameBits(values[slot], value))
                return slot;
        }
        return -1;
    }

    private static Span<decimal> Values(ref FlashBattle battle)
        => MemoryMarshal.CreateSpan(ref battle.DecimalVars[0], FlashLimits.DecimalCardVars);

    private static ReadOnlySpan<decimal> ReadValues(in FlashBattle battle)
        => MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in battle.DecimalVars[0]),
            FlashLimits.DecimalCardVars);

    private static bool SameBits(in decimal left, in decimal right)
    {
        ref readonly DecimalBits leftBits = ref Unsafe.As<decimal, DecimalBits>(ref Unsafe.AsRef(in left));
        ref readonly DecimalBits rightBits = ref Unsafe.As<decimal, DecimalBits>(ref Unsafe.AsRef(in right));
        return leftBits.Flags == rightBits.Flags && leftBits.Hi == rightBits.Hi && leftBits.Lo == rightBits.Lo;
    }

    // System.Decimal layout: int flags, uint hi32, ulong lo64.
    [StructLayout(LayoutKind.Sequential)]
    private struct DecimalBits
    {
        public int Flags;
        public uint Hi;
        public ulong Lo;
    }
}
