using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Colorless;

[BlitzContent("BEAT_DOWN")]
public sealed class BeatDown : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        int count = (int)BlitzCardEffects.Var(x, 0);
        if (count <= 0)
            return;
        int pileCount = x.B.DiscardCount;
        Span<BlitzCard> candidates = stackalloc BlitzCard[pileCount];
        Span<int> origins = stackalloc int[pileCount];
        int candidateCount = 0;
        for (int index = 0; index < pileCount; index++)
        {
            BlitzCard card = x.B.Cards[x.B.HandCount + x.B.DrawCount + index];
            BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if (stats.Type == BlitzCardType.Attack && !card.HasKeyword(BlitzKeywords.Unplayable))
            {
                candidates[candidateCount] = card;
                origins[candidateCount] = index;
                candidateCount++;
            }
        }
        if (candidateCount == 0)
            return;
        candidates = candidates[..candidateCount];
        origins = origins[..candidateCount];
        BeatDownStableShuffle(candidates, origins, x.R, ref x.B);
        int take = Math.Min(count, candidates.Length);
        if (take == 0)
            return;
        // The existing engine keeps the selected CardModel objects, so a card that an earlier
        // auto-play moves (drawn into hand, shuffled into the draw pile, exhausted, ...) is still
        // played from its new pile. Blitz marks each selected card with a one-action identity byte;
        // the mark travels with the card value through every pile move and is cleared in finally.
        int discardStart = BlitzPiles.Start(x.B, BlitzPile.Discard);
        for (int item = 0; item < take; item++)
        {
            ref BlitzCard marked = ref x.B.Cards[discardStart + origins[item]];
            marked.TransientMark = checked((byte)(item + 1));
        }
        try
        {
            for (int item = 0; item < take; item++)
            {
                if (BlitzKernel.IsOverOrEnding(x.B))
                    return;
                BlitzCard selected = candidates[item];
                BlitzCardStats stats = x.R.Card(selected.Id, selected.Upgrade);
                byte target = BlitzLimits.NoOwner;
                if (stats.Target == BlitzTargetType.AnyEnemy)
                {
                    // BeatDown resolves the target before AutoPlay's playability check.
                    target = BlitzKernel.RandomHittableEnemy(ref x.B);
                }
                if (!TryFindMarked(ref x.B, (byte)(item + 1), out BlitzPile pile, out int position))
                {
                    // The selected CardModel left every combat pile (removed from state, transformed
                    // or detached). The existing engine would still play that object; Blitz has no
                    // pile-free executing card representation, so this is an explicit boundary.
                    throw new BlitzUnsupportedException("BeatDown 选中的弃牌在结算前离开了战斗牌堆。");
                }
                BlitzKernel.AutoPlay(ref x, pile, position, target);
                if (x.NeedsChoice)
                    return;
            }
        }
        finally
        {
            ClearTransientMarks(ref x.B);
        }
    }

    private static bool TryFindMarked(ref BlitzBattle battle, byte mark, out BlitzPile pile, out int index)
    {
        for (int absolute = 0; absolute < battle.CardCount; absolute++)
        {
            if (battle.Cards[absolute].TransientMark != mark)
                continue;
            pile = BlitzPiles.PileOf(battle, absolute, out index);
            return true;
        }
        pile = BlitzPile.Hand;
        index = -1;
        return false;
    }

    private static void ClearTransientMarks(ref BlitzBattle battle)
    {
        // BeatDown reserves 1..63. Sly discard batches use 64..255 and may be nested around an
        // auto-played BeatDown, so their marks must survive this cleanup.
        for (int position = 0; position < battle.CardCount; position++)
        {
            if (battle.Cards[position].TransientMark < 64)
                battle.Cards[position].TransientMark = 0;
        }
        for (int slot = 0; slot < BlitzLimits.DetachedCards; slot++)
        {
            if (battle.DetachedCards[slot].TransientMark < 64)
                battle.DetachedCards[slot].TransientMark = 0;
        }
    }

    private static void BeatDownStableShuffle(Span<BlitzCard> cards, Span<int> origins, BlitzRules rules,
        ref BlitzBattle battle)
    {
        // Native List<T>.StableShuffle: sort with CardModel.CompareTo, then Fisher-Yates.
        BlitzCardSort.SortWithOrigins(cards, origins, rules);
        ref BlitzRngState rng = ref battle.RngStream(BlitzRngStream.Shuffle);
        for (int n = cards.Length; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (cards[k], cards[n]) = (cards[n], cards[k]);
            (origins[k], origins[n]) = (origins[n], origins[k]);
        }
    }
}

