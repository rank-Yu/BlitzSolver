using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Ironclad;

// KG1 batch: auto-play, card-pool generation and transform. Existing-engine paths:
// - Havoc / Cascade: Simulator.AutoPlayFromDrawPile (AutoPlayCardMirrors).
// - Hellraiser: CardPowerOnPlaySupport applies HellraiserPower; the draw hook lives in
//   AfterCardDrawnMirrors.HandleHellraiserPower and reset in EndTurnPowerSupport.TriggerBatch048.
// - HowlFromBeyond: general attack plus the AfterAutoPostPlayPhaseEntered card hook
//   (AfterAutoPostPlayPhaseEnteredMirrors.HandleHowlFromBeyond).
// - Stampede: CardPowerOnPlaySupport.SToZ applies StampedePower; the hook lives in
//   AfterAutoPostPlayPhaseEnteredMirrors.HandleStampedePower.
// - InfernalBlade / Stoke: CardGenerationCardMirrors (character Attack / character all pools).
// - PrimalForce: CardOnPlaySupport.Batch042 transforms every transformable hand Attack into
//   GiantRock, upgraded when PrimalForce is upgraded (CardChoiceResolution.TransformCards).

[FlashContent("HAVOC")]
public sealed class Havoc : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.AutoPlayFromDrawPile(ref x, 1, forceExhaust: true);
}

[FlashContent("CASCADE")]
public sealed class Cascade : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.AutoPlayFromDrawPile(ref x, FlashKernel.ResolveEnergyXValue(ref x) + (x.Card.Upgrade > 0 ? 1 : 0),
            forceExhaust: false);
}

[FlashContent("HELLRAISER")]
public sealed class Hellraiser : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.HellraiserPower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("STAMPEDE")]
public sealed class Stampede : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.StampedePower>(), (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
}

[FlashContent("HOWL_FROM_BEYOND")]
public sealed class HowlFromBeyond : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void AfterAutoPostPlayPhaseEntered(ref FlashExec x, FlashListener self)
    {
        if (self.Kind != FlashListenerKind.Card || self.Owner != FlashLimits.PlayerOwner)
            return;
        int exhaustStart = x.B.HandCount + x.B.DrawCount + x.B.DiscardCount;
        int exhaustEnd = exhaustStart + x.B.ExhaustCount;
        if (self.Index < exhaustStart || self.Index >= exhaustEnd)
            return;
        FlashKernel.AutoPlay(ref x, FlashPile.Exhaust, self.Index - exhaustStart);
    }
}

[FlashContent("GIANT_ROCK")]
public sealed class GiantRock : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("PRIMAL_FORCE")]
public sealed class PrimalForce : FlashCardDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("GIANT_ROCK", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        int handCount = x.B.HandCount;
        Span<int> attacks = stackalloc int[handCount];
        int attackCount = 0;
        for (int index = 0; index < handCount; index++)
        {
            FlashCard card = x.B.Cards[index];
            if (FlashKernel.CardType(x.B, x.R, card) == FlashCardType.Attack)
                attacks[attackCount++] = index;
        }
        ushort giantRock = FlashRegistry.CardId<GiantRock>();
        int upgrade = x.Card.Upgrade > 0 ? 1 : 0;
        for (int item = 0; item < attackCount; item++)
        {
            FlashCard replacement = FlashKernel.CreateCard(x.R, giantRock, upgrade);
            FlashKernel.ReplaceCard(ref x, FlashPile.Hand, attacks[item], replacement);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("INFERNAL_BLADE")]
public sealed class InfernalBlade : FlashCardDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAttacks];

    public override void OnPlay(ref FlashExec x)
    {
        Span<FlashCard> generated = stackalloc FlashCard[1];
        int count = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.CharacterAttacks, 1,
            distinct: true, FlashRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < count; index++)
        {
            FlashKernel.MakeFreeThisTurn(x.R, ref generated[index]);
            FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Hand);
        }
    }
}

