
namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Ironclad;

// Existing engine: TwinStrike is registered to BespokeCardMirrors.TwinStrikeOnPlay (one target, two hits).
// IronWave, PommelStrike and ShrugItOff run the inferred attack/block/draw recipe in source order.
// Uppercut runs the attack recipe, then CorePowerSupport.ApplyCardPowers applies Weak and Vulnerable.
// Thunderclap runs the all-opponents attack recipe, then ApplyCardPowers applies Vulnerable to every
// hittable enemy.

[FlashContent("TWIN_STRIKE")]
public sealed class TwinStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: 2);
}

[FlashContent("IRON_WAVE")]
public sealed class IronWave : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 1);
        FlashCardEffects.GeneralAttack(ref x, 0);
    }
}

[FlashContent("POMMEL_STRIKE")]
public sealed class PommelStrike : FlashCardDef
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
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[FlashContent("SHRUG_IT_OFF")]
public sealed class ShrugItOff : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[FlashContent("THUNDERCLAP")]
public sealed class Thunderclap : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // ApplyCardPowers settles newly dead enemies after the mirrored attack and before its
        // card-specific switch; Thunderclap's Vulnerable must therefore see any Wrigglers spawned by
        // the Phrog Parasite the attack just killed.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToAllEnemies(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[FlashContent("UPPERCUT")]
public sealed class Uppercut : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Power"];

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
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}
