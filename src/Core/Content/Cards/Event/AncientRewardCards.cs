using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Event;

[BlitzContent("ABUNDANCE")]
public sealed class Abundance : BlitzCardDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterPowers];

    // CardGenerationCardMirrors.AbundanceOnPlay: three distinct unlocked character Power cards,
    // each upgraded once, recorded as generated options; CardChoiceSupport.GetSpec then asks for
    // minCount 1, maxCount 1 GenerateToHand (CardChoiceResolution adds it free this turn).
    public override void OnPlay(ref BlitzExec x)
    {
        Span<BlitzCard> options = stackalloc BlitzCard[3];
        int count = BlitzCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, BlitzCardType.Power, 3, options,
            BlitzCardPools.CharacterPowers);
        if (count == 0)
            return;
        for (int index = 0; index < count; index++)
        {
            BlitzKernel.UpgradeCard(x.R, ref options[index]);
            x.GeneratedOptions[index] = options[index];
        }
        x.GeneratedOptionCount = count;
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        int count = x.GeneratedOptionCount;
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseGeneratedCardsInRange(ref x, BlitzChoiceEffect.GenerateToHand,
                x.GeneratedOptions[..count], minCount: 1, maxCount: 1, selected, out int selectedCount))
        {
            return;
        }
        if (selectedCount == 0)
            return;
        BlitzCard card = x.GeneratedOptions[selected[0]];
        BlitzKernel.MakeFreeThisTurn(x.R, ref card);
        BlitzKernel.AddGeneratedCard(ref x, card, BlitzPile.Hand);
    }
}

[BlitzContent("APOTHEOSIS")]
public sealed class Apotheosis : BlitzCardDef
{
    // CardPileOnPlaySupport.UpgradeAllOtherCards: every other card in the combat piles (AllCards
    // order: hand, draw, discard, exhaust, play) that is upgradable is upgraded once.
    public override void OnPlay(ref BlitzExec x)
    {
        int playStart = BlitzPiles.Start(x.B, BlitzPile.Play);
        int skip = playStart + x.PlayPosition;
        int cardCount = x.B.CardCount;
        for (int position = 0; position < cardCount; position++)
        {
            if (position == skip)
                continue;
            BlitzKernel.UpgradeCard(x.R, ref x.B.Cards[position]);
        }
    }
}

[BlitzContent("APPARITION")]
public sealed class Apparition : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["IntangiblePower"];

    // CorePowerSupport.ApplyCardPowers: WraithForm or Apparition applies IntangiblePower.
    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.IntangiblePower>(), 0);
}

[BlitzContent("BRIGHTEST_FLAME")]
public sealed class BrightestFlame : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["MaxHp", "Energy", "Cards"];

    // Existing engine: CardOnPlayInferrer's draw recipe runs first, then the CardEffectSpecRegistry
    // BrightestFlame case (GainEnergy, then the isFromCard max-HP loss). The native OnPlay gains
    // energy before drawing; the existing engine's order is authoritative here.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 2), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        BlitzKernel.LoseMaxHp(ref x, BlitzLimits.PlayerOwner, (int)BlitzCardEffects.Var(x, 0),
            BlitzValueProps.Unblockable | BlitzValueProps.Unpowered | BlitzValueProps.Move);
    }
}

[BlitzContent("FORBIDDEN_GRIMOIRE")]
public sealed class ForbiddenGrimoire : BlitzCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["FORBIDDEN_GRIMOIRE_POWER"];

    // CardPowerOnPlaySupport.ForbiddenGrimoire: Apply<ForbiddenGrimoirePower>(owner, 1, owner),
    // RecordLongTermResource(50) and a growth reward.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.ForbiddenGrimoirePower>(), 1, BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        x.B.AddCounter(BlitzCounter.LongTermResourceValue, 50);
        BlitzGrowthRewards.Add(ref x.B, BlitzGrowthSource.ForbiddenGrimoire);
    }
}

