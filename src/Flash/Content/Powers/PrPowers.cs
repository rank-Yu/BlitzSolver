using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Powers;

// Powers that entered the vanilla content closure with this batch's potions and relics.

/// <summary>
/// <c>AmbergrisPower</c>: while its amount is positive its owner takes another player turn instead of
/// the enemy turn; <c>AfterTakingExtraTurn</c> decrements it
/// (<c>ExtraTurnMirrors</c> registers both callbacks).
/// </summary>
[FlashContent("AMBERGRIS_POWER")]
public sealed class AmbergrisPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool ShouldTakeExtraTurn(ref FlashExec x, FlashListener self)
        => self.Owner == FlashLimits.PlayerOwner && x.B.Powers[self.Index].Amount > 0;

    public override void AfterTakingExtraTurn(ref FlashExec x, FlashListener self)
    {
        if (self.Owner != FlashLimits.PlayerOwner)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        FlashKernel.SetPowerAmount(ref x.B, FlashLimits.PlayerOwner, Id, amount - 1);
    }
}

/// <summary>
/// <c>ConfusedPower</c>: after a card is drawn, a card with a non-negative canonical energy cost gets a
/// random this-combat cost in 0..3 (<c>AfterCardDrawnMirrors.HandleConfusedPower</c> →
/// <c>CardEnergyCost.SetThisCombat(CombatEnergyCosts.NextInt(4))</c>).
/// </summary>
[FlashContent("CONFUSED_POWER")]
public sealed class ConfusedPower : FlashPowerDef
{
    private const int CostRange = 4;

    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        if (self.Owner != FlashLimits.PlayerOwner || (uint)drawnPosition >= (uint)x.B.HandCount)
            return;
        ref FlashCard card = ref x.B.Cards[drawnPosition];
        if (x.R.Card(card.Id, card.Upgrade).EnergyCost < 0)
            return;
        int cost = x.B.RngStream(FlashRngStream.CombatEnergyCosts).NextInt(CostRange);
        FlashKernel.AddEnergyCostModifier(ref card, cost, FlashCostType.Absolute,
            FlashCostExpiration.EndOfCombat, reduceOnly: false);
    }
}
