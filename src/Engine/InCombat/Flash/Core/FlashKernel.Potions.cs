namespace CombatSolver.Engine.InCombat.Flash;

public static partial class FlashKernel
{
    /// <summary>
    /// Mirrors the <c>UsePotion</c> branch of <c>CombatBeamSolver.Replay</c> with
    /// <c>PotionExecutionSupport.Prepare</c> and <c>Complete</c>: consume the slot, <c>BeforePotionUsed</c>,
    /// the potion's effect, <c>AfterPotionUsed</c> while the player lives, the enemy-death pass, then
    /// shuffle accounting, the win check and a requested turn end.
    /// </summary>
    /// <summary>
    /// <c>PowerLifecycleSupport.UpdateSurroundedForTarget</c> for a potion's target: the player's
    /// SurroundedPower faces the back-attack marker of the targeted enemy.
    /// </summary>
    private static void UpdateSurroundedForPotionTarget(ref FlashExec x, byte target)
    {
        if (!FlashRegistry.TryPower("SURROUNDED_POWER", out ushort surrounded))
            return;
        bool left = FlashRegistry.TryPower("BACK_ATTACK_LEFT_POWER", out ushort backLeft)
            && GetAmount(x.B, target, backLeft) > 0;
        bool right = FlashRegistry.TryPower("BACK_ATTACK_RIGHT_POWER", out ushort backRight)
            && GetAmount(x.B, target, backRight) > 0;
        if (!left && !right)
            return;
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref FlashPower power = ref x.B.Powers[index];
            if (power.Id != surrounded || power.Owner != FlashLimits.PlayerOwner || power.Amount <= 0)
                continue;
            power.Aux0 = left ? 1 : 0;
        }
    }

    private static void UsePotionAction(ref FlashExec x, in FlashAction action)
    {
        if (action.PotionSlot >= x.B.PotionSlotCount)
            throw new ArgumentOutOfRangeException(nameof(action), action.PotionSlot, "药水槽位越界。");
        ushort stored = x.B.Potions[action.PotionSlot];
        if (stored == 0)
            throw new InvalidOperationException($"药水槽位 {action.PotionSlot} 为空。");
        FlashPotionDef def = FlashRegistry.Potion((ushort)(stored - 1));
        int shuffleEvents = x.B.ShuffleEventCount;
        // SimulatedCombatState.ConsumePotion: a freeFromEntropic slot records a zero strategic cost and
        // clears its free flag. The root-slot diff cannot see generated potions, so the scorer adds the
        // generated-use counter; this explicit use is one of them.
        int freeMask = x.B.Counter(FlashCounter.EntropicFreePotionMask);
        int slotBit = 1 << action.PotionSlot;
        bool freeFromEntropic = (freeMask & slotBit) != 0;
        if (freeFromEntropic)
            x.B.SetCounter(FlashCounter.EntropicFreePotionMask, freeMask & ~slotBit);
        // SimulatedCombatState.PotionUses records every use; a potion that is not the root potion of
        // this slot was generated in combat and its use is invisible to the root-slot diff.
        if (stored != x.R.RootPotion(action.PotionSlot))
        {
            x.B.AddCounter(FlashCounter.GeneratedPotionUseCount, 1);
            RecordGeneratedPotionStrategicCost(ref x.B,
                freeFromEntropic ? 0 : x.R.Potion(def.Id).StrategicCost, automatic: false);
        }

        // Prepare: ConsumePotion (the slot empties; the use record only feeds search scoring), then
        // BeforePotionUsed, which has no supported listeners yet.
        x.B.Potions[action.PotionSlot] = 0;
        x.Target = action.Target;
        x.Card = default;
        x.Stats = null;
        // SimulatedCombatState.BeforePotionUsed: SurroundedPower's facing follows the potion's target
        // before the potion effect runs.
        UpdateSurroundedForPotionTarget(ref x,
            action.Target != FlashLimits.NoOwner ? action.Target : FlashLimits.PlayerOwner);
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

    /// <summary>
    /// Accumulates the packed strategic cost of generated potion uses
    /// (<see cref="FlashCounter.GeneratedPotionStrategicCost"/>): low 16 bits explicit, high 16 bits
    /// automatic. Free-from-Entropic uses pass zero.
    /// </summary>
    public static void RecordGeneratedPotionStrategicCost(ref FlashBattle battle, int cost, bool automatic)
    {
        if (cost < 0 || cost > 0xFFFF)
            throw new FlashCapacityException("GeneratedPotionStrategicCost", 0xFFFF);
        int packed = battle.Counter(FlashCounter.GeneratedPotionStrategicCost);
        int total = automatic ? (int)((uint)packed >> 16) : packed & 0xFFFF;
        if (total > 0xFFFF - cost)
            throw new FlashCapacityException("GeneratedPotionStrategicCost", 0xFFFF);
        total += cost;
        battle.SetCounter(FlashCounter.GeneratedPotionStrategicCost,
            automatic ? (total << 16) | (packed & 0xFFFF) : (packed & unchecked((int)0xFFFF0000)) | total);
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
        => GenerateRandomPotionFromPool(ref battle, rules, rules.PotionPool);

    /// <summary>
    /// Mirrors <c>PotionFactory.CreateRandomPotionOutOfCombat</c> (EntropicBrew): the same rarity and
    /// <c>NextItem</c> draws as <see cref="GenerateRandomPotion"/>, but over the unfiltered character +
    /// shared pool frozen at admission (potions that cannot be generated in combat are included).
    /// </summary>
    public static ushort GenerateRandomPotionOutOfCombat(ref FlashBattle battle, FlashRules rules)
        => GenerateRandomPotionFromPool(ref battle, rules, rules.OutOfCombatPotionPool);

    private static ushort GenerateRandomPotionFromPool(ref FlashBattle battle, FlashRules rules,
        IReadOnlyList<ushort> pool)
    {
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
        => TryProcurePotion(ref battle, potionId, freeFromEntropic: false);

    /// <summary>
    /// <see cref="TryProcurePotion(ref FlashBattle, ushort)"/> with the native <c>freeFromEntropic</c>
    /// flag: the slot is marked in <see cref="FlashCounter.EntropicFreePotionMask"/> so a later use
    /// records zero strategic cost.
    /// </summary>
    public static bool TryProcurePotion(ref FlashBattle battle, ushort potionId, bool freeFromEntropic)
    {
        for (int slot = 0; slot < battle.PotionSlotCount; slot++)
        {
            if (battle.Potions[slot] != 0)
                continue;
            battle.Potions[slot] = (ushort)(potionId + 1);
            if (freeFromEntropic)
                battle.SetCounter(FlashCounter.EntropicFreePotionMask,
                    battle.Counter(FlashCounter.EntropicFreePotionMask) | (1 << slot));
            return true;
        }
        return false;
    }
}
