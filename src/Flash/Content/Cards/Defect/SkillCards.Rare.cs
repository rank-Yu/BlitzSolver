using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Defect;

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

[FlashContent("RAINBOW")]
public sealed class Rainbow : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("LIGHTNING_ORB"));
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("FROST_ORB"));
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("DARK_ORB"));
    }
}

[FlashContent("MULTI_CAST")]
public sealed class MultiCast : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        int count = FlashKernel.ResolveEnergyXValue(ref x);
        if (x.Card.Upgrade > 0)
            count++;
        FlashKernel.EvokeNext(ref x, repeat: count);
    }
}

[FlashContent("SIGNAL_BOOST")]
public sealed class SignalBoost : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SignalBoostPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.SignalBoostPower>(), 0);
}

[FlashContent("VOLTAIC")]
public sealed class Voltaic : FlashCardDef
{
    // OrbCardMirrors.VoltaicOnPlay: CalculatedVarSpecRegistry.CountLightningChannels then channel.
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("LIGHTNING_ORB"),
            x.B.Counter(FlashCounter.LightningChannels));
}

[FlashContent("SUPERCRITICAL")]
public sealed class Supercritical : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
}

[FlashContent("REBOOT")]
public sealed class Reboot : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // CardDrawCardMirrors.RebootOnPlay.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.MoveHandToDraw(ref x);
        if (x.NeedsChoice)
            return;
        FlashKernel.Shuffle(ref x);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);
    }
}

[FlashContent("MODDED")]
public sealed class Modded : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat", "Cards"];

    // Draw is inferred; CardEffectSpecRegistry.Modded adds slots and then +1 cost this combat.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 1), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        FlashKernel.AddOrbSlots(ref x.B, (int)FlashCardEffects.Var(x, 0));
        UpdatePlayedCardCost(ref x);
    }

    private static void UpdatePlayedCardCost(ref FlashExec x)
    {
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("改装没有正在打出的卡。");
        ref FlashCard played = ref FlashPiles.At(ref x.B, FlashPile.Play, position);
        if (played.Id != x.Card.Id)
            throw new InvalidOperationException("打出堆顶不是当前卡。");
        FlashKernel.AddEnergyCostModifier(ref played, 1, FlashCostType.Relative,
            FlashCostExpiration.EndOfCombat, reduceOnly: false);
    }
}
