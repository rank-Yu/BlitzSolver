using CombatSolver.Flash.Kernel;
using CombatSolver.Flash.Search.Scoring;

namespace CombatSolver.Flash.Search;

/// <summary>
/// Port of <c>CardChoiceSupport.BuildChoices</c> and its branch budget to Flash choice requests.
/// Options are ordered by the same removal/card value key, combinations are generated in the same
/// recursion order with adjacent equal-state options suppressed, and the retained set is ordered by
/// the same selection priority. Branch limits come from the request profile
/// (<c>MaxPileChoiceBranchesPerAction</c> / <c>MaxHandChoiceBranchesPerAction</c>).
/// </summary>
internal static class FlashChoiceOrderer
{
    private const double SelfClearingRemovalPenalty = 1_000d;
    private const int MaximumOccurrenceReserve = 2;

    private readonly record struct Selection(ulong Mask, double Priority);

    public static int Enumerate(
        in FlashBattle battle, in FlashChoiceRequest request, scoped ReadOnlySpan<FlashCard> cards,
        FlashRules rules, FlashScorer scorer, int maxPileBranches, int maxHandBranches,
        Span<ulong> output)
    {
        if (request.OptionCount == 0)
        {
            if (request.MinCount <= 0 && request.MaxCount >= 0)
            {
                output[0] = 0;
                return 1;
            }
            return 0;
        }
        if (request.MaxCount < request.MinCount)
            return 0;
        int optionCount = request.OptionCount;
        int minTake = Math.Min(request.MinCount, optionCount);
        int maxTake = Math.Min(request.MaxCount, optionCount);
        if (maxTake < minTake)
            return 0;
        if (request.MinCount == request.MaxCount && request.MinCount >= optionCount)
        {
            ulong all = optionCount == 64 ? ulong.MaxValue : (1UL << optionCount) - 1;
            output[0] = all;
            return 1;
        }

        int branchLimit = request.SourceIsNone || request.Source != FlashPile.Hand
            ? maxPileBranches
            : maxHandBranches;
        bool exactSingleCardRouting = request.MaxCount == 1
            && request.Effect is FlashChoiceEffect.MoveToHand or FlashChoiceEffect.MoveToDrawTop
                or FlashChoiceEffect.MoveToHandFreeThisTurn or FlashChoiceEffect.SetFreeThisCombat
                or FlashChoiceEffect.GenerateToHand;
        if (exactSingleCardRouting)
        {
            int skipBranch = minTake == 0 ? 1 : 0;
            branchLimit = Math.Max(branchLimit, optionCount + skipBranch);
        }
        bool diversifyHandDiscard = !request.SourceIsNone && request.Source == FlashPile.Hand
            && request.Effect is FlashChoiceEffect.Discard or FlashChoiceEffect.DiscardAndDraw
            && minTake == maxTake && maxTake > 1;

        // One ordering key per option, built once. The existing builder orders by value and then
        // by the choice-card key's ordinal text.
        int[] order = new int[optionCount];
        string[] keys = new string[optionCount];
        double[] values = new double[optionCount];
        for (int index = 0; index < optionCount; index++)
        {
            FlashCard card = cards[index];
            order[index] = index;
            keys[index] = FlashCardKey.Choice(battle, card, rules, scorer);
            values[index] = request.Effect is FlashChoiceEffect.Discard
                    or FlashChoiceEffect.DiscardAndDraw or FlashChoiceEffect.Exhaust
                    or FlashChoiceEffect.Transform
                ? RemovalPriority(battle, request, card, rules, scorer)
                : scorer.AutomaticChoiceValue(battle, card);
        }
        bool removalOrder = request.Effect is FlashChoiceEffect.Discard
            or FlashChoiceEffect.DiscardAndDraw or FlashChoiceEffect.Exhaust or FlashChoiceEffect.Transform;
        // OrderBy in the existing builder is stable; a comparison sort would reorder equal
        // value/key options and change which combinations the cap keeps.
        for (int index = 1; index < order.Length; index++)
        {
            int current = order[index];
            int prior = index - 1;
            while (prior >= 0)
            {
                int left = order[prior];
                int comparison = removalOrder
                    ? values[left].CompareTo(values[current])
                    : values[current].CompareTo(values[left]);
                if (comparison == 0)
                    comparison = string.CompareOrdinal(keys[left], keys[current]);
                if (comparison <= 0)
                    break;
                order[prior + 1] = order[prior];
                prior--;
            }
            order[prior + 1] = current;
        }

        int[] previousEqualIndex = new int[optionCount];
        bool hasRepeatedOptions = false;
        for (int index = 0; index < optionCount; index++)
        {
            previousEqualIndex[index] = -1;
            for (int prior = index - 1; prior >= 0; prior--)
            {
                if (!string.Equals(keys[order[prior]], keys[order[index]], StringComparison.Ordinal))
                    continue;
                previousEqualIndex[index] = prior;
                hasRepeatedOptions = true;
                break;
            }
        }

        List<Selection> selections = new(optionCount * 2);
        List<Selection> cardinalityRepresentatives = new(maxTake - minTake + 1);
        int[] combination = new int[maxTake];
        int combinationLimit = diversifyHandDiscard
            ? Math.Max(branchLimit, Math.Min(256, checked(branchLimit * 8)))
            : branchLimit;
        for (int take = minTake; take <= maxTake; take++)
        {
            int firstOfSize = selections.Count;
            BuildCombinations(order, previousEqualIndex, take, 0, 0UL, 0, combination,
                selections, firstOfSize, combinationLimit);
            if (selections.Count > firstOfSize)
                cardinalityRepresentatives.Add(selections[firstOfSize]);
        }
        for (int index = 0; index < selections.Count; index++)
        {
            selections[index] = selections[index] with
            {
                Priority = EvaluateChoicePriority(
                    battle, request, cards, rules, scorer, selections[index].Mask),
            };
        }
        for (int index = 0; index < cardinalityRepresentatives.Count; index++)
        {
            ulong mask = cardinalityRepresentatives[index].Mask;
            cardinalityRepresentatives[index] = new Selection(
                mask, EvaluateChoicePriority(battle, request, cards, rules, scorer, mask));
        }

        int effectiveBranchLimit = Math.Max(branchLimit, cardinalityRepresentatives.Count);
        List<Selection> retained;
        if (diversifyHandDiscard)
        {
            retained = BuildHandDiscardRepresentatives(
                battle, request, cards, rules, scorer, selections, effectiveBranchLimit);
        }
        else
        {
            retained = new List<Selection>(cardinalityRepresentatives);
            List<Selection> ranked = selections
                .OrderByDescending(selection => selection.Priority).ToList();
            for (int index = 0; index < ranked.Count && retained.Count < effectiveBranchLimit; index++)
            {
                if (!ContainsMask(retained, ranked[index].Mask))
                    retained.Add(ranked[index]);
            }
        }

        // The existing builder reserves up to two physical-occurrence supplements for identity
        // changing effects with repeated option states. Flash selections address option positions,
        // so these are already represented; only a semantic duplicate dedup can hide one. The
        // reserve is bounded and applied last, exactly as the existing order does.
        if (hasRepeatedOptions && IsIdentityChangingPersistentChoiceEffect(request.Effect))
        {
            ReserveOccurrenceRepresentatives(
                retained, keys, order, effectiveBranchLimit, MaximumOccurrenceReserve);
        }

        List<Selection> ordered = retained
            .OrderByDescending(selection => selection.Priority).ToList();
        int count = Math.Min(ordered.Count, output.Length);
        for (int index = 0; index < count; index++)
            output[index] = ordered[index].Mask;
        return count;
    }

