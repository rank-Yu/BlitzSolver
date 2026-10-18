namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Ironclad;

// Existing engine paths for this batch:
// - BloodWall infers Block; CardEffectSpecRegistry applies the self HpLoss afterwards.
// - Taunt infers Block; CorePowerSupport applies Vulnerable afterwards.
// - Tremble has no mirror; CorePowerSupport applies Vulnerable only.
// - Colossus infers Block; CardEffectSpecRegistry applies ColossusPower afterwards.

[FlashContent("BLOOD_WALL")]
public sealed class BloodWall : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.LoseHp(ref x, FlashCardEffects.Var(x, 0));
    }
}

[FlashContent("TAUNT")]
public sealed class Taunt : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[FlashContent("TREMBLE")]
public sealed class Tremble : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 0);
}

[FlashContent("COLOSSUS")]
public sealed class Colossus : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Colossus"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ColossusPower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner);
    }
}
