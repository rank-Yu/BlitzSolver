using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Ironclad;

[BlitzContent("BURNING_PACT")]
public sealed class BurningPact : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // CardOnPlayMirrors registers an empty OnPlay; everything happens in the choice stage.
    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        // GetSpec: Exhaust, Hand, 1, every hand card. ApplyPostChoiceEffects then draws; its
        // "source already in discard" branch needs an effect that moves the played card out of the
        // play pile, which no supported content has.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Exhaust, BlitzPile.Hand, options, 1, selected, out int chosen))
            return;
        BlitzKernel.ExhaustSelected(ref x, BlitzPile.Hand, selected[..chosen]);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 0), fromHandDraw: false);
    }
}

[BlitzContent("INFERNAL_BLADE")]
public sealed class InfernalBlade : BlitzCardDef
{
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAttacks];

    public override void OnPlay(ref BlitzExec x)
    {
        Span<BlitzCard> generated = stackalloc BlitzCard[1];
        int count = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.CharacterAttacks, 1,
            distinct: true, BlitzRngStream.CombatCardGeneration, generated);
        for (int index = 0; index < count; index++)
        {
            BlitzKernel.MakeFreeThisTurn(x.R, ref generated[index]);
            BlitzKernel.AddGeneratedCard(ref x, generated[index], BlitzPile.Hand);
        }
    }
}

[BlitzContent("DISCOVERY")]
public sealed class Discovery : BlitzCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAll];

    public override void OnPlay(ref BlitzExec x)
    {
        Span<BlitzCard> options = stackalloc BlitzCard[3];
        int count = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.CharacterAll, 3,
            distinct: true, BlitzRngStream.CombatCardGeneration, options);
        if (count == 0)
            return;
        for (int index = 0; index < count; index++)
            x.GeneratedOptions[index] = options[index];
        x.GeneratedOptionCount = count;
        // CardGenerationOptions + RecordRisk(UnresolvedPlayerChoice) are compensated for Discovery
        // by CardOnPlayCompensationCatalog, so snapshot.HasRisk remains false; see the Colorless
        // Splash definition for the same reasoning.
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        int count = x.GeneratedOptionCount;
        if (count == 0)
            return;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseGeneratedCardsInRange(ref x, BlitzChoiceEffect.GenerateToHand,
                x.GeneratedOptions[..count], minCount: 0, maxCount: 1, selected, out int selectedCount))
            return;
        if (selectedCount == 0)
            return;
        // CardChoiceResolution.AddGeneratedSelectionToHand uses PredictedCard.Clone(), which clones
        // the prediction wrapper only and leaves IsClone false; it is not a gameplay clone.
        BlitzCard card = x.GeneratedOptions[selected[0]];
        BlitzKernel.MakeFreeThisTurn(x.R, ref card);
        BlitzKernel.AddGeneratedCard(ref x, card, BlitzPile.Hand);
    }
}

[BlitzContent("FLAME_BARRIER")]
public sealed class FlameBarrier : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "DamageBack"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.FlameBarrierPower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("RAGE")]
public sealed class Rage : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.RagePower>(),
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("BLOODLETTING")]
public sealed class Bloodletting : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "Energy"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.LoseHpFromCard(ref x, BlitzCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 1));
    }
}

[BlitzContent("SECOND_WIND")]
public sealed class SecondWind : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x)
    {
        // The native loop walks a snapshot of the non-Attack hand cards. Exhausting only removes
        // from the hand and cards entering the hand are appended, so walking the live hand and
        // stopping after the snapshot count visits the same cards.
        int remaining = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            BlitzCard card = x.B.Cards[index];
            if (BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Attack)
                remaining++;
        }
        for (int index = 0; index < x.B.HandCount && remaining > 0;)
        {
            // Once combat is ending the exhaust and block calls are no-ops, so stopping here leaves
            // the same final state.
            if (BlitzKernel.IsOverOrEnding(x.B))
                return;
            BlitzCard candidate = x.B.Cards[index];
            if (BlitzKernel.CardType(x.B, x.R, candidate) == BlitzCardType.Attack)
            {
                index++;
                continue;
            }
            // Exhausting may also append cards to the hand (BurningSticks copies the first exhausted Skill);
            // appended cards come after every snapshot card, so the walk still stops before them. The
            // candidate itself must have reached the exhaust pile.
            int exhaustBefore = x.B.ExhaustCount;
            remaining--;
            BlitzKernel.Exhaust(ref x, BlitzPile.Hand, index);
            if (x.B.ExhaustCount != exhaustBefore + 1)
                throw new BlitzUnsupportedException("SecondWind 的消耗没有把手牌移入消耗堆。");
            if (x.NeedsChoice)
                return;
            BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, BlitzCardEffects.Var(x, 0),
                BlitzValueProps.Move, fromCard: true);
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("EXPECT_A_FIGHT")]
public sealed class ExpectAFight : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref BlitzExec x)
    {
        int strength = BlitzKernel.GetAmount(x.B, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StrengthPower>());
        decimal block = BlitzCardEffects.Var(x, 0)
            + BlitzCardEffects.Var(x, 1) * Math.Max(0, strength);
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, block, BlitzValueProps.Move, fromCard: true);
    }

    public override bool TryGetCalculatedDamage(ref BlitzExec x, in BlitzCard self, byte target, out decimal damage)
    {
        int strength = BlitzKernel.GetAmount(x.B, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StrengthPower>());
        damage = BodySlam.SelfVar(x, self, 0)
            + BodySlam.SelfVar(x, self, 1) * Math.Max(0, strength);
        return true;
    }
}

[BlitzContent("COLOSSUS")]
public sealed class Colossus : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Colossus"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.ColossusPower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("EVIL_EYE")]
public sealed class EvilEye : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (x.B.Counter(BlitzCounter.CardsExhaustedThisTurn) > 0)
            BlitzCardEffects.GeneralBlock(ref x, 0);
    }
}

[BlitzContent("FORGOTTEN_RITUAL")]
public sealed class ForgottenRitual : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 0));
}

[BlitzContent("DRUM_OF_BATTLE")]
public sealed class DrumOfBattle : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards", "Energy"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 0), fromHandDraw: false);

    public override void AfterCardExhausted(ref BlitzExec x, BlitzListener self, in BlitzCard card, bool causedByEthereal)
    {
        // The listener's card slot is the exhausted copy only when that copy is the last card moved
        // into the exhaust pile; other identical copies must not trigger.
        if (x.B.ExhaustCount == 0)
            return;
        int exhaustedPosition = BlitzPiles.Start(x.B, BlitzPile.Exhaust) + x.B.ExhaustCount - 1;
        if (self.Index != exhaustedPosition || x.B.Cards[exhaustedPosition].Id != Id)
            return;
        BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
        // GeneratePlayCount has no supported ModifyCardPlayCount listeners, so it is one.
        BlitzKernel.GainEnergy(ref x, stats.Vars[1] + card.Var1);
    }
}

[BlitzContent("BATTLE_TRANCE")]
public sealed class BattleTrance : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // BattleTrance: the inferred owner draw (Cards) runs first, then
    // CorePowerSupport.ApplyCardPowers applies NoDrawPower 1 to the owner.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 0), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.NoDrawPower>(),
            1, BlitzLimits.PlayerOwner);
    }
}
