namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Regent;

// Regent card choices. Each definition follows CardChoiceSupport.GetSpec/Apply/ApplyPostChoiceEffects
// for the card and the existing-engine mirror that runs before the choice:
// - CosmicIndifference: CardOnPlayInferrer block + Spec(MoveToDrawTop, Discard).
// - PhotonCut: inferred attack and draw + Spec(MoveToDrawTop, Hand, PutBack).
// - HeirloomHammer: inferred attack + Spec(Duplicate, colorless hand, Repeat).
// - DecisionsDecisions: inferred draw + Spec(AutoPlayRepeated, skill hand).
// - Glimmer: inferred draw + Spec(MoveToDrawTop, Hand, PutBack).
// - Charge / Guards / Begone: CardChoiceSupport Spec + CardChoiceResolution.TransformSelectedCards
//   with the canonical Minion replacement.

/// <summary>
/// <c>CosmicIndifference</c>: block, then choose one discard card and put it on top of the draw pile
/// (<c>CardChoiceSupport.GetSpec</c> CosmicIndifference → <c>Apply</c> MoveToDrawTop).
/// </summary>
[FlashContent("COSMIC_INDIFFERENCE")]
public sealed class CosmicIndifference : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToDrawTop, FlashPile.Discard, options, 1,
                selected, out int chosen))
        {
            return;
        }
        FlashKernel.MoveSelected(ref x.B, FlashPile.Discard, selected[..chosen], FlashPile.Draw,
            FlashPilePosition.Top);
    }
}

/// <summary>
/// <c>PhotonCut</c>: attack, draw the Cards variable, then choose the PutBack number of hand cards and
/// put them on top of the draw pile. The played card is in the play pile, so every hand card is an
/// option.
/// </summary>
[FlashContent("PHOTON_CUT")]
public sealed class PhotonCut : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards", "PutBack"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }

    public override void OwnChoice(ref FlashExec x)
    {
        int count = Math.Min((int)FlashCardEffects.Var(x, 2), x.B.HandCount);
        if (count <= 0)
            return;
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToDrawTop, FlashPile.Hand, options, count,
                selected, out int chosen))
        {
            return;
        }
        FlashKernel.MoveSelected(ref x.B, FlashPile.Hand, selected[..chosen], FlashPile.Draw,
            FlashPilePosition.Top);
    }
}

/// <summary>
/// <c>Glimmer</c>: draw the Cards variable, then choose one hand card and put it on top of the draw
/// pile.
/// </summary>
[FlashContent("GLIMMER")]
public sealed class Glimmer : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "PutBack"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToDrawTop, FlashPile.Hand, options, 1,
                selected, out int chosen))
        {
            return;
        }
        FlashKernel.MoveSelected(ref x.B, FlashPile.Hand, selected[..chosen], FlashPile.Draw,
            FlashPilePosition.Top);
    }
}

