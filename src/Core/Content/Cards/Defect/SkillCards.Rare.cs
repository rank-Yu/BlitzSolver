using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[BlitzContent("GENETIC_ALGORITHM")]
public sealed class GeneticAlgorithm : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Increase"];

    // CardEffectSpecRegistry.GeneticAlgorithm: block, then IncreasedBlock += Increase and
    // CurrentBlock = 1 + IncreasedBlock. The deck version also records long-term growth.
    // IncreasedBlock is carried in Private (ProjectPrivate).
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        int increase = (int)BlitzCardEffects.Var(x, 1);
        if (increase <= 0)
            return;
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("基因算法没有正在打出的卡。");
        ref BlitzCard played = ref BlitzPiles.At(ref x.B, BlitzPile.Play, position);
        if (played.Id != x.Card.Id)
            throw new InvalidOperationException("打出堆顶不是当前卡。");
        if (increase > short.MaxValue - played.Var0 || increase > short.MaxValue - played.Private)
            throw new BlitzUnsupportedException("基因算法的格挡累加超出卡牌私有计数范围。");
        played.Var0 = (short)(played.Var0 + increase);
        played.Private = (short)(played.Private + increase);
        x.Card = played;
        if ((played.Flags & BlitzCardFlags.DeckCard) != 0)
        {
            x.B.AddCounter(BlitzCounter.LongTermResourceValue, increase);
            BlitzGrowthRewards.Add(ref x.B, BlitzGrowthSource.GeneticAlgorithm);
        }
    }

    public override void ProjectPrivate(ref BlitzCard card) => card.Private = card.Var0;

    public override void AfterDowngraded(ref BlitzBattle battle, int position)
    {
        // GeneticAlgorithm.AfterDowngraded → UpdateBlock: CurrentBlock = 1 + IncreasedBlock. Level 0's
        // canonical Block is 1, so the delta is IncreasedBlock.
        battle.Cards[position].Var0 = battle.Cards[position].Private;
    }
}

[BlitzContent("RAINBOW")]
public sealed class Rainbow : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("LIGHTNING_ORB"));
        if (x.NeedsChoice)
            return;
        BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("FROST_ORB"));
        if (x.NeedsChoice)
            return;
        BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("DARK_ORB"));
    }
}

[BlitzContent("MULTI_CAST")]
public sealed class MultiCast : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        int count = BlitzKernel.ResolveEnergyXValue(ref x);
        if (x.Card.Upgrade > 0)
            count++;
        BlitzKernel.EvokeNext(ref x, repeat: count);
    }
}

[BlitzContent("SIGNAL_BOOST")]
public sealed class SignalBoost : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["SignalBoostPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.SignalBoostPower>(), 0);
}

[BlitzContent("VOLTAIC")]
public sealed class Voltaic : BlitzCardDef
{
    // OrbCardMirrors.VoltaicOnPlay: CalculatedVarSpecRegistry.CountLightningChannels then channel.
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("LIGHTNING_ORB"),
            x.B.Counter(BlitzCounter.LightningChannels));
}

[BlitzContent("SUPERCRITICAL")]
public sealed class Supercritical : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref BlitzExec x) => BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 0));
}

[BlitzContent("REBOOT")]
public sealed class Reboot : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // CardDrawCardMirrors.RebootOnPlay.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.MoveHandToDraw(ref x);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Shuffle(ref x);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 0), fromHandDraw: false);
    }
}

[BlitzContent("MODDED")]
public sealed class Modded : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat", "Cards"];

    // Draw is inferred; CardEffectSpecRegistry.Modded adds slots and then +1 cost this combat.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 1), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        BlitzKernel.AddOrbSlots(ref x.B, (int)BlitzCardEffects.Var(x, 0));
        UpdatePlayedCardCost(ref x);
    }

    private static void UpdatePlayedCardCost(ref BlitzExec x)
    {
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("改装没有正在打出的卡。");
        ref BlitzCard played = ref BlitzPiles.At(ref x.B, BlitzPile.Play, position);
        if (played.Id != x.Card.Id)
            throw new InvalidOperationException("打出堆顶不是当前卡。");
        BlitzKernel.AddEnergyCostModifier(ref played, 1, BlitzCostType.Relative,
            BlitzCostExpiration.EndOfCombat, reduceOnly: false);
    }
}
