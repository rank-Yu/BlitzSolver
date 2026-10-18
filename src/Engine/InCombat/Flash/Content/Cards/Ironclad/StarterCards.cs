namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Ironclad;

// Existing engine: StrikeIronclad and DefendIronclad run the inferred Attack/Block recipe;
// Bash runs the attack recipe, then CorePowerSupport.ApplyCardPowers applies Vulnerable.

[FlashContent("STRIKE_IRONCLAD")]
public sealed class StrikeIronclad : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("DEFEND_IRONCLAD")]
public sealed class DefendIronclad : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("BASH")]
public sealed class Bash : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}
