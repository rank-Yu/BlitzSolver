using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Ironclad;

[ContentId("BRAND")]
public sealed class Brand : CardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "StrengthPower"];

    // CardOnPlaySupport (Batch042): self damage with the played card as source.
    public override void OnPlay(ref Exec x) => CardEffects.LoseHpFromCard(ref x, CardEffects.Var(x, 0));

    public override void OwnChoice(ref Exec x)
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
                answered = Kernel.ChooseCardsInRange(ref x, ChoiceEffect.Exhaust, Pile.Hand,
                    options, 0, 0, selected, out chosen);
        }
        else
        {
            answered = Kernel.ChooseCards(ref x, ChoiceEffect.Exhaust, Pile.Hand, options, 1,
                selected, out chosen);
        }
        if (!answered)
            return;
        Kernel.ExhaustSelected(ref x, Pile.Hand, selected[..chosen]);
        if (x.NeedsChoice)
            return;
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StrengthPower>(),
            (int)CardEffects.Var(x, 1), Limits.PlayerOwner);
    }
}

[ContentId("CASCADE")]
public sealed class Cascade : CardDef
{
    public override void OnPlay(ref Exec x)
        => Kernel.AutoPlayFromDrawPile(ref x, Kernel.ResolveEnergyXValue(ref x) + (x.Card.Upgrade > 0 ? 1 : 0),
            forceExhaust: false);
}

[ContentId("PRIMAL_FORCE")]
public sealed class PrimalForce : CardDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("GIANT_ROCK", 0)];

    public override void OnPlay(ref Exec x)
    {
        int handCount = x.B.HandCount;
        Span<int> attacks = stackalloc int[handCount];
        int attackCount = 0;
        for (int index = 0; index < handCount; index++)
        {
            Card card = x.B.Cards[index];
            if (Kernel.ResolveCardType(x.B, x.R, card) == CardType.Attack)
                attacks[attackCount++] = index;
        }
        ushort giantRock = Registry.CardId<GiantRock>();
        int upgrade = x.Card.Upgrade > 0 ? 1 : 0;
        for (int item = 0; item < attackCount; item++)
        {
            Card replacement = Kernel.CreateCard(x.R, giantRock, upgrade);
            Kernel.ReplaceCard(ref x, Pile.Hand, attacks[item], replacement);
            if (x.NeedsChoice)
                return;
        }
    }
}

[ContentId("STOKE")]
public sealed class Stoke : CardDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [CardPools.CharacterAll];

    public override void OnPlay(ref Exec x)
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
            throw new CapacityException("TransientMarks", 256 - 64);
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
                    throw new UnsupportedException("Stoke 的快照手牌在消耗过程中离开了战斗牌堆。");
                Pile pile = Piles.PileOf(x.B, absolute, out int position);
                // A card already in the exhaust pile is the native no-op case for CardCmd.Exhaust.
                if (pile != Pile.Exhaust)
                    Kernel.Exhaust(ref x, pile, position);
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
            for (int slot = 0; slot < Limits.DetachedCards; slot++)
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
        Span<Card> generated = stackalloc Card[Math.Min(exhaustCount, Limits.Cards)];
        int count = Kernel.SampleCardPool(ref x.B, x.R, CardPools.CharacterAll, exhaustCount,
            distinct: false, RngStream.CombatCardGeneration, generated);
        for (int index = 0; index < count; index++)
        {
            if (x.Card.Upgrade > 0)
                Kernel.UpgradeCard(x.R, ref generated[index]);
            Kernel.AddGeneratedCard(ref x, generated[index], Pile.Hand);
        }
    }

    private static int FindMarked(in Battle battle, byte mark)
    {
        for (int absolute = 0; absolute < battle.CardCount; absolute++)
        {
            if (battle.Cards[absolute].TransientMark == mark)
                return absolute;
        }
        return -1;
    }
}

[ContentId("IMPERVIOUS")]
public sealed class Impervious : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralBlock(ref x, 0);
}

[ContentId("OFFERING")]
public sealed class Offering : CardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "Energy", "Cards"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.LoseHpFromCard(ref x, CardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        Kernel.GainEnergy(ref x, CardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        Kernel.Draw(ref x, CardEffects.Var(x, 2), fromHandDraw: false);
    }
}

[ContentId("DOMINATE")]
public sealed class Dominate : CardDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    public override void OnPlay(ref Exec x)
    {
        if (x.Target == Limits.NoOwner)
            return;
        ushort vulnerable = Registry.PowerId<Powers.VulnerablePower>();
        Kernel.ApplyPower(ref x, x.Target, vulnerable, (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.Dominate resolves queued power changes (Vicious) before reading the
        // target's resulting Vulnerable amount.
        Kernel.ResolvePowerAmountChanges(ref x);
        if (x.NeedsChoice)
            return;
        int amount = Kernel.GetAmount(x.B, x.Target, vulnerable);
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StrengthPower>(),
            amount, Limits.PlayerOwner);
    }
}

[ContentId("NOT_YET")]
public sealed class NotYet : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Heal"];

    public override void OnPlay(ref Exec x)
        => Kernel.Heal(ref x, Limits.PlayerOwner, CardEffects.Var(x, 0));
}

[ContentId("ONE_TWO_PUNCH")]
public sealed class OneTwoPunch : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Attacks"];

    public override void OnPlay(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.OneTwoPunchPower>(),
            (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}