    private static void BuildCombinations(
        int[] order, int[] previousEqualIndex, int count, int start, ulong mask, int depth,
        int[] combination, List<Selection> output, int outputStart, int limit)
    {
        if (output.Count - outputStart >= limit)
            return;
        if (depth == count)
        {
            output.Add(new Selection(mask, 0d));
            return;
        }
        int remaining = count - depth;
        for (int position = start; position <= order.Length - remaining; position++)
        {
            if (previousEqualIndex[position] >= start)
                continue;
            combination[depth] = order[position];
            BuildCombinations(order, previousEqualIndex, count, position + 1,
                mask | (1UL << order[position]), depth + 1, combination, output, outputStart, limit);
            if (output.Count - outputStart >= limit)
                return;
        }
    }

    private static List<Selection> BuildHandDiscardRepresentatives(
        in FlashBattle battle, in FlashChoiceRequest request, scoped ReadOnlySpan<FlashCard> cards,
        FlashRules rules, FlashScorer scorer, List<Selection> selections, int limit)
    {
        List<Selection> ranked = selections
            .OrderByDescending(selection => selection.Priority).ToList();
        List<Selection> retained = new(limit);
        void Add(Selection selection)
        {
            if (retained.Count < limit && !ContainsMask(retained, selection.Mask))
                retained.Add(selection);
        }
        if (ranked.Count > 0)
            Add(ranked[0]);
        Add(BestByDiscardMetric(ranked, battle, request, cards, rules, scorer, DiscardMetric.Sly));
        Add(BestByDiscardMetric(ranked, battle, request, cards, rules, scorer, DiscardMetric.StatusCurse));
        Add(BestByDiscardMetric(ranked, battle, request, cards, rules, scorer, DiscardMetric.Retain));
        Add(BestByDiscardMetric(ranked, battle, request, cards, rules, scorer, DiscardMetric.TotalValue));
        for (int option = 0; option < request.OptionCount; option++)
        {
            ulong bit = 1UL << option;
            for (int index = 0; index < ranked.Count; index++)
            {
                if ((ranked[index].Mask & bit) == 0)
                    continue;
                Add(ranked[index]);
                break;
            }
        }
        for (int index = 0; index < ranked.Count; index++)
            Add(ranked[index]);
        return retained;
    }

