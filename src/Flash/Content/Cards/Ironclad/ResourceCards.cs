using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Ironclad;

// Existing engine paths for this batch:
// - Impervious infers the BlockVar recipe (CardEffectRecipe); Flash only gains the block.
// - Offering is an explicit CardDrawCardMirrors.OfferingOnPlay registration: self damage with the
//   played card as source, then energy, then draw.
// - Bloodletting is a CardOnPlaySupport case: self damage with the played card as source, then
//   energy.
// - SecondWind is BespokeCardMirrors.SecondWindOnPlay: snapshot the non-Attack hand cards, exhaust
//   each, and gain the BlockVar once per exhausted card.
// - ExpectAFight is CalculatedVarSpecRegistry.CalculatedBlock: CalculationBase +
//   CalculationExtra * max(0, StrengthPower amount), gained as Move block.
// - Dominate is a CorePowerSupport case: apply Vulnerable to the target, then apply Strength equal
//   to the target's resulting Vulnerable amount.

[FlashContent("IMPERVIOUS")]
public sealed class Impervious : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("OFFERING")]
public sealed class Offering : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "Energy", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.LoseHpFromCard(ref x, FlashCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 2), fromHandDraw: false);
    }
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

[FlashContent("DOMINATE")]
public sealed class Dominate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        ushort vulnerable = FlashRegistry.PowerId<Powers.VulnerablePower>();
        FlashKernel.ApplyPower(ref x, x.Target, vulnerable, (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.Dominate resolves queued power changes (Vicious) before reading the
        // target's resulting Vulnerable amount.
        FlashKernel.ResolvePowerAmountChanges(ref x);
        if (x.NeedsChoice)
            return;
        int amount = FlashKernel.GetAmount(x.B, x.Target, vulnerable);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            amount, FlashLimits.PlayerOwner);
    }
}