[FlashContent("STOKE")]
public sealed class Stoke : FlashCardDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void OnPlay(ref FlashExec x)
    {
        // Vanilla snapshots the hand CardModel objects, exhausts each object, then generates one card
        // per initial hand card. A digest/Id match is not object identity: exhausting one card can
        // change another card's mutable cost (Stomp), and Dark Embrace can draw a new equal card into
        // hand before the snapshot card is reached. Mark each initial hand card with a one-action
        // identity byte; the mark travels with the value through every pile move and never appears in
        // a state key. Marks 64..255 match the Sly batch convention; BeatDown owns 1..63.
        int exhaustCount = x.B.HandCount;
        int markBase = x.TransientMarkBase;
        if (64 + markBase + exhaustCount > 256)
            throw new FlashCapacityException("TransientMarks", 256 - 64);
        x.TransientMarkBase = checked((byte)(markBase + exhaustCount));
        int markStart = 64 + markBase;
        try
        {
            for (int index = 0; index < exhaustCount; index++)
                x.B.Cards[index].TransientMark = checked((byte)(markStart + index));
            for (int item = 0; item < exhaustCount; item++)
            {
                byte mark = checked((byte)(markStart + item));
                int absolute = FindMarked(x.B, mark);
                if (absolute < 0)
                    throw new FlashUnsupportedException("Stoke 的快照手牌在消耗过程中离开了战斗牌堆。");
                FlashPile pile = FlashPiles.PileOf(x.B, absolute, out int position);
                // A card already in the exhaust pile is the native no-op case for CardCmd.Exhaust.
                if (pile != FlashPile.Exhaust)
                    FlashKernel.Exhaust(ref x, pile, position);
                if (x.NeedsChoice)
                    return;
            }
        }
        finally
        {
            for (int position = 0; position < x.B.CardCount; position++)
            {
                if (x.B.Cards[position].TransientMark >= markStart
                    && x.B.Cards[position].TransientMark < markStart + exhaustCount)
                {
                    x.B.Cards[position].TransientMark = 0;
                }
            }
            for (int slot = 0; slot < FlashLimits.DetachedCards; slot++)
            {
                if (x.B.DetachedCards[slot].TransientMark >= markStart
                    && x.B.DetachedCards[slot].TransientMark < markStart + exhaustCount)
                {
                    x.B.DetachedCards[slot].TransientMark = 0;
                }
            }
            x.TransientMarkBase = checked((byte)markBase);
        }
        // Generation happens after the whole exhaust loop, as in the native card.
        Span<FlashCard> generated = stackalloc FlashCard[Math.Min(exhaustCount, FlashLimits.Cards)];
        int count = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.CharacterAll, exhaustCount,
            distinct: false, FlashRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < count; index++)
        {
            if (x.Card.Upgrade > 0)
                FlashKernel.UpgradeCard(x.R, ref generated[index]);
            FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Hand);
        }
    }

    private static int FindMarked(in FlashBattle battle, byte mark)
    {
        for (int absolute = 0; absolute < battle.CardCount; absolute++)
        {
            if (battle.Cards[absolute].TransientMark == mark)
                return absolute;
        }
        return -1;
    }
}

[FlashContent("DISCOVERY")]
public sealed class Discovery : FlashCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void OnPlay(ref FlashExec x)
    {
        Span<FlashCard> options = stackalloc FlashCard[3];
        int count = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.CharacterAll, 3,
            distinct: true, FlashRngStream.CombatCardGeneration, options);
        if (count == 0)
            return;
        for (int index = 0; index < count; index++)
            x.GeneratedOptions[index] = options[index];
        x.GeneratedOptionCount = count;
        // CardGenerationOptions + RecordRisk(UnresolvedPlayerChoice) are compensated for Discovery
        // by CardOnPlayCompensationCatalog, so snapshot.HasRisk remains false; see the Colorless
        // Splash definition for the same reasoning.
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
