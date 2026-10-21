using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

// Discard-driven cards and the Sly auto-play path. Existing engine: CardOnPlayInferrer's draw/attack
// recipes, CardChoiceSupport.GetSpec + CardChoiceResolution's PlanChoiceEffect.Discard/ApplySly,
// CardDrawCardMirrors (CalculatedGamble/EscapePlan/Expertise), CardPileOnPlaySupport (ShadowStep/
// StormOfSteel) and CorePowerSupport (Tactician).

[FlashContent("ACROBATICS")]
public sealed class Acrobatics : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: Acrobatics → Discard, Hand, 1, every hand card.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Discard, FlashPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        FlashKernel.DiscardAndDraw(ref x, FlashPile.Hand, selected[..chosen], 0);
    }
}

[FlashContent("DAGGER_THROW")]
public sealed class DaggerThrow : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers settles newly dead enemies after the mirror attack and
        // before the card-specific draw compensation; HittableEnemies/Hellraiser read that phase.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, 1, fromHandDraw: false);
    }

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: DaggerThrow → Discard, Hand, 1, every hand card.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Discard, FlashPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        FlashKernel.DiscardAndDraw(ref x, FlashPile.Hand, selected[..chosen], 0);
    }
}

[FlashContent("PREPARED")]
public sealed class Prepared : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: Prepared → Discard, Hand, Cards, every hand card. The bounds are clamped to the
        // option count by ChooseCardsInRange, as CardChoiceSupport.BuildChoices does.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[x.B.HandCount];
        int count = (int)FlashCardEffects.Var(x, 0);
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Discard, FlashPile.Hand, options, count, selected,
                out int chosen))
        {
            return;
        }
        FlashKernel.DiscardAndDraw(ref x, FlashPile.Hand, selected[..chosen], 0);
    }
}

[FlashContent("CALCULATED_GAMBLE")]
public sealed class CalculatedGamble : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        // CardDrawCardMirrors.CalculatedGambleOnPlay: DiscardAndDraw(hand, hand.Length), where the
        // draw count is the hand size captured before the discard.
        int hand = x.B.HandCount;
        FlashKernel.DiscardAllInHand(ref x, hand);
    }
}

[FlashContent("REFLEX")]
public sealed class Reflex : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);
}

[FlashContent("TACTICIAN")]
public sealed class Tactician : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
}

[FlashContent("FLICK_FLACK")]
public sealed class FlickFlack : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("SHADOW_STEP")]
public sealed class ShadowStep : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        // CardOnPlaySupport.ShadowStep: discard the whole hand, then apply ShadowStepPower 1.
        FlashKernel.DiscardAllInHand(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.ShadowStepPower>(), 1, FlashLimits.PlayerOwner);
    }
}

[FlashContent("STORM_OF_STEEL")]
public sealed class StormOfSteel : ShivGeneratingCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        // CardPileOnPlaySupport.ReplaceHandWithShivs: discard the whole hand, then add one Shiv per
        // discarded card (upgraded when this card is upgraded).
        int discarded = x.B.HandCount;
        FlashKernel.DiscardAllInHand(ref x, 0);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, discarded, upgraded: x.Card.Upgrade != 0);
    }
}

[FlashContent("HIDDEN_DAGGERS")]
public sealed class HiddenDaggers : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Shivs"];

    // CardOnPlaySupport has no OnPlay for HiddenDaggers: everything is the choice and its
    // ApplyPostChoiceEffects.
    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: Discard, Hand, Cards, every hand card; BuildRequiredEmptyChoice asks an empty
        // Discard choice when the hand is empty. ApplyPostChoiceEffects then adds Shivs Shivs,
        // upgraded when this card is upgraded.
        int cards = (int)FlashCardEffects.Var(x, 0);
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[x.B.HandCount];
        int chosen;
        if (options.IsEmpty)
        {
            // The existing engine's nested-choice sink applies BuildRequiredEmptyChoice without
            // asking during an auto-play (ResolveNestedCardChoice); a manual play asks it.
            if (x.CurrentPlayIsAutoPlay)
            {
                chosen = 0;
            }
            else if (!FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.Discard, FlashPile.Hand, options, 0, 0,
                         selected, out chosen))
            {
                return;
            }
        }
        else if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Discard, FlashPile.Hand, options, cards,
                     selected, out chosen))
        {
            return;
        }
        FlashKernel.DiscardAndDraw(ref x, FlashPile.Hand, selected[..chosen], 0);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)FlashCardEffects.Var(x, 1), upgraded: x.Card.Upgrade != 0);
    }
}

[FlashContent("HAND_TRICK")]
public sealed class HandTrick : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: ApplySly, Hand, 1, hand Skills that are not already Sly this turn. With no
        // option, CardChoiceSupport.Spec returns null and nothing is asked.
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Skill || FlashKernel.IsSlyThisTurn(x.B, card))
                continue;
            options[count++] = index;
        }
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.ApplySly, FlashPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        for (int index = 0; index < chosen; index++)
        {
            x.B.Cards[selected[index]].Flags |= FlashCardFlags.SlyThisTurn;
        }
    }
}

[FlashContent("ESCAPE_PLAN")]
public sealed class EscapePlan : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    // CardDrawCardMirrors.EscapePlanOnPlay: draw one card; gain block only when the single drawn card
    // is a Skill.
    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashKernel.TryDrawOne(ref x, fromHandDraw: false, out FlashCard card))
            return;
        if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Skill)
            return;
        FlashCardEffects.GeneralBlock(ref x, 0);
    }
}

[FlashContent("EXPERTISE")]
public sealed class Expertise : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // CardDrawCardMirrors.ExpertiseOnPlay: draw Cards, then give every drawn card single-turn Retain.
    public override void OnPlay(ref FlashExec x)
    {
        int count = (int)FlashCardEffects.Var(x, 0);
        for (int index = 0; index < count; index++)
        {
            int before = x.B.HandCount;
            if (!FlashKernel.TryDrawOne(ref x, fromHandDraw: false, out FlashCard card))
                return;
            if (x.B.HandCount != before + 1)
                continue;
            ref FlashCard drawn = ref x.B.Cards[x.B.HandCount - 1];
            if (FlashFingerprint.Equal(drawn, card))
            {
                drawn.Flags |= FlashCardFlags.RetainThisTurn;
            }
        }
    }
}
