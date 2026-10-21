using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

[FlashContent("DAGGER_THROW")]
public sealed class DaggerThrow : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

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
        FlashKernel.Draw(ref x, 1, fromHandDraw: false);
    }

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: DaggerThrow → Discard, Hand, 1, every hand card.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Discard, FlashPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        FlashKernel.DiscardAndDraw(ref x, FlashPile.Hand, selected[..chosen], 0);
    }
}

[FlashContent("FLICK_FLACK")]
public sealed class FlickFlack : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("POISONED_STAB")]
public sealed class PoisonedStab : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "PoisonPower"];

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
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.PoisonPower>(), 1);
    }
}

[FlashContent("RICOCHET")]
public sealed class Ricochet : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    // RandomTargetAttackCardMirrors.RicochetOnPlay: Repeat randomly aimed hits.
    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), (int)FlashCardEffects.Var(x, 1));
}

[FlashContent("LEADING_STRIKE")]
public sealed class LeadingStrike : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Shivs", "Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        // BespokeCardMirrors.LeadingStrikeOnPlay: attack, then Shivs Shivs to hand.
        FlashCardEffects.GeneralAttack(ref x, 1);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)FlashCardEffects.Var(x, 0), upgraded: false);
    }
}

[FlashContent("SUCKER_PUNCH")]
public sealed class SuckerPunch : FlashCardDef
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
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[FlashContent("SLICE")]
public sealed class Slice : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("DAGGER_SPRAY")]
public sealed class DaggerSpray : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // BespokeCardMirrors.DaggerSprayOnPlay: two hits to all opponents.
    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hitCount: 2);
}

[FlashContent("PREDATOR")]
public sealed class Predator : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry.Predator: DrawCardsNextTurnPower with the fixed amount 2.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.DrawCardsNextTurnPower>(), 2, FlashLimits.PlayerOwner);
    }
}
