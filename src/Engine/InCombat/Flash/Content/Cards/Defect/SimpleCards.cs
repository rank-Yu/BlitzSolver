namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Defect;

// Straightforward Defect attacks, blocks, draws and resource cards. Existing engine paths are named
// on each definition: the CardOnPlayInferrer's attack/block/draw recipe runs first (mirror), then
// CorePowerSupport / CardPowerOnPlaySupport / CardEffectSpecRegistry / CardOnPlaySupport compensation.

[FlashContent("VOLTAIC")]
public sealed class Voltaic : FlashCardDef
{
    // OrbCardMirrors.VoltaicOnPlay: CalculatedVarSpecRegistry.CountLightningChannels then channel.
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("LIGHTNING_ORB"),
            x.B.Counter(FlashCounter.LightningChannels));
}

[FlashContent("BARRAGE")]
public sealed class Barrage : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // GeneralAttackOnPlay reads CalculatedHits = orb count.
    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), x.B.OrbCount);
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

[FlashContent("CAPACITOR")]
public sealed class Capacitor : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.AddOrbSlots(ref x.B, (int)FlashCardEffects.Var(x, 0));
}

[FlashContent("BOOT_SEQUENCE")]
public sealed class BootSequence : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("CHARGE_BATTERY")]
public sealed class ChargeBattery : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(), 1);
    }
}

[FlashContent("SUPERCRITICAL")]
public sealed class Supercritical : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
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

[FlashContent("ROCKET_PUNCH")]
public sealed class RocketPunch : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }

    // AfterCardGeneratedForCombatMirrors.HandleRocketPunch: a generated Status lowers this card's cost
    // by 1 until played. All generated cards in Flash come from the player.
    public override void AfterCardGeneratedForCombat(ref FlashExec x, FlashListener self, int position, byte creator)
    {
        if (creator != FlashLimits.PlayerOwner)
            return;
        if ((uint)position >= (uint)x.B.CardCount)
            throw new FlashUnsupportedException("生成牌触发时该牌已不在牌堆。");
        FlashCard generated = x.B.Cards[position];
        if (x.R.Card(generated.Id, generated.Upgrade).Type != FlashCardType.Status)
            return;
        if ((uint)self.Index >= (uint)x.B.CardCount)
            throw new FlashUnsupportedException("火箭拳在生成牌结算前离开了牌堆。");
        FlashKernel.AddEnergyCostModifier(ref x.B.Cards[self.Index], -1, FlashCostType.Relative,
            FlashCostExpiration.WhenPlayed, reduceOnly: false);
    }
}

[FlashContent("SWEEPING_BEAM")]
public sealed class SweepingBeam : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[FlashContent("BEAM_CELL")]
public sealed class BeamCell : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[FlashContent("COLD_SNAP")]
public sealed class ColdSnap : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("FROST_ORB"));
    }
}

[FlashContent("BALL_LIGHTNING")]
public sealed class BallLightning : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("LIGHTNING_ORB"));
    }
}

[FlashContent("GO_FOR_THE_EYES")]
public sealed class GoForTheEyes : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        // CardEffectSpecRegistry.GoForTheEyes: only against an enemy whose current move attacks.
        if (x.Target != FlashLimits.NoOwner && FlashKernel.EnemyIntendsToAttack(x.B, x.R, x.Target))
            FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[FlashContent("COMPILE_DRIVER")]
public sealed class CompileDriver : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardDrawCardMirrors.CompileDriverOnPlay: attack, then draw one per distinct orb id.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, FlashKernel.DistinctOrbCount(x.B), fromHandDraw: false);
    }
}

[FlashContent("FTL")]
public sealed class Ftl : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "PlayMax", "Cards"];

    // CardDrawCardMirrors.FtlOnPlay: attack, then draw while fewer than PlayMax cards were played.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (x.B.Counter(FlashCounter.CardsPlayedThisTurn) < (int)FlashCardEffects.Var(x, 1))
            FlashKernel.Draw(ref x, (int)FlashCardEffects.Var(x, 2), fromHandDraw: false);
    }
}

