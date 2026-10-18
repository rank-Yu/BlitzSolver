namespace CombatSolver.Engine.InCombat.Flash;

public static partial class FlashKernel
{
    /// <summary>
    /// Mirrors the <c>UsePotion</c> branch of <c>CombatBeamSolver.Replay</c> with
    /// <c>PotionExecutionSupport.Prepare</c> and <c>Complete</c>: consume the slot, <c>BeforePotionUsed</c>,
    /// the potion's effect, <c>AfterPotionUsed</c> while the player lives, the enemy-death pass, then
    /// shuffle accounting, the win check and a requested turn end.
    /// </summary>
    private static void UsePotionAction(ref FlashExec x, in FlashAction action)
    {
        if (action.PotionSlot >= x.B.PotionSlotCount)
            throw new ArgumentOutOfRangeException(nameof(action), action.PotionSlot, "药水槽位越界。");
        ushort stored = x.B.Potions[action.PotionSlot];
        if (stored == 0)
            throw new InvalidOperationException($"药水槽位 {action.PotionSlot} 为空。");
        FlashPotionDef def = FlashRegistry.Potion((ushort)(stored - 1));
        int shuffleEvents = x.B.ShuffleEventCount;

        // Prepare: ConsumePotion (the slot empties; the use record only feeds search scoring), then
        // BeforePotionUsed, which has no supported listeners yet.
        x.B.Potions[action.PotionSlot] = 0;
        x.Target = action.Target;
        x.Card = default;
        x.Stats = null;
        x.EffectDepth++;
        def.OnUse(ref x);
        x.EffectDepth--;
        if (x.NeedsChoice)
            return;
        // Complete: potion choices are not supported yet; AfterPotionUsed has no supported listeners;
        // power amount resolution and history compensation have no supported sources.
        ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        // SettleReplayActionBoundary: no supported power reactions or normalizations.
        if (x.B.ShuffleEventCount != shuffleEvents)
            x.B.ShufflesCrossed++;
        CheckWinCondition(ref x.B);
        if ((x.B.Flags & FlashBattleFlags.PlayerTurnEndRequested) != 0)
        {
            x.B.Flags &= ~FlashBattleFlags.PlayerTurnEndRequested;
            if (IsInProgress(x.B))
                AdvanceRound(ref x);
            x.B.Flags &= ~FlashBattleFlags.PlayerTurnEndRequested;
        }
    }

    /// <summary>A variable of the potion being used, from the rule table.</summary>
    public static decimal PotionVar(in FlashExec x, FlashPotionDef def, int index) => x.R.Potion(def.Id).Vars[index];

    public static FlashValueProps PotionVarProps(in FlashExec x, FlashPotionDef def, int index) => x.R.Potion(def.Id).VarProps[index];
}
