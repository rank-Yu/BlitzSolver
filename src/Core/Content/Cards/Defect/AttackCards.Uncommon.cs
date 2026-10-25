using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[BlitzContent("SYNTHESIS")]
public sealed class Synthesis : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry.Synthesis: attack, then one FreePowerPower.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.FreePowerPower>(), 1,
            BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("TESLA_COIL")]
public sealed class TeslaCoil : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        Span<int> lightning = stackalloc int[BlitzLimits.Orbs];
        if (!BlitzRegistry.TryOrb("LIGHTNING_ORB", out ushort id))
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
                BlitzKernel.OrbPassiveAt(ref x, lightning[index], x.Target);
                if (x.NeedsChoice)
                    return;
            }
        }
    }
}

[BlitzContent("REFRACT")]
public sealed class Refract : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat", "Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 1), hitCount: 2);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("GLASS_ORB"), (int)BlitzCardEffects.Var(x, 0));
    }
}

[BlitzContent("NULL")]
public sealed class Null : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        // OrbCardMirrors.NullOnPlay mirrors attack then channel; CorePowerSupport applies Weak after.
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ChannelOrb(ref x, BlitzKernel.OrbId("DARK_ORB"));
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[BlitzContent("ROCKET_PUNCH")]
public sealed class RocketPunch : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers settles newly dead enemies after the mirror attack and
        // before the card-specific draw compensation; HittableEnemies/Hellraiser read that phase.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 1), fromHandDraw: false);
    }

    // AfterCardGeneratedForCombatMirrors.HandleRocketPunch: a generated Status lowers this card's cost
    // by 1 until played. All generated cards in Blitz come from the player.
    public override void AfterCardGeneratedForCombat(ref BlitzExec x, BlitzListener self, int position, byte creator)
    {
        if (creator != BlitzLimits.PlayerOwner)
            return;
        if ((uint)position >= (uint)x.B.CardCount)
            throw new BlitzUnsupportedException("生成牌触发时该牌已不在牌堆。");
        BlitzCard generated = x.B.Cards[position];
        if (BlitzKernel.CardType(x.B, x.R, generated) != BlitzCardType.Status)
            return;
        if ((uint)self.Index >= (uint)x.B.CardCount)
            throw new BlitzUnsupportedException("火箭拳在生成牌结算前离开了牌堆。");
        BlitzKernel.AddEnergyCostModifier(ref x.B.Cards[self.Index], -1, BlitzCostType.Relative,
            BlitzCostExpiration.WhenPlayed, reduceOnly: false);
    }
}

[BlitzContent("FTL")]
public sealed class Ftl : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "PlayMax", "Cards"];

    // CardDrawCardMirrors.FtlOnPlay: attack, then draw while fewer than PlayMax cards were played.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers settles newly dead enemies after the mirror attack and
        // before the card-specific draw compensation; HittableEnemies/Hellraiser read that phase.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        if (x.B.Counter(BlitzCounter.CardsPlayedThisTurn) < (int)BlitzCardEffects.Var(x, 1))
            BlitzKernel.Draw(ref x, (int)BlitzCardEffects.Var(x, 2), fromHandDraw: false);
    }
}

[BlitzContent("SUNDER")]
public sealed class Sunder : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Energy"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.Sunder: WasCardKill on the chosen target.
        if (x.CardKilledTarget)
            BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 1));
    }
}

[BlitzContent("SCRAPE")]
public sealed class Scrape : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    // CardDrawCardMirrors.ScrapeOnPlay: attack, draw Cards, discard the drawn cards that are not free.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        Span<BlitzCard> drawn = stackalloc BlitzCard[BlitzLimits.Cards];
        Span<int> positions = stackalloc int[BlitzLimits.Cards];
        int count = BlitzKernel.DrawInto(ref x, (int)BlitzCardEffects.Var(x, 1), fromHandDraw: false, drawn,
            positions);
        if (x.NeedsChoice)
            return;
        Span<int> chosen = stackalloc int[count];
        int discard = 0;
        for (int index = 0; index < count; index++)
        {
            BlitzCard card = drawn[index];
            int cost = BlitzKernel.EnergyCostValueWithModifiers(x.B, x.R, card, BlitzPile.Hand);
            if ((card.Flags & BlitzCardFlags.CostsX) != 0 || cost != 0)
                chosen[discard++] = index;
        }
        if (discard > 0)
            BlitzCardEffects.DiscardDrawn(ref x, drawn, positions, chosen[..discard]);
    }
}
