using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Defect;

[FlashContent("FUEL")]
public sealed class Fuel : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
}