    private enum DiscardMetric
    {
        Sly,
        StatusCurse,
        Retain,
        TotalValue,
    }

    private static Selection BestByDiscardMetric(
        List<Selection> ranked, in FlashBattle battle, in FlashChoiceRequest request,
        scoped ReadOnlySpan<FlashCard> cards, FlashRules rules, FlashScorer scorer, DiscardMetric metric)
    {
        Selection best = ranked[0];
        double bestScore = DiscardMetricValue(battle, request, cards, rules, scorer, metric, best.Mask);
        for (int index = 1; index < ranked.Count; index++)
        {
            double value = DiscardMetricValue(
                battle, request, cards, rules, scorer, metric, ranked[index].Mask);
            if (value <= bestScore)
                continue;
            best = ranked[index];
            bestScore = value;
        }
        return best;
    }

    private static double DiscardMetricValue(
        in FlashBattle battle, in FlashChoiceRequest request, scoped ReadOnlySpan<FlashCard> cards,
        FlashRules rules, FlashScorer scorer, DiscardMetric metric, ulong mask)
        => metric switch
        {
            DiscardMetric.Sly => SlyCount(battle, request, cards, mask),
            DiscardMetric.StatusCurse => StatusCurseCount(battle, request, cards, mask, rules),
            DiscardMetric.Retain => RetainCount(battle, request, cards, mask),
            DiscardMetric.TotalValue => TotalValue(battle, request, cards, mask, scorer),
            _ => 0,
        };

    private static bool ContainsMask(List<Selection> selections, ulong mask)
    {
        for (int index = 0; index < selections.Count; index++)
        {
            if (selections[index].Mask == mask)
                return true;
        }
        return false;
    }

    /// <summary>
    /// <c>CardChoiceSupport.ReserveIdentityOccurrenceRepresentatives</c>: when equal-state cards
    /// exist at several positions, one bounded physical-occurrence supplement per decision keeps
    /// the choice result that depends on which copy was selected. The supplement maps each selected
    /// occurrence to the corresponding occurrence counted from the option list's tail.
    /// </summary>
    private static void ReserveOccurrenceRepresentatives(
        List<Selection> retained, string[] keys, int[] order, int branchLimit, int reservedLimit)
    {
        if (reservedLimit <= 0 || branchLimit <= 0 || retained.Count == 0)
            return;
        int maximumRetained = branchLimit + reservedLimit;
        List<ulong> supplements = new(reservedLimit);
        int admitted = 0;
        List<Selection> ranked = retained
            .OrderByDescending(selection => selection.Priority).ToList();
        for (int index = 0; index < ranked.Count && admitted < reservedLimit; index++)
        {
            ulong? tail = BuildTailOccurrenceRepresentative(ranked[index].Mask, keys, order);
            if (tail is not { } representative
                || ContainsMask(retained, representative)
                || supplements.Contains(representative))
            {
                continue;
            }
            supplements.Add(representative);
            admitted++;
        }
        foreach (ulong supplement in supplements)
        {
            if (retained.Count >= maximumRetained)
                break;
            retained.Add(new Selection(supplement, 0d));
        }
    }

