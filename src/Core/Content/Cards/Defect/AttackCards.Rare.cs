using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[BlitzContent("ICE_LANCE")]
public sealed class IceLance : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("FROST_ORB"), (int)BlitzCardEffects.Var(x, 1));
    }
}

[BlitzContent("SHATTER")]
public sealed class Shatter : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int orbs = x.B.OrbCount;
        for (int index = 0; index < orbs; index++)
        {
            BlitzKernel.EvokeNext(ref x, repeat: 2);
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("HYPERBEAM")]
public sealed class Hyperbeam : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "FocusPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardEffectSpecRegistry's Hyperbeam → ApplyTemporaryFocusLoss<HyperbeamFocusDownPower>.
        BlitzKernel.ApplyTemporaryFocus(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.HyperbeamFocusDownPower>(), (int)BlitzCardEffects.Var(x, 1),
            BlitzLimits.PlayerOwner, sign: -1);
    }
}

[BlitzContent("METEOR_STRIKE")]
public sealed class MeteorStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // OrbCardMirrors.MeteorStrikeOnPlay: AttackSingle, then channel three Plasma orbs.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("PLASMA_ORB"), 3);
    }
}

[BlitzContent("HELIX_DRILL")]
public sealed class HelixDrill : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CalculatedVarSpecRegistry.HelixDrill: energy spent this turn minus this card's current cost.
    public override void OnPlay(ref BlitzExec x)
    {
        int hits = Math.Max(0, x.B.Counter(BlitzCounter.EnergySpentThisTurn)
            - BlitzKernel.EnergyCostWithModifiers(x.B, x.R, x.Card, BlitzPile.Play));
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), hits);
    }
}

[BlitzContent("FLAK_CANNON")]
public sealed class FlakCannon : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        // RandomTargetAttackCardMirrors.FlakCannonOnPlay: exhaust every Status card (outside the
        // exhaust pile), then a random-target attack with one hit per status.
        int hits = BlitzCardEffects.CountStatusCards(x.B, x.R);
        BlitzCardEffects.ExhaustAllStatusCards(ref x);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), hits);
    }
}

[BlitzContent("ADAPTIVE_STRIKE")]
public sealed class AdaptiveStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry.AdaptiveStrike: attack, then a clone with cost 0 this combat to discard.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzCard clone = BlitzKernel.CloneCard(x.Card);
        BlitzKernel.AddEnergyCostModifier(ref clone, 0, BlitzCostType.Absolute, BlitzCostExpiration.EndOfCombat,
            reduceOnly: false);
        BlitzKernel.AddGeneratedCard(ref x, clone, BlitzPile.Discard);
    }
}

[BlitzContent("ALL_FOR_ONE")]
public sealed class AllForOne : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry.AllForOne: attack, then return every zero-cost non-X Attack/Skill/Power
    // from the discard pile to hand in discard order.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = x.B.DiscardCount;
        int start = BlitzPiles.Start(x.B, BlitzPile.Discard);
        Span<BlitzCard> chosen = stackalloc BlitzCard[count];
        int selected = 0;
        for (int offset = 0; offset < count; offset++)
        {
            BlitzCard card = x.B.Cards[start + offset];
            BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if (stats.Type is not (BlitzCardType.Attack or BlitzCardType.Skill or BlitzCardType.Power))
                continue;
            if ((card.Flags & BlitzCardFlags.CostsX) != 0)
                continue;
            if (BlitzKernel.EnergyCostWithModifiers(x.B, x.R, card, BlitzPile.Discard) != 0)
                continue;
            chosen[selected++] = card;
        }
        for (int item = 0; item < selected; item++)
        {
            int position = FindDiscard(x.B, chosen[item]);
            if (position < 0)
                throw new BlitzUnsupportedException("AllForOne 选中的弃牌在结算前离开了弃牌堆。");
            if (!BlitzKernel.MoveCard(ref x.B, BlitzPile.Discard, position, BlitzPile.Hand))
                return;
        }
    }

    private static int FindDiscard(in BlitzBattle battle, in BlitzCard card)
    {
        ReadOnlySpan<BlitzCard> discard = BlitzPiles.ReadCards(battle, BlitzPile.Discard);
        for (int index = 0; index < discard.Length; index++)
        {
            if (BlitzFingerprint.Equal(discard[index], card))
                return index;
        }
        return -1;
    }
}
