namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Colorless;

// KG1 batch: auto-play, card-pool generation, generated-card choice and a random potion.
// Existing-engine paths:
// - BeatDown / Catastrophe: CardSelectionCardMirrors.BeatDownOnPlay / CatastropheOnPlay.
// - Jackpot: CardGenerationCardMirrors.JackpotOnPlay (character cards with canonical cost 0, with
//   replacement, upgraded when Jackpot is upgraded).
// - JackOfAllTrades: CardGenerationCardMirrors.JackOfAllTradesOnPlay (distinct colorless cards,
//   excluding itself).
// - Discovery / Splash: CardGenerationCardMirrors.RecordOptions + RecordRisk then
//   CardChoiceSupport.GetSpec's GenerateToHand branch.
// - Alchemize: PotionGenerationCardMirrors.AlchemizeOnPlay (random combat potion).
// - Calamity: CardPowerOnPlaySupport applies CalamityPower.

[FlashContent("BEAT_DOWN")]
public sealed class BeatDown : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        int count = (int)FlashCardEffects.Var(x, 0);
        if (count <= 0)
            return;
        int pileCount = x.B.DiscardCount;
        Span<FlashCard> candidates = stackalloc FlashCard[pileCount];
        Span<int> origins = stackalloc int[pileCount];
        int candidateCount = 0;
        for (int index = 0; index < pileCount; index++)
        {
            FlashCard card = x.B.Cards[x.B.HandCount + x.B.DrawCount + index];
            FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if (stats.Type == FlashCardType.Attack && !card.HasKeyword(FlashKeywords.Unplayable))
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
        // played from its new pile. Flash marks each selected card with a one-action identity byte;
        // the mark travels with the card value through every pile move and is cleared in finally.
        int discardStart = FlashPiles.Start(x.B, FlashPile.Discard);
        for (int item = 0; item < take; item++)
        {
            ref FlashCard marked = ref x.B.Cards[discardStart + origins[item]];
            marked.TransientMark = checked((byte)(item + 1));
        }
        try
        {
            for (int item = 0; item < take; item++)
            {
                if (FlashKernel.IsOverOrEnding(x.B))
                    return;
                FlashCard selected = candidates[item];
                FlashCardStats stats = x.R.Card(selected.Id, selected.Upgrade);
                byte target = FlashLimits.NoOwner;
                if (stats.Target == FlashTargetType.AnyEnemy)
                {
                    // BeatDown resolves the target before AutoPlay's playability check.
                    target = FlashKernel.RandomHittableEnemy(ref x.B);
                }
                if (!TryFindMarked(ref x.B, (byte)(item + 1), out FlashPile pile, out int position))
                {
                    // The selected CardModel left every combat pile (removed from state, transformed
                    // or detached). The existing engine would still play that object; Flash has no
                    // pile-free executing card representation, so this is an explicit boundary.
                    throw new FlashUnsupportedException("BeatDown 选中的弃牌在结算前离开了战斗牌堆。");
                }
                FlashKernel.AutoPlay(ref x, pile, position, target);
                if (x.NeedsChoice)
                    return;
            }
        }
        finally
        {
            ClearTransientMarks(ref x.B);
        }
    }

    private static bool TryFindMarked(ref FlashBattle battle, byte mark, out FlashPile pile, out int index)
    {
        for (int absolute = 0; absolute < battle.CardCount; absolute++)
        {
            if (battle.Cards[absolute].TransientMark != mark)
                continue;
            pile = FlashPiles.PileOf(battle, absolute, out index);
            return true;
        }
        pile = FlashPile.Hand;
        index = -1;
        return false;
    }

    private static void ClearTransientMarks(ref FlashBattle battle)
    {
        for (int position = 0; position < battle.CardCount; position++)
            battle.Cards[position].TransientMark = 0;
        for (int slot = 0; slot < FlashLimits.DetachedCards; slot++)
            battle.DetachedCards[slot].TransientMark = 0;
    }

    private static void BeatDownStableShuffle(Span<FlashCard> cards, Span<int> origins, FlashRules rules,
        ref FlashBattle battle)
    {
        // Native List<T>.StableShuffle: sort with CardModel.CompareTo, then Fisher-Yates.
        FlashCardSort.SortWithOrigins(cards, origins, rules);
        ref FlashRngState rng = ref battle.RngStream(FlashRngStream.Shuffle);
        for (int n = cards.Length; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (cards[k], cards[n]) = (cards[n], cards[k]);
            (origins[k], origins[n]) = (origins[n], origins[k]);
        }
    }
}

