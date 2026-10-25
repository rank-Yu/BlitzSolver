using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Colorless;

/// <summary>
/// Native <c>Luminesce</c> (0-cost Token Skill, Exhaust/Retain): gains its Energy variable
/// (<c>CardOnPlaySupport</c>'s ForgottenRitual / Fuel / Luminesce case calls
/// <c>simulator.GainEnergy(card.Owner, card.DynamicVars.Energy.IntValue)</c>).
/// </summary>
[BlitzContent("LUMINESCE")]
public sealed class Luminesce : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref BlitzExec x) => BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 0));
}
