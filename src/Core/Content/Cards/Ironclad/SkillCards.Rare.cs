using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Ironclad;

[BlitzContent("BRAND")]
public sealed class Brand : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "StrengthPower"];

    // CardOnPlaySupport (Batch042): self damage with the played card as source.
    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.LoseHpFromCard(ref x, BlitzCardEffects.Var(x, 0));

    public override void OwnChoice(ref BlitzExec x)
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
                answered = BlitzKernel.ChooseCardsInRange(ref x, BlitzChoiceEffect.Exhaust, BlitzPile.Hand,
                    options, 0, 0, selected, out chosen);
        }
        else
        {
            answered = BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Exhaust, BlitzPile.Hand, options, 1,
                selected, out chosen);
        }
        if (!answered)
            return;
        BlitzKernel.ExhaustSelected(ref x, BlitzPile.Hand, selected[..chosen]);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("CASCADE")]
public sealed class Cascade : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.AutoPlayFromDrawPile(ref x, BlitzKernel.ResolveEnergyXValue(ref x) + (x.Card.Upgrade > 0 ? 1 : 0),
            forceExhaust: false);
}

[BlitzContent("PRIMAL_FORCE")]
public sealed class PrimalForce : BlitzCardDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("GIANT_ROCK", 0)];

    public override void OnPlay(ref BlitzExec x)
    {
        int handCount = x.B.HandCount;
        Span<int> attacks = stackalloc int[handCount];
        int attackCount = 0;
        for (int index = 0; index < handCount; index++)
        {
            BlitzCard card = x.B.Cards[index];
            if (BlitzKernel.CardType(x.B, x.R, card) == BlitzCardType.Attack)
                attacks[attackCount++] = index;
        }
        ushort giantRock = BlitzRegistry.CardId<GiantRock>();
        int upgrade = x.Card.Upgrade > 0 ? 1 : 0;
        for (int item = 0; item < attackCount; item++)
        {
            BlitzCard replacement = BlitzKernel.CreateCard(x.R, giantRock, upgrade);
            BlitzKernel.ReplaceCard(ref x, BlitzPile.Hand, attacks[item], replacement);
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("STOKE")]
public sealed class Stoke : BlitzCardDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAll];

    public override void OnPlay(ref BlitzExec x)
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
            throw new BlitzCapacityException("TransientMarks", 256 - 64);
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
                    throw new BlitzUnsupportedException("Stoke 的快照手牌在消耗过程中离开了战斗牌堆。");
                BlitzPile pile = BlitzPiles.PileOf(x.B, absolute, out int position);
                // A card already in the exhaust pile is the native no-op case for CardCmd.Exhaust.
                if (pile != BlitzPile.Exhaust)
                    BlitzKernel.Exhaust(ref x, pile, position);
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
            for (int slot = 0; slot < BlitzLimits.DetachedCards; slot++)
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
        Span<BlitzCard> generated = stackalloc BlitzCard[Math.Min(exhaustCount, BlitzLimits.Cards)];
        int count = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.CharacterAll, exhaustCount,
            distinct: false, BlitzRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < count; index++)
        {
            if (x.Card.Upgrade > 0)
                BlitzKernel.UpgradeCard(x.R, ref generated[index]);
            BlitzKernel.AddGeneratedCard(ref x, generated[index], BlitzPile.Hand);
        }
    }

    private static int FindMarked(in BlitzBattle battle, byte mark)
    {
        for (int absolute = 0; absolute < battle.CardCount; absolute++)
        {
            if (battle.Cards[absolute].TransientMark == mark)
                return absolute;
        }
        return -1;
    }
}

[BlitzContent("IMPERVIOUS")]
public sealed class Impervious : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);
}

[BlitzContent("OFFERING")]
public sealed class Offering : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "Energy", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.LoseHpFromCard(ref x, BlitzCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 2), fromHandDraw: false);
    }
}

[BlitzContent("DOMINATE")]
public sealed class Dominate : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        ushort vulnerable = BlitzRegistry.PowerId<Powers.VulnerablePower>();
        BlitzKernel.ApplyPower(ref x, x.Target, vulnerable, (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.Dominate resolves queued power changes (Vicious) before reading the
        // target's resulting Vulnerable amount.
        BlitzKernel.ResolvePowerAmountChanges(ref x);
        if (x.NeedsChoice)
            return;
        int amount = BlitzKernel.GetAmount(x.B, x.Target, vulnerable);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
            amount, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("NOT_YET")]
public sealed class NotYet : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Heal"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Heal(ref x, BlitzLimits.PlayerOwner, BlitzCardEffects.Var(x, 0));
}

[BlitzContent("ONE_TWO_PUNCH")]
public sealed class OneTwoPunch : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Attacks"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.OneTwoPunchPower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}
