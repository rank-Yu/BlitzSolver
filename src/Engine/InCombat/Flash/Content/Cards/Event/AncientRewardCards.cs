namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Event;

// Basic/Ancient event-reward cards (EC batch). They enter the deck through Ancient events and are
// never in a combat generation pool (CardFactory.FilterForCombat excludes Ancient/Event). Existing
// engine paths are named on each definition.

[FlashContent("ABUNDANCE")]
public sealed class Abundance : FlashCardDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterPowers];

    // CardGenerationCardMirrors.AbundanceOnPlay: three distinct unlocked character Power cards,
    // each upgraded once, recorded as generated options; CardChoiceSupport.GetSpec then asks for
    // minCount 1, maxCount 1 GenerateToHand (CardChoiceResolution adds it free this turn).
    public override void OnPlay(ref FlashExec x)
    {
        Span<FlashCard> options = stackalloc FlashCard[3];
        int count = FlashCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, FlashCardType.Power, 3, options,
            FlashCardPools.CharacterPowers);
        if (count == 0)
            return;
        for (int index = 0; index < count; index++)
        {
            FlashKernel.UpgradeCard(x.R, ref options[index]);
            x.GeneratedOptions[index] = options[index];
        }
        x.GeneratedOptionCount = count;
    }

    public override void OwnChoice(ref FlashExec x)
    {
        int count = x.GeneratedOptionCount;
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseGeneratedCardsInRange(ref x, FlashChoiceEffect.GenerateToHand,
                x.GeneratedOptions[..count], minCount: 1, maxCount: 1, selected, out int selectedCount))
        {
            return;
        }
        if (selectedCount == 0)
            return;
        FlashCard card = x.GeneratedOptions[selected[0]];
        FlashKernel.MakeFreeThisTurn(x.R, ref card);
        FlashKernel.AddGeneratedCard(ref x, card, FlashPile.Hand);
    }
}

[FlashContent("APOTHEOSIS")]
public sealed class Apotheosis : FlashCardDef
{
    // CardPileOnPlaySupport.UpgradeAllOtherCards: every other card in the combat piles (AllCards
    // order: hand, draw, discard, exhaust, play) that is upgradable is upgraded once.
    public override void OnPlay(ref FlashExec x)
    {
        int playStart = FlashPiles.Start(x.B, FlashPile.Play);
        int skip = playStart + x.PlayPosition;
        int cardCount = x.B.CardCount;
        for (int position = 0; position < cardCount; position++)
        {
            if (position == skip)
                continue;
            FlashKernel.UpgradeCard(x.R, ref x.B.Cards[position]);
        }
    }
}

[FlashContent("APPARITION")]
public sealed class Apparition : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["IntangiblePower"];

    // CorePowerSupport.ApplyCardPowers: WraithForm or Apparition applies IntangiblePower.
    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.IntangiblePower>(), 0);
}

[FlashContent("BRIGHTEST_FLAME")]
public sealed class BrightestFlame : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["MaxHp", "Energy", "Cards"];

    // Existing engine: CardOnPlayInferrer's draw recipe runs first, then the CardEffectSpecRegistry
    // BrightestFlame case (GainEnergy, then the isFromCard max-HP loss). The native OnPlay gains
    // energy before drawing; the existing engine's order is authoritative here.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 2), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        FlashKernel.LoseMaxHp(ref x, FlashLimits.PlayerOwner, (int)FlashCardEffects.Var(x, 0),
            FlashValueProps.Unblockable | FlashValueProps.Unpowered | FlashValueProps.Move);
    }
}

[FlashContent("FORBIDDEN_GRIMOIRE")]
public sealed class ForbiddenGrimoire : FlashCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["FORBIDDEN_GRIMOIRE_POWER"];

    // CardPowerOnPlaySupport.ForbiddenGrimoire: Apply<ForbiddenGrimoirePower>(owner, 1, owner),
    // RecordLongTermResource(50) and a growth reward.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.ForbiddenGrimoirePower>(), 1, FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        x.B.AddCounter(FlashCounter.LongTermResourceValue, 50);
        FlashGrowthRewards.Add(ref x.B, FlashGrowthSource.ForbiddenGrimoire);
    }
}

