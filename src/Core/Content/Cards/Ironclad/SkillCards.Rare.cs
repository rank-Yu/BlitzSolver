using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Ironclad;

[FlashContent("BRAND")]
public sealed class Brand : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "StrengthPower"];

    // CardOnPlaySupport (Batch042): self damage with the played card as source.
    public override void OnPlay(ref FlashExec x) => FlashCardEffects.LoseHpFromCard(ref x, FlashCardEffects.Var(x, 0));

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: Exhaust, Hand, 1, every hand card; ApplyPostChoiceEffects applies Strength. With an
        // empty hand there is no spec, but BuildRequiredEmptyChoice still asks an empty Exhaust/Hand
        // choice before the post-choice effects.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        int chosen = 0;
        bool answered = true;
        if (options.IsEmpty)
        {
            // ResolveNestedCardChoice applies the required empty choice without asking during an
            // auto-play; a manual play asks it.
            if (!x.CurrentPlayIsAutoPlay)
                answered = FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand,
                    options, 0, 0, selected, out chosen);
        }
        else
        {
            answered = FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand, options, 1,
                selected, out chosen);
        }
        if (!answered)
            return;
        FlashKernel.ExhaustSelected(ref x, FlashPile.Hand, selected[..chosen]);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("CASCADE")]
public sealed class Cascade : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.AutoPlayFromDrawPile(ref x, FlashKernel.ResolveEnergyXValue(ref x) + (x.Card.Upgrade > 0 ? 1 : 0),
            forceExhaust: false);
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

[FlashContent("IMPERVIOUS")]
public sealed class Impervious : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("OFFERING")]
public sealed class Offering : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "Energy", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.LoseHpFromCard(ref x, FlashCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 2), fromHandDraw: false);
    }
}

[FlashContent("DOMINATE")]
public sealed class Dominate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        ushort vulnerable = FlashRegistry.PowerId<Powers.VulnerablePower>();
        FlashKernel.ApplyPower(ref x, x.Target, vulnerable, (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.Dominate resolves queued power changes (Vicious) before reading the
        // target's resulting Vulnerable amount.
        FlashKernel.ResolvePowerAmountChanges(ref x);
        if (x.NeedsChoice)
            return;
        int amount = FlashKernel.GetAmount(x.B, x.Target, vulnerable);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            amount, FlashLimits.PlayerOwner);
    }
}

[FlashContent("NOT_YET")]
public sealed class NotYet : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Heal"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Heal(ref x, FlashLimits.PlayerOwner, FlashCardEffects.Var(x, 0));
}

[FlashContent("ONE_TWO_PUNCH")]
public sealed class OneTwoPunch : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Attacks"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.OneTwoPunchPower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}
