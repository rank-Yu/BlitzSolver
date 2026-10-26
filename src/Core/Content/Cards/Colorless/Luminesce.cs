using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Content.Cards.Colorless;

/// <summary>
/// Native <c>Luminesce</c> (0-cost Token Skill, Exhaust/Retain): gains its Energy variable
/// (<c>CardOnPlaySupport</c>'s ForgottenRitual / Fuel / Luminesce case calls
/// <c>simulator.GainEnergy(card.Owner, card.DynamicVars.Energy.IntValue)</c>).
/// </summary>
[ContentId("LUMINESCE")]
public sealed class Luminesce : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref Exec x) => Kernel.GainEnergy(ref x, CardEffects.Var(x, 0));
}