[FlashContent("CATASTROPHE")]
public sealed class Catastrophe : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        int count = (int)FlashCardEffects.Var(x, 0);
        Span<FlashCard> buffer = stackalloc FlashCard[FlashLimits.Cards];
        Span<int> origins = stackalloc int[FlashLimits.Cards];
        for (int item = 0; item < count; item++)
        {
            int pileCount = x.B.DrawCount;
            if (pileCount == 0)
                return;
            int eligibleCount = 0;
            for (int index = 0; index < pileCount; index++)
            {
                FlashCard card = x.B.Cards[x.B.HandCount + index];
                if (!card.HasKeyword(FlashKeywords.Unplayable))
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
            FlashCard selected = buffer[0];
            int position = origins[0];
            if (position >= x.B.DrawCount
                || !FlashFingerprint.Equal(FlashPiles.At(ref x.B, FlashPile.Draw, position), selected))
                throw new FlashUnsupportedException("Catastrophe 选中的抽牌在结算前离开了抽牌堆。");
            FlashKernel.AutoPlay(ref x, FlashPile.Draw, position);
            if (x.NeedsChoice)
                return;
        }
    }

    private static void StableShuffle(Span<FlashCard> cards, Span<int> origins, FlashRules rules,
        ref FlashBattle battle)
    {
        FlashCardSort.SortWithOrigins(cards, origins, rules);
        ref FlashRngState rng = ref battle.RngStream(FlashRngStream.Shuffle);
        for (int n = cards.Length; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (cards[k], cards[n]) = (cards[n], cards[k]);
            (origins[k], origins[n]) = (origins[n], origins[k]);
        }
    }
}

[FlashContent("JACKPOT")]
public sealed class Jackpot : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterZeroCost];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = (int)FlashCardEffects.Var(x, 1);
        if (count > FlashLimits.Cards)
            throw new FlashCapacityException("Cards", FlashLimits.Cards);
        Span<FlashCard> generated = stackalloc FlashCard[Math.Max(0, count)];
        int sampled = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.CharacterZeroCost, count,
            distinct: false, FlashRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < sampled; index++)
        {
            if (x.Card.Upgrade > 0)
                FlashKernel.UpgradeCard(x.R, ref generated[index]);
            FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Hand);
        }
    }
}

[FlashContent("JACK_OF_ALL_TRADES")]
public sealed class JackOfAllTrades : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.ColorlessExceptJackOfAllTrades];

    public override void OnPlay(ref FlashExec x)
    {
        int count = (int)FlashCardEffects.Var(x, 0);
        Span<FlashCard> generated = stackalloc FlashCard[Math.Max(0, count)];
        int sampled = FlashKernel.SampleCardPool(ref x.B, x.R,
            FlashCardPools.ColorlessExceptJackOfAllTrades, count, distinct: true,
            FlashRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < sampled; index++)
            FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Hand);
    }
}

[FlashContent("SPLASH")]
public sealed class Splash : FlashCardDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.OtherCharacterAttacks];

    public override void OnPlay(ref FlashExec x)
    {
        Span<FlashCard> options = stackalloc FlashCard[3];
        int count = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.OtherCharacterAttacks, 3,
            distinct: true, FlashRngStream.CombatCardGeneration, options);
        if (count == 0)
            return;
        for (int index = 0; index < count; index++)
        {
            if (x.Card.Upgrade > 0)
                FlashKernel.UpgradeCard(x.R, ref options[index]);
            x.GeneratedOptions[index] = options[index];
        }
        x.GeneratedOptionCount = count;
        // RecordOptions records UnresolvedPlayerChoice, but PredictionCoverage treats every OnPlay
        // risk from a compensated card as compensated and snapshot.HasRisk stays false. The kernel
        // therefore does not raise the flag; only the imported root's uncompensated risk does.
    }

    public override void OwnChoice(ref FlashExec x)
    {
        int count = x.GeneratedOptionCount;
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseGeneratedCardsInRange(ref x, FlashChoiceEffect.GenerateToHand,
                x.GeneratedOptions[..count], minCount: 0, maxCount: 1, selected, out int selectedCount))
            return;
        if (selectedCount == 0)
            return;
        // CardChoiceResolution.AddGeneratedSelectionToHand uses PredictedCard.Clone(), which clones
        // the prediction wrapper only and leaves IsClone false; it is not a gameplay clone.
        FlashCard card = x.GeneratedOptions[selected[0]];
        FlashKernel.MakeFreeThisTurn(x.R, ref card);
        FlashKernel.AddGeneratedCard(ref x, card, FlashPile.Hand);
    }
}

[FlashContent("ALCHEMIZE")]
public sealed class Alchemize : FlashCardDef
{
    public override bool GeneratesRandomPotion => true;

    public override void OnPlay(ref FlashExec x)
    {
        ushort potion = FlashKernel.GenerateRandomPotion(ref x.B, x.R);
        if (FlashKernel.TryProcurePotion(ref x.B, potion))
        {
            // SimulatedCombatState.RecordLongTermResource(20) after a successful procure.
            x.B.AddCounter(FlashCounter.LongTermResourceValue, 20);
            FlashGrowthRewards.Add(ref x.B, FlashGrowthSource.Alchemize);
        }
    }
}

[FlashContent("CALAMITY")]
public sealed class Calamity : FlashCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["CALAMITY_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.CalamityPower>(), 1, FlashLimits.PlayerOwner);
}