[FlashContent("SUNDER")]
public sealed class Sunder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.Sunder: WasCardKill on the chosen target.
        if (x.CardKilledTarget)
            FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 1));
    }
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

[FlashContent("HELIX_DRILL")]
public sealed class HelixDrill : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CalculatedVarSpecRegistry.HelixDrill: energy spent this turn minus this card's current cost.
    public override void OnPlay(ref FlashExec x)
    {
        int hits = Math.Max(0, x.B.Counter(FlashCounter.EnergySpentThisTurn)
            - FlashKernel.EnergyCostWithModifiers(x.B, x.R, x.Card, FlashPile.Play));
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hits);
    }
}

[FlashContent("UPROAR")]
public sealed class Uproar : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        // CardSelectionCardMirrors.UproarOnPlay: two hits, then auto-play the first attack of a stable
        // shuffle of the draw pile (playable attacks first, then every attack).
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), 2);
        if (x.NeedsChoice)
            return;
        int drawCount = x.B.DrawCount;
        if (drawCount == 0)
            return;
        Span<FlashCard> buffer = stackalloc FlashCard[drawCount];
        Span<int> origins = stackalloc int[drawCount];
        int eligible = 0;
        for (int index = 0; index < drawCount; index++)
        {
            FlashCard card = x.B.Cards[x.B.HandCount + index];
            if (x.R.Card(card.Id, card.Upgrade).Type != FlashCardType.Attack)
                continue;
            if ((card.Keywords & FlashKeywords.Unplayable) != 0)
                continue;
            buffer[eligible] = card;
            origins[eligible] = index;
            eligible++;
        }
        int count = eligible;
        if (count == 0)
        {
            for (int index = 0; index < drawCount; index++)
            {
                FlashCard card = x.B.Cards[x.B.HandCount + index];
                if (x.R.Card(card.Id, card.Upgrade).Type != FlashCardType.Attack)
                    continue;
                buffer[count] = card;
                origins[count] = index;
                count++;
            }
        }
        if (count == 0)
            return;
        FlashCardSort.SortWithOrigins(buffer[..count], origins[..count], x.R);
        ref FlashRngState rng = ref x.B.RngStream(FlashRngStream.Shuffle);
        for (int n = count; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (buffer[k], buffer[n]) = (buffer[n], buffer[k]);
            (origins[k], origins[n]) = (origins[n], origins[k]);
        }
        int position = origins[0];
        if (position >= x.B.DrawCount
            || !FlashFingerprint.Equal(FlashPiles.At(ref x.B, FlashPile.Draw, position), buffer[0]))
        {
            throw new FlashUnsupportedException("Uproar 选中的抽牌在结算前离开了抽牌堆。");
        }
        FlashKernel.AutoPlay(ref x, FlashPile.Draw, position);
    }
}

[FlashContent("FLAK_CANNON")]
public sealed class FlakCannon : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        // RandomTargetAttackCardMirrors.FlakCannonOnPlay: exhaust every Status card (outside the
        // exhaust pile), then a random-target attack with one hit per status.
        int hits = FlashCardEffects.CountStatusCards(x.B, x.R);
        FlashCardEffects.ExhaustAllStatusCards(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hits);
    }
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

[FlashContent("HOLOGRAM")]
public sealed class Hologram : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        // CardChoiceSupport.GetSpec: Hologram moves one discard card to hand.
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Discard, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen > 0)
            FlashKernel.MoveSelected(ref x.B, FlashPile.Discard, selected[..chosen], FlashPile.Hand,
                FlashPilePosition.Bottom);
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

