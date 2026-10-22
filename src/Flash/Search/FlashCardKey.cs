using System.Text;
using CombatSolver.Flash.Kernel;
using CombatSolver.Flash.Search.Scoring;

namespace CombatSolver.Flash.Search;

/// <summary>
/// The Flash projection of the existing search's playable-card state key and choice-card key
/// (<c>SearchEvaluator.BuildPlayableCardKey</c> / <c>CardChoiceSupport.ChoiceCardKey</c>). Both
/// existing keys cover the same card fields; this type includes exactly those fields and excludes
/// Flash-only bookkeeping such as <c>PowerAfflictionChecked</c>, <c>CreatedAfterRoot</c>,
/// <c>IsClone</c> and route identity, so the duplicate-branch partition matches the regular search.
/// </summary>
internal static class FlashCardKey
{
    /// <summary>
    /// The regular search's playable-card key: the fields <c>BuildCardStateFingerprint</c> appends
    /// for a <c>PredictedCard</c>, mapped onto the Flash card value.
    /// </summary>
    public static StateFingerprint Playable(
        in FlashBattle battle, in FlashCard card, FlashRules rules, FlashScorer scorer)
    {
        StateFingerprintBuilder key = new();
        AppendState(ref key, battle, card, rules, scorer);
        return key.Finish();
    }

    /// <summary>Playable key without a scorer; the decimal-var delta is read from the battle table.</summary>
    public static StateFingerprint Playable(in FlashBattle battle, in FlashCard card, FlashRules rules)
    {
        StateFingerprintBuilder key = new();
        AppendState(ref key, battle, card, rules, scorer: null);
        return key.Finish();
    }

    /// <summary>
    /// <c>CardChoiceSupport.ChoiceCardKey</c>'s exact text form, used where the existing choice
    /// builder orders equal-valued options by its ordinal string. The component order and the
    /// suffix from <c>CardCostStateSupport</c> match the regular implementation.
    /// </summary>
    public static string Choice(
        in FlashBattle battle, in FlashCard card, FlashRules rules, FlashScorer scorer)
    {
        FlashCardStats stats = rules.Card(card.Id, card.Upgrade);
        StringBuilder text = new();
        text.Append(FlashRegistry.Card(card.Id).NativeId).Append('+').Append(card.Upgrade)
            .Append("|energy=").Append((card.Flags & FlashCardFlags.CostsX) != 0).Append(':')
            .Append(FlashKernel.EnergyCostWithLocalModifiers(battle, card))
            .Append("|stars=").Append((card.Flags & FlashCardFlags.StarCostsX) != 0).Append(':')
            .Append(CurrentStarCost(card))
            .Append("|replay=").Append(card.ReplayCount)
            .Append("|exhaust=").Append((card.Flags & FlashCardFlags.ExhaustOnNextPlay) != 0)
            .Append("|sly=").Append((card.Flags & FlashCardFlags.SlyThisTurn) != 0)
            .Append("|retain=").Append((card.Flags & FlashCardFlags.RetainThisTurn) != 0)
            .Append("|deck=").Append((card.Flags & FlashCardFlags.DeckCard) != 0)
            .Append("|keywords=");
        AppendKeywordNames(text, card.Keywords);
        text.Append("|vars=");
        AppendVarNames(text, battle, card, stats, rules, scorer);
        text.Append('|');
        AppendEnchantmentText(text, card);
        text.Append('|');
        AppendAfflictionText(text, card);
        text.Append("|baselib=-");
        AppendCostStateText(text, card);
        return text.ToString();
    }