[FlashContent("MAUL")]
public sealed class Maul : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    // BespokeCardMirrors.MaulOnPlay: two attack hits, then every Maul card in AllCards gets
    // DynamicVars.Damage += Increase and _extraDamageFromMaulPlays += Increase. Flash keeps the
    // Damage offset in Var0 and the private accumulator in Private.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hitCount: 2);
        if (x.NeedsChoice)
            return;
        int increase = (int)FlashCardEffects.Var(x, 1);
        if (increase < 0)
            throw new FlashUnsupportedException($"Maul 的 Increase 变量 {increase} 为负。");
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref FlashCard card = ref x.B.Cards[position];
            if (card.Id != x.Card.Id)
                continue;
            if (card.Private > short.MaxValue - increase || card.Var0 > short.MaxValue - increase)
                throw new FlashCapacityException("Maul.Increase", short.MaxValue);
            card.Var0 = (short)(card.Var0 + increase);
            card.Private = (short)(card.Private + increase);
        }
    }

    // Native AfterDowngraded re-adds the private accumulator after DowngradeInternal replaced the
    // dynamic variables with a fresh canonical set (Damage = 5 + _extraDamageFromMaulPlays).
    public override void AfterDowngraded(ref FlashBattle battle, int position)
        => battle.Cards[position].Var0 = battle.Cards[position].Private;

    // The existing engine's Maul counter is only ever changed together with the Damage variable, so
    // the imported variable delta is the accumulator (as for Claw/GeneticAlgorithm).
    public override void ProjectPrivate(ref FlashCard card) => card.Private = card.Var0;
}

[FlashContent("NEOWS_FURY")]
public sealed class NeowsFury : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    // Inferred attack; CardChoiceSupport.GetSpec's NeowsFury case asks for up to Cards cards from
    // the discard pile (min 0) limited by the remaining hand capacity, then moves them to hand.
    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        int room = x.B.MaxHandSize - x.B.HandCount;
        int maximum = Math.Min((int)FlashCardEffects.Var(x, 1), room);
        if (maximum <= 0)
            return;
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[maximum];
        if (!FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Discard,
                options, minCount: 0, maxCount: maximum, selected, out int selectedCount))
        {
            return;
        }
        FlashKernel.MoveSelected(ref x.B, FlashPile.Discard, selected[..selectedCount], FlashPile.Hand,
            FlashPilePosition.Bottom);
    }
}

[FlashContent("RELAX")]
public sealed class Relax : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards", "Energy"];

    // Inferred block recipe, then CardEffectSpecRegistry.Relax's DrawCardsNextTurnPower and
    // EnergyNextTurnPower.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.DrawCardsNextTurnPower>(), (int)FlashCardEffects.Var(x, 1),
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(), (int)FlashCardEffects.Var(x, 2),
            FlashLimits.PlayerOwner);
    }
}

[FlashContent("SUPPRESS")]
public sealed class Suppress : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

    // Inferred attack; CorePowerSupport.ApplyCardPowers applies Weak after the enemy-death pass.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[FlashContent("WHISTLE")]
public sealed class Whistle : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // Inferred attack; CardEffectSpecRegistry.Whistle calls combat.ForceStunnedMove(target) with the
    // default follow-up (the most recent logged move, or the current node).
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (x.Target == FlashLimits.NoOwner)
            throw new FlashUnsupportedException("Whistle 需要目标才能眩晕。");
        FlashKernel.ForceStunnedMove(ref x, x.Target, -1);
    }
}

[FlashContent("WISH")]
public sealed class Wish : FlashCardDef
{
    // CardChoiceSupport.GetSpec's Wish case: MoveToHand, Draw, exactly one of every draw-pile card.
    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[Math.Max(1, options.Length)];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Draw, options, 1,
                selected, out int chosen))
        {
            return;
        }
        FlashKernel.MoveSelected(ref x.B, FlashPile.Draw, selected[..chosen], FlashPile.Hand,
            FlashPilePosition.Bottom);
    }
}

[FlashContent("WRAITH_FORM")]
public sealed class WraithForm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["IntangiblePower", "WraithFormPower"];

    // CorePowerSupport.ApplyCardPowers: IntangiblePower then WraithFormPower.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.IntangiblePower>(), 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.WraithFormPower>(), 1);
    }
}