[BlitzContent("CATASTROPHE")]
public sealed class Catastrophe : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        int count = (int)BlitzCardEffects.Var(x, 0);
        Span<BlitzCard> buffer = stackalloc BlitzCard[BlitzLimits.Cards];
        Span<int> origins = stackalloc int[BlitzLimits.Cards];
        for (int item = 0; item < count; item++)
        {
            int pileCount = x.B.DrawCount;
            if (pileCount == 0)
                return;
            int eligibleCount = 0;
            for (int index = 0; index < pileCount; index++)
            {
                BlitzCard card = x.B.Cards[x.B.HandCount + index];
                if (!card.HasKeyword(BlitzKeywords.Unplayable))
                {
                    buffer[eligibleCount] = card;
                    origins[eligibleCount] = index;
                    eligibleCount++;
                }
            }
            int shuffledCount = eligibleCount;
            if (shuffledCount == 0)
            {
                for (int index = 0; index < pileCount; index++)
                {
                    buffer[index] = x.B.Cards[x.B.HandCount + index];
                    origins[index] = index;
                }
                shuffledCount = pileCount;
            }
            StableShuffle(buffer[..shuffledCount], origins[..shuffledCount], x.R, ref x.B);
            BlitzCard selected = buffer[0];
            int position = origins[0];
            if (position >= x.B.DrawCount
                || !BlitzFingerprint.Equal(BlitzPiles.At(ref x.B, BlitzPile.Draw, position), selected))
                throw new BlitzUnsupportedException("Catastrophe 选中的抽牌在结算前离开了抽牌堆。");
            BlitzKernel.AutoPlay(ref x, BlitzPile.Draw, position);
            if (x.NeedsChoice)
                return;
        }
    }

    private static void StableShuffle(Span<BlitzCard> cards, Span<int> origins, BlitzRules rules,
        ref BlitzBattle battle)
    {
        BlitzCardSort.SortWithOrigins(cards, origins, rules);
        ref BlitzRngState rng = ref battle.RngStream(BlitzRngStream.Shuffle);
        for (int n = cards.Length; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (cards[k], cards[n]) = (cards[n], cards[k]);
            (origins[k], origins[n]) = (origins[n], origins[k]);
        }
    }
}

[BlitzContent("JACKPOT")]
public sealed class Jackpot : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterZeroCost];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = (int)BlitzCardEffects.Var(x, 1);
        if (count > BlitzLimits.Cards)
            throw new BlitzCapacityException("Cards", BlitzLimits.Cards);
        Span<BlitzCard> generated = stackalloc BlitzCard[Math.Max(0, count)];
        int sampled = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.CharacterZeroCost, count,
            distinct: false, BlitzRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < sampled; index++)
        {
            if (x.Card.Upgrade > 0)
                BlitzKernel.UpgradeCard(x.R, ref generated[index]);
            BlitzKernel.AddGeneratedCard(ref x, generated[index], BlitzPile.Hand);
        }
    }
}

[BlitzContent("JACK_OF_ALL_TRADES")]
public sealed class JackOfAllTrades : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.ColorlessExceptJackOfAllTrades];

    public override void OnPlay(ref BlitzExec x)
    {
        int count = (int)BlitzCardEffects.Var(x, 0);
        Span<BlitzCard> generated = stackalloc BlitzCard[Math.Max(0, count)];
        int sampled = BlitzKernel.SampleCardPool(ref x.B, x.R,
            BlitzCardPools.ColorlessExceptJackOfAllTrades, count, distinct: true,
            BlitzRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < sampled; index++)
            BlitzKernel.AddGeneratedCard(ref x, generated[index], BlitzPile.Hand);
    }
}

[BlitzContent("SPLASH")]
public sealed class Splash : BlitzCardDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.OtherCharacterAttacks];

    public override void OnPlay(ref BlitzExec x)
    {
        Span<BlitzCard> options = stackalloc BlitzCard[3];
        int count = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.OtherCharacterAttacks, 3,
            distinct: true, BlitzRngStream.CombatCardGeneration, options);
        if (count == 0)
            return;
        for (int index = 0; index < count; index++)
        {
            if (x.Card.Upgrade > 0)
                BlitzKernel.UpgradeCard(x.R, ref options[index]);
            x.GeneratedOptions[index] = options[index];
        }
        x.GeneratedOptionCount = count;
        // RecordOptions records UnresolvedPlayerChoice, but PredictionCoverage treats every OnPlay
        // risk from a compensated card as compensated and snapshot.HasRisk stays false. The kernel
        // therefore does not raise the flag; only the imported root's uncompensated risk does.
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        int count = x.GeneratedOptionCount;
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseGeneratedCardsInRange(ref x, BlitzChoiceEffect.GenerateToHand,
                x.GeneratedOptions[..count], minCount: 0, maxCount: 1, selected, out int selectedCount))
            return;
        if (selectedCount == 0)
            return;
        // CardChoiceResolution.AddGeneratedSelectionToHand uses PredictedCard.Clone(), which clones
        // the prediction wrapper only and leaves IsClone false; it is not a gameplay clone.
        BlitzCard card = x.GeneratedOptions[selected[0]];
        BlitzKernel.MakeFreeThisTurn(x.R, ref card);
        BlitzKernel.AddGeneratedCard(ref x, card, BlitzPile.Hand);
    }
}

[BlitzContent("ALCHEMIZE")]
public sealed class Alchemize : BlitzCardDef
{
    public override bool GeneratesRandomPotion => true;

    public override void OnPlay(ref BlitzExec x)
    {
        ushort potion = BlitzKernel.GenerateRandomPotion(ref x.B, x.R);
        if (BlitzKernel.TryProcurePotion(ref x.B, potion))
        {
            // SimulatedCombatState.RecordLongTermResource(20) after a successful procure.
            x.B.AddCounter(BlitzCounter.LongTermResourceValue, 20);
            BlitzGrowthRewards.Add(ref x.B, BlitzGrowthSource.Alchemize);
        }
    }
}

[BlitzContent("CALAMITY")]
public sealed class Calamity : BlitzCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["CALAMITY_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.CalamityPower>(), 1, BlitzLimits.PlayerOwner);
}