    private static void AppendState(
        ref StateFingerprintBuilder key, in FlashBattle battle, in FlashCard card,
        FlashRules rules, FlashScorer? scorer)
    {
        FlashCardStats stats = rules.Card(card.Id, card.Upgrade);
        key.Add(FlashRegistry.Card(card.Id).NativeId);
        key.Add(card.Upgrade);
        key.Add((int)(card.Flags & (FlashCardFlags.CostsX | FlashCardFlags.ExhaustOnNextPlay
            | FlashCardFlags.SlyThisTurn | FlashCardFlags.RetainThisTurn
            | FlashCardFlags.DeckCard | FlashCardFlags.RemovedFromState)));
        key.Add((int)(card.Keywords & ~FlashKeywords.CreatedAfterRoot));
        key.Add(FlashKernel.EnergyCostWithLocalModifiers(battle, card));
        key.Add(CurrentStarCost(card));
        key.Add(card.BaseCost);
        key.Add(card.BaseStarCost);
        key.Add(card.ReplayCount);
        key.Add(card.EnergyModifierCount);
        for (int index = 0; index < card.EnergyModifierCount; index++)
        {
            FlashCostModifier modifier = card.EnergyModifiers[index];
            key.Add(modifier.Amount);
            key.Add((int)modifier.Type);
            key.Add((int)modifier.Expiration);
            key.Add(modifier.IsReduceOnly);
        }
        AppendStarStack(ref key, card);
        for (int index = 0; index < stats.Vars.Length; index++)
        {
            key.Add(FlashRegistry.Card(card.Id).VarNames[index]);
            key.Add(scorer == null
                ? VarDecimal(battle, card, stats, index)
                : scorer.ChoiceVarDecimal(battle, card, index));
        }
        // The existing key appends saved per-card counters; Flash stores them in Private or in
        // Var0/Var1. Var0/Var1 are covered above, so only definitions that keep extra bits in
        // Private opt in here.
        if (FlashRegistry.Card(card.Id).PlayableKeyUsesPrivate)
            key.Add(card.Private);
        key.Add(card.EnchantmentId);
        key.Add(card.EnchantmentAmount);
        key.Add(card.EnchantmentPrivate);
        key.Add((card.CostState >> 5) & 0x07);
        key.Add(card.AfflictionId);
        key.Add(card.AfflictionAmount);
    }

    private static decimal VarDecimal(
        in FlashBattle battle, in FlashCard card, FlashCardStats stats, int index)
    {
        if ((uint)index >= (uint)stats.Vars.Length)
            return 0m;
        decimal delta = card.Private > 0
            ? FlashDecimalVars.Delta(in battle, in card, index)
            : index switch { 0 => card.Var0, 1 => card.Var1, _ => 0m };
        return stats.Vars[index] + delta;
    }

    /// <summary>Native <c>CardModel.CurrentStarCost</c>, including the negative-base zero override.</summary>
    internal static int CurrentStarCost(in FlashCard card)
    {
        if (!card.HasTemporaryStarCost)
            return card.BaseStarCost;
        if (card.TemporaryStarCost == 0 && card.BaseStarCost < 0)
            return card.BaseStarCost;
        return card.TemporaryStarCost;
    }

    private static void AppendStarStack(ref StateFingerprintBuilder key, in FlashCard card)
    {
        int count = card.TemporaryStarCostCount;
        key.Add(count);
        for (int index = 0; index < count; index++)
        {
            key.Add(card.TemporaryStarCost);
            if (index == count - 1)
            {
                key.Add(card.TemporaryStarCostClearsAtTurnEnd);
                key.Add(card.TemporaryStarCostClearsWhenPlayed);
                continue;
            }
            byte flags = (byte)((card.TemporaryStarCostStack >> (2 + index * 2)) & 0x03);
            key.Add((flags & 1) != 0);
            key.Add((flags & 2) != 0);
        }
    }

