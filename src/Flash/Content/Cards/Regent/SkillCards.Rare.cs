using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Regent;

/// <summary>
/// <c>DecisionsDecisions</c>: draw the Cards variable, choose one non-unplayable Skill in hand, then
/// auto-play it Repeat times (<c>CardChoiceResolution.AutoPlayRepeated</c> →
/// <c>ContinueRepeatedSelection</c>). The selected instance carries a dynamic 64..255 transient
/// mark and is followed through every pile move; equal copies elsewhere are never substituted.
/// </summary>
[FlashContent("DECISIONS_DECISIONS")]
public sealed class DecisionsDecisions : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if (stats.Type == FlashCardType.Skill && !FlashKernel.HasKeyword(x.B, card, FlashKeywords.Unplayable))
                options[count++] = index;
        }
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.AutoPlayRepeated, FlashPile.Hand, options[..count],
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
            throw new FlashCapacityException("TransientMarks", 256 - 64);
        x.TransientMarkBase = checked((byte)(markBase + 1));
        byte mark = checked((byte)(64 + markBase));
        ref FlashCard selectedCard = ref FlashPiles.At(ref x.B, FlashPile.Hand, selected[0]);
        selectedCard.TransientMark = mark;
        FlashCard repeated = selectedCard;
        bool leftCombat = false;
        try
        {
            int repeats = (int)FlashCardEffects.Var(x, 1);
            for (int index = 0; index < repeats; index++)
            {
                if (FlashKernel.IsOverOrEnding(x.B) || x.B.Player.IsDead)
                    return;
                bool played;
                FlashCard playedCard;
                FlashPile resultPile;
                int resultIndex;
                if (FlashKernel.TryFindUniqueTransientMark(x.B, mark, out FlashPile pile, out int position))
                {
                    played = FlashKernel.AutoPlay(ref x, pile, position, FlashLimits.NoOwner, skipXCapture: false,
                        out playedCard, out resultPile, out resultIndex);
                }
                else if (leftCombat)
                {
                    // The existing engine's AutoPlay re-adds a live card whose GetPile(State) is null
                    // and plays it again (a Power that left combat on its previous play).
                    played = FlashKernel.AutoPlay(ref x, repeated, FlashLimits.NoOwner, skipXCapture: false,
                        historyCourseDupe: false, out playedCard, out resultPile, out resultIndex);
                }
                else
                {
                    throw new FlashUnsupportedException(
                        $"重复自动打出的 {FlashRegistry.Card(repeated.Id).NativeId}+{repeated.Upgrade} 失去瞬态标记。");
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
            for (int slot = 0; slot < FlashLimits.DetachedCards; slot++)
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
[FlashContent("GUARDS")]
public sealed class Guards : FlashCardDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("MINION_SACRIFICE", 0)];

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[options.Length];
        if (!FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.Transform, FlashPile.Hand, options, 0,
                options.Length, selected, out int chosen))
        {
            return;
        }
        Charge.TransformSelected(ref x, FlashPile.Hand, selected[..chosen], "MINION_SACRIFICE");
    }
}

/// <summary>
/// <c>ForegoneConclusion</c>: applies ForegoneConclusionPower with the Cards variable
/// (<c>CardPowerOnPlaySupport</c>); the power's draw-pile choice runs at BeforeHandDraw.
/// </summary>
[FlashContent("FOREGONE_CONCLUSION")]
public sealed class ForegoneConclusion : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> AppliedPowers => ["FOREGONE_CONCLUSION_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.ForegoneConclusionPower>(), (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
}

/// <summary><c>TheSmith</c>: CardOnPlaySupport forges the Forge variable.</summary>
[FlashContent("THE_SMITH")]
public sealed class TheSmith : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 0));
}

/// <summary>
/// <c>IAmInvincible</c>: the inferred recipe gains Block; AfterAutoPostPlayPhaseEnteredMirrors then
/// auto-plays one card from the top of the draw pile when this card is that top card.
/// </summary>
[FlashContent("I_AM_INVINCIBLE")]
public sealed class IAmInvincible : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override void AfterAutoPostPlayPhaseEntered(ref FlashExec x, FlashListener self)
    {
        if (self.Kind != FlashListenerKind.Card || self.Owner != FlashLimits.PlayerOwner)
            return;
        // DrawPile.TopCard?.References(card): the top of the draw pile is this card. Flash has no
        // instance identity, so the hook does not test the listener's own position (it can be stale
        // after an earlier copy was auto-played); instead it checks the current top value. Every copy
        // anywhere is a listener, and there is at least one listener per draw-pile copy, so all
        // consecutive I Am Invincible cards at the top are auto-played exactly once each.
        if (x.B.DrawCount == 0)
            return;
        ref readonly FlashCard top = ref FlashPiles.At(ref x.B, FlashPile.Draw, 0);
        if (top.Id != FlashRegistry.CardId<IAmInvincible>())
            return;
        FlashKernel.AutoPlayFromDrawPile(ref x, 1, forceExhaust: false);
    }
}

/// <summary>
/// <c>BigBang</c> (Exhaust): the inferred recipe draws the Cards variable, then
/// CardEffectSpecRegistry's <c>BigBang</c> gains Energy, gains Stars and forges, in that order.
/// </summary>
[FlashContent("BIG_BANG")]
public sealed class BigBang : ForgeCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Energy", "Stars", "Forge"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 1));
        if (!FlashKernel.GainStars(ref x, (int)FlashCardEffects.Var(x, 2)))
            return;
        FlashKernel.Forge(ref x, (int)FlashCardEffects.Var(x, 3));
    }
}
