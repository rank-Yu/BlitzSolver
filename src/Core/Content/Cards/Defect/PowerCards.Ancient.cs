using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Content.Cards.Defect;

[ContentId("BIASED_COGNITION")]
public sealed class BiasedCognition : CardDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower", "BiasedCognitionPower"];

    // CorePowerSupport.BiasedCognition: Focus first, then the per-turn Focus loss power.
    public override void OnPlay(ref Exec x)
    {
        CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.FocusPower>(), 0);
        if (x.NeedsChoice)
            return;
        CardEffects.ApplyToOwner(ref x, Registry.PowerId<Powers.BiasedCognitionPower>(), 1);
    }
}
