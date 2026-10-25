using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

[BlitzContent("DAGGER_THROW")]
public sealed class DaggerThrow : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

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
        BlitzKernel.Draw(ref x, 1, fromHandDraw: false);
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        // GetSpec: DaggerThrow → Discard, Hand, 1, every hand card.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Discard, BlitzPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        BlitzKernel.DiscardAndDraw(ref x, BlitzPile.Hand, selected[..chosen], 0);
    }
}

[BlitzContent("FLICK_FLACK")]
public sealed class FlickFlack : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("POISONED_STAB")]
public sealed class PoisonedStab : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "PoisonPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.PoisonPower>(), 1);
    }
}

[BlitzContent("RICOCHET")]
public sealed class Ricochet : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    // RandomTargetAttackCardMirrors.RicochetOnPlay: Repeat randomly aimed hits.
    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), (int)BlitzCardEffects.Var(x, 1));
}

[BlitzContent("LEADING_STRIKE")]
public sealed class LeadingStrike : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Shivs", "Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        // BespokeCardMirrors.LeadingStrikeOnPlay: attack, then Shivs Shivs to hand.
        BlitzCardEffects.GeneralAttack(ref x, 1);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)BlitzCardEffects.Var(x, 0), upgraded: false);
    }
}

[BlitzContent("SUCKER_PUNCH")]
public sealed class SuckerPunch : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[BlitzContent("SLICE")]
public sealed class Slice : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("DAGGER_SPRAY")]
public sealed class DaggerSpray : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // BespokeCardMirrors.DaggerSprayOnPlay: two hits to all opponents.
    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), hitCount: 2);
}

[BlitzContent("PREDATOR")]
public sealed class Predator : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry.Predator: DrawCardsNextTurnPower with the fixed amount 2.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.DrawCardsNextTurnPower>(), 2, BlitzLimits.PlayerOwner);
    }
}
