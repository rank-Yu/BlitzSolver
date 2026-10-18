namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Colorless;

// Colorless attack cards. Existing engine paths for this batch:
// - DramaticEntrance, UltimateStrike and Salvo infer the Attack recipe (Salvo is an
//   EngineInferred method recipe), then CardOnPlaySupport adds Salvo's RetainHandPower stack.
// - FlashOfSteel is CardDrawCardMirrors' inferred attack + owner draw.
// - MindBlast and Rend are CardCalculatedVar attacks: CalculationBase + ExtraDamage * multiplier,
//   resolved by CalculatedVarSpecRegistry (draw pile count / target non-temporary debuff powers).
// - Volley is RandomTargetAttackCardMirrors.VolleyOnPlay: ResolveEnergyXValue hits on random
//   hittable opponents, one CombatTargets pick per hit.

[FlashContent("DRAMATIC_ENTRANCE")]
public sealed class DramaticEntrance : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("FLASH_OF_STEEL")]
public sealed class FlashOfSteel : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[FlashContent("MIND_BLAST")]
public sealed class MindBlast : FlashCardDef
{
    // Native CalculatedDamageVar: CalculationBase + ExtraDamage * draw pile card count.
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        decimal damage = FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * x.B.DrawCount;
        FlashCardEffects.Attack(ref x, damage);
    }
}

[FlashContent("REND")]
public sealed class Rend : FlashCardDef
{
    // Native CalculatedDamageVar: CalculationBase + ExtraDamage * the target's non-temporary
    // debuff count (CalculatedVarSpecRegistry.TryMultiplier, Rend case).
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        int debuffs = 0;
        if (x.Target != FlashLimits.NoOwner)
        {
            for (int index = 0; index < x.B.PowerCount; index++)
            {
                ref readonly FlashPower power = ref x.B.Powers[index];
                if (power.Owner != x.Target)
                    continue;
                FlashPowerDef def = FlashRegistry.Power(power.Id);
                if (def.TypeForAmount(power.Amount) == FlashPowerType.Debuff && !def.IsTemporaryPower)
                    debuffs++;
            }
        }
        decimal damage = FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * debuffs;
        FlashCardEffects.Attack(ref x, damage);
    }
}

[FlashContent("SALVO")]
public sealed class Salvo : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RetainHandPower>(),
            1, FlashLimits.PlayerOwner);
    }
}

[FlashContent("ULTIMATE_STRIKE")]
public sealed class UltimateStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("VOLLEY")]
public sealed class Volley : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        // HasEnergyCostX: one random-target hit per energy spent (ResolveEnergyXValue).
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), x.XValue);
    }
}