    private static ulong? BuildTailOccurrenceRepresentative(
        ulong mask, string[] keys, int[] order)
    {
        Span<int> selected = stackalloc int[order.Length];
        int selectedCount = 0;
        for (int index = 0; index < order.Length; index++)
        {
            if ((mask & (1UL << order[index])) != 0)
                selected[selectedCount++] = order[index];
        }
        if (selectedCount == 0)
            return null;
        ulong representative = 0UL;
        bool changed = false;
        for (int index = 0; index < selectedCount; index++)
        {
            int option = selected[index];
            string key = keys[option];
            int remaining = 0;
            for (int later = index + 1; later < selectedCount; later++)
            {
                if (string.Equals(keys[selected[later]], key, StringComparison.Ordinal))
                    remaining++;
            }
            int mapped = -1;
            for (int candidate = keys.Length - 1; candidate >= 0; candidate--)
            {
                if (!string.Equals(keys[candidate], key, StringComparison.Ordinal))
                    continue;
                if (remaining-- != 0)
                    continue;
                mapped = candidate;
                break;
            }
            if (mapped < 0)
                return null;
            representative |= 1UL << mapped;
            changed |= mapped != option;
        }
        if (!changed || System.Numerics.BitOperations.PopCount(representative) != selectedCount)
            return null;
        return representative;
    }

    private static int SlyCount(
        in FlashBattle battle, in FlashChoiceRequest request, scoped ReadOnlySpan<FlashCard> cards, ulong mask)
    {
        int count = 0;
        for (int option = 0; option < cards.Length; option++)
        {
            if ((mask & (1UL << option)) != 0
                && (cards[option].Flags & FlashCardFlags.SlyThisTurn) != 0)
                count++;
        }
        return count;
    }

    private static int StatusCurseCount(
        in FlashBattle battle, in FlashChoiceRequest request, scoped ReadOnlySpan<FlashCard> cards,
        ulong mask, FlashRules rules)
    {
        int count = 0;
        for (int option = 0; option < cards.Length; option++)
        {
            if ((mask & (1UL << option)) == 0)
                continue;
            FlashCardType type = rules.Card(cards[option].Id, cards[option].Upgrade).Type;
            if (type is FlashCardType.Status or FlashCardType.Curse
                || cards[option].HasKeyword(FlashKeywords.Unplayable))
                count++;
        }
        return count;
    }

    private static int RetainCount(
        in FlashBattle battle, in FlashChoiceRequest request, scoped ReadOnlySpan<FlashCard> cards, ulong mask)
    {
        int count = 0;
        for (int option = 0; option < cards.Length; option++)
        {
            if ((mask & (1UL << option)) != 0
                && (cards[option].Flags & FlashCardFlags.RetainThisTurn) != 0)
                count++;
        }
        return count;
    }

    private static double TotalValue(
        in FlashBattle battle, in FlashChoiceRequest request, scoped ReadOnlySpan<FlashCard> cards,
        ulong mask, FlashScorer scorer)
    {
        double total = 0d;
        for (int option = 0; option < cards.Length; option++)
        {
            if ((mask & (1UL << option)) != 0)
                total += scorer.AutomaticChoiceValue(battle, cards[option]);
        }
        return total;
    }

    private static double EvaluateChoicePriority(
        in FlashBattle battle, in FlashChoiceRequest request, scoped ReadOnlySpan<FlashCard> cards,
        FlashRules rules, FlashScorer scorer, ulong mask)
    {
        double value = 0d;
        int count = 0;
        for (int option = 0; option < cards.Length; option++)
        {
            if ((mask & (1UL << option)) == 0)
                continue;
            count++;
            value += request.Effect is FlashChoiceEffect.Transform or FlashChoiceEffect.Exhaust
                ? RemovalPriority(battle, request, cards[option], rules, scorer)
                : scorer.AutomaticChoiceValue(battle, cards[option]);
        }
        if (request.Effect == FlashChoiceEffect.Discard && request.Source is not FlashPile.Hand)
            return ReorderDiscardPriority(battle, request, cards, rules, scorer, mask);
        return request.Effect switch
        {
            FlashChoiceEffect.Transform => count * 0d - value,
            FlashChoiceEffect.Discard or FlashChoiceEffect.DiscardAndDraw =>
                DiscardTriggerValue(battle, request, cards, rules, scorer, mask) - value,
            FlashChoiceEffect.Exhaust => -value,
            _ => value,
        };
    }

