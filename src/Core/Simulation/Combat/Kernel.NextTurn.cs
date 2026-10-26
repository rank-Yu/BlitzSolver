namespace BlitzSolver.Simulation.Combat;

public static partial class Kernel
{
    private static readonly Lazy<ushort> DrawCardsNextTurnId = new(() => PowerIdOrMax("DRAW_CARDS_NEXT_TURN_POWER"));
    private static readonly Lazy<ushort> EnergyNextTurnId = new(() => PowerIdOrMax("ENERGY_NEXT_TURN_POWER"));

    /// <summary>
    /// <c>SimulatedCombatState.ConsumeEnergyNextTurn</c>: the player's EnergyNextTurn amount is added
    /// to this turn's energy and cleared. The existing engine consumes it directly in the turn-start
    /// path instead of running the power's AfterEnergyReset.
    /// </summary>
    internal static int ConsumeEnergyNextTurn(ref Battle battle)
    {
        ushort id = EnergyNextTurnId.Value;
        if (id == ushort.MaxValue)
            return 0;
        int amount = GetAmount(battle, Limits.PlayerOwner, id);
        if (amount > 0)
        SetPowerAmount(ref battle, Limits.PlayerOwner, id, 0);
        return amount;
    }

    /// <summary>
    /// The <c>drawNotYetSnapshotted</c> term of
    /// <c>PersistentPowerSupport.ConsumeModifiedHandDraw</c>: a DrawCardsNextTurn power whose
    /// <c>AmountOnTurnStart</c> is still zero was applied after the last turn-start snapshot and is
    /// not seen by its own <c>ModifyHandDraw</c>; the turn-start path adds it directly.
    /// </summary>
    private static int DrawNextTurnNotYetSnapshotted(in Battle battle)
    {
        ushort id = DrawCardsNextTurnId.Value;
        if (id == ushort.MaxValue)
            return 0;
        int index = FindPower(battle, Limits.PlayerOwner, id);
        if (index < 0)
            return 0;
        ref readonly Power power = ref battle.Powers[index];
        if (power.Amount > 0 && power.AmountOnTurnStart == 0)
        {
            return power.Amount;
        }
        return 0;
    }
}