/// <summary>
/// <c>HeirloomHammer</c>: the inferred attack, then choose one colorless hand card and add Repeat
/// clones of it to hand (<c>CardChoiceResolution.DuplicateSelectedCard</c>: <c>CreateClone</c> per
/// copy, bottom of the hand, fixed result kind).
/// </summary>
[FlashContent("HEIRLOOM_HAMMER")]
public sealed class HeirloomHammer : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            if (x.R.Card(card.Id, card.Upgrade).IsColorless)
                options[count++] = index;
        }
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Duplicate, FlashPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        if (chosen == 0)
            return;
        FlashCard original = FlashPiles.At(ref x.B, FlashPile.Hand, selected[0]);
        int copies = (int)FlashCardEffects.Var(x, 1);
        for (int index = 0; index < copies; index++)
        {
            FlashCard clone = FlashKernel.CloneCard(original);
            if (!FlashKernel.AddGeneratedCard(ref x, clone, FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>DecisionsDecisions</c>: draw the Cards variable, choose one non-unplayable Skill in hand, then
/// auto-play it Repeat times (<c>CardChoiceResolution.AutoPlayRepeated</c> →
/// <c>ContinueRepeatedSelection</c>). The card is re-located before each auto-play because the
/// previous play may have moved or changed it.
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
        FlashCard repeated = FlashPiles.At(ref x.B, FlashPile.Hand, selected[0]);
        // The selected hand position is the card's identity for the first play; later plays follow
        // the result position captured after each auto-play (equal copies elsewhere must not be
        // picked instead).
        FlashPile repeatedPile = FlashPile.Hand;
        int repeatedIndex = selected[0];
        bool repeatedLocated = true;
        int repeats = (int)FlashCardEffects.Var(x, 1);
        for (int index = 0; index < repeats; index++)
        {
            bool tracked = repeatedLocated && repeatedIndex >= 0
                && repeatedIndex < FlashPiles.Count(x.B, repeatedPile)
                && FlashFingerprint.Equal(FlashPiles.At(ref x.B, repeatedPile, repeatedIndex), repeated);
            FlashPile pile = repeatedPile;
            int position = repeatedIndex;
            if (!tracked && !TryLocate(x.B, repeated, out pile, out position))
                throw new FlashUnsupportedException("重复自动打出的卡牌在结算前离开了战斗牌堆。");
            bool played = FlashKernel.AutoPlay(ref x, pile, position, FlashLimits.NoOwner, skipXCapture: false,
                out FlashCard playedCard, out FlashPile resultPile, out int resultIndex);
            if (x.NeedsChoice)
                return;
            if (!played)
                break;
            // The play may have changed the card (AddCurrentCardVar writes the play-pile copy); use
            // the post-tail value and its result position for the next iteration's location.
            repeated = playedCard;
            if (resultIndex >= 0 && resultIndex < FlashPiles.Count(x.B, resultPile)
                && FlashFingerprint.Equal(FlashPiles.At(ref x.B, resultPile, resultIndex), playedCard))
            {
                repeatedPile = resultPile;
                repeatedIndex = resultIndex;
                repeatedLocated = true;
            }
            else
            {
                repeatedLocated = false;
            }
        }
    }

    /// <summary>
    /// Pile and position of a card value. Equal copies are interchangeable (the byte state after the
    /// play is the same whichever one is played), so the first equal card in combat pile order is the
    /// same card the existing engine would replay when no play changed its value.
    /// </summary>
    private static bool TryLocate(in FlashBattle battle, in FlashCard card, out FlashPile pile, out int position)
    {
        ReadOnlySpan<FlashPile> piles =
            [FlashPile.Hand, FlashPile.Draw, FlashPile.Discard, FlashPile.Exhaust, FlashPile.Play];
        foreach (FlashPile candidate in piles)
        {
            int start = FlashPiles.Start(battle, candidate);
            int count = FlashPiles.Count(battle, candidate);
            for (int offset = 0; offset < count; offset++)
            {
                if (!FlashFingerprint.Equal(battle.Cards[start + offset], card))
                    continue;
                pile = candidate;
                position = offset;
                return true;
            }
        }
        pile = FlashPile.Hand;
        position = -1;
        return false;
    }
}

/// <summary>
/// <c>Charge</c>: choose the Cards number of draw-pile cards and transform each into the canonical
/// Minion Dive Bomb, upgraded when Charge is upgraded
/// (<c>CardChoiceResolution.TransformSelectedCards</c>).
/// </summary>
[FlashContent("CHARGE")]
public sealed class Charge : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("MINION_DIVE_BOMB", 0)];

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        int count = Math.Min((int)FlashCardEffects.Var(x, 0), x.B.DrawCount);
        if (count <= 0)
            return;
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Transform, FlashPile.Draw, options, count,
                selected, out int chosen))
        {
            return;
        }
        TransformSelected(ref x, FlashPile.Draw, selected[..chosen], "MINION_DIVE_BOMB");
    }

    internal static void TransformSelected(ref FlashExec x, FlashPile pile, scoped ReadOnlySpan<int> selected,
        string cardId)
    {
        if (selected.IsEmpty || FlashKernel.IsOverOrEnding(x.B))
            return;
        if (!FlashRegistry.TryCard(cardId, out ushort replacementId))
            throw new FlashUnsupportedException($"变化需要 {cardId} 的 Flash 定义。");
        FlashKernel.TransformSelectedTo(ref x, pile, selected, replacementId, x.Card.Upgrade > 0);
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
/// <c>Begone</c>: choose one hand card and transform it into the canonical Minion Strike, upgraded
/// when Begone is upgraded.
/// </summary>
[FlashContent("BEGONE")]
public sealed class Begone : FlashCardDef
{
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("MINION_STRIKE", 0)];

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Transform, FlashPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        Charge.TransformSelected(ref x, FlashPile.Hand, selected[..chosen], "MINION_STRIKE");
    }
}

/// <summary>
/// <c>Tyranny</c>: applies TyrannyPower 1 (<c>CardPowerOnPlaySupport.SToZ</c>); the power's exhaust
/// choice runs at the player turn start.
/// </summary>
[FlashContent("TYRANNY")]
public sealed class Tyranny : FlashCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["TYRANNY_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.TyrannyPower>(), 1, FlashLimits.PlayerOwner);
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