[FlashContent("ADAPTIVE_STRIKE")]
public sealed class AdaptiveStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry.AdaptiveStrike: attack, then a clone with cost 0 this combat to discard.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCard clone = FlashKernel.CloneCard(x.Card);
        FlashKernel.AddEnergyCostModifier(ref clone, 0, FlashCostType.Absolute, FlashCostExpiration.EndOfCombat,
            reduceOnly: false);
        FlashKernel.AddGeneratedCard(ref x, clone, FlashPile.Discard);
    }
}

[FlashContent("TURBO")]
public sealed class Turbo : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("VOID", 0)];

    // CardOnPlaySupport.Turbo: gain Energy, then generate one Void to the discard pile.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryCard("VOID", out ushort id))
            throw new FlashUnsupportedException("涡轮生成需要 VOID 的 Flash 定义。");
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, id), FlashPile.Discard);
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

[FlashContent("GUNK_UP")]
public sealed class GunkUp : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SLIMED", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), (int)FlashCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryCard("SLIMED", out ushort id))
            throw new FlashUnsupportedException("黏液需要 SLIMED 的 Flash 定义。");
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, id), FlashPile.Discard);
    }
}

[FlashContent("SCRAPE")]
public sealed class Scrape : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    // CardDrawCardMirrors.ScrapeOnPlay: attack, draw Cards, discard the drawn cards that are not free.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        Span<FlashCard> drawn = stackalloc FlashCard[FlashLimits.Cards];
        Span<int> positions = stackalloc int[FlashLimits.Cards];
        int count = FlashKernel.DrawInto(ref x, (int)FlashCardEffects.Var(x, 1), fromHandDraw: false, drawn,
            positions);
        if (x.NeedsChoice)
            return;
        Span<int> chosen = stackalloc int[count];
        int discard = 0;
        for (int index = 0; index < count; index++)
        {
            FlashCard card = drawn[index];
            int cost = FlashKernel.EnergyCostValueWithModifiers(x.B, x.R, card, FlashPile.Hand);
            if ((card.Flags & FlashCardFlags.CostsX) != 0 || cost != 0)
                chosen[discard++] = index;
        }
        if (discard > 0)
            FlashCardEffects.DiscardDrawn(ref x, drawn, positions, chosen[..discard]);
    }
}

[FlashContent("ALL_FOR_ONE")]
public sealed class AllForOne : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry.AllForOne: attack, then return every zero-cost non-X Attack/Skill/Power
    // from the discard pile to hand in discard order.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = x.B.DiscardCount;
        int start = FlashPiles.Start(x.B, FlashPile.Discard);
        Span<FlashCard> chosen = stackalloc FlashCard[count];
        int selected = 0;
        for (int offset = 0; offset < count; offset++)
        {
            FlashCard card = x.B.Cards[start + offset];
            FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if (stats.Type is not (FlashCardType.Attack or FlashCardType.Skill or FlashCardType.Power))
                continue;
            if ((card.Flags & FlashCardFlags.CostsX) != 0)
                continue;
            if (FlashKernel.EnergyCostWithModifiers(x.B, x.R, card, FlashPile.Discard) != 0)
                continue;
            chosen[selected++] = card;
        }
        for (int item = 0; item < selected; item++)
        {
            int position = FindDiscard(x.B, chosen[item]);
            if (position < 0)
                throw new FlashUnsupportedException("AllForOne 选中的弃牌在结算前离开了弃牌堆。");
            if (!FlashKernel.MoveCard(ref x.B, FlashPile.Discard, position, FlashPile.Hand))
                return;
        }
    }

    private static int FindDiscard(in FlashBattle battle, in FlashCard card)
    {
        ReadOnlySpan<FlashCard> discard = FlashPiles.ReadCards(battle, FlashPile.Discard);
        for (int index = 0; index < discard.Length; index++)
        {
            if (FlashFingerprint.Equal(discard[index], card))
                return index;
        }
        return -1;
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

[FlashContent("FUEL")]
public sealed class Fuel : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
}
