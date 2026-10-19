namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Star (Regent resource) kernel. Sources: <c>CombatPredictionSimulator.Energy.GainStars</c>,
/// <c>CardModel.SpendResources</c>, <c>CombatPredictedCardExtensions.GetStarCostWithModifiers</c> and
/// <c>CardModel.ResolveStarXValue</c>.
/// </summary>
public static partial class FlashKernel
{
    /// <summary>
    /// Mirrors <c>PlayerCmd.GainStars</c> through <c>CombatPredictionSimulator.GainStars</c>: the gain
    /// is skipped while the combat is ending, <c>Hook.ShouldGainStars</c> has no vanilla listener, the
    /// state is clamped into [0, 999,999,999] and <c>AfterStarsGained</c> runs after the mutation.
    /// Returns false when a listener left a pending choice.
    /// </summary>
    public static bool GainStars(ref FlashExec x, int amount)
    {
        if (IsEnding(x.B))
            return true;
        x.B.Stars = (int)Math.Clamp((long)x.B.Stars + amount, 0, FlashLimits.ValueClamp);
        // SimulatedCombatState.RecordStarsGained only records a positive gain.
        if (amount > 0)
            x.B.AddCounter(FlashCounter.StarsGainedThisTurn, amount);
        return FlashHooks.AfterStarsGained(ref x, amount);
    }

    /// <summary>
    /// Mirrors <c>CardModel.GetStarCostWithModifiers</c>: X star cost pays every current star; otherwise
    /// the temporary star cost (last entry) or the base cost goes through
    /// <c>HookMirrors.ModifyStarCost</c>; the caller clamps at zero. <paramref name="pile"/> is the
    /// card's current pile, which VoidFormPower/BrilliantScarf read.
    /// </summary>
    public static int StarCostWithModifiers(in FlashBattle battle, FlashRules rules, in FlashCard card,
        FlashPile pile)
    {
        if ((card.Flags & FlashCardFlags.StarCostsX) != 0)
            return battle.Stars;
        int cost = card.HasTemporaryStarCost ? card.TemporaryStarCost : card.BaseStarCost;
        cost = (int)FlashHooks.ModifyStarCost(battle, rules, pile, card, cost);
        return Math.Max(0, cost);
    }

    /// <summary>
    /// Mirrors <c>CardModel.ResolveStarXValue</c>: the star value captured at spend time
    /// (<c>LastStarsSpent</c>) with <c>Hook.ModifyXValue</c> applied. Every effect reads it at the
    /// point the native code resolves the X value.
    /// </summary>
    public static int ResolveStarXValue(ref FlashExec x)
    {
        if ((x.Card.Flags & FlashCardFlags.StarCostsX) == 0)
            throw new FlashUnsupportedException("这张卡没有 X 星能费用，不能解析星能 X 值。");
        return FlashHooks.ModifyXValue(x.B, x.R, x.Card, x.StarValue);
    }
}
