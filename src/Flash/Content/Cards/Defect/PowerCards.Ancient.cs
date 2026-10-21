using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Defect;

[FlashContent("BIASED_COGNITION")]
public sealed class BiasedCognition : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower", "BiasedCognitionPower"];

    // CorePowerSupport.BiasedCognition: Focus first, then the per-turn Focus loss power.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.FocusPower>(), 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.BiasedCognitionPower>(), 1);
    }
}
