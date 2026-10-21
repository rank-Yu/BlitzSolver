using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Ironclad;

[FlashContent("GIANT_ROCK")]
public sealed class GiantRock : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}
