namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Defect;

// Remaining Defect cards whose existing-engine implementations use per-card private accumulators or
// a batch transform.

[FlashContent("SYNTHESIS")]
public sealed class Synthesis : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry.Synthesis: attack, then one FreePowerPower.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FreePowerPower>(), 1,
            FlashLimits.PlayerOwner);
    }
}

[FlashContent("COOLHEADED")]
public sealed class CoolHeaded : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // OrbCardMirrors.CoolheadedOnPlay: channel Frost, then draw Cards.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("FROST_ORB"));
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);
    }
}

[FlashContent("CLAW")]
public sealed class Claw : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    // CardEffectSpecRegistry.Claw: attack, then every Claw in combat gains Increase damage.
    // ExtraDamageFromClawPlays is carried in Private (ProjectPrivate) so DowngradeCard can restore it.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int increase = (int)FlashCardEffects.Var(x, 1);
        if (increase == 0)
            return;
        if (!FlashRegistry.TryCard("CLAW", out ushort claw))
            return;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref FlashCard card = ref x.B.Cards[position];
            if (card.Id != claw)
                continue;
            AddIncrease(ref card, increase);
        }
    }

    public override void ProjectPrivate(ref FlashCard card) => card.Private = card.Var0;

    public override void AfterDowngraded(ref FlashBattle battle, int position)
    {
        // Claw.AfterDowngraded: DynamicVars.Damage.BaseValue += ExtraDamageFromClawPlays.
        battle.Cards[position].Var0 = battle.Cards[position].Private;
    }

    private static void AddIncrease(ref FlashCard card, int increase)
    {
        if (increase > short.MaxValue - card.Var0 || increase > short.MaxValue - card.Private)
            throw new FlashUnsupportedException("利爪的伤害累加超出卡牌私有计数范围。");
        card.Var0 = (short)(card.Var0 + increase);
        card.Private = (short)(card.Private + increase);
    }
}

[FlashContent("GENETIC_ALGORITHM")]
public sealed class GeneticAlgorithm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Increase"];

    // CardEffectSpecRegistry.GeneticAlgorithm: block, then IncreasedBlock += Increase and
    // CurrentBlock = 1 + IncreasedBlock. The deck version also records long-term growth.
    // IncreasedBlock is carried in Private (ProjectPrivate).
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        int increase = (int)FlashCardEffects.Var(x, 1);
        if (increase <= 0)
            return;
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("基因算法没有正在打出的卡。");
        ref FlashCard played = ref FlashPiles.At(ref x.B, FlashPile.Play, position);
        if (played.Id != x.Card.Id)
            throw new InvalidOperationException("打出堆顶不是当前卡。");
        if (increase > short.MaxValue - played.Var0 || increase > short.MaxValue - played.Private)
            throw new FlashUnsupportedException("基因算法的格挡累加超出卡牌私有计数范围。");
        played.Var0 = (short)(played.Var0 + increase);
        played.Private = (short)(played.Private + increase);
        x.Card = played;
        if ((played.Flags & FlashCardFlags.DeckCard) != 0)
        {
            x.B.AddCounter(FlashCounter.LongTermResourceValue, increase);
            FlashGrowthRewards.Add(ref x.B, FlashGrowthSource.GeneticAlgorithm);
        }
    }

    public override void ProjectPrivate(ref FlashCard card) => card.Private = card.Var0;

    public override void AfterDowngraded(ref FlashBattle battle, int position)
    {
        // GeneticAlgorithm.AfterDowngraded → UpdateBlock: CurrentBlock = 1 + IncreasedBlock. Level 0's
        // canonical Block is 1, so the delta is IncreasedBlock.
        battle.Cards[position].Var0 = battle.Cards[position].Private;
    }
}

[FlashContent("COMPACT")]
public sealed class Compact : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("FUEL", 0), ("FUEL", 1)];

    // CardEffectSpecRegistry.Compact: block, then transform every transformable Status card in hand
    // into Fuel (upgraded when Compact is upgraded). CardChoiceSupport.TransformCardBatch removes all
    // originals before inserting the replacements.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryCard("FUEL", out ushort fuel))
            throw new FlashUnsupportedException("压缩变换需要 FUEL 的 Flash 定义。");
        Span<int> statuses = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            FlashCard card = x.B.Cards[index];
            if ((card.Keywords & FlashKeywords.Eternal) != 0)
                continue;
            if (x.R.Card(card.Id, card.Upgrade).Type != FlashCardType.Status)
                continue;
            statuses[count++] = index;
        }
        if (count == 0)
            return;
        FlashKernel.TransformCards(ref x, FlashPile.Hand, statuses[..count],
            FlashKernel.CreateCard(x.R, fuel), x.Card.Upgrade > 0 ? 1 : 0);
    }
}
