using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Ai.Combat;

/// <summary>
/// Content equivalence of two <see cref="Card"/> values for the search's option deduplication.
///
/// <para>Blitz has no persistent per-card identity in <see cref="Battle"/>: references to a card
/// object are carried by pile position and, inside one action, by the value plus occurrence
/// (<see cref="Kernel"/>'s selected-card snapshot). The one identity-marked field is
/// <see cref="Card.TransientMark"/> ("one-action identity mark used by BeatDown to follow a card
/// object after an earlier auto-play moves it out of the discard pile"); it is zero at every stable
/// boundary. A card with a nonzero mark is treated as referenced and therefore never equivalent to
/// another card. Detached cards (<see cref="Battle.DetachedCards"/>) and the HistoryCourse slot are
/// values referenced by a power/relic slot, not pile positions, and are never choice or hand options;
/// their reference does not make a pile card an identity.</para>
///
/// <para>Every other field is content: <see cref="Card.Id"/>, upgrade, flags, keywords, base costs,
/// replay count, local cost modifiers, star-cost stack, enchantment, affliction, dynamic-variable
/// deltas, the card-private counter/ledger, and the dampen upgrade record. In particular
/// <see cref="Card.Private"/> stays in the key even when it is a decimal-slot reference: the engine
/// canonicalizes slots by value at every stable boundary, and treating a different slot as different
/// content is conservative (it can only keep two cards apart).</para>
/// </summary>
internal static class CardEquivalence
{
    /// <summary>
    /// True when the two cards have identical content and neither carries a transient identity mark.
    /// This is byte equality minus <see cref="Card.TransientMark"/> (the struct has no padding not
    /// already covered by a field).
    /// </summary>
    public static bool ContentEquals(in Card left, in Card right)
    {
        if (left.TransientMark != 0 || right.TransientMark != 0)
            return false;
        if (left.Id != right.Id
            || left.Upgrade != right.Upgrade
            || left.Flags != right.Flags
            || left.Keywords != right.Keywords
            || left.BaseCost != right.BaseCost
            || left.BaseStarCost != right.BaseStarCost
            || left.ReplayCount != right.ReplayCount
            || left.CostState != right.CostState
            || left.ModifierCount != right.ModifierCount
            || left.TemporaryStarCost != right.TemporaryStarCost
            || left.TemporaryStarCostStack != right.TemporaryStarCostStack
            || left.EnchantmentId != right.EnchantmentId
            || left.AfflictionId != right.AfflictionId
            || left.EnchantmentAmount != right.EnchantmentAmount
            || left.EnchantmentPrivate != right.EnchantmentPrivate
            || left.AfflictionAmount != right.AfflictionAmount
            || left.Var0 != right.Var0
            || left.Var1 != right.Var1
            || left.Private != right.Private
            || left.DampenOriginalUpgrade != right.DampenOriginalUpgrade)
            return false;
        for (int index = 0; index < Limits.CardEnergyCostModifiers; index++)
        {
            ref readonly CostModifier a = ref left.EnergyModifiers[index];
            ref readonly CostModifier b = ref right.EnergyModifiers[index];
            if (a.Amount != b.Amount || a.Type != b.Type || a.Packed != b.Packed)
                return false;
        }
        return true;
    }

    /// <summary>
    /// True when every hand position between the two cards is content-equal to them. Playing either
    /// copy then leaves the same hand sequence, so the two play actions are fully equivalent for this
    /// parent state.
    /// </summary>
    public static bool SameHandRun(in Battle battle, int left, int right)
    {
        int low = Math.Min(left, right);
        int high = Math.Max(left, right);
        Card card = battle.Cards[low];
        for (int index = low + 1; index <= high; index++)
        {
            if (!ContentEquals(in card, in battle.Cards[index]))
                return false;
        }
        return true;
    }
}
