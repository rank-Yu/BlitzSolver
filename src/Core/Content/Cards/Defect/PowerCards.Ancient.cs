using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Defect;

[BlitzContent("BIASED_COGNITION")]
public sealed class BiasedCognition : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower", "BiasedCognitionPower"];

    // CorePowerSupport.BiasedCognition: Focus first, then the per-turn Focus loss power.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.FocusPower>(), 0);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.BiasedCognitionPower>(), 1);
    }
}
