using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Defect;

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

[FlashContent("TESLA_COIL")]
public sealed class TeslaCoil : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        Span<int> lightning = stackalloc int[FlashLimits.Orbs];
        if (!FlashRegistry.TryOrb("LIGHTNING_ORB", out ushort id))
            return;
        int count = 0;
        for (int index = 0; index < x.B.OrbCount; index++)
        {
            if (x.B.Orbs[index].Id == id)
                lightning[count++] = index;
        }
        int triggers = x.Card.Upgrade > 0 ? 2 : 1;
        for (int index = 0; index < count; index++)
        {
            for (int trigger = 0; trigger < triggers; trigger++)
            {
                FlashKernel.OrbPassiveAt(ref x, lightning[index], x.Target);
                if (x.NeedsChoice)
                    return;
            }
        }
    }
}

[FlashContent("REFRACT")]
public sealed class Refract : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat", "Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 1), hitCount: 2);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("GLASS_ORB"), (int)FlashCardEffects.Var(x, 0));
    }
}

[FlashContent("NULL")]
public sealed class Null : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        // OrbCardMirrors.NullOnPlay mirrors attack then channel; CorePowerSupport applies Weak after.
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("DARK_ORB"));
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 1);
    }
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
        // CorePowerSupport.ApplyCardPowers settles newly dead enemies after the mirror attack and
        // before the card-specific draw compensation; HittableEnemies/Hellraiser read that phase.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
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
        if (FlashKernel.CardType(x.B, x.R, generated) != FlashCardType.Status)
            return;
        if ((uint)self.Index >= (uint)x.B.CardCount)
            throw new FlashUnsupportedException("火箭拳在生成牌结算前离开了牌堆。");
        FlashKernel.AddEnergyCostModifier(ref x.B.Cards[self.Index], -1, FlashCostType.Relative,
            FlashCostExpiration.WhenPlayed, reduceOnly: false);
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
        // CorePowerSupport.ApplyCardPowers settles newly dead enemies after the mirror attack and
        // before the card-specific draw compensation; HittableEnemies/Hellraiser read that phase.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
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
