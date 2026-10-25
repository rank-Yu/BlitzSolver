using BlitzSolver.Engine;

namespace BlitzSolver.Content.Powers;

/// <summary>
/// <c>AmbergrisPower</c>: while its amount is positive its owner takes another player turn instead of
/// the enemy turn; <c>AfterTakingExtraTurn</c> decrements it
/// (<c>ExtraTurnMirrors</c> registers both callbacks).
/// </summary>
[BlitzContent("AMBERGRIS_POWER")]
public sealed class AmbergrisPower : BlitzPowerDef
{
    public override BlitzPowerType Type => BlitzPowerType.Buff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Counter;

    public override bool ShouldTakeExtraTurn(ref BlitzExec x, BlitzListener self)
        => self.Owner == BlitzLimits.PlayerOwner && x.B.Powers[self.Index].Amount > 0;

    public override void AfterTakingExtraTurn(ref BlitzExec x, BlitzListener self)
    {
        if (self.Owner != BlitzLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        BlitzKernel.SetPowerAmount(ref x.B, BlitzLimits.PlayerOwner, Id, amount - 1);
    }
}

/// <summary>
/// <c>ConfusedPower</c>: after a card is drawn, a card with a non-negative canonical energy cost gets a
/// random this-combat cost in 0..3 (<c>AfterCardDrawnMirrors.HandleConfusedPower</c> →
/// <c>CardEnergyCost.SetThisCombat(CombatEnergyCosts.NextInt(4))</c>).
/// </summary>
[BlitzContent("CONFUSED_POWER")]
public sealed class ConfusedPower : BlitzPowerDef
{
    private const int CostRange = 4;

    public override BlitzPowerType Type => BlitzPowerType.Debuff;

    public override BlitzPowerStackType StackType => BlitzPowerStackType.Single;

    public override void AfterCardDrawn(ref BlitzExec x, BlitzListener self, int drawnPosition, bool fromHandDraw)
    {
        if (self.Owner != BlitzLimits.PlayerOwner || (uint)drawnPosition >= (uint)x.B.HandCount)
            return;
        ref BlitzCard card = ref x.B.Cards[drawnPosition];
        if (x.R.Card(card.Id, card.Upgrade).EnergyCost < 0)
            return;
        int cost = x.B.RngStream(BlitzRngStream.CombatEnergyCosts).NextInt(CostRange);
        BlitzKernel.AddEnergyCostModifier(ref card, cost, BlitzCostType.Absolute,
            BlitzCostExpiration.EndOfCombat, reduceOnly: false);
    }
}
