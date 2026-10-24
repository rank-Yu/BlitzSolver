using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Defect;

[FlashContent("ICE_LANCE")]
public sealed class IceLance : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("FROST_ORB"), (int)FlashCardEffects.Var(x, 1));
    }
}

[FlashContent("SHATTER")]
public sealed class Shatter : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int orbs = x.B.OrbCount;
        for (int index = 0; index < orbs; index++)
        {
            FlashKernel.EvokeNext(ref x, repeat: 2);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("HYPERBEAM")]
public sealed class Hyperbeam : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "FocusPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardEffectSpecRegistry's Hyperbeam → ApplyTemporaryFocusLoss<HyperbeamFocusDownPower>.
        FlashKernel.ApplyTemporaryFocus(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.HyperbeamFocusDownPower>(), (int)FlashCardEffects.Var(x, 1),
            FlashLimits.PlayerOwner, sign: -1);
    }
}

[FlashContent("METEOR_STRIKE")]
public sealed class MeteorStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // OrbCardMirrors.MeteorStrikeOnPlay: AttackSingle, then channel three Plasma orbs.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("PLASMA_ORB"), 3);
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
