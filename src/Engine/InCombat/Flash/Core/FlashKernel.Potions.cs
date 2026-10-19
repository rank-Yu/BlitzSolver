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
        // Complete: potion choices are not supported yet. AfterPotionUsed runs while the player lives,
        // before the power-amount resolution and the enemy-death pass (PotionExecutionSupport.Complete).
        if (x.B.Player.IsAlive)
        {
            FlashHooks.AfterPotionUsed(ref x, def.Id);
            if (x.NeedsChoice)
                return;
        }
        ResolvePowerAmountChanges(ref x);
        if (x.NeedsChoice)
            return;
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

    /// <summary>
    /// Mirrors <c>PotionFactory.CreateRandomPotionInCombat</c>: one CombatPotionGeneration
    /// <c>NextFloat</c> for the rarity (Rare <= 0.1, Uncommon <= 0.35, else Common), then one
    /// <c>NextItem</c> draw over the candidates of that rarity in frozen pool order.
    /// </summary>
    public static ushort GenerateRandomPotion(ref FlashBattle battle, FlashRules rules)
    {
        IReadOnlyList<ushort> pool = rules.PotionPool;
        if (pool.Count == 0)
            throw new FlashUnsupportedException("随机药水池为空。");
        ref FlashRngState rng = ref battle.RngStream(FlashRngStream.CombatPotionGeneration);
        float roll = rng.NextFloat(1f);
        FlashPotionRarity rarity = roll <= 0.1f
            ? FlashPotionRarity.Rare
            : roll <= 0.35f ? FlashPotionRarity.Uncommon : FlashPotionRarity.Common;
        Span<ushort> candidates = stackalloc ushort[pool.Count];
        int count = 0;
        for (int index = 0; index < pool.Count; index++)
        {
            if (rules.Potion(pool[index]).Rarity == rarity)
                candidates[count++] = pool[index];
        }
        if (count == 0)
            throw new FlashUnsupportedException($"随机药水没有稀有度 {rarity} 的候选。");
        return candidates[rng.NextInt(0, count)];
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.TryProcurePotion</c> for a supported root: the first empty slot
    /// receives the potion. <c>ShouldProcurePotion</c> (Sozu) and <c>AfterPotionProcured</c> relic
    /// reactions have no Flash definitions, so roots holding them are rejected at admission.
    /// </summary>
    public static bool TryProcurePotion(ref FlashBattle battle, ushort potionId)
    {
        for (int slot = 0; slot < battle.PotionSlotCount; slot++)
        {
            if (battle.Potions[slot] != 0)
                continue;
            battle.Potions[slot] = (ushort)(potionId + 1);
            return true;
        }
        return false;
    }
}
