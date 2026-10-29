using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Cards.Defect;

[ContentId("GENETIC_ALGORITHM")]
public sealed class GeneticAlgorithm : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Increase"];

    // CardEffectSpecRegistry.GeneticAlgorithm: block, then IncreasedBlock += Increase and
    // CurrentBlock = 1 + IncreasedBlock. The deck version also records long-term growth.
    // IncreasedBlock is carried in Private (ProjectPrivate).
    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        int increase = (int)CardEffects.Var(x, 1);
        if (increase <= 0)
            return;
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("基因算法没有正在打出的卡。");
        ref Card played = ref Piles.At(ref x.B, Pile.Play, position);
        if (played.Id != x.Card.Id)
            throw new InvalidOperationException("打出堆顶不是当前卡。");
        if (increase > short.MaxValue - played.Var0 || increase > short.MaxValue - played.Private)
            throw new UnsupportedException("基因算法的格挡累加超出卡牌私有计数范围。");
        played.Var0 = (short)(played.Var0 + increase);
        played.Private = (short)(played.Private + increase);
        x.Card = played;
        if ((played.Flags & CardFlags.DeckCard) != 0)
        {
            x.B.AddCounter(Counter.LongTermResourceValue, increase);
            GrowthRewards.Add(ref x.B, GrowthRewardSource.GeneticAlgorithm);
        }
    }

    public override void ProjectPrivate(ref Card card) => card.Private = card.Var0;

    public override void AfterDowngraded(ref Battle battle, int position)
    {
        // GeneticAlgorithm.AfterDowngraded → UpdateBlock: CurrentBlock = 1 + IncreasedBlock. Level 0's
        // canonical Block is 1, so the delta is IncreasedBlock.
        battle.Cards[position].Var0 = battle.Cards[position].Private;
    }
}

[ContentId("RAINBOW")]
public sealed class Rainbow : CardDef
{
    public override void OnPlay(ref Exec x)
    {
        Kernel.ChannelOrb(ref x, Kernel.OrbId("LIGHTNING_ORB"));
        if (x.NeedsChoice)
            return;
        Kernel.ChannelOrb(ref x, Kernel.OrbId("FROST_ORB"));
        if (x.NeedsChoice)
            return;
        Kernel.ChannelOrb(ref x, Kernel.OrbId("DARK_ORB"));
    }
}

[ContentId("MULTI_CAST")]
public sealed class MultiCast : CardDef
{
    public override void OnPlay(ref Exec x)
    {
        int count = Kernel.ResolveEnergyXValue(ref x);
        if (x.Card.Upgrade > 0)
            count++;
        Kernel.EvokeNext(ref x, repeat: count);
    }
}

[ContentId("SIGNAL_BOOST")]
public sealed class SignalBoost : CardDef
{
    public override IReadOnlyList<string> VarNames => ["SignalBoostPower"];

    public override void OnPlay(ref Exec x)
        => CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.SignalBoostPower>(), 0);
}

[ContentId("VOLTAIC")]
public sealed class Voltaic : CardDef
{
    // OrbCardMirrors.VoltaicOnPlay: CalculatedVarSpecRegistry.CountLightningChannels then channel.
    public override void OnPlay(ref Exec x)
        => Kernel.ChannelOrbs(ref x, Kernel.OrbId("LIGHTNING_ORB"),
            x.B.Counter(Counter.LightningChannels));
}

[ContentId("SUPERCRITICAL")]
public sealed class Supercritical : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref Exec x) => Kernel.GainEnergy(ref x, CardEffects.Var(x, 0));
}

[ContentId("REBOOT")]
public sealed class Reboot : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // CardDrawCardMirrors.RebootOnPlay.
    public override void OnPlay(ref Exec x)
    {
        CardEffects.MoveHandToDraw(ref x);
        if (x.NeedsChoice)
            return;
        Kernel.Shuffle(ref x);
        if (x.NeedsChoice)
            return;
        Kernel.Draw(ref x, (int)CardEffects.Var(x, 0), fromHandDraw: false);
    }
}

[ContentId("MODDED")]
public sealed class Modded : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat", "Cards"];

    // Draw is inferred; CardEffectSpecRegistry.Modded adds slots and then +1 cost this combat.
    public override void OnPlay(ref Exec x)
    {
        Kernel.Draw(ref x, (int)CardEffects.Var(x, 1), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        Kernel.AddOrbSlots(ref x.B, (int)CardEffects.Var(x, 0));
        UpdatePlayedCardCost(ref x);
    }

    private static void UpdatePlayedCardCost(ref Exec x)
    {
        int position = x.B.PlayCount - 1;
        if (position < 0)
            throw new InvalidOperationException("改装没有正在打出的卡。");
        ref Card played = ref Piles.At(ref x.B, Pile.Play, position);
        if (played.Id != x.Card.Id)
            throw new InvalidOperationException("打出堆顶不是当前卡。");
        Kernel.AddEnergyCostModifier(ref played, 1, CostType.Relative,
            CostExpiration.EndOfCombat, reduceOnly: false);
    }
}
