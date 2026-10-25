using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[BlitzContent("COMPACT")]
public sealed class Compact : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("FUEL", 0), ("FUEL", 1)];

    // CardEffectSpecRegistry.Compact: block, then transform every transformable Status card in hand
    // into Fuel (upgraded when Compact is upgraded). CardChoiceSupport.TransformCardBatch removes all
    // originals before inserting the replacements.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!BlitzRegistry.TryCard("FUEL", out ushort fuel))
            throw new BlitzUnsupportedException("压缩变换需要 FUEL 的 Blitz 定义。");
        Span<int> statuses = stackalloc int[x.B.HandCount];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            BlitzCard card = x.B.Cards[index];
            if ((card.Keywords & BlitzKeywords.Eternal) != 0)
                continue;
            if (BlitzKernel.CardType(x.B, x.R, card) != BlitzCardType.Status)
                continue;
            statuses[count++] = index;
        }
        if (count == 0)
            return;
        BlitzKernel.TransformCards(ref x, BlitzPile.Hand, statuses[..count],
            BlitzKernel.CreateCard(x.R, fuel), x.Card.Upgrade > 0 ? 1 : 0);
    }
}

[BlitzContent("GLACIER")]
public sealed class Glacier : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("FROST_ORB"), 2);
    }
}

[BlitzContent("GLASSWORK")]
public sealed class Glasswork : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("GLASS_ORB"));
    }
}

[BlitzContent("FUSION")]
public sealed class Fusion : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("PLASMA_ORB"));
}

[BlitzContent("CHILL")]
public sealed class Chill : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        // OrbCardMirrors.ChillOnPlay: one Frost per hittable enemy, counted when the card resolves.
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (BlitzKernel.IsHittable(x.B, owner))
                count++;
        }
        BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("FROST_ORB"), count);
    }
}

[BlitzContent("CHAOS")]
public sealed class Chaos : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnPlay(ref BlitzExec x)
    {
        int count = (int)BlitzCardEffects.Var(x, 0);
        for (int index = 0; index < count; index++)
        {
            ushort orb = BlitzKernel.RandomOrb(ref x.B);
            if (!BlitzKernel.ChannelOrb(ref x, orb))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("DARKNESS")]
public sealed class Darkness : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("DARK_ORB"));
        if (x.NeedsChoice)
            return;
        // OrbCardMirrors.DarknessOnPlay: every Dark orb in the resulting queue triggers Passive once
        // (twice when upgraded). OrbCmd.Passive, so no ModifyOrbPassiveTriggerCounts.
        Span<int> dark = stackalloc int[BlitzLimits.Orbs];
        int count = DarkIndices(x.B, dark);
        int triggers = x.Card.Upgrade > 0 ? 2 : 1;
        for (int index = 0; index < count; index++)
        {
            for (int trigger = 0; trigger < triggers; trigger++)
            {
                BlitzKernel.OrbPassiveAt(ref x, dark[index]);
                if (x.NeedsChoice)
                    return;
            }
        }
    }

    private static int DarkIndices(in BlitzBattle battle, Span<int> output)
    {
        if (!BlitzRegistry.TryOrb("DARK_ORB", out ushort id))
            return 0;
        int count = 0;
        for (int index = 0; index < battle.OrbCount; index++)
        {
            if (battle.Orbs[index].Id == id)
                output[count++] = index;
        }
        return count;
    }
}

[BlitzContent("TEMPEST")]
public sealed class Tempest : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        int count = BlitzKernel.ResolveEnergyXValue(ref x);
        if (x.Card.Upgrade > 0)
            count++;
        BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("LIGHTNING_ORB"), count);
    }
}

[BlitzContent("OVERCLOCK")]
public sealed class Overclock : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("BURN", 0)];

    // Draw is inferred; CardEffectSpecRegistry's generation adds one Burn to the discard pile.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 0), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        if (!BlitzRegistry.TryCard("BURN", out ushort burn))
            throw new BlitzUnsupportedException("过载生成需要 BURN 的 Blitz 定义。");
        BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CreateCard(x.R, burn), BlitzPile.Discard);
    }
}

[BlitzContent("SHADOW_SHIELD")]
public sealed class ShadowShield : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("DARK_ORB"));
    }
}

[BlitzContent("BOOT_SEQUENCE")]
public sealed class BootSequence : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);
}

[BlitzContent("SKIM")]
public sealed class Skim : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 0), fromHandDraw: false);
}

[BlitzContent("DOUBLE_ENERGY")]
public sealed class DoubleEnergy : BlitzCardDef
{
    // CardOnPlaySupport.DoubleEnergy: GainEnergy(playerState.Energy) after the card's own cost.
    public override void OnPlay(ref BlitzExec x) => BlitzKernel.GainEnergy(ref x, x.B.Energy);
}

[BlitzContent("SCAVENGE")]
public sealed class Scavenge : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        // GetSpec: Exhaust, Hand, 1, every hand card; BuildRequiredEmptyChoice asks an empty choice when
        // the hand is empty (ResolveNestedCardChoice applies it directly during an auto-play). The
        // EnergyNextTurn application is ApplyPostChoiceEffects, after the exhaust.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        int chosen = 0;
        if (options.IsEmpty)
        {
            if (!x.CurrentPlayIsAutoPlay
                && !BlitzKernel.ChooseCardsInRange(ref x, BlitzChoiceEffect.Exhaust, BlitzPile.Hand, options, 0, 0,
                    selected, out chosen))
            {
                return;
            }
        }
        else if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Exhaust, BlitzPile.Hand, options, 1, selected,
                     out chosen))
        {
            return;
        }
        if (chosen > 0)
            BlitzKernel.ExhaustSelected(ref x, BlitzPile.Hand, selected[..chosen]);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.EnergyNextTurnPower>(), 0);
    }
}

[BlitzContent("FIGHT_THROUGH")]
public sealed class FightThrough : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("WOUND", 0)];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!BlitzRegistry.TryCard("WOUND", out ushort id))
            throw new BlitzUnsupportedException("突破需要 WOUND 的 Blitz 定义。");
        for (int index = 0; index < 2; index++)
        {
            if (!BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CreateCard(x.R, id), BlitzPile.Discard))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("WHITE_NOISE")]
public sealed class WhiteNoise : BlitzCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterPowers];

    // CardGenerationCardMirrors.WhiteNoiseOnPlay: one free distinct Power card to hand.
    public override void OnPlay(ref BlitzExec x)
    {
        Span<BlitzCard> generated = stackalloc BlitzCard[1];
        if (BlitzCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, BlitzCardType.Power, 1, generated,
                BlitzCardPools.CharacterPowers) == 0)
        {
            return;
        }
        BlitzKernel.MakeFreeThisTurn(x.R, ref generated[0]);
        BlitzKernel.AddGeneratedCard(ref x, generated[0], BlitzPile.Hand);
    }
}

[BlitzContent("SYNCHRONIZE")]
public sealed class Synchronize : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "CalculationExtra"];

    // CardOnPlaySupport.Synchronize: CalculationBase + CalculationExtra × distinct orb ids of
    // temporary Focus (SynchronizePower).
    public override void OnPlay(ref BlitzExec x)
    {
        int amount = (int)BlitzCardEffects.Var(x, 0)
            + (int)BlitzCardEffects.Var(x, 1) * BlitzKernel.DistinctOrbCount(x.B);
        BlitzKernel.ApplyTemporaryFocus(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.SynchronizePower>(),
            amount, BlitzLimits.PlayerOwner, sign: 1);
    }
}
