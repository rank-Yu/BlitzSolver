namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Ironclad;

// Cards with their own choice. OnPlay holds the existing engine's OnPlay mirror (or inferred recipe)
// and compensation; OwnChoice holds CardChoiceSupport.GetSpec, Apply and ApplyPostChoiceEffects. Every
// spec here is built by CardChoiceSupport.Spec, which asks nothing when there are no options; the
// post-choice effects still run (ApplyNoChoiceEffects), except for the cards in
// CardChoiceSupport.BuildRequiredEmptyChoice, which ask an empty choice instead.

[FlashContent("ARMAMENTS")]
public sealed class Armaments : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        // Inferred BlockVar recipe, then CorePowerSupport: the upgraded card upgrades every upgradable
        // hand card in hand order.
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice || x.Card.Upgrade == 0)
            return;
        for (int index = 0; index < x.B.HandCount; index++)
            FlashKernel.UpgradeCard(x.R, ref x.B.Cards[index]);
    }

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: Armaments when !IsUpgraded → Upgrade, Hand, 1, upgradable hand cards.
        if (x.Card.Upgrade != 0)
            return;
        Span<int> options = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            if (card.Upgrade < x.R.Card(card.Id, card.Upgrade).MaxUpgradeLevel)
                options[count++] = index;
        }
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Upgrade, FlashPile.Hand, options[..count], 1, selected, out int chosen))
            return;
        FlashKernel.UpgradeSelected(ref x, FlashPile.Hand, selected[..chosen]);
    }
}

[FlashContent("HEADBUTT")]
public sealed class Headbutt : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: MoveToDrawTop, Discard, 1, the discard pile without the played card (which is in
        // the play pile during resolution).
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToDrawTop, FlashPile.Discard, options, 1, selected, out int chosen))
            return;
        FlashKernel.MoveSelected(ref x.B, FlashPile.Discard, selected[..chosen], FlashPile.Draw, FlashPilePosition.Top);
    }
}

[FlashContent("BURNING_PACT")]
public sealed class BurningPact : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // CardOnPlayMirrors registers an empty OnPlay; everything happens in the choice stage.
    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: Exhaust, Hand, 1, every hand card. ApplyPostChoiceEffects then draws; its
        // "source already in discard" branch needs an effect that moves the played card out of the
        // play pile, which no supported content has.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand, options, 1, selected, out int chosen))
            return;
        FlashKernel.ExhaustSelected(ref x, FlashPile.Hand, selected[..chosen]);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);
    }
}

[FlashContent("TRUE_GRIT")]
public sealed class TrueGrit : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        // CardSelectionCardMirrors.TrueGritOnPlay: block, then the base card exhausts a random hand
        // card (Rng.CombatCardSelection.NextItem; no draw from an empty hand).
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice || x.Card.Upgrade != 0 || x.B.HandCount == 0)
            return;
        int pick = x.B.RngStream(FlashRngStream.CombatCardSelection).NextInt(0, x.B.HandCount);
        FlashKernel.Exhaust(ref x, FlashPile.Hand, pick);
    }

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: TrueGrit when IsUpgraded → Exhaust, Hand, 1, every hand card.
        if (x.Card.Upgrade == 0)
            return;
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand, options, 1, selected, out int chosen))
            return;
        FlashKernel.ExhaustSelected(ref x, FlashPile.Hand, selected[..chosen]);
    }
}

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
        int chosen;
        bool answered = options.IsEmpty
            ? FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand, options, 0, 0, selected, out chosen)
            : FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand, options, 1, selected, out chosen);
        if (!answered)
            return;
        FlashKernel.ExhaustSelected(ref x, FlashPile.Hand, selected[..chosen]);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}
