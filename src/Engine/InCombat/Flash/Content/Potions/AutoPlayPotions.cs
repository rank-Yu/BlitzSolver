namespace CombatSolver.Engine.InCombat.Flash.Content.Potions;

/// <summary>
/// Native <c>DistilledChaos</c>: <c>AutoPlayPotionMirrors.DistilledChaosOnUse</c> calls
/// <c>simulator.AutoPlayFromDrawPile(player, Repeat, CardPilePosition.Top)</c> with
/// <c>forceExhaust: false</c>, i.e. it moves up to Repeat cards from the draw-pile top to the play
/// pile and auto-plays them in order with their normal result piles.
/// </summary>
[FlashContent("DISTILLED_CHAOS")]
public sealed class DistilledChaos : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnUse(ref FlashExec x)
        => FlashKernel.AutoPlayFromDrawPile(ref x, (int)FlashKernel.PotionVar(x, this, 0), forceExhaust: false);
}