[BlitzContent("MAUL")]
public sealed class Maul : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    // BespokeCardMirrors.MaulOnPlay: two attack hits, then every Maul card in AllCards gets
    // DynamicVars.Damage += Increase and _extraDamageFromMaulPlays += Increase. Blitz keeps the
    // Damage offset in Var0 and the private accumulator in Private.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), hitCount: 2);
        if (x.NeedsChoice)
            return;
        int increase = (int)BlitzCardEffects.Var(x, 1);
        if (increase < 0)
            throw new BlitzUnsupportedException($"Maul 的 Increase 变量 {increase} 为负。");
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref BlitzCard card = ref x.B.Cards[position];
            if (card.Id != x.Card.Id)
                continue;
            if (card.Private > short.MaxValue - increase || card.Var0 > short.MaxValue - increase)
                throw new BlitzCapacityException("Maul.Increase", short.MaxValue);
            card.Var0 = (short)(card.Var0 + increase);
            card.Private = (short)(card.Private + increase);
        }
    }

    // Native AfterDowngraded re-adds the private accumulator after DowngradeInternal replaced the
    // dynamic variables with a fresh canonical set (Damage = 5 + _extraDamageFromMaulPlays).
    public override void AfterDowngraded(ref BlitzBattle battle, int position)
        => battle.Cards[position].Var0 = battle.Cards[position].Private;

    // The existing engine's Maul counter is only ever changed together with the Damage variable, so
    // the imported variable delta is the accumulator (as for Claw/GeneticAlgorithm).
    public override void ProjectPrivate(ref BlitzCard card) => card.Private = card.Var0;
}

[BlitzContent("NEOWS_FURY")]
public sealed class NeowsFury : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    // Inferred attack; CardChoiceSupport.GetSpec's NeowsFury case asks for up to Cards cards from
    // the discard pile (min 0) limited by the remaining hand capacity, then moves them to hand.
    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref BlitzExec x)
    {
        int room = x.B.MaxHandSize - x.B.HandCount;
        int maximum = Math.Min((int)BlitzCardEffects.Var(x, 1), room);
        if (maximum <= 0)
            return;
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[maximum];
        if (!BlitzKernel.ChooseCardsInRange(ref x, BlitzChoiceEffect.MoveToHand, BlitzPile.Discard,
                options, minCount: 0, maxCount: maximum, selected, out int selectedCount))
        {
            return;
        }
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Discard, selected[..selectedCount], BlitzPile.Hand,
            BlitzPilePosition.Bottom);
    }
}

[BlitzContent("RELAX")]
public sealed class Relax : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards", "Energy"];

    // Inferred block recipe, then CardEffectSpecRegistry.Relax's DrawCardsNextTurnPower and
    // EnergyNextTurnPower.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.DrawCardsNextTurnPower>(), (int)BlitzCardEffects.Var(x, 1),
            BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.EnergyNextTurnPower>(), (int)BlitzCardEffects.Var(x, 2),
            BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("SUPPRESS")]
public sealed class Suppress : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

    // Inferred attack; CorePowerSupport.ApplyCardPowers applies Weak after the enemy-death pass.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[BlitzContent("WHISTLE")]
public sealed class Whistle : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // Inferred attack; CardEffectSpecRegistry.Whistle calls combat.ForceStunnedMove(target) with the
    // default follow-up (the most recent logged move, or the current node).
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (x.Target == BlitzLimits.NoOwner)
            throw new BlitzUnsupportedException("Whistle 需要目标才能眩晕。");
        BlitzKernel.ForceStunnedMove(ref x, x.Target, -1);
    }
}

[BlitzContent("WISH")]
public sealed class Wish : BlitzCardDef
{
    // CardChoiceSupport.GetSpec's Wish case: MoveToHand, Draw, exactly one of every draw-pile card.
    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[Math.Max(1, options.Length)];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.MoveToHand, BlitzPile.Draw, options, 1,
                selected, out int chosen))
        {
            return;
        }
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Draw, selected[..chosen], BlitzPile.Hand,
            BlitzPilePosition.Bottom);
    }
}

[BlitzContent("WRAITH_FORM")]
public sealed class WraithForm : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["IntangiblePower", "WraithFormPower"];

    // CorePowerSupport.ApplyCardPowers: IntangiblePower then WraithFormPower.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.IntangiblePower>(), 0);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.WraithFormPower>(), 1);
    }
}
