using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Defect;

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
            if (FlashKernel.CardType(x.B, x.R, card) != FlashCardType.Status)
                continue;
            statuses[count++] = index;
        }
        if (count == 0)
            return;
        FlashKernel.TransformCards(ref x, FlashPile.Hand, statuses[..count],
            FlashKernel.CreateCard(x.R, fuel), x.Card.Upgrade > 0 ? 1 : 0);
    }
}

[FlashContent("GLACIER")]
public sealed class Glacier : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("FROST_ORB"), 2);
    }
}

[FlashContent("GLASSWORK")]
public sealed class Glasswork : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("GLASS_ORB"));
    }
}

[FlashContent("FUSION")]
public sealed class Fusion : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("PLASMA_ORB"));
}

[FlashContent("CHILL")]
public sealed class Chill : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        // OrbCardMirrors.ChillOnPlay: one Frost per hittable enemy, counted when the card resolves.
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (FlashKernel.IsHittable(x.B, owner))
                count++;
        }
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("FROST_ORB"), count);
    }
}

[FlashContent("CHAOS")]
public sealed class Chaos : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnPlay(ref FlashExec x)
    {
        int count = (int)FlashCardEffects.Var(x, 0);
        for (int index = 0; index < count; index++)
        {
            ushort orb = FlashKernel.RandomOrb(ref x.B);
            if (!FlashKernel.ChannelOrb(ref x, orb))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("DARKNESS")]
public sealed class Darkness : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("DARK_ORB"));
        if (x.NeedsChoice)
            return;
        // OrbCardMirrors.DarknessOnPlay: every Dark orb in the resulting queue triggers Passive once
        // (twice when upgraded). OrbCmd.Passive, so no ModifyOrbPassiveTriggerCounts.
        Span<int> dark = stackalloc int[FlashLimits.Orbs];
        int count = DarkIndices(x.B, dark);
        int triggers = x.Card.Upgrade > 0 ? 2 : 1;
        for (int index = 0; index < count; index++)
        {
            for (int trigger = 0; trigger < triggers; trigger++)
            {
                FlashKernel.OrbPassiveAt(ref x, dark[index]);
                if (x.NeedsChoice)
                    return;
            }
        }
    }

    private static int DarkIndices(in FlashBattle battle, Span<int> output)
    {
        if (!FlashRegistry.TryOrb("DARK_ORB", out ushort id))
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

[FlashContent("TEMPEST")]
public sealed class Tempest : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        int count = FlashKernel.ResolveEnergyXValue(ref x);
        if (x.Card.Upgrade > 0)
            count++;
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("LIGHTNING_ORB"), count);
    }
}

[FlashContent("OVERCLOCK")]
public sealed class Overclock : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("BURN", 0)];

    // Draw is inferred; CardEffectSpecRegistry's generation adds one Burn to the discard pile.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryCard("BURN", out ushort burn))
            throw new FlashUnsupportedException("过载生成需要 BURN 的 Flash 定义。");
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, burn), FlashPile.Discard);
    }
}

[FlashContent("SHADOW_SHIELD")]
public sealed class ShadowShield : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("DARK_ORB"));
    }
}

[FlashContent("BOOT_SEQUENCE")]
public sealed class BootSequence : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("SKIM")]
public sealed class Skim : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 0), fromHandDraw: false);
}

[FlashContent("DOUBLE_ENERGY")]
public sealed class DoubleEnergy : FlashCardDef
{
    // CardOnPlaySupport.DoubleEnergy: GainEnergy(playerState.Energy) after the card's own cost.
    public override void OnPlay(ref FlashExec x) => FlashKernel.GainEnergy(ref x, x.B.Energy);
}

[FlashContent("SCAVENGE")]
public sealed class Scavenge : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
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
                && !FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand, options, 0, 0,
                    selected, out chosen))
            {
                return;
            }
        }
        else if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand, options, 1, selected,
                     out chosen))
        {
            return;
        }
        if (chosen > 0)
            FlashKernel.ExhaustSelected(ref x, FlashPile.Hand, selected[..chosen]);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(), 0);
    }
}

[FlashContent("FIGHT_THROUGH")]
public sealed class FightThrough : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("WOUND", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryCard("WOUND", out ushort id))
            throw new FlashUnsupportedException("突破需要 WOUND 的 Flash 定义。");
        for (int index = 0; index < 2; index++)
        {
            if (!FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, id), FlashPile.Discard))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("WHITE_NOISE")]
public sealed class WhiteNoise : FlashCardDef
{
    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterPowers];

    // CardGenerationCardMirrors.WhiteNoiseOnPlay: one free distinct Power card to hand.
    public override void OnPlay(ref FlashExec x)
    {
        Span<FlashCard> generated = stackalloc FlashCard[1];
        if (FlashCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, FlashCardType.Power, 1, generated,
                FlashCardPools.CharacterPowers) == 0)
        {
            return;
        }
        FlashKernel.MakeFreeThisTurn(x.R, ref generated[0]);
        FlashKernel.AddGeneratedCard(ref x, generated[0], FlashPile.Hand);
    }
}

[FlashContent("SYNCHRONIZE")]
public sealed class Synchronize : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "CalculationExtra"];

    // CardOnPlaySupport.Synchronize: CalculationBase + CalculationExtra × distinct orb ids of
    // temporary Focus (SynchronizePower).
    public override void OnPlay(ref FlashExec x)
    {
        int amount = (int)FlashCardEffects.Var(x, 0)
            + (int)FlashCardEffects.Var(x, 1) * FlashKernel.DistinctOrbCount(x.B);
        FlashKernel.ApplyTemporaryFocus(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.SynchronizePower>(),
            amount, FlashLimits.PlayerOwner, sign: 1);
    }
}