    /// <summary><c>CardChoiceSupport.ReorderDiscardPriority</c>: the source pile's average value is
    /// what a discard from a non-hand pile replaces.</summary>
    private static double ReorderDiscardPriority(
        in FlashBattle battle, in FlashChoiceRequest request, scoped ReadOnlySpan<FlashCard> cards,
        FlashRules rules, FlashScorer scorer, ulong mask)
    {
        double replacement = 0d;
        if (!request.SourceIsNone)
        {
            ReadOnlySpan<FlashCard> source = FlashPiles.ReadCards(battle, request.Source);
            if (source.Length > 0)
            {
                double total = 0d;
                for (int index = 0; index < source.Length; index++)
                    total += scorer.AutomaticChoiceValue(battle, source[index]);
                replacement = total / source.Length;
            }
        }
        double value = 0d;
        for (int option = 0; option < cards.Length; option++)
        {
            if ((mask & (1UL << option)) == 0)
                continue;
            value += replacement - scorer.AutomaticChoiceValue(battle, cards[option])
                + SingleDiscardTriggerValue(battle, cards[option], rules, scorer);
        }
        return value;
    }

    private static double DiscardTriggerValue(
        in FlashBattle battle, in FlashChoiceRequest request, scoped ReadOnlySpan<FlashCard> cards,
        FlashRules rules, FlashScorer scorer, ulong mask)
    {
        double total = 0d;
        for (int option = 0; option < cards.Length; option++)
        {
            if ((mask & (1UL << option)) != 0)
                total += SingleDiscardTriggerValue(battle, cards[option], rules, scorer);
        }
        return total;
    }

    private static double SingleDiscardTriggerValue(
        in FlashBattle battle, in FlashCard card, FlashRules rules, FlashScorer scorer)
    {
        if ((card.Flags & FlashCardFlags.SlyThisTurn) == 0)
            return 0d;
        return scorer.AutomaticChoiceValue(battle, card) * 2d
            + NamedVar(battle, card, rules, scorer, "Energy") * 12d
            + NamedVar(battle, card, rules, scorer, "Stars") * 12d;
    }

    private static double RemovalPriority(
        in FlashBattle battle, in FlashChoiceRequest request, in FlashCard card,
        FlashRules rules, FlashScorer scorer)
    {
        double value = request.Effect is FlashChoiceEffect.Exhaust or FlashChoiceEffect.Transform
            ? BasicRemovalValue(battle, card, rules, scorer)
            : scorer.AutomaticChoiceValue(battle, card);
        if (LeavesOnItsOwn(request, card))
            value += SelfClearingRemovalPenalty;
        return value;
    }

    private static double BasicRemovalValue(
        in FlashBattle battle, in FlashCard card, FlashRules rules, FlashScorer scorer)
    {
        FlashCardStats stats = rules.Card(card.Id, card.Upgrade);
        bool strike = stats.Rarity == FlashCardRarity.Basic
            && (stats.Tags & FlashCardTag.Strike) != 0;
        bool defend = stats.Rarity == FlashCardRarity.Basic
            && (stats.Tags & FlashCardTag.Defend) != 0;
        if (!strike && !defend)
            return scorer.AutomaticChoiceValue(battle, card);
        double variable = NamedVar(battle, card, rules, scorer, strike ? "Damage" : "Block");
        return variable * (strike ? 2d / 3d : 1.2d);
    }

    private static bool LeavesOnItsOwn(in FlashChoiceRequest request, in FlashCard card)
        => request.Source is FlashPile.Hand
            && request.Effect is FlashChoiceEffect.Exhaust or FlashChoiceEffect.Transform
            && card.HasKeyword(FlashKeywords.Ethereal);

    private static double NamedVar(
        in FlashBattle battle, in FlashCard card, FlashRules rules, FlashScorer scorer, string name)
    {
        IReadOnlyList<string> names = FlashRegistry.Card(card.Id).VarNames;
        for (int index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], name, StringComparison.Ordinal))
                return (double)scorer.ChoiceVarDecimal(battle, card, index);
        }
        return 0d;
    }

    internal static bool IsIdentityChangingPersistentChoiceEffect(FlashChoiceEffect effect)
        => effect is FlashChoiceEffect.Exhaust or FlashChoiceEffect.Upgrade
            or FlashChoiceEffect.Transform or FlashChoiceEffect.Duplicate
            or FlashChoiceEffect.Modify or FlashChoiceEffect.Nightmare
            or FlashChoiceEffect.SetFreeThisCombat or FlashChoiceEffect.ApplySly
            or FlashChoiceEffect.ApplyEthereal or FlashChoiceEffect.ApplyRetain;
}