    private static void AppendCostStateText(StringBuilder text, in FlashCard card)
    {
        if (card.EnergyModifierCount == 0 && !card.HasTemporaryStarCost)
            return;
        text.Append("|cost-state=").Append(card.BaseCost)
            .Append(':').Append(card.EnergyModifierCount).Append('[');
        for (int index = 0; index < card.EnergyModifierCount; index++)
        {
            FlashCostModifier modifier = card.EnergyModifiers[index];
            text.Append((int)modifier.Amount).Append(':').Append((int)modifier.Type)
                .Append(':').Append((int)modifier.Expiration).Append(':')
                .Append(modifier.IsReduceOnly).Append(',');
        }
        text.Append("]/stars=").Append(card.BaseStarCost).Append(':')
            .Append(card.TemporaryStarCostCount).Append('[');
        int count = card.TemporaryStarCostCount;
        for (int index = 0; index < count; index++)
        {
            text.Append(card.TemporaryStarCost).Append(':');
            if (index == count - 1)
            {
                text.Append(card.TemporaryStarCostClearsAtTurnEnd).Append(':')
                    .Append(card.TemporaryStarCostClearsWhenPlayed);
            }
            else
            {
                byte flags = (byte)((card.TemporaryStarCostStack >> (2 + index * 2)) & 0x03);
                text.Append((flags & 1) != 0).Append(':')
                    .Append((flags & 2) != 0);
            }
            text.Append(',');
        }
        text.Append(']');
    }

    private static void AppendKeywordNames(StringBuilder text, FlashKeywords keywords)
    {
        // Native CardKeyword numeric order, as HashSet<CardKeyword>.Order() uses. CreatedAfterRoot
        // is Flash bookkeeping, not a native keyword, and is not part of the existing key.
        bool first = true;
        Append(FlashKeywords.Exhaust, "Exhaust");
        Append(FlashKeywords.Ethereal, "Ethereal");
        Append(FlashKeywords.Innate, "Innate");
        Append(FlashKeywords.Unplayable, "Unplayable");
        Append(FlashKeywords.Retain, "Retain");
        Append(FlashKeywords.Sly, "Sly");
        Append(FlashKeywords.Eternal, "Eternal");
        return;

        void Append(FlashKeywords bit, string name)
        {
            if ((keywords & bit) == 0)
                return;
            if (!first)
                text.Append(',');
            text.Append(name);
            first = false;
        }
    }

    private static void AppendVarNames(
        StringBuilder text, in FlashBattle battle, in FlashCard card, FlashCardStats stats,
        FlashRules rules, FlashScorer scorer)
    {
        IReadOnlyList<string> names = FlashRegistry.Card(card.Id).VarNames;
        // CardChoiceSupport sorts dynamic variables by native name; the rule table stores them in
        // the native declaration order, which is not necessarily ordinal.
        Span<int> order = stackalloc int[names.Count];
        for (int index = 0; index < names.Count; index++)
            order[index] = index;
        for (int index = 1; index < order.Length; index++)
        {
            int current = order[index];
            int prior = index - 1;
            while (prior >= 0 && string.CompareOrdinal(names[order[prior]], names[current]) > 0)
            {
                order[prior + 1] = order[prior];
                prior--;
            }
            order[prior + 1] = current;
        }
        for (int index = 0; index < order.Length; index++)
        {
            if (index != 0)
                text.Append(';');
            int variable = order[index];
            text.Append(names[variable]).Append('=')
                .Append(scorer.ChoiceVarDecimal(battle, card, variable));
        }
    }

    private static void AppendEnchantmentText(StringBuilder text, in FlashCard card)
    {
        if (card.EnchantmentId == 0)
        {
            text.Append("-:0:0");
            return;
        }
        FlashEnchantmentDef enchantment = FlashRegistry.Enchantment(
            (ushort)(card.EnchantmentId - 1));
        text.Append(enchantment.NativeId)
            .Append(':').Append(card.EnchantmentAmount)
            .Append(':').Append((card.CostState >> 5) & 0x07);
        if (enchantment.PrivateKey == FlashEnchantmentPrivateKey.Bool)
            text.Append(':').Append(card.EnchantmentPrivate != 0);
        else if (enchantment.PrivateKey == FlashEnchantmentPrivateKey.Int)
            text.Append(':').Append(card.EnchantmentPrivate);
    }

    private static void AppendAfflictionText(StringBuilder text, in FlashCard card)
    {
        if (card.AfflictionId != 0)
            text.Append(FlashRegistry.Affliction((ushort)(card.AfflictionId - 1)).NativeId);
        text.Append(':').Append(card.AfflictionAmount);
    }
}
