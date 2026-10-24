using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Ironclad;

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

[FlashContent("INFERNAL_BLADE")]
public sealed class InfernalBlade : FlashCardDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAttacks];

    public override void OnPlay(ref FlashExec x)
    {
        Span<FlashCard> generated = stackalloc FlashCard[1];
        int count = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.CharacterAttacks, 1,
            distinct: true, FlashRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < count; index++)
        {
            FlashKernel.MakeFreeThisTurn(x.R, ref generated[index]);
            FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Hand);
        }
    }
}

[FlashContent("DISCOVERY")]
public sealed class Discovery : FlashCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void OnPlay(ref FlashExec x)
    {
        Span<FlashCard> options = stackalloc FlashCard[3];
        int count = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.CharacterAll, 3,
            distinct: true, FlashRngStream.CombatCardGeneration, options);
        if (count == 0)
            return;
        for (int index = 0; index < count; index++)
            x.GeneratedOptions[index] = options[index];
        x.GeneratedOptionCount = count;
        // CardGenerationOptions + RecordRisk(UnresolvedPlayerChoice) are compensated for Discovery
        // by CardOnPlayCompensationCatalog, so snapshot.HasRisk remains false; see the Colorless
        // Splash definition for the same reasoning.
    }

    public override void OwnChoice(ref FlashExec x)
    {
        int count = x.GeneratedOptionCount;
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseGeneratedCardsInRange(ref x, FlashChoiceEffect.GenerateToHand,
                x.GeneratedOptions[..count], minCount: 0, maxCount: 1, selected, out int selectedCount))
            return;
        if (selectedCount == 0)
            return;
        // CardChoiceResolution.AddGeneratedSelectionToHand uses PredictedCard.Clone(), which clones
        // the prediction wrapper only and leaves IsClone false; it is not a gameplay clone.
        FlashCard card = x.GeneratedOptions[selected[0]];
        FlashKernel.MakeFreeThisTurn(x.R, ref card);
        FlashKernel.AddGeneratedCard(ref x, card, FlashPile.Hand);
    }
}

[FlashContent("FLAME_BARRIER")]
public sealed class FlameBarrier : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "DamageBack"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FlameBarrierPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("RAGE")]
public sealed class Rage : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RagePower>(),
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("BLOODLETTING")]
public sealed class Bloodletting : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.LoseHpFromCard(ref x, FlashCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 1));
    }
}

[FlashContent("SECOND_WIND")]
public sealed class SecondWind : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        // The native loop walks a snapshot of the non-Attack hand cards. Exhausting only removes
        // from the hand and cards entering the hand are appended, so walking the live hand and
        // stopping after the snapshot count visits the same cards.
        int remaining = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            FlashCard card = x.B.Cards[index];
            if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Attack)
                remaining++;
        }
        for (int index = 0; index < x.B.HandCount && remaining > 0;)
        {
            // Once combat is ending the exhaust and block calls are no-ops, so stopping here leaves
            // the same final state.
            if (FlashKernel.IsOverOrEnding(x.B))
                return;
            FlashCard candidate = x.B.Cards[index];
            if (FlashKernel.CardType(x.B, x.R, candidate) == FlashCardType.Attack)
            {
                index++;
                continue;
            }
            // Exhausting may also append cards to the hand (BurningSticks copies the first exhausted Skill);
            // appended cards come after every snapshot card, so the walk still stops before them. The
            // candidate itself must have reached the exhaust pile.
            int exhaustBefore = x.B.ExhaustCount;
            remaining--;
            FlashKernel.Exhaust(ref x, FlashPile.Hand, index);
            if (x.B.ExhaustCount != exhaustBefore + 1)
                throw new FlashUnsupportedException("SecondWind 的消耗没有把手牌移入消耗堆。");
            if (x.NeedsChoice)
                return;
            FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, FlashCardEffects.Var(x, 0),
                FlashValueProps.Move, fromCard: true);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("EXPECT_A_FIGHT")]
public sealed class ExpectAFight : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref FlashExec x)
    {
        int strength = FlashKernel.GetAmount(x.B, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>());
        decimal block = FlashCardEffects.Var(x, 0)
            + FlashCardEffects.Var(x, 1) * Math.Max(0, strength);
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, block, FlashValueProps.Move, fromCard: true);
    }

    public override bool TryGetCalculatedDamage(ref FlashExec x, in FlashCard self, byte target, out decimal damage)
    {
        int strength = FlashKernel.GetAmount(x.B, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>());
        damage = BodySlam.SelfVar(x, self, 0)
            + BodySlam.SelfVar(x, self, 1) * Math.Max(0, strength);
        return true;
    }
}

[FlashContent("COLOSSUS")]
public sealed class Colossus : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Colossus"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ColossusPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("EVIL_EYE")]
public sealed class EvilEye : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (x.B.Counter(FlashCounter.CardsExhaustedThisTurn) > 0)
            FlashCardEffects.GeneralBlock(ref x, 0);
    }
}

[FlashContent("FORGOTTEN_RITUAL")]
public sealed class ForgottenRitual : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
}

[FlashContent("DRUM_OF_BATTLE")]
public sealed class DrumOfBattle : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Energy"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);

    public override void AfterCardExhausted(ref FlashExec x, FlashListener self, in FlashCard card, bool causedByEthereal)
    {
        // The listener's card slot is the exhausted copy only when that copy is the last card moved
        // into the exhaust pile; other identical copies must not trigger.
        if (x.B.ExhaustCount == 0)
            return;
        int exhaustedPosition = FlashPiles.Start(x.B, FlashPile.Exhaust) + x.B.ExhaustCount - 1;
        if (self.Index != exhaustedPosition || x.B.Cards[exhaustedPosition].Id != Id)
            return;
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        // GeneratePlayCount has no supported ModifyCardPlayCount listeners, so it is one.
        FlashKernel.GainEnergy(ref x, stats.Vars[1] + card.Var1);
    }
}

[FlashContent("BATTLE_TRANCE")]
public sealed class BattleTrance : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // BattleTrance: the inferred owner draw (Cards) runs first, then
    // CorePowerSupport.ApplyCardPowers applies NoDrawPower 1 to the owner.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.NoDrawPower>(),
            1, FlashLimits.PlayerOwner);
    }
}
