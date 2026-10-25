using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Regent;

/// <summary>
/// <c>DecisionsDecisions</c>: draw the Cards variable, choose one non-unplayable Skill in hand, then
/// auto-play it Repeat times (<c>CardChoiceResolution.AutoPlayRepeated</c> →
/// <c>ContinueRepeatedSelection</c>). The selected instance carries a dynamic 64..255 transient
/// mark and is followed through every pile move; equal copies elsewhere are never substituted.
/// </summary>
[BlitzContent("DECISIONS_DECISIONS")]
public sealed class DecisionsDecisions : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Repeat"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly BlitzCard card = ref x.B.Cards[index];
            BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if (stats.Type == BlitzCardType.Skill && !BlitzKernel.HasKeyword(x.B, card, BlitzKeywords.Unplayable))
                options[count++] = index;
        }
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.AutoPlayRepeated, BlitzPile.Hand, options[..count],
                1, selected, out int chosen))
        {
            return;
        }
        if (chosen == 0)
            return;
        // ContinueRepeatedSelection keeps the selected CardModel, so equal copies elsewhere are not
        // interchangeable. Mark the selected instance with the dynamic 64..255 identity range
        // (TransientMarkBase, shared with Sly/Stoke and the Mayhem queue) and follow the mark through
        // every pile move. The mark never enters a state key and is cleared in finally.
        int markBase = x.TransientMarkBase;
        if (64 + markBase + 1 > 256)
            throw new BlitzCapacityException("TransientMarks", 256 - 64);
        x.TransientMarkBase = checked((byte)(markBase + 1));
        byte mark = checked((byte)(64 + markBase));
        ref BlitzCard selectedCard = ref BlitzPiles.At(ref x.B, BlitzPile.Hand, selected[0]);
        selectedCard.TransientMark = mark;
        BlitzCard repeated = selectedCard;
        bool leftCombat = false;
        try
        {
            int repeats = (int)BlitzCardEffects.Var(x, 1);
            for (int index = 0; index < repeats; index++)
            {
                if (BlitzKernel.IsOverOrEnding(x.B) || x.B.Player.IsDead)
                    return;
                bool played;
                BlitzCard playedCard;
                BlitzPile resultPile;
                int resultIndex;
                if (BlitzKernel.TryFindUniqueTransientMark(x.B, mark, out BlitzPile pile, out int position))
                {
                    played = BlitzKernel.AutoPlay(ref x, pile, position, BlitzLimits.NoOwner, skipXCapture: false,
                        out playedCard, out resultPile, out resultIndex);
                }
                else if (leftCombat)
                {
                    // The existing engine's AutoPlay re-adds a live card whose GetPile(State) is null
                    // and plays it again (a Power that left combat on its previous play).
                    played = BlitzKernel.AutoPlay(ref x, repeated, BlitzLimits.NoOwner, skipXCapture: false,
                        historyCourseDupe: false, out playedCard, out resultPile, out resultIndex);
                }
                else
                {
                    throw new BlitzUnsupportedException(
                        $"重复自动打出的 {BlitzRegistry.Card(repeated.Id).NativeId}+{repeated.Upgrade} 失去瞬态标记。");
                }
                if (x.NeedsChoice)
                    return;
                if (!played)
                    break;
                repeated = playedCard;
                leftCombat = resultIndex < 0;
            }
        }
        finally
        {
            for (int position = 0; position < x.B.CardCount; position++)
            {
                if (x.B.Cards[position].TransientMark == mark)
                    x.B.Cards[position].TransientMark = 0;
            }
            for (int slot = 0; slot < BlitzLimits.DetachedCards; slot++)
            {
                if (x.B.DetachedCards[slot].TransientMark == mark)
                    x.B.DetachedCards[slot].TransientMark = 0;
            }
            x.TransientMarkBase = checked((byte)markBase);
        }
    }
}

/// <summary>
/// <c>Guards</c>: optionally choose any number of hand cards and transform each into the canonical
/// Minion Sacrifice, upgraded when Guards is upgraded.
/// </summary>
[BlitzContent("GUARDS")]
public sealed class Guards : BlitzCardDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("MINION_SACRIFICE", 0)];

    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[options.Length];
        if (!BlitzKernel.ChooseCardsInRange(ref x, BlitzChoiceEffect.Transform, BlitzPile.Hand, options, 0,
                options.Length, selected, out int chosen))
        {
            return;
        }
        Charge.TransformSelected(ref x, BlitzPile.Hand, selected[..chosen], "MINION_SACRIFICE");
    }
}

/// <summary>
/// <c>ForegoneConclusion</c>: applies ForegoneConclusionPower with the Cards variable
/// (<c>CardPowerOnPlaySupport</c>); the power's draw-pile choice runs at BeforeHandDraw.
/// </summary>
[BlitzContent("FOREGONE_CONCLUSION")]
public sealed class ForegoneConclusion : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["FOREGONE_CONCLUSION_POWER"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.ForegoneConclusionPower>(), (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
}

/// <summary><c>TheSmith</c>: CardOnPlaySupport forges the Forge variable.</summary>
[BlitzContent("THE_SMITH")]
public sealed class TheSmith : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Forge(ref x, (int)BlitzCardEffects.Var(x, 0));
}

/// <summary>
/// <c>IAmInvincible</c>: the inferred recipe gains Block; AfterAutoPostPlayPhaseEnteredMirrors then
/// auto-plays one card from the top of the draw pile when this card is that top card.
/// </summary>
[BlitzContent("I_AM_INVINCIBLE")]
public sealed class IAmInvincible : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);

    public override void AfterAutoPostPlayPhaseEntered(ref BlitzExec x, BlitzListener self)
    {
        if (self.Kind != BlitzListenerKind.Card || self.Owner != BlitzLimits.PlayerOwner)
            return;
        // DrawPile.TopCard?.References(card): the top of the draw pile is this card. Blitz has no
        // instance identity, so the hook does not test the listener's own position (it can be stale
        // after an earlier copy was auto-played); instead it checks the current top value. Every copy
        // anywhere is a listener, and there is at least one listener per draw-pile copy, so all
        // consecutive I Am Invincible cards at the top are auto-played exactly once each.
        if (x.B.DrawCount == 0)
            return;
        ref readonly BlitzCard top = ref BlitzPiles.At(ref x.B, BlitzPile.Draw, 0);
        if (top.Id != BlitzRegistry.CardId<IAmInvincible>())
            return;
        BlitzKernel.AutoPlayFromDrawPile(ref x, 1, forceExhaust: false);
    }
}

/// <summary>
/// <c>BigBang</c> (Exhaust): the inferred recipe draws the Cards variable, then
/// CardEffectSpecRegistry's <c>BigBang</c> gains Energy, gains Stars and forges, in that order.
/// </summary>
[BlitzContent("BIG_BANG")]
public sealed class BigBang : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Energy", "Stars", "Forge"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 0), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 1));
        if (!BlitzKernel.GainStars(ref x, (int)BlitzCardEffects.Var(x, 2)))
            return;
        BlitzKernel.Forge(ref x, (int)BlitzCardEffects.Var(x, 3));
    }
}
