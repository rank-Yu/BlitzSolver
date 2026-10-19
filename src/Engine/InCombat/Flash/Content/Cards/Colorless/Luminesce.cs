namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Colorless;

/// <summary>
/// Native <c>Luminesce</c> (0-cost Token Skill, Exhaust/Retain): gains its Energy variable
/// (<c>CardOnPlaySupport</c>'s ForgottenRitual / Fuel / Luminesce case calls
/// <c>simulator.GainEnergy(card.Owner, card.DynamicVars.Energy.IntValue)</c>).
/// </summary>
[FlashContent("LUMINESCE")]
public sealed class Luminesce : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
}
